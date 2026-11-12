using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Lajes;

// =====================================================================================================================
// Paginação de lajes alveolares (2026-11-12) — geometria plana do pano.
// Tudo no sistema local do pano (u, v): u = direção do vão (comprimento do painel), v = transversal (largura). Metros.
// =====================================================================================================================

/// <summary>Ponto no sistema local do pano (u = vão, v = transversal).</summary>
public struct PontoUV
{
    public double U, V;
    public PontoUV(double u, double v) { U = u; V = v; }
    public override string ToString() => $"({Fmt.N(U, "0.####")}, {Fmt.N(V, "0.####")})";
}

/// <summary>Retângulo alinhado ao sistema do pano: [U0, U1] × [V0, V1].</summary>
public struct RetUV
{
    public double U0, V0, U1, V1;
    public RetUV(double u0, double v0, double u1, double v1) { U0 = u0; V0 = v0; U1 = u1; V1 = v1; }
    public double LarguraU => U1 - U0;
    public double LarguraV => V1 - V0;
    public RetUV Expandido(double e) => new RetUV(U0 - e, V0 - e, U1 + e, V1 + e);
    public override string ToString() => $"[{Fmt.N(U0, "0.####")}..{Fmt.N(U1, "0.####")}] x [{Fmt.N(V0, "0.####")}..{Fmt.N(V1, "0.####")}]";
}

/// <summary>Funções geométricas básicas da paginação (puras).</summary>
public static class GeometriaPano
{
    public const double Eps = 1e-6;

    /// <summary>Par-ímpar (raio na direção +u). Independe do sentido do contorno.</summary>
    public static bool PontoNoPoligono(double u, double v, IReadOnlyList<PontoUV> pts)
    {
        bool dentro = false;
        int n = pts.Count;
        for (int i = 0; i < n; i++)
        {
            var a = pts[i]; var b = pts[(i + 1) % n];
            if ((a.V > v) != (b.V > v))
                if (a.U + (v - a.V) * (b.U - a.U) / (b.V - a.V) > u) dentro = !dentro;
        }
        return dentro;
    }

    /// <summary>Interseção de dois retângulos; null se não houver área (lados maiores que <paramref name="tol"/>).</summary>
    public static RetUV? Intersecao(RetUV a, RetUV b, double tol = 1e-4)
    {
        double u0 = Math.Max(a.U0, b.U0), v0 = Math.Max(a.V0, b.V0);
        double u1 = Math.Min(a.U1, b.U1), v1 = Math.Min(a.V1, b.V1);
        return (u1 - u0 > tol && v1 - v0 > tol) ? new RetUV(u0, v0, u1, v1) : (RetUV?)null;
    }

    /// <summary>Tira [a, b] de cada trecho; sobras menores ou iguais a <paramref name="minimo"/> são descartadas.</summary>
    public static List<(double A, double B)> SubtrairIntervalo(List<(double A, double B)> trechos, double a, double b, double minimo = 0.05)
    {
        var r = new List<(double, double)>();
        foreach (var (x0, x1) in trechos)
        {
            if (b <= x0 || a >= x1) { r.Add((x0, x1)); continue; }
            if (a - x0 > minimo) r.Add((x0, a));
            if (x1 - b > minimo) r.Add((b, x1));
        }
        return r;
    }

    /// <summary>Arredondamento "meio para o par" em casas decimais (como o round do protótipo).</summary>
    public static double Arred(double x, int casas) => Math.Round(x, casas, MidpointRounding.ToEven);
}

/// <summary>
/// Sistema do pano a partir do ângulo do vão em planta (Floor.SpanDirectionAngle): u = direção do vão, v = transversal.
/// Convenção (a mesma do protótipo para vãos em X e em Y): u e v apontam para o lado em que x + y cresce; vão em X → (u, v) = (x, y);
/// vão em Y → (u, v) = (y, x). Ou seja, o sistema pode ser espelhado — não importa para retângulos e contornos.
/// </summary>
public sealed class SistemaPano
{
    public double DuX { get; }
    public double DuY { get; }
    public double DvX { get; }
    public double DvY { get; }
    /// <summary>Ângulo do vão usado (radianos).</summary>
    public double Angulo { get; }

    public SistemaPano(double anguloVaoRad)
    {
        Angulo = anguloVaoRad;
        double cx = Limpo(Math.Cos(anguloVaoRad)), cy = Limpo(Math.Sin(anguloVaoRad));
        if (cx + cy < -1e-12 || (Math.Abs(cx + cy) <= 1e-12 && cx < 0)) { cx = -cx; cy = -cy; }
        DuX = cx; DuY = cy;
        double px = -cy, py = cx;                       // perpendicular
        if (px + py < -1e-12 || (Math.Abs(px + py) <= 1e-12 && px < 0)) { px = -px; py = -py; }
        DvX = Limpo(px); DvY = Limpo(py);
    }

    /// <summary>Vão alinhado a X ou a Y (o caso comum; aí os retângulos do pano são retângulos alinhados em planta).</summary>
    public bool Ortogonal => (Math.Abs(DuX) < 1e-12 || Math.Abs(DuY) < 1e-12);

    private static double Limpo(double c) => Math.Abs(c) < 1e-12 ? 0 : (Math.Abs(Math.Abs(c) - 1) < 1e-12 ? Math.Sign(c) : c);

    public PontoUV UV(double x, double y) => new PontoUV(x * DuX + y * DuY, x * DvX + y * DvY);
    /// <summary>Componentes (u, v) de um vetor de planta.</summary>
    public (double U, double V) VetorUV(double x, double y) => (x * DuX + y * DuY, x * DvX + y * DvY);
    public (double X, double Y) XY(double u, double v) => (u * DuX + v * DvX, u * DuY + v * DvY);

    /// <summary>Os 4 cantos em planta do retângulo do pano (u0v0, u1v0, u1v1, u0v1).</summary>
    public (double X, double Y)[] Cantos(RetUV r) => new[] { XY(r.U0, r.V0), XY(r.U1, r.V0), XY(r.U1, r.V1), XY(r.U0, r.V1) };

    /// <summary>Caixa em planta do retângulo do pano (x0, y0, x1, y1) — o próprio retângulo quando o vão é ortogonal.</summary>
    public (double X0, double Y0, double X1, double Y1) CaixaXY(RetUV r)
    {
        var c = Cantos(r);
        return (c.Min(p => p.X), c.Min(p => p.Y), c.Max(p => p.X), c.Max(p => p.Y));
    }

    /// <summary>Caixa no sistema do pano de uma nuvem de pontos em planta (ex.: seção de um pilar).</summary>
    public RetUV CaixaUV(IEnumerable<(double X, double Y)> pontos)
    {
        double u0 = double.MaxValue, v0 = double.MaxValue, u1 = double.MinValue, v1 = double.MinValue;
        foreach (var (x, y) in pontos)
        {
            var p = UV(x, y);
            u0 = Math.Min(u0, p.U); v0 = Math.Min(v0, p.V); u1 = Math.Max(u1, p.U); v1 = Math.Max(v1, p.V);
        }
        return new RetUV(u0, v0, u1, v1);
    }
}
