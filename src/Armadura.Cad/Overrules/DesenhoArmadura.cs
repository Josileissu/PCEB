using System;
using System.Collections.Generic;
using Armadura.Cad.Barra;
using Armadura.Cad.Infra;

namespace Armadura.Cad.Overrules;

/// <summary>
/// Desenho da barra na tela: com "Representar curvatura", as quinas entre segmentos retos viram
/// arcos com o raio da linha média da dobra (pino/2 + φ/2). Só muda a exibição; a polilinha
/// gravada continua com as quinas (o DWG sem o plugin mostra a poligonal).
/// Filtro: só polilinhas com XData ARMADURA_BR.
/// </summary>
internal sealed class DesenhoArmadura : Gi.DrawableOverrule
{
    private static DesenhoArmadura _instancia;

    public static void Registrar()
    {
        if (_instancia != null) return;
        _instancia = new DesenhoArmadura();
        _instancia.SetXDataFilter(XDataCad.App);
        Rt.Overrule.AddOverrule(Rt.RXObject.GetClass(typeof(Polyline)), _instancia, false);
        Rt.Overrule.Overruling = true;
    }

    public static void Remover()
    {
        if (_instancia == null) return;
        try { Rt.Overrule.RemoveOverrule(Rt.RXObject.GetClass(typeof(Polyline)), _instancia); }
        catch (Exception ex) { Log.Erro("RemoverOverrule", ex); }
        _instancia = null;
    }

    public override bool WorldDraw(Gi.Drawable drawable, Gi.WorldDraw wd)
    {
        try
        {
            var cfg = drawable is Polyline p0 ? ConfigDesenho.ObterSemLer(p0.Database) : Plugin.Config;
            if (cfg != null && cfg.RepresentarCurvatura && drawable is Polyline pl && pl.Database != null)
            {
                var d = XDataCad.LerBarra(pl);
                // cordoalhas não são dobradas: desenho normal
                if (d != null && d.EhBarraComum && !Core.Tabelas.TabelaBarras.EhCordoalha(d.Aco) && DesenharComCurvas(pl, d, cfg, wd)) return true;
            }
        }
        catch (Exception ex) { Log.Erro("DesenhoArmadura.WorldDraw", ex); }
        return base.WorldDraw(drawable, wd);
    }

    private static bool DesenharComCurvas(Polyline pl, Core.Modelo.DadosArmadura d,
        Core.Configuracao.ConfiguracaoArmadura cfg, Gi.WorldDraw wd)
    {
        int n = GeometriaBarra.NumeroSegmentos(pl);
        if (n < 2) return false;
        for (int i = 0; i < n; i++) if (GeometriaBarra.EhArco(pl, i)) return false; // com arcos: desenho normal

        double cpu = Desenho.CmPorUnidade(pl.Database, cfg);
        double raio = (cfg.Dobramento.DiametroPino(d.Bitola, d.Aco, d.Estribo) / 2.0 + d.Bitola / 20.0) / cpu;
        int nv = pl.NumberOfVertices;
        var p = new List<Point2d>();
        for (int i = 0; i < nv; i++) p.Add(pl.GetPoint2dAt(i));

        // recuo em cada vértice interno (e em todos, se fechada)
        var recuo = new double[nv];
        var centro = new Point2d?[nv];
        var ang = new double[nv];
        for (int v = 0; v < nv; v++)
        {
            if (!pl.Closed && (v == 0 || v == nv - 1)) continue;
            var a = p[(v - 1 + nv) % nv];
            var b = p[v];
            var c = p[(v + 1) % nv];
            var u1 = b - a; var u2 = c - b;
            if (u1.Length < 1e-9 || u2.Length < 1e-9) continue;
            double defl = u1.GetAngleTo(u2); // 0..π
            if (defl < 1e-3 || defl > Math.PI - 1e-3) continue;
            double t = raio * Math.Tan(defl / 2);
            // cabe? no máximo metade de cada segmento adjacente
            if (t > 0.5 * u1.Length || t > 0.5 * u2.Length) continue;
            recuo[v] = t;
            var dirBis = (u2.GetNormal() - u1.GetNormal());
            if (dirBis.Length < 1e-12) continue;
            double distCentro = raio / Math.Cos(defl / 2);
            centro[v] = b + dirBis.GetNormal() * distCentro;
            ang[v] = defl;
        }

        double z = pl.Elevation;
        Point3d P(Point2d q) => new Point3d(q.X, q.Y, z);
        for (int i = 0; i < n; i++)
        {
            int a = i, b = (i + 1) % nv;
            var u = (p[b] - p[a]).GetNormal();
            var ini = p[a] + u * recuo[a];
            var fim = p[b] - u * recuo[b];
            wd.Geometry.WorldLine(P(ini), P(fim));
        }
        for (int v = 0; v < nv; v++)
        {
            if (centro[v] == null) continue;
            var a = p[(v - 1 + nv) % nv];
            var b = p[v];
            var c = p[(v + 1) % nv];
            var t1 = b + (a - b).GetNormal() * recuo[v];
            var t2 = b + (c - b).GetNormal() * recuo[v];
            var cc = centro[v].Value;
            wd.Geometry.CircularArc(P(t1), P(PontoMedioArco(cc, t1, t2, raio)), P(t2), Gi.ArcType.ArcSimple);
        }
        return true;
    }

    private static Point2d PontoMedioArco(Point2d c, Point2d t1, Point2d t2, double r)
    {
        var m = (t1 - c) + (t2 - c);
        return m.Length < 1e-12 ? t1 : c + m.GetNormal() * r;
    }
}
