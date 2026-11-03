using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Armadura.Cad.Barra;
using Armadura.Cad.Infra;
using Armadura.Cad.Relacao;
using Armadura.Core.Calculo;
using Armadura.Core.Configuracao;
using Armadura.Core.Descricao;
using Armadura.Core.Modelo;
using Armadura.Core.Persistencia;
using Armadura.Core.RelacaoAco;

namespace Armadura.Cad;

public sealed class Comandos
{
    // ================= ARMBARRA =================

    [Rt.CommandMethod("ARMBARRA")]
    public void ArmBarra() => Executar("ARMBARRA", (doc, ed, db, cfg) =>
    {
        var ucs = ed.CurrentUserCoordinateSystem;
        var pts = new List<Point2d>();
        Point3d ultimoUcs;
        double elevacao;

        // primeiro ponto, com as opções dos valores correntes
        while (true)
        {
            ed.WriteMessage($"\nAtual: {ResumoCorrente(cfg)}");
            var po = new PromptPointOptions("\nPrimeiro ponto da armadura ou");
            AdicionarPalavras(po.Keywords, "Bitola", "Aco", "Quantidade", "Espacamento", "Cobrimento");
            po.AppendKeywordsToMessage = true;
            var r = ed.GetPoint(po);
            if (r.Status == PromptStatus.Keyword && r.StringResult == "Cobrimento")
            {
                // ponto no cobrimento: clica nas linhas da forma em vez de pontos
                var id = ComandosCobrimento.DesenharPorCobrimento(ed, db, cfg, NovaBarra(cfg));
                if (!id.IsNull) ed.WriteMessage("\nArmadura criada pelo cobrimento. Use ARMRAATUALIZAR para numerar.");
                return;
            }
            if (r.Status == PromptStatus.Keyword) { PedirValorCorrente(ed, cfg, r.StringResult); continue; }
            if (r.Status != PromptStatus.OK) return;
            ultimoUcs = r.Value;
            var w = r.Value.TransformBy(ucs);
            pts.Add(new Point2d(w.X, w.Y));
            elevacao = w.Z;
            break;
        }

        using var tr = db.TransactionManager.StartTransaction();
        Polyline pl = null;
        bool arco = false;
        var historicoUcs = new List<Point3d> { ultimoUcs };

        while (true)
        {
            var po = new PromptPointOptions(arco ? "\nPonto no arco ou" : "\nPróximo ponto ou")
            {
                UseBasePoint = true, BasePoint = ultimoUcs, AllowNone = true, AppendKeywordsToMessage = true,
            };
            if (arco) AdicionarPalavras(po.Keywords, "Linha", "Desfazer");
            else AdicionarPalavras(po.Keywords, "Arco", "Desfazer");
            var r = ed.GetPoint(po);

            if (r.Status == PromptStatus.Keyword)
            {
                if (r.StringResult == "Arco") arco = true;
                else if (r.StringResult == "Linha") arco = false;
                else if (r.StringResult == "Desfazer" && pts.Count > 1)
                {
                    pts.RemoveAt(pts.Count - 1);
                    historicoUcs.RemoveAt(historicoUcs.Count - 1);
                    ultimoUcs = historicoUcs[historicoUcs.Count - 1];
                    pl.RemoveVertexAt(pl.NumberOfVertices - 1);
                    pl.SetBulgeAt(pl.NumberOfVertices - 1, 0);
                    AtualizarTela(tr, ed);
                }
                continue;
            }
            if (r.Status != PromptStatus.OK) break; // Enter ou Esc: termina com o que já foi desenhado

            double bulge = 0;
            Point3d fimUcs = r.Value;
            if (arco)
            {
                var pf = new PromptPointOptions("\nPonto final do arco:") { UseBasePoint = true, BasePoint = r.Value };
                var rf = ed.GetPoint(pf);
                if (rf.Status != PromptStatus.OK) continue;
                var pm = r.Value.TransformBy(ucs);
                var p1 = rf.Value.TransformBy(ucs);
                bulge = GeometriaBarra.BulgeTresPontos(pts[pts.Count - 1], new Point2d(pm.X, pm.Y), new Point2d(p1.X, p1.Y));
                fimUcs = rf.Value;
            }
            var w = fimUcs.TransformBy(ucs);
            var p = new Point2d(w.X, w.Y);
            if (p.GetDistanceTo(pts[pts.Count - 1]) < 1e-9) continue;

            if (pl == null)
            {
                pl = new Polyline { Elevation = elevacao };
                pl.SetDatabaseDefaults(db);
                pl.AddVertexAt(0, pts[0], bulge, 0, 0);
                pl.AddVertexAt(1, p, 0, 0, 0);
                pl.LayerId = Desenho.GarantirLayer(db, tr, cfg.LayerBarra, cfg.CorBarra);
                var esp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                esp.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
            }
            else
            {
                pl.SetBulgeAt(pl.NumberOfVertices - 1, bulge);
                pl.AddVertexAt(pl.NumberOfVertices, p, 0, 0, 0);
            }
            pts.Add(p);
            historicoUcs.Add(fimUcs);
            ultimoUcs = fimUcs;
            AtualizarTela(tr, ed);
        }

        if (pl == null || pl.NumberOfVertices < 2)
        {
            // menos de 2 pontos: apaga a pré-visualização e confirma (abortar atrapalha o GstarCAD)
            pl?.Erase();
            tr.Commit();
            return;
        }
        var d = NovaBarra(cfg);
        ServicoBarra.Criar(tr, pl, d, cfg);
        if (ServicoTraspasse.VerificarLimite(tr, pl, cfg))
            ed.WriteMessage("\nA barra passou do comprimento comercial e foi dividida com traspasse.");
        tr.Commit();
        ed.WriteMessage($"\nArmadura criada ({GeometriaBarra.NumeroSegmentos(pl)} segmentos). " +
                        "Use ARMRAATUALIZAR para numerar (barra nova = N0).");
    });

