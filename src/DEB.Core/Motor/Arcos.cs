using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Motor;

/// <summary>Arcos das barras do modelo (bulge por segmento + normal do plano, ver <see cref="PosicaoArmadura.Bulges"/>).</summary>
public static class Arcos
{
    private static P3 Un(P3 a) { double n = a.Comprimento; return n < 1e-12 ? new P3(0, 0, 0) : a * (1 / n); }
    private static P3 Cr(P3 a, P3 b) => new P3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static double Pe(P3 a, P3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>Gira <paramref name="p"/> de <paramref name="ang"/> (rad, anti-horário visto de <paramref name="n"/>) em torno do
    /// eixo de direção n que passa em <paramref name="c"/> (Rodrigues).</summary>
    public static P3 Girar(P3 p, P3 c, P3 n, double ang)
    {
        var v = p - c;
        double co = Math.Cos(ang), si = Math.Sin(ang);
        return c + v * co + Cr(n, v) * si + n * (Pe(n, v) * (1 - co));
    }

    /// <summary>
    /// Pontos do eixo com os arcos discretizados (<paramref name="porArco"/> trechos por arco): para as vistas em que a barra
    /// aparece em projeção (de lado, em perspectiva) e para medidas aproximadas. Sem arcos, devolve os próprios vértices.
    /// </summary>
    public static List<P3> Amostrar(IReadOnlyList<P3> v, IReadOnlyList<double> bulges, P3 normal, bool fechada, int porArco = 16)
    {
        if (bulges == null || bulges.All(b => Math.Abs(b) < 1e-12) || v.Count < 2) return v.ToList();
        var n = Un(normal);
        var r = new List<P3> { v[0] };
        int nseg = fechada ? v.Count : v.Count - 1;
        for (int k = 0; k < nseg; k++)
        {
            var a = v[k]; var b = v[(k + 1) % v.Count];
            double bu = k < bulges.Count ? bulges[k] : 0;
            if (Math.Abs(bu) < 1e-12) { r.Add(b); continue; }
            double th = 4 * Math.Atan(bu);
            var cd = b - a;
            double L = cd.Comprimento;
            var w = Un(Cr(n, cd));                     // à esquerda da corda, visto de n
            var c = (a + b) * 0.5 + w * (L / 2 * (1 - bu * bu) / (2 * bu));
            for (int j = 1; j <= porArco; j++) r.Add(j == porArco ? b : Girar(a, c, n, th * j / porArco));
        }
        return r;
    }

    /// <summary>Comprimento do eixo com os arcos (exato).</summary>
    public static double Comprimento(IReadOnlyList<P3> v, IReadOnlyList<double> bulges, bool fechada)
    {
        double L = 0;
        int nseg = fechada ? v.Count : v.Count - 1;
        for (int k = 0; k < nseg; k++)
        {
            double c = (v[(k + 1) % v.Count] - v[k]).Comprimento;
            double b = bulges != null && k < bulges.Count ? bulges[k] : 0;
            if (Math.Abs(b) < 1e-12) { L += c; continue; }
            double th = Math.Abs(4 * Math.Atan(b));
            L += c / (2 * Math.Sin(th / 2)) * th;
        }
        return L;
    }
}
