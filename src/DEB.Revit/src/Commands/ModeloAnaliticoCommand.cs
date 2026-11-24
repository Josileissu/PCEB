using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DEB.Core.Analise.Objetos;
using DEB.Revit.Analise;
using DEB.Revit.Precast;

namespace DEB.Revit.Commands
{
    /// <summary>
    /// "Modelo analítico" (D46): monta a estrutura por objetos (pilares pré-moldados, vigas apoiadas nos consolos, ligações),
    /// analisa, mostra o resumo e, com o aceite, gera as barras no modelo analítico do Revit (com o dono de cada uma).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ModeloAnaliticoCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            var notas = new List<string>();
            List<ColumnGroup> grupos = PrecastSurvey.Run(doc, out List<string> n0);
            notas.AddRange(n0);
            var cfg = DEB.Revit.Configuracao.ConfiguracaoRevit.Carregar(doc, notas);
            ReacoesNosConsolos.Aplicar(doc, grupos, notas, cfg, out EstruturaObjetos e);
            if (e == null)
            {
                TaskDialog.Show("Modelo analítico", "Não há estrutura para montar (pilares com consolos de vigas pré-moldadas).\n\n" + string.Join("\n", notas.Take(10)));
                return Result.Cancelled;
            }
            var m = e.Modelo;
            int antigos = ModeloAnaliticoRevit.Gerados(doc).Count;
            var td = new TaskDialog("Modelo analítico")
            {
                MainInstruction = $"{e.Pilares.Count} pilares, {e.Vigas.Count} vigas, {e.Ligacoes.Count} ligações por consolo",
                MainContent = $"Modelo de barras: {m.Nos.Count} nós, {m.Barras.Count} barras, {m.Molas.Count} aparelhos, {m.Vinculos.Count} vínculos rígidos. " +
                              "A análise já passou as cargas aos consolos.\n\nGerar as barras no modelo analítico do Revit?" +
                              (antigos > 0 ? $"\n({antigos} elementos analíticos gerados antes pelo PCEB serão substituídos.)" : ""),
                ExpandedContent = string.Join("\n", notas.Take(20)),
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
            };
            if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;
            var avisos = new List<string>();
            string resumo = ModeloAnaliticoRevit.Gerar(doc, e, avisos);
            TaskDialog.Show("Modelo analítico", "Gerado: " + resumo + (avisos.Count > 0 ? "\n\n" + string.Join("\n", avisos.Take(15)) : ""));
            return Result.Succeeded;
        }
    }
}
