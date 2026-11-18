using System;
using System.Collections.Generic;

namespace DEB.Core.Normas.Verificacao;

/// <summary>Dados de um consolo para as verificações de detalhamento (cm e mm como indicado).</summary>
public sealed class DadosConsolo
{
    /// <summary>Identificação no relatório ("40x23,5x25 duplo").</summary>
    public string Nome { get; set; } = "";
    /// <summary>Largura (ao longo da face do pilar), balanço, altura no engastamento e na ponta, cm.</summary>
    public double B { get; set; }
    public double A { get; set; }
    public double H { get; set; }
    public double H1 { get; set; }
    /// <summary>Cobrimento, cm.</summary>
    public double Cob { get; set; } = 2.5;
    public int TirN { get; set; }
    /// <summary>Bitolas em mm.</summary>
    public double TirPhi { get; set; }
    /// <summary>Tirante ancorado na ponta por alça horizontal (laço); false = barra transversal soldada de mesmo diâmetro.</summary>
    public bool TirAlca { get; set; }
    public int CostN { get; set; }
    public double CostPhi { get; set; }
    /// <summary>Estribos verticais (2 ramos cada).</summary>
    public int VertN { get; set; }
    public double VertPhi { get; set; }
    public double FckMPa { get; set; } = 40;
    public double FykMPa { get; set; } = 500;

    /// <summary>Altura útil no engastamento (cm): até o centro do tirante, na 1ª camada abaixo do cobrimento.</summary>
    public double D => H - Cob - TirPhi / 20;
}

/// <summary>
/// Regras C* do consolo (NBR 9062:2017, 7.3), sem esforços (D19/D36): o que dá para conferir só com a geometria e a armadura
/// desenhada. As que dependem do aparelho de apoio (a, a2, afastamento lateral: C3–C5) entram quando a família do consolo
/// tiver o elemento "apoio" (D36, PC2); as que dependem da carga (As,tir, biela) ficam para a fase de cálculo.
/// Texto conferido no PDF da norma (p. 58–62).
/// </summary>
public static class VerificacoesConsolo
{
    private static string N(double x, string f = "0.#") => Fmt.Virgula(x, f);

    private static ResultadoVerificacao R(DadosConsolo c, string cod, string desc, string valor, string limite, bool falha, string refer,
                                          SituacaoVerificacao seFalha = SituacaoVerificacao.NaoAtende, string dica = "") =>
        new ResultadoVerificacao
        {
            Codigo = cod, Grupo = "Consolo " + c.Nome, Descricao = desc, Valor = valor, Limite = limite, Referencia = refer, Dica = dica,
            Situacao = falha ? seFalha : SituacaoVerificacao.Ok,
        };

