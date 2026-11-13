using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DEB.Revit.Core;
using DEB.Revit.Presentation;
using DEB.Revit.Tools;

namespace DEB.Revit.Formas
{
    /// <summary>"Atualizar dados das pranchas" (painel Formas): cria os parâmetros SD_ que faltam e grava os textos calculados.</summary>
    [Transaction(TransactionMode.Manual)]
    public class AtualizarDadosFormasCommand : ToolCommand
    {
        protected override string Title => "Atualizar dados das pranchas";

        protected override Result Run(UIApplication uiapp, UIDocument uidoc, Document doc)
        {
            var log = new PieceLog(Title);
            PresentationStandard std = ExecucaoFormas.Padrao(log);
            bool ok = ExecucaoFormas.Dados(doc, std, log);
            FormasUi.Mostrar(Title, ExecucaoFormas.Relatorio(log, ok ? "Dados das pranchas atualizados." : "Dados NÃO atualizados (nada foi alterado).", out string arq), arq);
            return ok ? Result.Succeeded : Result.Failed;
        }
    }

    /// <summary>"Cargas (CSV)": importa os esforços na fundação (pela marca do pilar) ou exporta os do modelo no mesmo formato.</summary>
    [Transaction(TransactionMode.Manual)]
    public class CargasCsvFormasCommand : ToolCommand
    {
        protected override string Title => "Cargas na fundação (CSV)";

