using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D;
using DEB.Core.Motor;

namespace DEB.Core.Elementos.Viga.PreMoldada.Modelo3D;

/// <summary>
/// Desenho da armação proposta para o relatório (D40): elevação e seções (no dente e no meio do vão) em SVG, feitos das MESMAS
/// barras 3D que vão para o Revit (<see cref="VigaRebarLayout"/>) — a prévia e a aplicação saem do mesmo modelo (D26).
/// </summary>
public static class DesenhoArmacaoSvg
{
    private static double Cm(double ft) => ft * 30.48;
    private static string F(double x) => x.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Cor(BarRole r) => r switch
    {
        BarRole.Longitudinal or BarRole.BeamWelded => "#c01f1f",
        BarRole.Stirrup => "#2e8b57",
        BarRole.BeamGerber or BarRole.BeamHanger => "#d48a1a",
        BarRole.LiftLoop => "#8e44ad",
        BarRole.BeamLedge or BarRole.BeamLedgeHairpin or BarRole.BeamLedgeBar or BarRole.BeamUpstand or BarRole.BeamUpstandBar or BarRole.BeamTopping => "#0e8a8a",
        _ => "#1f5aa6",
    };

    public static readonly (string Texto, string Cor)[] Legenda =
    {
        ("Longitudinal", "#c01f1f"), ("Estribo", "#2e8b57"), ("Dente: estribos e suspensão", "#d48a1a"),
        ("Dente: tirante, grampo e costura", "#1f5aa6"), ("Alça CP-190", "#8e44ad"), ("Aba e ressalto", "#0e8a8a"),
    };

