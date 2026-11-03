using System;
using System.Collections.Generic;
using System.Linq;
using Armadura.Cad.Barra;
using Armadura.Cad.Infra;
using Armadura.Core.Configuracao;
using Armadura.Core.Estribos;
using Armadura.Core.Modelo;
using Armadura.Core.Persistencia;

[assembly: Rt.CommandClass(typeof(Armadura.Cad.ComandosEstribo))]

namespace Armadura.Cad;

/// <summary>ARMESTRIBO / ARMESTRIBOREP (seção 3.1): seção → estribo rebatido (armadura) → faixas.</summary>
public sealed class ComandosEstribo
{
    [Rt.CommandMethod("ARMESTRIBO")]
    public void Estribo() => Comandos.Executar("ARMESTRIBO", (doc, ed, db, cfg) =>
    {
        var o = Copia(Plugin.Config.UltimoEstribo);
        using (var f = new Paleta.FormEstribo(o, cfg))
            if (CadApp.ShowModalDialog(f) != System.Windows.Forms.DialogResult.OK) return;
        Plugin.Config.UltimoEstribo = o;
        Plugin.SalvarConfig();
        Inserir(ed, db, cfg, o);
    });

    [Rt.CommandMethod("ARMESTRIBOREP")]
    public void Repetir() => Comandos.Executar("ARMESTRIBOREP", (doc, ed, db, cfg) =>
        Inserir(ed, db, cfg, Copia(Plugin.Config.UltimoEstribo)));

    private static OpcoesEstribo Copia(OpcoesEstribo o) => (o ?? new OpcoesEstribo()).Clonar();

    private static Point3d Wcs(Editor ed, Point3d p) => p.TransformBy(ed.CurrentUserCoordinateSystem);

    /// <summary>Fluxo de prompts: posição/dimensões → rebatido → faixas.</summary>
    private static void Inserir(Editor ed, Database db, ConfiguracaoArmadura cfg, OpcoesEstribo o)
    {
        double b, h, diametro = 0;
        Point2d cantoSecao; // canto superior esquerdo do ESTRIBO na seção
        double c = o.UsarSecao ? o.Cobrimento : 0;
        string oque = o.UsarSecao ? "da forma" : "do estribo";

        if (o.Circular)
        {
            double raio;
            Point3d centro;
            if (o.DimensoesFixas)
            {
                var r = ed.GetPoint($"\nQuadrante superior {oque}:");
                if (r.Status != PromptStatus.OK) return;
                raio = o.Diametro / 2;
                var q = Wcs(ed, r.Value);
                centro = new Point3d(q.X, q.Y - raio, q.Z);
            }
            else
            {
                var r = ed.GetPoint($"\nCentro {oque}:");
                if (r.Status != PromptStatus.OK) return;
                var rr = ed.GetDistance(new PromptDistanceOptions($"\nRaio {oque}:") { BasePoint = r.Value, UseBasePoint = true });
                if (rr.Status != PromptStatus.OK) return;
                centro = Wcs(ed, r.Value);
                raio = rr.Value;
            }
            raio -= c;
            if (raio <= 0) { ed.WriteMessage("\nO cobrimento é maior que o raio."); return; }
            diametro = 2 * raio;
            b = h = diametro;
            cantoSecao = new Point2d(centro.X - raio, centro.Y + raio);
        }
        else
        {
            Point2d p1, p2;
            if (o.DimensoesFixas)
            {
                var r = ed.GetPoint($"\nCanto superior esquerdo {oque}:");
                if (r.Status != PromptStatus.OK) return;
                var q = Wcs(ed, r.Value);
                p1 = new Point2d(q.X, q.Y);
                p2 = new Point2d(q.X + o.Base, q.Y - o.Altura);
            }
            else
            {
                var r1 = ed.GetPoint($"\nPrimeiro canto {oque}:");
                if (r1.Status != PromptStatus.OK) return;
                var r2 = ed.GetCorner($"\nCanto oposto {oque}:", r1.Value);
                if (r2.Status != PromptStatus.OK) return;
                var a = Wcs(ed, r1.Value);
                var z = Wcs(ed, r2.Value);
                p1 = new Point2d(Math.Min(a.X, z.X), Math.Max(a.Y, z.Y));
                p2 = new Point2d(Math.Max(a.X, z.X), Math.Min(a.Y, z.Y));
            }
            b = p2.X - p1.X - 2 * c;
            h = p1.Y - p2.Y - 2 * c;
            if (b <= 0 || h <= 0) { ed.WriteMessage("\nO cobrimento não cabe na seção."); return; }
            cantoSecao = new Point2d(p1.X + c, p1.Y - c);
        }

        var rb = ed.GetPoint(new PromptPointOptions("\nRebater estribo – canto superior esquerdo do estribo rebatido (Enter = no próprio corte):")
            { AllowNone = true });
        if (rb.Status != PromptStatus.OK && rb.Status != PromptStatus.None) return;
        Point2d? rebatido = null;
        if (rb.Status == PromptStatus.OK) { var q = Wcs(ed, rb.Value); rebatido = new Point2d(q.X, q.Y); }

        List<ObjectId> barras;
        using (var tr = new TransacaoSegura(db))
        {
            barras = Criar(tr, db, cfg, o, cantoSecao, b, h, diametro, rebatido);
            tr.Commit();
        }
        ed.WriteMessage($"\nEstribo {o.Formato}: {b:0.##} × {h:0.##} cm, {barras.Count} armadura(s).");

        // faixas de estribos
        double esp = o.Espacamento;
        while (true)
        {
            var r1 = ed.GetPoint(new PromptPointOptions("\nFaixa de estribos – ponto inicial (Enter termina):") { AllowNone = true });
            if (r1.Status != PromptStatus.OK) break;
            var r2 = ed.GetPoint(new PromptPointOptions("\nPonto final:") { UseBasePoint = true, BasePoint = r1.Value });
            if (r2.Status != PromptStatus.OK) break;
            var rp = ed.GetPoint(new PromptPointOptions("\nPosição da cota:") { UseBasePoint = true, BasePoint = r2.Value });
            if (rp.Status != PromptStatus.OK) break;
            var s = Comandos.PedirReal(ed, "Espaçamento (cm)", esp, 0.1);
            if (s == null) break;
            esp = s.Value;
            CriarFaixas(db, cfg, barras, Wcs(ed, r1.Value), Wcs(ed, r2.Value), Wcs(ed, rp.Value), esp);
        }
    }

