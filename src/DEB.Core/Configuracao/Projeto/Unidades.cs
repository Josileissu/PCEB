using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DEB.Core.Configuracao.Projeto;

/// <summary>
/// Grandezas que aparecem nas telas, no memorial e na prancha (D45). O cálculo do núcleo é sempre na UNIDADE BASE de cada
/// grandeza (<see cref="Unidades.Base"/>, kN e m como no SI de engenharia); a unidade escolhida na configuração só muda a
/// entrada e a saída.
/// </summary>
public enum Grandeza
{
    Comprimento, Secao, Cobrimento, Diametro, Deslocamento, Angulo, Rotacao,
    Area, AreaAco, AreaAcoPorMetro, Inercia, ModuloResistente, Taxa,
    Forca, Cortante, Normal, Momento, Torcao, CargaLinear, CargaSuperficie, MomentoDistribuido, RigidezMola, RigidezRotacao,
    Tensao, ModuloElasticidade, PesoEspecifico, Massa, Temperatura,
    Aceleracao, Velocidade, Frequencia, Tempo, Idade,
}

/// <summary>Tabela de unidades por grandeza: símbolo → quantas unidades BASE vale 1 unidade (fator multiplicativo).</summary>
public static class Unidades
{
    private const double G = 9.80665;   // m/s²: 1 tf = 9,80665 kN; 1 kgf = 9,80665 N

    private static readonly Dictionary<Grandeza, (string Base, (string Sim, double Fator)[] Opcoes)> T = new()
    {
        [Grandeza.Comprimento] = ("m", new[] { ("m", 1.0), ("cm", 0.01), ("mm", 0.001) }),
        [Grandeza.Secao] = ("m", new[] { ("cm", 0.01), ("mm", 0.001), ("m", 1.0) }),
        [Grandeza.Cobrimento] = ("m", new[] { ("cm", 0.01), ("mm", 0.001) }),
        [Grandeza.Diametro] = ("m", new[] { ("mm", 0.001), ("pol", 0.0254) }),
        [Grandeza.Deslocamento] = ("m", new[] { ("mm", 0.001), ("cm", 0.01), ("m", 1.0) }),
        [Grandeza.Angulo] = ("rad", new[] { ("graus", Math.PI / 180), ("rad", 1.0) }),
        [Grandeza.Rotacao] = ("rad", new[] { ("mrad", 0.001), ("rad", 1.0), ("graus", Math.PI / 180) }),
        [Grandeza.Area] = ("m²", new[] { ("cm²", 1e-4), ("m²", 1.0), ("mm²", 1e-6) }),
        [Grandeza.AreaAco] = ("m²", new[] { ("cm²", 1e-4), ("mm²", 1e-6) }),
        [Grandeza.AreaAcoPorMetro] = ("m²/m", new[] { ("cm²/m", 1e-4), ("mm²/m", 1e-6) }),
        [Grandeza.Inercia] = ("m⁴", new[] { ("cm⁴", 1e-8), ("m⁴", 1.0), ("mm⁴", 1e-12) }),
        [Grandeza.ModuloResistente] = ("m³", new[] { ("cm³", 1e-6), ("m³", 1.0), ("mm³", 1e-9) }),
        [Grandeza.Taxa] = ("%", new[] { ("%", 1.0) }),
        [Grandeza.Forca] = ("kN", new[] { ("kN", 1.0), ("tf", G), ("kgf", G / 1000), ("N", 0.001) }),
        [Grandeza.Cortante] = ("kN", new[] { ("kN", 1.0), ("tf", G), ("kgf", G / 1000) }),
        [Grandeza.Normal] = ("kN", new[] { ("kN", 1.0), ("tf", G), ("kgf", G / 1000) }),
        [Grandeza.Momento] = ("kN·m", new[] { ("kN·m", 1.0), ("tf·m", G), ("kN·cm", 0.01), ("kgf·cm", G / 1000 * 0.01) }),
        [Grandeza.Torcao] = ("kN·m", new[] { ("kN·m", 1.0), ("tf·m", G), ("kN·cm", 0.01), ("kgf·cm", G / 1000 * 0.01) }),
        [Grandeza.CargaLinear] = ("kN/m", new[] { ("kN/m", 1.0), ("tf/m", G), ("kgf/m", G / 1000) }),
        [Grandeza.CargaSuperficie] = ("kN/m²", new[] { ("kN/m²", 1.0), ("tf/m²", G), ("kgf/m²", G / 1000) }),
        [Grandeza.MomentoDistribuido] = ("kN·m/m", new[] { ("kN·m/m", 1.0), ("tf·m/m", G) }),
        [Grandeza.RigidezMola] = ("kN/m", new[] { ("kN/m", 1.0), ("kN/cm", 100.0), ("tf/m", G) }),
        [Grandeza.RigidezRotacao] = ("kN·m/rad", new[] { ("kN·m/rad", 1.0), ("tf·m/rad", G) }),
        [Grandeza.Tensao] = ("MPa", new[] { ("MPa", 1.0), ("kN/cm²", 10.0), ("kgf/cm²", G / 100) }),
        [Grandeza.ModuloElasticidade] = ("MPa", new[] { ("GPa", 1000.0), ("MPa", 1.0), ("kN/cm²", 10.0) }),
        [Grandeza.PesoEspecifico] = ("kN/m³", new[] { ("kN/m³", 1.0), ("kgf/m³", G / 1000), ("tf/m³", G) }),
        [Grandeza.Massa] = ("kg", new[] { ("kg", 1.0), ("t", 1000.0) }),
        [Grandeza.Temperatura] = ("°C", new[] { ("°C", 1.0) }),
        [Grandeza.Aceleracao] = ("m/s²", new[] { ("m/s²", 1.0), ("g", G) }),
        [Grandeza.Velocidade] = ("m/s", new[] { ("m/s", 1.0), ("km/h", 1 / 3.6) }),
        [Grandeza.Frequencia] = ("Hz", new[] { ("Hz", 1.0), ("rad/s", 1 / (2 * Math.PI)) }),
        [Grandeza.Tempo] = ("s", new[] { ("s", 1.0) }),
        [Grandeza.Idade] = ("dias", new[] { ("dias", 1.0), ("meses", 30.0) }),
    };

