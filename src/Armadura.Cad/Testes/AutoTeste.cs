using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Armadura.Cad.Barra;
using Armadura.Cad.Infra;
using Armadura.Cad.Relacao;
using Armadura.Core.Modelo;
using Armadura.Core.Persistencia;

[assembly: Rt.CommandClass(typeof(Armadura.Cad.Testes.AutoTeste))]

namespace Armadura.Cad.Testes;

/// <summary>
/// Teste de integração dentro do CAD. ARMTESTE cria barras pela API e enfileira comandos REAIS
/// (ERASE, COPY, MOVE, STRETCH, ARMRAATUALIZAR) para exercitar os eventos; ARMTESTE2 confere.
/// Relatório: %AppData%\ArmaduraBR\logs\autoteste.txt.
/// Com a variável de ambiente ARM_AUTOTESTE_SAIDA=<arquivo.dwg>, salva o desenho e fecha o CAD.
/// </summary>
public sealed class AutoTeste
{
    private static readonly Dictionary<string, string> H = new Dictionary<string, string>();
    private static readonly Dictionary<string, object> Antes = new Dictionary<string, object>();
    private static readonly StringBuilder Rel = new StringBuilder();
    private static int _ok, _falhas;

    private static string Arquivo => Path.Combine(Log.PastaLogs, "autoteste.txt");

    private static void Checar(bool cond, string descricao)
    {
        if (cond) _ok++; else _falhas++;
        Rel.AppendLine((cond ? "[OK]    " : "[FALHA] ") + descricao);
    }

    private static void Gravar()
    {
        Directory.CreateDirectory(Log.PastaLogs);
        File.WriteAllText(Arquivo, $"Autoteste {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {_ok} ok, {_falhas} falha(s)\r\n" + Rel, Encoding.UTF8);
    }

