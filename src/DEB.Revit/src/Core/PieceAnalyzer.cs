using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StructuralDetailer.Core
{
    public enum PieceKind { Beam, Column }

    internal sealed class PieceNotSupportedException : Exception
    {
        public PieceNotSupportedException(string message) : base(message) { }
    }

    /// <summary>
    /// Peça analisada, com sistema local:
    ///   Viga  → X = eixo da viga, Z = "para cima" (perpendicular ao eixo), Y = Z × X
    ///   Pilar → Z = eixo do pilar, X = HandOrientation da família, Y = Z × X
    /// Origin é o canto mínimo da caixa envolvente no sistema local.
    /// </summary>
    internal sealed class PieceInfo
    {
        public FamilyInstance Instance { get; set; }
        public PieceKind Kind { get; set; }
        public string Mark { get; set; }

        public XYZ AxisX { get; set; }
        public XYZ AxisY { get; set; }
        public XYZ AxisZ { get; set; }
        public XYZ Origin { get; set; }

        public double LengthX { get; set; }
        public double LengthY { get; set; }
        public double LengthZ { get; set; }

        public List<XYZ> Vertices { get; set; }

        /// <summary>Pilar: alturas locais (a partir da base) dos cortes transversais. null = meia altura.</summary>
        public List<double> SectionHeights { get; set; }

        /// <summary>Elementos anexados à peça (ex.: consolos) que recebem cotas próprias.</summary>
        public List<FamilyInstance> Attached { get; set; } = new List<FamilyInstance>();

        public string KindLabel => Kind == PieceKind.Beam ? "Viga" : "Pilar";

        public string Description => Kind == PieceKind.Beam
            ? $"L = {U.M(LengthX)} m, seção {U.Cm(LengthY)} × {U.Cm(LengthZ)} cm"
            : $"H = {U.M(LengthZ)} m, seção {U.Cm(LengthX)} × {U.Cm(LengthY)} cm";

        public Transform ToTransform()
        {
            Transform t = Transform.Identity;
            t.Origin = Origin;
            t.BasisX = AxisX;
            t.BasisY = AxisY;
            t.BasisZ = AxisZ;
            return t;
        }

        public double MaxAlong(XYZ dir) => Vertices.Max(v => v.DotProduct(dir));
    }

    internal static class PieceAnalyzer
    {
        public static bool IsSupported(Element e)
        {
            if (!(e is FamilyInstance fi) || fi.Category == null) return false;
            BuiltInCategory bic = fi.Category.BuiltInCategory;
            return bic == BuiltInCategory.OST_StructuralFraming || bic == BuiltInCategory.OST_StructuralColumns;
        }

        public static PieceInfo Analyze(Element element)
        {
            if (!(element is FamilyInstance fi) || !IsSupported(fi))
                throw new PieceNotSupportedException("O elemento não é uma viga (Structural Framing) nem um pilar (Structural Column).");

            PieceKind kind;
            XYZ x, y, z;

            if (fi.Category.BuiltInCategory == BuiltInCategory.OST_StructuralFraming)
            {
                kind = PieceKind.Beam;
                if (!(fi.Location is LocationCurve lc) || !(lc.Curve is Line line))
                    throw new PieceNotSupportedException("Viga sem eixo reto. Vigas curvas ainda não são suportadas.");

                x = line.Direction.Normalize();
                if (Math.Abs(x.Z) > 0.999)
                    throw new PieceNotSupportedException("Elemento de framing vertical. Modele como pilar.");

                z = (XYZ.BasisZ - x * x.Z).Normalize();   // "para cima", perpendicular ao eixo (vigas inclinadas)
                y = z.CrossProduct(x).Normalize();
            }
            else
            {
                kind = PieceKind.Column;
                if (fi.Location is LocationCurve lc2 && lc2.Curve is Line axis)
                {
                    z = axis.Direction.Normalize();
                    if (z.Z < 0) z = z.Negate();
                }
                else
                {
                    z = XYZ.BasisZ;
                }

                XYZ hand = fi.HandOrientation;
                XYZ h = hand - z * hand.DotProduct(z);
                if (h.GetLength() < 1e-6) h = XYZ.BasisX - z * z.X;
                if (h.GetLength() < 1e-6) h = XYZ.BasisY - z * z.Y;
                x = h.Normalize();
                y = z.CrossProduct(x).Normalize();
            }

            List<SolidRef> solids = GeometryHelper.GetSolids(fi, null);
            List<XYZ> verts = GeometryHelper.GetVertices(solids);
            if (verts.Count == 0)
                throw new PieceNotSupportedException("A peça não tem geometria sólida.");

            double minX = verts.Min(p => p.DotProduct(x)), maxX = verts.Max(p => p.DotProduct(x));
            double minY = verts.Min(p => p.DotProduct(y)), maxY = verts.Max(p => p.DotProduct(y));
            double minZ = verts.Min(p => p.DotProduct(z)), maxZ = verts.Max(p => p.DotProduct(z));

            return new PieceInfo
            {
                Instance = fi,
                Kind = kind,
                Mark = ReadMark(fi, kind),
                AxisX = x,
                AxisY = y,
                AxisZ = z,
                Origin = x * minX + y * minY + z * minZ,
                LengthX = maxX - minX,
                LengthY = maxY - minY,
                LengthZ = maxZ - minZ,
                Vertices = verts
            };
        }

        private static string ReadMark(FamilyInstance fi, PieceKind kind)
        {
            string mark = fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
            if (string.IsNullOrWhiteSpace(mark))
                mark = (kind == PieceKind.Beam ? "V" : "P") + "-" + fi.Id.Value;
            return NameRegistry.Sanitize(mark);
        }
    }
}
