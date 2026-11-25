using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Analise.Objetos;
using DEB.Core.Motor;

namespace DEB.Core.Elementos.Viga.PreMoldada.Calculo;

/// <summary>
/// Desenho da análise da viga no CAD (D51, etapa 2), no padrão do arquivo de estudo do usuário: modelo de análise (barra, nós, apoios,
/// molas da ligação semirrígida, linhas de carga PP/CP/SC e pontuais), as seções do modelo (as mesmas do detalhamento: A, B, C…),
/// tabelas "carga da laje" e "carga da viga", e os diagramas da envoltória — Vd, Md e δ (elástica e, embaixo, com fissuração e
/// fluência). Verdadeira grandeza em cm (x ao longo da viga, ponta esquerda em 0; y para cima, eixo do modelo em y = 0); a escala
/// só dá a altura dos textos e o tamanho dos símbolos. Valores nas unidades da configuração do projeto.
/// </summary>
public static class DesenhoAnaliseViga
{
    public const string LTextos = "PCEB_TEXTOS", LBarra = "PCEB_Barra", LNo = "PCEB_NÓ", LSuporte = "PCEB_SUPORTE", LSemi = "PCEB_SEMI_RÍGIDO",
        LCargas = "PCEB_CARGAS", LSecao = "PCEB_SEÇÃO", LCortante = "PCEB_DIAGRAMA_CORTANTE", LFletor = "PCEB_DIAGRAMA_FLETOR",
        LDeslocada = "PCEB_DESLOCADA", LDeslocadaTotal = "PCEB_DESLOCADA_TOTAL", LTabela = "PCEB_TABELA", LMemoria = "PCEB_MEMORIA";

    /// <summary>Distâncias verticais entre os blocos do desenho (cm na escala 1:50; multiplicadas por escala/50).</summary>
    private const double Folga = 60, AlturaDiagrama = 45;

