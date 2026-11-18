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
    /// <summary>Aparelho de apoio (D36); null = sem informação (C1 com a2 = 0, C3–C5 não entram).</summary>
    public DEB.Core.Intercambio.ApoioPeca Apoio { get; set; }

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

        var ap = c.Apoio;
        double a2 = ap?.Afastamento ?? 0;
        // C1: face externa h1 ≥ h/2 − a2 (7.3.3.1). Sem o apoio, a2 = 0 (limite mais rigoroso) e a falha fica "Conferir".
        r.Add(R(c, "C1", "Altura da face externa do consolo", $"h1 = {N(c.H1)} cm",
                ap == null ? $"≥ h/2 − a2 = {N(c.H / 2)} cm (sem a2)" : $"≥ h/2 − a2 = {N(c.H / 2)} − {N(a2)} = {N(c.H / 2 - a2)} cm",
                c.H1 < c.H / 2 - a2 - 1e-6, "NBR 9062, 7.3.3.1", ap == null ? SituacaoVerificacao.Conferir : SituacaoVerificacao.NaoAtende,
                ap == null ? "Sem o aparelho de apoio (a2) o limite é o mais rigoroso." : ""));

        // C2: classificação; com o apoio, a = distância da face do pilar ao centro do apoio
        double a = ap != null && ap.Comprimento > 0 ? c.A - a2 - ap.Comprimento / 2 : c.A;
        double ad = d > 0 ? a / d : 0;
        string classe = ad <= 0.5 ? "muito curto" : ad <= 1 ? "curto" : ad <= 2 ? "consolo (a/d ≤ 2)" : "viga (a/d > 2: NBR 6118)";
        r.Add(ap != null && ap.Comprimento > 0
            ? R(c, "C2", $"Classificação ({classe}); a = da face do pilar ao centro do apoio", $"a/d = {N(a)}/{N(d)} = {N(ad, "0.00")}",
                "≤ 0,5 muito curto; ≤ 1 curto; ≤ 2 consolo", false, "NBR 9062, 7.3.2 e 7.3.5.1")
            : R(c, "C2", $"Classificação pelo balanço ({classe})", $"balanço/d = {N(c.A)}/{N(d)} = {N(ad, "0.00")}",
                "a carga fica dentro do balanço: a/d ≤ este valor", false, "NBR 9062, 7.3.2 e 7.3.5.1"));

        if (ap != null)
        {
            // C3: distância a2 mínima pela ancoragem do tirante (7.3.3.4)
            double phi = c.TirPhi / 10, mult = !c.TirAlca ? 1 : c.TirPhi < 20 ? 3.5 : 5;
            double a2min = c.Cob + mult * phi;
            string como = !c.TirAlca ? "c + φ (barra transversal soldada)" : c.TirPhi < 20 ? "c + 3,5φ (alças horizontais, φ < 20)" : "c + 5φ (alças horizontais, φ ≥ 20)";
            r.Add(R(c, "C3", $"Distância a2 da face externa do apoio à ponta do consolo ({ap.NomeTipo})", $"a2 = {N(a2)} cm",
                    $"≥ {como} = {N(a2min)} cm", a2 < a2min - 1e-6, "NBR 9062, 7.3.3.4"));
            // C4: afastamento lateral do apoio maior que o cobrimento → armar o fendilhamento (7.3.3.3)
            if (ap.Largura > 0)
            {
                double lateral = (c.B - ap.Largura) / 2;
                r.Add(R(c, "C4", "Afastamento lateral do apoio", $"{N(lateral)} cm", $"≤ cobrimento = {N(c.Cob)} cm (acima: armar o fendilhamento)",
                        lateral > c.Cob + 1e-6, "NBR 9062, 7.3.3.3", SituacaoVerificacao.Conferir,
                        "Armar para a força de fendilhamento (blocos parcialmente carregados, NBR 6118) ou conferir se já está prevista."));
            }
            // C5: alças horizontais sem armadura de fendilhamento no plano das alças só com c ≤ a2 ≤ c + 3φ (7.3.3.5)
            if (c.TirAlca)
                r.Add(R(c, "C5", "Alças do tirante sem armadura contra o fendilhamento horizontal", $"a2 = {N(a2)} cm",
                        $"entre c = {N(c.Cob)} e c + 3φ = {N(c.Cob + 3 * phi)} cm", a2 < c.Cob - 1e-6 || a2 > c.Cob + 3 * phi + 1e-6,
                        "NBR 9062, 7.3.3.5", SituacaoVerificacao.Conferir, "Fora da faixa: prever armadura contra o fendilhamento no plano das alças."));
        }

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
            double espMaxCost = ap != null && ap.Comprimento > 0 ? Math.Min(10, a) : 10;
            r.Add(R(c, "C10", "Espaçamento vertical da costura (em 2/3 d)", $"{N(espCost)} cm",
                    ap != null && ap.Comprimento > 0 ? $"≤ mín(10 cm; a) = {N(espMaxCost)} cm" : "≤ 10 cm e ≤ a",
                    espCost > espMaxCost + 1e-6, "NBR 9062, 7.3.3.12", dica: ap == null ? "O limite \"≤ a\" depende da posição da carga (apoio)." : ""));
        }

        // C11: costura mínima (As,cost/s ≥ 0,4·As,tir/d para curto; 0,5 para muito curto), distribuída em 2/3 d
        double taxaCost = d > 0 ? asCost / (2.0 / 3 * d) : 0, taxaTir = d > 0 ? asTir / d : 0;
        double k = ad <= 0.5 ? 0.5 : 0.4;
        r.Add(R(c, "C11", $"Costura mínima ({(ad <= 0.5 ? "muito curto" : "curto")}): As,cost/s ≥ {N(k, "0.0")}·As,tir/d",
                $"{N(taxaCost, "0.000")} cm²/cm", $"≥ {N(k * taxaTir, "0.000")} cm²/cm", taxaCost < k * taxaTir - 1e-9, "NBR 9062, 7.3.6",
                dica: c.CostN == 0 ? "A costura é obrigatória." : ""));

        // C12: dispensa de costura só com d > 4(a + a0) (7.3.3.13, Fig. 19); a0 ≥ 0, então d ≤ 4a já decide que não se aplica
        r.Add(d <= 4 * a + 1e-9
            ? R(c, "C12", "Dispensa da costura (d > 4(a + a0)): não se aplica, a costura é obrigatória", $"d = {N(d)} cm", $"4a = {N(4 * a)} cm",
                false, "NBR 9062, 7.3.3.13")
            : R(c, "C12", "Dispensa da costura possível se d > 4(a + a0): então pele com ρ ≥ 0,002 por face na zona 2", $"d = {N(d)} cm",
                $"> 4(a + a0); 4a = {N(4 * a)} cm", false, "NBR 9062, 7.3.3.13, Fig. 19", dica: "Informativo: a0 conforme a Figura 19."));

        // C14: o tirante é um só tipo de barra (n, φ) da ponta até o pilar no desenho: sem redução de seção até o engaste (a/d ≤ 2)
        if (ad <= 2)
            r.Add(R(c, "C14", "Tirante sem redução de seção entre a carga e o engaste (as mesmas barras da ponta ao pilar no desenho)",
                    $"{c.TirN} ø{N(c.TirPhi)}", "", false, "NBR 9062, 7.3.5.1"));

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

    /// <summary>
    /// C16: na face do elemento-suporte (pilar) junto ao consolo, barras com área ≥ a do tirante (7.3.3.14, Fig. 20a).
    /// <paramref name="asFaceCm2"/> = barras longitudinais da face que passam por toda a altura do consolo.
    /// </summary>
    public static ResultadoVerificacao ArmaduraNaFaceDoSuporte(DadosConsolo c, string face, double asFaceCm2)
    {
        double asTir = c.TirN * Math.PI * c.TirPhi * c.TirPhi / 400;
        return R(c, "C16", $"Barras do pilar na face {face} do consolo: As ≥ As,tir", $"{N(asFaceCm2, "0.00")} cm²", $"≥ {N(asTir, "0.00")} cm²",
                 asFaceCm2 < asTir - 1e-9, "NBR 9062, 7.3.3.14, Fig. 20",
                 dica: asFaceCm2 < asTir - 1e-9 ? "Aumentar as barras da face (ou acrescentar barras junto ao consolo em toda a altura dele)." : "");
    }

    /// <summary>
    /// C17: ancoragem do tirante no pilar (7.3.5.5 → NBR 6118, 9.4.2). Duplo (passante) = OK. Consolo sozinho: o trecho reto
    /// disponível no pilar (da face até a face oposta, menos cobrimento, estribo e longitudinal) contra lb,nec = α·lb com a
    /// aderência mais desfavorável (má, η2 = 0,7) e α = 0,7 com gancho/perna (1,0 sem). Sem As,calc, abaixo disso e acima do
    /// lb,mín = máx(0,3·lb; 10φ; 10 cm) fica "Conferir" (lb,nec = α·lb·As,calc/As,ef); abaixo do lb,mín, "Não atende".
    /// </summary>
    public static ResultadoVerificacao AncoragemNoSuporte(DadosConsolo c, bool passante, double disponivelCm, bool gancho, string aco)
    {
        if (passante)
            return R(c, "C17", "Ancoragem do tirante no pilar: tirante passante (consolo duplo)", "", "", false, "NBR 9062, 7.3.5.5");
        var regra = new RegrasTraspasse { Fck = c.FckMPa };
        double lbBoa = regra.Lb(c.TirPhi, aco);
        regra.Aderencia = SituacaoAderencia.Ma;
        double alfa = gancho ? 0.7 : 1.0, lbNec = alfa * regra.Lb(c.TirPhi, aco);
        double lbMin = Math.Max(0.3 * lbBoa, Math.Max(10 * c.TirPhi / 10, 10));
        var sit = disponivelCm >= lbNec - 1e-6 ? SituacaoVerificacao.Ok
                : disponivelCm >= lbMin - 1e-6 ? SituacaoVerificacao.Conferir : SituacaoVerificacao.NaoAtende;
        return new ResultadoVerificacao
        {
            Codigo = "C17", Grupo = "Consolo " + c.Nome, Situacao = sit, Referencia = "NBR 9062, 7.3.5.5; NBR 6118, 9.4.2",
            Descricao = $"Ancoragem do tirante ø{N(c.TirPhi)} no pilar (trecho reto{(gancho ? " antes do gancho/perna" : "")})",
            Valor = $"{N(disponivelCm)} cm", Limite = $"≥ {(gancho ? "0,7·" : "")}lb (má aderência) = {N(lbNec)} cm",
            Dica = sit == SituacaoVerificacao.Conferir
                ? $"Acima do lb,mín = {N(lbMin)} cm: atende se As,calc/As,ef ≤ {N(disponivelCm / lbNec, "0.00")} ou com boa aderência na concretagem."
                : sit == SituacaoVerificacao.NaoAtende ? $"Abaixo do lb,mín = {N(lbMin)} cm." : "",
        };
    }
}
