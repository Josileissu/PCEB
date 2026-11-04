using System;
using System.Text.RegularExpressions;

namespace DEB.Core.Numeracao;

public enum ModoFiltroMarca { ComecaCom = 1, Contem = 2, Igual = 3, Regex = 4, Todas = 5 }

/// <summary>Compara a marca de uma peça com o filtro (sem diferenciar maiúsculas); peças sem marca nunca entram.</summary>
public static class FiltroMarca
{
    public static bool Corresponde(string marca, ModoFiltroMarca modo, string texto)
    {
        if (string.IsNullOrEmpty(marca)) return false;
        texto ??= "";
        return modo switch
        {
            ModoFiltroMarca.ComecaCom => marca.StartsWith(texto, StringComparison.OrdinalIgnoreCase),
            ModoFiltroMarca.Contem => marca.IndexOf(texto, StringComparison.OrdinalIgnoreCase) >= 0,
            ModoFiltroMarca.Igual => marca.Equals(texto, StringComparison.OrdinalIgnoreCase),
            ModoFiltroMarca.Regex => Regex.IsMatch(marca, texto, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)),
            ModoFiltroMarca.Todas => true,
            _ => false,
        };
    }
}
