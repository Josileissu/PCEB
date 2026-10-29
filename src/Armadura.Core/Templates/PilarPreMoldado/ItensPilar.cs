using System;
using System.Collections.Generic;
using System.Linq;
using Armadura.Core.Configuracao;
using Armadura.Core.Modelo;

namespace Armadura.Core.Templates.PilarPreMoldado;

/// <summary>
/// Etapa 4 do pilar (2026-10-29): içamento (alças de cordoalha simétricas ao CG, a ~20 % das pontas — regra do
/// escritório), montagem por furo (25–35 % do comprimento, com os grampos de reforço) ou alça de topo, inserto do topo
/// (gabarito e chumbadores) e os detalhes padrão desenhados pelo template: chaves de cisalhamento, espaçamento das
/// barras no canto, alça de içamento, reforço do furo, raio de curvatura, emendas, comprimento das barras e SPDA.
/// Alças em cordoalha (CP-190): a NBR 9062 (5.3.3.5) proíbe CA-25/CA-50/CA-60.
/// </summary>
public sealed partial class TemplatePilarPreMoldado
{
    public const string MontagemFuro = "Furo", MontagemAlcaTopo = "Alça de topo", MontagemNenhuma = "Nenhuma";

    private const string G17 = "17. Içamento (alças)", G18 = "18. Montagem", G19 = "19. Inserto no topo", G20 = "20. Detalhes padrão";

    private static readonly ParametroTemplate[] ParametrosItens =
    {
        P(G17, "Alcas", "Alças de içamento (desforma)", "Sim", tipo: TipoParametro.SimNao),
        P(G17, "AlcaFace", "Face das alças", "2", tipo: TipoParametro.Inteiro, min: 1, max: 4),
        P(G17, "AlcaPct", "Distância às pontas", "20", "% do comprimento", min: 5, max: 45,
          descricao: "As duas alças ficam simétricas em relação ao CG do pilar (com os consolos), afastadas (50 − x) % do comprimento dele."),
        P(G17, "AlcaZ1", "Alça de baixo (0 = automática)", "0", "cm", min: 0, descricao: "Distância da base; 0 = pela regra do CG."),
        P(G17, "AlcaZ2", "Alça de cima (0 = automática)", "0", "cm", min: 0),
        P(G17, "AlcaBitola", "Cordoalha", "12.7", "mm", TipoParametro.Bitola, 9.5, 15.2),
        P(G17, "AlcaPernas", "Cordoalhas por alça", "2", tipo: TipoParametro.Inteiro, min: 1, max: 4),
        P(G17, "AlcaEmb", "Embutimento", "45", "cm", min: 10),
        P(G17, "AlcaAcima", "Altura acima da face", "15", "cm", min: 5),
        P(G17, "AlcaEntre", "Distância entre as pernas", "10", "cm", min: 4),
        P(G17, "AlcaPe", "Pé (dobra no fim de cada perna)", "20", "cm", min: 0),

        P(G18, "Montagem", "Montagem por", MontagemFuro, tipo: TipoParametro.Opcao, opcoes: new[] { MontagemFuro, MontagemAlcaTopo, MontagemNenhuma }),
        P(G18, "FuroDiam", "Furo: diâmetro", "100", "mm", min: 30),
        P(G18, "FuroPct", "Furo: distância ao topo", "30", "% do comprimento", min: 5, max: 50, descricao: "Regra do escritório: 25 % a 35 %."),
        P(G18, "FuroDir", "Furo: atravessa as faces", "1-3", tipo: TipoParametro.Opcao, opcoes: new[] { "1-3", "2-4" }),
        P(G18, "FuroGrampos", "Furo: grampos de reforço", "8", tipo: TipoParametro.Inteiro, min: 0, max: 20),
        P(G18, "FuroGrampoBitola", "Furo: bitola dos grampos", "6.3", "mm", TipoParametro.Bitola, 5, 12.5),
        P(G18, "FuroGrampoPerna", "Furo: perna dos grampos", "50", "cm", min: 10),

        P(G19, "Inserto", "Inserto no topo", "Sim", tipo: TipoParametro.SimNao),
        P(G19, "InsertoNome", "Inserto", "25CB660", tipo: TipoParametro.Texto),
        P(G19, "Gabarito", "Gabarito", "GC1A", tipo: TipoParametro.Texto),
        P(G19, "GabaritoLado", "Gabarito: distância entre chumbadores", "13", "cm", min: 2),
        P(G19, "ChumbBitola", "Chumbadores: bitola", "25", "mm", min: 6),
        P(G19, "ChumbComp", "Chumbadores: comprimento dentro do pilar", "60", "cm", min: 5),

        P(G20, "DetalhesPadrao", "Desenhar os detalhes padrão", "Sim", tipo: TipoParametro.SimNao),
        P(G20, "AvisoSPDA", "Aviso do SPDA", "Sim", tipo: TipoParametro.SimNao),
    };

