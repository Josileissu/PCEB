using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Documentacao;
using DEB.Core.Normas.Verificacao;

namespace DEB.Core.Elementos.Metalica;

/// <summary>Esforços de cálculo de um caso numa ligação: Nd (kN, tração +), Vd (kN), Md (kN·m), Hd (kN).</summary>
public readonly struct EsforcoLigacao
{
    public string Nome { get; }
    public double Nd { get; }
    public double Vd { get; }
    public double Md { get; }
    /// <summary>Força horizontal no plano da ligação (kN), quando a tipologia usa (apoio de terça: força axial da terça).</summary>
    public double Hd { get; }
    public EsforcoLigacao(string nome, double nd, double vd = 0, double md = 0, double hd = 0) { Nome = nome; Nd = nd; Vd = vd; Md = md; Hd = hd; }
}

/// <summary>Peças comuns das tipologias complementares (D50, item 5).</summary>
public static class FuncoesLigacao
{
    public static string F(double x, string f = "0.#") => Fmt.Virgula(x, f);

    /// <summary>Fator de redução da compressão (5.3.3): χ = 0,658^λ0² (λ0 ≤ 1,5) ou 0,877/λ0².</summary>
    public static double Chi(double kl, double r, double fy, double e = 200000)
    {
        double l0 = kl / r * Math.Sqrt(fy / (Math.PI * Math.PI * e));
        return l0 <= 1.5 ? Math.Pow(0.658, l0 * l0) : 0.877 / (l0 * l0);
    }

    /// <summary>Distância mínima do centro do furo-padrão à borda (Tab. 14, borda laminada ou cortada a maçarico ≈ 1,25·db).</summary>
    public static double BordaMinima(double db) => 1.25 * db;

    public static (double Eta, string Caso) Pior(List<EsforcoLigacao> cs, Func<EsforcoLigacao, double> f) =>
        cs.Count == 0 ? (0, "") : cs.Select(c => (f(c), c.Nome)).OrderByDescending(x => x.Item1).First();

    public static void K(ListaVerificacoes v, string grupo, string cod, string desc, (double Eta, string Caso) p, string refer, string dica = "") =>
        v.Add(new ResultadoVerificacao { Codigo = cod, Grupo = grupo, Descricao = desc, Valor = $"η = {F(p.Eta, "0.00")}" + (p.Caso != "" ? $" ({p.Caso})" : ""),
            Limite = "≤ 1,00", Referencia = refer, Situacao = p.Eta > 1 + 1e-9 ? SituacaoVerificacao.NaoAtende : SituacaoVerificacao.Ok, Dica = p.Eta > 1 + 1e-9 ? dica : "" });

    public static void Geo(ListaVerificacoes v, string grupo, string cod, string desc, bool ok, string valor, string limite, string refer) =>
        v.Add(new ResultadoVerificacao { Codigo = cod, Grupo = grupo, Descricao = desc, Valor = valor, Limite = limite, Referencia = refer,
            Situacao = ok ? SituacaoVerificacao.Ok : SituacaoVerificacao.Conferir });

    /// <summary>
    /// Resistência da ligação num sentido de carga (resumo da biblioteca, sem esforços do modelo): o maior fator s tal que nenhuma
    /// verificação de resistência (η) passe de 1 para o esforço s·(unitário). Busca por bisseção (vale também para interações
    /// quadráticas). Devolve a resistência (kN ou kN·m) e o código que governa.
    /// </summary>
    public static (double R, string Governa) Capacidade(Func<double, ListaVerificacoes> verificar, double max = 1e5)
    {
        bool Falha(double s) => verificar(s).Any(x => x.Valor.StartsWith("η") && x.Situacao == SituacaoVerificacao.NaoAtende);
        if (!Falha(max)) return (double.PositiveInfinity, "");
        double a = 0, b = max;
        for (int k = 0; k < 60 && b - a > 1e-3 * Math.Max(1, a); k++) { double m = (a + b) / 2; if (Falha(m)) b = m; else a = m; }
        string gov = verificar(b).FirstOrDefault(x => x.Valor.StartsWith("η") && x.Situacao == SituacaoVerificacao.NaoAtende)?.Codigo ?? "";
        return (a, gov);
    }
}

// ============================================================================ apoio de terça

/// <summary>
/// Apoio de terça (D50, item 5): chapa vertical soldada na mesa superior da viga ou do banzo da tesoura (dois filetes, um de cada
/// face), no plano da alma da terça (perfil U ou Ue formado a frio), que é parafusada nela numa fila vertical. A chapa atravessa
/// a mesa (largura bp ao longo da terça). Esforços (kN): Vd = reação vertical; Hd = força axial da terça (no plano da chapa:
/// escora, linha de corrente); Nd = componente perpendicular à chapa (inclinação do telhado, vento), positiva afastando a terça.
/// </summary>
public sealed class DadosApoioTerca
{
    public TipoParafuso Parafuso { get; set; } = TipoParafuso.Lista[0];
    public bool RoscaNoPlano { get; set; } = true;
    public double Db { get; set; } = 12.7;
    public int N { get; set; } = 2;
    public double S { get; set; } = 60;
    public AcoEstrutural AcoChapa { get; set; }
    public double Tp { get; set; } = 6.3;
    public double Hp { get; set; } = 150;      // altura da chapa (acima da mesa)
    public double Bp { get; set; } = 80;       // largura da chapa (ao longo da terça)
    public double Ev { get; set; } = 30;       // do topo da chapa ao parafuso de cima
    public double HTerca { get; set; } = 150;
    public double TTerca { get; set; } = 2.65;
    public AcoEstrutural AcoTerca { get; set; }
    public double Perna { get; set; } = 5;
    public double Fw { get; set; } = 485;

    /// <summary>Altura do centro do grupo de parafusos acima da mesa.</summary>
    public double ZCentro => Hp - Ev - (N - 1) * S / 2;

    public static DadosApoioTerca DoModelo(Ligacoes.ModeloLigacao m)
    {
        var d = new DadosApoioTerca
        {
            Parafuso = TipoParafuso.Buscar(m.Texto("parafuso")), RoscaNoPlano = m.Texto("rosca") != "Não", Db = m.Numero("db"), N = (int)m.Numero("n"), S = m.Numero("s"),
            AcoChapa = AcoEstrutural.Buscar(m.Texto("acoChapa")) ?? AcoEstrutural.Buscar("A36"), Tp = m.Numero("tp"), Bp = m.Numero("bp"), Ev = m.Numero("ev"),
            HTerca = m.Numero("hTerca"), TTerca = m.Numero("tTerca"), AcoTerca = AcoEstrutural.Buscar(m.Texto("acoTerca")) ?? AcoEstrutural.Buscar("A36"),
            Perna = m.Numero("perna"), Fw = m.Numero("fw"),
        };
        // auto: o que cabe os parafusos com borda, pelo menos 3/4 da altura da terça
        d.Hp = m.EhAuto("hp") ? Math.Max(d.Ev + (d.N - 1) * d.S + 1.5 * d.Db + 20, d.HTerca * 0.75) : m.Numero("hp");
        return d;
    }
}

