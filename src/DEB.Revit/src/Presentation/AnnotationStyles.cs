using System.Linq;
using Autodesk.Revit.DB;
using DEB.Revit.Core;

namespace DEB.Revit.Presentation
{
    /// <summary>Tipos de cota e de texto definidos pelo padrão; criados (por duplicação) quando não existem no projeto.</summary>
    internal sealed class AnnotationStyles
    {
        public DimensionType Dimension { get; private set; }
        public TextNoteType Text { get; private set; }
        public TextNoteType FramedText { get; private set; }
        public TextNoteType Title { get; private set; }
        public TextNoteType ViewTitle { get; private set; }

        /// <summary>Precisa de transação.</summary>
        public static AnnotationStyles Ensure(Document doc, PresentationStandard s)
        {
            var st = new AnnotationStyles
            {
                Dimension = DimensionTypeFor(doc, s.Unidades),
                Text = TextType(doc, $"SD - Texto {s.Textos.AlturaTextoMm:0.#} mm", s.Textos.AlturaTextoMm, s.Textos.Fonte, false),
                FramedText = TextType(doc, $"SD - Texto {s.Textos.AlturaTextoMm:0.#} mm (moldura)", s.Textos.AlturaTextoMm, s.Textos.Fonte, true),
                Title = TextType(doc, $"SD - Título {s.Textos.AlturaTituloPecaMm:0.#} mm", s.Textos.AlturaTituloPecaMm, s.Textos.Fonte, false),
                ViewTitle = TextType(doc, $"SD - Título vista {s.Textos.AlturaTituloVistaMm:0.#} mm", s.Textos.AlturaTituloVistaMm, s.Textos.Fonte, false)
            };
            return st;
        }

        private static DimensionType DimensionTypeFor(Document doc, UnitSettings u)
        {
            DimensionType found = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>()
                .FirstOrDefault(t => t.Name == u.TipoDeCota);
            if (found != null) return found;

            DimensionType source = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>()
                .Where(t => t.StyleType == DimensionStyleType.Linear && t.GetSpecTypeId() == SpecTypeId.Length)
                .OrderBy(t => t.Name).FirstOrDefault()
                ?? new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>()
                    .First(t => t.StyleType == DimensionStyleType.Linear);

            var dt = (DimensionType)source.Duplicate(u.TipoDeCota);
            ForgeTypeId unit = u.Cotas == "mm" ? UnitTypeId.Millimeters : UnitTypeId.Centimeters;
            var fo = new FormatOptions(unit) { UseDefault = false, Accuracy = u.Precisao, SuppressTrailingZeros = true };
            dt.SetUnitsFormatOptions(fo);
            // texto movido para fora das linhas de chamada (trechos curtos da cota acumulada) ganha linha de chamada
            try { dt.get_Parameter(BuiltInParameter.DIM_LEADER)?.Set(1); } catch { /* tipo sem o parâmetro */ }
            return dt;
        }

        private static TextNoteType TextType(Document doc, string name, double heightMm, string font, bool border)
        {
            TextNoteType found = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
                .FirstOrDefault(t => t.Name == name);
            if (found != null) return found;

            TextNoteType source = doc.GetElement(doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType)) as TextNoteType
                                  ?? new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().First();
            var t = (TextNoteType)source.Duplicate(name);
            t.get_Parameter(BuiltInParameter.TEXT_SIZE)?.Set(U.Mm(heightMm));
            if (!string.IsNullOrWhiteSpace(font)) t.get_Parameter(BuiltInParameter.TEXT_FONT)?.Set(font);
            t.get_Parameter(BuiltInParameter.TEXT_BOX_VISIBILITY)?.Set(border ? 1 : 0);
            t.get_Parameter(BuiltInParameter.TEXT_BACKGROUND)?.Set(1);   // transparente
            return t;
        }
    }
}
