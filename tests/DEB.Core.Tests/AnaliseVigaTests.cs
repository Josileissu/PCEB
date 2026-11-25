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
}
