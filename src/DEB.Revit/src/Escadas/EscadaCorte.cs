using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using DEB.Core.Elementos.Escada;
using DEB.Revit.UI;
using WPoint = System.Windows.Point;

namespace DEB.Revit.Escadas
{
    /// <summary>
    /// Corte transversal da escada no meio do lance, perpendicular ao plano inclinado: laje (h), degrau (tracejado), apoios
    /// laterais (transversal e balanço) e as barras — as que correm na largura em linha (com as dobras do perfil) e as que correm
    /// ao longo do lance em pontos. Com <see cref="MostrarDeformada"/>, a deformada da faixa (transversal e balanço).
    /// </summary>
    internal sealed class EscadaCorte : FrameworkElement
    {
        public ResultadoEscada Resultado { get; set; }
        public bool MostrarDeformada { get; set; }
        private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
        private static readonly Typeface Tf = new Typeface("Segoe UI");

        protected override void OnRender(DrawingContext dc)
        {
            double W = ActualWidth, H = ActualHeight;
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, W, H));
            var r = Resultado;
            if (r?.Geo == null || W < 80 || H < 80) return;
            var e = r.Entrada; var g = r.Geo;
            double B = e.Largura, h = g.H, la = e.LarguraApoio, c = g.Cos, tg = Math.Tan(g.Alfa);
            double hDeg = g.Espelho * c;   // altura do degrau medida perpendicular à laje (no canto)
            bool trans = e.Tipo == TipoEscada.Transversal, bal = e.Tipo == TipoEscada.Balanco;
            double y0 = -B / 2 - (trans || bal ? la + 10 : 15), y1 = B / 2 + (trans ? la + 10 : 15);
            double t0 = -35, t1 = h + hDeg + 25;
            double m = 30, esc = Math.Min((W - 2 * m) / (y1 - y0), (H - 2 * m - 18) / (t1 - t0));
            double ox = (W - (y1 - y0) * esc) / 2, oy = H - m - ((H - 2 * m - 18) - (t1 - t0) * esc) / 2;
            WPoint P(double y, double t) => new WPoint(ox + (y - y0) * esc, oy - (t - t0) * esc);
            FormattedText Ft(string s, double t, Brush b) => new FormattedText(s, Pt, FlowDirection.LeftToRight, Tf, t, b, 1.25);
            var tinta = Estilo.B(Estilo.Tinta); var suave = Estilo.B(Estilo.Suave);
            dc.DrawText(Ft("Corte transversal (meio do lance)", 11.5, tinta), new WPoint(8, 4));

            // apoios laterais
            var fundoApoio = Estilo.B(Estilo.Hex("#d9d8d2")); var penA = new Pen(Estilo.B(Estilo.Concreto), 1); penA.Freeze();
            if (trans || bal) dc.DrawRectangle(fundoApoio, penA, new Rect(P(-B / 2 - la, h + 10), P(-B / 2, -30)));
            if (trans) dc.DrawRectangle(fundoApoio, penA, new Rect(P(B / 2, h + 10), P(B / 2 + la, -30)));
            // laje e degrau
            var penC = new Pen(tinta, 1.3); penC.Freeze();
            dc.DrawRectangle(Estilo.B(Estilo.Hex("#eeede6")), penC, new Rect(P(-B / 2, h), P(B / 2, 0)));
            var penD = new Pen(suave, 0.9) { DashStyle = DashStyles.Dash }; penD.Freeze();
            dc.DrawRectangle(null, penD, new Rect(P(-B / 2, h + hDeg), P(B / 2, h)));
            var fd = Ft("degrau", 9.5, suave); dc.DrawText(fd, new WPoint(P(B / 2, h + hDeg).X - fd.Width - 4, P(0, h + hDeg).Y + 2));

