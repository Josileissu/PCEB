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
            // consolos de cada pilar: o mesmo reconhecimento do botão "Pilares pré-moldados" (famílias separadas ou aninhadas na viga)
            var consolos = new Dictionary<long, List<FamilyInstance>>();
            try
            {
                foreach (ColumnGroup g in PrecastSurvey.Run(doc, out List<string> _))
                    foreach (PrecastColumn m in g.Members)
                        consolos[m.Instance.Id.Value] = m.Corbels.Select(c => c.Instance).Where(c => c != null).ToList();
            }
            catch (Exception ex) { notes.Add("consolos dos pilares não reconhecidos: " + ex.Message); }
            foreach (BuiltInCategory cat in new[] { BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralColumns })
            {
                IEnumerable<FamilyInstance> els = new FilteredElementCollector(doc).OfCategory(cat)
                    .WhereElementIsNotElementType().OfType<FamilyInstance>().Where(fi => fi.SuperComponent == null);
                foreach (FamilyInstance fi in els)
                {
                    try { r.Add(Survey(fi, consolos.TryGetValue(fi.Id.Value, out var cs) ? cs : new List<FamilyInstance>())); }
                    catch (PieceNotSupportedException ex) { notes.Add($"{fi.Name} [{fi.Id.Value}] ignorado: {ex.Message}"); }
                    catch (Exception ex) { notes.Add($"{fi.Name} [{fi.Id.Value}]: erro no levantamento — {ex.Message}"); }
                }
            }
            return r;
        }

        public static PecaLevantada Survey(FamilyInstance fi, List<FamilyInstance> corbels)
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

            var xs = info.Vertices.Select(v => Cm((v - info.Origin).DotProduct(along))).ToList();
            var corbelRefs = new List<List<SolidRef>>();
            if (!beam)
            {
                p.Bx = Cm(info.LengthX);
                p.By = Cm(info.LengthY);
                foreach (FamilyInstance c in corbels)
                {
                    List<SolidRef> cr = GeometryHelper.GetSolids(c, null);
                    corbelRefs.Add(cr);
                    var cs = cr.Select(sr => sr.ToWorld.IsIdentity ? sr.Solid : SolidUtils.CreateTransformed(sr.Solid, sr.ToWorld)).ToList();
                    if (cs.Count == 0) continue;
                    ConsoloLevantado k = Corbel(info, cs, out string aviso);
                    if (aviso != null) p.Avisos.Add($"consolo {c.Id.Value}: {aviso}");
                    if (k == null) continue;
                    p.Consolos.Add(k);
                    xs.AddRange(GeometryHelper.GetVertices(cr).Select(v => Cm((v - info.Origin).DotProduct(along))));
                    // o corte horizontal passa pelo pilar e pelo consolo juntos
                    foreach (Solid s in cs)
                    {
                        try { solids[0] = BooleanOperationsUtils.ExecuteBooleanOperation(solids[0], s, BooleanOperationsType.Union); }
                        catch { solids.Add(s); p.Avisos.Add($"consolo {c.Id.Value}: não foi possível unir ao pilar no corte"); }
                    }
                }
            }
            var cortes = new List<(double, double, SecaoPoligonal)>();
            foreach (var (x0, x1, meio) in Subdivisao.Intervalos(xs, p.Comprimento))
            {
                SecaoPoligonal s = Cut(solids, info.Origin, along, u, w, U.Mm(meio * 10), out string aviso);
                if (aviso != null) p.Avisos.Add($"corte em {meio:0.#} cm: {aviso}");
                if (s != null) cortes.Add((x0, x1, s));
            }
            GC.KeepAlive(refs);
            GC.KeepAlive(corbelRefs);
            p.Trechos = Subdivisao.Juntar(cortes, p.Tipo);
            ConferenciaPecas.Avaliar(p);
            return p;
        }

        /// <summary>
        /// Consolo no sistema do pilar: face pelo lado em que encosta (1 = +x, 2 = −y, 3 = −x, 4 = +y), trecho ao longo da face e
        /// o perfil cortado no meio da largura (distância para fora × altura a partir da base), com os chanfros.
        /// </summary>
        private static ConsoloLevantado Corbel(PieceInfo info, List<Solid> solids, out string aviso)
        {
            aviso = null;
            double tol = U.Mm(5);
            var v = new List<XYZ>();
            foreach (Solid s in solids) foreach (Edge e in s.Edges) v.AddRange(e.Tessellate());
            double X(XYZ q) => (q - info.Origin).DotProduct(info.AxisX);
            double Y(XYZ q) => (q - info.Origin).DotProduct(info.AxisY);
            double x0 = v.Min(X), x1 = v.Max(X), y0 = v.Min(Y), y1 = v.Max(Y);
            int face; XYZ fp, tangent, outward; double s0, s1;
            if (Math.Abs(x0 - info.LengthX) <= tol) { face = 1; fp = info.Origin + info.AxisX * info.LengthX; tangent = info.AxisY; outward = info.AxisX; s0 = y0; s1 = y1; }
            else if (Math.Abs(y1) <= tol) { face = 2; fp = info.Origin; tangent = info.AxisX; outward = info.AxisY.Negate(); s0 = x0; s1 = x1; }
            else if (Math.Abs(x1) <= tol) { face = 3; fp = info.Origin; tangent = info.AxisY; outward = info.AxisX.Negate(); s0 = y0; s1 = y1; }
            else if (Math.Abs(y0 - info.LengthY) <= tol) { face = 4; fp = info.Origin + info.AxisY * info.LengthY; tangent = info.AxisX; outward = info.AxisY; s0 = x0; s1 = x1; }
            else { aviso = "não encosta numa face do pilar"; return null; }
            SecaoPoligonal perfil = Cut(solids, fp, tangent, outward, info.AxisZ, (s0 + s1) / 2, out string a2);
            if (perfil == null) { aviso = a2 ?? "perfil não cortado"; return null; }
            return new ConsoloLevantado { Face = face, S0 = Round(Cm(s0)), S1 = Round(Cm(s1)), Perfil = perfil };
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
