using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DEB.Core.Numeracao;

namespace DEB.Revit.Tools
{
    /// <summary>Categorias oferecidas nas ferramentas (nome na tela → categoria).</summary>
    internal static class ToolCategories
    {
        public static readonly (string Name, BuiltInCategory Cat)[] Marked =
        {
            ("Pilares estruturais", BuiltInCategory.OST_StructuralColumns),
            ("Fundações", BuiltInCategory.OST_StructuralFoundation),
            ("Vigas", BuiltInCategory.OST_StructuralFraming),
            ("Lajes", BuiltInCategory.OST_Floors),
            ("Paredes", BuiltInCategory.OST_Walls),
        };
    }

    /// <summary>
    /// Seleciona as peças do modelo cuja marca começa com / contém / é igual / casa com uma expressão regular.
    /// Ex-scripts FiltrarPilaresPorMarca e FiltrarFundacoesPorMarca (agora para qualquer categoria da lista).
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class SelectByMarkCommand : ToolCommand
    {
        private static readonly (string Name, ModoFiltroMarca Mode)[] Modes =
        {
            ("Começa com (P → P1, P2, P10…)", ModoFiltroMarca.ComecaCom),
            ("Contém (F → F1, SF2, FUN3…)", ModoFiltroMarca.Contem),
            ("Igual a (F1 → só F1)", ModoFiltroMarca.Igual),
            ("Expressão regular (avançado)", ModoFiltroMarca.Regex),
            ("Todas (com marca)", ModoFiltroMarca.Todas),
        };

        protected override string Title => "Selecionar por marca";

        protected override Result Run(UIApplication uiapp, UIDocument uidoc, Document doc)
        {
            var form = new FieldForm(Title)
                .Choice("cat", "Categoria:", ToolCategories.Marked.Select(c => c.Name), ToolCategories.Marked[0].Name)
                .Choice("modo", "Tipo de filtro:", Modes.Select(m => m.Name), Modes[0].Name)
                .Text("texto", "Texto da marca:", "P")
                .Validate(f =>
                {
                    var mode = Modes.First(m => m.Name == f.Get("modo")).Mode;
                    if (mode != ModoFiltroMarca.Todas && f.Get("texto").Length == 0) return "Informe o texto da marca.";
                    if (mode == ModoFiltroMarca.Regex)
                        try { _ = new System.Text.RegularExpressions.Regex(f.Get("texto")); }
                        catch (ArgumentException ex) { return "Expressão regular inválida: " + ex.Message; }
                    return null;
                });
            if (!form.Show("Selecionar")) throw new ToolCancelled();

            var (catName, cat) = ToolCategories.Marked.First(c => c.Name == form.Get("cat"));
            ModoFiltroMarca modo = Modes.First(m => m.Name == form.Get("modo")).Mode;
            string texto = form.Get("texto");

            List<Element> found = Find(doc, cat, modo, texto);
            if (found.Count == 0) throw new ToolCancelled($"Nenhum elemento de \"{catName}\" com esse filtro.");

            uidoc.Selection.SetElementIds(found.Select(e => e.Id).ToList());
            var groups = found.GroupBy(ElementTools.MarkText).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToList();
            string lista = string.Join("\n", groups.Take(20).Select(g => $"{g.Key}: {g.Count()}"));
            if (groups.Count > 20) lista += "\n… (lista truncada)";
            TaskDialog.Show(Title, $"Selecionados {found.Count} elementos ({catName}).\n\n{lista}");
            return Result.Succeeded;
        }

        internal static List<Element> Find(Document doc, BuiltInCategory cat, ModoFiltroMarca modo, string texto) =>
            new FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType()
                .Where(e => FiltroMarca.Corresponde(ElementTools.MarkText(e), modo, texto)).ToList();
    }

    /// <summary>
    /// Troca o material de pilares e/ou vigas pelo material escolhido (no tipo; ou na peça, quando a família tem o
    /// material na instância). Ex-scripts AlterarMaterialPilares e AlterarMaterialVigas.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ChangeMaterialCommand : ToolCommand
    {
        private const string DefaultMaterial = "Concreto - Pré-Moldado - C40";

        protected override string Title => "Alterar material";

        protected override Result Run(UIApplication uiapp, UIDocument uidoc, Document doc)
        {
            List<Material> materials = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
            string preset = materials.FirstOrDefault(m => m.Name.Equals(DefaultMaterial, StringComparison.OrdinalIgnoreCase))?.Name;

            var form = new FieldForm(Title, "O material é gravado no tipo (todas as peças do tipo mudam) ou, se a família tiver o material na instância, em cada peça.")
                .Choice("mat", "Material:", materials.Select(m => m.Name), preset)
                .Check("pilares", "Pilares estruturais", true)
                .Check("vigas", "Vigas", true)
                .Validate(f => f.Get("mat") == null ? "Escolha o material."
                             : f.IsChecked("pilares") || f.IsChecked("vigas") ? null : "Marque pilares e/ou vigas.");
            if (!form.Show("Alterar")) throw new ToolCancelled();

            Material mat = materials.First(m => m.Name == form.Get("mat"));
            var cats = new List<BuiltInCategory>();
            if (form.IsChecked("pilares")) cats.Add(BuiltInCategory.OST_StructuralColumns);
            if (form.IsChecked("vigas")) cats.Add(BuiltInCategory.OST_StructuralFraming);

            var (types, pieces) = Apply(doc, mat, cats);
            TaskDialog.Show(Title, $"Material \"{mat.Name}\" gravado em {types} tipo(s) e em {pieces} peça(s) com material próprio.");
            return Result.Succeeded;
        }

        /// <summary>
        /// Grava o material (sem janelas; usado também pelo AutoRun). Como no script: parâmetro "Material" ou
        /// "Material estrutural" do TIPO. Famílias em que o material é da instância (ex.: as do Projeto1.rvt) recebem
        /// o material em cada peça. Devolve quantos tipos e quantas peças mudaram.
        /// </summary>
        internal static (int Types, int Pieces) Apply(Document doc, Material mat, List<BuiltInCategory> cats)
        {
            static Parameter MaterialParam(Element e)
            {
                Parameter p = e?.LookupParameter("Material") ?? e?.LookupParameter("Material estrutural");
                return p != null && !p.IsReadOnly && p.StorageType == StorageType.ElementId ? p : null;
            }
            List<Element> pieces = cats.SelectMany(c => new FilteredElementCollector(doc).OfCategory(c).WhereElementIsNotElementType()).ToList();
            var doneTypes = new HashSet<ElementId>();
            int types = 0, own = 0;
            using (var t = new Transaction(doc, "Alterar material"))
            {
                t.Start();
                foreach (Element e in pieces)
                {
                    ElementId typeId = e.GetTypeId();
                    Parameter onType = MaterialParam(doc.GetElement(typeId));
                    if (onType != null)
                    {
                        if (doneTypes.Add(typeId)) { onType.Set(mat.Id); types++; }
                        continue;
                    }
                    Parameter onPiece = MaterialParam(e);
                    if (onPiece == null) continue;
                    onPiece.Set(mat.Id);
                    own++;
                }
                t.Commit();
            }
            return (types, own);
        }
    }
}
