using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Pecas;

/// <summary>
/// Medidas do consolo pelo perfil (2026-11-09): pontos (d, c) — d = distância para fora da face do pilar, c = altura a partir
/// da base. Um chanfro na ponta (trecho curto inclinado, até <see cref="LimiteChanfro"/>) NÃO faz o consolo trapezoidal: a
/// altura da ponta é a das retas principais do fundo e do topo prolongadas até a ponta. Antes, a altura era medida nos
/// vértices da ponta e o chanfro de 2 cm virava "ponta 21,5" num consolo retangular de 23,5 (relato do usuário).
/// </summary>
public static class PerfilConsolo
{
    /// <summary>Maior chanfro na ponta (projeção horizontal, cm); acima disso o trecho inclinado é a forma do consolo.</summary>
    public const double LimiteChanfro = 5;

    public sealed class Medidas
    {
        public double Balanco { get; set; }
        /// <summary>Fundo e topo junto da face do pilar (c).</summary>
        public double BaseNaFace { get; set; }
        public double TopoNaFace { get; set; }
        /// <summary>Altura na ponta sem os chanfros (retas principais prolongadas).</summary>
        public double AlturaPonta { get; set; }
        /// <summary>Chanfro na aresta inferior/superior da ponta: (horizontal, vertical); (0, 0) = sem chanfro.</summary>
        public (double D, double C) ChanfroInferior { get; set; }
        public (double D, double C) ChanfroSuperior { get; set; }
        public double AlturaNaFace => TopoNaFace - BaseNaFace;
    }

    public static Medidas Medir(IEnumerable<(double D, double C)> pontos, double tol = 0.1)
    {
        var pts = pontos.Select(p => (D: Math.Round(p.D / tol) * tol, p.C)).ToList();
        double proj = pts.Max(p => p.D);
        // o perfil do consolo é convexo (retangular, trapezoidal, chanfros): envoltórias convexas de baixo e de cima
        var baixo = Envoltoria(pts.GroupBy(p => p.D).Select(g => (D: g.Key, C: g.Min(x => x.C))).OrderBy(p => p.D).ToList(), -1);
        var cima = Envoltoria(pts.GroupBy(p => p.D).Select(g => (D: g.Key, C: g.Max(x => x.C))).OrderBy(p => p.D).ToList(), 1);
        var (cb, chB) = NaPonta(baixo, proj);
        var (ct, chT) = NaPonta(cima, proj);
        return new Medidas
        {
            Balanco = proj, BaseNaFace = baixo[0].C, TopoNaFace = cima[0].C, AlturaPonta = ct - cb,
            ChanfroInferior = chB, ChanfroSuperior = chT,
        };
    }

    /// <summary>Cadeia convexa (monotônica): <paramref name="lado"/> −1 = de baixo, +1 = de cima.</summary>
    private static List<(double D, double C)> Envoltoria(List<(double D, double C)> p, int lado)
    {
        var h = new List<(double D, double C)>();
        foreach (var q in p)
        {
            while (h.Count >= 2)
            {
                var (ad, ac) = h[h.Count - 2];
                var (bd, bc) = h[h.Count - 1];
                double cr = (bd - ad) * (q.C - ac) - (bc - ac) * (q.D - ad);
                if (lado * cr >= -1e-9) h.RemoveAt(h.Count - 1); else break;
            }
            h.Add(q);
        }
        return h;
    }

    /// <summary>
    /// c da reta principal na ponta e o chanfro: se o último trecho do contorno é curto (≤ limite) e há um trecho antes dele,
    /// a reta principal é a do penúltimo trecho, prolongada até a ponta.
    /// </summary>
    private static (double C, (double D, double C) Chanfro) NaPonta(List<(double D, double C)> e, double proj)
    {
        int n = e.Count;
        if (n < 3) return (e[n - 1].C, (0, 0));
        var (d1, c1) = e[n - 1];
        var (d0, c0) = e[n - 2];
        if (d1 - d0 > LimiteChanfro + 1e-6) return (c1, (0, 0));
        var (da, ca) = e[n - 3];
        double inc = (c0 - ca) / Math.Max(1e-9, d0 - da), incUlt = (c1 - c0) / Math.Max(1e-9, d1 - d0);
        if (Math.Abs(inc - incUlt) < 1e-3) return (c1, (0, 0));   // mesma reta: não é chanfro
        double c = c0 + inc * (proj - d0);
        return (c, (d1 - d0, Math.Abs(c1 - c)));
    }
}
