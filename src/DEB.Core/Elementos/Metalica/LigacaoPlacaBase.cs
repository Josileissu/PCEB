using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Documentacao;
using DEB.Core.Normas.Verificacao;

namespace DEB.Core.Elementos.Metalica;

/// <summary>
/// Placa de base de pilar I/H, tipo 1 da NBR 8800:2024 (6.7, Figura 21: chumbadores externos, fora das mesas, nb de 4 a 8). Dimensões
/// em mm; tensões em MPa; forças em kN; momentos em kN·m (em torno do eixo de maior inércia do perfil, o único previsto em 6.7.1.3).
/// </summary>
public sealed class DadosPlacaBase
{
    public PerfilI Pilar { get; set; }
    public AcoEstrutural AcoPlaca { get; set; }
    /// <summary>Espessura da placa (mm); 0 = o PCEB escolhe a menor comercial que atende.</summary>
    public double Tp { get; set; }
    /// <summary>ℓx (paralelo à alma, mm) e ℓy (paralelo às mesas, mm); 0 = pela Tabela 18 (ℓx = d + 4·a1; ℓy = (0,5·nb − 1)·a2 + 2·a1 ≥ bf + 25).</summary>
    public double Lx { get; set; }
    public double Ly { get; set; }
    public int Nb { get; set; } = 4;
    public double Db { get; set; } = 25;
    /// <summary>Resistência à ruptura do chumbador (6.7.1.5-c: ASTM A36 ou equivalente, fu = 400 MPa).</summary>
    public double FubChumbador { get; set; } = 400;
    public string AcoChumbador { get; set; } = "ASTM F1554 Gr. 36";
    public double Fck { get; set; } = 25;
    public double GamaC { get; set; } = 1.4;
    /// <summary>A2/A1 do apoio (6.6.5).</summary>
    public double A2A1 { get; set; } = 1;
    public double Perna { get; set; } = 8;
    public double Fw { get; set; } = 485;
    public double Mu { get; set; } = 0.45;

    // Tabela 18 (por db, mm): a1, a2, a3, fck mínimo (MPa)
    private static readonly (double Db, double A1, double A2, double A3, double FckMin)[] Tab18 =
    {
        (19, 40, 80, 120, 20), (22, 45, 90, 140, 20), (25, 50, 100, 160, 25), (32, 65, 130, 190, 25),
        (38, 80, 160, 230, 25), (44, 90, 180, 270, 25), (50, 100, 200, 300, 30),
    };

    /// <summary>Linha da Tabela 18 para o diâmetro (o mais próximo; <paramref name="exato"/> diz se está na tabela).</summary>
    public (double A1, double A2, double A3, double FckMin) Tabela18(out bool exato)
    {
        var l = Tab18.OrderBy(t => Math.Abs(t.Db - Db)).First();
        exato = Math.Abs(l.Db - Db) <= 1.0;
        return (l.A1, l.A2, l.A3, l.FckMin);
    }

    public double A1 => Tabela18(out _).A1;
    public double LxEf => Lx > 0 ? Lx : Pilar.D + 4 * A1;
    public double LyEf => Ly > 0 ? Ly : Math.Max(Pilar.Bf + 25, (0.5 * Nb - 1) * Tabela18(out _).A2 + 2 * A1);
    /// <summary>Distância da linha de chumbadores mais externa ao eixo da placa.</summary>
    public double A => LxEf / 2 - A1;

    /// <summary>Espessuras comerciais de chapa (mm).</summary>
    public static readonly double[] Espessuras = { 12.5, 16, 19, 22.4, 25, 31.5, 37.5, 44.5, 50, 57, 63, 75, 89, 100 };

