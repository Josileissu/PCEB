using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DEB.Core.Geometria.Secao;
using DEB.Core.Motor;
using Xunit;

namespace DEB.Core.Tests.Geometria;

/// <summary>
/// Prévia do arranjo por família (D26, D31) para o dono conferir no olho: com ARM_PREVIA=&lt;pasta&gt;, grava em
/// &lt;pasta&gt;\secoes\ um SVG por caso (contorno, estribos pelo eixo, barras numeradas, cobrimento medido em cada lado do
/// estribo, lista das barras, estribos com o comprimento e avisos). Sem a variável, não faz nada. O mecanismo JSON de
/// <see cref="PreviaTemplateTests"/> é de prancha de template (ResultadoTemplate); aqui é seção solta, então SVG sem biblioteca.
/// </summary>
public class PreviaSecoesTests
{
    private static Dictionary<ParteSecao, double> Cob(params (ParteSecao P, double C)[] c) => c.ToDictionary(x => x.P, x => x.C);

    [Fact]
    public void GravarPreviaDasSecoes()
    {
        var raiz = Environment.GetEnvironmentVariable("ARM_PREVIA");
        if (string.IsNullOrEmpty(raiz)) return;
        var pasta = Path.Combine(raiz, "secoes");
        Directory.CreateDirectory(pasta);
        var t = Cob((ParteSecao.Topo, 3), (ParteSecao.Fundo, 2.5), (ParteSecao.Esquerda, 3.5), (ParteSecao.Direita, 4),
                    (ParteSecao.LateralAlmaEsquerda, 3), (ParteSecao.LateralAlmaDireita, 3));
        var l = Cob((ParteSecao.Topo, 3), (ParteSecao.Fundo, 3), (ParteSecao.Esquerda, 3), (ParteSecao.Direita, 2.5),
                    (ParteSecao.LateralAlmaEsquerda, 3), (ParteSecao.LateralAlmaDireita, 3));
        var i = Cob((ParteSecao.Topo, 3), (ParteSecao.Fundo, 3), (ParteSecao.LateralAlmaEsquerda, 2.5), (ParteSecao.LateralAlmaDireita, 2.5),
                    (ParteSecao.MesaSuperior, 2.5), (ParteSecao.MesaInferior, 3.5));
        var q = Cob((ParteSecao.Topo, 3), (ParteSecao.Fundo, 3), (ParteSecao.Esquerda, 3), (ParteSecao.Direita, 3));
        var lv = new Dictionary<string, double> { ["bw"] = 25, ["h"] = 60, ["hf"] = 20, ["ba"] = 30 };
        var casos = new (string Arquivo, SecaoTransversal S, Dictionary<ParteSecao, double> C, int NB, int NH, double PhiF)[]
        {
            ("T.svg", SecaoTransversal.T(20, 60, 15, 20, 20), t, 0, 0, 10),
            ("T_invertida.svg", SecaoTransversal.T(20, 60, 15, 20, 20, invertida: true), t, 0, 0, 10),
            ("L_aba_embaixo_direita.svg", SecaoTransversal.DaFamilia(FamiliasSecao.L, lv, new OrientacaoSecao(false, false)), l, 0, 0, 10),
            ("L_aba_embaixo_esquerda.svg", SecaoTransversal.DaFamilia(FamiliasSecao.L, lv, new OrientacaoSecao(true, false)), l, 0, 0, 10),
            ("L_aba_em_cima_direita.svg", SecaoTransversal.DaFamilia(FamiliasSecao.L, lv, new OrientacaoSecao(false, true)), l, 0, 0, 10),
            ("L_aba_em_cima_esquerda.svg", SecaoTransversal.DaFamilia(FamiliasSecao.L, lv, new OrientacaoSecao(true, true)), l, 0, 0, 10),
            ("I.svg", SecaoTransversal.I(12, 80, 15, 19, 19, 20, 14, 14), i, 0, 2, 10),
            ("trapezoidal.svg", SecaoTransversal.Trapezoidal(20, 30, 40), q, 1, 1, 16),
            ("trapezoidal_invertida.svg", SecaoTransversal.DaFamilia(FamiliasSecao.Trapezoidal,
                new Dictionary<string, double> { ["bi"] = 20, ["bs"] = 30, ["h"] = 40 }, OrientacaoSecao.Invertida), q, 1, 1, 16),
        };
        foreach (var c in casos)
        {
            var a = SecaoBarras.Posicoes(c.S, c.C, 8, 20, 1, 0, 20, c.NB, c.NH, c.PhiF);
            File.WriteAllText(Path.Combine(pasta, c.Arquivo), Svg(c.S, c.C, a, c.NB, c.NH, c.PhiF), new UTF8Encoding(false));
        }
    }