    public static List<ResultadoVerificacao> Verificar(DadosConsolo c)
    {
        var r = new List<ResultadoVerificacao>();
        double bMin = Math.Min(c.B, c.H), d = c.D;
        double asTir = c.TirN * Math.PI * c.TirPhi * c.TirPhi / 400;            // cm²
        double asCost = c.CostN * 2 * Math.PI * c.CostPhi * c.CostPhi / 400;    // estribos horizontais de 2 ramos, cm²
        double asVert = c.VertN * 2 * Math.PI * c.VertPhi * c.VertPhi / 400;

        // C1: face externa h1 ≥ h/2 − a2 (7.3.3.1). Sem o apoio, a2 = 0 (limite mais rigoroso) e a falha fica "Conferir".
        r.Add(R(c, "C1", "Altura da face externa do consolo", $"h1 = {N(c.H1)} cm", $"≥ h/2 − a2 = {N(c.H / 2)} cm (sem a2)",
                c.H1 < c.H / 2 - 1e-6, "NBR 9062, 7.3.3.1", SituacaoVerificacao.Conferir,
                "Com o aparelho de apoio (a2) o limite cai; a família do consolo ainda não traz o apoio."));

        // C2: classificação pelo balanço (a carga fica dentro dele: a/d ≤ balanço/d) — informativa
        double ad = d > 0 ? c.A / d : 0;
        string classe = ad <= 0.5 ? "muito curto" : ad <= 1 ? "curto" : ad <= 2 ? "consolo (a/d ≤ 2)" : "viga (a/d > 2: NBR 6118)";
        r.Add(R(c, "C2", $"Classificação pelo balanço ({classe})", $"balanço/d = {N(c.A, "0.#")}/{N(d, "0.#")} = {N(ad, "0.00")}",
                "a carga fica dentro do balanço: a/d ≤ este valor", false, "NBR 9062, 7.3.2 e 7.3.5.1"));

        // C6/C7: bitola e espaçamento do tirante conforme a ancoragem na ponta
        double phiMax = Math.Min((c.TirAlca ? bMin / 8 : bMin / 6) * 10, 25);
        double espTir = c.TirN > 1 ? (c.B - 2 * c.Cob - c.TirPhi / 10) / (c.TirN - 1) : 0;
        double espMax = Math.Min((c.TirAlca ? 15 : 20) * c.TirPhi / 10, d);
        string anc = c.TirAlca ? "alças horizontais" : "barra transversal soldada";
        string item = c.TirAlca ? "NBR 9062, 7.3.3.6" : "NBR 9062, 7.3.3.7";
        r.Add(R(c, c.TirAlca ? "C6" : "C7", $"Bitola do tirante ancorado por {anc}", $"ø{N(c.TirPhi)}",
                $"≤ {N(phiMax)} mm = mín(b/{(c.TirAlca ? 8 : 6)}; 25 mm)", c.TirPhi > phiMax + 1e-6, item));
        if (c.TirN > 1)
            r.Add(R(c, c.TirAlca ? "C6" : "C7", "Espaçamento do tirante (em planta)", $"{N(espTir)} cm",
                    $"≤ {N(espMax)} cm = mín({(c.TirAlca ? 15 : 20)}φ; d)", espTir > espMax + 1e-6, item));

        // C9: tirante no quinto superior da altura (uma camada, abaixo do cobrimento): posição da barra pelo eixo
        double prof = c.Cob + c.TirPhi / 20;
        r.Add(R(c, "C9", "Tirante no quinto da altura junto à borda tracionada", $"eixo a {N(prof, "0.##")} cm do topo",
                $"≤ h/5 = {N(c.H / 5)} cm", prof > c.H / 5 + 1e-6, "NBR 9062, 7.3.3.11"));

        // C10: costura — bitola e espaçamento vertical (distribuída em 2/3 d)
        if (c.CostN > 0)
        {
            double phiCostMax = bMin / 15 * 10;
            r.Add(R(c, "C10", "Bitola da costura", $"ø{N(c.CostPhi)}", $"≤ b/15 = {N(phiCostMax)} mm", c.CostPhi > phiCostMax + 1e-6, "NBR 9062, 7.3.3.12"));
            double espCost = 2.0 / 3 * d / c.CostN;
            r.Add(R(c, "C10", "Espaçamento vertical da costura (em 2/3 d)", $"{N(espCost)} cm", "≤ 10 cm e ≤ a",
                    espCost > 10 + 1e-6, "NBR 9062, 7.3.3.12", dica: "O limite \"≤ a\" depende da posição da carga (apoio)."));
        }

        // C11: costura mínima (As,cost/s ≥ 0,4·As,tir/d para curto; 0,5 para muito curto), distribuída em 2/3 d
        double taxaCost = d > 0 ? asCost / (2.0 / 3 * d) : 0, taxaTir = d > 0 ? asTir / d : 0;
        double k = ad <= 0.5 ? 0.5 : 0.4;
        r.Add(R(c, "C11", $"Costura mínima ({(ad <= 0.5 ? "muito curto" : "curto")}): As,cost/s ≥ {N(k, "0.0")}·As,tir/d",
                $"{N(taxaCost, "0.000")} cm²/cm", $"≥ {N(k * taxaTir, "0.000")} cm²/cm", taxaCost < k * taxaTir - 1e-9, "NBR 9062, 7.3.6",
                dica: c.CostN == 0 ? "A costura é obrigatória." : ""));

        // C13: taxa mecânica do tirante ω = ρ·fyk/fck ≥ 0,04 (a/d ≤ 2)
        double w = c.B > 0 && d > 0 && c.FckMPa > 0 ? asTir / (c.B * d) * c.FykMPa / c.FckMPa : 0;
        r.Add(R(c, "C13", "Taxa mecânica do tirante ω = (As,tir/b·d)·fyk/fck", N(w, "0.000"), "≥ 0,04", w < 0.04 - 1e-9, "NBR 9062, 7.3.5.2"));

        // C15: estribos verticais (a/d ≤ 1, carga direta): construtivos, taxa mínima 0,15 % de b·h
        if (ad <= 1)
            r.Add(R(c, "C15", "Estribos verticais: área total ≥ 0,15 % de b·h", $"{N(asVert, "0.00")} cm²", $"≥ {N(0.0015 * c.B * c.H, "0.00")} cm²",
                    asVert < 0.0015 * c.B * c.H - 1e-9, "NBR 9062, 7.3.7.2", SituacaoVerificacao.Conferir,
                    "Leitura da taxa como área total dos ramos verticais no balanço; conferir com o livro de comentários."));
        return r;
    }
}
