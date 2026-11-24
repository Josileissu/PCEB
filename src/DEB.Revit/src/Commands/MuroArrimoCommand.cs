using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DEB.Core.Elementos.Contencao;
using DEB.Revit.Configuracao;
using DEB.Revit.Contencao;

namespace DEB.Revit.Commands
{
    /// <summary>
    /// "Muro de arrimo" (D47): com uma parede selecionada (ou escolhida), lê a parede e a sapata corrida, abre a janela do PCEB
    /// (seção, empuxo, estabilidade, armaduras) e, em "Aplicar ao elemento", grava os dados do muro na parede.
    /// Sem parede: abre a janela com o muro de partida, só para cálculo.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class MuroArrimoCommand : IExternalCommand
    {
        private sealed class FiltroParede : ISelectionFilter
        {
            public bool AllowElement(Element e) => e is Wall;
            public bool AllowReference(Reference r, XYZ p) => false;
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null) return Result.Cancelled;
            Wall w = uidoc.Selection.GetElementIds().Select(doc.GetElement).OfType<Wall>().FirstOrDefault();
            if (w == null)
            {
                try { w = doc.GetElement(uidoc.Selection.PickObject(ObjectType.Element, new FiltroParede(), "Selecione a parede do muro de arrimo (Esc = só calcular)")) as Wall; }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { w = null; }
            }
            var notas = new List<string>();
            bool aplicou = Abrir(doc, w, commandData.Application.MainWindowHandle, notas, null, "");
            if (notas.Count > 0) TaskDialog.Show("Muro de arrimo", string.Join("\n\n", notas));
            return aplicou ? Result.Succeeded : Result.Cancelled;
        }

        /// <summary>Abre a janela; devolve verdadeiro se os dados foram aplicados à parede. captura ≠ null (AutoRun): só a imagem.</summary>
        internal static bool Abrir(Document doc, Wall w, IntPtr dono, List<string> notas, string captura, string aba)
        {
            var cfg = ConfiguracaoRevit.Carregar(doc, notas);
            DadosMuro dados = w != null ? MuroRevit.Ler(w, cfg, notas) : DadosMuro.DaConfiguracao(cfg);
            string titulo = w != null ? $"{w.Name} (id {w.Id})" : "sem elemento";
            var sessao = new SessaoMuro(dados, cfg, titulo);
            var win = new JanelaMuro(sessao) { Captura = captura, ModoCaptura = aba ?? "" };
            if (dono != IntPtr.Zero) new WindowInteropHelper(win) { Owner = dono };
            win.ShowDialog();
            if (win.Erro != null) { notas.Add(win.Erro); return false; }
            if (!sessao.Aplicar || captura != null) return false;
            if (w == null) { notas.Add("Sem parede selecionada: os dados não foram gravados (selecione a parede e abra de novo para aplicar)."); return false; }
            MuroRevit.Gravar(w, sessao.Dados);
            notas.Add($"Dados do muro gravados na parede {w.Id}. Situação: {sessao.Resultado.Situacao}.");
            foreach (var v in sessao.Resultado.Verificacoes.Where(v => v.Situacao != DEB.Core.Normas.Verificacao.SituacaoVerificacao.Ok))
                notas.Add(v.Mensagem);
            return true;
        }
    }
}
