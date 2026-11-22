using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DEB.Core.Documentacao;

/// <summary>Um passo da memória de cálculo: título e linhas de texto (fórmula com os números).</summary>
public sealed class PassoMemoria
{
    public string Titulo { get; set; } = "";
    public List<string> Linhas { get; set; } = new List<string>();
}

/// <summary>Diagrama de esforço para o relatório (x em cm ao longo da peça; y na unidade do diagrama).</summary>
public sealed class DiagramaRelatorio
{
    public string Titulo { get; set; } = "";
    public string Unidade { get; set; } = "";
    public List<(double X, double Y)> Pontos { get; set; } = new List<(double, double)>();
    /// <summary>Posições dos apoios (cm), marcadas embaixo do diagrama.</summary>
    public List<double> Apoios { get; set; } = new List<double>();
    /// <summary>Positivo para baixo (momento fletor, como se desenha no Brasil).</summary>
    public bool PositivoParaBaixo { get; set; }

    /// <summary>SVG do diagrama (sem dependências; cores por classe do CSS do relatório).</summary>
    public string Svg(int largura = 720, int altura = 190)
    {
        if (Pontos.Count < 2) return "";
        double x0 = Pontos.Min(p => p.X), x1 = Pontos.Max(p => p.X);
        double ymax = Math.Max(1e-9, Pontos.Max(p => Math.Abs(p.Y)));
        const int mx = 56, my = 26;
        double w = largura - 2 * mx, h = (altura - 2 * my) / 2.0, yb = altura / 2.0;
        double sinal = PositivoParaBaixo ? 1 : -1;
        string X(double x) => (mx + (x - x0) / Math.Max(1e-9, x1 - x0) * w).ToString("0.#", CultureInfo.InvariantCulture);
        string Y(double y) => (yb + sinal * y / ymax * h).ToString("0.#", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append($"<svg class=\"diag\" viewBox=\"0 0 {largura} {altura}\" role=\"img\" aria-label=\"{RelatorioHtml.H(Titulo)}\">");
        sb.Append($"<line class=\"eixo\" x1=\"{mx}\" y1=\"{yb}\" x2=\"{mx + w}\" y2=\"{yb}\"/>");
        sb.Append("<polygon class=\"area\" points=\"");
        sb.Append($"{X(Pontos[0].X)},{Y(0)} ");
        foreach (var p in Pontos) sb.Append($"{X(p.X)},{Y(p.Y)} ");
        sb.Append($"{X(Pontos[Pontos.Count - 1].X)},{Y(0)}\"/>");
        sb.Append("<polyline class=\"linha\" points=\"");
        foreach (var p in Pontos) sb.Append($"{X(p.X)},{Y(p.Y)} ");
        sb.Append("\"/>");
        foreach (double a in Apoios)
        {
            string xa = X(a);
            sb.Append($"<polygon class=\"apoio\" points=\"{xa},{yb} {(mx + (a - x0) / Math.Max(1e-9, x1 - x0) * w - 6).ToString("0.#", CultureInfo.InvariantCulture)},{yb + 10} {(mx + (a - x0) / Math.Max(1e-9, x1 - x0) * w + 6).ToString("0.#", CultureInfo.InvariantCulture)},{yb + 10}\"/>");
        }
        // valores extremos
        var pmax = Pontos.OrderByDescending(p => p.Y).First();
        var pmin = Pontos.OrderBy(p => p.Y).First();
        foreach (var p in new[] { pmax, pmin }.Distinct())
        {
            if (Math.Abs(p.Y) < 1e-9) continue;
            sb.Append($"<text class=\"rot\" x=\"{X(p.X)}\" y=\"{(yb + sinal * p.Y / ymax * h + (sinal * p.Y > 0 ? 14 : -6)).ToString("0.#", CultureInfo.InvariantCulture)}\" text-anchor=\"middle\">" +
                      $"{Fmt.Virgula(p.Y, "0.0")} {RelatorioHtml.H(Unidade)}</text>");
        }
        sb.Append($"<text class=\"rot\" x=\"{mx}\" y=\"{altura - 4}\">0</text>");
        sb.Append($"<text class=\"rot\" x=\"{mx + w}\" y=\"{altura - 4}\" text-anchor=\"end\">{Fmt.Virgula(x1 - x0, "0")} cm</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }
}
