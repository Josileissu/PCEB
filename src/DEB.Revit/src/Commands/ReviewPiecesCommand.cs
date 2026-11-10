using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DEB.Core.Pecas;
using DEB.Revit.Core;
using DEB.Revit.Precast;
using DEB.Revit.Precast.UI;

namespace DEB.Revit.Commands
{
    /// <summary>
    /// "Conferir peças" (2026-11-09, etapa 2): levanta vigas e pilares, subdivide em trechos de seção constante, agrupa as
    /// iguais pela geometria e mostra a janela de conferência. Só grava no modelo o que o usuário pedir na janela: a marca do
    /// grupo e "não exportar" (etapa 3).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ReviewPiecesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            // peças selecionadas no Revit ao abrir: a janela começa marcando só os grupos delas (o usuário troca à vontade)
            var selecao = new HashSet<string>(uidoc.Selection.GetElementIds().Select(id => id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            try
            {
                var notes = new List<string>();
                List<PecaLevantada> pecas = PieceReview.Run(doc, notes);
                List<GrupoPecas> grupos = ConferenciaPecas.Agrupar(pecas);
                if (grupos.Count == 0)
                {
                    TaskDialog.Show("Conferir peças", "Nenhuma viga nem pilar estrutural encontrado no modelo.");
                    return Result.Cancelled;
                }
                // "Exportar para o CAD…" da janela: o mesmo levantamento que ela mostra (não mede de novo)
                // e as duas únicas gravações no modelo: a marca do grupo e "não exportar"
                new PieceReviewWindow(grupos, notes, new ReviewActions
                {
                    Export = (escolhidas, desmarcados) =>
                    {
                        var n = new List<string>(notes);
                        if (desmarcados > 0) n.Add($"{desmarcados} grupo(s) desmarcado(s) na janela \"Conferir peças\" ficaram de fora desta exportação.");
                        ExportPiecesCommand.Save(doc, PrecastExport.Build(doc, escolhidas, n));
                    },
                    SelecaoRevit = selecao,
                    SetMark = (g, m) => PieceReviewEdits.SetMark(doc, g, m),
                    SetSkip = (g, v) => PieceReviewEdits.SetSkipped(doc, g, v),
                }).ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                try
                {
                    Directory.CreateDirectory(DetailingReport.LogDir);
                    File.WriteAllText(Path.Combine(DetailingReport.LogDir, $"conferir_{DateTime.Now:yyyyMMdd_HHmmss}.log"), ex.ToString());
                }
                catch { /* sem log, a mensagem ainda aparece */ }
                TaskDialog.Show("Conferir peças", "Não foi possível conferir as peças: " + ex.Message);
                return Result.Failed;
            }
        }
    }
}