    public static ResultadoTemplate Gerar(ResultadoAnaliseViga r, EntradaAnaliseViga ent, ValoresTemplate v, UnidadesAnalise u = null,
        IReadOnlyList<TipoDente> catalogo = null, double escala = 50, double alturaTextoMm = 2.0, ResultadoCalculoViga calc = null)
    {
        u ??= new UnidadesAnalise(null);
        catalogo ??= TemplateVigaPreMoldada.CatalogoDentes;
        var o = new ResultadoTemplate { Escala = escala };
        double k = escala / 50, L = r.L;
        // altura dos textos = a das tabelas do CAD (AlturaTextoMm da configuração × escala): o espaçamento das tabelas depende dela
        double ht = (alturaTextoMm > 0 ? alturaTextoMm : 2.0) / 10 * escala, hTit = 1.6 * ht;
        string N(double x, string f = "0.###") { string t = Fmt.Virgula(x, f); return t.StartsWith("-") && t.Trim('-', '0', ',').Length == 0 ? t.Substring(1) : t; }
        (double V, string S) T(string un, double x) => u.ParaTela(un, x);
        void Txt(string s, double x, double y, double h, int al = 0, string layer = LTextos) =>
            o.Textos.Add(new TextoTemplate { Texto = s, X = x, Y = y, Altura = h, Alinhamento = al, Layer = layer });
        void Lin(string layer, params (double X, double Y)[] p) => o.Contornos.Add(new ContornoTemplate { Vertices = p.ToList(), Layer = layer });
        void Poli(string layer, IEnumerable<(double X, double Y)> p, bool fechado = false) =>
            o.Contornos.Add(new ContornoTemplate { Vertices = p.ToList(), Fechado = fechado, Layer = layer });

        double xa = r.ApoioE, xb = L - r.ApoioD;
        var semi = r.Modelos.FirstOrDefault(m => m.AlfaRE > 0 || m.AlfaRD > 0);

        // ------------------------------------------------ modelo de análise
        double yCargas = 25 * k;
        Txt("MODELO DE ANÁLISE", 0, yCargas + 3 * 25 * k + 2 * hTit, hTit);
        // linhas de carga (de cima para baixo: SC, CP, PP), com o total linear
        var casos = new[] { (CasoViga.SC, "SC,viga + SC,laje"), (CasoViga.CP, "CP,viga + CP,laje"), (CasoViga.PP, "PP,viga + PP,laje") };
        for (int i = 0; i < casos.Length; i++)
        {
            double y = yCargas + (casos.Length - 1 - i) * 25 * k;
            Lin(LCargas, (0, y), (L, y));
            for (int j = 0; j <= 10; j++) { double x = L * j / 10; Lin(LCargas, (x, y), (x, y - 6 * k)); Lin(LCargas, (x - 1.5 * k, y - 3.5 * k), (x, y - 6 * k), (x + 1.5 * k, y - 3.5 * k)); }
            var q = T("kN/m", r.Linear[casos[i].Item1]);
            Txt($"{casos[i].Item2} = {N(q.V)} {q.S}", L / 2, y + 2 * k, ht, 1);
        }
        // pontuais: seta acima da linha de PP, com G e Q
        foreach (var p in ent.Pontuais.Where(p => p.X >= 0 && p.X <= L))
        {
            double yt = yCargas + 3 * 25 * k;
            Lin(LCargas, (p.X, yt), (p.X, 0));
            Lin(LCargas, (p.X - 2 * k, 4 * k), (p.X, 0), (p.X + 2 * k, 4 * k));
            var g = T("kN", p.G); var qq = T("kN", p.Q);
            Txt($"G = {N(g.V)} {g.S}" + (Math.Abs(p.Q) > 0 ? $"; Q = {N(qq.V)} {qq.S}" : ""), p.X + 2 * k, yt + k, ht);
        }
        // barra, nós nas mudanças de seção e nos apoios
        Lin(LBarra, (0, 0), (L, 0));
        var nos = r.Trechos.Select(t => t.X0).Concat(new[] { L, xa, xb }).Concat(ent.Pontuais.Select(p => p.X))
            .Where(x => x > 1e-6 && x < L - 1e-6).Select(x => Math.Round(x, 2)).Distinct();
        foreach (double x in nos) o.Circulos.Add(new CirculoTemplate { X = x, Y = 0, Raio = 1.2 * k, Layer = LNo });
        // apoios: fixo à esquerda (triângulo com base hachurada), móvel à direita (triângulo sobre roletes)
        void Triangulo(double x) => Poli(LSuporte, new[] { (x, 0.0), (x - 6 * k, -10 * k), (x + 6 * k, -10 * k) }, true);
        Triangulo(xa);
        Lin(LSuporte, (xa - 9 * k, -10 * k), (xa + 9 * k, -10 * k));
        for (int j = 0; j < 6; j++) { double x = xa - 9 * k + j * 3.6 * k; Lin(LSuporte, (x, -10 * k), (x - 3 * k, -14 * k)); }
        Triangulo(xb);
        o.Circulos.Add(new CirculoTemplate { X = xb - 3 * k, Y = -11.5 * k, Raio = 1.5 * k, Layer = LSuporte });
        o.Circulos.Add(new CirculoTemplate { X = xb + 3 * k, Y = -11.5 * k, Raio = 1.5 * k, Layer = LSuporte });
        Lin(LSuporte, (xb - 9 * k, -13 * k), (xb + 9 * k, -13 * k));
        // ligação semirrígida: mola em espiral sobre o apoio e o αR
        if (semi != null)
            foreach (var (x, a, lado) in new[] { (xa, semi.AlfaRE, -1), (xb, semi.AlfaRD, 1) })
            {
                if (a <= 0) continue;
                var esp = new List<(double, double)>();
                for (int j = 0; j <= 48; j++)
                {
                    double t = j / 48.0 * 3 * 2 * Math.PI, rr = 1.5 * k + 4 * k * j / 48.0;
                    esp.Add((x + rr * Math.Cos(t), 14 * k + rr * Math.Sin(t)));
                }
                Poli(LSemi, esp);
                Lin(LSemi, (x, 0), (x, 14 * k));
                Txt($"αR = {N(a * 100, "0.#")} %", x + lado * 8 * k, 20 * k, ht, lado < 0 ? 2 : 0, LSemi);
            }

        // ------------------------------------------------ seções do modelo (as do detalhamento), abaixo do modelo
        var avisos = new List<string>();
        double b = v.Numero("B"), h = v.Numero("H");
        var e = TemplateVigaPreMoldada.LerExtremidade(v, true, catalogo, avisos);
        var d = TemplateVigaPreMoldada.LerExtremidade(v, false, catalogo, avisos);
        var aba = TemplateVigaPreMoldada.LerAba(v, h, L, e, d, avisos);
        double yTopoSec = -Folga * k;
        double yFimSec = yTopoSec;
        for (int i = 0; i < r.Trechos.Count; i++)
        {
            var t = r.Trechos[i];
            double xm = (t.X0 + t.X1) / 2;
            var c = AnaliseViga.ContornoSecao(xm, b, h, L, e, d, aba);
            double zTop = c.Max(p => p.Z), yMin = c.Min(p => p.Y), yMax = c.Max(p => p.Y);
            double dx = xm - (yMin + yMax) / 2;
            Poli(LSecao, c.Select(p => (dx + p.Y, yTopoSec - (zTop - p.Z))), true);
            string letra = ((char)('A' + i)).ToString();
            Txt(letra, xm, yTopoSec + 3 * k, hTit, 1);
            yFimSec = Math.Min(yFimSec, yTopoSec - (zTop - c.Min(p => p.Z)));
            Txt(t.Secao.Replace("×", " x "), xm, yTopoSec - (zTop - c.Min(p => p.Z)) - 1.8 * ht, ht, 1);
        }

        // ------------------------------------------------ diagramas: Vd, Md (envoltória) e δ
        // o eixo do Vd fica abaixo das seções: rótulo da seção, folga, título e a metade de cima do diagrama
        double y0 = yFimSec - 3 * ht - Folga * k - AlturaDiagrama * k - 2 * hTit;
        var env = r.Envoltoria;
        // eixo de referência e pontas de cada diagrama
        void Eixo(double y, string layer) { Lin(layer, (0, y), (L, y)); Lin(layer, (0, y - 3 * k), (0, y + 3 * k)); Lin(layer, (L, y - 3 * k), (L, y + 3 * k)); }
        void Valor(string layer, double x, double y, double val, int al) => Txt(N(val, "0.00"), x, y, ht, al, layer);

        // Vd: envoltória (maior e menor em cada x); positivo para cima
        {
            double vmax = Math.Max(1e-9, env.Max(p => Math.Max(Math.Abs(p.VMax), Math.Abs(p.VMin))));
            double esc = AlturaDiagrama * k / vmax;
            var un = T("kN", 1).S;
            Txt($"Vd ({un})", L / 2, y0 + AlturaDiagrama * k + 2 * ht, hTit, 1, LTextos);
            Eixo(y0, LCortante);
            Poli(LCortante, env.Select(p => (p.X, y0 + p.VMax * esc)));
            if (r.Modelos.Count > 1) Poli(LCortante, env.Select(p => (p.X, y0 + p.VMin * esc)));
            foreach (double x in new[] { xa, xb })
            {
                var pt = env.Where(p => Math.Abs(p.X - x) < 0.01).OrderByDescending(p => Math.Abs(p.VMax)).FirstOrDefault();
                if (pt.X == 0 && x > 0 && pt.VMax == 0) continue;
                double vv = Math.Abs(pt.VMax) > Math.Abs(pt.VMin) ? pt.VMax : pt.VMin;
                Lin(LCortante, (x, y0), (x, y0 + vv * esc));
                Valor(LCortante, x + (x < L / 2 ? 2 : -2) * k, y0 + vv * esc + (vv > 0 ? k : -ht - k), T("kN", vv).V, x < L / 2 ? 0 : 2);
            }
        }
        // Md: envoltória; positivo (tração embaixo) para baixo, como na prancha
        double yM = y0 - 2 * AlturaDiagrama * k - Folga * k;
        {
            double mmax = Math.Max(1e-9, env.Max(p => Math.Max(Math.Abs(p.MMax), Math.Abs(p.MMin))));
            double esc = AlturaDiagrama * k / mmax;
            var un = T("kN·m", 1).S;
            Txt($"Md ({un})", L / 2, yM + AlturaDiagrama * k + 2 * ht, hTit, 1, LTextos);
            Eixo(yM, LFletor);
            Poli(LFletor, env.Select(p => (p.X, yM - p.MMax * esc)));
            if (r.Modelos.Count > 1) Poli(LFletor, env.Select(p => (p.X, yM - p.MMin * esc)));
            var pMax = env.OrderByDescending(p => p.MMax).First();
            Lin(LFletor, (pMax.X, yM), (pMax.X, yM - pMax.MMax * esc));
            Valor(LFletor, pMax.X, yM - pMax.MMax * esc - ht - k, T("kN·m", pMax.MMax).V, 1);
            if (r.Modelos.Count > 1)
                foreach (double x in new[] { xa, xb })
                {
                    var pt = env.Where(p => Math.Abs(p.X - x) < 0.01).OrderBy(p => p.MMin).FirstOrDefault();
                    if (pt.MMin > -1e-6) continue;
                    Lin(LFletor, (x, yM), (x, yM - pt.MMin * esc));
                    Valor(LFletor, x + (x < L / 2 ? 2 : -2) * k, yM - pt.MMin * esc + k, T("kN·m", pt.MMin).V, x < L / 2 ? 0 : 2);
                }
        }
        // δ: elástica (pior dos modelos) e, embaixo, com fissuração e fluência; para baixo +
        double yD = yM - 2 * AlturaDiagrama * k - Folga * k;
        {
            var elast = r.Modelos[0].Els.Select((p, i) => (p.X, r.Modelos.Max(m => m.Els[i].Flecha))).ToList();
            var total = r.Modelos.All(m => m.FlechaTotal.Count > 0)
                ? r.Modelos[0].FlechaTotal.Select((p, i) => (p.X, r.Modelos.Max(m => m.FlechaTotal[i].Flecha))).ToList() : null;
            double fmax = Math.Max(1e-9, (total ?? elast).Max(p => Math.Abs(p.Item2)));
            double esc = AlturaDiagrama * k / fmax;
            Txt("δ (mm)", L / 2, yD + 2 * ht + 6 * k, hTit, 1, LTextos);
            Eixo(yD, LDeslocada);
            Poli(LDeslocada, elast.Select(p => (p.X, yD - p.Item2 * esc)));
            var mE = elast.OrderByDescending(p => p.Item2).First();
            Valor(LDeslocada, mE.X, yD - mE.Item2 * esc + k, mE.Item2 * 10, 1);
            Txt("elástica (Ecs·Ic)", L + 4 * k, yD - mE.Item2 * esc, ht, 0, LDeslocada);
            if (total != null)
            {
                Poli(LDeslocadaTotal, total.Select(p => (p.X, yD - p.Item2 * esc)));
                var mT = total.OrderByDescending(p => p.Item2).First();
                Valor(LDeslocadaTotal, mT.X, yD - mT.Item2 * esc - ht - k, mT.Item2 * 10, 1);
                Txt($"com fissuração e fluência (αf = {N(r.AlfaF)}); limite ℓ/250 = {N(r.Vao / 250 * 10, "0.0")}", L + 4 * k, yD - mT.Item2 * esc, ht, 0, LDeslocadaTotal);
            }
        }

        // ------------------------------------------------ memória de cálculo no espaço reservado, abaixo dos diagramas (etapa 5)
        {
            double yMem = yD - AlturaDiagrama * k - Folga * k - 2 * hTit;
            foreach (var (texto, titulo) in MemoriaAnaliseViga.Linhas(r, ent, calc, v["Nome"], u))
            {
                if (titulo) yMem -= 0.8 * ht;
                Txt(texto, 0, yMem, titulo ? 1.25 * ht : ht, 0, LMemoria);
                yMem -= (titulo ? 2.0 : 1.6) * ht;
            }
        }

        // ------------------------------------------------ tabelas à direita do modelo
        // tabelas à direita do modelo, afastadas como o usuário arrumou no desenho de teste (+350 em 1:50)
        double xt = L + 390 * k, yt0 = yCargas + 3 * 25 * k + 2 * hTit;
        string Un(string interna) => T(interna, 1).S;
        var tl = new TabelaTemplate { Titulo = TabelasAnaliseViga.TLaje, X = xt, Y = yt0, Escala = escala };
        tl.Larguras.AddRange(new double[] { 7, 7, 6, 7, 4, 9, 6 });
        tl.Linhas.Add(new[] { "TIPO", "VALOR", "UN.", "VÃO INF.", "UN.", "CARGA LINEAR", "UN." });
        foreach (var (nome, c) in new[] { ("PP,laje", ent.LajePP), ("CP,laje", ent.LajeCP), ("SC,laje", ent.LajeSC) })
            tl.Linhas.Add(new[] { nome, N(T("kN/m²", c.Valor).V), Un("kN/m²"), N(c.VaoInfluencia), "m", N(T("kN/m", c.Linear).V), Un("kN/m") });
        o.Tabelas.Add(tl);
        var tv = new TabelaTemplate { Titulo = TabelasAnaliseViga.TViga, X = xt, Y = yt0 - (tl.AlturaEmTextos() + 4) * ht, Escala = escala };
        tv.Larguras.AddRange(new double[] { 7, 9, 6 });
        tv.Linhas.Add(new[] { "TIPO", "CARGA LINEAR", "UN." });
        tv.Linhas.Add(new[] { "PP,laje", N(T("kN/m", ent.LajePP.Linear).V), Un("kN/m") });
        tv.Linhas.Add(new[] { "CP,laje", N(T("kN/m", ent.LajeCP.Linear).V), Un("kN/m") });
        tv.Linhas.Add(new[] { "SC,laje", N(T("kN/m", ent.LajeSC.Linear).V), Un("kN/m") });
        tv.Linhas.Add(new[] { "PP,viga", N(T("kN/m", r.PPViga).V), Un("kN/m") });
        tv.Linhas.Add(new[] { "CP,viga", N(T("kN/m", ent.VigaCP).V), Un("kN/m") });
        tv.Linhas.Add(new[] { "SC,viga", N(T("kN/m", ent.VigaSC).V), Un("kN/m") });
        o.Tabelas.Add(tv);
        double yProx = tv.Y - (tv.AlturaEmTextos() + 4) * ht;
        {
            var tp = new TabelaTemplate { Titulo = TabelasAnaliseViga.TPontuais, X = xt, Y = yProx, Escala = escala };
            tp.Larguras.AddRange(new double[] { 7, 7, 7, 6 });
            tp.Linhas.Add(new[] { "X (cm)", "G", "Q", "UN." });
            foreach (var p in ent.Pontuais) tp.Linhas.Add(new[] { N(p.X, "0.#"), N(T("kN", p.G).V), N(T("kN", p.Q).V), Un("kN") });
            for (int i = 0; i < 2; i++) tp.Linhas.Add(new[] { "", "", "", Un("kN") });   // linhas livres para novas cargas
            o.Tabelas.Add(tp);
            yProx = tp.Y - (tp.AlturaEmTextos() + 4) * ht;
        }
        var tpar = new TabelaTemplate { Titulo = TabelasAnaliseViga.TParametros, X = xt, Y = yProx, Escala = escala };
        tpar.Larguras.AddRange(new double[] { 22, 7, 4 });
        tpar.Linhas.AddRange(TabelasAnaliseViga.LinhasParametros(ent));
        o.Tabelas.Add(tpar);
        yProx = tpar.Y - (tpar.AlturaEmTextos() + 4) * ht;
        var tm = new TabelaTemplate { Titulo = TabelasAnaliseViga.TModelos, X = xt, Y = yProx, Escala = escala };
        tm.Larguras.AddRange(new double[] { 22, 6, 6, 11, 11, 11 });
        tm.Linhas.Add(new[] { "MODELO", "αR,E", "αR,D", $"Rsec ({Un("kN·m")}/rad)", $"Md,máx ({Un("kN·m")})", $"Md,mín ({Un("kN·m")})" });
        foreach (var m in r.Modelos)
            tm.Linhas.Add(new[] { m.Nome, N(m.AlfaRE), N(m.AlfaRD), m.RsecE <= 0 ? "0" : double.IsInfinity(m.RsecE) ? "∞" : N(T("kN·m", m.RsecE).V, "0"),
                                  N(T("kN·m", m.Elu.Max(p => p.M)).V), N(T("kN·m", m.Elu.Min(p => p.M)).V) });
        o.Tabelas.Add(tm);
        foreach (var a in r.Avisos) o.Avisos.Add(a);
        return o;
    }
}
