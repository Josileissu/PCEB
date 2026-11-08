using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;

namespace DEB.Core.Templates;

/// <summary>Idioma dos textos do detalhamento (pedido do usuário, 2026-10-29: PT, EN ou bilíngue).</summary>
public enum IdiomaDesenho { PT, EN, PTEN }

/// <summary>
/// Primitivas de desenho sobre um <see cref="ResultadoTemplate"/> numa escala: textos conforme o idioma, linhas,
/// retângulos, cotas e chamadas com seta. Usadas pelos detalhes típicos comuns aos templates e à prancha do motor.
/// </summary>
public sealed class DesenhoTemplate
{
    public ResultadoTemplate R { get; }
    public IdiomaDesenho Idioma { get; }
    /// <summary>Escala (1:n) do desenho.</summary>
    public double escV { get; }
    /// <summary>Altura de texto (cm) nessa escala.</summary>
    public double hV { get; }

    public DesenhoTemplate(ResultadoTemplate r, IdiomaDesenho idioma, double escala, double alturaTexto)
    {
        R = r ?? throw new ArgumentNullException(nameof(r));
        Idioma = idioma; escV = escala; hV = alturaTexto;
    }

    /// <summary>Altura de texto (cm) numa outra escala.</summary>
    public double Ht(double esc) => hV / escV * esc;

    /// <summary>Linhas do texto conforme o idioma: PT, EN, ou PT e "(EN)" embaixo.</summary>
    public string[] Linhas(string pt, string en) => Idioma switch
    {
        IdiomaDesenho.PT => new[] { pt },
        IdiomaDesenho.EN => new[] { en.Length > 0 ? en : pt },
        _ => en.Length > 0 && en != pt ? new[] { pt, $"({en})" } : new[] { pt },
    };

    public void Texto(string s, double x, double y, double h, int alinhamento = 0, double angulo = 0, string layer = "ARM-TEXTO-FORMA") =>
        R.Textos.Add(new TextoTemplate { Texto = s, X = x, Y = y, Altura = h, Alinhamento = alinhamento, Angulo = angulo, Layer = layer });

    /// <summary>Bloco de linhas (de cima para baixo) a partir de y; devolve o y abaixo da última linha.</summary>
    public double Bloco(IEnumerable<string> linhas, double x, double y, double h, int alinhamento = 0, double entre = 1.6)
    {
        foreach (var l in linhas) { Texto(l, x, y, h, alinhamento); y -= entre * h; }
        return y;
    }

    public void Linha(double x1, double y1, double x2, double y2, string layer = "ARM-FORMA") =>
        R.Contornos.Add(new ContornoTemplate { Vertices = { (x1, y1), (x2, y2) }, Layer = layer });

    public void Retangulo(double x1, double y1, double x2, double y2, string layer = "ARM-FORMA") =>
        R.Contornos.Add(new ContornoTemplate { Vertices = { (x1, y1), (x2, y1), (x2, y2), (x1, y2) }, Fechado = true, Layer = layer });

    public void Cota(double x1, double y1, double x2, double y2, double px, double py, double angulo, double escala, string texto = "") =>
        R.Cotas.Add(new CotaTemplate { P1 = (x1, y1), P2 = (x2, y2), Posicao = (px, py), Angulo = angulo, Escala = escala, Texto = texto });

    /// <summary>Linha de chamada com seta cheia na ponta (como as do detalhe 01).</summary>
    public void Seta((double X, double Y) de, (double X, double Y) para, double ht, string layer)
    {
        Linha(de.X, de.Y, para.X, para.Y, layer);
        double dx = para.X - de.X, dy = para.Y - de.Y, l = Math.Sqrt(dx * dx + dy * dy);
        if (l < 1e-9) return;
        double ux = dx / l, uy = dy / l, comp = 0.8 * ht, larg = 0.25 * ht;
        var tri = new List<(double, double)>
        {
            para, (para.X - ux * comp - uy * larg, para.Y - uy * comp + ux * larg), (para.X - ux * comp + uy * larg, para.Y - uy * comp - ux * larg),
        };
        R.Contornos.Add(new ContornoTemplate { Vertices = tri, Fechado = true, Layer = layer });
        R.Hachuras.Add(new HachuraTemplate { Vertices = tri, Padrao = "SOLID", Layer = layer });
    }
}

