using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Documentacao;
using DEB.Core.Normas.Verificacao;

namespace DEB.Core.Elementos.Metalica;

/// <summary>Comprimentos e fatores de uma barra de aço (cm): flambagem em x e y, torção, destravamento lateral da mesa e Cb.</summary>
public sealed class ComprimentosBarra
{
    public double Lx { get; set; }
    public double Ly { get; set; }
    public double Lz { get; set; }
    /// <summary>Comprimento destravado para a FLT (cm).</summary>
    public double Lb { get; set; }
    public double Cb { get; set; } = 1.0;
}

/// <summary>Esforços solicitantes de cálculo numa seção (kN, kN·cm): N &gt; 0 tração; Mx e My em módulo; Vy na alma, Vx nas mesas.</summary>
public struct EsforcosAco
{
    public string Nome;
    public double N, Mx, My, Vy, Vx;
}

/// <summary>Resistências de cálculo de uma barra (kN, kN·cm), com o estado-limite que governa.</summary>
public sealed class ResistenciasAco
{
    public double NtRd, NcRd, MxRd, MyRd, VyRd, VxRd;
    public double Chi, Lambda0, Q, Aef, Ne;
    public string ModoNe = "", GovernaMx = "", GovernaMy = "";
    public double LambdaX, LambdaY;
    public bool AlmaEsbelta;
    public PassoMemoria Memoria { get; } = new PassoMemoria { Titulo = "Resistências de cálculo (NBR 8800:2024)" };
}

/// <summary>
/// Barra de perfil I/H duplamente simétrico pela NBR 8800:2024 (D48): tração (5.2, escoamento da seção bruta — a ruptura da seção
/// líquida fica na ligação), compressão (5.3: χ pela curva única, Ne por flexão em x e y e por torção, área efetiva por larguras
/// efetivas, 5.3.4), flexão em x (Anexo D: FLT com Cb em λr e Mcr, FLM, FLA de alma não esbelta) e em y (FLM), cortante (5.4.3) e
/// interação (5.5.1.2). γa1 = 1,10 (escoamento e instabilidade). Unidades: cm, kN, kN/cm².
/// </summary>
public static class BarraAco
{
    public const double GamaA1 = 1.10, GamaA2 = 1.35;
    private static string F(double x, string f = "0.##") => Fmt.Virgula(x, f);

