using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Analise.Barras;
using DEB.Core.Motor;
using DEB.Core.Pecas;

namespace DEB.Core.Elementos.Viga.PreMoldada.Calculo;

// =====================================================================================================================
// Análise da viga pré-moldada no CAD (25/11/2026): modelo de barras da peça (seções do dente, do vão e da aba, as mesmas do
// detalhamento), cargas por caso (PP, CP, SC) da viga e da laje, cargas pontuais e DOIS modelos de ligação — rotulado e
// semirrígido (fator de restrição αR, NBR 9062) — para a envoltória conservadora. Resolve com o SolverBarras do núcleo
// (dentro do programa; a conferência externa é feita com PyNite/OpenSees). Unidades internas: cm, kN.
// =====================================================================================================================

/// <summary>Casos de carga da viga: peso próprio, permanente e sobrecarga (variável).</summary>
public enum CasoViga { PP, CP, SC }

/// <summary>Carga de laje por área sobre a viga: valor (kN/m²) × vão de influência (m, o vão inteiro) = carga linear (kN/m).</summary>
public sealed class CargaLaje
{
    public double Valor { get; set; }
    public double VaoInfluencia { get; set; }
    public double Linear => Valor * VaoInfluencia;
}

/// <summary>Entrada da análise: o que o engenheiro digita (valores característicos, kN e m).</summary>
public sealed class EntradaAnaliseViga
{
    public CargaLaje LajePP { get; set; } = new CargaLaje();
    public CargaLaje LajeCP { get; set; } = new CargaLaje();
    public CargaLaje LajeSC { get; set; } = new CargaLaje();
    /// <summary>Permanente e sobrecarga da própria viga (kN/m); o peso próprio é calculado.</summary>
    public double VigaCP { get; set; }
    public double VigaSC { get; set; }
    /// <summary>Cargas pontuais (x em cm da ponta esquerda): G vai para o caso CP, Q para o SC.</summary>
    public List<CargaConcentrada> Pontuais { get; set; } = new List<CargaConcentrada>();
    /// <summary>Distância da ponta ao eixo do apoio (cm); 0 = automática (furo do pino, meio do dente ou 10 cm).</summary>
    public double ApoioE { get; set; }
    public double ApoioD { get; set; }
    /// <summary>Modelo rotulado (αR = 0) na envoltória.</summary>
    public bool Rotulado { get; set; } = true;
    /// <summary>Modelo semirrígido na envoltória, com o fator de restrição à rotação de cada ponta (0 &lt; αR ≤ 1; 1 = engaste).</summary>
    public bool Semirrigido { get; set; } = true;
    public double AlfaRE { get; set; } = 0.10;
    public double AlfaRD { get; set; } = 0.10;
    /// <summary>
    /// Torção de equilíbrio pela laje apoiada na aba (NBR 6118, 17.5.1): ligada, a carga da laje vai para a aba (fora do eixo da
    /// alma) no dimensionamento; desligada, entra no eixo. Não muda a flexão da análise.
    /// </summary>
    public bool Torcao { get; set; } = true;
}

/// <summary>Ponto dos diagramas: x (cm), momento fletor (kN·m, positivo = tração embaixo), cortante (kN, forças à esquerda, para
/// cima +) e deslocamento vertical (cm, para baixo +).</summary>
public readonly struct PontoDiagrama
{
    public double X { get; }
    public double M { get; }
    public double V { get; }
    public double Flecha { get; }
    public PontoDiagrama(double x, double m, double v, double f) { X = x; M = m; V = v; Flecha = f; }
}

/// <summary>Um modelo de ligação (rotulado ou semirrígido) resolvido: diagramas por caso e das combinações.</summary>
public sealed class ModeloAnaliseViga
{
    public string Nome { get; set; } = "";
    public double AlfaRE { get; set; }
    public double AlfaRD { get; set; }
    /// <summary>Rigidez à rotação das ligações (kN·m/rad); 0 = rótula, infinito = engaste.</summary>
    public double RsecE { get; set; }
    public double RsecD { get; set; }
    public Dictionary<CasoViga, List<PontoDiagrama>> Casos { get; } = new Dictionary<CasoViga, List<PontoDiagrama>>();
    /// <summary>Reações verticais características por caso (kN): esquerda e direita.</summary>
    public Dictionary<CasoViga, (double E, double D)> Reacoes { get; } = new Dictionary<CasoViga, (double, double)>();
    /// <summary>ELU, combinação normal: γg·(PP + CP) + γq·SC.</summary>
    public List<PontoDiagrama> Elu { get; } = new List<PontoDiagrama>();
    /// <summary>ELS quase permanente: PP + CP + ψ2·SC (flecha elástica imediata, seção bruta).</summary>
    public List<PontoDiagrama> Els { get; } = new List<PontoDiagrama>();
}

