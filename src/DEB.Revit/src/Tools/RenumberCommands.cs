using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DEB.Core.Numeracao;

namespace DEB.Revit.Tools
{
    /// <summary>
    /// Renumera a marca (ALL_MODEL_MARK) das peças selecionadas — soltas ou dentro de grupos — com prefixo e número
    /// inicial, na ordem do croqui (algoritmos em DEB.Core: OrdenacaoMarcas). Ex-scripts RenomearMarca* (Revit_Suelio).
    /// </summary>
    public abstract class RenumberCommand : ToolCommand
    {
        /// <summary>Tolerância das linhas: 0,50 m (como nos scripts).</summary>
        protected const double RowTolerance = 0.50 * ElementTools.FeetPerMeter;

        protected abstract string Plural { get; }        // "pilares"
        protected abstract string DefaultPrefix { get; }  // "P"
        protected abstract bool Accept(Element el);
        /// <summary>Ordem das peças; devolve também o texto do resumo.</summary>
        protected abstract (List<Element> Ordered, string Summary) Order(List<Element> items);

        protected override string Title => "Renumerar " + Plural;

        protected override Result Run(UIApplication uiapp, UIDocument uidoc, Document doc)
        {
            if (uidoc.Selection.GetElementIds().Count == 0)
                throw new ToolCancelled($"Selecione {Plural} ou grupos que os contenham.");
            List<Element> items = ElementTools.FromSelection(uidoc, el => Accept(el) && ElementTools.Mark(el) is { IsReadOnly: false });
            if (items.Count == 0) throw new ToolCancelled($"Nenhum elemento válido ({Plural}) na seleção.");

            var form = new FieldForm(Title, $"{items.Count} {Plural} na seleção. Numeração de cima para baixo e da esquerda para a direita.")
                .Text("prefixo", "Prefixo da marca:", DefaultPrefix)
                .Text("inicio", "Número inicial:", "1")
                .Validate(f => string.IsNullOrWhiteSpace(f.Get("prefixo")) ? "Informe o prefixo."
                             : int.TryParse(f.Get("inicio"), out _) ? null : "Número inicial inválido.");
            if (!form.Show("Renumerar")) throw new ToolCancelled();
            string prefix = form.Get("prefixo");
            int n = int.Parse(form.Get("inicio"));

            TaskDialog.Show(Title, Apply(doc, items, prefix, n));
            return Result.Succeeded;
        }

        /// <summary>Ordena e grava as marcas numa transação (sem janelas; usado também pelo AutoRun). Devolve o resumo.</summary>
        internal string Apply(Document doc, List<Element> items, string prefix, int start)
        {
            var (ordered, summary) = Order(items);
            int done = 0, n = start;
            using (var t = new Transaction(doc, Title))
            {
                t.Start();
                foreach (Element el in ordered)
                {
                    Parameter mark = ElementTools.Mark(el);
                    if (mark == null || mark.IsReadOnly) continue;
                    mark.Set($"{prefix}{n++}");
                    done++;
                }
                t.Commit();
            }
            return $"Renumerados {done} {Plural}{summary}.";
        }

        /// <summary>Pilares, fundações e peças: canto superior esquerdo do croqui, linhas por proximidade em Y.</summary>
        protected static (List<Element>, string) ByTopLeftRows(List<Element> items)
        {
            var refs = new List<(Element El, double X, double Y)>();
            foreach (Element el in items)
                if (ElementTools.TryTopLeft(el, out double x, out double y)) refs.Add((el, x, y));
            ResultadoOrdenacao r = OrdenacaoMarcas.PorLinhas(refs.Select(p => (p.X, p.Y)).ToList(), RowTolerance);
            return (r.Ordem.Select(i => refs[i].El).ToList(), $" em {r.Linhas} linhas");
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class RenumberColumnsCommand : RenumberCommand
    {
        protected override string Plural => "pilares";
        protected override string DefaultPrefix => "P";
        protected override bool Accept(Element el) => ElementTools.IsCategory(el, BuiltInCategory.OST_StructuralColumns);
        protected override (List<Element>, string) Order(List<Element> items) => ByTopLeftRows(items);
    }

    [Transaction(TransactionMode.Manual)]
    public class RenumberFoundationsCommand : RenumberCommand
    {
        protected override string Plural => "fundações";
        protected override string DefaultPrefix => "F";
        protected override bool Accept(Element el) => ElementTools.IsCategory(el, BuiltInCategory.OST_StructuralFoundation);
        protected override (List<Element>, string) Order(List<Element> items) => ByTopLeftRows(items);
    }

    /// <summary>Qualquer peça com marca editável (peças, painéis, famílias diversas).</summary>
    [Transaction(TransactionMode.Manual)]
    public class RenumberPiecesCommand : RenumberCommand
    {
        protected override string Plural => "peças";
        protected override string DefaultPrefix => "P";
        protected override bool Accept(Element el) => el.Category != null;
        protected override (List<Element>, string) Order(List<Element> items) => ByTopLeftRows(items);
    }

    [Transaction(TransactionMode.Manual)]
    public class RenumberBeamsCommand : RenumberCommand
    {
        protected override string Plural => "vigas";
        protected override string DefaultPrefix => "V";
        protected override bool Accept(Element el) => ElementTools.IsCategory(el, BuiltInCategory.OST_StructuralFraming) && el is FamilyInstance;

        protected override (List<Element>, string) Order(List<Element> items)
        {
            var axes = new List<(Element El, ((double, double, double), (double, double, double)) Axis)>();
            foreach (Element el in items)
                if (ElementTools.TryAxis(el, out var a)) axes.Add((el, a));
            ResultadoOrdenacao r = OrdenacaoMarcas.Vigas(axes.Select(x => x.Axis).ToList(), RowTolerance);
            return (r.Ordem.Select(i => axes[i].El).ToList(),
                $":\n• {r.Horizontais} horizontais em {r.LinhasHorizontais} linhas\n• {r.Verticais} verticais em {r.LinhasVerticais} linhas\n• {r.Inclinadas} inclinadas");
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class RenumberSlabsCommand : RenumberCommand
    {
        protected override string Plural => "lajes";
        protected override string DefaultPrefix => "L";
        protected override bool Accept(Element el) => ElementTools.IsCategory(el, BuiltInCategory.OST_Floors);

        protected override (List<Element>, string) Order(List<Element> items)
        {
            var boxes = new List<(Element El, (double, double, double, double) Box)>();
            foreach (Element el in items)
                if (ElementTools.TryTopFaceBounds(el, out var b)) boxes.Add((el, b));
            ResultadoOrdenacao r = OrdenacaoMarcas.Lajes(boxes.Select(x => x.Box).ToList());
            return (r.Ordem.Select(i => boxes[i].El).ToList(), $" em {r.Linhas} linhas");
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class RenumberWallsCommand : RenumberCommand
    {
        protected override string Plural => "paredes";
        protected override string DefaultPrefix => "PAR";
        protected override bool Accept(Element el) => el is Wall;

        protected override (List<Element>, string) Order(List<Element> items)
        {
            var axes = new List<(Element El, ((double, double, double), (double, double, double)) Axis)>();
            foreach (Element el in items)
                if (ElementTools.TryAxis(el, out var a)) axes.Add((el, a));
            ResultadoOrdenacao r = OrdenacaoMarcas.Paredes(axes.Select(x => x.Axis).ToList(), RowTolerance);
            return (r.Ordem.Select(i => axes[i].El).ToList(),
                $":\n• Horizontais: {r.Horizontais}\n• Verticais: {r.Verticais}\n• Inclinadas: {r.Inclinadas}");
        }
    }
}
