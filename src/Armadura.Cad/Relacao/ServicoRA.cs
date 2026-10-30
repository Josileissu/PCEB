using System;
using System.Collections.Generic;
using System.Linq;
using Armadura.Cad.Barra;
using Armadura.Cad.Infra;
using Armadura.Core.Calculo;
using Armadura.Core.Configuracao;
using Armadura.Core.Numeracao;
using Armadura.Core.Persistencia;
using Armadura.Core.RelacaoAco;
using Armadura.Core.Tabelas;

namespace Armadura.Cad.Relacao;

/// <summary>
/// Numeração e tabelas "Relação do aço" + "Resumo do aço" (TABLE nativa).
/// Detalhamento = bloco do CAD: barras dentro de blocos contam × nº de inserções (também aninhados);
/// uma tabela PARCIAL lista só as barras dos blocos dela (ou do bloco em que ela está).
/// </summary>
internal static class ServicoRA
{
    /// <summary>Uma barra vista por um caminho de inserção (a mesma barra pode aparecer por vários).</summary>
    private sealed class Ocorrencia
    {
        public Polyline Pl;
        public Core.Modelo.DadosArmadura Dados;
        public List<Core.Modelo.GeoSegmento> Geo;
        public double Comprimento;
        public int Multiplicidade;
        /// <summary>Bloco de nível mais alto (o detalhamento); "" = fora de bloco.</summary>
        public string Elemento = "";
        /// <summary>Todos os blocos do caminho (para tabelas parciais de blocos aninhados).</summary>
        public HashSet<string> Cadeia = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Nome "de verdade" do bloco (blocos dinâmicos inserem definições anônimas).</summary>
    public static string NomeBloco(Transaction tr, BlockReference br)
    {
        var id = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
        return ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name;
    }

