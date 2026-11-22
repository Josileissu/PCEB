using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Normas.Verificacao;

/// <summary>Dados de um dente Gerber para as verificações de detalhamento (cm; bitolas em mm), do que o template desenha.</summary>
public sealed class DadosDente
{
    /// <summary>Identificação no relatório ("ponta esquerda, DG 45x25").</summary>
    public string Nome { get; set; } = "";
    /// <summary>Largura da viga, altura útil da viga (dvig), altura e comprimento do dente.</summary>
    public double B { get; set; }
    public double DVig { get; set; }
    public double Altura { get; set; }
    public double Comprimento { get; set; }
    public double CobTopo { get; set; } = 2.5;
    public double CobPontas { get; set; } = 2.5;
    /// <summary>Estribos verticais do dente (Gerber, fechados, 2 ramos).</summary>
    public int GerberN { get; set; }
    public double GerberPhi { get; set; }
    /// <summary>Tirante: barras retas no topo do dente com barra transversal soldada de mesmo φ na ponta; comprimento da ponta (a cobrimento).</summary>
    public int TirN { get; set; }
    public double TirPhi { get; set; }
    public double TirComp { get; set; }
    /// <summary>Estribos horizontais em U (grampo em cima, costura embaixo): fechados na ponta, pernas entrando na viga (comprimento da ponta).</summary>
    public int GrampoN { get; set; }
    public double GrampoPhi { get; set; }
    public double GrampoPerna { get; set; }
    public int CosturaN { get; set; }
    public double CosturaPhi { get; set; }
    public double CosturaPerna { get; set; }
    /// <summary>Suspensão: estribos fechados de altura total; posições (eixo) medidas da ponta.</summary>
    public double SuspPhi { get; set; }
    public List<double> SuspPosicoes { get; set; } = new List<double>();
    /// <summary>Trecho da suspensão a partir da face do dente: cobrimento + dvig/4 arredondado para baixo (D36).</summary>
    public double TrechoSuspensao { get; set; }
    /// <summary>Furos de pino desenhados nesta ponta.</summary>
    public int Furos { get; set; }
    public double FckMPa { get; set; } = 40;
    public string Aco { get; set; } = "CA-50";
    /// <summary>Força no apoio do dente (kN, de cálculo) quando há cálculo (D40); null = sem cálculo.</summary>
    public double? Vd { get; set; }

    /// <summary>Altura útil do dente (cm): até o centro do tirante, por dentro do estribo Gerber.</summary>
    public double D => Altura - CobTopo - (GerberN > 0 ? GerberPhi / 10 : 0) - TirPhi / 20;
}

/// <summary>
/// Regras D* do dente Gerber (NBR 9062:2017, 7.4), sem esforços (D19/D36). D5 reusa as regras do consolo (7.4.5.3: 7.3.3 e 7.3.6)
/// com os dados do dente; as que dependem do aparelho de apoio (C1, C3–C5) não entram — a viga não tem o apoio. Texto conferido no
/// PDF da norma (p. 55–57; Fig. 23: Lb = comprimento de ancoragem para má aderência).
/// </summary>
public static class VerificacoesDente
{
    private static string N(double x, string f = "0.#") => Fmt.Virgula(x, f);

    private static ResultadoVerificacao R(DadosDente t, string cod, string desc, string valor, string limite, bool falha, string refer,
                                          SituacaoVerificacao seFalha = SituacaoVerificacao.NaoAtende, string dica = "") =>
        new ResultadoVerificacao
        {
            Codigo = cod, Grupo = "Dente " + t.Nome, Descricao = desc, Valor = valor, Limite = limite, Referencia = refer, Dica = dica,
            Situacao = falha ? seFalha : SituacaoVerificacao.Ok,
        };

    /// <summary>lb básico (cm) com má aderência (Fig. 23), pela regra de ancoragem do núcleo.</summary>
    public static double LbMa(double phiMm, double fck, string aco) =>
        new RegrasTraspasse { Fck = fck, Aderencia = SituacaoAderencia.Ma }.Lb(phiMm, aco);

    /// <summary>Furos de pino esperados pela largura (D23): b &lt; 30 cm → 1; b ≥ 30 cm → 2.</summary>
    public static int FurosEsperados(double b) => b < 30 - 1e-6 ? 1 : 2;

