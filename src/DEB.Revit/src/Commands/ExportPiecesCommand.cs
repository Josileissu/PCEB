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
        /// <summary>
        /// Pede o arquivo, grava e mostra o resumo (usado também pelo botão da janela "Conferir peças"). As observações — peças
        /// que ficaram de fora e por quê — vão no detalhe da mensagem e ficam todas no próprio JSON ("notas").
        /// </summary>
        internal static bool Save(Document doc, ArquivoPecas arq)
        {
            const int maxNotas = 40;
            string notas = arq.Notas.Count == 0 ? null
                : string.Join("\n", arq.Notas.Take(maxNotas).Select(n => "• " + n)) +
                  (arq.Notas.Count > maxNotas ? $"\n… e mais {arq.Notas.Count - maxNotas} (todas no arquivo, em \"notas\")." : "");
            if (arq.Pilares.Count == 0 && arq.Vigas.Count == 0)
            {
                new TaskDialog("Exportar para o CAD")
                {
                    MainInstruction = "Nenhuma peça que o CAD desenhe foi encontrada no modelo.",
                    MainContent = notas == null ? "" : "Veja os motivos em \"detalhes\". A janela \"Conferir peças\" mostra cada peça.",
                    ExpandedContent = notas ?? "",
                }.Show();
                return false;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exportar peças para o CAD",
                Filter = "Peças do DEB (*.json)|*.json",
                FileName = ArquivoPecas.NomeArquivo(arq.Origem, DateTime.Now),
                InitialDirectory = string.IsNullOrEmpty(doc.PathName) ? null : Path.GetDirectoryName(doc.PathName),
            };
            if (dlg.ShowDialog() != true) return false;
            arq.Gravar(dlg.FileName);

            new TaskDialog("Exportar para o CAD")
            {
                MainInstruction = $"Pilares: {arq.Pilares.Count} grupo(s) de peças iguais ({arq.Pilares.Sum(p => p.Quantidade)} peça(s)).\n" +
                                  $"Vigas: {arq.Vigas.Count} grupo(s) ({arq.Vigas.Sum(v => v.Quantidade)} peça(s)).",
                MainContent = $"Gravado em:\n{dlg.FileName}\n\nNo CAD, use o comando ARMIMPORTARPECAS." +
                              (notas == null ? "" : $"\n\n{arq.Notas.Count} observação(ões) — peças que ficaram de fora e o que conferir — em \"detalhes\"."),
                ExpandedContent = notas ?? "",
            }.Show();
            return true;
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            try
            {
                return Save(doc, PrecastExport.Build(doc)) ? Result.Succeeded : Result.Cancelled;
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