/// <summary>Envoltória dos modelos no ELU: em cada x, o maior e o menor momento e cortante.</summary>
public readonly struct PontoEnvoltoria
{
    public double X { get; }
    public double MMax { get; }
    public double MMin { get; }
    public double VMax { get; }
    public double VMin { get; }
    public PontoEnvoltoria(double x, double mMax, double mMin, double vMax, double vMin) { X = x; MMax = mMax; MMin = mMin; VMax = vMax; VMin = vMin; }
}

public sealed class ResultadoAnaliseViga
{
    public double L { get; set; }
    public double ApoioE { get; set; }
    public double ApoioD { get; set; }
    public double Vao => L - ApoioE - ApoioD;
    /// <summary>Peso próprio médio da peça (kN/m), calculado.</summary>
    public double PPViga { get; set; }
    /// <summary>Carga linear total por caso (kN/m): viga + laje.</summary>
    public Dictionary<CasoViga, double> Linear { get; } = new Dictionary<CasoViga, double>();
    /// <summary>Trechos de seção do modelo: x inicial, x final e a descrição (dente, vão, com aba…).</summary>
    public List<(double X0, double X1, string Secao, double A, double I)> Trechos { get; } = new List<(double, double, string, double, double)>();
    public double E { get; set; }
    public List<ModeloAnaliseViga> Modelos { get; } = new List<ModeloAnaliseViga>();
    public List<PontoEnvoltoria> Envoltoria { get; } = new List<PontoEnvoltoria>();
    public List<string> Avisos { get; } = new List<string>();

    public double MdMax => Envoltoria.Count == 0 ? 0 : Envoltoria.Max(p => p.MMax);
    public double MdMin => Envoltoria.Count == 0 ? 0 : Envoltoria.Min(p => p.MMin);
    public double VdMax => Envoltoria.Count == 0 ? 0 : Envoltoria.Max(p => Math.Max(Math.Abs(p.VMax), Math.Abs(p.VMin)));
}

public static class AnaliseViga
{
    /// <summary>
    /// Rigidez à rotação da ligação (kN·cm/rad) pelo fator de restrição αR da NBR 9062 (5.1.2.3):
    /// αR = [1 + 3·(EI)sec/(Rsec·Lef)]⁻¹ ⇒ Rsec = 3·(EI)sec/(Lef·(1/αR − 1)). αR = 0: rótula (0); αR = 1: engaste (infinito).
    /// </summary>
    public static double RsecPorAlfaR(double alfaR, double EI, double lef)
    {
        if (alfaR <= 0) return 0;
        if (alfaR >= 1) return double.PositiveInfinity;
        return 3 * EI / (lef * (1 / alfaR - 1));
    }

    /// <summary>Área (cm²), CG (cm, a partir do fundo da seção) e inércia (cm⁴) da seção da viga em x: alma (do dente ou cheia) + aba.</summary>
    public static (double A, double Zg, double I, string Nome) Secao(double x, double b, double h, double L, ExtremidadeViga e, ExtremidadeViga d, AbaViga aba)
    {
        bool denteE = e.TemDente && x < e.CompDente - 1e-6, denteD = d.TemDente && x > L - d.CompDente + 1e-6;
        double recorte = denteE ? e.Recorte(h) : denteD ? d.Recorte(h) : 0;
        var partes = new List<SecaoPoligonal> { SecaoPoligonal.Retangulo(b, h - recorte, 0, recorte) };
        string nome = denteE || denteD ? $"dente {b:0.#}×{h - recorte:0.#}" : $"{b:0.#}×{h:0.#}";
        if (aba.Em(x))
        {
            // aba inclinada: topo horizontal, face externa com AltPonta, fundo descendo até a alma (Base)
            double z0 = aba.Base, z1 = aba.Base + aba.Altura, zp = z1 - aba.AltPonta;
            if (z1 > recorte + 1e-6)
            {
                double zc0 = Math.Max(z0, recorte), zcp = Math.Max(zp, recorte);
                if (aba.Esquerda) partes.Add(new SecaoPoligonal { Contorno = { (0, zc0), (0, z1), (-aba.Largura, z1), (-aba.Largura, zcp) } });
                if (aba.Direita) partes.Add(new SecaoPoligonal { Contorno = { (b, zc0), (b + aba.Largura, zcp), (b + aba.Largura, z1), (b, z1) } });
                nome += aba.Lados == 2 ? " + 2 abas" : " + aba";
            }
        }
        double A = partes.Sum(p => p.Area), zg = partes.Sum(p => p.Area * p.CG.Z) / A;
        double I = partes.Sum(p => p.Iy + p.Area * Math.Pow(p.CG.Z - zg, 2));
        return (A, zg, I, nome);
    }

