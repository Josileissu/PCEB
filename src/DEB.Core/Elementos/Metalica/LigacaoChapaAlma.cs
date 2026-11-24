using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Documentacao;
using DEB.Core.Normas.Verificacao;

namespace DEB.Core.Elementos.Metalica;

/// <summary>Parafuso estrutural (NBR 8800:2024 Tab. A.3): fub em MPa.</summary>
public sealed class TipoParafuso
{
    public string Nome { get; set; } = "";
    public double Fyb { get; set; }
    public double Fub { get; set; }
    public bool AltaResistencia { get; set; }

    public static IReadOnlyList<TipoParafuso> Lista { get; } = new[]
    {
        new TipoParafuso { Nome = "ASTM A307", Fub = 415 },
        new TipoParafuso { Nome = "ISO 4016 Classe 4.6", Fyb = 235, Fub = 400 },
        new TipoParafuso { Nome = "ASTM F3125 A325", Fyb = 635, Fub = 830, AltaResistencia = true },
        new TipoParafuso { Nome = "ISO 4016 Classe 8.8", Fyb = 640, Fub = 800, AltaResistencia = true },
        new TipoParafuso { Nome = "ASTM F3125 A490", Fyb = 895, Fub = 1040, AltaResistencia = true },
        new TipoParafuso { Nome = "ISO 4016 Classe 10.9", Fyb = 900, Fub = 1000, AltaResistencia = true },
    };

    public static TipoParafuso Buscar(string nome) =>
        Lista.FirstOrDefault(p => p.Nome.Replace(" ", "").IndexOf((nome ?? "").Replace(" ", ""), StringComparison.OrdinalIgnoreCase) >= 0) ?? Lista[2];
}

/// <summary>Componentes de parafusos, chapas e soldas pela NBR 8800:2024 (6.2, 6.3, 6.5). Forças em kN; dimensões em mm; tensões em MPa.</summary>
public static class ComponentesLigacao
{
    public const double GamaW2 = 1.35;

    /// <summary>Furo-padrão (6.3.6, Tab. 12): db + 1,5 mm até 24 mm; db + 3 mm acima.</summary>
    public static double Furo(double db) => db <= 24 + 1e-9 ? db + 1.5 : db + 3;

    /// <summary>Cisalhamento por plano de corte (6.3.3.2): 0,45·Ab·fub/γa2 (rosca no plano ou parafuso comum) ou 0,56·Ab·fub/γa2.</summary>
    public static double CisalhamentoParafuso(double db, TipoParafuso p, bool roscaNoPlano, double gamaA2 = 1.35) =>
        (roscaNoPlano || !p.AltaResistencia ? 0.45 : 0.56) * Math.PI * db * db / 4 * p.Fub / gamaA2 / 1000;

    /// <summary>Tração (6.3.3.1): 0,75·Ab·fub/γa2.</summary>
    public static double TracaoParafuso(double db, TipoParafuso p, double gamaA2 = 1.35) => 0.75 * Math.PI * db * db / 4 * p.Fub / gamaA2 / 1000;

    /// <summary>
    /// Pressão de contato e rasgamento num furo-padrão (6.3.3.3-a): 1,2·ℓf·t·fu ≤ 2,4·db·t·fu (deformação no furo limitada) ou
    /// 1,5·ℓf·t·fu ≤ 3,0·db·t·fu (não limitada), dividido por γa2.
    /// </summary>
    public static double PressaoContato(double lf, double t, double db, double fu, bool deformacaoLimitada, double gamaA2 = 1.35)
    {
        double a = deformacaoLimitada ? 1.2 : 1.5, b = deformacaoLimitada ? 2.4 : 3.0;
        return Math.Min(a * Math.Max(0, lf) * t * fu, b * db * t * fu) / gamaA2 / 1000;
    }

    /// <summary>Colapso por rasgamento (6.5.6): (0,6·fu·Anv + Cts·fu·Ant) ≤ (0,6·fy·Agv + Cts·fu·Ant), dividido por γa2 (áreas em mm²).</summary>
    public static double Rasgamento(double agv, double anv, double ant, double fy, double fu, double cts = 1.0, double gamaA2 = 1.35) =>
        Math.Min(0.6 * fu * anv + cts * fu * ant, 0.6 * fy * agv + cts * fu * ant) / gamaA2 / 1000;

    /// <summary>Filete de solda (Tabela 9): 0,6·Aw·fw/γw2 por mm de comprimento, Aw = garganta 0,707·perna.</summary>
    public static double FiletePorMm(double perna, double fw, double gamaW2 = GamaW2) => 0.6 * 0.707 * perna * fw / gamaW2 / 1000;
}

