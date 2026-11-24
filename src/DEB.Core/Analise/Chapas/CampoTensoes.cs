using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Analise.Chapas;

/// <summary>
/// Campos de tensões compatíveis (D50, item 9 — na linha do CSFM): a chapa de concreto com as armaduras discretas é resolvida com
/// materiais não lineares, por iterações secantes e carga crescente.
/// Concreto: não resiste à tração (fissura girante, rigidez residual numérica); à compressão, parábola-retângulo da NBR 6118 (8.2.10.1)
/// com fcd reduzido pela fissuração transversal, kc2 = 1/(1,2 + 55·ε1) ≤ 1 (fib MC2010 7.3.3.3, o do CSFM) e ηfc = (30/fck)^(1/3) ≤ 1.
/// Aço: elastoplástico perfeito (fyd), aderência perfeita. Falha: εc2 &lt; εcu no concreto, εs &gt; εud = 10‰ no aço ou sem convergência.
/// Resultado: o campo de compressões, as tensões nas barras no carregamento de cálculo (λ = 1) e o maior λ que a peça aguenta.
/// </summary>
public sealed class CampoTensoes
{
    public double Fcd { get; set; }          // kN/cm²
    public double Fck { get; set; }          // MPa
    public double Fyd { get; set; } = 43.48; // kN/cm²
    public double Ec { get; set; }           // kN/cm²
    public double Es { get; set; } = 21000;
    public double EpsC2 { get; set; } = 0.002;
    public double EpsCu { get; set; } = 0.0035;
    public double EpsUd { get; set; } = 0.010;
    /// <summary>Deformação de tração até a qual o concreto ainda conta como não fissurado (só estabilidade numérica).</summary>
    public double EpsT0 { get; set; } = 1e-5;

    public sealed class Estado
    {
        public double Lambda;
        public bool Convergiu;
        public int Iteracoes;
        public double[] S2, S1, Ang, Kc2, Eps1, Eps2;   // por elemento (kN/cm²)
        public double[] SigmaS, EpsS;                    // por barra
        public string Falha = "";
    }

    public Estado Calculo { get; private set; }
    public double LambdaMax { get; private set; }
    public string FalhaGovernante { get; private set; } = "";
    public List<(double Lambda, double Deslocamento)> Curva { get; } = new List<(double, double)>();

    private readonly ChapaPlana _c;
    private readonly Dictionary<int, (double Fx, double Fy)> _f0;