/// <summary>
/// Detalhes típicos da prancha do escritório, comuns a vários elementos (pilar pré-moldado, blocos, prancha do motor):
/// DETALHE 01 - CHAVES DE CISALHAMENTO e o detalhe típico sem escala (raio de curvatura, emendas por traspasse e por
/// solda, comprimento das barras). Saíram do template do pilar (2026-11-07) para o motor não depender dele.
/// </summary>
public static class DetalhesTipicos
{
    /// <summary>
    /// Chaves (opcional) + detalhe típico, lado a lado a partir de x0. Devolve o x depois do último detalhe;
    /// <paramref name="yBaixo"/> = y abaixo de tudo.
    /// </summary>
    public static double Desenhar(ResultadoTemplate r, IdiomaDesenho idioma, double escala, double alturaTextoMm,
        double x0, double yTopo, bool chaves, double passo, double dente, double prof, out double yBaixo)
    {
        double mm = alturaTextoMm > 0 ? alturaTextoMm : 2;
        var c = new DesenhoTemplate(r, idioma, escala, mm / 10 * escala);
        double yb = yTopo, x = x0;
        if (chaves) x = Chaves(c, x, yTopo, passo, dente, prof, ref yb);
        x = Tipico(c, x, yTopo, ref yb);
        yBaixo = yb;
        return x;
    }

    private static double Ht(DesenhoTemplate c, double esc) => c.Ht(esc);
    private static void Seta(DesenhoTemplate c, (double X, double Y) de, (double X, double Y) para, double ht, string layer) => c.Seta(de, para, ht, layer);