    public static List<ResultadoVerificacao> Verificar(DadosDente t)
    {
        var r = new List<ResultadoVerificacao>();
        // D1: tirante ancorado no dente por barra transversal soldada de mesmo φ (o template desenha sempre a soldada na ponta)
        r.Add(t.TirN > 0 && t.TirPhi > 0
            ? R(t, "D1", "Tirante ancorado no dente por barra transversal soldada de mesmo diâmetro", $"{t.TirN} ø{N(t.TirPhi)} + soldada ø{N(t.TirPhi)}",
                "", false, "NBR 9062, 7.4.4.1")
            : R(t, "D1", "Dente sem tirante", "", "", true, "NBR 9062, 7.4.4.1"));

        // D2: a ancoragem do tirante começa a (dvig − d) do 1º estribo de suspensão, com lb de má aderência. Sem As,calc o
        // limite rigoroso é lb (lb,nec = lb·As,calc/As,ef ≥ lb,mín): abaixo de lb e acima de lb,mín fica "Conferir".
        if (t.TirN > 0 && t.TirPhi > 0)
        {
            double x1 = t.SuspPosicoes.Count > 0 ? t.SuspPosicoes.Min() : t.Comprimento;
            double inicio = x1 + Math.Max(0, t.DVig - t.D), fim = t.CobPontas + t.TirComp, disp = fim - inicio;
            double lb = LbMa(t.TirPhi, t.FckMPa, t.Aco), lbMin = Math.Max(0.3 * lb, Math.Max(10 * t.TirPhi / 10, 10));
            var sit = disp >= lb - 1e-6 ? SituacaoVerificacao.Ok : disp >= lbMin - 1e-6 ? SituacaoVerificacao.Conferir : SituacaoVerificacao.NaoAtende;
            r.Add(new ResultadoVerificacao
            {
                Codigo = "D2", Grupo = "Dente " + t.Nome, Situacao = sit, Referencia = "NBR 9062, 7.4.4.2, Fig. 23",
                Descricao = $"Ancoragem do tirante na viga, a partir de (dvig − d) = {N(t.DVig - t.D)} cm do 1º estribo de suspensão",
                Valor = $"{N(disp)} cm", Limite = $"≥ lb (má aderência) = {N(lb)} cm",
                Dica = sit == SituacaoVerificacao.Conferir ? $"Acima do lb,mín = {N(lbMin)} cm: atende se As,calc/As,ef ≤ {N(disp / lb, "0.00")}."
                     : sit == SituacaoVerificacao.NaoAtende ? $"Abaixo do lb,mín = {N(lbMin)} cm: aumentar o comprimento do tirante." : "",
            });
        }

        // D3: estribos horizontais (U fechado na ponta = ancorado na face externa) penetrando 1,5·lb (má aderência, Fig. 23) na viga
        foreach (var (nome, n, phi, perna) in new[] { ("grampo", t.GrampoN, t.GrampoPhi, t.GrampoPerna), ("costura", t.CosturaN, t.CosturaPhi, t.CosturaPerna) })
        {
            if (n <= 0 || phi <= 0) continue;
            double pen = t.CobPontas + perna - t.Comprimento, min = 1.5 * LbMa(phi, t.FckMPa, t.Aco);
            r.Add(R(t, "D3", $"Estribos horizontais do dente ({nome} ø{N(phi)}, U fechado na ponta): penetração na viga", $"{N(pen)} cm",
                    $"≥ 1,5·lb (má aderência) = {N(min)} cm", pen < min - 1e-6, "NBR 9062, 7.4.5.1"));
        }
        if (t.GrampoN <= 0 && t.CosturaN <= 0)
            r.Add(R(t, "D3", "Dente sem estribos horizontais", "", "", true, "NBR 9062, 7.4.5.1"));

        // D4: estribos verticais no dente (7.3.7): mesma leitura do C15 (área total dos ramos ≥ 0,15 % de b·h)
        double asVert = t.GerberN * 2 * Math.PI * t.GerberPhi * t.GerberPhi / 400, minVert = 0.0015 * t.B * t.Altura;
        r.Add(R(t, "D4", "Estribos verticais no dente: área total ≥ 0,15 % de b·h", $"{N(asVert, "0.00")} cm²", $"≥ {N(minVert, "0.00")} cm²",
                asVert < minVert - 1e-9, "NBR 9062, 7.4.5.2 e 7.3.7.2", t.GerberN <= 0 ? SituacaoVerificacao.NaoAtende : SituacaoVerificacao.Conferir,
                t.GerberN <= 0 ? "Os estribos verticais no dente são obrigatórios."
                               : "Leitura da taxa como área total dos ramos verticais no dente; conferir com o livro de comentários."));

        // D5: regras do consolo (7.3.3 e 7.3.6) com o dente como consolo: balanço = comprimento, h = h1 = altura, tirante com barra
        // soldada, costura = os U de baixo (o grampo não entra na costura), cobrimento até o tirante = topo + estribo Gerber
        if (t.TirN > 0 && t.TirPhi > 0)
        {
            var c = new DadosConsolo
            {
                Nome = t.Nome, B = t.B, A = t.Comprimento, H = t.Altura, H1 = t.Altura, Cob = t.CobTopo + (t.GerberN > 0 ? t.GerberPhi / 10 : 0),
                TirN = t.TirN, TirPhi = t.TirPhi, TirAlca = false, CostN = t.CosturaN, CostPhi = t.CosturaPhi, VertN = t.GerberN, VertPhi = t.GerberPhi,
                FckMPa = t.FckMPa, FykMPa = TabelasNbr.Fyk(t.Aco),
            };
            var aplicaveis = new[] { "C2", "C6", "C7", "C9", "C10", "C11", "C12" };
            foreach (var x in VerificacoesConsolo.Verificar(c).Where(x => aplicaveis.Contains(x.Codigo)))
            {
                x.Descricao = $"{x.Descricao} (regra {x.Codigo} do consolo)";
                x.Codigo = "D5";
                x.Grupo = "Dente " + t.Nome;
                r.Add(x);
            }
        }

        // D6: suspensão concentrada junto ao dente, no trecho cobrimento + dvig/4 a partir da face (D36), com estribos fechados de
        // altura total (envolvem a longitudinal, pela forma do template)
        if (t.SuspPosicoes.Count == 0)
            r.Add(R(t, "D6", "Dente sem armadura de suspensão", "", "", true, "NBR 9062, 7.4.6"));
        else
        {
            double ini = t.SuspPosicoes.Min() - t.SuspPhi / 20 - t.Comprimento, fim = t.SuspPosicoes.Max() + t.SuspPhi / 20 - t.Comprimento;
            r.Add(R(t, "D6", $"Suspensão: {t.SuspPosicoes.Count} estribos fechados de altura total, junto à face do dente",
                    $"de {N(ini)} a {N(fim)} cm da face", $"dentro de cobrimento + dvig/4 = {N(t.TrechoSuspensao)} cm",
                    ini < -1e-6 || (t.TrechoSuspensao > 0 && fim > t.TrechoSuspensao + 1e-6), "NBR 9062, 7.4.6.2, Fig. 23; D36"));
            // espaçamento livre entre os estribos da suspensão (NBR 6118, 18.3.2.2)
            if (t.SuspPosicoes.Count > 1)
            {
                var xs = t.SuspPosicoes.OrderBy(x => x).ToList();
                double livre = xs.Zip(xs.Skip(1), (a, b) => b - a).Min() - t.SuspPhi / 10, min = Math.Max(2, t.SuspPhi / 10);
                r.Add(R(t, "D6", "Espaçamento livre entre os estribos de suspensão", $"{N(livre)} cm", $"≥ {N(min)} cm",
                        livre < min - 1e-6, "NBR 6118, 18.3.2.2", SituacaoVerificacao.Conferir,
                        livre < min - 1e-6 ? "Use menos estribos de bitola maior ou ramos duplos." : ""));
            }
        }
        // D7: barras verticais na suspensão absorvem no máximo 0,4·Fd; com o cálculo (D40) o Fd é conhecido
        if (t.Vd is double fd)
            r.Add(R(t, "D7", "Suspensão por barras verticais: no máximo 0,4·Fd (a do desenho é só de estribos fechados)", "0 kN (só estribos fechados)",
                    $"≤ 0,4·Fd = {N(0.4 * fd, "0.0")} kN", false, "NBR 9062, 7.4.6.2"));
        else
            r.Add(R(t, "D7", "Suspensão por barras verticais: no máximo 0,4·Fd (a do desenho é só de estribos fechados)", "", "≤ 0,4·Fd", true,
                    "NBR 9062, 7.4.6.2", SituacaoVerificacao.Conferir, "Depende de Fd (fase de cálculo)."));

        // D8: furos de pino pela largura (regra do escritório, D23)
        int esp = FurosEsperados(t.B);
        r.Add(R(t, "D8", $"Furos de pino no dente (b = {N(t.B)} cm)", $"{t.Furos} furo(s)", $"{esp} (b < 30 cm: 1; b ≥ 30 cm: 2)",
                t.Furos != esp, "D23, D36"));

        return r;
    }
}
