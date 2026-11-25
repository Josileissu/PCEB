using System;
using System.Linq;
using DEB.Core.Elementos.Viga.PreMoldada;
using DEB.Core.Elementos.Viga.PreMoldada.Calculo;
using DEB.Core.Motor;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Análise da viga pré-moldada no CAD: modelos rotulado e semirrígido (αR, NBR 9062), casos PP/CP/SC, pontuais e envoltória.</summary>
[Collection("Pilar")]
public class AnaliseVigaTests
{
    private static ValoresTemplate Viga(Action<ValoresTemplate> ajustar = null)
    {
        var v = new ValoresTemplate(new TemplateVigaPreMoldada().Parametros);
        v["B"] = "20"; v["H"] = "60"; v["L"] = "600"; v["ExtE"] = "Reta"; v["ExtD"] = "Reta"; v["Fck"] = "40";
        ajustar?.Invoke(v);
        return v;
    }

    private static double MApoio(ModeloAnaliseViga m, CasoViga c, double x) => m.Casos[c].OrderBy(p => Math.Abs(p.X - x)).First().M;

    [Fact]
    public void RotuladoIgualABiapoiadaComPontuais()
    {
        var ent = new EntradaAnaliseViga
        {
            Semirrigido = false, ApoioE = 15, ApoioD = 15, VigaCP = 10,
            Pontuais = { new CargaConcentrada { X = 250, G = 30, Q = 12 } },
        };
        var r = AnaliseViga.Rodar(Viga(), ent);
        var m = Assert.Single(r.Modelos);
        var est = new EstaticaViga(600, 15, 585, 10.0 / 100, new[] { (250.0, 30.0) });
        foreach (var p in m.Casos[CasoViga.CP].Where(p => p.X > 1 && p.X < 599))
            Assert.Equal(est.M(p.X) / 100, p.M, 2);   // os pontos a x + 0,001 repetem o M do nó
        var rCP = m.Reacoes[CasoViga.CP];
        Assert.Equal(est.Ra, rCP.E, 3);
        Assert.Equal(est.Rb, rCP.D, 3);
        // cortante: salto de 30 kN na pontual
        Assert.Equal(est.V(250 - 1e-6), m.Casos[CasoViga.CP].First(p => Math.Abs(p.X - 250) < 1e-6).V, 3);
        Assert.Equal(est.V(250 + 1e-6), m.Casos[CasoViga.CP].First(p => Math.Abs(p.X - 250.001) < 1e-6).V, 3);
        // SC: só a parcela Q da pontual
        Assert.Equal(12 * (585 - 250) / 570.0, m.Reacoes[CasoViga.SC].E, 3);
    }

    [Theory]
    [InlineData(0.10)]
    [InlineData(0.50)]
    [InlineData(1.00)]
    public void SemirrigidoPeloAlfaR(double alfa)
    {
        // apoios nas pontas, seção constante, carga uniforme: M no apoio = q·ℓ²/12 · 3αR/(2 + αR) (αR = 1: engaste, q·ℓ²/12)
        var ent = new EntradaAnaliseViga { Rotulado = false, AlfaRE = alfa, AlfaRD = alfa, ApoioE = 0.0005, ApoioD = 0.0005, VigaCP = 12 };
        var r = AnaliseViga.Rodar(Viga(), ent);
        var m = Assert.Single(r.Modelos);
        double q = 12, l = 6, esperado = -q * l * l / 12 * 3 * alfa / (2 + alfa);
        Assert.Equal(esperado, MApoio(m, CasoViga.CP, 0), 1);
        Assert.Equal(esperado, MApoio(m, CasoViga.CP, 600), 1);
        Assert.Equal(q * l * l / 8 + esperado, MApoio(m, CasoViga.CP, 300), 1);
    }

    [Fact]
    public void EnvoltoriaPegaOPiorDosDoisModelos()
    {
        var ent = new EntradaAnaliseViga
        {
            AlfaRE = 0.1, AlfaRD = 0.1, ApoioE = 15, ApoioD = 15,
            LajePP = { Valor = 5, VaoInfluencia = 5 }, LajeCP = { Valor = 2, VaoInfluencia = 5 }, LajeSC = { Valor = 6, VaoInfluencia = 5 },
        };
        var r = AnaliseViga.Rodar(Viga(), ent);
        Assert.Equal(2, r.Modelos.Count);
        Assert.Equal(25, r.Linear[CasoViga.PP] - r.PPViga, 6);   // 5 kN/m² × 5 m
        Assert.Equal(30, r.Linear[CasoViga.SC], 6);
        var rot = r.Modelos[0]; var semi = r.Modelos[1];
        Assert.Equal(rot.Elu.Max(p => p.M), r.MdMax, 6);      // positivo: rotulado
        Assert.Equal(semi.Elu.Min(p => p.M), r.MdMin, 6);     // negativo no apoio: semirrígido
        Assert.True(r.MdMin < -1);
        Assert.True(semi.Elu.Max(p => p.M) < r.MdMax);
    }

