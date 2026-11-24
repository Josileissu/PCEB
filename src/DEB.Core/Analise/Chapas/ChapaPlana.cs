using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Analise.Chapas;

/// <summary>Elemento quadrilátero de 4 nós (Q4 isoparamétrico, estado plano de tensão) com espessura própria (cm).</summary>
public sealed class ElementoQ4
{
    public int[] Nos { get; set; } = new int[4];
    public double Espessura { get; set; }
    /// <summary>Tensões no centro (kN/cm²): σx, σy, τxy; principais σ1 ≥ σ2 e o ângulo de σ1 com x (rad).</summary>
    public double Sx, Sy, Txy, S1, S2, Angulo;
    /// <summary>Deformações no centro (εx, εy, γxy) da última solução.</summary>
    public double Ex, Ey, Gxy;
    /// <summary>Matriz constitutiva própria (secante, análise não linear); null = a elástica da chapa.</summary>
    public double[,] D { get; set; }
    public string Regiao { get; set; } = "";
}

/// <summary>
/// Chapa em estado plano de tensão (D50, item 3; base do campo de tensões do nó, D46-6): malha de Q4 com integração 2×2, apoios por
/// grau, forças nodais, solução elástica por Cholesky em banda. Unidades: cm, kN, kN/cm². Validação nos testes: viga em balanço
/// (flexão + cisalhamento de Timoshenko) e equilíbrio das reações.
/// </summary>
/// <summary>Barra de armadura (treliça) entre dois nós da chapa, aderência perfeita (campos de tensões, D50 item 9). Área em cm², E em kN/cm².</summary>
public sealed class BarraChapa
{
    public int I, J;
    public double Area, E = 21000, Es = 21000;
    public string Grupo = "";
    /// <summary>Deformação e tensão (kN/cm²) da última solução.</summary>
    public double Eps, Sigma;
}

public sealed class ChapaPlana
{
    /// <summary>Armaduras da chapa (rigidez Es·A/L, Es secante na análise não linear).</summary>
    public List<BarraChapa> Barras { get; } = new List<BarraChapa>();
    public List<(double X, double Y)> Nos { get; } = new List<(double, double)>();
    public List<ElementoQ4> Elementos { get; } = new List<ElementoQ4>();
    public double E { get; set; } = 3000;   // kN/cm²
    public double Nu { get; set; } = 0.2;
    /// <summary>Graus presos: (nó, 0 = x / 1 = y).</summary>
    public HashSet<(int No, int Gdl)> Presos { get; } = new HashSet<(int, int)>();
    public Dictionary<int, (double Fx, double Fy)> Forcas { get; } = new Dictionary<int, (double, double)>();

    public double[] U { get; private set; }
    public double[] Reacoes { get; private set; }

    public void Forca(int no, double fx, double fy)
    {
        Forcas.TryGetValue(no, out var f);
        Forcas[no] = (f.Fx + fx, f.Fy + fy);
    }

    private static readonly double G = 1 / Math.Sqrt(3);

    private double[,] Dmat()
    {
        double c = E / (1 - Nu * Nu);
        return new[,] { { c, c * Nu, 0 }, { c * Nu, c, 0 }, { 0, 0, c * (1 - Nu) / 2 } };
    }

    /// <summary>Matriz B (3×8) e det J no ponto (ξ, η).</summary>
    private (double[,] B, double DetJ) Bmat(ElementoQ4 el, double xi, double eta)
    {
        double[] dNxi = { -(1 - eta) / 4, (1 - eta) / 4, (1 + eta) / 4, -(1 + eta) / 4 };
        double[] dNeta = { -(1 - xi) / 4, -(1 + xi) / 4, (1 + xi) / 4, (1 - xi) / 4 };
        double j11 = 0, j12 = 0, j21 = 0, j22 = 0;
        for (int k = 0; k < 4; k++)
        {
            var (x, y) = Nos[el.Nos[k]];
            j11 += dNxi[k] * x; j12 += dNxi[k] * y; j21 += dNeta[k] * x; j22 += dNeta[k] * y;
        }
        double det = j11 * j22 - j12 * j21;
        var b = new double[3, 8];
        for (int k = 0; k < 4; k++)
        {
            double dx = (j22 * dNxi[k] - j12 * dNeta[k]) / det, dy = (-j21 * dNxi[k] + j11 * dNeta[k]) / det;
            b[0, 2 * k] = dx; b[1, 2 * k + 1] = dy; b[2, 2 * k] = dy; b[2, 2 * k + 1] = dx;
        }
        return (b, det);
    }

