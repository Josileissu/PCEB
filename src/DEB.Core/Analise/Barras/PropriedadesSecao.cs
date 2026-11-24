using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Pecas;

namespace DEB.Core.Analise.Barras;

/// <summary>
/// Propriedades de rigidez da seção levantada (D46, item 3: da geometria física, não das propriedades analíticas do Revit).
/// Área, CG e inércias da <see cref="SecaoPoligonal"/>; produto de inércia e eixos principais; constante de torção:
/// <list type="bullet">
/// <item>retângulo: J = b·t³·(1/3 − 0,21·(t/b)·(1 − t⁴/(12·b⁴))) (Roark, erro &lt; 1 %);</item>
/// <item>outras seções maciças: função de tensões de Prandtl por diferenças finitas (<see cref="J"/>);</item>
/// <item>seção com furos: J ≈ A⁴/(40·Ip).</item>
/// </list>
/// Gabarito: sectionproperties 3.10 (MIT), valores nos testes.
/// </summary>
public static class PropriedadesSecao
{
    /// <summary>Produto de inércia pelo CG, ∫(y − yg)(z − zg) dA (cm⁴).</summary>
    public static double Iyz(SecaoPoligonal s)
    {
        double Pyz(IReadOnlyList<(double Y, double Z)> p)
        {
            double r = 0, a = 0;
            for (int i = 0; i < p.Count; i++)
            {
                var (y0, z0) = p[i];
                var (y1, z1) = p[(i + 1) % p.Count];
                double cr = y0 * z1 - y1 * z0;
                a += cr;
                r += (y0 * z1 + 2 * y0 * z0 + 2 * y1 * z1 + y1 * z0) * cr;
            }
            return (a < 0 ? -1 : 1) * r / 24;
        }
        double pyz = Pyz(s.Contorno) - s.Furos.Sum(f => Pyz(f));
        var (yg, zg) = s.CG;
        return pyz - s.Area * yg * zg;
    }

    /// <summary>
    /// Constante de torção de Saint-Venant (cm⁴). Retângulo: fórmula de Roark. Outras seções maciças: função de tensões de Prandtl
    /// (∇²φ = −2, φ = 0 no contorno) por diferenças finitas em malha regular, J = 2∫φ dA, com extrapolação de Richardson entre duas
    /// malhas — conferido com o sectionproperties (L, T, T invertido, trapézio: diferença &lt; 2 %). Seção com furos: A⁴/(40·Ip).
    /// </summary>
    public static double J(SecaoPoligonal s)
    {
        var f = s.Furos.Count == 0 ? s.Faixas() : null;
        if (f != null && f.Count == 1) return JRetangulo(f[0].Largura, f[0].Altura);
        if (s.Furos.Count > 0) return Math.Pow(s.Area, 4) / (40 * (s.Iy + s.Iz));
        double h = Passo(s);
        double j1 = JPrandtl(s, h), j2 = JPrandtl(s, h / 2);
        return (4 * j2 - j1) / 3;
    }

    /// <summary>
    /// Passo da malha: divisor do MDC das coordenadas dos vértices (em 0,01 cm), para os nós caírem sobre os lados horizontais e
    /// verticais (contorno exato e erro de 2ª ordem, que a extrapolação de Richardson elimina); cerca de 80 passos no lado maior.
    /// </summary>
    private static double Passo(SecaoPoligonal s)
    {
        double lado = Math.Max(s.B, s.H), alvo = lado / 80;
        long Mdc(long a, long b) { while (b != 0) (a, b) = (b, a % b); return Math.Abs(a); }
        long g = 0;
        foreach (var (y, z) in s.Contorno)
        {
            g = Mdc(g, (long)Math.Round((y - s.YMin) * 100));
            g = Mdc(g, (long)Math.Round((z - s.ZMin) * 100));
        }
        double gc = g / 100.0;
        if (gc < alvo / 4) return alvo;   // vértices sem divisor útil: passo livre
        return gc / Math.Ceiling(gc / alvo);
    }

