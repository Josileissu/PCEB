using System;
using System.Collections.Generic;
using System.Linq;
using Armadura.Core.Configuracao;
using Armadura.Core.Estribos;
using Armadura.Core.Modelo;
using Armadura.Core.Normas;
using Armadura.Core.Tabelas;

namespace Armadura.Core.Templates.PilarPreMoldado;

/// <summary>Barra longitudinal na seção (coordenadas da seção: origem no canto inferior esquerdo, x ao longo de b).</summary>
public readonly struct BarraNaSecao
{
    public BarraNaSecao(double x, double y, double bitola, string grupo) { X = x; Y = y; Bitola = bitola; Grupo = grupo; }
    public double X { get; }
    public double Y { get; }
    public double Bitola { get; }
    /// <summary>"canto", "vizinha" (ao lado do canto), "faceB" (faces 2 e 4) ou "faceH" (faces 1 e 3).</summary>
    public string Grupo { get; }
}

/// <summary>Trecho de uma barra emendada, ao longo do pilar (cm a partir da base).</summary>
public readonly struct PecaBarra
{
    public PecaBarra(double ini, double fim) { Ini = ini; Fim = fim; }
    public double Ini { get; }
    public double Fim { get; }
    public double Comprimento => Fim - Ini;
}

/// <summary>
/// Etapa 2 do pilar (2026-10-29): armaduras — longitudinais (cantos com 1 ou 3 barras em "L", faces), emendas
/// alternadas ou na mesma seção (ou barras em rolo), estribos por zona (topo, corrente, base) com grampos, grampos U
/// no topo e na base, elevação das armaduras com as faixas, seções 1-1/2-2/3-3 com as barras em ponto e as
/// indicações N (numeração automática), formas dos estribos e grampos, e a relação do aço do detalhamento.
/// </summary>
public sealed partial class TemplatePilarPreMoldado
{
    public const string SemEmendas = "Sem emendas (barras em rolo)", ComEmendas = "Com emendas";
    public const string Alternadas = "Alternadas", MesmaSecao = "Na mesma seção";
    public const string GramposNenhum = "Nenhum", GramposTopoBase = "Topo e base", GramposTodas = "Todas as zonas";

    private const string G7 = "7. Armadura longitudinal", G8 = "8. Emendas", G9 = "9. Estribos", G10 = "10. Grampos suplementares",
        G11 = "11. Grampos U (topo e base)";

    private static readonly ParametroTemplate[] ParametrosArmadura =
    {
        P(G7, "LongAco", "Aço", "CA-50", tipo: TipoParametro.Opcao, opcoes: new[] { "CA-50", "CA-25" }),
        P(G7, "CantoBitola", "Bitola da barra do canto", "25", "mm", TipoParametro.Bitola, 10, 40),
        P(G7, "BarrasPorCanto", "Barras por canto (1 ou 3 em \"L\")", "3", tipo: TipoParametro.Inteiro, min: 1, max: 3),
        P(G7, "CantoEsp", "Canto em \"L\": distância entre eixos", "5", "cm", min: 2),
        P(G7, "VizBitola", "Bitola das barras ao lado do canto", "20", "mm", TipoParametro.Bitola, 10, 40),
        P(G7, "FaceBNum", "Barras intermediárias por face b (faces 2 e 4)", "0", tipo: TipoParametro.Inteiro, min: 0, max: 20),
        P(G7, "FaceHNum", "Barras intermediárias por face h (faces 1 e 3)", "0", tipo: TipoParametro.Inteiro, min: 0, max: 20),
        P(G7, "FaceBitola", "Bitola das barras intermediárias", "20", "mm", TipoParametro.Bitola, 10, 40),
        P(G7, "DiamAgregado", "Diâmetro máximo do agregado", "19", "mm", min: 4),

        P(G8, "Emendas", "Emendas das longitudinais", ComEmendas, tipo: TipoParametro.Opcao, opcoes: new[] { ComEmendas, SemEmendas },
          descricao: "Com emendas: barras divididas no comprimento comercial, traspasse pela NBR 6118 e nunca menor que 45φ, " +
                     "com nota ao lado. Sem emendas: barras inteiras (em rolo)."),
        P(G8, "EmendaArranjo", "Emendas", Alternadas, tipo: TipoParametro.Opcao, opcoes: new[] { Alternadas, MesmaSecao }),
        P(G8, "Comercial", "Comprimento comercial das barras", "1200", "cm", min: 100),

        P(G9, "EstAco", "Aço", "CA-50", tipo: TipoParametro.Opcao, opcoes: new[] { "CA-50", "CA-60" }),
        P(G9, "EstBitola", "Trecho corrente: bitola", "6.3", "mm", TipoParametro.Bitola, 5, 16),
        P(G9, "EstEsp", "Trecho corrente: espaçamento", "15", "cm", min: 2),
        P(G9, "TopoComp", "Zona do topo: comprimento", "100", "cm", min: 0, descricao: "0 = sem zona própria."),
        P(G9, "TopoBitola", "Zona do topo: bitola", "8", "mm", TipoParametro.Bitola, 5, 16),
        P(G9, "TopoEsp", "Zona do topo: espaçamento", "5", "cm", min: 2),
        P(G9, "BaseComp", "Zona da base: comprimento", "260", "cm", min: 0, descricao: "0 = sem zona própria."),
        P(G9, "BaseBitola", "Zona da base: bitola", "8", "mm", TipoParametro.Bitola, 5, 16),
        P(G9, "BaseEsp", "Zona da base: espaçamento", "10", "cm", min: 2),

        P(G10, "Grampos", "Grampos", GramposTopoBase, tipo: TipoParametro.Opcao, opcoes: new[] { GramposNenhum, GramposTopoBase, GramposTodas }),
        P(G10, "GrampoDirecoes", "Direções (1 = só x, 2 = x e y)", "2", tipo: TipoParametro.Inteiro, min: 1, max: 2),
        P(G10, "GrampoBitola", "Bitola", "6.3", "mm", TipoParametro.Bitola, 5, 16),

        P(G11, "UTopo", "U no topo", "Sim", tipo: TipoParametro.SimNao),
        P(G11, "UTopoBitola", "Topo: bitola", "12.5", "mm", TipoParametro.Bitola, 6.3, 25),
        P(G11, "UTopoQtd", "Topo: quantidade por direção", "3", tipo: TipoParametro.Inteiro, min: 1, max: 10),
        P(G11, "UTopoPerna", "Topo: comprimento das pernas", "88", "cm", min: 10),
        P(G11, "UBase", "U na base", "Sim", tipo: TipoParametro.SimNao),
        P(G11, "UBaseBitola", "Base: bitola", "12.5", "mm", TipoParametro.Bitola, 6.3, 25),
        P(G11, "UBaseQtd", "Base: quantidade por direção", "3", tipo: TipoParametro.Inteiro, min: 1, max: 10),
        P(G11, "UBasePerna", "Base: comprimento das pernas", "288", "cm", min: 10),
    };

