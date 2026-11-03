using System;
using System.Collections.Generic;
using System.Linq;
using Armadura.Core.Modelo;

namespace Armadura.Core.RelacaoAco;

/// <summary>Um texto do esquema (medida de um segmento).</summary>
public sealed class RotuloEsquema
{
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Rotação (rad), sempre legível (entre −90° e +90°).</summary>
    public double Rotacao { get; set; }
    public string Texto { get; set; } = "";
}

/// <summary>Esquema da barra dentro de uma caixa (coordenadas locais; origem no centro da caixa).</summary>
public sealed class Esquema
{
    public List<(double X, double Y, double Bulge)> Vertices { get; } = new List<(double, double, double)>();
    public List<RotuloEsquema> Rotulos { get; } = new List<RotuloEsquema>();
    public bool Fechada { get; set; }
}

/// <summary>
/// Detalhe de dobra (prévia da forma da barra) para a relação do aço e a lista de corte e dobra.
/// A forma usa os ângulos reais; os comprimentos são "comprimidos" (raiz quadrada, com mínimo) para
/// que ganchos curtos apareçam ao lado de trechos longos. Cada segmento leva a medida real (cm).
/// O maior trecho reto fica na horizontal.
/// </summary>
public static class EsquemaDobra
{
    /// <summary>Menor fração do maior segmento que um segmento ocupa no esquema.</summary>
    public const double FracaoMinima = 0.12;

    /// <param name="largura">largura da caixa</param>
    /// <param name="altura">altura da caixa</param>
    /// <param name="alturaTexto">altura dos textos (define as margens)</param>
    /// <param name="texto">texto da medida de cada segmento (índice, comprimento em cm)</param>
    public static Esquema Gerar(IReadOnlyList<GeoSegmento> geo, bool fechada, double largura, double altura,
        double alturaTexto, Func<int, double, string> texto)
    {
        var r = new Esquema { Fechada = fechada };
        if (geo == null || geo.Count == 0) return r;
        double lmax = geo.Max(s => s.Comprimento);
        if (lmax <= 0) return r;
        double Comprimir(double l) => lmax * Math.Max(FracaoMinima, Math.Sqrt(Math.Max(0, l) / lmax));

        // 1. caminho "tartaruga" com os comprimentos comprimidos
        var pts = new List<(double X, double Y)> { (0, 0) };
        var bulges = new List<double>();
        var meios = new List<(double X, double Y, double Dir)>();
        double rumo = 0, x = 0, y = 0;
        int maiorReto = -1;
        double maiorL = -1, rumoMaior = 0;
        for (int i = 0; i < geo.Count; i++)
        {
            var s = geo[i];
            if (i > 0) rumo += s.Deflexao * Math.PI / 180.0;
            double lc = Comprimir(s.Comprimento);
            if (s.Tipo == TipoSegmento.Arco && Math.Abs(s.AnguloArco) > 1e-6)
            {
                double th = s.AnguloArco * Math.PI / 180.0;
                double corda = lc * Math.Sin(Math.Abs(th) / 2) / (Math.Abs(th) / 2);
                double dir = rumo + th / 2;
                double nx = x + corda * Math.Cos(dir), ny = y + corda * Math.Sin(dir);
                // meio do arco: no meio da corda, deslocado pela flecha
                double flecha = corda / 2 * Math.Tan(Math.Abs(th) / 4);
                double sgn = Math.Sign(th);
                meios.Add(((x + nx) / 2 + flecha * Math.Sin(dir) * sgn, (y + ny) / 2 - flecha * Math.Cos(dir) * sgn, dir));
                bulges.Add(Math.Tan(th / 4));
                x = nx; y = ny;
                rumo += th;
            }
            else
            {
                double nx = x + lc * Math.Cos(rumo), ny = y + lc * Math.Sin(rumo);
                meios.Add(((x + nx) / 2, (y + ny) / 2, rumo));
                bulges.Add(0);
                if (s.Comprimento > maiorL) { maiorL = s.Comprimento; maiorReto = i; rumoMaior = rumo; }
                x = nx; y = ny;
            }
            pts.Add((x, y));
        }
        if (fechada && pts.Count > 1 && Dist(pts[0], pts[pts.Count - 1]) < 1e-9) pts.RemoveAt(pts.Count - 1);

        // 2. gira para o maior trecho reto ficar na horizontal
        double giro = maiorReto >= 0 ? -rumoMaior : 0;
        (double, double) Girar((double X, double Y) p) =>
            (p.X * Math.Cos(giro) - p.Y * Math.Sin(giro), p.X * Math.Sin(giro) + p.Y * Math.Cos(giro));
        pts = pts.Select(Girar).ToList();
        var meiosG = meios.Select(m => { var (gx, gy) = Girar((m.X, m.Y)); return (X: gx, Y: gy, Dir: m.Dir + giro); }).ToList();

        // 3. encaixa na caixa (margem para os textos) e centraliza
        var todos = pts.Concat(meiosG.Select(m => (m.X, m.Y))).ToList();
        double minX = todos.Min(p => p.Item1), maxX = todos.Max(p => p.Item1);
        double minY = todos.Min(p => p.Item2), maxY = todos.Max(p => p.Item2);
        double margem = 1.8 * alturaTexto;
        double w = Math.Max(1e-9, largura - 2 * margem), h = Math.Max(1e-9, altura - 2 * margem);
        double ex = maxX - minX, ey = maxY - minY;
        double escala = Math.Min(ex > 1e-9 ? w / ex : double.MaxValue, ey > 1e-9 ? h / ey : double.MaxValue);
        if (escala == double.MaxValue) escala = 1;
        double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
        (double, double) Ajustar(double px, double py) => ((px - cx) * escala, (py - cy) * escala);

        for (int i = 0; i < pts.Count; i++)
        {
            var (ax, ay) = Ajustar(pts[i].Item1, pts[i].Item2);
            r.Vertices.Add((ax, ay, i < bulges.Count ? bulges[i] : 0));
        }

        // 4. medidas: no meio de cada segmento, do lado de fora do desenho, sempre legíveis
        for (int i = 0; i < meiosG.Count; i++)
        {
            if (geo[i].Sobreposto) continue; // trecho só de desenho (canto do estribo): sem medida
            var (mx, my) = Ajustar(meiosG[i].X, meiosG[i].Y);
            double dir = meiosG[i].Dir;
            double ux = Math.Cos(dir), uy = Math.Sin(dir);
            if (ux < -1e-9 || (Math.Abs(ux) <= 1e-9 && uy < 0)) { ux = -ux; uy = -uy; }
            double nx = -uy, ny = ux; // "acima" do texto
            if (mx * nx + my * ny < -1e-9) { nx = -nx; ny = -ny; } // para fora (o centro da caixa é a origem)
            double off = 0.9 * alturaTexto;
            r.Rotulos.Add(new RotuloEsquema
            {
                X = mx + nx * off, Y = my + ny * off, Rotacao = Math.Atan2(uy, ux), Texto = texto(i, geo[i].Comprimento),
            });
        }
        return r;
    }

    private static double Dist((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
