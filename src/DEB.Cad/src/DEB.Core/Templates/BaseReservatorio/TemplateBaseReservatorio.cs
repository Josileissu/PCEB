using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Modelo;

namespace DEB.Core.Templates.BaseReservatorio;

/// <summary>
/// Base de reservatório (circular ou retangular) sobre estacas, no padrão do escritório (projetos Assaí
/// SPA/SRP/ASC 2024–2025): malhas inferior e superior com dobras, faixa central reforçada opcional, borda
/// (U nas faces/U radial + barras de borda/anel), malha intermediária (barras ou tela), planta de locação e
/// formas, planta de armaduras com faixas, corte 1-1, tabelas de comprimentos "L" e relação do aço.
/// Coordenadas em cm; a planta de formas fica centrada na origem.
/// </summary>
public sealed class TemplateBaseReservatorio : ITemplateArmadura
{
    public string Id => "base-reservatorio";
    public string Nome => "Base de reservatório (circular ou retangular)";
    public CategoriaElemento Categoria => CategoriaElemento.BaseCircularReservatorio;
    public string Descricao => "Bloco sobre estacas para reservatório metálico: malhas inferior e superior, borda, malha " +
                               "intermediária, planta de formas e estacas, planta de armaduras, corte e relação do aço.";

    public const string Circular = "Circular", Retangular = "Retangular";
    public const string AteOutraFace = "Até a outra face", Fixa = "Fixa", SemDobra = "Sem dobra";
    public const string VarUnica = "Posição VAR única", VarPorBarra = "Uma posição por barra";

    private static ParametroTemplate P(string grupo, string nome, string rotulo, string padrao, string unidade = "",
        TipoParametro tipo = TipoParametro.Numero, double min = double.NegativeInfinity, double max = double.PositiveInfinity,
        string descricao = "", params string[] opcoes) => new ParametroTemplate
    {
        Grupo = grupo, Nome = nome, Rotulo = rotulo, Padrao = padrao, Unidade = unidade, Tipo = tipo, Minimo = min, Maximo = max,
        Descricao = descricao, Opcoes = opcoes.ToList(),
    };

    private const string G1 = "1. Geometria", G2 = "2. Estacas", G3 = "3. Malha inferior (positiva)", G4 = "4. Malha superior (negativa)",
        G5 = "5. Faixa central reforçada", G6 = "6. Borda", G7 = "7. Malha intermediária", G8 = "8. Desenho";

