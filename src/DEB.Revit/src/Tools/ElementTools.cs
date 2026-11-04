using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DEB.Revit.Tools
{
    /// <summary>Coleta e geometria comuns às ferramentas.</summary>
    internal static class ElementTools
    {
        public const double FeetPerMeter = 1 / 0.3048;

        /// <summary>
        /// Elementos da seleção atual que passam no filtro, entrando em grupos (inclusive aninhados) e sem repetir.
        /// </summary>
        public static List<Element> FromSelection(UIDocument uidoc, Func<Element, bool> accept)
        {
            Document doc = uidoc.Document;
            var result = new List<Element>();
            var seen = new HashSet<ElementId>();
            void Visit(Element el)
            {
                if (el == null || !seen.Add(el.Id)) return;
                if (el is Group g) { foreach (ElementId id in g.GetMemberIds()) Visit(doc.GetElement(id)); return; }
                if (accept(el)) result.Add(el);
            }
            foreach (ElementId id in uidoc.Selection.GetElementIds()) Visit(doc.GetElement(id));
            return result;
        }

        public static bool IsCategory(Element el, BuiltInCategory cat) => el?.Category?.BuiltInCategory == cat;

        public static Parameter Mark(Element el) => el.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);

        public static string MarkText(Element el) => Mark(el)?.AsString() ?? "";

        private static Options FineGeometry() => new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = true };

        /// <summary>
        /// Referência do croqui: maior Y e menor X das arestas dos sólidos (canto superior esquerdo em planta).
        /// Lido na hora (nenhum sólido é guardado — ver armadilhas do CLAUDE.md).
        /// </summary>
        public static bool TryTopLeft(Element el, out double x, out double y)
        {
            double yMax = double.MinValue, xMin = double.MaxValue;
            bool found = false;
            foreach (Solid s in Solids(el.get_Geometry(FineGeometry())))
                foreach (Edge e in s.Edges)
                {
                    Curve c = e.AsCurve();
                    XYZ p0 = c.GetEndPoint(0), p1 = c.GetEndPoint(1);
                    yMax = Math.Max(yMax, Math.Max(p0.Y, p1.Y));
                    xMin = Math.Min(xMin, Math.Min(p0.X, p1.X));
                    found = true;
                }
            x = xMin; y = yMax;
            return found;
        }

        /// <summary>
        /// Caixa em planta do contorno externo das faces superiores horizontais (lajes); sem face assim (laje inclinada),
        /// a caixa do elemento.
        /// </summary>
        public static bool TryTopFaceBounds(Element el, out (double XMin, double XMax, double YMin, double YMax) box)
        {
            double xMin = double.MaxValue, xMax = double.MinValue, yMin = double.MaxValue, yMax = double.MinValue;
            bool found = false;
            foreach (Solid s in Solids(el.get_Geometry(FineGeometry())))
                foreach (Face f in s.Faces)
                {
                    if (f is not PlanarFace pf || pf.FaceNormal.Z < 0.99) continue;
                    IList<CurveLoop> loops = pf.GetEdgesAsCurveLoops();
                    if (loops == null || loops.Count == 0) continue;
                    CurveLoop outer = loops.OrderByDescending(l => l.GetExactLength()).First();
                    foreach (Curve c in outer)
                        foreach (XYZ p in c is Line ? new[] { c.GetEndPoint(0), c.GetEndPoint(1) } : c.Tessellate())
                        {
                            xMin = Math.Min(xMin, p.X); xMax = Math.Max(xMax, p.X);
                            yMin = Math.Min(yMin, p.Y); yMax = Math.Max(yMax, p.Y);
                        }
                    found = true;
                }
            if (!found)
            {
                BoundingBoxXYZ bb = el.get_BoundingBox(null);
                if (bb != null) { xMin = bb.Min.X; xMax = bb.Max.X; yMin = bb.Min.Y; yMax = bb.Max.Y; found = true; }
            }
            box = (xMin, xMax, yMin, yMax);
            return found;
        }

        /// <summary>Extremos da linha de locação (vigas, paredes).</summary>
        public static bool TryAxis(Element el, out ((double X, double Y, double Z) A, (double X, double Y, double Z) B) axis)
        {
            axis = default;
            if (el.Location is not LocationCurve lc || lc.Curve == null) return false;
            XYZ a = lc.Curve.GetEndPoint(0), b = lc.Curve.GetEndPoint(1);
            axis = ((a.X, a.Y, a.Z), (b.X, b.Y, b.Z));
            return true;
        }

        private static IEnumerable<Solid> Solids(GeometryElement geom)
        {
            if (geom == null) yield break;
            foreach (GeometryObject go in geom)
            {
                if (go is Solid s && s.Volume > 0) yield return s;
                else if (go is GeometryInstance gi)
                    foreach (Solid si in Solids(gi.GetInstanceGeometry())) yield return si;
            }
        }
    }
}