    /// <summary>Prandtl em malha de nós com passo h (gradiente conjugado com pré-condicionador diagonal).</summary>
    private static double JPrandtl(SecaoPoligonal s, double h)
    {
        double y0 = s.YMin, z0 = s.ZMin;
        int ny = (int)Math.Ceiling(s.B / h) + 1, nz = (int)Math.Ceiling(s.H / h) + 1;
        var idx = new int[ny, nz];
        int n = 0;
        for (int i = 0; i < ny; i++)
            for (int k = 0; k < nz; k++)
                idx[i, k] = Dentro(s.Contorno, y0 + i * h, z0 + k * h) ? n++ : -1;
        if (n == 0) return 0;
        // A·φ = b, A = Laplaciano negativo de 5 pontos (·h²), b = 2h²
        var x = new double[n]; var r = new double[n]; var p = new double[n]; var ap = new double[n];
        double b = 2 * h * h;
        for (int q = 0; q < n; q++) r[q] = b;
        void Mult(double[] v, double[] w)
        {
            for (int i = 0; i < ny; i++)
                for (int k = 0; k < nz; k++)
                {
                    int q = idx[i, k];
                    if (q < 0) continue;
                    double soma = 4 * v[q];
                    if (i > 0 && idx[i - 1, k] >= 0) soma -= v[idx[i - 1, k]];
                    if (i < ny - 1 && idx[i + 1, k] >= 0) soma -= v[idx[i + 1, k]];
                    if (k > 0 && idx[i, k - 1] >= 0) soma -= v[idx[i, k - 1]];
                    if (k < nz - 1 && idx[i, k + 1] >= 0) soma -= v[idx[i, k + 1]];
                    w[q] = soma;
                }
        }
        // diagonal constante (4): o pré-condicionador vira escala; CG puro
        Array.Copy(r, p, n);
        double rr = r.Sum(v => v * v), r0 = rr;
        for (int it = 0; it < 20 * n && rr > 1e-20 * r0; it++)
        {
            Mult(p, ap);
            double pap = 0; for (int q = 0; q < n; q++) pap += p[q] * ap[q];
            double alfa = rr / pap;
            double rrn = 0;
            for (int q = 0; q < n; q++) { x[q] += alfa * p[q]; r[q] -= alfa * ap[q]; rrn += r[q] * r[q]; }
            double beta = rrn / rr; rr = rrn;
            for (int q = 0; q < n; q++) p[q] = r[q] + beta * p[q];
        }
        return 2 * x.Sum() * h * h;
    }

    /// <summary>Ponto estritamente dentro do polígono (ponto no contorno = fora: φ = 0 ali).</summary>
    private static bool Dentro(IReadOnlyList<(double Y, double Z)> p, double y, double z)
    {
        const double eps = 1e-9;
        bool dentro = false;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
        {
            var (yi, zi) = p[i]; var (yj, zj) = p[j];
            // no contorno?
            double cr = (yj - yi) * (z - zi) - (zj - zi) * (y - yi);
            if (Math.Abs(cr) < eps * Math.Max(1, Math.Abs(yj - yi) + Math.Abs(zj - zi)) &&
                y >= Math.Min(yi, yj) - eps && y <= Math.Max(yi, yj) + eps && z >= Math.Min(zi, zj) - eps && z <= Math.Max(zi, zj) + eps) return false;
            if ((zi > z) != (zj > z) && y < (yj - yi) * (z - zi) / (zj - zi) + yi) dentro = !dentro;
        }
        return dentro;
    }

    /// <summary>J do retângulo b × h (Roark).</summary>
    public static double JRetangulo(double b, double h)
    {
        double lado = Math.Max(b, h), t = Math.Min(b, h);
        if (t <= 0) return 0;
        double q = t / lado;
        return lado * t * t * t * (1.0 / 3 - 0.21 * q * (1 - Math.Pow(q, 4) / 12));
    }

    /// <summary>
    /// Seção da barra nos eixos da seção (y horizontal, z vertical, como a <see cref="SecaoPoligonal"/>), com o ângulo dos eixos
    /// principais. Seção assimétrica (L, T com aba de um lado): <see cref="Principal"/> devolve as inércias principais e o ângulo
    /// para girar o vetor x-z da barra.
    /// </summary>
    public static SecaoBarra Barra(SecaoPoligonal s, string descricao = "") => new SecaoBarra
    {
        A = s.Area, Iy = s.Iy, Iz = s.Iz, J = J(s), Descricao = descricao != "" ? descricao : $"{Fmt.Virgula(s.B, "0.#")}×{Fmt.Virgula(s.H, "0.#")}",
    };

    /// <summary>Inércias principais (I1 ≥ I2) e o ângulo α (rad) do eixo de I1 em relação ao eixo y da seção.</summary>
    public static (double I1, double I2, double Alfa) Principal(SecaoPoligonal s)
    {
        double iy = s.Iy, iz = s.Iz, iyz = Iyz(s);
        // aqui "Iy" = ∫z² (flexão em torno de y) e "Iz" = ∫y²; o eixo principal sai do círculo de Mohr
        double c = (iy + iz) / 2, r = Math.Sqrt(Math.Pow((iy - iz) / 2, 2) + iyz * iyz);
        double alfa = 0.5 * Math.Atan2(-2 * iyz, iy - iz);
        return (c + r, c - r, alfa);
    }
}