    public static ResultadoAnaliseViga Rodar(ValoresTemplate v, EntradaAnaliseViga ent, CoeficientesViga k = null, IReadOnlyList<TipoDente> catalogo = null)
    {
        k ??= new CoeficientesViga();
        ent ??= new EntradaAnaliseViga();
        catalogo ??= TemplateVigaPreMoldada.CatalogoDentes;
        var avisos = new List<string>();
        double b = v.Numero("B"), h = v.Numero("H"), L = v.Numero("L");
        var e = TemplateVigaPreMoldada.LerExtremidade(v, true, catalogo, avisos);
        var d = TemplateVigaPreMoldada.LerExtremidade(v, false, catalogo, avisos);
        var aba = TemplateVigaPreMoldada.LerAba(v, h, L, e, d, avisos);
        var ress = TemplateVigaPreMoldada.LerRessalto(v, b, h, avisos);
        double gc = v.Numero("PesoEspecifico") > 0 ? v.Numero("PesoEspecifico") / 100 : 25;   // kN/m³
        var mat = MaterialBarra.Concreto(v.Numero("Fck"), k.AlfaE, gc);

        var r = new ResultadoAnaliseViga { L = L, E = mat.E };
        r.ApoioE = ent.ApoioE > 0 ? ent.ApoioE : CalculoViga.ApoioAutomatico(e);
        r.ApoioD = ent.ApoioD > 0 ? ent.ApoioD : CalculoViga.ApoioAutomatico(d);
        double xa = r.ApoioE, xb = L - r.ApoioD;
        if (xb - xa < 1) throw new ArgumentException("Vão de cálculo nulo: confira os apoios.");
        double vol = TemplateVigaPreMoldada.Volume(b, h, L, e, d) + aba.VolumeCm3 / 1e6 + ress.VolumeCm3(L) / 1e6;   // m³
        r.PPViga = vol * gc / (L / 100);
        if (ress.VolumeCm3(L) > 0) r.Avisos.Add("Ressalto: entra no peso próprio, não na rigidez do modelo.");
        r.Linear[CasoViga.PP] = r.PPViga + ent.LajePP.Linear;
        r.Linear[CasoViga.CP] = ent.VigaCP + ent.LajeCP.Linear;
        r.Linear[CasoViga.SC] = ent.VigaSC + ent.LajeSC.Linear;
        foreach (var p in ent.Pontuais.Where(p => p.X < 0 || p.X > L))
            r.Avisos.Add($"Carga pontual em x = {Fmt.Virgula(p.X, "0.#")} cm fica fora da viga: ignorada.");
        var pontuais = ent.Pontuais.Where(p => p.X >= 0 && p.X <= L).ToList();

        // ---- estações: pontas, apoios, mudanças de seção, cargas pontuais, mais a subdivisão (no máximo L/40 por barra)
        var xs = new List<double> { 0, L, xa, xb };
        if (e.TemDente) xs.Add(e.CompDente);
        if (d.TemDente) xs.Add(L - d.CompDente);
        if (aba.Existe) { xs.Add(aba.Ini); xs.Add(aba.Fim); }
        xs.AddRange(pontuais.Select(p => p.X));
        xs = xs.Where(x => x >= -1e-6 && x <= L + 1e-6).Select(x => Math.Round(x, 3)).Distinct().OrderBy(x => x).ToList();
        double passo = L / 40;
        var est = new List<double>();
        for (int i = 0; i < xs.Count - 1; i++)
        {
            int n = Math.Max(1, (int)Math.Ceiling((xs[i + 1] - xs[i]) / passo - 1e-9));
            for (int j = 0; j < n; j++) est.Add(xs[i] + (xs[i + 1] - xs[i]) * j / n);
        }
        est.Add(L);
        est = est.Where((x, i) => i == 0 || x - est[i - 1] > 1e-3).ToList();

        // trechos de seção (para o relatório e o desenho do modelo)
        for (int i = 0; i < xs.Count - 1; i++)
        {
            var s = Secao((xs[i] + xs[i + 1]) / 2, b, h, L, e, d, aba);
            if (r.Trechos.Count > 0 && r.Trechos[r.Trechos.Count - 1].Secao == s.Nome)
            { var t = r.Trechos[r.Trechos.Count - 1]; r.Trechos[r.Trechos.Count - 1] = (t.X0, xs[i + 1], t.Secao, t.A, t.I); }
            else r.Trechos.Add((xs[i], xs[i + 1], s.Nome, s.A, s.I));
        }
        double EIvao = mat.E * Secao((xa + xb) / 2, b, h, L, e, d, aba).I;

        // ---- modelos de ligação
        var modelos = new List<ModeloAnaliseViga>();
        if (ent.Rotulado || !ent.Semirrigido) modelos.Add(new ModeloAnaliseViga { Nome = "Rotulado" });
        if (ent.Semirrigido && (ent.AlfaRE > 0 || ent.AlfaRD > 0))
            modelos.Add(new ModeloAnaliseViga
            {
                Nome = Math.Abs(ent.AlfaRE - ent.AlfaRD) < 1e-9 ? $"Semirrígido (αR = {Fmt.Virgula(ent.AlfaRE, "0.##")})"
                                                                : $"Semirrígido (αR = {Fmt.Virgula(ent.AlfaRE, "0.##")} / {Fmt.Virgula(ent.AlfaRD, "0.##")})",
                AlfaRE = Math.Min(1, ent.AlfaRE), AlfaRD = Math.Min(1, ent.AlfaRD),
            });
        foreach (var mod in modelos)
        {
            double kE = RsecPorAlfaR(mod.AlfaRE, EIvao, xb - xa), kD = RsecPorAlfaR(mod.AlfaRD, EIvao, xb - xa);
            mod.RsecE = kE / 100; mod.RsecD = kD / 100;   // kN·cm/rad → kN·m/rad
            Resolver(mod, est, xa, xb, kE, kD, mat, r, pontuais, (x) => Secao(x, b, h, L, e, d, aba));
            for (int i = 0; i < mod.Casos[CasoViga.PP].Count; i++)
            {
                PontoDiagrama P(CasoViga c) => mod.Casos[c][i];
                var pp = P(CasoViga.PP); var cp = P(CasoViga.CP); var sc = P(CasoViga.SC);
                mod.Elu.Add(new PontoDiagrama(pp.X, k.GamaG * (pp.M + cp.M) + k.GamaQ * sc.M, k.GamaG * (pp.V + cp.V) + k.GamaQ * sc.V,
                                              k.GamaG * (pp.Flecha + cp.Flecha) + k.GamaQ * sc.Flecha));
                mod.Els.Add(new PontoDiagrama(pp.X, pp.M + cp.M + k.Psi2 * sc.M, pp.V + cp.V + k.Psi2 * sc.V, pp.Flecha + cp.Flecha + k.Psi2 * sc.Flecha));
            }
            r.Modelos.Add(mod);
        }
        for (int i = 0; i < r.Modelos[0].Elu.Count; i++)
        {
            var pts = r.Modelos.Select(m => m.Elu[i]).ToList();
            r.Envoltoria.Add(new PontoEnvoltoria(pts[0].X, pts.Max(p => p.M), pts.Min(p => p.M), pts.Max(p => p.V), pts.Min(p => p.V)));
        }
        r.Avisos.AddRange(avisos);
        return r;
    }

    // Uma barra por intervalo de estações, ao longo de X (Z para cima); apoio esquerdo fixo, direito móvel; mola de rotação (Ry) nos
    // apoios; os graus fora do plano travados nos apoios. Cargas pontuais nos nós (as estações incluem as posições delas).
    private static void Resolver(ModeloAnaliseViga mod, List<double> est, double xa, double xb, double kE, double kD, MaterialBarra mat,
        ResultadoAnaliseViga r, List<CargaConcentrada> pontuais, Func<double, (double A, double Zg, double I, string Nome)> secao)
    {
        var m = new ModeloBarras();
        foreach (double x in est) m.NovoNo(x, 0, 0);
        for (int i = 0; i < est.Count - 1; i++)
        {
            var s = secao((est[i] + est[i + 1]) / 2);
            m.Barra(i, i + 1, new SecaoBarra { A = s.A, Iy = s.I, Iz = s.I, J = s.I, Descricao = s.Nome }, mat);
        }
        int No(double x) { int j = 0; double dm = double.MaxValue; for (int i = 0; i < est.Count; i++) if (Math.Abs(est[i] - x) < dm) { dm = Math.Abs(est[i] - x); j = i; } return j; }
        int na = No(xa), nb = No(xb);
        void Apoio(int no, bool ux, double kr)
        {
            var a = m.Apoio(no, new[] { ux, true, true, true, double.IsPositiveInfinity(kr), true });
            if (!double.IsPositiveInfinity(kr)) a.Mola[4] = kr;
        }
        Apoio(na, true, kE);
        Apoio(nb, false, kD);
        foreach (CasoViga c in Enum.GetValues(typeof(CasoViga)))
        {
            var caso = new CasoCarga { Nome = c.ToString(), Natureza = c == CasoViga.SC ? NaturezaCaso.Variavel : NaturezaCaso.Permanente };
            double w = r.Linear[c] / 100;   // kN/m → kN/cm
            if (Math.Abs(w) > 0)
                for (int i = 0; i < m.Barras.Count; i++) caso.Distribuidas.Add(new CargaDistribuida { Barra = i, Q = new Vetor3(0, 0, -w) });
            foreach (var p in pontuais)
            {
                double f = c == CasoViga.CP ? p.G : c == CasoViga.SC ? p.Q : 0;
                if (Math.Abs(f) > 0) caso.Nodais.Add((No(p.X), new double[] { 0, 0, -f, 0, 0, 0 }));
            }
            m.Casos.Add(caso);
        }
        var solver = new SolverBarras(m);
        var res = solver.Resolver();
        foreach (var caso in m.Casos)
        {
            var rc = res[caso.Nome];
            var c = (CasoViga)Enum.Parse(typeof(CasoViga), caso.Nome);
            var l = new List<PontoDiagrama>();
            for (int i = 0; i < est.Count; i++)
            {
                // em cada estação, o esforço da barra que chega nela (a da direita na ponta esquerda)
                int bi = i == 0 ? 0 : i - 1;
                var bar = m.Barras[bi];
                double xl = i == 0 ? 0 : est[i] - est[i - 1];
                var ef = solver.Esforcos(rc, caso, bar, xl);
                double fl = -rc.Deslocamentos[i][2];
                l.Add(new PontoDiagrama(est[i], -ef.My / 100, -ef.Vz, fl));
            }
            // cortante logo à direita de cada estação (o diagrama fica com os dois valores nas descontinuidades)
            mod.Casos[c] = ComDescontinuidades(l, est, m, solver, rc, caso);
            mod.Reacoes[c] = (rc.Reacoes[na][2], rc.Reacoes[nb][2]);
        }
    }

    // Acrescenta, em cada nó interno, o ponto logo à direita (x + 1e-3) com os esforços da barra seguinte: nos apoios (cortante e,
    // com mola, o momento da ligação) e nas cargas pontuais o diagrama mostra o salto; o ponto extra existe em todos os casos, para as listas poderem ser somadas.
    private static List<PontoDiagrama> ComDescontinuidades(List<PontoDiagrama> l, List<double> est, ModeloBarras m, SolverBarras s, ResultadoCaso rc, CasoCarga caso)
    {
        var o = new List<PontoDiagrama>();
        for (int i = 0; i < l.Count; i++)
        {
            o.Add(l[i]);
            if (i > 0 && i < l.Count - 1)
            {
                var ef = s.Esforcos(rc, caso, m.Barras[i], 0);
                o.Add(new PontoDiagrama(est[i] + 1e-3, -ef.My / 100, -ef.Vz, l[i].Flecha));
            }
        }
        return o;
    }
}
