using System;
using System.Collections.Generic;
using System.Linq;
using Armadura.Cad.Infra;
using Armadura.Core.Configuracao;
using Armadura.Core.Estribos;
using Armadura.Core.Modelo;
using Armadura.Core.Persistencia;

namespace Armadura.Cad.Barra;

/// <summary>
/// Estribo fechado com ganchos a partir de uma poligonal fechada (qualquer forma): a barra marcada
/// como estribo ganha os ganchos num vértice (padrão: o superior mais à esquerda), no padrão do
/// escritório (passagem do canto = pino + 2φ, gancho 90° perpendicular, 45°/180° em diagonal).
/// </summary>
internal static class ServicoEstribo
{
    /// <summary>Poligonal fechada sem arcos (Closed, ou o último vértice sobre o primeiro), com 3+ vértices.</summary>
    public static List<(double X, double Y)> PoligonoFechado(Polyline pl)
    {
        int n = pl.NumberOfVertices;
        if (n < 3) return null;
        for (int i = 0; i < GeometriaBarra.NumeroSegmentos(pl); i++) if (GeometriaBarra.EhArco(pl, i)) return null;
        var pts = Enumerable.Range(0, n).Select(i => pl.GetPoint2dAt(i)).Select(p => (p.X, p.Y)).ToList();
        bool fechada = pl.Closed || pl.GetPoint2dAt(0).GetDistanceTo(pl.GetPoint2dAt(n - 1)) < 1e-6;
        if (!fechada) return null;
        if (!pl.Closed) pts.RemoveAt(pts.Count - 1);
        return pts.Count >= 3 ? pts : null;
    }

    /// <summary>A barra já é um estribo fechado com ganchos (gerado por este serviço ou pelo ARMESTRIBO).</summary>
    public static bool ComGanchos(Polyline pl, DadosArmadura d)
    {
        int s = d.Segmentos.Count;
        if (pl.Closed || s < 7 || pl.NumberOfVertices != s + 1) return false;
        if (!d.Segmentos[1].Sobreposto || !d.Segmentos[s - 2].Sobreposto) return false;
        return Volta(pl) != null;
    }

    private static List<(double X, double Y)> Volta(Polyline pl) =>
        GeradorEstribo.PoligonoDoEstribo(Enumerable.Range(0, pl.NumberOfVertices).Select(i => pl.GetPoint2dAt(i)).Select(p => (p.X, p.Y)).ToList());

    /// <summary>Gancho atual de um estribo com ganchos.</summary>
    public static FormatoSegmento Gancho(DadosArmadura d) =>
        d.Segmentos.Count > 0 && Segmento.EhGancho(d.Segmentos[0].Formato) ? d.Segmentos[0].Formato : FormatoSegmento.Gancho90;

    /// <summary>Posição atual dos ganchos (null = não está em nenhum dos 4 cantos extremos).</summary>
    public static PosicaoGanchos? Posicao(Polyline pl)
    {
        var v = Volta(pl);
        return v == null ? null : GeradorEstribo.PosicaoDoCanto(v, 0);
    }

    /// <summary>
    /// Transforma a barra (poligonal fechada, ou estribo com ganchos que muda de gancho/canto/bitola)
    /// no estribo fechado com ganchos. <paramref name="posicao"/>: onde ficam os ganchos; null = o canto
    /// atual (estribo com ganchos) ou o superior mais à esquerda (poligonal fechada).
    /// Retorna a mensagem de erro, ou null. Não chama <see cref="ServicoBarra.Atualizar"/>.
    /// </summary>
    public static string Converter(Transaction tr, Polyline pl, DadosArmadura d, ConfiguracaoArmadura cfg,
        FormatoSegmento? gancho = null, PosicaoGanchos? posicao = null)
    {
        List<(double X, double Y)> poligono;
        int c;
        if (ComGanchos(pl, d))
        {
            poligono = Volta(pl);
            c = posicao.HasValue ? GeradorEstribo.Canto(poligono, posicao.Value, 0) : 0;
            gancho ??= Gancho(d);
        }
        else
        {
            poligono = PoligonoFechado(pl);
            if (poligono == null) return "Só uma polilinha FECHADA, sem arcos, vira estribo com ganchos.";
            c = GeradorEstribo.Canto(poligono, posicao ?? PosicaoGanchos.SuperiorEsquerdo);
            gancho ??= cfg.UltimoEstribo?.Gancho ?? FormatoSegmento.Gancho90;
        }
        if (!Segmento.EhGancho(gancho.Value)) gancho = FormatoSegmento.Gancho90;
        double cpu = Desenho.CmPorUnidade(pl.Database, cfg);
        double lg = cfg.Dobramento.ComprimentoGancho(gancho.Value, d.Bitola, d.Aco, estribo: true) / cpu;
        double passagem = (cfg.Dobramento.DiametroPino(d.Bitola, d.Aco, estribo: true) + 2 * d.Bitola / 10.0) / cpu;
        var forma = GeradorEstribo.FechadoPoligono(poligono, c, gancho.Value, lg, passagem);

        if (!pl.IsWriteEnabled) pl.UpgradeOpen();
        ApagarCotas(tr, pl.Database, d);
        TrocarVertices(pl, forma.Vertices.Select(v => (v.X, v.Y)).ToList(), fechada: false);
        d.Estribo = true;
        d.Segmentos.Clear();
        for (int s = 0; s < forma.Formatos.Count; s++)
        {
            bool g = Segmento.EhGancho(forma.Formatos[s]);
            bool sob = forma.Sobrepostos.Contains(s);
            d.Segmentos.Add(new Segmento
            {
                Formato = forma.Formatos[s], Sobreposto = sob, Cotar = !g && !sob && forma.Cotados.Contains(s),
            });
        }
        XDataCad.Gravar(pl, CodecDados.Codificar(d));
        return null;
    }

    /// <summary>Estribo com ganchos → poligonal fechada (deixou de ser estribo).</summary>
    public static void Desfazer(Transaction tr, Polyline pl, DadosArmadura d)
    {
        var volta = Volta(pl);
        if (volta == null) return;
        if (!pl.IsWriteEnabled) pl.UpgradeOpen();
        ApagarCotas(tr, pl.Database, d);
        TrocarVertices(pl, volta, fechada: true);
        d.Segmentos.Clear();
        for (int s = 0; s < volta.Count; s++) d.Segmentos.Add(new Segmento());
        XDataCad.Gravar(pl, CodecDados.Codificar(d));
    }

    private static void ApagarCotas(Transaction tr, Database db, DadosArmadura d)
    {
        foreach (var s in d.Segmentos.Where(s => !string.IsNullOrEmpty(s.HandleCota)))
        {
            var id = XDataCad.IdDoHandle(db, s.HandleCota);
            if (id.IsNull || id.IsErased || !(tr.GetObject(id, OpenMode.ForWrite) is DBText t)) continue;
            if (XDataCad.Tipo(t) == TipoParte.Cota) t.Erase();
        }
    }

    /// <summary>Troca todos os vértices (sem bulge) mantendo a entidade (handle, XData, layer).</summary>
    private static void TrocarVertices(Polyline pl, List<(double X, double Y)> pts, bool fechada)
    {
        int antigos = pl.NumberOfVertices;
        pl.Closed = false;
        foreach (var p in pts) pl.AddVertexAt(pl.NumberOfVertices, new Point2d(p.X, p.Y), 0, 0, 0);
        for (int i = 0; i < antigos; i++) pl.RemoveVertexAt(0);
        pl.Closed = fechada;
    }
}