    /// <summary>DETALHE 01 - CHAVES DE CISALHAMENTO (1:10, como o P61); devolve o x do próximo detalhe.</summary>
    public static double Chaves(DesenhoTemplate c, double x, double yTopo, double passo, double dente, double prof, ref double yBaixo)
    {
        var r = c.R;
        double hv = c.hV;
        double Titulo(string pt, string en, double xt, double yt, double esc, string escala)
        {
            double ht = Ht(c, esc);
            var linhas = c.Linhas(pt, en);
            foreach (var t in linhas) { c.Texto(t, xt, yt, 1.3 * ht); yt -= 2 * ht; }
            c.Texto(escala, xt, yt, 0.8 * ht);
            return xt + linhas.Max(l => l.Length) * 0.75 * 1.3 * ht + 0.5 * hv;
        }
        double fimTitulo;
        double esc = 10, ht = Ht(c, esc);
        fimTitulo = Titulo("DETALHE 01 - CHAVES DE CISALHAMENTO", "DETAIL 01 - SHEAR KEYS", x, yTopo, esc, "ESC 1:10");
        // círculo de ~5 passos de raio; a face do pilar um pouco à esquerda do centro; o pilar termina embaixo
        double raio = 5 * passo;
        double cx = x + raio + 14 * ht, cy = yTopo - 9 * ht - raio;
        double fx = cx - 0.16 * raio, yFim = cy - 0.55 * raio;
        int n = (int)Math.Ceiling((cy + raio - yFim) / passo) + 1;
        var (perfil, arestas) = PerfilChaves(passo, dente, prof, n);
        // perfil (x = 0 na face) recortado pelo círculo
        for (int k = 0; k + 1 < perfil.Count; k++)
        {
            var seg = DentroDoCirculo((fx + perfil[k].X, yFim + perfil[k].Y), (fx + perfil[k + 1].X, yFim + perfil[k + 1].Y), cx, cy, raio);
            if (seg is { } sg) c.Linha(sg.a.X, sg.a.Y, sg.b.X, sg.b.Y, "ARM-CHAVE");
        }
        // arestas (as duas bordas de cada chanfro) e o fim do pilar, do círculo até o perfil
        double XCirculo(double yy) => cx - Math.Sqrt(Math.Max(0, raio * raio - (yy - cy) * (yy - cy)));
        foreach (var (xa, ya) in arestas.Prepend((perfil[0].X, 0.0)))
        {
            double yy = yFim + ya;
            if (Math.Abs(yy - cy) >= raio - 1e-6) continue;
            c.Linha(XCirculo(yy), yy, fx + xa, yy, "ARM-CHAVE");
        }
        r.Circulos.Add(new CirculoTemplate { X = cx, Y = cy, Raio = raio, Layer = "ARM-DETALHE" });
        // cotas: dente / transição alternados (4 | 1) e o passo (5), nos trechos inteiros dentro do círculo
        double xc1 = fx + 0.32 * raio, xc2 = fx + 0.62 * raio, yMax = cy + 0.62 * raio;
        for (int k = 0; k < n; k++)
        {
            double y0 = yFim + k * passo;
            if (y0 + passo > yMax) break;
            if (k > 0 || y0 + dente < yMax) c.Cota(fx, y0, fx, y0 + dente, xc1, y0 + dente / 2, Math.PI / 2, esc);
            c.Cota(fx, y0 + dente, fx, y0 + passo, xc1, y0 + dente + (passo - dente) / 2, Math.PI / 2, esc);
            if (y0 + dente + passo <= yMax) c.Cota(fx, y0 + dente, fx, y0 + dente + passo, xc2, y0 + dente + passo / 2, Math.PI / 2, esc);
        }
        // chamadas com seta: "CHAVES DE CISALHAMENTO" para um trecho recuado e um na face; "PILAR" para a borda do círculo
        double txC = cx - raio - 1.5 * ht, tyC = cy + 0.5 * raio;
        var linhasC = c.Linhas("CHAVES DE CISALHAMENTO", "SHEAR KEY");
        double larg = linhasC.Max(t => t.Length) * 0.75 * 0.9 * ht;
        double yt2 = tyC;
        foreach (var t in linhasC) { c.Texto(t, txC, yt2, 0.9 * ht, 2); yt2 -= 1.4 * ht; }
        double ySub = tyC - 0.35 * ht;
        c.Linha(txC - larg, ySub, txC + 0.5 * ht, ySub, "ARM-DETALHE");
        int kr = Math.Max(1, (int)Math.Floor((cy - yFim) / passo));       // recuado (passo par) perto do centro
        int kf = kr % 2 == 0 ? kr + 1 : kr - 1;                             // na face
        foreach (int k in new[] { kr, kf })
        {
            double xr = k % 2 == 0 ? -prof : 0;
            Seta(c, (txC + 0.5 * ht, ySub), (fx + xr, yFim + k * passo + dente / 2), ht, "ARM-DETALHE");
        }
        double ang = 1.25 * Math.PI, pxP = cx + raio * Math.Cos(ang), pyP = cy + raio * Math.Sin(ang);
        double txP = pxP - 4 * ht, tyP = pyP - 3 * ht;
        var linhasP = c.Linhas("PILAR", "COLUMN");
        double yp = tyP;
        foreach (var t in linhasP) { c.Texto(t, txP, yp, 0.9 * ht, 1); yp -= 1.4 * ht; }
        double largP = linhasP.Max(t => t.Length) * 0.75 * 0.9 * ht;
        c.Linha(txP - largP / 2 - 0.5 * ht, tyP - 0.35 * ht, txP + largP / 2 + 0.5 * ht, tyP - 0.35 * ht, "ARM-DETALHE");
        Seta(c, (txP + largP / 2 + 0.5 * ht, tyP - 0.35 * ht), (pxP, pyP), ht, "ARM-DETALHE");
        tyP = yp;
        x = Math.Max(cx + raio + 6 * ht, xc2 + 4 * ht);
        x = Math.Max(x, fimTitulo + 4 * hv);
        yBaixo = Math.Min(yBaixo, tyP - 3 * ht);
        return x;
    }

