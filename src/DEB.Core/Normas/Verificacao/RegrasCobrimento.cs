using System;
using System.Collections.Generic;

namespace DEB.Core.Normas.Verificacao;

/// <summary>Dados de cobrimento de uma peça (mm).</summary>
public sealed class DadosCobrimento
{
    public ClasseAgressividade Classe { get; set; } = ClasseAgressividade.II;
    public ElementoCobrimento Elemento { get; set; } = ElementoCobrimento.VigaPilar;
    /// <summary>Peça pré-moldada (NBR 9062): Δc = 5 mm em vez de 10 mm.</summary>
    public bool PreMoldado { get; set; } = true;
    /// <summary>Concreto protendido: Tabela 7.2 de protendido e mínimo absoluto de 25 mm da NBR 9062.</summary>
    public bool Protendido { get; set; }
    public double FckMPa { get; set; }
    /// <summary>Relação água/cimento ≤ 0,45 declarada pelo usuário (com fck ≥ 40 MPa libera a 2ª redução da NBR 9062).</summary>
    public bool AguaCimentoAte045 { get; set; }
    /// <summary>Menor cobrimento usado no desenho (faces), mm.</summary>
    public double CobrimentoMm { get; set; }
    /// <summary>Maior bitola (ou φ equivalente do feixe, φ√n) coberta por esse cobrimento, mm.</summary>
    public double MaiorBitolaMm { get; set; }
    /// <summary>Dimensão máxima característica do agregado graúdo, mm (0 = não informado).</summary>
    public double AgregadoMm { get; set; }
}

/// <summary>
/// G1–G4: cobrimento nominal (NBR 6118, Tab. 7.2, 7.4.7.5, 7.4.7.6) com as reduções do pré-moldado aplicadas
/// automaticamente (NBR 9062, 9.2.1.1) — decisão do usuário (18/11/2026): "aplica as reduções automaticamente e avisa".
/// </summary>
public static class RegrasCobrimento
{
    /// <summary>Número com vírgula decimal (os textos vão para o relatório e os avisos em português).</summary>
    private static string N(double x, string f = "0.#") => Fmt.Virgula(x, f);

    /// <summary>Mínimos absolutos da NBR 9062, 9.2.1.1.2 (mm): concreto armado 15 (laje) ou 20; protendido 25.</summary>
    private static double MinimoAbsoluto(ElementoCobrimento e, bool protendido) => protendido ? 25 : e == ElementoCobrimento.Laje ? 15 : 20;

    /// <summary>
    /// Cobrimento nominal exigido (mm) e as reduções aplicadas, em texto: Tab. 7.2 (Δc = 10 mm); pré-moldado −5 mm (Δc = 5 mm,
    /// 9.2.1.1.1); com fck ≥ 40 MPa e a/c ≤ 0,45 mais −5 mm (9.2.1.1.2), sem passar dos mínimos da 9062.
    /// </summary>
    public static (double Mm, string Reducoes) Exigido(DadosCobrimento d)
    {
        double tab = TabelasNbr.CobrimentoNominalMm(d.Classe, d.Elemento, d.Protendido);
        if (!d.PreMoldado) return (tab, "");
        var red = new List<string> { "−5 mm (Δc = 5 mm, NBR 9062 9.2.1.1.1)" };
        double c = tab - 5;
        if (d.FckMPa >= 40 && d.AguaCimentoAte045)
        {
            c -= 5;
            red.Add("−5 mm (fck ≥ 40 MPa e a/c ≤ 0,45, NBR 9062 9.2.1.1.2)");
        }
        double min = MinimoAbsoluto(d.Elemento, d.Protendido);
        if (c < min) { c = min; red.Add($"limitado a {min:0} mm (NBR 9062 9.2.1.1.2)"); }
        return (c, string.Join("; ", red));
    }

    public static IEnumerable<ResultadoVerificacao> Verificar(DadosCobrimento d)
    {
        var (exig, red) = Exigido(d);
        string classe = TabelasNbr.Romano(d.Classe);
        double tab = TabelasNbr.CobrimentoNominalMm(d.Classe, d.Elemento, d.Protendido);
        bool reduzido = exig < tab - 1e-9;
        // G1/G2: abaixo do exigido = não atende; atende só graças às reduções = conferir (avisa a redução aplicada)
        yield return new ResultadoVerificacao
        {
            Codigo = reduzido ? "G2" : "G1", Grupo = "Cobrimento",
            Descricao = $"Cobrimento nominal, classe de agressividade {classe}{(d.Protendido ? ", concreto protendido" : "")}",
            Valor = $"{N(d.CobrimentoMm)} mm", Limite = $"≥ {N(exig)} mm",
            Situacao = d.CobrimentoMm < exig - 1e-6 ? SituacaoVerificacao.NaoAtende
                     : reduzido && d.CobrimentoMm < tab - 1e-6 ? SituacaoVerificacao.Conferir : SituacaoVerificacao.Ok,
            Referencia = reduzido ? "NBR 6118, Tab. 7.2; NBR 9062, 9.2.1.1" : "NBR 6118, 7.4.7.2, Tab. 7.2",
            Dica = reduzido ? $"Tabela 7.2: {N(tab)} mm; reduções aplicadas: {red}." : "",
        };
        if (d.MaiorBitolaMm > 0)
            yield return new ResultadoVerificacao
            {
                Codigo = "G3", Grupo = "Cobrimento", Descricao = "Cobrimento maior ou igual à bitola (ou ao φ do feixe)",
                Valor = $"{N(d.CobrimentoMm)} mm", Limite = $"≥ {N(d.MaiorBitolaMm)} mm",
                Situacao = d.CobrimentoMm < d.MaiorBitolaMm - 1e-6 ? SituacaoVerificacao.NaoAtende : SituacaoVerificacao.Ok,
                Referencia = "NBR 6118, 7.4.7.5",
            };
        if (d.AgregadoMm > 0)
            yield return new ResultadoVerificacao
            {
                Codigo = "G4", Grupo = "Cobrimento", Descricao = "Agregado graúdo dmáx ≤ 1,2 × cobrimento",
                Valor = $"{N(d.AgregadoMm)} mm", Limite = $"≤ {N(1.2 * d.CobrimentoMm)} mm",
                Situacao = d.AgregadoMm > 1.2 * d.CobrimentoMm + 1e-6 ? SituacaoVerificacao.NaoAtende : SituacaoVerificacao.Ok,
                Referencia = "NBR 6118, 7.4.7.6",
            };
    }
}
