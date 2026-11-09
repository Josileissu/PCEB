using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DEB.Core.Pecas;

namespace DEB.Revit.Precast.UI
{
    /// <summary>
    /// Desenho da peça na conferência (2026-11-09): em cima a elevação esquemática com os trechos coloridos pela função e
    /// cotados; embaixo a seção de cada trecho diferente, todas na mesma escala, com as cotas (largura e altura totais e as das
    /// faixas, quando a seção é ortogonal), a forma reconhecida, a área e a inércia. Clicar num trecho da elevação destaca a
    /// seção dele. Desenha em OnRender (como a <see cref="ColumnPreview"/>).
    /// </summary>
    internal sealed class PieceReviewPreview : FrameworkElement
    {
        private GrupoPecas _group;
        private int _sel = -1;
        private readonly List<(Rect R, int I)> _hits = new List<(Rect, int)>();

        public GrupoPecas Group { get => _group; set { _group = value; _sel = -1; InvalidateVisual(); } }

        public PieceReviewPreview() { MouseLeftButtonDown += OnClick; }

        private void OnClick(object s, MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(this);
            var h = _hits.FirstOrDefault(x => x.R.Contains(p));
            if (h.R.Width > 0) { _sel = h.I; InvalidateVisual(); }
        }

        private static readonly Typeface Face = new Typeface("Segoe UI");
        private static readonly Pen Outline = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)), 1.2));
        private static readonly Pen Thick = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xC0, 0x1F, 0x1F)), 2.4));
        private static readonly Pen DimPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)), 0.8));
        private static readonly Brush Fill = Frozen(new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE4)));

        private static readonly Dictionary<string, Brush> Colors = new Dictionary<string, Brush>
        {
            [FuncaoTrecho.Dente] = Frozen(new SolidColorBrush(Color.FromRgb(0xF6, 0xC3, 0x8B))),
            [FuncaoTrecho.ApoioLaje] = Frozen(new SolidColorBrush(Color.FromRgb(0x9C, 0xC8, 0xEE))),
            [FuncaoTrecho.ApoioEscada] = Frozen(new SolidColorBrush(Color.FromRgb(0xC9, 0xA9, 0xE6))),
            [FuncaoTrecho.ApoioViga] = Frozen(new SolidColorBrush(Color.FromRgb(0xA6, 0xDB, 0xA6))),
            [FuncaoTrecho.SemApoio] = Frozen(new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE4))),
        };

        public static Brush ColorOf(string funcao) => funcao != null && Colors.TryGetValue(funcao, out var b) ? b : Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xA0)));

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));
            _hits.Clear();
            if (_group == null || ActualWidth < 100 || ActualHeight < 100) return;
            PecaLevantada p = _group.Representante;
            if (p.Trechos.Count == 0) { Text(dc, "Sem seção: a geometria não pôde ser cortada.", 20, 20, 13); return; }

            double topH = Math.Max(150, ActualHeight * 0.34);
            double fim = DrawElevation(dc, p, new Rect(0, 0, ActualWidth, topH));   // a legenda pode passar da faixa da elevação
            DrawSections(dc, p, new Rect(0, fim + 12, ActualWidth, ActualHeight - fim - 12));
        }

        // ------------------------------------------------------------------ elevação

        /// <summary>Desenha a elevação, as cotas e a legenda; devolve o y abaixo de tudo.</summary>
        private double DrawElevation(DrawingContext dc, PecaLevantada p, Rect area)
        {
            const double pad = 30;
            string titulo = p.Tipo == TipoPeca.Viga ? "ELEVAÇÃO (ao longo do eixo)" : "ELEVAÇÃO (da base para o topo →)";
            Text(dc, titulo, area.X + pad, area.Y + 6, 12, bold: true);
            double zMin = p.Trechos.Min(t => t.Secao.ZMin), zMax = p.Trechos.Max(t => t.Secao.ZMax);
            double sx = (area.Width - 2 * pad) / Math.Max(1, p.Comprimento);
            double sz = Math.Min(sx * 4, (area.Height - 95) / Math.Max(1, zMax - zMin));   // altura exagerada até 4× se a peça for longa
            double y0 = area.Y + 40 + (zMax - zMin) * sz;   // fundo na tela
            for (int i = 0; i < p.Trechos.Count; i++)
            {
                TrechoPeca t = p.Trechos[i];
                var r = new Rect(area.X + pad + t.X0 * sx, y0 - (t.Secao.ZMax - zMin) * sz, t.Comprimento * sx, (t.Secao.ZMax - t.Secao.ZMin) * sz);
                string f = string.IsNullOrEmpty(t.Funcao) ? t.FuncaoSugerida : t.Funcao;
                dc.DrawRectangle(ColorOf(f), i == _sel ? Thick : Outline, r);
                _hits.Add((new Rect(r.X, area.Y + 30, r.Width, y0 - area.Y), i));
                if (r.Width > 34) Text(dc, $"{i + 1}", r.X + r.Width / 2, r.Y + r.Height / 2 - 7, 11, center: true);
            }
            // cadeia dos trechos e total
            double yc = y0 + 16;
            if (p.Trechos.Count > 1)
                foreach (TrechoPeca t in p.Trechos) DimH(dc, area.X + pad + t.X0 * sx, area.X + pad + t.X1 * sx, yc, N(t.Comprimento));
            else yc -= 22;
            DimH(dc, area.X + pad, area.X + pad + p.Comprimento * sx, yc + 22, N(p.Comprimento));
            // legenda das funções
            double lx = area.X + pad, ly = yc + 40;
            for (int i = 0; i < p.Trechos.Count; i++)
            {
                TrechoPeca t = p.Trechos[i];
                string f = string.IsNullOrEmpty(t.Funcao) ? t.FuncaoSugerida + (string.IsNullOrEmpty(t.FuncaoSugerida) ? "?" : " (sugerida)") : t.Funcao;
                string s = $"{i + 1}: {N(t.X0)}–{N(t.X1)} {f}";
                double w = Measure(s, 11) + 26;
                if (lx + w > area.Right - pad) { lx = area.X + pad; ly += 16; }
                dc.DrawRectangle(ColorOf(string.IsNullOrEmpty(t.Funcao) ? t.FuncaoSugerida : t.Funcao), Outline, new Rect(lx, ly + 2, 10, 10));
                Text(dc, s, lx + 14, ly, 11);
                lx += w;
            }
            return ly + 16;
        }

        // ------------------------------------------------------------------ seções

        private void DrawSections(DrawingContext dc, PecaLevantada p, Rect area)
        {
            // trechos com seção diferente (as iguais aparecem uma vez, com os números dos trechos)
            var unicas = new List<(SecaoPoligonal S, List<int> Trechos)>();
            for (int i = 0; i < p.Trechos.Count; i++)
            {
                var u = unicas.FirstOrDefault(x => x.S.MesmaSecao(p.Trechos[i].Secao));
                if (u.S != null) u.Trechos.Add(i);
                else unicas.Add((p.Trechos[i].Secao, new List<int> { i }));
            }
            if (unicas.Count > 6) unicas = unicas.Take(6).ToList();
            double cellW = area.Width / unicas.Count;
            const double padX = 46, top = 58, bottom = 70;
            double bMax = unicas.Max(x => x.S.B), hMax = unicas.Max(x => x.S.H);
            double sc = Math.Min((cellW - 2 * padX) / bMax, (area.Height - top - bottom) / hMax);
            for (int k = 0; k < unicas.Count; k++)
            {
                var (s, trs) = unicas[k];
                var cell = new Rect(area.X + k * cellW, area.Y, cellW, area.Height);
                bool sel = trs.Contains(_sel);
                var (tipo, desc) = s.Classificar();
                Text(dc, $"SEÇÃO — trecho{(trs.Count > 1 ? "s" : "")} {string.Join(", ", trs.Select(i => i + 1))}", cell.X + 10, cell.Y + 6, 12, bold: true, brush: sel ? Brushes.DarkRed : Brushes.Black);
                Text(dc, desc, cell.X + 10, cell.Y + 24, 11, maxW: cellW - 20);
                // origem: canto inferior esquerdo da caixa, centrado na célula
                double ox = cell.X + (cellW - s.B * sc) / 2, oy = cell.Bottom - bottom;
                Point P((double Y, double Z) q) => new Point(ox + (q.Y - s.YMin) * sc, oy - (q.Z - s.ZMin) * sc);
                dc.DrawGeometry(Fill, sel ? Thick : Outline, Poly(s.Contorno.Select(P)));
                foreach (var f in s.Furos) dc.DrawGeometry(Brushes.White, Outline, Poly(f.Select(P)));
                // cotas: totais por fora; faixas (seção ortogonal) por dentro
                var fx = s.Faixas();
                var ys = s.Contorno.Select(q => q.Y).Distinct().OrderBy(v => v).ToList();
                var zs = s.Contorno.Select(q => q.Z).Distinct().OrderBy(v => v).ToList();
                if (fx != null && ys.Count > 2 && ys.Count <= 6)
                    for (int i = 0; i + 1 < ys.Count; i++) DimH(dc, P((ys[i], s.ZMin)).X, P((ys[i + 1], s.ZMin)).X, oy + 14, N(ys[i + 1] - ys[i]));
                DimH(dc, P((s.YMin, 0)).X, P((s.YMax, 0)).X, oy + (fx != null && ys.Count > 2 ? 34 : 14), N(s.B));
                if (fx != null && zs.Count > 2 && zs.Count <= 6)
                    for (int i = 0; i + 1 < zs.Count; i++) DimV(dc, P((s.YMax, zs[i])).Y, P((s.YMax, zs[i + 1])).Y, P((s.YMax, 0)).X + 14, N(zs[i + 1] - zs[i]));
                DimV(dc, oy, oy - s.H * sc, P((s.YMax, 0)).X + (fx != null && zs.Count > 2 ? 34 : 14), N(s.H));
                Text(dc, $"A = {N(s.Area)} cm²   Iy = {s.Iy.ToString("#,0", CultureInfo.GetCultureInfo("pt-BR"))} cm⁴   CG a {N(s.CG.Z - s.ZMin)} cm do fundo",
                     cell.X + 10, cell.Bottom - 20, 10.5, maxW: cellW - 20);
            }
        }

        // ------------------------------------------------------------------ primitivas

        private static Geometry Poly(IEnumerable<Point> pts)
        {
            var l = pts.ToList();
            var g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(l[0], true, true);
                c.PolyLineTo(l.Skip(1).ToList(), true, false);
            }
            g.Freeze();
            return g;
        }

        private void DimH(DrawingContext dc, double x0, double x1, double y, string t)
        {
            dc.DrawLine(DimPen, new Point(x0, y), new Point(x1, y));
            foreach (double x in new[] { x0, x1 })
            {
                dc.DrawLine(DimPen, new Point(x, y - 4), new Point(x, y + 4));
                dc.DrawLine(DimPen, new Point(x - 3, y + 3), new Point(x + 3, y - 3));
            }
            if (Math.Abs(x1 - x0) > Measure(t, 10) + 4) Text(dc, t, (x0 + x1) / 2, y - 14, 10, center: true);
        }

        private void DimV(DrawingContext dc, double y0, double y1, double x, string t)
        {
            dc.DrawLine(DimPen, new Point(x, y0), new Point(x, y1));
            foreach (double y in new[] { y0, y1 })
            {
                dc.DrawLine(DimPen, new Point(x - 4, y), new Point(x + 4, y));
                dc.DrawLine(DimPen, new Point(x - 3, y + 3), new Point(x + 3, y - 3));
            }
            if (Math.Abs(y1 - y0) > Measure(t, 10) + 4)
            {
                double ym = (y0 + y1) / 2;
                dc.PushTransform(new RotateTransform(-90, x + 3, ym));
                Text(dc, t, x + 3, ym - 14, 10, center: true);
                dc.Pop();
            }
        }

        private void Text(DrawingContext dc, string s, double x, double y, double size, bool bold = false, bool center = false,
            Brush brush = null, double maxW = 0)
        {
            var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                bold ? new Typeface(Face.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal) : Face,
                size, brush ?? Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            if (maxW > 0) { ft.MaxTextWidth = maxW; ft.MaxLineCount = 2; ft.Trimming = TextTrimming.CharacterEllipsis; }
            dc.DrawText(ft, new Point(center ? x - ft.Width / 2 : x, y));
        }

        private double Measure(string s, double size) =>
            new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size, Brushes.Black,
                VisualTreeHelper.GetDpi(this).PixelsPerDip).Width;

        private static string N(double v) => v.ToString("0.#", CultureInfo.GetCultureInfo("pt-BR"));

        private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    }
}
