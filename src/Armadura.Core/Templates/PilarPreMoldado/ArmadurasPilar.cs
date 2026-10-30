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
    public BarraNaSecao(double x, double y, double bitola, string grupo, int feixe = 1, string arranjo = "")
    {
        X = x; Y = y; Bitola = bitola; Grupo = grupo; Feixe = feixe; Arranjo = arranjo;
    }
    /// <summary>Centro da barra (ou do feixe).</summary>
    public double X { get; }
    public double Y { get; }
    public double Bitola { get; }
    /// <summary>Linha da lista ("L1"…); em <see cref="TemplatePilarPreMoldado.PosicoesBarras"/>: "canto", "vizinha", "faceB" ou "faceH".</summary>
    public string Grupo { get; }
    public int Feixe { get; }
    public string Arranjo { get; }
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
/// Etapa 2 do pilar (2026-10-29): armaduras — longitudinais (lista: cantos, "L", faces, feixes, redução com a altura), emendas
/// alternadas ou na mesma seção (ou barras em rolo), estribos por zona (lista + trecho corrente) com grampos, grampos U
/// no topo e na base, elevação das armaduras com as faixas, seções onde a composição muda, com as barras em ponto e as
/// indicações N (numeração automática), formas dos estribos e grampos, e a relação do aço do detalhamento.
/// </summary>
public sealed partial class TemplatePilarPreMoldado
{
    public const string SemEmendas = "Sem emendas (barras em rolo)", ComEmendas = "Com emendas";
    public const string Alternadas = "Alternadas", MesmaSecao = "Na mesma seção";

    private const string G7 = "07. Armadura longitudinal", G8 = "08. Emendas", G9 = "09. Estribos", G10 = "10. Grampos suplementares",
        G11 = "11. Grampos U (topo e base)";

    private static IEnumerable<ParametroTemplate> ParametrosArmadura() => new[]
    {
        P(G7, "LongAco", "Aço", "CA-50", tipo: TipoParametro.Opcao, opcoes: new[] { "CA-50", "CA-25" }),
        ParametroLongitudinais(),
        P(G7, "CantoEsp", "Canto em \"L\": distância entre eixos", "5", "cm", min: 2),
        P(G7, "DiamAgregado", "Diâmetro máximo do agregado", "19", "mm", min: 4),

        P(G8, "Emendas", "Emendas das longitudinais", ComEmendas, tipo: TipoParametro.Opcao, opcoes: new[] { ComEmendas, SemEmendas },
          descricao: "Com emendas: barras divididas no comprimento comercial, traspasse pela NBR 6118 e nunca menor que 45φ, " +
                     "com nota ao lado. Sem emendas: barras inteiras (em rolo)."),
        P(G8, "EmendaArranjo", "Emendas", Alternadas, tipo: TipoParametro.Opcao, opcoes: new[] { Alternadas, MesmaSecao }),
        P(G8, "Comercial", "Comprimento comercial das barras", "1200", "cm", min: 100),

        P(G9, "EstAco", "Aço", "CA-50", tipo: TipoParametro.Opcao, opcoes: new[] { "CA-50", "CA-60" }),
        P(G9, "EstBitola", "Trecho corrente: bitola", "6.3", "mm", TipoParametro.Bitola, 5, 16),
        P(G9, "EstEsp", "Trecho corrente: espaçamento", "15", "cm", min: 2),
        ParametroZonas(),

        P(G10, "GramposCorrente", "Grampos no trecho corrente", "Não", tipo: TipoParametro.SimNao,
          descricao: "Nas zonas da lista, os grampos são escolhidos em cada zona."),
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
        public string Chave;            // linha da lista ("L1", "L2"…)
        public double Bitola;
        public LinhaLongitudinal Linha;
        public List<BarraNaSecao> Barras = new List<BarraNaSecao>();
    }

    /// <summary>Uma longitudinal desenhada na elevação (uma peça de uma linha, A ou B).</summary>
    private sealed class Longitudinal
    {
        public string Nome;
        public Grupo Grupo;
        public bool B;                  // grupo alternado
        public PecaBarra Peca;
        public int Quantidade;
    }