    public static DadosPlacaBase DoModelo(Ligacoes.ModeloLigacao m, PerfilI pilar, double gamaC)
    {
        string ach = m.Texto("acoChumb");
        return new DadosPlacaBase
        {
            Pilar = pilar, AcoPlaca = AcoEstrutural.Buscar(m.Texto("acoPlaca")) ?? AcoEstrutural.Buscar("A36"),
            Tp = m.Numero("tp", 0), Lx = m.Numero("h", 0), Ly = m.Numero("b", 0), Nb = (int)m.Numero("nChumb"), Db = m.Numero("dChumb"),
            AcoChumbador = ach, FubChumbador = ach.Contains("55") ? 517 : 400, Fck = m.Numero("fck"), A2A1 = m.Numero("a2a1"), GamaC = gamaC,
            Perna = m.Numero("perna"), Fw = m.Numero("fw"),
        };
    }
}

public static class LigacaoPlacaBase
{
    private static string F(double x, string f = "0.#") => Fmt.Virgula(x, f);

    /// <summary>Resultado de um caso: espessura mínima, diâmetro mínimo, força no chumbador, tensões e cortante resistente.</summary>
    public sealed class Caso
    {
        public string Nome = "", Tipo = "";
        public double TpMin, DbMin, Ft, SigmaSd, SigmaRd, VRd, Vd, Lc, Lmax;
        public bool Geometria = true;   // condição de 6.7.2.2-c/f atendida
        public bool PrecisaDispositivoCortante;
    }

    /// <summary>
    /// Cálculo de um caso (6.7.2.2): Nd &gt; 0 tração (kN), Md (kN·m) em torno do eixo de maior inércia, Vd (kN).
    /// </summary>
    public static Caso Calcular(DadosPlacaBase d, string nome, double nd, double md, double vd, double gamaA1 = 1.10, double gamaA2 = 1.35)
    {
        var c = new Caso { Nome = nome, Vd = Math.Abs(vd) };
        double lx = d.LxEf, ly = d.LyEf, a = d.A, a1 = d.A1, nb = d.Nb, dd = d.Pilar.D, bf = d.Pilar.Bf;
        double fyg = d.AcoPlaca.Fy / gamaA1;                                     // MPa
        double sRd = Math.Min(0.85 * d.Fck / d.GamaC * Math.Sqrt(Math.Min(4, Math.Max(1, d.A2A1))), 1.7 * d.Fck / d.GamaC);
        double tauRd = Math.Min(0.2 * d.Fck / d.GamaC, 4);
        double m = (lx - 0.95 * dd) / 2, n = (ly - 0.80 * bf) / 2, n0 = Math.Sqrt(dd * bf) / 4;
        double fub = d.FubChumbador;
        double N = Math.Abs(nd) * 1000, M = Math.Abs(md) * 1e6;                   // N e N·mm
        double e = N > 1e-9 ? M / N : double.PositiveInfinity;
        c.SigmaRd = sRd;
        double DbMin(double ft) => Math.Sqrt(4 * ft / (0.75 * Math.PI * fub / gamaA2));
        double P(double lc) => Math.Sqrt(Math.Max(0, lc * (2 * m - lc)));
        double Lambda(double nSd)
        {
            double x = 4 * dd * bf / Math.Pow(dd + bf, 2) * nSd / (lx * ly * sRd);
            return x >= 1 ? 1 : Math.Min(1, 2 * Math.Sqrt(x) / (1 + Math.Sqrt(1 - x)));
        }
        double vMax = tauRd * lx * ly / 1000;

        if (nd <= 0)   // compressão
        {
            double eLim = 0.5 * (lx - N / (sRd * ly));
            if (M < 1e-6)
            {
                c.Tipo = "C1"; c.SigmaSd = N / (lx * ly); c.Lc = lx;
                c.Lmax = Math.Max(Math.Max(m, n), Lambda(N) * n0);
                c.TpMin = c.Lmax * Math.Sqrt(2 * c.SigmaSd / fyg);
                c.VRd = Math.Min(d.Mu * c.SigmaSd * lx * ly / gamaA2 / 1000, vMax);
            }
            else if (e <= eLim)
            {
                c.Tipo = "C2"; c.Lc = lx - 2 * e; c.SigmaSd = N / (c.Lc * ly);
                c.Lmax = c.Lc >= m ? Math.Max(Math.Max(m, n), Lambda(N) * n0) : Math.Max(P(c.Lc), n);
                c.TpMin = c.Lmax * Math.Sqrt(2 * c.SigmaSd / fyg);
                c.VRd = Math.Min(d.Mu * c.SigmaSd * c.Lc * ly / gamaA2 / 1000, vMax);
            }
            else
            {
                c.Tipo = "C3";
                double k = lx / 2 + a, rad = k * k - 2 * N * (e + a) / (sRd * ly);
                if (rad < 0) { c.Geometria = false; rad = 0; }
                c.Lc = k - Math.Sqrt(rad); c.SigmaSd = sRd;
                c.Ft = 2 * (sRd * c.Lc * ly - N) / nb;                            // N por chumbador
                c.Lmax = c.Lc >= m ? Math.Max(Math.Max(m, n), Lambda(N) * n0) : Math.Max(P(c.Lc), n);
                double t1 = c.Lmax * Math.Sqrt(2 * sRd / fyg), t2 = Math.Sqrt(2 * nb * c.Ft * Math.Max(0, m - a1) / (ly * fyg));
                c.TpMin = Math.Max(t1, t2); c.DbMin = DbMin(c.Ft);
                c.VRd = Math.Min(d.Mu * sRd * c.Lc * ly / gamaA2 / 1000, vMax);
            }
        }
        else           // tração
        {
            if (M < 1e-6 || e <= a)
            {
                c.Tipo = M < 1e-6 ? "T1" : "T2";
                c.Ft = N / nb + (M > 0 ? M / (a * nb) : 0);
                c.TpMin = Math.Sqrt(2 * nb * c.Ft * Math.Max(0, m - a1) / (ly * fyg));
                c.DbMin = DbMin(c.Ft);
                c.VRd = 0; c.PrecisaDispositivoCortante = c.Vd > 0;
            }
            else
            {
                c.Tipo = "T3";
                double k = lx / 2 + a, rad = k * k - 2 * N * (e - a) / (sRd * ly);
                if (rad < 0) { c.Geometria = false; rad = 0; }
                c.Lc = k - Math.Sqrt(rad); c.SigmaSd = sRd;
                c.Ft = 2 * (sRd * c.Lc * ly + N) / nb;
                c.Lmax = c.Lc >= m ? Math.Max(m, n) : Math.Max(P(c.Lc), n);
                double t1 = c.Lmax * Math.Sqrt(2 * sRd / fyg), t2 = Math.Sqrt(2 * nb * c.Ft * Math.Max(0, m - a1) / (ly * fyg));
                c.TpMin = Math.Max(t1, t2); c.DbMin = DbMin(c.Ft);
                c.VRd = Math.Min(d.Mu * sRd * c.Lc * ly / gamaA2 / 1000, vMax);
            }
        }
        if (c.Vd > c.VRd + 1e-9) c.PrecisaDispositivoCortante = true;
        return c;
    }

