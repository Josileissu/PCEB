using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Modelo;
using DEB.Core.Templates.PilarPreMoldado;

namespace DEB.Core.Templates.VigaPreMoldada;

/// <summary>Um grupo de cordoalhas (uma linha da tabela): mesma fiada, bitola, isolamento e força.</summary>
public sealed class GrupoCordoalhas
{
    public int Linha { get; set; }
    /// <summary>Fiada na grade da pista: 1 = a 5 cm do fundo, 2 = a 10 cm…</summary>
    public int Fiada { get; set; } = 1;
    public double Bitola { get; set; } = 12.7;
    /// <summary>Comprimento isolado (mangueira) em cada ponta, cm.</summary>
    public double IsolE { get; set; }
    public double IsolD { get; set; }
    /// <summary>Força de protensão por cordoalha, tf (0 = sem texto).</summary>
    public double Forca { get; set; }
    /// <summary>Posições na seção (x a partir do eixo vertical, y a partir do fundo) e o número de cada cordoalha.</summary>
    public List<(int Numero, double X, double Y)> Cordoalhas { get; set; } = new List<(int, double, double)>();
    public int Quantidade => Cordoalhas.Count;
    public double Y => Cordoalhas.Count > 0 ? Cordoalhas[0].Y : 0;
    public bool Isolada => IsolE > 0 || IsolD > 0;
}

/// <summary>
/// VP-3 (2026-11-08): viga protendida por pré-tração no padrão das pranchas 1071/1078/710 e nas respostas do usuário
/// (2026-11-06/07): cordoalhas nos nós de uma grade de 5 cm (1ª fiada a 5 cm do fundo, simétrica no eixo vertical), na
/// relação do aço como CP-190; ARMADURAS DE PROTENSÃO abaixo das passivas, uma linha por grupo com o mesmo isolamento
/// (trecho grosso "MANGUEIRA ISOLAMENTO" cotado em cada ponta) e a seção NOMENCLATURA E POSICIONAMENTO DAS CORDOALHAS.
/// </summary>
public sealed partial class TemplateVigaPreMoldada
{
    private const string G9 = "09. Protensão (pré-tração)";
    public const double PassoGrade = 5;

    private static IEnumerable<ParametroTemplate> ParametrosProtensao() => new[]
    {
        P(G9, "LiberacaoGradual", "Liberação gradual da protensão", "Sim", tipo: TipoParametro.SimNao,
          descricao: "NBR 6118 9.4.5.2: não gradual = comprimento de transferência × 1,25 (fretagem nas pontas)."),
        P(G9, "Protendida", "Viga protendida", "Não", tipo: TipoParametro.SimNao,
          descricao: "Sim = desenha as cordoalhas da tabela; o quadro de produção troca \"ao saque\" por \"ato da protensão\"."),
        new ParametroTemplate
        {
            Grupo = G9, Nome = "Cordoalhas", Rotulo = "Cordoalhas (grupos)", Tipo = TipoParametro.Tabela, NomeItem = "grupo",
            Padrao = "1|3|12.7|0|0|14;2|2|12.7|100|100|14",
            Descricao = "Uma linha por grupo com o mesmo isolamento: fiada (1 = a 5 cm do fundo, grade de 5 cm), quantidade, bitola, " +
                        "isolamento em cada ponta (cm; 0 = aderente) e força por cordoalha (tf). Grupos da mesma fiada dividem os nós da " +
                        "grade, simétricos no eixo; o 1º grupo fica no meio.",
            Colunas =
            {
                new ColunaTabela { Nome = "Fiada", Rotulo = "Fiada", Tipo = TipoParametro.Numero, Padrao = "1", Largura = 55 },
                new ColunaTabela { Nome = "Qtd", Rotulo = "Quantidade", Tipo = TipoParametro.Numero, Padrao = "2", Largura = 75 },
                new ColunaTabela { Nome = "Bitola", Rotulo = "Bitola (mm)", Tipo = TipoParametro.Opcao, Opcoes = { "9.5", "12.7", "15.2" }, Padrao = "12.7", Largura = 80 },
                new ColunaTabela { Nome = "IsolE", Rotulo = "Isol. esq. (cm)", Tipo = TipoParametro.Numero, Padrao = "0", Largura = 85 },
                new ColunaTabela { Nome = "IsolD", Rotulo = "Isol. dir. (cm)", Tipo = TipoParametro.Numero, Padrao = "0", Largura = 85 },
                new ColunaTabela { Nome = "Forca", Rotulo = "Força (tf/cabo)", Tipo = TipoParametro.Numero, Padrao = "14", Largura = 90 },
            },
        },
    };

