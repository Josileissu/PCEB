using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Pecas;

namespace DEB.Core.Elementos.Pilar.Calculo;

/// <summary>Seção de concreto armado para o ELU: contorno (cm, eixos da seção y, z), barras (cm, mm) e materiais.</summary>
public sealed class SecaoArmada
{
    public SecaoPoligonal Concreto { get; set; }
    /// <summary>Barras: posição (cm, mesmos eixos do contorno) e diâmetro (mm).</summary>
    public List<(double Y, double Z, double Phi)> Barras { get; } = new List<(double, double, double)>();
    public double FckMPa { get; set; } = 30;
    public double FykMPa { get; set; } = 500;
    public double GamaC { get; set; } = 1.4;
    public double GamaS { get; set; } = 1.15;
    /// <summary>Módulo do aço (kN/cm²).</summary>
    public double Es { get; set; } = 21000;
    public double As => Barras.Sum(b => Math.PI * b.Phi * b.Phi / 400);
}

/// <summary>
/// Flexo-compressão oblíqua no ELU (NBR 6118:2023, 17.2): diagrama parábola-retângulo com 0,85·fcd (8.2.10.1; εc2, εcu e n pelo
/// fck), aço elastoplástico com εsu = 10 ‰, domínios pelos pivôs A, B e C; seção em fibras (faixas horizontais cortadas no contorno),
/// barras com o concreto deslocado descontado. A curva de interação no plano dos momentos sai girando a linha neutra; o
/// aproveitamento é |Md|/|MRd| na direção do momento atuante. Conferido com o concreteproperties (gabarito em tests).
/// Convenções: N de compressão positivo (kN); My = Σ F·z e Mz = Σ F·y (kN·cm), positivo comprimindo o lado +z / +y.
/// </summary>
public static class FlexoCompressaoObliqua
{
    private const double Esu = 0.010;

    private sealed class Fibras
    {
        public double[] Y, Z, A;          // concreto, coordenadas em relação ao CG
        public double[] By, Bz, Bas;      // barras
        public double[] Vy, Vz;           // vértices do contorno (CG)
        public double Fcd, Fyd, Es, Ec2, Ecu, N;
    }

    private static Fibras Montar(SecaoArmada s, int faixas = 50)
    {
        var (yg, zg) = s.Concreto.CG;
        double fck = s.FckMPa;
        var f = new Fibras
        {
            Fcd = 0.85 * fck / s.GamaC / 10, Fyd = s.FykMPa / s.GamaS / 10, Es = s.Es,
            Ec2 = fck <= 50 ? 0.002 : (2.0 + 0.085 * Math.Pow(fck - 50, 0.53)) / 1000,
            Ecu = fck <= 50 ? 0.0035 : (2.6 + 35 * Math.Pow((90 - fck) / 100, 4)) / 1000,
            N = fck <= 50 ? 2 : 1.4 + 23.4 * Math.Pow((90 - fck) / 100, 4),
        };
        var y = new List<double>(); var z = new List<double>(); var a = new List<double>();
        double z0 = s.Concreto.ZMin, h = s.Concreto.H, dz = h / faixas;
        for (int k = 0; k < faixas; k++)
        {
            double zc = z0 + (k + 0.5) * dz;
            var c = s.Concreto.CortesHorizontais(zc);
            for (int i = 0; i + 1 < c.Count; i += 2)
            {
                double y0 = c[i], y1 = c[i + 1], w = y1 - y0;
                int n = Math.Max(1, (int)Math.Ceiling(w / dz));
                for (int j = 0; j < n; j++) { y.Add(y0 + (j + 0.5) * w / n - yg); z.Add(zc - zg); a.Add(w / n * dz); }
            }
        }
        f.Y = y.ToArray(); f.Z = z.ToArray(); f.A = a.ToArray();
        f.By = s.Barras.Select(b => b.Y - yg).ToArray(); f.Bz = s.Barras.Select(b => b.Z - zg).ToArray();
        f.Bas = s.Barras.Select(b => Math.PI * b.Phi * b.Phi / 400).ToArray();
        f.Vy = s.Concreto.Contorno.Select(p => p.Y - yg).ToArray(); f.Vz = s.Concreto.Contorno.Select(p => p.Z - zg).ToArray();
        return f;
    }