    /// <summary>
    /// Desenha o estribo na seção (polilinhas simples) e o estribo rebatido (armaduras). Sem ponto de
    /// rebatimento, as polilinhas da seção viram as armaduras. Retorna as barras criadas.
    /// </summary>
    internal static List<ObjectId> Criar(Transaction tr, Database db, ConfiguracaoArmadura cfg, OpcoesEstribo o,
        Point2d cantoSecao, double b, double h, double diametro, Point2d? rebatido)
    {
        double lg = cfg.Dobramento.ComprimentoGancho(o.Gancho, o.Bitola, o.Aco, estribo: true);
        // passagem dos ganchos pelo canto = diâmetro externo de curvatura (pino + 2φ): 2,5 no ø5 CA-60,
        // como no exemplo do escritório; é aço (as duas dobras se encostam)
        double passagem = cfg.Dobramento.DiametroPino(o.Bitola, o.Aco, estribo: true) + 2 * o.Bitola / 10.0;
        var formas = GeradorEstribo.Gerar(o, b, h, diametro, lg, afastamento: passagem);
        var esp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
        var layer = Desenho.GarantirLayer(db, tr, cfg.LayerBarra, cfg.CorBarra);
        var barras = new List<ObjectId>();

        Polyline Desenhar(FormaEstribo f, Point2d origem)
        {
            var pl = new Polyline();
            pl.SetDatabaseDefaults(db);
            for (int i = 0; i < f.Vertices.Count; i++)
            {
                var v = f.Vertices[i];
                pl.AddVertexAt(i, new Point2d(origem.X + v.X, origem.Y + v.Y), v.Bulge, 0, 0);
            }
            pl.LayerId = layer;
            esp.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            return pl;
        }

        // na seção: representação simples (sem vínculo), exceto quando não há rebatimento
        var naSecao = formas.Select(f => (f, pl: Desenhar(f, cantoSecao))).ToList();

        // rebatido: as armaduras lado a lado (o pente/grampo à direita do fechado)
        double folga = Math.Max(10, 0.3 * b);
        for (int i = 0; i < formas.Count; i++)
        {
            var f = formas[i];
            Polyline pl;
            if (rebatido.HasValue)
            {
                if (!f.Armadura) continue; // múltiplo: no rebatido basta o que é armadura
                double dx = o.Formato == FormatoEstribo.Pente && i > 0 ? b + folga : 0;
                pl = Desenhar(f, new Point2d(rebatido.Value.X + dx, rebatido.Value.Y));
            }
            else
            {
                if (!f.Armadura) continue;
                pl = naSecao[i].pl;
            }
            var d = new DadosArmadura
            {
                Bitola = o.Bitola, Aco = o.Aco, Estribo = true, Quantidade = 1, Repeticoes = Math.Max(1, f.Repeticoes),
                Espacamento = o.Espacamento, Escala = cfg.Escala,
            };
            // formatos e cotas: ganchos e passagens sem cota; no fechado, só uma largura e uma altura
            for (int s = 0; s < f.Formatos.Count; s++)
            {
                bool gancho = Segmento.EhGancho(f.Formatos[s]);
                bool sobreposto = f.Sobrepostos.Contains(s);
                d.Segmentos.Add(new Segmento
                {
                    Formato = f.Formatos[s],
                    Sobreposto = sobreposto,
                    Cotar = !gancho && !sobreposto && (f.Cotados.Count == 0 || f.Cotados.Contains(s)),
                });
            }
            ServicoBarra.Criar(tr, pl, d, cfg);
            barras.Add(pl.ObjectId);
        }
        return barras;
    }

    /// <summary>Uma faixa ortogonal por armadura do estribo (a 2ª, de pente/grampo, um pouco mais afastada).</summary>
    internal static void CriarFaixas(Database db, ConfiguracaoArmadura cfg, List<ObjectId> barras, Point3d p1, Point3d p2,
        Point3d posicao, double espacamento)
    {
        using var tr = new TransacaoSegura(db);
        var u = (p2 - p1);
        var n = u.Length > 1e-9 ? u.GetNormal().CrossProduct(Vector3d.ZAxis) : Vector3d.XAxis;
        // afasta as cotas seguintes para o lado em que a 1ª foi posicionada
        if ((posicao - p1).DotProduct(n) < 0) n = n.Negate();
        double passo = 3 * cfg.AlturaTexto(Desenho.CmPorUnidade(db, cfg));
        for (int i = 0; i < barras.Count; i++)
        {
            var pl = (Polyline)tr.GetObject(barras[i], OpenMode.ForWrite);
            var f = new DadosFaixa
            {
                Tipo = TipoFaixa.Ortogonal, Espacamento = espacamento, EspacamentoInicial = cfg.EspacamentoInicialFaixa,
                P1X = p1.X, P1Y = p1.Y, P2X = p2.X, P2Y = p2.Y,
            };
            ServicoFaixa.Criar(tr, pl, f, posicao + n * (i * passo), cfg);
        }
        tr.Commit();
    }
}
