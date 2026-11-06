using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace DEB.Revit.Core
{
    /// <summary>
    /// Cotas gerais da peça em cada vista ortogonal: para cada eixo local paralelo ao plano da vista,
    /// cota entre as duas faces extremas. Medidas horizontais vão abaixo da peça, verticais à direita.
    /// </summary>
    internal sealed class DimensionService
    {
        private readonly Document _doc;
        private readonly DetailingConfig _cfg;
        private readonly PieceLog _log;

        public DimensionService(Document doc, DetailingConfig cfg, PieceLog log)
        {
            _doc = doc; _cfg = cfg; _log = log;
        }

        /// <summary>Tipo de cota a usar (null = padrão do projeto).</summary>
        public DimensionType DimensionType { get; set; }

        private Dimension NewDimension(View view, Line line, ReferenceArray refs)
        {
            return DimensionType != null ? _doc.Create.NewDimension(view, line, refs, DimensionType)
                                         : _doc.Create.NewDimension(view, line, refs);
        }

        /// <summary>
        /// Cadeia com trechos curtos (21,5 / 23,5 dos consolos): o texto que não cabe entre as linhas de chamada vai para o
        /// lado do vizinho comprido, com a linha de chamada do Revit (como o escritório faz à mão).
        /// </summary>
        private static void MoveSmallTexts(Dimension d, View view)
        {
            if (d == null || d.NumberOfSegments < 2 || !(d.Curve is Line ln)) return;
            XYZ dir = ln.Direction;
            double th = d.DimensionType?.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? U.Mm(2.5);
            double k = th * view.Scale;   // altura do texto em unidades do modelo
            List<DimensionSegment> segs = d.Segments.Cast<DimensionSegment>().OrderBy(s => s.Origin.DotProduct(dir)).ToList();
            double TextLen(DimensionSegment s) => ((s.ValueString ?? "000").Length * 0.7 + 1.0) * k;
            bool Small(DimensionSegment s) => (s.Value ?? 0) < TextLen(s);
            for (int i = 0; i < segs.Count; i++)
            {
                DimensionSegment s = segs[i];
                if (!Small(s)) continue;
                int sign;
                if (i == 0 || !Small(segs[i - 1])) sign = -1;                  // vai para o vizinho de baixo
                else if (i == segs.Count - 1 || !Small(segs[i + 1])) sign = 1;  // ou para o de cima
                else continue;
                XYZ tp;
                try { tp = s.TextPosition; } catch { continue; }
                if (tp == null) continue;
                double shift = (s.Value ?? 0) / 2 + TextLen(s) / 2 + 0.3 * k;
                try { s.TextPosition = tp + dir * (sign * shift); } catch { /* posição não aplicável */ }
            }
        }

        /// <summary>
        /// Cota uma vista. Uma falha aqui vira aviso da vista: não deve desfazer o detalhamento
        /// (assembly, vistas e folha) da peça inteira.
        /// </summary>
        /// <param name="vertical">Com a cota do comprimento total (ao longo do eixo da peça).</param>
        public int AddOverallDimensions(DetailView dv, PieceInfo p, bool vertical = true)
        {
            try
            {
                return AddOverallDimensionsCore(dv, p, vertical);
            }
            catch (Exception ex)
            {
                _log.Warn($"{dv.Title}: cotas não criadas ({ex.Message}).");
                _log.Trace($"Cotar {dv.Title}", ex);
                return 0;
            }
        }

        /// <summary>
        /// Cotas de um elemento anexado à peça (consolo): níveis em relação à base do pilar,
        /// balanço e largura. Cada cota só é feita quando o eixo correspondente está no plano da vista.
        /// </summary>
        /// <param name="detail">Vista de detalhe do consolo: só as medidas dele (sem a cadeia até a base do pilar).</param>
        /// <param name="skipLevels">Sem a cadeia vertical (a vista recebe a cota acumulada de <see cref="AddLevelChain"/>).</param>
        public int AddAttachedDimensions(DetailView dv, PieceInfo p, FamilyInstance k, bool detail = false, bool skipLevels = false)
        {
            try
            {
                return AddAttachedDimensionsCore(dv, p, k, detail, skipLevels);
            }
            catch (Exception ex)
            {
                _log.Warn($"{dv.Title}: cotas do consolo {k.Id.Value} não criadas ({ex.Message}).");
                _log.Trace($"Cotar consolo em {dv.Title}", ex);
                return 0;
            }
        }

        private int AddAttachedDimensionsCore(DetailView dv, PieceInfo p, FamilyInstance k, bool detail, bool skipLevels)
        {
            View view = dv.View;
            if (view is View3D) return 0;
            XYZ vd = view.ViewDirection;
            bool InPlane(XYZ a) => Math.Abs(a.DotProduct(vd)) < 1e-3;

            List<SolidRef> ks = GeometryHelper.GetSolids(k, view);
            if (ks.Count == 0) ks = GeometryHelper.GetSolids(k, null);
            List<SolidRef> cs = GeometryHelper.GetSolids(p.Instance, view);
            if (cs.Count == 0) cs = GeometryHelper.GetSolids(p.Instance, null);
            if (ks.Count == 0) return 0;

            List<XYZ> kv = GeometryHelper.GetVertices(ks);
            double Max(XYZ d) => kv.Max(v => v.DotProduct(d));

            // Direção "para fora" do consolo: eixo da seção em que o centro dele mais se afasta do pilar.
            XYZ kc = kv.Aggregate(XYZ.Zero, (s, v) => s + v) / kv.Count;
            XYZ pc = p.Vertices.Aggregate(XYZ.Zero, (s, v) => s + v) / p.Vertices.Count;
            XYZ delta = kc - pc;
            XYZ u = Math.Abs(delta.DotProduct(p.AxisX)) >= Math.Abs(delta.DotProduct(p.AxisY))
                ? p.AxisX * Math.Sign(delta.DotProduct(p.AxisX))
                : p.AxisY * Math.Sign(delta.DotProduct(p.AxisY));
            XYZ t = p.AxisZ.CrossProduct(u);
            double gap = U.Mm(_cfg.DimensionOffsetMm) * view.Scale;
            double w = view.Origin.DotProduct(vd);

            // 1º passo: referências e linhas (sólidos não são acessados depois de criar cotas).
            var plans = new List<(Line Line, ReferenceArray Refs)>();

            if (InPlane(p.AxisZ) && !skipLevels)
            {
                // Base do pilar + níveis do consolo, em cadeia, ao lado da ponta do consolo.
                var refs = new List<(double T, Reference Ref)>();
                if (!detail && GeometryHelper.TryGetExtremeFaces(cs, p.AxisZ, out Reference rb, out double tb, out _, out _))
                    refs.Add((tb, rb));
                refs.AddRange(GeometryHelper.RefsAlong(ks, p.AxisZ, vd));
                if (detail)
                {
                    // detalhe: altura do consolo ao lado da ponta (ou à esquerda no corte transversal)
                    XYZ od = InPlane(u) ? u : view.RightDirection.Negate();
                    var zs = refs.OrderBy(r => r.T).ToList();
                    if (zs.Count >= 2) Add(plans, new List<(double, Reference)> { zs[0], zs[^1] }, p.AxisZ, od, Max(od) + 2 * gap, vd, w);
                }
                else
                {
                    XYZ outDir = InPlane(u) ? u : view.RightDirection;
                    double s = Math.Max(Max(outDir), p.MaxAlong(outDir)) + 2 * gap;
                    Add(plans, refs, p.AxisZ, outDir, s, vd, w);
                }
            }
            if (InPlane(u))
            {
                // Balanço: face do pilar até a ponta, acima do consolo (elevação) ou ao lado dele (corte horizontal).
                List<(double T, Reference Ref)> along = GeometryHelper.RefsAlong(ks, u, vd);
                bool elevation = InPlane(p.AxisZ);
                XYZ side = elevation ? p.AxisZ : t;
                double sSide = elevation ? Max(side) + gap : Math.Max(Max(side), p.MaxAlong(side)) + gap;
                if (along.Count >= 2)
                    Add(plans, new List<(double, Reference)> { along[0], along[^1] }, u, side, sSide, vd, w);
            }
            if (InPlane(t))
            {
                // Largura do consolo, além da ponta.
                List<(double T, Reference Ref)> across = GeometryHelper.RefsAlong(ks, t, vd);
                // (corte transversal do detalhe: o balanço é a direção da vista; a cota vai acima do consolo)
                XYZ sideT = detail && !InPlane(u) ? p.AxisZ : u;
                if (across.Count >= 2)
                    Add(plans, new List<(double, Reference)> { across[0], across[^1] }, t, sideT, Max(sideT) + gap, vd, w);
            }

            // 2º passo: cria as cotas.
            int created = 0;
            foreach (var (line, refs) in plans)
            {
                try
                {
                    NewDimension(view, line, refs);
                    created++;
                }
                catch (Exception ex)
                {
                    _log.Warn($"{dv.Title}: cota do consolo não criada ({ex.Message}).");
                }
            }
            return created;
        }

        /// <summary>Monta a linha de cota ao longo de axis, deslocada s na direção outDir, no plano da vista.</summary>
        private static void Add(List<(Line, ReferenceArray)> plans, List<(double T, Reference Ref)> refs,
            XYZ axis, XYZ outDir, double s, XYZ vd, double w)
        {
            refs = refs.OrderBy(r => r.T).ToList();
            if (refs.Count < 2 || refs[^1].T - refs[0].T < U.Mm(1)) return;
            XYZ P(double t) => axis * t + outDir * s + vd * w;
            var ra = new ReferenceArray();
            foreach (var r in refs) ra.Append(r.Ref);
            plans.Add((Line.CreateBound(P(refs[0].T), P(refs[^1].T)), ra));
        }

        /// <summary>
        /// Cota acumulada da elevação (como o pilar padrão do CAD): uma cadeia só, à direita, da base ao topo do pilar
        /// passando pelas faces horizontais de todos os consolos — no lugar de uma cadeia por consolo.
        /// </summary>
        public int AddLevelChain(DetailView dv, PieceInfo p)
        {
            try
            {
                View view = dv.View;
                XYZ vd = view.ViewDirection, right = view.RightDirection;
                if (view is View3D || Math.Abs(p.AxisZ.DotProduct(vd)) > 1e-3 || p.Attached.Count == 0) return 0;
                List<SolidRef> cs = GeometryHelper.GetSolids(p.Instance, view);
                if (cs.Count == 0) cs = GeometryHelper.GetSolids(p.Instance, null);
                var refs = new List<(double T, Reference Ref)>();
                if (GeometryHelper.TryGetExtremeFaces(cs, p.AxisZ, out Reference rb, out double tb, out Reference rt, out double tt))
                {
                    refs.Add((tb, rb));
                    refs.Add((tt, rt));
                }
                var kv = new List<XYZ>();
                foreach (FamilyInstance k in p.Attached)
                {
                    List<SolidRef> ks = GeometryHelper.GetSolids(k, view);
                    if (ks.Count == 0) ks = GeometryHelper.GetSolids(k, null);
                    kv.AddRange(GeometryHelper.GetVertices(ks));
                    foreach (var r in GeometryHelper.RefsAlong(ks, p.AxisZ, vd))
                        if (!refs.Any(x => Math.Abs(x.T - r.T) < U.Mm(1))) refs.Add(r);
                }
                double gap = U.Mm(_cfg.DimensionOffsetMm) * view.Scale;
                double s = Math.Max(p.MaxAlong(right), kv.Count > 0 ? kv.Max(v => v.DotProduct(right)) : double.MinValue) + gap;
                var plans = new List<(Line, ReferenceArray)>();
                Add(plans, refs, p.AxisZ, right, s, vd, view.Origin.DotProduct(vd));
                foreach (var (line, ra) in plans)
                {
                    Dimension d = NewDimension(view, line, ra);
                    _doc.Regenerate();
                    MoveSmallTexts(d, view);
                }
                return plans.Count;
            }
            catch (Exception ex)
            {
                _log.Warn($"{dv.Title}: cota acumulada não criada ({ex.Message}).");
                _log.Trace($"Cota acumulada {dv.Title}", ex);
                return 0;
            }
        }

        private int AddOverallDimensionsCore(DetailView dv, PieceInfo p, bool vertical)
        {
            View view = dv.View;
            if (view is View3D) return 0;

            XYZ vd = view.ViewDirection;
            XYZ right = view.RightDirection;
            XYZ up = view.UpDirection;

            List<SolidRef> solids = GeometryHelper.GetSolids(p.Instance, view);
            if (solids.Count == 0) solids = GeometryHelper.GetSolids(p.Instance, null);
            List<XYZ> attached = p.Attached.SelectMany(k => GeometryHelper.GetVertices(GeometryHelper.GetSolids(k, null))).ToList();

            // 1º passo: lê todas as faces extremas. Criar uma cota regenera a vista e, em cortes,
            // invalida os sólidos da geometria de vista (acessá-los depois gera SEHException).
            double w = view.Origin.DotProduct(vd);
            var lines = new List<(Line Line, Reference Min, Reference Max)>();
            foreach (XYZ axis in new[] { p.AxisX, p.AxisY, p.AxisZ })
            {
                if (Math.Abs(axis.DotProduct(vd)) > 1e-3) continue;   // eixo fora do plano da vista
                if (!vertical && axis.IsAlmostEqualTo(p.AxisZ)) continue;

                XYZ outDir;
                if (Math.Abs(axis.DotProduct(right)) > 0.999) outDir = up.Negate();   // cota horizontal → abaixo
                else if (Math.Abs(axis.DotProduct(up)) > 0.999) outDir = right;       // cota vertical → à direita
                else continue;                                                        // vista girada: ignora

                if (!GeometryHelper.TryGetExtremeFaces(solids, axis, out Reference rMin, out double tMin,
                                                                     out Reference rMax, out double tMax))
                {
                    _log.Warn($"{dv.Title}: faces extremas não encontradas para cotar.");
                    continue;
                }

                // axis, outDir e vd são ortonormais: o ponto é montado pelas três coordenadas.
                double gap = U.Mm(_cfg.DimensionOffsetMm) * view.Scale;
                double s = p.MaxAlong(outDir) + gap;
                if (outDir.IsAlmostEqualTo(right) && attached.Count > 0)
                    // total vertical por fora dos consolos e da cadeia acumulada (AddLevelChain)
                    s = Math.Max(p.MaxAlong(outDir), attached.Max(v => v.DotProduct(outDir))) + 2 * gap;
                XYZ P(double t) => axis * t + outDir * s + vd * w;
                lines.Add((Line.CreateBound(P(tMin), P(tMax)), rMin, rMax));
            }

            // 2º passo: cria as cotas (daqui em diante os sólidos não são mais acessados).
            int created = 0;
            foreach (var (line, rMin, rMax) in lines)
            {
                try
                {
                    var refs = new ReferenceArray();
                    refs.Append(rMin);
                    refs.Append(rMax);
                    NewDimension(view, line, refs);
                    created++;
                }
                catch (Exception ex)
                {
                    _log.Warn($"{dv.Title}: cota não criada ({ex.Message}).");
                }
            }
            return created;
        }
    }
}