    /// <summary>Grampo U do topo ou da base (uma direção).</summary>
    private sealed class GrampoU
    {
        public string Nome;
        public bool Topo;
        public int Dir;
        public double W, Phi, Y1, Y2;
        public int Qtd;
    }

    private static void Aviso(ResultadoTemplate r, string s) { if (!r.Avisos.Contains(s)) r.Avisos.Add(s); }

    /// <summary>Desenha as armaduras a partir de <paramref name="x0"/>; devolve o x onde elas terminam.</summary>
    private double GerarArmaduras(Ctx c, ValoresTemplate v, ConfiguracaoArmadura cfg, double x0, double b, double h, double L, double ch)
    {
        var r = c.R;
        double hv = c.hV, hc = c.hC;
        var cob = Cobrimentos(v);
        string aco = v["LongAco"], acoEst = v["EstAco"];
        double sCanto = v.Numero("CantoEsp");
        double yIni = cob[5], yFim = L - cob[4];

        var linhas = LerLongitudinais(v, yIni, yFim, r.Avisos);
        if (linhas.Count == 0) { r.Avisos.Add("Sem armadura longitudinal: inclua as barras na lista \"Barras longitudinais\"."); return x0; }
        var zonas = LerZonas(v, yIni, yFim, L, r.Avisos);
        double phiT = zonas.Max(z => z.Bitola);
        var posicoes = PosicoesLinhas(b, h, cob, phiT, linhas, sCanto);
        var grupos = linhas.Select(l => new Grupo { Chave = l.Chave, Bitola = l.Bitola, Linha = l, Barras = posicoes[l.Linha] }).ToList();
        bool emL = linhas.Any(l => l.Arranjo == ArrAoLado);
        double phiCanto = linhas.Where(l => l.Arranjo == ArrCantos).Select(l => l.Bitola).DefaultIfEmpty(10).Max();

        // ---------------- longitudinais: peças (emendas pelo comprimento comercial) ----------------
        bool emendas = v["Emendas"] == ComEmendas, mesma = v["EmendaArranjo"] == MesmaSecao;
        double comercialPar = v.Numero("Comercial") > 0 ? v.Numero("Comercial") : 1200;
        var longs = new List<Longitudinal>();
        foreach (var g in grupos)
        {
            double yA = g.Linha.Ini, yB = g.Linha.Fim;
            int nPos = g.Barras.Count, fx = g.Linha.Feixe;
            var info = cfg.Barras.Obter(g.Bitola, aco);
            double comercial = info != null && info.ComprimentoComercial > 0 ? Math.Min(info.ComprimentoComercial, comercialPar) : comercialPar;
            if (!emendas || yB - yA <= comercial)
            {
                if (!emendas && yB - yA > comercial && (info == null || info.Fabricacao != Fabricacao.Rolo))
                    Aviso(r, $"ø{Num(g.Bitola, "0.0")} sem emendas ({Num(yB - yA, "0.#")} cm): na tabela de bitolas ela é vendida em barras de " +
                             $"{Num(comercial, "0")} cm — confira a disponibilidade em rolo.");
                longs.Add(new Longitudinal { Grupo = g, Peca = new PecaBarra(yA, yB), Quantidade = nPos * fx });
                continue;
            }
            double lap = TraspassePilar(g.Bitola, aco, v.Numero("Fck"), mesma, cfg.Barras);
            int nA = mesma ? nPos : (nPos + 1) / 2, nB = nPos - nA;
            foreach (var p in Pecas(yA, yB, comercial, lap, comercial))
                longs.Add(new Longitudinal { Grupo = g, Peca = p, Quantidade = nA * fx });
            if (nB > 0)
                foreach (var p in Pecas(yA, yB, comercial, lap, PrimeiraAlternada(comercial, lap)))
                    longs.Add(new Longitudinal { Grupo = g, B = true, Peca = p, Quantidade = nB * fx });
        }

        // ---------------- elevação: uma coluna por arranjo (e lado A/B); as linhas do arranjo uma sobre a outra ----------------
        double x = x0;
        int seq = 0;
        var ordemArr = linhas.Select(l => l.Arranjo).Distinct().ToList();
        foreach (var coluna in longs.GroupBy(l => (l.Grupo.Linha.Arranjo, l.B)).OrderBy(gc => ordemArr.IndexOf(gc.Key.Arranjo)).ThenBy(gc => gc.Key.B))
        {
            var pecas = coluna.OrderBy(l => l.Peca.Ini).ThenBy(l => l.Grupo.Linha.Linha).ToList();
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
                if (k == 0) continue;
                var ant = pecas[k - 1];
                double a = l.Peca.Ini, bFim = ant.Peca.Fim;
                if (bFim - a < 1e-6) continue; // interrupção sem emenda (redução com a altura)
                // emenda entre duas linhas da lista (redução): o traspasse digitado tem de cobrir o da norma
                if (ant.Grupo != l.Grupo)
                {
                    double req = TraspassePilar(Math.Max(ant.Grupo.Bitola, l.Grupo.Bitola), aco, v.Numero("Fck"), true, cfg.Barras);
                    if (bFim - a < req - 1e-6)
                        Aviso(r, $"Emenda das longitudinais {ant.Grupo.Linha.Linha} e {l.Grupo.Linha.Linha} ({l.Grupo.Linha.Arranjo}): traspasse " +
                                 $"{Num(bFim - a, "0.#")} cm < {Num(req, "0")} cm (NBR 6118 na mesma seção, mín. 45ø).");
                }
                // emenda: cota do traspasse e a nota para o projetista
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
            x += 12 * hv;
        }

        // ---------------- grampos U (elevação) ----------------
        double wEst = b - cob[0] - cob[2], hEst = h - cob[1] - cob[3]; // medidas externas do estribo
        var us = new List<GrampoU>();
        foreach (var topo in new[] { true, false })
        {
            string k = topo ? "UTopo" : "UBase";
            if (!v.SimNao(k)) continue;
            double phiU = v.Numero(k + "Bitola"), perna = v.Numero(k + "Perna");
            int qtd = Math.Max(1, v.Inteiro(k + "Qtd"));
            // direção x: por dentro do estribo; direção y: dentro da primeira, afastada do canto
            double w1 = Math.Floor(wEst - 2 * phiT / 10 - phiU / 10);
            double recuo = emL ? sCanto : (phiCanto + phiU) / 10;
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
                us.Add(new GrampoU { Nome = nome, Topo = topo, Dir = dir, W = w, Phi = phiU, Qtd = qtd,
                                     Y1 = topo ? yFim - perna : yIni, Y2 = topo ? yFim : yIni + perna });
                x += w + 8 * hv;
            }
        }
        if (us.Count > 0) x -= 8 * hv;

        // ---------------- estribos e grampos: formas (perto das seções) e faixas na elevação ----------------
        double xFx = x + 8 * hv;
        double xSec = xFx + 22 * hv;
        c.XFimFaixas = xFx + 12 * hv; // depois das faixas e das marcas dos cortes
        var estribosPorBitola = new Dictionary<double, string>();
        int dirsGr = Math.Max(1, Math.Min(2, v.Inteiro("GrampoDirecoes")));
        double phiGr = v.Numero("GrampoBitola");
        bool quadrado = Math.Abs(wEst - hEst) < 1e-6;
        var grampos = new List<string>();
        // formas: abaixo das seções (preenchidas depois de saber a altura das seções)
        var formas = new List<Action<double, double>>();
        foreach (var z in zonas)
        {
            if (estribosPorBitola.ContainsKey(z.Bitola)) continue;
            string nome = $"estribo{estribosPorBitola.Count}";
            estribosPorBitola[z.Bitola] = nome;
            double phi = z.Bitola;
            formas.Add((fx, fy) => r.Barras.Add(Estribo(cfg, nome, fx, fy, wEst, hEst, phi, acoEst, c.escC)));
        }
        if (zonas.Any(z => z.Grampos))
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
            if (z.Y2 - z.Y1 < 1e-6) continue;
            r.Faixas.Add(new FaixaTemplate
            {
                Barra = estribosPorBitola[z.Bitola], Tipo = TipoFaixa.Ortogonal, P1 = (xFx, z.Y1), P2 = (xFx, z.Y2),
                Posicao = (xFx + 2 * hv, (z.Y1 + z.Y2) / 2), Espacamento = z.Esp, Escala = c.escV, MostrarDiametro = false,
            });
            if (!z.Grampos) continue;
            for (int d = 0; d < grampos.Count; d++)
                r.Faixas.Add(new FaixaTemplate
                {
                    Barra = grampos[d], Tipo = TipoFaixa.Ortogonal, P1 = (xFx + (3 + 2 * d) * hv, z.Y1), P2 = (xFx + (3 + 2 * d) * hv, z.Y2),
                    Posicao = (xFx + (5 + 2 * d) * hv, (z.Y1 + z.Y2) / 2), Espacamento = z.Esp, Escala = c.escV, MostrarDiametro = false,
                    MostrarComprimento = false, // o comprimento já está na faixa do estribo, ao lado
                    Repeticoes = quadrado ? dirsGr : 1,
                });
        }