    /// <summary>
    /// Lê os grupos e põe cada cordoalha num nó da grade: por fiada, nós simétricos no eixo (n ímpar: 0, ±5…; n par: ±2,5, ±7,5…),
    /// do meio para fora na ordem da tabela; numeração da esquerda para a direita, de baixo para cima. Avisa o que sai da seção.
    /// </summary>
    public static List<GrupoCordoalhas> LerCordoalhas(ValoresTemplate v, List<string> avisos)
    {
        var gs = new List<GrupoCordoalhas>();
        var qtd = new Dictionary<GrupoCordoalhas, int>();
        var linhas = v.Tabela("Cordoalhas");
        for (int k = 1; k <= linhas.Count; k++)
        {
            var l = linhas[k - 1];
            int fiada = (int)Math.Round(ValoresTemplate.NumeroCelula(l, 0, 1)), q = (int)Math.Round(ValoresTemplate.NumeroCelula(l, 1, 0));
            double phi = ValoresTemplate.NumeroCelula(l, 2, 12.7);
            if (fiada < 1 || q < 1 || phi <= 0) { avisos.Add($"Cordoalhas, grupo {k}: informe a fiada (≥ 1), a quantidade e a bitola."); continue; }
            var g = new GrupoCordoalhas
            {
                Linha = k, Fiada = fiada, Bitola = phi,
                IsolE = Math.Max(0, ValoresTemplate.NumeroCelula(l, 3, 0)), IsolD = Math.Max(0, ValoresTemplate.NumeroCelula(l, 4, 0)),
                Forca = Math.Max(0, ValoresTemplate.NumeroCelula(l, 5, 0)),
            };
            gs.Add(g); qtd[g] = q;
        }
        foreach (var fi in gs.GroupBy(g => g.Fiada))
        {
            int n = fi.Sum(g => qtd[g]);
            double y = PassoGrade * fi.Key;
            // nós do meio para fora, o da esquerda antes do da direita
            var nos = Enumerable.Range(0, n).Select(i => (i - (n - 1) / 2.0) * PassoGrade).OrderBy(x => Math.Abs(x)).ThenBy(x => x).ToList();
            int p = 0;
            foreach (var g in fi) for (int i = 0; i < qtd[g]; i++) g.Cordoalhas.Add((0, nos[p++], y));
        }
        // numeração: de baixo para cima, da esquerda para a direita
        var todas = gs.SelectMany(g => g.Cordoalhas.Select((c, i) => (g, i, c))).OrderBy(t => t.c.Y).ThenBy(t => t.c.X).ToList();
        for (int n = 0; n < todas.Count; n++) { var (g, i, c) = todas[n]; g.Cordoalhas[i] = (n + 1, c.X, c.Y); }
        foreach (var g in gs) g.Cordoalhas.Sort((a, b) => a.Numero.CompareTo(b.Numero));
        return gs;
    }

