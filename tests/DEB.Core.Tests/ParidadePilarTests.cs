using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Calculo;
using DEB.Core.Configuracao;
using DEB.Core.Modelo;
using DEB.Core.Motor;
using DEB.Core.Documentacao;
using DEB.Core.Elementos.Pilar.PreMoldado;
using DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D;
using Xunit;
using DEB.Core.Elementos.Consolo;

namespace DEB.Core.Tests;

/// <summary>
/// Paridade do pilar pré-moldado (etapa 3 da organização, O-2): o que o DEB.Revit calcula (ColumnRebarLayout, barras em 3D)
/// × o que o template do CAD calcula (TemplatePilarPreMoldado, a referência — D7) para a MESMA peça e a MESMA armadura.
/// Os dois lados recebem os mesmos dados (seção, comprimento, cobrimento, bitolas, quantidades, zonas, consolo); o CAD recebe
/// um tipo de consolo do catálogo com a armadura da especificação do Revit.
/// Comprimentos: os dois pelo núcleo (Revit: <see cref="ComprimentoPorEixo"/> sobre o eixo das barras, como
/// SharedRules.RealLengthCm; CAD: <see cref="MedidaBarra.Unitario"/> sobre a barra desenhada). O comprimento que a API do
/// Revit mede (REBAR_ELEM_LENGTH) não entra aqui: só existe dentro do Revit (ver docs/04_arquitetura/paridade_pilar.md).
/// Cada medida tem o valor esperado dos dois lados; quando diferem, a causa fica escrita (divergência conhecida). Qualquer
/// mudança num dos dois caminhos faz o teste falhar.
/// </summary>
[Collection("Pilar")]
public class ParidadePilarTests
{
    private const double CmPorPe = 30.48;
    private static readonly TemplatePilarPreMoldado T = new TemplatePilarPreMoldado();

    /// <summary>Caso: seção (cm), bitola longitudinal (mm) e consolos (0, 1 na face +x, 2 = +x e −x).</summary>
    public sealed class Caso
    {
        public string Nome = "";
        public double B, H;
        public double Phi;
        public int Consolos;
        public override string ToString() => Nome;
    }

    private static readonly Caso[] Casos =
    {
        new Caso { Nome = "40x40-sem-12.5", B = 40, H = 40, Phi = 12.5, Consolos = 0 },
        new Caso { Nome = "40x40-um-12.5", B = 40, H = 40, Phi = 12.5, Consolos = 1 },
        new Caso { Nome = "40x40-opostos-16", B = 40, H = 40, Phi = 16, Consolos = 2 },
        new Caso { Nome = "50x50-sem-16", B = 50, H = 50, Phi = 16, Consolos = 0 },
        new Caso { Nome = "50x50-um-16", B = 50, H = 50, Phi = 16, Consolos = 1 },
        new Caso { Nome = "50x50-opostos-12.5", B = 50, H = 50, Phi = 12.5, Consolos = 2 },
    };

    // pilar de 6 m, 1 m embutido; consolo 40 (largura) × 30 (altura) × 25 (balanço), topo a 4 m da base
    private const double L = 600, Lemb = 100, CB = 40, CH = 30, CA = 25, CTopo = 400;

    public static IEnumerable<object[]> TodosOsCasos() => Casos.Select(c => new object[] { c });

    // ------------------------------------------------------------------ caminho do Revit

    private static (ColumnBox F, List<Corbel> K, ColumnRebarSpec S) Revit(Caso c)
    {
        var f = new ColumnBox(U_.Mm(c.B * 10), U_.Mm(c.H * 10), U_.Mm(L * 10));
        var k = new List<Corbel>();
        foreach (var face in new[] { ColumnFace.PX, ColumnFace.NX }.Take(c.Consolos))
            k.Add(new Corbel
            {
                Face = face, S0 = U_.Mm((c.H - CB) / 2 * 10), S1 = U_.Mm((c.H + CB) / 2 * 10),
                ZBottom = U_.Mm((CTopo - CH) * 10), ZTop = U_.Mm(CTopo * 10), Projection = U_.Mm(CA * 10), TipHeight = U_.Mm(CH * 10),
            });
        var s = ColumnRebarSpec.Default(f, Lemb * 10, k);
        s.LongDiameterMm = c.Phi;
        return (f, k, s);
    }

    /// <summary>Mesma regra de SharedRules.RealLengthCm (DEB.Revit): aço pela bitola, "estribo" para estribos, grampos e costura.</summary>
    private static double ComprimentoRevit(BarDef b)
    {
        bool tie = b.Role == BarRole.Stirrup || b.Role == BarRole.CrossTie || b.Role == BarRole.CorbelSew || b.Role == BarRole.CorbelStirrup;
        var eixo = b.Points.Select(p => (p.A * CmPorPe, p.B * CmPorPe, p.C * CmPorPe)).ToList();
        string aco = b.Steel ?? (b.DiameterMm < 6.3 ? "CA-60" : "CA-50");
        var arcos = b.ArcMid.ToDictionary(kv => kv.Key, kv => (kv.Value.A * CmPorPe, kv.Value.B * CmPorPe, kv.Value.C * CmPorPe));
        return ComprimentoPorEixo.Calcular(eixo, b.Closed, b.DiameterMm, aco, tie, b.Hooks135, new ConfiguracaoArmadura(), arcos);
    }

    // ------------------------------------------------------------------ caminho do CAD (template), com os dados do Revit

    private static TipoConsolo TipoDoRevit(ColumnRebarSpec s, bool duplo) => new TipoConsolo
    {
        Nome = duplo ? "PARIDADE D" : "PARIDADE S", Duplo = duplo, B = CB, A = CA, H = CH, H1 = CH,
        TirN = s.TieCount, TirPhi = s.TieDiameterMm, Laco = !s.TieWeldedBar, TirPerna = s.TieInnerLegMm / 10,
        CostN = s.SewCount, CostPhi = s.SewDiameterMm, VertN = s.CorbelStirrupCount, VertPhi = s.CorbelStirrupDiameterMm,
        InfN = s.CorbelBottomCount, InfPhi = s.CorbelBottomDiameterMm,
        PinoN = s.CorbelPinCount, PinoPhi = s.CorbelPinDiameterMm, PinoAcima = s.CorbelPinAboveMm / 10,
        PinoEmb = s.CorbelPinEmbedMm / 10, PinoPe = s.CorbelPinFootMm / 10,
    };

