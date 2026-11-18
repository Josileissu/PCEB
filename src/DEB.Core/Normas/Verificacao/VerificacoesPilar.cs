using System;

namespace DEB.Core.Normas.Verificacao;

/// <summary>
/// Regras P1–P14 do pilar (NBR 6118:2023, 13.2.3, 17.3.5.3, 18.2.4, 18.4), sem cálculo de esforços. Uma regra só, chamada pelo
/// template do CAD (<c>ArmadurasPilar</c>) e pela armadura do Revit (<c>ColumnRebarSpec.Check</c>) — antes eram duas cópias.
/// Todas as medidas em mm (áreas em mm²). Cada função devolve o resultado com a situação (OK também), para o relatório.
/// </summary>
public static class VerificacoesPilar
{
    private const string G = "Pilar";

    private static ResultadoVerificacao R(string cod, string grupo, string desc, string valor, string limite, bool falha,
                                          string refer, SituacaoVerificacao seFalha = SituacaoVerificacao.NaoAtende, string dica = "") =>
        new ResultadoVerificacao
        {
            Codigo = cod, Grupo = grupo, Descricao = desc, Valor = valor, Limite = limite, Referencia = refer, Dica = dica,
            Situacao = falha ? seFalha : SituacaoVerificacao.Ok,
        };

    /// <summary>P1: menor dimensão ≥ 19 cm (14 a 19 cm com γn, Tab. 13.1) e área ≥ 360 cm².</summary>
    public static ResultadoVerificacao DimensaoMinima(double bMm, double hMm)
    {
        double menor = Math.Min(bMm, hMm), area = bMm * hMm / 100;   // cm²
        if (menor < 140 - 1e-6 || area < 360 - 1e-6)
            return R("P1", G, "Seção do pilar abaixo do mínimo", $"{Fmt.N(menor / 10)} cm, {Fmt.N(area, "0")} cm²",
                     "menor dimensão ≥ 14 cm e área ≥ 360 cm²", true, "NBR 6118, 13.2.3");
        if (menor < 190 - 1e-6)
        {
            double gn = 1.95 - 0.05 * menor / 10;
            return R("P1", G, "Menor dimensão entre 14 e 19 cm: esforços multiplicados por γn", $"{Fmt.N(menor / 10)} cm",
                     "≥ 19 cm", true, "NBR 6118, 13.2.3, Tab. 13.1", SituacaoVerificacao.Conferir, $"γn = {Fmt.Virgula(gn, "0.00")}.");
        }
        return R("P1", G, "Menor dimensão do pilar", $"{Fmt.N(menor / 10)} cm", "≥ 19 cm", false, "NBR 6118, 13.2.3");
    }

    /// <summary>P2: lado maior até 5× o menor; acima disso é pilar-parede (18.5).</summary>
    public static ResultadoVerificacao PilarParede(double bMm, double hMm)
    {
        double menor = Math.Min(bMm, hMm), maior = Math.Max(bMm, hMm);
        return R("P2", G, "Relação entre lados (acima de 5 é pilar-parede: regras de 18.5)", Fmt.Virgula(maior / menor, "0.0#"), "≤ 5",
                 maior > 5 * menor + 1e-6, "NBR 6118, 18.4.1 e 18.5", SituacaoVerificacao.Conferir);
    }

    /// <summary>P3: 10 mm ≤ φ ≤ 1/8 da menor dimensão.</summary>
    public static ResultadoVerificacao BitolaLongitudinal(double phiMm, double menorMm)
    {
        if (phiMm < 10 - 1e-6)
            return R("P3", "Longitudinal", $"Longitudinal ø{Fmt.N(phiMm)} < 10 mm", $"ø{Fmt.N(phiMm)}", "≥ 10 mm", true, "NBR 6118, 18.4.2.1");
        return R("P3", "Longitudinal", $"Longitudinal ø{Fmt.N(phiMm)} e 1/8 da menor dimensão", $"ø{Fmt.N(phiMm)}",
                 $"≤ {Fmt.N(menorMm / 8)} mm", phiMm > menorMm / 8 + 1e-6, "NBR 6118, 18.4.2.1");
    }

    /// <summary>P4: barra em cada vértice — falha quando há trecho da altura sem barra de canto (cm, a partir da base).</summary>
    public static ResultadoVerificacao Vertices(double semCantoDeCm, double ateCm) =>
        R("P4", "Longitudinal", $"Sem barras de canto de {Fmt.N(semCantoDeCm)} a {Fmt.N(ateCm)} cm: é preciso uma barra em cada vértice",
          "", "", ateCm > semCantoDeCm + 1e-6, "NBR 6118, 18.4.2.2");

    /// <summary>P5: espaçamento livre ≥ max(20 mm; φ, φ do feixe φ√n ou da luva; 1,2·dmáx do agregado), também nas emendas.</summary>
    public static ResultadoVerificacao EspacamentoLivre(double livreMm, double phiEqMaxMm, double agregadoMm)
    {
        double min = Math.Max(20, Math.Max(phiEqMaxMm, 1.2 * agregadoMm));
        return R("P5", "Longitudinal", "Espaçamento livre entre barras", $"{Fmt.N(livreMm, "0.#")} mm", $"≥ {Fmt.N(min, "0.#")} mm",
                 livreMm < min - 1e-6, "NBR 6118, 18.4.2.2");
    }