    public IReadOnlyList<ParametroTemplate> Parametros { get; } = new[]
    {
        P(G1, "Forma", "Forma", Circular, tipo: TipoParametro.Opcao, opcoes: new[] { Circular, Retangular }),
        P(G1, "D", "Diâmetro (circular)", "700", "cm", min: 100),
        P(G1, "Lx", "Lado x (retangular)", "680", "cm", min: 100),
        P(G1, "Ly", "Lado y (retangular)", "680", "cm", min: 100),
        P(G1, "h", "Altura", "85", "cm", min: 30),
        P(G1, "CobSup", "Cobrimento superior e lateral", "4", "cm", min: 1, max: 15),
        P(G1, "CobInf", "Cobrimento inferior", "5", "cm", min: 1, max: 15),
        P(G1, "DiamTanque", "Diâmetro do reservatório", "573", "cm", min: 0, descricao: "Só para o desenho (0 = não desenhar)."),
        P(G1, "Capacidade", "Capacidade do reservatório", "530 m³", tipo: TipoParametro.Texto),

        P(G2, "Arranjo", "Arranjo das estacas", "Anéis", tipo: TipoParametro.Opcao, opcoes: new[] { "Anéis", "Grade", "Nenhuma" }),
        P(G2, "DiamEstaca", "Diâmetro da estaca", "30", "cm", min: 10),
        P(G2, "GradeNx", "Grade: estacas em x", "5", tipo: TipoParametro.Inteiro, min: 1),
        P(G2, "GradeNy", "Grade: estacas em y", "5", tipo: TipoParametro.Inteiro, min: 1),
        P(G2, "GradeEx", "Grade: espaçamento em x", "150", "cm", min: 0),
        P(G2, "GradeEy", "Grade: espaçamento em y", "150", "cm", min: 0),
        P(G2, "Anel1N", "Anel externo: estacas", "8", tipo: TipoParametro.Inteiro, min: 0),
        P(G2, "Anel1R", "Anel externo: raio", "275", "cm", min: 0),
        P(G2, "Anel1Ang", "Anel externo: ângulo da 1ª estaca", "90", "°"),
        P(G2, "Anel2N", "Anel interno: estacas", "6", tipo: TipoParametro.Inteiro, min: 0),
        P(G2, "Anel2R", "Anel interno: raio", "130", "cm", min: 0),
        P(G2, "Anel2Ang", "Anel interno: ângulo da 1ª estaca", "0", "°"),
        P(G2, "EstacaCentral", "Estaca no centro", "Não", tipo: TipoParametro.SimNao),

        P(G3, "InfAco", "Aço", "CA-50", tipo: TipoParametro.Opcao, opcoes: new[] { "CA-50", "CA-60" }),
        P(G3, "InfBitola", "Bitola", "12.5", "mm", TipoParametro.Bitola, 5, 40),
        P(G3, "InfEsp", "Espaçamento", "14", "cm", min: 5),
        P(G3, "InfDobra", "Dobra nas pontas", Fixa, tipo: TipoParametro.Opcao, opcoes: new[] { AteOutraFace, Fixa, SemDobra }),
        P(G3, "InfDobraValor", "Dobra fixa", "50", "cm", min: 0),

        P(G4, "SupAco", "Aço", "CA-50", tipo: TipoParametro.Opcao, opcoes: new[] { "CA-50", "CA-60" }),
        P(G4, "SupBitola", "Bitola", "12.5", "mm", TipoParametro.Bitola, 5, 40),
        P(G4, "SupEsp", "Espaçamento", "14", "cm", min: 5),
        P(G4, "SupDobra", "Dobra nas pontas", Fixa, tipo: TipoParametro.Opcao, opcoes: new[] { AteOutraFace, Fixa, SemDobra }),
        P(G4, "SupDobraValor", "Dobra fixa", "30", "cm", min: 0),

        P(G5, "CentralLargura", "Largura da faixa central", "0", "cm", min: 0,
          descricao: "Barras dentro desta largura (no centro) usam a bitola central. 0 = sem faixa central."),
        P(G5, "CentralBitola", "Bitola central", "16", "mm", TipoParametro.Bitola, 5, 40),

        P(G6, "UBitola", "U de borda: bitola", "10", "mm", TipoParametro.Bitola, 5, 40),
        P(G6, "UEsp", "U de borda: espaçamento", "18", "cm", min: 5, descricao: "Circular: medido no perímetro."),
        P(G6, "UAba", "U de borda: aba horizontal", "100", "cm", min: 10),
        P(G6, "BordaQtdFace", "Retangular: barras de borda por face", "3", tipo: TipoParametro.Inteiro, min: 0,
          descricao: "Barras corridas ao longo das faces (dentro dos U)."),
        P(G6, "BordaBitola", "Retangular: bitola das barras de borda", "16", "mm", TipoParametro.Bitola, 5, 40),
        P(G6, "AnelQtd", "Circular: barras do anel", "4", tipo: TipoParametro.Inteiro, min: 0),
        P(G6, "AnelBitola", "Circular: bitola do anel", "12.5", "mm", TipoParametro.Bitola, 5, 40),
        P(G6, "AnelEmenda", "Circular: emenda do anel", "80", "cm", min: 0, descricao: "Emendas por traspasse alternadas (nota no desenho)."),

        P(G7, "Inter", "Malha intermediária", "Nenhuma", tipo: TipoParametro.Opcao, opcoes: new[] { "Nenhuma", "Barras", "Tela" }),
        P(G7, "InterBitola", "Barras: bitola", "6.3", "mm", TipoParametro.Bitola, 5, 40),
        P(G7, "InterEsp", "Barras: espaçamento", "12", "cm", min: 5),
        P(G7, "InterCamadas", "Camadas (1 = simples, 2 = dupla)", "1", tipo: TipoParametro.Inteiro, min: 1, max: 4),
        P(G7, "InterTela", "Tela: modelo", "Q-196", tipo: TipoParametro.Texto),

        P(G8, "VarModo", "Barras de comprimento variável", VarUnica, tipo: TipoParametro.Opcao, opcoes: new[] { VarUnica, VarPorBarra },
          descricao: "Base circular: uma posição VAR com a tabela de comprimentos, ou uma posição para cada comprimento."),
        P(G8, "Escala", "Escala (1:n)", "30", min: 1),
        P(G8, "Nome", "Nome do detalhamento", "BASE RESERVATÓRIO", tipo: TipoParametro.Texto),
    };

    // ------------------------------------------------------------------

