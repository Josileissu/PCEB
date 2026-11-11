using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Estribos;
using DEB.Core.Modelo;

namespace DEB.Core.Templates.VigaPreMoldada;

/// <summary>
/// Etapa 3 da viga (2026-11-08): armadura do dente Gerber do catálogo (NBR 9062 7.4; padrão das pranchas 316-6208/6219):
/// estribos Gerber dentro do dente e suspensão junto da face do recorte (desenhados na elevação, com a indicação N), grampo,
/// costura e tirante fora da forma (linhas com a função, como as longitudinais), o tirante e a armadura inferior com a barra
/// transversal soldada na ponta ("VER DET. DE SOLDA"), o dente na seção de armadura e as formas dos estribos do dente.
/// </summary>
public sealed partial class TemplateVigaPreMoldada
{
    private static string Sufixo(bool esquerda) => esquerda ? "_E" : "_D";

    /// <summary>x (eixo) dos estribos Gerber dentro do dente, divididos por igual entre as faces (a cobrimento).</summary>
    public static List<double> PosicoesGerber(TipoDente t, double cobPontas, bool esquerda, double L)
    {
        double phi = t.EstGerberBitola / 10, a = cobPontas + phi / 2, z = t.Comprimento - cobPontas - phi / 2;
        int n = Math.Max(0, t.EstGerberQtd);
        var xs = n <= 0 || z < a ? new List<double>() : n == 1 ? new List<double> { (a + z) / 2 }
            : Enumerable.Range(0, n).Select(i => a + i * (z - a) / (n - 1)).ToList();
        return esquerda ? xs : xs.Select(x => L - x).ToList();
    }

    /// <summary>x (eixo) da suspensão: a partir da face do recorte (a cobrimento), a cada SuspEsp, para dentro da viga.</summary>
    public static List<double> PosicoesSuspensao(TipoDente t, double cobPontas, bool esquerda, double L)
    {
        double x0 = t.Comprimento + cobPontas + t.SuspBitola / 20;
        var xs = Enumerable.Range(0, Math.Max(0, t.SuspQtd)).Select(i => x0 + i * t.SuspEsp).ToList();
        return esquerda ? xs : xs.Select(x => L - x).ToList();
    }

    /// <summary>Comprimento ocupado pela suspensão a partir da face do recorte (os estribos normais começam depois).</summary>
    public static double ExtensaoSuspensao(ExtremidadeViga x) => x.TemDente && x.Dente.SuspQtd > 0 ? x.Dente.SuspQtd * x.Dente.SuspEsp : 0;

    /// <summary>
    /// Estribos Gerber e suspensão na elevação das armaduras: as linhas dentro da forma e, como nas pranchas do escritório
    /// (pedido do usuário, 2026-11-11: "na região próxima ao dente é apresentada a armadura de suspensão, a armadura do dente e as
    /// faixas respectivas"), uma FAIXA COM COTA de cada um na linha <paramref name="yF"/> das faixas dos estribos: o dente (da ponta
    /// à face do recorte, "4 N4" em 25) e a suspensão (da face do recorte ao fim dela, "5 N10" em 20), encadeadas com a faixa dos
    /// estribos da alma. O texto da suspensão fica uma linha abaixo para não encavalar com o do dente.
    /// </summary>
    private static void DenteEmElevacao(Ctx c, ValoresTemplate v, ConfiguracaoArmadura cfg, double L, ExtremidadeViga x, bool esquerda,
        double yT, double yB, double yF)
    {
        if (!x.TemDente) return;
        var t = x.Dente;
        double hv = c.hV, cobF = v.Numero("CobFundo"), cobT = v.Numero("CobTopo"), cob = v.Numero("CobPontas");
        string suf = Sufixo(esquerda);
        double X(double d) => esquerda ? d : L - d;   // distância à ponta → x da elevação
        var g = PosicoesGerber(t, cob, esquerda, L);
        foreach (var xx in g) c.Linha(xx, yT - t.Altura + cobF, xx, yT - cobT, "ARM-BARRA-DETALHE");
        if (g.Count > 0) c.R.Faixas.Add(FaixaContada("gerber" + suf, X(0), X(x.CompDente), yF, g.Count, yF + 0.8 * hv, c.escV, cfg));
        var s = PosicoesSuspensao(t, cob, esquerda, L);
        foreach (var xx in s) c.Linha(xx, yB + cobF, xx, yT - cobT, "ARM-BARRA-DETALHE");
        if (s.Count > 0)
        {
            // até onde começa a faixa dos estribos da alma (ZonasEstribos), para a cadeia de cotas não ter folga
            double fim = x.CompDente + ExtensaoSuspensao(x) + cob + v.Numero("EstBitola") / 20;
            c.R.Faixas.Add(FaixaContada("suspensao" + suf, X(x.CompDente), X(fim), yF, s.Count, yF - 2.4 * hv, c.escV, cfg));
        }
    }

