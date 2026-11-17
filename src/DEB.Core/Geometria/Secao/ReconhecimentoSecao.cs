using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Pecas;

namespace DEB.Core.Geometria.Secao;

/// <summary>
/// Reconhecimento da família de uma seção medida (D28): polígono → família + orientação + parâmetros, ou a seção medida
/// (família "poligonal") se não casar com nenhuma. Retangular, T, L e I vêm de <see cref="SecaoPoligonal.Classificar"/>
/// (mesmas faixas e tolerância de hoje: o que ele chama de L continua L, com os parâmetros tirados das faixas); a
/// trapezoidal é procurada entre as que ele chama de poligonais, e o seu contorno tem de dar a mesma seção que o polígono
/// (<see cref="SecaoPoligonal.MesmaSecao"/>), senão fica a medida.
/// Forma normal da resposta: a T só usa o espelho vertical (o horizontal vira troca de ae e ad); a L usa os dois; I,
/// trapezoidal e retangular voltam sempre na posição canônica (o espelho vira troca de parâmetros).
/// </summary>
public static class ReconhecimentoSecao
{
    public static SecaoTransversal Reconhecer(SecaoPoligonal s, double tol = SecaoPoligonal.Tolerancia)
    {
        if (s == null || s.Contorno.Count < 3) throw new ArgumentException("Seção sem contorno.");
        if (s.Furos.Count > 0) return SecaoTransversal.DaMedida(s);   // nenhuma família tem furo
        SecaoTransversal r;
        try { r = PelaForma(s, tol); }
        catch (ArgumentException) { r = null; }                        // valores que não formam a família (ex.: balanço ~0)
        return r ?? SecaoTransversal.DaMedida(s);
    }

    private static SecaoTransversal PelaForma(SecaoPoligonal s, double tol)
    {
        var tipo = s.Classificar().Tipo;
        var f = s.Faixas();
        switch (tipo)
        {
            case TipoSecao.Retangular:
                return SecaoTransversal.Retangular(s.B, s.H);
            case TipoSecao.T:
            case TipoSecao.TInvertido:
            {
                bool inv = tipo == TipoSecao.TInvertido;
                var (mesa, alma) = inv ? (f[0], f[1]) : (f[1], f[0]);
                return SecaoTransversal.T(alma.Largura, s.H, mesa.Altura, alma.Y0 - mesa.Y0, mesa.Y1 - alma.Y1, inv);
            }
            case TipoSecao.L:
            {
                bool abaEmbaixo = f[0].Largura > f[1].Largura;
                var (aba, alma) = abaEmbaixo ? (f[0], f[1]) : (f[1], f[0]);
                bool almaAEsquerda = Math.Abs(alma.Y0 - aba.Y0) <= SecaoPoligonal.Tolerancia;   // o mesmo teste do Classificar
                return SecaoTransversal.L(alma.Largura, s.H, aba.Altura, aba.Largura - alma.Largura,
                                          new OrientacaoSecao(!almaAEsquerda, !abaEmbaixo));
            }
            case TipoSecao.I:
            {
                var (fi, fm, fs) = (f[0], f[1], f[2]);
                return SecaoTransversal.I(fm.Largura, s.H, fs.Altura, fm.Y0 - fs.Y0, fs.Y1 - fm.Y1, fi.Altura, fm.Y0 - fi.Y0, fi.Y1 - fm.Y1);
            }
            default:
                return Trapezio(s, tol);
        }
    }

    /// <summary>Trapézio simétrico: 4 vértices (tirados os repetidos e os alinhados), fundo e topo horizontais, centros alinhados.</summary>
    private static SecaoTransversal Trapezio(SecaoPoligonal s, double tol)
    {
        var p = SemAlinhados(s.Contorno);
        if (p.Count != 4) return null;
        var porZ = p.OrderBy(q => q.Z).ToList();
        var (a, b, c, d) = (porZ[0], porZ[1], porZ[2], porZ[3]);
        if (Math.Abs(a.Z - b.Z) > tol || Math.Abs(c.Z - d.Z) > tol || c.Z - b.Z <= tol) return null;
        double bi = Math.Abs(a.Y - b.Y), bs = Math.Abs(c.Y - d.Y);
        if (Math.Abs((a.Y + b.Y) / 2 - (c.Y + d.Y) / 2) > tol) return null;   // não simétrico
        var r = SecaoTransversal.Trapezoidal(bi, bs, s.H);
        return r.Contorno().MesmaSecao(s, tol) ? r : null;                     // (descarta contorno cruzado)
    }

    /// <summary>Contorno sem pontos repetidos nem vértices no meio de um lado reto.</summary>
    private static List<(double Y, double Z)> SemAlinhados(IReadOnlyList<(double Y, double Z)> c)
    {
        var p = new List<(double Y, double Z)>();
        foreach (var q in c)
            if (p.Count == 0 || Math.Abs(q.Y - p[p.Count - 1].Y) > 1e-6 || Math.Abs(q.Z - p[p.Count - 1].Z) > 1e-6) p.Add(q);
        if (p.Count > 1 && Math.Abs(p[0].Y - p[p.Count - 1].Y) <= 1e-6 && Math.Abs(p[0].Z - p[p.Count - 1].Z) <= 1e-6) p.RemoveAt(p.Count - 1);
        bool mudou = true;
        while (mudou && p.Count > 3)
        {
            mudou = false;
            for (int i = 0; i < p.Count; i++)
            {
                var (u, v, w) = (p[(i + p.Count - 1) % p.Count], p[i], p[(i + 1) % p.Count]);
                double cr = (v.Y - u.Y) * (w.Z - u.Z) - (v.Z - u.Z) * (w.Y - u.Y);
                double len = Math.Sqrt(Math.Pow(w.Y - u.Y, 2) + Math.Pow(w.Z - u.Z, 2));
                if (len > 1e-9 && Math.Abs(cr) / len <= 1e-6) { p.RemoveAt(i); mudou = true; break; }
            }
        }
        return p;
    }
}