    private sealed class Ctx
    {
        public ValoresTemplate V;
        public ConfiguracaoArmadura Cfg;
        public ResultadoTemplate R;
        public bool Circ;
        public double W, H, Rd, h, cs, ci, esc, ht, gap;
        public double Xa;          // centro da planta de armaduras
        public double Xd, Yd;      // coluna de detalhes (cursor)
        public double LarguraDetalhes;
        public int Seq;
    }

    public ResultadoTemplate Gerar(ValoresTemplate v, ConfiguracaoArmadura cfg)
    {
        var c = new Ctx { V = v, Cfg = cfg, R = new ResultadoTemplate() };
        c.Circ = !string.Equals(v["Forma"], Retangular, StringComparison.OrdinalIgnoreCase);
        c.Rd = v.Numero("D") / 2;
        c.W = c.Circ ? 2 * c.Rd : v.Numero("Lx");
        c.H = c.Circ ? 2 * c.Rd : v.Numero("Ly");
        c.h = v.Numero("h");
        c.cs = v.Numero("CobSup");
        c.ci = v.Numero("CobInf");
        c.esc = Math.Max(1, v.Numero("Escala"));
        c.ht = cfg.AlturaTextoMm / 10.0 * c.esc;
        c.gap = Math.Max(150, 0.35 * Math.Max(c.W, c.H));
        c.Xa = c.W + c.gap;
        c.Xd = c.Xa + c.W / 2 + c.gap;
        c.Yd = c.H / 2;
        c.R.Escala = c.esc;
        c.R.Detalhamento = string.IsNullOrWhiteSpace(v["Nome"]) ? "BASE RESERVATÓRIO" : v["Nome"].Trim();

        PlantaDeFormas(c);
        PlantaDeArmaduras(c);
        Corte(c);
        c.R.PosicaoRA = (c.Xd + c.LarguraDetalhes + c.gap, c.H / 2);
        Validar(c);
        return c.R;
    }

    // ---------- desenho de forma ----------

    private static ContornoTemplate Contorno(Ctx c, double cx, double cy)
    {
        var k = new ContornoTemplate { Fechado = true, Layer = "ARM-FORMA" };
        if (c.Circ)
        {
            k.Vertices.Add((cx + c.Rd, cy)); k.Bulges.Add(1);
            k.Vertices.Add((cx - c.Rd, cy)); k.Bulges.Add(1);
        }
        else
        {
            k.Vertices.AddRange(new[] { (cx - c.W / 2, cy - c.H / 2), (cx + c.W / 2, cy - c.H / 2), (cx + c.W / 2, cy + c.H / 2), (cx - c.W / 2, cy + c.H / 2) });
        }
        return k;
    }

    private static void Texto(Ctx c, string t, double x, double y, double fator = 1, int alinh = 0, string layer = "ARM-TEXTO-FORMA") =>
        c.R.Textos.Add(new TextoTemplate { Texto = t, X = x, Y = y, Altura = c.ht * fator, Alinhamento = alinh, Layer = layer });

    private static void Titulo(Ctx c, string t, double x, double y)
    {
        Texto(c, t, x, y, 2.0);
        Texto(c, "ESC 1:" + Num(c.esc), x, y - 2.8 * c.ht);
    }

