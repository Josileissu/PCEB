using System.Diagnostics;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StructuralDetailer.Core;

namespace StructuralDetailer.Commands
{
    /// <summary>Abre o JSON de configuração no editor padrão do Windows.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class OpenConfigCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            DetailingConfig.Load(out _);   // garante que o arquivo existe
            try
            {
                Process.Start(new ProcessStartInfo(DetailingConfig.ConfigPath) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Nenhum programa associado a .json: abre no Bloco de Notas.
                Process.Start("notepad.exe", "\"" + DetailingConfig.ConfigPath + "\"");
            }

            TaskDialog.Show("Detalhamento Estrutural",
                "Arquivo de configuração aberto:\n" + DetailingConfig.ConfigPath +
                "\n\nAs alterações valem na próxima vez que você usar \"Detalhar peça\".");
            return Result.Succeeded;
        }
    }
}
