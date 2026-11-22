using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Elementos.Viga.PreMoldada.Calculo;

/// <summary>
/// Viga biapoiada com balanços nas pontas (apoios em <see cref="Xa"/> e <see cref="Xb"/>, cm da ponta esquerda): carga distribuída
/// uniforme em todo o comprimento e cargas concentradas. Unidades: cm, kN, kN/cm; momentos em kN·cm (positivo = tração embaixo).
/// </summary>
public sealed class EstaticaViga
{
    public double L { get; }
    public double Xa { get; }
    public double Xb { get; }
    /// <summary>Carga distribuída (kN/cm, para baixo).</summary>
    public double W { get; }
    public IReadOnlyList<(double X, double P)> Pontuais { get; }
    public double Ra { get; }
    public double Rb { get; }

    public EstaticaViga(double L, double xa, double xb, double w, IEnumerable<(double X, double P)> pontuais = null)
    {
        if (xb - xa < 1) throw new ArgumentException("Vão de cálculo nulo: confira os apoios.");
        this.L = L; Xa = xa; Xb = xb; W = w;
        Pontuais = (pontuais ?? Enumerable.Empty<(double, double)>()).Where(p => p.X >= 0 && p.X <= L).ToList();
        double total = w * L + Pontuais.Sum(p => p.P);
        Rb = (w * L * (L / 2 - xa) + Pontuais.Sum(p => p.P * (p.X - xa))) / (xb - xa);
        Ra = total - Rb;
    }

    /// <summary>Momento fletor em x (kN·cm), pelas forças à esquerda.</summary>
    public double M(double x)
    {
        double m = -W * x * x / 2;
        if (x > Xa) m += Ra * (x - Xa);
        if (x > Xb) m += Rb * (x - Xb);
        foreach (var (xp, p) in Pontuais) if (x > xp) m -= p * (x - xp);
        return m;
    }

    /// <summary>Cortante logo à direita de x (kN), pelas forças à esquerda (para cima = positivo).</summary>
    public double V(double x)
    {
        double v = -W * x;
        if (x >= Xa) v += Ra;
        if (x >= Xb) v += Rb;
        foreach (var (xp, p) in Pontuais) if (x >= xp) v -= p;
        return v;
    }

    /// <summary>Cortante junto ao apoio esquerdo (dentro do vão) e ao direito (dentro do vão), em módulo.</summary>
    public double VApoioE => Math.Abs(V(Xa + 1e-6));
    public double VApoioD => Math.Abs(V(Xb - 1e-6));

    /// <summary>Pontos de amostragem do vão: 200 divisões mais as cargas concentradas (antes e depois de cada uma).</summary>
    public List<double> Pontos(int n = 200)
    {
        var xs = Enumerable.Range(0, n + 1).Select(i => Xa + (Xb - Xa) * i / n).ToList();
        foreach (var (xp, _) in Pontuais)
            if (xp > Xa && xp < Xb) { xs.Add(xp - 1e-3); xs.Add(xp); xs.Add(xp + 1e-3); }
        return xs.Distinct().OrderBy(x => x).ToList();
    }

    /// <summary>Flecha no meio do vão (cm) para rigidez EI constante (kN·cm²), pelo princípio dos trabalhos virtuais.</summary>
    public double FlechaMeio(double EI, int n = 400)
    {
        double xm = (Xa + Xb) / 2, s = 0, h = (Xb - Xa) / n;
        double m(double x) => x <= xm ? (x - Xa) / 2 : (Xb - x) / 2;
        for (int i = 0; i <= n; i++)
        {
            double x = Xa + i * h, f = M(x) * m(x);
            s += (i == 0 || i == n) ? f / 2 : f;
        }
        return s * h / EI;
    }
}