        // ---------------- seções: uma por composição diferente (linhas ativas, estribo, grampos, U), de cima para baixo ----------------
        var cortes = Cortes(linhas, zonas, us, yIni, yFim);
        VerificarArmadura(r, b, h, linhas, zonas);

        double yS = L + 10 * hv;
        foreach (var corte in cortes)
        {
            // marcas na elevação das armaduras (todos os trechos com esta composição)
            foreach (double yc in corte.Marcas)
                foreach (double xm in new[] { x0 - 3 * hv, xFx + 9 * hv })
                {
                    c.Linha(xm, yc, xm + 2 * hv, yc, "ARM-CORTE");
                    c.Texto(corte.Id, xm + hv, yc + 0.5 * hv, hv, 1, 0, "ARM-CORTE");
                }
            string id = corte.Id;
            // título
            double yt = yS;
            foreach (var t in c.Linhas($"SEÇÃO {id}-{id} - ARMADURA", $"SECTION {id}-{id} - REBAR")) { c.Texto(t, xSec, yt, 1.2 * hc); yt -= 2 * hc; }
            c.Texto($"ESC 1:{c.escC:0}", xSec, yt, 0.8 * hc);
            double sy0 = yt - 8 * hc - h, sx0 = xSec + 8 * hc;
            var ativas = linhas.Where(l => l.Ini <= corte.Y + 1e-6 && l.Fim >= corte.Y - 1e-6).ToList();
            var barras = ativas.SelectMany(l => posicoes[l.Linha]).ToList();
            var zona = zonas.First(z => z.Y1 <= corte.Y + 1e-6 && z.Y2 >= corte.Y - 1e-6);
            var usAtivos = us.Where(u => u.Y1 <= corte.Y + 1e-6 && u.Y2 >= corte.Y - 1e-6).ToList();
            VerificarSecao(r, b, h, barras, phiT, zona.Grampos, v.Numero("DiamAgregado"));
            DesenharSecao(c, sx0, sy0, b, h, ch, cob, phiT, barras, longs, corte.Y, usAtivos, zona.Grampos && grampos.Count > 0, dirsGr);
            yS = sy0 - 10 * hc;
        }

