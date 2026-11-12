using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DEB.Core.Lajes;

namespace DEB.Revit.LajeAlveolar
{
    /// <summary>Utilidades de geometria do Revit para a paginação de lajes alveolares (unidades: pés no Revit, metros no núcleo).</summary>
    internal static class GeometriaRevit
    {
        public const double Pe = 0.3048;
        public static readonly Options Fino = new Options { DetailLevel = ViewDetailLevel.Fine };

        public static double M(double pes) => pes * Pe;
        /// <summary>Metros arredondados a 0,1 mm, para as coordenadas lidas do modelo (como o protótipo). Sem isso, no teste com um modelo
        /// real, recortes de pilar com borda a frações de 0,1 mm não cortavam o painel (NewOpening sem efeito).</summary>
        public static double M4(double pes) => Math.Round(pes * Pe, 4, MidpointRounding.AwayFromZero);
        public static double Ft(double m) => m / Pe;
        public static XYZ P(double xm, double ym, double zm) => new XYZ(xm / Pe, ym / Pe, zm / Pe);

        /// <summary>Sólidos do elemento (inclusive de instâncias). A lista de GeometryElement devolvida segura a memória nativa
        /// dos sólidos — mantenha-a viva enquanto usar os sólidos (armadilha do CLAUDE.md: SEHException).</summary>
        public static List<Solid> Solidos(Element e, out List<GeometryElement> donos)
        {
            donos = new List<GeometryElement>();
            var r = new List<Solid>();
            var ge = e.get_Geometry(Fino);
            if (ge == null) return r;
            donos.Add(ge);
            foreach (var g in ge)
            {
                if (g is Solid s && s.Volume > 1e-9) r.Add(s);
                else if (g is GeometryInstance gi)
                {
                    var ig = gi.GetInstanceGeometry();
                    donos.Add(ig);
                    foreach (var o in ig) if (o is Solid s1 && s1.Volume > 1e-9) r.Add(s1);
                }
            }
            return r;
        }

        /// <summary>Trechos de uma linha dentro dos sólidos, como (t0, t1) em pés ao longo de <paramref name="dir"/> a partir de <paramref name="origem"/>.</summary>
        public static List<(double A, double B)> TrechosDentro(IEnumerable<Solid> solidos, XYZ origem, XYZ dir, double meioComprimentoPes)
        {
            var r = new List<(double, double)>();
            Line ln;
            try { ln = Line.CreateBound(origem - dir * meioComprimentoPes, origem + dir * meioComprimentoPes); }
            catch { return r; }
            var opt = new SolidCurveIntersectionOptions { ResultType = SolidCurveIntersectionMode.CurveSegmentsInside };
            foreach (var s in solidos)
            {
                try
                {
                    var res = s.IntersectWithCurve(ln, opt);
                    for (int k = 0; k < res.SegmentCount; k++)
                    {
                        var sg = res.GetCurveSegment(k);
                        double a = (sg.GetEndPoint(0) - origem).DotProduct(dir), b = (sg.GetEndPoint(1) - origem).DotProduct(dir);
                        r.Add((Math.Min(a, b), Math.Max(a, b)));
                    }
                }
                catch { /* sólido que a API não corta: ignora */ }
            }
            return r.OrderBy(t => t.Item1).ToList();
        }

        /// <summary>Volume total (m³).</summary>
        public static double Volume(Element e)
        {
            var ss = Solidos(e, out var donos);
            double v = ss.Sum(s => s.Volume) * Pe * Pe * Pe;
            GC.KeepAlive(donos);
            return v;
        }

        /// <summary>Caixa do elemento no sistema do pano: (u0, v0, u1, v1) em metros e o topo z (m).</summary>
        public static (RetUV Ret, double ZMin, double ZMax) CaixaNoPano(Element e, SistemaPano s)
        {
            double u0 = double.MaxValue, v0 = double.MaxValue, u1 = double.MinValue, v1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
            var ss = Solidos(e, out var donos);
            foreach (var so in ss)
                foreach (Edge ed in so.Edges)
                    foreach (var q in ed.Tessellate())
                    {
                        var p = s.UV(M(q.X), M(q.Y));
                        u0 = Math.Min(u0, p.U); v0 = Math.Min(v0, p.V); u1 = Math.Max(u1, p.U); v1 = Math.Max(v1, p.V);
                        z0 = Math.Min(z0, M(q.Z)); z1 = Math.Max(z1, M(q.Z));
                    }
            GC.KeepAlive(donos);
            return (new RetUV(u0, v0, u1, v1), z0, z1);
        }

        /// <summary>Perfil retangular horizontal (pés) dos 4 cantos de um retângulo do pano, na cota z (m).</summary>
        public static CurveArray PerfilRet(SistemaPano s, RetUV r, double zm)
        {
            var c = s.Cantos(r);
            var ca = new CurveArray();
            for (int i = 0; i < 4; i++)
            {
                var a = P(c[i].X, c[i].Y, zm); var b = P(c[(i + 1) % 4].X, c[(i + 1) % 4].Y, zm);
                ca.Append(Line.CreateBound(a, b));
            }
            return ca;
        }

        public static CurveLoop LacoRet(SistemaPano s, RetUV r, double zm)
        {
            var c = s.Cantos(r);
            var cl = new CurveLoop();
            for (int i = 0; i < 4; i++) cl.Append(Line.CreateBound(P(c[i].X, c[i].Y, zm), P(c[(i + 1) % 4].X, c[(i + 1) % 4].Y, zm)));
            return cl;
        }
    }
}
