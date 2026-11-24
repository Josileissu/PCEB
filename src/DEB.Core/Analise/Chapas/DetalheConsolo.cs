using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Analise.Barras;
using DEB.Core.Analise.Dimensionamento;
using DEB.Core.Analise.Objetos;
using DEB.Core.Configuracao.Projeto;
using DEB.Core.Persistencia;

namespace DEB.Core.Analise.Chapas;

/// <summary>
/// Detalhe do consolo na janela de análise (D50, item 3): trecho do pilar com o consolo, aparelho e ponta da viga no plano da viga,
/// com as cargas da combinação ELU mais desfavorável, o modelo de biela e tirante e a chapa elástica (Q4) com as tensões principais
/// para as isovalores. Coordenadas locais (cm): x da face do pilar para a ponta do consolo; y = 0 no topo do consolo.
/// </summary>
public static class DetalheConsolo
{
    public sealed class Resultado
    {
        public double Lp, Tp, A, H, H1, B, Pos, Pad, D, Cob;
        public bool Estimado;
        public double Vd, Hd, Nacima;
        public string Combinacao = "";
        public double Rs, Rc, Theta, Z;
        public ChapaPlana Chapa;
    }

    public static Resultado Calcular(EstruturaObjetos e, LigacaoConsolo l, ConfiguracaoProjeto cfg = null)
    {
        cfg ??= new ConfiguracaoProjeto();
        var r = new Resultado();
        var p = l.Pilar;
        Vetor3 dir = l.DirecaoViga;
        bool aoLongoY = Math.Abs(dir.Ponto(p.DirY)) > 0.7;
        r.Lp = aoLongoY ? p.Secao.B : p.Secao.H;
        r.Tp = aoLongoY ? p.Secao.H : p.Secao.B;
        var c = l.Consolo;
        if (c != null) { r.A = c.A; r.H = c.H; r.H1 = c.H1 > 0 ? c.H1 : c.H / 2; r.B = c.B; r.Cob = c.Cob; r.D = c.D; }
        else
        {
            r.Estimado = true;
            double pad = l.Aparelho?.Comprimento > 0 ? l.Aparelho.Comprimento : 10;
            r.A = Math.Max(20, pad + 12); r.H = Math.Max(30, 1.2 * r.A); r.H1 = r.H / 2; r.B = Math.Min(r.Tp, l.Viga.Secao.B + 10);
            r.Cob = cfg.Numero("durab.cobConsolo") * 100; r.D = r.H - r.Cob - 0.8;
        }
        r.Pad = l.Aparelho?.Comprimento > 0 ? l.Aparelho.Comprimento : 10;
        var carga = e.CargaDoConsolo(l);
        r.Pos = carga.A > 0 ? carga.A : cfg.Numero("acoes.posCargaConsolo") * r.A;

        // combinação ELU de maior V (a análise já tem γf; o γn da NBR 9062 entra no cálculo do consolo)
        var elu = e.DefinicaoCombinacoes.Where(x => x.Tipo == "ELU").Select(x => (x.Nome, F: e.Esforco(l, e.Combinacoes[x.Nome]))).OrderByDescending(x => x.F.V).First();
        r.Vd = elu.F.V; r.Hd = elu.F.H; r.Combinacao = elu.Nome;
        // força normal do pilar logo acima do consolo
        var esf = e.Esforcos(p, e.Combinacoes[elu.Nome]).Where(x => x.Z > l.ZTopoConsolo + 0.5).OrderBy(x => x.Z).FirstOrDefault();
        r.Nacima = esf != null ? Math.Max(0, -esf.N) : 0;

        // biela e tirante (consolo curto): braço z = 0,9·d; tirante Rs = Vd·a/z + Hd; biela Rc = Vd/sen θ, tg θ = z/a
        r.Z = 0.9 * r.D;
        r.Theta = Math.Atan2(r.Z, Math.Max(1, r.Pos));
        r.Rs = r.Vd * r.Pos / r.Z + Math.Max(0, r.Hd);
        r.Rc = r.Vd / Math.Sin(r.Theta);

        // chapa: pilar de x ∈ [−Lp, 0], y ∈ [−(H + Lp), Lp]; consolo x ∈ [0, A], entre o topo e a face inferior inclinada
        double y0 = -(r.H + r.Lp), y1 = r.Lp, h = Math.Max(1.25, Math.Min(2.5, r.Lp / 14));
        double Inferior(double x) => -r.H + (r.H - r.H1) * x / Math.Max(1e-6, r.A);
        bool Dentro(double x, double y) => x <= 0 ? x >= -r.Lp && y >= y0 && y <= y1 : x <= r.A && y <= 0 && y >= Inferior(x);
        var ch = ChapaPlana.Malha(-r.Lp, r.A, y0, y1, h, Dentro, (x, y) => x <= 0 ? r.Tp : r.B, (x, y) => x <= 0 ? "pilar" : "consolo");
        double fck = cfg.Numero("materiais.fck");
        ch.E = MaterialBarra.Concreto(fck).E; ch.Nu = 0.2;
        var nos = Enumerable.Range(0, ch.Nos.Count).ToList();
        foreach (int i in nos.Where(i => ch.Nos[i].Y < y0 + 1e-6)) { ch.Presos.Add((i, 0)); ch.Presos.Add((i, 1)); }
        // aparelho: Vd e Hd distribuídos nos nós do topo dentro do aparelho
        var topo = nos.Where(i => Math.Abs(ch.Nos[i].Y) < 1e-6 && ch.Nos[i].X > 1e-6 && Math.Abs(ch.Nos[i].X - r.Pos) <= r.Pad / 2 + h / 2).ToList();
        if (topo.Count == 0) topo = nos.Where(i => Math.Abs(ch.Nos[i].Y) < 1e-6 && ch.Nos[i].X > 0).OrderBy(i => Math.Abs(ch.Nos[i].X - r.Pos)).Take(1).ToList();
        foreach (int i in topo) ch.Forca(i, r.Hd / topo.Count, -r.Vd / topo.Count);
        // força normal do pilar acima, uniforme no topo do trecho
        var cima = nos.Where(i => ch.Nos[i].Y > y1 - 1e-6).ToList();
        if (r.Nacima > 0 && cima.Count > 1)
            foreach (int i in cima)
            {
                bool borda = ch.Nos[i].X < -r.Lp + 1e-6 || ch.Nos[i].X > -1e-6;
                ch.Forca(i, 0, -r.Nacima / (cima.Count - 1) * (borda ? 0.5 : 1));
            }
        ch.Resolver();
        r.Chapa = ch;
        return r;
    }

