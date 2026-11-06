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
            // instância de teste: janelas do Revit (avisos de atualização ao abrir arquivos antigos...) não podem parar o teste
            app.DialogBoxShowing += (s, e) =>
            {
                Dialogs.Add(e.DialogId);
                if (e is TaskDialogShowingEventArgs) e.OverrideResult((int)TaskDialogResult.Close);
                else e.OverrideResult(1);   // IDOK
            };
        }

        /// <summary>Janelas do Revit fechadas sozinhas durante o AutoRun (vão para o resultado).</summary>
        internal static readonly List<string> Dialogs = new List<string>();

        private static void OnIdling(object sender, IdlingEventArgs e)
        {
            var uiapp = (UIApplication)sender;
            string task = _task.Trim().ToLowerInvariant();
            bool semProjeto = task == "familias" || task == "criarmodelo" || task == "contarlegendas";   // tarefas que não precisam de projeto aberto
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (!semProjeto && (uidoc == null || uidoc.Document.IsFamilyDocument)) return;   // ainda abrindo o projeto

            uiapp.Idling -= OnIdling;
            string status;
            try
            {
                switch (task)
                {
                    case "familias":
                        status = "OK " + TestModel.InspectFamilies(uiapp.Application);
                        break;
                    case "criarmodelo":
                        status = "OK " + TestModel.BuildPrecastModel(uiapp.Application);
                        break;
                    case "diag":
                        status = "OK " + ModelDiagnostics.Run(uidoc.Document);
                        break;
                    case "probe":
                        status = "OK " + PrecastProbe.Run(uidoc.Document);
                        break;
                    case "precast":
                        status = "OK " + PrecastAutoTest(uidoc);
                        break;
                    case "contarlegendas":
                        status = "OK " + CountLegends(uiapp.Application);
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

        /// <summary>SD_AUTORUN = contarlegendas: legendas de cada arquivo de SD_FAMILIAS (.rte/.rvt separados por ";"),
        /// para escolher o ArquivoLegendaBase do padrão.</summary>
        private static string CountLegends(Autodesk.Revit.ApplicationServices.Application app)
        {
            var sb = new StringBuilder();
            foreach (string f in (Environment.GetEnvironmentVariable("SD_FAMILIAS") ?? "").Split(';').Where(x => x.Trim() != ""))
            {
                try
                {
                    Document d = Presentation.LegendFactory.OpenQuietly(app, f.Trim());
                    List<string> names = new FilteredElementCollector(d).OfClass(typeof(View)).Cast<View>()
                        .Where(v => v.ViewType == ViewType.Legend && !v.IsTemplate).Select(v => v.Name).ToList();
                    sb.AppendLine($"{f}: {names.Count} legenda(s) {string.Join(" | ", names)}");
                    d.Close(false);
                }
                catch (Exception ex) { sb.AppendLine($"{f}: ERRO {ex.Message}"); }
            }
            if (Dialogs.Count > 0) sb.AppendLine("janelas fechadas: " + string.Join(", ", Dialogs));
            string log = Path.Combine(DetailingReport.LogDir, "legendas.txt");
            File.WriteAllText(log, sb.ToString(), Encoding.UTF8);
            return log;
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
