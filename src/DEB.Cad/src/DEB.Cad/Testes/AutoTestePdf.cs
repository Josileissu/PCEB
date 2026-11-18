using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DEB.Cad.Infra;
using DEB.Cad.Pdf;
using DEB.Core.Documentacao;

[assembly: Rt.CommandClass(typeof(DEB.Cad.Testes.AutoTestePdf))]

namespace DEB.Cad.Testes;

/// <summary>
/// ARMTESTEPDFLOTE: o lote do ARMPDFLOTE sem janela, para a instância de teste. Variáveis de ambiente:
/// ARM_PDFLOTE_ARQUIVOS = DWGs separados por ";" (ou uma pasta), ARM_PDFLOTE_DESTINO = pasta dos PDFs (vazio = pasta do DWG),
/// ARM_PDFLOTE_AREA = Moldura|Extensao|Layout, ARM_PDFLOTE_IMPRESSORA = impressora (padrão: a do CAD), ARM_PDFLOTE_CTB = tabela de penas, ARM_PDFLOTE_ABERTOS = 1 (abre os DWGs antes,
/// para testar o caminho "desenhos abertos"), ARM_PDFLOTE_FIM = 1 (encerra a instância de teste no fim). Relatório em %AppData%\PCEB\DEB.Cad\logs\autoteste_pdflote.txt.
/// </summary>
public sealed class AutoTestePdf
{
    /// <summary>
    /// Caso de teste com 3 layouts: abre <paramref name="origem"/>, copia o Layout1 para "Folha 2", cria "A4 teste" com um retângulo
    /// 29,7 × 21 (A4 deitado em cm, como as pranchas de teste) e salva como <paramref name="destino"/>.
    /// </summary>
    private static void CriarVariosLayouts(string origem, string destino)
    {
        var dm = CadApp.DocumentManager;
        var doc = dm.Open(origem, true);
        dm.MdiActiveDocument = doc;
        try
        {
            using (doc.LockDocument())
            {
                var lm = LayoutManager.Current;
                lm.CopyLayout("Layout1", "Folha 2");
                var id = lm.CreateLayout("A4 teste");
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var lay = (Layout)tr.GetObject(id, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForWrite);
                    var pl = new Polyline();
                    pl.SetDatabaseDefaults(doc.Database);
                    pl.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(29.7, 0), 0, 0, 0);
                    pl.AddVertexAt(2, new Point2d(29.7, 21), 0, 0, 0);
                    pl.AddVertexAt(3, new Point2d(0, 21), 0, 0, 0);
                    pl.Closed = true;
                    btr.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                    var tx = new DBText { Position = new Point3d(2, 2, 0), Height = 2, TextString = "A4 DEITADA - canto inferior esquerdo" };
                    tx.SetDatabaseDefaults(doc.Database);
                    btr.AppendEntity(tx); tr.AddNewlyCreatedDBObject(tx, true);
                    var ln = new Line(new Point3d(0, 0, 0), new Point3d(29.7, 21, 0));
                    ln.SetDatabaseDefaults(doc.Database);
                    ln.ColorIndex = 1; // vermelho: com ctb monocromático tem de sair preto
                    btr.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
                    tr.Commit();
                }
                lm.CurrentLayout = "Layout1";
                doc.Database.SaveAs(destino, DwgVersion.Current);
            }
        }
        finally { doc.CloseAndDiscard(); }
    }

    /// <summary>
    /// DWG como os recebidos de outro computador (relato do usuário, 17/11/2026): folhas só no Model (duas molduras A1 a 1:50 em
    /// cm, uma seção 42 × 29,7 dentro da 1ª que NÃO pode virar folha), Layout1 vazio e o page setup do Model e do Layout1
    /// apontando para impressora, papel e ctb que não existem. O page setup é escrito pelo DXF (o validador do CAD não aceita
    /// impressora inexistente): DxfOut → troca os grupos 2/4/7 do AcDbPlotSettings → DxfIn → SaveAs.
    /// </summary>
    private static void CriarSoModel(string destino)
    {
        var dxf1 = Path.ChangeExtension(destino, ".orig.dxf");
        var dxf2 = Path.ChangeExtension(destino, ".editado.dxf");
        using (var db = new Database(true, true))
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                void Ret(double x0, double y0, double l, double a, int cor)
                {
                    var pl = new Polyline();
                    pl.SetDatabaseDefaults(db);
                    pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(x0 + l, y0), 0, 0, 0);
                    pl.AddVertexAt(2, new Point2d(x0 + l, y0 + a), 0, 0, 0);
                    pl.AddVertexAt(3, new Point2d(x0, y0 + a), 0, 0, 0);
                    pl.Closed = true;
                    pl.ColorIndex = cor;
                    ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                }
                void Txt(double x, double y, string s)
                {
                    var t = new DBText { Position = new Point3d(x, y, 0), Height = 100, TextString = s };
                    t.SetDatabaseDefaults(db);
                    ms.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
                }
                Ret(0, 0, 4205, 2970, 7);          // A1 a 1:50 em cm
                Ret(5000, 0, 4205, 2970, 7);       // 2ª folha
                Ret(500, 500, 42, 29.7, 3);        // seção de pilar (A3 a 1:1 em cm, dentro da folha)
                Txt(200, 2600, "FOLHA 1 - so no Model");
                Txt(5200, 2600, "FOLHA 2 - so no Model");
                var ln = new Line(new Point3d(0, 0, 0), new Point3d(4205, 2970, 0));
                ln.SetDatabaseDefaults(db);
                ln.ColorIndex = 1; // vermelho: com o PCEB.ctb sai vermelho (exceção da tabela); cor 3 sai preta
                ms.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
                var ln2 = new Line(new Point3d(5000, 0, 0), new Point3d(9205, 2970, 0));
                ln2.SetDatabaseDefaults(db);
                ln2.ColorIndex = 3; // verde: com o PCEB.ctb sai PRETO
                ms.AppendEntity(ln2); tr.AddNewlyCreatedDBObject(ln2, true);
                tr.Commit();
            }
            db.SalvarDxf(dxf1, 16);
        }
        // page setup "de outro computador"
        var l = File.ReadAllLines(dxf1, Encoding.Default).ToList();
        var saida = new List<string>();
        bool dentro = false, viu7 = false;
        for (int i = 0; i + 1 < l.Count; i += 2)
        {
            var cod = l[i].Trim(); var val = l[i + 1];
            if (cod == "100")
            {
                if (dentro && !viu7) { saida.Add("  7"); saida.Add("escritorio_inexistente.ctb"); }
                dentro = val.Trim() == "AcDbPlotSettings"; viu7 = false;
            }
            else if (dentro && cod == "2") val = "Plotter Inexistente.pc3";
            else if (dentro && cod == "4") val = "ISO_A1_(841.00_x_594.00_MM)";
            else if (dentro && cod == "7") { val = "escritorio_inexistente.ctb"; viu7 = true; }
            saida.Add(l[i]); saida.Add(val);
        }
        File.WriteAllLines(dxf2, saida, Encoding.Default);
        using (var db2 = new Database(false, true))
        {
            db2.DxfIn(dxf2, Path.ChangeExtension(destino, ".dxfin.log"));
            db2.SaveAs(destino, DwgVersion.Current);
        }
    }

    private static string LayoutManagerCorrente(Document d)
    {
        try { return (string)CadApp.GetSystemVariable("CTAB") + (d == CadApp.DocumentManager.MdiActiveDocument ? " (ativo)" : ""); }
        catch { return "?"; }
    }

    /// <summary>Lista as entidades do espaço do papel (e, um nível abaixo, dos blocos) com layer e extensão.</summary>
    private static void Diagnostico(string arq, Action<string> L)
    {
        using var db = new Database(false, true);
        db.ReadDwgFile(arq, FileOpenMode.OpenForReadAndAllShare, true, "");
        db.CloseInput(true);
        var antigo = HostApplicationServices.WorkingDatabase;
        HostApplicationServices.WorkingDatabase = db;
        try
        {
            using var tr = db.TransactionManager.StartTransaction();
            var dic = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry e in dic)
            {
                var lay = (Layout)tr.GetObject(e.Value, OpenMode.ForRead);
                if (lay.ModelType) continue;
                L($"DIAG {Path.GetFileName(arq)} / {lay.LayoutName} limites {lay.Limits.MinPoint}-{lay.Limits.MaxPoint}");
                var btr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    string ext; try { var x = ent.GeometricExtents; ext = FormattableString.Invariant($"{x.MinPoint.X:0.##},{x.MinPoint.Y:0.##} - {x.MaxPoint.X:0.##},{x.MaxPoint.Y:0.##}"); } catch { ext = "(sem)"; }
                    L($"  {ent.GetType().Name} [{ent.Layer}] {ext}");
                    if (ent is BlockReference br)
                    {
                        var col = new DBObjectCollection();
                        try { br.Explode(col); } catch (Exception ex) { L("    explode: " + ex.Message); }
                        foreach (DBObject o in col)
                        {
                            if (o is Entity s2)
                            {
                                string e2; try { var x = s2.GeometricExtents; e2 = FormattableString.Invariant($"{x.MinPoint.X:0.##},{x.MinPoint.Y:0.##} - {x.MaxPoint.X:0.##},{x.MaxPoint.Y:0.##} ({x.MaxPoint.X - x.MinPoint.X:0.##} x {x.MaxPoint.Y - x.MinPoint.Y:0.##})"); } catch { e2 = "(sem)"; }
                                if (!(s2 is DBText) && !(s2 is AttributeDefinition)) L($"    {s2.GetType().Name} [{s2.Layer}] vis={s2.Visible} {e2}");
                            }
                            o.Dispose();
                        }
                    }
                }
            }
            tr.Commit();
        }
        finally { HostApplicationServices.WorkingDatabase = antigo; }
    }

    /// <summary>
    /// Diagnóstico: mídias da impressora ARM_PDFLOTE_IMPRESSORA (padrão PDF24) com o tamanho e as margens que o CAD considera,
    /// em ARM_MIDIAS_SAIDA (arquivo); depois encerra a própria instância de teste.
    /// </summary>
    [Rt.CommandMethod("ARMTESTEMIDIAS", Rt.CommandFlags.Session)]
    public void Midias()
    {
        var linhas = new List<string>();
        try
        {
            string imp = Environment.GetEnvironmentVariable("ARM_PDFLOTE_IMPRESSORA");
            if (string.IsNullOrEmpty(imp)) imp = "PDF24";
            linhas.Add("impressora " + imp);
            linhas.AddRange(PlotadorPdf.ListarMidias(imp));
        }
        catch (Exception ex) { linhas.Add("ERRO: " + ex); }
        string saida = Environment.GetEnvironmentVariable("ARM_MIDIAS_SAIDA");
        if (!string.IsNullOrEmpty(saida)) File.WriteAllLines(saida, linhas);
        if (!string.IsNullOrEmpty(saida)) System.Diagnostics.Process.GetCurrentProcess().Kill();
    }

    [Rt.CommandMethod("ARMTESTEPDFLOTE", Rt.CommandFlags.Session)]
    public void Lote()
    {
        var rel = new StringBuilder();
        void L(string s) { rel.AppendLine(s); Log.Info("autoteste pdf: " + s); }
        try
        {
            var cfg = new ConfigLotePdf
            {
                EstiloPlotagem = Environment.GetEnvironmentVariable("ARM_PDFLOTE_CTB") ?? "",
                SobrescreverTudo = true,
            };
            cfg.GravarNoLayout = Environment.GetEnvironmentVariable("ARM_PDFLOTE_GRAVAR") == "1";
            var dest = Environment.GetEnvironmentVariable("ARM_PDFLOTE_DESTINO");
            if (!string.IsNullOrEmpty(dest)) { cfg.NaPastaDoDesenho = false; cfg.PastaDestino = dest; }
            if (Enum.TryParse(Environment.GetEnvironmentVariable("ARM_PDFLOTE_AREA") ?? "", out AreaPlotLote area)) cfg.Area = area;
            cfg.NoAutoCad = ComandosPdf.NoAutoCad;
            var imp = Environment.GetEnvironmentVariable("ARM_PDFLOTE_IMPRESSORA"); // ex.: "AutoCAD PDF (High Quality Print).pc3"
            if (!string.IsNullOrEmpty(imp)) cfg.ImpressoraLote = imp;
            L($"impressora {cfg.ImpressoraLote}, estilo '{cfg.EstiloPlotagem}', área {cfg.Area}");
            try { foreach (var m in PlotadorPdf.ListarMidias(cfg.ImpressoraLote)) L("  mídia " + m); }
            catch (Exception ex) { L("  ERRO ao listar mídias: " + ex.Message); }
            L("estilos: " + string.Join(", ", PlotadorPdf.EstilosDisponiveis()));
            L("dispositivos do CAD (combo): " + string.Join(" | ", PlotadorPdf.Dispositivos().Where(d => !ImpressorasPdf.EhNenhuma(d))));
            // ARM_PDFLOTE_CRIARFOLHAS = 1: o botão "Criar folhas na impressora" para a impressora do lote (.pc3), sem as caixas de
            // diálogo. GRAVA o .pmp da impressora: faça backup da pasta Plotters antes e restaure depois
            if (Environment.GetEnvironmentVariable("ARM_PDFLOTE_CRIARFOLHAS") == "1")
            {
                try
                {
                    var plano = FolhasPc3.Planejar(cfg.ImpressoraLote, cfg.Formatos);
                    L($"criar folhas: {plano.Mudancas} mudança(s), pmp {plano.Pmp} (novo {plano.PmpNovo}, religar {plano.Religar})");
                    if (plano.Mudancas > 0) L(FolhasPc3.Aplicar(plano));
                    var depois = PlotadorPdf.NomesMidias(cfg.ImpressoraLote) ?? new HashSet<string>();
                    var faltam = cfg.Formatos.Where(f => !depois.Contains(f.Nome.Trim())).Select(f => f.Nome).ToList();
                    L(faltam.Count == 0 ? "criar folhas: o CAD já lista todas as folhas da lista" : "criar folhas: o CAD AINDA NÃO lista: " + string.Join(", ", faltam));
                }
                catch (Exception ex) { L("criar folhas: ERRO " + ErrosLotePdf.Classificar(ex, EtapaLotePdf.Configurar).Resumo + " — " + ex.Message); }
            }

            var criar = Environment.GetEnvironmentVariable("ARM_PDFLOTE_CRIAR");
            if (!string.IsNullOrEmpty(criar)) { var p = criar.Split('|'); CriarVariosLayouts(p[0], p[1]); L("criado " + p[1]); }
            var soModel = Environment.GetEnvironmentVariable("ARM_PDFLOTE_CRIARMODEL"); // DWG só com Model e page setup de outro PC
            if (!string.IsNullOrEmpty(soModel)) { CriarSoModel(soModel); L("criado " + soModel); }
            L("pasta de estilos: " + (PlotadorPdf.PastaEstilos() ?? "(não achada)"));
            if (Environment.GetEnvironmentVariable("ARM_PDFLOTE_FOLHAS_DIAG") == "1")
            {
                // só leitura: o que o botão "Criar folhas na impressora" diria (nada é gravado)
                foreach (var i in new[] { "Microsoft Print to PDF", "PDF24" })
                    L($"motivo sem folhas em {i}: {PlotadorPdf.MotivoSemFolhas(i) ?? "(nenhum: basta criar)"}");
                L("pasta dos .pc3: " + (FolhasPc3.PastaPc3() ?? "(não achada)"));
                foreach (var pc3 in new[] { "DWG To PDF.pc3", "Inexistente.pc3" })
                {
                    try { var p = FolhasPc3.Planejar(pc3, cfg.Formatos); L($"plano {pc3}: {p.Mudancas} mudança(s), pmp {p.Pmp}"); }
                    catch (Exception ex) { L($"plano {pc3}: {ErrosLotePdf.Classificar(ex, EtapaLotePdf.Configurar).Resumo}"); }
                }
            }
            var entrada = Environment.GetEnvironmentVariable("ARM_PDFLOTE_ARQUIVOS") ?? "";
            var arquivos = Directory.Exists(entrada)
                ? Directory.GetFiles(entrada, "*.dwg").OrderBy(a => a).ToList()
                : entrada.Split(';').Where(a => a.Trim().Length > 0).Select(a => a.Trim()).ToList();
            var folhas = new List<FolhaPdf>();
            bool abertos = Environment.GetEnvironmentVariable("ARM_PDFLOTE_ABERTOS") == "1";
            foreach (var a in arquivos)
            {
                if (abertos)
                {
                    var d = CadApp.DocumentManager.Open(a, true);
                    folhas.AddRange(LeitorFolhas.DoDocumento(d, cfg));
                }
                else folhas.AddRange(LeitorFolhas.DoArquivo(a, cfg));
            }
            if (Environment.GetEnvironmentVariable("ARM_PDFLOTE_DIAG") == "1")
                foreach (var a in arquivos) Diagnostico(a, L);
            foreach (var f in folhas)
                L(FormattableString.Invariant($"folha {f.NomeDesenho} / {f.Rotulo} (layout {f.Layout}, marcada {f.Marcada}): {f.Largura:0.#} x {f.Altura:0.#} ({f.OrigemCaixa}) → {f.Formato?.Nome ?? "?"} (dif {f.Reconhecido?.Diferenca:0.#}, fator {f.Reconhecido?.Fator}) estilo atual {f.EstiloAtual} {f.Erro} | page setup: {f.PageSetupAtual}"));
            // ARM_PDFLOTE_FORMATO_ERRADO = nome de um formato que não existe na impressora: força o erro "papel não existe" na 1ª folha
            var formErrado = Environment.GetEnvironmentVariable("ARM_PDFLOTE_FORMATO_ERRADO");
            if (!string.IsNullOrEmpty(formErrado) && folhas.Count > 0)
            {
                var alvo = folhas.First(f => f.Formato != null);
                alvo.Formato = new FormatoFolha(formErrado, alvo.Formato.Largura, alvo.Formato.Altura);
                L($"forçado formato inexistente '{formErrado}' em {alvo.NomeDesenho} / {alvo.Rotulo}");
            }
            // ARM_PDFLOTE_TRAVAR = 1: o PDF de destino da última folha fica aberto (exclusivo) durante o lote
            FileStream trava = null;
            if (Environment.GetEnvironmentVariable("ARM_PDFLOTE_TRAVAR") == "1")
            {
                var ultima = folhas.Last(f => f.Formato != null);
                var destTrava = ExecutorLotePdf.Destinos(folhas.Where(f => f.Formato != null).ToList(), cfg)[ultima];
                Directory.CreateDirectory(Path.GetDirectoryName(destTrava));
                File.WriteAllText(destTrava, "travado");
                trava = new FileStream(destTrava, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                L("travado " + destTrava);
            }

            var png = Environment.GetEnvironmentVariable("ARM_PDFLOTE_JANELA");
            if (!string.IsNullOrEmpty(png))
            {
                // a janela do ARMPDFLOTE com os mesmos DWGs (PNG para conferir); não exporta por ela
                using var j = new Paleta.FormPdfLote(cfg);
                j.Show();
                j.AdicionarArquivos(arquivos);
                System.Windows.Forms.Application.DoEvents();
                using (var bmp = new System.Drawing.Bitmap(j.Width, j.Height))
                {
                    j.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, j.Width, j.Height));
                    bmp.Save(png);
                }
                j.Close();
                L("janela salva em " + png);
            }
            if (Environment.GetEnvironmentVariable("ARM_PDFLOTE_SOLEITURA") == "1") return;
            var t0 = DateTime.Now;
            // mesmo caminho do comando: janela de progresso + executor (sem perguntas: SobrescreverTudo)
            // ARM_PDFLOTE_CANCELAR_APOS = n: simula o botão Cancelar depois de n folhas
            int.TryParse(Environment.GetEnvironmentVariable("ARM_PDFLOTE_CANCELAR_APOS") ?? "", out int cancelarApos);
            int consultas = 0;
            var (res, cancelado) = ComandosPdf.Executar(folhas.Where(f => f.Formato != null).ToList(), cfg, false,
                () => cancelarApos > 0 && Directory.Exists(cfg.PastaDestino) && Directory.GetFiles(cfg.PastaDestino, "*.pdf").Length >= cancelarApos && ++consultas > 0);
            trava?.Dispose();
            if (cancelado) L("CANCELADO");
            L("---- relatório ----");
            L(Paleta.FormRelatorioPdf.Texto(res, cancelado));
            L("---- copiar detalhes ----");
            L(Paleta.FormRelatorioPdf.DetalhesParaCopiar(res, cancelado));
            var relPng = Environment.GetEnvironmentVariable("ARM_PDFLOTE_RELATORIO_PNG");
            if (!string.IsNullOrEmpty(relPng))
            {
                using var j = new Paleta.FormRelatorioPdf(res, cancelado);
                j.Show();
                System.Windows.Forms.Application.DoEvents();
                using (var bmp = new System.Drawing.Bitmap(j.Width, j.Height))
                {
                    j.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, j.Width, j.Height));
                    bmp.Save(relPng);
                }
                // botão "Copiar detalhes do erro" pelo próprio Click (sem mouse): confere a área de transferência
                var copiar = j.Controls.Find("copiar", true).FirstOrDefault() as System.Windows.Forms.Button;
                if (copiar != null && copiar.Enabled)
                {
                    copiar.PerformClick();
                    try { L("área de transferência: " + (System.Windows.Forms.Clipboard.GetText() ?? "").Split('\n').FirstOrDefault()); } catch (Exception ex) { L("clipboard: " + ex.Message); }
                }
                j.Close();
                L("relatório salvo em " + relPng);
            }
            // desenhos abertos: o layout corrente volta e (sem "gravar no layout") o page setup salvo não muda
            foreach (Document d in CadApp.DocumentManager)
            {
                using (d.LockDocument())
                using (var tr = d.Database.TransactionManager.StartTransaction())
                {
                    var dic = (DBDictionary)tr.GetObject(d.Database.LayoutDictionaryId, OpenMode.ForRead);
                    foreach (DBDictionaryEntry e in dic)
                    {
                        var lay = (Layout)tr.GetObject(e.Value, OpenMode.ForRead);
                        if (!lay.ModelType) L($"depois: {Path.GetFileName(d.Name)} / {lay.LayoutName}: {lay.PlotConfigurationName} | {lay.CanonicalMediaName} | {lay.PlotType} | {lay.CurrentStyleSheet}");
                    }
                    tr.Commit();
                }
                L($"depois: {Path.GetFileName(d.Name)} CTAB={d.Database.TileMode}/{LayoutManagerCorrente(d)}");
            }
            L(FormattableString.Invariant($"lote em {(DateTime.Now - t0).TotalSeconds:0.#} s"));
            foreach (var r in res)
                L($"{(r.Pdf != null ? "[OK]" : "[FALHA]")} {r.Folha.NomeDesenho} / {r.Folha.Rotulo} → {r.Pdf ?? r.Erro} | {r.Info} | causa {r.Diagnostico?.Causa} | avisos {string.Join("; ", r.Avisos)}");
        }
        catch (Exception ex) { L("ERRO: " + ex); }
        finally
        {
            Directory.CreateDirectory(Log.PastaLogs);
            File.WriteAllText(Path.Combine(Log.PastaLogs, "autoteste_pdflote.txt"), rel.ToString(), Encoding.UTF8);
        }
        // ARM_PDFLOTE_FIM=1: encerra a instância de TESTE aqui (o roteiro .scr segue rodando quando o lote abre desenhos, e um
        // ARMTESTEFIM na linha seguinte mataria o CAD no meio do lote)
        if (Environment.GetEnvironmentVariable("ARM_PDFLOTE_FIM") == "1")
        {
            Log.Info("autoteste pdf: fim — encerrando a instância de teste");
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
    }
}