    /// <summary>
    /// Faixa com cota de <paramref name="n"/> barras exatamente entre xa e xb (estribos do dente, suspensão): o espaçamento é o que dá
    /// <paramref name="n"/> pela regra de faixas da configuração, e ele não aparece no texto ("4 N4", não "4 N4 c/6.7").
    /// </summary>
    public static FaixaTemplate FaixaContada(string barra, double xa, double xb, double y, int n, double yTexto, double esc, ConfiguracaoArmadura cfg)
    {
        double comp = Math.Abs(xb - xa), f0 = Math.Max(0, Math.Min(1, cfg.EspacamentoInicialFaixa));
        // Quantidade = piso(q) + 1 (ou teto(q) + 1) com q = comp/s − 2·f0 → q = n − 1; no modo Teto, q = n
        double k = cfg.ModoQuantidadeFaixa == ModoQuantidadeFaixa.Teto ? n : n - 1;
        double s = n <= 1 && k <= 0 ? comp : comp / (k + 2 * f0);
        double x1 = Math.Min(xa, xb), x2 = Math.Max(xa, xb);
        return new FaixaTemplate
        {
            Barra = barra, Tipo = TipoFaixa.Ortogonal, P1 = (x1, y), P2 = (x2, y), Posicao = ((x1 + x2) / 2, yTexto),
            Espacamento = Math.Max(0.1, s), MostrarEspacamento = false, Escala = esc,
        };
    }

    /// <summary>Uma barra do dente desenhada fora da forma, na ponta dela: descrição em cima, função embaixo.</summary>
    private static BarraTemplate BarraDoDente(Ctx c, string nome, double phi, string aco, int qtd, List<(double X, double Y)> pts,
        string pt, string en, double esc)
    {
        double hv = c.hV;
        var bt = new BarraTemplate { Nome = nome, Bitola = phi, Aco = aco, Quantidade = Math.Max(1, qtd), Escala = esc };
        bt.Vertices.AddRange(pts);
        for (int i = 0; i + 1 < pts.Count; i++) bt.Formatos.Add(FormatoSegmento.Linha);
        double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
        bt.PosicaoDescricao = ((x0 + x1) / 2, y1 + 2.6 * hv);
        c.R.Barras.Add(bt);
        c.Texto(string.Join(" / ", c.Linhas(pt, en)), (x0 + x1) / 2, y0 - 3.4 * hv, 0.75 * hv, 1);
        return bt;
    }

    /// <summary>Barra transversal soldada na ponta (círculo + "VER DET. DE SOLDA" + a barra curta descrita na diagonal).</summary>
    private static void BarraSoldada(Ctx c, string nome, double x, double y, double phi, string aco, double comp, bool esquerda, double esc)
    {
        double hv = c.hV, r = Math.Max(1.2, 0.6 * hv);
        c.R.Circulos.Add(new CirculoTemplate { X = x, Y = y, Raio = r, Layer = "ARM-BARRA-DETALHE" });
        double s = esquerda ? -1 : 1;
        // a barra curta na diagonal, saindo do círculo para fora da viga (como o "1 N14 ø25.0 C=35" das pranchas)
        double ang = Math.PI / 4, dx = Math.Cos(ang) * comp, dy = Math.Sin(ang) * comp;   // a barra inteira (largura − 2 cob)
        var bt = new BarraTemplate
        {
            Nome = nome, Bitola = phi, Aco = aco, Quantidade = 1, Escala = esc,
            Vertices = { (x + s * (r + 1), y + r + 1), (x + s * (r + 1) + s * dx, y + r + 1 + dy) },
            Formatos = { FormatoSegmento.Linha },
            PosicaoDescricao = (x + s * (r + 1 + dx / 2) - s * 1.2 * hv, y + r + 1 + dy / 2 + 1.2 * hv),
            RotacaoDescricao = esquerda ? -ang : ang,
        };
        c.R.Barras.Add(bt);
        var txt = c.Linhas("VER DET. DE SOLDA", "SEE WELDING DET.");
        double yy = y - r - 5.2 * hv;   // abaixo da função da barra
        foreach (var t in txt) { c.Texto(t, x, yy, 0.6 * hv, 1); yy -= 0.9 * hv; }
    }

