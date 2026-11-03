using System;
using System.Collections.Generic;
using System.Linq;
using Armadura.Cad.Barra;
using Armadura.Cad.Infra;
using Armadura.Cad.Relacao;
using Armadura.Core.Configuracao;
using Armadura.Core.Importacao;
using Armadura.Core.Modelo;
using Armadura.Core.Persistencia;
using Armadura.Core.RelacaoAco;

[assembly: Rt.CommandClass(typeof(Armadura.Cad.ComandosImportacao))]

namespace Armadura.Cad;

/// <summary>
/// ARMIMPORTARCAD: lê um .cad do QiArmaduras (AltoQi Builder) e desenha tudo como armaduras do plugin
/// (barras, estribos, faixas, indicações, rebatidas, detalhamentos = blocos, tabelas da RA) e o
/// desenho de forma como entidades comuns. O arquivo .cad nunca é alterado.
/// </summary>
public sealed class ComandosImportacao
{
    [Rt.CommandMethod("ARMIMPORTARCAD")]
    public void Importar() => Comandos.Executar("ARMIMPORTARCAD", (doc, ed, db, cfg) =>
    {
        string caminho = PedirArquivo(ed);
        if (string.IsNullOrEmpty(caminho)) return;
        var rp = ed.GetPoint(new PromptPointOptions("\nPonto de inserção (Enter = mesmas coordenadas do .cad):") { AllowNone = true });
        if (rp.Status != PromptStatus.OK && rp.Status != PromptStatus.None) return;
        var origem = rp.Status == PromptStatus.OK ? rp.Value.TransformBy(ed.CurrentUserCoordinateSystem) : Point3d.Origin;

        ResultadoImportacao r;
        try { r = ConversorQiCad.Converter(LeitorQiCad.Ler(caminho)); }
        catch (Exception ex)
        {
            Log.Erro("ARMIMPORTARCAD (leitura)", ex);
            ed.WriteMessage($"\nNão foi possível ler o arquivo: {ex.Message}");
            return;
        }
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        var resumo = Desenhar(doc, db, cfg, r, new Vector2d(origem.X, origem.Y));
        Log.Info($"ARMIMPORTARCAD {System.IO.Path.GetFileName(caminho)}: {resumo} ({relogio.Elapsed.TotalSeconds:0.0} s)");
        ed.WriteMessage("\n" + resumo);
        foreach (var a in r.Avisos) ed.WriteMessage("\nAviso: " + a);
        try { ed.Command("_.ZOOM", "_E"); } catch { /* sem zoom em script */ }
    });

    private static string PedirArquivo(Editor ed) =>
        Arquivos.Pedir(ed, false, "Importar desenho do QiArmaduras", "Desenho do QiArmaduras (*.cad)|*.cad|Todos os arquivos|*.*", ".cad");

