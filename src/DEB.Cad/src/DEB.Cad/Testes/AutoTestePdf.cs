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

            var criar = Environment.GetEnvironmentVariable("ARM_PDFLOTE_CRIAR");
            if (!string.IsNullOrEmpty(criar)) { var p = criar.Split('|'); CriarVariosLayouts(p[0], p[1]); L("criado " + p[1]); }
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
                L(FormattableString.Invariant($"folha {f.NomeDesenho} / {f.Layout}: {f.Largura:0.#} x {f.Altura:0.#} ({f.OrigemCaixa}) → {f.Formato?.Nome ?? "?"} (dif {f.Reconhecido?.Diferenca:0.#}, fator {f.Reconhecido?.Fator}) estilo atual {f.EstiloAtual} {f.Erro} | page setup: {f.PageSetupAtual}"));

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
            if (cancelado) L("CANCELADO");
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
                L($"{(r.Pdf != null ? "[OK]" : "[FALHA]")} {r.Folha.NomeDesenho} / {r.Folha.Layout} → {r.Pdf ?? r.Erro} | {r.Info}");
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