    /// <summary>Elevação da viga com as barras (escala vertical ampliada até 3× para a viga baixa e comprida caber).</summary>
    public static string Elevacao(ValoresTemplate v, IReadOnlyList<BarDef> bars, double z0, int largura = 1000)
    {
        double h = v.Numero("H"), L = v.Numero("L");
        var lix = new List<string>();
        var e = TemplateVigaPreMoldada.LerExtremidade(v, true, TemplateVigaPreMoldada.CatalogoDentes, lix);
        var d = TemplateVigaPreMoldada.LerExtremidade(v, false, TemplateVigaPreMoldada.CatalogoDentes, lix);
        double mx = 20, w = largura - 2 * mx, esc = w / L, escV = Math.Min(3 * esc, 220 / Math.Max(h, 1));
        var rs = TemplateVigaPreMoldada.LerRessalto(v, v.Numero("B"), h, null);
        double hr = rs.Existe ? rs.Altura : 0;
        double acima = Math.Max(hr, v.SimNao("Alcas") ? v.Numero("AlcaAcima") : 0);
        double yTop = 14 + acima * escV, alt = yTop + h * escV + 34;
        string X(double x) => F(mx + x * esc);
        string Y(double z) => F(yTop + (z0 + h - z) * escV);
        var sb = new StringBuilder();
        sb.Append($"<svg class=\"arm\" viewBox=\"0 0 {largura} {F(alt)}\" role=\"img\" aria-label=\"Elevação da armação\">");
        // forma com os recortes dos dentes
        var pts = new List<(double X, double Z)>();
        if (e.TemDente) pts.AddRange(new[] { (0.0, z0 + h - e.Dente.Altura), (e.CompDente, z0 + h - e.Dente.Altura), (e.CompDente, z0) });
        else pts.Add((0, z0));
        if (d.TemDente) pts.AddRange(new[] { (L - d.CompDente, z0), (L - d.CompDente, z0 + h - d.Dente.Altura), (L, z0 + h - d.Dente.Altura) });
        else pts.Add((L, z0));
        pts.Add((L, z0 + h)); pts.Add((0, z0 + h));
        sb.Append("<polygon class=\"forma\" points=\"" + string.Join(" ", pts.Select(p => $"{X(p.X)},{Y(p.Z)}")) + "\"/>");
        if (hr > 0) sb.Append($"<polygon class=\"forma\" points=\"{X(0)},{Y(z0 + h)} {X(L)},{Y(z0 + h)} {X(L)},{Y(z0 + h + hr)} {X(0)},{Y(z0 + h + hr)}\"/>");
        foreach (var b in bars)
        {
            string cor = Cor(b.Role);
            double esp = b.Role == BarRole.Stirrup ? 0.8 : 1.4;
            foreach (var inst in b.Instances())
            {
                // vista lateral: projeção em (A, C); U horizontais viram a linha da perna
                var q = inst.Select(p => (A: Cm(p.A), C: Cm(p.C))).ToList();
                var lin = new StringBuilder();
                for (int i = 0; i < q.Count; i++)
                {
                    lin.Append($"{X(q[i].A)},{Y(q[i].C)} ");
                    if (b.ArcMid.TryGetValue(i, out Loc m)) lin.Append($"{X(Cm(m.A))},{Y(Cm(m.C))} ");
                }
                if (b.Closed) lin.Append($"{X(q[0].A)},{Y(q[0].C)}");
                sb.Append($"<polyline fill=\"none\" stroke=\"{cor}\" stroke-width=\"{F(esp)}\" points=\"{lin}\"/>");
            }
        }
        double yc = yTop + h * escV + 20;
        sb.Append($"<line class=\"cota\" x1=\"{X(0)}\" y1=\"{F(yc)}\" x2=\"{X(L)}\" y2=\"{F(yc)}\"/>");
        sb.Append($"<text class=\"rot\" x=\"{X(L / 2)}\" y=\"{F(yc - 4)}\" text-anchor=\"middle\">{F(L)} cm</text>");
        if (escV > esc * 1.05) sb.Append($"<text class=\"rot\" x=\"{X(0)}\" y=\"{F(alt - 2)}\">escala vertical {F(escV / esc)}× a horizontal</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>Seção transversal em x (cm): forma da alma (ou do dente) e as barras cortadas; estribo do plano mais próximo.</summary>
    public static string Secao(ValoresTemplate v, IReadOnlyList<BarDef> bars, double x, double y0, double z0, string titulo, int tamanho = 260)
    {
        double b = v.Numero("B"), h = v.Numero("H"), L = v.Numero("L");
        var lix = new List<string>();
        var e = TemplateVigaPreMoldada.LerExtremidade(v, true, TemplateVigaPreMoldada.CatalogoDentes, lix);
        var d = TemplateVigaPreMoldada.LerExtremidade(v, false, TemplateVigaPreMoldada.CatalogoDentes, lix);
        double zb = z0;
        if (e.TemDente && x < e.CompDente) zb = z0 + h - e.Dente.Altura;
        if (d.TemDente && x > L - d.CompDente) zb = z0 + h - d.Dente.Altura;
        // forma: alma (ou dente) com a aba e o ressalto, pelo contorno da seção do CAD (x da seção = y0 + b − y)
        var aba = TemplateVigaPreMoldada.LerAba(v, h, L, e, d, null);
        var rs = TemplateVigaPreMoldada.LerRessalto(v, b, h, null);
        AbaViga ab = aba.Em(x) ? (zb > z0 ? aba.NoDente(zb - z0) : aba) : null;
        double hSec = z0 + h - zb, le = ab?.LargE ?? 0, ld = ab?.LargD ?? 0, hr = rs.Existe ? rs.Altura : 0;
        double baixo = ab != null ? Math.Min(0, ab.Base) : 0;   // aba pendurada abaixo do dente
        double esc = (tamanho - 50) / Math.Max(b + le + ld, hSec + hr - baixo), w = (b + le + ld) * esc + 60, alt = (hSec + hr - baixo) * esc + 50;
        string X(double y) => F(30 + (y0 + b - y + le) * esc);
        string Y(double z) => F(26 + (z0 + h + hr - z) * esc);
        var sb = new StringBuilder();
        sb.Append($"<svg class=\"arm\" viewBox=\"0 0 {F(w)} {F(alt)}\" role=\"img\" aria-label=\"{titulo}\">");
        sb.Append($"<text class=\"rot\" x=\"{F(w / 2)}\" y=\"14\" text-anchor=\"middle\">{titulo}</text>");
        var cont = TemplateVigaPreMoldada.ContornoSecao(b, hSec, ab, rs.Existe ? rs : null, 0, 0);
        sb.Append("<polygon class=\"forma\" points=\"" + string.Join(" ", cont.Select(q => $"{X(y0 + b - q.X)},{Y(zb + q.Y)}")) + "\"/>");
        // barras planas (estribos, grampos, ligação com a capa): de cada conjunto, a barra mais perto de x; uma por papel e lado
        var planos = bars.Where(bd => bd.Normal.A != 0 && bd.Role != BarRole.BeamWelded).ToList();
        var perto = new Dictionary<(BarRole, int), (double Dist, BarDef Bd, List<Loc> Inst)>();
        foreach (var bd in planos)
            foreach (var inst in bd.Instances())
            {
                double dd = Math.Abs(Cm(inst[0].A) - x);
                var chave = (bd.Role, Math.Sign(Math.Round(inst.Average(p => Cm(p.B)) - (y0 + b / 2))));
                if (dd < 30 && (!perto.TryGetValue(chave, out var o) || dd < o.Dist)) perto[chave] = (dd, bd, inst);
            }
        foreach (var (_, bd, inst) in perto.Values)
        {
            var lin = string.Join(" ", inst.Select(p => $"{X(Cm(p.B))},{Y(Cm(p.C))}"));
            if (bd.Closed) lin += $" {X(Cm(inst[0].B))},{Y(Cm(inst[0].C))}";
            sb.Append($"<polyline fill=\"none\" stroke=\"{Cor(bd.Role)}\" stroke-width=\"1.6\" points=\"{lin}\"/>");
        }
        // barras cortadas pelo plano x
        foreach (var bd in bars.Where(bd => bd.Normal.A == 0 && bd.Role != BarRole.LiftLoop))
            foreach (var inst in bd.Instances())
                for (int i = 0; i + 1 < inst.Count; i++)
                {
                    double a0 = Cm(inst[i].A), a1 = Cm(inst[i + 1].A);
                    if (Math.Abs(a1 - a0) < 1e-6 || x < Math.Min(a0, a1) || x > Math.Max(a0, a1)) continue;
                    double t = (x - a0) / (a1 - a0);
                    double yy = Cm(inst[i].B) + t * (Cm(inst[i + 1].B) - Cm(inst[i].B)), zz = Cm(inst[i].C) + t * (Cm(inst[i + 1].C) - Cm(inst[i].C));
                    double r = Math.Max(2.2, bd.DiameterMm / 20 * esc);
                    sb.Append($"<circle cx=\"{X(yy)}\" cy=\"{Y(zz)}\" r=\"{F(r)}\" fill=\"{Cor(bd.Role)}\"/>");
                }
        sb.Append($"<text class=\"rot\" x=\"{F(w / 2)}\" y=\"{F(alt - 4)}\" text-anchor=\"middle\">{F(b)} × {F(hSec)} cm{(ab != null || rs.Existe ? " (alma)" : "")}</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>Elevação, seção no dente (se houver) e seção no meio do vão, com a legenda — o bloco "Armação proposta" do relatório.</summary>
    public static List<(string Titulo, string Svg)> Figuras(ValoresTemplate v, IReadOnlyList<BarDef> bars, double y0, double z0)
    {
        double L = v.Numero("L");
        var lix = new List<string>();
        var e = TemplateVigaPreMoldada.LerExtremidade(v, true, TemplateVigaPreMoldada.CatalogoDentes, lix);
        var r = new List<(string, string)> { ("Elevação", Elevacao(v, bars, z0)) };
        if (e.TemDente) r.Add(("Seção no dente", Secao(v, bars, e.CompDente / 2, y0, z0, "SEÇÃO NO DENTE")));
        else r.Add(("Seção junto ao apoio", Secao(v, bars, Math.Min(L / 4, 30), y0, z0, "SEÇÃO JUNTO AO APOIO")));
        r.Add(("Seção no meio do vão", Secao(v, bars, L / 2, y0, z0, "SEÇÃO NO MEIO DO VÃO")));
        var leg = new StringBuilder("<div class=\"legenda\">");
        foreach (var (t, c) in Legenda) leg.Append($"<span><i style=\"background:{c}\"></i>{t}</span>");
        leg.Append("</div>");
        r.Add(("", leg.ToString()));
        return r;
    }
}