    private static string N(double x) => x.ToString("0.###", CultureInfo.InvariantCulture);

    private static ResultadoTemplate Cad(Caso c, ColumnRebarSpec s, ConfiguracaoArmadura cfg)
    {
        var v = new ValoresTemplate(T.Parametros);
        v["B"] = N(c.B); v["H"] = N(c.H); v["L"] = N(L); v["Lemb"] = N(Lemb); v["Chanfro"] = "0";
        foreach (var k in new[] { "CobFace1", "CobFace2", "CobFace3", "CobFace4" }) v[k] = N(s.CoverMm / 10);
        v["CobTopo"] = v["CobBase"] = N(s.LongEndCoverMm / 10);
        v["LongAco"] = "CA-50";
        int porFace = s.LongBarsAlongX - 2;
        v["Longitudinais"] = $"{TemplatePilarPreMoldado.ArrCantos}||{N(c.Phi)}|1||;" +
                             $"{TemplatePilarPreMoldado.ArrFaces24}|{porFace}|{N(c.Phi)}|1||;" +
                             $"{TemplatePilarPreMoldado.ArrFaces13}|{porFace}|{N(c.Phi)}|1||";
        v["Emendas"] = TemplatePilarPreMoldado.SemEmendas;
        v["EstAco"] = s.StirrupDiameterMm < 6.3 ? "CA-60" : "CA-50";
        v["EstBitola"] = N(s.StirrupDiameterMm); v["EstEsp"] = N(s.StirrupSpacingMm / 10);
        string phiT = N(s.StirrupDiameterMm);
        var zonas = new List<string>
        {
            $"Base|Base|0|{N(s.BaseZoneLengthMm / 10)}|{phiT}|{N(s.BaseZoneSpacingMm / 10)}|Sim",
            $"Topo|Topo|0|{N(s.TopZoneLengthMm / 10)}|{phiT}|{N(s.TopZoneSpacingMm / 10)}|Sim",
        };
        if (c.Consolos > 0)
            zonas.Add($"Consolo|Base|{N(CTopo - CH - s.CorbelZoneExtensionMm / 10)}|{N(CTopo + s.CorbelZoneExtensionMm / 10)}|{phiT}|{N(s.CorbelZoneSpacingMm / 10)}|Sim");
        v["ZonasEstribos"] = string.Join(";", zonas);
        v["GramposCorrente"] = s.CrossTies ? "Sim" : "Não";
        v["GrampoDirecoes"] = "2";
        v["GrampoBitola"] = phiT;
        v["UTopo"] = v["UBase"] = "Não";
        // consolo na face +x = face interna 1 = face 3 apresentada; opostos: "1 e 3" (duplo)
        v["Consolos"] = c.Consolos == 0 ? "" : c.Consolos == 1 ? $"PARIDADE S|3|{N(CTopo)}|0" : $"PARIDADE D|1 e 3|{N(CTopo)}|0";
        Assert.Empty(CatalogoTemplates.Validar(T, v));
        return T.Gerar(v, cfg);
    }

    // ------------------------------------------------------------------ medidas

    /// <summary>Uma medida dos dois caminhos.</summary>
    public readonly struct Medida
    {
        public Medida(string item, double revit, double cad) { Item = item; Revit = revit; Cad = cad; }
        public string Item { get; }
        public double Revit { get; }
        public double Cad { get; }
    }

