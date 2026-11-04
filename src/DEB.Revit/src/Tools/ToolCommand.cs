using System;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DEB.Revit.Core;

namespace DEB.Revit.Tools
{
    /// <summary>Cancelamento pelo usuário (janela fechada, nada selecionado): sai sem mensagem de erro.</summary>
    internal sealed class ToolCancelled : Exception
    {
        /// <summary>Aviso a mostrar ao usuário (ex.: "Selecione pilares..."); null = sair em silêncio.</summary>
        public string Notice { get; }
        public ToolCancelled(string notice = null) : base(notice ?? "Cancelado.") { Notice = notice; }
    }

    /// <summary>
    /// Base das ferramentas (ex-scripts Revit_Suelio). Barreira de exceções: um erro vira mensagem em português,
    /// a transação aberta é desfeita (using) e o detalhe vai para %AppData%\PCEB\DEB.Revit\logs\ferramentas_AAAAMMDD.log.
    /// O Revit nunca recebe a exceção (requisito de robustez do PCEB).
    /// </summary>
    public abstract class ToolCommand : IExternalCommand
    {
        protected abstract string Title { get; }

        protected abstract Result Run(UIApplication uiapp, UIDocument uidoc, Document doc);

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            try
            {
                UIDocument uidoc = uiapp.ActiveUIDocument;
                if (uidoc == null) throw new ToolCancelled("Abra um projeto.");
                ToolDialogs.Owner = uiapp.MainWindowHandle;
                return Run(uiapp, uidoc, uidoc.Document);
            }
            catch (ToolCancelled c)
            {
                if (!string.IsNullOrEmpty(c.Notice)) TaskDialog.Show(Title, c.Notice);
                return Result.Cancelled;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                string log = WriteLog(ex);
                TaskDialog.Show(Title, $"Não foi possível concluir: {ex.Message}\n\nNada foi alterado no modelo.\nDetalhes: {log}");
                return Result.Failed;
            }
        }

        private string WriteLog(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(DetailingReport.LogDir);
                string path = Path.Combine(DetailingReport.LogDir, $"ferramentas_{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss} [{Title}] {ex}{Environment.NewLine}", Encoding.UTF8);
                return path;
            }
            catch { return "(log indisponível)"; }
        }
    }
}
