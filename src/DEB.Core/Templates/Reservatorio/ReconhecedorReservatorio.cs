using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using DEB.Core.Reconhecimento;

namespace DEB.Core.Templates.Reservatorio;

/// <summary>Segmento de reta do desenho (unidades do desenho).</summary>
public sealed class LinhaDesenho
{
    public double X1, Y1, X2, Y2;
    public string Layer = "";
    public LinhaDesenho() { }
    public LinhaDesenho(double x1, double y1, double x2, double y2, string layer) { X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; Layer = layer ?? ""; }
    public double Comprimento => Math.Sqrt((X2 - X1) * (X2 - X1) + (Y2 - Y1) * (Y2 - Y1));
    public bool Horizontal => Math.Abs(Y2 - Y1) <= 1e-3 * Math.Max(1, Comprimento);
    public bool Vertical => Math.Abs(X2 - X1) <= 1e-3 * Math.Max(1, Comprimento);
}

/// <summary>Texto do desenho (já limpo de formatação), com ponto de inserção, altura e rotação (rad).</summary>
public sealed class TextoDesenho
{
    public string Texto = "";
    public double X, Y, Altura, Rotacao;
    public string Layer = "";
}

/// <summary>Resultado do reconhecimento: valores do template e o relatório do que foi lido.</summary>
public sealed class ReservatorioReconhecido
{
    public ValoresTemplate Valores;
    public List<string> Relatorio { get; } = new List<string>();
    public List<string> Avisos { get; } = new List<string>();
    /// <summary>Extensão do desenho original (unidades do desenho): o detalhamento novo vai ao lado.</summary>
    public double XMax, YMax, YMin;
    public bool Ok;
}

/// <summary>
/// Reconhece um reservatório retangular desenhado por outro programa (feito sobre o padrão de saída do Eberick:
/// planta das paredes, cortes e plantas das lajes, com "Escala 1:n" em cada título, desenho em cm de papel ou
/// em cm reais). Lê a geometria das linhas de forma (layer com "forma" no nome; senão todas) e as bitolas e
/// espaçamentos dos textos das barras ("16 N14 ø6.3 c/25 C=405") de cada vista. Resultado: valores do
/// <see cref="TemplateReservatorio"/> (o que não foi achado fica no padrão do template e vai para os avisos).
/// </summary>
public static class ReconhecedorReservatorio
{
    private sealed class Vista
    {
        public string Tipo = "";      // PLANTA, CORTE, LAJES
        public string Titulo = "";
        public double Escala = 1, Fator = 1;
        public double Nivel;          // lajes: "(0.0)" do título
        public bool Positiva, Negativa;
        public List<LinhaDesenho> Linhas = new List<LinhaDesenho>();
        public double X0, Y0, X1, Y1;
        public List<TextoDesenho> Textos = new List<TextoDesenho>();
        public TextoDesenho TextoTitulo;
    }

    private static readonly Regex RxEscala = new Regex(@"(?i)esc(?:ala)?\.?\s*1\s*[:/]\s*(\d+)");
    private static readonly Regex RxNivel = new Regex(@"\((-?\d+(?:[.,]\d+)?)\)");

