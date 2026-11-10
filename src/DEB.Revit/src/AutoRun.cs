using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using DEB.Revit.Core;
using DEB.Revit.Precast;

namespace DEB.Revit
{
    /// <summary>
    /// Execução sem interação, para testes automatizados. Ativada por variáveis de ambiente
    /// ao abrir o Revit:
    ///   SD_AUTORUN  = diag   → grava o diagnóstico do modelo assim que um projeto estiver aberto
    ///   SD_AUTOEXIT = 1      → fecha o Revit ao terminar
    /// O resultado (caminho do arquivo gerado ou o erro) vai para %AppData%\PCEB\DEB.Revit\logs\autorun.txt.
    /// Sem essas variáveis, nada acontece.
    /// </summary>
    internal static class AutoRun
    {
        private static string _task;
        private static bool _exit;

        public static void Register(UIControlledApplication app)
        {
            _task = Environment.GetEnvironmentVariable("SD_AUTORUN");
            if (string.IsNullOrWhiteSpace(_task)) return;
            _exit = Environment.GetEnvironmentVariable("SD_AUTOEXIT") == "1";
            app.Idling += OnIdling;
        }

        private static void OnIdling(object sender, IdlingEventArgs e)
        {
            var uiapp = (UIApplication)sender;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null || uidoc.Document.IsFamilyDocument) return;   // ainda abrindo o projeto

            uiapp.Idling -= OnIdling;
            string status;
            try
            {
                switch (_task.Trim().ToLowerInvariant())
                {
                    case "diag":
                        status = "OK " + ModelDiagnostics.Run(uidoc.Document);
                        break;
                    case "probe":
                        status = "OK " + PrecastProbe.Run(uidoc.Document);
                        break;
                    case "precast":
                        status = "OK " + PrecastAutoTest(uidoc);
                        break;
                    case "conferir":
                        status = "OK " + ReviewAutoTest(uidoc.Document);
                        break;
                    case "conferireditar":
                        status = "OK " + ReviewEditAutoTest(uidoc.Document);
                        break;
                    case "exportarpecas":
                        status = "OK " + ExportPiecesAutoTest(uidoc.Document);
                        break;
                    case "ferramentas":
                        status = "OK " + Tools.ToolsAutoTest.Run(uiapp, uidoc);
                        break;
                    default:
                        status = "ERRO tarefa desconhecida: " + _task;
                        break;
                }
            }
            catch (Exception ex)
            {
                status = "ERRO " + ex;
            }

            try
            {
                Directory.CreateDirectory(DetailingReport.LogDir);
                File.WriteAllText(Path.Combine(DetailingReport.LogDir, "autorun.txt"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {status}");
            }
            catch { /* sem log, sem drama */ }

            if (_exit)
                uiapp.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit));
        }