    private static string N(double x) => x.ToString("0.##", CultureInfo.InvariantCulture);
    private static string V(double x) => x.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');
    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string Svg(SecaoTransversal s, Dictionary<ParteSecao, double> cob, ArranjoSecao a, int nB, int nH, double phiF)
    {
        const double k = 10, m = 70;   // px por cm; margem
        var c = s.Contorno().Contorno;
        double bb = s.B, hh = s.H;
        string P(double y, double z) => $"{N(m + y * k)},{N(m + (hh - z) * k)}";
        var linhas = new List<string>
        {
            s.Descricao,
            "Cobrimentos informados: " + string.Join(", ", cob.Select(x => $"{x.Key} {V(x.Value)}")),
            $"φt 8 · φ canto 20 · intermediárias nFaceB {nB}, nFaceH {nH}, φ {V(phiF)}" + (a.Definido ? "" : " · NÃO DEFINIDO: " + a.Erro),
            "",
            "Barras (x; y em cm a partir do canto inferior esquerdo da caixa):",
        };
        linhas.AddRange(a.Barras.Select((b, j) => $"  {j + 1}: ({V(b.X)}; {V(b.Y)}) ø{V(b.Bitola)} {b.Grupo} {(b.Parte.HasValue ? b.Parte.ToString() : "")}"));
        linhas.Add("Estribos (eixo):");
        linhas.AddRange(a.Estribos.Select(e => $"  {(e.Parte.HasValue ? e.Parte.ToString() : "seção")}: ø{V(e.Bitola)} {e.Aco} C = {V(e.Comprimento)} cm; " +
                                                string.Join(" ", e.Eixo.Select(p => $"({V(p.X)}; {V(p.Y)})"))));
        linhas.Add("Avisos:" + (a.Avisos.Count == 0 ? " nenhum" : ""));
        linhas.AddRange(a.Avisos.Select(x => "  " + x));

        double w = Math.Max(bb * k + 2 * m, 900), hDes = hh * k + 2 * m, hTot = hDes + 16 * linhas.Count + 20;
        var sb = new StringBuilder();
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(w)}\" height=\"{N(hTot)}\" font-family=\"Segoe UI, Arial, sans-serif\">");
        sb.AppendLine($"<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
        sb.AppendLine($"<polygon points=\"{string.Join(" ", c.Select(p => P(p.Y, p.Z)))}\" fill=\"#e6e6e6\" stroke=\"#333\" stroke-width=\"1.5\"/>");
        var cores = new[] { "#1f6fb4", "#d07a00", "#2a9d4a", "#8e44ad" };
        var cotas = new HashSet<string>();   // um cobrimento por ponto da face (lados comuns a dois estribos)
        for (int j = 0; j < a.Estribos.Count; j++)
        {
            var e = a.Estribos[j];
            string cor = cores[j % cores.Length];
            sb.AppendLine($"<polygon points=\"{string.Join(" ", e.Eixo.Select(p => P(p.X, p.Y)))}\" fill=\"none\" stroke=\"{cor}\" stroke-opacity=\"0.6\" stroke-width=\"{N(e.Bitola / 10 * k)}\"/>");
            var p0 = e.Eixo[0];
            sb.AppendLine($"<text x=\"{N(m + p0.X * k + 4)}\" y=\"{N(m + (hh - p0.Y) * k - 4)}\" font-size=\"10\" fill=\"{cor}\">{Esc(e.Parte?.ToString() ?? "estribo")}</text>");
            // cobrimento medido: de um ponto de cada lado do estribo (meio; senão perto das pontas) até a face paralela mais
            // próxima, menos φt/2
            for (int i = 0; i < e.Eixo.Count; i++)
            {
                var u = e.Eixo[i]; var v = e.Eixo[(i + 1) % e.Eixo.Count];
                double ly = v.X - u.X, lz = v.Y - u.Y, ll = Math.Sqrt(ly * ly + lz * lz);
                double my = 0, mz = 0;
                (double D, double Y, double Z)? melhor = null;
                foreach (var fr in new[] { 0.5, 0.15, 0.85 })
                {
                my = u.X + fr * ly; mz = u.Y + fr * lz;
                for (int q = 0; q < c.Count; q++)
                {
                    var f0 = c[q]; var f1 = c[(q + 1) % c.Count];
                    double fy = f1.Y - f0.Y, fz = f1.Z - f0.Z, fl = Math.Sqrt(fy * fy + fz * fz);
                    if (Math.Abs(ly * fz - lz * fy) / (ll * fl) > 1e-6) continue;   // só a face paralela
                    double tt = ((my - f0.Y) * fy + (mz - f0.Z) * fz) / (fl * fl);
                    if (tt < 0 || tt > 1) continue;
                    double py = f0.Y + tt * fy, pz = f0.Z + tt * fz, d = Math.Sqrt((py - my) * (py - my) + (pz - mz) * (pz - mz));
                    if (d <= 10 && (melhor == null || d < melhor.Value.D)) melhor = (d, py, pz);
                }
                if (melhor != null) break;
                }
                if (melhor == null || !cotas.Add($"{Math.Round(melhor.Value.Y, 1)}|{Math.Round(melhor.Value.Z, 1)}")) continue;
                double t2 = e.Bitola / 20, cobr = melhor.Value.D - t2;
                var (fyy, fzz) = (melhor.Value.Y, melhor.Value.Z);
                double ey = my + (fyy - my) / melhor.Value.D * t2, ez = mz + (fzz - mz) / melhor.Value.D * t2;
                sb.AppendLine($"<line x1=\"{N(m + fyy * k)}\" y1=\"{N(m + (hh - fzz) * k)}\" x2=\"{N(m + ey * k)}\" y2=\"{N(m + (hh - ez) * k)}\" stroke=\"#c0392b\" stroke-width=\"1.5\"/>");
                double ty = fyy + (fyy - my) / melhor.Value.D * 1.2, tz = fzz + (fzz - mz) / melhor.Value.D * 1.2;
                sb.AppendLine($"<text x=\"{N(m + ty * k)}\" y=\"{N(m + (hh - tz) * k + 3)}\" font-size=\"9\" fill=\"#c0392b\" text-anchor=\"middle\">c={V(Math.Round(cobr, 2))}</text>");
            }
        }
        for (int j = 0; j < a.Barras.Count; j++)
        {
            var b = a.Barras[j];
            sb.AppendLine($"<circle cx=\"{N(m + b.X * k)}\" cy=\"{N(m + (hh - b.Y) * k)}\" r=\"{N(b.Bitola / 20 * k)}\" fill=\"#222\"/>");
            sb.AppendLine($"<text x=\"{N(m + b.X * k + b.Bitola / 20 * k + 1)}\" y=\"{N(m + (hh - b.Y) * k - b.Bitola / 20 * k)}\" font-size=\"9\" fill=\"#000\">{j + 1}</text>");
        }
        double y0 = hDes + 10;
        for (int j = 0; j < linhas.Count; j++)
            sb.AppendLine($"<text x=\"10\" y=\"{N(y0 + 16 * j)}\" font-size=\"12\" fill=\"#000\" xml:space=\"preserve\">{Esc(linhas[j])}</text>");
        sb.AppendLine("</svg>");
        return sb.ToString();
    }
}
