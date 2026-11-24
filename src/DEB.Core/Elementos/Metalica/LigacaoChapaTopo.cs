using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Documentacao;
using DEB.Core.Normas.Verificacao;

namespace DEB.Core.Elementos.Metalica;

/// <summary>
/// Ligação rígida viga–pilar por chapa de topo (D49), soldada na ponta da viga e parafusada na mesa do pilar: duas colunas de
/// parafusos com gabarito g; em volta da mesa tracionada, uma linha acima (chapa estendida) e as linhas internas abaixo; uma linha
/// junto à mesa comprimida para o cortante. Dimensões em mm; forças em kN; momentos em kN·m.
/// </summary>
public sealed class DadosChapaTopo
{
    public PerfilI Viga { get; set; }
    public AcoEstrutural AcoChapa { get; set; }
    public TipoParafuso Parafuso { get; set; } = TipoParafuso.Lista[2];
    public double Db { get; set; } = 22.2;
    public bool Estendida { get; set; } = true;
    public double G { get; set; } = 100;
    /// <summary>Distância da face da mesa ao eixo do parafuso (vertical).</summary>
    public double Pf { get; set; } = 45;
    public int LinhasInternas { get; set; } = 1;
    /// <summary>Espessura (mm); 0 = a menor comercial que dispensa o efeito de alavanca (6.3.5.3).</summary>
    public double Tp { get; set; }
    /// <summary>Largura da chapa (mm); 0 = bf + 25.</summary>
    public double Bp { get; set; }
    /// <summary>Perna do filete nas mesas (mm); 0 = a que desenvolve a força da mesa.</summary>
    public double PernaMesa { get; set; }
    public double PernaAlma { get; set; } = 6;
    public double Fw { get; set; } = 485;
    public double BpEf => Bp > 0 ? Bp : Viga.Bf + 25;
    /// <summary>Espessura adotada na última verificação (a do modelo ou a escolhida pelo cálculo), para o desenho e o Revit.</summary>
    public double TpAdotada { get; set; }
    /// <summary>Parafusos tracionados (na região da mesa tracionada) e total.</summary>
    public int NTracionados => 2 * (LinhasInternas + (Estendida ? 1 : 0));
    public int NTotal => NTracionados + 2;

    public static DadosChapaTopo DoModelo(Ligacoes.ModeloLigacao m, PerfilI viga) => new DadosChapaTopo
    {
        Viga = viga, AcoChapa = AcoEstrutural.Buscar(m.Texto("acoChapa")) ?? AcoEstrutural.Buscar("A36"), Parafuso = TipoParafuso.Buscar(m.Texto("parafuso")),
        Db = m.Numero("db"), Estendida = m.Texto("estendida") != "Não", G = m.Numero("g"), Pf = m.Numero("pf"), LinhasInternas = (int)m.Numero("linhasInternas"),
        Tp = m.Numero("tp", 0), Bp = m.Numero("bp", 0), PernaMesa = m.Numero("pernaMesa", 0), PernaAlma = m.Numero("pernaAlma"), Fw = m.Numero("fw"),
    };
}

public static class LigacaoChapaTopo
{
    private static string F(double x, string f = "0.#") => Fmt.Virgula(x, f);