    [Rt.CommandMethod("ARMTESTE")]
    public void Fase1() => Comandos.Executar("ARMTESTE", (doc, ed, db, cfg) =>
    {
        Rel.Clear(); _ok = _falhas = 0; H.Clear(); Antes.Clear();
        // modelo em mm de propósito: o plugin desenha em cm independentemente do INSUNITS
        db.Insunits = UnitsValue.Millimeters;

        using (var tr = db.TransactionManager.StartTransaction())
        {
            var esp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            Polyline Nova(string nome, double bitola, int qtd, double espac, params (double x, double y, double b)[] v)
            {
                var pl = new Polyline();
                pl.SetDatabaseDefaults(db);
                for (int i = 0; i < v.Length; i++) pl.AddVertexAt(i, new Point2d(v[i].x, v[i].y), v[i].b, 0, 0);
                esp.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                ServicoBarra.Criar(tr, pl, new DadosArmadura { Bitola = bitola, Quantidade = qtd, Espacamento = espac, Aco = bitola <= 5 ? "CA-60" : "CA-50" }, cfg);
                H[nome] = XDataCad.Hex(pl.ObjectId);
                return pl;
            }

            // A: L 300 + gancho 90° (ø10 → 11.5 cm)
            var a = Nova("A", 10, 4, 0, (0, 0, 0), (300, 0, 0), (300, 50, 0));
            var da = XDataCad.LerBarra(a);
            double g = cfg.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho90, 10, "CA-50", false);
            GeometriaBarra.AlterarComprimento(a, 1, g);
            da.Segmentos[1].Formato = FormatoSegmento.Gancho90;
            da.Segmentos[1].ComprimentoRegistrado = g;
            XDataCad.Gravar(a, da);
            ServicoBarra.Atualizar(tr, a, cfg);
            Checar(Math.Abs(GeometriaBarra.Ler(a, 1)[1].Comprimento - 11.5) < 1e-6, "gancho 90° ø10 CA-50 = 11.5 cm");
            Checar(XDataCad.LerBarra(a).Segmentos[1].Formato == FormatoSegmento.Gancho90, "formato do gancho mantido após atualizar");

            // B: mesma forma de A, espelhada e desenhada ao contrário → deve receber o mesmo N
            var b = Nova("B", 10, 2, 0, (1000, 11.5, 0), (1000, 0, 0), (700, 0, 0));
            var db_ = XDataCad.LerBarra(b);
            db_.Segmentos[0].Formato = FormatoSegmento.Gancho90;
            db_.Segmentos[0].ComprimentoRegistrado = 11.5;
            XDataCad.Gravar(b, db_);
            ServicoBarra.Atualizar(tr, b, cfg);

            // C: reta ø12.5 c/15; D: barra com arco ø8 (CA-50); E: estribo-like ø5 fechado
            Nova("C", 12.5, 2, 15, (0, 100, 0), (500, 100, 0));
            Nova("D", 8, 3, 0, (0, 250, 0), (200, 250, 1), (400, 250, 0));
            var e = Nova("E", 5, 10, 15, (600, 200, 0), (620, 200, 0), (620, 250, 0), (600, 250, 0));
            e.Closed = true;
            ServicoBarra.Atualizar(tr, e, cfg);

            ServicoRA.Inserir(db, tr, new Point3d(0, -60, 0), cfg);
            tr.Commit();
        }

        using (var tr = db.TransactionManager.StartTransaction())
        {
            var dA = Barra(tr, db, "A"); var dB = Barra(tr, db, "B"); var dC = Barra(tr, db, "C");
            var dD = Barra(tr, db, "D"); var dE = Barra(tr, db, "E");
            Checar(dA.Numero > 0 && dA.Numero == dB.Numero, $"A e B (espelhada/invertida) com o mesmo N (A=N{dA.Numero}, B=N{dB.Numero})");
            Checar(new[] { dE.Numero, dD.Numero, dA.Numero, dC.Numero }.SequenceEqual(new[] { 1, 2, 3, 4 }),
                $"numeração por bitola: ø5=N{dE.Numero} ø8=N{dD.Numero} ø10=N{dA.Numero} ø12.5=N{dC.Numero}");
            Checar(!string.IsNullOrEmpty(dA.HandleDescricao), "A tem descrição");
            var cotaA = Ent<DBText>(tr, db, dA.Segmentos[0].HandleCota);
            Checar(dA.Escala == 10 && cotaA != null && Math.Abs(cotaA.Height - 2) < 1e-9,
                $"escala base 1:10 → texto 2 un (escala {dA.Escala}, altura {cotaA?.Height})");
            Checar(dA.Segmentos.All(s => !string.IsNullOrEmpty(s.HandleCota)), "A tem cota em todos os segmentos");
            Checar(dE.Segmentos.Count == 4, "estribo fechado com 4 segmentos");
            Checar(dD.Segmentos[1].Formato == FormatoSegmento.Curva && dD.Segmentos[0].Formato == FormatoSegmento.Linha,
                "segmento em arco marcado como Curva");
            var descA = Ent<MText>(tr, db, dA.HandleDescricao);
            Rel.AppendLine("        descrição A: " + descA?.Contents);
            // comprimento real: 300 + 11,5 = 311,5 − (2·3,5 − 3·π/2 = 2,29) da dobra do gancho = 309,2
            Checar(descA != null && descA.Contents.StartsWith("4 N3 %%c10.0 C=309"), "texto da descrição de A (4 N3 ø10.0 C=309, real)");
            var tabela = Desenho.Entidades<Table>(db, tr).Select(id => (Table)tr.GetObject(id, OpenMode.ForRead)).FirstOrDefault();
            Checar(tabela != null && tabela.Rows.Count >= 2 + 4 + 2 + 4, $"tabela RA criada ({tabela?.Rows.Count} linhas)");

            H["descC"] = dC.HandleDescricao;
            H["descA"] = dA.HandleDescricao;
            Antes["descA.dx"] = dA.DescricaoDx;
            Antes["guidA"] = dA.Id;
            Antes["nA"] = dA.Numero;
            tr.Commit();
        }
        Gravar();

        // comandos reais, executados em sequência depois deste
        string Sel(string h) => $"(handent \"{h}\")";
        doc.SendStringToExecute($"_.ERASE {Sel(H["descC"])}\n\n", true, false, true);
        doc.SendStringToExecute("ARMTESTEMARCA\n", true, false, true);
        doc.SendStringToExecute($"_.MOVE {Sel(H["descA"])}\n\n0,0\n50,0\n", true, false, true);
        doc.SendStringToExecute("ARMTESTEMARCA\n", true, false, true);
        // COPY pode estar em modo múltiplo ou único (COPYMODE do perfil): ESC encerra nos dois casos
        // (um Enter extra repetia o COPY no modo único e engolia o STRETCH seguinte)
        doc.SendStringToExecute($"_.COPY {Sel(H["A"])}\n\n0,0\n0,400\n\x1B\x1B", true, false, true);
        doc.SendStringToExecute("ARMTESTEMARCA\n", true, false, true);
        doc.SendStringToExecute("_.STRETCH _C 690,-5 710,5\n\n0,0\n-30,0\n", true, false, true);
        // ARMBARRA digitado como o usuário: reta, ponto errado desfeito, reta, arco por 3 pontos, Enter
        doc.SendStringToExecute("ARMBARRA\n0,600\n100,600\n999,999\nD\n100,650\nA\n150,700\n200,650\n\n", true, false, true);
        doc.SendStringToExecute("ARMRAATUALIZAR\n", true, false, true);
        doc.SendStringToExecute("ARMTESTE2\n", true, false, true);
    }, suspender: false);

    [Rt.CommandMethod("ARMTESTE2")]
    public void Fase2() => Comandos.Executar("ARMTESTE2", (doc, ed, db, cfg) =>
    {
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var barras = Desenho.Entidades<Polyline>(db, tr)
                .Select(id => (Polyline)tr.GetObject(id, OpenMode.ForRead))
                .Select(pl => (pl, d: XDataCad.LerBarra(pl))).Where(x => x.d != null).ToList();
            Checar(Id(db, H["C"]).IsErased, "ERASE da descrição apagou a barra C");
            Checar(Desenho.Entidades<DBText>(db, tr).Select(i => (DBText)tr.GetObject(i, OpenMode.ForRead))
                       .All(t => XDataCad.Tipo(t) != TipoParte.Cota || ServicoBarra.BarraDaParte(tr, t) != null),
                   "nenhuma cota órfã");
            Checar(Desenho.Entidades<MText>(db, tr).Select(i => (MText)tr.GetObject(i, OpenMode.ForRead))
                       .All(t => XDataCad.Tipo(t) != TipoParte.Descricao || ServicoBarra.BarraDaParte(tr, t) != null),
                   "nenhuma descrição órfã");
            Checar(barras.Count == 6, $"6 barras após apagar C, copiar A e desenhar com ARMBARRA ({barras.Count})");

            var desenhada = barras.FirstOrDefault(x => x.pl.NumberOfVertices > 0 && x.pl.GetPoint2dAt(0).GetDistanceTo(new Point2d(0, 600)) < 1e-6);
            Checar(desenhada.pl != null, "ARMBARRA criou a barra digitada");
            if (desenhada.pl != null)
            {
                var geo = GeometriaBarra.Ler(desenhada.pl, 1);
                Rel.AppendLine("        ARMBARRA: " + string.Join(" + ", geo.Select(s => $"{s.Tipo} {s.Comprimento:0.##}")));
                Checar(geo.Count == 3 && Math.Abs(geo[0].Comprimento - 100) < 1e-6 && Math.Abs(geo[1].Comprimento - 50) < 1e-6,
                    "ARMBARRA: 'Desfazer' removeu o ponto errado (100 + 50 + arco)");
                Checar(geo.Count == 3 && geo[2].Tipo == TipoSegmento.Arco && Math.Abs(geo[2].Comprimento - Math.PI * 50) < 1e-6,
                    "ARMBARRA: arco por 3 pontos (meia-volta de raio 50 = 157.08)");
                Checar(desenhada.d.Bitola == cfg.BitolaCorrente && !string.IsNullOrEmpty(desenhada.d.HandleDescricao),
                    "ARMBARRA: bitola corrente e descrição criada");
            }

            var copia = barras.FirstOrDefault(x => XDataCad.Hex(x.pl.ObjectId) != H["A"] && x.pl.NumberOfVertices == 3
                                                   && Math.Abs(x.pl.GetPoint2dAt(0).Y - 400) < 1e-6);
            Checar(copia.pl != null, "COPY criou a cópia de A");
            if (copia.pl != null)
            {
                Checar(copia.d.Id != (Guid)Antes["guidA"], "cópia recebeu GUID novo");
                Checar(!string.IsNullOrEmpty(copia.d.HandleDescricao) && copia.d.HandleDescricao != H["descA"], "cópia tem a sua própria descrição");
                Checar(copia.d.Numero == (int)Antes["nA"], $"cópia igual a A mantém o N (N{copia.d.Numero})");
                var dc = Ent<MText>(tr, db, copia.d.HandleDescricao);
                Checar(dc != null && Math.Abs(dc.Location.Y - (400 + Barra(tr, db, "A").DescricaoDy)) < 1e-6, "descrição da cópia na mesma posição relativa");
            }

            var dA = Barra(tr, db, "A");
            var descA = Ent<MText>(tr, db, H["descA"]);
            Rel.AppendLine($"        descA: handle atual {dA.HandleDescricao} (antes {H["descA"]}), dx {dA.DescricaoDx} (antes {Antes["descA.dx"]}), " +
                           $"local {descA?.Location}");
            Checar(Math.Abs(dA.DescricaoDx - ((double)Antes["descA.dx"] + 50)) < 1e-6, "MOVE da descrição atualizou a posição guardada");
            Checar(!Id(db, H["A"]).IsErased, "mover a descrição não apagou a barra");

            var dB = Barra(tr, db, "B");
            var cotaB = Ent<DBText>(tr, db, dB.Segmentos[1].HandleCota);
            Checar(cotaB?.TextString == "330", $"STRETCH atualizou a cota de B ({cotaB?.TextString})");
            Checar(dB.Segmentos[0].Formato == FormatoSegmento.Gancho90, "gancho de B não esticado continua gancho");
            Checar(dB.Numero != dA.Numero, $"B alterada recebeu outro N (A=N{dA.Numero}, B=N{dB.Numero})");
            tr.Commit();
        }

        TestarPaleta(doc, db, cfg);
        TestarTabela(doc, db, cfg);

        // barra nova (N0) + comando da lista "atualiza a RA antes" → ARMTESTE3 confere a numeração
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var esp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var pl = new Polyline();
            pl.SetDatabaseDefaults(db);
            pl.AddVertexAt(0, new Point2d(0, 900), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(180, 900), 0, 0, 0);
            esp.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            ServicoBarra.Criar(tr, pl, new DadosArmadura { Bitola = 16, Quantidade = 2 }, cfg);
            H["F"] = XDataCad.Hex(pl.ObjectId);

            // G: estribo ø5 fechado 20×50; H: barra ø10 reta — para as faixas
            Polyline Nova(string nome, double bitola, string aco, bool fechada, params (double x, double y)[] v)
            {
                var p = new Polyline();
                p.SetDatabaseDefaults(db);
                for (int i = 0; i < v.Length; i++) p.AddVertexAt(i, new Point2d(v[i].x, v[i].y), 0, 0, 0);
                p.Closed = fechada;
                esp.AppendEntity(p);
                tr.AddNewlyCreatedDBObject(p, true);
                ServicoBarra.Criar(tr, p, new DadosArmadura { Bitola = bitola, Aco = aco, Estribo = fechada, Quantidade = 1 }, cfg);
                H[nome] = XDataCad.Hex(p.ObjectId);
                return p;
            }
            Nova("G", 5, "CA-60", true, (1500, 300), (1520, 300), (1520, 350), (1500, 350));
            Nova("H", 10, "CA-50", false, (0, -400), (150, -400));
            Nova("R", 12.5, "CA-50", false, (3000, 500), (3200, 500), (3200, 550));

            // forma de uma seção 30×60 (linhas simples) para o "ponto no cobrimento"
            foreach (var (a, b) in new[] { ((4000.0, 0.0), (4030.0, 0.0)), ((4000.0, 0.0), (4000.0, 60.0)), ((4030.0, 0.0), (4030.0, 60.0)) })
            {
                var ln = new Line(new Point3d(a.Item1, a.Item2, 0), new Point3d(b.Item1, b.Item2, 0));
                ln.SetDatabaseDefaults(db);
                esp.AppendEntity(ln);
                tr.AddNewlyCreatedDBObject(ln, true);
            }
            tr.Commit();
        }
        cfg.ComandosQueAtualizamRA.Add("ARMTESTEMARCA");
        Gravar();

        // estribo simples pela repetição (opções de teste; as do usuário voltam no fim)
        _estriboDoUsuario = Plugin.Config.UltimoEstribo;
        Plugin.Config.UltimoEstribo = new Core.Estribos.OpcoesEstribo
        {
            Formato = Core.Estribos.FormatoEstribo.Simples, Gancho = FormatoSegmento.Gancho45, Bitola = 5, Aco = "CA-60",
            UsarSecao = true, Cobrimento = 2.5, Espacamento = 15, DimensoesFixas = false,
        };
        doc.SendStringToExecute("ARMESTRIBOREP\n2000,0\n2020,-50\n2100,0\n2200,0\n2200,-300\n2230,-150\n\n\n", true, false, true);

        // faixas digitadas como o usuário, com a barra pré-selecionada (pickfirst)
        string Pre(string h) => $"(sssetfirst nil (ssadd (handent \"{h}\")))\n";
        doc.SendStringToExecute(Pre(H["G"]) + "ARMFAIXAORT\n1000,0\n1400,0\n1200,-30\n15\n\n", true, false, true);
        doc.SendStringToExecute(Pre(H["H"]) + "ARMCORTECOTAORT\n0,-300\n100,-300\n50,-330\n20\n50,-280\n\n", true, false, true);
        doc.SendStringToExecute(Pre(H["H"]) + "ARMCHAMADA\n200,-300\n220,-300\n\n260,-260\n3\n\n", true, false, true);
        doc.SendStringToExecute(Pre(H["H"]) + "ARMINDICACAO\n300,-300\n\n320,-260\n\n", true, false, true);
        // esticar a faixa de G (400 → 300) e copiar a faixa de corte de H (soma mais uma)
        doc.SendStringToExecute("_.STRETCH _C 1390,-10 1410,10\n\n0,0\n-100,0\n", true, false, true);
        doc.SendStringToExecute("_.COPY _C -5,-335 105,-325\n\n0,0\n0,-100\n\x1B\x1B", true, false, true);
        // rebater R para baixo; esticar a rebatida (50 → 80) e depois movê-la (só o deslocamento muda)
        doc.SendStringToExecute(Pre(H["R"]) + "ARMREBATER\n3000,500\n3000,300\n", true, false, true);
        doc.SendStringToExecute("_.STRETCH _C 3195,545 3205,555\n\n0,0\n0,30\n", true, false, true);
        doc.SendStringToExecute("_.MOVE _C 3095,495 3105,505\n\n0,0\n100,0\n", true, false, true);
        // U pelo cobrimento: linha esquerda (lado de dentro), ponto no topo, fundo, direita, ponto no topo
        doc.SendStringToExecute("_.ZOOM _W 3990,-10 4040,70\n", true, false, true);
        doc.SendStringToExecute("ARMBARRA\nCobrimento\n2.5\n4000,40\n4010,40\nPonto\n4005,55\n4015,0\n4030,30\nPonto\n4020,55\n\n",
            true, false, true);
        doc.SendStringToExecute("ARMTESTEMARCA\n", true, false, true);
        doc.SendStringToExecute("ARMTESTE3\n", true, false, true);
    }, suspender: false);

    private static List<(Entity ent, DadosFaixa f)> Faixas(Transaction tr, Database db, string barra) =>
        Barra(tr, db, barra).Faixas
            .Select(h => Ent<Entity>(tr, db, h)).Where(e => e != null)
            .Select(e => (e, ServicoFaixa.Ler(e))).ToList();

    [Rt.CommandMethod("ARMTESTE3")]
    public void Fase3() => Comandos.Executar("ARMTESTE3", (doc, ed, db, cfg) =>
    {
        cfg.ComandosQueAtualizamRA.Remove("ARMTESTEMARCA");
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var dF = Barra(tr, db, "F");
            Checar(dF.Numero > 0, $"RA atualizada sozinha antes do comando da lista (barra nova virou N{dF.Numero})");

            // G: faixa ortogonal esticada para 300 cm c/15 → 21
            var fg = Faixas(tr, db, "G");
            var dimG = fg.Count == 1 ? fg[0].ent as Dimension : null;
            Rel.AppendLine($"        faixa de G: {dimG?.DimensionText} (medida {dimG?.Measurement:0.##})");
            Checar(dimG != null && dimG.DimensionText.StartsWith("21 N") && dimG.DimensionText.EndsWith("c/15"),
                "ARMFAIXAORT + STRETCH: faixa recalculada (300 cm c/15 = 21)");
            Checar(Barra(tr, db, "G").Quantidade == 21, $"quantidade do estribo = soma das faixas ({Barra(tr, db, "G").Quantidade})");
            Checar(dimG != null && Math.Abs(dimG.Dimtxt - 2) < 1e-9, $"texto da faixa na escala da barra (2 un; {dimG?.Dimtxt})");

            // H: corte (6) + chamada (3) + indicação (0) + cópia do corte (6) = 15
            var fh = Faixas(tr, db, "H");
            Rel.AppendLine("        faixas de H: " + string.Join("; ", fh.Select(x => $"{x.f.Tipo} q={x.f.Quantidade} partes={x.f.Partes.Count}")));
            var cortes = fh.Where(x => x.f.Tipo == TipoFaixa.CorteCotaOrtogonal).ToList();
            Checar(fh.Count == 4 && cortes.Count == 2, $"H tem 4 faixas, 2 de corte (a copiada entrou) ({fh.Count})");
            Checar(cortes.All(c => c.f.Quantidade == 6 && c.f.Partes.Count == 6), "corte: 6 barras e 6 círculos em cada faixa");
            var circulos = cortes.SelectMany(c => c.f.Partes).Distinct().Count();
            Checar(circulos == 12, $"a faixa copiada tem os próprios círculos ({circulos} distintos)");
            var circ = Ent<Entity>(tr, db, cortes[0].f.Partes[0]);
            Checar(circ is Polyline && ((Polyline)circ).Closed && circ.Layer == cfg.LayerCorte, "círculo do corte preenchido no layer de corte");
            var cham = fh.FirstOrDefault(x => x.f.Tipo == TipoFaixa.Chamada);
            Checar(cham.ent is MText mt && mt.Contents.StartsWith("3 N") && cham.f.Partes.Count == 2, "chamada: texto \"3 N…\" e 2 linhas");
            var ind = fh.FirstOrDefault(x => x.f.Tipo == TipoFaixa.Indicacao);
            Checar(ind.ent is MText mi && mi.Contents.StartsWith("N") && ind.f.Partes.Count == 1, "indicação: texto \"N…\" e 1 linha");
            Checar(Barra(tr, db, "H").Quantidade == 15, $"quantidade de H = 6 + 3 + 6 (indicação não soma) ({Barra(tr, db, "H").Quantidade})");

            TestarEstribos(tr, db, cfg);

            // rebatida
            var plR = (Polyline)tr.GetObject(Id(db, H["R"]), OpenMode.ForRead);
            var dR = XDataCad.LerBarra(plR);
            var geoR = GeometriaBarra.Ler(plR, 1);
            Rel.AppendLine($"        barra R: início {plR.GetPoint2dAt(0)}, segmentos {string.Join(" + ", geoR.Select(s => s.Comprimento.ToString("0.##")))}");
            Checar(plR.GetPoint2dAt(0).GetDistanceTo(new Point2d(3000, 300)) < 1e-6, "ARMREBATER levou a barra para o destino");
            Checar(dR.Rebatidas.Count == 1, $"barra lista 1 rebatida ({dR.Rebatidas.Count})");
            var reb = dR.Rebatidas.Count == 1 ? Ent<Polyline>(tr, db, dR.Rebatidas[0]) : null;
            if (reb != null)
            {
                Rel.AppendLine($"        rebatida: início {reb.GetPoint2dAt(0)}");
                Checar(geoR.Count == 2 && Math.Abs(geoR[1].Comprimento - 80) < 1e-6, "esticar a rebatida esticou a barra (50 → 80)");
                Checar(reb.GetPoint2dAt(0).GetDistanceTo(new Point2d(3100, 500)) < 1e-6, "mover a rebatida não moveu a barra");
                var vR = XDataCad.LerVinculo(reb);
                Checar(Math.Abs(vR.Dx - (-100)) < 1e-6 && Math.Abs(vR.Dy - (-200)) < 1e-6, $"deslocamento guardado ({vR.Dx}, {vR.Dy})");
                Checar(!reb.Database.Equals(null) && XDataCad.LerBarra(reb) == null, "rebatida não entra como barra na relação do aço");
                H["rebatida"] = dR.Rebatidas[0];
            }
            var descR = Ent<MText>(tr, db, dR.HandleDescricao);
            Checar(descR != null && descR.Location.Y < 400, "a descrição foi junto com a barra");
            // ponto no cobrimento: U 52,5 + 25 + 52,5 dentro da seção 30×60 com cobrimento 2,5
            var u = Desenho.Entidades<Polyline>(db, tr).Select(i => (Polyline)tr.GetObject(i, OpenMode.ForRead))
                .FirstOrDefault(p => XDataCad.LerBarra(p) != null && p.NumberOfVertices > 0
                                     && p.GetPoint2dAt(0).GetDistanceTo(new Point2d(4002.5, 55)) < 1e-6);
            Checar(u != null, "ARMBARRA pelo cobrimento criou a barra");
            if (u != null)
            {
                var gu = GeometriaBarra.Ler(u, 1);
                Rel.AppendLine("        U pelo cobrimento: " + string.Join(" → ", Enumerable.Range(0, u.NumberOfVertices).Select(i => u.GetPoint2dAt(i).ToString())));
                Checar(u.NumberOfVertices == 4 && u.GetPoint2dAt(1).GetDistanceTo(new Point2d(4002.5, 2.5)) < 1e-6
                       && u.GetPoint2dAt(2).GetDistanceTo(new Point2d(4027.5, 2.5)) < 1e-6
                       && u.GetPoint2dAt(3).GetDistanceTo(new Point2d(4027.5, 55)) < 1e-6,
                    "vértices nas interseções das linhas deslocadas (lado de dentro deduzido)");
                // ARMRECOTA: desliga a cota do fundo e pede para restaurar clicando no segmento
                var du = XDataCad.LerBarra(u);
                du.Segmentos[1].Cotar = false;
                u.UpgradeOpen();
                XDataCad.Gravar(u, du);
                using (Sincronizador.Suspender(doc)) ServicoBarra.Atualizar(tr, u, cfg);
                Checar(string.IsNullOrEmpty(XDataCad.LerBarra(u).Segmentos[1].HandleCota), "cota do fundo desligada");
                H["U"] = XDataCad.Hex(u.ObjectId);
            }

            // apaga a barra R (eventos): ARMTESTE4 confere que a rebatida sumiu junto
            plR.UpgradeOpen();
            plR.Erase();

            // apaga a chamada como o usuário (eventos) — ARMTESTE4 confere
            H["chamada"] = XDataCad.Hex(cham.ent.ObjectId);
            H["linhaChamada"] = cham.f.Partes[0];
            cham.ent.UpgradeOpen();
            cham.ent.Erase();

            // apaga só a linha da indicação (eventos): ela não pode voltar na regeneração
            var indL = Faixas(tr, db, "H").FirstOrDefault(x => x.f.Tipo == TipoFaixa.Indicacao);
            if (indL.ent != null && indL.f.Partes.Count == 1)
            {
                H["indicacao"] = XDataCad.Hex(indL.ent.ObjectId);
                var linha = Ent<Entity>(tr, db, indL.f.Partes[0]);
                linha.UpgradeOpen();
                linha.Erase();
            }

            // Enter dado pelo usuário ao editar a descrição de H e o texto de uma faixa de cota (eventos)
            var descH = Ent<MText>(tr, db, Barra(tr, db, "H").HandleDescricao);
            if (descH != null)
            {
                descH.UpgradeOpen();
                var pal = descH.Contents.Split(' ');
                descH.Contents = string.Join(" ", pal.Take(2)) + "\\P" + string.Join(" ", pal.Skip(2));
            }
            var fCota = Faixas(tr, db, "H").FirstOrDefault(x => x.f.TemCota);
            if (fCota.ent is Dimension dimQ)
            {
                H["faixaQuebra"] = XDataCad.Hex(dimQ.ObjectId);
                dimQ.UpgradeOpen();
                var pal = dimQ.DimensionText.Split(' ');
                dimQ.DimensionText = pal[0] + "\\P" + string.Join(" ", pal.Skip(1));
            }
            tr.Commit();
        }
        Gravar();
        doc.SendStringToExecute("_.ZOOM _W 3990,-10 4040,70\n", true, false, true);
        doc.SendStringToExecute("ARMRECOTA\n4015,2.5\n\n", true, false, true);
        doc.SendStringToExecute("ARMTESTE4\n", true, false, true);
    }, suspender: false);

    [Rt.CommandMethod("ARMTESTE4")]
    public void Fase4() => Comandos.Executar("ARMTESTE4", (doc, ed, db, cfg) =>
    {
        if (_estriboDoUsuario != null) Plugin.Config.UltimoEstribo = _estriboDoUsuario;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            Checar(Barra(tr, db, "H").Quantidade == 12, $"apagar a chamada tira 3 da quantidade ({Barra(tr, db, "H").Quantidade})");
            Checar(Id(db, H["linhaChamada"]).IsErased, "apagar a chamada apaga as linhas dela");
            Checar(Faixas(tr, db, "H").Count == 3, "a barra deixou de listar a faixa apagada");
            {
                // Enter na edição: a quebra fica guardada e sobrevive à regeneração
                var dH = Barra(tr, db, "H");
                var plH = (Polyline)tr.GetObject(Id(db, H["H"]), OpenMode.ForWrite);
                using (Sincronizador.Suspender(doc)) ServicoBarra.Atualizar(tr, plH, cfg);
                string cH = Ent<MText>(tr, db, Barra(tr, db, "H").HandleDescricao)?.Contents ?? "";
                Checar(dH.QuebrasDescricao.SequenceEqual(new[] { 2 }) && cH.Split(new[] { "\\P" }, StringSplitOptions.None).Length == 2,
                    $"descrição editada com Enter fica em 2 linhas após regenerar (\"{cH}\")");
                if (H.ContainsKey("faixaQuebra"))
                {
                    var dq = (Dimension)tr.GetObject(Id(db, H["faixaQuebra"]), OpenMode.ForRead);
                    Checar(ServicoFaixa.Ler(dq).Quebras.SequenceEqual(new[] { 1 }) && dq.DimensionText.Contains("\\P"),
                        $"texto da faixa editado com Enter fica em 2 linhas (\"{dq.DimensionText}\")");
                }
            }
            if (H.ContainsKey("indicacao"))
            {
                var indE = (Entity)tr.GetObject(Id(db, H["indicacao"]), OpenMode.ForWrite);
                var barH = (Polyline)tr.GetObject(Id(db, H["H"]), OpenMode.ForWrite);
                using (Sincronizador.Suspender(doc)) ServicoBarra.Atualizar(tr, barH, cfg);
                var fi = ServicoFaixa.Ler(indE);
                Checar(fi.Partes.Count == 0 && fi.LinhasApagadas.SequenceEqual(new[] { 0 }),
                    $"linha de chamada apagada não volta na regeneração ({fi.Partes.Count} linha(s))");
                // "Mostrar linha de chamada" = Sim (mesma ação da paleta) recria a linha
                var pf = new Paleta.PropriedadesFaixa(indE.ObjectId, fi, 1);
                Checar(!pf.MostrarLinhas, "paleta: linha apagada aparece como \"Mostrar linha de chamada\" = Não");
                pf.MostrarLinhas = true;
                foreach (var a in pf.Pendentes) a(fi);
                XDataCad.Gravar(indE, fi);
                using (Sincronizador.Suspender(doc)) ServicoBarra.Atualizar(tr, barH, cfg);
                fi = ServicoFaixa.Ler(indE);
                Checar(fi.Partes.Count == 1 && fi.LinhasApagadas.Count == 0, "Sim na linha de chamada recria a linha apagada");

                // campos opcionais do texto da faixa
                var fo = Faixas(tr, db, "H").First(x => x.f.TemCota);
                var fod = fo.f;
                fod.MostrarQuantidade = false; fod.MostrarEspacamento = false;
                fo.ent.UpgradeOpen();
                XDataCad.Gravar(fo.ent, fod);
                using (Sincronizador.Suspender(doc)) ServicoBarra.Atualizar(tr, barH, cfg);
                string txt = ((Dimension)tr.GetObject(fo.ent.ObjectId, OpenMode.ForRead)).DimensionText;
                Checar(txt.StartsWith("N") && !txt.Contains("c/"), $"faixa sem quantidade e sem espaçamento: \"{txt}\"");
                fod.MostrarQuantidade = true; fod.MostrarEspacamento = true;
                // repetições da faixa: a barra soma 2× a quantidade dela; o texto fica "2x6 N…" (sem parênteses)
                int antesRep = XDataCad.LerBarra(barH).Quantidade;
                fod.Repeticoes = 2;
                XDataCad.Gravar(fo.ent, fod);
                using (Sincronizador.Suspender(doc)) ServicoBarra.Atualizar(tr, barH, cfg);
                string txtRep = ((Dimension)tr.GetObject(fo.ent.ObjectId, OpenMode.ForRead)).DimensionText;
                int depoisRep = XDataCad.LerBarra(barH).Quantidade;
                Checar(depoisRep == antesRep + fod.Quantidade && txtRep.Replace("\\P", " ").StartsWith("2x" + fod.Quantidade + " "),
                    $"faixa com 2 repetições: barra {antesRep} → {depoisRep}, texto \"{txtRep}\"");
                fod.Repeticoes = 1;
                XDataCad.Gravar(fo.ent, fod);
                using (Sincronizador.Suspender(doc)) ServicoBarra.Atualizar(tr, barH, cfg);
            }
            if (H.ContainsKey("rebatida")) Checar(Id(db, H["rebatida"]).IsErased, "apagar a barra apagou a rebatida");
            if (H.ContainsKey("U"))
            {
                var du = Barra(tr, db, "U");
                Checar(du.Segmentos[1].Cotar && !string.IsNullOrEmpty(du.Segmentos[1].HandleCota), "ARMRECOTA restaurou a cota do fundo");
            }
            tr.Commit();
        }

        // ----- fase 3: detalhamento = bloco -----
        TestarDetalhamento(doc, db, cfg);
        Gravar();

        // REGEN recalcula as descrições: deixa a de A desatualizada sem o sincronizador ver
        using (Sincronizador.Suspender(doc))
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var desc = Ent<MText>(tr, db, Barra(tr, db, "A").HandleDescricao);
            if (desc != null) { desc.UpgradeOpen(); desc.Contents = "DESATUALIZADA"; }
            tr.Commit();
        }
        doc.SendStringToExecute("_.REGEN\n", true, false, true);
        doc.SendStringToExecute("ARMTESTEREGEN\n", true, false, true);
    }, suspender: false);

    [Rt.CommandMethod("ARMTESTEREGEN")]
    public void FaseRegen() => Comandos.Executar("ARMTESTEREGEN", (doc, ed, db, cfg) =>
    {
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var desc = Ent<MText>(tr, db, Barra(tr, db, "A").HandleDescricao);
            Rel.AppendLine("        descrição de A após REGEN: " + desc?.Contents);
            Checar(desc != null && desc.Contents.Contains("C=") && desc.Contents != "DESATUALIZADA",
                "REGEN recalculou a descrição desatualizada");
            tr.Commit();
        }
        Gravar();
        // RA parcial digitada: seleciona o bloco V1 e insere a tabela
        doc.SendStringToExecute($"ARMRAPARCIAL\n(handent \"{H["refV1"]}\")\n\n5500,-100\n", true, false, true);
        doc.SendStringToExecute("ARMTESTE5\n", true, false, true);
    }, suspender: false);

    /// <summary>Barras + faixa → ARMDETCRIAR (API) → 2ª inserção. Confere a tradução dos vínculos.</summary>
    private static void TestarDetalhamento(Document doc, Database db, Core.Configuracao.ConfiguracaoArmadura cfg)
    {
        try
        {
            ObjectId[] selecao;
            using (var tr = db.TransactionManager.StartTransaction())
            using (Sincronizador.Suspender(doc))
            {
                var esp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                Polyline Nova(string nome, double bitola, params (double x, double y)[] v)
                {
                    var p = new Polyline();
                    p.SetDatabaseDefaults(db);
                    for (int i = 0; i < v.Length; i++) p.AddVertexAt(i, new Point2d(v[i].x, v[i].y), 0, 0, 0);
                    esp.AppendEntity(p);
                    tr.AddNewlyCreatedDBObject(p, true);
                    ServicoBarra.Criar(tr, p, new DadosArmadura { Bitola = bitola, Quantidade = 1 }, cfg);
                    H[nome] = XDataCad.Hex(p.ObjectId);
                    return p;
                }
                var k1 = Nova("K1", 10, (5000, 0), (5300, 0));
                Nova("K2", 8, (5000, 100), (5200, 100), (5200, 140));
                var f = new DadosFaixa { Tipo = TipoFaixa.Chamada, Quantidade = 4 };
                f.Referencias.Add((5100, 0));
                ServicoFaixa.Criar(tr, k1, f, new Point3d(5150, 40, 0), cfg);

                // tudo o que pertence a K1 e K2 (barras, textos, faixa e linhas)
                var ids = new List<ObjectId>();
                foreach (var nome in new[] { "K1", "K2" })
                {
                    var pl = (Polyline)tr.GetObject(Id(db, H[nome]), OpenMode.ForRead);
                    var d = XDataCad.LerBarra(pl);
                    ids.Add(pl.ObjectId);
                    ids.Add(Id(db, d.HandleDescricao));
                    ids.AddRange(d.Segmentos.Where(s => s.HandleCota != "").Select(s => Id(db, s.HandleCota)));
                    foreach (var hf in d.Faixas)
                    {
                        ids.Add(Id(db, hf));
                        ids.AddRange(ServicoFaixa.Ler(Ent<Entity>(tr, db, hf)).Partes.Select(p => Id(db, p)));
                    }
                }
                selecao = ids.Where(i => !i.IsNull).ToArray();
                tr.Commit();
            }

            using (var tr = db.TransactionManager.StartTransaction())
            using (Sincronizador.Suspender(doc))
            {
                var refId = Relacao.ServicoDetalhamento.Criar(db, tr, selecao, "V1", new DadosDetalhamento { Grupo = "Vigas" });
                H["refV1"] = XDataCad.Hex(refId);
                var br = (BlockReference)tr.GetObject(refId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                H["btrV1"] = XDataCad.Hex(btr.ObjectId);

                Checar(Id(db, H["K1"]).IsErased && Id(db, H["K2"]).IsErased, "ARMDETCRIAR: as barras originais saíram do modelo");
                var barras = btr.Cast<ObjectId>().Select(i => tr.GetObject(i, OpenMode.ForRead)).OfType<Polyline>()
                    .Where(p => XDataCad.LerBarra(p) != null).ToList();
                Checar(barras.Count == 2, $"o bloco V1 contém as 2 barras ({barras.Count})");
                // os vínculos (1005) foram traduzidos: descrição, cotas e faixa continuam ligados às cópias
                bool vinculos = barras.All(p =>
                {
                    var d = XDataCad.LerBarra(p);
                    var desc = ServicoBarra.Parte<MText>(tr, db, d.HandleDescricao, TipoParte.Descricao, XDataCad.Hex(p.ObjectId));
                    return desc != null && desc.OwnerId == btr.ObjectId;
                });
                Checar(vinculos, "descrições dentro do bloco continuam ligadas às barras (handles 1005 traduzidos)");
                var k1b = barras.FirstOrDefault(p => XDataCad.LerBarra(p).Bitola == 10);
                var dk1 = k1b == null ? null : XDataCad.LerBarra(k1b);
                var faixaB = dk1 != null && dk1.Faixas.Count == 1 ? Ent<Entity>(tr, db, dk1.Faixas[0]) : null;
                Checar(faixaB != null && faixaB.OwnerId == btr.ObjectId && ServicoFaixa.BarraDaFaixa(tr, faixaB)?.ObjectId == k1b.ObjectId,
                    "a faixa foi para dentro do bloco ligada à barra");
                Checar(dk1?.Quantidade == 4, $"quantidade de K1 no bloco = faixa (4) ({dk1?.Quantidade})");
                H["K1b"] = XDataCad.Hex(k1b.ObjectId);
                H["K2b"] = XDataCad.Hex(barras.First(p => p != k1b).ObjectId);
                Checar(Relacao.ServicoDetalhamento.Ler(btr)?.Grupo == "Vigas", "dados do detalhamento gravados no bloco");

                // 2ª inserção do mesmo detalhamento
                var esp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var br2 = new BlockReference(new Point3d(5000, -300, 0), btr.ObjectId);
                esp.AppendEntity(br2);
                tr.AddNewlyCreatedDBObject(br2, true);
                tr.Commit();
            }
            // a lista de referências do bloco só é atualizada depois do commit
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(Id(db, H["btrV1"]), OpenMode.ForRead);
                int n = Relacao.ServicoDetalhamento.Insercoes(tr, btr);
                Checar(n == 2, $"V1 inserido 2 vezes ({n})");
                tr.Commit();
            }
        }
        catch (Exception ex)
        {
            Checar(false, "detalhamento: exceção " + ex);
        }
    }

    /// <summary>Linhas de dados da relação (entre o cabeçalho e o resumo/legenda).</summary>
    private static List<string[]> LinhasRelacao(Table t)
    {
        var r = new List<string[]>();
        for (int i = 2; i < t.Rows.Count; i++)
        {
            string c0 = t.Cells[i, 0].TextString;
            if (c0.StartsWith("RESUMO") || c0.StartsWith("*")) break;
            r.Add(Enumerable.Range(0, t.Columns.Count).Select(c => t.Cells[i, c].TextString).ToArray());
        }
        return r;
    }

    private static Table TabelaParcial(Transaction tr, Database db, Core.Configuracao.ConfiguracaoArmadura cfg, string bloco) =>
        Desenho.Entidades<Table>(db, tr).Select(i => (Table)tr.GetObject(i, OpenMode.ForRead))
            .FirstOrDefault(t => Relacao.ServicoRA.Opcoes(t, cfg)?.Blocos.Contains(bloco, StringComparer.OrdinalIgnoreCase) == true);

    private static void TestarRelacaoParcialEBloqueio(Document doc, Database db, Core.Configuracao.ConfiguracaoArmadura cfg)
    {
        try
        {
            using (Sincronizador.Suspender(doc))
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // RA parcial do bloco V1 (inserido 2×): K1 4×2 = 8, K2 1×2 = 2
                var tp = TabelaParcial(tr, db, cfg, "V1");
                Checar(tp != null, "ARMRAPARCIAL criou a tabela parcial de V1");
                if (tp != null)
                {
                    var linhas = LinhasRelacao(tp);
                    Rel.AppendLine("        RA parcial V1: " + string.Join(" | ", linhas.Select(l => string.Join(" ", l))));
                    Checar(linhas.Count == 2 && linhas.Select(l => l[3]).OrderBy(x => x).SequenceEqual(new[] { "2", "8" }),
                        "RA parcial: só as barras de V1, × 2 inserções (8 e 2)");
                }

                // numeração Individual: recomeça no detalhamento; coluna ELEMENTO
                cfg.TipoNumeracao = Core.Numeracao.TipoNumeracao.Individual;
                Relacao.ServicoRA.Atualizar(db, tr, cfg);
                var nK1 = XDataCad.LerBarra(tr.GetObject(Id(db, H["K1b"]), OpenMode.ForRead)).Numero;
                var nK2 = XDataCad.LerBarra(tr.GetObject(Id(db, H["K2b"]), OpenMode.ForRead)).Numero;
                Checar(nK2 == 1 && nK1 == 2, $"numeração individual: em V1, ø8 = N1 e ø10 = N2 ({nK2}, {nK1})");
                if (tp != null)
                {
                    var l = LinhasRelacao(tp);
                    Checar(tp.Columns.Count == 7 && tp.Cells[1, 0].TextString == "ELEMENTO" && l.All(x => x[0] == "V1"),
                        "tabela com coluna ELEMENTO (= V1)");
                }
                cfg.TipoNumeracao = Core.Numeracao.TipoNumeracao.Global;
                Relacao.ServicoRA.Atualizar(db, tr, cfg);

                // bloqueio: números fixos; uma barra alterada ganha número novo acima do maior, com *
                var antes = Desenho.Entidades<Polyline>(db, tr, incluirBlocos: true)
                    .Select(i => (Polyline)tr.GetObject(i, OpenMode.ForRead))
                    .Select(p => (p.ObjectId, d: XDataCad.LerBarra(p))).Where(x => x.d != null)
                    .ToDictionary(x => x.ObjectId, x => x.d.Numero);
                int maior = antes.Values.Max();
                Checar(Relacao.ServicoRA.AlternarBloqueio(db, tr, cfg), "ARMRABLOQUEAR liga o bloqueio");
                var plD = (Polyline)tr.GetObject(Id(db, H["D"]), OpenMode.ForWrite);
                var dD = XDataCad.LerBarra(plD);
                dD.Bitola = 12.5;
                XDataCad.Gravar(plD, dD);
                Relacao.ServicoRA.Atualizar(db, tr, cfg);
                var depois = antes.Keys.ToDictionary(k => k, k => XDataCad.LerBarra(tr.GetObject(k, OpenMode.ForRead)).Numero);
                var dD2 = XDataCad.LerBarra(plD);
                Checar(dD2.Numero == maior + 1 && dD2.Nova, $"barra alterada com a RA bloqueada: N{dD2.Numero} (maior era {maior}), destacada");
                Checar(depois.Where(kv => kv.Key != plD.ObjectId).All(kv => kv.Value == antes[kv.Key]), "as demais barras mantiveram o número");
                var tg = Desenho.Entidades<Table>(db, tr).Select(i => (Table)tr.GetObject(i, OpenMode.ForRead))
                    .First(t => Relacao.ServicoRA.Opcoes(t, cfg)?.Parcial == false);
                Checar(LinhasRelacao(tg).Any(x => x[1] == (maior + 1) + "*"), $"tabela marca N{maior + 1}* (nova)");
                Checar(!Relacao.ServicoRA.AlternarBloqueio(db, tr, cfg), "ARMRABLOQUEAR desliga o bloqueio");
                Checar(!XDataCad.LerBarra(plD).Nova && !LinhasRelacao(tg).Any(x => x[1].EndsWith("*")), "desbloqueio limpa os destaques");

                // camadas "3+1": 2 nervuras × 4 barras = 8
                var plF = (Polyline)tr.GetObject(Id(db, H["F"]), OpenMode.ForWrite);
                var dF = XDataCad.LerBarra(plF);
                dF.Camadas = "3+1";
                XDataCad.Gravar(plF, dF);
                ServicoBarra.Atualizar(tr, plF, cfg);
                var descF = Ent<MText>(tr, db, XDataCad.LerBarra(plF).HandleDescricao);
                Checar(XDataCad.LerBarra(plF).QuantidadeTotal() == 8 && descF.Contents.StartsWith("2x(3+1)"),
                    $"camadas 3+1 com 2 nervuras = 8 barras ({descF.Contents})");
                tr.Commit();
            }

            // paleta: detalhamento renomeado e com dados; a RA parcial acompanha o nome novo
            var dets = Paleta.PaletaArmadura.LerDetalhamentos(db, new[] { Id(db, H["refV1"]) });
            Checar(dets.Count == 1 && dets[0].Repeticoes == 2 && dets[0].Grupo == "Vigas", "paleta mostra o detalhamento (2 repetições)");
            dets[0].Nome = "V1A";
            dets[0].Volume = 0.5;
            var erros = Paleta.PaletaArmadura.AplicarDetalhamentos(doc, dets);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(Id(db, H["btrV1"]), OpenMode.ForRead);
                Checar(erros.Count == 0 && btr.Name == "V1A" && Relacao.ServicoDetalhamento.Ler(btr).Volume == 0.5,
                    "paleta renomeou o detalhamento e gravou o volume");
                var tp = TabelaParcial(tr, db, cfg, "V1A");
                Checar(tp != null && LinhasRelacao(tp).Count == 2, "RA parcial acompanhou o nome novo do bloco");
                tr.Commit();
            }
            using (var f = new Paleta.FormDetalhamentos(new List<Paleta.LinhaDetalhamento>())) { }
            Checar(true, "grade de detalhamentos monta sem erro");
        }
        catch (Exception ex)
        {
            Checar(false, "RA parcial/bloqueio: exceção " + ex);
        }
    }

    /// <summary>Template só do autoteste: uma barra em L com gancho e um contorno.</summary>
    private sealed class TemplateTeste : Core.Templates.ITemplateArmadura
    {
        public string Id => "teste-l";
        public string Nome => "Teste (L)";
        public Core.Templates.CategoriaElemento Categoria => Core.Templates.CategoriaElemento.Laje;
        public string Descricao => "Só para o autoteste.";
        public IReadOnlyList<Core.Templates.ParametroTemplate> Parametros { get; } = new[]
        {
            new Core.Templates.ParametroTemplate { Nome = "L", Rotulo = "Comprimento", Unidade = "cm", Padrao = "200", Minimo = 10 },
        };
        public Core.Templates.ResultadoTemplate Gerar(Core.Templates.ValoresTemplate v, Core.Configuracao.ConfiguracaoArmadura cfg)
        {
            var r = new Core.Templates.ResultadoTemplate();
            double l = v.Numero("L");
            r.Contornos.Add(new Core.Templates.ContornoTemplate { Vertices = { (0, 0), (l, 0), (l, 20), (0, 20) }, Fechado = true });
            r.Barras.Add(new Core.Templates.BarraTemplate
            {
                Vertices = { (3, 17), (l - 3, 17), (l - 3, 3) }, Formatos = { FormatoSegmento.Linha, FormatoSegmento.Gancho90 },
                Bitola = 10, Aco = "CA-50", Quantidade = 3,
            });
            return r;
        }
    }

    /// <summary>Pedidos de 2026-10-27: estribo de poligonal, offset, escala da faixa, paleta recolhida, tabela, templates, menu.</summary>
    private static void TestarNovidades(Document doc, Database db, Core.Configuracao.ConfiguracaoArmadura cfg)
    {
        // toda classe com comandos tem de estar em [assembly: CommandClass] (com a lista, o CAD só registra
        // os comandos das classes listadas — o ARMTEMPLATE ficou de fora na 0.1.9)
        var asm = typeof(AutoTeste).Assembly;
        var listadas = new HashSet<Type>(asm.GetCustomAttributes(typeof(Rt.CommandClassAttribute), false)
            .Cast<Rt.CommandClassAttribute>().Select(a => a.Type));
        var faltando = asm.GetTypes().Where(t => t.GetMethods().Any(m => m.GetCustomAttributes(typeof(Rt.CommandMethodAttribute), false).Length > 0))
            .Where(t => !listadas.Contains(t)).Select(t => t.Name).ToList();
        Checar(faltando.Count == 0, "todas as classes de comandos registradas" + (faltando.Count > 0 ? ": faltam " + string.Join(", ", faltando) : ""));
        Checar(Core.Templates.CatalogoTemplates.Obter("base-reservatorio") != null, "template da base de reservatório registrado no ARMTEMPLATE");
        try
        {
            using (Sincronizador.Suspender(doc))
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var esp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                Polyline Poli(bool fechada, params (double x, double y)[] v)
                {
                    var p = new Polyline();
                    p.SetDatabaseDefaults(db);
                    for (int i = 0; i < v.Length; i++) p.AddVertexAt(i, new Point2d(v[i].x, v[i].y), 0, 0, 0);
                    p.Closed = fechada;
                    esp.AppendEntity(p);
                    tr.AddNewlyCreatedDBObject(p, true);
                    return p;
                }

                // tabela parcial: nome do detalhamento no título e coluna ELEMENTO com numeração global
                var tp = TabelaParcial(tr, db, cfg, "V1A");
                if (tp != null)
                {
                    var op = Relacao.ServicoRA.Opcoes(tp, cfg);
                    op.NomeNoTitulo = true;
                    op.ColunaElemento = Core.RelacaoAco.ColunaElemento.Sempre;
                    Relacao.ServicoRA.GravarOpcoes(tp, op);
                    Relacao.ServicoRA.Atualizar(db, tr, cfg);
                    Rel.AppendLine("        título da RA parcial: " + tp.Cells[0, 0].TextString);
                    Checar(tp.Cells[0, 0].TextString.Contains("V1A") && tp.Cells[1, 0].TextString == "ELEMENTO",
                        "RA parcial com o nome do detalhamento no título e coluna ELEMENTO (numeração global)");
                    op.NomeNoTitulo = false;
                    op.ColunaElemento = Core.RelacaoAco.ColunaElemento.Automatica;
                    Relacao.ServicoRA.GravarOpcoes(tp, op);
                    Relacao.ServicoRA.Atualizar(db, tr, cfg);
                    Checar(tp.Cells[1, 0].TextString != "ELEMENTO", "coluna ELEMENTO automática some na numeração global");
                }

                // poligonal fechada → estribo com ganchos no canto superior esquerdo
                const double Y0 = -5000; // longe das barras dos outros testes (procuradas por posição)
                var ret = Poli(true, (12000, Y0), (12060, Y0), (12060, Y0 + 80), (12000, Y0 + 80));
                // o gancho padrão da conversão é o último escolhido no ARMESTRIBO (config do usuário): fixa 90° aqui
                var ganchoUsuario = cfg.UltimoEstribo.Gancho;
                cfg.UltimoEstribo.Gancho = FormatoSegmento.Gancho90;
                try { Checar(Comandos.CriarBarra(tr, ret, cfg, estribo: true), "polilinha fechada convertida em estribo"); }
                finally { cfg.UltimoEstribo.Gancho = ganchoUsuario; }
                var de = XDataCad.LerBarra(ret);
                Rel.AppendLine("        estribo da poligonal: " + string.Join(" ", Enumerable.Range(0, ret.NumberOfVertices).Select(i => ret.GetPoint2dAt(i)).Select(p => FormattableString.Invariant($"({p.X - 12000:0.##}; {p.Y - Y0:0.##})"))));
                Checar(!ret.Closed && ret.NumberOfVertices == 9 && ret.GetPoint2dAt(2).GetDistanceTo(new Point2d(12000, Y0 + 80)) < 1e-6
                       && de.Estribo && ServicoEstribo.ComGanchos(ret, de) && de.Segmentos[0].Formato == FormatoSegmento.Gancho90,
                    "estribo: 9 vértices, ganchos de 90° no canto superior esquerdo");
                bool dentro(Point2d p) => p.X > 12000 && p.X < 12060 && p.Y > Y0 && p.Y < Y0 + 80;
                Checar(dentro(ret.GetPoint2dAt(0)) && dentro(ret.GetPoint2dAt(8)), "ganchos para dentro da poligonal");
                Checar(ServicoEstribo.Converter(tr, ret, de, cfg, gancho: FormatoSegmento.Gancho45) == null
                       && XDataCad.LerBarra(ret).Segmentos[0].Formato == FormatoSegmento.Gancho45, "troca do gancho para 45°");
                de = XDataCad.LerBarra(ret);
                ServicoEstribo.Converter(tr, ret, de, cfg, posicao: Core.Estribos.PosicaoGanchos.SuperiorDireito);
                Checar(ret.GetPoint2dAt(2).GetDistanceTo(new Point2d(12060, Y0 + 80)) < 1e-6 && ServicoEstribo.Posicao(ret) == Core.Estribos.PosicaoGanchos.SuperiorDireito,
                    "ganchos movidos para o canto superior direito");
                ServicoEstribo.Desfazer(tr, ret, XDataCad.LerBarra(ret));
                Checar(ret.Closed && ret.NumberOfVertices == 4, "Estribo = Não volta à poligonal fechada");

                // offset: para dentro (sem ponto) e para fora (ponto clicado)
                var forma = Poli(true, (12100, Y0), (12200, Y0), (12200, Y0 + 50), (12100, Y0 + 50));
                using (var dentroP = Comandos.Offset(forma, 5, null))
                using (var fora = Comandos.Offset(forma, 5, new Point3d(12300, Y0 + 25, 0)))
                    Checar(dentroP != null && fora != null && Math.Abs(Math.Abs(dentroP.Area) - 3600) < 1e-3 && Math.Abs(Math.Abs(fora.Area) - 6600) < 1e-3,
                        $"ARMOFFSET: 90×40 para dentro e 110×60 para fora ({dentroP?.Area:0.#}, {fora?.Area:0.#})");

                // faixa com escala própria (barra 1:50, faixa 1:25)
                var bf = Poli(false, (12400, Y0), (12700, Y0));
                ServicoBarra.Criar(tr, bf, new DadosArmadura { Bitola = 10, Quantidade = 1, Escala = 50 }, cfg);
                var fx = new DadosFaixa { Tipo = TipoFaixa.Ortogonal, Espacamento = 15, Escala = 25, P1X = 12400, P1Y = Y0 - 100, P2X = 12700, P2Y = Y0 - 100 };
                var fid = ServicoFaixa.Criar(tr, bf, fx, new Point3d(12550, Y0 - 120, 0), cfg);
                double cpu = Desenho.CmPorUnidade(db, cfg);
                var dim = (Dimension)tr.GetObject(fid, OpenMode.ForRead);
                var descF = Ent<MText>(tr, db, XDataCad.LerBarra(bf).HandleDescricao);
                Checar(Math.Abs(dim.Dimtxt - cfg.AlturaTexto(cpu, 25)) < 1e-9 && Math.Abs(descF.TextHeight - cfg.AlturaTexto(cpu, 50)) < 1e-9,
                    $"faixa 1:25 (texto {dim.Dimtxt:0.##}) numa barra 1:50 (texto {descF.TextHeight:0.##})");
                H["faixaEscala"] = XDataCad.Hex(fid);
                H["barraEscala"] = XDataCad.Hex(bf.ObjectId);

                // chamada sem linha de chamada (opção do ARMCHAMADA e da paleta)
                var ch = new DadosFaixa { Tipo = TipoFaixa.Chamada, Quantidade = 2, OcultarLinhas = true, Referencias = { (12500, Y0), (12600, Y0) } };
                var chId = ServicoFaixa.Criar(tr, bf, ch, new Point3d(12650, Y0 + 40, 0), cfg);
                var chEnt = (Entity)tr.GetObject(chId, OpenMode.ForWrite);
                Checar(ServicoFaixa.Ler(chEnt).Partes.Count == 0, "chamada sem linha de chamada: só o texto");
                var chDados = ServicoFaixa.Ler(chEnt);
                chDados.OcultarLinhas = false;
                XDataCad.Gravar(chEnt, chDados);
                ServicoBarra.Atualizar(tr, bf, cfg);
                Checar(ServicoFaixa.Ler(chEnt).Partes.Count == 2, "chamada: linhas de chamada voltam ao religar a opção");

                // template: o CAD desenha o que o template gera
                var tpl = new TemplateTeste();
                Core.Templates.CatalogoTemplates.Registrar(tpl);
                var res = tpl.Gerar(new Core.Templates.ValoresTemplate(tpl.Parametros), cfg);
                var ids = ComandosTemplate.Desenhar(tr, db, cfg, res, new Point3d(13000, Y0, 0));
                var dt = ids.Count == 1 ? XDataCad.LerBarra(tr.GetObject(ids[0], OpenMode.ForRead)) : null;
                Checar(dt != null && dt.Quantidade == 3 && dt.Segmentos[1].Formato == FormatoSegmento.Gancho90,
                    "template desenhado: 1 armadura (3 barras) com gancho");
                Core.Templates.CatalogoTemplates.Remover(tpl.Id);
                tr.Commit();
            }

            // paleta: faixa selecionada mostra as propriedades da faixa
            var faixas = Paleta.PaletaArmadura.LerFaixas(db, new[] { Id(db, H["faixaEscala"]) }, out bool soFaixas);
            Checar(soFaixas && faixas.Count == 1 && Math.Abs(faixas[0].Escala - 25) < 1e-9 && !faixas[0].EscalaDaBarra,
                "paleta: faixa com escala própria 1:25");

            // paleta "invisível" (ocultar automaticamente recolhida) também acompanha a seleção
            // (AutoRollUp/RolledUp não são implementados na API do GstarCAD: oculta a paleta)
            bool estava = Paleta.PaletaArmadura.Visivel;
            Paleta.PaletaArmadura.OcultarParaTeste();
            doc.Editor.SetImpliedSelection(new[] { Id(db, H["barraEscala"]) });
            Paleta.PaletaArmadura.Recarregar();
            var mostradas = Paleta.PaletaArmadura.Mostradas;
            Checar(!Paleta.PaletaArmadura.Visivel && mostradas.Count == 1 && mostradas[0].Id == Id(db, H["barraEscala"]),
                $"paleta oculta/recolhida também acompanha a seleção ({mostradas.Count})");
            doc.Editor.SetImpliedSelection(new ObjectId[0]);
            if (estava) Paleta.PaletaArmadura.Mostrar();

            // janelas novas montam sem erro (e o menu edita uma cópia)
            using (var f = new Paleta.FormConfiguracao(cfg, false, Plugin.CaminhoConfig))
            {
                new Paleta.ConfiguracaoEditavel(f.Editada).EscalaFaixa = 25;
                Checar(f.Editada.EscalaFaixa == 25 && !ReferenceEquals(f.Editada, cfg), "menu de configurações edita uma cópia");
            }
            using (new FormTemplate()) { }
            Checar(true, "janela de templates monta sem erro");
        }
        catch (Exception ex)
        {
            Checar(false, "novidades 2026-10-27: exceção " + ex);
        }
    }

    [Rt.CommandMethod("ARMTESTE5")]
    public void Fase5() => Comandos.Executar("ARMTESTE5", (doc, ed, db, cfg) =>
    {
        TestarRelacaoParcialEBloqueio(doc, db, cfg);
        TestarNovidades(doc, db, cfg);
        TestarReconhecimento(doc, db, cfg);

        // ----- fase 4: cordoalha, traspasse, treliça, tela -----
        TestarCordoalha(doc, db, cfg);
        using (var tr = db.TransactionManager.StartTransaction())
        using (Sincronizador.Suspender(doc))
        {
            var esp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            void Nova(string nome, double bitola, double x0, double y, double x1)
            {
                var p = new Polyline();
                p.SetDatabaseDefaults(db);
                p.AddVertexAt(0, new Point2d(x0, y), 0, 0, 0);
                p.AddVertexAt(1, new Point2d(x1, y), 0, 0, 0);
                esp.AppendEntity(p);
                tr.AddNewlyCreatedDBObject(p, true);
                ServicoBarra.Criar(tr, p, new DadosArmadura { Bitola = bitola, Quantidade = 2 }, cfg);
                H[nome] = XDataCad.Hex(p.ObjectId);
            }
            Nova("Z1", 10, 6000, 0, 7500);      // 1500: dividir no ponto 700
            Nova("Z2", 12.5, 6000, 200, 8500);  // 2500: divisão automática (3 peças)
            Nova("Z3", 10, 6000, 1500, 7000);   // 1000: esticada para 1500 com verificação de limite
            tr.Commit();
        }
        // o teste mexe nestes valores do usuário: guarda para restaurar no ARMTESTE6
        _restaurar = (cfg.Traspasse.VerificarComprimentoLimite, Plugin.Config.TrelicaCorrente, Plugin.Config.TelaCorrente);
        cfg.Traspasse.VerificarComprimentoLimite = true;
        string Pre(string h) => $"(sssetfirst nil (ssadd (handent \"{h}\")))\n";
        doc.SendStringToExecute(Pre(H["Z1"]) + "ARMDIVIDIR\n6700,0\n", true, false, true);
        doc.SendStringToExecute(Pre(H["Z2"]) + "ARMDIVIDIR\n\n", true, false, true);
        doc.SendStringToExecute("ARMTRELICA\nModelo\nTR 12645\n6000,500\n6300,500\n", true, false, true);
        doc.SendStringToExecute("ARMTELA\nModelo\nQ-196\n6000,700\n6245,1300\n", true, false, true);
        doc.SendStringToExecute("_.STRETCH _C 6995,1495 7005,1505\n\n0,0\n500,0\n", true, false, true);
        doc.SendStringToExecute("ARMRAATUALIZAR\n", true, false, true);
        // cota movida com o MOVE de verdade (o Sincronizador guarda o deslocamento)
        if (H.TryGetValue("recRetaCota", out var hCota))
        {
            doc.SendStringToExecute($"_.MOVE (handent \"{hCota}\")\n\n0,0\n0,8\n", true, false, true);
        }
        doc.SendStringToExecute("ARMTESTE6\n", true, false, true);
    }, suspender: false);

    private static string _zoomTabela;

    /// <summary>Detalhe de dobra na tabela (paleta), MOVE da tabela e lista de corte e dobra.</summary>
    /// <summary>
    /// ARMRECONHECER (2026-10-29): barras "de outro programa" em layers quaisquer, com o texto ao lado e as
    /// medidas dos trechos; e cotas movidas pelo usuário acompanhando a barra (ARMTEXTOPADRAO desfaz).
    /// </summary>
    private static void TestarReconhecimento(Document doc, Database db, Core.Configuracao.ConfiguracaoArmadura cfg)
    {
        try
        {
            var ids = new List<ObjectId>();
            ObjectId idL, idReta, idEst, idSolta;
            using (var tr = db.TransactionManager.StartTransaction())
            using (Sincronizador.Suspender(doc))
            {
                var esp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var layer = Desenho.GarantirLayer(db, tr, "PILARES_TERCEIROS", 3);
                ObjectId Add(Entity e) { esp.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); ids.Add(e.ObjectId); return e.ObjectId; }
                Polyline Pl(bool fechada, params (double x, double y)[] pts)
                {
                    var p = new Polyline();
                    p.SetDatabaseDefaults(db);
                    for (int i = 0; i < pts.Length; i++) p.AddVertexAt(i, new Point2d(pts[i].x, pts[i].y), 0, 0, 0);
                    p.Closed = fechada;
                    p.LayerId = layer;
                    return p;
                }
                DBText Txt(string s, double x, double y)
                {
                    var t = new DBText { TextString = s, Height = 2, Position = new Point3d(x, y, 0) };
                    t.SetDatabaseDefaults(db);
                    t.LayerId = layer;
                    return t;
                }
                // L como na imagem do usuário: 35 na vertical, 42 na horizontal, texto embaixo
                idL = Add(Pl(false, (20000, -7965), (20000, -8000), (20042, -8000)));
                Add(Txt("35", 19994, -7983));
                Add(Txt("42", 20019, -8004));
                Add(Txt("25 N0 %%c16.0 c/10 C=73", 20005, -8010));
                // barra reta desenhada como LINE (layer 0) com MText
                idReta = Add(new Line(new Point3d(20100, -8000, 0), new Point3d(20400, -8000, 0)));
                var mt = new MText { Contents = "{\\H2;4 N2 %%c10 C=300}", TextHeight = 2, Location = new Point3d(20250, -7997, 0), Attachment = AttachmentPoint.MiddleCenter };
                mt.SetDatabaseDefaults(db);
                Add(mt);
                // retângulo fechado com texto de estribo
                idEst = Add(Pl(true, (20500, -8000), (20520, -8000), (20520, -7960), (20500, -7960)));
                Add(Txt("N3 22 %%c5.0 c/15 C=114", 20495, -8006));
                // polilinha sem texto e texto sem barra
                idSolta = Add(Pl(false, (20700, -8000), (20800, -8000)));
                Add(Txt("10 N9 %%c8.0 C=100", 21000, -9000));
                tr.Commit();
            }

            var res = ComandosReconhecimento.Reconhecer(db, ids, cfg, true, () => true);
            Rel.AppendLine($"        reconhecimento: {res?.Barras.Count} barras, {res?.Estribos} estribo(s), {res?.CotasAproveitadas} cotas, " +
                           $"{res?.CurvasSemTexto.Count} curva(s) sem texto, textos sem barra: {string.Join(" | ", res?.TextosSemBarra ?? new List<string>())}");
            foreach (var a in res?.Avisos ?? new List<string>()) Rel.AppendLine("        aviso: " + a);
            Checar(res != null && res.Barras.Count == 3 && res.Estribos == 1, "reconhecer: 3 barras (L, LINE, estribo)");
            Checar(res != null && res.CurvasSemTexto.Count == 1 && res.CurvasSemTexto[0] == idSolta, "reconhecer: polilinha sem texto fica de fora");
            Checar(res != null && res.TextosSemBarra.Count == 1 && res.TextosSemBarra[0].StartsWith("10 N9"), "reconhecer: texto sem barra informado");
            Checar(res != null && res.CotasAproveitadas == 2, $"reconhecer: medidas 35 e 42 viram cotas ({res?.CotasAproveitadas})");

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pl = (Polyline)tr.GetObject(idL, OpenMode.ForRead);
                var d = XDataCad.LerBarra(pl);
                Checar(d != null && d.Bitola == 16 && d.Quantidade == 25 && d.Espacamento == 10 && d.Escala == 10,
                    $"reconhecer: L = 25 ø16 c/10, escala 1:10 pelo texto ({d?.Quantidade} ø{d?.Bitola} c/{d?.Espacamento} 1:{d?.Escala})");
                var desc = Ent<MText>(tr, db, d.HandleDescricao);
                Checar(desc != null && desc.Location.DistanceTo(new Point3d(20005 + 0, -8010, 0)) < 20, $"reconhecer: descrição no lugar do texto ({desc?.Location})");
                var c0 = Ent<DBText>(tr, db, d.Segmentos[0].HandleCota);
                var c1 = Ent<DBText>(tr, db, d.Segmentos[1].HandleCota);
                Checar(c0 != null && c0.TextString == "35" && d.Segmentos[0].CotaMovida && c1 != null && c1.TextString == "42",
                    $"reconhecer: cotas 35/42 no lugar das medidas ({c0?.AlignmentPoint}, {c1?.AlignmentPoint})");
                Checar(pl.Layer == cfg.LayerBarra, $"reconhecer: barra de layer qualquer vai para {cfg.LayerBarra}");
                int restantes = ids.Count(id => !id.IsErased && tr.GetObject(id, OpenMode.ForRead) is DBText or MText);
                Checar(restantes == 1, $"reconhecer: textos originais apagados (sobrou só o sem barra: {restantes})");
                Checar(idReta.IsErased, "reconhecer: LINE trocada por polilinha");
                var reta = res.Barras.Select(id => (Polyline)tr.GetObject(id, OpenMode.ForRead)).Select(p => XDataCad.LerBarra(p)).FirstOrDefault(x => x.Numero == 2);
                Checar(reta != null && reta.Bitola == 10 && reta.Quantidade == 4, "reconhecer: LINE = 4 N2 ø10");
                if (reta != null)
                {
                    var plReta = res.Barras.Select(id => (Polyline)tr.GetObject(id, OpenMode.ForRead)).First(p => XDataCad.LerBarra(p).Numero == 2);
                    H["recReta"] = XDataCad.Hex(plReta.ObjectId);
                    H["recRetaCota"] = reta.Segmentos[0].HandleCota;
                    Antes["recRetaCotaY"] = Ent<DBText>(tr, db, reta.Segmentos[0].HandleCota).AlignmentPoint.Y;
                }
                var est = XDataCad.LerBarra(tr.GetObject(idEst, OpenMode.ForRead));
                Checar(est != null && est.Estribo && est.Bitola == 5 && est.Quantidade == 22 && est.Espacamento == 15, "reconhecer: estribo N3 22 ø5 c/15");
                tr.Commit();
            }

            // cota movida acompanha a barra; ARMTEXTOPADRAO devolve ao padrão
            using (var tr = db.TransactionManager.StartTransaction())
            using (Sincronizador.Suspender(doc))
            {
                var pl = (Polyline)tr.GetObject(idL, OpenMode.ForWrite);
                var d = XDataCad.LerBarra(pl);
                var c1 = Ent<DBText>(tr, db, d.Segmentos[1].HandleCota);
                c1.UpgradeOpen();
                c1.AlignmentPoint = c1.AlignmentPoint + new Vector3d(10, -6, 0); // o usuário arrasta a cota
                c1.AdjustAlignment(db);
                ServicoBarra.RegistrarMovimentoCota(tr, c1, cfg);
                var antes = c1.AlignmentPoint;
                double meioAntes = (pl.GetPoint2dAt(1).X + pl.GetPoint2dAt(2).X) / 2;
                pl.SetPointAt(2, new Point2d(20062, -8000)); // trecho horizontal esticado para 62
                ServicoBarra.Atualizar(tr, pl, cfg);
                double meioDepois = (pl.GetPoint2dAt(1).X + pl.GetPoint2dAt(2).X) / 2;
                var depois = c1.AlignmentPoint;
                Checar(c1.TextString == "62" && Math.Abs(depois.X - antes.X - (meioDepois - meioAntes)) < 1e-6 && Math.Abs(depois.Y - antes.Y) < 1e-6,
                    $"cota movida acompanha o trecho esticado ({antes} → {depois})");
                ServicoBarra.RestaurarPosicoes(tr, pl, cfg);
                d = XDataCad.LerBarra(pl);
                Checar(!d.Segmentos[0].CotaMovida && !d.Segmentos[1].CotaMovida && Math.Abs(c1.AlignmentPoint.X - meioDepois) < 1e-6,
                    $"ARMTEXTOPADRAO: cotas de volta ao meio do trecho ({c1.AlignmentPoint})");
                tr.Commit();
            }
        }
        catch (Exception ex)
        {
            Checar(false, "reconhecimento: exceção " + ex);
        }
    }

    private static void TestarDetalheECorteDobra(Document doc, Database db, Core.Configuracao.ConfiguracaoArmadura cfg)
    {
        try
        {
            var tid = Id(db, H["tabelaGlobal"]);
            int colsAntes;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                colsAntes = ((Table)tr.GetObject(tid, OpenMode.ForRead)).Columns.Count;
                tr.Commit();
            }
            var props = Paleta.PaletaArmadura.LerTabelas(db, new[] { tid });
            props[0].DetalheDobra = true;
            Paleta.PaletaArmadura.AplicarTabelas(doc, props);

            Core.RelacaoAco.OpcoesTabelaRA op;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var t = (Table)tr.GetObject(tid, OpenMode.ForRead);
                op = Relacao.ServicoRA.Opcoes(t, cfg);
                int col = t.Columns.Count - 1;
                Checar(t.Columns.Count == colsAntes + 1 && t.Cells[1, col].TextString.Replace("\\P", " ").StartsWith("DETALHE DE DOBRA"),
                    "detalhe de dobra: coluna nova no fim da relação");
                var partes = op.Partes.Select(h => Ent<Entity>(tr, db, h)).Where(e => e != null).ToList();
                var formas = partes.OfType<Polyline>().ToList();
                int linhasRelacao = LinhasRelacao(t).Count;
                Rel.AppendLine($"        detalhe de dobra: {formas.Count} formas, {partes.OfType<DBText>().Count()} medidas, {linhasRelacao} posições");
                Checar(formas.Count >= linhasRelacao - 1 && formas.Count <= linhasRelacao, "uma forma por posição (treliça/tela inclusas quando têm forma)");
                double xCol = t.Position.X + Enumerable.Range(0, col).Sum(c => t.ColumnWidth(c));
                Checar(formas.All(f => f.GeometricExtents.MinPoint.X >= xCol - 1e-6 && f.GeometricExtents.MaxPoint.X <= xCol + t.ColumnWidth(col) + 1e-6),
                    "formas desenhadas dentro da coluna do detalhe");
                // cada forma dentro da célula da SUA linha: a linha da posição N tem o N na coluna "N"
                int colN = t.Columns.Count == 8 ? 2 : 1;
                bool naLinha = true;
                for (int r = 2; r < t.Rows.Count; r++)
                {
                    if (!int.TryParse(t.Cells[r, colN].TextString.TrimEnd('*'), out _)) continue;
                    using var cantos = new Point3dCollection();
                    t.GetCellExtents(r, col, true, cantos);
                    double y0 = cantos.Cast<Point3d>().Min(p => p.Y), y1 = cantos.Cast<Point3d>().Max(p => p.Y);
                    naLinha &= formas.Count(f => f.GeometricExtents.MinPoint.Y >= y0 - 1e-6 && f.GeometricExtents.MaxPoint.Y <= y1 + 1e-6) == 1;
                }
                Checar(naLinha, "cada forma fica dentro da célula da linha da sua posição");
                var ext = t.GeometricExtents;
                _zoomTabela = $"{ext.MinPoint.X - 5:0.##},{ext.MinPoint.Y - 5:0.##} {ext.MaxPoint.X + 5:0.##},{ext.MaxPoint.Y + 5:0.##}".Replace(',', '.').Replace(". ", ", ");
                _zoomTabela = $"{(ext.MinPoint.X - 5).ToString(System.Globalization.CultureInfo.InvariantCulture)},{(ext.MinPoint.Y - 5).ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                              $"{(ext.MaxPoint.X + 5).ToString(System.Globalization.CultureInfo.InvariantCulture)},{(ext.MaxPoint.Y + 5).ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                tr.Commit();
            }

            // MOVE da tabela: os detalhes acompanham
            using (var tr = db.TransactionManager.StartTransaction())
            using (Sincronizador.Suspender(doc))
            {
                var t = (Table)tr.GetObject(tid, OpenMode.ForWrite);
                var f0 = Ent<Polyline>(tr, db, op.Partes.First(h => Ent<Polyline>(tr, db, h) != null));
                var antes = f0.GetPoint2dAt(0);
                t.TransformBy(Matrix3d.Displacement(new Vector3d(0, -20, 0)));
                Relacao.ServicoRA.AcompanharMovimento(tr, t, cfg);
                Checar(f0.GetPoint2dAt(0).GetDistanceTo(antes + new Vector2d(0, -20)) < 1e-6, "MOVE da tabela leva os detalhes de dobra junto");
                t.TransformBy(Matrix3d.Displacement(new Vector3d(0, 20, 0)));
                Relacao.ServicoRA.AcompanharMovimento(tr, t, cfg);
                tr.Commit();
            }

            // lista de corte e dobra
            var saida = Environment.GetEnvironmentVariable("ARM_AUTOTESTE_SAIDA");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var r = Relacao.ServicoRA.ResultadoDoDesenho(db, tr, cfg);
                var csv = Core.RelacaoAco.ExportadorCorteDobra.Csv(r, cfg);
                var html = Core.RelacaoAco.ExportadorCorteDobra.Html(r, cfg, "Autoteste");
                int posicoes = r.Linhas.Count(l => !l.EhTrelica);
                Checar(csv.Split('\n').Count(l => l.Trim().Length > 0) == posicoes + 1, $"corte e dobra: {posicoes} posições no CSV");
                Checar(html.Contains("<svg") && html.Contains("CP-190"), "corte e dobra: HTML com desenhos (inclui a cordoalha)");
                if (!string.IsNullOrEmpty(saida))
                {
                    var pasta = Path.GetDirectoryName(saida);
                    File.WriteAllText(Path.Combine(pasta, "autoteste_corte_dobra.csv"), csv, new UTF8Encoding(true));
                    File.WriteAllText(Path.Combine(pasta, "autoteste_corte_dobra.html"), html, new UTF8Encoding(false));
                }
                tr.Commit();
            }
        }
        catch (Exception ex) { Checar(false, "detalhe/corte e dobra: exceção " + ex); }
    }

    private static void TestarCordoalha(Document doc, Database db, Core.Configuracao.ConfiguracaoArmadura cfg)
    {
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            using var s = Sincronizador.Suspender(doc);
            var esp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var p = new Polyline();
            p.SetDatabaseDefaults(db);
            p.AddVertexAt(0, new Point2d(6000, -500), 0, 0, 0);
            p.AddVertexAt(1, new Point2d(7500, -500), 0, 0, 0);
            esp.AppendEntity(p);
            tr.AddNewlyCreatedDBObject(p, true);
            ServicoBarra.Criar(tr, p, new DadosArmadura { Bitola = 12.7, Aco = "CP-190", Quantidade = 6 }, cfg);
            H["CP"] = XDataCad.Hex(p.ObjectId);
            var d = XDataCad.LerBarra(p);
            var desc = Ent<MText>(tr, db, d.HandleDescricao);
            Checar(desc != null && desc.Contents.Contains("12.7 CP-190") && desc.Contents.EndsWith("C=1500"),
                $"cordoalha: descrição com o aço ({desc?.Contents})");
            Checar(ServicoBarra.AlterarFormatoSegmento(p, d, 0, FormatoSegmento.Gancho90, 1) != null, "cordoalha: gancho recusado");
            bool semTraspasse = false;
            try { ServicoTraspasse.Traspasse(d, cfg); } catch (InvalidOperationException) { semTraspasse = true; }
            Checar(semTraspasse, "cordoalha: sem traspasse");
            tr.Commit();
        }
        catch (Exception ex) { Checar(false, "cordoalha: exceção " + ex); }
    }

    [Rt.CommandMethod("ARMTESTE6")]
    public void Fase6() => Comandos.Executar("ARMTESTE6", (doc, ed, db, cfg) =>
    {
        cfg.Traspasse.VerificarComprimentoLimite = _restaurar.limite;
        Plugin.Config.TrelicaCorrente = _restaurar.trelica;
        Plugin.Config.TelaCorrente = _restaurar.tela;
        Plugin.SalvarConfig();
        if (H.TryGetValue("recReta", out var hReta))
        {
            using var trc = db.TransactionManager.StartTransaction();
            var pr = (Polyline)trc.GetObject(Id(db, hReta), OpenMode.ForRead);
            var dr = XDataCad.LerBarra(pr);
            var cr = Ent<DBText>(trc, db, dr.Segmentos[0].HandleCota);
            double meio = (pr.GetPoint2dAt(0).X + pr.GetPoint2dAt(1).X) / 2;
            Rel.AppendLine($"        cota movida pelo MOVE: SM={dr.Segmentos[0].CotaMovida} dx={dr.Segmentos[0].CotaDx:0.###} dy={dr.Segmentos[0].CotaDy:0.###}, texto {cr?.TextString} em {cr?.AlignmentPoint}");
            Checar(dr.Segmentos[0].CotaMovida && Math.Abs(Math.Abs(dr.Segmentos[0].CotaDy) - 8) < 1e-6,
                "MOVE da cota: deslocamento guardado pelo Sincronizador");
            Checar(cr != null && cr.TextString == "300" && Math.Abs(cr.AlignmentPoint.X - meio) < 1e-6
                   && Math.Abs(cr.AlignmentPoint.Y - ((double)Antes["recRetaCotaY"] + 8)) < 1e-6,
                "MOVE da cota: continua no meio do trecho, 8 acima do padrão");
            trc.Commit();
        }
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var barras = Desenho.Entidades<Polyline>(db, tr).Select(i => (Polyline)tr.GetObject(i, OpenMode.ForRead))
                .Select(p => (p, d: XDataCad.LerBarra(p))).Where(x => x.d != null).ToList();
            double Comp(Polyline p) => GeometriaBarra.Ler(p, 1).Sum(s => s.Comprimento);
            List<double> Pecas(double y) => barras.Where(x => x.p.GetPoint2dAt(0).X >= 5990 && Math.Abs(x.p.GetPoint2dAt(0).Y - y) < 2)
                .Select(x => Math.Round(Comp(x.p), 2)).OrderBy(c => c).ToList();

            double l10 = cfg.Traspasse.Comprimento(10, "CA-50"), l125 = cfg.Traspasse.Comprimento(12.5, "CA-50");
            var z1 = Pecas(0);
            Rel.AppendLine($"        ARMDIVIDIR no ponto: {string.Join(" + ", z1)} (traspasse {l10})");
            Checar(Id(db, H["Z1"]).IsErased && z1.SequenceEqual(new[] { 700 + l10 / 2, 800 + l10 / 2 }),
                "ARMDIVIDIR no ponto 700: 2 barras sobrepostas no traspasse");
            var z2 = Pecas(200);
            Rel.AppendLine($"        ARMDIVIDIR automático: {string.Join(" + ", z2)} (traspasse {l125})");
            Checar(z2.Count == 3 && z2.All(c => c <= 1200 + 1e-6) && Math.Abs(z2.Sum() - (2500 + 2 * l125)) < 1e-6,
                "ARMDIVIDIR automático: 3 peças de até 12 m, somando 2500 + 2 traspasses");
            var z3 = Pecas(1500);
            Checar(Id(db, H["Z3"]).IsErased && z3.Count == 2 && z3.All(c => c <= 1200), $"verificação de limite: esticada para 1500 virou 2 peças ({string.Join(" + ", z3)})");

            var tre = barras.FirstOrDefault(x => x.d.EhTrelica);
            Checar(tre.p != null && tre.d.Trelica == "TR 12645", "ARMTRELICA com o modelo escolhido");
            if (tre.p != null)
            {
                var partes = tre.d.PartesDesenho.Select(h => Ent<Polyline>(tr, db, h)).Where(e => e != null).ToList();
                Checar(partes.Count == 2 && partes.Any(e => e.NumberOfVertices == 31) && partes.Any(e => Math.Abs(e.GetPoint2dAt(0).Y - 512) < 1e-6),
                    "treliça: banzo superior (H = 12) e sinusoide (31 vértices) gerados");
                var desc = Ent<MText>(tr, db, tre.d.HandleDescricao);
                Rel.AppendLine("        treliça: " + desc?.Contents);
                Checar(desc != null && desc.Contents.Contains("TR 12645") && desc.Contents.EndsWith("C=300"), "descrição da treliça");
            }
            var tela = barras.FirstOrDefault(x => x.d.EhTela);
            Checar(tela.p != null && tela.d.Tela == "Q-196" && tela.d.Numero == 0 && tela.d.PartesDesenho.Count == 1
                   && tela.d.Segmentos.Count(s => !string.IsNullOrEmpty(s.HandleCota)) == 2, "ARMTELA: painel Q-196 sem número, com diagonal e as 2 dimensões cotadas");
            if (tela.p != null)
            {
                var dTela = Ent<MText>(tr, db, tela.d.HandleDescricao);
                double angDiag = Math.Atan2(600, 245);
                Rel.AppendLine($"        tela: \"{dTela?.Contents}\" girada {dTela?.Rotation * 180 / Math.PI:0.#}°");
                Checar(dTela != null && Math.Abs(dTela.Rotation - angDiag) < 1e-6, "tela: texto na diagonal do painel");
                // distribuição do aço: Q-196 c/10 x c/10 em 245 × 600 → 25 fios ao longo do comprimento + 60 transversais
                var pt = (Polyline)tr.GetObject(tela.p.ObjectId, OpenMode.ForWrite);
                var dt = XDataCad.LerBarra(pt);
                dt.TelaDistribuicao = true;
                XDataCad.Gravar(pt, dt);
                ServicoBarra.Atualizar(tr, pt, cfg);
                int partes = XDataCad.LerBarra(pt).PartesDesenho.Count;
                Checar(partes == 1 + 25 + 60, $"tela: distribuição com 25 + 60 fios + diagonal ({partes})");
                dt = XDataCad.LerBarra(pt);
                dt.TelaDistribuicao = false;
                dt.TelaTextoDiagonal = false;
                XDataCad.Gravar(pt, dt);
                ServicoBarra.Atualizar(tr, pt, cfg);
                Checar(XDataCad.LerBarra(pt).PartesDesenho.Count == 1 && Math.Abs(Ent<MText>(tr, db, tela.d.HandleDescricao).Rotation) < 1e-9,
                    "tela: sem distribuição e texto de volta na horizontal");
                // para a imagem do autoteste: distribuição e texto na diagonal
                dt = XDataCad.LerBarra(pt);
                dt.TelaDistribuicao = true;
                dt.TelaTextoDiagonal = true;
                XDataCad.Gravar(pt, dt);
                ServicoBarra.Atualizar(tr, pt, cfg);
            }

            // telas identificadas (padrão do escritório): T1/T2 - 2x Q-92 + dimensões na diagonal interrompida
            {
                var cfgT = cfg.Clonar();
                cfgT.TelaIdentificada = true;
                ObjectId Painel(double x0, double y0, double x1, double y1)
                {
                    var id = ComandosFase4.CriarTela(tr, db, cfgT, new Point2d(x0, y0), new Point2d(x1, y1), "Q-92", 1);
                    var p = (Polyline)tr.GetObject(id, OpenMode.ForWrite);
                    var dd = XDataCad.LerBarra(p);
                    dd.Repeticoes = 2; // 2 camadas
                    dd.Escala = 50;    // planta da laje
                    XDataCad.Gravar(p, dd);
                    return id;
                }
                var t1 = Painel(15000, -8000, 15348, -7755);   // 245 x 348
                var t2 = Painel(15000, -7755, 15348, -7542);   // 213 x 348
                var t3 = Painel(15400, -8000, 15748, -7755);   // igual ao T1
                Relacao.ServicoRA.Atualizar(db, tr, cfgT);
                var d1 = XDataCad.LerBarra(tr.GetObject(t1, OpenMode.ForRead));
                var d2 = XDataCad.LerBarra(tr.GetObject(t2, OpenMode.ForRead));
                var d3 = XDataCad.LerBarra(tr.GetObject(t3, OpenMode.ForRead));
                var x1 = Ent<MText>(tr, db, d1.HandleDescricao);
                var x2 = Ent<MText>(tr, db, d2.HandleDescricao);
                Rel.AppendLine($"        telas identificadas: \"{x1?.Contents}\" / \"{x2?.Contents}\"");
                Checar(d1.NumeroTela == 1 && d2.NumeroTela == 2 && d3.NumeroTela == 1,
                    $"tela identificada: T1, T2 e o painel igual ao T1 também T1 ({d1.NumeroTela}, {d2.NumeroTela}, {d3.NumeroTela})");
                Checar(x1?.Contents == "T1 - 2x Q-92\\P245 x 348" && x2?.Contents == "T2 - 2x Q-92\\P213 x 348",
                    "tela identificada: \"T1 - 2x Q-92\" + \"245 x 348\"");
                Checar(d1.PartesDesenho.Count == 2 && d1.Segmentos.All(s => string.IsNullOrEmpty(s.HandleCota)),
                    $"tela identificada: diagonal interrompida no texto (2 trechos) e sem cotas ({d1.PartesDesenho.Count})");
                var meio = new Point3d(15174, -7877.5, 0);
                Checar(x1 != null && x1.Location.DistanceTo(meio) < 1e-6, "tela identificada: texto centrado na diagonal");
            }

            var tg = Desenho.Entidades<Table>(db, tr).Select(i => (Table)tr.GetObject(i, OpenMode.ForRead))
                .First(t => Relacao.ServicoRA.Opcoes(t, cfg)?.Parcial == false);
            var txt = TextoTabela(tg);
            Checar(txt.Contains("TR 12645|") && txt.Contains("PESO TOTAL TRELIÇAS"), "relação: treliça por modelo e peso total das treliças");
            Checar(txt.Contains("Q-196") && txt.Contains("14.70 m²"), "resumo: tela Q-196 com 14,70 m²");
            Checar(txt.Contains("PESO TOTAL CP-190"), "resumo: peso total da cordoalha CP-190");
            H["tabelaGlobal"] = XDataCad.Hex(tg.ObjectId);
            tr.Commit();
        }
        TestarDetalheECorteDobra(doc, db, cfg);

        // configuração gravada no desenho: prefixo "P" vale só para este DWG
        // configuração gravada no desenho: prefixo "P" vale só para este DWG
        var propria = cfg.Clonar();
        propria.PrefixoPosicao = "P";
        using (var tr = db.TransactionManager.StartTransaction())
        {
            ConfigDesenho.Gravar(db, tr, propria);
            Relacao.ServicoRA.Atualizar(db, tr, ConfigDesenho.Obter(db));
            var desc = Ent<MText>(tr, db, Barra(tr, db, "F").HandleDescricao);
            Checar(desc != null && desc.Contents.Contains(" P"), $"configuração do desenho aplicada ({desc?.Contents})");
            tr.Commit();
        }
        Checar(Plugin.Config.PrefixoPosicao == "N", "configuração do usuário intacta (prefixo N)");
        TestarTemplateBase(doc, db, ConfigDesenho.Obter(db));
        TestarTemplatePilar(doc, db, ConfigDesenho.Obter(db));

        var saida = Environment.GetEnvironmentVariable("ARM_AUTOTESTE_SAIDA");
        if (!string.IsNullOrEmpty(saida))
        {
            db.SaveAs(saida, DwgVersion.Current);
            var lida = ConfigDesenho.LerDeArquivo(saida);
            Checar(lida?.PrefixoPosicao == "P", "configuração do desenho salva no DWG e relida de arquivo");
        }
        Gravar();
        ed.WriteMessage($"\nAutoteste: {_ok} ok, {_falhas} falha(s). Relatório: {Arquivo}");
        if (!string.IsNullOrEmpty(saida))
        {
            doc.SendStringToExecute("_.ZOOM _E\n", true, false, true);
            // imagem para conferência visual (PNGOUT: Enter na seleção = desenho todo).
            // FILEDIA=0 só durante o PNGOUT (senão abre o diálogo); ARMTESTEFIM devolve o valor original.
            _filedia = CadApp.GetSystemVariable("FILEDIA");
            CadApp.SetSystemVariable("FILEDIA", 0);
            // sem aspas: o prompt de arquivo do GstarCAD não as aceita (o caminho não pode ter espaços)
            doc.SendStringToExecute($"_.PNGOUT {Path.ChangeExtension(saida, ".png")}\n\n", true, false, true);
            // detalhe das faixas de H (corte, chamada, indicação, cópia)
            doc.SendStringToExecute("_.ZOOM _W -20,-450 340,-250\n", true, false, true);
            doc.SendStringToExecute($"_.PNGOUT {Path.Combine(Path.GetDirectoryName(saida), "autoteste_faixas.png")}\n\n", true, false, true);
            // detalhe do estribo: seção, rebatido e faixa
            doc.SendStringToExecute("_.ZOOM _W 1990,-320 2260,15\n", true, false, true);
            doc.SendStringToExecute($"_.PNGOUT {Path.Combine(Path.GetDirectoryName(saida), "autoteste_estribo.png")}\n\n", true, false, true);
            // tabela com detalhe de dobra
            if (_zoomTabela != null)
            {
                doc.SendStringToExecute($"_.ZOOM _W {_zoomTabela}\n", true, false, true);
                doc.SendStringToExecute($"_.PNGOUT {Path.Combine(Path.GetDirectoryName(saida), "autoteste_tabela_dobra.png")}\n\n", true, false, true);
            }
            // detalhes da fase 4: treliça; traspasse (barras divididas) e tela
            doc.SendStringToExecute("_.ZOOM _W 5985,470 6315,560\n", true, false, true);
            doc.SendStringToExecute($"_.PNGOUT {Path.Combine(Path.GetDirectoryName(saida), "autoteste_trelica.png")}\n\n", true, false, true);
            doc.SendStringToExecute("_.ZOOM _W 5980,-40 7600,1340\n", true, false, true);
            doc.SendStringToExecute($"_.PNGOUT {Path.Combine(Path.GetDirectoryName(saida), "autoteste_fase4.png")}\n\n", true, false, true);
            // telas identificadas (T1, T2)
            doc.SendStringToExecute("_.ZOOM _W 14980,-8020 15770,-7520\n", true, false, true);
            doc.SendStringToExecute($"_.PNGOUT {Path.Combine(Path.GetDirectoryName(saida), "autoteste_telas_T.png")}\n\n", true, false, true);
            // detalhe da barra desenhada pelo cobrimento
            doc.SendStringToExecute("_.ZOOM _W 3985,-15 4045,75\n", true, false, true);
            doc.SendStringToExecute($"_.PNGOUT {Path.Combine(Path.GetDirectoryName(saida), "autoteste_cobrimento.png")}\n\n", true, false, true);
            // templates: base circular e retangular
            foreach (var (nome, janela) in _zoomBases)
            {
                doc.SendStringToExecute($"_.ZOOM _W {janela}\n", true, false, true);
                doc.SendStringToExecute($"_.PNGOUT {Path.Combine(Path.GetDirectoryName(saida), "autoteste_" + nome + ".png")}\n\n", true, false, true);
            }
            // EXPLODE de uma 2ª inserção do detalhamento do pilar: as chamadas têm de ficar no lugar (âncoras das faixas)
            if (_pilarExplodir != null)
            {
                // o EXPLODE do GstarCAD pega UM objeto e termina: um Enter só (o 2º repetia o comando e travava o roteiro)
                doc.SendStringToExecute($"_.EXPLODE (handent \"{_pilarExplodir}\")\n", true, false, true);
                doc.SendStringToExecute("ARMTESTEEXPLODIDO\n", true, false, true);
            }
            // restaura pela linha de comando (a restauração via API em ARMTESTEFIM nem sempre chega ao registro)
            doc.SendStringToExecute($"FILEDIA {Convert.ToInt32(_filedia ?? 1)}\n", true, false, true);
            doc.SendStringToExecute("ARMTESTEFIM\n", true, false, true);
        }
    }, suspender: false);

    private static readonly List<(string nome, string janela)> _zoomBases = new List<(string, string)>();
    private static string _pilarExplodir;
    private static Point3d _pilarExplodirEm;

    /// <summary>Depois do EXPLODE da 2ª inserção do pilar: toda linha de chamada sai de perto do seu texto (região da inserção).</summary>
    [Rt.CommandMethod("ARMTESTEEXPLODIDO")]
    public void Explodido() => Comandos.Executar("ARMTESTEEXPLODIDO", (doc, ed, db, cfg) =>
    {
        using var tr = db.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        var p = _pilarExplodirEm;
        bool Dentro(Point3d q) => q.X > p.X - 600 && q.X < p.X + 9000 && q.Y > p.Y - 3500 && q.Y < p.Y + 4000;
        int ok = 0, fora = 0;
        foreach (ObjectId id in ms)
        {
            if (!(tr.GetObject(id, OpenMode.ForRead) is Line ln) || XDataCad.Tipo(ln) != TipoParte.Chamada) continue;
            if (!Dentro(ln.EndPoint)) continue; // só as do pilar explodido
            if (Dentro(ln.StartPoint) && ln.Length < 500) ok++; else fora++;
        }
        Rel.AppendLine($"        EXPLODE do pilar: {ok} linhas de chamada no lugar, {fora} fora");
        // diagnóstico: as faixas de texto explodidas apontam barras do modelo ou da definição do bloco?
        int noModelo = 0, noBloco = 0, comAncora = 0, faixasTexto = 0;
        string exemplo = "";
        foreach (ObjectId id in ms)
        {
            if (!(tr.GetObject(id, OpenMode.ForRead) is MText mt) || XDataCad.Tipo(mt) != TipoParte.Faixa || !Dentro(mt.Location)) continue;
            var f = Armadura.Cad.Barra.ServicoFaixa.Ler(mt);
            faixasTexto++;
            if (f.TemAncora) comAncora++;
            var bid = XDataCad.IdDoHandle(db, f.HandleBarra);
            if (!bid.IsNull && tr.GetObject(bid, OpenMode.ForRead) is Entity be) { if (be.OwnerId == ms.ObjectId) noModelo++; else noBloco++; }
            if (exemplo.Length == 0 && f.Referencias.Count > 0)
                exemplo = FormattableString.Invariant($"texto ({mt.Location.X:0},{mt.Location.Y:0}) âncora ({f.AncoraX:0},{f.AncoraY:0}) ref ({f.Referencias[0].X:0},{f.Referencias[0].Y:0})");
        }
        Rel.AppendLine($"        faixas de texto explodidas: {faixasTexto} (com âncora {comAncora}); barra no modelo {noModelo}, na definição do bloco {noBloco}; {exemplo}");
        var todas = ms.Cast<ObjectId>().Select(i => tr.GetObject(i, OpenMode.ForRead)).OfType<MText>().Where(m => XDataCad.Tipo(m) == TipoParte.Faixa).ToList();
        var foraReg = todas.Where(m => !Dentro(m.Location) && m.Location.Y < -60000).Take(5)
            .Select(m => FormattableString.Invariant($"({m.Location.X - p.X:0},{m.Location.Y - p.Y:0})"));
        Rel.AppendLine($"        faixas de texto no modelo: {todas.Count}; perto do pilar mas fora da região: " + string.Join(" ", foraReg));
        var btP = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        if (btP.Has("P61 TESTE"))
        {
            var def = (BlockTableRecord)tr.GetObject(btP["P61 TESTE"], OpenMode.ForRead);
            var tipos = def.Cast<ObjectId>().Select(i => tr.GetObject(i, OpenMode.ForRead)).OfType<Entity>()
                .GroupBy(en => XDataCad.Tipo(en) + ":" + en.GetType().Name).Select(g => $"{g.Key}={g.Count()}");
            Rel.AppendLine("        na definição do bloco: " + string.Join(", ", tipos));
        }
        Checar(ok > 10 && fora == 0, "EXPLODE do detalhamento do pilar: chamadas continuam apontando as barras");
        tr.Commit();
        Gravar(); // roda depois do relatório principal: grava de novo
    });

    /// <summary>Template da base de reservatório (circular com VAR e retangular), desenhado como detalhamento.</summary>
    /// <summary>Template do pilar pré-moldado, etapa 1 (forma): P61 desenhado como detalhamento; PNG para conferir.</summary>
    private static void TestarTemplatePilar(Document doc, Database db, Core.Configuracao.ConfiguracaoArmadura cfg)
    {
        try
        {
            var t = new Core.Templates.PilarPreMoldado.TemplatePilarPreMoldado();
            var v = new Core.Templates.ValoresTemplate(t.Parametros);
            v["Nome"] = "P61 TESTE";
            var r = t.Gerar(v, cfg);
            var origem = new Point3d(30000, -70000, 0);
            using (Sincronizador.Suspender(doc))
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var idsArm = ComandosTemplate.Desenhar(tr, db, cfg, r, origem);
                tr.Commit();
                Checar(idsArm.Count == r.Barras.Count, $"pilar: {idsArm.Count} armaduras desenhadas ({r.Barras.Count} no template)");
            }
            foreach (var a in r.Avisos) Rel.AppendLine("        aviso do pilar: " + a);
            // regiões para conferir no PNG: elevação das armaduras (topo e base) e as seções
            var sec1 = r.Textos.FirstOrDefault(x => x.Texto == "SEÇÃO 1-1 - ARMADURA");
            var longX = r.Barras.Where(b => b.Nome.StartsWith("long")).Select(b => b.Vertices[0].X).DefaultIfEmpty(0).Min();
            if (sec1 != null)
                _zoomBases.Add(("pilar_p61_secoes", FormattableString.Invariant(
                    $"{origem.X + sec1.X - 20:0},{origem.Y + sec1.Y - 520:0} {origem.X + sec1.X + 260:0},{origem.Y + sec1.Y + 30:0}")));
            var detCons = r.Textos.FirstOrDefault(x => x.Texto.StartsWith("ARMADURAS CONSOLO"));
            if (detCons != null)
                _zoomBases.Add(("pilar_p61_consolo", FormattableString.Invariant(
                    $"{origem.X + detCons.X - 20:0},{origem.Y + detCons.Y - 420:0} {origem.X + detCons.X + 760:0},{origem.Y + detCons.Y + 30:0}")));
            var detPad = r.Textos.FirstOrDefault(x => x.Texto == "ALÇA DE IÇAMENTO");
            if (detPad != null)
                _zoomBases.Add(("pilar_p61_detalhes", FormattableString.Invariant(
                    $"{origem.X + detPad.X - 20:0},{origem.Y + detPad.Y - 260:0} {origem.X + detPad.X + 2000:0},{origem.Y + detPad.Y + 30:0}")));
            _zoomBases.Add(("pilar_p61_vistas_alcas", FormattableString.Invariant(
                $"{origem.X + 0:0},{origem.Y + 150:0} {origem.X + 1250:0},{origem.Y + 420:0}")));
            var chaves = r.Textos.FirstOrDefault(x => x.Texto == "DETALHE 01 - CHAVES DE CISALHAMENTO");
            if (chaves != null)
                _zoomBases.Add(("pilar_p61_chaves", FormattableString.Invariant(
                    $"{origem.X + chaves.X - 10:0},{origem.Y + chaves.Y - 60:0} {origem.X + chaves.X + 60:0},{origem.Y + chaves.Y + 5:0}")));
            var corteB = r.Textos.FirstOrDefault(x => x.Texto == "CORTE B-B");
            if (corteB != null)
                _zoomBases.Add(("pilar_p61_corteB", FormattableString.Invariant(
                    $"{origem.X + corteB.X - 30:0},{origem.Y + corteB.Y - 230:0} {origem.X + corteB.X + 260:0},{origem.Y + corteB.Y + 20:0}")));
            _zoomBases.Add(("pilar_p61_vistas_consolo", FormattableString.Invariant(
                $"{origem.X + 150:0},{origem.Y + 470:0} {origem.X + 1250:0},{origem.Y + 720:0}")));
            _zoomBases.Add(("pilar_p61_arm_topo", FormattableString.Invariant(
                $"{origem.X + longX - 60:0},{origem.Y + 1390.5 - 260:0} {origem.X + longX + 360:0},{origem.Y + 1390.5 + 40:0}")));
            _zoomBases.Add(("pilar_p61_arm_emenda", FormattableString.Invariant(
                $"{origem.X + longX - 60:0},{origem.Y + 950:0} {origem.X + longX + 360:0},{origem.Y + 1250:0}")));
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                Checar(bt.Has(r.Detalhamento), $"pilar: detalhamento \"{r.Detalhamento}\" criado");
                var btr = (BlockTableRecord)tr.GetObject(bt[r.Detalhamento], OpenMode.ForRead);
                var ents = btr.Cast<ObjectId>().Select(i => tr.GetObject(i, OpenMode.ForRead)).ToList();
                int dims = ents.OfType<RotatedDimension>().Count(), textos = ents.OfType<DBText>().Count();
                var total = ents.OfType<RotatedDimension>().FirstOrDefault(d => d.DimensionText.StartsWith("COMPRIMENTO TOTAL"));
                Rel.AppendLine($"        pilar P61: {ents.Count} entidades no bloco, {dims} cotas, {textos} textos; total = {total?.Measurement:0.0}");
                Checar(total != null && Math.Abs(total.Measurement - 1390.5) < 1e-6, "pilar: cota do comprimento total = 1390,5");
                var arm = ents.OfType<Polyline>().Select(p => XDataCad.LerBarra(p)).Where(d => d != null).ToList();
                Rel.AppendLine("        pilar: armaduras no bloco: " + string.Join(", ", arm.OrderBy(d => d.Numero).Select(d => FormattableString.Invariant($"N{d.Numero} {d.Quantidade}ø{d.Bitola}"))));
                Checar(arm.Count == r.Barras.Count && arm.All(d => d.Numero > 0), "pilar: armaduras numeradas no detalhamento");
                var est = arm.Where(d => d.Estribo && !d.Segmentos.Any(sg => sg.Formato == FormatoSegmento.Gancho45)).ToList();
                // os do pilar somam as faixas das zonas (ø8: topo 20 + base 26 = 46; ø6,3 corrente); os do consolo têm a quantidade do template
                Checar(est.All(d => d.Quantidade > 0) && est.Any(d => d.Bitola == 8 && d.Quantidade == 46) && est.Any(d => d.Bitola == 6.3 && d.Quantidade > 60),
                    $"pilar: estribos com a quantidade das faixas ({string.Join("/", est.Select(d => d.Quantidade))})");
                var tpP = TabelaParcial(tr, db, cfg, r.Detalhamento);
                var linhasRA = tpP == null ? new List<string[]>() : LinhasRelacao(tpP);
                Rel.AppendLine("        pilar: RA: " + string.Join(" | ", linhasRA.Select(l => string.Join(" ", l))));
                Checar(tpP != null && linhasRA.Count >= 8, "pilar: relação do aço do detalhamento");
                var prod = ents.OfType<Table>().FirstOrDefault(x => x.Cells[0, 0].TextString.TrimStart().StartsWith("INFORMAÇÕES PARA PRODUÇÃO"));
                Rel.AppendLine("        quadro de produção: " + (prod == null ? "(não achado)" : string.Join(" | ", Enumerable.Range(0, prod.Rows.Count).Select(l => prod.Cells[l, 0].TextString + " = " + (prod.Columns.Count > 1 ? prod.Cells[l, 1].TextString : "")))));
                Checar(prod != null && prod.Rows.Count == 7 && prod.Cells[4, 1].TextString.Trim() == "3,523 m³", "pilar: quadro de produção em tabela com o volume calculado");
                var refQ = btr.GetBlockReferenceIds(true, false).Cast<ObjectId>().FirstOrDefault();
                if (prod != null && !refQ.IsNull)
                {
                    // a tabela está dentro do bloco: desloca pela inserção
                    var ins = ((BlockReference)tr.GetObject(refQ, OpenMode.ForRead)).Position - btr.Origin;
                    var e = prod.GeometricExtents;
                    _zoomBases.Add(("pilar_p61_producao", FormattableString.Invariant(
                        $"{e.MinPoint.X + ins.X - 20:0},{e.MinPoint.Y + ins.Y - 20:0} {e.MaxPoint.X + ins.X + 20:0},{e.MaxPoint.Y + ins.Y + 20:0}")));
                }
                var refId = btr.GetBlockReferenceIds(true, false).Cast<ObjectId>().FirstOrDefault();
                if (!refId.IsNull)
                {
                    var ext = ((Entity)tr.GetObject(refId, OpenMode.ForRead)).GeometricExtents;
                    _zoomBases.Add(("pilar_p61", FormattableString.Invariant($"{ext.MinPoint.X - 50:0},{ext.MinPoint.Y - 50:0} {ext.MaxPoint.X + 50:0},{ext.MaxPoint.Y + 50:0}")));
                    // topo das vistas e corte A-A ampliados
                    _zoomBases.Add(("pilar_p61_topo", FormattableString.Invariant($"{ext.MinPoint.X - 20:0},{ext.MaxPoint.Y - 700:0} {ext.MaxPoint.X + 20:0},{ext.MaxPoint.Y + 20:0}")));
                    _zoomBases.Add(("pilar_p61_base", FormattableString.Invariant($"{ext.MinPoint.X - 20:0},{origem.Y - 60:0} {origem.X + 1300:0},{origem.Y + 420:0}")));
                }
                tr.Commit();
            }
        }
        catch (Exception ex) { Checar(false, "template do pilar: exceção " + ex); }
        // layers criados pelo plugin seguem o layers.csv; nenhum com espessura "padrão" do CAD (pedido de 2026-10-30)
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            var ruins = new List<string>();
            int vistos = 0;
            foreach (ObjectId id in lt)
            {
                var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (!l.Name.StartsWith("ARM-", StringComparison.OrdinalIgnoreCase)) continue;
                vistos++;
                var p = Layers.Obter(l.Name);
                var tipo = ((LinetypeTableRecord)tr.GetObject(l.LinetypeObjectId, OpenMode.ForRead)).Name;
                bool ok = l.LineWeight != LineWeight.ByLineWeightDefault && (p == null ||
                    (l.Color.ColorIndex == p.Cor && (int)l.LineWeight == Core.Configuracao.PadroesLayers.Centesimos(p.EspessuraMm)
                     && (string.IsNullOrEmpty(p.TipoLinha) || string.Equals(tipo, p.TipoLinha, StringComparison.OrdinalIgnoreCase))));
                if (!ok) ruins.Add($"{l.Name} (cor {l.Color.ColorIndex}, {l.LineWeight}, {tipo})");
            }
            Rel.AppendLine($"        layers ARM- no desenho: {vistos}; fora do padrão: " + (ruins.Count == 0 ? "nenhum" : string.Join(", ", ruins)));
            Checar(vistos > 10 && ruins.Count == 0, "layers do plugin com cor, espessura e tipo de linha do layers.csv");
            tr.Commit();
        }
        catch (Exception ex) { Checar(false, "layers: " + ex.Message); }
        // etapa 5: longitudinais e zonas em lista — redução com a altura (emenda entre linhas), feixe e zona do consolo
        try
        {
            var t = new Core.Templates.PilarPreMoldado.TemplatePilarPreMoldado();
            var v = new Core.Templates.ValoresTemplate(t.Parametros);
            v["Nome"] = "P62 REDUCAO";
            v["Longitudinais"] = "Cantos||25|1||;Ao lado dos cantos||20|1||;Faces 2 e 4|2|20|1||700;Faces 2 e 4|1|16|2|600|";
            v["ZonasEstribos"] = "Topo|Topo|0|100|8|5|Sim;Consolo|Base|540|620|10|5|Sim;Base|Base|0|260|8|10|Sim";
            var r = t.Gerar(v, cfg);
            var origem = new Point3d(80000, -70000, 0);
            using (Sincronizador.Suspender(doc))
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ids = ComandosTemplate.Desenhar(tr, db, cfg, r, origem);
                tr.Commit();
                Checar(ids.Count == r.Barras.Count, $"pilar com redução: {ids.Count} armaduras desenhadas ({r.Barras.Count} no template)");
            }
            foreach (var a in r.Avisos) Rel.AppendLine("        aviso do pilar com redução: " + a);
            var secoes = r.Textos.Where(x => x.Texto.StartsWith("SEÇÃO ") && x.Texto.EndsWith(" - ARMADURA")).Select(x => x.Texto.Substring(6, 3)).ToList();
            Rel.AppendLine("        pilar com redução: seções " + string.Join(", ", secoes));
            Checar(secoes.Count >= 4 && r.Textos.Any(x => x.Texto.StartsWith("EMENDA: TRASPASSE 100 cm")),
                "pilar com redução: emenda de 100 cm entre as linhas e uma seção por composição");
            // coluna das faces 2 e 4: ø20 até 700 e ø16 (feixe) a partir de 600
            var x16 = r.Barras.First(b => b.Nome.StartsWith("long") && b.Bitola == 16).Vertices[0].X;
            _zoomBases.Add(("pilar_reducao_emenda", FormattableString.Invariant(
                $"{origem.X + x16 - 40:0},{origem.Y + 520:0} {origem.X + x16 + 160:0},{origem.Y + 780:0}")));
            var s1 = r.Textos.First(x => x.Texto == "SEÇÃO 1-1 - ARMADURA");
            _zoomBases.Add(("pilar_reducao_secoes", FormattableString.Invariant(
                $"{origem.X + s1.X - 10:0},{origem.Y + s1.Y - 105:0} {origem.X + s1.X + 140:0},{origem.Y + s1.Y + 8:0}")));
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[r.Detalhamento], OpenMode.ForRead);
                var arm = btr.Cast<ObjectId>().Select(i => tr.GetObject(i, OpenMode.ForRead)).OfType<Polyline>().Select(p => XDataCad.LerBarra(p)).Where(d => d != null).ToList();
                Rel.AppendLine("        pilar com redução: armaduras: " + string.Join(", ", arm.OrderBy(d => d.Numero).Select(d => FormattableString.Invariant($"N{d.Numero} {d.Quantidade}ø{d.Bitola}"))));
                // feixe de 2 ø16 nas faces 2 e 4 (1 por face): 4 barras; estribo ø10 do consolo
                Checar(arm.Any(d => d.Bitola == 16 && d.Quantidade == 4) && arm.Any(d => d.Estribo && d.Bitola == 10 && d.Quantidade > 0),
                    "pilar com redução: feixe de ø16 (4 barras) e estribos da zona do consolo");
                tr.Commit();
            }
            // janela da lista de longitudinais
            var saidaL = Environment.GetEnvironmentVariable("ARM_AUTOTESTE_SAIDA");
            using var fl = new Paleta.FormTabela(t.Parametros.First(x => x.Nome == "Longitudinais"), v["Longitudinais"]);
            fl.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            fl.Location = new System.Drawing.Point(-3000, -3000);
            fl.Show();
            if (!string.IsNullOrEmpty(saidaL))
                using (var bmp = new System.Drawing.Bitmap(fl.Width, fl.Height))
                {
                    fl.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, fl.Width, fl.Height));
                    bmp.Save(Path.Combine(Path.GetDirectoryName(saidaL), "autoteste_janela_longitudinais.png"));
                }
            // trocar para "Cantos" esvazia "barras por face"
            fl.Digitar(2, 0, "Cantos");
            bool vazio = fl.Celula(2, 1) == "";
            fl.Close();
            Checar(vazio, "lista de longitudinais: nos cantos a quantidade por face fica vazia");
        }
        catch (Exception ex) { Checar(false, "pilar com redução: exceção " + ex); }
        // janela da lista de consolos (a mesma do botão "…" do ARMTEMPLATE), salva em PNG para conferir
        try
        {
            var saidaF = Environment.GetEnvironmentVariable("ARM_AUTOTESTE_SAIDA");
            var par = new Core.Templates.PilarPreMoldado.TemplatePilarPreMoldado().Parametros.First(x => x.Nome == "Consolos");
            using var f = new Paleta.FormTabela(par, "CD 40x23,5x25|1,3|592.5|0;CS 20x28,5x25|2|900|5");
            f.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            f.Location = new System.Drawing.Point(-3000, -3000); // fora da tela
            f.Show();
            if (!string.IsNullOrEmpty(saidaF))
                using (var bmp = new System.Drawing.Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(Path.Combine(Path.GetDirectoryName(saidaF), "autoteste_janela_consolos.png"));
                }
            // faces em lista conforme o tipo: duplo já ocupa as duas faces opostas
            bool facesOk = f.Celula(0, 1) == "1 e 3" && f.Celula(1, 1) == "2";
            f.Digitar(1, 0, "CD 40x23,5x25");
            facesOk &= f.Celula(1, 1) == "2 e 4";
            f.Digitar(0, 0, "CS 20x28,5x25");
            facesOk &= f.Celula(0, 1) == "1";
            Rel.AppendLine($"        faces após trocar o tipo: \"{f.Celula(0, 1)}\", \"{f.Celula(1, 1)}\"");
            f.Close();
            Checar(true, "janela da lista de consolos abre");
            Checar(facesOk, "lista de consolos: faces conforme o tipo (duplo = 1 e 3 / 2 e 4)");
        }
        catch (Exception ex) { Checar(false, "janela da lista de consolos: " + ex.Message); }
        // 2ª inserção do mesmo detalhamento, para o EXPLODE no fim do roteiro
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has("P61 TESTE"))
            {
                var ms = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                _pilarExplodirEm = new Point3d(30000, -76000, 0);
                var br = new BlockReference(_pilarExplodirEm, bt["P61 TESTE"]);
                ms.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);
                _pilarExplodir = br.Handle.ToString();
            }
            tr.Commit();
        }
        catch (Exception ex) { Checar(false, "pilar: 2ª inserção para o EXPLODE: " + ex.Message); }
    }

    private static void TestarTemplateBase(Document doc, Database db, Core.Configuracao.ConfiguracaoArmadura cfg)
    {
        try
        {
            var t = new Core.Templates.BaseReservatorio.TemplateBaseReservatorio();
            foreach (var (forma, origem, nome) in new[]
            {
                (Core.Templates.BaseReservatorio.TemplateBaseReservatorio.Circular, new Point3d(30000, -20000, 0), "base_circular"),
                (Core.Templates.BaseReservatorio.TemplateBaseReservatorio.Retangular, new Point3d(30000, -45000, 0), "base_retangular"),
            })
            {
                var v = new Core.Templates.ValoresTemplate(t.Parametros);
                v["Forma"] = forma;
                v["Nome"] = "TESTE " + nome.ToUpperInvariant();
                if (forma == Core.Templates.BaseReservatorio.TemplateBaseReservatorio.Retangular) { v["Arranjo"] = "Grade"; v["CentralLargura"] = "0"; }
                else { v["CentralLargura"] = "200"; v["Inter"] = "Tela"; v["InterCamadas"] = "2"; }
                var r = t.Gerar(v, cfg);
                ObjectId refId;
                using (Sincronizador.Suspender(doc))
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var ids = ComandosTemplate.Desenhar(tr, db, cfg, r, origem);
                    tr.Commit();
                    Checar(ids.Count == r.Barras.Count, $"template {nome}: {ids.Count} armaduras desenhadas");
                }
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    Checar(bt.Has(r.Detalhamento), $"template {nome}: detalhamento \"{r.Detalhamento}\" criado");
                    var btr = (BlockTableRecord)tr.GetObject(bt[r.Detalhamento], OpenMode.ForRead);
                    var barras = btr.Cast<ObjectId>().Select(i => tr.GetObject(i, OpenMode.ForRead)).OfType<Polyline>()
                        .Select(p => (p, d: XDataCad.LerBarra(p))).Where(x => x.d != null).ToList();
                    var tp = TabelaParcial(tr, db, cfg, r.Detalhamento);
                    var linhas = tp == null ? new List<string[]>() : LinhasRelacao(tp);
                    Rel.AppendLine($"        {nome}: {barras.Count} armaduras no bloco; RA: " + string.Join(" | ", linhas.Select(l => string.Join(" ", l))));
                    Checar(tp != null && linhas.Count >= 4, $"template {nome}: relação do aço parcial com as posições");
                    if (forma == Core.Templates.BaseReservatorio.TemplateBaseReservatorio.Circular)
                    {
                        var vars = barras.Where(x => x.d.EhVarLista).ToList();
                        Checar(vars.Count >= 4 && vars.All(x => x.d.Quantidade == x.d.ComprimentosVar.Count),
                            $"base circular: {vars.Count} grupos VAR com a quantidade = nº de comprimentos");
                        Checar(linhas.Any(l => l.Contains("VAR")), "base circular: RA com posição VAR");
                        var desc = vars.Select(x => Ent<MText>(tr, db, x.d.HandleDescricao)?.Contents ?? "").FirstOrDefault();
                        Checar(desc != null && desc.Contains("C=VAR"), $"base circular: descrição do grupo VAR ({desc})");
                    }
                    refId = btr.GetBlockReferenceIds(true, false).Cast<ObjectId>().FirstOrDefault();
                    if (!refId.IsNull)
                    {
                        var ext = ((Entity)tr.GetObject(refId, OpenMode.ForRead)).GeometricExtents;
                        if (tp != null) ext.AddExtents(tp.GeometricExtents);
                        _zoomBases.Add((nome, FormattableString.Invariant($"{ext.MinPoint.X - 50:0},{ext.MinPoint.Y - 50:0} {ext.MaxPoint.X + 50:0},{ext.MaxPoint.Y + 50:0}")));
                    }
                    tr.Commit();
                }
            }
        }
        catch (Exception ex) { Checar(false, "template da base: exceção " + ex); }
    }

    private static string TextoTabela(Table t) =>
        string.Join("|", Enumerable.Range(0, t.Rows.Count).SelectMany(r =>
            Enumerable.Range(0, t.Columns.Count).Select(c => t.Cells[r, c].TextString)));

    /// <summary>Tabela: padrão só com peso; troca de apresentação pela paleta.</summary>
    private static void TestarTabela(Document doc, Database db, Core.Configuracao.ConfiguracaoArmadura cfg)
    {
        try
        {
            ObjectId tid;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                tid = Desenho.Entidades<Table>(db, tr).First();
                var txt = TextoTabela((Table)tr.GetObject(tid, OpenMode.ForRead));
                Checar(txt.Contains("PESO (kg)") && !txt.Contains("BARRAS"), "tabela padrão: resumo só com peso, sem BARRAS");
                Checar(txt.Contains("RELAÇÃO DO AÇO") && txt.Contains("PESO TOTAL (kg)"), "tabela padrão: relação + peso total geral");
                tr.Commit();
            }

            var props = Paleta.PaletaArmadura.LerTabelas(db, new[] { tid, Id(db, H["A"]) });
            Checar(props.Count == 1, "paleta reconhece a tabela da relação do aço");
            props[0].Apresentacao = Core.RelacaoAco.ApresentacaoRA.SomenteResumo;
            props[0].Resumo = Core.RelacaoAco.ApresentacaoResumo.BarrasEPeso;
            Paleta.PaletaArmadura.AplicarTabelas(doc, props);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var t = (Table)tr.GetObject(tid, OpenMode.ForRead);
                var txt = TextoTabela(t);
                Rel.AppendLine($"        tabela 'somente resumo, barras e peso': {t.Rows.Count} linhas");
                Checar(t.Cells[0, 0].TextString == "RESUMO DO AÇO" && !txt.Contains("RELAÇÃO DO AÇO"), "paleta: somente resumo");
                Checar(txt.Contains("BARRAS") && txt.Contains("PESO (kg)"), "paleta: resumo com barras e peso");
                tr.Commit();
            }

            // volta para o padrão pedido pelo usuário (para o PNG final)
            props = Paleta.PaletaArmadura.LerTabelas(db, new[] { tid });
            props[0].Apresentacao = Core.RelacaoAco.ApresentacaoRA.TabelaEResumo;
            props[0].Resumo = Core.RelacaoAco.ApresentacaoResumo.Peso;
            Paleta.PaletaArmadura.AplicarTabelas(doc, props);
        }
        catch (Exception ex)
        {
            Checar(false, "tabela: exceção " + ex);
        }
    }

    /// <summary>Paleta: abre, lê a seleção e aplica alterações em uma e em duas barras.</summary>
    private static void TestarPaleta(Document doc, Database db, Core.Configuracao.ConfiguracaoArmadura cfg)
    {
        try
        {
            Paleta.PaletaArmadura.Mostrar();
            Checar(Paleta.PaletaArmadura.Visivel, "paleta aberta");
            var ids = new[] { Id(db, H["A"]), Id(db, H["B"]) };
            doc.Editor.SetImpliedSelection(ids);
            Paleta.PaletaArmadura.Recarregar();
            Rel.AppendLine($"        paleta com a seleção implícita: {Paleta.PaletaArmadura.Mostradas.Count} barra(s)");

            // propriedades rápidas (ARMPALETARAPIDA): mesma seleção, janela flutuante
            Checar(Paleta.PaletaRapida.Mostrar(doc), "paleta rápida: abre com a seleção");
            var rapida = Paleta.PaletaRapida.Atual;
            Checar(rapida != null && rapida.Objetos.OfType<Paleta.PropriedadesBarra>().Count() == 2 && rapida.Text == "Armadura (2)",
                $"paleta rápida: 2 barras, título \"{rapida?.Text}\"");
            var grupos = rapida?.GruposParaTeste ?? new List<(string, bool)>();
            Rel.AppendLine("        paleta rápida, grupos: " + string.Join(", ", grupos.Select(g => g.Item1 + (g.Item2 ? " (aberto)" : ""))));
            Checar(grupos.Count > 1 && grupos[0].Item2 && grupos.Skip(1).All(g => !g.Item2), "paleta rápida: só o 1º grupo aberto");
            var saidaRapida = Environment.GetEnvironmentVariable("ARM_AUTOTESTE_SAIDA");
            if (rapida != null && !string.IsNullOrEmpty(saidaRapida))
            {
                using var bmp = new System.Drawing.Bitmap(rapida.Width, rapida.Height);
                rapida.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, rapida.Width, rapida.Height));
                bmp.Save(Path.Combine(Path.GetDirectoryName(saidaRapida), "autoteste_paleta_rapida.png"));
            }
            doc.Editor.SetImpliedSelection(new ObjectId[0]);
            rapida?.Recarregar();
            Checar(Paleta.PaletaRapida.Atual == null, "paleta rápida: some quando a seleção fica vazia");
            doc.Editor.SetImpliedSelection(ids);

            // seleção pela descrição também resolve para a barra; barra + descrição contam uma vez
            var duas = Paleta.PaletaArmadura.Ler(db, new[] { ids[0], Id(db, H["descA"]), ids[1] }, out _);
            Checar(duas.Count == 2, $"paleta: A + descrição de A + B = 2 barras ({duas.Count})");

            // alteração em lote: bitola 12.5 nas duas → ganchos recalculados pela norma
            foreach (var p in duas) p.Bitola = 12.5;
            var erros = Paleta.PaletaArmadura.Aplicar(doc, duas);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var a = (Polyline)tr.GetObject(ids[0], OpenMode.ForRead);
                var b = (Polyline)tr.GetObject(ids[1], OpenMode.ForRead);
                double g = cfg.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho90, 12.5, "CA-50", false);
                Checar(erros.Count == 0 && XDataCad.LerBarra(a).Bitola == 12.5 && XDataCad.LerBarra(b).Bitola == 12.5,
                    "paleta: bitola alterada nas duas barras");
                Checar(Math.Abs(GeometriaBarra.Ler(a, 1)[1].Comprimento - g) < 1e-6 && Math.Abs(GeometriaBarra.Ler(b, 1)[0].Comprimento - g) < 1e-6,
                    $"paleta: ganchos recalculados para ø12.5 ({g:0.###} cm)");
                var desc = Ent<MText>(tr, db, XDataCad.LerBarra(a).HandleDescricao);
                Checar(desc != null && desc.Contents.Contains("%%c12.5"), $"paleta: descrição atualizada ({desc?.Contents})");
                tr.Commit();
            }

            // uma barra: segmentos
            var uma = Paleta.PaletaArmadura.Ler(db, new[] { ids[0] }, out _)[0];
            uma.Segmentos.Itens[0].Comprimento = 250;
            uma.Segmentos.Itens[1].Var = true;
            erros = Paleta.PaletaArmadura.Aplicar(doc, new[] { uma });
            var dep = Paleta.PaletaArmadura.Ler(db, new[] { ids[0] }, out _)[0];
            Checar(erros.Count == 0 && Math.Abs(dep.Segmentos.Itens[0].Comprimento - 250) < 1e-6, "paleta: comprimento do segmento 1 = 250");
            Checar(dep.Segmentos.Itens[1].Var && dep.Comprimento.StartsWith("VAR"), $"paleta: VAR no gancho ({dep.Comprimento})");

            // escala da barra pela paleta: 1:25 → textos de 5 un
            dep.Escala = 25;
            Paleta.PaletaArmadura.Aplicar(doc, new[] { dep });
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var d25 = XDataCad.LerBarra(tr.GetObject(ids[0], OpenMode.ForRead));
                var desc25 = Ent<MText>(tr, db, d25.HandleDescricao);
                var cota25 = Ent<DBText>(tr, db, d25.Segmentos[0].HandleCota);
                Checar(d25.Escala == 25 && Math.Abs(desc25.TextHeight - 5) < 1e-9 && Math.Abs(cota25.Height - 5) < 1e-9,
                    $"paleta: escala 1:25 → texto 5 un (descrição {desc25?.TextHeight}, cota {cota25?.Height})");
                tr.Commit();
            }
            dep = Paleta.PaletaArmadura.Ler(db, new[] { ids[0] }, out _)[0];
            Checar(dep.Escala == 25, "paleta mostra a escala 1:25");

            dep.Segmentos.Itens[1].Formato = FormatoSegmento.Linha;
            Paleta.PaletaArmadura.Aplicar(doc, new[] { dep });
            dep = Paleta.PaletaArmadura.Ler(db, new[] { ids[0] }, out _)[0];
            Checar(dep.Segmentos.Itens[1].Formato == FormatoSegmento.Linha, "paleta: formato Gancho → Linha");

            bool recusou = false;
            var d = Paleta.PaletaArmadura.Ler(db, new[] { Id(db, H["D"]) }, out _);
            try { d[0].Segmentos.Itens[1].Comprimento = 10; } catch (ArgumentException) { recusou = true; }
            Checar(recusou, "paleta: comprimento de arco recusado");

            bool recusouQtd = false;
            try { uma.Quantidade = 0; } catch (ArgumentException) { recusouQtd = true; }
            Checar(recusouQtd, "paleta: quantidade 0 recusada");
        }
        catch (Exception ex)
        {
            Checar(false, "paleta: exceção " + ex);
        }
    }

    private static int _marca;
    private static object _filedia;
    private static Core.Estribos.OpcoesEstribo _estriboDoUsuario;
    private static (bool limite, string trelica, string tela) _restaurar = (false, "TR 8644", "Q-138");

    private static void TestarEstribos(Transaction tr, Database db, Core.Configuracao.ConfiguracaoArmadura cfg)
    {
        try
        {
            // ARMESTRIBOREP digitado: seção 20×50, cobrimento 2,5 → estribo 15×45, rebatido em (2100, 0)
            var est = Desenho.Entidades<Polyline>(db, tr).Select(i => (Polyline)tr.GetObject(i, OpenMode.ForRead))
                .Select(p => (p, d: XDataCad.LerBarra(p))).Where(x => x.d != null && x.d.Estribo && x.p.NumberOfVertices == 9)
                .FirstOrDefault(x => Math.Abs(x.p.GetPoint2dAt(2).X - 2100) < 1e-6 && Math.Abs(x.p.GetPoint2dAt(2).Y) < 1e-6);
            Checar(est.p != null, "ARMESTRIBOREP criou o estribo rebatido");
            if (est.p != null)
            {
                var geo = GeometriaBarra.Ler(est.p, 1);
                double g = cfg.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho45, 5, "CA-60", true);
                Rel.AppendLine("        estribo: " + string.Join(" + ", geo.Select(s => s.Comprimento.ToString("0.##") + (s.Sobreposto ? "*" : ""))) + $" (gancho {g:0.###}; * = passagem do canto)");
                Checar(Math.Abs(geo[2].Comprimento - 15) < 1e-6 && Math.Abs(geo[5].Comprimento - 45) < 1e-6
                       && Math.Abs(geo[0].Comprimento - g) < 1e-6 && Math.Abs(geo[7].Comprimento - g) < 1e-6,
                    "estribo 15×45 (seção − 2·cobrimento) com ganchos de estribo pela norma");
                Checar(est.d.Segmentos[0].Formato == FormatoSegmento.Gancho45 && est.d.Segmentos[7].Formato == FormatoSegmento.Gancho45,
                    "ganchos marcados como Gancho 45°");
                Checar(geo[1].Sobreposto && geo[6].Sobreposto && geo.Count(s => s.Sobreposto) == 2,
                    "passagens do canto marcadas como sobrepostas");
                var p0 = est.p.GetPoint2dAt(0);
                var p8 = est.p.GetPoint2dAt(8);
                Checar(p0.GetDistanceTo(p8) > 1, "ganchos do estribo não se sobrepõem");
                Checar(est.d.Quantidade == 21 && est.d.Faixas.Count == 1, $"faixa de estribos 300 c/15 = 21 ({est.d.Quantidade})");
                var desc = Ent<MText>(tr, db, est.d.HandleDescricao);
                Rel.AppendLine("        descrição do estribo: " + desc?.Contents);
                Checar(desc != null && desc.Contents.StartsWith("21 N") && desc.Contents.Contains("c/15") && desc.Contents.EndsWith("C=133"),
                    "descrição do estribo (real: 137,5 − 5 × 0,93 (90°) − 2 × 0,14 (ganchos 135°) = 132,6 → 133)");
                Checar(est.d.Segmentos.Count(s => !string.IsNullOrEmpty(s.HandleCota)) == 2, "estribo cotado só na largura e na altura");
            }
            var secao = Desenho.Entidades<Polyline>(db, tr).Select(i => (Polyline)tr.GetObject(i, OpenMode.ForRead))
                .FirstOrDefault(p => !XDataCad.TemDados(p) && p.NumberOfVertices == 9 && Math.Abs(p.GetPoint2dAt(2).X - 2002.5) < 1e-6);
            Checar(secao != null && Math.Abs(secao.GetPoint2dAt(2).Y - (-2.5)) < 1e-6, "estribo na seção (polilinha simples, descontado o cobrimento)");

            // pente 4 ramos e múltiplo 4 ramos pela API
            var o = new Core.Estribos.OpcoesEstribo { Formato = Core.Estribos.FormatoEstribo.Pente, Ramos = 4, Bitola = 5, Aco = "CA-60" };
            var pente = ComandosEstribo.Criar(tr, db, cfg, o, new Point2d(2500, 0), 30, 50, 0, new Point2d(2600, 0));
            Checar(pente.Count == 2, $"pente: fechado + pente = 2 armaduras ({pente.Count})");
            var dp = XDataCad.LerBarra(tr.GetObject(pente[1], OpenMode.ForRead));
            Checar(dp.Segmentos.Count == 5, $"pente de 4 ramos: 2 pernas internas ({dp.Segmentos.Count} segmentos)");

            o.Formato = Core.Estribos.FormatoEstribo.Multiplo;
            var mult = ComandosEstribo.Criar(tr, db, cfg, o, new Point2d(2800, 0), 30, 50, 0, new Point2d(2900, 0));
            var dm = XDataCad.LerBarra(tr.GetObject(mult[0], OpenMode.ForRead));
            Checar(mult.Count == 1 && dm.Repeticoes == 2, $"múltiplo 4 ramos: 1 armadura com 2 repetições ({mult.Count}, rep {dm.Repeticoes})");

            o.Formato = Core.Estribos.FormatoEstribo.CircularExterno;
            var circ = ComandosEstribo.Criar(tr, db, cfg, o, new Point2d(3100, 0), 40, 40, 40, null);
            var geoC = GeometriaBarra.Ler((Polyline)tr.GetObject(circ[0], OpenMode.ForRead), 1);
            Checar(geoC.Count == 4 && Math.Abs(geoC[1].Comprimento - Math.PI * 20) < 1e-6, "estribo circular: 2 meias-voltas + ganchos");

            using (var f = new Paleta.FormEstribo(new Core.Estribos.OpcoesEstribo(), cfg)) { }
            Checar(true, "diálogo do estribo monta sem erro");
        }
        catch (Exception ex)
        {
            Checar(false, "estribos: exceção " + ex);
        }
    }

    [Rt.CommandMethod("ARMTESTEMARCA")]
    public void Marca() => Log.Info($"autoteste: marca {++_marca}");

    [Rt.CommandMethod("ARMTESTEFIM", Rt.CommandFlags.Session)]
    public void Fim()
    {
        try
        {
            if (_filedia != null) { CadApp.SetSystemVariable("FILEDIA", _filedia); _filedia = null; }
            // o desenho já foi salvo em ARM_AUTOTESTE_SAIDA: descarta sem perguntar e fecha
            foreach (Document d in CadApp.DocumentManager.Cast<Document>().ToList()) d.CloseAndDiscard();
            CadApp.Quit();
        }
        catch (Exception ex) { Log.Erro("ARMTESTEFIM", ex); }
    }

    /// <summary>
    /// Ferramenta de estudo: converte o DWG de ARM_DXF_ENTRADA em DXF (ARM_DXF_SAIDA) num banco lateral — o
    /// desenho de referência do usuário só é lido — e fecha o CAD. Uso: script com NETLOAD + ARMTESTEDXF.
    /// </summary>
    [Rt.CommandMethod("ARMTESTEDXF", Rt.CommandFlags.Session)]
    public void ParaDxf()
    {
        try
        {
            var entrada = Environment.GetEnvironmentVariable("ARM_DXF_ENTRADA");
            var saida = Environment.GetEnvironmentVariable("ARM_DXF_SAIDA");
            using (var db = new Database(false, true))
            {
                db.ReadDwgFile(entrada, FileOpenMode.OpenForReadAndAllShare, true, "");
                db.CloseInput(true);
                db.DxfOut(saida, 16, DwgVersion.Current);
            }
            Log.Info($"ARMTESTEDXF: {entrada} → {saida}");
        }
        catch (Exception ex) { Log.Erro("ARMTESTEDXF", ex); }
        foreach (Document d in CadApp.DocumentManager.Cast<Document>().ToList()) d.CloseAndDiscard();
        CadApp.Quit();
    }

    private static ObjectId Id(Database db, string h) => XDataCad.IdDoHandle(db, h);

    private static DadosArmadura Barra(Transaction tr, Database db, string nome) =>
        XDataCad.LerBarra(tr.GetObject(Id(db, H[nome]), OpenMode.ForRead, true)) ?? new DadosArmadura { Quantidade = -1 };

    private static T Ent<T>(Transaction tr, Database db, string h) where T : Entity
    {
        var id = Id(db, h);
        return id.IsNull || id.IsErased ? null : tr.GetObject(id, OpenMode.ForRead) as T;
    }
}
