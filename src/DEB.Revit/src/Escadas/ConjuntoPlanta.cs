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
    /// Planta do conjunto (D43): o contorno de cada peça (das pontas, com a largura), as linhas dos degraus e a seta de subida nos
    /// lances, o patamar hachurado, a viga do patamar tracejada e cada apoio entre peças com a carga transferida. Sai do mesmo
    /// <see cref="ConjuntoEscada"/> que vai para o Revit.
    /// </summary>
    internal sealed class ConjuntoPlanta : FrameworkElement
    {
        public ConjuntoEscada Conjunto { get; set; }
        public ResultadoConjunto Resultado { get; set; }

        private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
        private static readonly Typeface Tf = new Typeface("Segoe UI");

        protected override void OnRender(DrawingContext dc)
        {
            double W = ActualWidth, H = ActualHeight;
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, W, H));
            var c = Conjunto;
            if (c == null || c.Posicoes.Count == 0 || W < 100 || H < 100) return;
            var tinta = Estilo.B(Estilo.Tinta); var suave = Estilo.B(Estilo.Suave);
            FormattedText Ft(string s, double t, Brush b, bool neg = false) => new FormattedText(s, Pt, FlowDirection.LeftToRight,
                neg ? new Typeface(Tf.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal) : Tf, t, b, 1.25);

            // contornos em planta (cm)
            var formas = new List<(string Nome, List<(double X, double Y)> Pts, bool Viga, EscadaLaje E, GeometriaEscada G, PosicaoPeca P)>();
            foreach (var (nome, e) in c.Pecas)
            {
                if (!c.Posicoes.TryGetValue(nome, out var p)) continue;
                var g = GeometriaEscada.De(e);
                double a0 = g.XPontaInf, a1 = g.XPontaSup, b = e.Largura / 2;
                formas.Add((nome, new[] { p.Em(a0, -b), p.Em(a1, -b), p.Em(a1, b), p.Em(a0, b) }.ToList(), false, e, g, p));
            }
            foreach (var v in c.Vigas)
            {
                if (!c.Posicoes.TryGetValue(v.Nome, out var p)) continue;
                formas.Add((v.Nome, new[] { p.Em(0, -v.B / 2), p.Em(v.Comprimento, -v.B / 2), p.Em(v.Comprimento, v.B / 2), p.Em(0, v.B / 2) }.ToList(), true, null, null, p));
            }
            if (formas.Count == 0) return;
            var todos = formas.SelectMany(f => f.Pts).ToList();
            double x0 = todos.Min(q => q.X) - 30, x1 = todos.Max(q => q.X) + 30, y0 = todos.Min(q => q.Y) - 30, y1 = todos.Max(q => q.Y) + 30;
            double m = 30, esc = Math.Min((W - 2 * m) / (x1 - x0), (H - 2 * m) / (y1 - y0));
            double ox = m + ((W - 2 * m) - (x1 - x0) * esc) / 2, oy = H - m - ((H - 2 * m) - (y1 - y0) * esc) / 2;
            WPoint P((double X, double Y) q) => new WPoint(ox + (q.X - x0) * esc, oy - (q.Y - y0) * esc);
            Geometry Poli(List<(double X, double Y)> pts)
            {
                var sg = new StreamGeometry();
                using (var ctx = sg.Open()) { ctx.BeginFigure(P(pts[0]), true, true); ctx.PolyLineTo(pts.Skip(1).Select(P).ToList(), true, true); }
                sg.Freeze(); return sg;
            }

            var penPeca = new Pen(tinta, 1.4); penPeca.Freeze();
            var penViga = new Pen(Estilo.B(Estilo.Anil), 1.3) { DashStyle = DashStyles.Dash }; penViga.Freeze();
            var penDeg = new Pen(suave, 0.7); penDeg.Freeze();
            // vigas primeiro (ficam sob o patamar)
            foreach (var f in formas.Where(f => f.Viga))
            {
                dc.DrawGeometry(Estilo.B(Estilo.Hex("#e7ecf4")), penViga, Poli(f.Pts));
                var vg = c.Vigas.First(v => v.Nome == f.Nome);
                var ft = Ft($"{f.Nome} — viga do patamar {vg.B:0}×{vg.H:0}", 10.5, Estilo.B(Estilo.Anil), true);
                // rótulo do lado de fora da viga, centrado nela e dentro da área do desenho
                var pa = P(f.P.Em(vg.Comprimento / 2, vg.B / 2)); var pb = P(f.P.Em(vg.Comprimento / 2, -vg.B / 2));
                var pf = pa.Y < pb.Y ? pa : pb;
                double tx = Math.Max(4, Math.Min(W - ft.Width - 4, pf.X - ft.Width / 2)), ty = Math.Max(2, pf.Y - ft.Height - 3);
                dc.DrawText(ft, new WPoint(tx, ty));
            }
            foreach (var f in formas.Where(f => !f.Viga))
            {
                bool pat = f.E.SoPatamar;
                dc.DrawGeometry(pat ? Estilo.B(Estilo.Hex("#f1efe6")) : Brushes.White, penPeca, Poli(f.Pts));
                double b = f.E.Largura / 2;
                if (!pat)
                {
                    // degraus e patamares das pontas
                    for (int i = 0; i < f.G.N; i++)
                    {
                        double x = f.G.X1 + i * f.G.Piso;
                        dc.DrawLine(penDeg, P(f.P.Em(x, -b)), P(f.P.Em(x, b)));
                    }
                    // seta de subida
                    var pa = P(f.P.Em(f.G.X1, 0)); var pb = P(f.P.Em(f.G.X2, 0));
                    var penS = new Pen(Estilo.B(Estilo.Mata), 1.4); penS.Freeze();
                    dc.DrawLine(penS, pa, pb);
                    var u = new Vector(pb.X - pa.X, pb.Y - pa.Y); if (u.Length > 1) u.Normalize();
                    var n = new Vector(-u.Y, u.X);
                    dc.DrawLine(penS, pb, pb - u * 9 + n * 5); dc.DrawLine(penS, pb, pb - u * 9 - n * 5);
                    dc.DrawEllipse(Estilo.B(Estilo.Mata), null, pa, 3, 3);
                }
                else
                {
                    var penH = new Pen(Estilo.B(Estilo.Hex("#c9c5b4")), 0.6); penH.Freeze();
                    for (double s = f.G.XPontaInf + 15; s < f.G.XPontaSup; s += 15) dc.DrawLine(penH, P(f.P.Em(s, -b)), P(f.P.Em(s, b)));
                }
                var centro = P(f.P.Em((f.G.XPontaInf + f.G.XPontaSup) / 2, pat ? 0 : b * 0.45));
                string txt = pat ? $"{f.Nome} — patamar {f.E.ComprimentoPatamar:0} × {f.E.Largura:0}" : $"{f.Nome} — sobe {f.E.Desnivel:0} cm";
                var ftn = Ft(txt, 11, tinta, true);
                dc.DrawRectangle(Brushes.White, null, new Rect(centro.X - ftn.Width / 2 - 2, centro.Y - ftn.Height / 2, ftn.Width + 4, ftn.Height));
                dc.DrawText(ftn, new WPoint(centro.X - ftn.Width / 2, centro.Y - ftn.Height / 2));
                // cota do topo da ponta de baixo
                var fz = Ft($"z = {f.P.Z:0}", 9.5, suave);
                var p0 = P(f.P.Em(f.G.XPontaInf + 4, -b + 4));
                dc.DrawText(fz, new WPoint(p0.X + 2, p0.Y - fz.Height));
            }
            // apoios entre peças: ponto e carga
            var cor = Estilo.B(Estilo.Ocre);
            var penA = new Pen(cor, 2); penA.Freeze();
            foreach (var a in c.Apoios)
            {
                if (!c.Posicoes.TryGetValue(a.Apoiada, out var pa)) continue;
                var ea = c.Peca(a.Apoiada); if (ea == null) continue;
                var ga = GeometriaEscada.De(ea);
                double xa = a.Ponta == PontaPeca.Inferior ? 0 : ga.Vao, b = ea.Largura / 2;
                var q0 = P(pa.Em(xa, -b)); var q1 = P(pa.Em(xa, b));
                dc.DrawLine(penA, q0, q1);
                string val = "";
                if (Resultado != null && Resultado.Pecas.TryGetValue(a.Apoiada, out var ra))
                {
                    var (gk, qk) = CalculoConjuntoEscada.ReacaoPonta(ra, a.Ponta);
                    val = $" {gk * ea.Largura / 100:0.0} + {qk * ea.Largura / 100:0.0} kN";
                }
                var ft = Ft($"{a.Apoiada} → {a.Suporte}{val}", 9.5, cor, true);
                var pm = new WPoint((q0.X + q1.X) / 2, (q0.Y + q1.Y) / 2);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), null, new Rect(pm.X + 4, pm.Y + 2, ft.Width, ft.Height));
                dc.DrawText(ft, new WPoint(pm.X + 4, pm.Y + 2));
            }
            // norte do conjunto: eixos x, y
            var penE = new Pen(suave, 1); penE.Freeze();
            var o = new WPoint(14, H - 14);
            dc.DrawLine(penE, o, new WPoint(o.X + 30, o.Y)); dc.DrawLine(penE, o, new WPoint(o.X, o.Y - 30));
            dc.DrawText(Ft("x", 9.5, suave), new WPoint(o.X + 32, o.Y - 8)); dc.DrawText(Ft("y", 9.5, suave), new WPoint(o.X - 3, o.Y - 44));
            var fe = Ft($"origem = eixo do apoio de baixo do L1 · escala ≈ 1:{Math.Round(1 / (esc / 96 * 2.54) / 5) * 5:0} (na tela)", 9.5, suave);
            dc.DrawText(fe, new WPoint(56, H - fe.Height - 4));
        }
    }
}
