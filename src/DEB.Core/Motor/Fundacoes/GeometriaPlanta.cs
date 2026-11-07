using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Motor.Fundacoes;

/// <summary>
/// Geometria em planta do bloco (polígonos convexos anti-horários).
/// CÓPIA das funções privadas de <c>Templates/Blocos/TemplateBlocoEstacas.cs</c> (Unit, Dot, Recortar, Corda, Encolher,
/// Area e o hexágono do bloco triangular) — duplicação temporária da frente B; remover na integração, deixando uma só.
/// </summary>
internal static class GeometriaPlanta
{
    public static (double X, double Y) Unit((double X, double Y) a) { double n = Math.Sqrt(a.X * a.X + a.Y * a.Y); return (a.X / n, a.Y / n); }
    public static double Dot((double X, double Y) a, (double X, double Y) b) => a.X * b.X + a.Y * b.Y;

    /// <summary>Sutherland–Hodgman: mantém n·p ≤ d.</summary>
    public static List<(double X, double Y)> Recortar(List<(double X, double Y)> poly, (double X, double Y) n, double d)
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
    public static (double A, double B)? Corda(List<(double X, double Y)> poly, (double X, double Y) o, (double X, double Y) u)
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
    public static List<(double X, double Y)> Encolher(List<(double X, double Y)> p, double d)
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

    public static double Area(List<(double X, double Y)> p)
    {
        double s = 0;
        for (int i = 0; i < p.Count; i++) { var a = p[i]; var b = p[(i + 1) % p.Count]; s += a.X * b.Y - b.X * a.Y; }
        return Math.Abs(s) / 2;
    }

    /// <summary>Bloco triangular: estacas num triângulo equilátero de lado s e o hexágono com as 6 faces a e dos eixos
    /// (3 paralelas aos lados, 3 nos cantos). Origem no canto inferior esquerdo da caixa envolvente.</summary>
    public static (List<(double X, double Y)> Contorno, List<(double X, double Y)> Estacas) Triangular(double s, double e)
    {
        double h3 = s * Math.Sqrt(3) / 2;
        var est = new List<(double X, double Y)> { (0, 0), (h3, -s / 2), (h3, s / 2) };
        var g = (X: est.Average(p => p.X), Y: est.Average(p => p.Y));
        var semi = new List<((double X, double Y) n, double d)>();
        for (int i = 0; i < 3; i++)
        {
            var a = est[i]; var b = est[(i + 1) % 3]; var o = est[(i + 2) % 3];
            var n = Unit((b.Y - a.Y, -(b.X - a.X)));
            if (Dot(n, (o.X - a.X, o.Y - a.Y)) > 0) n = (-n.X, -n.Y);
            semi.Add((n, Dot(n, a) + e));
            var dv = Unit((a.X - g.X, a.Y - g.Y));
            semi.Add((dv, Dot(dv, a) + e));
        }
        var poly = new List<(double X, double Y)> { (-1e4, -1e4), (1e4, -1e4), (1e4, 1e4), (-1e4, 1e4) };
        foreach (var (n, d) in semi) poly = Recortar(poly, n, d);
        double mx = poly.Min(p => p.X), my = poly.Min(p => p.Y);
        return (poly.Select(p => (p.X - mx, p.Y - my)).ToList(), est.Select(p => (p.X - mx, p.Y - my)).ToList());
    }
}