    /// <summary>Desenha o resultado no espaço corrente. Retorna o resumo para o usuário.</summary>
    internal static string Desenhar(Document doc, Database db, ConfiguracaoArmadura cfg, ResultadoImportacao r, Vector2d deslocamento)
    {
        double cpu = Desenho.CmPorUnidade(db, cfg);
        Point2d P(PontoImp p) => new Point2d(p.X / cpu + deslocamento.X, p.Y / cpu + deslocamento.Y);
        Point3d P3(PontoImp p) { var q = P(p); return new Point3d(q.X, q.Y, 0); }

        int nBarras = 0, nFaixas = 0, nRebatidas = 0, nOutros = 0, nDets = 0, nTabelas = 0, rebatidasSoltas = 0;
        var porDetalhamento = new Dictionary<string, List<ObjectId>>(StringComparer.OrdinalIgnoreCase);
        void Guardar(string det, ObjectId id) { if (det.Length == 0) return; if (!porDetalhamento.TryGetValue(det, out var l)) porDetalhamento[det] = l = new List<ObjectId>(); l.Add(id); }

        using (doc.LockDocument())
        using (Sincronizador.Suspender(doc))
        using (var tr = new TransacaoSegura(db))
        {
            var esp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var layers = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
            ObjectId Layer(string nome)
            {
                if (string.IsNullOrWhiteSpace(nome)) return db.Clayer;
                nome = Limpar(nome);
                if (!layers.TryGetValue(nome, out var id)) layers[nome] = id = Desenho.GarantirLayer(db, tr, nome, 7);
                return id;
            }
            ObjectId Adicionar(Entity e, ItemImportado it)
            {
                e.SetDatabaseDefaults(db);
                e.LayerId = Layer(it.Layer);
                esp.AppendEntity(e);
                tr.AddNewlyCreatedDBObject(e, true);
                Guardar(it.Detalhamento, e.ObjectId);
                nOutros++;
                return e.ObjectId;
            }
            Polyline Poli(IList<PontoImp> pts, IList<double> bulges, bool fechada)
            {
                var pl = new Polyline();
                for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, P(pts[i]), i < bulges.Count ? bulges[i] : 0, 0, 0);
                pl.Closed = fechada;
                return pl;
            }

            // ---- desenho de forma ----
            foreach (var l in r.Linhas)
            {
                if (l.Pontos.Count == 2 && !l.Fechada && (l.Bulges.Count == 0 || l.Bulges.All(b => b == 0)))
                    Adicionar(new Line(P3(l.Pontos[0]), P3(l.Pontos[1])), l);
                else Adicionar(Poli(l.Pontos, l.Bulges, l.Fechada), l);
            }
            foreach (var c in r.Circulos)
                Adicionar(c.AnguloIni.HasValue
                    ? new Arc(P3(c.Centro), c.Raio / cpu, c.AnguloIni.Value, c.AnguloFim.Value)
                    : (Entity)new Circle(P3(c.Centro), Vector3d.ZAxis, c.Raio / cpu), c);
            foreach (var t in r.Textos) Adicionar(Texto(db, t, P3(t.Posicao), cpu), t);
            foreach (var c in r.Cotas)
            {
                var dim = new RotatedDimension(c.Angulo, P3(c.P1), P3(c.P2), P3(c.Posicao), c.Texto, db.Dimstyle);
                double h = cfg.AlturaTexto(cpu, c.Escala);
                Adicionar(dim, c);
                dim.Dimscale = 1; dim.Dimtxt = h; dim.Dimasz = h; dim.Dimtsz = 0.5 * h; dim.Dimexe = 0.5 * h; dim.Dimexo = 0.5 * h; dim.Dimgap = 0.3 * h;
            }

            // ---- barras (com a rebatida: a barra nasce no lugar da rebatida e o ARMREBATER a leva de volta) ----
            var rebatidaDe = r.Rebatidas.Where(x => x.Barra > 0).GroupBy(x => x.Barra).ToDictionary(g => g.Key, g => g.ToList());
            var barras = new Dictionary<int, ObjectId>();
            foreach (var b in r.Barras)
            {
                var d = b.Dados;
                if (d.Bitola <= 0) d.Bitola = cfg.BitolaCorrente;
                Vector2d? volta = null;
                RebatidaImportada usada = null;
                if (rebatidaDe.TryGetValue(b.Origem, out var rebs))
                {
                    usada = rebs.FirstOrDefault(x => Translacao(b.Pontos, x.Pontos, out _));
                    if (usada != null) { Translacao(b.Pontos, usada.Pontos, out var dv); volta = new Vector2d(-dv.X / cpu, -dv.Y / cpu); }
                }
                var desl = volta.HasValue ? -volta.Value : new Vector2d(0, 0);
                var pl = Poli(b.Pontos, b.Bulges, b.Fechada);
                pl.TransformBy(Matrix3d.Displacement(new Vector3d(desl.X, desl.Y, 0)));
                pl.SetDatabaseDefaults(db);
                esp.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                if (b.Descricao.HasValue)
                {
                    var pd = P(b.Descricao.Value) + desl;
                    var v0 = pl.GetPoint2dAt(0);
                    d.TemPosicaoDescricao = true;
                    d.DescricaoDx = pd.X - v0.X;
                    d.DescricaoDy = pd.Y - v0.Y;
                }
                var repDet = r.Detalhamentos.FirstOrDefault(x => x.Nome == b.Detalhamento)?.Repeticoes ?? 1;
                d.Repeticoes = Math.Max(1, d.Repeticoes) * Math.Max(1, repDet); // repetições do detalhamento no QiArmaduras
                ServicoBarra.Criar(tr, pl, d, cfg);
                if (volta.HasValue)
                {
                    ServicoRebatida.Rebater(tr, pl, volta.Value);
                    ServicoBarra.Atualizar(tr, pl, cfg);
                    nRebatidas++;
                }
                barras[b.Origem] = pl.ObjectId;
                nBarras++;
                foreach (var outra in rebs?.Where(x => x != usada) ?? Enumerable.Empty<RebatidaImportada>())
                {
                    Adicionar(Poli(outra.Pontos, new double[0], false), outra);
                    rebatidasSoltas++;
                }
            }
            foreach (var x in r.Rebatidas.Where(x => !barras.ContainsKey(x.Barra)))
            {
                Adicionar(Poli(x.Pontos, new double[0], false), x);
                rebatidasSoltas++;
            }

            // ---- faixas e indicações ----
            foreach (var f in r.Faixas)
            {
                if (!barras.TryGetValue(f.Barra, out var bid)) continue;
                var fd = f.Dados;
                if (fd.Tipo == TipoFaixa.Indicacao)
                    fd.Referencias = fd.Referencias.Select(p => { var q = P(new PontoImp(p.X, p.Y)); return (q.X, q.Y); }).ToList();
                else
                {
                    var p1 = P(new PontoImp(fd.P1X, fd.P1Y));
                    var p2 = P(new PontoImp(fd.P2X, fd.P2Y));
                    fd.P1X = p1.X; fd.P1Y = p1.Y; fd.P2X = p2.X; fd.P2Y = p2.Y;
                }
                ServicoFaixa.Criar(tr, (Polyline)tr.GetObject(bid, OpenMode.ForWrite), fd, P3(f.Posicao), cfg);
                nFaixas++;
            }

            // partes de cada barra entram no detalhamento junto com ela
            foreach (var b in r.Barras.Where(x => x.Detalhamento.Length > 0 && barras.ContainsKey(x.Origem)))
                foreach (var id in PartesDaBarra(tr, db, (Polyline)tr.GetObject(barras[b.Origem], OpenMode.ForRead)))
                    Guardar(b.Detalhamento, id);

            // ---- detalhamentos (blocos) ----
            foreach (var det in r.Detalhamentos)
            {
                if (!porDetalhamento.TryGetValue(det.Nome, out var ids) || ids.Count == 0) continue;
                string nome = Limpar(det.Nome);
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                while (bt.Has(nome)) nome += "_";
                var dados = new DadosDetalhamento
                {
                    Grupo = ServicoDetalhamento.GrupoSugerido(nome), Classe = det.Classe, Volume = det.Volume, AreaForma = det.AreaForma,
                    Comentarios = det.Repeticoes > 1 ? $"Importado do QiArmaduras com {det.Repeticoes} repetições (aplicadas às barras)." : "Importado do QiArmaduras.",
                };
                ServicoDetalhamento.Criar(db, tr, ids.Distinct().Where(i => !i.IsErased).ToArray(), nome, dados);
                det.Nome = nome;
                nDets++;
            }

            // numeração do QiArmaduras recomeça por detalhamento → o desenho passa a usar Individual
            if (r.NumeracaoPorDetalhamento && cfg.TipoNumeracao == Core.Numeracao.TipoNumeracao.Global)
            {
                var nova = cfg.Clonar();
                nova.TipoNumeracao = Core.Numeracao.TipoNumeracao.Individual;
                ConfigDesenho.Gravar(db, tr, nova);
                cfg = ConfigDesenho.Obter(db);
            }

            // ---- tabelas da RA (recalculadas pelo plugin) ----
            foreach (var t in r.Tabelas)
            {
                var op = OpcoesTabelaRA.Padrao(cfg);
                op.Escala = t.Escala;
                op.Blocos.AddRange(t.Detalhamentos.Select(n => r.Detalhamentos.FirstOrDefault(x => x.Nome == n || Limpar(x.Nome) == Limpar(n))?.Nome ?? n));
                op.NomeNoTitulo = op.Blocos.Count > 0;
                ServicoRA.Inserir(db, tr, P3(t.Posicao), cfg, op, atualizar: false);
                nTabelas++;
            }
            if (nBarras > 0 || nTabelas > 0) ServicoRA.Atualizar(db, tr, cfg); // numera e preenche tudo uma vez
            tr.Commit();
        }
        return $"Importado: {nBarras} armadura(s), {nFaixas} faixa(s)/indicação(ões), {nRebatidas} rebatida(s), {nDets} detalhamento(s), " +
               $"{nTabelas} relação(ões) do aço e {nOutros} entidade(s) de desenho." +
               (rebatidasSoltas > 0 ? $" {rebatidasSoltas} rebatida(s) que não eram cópia deslocada da barra ficaram como polilinha comum." : "") +
               (r.NumeracaoPorDetalhamento ? " A numeração do QiArmaduras recomeça em cada detalhamento: o desenho usa numeração Individual." : "");
    }

    /// <summary>Os pontos de b são os de a deslocados (mesma forma)? Retorna o deslocamento.</summary>
    private static bool Translacao(List<PontoImp> a, List<PontoImp> b, out PontoImp desl)
    {
        desl = new PontoImp();
        if (a.Count != b.Count || a.Count == 0) return false;
        desl = new PontoImp(b[0].X - a[0].X, b[0].Y - a[0].Y);
        for (int i = 1; i < a.Count; i++)
            if (Math.Abs(b[i].X - a[i].X - desl.X) > 1e-3 || Math.Abs(b[i].Y - a[i].Y - desl.Y) > 1e-3) return false;
        return true;
    }

    /// <summary>Descrição, cotas, faixas (e as partes delas), rebatidas e desenho gerado da barra.</summary>
    private static IEnumerable<ObjectId> PartesDaBarra(Transaction tr, Database db, Polyline pl)
    {
        var d = XDataCad.LerBarra(pl);
        if (d == null) yield break;
        IEnumerable<string> handles = new[] { d.HandleDescricao }.Concat(d.Segmentos.Select(s => s.HandleCota))
            .Concat(d.Rebatidas).Concat(d.PartesDesenho);
        foreach (var h in handles)
        {
            var id = XDataCad.IdDoHandle(db, h);
            if (!id.IsNull && !id.IsErased) yield return id;
        }
        foreach (var hf in d.Faixas)
        {
            var fid = XDataCad.IdDoHandle(db, hf);
            if (fid.IsNull || fid.IsErased) continue;
            yield return fid;
            var f = ServicoFaixa.Ler((Entity)tr.GetObject(fid, OpenMode.ForRead));
            foreach (var hp in f?.Partes ?? new List<string>())
            {
                var pid = XDataCad.IdDoHandle(db, hp);
                if (!pid.IsNull && !pid.IsErased) yield return pid;
            }
        }
    }

    private static DBText Texto(Database db, TextoImportado t, Point3d p, double cpu)
    {
        var txt = new DBText { TextString = t.Texto, Height = t.Altura / cpu, Rotation = t.Angulo, Position = p };
        if (db.Textstyle.IsValid) txt.TextStyleId = db.Textstyle;
        var h = t.AlinhamentoH switch { 1 => TextHorizontalMode.TextCenter, 2 => TextHorizontalMode.TextRight, _ => TextHorizontalMode.TextLeft };
        var v = t.AlinhamentoV switch { 1 => TextVerticalMode.TextVerticalMid, 2 => TextVerticalMode.TextTop, _ => TextVerticalMode.TextBase };
        if (h != TextHorizontalMode.TextLeft || v != TextVerticalMode.TextBase)
        {
            txt.HorizontalMode = h;
            txt.VerticalMode = v;
            txt.AlignmentPoint = p;
        }
        return txt;
    }

    /// <summary>Nome válido para layer/bloco.</summary>
    private static string Limpar(string nome)
    {
        var s = new string((nome ?? "").Select(c => "<>/\\\":;?*|,=`".IndexOf(c) >= 0 ? '_' : c).ToArray()).Trim();
        return s.Length == 0 ? "SEM_NOME" : s;
    }
}