    private static double Sc(Fibras f, double e) => e <= 0 ? 0 : e < f.Ec2 ? f.Fcd * (1 - Math.Pow(1 - e / f.Ec2, f.N)) : f.Fcd;

    /// <summary>Esforços resistentes para a linha neutra de ângulo α (compressão do lado da normal (−sen α, cos α)) e profundidade x.</summary>
    private static (double N, double My, double Mz) Esforcos(Fibras f, double alfa, double x)
    {
        double ny = -Math.Sin(alfa), nz = Math.Cos(alfa);
        double vmax = double.MinValue, vmin = double.MaxValue;
        for (int i = 0; i < f.Vy.Length; i++) { double v = f.Vy[i] * ny + f.Vz[i] * nz; vmax = Math.Max(vmax, v); vmin = Math.Min(vmin, v); }
        double h = vmax - vmin;
        double d = f.Bas.Length > 0 ? vmax - Enumerable.Range(0, f.Bas.Length).Min(i => f.By[i] * ny + f.Bz[i] * nz) : h;
        double xlim = f.Ecu / (f.Ecu + Esu) * d, tc = (f.Ecu - f.Ec2) / f.Ecu * h;
        // deformação na profundidade t (a partir da fibra mais comprimida), compressão positiva
        Func<double, double> eps;
        if (x <= xlim) { double phi = Esu / (d - x); eps = t => phi * (x - t); }
        else if (x <= h) { double phi = f.Ecu / x; eps = t => phi * (x - t); }
        else { double phi = f.Ec2 / (x - tc); eps = t => phi * (x - t); }
        double n = 0, my = 0, mz = 0;
        for (int i = 0; i < f.A.Length; i++)
        {
            double s = Sc(f, eps(vmax - (f.Y[i] * ny + f.Z[i] * nz)));
            if (s == 0) continue;
            double F = s * f.A[i]; n += F; my += F * f.Z[i]; mz += F * f.Y[i];
        }
        for (int i = 0; i < f.Bas.Length; i++)
        {
            double e = eps(vmax - (f.By[i] * ny + f.Bz[i] * nz));
            double s = Math.Max(-f.Fyd, Math.Min(f.Fyd, f.Es * e)) - Sc(f, e);
            double F = s * f.Bas[i]; n += F; my += F * f.Bz[i]; mz += F * f.By[i];
        }
        return (n, my, mz);
    }

    /// <summary>Força normal resistente máxima (compressão uniforme εc2) e mínima (tração uniforme εsu), kN.</summary>
    public static (double NMax, double NMin) Limites(SecaoArmada s)
    {
        var f = Montar(s);
        double ac = f.A.Sum(), asTot = f.Bas.Sum();
        return (ac * f.Fcd + asTot * (Math.Min(f.Fyd, f.Es * f.Ec2) - f.Fcd), -asTot * f.Fyd);
    }

    /// <summary>Momentos resistentes (My, Mz) para a força normal nd e a linha neutra de ângulo α; null se nd fora dos limites.</summary>
    public static (double My, double Mz)? Resistente(SecaoArmada s, double nd, double alfa) => Resistente(Montar(s), nd, alfa);