    /// <summary>Unidade base da grandeza (a do cálculo no núcleo).</summary>
    public static string Base(Grandeza g) => T[g].Base;

    /// <summary>Símbolos aceitos para a grandeza, na ordem da tela (o primeiro é o de partida).</summary>
    public static IReadOnlyList<string> Opcoes(Grandeza g) => T[g].Opcoes.Select(o => o.Sim).ToList();

    public static bool Aceita(Grandeza g, string simbolo) => T[g].Opcoes.Any(o => o.Sim == simbolo);

    private static double Fator(Grandeza g, string simbolo)
    {
        foreach (var (s, f) in T[g].Opcoes) if (s == simbolo) return f;
        throw new ArgumentException($"Unidade \"{simbolo}\" não vale para {g}.");
    }

    /// <summary>Valor na unidade <paramref name="simbolo"/> → unidade base.</summary>
    public static double ParaBase(Grandeza g, double valor, string simbolo) => valor * Fator(g, simbolo);

    /// <summary>Valor na unidade base → unidade <paramref name="simbolo"/>.</summary>
    public static double DaBase(Grandeza g, double valorBase, string simbolo) => valorBase / Fator(g, simbolo);

    /// <summary>Texto "74,0 kN·m" na unidade escolhida, com vírgula ou ponto.</summary>
    public static string Texto(Grandeza g, double valorBase, string simbolo, int casas, bool virgula = true)
    {
        string n = DaBase(g, valorBase, simbolo).ToString("F" + Math.Max(0, casas), CultureInfo.InvariantCulture);
        if (virgula) n = n.Replace('.', ',');
        string sep = simbolo == "%" || simbolo == "°C" ? "" : " ";
        return n + sep + simbolo;
    }

    /// <summary>
    /// Atalhos de sistema (D45): preenchem de uma vez as unidades de força e de comprimento; cada grandeza segue editável.
    /// Chave = id do campo de unidade no catálogo (<c>unid.&lt;grandeza&gt;</c>).
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<Grandeza, string>> Sistemas { get; } =
        new Dictionary<string, IReadOnlyDictionary<Grandeza, string>>
        {
            ["SI · kN, m"] = new Dictionary<Grandeza, string>
            {
                [Grandeza.Forca] = "kN", [Grandeza.Cortante] = "kN", [Grandeza.Normal] = "kN", [Grandeza.Momento] = "kN·m", [Grandeza.Torcao] = "kN·m",
                [Grandeza.CargaLinear] = "kN/m", [Grandeza.CargaSuperficie] = "kN/m²", [Grandeza.Comprimento] = "m", [Grandeza.Tensao] = "MPa",
            },
            ["SI de prancha · kN, cm"] = new Dictionary<Grandeza, string>
            {
                [Grandeza.Forca] = "kN", [Grandeza.Cortante] = "kN", [Grandeza.Normal] = "kN", [Grandeza.Momento] = "kN·cm", [Grandeza.Torcao] = "kN·cm",
                [Grandeza.CargaLinear] = "kN/m", [Grandeza.CargaSuperficie] = "kN/m²", [Grandeza.Comprimento] = "cm", [Grandeza.Tensao] = "kN/cm²",
            },
            ["Técnico · tf, m"] = new Dictionary<Grandeza, string>
            {
                [Grandeza.Forca] = "tf", [Grandeza.Cortante] = "tf", [Grandeza.Normal] = "tf", [Grandeza.Momento] = "tf·m", [Grandeza.Torcao] = "tf·m",
                [Grandeza.CargaLinear] = "tf/m", [Grandeza.CargaSuperficie] = "tf/m²", [Grandeza.Comprimento] = "m", [Grandeza.Tensao] = "kgf/cm²",
            },
            ["Técnico de prancha · kgf, cm"] = new Dictionary<Grandeza, string>
            {
                [Grandeza.Forca] = "kgf", [Grandeza.Cortante] = "kgf", [Grandeza.Normal] = "kgf", [Grandeza.Momento] = "kgf·cm", [Grandeza.Torcao] = "kgf·cm",
                [Grandeza.CargaLinear] = "kgf/m", [Grandeza.CargaSuperficie] = "kgf/m²", [Grandeza.Comprimento] = "cm", [Grandeza.Tensao] = "kgf/cm²",
            },
        };
}