    private static List<Medida> Medir(Caso c)
    {
        var (f, k, s) = Revit(c);
        List<BarDef> bars = ColumnRebarLayout.Compute(f, k, s);
        var cfg = new ConfiguracaoArmadura { AlturaTextoMm = 2 };
        var m = new List<Medida>();
        List<string> avisos = null;
        ResultadoTemplate r = null;
        TemplatePilarPreMoldado.Catalogo = TipoConsolo.Padrao().Concat(new[] { TipoDoRevit(s, false), TipoDoRevit(s, true) }).ToList();
        try { r = Cad(c, s, cfg); avisos = r.Avisos; }
        finally { TemplatePilarPreMoldado.Catalogo = TipoConsolo.Padrao(); }

        BarraTemplate Barra(string nome) => r.Barras.FirstOrDefault(b => b.Nome == nome);
        int FaixasQtd(string barra) => r.Faixas.Where(x => x.Barra == barra && x.Tipo == TipoFaixa.Ortogonal)
            .Sum(x => CalculoFaixa.Quantidade(Math.Abs(x.P2.Y - x.P1.Y), x.Espacamento, cfg.EspacamentoInicialFaixa, cfg.ModoQuantidadeFaixa) * Math.Max(1, x.Repeticoes));
        double Un(string nome) { var b = Barra(nome); return b == null ? 0 : MedidaBarra.Unitario(b, cfg); }
        int Qtd(string nome) { var b = Barra(nome); return b == null ? 0 : Math.Max(1, b.Quantidade) * Math.Max(1, b.Repeticoes); }
        BarDef Um(BarRole role) => bars.FirstOrDefault(b => b.Role == role);
        int RevQtd(BarRole role) => bars.Where(b => b.Role == role).Sum(b => b.Count);

        // ---- longitudinais: quantidade e posições na seção (cm, origem no canto −x −y) ----
        var longR = bars.Where(b => b.Role == BarRole.Longitudinal).Select(b => (X: b.Points[0].A * CmPorPe, Y: b.Points[0].B * CmPorPe)).ToList();
        double phiT = s.StirrupDiameterMm;
        var cob = Enumerable.Repeat(s.CoverMm / 10, 4).ToArray();
        var linhas = TemplatePilarPreMoldado.LerLongitudinais(Valores(c, s), s.LongEndCoverMm / 10, L - s.LongEndCoverMm / 10, new List<string>());
        var longC = TemplatePilarPreMoldado.PosicoesLinhas(c.B, c.H, cob, phiT, linhas, 5).Values.SelectMany(x => x).Select(x => (x.X, x.Y)).ToList();
        m.Add(new Medida("longitudinais: quantidade", longR.Count, longC.Count));
        // a maior distância de uma barra de um lado à barra mais próxima do outro (0 = mesmas posições)
        double Dist((double X, double Y) p, List<(double X, double Y)> q) => q.Min(o => Math.Sqrt((o.X - p.X) * (o.X - p.X) + (o.Y - p.Y) * (o.Y - p.Y)));
        m.Add(new Medida("longitudinais: maior afastamento entre posições (cm)", longR.Max(p => Dist(p, longC)), longC.Max(p => Dist(p, longR))));
        m.Add(new Medida("longitudinais: comprimento", ComprimentoRevit(Um(BarRole.Longitudinal)), Un("long1")));

        // ---- estribos e grampos ----
        m.Add(new Medida("estribo: comprimento", ComprimentoRevit(Um(BarRole.Stirrup)), Un("estribo0")));
        m.Add(new Medida("estribo: quantidade", RevQtd(BarRole.Stirrup), FaixasQtd("estribo0")));
        // alturas de todos os estribos (cm): Revit pelos conjuntos; CAD pelas faixas (posições da regra de faixas da configuração)
        var (zR, zC) = AlturasEstribos(bars, r, cfg);
        m.Add(new Medida("estribo: maior afastamento entre alturas (cm)", Math.Round(Afast(zR, zC), 6), Math.Round(Afast(zC, zR), 6)));
        m.Add(new Medida("grampo: comprimento", ComprimentoRevit(Um(BarRole.CrossTie)), Un("grampo0")));
        m.Add(new Medida("grampo: quantidade", RevQtd(BarRole.CrossTie), FaixasQtd("grampo0")));

        // ---- alças de içamento (D30, item 5): alturas pela regra do CG e embutimento das pernas ----
        // CAD: o laço desenhado de lado nas vistas (ARM-ALCA, 6 vértices: pé, perna, topo, topo, perna, pé); centro = meio das
        // pernas; embutimento = do fundo da perna à face = trecho de perna − (altura acima da face − raio do laço), padrões 15 e 10/2
        var lacos = r.Contornos.Where(k => k.Layer == "ARM-ALCA" && k.Vertices.Count == 6).ToList();
        var zCad = lacos.Select(k => Math.Round((k.Vertices[1].Y + k.Vertices[4].Y) / 2, 6)).Distinct().OrderBy(z => z).ToList();
        double embCad = lacos.Select(k => Math.Abs(k.Vertices[2].X - k.Vertices[1].X) - (15 - 10 / 2.0)).Max();
        // Revit: o meio do topo de cada alça; embutimento = profundidade do lado de fora do pé, medida da face da alça
        var lifts = bars.Where(b => b.Role == BarRole.LiftLoop).ToList();
        var zRev = lifts.Select(b => Math.Round((b.Points[2].C + b.Points[3].C) / 2 * CmPorPe, 6)).Distinct().OrderBy(z => z).ToList();
        ColumnFace fl = ColumnRebarLayout.LiftFaceOf(f, k, s);
        double Prof(Loc p) => (fl == ColumnFace.PX ? f.Bx - p.A : fl == ColumnFace.NX ? p.A : fl == ColumnFace.PY ? f.By - p.B : p.B) * CmPorPe;
        double embRev = lifts.Max(b => Prof(b.Points[0])) + s.LiftDiameterMm / 20;
        m.Add(new Medida("alças: altura da de baixo (cm)", zRev.First(), zCad.First()));
        m.Add(new Medida("alças: altura da de cima (cm)", zRev.Last(), zCad.Last()));
        m.Add(new Medida("alças: embutimento das pernas (cm)", Math.Round(embRev, 6), Math.Round(embCad, 6)));
        m.Add(new Medida("alças: comprimento", ComprimentoRevit(lifts[0]), Un("alca")));
        m.Add(new Medida("alças: aço", lifts[0].Steel == Barra("alca").Aco ? 1 : 0, 1));
        // face (numeração apresentada; CAD: o parâmetro AlcaFace) e cordoalhas por ponto (CAD: AlcaPernas, a quantidade da barra)
        m.Add(new Medida("alças: face", ColumnRebarLayout.FaceNumber(fl), Valores(c, s).Inteiro("AlcaFace")));
        m.Add(new Medida("alças: cordoalhas por ponto", lifts.Count / Math.Max(1, zRev.Count), Barra("alca").Quantidade));

        if (c.Consolos == 0) return m;
        // ---- consolo ----
        bool duplo = c.Consolos == 2;
        string tir = duplo ? "cons1tir" : "cons1tirs";   // opostos em nível: tirante passante (D30)
        m.Add(new Medida("tirante: comprimento", ComprimentoRevit(Um(BarRole.CorbelTie)), Un(tir)));
        m.Add(new Medida("tirante: quantidade", RevQtd(BarRole.CorbelTie), Qtd(tir)));
        m.Add(new Medida("costura: comprimento", ComprimentoRevit(Um(BarRole.CorbelSew)), Un("cons1cost")));
        m.Add(new Medida("costura: quantidade", RevQtd(BarRole.CorbelSew), Qtd("cons1cost")));
        m.Add(new Medida("barra soldada: comprimento", ComprimentoRevit(Um(BarRole.CorbelAnchor)), Un("cons1tr")));
        m.Add(new Medida("estribo vertical do consolo: comprimento", ComprimentoRevit(Um(BarRole.CorbelStirrup)), Un("cons1vert")));
        m.Add(new Medida("armadura inferior do consolo: comprimento", ComprimentoRevit(Um(BarRole.CorbelBottom)), Un("cons1inf")));
        m.Add(new Medida("pino: comprimento", ComprimentoRevit(Um(BarRole.CorbelPin)), Un("cons1pino")));
        return m;
    }

    /// <summary>Alturas (cm) de todos os estribos: Revit pelos conjuntos; CAD pelas faixas (posições da regra de faixas da configuração).</summary>
    private static (List<double> Revit, List<double> Cad) AlturasEstribos(List<BarDef> bars, ResultadoTemplate r, ConfiguracaoArmadura cfg)
    {
        var zR = bars.Where(b => b.Role == BarRole.Stirrup).SelectMany(b => Enumerable.Range(0, b.Count).Select(i => (b.Points[0].C + i * b.Spacing) * CmPorPe)).ToList();
        var zC = r.Faixas.Where(x => x.Barra.StartsWith("estribo") && x.Tipo == TipoFaixa.Ortogonal).SelectMany(x =>
        {
            double comp = Math.Abs(x.P2.Y - x.P1.Y), y0 = Math.Min(x.P1.Y, x.P2.Y);
            int q = CalculoFaixa.Quantidade(comp, x.Espacamento, cfg.EspacamentoInicialFaixa, cfg.ModoQuantidadeFaixa);
            return CalculoFaixa.Posicoes(comp, q, x.Espacamento, cfg.EspacamentoInicialFaixa).Select(p => y0 + p);
        }).ToList();
        return (zR, zC);
    }

