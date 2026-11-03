using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DEB.Core.Descricao;

/// <summary>
/// Quebras de linha que o usuário põe ao editar a descrição da barra ou o texto da faixa (Enter no editor,
/// pedido de 2026-10-27). Guardam-se como "depois da palavra k" (k = nº de palavras antes da quebra): o texto
/// continua sendo gerado (quantidade, N, C=… mudam) e as quebras são reaplicadas nos mesmos pontos.
/// </summary>
public static class QuebrasTexto
{
    private static readonly Regex CodigoComArgumento = new Regex(@"\\[fFhHwWqQtTaAcCpP][^;]*;", RegexOptions.Compiled);
    private static readonly Regex CodigoSimples = new Regex(@"\\[LlOoKkNn~]", RegexOptions.Compiled);

    /// <summary>
    /// Lê as quebras de um texto editado (conteúdo do MText ou texto da cota, com "\P" ou quebras reais).
    /// Os códigos de formatação do editor são ignorados.
    /// </summary>
    public static List<int> Ler(string editado)
    {
        var r = new List<int>();
        if (string.IsNullOrEmpty(editado)) return r;
        string s = editado.Replace("\\P", "\n");
        s = CodigoComArgumento.Replace(s, "");
        s = CodigoSimples.Replace(s, " ");
        s = s.Replace("{", "").Replace("}", "").Replace("\r\n", "\n").Replace('\r', '\n');
        int palavras = 0;
        foreach (var linha in s.Split('\n'))
        {
            int n = linha.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            if (n == 0) continue; // linha vazia (Enter duplo) não conta
            if (palavras > 0) r.Add(palavras);
            palavras += n;
        }
        return r;
    }

    /// <summary>
    /// Aplica as quebras ao texto gerado (palavras separadas por espaço): a quebra k troca o espaço depois da
    /// k-ésima palavra por "\P". Quebras fora do texto (ele ficou com menos palavras) são ignoradas.
    /// Texto que já tem quebra própria (tela identificada) não é mexido.
    /// </summary>
    public static string Aplicar(string gerado, IReadOnlyCollection<int> quebras)
    {
        if (string.IsNullOrEmpty(gerado) || quebras == null || quebras.Count == 0 || gerado.Contains("\\P")) return gerado;
        var palavras = gerado.Split(' ');
        if (palavras.Length < 2) return gerado;
        var sb = new System.Text.StringBuilder(palavras[0]);
        for (int i = 1; i < palavras.Length; i++)
            sb.Append(quebras.Contains(i) ? "\\P" : " ").Append(palavras[i]);
        return sb.ToString();
    }

    public static bool Iguais(IReadOnlyCollection<int> a, IReadOnlyCollection<int> b) =>
        (a?.Count ?? 0) == (b?.Count ?? 0) && (a == null || a.OrderBy(x => x).SequenceEqual(b.OrderBy(x => x)));
}