    public static ResistenciasAco Resistencias(PerfilI p, AcoEstrutural aco, ComprimentosBarra l, double gamaA1 = GamaA1)
    {
        var r = new ResistenciasAco();
        double fy = aco.Fy / 10, e = aco.E / 10, g = aco.G / 10;          // kN/cm²
        double raiz = Math.Sqrt(e / fy);
        var m = r.Memoria.Linhas;
        m.Add($"{p.Nome} ({(p.Soldado ? "soldado" : "laminado")}), {aco.Nome}: fy = {F(aco.Fy, "0")} MPa, E = {F(aco.E, "0")} MPa; " +
              $"A = {F(p.A, "0.0")} cm², Ix = {F(p.Ix, "0")} cm⁴, Iy = {F(p.Iy, "0")} cm⁴, Zx = {F(p.Zx, "0")} cm³, J = {F(p.J, "0.##")} cm⁴, Cw = {F(p.Cw, "0")} cm⁶.");

        // ---- tração (5.2.2 a)
        r.NtRd = p.A * fy / gamaA1;

        // ---- compressão: Ne (5.3.5.1)
        double nex = l.Lx > 0 ? Math.PI * Math.PI * e * p.Ix / (l.Lx * l.Lx) : double.PositiveInfinity;
        double ney = l.Ly > 0 ? Math.PI * Math.PI * e * p.Iy / (l.Ly * l.Ly) : double.PositiveInfinity;
        double r0q = p.Rx * p.Rx + p.Ry * p.Ry;
        double nez = l.Lz > 0 ? (Math.PI * Math.PI * e * p.Cw / (l.Lz * l.Lz) + g * p.J) / r0q : double.PositiveInfinity;
        r.Ne = Math.Min(nex, Math.Min(ney, nez));
        r.ModoNe = r.Ne == nex ? "flexão em x" : r.Ne == ney ? "flexão em y" : "torção";
        r.LambdaX = l.Lx / p.Rx; r.LambdaY = l.Ly / p.Ry;
        r.Lambda0 = Math.Sqrt(p.A * fy / r.Ne);
        r.Chi = r.Lambda0 <= 1.5 ? Math.Pow(0.658, r.Lambda0 * r.Lambda0) : 0.877 / (r.Lambda0 * r.Lambda0);
        // área efetiva (5.3.4.2): mesas AL (grupo 4 laminado, 5 soldado), alma AA (grupo 2)
        double bMesa = p.Bf / 2, tMesa = p.Tf, hAlma = p.H, kc = Math.Min(0.76, Math.Max(0.35, 4 / Math.Sqrt(hAlma / p.Tw)));
        double limMesa = p.Soldado ? 0.64 * Math.Sqrt(kc) * raiz : 0.56 * raiz, limAlma = 1.49 * raiz;
        double Bef(double b, double t, double lim, double c1, double c2)
        {
            if (b / t <= lim / Math.Sqrt(r.Chi)) return b;
            double sel = Math.Pow(c2 * lim / (b / t), 2) * fy;
            double q = Math.Sqrt(sel / (r.Chi * fy));
            return Math.Min(b, b * (1 - c1 * q) * q);
        }
        double befMesa = Bef(bMesa, tMesa, limMesa, 0.22, 1.49), befAlma = Bef(hAlma, p.Tw, limAlma, 0.18, 1.31);
        r.Aef = p.A - 4 * (bMesa - befMesa) * tMesa / 100 - (hAlma - befAlma) * p.Tw / 100;   // mm² → cm²
        r.Q = r.Aef / p.A;
        r.NcRd = r.Chi * r.Aef * fy / gamaA1;
        m.Add($"Compressão (5.3): Nex = {F(nex, "0")} kN, Ney = {F(ney, "0")} kN, Nez = {F(nez, "0")} kN → Ne = {F(r.Ne, "0")} kN ({r.ModoNe}); " +
              $"λ0 = √(A·fy/Ne) = {F(r.Lambda0, "0.###")} → χ = {F(r.Chi, "0.###")}; b/t da mesa = {F(bMesa / tMesa, "0.#")} (lim. {F(limMesa, "0.#")}), " +
              $"h/tw = {F(hAlma / p.Tw, "0.#")} (lim. {F(limAlma, "0.#")}); Aef = {F(r.Aef, "0.0")} cm² → Nc,Rd = χ·Aef·fy/γa1 = {F(r.NcRd, "0")} kN.");

        // ---- flexão em x (Anexo D, Tabela D.1, seções I e H com dois eixos de simetria)
        double sr = 0.3 * fy, mpl = p.Zx * fy, mrFlt = (fy - sr) * p.Wx;
        double mrdFlt;
        {
            double lam = l.Lb / p.Ry, lp = 1.76 * raiz;
            double b1 = (fy - sr) * p.Wx / (e * p.J);
            double cb = l.Cb;
            double lr = 1.38 * Math.Sqrt(p.Iy * p.J) / (p.Ry * p.J * b1) * Math.Sqrt(1 + Math.Sqrt(1 + 27 * p.Cw * b1 * b1 / (cb * cb * p.Iy)));
            double mcr = l.Lb > 0 ? cb * Math.PI * Math.PI * e * p.Iy / (l.Lb * l.Lb) * Math.Sqrt(p.Cw / p.Iy * (1 + 0.039 * p.J * l.Lb * l.Lb / p.Cw)) : double.PositiveInfinity;
            double mn = lam <= lp ? mpl : lam <= lr ? mpl - (mpl - mrFlt) * (lam - lp) / (lr - lp) : mcr;
            mrdFlt = Math.Min(mn, mpl) / gamaA1;
            m.Add($"FLT (D.2.1): Lb = {F(l.Lb, "0")} cm, λ = Lb/ry = {F(lam, "0.#")}, λp = {F(lp, "0.#")}, λr = {F(lr, "0.#")} (Cb = {F(cb, "0.##")}); " +
                  $"Mr = (fy − σr)·Wx = {F(mrFlt / 100, "0.#")} kN·m, Mcr = {F(Math.Min(mcr, 1e12) / 100, "0.#")} kN·m → MRd = {F(mrdFlt / 100, "0.#")} kN·m.");
        }
        double mrdFlm;
        {
            double lam = bMesa / tMesa, lp = 0.38 * raiz;
            double lr = p.Soldado ? 0.95 * Math.Sqrt(e / ((fy - sr) / kc)) : 0.83 * Math.Sqrt(e / (fy - sr));
            double mcr = (p.Soldado ? 0.90 * e * kc : 0.69 * e) / (lam * lam) * p.Wx;
            double mn = lam <= lp ? mpl : lam <= lr ? mpl - (mpl - mrFlt) * (lam - lp) / (lr - lp) : mcr;
            mrdFlm = mn / gamaA1;
            m.Add($"FLM: b/t = {F(lam, "0.#")}, λp = {F(lp, "0.#")}, λr = {F(lr, "0.#")} → MRd = {F(mrdFlm / 100, "0.#")} kN·m.");
        }
        double mrdFla;
        {
            double lam = hAlma / p.Tw, lp = 3.76 * raiz, lr = 5.70 * raiz, mr = fy * p.Wx;
            r.AlmaEsbelta = lam > lr;
            double mn = lam <= lp ? mpl : lam <= lr ? mpl - (mpl - mr) * (lam - lp) / (lr - lp) : mr;   // > λr: Anexo E (não tratado)
            mrdFla = mn / gamaA1;
            m.Add($"FLA: h/tw = {F(lam, "0.#")}, λp = {F(lp, "0.#")}, λr = {F(lr, "0.#")}" + (r.AlmaEsbelta ? " — alma esbelta: Anexo E (não tratado nesta etapa)" : "") +
                  $" → MRd = {F(mrdFla / 100, "0.#")} kN·m.");
        }
        double lim152 = 1.5 * p.Wx * fy / gamaA1;
        r.MxRd = new[] { mrdFlt, mrdFlm, mrdFla, lim152 }.Min();
        r.GovernaMx = r.MxRd == mrdFlt ? "FLT" : r.MxRd == mrdFlm ? "FLM" : r.MxRd == mrdFla ? "FLA" : "1,5·W·fy";

        // ---- flexão em y (só FLM para I; D.2.8-c)
        {
            double lam = bMesa / tMesa, lp = 0.38 * raiz, mply = p.Zy * fy, mry = (fy - sr) * p.Wy;
            double lr = p.Soldado ? 0.95 * Math.Sqrt(e / ((fy - sr) / kc)) : 0.83 * Math.Sqrt(e / (fy - sr));
            double mcr = (p.Soldado ? 0.90 * e * kc : 0.69 * e) / (lam * lam) * p.Wy;
            double mn = lam <= lp ? mply : lam <= lr ? mply - (mply - mry) * (lam - lp) / (lr - lp) : mcr;
            r.MyRd = Math.Min(mn, 1.5 * p.Wy * fy) / gamaA1;
            r.GovernaMy = mn <= 1.5 * p.Wy * fy ? "FLM" : "1,5·W·fy";
            m.Add($"Flexão em y: Mpl,y = {F(mply / 100, "0.#")} kN·m, FLM → My,Rd = {F(r.MyRd / 100, "0.#")} kN·m.");
        }

        // ---- cortante (5.4.3.1: alma, kv = 5,34 sem enrijecedores; mesas: kv = 1,2, h = bf/2, t = tf)
        double Vrd(double lam, double kv, double aw)
        {
            double lp = 1.10 * Math.Sqrt(kv * e / fy), lr = 1.37 * Math.Sqrt(kv * e / fy), vpl = 0.60 * aw * fy;
            return (lam <= lp ? vpl : lam <= lr ? lp / lam * vpl : 1.24 * Math.Pow(lp / lam, 2) * vpl) / gamaA1;
        }
        r.VyRd = Vrd(hAlma / p.Tw, 5.34, p.D * p.Tw / 100);
        r.VxRd = Vrd(bMesa / tMesa, 1.2, 2 * p.Bf * p.Tf / 100);
        m.Add($"Cortante (5.4.3): na alma Vy,Rd = {F(r.VyRd, "0")} kN (kv = 5,34, Aw = d·tw); nas mesas Vx,Rd = {F(r.VxRd, "0")} kN.");
        m.Add($"Tração (5.2.2): Nt,Rd = Ag·fy/γa1 = {F(r.NtRd, "0")} kN (a ruptura da seção líquida efetiva é verificada na ligação).");
        return r;
    }

    /// <summary>Verificações da barra para os casos (códigos MT1…MT7), com o caso que governa cada uma.</summary>
    public static ListaVerificacoes Verificar(PerfilI p, AcoEstrutural aco, ComprimentosBarra l, IEnumerable<EsforcosAco> casos, string grupo, out ResistenciasAco r, double gamaA1 = GamaA1)
    {
        r = Resistencias(p, aco, l, gamaA1);
        var v = new ListaVerificacoes();
        var cs = casos.ToList();
        double Eta(EsforcosAco c, ResistenciasAco rr)
        {
            double nrd = c.N >= 0 ? rr.NtRd : rr.NcRd, nn = Math.Abs(c.N) / nrd, mm = c.Mx / rr.MxRd + c.My / rr.MyRd;
            return nn >= 0.2 ? nn + 8.0 / 9 * mm : nn / 2 + mm;
        }
        void K(string cod, string desc, double eta, string caso, string refer, string dica = "") =>
            v.Add(new ResultadoVerificacao
            {
                Codigo = cod, Grupo = grupo, Descricao = desc, Valor = $"η = {F(eta, "0.00")}" + (caso != "" ? $" ({caso})" : ""), Limite = "≤ 1,00",
                Referencia = refer, Situacao = eta > 1 + 1e-9 ? SituacaoVerificacao.NaoAtende : SituacaoVerificacao.Ok, Dica = eta > 1 + 1e-9 ? dica : "",
            });
        (double, string) Pior(Func<EsforcosAco, double> f) => cs.Count == 0 ? (0, "") : cs.Select(c => (f(c), c.Nome)).OrderByDescending(x => x.Item1).First();

        var rr0 = r;
        var t = Pior(c => c.N > 0 ? c.N / rr0.NtRd : 0); K("MT1", "Tração", t.Item1, t.Item2, "NBR 8800 5.2", "Aumente o perfil.");
        var c0 = Pior(c => c.N < 0 ? -c.N / rr0.NcRd : 0); K("MT2", $"Compressão (χ = {F(r.Chi, "0.###")}, {r.ModoNe})", c0.Item1, c0.Item2, "NBR 8800 5.3", "Trave a barra ou aumente o perfil.");
        var mx = Pior(c => c.Mx / rr0.MxRd); K("MT3", $"Flexão em x ({r.GovernaMx})", mx.Item1, mx.Item2, "NBR 8800 5.4.2 e Anexo D", "Trave a mesa comprimida ou aumente o perfil.");
        var my = Pior(c => c.My / rr0.MyRd); K("MT4", "Flexão em y", my.Item1, my.Item2, "NBR 8800 5.4.2 e Anexo D");
        var vv = Pior(c => Math.Max(Math.Abs(c.Vy) / rr0.VyRd, Math.Abs(c.Vx) / rr0.VxRd)); K("MT5", "Força cortante", vv.Item1, vv.Item2, "NBR 8800 5.4.3");
        var it = Pior(c => Eta(c, rr0)); K("MT6", "Força axial e momentos combinados", it.Item1, it.Item2, "NBR 8800 5.5.1.2");
        double lam = Math.Max(r.LambdaX, r.LambdaY);
        bool comprimida = cs.Any(c => c.N < 0);
        v.Add(new ResultadoVerificacao
        {
            Codigo = "MT7", Grupo = grupo, Descricao = "Índice de esbeltez", Valor = $"L/r = {F(lam, "0")}", Limite = comprimida ? "≤ 200" : "≤ 300 (recomendação)",
            Referencia = comprimida ? "NBR 8800 5.3.7" : "NBR 8800 5.2.8",
            Situacao = lam > (comprimida ? 200 : 300) ? (comprimida ? SituacaoVerificacao.NaoAtende : SituacaoVerificacao.Conferir) : SituacaoVerificacao.Ok,
        });
        if (r.AlmaEsbelta)
            v.Add(new ResultadoVerificacao { Codigo = "MT8", Grupo = grupo, Descricao = "Alma esbelta (h/tw > 5,70·√(E/fy))", Valor = $"h/tw = {F(p.H / p.Tw, "0")}",
                Referencia = "NBR 8800 Anexo E", Situacao = SituacaoVerificacao.Conferir, Dica = "Viga de alma esbelta: verificação do Anexo E ainda não feita." });
        return v;
    }

    /// <summary>
    /// Cb (5.4.2.3-a) pelos momentos em módulo no trecho destravado: máximo e nos quartos (Rm = 1 para a seção duplamente simétrica).
    /// </summary>
    public static double Cb(double mMax, double mA, double mB, double mC)
    {
        double den = 2.5 * mMax + 3 * mA + 4 * mB + 3 * mC;
        return den <= 1e-12 ? 1 : 12.5 * mMax / den;
    }
}
