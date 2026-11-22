using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DEB.Revit.Core;
using DEB.Revit.Vigas;

namespace DEB.Revit.Commands
{
    /// <summary>
    /// "Vigas pré-moldadas" (D40): levanta as vigas, agrupa as iguais, lê as cargas digitadas, calcula (concreto armado, biapoiada),
    /// mostra a armadura proposta e, no aceite, arma as peças no Revit com montagem, vistas e tabelas de aço; grava o relatório de
    /// cálculo de cada grupo em "Relatórios PCEB", ao lado do modelo.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class PrecastBeamsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            try
            {
                var notas = new List<string>();
                List<GrupoViga> grupos = VigasPreMoldadas.Levantar(doc, notas);
                if (grupos.Count == 0)
                {
                    new TaskDialog("Vigas pré-moldadas")
                    {
                        MainInstruction = "Nenhuma viga pré-moldada que o programa arme foi encontrada.",
                        MainContent = "A janela \"Conferir peças\" mostra cada viga e o motivo.", ExpandedContent = string.Join("\n", notas),
                    }.Show();
                    return Result.Cancelled;
                }
                foreach (GrupoViga g in grupos) VigasPreMoldadas.Calcular(g);

                var win = new VigasWindow(doc, grupos, notas);
                new WindowInteropHelper(win) { Owner = uiapp.MainWindowHandle };
                if (win.ShowDialog() != true) return Result.Cancelled;

                var report = new DetailingReport();
                using (var tg = new TransactionGroup(doc, "Vigas pré-moldadas"))
                {
                    tg.Start();
                    VigasPreMoldadas.Aplicar(doc, win.Selecionados, report, uidoc.ActiveView?.Id ?? ElementId.InvalidElementId, win.CriarVistas);
                    if (report.Succeeded > 0) tg.Assimilate();
                    else tg.RollBack();
                }
                report.WriteToFile(doc.Title, uiapp.Application.VersionNumber);
                DetailPieceCommand.ShowReport(report, win.Selecionados.Sum(g => g.Quantidade));
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                try
                {
                    Directory.CreateDirectory(DetailingReport.LogDir);
                    File.WriteAllText(Path.Combine(DetailingReport.LogDir, $"vigas_{DateTime.Now:yyyyMMdd_HHmmss}.log"), ex.ToString());
                }
                catch { }
                TaskDialog.Show("Vigas pré-moldadas", "Não foi possível concluir: " + ex.Message);
                return Result.Failed;
            }
        }
    }
}