public static class LigacaoApoioTerca
{
    public static ListaVerificacoes Verificar(DadosApoioTerca d, IEnumerable<EsforcoLigacao> casos, string grupo, out PassoMemoria mem, double gamaA1 = 1.10, double gamaA2 = 1.35)
    {
        string F(double x, string f = "0.#") => FuncoesLigacao.F(x, f);
        mem = new PassoMemoria { Titulo = "Apoio de terça (NBR 8800:2024, 5.3, 5.4, 6.2, 6.3 e 6.5; NBR 14762 para a alma da terça)" };
        var v = new ListaVerificacoes(); var cs = casos.ToList();
        double dh = ComponentesLigacao.Furo(d.Db), zc = d.ZCentro;
        double fv = ComponentesLigacao.CisalhamentoParafuso(d.Db, d.Parafuso, d.RoscaNoPlano, gamaA2), ft = ComponentesLigacao.TracaoParafuso(d.Db, d.Parafuso, gamaA2);
        mem.Linhas.Add($"Chapa {F(d.Bp, "0")} × {F(d.Hp, "0")} × {F(d.Tp)} mm ({d.AcoChapa.Nome}); {d.N} parafusos ø{F(d.Db)} {d.Parafuso.Nome} a {F(d.S, "0")} mm; " +
                       $"terça com alma de {F(d.TTerca)} mm; centro dos parafusos a {F(zc, "0")} mm da mesa.");
        double Corte(EsforcoLigacao c) => Math.Sqrt(c.Vd * c.Vd + c.Hd * c.Hd) / d.N;
        // 6.3.3.4: tração e corte combinados (elíptica)
        FuncoesLigacao.K(v, grupo, "AT1", "Parafusos: corte e tração combinados", FuncoesLigacao.Pior(cs, c => Math.Pow(Corte(c) / fv, 2) + Math.Pow(Math.Max(0, c.Nd) / d.N / ft, 2)),
                "NBR 8800 6.3.3.4", "Aumente o diâmetro ou o número de parafusos.");
        double lf = Math.Min(d.Ev - dh / 2, d.N > 1 ? d.S - dh : 1e9);
        double fcChapa = ComponentesLigacao.PressaoContato(lf, d.Tp, d.Db, d.AcoChapa.Fu, true, gamaA2);
        double fcTerca = ComponentesLigacao.PressaoContato(1e9, d.TTerca, d.Db, d.AcoTerca.Fu, true, gamaA2);
        FuncoesLigacao.K(v, grupo, "AT2", "Pressão de contato na chapa", FuncoesLigacao.Pior(cs, c => Corte(c) / fcChapa), "NBR 8800 6.3.3.3");
        FuncoesLigacao.K(v, grupo, "AT3", "Pressão de contato na alma da terça", FuncoesLigacao.Pior(cs, c => Corte(c) / fcTerca), "NBR 8800 6.3.3.3 / NBR 14762 10.3",
                "Alma fina: aumente o diâmetro ou use arruela de reforço.");
        // flexão da chapa na base: fora do plano (Nd, Z = bp·t²/4) e no plano (Hd, Z = t·bp²/4), soma linear
        double mFora = d.Bp * d.Tp * d.Tp / 4 * d.AcoChapa.Fy / gamaA1 / 1000, mNo = d.Tp * d.Bp * d.Bp / 4 * d.AcoChapa.Fy / gamaA1 / 1000;   // kN·mm
        FuncoesLigacao.K(v, grupo, "AT4", "Flexão da chapa na base (Nd fora do plano, Hd no plano)", FuncoesLigacao.Pior(cs, c => Math.Abs(c.Nd) * zc / mFora + Math.Abs(c.Hd) * zc / mNo),
                "NBR 8800 5.4 (seção retangular, plastificação)", "Aumente a espessura ou baixe os parafusos.");
        // compressão da chapa sob a reação (coluna engastada na base e livre no topo, K = 2)
        double chi = FuncoesLigacao.Chi(2 * zc, d.Tp / Math.Sqrt(12), d.AcoChapa.Fy), ncRd = chi * d.Bp * d.Tp * d.AcoChapa.Fy / gamaA1 / 1000;
        FuncoesLigacao.K(v, grupo, "AT5", "Chapa à compressão (reação da terça, K = 2)", FuncoesLigacao.Pior(cs, c => Math.Max(0, c.Vd) / ncRd), "NBR 8800 5.3");
        // solda: dois cordões de comprimento bp (um em cada face); vertical = Vd + momentos; horizontal = Hd e Nd
        double fw = ComponentesLigacao.FiletePorMm(d.Perna, d.Fw);
        FuncoesLigacao.K(v, grupo, "AT6", "Solda da chapa na mesa (dois filetes)", FuncoesLigacao.Pior(cs, c =>
        {
            double fz = Math.Abs(c.Vd) / (2 * d.Bp) + 3 * Math.Abs(c.Hd) * zc / (d.Bp * d.Bp) + Math.Abs(c.Nd) * zc / ((d.Tp + d.Perna) * d.Bp);
            double fx = Math.Abs(c.Hd) / (2 * d.Bp), fy = Math.Abs(c.Nd) / (2 * d.Bp);
            return Math.Sqrt(fz * fz + fx * fx + fy * fy) / fw;
        }), "NBR 8800 6.2.5 (Tab. 9)", "Aumente o filete.");
        mem.Linhas.Add($"Fv,Rd = {F(fv, "0.0")} kN e Ft,Rd = {F(ft, "0.0")} kN por parafuso; contato: chapa {F(fcChapa, "0.0")} kN, terça {F(fcTerca, "0.0")} kN; " +
                       $"MRd da chapa: fora do plano {F(mFora / 1000, "0.00")} kN·m, no plano {F(mNo / 1000, "0.00")} kN·m; Nc,Rd = {F(ncRd, "0")} kN (χ = {F(chi, "0.00")}).");
        double bmin = FuncoesLigacao.BordaMinima(d.Db);
        FuncoesLigacao.Geo(v, grupo, "AT7", "Espaçamento e bordas", (d.N < 2 || d.S >= 3 * d.Db - 1e-9) && d.Ev >= bmin - 1e-9 && d.Bp / 2 >= bmin - 1e-9,
                  $"s = {F(d.S, "0")} mm; bordas {F(d.Ev, "0")} e {F(d.Bp / 2, "0")} mm", "s ≥ 3·db; bordas ≥ 1,25·db", "NBR 8800 6.3.9 e 6.3.11");
        return v;
    }
}

