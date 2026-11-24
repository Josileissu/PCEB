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
        /// <summary>Outros consolos do mesmo pilar no mesmo plano e perto (D50: "trechos onde há muitos consolos próximos").</summary>
        public List<Vizinho> Vizinhos = new List<Vizinho>();
    }

    /// <summary>Consolo vizinho na chapa: lado (+1 o mesmo deste, −1 a face oposta), topo relativo (cm), geometria e cargas.</summary>
    public sealed class Vizinho
    {
        public string Nome = "";
        public int Lado;
        public double Dy, A, H, H1, Pos, Pad, Vd, Hd;
    }

    private static (double A, double H, double H1, double B) Geometria(LigacaoConsolo l, double tp)
    {
        var c = l.Consolo;
        if (c != null) return (c.A, c.H, c.H1 > 0 ? c.H1 : c.H / 2, c.B);
        double pad = l.Aparelho?.Comprimento > 0 ? l.Aparelho.Comprimento : 10, a = Math.Max(20, pad + 12), h = Math.Max(30, 1.2 * a);
        return (a, h, h / 2, Math.Min(tp, l.Viga.Secao.B + 10));
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
        r.Combinacao = elu.Nome;

        // biela e tirante (consolo curto): braço z = 0,9·d; tirante Rs = Vd·a/z + Hd; biela Rc = Vd/sen θ, tg θ = z/a
        r.Z = 0.9 * r.D;
        r.Theta = Math.Atan2(r.Z, Math.Max(1, r.Pos));
        r.Rs = r.Vd * r.Pos / r.Z + Math.Max(0, r.Hd);
        r.Rc = r.Vd / Math.Sin(r.Theta);

        // vizinhos: consolos do mesmo pilar no mesmo plano (viga paralela) com topo a menos de H + Lp deste
        foreach (var o in e.Ligacoes.Where(x => x != l && x.Pilar == p))
        {
            double cos = o.DirecaoViga.Ponto(dir);
            if (Math.Abs(cos) < 0.9) continue;
            double dy = o.ZTopoConsolo - l.ZTopoConsolo;
            var (ga, gh, gh1, _) = Geometria(o, r.Tp);
            if (dy > r.Lp + 1e-6 || dy - gh < -(r.H + r.Lp) - 1e-6) continue;
            var eo = e.DefinicaoCombinacoes.Where(x => x.Tipo == "ELU").Select(x => e.Esforco(o, e.Combinacoes[x.Nome])).OrderByDescending(x => x.V).First();
            var co = e.CargaDoConsolo(o);
            int lado = cos > 0 ? 1 : -1;
            r.Vizinhos.Add(new Vizinho { Nome = o.Dono.ToString(), Lado = lado, Dy = dy, A = ga, H = gh, H1 = gh1, Pos = co.A > 0 ? co.A : cfg.Numero("acoes.posCargaConsolo") * ga,
                Pad = o.Aparelho?.Comprimento > 0 ? o.Aparelho.Comprimento : 10, Vd = eo.V, Hd = lado * eo.H });
        }

        // chapa: pilar de x ∈ [−Lp, 0]; consolos à direita (x ∈ [0, A]) e, os da face oposta, à esquerda (x ∈ [−Lp − A, −Lp])
        double yMin = Math.Min(-r.H, r.Vizinhos.Select(v => v.Dy - v.H).DefaultIfEmpty(0).Min()), yMax = Math.Max(0, r.Vizinhos.Select(v => v.Dy).DefaultIfEmpty(0).Max());
        double y0 = yMin - r.Lp, y1 = yMax + r.Lp, h = Math.Max(1.25, Math.Min(2.5, r.Lp / 14));
        var consolos = new List<(int Lado, double Dy, double A, double H, double H1)> { (1, 0, r.A, r.H, r.H1) };
        consolos.AddRange(r.Vizinhos.Select(v => (v.Lado, v.Dy, v.A, v.H, v.H1)));
        bool NoConsolo(double x, double y)
        {
            foreach (var (lado, dy, a, hh, h1) in consolos)
            {
                double u = lado > 0 ? x : -r.Lp - x;      // distância à face do pilar
                if (u < 0 || u > a) continue;
                double inf = dy - hh + (hh - h1) * u / Math.Max(1e-6, a);
                if (y <= dy && y >= inf) return true;
            }
            return false;
        }
        bool Dentro(double x, double y) => (x <= 0 && x >= -r.Lp && y >= y0 && y <= y1) || NoConsolo(x, y);
        // força normal do pilar logo acima do trecho (acima do consolo mais alto)
        var esf = e.Esforcos(p, e.Combinacoes[elu.Nome]).Where(x => x.Z > l.ZTopoConsolo + yMax + 0.5).OrderBy(x => x.Z).FirstOrDefault();
        r.Nacima = esf != null ? Math.Max(0, -esf.N) : 0;
        double xMin = -r.Lp - r.Vizinhos.Where(v => v.Lado < 0).Select(v => v.A).DefaultIfEmpty(0).Max();
        double xMax = Math.Max(r.A, r.Vizinhos.Where(v => v.Lado > 0).Select(v => v.A).DefaultIfEmpty(0).Max());
        var ch = ChapaPlana.Malha(xMin, xMax, y0, y1, h, Dentro, (x, y) => x <= 0 && x >= -r.Lp ? r.Tp : r.B, (x, y) => x <= 0 && x >= -r.Lp ? "pilar" : "consolo");
        double fck = cfg.Numero("materiais.fck");
        ch.E = MaterialBarra.Concreto(fck).E; ch.Nu = 0.2;
        var nos = Enumerable.Range(0, ch.Nos.Count).ToList();
        double yBase = ch.Nos.Min(n => n.Y), yTopoChapa = ch.Nos.Max(n => n.Y);
        foreach (int i in nos.Where(i => ch.Nos[i].Y < yBase + 1e-6)) { ch.Presos.Add((i, 0)); ch.Presos.Add((i, 1)); }
        // aparelho: Vd e Hd distribuídos nos nós do topo dentro do aparelho
        // nós do topo de um consolo: a fileira mais alta dos elementos do consolo no vão do aparelho (a malha não passa exatamente em y)
        var noConsolo = new HashSet<int>(ch.Elementos.Where(x => x.Regiao == "consolo").SelectMany(x => x.Nos));
        List<int> TopoAparelho(double xc, double pad, double yTopo, Func<double, bool> lado)
        {
            var cand = noConsolo.Where(i => lado(ch.Nos[i].X) && Math.Abs(ch.Nos[i].Y - yTopo) <= h).ToList();
            if (cand.Count == 0) return new List<int>();
            double yMaxL = cand.Max(i => ch.Nos[i].Y);
            var fila = cand.Where(i => Math.Abs(ch.Nos[i].Y - yMaxL) < 1e-6).ToList();
            var sob = fila.Where(i => Math.Abs(ch.Nos[i].X - xc) <= pad / 2 + h / 2).ToList();
            return sob.Count > 0 ? sob : fila.OrderBy(i => Math.Abs(ch.Nos[i].X - xc)).Take(1).ToList();
        }
        var topo = TopoAparelho(r.Pos, r.Pad, 0, x => x > 1e-6);
        foreach (int i in topo) ch.Forca(i, r.Hd / topo.Count, -r.Vd / topo.Count);
        foreach (var v in r.Vizinhos)
        {
            double xc = v.Lado > 0 ? v.Pos : -r.Lp - v.Pos;
            var tv = TopoAparelho(xc, v.Pad, v.Dy, x => v.Lado > 0 ? x > 1e-6 : x < -r.Lp - 1e-6);
            foreach (int i in tv) ch.Forca(i, v.Hd / tv.Count, -v.Vd / tv.Count);
        }
        // força normal do pilar acima, uniforme no topo do trecho
        var cima = nos.Where(i => ch.Nos[i].Y > yTopoChapa - 1e-6).ToList();
        if (r.Nacima > 0 && cima.Count > 1)
            foreach (int i in cima)
            {
                bool borda = ch.Nos[i].X <= cima.Min(k => ch.Nos[k].X) + 1e-6 || ch.Nos[i].X >= cima.Max(k => ch.Nos[k].X) - 1e-6;
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
        // aparelho e ponta da viga como estão no modelo: espessura do aparelho em uso e a forma da ponta (dente, ressalto) sobre ele
        var ap = l.AparelhoEmUso;
        double tAp = ap != null && ap.Espessura > 0 ? ap.Espessura : 1.2;
        g.Definir("tAp", tAp);
        g.Definir("aparelho", ap == null ? "sem aparelho (articulação)" : $"{DEB.Core.Intercambio.ApoioPeca.Texto(ap.Tipo)} {Fmt.Virgula(ap.Comprimento, "0.#")} × {Fmt.Virgula(ap.Largura, "0.#")} × {Fmt.Virgula(ap.Espessura, "0.#")} cm");
        var perfil = l.PontaI ? l.Viga.PerfilPontaI : l.Viga.PerfilPontaJ;
        double ate = l.PontaI ? l.Viga.ApoioAtePontaI : l.Viga.ApoioAtePontaJ;
        double xFace = ate > 0 ? r.Pos - ate : r.Pos - r.Pad / 2 - 3;
        var pt = ValorJson.NovaLista(); g.Definir("ponta", pt);
        if (perfil.Count == 0) perfil = new List<(double, double, double, double)> { (0, 1.5 * l.Viga.Secao.H, 0, l.Viga.Secao.H) };
        double zApoio = perfil.OrderBy(x => x.X0).First().ZBase;   // o fundo da ponta apoia no aparelho
        foreach (var (x0, x1, zb, zt) in perfil)
        {
            var q = ValorJson.NovaLista();
            foreach (double v in new[] { xFace + x0, xFace + x1, tAp + zb - zApoio, tAp + zt - zApoio }) q.Adicionar(ValorJson.De(Math.Round(v, 2)));
            pt.Adicionar(q);
        }
        g.Definir("pontaModelo", (l.PontaI ? l.Viga.PerfilPontaI : l.Viga.PerfilPontaJ).Count > 0);
        var cg = ValorJson.NovoObjeto(); o.Definir("cargas", cg);
        cg.Definir("vd", r.Vd); cg.Definir("hd", r.Hd); cg.Definir("n", r.Nacima); cg.Definir("comb", r.Combinacao);
        var vz = ValorJson.NovaLista(); o.Definir("vizinhos", vz);
        foreach (var v in r.Vizinhos)
        {
            var q = ValorJson.NovoObjeto(); vz.Adicionar(q);
            q.Definir("nome", v.Nome); q.Definir("lado", v.Lado); q.Definir("dy", v.Dy); q.Definir("a", v.A); q.Definir("h", v.H); q.Definir("h1", v.H1);
            q.Definir("pos", v.Pos); q.Definir("pad", v.Pad); q.Definir("vd", v.Vd); q.Definir("hd", v.Hd);
        }
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

    // ================================================================ campo de tensões não linear (D50, item 9)

    /// <summary>Resultado do campo de tensões do consolo: a análise, as hipóteses de armadura e o resumo.</summary>
    public sealed class ResultadoCampo
    {
        public Resultado Base;
        public CampoTensoes Campo;
        public List<string> Hipoteses = new List<string>();
        public double Fcd, Fyd;   // kN/cm²
    }

    /// <summary>
    /// Campo de tensões do trecho (o mesmo da chapa elástica, com os vizinhos): armaduras do consolo (tirante, costura e estribos
    /// verticais) pelo detalhamento do pilar, ou estimadas pela biela-tirante quando não há; armadura do pilar com ρ = 1% nas duas
    /// faces (hipótese). Concreto e aço não lineares, carga do cálculo (λ = 1) e crescente até falhar.
    /// </summary>
    public static ResultadoCampo Campo(EstruturaObjetos e, LigacaoConsolo l, ConfiguracaoProjeto cfg = null)
    {
        cfg ??= new ConfiguracaoProjeto();
        var r = Calcular(e, l, cfg);
        var ch = r.Chapa;
        double fck = cfg.Numero("materiais.fck"), fyk = 500;
        var res = new ResultadoCampo { Base = r, Fcd = fck / cfg.Numero("normas.gamaC") / 10, Fyd = fyk / cfg.Numero("normas.gamaS") / 10 };
        double hx = ch.Elementos.Count > 0 ? Math.Abs(ch.Nos[ch.Elementos[0].Nos[1]].X - ch.Nos[ch.Elementos[0].Nos[0]].X) : 2;
        double hy = ch.Elementos.Count > 0 ? Math.Abs(ch.Nos[ch.Elementos[0].Nos[3]].Y - ch.Nos[ch.Elementos[0].Nos[0]].Y) : 2;
        // nós da malha por linha e por coluna (só os usados por elementos)
        var usados = new HashSet<int>(ch.Elementos.SelectMany(x => x.Nos));
        double Ar(int n, double phiMm) => n * Math.PI * phiMm * phiMm / 400;
        void Horizontal(double y, double x0, double x1, double area, string grupo)
        {
            if (area <= 0) return;
            var linha = usados.Where(i => Math.Abs(ch.Nos[i].Y - y) <= hy / 2 + 1e-9).ToList();
            if (linha.Count == 0) return;
            double yr = linha.OrderBy(i => Math.Abs(ch.Nos[i].Y - y)).Select(i => ch.Nos[i].Y).First();
            var nos = linha.Where(i => Math.Abs(ch.Nos[i].Y - yr) < 1e-6 && ch.Nos[i].X >= x0 - hx / 2 && ch.Nos[i].X <= x1 + hx / 2).OrderBy(i => ch.Nos[i].X).ToList();
            for (int k = 0; k + 1 < nos.Count; k++)
                if (ch.Nos[nos[k + 1]].X - ch.Nos[nos[k]].X < hx * 1.5) ch.Barras.Add(new BarraChapa { I = nos[k], J = nos[k + 1], Area = area, Grupo = grupo });
        }
        void Vertical(double x, double y0, double y1, double area, string grupo)
        {
            if (area <= 0) return;
            var col = usados.Where(i => Math.Abs(ch.Nos[i].X - x) <= hx / 2 + 1e-9).ToList();
            if (col.Count == 0) return;
            double xr = col.OrderBy(i => Math.Abs(ch.Nos[i].X - x)).Select(i => ch.Nos[i].X).First();
            var nos = col.Where(i => Math.Abs(ch.Nos[i].X - xr) < 1e-6 && ch.Nos[i].Y >= y0 - hy / 2 && ch.Nos[i].Y <= y1 + hy / 2).OrderBy(i => ch.Nos[i].Y).ToList();
            for (int k = 0; k + 1 < nos.Count; k++)
                if (ch.Nos[nos[k + 1]].Y - ch.Nos[nos[k]].Y < hy * 1.5) ch.Barras.Add(new BarraChapa { I = nos[k], J = nos[k + 1], Area = area, Grupo = grupo });
        }
        // armadura de um consolo (lado +1: x de 0 a A; −1: de −Lp − A a −Lp); o tirante atravessa o pilar até a face oposta
        void Consolo(int lado, double dy, double a, double hh, double h1, LigacaoConsolo lig, double vd, double hd, double pos)
        {
            var c = lig?.Consolo;
            double cob = c?.Cob ?? r.Cob, d = hh - cob - 0.8, z = 0.9 * d;
            double asTir, asCost, asVert; double phiT;
            if (c != null && c.TirN > 0)
            {
                asTir = Ar(c.TirN, c.TirPhi); phiT = c.TirPhi; asCost = 2 * Ar(Math.Max(0, c.CostN), c.CostPhi); asVert = 2 * Ar(Math.Max(0, c.VertN), c.VertPhi);
                res.Hipoteses.Add($"{lig.Dono}: armadura do detalhamento — tirante {c.TirN} ø{c.TirPhi:0.#}, costura {c.CostN} ø{c.CostPhi:0.#} (2 ramos), verticais {c.VertN} ø{c.VertPhi:0.#} (2 ramos)");
            }
            else
            {
                double rs = vd * pos / z + Math.Max(0, hd);
                asTir = rs / res.Fyd; phiT = 12.5; asCost = 0.4 * asTir; asVert = 0.2 * asTir;
                res.Hipoteses.Add($"{lig?.Dono.ToString() ?? "consolo"}: sem detalhamento — armaduras estimadas: tirante {asTir:0.00} cm² (Rs/fyd), costura 0,4·As, verticais 0,2·As");
            }
            double yT = dy - cob - phiT / 20;
            double xFace = lado > 0 ? 0 : -r.Lp, xPonta = xFace + lado * (a - cob), xFundo = lado > 0 ? -r.Lp + cob : -cob;
            Horizontal(yT, Math.Min(xFundo, xPonta), Math.Max(xFundo, xPonta), asTir, "tirante");
            int nc = c?.CostN > 0 ? c.CostN : 2;
            for (int k = 1; k <= nc; k++)
                Horizontal(yT - k * (2.0 / 3 * d) / (nc + 0.5), Math.Min(xFundo, xPonta), Math.Max(xFundo, xPonta), asCost / nc, "costura");
            int nv = c?.VertN > 0 ? c.VertN : 2;
            for (int k = 1; k <= nv; k++)
            {
                double u = a * k / (nv + 1), x = xFace + lado * u, inf = dy - hh + (hh - h1) * u / Math.Max(1e-6, a);
                Vertical(x, inf + cob, dy - cob, asVert / nv, "vertical");
            }
        }
        Consolo(1, 0, r.A, r.H, r.H1, l, r.Vd, r.Hd, r.Pos);
        foreach (var v in r.Vizinhos)
        {
            var lv = e.Ligacoes.FirstOrDefault(x => x.Dono.ToString() == v.Nome);
            Consolo(v.Lado, v.Dy, v.A, v.H, v.H1, lv, v.Vd, v.Hd, v.Pos);
        }
        // pilar: ρ = 1% da seção, metade em cada face do plano (hipótese: a armadura real do pilar não é lida aqui)
        double yMin = ch.Nos.Min(n => n.Y), yMax = ch.Nos.Max(n => n.Y), asFace = 0.005 * r.Lp * r.Tp;
        Vertical(-r.Lp + 4, yMin, yMax, asFace, "pilar");
        Vertical(-4, yMin, yMax, asFace, "pilar");
        res.Hipoteses.Add($"Pilar: ρ = 1% ({2 * asFace:0.0} cm²) em duas faces a 4 cm da borda — hipótese; estribos do pilar não entram.");
        res.Hipoteses.Add($"Concreto C{fck:0}: parábola-retângulo (NBR 6118), sem tração, kc2 = 1/(1,2 + 55·ε1) ≤ 1, ηfc = (30/fck)^⅓ ≤ 1; aço CA-50 elastoplástico (fyd = {res.Fyd * 10:0} MPa), aderência perfeita.");
        var campo = new CampoTensoes(ch) { Fck = fck, Fcd = res.Fcd, Fyd = res.Fyd, Ec = ch.E };
        campo.Analisar();
        res.Campo = campo;
        return res;
    }

    /// <summary>JSON do campo de tensões para a página (MPa): elementos [σ2, ângulo, kc2·ηfc·fcd], barras [x1, y1, x2, y2, σs, grupo] e o resumo.</summary>
    public static string JsonCampo(EstruturaObjetos e, LigacaoConsolo l, ConfiguracaoProjeto cfg, string chave)
    {
        var rc = Campo(e, l, cfg);
        var c = rc.Campo; var ch = rc.Base.Chapa; var est = c.Calculo;
        var o = ValorJson.NovoObjeto();
        o.Definir("tipo", "campo"); o.Definir("chave", chave);
        o.Definir("lambda", c.LambdaMax); o.Definir("falha", c.FalhaGovernante); o.Definir("calculou", est != null && est.Lambda >= 1 - 1e-9);
        o.Definir("fcd", rc.Fcd * 10); o.Definir("fyd", rc.Fyd * 10);
        var hs = ValorJson.NovaLista(); o.Definir("hipoteses", hs); foreach (string h in rc.Hipoteses) hs.Adicionar(ValorJson.De(h));
        var els = ValorJson.NovaLista(); o.Definir("els", els);
        double eta = Math.Min(1, Math.Pow(30 / Math.Max(1, c.Fck), 1.0 / 3)), uMax = 0, sMax = 0;
        if (est != null)
            for (int i = 0; i < ch.Elementos.Count; i++)
            {
                var q = ValorJson.NovaLista();
                double lim = est.Kc2[i] * eta * rc.Fcd;
                q.Adicionar(ValorJson.De(Math.Round(est.S2[i] * 10, 3))); q.Adicionar(ValorJson.De(Math.Round(est.Ang[i], 4))); q.Adicionar(ValorJson.De(Math.Round(lim * 10, 3)));
                els.Adicionar(q);
                uMax = Math.Max(uMax, -est.S2[i] / Math.Max(1e-9, lim));
            }
        var bs = ValorJson.NovaLista(); o.Definir("barras", bs);
        for (int j = 0; j < ch.Barras.Count; j++)
        {
            var b = ch.Barras[j]; var q = ValorJson.NovaLista();
            foreach (double v in new[] { ch.Nos[b.I].X, ch.Nos[b.I].Y, ch.Nos[b.J].X, ch.Nos[b.J].Y }) q.Adicionar(ValorJson.De(Math.Round(v, 2)));
            double sg = est != null ? est.SigmaS[j] * 10 : 0;
            q.Adicionar(ValorJson.De(Math.Round(sg, 1))); q.Adicionar(ValorJson.De(b.Grupo)); q.Adicionar(ValorJson.De(Math.Round(b.Area, 3)));
            bs.Adicionar(q);
            sMax = Math.Max(sMax, Math.Abs(sg) / (rc.Fyd * 10));
        }
        o.Definir("usoConcreto", uMax); o.Definir("usoAco", sMax);
        return Persistencia.Json.Gravar(o, indentado: false);
    }
}