    private static double Afast(List<double> a, List<double> b) => a.Max(z => b.Min(w => Math.Abs(w - z)));

    /// <summary>Os valores do template para ler as linhas longitudinais (mesmo texto que <see cref="Cad"/> usa).</summary>
    private static ValoresTemplate Valores(Caso c, ColumnRebarSpec s)
    {
        var v = new ValoresTemplate(T.Parametros);
        int porFace = s.LongBarsAlongX - 2;
        v["Longitudinais"] = $"{TemplatePilarPreMoldado.ArrCantos}||{N(c.Phi)}|1||;" +
                             $"{TemplatePilarPreMoldado.ArrFaces24}|{porFace}|{N(c.Phi)}|1||;" +
                             $"{TemplatePilarPreMoldado.ArrFaces13}|{porFace}|{N(c.Phi)}|1||";
        return v;
    }

    // ------------------------------------------------------------------ estado travado (16/11/2026)

    private const string Igual = "";
    // causas das divergências conhecidas (tabela completa em docs/04_arquitetura/paridade_pilar.md)
    // ganchos: 135° nos dois desde a decisão D30 (16/11/2026); estribo, costura e estribo vertical do CAD saem de
    // GeradorEstribo.FechadoPadrao com gancho de 45° (ponta 5φt ≥ 5 cm), a mesma função do comprimento por eixo do Revit
    // divisa entre zonas: nenhum dos dois repete o estribo (EstribosNaZona). Desde a D30 (revisão) o Revit começa no cobrimento
    // das pontas, como o CAD, e as zonas saem da mesma função (MontarZonas: menor espaçamento onde se sobrepõem) — ver
    // ZonasSobrepostas
    // tirante e costura (D30): consolo sozinho, por dentro das longitudinais da face oposta (BarrasConsolo.FimNoPilar); opostos
    // em nível (BarrasConsolo.ParesEmNivel), passantes de ponta a ponta; perna do tirante medida por fora nos dois (D7).
    // Largura da costura (D30, revisão): por dentro dos estribos verticais nos dois (BarrasConsolo.LarguraCostura). A diferença
    // de −3 cm não vinha da entrada (cobrimento 2,5 e pinos de dobra iguais nos dois caminhos — ver LarguraDaCosturaEraRegra).
    // alça (D30): topo do laço em semicírculo nos dois (no Revit, trecho em arco no BarDef e Arc no vergalhão); comprimento
    // pelo eixo com o arco = o da polilinha do CAD: 2 pés + 2 pernas + π·entre/2
    // estribo vertical e armadura inferior do consolo: DEFINIDOS PELO USUÁRIO (D30, revisão: "é inserido pelo usuário") — não são
    // regra a unificar. CAD: no tipo de consolo do catálogo (ARMCONSOLOS: VertN, VertPhi, InfN, InfPhi, InfAnc, InfRetorno,
    // InfDescida); Revit: na especificação do grupo (quantidade e bitola). As formas que cada programa monta com essas entradas
    // são diferentes; a medida fica aqui só para travar o valor.
    private const string USUARIO = "definido pelo usuário (tipo de consolo no CAD; especificação do grupo no Revit) — não unificado";