// ============================================================================ contraventamento em chapa de nó

/// <summary>
/// Barra de contraventamento (cantoneira simples ou barra chata) parafusada numa fila em chapa de nó soldada ao suporte (D50,
/// item 5). Nd (kN): tração +, compressão −. A chapa é verificada na seção de Whitmore (30° a partir do primeiro parafuso).
/// </summary>
public sealed class DadosChapaNo
{
    public bool Cantoneira { get; set; } = true;
    public double Bb { get; set; } = 63.5;      // aba (cantoneira) ou largura (barra chata)
    public double Tb { get; set; } = 6.35;
    public AcoEstrutural AcoBarra { get; set; }
    public TipoParafuso Parafuso { get; set; } = TipoParafuso.Lista[2];
    public bool RoscaNoPlano { get; set; } = true;
    public double Db { get; set; } = 16;
    public int N { get; set; } = 3;
    public double S { get; set; } = 60;
    public double Ev { get; set; } = 35;        // borda na direção da força (na barra e na chapa)
    public double G { get; set; } = 35;         // gabarito: do dorso (ou da borda) da barra à fila
    public AcoEstrutural AcoChapa { get; set; }
    public double Tg { get; set; } = 9.5;
    public double Lc { get; set; } = 150;       // comprimento livre médio da chapa (compressão)
    public double Angulo { get; set; } = 45;    // entre a barra e o suporte (graus)
    public double Lw { get; set; } = 250;       // comprimento da chapa na solda
    public double Perna { get; set; } = 6;
    public double Fw { get; set; } = 485;

    public double Comprimento => (N - 1) * S;
    /// <summary>Largura de Whitmore: espraiamento a 30° ao longo do grupo.</summary>
    public double Whitmore => 2 * Comprimento * Math.Tan(Math.PI / 6);

    /// <summary>Distância do centro da aba ligada ao centroide da cantoneira (x̄) ou 0 na barra chata.</summary>
    public double Xb => Cantoneira ? (Bb * Tb * Tb / 2 + (Bb - Tb) * Tb * (Tb + (Bb - Tb) / 2)) / ((2 * Bb - Tb) * Tb) : 0;
    public double Ag => Cantoneira ? (2 * Bb - Tb) * Tb : Bb * Tb;

    public static DadosChapaNo DoModelo(Ligacoes.ModeloLigacao m) => new DadosChapaNo
    {
        Cantoneira = m.Texto("tipoBarra") != "Barra chata", Bb = m.Numero("bb"), Tb = m.Numero("tb"), AcoBarra = AcoEstrutural.Buscar(m.Texto("acoBarra")) ?? AcoEstrutural.Buscar("A36"),
        Parafuso = TipoParafuso.Buscar(m.Texto("parafuso")), RoscaNoPlano = m.Texto("rosca") != "Não", Db = m.Numero("db"), N = (int)m.Numero("n"), S = m.Numero("s"),
        Ev = m.Numero("ev"), G = m.Numero("g"), AcoChapa = AcoEstrutural.Buscar(m.Texto("acoChapa")) ?? AcoEstrutural.Buscar("A36"), Tg = m.Numero("tg"), Lc = m.Numero("lc"),
        Angulo = m.Numero("angulo"), Lw = m.Numero("lw"), Perna = m.Numero("perna"), Fw = m.Numero("fw"),
    };
}