    // ======================== cálculos (testáveis) ========================

    /// <summary>
    /// Posições das longitudinais na seção. Cobrimentos por face (1 = +x, 2 = −y, 3 = −x, 4 = +y) até o estribo;
    /// φt = maior estribo (mm). Cantos com 1 barra ou 3 em "L" (a do canto e uma ao lado em cada face, a
    /// <paramref name="sCanto"/> entre eixos); intermediárias distribuídas por igual entre as do canto.
    /// </summary>
    public static List<BarraNaSecao> PosicoesBarras(double b, double h, double[] cob, double phiT, double phiCanto, int porCanto,
        double sCanto, double phiViz, int nFaceB, int nFaceH, double phiFace)
    {
        double t = phiT / 10;
        double rc = phiCanto / 20;
        double xl = cob[2] + t + rc, xr = b - cob[0] - t - rc, yb = cob[1] + t + rc, yt = h - cob[3] - t - rc;
        var r = new List<BarraNaSecao>();
        foreach (var (x, y) in new[] { (xl, yb), (xr, yb), (xr, yt), (xl, yt) }) r.Add(new BarraNaSecao(x, y, phiCanto, "canto"));
        double ax = xl, bx = xr, ay = yb, by = yt; // limites das intermediárias
        if (porCanto >= 3)
        {
            double rv = phiViz / 20;
            // vizinhas: alinhadas à face (mesmo cobrimento até o estribo), a sCanto da barra do canto
            double ybv = cob[1] + t + rv, ytv = h - cob[3] - t - rv, xlv = cob[2] + t + rv, xrv = b - cob[0] - t - rv;
            r.Add(new BarraNaSecao(xl + sCanto, ybv, phiViz, "vizinha")); r.Add(new BarraNaSecao(xlv, yb + sCanto, phiViz, "vizinha"));
            r.Add(new BarraNaSecao(xr - sCanto, ybv, phiViz, "vizinha")); r.Add(new BarraNaSecao(xrv, yb + sCanto, phiViz, "vizinha"));
            r.Add(new BarraNaSecao(xr - sCanto, ytv, phiViz, "vizinha")); r.Add(new BarraNaSecao(xrv, yt - sCanto, phiViz, "vizinha"));
            r.Add(new BarraNaSecao(xl + sCanto, ytv, phiViz, "vizinha")); r.Add(new BarraNaSecao(xlv, yt - sCanto, phiViz, "vizinha"));
            ax = xl + sCanto; bx = xr - sCanto; ay = yb + sCanto; by = yt - sCanto;
        }
        double rf = phiFace / 20;
        for (int k = 1; k <= nFaceB; k++)
        {
            double x = ax + k * (bx - ax) / (nFaceB + 1);
            r.Add(new BarraNaSecao(x, cob[1] + t + rf, phiFace, "faceB"));
            r.Add(new BarraNaSecao(x, h - cob[3] - t - rf, phiFace, "faceB"));
        }
        for (int k = 1; k <= nFaceH; k++)
        {
            double y = ay + k * (by - ay) / (nFaceH + 1);
            r.Add(new BarraNaSecao(cob[2] + t + rf, y, phiFace, "faceH"));
            r.Add(new BarraNaSecao(b - cob[0] - t - rf, y, phiFace, "faceH"));
        }
        return r;
    }