    private double[,] Ke(ElementoQ4 el, double[,] d)
    {
        var k = new double[8, 8];
        foreach (double xi in new[] { -G, G })
            foreach (double eta in new[] { -G, G })
            {
                var (b, det) = Bmat(el, xi, eta);
                double f = det * el.Espessura;
                for (int i = 0; i < 8; i++)
                    for (int j = 0; j < 8; j++)
                    {
                        double s = 0;
                        for (int p = 0; p < 3; p++) for (int q = 0; q < 3; q++) s += b[p, i] * d[p, q] * b[q, j];
                        k[i, j] += s * f;
                    }
            }
        return k;
    }

    /// <summary>Monta, resolve e calcula as tensões no centro de cada elemento.</summary>
    public void Resolver()
    {
        int n = Nos.Count * 2;
        // banda: maior diferença de índice dentro de um elemento
        int bw = Math.Max(Elementos.Max(e => (e.Nos.Max() - e.Nos.Min()) * 2 + 2), Barras.Count == 0 ? 0 : Barras.Max(b => Math.Abs(b.I - b.J) * 2 + 2));
        var k = new double[n, bw];   // banda superior: k[i, j - i]
        var f = new double[n];
        var d = Dmat();
        foreach (var br in Barras)
        {
            var (xi, yi) = Nos[br.I]; var (xj, yj) = Nos[br.J];
            double L = Math.Sqrt((xj - xi) * (xj - xi) + (yj - yi) * (yj - yi)), c = (xj - xi) / L, sn = (yj - yi) / L, kk = br.Es * br.Area / L;
            double[] t = { -c, -sn, c, sn };
            int[] g = { br.I * 2, br.I * 2 + 1, br.J * 2, br.J * 2 + 1 };
            for (int a = 0; a < 4; a++)
                for (int b = 0; b < 4; b++)
                    if (g[b] >= g[a]) k[g[a], g[b] - g[a]] += kk * t[a] * t[b];
        }
        foreach (var el in Elementos)
        {
            var ke = Ke(el, el.D ?? d);
            for (int a = 0; a < 8; a++)
                for (int b = 0; b < 8; b++)
                {
                    int i = el.Nos[a / 2] * 2 + a % 2, j = el.Nos[b / 2] * 2 + b % 2;
                    if (j >= i) k[i, j - i] += ke[a, b];
                }
        }
        foreach (var kv in Forcas) { f[kv.Key * 2] += kv.Value.Fx; f[kv.Key * 2 + 1] += kv.Value.Fy; }
        var kOrig = (double[,])k.Clone();
        var fOrig = (double[])f.Clone();
        // apoios: penalidade grande na diagonal
        double pen = 0;
        for (int i = 0; i < n; i++) pen = Math.Max(pen, k[i, 0]);
        pen *= 1e10;
        foreach (var (no, g) in Presos) { k[no * 2 + g, 0] += pen; f[no * 2 + g] = 0; }
        // Cholesky em banda (LDLᵀ)
        for (int i = 0; i < n; i++)
        {
            for (int p = Math.Max(0, i - bw + 1); p < i; p++)
            {
                double lpi = k[p, i - p];
                if (lpi == 0) continue;
                double fac = lpi / k[p, 0];
                for (int j = i; j < Math.Min(n, p + bw); j++) k[i, j - i] -= fac * k[p, j - p];
            }
            if (Math.Abs(k[i, 0]) < 1e-12) throw new InvalidOperationException($"Chapa instável no grau {i} (nó {i / 2}): falta apoio.");
        }
        // substituições
        var u = (double[])f.Clone();
        for (int i = 0; i < n; i++)
            for (int p = Math.Max(0, i - bw + 1); p < i; p++) u[i] -= k[p, i - p] / k[p, 0] * u[p];
        for (int i = n - 1; i >= 0; i--)
        {
            double s = u[i];
            for (int j = i + 1; j < Math.Min(n, i + bw); j++) s -= k[i, j - i] * u[j];
            u[i] = s / k[i, 0];
        }
        U = u;
        // reações = K·u − f nos graus presos
        Reacoes = new double[n];
        foreach (var (no, g) in Presos)
        {
            int i = no * 2 + g; double s = 0;
            for (int j = Math.Max(0, i - bw + 1); j < Math.Min(n, i + bw); j++)
                s += (j >= i ? kOrig[i, j - i] : kOrig[j, i - j]) * u[j];
            Reacoes[i] = s - fOrig[i];
        }
        foreach (var el in Elementos)
        {
            var (b, _) = Bmat(el, 0, 0);
            var eps = new double[3];
            for (int p = 0; p < 3; p++) for (int a = 0; a < 8; a++) eps[p] += b[p, a] * u[el.Nos[a / 2] * 2 + a % 2];
            el.Ex = eps[0]; el.Ey = eps[1]; el.Gxy = eps[2];
            var de = el.D ?? d;
            el.Sx = de[0, 0] * eps[0] + de[0, 1] * eps[1] + de[0, 2] * eps[2]; el.Sy = de[1, 0] * eps[0] + de[1, 1] * eps[1] + de[1, 2] * eps[2];
            el.Txy = de[2, 0] * eps[0] + de[2, 1] * eps[1] + de[2, 2] * eps[2];
            double c = (el.Sx + el.Sy) / 2, r = Math.Sqrt(Math.Pow((el.Sx - el.Sy) / 2, 2) + el.Txy * el.Txy);
            el.S1 = c + r; el.S2 = c - r; el.Angulo = 0.5 * Math.Atan2(2 * el.Txy, el.Sx - el.Sy);
        }
        foreach (var br in Barras)
        {
            var (xi, yi) = Nos[br.I]; var (xj, yj) = Nos[br.J];
            double L = Math.Sqrt((xj - xi) * (xj - xi) + (yj - yi) * (yj - yi));
            double du = (u[br.J * 2] - u[br.I * 2]) * (xj - xi) / L + (u[br.J * 2 + 1] - u[br.I * 2 + 1]) * (yj - yi) / L;
            br.Eps = du / L; br.Sigma = br.Es * br.Eps;
        }
    }