    /// <summary>(caso, medida, Revit, CAD, causa). Comprimentos em cm (arredondados como na relação do aço); Igual = sem divergência.</summary>
    private static readonly (string Caso, string Item, double Revit, double Cad, string Causa)[] Esperado =
    {
        ("40x40-sem-12.5", "alças: altura da de baixo (cm)", 120, 120, Igual),
        ("40x40-sem-12.5", "alças: altura da de cima (cm)", 480, 480, Igual),
        ("40x40-sem-12.5", "alças: embutimento das pernas (cm)", 35.75, 35.75, Igual),
        ("40x40-sem-12.5", "alças: comprimento", 147, 147, Igual),
        ("40x40-sem-12.5", "alças: aço", 1, 1, Igual),
        ("40x40-sem-12.5", "alças: face", 2, 2, Igual),
        ("40x40-sem-12.5", "alças: cordoalhas por ponto", 2, 2, Igual),
        ("40x40-um-12.5", "alças: altura da de baixo (cm)", 122.6, 122.6, Igual),
        ("40x40-um-12.5", "alças: altura da de cima (cm)", 482.6, 482.6, Igual),
        ("40x40-um-12.5", "alças: embutimento das pernas (cm)", 35.75, 35.75, Igual),
        ("40x40-um-12.5", "alças: comprimento", 147, 147, Igual),
        ("40x40-um-12.5", "alças: aço", 1, 1, Igual),
        ("40x40-um-12.5", "alças: face", 2, 2, Igual),
        ("40x40-um-12.5", "alças: cordoalhas por ponto", 2, 2, Igual),
        ("40x40-opostos-16", "alças: altura da de baixo (cm)", 125, 125, Igual),
        ("40x40-opostos-16", "alças: altura da de cima (cm)", 485, 485, Igual),
        ("40x40-opostos-16", "alças: embutimento das pernas (cm)", 35.4, 35.4, Igual),
        ("40x40-opostos-16", "alças: comprimento", 147, 147, Igual),
        ("40x40-opostos-16", "alças: aço", 1, 1, Igual),
        ("40x40-opostos-16", "alças: face", 2, 2, Igual),
        ("40x40-opostos-16", "alças: cordoalhas por ponto", 2, 2, Igual),
        ("50x50-sem-16", "alças: altura da de baixo (cm)", 120, 120, Igual),
        ("50x50-sem-16", "alças: altura da de cima (cm)", 480, 480, Igual),
        ("50x50-sem-16", "alças: embutimento das pernas (cm)", 45.4, 45.4, Igual),
        ("50x50-sem-16", "alças: comprimento", 167, 167, Igual),
        ("50x50-sem-16", "alças: aço", 1, 1, Igual),
        ("50x50-sem-16", "alças: face", 2, 2, Igual),
        ("50x50-sem-16", "alças: cordoalhas por ponto", 2, 2, Igual),
        ("50x50-um-16", "alças: altura da de baixo (cm)", 121.7, 121.7, Igual),
        ("50x50-um-16", "alças: altura da de cima (cm)", 481.7, 481.7, Igual),
        ("50x50-um-16", "alças: embutimento das pernas (cm)", 45.4, 45.4, Igual),
        ("50x50-um-16", "alças: comprimento", 167, 167, Igual),
        ("50x50-um-16", "alças: aço", 1, 1, Igual),
        ("50x50-um-16", "alças: face", 2, 2, Igual),
        ("50x50-um-16", "alças: cordoalhas por ponto", 2, 2, Igual),
        ("50x50-opostos-12.5", "alças: altura da de baixo (cm)", 123.3, 123.3, Igual),
        ("50x50-opostos-12.5", "alças: altura da de cima (cm)", 483.3, 483.3, Igual),
        ("50x50-opostos-12.5", "alças: embutimento das pernas (cm)", 45.75, 45.75, Igual),
        ("50x50-opostos-12.5", "alças: comprimento", 167, 167, Igual),
        ("50x50-opostos-12.5", "alças: aço", 1, 1, Igual),
        ("50x50-opostos-12.5", "alças: face", 2, 2, Igual),
        ("50x50-opostos-12.5", "alças: cordoalhas por ponto", 2, 2, Igual),
        ("40x40-opostos-16", "armadura inferior do consolo: comprimento", 91, 79, USUARIO),
        ("40x40-opostos-16", "barra soldada: comprimento", 35, 35, Igual),
        ("40x40-opostos-16", "costura: comprimento", 252, 252, Igual),
        ("40x40-opostos-16", "costura: quantidade", 3, 3, Igual),
        ("40x40-opostos-16", "estribo vertical do consolo: comprimento", 131, 133, USUARIO),
        ("40x40-opostos-16", "estribo: comprimento", 153, 153, Igual),
        ("40x40-opostos-16", "estribo: maior afastamento entre alturas (cm)", 0, 0, Igual),
        ("40x40-opostos-16", "estribo: quantidade", 51, 51, Igual),
        ("40x40-opostos-16", "grampo: comprimento", 47, 47, Igual),
        ("40x40-opostos-16", "grampo: quantidade", 102, 102, Igual),
        ("40x40-opostos-16", "longitudinais: comprimento", 595, 595, Igual),
        ("40x40-opostos-16", "longitudinais: maior afastamento entre posições (cm)", 0, 0, Igual),
        ("40x40-opostos-16", "longitudinais: quantidade", 8, 8, Igual),
        ("40x40-opostos-16", "pino: comprimento", 74, 74, Igual),
        ("40x40-opostos-16", "tirante: comprimento", 85, 85, Igual),
        ("40x40-opostos-16", "tirante: quantidade", 3, 3, Igual),
        ("40x40-sem-12.5", "estribo: comprimento", 153, 153, Igual),
        ("40x40-sem-12.5", "estribo: maior afastamento entre alturas (cm)", 0, 0, Igual),
        ("40x40-sem-12.5", "estribo: quantidade", 45, 45, Igual),
        ("40x40-sem-12.5", "grampo: comprimento", 47, 47, Igual),
        ("40x40-sem-12.5", "grampo: quantidade", 90, 90, Igual),
        ("40x40-sem-12.5", "longitudinais: comprimento", 595, 595, Igual),
        ("40x40-sem-12.5", "longitudinais: maior afastamento entre posições (cm)", 0, 0, Igual),
        ("40x40-sem-12.5", "longitudinais: quantidade", 8, 8, Igual),
        ("40x40-um-12.5", "armadura inferior do consolo: comprimento", 92, 98, USUARIO),
        ("40x40-um-12.5", "barra soldada: comprimento", 35, 35, Igual),
        ("40x40-um-12.5", "costura: comprimento", 198, 198, Igual),
        ("40x40-um-12.5", "costura: quantidade", 3, 3, Igual),
        ("40x40-um-12.5", "estribo vertical do consolo: comprimento", 131, 133, USUARIO),
        ("40x40-um-12.5", "estribo: comprimento", 153, 153, Igual),
        ("40x40-um-12.5", "estribo: maior afastamento entre alturas (cm)", 0, 0, Igual),
        ("40x40-um-12.5", "estribo: quantidade", 51, 51, Igual),
        ("40x40-um-12.5", "grampo: comprimento", 47, 47, Igual),
        ("40x40-um-12.5", "grampo: quantidade", 102, 102, Igual),
        ("40x40-um-12.5", "longitudinais: comprimento", 595, 595, Igual),
        ("40x40-um-12.5", "longitudinais: maior afastamento entre posições (cm)", 0, 0, Igual),
        ("40x40-um-12.5", "longitudinais: quantidade", 8, 8, Igual),
        ("40x40-um-12.5", "pino: comprimento", 74, 74, Igual),
        ("40x40-um-12.5", "tirante: comprimento", 95, 95, Igual),
        ("40x40-um-12.5", "tirante: quantidade", 3, 3, Igual),
        ("50x50-opostos-12.5", "armadura inferior do consolo: comprimento", 102, 79, USUARIO),
        ("50x50-opostos-12.5", "barra soldada: comprimento", 35, 35, Igual),
        ("50x50-opostos-12.5", "costura: comprimento", 272, 272, Igual),
        ("50x50-opostos-12.5", "costura: quantidade", 3, 3, Igual),
        ("50x50-opostos-12.5", "estribo vertical do consolo: comprimento", 131, 133, USUARIO),
        ("50x50-opostos-12.5", "estribo: comprimento", 193, 193, Igual),
        ("50x50-opostos-12.5", "estribo: maior afastamento entre alturas (cm)", 0, 0, Igual),
        ("50x50-opostos-12.5", "estribo: quantidade", 51, 51, Igual),
        ("50x50-opostos-12.5", "grampo: comprimento", 57, 57, Igual),
        ("50x50-opostos-12.5", "grampo: quantidade", 102, 102, Igual),
        ("50x50-opostos-12.5", "longitudinais: comprimento", 595, 595, Igual),
        ("50x50-opostos-12.5", "longitudinais: maior afastamento entre posições (cm)", 0, 0, Igual),
        ("50x50-opostos-12.5", "longitudinais: quantidade", 8, 8, Igual),
        ("50x50-opostos-12.5", "pino: comprimento", 74, 74, Igual),
        ("50x50-opostos-12.5", "tirante: comprimento", 95, 95, Igual),
        ("50x50-opostos-12.5", "tirante: quantidade", 3, 3, Igual),
        ("50x50-sem-16", "estribo: comprimento", 193, 193, Igual),
        ("50x50-sem-16", "estribo: maior afastamento entre alturas (cm)", 0, 0, Igual),
        ("50x50-sem-16", "estribo: quantidade", 45, 45, Igual),
        ("50x50-sem-16", "grampo: comprimento", 57, 57, Igual),
        ("50x50-sem-16", "grampo: quantidade", 90, 90, Igual),
        ("50x50-sem-16", "longitudinais: comprimento", 595, 595, Igual),
        ("50x50-sem-16", "longitudinais: maior afastamento entre posições (cm)", 0, 0, Igual),
        ("50x50-sem-16", "longitudinais: quantidade", 8, 8, Igual),
        ("50x50-um-16", "armadura inferior do consolo: comprimento", 101, 98, USUARIO),
        ("50x50-um-16", "barra soldada: comprimento", 35, 35, Igual),
        ("50x50-um-16", "costura: comprimento", 217, 217, Igual),
        ("50x50-um-16", "costura: quantidade", 3, 3, Igual),
        ("50x50-um-16", "estribo vertical do consolo: comprimento", 131, 133, USUARIO),
        ("50x50-um-16", "estribo: comprimento", 193, 193, Igual),
        ("50x50-um-16", "estribo: maior afastamento entre alturas (cm)", 0, 0, Igual),
        ("50x50-um-16", "estribo: quantidade", 51, 51, Igual),
        ("50x50-um-16", "grampo: comprimento", 57, 57, Igual),
        ("50x50-um-16", "grampo: quantidade", 102, 102, Igual),
        ("50x50-um-16", "longitudinais: comprimento", 595, 595, Igual),
        ("50x50-um-16", "longitudinais: maior afastamento entre posições (cm)", 0, 0, Igual),
        ("50x50-um-16", "longitudinais: quantidade", 8, 8, Igual),
        ("50x50-um-16", "pino: comprimento", 74, 74, Igual),
        ("50x50-um-16", "tirante: comprimento", 105, 105, Igual),
        ("50x50-um-16", "tirante: quantidade", 3, 3, Igual),
    };

