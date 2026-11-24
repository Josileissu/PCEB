using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using DEB.Core.Elementos.Escada;
using DEB.Revit.UI;
using WColor = System.Windows.Media.Color;
using WPoint = System.Windows.Point;

namespace DEB.Revit.Escadas
{
    /// <summary>
    /// Elevação da escada na prévia (D26): o contorno com os degraus, os apoios, o eixo do cálculo e as barras propostas — tudo
    /// saído do MESMO resultado do núcleo que vai para o Revit. Cotas: vão, desnível, n × espelho e o piso.
    /// </summary>
    internal sealed class EscadaPreview : FrameworkElement
    {
        public ResultadoEscada Resultado { get; set; }
        /// <summary>Deformada (x, flecha em cm) para desenhar exagerada sobre o eixo; vazio = não desenha.</summary>
        public List<(double X, double F)> Deformada { get; } = new List<(double, double)>();
        public bool MostrarBarras { get; set; } = true;

        public static readonly WColor CorN1 = Estilo.Hex("#c0392b"), CorN2 = Estilo.Hex("#d35400"), CorDist = Estilo.Hex("#7f8c8d"),
                                      CorNeg = Estilo.Anil, CorEixo = Estilo.Hex("#8e44ad");
        public static IEnumerable<(string, WColor)> Legenda => new[]
        {
            ("principal (N1)", CorN1), ("positiva do patamar, cruzada na quina", CorN2), ("distribuição", CorDist), ("negativas e construtivas", CorNeg), ("eixo do cálculo", CorEixo),
        };

        /// <summary>Cor da barra pela função (a mesma na elevação e no corte).</summary>
        public static WColor CorDe(BarraEscada b) =>
            b.Nome == "N1" ? CorN1 : b.Funcao.StartsWith("distribui") ? CorDist : b.Funcao.Contains("cruza") ? CorN2 : CorNeg;

        private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
        private static readonly Typeface Tf = new Typeface("Segoe UI");

        protected override void OnRender(DrawingContext dc)
        {
            double W = ActualWidth, H = ActualHeight;
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, W, H));
            var r = Resultado;
            if (r?.Geo == null || r.Geo.Contorno.Count < 3 || W < 100 || H < 100) return;
            var g = r.Geo; var e = r.Entrada;
            double la = e.LarguraApoio;
            double x0 = -la / 2 - 10, x1 = g.Vao + la / 2 + 10;
            double z0 = g.Contorno.Min(p => p.Z) - 45, z1 = e.Desnivel + 30;
            double m = 46, esc = Math.Min((W - 2 * m) / (x1 - x0), (H - 2 * m) / (z1 - z0));
            double ox = m + ((W - 2 * m) - (x1 - x0) * esc) / 2, oy = H - m - ((H - 2 * m) - (z1 - z0) * esc) / 2;
            WPoint P(double x, double z) => new WPoint(ox + (x - x0) * esc, oy - (z - z0) * esc);
            FormattedText Ft(string s, double t, Brush b, bool neg = false) => new FormattedText(s, Pt, FlowDirection.LeftToRight,
                neg ? new Typeface(Tf.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal) : Tf, t, b, 1.25);
            var tinta = Estilo.B(Estilo.Tinta); var suave = Estilo.B(Estilo.Suave);

