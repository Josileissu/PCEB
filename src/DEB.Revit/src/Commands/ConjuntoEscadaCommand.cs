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
    /// "Escada L/U" (D43): escada montada por peças. Pede o eixo do apoio de baixo do 1º lance e o sentido de subida, abre a janela
    /// do conjunto (arranjo em U, em L ou em linha; cada peça editável na janela da escada) e, no aceite, cria todas as peças com as
    /// barras e a viga do patamar, nas posições do arranjo.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ConjuntoEscadaCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            try
            {
                PosicaoEscada pos;
                try
                {
                    XYZ a = uidoc.Selection.PickPoint(ObjectSnapTypes.Endpoints | ObjectSnapTypes.Midpoints | ObjectSnapTypes.Intersections,
                        "Escada L/U: clique no eixo do apoio de baixo do 1º lance (meio da largura)");
                    XYZ b = uidoc.Selection.PickPoint(ObjectSnapTypes.Endpoints | ObjectSnapTypes.Midpoints | ObjectSnapTypes.Intersections,
                        "Escada L/U: clique um ponto no sentido de subida do 1º lance");
                    var d = new XYZ(b.X - a.X, b.Y - a.Y, 0);
                    if (d.GetLength() < 1e-6) d = XYZ.BasisX;
                    var nivel = uidoc.ActiveView.GenLevel ?? EscadasRevit.NivelAbaixo(doc, a.Z);
                    pos = new PosicaoEscada { Base = new XYZ(a.X, a.Y, nivel?.Elevation ?? a.Z), Direcao = d.Normalize(), Nivel = nivel, Origem = $"Ponto clicado, nível {nivel?.Name}" };
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    TaskDialog.Show("Escada L/U", "Abra uma planta para clicar o ponto de partida.");
                    return Result.Cancelled;
                }
                var lance = ModelosConjunto.LancePadrao();
                var win = new ConjuntoEscadaWindow(lance, pos.Origem);
                new WindowInteropHelper(win) { Owner = uiapp.MainWindowHandle };
                if (win.ShowDialog() != true || win.Resultado == null) return Result.Cancelled;
                var log = Criar(doc, win.Conjunto, win.Resultado, pos);
                new TaskDialog("Escada L/U")
                {
                    MainInstruction = $"Conjunto criado: {string.Join(", ", log.Keys)}.",
                    MainContent = string.Join("\n", log.Values.SelectMany(l => l.Infos.Concat(l.Warnings.Select(w => "Atenção: " + w)))),
                }.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                try
                {
                    Directory.CreateDirectory(DetailingReport.LogDir);
                    File.WriteAllText(Path.Combine(DetailingReport.LogDir, $"escada_lu_{DateTime.Now:yyyyMMdd_HHmmss}.log"), ex.ToString());
                }
                catch { }
                TaskDialog.Show("Escada L/U", "Não foi possível concluir: " + ex.Message);
                return Result.Failed;
            }
        }

        /// <summary>Cria as peças (família, instância e barras, cada uma na sua transação) e as vigas do patamar. Devolve o relatório por peça (nome → marca).</summary>
        internal static Dictionary<string, PieceLog> Criar(Document doc, ConjuntoEscada c, ResultadoConjunto res, PosicaoEscada origem)
        {
            var logs = new Dictionary<string, PieceLog>();
            foreach (var (nome, _) in c.Pecas)
            {
                if (!res.Pecas.TryGetValue(nome, out var r) || !c.Posicoes.TryGetValue(nome, out var p)) continue;
                var pos = EscadasRevit.NoModelo(origem, p, doc);
                string marca = EscadaCommand.Criar(doc, r, pos, out var log);
                log.Info($"{nome} → {marca}");
                logs[$"{nome} ({marca})"] = log;
            }
            foreach (var v in c.Vigas)
            {
                if (!c.Posicoes.TryGetValue(v.Nome, out var p)) continue;
                var log = new PieceLog("Viga do patamar");
                string marca = EscadasRevit.ProximaMarca(doc);
                var fam = EscadasRevit.CriarFamiliaViga(doc, v, marca, log);
                using (var t = new Transaction(doc, "Viga do patamar PCEB " + marca))
                {
                    t.Start();
                    WarningSwallower.Attach(t);
                    EscadasRevit.ColocarViga(doc, fam, EscadasRevit.NoModelo(origem, p, doc), v, marca);
                    if (t.Commit() != TransactionStatus.Committed) log.Error("A viga do patamar não foi criada: o Revit recusou" + WarningSwallower.TakeReason() + ".");
                }
                logs[$"{v.Nome} ({marca})"] = log;
            }
            return logs;
        }
    }
}