    private static List<Ocorrencia> Coletar(Database db, Transaction tr, ConfiguracaoArmadura cfg)
    {
        double cpu = Desenho.CmPorUnidade(db, cfg);
        var porChave = new Dictionary<(ObjectId, string, string), Ocorrencia>();
        var leituras = new Dictionary<ObjectId, (Core.Modelo.DadosArmadura d, List<Core.Modelo.GeoSegmento> g, double c)>();

        void Visitar(ObjectId btrId, int mult, string elemento, List<string> cadeia, int profundidade)
        {
            if (profundidade > 30) return; // proteção contra referência circular
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent is Polyline pl)
                {
                    if (!leituras.TryGetValue(id, out var lida))
                    {
                        var d = XDataCad.LerBarra(pl);
                        if (d == null) { leituras[id] = (null, null, 0); continue; }
                        var geo = GeometriaBarra.Ler(pl, cpu);
                        d.AjustarSegmentos(geo.Count);
                        lida = (d, geo, CalculoComprimento.ComprimentoUnitario(geo, cfg, d));
                        leituras[id] = lida;
                    }
                    if (lida.d == null) continue;
                    var chave = (id, elemento ?? "", string.Join("/", cadeia));
                    if (!porChave.TryGetValue(chave, out var oc))
                    {
                        oc = new Ocorrencia
                        {
                            Pl = pl, Dados = lida.d, Geo = lida.g, Comprimento = lida.c, Elemento = elemento ?? "",
                        };
                        oc.Cadeia.UnionWith(cadeia);
                        porChave[chave] = oc;
                    }
                    oc.Multiplicidade += mult;
                }
                else if (ent is BlockReference br && !(ent is Table))
                {
                    var filho = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                    if (filho.IsFromExternalReference || filho.IsDependent || filho.IsLayout) continue;
                    string nome = NomeBloco(tr, br);
                    Visitar(br.BlockTableRecord, mult, elemento ?? nome, new List<string>(cadeia) { nome }, profundidade + 1);
                }
            }
        }

        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId btrId in bt)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            if (btr.IsLayout) Visitar(btrId, 1, null, new List<string>(), 0);
        }
        return porChave.Values.ToList();
    }

    /// <summary>O desenho tem alguma armadura? (barato: para antes de comandos como QSAVE)</summary>
    public static bool TemBarras(Database db, Transaction tr) =>
        Desenho.Entidades<Polyline>(db, tr, incluirBlocos: true).Any(id => XDataCad.TemDados(tr.GetObject(id, OpenMode.ForRead)));

    /// <summary>Renumera todas as barras, atualiza descrições e todas as tabelas. Retorna (barras, posições).</summary>
    /// <param name="travarTudo">no momento do bloqueio: todas as barras ficam com o número atual</param>
    public static (int barras, int posicoes) Atualizar(Database db, Transaction tr, ConfiguracaoArmadura cfg, bool travarTudo = false)
    {
        var ocorrencias = Coletar(db, tr, cfg);
        var barras = ocorrencias.GroupBy(o => o.Pl.ObjectId).Select(g => g.First()).ToList();

        // bloqueio: desliga sozinho se o tipo de numeração ou o número inicial mudaram (seção 4.3)
        var estado = EstadoRA.Ler(db, tr);
        if (estado.Bloqueada && (estado.Tipo != cfg.TipoNumeracao || estado.NumeroInicial != cfg.NumeroInicial))
        {
            estado.Bloqueada = false;
            estado.Gravar(db, tr);
            Log.Info("RA desbloqueada: o tipo de numeração ou o número inicial mudaram.");
        }

        var hashes = new Dictionary<ObjectId, string>();
        // telas soldadas não são numeradas; treliças vêm depois das barras, da menor para a maior altura
        var itens = barras.Where(b => !b.Dados.EhTela).Select(b =>
        {
            string assinatura = Numerador.Assinatura(b.Dados, b.Geo, cfg);
            string hash = Numerador.Hash(b.Elemento + "|" + assinatura);
            hashes[b.Pl.ObjectId] = hash;
            double ordemBitola = b.Dados.EhTrelica ? 1000 + (cfg.Trelicas.Trelica(b.Dados.Trelica)?.Altura ?? 0) : b.Dados.Bitola;
            return new ItemNumeracao
            {
                Chave = b.Pl.ObjectId, Aco = b.Dados.EhTrelica ? "TR" : b.Dados.Aco, Bitola = ordemBitola, Assinatura = assinatura,
                Ordem = b.Pl.Handle.Value, Grupo = b.Elemento,
                Travada = estado.Bloqueada && (travarTudo || b.Dados.AssinaturaBloqueio == hash), NumeroAtual = b.Dados.Numero,
            };
        }).ToList();
        var num = Numerador.Numerar(itens, cfg.TipoNumeracao, cfg.NumeroInicial, estado.Bloqueada, estado.MaiorNumero);
        NumerarTelas(barras.Select(b => b.Pl).Distinct().ToList(), barras.ToDictionary(b => b.Pl.ObjectId, b => b.Geo));

        var porBarra = ocorrencias.ToLookup(o => o.Pl.ObjectId); // a mesma barra vista por vários caminhos
        foreach (var b in barras)
        {
            var id = b.Pl.ObjectId;
            int n = num.Numeros.TryGetValue(id, out var nn) ? nn : 0; // tela: sem número
            bool nova = estado.Bloqueada && (b.Dados.Nova || num.Novas.Contains(id));
            string lck = estado.Bloqueada && hashes.TryGetValue(id, out var hh) ? hh : "";
            if (b.Dados.Numero != n || b.Dados.Nova != nova || b.Dados.AssinaturaBloqueio != lck)
            {
                var d = XDataCad.LerBarra(b.Pl); // relê: Coletar ajustou segmentos só em memória
                d.Numero = n;
                d.Nova = nova;
                d.AssinaturaBloqueio = lck;
                b.Pl.UpgradeOpen();
                XDataCad.Gravar(b.Pl, d);
            }
            foreach (var o in porBarra[id]) { o.Dados.Numero = n; o.Dados.Nova = nova; }
            ServicoBarra.Atualizar(tr, b.Pl, cfg);
        }
        if (estado.Bloqueada && num.MaiorNumero > estado.MaiorNumero)
        {
            estado.MaiorNumero = num.MaiorNumero;
            estado.Gravar(db, tr);
        }

        // tabelas (também as que estão dentro de blocos)
        bool porElemento = cfg.TipoNumeracao != TipoNumeracao.Global;
        int posicoes = 0;
        foreach (var tid in Desenho.Entidades<Table>(db, tr, incluirBlocos: true).ToList())
        {
            var t = (Table)tr.GetObject(tid, OpenMode.ForRead);
            var op = Opcoes(t, cfg);
            if (op == null) continue;
            var escopo = Escopo(tr, t, op);
            var lista = escopo.Count == 0 ? ocorrencias : ocorrencias.Where(o => o.Cadeia.Overlaps(escopo)).ToList();
            // coluna ELEMENTO pela opção da tabela; com ela, as linhas se separam por detalhamento
            bool comElemento = op.ComElemento(cfg.TipoNumeracao);
            var res = Calcular(lista, cfg, porElemento || comElemento, db);
            Preencher(tr, t, res, op, cfg, db, comElemento, escopo);
            if (escopo.Count == 0) posicoes = res.Linhas.Count;
        }
        if (posicoes == 0) posicoes = Calcular(ocorrencias, cfg, porElemento, db).Linhas.Count;
        return (barras.Count, posicoes);
    }

    /// <summary>
    /// Telas identificadas: T1, T2… na ordem em que foram desenhadas; painéis iguais (modelo, lados e
    /// quantidade) ficam com o mesmo T. Numeração do desenho inteiro.
    /// </summary>
    private static void NumerarTelas(List<Polyline> barras, Dictionary<ObjectId, List<Core.Modelo.GeoSegmento>> geos)
    {
        var telas = barras.Select(pl => (pl, d: XDataCad.LerBarra(pl)))
            .Where(x => x.d != null && x.d.EhTela && x.d.TelaIdentificada && geos[x.pl.ObjectId].Count >= 2)
            .OrderBy(x => x.pl.Handle.Value).ToList();
        var porAssinatura = new Dictionary<string, int>();
        foreach (var (pl, d) in telas)
        {
            var g = geos[pl.ObjectId];
            string a = Core.Descricao.FormatadorDescricao.AssinaturaTela(d, g[0].Comprimento, g[1].Comprimento);
            if (!porAssinatura.TryGetValue(a, out int t)) porAssinatura[a] = t = porAssinatura.Count + 1;
            if (d.NumeroTela == t) continue;
            d.NumeroTela = t;
            if (!pl.IsWriteEnabled) pl.UpgradeOpen();
            XDataCad.Gravar(pl, d);
        }
    }

    /// <summary>Relação do desenho inteiro (para exportar a lista de corte e dobra).</summary>
    public static ResultadoRA ResultadoDoDesenho(Database db, Transaction tr, ConfiguracaoArmadura cfg) =>
        Calcular(Coletar(db, tr, cfg), cfg, cfg.TipoNumeracao != TipoNumeracao.Global, db);

    /// <summary>Blocos que a tabela lista: os dela (RA parcial) + o bloco em que ela está desenhada.</summary>
    public static HashSet<string> Escopo(Transaction tr, Table t, OpcoesTabelaRA op)
    {
        var r = new HashSet<string>(op.Blocos, StringComparer.OrdinalIgnoreCase);
        var dono = (BlockTableRecord)tr.GetObject(t.OwnerId, OpenMode.ForRead);
        if (!dono.IsLayout) r.Add(dono.Name);
        return r;
    }

    private static ResultadoRA Calcular(List<Ocorrencia> lista, ConfiguracaoArmadura cfg, bool porElemento, Database db)
    {
        double cpu = Desenho.CmPorUnidade(db, cfg);
        var validas = lista.Where(o => o.Multiplicidade > 0).ToList();
        var itens = validas.Where(o => !o.Dados.EhTela).Select(o => new ItemRA
        {
            Numero = o.Dados.Numero,
            Aco = o.Dados.Aco,
            Bitola = o.Dados.Bitola,
            Quantidade = o.Dados.QuantidadeTotal(o.Multiplicidade),
            ComprimentoUnitario = o.Comprimento,
            Var = o.Dados.TemVar,
            ComprimentosVar = o.Dados.EhVarLista ? CalculoComprimento.ComprimentosReaisVar(o.Geo, cfg, o.Dados) : null,
            Corrido = o.Dados.Corrido,
            Elemento = o.Elemento,
            Nova = o.Dados.Nova,
            Forma = o.Geo,
            Fechada = o.Pl.Closed,
            Trelica = o.Dados.EhTrelica ? o.Dados.Trelica.Trim() : "",
            MassaLinear = o.Dados.EhTrelica ? cfg.Trelicas.Trelica(o.Dados.Trelica)?.PesoMetro ?? 0 : 0,
        });
        // telas: área do painel (m²) × quantidade, peso pelo catálogo (kg/m²)
        var telas = validas.Where(o => o.Dados.EhTela).Select(o =>
        {
            var m = cfg.Telas.Tela(o.Dados.Tela);
            return new ItemTela
            {
                Modelo = o.Dados.Tela.Trim(), Aco = m?.Aco ?? "CA-60",
                AreaM2 = Math.Round(Math.Abs(o.Pl.Area) * cpu * cpu / 10000.0, 4),
                Quantidade = o.Dados.QuantidadeTotal(o.Multiplicidade), MassaM2 = m?.PesoM2 ?? 0,
            };
        });
        return RelacaoDoAco.Calcular(itens, cfg, porElemento, telas);
    }

    /// <summary>Opções da tabela (null se não for uma tabela do plugin).</summary>
    public static OpcoesTabelaRA Opcoes(Entity t, ConfiguracaoArmadura cfg)
    {
        if (!(t is Table)) return null;
        var c = XDataCad.Ler(t);
        return c == null ? null : CodecDados.DecodificarTabela(c, OpcoesTabelaRA.Padrao(cfg));
    }

    public static void GravarOpcoes(Table t, OpcoesTabelaRA op)
    {
        if (!t.IsWriteEnabled) t.UpgradeOpen();
        XDataCad.Gravar(t, op);
    }

    /// <summary>Cria a tabela no ponto (canto superior esquerdo) e a preenche.</summary>
    /// <param name="atualizar">false: só cria (quem chama atualiza tudo uma vez no fim, ex.: importação)</param>
    public static ObjectId Inserir(Database db, Transaction tr, Point3d ponto, ConfiguracaoArmadura cfg, OpcoesTabelaRA op = null, bool atualizar = true)
    {
        XDataCad.GarantirRegApp(db, tr);
        var t = new Table
        {
            Position = ponto,
            LayerId = Desenho.GarantirLayer(db, tr, cfg.LayerTabela, cfg.CorTabela),
        };
        if (db.Tablestyle.IsValid) t.TableStyle = db.Tablestyle;
        var esp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
        esp.AppendEntity(t);
        tr.AddNewlyCreatedDBObject(t, true);
        GravarOpcoes(t, op ?? OpcoesTabelaRA.Padrao(cfg));
        if (atualizar) Atualizar(db, tr, cfg); // numera e preenche esta e as demais tabelas
        return t.ObjectId;
    }

    /// <summary>
    /// ARMRAASSOCIAR: os blocos passam a pertencer só a esta tabela (um detalhamento fica em uma única
    /// RA parcial). Retorna quantas outras tabelas perderam blocos.
    /// </summary>
    public static int Associar(Database db, Transaction tr, ObjectId tabela, IEnumerable<string> blocos, ConfiguracaoArmadura cfg)
    {
        var nomes = new HashSet<string>(blocos, StringComparer.OrdinalIgnoreCase);
        int outras = 0;
        foreach (var tid in Desenho.Entidades<Table>(db, tr, incluirBlocos: true).ToList())
        {
            var t = (Table)tr.GetObject(tid, OpenMode.ForRead);
            var op = Opcoes(t, cfg);
            if (op == null) continue;
            if (tid == tabela)
            {
                foreach (var n in nomes) if (!op.Blocos.Contains(n, StringComparer.OrdinalIgnoreCase)) op.Blocos.Add(n);
                GravarOpcoes(t, op);
            }
            else if (op.Blocos.RemoveAll(nomes.Contains) > 0)
            {
                GravarOpcoes(t, op);
                outras++;
            }
        }
        Atualizar(db, tr, cfg);
        return outras;
    }

    /// <summary>ARMRABLOQUEAR: liga (trava os números atuais) ou desliga (renumera tudo e limpa os destaques).</summary>
    public static bool AlternarBloqueio(Database db, Transaction tr, ConfiguracaoArmadura cfg)
    {
        var estado = EstadoRA.Ler(db, tr);
        if (!estado.Bloqueada)
        {
            Atualizar(db, tr, cfg); // números finais antes de travar
            estado.Bloqueada = true;
            estado.Tipo = cfg.TipoNumeracao;
            estado.NumeroInicial = cfg.NumeroInicial;
            estado.MaiorNumero = 0;
            estado.Gravar(db, tr);
            Atualizar(db, tr, cfg, travarTudo: true); // grava o hash de cada barra (travadas) e o maior número
        }
        else
        {
            estado.Bloqueada = false;
            estado.Gravar(db, tr);
            Atualizar(db, tr, cfg); // limpa hashes e destaques e renumera tudo
        }
        return estado.Bloqueada;
    }

    private static void Preencher(Transaction tr, Table t, ResultadoRA r, OpcoesTabelaRA op, ConfiguracaoArmadura cfg, Database db,
        bool comElemento, IEnumerable<string> detalhamentos)
    {
        t.UpgradeOpen();
        double h = cfg.AlturaTexto(Desenho.CmPorUnidade(db, cfg), op.Escala);
        var conteudo = MontadorTabela.Montar(r, op, cfg, comElemento, detalhamentos);
        int cols = conteudo.Colunas;

        // Recriar do zero é mais seguro que reaproveitar células mescladas de um tamanho anterior.
        try
        {
            if (t.Rows.Count > 0 && t.Columns.Count > 0)
                t.UnmergeCells(CellRange.Create(t, 0, 0, t.Rows.Count - 1, t.Columns.Count - 1));
        }
        catch { /* sem mesclas */ }
        int n = Math.Max(1, conteudo.Linhas.Count);
        t.SetSize(n, cols);
        int colsBase = conteudo.ColunaDetalhe >= 0 ? cols - 1 : cols;
        double[] larg = colsBase == 7 ? new double[] { 9, 8, 7, 8, 6, 8, 9 } : new double[] { 8, 7, 8, 6, 8, 9 };
        for (int c = 0; c < colsBase; c++) t.SetColumnWidth(c, larg[c] * h);
        if (conteudo.ColunaDetalhe >= 0) t.SetColumnWidth(conteudo.ColunaDetalhe, 34 * h);
        // mescla ANTES de escrever: no GstarCAD, mesclar depois zera a altura de texto da linha anterior
        foreach (var (l, c1, c2) in conteudo.Mesclas)
            t.MergeCells(CellRange.Create(t, l, c1, l, c2));
        for (int i = 0; i < n; i++)
        {
            // linhas com detalhe de dobra ficam mais altas para caber a forma; o cabeçalho da relação
            // comporta 2 linhas de texto (assim o CAD não cresce a linha depois e as posições batem)
            bool cabecalho = i + 1 < conteudo.Linhas.Count && conteudo.LinhasComForma.ContainsKey(i + 1) && !conteudo.LinhasComForma.ContainsKey(i);
            t.SetRowHeight(i, conteudo.LinhasComForma.ContainsKey(i) ? h * 6.0 : cabecalho ? h * 4.0 : h * 2.0);
            for (int c = 0; c < cols; c++)
            {
                t.SetTextHeight(i, c, h);
                t.SetAlignment(i, c, CellAlignment.MiddleCenter);
                string txt = i < conteudo.Linhas.Count ? conteudo.Linhas[i][c] : "";
                // título do detalhe em 2 linhas (\P = quebra de linha do MText da célula)
                if (txt == MontadorTabela.TituloDetalhe) txt = txt.Replace(" (", "\\P(");
                t.SetTextString(i, c, txt);
            }
        }
        t.GenerateLayout();
        try { t.RecomputeTableBlock(true); } catch (Rt.Exception ex) { Log.Erro("RecomputeTableBlock", ex); }
        DesenharDetalhes(tr, t, conteudo, op, cfg, h);
    }

    /// <summary>
    /// Detalhe de dobra: a forma de cada posição desenhada na última coluna (polilinha + medidas),
    /// como partes ligadas à tabela (somem e voltam com ela). Sem a coluna, só apaga as antigas.
    /// </summary>
    private static void DesenharDetalhes(Transaction tr, Table t, ConteudoTabela conteudo, OpcoesTabelaRA op,
        ConfiguracaoArmadura cfg, double h)
    {
        var db = t.Database;
        string hTabela = XDataCad.Hex(t.ObjectId);
        foreach (var p in op.Partes)
        {
            var e = ParteDaTabela(tr, db, p, hTabela);
            if (e != null) { if (!e.IsWriteEnabled) e.UpgradeOpen(); e.Erase(); }
        }
        op.Partes.Clear();
        op.PosX = t.Position.X;
        op.PosY = t.Position.Y;
        int col = conteudo.ColunaDetalhe;
        if (col >= 0 && conteudo.LinhasComForma.Count > 0)
        {
            XDataCad.GarantirRegApp(db, tr);
            var owner = (BlockTableRecord)tr.GetObject(t.OwnerId, OpenMode.ForWrite);
            double hs = 0.8 * h;
            void Adicionar(Entity e)
            {
                e.LayerId = t.LayerId;
                owner.AppendEntity(e);
                tr.AddNewlyCreatedDBObject(e, true);
                XDataCad.Gravar(e, new Vinculo { Tipo = TipoParte.Desenho, HandleBarra = hTabela });
                op.Partes.Add(XDataCad.Hex(e.ObjectId));
            }
            for (int r = 0; r < conteudo.Linhas.Count; r++)
            {
                if (conteudo.LinhasComForma.TryGetValue(r, out var linha) && linha.Forma != null && linha.Forma.Count > 0)
                {
                    // posição real da célula (o GstarCAD pode crescer linhas com texto quebrado: não somar alturas)
                    double cx, cy, larg, alt;
                    using (var cantos = new Point3dCollection())
                    {
                        t.GetCellExtents(r, col, true, cantos);
                        var xs = cantos.Cast<Point3d>().Select(p => p.X).ToList();
                        var ys = cantos.Cast<Point3d>().Select(p => p.Y).ToList();
                        cx = (xs.Min() + xs.Max()) / 2; cy = (ys.Min() + ys.Max()) / 2;
                        larg = xs.Max() - xs.Min(); alt = ys.Max() - ys.Min();
                    }
                    var esq = EsquemaDobra.Gerar(linha.Forma, linha.Fechada, larg, alt, hs,
                        (i, l) => CalculoComprimento.TextoCota(l, false, cfg));
                    var pl = new Polyline();
                    pl.SetDatabaseDefaults(db);
                    for (int i = 0; i < esq.Vertices.Count; i++)
                        pl.AddVertexAt(i, new Point2d(cx + esq.Vertices[i].X, cy + esq.Vertices[i].Y), esq.Vertices[i].Bulge, 0, 0);
                    pl.Closed = esq.Fechada;
                    pl.Elevation = t.Position.Z;
                    Adicionar(pl);
                    foreach (var ro in esq.Rotulos)
                    {
                        var p = new Point3d(cx + ro.X, cy + ro.Y, t.Position.Z);
                        var tx = new DBText
                        {
                            TextString = ro.Texto, Height = hs, Rotation = ro.Rotacao,
                            HorizontalMode = TextHorizontalMode.TextCenter, VerticalMode = TextVerticalMode.TextVerticalMid,
                            Position = p, AlignmentPoint = p,
                        };
                        Adicionar(tx);
                        if (db.Textstyle.IsValid) tx.TextStyleId = db.Textstyle;
                        tx.AdjustAlignment(db);
                    }
                }
            }
        }
        GravarOpcoes(t, op);
    }

    /// <summary>Parte do detalhe de dobra que pertence à tabela (vínculo recíproco pelo dono).</summary>
    private static Entity ParteDaTabela(Transaction tr, Database db, string handle, string hTabela)
    {
        var id = XDataCad.IdDoHandle(db, handle);
        if (id.IsNull || id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity e)) return null;
        var v = XDataCad.LerVinculo(e);
        return v != null && v.Tipo == TipoParte.Desenho && string.Equals(v.HandleBarra, hTabela, StringComparison.OrdinalIgnoreCase) ? e : null;
    }

    /// <summary>A parte é de uma tabela que a lista? (para o sincronizador não apagá-la como órfã)</summary>
    public static bool ParteDeTabelaValida(Transaction tr, Entity parte)
    {
        var v = XDataCad.LerVinculo(parte);
        if (v == null || v.Tipo != TipoParte.Desenho) return false;
        var id = XDataCad.IdDoHandle(parte.Database, v.HandleBarra);
        if (id.IsNull || id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Table t)) return false;
        var op = Opcoes(t, ConfigDesenho.Obter(t.Database));
        string h = XDataCad.Hex(parte.ObjectId);
        return op != null && op.Partes.Any(x => string.Equals(x, h, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A tabela foi movida: os detalhes vão junto (sem recalcular a relação).</summary>
    public static void AcompanharMovimento(Transaction tr, Table t, ConfiguracaoArmadura cfg)
    {
        var op = Opcoes(t, cfg);
        if (op == null || op.Partes.Count == 0) return;
        var delta = new Vector3d(t.Position.X - op.PosX, t.Position.Y - op.PosY, 0);
        if (delta.Length < 1e-9) return;
        string hTabela = XDataCad.Hex(t.ObjectId);
        var m = Matrix3d.Displacement(delta);
        foreach (var p in op.Partes)
        {
            var e = ParteDaTabela(tr, t.Database, p, hTabela);
            if (e != null) { if (!e.IsWriteEnabled) e.UpgradeOpen(); e.TransformBy(m); }
        }
        op.PosX = t.Position.X;
        op.PosY = t.Position.Y;
        GravarOpcoes(t, op);
    }

    /// <summary>Tabela apagada: apaga os detalhes de dobra dela.</summary>
    public static void ApagarDetalhes(Transaction tr, Table t, ConfiguracaoArmadura cfg)
    {
        var op = Opcoes(t, cfg);
        if (op == null) return;
        string hTabela = XDataCad.Hex(t.ObjectId);
        foreach (var p in op.Partes)
        {
            var e = ParteDaTabela(tr, t.Database, p, hTabela);
            if (e != null && !e.IsErased) { e.UpgradeOpen(); e.Erase(); }
        }
    }
}
