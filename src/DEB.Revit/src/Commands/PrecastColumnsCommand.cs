using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StructuralDetailer.Core;
using StructuralDetailer.Precast;
using StructuralDetailer.Precast.UI;

namespace StructuralDetailer.Commands
{
    /// <summary>
    /// "Pilares pré-moldados": levanta todos os pilares da obra com seus consolos, agrupa as peças
    /// iguais (inclusive giradas), abre a pré-visualização para escolher as armaduras e gera
    /// armaduras, assemblies e folhas.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class PrecastColumnsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            List<ColumnGroup> groups = PrecastSurvey.Run(doc, out List<string> notes);
            if (groups.Count == 0)
            {
                TaskDialog.Show("Pilares pré-moldados", "Nenhum pilar estrutural encontrado no modelo.");
                return Result.Cancelled;
            }

            var win = new PrecastColumnsWindow(groups, notes);
            new WindowInteropHelper(win) { Owner = uiapp.MainWindowHandle };
            if (win.ShowDialog() != true) return Result.Cancelled;

            List<ColumnGroup> selected = win.Selected;
            DetailingConfig cfg = DetailingConfig.Load(out string cfgWarning);
            var report = new DetailingReport();
            if (cfgWarning != null) report.General.Add(cfgWarning);

            var pipeline = new PrecastColumnPipeline(doc, cfg, report, uidoc.ActiveView?.Id ?? ElementId.InvalidElementId);
            using (var tg = new TransactionGroup(doc, "Pilares pré-moldados"))
            {
                tg.Start();
                pipeline.Run(selected, win.Options);
                if (report.Succeeded > 0) tg.Assimilate();
                else tg.RollBack();
            }

            report.WriteToFile(doc.Title, uiapp.Application.VersionNumber);
            DetailPieceCommand.ShowReport(report, selected.Sum(g => g.Members.Count));

            if (cfg.OpenSheetWhenDone && pipeline.LastSheet != null && pipeline.LastSheet.IsValidObject)
                uidoc.RequestViewChange(pipeline.LastSheet);
            return Result.Succeeded;
        }
    }
}
