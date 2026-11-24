using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DEB.Core.Analise.Objetos;
using DEB.Revit.Analise;
using DEB.Revit.Precast;

namespace DEB.Revit.Commands
{
    /// <summary>
    /// "Modelo de análise" (D46): monta a estrutura por objetos (pilares pré-moldados, vigas apoiadas nos consolos, ligações),
    /// analisa e mostra na janela própria do PCEB (3D, deformada, diagramas, mapa de cores). Não escreve no modelo do Revit.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class ModeloAnaliticoCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            Document doc = uiapp.ActiveUIDocument.Document;
            var notas = new List<string>();
            EstruturaObjetos e = Montar(doc, notas);
            if (e == null)
            {
                TaskDialog.Show("Modelo de análise", "Não há estrutura para montar (pilares com consolos de vigas pré-moldadas).\n\n" + string.Join("\n", notas.Take(10)));
                return Result.Cancelled;
            }
            var win = new JanelaAnalise(VisualizacaoAnalise.Json(e, doc.Title), doc.Title);
            new WindowInteropHelper(win) { Owner = uiapp.MainWindowHandle };
            win.ShowDialog();
            if (win.Erro != null) TaskDialog.Show("Modelo de análise", win.Erro);
            return Result.Succeeded;
        }

        /// <summary>Levanta, monta e analisa (as notas do levantamento vão aos avisos da estrutura).</summary>
        internal static EstruturaObjetos Montar(Document doc, List<string> notas)
        {
            List<ColumnGroup> grupos = PrecastSurvey.Run(doc, out List<string> n0);
            notas.AddRange(n0);
            var cfg = DEB.Revit.Configuracao.ConfiguracaoRevit.Carregar(doc, notas);
            ReacoesNosConsolos.Aplicar(doc, grupos, notas, cfg, out EstruturaObjetos e);
            if (e != null) foreach (string n in notas.Where(n => !n.StartsWith("Análise:"))) e.Avisos.Add(n);
            return e;
        }
    }
}