    /// <summary>
    /// Perfil das chaves de cisalhamento (x = 0 na face do pilar, x &lt; 0 para dentro; y para cima), como o detalhe 01 do
    /// escritório (referencia_chaves_cisalhamento.png): trechos retos de "dente" (4) alternando recuado "prof" (1) e na face,
    /// ligados por chanfros de "passo − dente" (1). Devolve também as arestas (bordas dos chanfros) = linhas do pilar.
    /// </summary>
    public static (List<(double X, double Y)> Perfil, List<(double X, double Y)> Arestas) PerfilChaves(double passo, double dente, double prof, int n)
    {
        // onda trapezoidal: trechos retos de "dente" alternando recuado (x = −prof) e na face (x = 0), ligados por chanfros de
        // passo − dente; cada chanfro tem duas arestas (as linhas horizontais do pilar), como o detalhe 01 do escritório
        double t = Math.Max(0, passo - dente);
        var p = new List<(double X, double Y)>();
        var a = new List<(double X, double Y)>();
        for (int k = 0; k < n; k++)
        {
            double y0 = k * passo, xk = k % 2 == 0 ? -prof : 0, xn = k % 2 == 0 ? 0 : -prof;
            if (k == 0) p.Add((xk, y0));
            p.Add((xk, y0 + dente));
            p.Add((xn, y0 + passo));
            a.Add((xk, y0 + dente));
            a.Add((xn, y0 + passo));
        }
        return (p, a);
    }

    /// <summary>Trecho do segmento dentro do círculo (null se fica todo fora).</summary>
    private static ((double X, double Y) a, (double X, double Y) b)? DentroDoCirculo((double X, double Y) p, (double X, double Y) q, double cx, double cy, double r)
    {
        double dx = q.X - p.X, dy = q.Y - p.Y, fx = p.X - cx, fy = p.Y - cy;
        double A = dx * dx + dy * dy, B = 2 * (fx * dx + fy * dy), C = fx * fx + fy * fy - r * r;
        if (A < 1e-12) return C <= 0 ? (p, q) : null;
        double disc = B * B - 4 * A * C;
        if (disc <= 0) return null;
        double s = Math.Sqrt(disc), t0 = Math.Max(0, (-B - s) / (2 * A)), t1 = Math.Min(1, (-B + s) / (2 * A));
        if (t1 - t0 < 1e-9) return null;
        return ((p.X + t0 * dx, p.Y + t0 * dy), (p.X + t1 * dx, p.Y + t1 * dy));
    }

    /// <summary>Barra do esquema (retângulo hachurado de espessura e), de x0 a x1 em y (base).</summary>
    private static void BarraEsquema(DesenhoTemplate c, double x0, double x1, double y, double e)
    {
        var ret = new List<(double, double)> { (x0, y), (x1, y), (x1, y + e), (x0, y + e) };
        c.R.Contornos.Add(new ContornoTemplate { Vertices = ret, Fechado = true, Layer = "ARM-DETALHE" });
        c.R.Hachuras.Add(new HachuraTemplate { Vertices = ret, EscalaPadrao = 0.15 * e, Layer = "ARM-HACHURA-DETALHE" });
    }

