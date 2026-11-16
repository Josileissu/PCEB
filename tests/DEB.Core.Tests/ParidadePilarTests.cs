using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Calculo;
using DEB.Core.Configuracao;
using DEB.Core.Modelo;
using DEB.Core.Templates;
using DEB.Core.Templates.PilarPreMoldado;
using DEB.Core.Templates.PilarPreMoldado.Modelo3D;
using Xunit;

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
        string aco = b.DiameterMm < 6.3 ? "CA-60" : "CA-50";
        return ComprimentoPorEixo.Calcular(eixo, b.Closed, b.DiameterMm, aco, tie, b.Hooks135, new ConfiguracaoArmadura());
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
        m.Add(new Medida("grampo: comprimento", ComprimentoRevit(Um(BarRole.CrossTie)), Un("grampo0")));
        m.Add(new Medida("grampo: quantidade", RevQtd(BarRole.CrossTie), FaixasQtd("grampo0")));

        if (c.Consolos == 0) return m;
        // ---- consolo ----
        bool duplo = c.Consolos == 2;
        string tir = duplo ? "cons1tir" : "cons1tirs";
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
    private const string GANCHO = "estribo com ganchos de 135° no Revit (ponta 5φ ≥ 5 cm) × 90° no CAD (ponta 10φ ≥ 7 cm)";
    private const string DIVISA = "o CAD conta cada zona com piso(L/s) + 1 e repete o estribo na divisa corrente/topo quando o trecho é " +
                                  "múltiplo do espaçamento; o Revit começa a 5 cm das pontas e não repete na divisa";
    private const string TIR = "o Revit para o tirante por dentro das longitudinais da face oposta e mede a perna interna no eixo; " +
                               "o CAD vai até o cobrimento da face oposta e mede a perna por fora";
    private const string TIR_DUPLO = "consolos opostos: o Revit faz um tirante em L ancorado no pilar para cada consolo; o CAD faz um " +
                                     "tirante reto passante para o par (2a + b − 2c)";
    private const string COST = "costura: o Revit vai até as longitudinais da face oposta, largura b − 2c, ganchos de 135°; o CAD vai até o " +
                                "cobrimento, largura b − 2c − 2φvert, ganchos de 90°";
    private const string COST_DUPLO = "consolos opostos: o Revit faz uma costura por consolo; o CAD faz uma passante para o par";
    private const string VERT = "estribo vertical do consolo: o Revit fica abaixo do tirante (altura h − 2c − φtir), ganchos de 135°; o CAD " +
                                "usa h − 2c, ganchos de 90°";
    private const string INF = "formas diferentes: Revit em U com as pernas para cima entrando até as longitudinais; CAD com a perna na " +
                               "ponta, o trecho para dentro do pilar (ancoragem do tipo) e o retorno";

    /// <summary>(caso, medida, Revit, CAD, causa). Comprimentos em cm (arredondados como na relação do aço); Igual = sem divergência.</summary>
    private static readonly (string Caso, string Item, double Revit, double Cad, string Causa)[] Esperado =
    {
        ("40x40-opostos-16", "armadura inferior do consolo: comprimento", 91, 79, INF),
        ("40x40-opostos-16", "barra soldada: comprimento", 35, 35, Igual),
        ("40x40-opostos-16", "costura: comprimento", 200, 255, COST_DUPLO),
        ("40x40-opostos-16", "costura: quantidade", 6, 3, COST_DUPLO),
        ("40x40-opostos-16", "estribo vertical do consolo: comprimento", 131, 135, VERT),
        ("40x40-opostos-16", "estribo: comprimento", 153, 155, GANCHO),
        ("40x40-opostos-16", "estribo: quantidade", 51, 51, Igual),
        ("40x40-opostos-16", "grampo: comprimento", 47, 47, Igual),
        ("40x40-opostos-16", "grampo: quantidade", 102, 102, Igual),
        ("40x40-opostos-16", "longitudinais: comprimento", 595, 595, Igual),
        ("40x40-opostos-16", "longitudinais: maior afastamento entre posições (cm)", 0, 0, Igual),
        ("40x40-opostos-16", "longitudinais: quantidade", 8, 8, Igual),
        ("40x40-opostos-16", "pino: comprimento", 74, 74, Igual),
        ("40x40-opostos-16", "tirante: comprimento", 95, 85, TIR_DUPLO),
        ("40x40-opostos-16", "tirante: quantidade", 6, 3, TIR_DUPLO),
        ("40x40-sem-12.5", "estribo: comprimento", 153, 155, GANCHO),
        ("40x40-sem-12.5", "estribo: quantidade", 45, 46, DIVISA),
        ("40x40-sem-12.5", "grampo: comprimento", 47, 47, Igual),
        ("40x40-sem-12.5", "grampo: quantidade", 90, 92, DIVISA),
        ("40x40-sem-12.5", "longitudinais: comprimento", 595, 595, Igual),
        ("40x40-sem-12.5", "longitudinais: maior afastamento entre posições (cm)", 0, 0, Igual),
        ("40x40-sem-12.5", "longitudinais: quantidade", 8, 8, Igual),
        ("40x40-um-12.5", "armadura inferior do consolo: comprimento", 92, 98, INF),
        ("40x40-um-12.5", "barra soldada: comprimento", 35, 35, Igual),
        ("40x40-um-12.5", "costura: comprimento", 201, 205, COST),
        ("40x40-um-12.5", "costura: quantidade", 3, 3, Igual),
        ("40x40-um-12.5", "estribo vertical do consolo: comprimento", 131, 135, VERT),
        ("40x40-um-12.5", "estribo: comprimento", 153, 155, GANCHO),
        ("40x40-um-12.5", "estribo: quantidade", 51, 51, Igual),
        ("40x40-um-12.5", "grampo: comprimento", 47, 47, Igual),
        ("40x40-um-12.5", "grampo: quantidade", 102, 102, Igual),
        ("40x40-um-12.5", "longitudinais: comprimento", 595, 595, Igual),
        ("40x40-um-12.5", "longitudinais: maior afastamento entre posições (cm)", 0, 0, Igual),
        ("40x40-um-12.5", "longitudinais: quantidade", 8, 8, Igual),
        ("40x40-um-12.5", "pino: comprimento", 74, 74, Igual),
        ("40x40-um-12.5", "tirante: comprimento", 95, 97, TIR),
        ("40x40-um-12.5", "tirante: quantidade", 3, 3, Igual),
        ("50x50-opostos-12.5", "armadura inferior do consolo: comprimento", 102, 79, INF),
        ("50x50-opostos-12.5", "barra soldada: comprimento", 35, 35, Igual),
        ("50x50-opostos-12.5", "costura: comprimento", 221, 275, COST_DUPLO),
        ("50x50-opostos-12.5", "costura: quantidade", 6, 3, COST_DUPLO),
        ("50x50-opostos-12.5", "estribo vertical do consolo: comprimento", 131, 135, VERT),
        ("50x50-opostos-12.5", "estribo: comprimento", 193, 195, GANCHO),
        ("50x50-opostos-12.5", "estribo: quantidade", 51, 51, Igual),
        ("50x50-opostos-12.5", "grampo: comprimento", 57, 57, Igual),
        ("50x50-opostos-12.5", "grampo: quantidade", 102, 102, Igual),
        ("50x50-opostos-12.5", "longitudinais: comprimento", 595, 595, Igual),
        ("50x50-opostos-12.5", "longitudinais: maior afastamento entre posições (cm)", 0, 0, Igual),
        ("50x50-opostos-12.5", "longitudinais: quantidade", 8, 8, Igual),
        ("50x50-opostos-12.5", "pino: comprimento", 74, 74, Igual),
        ("50x50-opostos-12.5", "tirante: comprimento", 105, 95, TIR_DUPLO),
        ("50x50-opostos-12.5", "tirante: quantidade", 6, 3, TIR_DUPLO),
        ("50x50-sem-16", "estribo: comprimento", 193, 195, GANCHO),
        ("50x50-sem-16", "estribo: quantidade", 45, 46, DIVISA),
        ("50x50-sem-16", "grampo: comprimento", 57, 57, Igual),
        ("50x50-sem-16", "grampo: quantidade", 90, 92, DIVISA),
        ("50x50-sem-16", "longitudinais: comprimento", 595, 595, Igual),
        ("50x50-sem-16", "longitudinais: maior afastamento entre posições (cm)", 0, 0, Igual),
        ("50x50-sem-16", "longitudinais: quantidade", 8, 8, Igual),
        ("50x50-um-16", "armadura inferior do consolo: comprimento", 101, 98, INF),
        ("50x50-um-16", "barra soldada: comprimento", 35, 35, Igual),
        ("50x50-um-16", "costura: comprimento", 220, 225, COST),
        ("50x50-um-16", "costura: quantidade", 3, 3, Igual),
        ("50x50-um-16", "estribo vertical do consolo: comprimento", 131, 135, VERT),
        ("50x50-um-16", "estribo: comprimento", 193, 195, GANCHO),
        ("50x50-um-16", "estribo: quantidade", 51, 51, Igual),
        ("50x50-um-16", "grampo: comprimento", 57, 57, Igual),
        ("50x50-um-16", "grampo: quantidade", 102, 102, Igual),
        ("50x50-um-16", "longitudinais: comprimento", 595, 595, Igual),
        ("50x50-um-16", "longitudinais: maior afastamento entre posições (cm)", 0, 0, Igual),
        ("50x50-um-16", "longitudinais: quantidade", 8, 8, Igual),
        ("50x50-um-16", "pino: comprimento", 74, 74, Igual),
        ("50x50-um-16", "tirante: comprimento", 105, 107, TIR),
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
        foreach (var x in medidas)
        {
            var e = esperadas.Single(q => q.Item == x.Item);
            string onde = $"{c.Nome} · {x.Item}";
            Assert.True(Math.Abs(e.Revit - x.Revit) < 1e-6, $"{onde}: Revit {N(x.Revit)} (travado {N(e.Revit)})");
            Assert.True(Math.Abs(e.Cad - x.Cad) < 1e-6, $"{onde}: CAD {N(x.Cad)} (travado {N(e.Cad)})");
            // divergência conhecida tem causa escrita; sem causa, os dois têm de ser iguais
            if (e.Causa == Igual) Assert.True(Math.Abs(x.Revit - x.Cad) < 1e-6, $"{onde}: Revit {N(x.Revit)} × CAD {N(x.Cad)}");
            else Assert.True(Math.Abs(x.Revit - x.Cad) > 1e-6, $"{onde}: ficou igual — tire a divergência da tabela");
        }
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
