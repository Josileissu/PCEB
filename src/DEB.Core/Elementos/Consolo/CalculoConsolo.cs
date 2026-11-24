using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao.Projeto;
using DEB.Core.Documentacao;
using DEB.Core.Normas.Verificacao;

namespace DEB.Core.Elementos.Consolo;

/// <summary>
/// Carga no consolo (D44, etapa 1): a reação da viga apoiada nele — característica (G e Q, kN) ou já de cálculo (Vd). Hd = 0 →
/// pela ligação (NBR 9062 7.3.9) e pelo mínimo da configuração; a = 0 → a fração do comprimento da configuração (El Debs 3.4.3).
/// </summary>
public sealed class CargaConsolo
{
    public double Vg { get; set; }
    public double Vq { get; set; }
    /// <summary>Força vertical de cálculo já ponderada (kN); &gt; 0 dispensa Vg/Vq e o γn.</summary>
    public double Vd { get; set; }
    /// <summary>Força horizontal de cálculo (kN); 0 = pela ligação e pelo mínimo.</summary>
    public double Hd { get; set; }
    /// <summary>Distância da força à face do pilar (cm); 0 = automática.</summary>
    public double A { get; set; }
    /// <summary>Carga indireta (pendurada): biela com 0,85 (NBR 9062 7.3.4.1).</summary>
    public bool Indireta { get; set; }
    /// <summary>Coeficiente de atrito da interface (consolo muito curto, NBR 9062 7.3.5.4): 1,4 monolítico.</summary>
    public double Mu { get; set; } = 1.4;
    /// <summary>Fração Hd/Vd da ligação (NBR 9062 7.3.9); 0 = o apoio padrão da configuração.</summary>
    public double FatorHd { get; set; }
    /// <summary>Origem da carga, para o memorial ("reação da viga V3, ponta esquerda").</summary>
    public string Origem { get; set; } = "";

    /// <summary>Fração Hd/Vd pelo aparelho de apoio da família (D36): elastômero 0,16; argamassa 0,5; concreto direto 0,8; sem informação 0.</summary>
    public static double FatorDoApoio(DEB.Core.Intercambio.ApoioPeca apoio) => apoio == null ? 0
        : apoio.Tipo == DEB.Core.Intercambio.ApoioPeca.Argamassa ? 0.5 : apoio.Tipo == DEB.Core.Intercambio.ApoioPeca.Direto ? 0.8 : 0.16;

    /// <summary>
    /// A maior das cargas (pela força de cálculo sem γn, γg·Vg + γq·Vq com 1,4) — envoltória dos consolos iguais de um grupo de
    /// pilares. A origem diz de quantas reações veio. Lista vazia = null.
    /// </summary>
    public static CargaConsolo Maior(IEnumerable<CargaConsolo> cargas)
    {
        var l = cargas.Where(x => x != null).ToList();
        if (l.Count == 0) return null;
        var m = l.OrderByDescending(x => x.Vd > 0 ? x.Vd : 1.4 * (x.Vg + x.Vq)).ThenByDescending(x => x.Vg).First();
        var r = (CargaConsolo)m.MemberwiseClone();
        if (l.Count > 1) r.Origem = m.Origem + $" (a maior de {l.Count} reações dos pilares do grupo)";
        return r;
    }
}

/// <summary>Resultado do cálculo do consolo: classe, esforços, armaduras necessárias × desenhadas, verificações e memória.</summary>
public sealed class ResultadoConsolo
{
    public string Classe { get; set; } = "";
    public double A { get; set; }
    public double D { get; set; }
    public double AsobreD => D > 0 ? A / D : 0;
    public double Vd { get; set; }
    public double Hd { get; set; }
    public double GamaN { get; set; }
    public double TauWd { get; set; }        // MPa
    public double TauLim { get; set; }       // MPa
    public double AsTirNec { get; set; }     // cm²
    public double AsTirTem { get; set; }
    public double AsCostNec { get; set; }    // cm², total em 2/3 d
    public double AsCostTem { get; set; }
    public double AsVertNec { get; set; }    // cm², total
    public double AsVertTem { get; set; }
    public ListaVerificacoes Verificacoes { get; } = new ListaVerificacoes();
    public PassoMemoria Memoria { get; set; } = new PassoMemoria();
}

