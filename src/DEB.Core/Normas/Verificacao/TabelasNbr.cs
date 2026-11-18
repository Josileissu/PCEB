using System;

namespace DEB.Core.Normas.Verificacao;

/// <summary>Classe de agressividade ambiental (NBR 6118, Tabela 6.1).</summary>
public enum ClasseAgressividade { I = 1, II = 2, III = 3, IV = 4 }

/// <summary>Elemento para a escolha do cobrimento (NBR 6118, Tabela 7.2).</summary>
public enum ElementoCobrimento { Laje, VigaPilar, ContatoSolo }

/// <summary>Tabelas da NBR 6118:2023 usadas pelas verificações (valores conferidos no PDF da norma em 18/11/2026).</summary>
public static class TabelasNbr
{
    // Tabela 17.3 — taxas mínimas de armadura de flexão para vigas de seção retangular, em % (CA-50, d/h = 0,8,
    // γc = 1,4, γs = 1,15). A tabela é imagem no PDF: transcrita da página 131 da norma.
    private static readonly int[] FckTab173 = { 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90 };
    private static readonly double[] RhoTab173 =
        { 0.150, 0.150, 0.150, 0.164, 0.179, 0.194, 0.208, 0.211, 0.219, 0.226, 0.233, 0.239, 0.245, 0.251, 0.256 };

    /// <summary>
    /// ρmín (%) da Tabela 17.3 para o fck (MPa). Entre duas colunas usa a de fck imediatamente acima (a favor da segurança);
    /// abaixo de 20 MPa, a de 20; acima de 90, a de 90.
    /// </summary>
    public static double RhoMinVigaPct(double fckMPa)
    {
        for (int i = 0; i < FckTab173.Length; i++)
            if (fckMPa <= FckTab173[i] + 1e-9) return RhoTab173[i];
        return RhoTab173[RhoTab173.Length - 1];
    }

    /// <summary>Cobrimento nominal (mm) da Tabela 7.2, Δc = 10 mm: concreto armado ou, com <paramref name="protendido"/>, protendido.</summary>
    public static double CobrimentoNominalMm(ClasseAgressividade classe, ElementoCobrimento elemento, bool protendido = false)
    {
        int k = (int)classe - 1;
        // protendido (cobrimento da bainha ou do fio/cordoalha): laje 25/30/40/50, viga/pilar 30/35/45/55
        if (protendido) return elemento == ElementoCobrimento.Laje ? new[] { 25, 30, 40, 50 }[k] : new[] { 30, 35, 45, 55 }[k];
        switch (elemento)
        {
            case ElementoCobrimento.Laje: return new[] { 20, 25, 35, 45 }[k];
            case ElementoCobrimento.VigaPilar: return new[] { 25, 30, 40, 50 }[k];
            // contato com o solo: a tabela começa na classe II (30/40/50); na classe I vale o da II
            default: return new[] { 30, 30, 40, 50 }[k];
        }
    }

    /// <summary>Classe pelo texto do parâmetro ("I", "II", "III", "IV"); desconhecido = II.</summary>
    public static ClasseAgressividade Classe(string texto)
    {
        switch ((texto ?? "").Trim().ToUpperInvariant())
        {
            case "I": case "1": return ClasseAgressividade.I;
            case "III": case "3": return ClasseAgressividade.III;
            case "IV": case "4": return ClasseAgressividade.IV;
            default: return ClasseAgressividade.II;
        }
    }

    public static string Romano(ClasseAgressividade c) => c switch
    {
        ClasseAgressividade.I => "I", ClasseAgressividade.III => "III", ClasseAgressividade.IV => "IV", _ => "II",
    };

    /// <summary>Fator do espaçamento máximo dos estribos de pilar em φ: 24 para CA-25, 12 para os demais (NBR 6118, 18.4.3).</summary>
    public static double FatorEspacamentoEstriboPilar(string acoLongitudinal) =>
        string.Equals((acoLongitudinal ?? "").Replace(" ", ""), "CA-25", StringComparison.OrdinalIgnoreCase) ? 24 : 12;

    /// <summary>
    /// Resistência média à tração do concreto (MPa, NBR 6118, 8.2.5): 0,3·fck^(2/3) até C50; 2,12·ln(1 + 0,11·fck) de C55 a C90.
    /// </summary>
    public static double Fctm(double fckMPa) =>
        fckMPa <= 50 ? 0.3 * Math.Pow(fckMPa, 2.0 / 3.0) : 2.12 * Math.Log(1 + 0.11 * fckMPa);

    /// <summary>fyk (MPa) pelo nome do aço; desconhecido = CA-50.</summary>
    public static double Fyk(string aco)
    {
        string a = (aco ?? "").Replace(" ", "").ToUpperInvariant();
        return a == "CA-25" ? 250 : a == "CA-60" ? 600 : 500;
    }
}