    /// <summary>Traspasse do escritório: o da NBR 6118 (fck e % emendada na mesma seção), nunca menor que 45φ, arredondado a 5 cm.</summary>
    public static double TraspassePilar(double bitola, string aco, double fck, bool mesmaSecao, TabelaBarras barras)
    {
        var regras = new RegrasTraspasse { Fck = fck, PercentualEmendado = mesmaSecao ? 100 : 50, Modo = ModoTraspasse.Calculado };
        double norma = regras.Comprimento(bitola, aco, barras);
        double l = Math.Max(norma, 45 * bitola / 10);
        return Math.Ceiling(l / 5 - 1e-9) * 5;
    }

    /// <summary>
    /// Peças de uma barra de <paramref name="ini"/> a <paramref name="fim"/> com emendas: a 1ª tem
    /// <paramref name="primeira"/>, as seguintes começam um traspasse antes do fim da anterior e têm até o comercial.
    /// </summary>
    public static List<PecaBarra> Pecas(double ini, double fim, double comercial, double traspasse, double primeira)
    {
        var r = new List<PecaBarra>();
        if (fim - ini <= comercial + 1e-6 || traspasse >= comercial) { r.Add(new PecaBarra(ini, fim)); return r; }
        double a = ini, b = Math.Min(fim, ini + Math.Min(primeira, comercial));
        r.Add(new PecaBarra(a, b));
        while (b < fim - 1e-6)
        {
            a = b - traspasse;
            b = Math.Min(fim, a + comercial);
            r.Add(new PecaBarra(a, b));
        }
        return r;
    }

    /// <summary>1ª peça do grupo alternado (B): emendas afastadas de pelo menos 1,3 traspasse (NBR 6118, 9.5.2.1).</summary>
    public static double PrimeiraAlternada(double comercial, double traspasse) =>
        Math.Floor((comercial - Math.Ceiling(1.3 * traspasse / 5) * 5) / 5) * 5;

    // ======================== desenho ========================

    private sealed class Grupo
    {
        public string Chave;            // "canto", "vizinha", "faceB", "faceH"
        public double Bitola;
        public List<BarraNaSecao> Barras = new List<BarraNaSecao>();
    }

    /// <summary>Uma longitudinal desenhada na elevação (uma peça de um grupo, A ou B).</summary>
    private sealed class Longitudinal
    {
        public string Nome;
        public Grupo Grupo;
        public bool B;                  // grupo alternado
        public PecaBarra Peca;
        public int Quantidade;
    }