    /// <summary>
    /// Verificações CT1…CT9 para os casos (Md em kN·m, Vd e Nd em kN, Nd &gt; 0 tração). Força na mesa tracionada Ff = |M|/(d − tf) + N/2,
    /// repartida igualmente entre os parafusos tracionados; efeito de alavanca por 6.3.5 com b = pf, a = borda (≤ 1,25·b) e largura
    /// tributária p = mín(bp/2; 3,5·b) por parafuso.
    /// </summary>
    public static ListaVerificacoes Verificar(DadosChapaTopo d, IEnumerable<(string Nome, double Md, double Vd, double Nd)> casos, string grupo,
                                              out PassoMemoria mem, double gamaA1 = 1.10, double gamaA2 = 1.35)
    {
        mem = new PassoMemoria { Titulo = "Ligação rígida por chapa de topo (NBR 8800:2024, 6.2, 6.3 e 6.3.5)" };
        var v = new ListaVerificacoes();
        var cs = casos.ToList();
        var w = d.Viga;
        double braco = w.D - w.Tf;
        double Ff((string Nome, double Md, double Vd, double Nd) c) => Math.Abs(c.Md) * 1000 / braco + c.Nd / 2;          // kN
        double ft0Max = cs.Count == 0 ? 0 : cs.Max(c => Math.Max(0, Ff(c)) / d.NTracionados);                               // kN por parafuso
        double ftRd = ComponentesLigacao.TracaoParafuso(d.Db, d.Parafuso, gamaA2), fvRd = ComponentesLigacao.CisalhamentoParafuso(d.Db, d.Parafuso, true, gamaA2);
        double df = ComponentesLigacao.Furo(d.Db), b = d.Pf, aBorda = Math.Min(Math.Max(d.Pf, 1.5 * d.Db), 1.25 * b);
        double p = Math.Min(d.BpEf / 2, 3.5 * b), fu = d.AcoChapa.Fu;
        double tRigida = ft0Max > 0 ? Math.Sqrt(4 * (b - 0.5 * d.Db) * ft0Max * 1000 * gamaA1 / (p * fu)) : 0;
        double tp = d.Tp > 0 ? d.Tp : DadosPlacaBase.Espessuras.FirstOrDefault(t => t >= tRigida - 1e-9);
        if (tp <= 0) tp = DadosPlacaBase.Espessuras.Last();
        d.TpAdotada = tp;
        double delta = 1 - df / p;
        double beta = ft0Max > 0 ? (aBorda + 0.5 * d.Db) / (b - 0.5 * d.Db) * (ftRd / ft0Max - 1) : 1;
        double alfa = beta >= 1 ? 1 : beta <= 0 ? 0 : Math.Min(1, beta / (delta * (1 - beta)));
        double tFlex = ft0Max > 0 ? Math.Sqrt(4 * (b - 0.5 * d.Db) * ft0Max * 1000 * gamaA1 / (p * fu * (1 + delta * alfa))) : 0;
        mem.Linhas.Add($"Viga {w.Nome}; chapa {F(d.BpEf, "0")} mm de largura ({d.AcoChapa.Nome}), {(d.Estendida ? "estendida" : "não estendida")}; {d.NTotal} parafusos ø{F(d.Db)} {d.Parafuso.Nome} " +
                       $"({d.NTracionados} na mesa tracionada), g = {F(d.G, "0")} mm, pf = {F(d.Pf, "0")} mm.");
        mem.Linhas.Add($"Ff = |M|/(d − tf) + N/2 (braço {F(braco, "0")} mm); Ft0,Sd,máx = {F(ft0Max, "0.0")} kN por parafuso; Ft,Rd = 0,75·Ab·fub/γa2 = {F(ftRd, "0.0")} kN.");
        mem.Linhas.Add($"Efeito de alavanca (6.3.5): b = {F(b, "0")} mm, a = {F(aBorda, "0")} mm (≤ 1,25·b), p = {F(p, "0")} mm, δ = {F(delta, "0.00")}, β = {F(beta, "0.00")}, α = {F(alfa, "0.00")}; " +
                       $"t para placa rígida = {F(tRigida, "0.0")} mm; t para placa flexível = {F(tFlex, "0.0")} mm → " +
                       (d.Tp > 0 ? $"chapa de {F(tp)} mm (do modelo)." : $"chapa comercial de {F(tp)} mm (rígida, sem alavanca)."));

        void K(string cod, string desc, string valor, string lim, bool ok, string refer, string dica = "", SituacaoVerificacao seFalha = SituacaoVerificacao.NaoAtende) =>
            v.Add(new ResultadoVerificacao { Codigo = cod, Grupo = grupo, Descricao = desc, Valor = valor, Limite = lim, Referencia = refer,
                Situacao = ok ? SituacaoVerificacao.Ok : seFalha, Dica = ok ? "" : dica });
        (double, string) Pior(Func<(string Nome, double Md, double Vd, double Nd), double> f) =>
            cs.Count == 0 ? (0, "") : cs.Select(c => (f(c), c.Nome)).OrderByDescending(x => x.Item1).First();

        // CT1 espessura (placa flexível com alavanca: a mínima que atende; rígida dispensa o aumento da força no parafuso)
        K("CT1", "Espessura da chapa de topo (efeito de alavanca)", $"tp = {F(tp)} mm", $"≥ {F(tFlex, "0.0")} mm (flexível) · {F(tRigida, "0.0")} mm (rígida)",
          tp >= tFlex - 1e-9, "NBR 8800 6.3.5.3 e 6.3.5.4", "Aumente a espessura, o diâmetro ou o número de parafusos.");
        if (beta < 0) K("CT2", "Parafusos tracionados (β ≥ 0)", $"β = {F(beta, "0.00")}", "≥ 0", false, "NBR 8800 6.3.5.2", "Ft0,Sd > Ft,Rd: troque o parafuso.");
        // CT3 tração + cortante nos tracionados (6.3.3.4), cortante repartido por todos os parafusos
        var it = Pior(c => { double ft = Math.Max(0, Ff(c)) / d.NTracionados, fv = Math.Abs(c.Vd) / d.NTotal; return Math.Pow(ft / ftRd, 2) + Math.Pow(fv / fvRd, 2); });
        K("CT3", "Parafusos: tração e cisalhamento combinados", $"{F(it.Item1, "0.00")} ({it.Item2})", "≤ 1,00", it.Item1 <= 1 + 1e-9, "NBR 8800 6.3.3.4",
          "Aumente o diâmetro ou o número de parafusos.");
        // CT4 pressão de contato na chapa (cortante)
        double lf = Math.Min(d.Pf - df / 2, d.G - df);
        double fc = ComponentesLigacao.PressaoContato(lf, tp, d.Db, fu, true, gamaA2);
        var pc = Pior(c => Math.Abs(c.Vd) / d.NTotal / fc);
        K("CT4", "Pressão de contato na chapa", $"η = {F(pc.Item1, "0.00")} ({pc.Item2})", "≤ 1,00", pc.Item1 <= 1 + 1e-9, "NBR 8800 6.3.3.3");
        // CT5 solda das mesas (dois filetes por mesa, 2·bf − tw) para Ff; CT6 solda da alma para V
        double ffMax = cs.Count == 0 ? 0 : cs.Max(c => Math.Max(0, Ff(c)));
        double compMesa = 2 * w.Bf - w.Tw, compAlma = 2 * (w.D - 2 * w.Tf);
        double pernaMesa = d.PernaMesa > 0 ? d.PernaMesa : Math.Max(5, Math.Ceiling(ffMax / (ComponentesLigacao.FiletePorMm(1, d.Fw) * compMesa)));
        double fwMesa = ComponentesLigacao.FiletePorMm(pernaMesa, d.Fw) * compMesa;
        K("CT5", "Solda das mesas à chapa", $"Ff = {F(ffMax, "0")} kN · filete {F(pernaMesa, "0")} mm", $"≤ {F(fwMesa, "0")} kN", ffMax <= fwMesa + 1e-9, "NBR 8800 6.2.5 (Tab. 9)",
          "Aumente o filete ou use penetração total na mesa tracionada.");
        double fwAlma = ComponentesLigacao.FiletePorMm(d.PernaAlma, d.Fw) * compAlma;
        var sa = Pior(c => Math.Abs(c.Vd) / fwAlma);
        K("CT6", "Solda da alma à chapa (cortante)", $"η = {F(sa.Item1, "0.00")} ({sa.Item2})", "≤ 1,00", sa.Item1 <= 1 + 1e-9, "NBR 8800 6.2.5 (Tab. 9)");
        // CT7 disposições
        bool geo = d.G >= 3 * d.Db - 1e-9 && d.BpEf >= d.G + 2 * 1.25 * d.Db - 1e-9 && d.Pf >= pernaMesa + df / 2 + 5;
        K("CT7", "Gabarito, bordas e folga para a solda", $"g = {F(d.G, "0")} mm, bp = {F(d.BpEf, "0")} mm, pf = {F(d.Pf, "0")} mm",
          "g ≥ 3·db; bp ≥ g + 2,5·db; pf ≥ filete + furo/2 + 5 mm", geo, "NBR 8800 6.3.9 e 6.3.11", "Ajuste g, bp ou pf.", SituacaoVerificacao.Conferir);
        // CT8 lado do pilar
        v.Add(new ResultadoVerificacao { Codigo = "CT8", Grupo = grupo, Descricao = "Mesa e alma do pilar (flexão local, escoamento e cisalhamento do painel)",
            Valor = "não verificada nesta etapa", Referencia = "NBR 8800 5.7", Situacao = SituacaoVerificacao.Conferir,
            Dica = "Confira 5.7.2 a 5.7.7 e os enrijecedores do pilar na altura das mesas da viga." });
        mem.Linhas.Add($"Solda: mesas {F(pernaMesa, "0")} mm em {F(compMesa, "0")} mm por mesa ({F(fwMesa, "0")} kN); alma {F(d.PernaAlma, "0")} mm em {F(compAlma, "0")} mm ({F(fwAlma, "0")} kN).");
        return v;
    }
}