    [Fact]
    public void VigaComDenteEAbaTemTrechosEEquilibrio()
    {
        // V101 do caso de teste: 19 × 150, L = 602, dentes de 25 × 75, aba de 20 a 20 cm do topo
        var v = Viga(x =>
        {
            x["B"] = "19"; x["H"] = "150"; x["L"] = "602"; x["Fck"] = "30";
            x["ExtE"] = "DG 75x25"; x["ExtD"] = "DG 75x25";
            x["Aba"] = TemplateVigaPreMoldada.AbaEsq; x["AbaLargura"] = "20"; x["AbaAltura"] = "23.5"; x["AbaAlturaPonta"] = "15"; x["AbaBase"] = "106.5";
        });
        var ent = new EntradaAnaliseViga { VigaCP = 9.9, LajePP = { Valor = 5, VaoInfluencia = 5 }, Pontuais = { new CargaConcentrada { X = 301, G = 20 } } };
        var cat = new[] { new TipoDente { Nome = "DG 75x25", Altura = 75, Comprimento = 25 } };
        var r = AnaliseViga.Rodar(v, ent, null, cat);
        Assert.Contains(r.Trechos, t => t.Secao.StartsWith("dente"));
        foreach (var m in r.Modelos)
        {
            var (e, d) = m.Reacoes[CasoViga.CP];
            Assert.Equal(9.9 * 6.02 + 20, e + d, 2);
            var (pe, pd) = m.Reacoes[CasoViga.PP];
            Assert.Equal(r.Linear[CasoViga.PP] * 6.02, pe + pd, 2);
        }
        var dente = r.Trechos.First(t => t.Secao.StartsWith("dente"));
        var vao = r.Trechos.First(t => !t.Secao.StartsWith("dente"));
        Assert.True(dente.I < vao.I / 4);
    }
    [Fact]
    public void V101ConferidaComPyNite()
    {
        // caso do arquivo de estudo (V101, 19 × 150, L = 602, dentes 75 × 25, aba de laje): mesmo modelo montado à parte no
        // PyNite 3.2.1 (25/11/2026) — Md,máx = 524,74 kN·m (rotulado), Md,mín = −49,85 kN·m (semirrígido, αR = 0,10), |Vd| = 363,65 kN
        var v = new ValoresTemplate(new TemplateVigaPreMoldada().Parametros);
        v["B"] = "19"; v["H"] = "150"; v["L"] = "602"; v["Fck"] = "30"; v["ExtE"] = "DG 75x25"; v["ExtD"] = "DG 75x25";
        v["Aba"] = TemplateVigaPreMoldada.AbaEsq; v["AbaLargura"] = "20"; v["AbaAltura"] = "23.5"; v["AbaAlturaPonta"] = "15"; v["AbaBase"] = "106.5";
        var cat = new[] { new TipoDente { Nome = "DG 75x25", Altura = 75, Comprimento = 25 } };
        var ent = new EntradaAnaliseViga
        {
            ApoioE = 11.5, ApoioD = 11.5, AlfaRE = 0.1, AlfaRD = 0.1, VigaCP = 9.9,
            LajePP = { Valor = 5, VaoInfluencia = 5 }, LajeCP = { Valor = 2, VaoInfluencia = 5 }, LajeSC = { Valor = 6, VaoInfluencia = 5 },
            Pontuais = { new CargaConcentrada { X = 200, G = 20, Q = 10 } },
        };
        var r = AnaliseViga.Rodar(v, ent, null, cat);
        Assert.InRange(r.MdMax, 524.74 - 0.05, 524.74 + 0.05);
        Assert.InRange(r.MdMin, -49.85 - 0.05, -49.85 + 0.05);
        Assert.InRange(r.VdMax, 363.65 - 0.05, 363.65 + 0.05);
        Assert.Equal(73.469, r.Modelos[1].Reacoes[CasoViga.CP].E, 2);
    }
    [Fact]
    public void RigidezSecanteEFlechaComFissuracao()
    {
        // apoio na ponta por padrão; Rsec pelo αR com (EI)sec = 0,4·Eci·Ic; flecha total = elástica × Ic/Ie × (1 + αf)
        var ent = new EntradaAnaliseViga { AlfaRE = 0.1, AlfaRD = 0.1, VigaCP = 25, VigaSC = 10 };
        var r = AnaliseViga.Rodar(Viga(), ent);
        Assert.Equal(0, r.ApoioE);
        Assert.Equal(600, r.Vao, 6);
        double eci = 5600 * Math.Sqrt(40) / 10;
        Assert.Equal(0.4 * eci * 20 * Math.Pow(60, 3) / 12, r.EIsec, 0);
        var semi = r.Modelos[1];
        Assert.Equal(3 * r.EIsec / (600 * (1 / 0.1 - 1)) / 100, semi.RsecE, 3);
        Assert.True(r.As > 0);
        Assert.NotEmpty(semi.FlechaTotal);
        double el = semi.Els.Max(p => p.Flecha), tot = semi.FlechaTotal.Max(p => p.Flecha);
        Assert.True(tot > el);
        Assert.Equal(el * r.Ic / semi.Ie * (1 + r.AlfaF), tot, 6);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DesenhoDaAnaliseTemModeloTabelasEDiagramas(bool semirrigido)
    {
        var ent = new EntradaAnaliseViga
        {
            Semirrigido = semirrigido, VigaCP = 9.9, LajeSC = { Valor = 6, VaoInfluencia = 5 },
            Pontuais = { new CargaConcentrada { X = 200, G = 20, Q = 10 } },
        };
        var v = Viga();
        var r = AnaliseViga.Rodar(v, ent);
        var d = DesenhoAnaliseViga.Gerar(r, ent, v);
        var layers = d.Contornos.Select(c => c.Layer).ToHashSet();
        foreach (var l in new[] { DesenhoAnaliseViga.LBarra, DesenhoAnaliseViga.LSuporte, DesenhoAnaliseViga.LCargas, DesenhoAnaliseViga.LSecao,
                                  DesenhoAnaliseViga.LCortante, DesenhoAnaliseViga.LFletor, DesenhoAnaliseViga.LDeslocada, DesenhoAnaliseViga.LDeslocadaTotal })
            Assert.Contains(l, layers);
        Assert.Equal(semirrigido, layers.Contains(DesenhoAnaliseViga.LSemi));
        Assert.Equal(new[] { "CARGA DA LAJE", "CARGA DA VIGA", "CARGAS PONTUAIS", "PARÂMETROS DA ANÁLISE", "MODELOS (ENVOLTÓRIA)" }, d.Tabelas.Select(t => t.Titulo));
        Assert.Equal(semirrigido ? 3 : 2, d.Tabelas[4].Linhas.Count);
        // os diagramas ficam abaixo das seções (nada do Vd sobe até elas)
        double fundoSecoes = d.Contornos.Where(c => c.Layer == DesenhoAnaliseViga.LSecao).Min(c => c.Vertices.Min(p => p.Y));
        double topoVd = d.Contornos.Where(c => c.Layer == DesenhoAnaliseViga.LCortante).Max(c => c.Vertices.Max(p => p.Y));
        Assert.True(topoVd < fundoSecoes);
        Assert.DoesNotContain(d.Textos, t => t.Texto == "-0");
    }
    [Fact]
    public void TabelasDoDesenhoVoltamComoEntrada()
    {
        // o que o desenho escreve (em tf/kgf, as unidades do escritório) é lido de volta igual — é por elas que o engenheiro digita
        var ent = new EntradaAnaliseViga
        {
            ApoioE = 5, ApoioD = 7.5, AlfaRE = 0.15, AlfaRD = 0.2, Rotulado = true, Semirrigido = true, FatorRigidezSecante = 0.5, Torcao = false,
            VigaCP = 9.9, VigaSC = 1.5, LajePP = { Valor = 5, VaoInfluencia = 5 }, LajeCP = { Valor = 2, VaoInfluencia = 4.5 }, LajeSC = { Valor = 6, VaoInfluencia = 5 },
            Pontuais = { new CargaConcentrada { X = 200, G = 20, Q = 10 }, new CargaConcentrada { X = 410, G = 7 } },
        };
        var v = Viga();
        var d = DesenhoAnaliseViga.Gerar(AnaliseViga.Rodar(v, ent), ent, v);
        var erros = new System.Collections.Generic.List<string>();
        var lida = TabelasAnaliseViga.Ler(d.Tabelas.Select(t => (t.Titulo, t.Linhas)), erros);
        Assert.Empty(erros);
        Assert.Equal(5, lida.ApoioE, 6); Assert.Equal(7.5, lida.ApoioD, 6);
        Assert.Equal(0.15, lida.AlfaRE, 6); Assert.Equal(0.2, lida.AlfaRD, 6);
        Assert.Equal(0.5, lida.FatorRigidezSecante, 6);
        Assert.False(lida.Torcao);
        Assert.Equal(9.9, lida.VigaCP, 2); Assert.Equal(1.5, lida.VigaSC, 2);
        Assert.Equal(5, lida.LajePP.Valor, 2); Assert.Equal(4.5, lida.LajeCP.VaoInfluencia, 6); Assert.Equal(6, lida.LajeSC.Valor, 2);
        Assert.Equal(2, lida.Pontuais.Count);
        Assert.Equal(20, lida.Pontuais[0].G, 2); Assert.Equal(10, lida.Pontuais[0].Q, 2); Assert.Equal(410, lida.Pontuais[1].X, 6);
    }

    [Fact]
    public void TabelasDigitadasEmOutraUnidadeEComErros()
    {
        var tabs = new[]
        {
            ("CARGA DA LAJE", new System.Collections.Generic.List<string[]> { new[] { "TIPO" }, new[] { "PP,laje", "500", "kgf/m²", "5", "m" }, new[] { "SC,laje", "3", "kN/m²", "4", "m" } }),
            ("CARGA DA VIGA", new System.Collections.Generic.List<string[]> { new[] { "TIPO" }, new[] { "CP,viga", "abc", "kN/m" } }),
            ("PARÂMETROS DA ANÁLISE", new System.Collections.Generic.List<string[]> { new[] { "P" }, new[] { TabelasAnaliseViga.PAlfaE, "10", "" } }),
        };
        var erros = new System.Collections.Generic.List<string>();
        var e = TabelasAnaliseViga.Ler(tabs, erros);
        Assert.Equal(500 * 0.00980665, e.LajePP.Valor, 4);
        Assert.Equal(12, e.LajeSC.Linear, 6);
        Assert.Equal(0.10, e.AlfaRE, 6);   // 10 % → 0,10
        Assert.Contains(erros, x => x.Contains("abc"));
    }
    // ---------------- etapa 4, parte 1: longitudinais pela envoltória (positiva, negativa, pele) ----------------

    private static (ValoresTemplate V, TipoDente[] Cat) V101()
    {
        var v = new ValoresTemplate(new TemplateVigaPreMoldada().Parametros);
        v["Nome"] = "V101"; v["B"] = "19"; v["H"] = "150"; v["L"] = "602"; v["Fck"] = "30"; v["ExtE"] = "DG 75x25"; v["ExtD"] = "DG 75x25";
        v["Aba"] = TemplateVigaPreMoldada.AbaEsq; v["AbaLargura"] = "20"; v["AbaAltura"] = "23.5"; v["AbaAlturaPonta"] = "15"; v["AbaBase"] = "106.5";
        return (v, new[] { new TipoDente { Nome = "DG 75x25", Altura = 75, Comprimento = 25 } });
    }

    private static EntradaAnaliseViga CargasV101(double alfa = 0.2) => new EntradaAnaliseViga
    {
        AlfaRE = alfa, AlfaRD = alfa, VigaCP = 9.9,
        LajePP = { Valor = 5, VaoInfluencia = 5 }, LajeCP = { Valor = 2, VaoInfluencia = 5 }, LajeSC = { Valor = 6, VaoInfluencia = 5 },
    };

    [Fact]
    public void CalculoComAnaliseArmaNegativasNasPontas()
    {
        var (v, cat) = V101();
        var ent = CargasV101();
        var an = AnaliseViga.Rodar(v, ent, null, cat);
        var r = CalculoViga.Calcular(v, AnaliseViga.CargasParaCalculo(ent, v, cat), null, cat, an);
        // esforços da envoltória
        Assert.Equal(an.MdMax, r.MdMax, 6);
        Assert.Equal(an.MdMin, r.MdMin, 6);
        Assert.True(r.MdMin < -10);
        Assert.Equal(an.VdMax, Math.Max(r.VdE, r.VdD), 6);
        // uma negativa em cada ponta, com área ≥ a necessária e comprimento além do fim do negativo + aℓ
        Assert.Equal(2, r.Negativas.Count);
        foreach (var ng in r.Negativas)
        {
            Assert.True(ng.N >= 2);
            Assert.True(ng.N * Math.PI * ng.Bitola * ng.Bitola / 400 >= ng.AsNec - 1e-9);
            var lado = r.DiagramaNegativo.Where(p => ng.Lado == "esquerda" ? p.X <= 301 : p.X > 301).ToList();
            double fimNeg = ng.Lado == "esquerda" ? lado.Max(p => p.X) : 602 - lado.Min(p => p.X);
            Assert.True(ng.Fim - ng.Ini >= fimNeg + r.Al - 2.5);
        }
        Assert.Contains(r.Verificacoes, x => x.Codigo == "K22");
        Assert.Contains(r.Memoria, m => m.Titulo.StartsWith("5b."));
        // a proposta leva as negativas, e o template as desenha como ARMADURA NEGATIVA
        var linhas = r.Proposta.Tabela("Longitudinais");
        Assert.Equal(2, linhas.Count(l => l[0] == TemplateVigaPreMoldada.FNegativa));
        var catAntes = TemplateVigaPreMoldada.CatalogoDentes;
        try
        {
            TemplateVigaPreMoldada.CatalogoDentes = cat.ToList();
            var des = new TemplateVigaPreMoldada().Gerar(r.Proposta, new DEB.Core.Configuracao.ConfiguracaoArmadura());
            Assert.Contains(des.Textos, t => t.Texto.Contains("ARMADURA NEGATIVA"));
        }
        finally { TemplateVigaPreMoldada.CatalogoDentes = catAntes; }
    }

    [Fact]
    public void SoRotuladoNaoTemNegativaENemSomaComASuperior()
    {
        var (v, cat) = V101();
        var ent = CargasV101();
        ent.Semirrigido = false;
        var an = AnaliseViga.Rodar(v, ent, null, cat);
        var r = CalculoViga.Calcular(v, AnaliseViga.CargasParaCalculo(ent, v, cat), null, cat, an);
        Assert.Empty(r.Negativas);
        Assert.DoesNotContain(r.Proposta.Tabela("Longitudinais"), l => l[0] == TemplateVigaPreMoldada.FNegativa);
        // com o semirrígido, a superior do template continua a mesma (a negativa não é reforço dela)
        var ent2 = CargasV101();
        var r2 = CalculoViga.Calcular(v, AnaliseViga.CargasParaCalculo(ent2, v, cat), null, cat, AnaliseViga.Rodar(v, ent2, null, cat));
        string Sup(ResultadoCalculoViga x) => string.Join(";", x.Proposta.Tabela("Longitudinais").Where(l => l[0] == TemplateVigaPreMoldada.FSuperior).Select(l => l[1] + "x" + l[2]));
        Assert.Equal(Sup(r), Sup(r2));
    }

    [Fact]
    public void NaSecaoSuperioresNosCantosENegativasEntreElas()
    {
        var sup = new LinhaLongViga { Linha = 1, Funcao = TemplateVigaPreMoldada.FSuperior, Quantidade = 2, Bitola = 10 };
        var neg = new LinhaLongViga { Linha = 2, Funcao = TemplateVigaPreMoldada.FNegativa, Quantidade = 3, Bitola = 16 };
        var todas = new[] { sup, neg };
        var ps = TemplateVigaPreMoldada.PontosNaSecao(sup, 30, 70, 3, 3, 3, 6.3, todas);
        var pn = TemplateVigaPreMoldada.PontosNaSecao(neg, 30, 70, 3, 3, 3, 6.3, todas);
        Assert.Equal(2, ps.Count); Assert.Equal(3, pn.Count);
        Assert.True(pn.Min(p => p.X) > ps.Min(p => p.X) + 1e-6 && pn.Max(p => p.X) < ps.Max(p => p.X) - 1e-6);
        Assert.Empty(ps.Select(p => Math.Round(p.X, 3)).Intersect(pn.Select(p => Math.Round(p.X, 3))));
    }
}