    /// <summary>Grampo, costura e tirante (com a barra soldada) de cada ponta com dente, em linhas abaixo de <paramref name="y"/>.</summary>
    private static double DenteBarras(Ctx c, ValoresTemplate v, double b, double L, ExtremidadeViga e, ExtremidadeViga d, double y)
    {
        double hv = c.hV, esc = c.escV, cob = v.Numero("CobPontas"), cobL = v.Numero("CobLateral");
        string aco = v["LongAco"];
        double w = b - 2 * cobL, passo = 9 * hv;
        // as barras das duas pontas dividem a linha só se não se cruzarem (viga curta: a da direita vai na linha de baixo)
        bool Cruzam(double alcanceE, double alcanceD) => cob + alcanceE + 8 * hv > L - cob - alcanceD;
        // tirante
        bool tE = e.TemDente && e.Dente.TiranteQtd > 0, tD = d.TemDente && d.Dente.TiranteQtd > 0;
        bool separa = tE && tD && Cruzam(e.Dente.TiranteComp, d.Dente.TiranteComp);
        foreach (var (x, esq) in new[] { (e, true), (d, false) })
        {
            if (!x.TemDente || x.Dente.TiranteQtd <= 0) continue;
            var t = x.Dente;
            if (!esq && separa) y -= passo;
            double x0 = esq ? cob : L - cob, x1 = esq ? cob + t.TiranteComp : L - cob - t.TiranteComp;
            BarraDoDente(c, "tirante" + Sufixo(esq), t.TiranteBitola, aco, t.TiranteQtd, new List<(double, double)> { (x0, y), (x1, y) }, "TIRANTE", "TIE", esc);
            BarraSoldada(c, "solda_tirante" + Sufixo(esq), x0, y, t.TiranteBitola, aco, w, esq, esc);
        }
        if (tE || tD) y -= passo;
        // grampo e costura: U horizontal (vistos de cima) com as pernas entrando na viga
        foreach (var (rotPt, rotEn, nome, sel) in new (string, string, string, Func<TipoDente, (double Phi, int Q, double Perna)>)[]
                 {
                     ("GRAMPO", "HAIRPIN", "grampo", t => (t.GrampoBitola, t.GrampoQtd, t.GrampoPerna)),
                     ("COSTURA", "STITCH", "costura", t => (t.CosturaBitola, t.CosturaQtd, t.CosturaPerna)),
                 })
        {
            bool linha = false;
            bool sepU = e.TemDente && d.TemDente && Cruzam(sel(e.Dente).Perna, sel(d.Dente).Perna);
            foreach (var (x, esq) in new[] { (e, true), (d, false) })
            {
                if (!x.TemDente) continue;
                var (phi, q, perna) = sel(x.Dente);
                if (q <= 0 || phi <= 0) continue;
                if (!esq && sepU && linha) y -= passo + w;
                linha = true;
                double xb = esq ? cob : L - cob, s = esq ? 1 : -1;
                var pts = new List<(double X, double Y)> { (xb + s * perna, y + w), (xb, y + w), (xb, y), (xb + s * perna, y) };
                BarraDoDente(c, nome + Sufixo(esq), phi, aco, q, pts, rotPt, rotEn, esc);
            }
            if (linha) y -= passo + w;
        }
        return y;
    }

