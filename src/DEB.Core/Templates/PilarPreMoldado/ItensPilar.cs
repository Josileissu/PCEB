using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Modelo;

namespace DEB.Core.Templates.PilarPreMoldado;

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

    private const string G17 = "13. Içamento (alças)", G18 = "14. Montagem", G19 = "15. Insertos metálicos", G20 = "16. Detalhes padrão";

    /// <summary>Catálogo de insertos (o plugin põe aqui o da configuração do usuário; ARMINSERTOS edita).</summary>
    public static List<TipoInserto> CatalogoInsertos { get; set; } = TipoInserto.Padrao();
    public const string LocalTopo = "Topo";

    /// <summary>Lista dos insertos do pilar (janela): tipo do catálogo, local (topo ou face), distância da base, deslocamento.</summary>
    private static ParametroTemplate ParametroInsertos() => new ParametroTemplate
    {
        Grupo = G19, Nome = "Insertos", Rotulo = "Insertos do pilar", Tipo = TipoParametro.Tabela, NomeItem = "inserto",
        Padrao = CatalogoInsertos.Any(t => t.NoTopo) ? $"{CatalogoInsertos.First(t => t.NoTopo).Nome}|{LocalTopo}|0|0" : "",
        Descricao = "Clique em … para incluir os insertos: tipo (catálogo do ARMINSERTOS), local (Topo ou Face 1…4), distância da base " +
                    "ao centro do inserto (faces) e deslocamento lateral.",
        Colunas =
        {
            new ColunaTabela { Nome = "Tipo", Rotulo = "Tipo", Tipo = TipoParametro.Opcao, Opcoes = CatalogoInsertos.Select(t => t.Nome).ToList(),
                Padrao = CatalogoInsertos.Count > 0 ? CatalogoInsertos[0].Nome : "", Largura = 160 },
            new ColunaTabela { Nome = "Local", Rotulo = "Local", Tipo = TipoParametro.Opcao,
                Opcoes = new List<string> { LocalTopo, "Face 1", "Face 2", "Face 3", "Face 4" }, Padrao = LocalTopo, Largura = 80 },
            new ColunaTabela { Nome = "Dist", Rotulo = "Base → centro (cm)", Tipo = TipoParametro.Numero, Padrao = "0", Largura = 110 },
            new ColunaTabela { Nome = "Desl", Rotulo = "Deslocamento (cm)", Tipo = TipoParametro.Numero, Padrao = "0", Largura = 110 },
        },
    };

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

        return it;
    }

    /// <summary>Inserto colocado no pilar.</summary>
    public sealed class InsertoPilar
    {
        public TipoInserto Tipo { get; set; }
        public bool Topo { get; set; }
        public int Face { get; set; }
        /// <summary>Distância da base ao centro (insertos de face), cm.</summary>
        public double Z { get; set; }
        public double Desl { get; set; }
    }

    public static List<InsertoPilar> LerInsertos(ValoresTemplate v, IReadOnlyList<TipoInserto> catalogo, double L, List<string> avisos)
    {
        var r = new List<InsertoPilar>();
        int k = 0;
        foreach (var l in v.Tabela("Insertos"))
        {
            k++;
            string nome = ValoresTemplate.Celula(l, 0);
            if (nome.Length == 0) continue;
            var t = TipoInserto.Obter(catalogo, nome);
            if (t == null) { avisos.Add($"Inserto {k}: tipo \"{nome}\" não existe no catálogo (ARMINSERTOS)."); continue; }
            string local = ValoresTemplate.Celula(l, 1);
            bool topo = local.Length == 0 || local.StartsWith(LocalTopo, StringComparison.OrdinalIgnoreCase);
            int face = topo ? 0 : local.Where(char.IsDigit).Select(ch => ch - '0').FirstOrDefault();
            if (!topo && (face < 1 || face > 4)) { avisos.Add($"Inserto {k}: local \"{local}\" inválido (Topo ou Face 1…4)."); continue; }
            if (topo != t.NoTopo)
                avisos.Add($"Inserto {k}: o tipo \"{t.Nome}\" é {(t.NoTopo ? "de topo" : "de face")} e foi posto {(topo ? "no topo" : "numa face")}.");
            var ins = new InsertoPilar { Tipo = t, Topo = topo, Face = face, Z = ValoresTemplate.NumeroCelula(l, 2), Desl = ValoresTemplate.NumeroCelula(l, 3) };
            if (!topo && (ins.Z < 0 || ins.Z > L)) { avisos.Add($"Inserto {k}: fora do pilar ({Num(ins.Z, "0.#")} cm da base)."); continue; }
            r.Add(ins);
        }
        return r;
    }

    /// <summary>Insertos nas vistas: chumbadores do topo (e o nome acima da VISTA 1) ou a chapa na face com as grapas.</summary>
    private void DesenharInsertosNasVistas(Ctx c, List<InsertoPilar> insertos, double[] xs, double[] larguras, double L)
    {
        double hv = c.hV;
        double yNome = L + 3.5 * hv;
        // chapas de frente por vista (cotadas da base em escada e com UM nome por tipo, como no P56)
        var deFrente = new Dictionary<int, List<(InsertoPilar ins, double um)>>();
        foreach (var ins in insertos)
        {
            var t = ins.Tipo;
            if (ins.Topo)
            {
                for (int vista = 1; vista <= 4; vista++)
                {
                    double xm = xs[vista - 1] + larguras[vista - 1] / 2 + (vista == 2 || vista == 4 ? ins.Desl : 0);
                    double a = vista == 1 || vista == 3 ? t.Ay : t.Ax;
                    foreach (double u in t.ChumbN <= 1 ? new[] { 0.0 } : new[] { -a / 2, a / 2 })
                        c.Linha(xm + u, L + t.ChumbAcima, xm + u, L - t.ChumbComp, "ARM-INSERTO");
                }
                // nome à esquerda da VISTA 1, sublinhado, com a linha de chamada até os chumbadores (como no P56)
                double xn = xs[0] - 4 * hv, xAlvo = xs[0] + larguras[0] / 2 - (t.ChumbN <= 1 ? 0 : t.Ay / 2);
                c.Texto("INSERTO", xn, yNome + 1.3 * hv, 0.9 * hv, 1, 0, "ARM-COTA-FORMA");
                c.Texto(t.Nome, xn, yNome, 0.9 * hv, 1, 0, "ARM-COTA-FORMA");
                c.Linha(xn - 3 * hv, yNome + 1.0 * hv, xn + 3 * hv, yNome + 1.0 * hv, "ARM-COTA-FORMA");
                c.Linha(xn + 3 * hv, yNome + 1.0 * hv, xAlvo, L + t.ChumbAcima, "ARM-COTA-FORMA");
                yNome += 3.2 * hv; // o próximo inserto do topo vai acima
                continue;
            }
            // chapa na face: de frente o retângulo; de lado a espessura e as grapas entrando no pilar
            for (int vista = 1; vista <= 4; vista++)
            {
                double x0 = xs[vista - 1], x1 = x0 + larguras[vista - 1];
                if (vista == ins.Face)
                {
                    double um = (x0 + x1) / 2 + ins.Desl;
                    c.Retangulo(um - t.ChapaL / 2, ins.Z - t.ChapaH / 2, um + t.ChapaL / 2, ins.Z + t.ChapaH / 2, "ARM-INSERTO");
                    if (!deFrente.TryGetValue(vista, out var l)) deFrente[vista] = l = new List<(InsertoPilar, double)>();
                    l.Add((ins, um));
                }
                else if (FaceEsquerda(vista) == ins.Face || FaceDireita(vista) == ins.Face)
                {
                    bool esq = FaceEsquerda(vista) == ins.Face;
                    double xf = esq ? x0 : x1, s = esq ? 1 : -1; // para dentro do pilar
                    c.Retangulo(Math.Min(xf, xf + s * t.ChapaE), ins.Z - t.ChapaH / 2, Math.Max(xf, xf + s * t.ChapaE), ins.Z + t.ChapaH / 2, "ARM-INSERTO");
                    int nLin = Math.Max(1, t.GrapaN / 2);
                    for (int i = 0; i < nLin; i++)
                    {
                        double z = nLin == 1 ? ins.Z : ins.Z - t.ChapaH / 2 + t.ChapaH * (i + 0.5) / nLin;
                        c.Linha(xf + s * t.ChapaE, z, xf + s * (t.ChapaE + t.GrapaComp), z, "ARM-INSERTO");
                    }
                }
            }
        }
        foreach (var kv in deFrente)
        {
            double x1 = xs[kv.Key - 1] + larguras[kv.Key - 1];
            int k = 0;
            // da base ao centro de cada chapa, uma coluna para cada (a linha de chamada sai da chapa até a sua coluna)
            foreach (var (ins, um) in kv.Value.OrderBy(q => q.ins.Z))
            {
                double xc = x1 + (2.5 + 2.1 * k++) * hv;
                c.Cota(x1, 0, um + ins.Tipo.ChapaL / 2, ins.Z, xc, ins.Z / 2, Math.PI / 2, c.escV);
            }
            foreach (var grupo in kv.Value.GroupBy(q => q.ins.Tipo.Nome))
            {
                var (ins, um) = grupo.OrderByDescending(q => q.ins.Z).First();
                double yt = ins.Z + ins.Tipo.ChapaH / 2 + 3 * hv, xt = um + 4 * hv;
                foreach (var tx in c.Linhas($"INSERTO {grupo.Key}", $"{grupo.Key} INSERT")) { c.Texto(tx, xt, yt, 0.8 * hv, 0, 0, "ARM-COTA-FORMA"); yt -= 1.2 * hv; }
                c.Linha(xt, ins.Z + ins.Tipo.ChapaH / 2 + 2.6 * hv, um + ins.Tipo.ChapaL / 2, ins.Z + ins.Tipo.ChapaH / 2, "ARM-COTA-FORMA");
            }
        }
    }

    /// <summary>Gabarito do inserto de topo no corte A-A (chumbadores nos cantos e cotas em cadeia, como o P61: 18,5 | 13 | 18,5).</summary>
    private static bool InsertoNoCorte(Ctx c, List<InsertoPilar> insertos, double sx0, double sy0, double b, double h)
    {
        var ins = insertos.FirstOrDefault(i => i.Topo);
        if (ins == null) return false;
        var t = ins.Tipo;
        double rc = t.ChumbPhi / 20, hc = c.hC;
        double gx0 = sx0 + (b - t.Ax) / 2 + ins.Desl, gy0 = sy0 + (h - t.Ay) / 2;
        c.Retangulo(gx0, gy0, gx0 + t.Ax, gy0 + t.Ay, "ARM-INSERTO");
        var pts = t.ChumbN <= 1 ? new[] { (gx0 + t.Ax / 2, gy0 + t.Ay / 2) }
            : t.ChumbN == 2 ? new[] { (gx0, gy0 + t.Ay / 2), (gx0 + t.Ax, gy0 + t.Ay / 2) }
            : new[] { (gx0, gy0), (gx0 + t.Ax, gy0), (gx0 + t.Ax, gy0 + t.Ay), (gx0, gy0 + t.Ay) };
        foreach (var (x, y) in pts) c.R.Circulos.Add(new CirculoTemplate { X = x, Y = y, Raio = rc, Layer = "ARM-INSERTO" });
        double yc = sy0 + h + 1.5 * hc;
        c.Cota(sx0, yc, gx0, yc, (sx0 + gx0) / 2, yc + 1.2 * hc, 0, c.escC);
        c.Cota(gx0, yc, gx0 + t.Ax, yc, gx0 + t.Ax / 2, yc + 1.2 * hc, 0, c.escC);
        c.Cota(gx0 + t.Ax, yc, sx0 + b, yc, (gx0 + t.Ax + sx0 + b) / 2, yc + 1.2 * hc, 0, c.escC);
        double xl = sx0 - 1.5 * hc;
        c.Cota(xl, sy0, xl, gy0, xl - 1.2 * hc, (sy0 + gy0) / 2, Math.PI / 2, c.escC);
        c.Cota(xl, gy0, xl, gy0 + t.Ay, xl - 1.2 * hc, gy0 + t.Ay / 2, Math.PI / 2, c.escC);
        c.Cota(xl, gy0 + t.Ay, xl, sy0 + h, xl - 1.2 * hc, (gy0 + t.Ay + sy0 + h) / 2, Math.PI / 2, c.escC);
        if (t.Gabarito.Length > 0) c.Texto("GABARITO " + t.Gabarito, gx0 + t.Ax + 2 * hc, gy0 - 2.5 * hc, 0.8 * hc, 0, 0, "ARM-INSERTO");
        return true;
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

        // ---- chaves de cisalhamento, 1:10 (como o detalhe 01 do P61) ----
        if (v["Interface"] == Chaves)
            x = DetalhesTipicos.Chaves(Desenho(c), x, yTopo, v.Numero("ChavePasso"), v.Numero("ChaveDente"), v.Numero("ChaveProf"), ref yBaixo);

        // ---- espaçamento das barras no canto, 1:5 ----
        {
            double esc = 5, ht = Ht(c, esc);
            var (phiC, phiV, l3) = BitolasDoCanto(v);
            double s = v.Numero("CantoEsp");
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

        // (detalhe típico de raio de curvatura, emendas e comprimento das barras: removido do pilar a pedido do usuário,
        // 2026-11-07 — o desenho gerado ficou feio; o padrão é o do escritório em auxiliar_cad/pilar_padrão)

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

    /// <summary>Contexto de desenho comum (detalhes típicos e seta), na escala das vistas.</summary>
    private static DesenhoTemplate Desenho(Ctx c) => new DesenhoTemplate(c.R, c.Idioma, c.escV, c.hV);

    private static void Seta(Ctx c, (double X, double Y) de, (double X, double Y) para, double ht, string layer) =>
        Desenho(c).Seta(de, para, ht, layer);

    /// <summary>Perfil das chaves de cisalhamento (ver <see cref="DetalhesTipicos.PerfilChaves"/>).</summary>
    public static (List<(double X, double Y)> Perfil, List<(double X, double Y)> Arestas) PerfilChaves(double passo, double dente, double prof, int n) =>
        DetalhesTipicos.PerfilChaves(passo, dente, prof, n);
}
