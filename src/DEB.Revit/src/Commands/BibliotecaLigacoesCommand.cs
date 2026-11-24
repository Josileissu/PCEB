using System;
using System.Collections.Generic;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DEB.Core.Elementos.Metalica.Ligacoes;
using DEB.Revit.Analise;

namespace DEB.Revit.Commands
{
    /// <summary>
    /// "Biblioteca de ligações" (D49): abre a janela com as ligações parametrizadas do PCEB e do escritório; "Gravar no escritório"
    /// salva o arquivo comum ao CAD e ao Revit (%AppData%\PCEB\padroes\ligacoes.json). As do PCEB vêm com o programa e não mudam.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class BibliotecaLigacoesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var avisos = new List<string>();
            bool gravou = Abrir(commandData.Application.MainWindowHandle, avisos, null);
            if (avisos.Count > 0) TaskDialog.Show("Biblioteca de ligações", string.Join("\n\n", avisos));
            return gravou ? Result.Succeeded : Result.Cancelled;
        }

        /// <summary>Abre a janela; devolve verdadeiro se gravou o escritório. captura ≠ null (AutoRun): só a imagem.</summary>
        internal static bool Abrir(IntPtr dono, List<string> avisos, string captura)
        {
            var b = BibliotecaLigacoes.Carregar(avisos);
            var s = new SessaoBiblioteca(b);
            var win = new JanelaBibliotecaLigacoes(s) { Captura = captura };
            if (dono != IntPtr.Zero) new WindowInteropHelper(win) { Owner = dono };
            win.ShowDialog();
            if (win.Erro != null) { avisos.Add(win.Erro); return false; }
            if (!win.Gravar || captura != null) return false;
            try { b.GravarEscritorio(); avisos.Add("Biblioteca do escritório gravada em " + BibliotecaLigacoes.CaminhoEscritorio + "."); return true; }
            catch (Exception ex) { avisos.Add("Não foi possível gravar a biblioteca: " + ex.Message); return false; }
        }
    }
}
