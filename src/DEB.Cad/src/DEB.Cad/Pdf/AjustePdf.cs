using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DEB.Cad.Pdf;

/// <summary>
/// Orientação do PDF da PDF24. A PDF24 (Ghostscript) grava toda folha em pé: um formulário deitado (A0++ 1486,3 × 841) sai com
/// MediaBox 841 × 1486,3 e o desenho girado 90° no sentido anti-horário; no A4 (formulário em pé) com o desenho deitado o CAD
/// gira 90° no sentido horário. Para o PDF abrir deitado como a folha, a página recebe /Rotate 90 ou 270 (giro de exibição, sem
/// mexer no conteúdo). O trecho "/MediaBox [..]\n/Rotate 0" é reescrito com o MESMO número de bytes, então a tabela de
/// referências (xref) continua válida.
/// </summary>
internal static class AjustePdf
{
    private static readonly Regex Pagina = new Regex(
        @"(?<=/Type\s*/Page\b[^>]*?)/MediaBox\s*\[\s*0\s+0\s+(?<w>[\d.]+)\s+(?<h>[\d.]+)\s*\]\s*/Rotate 0(?=[/\s>])",
        RegexOptions.CultureInvariant);

    /// <summary>Marca /Rotate <paramref name="angulo"/> (90 ou 270) nas páginas em pé. Devolve quantas páginas foram giradas.</summary>
    public static int Deitar(string arquivo, int angulo)
    {
        var latin1 = Encoding.GetEncoding(28591);
        var texto = latin1.GetString(File.ReadAllBytes(arquivo));
        int n = 0;
        var novo = Pagina.Replace(texto, m =>
        {
            double w = double.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture);
            double h = double.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
            if (h <= w) return m.Value;
            var caixa = $"/MediaBox[0 0 {m.Groups["w"].Value} {m.Groups["h"].Value}]";
            var giro = "/Rotate " + angulo.ToString(CultureInfo.InvariantCulture);
            int sobra = m.Length - caixa.Length - giro.Length;
            if (sobra < 0) return m.Value; // não cabe sem mexer no xref: deixa como está
            n++;
            return caixa + new string(' ', sobra) + giro;
        });
        if (n > 0) File.WriteAllBytes(arquivo, latin1.GetBytes(novo));
        return n;
    }
}