        protected override Result Run(UIApplication uiapp, UIDocument uidoc, Document doc)
        {
            var td = new TaskDialog(Title)
            {
                MainInstruction = "Cargas na fundação por arquivo CSV",
                MainContent = "Colunas: " + DEB.Core.Formas.CsvCargas.Cabecalho + "\nSeparador \";\", vírgula ou ponto decimal; célula vazia não altera o valor do pilar.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Importar CSV…", "Grava SD_Fz_Max… nos pilares com a mesma marca.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Exportar CSV…", "Grava os esforços do modelo (para editar no Excel e importar de novo).");
            TaskDialogResult r = td.Show();
            var log = new PieceLog(Title);
            PresentationStandard std = ExecucaoFormas.Padrao(log);
            if (r == TaskDialogResult.CommandLink1)
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "CSV (*.csv;*.txt)|*.csv;*.txt|Todos (*.*)|*.*", Title = "Importar cargas na fundação" };
                if (dlg.ShowDialog() != true) throw new ToolCancelled();
                string texto = ExecucaoFormas.LerTexto(dlg.FileName);
                bool ok = ExecucaoFormas.Etapa(doc, "Formas: importar cargas", log, () =>
                {
                    var avisos = new List<string>();
                    ParametrosFormas.Garantir(doc, avisos);
                    foreach (string a in avisos) log.Warn(a);
                    doc.Regenerate();
                    CargasFormas.Importar(doc, std.Formas, texto, log);
                });
                FormasUi.Mostrar(Title, ExecucaoFormas.Relatorio(log, ok ? "Importado de " + dlg.FileName : "Importação NÃO feita (nada foi alterado).", out string arq), arq);
                return ok ? Result.Succeeded : Result.Failed;
            }
            if (r == TaskDialogResult.CommandLink2)
            {
                var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "cargas_fundacao.csv", Title = "Exportar cargas na fundação" };
                if (dlg.ShowDialog() != true) throw new ToolCancelled();
                string texto = CargasFormas.Exportar(doc, std.Formas, out int n);
                File.WriteAllText(dlg.FileName, texto, new UTF8Encoding(true));
                TaskDialog.Show(Title, $"{n} pilar(es) exportado(s) para\n{dlg.FileName}");
                return Result.Succeeded;
            }
            throw new ToolCancelled();
        }
    }

    /// <summary>Base dos botões "Planta de locação" e "Planta de montagem": janela (nível, vista, folha) e o fluxo completo.</summary>
    public abstract class PlantaFormasCommand : ToolCommand
    {
        protected abstract TipoPlanta Tipo { get; }

        protected override Result Run(UIApplication uiapp, UIDocument uidoc, Document doc)
        {
            var log = new PieceLog(Title);
            PresentationStandard std = ExecucaoFormas.Padrao(log);
            FormasSheetSpec spec = new FolhaFormas(doc, std, log).Spec(Tipo);
            List<Level> niveis = ModeloFormas.Niveis(doc);
            if (niveis.Count == 0) throw new ToolCancelled("O projeto não tem níveis.");

            Level padrao = (uidoc.ActiveView as ViewPlan)?.GenLevel ?? FormasUi.NivelPadrao(doc, niveis, Tipo);
            const string automatica = "(a do comando, ou uma cópia da planta do nível)";
            var plantas = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .Where(p => !p.IsTemplate && p.GenLevel != null && (p.ViewType == ViewType.EngineeringPlan || p.ViewType == ViewType.FloorPlan))
                .OrderBy(p => p.GenLevel.ProjectElevation).ThenBy(p => p.Name).ToList();
            var rotulos = plantas.ToDictionary(p => $"{p.Name}  [{p.GenLevel.Name}]", p => p);

            var form = new FieldForm(Title,
                    "Prepara a planta do nível em 1:" + std.Formas.Escala + " com cotas de eixos e identificadores, a tabela " +
                    (Tipo == TipoPlanta.Locacao ? "de cargas na fundação" : Tipo == TipoPlanta.Paginacao ? "de painéis do nível" : "de lajes do nível") +
                    " e a folha. Rodar de novo atualiza o que o comando criou. {Nivel} = nome do nível.", 520)
                .Choice("nivel", "Nível:", niveis.Select(l => l.Name), padrao?.Name)
                .Choice("vista", "Vista:", new[] { automatica }.Concat(rotulos.Keys), automatica)
                .Check("folha", "Montar a folha", true)
                .Text("numero", "Número da folha:", spec.NumeroFolha)
                .Text("nome", "Nome da folha:", spec.NomeFolha)
                .Validate(f =>
                {
                    if (f.IsChecked("folha") && string.IsNullOrWhiteSpace(f.Get("numero"))) return "Informe o número da folha.";
                    if (f.Get("vista") != automatica && rotulos.TryGetValue(f.Get("vista") ?? "", out ViewPlan vp) && vp.GenLevel.Name != f.Get("nivel"))
                        return $"A vista escolhida é do nível \"{vp.GenLevel.Name}\", não de \"{f.Get("nivel")}\".";
                    return null;
                });
            if (!form.Show("Gerar")) throw new ToolCancelled();

            Level nivel = niveis.First(l => l.Name == form.Get("nivel"));
            ViewPlan indicada = form.Get("vista") != automatica && rotulos.TryGetValue(form.Get("vista") ?? "", out ViewPlan iv) ? iv : null;
            ViewSheet sheet = ExecucaoFormas.Planta(doc, std, Tipo, nivel, indicada, form.Get("numero"), form.Get("nome"), form.IsChecked("folha"), log, out ViewPlan vista);
            FormasUi.Mostrar(Title, ExecucaoFormas.Relatorio(log, vista != null ? $"{Title}: {nivel.Name}" : "A vista não foi preparada (etapa desfeita).", out string arq), arq);
            try { if (sheet != null) uidoc.ActiveView = sheet; else if (vista != null) uidoc.ActiveView = vista; } catch { /* fica na vista atual */ }
            return vista != null ? Result.Succeeded : Result.Failed;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class PlantaLocacaoCommand : PlantaFormasCommand
    {
        protected override string Title => "Planta de locação";
        protected override TipoPlanta Tipo => TipoPlanta.Locacao;
    }

    [Transaction(TransactionMode.Manual)]
    public class PlantaMontagemCommand : PlantaFormasCommand
    {
        protected override string Title => "Planta de montagem";
        protected override TipoPlanta Tipo => TipoPlanta.Montagem;
    }

    [Transaction(TransactionMode.Manual)]
    public class PlantaPaginacaoCommand : PlantaFormasCommand
    {
        protected override string Title => "Paginação das lajes";
        protected override TipoPlanta Tipo => TipoPlanta.Paginacao;
    }

    /// <summary>"Cortes e elevações": cortes do comando (um por direção, entre os eixos centrais) e as quatro elevações, com
    /// identificadores de pilares e vigas, e as duas folhas.</summary>
    [Transaction(TransactionMode.Manual)]
    public class CortesFormasCommand : ToolCommand
    {
        protected override string Title => "Cortes e elevações";

        protected override Result Run(UIApplication uiapp, UIDocument uidoc, Document doc)
        {
            var log = new PieceLog(Title);
            PresentationStandard std = ExecucaoFormas.Padrao(log);
            ViewSheet a = ExecucaoFormas.Cortes(doc, std, true, log, out List<ViewSection> c);
            ViewSheet b = ExecucaoFormas.Cortes(doc, std, false, log, out List<ViewSection> e);
            FormasUi.Mostrar(Title, ExecucaoFormas.Relatorio(log, $"{c.Count} corte(s), {e.Count} elevação(ões); folhas {a?.SheetNumber ?? "-"} e {b?.SheetNumber ?? "-"}.", out string arq), arq);
            try { if (a != null) uidoc.ActiveView = a; } catch { }
            return c.Count + e.Count > 0 ? Result.Succeeded : Result.Failed;
        }
    }

    /// <summary>"Seções consideradas": tabelas por tipo de viga e de pilar (seção, tipo, quantidade, material) e a folha.</summary>
    [Transaction(TransactionMode.Manual)]
    public class SecoesFormasCommand : ToolCommand
    {
        protected override string Title => "Seções consideradas";

        protected override Result Run(UIApplication uiapp, UIDocument uidoc, Document doc)
        {
            var log = new PieceLog(Title);
            ViewSheet s = ExecucaoFormas.Secoes(doc, ExecucaoFormas.Padrao(log), log);
            FormasUi.Mostrar(Title, ExecucaoFormas.Relatorio(log, $"Folha {s?.SheetNumber ?? "-"}.", out string arq), arq);
            try { if (s != null) uidoc.ActiveView = s; } catch { }
            return s != null ? Result.Succeeded : Result.Failed;
        }
    }

    internal static class FormasUi
    {
        /// <summary>Relatório numa janela (texto longo: as primeiras linhas e o caminho do arquivo).</summary>
        public static void Mostrar(string titulo, string texto, string arquivo)
        {
            string[] l = texto.Replace("\r", "").Split('\n');
            string corpo = string.Join("\n", l.Take(40)) + (l.Length > 40 ? "\n…" : "");
            var td = new TaskDialog(titulo) { MainContent = corpo };
            if (arquivo != null) td.FooterText = "Relatório completo: " + arquivo;
            td.Show();
        }

        /// <summary>Nível sugerido: locação = o mais perto do zero do projeto; montagem = o primeiro acima dele.</summary>
        public static Level NivelPadrao(Document doc, List<Level> niveis, TipoPlanta tipo)
        {
            double zero = 0;
            try { zero = BasePoint.GetProjectBasePoint(doc).Position.Z; } catch { /* zero interno */ }
            Level terreo = niveis.OrderBy(l => Math.Abs(l.ProjectElevation - zero)).First();
            return tipo == TipoPlanta.Locacao ? terreo : niveis.FirstOrDefault(l => l.ProjectElevation > terreo.ProjectElevation + 1e-6) ?? terreo;
        }
    }
}