    /// <summary>
    /// Detalhe típico sem escala (padrão da prancha do escritório): raio de curvatura (ganchos desenhados + tabela),
    /// emendas por traspasse, emendas por solda (com o corte a-a), comprimento das barras e notas PT/EN em colunas.
    /// </summary>
    public static double Tipico(DesenhoTemplate c, double x0, double yTopo, ref double yBaixo)
    {
        var r = c.R;
        double h = c.hV, x = x0;
        bool en = c.Idioma == IdiomaDesenho.EN;
        string semEscala = c.Idioma switch { IdiomaDesenho.EN => "NOT TO SCALE", IdiomaDesenho.PT => "SEM ESCALA", _ => "SEM ESCALA (NOT TO SCALE)" };
        double TituloT(string pt, string ing, double xt, double yt, double largura, string norma = "")
        {
            foreach (var t in c.Linhas(pt, ing)) { c.Texto(t, xt + largura / 2, yt, 1.1 * h, 1); yt -= 1.7 * h; }
            c.Linha(xt, yt + 0.6 * h, xt + largura, yt + 0.6 * h, "ARM-DETALHE");
            c.Texto(semEscala, xt, yt - 0.3 * h, 0.6 * h);
            if (norma.Length > 0) c.Texto(norma, xt + largura, yt - 0.3 * h, 0.6 * h, 2);
            return yt - 2.5 * h;
        }

        // ---------- raio de curvatura ----------
        double larg1 = 40 * h;
        double y = TituloT("RAIO (r) DE CURVATURA PARA GANCHOS", "BEND RADIUS (r) FOR HOOKS", x, yTopo, larg1);
        // gancho de 180° (2xØ) e de 90° (8xØ), esquemáticos
        double e = 0.6 * h, gx = x + 1.5 * h, gy = y - 2 * h;
        double rr = 1.2 * h;
        r.Contornos.Add(new ContornoTemplate
        {
            Vertices = { (gx + 1.6 * h, gy), (gx + rr, gy), (gx + rr, gy - 2 * rr), (gx + 8 * h, gy - 2 * rr) }, Bulges = { 0, 1, 0, 0 }, Layer = "ARM-DETALHE",
        });
        c.Texto("2xØ", gx + 1.6 * h, gy + 0.5 * h, 0.7 * h);
        c.Texto("r", gx + 1.6 * h, gy - rr - 0.3 * h, 0.7 * h);
        c.Texto("Ø", gx + 8.5 * h, gy - 2 * rr, 0.7 * h);
        double gy2 = gy - 6 * h;
        r.Contornos.Add(new ContornoTemplate
        {
            Vertices = { (gx, gy2 + 4 * h), (gx, gy2 + rr), (gx + rr, gy2), (gx + 8 * h, gy2) }, Bulges = { 0, 0.4142, 0, 0 }, Layer = "ARM-DETALHE",
        });
        c.Cota(gx - 0.8 * h, gy2 + rr, gx - 0.8 * h, gy2 + 4 * h, gx - 1.8 * h, gy2 + 2.5 * h, Math.PI / 2, c.escV, "8XØ");
        c.Texto("r", gx + 1.2 * h, gy2 + 1.2 * h, 0.7 * h);
        c.Texto("Ø", gx + 8.5 * h, gy2, 0.7 * h);
        var tab = new TabelaTemplate { Titulo = "", X = x + 11 * h, Y = y, Escala = c.escV };
        tab.Larguras.AddRange(new[] { 11.0, 7.0, 7.0 });
        tab.Alinhamentos.AddRange(new[] { 1, 1, 1 });
        tab.Titulo = c.Idioma == IdiomaDesenho.PTEN ? "BITOLA (Ø)" + TabelaTemplate.Quebra + "(DIAMETER)" : (en ? "DIAMETER (Ø)" : "BITOLA (Ø)");
        tab.Linhas.Add(new[] { "", "CA50", "CA60" });
        tab.Linhas.Add(new[] { "< 20 mm", "2,5xØ", "3xØ" });
        tab.Linhas.Add(new[] { "≥ 20 mm", "4xØ", "—" });
        tab.Linhas.Add(new[] { (en ? "STIRRUPS" : c.Idioma == IdiomaDesenho.PTEN ? "ESTRIBO" + TabelaTemplate.Quebra + "(STIRRUPS)" : "ESTRIBO") + TabelaTemplate.Quebra + "≤ 10 mm", "1,5xØ", "1,5xØ" });
        r.Tabelas.Add(tab);
        double yTab = y - (tab.AlturaEmTextos() + 2) * h;
        c.Texto("NBR-6118", x + 11 * h, yTab, 0.7 * h);
        // comprimento das barras (U com as dobras arredondadas)
        double yc = Math.Min(yTab, gy2) - 4 * h;
        yc = TituloT("COMPRIMENTO DAS BARRAS", "REBAR LENGTH", x, yc, larg1);
        double ux = x + 4 * h, uy = yc - 9 * h, uw = 28 * h, uh = 6 * h, ur = 1.2 * h;
        r.Contornos.Add(new ContornoTemplate
        {
            Vertices = { (ux, uy + uh), (ux, uy + ur), (ux + ur, uy), (ux + uw - ur, uy), (ux + uw, uy + ur), (ux + uw, uy + uh) },
            Bulges = { 0, 0.4142, 0, 0.4142, 0, 0 }, Layer = "ARM-DETALHE",
        });
        c.Cota(ux - h, uy, ux - h, uy + uh, ux - 2.2 * h, uy + uh / 2, Math.PI / 2, c.escV, "A1");
        c.Cota(ux, uy - h, ux + uw, uy - h, ux + uw / 2, uy - 2.2 * h, 0, c.escV, "B1");
        c.Texto("r", ux + 1.6 * h, uy + 1.4 * h, 0.7 * h);
        c.Texto("r", ux + uw - 2.2 * h, uy + 1.4 * h, 0.7 * h);
        c.Texto("Ø", ux + uw / 2, uy + 0.8 * h, 0.7 * h, 1);
        yBaixo = Math.Min(yBaixo, uy - 4 * h);
        x += larg1 + 8 * h;

        // ---------- emendas por traspasse ----------
        double larg2 = 46 * h;
        y = TituloT("EMENDAS POR TRASPASSE", "LAP SPLICES", x, yTopo, larg2, "NBR-6118");
        double comp = 26 * h, sob = 10 * h;
        double b1 = y - 2 * h;
        BarraEsquema(c, x, x + comp, b1, e);
        BarraEsquema(c, x + comp - sob, x + 2 * comp - sob, b1 - 1.4 * h, e);
        c.Cota(x + comp - sob, b1 - 2.4 * h, x + comp, b1 - 2.4 * h, x + comp - sob / 2, b1 - 3.6 * h, 0, c.escV, "≥ 45ø");
        c.Texto("Ø", x + comp - sob - 2 * h, b1 + 1.2 * h, 0.7 * h);
        double b2 = b1 - 7 * h, gap = 6 * h;
        BarraEsquema(c, x, x + comp + gap, b2, e);
        BarraEsquema(c, x + comp - sob + gap * 2, x + 2 * comp, b2 - 2.2 * h, e);
        c.Cota(x + comp - sob + gap, b2 - 3.4 * h, x + comp - sob + 2 * gap, b2 - 3.4 * h, x + comp - sob + 1.5 * gap, b2 - 4.6 * h, 0, c.escV, "≥ 9ø");
        c.Cota(x + comp - sob + 2 * gap, b2 - 3.4 * h, x + comp + gap, b2 - 3.4 * h, x + comp + gap - (sob - gap) / 2, b2 - 4.6 * h, 0, c.escV, "≥ 45ø");
        double yNotas = b2 - 8 * h;
        var notasPt = new[] { "NOTAS:", "1) ESSE TIPO DE EMENDA NÃO É PERMITIDO PARA:", "    - BARRA MAIOR QUE ø32 mm;",
            "2) INDICAÇÕES VÁLIDAS QUANDO NÃO HOUVER", "    RECOMENDAÇÕES ADVERSAS ANOTADAS EM PROJETO;",
            "3) A1 E B1 REPRESENTAM OS VALORES INDICADOS", "    NOS DETALHAMENTOS DAS ARMADURAS." };
        var notasEn = new[] { "NOTES:", "1) THIS TYPE OF SPLICE IS NOT ALLOWED FOR:", "    - BARS LARGER THAN ø32 mm;",
            "2) VALID INDICATIONS WHEN THERE ARE NO", "    ADVERSE RECOMMENDATIONS NOTED IN THE DESIGN;",
            "3) A1 AND B1 DENOTE THE VALUES SPECIFIED", "    IN THE REINFORCEMENT DETAILING." };
        double yfim1 = yNotas, yfim2 = yNotas;
        if (!en) yfim1 = c.Bloco(notasPt, x, yNotas, 0.75 * h);
        x += larg2 + 8 * h;

        // ---------- emendas por solda por traspasse ----------
        double larg3 = 46 * h;
        y = TituloT("EMENDAS POR SOLDA POR TRASPASSE", "LAP WELD SPLICES", x, yTopo, larg3, "NBR-6118");
        b1 = y - 3 * h;
        double cs = 5 * h; // cada trecho de 5ø
        BarraEsquema(c, x, x + 3 * cs + 8 * h, b1, e);
        BarraEsquema(c, x + 8 * h, x + 3 * cs + 20 * h, b1 - e, e);
        foreach (double xs in new[] { x + 8 * h, x + 8 * h + 2 * cs })
        {
            var cord = new List<(double, double)> { (xs, b1 - 0.2 * e), (xs + cs, b1 - 0.2 * e), (xs + cs, b1 + 1.2 * e), (xs, b1 + 1.2 * e) };
            r.Contornos.Add(new ContornoTemplate { Vertices = cord, Fechado = true, Layer = "ARM-DETALHE" });
            r.Hachuras.Add(new HachuraTemplate { Vertices = cord, Padrao = "ANSI37", EscalaPadrao = 0.2 * e, Layer = "ARM-HACHURA-DETALHE" });
        }
        for (int k = 0; k < 3; k++)
            c.Cota(x + 8 * h + k * cs, b1 - 2.2 * h, x + 8 * h + (k + 1) * cs, b1 - 2.2 * h, x + 8 * h + (k + 0.5) * cs, b1 - 3.4 * h, 0, c.escV, "≥ 5ø");
        // corte a-a: as duas barras e o cordão (≥ 0,3ø)
        double ax = x + 3 * cs + 26 * h, ay = b1 + 1.5 * h, ra = 1.5 * h;
        c.Texto("a-a", ax, ay + ra + 1.5 * h, 0.7 * h, 1);
        r.Circulos.Add(new CirculoTemplate { X = ax, Y = ay, Raio = ra, Layer = "ARM-DETALHE" });
        r.Circulos.Add(new CirculoTemplate { X = ax, Y = ay - 2 * ra, Raio = ra, Layer = "ARM-DETALHE" });
        var cordA = new List<(double, double)> { (ax - ra, ay - ra), (ax - 0.4 * ra, ay - 0.8 * ra), (ax - 0.4 * ra, ay - 1.2 * ra) };
        r.Hachuras.Add(new HachuraTemplate { Vertices = cordA, Padrao = "SOLID", EscalaPadrao = 1, Layer = "ARM-HACHURA-DETALHE" });
        c.Texto("≥ 0,3ø", ax + 0.5 * ra, ay - 3.6 * ra, 0.7 * h);
        if (!en && c.Idioma == IdiomaDesenho.PTEN) yfim2 = c.Bloco(notasEn, x, yNotas, 0.75 * h);
        else if (en) yfim2 = c.Bloco(notasEn, x, yNotas, 0.75 * h);
        x += larg3 + 8 * h;
        yBaixo = Math.Min(yBaixo, Math.Min(yfim1, yfim2) - 2 * h);
        return x;
    }
}