    private static List<(double X, double Y)> Estacas(Ctx c)
    {
        var v = c.V;
        var r = new List<(double, double)>();
        switch (v["Arranjo"])
        {
            case "Grade":
            {
                int nx = Math.Max(1, v.Inteiro("GradeNx")), ny = Math.Max(1, v.Inteiro("GradeNy"));
                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < ny; j++)
                        r.Add(((i - (nx - 1) / 2.0) * v.Numero("GradeEx"), (j - (ny - 1) / 2.0) * v.Numero("GradeEy")));
                break;
            }
            case "Anéis":
                foreach (var (n, raio, ang) in new[] { (v.Inteiro("Anel1N"), v.Numero("Anel1R"), v.Numero("Anel1Ang")), (v.Inteiro("Anel2N"), v.Numero("Anel2R"), v.Numero("Anel2Ang")) })
                    for (int k = 0; k < n; k++)
                    {
                        double a = (ang + 360.0 * k / n) * Math.PI / 180;
                        r.Add((raio * Math.Cos(a), raio * Math.Sin(a)));
                    }
                if (v.SimNao("EstacaCentral")) r.Add((0, 0));
                break;
        }
        return r;
    }

    private static void PlantaDeFormas(Ctx c)
    {
        var v = c.V;
        c.R.Contornos.Add(Contorno(c, 0, 0));
        double dt = v.Numero("DiamTanque");
        if (dt > 0) c.R.Circulos.Add(new CirculoTemplate { X = 0, Y = 0, Raio = dt / 2, Layer = "ARM-TANQUE" });
        double re = Math.Max(1, v.Numero("DiamEstaca")) / 2;
        int i = 1;
        foreach (var (x, y) in Estacas(c))
        {
            c.R.Circulos.Add(new CirculoTemplate { X = x, Y = y, Raio = re, Layer = "ARM-ESTACA" });
            Texto(c, "E" + i++, x + re * 0.8, y + re * 0.8, 0.8);
        }
        Texto(c, "BASE", 0, c.ht, 1.4, 1);
        Texto(c, "h=" + Num(c.h) + " cm", 0, -1.5 * c.ht, 1, 1);
        // cotas gerais
        double yc = -c.H / 2 - 4 * c.ht;
        c.R.Cotas.Add(new CotaTemplate { P1 = (-c.W / 2, 0), P2 = (c.W / 2, 0), Posicao = (0, yc), Angulo = 0, Escala = c.esc,
                                         Texto = c.Circ ? "Ø" + Num(c.W) : "" });
        if (!c.Circ)
            c.R.Cotas.Add(new CotaTemplate { P1 = (0, -c.H / 2), P2 = (0, c.H / 2), Posicao = (-c.W / 2 - 4 * c.ht, 0), Angulo = Math.PI / 2, Escala = c.esc });
        Titulo(c, "LOCAÇÃO DAS ESTACAS E FORMAS", -c.W / 2, yc - 6 * c.ht);

        // notas (abaixo da planta de formas)
        double yn = yc - 14 * c.ht;
        var notas = new List<string>
        {
            $"COBRIMENTO SUPERIOR E LATERAL = {Num(c.cs)} cm",
            $"COBRIMENTO INFERIOR = {Num(c.ci)} cm",
        };
        if (dt > 0) notas.Add($"RESERVATÓRIO METÁLICO Ø{Num(dt)}" + (string.IsNullOrWhiteSpace(v["Capacidade"]) ? "" : " - CAP. " + v["Capacidade"].Trim()));
        if (c.Circ && v.Inteiro("AnelQtd") > 0 && v.Numero("AnelEmenda") > 0)
            notas.Add($"ANEL DE BORDA: EMENDAS POR TRASPASSE ALTERNADAS DE {Num(v.Numero("AnelEmenda"))} cm");
        foreach (var n in notas) { Texto(c, n, -c.W / 2, yn); yn -= 2 * c.ht; }
    }

    // ---------- armaduras ----------

    private static string Num(double x) => Calculo.CalculoComprimento.NumeroCompacto(x);

    private static double Dobra(Ctx c, string camada)
    {
        string modo = c.V[camada + "Dobra"];
        if (string.Equals(modo, SemDobra, StringComparison.OrdinalIgnoreCase)) return 0;
        if (string.Equals(modo, AteOutraFace, StringComparison.OrdinalIgnoreCase)) return Math.Max(0, c.h - c.cs - c.ci);
        return Math.Max(0, c.V.Numero(camada + "DobraValor"));
    }

    /// <summary>Comprimento (trecho reto) de uma barra na posição t da distribuição.</summary>
    private static double Comprimento(Ctx c, bool emX, double t)
    {
        if (!c.Circ) return (emX ? c.W : c.H) - 2 * c.cs;
        double r = c.Rd - c.cs;
        return Math.Abs(t) >= r ? 0 : 2 * Math.Sqrt(r * r - t * t);
    }

    /// <summary>
    /// Uma família de barras retas (com ou sem dobras) distribuída numa direção: separa faixa central e
    /// laterais, gera os grupos (VAR ou fixos), as formas na coluna de detalhes e as faixas na planta.
    /// </summary>
    private static void Malha(Ctx c, string rotulo, bool emX, double bitola, string aco, double esp, double dobra, bool dobraParaCima,
        double xFaixa, double bitolaCentral, double larguraCentral, int camadas = 1)
    {
        double S = (emX ? c.H : c.W) - 2 * c.cs;          // vão da distribuição
        var cfg = c.Cfg;
        int n = CalculoFaixa.Quantidade(S, esp, cfg.EspacamentoInicialFaixa, cfg.ModoQuantidadeFaixa);
        // distribuição simétrica: a sobra fica metade de cada lado (barras iguais duas a duas)
        var ts = Enumerable.Range(0, n).Select(k => -(n - 1) * esp / 2 + k * esp).ToList();
        bool comCentral = larguraCentral > 0 && bitolaCentral > 0;
        var grupos = new List<(string nome, double bitola, List<double> ts)>();
        if (comCentral)
        {
            var centrais = ts.Where(t => Math.Abs(t) <= larguraCentral / 2 + 1e-9).ToList();
            var laterais = ts.Where(t => Math.Abs(t) > larguraCentral / 2 + 1e-9).ToList();
            if (centrais.Count > 0) grupos.Add(("central", bitolaCentral, centrais));
            if (laterais.Count > 0) grupos.Add(("lateral", bitola, laterais));
        }
        else grupos.Add(("", bitola, ts));

        bool porBarra = c.Circ && string.Equals(c.V["VarModo"], VarPorBarra, StringComparison.OrdinalIgnoreCase);
        foreach (var (nomeGrupo, bit, posicoes) in grupos)
        {
            var pares = posicoes.Select(t => (t, L: Comprimento(c, emX, t))).Where(p => p.L > 1).ToList();
            if (pares.Count == 0) continue;
            string desc = $"{rotulo}{(nomeGrupo.Length > 0 ? " " + nomeGrupo : "")} ({(emX ? "x" : "y")})";
            if (!c.Circ)
            {
                var b = BarraReta(c, desc, pares.Max(p => p.L), dobra, dobraParaCima, bit, aco, esp, camadas);
                b.Quantidade = pares.Count;
                Faixas(c, b.Nome, emX, xFaixa, posicoes, S, esp, pares.Count == ts.Count);
            }
            else if (!porBarra)
            {
                var b = BarraReta(c, desc, pares.Max(p => p.L), dobra, dobraParaCima, bit, aco, esp, camadas);
                b.ComprimentosVar = pares.Select(p => Math.Round(p.L + 2 * dobra, 1)).ToList();
                b.SegmentosVar.Add(dobra > 0 ? 1 : 0);
                b.Quantidade = pares.Count;
                Faixas(c, b.Nome, emX, xFaixa, posicoes, S, esp, pares.Count == ts.Count);
                TabelaL(c, desc, bit, esp, pares.Select(p => p.L + 2 * dobra).ToList());
            }
            else
            {
                // uma posição para cada comprimento (barras simétricas ficam juntas)
                foreach (var g in pares.GroupBy(p => Math.Round(p.L, 0)).OrderByDescending(g => g.Key))
                {
                    var b = BarraReta(c, $"{desc} L={Num(g.Key)}", g.Max(p => p.L), dobra, dobraParaCima, bit, aco, esp, camadas);
                    b.Quantidade = g.Count();
                }
            }
        }
    }

    /// <summary>Forma de uma barra reta com dobras nas pontas, desenhada na coluna de detalhes.</summary>
    private static BarraTemplate BarraReta(Ctx c, string desc, double L, double dobra, bool paraCima, double bitola, string aco,
        double esp, int repeticoes)
    {
        var b = new BarraTemplate
        {
            Nome = "b" + (++c.Seq) + " " + desc, Bitola = bitola, Aco = aco, Espacamento = esp, Repeticoes = Math.Max(1, repeticoes), Escala = c.esc,
        };
        double s = paraCima ? 1 : -1;
        double y0 = paraCima ? c.Yd - dobra : c.Yd;
        double x0 = c.Xd;
        if (dobra > 0)
        {
            b.Vertices.AddRange(new[] { (x0, y0 + s * dobra), (x0, y0), (x0 + L, y0), (x0 + L, y0 + s * dobra) });
            b.Formatos.AddRange(new[] { FormatoSegmento.Linha, FormatoSegmento.Linha, FormatoSegmento.Linha });
        }
        else
        {
            b.Vertices.AddRange(new[] { (x0, y0), (x0 + L, y0) });
            b.Formatos.Add(FormatoSegmento.Linha);
        }
        c.LarguraDetalhes = Math.Max(c.LarguraDetalhes, L);
        c.Yd -= dobra + 7 * c.ht;
        c.R.Barras.Add(b);
        return b;
    }

    /// <summary>Faixas na planta de armaduras: uma por trecho contínuo de barras (a central e as laterais).</summary>
    private static void Faixas(Ctx c, string barra, bool emX, double xFaixa, List<double> ts, double vao, double esp, bool todas)
    {
        var trechos = new List<(double a, double b)>();
        if (todas) trechos.Add((-vao / 2, vao / 2));
        else
        {
            var ord = ts.OrderBy(t => t).ToList();
            double ini = ord[0], ant = ord[0];
            foreach (var t in ord.Skip(1))
            {
                if (t - ant > esp * 1.5) { trechos.Add((ini, ant)); ini = t; }
                ant = t;
            }
            trechos.Add((ini, ant));
        }
        foreach (var (a, b) in trechos)
        {
            if (b - a < 1e-6) continue;
            var f = new FaixaTemplate { Barra = barra, Tipo = TipoFaixa.Ortogonal, Espacamento = esp, Escala = c.esc };
            if (emX)
            {
                // barras em x, distribuídas em y: faixa vertical
                f.P1 = (c.Xa + xFaixa, a); f.P2 = (c.Xa + xFaixa, b); f.Posicao = (c.Xa + xFaixa + 2 * c.ht, (a + b) / 2);
            }
            else
            {
                f.P1 = (c.Xa + a, xFaixa); f.P2 = (c.Xa + b, xFaixa); f.Posicao = ((c.Xa + a + c.Xa + b) / 2, xFaixa + 2 * c.ht);
            }
            c.R.Faixas.Add(f);
        }
    }

    /// <summary>Tabela dos comprimentos de um grupo VAR (externos, na ordem da distribuição).</summary>
    private static void TabelaL(Ctx c, string desc, double bitola, double esp, List<double> comps)
    {
        var t = new TabelaTemplate
        {
            Titulo = $"COMPRIMENTOS (cm) - {desc.ToUpperInvariant()} ø{bitola.ToString("0.0", CultureInfo.InvariantCulture)} c/{Num(esp)}",
            Escala = c.esc,
        };
        const int porLinha = 10;
        for (int i = 0; i < comps.Count; i += porLinha)
        {
            var linha = comps.Skip(i).Take(porLinha).Select(x => Num(Math.Round(x, 0))).ToList();
            linha.Insert(0, $"{i + 1}-{Math.Min(comps.Count, i + porLinha)}");
            t.Linhas.Add(linha.ToArray());
        }
        c.R.Tabelas.Add(t);
    }

    private static void PlantaDeArmaduras(Ctx c)
    {
        var v = c.V;
        c.R.Contornos.Add(Contorno(c, c.Xa, 0));
        Titulo(c, "ARMADURA DA BASE", c.Xa - c.W / 2, -c.H / 2 - 10 * c.ht);
        Texto(c, "DETALHES DAS BARRAS", c.Xd, c.H / 2 + 4 * c.ht, 1.4);

        double larguraCentral = v.Numero("CentralLargura"), bitCentral = v.Numero("CentralBitola");
        double dInf = Dobra(c, "Inf"), dSup = Dobra(c, "Sup");
        double q = Math.Min(c.W, c.H) / 4;
        // malha inferior (dobras para cima) e superior (dobras para baixo), nas 2 direções
        Malha(c, "Malha inferior", true, v.Numero("InfBitola"), v["InfAco"], v.Numero("InfEsp"), dInf, true, -q, bitCentral, larguraCentral);
        Malha(c, "Malha inferior", false, v.Numero("InfBitola"), v["InfAco"], v.Numero("InfEsp"), dInf, true, -q, bitCentral, larguraCentral);
        Malha(c, "Malha superior", true, v.Numero("SupBitola"), v["SupAco"], v.Numero("SupEsp"), dSup, false, q, bitCentral, larguraCentral);
        Malha(c, "Malha superior", false, v.Numero("SupBitola"), v["SupAco"], v.Numero("SupEsp"), dSup, false, q, bitCentral, larguraCentral);

        // malha intermediária
        int camadas = Math.Max(1, v.Inteiro("InterCamadas"));
        if (v["Inter"] == "Barras")
        {
            Malha(c, "Malha intermediária", true, v.Numero("InterBitola"), "CA-50", v.Numero("InterEsp"), 0, true, 0, 0, 0, camadas);
            Malha(c, "Malha intermediária", false, v.Numero("InterBitola"), "CA-50", v.Numero("InterEsp"), 0, true, 0, 0, 0, camadas);
        }
        else if (v["Inter"] == "Tela")
        {
            var tela = new BarraTemplate { Nome = "tela", Tela = v["InterTela"].Trim(), Repeticoes = camadas, Fechada = true, Escala = c.esc, Aco = "CA-60" };
            double r = c.Rd - c.cs;
            if (c.Circ)
            {
                double bul = Math.Tan(Math.PI / 8);
                for (int k = 0; k < 4; k++)
                {
                    double a = k * Math.PI / 2;
                    tela.Vertices.Add((c.Xa + r * Math.Cos(a), r * Math.Sin(a)));
                    tela.Bulges.Add(bul);
                }
            }
            else
            {
                double ax = c.W / 2 - c.cs, ay = c.H / 2 - c.cs;
                tela.Vertices.AddRange(new[] { (c.Xa - ax, -ay), (c.Xa + ax, -ay), (c.Xa + ax, ay), (c.Xa - ax, ay) });
            }
            c.R.Barras.Add(tela);
        }

        Borda(c);
    }

    private static void Borda(Ctx c)
    {
        var v = c.V;
        double hU = Math.Max(1, c.h - c.cs - c.ci), aba = v.Numero("UAba"), esp = v.Numero("UEsp");
        // U de borda (abas horizontais para dentro da base)
        var u = new BarraTemplate { Nome = "U de borda", Bitola = v.Numero("UBitola"), Aco = "CA-50", Espacamento = esp, Escala = c.esc };
        double x0 = c.Xd, y0 = c.Yd - hU;
        u.Vertices.AddRange(new[] { (x0 + aba, y0 + hU), (x0, y0 + hU), (x0, y0), (x0 + aba, y0) });
        u.Formatos.AddRange(new[] { FormatoSegmento.Linha, FormatoSegmento.Linha, FormatoSegmento.Linha });
        c.Yd -= hU + 7 * c.ht;
        c.LarguraDetalhes = Math.Max(c.LarguraDetalhes, aba);
        c.R.Barras.Add(u);
        if (c.Circ)
        {
            u.Quantidade = Math.Max(1, (int)Math.Round(Math.PI * (2 * c.Rd - 2 * c.cs) / esp));
            // anel de borda (dentro dos U), corrido
            int nAnel = v.Inteiro("AnelQtd");
            if (nAnel > 0)
            {
                double ra = c.Rd - c.cs - v.Numero("UBitola") / 10.0 - v.Numero("AnelBitola") / 20.0;
                var anel = new BarraTemplate
                {
                    Nome = "Anel de borda", Bitola = v.Numero("AnelBitola"), Aco = "CA-50", Quantidade = nAnel, Corrido = true, Fechada = true, Escala = c.esc,
                };
                anel.Vertices.Add((c.Xa + ra, 0)); anel.Bulges.Add(1);
                anel.Vertices.Add((c.Xa - ra, 0)); anel.Bulges.Add(1);
                anel.Formatos.AddRange(new[] { FormatoSegmento.Curva, FormatoSegmento.Curva });
                c.R.Barras.Add(anel);
            }
        }
        else
        {
            // U nas 4 faces: faixas junto de cada face (a soma das faixas é a quantidade)
            double ax = c.W / 2 - c.cs, ay = c.H / 2 - c.cs, d = 3 * c.ht;
            foreach (var (p1, p2, pos) in new[]
            {
                ((c.Xa - ax, -ay + d), (c.Xa + ax, -ay + d), (c.Xa, -ay + 2 * d)),
                ((c.Xa - ax, ay - d), (c.Xa + ax, ay - d), (c.Xa, ay - 2 * d)),
                ((c.Xa - ax + d, -ay), (c.Xa - ax + d, ay), (c.Xa - ax + 2 * d, 0)),
                ((c.Xa + ax - d, -ay), (c.Xa + ax - d, ay), (c.Xa + ax - 2 * d, 0)),
            })
                c.R.Faixas.Add(new FaixaTemplate { Barra = u.Nome, Tipo = TipoFaixa.Ortogonal, P1 = p1, P2 = p2, Posicao = pos, Espacamento = esp, Escala = c.esc });

            int q = v.Inteiro("BordaQtdFace");
            if (q > 0)
            {
                foreach (var (desc, L) in new[] { ("Borda (x)", c.W - 2 * c.cs), ("Borda (y)", c.H - 2 * c.cs) })
                {
                    var b = BarraReta(c, desc, L, 0, true, v.Numero("BordaBitola"), "CA-50", 0, 1);
                    b.Quantidade = 2 * q;
                }
            }
        }
    }

    // ---------- corte 1-1 ----------

    private static void Corte(Ctx c)
    {
        var v = c.V;
        double yb = Math.Min(-c.H / 2 - 16 * c.ht, c.Yd) - c.gap - c.h;   // base do corte
        double xl = c.Xa - c.W / 2, xr = c.Xa + c.W / 2;
        c.R.Contornos.Add(new ContornoTemplate { Fechado = true, Layer = "ARM-FORMA", Vertices = { (xl, yb), (xr, yb), (xr, yb + c.h), (xl, yb + c.h) } });
        // estacas cortadas pelo plano (perto do eixo x)
        double re = Math.Max(1, v.Numero("DiamEstaca")) / 2;
        foreach (var (x, y) in Estacas(c).Where(p => Math.Abs(p.Y) <= re + 1e-6))
            c.R.Contornos.Add(new ContornoTemplate { Layer = "ARM-ESTACA", Vertices = { (c.Xa + x - re, yb - 60), (c.Xa + x - re, yb), (c.Xa + x + re, yb), (c.Xa + x + re, yb - 60) } });

        // barras no corte (desenho representativo) e indicações das posições
        double xi = xl + c.cs, xf = xr - c.cs, yInf = yb + c.ci, ySup = yb + c.h - c.cs;
        double dInf = Dobra(c, "Inf"), dSup = Dobra(c, "Sup");
        void Linha(params (double, double)[] pts) => c.R.Contornos.Add(new ContornoTemplate { Layer = "ARM-CORTE", Vertices = pts.ToList() });
        Linha((xi, yInf + dInf), (xi, yInf), (xf, yInf), (xf, yInf + dInf));
        Linha((xi, ySup - dSup), (xi, ySup), (xf, ySup), (xf, ySup - dSup));
        if (v["Inter"] != "Nenhuma") Linha((xi, yb + c.h / 2), (xf, yb + c.h / 2));
        double aba = Math.Min(v.Numero("UAba"), c.W / 3);
        Linha((xi + aba, ySup), (xi, ySup), (xi, yInf), (xi + aba, yInf));
        Linha((xf - aba, ySup), (xf, ySup), (xf, yInf), (xf - aba, yInf));

        string Barra(string prefixo) => c.R.Barras.FirstOrDefault(b => b.Nome.Contains(prefixo))?.Nome;
        void Indicar(string barra, double x, double y, double xt, double yt)
        {
            if (barra == null) return;
            c.R.Faixas.Add(new FaixaTemplate { Barra = barra, Tipo = TipoFaixa.Indicacao, Referencias = { (x, y) }, Posicao = (xt, yt), Escala = c.esc });
        }
        Indicar(Barra("Malha inferior"), c.Xa - c.W / 4, yInf, c.Xa - c.W / 4 + 3 * c.ht, yb - 6 * c.ht);
        Indicar(Barra("Malha superior"), c.Xa + c.W / 4, ySup, c.Xa + c.W / 4 + 3 * c.ht, yb + c.h + 5 * c.ht);
        Indicar(Barra("U de borda"), xi, yb + c.h / 2, xl - 10 * c.ht, yb + c.h / 2);
        if (v["Inter"] == "Barras") Indicar(Barra("Malha intermediária"), c.Xa, yb + c.h / 2, xr + 4 * c.ht, yb + c.h / 2);

        c.R.Cotas.Add(new CotaTemplate { P1 = (xr, yb), P2 = (xr, yb + c.h), Posicao = (xr + 5 * c.ht, yb + c.h / 2), Angulo = Math.PI / 2, Escala = c.esc });
        Titulo(c, "CORTE 1-1", xl, yb - 70 - 4 * c.ht);

        // tabelas de comprimentos abaixo do corte
        double yt = yb - 70 - 12 * c.ht;
        foreach (var t in c.R.Tabelas)
        {
            t.X = xl;
            t.Y = yt;
            yt -= (t.Linhas.Count + 2) * 2.2 * c.ht + 4 * c.ht;
        }
    }

    private static void Validar(Ctx c)
    {
        var v = c.V;
        if (c.Circ && v.Numero("DiamTanque") > 2 * c.Rd) c.R.Avisos.Add("O reservatório é maior que a base.");
        double re = v.Numero("DiamEstaca") / 2;
        foreach (var (x, y) in Estacas(c))
        {
            bool fora = c.Circ ? Math.Sqrt(x * x + y * y) + re > c.Rd : Math.Abs(x) + re > c.W / 2 || Math.Abs(y) + re > c.H / 2;
            if (fora) { c.R.Avisos.Add("Há estaca fora da base: confira o arranjo."); break; }
        }
        if (Dobra(c, "Inf") + Dobra(c, "Sup") > c.h - c.cs - c.ci + 1e-6 && v["InfDobra"] != AteOutraFace)
            c.R.Avisos.Add("As dobras das malhas inferior e superior se cruzam na altura da base.");
    }
}