public static class LigacaoChapaNo
{
    public static ListaVerificacoes Verificar(DadosChapaNo d, IEnumerable<EsforcoLigacao> casos, string grupo, out PassoMemoria mem, double gamaA1 = 1.10, double gamaA2 = 1.35)
    {
        string F(double x, string f = "0.#") => FuncoesLigacao.F(x, f);
        mem = new PassoMemoria { Titulo = "Contraventamento em chapa de nó (NBR 8800:2024, 5.2, 5.3, 6.3 e 6.5; seção de Whitmore)" };
        var v = new ListaVerificacoes(); var cs = casos.ToList();
        double dh = ComponentesLigacao.Furo(d.Db), dhL = dh + 2;
        double fv = ComponentesLigacao.CisalhamentoParafuso(d.Db, d.Parafuso, d.RoscaNoPlano, gamaA2);
        FuncoesLigacao.K(v, grupo, "CN1", "Parafusos ao corte (simples)", FuncoesLigacao.Pior(cs, c => Math.Abs(c.Nd) / d.N / fv), "NBR 8800 6.3.3.2", "Aumente o número ou o diâmetro.");
        double lfB = Math.Min(d.Ev - dh / 2, d.N > 1 ? d.S - dh : 1e9);
        double fc = Math.Min(ComponentesLigacao.PressaoContato(lfB, d.Tb, d.Db, d.AcoBarra.Fu, true, gamaA2), ComponentesLigacao.PressaoContato(lfB, d.Tg, d.Db, d.AcoChapa.Fu, true, gamaA2));
        FuncoesLigacao.K(v, grupo, "CN2", "Pressão de contato (barra e chapa)", FuncoesLigacao.Pior(cs, c => Math.Abs(c.Nd) / d.N / fc), "NBR 8800 6.3.3.3");
        // barra: escoamento da seção bruta e ruptura da seção líquida efetiva (Ct = 1 − x̄/lc, ≥ 0,60; 5.2.5)
        double ct = d.Cantoneira ? (d.N >= 2 ? Math.Max(0.6, 1 - d.Xb / d.Comprimento) : 0.6) : 1.0;
        double an = d.Ag - dhL * d.Tb, ntRd = Math.Min(d.Ag * d.AcoBarra.Fy / gamaA1, ct * an * d.AcoBarra.Fu / gamaA2) / 1000;
        FuncoesLigacao.K(v, grupo, "CN3", "Barra à tração (bruta e líquida efetiva)", FuncoesLigacao.Pior(cs, c => Math.Max(0, c.Nd) / ntRd), "NBR 8800 5.2.2 e 5.2.5",
                "Aumente a barra ou o comprimento da ligação (Ct).");
        // rasgamento na barra (uma fila: plano de corte ao longo da fila, tração até a borda) e na chapa (dois planos, tração na largura do grupo)
        double agvB = (d.Comprimento + d.Ev) * d.Tb, anvB = agvB - (d.N - 0.5) * dhL * d.Tb, antB = ((d.Cantoneira ? d.Bb - d.G : d.Bb / 2) - dhL / 2) * d.Tb;
        double rasB = ComponentesLigacao.Rasgamento(agvB, anvB, antB, d.AcoBarra.Fy, d.AcoBarra.Fu, 1.0, gamaA2);
        double agvG = 2 * (d.Comprimento + d.Ev) * d.Tg, anvG = agvG - 2 * (d.N - 0.5) * dhL * d.Tg, antG = Math.Max(0, 2 * d.Ev - dhL) * d.Tg;
        double rasG = ComponentesLigacao.Rasgamento(agvG, anvG, antG, d.AcoChapa.Fy, d.AcoChapa.Fu, 1.0, gamaA2);
        FuncoesLigacao.K(v, grupo, "CN4", "Colapso por rasgamento (barra e chapa)", FuncoesLigacao.Pior(cs, c => Math.Abs(c.Nd) / Math.Min(rasB, rasG)), "NBR 8800 6.5.6", "Aumente bordas e passo.");
        // chapa de nó na seção de Whitmore: tração (escoamento) e compressão (K = 0,65 no comprimento livre médio, Thornton)
        double bw = Math.Max(d.Whitmore, d.Db * 3), ntG = bw * d.Tg * d.AcoChapa.Fy / gamaA1 / 1000;
        double chi = FuncoesLigacao.Chi(0.65 * d.Lc, d.Tg / Math.Sqrt(12), d.AcoChapa.Fy), ncG = chi * bw * d.Tg * d.AcoChapa.Fy / gamaA1 / 1000;
        FuncoesLigacao.K(v, grupo, "CN5", "Chapa de nó: Whitmore à tração", FuncoesLigacao.Pior(cs, c => Math.Max(0, c.Nd) / ntG), "NBR 8800 6.5.3 (largura de Whitmore)");
        FuncoesLigacao.K(v, grupo, "CN6", "Chapa de nó: Whitmore à compressão (K = 0,65)", FuncoesLigacao.Pior(cs, c => Math.Max(0, -c.Nd) / ncG), "NBR 8800 5.3 e 6.5.4 (Thornton)",
                "Aumente a espessura ou reduza o comprimento livre.");
        // solda no suporte: dois filetes de comprimento Lw; força com a componente normal e a tangencial ao suporte
        double fw = ComponentesLigacao.FiletePorMm(d.Perna, d.Fw), a = d.Angulo * Math.PI / 180;
        FuncoesLigacao.K(v, grupo, "CN7", "Solda da chapa de nó no suporte", FuncoesLigacao.Pior(cs, c => Math.Abs(c.Nd) / (2 * d.Lw) / fw), "NBR 8800 6.2.5 (Tab. 9)", "Aumente o filete ou o comprimento.");
        // compressão da barra (só a ligação; a flambagem global é da barra no modelo)
        mem.Linhas.Add($"{(d.Cantoneira ? $"L {F(d.Bb)} × {F(d.Tb)}" : $"barra chata {F(d.Bb)} × {F(d.Tb)}")} ({d.AcoBarra.Nome}), {d.N} parafusos ø{F(d.Db)} a {F(d.S, "0")} mm; " +
                       $"chapa de nó {F(d.Tg)} mm ({d.AcoChapa.Nome}), Whitmore bw = {F(bw, "0")} mm, Lc = {F(d.Lc, "0")} mm, χ = {F(chi, "0.00")}; Ct = {F(ct, "0.00")}.");
        mem.Linhas.Add($"Fv,Rd = {F(fv, "0.0")} kN; contato {F(fc, "0.0")} kN; Nt,Rd da barra {F(ntRd, "0")} kN; rasgamento: barra {F(rasB, "0")}, chapa {F(rasG, "0")} kN; " +
                       $"Whitmore: tração {F(ntG, "0")} kN, compressão {F(ncG, "0")} kN; ângulo com o suporte {F(d.Angulo, "0")}°.");
        double bmin = FuncoesLigacao.BordaMinima(d.Db);
        FuncoesLigacao.Geo(v, grupo, "CN8", "Espaçamento, bordas e número de parafusos", d.N >= 2 && d.S >= 3 * d.Db - 1e-9 && d.Ev >= bmin - 1e-9,
                  $"n = {d.N}; s = {F(d.S, "0")} mm; borda {F(d.Ev, "0")} mm", "n ≥ 2; s ≥ 3·db; bordas ≥ 1,25·db", "NBR 8800 6.3.9, 6.3.11 e 5.2.5");
        return v;
    }
}

// ============================================================================ tubo circular com chapa de nó em fenda

/// <summary>
/// Tubo circular com uma fenda nas extremidades, encaixado na chapa de nó e soldado por quatro filetes de comprimento Lw (D50,
/// item 5). Nd (kN): tração +, compressão −. Ct pela NBR 8800 5.2.5 (x̄ = D/π) e Lw ≥ 1,3·D dispensa a redução.
/// </summary>
public sealed class DadosTuboFendido
{
    public double D { get; set; } = 101.6;
    public double T { get; set; } = 6.0;
    public AcoEstrutural AcoTubo { get; set; }
    public AcoEstrutural AcoChapa { get; set; }
    public double Tg { get; set; } = 12.5;
    public double Lw { get; set; } = 150;
    public double Folga { get; set; } = 2;
    public double Lc { get; set; } = 150;
    public double Perna { get; set; } = 6;
    public double Fw { get; set; } = 485;
    public double Ag => Math.PI * (D * D - Math.Pow(D - 2 * T, 2)) / 4;
    public double Xb => D / Math.PI;

    public static DadosTuboFendido DoModelo(Ligacoes.ModeloLigacao m) => new DadosTuboFendido
    {
        D = m.Numero("d"), T = m.Numero("t"), AcoTubo = AcoEstrutural.Buscar(m.Texto("acoTubo")) ?? AcoEstrutural.Buscar("A572"),
        AcoChapa = AcoEstrutural.Buscar(m.Texto("acoChapa")) ?? AcoEstrutural.Buscar("A36"), Tg = m.Numero("tg"), Lw = m.Numero("lw"), Folga = m.Numero("folga"),
        Lc = m.Numero("lc"), Perna = m.Numero("perna"), Fw = m.Numero("fw"),
    };
}

