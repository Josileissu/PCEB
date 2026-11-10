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
            try { BeamCorbels(doc, consolos); }
            catch (Exception ex) { notes.Add("consolos das vigas não reconhecidos: " + ex.Message); }
            // níveis: a cota mostrada (Elevation) e a altura geométrica (ProjectElevation, mesmo referencial das coordenadas)
            List<Level> levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.ProjectElevation).ToList();
            foreach (BuiltInCategory cat in new[] { BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralColumns })
            {
                IEnumerable<FamilyInstance> els = new FilteredElementCollector(doc).OfCategory(cat)
                    .WhereElementIsNotElementType().OfType<FamilyInstance>().Where(fi => fi.SuperComponent == null);
                foreach (FamilyInstance fi in els)
                {
                    try { r.Add(Survey(fi, consolos.TryGetValue(fi.Id.Value, out var cs) ? cs : new List<FamilyInstance>(), levels)); }
                    catch (PieceNotSupportedException ex) { notes.Add($"{fi.Name} [{fi.Id.Value}] ignorado: {ex.Message}"); }
                    catch (Exception ex) { notes.Add($"{fi.Name} [{fi.Id.Value}]: erro no levantamento — {ex.Message}"); }
                }
            }
            return r;
        }

        /// <summary>
        /// Consolos nas vigas (viga apoiada em viga, 2026-11-09): o consolo é uma família aninhada na viga que SE APOIA, mas fica
        /// encostado na face lateral da viga que RECEBE (modelo real, V245). Cada consolo ainda sem dono (os dos pilares já foram
        /// atribuídos) vai para a viga cuja face lateral ele toca, dentro do comprimento e da altura dela, saindo para fora.
        /// </summary>
        private static void BeamCorbels(Document doc, Dictionary<long, List<FamilyInstance>> owner)
        {
            _faceOf.Clear();
            var used = new HashSet<long>(owner.Values.SelectMany(l => l).Select(c => c.Id.Value));
            var beams = new List<(FamilyInstance Fi, PieceInfo Info, BoundingBoxXYZ Bb, List<LateralFace> Faces)>();
            foreach (FamilyInstance b in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming)
                         .WhereElementIsNotElementType().OfType<FamilyInstance>().Where(fi => fi.SuperComponent == null))
            {
                try { var info = PieceAnalyzer.Analyze(b); beams.Add((b, info, b.get_BoundingBox(null), LateralFaces(b, info))); }
                catch { /* sem eixo reto etc.: fica de fora */ }
            }
            double tol = U.Mm(5), folga = U.Mm(30);
            foreach (FamilyInstance c in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                         .Where(fi => fi.Category != null && !used.Contains(fi.Id.Value) &&
                                      (fi.Category.BuiltInCategory == BuiltInCategory.OST_StructConnections ||
                                       fi.Category.BuiltInCategory == BuiltInCategory.OST_GenericModel)))
            {
                BoundingBoxXYZ cb = c.get_BoundingBox(null);
                if (cb == null) continue;
                List<XYZ> verts = null;
                foreach (var (b, info, bb, faces) in beams)
                {
                    if (bb == null || cb.Min.X > bb.Max.X + folga || cb.Max.X < bb.Min.X - folga || cb.Min.Y > bb.Max.Y + folga ||
                        cb.Max.Y < bb.Min.Y - folga || cb.Min.Z > bb.Max.Z + folga || cb.Max.Z < bb.Min.Z - folga) continue;
                    verts ??= GeometryHelper.GetVertices(GeometryHelper.GetSolids(c, null));
                    if (verts.Count == 0) break;
                    double X(XYZ q) => (q - info.Origin).DotProduct(info.AxisX);
                    double Y(XYZ q) => (q - info.Origin).DotProduct(info.AxisY);
                    double Z(XYZ q) => (q - info.Origin).DotProduct(info.AxisZ);
                    double x0 = verts.Min(X), x1 = verts.Max(X), y0 = verts.Min(Y), y1 = verts.Max(Y), z0 = verts.Min(Z), z1 = verts.Max(Z);
                    // encosta numa face lateral REAL da viga (a da alma, dentro das abas, também) e sai para fora dela
                    double min = U.Mm(10);
                    LateralFace f = faces.FirstOrDefault(q =>
                        (q.Sign > 0 ? Math.Abs(y0 - q.Y) <= tol && y1 - q.Y > U.Mm(20) : Math.Abs(y1 - q.Y) <= tol && q.Y - y0 > U.Mm(20)) &&
                        Math.Min(x1, q.X1) - Math.Max(x0, q.X0) > min && Math.Min(z1, q.Z1) - Math.Max(z0, q.Z0) > min);
                    if (f == null) continue;
                    _faceOf[c.Id.Value] = (f.Y, f.Sign);
                    if (!owner.TryGetValue(b.Id.Value, out var l)) owner[b.Id.Value] = l = new List<FamilyInstance>();
                    l.Add(c);
                    break;
                }
            }
        }

        /// <summary>Face lateral plana da viga (normal ±y do sistema da viga): posição y, sentido e extensão em x e z (pés).</summary>
        private sealed class LateralFace { public double Y, X0, X1, Z0, Z1; public int Sign; }

        /// <summary>Consolo de viga → face em que encosta (y e sentido), do reconhecimento para o perfil.</summary>
        private static readonly Dictionary<long, (double Y, int Sign)> _faceOf = new Dictionary<long, (double, int)>();

        private static List<LateralFace> LateralFaces(FamilyInstance b, PieceInfo info)
        {
            var r = new List<LateralFace>();
            List<SolidRef> refs = GeometryHelper.GetSolids(b, null);
            foreach (SolidRef sr in refs)
                foreach (Face f in sr.Solid.Faces)
                {
                    if (!(f is PlanarFace pf)) continue;
                    XYZ n = sr.ToWorld.OfVector(pf.FaceNormal);
                    double dy = n.DotProduct(info.AxisY);
                    if (Math.Abs(dy) < 0.999) continue;
                    var pts = new List<XYZ>();
                    foreach (EdgeArray loop in pf.EdgeLoops) foreach (Edge e in loop) foreach (XYZ q in e.Tessellate()) pts.Add(sr.ToWorld.OfPoint(q) - info.Origin);
                    if (pts.Count == 0) continue;
                    r.Add(new LateralFace
                    {
                        Y = pts.Average(q => q.DotProduct(info.AxisY)), Sign = dy > 0 ? 1 : -1,
                        X0 = pts.Min(q => q.DotProduct(info.AxisX)), X1 = pts.Max(q => q.DotProduct(info.AxisX)),
                        Z0 = pts.Min(q => q.DotProduct(info.AxisZ)), Z1 = pts.Max(q => q.DotProduct(info.AxisZ)),
                    });
                }
            GC.KeepAlive(refs);
            return r;
        }

        /// <param name="levels">Níveis do modelo por altura (para a cota da base e os níveis que cortam o pilar); null = não informar.</param>
        public static PecaLevantada Survey(FamilyInstance fi, List<FamilyInstance> corbels, List<Level> levels = null)
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
                Uid = fi.UniqueId ?? "",
                NaoExportar = PieceReviewEdits.IsSkipped(fi),
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
                // dados que o detalhamento do CAD usa: embutimento no cálice (parâmetro da família), cota da base e níveis
                Parameter emb = fi.LookupParameter("Embutimento");
                if (emb != null && emb.StorageType == StorageType.Double) p.Embutimento = Round(Cm(emb.AsDouble()));
                if (levels != null)
                {
                    double zBase = info.Origin.Z, zTop = (info.Origin + info.AxisZ * info.LengthZ).Z, tol = U.Mm(10);
                    if (zTop < zBase) (zBase, zTop) = (zTop, zBase);
                    double offset = levels.Count > 0 ? levels[0].ProjectElevation - levels[0].Elevation : 0;
                    p.CotaBase = Math.Round(U.ToMm(zBase - offset) / 1000.0, 4);
                    p.Niveis = levels.Where(l => l.ProjectElevation > zBase + tol && l.ProjectElevation < zTop - tol)
                        .Select(l => (l.Name, Math.Round(U.ToMm(l.Elevation) / 1000.0, 4))).ToList();
                }
            }
            {
                foreach (FamilyInstance c in corbels)
                {
                    List<SolidRef> cr = GeometryHelper.GetSolids(c, null);
                    corbelRefs.Add(cr);
                    var cs = cr.Select(sr => sr.ToWorld.IsIdentity ? sr.Solid : SolidUtils.CreateTransformed(sr.Solid, sr.ToWorld)).ToList();
                    if (cs.Count == 0) continue;
                    ConsoloLevantado k = beam ? BeamCorbel(info, cs, _faceOf.TryGetValue(c.Id.Value, out var fy) ? fy : ((double, int)?)null, out string aviso)
                                              : Corbel(info, cs, out aviso);
                    if (aviso != null) p.Avisos.Add($"consolo {c.Id.Value}: {aviso}");
                    if (k == null) continue;
                    p.Consolos.Add(k);
                    xs.AddRange(GeometryHelper.GetVertices(cr).Select(v => Cm((v - info.Origin).DotProduct(along))));
                    // o corte passa pela peça e pelo consolo juntos
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
            // viga: trecho com consolo de outra viga = apoio de viga
            if (beam)
                foreach (TrechoPeca t in p.Trechos)
                    if (p.Consolos.Any(k => Math.Min(k.S1, t.X1) - Math.Max(k.S0, t.X0) > 0.5)) t.FuncaoSugerida = FuncaoTrecho.ApoioViga;
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

        /// <summary>
        /// Consolo numa face lateral da viga: face 4 = +y (esquerda de quem olha ao longo do eixo), 2 = −y; trecho ao longo do
        /// eixo e o perfil cortado no meio dele (distância para fora × altura a partir do fundo da viga).
        /// </summary>
        private static ConsoloLevantado BeamCorbel(PieceInfo info, List<Solid> solids, (double Y, int Sign)? face, out string aviso)
        {
            aviso = null;
            var v = new List<XYZ>();
            foreach (Solid s in solids) foreach (Edge e in s.Edges) v.AddRange(e.Tessellate());
            double X(XYZ q) => (q - info.Origin).DotProduct(info.AxisX);
            double Y(XYZ q) => (q - info.Origin).DotProduct(info.AxisY);
            double x0 = v.Min(X), x1 = v.Max(X), y0 = v.Min(Y);
            // a face em que encosta (a da alma, dentro das abas) — do reconhecimento; sem ela, a da caixa
            bool mais = face?.Sign > 0 || (face == null && y0 >= info.LengthY - U.Mm(5));
            double fy = face?.Y ?? (mais ? info.LengthY : 0);
            XYZ fp = info.Origin + info.AxisY * fy, outward = mais ? info.AxisY : info.AxisY.Negate();
            SecaoPoligonal perfil = Cut(solids, fp, info.AxisX, outward, info.AxisZ, (x0 + x1) / 2, out string a2);
            if (perfil == null) { aviso = a2 ?? "perfil não cortado"; return null; }
            return new ConsoloLevantado { Face = mais ? 4 : 2, S0 = Round(Cm(x0)), S1 = Round(Cm(x1)), Perfil = perfil };
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
