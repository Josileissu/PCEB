using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Estribos;
using DEB.Core.Modelo;

namespace DEB.Core.Calculo;

/// <summary>
/// Comprimento real de uma barra descrita pelo EIXO (pontos em cm, 3D), como os modeladores 3D (Revit,
/// futuramente Blender/FreeCAD) a conhecem. Converte o eixo nas medidas EXTERNAS usadas pelo CAD e aplica
/// as mesmas regras (<see cref="CalculoComprimento.ComprimentoUnitario"/>): pinos, ganchos pela norma,
/// passagens do estribo fechado e arredondamento da configuração.
/// </summary>
public static class ComprimentoPorEixo
{
    /// <param name="eixo">vértices do eixo da barra, em cm (sem repetir o 1º no fim quando fechada)</param>
    /// <param name="fechada">poligonal fechada (estribo)</param>
    /// <param name="ganchos135">ganchos de 135° nas pontas (estribo fechado ou grampo), como no padrão do escritório</param>
    public static double Calcular(IReadOnlyList<(double X, double Y, double Z)> eixo, bool fechada, double bitolaMm,
        string aco, bool estribo, bool ganchos135, ConfiguracaoArmadura cfg)
    {
        var (geo, dados) = Montar(eixo, fechada, bitolaMm, aco, estribo, ganchos135, cfg);
        return CalculoComprimento.ComprimentoUnitario(geo, cfg, dados);
    }

    /// <summary>Segmentos (medidas externas) e dados da barra equivalentes ao desenho do CAD.</summary>
    public static (List<GeoSegmento> geo, DadosArmadura dados) Montar(IReadOnlyList<(double X, double Y, double Z)> eixo,
        bool fechada, double bitolaMm, string aco, bool estribo, bool ganchos135, ConfiguracaoArmadura cfg)
    {
        if (eixo == null || eixo.Count < 2) throw new ArgumentException("O eixo precisa de pelo menos 2 pontos.", nameof(eixo));
        double phi = bitolaMm / 10.0;
        var dados = new DadosArmadura { Bitola = bitolaMm, Aco = aco, Estribo = estribo };
        var regras = cfg.Dobramento;

        if (ganchos135 && fechada && eixo.Count >= 3)
        {
            // estribo fechado: poligonal externa (eixo afastado φ/2 para fora) no gerador do CAD
            var poligono = Afastar(NoPlano(eixo), phi / 2);
            var forma = GeradorEstribo.FechadoPadrao(poligono, bitolaMm, aco, regras, FormatoSegmento.Gancho45);
            var geo = Segmentos(forma.Vertices, fechada: false);
            for (int i = 0; i < geo.Count; i++)
            {
                bool sob = forma.Sobrepostos.Contains(i);
                geo[i].Sobreposto = sob;
                dados.Segmentos.Add(new Segmento { Formato = i < forma.Formatos.Count ? forma.Formatos[i] : FormatoSegmento.Linha, Sobreposto = sob });
            }
            return (geo, dados);
        }

        if (ganchos135 && !fechada && eixo.Count == 2)
        {
            // grampo: gancho, trecho reto (medida externa = eixo + φ), gancho
            double lg = regras.ComprimentoGancho(FormatoSegmento.Gancho45, bitolaMm, aco, estribo: true);
            var geo = new List<GeoSegmento>
            {
                new GeoSegmento(TipoSegmento.Reta, lg),
                new GeoSegmento(TipoSegmento.Reta, Distancia(eixo[0], eixo[1]) + phi, 135),
                new GeoSegmento(TipoSegmento.Reta, lg, 135),
            };
            foreach (var f in new[] { FormatoSegmento.Gancho45, FormatoSegmento.Linha, FormatoSegmento.Gancho45 })
                dados.Segmentos.Add(new Segmento { Formato = f });
            return (geo, dados);
        }

        // poligonal sem ganchos: cada trecho ganha (φ/2)·tan(δ/2) em cada ponta dobrada (medida até a face externa)
        int n = eixo.Count, nSeg = fechada ? n : n - 1;
        double Deflexao(int vertice)
        {
            if (!fechada && (vertice == 0 || vertice == n - 1)) return 0;
            var a = eixo[(vertice - 1 + n) % n]; var b = eixo[vertice]; var c = eixo[(vertice + 1) % n];
            return Angulo(Sub(b, a), Sub(c, b));
        }
        var r = new List<GeoSegmento>();
        for (int i = 0; i < nSeg; i++)
        {
            int ini = i, fim = (i + 1) % n;
            double di = Deflexao(ini), df = Deflexao(fim);
            double ext = Distancia(eixo[ini], eixo[fim]) + phi / 2 * (Math.Tan(di / 2) + Math.Tan(df / 2));
            r.Add(new GeoSegmento(TipoSegmento.Reta, ext, (i == 0 && !fechada) ? 0 : di * 180 / Math.PI));
            dados.Segmentos.Add(new Segmento());
        }
        return (r, dados);
    }