    /// <summary>Desenha as armaduras a partir de <paramref name="x0"/>; devolve o x onde elas terminam.</summary>
    private double GerarArmaduras(Ctx c, ValoresTemplate v, ConfiguracaoArmadura cfg, double x0, double b, double h, double L, double ch)
    {
        var r = c.R;
        double hv = c.hV, hc = c.hC;
        var cob = new[] { "CobFace1", "CobFace2", "CobFace3", "CobFace4", "CobTopo", "CobBase" }.Select(k => v.Numero(k)).ToArray();
        string aco = v["LongAco"], acoEst = v["EstAco"];
        double phiCanto = v.Numero("CantoBitola"), phiViz = v.Numero("VizBitola"), phiFace = v.Numero("FaceBitola");
        int porCanto = v.Inteiro("BarrasPorCanto") >= 3 ? 3 : 1;
        double sCanto = v.Numero("CantoEsp");
        double phiEst = v.Numero("EstBitola"), phiTopo = v.Numero("TopoBitola"), phiBase = v.Numero("BaseBitola");
        double zTopo = Math.Max(0, v.Numero("TopoComp")), zBase = Math.Max(0, v.Numero("BaseComp"));
        if (zTopo + zBase > L) { zTopo = zBase = 0; r.Avisos.Add("Zonas de estribos do topo e da base maiores que o pilar: ignoradas."); }
        double phiT = Math.Max(phiEst, Math.Max(zTopo > 0 ? phiTopo : 0, zBase > 0 ? phiBase : 0));

        var barras = PosicoesBarras(b, h, cob, phiT, phiCanto, porCanto, sCanto, phiViz, v.Inteiro("FaceBNum"), v.Inteiro("FaceHNum"), phiFace);
        var grupos = barras.GroupBy(x => x.Grupo).Select(g => new Grupo { Chave = g.Key, Bitola = g.First().Bitola, Barras = g.ToList() }).ToList();
        VerificarSecao(r, b, h, barras, phiT, v);

        // ---------------- longitudinais: peças (emendas) ----------------
        double yIni = cob[5], yFim = L - cob[4];
        bool emendas = v["Emendas"] == ComEmendas, mesma = v["EmendaArranjo"] == MesmaSecao;
        double comercialPar = v.Numero("Comercial") > 0 ? v.Numero("Comercial") : 1200;
        var longs = new List<Longitudinal>();
        foreach (var g in grupos)
        {
            var info = cfg.Barras.Obter(g.Bitola, aco);
            double comercial = info != null && info.ComprimentoComercial > 0 ? Math.Min(info.ComprimentoComercial, comercialPar) : comercialPar;
            if (!emendas || yFim - yIni <= comercial)
            {
                if (!emendas && yFim - yIni > comercial && (info == null || info.Fabricacao != Fabricacao.Rolo))
                    r.Avisos.Add($"ø{Num(g.Bitola, "0.0")} sem emendas ({Num(yFim - yIni, "0.#")} cm): na tabela de bitolas ela é vendida em barras de " +
                                 $"{Num(comercial, "0")} cm — confira a disponibilidade em rolo.");
                longs.Add(new Longitudinal { Grupo = g, Peca = new PecaBarra(yIni, yFim), Quantidade = g.Barras.Count });
                continue;
            }
            double lap = TraspassePilar(g.Bitola, aco, v.Numero("Fck"), mesma, cfg.Barras);
            int nA = mesma ? g.Barras.Count : (g.Barras.Count + 1) / 2, nB = g.Barras.Count - nA;
            foreach (var p in Pecas(yIni, yFim, comercial, lap, comercial))
                longs.Add(new Longitudinal { Grupo = g, Peca = p, Quantidade = nA });
            if (nB > 0)
                foreach (var p in Pecas(yIni, yFim, comercial, lap, PrimeiraAlternada(comercial, lap)))
                    longs.Add(new Longitudinal { Grupo = g, B = true, Peca = p, Quantidade = nB });
        }

        // ---------------- elevação das longitudinais ----------------
        double x = x0;
        int seq = 0;
        var colunaDoGrupo = new Dictionary<(Grupo, bool), double>();
        foreach (var grupoLado in longs.GroupBy(l => (l.Grupo, l.B)))
        {
            var pecas = grupoLado.OrderBy(l => l.Peca.Ini).ToList();
            colunaDoGrupo[grupoLado.Key] = x;
            for (int k = 0; k < pecas.Count; k++)
            {
                var l = pecas[k];
                l.Nome = $"long{++seq}";
                double xp = x + (k % 2) * 1.2 * hv; // peças emendadas lado a lado, para ver o traspasse
                r.Barras.Add(new BarraTemplate
                {
                    Nome = l.Nome, Vertices = { (xp, l.Peca.Ini), (xp, l.Peca.Fim) }, Bitola = l.Grupo.Bitola, Aco = aco,
                    Quantidade = l.Quantidade, Escala = c.escV,
                    // descrição na vertical, ao lado da barra (como no P61); a cota do comprimento fica do outro lado
                    PosicaoDescricao = (xp + 1.0 * hv, (l.Peca.Ini + l.Peca.Fim) / 2), RotacaoDescricao = Math.PI / 2,
                });
                if (k > 0)
                {
                    // emenda: cota do traspasse e a nota para o projetista
                    double a = l.Peca.Ini, bFim = pecas[k - 1].Peca.Fim;
                    c.Cota(x + 3.6 * hv, a, x + 3.6 * hv, bFim, x + 4.6 * hv, (a + bFim) / 2, Math.PI / 2, c.escV);
                    // texto na vertical, centrado na emenda; PT e EN lado a lado
                    double xn = x + 6.0 * hv;
                    foreach (var t in c.Linhas($"EMENDA: TRASPASSE {Num(bFim - a, "0")} cm (NBR 6118, MÍN. 45ø) - AJUSTAR SE NECESSÁRIO",
                                               $"SPLICE: LAP {bFim - a:0} cm (NBR 6118, MIN. 45ø) - ADJUST IF NECESSARY"))
                    {
                        c.Texto(t, xn, (a + bFim) / 2, 0.6 * hv, 1, Math.PI / 2);
                        xn += 0.9 * hv;
                    }
                }
            }
            x += 12 * hv;
        }

        // ---------------- grampos U (elevação) ----------------
        double wEst = b - cob[0] - cob[2], hEst = h - cob[1] - cob[3]; // medidas externas do estribo
        var us = new List<(string nome, bool topo, int dir, double w, double phi, int qtd)>();
        foreach (var topo in new[] { true, false })
        {
            string k = topo ? "UTopo" : "UBase";
            if (!v.SimNao(k)) continue;
            double phiU = v.Numero(k + "Bitola"), perna = v.Numero(k + "Perna");
            int qtd = Math.Max(1, v.Inteiro(k + "Qtd"));
            // direção x: por dentro do estribo; direção y: dentro da primeira, afastada do canto
            double w1 = Math.Floor(wEst - 2 * phiT / 10 - phiU / 10);
            double recuo = porCanto >= 3 ? sCanto : (phiCanto + phiU) / 10;
            double w2 = Math.Floor(hEst - 2 * phiT / 10 - phiU / 10 - 2 * recuo);
            for (int dir = 0; dir < 2; dir++)
            {
                double w = dir == 0 ? w1 : w2;
                string nome = $"U{(topo ? "topo" : "base")}{dir}";
                double y0 = topo ? yFim : yIni, s = topo ? -1 : 1;
                r.Barras.Add(new BarraTemplate
                {
                    Nome = nome, Vertices = { (x, y0 + s * perna), (x, y0), (x + w, y0), (x + w, y0 + s * perna) },
                    Bitola = phiU, Aco = aco, Quantidade = qtd, Escala = c.escV,
                    PosicaoDescricao = (x - 2.5 * hv, y0 + s * perna / 2), RotacaoDescricao = Math.PI / 2,
                });
                us.Add((nome, topo, dir, w, phiU, qtd));
                x += w + 8 * hv;
            }
        }
        if (us.Count > 0) x -= 8 * hv;

        // ---------------- estribos e grampos: formas (perto das seções) e faixas na elevação ----------------
        var zonas = new List<(double y1, double y2, double phi, double esp, bool grampos, string nome)>();
        string modoGr = v["Grampos"];
        if (zBase > 0) zonas.Add((yIni, zBase, phiBase, v.Numero("BaseEsp"), modoGr != GramposNenhum, "base"));
        zonas.Add((zBase > 0 ? zBase : yIni, zTopo > 0 ? L - zTopo : yFim, phiEst, v.Numero("EstEsp"), modoGr == GramposTodas, "corrente"));
        if (zTopo > 0) zonas.Add((L - zTopo, yFim, phiTopo, v.Numero("TopoEsp"), modoGr != GramposNenhum, "topo"));

        double xFx = x + 8 * hv;
        double xSec = xFx + 22 * hv;
        var estribosPorBitola = new Dictionary<double, string>();
        int dirsGr = Math.Max(1, Math.Min(2, v.Inteiro("GrampoDirecoes")));
        double phiGr = v.Numero("GrampoBitola");
        bool quadrado = Math.Abs(wEst - hEst) < 1e-6;
        var grampos = new List<string>();
        // formas: abaixo das seções (preenchidas depois de saber a altura das seções)
        var formas = new List<Action<double, double>>();
        foreach (var z in zonas)
        {
            if (estribosPorBitola.ContainsKey(z.phi)) continue;
            string nome = $"estribo{estribosPorBitola.Count}";
            estribosPorBitola[z.phi] = nome;
            double phi = z.phi;
            formas.Add((fx, fy) => r.Barras.Add(Estribo(cfg, nome, fx, fy, wEst, hEst, phi, acoEst, c.escC)));
        }
        if (zonas.Any(z => z.grampos))
        {
            int n = quadrado ? 1 : dirsGr;
            for (int d = 0; d < n; d++)
            {
                string nome = $"grampo{d}";
                double comp = d == 0 ? wEst : hEst;
                grampos.Add(nome);
                formas.Add((fx, fy) => r.Barras.Add(Grampo(cfg, nome, fx, fy, comp, phiGr, acoEst, c.escC)));
            }
        }
        foreach (var z in zonas)
        {
            if (z.y2 - z.y1 < 1e-6) continue;
            r.Faixas.Add(new FaixaTemplate
            {
                Barra = estribosPorBitola[z.phi], Tipo = TipoFaixa.Ortogonal, P1 = (xFx, z.y1), P2 = (xFx, z.y2),
                Posicao = (xFx + 2 * hv, (z.y1 + z.y2) / 2), Espacamento = z.esp, Escala = c.escV, MostrarDiametro = false,
            });
            if (!z.grampos) continue;
            for (int d = 0; d < grampos.Count; d++)
                r.Faixas.Add(new FaixaTemplate
                {
                    Barra = grampos[d], Tipo = TipoFaixa.Ortogonal, P1 = (xFx + (3 + 2 * d) * hv, z.y1), P2 = (xFx + (3 + 2 * d) * hv, z.y2),
                    Posicao = (xFx + (5 + 2 * d) * hv, (z.y1 + z.y2) / 2), Espacamento = z.esp, Escala = c.escV, MostrarDiametro = false,
                    Repeticoes = quadrado ? dirsGr : 1,
                });
        }

        // ---------------- seções 1-1 (topo), 2-2 (corrente), 3-3 (base) ----------------
        var cortes = new List<(string id, double y, string zona)>();
        if (zTopo > 0 || us.Any(u => u.topo)) cortes.Add(("1", L - Math.Max(zTopo, 20) / 2, "topo"));
        cortes.Add((cortes.Count == 0 ? "1" : "2", (zBase + (L - zTopo)) / 2, "corrente"));
        if (zBase > 0 || us.Any(u => !u.topo)) cortes.Add(((cortes.Count + 1).ToString(), Math.Max(zBase, 20) / 2, "base"));

        double yS = L + 10 * hv;
        foreach (var (id, yc, zona) in cortes)
        {
            // marcas na elevação das armaduras
            foreach (double xm in new[] { x0 - 3 * hv, xFx + 9 * hv })
            {
                c.Linha(xm, yc, xm + 2 * hv, yc, "ARM-CORTE");
                c.Texto(id, xm + hv, yc + 0.5 * hv, hv, 1, 0, "ARM-CORTE");
            }
            // título
            double yt = yS;
            foreach (var t in c.Linhas($"SEÇÃO {id}-{id} - ARMADURA", $"SECTION {id}-{id} - REBAR")) { c.Texto(t, xSec, yt, 1.2 * hc); yt -= 2 * hc; }
            c.Texto($"ESC 1:{c.escC:0}", xSec, yt, 0.8 * hc);
            double sy0 = yt - 8 * hc - h, sx0 = xSec + 8 * hc;
            bool grSecao = grampos.Count > 0 && (zonas.FirstOrDefault(z => z.nome == zona).nome != null ? zonas.First(z => z.nome == zona).grampos : modoGr == GramposTodas);
            DesenharSecao(c, sx0, sy0, b, h, ch, cob, phiT, barras, longs, yc, zona, us, grSecao, dirsGr);
            yS = sy0 - 10 * hc;
        }

        // ---------------- formas dos estribos e grampos ----------------
        double fxCur = xSec + 8 * hc, fyCur = yS - 4 * hc;
        foreach (var f in formas)
        {
            f(fxCur, fyCur);
            fxCur += Math.Max(wEst, hEst) + 12 * hc;
        }
        return Math.Max(xSec + b + 30 * hc, fxCur);
    }