    // ======================== cálculos (testáveis) ========================

    /// <summary>
    /// Alturas das alças (cm da base): simétricas ao CG, a (50 − pct) % do comprimento dele (regra do escritório:
    /// CG centralizado e alças a ~20 % das pontas). Valores &gt; 0 em z1/z2 mandam.
    /// </summary>
    public static (double Z1, double Z2) PosicaoAlcas(double L, double cg, double pct, double z1 = 0, double z2 = 0)
    {
        double d = (0.5 - pct / 100) * L;
        double a = z1 > 0 ? z1 : cg - d, b = z2 > 0 ? z2 : cg + d;
        a = Math.Max(0.05 * L, Math.Min(a, 0.95 * L));
        b = Math.Max(0.05 * L, Math.Min(b, 0.95 * L));
        return (Math.Round(a, 1), Math.Round(b, 1));
    }

    /// <summary>Raio do grampo de reforço em volta do furo (do P61: ø100 → C=125 com pernas de 50 → r ≈ 8 cm).</summary>
    public static double RaioGrampoFuro(double furoMm) => furoMm / 20 + 3;

    // ======================== içamento, montagem e inserto nas vistas ========================

    private sealed class Itens
    {
        public double Z1, Z2;          // alças
        public bool Alcas, AlcaTopo, Furo;
        public double ZFuro;
        public int NAlcas;
    }

