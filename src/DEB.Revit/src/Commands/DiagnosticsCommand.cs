using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DEB.Revit.Core;

namespace DEB.Revit.Commands
{
    /// <summary>Grava o levantamento do modelo (pilares, vigas, consolos, contatos) em JSON.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class DiagnosticsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            string path = ModelDiagnostics.Run(commandData.Application.ActiveUIDocument.Document);
            TaskDialog.Show("Detalhamento Estrutural", "Diagnóstico do modelo gravado em:\n" + path);
            return Result.Succeeded;
        }
    }
}