    [Theory]
    [MemberData(nameof(TodosOsCasos))]
    public void RevitECadComoTravado(Caso c)
    {
        var medidas = Medir(c);
        var esperadas = Esperado.Where(e => e.Caso == c.Nome).ToList();
        Assert.Equal(esperadas.Select(e => e.Item).OrderBy(x => x, StringComparer.Ordinal),
                     medidas.Select(x => x.Item).OrderBy(x => x, StringComparer.Ordinal));
        // todas as diferenças de uma vez (mais fácil de conferir quando uma regra muda de propósito)
        var erros = new List<string>();
        foreach (var x in medidas)
        {
            var e = esperadas.Single(q => q.Item == x.Item);
            string onde = $"{c.Nome} · {x.Item}";
            if (Math.Abs(e.Revit - x.Revit) > 1e-6) erros.Add($"{onde}: Revit {N(x.Revit)} (travado {N(e.Revit)})");
            if (Math.Abs(e.Cad - x.Cad) > 1e-6) erros.Add($"{onde}: CAD {N(x.Cad)} (travado {N(e.Cad)})");
            // divergência conhecida tem causa escrita; sem causa, os dois têm de ser iguais
            if (e.Causa == Igual && Math.Abs(x.Revit - x.Cad) > 1e-6) erros.Add($"{onde}: Revit {N(x.Revit)} × CAD {N(x.Cad)}");
            if (e.Causa != Igual && Math.Abs(x.Revit - x.Cad) < 1e-6) erros.Add($"{onde}: ficou igual — tire a divergência da tabela");
        }
        Assert.True(erros.Count == 0, string.Join(Environment.NewLine, erros));
    }

    /// <summary>
    /// Zonas que se sobrepõem (D30, decisão do dono): vale o MENOR espaçamento nos dois programas (TemplatePilarPreMoldado.MontarZonas).
    /// Base de 4 m c/10 e consolo de 3,5 a 4,2 m c/7,5: c/7,5 de 3,5 a 4,2 m nos dois; mesmas quantidades e alturas.
    /// </summary>
    [Fact]
    public void ZonasSobrepostas()
    {
        var c = Casos.First(x => x.Nome == "40x40-um-12.5");
        var (f, k, s) = Revit(c);
        s.BaseZoneLengthMm = 4000;
        var runs = ColumnRebarLayout.StirrupRuns(f, k, s);
        double EspRevit(double zCm) => runs.Where(r => r.Z * CmPorPe <= zCm + 1e-6).OrderBy(r => r.Z).Last().Spacing * CmPorPe;
        Assert.Equal(7.5, EspRevit(375), 6);
        var cfg = new ConfiguracaoArmadura { AlturaTextoMm = 2 };
        ResultadoTemplate r;
        TemplatePilarPreMoldado.Catalogo = TipoConsolo.Padrao().Concat(new[] { TipoDoRevit(s, false), TipoDoRevit(s, true) }).ToList();
        try { r = Cad(c, s, cfg); }
        finally { TemplatePilarPreMoldado.Catalogo = TipoConsolo.Padrao(); }
        Assert.Contains(r.Avisos, a => a.Contains("vale o menor espaçamento"));
        var fx = r.Faixas.Where(x => x.Barra.StartsWith("estribo") && x.Tipo == TipoFaixa.Ortogonal).ToList();
        Assert.Contains(fx, x => Math.Abs(Math.Min(x.P1.Y, x.P2.Y) - 350) < 1e-6 && Math.Abs(x.Espacamento - 7.5) < 1e-6);
        var (zR, zC) = AlturasEstribos(ColumnRebarLayout.Compute(f, k, s), r, cfg);
        Assert.Equal(zC.Count, zR.Count);
        Assert.Equal(0, Afast(zR, zC), 6);
        Assert.Equal(0, Afast(zC, zR), 6);
    }

