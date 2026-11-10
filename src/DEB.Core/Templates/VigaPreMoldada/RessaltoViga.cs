using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Estribos;
using DEB.Core.Modelo;

namespace DEB.Core.Templates.VigaPreMoldada;

/// <summary>
/// Ressalto sobre a alma (2026-11-10): faixa estreita acima do topo da viga, rente a uma das faces, em todo o comprimento — a capa
/// da laje encosta nele (o "aba laje + aba superior" de um modelo real: alma 30 × 50, aba de laje de um lado, ressalto 13 × 27,5 do
/// outro). Lados como os da aba: esquerda = lado de cima da planta e lado esquerdo das seções.
/// </summary>
public sealed class RessaltoViga
{
    public bool Esquerda { get; set; }
    public double Largura { get; set; }
    public double Altura { get; set; }
    public bool Existe => Largura > 0 && Altura > 0;
    public double VolumeCm3(double L) => Existe ? Largura * Altura * L : 0;
}

/// <summary>
/// Armadura do ressalto, proposta do usuário (2026-11-10: "uma armadura que fique dentro desse ressalto e uma armadura em L, com uma
/// perna dentro da alma e outra saindo para fora uns 70 cm, fará a conexão com a capa da laje"):
/// - ESTRIBO DO RESSALTO: fechado, dentro do ressalto (cobrimento nas faces livres; embaixo encosta no topo da alma), com as
///   longitudinais do ressalto nos cantos;
/// - LIGAÇÃO COM A CAPA: barra em L — a perna vertical desce pelo ressalto e entra na alma (ancoragem), a perna horizontal corre
///   no alto do ressalto e SAI pela face dele do lado da laje, <c>LigCapaSaida</c> (70 cm) para fora, para ficar na capa.
/// Os dois em faixa própria na elevação, com a forma na relação do aço.
/// </summary>
public sealed partial class TemplateVigaPreMoldada
{
    public const string RessaltoSem = "Sem ressalto", RessaltoEsq = "Esquerda", RessaltoDir = "Direita";
    private const string G11 = "11. Ressalto sobre a alma (ligação com a capa)";

    private static IEnumerable<ParametroTemplate> ParametrosRessalto() => new[]
    {
        P(G11, "Ressalto", "Ressalto", RessaltoSem, tipo: TipoParametro.Opcao, opcoes: new[] { RessaltoSem, RessaltoEsq, RessaltoDir },
          descricao: "Faixa estreita acima do topo da viga, rente à face escolhida (esquerda = lado de cima da planta), em todo o comprimento. " +
                     "A altura h da viga é a da alma, sem o ressalto."),
        P(G11, "RessaltoLargura", "Ressalto: largura", "13", "cm", min: 0),
        P(G11, "RessaltoAltura", "Ressalto: altura acima do topo da alma", "27.5", "cm", min: 0),
        P(G11, "RessEstBitola", "Estribo do ressalto: bitola", "6.3", "mm", TipoParametro.Bitola, 5, 12.5),
        P(G11, "RessEstEsp", "Estribo do ressalto: espaçamento", "15", "cm", min: 2),
        P(G11, "RessLongQtd", "Longitudinais do ressalto (nos cantos)", "4", tipo: TipoParametro.Inteiro, min: 0, max: 6),
        P(G11, "RessLongBitola", "Longitudinais do ressalto: bitola", "8", "mm", TipoParametro.Bitola, 5, 16),
        P(G11, "LigCapaBitola", "Ligação com a capa (L): bitola", "8", "mm", TipoParametro.Bitola, 5, 16,
          descricao: "Barra em L: perna vertical no ressalto e na alma, perna horizontal saindo do ressalto para a capa da laje."),
        P(G11, "LigCapaEsp", "Ligação com a capa: espaçamento", "20", "cm", min: 5),
        P(G11, "LigCapaSaida", "Ligação com a capa: comprimento fora do ressalto", "70", "cm", min: 10),
        P(G11, "LigCapaAncoragem", "Ligação com a capa: perna dentro da alma (abaixo do topo)", "40", "cm", min: 5),
    };

    // ======================== leitura (testável) ========================

