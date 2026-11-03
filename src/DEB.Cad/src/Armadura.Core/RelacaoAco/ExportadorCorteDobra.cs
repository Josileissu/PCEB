using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using Armadura.Core.Calculo;
using Armadura.Core.Configuracao;
using Armadura.Core.Modelo;

namespace Armadura.Core.RelacaoAco;

/// <summary>
/// Lista de corte e dobra para o fornecedor de aço cortado e dobrado (seção 6): uma linha por posição
/// com a forma (medidas de cada segmento e ângulos). Treliças não entram (não são cortadas e dobradas).
/// </summary>
public static class ExportadorCorteDobra
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    private static IEnumerable<LinhaRA> Posicoes(ResultadoRA r) =>
        r.Linhas.Where(l => !l.EhTrelica).OrderBy(l => l.Elemento, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.Numero);

    /// <summary>Letras dos segmentos: A, B, C… (AA, AB… depois do Z).</summary>
    public static string Letra(int i) => i < 26 ? ((char)('A' + i)).ToString() : Letra(i / 26 - 1) + Letra(i % 26);

    /// <summary>"A=12; B=961; C=12" (cm, arredondados como na relação).</summary>
    public static string Segmentos(LinhaRA l, ConfiguracaoArmadura cfg) =>
        l.Forma == null ? "" : string.Join("; ", l.Forma.Select((s, i) =>
            Letra(i) + "=" + CalculoComprimento.Numero(CalculoComprimento.Arredondar(s.Comprimento, cfg.CasasDecimais, cfg.Arredondamento), cfg.CasasDecimais)
            + (s.Tipo == TipoSegmento.Arco ? " (arco)" : "")));

    /// <summary>"B:90; C:90" — ângulo de dobra (graus) no início de cada segmento.</summary>
    public static string Angulos(LinhaRA l) =>
        l.Forma == null ? "" : string.Join("; ", l.Forma.Select((s, i) => (s, i)).Where(x => x.i > 0 && Math.Abs(x.s.Deflexao) > 0.5)
            .Select(x => Letra(x.i) + ":" + Math.Round(Math.Abs(x.s.Deflexao)).ToString(CultureInfo.InvariantCulture)));

    public static double Peso(LinhaRA l, ConfiguracaoArmadura cfg) =>
        Math.Round(l.ComprimentoTotal / 100.0 * cfg.Barras.MassaLinear(l.Bitola, l.Aco), 2);

    /// <summary>CSV para Excel em português (";" e vírgula decimal), UTF-8 com BOM.</summary>
    public static string Csv(ResultadoRA r, ConfiguracaoArmadura cfg)
    {
        var sb = new StringBuilder();
        string C(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        sb.AppendLine("ELEMENTO;N;AÇO;BITOLA (mm);QUANT;COMPR. UNIT (cm);COMPR. TOTAL (m);PESO (kg);SEGMENTOS (cm);ÂNGULOS (graus);FECHADA");
        foreach (var l in Posicoes(r))
        {
            sb.Append(C(l.Elemento)).Append(';')
              .Append(l.Numero.ToString(Br)).Append(';')
              .Append(C(l.Aco)).Append(';')
              .Append(l.Bitola.ToString("0.0", Br)).Append(';')
              .Append(l.Quantidade.ToString(Br)).Append(';')
              .Append(l.Var ? "VAR" : l.ComprimentoUnitario.ToString("0.##", Br)).Append(';')
              .Append((l.ComprimentoTotal / 100.0).ToString("0.00", Br)).Append(';')
              .Append(Peso(l, cfg).ToString("0.00", Br)).Append(';')
              .Append(C(Segmentos(l, cfg))).Append(';')
              .Append(C(Angulos(l))).Append(';')
              .Append(l.Fechada ? "SIM" : "NÃO")
              .AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>Página HTML com uma linha por posição e o desenho da forma (SVG).</summary>
    public static string Html(ResultadoRA r, ConfiguracaoArmadura cfg, string titulo)
    {
        string E(string s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\">");
        sb.AppendLine($"<title>{E(titulo)}</title>");
        sb.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222}" +
                      "table{border-collapse:collapse}th,td{border:1px solid #999;padding:4px 8px;text-align:center}" +
                      "th{background:#eee}td.forma{padding:2px}svg text{font-family:Arial;font-size:11px}" +
                      "@media print{body{margin:8mm}}</style></head><body>");
        sb.AppendLine($"<h2>{E(titulo)}</h2>");
        sb.AppendLine("<p>Lista de corte e dobra — medidas externas em cm. Conferir com o projeto antes de produzir.</p>");
        sb.AppendLine("<table><tr><th>ELEMENTO</th><th>N</th><th>AÇO</th><th>Ø (mm)</th><th>QUANT</th>" +
                      "<th>UNIT (cm)</th><th>TOTAL (m)</th><th>PESO (kg)</th><th>SEGMENTOS (cm)</th><th>DETALHE DE DOBRA</th></tr>");
        double pesoTotal = 0;
        foreach (var l in Posicoes(r))
        {
            double peso = Peso(l, cfg);
            pesoTotal += peso;
            sb.Append("<tr>")
              .Append($"<td>{E(string.IsNullOrEmpty(l.Elemento) ? "-" : l.Elemento)}</td><td>{l.Numero}</td><td>{E(l.Aco)}</td>")
              .Append($"<td>{l.Bitola.ToString("0.0", Br)}</td><td>{l.Quantidade}</td>")
              .Append($"<td>{(l.Var ? "VAR" : l.ComprimentoUnitario.ToString("0.##", Br))}</td>")
              .Append($"<td>{(l.ComprimentoTotal / 100.0).ToString("0.00", Br)}</td><td>{peso.ToString("0.00", Br)}</td>")
              .Append($"<td>{E(Segmentos(l, cfg))}</td>")
              .Append($"<td class=\"forma\">{Svg(l, cfg)}</td></tr>");
            sb.AppendLine();
        }
        sb.AppendLine($"<tr><th colspan=\"7\">PESO TOTAL (kg)</th><th>{pesoTotal.ToString("0.00", Br)}</th><th colspan=\"2\"></th></tr>");
        sb.AppendLine("</table></body></html>");
        return sb.ToString();
    }

    /// <summary>Desenho da forma em SVG (mesmo esquema do detalhe de dobra da tabela).</summary>
    public static string Svg(LinhaRA l, ConfiguracaoArmadura cfg, double largura = 320, double altura = 90)
    {
        if (l.Forma == null || l.Forma.Count == 0) return "";
        var e = EsquemaDobra.Gerar(l.Forma, l.Fechada, largura, altura, 11,
            (i, c) => CalculoComprimento.TextoCota(c, false, cfg));
        string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        // SVG: y para baixo → inverte o y do esquema
        var d = new StringBuilder();
        var v0 = e.Vertices[0];
        d.Append($"M {N(v0.X)} {N(-v0.Y)}");
        for (int i = 1; i < e.Vertices.Count; i++) Trecho(d, e.Vertices[i - 1], e.Vertices[i], N);
        if (e.Fechada) { Trecho(d, e.Vertices[e.Vertices.Count - 1], e.Vertices[0], N); d.Append(" Z"); }
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(largura)}\" height=\"{N(altura)}\" " +
                  $"viewBox=\"{N(-largura / 2)} {N(-altura / 2)} {N(largura)} {N(altura)}\">");
        sb.Append($"<path d=\"{d}\" fill=\"none\" stroke=\"#c00\" stroke-width=\"1.5\"/>");
        foreach (var r in e.Rotulos)
        {
            double graus = -r.Rotacao * 180 / Math.PI;
            sb.Append($"<text x=\"{N(r.X)}\" y=\"{N(-r.Y)}\" text-anchor=\"middle\" dominant-baseline=\"middle\" " +
                      $"transform=\"rotate({N(graus)} {N(r.X)} {N(-r.Y)})\">{WebUtility.HtmlEncode(r.Texto)}</text>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>Reta ou arco (pelo bulge) de a até b, no sistema do SVG (y invertido).</summary>
    private static void Trecho(StringBuilder d, (double X, double Y, double Bulge) a, (double X, double Y, double Bulge) b, Func<double, string> n)
    {
        if (Math.Abs(a.Bulge) < 1e-9) { d.Append($" L {n(b.X)} {n(-b.Y)}"); return; }
        double corda = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        double theta = 4 * Math.Atan(Math.Abs(a.Bulge));
        double raio = corda / (2 * Math.Sin(theta / 2));
        int grande = theta > Math.PI ? 1 : 0;
        // bulge > 0 = anti-horário no CAD; com o y invertido, vira horário no SVG (sweep = 0)
        int sweep = a.Bulge > 0 ? 0 : 1;
        d.Append($" A {n(raio)} {n(raio)} 0 {grande} {sweep} {n(b.X)} {n(-b.Y)}");
    }
}