    /// <summary>
    /// Consolos opostos (D30, revisão): em nível (0,6 cm) viram duplo com tirante e costura passantes; 1 cm de diferença no
    /// topo e cada um tem o seu, em L, até por dentro das longitudinais. Mesma função (BarrasConsolo.ParesEmNivel) no Revit.
    /// </summary>
    [Fact]
    public void OpostosSoViramDuploEmNivel()
    {
        var c = Casos.First(x => x.Nome == "40x40-opostos-16");
        var (f, k, s) = Revit(c);
        Assert.Single(ColumnRebarLayout.CorbelPairs(k));
        var passante = ColumnRebarLayout.Compute(f, k, s).Where(b => b.Role == BarRole.CorbelTie).ToList();
        Assert.Single(passante);
        Assert.Equal(85, ComprimentoRevit(passante[0]), 6);   // 2 × (25 − 2,5) + 40
        k[1].ZTop += U_.Mm(4); k[1].ZBottom += U_.Mm(4);
        k[0].ZTop += U_.Mm(4.5); k[0].ZBottom += U_.Mm(4.5);   // 0,5 mm: ainda em nível
        Assert.Single(ColumnRebarLayout.CorbelPairs(k));
        k[0].ZTop += U_.Mm(10); k[0].ZBottom += U_.Mm(10);     // 1 cm: níveis diferentes
        Assert.Empty(ColumnRebarLayout.CorbelPairs(k));
        var ls = ColumnRebarLayout.Compute(f, k, s).Where(b => b.Role == BarRole.CorbelTie).ToList();
        Assert.Equal(2, ls.Count);
        Assert.All(ls, b => Assert.Equal(95, ComprimentoRevit(b), 6));
    }

