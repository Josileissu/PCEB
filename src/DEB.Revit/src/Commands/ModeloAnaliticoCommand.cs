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
                TaskDialog.Show("Modelo de análise", "Não foi possível montar a estrutura (pilares, vigas e pisos).\n\n" + string.Join("\n", notas.Take(10)));
                return Result.Cancelled;
            }
            var win = new JanelaAnalise(VisualizacaoAnalise.Json(e, doc.Title, DEB.Revit.Configuracao.ConfiguracaoRevit.Carregar(doc)), doc.Title);
            new WindowInteropHelper(win) { Owner = uiapp.MainWindowHandle };
            win.ShowDialog();
            if (win.Erro != null) TaskDialog.Show("Modelo de análise", win.Erro);
            return Result.Succeeded;
        }

        /// <summary>Levanta pilares, vigas e pisos, monta a estrutura por objetos e analisa (as notas vão aos avisos da estrutura).</summary>
        internal static EstruturaObjetos Montar(Document doc, List<string> notas)
        {
            List<ColumnGroup> grupos = PrecastSurvey.Run(doc, out List<string> n0);
            notas.AddRange(n0);
            var cfg = DEB.Revit.Configuracao.ConfiguracaoRevit.Carregar(doc, notas);
            var porId = new Dictionary<long, (DEB.Revit.Vigas.GrupoViga G, DEB.Revit.Vigas.MembroViga M)>();
            try
            {
                foreach (var g in DEB.Revit.Vigas.VigasPreMoldadas.Levantar(doc, new List<string>()))
                    foreach (var m in g.Membros) porId[m.Id] = (g, m);
            }
            catch (System.Exception ex) { notas.Add("Vigas pré-moldadas não levantadas: " + ex.Message); }
            EstruturaObjetos e = EstruturaRevit.Montar(doc, grupos, porId, cfg, notas, out _);
            if (e.Pilares.Count + e.Vigas.Count + e.Lajes.Count == 0) return null;
            try { e.Analisar(cfg); }
            catch (DEB.Core.Analise.Barras.ErroAnalise ex) { notas.Add("Análise: " + ex.Message); return null; }
            foreach (string n in notas) e.Avisos.Add(n);
            return e;
        }
    }
}
