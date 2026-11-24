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
    [Transaction(TransactionMode.Manual)]
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
            var sessao = new SessaoAnalise(e, DEB.Revit.Configuracao.ConfiguracaoRevit.Carregar(doc), doc.Title, AjustesRevit.Ler(doc));
            // exportação (D50, item 8): pergunta onde gravar
            string pastaDoc = string.IsNullOrEmpty(doc.PathName) ? System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments) : System.IO.Path.GetDirectoryName(doc.PathName);
            sessao.GravarArquivo = (nome, texto) =>
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = System.IO.Path.GetFileNameWithoutExtension(doc.Title) + "_" + nome, InitialDirectory = pastaDoc,
                    Filter = "Arquivo (*" + System.IO.Path.GetExtension(nome) + ")|*" + System.IO.Path.GetExtension(nome), Title = "Exportar o modelo de análise",
                };
                if (dlg.ShowDialog() != true) return null;
                System.IO.File.WriteAllText(dlg.FileName, texto, new System.Text.UTF8Encoding(false));
                return dlg.FileName;
            };
            var win = new JanelaAnalise(sessao.Json(), doc.Title) { Sessao = sessao };
            new WindowInteropHelper(win) { Owner = uiapp.MainWindowHandle };
            win.ShowDialog();
            if (win.Erro != null) TaskDialog.Show("Modelo de análise", win.Erro);
            if (sessao.CargasAlteradas) CargasRevit.GravarPceb(doc, e.Cargas);
            if (sessao.EnviarCargas)
            {
                var nc = new List<string>();
                CargasRevit.EnviarAoRevit(doc, e, nc);
                TaskDialog.Show("Modelo de análise", string.Join(System.Environment.NewLine, nc));
            }
            if (sessao.Alterado)
            {
                AjustesRevit.Gravar(doc, sessao.Ajustes);
                TaskDialog.Show("Modelo de análise", $"Ajustes do modelo analítico gravados no modelo ({sessao.Ajustes.Count} objetos ajustados). " +
                                "Eles valem também no cálculo dos pilares, das vigas e das ligações.");
            }
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
            if (e.Pilares.Count + e.Vigas.Count + e.Lajes.Count + e.Metalicas.Count == 0) return null;
            // ajustes da janela de análise gravados no modelo (D50): valem por cima do que foi lido
            e.ValoresDePartida = SessaoAnalise.Valores(e);
            // cargas (D50, item 2): as lançadas no PCEB (gravadas no modelo) e as nativas do Revit
            e.Cargas.AddRange(CargasRevit.LerPceb(doc, notas));
            try { e.Cargas.AddRange(CargasRevit.LerNativas(doc, e, notas)); }
            catch (System.Exception ex) { notas.Add("Cargas do Revit não lidas: " + ex.Message); }
            var ajustes = AjustesRevit.Ler(doc, notas);
            if (ajustes.Count > 0)
            {
                notas.AddRange(AjustesAnalise.Aplicar(e, ajustes));
                notas.Add($"Ajustes do modelo analítico aplicados em {ajustes.Count} objetos (janela \"Modelo de análise\").");
            }
            try { e.Analisar(cfg); }
            catch (DEB.Core.Analise.Barras.ErroAnalise ex) { notas.Add("Análise: " + ex.Message); return null; }
            foreach (string n in notas) e.Avisos.Add(n);
            return e;
        }
    }
}
