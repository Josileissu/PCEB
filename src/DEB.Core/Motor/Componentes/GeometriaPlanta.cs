using System;
using System.Collections.Generic;
using System.Linq;
using static DEB.Core.Motor.Geometria2D;

namespace DEB.Core.Motor.Componentes;

/// <summary>
/// Geometria em planta própria dos apoios: o hexágono do arranjo triangular de 3 estacas. As funções gerais
/// (recorte, corda, offset, área) estão em <see cref="Geometria2D"/>; o template antigo do bloco ainda tem a sua cópia
/// (sai junto com ele).
/// </summary>
public static class GeometriaPlanta
{
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