/// <summary>
/// Cálculo do consolo de concreto (D44, etapa 1) com a armadura DESENHADA (tirante, costura e estribos verticais de
/// <see cref="DadosConsolo"/>). Classe pelo a/d (NBR 9062 7.3.2.2). Consolo curto pelo método da configuração
/// (<c>dim.consoloCurto</c>): El Debs (2017, Quadro 3.2 — τwd = Vd/(b·d) ≤ 0,2·fcd; As,tir = (Vd·a/(0,9d) + 1,2·Hd)/fyd;
/// costura ≥ 0,5·Vd·a/(0,9d·fyd) em 2/3 d; estribos ≥ 0,2·As,tir) ou a NBR 9062 7.3.5.3 e 7.3.6. Muito curto: atrito-cisalhamento
/// (7.3.5.4, τwu de 7.3.4.2). γn, γ e Hd mínimo vêm da configuração do projeto (D45).
/// </summary>
public static class CalculoConsolo
{
    private static double A(double phiMm) => Math.PI * phiMm * phiMm / 400;   // cm²
    private static string N(double x, string f = "0.##") => Fmt.Virgula(x, f);
    public const string MetodoElDebs = "El Debs (2017) · τ ≤ 0,2 fcd";

    /// <summary>Fração Hd/Fd do apoio padrão da configuração (texto "Elastômero · 0,16").</summary>
    public static double FatorApoio(string apoio)
    {
        int i = (apoio ?? "").LastIndexOf('·');
        return i >= 0 && double.TryParse(apoio.Substring(i + 1).Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double f) ? f : 0.16;
    }

