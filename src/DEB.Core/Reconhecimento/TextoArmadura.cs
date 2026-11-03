using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DEB.Core.Reconhecimento;

/// <summary>Dados lidos do texto de uma barra desenhada por outro programa ("25 N1 ø10.0 c/15 C=300").</summary>
public sealed class TextoBarra
{
    /// <summary>0 = o texto não traz a quantidade.</summary>
    public int Quantidade { get; set; }
    public int Repeticoes { get; set; } = 1;
    /// <summary>0 = o texto não traz o N.</summary>
    public int Numero { get; set; }
    public double Bitola { get; set; }
    public double Espacamento { get; set; }
    /// <summary>Comprimento (cm) do "C="; null = sem C= ou VAR.</summary>
    public double? Comprimento { get; set; }
    public bool Var { get; set; }
    public string Original { get; set; } = "";
}

/// <summary>
/// Reconhecimento de barras em DWG de outros programas (Eberick, TQS, Cype, QiArmaduras, desenho à mão):
/// interpreta os textos e associa cada texto à polilinha mais próxima. Sem CAD: as distâncias vêm de fora.
/// </summary>
public static class TextoArmadura
{
    private const RegexOptions Op = RegexOptions.CultureInvariant;
    private const string Num = @"(\d+(?:[.,]\d+)?)";

    private static readonly Regex RxBitola = new Regex(@"ø\s*" + Num, Op);
    private static readonly Regex RxNumero = new Regex(@"(?<![A-Za-z])N\s*[°º]?\s*(\d+)", Op);
    private static readonly Regex RxEspac = new Regex(@"c\s*/\s*" + Num, Op | RegexOptions.IgnoreCase);
    private static readonly Regex RxComp = new Regex(@"(?<![A-Za-z])C\s*=\s*(VAR|" + Num.Substring(1, Num.Length - 2) + ")", Op | RegexOptions.IgnoreCase);
    // "2x11 N1", "2x(11 N1", "2 x 4ø8"
    private static readonly Regex RxRepeticao = new Regex(@"(?<![\d.,])(\d+)\s*[xX]\s*\(?\s*(\d+)(?=\s*(?:N|ø))", Op);
    // quantidade logo antes do N ou do ø: "25 N1", "4ø10", "25N1"
    private static readonly Regex RxQtdAntes = new Regex(@"(?<![\w.,/=])(\d+)\s*(?=N\s*\d|ø)", Op);
    // quantidade entre o N e o ø: "N5 22ø5.0", "N3 – 4 ø8"
    private static readonly Regex RxQtdDepoisN = new Regex(@"N\s*\d+(?:\s*[-–—:]\s*|\s+)(\d+)\s*(?=ø)", Op);
    private static readonly Regex RxSoNumero = new Regex(@"^\s*" + Num + @"\s*(?:cm)?\s*$", Op | RegexOptions.IgnoreCase);

    /// <summary>Tira a formatação do MText (\P, {\f…;}, \H…;, \S…;) e unifica os símbolos de diâmetro.</summary>
    public static string Limpar(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = Regex.Replace(s, @"\\[Pp~]", " ");
        s = Regex.Replace(s, @"\\U\+([0-9A-Fa-f]{4})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
        s = Regex.Replace(s, @"\\S([^;]*?)[#^/]([^;]*);", "$1/$2");
        s = Regex.Replace(s, @"\\[A-Za-z][^;\\{}]*;", "");
        s = Regex.Replace(s, @"\\[LlOoKk]", "");
        s = s.Replace("{", "").Replace("}", "");
        s = Regex.Replace(s, "%%[cC]", "ø");
        foreach (var c in new[] { "Ø", "φ", "ϕ", "∅", "Φ", "\u00D8" }) s = s.Replace(c, "ø");
        s = Regex.Replace(s, @"%%[dDpP]", "");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    /// <summary>Texto de barra (tem diâmetro e N, quantidade ou C=); null se não for.</summary>
    public static TextoBarra Interpretar(string texto)
    {
        var s = Limpar(texto);
        var mb = RxBitola.Match(s);
        if (!mb.Success) return null;
        double bitola = Real(mb.Groups[1].Value);
        if (bitola < 3 || bitola > 50) return null;
        var t = new TextoBarra { Bitola = bitola, Original = s };

        var mn = RxNumero.Match(s);
        if (mn.Success) t.Numero = int.Parse(mn.Groups[1].Value, CultureInfo.InvariantCulture);

        var mr = RxRepeticao.Match(s);
        if (mr.Success)
        {
            t.Repeticoes = Math.Max(1, int.Parse(mr.Groups[1].Value, CultureInfo.InvariantCulture));
            t.Quantidade = int.Parse(mr.Groups[2].Value, CultureInfo.InvariantCulture);
        }
        else
        {
            var mq = RxQtdAntes.Match(s);
            if (mq.Success && mq.Index < mb.Index) t.Quantidade = int.Parse(mq.Groups[1].Value, CultureInfo.InvariantCulture);
            else
            {
                var md = RxQtdDepoisN.Match(s);
                if (md.Success) t.Quantidade = int.Parse(md.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }

        var me = RxEspac.Match(s);
        if (me.Success) t.Espacamento = Real(me.Groups[1].Value);

        var mc = RxComp.Match(s);
        if (mc.Success)
        {
            if (mc.Groups[1].Value.Equals("VAR", StringComparison.OrdinalIgnoreCase)) t.Var = true;
            else t.Comprimento = Real(mc.Groups[1].Value);
        }

        if (t.Numero == 0 && t.Quantidade == 0 && !mc.Success) return null; // "ø10" solto não basta
        return t;
    }

    /// <summary>Texto que é só um número (medida de um trecho da barra: "35", "42,5", "120 cm").</summary>
    public static bool EhMedida(string texto, out double valor)
    {
        valor = 0;
        var m = RxSoNumero.Match(Limpar(texto));
        if (!m.Success) return false;
        valor = Real(m.Groups[1].Value);
        return valor > 0;
    }

    /// <summary>A medida escrita confere com o trecho desenhado (arredondamento a 1 cm, ou 2 %).</summary>
    public static bool MedidaConfere(double escrita, double desenhada) =>
        Math.Abs(escrita - desenhada) <= Math.Max(1.0, 0.02 * desenhada);

    /// <summary>O C= do texto confere com o comprimento desenhado (5 %, mínimo 3 cm: o C= pode ser o real).</summary>
    public static bool ComprimentoConfere(double? c, double desenhado) =>
        c.HasValue && Math.Abs(c.Value - desenhado) <= Math.Max(3.0, 0.05 * desenhado);

    /// <summary>
    /// Associação um-para-um pelo menor custo (guloso): cada candidato (a, b, custo) liga o item a ao b;
    /// cada a e cada b entram uma vez só.
    /// </summary>
    public static List<(int A, int B)> Associar(IEnumerable<(int A, int B, double Custo)> candidatos)
    {
        var r = new List<(int, int)>();
        var usadosA = new HashSet<int>();
        var usadosB = new HashSet<int>();
        foreach (var c in candidatos.OrderBy(c => c.Custo).ThenBy(c => c.A).ThenBy(c => c.B))
        {
            if (usadosA.Contains(c.A) || usadosB.Contains(c.B)) continue;
            usadosA.Add(c.A);
            usadosB.Add(c.B);
            r.Add((c.A, c.B));
        }
        return r;
    }

    private static double Real(string s) => double.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);
}
