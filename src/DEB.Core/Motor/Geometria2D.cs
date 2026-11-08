using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Motor;

/// <summary>
/// Geometria plana comum ao motor (componentes e prancha): vetores 2D, recorte de polígono convexo, corda, offset,
/// área, perímetro e utilitários de polilinha. Uma só cópia (antes: GeometriaPlanta nos componentes e Geo na prancha).
/// Polígonos convexos anti-horários onde indicado.
/// </summary>
public static class Geometria2D
{
    public static (double X, double Y) Unit((double X, double Y) a) { double n = Math.Sqrt(a.X * a.X + a.Y * a.Y); return (a.X / n, a.Y / n); }
    public static double Dot((double X, double Y) a, (double X, double Y) b) => a.X * b.X + a.Y * b.Y;

    /// <summary>Sutherland–Hodgman: mantém n·p ≤ d.</summary>
    public static List<(double X, double Y)> Recortar(IReadOnlyList<(double X, double Y)> poly, (double X, double Y) n, double d)
    {
        var r = new List<(double X, double Y)>();
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count];
            double fa = Dot(n, a) - d, fb = Dot(n, b) - d;
            if (fa <= 0) r.Add(a);
            if (fa * fb < 0) { double t = fa / (fa - fb); r.Add((a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y))); }
        }
        return r;
    }

    /// <summary>Trecho [a, b] da reta p(s) = o + s·u dentro do contorno (convexo, anti-horário); null = fora.</summary>
    public static (double A, double B)? Corda(IReadOnlyList<(double X, double Y)> poly, (double X, double Y) o, (double X, double Y) u)
    {
        double a = double.NegativeInfinity, b = double.PositiveInfinity;
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i]; var q = poly[(i + 1) % poly.Count];
            var ed = (X: q.X - p.X, Y: q.Y - p.Y);
            double f0 = ed.X * (o.Y - p.Y) - ed.Y * (o.X - p.X);   // cross(ed, o − p) ≥ 0 dentro
            double f1 = ed.X * u.Y - ed.Y * u.X;
            if (Math.Abs(f1) < 1e-12) { if (f0 < 0) return null; continue; }
            double s = -f0 / f1;
            if (f1 > 0) a = Math.Max(a, s); else b = Math.Min(b, s);
        }
        return b > a ? (a, b) : ((double, double)?)null;
    }

    /// <summary>Contorno convexo recuado de d (offset para dentro).</summary>
    public static List<(double X, double Y)> Encolher(IReadOnlyList<(double X, double Y)> p, double d)
    {
        var poly = new List<(double X, double Y)> { (-1e4, -1e4), (1e4, -1e4), (1e4, 1e4), (-1e4, 1e4) };
        for (int i = 0; i < p.Count; i++)
        {
            var a = p[i]; var b = p[(i + 1) % p.Count];
            var n = Unit((b.Y - a.Y, -(b.X - a.X)));   // anti-horário: normal externa = direita da aresta
            poly = Recortar(poly, n, Dot(n, a) - d);
        }
        return poly;
    }

    /// <summary>Retângulo anti-horário (x0, y0) → (x1, y1).</summary>
    public static List<(double X, double Y)> Ret(double x0, double y0, double x1, double y1) =>
        new List<(double X, double Y)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };

    public static double Area(IReadOnlyList<(double X, double Y)> p)
    {
        double s = 0;
        for (int i = 0; i < p.Count; i++) { var a = p[i]; var b = p[(i + 1) % p.Count]; s += a.X * b.Y - b.X * a.Y; }
        return Math.Abs(s) / 2;
    }


    public static List<(double X, double Y)> SemRepetidos(IEnumerable<(double X, double Y)> pts, double tol = 1e-4)
    {
        var r = new List<(double X, double Y)>();
        foreach (var p in pts)
            if (r.Count == 0 || Math.Abs(r[r.Count - 1].X - p.X) > tol || Math.Abs(r[r.Count - 1].Y - p.Y) > tol) r.Add(p);
        return r;
    }

    public static double Perimetro(IReadOnlyList<(double X, double Y)> p, bool fechado = true)
    {
        double s = 0;
        int n = fechado ? p.Count : p.Count - 1;
        for (int i = 0; i < n; i++) { var a = p[i]; var b = p[(i + 1) % p.Count]; s += Dist(a, b); }
        return s;
    }

    public static double Dist((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

    /// <summary>Maior segmento da polilinha: início, fim e direção unitária; null = um ponto só.</summary>
    public static ((double X, double Y) A, (double X, double Y) B, (double X, double Y) U)? MaiorSegmento(IReadOnlyList<(double X, double Y)> p)
    {
        double melhor = 1e-6; int k = -1;
        for (int i = 0; i + 1 < p.Count; i++) { double d = Dist(p[i], p[i + 1]); if (d > melhor) { melhor = d; k = i; } }
        if (k < 0) return null;
        var a = p[k]; var b = p[k + 1];
        return (a, b, ((b.X - a.X) / melhor, (b.Y - a.Y) / melhor));
    }

    public static (double X, double Y) Centro(IReadOnlyList<(double X, double Y)> p) => (p.Average(q => q.X), p.Average(q => q.Y));

    public static double Extensao(IReadOnlyList<(double X, double Y)> p) =>
        p.Count == 0 ? 0 : Math.Max(p.Max(q => q.X) - p.Min(q => q.X), p.Max(q => q.Y) - p.Min(q => q.Y));
}