    public static ResultadoConsolo Calcular(DadosConsolo c, CargaConsolo carga, ConfiguracaoProjeto cfg = null)
    {
        cfg ??= new ConfiguracaoProjeto();
        var r = new ResultadoConsolo();
        var m = r.Memoria;
        m.Titulo = $"Consolo {c.Nome} — NBR 9062:2017, 7.3" + (cfg.Texto("dim.consoloCurto") == MetodoElDebs ? " e El Debs (2017), 3.5.1" : "");
        string grupo = "Consolo " + c.Nome;
        void K(string cod, string desc, string valor, string limite, bool falha, string refer, SituacaoVerificacao seFalha = SituacaoVerificacao.NaoAtende, string dica = "") =>
            r.Verificacoes.Add(new ResultadoVerificacao
            {
                Codigo = cod, Grupo = "Cálculo — " + grupo, Descricao = desc, Valor = valor, Limite = limite, Referencia = refer,
                Situacao = falha ? seFalha : SituacaoVerificacao.Ok, Dica = falha ? dica : "",
            });

        // materiais e coeficientes da configuração
        double gc = cfg.Numero("normas.gamaC"), gs = cfg.Numero("normas.gamaS"), gg = cfg.Numero("normas.gamaG"), gq = cfg.Numero("normas.gamaQ");
        double fcd = c.FckMPa / gc;                       // MPa
        double fyd = Math.Min(c.FykMPa / gs, 435) / 10;   // kN/cm² (≤ 435 MPa, 7.3.6 d)
        r.D = c.D;
        r.A = carga.A > 0 ? carga.A : cfg.Numero("acoes.posCargaConsolo") * c.A;
        double ad = r.AsobreD, b = c.B, d = r.D;

        // forças de cálculo
        bool perm = carga.Vg >= carga.Vq;
        r.GamaN = carga.Vd > 0 ? 1 : cfg.GamaN(perm);
        r.Vd = carga.Vd > 0 ? carga.Vd : r.GamaN * (gg * carga.Vg + gq * carga.Vq);
        double fatorApoio = carga.FatorHd > 0 ? carga.FatorHd : FatorApoio(cfg.Texto("acoes.apoioPadrao")), hdMin = cfg.Numero("acoes.hdMin");
        r.Hd = carga.Hd > 0 ? carga.Hd : Math.Max(fatorApoio, hdMin) * r.Vd;
        m.Linhas.Add($"Consolo {N(b, "0.#")} × {N(c.H, "0.#")} cm (ponta {N(c.H1, "0.#")} cm), balanço {N(c.A, "0.#")} cm; d = {N(d, "0.0")} cm; fck = {N(c.FckMPa)} MPa; " +
                     $"γc = {N(gc)}, γs = {N(gs)} → fcd = {N(fcd, "0.00")} MPa, fyd = {N(fyd * 10, "0")} MPa (≤ 435).");
        m.Linhas.Add(carga.Vd > 0
            ? $"Força vertical de cálculo digitada: Vd = {N(r.Vd, "0.0")} kN" + (carga.Origem != "" ? $" ({carga.Origem})" : "") + "."
            : $"Vd = γn·(γg·Vg + γq·Vq) = {N(r.GamaN)}·({N(gg)}·{N(carga.Vg, "0.0")} + {N(gq)}·{N(carga.Vq, "0.0")}) = {N(r.Vd, "0.0")} kN " +
              $"(γn da NBR 9062 7.3.1.1, {(perm ? "permanente preponderante" : "variável preponderante")})" + (carga.Origem != "" ? $"; {carga.Origem}" : "") + ".");
        m.Linhas.Add(carga.Hd > 0 ? $"Hd digitado = {N(r.Hd, "0.0")} kN."
            : $"Hd = máx({N(fatorApoio)}; {N(hdMin)})·Vd = {N(r.Hd, "0.0")} kN (ligação{(carga.FatorHd > 0 ? " do aparelho de apoio" : " padrão")}: NBR 9062 7.3.9; mínimo: El Debs, Quadro 3.2).");
        m.Linhas.Add($"a = {N(r.A, "0.0")} cm" + (carga.A > 0 ? " (digitado)" : $" ({N(cfg.Numero("acoes.posCargaConsolo"))} do balanço)") + $" → a/d = {N(ad, "0.00")}.");

        // armadura desenhada
        r.AsTirTem = c.TirN * A(c.TirPhi);
        r.AsCostTem = 2 * c.CostN * A(c.CostPhi);     // grampos horizontais de 2 ramos
        r.AsVertTem = 2 * c.VertN * A(c.VertPhi);     // estribos verticais de 2 ramos
        bool elDebs = cfg.Texto("dim.consoloCurto") == MetodoElDebs;
        double asv;

        if (ad > 2)
        {
            r.Classe = "a/d > 2: viga em balanço";
            m.Linhas.Add("a/d > 2: não é consolo; calcular como viga em balanço (NBR 9062 7.3.5.1 e NBR 6118).");
            K("CC1", "Classe do consolo (a/d)", $"a/d = {N(ad, "0.00")}", "≤ 2", true, "NBR 9062 7.3.2.2", SituacaoVerificacao.Conferir, "Calcule como viga em balanço.");
            return r;
        }
        if (ad > 1)
        {
            r.Classe = "1 < a/d ≤ 2: consolo longo (viga em balanço)";
            m.Linhas.Add("1 < a/d ≤ 2: dimensionar como viga em balanço pela NBR 6118 (flexão e cortante com Vc = 0, 7.3.7.1).");
            K("CC1", "Classe do consolo (a/d)", $"a/d = {N(ad, "0.00")}", "≤ 1 para o modelo de biela e tirante", true, "NBR 9062 7.3.2.2 a",
              SituacaoVerificacao.Conferir, "Consolo longo: confira como viga em balanço.");
        }

        bool muitoCurto = elDebs ? ad < 0.4 : ad <= 0.5;
        double tirH = r.Hd / fyd;
        if (muitoCurto)
        {
            r.Classe = $"a/d {(elDebs ? "< 0,4" : "≤ 0,5")}: consolo muito curto (atrito-cisalhamento)";
            asv = 0.8 * r.Vd / (fyd * carga.Mu);
            r.AsTirNec = asv + tirH;
            r.AsCostNec = 0.5 * asv / d * (2.0 / 3 * d);
            double rho = r.AsTirTem / (b * d);
            r.TauWd = r.Vd / (b * d) * 10;
            r.TauLim = Math.Min(Math.Min(3.0 + 0.9 * rho * fyd * 10, 0.27 * (1 - c.FckMPa / 250) * fcd), 8);
            m.Linhas.Add($"{r.Classe}: Asv = 0,8·Vd/(fyd·μ) = {N(asv)} cm² (μ = {N(carga.Mu)}); As,tir = Asv + Hd/fyd = {N(r.AsTirNec)} cm² (NBR 9062 7.3.5.4).");
            m.Linhas.Add($"Concreto: τwd = Vd/(b·d) = {N(r.TauWd, "0.00")} MPa ≤ τwu = 3,0 + 0,9·ρ·fyd ≤ 0,27·αv2·fcd e ≤ 8 MPa = {N(r.TauLim, "0.00")} MPa (7.3.4.2).");
            m.Linhas.Add($"Costura (7.3.6 b): 0,5·Asv/d em 2/3 d = {N(r.AsCostNec)} cm².");
        }
        else if (elDebs)
        {
            r.Classe = r.Classe == "" ? "0,4 ≤ a/d ≤ 1: consolo curto (biela e tirante, El Debs)" : r.Classe;
            double mv = r.Vd * r.A / (0.9 * d);
            asv = mv / fyd;
            r.AsTirNec = (mv + 1.2 * r.Hd) / fyd;
            r.AsCostNec = 0.5 * asv;
            r.TauWd = r.Vd / (b * d) * 10;
            r.TauLim = 0.2 * fcd * (carga.Indireta ? 0.85 : 1);
            m.Linhas.Add($"{r.Classe}: As,tir = (Vd·a/(0,9d) + 1,2·Hd)/fyd = ({N(mv, "0.0")} + 1,2·{N(r.Hd, "0.0")})/{N(fyd * 10, "0")}·10 = {N(r.AsTirNec)} cm².");
            m.Linhas.Add($"Concreto: τwd = Vd/(b·d) = {N(r.TauWd, "0.00")} MPa ≤ {(carga.Indireta ? "0,85·" : "")}0,2·fcd = {N(r.TauLim, "0.00")} MPa (El Debs, Quadro 3.2).");
            m.Linhas.Add($"Costura: Ash ≥ 0,5·Vd·a/(0,9d·fyd) = {N(r.AsCostNec)} cm², em 2/3 d = {N(2.0 / 3 * d, "0.0")} cm a partir do tirante (El Debs, eq. 3.27).");
        }
        else
        {
            r.Classe = r.Classe == "" ? "0,5 < a/d ≤ 1: consolo curto (biela e tirante, NBR 9062)" : r.Classe;
            asv = (0.1 + ad) * r.Vd / fyd;
            r.AsTirNec = asv + tirH;
            r.AsCostNec = 0.4 * asv / d * (2.0 / 3 * d);
            r.TauWd = r.Vd / (b * d) * 10;
            r.TauLim = 0.2 * fcd * (carga.Indireta ? 0.85 : 1);
            m.Linhas.Add($"{r.Classe}: Asv = (0,1 + a/d)·Fd/fyd = {N(asv)} cm²; As,tir = Asv + Hd/fyd = {N(r.AsTirNec)} cm² (7.3.5.3).");
            m.Linhas.Add($"Costura (7.3.6 a): 0,4·Asv/d em 2/3 d = {N(r.AsCostNec)} cm².");
            m.Linhas.Add($"Biela: a NBR 9062 (7.3.4.1) não fixa a largura da biela; estimativa pela tensão de referência τwd = {N(r.TauWd, "0.00")} MPa ≤ 0,2·fcd = {N(r.TauLim, "0.00")} MPa (El Debs).");
        }

        // estribos verticais: 0,15 % b·h (NBR 9062 7.3.7.2) e ≥ 0,2·As,tir (El Debs)
        double vertMin = 0.0015 * b * c.H;
        r.AsVertNec = Math.Max(vertMin, elDebs ? 0.2 * r.AsTirNec : 0);
        m.Linhas.Add($"Estribos verticais: ≥ 0,15 %·b·h = {N(vertMin)} cm²" + (elDebs ? $" e ≥ 0,2·As,tir = {N(0.2 * r.AsTirNec)} cm²" : "") + $" → {N(r.AsVertNec)} cm².");
        double omega = r.AsTirTem / (b * d) * c.FykMPa / c.FckMPa;
        m.Linhas.Add($"Desenhado: tirante {c.TirN} ø{N(c.TirPhi, "0.#")} = {N(r.AsTirTem)} cm²; costura {c.CostN} × 2 ø{N(c.CostPhi, "0.#")} = {N(r.AsCostTem)} cm²; " +
                     $"verticais {c.VertN} × 2 ø{N(c.VertPhi, "0.#")} = {N(r.AsVertTem)} cm²; taxa mecânica ω = ρ·fyk/fck = {N(omega, "0.000")}.");

        bool estimativa = !elDebs && !muitoCurto;
        K("CC2", "Compressão do concreto (tensão de referência)", $"τwd = {N(r.TauWd, "0.00")} MPa", $"≤ {N(r.TauLim, "0.00")} MPa", r.TauWd > r.TauLim + 1e-9,
          muitoCurto ? "NBR 9062 7.3.4.2" : elDebs ? "El Debs (2017) Quadro 3.2" : "NBR 9062 7.3.4.1 (estimativa)",
          estimativa ? SituacaoVerificacao.Conferir : SituacaoVerificacao.NaoAtende, "Aumente a altura ou a largura do consolo.");
        K("CC3", "Armadura do tirante", $"{c.TirN} ø{N(c.TirPhi, "0.#")} = {N(r.AsTirTem)} cm²", $"≥ {N(r.AsTirNec)} cm²", r.AsTirTem < r.AsTirNec - 1e-9,
          elDebs && !muitoCurto ? "El Debs (2017) Quadro 3.2" : "NBR 9062 7.3.5", dica: "Aumente a bitola ou o número de barras do tirante.");
        K("CC4", "Taxa mecânica do tirante (ω = ρ·fyk/fck)", N(omega, "0.000"), "≥ 0,04", omega < 0.04 - 1e-9, "NBR 9062 7.3.5.2");
        K("CC5", "Armadura de costura (em 2/3 d)", $"{N(r.AsCostTem)} cm²", $"≥ {N(r.AsCostNec)} cm²", r.AsCostTem < r.AsCostNec - 1e-9,
          elDebs && !muitoCurto ? "El Debs (2017) eq. 3.27" : "NBR 9062 7.3.6", dica: "Aumente os grampos horizontais.");
        K("CC6", "Estribos verticais", $"{N(r.AsVertTem)} cm²", $"≥ {N(r.AsVertNec)} cm²", r.AsVertTem < r.AsVertNec - 1e-9,
          "NBR 9062 7.3.7.2" + (elDebs ? " · El Debs" : ""), dica: "Aumente os estribos verticais.");
        foreach (var f in cfg.ForaDaNorma())
            m.Linhas.Add("Configuração fora da norma: " + f.Texto + ".");
        return r;
    }
}