    /// <summary>Malha estruturada: células de lado ~h cujo centro cai dentro de <paramref name="dentro"/>; espessura por célula.</summary>
    public static ChapaPlana Malha(double x0, double x1, double y0, double y1, double h, Func<double, double, bool> dentro, Func<double, double, double> espessura,
                                   Func<double, double, string> regiao = null)
    {
        var c = new ChapaPlana();
        int nx = Math.Max(1, (int)Math.Round((x1 - x0) / h)), ny = Math.Max(1, (int)Math.Round((y1 - y0) / h));
        double hx = (x1 - x0) / nx, hy = (y1 - y0) / ny;
        var id = new Dictionary<(int, int), int>();
        int No(int i, int j)
        {
            if (!id.TryGetValue((i, j), out int k)) { k = c.Nos.Count; c.Nos.Add((x0 + i * hx, y0 + j * hy)); id[(i, j)] = k; }
            return k;
        }
        // numeração por linhas de x (banda pequena quando nx < ny): percorre i por fora
        bool porColuna = nx <= ny;
        for (int a = 0; a < (porColuna ? ny : nx); a++)
            for (int b = 0; b < (porColuna ? nx : ny); b++)
            {
                int i = porColuna ? b : a, j = porColuna ? a : b;
                double xc = x0 + (i + 0.5) * hx, yc = y0 + (j + 0.5) * hy;
                if (!dentro(xc, yc)) continue;
                c.Elementos.Add(new ElementoQ4 { Nos = new[] { No(i, j), No(i + 1, j), No(i + 1, j + 1), No(i, j + 1) }, Espessura = espessura(xc, yc), Regiao = regiao?.Invoke(xc, yc) ?? "" });
            }
        return c;
    }
}
