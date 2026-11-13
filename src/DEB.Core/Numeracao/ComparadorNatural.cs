using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Numeracao;

/// <summary>
/// Ordem "natural" das marcas: os trechos numéricos comparam pelo valor (P2 antes de P10; V101 antes de V1010), o resto sem
/// diferenciar maiúsculas. Usada nas tabelas das pranchas de formas (o Revit ordena texto caractere a caractere: P10 antes de P2).
/// </summary>
public sealed class ComparadorNatural : IComparer<string>
{
    public static readonly ComparadorNatural Instancia = new ComparadorNatural();

    public int Compare(string a, string b)
    {
        a ??= ""; b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int i0 = i, j0 = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                string na = a.Substring(i0, i - i0).TrimStart('0'), nb = b.Substring(j0, j - j0).TrimStart('0');
                if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                int c = string.CompareOrdinal(na, nb);
                if (c != 0) return c;
                continue;
            }
            int d = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
            if (d != 0) return d;
            i++; j++;
        }
        int r = (a.Length - i).CompareTo(b.Length - j);
        return r != 0 ? r : string.CompareOrdinal(a, b);
    }

    /// <summary>Posição (1, 2, 3...) de cada marca na ordem natural; marcas iguais (sem diferenciar maiúsculas) têm a mesma posição.
    /// Marcas vazias ficam de fora.</summary>
    public static Dictionary<string, int> Posicoes(IEnumerable<string> marcas)
    {
        var r = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int n = 0;
        foreach (string m in marcas.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, Instancia))
            r[m] = ++n;
        return r;
    }
}
