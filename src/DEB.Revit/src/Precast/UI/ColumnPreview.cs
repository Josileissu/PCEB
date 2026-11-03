using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace StructuralDetailer.Precast.UI
{
    /// <summary>
    /// Desenho do pilar com a armadura calculada por <see cref="ColumnRebarLayout"/>:
    /// elevação frontal (A × C), elevação lateral (B × C), seção corrente e seção no consolo (A × B).
    /// Desenha em OnRender: leve o bastante para redesenhar a cada tecla.
    /// </summary>
    internal sealed class ColumnPreview : FrameworkElement
    {
        private ColumnGroup _group;

        public ColumnGroup Group
        {
            get => _group;
            set { _group = value; InvalidateVisual(); }
        }

        public void Refresh() => InvalidateVisual();

        private static readonly Brush Back = Brushes.White;
        private static readonly Pen Concrete = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), 1.2));
        private static readonly Brush ConcreteFill = Frozen(new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)));
        private static readonly Brush CorbelFill = Frozen(new SolidColorBrush(Color.FromRgb(0xDD, 0xE6, 0xF0)));
        private static readonly Pen Dash = Frozen(new Pen(Brushes.Gray, 0.8) { DashStyle = DashStyles.Dash });

        private static readonly Dictionary<BarRole, Pen> Pens = new Dictionary<BarRole, Pen>
        {
            [BarRole.Longitudinal] = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x1F, 0x4E, 0x99)), 1.6)),
            [BarRole.Stirrup] = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0x8B, 0x57)), 1.0)),
            [BarRole.CrossTie] = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x6B, 0xB2, 0x6B)), 1.0)),
            [BarRole.CorbelTie] = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xC0, 0x1F, 0x1F)), 1.8)),
            [BarRole.CorbelSew] = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x1A)), 1.2)),
            [BarRole.LiftLoop] = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x8E, 0x44, 0xAD)), 1.8))
        };

        public static IReadOnlyDictionary<BarRole, Pen> Legend => Pens;

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Back, null, new Rect(0, 0, ActualWidth, ActualHeight));
            if (_group == null || ActualWidth < 50 || ActualHeight < 50) return;

            PrecastColumn rep = _group.Representative;
            ColumnFrame f = rep.Frame;
            List<BarDef> bars;
            try { bars = ColumnRebarLayout.Compute(f, rep.Corbels, _group.Spec); }
            catch { bars = new List<BarDef>(); }

            // Espaço: 2 elevações (largas o bastante para os consolos) + coluna de seções.
            double maxProj = rep.Corbels.Select(k => k.Projection).DefaultIfEmpty(0).Max();
            double lift = _group.Spec.LiftLoops > 0 ? U_.Mm(_group.Spec.LiftProtrusionMm) : 0;
            double elevW = Math.Max(f.Bx, f.By) + 2 * maxProj;
            double elevH = f.H + lift;
            double pad = 34;
            double sectionsW = ActualWidth * 0.32;
            double availW = (ActualWidth - sectionsW - 4 * pad) / 2;
            double scale = Math.Min(availW / elevW, (ActualHeight - 2 * pad - 20) / elevH);

            // Elevação frontal: A horizontal; lateral: B horizontal. Base embaixo.
            var front = new Rect(pad, pad, availW, ActualHeight - 2 * pad);
            var side = new Rect(2 * pad + availW, pad, availW, ActualHeight - 2 * pad);
            DrawElevation(dc, front, "ELEVAÇÃO FRONTAL", f, rep, bars, scale, p => p.A, f.Bx, ColumnFace.NX, ColumnFace.PX, ColumnFace.NY, ColumnFace.PY, lift);
            DrawElevation(dc, side, "ELEVAÇÃO LATERAL", f, rep, bars, scale, p => p.B, f.By, ColumnFace.NY, ColumnFace.PY, ColumnFace.NX, ColumnFace.PX, lift);

            // Seções empilhadas à direita.
            double sx = 3 * pad + 2 * availW;
            var cuts = new List<(string Title, double Z)> { ("SEÇÃO CORRENTE", MidFree(rep)) };
            foreach (Corbel k in rep.Corbels.OrderBy(k => k.ZTop))
                if (cuts.All(c => Math.Abs(c.Z - k.ZTop) > U_.Mm(50)))
                    cuts.Add(($"SEÇÃO NO CONSOLO (h = {U_.ToMm(k.ZTop) / 1000:0.00} m)", k.ZTop - U_.Mm(_group.Spec.CoverMm + _group.Spec.TieDiameterMm)));
            double cellH = (ActualHeight - 2 * pad) / cuts.Count;
            for (int i = 0; i < cuts.Count; i++)
                DrawSection(dc, new Rect(sx, pad + i * cellH, sectionsW - pad, cellH - 8), cuts[i].Title, f, rep, bars, cuts[i].Z);
        }

        private void DrawElevation(DrawingContext dc, Rect box, string title, ColumnFrame f, PrecastColumn rep, List<BarDef> bars,
            double scale, Func<Loc, double> horiz, double width, ColumnFace left, ColumnFace right, ColumnFace back, ColumnFace front, double lift)
        {
            Text(dc, title, new Point(box.Left, box.Top - 22), 12, true);
            double cx = box.Left + box.Width / 2;
            double baseY = box.Bottom - 6;
            Point P(double h, double c) => new Point(cx + (h - width / 2) * scale, baseY - c * scale);

            // Concreto
            dc.DrawRectangle(ConcreteFill, Concrete, new Rect(P(0, f.H), P(width, 0)));
            if (rep.EmbedmentMm > 0)
            {
                double ze = U_.Mm(rep.EmbedmentMm);
                dc.DrawLine(Dash, P(-width * 0.4, ze), P(width * 1.4, ze));
                Text(dc, "embutimento", P(width * 1.05, ze + U_.Mm(40)), 9, false);
            }

            foreach (Corbel k in rep.Corbels)
            {
                if (k.Face == left || k.Face == right)
                {
                    // Consolo de perfil: trapézio para fora da face.
                    double sign = k.Face == right ? 1 : -1;
                    double x0 = k.Face == right ? width : 0;
                    var g = new StreamGeometry();
                    using (StreamGeometryContext s = g.Open())
                    {
                        s.BeginFigure(P(x0, k.ZTop), true, true);
                        s.LineTo(P(x0 + sign * k.Projection, k.ZTop), true, false);
                        s.LineTo(P(x0 + sign * k.Projection, k.ZTop - k.TipHeight), true, false);
                        s.LineTo(P(x0, k.ZBottom), true, false);
                    }
                    g.Freeze();
                    dc.DrawGeometry(CorbelFill, Concrete, g);
                }
                else if (k.Face == front)
                {
                    // Consolo de frente: retângulo sobre o pilar.
                    dc.DrawRectangle(CorbelFill, Concrete, new Rect(P(k.S0, k.ZTop), P(k.S1, k.ZBottom)));
                }
                else
                {
                    dc.DrawRectangle(null, Dash, new Rect(P(k.S0, k.ZTop), P(k.S1, k.ZBottom)));
                }
            }

            // Barras (estribos e grampos como traço; demais como polilinha).
            foreach (BarDef b in bars.OrderBy(b => b.Role == BarRole.Longitudinal ? 1 : 0))
            {
                Pen pen = Pens[b.Role];
                foreach (List<Loc> inst in b.Instances())
                {
                    var pts = inst.Select(q => P(horiz(q), q.C)).ToList();
                    if (b.Closed) pts.Add(pts[0]);
                    for (int i = 0; i < pts.Count - 1; i++)
                        if ((pts[i] - pts[i + 1]).Length > 0.3) dc.DrawLine(pen, pts[i], pts[i + 1]);
                }
            }

            // Cotas principais
            Text(dc, $"{U_.ToMm(f.H):0}", P(width, f.H / 2) + new Vector(8, 0), 10, false, rotate: true);
            Text(dc, $"{U_.ToMm(width):0}", P(width / 2, 0) + new Vector(-10, 4), 10, false);
            foreach (Corbel k in rep.Corbels.Where(k => k.Face == left || k.Face == right))
                Text(dc, $"{U_.ToMm(k.ZTop):0}", P(k.Face == right ? width + k.Projection : -k.Projection, k.ZTop) + new Vector(k.Face == right ? 4 : -34, -14), 9, false);
        }

        private void DrawSection(DrawingContext dc, Rect box, string title, ColumnFrame f, PrecastColumn rep, List<BarDef> bars, double z)
        {
            Text(dc, title, new Point(box.Left, box.Top), 12, true);
            var area = new Rect(box.Left, box.Top + 20, box.Width, box.Height - 26);

            List<Corbel> here = rep.Corbels.Where(k => z >= k.ZBottom - 1e-6 && z <= k.ZTop + 1e-6).ToList();
            double minA = 0, maxA = f.Bx, minB = 0, maxB = f.By;
            foreach (Corbel k in here)
                foreach (Loc q in new[] { k.At(f, k.Projection, k.S0, z), k.At(f, k.Projection, k.S1, z) })
                {
                    minA = Math.Min(minA, q.A); maxA = Math.Max(maxA, q.A);
                    minB = Math.Min(minB, q.B); maxB = Math.Max(maxB, q.B);
                }
            double sc = Math.Min(area.Width / (maxA - minA), area.Height / (maxB - minB)) * 0.85;
            double ox = area.Left + (area.Width - (maxA - minA) * sc) / 2, oy = area.Bottom - (area.Height - (maxB - minB) * sc) / 2;
            Point P(double a, double b) => new Point(ox + (a - minA) * sc, oy - (b - minB) * sc);

            dc.DrawRectangle(ConcreteFill, Concrete, new Rect(P(0, f.By), P(f.Bx, 0)));
            foreach (Corbel k in here)
            {
                Loc a = k.At(f, 0, k.S0, z), b = k.At(f, k.Projection, k.S1, z);
                dc.DrawRectangle(CorbelFill, Concrete, new Rect(P(a.A, a.B), P(b.A, b.B)));
            }

            // Barras que cruzam a cota z: longitudinais como ponto; laços e barras horizontais no plano.
            double tolZ = U_.Mm(30);
            foreach (BarDef b in bars)
            {
                Pen pen = Pens[b.Role];
                if (b.Role == BarRole.Longitudinal)
                {
                    Loc q = b.Points[0];
                    if (z >= b.Points.Min(x => x.C) && z <= b.Points.Max(x => x.C))
                        dc.DrawEllipse(pen.Brush, null, P(q.A, q.B), Math.Max(2, U_.Mm(b.DiameterMm) / 2 * sc), Math.Max(2, U_.Mm(b.DiameterMm) / 2 * sc));
                    continue;
                }

                // Mostra a barra do conjunto mais próxima da cota do corte (estribo, grampo, tirante, costura).
                List<Loc> best = null;
                double bestD = double.MaxValue;
                foreach (List<Loc> inst in b.Instances())
                {
                    bool horizontal = inst.All(x => Math.Abs(x.C - inst[0].C) < 1e-6);
                    double dz = horizontal ? Math.Abs(inst[0].C - z) : (z >= inst.Min(x => x.C) && z <= inst.Max(x => x.C) ? 0 : double.MaxValue);
                    if (b.Role == BarRole.CorbelTie) dz = Math.Abs(inst.Max(x => x.C) - z);
                    if (dz < bestD) { bestD = dz; best = inst; }
                }
                bool inPlane = b.Role == BarRole.CorbelTie || b.Role == BarRole.CorbelSew ? bestD < U_.Mm(200) : bestD < Math.Max(tolZ, b.Spacing);
                if (best == null || !inPlane || b.Role == BarRole.LiftLoop) continue;

                var pts = best.Select(q => P(q.A, q.B)).ToList();
                if (b.Closed) pts.Add(pts[0]);
                for (int i = 0; i < pts.Count - 1; i++)
                    if ((pts[i] - pts[i + 1]).Length > 0.3) dc.DrawLine(pen, pts[i], pts[i + 1]);
            }

            Text(dc, $"{U_.ToMm(f.Bx):0}", P(f.Bx / 2, 0) + new Vector(-10, 4), 10, false);
            Text(dc, $"{U_.ToMm(f.By):0}", P(f.Bx, f.By / 2) + new Vector(6, -6), 10, false);
        }

        /// <summary>Cota do meio do maior trecho sem consolo, acima do embutimento.</summary>
        private static double MidFree(PrecastColumn rep)
        {
            double h = rep.Frame.H, cur = Math.Min(U_.Mm(rep.EmbedmentMm), h / 2), bestA = cur, bestB = h, best = -1;
            foreach (var (zb, zt) in rep.Corbels.Select(k => (k.ZBottom, k.ZTop)).OrderBy(x => x.ZBottom).Append((h, h)))
            {
                if (zb - cur > best) { best = zb - cur; bestA = cur; bestB = zb; }
                cur = Math.Max(cur, zt);
            }
            return (bestA + bestB) / 2;
        }

        private static void Text(DrawingContext dc, string s, Point at, double size, bool bold, bool rotate = false)
        {
            var ft = new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
                size, Brushes.Black, 1.0);
            if (rotate)
            {
                dc.PushTransform(new RotateTransform(-90, at.X, at.Y));
                dc.DrawText(ft, new Point(at.X - ft.Width / 2, at.Y));
                dc.Pop();
            }
            else dc.DrawText(ft, at);
        }

        private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    }
}
