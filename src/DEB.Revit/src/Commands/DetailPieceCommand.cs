using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using StructuralDetailer.Core;
using StructuralDetailer.UI;

namespace StructuralDetailer.Commands
{
    /// <summary>
    /// "Detalhar peça": usa a seleção atual (vigas/pilares) ou pede para selecionar.
    /// Aceita várias peças de uma vez.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class DetailPieceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // Seleção
            List<FamilyInstance> pieces = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .Where(PieceAnalyzer.IsSupported)
                .Cast<FamilyInstance>()
                .ToList();

            if (pieces.Count == 0)
            {
                try
                {
                    IList<Reference> refs = uidoc.Selection.PickObjects(ObjectType.Element, new StructuralPieceFilter(),
                        "Selecione as vigas / pilares a detalhar e clique em Concluir");
                    pieces = refs.Select(r => doc.GetElement(r)).OfType<FamilyInstance>().ToList();
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
            }
            if (pieces.Count == 0) return Result.Cancelled;

            DetailingConfig cfg = DetailingConfig.Load(out string cfgWarning);
            var report = new DetailingReport();
            if (cfgWarning != null) report.General.Add(cfgWarning);

            var detailer = new PieceDetailer(doc, cfg, report, uidoc.ActiveView?.Id ?? ElementId.InvalidElementId);

            using (var group = new TransactionGroup(doc, "Detalhar peças"))
            {
                group.Start();
                foreach (FamilyInstance fi in pieces)
                {
                    if (detailer.Run(fi) == DetailOutcome.CancelAll) break;
                }

                if (report.Succeeded > 0) group.Assimilate();
                else group.RollBack();
            }

            report.WriteToFile(doc.Title, commandData.Application.Application.VersionNumber);
            ShowReport(report, pieces.Count);

            if (cfg.OpenSheetWhenDone && detailer.LastSheet != null && detailer.LastSheet.IsValidObject)
                uidoc.RequestViewChange(detailer.LastSheet);

            return Result.Succeeded;
        }

        internal static void ShowReport(DetailingReport report, int total)
        {
            var td = new TaskDialog("Detalhamento Estrutural")
            {
                MainInstruction = $"{report.Succeeded} de {total} peça(s) detalhada(s).",
                MainContent = report.Summary()
            };
            string details = report.Details();
            if (!string.IsNullOrEmpty(details))
            {
                td.ExpandedContent = details;
                td.FooterText = $"{report.WarningCount} aviso(s). Clique em \"Mostrar detalhes\" para ver.";
            }
            td.Show();
        }
    }
}
