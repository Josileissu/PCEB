using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DEB.Core.Elementos.Escada;
using DEB.Revit.Core;
using DEB.Revit.Escadas;

namespace DEB.Revit.Commands
{
    /// <summary>
    /// "Escada" (D42): escada em laje armada longitudinalmente. Com duas vigas (ou lajes) de apoio selecionadas, lê o vão, o
    /// desnível e a posição; sem seleção, pede o eixo do apoio inferior e o sentido de subida. Abre a janela de cálculo (prévia,
    /// esforços, MEF, verificações) e, no aceite, gera a família da escada, coloca no modelo e cria as barras.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class EscadaCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            try
            {
                var e = new EscadaLaje();
                var sel = uidoc.Selection.GetElementIds().Select(doc.GetElement)
                    .Where(x => x?.Category != null && (x.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFraming || x.Category.Id.Value == (long)BuiltInCategory.OST_Floors))
                    .ToList();
                PosicaoEscada pos = sel.Count == 2 ? EscadasRevit.DeApoios(doc, sel, e) : null;
                if (pos == null)
                {
                    try
                    {
                        XYZ a = uidoc.Selection.PickPoint(ObjectSnapTypes.Endpoints | ObjectSnapTypes.Midpoints | ObjectSnapTypes.Intersections,
                            "Escada: clique no eixo do apoio inferior (meio da largura)");
                        XYZ b = uidoc.Selection.PickPoint(ObjectSnapTypes.Endpoints | ObjectSnapTypes.Midpoints | ObjectSnapTypes.Intersections,
                            "Escada: clique um ponto no sentido de subida");
                        var d = new XYZ(b.X - a.X, b.Y - a.Y, 0);
                        if (d.GetLength() < 1e-6) d = XYZ.BasisX;
                        var nivel = uidoc.ActiveView.GenLevel ?? EscadasRevit.NivelAbaixo(doc, a.Z);
                        pos = new PosicaoEscada
                        {
                            Base = new XYZ(a.X, a.Y, nivel?.Elevation ?? a.Z), Direcao = d.Normalize(), Nivel = nivel,
                            Origem = $"Ponto clicado, nível {nivel?.Name}; desnível e vão pela janela",
                        };
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                    {
                        TaskDialog.Show("Escada", "Selecione as duas vigas (ou lajes) de apoio antes, ou abra uma planta para clicar o ponto de partida.");
                        return Result.Cancelled;
                    }
                }
                var win = new EscadaWindow(e, pos.Origem);
                new WindowInteropHelper(win) { Owner = uiapp.MainWindowHandle };
                if (win.ShowDialog() != true || win.Resultado == null) return Result.Cancelled;
                string marca = Criar(doc, win.Resultado, pos, out var log);
                new TaskDialog("Escada")
                {
                    MainInstruction = $"Escada {marca} criada com {log.Infos.Count(i => i.StartsWith("Barras:"))} conjunto(s) de barras.",
                    MainContent = string.Join("\n", log.Infos.Concat(log.Warnings.Select(w => "Atenção: " + w))),
                }.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                try
                {
                    Directory.CreateDirectory(DetailingReport.LogDir);
                    File.WriteAllText(Path.Combine(DetailingReport.LogDir, $"escada_{DateTime.Now:yyyyMMdd_HHmmss}.log"), ex.ToString());
                }
                catch { }
                TaskDialog.Show("Escada", "Não foi possível concluir: " + ex.Message);
                return Result.Failed;
            }
        }

        /// <summary>Gera a família, coloca e arma. Devolve a marca.</summary>
        internal static string Criar(Document doc, ResultadoEscada r, PosicaoEscada pos, out PieceLog log)
        {
            log = new PieceLog("Escada");
            string marca = EscadasRevit.ProximaMarca(doc);
            Family fam = EscadasRevit.CriarFamilia(doc, r, marca, log);
            using (var t = new Transaction(doc, "Escada PCEB " + marca))
            {
                t.Start();
                EscadasRevit.Colocar(doc, fam, pos, r, marca, log, out List<ElementId> barras);
                log.Info($"Barras: {barras.Count} conjunto(s) — " + string.Join(", ", r.Barras.Select(b => b.Nome)));
                t.Commit();
            }
            return marca;
        }
    }
}