    /// <summary>
    /// A diferença de largura da costura (−3 cm no CAD antes da D30) era de regra, não de entrada: os dois caminhos recebem o
    /// mesmo cobrimento (2,5 cm) e usam os mesmos pinos de dobra (configuração padrão do núcleo: estribo ≤ 10 mm → 3φ); a
    /// largura do Revit era b − 2c = 35 e a do CAD b − 2c − 2φvert = 35 − 2 × 0,63 = 33,74.
    /// </summary>
    [Fact]
    public void LarguraDaCosturaEraRegra()
    {
        var c = Casos.First(x => x.Nome == "40x40-um-12.5");
        var (_, _, s) = Revit(c);
        var cfgCad = new ConfiguracaoArmadura { AlturaTextoMm = 2 };
        var cfgRevit = new ConfiguracaoArmadura();   // SharedRules do DEB.Revit
        Assert.Equal(cfgRevit.Dobramento.DiametroPino(s.SewDiameterMm, "CA-50", true), cfgCad.Dobramento.DiametroPino(s.SewDiameterMm, "CA-50", true));
        Assert.Equal(cfgRevit.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho45, s.SewDiameterMm, "CA-50", true),
                     cfgCad.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho45, s.SewDiameterMm, "CA-50", true));
        Assert.Equal(33.74, BarrasConsolo.LarguraCostura(CB, s.CoverMm / 10, s.CorbelStirrupDiameterMm, s.SewDiameterMm), 6);
    }

    /// <summary>
    /// Divisa entre zonas (D30, item 2): a zona que não é a última perde o estribo que cairia na divisa. Contas do caso sem
    /// consolo (L = 600, cobrimento das pontas 2,5): CAD base 2,5–100 c/10 → ⌊97,5/10⌋ + 1 = 10; corrente 100–550 c/15 → 450 é
    /// múltiplo de 15: 30 (antes 31, o 31º caía em 550 junto com o 1º do topo); topo 550–597,5 c/10 → 5. Total 45, como o
    /// Revit (5–95: 10; 100–535: 30; 550–590: 5).
    /// </summary>
    [Fact]
    public void EstribosSemRepetirNaDivisa()
    {
        var cfg = new ConfiguracaoArmadura();
        Assert.True(KitDesenho.RepeteNaDivisa(450, 15, cfg));
        Assert.False(KitDesenho.RepeteNaDivisa(97.5, 10, cfg));
        Assert.Equal(30, KitDesenho.EstribosNaZona(450, 15, false, cfg));
        Assert.Equal(31, KitDesenho.EstribosNaZona(450, 15, true, cfg));   // a última zona inclui a ponta
        Assert.Equal(10, KitDesenho.EstribosNaZona(97.5, 10, false, cfg));
        // ⌈L/s⌉ + 1 põe estribo nas duas pontas sempre: a zona intermediária perde um (100 c/15: 8 → 7)
        var teto = new ConfiguracaoArmadura { ModoQuantidadeFaixa = ModoQuantidadeFaixa.TetoMaisUm };
        Assert.Equal(7, KitDesenho.EstribosNaZona(100, 15, false, teto));
        // espaçamento inicial: as pontas ficam livres, nada se repete
        Assert.False(KitDesenho.RepeteNaDivisa(450, 15, new ConfiguracaoArmadura { EspacamentoInicialFaixa = 0.5 }));

        // no desenho do CAD: a faixa do trecho corrente termina no último estribo dele (535), a do topo começa em 550
        var c = Casos.First(x => x.Nome == "40x40-sem-12.5");
        var (_, _, s) = Revit(c);
        var r = Cad(c, s, new ConfiguracaoArmadura { AlturaTextoMm = 2 });
        var faixas = r.Faixas.Where(x => x.Barra == "estribo0" && x.Tipo == TipoFaixa.Ortogonal).OrderBy(x => x.P1.Y).ToList();
        Assert.Equal(new[] { (2.5, 100.0), (100.0, 535.0), (550.0, 597.5) }.Select(p => $"{N(p.Item1)}-{N(p.Item2)}"),
                     faixas.Select(x => $"{N(x.P1.Y)}-{N(x.P2.Y)}"));
    }

    /// <summary>
    /// Armadura por linha antes da troca no Revit (D32): a especificação simples (uma bitola, barras por face com os cantos)
    /// convertida em linhas (cantos + faces) e calculada pela função do núcleo que o CAD usa dá as MESMAS posições, bitolas e
    /// trecho que o Revit calcula hoje (<see cref="ColumnRebarLayout.LongitudinalPositions"/>, cobrimento das pontas).
    /// </summary>
    [Theory]
    [MemberData(nameof(TodosOsCasos))]
    public void ArmaduraPorLinhaIgualAoRevitDeHoje(Caso c)
    {
        var (f, _, s) = Revit(c);
        ConferirLinhasIgualAoRevit(f, s);
    }

    /// <summary>O mesmo com faces diferentes em x e em y e outras bitolas (as do SD_VARIAR do AutoRun e um retângulo).</summary>
    [Theory]
    [InlineData(40, 40, 16, 3, 3)]
    [InlineData(50, 50, 20, 2, 3)]
    [InlineData(30, 60, 12.5, 2, 4)]
    [InlineData(70, 40, 25, 4, 2)]
    public void ArmaduraPorLinhaIgualAoRevitDeHojeComFacesDiferentes(double b, double h, double phi, int nx, int ny)
    {
        var f = new ColumnBox(U_.Mm(b * 10), U_.Mm(h * 10), U_.Mm(L * 10));
        var s = ColumnRebarSpec.Default(f, Lemb * 10, new List<Corbel>());
        s.LongDiameterMm = phi; s.LongBarsAlongX = nx; s.LongBarsAlongY = ny; s.StirrupDiameterMm = 6.3;
        ConferirLinhasIgualAoRevit(f, s);
    }

    private static void ConferirLinhasIgualAoRevit(ColumnBox f, ColumnRebarSpec s)
    {
        var avisos = new List<string>();
        double hCm = f.H * CmPorPe, yIni = s.LongEndCoverMm / 10, yFim = hCm - s.LongEndCoverMm / 10;
        var linhas = ArmaduraPorLinha.Ler(ArmaduraPorLinha.DaArmaduraSimples(s.LongDiameterMm, s.LongBarsAlongX, s.LongBarsAlongY), yIni, yFim, avisos);
        Assert.Empty(avisos);
        var secao = DEB.Core.Geometria.Secao.SecaoTransversal.Retangular(f.Bx * CmPorPe, f.By * CmPorPe);
        var cob = DEB.Core.Geometria.Secao.FacesRetangulo.Cobrimentos(Enumerable.Repeat(s.CoverMm / 10, 4).ToArray());
        var pos = ArmaduraPorLinha.Posicoes(secao, cob, s.StirrupDiameterMm, linhas, 5);
        Assert.True(pos.Definido, pos.Erro);
        var porLinha = pos.PorLinha.Values.SelectMany(x => x).Select(x => (X: x.X, Y: x.Y, Phi: x.Bitola)).ToList();
        // "hoje" = a conta do Revit antes da armadura por linha (ColumnRebarLayout.LongitudinalPositions até 17/11/2026:
        // SecaoBarras.Posicoes com uma bitola e as intermediárias de cada face), congelada aqui
        int nx = Math.Max(2, s.LongBarsAlongX), ny = Math.Max(2, s.LongBarsAlongY);
        var hoje = SecaoBarras.Posicoes(secao, cob, s.StirrupDiameterMm, s.LongDiameterMm, 1, 0, s.LongDiameterMm, nx - 2, ny - 2, s.LongDiameterMm)
            .Barras.Select(p => (X: p.X, Y: p.Y)).ToList();
        // e a do Revit agora (pela armadura por linha) é a mesma
        var agora = ColumnRebarLayout.LongitudinalPositions(f, s).Select(p => (X: p.A * CmPorPe, Y: p.B * CmPorPe)).ToList();
        Assert.Equal(hoje.Count, agora.Count);
        foreach (var p in hoje) Assert.Contains(agora, q => Math.Abs(q.X - p.X) < 1e-6 && Math.Abs(q.Y - p.Y) < 1e-6);
        Assert.Equal(hoje.Count, porLinha.Count);
        foreach (var p in hoje)
            Assert.Contains(porLinha, q => Math.Abs(q.X - p.X) < 1e-6 && Math.Abs(q.Y - p.Y) < 1e-6 && q.Phi == s.LongDiameterMm);
        // nenhuma posição repetida (a correspondência é um para um)
        Assert.Equal(porLinha.Count, porLinha.Select(q => (Math.Round(q.X, 6), Math.Round(q.Y, 6))).Distinct().Count());
        // trecho: o mesmo das barras do Revit de hoje (cobrimento das pontas), sem feixe
        var longR = ColumnRebarLayout.Compute(f, new List<Corbel>(), s).Where(b => b.Role == BarRole.Longitudinal).ToList();
        Assert.All(linhas, l => { Assert.Equal(yIni, l.Ini, 9); Assert.Equal(yFim, l.Fim, 9); Assert.Equal(1, l.Feixe); });
        Assert.All(longR, b => { Assert.Equal(yIni, b.Points[0].C * CmPorPe, 6); Assert.Equal(yFim, b.Points[1].C * CmPorPe, 6); });
    }

    /// <summary>
    /// As longitudinais do Revit saem de SecaoBarras.Posicoes (16/11/2026) na mesma ordem e posição da conta antiga do plugin
    /// (filas a cobrimento + φt + φ/2, divididas por igual): o conjunto de barras do Revit nasce das duas primeiras.
    /// </summary>
    [Theory]
    [MemberData(nameof(TodosOsCasos))]
    public void LongitudinaisNaOrdemDeAntes(Caso c)
    {
        var (f, _, s) = Revit(c);
        double oL = U_.Mm(s.CoverMm + s.StirrupDiameterMm + s.LongDiameterMm / 2);
        List<double> Fila(double a, double b, int n) => Enumerable.Range(0, n).Select(i => a + (b - a) * i / (n - 1)).ToList();
        var xs = Fila(oL, f.Bx - oL, s.LongBarsAlongX);
        var ys = Fila(oL, f.By - oL, s.LongBarsAlongY);
        var antes = new List<(double A, double B)>();
        foreach (double x in xs) { antes.Add((x, ys[0])); antes.Add((x, ys[ys.Count - 1])); }
        foreach (double y in ys.Skip(1).Take(ys.Count - 2)) { antes.Add((xs[0], y)); antes.Add((xs[xs.Count - 1], y)); }
        var agora = ColumnRebarLayout.LongitudinalPositions(f, s);
        Assert.Equal(antes.Count, agora.Count);
        for (int i = 0; i < antes.Count; i++)
        {
            Assert.Equal(antes[i].A, agora[i].A, 9);
            Assert.Equal(antes[i].B, agora[i].B, 9);
        }
    }
}
