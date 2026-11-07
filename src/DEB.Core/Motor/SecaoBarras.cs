using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Normas;
using DEB.Core.Tabelas;

namespace DEB.Core.Motor;

/// <summary>Barra longitudinal na seção (coordenadas da seção: origem no canto inferior esquerdo, x ao longo de b).</summary>
public readonly struct BarraNaSecao
{
    public BarraNaSecao(double x, double y, double bitola, string grupo, int feixe = 1, string arranjo = "")
    {
        X = x; Y = y; Bitola = bitola; Grupo = grupo; Feixe = feixe; Arranjo = arranjo;
    }
    /// <summary>Centro da barra (ou do feixe).</summary>
    public double X { get; }
    public double Y { get; }
    public double Bitola { get; }
    /// <summary>Linha da lista do pilar ("L1"…); em <see cref="SecaoBarras.Posicoes"/>: "canto", "vizinha", "faceB" ou "faceH".</summary>
    public string Grupo { get; }
    public int Feixe { get; }
    public string Arranjo { get; }
}

/// <summary>Trecho de uma barra emendada, ao longo dela (cm a partir do início).</summary>
public readonly struct PecaBarra
{
    public PecaBarra(double ini, double fim) { Ini = ini; Fim = fim; }
    public double Ini { get; }
    public double Fim { get; }
    public double Comprimento => Fim - Ini;
}

/// <summary>Peça de um grupo emendado: o trecho, quantas barras e se é do grupo alternado (B).</summary>
public readonly struct PecaEmendada
{
    public PecaEmendada(PecaBarra peca, int quantidade, bool alternada) { Peca = peca; Quantidade = quantidade; Alternada = alternada; }
    public PecaBarra Peca { get; }
    public int Quantidade { get; }
    public bool Alternada { get; }
}

/// <summary>
/// Barras longitudinais de uma seção retangular (pilar, arranque, viga): posições na seção (cantos com 1 ou 3 barras em
/// "L", intermediárias nas faces) e emendas pelo comprimento comercial (traspasse do escritório, peças, grupos
/// alternados). Antes ficava no template do pilar pré-moldado; extraído em 2026-11-07 para o motor (componente
/// <c>barras_secao_longitudinal</c> e arranque do bloco) — o pilar chama daqui.
/// </summary>
public static class SecaoBarras
{
    /// <summary>
    /// Posições das longitudinais na seção. Cobrimentos por face (1 = +x, 2 = −y, 3 = −x, 4 = +y) até o estribo;
    /// φt = maior estribo (mm). Cantos com 1 barra ou 3 em "L" (a do canto e uma ao lado em cada face, a
    /// <paramref name="sCanto"/> entre eixos); intermediárias distribuídas por igual entre as do canto.
    /// </summary>
    public static List<BarraNaSecao> Posicoes(double b, double h, double[] cob, double phiT, double phiCanto, int porCanto,
        double sCanto, double phiViz, int nFaceB, int nFaceH, double phiFace)
    {
        double t = phiT / 10;
        double rc = phiCanto / 20;
        double xl = cob[2] + t + rc, xr = b - cob[0] - t - rc, yb = cob[1] + t + rc, yt = h - cob[3] - t - rc;
        var r = new List<BarraNaSecao>();
        foreach (var (x, y) in new[] { (xl, yb), (xr, yb), (xr, yt), (xl, yt) }) r.Add(new BarraNaSecao(x, y, phiCanto, "canto"));
        double ax = xl, bx = xr, ay = yb, by = yt; // limites das intermediárias
        if (porCanto >= 3)
        {
            double rv = phiViz / 20;
            // vizinhas: alinhadas à face (mesmo cobrimento até o estribo), a sCanto da barra do canto
            double ybv = cob[1] + t + rv, ytv = h - cob[3] - t - rv, xlv = cob[2] + t + rv, xrv = b - cob[0] - t - rv;
            r.Add(new BarraNaSecao(xl + sCanto, ybv, phiViz, "vizinha")); r.Add(new BarraNaSecao(xlv, yb + sCanto, phiViz, "vizinha"));
            r.Add(new BarraNaSecao(xr - sCanto, ybv, phiViz, "vizinha")); r.Add(new BarraNaSecao(xrv, yb + sCanto, phiViz, "vizinha"));
            r.Add(new BarraNaSecao(xr - sCanto, ytv, phiViz, "vizinha")); r.Add(new BarraNaSecao(xrv, yt - sCanto, phiViz, "vizinha"));
            r.Add(new BarraNaSecao(xl + sCanto, ytv, phiViz, "vizinha")); r.Add(new BarraNaSecao(xlv, yt - sCanto, phiViz, "vizinha"));
            ax = xl + sCanto; bx = xr - sCanto; ay = yb + sCanto; by = yt - sCanto;
        }
        double rf = phiFace / 20;
        for (int k = 1; k <= nFaceB; k++)
        {
            double x = ax + k * (bx - ax) / (nFaceB + 1);
            r.Add(new BarraNaSecao(x, cob[1] + t + rf, phiFace, "faceB"));
            r.Add(new BarraNaSecao(x, h - cob[3] - t - rf, phiFace, "faceB"));
        }
        for (int k = 1; k <= nFaceH; k++)
        {
            double y = ay + k * (by - ay) / (nFaceH + 1);
            r.Add(new BarraNaSecao(cob[2] + t + rf, y, phiFace, "faceH"));
            r.Add(new BarraNaSecao(b - cob[0] - t - rf, y, phiFace, "faceH"));
        }
        return r;
    }

