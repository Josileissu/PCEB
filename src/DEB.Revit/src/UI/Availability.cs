using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using StructuralDetailer.Core;

namespace StructuralDetailer.UI
{
    /// <summary>Habilita o botão apenas com um projeto aberto (não em famílias).</summary>
    public class ProjectOnlyAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            UIDocument uidoc = applicationData.ActiveUIDocument;
            return uidoc != null && !uidoc.Document.IsFamilyDocument;
        }
    }

    /// <summary>Permite selecionar só vigas e pilares estruturais.</summary>
    internal sealed class StructuralPieceFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => PieceAnalyzer.IsSupported(elem);
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
