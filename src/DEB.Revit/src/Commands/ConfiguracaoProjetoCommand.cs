using System.Collections.Generic;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DEB.Core.Configuracao.Projeto;
using DEB.Revit.Configuracao;

namespace DEB.Revit.Commands
{
    /// <summary>
    /// "Configuração do projeto" (D43): abre a tela (níveis escritório e projeto) e, se o usuário gravar, salva o escritório no
    /// arquivo comum ao CAD e o projeto dentro do modelo.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ConfiguracaoProjetoCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            Document doc = uiapp.ActiveUIDocument?.Document;
            var avisos = new List<string>();
            if (!Abrir(doc, uiapp.MainWindowHandle, avisos, null, out bool gravou)) { Mostrar(avisos); return Result.Cancelled; }
            Mostrar(avisos);
            return gravou ? Result.Succeeded : Result.Cancelled;
        }

        /// <summary>Abre a tela e grava o que mudou. captura ≠ null (AutoRun): só grava a imagem da página. Falso = a tela não abriu.</summary>
        internal static bool Abrir(Document doc, System.IntPtr dono, List<string> avisos, string captura, out bool gravou, System.Action<SessaoConfiguracao> preparar = null)
        {
            gravou = false;
            bool comProjeto = doc != null && !doc.IsFamilyDocument;
            ConfiguracaoProjeto cfg = ConfiguracaoRevit.Carregar(comProjeto ? doc : null, avisos);
            string escAntes = cfg.Escritorio.ParaJson(), projAntes = cfg.Projeto.ParaJson();
            var sessao = new SessaoConfiguracao(cfg)
            {
                ComProjeto = comProjeto,
                Contexto = (comProjeto ? "Projeto: " + doc.Title : "Sem projeto aberto: só o padrão do escritório") +
                           $" · {cfg.Texto("normas.nbr6118")} · {cfg.Texto("normas.nbr9062")}",
            };
            preparar?.Invoke(sessao);
            var win = new JanelaConfiguracao(sessao) { Captura = captura };
            if (dono != System.IntPtr.Zero) new WindowInteropHelper(win) { Owner = dono };
            win.ShowDialog();
            if (win.Erro != null) { avisos.Add(win.Erro); return false; }
            if (!win.Gravar) return true;
            if (cfg.Escritorio.ParaJson() != escAntes)
            {
                ArquivoEscritorio.Gravar(cfg.Escritorio);
                avisos.Add("Padrão do escritório gravado em " + ArquivoEscritorio.Caminho + ".");
            }
            if (comProjeto && cfg.Projeto.ParaJson() != projAntes) ConfiguracaoRevit.GravarProjeto(doc, cfg.Projeto, avisos);
            foreach (var f in cfg.ForaDaNorma())
                if (!f.Justificado) avisos.Add("Fora da norma sem justificativa: " + f.Texto + ".");
            gravou = true;
            return true;
        }

        private static void Mostrar(List<string> avisos)
        {
            if (avisos.Count > 0) TaskDialog.Show("Configuração do projeto", string.Join("\n\n", avisos));
        }
    }
}