    public static ReservatorioReconhecido Reconhecer(IList<LinhaDesenho> linhas, IList<TextoDesenho> textos)
    {
        var t = new TemplateReservatorio();
        var res = new ReservatorioReconhecido { Valores = new ValoresTemplate(t.Parametros) };
        var v = res.Valores;
        if (linhas.Count == 0) { res.Avisos.Add("Nenhuma linha selecionada."); return res; }
        res.XMax = linhas.Max(l => Math.Max(l.X1, l.X2)); res.YMax = linhas.Max(l => Math.Max(l.Y1, l.Y2)); res.YMin = linhas.Min(l => Math.Min(l.Y1, l.Y2));

        // ---- 1. vistas: agrupamentos das linhas de forma, cada um com o título mais próximo ----
        // layer de forma: tem "forma" no nome e não é de barra ("DT-Barras na forma" do Eberick são as barras dentro das paredes)
        bool EhForma(string layer) => layer.IndexOf("forma", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                      layer.IndexOf("barra", StringComparison.OrdinalIgnoreCase) < 0 &&
                                      layer.IndexOf("arm", StringComparison.OrdinalIgnoreCase) < 0;
        var forma = linhas.Where(l => EhForma(l.Layer) && l.Comprimento > 0).ToList();
        if (forma.Count < 8) forma = linhas.Where(l => l.Comprimento > 0).ToList();
        var titulos = Titulos(textos);
        if (titulos.Count == 0) { res.Avisos.Add("Nenhum título de vista (PLANTA, CORTE, LAJES) encontrado."); return res; }
        double hMed = Mediana(textos.Where(x => x.Altura > 0).Select(x => x.Altura).ToList());
        var grupos = Agrupar(forma, 4 * Math.Max(hMed, 1e-6));
        var vistas = new List<Vista>();
        foreach (var g in grupos.Where(g => g.Count >= 6))
        {
            var vi = new Vista { Linhas = g };
            vi.X0 = g.Min(l => Math.Min(l.X1, l.X2)); vi.X1 = g.Max(l => Math.Max(l.X1, l.X2));
            vi.Y0 = g.Min(l => Math.Min(l.Y1, l.Y2)); vi.Y1 = g.Max(l => Math.Max(l.Y1, l.Y2));
            vistas.Add(vi);
        }
        // título → vista: pares pelo menor custo. No Eberick o título fica EMBAIXO da vista e alinhado à DIREITA da vista
        // inteira (forma + rebatidas), longe da forma; título acima da forma ou à esquerda dela custa 5×.
        double Custo(Vista x, TextoDesenho tt)
        {
            double w = x.X1 - x.X0, h = x.Y1 - x.Y0;
            bool embaixo = tt.Y <= x.Y0 + 0.1 * h, aDireita = tt.X >= x.X1 - 0.2 * w;
            return DistanciaCaixa(x, tt.X, tt.Y) * (embaixo ? 1 : 5) * (aDireita ? 1 : 5);
        }
        var pares = titulos.SelectMany(tp => vistas.Select(x => (tp, x, custo: Custo(x, tp.Item1)))).OrderBy(q => q.custo).ToList();
        var usados = new HashSet<TextoDesenho>();
        foreach (var ((tt, tipo), livre, _) in pares)
        {
            if (usados.Contains(tt) || livre.TextoTitulo != null) continue;
            usados.Add(tt);
            livre.TextoTitulo = tt; livre.Titulo = tt.Texto; livre.Tipo = tipo;
            var esc = textos.Where(x => RxEscala.IsMatch(x.Texto)).OrderBy(x => Dist(x.X, x.Y, tt.X, tt.Y)).FirstOrDefault();
            if (esc != null && Dist(esc.X, esc.Y, tt.X, tt.Y) < 6 * tt.Altura) livre.Escala = double.Parse(RxEscala.Match(esc.Texto).Groups[1].Value, CultureInfo.InvariantCulture);
            var mn = RxNivel.Match(tt.Texto);
            if (mn.Success) livre.Nivel = double.Parse(mn.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            livre.Positiva = tt.Texto.IndexOf("POSITIV", StringComparison.OrdinalIgnoreCase) >= 0;
            livre.Negativa = tt.Texto.IndexOf("NEGATIV", StringComparison.OrdinalIgnoreCase) >= 0;
            // desenho em "cm de papel" (Eberick): a vista inteira cabe em poucas dezenas de unidades
            double tam = Math.Max(livre.X1 - livre.X0, livre.Y1 - livre.Y0);
            livre.Fator = tam * livre.Escala < 5000 && tam < 150 && livre.Escala > 1 ? livre.Escala : 1;
        }
        vistas = vistas.Where(x => x.TextoTitulo != null).ToList();
        // textos de barra → vista que os contém (com folga) ou a mais próxima
        foreach (var tx in textos)
        {
            if (TextoArmadura.Interpretar(tx.Texto) == null && !Regex.IsMatch(tx.Texto, @"N\d+\s+c/\s*\d")) continue;
            var dono = vistas.OrderBy(x => DistanciaCaixa(x, tx.X, tx.Y) / Math.Max(1e-6, Math.Max(x.X1 - x.X0, x.Y1 - x.Y0))).First();
            if (DistanciaCaixa(dono, tx.X, tx.Y) <= 0.8 * Math.Max(dono.X1 - dono.X0, dono.Y1 - dono.Y0)) dono.Textos.Add(tx);
        }
        foreach (var x in vistas)
            res.Relatorio.Add($"Vista \"{x.Titulo}\": {x.Tipo}, 1:{Num(x.Escala)}{(x.Fator > 1 ? $" (desenho em cm de papel: × {Num(x.Fator)})" : "")}, " +
                              $"{x.Linhas.Count} linhas de forma, {x.Textos.Count} textos de barra.");

        // ---- 2. planta das paredes ----
        var planta = vistas.FirstOrDefault(x => x.Tipo == "PLANTA");
        if (planta == null) { res.Avisos.Add("Planta das paredes não encontrada (título com \"PLANTA\")."); return res; }
        if (!LerPlanta(planta, v, res)) return res;

        // ---- 3. cortes: fundo, altura, lajes superiores ----
        var cortes = vistas.Where(x => x.Tipo == "CORTE").ToList();
        LerCortes(cortes, v, res);

        // ---- 4. bitolas e espaçamentos ----
        LerArmaduras(planta, cortes, vistas.Where(x => x.Tipo == "LAJES").ToList(), v, res);
        res.Ok = true;
        return res;
    }

    // ---------------- títulos e vistas ----------------

    private static List<(TextoDesenho, string)> Titulos(IList<TextoDesenho> textos)
    {
        var r = new List<(TextoDesenho, string)>();
        double hMed = Mediana(textos.Where(x => x.Altura > 0).Select(x => x.Altura).ToList());
        foreach (var x in textos)
        {
            if (x.Altura < 1.8 * hMed) continue;
            string s = x.Texto.ToUpperInvariant();
            if (s.Contains("LAJE") && (s.Contains("ARMA") || s.Contains("POSITIV") || s.Contains("NEGATIV"))) r.Add((x, "LAJES"));
            else if (s.StartsWith("CORTE")) r.Add((x, "CORTE"));
            else if (s.Contains("PLANTA")) r.Add((x, "PLANTA"));
        }
        return r;
    }

    /// <summary>Agrupa linhas cujas caixas ficam a menos de <paramref name="folga"/> (união por grade).</summary>
    private static List<List<LinhaDesenho>> Agrupar(List<LinhaDesenho> ls, double folga)
    {
        int n = ls.Count;
        var pai = Enumerable.Range(0, n).ToArray();
        int Raiz(int i) { while (pai[i] != i) { pai[i] = pai[pai[i]]; i = pai[i]; } return i; }
        double cel = Math.Max(folga, 1e-6);
        var grade = new Dictionary<(long, long), List<int>>();
        for (int i = 0; i < n; i++)
        {
            var l = ls[i];
            long ax = (long)Math.Floor((Math.Min(l.X1, l.X2) - folga) / cel), bx = (long)Math.Floor((Math.Max(l.X1, l.X2) + folga) / cel);
            long ay = (long)Math.Floor((Math.Min(l.Y1, l.Y2) - folga) / cel), by = (long)Math.Floor((Math.Max(l.Y1, l.Y2) + folga) / cel);
            if ((bx - ax + 1) * (by - ay + 1) > 4000) { ax = bx = (long)Math.Floor(l.X1 / cel); ay = by = (long)Math.Floor(l.Y1 / cel); }
            for (long gx = ax; gx <= bx; gx++)
                for (long gy = ay; gy <= by; gy++)
                {
                    if (!grade.TryGetValue((gx, gy), out var lista)) grade[(gx, gy)] = lista = new List<int>();
                    foreach (var j in lista) pai[Raiz(i)] = Raiz(j);
                    lista.Add(i);
                }
        }
        return Enumerable.Range(0, n).GroupBy(Raiz).Select(g => g.Select(i => ls[i]).ToList()).OrderByDescending(g => g.Count).ToList();
    }

    // ---------------- planta ----------------

    private static bool LerPlanta(Vista p, ValoresTemplate v, ReservatorioReconhecido res)
    {
        double f = p.Fator;
        var ls = p.Linhas.Select(l => new LinhaDesenho(l.X1 * f, l.Y1 * f, l.X2 * f, l.Y2 * f, l.Layer)).ToList();
        double x0 = ls.Min(l => Math.Min(l.X1, l.X2)), x1 = ls.Max(l => Math.Max(l.X1, l.X2));
        double y0 = ls.Min(l => Math.Min(l.Y1, l.Y2)), y1 = ls.Max(l => Math.Max(l.Y1, l.Y2));
        // retângulo externo: as linhas longas mais externas
        var hor = ls.Where(l => l.Horizontal && l.Comprimento > 0.3 * (x1 - x0)).ToList();
        var ver = ls.Where(l => l.Vertical && l.Comprimento > 0.3 * (y1 - y0)).ToList();
        if (hor.Count < 2 || ver.Count < 2) { res.Avisos.Add("Planta: não achei as faces das paredes (linhas ortogonais)."); return false; }
        double ex0 = ver.Min(l => l.X1), ex1 = ver.Max(l => l.X1), ey0 = hor.Min(l => l.Y1), ey1 = hor.Max(l => l.Y1);
        double Lx = ex1 - ex0, Ly = ey1 - ey0;
        // espessura: distância da face externa à primeira linha paralela longa para dentro
        var ts = new List<double>();
        void Esp(IEnumerable<double> pos, double face, int sentido)
        {
            var d = pos.Select(q => (q - face) * sentido).Where(q => q > 3 && q < 80).DefaultIfEmpty(double.NaN).Min();
            if (!double.IsNaN(d)) ts.Add(d);
        }
        var yh = hor.Select(l => l.Y1).Distinct().ToList(); var xv = ver.Select(l => l.X1).Distinct().ToList();
        Esp(yh, ey0, 1); Esp(yh, ey1, -1); Esp(xv, ex0, 1); Esp(xv, ex1, -1);
        if (ts.Count == 0) { res.Avisos.Add("Planta: não consegui medir a espessura das paredes."); return false; }
        double t = Math.Round(Mediana(ts), 1);
        if (ts.Max() - ts.Min() > 2) res.Avisos.Add($"Paredes com espessuras diferentes ({string.Join(", ", ts.Select(Num))} cm): usei {Num(t)} cm para todas.");

        // internas: pares de linhas longas paralelas, afastadas de t, longe das faces externas
        List<double> Internas(List<double> pos, double a, double b)
        {
            var r = new List<double>();
            var ord = pos.Where(q => q > a + t + 1 && q < b - t - 1).OrderBy(q => q).ToList();
            for (int i = 0; i + 1 < ord.Count; i++)
                if (Math.Abs(ord[i + 1] - ord[i] - t) <= Math.Max(1.5, 0.1 * t))
                {
                    double eixo = (ord[i] + ord[i + 1]) / 2 - a;
                    if (!r.Any(q => Math.Abs(q - eixo) < t)) r.Add(Math.Round(eixo, 1));
                    i++;
                }
            return r;
        }
        var horInt = ls.Where(l => l.Horizontal && l.Comprimento > 0.5 * (Lx - 2 * t)).Select(l => l.Y1).Distinct().ToList();
        var verInt = ls.Where(l => l.Vertical && l.Comprimento > 0.5 * (Ly - 2 * t)).Select(l => l.X1).Distinct().ToList();
        var iy = Internas(horInt, ey0, ey1); var ix = Internas(verInt, ex0, ex1);

        v["Lx"] = Num(Math.Round(Lx, 1)); v["Ly"] = Num(Math.Round(Ly, 1)); v["t"] = Num(t);
        v["InternasX"] = string.Join(";", ix.Select(Num)); v["InternasY"] = string.Join(";", iy.Select(Num));
        res.Relatorio.Add($"Planta: {Num(Lx)} × {Num(Ly)} cm, paredes de {Num(t)} cm, 4 externas e {ix.Count + iy.Count} interna(s)" +
                          (ix.Count + iy.Count > 0 ? $" (eixos: {string.Join(", ", ix.Select(q => "x=" + Num(q)).Concat(iy.Select(q => "y=" + Num(q))))})" : "") + ".");
        // aberturas (linhas curtas nas faces): só aviso
        int curtas = ls.Count(l => (l.Horizontal || l.Vertical) && l.Comprimento < t * 1.5 && l.Comprimento > t * 0.5);
        if (curtas >= 4) res.Avisos.Add("A planta tem furos/aberturas nas paredes: não são detalhados nesta versão (reforços à parte).");
        return true;
    }

    // ---------------- cortes ----------------

    private static void LerCortes(List<Vista> cortes, ValoresTemplate v, ReservatorioReconhecido res)
    {
        double t = v.Numero("t");
        var hfs = new List<double>(); var Hs = new List<double>(); var bals = new List<double>();
        var lajes = new List<(double nivel, double h)>();
        foreach (var c in cortes)
        {
            double f = c.Fator;
            var ls = c.Linhas.Select(l => new LinhaDesenho(l.X1 * f, l.Y1 * f, l.X2 * f, l.Y2 * f, l.Layer)).ToList();
            double x0 = ls.Min(l => Math.Min(l.X1, l.X2)), x1 = ls.Max(l => Math.Max(l.X1, l.X2));
            var hor = ls.Where(l => l.Horizontal && l.Comprimento > 0.5 * (x1 - x0)).Select(l => (y: l.Y1, a: Math.Min(l.X1, l.X2), b: Math.Max(l.X1, l.X2))).OrderBy(q => q.y).ToList();
            var ver = ls.Where(l => l.Vertical && l.Comprimento > 30).ToList();
            if (hor.Count < 2 || ver.Count < 2) { res.Avisos.Add($"{c.Titulo}: fundo ou paredes não encontrados."); continue; }
            double yb = hor[0].y;
            var topo = hor.Where(q => q.y - yb >= 6 && q.y - yb <= 120).Select(q => q.y).DefaultIfEmpty(double.NaN).Min();
            if (double.IsNaN(topo)) { res.Avisos.Add($"{c.Titulo}: espessura do fundo não encontrada."); continue; }
            double hf = topo - yb;
            double yTop = ver.Max(l => Math.Max(l.Y1, l.Y2));
            double H = yTop - topo;
            // balanço: fundo além das faces externas das paredes
            double fundoA = hor.Where(q => Math.Abs(q.y - yb) < 0.5).Min(q => q.a), fundoB = hor.Where(q => Math.Abs(q.y - yb) < 0.5).Max(q => q.b);
            var paredesX = ver.Where(l => Math.Max(l.Y1, l.Y2) > topo + 0.5 * H).Select(l => l.X1).ToList();
            if (paredesX.Count >= 2) bals.Add(Math.Max(0, Math.Min(paredesX.Min() - fundoA, fundoB - paredesX.Max())));
            hfs.Add(hf); Hs.Add(H);
            // lajes superiores: pares horizontais longos acima do fundo (afastamento 8..40)
            var acima = hor.Where(q => q.y > topo + 20 && q.b - q.a > 0.4 * (paredesX.Count >= 2 ? paredesX.Max() - paredesX.Min() : x1 - x0)).Select(q => q.y).Distinct().OrderBy(q => q).ToList();
            for (int i = 0; i + 1 < acima.Count; i++)
            {
                double g = acima[i + 1] - acima[i];
                if (g >= 7 && g <= 40) { lajes.Add((acima[i + 1] - topo, g)); i++; }
            }
        }
        if (hfs.Count == 0) { res.Avisos.Add("Cortes não lidos: altura e fundo ficaram nos valores padrão do template."); return; }
        v["hf"] = Num(Math.Round(Mediana(hfs), 1)); v["H"] = Num(Math.Round(Hs.Max(), 1)); v["Balanco"] = Num(Math.Round(bals.Count > 0 ? Mediana(bals) : 0, 1));
        var unicas = new List<(double nivel, double h)>();
        foreach (var l in lajes.OrderBy(q => q.nivel)) if (!unicas.Any(u => Math.Abs(u.nivel - l.nivel) < 5)) unicas.Add(l);
        v["Lajes"] = ValoresTemplate.JuntarTabela(unicas.Select((l, i) => new[] { "L" + (i + 2), Num(Math.Round(l.nivel, 1)), Num(Math.Round(l.h, 1)), "" }));
        res.Relatorio.Add($"Cortes ({cortes.Count}): fundo {v["hf"]} cm, balanço {v["Balanco"]} cm, paredes de {v["H"]} cm acima do fundo" +
                          (unicas.Count > 0 ? $", {unicas.Count} laje(s) superior(es) (níveis {string.Join(", ", unicas.Select(u => Num(u.nivel)))})" : ", sem lajes superiores") + ".");
    }

    // ---------------- bitolas e espaçamentos ----------------

    private sealed class Lido { public double Bitola, Esp; public int Qtd; public double Rot; public string Texto; }

    private static List<Lido> Barras(Vista x) => x.Textos.Select(tx =>
    {
        var b = TextoArmadura.Interpretar(tx.Texto);
        if (b != null) return new Lido { Bitola = b.Bitola, Esp = b.Espacamento, Qtd = b.Quantidade, Rot = tx.Rotacao, Texto = b.Original };
        var m = Regex.Match(tx.Texto, @"(\d+)\s*N\d+\s+c/\s*(\d+(?:[.,]\d+)?)");   // "19 N27 c/20" (sem diâmetro)
        return m.Success ? new Lido { Bitola = 0, Esp = double.Parse(m.Groups[2].Value.Replace(',', '.'), CultureInfo.InvariantCulture), Qtd = int.Parse(m.Groups[1].Value), Rot = tx.Rotacao, Texto = tx.Texto } : null;
    }).Where(b => b != null).ToList();

    /// <summary>Par (bitola, espaçamento) mais frequente; relata os outros.</summary>
    private static bool Familia(IEnumerable<Lido> lidos, string rotulo, string pBit, string pEsp, ValoresTemplate v, ReservatorioReconhecido res, double bitolaReserva = 0)
    {
        var l = lidos.Where(b => b.Esp > 0).ToList();
        if (l.Count == 0) { res.Avisos.Add($"{rotulo}: nenhum texto de barra com espaçamento; ficou o padrão (ø{v[pBit]} c/{v[pEsp]})."); return false; }
        var grupos = l.GroupBy(b => (b.Bitola, b.Esp)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Esp).ToList();
        var (bit, esp) = grupos[0].Key;
        if (bit <= 0)
        {
            bit = bitolaReserva > 0 ? bitolaReserva : v.Numero(pBit);
            res.Avisos.Add($"{rotulo}: o desenho não informa o diâmetro (\"{grupos[0].First().Texto}\"); usei ø{bit:0.0}.");
        }
        v[pBit] = bit.ToString("0.0", CultureInfo.InvariantCulture); v[pEsp] = Num(esp);
        string outros = grupos.Count > 1 ? " (também no desenho: " + string.Join(", ", grupos.Skip(1).Select(g => $"ø{g.Key.Bitola:0.0} c/{Num(g.Key.Esp)}")) + ")" : "";
        res.Relatorio.Add($"{rotulo}: ø{bit:0.0} c/{Num(esp)}{outros}.");
        return true;
    }

    private static void LerArmaduras(Vista planta, List<Vista> cortes, List<Vista> lajes, ValoresTemplate v, ReservatorioReconhecido res)
    {
        var bp = Barras(planta);
        // horizontais: texto deitado = paredes em x; texto em pé = paredes em y (rebatidas ao lado delas)
        bool EmPe(Lido b) => Math.Abs(Math.Abs(Math.Sin(b.Rot)) - 1) < 0.1;
        Familia(bp.Where(b => !EmPe(b)), "Paredes em x, horizontais", "HBitola", "HEsp", v, res);
        var hy = new ValoresTemplate(); hy["HBitola"] = v["HBitola"]; hy["HEsp"] = v["HEsp"];
        if (Familia(bp.Where(EmPe), "Paredes em y, horizontais", "HBitola", "HEsp", hy, res))
        {
            v["HEspY"] = hy["HEsp"];
            if (Math.Abs(hy.Numero("HBitola") - v.Numero("HBitola")) > 1e-6)
                res.Avisos.Add($"Horizontais com bitolas diferentes nas duas direções (ø{v["HBitola"]} e ø{hy["HBitola"]}): usei ø{v["HBitola"]}.");
        }
        // barras sem espaçamento na planta das paredes ("4 N30 ø8.0 C=407"): verticais de canto
        var cantos = bp.Where(b => b.Esp <= 0 && b.Bitola > 0 && b.Qtd >= 2 && b.Qtd <= 8).ToList();
        if (cantos.Count > 0)
        {
            var g = cantos.GroupBy(b => b.Bitola).OrderByDescending(x => x.Count()).First();
            v["CantoVBitola"] = g.Key.ToString("0.0", CultureInfo.InvariantCulture); v["CantoVQtd"] = g.First().Qtd.ToString(CultureInfo.InvariantCulture);
            res.Relatorio.Add($"Paredes, verticais de canto: {g.First().Qtd} ø{g.Key:0.0} por canto" +
                              (cantos.Select(b => b.Bitola).Distinct().Count() > 1 ? $" (também: {string.Join(", ", cantos.Select(b => $"ø{b.Bitola:0.0}").Distinct())})" : "") + ".");
        }
        var bc = cortes.SelectMany(Barras).ToList();
        bool Vertical(Lido b) => Math.Abs(Math.Abs(Math.Sin(b.Rot)) - 1) < 0.1;
        Familia(bc.Where(Vertical), "Paredes, verticais", "VBitola", "VEsp", v, res);
        Familia(bc.Where(b => !Vertical(b)), "Arranques parede-fundo", "ArrBitola", "ArrEsp", v, res);

        double nivelFundo = lajes.Count > 0 ? lajes.Min(x => x.Nivel) : 0;
        var fundo = lajes.Where(x => Math.Abs(x.Nivel - nivelFundo) < 0.05).ToList();
        var sup = lajes.Where(x => x.Nivel - nivelFundo >= 0.05).ToList();
        var fPos = fundo.Where(x => !x.Negativa).SelectMany(Barras).ToList();
        var fNeg = fundo.Where(x => x.Negativa).SelectMany(Barras).ToList();
        Familia(fPos, "Fundo, positiva", "FPosBitola", "FPosEsp", v, res);
        Familia(fNeg, "Fundo, negativa", "FNegBitola", "FNegEsp", v, res, v.Numero("FPosBitola"));
        var borda = fPos.Where(b => b.Esp <= 0 && b.Bitola > 0 && b.Qtd >= 1 && b.Qtd <= 4).ToList();
        if (borda.Count > 0)
        {
            var g = borda.GroupBy(b => (b.Bitola, b.Qtd)).OrderByDescending(x => x.Count()).First();
            v["BordaBitola"] = g.Key.Bitola.ToString("0.0", CultureInfo.InvariantCulture); v["BordaQtd"] = g.Key.Qtd.ToString(CultureInfo.InvariantCulture);
            res.Relatorio.Add($"Fundo, borda: {g.Key.Qtd} ø{g.Key.Bitola:0.0} por lado.");
        }
        if (sup.Count > 0)
        {
            Familia(sup.Where(x => !x.Negativa).SelectMany(Barras), "Lajes superiores, positiva", "LPosBitola", "LPosEsp", v, res);
            Familia(sup.Where(x => x.Negativa).SelectMany(Barras), "Lajes superiores, negativa", "LNegBitola", "LNegEsp", v, res, v.Numero("LPosBitola"));
        }
        if (fundo.Count == 0) res.Avisos.Add("Plantas das lajes não encontradas: armadura do fundo no padrão do template.");
    }

    // ---------------- util ----------------

    private static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    private static double DistanciaCaixa(Vista x, double px, double py)
    {
        double dx = Math.Max(0, Math.Max(x.X0 - px, px - x.X1)), dy = Math.Max(0, Math.Max(x.Y0 - py, py - x.Y1));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Mediana(List<double> xs)
    {
        if (xs.Count == 0) return 0;
        var o = xs.OrderBy(q => q).ToList();
        return o.Count % 2 == 1 ? o[o.Count / 2] : (o[o.Count / 2 - 1] + o[o.Count / 2]) / 2;
    }

    private static string Num(double x) => Calculo.CalculoComprimento.NumeroCompacto(x);
}