    public static RessaltoViga LerRessalto(ValoresTemplate v, double b, double h, List<string> avisos)
    {
        string t = v["Ressalto"];
        var r = new RessaltoViga { Esquerda = t == RessaltoEsq, Largura = v.Numero("RessaltoLargura"), Altura = v.Numero("RessaltoAltura") };
        if (t != RessaltoEsq && t != RessaltoDir) { r.Largura = r.Altura = 0; return r; }
        if (!r.Existe) { avisos?.Add("Ressalto: informe a largura e a altura; viga desenhada sem ressalto."); return r; }
        if (r.Largura >= b - 1e-6) { avisos?.Add($"Ressalto: largura {Num(r.Largura, "0.#")} cm ≥ b da viga ({Num(b, "0.#")}); viga desenhada sem ressalto."); r.Largura = r.Altura = 0; return r; }
        double anc = v.Numero("LigCapaAncoragem");
        if (anc > h - v.Numero("CobFundo"))
            avisos?.Add($"Ligação com a capa: a perna de {Num(anc, "0.#")} cm na alma passa do fundo da viga (h = {Num(h, "0.#")}); foi limitada.");
        avisos?.Add("Ressalto e ligação com a capa: armadura proposta (padrão do escritório a confirmar) — conferir.");
        return r;
    }

    /// <summary>Posições (eixo, cm da ponta esquerda) de uma armadura distribuída ao longo da viga toda, pela regra de faixas do plugin.</summary>
    private static List<double> PosicoesAoLongo(ValoresTemplate v, ConfiguracaoArmadura cfg, double L, double phiMm, double esp)
    {
        double cob = v.Numero("CobPontas"), x0 = cob + phiMm / 20, x1 = L - cob - phiMm / 20;
        if (x1 - x0 < 1) return new List<double>();
        int n = CalculoFaixa.Quantidade(x1 - x0, Math.Max(2, esp), cfg.EspacamentoInicialFaixa, cfg.ModoQuantidadeFaixa);
        return CalculoFaixa.Posicoes(x1 - x0, n, Math.Max(2, esp), cfg.EspacamentoInicialFaixa).Select(s => x0 + s).ToList();
    }

    /// <summary>x (seção) da face do ressalto que dá para a laje e da face de fora, com a alma de sx0 a sx0 + b.</summary>
    private static (double Interna, double Externa) FacesRessalto(RessaltoViga rs, double b, double sx0) =>
        rs.Esquerda ? (sx0 + rs.Largura, sx0) : (sx0 + b - rs.Largura, sx0 + b);

    /// <summary>Estribo do ressalto (eixo) na seção: retângulo de cobrimento a cobrimento nas faces livres, do topo da alma ao topo do ressalto.</summary>
    public static (double X1, double Y1, double X2, double Y2) CaixaEstriboRessalto(ValoresTemplate v, RessaltoViga rs, double b, double h, double sx0, double sy0)
    {
        double cobL = v.Numero("CobLateral"), cobT = v.Numero("CobTopo"), t = v.Numero("RessEstBitola") / 20;
        var (fi, fe) = FacesRessalto(rs, b, sx0);
        double xa = Math.Min(fi, fe) + cobL + t, xb = Math.Max(fi, fe) - cobL - t;
        return (xa, sy0 + h + t, xb, sy0 + h + rs.Altura - cobT - t);
    }

    /// <summary>
    /// A barra em L de ligação com a capa (eixo) na seção: perna vertical junto da face de fora do ressalto, descendo até
    /// <c>LigCapaAncoragem</c> abaixo do topo da alma; perna horizontal no alto, saindo pela face da laje <c>LigCapaSaida</c> para fora.
    /// </summary>
    public static List<(double X, double Y)> PoligonoLigacaoCapa(ValoresTemplate v, RessaltoViga rs, double b, double h, double sx0, double sy0)
    {
        double cobL = v.Numero("CobLateral"), cobT = v.Numero("CobTopo"), cobF = v.Numero("CobFundo"), r = v.Numero("LigCapaBitola") / 20;
        double tE = v.Numero("RessEstBitola") / 10;   // por dentro do estribo do ressalto
        var (fi, fe) = FacesRessalto(rs, b, sx0);
        double s = fi > fe ? 1 : -1;                   // sentido de fora para a laje
        double xv = fe + s * (cobL + tE + r), yTopo = sy0 + h + rs.Altura - cobT - tE - r;
        double yPe = Math.Max(sy0 + cobF + r, sy0 + h - v.Numero("LigCapaAncoragem"));
        return new List<(double X, double Y)> { (xv, yPe), (xv, yTopo), (fi + s * v.Numero("LigCapaSaida"), yTopo) };
    }

    // ======================== desenho ========================

    /// <summary>Na planta: a face de dentro do ressalto (a mais alta da peça: linha cheia) e a largura cotada fora da ponta direita.</summary>
    private static void RessaltoEmPlanta(Ctx c, RessaltoViga rs, double b, double L, double yP)
    {
        if (!rs.Existe) return;
        double yi = rs.Esquerda ? yP + b - rs.Largura : yP + rs.Largura, ye = rs.Esquerda ? yP + b : yP;
        c.Linha(0, yi, L, yi);
        c.Cota(L, Math.Min(yi, ye), L, Math.Max(yi, ye), L + 6 * c.hV, (yi + ye) / 2, Math.PI / 2, c.escV);
    }

    /// <summary>Na elevação (topo da alma em <paramref name="yT"/>): a faixa do ressalto acima do topo, em todo o comprimento, e a altura cotada.</summary>
    private static void RessaltoEmElevacao(Ctx c, RessaltoViga rs, double L, double yT, bool cotar)
    {
        if (!rs.Existe) return;
        c.R.Contornos.Add(new ContornoTemplate { Vertices = { (0, yT), (0, yT + rs.Altura), (L, yT + rs.Altura), (L, yT) }, Layer = "ARM-FORMA" });
        if (cotar) c.Cota(L, yT, L, yT + rs.Altura, L + 5.5 * c.hV, yT + rs.Altura / 2, Math.PI / 2, c.escV);
    }

    /// <summary>Contorno da seção com o ressalto em cima (e a aba, se houver): a alma b × h com o canto inferior esquerdo em (x0, y0).</summary>
    public static List<(double X, double Y)> ContornoSecao(double b, double h, AbaViga a, RessaltoViga rs, double x0, double y0)
    {
        var p = ContornoSecao(b, h, a, x0, y0);
        if (rs == null || !rs.Existe) return p;
        // o topo da alma vai de (x0 + b, y0 + h) a (x0, y0 + h): o ressalto entra entre esses dois vértices
        int i = p.FindIndex(q => Math.Abs(q.X - (x0 + b)) < 1e-9 && Math.Abs(q.Y - (y0 + h)) < 1e-9);
        if (i < 0) return p;
        double yt = y0 + h + rs.Altura;
        var ins = rs.Esquerda
            ? new List<(double X, double Y)> { (x0 + rs.Largura, y0 + h), (x0 + rs.Largura, yt), (x0, yt) }
            : new List<(double X, double Y)> { (x0 + b, yt), (x0 + b - rs.Largura, yt), (x0 + b - rs.Largura, y0 + h) };
        p.InsertRange(i + 1, ins);
        return SemColineares(p);
    }

    /// <summary>Cotas do ressalto na seção de forma: largura em cima e altura do lado dele.</summary>
    private static void CotasRessaltoNaSecao(Ctx c, RessaltoViga rs, double b, double h, double sx0, double sy0)
    {
        double hc = c.hC, esc = c.escC, yt = sy0 + h + rs.Altura;
        var (fi, fe) = FacesRessalto(rs, b, sx0);
        c.Cota(Math.Min(fi, fe), yt, Math.Max(fi, fe), yt, (fi + fe) / 2, yt + 3 * hc, 0, esc);
        double xc = fe + (rs.Esquerda ? -3 : 3) * hc;
        c.Cota(fe, sy0 + h, fe, yt, xc, sy0 + h + rs.Altura / 2, Math.PI / 2, esc);
    }

    /// <summary>Armadura do ressalto na SEÇÃO - ARMADURA: o estribo, as longitudinais nos cantos e a barra em L, com uma indicação de cada.</summary>
    private static void RessaltoNaSecaoArmadura(Ctx c, ValoresTemplate v, RessaltoViga rs, double b, double h, double sx0, double sy0)
    {
        double hc = c.hC;
        var (x1, y1, x2, y2) = CaixaEstriboRessalto(v, rs, b, h, sx0, sy0);
        var (fi, fe) = FacesRessalto(rs, b, sx0);
        double xt = fe + (rs.Esquerda ? -6 : 6) * hc;   // as indicações saem pelo lado de fora (o da laje tem a barra em L)
        if (x2 - x1 > 2 && y2 - y1 > 2)
        {
            c.Retangulo(x1, y1, x2, y2, "ARM-BARRA-DETALHE");
            c.R.Faixas.Add(new FaixaTemplate { Barra = "ressestribo", Tipo = TipoFaixa.Indicacao, Referencias = { (rs.Esquerda ? x1 : x2, (y1 + y2) / 2) },
                Posicao = (xt, (y1 + y2) / 2 + 1.2 * hc), Escala = c.escC });
            int q = Math.Max(0, v.Inteiro("RessLongQtd"));
            double rr = v.Numero("RessLongBitola") / 20, t = v.Numero("RessEstBitola") / 20;
            var pts = new List<(double X, double Y)>();
            if (q > 0)
            {
                var cantos = new[] { (x1 + t + rr, y2 - t - rr), (x2 - t - rr, y2 - t - rr), (x1 + t + rr, y1 + t + rr), (x2 - t - rr, y1 + t + rr) };
                pts.AddRange(cantos.Take(Math.Min(4, q)));
                for (int i = 4; i < q; i++) pts.Add(((x1 + x2) / 2, y1 + t + rr + (i - 3) * (y2 - y1 - 2 * t - 2 * rr) / Math.Max(1, q - 3)));
                foreach (var (px, py) in pts) c.R.Circulos.Add(new CirculoTemplate { X = px, Y = py, Raio = rr, Layer = "ARM-BARRA-SECAO" });
                c.R.Faixas.Add(new FaixaTemplate { Barra = "resslong", Tipo = TipoFaixa.Indicacao, Referencias = pts, Posicao = (xt, (y1 + y2) / 2 - 1.2 * hc), Escala = c.escC });
            }
        }
        var l = PoligonoLigacaoCapa(v, rs, b, h, sx0, sy0);
        c.R.Contornos.Add(new ContornoTemplate { Vertices = l, Layer = "ARM-BARRA-DETALHE" });
        // indicação na perna que sai, um pouco além da face do ressalto
        double xr = fi + (l[2].X - fi) * 0.35;
        c.R.Faixas.Add(new FaixaTemplate { Barra = "ligcapa", Tipo = TipoFaixa.Indicacao, Referencias = { (xr, l[2].Y) }, Posicao = (xr, l[2].Y + 4 * hc), Escala = c.escC });
        // rótulo na ponta da perna que sai (longe da linha da indicação)
        c.Texto(string.Join(" / ", c.Linhas("CAPA DA LAJE", "SLAB TOPPING")), l[2].X, l[2].Y + 1.2 * hc, 0.8 * hc, l[2].X < fi ? 0 : 2);
    }

    /// <summary>
    /// Nas ARMADURAS EM ELEVAÇÃO (topo da alma em <paramref name="yT"/>): a faixa do ressalto, os estribos dele em traços e as faixas
    /// dos estribos e da ligação com a capa, nas linhas <paramref name="yFaixa"/> e logo abaixo. Devolve quantas linhas de faixa usou.
    /// </summary>
    private static int RessaltoEmElevacaoArmaduras(Ctx c, ValoresTemplate v, ConfiguracaoArmadura cfg, RessaltoViga rs, double L, double yT, double yFaixa)
    {
        if (!rs.Existe) return 0;
        RessaltoEmElevacao(c, rs, L, yT, cotar: false);
        double cobT = v.Numero("CobTopo");
        var xs = PosicoesAoLongo(v, cfg, L, v.Numero("RessEstBitola"), v.Numero("RessEstEsp"));
        foreach (double x in xs) c.Linha(x, yT, x, yT + rs.Altura - cobT, "ARM-BARRA-DETALHE");
        int linhas = 0;
        foreach (var (barra, phi, esp) in new[] { ("ressestribo", v.Numero("RessEstBitola"), v.Numero("RessEstEsp")), ("ligcapa", v.Numero("LigCapaBitola"), v.Numero("LigCapaEsp")) })
        {
            double cob = v.Numero("CobPontas"), x0 = cob + phi / 20, x1 = L - cob - phi / 20, y = yFaixa - linhas * 3.2 * c.hV;
            c.R.Faixas.Add(new FaixaTemplate { Barra = barra, Tipo = TipoFaixa.Ortogonal, P1 = (x0, y), P2 = (x1, y), Posicao = ((x0 + x1) / 2, y + 0.8 * c.hV),
                Espacamento = Math.Max(2, esp), Escala = c.escV });
            linhas++;
        }
        return linhas;
    }

    /// <summary>As longitudinais do ressalto numa linha fora da forma, com o rótulo embaixo.</summary>
    private static bool RessaltoLongitudinal(Ctx c, ValoresTemplate v, RessaltoViga rs, double L, double y)
    {
        int q = Math.Max(0, v.Inteiro("RessLongQtd"));
        if (!rs.Existe || q == 0) return false;
        double cob = v.Numero("CobPontas"), hv = c.hV;
        c.R.Barras.Add(new BarraTemplate
        {
            Nome = "resslong", Bitola = v.Numero("RessLongBitola"), Aco = v["LongAco"], Quantidade = q, Escala = c.escV,
            Vertices = { (cob, y), (L - cob, y) }, Formatos = { FormatoSegmento.Linha }, PosicaoDescricao = (L / 2, y + 2.6 * hv),
        });
        c.Texto(string.Join(" / ", c.Linhas("ARMADURA RESSALTO", "UPSTAND REINFORCEMENT")), L / 2, y - 3.4 * hv, 0.75 * hv, 1);
        return true;
    }

    /// <summary>Formas do estribo do ressalto (fechado) e da barra em L, para a relação do aço; empilhadas a partir de (x, y) para baixo.</summary>
    private static List<BarraTemplate> FormasRessalto(ValoresTemplate v, ConfiguracaoArmadura cfg, RessaltoViga rs, double b, double h)
    {
        var r = new List<BarraTemplate>();
        if (!rs.Existe) return r;
        var (x1, y1, x2, y2) = CaixaEstriboRessalto(v, rs, b, h, 0, 0);
        if (x2 - x1 > 2 && y2 - y1 > 2)
        {
            double phi = v.Numero("RessEstBitola");
            var f = GeradorEstribo.FechadoPadrao(new List<(double X, double Y)> { (0, 0), (x2 - x1, 0), (x2 - x1, y2 - y1), (0, y2 - y1) }, phi, v["EstAco"], cfg.Dobramento);
            var bt = new BarraTemplate { Nome = "ressestribo", Bitola = phi, Aco = v["EstAco"], Estribo = true, Quantidade = 1 };
            bt.Vertices.AddRange(f.Vertices.Select(p => (p.X, p.Y)));
            bt.Formatos.AddRange(f.Formatos);
            foreach (var s in f.Sobrepostos) bt.Sobrepostos.Add(s);
            foreach (var s in f.Cotados) bt.Cotados.Add(s);
            r.Add(bt);
        }
        var l = PoligonoLigacaoCapa(v, rs, b, h, 0, 0);
        var lc = new BarraTemplate { Nome = "ligcapa", Bitola = v.Numero("LigCapaBitola"), Aco = v["LongAco"], Quantidade = 1, Formatos = { FormatoSegmento.Linha, FormatoSegmento.Linha } };
        // perna vertical à esquerda e a horizontal para a direita (a forma não depende do lado)
        double hv = l[1].Y - l[0].Y, hz = Math.Abs(l[2].X - l[1].X);
        lc.Vertices.AddRange(new[] { (0.0, 0.0), (0.0, hv), (hz, hv) });
        r.Add(lc);
        return r;
    }
}