        /// <summary>
        /// Edições da conferência (etapa 3), sem mostrar a janela: grava a marca VT1 no 1º grupo de vigas e PT1 no 1º de pilares,
        /// "não exportar" no 2º grupo de vigas, levanta o modelo DE NOVO (as marcas e a decisão têm de vir do modelo) e exporta
        /// (SD_EXPORT = caminho do .json). Tudo dentro de um grupo de transações DESFEITO no fim: o modelo não muda.
        /// </summary>
        private static string ReviewEditAutoTest(Document doc)
        {
            string dir = Path.Combine(DetailingReport.LogDir, "conferireditar_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            using (var tg = new TransactionGroup(doc, "Conferir peças (teste)"))
            {
                tg.Start();
                try
                {
                    var notes = new List<string>();
                    var grupos = DEB.Core.Pecas.ConferenciaPecas.Agrupar(PieceReview.Run(doc, notes));
                    var vigas = grupos.Where(g => g.Representante.Tipo == DEB.Core.Pecas.TipoPeca.Viga).ToList();
                    var pilares = grupos.Where(g => g.Representante.Tipo == DEB.Core.Pecas.TipoPeca.Pilar).ToList();
                    if (vigas.Count > 0) sb.AppendLine("marca VT1: " + (PieceReviewEdits.SetMark(doc, vigas[0], "VT1") ?? "ok"));
                    if (vigas.Count > 1) sb.AppendLine("não exportar: " + (PieceReviewEdits.SetSkipped(doc, vigas[1], true) ?? "ok"));
                    if (pilares.Count > 0) sb.AppendLine("marca PT1: " + (PieceReviewEdits.SetMark(doc, pilares[0], "PT1") ?? "ok"));

                    // de novo, do modelo
                    var notes2 = new List<string>();
                    var pecas2 = PieceReview.Run(doc, notes2);
                    var arq = PrecastExport.Build(doc, pecas2, notes2);
                    string path = Environment.GetEnvironmentVariable("SD_EXPORT");
                    if (string.IsNullOrWhiteSpace(path)) path = Path.Combine(dir, "pecas.json");
                    arq.Gravar(path);
                    sb.AppendLine("pilares: " + string.Join(", ", arq.Pilares.Select(p => $"{p.Marca} x{p.Quantidade}")));
                    sb.AppendLine("vigas: " + string.Join(", ", arq.Vigas.Select(v => $"{v.Marca} x{v.Quantidade}")));
                    foreach (string n in arq.Notas) sb.AppendLine("nota: " + n);
                    sb.AppendLine("json: " + path);
                    try
                    {
                        var grupos2 = DEB.Core.Pecas.ConferenciaPecas.Agrupar(pecas2);
                        var w = new Precast.UI.PieceReviewWindow(grupos2, notes2, new Precast.UI.ReviewActions
                        {
                            Export = () => { }, SetMark = (g, m) => null, SetSkip = (g, v) => null,
                        });
                        w.RenderToPng(Path.Combine(dir, "vigas_1.png"), 0, 0);
                        w.RenderToPng(Path.Combine(dir, "vigas_2.png"), 0, 1);
                        w.RenderToPng(Path.Combine(dir, "pilares_1.png"), 1, 0);
                    }
                    catch (Exception ex) { sb.AppendLine("ERRO ao renderizar a janela: " + ex); }
                }
                finally { tg.RollBack(); }
            }
            File.WriteAllText(Path.Combine(dir, "resultado.txt"), sb.ToString());
            return dir;
        }

        /// <summary>
        /// Exportação das peças para o CAD (ARMIMPORTARPECAS), sem janela. SD_EXPORT = caminho do .json (senão vai para logs).
        /// </summary>
        private static string ExportPiecesAutoTest(Document doc)
        {
            var arq = PrecastExport.Build(doc);
            string path = Environment.GetEnvironmentVariable("SD_EXPORT");
            if (string.IsNullOrWhiteSpace(path))
                path = Path.Combine(DetailingReport.LogDir, DEB.Core.Intercambio.ArquivoPecas.NomeArquivo(arq.Origem, DateTime.Now));
            arq.Gravar(path);
            return $"{path} (pilares: {arq.Pilares.Count} grupo(s), {arq.Pilares.Sum(p => p.Quantidade)} peça(s); vigas: {arq.Vigas.Count} grupo(s), {arq.Vigas.Sum(v => v.Quantidade)} peça(s))";
        }

        /// <summary>
        /// Conferência das peças (etapa 2), sem mostrar a janela: grava o resumo dos grupos (situação, trechos, avisos) e a janela
        /// em PNG para cada grupo (até 8 por aba) em logs\conferir_AAAAMMDD_HHMMSS.
        /// </summary>
        private static string ReviewAutoTest(Document doc)
        {
            string dir = Path.Combine(DetailingReport.LogDir, "conferir_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);
            var notes = new List<string>();
            var grupos = DEB.Core.Pecas.ConferenciaPecas.Agrupar(PieceReview.Run(doc, notes));
            var sb = new StringBuilder();
            foreach (string n in notes) sb.AppendLine("NOTA: " + n);
            foreach (var g in grupos)
            {
                var r = g.Representante;
                sb.AppendLine($"{r.Tipo} {g.Marcas} x{g.Quantidade} [{g.Situacao}] {r.FamiliaTipo} L={r.Comprimento:0.#}");
                foreach (var t in r.Trechos)
                    sb.AppendLine($"   {t.X0:0.#}-{t.X1:0.#} {t.FuncaoSugerida}: {t.Secao.Classificar().Descricao} A={t.Secao.Area:0.#} Iy={t.Secao.Iy:0}");
                foreach (string a in g.Avisos) sb.AppendLine("   aviso: " + a);
            }
            try
            {
                var w = new Precast.UI.PieceReviewWindow(grupos, notes);
                for (int tab = 0; tab < 2; tab++)
                    for (int i = 0; i < Math.Min(8, w.Count(tab)); i++)
                        w.RenderToPng(Path.Combine(dir, $"{(tab == 0 ? "vigas" : "pilares")}_{i + 1}.png"), tab, i);
                // SD_CONFERIR_INDICES = "16,18,31": grupos de vigas escolhidos (posição na lista, a partir de 1), além dos 8 primeiros
                foreach (string s in (Environment.GetEnvironmentVariable("SD_CONFERIR_INDICES") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(s.Trim(), out int ix) && ix >= 1 && ix <= w.Count(0))
                        w.RenderToPng(Path.Combine(dir, $"vigas_{ix}.png"), 0, ix - 1);
                // vigas com consolo de outra viga (viga apoiada em viga), até 6
                var vigas = grupos.Where(g => g.Representante.Tipo == DEB.Core.Pecas.TipoPeca.Viga).ToList();
                int n = 0;
                for (int i = 0; i < vigas.Count && n < 6; i++)
                    if (vigas[i].Representante.Consolos.Count > 0)
                        w.RenderToPng(Path.Combine(dir, $"viga_com_consolo_{++n}.png"), 0, i);
            }
            catch (Exception ex) { sb.AppendLine("ERRO ao renderizar a janela: " + ex); }
            File.WriteAllText(Path.Combine(dir, "resultado.txt"), sb.ToString());
            return $"{dir} ({grupos.Count} grupo(s), {grupos.Sum(g => g.Quantidade)} peça(s))";
        }

        /// <summary>
        /// Fluxo completo de pilares pré-moldados com a armadura padrão, sem janela.
        /// SD_SAVEAS = caminho de uma CÓPIA do modelo para gravar o resultado (o original nunca é salvo).
        /// Exporta as folhas e vistas 3D geradas como PNG para conferência visual.
        /// </summary>
        private static string PrecastAutoTest(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string dir = Path.Combine(DetailingReport.LogDir, "precast_" + stamp);
            Directory.CreateDirectory(dir);

            List<ColumnGroup> groups = PrecastSurvey.Run(doc, out List<string> notes);
            var sb = new StringBuilder();
            foreach (string n in notes) sb.AppendLine("NOTA: " + n);
            foreach (ColumnGroup g in groups)
            {
                sb.AppendLine($"{g.Mark}: {g.Members.Count} un. | {g.Signature}");
                foreach (PrecastColumn m in g.Members)
                    sb.AppendLine($"   pilar {m.Id} giro {m.Rotation * 90}° consolos {m.Corbels.Count}");
                foreach (string w in g.Spec.Check(g.Representative)) sb.AppendLine("   verificação: " + w);
                foreach (BarDef b in ColumnRebarLayout.Compute(g.Representative.Frame, g.Representative.Corbels, g.Spec))
                    sb.AppendLine($"   barra {b.Role} Ø{b.DiameterMm} x{b.Count} pts={b.Points.Count}");
            }

            try
            {
                new Precast.UI.PrecastColumnsWindow(groups, notes).RenderToPng(Path.Combine(dir, "janela.png"));
            }
            catch (Exception ex)
            {
                sb.AppendLine("ERRO ao renderizar a janela: " + ex);
            }

            DetailingConfig cfg = DetailingConfig.Load(out _);
            cfg.OpenSheetWhenDone = false;
            var report = new DetailingReport();
            var pipeline = new PrecastColumnPipeline(doc, cfg, report, uidoc.ActiveView?.Id ?? ElementId.InvalidElementId);
            using (var tg = new TransactionGroup(doc, "Pilares pré-moldados (teste)"))
            {
                tg.Start();
                pipeline.Run(groups, new PipelineOptions());
                tg.Assimilate();
            }
            sb.AppendLine().AppendLine(report.Summary()).AppendLine().AppendLine(report.Details());
            foreach (PieceLog p in report.Pieces)
                foreach (string t in p.Traces) sb.AppendLine().AppendLine(t);

            // Imagens das folhas e das vistas 3D de assembly.
            List<ElementId> ids = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && v.AssociatedAssemblyInstanceId != ElementId.InvalidElementId &&
                            (v is ViewSheet || v is View3D))
                .Select(v => v.Id).ToList();
            if (ids.Count > 0)
            {
                var ieo = new ImageExportOptions
                {
                    ExportRange = ExportRange.SetOfViews,
                    FilePath = Path.Combine(dir, "vista"),
                    FitDirection = FitDirectionType.Horizontal,
                    HLRandWFViewsFileType = ImageFileType.PNG,
                    ShadowViewsFileType = ImageFileType.PNG,
                    ImageResolution = ImageResolution.DPI_150,
                    PixelSize = 2400,
                    ZoomType = ZoomFitType.FitToPage
                };
                ieo.SetViewsAndSheets(ids);
                doc.ExportImage(ieo);
            }

            string saveAs = Environment.GetEnvironmentVariable("SD_SAVEAS");
            if (!string.IsNullOrWhiteSpace(saveAs))
            {
                doc.SaveAs(saveAs, new SaveAsOptions { OverwriteExistingFile = true });
                sb.AppendLine("Modelo salvo em " + saveAs);
            }

            string log = Path.Combine(dir, "resultado.txt");
            File.WriteAllText(log, sb.ToString(), Encoding.UTF8);
            return log;
        }
    }
}
