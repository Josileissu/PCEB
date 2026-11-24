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
    /// "Muro de arrimo" (D47): com uma parede ou um muro PCEB selecionado (ou escolhido), lê o elemento, abre a janela do PCEB
    /// (seção, empuxo, estabilidade, armaduras) e, em "Aplicar ao elemento", grava os dados (no muro PCEB também troca a seção e o
    /// comprimento). "Criar no Revit" (D50, item 7): cria a família PCEB com a seção da janela e pede na planta o início, a direção e
    /// o lado do aterro; o comprimento é o da janela.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class MuroArrimoCommand : IExternalCommand
    {
        private sealed class FiltroParede : ISelectionFilter
        {
            public bool AllowElement(Element e) => e is Wall || FamiliaMuro.EhMuro(e);
            public bool AllowReference(Reference r, XYZ p) => false;
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null) return Result.Cancelled;
            Element w = uidoc.Selection.GetElementIds().Select(doc.GetElement).FirstOrDefault(e => e is Wall || FamiliaMuro.EhMuro(e));
            if (w == null)
            {
                try { w = doc.GetElement(uidoc.Selection.PickObject(ObjectType.Element, new FiltroParede(), "Selecione a parede ou o muro PCEB (Esc = calcular ou criar um novo)")); }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { w = null; }
            }
            var notas = new List<string>();
            bool aplicou = Abrir(doc, w, commandData.Application.MainWindowHandle, notas, null, "", uidoc);
            if (notas.Count > 0) TaskDialog.Show("Muro de arrimo", string.Join("\n\n", notas));
            return aplicou ? Result.Succeeded : Result.Cancelled;
        }

        /// <summary>Abre a janela; devolve verdadeiro se os dados foram aplicados à parede. captura ≠ null (AutoRun): só a imagem.</summary>
        internal static bool Abrir(Document doc, Element w, IntPtr dono, List<string> notas, string captura, string aba, UIDocument uidoc = null)
        {
            var cfg = ConfiguracaoRevit.Carregar(doc, notas);
            DadosMuro dados = w != null ? MuroRevit.LerElemento(w, cfg, notas) : DadosMuro.DaConfiguracao(cfg);
            string titulo = w != null ? $"{w.Name} (id {w.Id})" : "sem elemento";
            var sessao = new SessaoMuro(dados, cfg, titulo) { TemElemento = w != null };
            var win = new JanelaMuro(sessao) { Captura = captura, ModoCaptura = aba ?? "" };
            if (dono != IntPtr.Zero) new WindowInteropHelper(win) { Owner = dono };
            win.ShowDialog();
            if (win.Erro != null) { notas.Add(win.Erro); return false; }
            if (!sessao.Aplicar || captura != null) return false;
            var log = new System.Text.StringBuilder();
            if (sessao.Criar)
            {
                if (!PedirPosicao(uidoc, out XYZ p0, out XYZ dir, out bool esquerda, out Level nivel, notas)) return false;
                var fi = FamiliaMuro.Criar(doc, sessao.Dados, nivel, p0, dir, esquerda, log);
                if (log.Length > 0) notas.Add(log.ToString().Trim());
                if (fi == null) return false;
                notas.Add($"Situação do cálculo: {sessao.Resultado.Situacao}.");
            }
            else if (w is Wall parede)
            {
                MuroRevit.Gravar(parede, sessao.Dados);
                notas.Add($"Dados do muro gravados na parede {w.Id}. Situação: {sessao.Resultado.Situacao}.");
            }
            else if (w is FamilyInstance muro)
            {
                FamiliaMuro.Atualizar(muro, sessao.Dados, log);
                if (log.Length > 0) notas.Add(log.ToString().Trim());
                notas.Add($"Muro {w.Id} atualizado (seção, comprimento e dados). Situação: {sessao.Resultado.Situacao}.");
            }
            else { notas.Add("Sem elemento selecionado: use \"Criar no Revit\" ou selecione a parede e abra de novo para aplicar."); return false; }
            foreach (var v in sessao.Resultado.Verificacoes.Where(v => v.Situacao != DEB.Core.Normas.Verificacao.SituacaoVerificacao.Ok))
                notas.Add(v.Mensagem);
            return true;
        }

        /// <summary>Na planta ativa: início, um ponto na direção do muro e um ponto do lado do aterro (Esc cancela).</summary>
        private static bool PedirPosicao(UIDocument uidoc, out XYZ p0, out XYZ dir, out bool esquerda, out Level nivel, List<string> notas)
        {
            p0 = dir = null; esquerda = true; nivel = null;
            if (uidoc == null) return false;
            Document doc = uidoc.Document;
            View v = doc.ActiveView;
            nivel = v.GenLevel ?? new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).FirstOrDefault();
            if (!(v is ViewPlan) || nivel == null) { notas.Add("Abra uma planta (vista de nível) para criar o muro: os pontos são clicados nela."); return false; }
            try
            {
                if (v.SketchPlane == null)
                    using (var t = new Transaction(doc, "PCEB: plano de trabalho"))
                    {
                        t.Start();
                        v.SketchPlane = SketchPlane.Create(doc, nivel.Id);
                        t.Commit();
                    }
                p0 = uidoc.Selection.PickPoint("Muro de arrimo: clique o INÍCIO do muro (face de trás do fuste, terreno da frente no nível)");
                XYZ p1 = uidoc.Selection.PickPoint("Clique um ponto na DIREÇÃO do muro (o comprimento é o da janela)");
                XYZ p2 = uidoc.Selection.PickPoint("Clique um ponto do LADO DO ATERRO (solo contido)");
                dir = new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0);
                if (dir.GetLength() < 1e-6) { notas.Add("Direção indefinida (pontos iguais)."); return false; }
                dir = dir.Normalize();
                XYZ lado = new XYZ(p2.X - p0.X, p2.Y - p0.Y, 0);
                esquerda = dir.X * lado.Y - dir.Y * lado.X >= 0;
                return true;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { notas.Add("Criação do muro cancelada."); return false; }
        }
    }
}