            // apoios (vigas) nas pontas: retângulo de largura do apoio sob o eixo
            var penApoio = new Pen(Estilo.B(Estilo.Concreto), 1); penApoio.Freeze();
            var fundoApoio = Estilo.B(Estilo.Hex("#d9d8d2"));
            bool preM = e.PreMoldada;
            foreach (var (xa, zt) in e.Tipo == TipoEscada.Longitudinal ? (preM ? new[] { (0.0, g.ZAssentoInf), (g.Vao, g.ZAssentoSup) } : new[] { (0.0, 0.0), (g.Vao, e.Desnivel) }) : new (double, double)[0])   // nos laterais os apoios ficam fora da elevação (ver o corte)
            {
                double hv = 40;
                var a = P(xa - la / 2, zt); var b = P(xa + la / 2, zt - hv);
                dc.DrawRectangle(fundoApoio, penApoio, new Rect(a, b));
            }
            // pré-moldada: pinos (no centro de cada apoio) e alças
            if (preM)
            {
                var penP = new Pen(Estilo.B(Estilo.Ocre), 2); penP.Freeze();
                foreach (var (xa, zb, zt, lig) in new[] { (0.0, g.ZAssentoInf, 0.0, e.LigacaoInferior), (g.Vao, g.ZAssentoSup, e.Desnivel, e.LigacaoSuperior) })
                    if (lig.Pinos > 0)
                    {
                        dc.DrawLine(penP, P(xa, zb - lig.EmbutimentoPino), P(xa, zt));
                        var fp = Ft($"{lig.Pinos} pinos ø{Fmt(lig.BitolaPino, "0.#")}", 9.5, Estilo.B(Estilo.Ocre));
                        dc.DrawText(fp, new WPoint(P(xa, 0).X + 4, P(0, zb - lig.EmbutimentoPino).Y - fp.Height));
                    }
                if (r.Icamento != null)
                    foreach (var (xa, za) in r.Icamento.Alcas)
                    {
                        var q = P(xa, za);
                        var tri = new StreamGeometry();
                        using (var sg = tri.Open()) { sg.BeginFigure(new WPoint(q.X, q.Y - 2), true, true); sg.LineTo(new WPoint(q.X - 6, q.Y - 14), true, true); sg.LineTo(new WPoint(q.X + 6, q.Y - 14), true, true); }
                        tri.Freeze(); dc.DrawGeometry(Estilo.B(Estilo.Ipe), null, tri);
                    }
            }
            // contorno da escada
            var geo = new StreamGeometry();
            using (var sg = geo.Open())
            {
                sg.BeginFigure(P(g.Contorno[0].X, g.Contorno[0].Z), true, true);
                foreach (var p in g.Contorno.Skip(1)) sg.LineTo(P(p.X, p.Z), true, true);
            }
            geo.Freeze();
            var penC = new Pen(tinta, 1.3) { LineJoin = PenLineJoin.Miter }; penC.Freeze();
            dc.DrawGeometry(Estilo.B(Estilo.Hex("#eeede6")), penC, geo);
            // eixo do cálculo
            var penE = new Pen(Estilo.B(CorEixo), 1) { DashStyle = DashStyles.DashDot }; penE.Freeze();
            foreach (var t in g.Trechos) dc.DrawLine(penE, P(t.P0.X, t.P0.Z), P(t.P1.X, t.P1.Z));
            // deformada exagerada
            if (Deformada.Count > 1)
            {
                double fmax = Deformada.Max(d => Math.Abs(d.F));
                double fat = fmax > 1e-9 ? 25 / fmax : 0;   // 25 cm no desenho para a flecha máxima
                var pd = new Pen(Estilo.B(CorEixo), 2); pd.Freeze();
                var sg2 = new StreamGeometry();
                using (var s = sg2.Open())
                {
                    bool prim = true;
                    foreach (var (x, f) in Deformada)
                    {
                        var t = g.TrechoEm(x); var q = P(x, t.ZEm(x) - f * fat);
                        if (prim) { s.BeginFigure(q, false, false); prim = false; } else s.LineTo(q, true, true);
                    }
                }
                sg2.Freeze(); dc.DrawGeometry(null, pd, sg2);
                var ft = Ft($"deformada (QP, exagerada {Fmt(fat, "0")}×)", 10.5, Estilo.B(CorEixo));
                dc.DrawText(ft, new WPoint(W - m - ft.Width, 8));
            }
            // barras
            if (MostrarBarras)
                foreach (var b in r.Barras)
                {
                    var cor = CorDe(b);
                    if (b.Transversal)
                    {
                        // pontos ao longo da linha, no espaçamento
                        double comp = CalculoEscada.Comprimento(b.Pontos), s = Math.Max(5, b.Espacamento);
                        var br = Estilo.B(cor);
                        for (double d = 0; d <= comp + 1e-6; d += s)
                        {
                            var pt = Ponto(b.Pontos, d);
                            dc.DrawEllipse(br, null, P(pt.X, pt.Z), Math.Max(1.6, b.Bitola / 20 * esc), Math.Max(1.6, b.Bitola / 20 * esc));
                        }
                        continue;
                    }
                    var pen = new Pen(Estilo.B(cor), Math.Max(1.6, b.Bitola / 10 * esc)) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    pen.Freeze();
                    for (int i = 1; i < b.Pontos.Count; i++) dc.DrawLine(pen, P(b.Pontos[i - 1].X, b.Pontos[i - 1].Z), P(b.Pontos[i].X, b.Pontos[i].Z));
                    // rótulo no meio do maior segmento
                    int k = Enumerable.Range(1, b.Pontos.Count - 1).OrderByDescending(i => Dist(b.Pontos[i - 1], b.Pontos[i])).First();
                    var mp = P((b.Pontos[k - 1].X + b.Pontos[k].X) / 2, (b.Pontos[k - 1].Z + b.Pontos[k].Z) / 2);
                    var ft = Ft($"{b.Nome} ø{Fmt(b.Bitola, "0.#")} c/{Fmt(b.Espacamento, "0.#")}", 10.5, Estilo.B(cor), true);
                    bool emBaixo = b.Pontos.Average(p => p.Z) < g.Trechos.Average(t => (t.P0.Z + t.P1.Z) / 2) && !b.Funcao.Contains("negativa");
                    dc.DrawText(ft, new WPoint(mp.X - ft.Width / 2, emBaixo ? mp.Y + 8 : mp.Y - ft.Height - 6));
                }
            // quina reentrante
            if (g.QuinaReentrante.HasValue)
            {
                var q = P(g.QuinaReentrante.Value.X, g.QuinaReentrante.Value.Z);
                var pq = new Pen(Estilo.B(Estilo.Ocre), 1.2) { DashStyle = DashStyles.Dot }; pq.Freeze();
                dc.DrawEllipse(null, pq, q, 16, 16);
                var ft = Ft("quina reentrante: barras cruzadas", 10, Estilo.B(Estilo.Ocre));
                dc.DrawText(ft, new WPoint(q.X + 18, q.Y + 6));
            }
            // cotas: vão (embaixo) e desnível (à direita)
            var penCota = new Pen(suave, 0.8); penCota.Freeze();
            double zc = g.Contorno.Min(p => p.Z) - 30;
            Cota(dc, P(0, zc), P(g.Vao, zc), $"ℓ = {Fmt(g.Vao, "0")} cm", penCota, Ft, suave, true);
            if (g.N > 0)
            {
                if (g.X1 > 1e-9) Cota(dc, P(0, zc + 14), P(g.X1, zc + 14), Fmt(g.X1, "0"), penCota, Ft, suave, true);
                Cota(dc, P(g.X1, zc + 14), P(g.X2, zc + 14), $"{g.N - 1} × {Fmt(g.Piso, "0.#")} = {Fmt(g.LanceHorizontal, "0")}", penCota, Ft, suave, true);
                if (g.Vao - g.X2 > 1e-9) Cota(dc, P(g.X2, zc + 14), P(g.Vao, zc + 14), Fmt(g.Vao - g.X2, "0"), penCota, Ft, suave, true);
                double xd = g.Vao + la / 2 + 4;
                Cota(dc, P(xd, 0), P(xd, e.Desnivel), $"{g.N} × {Fmt(g.Espelho, "0.0#")} = {Fmt(e.Desnivel, "0.#")}", penCota, Ft, suave, false);
            }
            // cargas de outras peças: seta e texto sobre a peça
            for (int ic = 0; ic < e.CargasApoiadas.Count; ic++)
            {
                var ca = e.CargasApoiadas[ic];
                double xc = ca.X + ca.Comprimento / 2, zt = g.TrechoEm(Math.Max(0, Math.Min(g.Vao, xc))).ZEm(xc) + g.TrechoEm(xc).H / 2;
                var pa = P(xc, zt + 40 + 14 * ic); var pb = P(xc, zt + 2);
                var pc = new Pen(Estilo.B(Estilo.Ocre), 1.6); pc.Freeze();
                dc.DrawLine(pc, pa, pb);
                dc.DrawLine(pc, pb, new WPoint(pb.X - 4, pb.Y - 7)); dc.DrawLine(pc, pb, new WPoint(pb.X + 4, pb.Y - 7));
                var fc = Ft($"{ca.Nome}: {Fmt(ca.G, "0.#")} + {Fmt(ca.Q, "0.#")} kN/m", 9.5, Estilo.B(Estilo.Ocre));
                dc.DrawText(fc, new WPoint(pa.X - fc.Width / 2, pa.Y - fc.Height - 1));
            }
            // espessura
            var lt = g.Trechos.FirstOrDefault(t => t.Inclinado) ?? g.Trechos[0];
            var mid = ((lt.P0.X + lt.P1.X) / 2, (lt.P0.Z + lt.P1.Z) / 2);
            var fth = Ft($"h = {Fmt(g.H, "0.#")}", 10.5, tinta);
            var pm = P(mid.Item1, mid.Item2 - g.H / g.Cos);
            dc.DrawText(fth, new WPoint(pm.X + 6, pm.Y + 4));
            var fte = Ft($"escala ≈ 1:{Fmt(Math.Round(1 / (esc / 96 * 2.54) / 5) * 5, "0")} (na tela)", 9.5, suave);
            dc.DrawText(fte, new WPoint(8, H - fte.Height - 4));
        }

        private static void Cota(DrawingContext dc, WPoint a, WPoint b, string txt, Pen pen, Func<string, double, Brush, bool, FormattedText> ft, Brush br, bool horizontal)
        {
            dc.DrawLine(pen, a, b);
            foreach (var p in new[] { a, b })
                dc.DrawLine(pen, new WPoint(p.X - 3, p.Y + 3), new WPoint(p.X + 3, p.Y - 3));
            var f = ft(txt, 10, br, false);
            if (horizontal) dc.DrawText(f, new WPoint((a.X + b.X) / 2 - f.Width / 2, a.Y - f.Height - 1));
            else
            {
                dc.PushTransform(new RotateTransform(-90, (a.X + b.X) / 2, (a.Y + b.Y) / 2));
                dc.DrawText(f, new WPoint((a.X + b.X) / 2 - f.Width / 2, (a.Y + b.Y) / 2 - f.Height - 2));
                dc.Pop();
            }
        }

        private static double Dist((double X, double Z) a, (double X, double Z) b) => Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Z - a.Z, 2));

        private static (double X, double Z) Ponto(IReadOnlyList<(double X, double Z)> p, double d)
        {
            for (int i = 1; i < p.Count; i++)
            {
                double l = Dist(p[i - 1], p[i]);
                if (d <= l || i == p.Count - 1) { double u = l > 0 ? Math.Min(1, d / l) : 0; return (p[i - 1].X + (p[i].X - p[i - 1].X) * u, p[i - 1].Z + (p[i].Z - p[i - 1].Z) * u); }
                d -= l;
            }
            return p[p.Count - 1];
        }

        private static string Fmt(double v, string f) => v.ToString(f, Pt);
    }
}