    /// <summary>Traspasse do escritório: o da NBR 6118 (fck e % emendada na mesma seção), nunca menor que 45φ, arredondado a 5 cm.</summary>
    public static double Traspasse(double bitola, string aco, double fck, bool mesmaSecao, TabelaBarras barras)
    {
        var regras = new RegrasTraspasse { Fck = fck, PercentualEmendado = mesmaSecao ? 100 : 50, Modo = ModoTraspasse.Calculado };
        double norma = regras.Comprimento(bitola, aco, barras);
        double l = Math.Max(norma, 45 * bitola / 10);
        return Math.Ceiling(l / 5 - 1e-9) * 5;
    }

    /// <summary>
    /// Peças de uma barra de <paramref name="ini"/> a <paramref name="fim"/> com emendas: a 1ª tem
    /// <paramref name="primeira"/>, as seguintes começam um traspasse antes do fim da anterior e têm até o comercial.
    /// </summary>
    public static List<PecaBarra> Pecas(double ini, double fim, double comercial, double traspasse, double primeira)
    {
        var r = new List<PecaBarra>();
        if (fim - ini <= comercial + 1e-6 || traspasse >= comercial) { r.Add(new PecaBarra(ini, fim)); return r; }
        double a = ini, b = Math.Min(fim, ini + Math.Min(primeira, comercial));
        r.Add(new PecaBarra(a, b));
        while (b < fim - 1e-6)
        {
            a = b - traspasse;
            b = Math.Min(fim, a + comercial);
            r.Add(new PecaBarra(a, b));
        }
        return r;
    }

    /// <summary>1ª peça do grupo alternado (B): emendas afastadas de pelo menos 1,3 traspasse (NBR 6118, 9.5.2.1).</summary>
    public static double PrimeiraAlternada(double comercial, double traspasse) =>
        Math.Floor((comercial - Math.Ceiling(1.3 * traspasse / 5) * 5) / 5) * 5;

    /// <summary>
    /// Peças de <paramref name="nBarras"/> barras iguais de <paramref name="ini"/> a <paramref name="fim"/>: inteiras se
    /// couberem no comercial; senão, emendadas — todas na mesma seção, ou alternadas (metade, arredondada para cima, no
    /// grupo A começando com o comercial; o resto no grupo B começando com <see cref="PrimeiraAlternada"/>).
    /// </summary>
    public static List<PecaEmendada> Emendadas(double ini, double fim, int nBarras, double comercial, double traspasse, bool mesmaSecao)
    {
        var r = new List<PecaEmendada>();
        if (nBarras <= 0) return r;
        if (fim - ini <= comercial + 1e-6) { r.Add(new PecaEmendada(new PecaBarra(ini, fim), nBarras, false)); return r; }
        int nA = mesmaSecao ? nBarras : (nBarras + 1) / 2, nB = nBarras - nA;
        r.AddRange(Pecas(ini, fim, comercial, traspasse, comercial).Select(p => new PecaEmendada(p, nA, false)));
        if (nB > 0)
            r.AddRange(Pecas(ini, fim, comercial, traspasse, PrimeiraAlternada(comercial, traspasse)).Select(p => new PecaEmendada(p, nB, true)));
        return r;
    }
}