    public CampoTensoes(ChapaPlana chapa)
    {
        _c = chapa;
        _f0 = chapa.Forcas.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private double EtaFc => Math.Min(1, Math.Pow(30 / Math.Max(1, Fck), 1.0 / 3));

    /// <summary>Tensão de compressão (positiva) para a deformação de compressão e (positiva) e o fcd efetivo.</summary>
    private double SigmaC(double e, double fc)
    {
        if (e <= 0) return 0;
        if (e >= EpsC2) return fc;
        double r = 1 - e / EpsC2;
        return fc * (1 - r * r);
    }

    /// <summary>Resolve num fator de carga; devolve o estado (convergência pelas deformações principais).</summary>
    public Estado Resolver(double lambda, Estado partida = null, int maxIter = 150)
    {
        foreach (var kv in _f0) _c.Forcas[kv.Key] = (kv.Value.Fx * lambda, kv.Value.Fy * lambda);
        int ne = _c.Elementos.Count, nb = _c.Barras.Count;
        var est = new Estado { Lambda = lambda, S2 = new double[ne], S1 = new double[ne], Ang = new double[ne], Kc2 = new double[ne], Eps1 = new double[ne], Eps2 = new double[ne],
                               SigmaS = new double[nb], EpsS = new double[nb] };
        double eRes = Ec * 1e-4;
        // partida: elástico (ou o estado anterior)
        if (partida == null) foreach (var el in _c.Elementos) el.D = null;
        foreach (var b in _c.Barras) b.Es = b.E;
        double[] uAnt = null, epsAnt = null;
        for (int it = 1; it <= maxIter; it++)
        {
            _c.Resolver();
            // atualiza as secantes
            double fc0 = EtaFc * Fcd;
            for (int i = 0; i < ne; i++)
            {
                var el = _c.Elementos[i];
                double c = (el.Ex + el.Ey) / 2, r = Math.Sqrt(Math.Pow((el.Ex - el.Ey) / 2, 2) + Math.Pow(el.Gxy / 2, 2));
                double e1 = c + r, e2 = c - r, th = 0.5 * Math.Atan2(el.Gxy, el.Ex - el.Ey);
                double kc2 = e1 > EpsT0 ? Math.Min(1, 1 / (1.2 + 55 * e1)) : 1;
                double s2 = e2 < 0 ? SigmaC(-e2, kc2 * fc0) : 0;           // compressão positiva
                double E2 = e2 < -1e-12 ? Math.Max(eRes, s2 / -e2) : e2 > EpsT0 ? eRes : Ec;
                // faixa de tração "não fissurada" (ε1 ≤ εt0): estabiliza a direção sem carga (σ1 ≤ Ec·εt0 ≈ 0,3 MPa); acima, fissurado sem tração
                double E1 = e1 > EpsT0 ? eRes : e1 >= -1e-12 ? Ec : Math.Max(eRes, SigmaC(-e1, kc2 * fc0) / -e1);
                // fissura girante: tensões e deformações coaxiais → G = (σ1 − σ2) / (2·(ε1 − ε2)) (tração positiva; σ1 do concreto fissurado ≈ 0)
                double s1v = e1 > EpsT0 ? eRes * e1 : e1 >= -1e-12 ? Ec * e1 : -SigmaC(-e1, kc2 * fc0), s2v = e2 < -1e-12 ? -s2 : (e2 > EpsT0 ? eRes * e2 : Ec * e2);
                double G = e1 - e2 > 1e-9 ? Math.Max(eRes, (s1v - s2v) / (2 * (e1 - e2))) : Math.Min(E1, E2) / 2;
                // D no sistema principal (1, 2) → global: rotação pelo ângulo th da direção 1
                double cs = Math.Cos(th), sn = Math.Sin(th);
                var T = new[,] { { cs * cs, sn * sn, cs * sn }, { sn * sn, cs * cs, -cs * sn }, { -2 * cs * sn, 2 * cs * sn, cs * cs - sn * sn } };   // ε' = T ε
                var Dl = new[,] { { E1, 0, 0 }, { 0, E2, 0 }, { 0, 0, G } };
                var D = new double[3, 3];
                for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++) { double sum = 0; for (int p = 0; p < 3; p++) for (int q = 0; q < 3; q++) sum += T[p, a] * Dl[p, q] * T[q, b]; D[a, b] = sum; }
                // relaxação: média com a secante anterior (o método secante puro oscila entre estados com cargas baixas)
                if (el.D != null && it > 1) for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++) D[a, b] = 0.5 * (D[a, b] + el.D[a, b]);
                el.D = D;
                est.S2[i] = -s2; est.S1[i] = 0; est.Ang[i] = th; est.Kc2[i] = kc2; est.Eps1[i] = e1; est.Eps2[i] = e2;
            }
            for (int j = 0; j < nb; j++)
            {
                var b = _c.Barras[j];
                double sg = Math.Max(-Fyd, Math.Min(Fyd, b.E * b.Eps));
                double esn = Math.Abs(b.Eps) > 1e-12 ? Math.Max(b.E * 1e-3, sg / b.Eps) : b.E;
                b.Es = it > 1 ? 0.5 * (esn + b.Es) : esn;
                est.SigmaS[j] = sg; est.EpsS[j] = b.Eps;
            }
            // convergência pelas tensões (concreto e aço): os deslocamentos do concreto fissurado e descarregado não importam
            // e as deformações das partes carregadas (concreto comprimido e barras): no patamar a tensão para e a deformação cresce sem fim
            var sig = est.S2.Concat(est.SigmaS).ToArray();
            var eps = new double[ne + nb];
            for (int i = 0; i < ne; i++) eps[i] = -est.S2[i] > 0.02 * Fcd ? est.Eps2[i] : 0;
            for (int j = 0; j < nb; j++) eps[ne + j] = est.EpsS[j];
            double ds = 1, de = 1, emax = 1e-9;
            if (uAnt != null)
            {
                ds = 0; de = 0;
                for (int k = 0; k < sig.Length; k++) ds = Math.Max(ds, Math.Abs(sig[k] - uAnt[k]));
                for (int k = 0; k < eps.Length; k++) { de = Math.Max(de, Math.Abs(eps[k] - epsAnt[k])); emax = Math.Max(emax, Math.Abs(eps[k])); }
            }
            uAnt = sig; epsAnt = eps;
            est.Iteracoes = it;
            if (it > 2 && ds < 1e-3 * Math.Max(Fcd, 0.1 * Fyd) && de < 2e-3 * emax) { est.Convergiu = true; break; }
        }
        double eMin = est.Eps2.DefaultIfEmpty(0).Min(), sMax = est.EpsS.Select(Math.Abs).DefaultIfEmpty(0).Max();
        if (!est.Convergiu) est.Falha = "a iteração parou de convergir (início de mecanismo, esmagamento localizado ou limite numérico; os resultados em λ = 1 valem)";
        else if (-eMin > EpsCu) est.Falha = $"esmagamento do concreto (εc = {-eMin * 1000:0.0}‰ > {EpsCu * 1000:0.0}‰)";
        else if (sMax > EpsUd) est.Falha = $"deformação do aço {sMax * 1000:0.0}‰ > {EpsUd * 1000:0}‰";
        return est;
    }

    /// <summary>
    /// Estado no carregamento de cálculo (λ = 1) e o maior fator de carga, por passos de 0,1 até 1 e depois crescentes até falhar
    /// (refinado por bisseção). <paramref name="noControle"/>: grau cujo deslocamento vai para a curva carga × deslocamento.
    /// </summary>
    public void Analisar(int noControle = -1, double lambdaLimite = 3)
    {
        Estado ultimo = null; double lOk = 0;
        Estado Passo(double l)
        {
            var e = Resolver(l, ultimo);
            if (e.Falha == "") { ultimo = e; lOk = l; if (noControle >= 0) Curva.Add((l, _c.U[noControle])); }
            return e;
        }
        string falha = "";
        foreach (double l in new[] { 0.25, 0.5, 0.75, 1.0 })
        {
            var e = Passo(l);
            if (e.Falha != "") { falha = e.Falha; break; }
        }
        Calculo = lOk >= 1 - 1e-9 ? ultimo : null;
        if (falha == "")
        {
            double l = 1;
            while (l < lambdaLimite)
            {
                double prox = Math.Min(lambdaLimite, l * 1.5);
                var e = Passo(prox);
                if (e.Falha != "") { falha = e.Falha; break; }
                l = prox;
            }
        }
        // bisseção entre o último que passou e o que falhou
        if (falha != "")
        {
            double a = lOk, b = lOk <= 0 ? 0.25 : Math.Min(lambdaLimite, lOk * 1.5);
            for (int k = 0; k < 6 && b - a > 0.01; k++)
            {
                double m = (a + b) / 2;
                var e = Resolver(m, ultimo);
                if (e.Falha == "") { a = m; ultimo = e; } else { b = m; falha = e.Falha; }
            }
            lOk = a;
        }
        LambdaMax = lOk;
        FalhaGovernante = falha == "" ? $"não falhou até λ = {lambdaLimite:0.#} (limite da busca)" : falha;
        if (Calculo == null) Calculo = ultimo;
        // deixa a chapa no estado de cálculo (λ = 1) para quem ler depois
        if (LambdaMax >= 1) Resolver(1.0, null);
    }
}