    /// <summary>JSON do detalhe para a página (tensões em MPa).</summary>
    public static string Json(EstruturaObjetos e, LigacaoConsolo l, ConfiguracaoProjeto cfg, string chave)
    {
        var r = Calcular(e, l, cfg);
        var o = ValorJson.NovoObjeto();
        o.Definir("tipo", "consolo"); o.Definir("chave", chave);
        var g = ValorJson.NovoObjeto(); o.Definir("geo", g);
        foreach (var (k, v) in new[] { ("lp", r.Lp), ("tp", r.Tp), ("a", r.A), ("h", r.H), ("h1", r.H1), ("b", r.B), ("pos", r.Pos), ("pad", r.Pad), ("d", r.D), ("cob", r.Cob),
                                        ("hViga", l.Viga.Secao.H), ("bViga", l.Viga.Secao.B) })
            g.Definir(k, v);
        g.Definir("estimado", r.Estimado);
        var cg = ValorJson.NovoObjeto(); o.Definir("cargas", cg);
        cg.Definir("vd", r.Vd); cg.Definir("hd", r.Hd); cg.Definir("n", r.Nacima); cg.Definir("comb", r.Combinacao);
        var bt = ValorJson.NovoObjeto(); o.Definir("biela", bt);
        bt.Definir("rs", r.Rs); bt.Definir("rc", r.Rc); bt.Definir("theta", r.Theta * 180 / Math.PI); bt.Definir("z", r.Z);
        var dm = DimensionamentoObjetos.Consolo(e, l, cfg);
        var dj = ValorJson.NovoObjeto(); o.Definir("dim", dj);
        dj.Definir("situacao", dm.Situacao.ToString());
        var rs = ValorJson.NovaLista(); dj.Definir("resumo", rs); foreach (string s in dm.Resumo) rs.Adicionar(ValorJson.De(s));
        var mef = ValorJson.NovoObjeto(); o.Definir("mef", mef);
        var nl = ValorJson.NovaLista(); mef.Definir("nos", nl);
        foreach (var (x, y) in r.Chapa.Nos) { var q = ValorJson.NovaLista(); q.Adicionar(ValorJson.De(Math.Round(x, 2))); q.Adicionar(ValorJson.De(Math.Round(y, 2))); nl.Adicionar(q); }
        var el = ValorJson.NovaLista(); mef.Definir("els", el);
        foreach (var x in r.Chapa.Elementos)
        {
            var q = ValorJson.NovaLista();
            foreach (int n in x.Nos) q.Adicionar(ValorJson.De(n));
            foreach (double v in new[] { x.S1 * 10, x.S2 * 10, x.Angulo, x.Sx * 10, x.Sy * 10, x.Txy * 10 }) q.Adicionar(ValorJson.De(Math.Round(v, 3)));
            el.Adicionar(q);
        }
        mef.Definir("s1max", r.Chapa.Elementos.Max(x => x.S1) * 10); mef.Definir("s2min", r.Chapa.Elementos.Min(x => x.S2) * 10);
        double fcd = cfg.Numero("materiais.fck") / cfg.Numero("normas.gamaC"), fctm = 0.3 * Math.Pow(cfg.Numero("materiais.fck"), 2.0 / 3);
        mef.Definir("fcd", fcd); mef.Definir("fctm", fctm);
        return Persistencia.Json.Gravar(o, indentado: false);
    }
}
