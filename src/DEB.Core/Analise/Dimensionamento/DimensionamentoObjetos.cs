using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Analise.Barras;
using DEB.Core.Analise.Objetos;
using DEB.Core.Configuracao.Projeto;
using DEB.Core.Documentacao;
using DEB.Core.Elementos.Consolo;
using DEB.Core.Elementos.Pilar.Calculo;
using DEB.Core.Elementos.Viga.PreMoldada.Calculo;
using DEB.Core.Normas.Verificacao;

namespace DEB.Core.Analise.Dimensionamento;

/// <summary>Resultado do dimensionamento de uma peça ou ligação a partir da análise (D46): situação, resumo, verificações e memória.</summary>
public sealed class ResultadoPeca
{
    public Dono Dono { get; set; }
    /// <summary>Chave do dono (a mesma da janela do modelo de análise).</summary>
    public string Chave { get; set; } = "";
    public List<string> Resumo { get; } = new List<string>();
    public ListaVerificacoes Verificacoes { get; } = new ListaVerificacoes();
    public PassoMemoria Memoria { get; set; } = new PassoMemoria();
    public SituacaoVerificacao Situacao => Verificacoes.Count == 0 ? SituacaoVerificacao.Conferir : Verificacoes.Geral;
}

/// <summary>Envoltória dos esforços de cálculo (ELU) numa barra: máximo e mínimo de cada esforço por estação.</summary>
public sealed class EnvoltoriaBarra
{
    public double[] X { get; set; }
    /// <summary>[estação][esforço 1..6] = N, Vy, Vz, T, My, Mz (convenção dos resultados).</summary>
    public double[][] Max { get; set; }
    public double[][] Min { get; set; }
}

/// <summary>
/// Ambiente de dimensionamento das peças (D46): cada tipo de objeto tem a sua verificação, alimentada pela envoltória ELU da
/// análise e pela configuração do projeto (D45). O que já tem cálculo no núcleo é reusado (flexão da viga, consolo); o que ainda
/// não tem fica como "Conferir", com os esforços que o cálculo vai usar — etapa 3 do pilar, laje com armadura por metro.
/// </summary>
public static class DimensionamentoObjetos
{
    private static string N(double x, string f = "0.##") => Fmt.Virgula(x, f);

    /// <summary>Envoltória ELU de uma barra.</summary>
    public static EnvoltoriaBarra Envoltoria(EstruturaObjetos e, int barra, string tipo = "ELU")
    {
        var combos = e.DefinicaoCombinacoes.Where(c => c.Tipo == tipo).Select(c => e.Estacoes(c.Nome)[barra]).ToList();
        int n = combos[0].Length;
        var env = new EnvoltoriaBarra { X = combos[0].Select(s => s[0]).ToArray(), Max = new double[n][], Min = new double[n][] };
        for (int k = 0; k < n; k++)
        {
            env.Max[k] = new double[7]; env.Min[k] = new double[7];
            for (int q = 1; q <= 6; q++) { env.Max[k][q] = combos.Max(c => c[k][q]); env.Min[k][q] = combos.Min(c => c[k][q]); }
        }
        return env;
    }

    public static List<ResultadoPeca> Verificar(EstruturaObjetos e, ConfiguracaoProjeto cfg = null)
    {
        cfg ??= new ConfiguracaoProjeto();
        var r = new List<ResultadoPeca>();
        foreach (var v in e.Vigas) r.Add(Viga(e, v, cfg));
        foreach (var p in e.Pilares) r.Add(Pilar(e, p, cfg));
        foreach (var l in e.Ligacoes) r.Add(Consolo(e, l, cfg));
        foreach (var l in e.Lajes) r.Add(Laje(e, l, cfg));
        return r;
    }

    private static string Chave(Dono d) => d.Tipo + ":" + (d.Id != "" ? d.Id : d.Nome);

