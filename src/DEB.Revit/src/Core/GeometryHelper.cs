using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace DEB.Revit.Core
{
    /// <summary>Sólido da peça + transformação para coordenadas do projeto.</summary>
    internal sealed class SolidRef
    {
        public Solid Solid { get; }
        public Transform ToWorld { get; }
        /// <summary>0 = geometria direta da instância; 1 = geometria do símbolo; 2+ = família aninhada.</summary>
        public int Depth { get; }

        /// <summary>
        /// GeometryElements de onde o sólido saiu. Precisam continuar vivos: se o GC os coletar,
        /// a memória nativa é liberada e acessar Solid.Faces gera SEHException.
        /// </summary>
        private readonly IReadOnlyList<GeometryElement> _owners;

        public SolidRef(Solid solid, Transform toWorld, int depth, IReadOnlyList<GeometryElement> owners)
        {
            Solid = solid; ToWorld = toWorld; Depth = depth; _owners = owners;
        }
    }

    internal static class GeometryHelper
    {
        /// <summary>
        /// Lê os sólidos do elemento com referências calculadas (necessárias para cotas).
        /// Para instâncias não cortadas, usa a geometria do símbolo: suas referências são
        /// as que o Revit aceita em NewDimension.
        /// </summary>
        public static List<SolidRef> GetSolids(Element element, View view)
        {
            var opt = new Options { ComputeReferences = true, IncludeNonVisibleObjects = false };
            if (view != null) opt.View = view;
            else opt.DetailLevel = ViewDetailLevel.Fine;

            var list = new List<SolidRef>();
            var owners = new List<GeometryElement>();
            Collect(element.get_Geometry(opt), Transform.Identity, 0, list, owners);
            return list;
        }

        private static void Collect(GeometryElement ge, Transform toWorld, int depth,
            List<SolidRef> list, List<GeometryElement> owners)
        {
            if (ge == null) return;
            owners.Add(ge);
            foreach (GeometryObject go in ge)
            {
                if (go is Solid s && s.Faces.Size > 0 && s.Volume > 1e-9)
                {
                    list.Add(new SolidRef(s, toWorld, depth, owners));
                }
                else if (go is GeometryInstance gi)
                {
                    Collect(gi.GetSymbolGeometry(), toWorld.Multiply(gi.Transform), depth + 1, list, owners);
                }
            }
        }

        /// <summary>Pontos das arestas em coordenadas do projeto (para extensões e offsets).</summary>
        public static List<XYZ> GetVertices(IEnumerable<SolidRef> solids)
        {
            var pts = new List<XYZ>();
            foreach (SolidRef sr in solids)
            {
                foreach (Edge e in sr.Solid.Edges)
                {
                    foreach (XYZ p in e.Tessellate())
                        pts.Add(sr.ToWorld.OfPoint(p));
                }
            }
            return pts;
        }

        /// <summary>
        /// Encontra as duas faces planas extremas perpendiculares à direção <paramref name="dir"/>
        /// (ex.: as duas faces de topo de uma viga para cotar o comprimento total).
        /// </summary>
        public static bool TryGetExtremeFaces(IList<SolidRef> solids, XYZ dir,
            out Reference refMin, out double tMin, out Reference refMax, out double tMax)
        {
            refMin = null; refMax = null;
            tMin = double.MaxValue; tMax = double.MinValue;
            const double tol = 1e-6;

            foreach (SolidRef sr in solids.OrderBy(s => s.Depth))
            {
                foreach (Face f in sr.Solid.Faces)
                {
                    if (!(f is PlanarFace pf) || pf.Reference == null) continue;

                    XYZ n = sr.ToWorld.OfVector(pf.FaceNormal).Normalize();
                    if (Math.Abs(n.DotProduct(dir)) < 0.9999) continue;

                    double t = sr.ToWorld.OfPoint(pf.Origin).DotProduct(dir);
                    if (t < tMin - tol) { tMin = t; refMin = pf.Reference; }
                    if (t > tMax + tol) { tMax = t; refMax = pf.Reference; }
                }
            }

            return refMin != null && refMax != null && (tMax - tMin) > U.Mm(1);
        }

        /// <summary>
        /// Referências cotáveis ao longo de <paramref name="dir"/> numa vista de direção <paramref name="viewDir"/>:
        /// faces planas perpendiculares a dir e arestas retas perpendiculares a dir contidas no plano da vista
        /// (necessárias em peças com faces inclinadas, como o fundo de um consolo trapezoidal).
        /// Uma referência por posição (tolerância de 1 mm), em ordem crescente.
        /// </summary>
        public static List<(double T, Reference Ref)> RefsAlong(IList<SolidRef> solids, XYZ dir, XYZ viewDir)
        {
            var found = new List<(double T, Reference Ref, int Rank)>();
            XYZ edgeDir = dir.CrossProduct(viewDir);
            bool edgesOk = !edgeDir.IsZeroLength();
            if (edgesOk) edgeDir = edgeDir.Normalize();

            foreach (SolidRef sr in solids.OrderBy(s => s.Depth))
            {
                foreach (Face f in sr.Solid.Faces)
                {
                    if (!(f is PlanarFace pf) || pf.Reference == null) continue;
                    XYZ n = sr.ToWorld.OfVector(pf.FaceNormal).Normalize();
                    if (Math.Abs(n.DotProduct(dir)) < 0.9999) continue;
                    found.Add((sr.ToWorld.OfPoint(pf.Origin).DotProduct(dir), pf.Reference, 0));
                }
                if (!edgesOk) continue;
                foreach (Edge e in sr.Solid.Edges)
                {
                    if (e.Reference == null || !(e.AsCurve() is Line ln)) continue;
                    XYZ d = sr.ToWorld.OfVector(ln.Direction).Normalize();
                    if (Math.Abs(d.DotProduct(edgeDir)) < 0.9999) continue;
                    found.Add((sr.ToWorld.OfPoint(ln.GetEndPoint(0)).DotProduct(dir), e.Reference, 1));
                }
            }

            // Prefere face a aresta na mesma posição.
            double tol = U.Mm(1);
            var result = new List<(double, Reference)>();
            foreach (var g in found.OrderBy(x => x.T).ThenBy(x => x.Rank))
            {
                if (result.Count > 0 && Math.Abs(result[^1].Item1 - g.T) < tol) continue;
                result.Add((g.T, g.Ref));
            }
            return result;
        }
    }
}