/// <summary>
/// Ligação flexível por chapa de alma (D48): chapa soldada ao suporte (mesa do pilar ou alma da viga de apoio) com dois filetes e
/// parafusada na alma da viga, numa fila vertical. Dimensões em mm. Vd e Nd de cálculo em kN (Nd &gt; 0 tração).
/// </summary>
public sealed class DadosChapaAlma
{
    public PerfilI Viga { get; set; }
    public AcoEstrutural AcoViga { get; set; }
    public AcoEstrutural AcoChapa { get; set; }
    public TipoParafuso Parafuso { get; set; } = TipoParafuso.Lista[2];
    public bool RoscaNoPlano { get; set; } = true;
    public double Db { get; set; } = 19.05;
    public int N { get; set; } = 3;
    /// <summary>Passo vertical entre parafusos.</summary>
    public double S { get; set; } = 75;
    public double Tp { get; set; } = 9.5;
    /// <summary>Distância vertical do topo da chapa ao primeiro furo (e do último à base).</summary>
    public double Ev { get; set; } = 38;
    /// <summary>Distância horizontal da linha de parafusos à borda livre da chapa.</summary>
    public double EhChapa { get; set; } = 38;
    /// <summary>Distância horizontal da linha de parafusos à ponta da viga (alma).</summary>
    public double EhViga { get; set; } = 38;
    /// <summary>Excentricidade: da face do suporte (solda) à linha de parafusos.</summary>
    public double A { get; set; } = 75;
    /// <summary>Perna do filete (cada lado) e resistência do eletrodo.</summary>
    public double Perna { get; set; } = 6;
    public double Fw { get; set; } = 485;
    public double AlturaChapa => 2 * Ev + (N - 1) * S;
    public bool DeformacaoLimitada { get; set; } = true;

    /// <summary>
    /// Proposta para a viga (D48): parafusos ø19 (¾") a cada 75 mm, bordas de 38 mm, chapa de 9,5 mm (≤ db/2 + 1,6: gira como
    /// rótula), altura entre metade da altura da viga e a altura livre da alma; filetes de 6 mm (eletrodo 70).
    /// </summary>
    public static DadosChapaAlma Proposta(PerfilI viga, AcoEstrutural acoViga, AcoEstrutural acoChapa, TipoParafuso parafuso)
    {
        var d = new DadosChapaAlma { Viga = viga, AcoViga = acoViga, AcoChapa = acoChapa, Parafuso = parafuso };
        double livre = viga.D - 2 * viga.Tf - 2 * Math.Max(viga.R, 10);
        int n = Math.Max(2, (int)Math.Floor((livre - 2 * d.Ev) / d.S) + 1);
        while (n > 2 && d.Ev * 2 + (n - 1) * d.S > livre) n--;
        d.N = n;
        return d;
    }
}

public static class LigacaoChapaAlma
{
    private static string F(double x, string f = "0.#") => Fmt.Virgula(x, f);

