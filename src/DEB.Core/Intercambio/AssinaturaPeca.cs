using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DEB.Core.Intercambio;

/// <summary>
/// "Impressão digital" dos dados de um grupo de peças importado do modelo (2026-11-10, reimportação): fica no comentário do
/// detalhamento criado pelo ARMIMPORTARPECAS. Ao importar de novo depois de mudar o modelo, o grupo com a mesma marca e a mesma
/// assinatura NÃO é redesenhado (o que o usuário ajustou no CAD fica); o que mudou é apontado, e o usuário escolhe substituir ou
/// manter os dois. Entram só os dados que vêm do modelo (medidas, dentes, aba, consolos, níveis, quantidade) — o PADRÃO e os
/// catálogos do usuário não entram.
/// </summary>
public static class AssinaturaPeca
{
    private static readonly Regex NoComentario = new Regex(@"dados #([0-9A-F]{8})", RegexOptions.CultureInvariant);

    public static string De(PilarPeca p)
    {
        var s = new List<string> { "P", N(p.B), N(p.H), N(p.L), N(p.CotaBase * 100), p.Embutimento.HasValue ? N(p.Embutimento.Value) : "-", I(p.Quantidade) };
        s.Add(p.Niveis == null ? "-" : string.Join(",", p.Niveis.OrderBy(n => n.Cota).Select(n => n.Nome + "@" + N(n.Cota * 100))));
        s.AddRange(p.Consolos.Select(c => $"{c.Face}:{N(c.Inicio)}:{N(c.Fim)}:{N(c.Base)}:{N(c.Topo)}:{N(c.Balanco)}:{N(c.AlturaPonta)}").OrderBy(x => x, StringComparer.Ordinal));
        return Hash(string.Join("|", s));
    }

    public static string De(VigaPeca v)
    {
        string D(DentePeca d) => d == null ? "R" : N(d.Altura) + "x" + N(d.Comprimento);
        string a = v.Aba == null ? "-" : $"{v.Aba.Lado}:{N(v.Aba.Largura)}:{N(v.Aba.Altura)}:{N(v.Aba.AlturaPonta > 0 ? v.Aba.AlturaPonta : v.Aba.Altura)}:{N(v.Aba.Base)}:{N(v.Aba.Inicio)}:{N(v.Aba.Fim)}";
        return Hash(string.Join("|", new[] { "V", N(v.B), N(v.H), N(v.L), D(v.DenteE), D(v.DenteD), a, I(v.Quantidade) }));
    }

    /// <summary>Comentário do detalhamento importado: origem, elementos do modelo e a assinatura dos dados.</summary>
    public static string Comentario(string origem, string data, IEnumerable<string> idsDosElementos, string assinatura)
    {
        string ids = string.Join(", ", idsDosElementos.Where(s => !string.IsNullOrEmpty(s)));
        return $"Importado de {origem} ({data})" + (ids.Length > 0 ? $"; elementos {ids}" : "") + $"; dados #{assinatura}.";
    }

    /// <summary>A assinatura gravada num comentário; null se o detalhamento não veio de uma importação (ou é de antes da assinatura).</summary>
    public static string DoComentario(string comentario)
    {
        var m = NoComentario.Match(comentario ?? "");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>O detalhamento foi criado por uma importação (com ou sem assinatura)?</summary>
    public static bool Importado(string comentario) => (comentario ?? "").StartsWith("Importado de ", StringComparison.Ordinal);

    // medidas ao milímetro, como as assinaturas dos grupos
    private static string N(double cm) => Fmt.Mm(cm);
    private static string I(int n) => n.ToString(CultureInfo.InvariantCulture);

    /// <summary>FNV-1a de 32 bits (estável entre versões do .NET, ao contrário do GetHashCode das strings).</summary>
    private static string Hash(string s)
    {
        uint h = 2166136261;
        foreach (char ch in s) { h ^= ch; h *= 16777619; }
        return h.ToString("X8", CultureInfo.InvariantCulture);
    }
}
