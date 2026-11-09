using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DEB.Core.Pecas;
using DEB.Revit.Core;

namespace DEB.Revit.Precast
{
    /// <summary>
    /// Levantamento para a conferência das peças (2026-11-09, etapa 2): vigas e pilares estruturais, cada um subdividido em
    /// trechos de seção constante. Estações nos vértices ao longo do eixo (viga) ou da altura (pilar); no meio de cada intervalo
    /// o sólido é cortado (<see cref="BooleanOperationsUtils.CutWithHalfSpace"/>) e a face no plano do corte é a seção exata
    /// (com furos). Só lê o modelo. Ver docs/04_arquitetura/conferencia_e_subdivisao_pecas.md.
    /// </summary>
    internal static class PieceReview
    {
        public static List<PecaLevantada> Run(Document doc, List<string> notes)
        {
            var r = new List<PecaLevantada>();
            foreach (BuiltInCategory cat in new[] { BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralColumns })
            {
                IEnumerable<FamilyInstance> els = new FilteredElementCollector(doc).OfCategory(cat)
                    .WhereElementIsNotElementType().OfType<FamilyInstance>().Where(fi => fi.SuperComponent == null);
                foreach (FamilyInstance fi in els)
                {
                    try { r.Add(Survey(fi)); }
                    catch (PieceNotSupportedException ex) { notes.Add($"{fi.Name} [{fi.Id.Value}] ignorado: {ex.Message}"); }
                    catch (Exception ex) { notes.Add($"{fi.Name} [{fi.Id.Value}]: erro no levantamento — {ex.Message}"); }
                }
            }
            return r;
        }

        public static PecaLevantada Survey(FamilyInstance fi)
        {
            PieceInfo info = PieceAnalyzer.Analyze(fi);
            bool beam = info.Kind == PieceKind.Beam;
            XYZ along = beam ? info.AxisX : info.AxisZ;
            XYZ u = beam ? info.AxisY : info.AxisX;   // horizontal da seção
            XYZ w = beam ? info.AxisZ : info.AxisY;   // vertical da seção
            double len = beam ? info.LengthX : info.LengthZ;

            var p = new PecaLevantada
            {
                Id = fi.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Tipo = beam ? TipoPeca.Viga : TipoPeca.Pilar,
                Marca = fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "",
                Familia = fi.Symbol?.FamilyName ?? "",
                TipoRevit = fi.Symbol?.Name ?? fi.Name,
                PreMoldada = fi.StructuralMaterialType == Autodesk.Revit.DB.Structure.StructuralMaterialType.PrecastConcrete,
                Comprimento = Cm(len),
            };
            ElementId matId = fi.StructuralMaterialId;
            p.Material = matId != null && matId != ElementId.InvalidElementId ? (fi.Document.GetElement(matId)?.Name ?? "") : "";

            // sólidos no sistema do projeto (refs guardadas até o fim: o Solid sem o GeometryElement dono quebra — ver CLAUDE.md)
            List<SolidRef> refs = GeometryHelper.GetSolids(fi, null);
            var solids = new List<Solid>();
            foreach (SolidRef sr in refs)
                solids.Add(sr.ToWorld.IsIdentity ? sr.Solid : SolidUtils.CreateTransformed(sr.Solid, sr.ToWorld));

            var xs = info.Vertices.Select(v => Cm((v - info.Origin).DotProduct(along)));
            var cortes = new List<(double, double, SecaoPoligonal)>();
            foreach (var (x0, x1, meio) in Subdivisao.Intervalos(xs, p.Comprimento))
            {
                SecaoPoligonal s = Cut(solids, info.Origin, along, u, w, U.Mm(meio * 10), out string aviso);
                if (aviso != null) p.Avisos.Add($"corte em {meio:0.#} cm: {aviso}");
                if (s != null) cortes.Add((x0, x1, s));
            }
            GC.KeepAlive(refs);
            p.Trechos = Subdivisao.Juntar(cortes);
            ConferenciaPecas.Avaliar(p);
            return p;
        }

        /// <summary>Seção no plano perpendicular a <paramref name="along"/> a <paramref name="t"/> (pés) da origem; null se não cortou.</summary>
        private static SecaoPoligonal Cut(List<Solid> solids, XYZ origin, XYZ along, XYZ u, XYZ w, double t, out string aviso)
        {
            aviso = null;
            XYZ po = origin + along * t;
            Plane plane = Plane.CreateByNormalAndOrigin(along, po);
            var loops = new List<List<(double Y, double Z)>>();
            foreach (Solid s in solids)
            {
                Solid cut;
                try { cut = BooleanOperationsUtils.CutWithHalfSpace(s, plane); }
                catch { continue; }
                if (cut == null || cut.Faces.Size == 0) continue;
                foreach (Face f in cut.Faces)
                {
                    if (!(f is PlanarFace pf) || !pf.FaceNormal.IsAlmostEqualTo(along.Negate())) continue;
                    if (Math.Abs((pf.Origin - po).DotProduct(along)) > 1e-6) continue;
                    foreach (CurveLoop cl in pf.GetEdgesAsCurveLoops())
                    {
                        var pts = new List<(double, double)>();
                        foreach (Curve c in cl)
                        {
                            IList<XYZ> tp = c.Tessellate();
                            for (int i = 0; i + 1 < tp.Count; i++)
                                pts.Add((Round(Cm((tp[i] - origin).DotProduct(u))), Round(Cm((tp[i] - origin).DotProduct(w)))));
                        }
                        if (pts.Count >= 3) loops.Add(pts);
                    }
                }
            }
            if (loops.Count == 0) { aviso = "o plano não cortou a peça"; return null; }

            // contorno = o maior; furos = os que estão dentro dele; o resto = outro pedaço (aviso)
            loops = loops.OrderByDescending(l => Math.Abs(Area(l))).ToList();
            var s0 = new SecaoPoligonal { Contorno = loops[0] };
            int pedacos = 0;
            foreach (var l in loops.Skip(1))
            {
                if (Dentro(l[0], loops[0])) s0.Furos.Add(l);
                else pedacos++;
            }
            if (pedacos > 0) aviso = $"seção em {pedacos + 1} pedaços (sólidos separados?); usado o maior";
            return s0;
        }

        private static double Area(List<(double Y, double Z)> p)
        {
            double a = 0;
            for (int i = 0; i < p.Count; i++) { var (y0, z0) = p[i]; var (y1, z1) = p[(i + 1) % p.Count]; a += y0 * z1 - y1 * z0; }
            return a / 2;
        }

        private static bool Dentro((double Y, double Z) q, List<(double Y, double Z)> poly)
        {
            bool c = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var (yi, zi) = poly[i];
                var (yj, zj) = poly[j];
                if ((zi > q.Z) != (zj > q.Z) && q.Y < (yj - yi) * (q.Z - zi) / (zj - zi) + yi) c = !c;
            }
            return c;
        }

        private static double Cm(double ft) => U.ToMm(ft) / 10.0;
        private static double Round(double cm) => Math.Round(cm, 2);
    }
}
