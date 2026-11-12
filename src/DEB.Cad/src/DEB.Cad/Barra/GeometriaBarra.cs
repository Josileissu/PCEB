using System;
using System.Collections.Generic;
using DEB.Core.Modelo;

namespace DEB.Cad.Barra;

/// <summary>Leitura e edição da geometria da polilinha de uma barra.</summary>
internal static class GeometriaBarra
{
    public static int NumeroSegmentos(Polyline pl) =>
        pl.NumberOfVertices < 2 ? 0 : (pl.Closed ? pl.NumberOfVertices : pl.NumberOfVertices - 1);

    public static bool EhArco(Polyline pl, int i) => Math.Abs(pl.GetBulgeAt(i)) > 1e-9;

    /// <summary>Segmentos em cm, com os ângulos entre eles.</summary>
    public static List<GeoSegmento> Ler(Polyline pl, double cmPorUnidade)
    {
        int n = NumeroSegmentos(pl);
        var r = new List<GeoSegmento>(n);
        Vector2d? tangFimAnterior = null;
        for (int i = 0; i < n; i++)
        {
            var (ini, fim, comp, angArco) = Tangentes(pl, i);
            double defl = 0;
            if (tangFimAnterior.HasValue && ini.Length > 1e-12 && tangFimAnterior.Value.Length > 1e-12)
                defl = AnguloComSinal(tangFimAnterior.Value, ini) * 180.0 / Math.PI;
            r.Add(new GeoSegmento(EhArco(pl, i) ? TipoSegmento.Arco : TipoSegmento.Reta,
                comp * cmPorUnidade, defl, angArco * 180.0 / Math.PI));
            tangFimAnterior = fim;
        }
        // polilinha fechada: o 1º segmento dobra em relação ao último (dobra a mais no comprimento real)
        if (pl.Closed && n > 1)
        {
            var fimUltimo = Tangentes(pl, n - 1).fim;
            var iniPrimeiro = Tangentes(pl, 0).ini;
            if (fimUltimo.Length > 1e-12 && iniPrimeiro.Length > 1e-12)
                r[0].Deflexao = AnguloComSinal(fimUltimo, iniPrimeiro) * 180.0 / Math.PI;
        }
        // trechos sobrepostos (canto do estribo) vêm dos dados da barra
        var d = Infra.XDataCad.LerBarra(pl);
        if (d != null)
            for (int i = 0; i < r.Count && i < d.Segmentos.Count; i++) r[i].Sobreposto = d.Segmentos[i].Sobreposto;
        return r;
    }

    /// <summary>Tangente no início e no fim, comprimento (unidades do desenho) e ângulo central do arco.</summary>
    public static (Vector2d ini, Vector2d fim, double comp, double angArco) Tangentes(Polyline pl, int i)
    {
        if (!EhArco(pl, i))
        {
            var s = pl.GetLineSegment2dAt(i);
            var d = s.EndPoint - s.StartPoint;
            return (d, d, s.Length, 0);
        }
        double bulge = pl.GetBulgeAt(i);
        var a = pl.GetArcSegment2dAt(i);
        double theta = 4 * Math.Atan(bulge); // com sinal: + = anti-horário
        var corda = pl.GetPoint2dAt((i + 1) % pl.NumberOfVertices) - pl.GetPoint2dAt(i);
        return (corda.RotateBy(-theta / 2), corda.RotateBy(theta / 2), a.Radius * Math.Abs(theta), theta);
    }

    private static double AnguloComSinal(Vector2d a, Vector2d b) =>
        Math.Atan2(a.X * b.Y - a.Y * b.X, a.X * b.X + a.Y * b.Y);

    /// <summary>Ponto médio do segmento e direção (unitária) da tangente nesse ponto.</summary>
    public static (Point2d meio, Vector2d dir) Meio(Polyline pl, int i)
    {
        if (!EhArco(pl, i))
        {
            var s = pl.GetLineSegment2dAt(i);
            var d = s.EndPoint - s.StartPoint;
            return (s.StartPoint + d * 0.5, d.Length > 1e-12 ? d.GetNormal() : Vector2d.XAxis);
        }
        var p3 = pl.GetPointAtParameter(i + 0.5);
        var meio = new Point2d(p3.X, p3.Y);
        var (ini, _, _, theta) = Tangentes(pl, i);
        var dir = ini.RotateBy(theta / 2);
        return (meio, dir.Length > 1e-12 ? dir.GetNormal() : Vector2d.XAxis);
    }

    /// <summary>
    /// Muda o comprimento de um segmento reto mantendo a direção. No 1º segmento (de barra aberta)
    /// move o vértice inicial; nos demais, desloca todos os vértices seguintes.
    /// </summary>
    public static bool AlterarComprimento(Polyline pl, int i, double novoComprimento)
    {
        if (EhArco(pl, i) || novoComprimento <= 0) return false;
        var s = pl.GetLineSegment2dAt(i);
        var d = s.EndPoint - s.StartPoint;
        if (d.Length < 1e-12) return false;
        var u = d.GetNormal();
        int n = pl.NumberOfVertices;

        if (i == 0 && !pl.Closed && n > 2)
        {
            pl.SetPointAt(0, s.EndPoint - u * novoComprimento);
            return true;
        }
        // barra fechada: o último segmento termina no vértice 0, que não se move
        if (pl.Closed && i == n - 1) return false;
        var delta = (s.StartPoint + u * novoComprimento) - s.EndPoint;
        for (int j = i + 1; j < n; j++)
            pl.SetPointAt(j, pl.GetPoint2dAt(j) + delta);
        return true;
    }

    /// <summary>Bulge do arco que passa por p0, pm e p1.</summary>
    public static double BulgeTresPontos(Point2d p0, Point2d pm, Point2d p1)
    {
        // ângulo inscrito: o bulge = tan(θ/4), θ = 2·(π − ∠p0 pm p1) com o sinal da orientação
        var a = p0 - pm;
        var b = p1 - pm;
        double ang = Math.Abs(AnguloComSinal(a, b)); // ângulo em pm
        double theta = 2 * (Math.PI - ang);
        double cruz = (pm.X - p0.X) * (p1.Y - p0.Y) - (pm.Y - p0.Y) * (p1.X - p0.X);
        double sinal = cruz > 0 ? 1 : -1; // pm à direita da corda p0→p1 → arco anti-horário (bulge > 0)
        return sinal * Math.Tan(theta / 4);
    }
    /// <summary>Vértices com bulge (coordenadas do desenho), para o JOIN.</summary>
    public static List<Core.Calculo.VerticePoli> Vertices(Polyline pl)
    {
        var r = new List<Core.Calculo.VerticePoli>(pl.NumberOfVertices);
        for (int i = 0; i < pl.NumberOfVertices; i++)
        {
            var p = pl.GetPoint2dAt(i);
            r.Add(new Core.Calculo.VerticePoli(p.X, p.Y, pl.GetBulgeAt(i)));
        }
        return r;
    }

    /// <summary>Troca toda a geometria da polilinha (mesmo objeto: handle, XData e vínculos ficam).</summary>
    public static void DefinirVertices(Polyline pl, IReadOnlyList<Core.Calculo.VerticePoli> v)
    {
        int antigos = pl.NumberOfVertices;
        for (int i = 0; i < v.Count; i++) pl.AddVertexAt(antigos + i, new Point2d(v[i].X, v[i].Y), v[i].Bulge, 0, 0);
        for (int i = 0; i < antigos; i++) pl.RemoveVertexAt(0);
    }
}