        // ---------------- formas dos estribos e grampos ----------------
        double fxCur = xSec + 8 * hc, fyCur = yS - 4 * hc, fxFim = fxCur;
        foreach (var f in formas)
        {
            f(fxCur, fyCur);
            fxFim = fxCur + Math.Max(wEst, hEst) + 4 * hc; // sem o espaço que sobra depois da última forma
            fxCur += Math.Max(wEst, hEst) + 12 * hc;
        }
        return Math.Max(xSec + b + 20 * hc, fxFim); // seção + etiquetas da direita
    }

    /// <summary>Seção a desenhar: número, altura do corte (o trecho mais longo) e as alturas de todos os trechos iguais.</summary>
    public sealed class CortePilar
    {
        public string Id { get; set; } = "";
        public double Y { get; set; }
        public List<double> Marcas { get; set; } = new List<double>();
        public string Assinatura { get; set; } = "";
    }

    /// <summary>
    /// Seções da armadura: o pilar é dividido onde muda a composição (linhas ativas, estribo e grampos da zona, grampos U); trechos
    /// de emenda entre linhas do mesmo arranjo e trechos com menos de 10 cm não ganham seção. Composições iguais repetem o número.
    /// Os grampos U não dividem trechos: entram na seção que os corta (e a distinguem de outra igual sem eles).
    /// </summary>
    private static List<CortePilar> Cortes(List<LinhaLongitudinal> linhas, List<ZonaEstribo> zonas, List<GrampoU> us, double yIni, double yFim)
    {
        var ys = new List<double> { yIni, yFim };
        ys.AddRange(linhas.SelectMany(l => new[] { l.Ini, l.Fim }));
        ys.AddRange(zonas.SelectMany(z => new[] { z.Y1, z.Y2 }));
        ys.AddRange(us.SelectMany(u => new[] { u.Y1, u.Y2 }));
        ys = ys.Where(y => y >= yIni - 1e-6 && y <= yFim + 1e-6).OrderBy(y => y).ToList();
        string Assinatura(double y)
        {
            var at = linhas.Where(l => l.Ini <= y && l.Fim >= y).ToList();
            if (at.GroupBy(l => l.Arranjo).Any(g => g.Count() > 1)) return null; // emenda entre linhas
            var z = zonas.FirstOrDefault(q => q.Y1 <= y && q.Y2 >= y);
            return string.Join(",", at.Select(l => l.Chave)) + "|" + (z == null ? "" : $"{z.Bitola}/{z.Grampos}");
        }
        // trechos com a mesma composição, de baixo para cima
        var trechos = new List<(double a, double b, string sig)>();
        for (int i = 0; i + 1 < ys.Count; i++)
        {
            double a = ys[i], bb = ys[i + 1];
            if (bb - a < 1e-6) continue;
            string sig = Assinatura((a + bb) / 2);
            if (trechos.Count > 0 && sig != null && trechos[trechos.Count - 1].sig == sig && Math.Abs(trechos[trechos.Count - 1].b - a) < 1e-6)
                trechos[trechos.Count - 1] = (trechos[trechos.Count - 1].a, bb, sig);
            else trechos.Add((a, bb, sig));
        }
        var validos = trechos.Where(t => t.sig != null && t.b - t.a >= 10).ToList();
        if (validos.Count == 0) validos = trechos.Where(t => t.sig != null).OrderByDescending(t => t.b - t.a).Take(1).ToList();
        // os U que o meio do trecho corta distinguem seções iguais (topo × base), sem criar trechos novos
        validos = validos.Select(t => (t.a, t.b, sig: t.sig + "|" +
            string.Join(",", us.Where(u => u.Y1 <= (t.a + t.b) / 2 && u.Y2 >= (t.a + t.b) / 2).Select(u => u.Nome)))).ToList();
        var r = new List<CortePilar>();
        foreach (var t in validos.OrderByDescending(t => t.b)) // de cima para baixo: 1-1 no topo
        {
            var corte = r.FirstOrDefault(q => q.Assinatura == t.sig);
            if (corte == null)
            {
                corte = new CortePilar { Id = (r.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Assinatura = t.sig, Y = (t.a + t.b) / 2 };
                r.Add(corte);
            }
            else if (t.b - t.a > 0)
            {
                // o corte fica no trecho mais longo da composição
                var maior = validos.Where(q => q.sig == t.sig).OrderByDescending(q => q.b - q.a).First();
                corte.Y = (maior.a + maior.b) / 2;
            }
            corte.Marcas.Add((t.a + t.b) / 2);
        }
        // grampos U: aparecem na seção cujo corte os atravessa; se nenhum corte pega um U, o corte do trecho que mais o cobre vai para ele
        foreach (var u in us)
        {
            if (r.Any(q => q.Y >= u.Y1 - 1e-6 && q.Y <= u.Y2 + 1e-6)) continue;
            var melhor = validos.Select(t => (t, a: Math.Max(t.a, u.Y1), b: Math.Min(t.b, u.Y2))).Where(q => q.b - q.a > 1e-6)
                .OrderByDescending(q => q.b - q.a).FirstOrDefault();
            if (melhor.t.sig == null) continue;
            var corte = r.First(q => q.Assinatura == melhor.t.sig);
            double yNovo = (melhor.a + melhor.b) / 2;
            corte.Marcas[corte.Marcas.IndexOf(corte.Marcas.OrderBy(m => Math.Abs(m - corte.Y)).First())] = yNovo;
            corte.Y = yNovo;
        }
        return r;
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

    /// <summary>Seção de armadura: forma, estribo, grampos, barras em ponto (diâmetro real, feixes) e indicações N.</summary>
    private static void DesenharSecao(Ctx c, double sx0, double sy0, double b, double h, double ch, double[] cob, double phiT,
        List<BarraNaSecao> barras, List<Longitudinal> longs, double yCorte, List<GrampoU> us, bool comGrampos, int dirsGr)
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
                foreach (var (dx, dy) in CentrosFeixe(bs.Bitola, bs.Feixe))
                    r.Circulos.Add(new CirculoTemplate { X = px + dx, Y = py + dy, Raio = bs.Bitola / 20, Layer = "ARM-BARRA-SECAO" });
                // na alternância, metade das barras da linha é a "B"
                var candidatas = longs.Where(l => l.Grupo.Chave == grupo.Key && l.Peca.Ini <= yCorte + 1e-6 && l.Peca.Fim >= yCorte - 1e-6).ToList();
                var peca = candidatas.FirstOrDefault(l => l.B == (i % 2 == 1)) ?? candidatas.FirstOrDefault();
                if (peca != null) laterais.Add((py, new List<(double, double)> { (px, py) }, peca.Nome, px < xm));
                i++;
            }
        }
        // grampos U: pernas em ponto e o trecho reto em vista; UMA indicação por conjunto (com uma linha para cada U)
        foreach (var u in us)
        {
            double ru = u.Phi / 20;
            var pernas = new List<(double X, double Y)>();
            for (int k = 1; k <= u.Qtd; k++)
            {
                double desl = (k - (u.Qtd + 1) / 2.0) * 8 * ru;
                (double X, double Y) pa, pb;
                if (u.Dir == 0) { double y = sy0 + h / 2 + desl; pa = (xm - u.W / 2, y); pb = (xm + u.W / 2, y); }
                else { double xx = xm + desl; pa = (xx, sy0 + h / 2 + u.W / 2); pb = (xx, sy0 + h / 2 - u.W / 2); }
                c.Linha(pa.X, pa.Y, pb.X, pb.Y, "ARM-ESTRIBO-SECAO");
                r.Circulos.Add(new CirculoTemplate { X = pa.X, Y = pa.Y, Raio = ru, Layer = "ARM-BARRA-SECAO" });
                r.Circulos.Add(new CirculoTemplate { X = pb.X, Y = pb.Y, Raio = ru, Layer = "ARM-BARRA-SECAO" });
                pernas.Add(pa);
            }
            if (u.Dir == 0) laterais.Add((pernas.Average(p => p.Y), pernas, u.Nome, true));
            else
                r.Faixas.Add(new FaixaTemplate
                {
                    Barra = u.Nome, Tipo = TipoFaixa.Indicacao, Referencias = pernas, Posicao = (xm, sy0 + h + 3 * hc), Escala = c.escC,
                });
        }
        // barras de mesmo N do mesmo lado: UMA etiqueta com uma linha para cada barra (checagem de 2026-10-30)
        laterais = laterais.GroupBy(l => (l.barra, l.esq))
            .Select(g => (Y: g.Average(l => l.Y), pts: g.SelectMany(l => l.pts).ToList(), barra: g.Key.barra, esq: g.Key.esq)).ToList();
        // laterais: cada texto na altura da sua barra, afastado só o necessário para não encostar no vizinho
        double gap = 1.9 * hc; // com as etiquetas agrupadas, as do meio ficam juntas: um pouco mais de folga
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

    /// <summary>Verificações da NBR 6118 que valem para o pilar todo (bitolas e espaçamento dos estribos; avisos, não bloqueiam).</summary>
    private static void VerificarArmadura(ResultadoTemplate r, double b, double h, List<LinhaLongitudinal> linhas, List<ZonaEstribo> zonas)
    {
        double menor = Math.Min(b, h);
        foreach (var phi in linhas.Select(x => x.Bitola).Distinct())
        {
            if (phi < 10) Aviso(r, $"Longitudinal ø{Num(phi, "0.0")} < 10 mm (NBR 6118, 18.4.2.1).");
            if (phi > menor * 10 / 8) Aviso(r, $"Longitudinal ø{Num(phi, "0.0")} > 1/8 da menor dimensão (NBR 6118, 18.4.2.1).");
        }
        double phiMax = linhas.Max(x => x.Bitola), phiMin = linhas.Min(x => x.Bitola);
        foreach (var pt in zonas.Select(z => z.Bitola).Distinct())
            if (pt < 5 || pt < phiMax / 4) Aviso(r, $"Estribo ø{Num(pt, "0.0")}: menor que 5 mm ou φ/4 = {Num(phiMax / 4, "0.0#")} mm (NBR 6118, 18.4.3).");
        double sMax = Math.Min(20, Math.Min(menor, 12 * phiMin / 10));
        foreach (var z in zonas.Where(z => z.Esp > sMax + 1e-6).GroupBy(z => z.Esp))
            Aviso(r, $"Espaçamento dos estribos {Num(z.Key, "0.#")} cm > {Num(sMax, "0.#")} cm (mín. de 20 cm, menor dimensão e 12φ; NBR 6118, 18.4.3).");
        // pelo menos uma barra em cada vértice, no pilar todo
        double yIni = zonas.Min(z => z.Y1), yFim = zonas.Max(z => z.Y2), y = yIni;
        foreach (var l in linhas.Where(l => l.Arranjo == ArrCantos).OrderBy(l => l.Ini))
            if (l.Ini <= y + 1e-6) y = Math.Max(y, l.Fim);
        if (y < yFim - 1e-6)
            Aviso(r, $"Sem barras de canto de {Num(y, "0.#")} a {Num(yFim, "0.#")} cm: é preciso uma barra em cada vértice (NBR 6118, 18.4.2.1).");
    }

    /// <summary>Verificações de cada seção: espaçamento entre barras e grampos para barras longe do canto (NBR 6118).</summary>
    private static void VerificarSecao(ResultadoTemplate r, double b, double h, List<BarraNaSecao> barras, double phiT, bool grampos, double diamAg)
    {
        if (barras.Count < 2) return;
        double menor = Math.Min(b, h);
        // feixe: diâmetro equivalente φ√n (NBR 6118, 17.3.5.2.3)
        double Eq(BarraNaSecao x) => x.Bitola * Math.Sqrt(x.Feixe);
        double phiMax = barras.Max(Eq);
        double livreMin = Math.Max(2, Math.Max(phiMax / 10, 1.2 * diamAg / 10));
        double pior = double.MaxValue, maiorEixo = 0;
        foreach (var a in barras)
        {
            var viz = barras.Where(x => !(x.X == a.X && x.Y == a.Y))
                .Select(x => (d: Math.Sqrt((x.X - a.X) * (x.X - a.X) + (x.Y - a.Y) * (x.Y - a.Y)), x)).OrderBy(p => p.d).First();
            pior = Math.Min(pior, viz.d - RaioFeixe(a.Bitola, a.Feixe) - RaioFeixe(viz.x.Bitola, viz.x.Feixe));
            maiorEixo = Math.Max(maiorEixo, viz.d);
        }
        if (pior < livreMin - 1e-6)
            Aviso(r, $"Espaçamento livre entre barras {Num(pior, "0.0#")} cm < {Num(livreMin, "0.0#")} cm (NBR 6118, 18.4.2.2).");
        if (maiorEixo > Math.Min(2 * menor, 40) + 1e-6)
            Aviso(r, $"Espaçamento entre eixos {Num(maiorEixo, "0.#")} cm > {Num(Math.Min(2 * menor, 40), "0.#")} cm (NBR 6118, 18.4.2.2).");
        // proteção contra flambagem: barras a mais de 20 φt de um canto pedem grampo
        var cantos = barras.Where(x => x.Arranjo == ArrCantos).ToList();
        if (cantos.Count == 0 || grampos) return;
        bool longe = barras.Where(x => x.Arranjo != ArrCantos).Any(x => cantos.Min(cn => Math.Max(Math.Abs(cn.X - x.X), Math.Abs(cn.Y - x.Y))) > 20 * phiT / 10 + 1e-6);
        if (longe) Aviso(r, "Há barras a mais de 20φt do canto sem grampos suplementares (NBR 6118, 18.2.4).");
    }
}