    /// <summary>P6: espaçamento entre eixos ≤ min(2·b; 400 mm).</summary>
    public static ResultadoVerificacao EspacamentoEixos(double maiorMm, double menorLadoMm)
    {
        double max = Math.Min(2 * menorLadoMm, 400);
        return R("P6", "Longitudinal", "Espaçamento entre eixos das barras longitudinais", $"{Fmt.N(maiorMm, "0")} mm", $"≤ {Fmt.N(max, "0")} mm",
                 maiorMm > max + 1e-6, "NBR 6118, 18.4.2.2");
    }

    /// <summary>
    /// P7: taxa geométrica ≥ 0,4 % (piso; o mínimo completo max(0,15·Nd/fyd; 0,004·Ac) precisa de Nd).
    /// P8: taxa ≤ 8 % <b>contando a sobreposição das emendas</b> (17.3.5.3.2; antes o Revit usava 4 %).
    /// </summary>
    public static ResultadoVerificacao TaxaMinima(double asMm2, double acMm2) =>
        R("P7", "Longitudinal", "Taxa geométrica no trecho menos armado", $"{Fmt.Virgula(asMm2 / acMm2 * 100, "0.00")} %", "≥ 0,40 %",
          asMm2 / acMm2 < 0.004 - 1e-9, "NBR 6118, 17.3.5.3.1", dica: "O mínimo completo, 0,15·Nd/fyd, depende da força normal.");

    public static ResultadoVerificacao TaxaMaxima(double asMm2, double acMm2) =>
        R("P8", "Longitudinal", "Taxa geométrica no trecho mais armado, inclusive emendas", $"{Fmt.Virgula(asMm2 / acMm2 * 100, "0.00")} %",
          "≤ 8 %", asMm2 / acMm2 > 0.08 + 1e-9, "NBR 6118, 17.3.5.3.2");

    /// <summary>
    /// P10/P12: φt ≥ max(5 mm; φ/4). Com φt &lt; φ/4 a norma admite s ≤ 90 000·φt²/(φ·fyk) (mesmo aço): então Conferir, não falha.
    /// </summary>
    public static ResultadoVerificacao BitolaEstribo(double phiTMm, double phiLongEqMaxMm, double espacamentoMm = 0, double fykMPa = 500)
    {
        double min = Math.Max(5, phiLongEqMaxMm / 4);
        bool falha = phiTMm < min - 1e-6;
        if (falha && phiTMm >= 5 - 1e-6 && espacamentoMm > 0)
        {
            double sAlt = 90000 * phiTMm * phiTMm / (phiLongEqMaxMm * fykMPa);
            if (espacamentoMm <= sAlt + 1e-6)
                return R("P12", "Estribos", $"Estribo ø{Fmt.N(phiTMm)} < φ/4, admitido pelo espaçamento", $"s = {Fmt.N(espacamentoMm, "0")} mm",
                         $"≤ 90000·φt²/(φ·fyk) = {Fmt.N(sAlt, "0")} mm", true, "NBR 6118, 18.4.3", SituacaoVerificacao.Conferir,
                         "Vale só com o mesmo tipo de aço nas longitudinais e nos estribos.");
        }
        return R("P10", "Estribos", $"Estribo ø{Fmt.N(phiTMm)}: mínimo de 5 mm e φ/4", $"ø{Fmt.N(phiTMm)}", $"≥ {Fmt.N(min, "0.0#")} mm",
                 falha, "NBR 6118, 18.4.3");
    }

    /// <summary>P11: espaçamento dos estribos ≤ min(200 mm; menor dimensão; 12φ, ou 24φ no CA-25).</summary>
    public static ResultadoVerificacao EspacamentoEstribos(double sMm, double menorLadoMm, double phiLongMinMm, string acoLong, string zona = "")
    {
        double k = TabelasNbr.FatorEspacamentoEstriboPilar(acoLong);
        double max = Math.Min(200, Math.Min(menorLadoMm, k * phiLongMinMm));
        string onde = zona.Length > 0 ? $" ({zona})" : "";
        return R("P11", "Estribos", $"Espaçamento dos estribos{onde}", $"{Fmt.N(sMm, "0.#")} mm",
                 $"≤ {Fmt.N(max, "0.#")} mm = mín. de 20 cm, menor dimensão e {k:0}φ", sMm > max + 1e-6, "NBR 6118, 18.4.3");
    }

    /// <summary>P13: concretos C55 a C90 — espaçamentos máximos reduzidos em 50 % e ganchos ≥ 135°.</summary>
    public static ResultadoVerificacao ConcretoAltaResistencia(double fckMPa) =>
        R("P13", "Estribos", "Concreto do grupo II (C55 a C90): reduzir em 50 % os espaçamentos máximos dos estribos, ganchos ≥ 135°",
          $"fck = {Fmt.N(fckMPa)} MPa", "< 55 MPa", fckMPa >= 55 - 1e-9, "NBR 6118, 18.4.3", SituacaoVerificacao.Conferir);

    /// <summary>P14: barras a mais de 20φt do canto (ou mais de duas nesse trecho) pedem grampo suplementar.</summary>
    public static ResultadoVerificacao ProtecaoFlambagem(bool barraSemProtecao) =>
        R("P14", "Estribos", "Há barras a mais de 20φt do canto sem grampos suplementares", "", "", barraSemProtecao, "NBR 6118, 18.2.4");
}