public static class LigacaoTuboFendido
{
    public static ListaVerificacoes Verificar(DadosTuboFendido d, IEnumerable<EsforcoLigacao> casos, string grupo, out PassoMemoria mem, double gamaA1 = 1.10, double gamaA2 = 1.35)
    {
        string F(double x, string f = "0.#") => FuncoesLigacao.F(x, f);
        mem = new PassoMemoria { Titulo = "Tubo circular com chapa de nó em fenda (NBR 8800:2024, 5.2.5, 6.2 e 6.5; NBR 16239)" };
        var v = new ListaVerificacoes(); var cs = casos.ToList();
        // solda: quatro filetes; o metal-base do tubo ao cisalhamento limita (0,6·fu·t/γa2 por mm)
        double fw = Math.Min(ComponentesLigacao.FiletePorMm(d.Perna, d.Fw), 0.6 * d.AcoTubo.Fu * d.T / gamaA2 / 1000);
        FuncoesLigacao.K(v, grupo, "TF1", "Solda tubo–chapa (4 filetes) e metal-base do tubo", FuncoesLigacao.Pior(cs, c => Math.Abs(c.Nd) / (4 * d.Lw) / fw), "NBR 8800 6.2.5 (Tab. 9)",
                "Aumente o comprimento de solda Lw.");
        double ct = d.Lw >= 1.3 * d.D ? 1.0 : Math.Max(0.6, 1 - d.Xb / d.Lw);
        double an = d.Ag - 2 * (d.Tg + d.Folga) * d.T, ntRd = Math.Min(d.Ag * d.AcoTubo.Fy / gamaA1, ct * an * d.AcoTubo.Fu / gamaA2) / 1000;
        FuncoesLigacao.K(v, grupo, "TF2", "Tubo à tração (bruta e líquida com a fenda, Ct)", FuncoesLigacao.Pior(cs, c => Math.Max(0, c.Nd) / ntRd), "NBR 8800 5.2.2 e 5.2.5",
                "Aumente Lw (Lw ≥ 1,3·D: Ct = 1) ou a espessura.");
        // chapa: Whitmore a partir do início da solda (largura D + 2·Lw·tg 30°)
        double bw = d.D + 2 * d.Lw * Math.Tan(Math.PI / 6), ntG = bw * d.Tg * d.AcoChapa.Fy / gamaA1 / 1000;
        double chi = FuncoesLigacao.Chi(0.65 * d.Lc, d.Tg / Math.Sqrt(12), d.AcoChapa.Fy), ncG = chi * bw * d.Tg * d.AcoChapa.Fy / gamaA1 / 1000;
        FuncoesLigacao.K(v, grupo, "TF3", "Chapa de nó: Whitmore à tração", FuncoesLigacao.Pior(cs, c => Math.Max(0, c.Nd) / ntG), "NBR 8800 6.5.3");
        FuncoesLigacao.K(v, grupo, "TF4", "Chapa de nó: Whitmore à compressão (K = 0,65)", FuncoesLigacao.Pior(cs, c => Math.Max(0, -c.Nd) / ncG), "NBR 8800 5.3 e 6.5.4");
        // rasgamento da chapa: dois planos de corte ao longo da solda e tração na largura D
        double agv = 2 * d.Lw * d.Tg, ant = d.D * d.Tg, ras = ComponentesLigacao.Rasgamento(agv, agv, ant, d.AcoChapa.Fy, d.AcoChapa.Fu, 1.0, gamaA2);
        FuncoesLigacao.K(v, grupo, "TF5", "Chapa de nó ao colapso por rasgamento", FuncoesLigacao.Pior(cs, c => Math.Max(0, c.Nd) / ras), "NBR 8800 6.5.6");
        mem.Linhas.Add($"Tubo ø{F(d.D)} × {F(d.T)} mm ({d.AcoTubo.Nome}), Ag = {F(d.Ag, "0")} mm², fenda {F(d.Tg + d.Folga)} mm, An = {F(an, "0")} mm²; " +
                       $"Lw = {F(d.Lw, "0")} mm, x̄ = D/π = {F(d.Xb, "0.0")} mm, Ct = {F(ct, "0.00")}; chapa {F(d.Tg)} mm, bw = {F(bw, "0")} mm, χ = {F(chi, "0.00")}.");
        mem.Linhas.Add($"Solda {F(fw, "0.00")} kN/mm por filete; Nt,Rd do tubo {F(ntRd, "0")} kN; Whitmore {F(ntG, "0")} / {F(ncG, "0")} kN; rasgamento {F(ras, "0")} kN.");
        FuncoesLigacao.Geo(v, grupo, "TF6", "Comprimento de solda", d.Lw >= d.D - 1e-9, $"Lw = {F(d.Lw, "0")} mm, D = {F(d.D, "0")} mm", "Lw ≥ D (≥ 1,3·D dispensa Ct)",
                  "NBR 8800 5.2.5; NBR 16239");
        return v;
    }
}

// ============================================================================ emenda de viga com talas

/// <summary>
/// Emenda de viga I com talas parafusadas (D50, item 5): uma tala externa em cada mesa (corte simples) e duas talas na alma (corte
/// duplo). Md divide-se entre mesas (binário) e alma (pela razão de inércias); Vd vai à alma com a excentricidade do grupo.
/// </summary>
public sealed class DadosEmendaTalas
{
    public PerfilI Viga { get; set; }
    public AcoEstrutural AcoViga { get; set; }
    public TipoParafuso Parafuso { get; set; } = TipoParafuso.Lista[2];
    public bool RoscaNoPlano { get; set; } = true;
    public double Db { get; set; } = 19.05;
    public int Nm { get; set; } = 3;           // linhas de parafusos na mesa, de cada lado da junta (2 parafusos por linha)
    public double Sm { get; set; } = 75;
    public double Gm { get; set; } = 100;      // gabarito na mesa
    public double Tm { get; set; } = 12.5;
    public int Na { get; set; } = 3;           // parafusos na alma de cada lado (uma fila)
    public double Sa { get; set; } = 75;
    public double Ta { get; set; } = 8;
    public double E { get; set; } = 38;        // borda (talas)
    public double Folga { get; set; } = 10;
    public AcoEstrutural AcoTala { get; set; }

    public double LTalaMesa => 2 * (2 * E + (Nm - 1) * Sm) + Folga;
    public double HTalaAlma => 2 * E + (Na - 1) * Sa;
    public double BTalaMesa => Viga.Bf;