    /// <summary>O dente na SEÇÃO X-X - ARMADURA: estribo Gerber, tirante no topo, grampos e costuras (as pernas, nos lados).</summary>
    private static void DenteNaSecao(Ctx c, ValoresTemplate v, TipoDente t, bool esquerda, double b, double sx0, double sy0, double sy1)
    {
        double hc = c.hC, cobF = v.Numero("CobFundo"), cobT = v.Numero("CobTopo"), cobL = v.Numero("CobLateral");
        string suf = Sufixo(esquerda);
        double tg = t.EstGerberBitola / 10;
        if (t.EstGerberQtd > 0)
        {
            c.Retangulo(sx0 + cobL + tg / 2, sy0 + cobF + tg / 2, sx0 + b - cobL - tg / 2, sy1 - cobT - tg / 2, "ARM-BARRA-DETALHE");
            c.R.Faixas.Add(new FaixaTemplate { Barra = "gerber" + suf, Tipo = TipoFaixa.Indicacao, Referencias = { (sx0 + cobL + tg / 2, sy0 + (sy1 - sy0) / 2) },
                Posicao = (sx0 - 5 * hc, sy0 + (sy1 - sy0) / 2), Escala = c.escC });
        }
        double xl = sx0 + cobL + tg, xr = sx0 + b - cobL - tg;
        var pts = new List<(double X, double Y)>();
        void Pontos(string nome, double phi, IEnumerable<(double X, double Y)> ps, (double X, double Y) pos)
        {
            var l = ps.ToList();
            if (l.Count == 0 || phi <= 0) return;
            foreach (var (px, py) in l) c.R.Circulos.Add(new CirculoTemplate { X = px, Y = py, Raio = phi / 20, Layer = "ARM-BARRA-SECAO" });
            c.R.Faixas.Add(new FaixaTemplate { Barra = nome + suf, Tipo = TipoFaixa.Indicacao, Referencias = l, Posicao = pos, Escala = c.escC });
        }
        // tirante: uma camada no topo, dentro do estribo Gerber
        double yTir = sy1 - cobT - tg - t.TiranteBitola / 20;
        int nt = Math.Max(0, t.TiranteQtd);
        Pontos("tirante", t.TiranteBitola, Enumerable.Range(0, nt).Select(i => (nt == 1 ? (xl + xr) / 2 : xl + t.TiranteBitola / 20 + i * (xr - xl - t.TiranteBitola / 10) / (nt - 1), yTir)),
            (sx0 + b / 2, sy1 + 3 * hc));
        // grampos na metade de cima, costuras na de baixo (as duas pernas de cada U)
        double ya = sy0 + cobF + tg, ym = sy0 + (sy1 - sy0) / 2, yb = yTir - t.TiranteBitola / 20 - 1.5;
        IEnumerable<(double, double)> Pernas(int q, double phi, double y0, double y1) =>
            Enumerable.Range(0, Math.Max(0, q)).SelectMany(i =>
            {
                double yy = q == 1 ? (y0 + y1) / 2 : y0 + i * (y1 - y0) / (q - 1);
                return new[] { (xl + phi / 20, yy), (xr - phi / 20, yy) };
            });
        Pontos("grampo", t.GrampoBitola, Pernas(t.GrampoQtd, t.GrampoBitola, ym, yb).Where(p => p.Item1 > sx0 + b / 2), (sx0 + b + 5 * hc, (ym + yb) / 2));
        Pontos("costura", t.CosturaBitola, Pernas(t.CosturaQtd, t.CosturaBitola, ya, ym - 1.5).Where(p => p.Item1 > sx0 + b / 2), (sx0 + b + 5 * hc, (ya + ym) / 2));
    }

    /// <summary>Formas dos estribos do dente (Gerber e suspensão) para a coluna das formas, com a descrição embaixo.</summary>
    private static List<BarraTemplate> FormasDente(ValoresTemplate v, ConfiguracaoArmadura cfg, double b, double h, ExtremidadeViga x, bool esquerda)
    {
        var r = new List<BarraTemplate>();
        if (!x.TemDente) return r;
        var t = x.Dente;
        double cobL = v.Numero("CobLateral"), cobF = v.Numero("CobFundo"), cobT = v.Numero("CobTopo"), w = b - 2 * cobL;
        string aco = v["EstAco"];
        BarraTemplate Fechado(string nome, double phi, double hh, int q)
        {
            var f = GeradorEstribo.FechadoPadrao(new List<(double X, double Y)> { (0, 0), (w, 0), (w, hh), (0, hh) }, phi, aco, cfg.Dobramento);
            var bt = new BarraTemplate { Nome = nome, Bitola = phi, Aco = aco, Estribo = true, Quantidade = Math.Max(1, q) };
            bt.Vertices.AddRange(f.Vertices.Select(p => (p.X, p.Y)));
            bt.Formatos.AddRange(f.Formatos);
            foreach (var s in f.Sobrepostos) bt.Sobrepostos.Add(s);
            foreach (var s in f.Cotados) bt.Cotados.Add(s);
            return bt;
        }
        if (t.EstGerberQtd > 0 && t.EstGerberBitola > 0) r.Add(Fechado("gerber" + Sufixo(esquerda), t.EstGerberBitola, t.Altura - cobF - cobT, t.EstGerberQtd));
        if (t.SuspQtd > 0 && t.SuspBitola > 0) r.Add(Fechado("suspensao" + Sufixo(esquerda), t.SuspBitola, h - cobF - cobT, t.SuspQtd));
        return r;
    }
}