    private Itens DesenharItensNasVistas(Ctx c, ValoresTemplate v, List<ConsoloPilar> consolos, double[] xs, double[] larguras,
        double b, double h, double L, double ch)
    {
        var r = c.R;
        double hv = c.hV;
        var it = new Itens { Alcas = v.SimNao("Alcas"), AlcaTopo = v["Montagem"] == MontagemAlcaTopo, Furo = v["Montagem"] == MontagemFuro };
        double emb = v.Numero("AlcaEmb"), acima = v.Numero("AlcaAcima"), entre = v.Numero("AlcaEntre");

        // laço da alça na vista de lado (pernas dentro, laço fora da face) e o traço na vista de frente
        void AlcaDeLado(double xf, double s, double z)
        {
            double r0 = entre / 2;
            r.Contornos.Add(new ContornoTemplate
            {
                Vertices = { (xf - s * emb, z - r0), (xf + s * (acima - r0), z - r0), (xf + s * (acima - r0), z + r0), (xf - s * emb, z + r0) },
                Bulges = { 0, s > 0 ? 1 : -1, 0, 0 }, Layer = "ARM-ALCA",
            });
        }

        if (it.Alcas)
        {
            int face = Math.Max(1, Math.Min(4, v.Inteiro("AlcaFace")));
            double cg = AlturaCG(b, h, L, ch, consolos);
            (it.Z1, it.Z2) = PosicaoAlcas(L, cg, v.Numero("AlcaPct"), v.Numero("AlcaZ1"), v.Numero("AlcaZ2"));
            it.NAlcas = 2;
            foreach (var co in consolos.Where(co => co.Faces.Contains(face)))
                foreach (double z in new[] { it.Z1, it.Z2 })
                    if (z > co.ZTopo - co.H - 20 && z < co.ZTopo + co.PinoAcima + 20)
                        r.Avisos.Add($"Alça a {Num(z, "0")} cm da base perto do consolo {co.Slot} na mesma face: ajuste a posição (AlcaZ1/AlcaZ2).");
            for (int vista = 1; vista <= 4; vista++)
            {
                double x0 = xs[vista - 1], x1 = x0 + larguras[vista - 1], xm = (x0 + x1) / 2;
                foreach (double z in new[] { it.Z1, it.Z2 })
                {
                    if (vista == face)
                    {
                        // de frente: o laço aparece como um traço; cotas do centro às arestas (P61: 25 | 25)
                        c.Linha(xm, z - entre / 2, xm, z + entre / 2, "ARM-ALCA");
                        c.Cota(x0, z + entre, xm, z + entre, (x0 + xm) / 2, z + entre + 1.5 * hv, 0, c.escV);
                        c.Cota(xm, z + entre, x1, z + entre, (xm + x1) / 2, z + entre + 1.5 * hv, 0, c.escV);
                        double yt = z + 4 * hv;
                        foreach (var t in c.Linhas("ALÇA", "LIFTING HANDLE")) { c.Texto(t, x0 - 1.5 * hv, yt, 0.8 * hv, 2); yt -= 1.2 * hv; }
                    }
                    else if (FaceEsquerda(vista) == face) AlcaDeLado(x0, -1, z);
                    else if (FaceDireita(vista) == face) AlcaDeLado(x1, 1, z);
                }
                // cotas das alças na vista de frente: base → alça → alça → topo (como no P61)
                if (vista == face)
                {
                    double xc = x1 + 3 * hv;
                    c.Cota(x1, 0, x1, it.Z1, xc, it.Z1 / 2, Math.PI / 2, c.escV);
                    c.Cota(x1, it.Z1, x1, it.Z2, xc, (it.Z1 + it.Z2) / 2, Math.PI / 2, c.escV);
                    c.Cota(x1, it.Z2, x1, L, xc, (it.Z2 + L) / 2, Math.PI / 2, c.escV);
                }
            }
        }

        // montagem: alça de topo (no centro do topo) ou furo passante
        if (it.AlcaTopo)
        {
            it.NAlcas++;
            for (int vista = 1; vista <= 4; vista++)
            {
                double xm = xs[vista - 1] + larguras[vista - 1] / 2, r0 = entre / 2;
                r.Contornos.Add(new ContornoTemplate
                {
                    Vertices = { (xm - r0, L - emb), (xm - r0, L + acima - r0), (xm + r0, L + acima - r0), (xm + r0, L - emb) },
                    Bulges = { 0, -1, 0, 0 }, Layer = "ARM-ALCA",
                });
            }
        }
        if (it.Furo)
        {
            double diam = v.Numero("FuroDiam") / 10, rf = diam / 2;
            it.ZFuro = L - v.Numero("FuroPct") / 100 * L;
            double pct = v.Numero("FuroPct");
            if (pct < 25 || pct > 35) r.Avisos.Add($"Furo a {Num(pct, "0.#")} % do comprimento a partir do topo (regra do escritório: 25 % a 35 %).");
            var (fa, fb) = v["FuroDir"] == "2-4" ? (2, 4) : (1, 3);
            for (int vista = 1; vista <= 4; vista++)
            {
                double x0 = xs[vista - 1], x1 = x0 + larguras[vista - 1], xm = (x0 + x1) / 2;
                if (vista == fa || vista == fb)
                {
                    r.Circulos.Add(new CirculoTemplate { X = xm, Y = it.ZFuro, Raio = rf, Layer = "ARM-FURO" });
                    if (vista == fa)
                    {
                        double yt = it.ZFuro + 2.5 * hv;
                        foreach (var t in c.Linhas($"FURO ø{Num(diam * 10, "0")}mm", "HOLE")) { c.Texto(t, x0 - 1.5 * hv, yt, 0.8 * hv, 2); yt -= 1.2 * hv; }
                        c.Cota(x0, it.ZFuro, x0, L, x0 - 2.5 * hv, (it.ZFuro + L) / 2, Math.PI / 2, c.escV);
                    }
                }
                else
                {
                    // de lado: o furo atravessa (linhas ocultas)
                    c.Linha(x0, it.ZFuro - rf, x1, it.ZFuro - rf, "ARM-FURO-OCULTO");
                    c.Linha(x0, it.ZFuro + rf, x1, it.ZFuro + rf, "ARM-FURO-OCULTO");
                }
            }
        }

        // inserto do topo: chumbadores nas vistas (dois por vista, a ± lado/2) e o nome acima da VISTA 1
        if (v.SimNao("Inserto"))
        {
            double g = v.Numero("GabaritoLado"), comp = v.Numero("ChumbComp");
            for (int vista = 1; vista <= 4; vista++)
            {
                double xm = xs[vista - 1] + larguras[vista - 1] / 2;
                foreach (double u in new[] { -g / 2, g / 2 }) c.Linha(xm + u, L, xm + u, L - comp, "ARM-INSERTO");
            }
            double yt = L + 6 * hv;
            foreach (var t in new[] { "INSERTO", v["InsertoNome"] }) { c.Texto(t, xs[0] + larguras[0] / 2, yt, 0.8 * hv, 1, 0, "ARM-INSERTO"); yt -= 1.2 * hv; }
        }
        return it;
    }

    /// <summary>Gabarito do inserto no corte A-A: quadrado com os chumbadores nos cantos e as cotas (P61: 18,5 | 13 | 18,5).</summary>
    private static void InsertoNoCorte(Ctx c, ValoresTemplate v, double sx0, double sy0, double b, double h)
    {
        if (!v.SimNao("Inserto")) return;
        double g = v.Numero("GabaritoLado"), rc = v.Numero("ChumbBitola") / 20, hc = c.hC;
        double gx0 = sx0 + (b - g) / 2, gy0 = sy0 + (h - g) / 2;
        c.Retangulo(gx0, gy0, gx0 + g, gy0 + g, "ARM-INSERTO");
        foreach (var (x, y) in new[] { (gx0, gy0), (gx0 + g, gy0), (gx0 + g, gy0 + g), (gx0, gy0 + g) })
            c.R.Circulos.Add(new CirculoTemplate { X = x, Y = y, Raio = rc, Layer = "ARM-INSERTO" });
        // cotas em cadeia acima e à esquerda (as totais ficam embaixo e à direita)
        double yc = sy0 + h + 1.5 * hc;
        c.Cota(sx0, yc, gx0, yc, (sx0 + gx0) / 2, yc + 1.2 * hc, 0, c.escC);
        c.Cota(gx0, yc, gx0 + g, yc, gx0 + g / 2, yc + 1.2 * hc, 0, c.escC);
        c.Cota(gx0 + g, yc, sx0 + b, yc, (gx0 + g + sx0 + b) / 2, yc + 1.2 * hc, 0, c.escC);
        c.Texto("GABARITO " + v["Gabarito"], gx0 + g + 2 * hc, gy0 - 2.5 * hc, 0.8 * hc, 0, 0, "ARM-INSERTO");
    }

    // ======================== detalhes padrão ========================

    /// <summary>Altura de texto (cm) para uma escala 1:n.</summary>
    private static double Ht(Ctx c, double esc) => c.hV / c.escV * esc;

    private double DetalhesPadrao(Ctx c, ValoresTemplate v, ConfiguracaoArmadura cfg, Itens it, double[] cob, double phiT, double x0, double yTopo)
    {
        var r = c.R;
        double hv = c.hV, x = x0, yBaixo = yTopo;
        double Titulo(string pt, string en, double xt, double yt, double esc, string escala)
        {
            double ht = Ht(c, esc);
            var linhas = c.Linhas(pt, en);
            foreach (var t in linhas) { c.Texto(t, xt, yt, 1.3 * ht); yt -= 2 * ht; }
            c.Texto(escala, xt, yt, 0.8 * ht);
            return xt + linhas.Max(l => l.Length) * 0.75 * 1.3 * ht + 0.5 * hv;
        }
        // o próximo detalhe começa depois do desenho E do título do anterior
        double fimTitulo = x0;

        // ---- alça de içamento (cordoalha), 1:20 ----
        if (it.NAlcas > 0)
        {
            double esc = 20, ht = Ht(c, esc);
            double phi = v.Numero("AlcaBitola"), emb = v.Numero("AlcaEmb"), acima = v.Numero("AlcaAcima"), e = v.Numero("AlcaEntre"), pe = v.Numero("AlcaPe");
            fimTitulo = Titulo("ALÇA DE IÇAMENTO", "LIFTING HANDLE", x, yTopo, esc, "ESC. 1:20");
            double y0 = yTopo - 10 * ht - emb - acima, xa = x + pe + 2 * ht;
            var bt = new BarraTemplate
            {
                Nome = "alca", Bitola = phi, Aco = "CP-190", Quantidade = Math.Max(1, v.Inteiro("AlcaPernas")), Repeticoes = it.NAlcas, Escala = esc,
                Vertices = { (xa - pe, y0), (xa, y0), (xa, y0 + emb + acima - e / 2), (xa + e, y0 + emb + acima - e / 2), (xa + e, y0), (xa + e + pe, y0) },
                Bulges = { 0, 0, -1, 0, 0, 0 },
            };
            if (pe <= 0) { bt.Vertices.RemoveAt(5); bt.Vertices.RemoveAt(0); bt.Bulges.Clear(); bt.Bulges.AddRange(new[] { 0.0, -1, 0, 0 }); }
            r.Barras.Add(bt);
            // face do concreto (tracejada) e as cotas do embutimento e da altura livre
            c.Linha(xa - pe - 2 * ht, y0 + emb, xa + e + pe + 2 * ht, y0 + emb, "ARM-NIVEL");
            c.Cota(xa + e + pe + ht, y0, xa + e + pe + ht, y0 + emb, xa + e + pe + 2.5 * ht, y0 + emb / 2, Math.PI / 2, esc);
            c.Cota(xa + e + pe + ht, y0 + emb, xa + e + pe + ht, y0 + emb + acima, xa + e + pe + 2.5 * ht, y0 + emb + acima / 2, Math.PI / 2, esc);
            c.Texto(c.Idioma == IdiomaDesenho.EN ? "(strand CP-190)" : "(cordoalha CP-190)", xa + e / 2, y0 - 6 * ht, 0.8 * ht, 1);
            x = xa + e + pe + 14 * ht;
            x = Math.Max(x, fimTitulo + 4 * hv);
            yBaixo = Math.Min(yBaixo, y0 - 10 * ht);
        }

        // ---- reforço do furo, 1:25 ----
        if (it.Furo && v.Inteiro("FuroGrampos") > 0)
        {
            double esc = c.escC, ht = Ht(c, esc);
            double diam = v.Numero("FuroDiam") / 10, rg = RaioGrampoFuro(v.Numero("FuroDiam")), perna = v.Numero("FuroGrampoPerna");
            fimTitulo = Titulo("REFORÇO NOS FUROS DOS PILARES", "COLUMN HOLE REINFORCEMENT", x, yTopo, esc, $"ESC 1:{esc:0}");
            double yc = yTopo - 10 * ht - rg - 4, xc = x + 4 * ht;
            r.Circulos.Add(new CirculoTemplate { X = xc + perna, Y = yc, Raio = diam / 2, Layer = "ARM-FURO" });
            var gr = new BarraTemplate
            {
                Nome = "reforcofuro", Bitola = v.Numero("FuroGrampoBitola"), Aco = "CA-50", Quantidade = v.Inteiro("FuroGrampos"), Escala = esc,
                Vertices = { (xc, yc + rg), (xc + perna, yc + rg), (xc + perna, yc - rg), (xc, yc - rg) }, Bulges = { 0, -1, 0, 0 },
            };
            r.Barras.Add(gr);
            double yt = yc - rg - 6 * ht;
            foreach (var t in c.Linhas($"{v.Inteiro("FuroGrampos")} GRAMPOS POR FURO", $"{v.Inteiro("FuroGrampos")} TIES EACH HOLE")) { c.Texto(t, xc + perna / 2, yt, 0.8 * ht, 1); yt -= 1.3 * ht; }
            x = xc + perna + rg + 14 * ht;
            x = Math.Max(x, fimTitulo + 4 * hv);
            yBaixo = Math.Min(yBaixo, yt - 4 * ht);
        }

        if (!v.SimNao("DetalhesPadrao")) return yBaixo - 10 * hv;

        // ---- chaves de cisalhamento, 1:10 ----
        if (v["Interface"] == Chaves)
        {
            double esc = 10, ht = Ht(c, esc);
            double passo = v.Numero("ChavePasso"), dente = v.Numero("ChaveDente"), prof = v.Numero("ChaveProf");
            fimTitulo = Titulo("DETALHE 01 - CHAVES DE CISALHAMENTO", "DETAIL 01 - SHEAR KEYS", x, yTopo, esc, "ESC 1:10");
            double xf = x + 6 * ht, yb = yTopo - 10 * ht - 5 * passo;
            var perfil = new ContornoTemplate { Layer = "ARM-FORMA" };
            perfil.Vertices.Add((xf - 3 * passo, yb));
            for (int k = 0; k < 5; k++)
            {
                double y = yb + k * passo;
                perfil.Vertices.Add((xf, y));
                perfil.Vertices.Add((xf, y + dente));
                perfil.Vertices.Add((xf - prof, y + dente + (passo - dente) / 2));
            }
            perfil.Vertices.Add((xf, yb + 5 * passo));
            perfil.Vertices.Add((xf - 3 * passo, yb + 5 * passo));
            r.Contornos.Add(perfil);
            for (int k = 0; k < 5; k++)
            {
                double y = yb + k * passo;
                c.Cota(xf + 1, y, xf + 1, y + dente, xf + 1 + 1.2 * ht, y + dente / 2, Math.PI / 2, esc);
                c.Cota(xf + 3, y, xf + 3, y + passo, xf + 3 + 1.2 * ht, y + passo / 2, Math.PI / 2, esc);
            }
            c.Cota(xf - prof, yb - 1, xf, yb - 1, xf - prof / 2, yb - 1 - 1.2 * ht, 0, esc);
            c.Texto(c.Idioma == IdiomaDesenho.EN ? "COLUMN" : "PILAR", xf - 2 * passo, yb + 2.5 * passo, ht, 1);
            x = xf + 3 + 8 * ht;
            x = Math.Max(x, fimTitulo + 4 * hv);
            yBaixo = Math.Min(yBaixo, yb - 6 * ht);
        }

        // ---- espaçamento das barras no canto, 1:5 ----
        {
            double esc = 5, ht = Ht(c, esc);
            double phiC = v.Numero("CantoBitola"), phiV = v.Numero("VizBitola"), s = v.Numero("CantoEsp");
            bool l3 = v.Inteiro("BarrasPorCanto") >= 3;
            fimTitulo = Titulo("ESPAÇAMENTO BARRAS", "REBAR SPACING", x, yTopo, esc, "ESC 1:5");
            double canto = cob[2] + phiT / 10 + phiC / 20; // centro da barra do canto à face
            double fx = x + 2 * ht, fy = yTopo - 10 * ht;  // canto superior esquerdo da seção
            double tam = canto + (l3 ? s : 0) + 6;
            c.Linha(fx, fy, fx + tam, fy, "ARM-FORMA");
            c.Linha(fx, fy, fx, fy - tam, "ARM-FORMA");
            double cx = fx + canto, cy = fy - canto;
            r.Circulos.Add(new CirculoTemplate { X = cx, Y = cy, Raio = phiC / 20, Layer = "ARM-BARRA-SECAO" });
            if (l3)
            {
                r.Circulos.Add(new CirculoTemplate { X = cx + s, Y = fy - (cob[3] + phiT / 10 + phiV / 20), Raio = phiV / 20, Layer = "ARM-BARRA-SECAO" });
                r.Circulos.Add(new CirculoTemplate { X = fx + cob[2] + phiT / 10 + phiV / 20, Y = cy - s, Raio = phiV / 20, Layer = "ARM-BARRA-SECAO" });
                c.Cota(cx, fy + 1, cx + s, fy + 1, cx + s / 2, fy + 1 + 1.2 * ht, 0, esc);
                c.Cota(fx - 1, cy, fx - 1, cy - s, fx - 1 - 1.2 * ht, cy - s / 2, Math.PI / 2, esc);
            }
            c.Cota(fx, fy + 1, cx, fy + 1, fx + canto / 2, fy + 1 + 1.2 * ht, 0, esc);
            c.Cota(fx - 1, fy, fx - 1, cy, fx - 1 - 1.2 * ht, fy - canto / 2, Math.PI / 2, esc);
            x = fx + tam + 10 * ht;
            x = Math.Max(x, fimTitulo + 4 * hv);
            yBaixo = Math.Min(yBaixo, fy - tam - 6 * ht);
        }

        // ---- raio de curvatura (NBR 6118), tabela ----
        var tab = new TabelaTemplate
        {
            Titulo = c.Idioma switch
            {
                IdiomaDesenho.EN => "BEND RADIUS (r) FOR HOOKS",
                IdiomaDesenho.PT => "RAIO (r) DE CURVATURA PARA GANCHOS",
                _ => "RAIO (r) DE CURVATURA PARA GANCHOS" + TabelaTemplate.Quebra + "(BEND RADIUS (r) FOR HOOKS)",
            },
            X = x, Y = yTopo, Escala = c.escV,
        };
        tab.Larguras.AddRange(new[] { 15.0, 7.0, 7.0 });
        tab.Linhas.Add(new[] { c.Idioma == IdiomaDesenho.EN ? "DIAMETER (Ø)" : "BITOLA (Ø)", "CA50", "CA60" });
        tab.Linhas.Add(new[] { "< 20 mm", "2,5xØ", "3xØ" });
        tab.Linhas.Add(new[] { "≥ 20 mm", "4xØ", "—" });
        tab.Linhas.Add(new[] { c.Idioma == IdiomaDesenho.EN ? "STIRRUPS ≤ 10 mm" : "ESTRIBO ≤ 10 mm", "1,5xØ", "1,5xØ" });
        r.Tabelas.Add(tab);
        c.Texto("NBR 6118", x, yTopo - (tab.AlturaEmTextos() + 4) * hv, 0.8 * hv);
        x += 29 * hv + 8 * hv;

        // ---- emendas por traspasse e por solda (esquemas) ----
        {
            double ht = hv, y = yTopo;
            foreach (var t in c.Linhas("EMENDAS POR TRASPASSE", "LAP SPLICES")) { c.Texto(t, x, y, 1.1 * ht); y -= 1.7 * ht; }
            y -= 2 * ht;
            double comp = 30 * ht, sobre = 12 * ht;
            c.Retangulo(x, y, x + comp, y + 0.6 * ht, "ARM-BARRA-DETALHE");
            c.Retangulo(x + comp - sobre, y - 1.2 * ht, x + 2 * comp - sobre, y - 0.6 * ht, "ARM-BARRA-DETALHE");
            c.Cota(x + comp - sobre, y - 2 * ht, x + comp, y - 2 * ht, x + comp - sobre / 2, y - 3.2 * ht, 0, c.escV, "≥ 45ø");
            y -= 7 * ht;
            foreach (var t in c.Linhas("EMENDAS POR SOLDA POR TRASPASSE", "LAP WELD SPLICES")) { c.Texto(t, x, y, 1.1 * ht); y -= 1.7 * ht; }
            y -= 2 * ht;
            c.Retangulo(x, y, x + comp, y + 0.6 * ht, "ARM-BARRA-DETALHE");
            c.Retangulo(x + comp - sobre, y - 0.6 * ht, x + 2 * comp - sobre, y, "ARM-BARRA-DETALHE");
            c.Cota(x + comp - sobre, y - 1.6 * ht, x + comp, y - 1.6 * ht, x + comp - sobre / 2, y - 2.8 * ht, 0, c.escV, "≥ 5ø");
            y -= 5 * ht;
            var notasPt = new[] { "NOTAS:", "1) EMENDA POR TRASPASSE NÃO PERMITIDA PARA BARRA > ø32 mm;",
                "2) INDICAÇÕES VÁLIDAS QUANDO NÃO HOUVER RECOMENDAÇÕES ADVERSAS NO PROJETO;",
                "3) A1 E B1 SÃO OS VALORES INDICADOS NO DETALHAMENTO DAS ARMADURAS." };
            var notasEn = new[] { "NOTES:", "1) LAP SPLICES ARE NOT ALLOWED FOR BARS > ø32 mm;",
                "2) VALID WHEN THERE ARE NO ADVERSE RECOMMENDATIONS IN THE DESIGN;",
                "3) A1 AND B1 DENOTE THE VALUES SPECIFIED IN THE REINFORCEMENT DETAILING." };
            if (c.Idioma != IdiomaDesenho.EN) y = c.Bloco(notasPt, x, y, 0.8 * ht) - ht;
            if (c.Idioma != IdiomaDesenho.PT) y = c.Bloco(notasEn, x, y, 0.8 * ht) - ht;
            // comprimento das barras: U com A1 e B1 (medidas externas)
            foreach (var t in c.Linhas("COMPRIMENTO DAS BARRAS", "REBAR LENGTH")) { c.Texto(t, x, y, 1.1 * ht); y -= 1.7 * ht; }
            double ux = x + 2 * ht, uy = y - 8 * ht, uw = 26 * ht, uh = 6 * ht;
            r.Contornos.Add(new ContornoTemplate { Vertices = { (ux, uy + uh), (ux, uy), (ux + uw, uy), (ux + uw, uy + uh) }, Layer = "ARM-BARRA-DETALHE" });
            c.Cota(ux - ht, uy, ux - ht, uy + uh, ux - 2.2 * ht, uy + uh / 2, Math.PI / 2, c.escV, "A1");
            c.Cota(ux, uy - ht, ux + uw, uy - ht, ux + uw / 2, uy - 2.2 * ht, 0, c.escV, "B1");
            yBaixo = Math.Min(yBaixo, uy - 4 * ht);
            x += 2 * comp + 10 * ht;
        }

        // ---- SPDA ----
        if (v.SimNao("AvisoSPDA"))
        {
            var spda = new List<string>();
            if (c.Idioma != IdiomaDesenho.EN) spda.AddRange(new[] { "ATENÇÃO!", "P/ ELEMENTOS, CONEXÕES E POSICIONAMENTO", "DO SISTEMA DE SPDA, VER PROJETO ESPECÍFICO" });
            if (c.Idioma != IdiomaDesenho.PT) spda.AddRange(new[] { "ATTENTION!", "FOR ELEMENTS, CONNECTIONS AND POSITIONING", "OF LPS SYSTEM, SEE SPECIFIC DESIGN" });
            double yy = c.Bloco(spda, x + hv, yTopo - hv, 0.9 * hv);
            c.Retangulo(x, yy, x + 42 * hv, yTopo + hv, "ARM-TEXTO-FORMA");
        }
        return yBaixo - 10 * hv;
    }
}
