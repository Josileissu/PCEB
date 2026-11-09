using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DEB.Core.Intercambio;
using DEB.Revit.Core;
using DEB.Revit.Precast;

namespace DEB.Revit.Commands
{
    /// <summary>
    /// "Exportar para o CAD": grava os grupos de pilares pré-moldados iguais (seção, comprimento, níveis, consolos) num
    /// arquivo .json do DEB, e também as vigas pré-moldadas (seção, comprimento, dentes Gerber); no CAD, o ARMIMPORTARPECAS gera um
    /// detalhamento por grupo pelos templates do pilar e da viga.
    /// Só lê o modelo.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class ExportPiecesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            try
            {
                ArquivoPecas arq = PrecastExport.Build(doc);
                if (arq.Pilares.Count == 0 && arq.Vigas.Count == 0)
                {
                    TaskDialog.Show("Exportar para o CAD", "Nenhum pilar nem viga pré-moldada encontrado no modelo.");
                    return Result.Cancelled;
                }

                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Exportar peças para o CAD",
                    Filter = "Peças do DEB (*.json)|*.json",
                    FileName = ArquivoPecas.NomeArquivo(arq.Origem, DateTime.Now),
                    InitialDirectory = string.IsNullOrEmpty(doc.PathName) ? null : Path.GetDirectoryName(doc.PathName),
                };
                if (dlg.ShowDialog() != true) return Result.Cancelled;
                arq.Gravar(dlg.FileName);

                TaskDialog.Show("Exportar para o CAD",
                    $"Pilares: {arq.Pilares.Count} grupo(s) de peças iguais ({arq.Pilares.Sum(p => p.Quantidade)} peça(s)).\n" +
                    $"Vigas: {arq.Vigas.Count} grupo(s) ({arq.Vigas.Sum(v => v.Quantidade)} peça(s)).\n\nGravado em:\n{dlg.FileName}\n\n" +
                    "No CAD, use o comando ARMIMPORTARPECAS." +
                    (arq.Notas.Count > 0 ? "\n\nObservações:\n" + string.Join("\n", arq.Notas) : ""));
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                try
                {
                    Directory.CreateDirectory(DetailingReport.LogDir);
                    File.WriteAllText(Path.Combine(DetailingReport.LogDir, $"exportar_{DateTime.Now:yyyyMMdd_HHmmss}.log"), ex.ToString());
                }
                catch { /* sem log, a mensagem ainda aparece */ }
                TaskDialog.Show("Exportar para o CAD", "Não foi possível exportar: " + ex.Message);
                return Result.Failed;
            }
        }
    }
}
