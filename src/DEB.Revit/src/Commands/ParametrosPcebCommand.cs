using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DEB.Revit.Analise;
using DEB.Revit.Formas;
using DEB.Revit.Presentation;
using DEB.Revit.Vigas;

namespace DEB.Revit.Commands
{
    /// <summary>
    /// "Parâmetros do PCEB" (D50): cria no projeto, de uma vez, os parâmetros compartilhados que o PCEB usa para trabalhar com o Revit e
    /// com a análise — armaduras (aço, peso, comprimento do núcleo), formas (seção, níveis, esforços, lajes), cargas das vigas e cargas e
    /// tipo das lajes. Mostra antes o que falta; os que já existem não mudam (os GUIDs são fixos).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ParametrosPcebCommand : IExternalCommand
    {
        internal sealed class Grupo
        {
            public string Nome = "", Categorias = "";
            public List<(string Nome, Guid Guid)> Itens = new List<(string, Guid)>();
            public Func<Document, List<string>, int> Criar;
        }

        internal static List<Grupo> Grupos() => new List<Grupo>
        {
            new Grupo { Nome = "Armaduras", Categorias = "vergalhões",
                Itens = RebarData.Definicoes.ToList(),
                Criar = (d, av) => { int antes = RebarData.Definicoes.Count(x => SharedParameterElement.Lookup(d, x.Guid) == null); RebarData.EnsureParameters(d);
                                     return antes - RebarData.Definicoes.Count(x => SharedParameterElement.Lookup(d, x.Guid) == null); } },
            new Grupo { Nome = "Formas", Categorias = "pilares, vigas, lajes e fundações",
                Itens = ParametrosFormas.Todos.Select(x => (x.Nome, x.Guid)).ToList(), Criar = ParametrosFormas.Garantir },
            new Grupo { Nome = "Cargas das vigas", Categorias = "vigas (framing estrutural)",
                Itens = ParametrosCargasViga.Todos.Select(x => (x.Nome, x.Guid)).ToList(), Criar = ParametrosCargasViga.Garantir },
            new Grupo { Nome = "Análise das lajes", Categorias = "pisos",
                Itens = ParametrosAnalise.Todos.Select(x => (x.Nome, x.Guid)).ToList(), Criar = ParametrosAnalise.Garantir },
        };

        /// <summary>Texto do que falta por grupo (vazio = nada falta).</summary>
        internal static string Faltando(Document doc, List<Grupo> grupos)
        {
            var sb = new StringBuilder();
            foreach (var g in grupos)
            {
                var f = g.Itens.Where(i => SharedParameterElement.Lookup(doc, i.Guid) == null).Select(i => i.Nome).ToList();
                if (f.Count > 0) sb.AppendLine($"• {g.Nome} ({g.Categorias}): {string.Join(", ", f)}");
            }
            return sb.ToString();
        }

        /// <summary>Cria os que faltam numa transação; devolve o relatório.</summary>
        internal static string Criar(Document doc, List<Grupo> grupos)
        {
            var avisos = new List<string>();
            var sb = new StringBuilder();
            using (var t = new Transaction(doc, "PCEB: parâmetros do PCEB"))
            {
                t.Start();
                foreach (var g in grupos)
                {
                    int n;
                    try { n = g.Criar(doc, avisos); }
                    catch (Exception ex) { avisos.Add($"{g.Nome}: {ex.Message}"); n = 0; }
                    int total = g.Itens.Count, tem = g.Itens.Count(i => SharedParameterElement.Lookup(doc, i.Guid) != null);
                    sb.AppendLine($"{g.Nome}: {n} criado(s); {tem} de {total} no projeto.");
                }
                t.Commit();
            }
            foreach (string a in avisos) sb.AppendLine("Aviso: " + a);
            return sb.ToString();
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument) { TaskDialog.Show("Parâmetros do PCEB", "Abra um projeto (não uma família)."); return Result.Cancelled; }
            var grupos = Grupos();
            string falta = Faltando(doc, grupos);
            if (falta.Length == 0)
            {
                TaskDialog.Show("Parâmetros do PCEB", "O projeto já tem todos os parâmetros do PCEB (" + grupos.Sum(g => g.Itens.Count) + ").");
                return Result.Succeeded;
            }
            var td = new TaskDialog("Parâmetros do PCEB")
            {
                MainInstruction = "Criar os parâmetros do PCEB que faltam neste projeto?",
                MainContent = falta + "\nSão parâmetros compartilhados com GUID fixo (os mesmos em todos os projetos e nas famílias de anotação). " +
                              "Os que já existem não mudam.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.Yes,
            };
            if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;
            TaskDialog.Show("Parâmetros do PCEB", Criar(doc, grupos));
            return Result.Succeeded;
        }
    }
}