    /// <summary>Verifica a ligação para os pares (Vd, Nd); devolve as verificações LC1…LC9 e a memória.</summary>
    public static ListaVerificacoes Verificar(DadosChapaAlma d, IEnumerable<(string Nome, double Vd, double Nd)> casos, string grupo, out PassoMemoria mem,
                                              double gamaA1 = 1.10, double gamaA2 = 1.35)
    {
        mem = new PassoMemoria { Titulo = "Ligação flexível por chapa de alma (NBR 8800:2024, 6.2, 6.3 e 6.5)" };
        var v = new ListaVerificacoes();
        var cs = casos.ToList();
        double dh = ComponentesLigacao.Furo(d.Db), dhLiq = dh + 2.0;      // área líquida: furo + 2 mm (5.2.4.1)
        double hp = d.AlturaChapa, tw = d.Viga.Tw, tp = d.Tp;
        double fyP = d.AcoChapa.Fy, fuP = d.AcoChapa.Fu, fuV = d.AcoViga.Fu;
        mem.Linhas.Add($"{d.N} parafusos ø{F(d.Db)} mm {d.Parafuso.Nome} (furo {F(dh)} mm), passo {F(d.S, "0")} mm, numa fila; chapa {F(tp)} × {F(hp, "0")} mm {d.AcoChapa.Nome}; " +
                       $"alma da viga {d.Viga.Nome} tw = {F(tw)} mm; excentricidade da solda à fila e = {F(d.A, "0")} mm; filetes de {F(d.Perna)} mm (fw = {F(d.Fw, "0")} MPa) nos dois lados.");

        // ---- parafusos: grupo excêntrico pelo método elástico (V e N no centro do grupo; M = V·e)
        double fv1 = ComponentesLigacao.CisalhamentoParafuso(d.Db, d.Parafuso, d.RoscaNoPlano, gamaA2);
        var ys = Enumerable.Range(0, d.N).Select(i => (i - (d.N - 1) / 2.0) * d.S).ToArray();
        double sumY2 = ys.Sum(y => y * y), yMax = ys.Max(y => Math.Abs(y));
        double Forca(double vd, double nd)
        {
            double m = vd * d.A;                                         // kN·mm
            double fx = nd / d.N + (sumY2 > 0 ? m * yMax / sumY2 : 0);   // horizontal: normal + momento
            double fy = vd / d.N;
            return Math.Sqrt(fx * fx + fy * fy);
        }
        (double Eta, string Caso) Pior(Func<(string Nome, double Vd, double Nd), double> f) =>
            cs.Count == 0 ? (0, "") : cs.Select(c => (f(c), c.Nome)).OrderByDescending(x => x.Item1).First();
        void K(string cod, string desc, (double Eta, string Caso) p, string refer, string dica = "") =>
            v.Add(new ResultadoVerificacao
            {
                Codigo = cod, Grupo = grupo, Descricao = desc, Valor = $"η = {F(p.Eta, "0.00")}" + (p.Caso != "" ? $" ({p.Caso})" : ""), Limite = "≤ 1,00", Referencia = refer,
                Situacao = p.Eta > 1 + 1e-9 ? SituacaoVerificacao.NaoAtende : SituacaoVerificacao.Ok, Dica = p.Eta > 1 + 1e-9 ? dica : "",
            });
        mem.Linhas.Add($"Parafuso: Fv,Rd = {F(fv1, "0.0")} kN por plano ({(d.RoscaNoPlano || !d.Parafuso.AltaResistencia ? "0,45" : "0,56")}·Ab·fub/γa2); " +
                       $"força no parafuso mais solicitado pelo método elástico (V/n, N/n e V·e·ymáx/Σy²).");
        K("LC1", "Parafusos ao cisalhamento (grupo excêntrico, método elástico)", Pior(c => Forca(c.Vd, c.Nd) / fv1), "NBR 8800 6.3.3.2 e 6.5.2",
          "Aumente o número ou o diâmetro dos parafusos.");

        // ---- pressão de contato e rasgamento entre furos: pior ℓf entre a borda e o furo vizinho (a direção da força varia)
        double lfViga = Math.Min(d.EhViga - dh / 2, d.N > 1 ? d.S - dh : double.PositiveInfinity);
        double lfChapa = Math.Min(Math.Min(d.EhChapa, d.Ev) - dh / 2, d.N > 1 ? d.S - dh : double.PositiveInfinity);
        double fcViga = ComponentesLigacao.PressaoContato(lfViga, tw, d.Db, fuV, d.DeformacaoLimitada, gamaA2);
        double fcChapa = ComponentesLigacao.PressaoContato(lfChapa, tp, d.Db, fuP, d.DeformacaoLimitada, gamaA2);
        mem.Linhas.Add($"Pressão de contato por furo (pior ℓf): alma da viga ℓf = {F(lfViga)} mm → {F(fcViga, "0.0")} kN; chapa ℓf = {F(lfChapa)} mm → {F(fcChapa, "0.0")} kN.");
        K("LC2", "Pressão de contato e rasgamento entre furos — alma da viga", Pior(c => Forca(c.Vd, c.Nd) / fcViga), "NBR 8800 6.3.3.3");
        K("LC3", "Pressão de contato e rasgamento entre furos — chapa", Pior(c => Forca(c.Vd, c.Nd) / fcChapa), "NBR 8800 6.3.3.3");

        // ---- chapa: cisalhamento (6.5.5), rasgamento (6.5.6), flexão + normal
        double ag = hp * tp, anv = (hp - d.N * dhLiq) * tp;
        double vEsc = 0.60 * fyP * ag / gamaA1 / 1000, vRup = 0.60 * fuP * anv / gamaA2 / 1000;
        double agv = (hp - d.Ev) * tp, anvB = agv - (d.N - 0.5) * dhLiq * tp, ant = (d.EhChapa - 0.5 * dhLiq) * tp;
        double rasg = ComponentesLigacao.Rasgamento(agv, anvB, ant, fyP, fuP, 1.0, gamaA2);
        double zp = tp * hp * hp / 4, mRd = zp * fyP / gamaA1 / 1e6;                  // kN·m
        double nRd = ag * fyP / gamaA1 / 1000;
        mem.Linhas.Add($"Chapa: escoamento ao cisalhamento 0,6·fy·Ag/γa1 = {F(vEsc, "0")} kN; ruptura 0,6·fu·Anv/γa2 = {F(vRup, "0")} kN; rasgamento = {F(rasg, "0")} kN " +
                       $"(Agv = {F(agv, "0")}, Anv = {F(anvB, "0")}, Ant = {F(ant, "0")} mm²); flexão Zp·fy/γa1 = {F(mRd, "0.00")} kN·m; tração Ag·fy/γa1 = {F(nRd, "0")} kN.");
        K("LC4", "Chapa ao cisalhamento (escoamento e ruptura)", Pior(c => c.Vd / Math.Min(vEsc, vRup)), "NBR 8800 6.5.5", "Aumente a altura ou a espessura da chapa.");
        K("LC5", "Chapa ao colapso por rasgamento", Pior(c => c.Vd / rasg), "NBR 8800 6.5.6", "Aumente as distâncias às bordas.");
        K("LC6", "Chapa à flexão com força normal", Pior(c => c.Vd * d.A / 1000 / mRd + Math.Abs(c.Nd) / nRd), "NBR 8800 6.5 e 5.5.1.2",
          "Aumente a espessura ou a altura da chapa.");

        // ---- solda: dois filetes na altura da chapa; V, N e M = V·e pelo método elástico (por mm de cordão)
        double fwRd = ComponentesLigacao.FiletePorMm(d.Perna, d.Fw);   // kN/mm por filete
        double Solda(double vd, double nd)
        {
            double l = hp, fvv = vd / (2 * l), fn = nd / (2 * l), fm = vd * d.A * 6 / (2 * l * l);
            return Math.Sqrt(fvv * fvv + Math.Pow(Math.Abs(fn) + fm, 2)) / fwRd;
        }
        mem.Linhas.Add($"Solda: filete de {F(d.Perna)} mm, 0,6·fw·(0,707·perna)/γw2 = {F(fwRd * 1000, "0")} N/mm; dois cordões de {F(hp, "0")} mm com V, N e M = V·e (elástico).");
        K("LC7", "Solda da chapa ao suporte (filetes)", Pior(c => Solda(c.Vd, c.Nd)), "NBR 8800 6.2.5 (Tab. 9)", "Aumente a perna do filete.");

        // ---- disposições construtivas
        bool passo = d.N < 2 || d.S >= 3 * d.Db - 1e-9;
        double bordaMin = 1.25 * d.Db;
        bool bordas = d.Ev >= bordaMin && d.EhChapa >= bordaMin && d.EhViga >= bordaMin;
        bool ductil = tp <= d.Db / 2 + 1.6 + 1e-9 && tp <= d.Viga.Tw + 99;
        v.Add(new ResultadoVerificacao
        {
            Codigo = "LC8", Grupo = grupo, Descricao = "Espaçamento e distâncias às bordas",
            Valor = $"s = {F(d.S, "0")} mm (≥ 3·db = {F(3 * d.Db, "0")}); bordas {F(Math.Min(d.Ev, Math.Min(d.EhChapa, d.EhViga)), "0")} mm (≥ {F(bordaMin, "0")})",
            Referencia = "NBR 8800 6.3.9 e 6.3.11 (Tab. 14: conferir o tipo de borda)",
            Situacao = !passo ? SituacaoVerificacao.NaoAtende : !bordas ? SituacaoVerificacao.Conferir : SituacaoVerificacao.Ok,
        });
        v.Add(new ResultadoVerificacao
        {
            Codigo = "LC9", Grupo = grupo, Descricao = "Capacidade de rotação da ligação flexível", Valor = $"tp = {F(tp)} mm", Limite = $"≤ db/2 + 1,6 = {F(d.Db / 2 + 1.6)} mm",
            Referencia = "NBR 8800 6.1.3 (boa prática do AISC Manual, parte 10)", Situacao = ductil ? SituacaoVerificacao.Ok : SituacaoVerificacao.Conferir,
            Dica = ductil ? "" : "Chapa espessa: a ligação pode não girar como rótula; confira a rotação ou trate como semirrígida.",
        });
        return v;
    }
}