    /// <summary>
    /// Segmentos de uma polilinha 2D com bulge (mesma convenção do CAD: bulge = tan(θ/4), + = anti-horário).
    /// Comprimentos nas unidades dos vértices; deflexões em graus, com sinal.
    /// </summary>
    public static List<GeoSegmento> Segmentos(IReadOnlyList<VerticeEstribo> v, bool fechada)
    {
        int n = fechada ? v.Count : v.Count - 1;
        var r = new List<GeoSegmento>(Math.Max(0, n));
        (double X, double Y)? fimAnterior = null;
        (double X, double Y) iniPrimeiro = default, fimUltimo = default;
        for (int i = 0; i < n; i++)
        {
            var a = v[i]; var b = v[(i + 1) % v.Count];
            double cx = b.X - a.X, cy = b.Y - a.Y, corda = Math.Sqrt(cx * cx + cy * cy);
            double theta = 4 * Math.Atan(a.Bulge);
            (double X, double Y) ini = Girar(cx, cy, -theta / 2), fim = Girar(cx, cy, theta / 2);
            double comp = Math.Abs(theta) < 1e-12 ? corda : corda / (2 * Math.Sin(Math.Abs(theta) / 2)) * Math.Abs(theta);
            double defl = fimAnterior.HasValue ? AnguloComSinal(fimAnterior.Value, ini) * 180 / Math.PI : 0;
            r.Add(new GeoSegmento(Math.Abs(theta) < 1e-12 ? TipoSegmento.Reta : TipoSegmento.Arco, comp, defl, theta * 180 / Math.PI));
            fimAnterior = fim;
            if (i == 0) iniPrimeiro = ini;
            fimUltimo = fim;
        }
        if (fechada && r.Count > 1) r[0].Deflexao = AnguloComSinal(fimUltimo, iniPrimeiro) * 180 / Math.PI;
        return r;
    }

    // ---------------------------------------------------------------- geometria

    /// <summary>Pontos de uma poligonal plana 3D em coordenadas 2D do seu plano.</summary>
    private static List<(double X, double Y)> NoPlano(IReadOnlyList<(double X, double Y, double Z)> p)
    {
        var u = Unit(Sub(p[1], p[0]));
        var normal = (X: 0.0, Y: 0.0, Z: 0.0);
        for (int i = 2; i < p.Count && Len(normal) < 1e-9; i++) normal = Cross(Sub(p[1], p[0]), Sub(p[i], p[0]));
        if (Len(normal) < 1e-9) throw new ArgumentException("Poligonal sem área (pontos alinhados).");
        normal = Unit(normal);
        var w = Cross(normal, u);
        return p.Select(q => { var d = Sub(q, p[0]); return (Dot(d, u), Dot(d, w)); }).ToList();
    }

    /// <summary>Afasta uma poligonal fechada convexa d para fora (vértices em esquadro, como as faces externas).</summary>
    private static List<(double X, double Y)> Afastar(List<(double X, double Y)> p, double d)
    {
        int n = p.Count;
        double area = 0;
        for (int i = 0; i < n; i++) area += p[i].X * p[(i + 1) % n].Y - p[(i + 1) % n].X * p[i].Y;
        double s = area > 0 ? 1 : -1; // anti-horário: normal externa = (dy, -dx)
        (double X, double Y) Normal(int i)
        {
            var a = p[i]; var b = p[(i + 1) % n];
            double dx = b.X - a.X, dy = b.Y - a.Y, l = Math.Sqrt(dx * dx + dy * dy);
            return (s * dy / l, -s * dx / l);
        }
        var r = new List<(double X, double Y)>(n);
        for (int i = 0; i < n; i++)
        {
            var n1 = Normal((i - 1 + n) % n); var n2 = Normal(i);
            double k = d / (1 + n1.X * n2.X + n1.Y * n2.Y);
            r.Add((p[i].X + (n1.X + n2.X) * k, p[i].Y + (n1.Y + n2.Y) * k));
        }
        return r;
    }

    private static (double X, double Y) Girar(double x, double y, double a) =>
        (x * Math.Cos(a) - y * Math.Sin(a), x * Math.Sin(a) + y * Math.Cos(a));
    private static double AnguloComSinal((double X, double Y) a, (double X, double Y) b) =>
        Math.Atan2(a.X * b.Y - a.Y * b.X, a.X * b.X + a.Y * b.Y);
    private static (double X, double Y, double Z) Sub((double X, double Y, double Z) a, (double X, double Y, double Z) b) => (a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static (double X, double Y, double Z) Cross((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static double Dot((double X, double Y, double Z) a, (double X, double Y, double Z) b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static double Len((double X, double Y, double Z) a) => Math.Sqrt(Dot(a, a));
    private static (double X, double Y, double Z) Unit((double X, double Y, double Z) a) { double l = Len(a); return (a.X / l, a.Y / l, a.Z / l); }
    private static double Distancia((double X, double Y, double Z) a, (double X, double Y, double Z) b) => Len(Sub(b, a));
    private static double Angulo((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        Math.Acos(Math.Max(-1, Math.Min(1, Dot(a, b) / (Len(a) * Len(b)))));
}