            // distância perpendicular de um ponto da elevação à face de baixo do lance
            double T((double X, double Z) p) => (p.Z - ((p.X - g.X1) * tg - h / c)) * c;
            double xm = (g.X1 + g.X2) / 2;
            foreach (var b in r.Barras)
            {
                var cor = EscadaPreview.CorDe(b);
                var br = Estilo.B(cor);
                if (b.Transversal)
                {
                    double tb = T(b.Pontos[0]);
                    var perf = b.PerfilY.Count > 0 ? b.PerfilY : new List<(double Y, double N)> { (-B / 2 + e.Cobrimento, 0), (B / 2 - e.Cobrimento, 0) };
                    var pen = new Pen(br, Math.Max(1.5, b.Bitola / 10 * esc)) { LineJoin = PenLineJoin.Round }; pen.Freeze();
                    for (int i = 1; i < perf.Count; i++) dc.DrawLine(pen, P(perf[i - 1].Y, tb + perf[i - 1].N), P(perf[i].Y, tb + perf[i].N));
                    var ft = Ft($"{b.Nome} ø{F(b.Bitola, "0.#")}", 10, br);
                    var pr = P(perf.OrderBy(q => q.Y).First().Y, tb);
                    dc.DrawText(ft, new WPoint(Math.Max(2, pr.X - ft.Width - 4), pr.Y - ft.Height / 2));
                }
                else
                {
                    // barra ao longo do lance: um ponto por barra na largura, onde a barra passa pelo meio do lance
                    double? tb = null;
                    for (int i = 1; i < b.Pontos.Count; i++)
                    {
                        var a = b.Pontos[i - 1]; var q = b.Pontos[i];
                        if ((a.X - xm) * (q.X - xm) <= 0 && Math.Abs(q.X - a.X) > 1e-9)
                        { double u = (xm - a.X) / (q.X - a.X); tb = T((xm, a.Z + (q.Z - a.Z) * u)); break; }
                    }
                    if (tb == null || tb < -1 || tb > h + hDeg) continue;
                    double rr = Math.Max(1.8, b.Bitola / 20 * esc);
                    int n = Math.Max(1, b.Quantidade);
                    double yI = -B / 2 + e.Cobrimento + b.Bitola / 20, yF = B / 2 - e.Cobrimento - b.Bitola / 20;
                    for (int i = 0; i < n; i++)
                    {
                        double y = n == 1 ? 0 : Math.Min(yF, yI + i * b.Espacamento);
                        dc.DrawEllipse(br, null, P(y, tb.Value), rr, rr);
                    }
                }
            }
            // deformada da faixa (transversal e balanço)
            if (MostrarDeformada && (trans || bal) && r.Diagrama.Count > 1)
            {
                double fmax = r.Diagrama.Max(p => Math.Abs(p.FlechaElastica)), fat = fmax > 1e-9 ? 18 / fmax : 0;
                double yOrig = trans ? -r.VaoCalculo / 2 : -B / 2;
                var pd = new Pen(Estilo.B(EscadaPreview.CorEixo), 2); pd.Freeze();
                var sg = new StreamGeometry();
                using (var s = sg.Open())
                {
                    bool prim = true;
                    foreach (var p in r.Diagrama)
                    {
                        var q = P(yOrig + p.X, h / 2 - p.FlechaElastica * fat);
                        if (prim) { s.BeginFigure(q, false, false); prim = false; } else s.LineTo(q, true, true);
                    }
                }
                sg.Freeze(); dc.DrawGeometry(null, pd, sg);
                var ft = Ft($"deformada (QP, {F(fat, "0")}×)", 10, Estilo.B(EscadaPreview.CorEixo));
                dc.DrawText(ft, new WPoint(W - ft.Width - 8, 4));
            }
            // cotas
            var pc = new Pen(suave, 0.8); pc.Freeze();
            var a0 = P(-B / 2, -18); var a1 = P(B / 2, -18);
            dc.DrawLine(pc, a0, a1);
            var fb = Ft($"largura {F(B, "0")}", 10, suave); dc.DrawText(fb, new WPoint((a0.X + a1.X) / 2 - fb.Width / 2, a0.Y + 2));
            var fh = Ft($"h = {F(h, "0.#")}", 10, tinta); dc.DrawText(fh, new WPoint(P(B / 2, h / 2).X + 4, P(0, h / 2).Y - fh.Height / 2));
            string lado = bal ? "apoio (engaste)" : trans ? "apoios laterais" : "apoios nas pontas do lance (fora do corte)";
            var fl = Ft(lado, 9.5, suave); dc.DrawText(fl, new WPoint(8, H - fl.Height - 4));
        }

        private static string F(double v, string f) => v.ToString(f, Pt);
    }
}
