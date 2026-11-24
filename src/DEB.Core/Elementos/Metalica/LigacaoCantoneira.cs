using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Documentacao;
using DEB.Core.Normas.Verificacao;

namespace DEB.Core.Elementos.Metalica;

/// <summary>
/// Ligação flexível por cantoneira dupla (D49): duas cantoneiras iguais, uma de cada lado da alma da viga, parafusadas na alma
/// (corte duplo) e parafusadas ou soldadas no suporte. Dimensões em mm; forças em kN.
/// </summary>
public sealed class DadosCantoneira
{
    public PerfilI Viga { get; set; }
    public AcoEstrutural AcoViga { get; set; }
    public AcoEstrutural AcoCantoneira { get; set; }
    public TipoParafuso Parafuso { get; set; } = TipoParafuso.Lista[2];
    public bool RoscaNoPlano { get; set; } = true;
    public double Db { get; set; } = 19.05;
    public int N { get; set; } = 3;
    public double S { get; set; } = 75;
    public double Aba { get; set; } = 76.2;
    public double T { get; set; } = 7.94;
    public double Ev { get; set; } = 32;
    public double Eh { get; set; } = 32;
    public bool SoldadaNoSuporte { get; set; }
    public double Perna { get; set; } = 6;
    public double Fw { get; set; } = 485;
    public double Comprimento => 2 * Ev + (N - 1) * S;

    public static DadosCantoneira DoModelo(Ligacoes.ModeloLigacao m, PerfilI viga, AcoEstrutural acoViga)
    {
        var d = new DadosCantoneira
        {
            Viga = viga, AcoViga = acoViga, AcoCantoneira = AcoEstrutural.Buscar(m.Texto("acoCantoneira")) ?? AcoEstrutural.Buscar("A36"),
            Parafuso = TipoParafuso.Buscar(m.Texto("parafuso")), RoscaNoPlano = m.Texto("rosca") != "Não", Db = m.Numero("db"), S = m.Numero("s"),
            Aba = m.Numero("aba"), T = m.Numero("t"), Ev = m.Numero("ev"), Eh = m.Numero("eh"), SoldadaNoSuporte = m.Texto("noSuporte") == "Soldada",
            Perna = m.Numero("perna"), Fw = m.Numero("fw"),
        };
        if (m.EhAuto("n"))
        {
            double livre = viga.D - 2 * viga.Tf - 2 * Math.Max(viga.R, 10);
            int n = Math.Max(2, (int)Math.Floor((livre - 2 * d.Ev) / d.S) + 1);
            while (n > 2 && 2 * d.Ev + (n - 1) * d.S > livre) n--;
            d.N = n;
        }
        else d.N = (int)m.Numero("n");
        return d;
    }
}

public static class LigacaoCantoneira
{
    private static string F(double x, string f = "0.#") => Fmt.Virgula(x, f);