    public static DadosEmendaTalas DoModelo(Ligacoes.ModeloLigacao m, PerfilI viga, AcoEstrutural acoViga)
    {
        var d = new DadosEmendaTalas
        {
            Viga = viga, AcoViga = acoViga, Parafuso = TipoParafuso.Buscar(m.Texto("parafuso")), RoscaNoPlano = m.Texto("rosca") != "Não", Db = m.Numero("db"),
            Nm = (int)m.Numero("nm"), Sm = m.Numero("sm"), Gm = m.Numero("gm"), Tm = m.Numero("tm"), Sa = m.Numero("sa"), Ta = m.Numero("ta"), E = m.Numero("e"),
            Folga = m.Numero("folga"), AcoTala = AcoEstrutural.Buscar(m.Texto("acoTala")) ?? AcoEstrutural.Buscar("A36"),
        };
        if (m.EhAuto("na"))
        {
            double livre = viga.D - 2 * viga.Tf - 2 * Math.Max(viga.R, 10);
            int n = Math.Max(2, (int)Math.Floor((livre - 2 * d.E) / d.Sa) + 1);
            while (n > 2 && 2 * d.E + (n - 1) * d.Sa > livre) n--;
            d.Na = n;
        }
        else d.Na = (int)m.Numero("na");
        return d;
    }
}

public static class LigacaoEmendaTalas
{
    public static ListaVerificacoes Verificar(DadosEmendaTalas d, IEnumerable<EsforcoLigacao> casos, string grupo, out PassoMemoria mem, double gamaA1 = 1.10, double gamaA2 = 1.35)
    {
        string F(double x, string f = "0.#") => FuncoesLigacao.F(x, f);
        mem = new PassoMemoria { Titulo = "Emenda de viga com talas parafusadas (NBR 8800:2024, 6.3, 6.5)" };
        var v = new ListaVerificacoes(); var cs = casos.ToList();
        var p = d.Viga;
        double dh = ComponentesLigacao.Furo(d.Db), dhL = dh + 2, fv = ComponentesLigacao.CisalhamentoParafuso(d.Db, d.Parafuso, d.RoscaNoPlano, gamaA2);
        // divisão do momento: inércia das mesas e da alma (perfil sem raios)
        double hw = p.D - 2 * p.Tf, iM = 2 * p.Bf * p.Tf * Math.Pow((p.D - p.Tf) / 2, 2), iA = p.Tw * hw * hw * hw / 12, fa = iA / (iM + iA);
        double af = p.Bf * p.Tf, ag = 2 * af + hw * p.Tw;
        double ForcaMesa(EsforcoLigacao c) => Math.Abs(c.Md) * 1000 * (1 - fa) / (p.D - p.Tf) + Math.Abs(c.Nd) * af / ag;   // kN
        int nbm = 2 * d.Nm;
        FuncoesLigacao.K(v, grupo, "ET1", "Parafusos das mesas (corte simples)", FuncoesLigacao.Pior(cs, c => ForcaMesa(c) / nbm / fv), "NBR 8800 6.3.3.2", "Mais linhas ou diâmetro maior.");
        double lfm = Math.Min(d.E - dh / 2, d.Sm - dh);
        double fcm = Math.Min(ComponentesLigacao.PressaoContato(lfm, p.Tf, d.Db, d.AcoViga.Fu, true, gamaA2), ComponentesLigacao.PressaoContato(lfm, d.Tm, d.Db, d.AcoTala.Fu, true, gamaA2));
        FuncoesLigacao.K(v, grupo, "ET2", "Pressão de contato nas mesas e talas de mesa", FuncoesLigacao.Pior(cs, c => ForcaMesa(c) / nbm / fcm), "NBR 8800 6.3.3.3");
        double agm = d.BTalaMesa * d.Tm, anm = (d.BTalaMesa - 2 * dhL) * d.Tm;
        double ntm = Math.Min(agm * d.AcoTala.Fy / gamaA1, Math.Min(anm, 0.85 * agm) * d.AcoTala.Fu / gamaA2) / 1000;
        FuncoesLigacao.K(v, grupo, "ET3", "Tala de mesa à tração (bruta e líquida)", FuncoesLigacao.Pior(cs, c => ForcaMesa(c) / ntm), "NBR 8800 5.2 e 6.5.3", "Aumente a espessura da tala.");
        double ancM = (p.Bf - 2 * dhL) * p.Tf, ntMesa = ancM * d.AcoViga.Fu / gamaA2 / 1000;
        FuncoesLigacao.K(v, grupo, "ET4", "Mesa da viga na seção furada", FuncoesLigacao.Pior(cs, c => ForcaMesa(c) / ntMesa), "NBR 8800 5.2.4");
        // alma: Vd e a parcela do momento; excentricidade do centro do grupo à junta
        double ex = d.Folga / 2 + d.E;
        var ys = Enumerable.Range(0, d.Na).Select(i => (i - (d.Na - 1) / 2.0) * d.Sa).ToArray();
        double sy2 = ys.Sum(y => y * y), ymax = ys.Max(y => Math.Abs(y));
        double Alma(EsforcoLigacao c)
        {
            double m = Math.Abs(c.Md) * 1000 * fa + Math.Abs(c.Vd) * ex;   // kN·mm
            double fx = (sy2 > 0 ? m * ymax / sy2 : 0) + Math.Abs(c.Nd) * (hw * p.Tw / ag) / d.Na, fy = Math.Abs(c.Vd) / d.Na;
            return Math.Sqrt(fx * fx + fy * fy);
        }
        FuncoesLigacao.K(v, grupo, "ET5", "Parafusos da alma (corte duplo, grupo excêntrico)", FuncoesLigacao.Pior(cs, c => Alma(c) / (2 * fv)), "NBR 8800 6.3.3.2 e 6.5.2");
        double lfa = Math.Min(d.E - dh / 2, d.Na > 1 ? d.Sa - dh : 1e9);
        double fca = Math.Min(ComponentesLigacao.PressaoContato(lfa, p.Tw, d.Db, d.AcoViga.Fu, true, gamaA2), 2 * ComponentesLigacao.PressaoContato(lfa, d.Ta, d.Db, d.AcoTala.Fu, true, gamaA2));
        FuncoesLigacao.K(v, grupo, "ET6", "Pressão de contato na alma e talas de alma", FuncoesLigacao.Pior(cs, c => Alma(c) / fca), "NBR 8800 6.3.3.3");
        double h = d.HTalaAlma, vRd = Math.Min(0.6 * d.AcoTala.Fy * 2 * h * d.Ta / gamaA1, 0.6 * d.AcoTala.Fu * 2 * (h - d.Na * dhL) * d.Ta / gamaA2) / 1000;
        FuncoesLigacao.K(v, grupo, "ET7", "Talas de alma ao cisalhamento", FuncoesLigacao.Pior(cs, c => Math.Abs(c.Vd) / vRd), "NBR 8800 6.5.5");
        mem.Linhas.Add($"{p.Nome}: mesas levam {F((1 - fa) * 100, "0")}% do momento, alma {F(fa * 100, "0")}%; talas de mesa {F(d.BTalaMesa, "0")} × {F(d.Tm)} × {F(d.LTalaMesa, "0")} mm, " +
                       $"{nbm} parafusos ø{F(d.Db)} por lado em cada mesa; talas de alma 2 × {F(d.Ta)} × {F(h, "0")} mm com {d.Na} parafusos por lado.");
        mem.Linhas.Add($"Fv,Rd = {F(fv, "0.0")} kN; tala de mesa {F(ntm, "0")} kN; mesa furada {F(ntMesa, "0")} kN; talas de alma ao cisalhamento {F(vRd, "0")} kN.");
        double bmin = FuncoesLigacao.BordaMinima(d.Db);
        FuncoesLigacao.Geo(v, grupo, "ET8", "Espaçamentos, bordas e altura da tala de alma",
                  d.Sm >= 3 * d.Db - 1e-9 && d.Sa >= 3 * d.Db - 1e-9 && d.E >= bmin - 1e-9 && h <= hw - 2 * Math.Max(p.R, 10) + 1e-9 && d.Gm + dh <= p.Bf - 2 * bmin + 1e-9,
                  $"s = {F(d.Sm, "0")}/{F(d.Sa, "0")} mm, borda {F(d.E, "0")} mm, gm = {F(d.Gm, "0")} mm", "s ≥ 3·db; bordas ≥ 1,25·db; tala cabe na alma", "NBR 8800 6.3.9 e 6.3.11");
        return v;
    }
}