    /// <summary>Estribo fechado (medidas externas w × h) com ganchos de 90° no canto superior esquerdo (padrão do escritório).</summary>
    private static BarraTemplate Estribo(ConfiguracaoArmadura cfg, string nome, double x, double y, double w, double h, double phi, string aco, double escala)
    {
        var poligono = new List<(double X, double Y)> { (x, y), (x + w, y), (x + w, y + h), (x, y + h) };
        int canto = GeradorEstribo.Canto(poligono, PosicaoGanchos.SuperiorEsquerdo);
        double lg = cfg.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho90, phi, aco, estribo: true);
        double passagem = cfg.Dobramento.DiametroPino(phi, aco, estribo: true) + 2 * phi / 10;
        var f = GeradorEstribo.FechadoPoligono(poligono, canto, FormatoSegmento.Gancho90, lg, passagem);
        var bt = new BarraTemplate { Nome = nome, Bitola = phi, Aco = aco, Estribo = true, Escala = escala, Quantidade = 1 };
        bt.Vertices.AddRange(f.Vertices.Select(p => (p.X, p.Y)));
        bt.Formatos.AddRange(f.Formatos);
        foreach (var s in f.Sobrepostos) bt.Sobrepostos.Add(s);
        foreach (var s in f.Cotados) bt.Cotados.Add(s);
        return bt;
    }

    /// <summary>Grampo suplementar: reto (medida externa do estribo) com ganchos de 135° (45°) para dentro.</summary>
    private static BarraTemplate Grampo(ConfiguracaoArmadura cfg, string nome, double x, double y, double comp, double phi, string aco, double escala)
    {
        double lg = cfg.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho45, phi, aco, estribo: true);
        double d = lg * Math.Sqrt(0.5);
        var bt = new BarraTemplate { Nome = nome, Bitola = phi, Aco = aco, Estribo = true, Escala = escala, Quantidade = 1 };
        bt.Vertices.AddRange(new[] { (x + d, y - d), (x, y), (x + comp, y), (x + comp - d, y - d) });
        bt.Formatos.AddRange(new[] { FormatoSegmento.Gancho45, FormatoSegmento.Linha, FormatoSegmento.Gancho45 });
        return bt;
    }

    /// <summary>Seção de armadura: forma, estribo, grampos, barras em ponto (diâmetro real) e indicações N.</summary>
    private static void DesenharSecao(Ctx c, double sx0, double sy0, double b, double h, double ch, double[] cob, double phiT,
        List<BarraNaSecao> barras, List<Longitudinal> longs, double yCorte, string zona,
        List<(string nome, bool topo, int dir, double w, double phi, int qtd)> us, bool comGrampos, int dirsGr)
    {
        var r = c.R;
        double hc = c.hC;
        // forma
        var forma = new ContornoTemplate { Fechado = true, Layer = "ARM-FORMA" };
        if (ch > 0)
            forma.Vertices.AddRange(new[] { (sx0 + ch, sy0), (sx0 + b - ch, sy0), (sx0 + b, sy0 + ch), (sx0 + b, sy0 + h - ch), (sx0 + b - ch, sy0 + h), (sx0 + ch, sy0 + h), (sx0, sy0 + h - ch), (sx0, sy0 + ch) });
        else forma.Vertices.AddRange(new[] { (sx0, sy0), (sx0 + b, sy0), (sx0 + b, sy0 + h), (sx0, sy0 + h) });
        r.Contornos.Add(forma);
        // estribo (linha de centro)
        double t = phiT / 20;
        double ex0 = sx0 + cob[2] + t, ex1 = sx0 + b - cob[0] - t, ey0 = sy0 + cob[1] + t, ey1 = sy0 + h - cob[3] - t;
        r.Contornos.Add(new ContornoTemplate { Vertices = { (ex0, ey0), (ex1, ey0), (ex1, ey1), (ex0, ey1) }, Fechado = true, Layer = "ARM-ESTRIBO-SECAO" });
        if (comGrampos)
        {
            c.Linha(ex0, sy0 + h / 2, ex1, sy0 + h / 2, "ARM-ESTRIBO-SECAO");
            if (dirsGr > 1) c.Linha(sx0 + b / 2, ey0, sx0 + b / 2, ey1, "ARM-ESTRIBO-SECAO");
        }
        // barras em ponto; cada longitudinal ganha a sua indicação (como no P61), apontando a peça que passa no corte
        double xm = sx0 + b / 2;
        // rótulos laterais: (y desejado, pontos, barra, lado esquerdo?)
        var laterais = new List<(double Y, List<(double X, double Y)> pts, string barra, bool esq)>();
        foreach (var grupo in barras.GroupBy(x => x.Grupo))
        {
            int i = 0;
            foreach (var bs in grupo)
            {
                double px = sx0 + bs.X, py = sy0 + bs.Y;
                r.Circulos.Add(new CirculoTemplate { X = px, Y = py, Raio = bs.Bitola / 20, Layer = "ARM-BARRA-SECAO" });
                // na alternância, metade das barras do grupo é a "B"
                var candidatas = longs.Where(l => l.Grupo.Chave == grupo.Key && l.Peca.Ini <= yCorte + 1e-6 && l.Peca.Fim >= yCorte - 1e-6).ToList();
                var peca = candidatas.FirstOrDefault(l => l.B == (i % 2 == 1)) ?? candidatas.FirstOrDefault();
                if (peca != null) laterais.Add((py, new List<(double, double)> { (px, py) }, peca.Nome, px < xm));
                i++;
            }
        }
        // grampos U: pernas em ponto e o trecho reto em vista; UMA indicação por conjunto (com uma linha para cada U)
        foreach (var u in us.Where(u => (zona == "topo" && u.topo) || (zona == "base" && !u.topo)))
        {
            double ru = u.phi / 20;
            var pernas = new List<(double X, double Y)>();
            for (int k = 1; k <= u.qtd; k++)
            {
                double desl = (k - (u.qtd + 1) / 2.0) * 8 * ru;
                (double X, double Y) pa, pb;
                if (u.dir == 0) { double y = sy0 + h / 2 + desl; pa = (xm - u.w / 2, y); pb = (xm + u.w / 2, y); }
                else { double xx = xm + desl; pa = (xx, sy0 + h / 2 + u.w / 2); pb = (xx, sy0 + h / 2 - u.w / 2); }
                c.Linha(pa.X, pa.Y, pb.X, pb.Y, "ARM-ESTRIBO-SECAO");
                r.Circulos.Add(new CirculoTemplate { X = pa.X, Y = pa.Y, Raio = ru, Layer = "ARM-BARRA-SECAO" });
                r.Circulos.Add(new CirculoTemplate { X = pb.X, Y = pb.Y, Raio = ru, Layer = "ARM-BARRA-SECAO" });
                pernas.Add(pa);
            }
            if (u.dir == 0) laterais.Add((pernas.Average(p => p.Y), pernas, u.nome, true));
            else
                r.Faixas.Add(new FaixaTemplate
                {
                    Barra = u.nome, Tipo = TipoFaixa.Indicacao, Referencias = pernas, Posicao = (xm, sy0 + h + 3 * hc), Escala = c.escC,
                });
        }
        // laterais: cada texto na altura da sua barra, afastado só o necessário para não encostar no vizinho
        double gap = 1.5 * hc;
        foreach (var lado in laterais.GroupBy(l => l.esq))
        {
            var lista = lado.OrderByDescending(l => l.Y).ToList();
            var ys = lista.Select(l => l.Y).ToArray();
            for (int k = 1; k < ys.Length; k++) ys[k] = Math.Min(ys[k], ys[k - 1] - gap);      // de cima para baixo
            for (int k = ys.Length - 2; k >= 0; k--) ys[k] = Math.Max(ys[k], ys[k + 1] + gap); // e de volta
            // recentra o conjunto na seção (o deslocamento médio vai a zero)
            double ajuste = lista.Select((l, k) => l.Y - ys[k]).Average();
            double tx = lado.Key ? sx0 - 4 * hc : sx0 + b + 4 * hc;
            for (int k = 0; k < lista.Count; k++)
                r.Faixas.Add(new FaixaTemplate
                {
                    Barra = lista[k].barra, Tipo = TipoFaixa.Indicacao, Referencias = lista[k].pts, Posicao = (tx, ys[k] + ajuste), Escala = c.escC,
                });
        }
    }

    /// <summary>Verificações de detalhamento da NBR 6118 (avisos, não bloqueiam).</summary>
    private static void VerificarSecao(ResultadoTemplate r, double b, double h, List<BarraNaSecao> barras, double phiT, ValoresTemplate v)
    {
        double menor = Math.Min(b, h);
        foreach (var phi in barras.Select(x => x.Bitola).Distinct())
        {
            if (phi < 10) r.Avisos.Add($"Longitudinal ø{Num(phi, "0.0")} < 10 mm (NBR 6118, 18.4.2.1).");
            if (phi > menor * 10 / 8) r.Avisos.Add($"Longitudinal ø{Num(phi, "0.0")} > 1/8 da menor dimensão (NBR 6118, 18.4.2.1).");
        }
        double phiMax = barras.Max(x => x.Bitola);
        foreach (var k in new[] { "EstBitola", "TopoBitola", "BaseBitola" })
        {
            double pt = v.Numero(k);
            if (pt < 5 || pt < phiMax / 4) r.Avisos.Add($"Estribo ø{Num(pt, "0.0")}: menor que 5 mm ou φ/4 = {Num(phiMax / 4, "0.0#")} mm (NBR 6118, 18.4.3).");
        }
        double phiMin = barras.Min(x => x.Bitola);
        double sMax = Math.Min(20, Math.Min(menor, 12 * phiMin / 10));
        if (v.Numero("EstEsp") > sMax + 1e-6)
            r.Avisos.Add($"Espaçamento dos estribos {Num(v.Numero("EstEsp"), "0.#")} cm > {Num(sMax, "0.#")} cm (mín. de 20 cm, menor dimensão e 12φ; NBR 6118, 18.4.3).");
        // espaçamento livre entre barras vizinhas
        double livreMin = Math.Max(2, Math.Max(phiMax / 10, 1.2 * v.Numero("DiamAgregado") / 10));
        double pior = double.MaxValue, maiorEixo = 0;
        foreach (var a in barras)
        {
            var viz = barras.Where(x => !(x.X == a.X && x.Y == a.Y))
                .Select(x => (d: Math.Sqrt((x.X - a.X) * (x.X - a.X) + (x.Y - a.Y) * (x.Y - a.Y)), x)).OrderBy(p => p.d).First();
            pior = Math.Min(pior, viz.d - (a.Bitola + viz.x.Bitola) / 20);
            maiorEixo = Math.Max(maiorEixo, viz.d);
        }
        if (pior < livreMin - 1e-6)
            r.Avisos.Add($"Espaçamento livre entre barras {Num(pior, "0.0#")} cm < {Num(livreMin, "0.0#")} cm (NBR 6118, 18.4.2.2).");
        if (maiorEixo > Math.Min(2 * menor, 40) + 1e-6)
            r.Avisos.Add($"Espaçamento entre eixos {Num(maiorEixo, "0.#")} cm > {Num(Math.Min(2 * menor, 40), "0.#")} cm (NBR 6118, 18.4.2.2).");
        // proteção contra flambagem: barras a mais de 20 φt de um canto pedem grampo
        var cantos = barras.Where(x => x.Grupo == "canto").ToList();
        bool longe = barras.Where(x => x.Grupo != "canto").Any(x => cantos.Min(cn => Math.Max(Math.Abs(cn.X - x.X), Math.Abs(cn.Y - x.Y))) > 20 * phiT / 10 + 1e-6);
        if (longe && v["Grampos"] == GramposNenhum)
            r.Avisos.Add("Há barras a mais de 20φt do canto sem grampos suplementares (NBR 6118, 18.2.4).");
    }
}