    /// <summary>
    /// Verificações CD1…CD8 para (Vd, Nd): parafusos na alma da viga em corte duplo com a excentricidade da aba (do suporte à fila),
    /// parafusos no suporte (corte simples, 2·n) ou solda, pressão de contato na alma e nas cantoneiras, cisalhamento e rasgamento
    /// das cantoneiras.
    /// </summary>
    public static ListaVerificacoes Verificar(DadosCantoneira d, IEnumerable<(string Nome, double Vd, double Nd)> casos, string grupo,
                                              out PassoMemoria mem, double gamaA1 = 1.10, double gamaA2 = 1.35)
    {
        mem = new PassoMemoria { Titulo = "Ligação flexível por cantoneira dupla (NBR 8800:2024, 6.2, 6.3 e 6.5)" };
        var v = new ListaVerificacoes();
        var cs = casos.ToList();
        double dh = ComponentesLigacao.Furo(d.Db), dhLiq = dh + 2, L = d.Comprimento, t = d.T;
        double fv1 = ComponentesLigacao.CisalhamentoParafuso(d.Db, d.Parafuso, d.RoscaNoPlano, gamaA2);
        double e = d.Aba - d.Eh;                                            // da face do suporte (dorso) à fila na alma
        var ys = Enumerable.Range(0, d.N).Select(i => (i - (d.N - 1) / 2.0) * d.S).ToArray();
        double sumY2 = ys.Sum(y => y * y), yMax = ys.Max(y => Math.Abs(y));
        double Alma(double vd, double nd)   // força num parafuso da alma (os dois planos juntos)
        {
            double fx = nd / d.N + (sumY2 > 0 ? vd * e * yMax / sumY2 : 0), fy = vd / d.N;
            return Math.Sqrt(fx * fx + fy * fy);
        }
        (double, string) Pior(Func<(string Nome, double Vd, double Nd), double> f) =>
            cs.Count == 0 ? (0, "") : cs.Select(c => (f(c), c.Nome)).OrderByDescending(x => x.Item1).First();
        void K(string cod, string desc, (double Eta, string Caso) p, string refer, string dica = "") =>
            v.Add(new ResultadoVerificacao { Codigo = cod, Grupo = grupo, Descricao = desc, Valor = $"η = {F(p.Eta, "0.00")}" + (p.Caso != "" ? $" ({p.Caso})" : ""),
                Limite = "≤ 1,00", Referencia = refer, Situacao = p.Eta > 1 + 1e-9 ? SituacaoVerificacao.NaoAtende : SituacaoVerificacao.Ok, Dica = p.Eta > 1 + 1e-9 ? dica : "" });

        mem.Linhas.Add($"2 cantoneiras L {F(d.Aba)} × {F(t)} mm × {F(L, "0")} mm ({d.AcoCantoneira.Nome}); {d.N} parafusos ø{F(d.Db)} {d.Parafuso.Nome} na alma (corte duplo), " +
                       (d.SoldadaNoSuporte ? $"soldadas no suporte (filete {F(d.Perna)} mm)" : $"{2 * d.N} parafusos no suporte (corte simples)") +
                       $"; excentricidade na alma e = aba − eh = {F(e, "0")} mm (método elástico).");
        K("CD1", "Parafusos na alma da viga (corte duplo, grupo excêntrico)", Pior(c => Alma(c.Vd, c.Nd) / (2 * fv1)), "NBR 8800 6.3.3.2 e 6.5.2",
          "Aumente o número ou o diâmetro dos parafusos.");
        if (!d.SoldadaNoSuporte)
            K("CD2", "Parafusos no suporte (corte simples)", Pior(c => Math.Sqrt(Math.Pow(c.Vd / (2 * d.N), 2) + Math.Pow(Math.Max(0, c.Nd) / (2 * d.N), 2)) / fv1),
              "NBR 8800 6.3.3.2", "Aumente o número ou o diâmetro dos parafusos.");
        else
        {
            double fw = ComponentesLigacao.FiletePorMm(d.Perna, d.Fw);
            // dois cordões verticais (um por cantoneira), com o momento V·(aba)/2 por cantoneira, elástico
            K("CD2", "Solda das cantoneiras no suporte", Pior(c =>
            {
                double v1 = c.Vd / 2, fvv = v1 / L, fm = v1 * d.Aba * 6 / (L * L), fn = Math.Max(0, c.Nd) / 2 / L;
                return Math.Sqrt(fvv * fvv + Math.Pow(fm + fn, 2)) / fw;
            }), "NBR 8800 6.2.5 (Tab. 9)", "Aumente o filete.");
        }
        double lfAlma = Math.Min(d.Eh - dh / 2, d.N > 1 ? d.S - dh : 1e9);
        double lfCant = Math.Min(Math.Min(d.Ev, d.Eh) - dh / 2, d.N > 1 ? d.S - dh : 1e9);
        double fcAlma = ComponentesLigacao.PressaoContato(lfAlma, d.Viga.Tw, d.Db, d.AcoViga.Fu, true, gamaA2);
        double fcCant = 2 * ComponentesLigacao.PressaoContato(lfCant, t, d.Db, d.AcoCantoneira.Fu, true, gamaA2);
        K("CD3", "Pressão de contato na alma da viga", Pior(c => Alma(c.Vd, c.Nd) / fcAlma), "NBR 8800 6.3.3.3");
        K("CD4", "Pressão de contato nas cantoneiras", Pior(c => Alma(c.Vd, c.Nd) / fcCant), "NBR 8800 6.3.3.3");
        double ag = 2 * L * t, anv = 2 * (L - d.N * dhLiq) * t;
        double vRd = Math.Min(0.6 * d.AcoCantoneira.Fy * ag / gamaA1, 0.6 * d.AcoCantoneira.Fu * anv / gamaA2) / 1000;
        K("CD5", "Cantoneiras ao cisalhamento (escoamento e ruptura)", Pior(c => c.Vd / vRd), "NBR 8800 6.5.5", "Aumente a espessura ou o comprimento.");
        double agv = 2 * (L - d.Ev) * t, anvB = agv - 2 * (d.N - 0.5) * dhLiq * t, ant = 2 * (d.Eh - 0.5 * dhLiq) * t;
        double rasg = ComponentesLigacao.Rasgamento(agv, anvB, ant, d.AcoCantoneira.Fy, d.AcoCantoneira.Fu, 1.0, gamaA2);
        K("CD6", "Cantoneiras ao colapso por rasgamento", Pior(c => c.Vd / rasg), "NBR 8800 6.5.6", "Aumente as bordas.");
        mem.Linhas.Add($"Fv,Rd = {F(fv1, "0.0")} kN por plano; pressão de contato: alma {F(fcAlma, "0.0")} kN, cantoneiras {F(fcCant, "0.0")} kN por parafuso; " +
                       $"cantoneiras ao cisalhamento {F(vRd, "0")} kN; rasgamento {F(rasg, "0")} kN.");
        bool geo = (d.N < 2 || d.S >= 3 * d.Db - 1e-9) && Math.Min(d.Ev, d.Eh) >= 1.25 * d.Db && L <= d.Viga.D - 2 * d.Viga.Tf + 1e-9;
        v.Add(new ResultadoVerificacao { Codigo = "CD7", Grupo = grupo, Descricao = "Espaçamento, bordas e altura das cantoneiras",
            Valor = $"s = {F(d.S, "0")} mm, bordas ≥ {F(Math.Min(d.Ev, d.Eh), "0")} mm, L = {F(L, "0")} mm", Limite = $"s ≥ 3·db; bordas ≥ 1,25·db; L ≤ altura livre da alma",
            Referencia = "NBR 8800 6.3.9 e 6.3.11", Situacao = geo ? SituacaoVerificacao.Ok : SituacaoVerificacao.Conferir });
        bool rot = t <= 16 + 1e-9;
        v.Add(new ResultadoVerificacao { Codigo = "CD8", Grupo = grupo, Descricao = "Capacidade de rotação (ligação flexível)", Valor = $"t = {F(t)} mm", Limite = "≤ 16 mm",
            Referencia = "NBR 8800 6.1.3 (boa prática do AISC Manual, parte 10)", Situacao = rot ? SituacaoVerificacao.Ok : SituacaoVerificacao.Conferir });
        return v;
    }
}