// ============================================================================ insert: chapa embutida no concreto com pinos com cabeça

/// <summary>
/// Insert entre o aço e o concreto (D50): chapa embutida na face do concreto com pinos com cabeça (stud) soldados atrás, numa grade
/// nx × ny; a peça de aço apoia ou é soldada na chapa. Esforços na face (kN, kN·m): Nd = tração perpendicular à chapa (+ arranca),
/// Vd = cortante no plano da chapa, Md = momento em torno do eixo y da grade (tração nos pinos de um lado).
/// Resistências pela EN 1992-4 (fixações no concreto; a NBR não traz método próprio): aço do pino, cone de concreto, arrancamento pela
/// cabeça, pry-out e ruptura da borda; interações do aço e do concreto.
/// </summary>
public sealed class DadosChapaEmbutida
{
    public double Db { get; set; } = 19;          // diâmetro do pino (mm)
    public double DCabeca { get; set; }           // 0 = 1,6·d (ISO 13918)
    public double Hef { get; set; } = 100;        // embutimento efetivo (mm)
    public int Nx { get; set; } = 2;
    public int Ny { get; set; } = 2;
    public double Sx { get; set; } = 150;
    public double Sy { get; set; } = 150;
    public double Fuk { get; set; } = 450;        // pino ISO 13918 SD1: fu 450, fy 350
    public double Fyk { get; set; } = 350;
    public double Fck { get; set; } = 30;
    public bool Fissurado { get; set; } = true;
    public double Borda { get; set; }             // distância do pino mais perto à borda do concreto na direção da cortante (mm); 0 = longe
    public double Bp { get; set; } = 250;
    public double Hp { get; set; } = 250;
    public double Tp { get; set; } = 16;
    public AcoEstrutural AcoChapa { get; set; }
    public int N => Nx * Ny;
    public double As => Math.PI * Db * Db / 4;

    public static DadosChapaEmbutida DoModelo(Ligacoes.ModeloLigacao m)
    {
        double db = m.Numero("db");
        double borda = Math.Max(50, 2.5 * db);
        return new DadosChapaEmbutida
        {
            Db = db, DCabeca = m.Numero("dCabeca", 0), Hef = m.Numero("hef"), Nx = (int)m.Numero("nx"), Ny = (int)m.Numero("ny"),
            Sx = m.Numero("sx"), Sy = m.Numero("sy"), Fck = m.Numero("fck"), Fissurado = m.Texto("fissurado") != "Não", Borda = m.Numero("borda", 0),
            Bp = m.EhAuto("bp") ? m.Numero("sx") * (m.Numero("nx") - 1) + 2 * borda : m.Numero("bp"),
            Hp = m.EhAuto("hp") ? m.Numero("sy") * (m.Numero("ny") - 1) + 2 * borda : m.Numero("hp"),
            Tp = m.Numero("tp"), AcoChapa = AcoEstrutural.Buscar(m.Texto("acoChapa")) ?? AcoEstrutural.Buscar("A36"),
        };
    }
}