    private static (double My, double Mz)? Resistente(Fibras f, double nd, double alfa)
    {
        double h = Math.Sqrt(f.Vy.Max() * f.Vy.Max() + f.Vz.Max() * f.Vz.Max()) * 2 + 1;
        double lo = -40 * h, hi = 200 * h;
        if (Esforcos(f, alfa, lo).N > nd || Esforcos(f, alfa, hi).N < nd) return null;
        for (int it = 0; it < 80; it++)
        {
            double m = (lo + hi) / 2;
            if (Esforcos(f, alfa, m).N < nd) lo = m; else hi = m;
            if (hi - lo < 1e-5 * h) break;
        }
        var r = Esforcos(f, alfa, (lo + hi) / 2);
        return (r.My, r.Mz);
    }

    /// <summary>Curva de interação no plano (My, Mz) para nd (pontos girando a linha neutra; ordenados pelo ângulo do momento).</summary>
    public static List<(double My, double Mz)> Curva(SecaoArmada s, double nd, int pontos = 144)
    {
        var f = Montar(s);
        var l = new List<(double My, double Mz)>();
        for (int k = 0; k < pontos; k++)
        {
            var r = Resistente(f, nd, 2 * Math.PI * k / pontos);
            if (r != null) l.Add(r.Value);
        }
        return l.OrderBy(p => Math.Atan2(p.Mz, p.My)).ToList();
    }

    /// <summary>
    /// Aproveitamento |Md|/|MRd| na direção de (myd, mzd) para nd; &gt; 1 = não atende; +∞ se nd fora dos limites resistentes.
    /// </summary>
    public static double Aproveitamento(SecaoArmada s, double nd, double myd, double mzd) => Aproveitamento(Curva(s, nd), nd, myd, mzd, s);

    public static double Aproveitamento(List<(double My, double Mz)> curva, double nd, double myd, double mzd, SecaoArmada s = null)
    {
        if (curva.Count < 3) return double.PositiveInfinity;
        double md = Math.Sqrt(myd * myd + mzd * mzd);
        if (md < 1e-9) return 0;
        double ang = Math.Atan2(mzd, myd);
        // raio da curva na direção do momento: interseção do raio com o polígono
        double best = double.NaN;
        for (int i = 0; i < curva.Count; i++)
        {
            var p = curva[i]; var q = curva[(i + 1) % curva.Count];
            double dx = Math.Cos(ang), dy = Math.Sin(ang);
            double ex = q.My - p.My, ey = q.Mz - p.Mz;
            double den = dx * ey - dy * ex;
            if (Math.Abs(den) < 1e-12) continue;
            double t = (p.My * ey - p.Mz * ex) / den;          // distância ao longo do raio
            double u = (p.My * dy - p.Mz * dx) / den;          // posição no lado
            if (t > 0 && u >= -1e-9 && u <= 1 + 1e-9) best = double.IsNaN(best) ? t : Math.Max(best, t);
        }
        return double.IsNaN(best) ? double.PositiveInfinity : md / best;
    }

    // ------------------------------------------------------------------ 2ª ordem local

    /// <summary>
    /// Momento total no pilar-padrão com rigidez κ aproximada (NBR 6118 15.8.3.3.3), forma fechada:
    /// 5h·M² + (h²·Nd − Nd·le²/320 − 5h·αb·M1d,A)·M − Nd·h²·αb·M1d,A = 0. Unidades: kN, cm. Devolve ao menos M1d,A.
    /// </summary>
    public static double MomentoTotalKappa(double nd, double m1dA, double h, double le, double alfaB = 1.0)
    {
        if (nd <= 0) return m1dA;
        double a = 5 * h, b = h * h * nd - nd * le * le / 320 - 5 * h * alfaB * m1dA, c = -nd * h * h * alfaB * m1dA;
        double m = (-b + Math.Sqrt(b * b - 4 * a * c)) / (2 * a);
        return Math.Max(m, m1dA);
    }

    /// <summary>λ1 (15.8.2): (25 + 12,5·e1/h)/αb, entre 35 e 90.</summary>
    public static double Lambda1(double e1, double h, double alfaB = 1.0) => Math.Max(35, Math.Min(90, (25 + 12.5 * e1 / h) / alfaB));
}