    // ================= ARMCONVERTER =================

    [Rt.CommandMethod("ARMCONVERTER", Rt.CommandFlags.UsePickSet)]
    public void ArmConverter() => Executar("ARMCONVERTER", (doc, ed, db, cfg) =>
    {
        ed.WriteMessage($"\nValores usados: {ResumoCorrente(cfg)}");
        var filtro = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE") });
        var r = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelecione as polilinhas a converter:" }, filtro);
        if (r.Status != PromptStatus.OK) return;
        bool estribos = false;
        using (var tr = new TransacaoSegura(db))
        {
            bool algumaFechada = r.Value.GetObjectIds().Any(id => ServicoEstribo.PoligonoFechado((Polyline)tr.GetObject(id, OpenMode.ForRead)) != null);
            if (algumaFechada)
            {
                var est = PerguntarEstribo(ed);
                if (est == null) return;
                estribos = est.Value;
            }
        }
        int n = 0, ja = 0, e = 0;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            foreach (var id in r.Value.GetObjectIds())
            {
                var pl = (Polyline)tr.GetObject(id, OpenMode.ForWrite);
                if (XDataCad.TemDados(pl)) { ja++; continue; }
                if (CriarBarra(tr, pl, cfg, estribos && ServicoEstribo.PoligonoFechado(pl) != null)) e++;
                n++;
            }
            tr.Commit();
        }
        ed.WriteMessage($"\n{n} polilinha(s) convertida(s) em armadura" + (e > 0 ? $" ({e} estribo(s) com ganchos)" : "") +
                        (ja > 0 ? $"; {ja} já era(m) armadura." : "."));
    });

    /// <summary>"Polilinha fechada vira estribo com ganchos?" (null = cancelado).</summary>
    internal static bool? PerguntarEstribo(Editor ed)
    {
        var pk = new PromptKeywordOptions("\nPolilinha fechada vira estribo com ganchos (no vértice superior esquerdo)?")
            { AllowNone = true, AppendKeywordsToMessage = true };
        AdicionarPalavras(pk.Keywords, "Sim", "Nao");
        pk.Keywords.Default = "Sim";
        var rk = ed.GetKeywords(pk);
        if (rk.Status != PromptStatus.OK && rk.Status != PromptStatus.None) return null;
        return rk.StringResult != "Nao";
    }

    /// <summary>Converte a polilinha em armadura com os valores correntes; estribo: ganchos no canto. Retorna se virou estribo.</summary>
    internal static bool CriarBarra(Transaction tr, Polyline pl, ConfiguracaoArmadura cfg, bool estribo)
    {
        var d = NovaBarra(cfg);
        d.Estribo = estribo;
        ServicoBarra.Criar(tr, pl, d, cfg);
        if (!estribo) return false;
        var dados = XDataCad.LerBarra(pl);
        if (ServicoEstribo.Converter(tr, pl, dados, cfg) != null) return false;
        ServicoBarra.Atualizar(tr, pl, cfg);
        return true;
    }

    // ================= ARMOFFSET =================

    /// <summary>
    /// Armadura paralela a uma poligonal (aberta ou fechada), afastada dela (ex.: o cobrimento). A poligonal
    /// original fica como está. Fechada: pode virar estribo com ganchos.
    /// </summary>
    [Rt.CommandMethod("ARMOFFSET")]
    public void ArmOffset() => Executar("ARMOFFSET", (doc, ed, db, cfg) =>
    {
        ed.WriteMessage($"\nValores usados: {ResumoCorrente(cfg)}");
        double cpu = Desenho.CmPorUnidade(db, cfg);
        bool? estribo = null;
        int criadas = 0;
        while (true)
        {
            var pe = new PromptEntityOptions("\nSelecione a poligonal (Enter termina):") { AllowNone = true };
            pe.SetRejectMessage("\nSelecione uma polilinha.");
            pe.AddAllowedClass(typeof(Polyline), true);
            var re = ed.GetEntity(pe);
            if (re.Status != PromptStatus.OK) break;

            var dist = PedirReal(ed, "Afastamento da poligonal (cm)", cfg.CobrimentoCorrente > 0 ? cfg.CobrimentoCorrente : 2.5, 0);
            if (dist == null) break;
            if (Math.Abs(cfg.CobrimentoCorrente - dist.Value) > 1e-9) { Plugin.Config.CobrimentoCorrente = dist.Value; Plugin.SalvarConfig(); ConfigDesenho.SincronizarCorrentes(); }

            bool fechada;
            using (var tr = new TransacaoSegura(db))
                fechada = ServicoEstribo.PoligonoFechado((Polyline)tr.GetObject(re.ObjectId, OpenMode.ForRead)) != null;
            var rl = ed.GetPoint(new PromptPointOptions(fechada
                ? "\nClique do lado da armadura (Enter = para dentro):" : "\nClique do lado da armadura:") { AllowNone = fechada });
            if (rl.Status != PromptStatus.OK && !(fechada && rl.Status == PromptStatus.None)) break;
            Point3d? lado = rl.Status == PromptStatus.OK ? rl.Value.TransformBy(ed.CurrentUserCoordinateSystem) : (Point3d?)null;
            if (fechada && estribo == null) { estribo = PerguntarEstribo(ed); if (estribo == null) break; }

            using (var tr = new TransacaoSegura(db))
            {
                var origem = (Polyline)tr.GetObject(re.ObjectId, OpenMode.ForRead);
                var nova = Offset(origem, dist.Value / cpu, lado);
                if (nova == null) { ed.WriteMessage("\nNão foi possível afastar esta poligonal (afastamento grande demais?)."); continue; }
                nova.SetDatabaseDefaults(db);
                nova.LayerId = Desenho.GarantirLayer(db, tr, cfg.LayerBarra, cfg.CorBarra);
                var esp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                esp.AppendEntity(nova);
                tr.AddNewlyCreatedDBObject(nova, true);
                CriarBarra(tr, nova, cfg, fechada && estribo == true);
                tr.Commit();
                criadas++;
            }
        }
        if (criadas > 0) ed.WriteMessage($"\n{criadas} armadura(s) criada(s) a partir de poligonais.");
    });

    /// <summary>
    /// Curva paralela à <paramref name="pl"/> a <paramref name="d"/> unidades: do lado do ponto clicado ou,
    /// sem ponto (poligonal fechada), para dentro (a de menor área). Só uma polilinha resultante.
    /// </summary>
    internal static Polyline Offset(Polyline pl, double d, Point3d? lado)
    {
        if (d <= 0) return (Polyline)pl.Clone();
        Polyline Uma(double s)
        {
            try
            {
                var c = pl.GetOffsetCurves(s);
                var r = c.Count == 1 ? c[0] as Polyline : null;
                foreach (DBObject o in c) if (!ReferenceEquals(o, r)) o.Dispose();
                return r;
            }
            catch (Rt.Exception) { return null; }
        }
        var a = Uma(d);
        var b = Uma(-d);
        if (a == null || b == null) return a ?? b;
        bool escolheA;
        if (lado.HasValue)
            escolheA = a.GetClosestPointTo(lado.Value, false).DistanceTo(lado.Value) <= b.GetClosestPointTo(lado.Value, false).DistanceTo(lado.Value);
        else
            escolheA = Math.Abs(a.Area) <= Math.Abs(b.Area);
        var fica = escolheA ? a : b;
        (escolheA ? b : a).Dispose();
        return fica;
    }

    // ================= ARMPROP =================

    [Rt.CommandMethod("ARMPROP")]
    public void ArmProp() => Executar("ARMPROP", (doc, ed, db, cfg) =>
    {
        var id = SelecionarBarra(ed, db, "\nSelecione a armadura (barra, descrição ou cota):");
        if (id.IsNull) return;
        while (true)
        {
            using var tr = new TransacaoSegura(db);
            var pl = (Polyline)tr.GetObject(id, OpenMode.ForRead);
            var d = XDataCad.LerBarra(pl);
            var geo = GeometriaBarra.Ler(pl, Desenho.CmPorUnidade(db, cfg));
            ed.WriteMessage($"\n{FormatadorDescricao.Formatar(d, CalculoComprimento.ComprimentoUnitario(geo, cfg, d), cfg).Replace("%%c", "ø")}" +
                            $"  | aço {d.Aco}, repetições {d.Repeticoes}, estribo {SN(d.Estribo)}, corrido {SN(d.Corrido)}, visível {SN(d.Visivel)}");
            var pk = new PromptKeywordOptions("\nAlterar") { AllowNone = true, AppendKeywordsToMessage = true };
            AdicionarPalavras(pk.Keywords, "Bitola", "Aco", "Quantidade", "Espacamento", "Repeticoes", "Estribo", "Corrido", "Visivel", "Descricao", "Sair");
            pk.Keywords.Default = "Sair";
            var rk = ed.GetKeywords(pk);
            if (rk.Status != PromptStatus.OK || rk.StringResult == "Sair") return;

            bool mudou = true;
            switch (rk.StringResult)
            {
                case "Bitola": { var v = PedirBitola(ed, cfg, d.Bitola); if (v == null) mudou = false; else d.Bitola = v.Value; break; }
                case "Aco": { var v = PedirAco(ed, d.Aco); if (v == null) mudou = false; else d.Aco = v; break; }
                case "Quantidade": { var v = PedirInteiro(ed, "Quantidade", d.Quantidade, 1); if (v == null) mudou = false; else d.Quantidade = v.Value; break; }
                case "Espacamento": { var v = PedirReal(ed, "Espaçamento (cm, 0 = não indicar)", d.Espacamento, 0); if (v == null) mudou = false; else d.Espacamento = v.Value; break; }
                case "Repeticoes": { var v = PedirInteiro(ed, "Repetições", d.Repeticoes, 1); if (v == null) mudou = false; else d.Repeticoes = v.Value; break; }
                case "Estribo": d.Estribo = !d.Estribo; break;
                case "Corrido": d.Corrido = !d.Corrido; break;
                case "Visivel": d.Visivel = !d.Visivel; break;
                case "Descricao": { var c = PedirCampos(ed, d.Campos); if (c == null) mudou = false; else d.Campos = c.Value; break; }
            }
            if (!mudou) continue;
            pl.UpgradeOpen();
            XDataCad.Gravar(pl, d);
            ServicoBarra.Atualizar(tr, pl, cfg);
            tr.Commit();
        }
    });

    // ================= ARMEDITSEG =================

    [Rt.CommandMethod("ARMEDITSEG")]
    public void ArmEditSeg() => Executar("ARMEDITSEG", (doc, ed, db, cfg) =>
    {
        var pe = new PromptEntityOptions("\nClique no segmento da armadura (ou na cota dele):");
        pe.SetRejectMessage("\nNão é uma armadura.");
        pe.AddAllowedClass(typeof(Polyline), false);
        pe.AddAllowedClass(typeof(DBText), false);
        var r = ed.GetEntity(pe);
        if (r.Status != PromptStatus.OK) return;

        ObjectId barraId;
        int idx;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var ent = (Entity)tr.GetObject(r.ObjectId, OpenMode.ForRead);
            var pl = ServicoBarra.ResolverBarra(tr, r.ObjectId);
            if (pl == null) { ed.WriteMessage("\nNão é uma armadura."); return; }
            barraId = pl.ObjectId;
            if (ent is DBText) idx = XDataCad.LerVinculo(ent).Indice;
            else
            {
                var w = r.PickedPoint.TransformBy(ed.CurrentUserCoordinateSystem);
                var perto = pl.GetClosestPointTo(new Point3d(w.X, w.Y, pl.Elevation), false);
                idx = (int)Math.Floor(pl.GetParameterAtPoint(perto) + 1e-9);
                idx = Math.Max(0, Math.Min(GeometriaBarra.NumeroSegmentos(pl) - 1, idx));
            }
            tr.Commit();
        }

        double cpu = Desenho.CmPorUnidade(db, cfg);
        while (true)
        {
            using var tr = new TransacaoSegura(db);
            var pl = (Polyline)tr.GetObject(barraId, OpenMode.ForRead);
            var d = XDataCad.LerBarra(pl);
            var geo = GeometriaBarra.Ler(pl, cpu);
            d.AjustarSegmentos(geo.Count);
            if (idx >= geo.Count) return;
            var s = d.Segmentos[idx];
            bool arco = geo[idx].Tipo == TipoSegmento.Arco;
            ed.WriteMessage($"\nSegmento {idx + 1} de {geo.Count}: {CalculoComprimento.NumeroCompacto(geo[idx].Comprimento)} cm, " +
                            $"{NomeFormato(s.Formato)}{(s.Var ? ", VAR" : "")}{(s.Cotar ? "" : ", sem cota")}");

            var pk = new PromptKeywordOptions("\nAlterar") { AllowNone = true, AppendKeywordsToMessage = true };
            if (!arco) AdicionarPalavras(pk.Keywords, "Comprimento", "Formato");
            AdicionarPalavras(pk.Keywords, "Var", "Cotar", "Proximo", "Sair");
            pk.Keywords.Default = "Sair";
            var rk = ed.GetKeywords(pk);
            if (rk.Status != PromptStatus.OK || rk.StringResult == "Sair") return;

            switch (rk.StringResult)
            {
                case "Proximo": idx = (idx + 1) % geo.Count; continue;
                case "Var": s.Var = !s.Var; break;
                case "Cotar": s.Cotar = !s.Cotar; break;
                case "Comprimento":
                {
                    var v = PedirReal(ed, "Comprimento (cm)", geo[idx].Comprimento, 0.1);
                    if (v == null) continue;
                    var erro = ServicoBarra.AlterarComprimentoSegmento(pl, d, idx, v.Value, cpu);
                    if (erro != null) { ed.WriteMessage("\n" + erro); continue; }
                    break;
                }
                case "Formato":
                {
                    var pf = new PromptKeywordOptions("\nFormato") { AppendKeywordsToMessage = true };
                    AdicionarPalavras(pf.Keywords, "Linha", "G45", "G90", "G180");
                    pf.Keywords.Default = "Linha";
                    var rf = ed.GetKeywords(pf);
                    if (rf.Status != PromptStatus.OK) continue;
                    var f = rf.StringResult switch
                    {
                        "G45" => FormatoSegmento.Gancho45,
                        "G90" => FormatoSegmento.Gancho90,
                        "G180" => FormatoSegmento.Gancho180,
                        _ => FormatoSegmento.Linha,
                    };
                    var erro = ServicoBarra.AlterarFormatoSegmento(pl, d, idx, f, cpu);
                    if (erro != null) { ed.WriteMessage("\n" + erro); continue; }
                    if (Segmento.EhGancho(f))
                        ed.WriteMessage($"\nGancho: {CalculoComprimento.NumeroCompacto(cfg.Dobramento.ComprimentoGancho(f, d.Bitola, d.Aco, d.Estribo))} cm (ponta reta + pino/2 + ø).");
                    break;
                }
            }
            if (!pl.IsWriteEnabled) pl.UpgradeOpen();
            XDataCad.Gravar(pl, d);
            ServicoBarra.Atualizar(tr, pl, cfg);
            tr.Commit();
        }
    });

    // ================= Paleta =================

    [Rt.CommandMethod("ARMCOMANDOS")]
    public void ArmComandos() => Executar("ARMCOMANDOS", (doc, ed, db, cfg) =>
    {
        Paleta.PainelComandos.Alternar();
        Plugin.Config.MostrarComandos = Paleta.PainelComandos.Visivel;
        Plugin.SalvarConfig();
    });

    [Rt.CommandMethod("ARMPALETA")]
    public void ArmPaleta() => Executar("ARMPALETA", (doc, ed, db, cfg) =>
    {
        Paleta.PaletaArmadura.Alternar();
        Plugin.Config.MostrarPaleta = Paleta.PaletaArmadura.Visivel;
        Plugin.SalvarConfig();
    }, suspender: false);

    /// <summary>
    /// Propriedades rápidas: janela pequena ao lado do cursor para a seleção (como o Quick Properties).
    /// Sem seleção prévia, pede os objetos e os deixa selecionados (a janela acompanha a seleção).
    /// </summary>
    [Rt.CommandMethod("ARMPALETARAPIDA", Rt.CommandFlags.UsePickSet | Rt.CommandFlags.Redraw)]
    public void ArmPaletaRapida() => Executar("ARMPALETARAPIDA", (doc, ed, db, cfg) =>
    {
        var impl = ed.SelectImplied();
        if (impl.Status != PromptStatus.OK || impl.Value == null || impl.Value.Count == 0)
        {
            var r = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelecione armaduras, faixas, tabelas ou detalhamentos" });
            if (r.Status != PromptStatus.OK) return;
            ed.SetImpliedSelection(r.Value.GetObjectIds());
        }
        if (!Paleta.PaletaRapida.Mostrar(doc))
            ed.WriteMessage("\nNada do plugin Armadura na seleção.");
        try { CadUtils.SetFocusToDwgView(); } catch (Exception) { }
    }, suspender: false);

    // ================= Relação do aço =================

    [Rt.CommandMethod("ARMRAATUALIZAR")]
    public void ArmRaAtualizar() => Executar("ARMRAATUALIZAR", (doc, ed, db, cfg) =>
    {
        using var tr = db.TransactionManager.StartTransaction();
        var (barras, posicoes) = ServicoRA.Atualizar(db, tr, cfg);
        tr.Commit();
        ed.WriteMessage($"\nRelação do aço atualizada: {barras} barra(s), {posicoes} posição(ões).");
    });

    [Rt.CommandMethod("ARMRAINSERIR")]
    public void ArmRaInserir() => Executar("ARMRAINSERIR", (doc, ed, db, cfg) =>
    {
        var op = OpcoesTabelaRA.Padrao(cfg);
        while (true)
        {
            ed.WriteMessage($"\nApresentação: {NomeApresentacao(op.Apresentacao)}; resumo: {NomeResumo(op.Resumo)}; " +
                            $"peso total: {SN(op.MostrarPesoTotal)}; detalhe de dobra: {SN(op.DetalheDobra)}. (Depois, mude pela paleta selecionando a tabela.)");
            var po = new PromptPointOptions("\nCanto superior esquerdo da relação do aço ou") { AppendKeywordsToMessage = true };
            AdicionarPalavras(po.Keywords, "Apresentacao", "Resumo", "PesoTotal", "Dobra");
            var r = ed.GetPoint(po);
            if (r.Status == PromptStatus.Keyword)
            {
                switch (r.StringResult)
                {
                    case "Apresentacao":
                    {
                        var pk = new PromptKeywordOptions("\nApresentação") { AppendKeywordsToMessage = true };
                        AdicionarPalavras(pk.Keywords, "TabelaEResumo", "Tabela", "Resumo");
                        var rk = ed.GetKeywords(pk);
                        if (rk.Status == PromptStatus.OK)
                            op.Apresentacao = rk.StringResult == "Tabela" ? ApresentacaoRA.SomenteTabela
                                : rk.StringResult == "Resumo" ? ApresentacaoRA.SomenteResumo : ApresentacaoRA.TabelaEResumo;
                        break;
                    }
                    case "Resumo":
                    {
                        var pk = new PromptKeywordOptions("\nColunas do resumo") { AppendKeywordsToMessage = true };
                        AdicionarPalavras(pk.Keywords, "Peso", "Barras", "BarrasEPeso");
                        var rk = ed.GetKeywords(pk);
                        if (rk.Status == PromptStatus.OK)
                            op.Resumo = rk.StringResult == "Barras" ? ApresentacaoResumo.Barras
                                : rk.StringResult == "BarrasEPeso" ? ApresentacaoResumo.BarrasEPeso : ApresentacaoResumo.Peso;
                        break;
                    }
                    case "PesoTotal": op.MostrarPesoTotal = !op.MostrarPesoTotal; break;
                    case "Dobra": op.DetalheDobra = !op.DetalheDobra; break;
                }
                continue;
            }
            if (r.Status != PromptStatus.OK) return;
            using var tr = db.TransactionManager.StartTransaction();
            ServicoRA.Inserir(db, tr, r.Value.TransformBy(ed.CurrentUserCoordinateSystem), cfg, op);
            tr.Commit();
            return;
        }
    });

    private static string NomeApresentacao(ApresentacaoRA a) => a switch
    {
        ApresentacaoRA.SomenteTabela => "somente tabela",
        ApresentacaoRA.SomenteResumo => "somente resumo",
        _ => "tabela e resumo",
    };

    private static string NomeResumo(ApresentacaoResumo r) => r switch
    {
        ApresentacaoResumo.Barras => "barras",
        ApresentacaoResumo.BarrasEPeso => "barras e peso",
        _ => "peso (kg)",
    };

    // ================= Configuração =================

    /// <summary>Menu de configurações (janela). A versão de linha de comando é o -ARMCONFIG.</summary>
    [Rt.CommandMethod("ARMCONFIG")]
    public void ArmConfig() => Executar("ARMCONFIG", (doc, ed, db, cfg) =>
    {
        bool doDesenho = ConfigDesenho.TemNoDesenho(db);
        using var f = new Paleta.FormConfiguracao(cfg, doDesenho, Plugin.CaminhoConfig);
        if (CadApp.ShowModalDialog(f) != System.Windows.Forms.DialogResult.OK) return;
        switch (f.Escolha)
        {
            case Paleta.FormConfiguracao.Acao.Salvar:
                if (doDesenho)
                {
                    using var tr = db.TransactionManager.StartTransaction();
                    ConfigDesenho.Gravar(db, tr, f.Editada);
                    tr.Commit();
                }
                else
                {
                    f.Editada.Salvar(Plugin.CaminhoConfig);
                    Plugin.RecarregarConfig();
                }
                AplicarConfiguracao(doc, db);
                ed.WriteMessage(doDesenho ? "\nConfiguração do desenho salva." : "\nConfiguração do usuário salva.");
                break;
            case Paleta.FormConfiguracao.Acao.CopiarParaDesenho:
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    ConfigDesenho.Gravar(db, tr, f.Editada);
                    tr.Commit();
                }
                AplicarConfiguracao(doc, db);
                ed.WriteMessage("\nA configuração foi gravada neste desenho; ele passa a usá-la mesmo em outro computador.");
                break;
            }
            case Paleta.FormConfiguracao.Acao.RemoverDoDesenho:
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    ConfigDesenho.Remover(db, tr);
                    tr.Commit();
                }
                AplicarConfiguracao(doc, db);
                ed.WriteMessage("\nO desenho voltou a usar a configuração do usuário.");
                break;
            }
            case Paleta.FormConfiguracao.Acao.EditarJson:
                string temp = System.IO.Path.Combine(Log.PastaDados, "config_desenho.json");
                if (doDesenho)
                {
                    cfg.Salvar(temp);
                    System.Diagnostics.Process.Start("notepad.exe", "\"" + temp + "\"");
                    ed.WriteMessage("\nEdite, salve e rode -ARMCONFIG → Recarregar para gravar no desenho.");
                }
                else
                {
                    Plugin.SalvarConfig();
                    System.Diagnostics.Process.Start("notepad.exe", "\"" + Plugin.CaminhoConfig + "\"");
                    ed.WriteMessage("\nEdite, salve e rode -ARMCONFIG → Recarregar.");
                }
                break;
        }
    }, suspender: false);

    /// <summary>Depois de mudar a configuração: recalcula barras e tabelas e regenera.</summary>
    private static void AplicarConfiguracao(Document doc, Database db)
    {
        var cfg = ConfigDesenho.Obter(db);
        using (Sincronizador.Suspender(doc))
        using (var tr = db.TransactionManager.StartTransaction())
        {
            if (ServicoRA.TemBarras(db, tr)) ServicoRA.Atualizar(db, tr, cfg);
            tr.Commit();
        }
        doc.Editor.Regen();
    }

    [Rt.CommandMethod("-ARMCONFIG")]
    public void ArmConfigLinha() => Executar("-ARMCONFIG", (doc, ed, db, cfg) =>
    {
        bool doDesenho = ConfigDesenho.TemNoDesenho(db);
        ed.WriteMessage(doDesenho
            ? "\nEste desenho tem configuração PRÓPRIA (gravada no DWG)."
            : $"\nEste desenho usa a configuração do usuário: {Plugin.CaminhoConfig}");
        var pk = new PromptKeywordOptions("\nConfiguração") { AllowNone = true, AppendKeywordsToMessage = true };
        AdicionarPalavras(pk.Keywords, "Abrir", "Recarregar", "GravarNoDesenho");
        if (doDesenho) AdicionarPalavras(pk.Keywords, "EditarDoDesenho", "RemoverDoDesenho");
        pk.Keywords.Default = "Abrir";
        var r = ed.GetKeywords(pk);
        if (r.Status != PromptStatus.OK) return;
        string temp = System.IO.Path.Combine(Log.PastaDados, "config_desenho.json");
        switch (r.StringResult)
        {
            case "Recarregar":
                Plugin.RecarregarConfig();
                // se a última edição foi da configuração do desenho, grava o arquivo temporário no DWG
                if (doDesenho && System.IO.File.Exists(temp))
                {
                    var cfgDesenho = ConfiguracaoArmadura.Carregar(temp);
                    using var tr = db.TransactionManager.StartTransaction();
                    ConfigDesenho.Gravar(db, tr, cfgDesenho);
                    tr.Commit();
                    System.IO.File.Delete(temp);
                    ed.WriteMessage("\nConfiguração do desenho atualizada.");
                }
                ed.Regen();
                ed.WriteMessage("\nConfiguração recarregada. Rode ARMRAATUALIZAR para aplicar às barras existentes.");
                break;
            case "GravarNoDesenho":
            {
                using var tr = db.TransactionManager.StartTransaction();
                ConfigDesenho.Gravar(db, tr, Plugin.Config);
                tr.Commit();
                ed.WriteMessage("\nA configuração do usuário foi copiada para este desenho; ele passa a usá-la mesmo em outro computador.");
                break;
            }
            case "EditarDoDesenho":
                ConfigDesenho.Obter(db).Salvar(temp);
                System.Diagnostics.Process.Start("notepad.exe", "\"" + temp + "\"");
                ed.WriteMessage("\nEdite, salve e rode ARMCONFIG → Recarregar para gravar no desenho.");
                break;
            case "RemoverDoDesenho":
            {
                using var tr = db.TransactionManager.StartTransaction();
                ConfigDesenho.Remover(db, tr);
                tr.Commit();
                ed.Regen();
                ed.WriteMessage("\nO desenho voltou a usar a configuração do usuário.");
                break;
            }
            default:
                Plugin.SalvarConfig();
                System.Diagnostics.Process.Start("notepad.exe", "\"" + Plugin.CaminhoConfig + "\"");
                ed.WriteMessage("\nEdite, salve e rode ARMCONFIG → Recarregar.");
                break;
        }
    }, suspender: false);

    [Rt.CommandMethod("ARMSOBRE")]
    public void ArmSobre()
    {
        var ed = CadApp.DocumentManager.MdiActiveDocument?.Editor;
        ed?.WriteMessage($"\nArmadura {Plugin.Versao} — detalhamento de armaduras para concreto armado." +
                         "\nO plugin NÃO dimensiona estruturas: só desenha, numera e quantifica." +
                         "\nTodas as decisões de cálculo são do engenheiro responsável." +
                         $"\nConfiguração: {Plugin.CaminhoConfig}\nLogs: {Log.PastaLogs}\n" +
                         (Demo.Ativa ? (Demo.Expirada ? Demo.Mensagem : Demo.TextoRestante) + "\n" : ""));
    }

    // ================= ARMLAYERS =================

    /// <summary>
    /// Padrão de layers (layers.csv): aplica cor, espessura e tipo de linha aos layers do desenho que estão no arquivo
    /// [Abrir] abre o arquivo para editar (pedido do usuário, 2026-10-30).
    /// </summary>
    [Rt.CommandMethod("ARMLAYERS")]
    public void ArmLayers() => Executar("ARMLAYERS", (doc, ed, db, cfg) =>
    {
        Layers.GarantirArquivo();
        ed.WriteMessage($"\nPadrão de layers: {Layers.Caminho}");
        var po = new PromptKeywordOptions("\nAplicar o padrão aos layers deste desenho ou") { AllowNone = true };
        AdicionarPalavras(po.Keywords, "Aplicar", "Abrir");
        po.Keywords.Default = "Aplicar";
        po.AppendKeywordsToMessage = true;
        var r = ed.GetKeywords(po);
        if (r.Status != PromptStatus.OK && r.Status != PromptStatus.None) return;
        if (r.StringResult == "Abrir")
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Layers.Caminho) { UseShellExecute = true });
            return;
        }
        int n = 0;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId id in lt)
            {
                var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                var p = Layers.Obter(l.Name);
                if (p == null) continue;
                l.UpgradeOpen();
                Layers.Aplicar(db, tr, l, p);
                n++;
            }
            tr.Commit();
        }
        ed.WriteMessage($"\n{n} layer(s) atualizados pelo padrão.\n");
    });

    // ================= auxiliares =================

    internal static void Executar(string nome, Action<Document, Editor, Database, ConfiguracaoArmadura> acao, bool suspender = true)
    {
        var doc = CadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) return;
        if (Demo.Expirada) { Log.Info($"demonstração expirada: {nome} bloqueado"); doc.Editor.WriteMessage("\n" + Demo.Mensagem + "\n"); return; }
        try
        {
            using (suspender ? Sincronizador.Suspender(doc) : null)
                acao(doc, doc.Editor, doc.Database, ConfigDesenho.Obter(doc.Database));
        }
        catch (Exception ex)
        {
            Log.Erro(nome, ex);
            doc.Editor.WriteMessage($"\n{nome}: erro — {ex.Message} (detalhes em {Log.PastaLogs})");
        }
    }

    private static bool _semFlush;

    /// <summary>Mostra na tela o que ainda está na transação aberta (pré-visualização da barra).</summary>
    private static void AtualizarTela(Transaction tr, Editor ed)
    {
        // GstarCAD 2021 não implementa QueueForGraphicsFlush (eNotImplementedYet): tenta uma vez só
        if (!_semFlush)
        {
            try { tr.TransactionManager.QueueForGraphicsFlush(); }
            catch (Rt.Exception) { _semFlush = true; }
        }
        try { ed.UpdateScreen(); } catch (Rt.Exception) { }
    }

    internal static DadosArmadura NovaBarra(ConfiguracaoArmadura cfg) => new DadosArmadura
    {
        Bitola = cfg.BitolaCorrente,
        Aco = cfg.AcoCorrente,
        Quantidade = Math.Max(1, cfg.QuantidadeCorrente),
        Espacamento = cfg.EspacamentoCorrente,
        Escala = cfg.Escala,
    };

    private static string ResumoCorrente(ConfiguracaoArmadura cfg) =>
        $"ø{cfg.BitolaCorrente.ToString("0.0", CultureInfo.InvariantCulture)} {cfg.AcoCorrente}, quantidade {cfg.QuantidadeCorrente}" +
        (cfg.EspacamentoCorrente > 0 ? $", c/{CalculoComprimento.NumeroCompacto(cfg.EspacamentoCorrente)}" : "");

    private static void PedirValorCorrente(Editor ed, ConfiguracaoArmadura cfg, string opcao)
    {
        switch (opcao)
        {
            case "Bitola":
            {
                var v = PedirBitola(ed, cfg, cfg.BitolaCorrente);
                if (v == null) break;
                cfg.BitolaCorrente = v.Value;
                var b = cfg.Barras.Obter(v.Value, cfg.AcoCorrente);
                if (b != null && Core.Tabelas.TabelaBarras.EhCordoalha(b.Aco) != Core.Tabelas.TabelaBarras.EhCordoalha(cfg.AcoCorrente))
                    cfg.AcoCorrente = b.Aco; // ø12.7 → CP-190; voltar para ø10 → CA-50
                break;
            }
            case "Aco": { var v = PedirAco(ed, cfg.AcoCorrente); if (v != null) cfg.AcoCorrente = v; break; }
            case "Quantidade": { var v = PedirInteiro(ed, "Quantidade", cfg.QuantidadeCorrente, 1); if (v != null) cfg.QuantidadeCorrente = v.Value; break; }
            case "Espacamento": { var v = PedirReal(ed, "Espaçamento (cm, 0 = não indicar)", cfg.EspacamentoCorrente, 0); if (v != null) cfg.EspacamentoCorrente = v.Value; break; }
        }
        // valores correntes são sempre do usuário, mesmo quando o desenho tem configuração própria
        var u = Plugin.Config;
        u.BitolaCorrente = cfg.BitolaCorrente;
        u.AcoCorrente = cfg.AcoCorrente;
        u.QuantidadeCorrente = cfg.QuantidadeCorrente;
        u.EspacamentoCorrente = cfg.EspacamentoCorrente;
        ConfigDesenho.SincronizarCorrentes();
        Plugin.SalvarConfig();
    }

    private static double? PedirBitola(Editor ed, ConfiguracaoArmadura cfg, double atual)
    {
        var ativas = cfg.Barras.Ativas.Select(b => b.Diametro.ToString("0.0", CultureInfo.InvariantCulture)).Distinct();
        ed.WriteMessage($"\nBitolas ativas (mm): {string.Join("  ", ativas)}");
        var v = PedirReal(ed, "Bitola (mm)", atual, 1);
        if (v == null) return null;
        var b = cfg.Barras.Obter(v.Value);
        if (b == null) { ed.WriteMessage("\nBitola fora da tabela de barras (ARMCONFIG)."); return null; }
        if (!b.Ativa) ed.WriteMessage("\nAviso: bitola inativa na configuração.");
        return b.Diametro;
    }

    private static string PedirAco(Editor ed, string atual)
    {
        var pk = new PromptKeywordOptions("\nAço") { AppendKeywordsToMessage = true, AllowNone = true };
        AdicionarPalavras(pk.Keywords, "CA25", "CA50", "CA60", "CP190");
        pk.Keywords.Default = (atual ?? "CA-50").Replace("-", "");
        var r = ed.GetKeywords(pk);
        if (r.Status != PromptStatus.OK) return null;
        return r.StringResult.Insert(2, "-");
    }

    internal static int? PedirInteiro(Editor ed, string nome, int atual, int minimo)
    {
        var po = new PromptIntegerOptions($"\n{nome}") { DefaultValue = atual, UseDefaultValue = true, LowerLimit = minimo, AllowNone = true };
        var r = ed.GetInteger(po);
        return r.Status == PromptStatus.OK ? r.Value : (int?)null;
    }

    internal static double? PedirReal(Editor ed, string nome, double atual, double minimo)
    {
        var po = new PromptDoubleOptions($"\n{nome}")
        {
            DefaultValue = Math.Round(atual, 4), UseDefaultValue = true, AllowNegative = false, AllowZero = minimo <= 0, AllowNone = true,
        };
        var r = ed.GetDouble(po);
        if (r.Status != PromptStatus.OK) return null;
        if (r.Value < minimo) { ed.WriteMessage($"\nValor mínimo: {minimo}."); return null; }
        return r.Value;
    }

    private static CamposDescricao? PedirCampos(Editor ed, CamposDescricao atual)
    {
        var c = atual;
        while (true)
        {
            string Marca(CamposDescricao f) => c.HasFlag(f) ? "x" : " ";
            ed.WriteMessage($"\nDescrição: [{Marca(CamposDescricao.Quantidade)}] quantidade  [{Marca(CamposDescricao.Numero)}] N  " +
                            $"[{Marca(CamposDescricao.Bitola)}] bitola  [{Marca(CamposDescricao.Espacamento)}] espaçamento  " +
                            $"[{Marca(CamposDescricao.Comprimento)}] comprimento");
            var pk = new PromptKeywordOptions("\nLigar/desligar") { AllowNone = true, AppendKeywordsToMessage = true };
            AdicionarPalavras(pk.Keywords, "Quantidade", "Numero", "Bitola", "Espacamento", "Comprimento", "Ok");
            pk.Keywords.Default = "Ok";
            var r = ed.GetKeywords(pk);
            if (r.Status != PromptStatus.OK) return null;
            switch (r.StringResult)
            {
                case "Ok": return c;
                case "Quantidade": c ^= CamposDescricao.Quantidade; break;
                case "Numero": c ^= CamposDescricao.Numero; break;
                case "Bitola": c ^= CamposDescricao.Bitola; break;
                case "Espacamento": c ^= CamposDescricao.Espacamento; break;
                case "Comprimento": c ^= CamposDescricao.Comprimento; break;
            }
        }
    }

    /// <summary>
    /// Barra selecionada: usa a seleção prévia (pickfirst) se ela for uma única armadura; senão pede.
    /// Os comandos que usam isto precisam de CommandFlags.UsePickSet.
    /// </summary>
    internal static ObjectId SelecionarBarra(Editor ed, Database db, string msg)
    {
        var impl = ed.SelectImplied();
        if (impl.Status == PromptStatus.OK && impl.Value != null)
        {
            ed.SetImpliedSelection(new ObjectId[0]);
            ObjectId unica = ObjectId.Null;
            int quantas = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in impl.Value.GetObjectIds())
                {
                    var pl = ServicoBarra.ResolverBarra(tr, id);
                    if (pl != null && pl.ObjectId != unica) { unica = pl.ObjectId; quantas++; }
                }
                tr.Commit(); // nunca abortar transações (armadilha do GstarCAD)
            }
            if (quantas == 1) return unica;
        }
        while (true)
        {
            var r = ed.GetEntity(new PromptEntityOptions(msg));
            if (r.Status != PromptStatus.OK) return ObjectId.Null;
            ObjectId achada;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                achada = ServicoBarra.ResolverBarra(tr, r.ObjectId)?.ObjectId ?? ObjectId.Null;
                tr.Commit();
            }
            if (!achada.IsNull) return achada;
            ed.WriteMessage("\nIsto não é uma armadura.");
        }
    }

    internal static void AdicionarPalavras(KeywordCollection kw, params string[] palavras)
    {
        foreach (var p in palavras) kw.Add(p);
    }

    private static string SN(bool v) => v ? "sim" : "não";

    private static string NomeFormato(FormatoSegmento f) => f switch
    {
        FormatoSegmento.Gancho45 => "gancho 45°",
        FormatoSegmento.Gancho90 => "gancho 90°",
        FormatoSegmento.Gancho180 => "gancho 180°",
        FormatoSegmento.Curva => "curva",
        _ => "linha",
    };
}