public static class LigacaoChapaEmbutida
{
    public static ListaVerificacoes Verificar(DadosChapaEmbutida d, IEnumerable<EsforcoLigacao> casos, string grupo, out PassoMemoria mem)
    {
        string F(double x, string f = "0.#") => FuncoesLigacao.F(x, f);
        mem = new PassoMemoria { Titulo = "Insert: chapa embutida com pinos com cabeça (EN 1992-4; pino pela ISO 13918)" };
        var v = new ListaVerificacoes(); var cs = casos.ToList();
        // coeficientes da EN 1992-4 (4.4.3.1): aço γMs = 1,2·fuk/fyk ≥ 1,4 (tração) e 1,0·fuk/fyk ≥ 1,25 (cortante); concreto γMc = 1,5
        double gMsN = Math.Max(1.4, 1.2 * d.Fuk / d.Fyk), gMsV = Math.Max(1.25, d.Fuk / d.Fyk), gMc = 1.5;
        // tração de cada pino (elástico; momento em torno de y)
        var xs = Enumerable.Range(0, d.Nx).Select(i => (i - (d.Nx - 1) / 2.0) * d.Sx).ToArray();
        double sx2 = xs.Sum(x => x * x) * d.Ny;
        double Tmax(EsforcoLigacao c) => Math.Max(0, c.Nd / d.N + (sx2 > 0 ? Math.Abs(c.Md) * 1000 * xs.Max() / sx2 : 0));
        double Tgrupo(EsforcoLigacao c) => xs.Sum(x => Math.Max(0, c.Nd / d.N + (sx2 > 0 ? Math.Abs(c.Md) * 1000 * x / sx2 : 0))) * d.Ny;
        double nRds = d.As * d.Fuk / gMsN / 1000, vRds = 0.6 * d.As * d.Fuk / gMsV / 1000;
        // cone de concreto (7.2.1.4): N0 = k1·√fck·hef^1,5; áreas projetadas com scr = 3·hef; borda reduz (ψs,N)
        double k1 = d.Fissurado ? 7.7 : 11.0, hef = d.Hef, scr = 3 * hef, ccr = 1.5 * hef;
        double n0 = k1 * Math.Sqrt(d.Fck) * Math.Pow(hef, 1.5) / 1000;
        double c = d.Borda > 0 ? Math.Min(d.Borda, ccr) : ccr;
        double ax = c + Math.Min(scr, d.Sx) * (d.Nx - 1) + ccr, ay = 2 * ccr + Math.Min(scr, d.Sy) * (d.Ny - 1);
        double psiS = d.Borda > 0 ? Math.Min(1, 0.7 + 0.3 * c / ccr) : 1;
        double nRdc = n0 * ax * ay / (scr * scr) * psiS / gMc;
        // arrancamento pela cabeça (7.2.1.5): NRk,p = k2·Ah·fck, k2 = 7,5 (fissurado) ou 10,5
        double dh = d.DCabeca > 0 ? d.DCabeca : 1.6 * d.Db, ah = Math.PI / 4 * (dh * dh - d.Db * d.Db);
        double nRdp = (d.Fissurado ? 7.5 : 10.5) * ah * d.Fck / gMc / 1000;
        // pry-out (7.2.2.4): k8 = 2 (hef ≥ 60) sobre o cone do grupo
        double vRdcp = (hef >= 60 ? 2.0 : 1.0) * nRdc;
        // ruptura da borda na cortante (7.2.2.5), com a borda informada: V0 = k9·d^α·lf^β·√fck·c^1,5
        double vRdc = double.PositiveInfinity;
        if (d.Borda > 0)
        {
            double lf = Math.Min(hef, 12 * d.Db), alfa = 0.1 * Math.Sqrt(lf / c), beta = 0.1 * Math.Pow(d.Db / c, 0.2);
            double v0 = (d.Fissurado ? 1.7 : 2.4) * Math.Pow(d.Db, alfa) * Math.Pow(lf, beta) * Math.Sqrt(d.Fck) * Math.Pow(c, 1.5) / 1000;
            double larg = Math.Min(3 * c + Math.Min(3 * c, d.Sy) * (d.Ny - 1), 3 * c * d.Ny);
            vRdc = v0 * larg / (3 * c) / gMc;
        }
        FuncoesLigacao.K(v, grupo, "IN1", "Pinos à tração (aço)", FuncoesLigacao.Pior(cs, x => Tmax(x) / nRds), "EN 1992-4 7.2.1.3", "Mais pinos ou diâmetro maior.");
        FuncoesLigacao.K(v, grupo, "IN2", "Cone de concreto (grupo)", FuncoesLigacao.Pior(cs, x => Tgrupo(x) / nRdc), "EN 1992-4 7.2.1.4", "Aumente o embutimento ou o espaçamento.");
        FuncoesLigacao.K(v, grupo, "IN3", "Arrancamento pela cabeça", FuncoesLigacao.Pior(cs, x => Tmax(x) / nRdp), "EN 1992-4 7.2.1.5");
        FuncoesLigacao.K(v, grupo, "IN4", "Pinos à cortante (aço)", FuncoesLigacao.Pior(cs, x => Math.Abs(x.Vd) / d.N / vRds), "EN 1992-4 7.2.2.3");
        FuncoesLigacao.K(v, grupo, "IN5", "Pry-out (concreto atrás dos pinos)", FuncoesLigacao.Pior(cs, x => Math.Abs(x.Vd) / vRdcp), "EN 1992-4 7.2.2.4");
        if (d.Borda > 0)
            FuncoesLigacao.K(v, grupo, "IN6", "Ruptura da borda (cortante para a borda)", FuncoesLigacao.Pior(cs, x => Math.Abs(x.Vd) / vRdc), "EN 1992-4 7.2.2.5",
                             "Afaste os pinos da borda ou arme a borda.");
        // interações (7.2.3): aço (N/NRd)² + (V/VRd)² ≤ 1; concreto (N/NRd)^1,5 + (V/VRd)^1,5 ≤ 1
        FuncoesLigacao.K(v, grupo, "IN7", "Interação tração × cortante no aço",
            FuncoesLigacao.Pior(cs, x => Math.Pow(Tmax(x) / nRds, 2) + Math.Pow(Math.Abs(x.Vd) / d.N / vRds, 2)), "EN 1992-4 7.2.3 (Tab. 7.3)");
        FuncoesLigacao.K(v, grupo, "IN8", "Interação tração × cortante no concreto", FuncoesLigacao.Pior(cs, x =>
            Math.Pow(Math.Max(Tgrupo(x) / nRdc, Tmax(x) / nRdp), 1.5) + Math.Pow(Math.Abs(x.Vd) / Math.Min(vRdcp, vRdc), 1.5)), "EN 1992-4 7.2.3 (Tab. 7.3)");
        mem.Linhas.Add($"{d.N} pinos ø{F(d.Db, "0")} × hef {F(hef, "0")} mm (cabeça ø{F(dh, "0")}), grade {d.Nx} × {d.Ny} a {F(d.Sx, "0")} × {F(d.Sy, "0")} mm; " +
                       $"chapa {F(d.Bp, "0")} × {F(d.Hp, "0")} × {F(d.Tp)} mm; concreto C{F(d.Fck, "0")} {(d.Fissurado ? "fissurado" : "não fissurado")}" +
                       (d.Borda > 0 ? $", borda a {F(d.Borda, "0")} mm." : "."));
        mem.Linhas.Add($"Por pino: NRd,s = {F(nRds, "0.0")} kN, VRd,s = {F(vRds, "0.0")} kN, NRd,p = {F(nRdp, "0.0")} kN (γMs = {F(gMsN, "0.00")}/{F(gMsV, "0.00")}); " +
                       $"grupo: NRd,c = {F(nRdc, "0.0")} kN (N0 = {F(n0, "0.0")} kN, Ac,N/A0 = {F(ax * ay / (scr * scr), "0.00")}), VRd,cp = {F(vRdcp, "0.0")} kN" +
                       (d.Borda > 0 ? $", VRd,c = {F(vRdc, "0.0")} kN" : "") + "; γMc = 1,5.");
        bool geo = d.Sx >= 5 * d.Db - 1e-9 && (d.Ny < 2 || d.Sy >= 5 * d.Db - 1e-9) && hef >= 4 * d.Db - 1e-9 && d.Tp >= 0.4 * d.Db - 1e-9;
        FuncoesLigacao.Geo(v, grupo, "IN9", "Espaçamento, embutimento e espessura da chapa", geo,
            $"s = {F(Math.Min(d.Sx, d.Ny > 1 ? d.Sy : d.Sx), "0")} mm, hef = {F(hef, "0")} mm, tp = {F(d.Tp)} mm", "s ≥ 5·d; hef ≥ 4·d; tp ≥ 0,4·d (solda do pino)",
            "EN 1992-4 / ISO 13918 / AWS D1.1");
        return v;
    }
}