    /// <summary>Verificações de desenho: dentro dos estribos, espaço livre (NBR 9062 9.2.1), fcj de liberação (9.2.5.3), recorte do dente.</summary>
    public static void VerificarCordoalhas(List<GrupoCordoalhas> gs, double b, double h, double cobL, double cobF, double cobT, double phiEst,
        double fcjProt, ExtremidadeViga e, ExtremidadeViga d, List<string> avisos)
    {
        if (gs.Count == 0) return;
        double t = phiEst / 10;
        foreach (var g in gs)
        {
            double r = g.Bitola / 20;
            double xMax = g.Cordoalhas.Max(c => Math.Abs(c.X));
            if (xMax + r > b / 2 - cobL - t + 1e-6)
                avisos.Add($"Cordoalhas, grupo {g.Linha}: a fiada {g.Fiada} não cabe na largura (cordoalha a {Num(xMax, "0.#")} cm do eixo; " +
                           $"limite {Num(b / 2 - cobL - t - r, "0.#")} cm) — diminua a quantidade ou passe para outra fiada.");
            if (g.Y - r < cobF + t - 1e-6 || g.Y + r > h - cobT - t + 1e-6)
                avisos.Add($"Cordoalhas, grupo {g.Linha}: a fiada {g.Fiada} (y = {Num(g.Y, "0.#")} cm) sai da altura útil da seção.");
            double livre = PassoGrade - g.Bitola / 10;
            if (livre < Math.Max(2, 2 * g.Bitola / 10) - 1e-6)
                avisos.Add($"Cordoalhas ø{Num(g.Bitola, "0.#")}: espaço livre {Num(livre, "0.##")} cm < 2φ / 2 cm (NBR 9062 9.2.1).");
            foreach (var (x, lado) in new[] { (e, "esquerda"), (d, "direita") })
                if (x.TemDente && g.Y < x.Recorte(h) + 1e-6)
                    avisos.Add($"Cordoalhas, grupo {g.Linha}: a fiada {g.Fiada} passa no recorte do dente da ponta {lado}; ela é cortada na face do recorte.");
        }
        if (fcjProt < 21 - 1e-6)
            avisos.Add($"fcj na liberação da protensão = {Num(fcjProt, "0.#")} MPa < 21 MPa (NBR 9062 9.2.5.3).");
    }

    /// <summary>
    /// Cordoalha superior (controle da tração no topo na liberação): fiada acima da meia altura. Vai numa linha à parte ACIMA das
    /// passivas, como nas pranchas 1071/1078 — cada linha do lado da face em que está na seção.
    /// </summary>
    public static bool Superior(GrupoCordoalhas g, double h) => g.Y > h / 2;

    /// <summary>Área nominal da cordoalha de 7 fios CP-190 RB (NBR 7483), cm².</summary>
    public static double AreaCordoalha(double phi) => phi < 11 ? 0.548 : phi < 14 ? 0.987 : 1.40;

    /// <summary>
    /// Comprimento de transferência ℓbpt (cm), NBR 6118:2014 9.4.5: ℓbp = (7φ/36)·fpyd/fbpd (cordoalhas de 3/7 fios),
    /// fbpd = ηp1·ηp2·fctd com ηp1 = 1,2, ηp2 = 1,0 e fctd = 0,7·0,3·fcj^(2/3)/1,4 na idade da protensão (9.3.2.1);
    /// ℓbpt = 0,5·ℓbp·σpi/fpyd, × 1,25 se a liberação não for gradual. CP-190 RB: fpyd = 0,9·1900/1,15.
    /// Sem força informada, σpi = fpyd (a favor da segurança).
    /// </summary>
    public static double ComprimentoTransferencia(double phiMm, double forcaTf, double fcjMPa, bool gradual = true)
    {
        double fpyd = 0.9 * 1900 / 1.15;
        double fctd = 0.7 * 0.3 * Math.Pow(Math.Max(1, fcjMPa), 2.0 / 3) / 1.4, fbpd = 1.2 * 1.0 * fctd;
        double lbp = 7 * (phiMm / 10) / 36 * fpyd / fbpd;
        double spi = forcaTf > 0 ? Math.Min(fpyd, forcaTf * 9.80665 / AreaCordoalha(phiMm) * 10) : fpyd;
        double lbpt = 0.5 * lbp * spi / fpyd;
        return gradual ? lbpt : 1.25 * lbpt;
    }

    /// <summary>
    /// Fim da transferência das cordoalhas aderentes em cada ponta (cm a partir da ponta): início da cordoalha + ℓbpt. As isoladas
    /// numa ponta transferem depois da mangueira, longe da ponta — não entram na fretagem dessa ponta.
    /// </summary>
    public static (double E, double D, double Lbpt) Fretagem(List<GrupoCordoalhas> gs, double h, double L, ExtremidadeViga e, ExtremidadeViga d,
        double fcj, bool gradual)
    {
        double fe = 0, fd = 0, lmax = 0;
        foreach (var g in gs)
        {
            var (ini, fim) = TrechoCordoalha(g, h, L, e, d);
            double lt = ComprimentoTransferencia(g.Bitola, g.Forca, fcj, gradual);
            if (g.IsolE <= 0) { fe = Math.Max(fe, ini + lt); lmax = Math.Max(lmax, lt); }
            if (g.IsolD <= 0) { fd = Math.Max(fd, L - fim + lt); lmax = Math.Max(lmax, lt); }
        }
        return (fe, fd, lmax);
    }

    /// <summary>Trecho da cordoalha ao longo da viga: de ponta a ponta, ou da face do recorte se a fiada passa no recorte do dente.</summary>
    public static (double Ini, double Fim) TrechoCordoalha(GrupoCordoalhas g, double h, double L, ExtremidadeViga e, ExtremidadeViga d) =>
        (e.TemDente && g.Y < e.Recorte(h) ? e.CompDente : 0, L - (d.TemDente && g.Y < d.Recorte(h) ? d.CompDente : 0));

    private static string ListaNumeros(GrupoCordoalhas g) => string.Join(", ", g.Cordoalhas.Select(c => c.Numero));

    /// <summary>ARMADURAS DE PROTENSÃO abaixo de <paramref name="yTopo"/>: uma linha por grupo com o isolamento cotado; devolve o y abaixo.</summary>
    private static double CordoalhasEmElevacao(Ctx c, List<GrupoCordoalhas> gs, double h, double L, ExtremidadeViga e, ExtremidadeViga d, double yTopo,
        bool superiores = false)
    {
        if (gs.Count == 0) return yTopo;
        double hv = c.hV, esc = c.escV, y = yTopo;
        int n = gs.Sum(g => g.Quantidade);
        var phis = gs.Select(g => g.Bitola).Distinct().OrderBy(p => p).Select(p => "ø " + Num(p, "0.0") + "mm").ToList();
        foreach (var t in c.Linhas($"ARMADURAS DE PROTENSÃO{(superiores ? " SUPERIORES" : "")} - {n} {string.Join(" / ", phis)} CP 190 RB",
                                   $"{(superiores ? "TOP " : "")}PRESTRESSING STRANDS - {n} {string.Join(" / ", phis)} CP 190 RB"))
        { c.Texto(t, 0, y, 1.0 * hv); y -= 1.6 * hv; }
        y -= 5 * hv;
        // como na seção: as de cima primeiro nas superiores, as de baixo por último nas inferiores
        foreach (var g in (superiores ? gs.OrderByDescending(g => g.Fiada) : gs.OrderBy(g => g.Fiada)).ThenBy(g => g.Linha))
        {
            var (ini, fim) = TrechoCordoalha(g, h, L, e, d);
            c.R.Barras.Add(new BarraTemplate
            {
                Nome = "cord" + g.Linha, Bitola = g.Bitola, Aco = "CP-190", Quantidade = g.Quantidade, Escala = esc, Layer = "ARM-CORDOALHA",
                Vertices = { (ini, y), (fim, y) }, Formatos = { FormatoSegmento.Linha },
                PosicaoDescricao = ((ini + fim) / 2, y + 2.6 * hv),
            });
            // isolamento: trecho grosso em cada ponta, rotulado e cotado
            foreach (var (iso, esq) in new[] { (g.IsolE, true), (g.IsolD, false) })
            {
                if (iso <= 0) continue;
                double x1 = esq ? ini : fim - iso, x2 = esq ? ini + iso : fim, w = 0.35 * hv;
                var ret = new List<(double X, double Y)> { (x1, y - w / 2), (x2, y - w / 2), (x2, y + w / 2), (x1, y + w / 2) };
                c.R.Contornos.Add(new ContornoTemplate { Fechado = true, Layer = "ARM-ISOLAMENTO", Vertices = ret });
                c.R.Hachuras.Add(new HachuraTemplate { Padrao = "SOLID", Layer = "ARM-ISOLAMENTO", Vertices = ret.ToList() });
                c.Cota(x1, y, x2, y, (x1 + x2) / 2, y - 2.2 * hv, 0, esc);
                c.Texto(string.Join(" / ", c.Linhas("MANGUEIRA ISOLAMENTO", "DEBONDING SLEEVE")), esq ? x1 : x2, y - 4.6 * hv, 0.6 * hv, esq ? 0 : 2);
            }
            string forca = g.Forca > 0 ? $" - FORÇA DE PROTENSÃO = {Num(g.Forca, "0.0#")} tf/CABO" : "";
            string forcaEn = g.Forca > 0 ? $" - PRESTRESSING FORCE = {g.Forca.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture)} tf/STRAND" : "";
            string iso2 = g.Isolada ? "" : " (ADERENTES)";
            c.Texto(string.Join(" / ", c.Linhas($"CORDOALHAS {ListaNumeros(g)} - ø{Num(g.Bitola, "0.0")}{forca}{iso2}",
                                               $"STRANDS {ListaNumeros(g)} - ø{g.Bitola.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}{forcaEn}{(g.Isolada ? "" : " (BONDED)")}")),
                (ini + fim) / 2, y - (g.Isolada ? 6.4 : 3.4) * hv, 0.75 * hv, 1);
            y -= (g.Isolada ? 11 : 8) * hv;
        }
        return y;
    }

    /// <summary>
    /// Cordoalhas em ponto numa seção de armadura (as que passam no x do corte). Grupos iguais na relação (mesma bitola e mesmo
    /// trecho = mesmo N) ganham UMA indicação só, à esquerda da seção.
    /// </summary>
    private static void CordoalhasNaSecao(Ctx c, List<GrupoCordoalhas> gs, double xCorte, double h, double L, ExtremidadeViga e, ExtremidadeViga d,
        double b, double sx0, double sy0)
    {
        int k = 0;
        var passam = gs.Select(g => (g, t: TrechoCordoalha(g, h, L, e, d))).Where(q => xCorte >= q.t.Ini - 1e-6 && xCorte <= q.t.Fim + 1e-6).ToList();
        foreach (var igual in passam.GroupBy(q => (q.g.Bitola, Math.Round(q.t.Ini, 3), Math.Round(q.t.Fim, 3))))
        {
            var pts = igual.SelectMany(q => q.g.Cordoalhas.Select(z => (sx0 + b / 2 + z.X, sy0 + z.Y))).ToList();
            foreach (var q in igual)
                foreach (var z in q.g.Cordoalhas)
                    c.R.Circulos.Add(new CirculoTemplate { X = sx0 + b / 2 + z.X, Y = sy0 + z.Y, Raio = q.g.Bitola / 20, Layer = "ARM-CORDOALHA" });
            c.R.Faixas.Add(new FaixaTemplate
            {
                Barra = "cord" + igual.First().g.Linha, Tipo = TipoFaixa.Indicacao, Referencias = pts,
                Posicao = (sx0 - 6 * c.hC, pts.Average(p => p.Item2) + 2 * c.hC * k++), Escala = c.escC,
            });
        }
    }

    /// <summary>Escala da seção de nomenclatura (1:15 nas pranchas 1071/1078; a grade de 5 cm fica legível).</summary>
    public const double EscalaNomenclatura = 15;

    /// <summary>
    /// NOMENCLATURA E POSICIONAMENTO DAS CORDOALHAS EM SEÇÃO TRANSVERSAL (seção do vão, ampliada para 1:15): números das
    /// cordoalhas (isoladas em vermelho) e a grade cotada; devolve o y abaixo.
    /// </summary>
    private static double NomenclaturaCordoalhas(Ctx c, List<GrupoCordoalhas> gs, double b, double h, double x0, double yTopo)
    {
        if (gs.Count == 0) return yTopo;
        double hc = c.hC, esc = c.escC, y = yTopo, f = esc / EscalaNomenclatura;   // geometria ampliada; textos no tamanho das seções
        foreach (var t in c.Linhas("NOMENCLATURA E POSICIONAMENTO DAS CORDOALHAS EM SEÇÃO TRANSVERSAL", "STRAND NUMBERING AND POSITIONING IN CROSS SECTION"))
        { c.Texto(t, x0, y, 1.2 * hc); y -= 2 * hc; }
        c.Texto($"ESC 1:{EscalaNomenclatura:0}", x0, y, 0.8 * hc);
        double sx0 = x0 + 4 * hc, sy1 = y - 5 * hc, sy0 = sy1 - h * f, xe = sx0 + b * f / 2;
        c.Retangulo(sx0, sy0, sx0 + b * f, sy1, "ARM-FORMA");
        foreach (var g in gs)
            foreach (var q in g.Cordoalhas)
            {
                string layer = g.Isolada ? "ARM-CORDOALHA-ISOLADA" : "ARM-CORDOALHA";
                double cx = xe + q.X * f, cy = sy0 + q.Y * f, rr = g.Bitola / 20 * f;
                c.R.Circulos.Add(new CirculoTemplate { X = cx, Y = cy, Raio = rr, Layer = layer });
                c.Texto(q.Numero.ToString(), cx, cy + rr + 0.3 * hc, 0.5 * hc, 1, 0, g.Isolada ? "ARM-CORDOALHA-ISOLADA" : "ARM-TEXTO-FORMA");
            }
        // grade cotada (valores reais): nós da fiada mais larga embaixo, as fiadas à direita a partir do fundo
        var larga = gs.GroupBy(g => g.Fiada).OrderByDescending(q => q.Sum(g => g.Quantidade)).First().SelectMany(g => g.Cordoalhas).Select(q => q.X).OrderBy(x => x).ToList();
        var xs = new List<double> { -b / 2 }.Concat(larga).Concat(new[] { b / 2 }).ToList();
        for (int i = 0; i + 1 < xs.Count; i++)
        {
            double a = xe + xs[i] * f, z = xe + xs[i + 1] * f;
            c.Cota(a, sy0, z, sy0, (a + z) / 2, sy0 - 3 * hc, 0, esc, Num(xs[i + 1] - xs[i], "0.#"));
        }
        var ys = new List<double> { 0 }.Concat(gs.Select(g => g.Y).Distinct().OrderBy(q => q)).ToList();
        for (int i = 0; i + 1 < ys.Count; i++)
        {
            double a = sy0 + ys[i] * f, z = sy0 + ys[i + 1] * f;
            c.Cota(sx0 + b * f, a, sx0 + b * f, z, sx0 + b * f + 3 * hc, (a + z) / 2, Math.PI / 2, esc, Num(ys[i + 1] - ys[i], "0.#"));
        }
        y = sy0 - 7 * hc;
        if (gs.Any(g => g.Isolada))
        {
            foreach (var t in c.Linhas("EM VERMELHO: CORDOALHAS ISOLADAS NAS PONTAS", "IN RED: STRANDS DEBONDED AT THE ENDS"))
            { c.Texto(t, x0, y, 0.7 * hc); y -= 1.6 * hc; }
        }
        foreach (var t in c.Linhas("OBSERVAÇÕES: Atentar-se ao isolamento das cordoalhas e ao posicionamento na seção.",
                                   "NOTES: Pay attention to the strand debonding and to their position in the section."))
        { c.Texto(t, x0, y, 0.7 * hc); y -= 1.6 * hc; }
        return y - 4 * hc;
    }

    /// <summary>Barras passivas da seção do vão que encostam numa cordoalha (centro a centro &lt; raios + 2 cm): aviso.</summary>
    public static void VerificarPassivasComCordoalhas(List<GrupoCordoalhas> gs, List<LinhaLongViga> longs, double b, double h,
        double cobF, double cobT, double cobL, double phiEst, List<string> avisos)
    {
        if (gs.Count == 0) return;
        foreach (var l in longs)
        {
            var pts = PontosNaSecao(l, b, h, cobF, cobT, cobL, phiEst, longs);
            foreach (var g in gs)
                if (pts.Any(p => g.Cordoalhas.Any(q => Math.Sqrt(Math.Pow(p.X - (b / 2 + q.X), 2) + Math.Pow(p.Y - q.Y, 2)) < l.Bitola / 20 + g.Bitola / 20 + 2 - 1e-6)))
                {
                    avisos.Add($"Longitudinal {l.Linha} ({l.Funcao}) encosta nas cordoalhas do grupo {g.Linha} (fiada {g.Fiada}): conferir as posições.");
                    break;
                }
        }
    }
}