    private static (double Fck, double Fcd, double Fyd, double Fyk, double Gc) Materiais(ConfiguracaoProjeto cfg)
    {
        double fck = cfg.Numero("materiais.fck"), gc = cfg.Numero("normas.gamaC"), gs = cfg.Numero("normas.gamaS");
        double fyk = TabelasNbr.Fyk(cfg.Texto("materiais.aco"));
        return (fck, fck / gc / 10, fyk / gs / 10, fyk, gc);
    }

    private static void K(ResultadoPeca r, string cod, string desc, string valor, string limite, bool falha, string refer,
                          SituacaoVerificacao seFalha = SituacaoVerificacao.NaoAtende, string dica = "") =>
        r.Verificacoes.Add(new ResultadoVerificacao
        {
            Codigo = cod, Grupo = "Análise — " + r.Dono, Descricao = desc, Valor = valor, Limite = limite, Referencia = refer,
            Situacao = falha ? seFalha : SituacaoVerificacao.Ok, Dica = falha ? dica : "",
        });

    // ------------------------------------------------------------------ viga (flexão simples, cortante, flecha)

    public static ResultadoPeca Viga(EstruturaObjetos e, VigaObjeto v, ConfiguracaoProjeto cfg)
    {
        var r = new ResultadoPeca { Dono = v.Dono, Chave = Chave(v.Dono) };
        r.Memoria.Titulo = $"Viga {v.Dono} — envoltória ELU da análise (NBR 6118)";
        var (fck, fcd, fyd, fyk, _) = Materiais(cfg);
        double b = v.Secao.B, h = v.Secao.H, cob = cfg.Numero("durab.cobViga") * 100, d = h - cob - 1.5;
        var envs = v.Barras.Select(x => Envoltoria(e, x.Id)).ToList();
        // momento positivo (traciona embaixo) = −My; negativo = +My (convenção vetorial, z local para cima)
        double mPos = Math.Max(0, envs.Max(x => x.Min.Max(s => -s[5])));
        double mNeg = Math.Max(0, envs.Max(x => x.Max.Max(s => s[5])));
        double vd = envs.Max(x => Math.Max(x.Max.Max(s => Math.Abs(s[3])), x.Min.Max(s => Math.Abs(s[3]))));
        double td = envs.Max(x => Math.Max(x.Max.Max(s => Math.Abs(s[4])), x.Min.Max(s => Math.Abs(s[4]))));
        var fl = new Flexao(fck, fcd, fyd);
        var (asPos, asCompr, _, xdPos) = fl.Armadura(mPos, b, d, cob + 1.5);
        var (asNeg, _, _, xdNeg) = fl.Armadura(mNeg, b, d, cob + 1.5);
        double asMin = TabelasNbr.RhoMinVigaPct(fck) / 100 * b * h;
        r.Memoria.Linhas.Add($"b = {N(b, "0.#")} cm, h = {N(h, "0.#")} cm, d = {N(d, "0.#")} cm; fck = {N(fck)} MPa; fyd = {N(fyd * 10, "0")} MPa.");
        r.Memoria.Linhas.Add($"Md+ = {N(mPos / 100, "0.0")} kN·m → As = {N(asPos)} cm² (x/d = {N(xdPos, "0.00")}); Md− = {N(mNeg / 100, "0.0")} kN·m → As' = {N(asNeg)} cm²; As,mín = {N(asMin)} cm².");
        K(r, "A1", "Flexão positiva (armadura inferior)", $"Md = {N(mPos / 100, "0.0")} kN·m → As = {N(Math.Max(asPos, asMin))} cm²", $"x/d ≤ {N(fl.XdLim)}",
          asCompr > 0, "NBR 6118 17.2.2 e 14.6.4.3", SituacaoVerificacao.Conferir, "Armadura dupla: aumente a altura ou confira a seção T com a capa.");
        K(r, "A2", "Flexão negativa (armadura superior)", $"Md = {N(mNeg / 100, "0.0")} kN·m → As = {N(asNeg)} cm²", $"x/d = {N(xdNeg, "0.00")}",
          xdNeg > fl.XdLim + 1e-9, "NBR 6118 17.2.2", SituacaoVerificacao.Conferir);
        double av2 = 1 - fck / 250, vrd2 = 0.27 * av2 * fcd * b * d;
        double fctd = 0.7 * TabelasNbr.Fctm(fck) / cfg.Numero("normas.gamaC") / 10, vc = 0.6 * fctd * b * d;
        double fywd = Math.Min(fyd, 43.5), aswMin = 0.2 * TabelasNbr.Fctm(fck) / fyk * b;
        double asw = Math.Max(aswMin, (vd - vc) / (0.9 * d * fywd));
        r.Memoria.Linhas.Add($"Vd = {N(vd, "0.0")} kN; VRd2 = {N(vrd2, "0.0")} kN; Vc = {N(vc, "0.0")} kN → Asw/s = {N(asw * 100)} cm²/m (mínimo {N(aswMin * 100)}).");
        K(r, "A3", "Bielas comprimidas (VRd2)", $"Vd = {N(vd, "0.0")} kN", $"≤ {N(vrd2, "0.0")} kN", vd > vrd2, "NBR 6118 17.4.2.2", dica: "Aumente a largura ou a altura.");
        K(r, "A4", "Estribos (Asw/s)", $"{N(asw * 100)} cm²/m", $"mínimo {N(aswMin * 100)} cm²/m", false, "NBR 6118 17.4.2.2 e 17.4.1.1.1");
        if (td > 0.01 * Math.Max(1, mPos))
            K(r, "A5", "Torção na envoltória", $"Td = {N(td / 100, "0.00")} kN·m", "conferir (17.5)", true, "NBR 6118 17.5", SituacaoVerificacao.Conferir,
              "Viga com torção de equilíbrio: use o cálculo da torção da viga (TorcaoViga).");
        // flecha: ELS quase permanente, elástica × 2,5 (fissuração + fluência; estimativa) contra L/250
        var qp = e.DefinicaoCombinacoes.FirstOrDefault(c => c.Tipo == "ELS" && c.Nome.Contains("q. perm"));
        if (qp != null)
        {
            var est = v.Barras.SelectMany(x => e.Estacoes(qp.Nome)[x.Id]).ToList();
            double zi = est.First()[9], zj = est.Last()[9];
            double flecha = est.Select((s, k) => -(s[9] - (zi + (zj - zi) * k / Math.Max(1, est.Count - 1)))).Max();
            double lim = v.Vao / 250, est25 = 2.5 * flecha;
            r.Memoria.Linhas.Add($"Flecha elástica ({qp.Nome}) = {N(flecha * 10, "0.0")} mm; estimada com fissuração e fluência (×2,5) = {N(est25 * 10, "0.0")} mm; L/250 = {N(lim * 10, "0.0")} mm.");
            K(r, "A6", "Flecha (estimativa ×2,5)", $"{N(est25 * 10, "0.0")} mm", $"≤ L/250 = {N(lim * 10, "0.0")} mm", est25 > lim, "NBR 6118 13.3 e 17.3.2",
              SituacaoVerificacao.Conferir, "Confira a flecha com a inércia fissurada (Branson) e a contraflecha.");
        }
        r.Resumo.Add($"As inf. {N(Math.Max(asPos, asMin))} cm² · As sup. {N(Math.Max(asNeg, 0))} cm² · Asw/s {N(asw * 100)} cm²/m");
        return r;
    }

    // ------------------------------------------------------------------ pilar (etapa 3: flexo-compressão oblíqua com 2ª ordem local)

    /// <summary>
    /// Pilar (D44 etapa 3): para cada combinação ELU e cada seção das pontas dos trechos, Nd e os momentos da análise (com o mínimo
    /// de 15.3.3.2, um caso por direção) e, na base, a 2ª ordem local pelo pilar-padrão com rigidez κ (15.8.3.3.3) quando
    /// λ1 &lt; λ ≤ 90; flexo-compressão oblíqua com a armadura do detalhamento — ou, sem ela, a menor armadura simétrica que passa
    /// (4 a 12 barras, ø10 a ø32). Taxas de 17.3.5.3.
    /// </summary>
    public static ResultadoPeca Pilar(EstruturaObjetos e, PilarObjeto p, ConfiguracaoProjeto cfg)
    {
        var r = new ResultadoPeca { Dono = p.Dono, Chave = Chave(p.Dono) };
        r.Memoria.Titulo = $"Pilar {p.Dono} — NBR 6118:2023, 15 e 17.2 (etapa 3)";
        var (fck, fcd, fyd, fyk, gc) = Materiais(cfg);
        double gs = cfg.Numero("normas.gamaS");
        double hy = p.Secao.B, hz = p.Secao.H, ac = p.Secao.Area, cob = cfg.Numero("durab.cobPilar") * 100;
        bool monolitico = e.LigacoesDiretas.Any(l => l.Pilar == p && !l.Articulada);
        double comp = p.Comprimento - p.Embutimento, le = (monolitico ? 1.0 : 2.0) * comp;
        double lamZ = le * Math.Sqrt(12) / hz, lamY = le * Math.Sqrt(12) / hy;   // λ para flexão em z (My) e em y (Mz)
        double zBase = p.Nos[0].Z;

        // casos: (nome, Nd, My, Mz) na convenção da seção (compressão positiva; My comprime +z, Mz comprime +y)
        var casos = new List<(string Nome, double Nd, double My, double Mz)>();
        double ndMax = 0, myMax = 0, mzMax = 0;
        foreach (var c in e.DefinicaoCombinacoes.Where(c => c.Tipo == "ELU"))
        {
            var est = e.Estacoes(c.Nome);
            foreach (var b in p.Barras)
            {
                var s = est[b.Id];
                var pontas = new[] { (s[0], p.Nos.First(n => n.No == b.NoI).Z), (s[s.Length - 1], p.Nos.First(n => n.No == b.NoJ).Z) };
                foreach (var (sx, onde) in pontas)
                {
                    double nd = Math.Max(0, -sx[1]), my = -sx[5], mz = sx[6];
                    ndMax = Math.Max(ndMax, nd); myMax = Math.Max(myMax, Math.Abs(my)); mzMax = Math.Max(mzMax, Math.Abs(mz));
                    double mMinY = nd * (1.5 + 0.03 * hz), mMinZ = nd * (1.5 + 0.03 * hy);
                    double myT = my, mzT = mz;
                    if (Math.Abs(onde - zBase) < 1e-6 && nd > 0)
                    {
                        double m1y = Math.Max(Math.Abs(my), mMinY), m1z = Math.Max(Math.Abs(mz), mMinZ);
                        if (lamZ > FlexoCompressaoObliqua.Lambda1(m1y / nd, hz) && lamZ <= 90)
                            myT = Sinal(my) * FlexoCompressaoObliqua.MomentoTotalKappa(nd, m1y, hz, le);
                        if (lamY > FlexoCompressaoObliqua.Lambda1(m1z / nd, hy) && lamY <= 90)
                            mzT = Sinal(mz) * FlexoCompressaoObliqua.MomentoTotalKappa(nd, m1z, hy, le);
                    }
                    string nome = $"{c.Nome}, z = {Fmt.Virgula(onde, "0")} cm";
                    casos.Add((nome + ", mín. em z", nd, Sinal(myT) * Math.Max(Math.Abs(myT), mMinY), mzT));
                    casos.Add((nome + ", mín. em y", nd, myT, Sinal(mzT) * Math.Max(Math.Abs(mzT), mMinZ)));
                }
            }
        }
        double nu = ndMax / (ac * fcd);
        r.Memoria.Linhas.Add($"Seção {N(hy, "0.#")} × {N(hz, "0.#")} cm; fck = {N(fck)} MPa; Nd,máx = {N(ndMax, "0.0")} kN (ν = {N(nu, "0.00")}); " +
                             $"My,d = {N(myMax / 100, "0.0")} kN·m, Mz,d = {N(mzMax / 100, "0.0")} kN·m (1ª ordem, envoltória ELU).");
        r.Memoria.Linhas.Add($"le = {(monolitico ? "l" : "2·l (balanço engastado no cálice)")} = {N(le, "0")} cm → λ = {N(lamZ, "0.0")} (flexão em z) e {N(lamY, "0.0")} (em y). " +
                             "2ª ordem local na base pelo pilar-padrão com rigidez κ aproximada (15.8.3.3.3), αb = 1, quando λ1 < λ ≤ 90; momento mínimo de 15.3.3.2 em cada direção.");
        double lam = Math.Max(lamY, lamZ);
        K(r, "P1", "Força normal reduzida", $"ν = {N(nu, "0.00")}", "≤ 1,0", nu > 1.0, "NBR 6118 17.2", dica: "Aumente a seção ou o fck.");
        K(r, "P2", "Esbeltez", $"λ = {N(lam, "0.0")}", "≤ 90 (pilar-padrão)", lam > 90, "NBR 6118 15.8.3",
          lam > 200 ? SituacaoVerificacao.NaoAtende : SituacaoVerificacao.Conferir, "λ > 90: pilar-padrão acoplado a M–N–1/r (até 140) ou método geral.");
        if (casos.Count == 0) { r.Resumo.Add("sem esforços de cálculo"); return r; }

        SecaoArmada Nova() => new SecaoArmada { Concreto = p.Secao, FckMPa = fck, FykMPa = fyk, GamaC = gc, GamaS = gs };
        // casos mais exigentes primeiro (maior excentricidade relativa): a proposta para no primeiro que falha
        casos = casos.OrderByDescending(cs => (Math.Abs(cs.My) / hz + Math.Abs(cs.Mz) / hy) / Math.Max(1, cs.Nd) + cs.Nd / (ac * fcd) * 0.01).ToList();
        double Pior(SecaoArmada sa, out string qual, double parar = double.PositiveInfinity)
        {
            double pior = 0; qual = "";
            var curvas = new Dictionary<double, List<(double My, double Mz)>>();
            foreach (var cs in casos)
            {
                double nk = Math.Round(cs.Nd, 1);
                if (!curvas.TryGetValue(nk, out var cv)) curvas[nk] = cv = FlexoCompressaoObliqua.Curva(sa, cs.Nd, 72);
                double eta = FlexoCompressaoObliqua.Aproveitamento(cv, cs.Nd, cs.My, cs.Mz);
                if (eta > pior) { pior = eta; qual = cs.Nome; }
                if (pior > parar) break;
            }
            return pior;
        }
        double asMin = Math.Max(0.15 * ndMax / fyd, 0.004 * ac), asMax = 0.04 * ac;
        SecaoArmada secao = Nova();
        string arranjo = "";
        if (p.Armadura.Count > 0)
        {
            foreach (var bb in p.Armadura) secao.Barras.Add(bb);
            arranjo = string.Join(" + ", p.Armadura.GroupBy(x => x.Phi).OrderByDescending(g => g.Key).Select(g => $"{g.Count()} ø{N(g.Key, "0.#")}")) + " (detalhamento)";
        }
        else
        {
            var cand = new List<(int N, double Phi)>();
            foreach (double phi in new[] { 10, 12.5, 16, 20, 25, 32 }) foreach (int n in new[] { 4, 6, 8, 10, 12 }) cand.Add((n, phi));
            foreach (var (n, phi) in cand.OrderBy(c => c.N * c.Phi * c.Phi))
            {
                if (n * Math.PI * phi * phi / 400 < asMin) continue;
                var tent = Nova();
                foreach (var bb in BarrasNoContorno(p.Secao, n, phi, cob + 0.63 + phi / 20)) tent.Barras.Add(bb);
                if (Pior(tent, out _, 1.0) <= 1.0) { secao = tent; arranjo = $"{n} ø{N(phi, "0.#")} (proposta)"; break; }
            }
            if (arranjo == "") { foreach (var bb in BarrasNoContorno(p.Secao, 12, 32, cob + 0.63 + 1.6)) secao.Barras.Add(bb); arranjo = "12 ø32 (máximo da proposta)"; }
        }
        double eta = Pior(secao, out string critico), asTem = secao.As;
        r.Memoria.Linhas.Add($"Armadura: {arranjo}, As = {N(asTem)} cm² (mín. {N(asMin)} = máx(0,15·Nd/fyd; 0,4 %·Ac); máx. {N(asMax)} = 4 %·Ac fora das emendas).");
        r.Memoria.Linhas.Add($"Flexo-compressão oblíqua (parábola-retângulo; curva de interação a Nd): maior aproveitamento η = {N(eta, "0.00")} em {critico}.");
        K(r, "P3", "Flexo-compressão oblíqua", $"η = {N(eta, "0.00")} ({critico})", "≤ 1,00", eta > 1.0, "NBR 6118 17.2.5 e 15.8.3",
          dica: "Aumente a armadura ou a seção.");
        K(r, "P4", "Taxa de armadura longitudinal", $"As = {N(asTem)} cm²", $"{N(asMin)} a {N(asMax)} cm²", asTem < asMin - 1e-9 || asTem > asMax + 1e-9, "NBR 6118 17.3.5.3");
        r.Resumo.Add($"{arranjo} · η = {N(eta, "0.00")} · Nd {N(ndMax, "0")} kN · λ {N(lam, "0")}");
        return r;
    }

    private static double Sinal(double x) => x < 0 ? -1 : 1;

    /// <summary>n barras de diâmetro φ no contorno de uma seção retangular (cantos + faces), com o centro a "afast" cm das faces.</summary>
    public static List<(double Y, double Z, double Phi)> BarrasNoContorno(Pecas.SecaoPoligonal s, int n, double phi, double afast)
    {
        double y0 = s.YMin + afast, y1 = s.YMax - afast, z0 = s.ZMin + afast, z1 = s.ZMax - afast;
        var l = new List<(double, double, double)> { (y0, z0, phi), (y1, z0, phi), (y1, z1, phi), (y0, z1, phi) };
        // as barras que sobram vão aos pares, alternando as faces mais largas e as mais estreitas (simetria)
        bool largasY = (y1 - y0) >= (z1 - z0);
        int pares = (n - 4) / 2, nLarga = (pares + 1) / 2, nEstreita = pares / 2;
        void Face(int q, bool emY)
        {
            for (int i = 1; i <= q; i++)
            {
                double t = (double)i / (q + 1);
                if (emY) { l.Add((y0 + (y1 - y0) * t, z0, phi)); l.Add((y0 + (y1 - y0) * t, z1, phi)); }
                else { l.Add((y0, z0 + (z1 - z0) * t, phi)); l.Add((y1, z0 + (z1 - z0) * t, phi)); }
            }
        }
        Face(nLarga, largasY); Face(nEstreita, !largasY);
        return l;
    }

    // ------------------------------------------------------------------ consolo (cálculo do núcleo, com a carga da análise)

    public static ResultadoPeca Consolo(EstruturaObjetos e, LigacaoConsolo l, ConfiguracaoProjeto cfg)
    {
        var r = new ResultadoPeca { Dono = l.Dono, Chave = Chave(l.Dono) };
        var carga = e.CargaDoConsolo(l);
        var elu = e.DefinicaoCombinacoes.Where(c => c.Tipo == "ELU").Select(c => e.Esforco(l, e.Combinacoes[c.Nome])).ToList();
        double vmax = elu.Max(x => x.V), hmax = elu.Max(x => Math.Abs(x.H));
        r.Resumo.Add($"Vd {N(vmax, "0.0")} kN · Hd {N(hmax, "0.00")} kN (análise) · {l.Observacao}");
        if (l.Consolo == null)
        {
            r.Memoria.Titulo = $"Consolo {l.Dono}";
            r.Memoria.Linhas.Add($"Vg = {N(carga.Vg, "0.0")} kN, Vq = {N(carga.Vq, "0.0")} kN, Hg = {N(carga.Hg, "0.00")} kN, Hq = {N(carga.Hq, "0.00")} kN (característicos, da análise).");
            K(r, "CC0", "Armadura do consolo", "não informada", "do detalhamento do pilar", true, "NBR 9062 7.3", SituacaoVerificacao.Conferir,
              "O cálculo do consolo sai na janela dos pilares, com a armadura desenhada.");
            return r;
        }
        var c = CalculoConsolo.Calcular(l.Consolo, carga, cfg);
        foreach (var x in c.Verificacoes) r.Verificacoes.Add(x);
        r.Memoria = c.Memoria;
        r.Resumo.Add(c.Classe);
        return r;
    }

    // ------------------------------------------------------------------ laje (armadura por metro, flecha)

    public static ResultadoPeca Laje(EstruturaObjetos e, LajeObjeto la, ConfiguracaoProjeto cfg)
    {
        var r = new ResultadoPeca { Dono = la.Dono, Chave = Chave(la.Dono) };
        r.Memoria.Titulo = $"Laje {la.Dono} — grelha, envoltória ELU (NBR 6118 19)";
        var (fck, fcd, fyd, _, _) = Materiais(cfg);
        double h = la.Espessura, cob = cfg.Numero("durab.cobLaje") * 100, d = h - cob - 0.5;
        if (la.Unidirecional)
        {
            // produto de catálogo (sem verificação, pedido de 24/11): momento e cortante por metro no vão para escolher no fabricante
            double mMax = 0, mNeg = 0, vMax = 0;
            foreach (var c in e.DefinicaoCombinacoes.Where(c => c.Tipo == "ELU"))
            {
                var res = e.Combinacoes[c.Nome];
                // faixas sobre viga ou borda apoiada (as duas pontas presas) acompanham a viga: não são vão de laje
                foreach (var b in la.BarrasX.Where(b => !(la.Apoiado(b.NoI) && la.Apoiado(b.NoJ))))
                {
                    double[] f = res.ForcasBarras[b.Id], larg = { b.Secao.A / la.Espessura };
                    mMax = Math.Max(mMax, Math.Max(f[4], -f[10]) / larg[0]); mNeg = Math.Max(mNeg, Math.Max(-f[4], f[10]) / larg[0]);
                    vMax = Math.Max(vMax, Math.Max(Math.Abs(f[2]), Math.Abs(f[8])) / larg[0] * 100);
                }
            }
            string tipo = la.Tipo == TipoLaje.Alveolar ? "alveolar" : la.Tipo == TipoLaje.Trelicada ? "treliçada" : "de vigota protendida";
            r.Memoria.Titulo = $"Laje {tipo} {la.Dono} — produto de catálogo";
            r.Memoria.Linhas.Add($"Laje {tipo}, h = {N(h, "0.#")} cm, vão na direção x da malha; peso próprio pela fração maciça {N(la.FracaoMacica)}.");
            r.Memoria.Linhas.Add($"Para escolher no catálogo do fabricante (ELU, por metro de largura): Md+ = {N(mMax)} kN·m/m, Md− = {N(mNeg)} kN·m/m, Vd = {N(vMax, "0.0")} kN/m.");
            K(r, "L0", $"Laje {tipo} (produto de catálogo)", $"Md = {N(mMax)} kN·m/m; Vd = {N(vMax, "0.0")} kN/m", "catálogo do fabricante", true, "NBR 9062 · NBR 14859 / 14861",
              SituacaoVerificacao.Conferir, "O PCEB não calcula a laje pré-fabricada: confira no catálogo com estes esforços.");
            r.Resumo.Add($"{tipo}: Md {N(mMax)} kN·m/m · Vd {N(vMax, "0.0")} kN/m (catálogo)");
            return r;
        }
        double mxPos = 0, mxNeg = 0, myPos = 0, myNeg = 0;
        foreach (var c in e.DefinicaoCombinacoes.Where(c => c.Tipo == "ELU"))
        {
            var (_, mx, my, _) = la.Resultados(e.Combinacoes[c.Nome]);
            foreach (double v in mx) if (!double.IsNaN(v)) { mxPos = Math.Max(mxPos, v); mxNeg = Math.Max(mxNeg, -v); }
            foreach (double v in my) if (!double.IsNaN(v)) { myPos = Math.Max(myPos, v); myNeg = Math.Max(myNeg, -v); }
        }
        var fl = new Flexao(fck, fcd, fyd);
        double As(double mKNm) => fl.Armadura(mKNm * 100, 100, d, cob).As;   // por metro
        double asMin = TabelasNbr.RhoMinVigaPct(fck) / 100 * 100 * h * 0.67;
        r.Memoria.Linhas.Add($"h = {N(h, "0.#")} cm, d = {N(d, "0.#")} cm; malha de {N(la.Sx, "0")} × {N(la.Sy, "0")} cm em média (passando pelas vigas e pilares).");
        r.Memoria.Linhas.Add($"mx: +{N(mxPos)} / −{N(mxNeg)} kN·m/m → As = {N(As(mxPos))} / {N(As(mxNeg))} cm²/m; my: +{N(myPos)} / −{N(myNeg)} kN·m/m → As = {N(As(myPos))} / {N(As(myNeg))} cm²/m; mínimo {N(asMin)} cm²/m (0,67·ρmín, 19.3.3.2).");
        double mmax = new[] { mxPos, mxNeg, myPos, myNeg }.Max();
        double xd = fl.Armadura(mmax * 100, 100, d, cob).XsobreD;
        K(r, "L1", "Flexão (maior momento)", $"m = {N(mmax)} kN·m/m", $"x/d = {N(xd, "0.00")} ≤ {N(fl.XdLim)}", xd > fl.XdLim + 1e-9, "NBR 6118 17.2.2 e 19", dica: "Aumente a espessura.");
        var qp = e.DefinicaoCombinacoes.FirstOrDefault(c => c.Tipo == "ELS" && c.Nome.Contains("q. perm"));
        if (qp != null)
        {
            var (uz, _, _, _) = la.Resultados(e.Combinacoes[qp.Nome]);
            double wmax = 0; foreach (double v in uz) if (!double.IsNaN(v)) wmax = Math.Max(wmax, -v);
            double lmenor = Math.Min(la.Xs[la.Nx], la.Ys[la.Ny]), lim = lmenor / 250, w25 = 2.5 * wmax;
            r.Memoria.Linhas.Add($"Flecha elástica ({qp.Nome}) = {N(wmax * 10, "0.0")} mm (com a das vigas); estimada ×2,5 = {N(w25 * 10, "0.0")} mm; L/250 = {N(lim * 10, "0.0")} mm.");
            K(r, "L2", "Flecha (estimativa ×2,5)", $"{N(w25 * 10, "0.0")} mm", $"≤ {N(lim * 10, "0.0")} mm", w25 > lim, "NBR 6118 13.3", SituacaoVerificacao.Conferir);
        }
        r.Resumo.Add($"As x {N(Math.Max(As(mxPos), asMin))} / {N(As(mxNeg))} · As y {N(Math.Max(As(myPos), asMin))} / {N(As(myNeg))} cm²/m (inf./sup.)");
        return r;
    }
}
