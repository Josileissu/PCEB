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

        private void NewDimension(View view, Line line, ReferenceArray refs)
        {
            if (DimensionType != null) _doc.Create.NewDimension(view, line, refs, DimensionType);
            else _doc.Create.NewDimension(view, line, refs);
        }

        /// <summary>
        /// Cota uma vista. Uma falha aqui vira aviso da vista: não deve desfazer o detalhamento
        /// (assembly, vistas e folha) da peça inteira.
        /// </summary>
        public int AddOverallDimensions(DetailView dv, PieceInfo p)
        {
            try
            {
                return AddOverallDimensionsCore(dv, p);
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
        public int AddAttachedDimensions(DetailView dv, PieceInfo p, FamilyInstance k, bool detail = false)
        {
            try
            {
                return AddAttachedDimensionsCore(dv, p, k, detail);
            }
            catch (Exception ex)
            {
                _log.Warn($"{dv.Title}: cotas do consolo {k.Id.Value} não criadas ({ex.Message}).");
                _log.Trace($"Cotar consolo em {dv.Title}", ex);
                return 0;
            }
        }

        private int AddAttachedDimensionsCore(DetailView dv, PieceInfo p, FamilyInstance k, bool detail)
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

            if (InPlane(p.AxisZ))
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

        private int AddOverallDimensionsCore(DetailView dv, PieceInfo p)
        {
            View view = dv.View;
            if (view is View3D) return 0;

            XYZ vd = view.ViewDirection;
            XYZ right = view.RightDirection;
            XYZ up = view.UpDirection;

            List<SolidRef> solids = GeometryHelper.GetSolids(p.Instance, view);
            if (solids.Count == 0) solids = GeometryHelper.GetSolids(p.Instance, null);

            // 1º passo: lê todas as faces extremas. Criar uma cota regenera a vista e, em cortes,
            // invalida os sólidos da geometria de vista (acessá-los depois gera SEHException).
            double w = view.Origin.DotProduct(vd);
            var lines = new List<(Line Line, Reference Min, Reference Max)>();
            foreach (XYZ axis in new[] { p.AxisX, p.AxisY, p.AxisZ })
            {
                if (Math.Abs(axis.DotProduct(vd)) > 1e-3) continue;   // eixo fora do plano da vista

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
                double s = p.MaxAlong(outDir) + U.Mm(_cfg.DimensionOffsetMm) * view.Scale;
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