    /// <summary>Verificações PB1…PB8 para os casos (Nd tração +, Md, Vd); escolhe a espessura comercial se a do modelo for "auto".</summary>
    public static ListaVerificacoes Verificar(DadosPlacaBase d, IEnumerable<(string Nome, double Nd, double Md, double Vd)> casos, string grupo,
                                              out PassoMemoria mem, double gamaA1 = 1.10, double gamaA2 = 1.35)
    {
        mem = new PassoMemoria { Titulo = "Placa de base (NBR 8800:2024, 6.6.5 e 6.7 — base tipo 1)" };
        var v = new ListaVerificacoes();
        var res = casos.Select(c => Calcular(d, c.Nome, c.Nd, c.Md, c.Vd, gamaA1, gamaA2)).ToList();
        var (a1, a2, a3, fckMin) = d.Tabela18(out bool naTabela);
        double tpMin = res.Count == 0 ? 0 : res.Max(r => r.TpMin), dbMin = res.Count == 0 ? 0 : res.Max(r => r.DbMin);
        bool auto = d.Tp <= 0;
        double tp = auto ? DadosPlacaBase.Espessuras.FirstOrDefault(t => t >= tpMin - 1e-9) : d.Tp;
        if (tp <= 0) tp = DadosPlacaBase.Espessuras.Last();
        var crit = res.OrderByDescending(r => r.TpMin).FirstOrDefault();
        mem.Linhas.Add($"Pilar {d.Pilar.Nome}; placa {F(d.LxEf, "0")} × {F(d.LyEf, "0")} mm ({d.AcoPlaca.Nome}); {d.Nb} chumbadores ø{F(d.Db, "0")} {d.AcoChumbador} " +
                       $"a {F(a1, "0")} mm da face (Tab. 18: a1 = {F(a1, "0")}, a2 = {F(a2, "0")} mm); a = {F(d.A, "0")} mm.");
        if (res.Count > 0)
            mem.Linhas.Add($"σc,Rd = 0,85·fck/γc·√(A2/A1) ≤ 1,7·fck/γc = {F(res[0].SigmaRd, "0.00")} MPa (fck = {F(d.Fck, "0")} MPa, A2/A1 = {F(d.A2A1, "0.##")}).");
        foreach (var r in res)
            mem.Linhas.Add($"  {r.Nome}: caso {r.Tipo}" + (r.Lc > 0 ? $", ℓc = {F(r.Lc, "0")} mm" : "") + (r.SigmaSd > 0 ? $", σc = {F(r.SigmaSd, "0.00")} MPa" : "") +
                           (r.Ft > 0 ? $", Ft = {F(r.Ft / 1000, "0.0")} kN por chumbador" : "") + $" → tp,mín = {F(r.TpMin, "0.0")} mm" + (r.DbMin > 0 ? $", db,mín = {F(r.DbMin, "0.0")} mm" : "") +
                           $"; Vd = {F(r.Vd, "0.0")} kN, VRd = {F(r.VRd, "0.0")} kN.");
        mem.Linhas.Add(auto ? $"Espessura: tp,mín = {F(tpMin, "0.0")} mm → chapa comercial de {F(tp)} mm." : $"Espessura do modelo: {F(tp)} mm.");

        void K(string cod, string desc, string valor, string lim, SituacaoVerificacao s, string refer, string dica = "") =>
            v.Add(new ResultadoVerificacao { Codigo = cod, Grupo = grupo, Descricao = desc, Valor = valor, Limite = lim, Referencia = refer, Situacao = s,
                Dica = s == SituacaoVerificacao.Ok ? "" : dica });
        SituacaoVerificacao Ok(bool b) => b ? SituacaoVerificacao.Ok : SituacaoVerificacao.NaoAtende;

        K("PB1", "Espessura da placa (charneira plástica)", $"tp = {F(tp)} mm" + (crit != null ? $" ({crit.Nome})" : ""), $"≥ tp,mín = {F(tpMin, "0.0")} mm",
          Ok(tp >= tpMin - 1e-9), "NBR 8800 6.7.2.2", "Aumente a espessura ou a placa.");
        var sig = res.Where(r => r.Tipo == "C1" || r.Tipo == "C2").OrderByDescending(r => r.SigmaSd / r.SigmaRd).FirstOrDefault();
        if (sig != null)
            K("PB2", "Pressão de contato no concreto", $"σc,Sd = {F(sig.SigmaSd, "0.00")} MPa ({sig.Nome})", $"≤ σc,Rd = {F(sig.SigmaRd, "0.00")} MPa",
              Ok(sig.SigmaSd <= sig.SigmaRd + 1e-9), "NBR 8800 6.6.5 e 6.7.1.5-d", "Aumente a placa ou o fck do bloco.");
        bool geo = res.All(r => r.Geometria);
        if (!geo) K("PB3", "Equilíbrio da grande excentricidade", "sem solução para ℓc", "(ℓx/2 + a)² ≥ 2·N·(e ± a)/(σc,Rd·ℓy)", SituacaoVerificacao.NaoAtende,
                    "NBR 8800 6.7.2.2-c/f", "Altere a ligação: placa maior ou mais chumbadores.");
        if (dbMin > 0)
            K("PB4", "Diâmetro dos chumbadores (tração)", $"db = {F(d.Db, "0")} mm", $"≥ db,mín = {F(dbMin, "0.0")} mm", Ok(d.Db >= dbMin - 1e-9), "NBR 8800 6.7.2.2",
              "Aumente o diâmetro ou o número de chumbadores.");
        var cort = res.OrderByDescending(r => r.VRd > 0 ? r.Vd / r.VRd : r.Vd > 0 ? 99 : 0).FirstOrDefault();
        if (cort != null)
        {
            bool disp = res.Any(r => r.PrecisaDispositivoCortante);
            K("PB5", "Força cortante por atrito placa–argamassa", $"Vd = {F(cort.Vd, "0.0")} kN ({cort.Nome})", $"≤ VRd = {F(cort.VRd, "0.0")} kN (μ = {F(d.Mu, "0.00")})",
              disp ? SituacaoVerificacao.Conferir : SituacaoVerificacao.Ok, "NBR 8800 6.7.2.2 e 6.7.2.3",
              "Atrito insuficiente (ou tração): use placa de cisalhamento (6.7.2.4) ou arruelas soldadas (6.7.2.5).");
        }
        K("PB6", "Chumbadores e disposições da Tabela 18", $"ø{F(d.Db, "0")} mm, {d.Nb} chumbadores, fck = {F(d.Fck, "0")} MPa",
          $"diâmetro da tabela, 4 ≤ nb ≤ 8, fck ≥ {F(fckMin, "0")} MPa",
          naTabela && d.Nb >= 4 && d.Nb <= 8 && d.Fck >= fckMin - 1e-9 ? SituacaoVerificacao.Ok : SituacaoVerificacao.NaoAtende, "NBR 8800 6.7.1.5-c e Tab. 18",
          "Use um diâmetro da Tabela 18, de 4 a 8 chumbadores e o fck mínimo do bloco.");
        if (d.FubChumbador > 400 + 1e-9)
            K("PB7", "Aço do chumbador", d.AcoChumbador, "ASTM A36 ou equivalente (fy = 250, fu = 400 MPa)", SituacaoVerificacao.Conferir, "NBR 8800 6.7.1.5-c",
              "O método de 6.7 vale para chumbadores equivalentes ao A36; aço mais resistente: confira por outro método.");
        // solda pilar–placa: mesa tracionada leva M/(d − tf) + parte de N; alma leva V
        double fwRd = ComponentesLigacao.FiletePorMm(d.Perna, d.Fw);
        double compMesa = 2 * d.Pilar.Bf - d.Pilar.Tw, compAlma = 2 * (d.Pilar.D - 2 * d.Pilar.Tf);
        double Solda((string Nome, double Nd, double Md, double Vd) c)
        {
            double ff = Math.Abs(c.Md) * 1000 / (d.Pilar.D - d.Pilar.Tf) + Math.Max(0, c.Nd) * d.Pilar.Bf * d.Pilar.Tf / (d.Pilar.A * 100);
            return Math.Max(ff / (fwRd * compMesa), Math.Abs(c.Vd) / (fwRd * compAlma));
        }
        var cs = casos.ToList();
        if (cs.Count > 0)
        {
            var pior = cs.Select(c => (Solda(c), c.Nome)).OrderByDescending(x => x.Item1).First();
            K("PB8", "Solda do pilar à placa (filetes)", $"η = {F(pior.Item1, "0.00")} ({pior.Nome})", "≤ 1,00", Ok(pior.Item1 <= 1 + 1e-9), "NBR 8800 6.2.5 (Tab. 9)",
              "Aumente a perna do filete ou use penetração total nas mesas.");
            mem.Linhas.Add($"Solda: filete de {F(d.Perna)} mm ({F(fwRd * 1000, "0")} N/mm), {F(compMesa, "0")} mm por mesa e {F(compAlma, "0")} mm na alma; " +
                           "mesa: M/(d − tf) + tração proporcional à área da mesa; alma: V. Compressão por contato (6.1.6).");
        }
        return v;
    }
}
