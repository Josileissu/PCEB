using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Templates;
using DEB.Core.Templates.VigaPreMoldada;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>VP-4, 1ª parte (2026-11-09): viga com aba de apoio de laje — L (um lado) e T invertido (dois lados).</summary>
[Collection("Pilar")]   // usa o contexto de desenho e o catálogo estático
public class VigaAbaTests
{
    private static readonly TemplateVigaPreMoldada T = new TemplateVigaPreMoldada();

    private static ResultadoTemplate Gerar(Action<ValoresTemplate> ajustar)
    {
        var v = new ValoresTemplate(T.Parametros);
        ajustar?.Invoke(v);
        Assert.Empty(CatalogoTemplates.Validar(T, v));
        return T.Gerar(v, new ConfiguracaoArmadura());
    }

    private static bool TemCota(ResultadoTemplate r, double medida) =>
        r.Cotas.Any(k => Math.Abs(Math.Sqrt(Math.Pow(k.P2.X - k.P1.X, 2) + Math.Pow(k.P2.Y - k.P1.Y, 2)) - medida) < 1e-6);

    private static readonly ExtremidadeViga Reta = new ExtremidadeViga();

    [Fact]
    public void SemAbaNadaMuda()
    {
        var r = Gerar(null);
        Assert.DoesNotContain(r.Barras, b => b.Nome.StartsWith("aba"));
        Assert.DoesNotContain(r.Avisos, a => a.Contains("Aba"));
    }

    [Fact]
    public void ContornoDaSecaoLTInvertidoEAbaElevada()
    {
        var l = TemplateVigaPreMoldada.ContornoSecao(40, 50, new AbaViga { Direita = true, Largura = 15, Altura = 20, Ini = 0, Fim = 600 }, 0, 0);
        Assert.Equal(new[] { (0.0, 0.0), (55.0, 0.0), (55.0, 20.0), (40.0, 20.0), (40.0, 50.0), (0.0, 50.0) }, l);
        var t = TemplateVigaPreMoldada.ContornoSecao(35, 40, new AbaViga { Esquerda = true, Direita = true, Largura = 15, Altura = 20, Ini = 0, Fim = 600 }, 0, 0);
        Assert.Equal(8, t.Count);
        Assert.Equal((-15.0, 50.0), (t.Min(p => p.X), t.Max(p => p.X)));
        var elevada = TemplateVigaPreMoldada.ContornoSecao(26, 170, new AbaViga { Esquerda = true, Largura = 25, Altura = 20, Base = 60, Ini = 0, Fim = 356 }, 0, 0);
        Assert.Equal(8, elevada.Count);
        Assert.Equal(4, TemplateVigaPreMoldada.ContornoSecao(20, 50, null, 0, 0).Count);
    }

    /// <summary>V129 da prancha 316-6208: 40 × 50, L com aba de laje, dentes Gerber — a aba vai de dente a dente.</summary>
    [Fact]
    public void VigaLComDentes()
    {
        var r = Gerar(v =>
        {
            v["Nome"] = "V129"; v["B"] = "40"; v["H"] = "50"; v["L"] = "600"; v["ExtE"] = "DG 45x25"; v["ExtD"] = "DG 45x25";
            v["Aba"] = TemplateVigaPreMoldada.AbaDir; v["AbaLargura"] = "15"; v["AbaAltura"] = "20";
        });
        // cotas da aba: balanço 15 (planta e seção) e altura 20 (elevação e seção)
        Assert.True(TemCota(r, 15));
        Assert.True(TemCota(r, 20));
        // seção de forma em L (6 vértices) e a mesma forma em linha fina na seção de armadura
        Assert.Contains(r.Contornos, k => k.Layer == "ARM-PILAR-SECAO" && k.Fechado && k.Vertices.Count == 6);
        Assert.Contains(r.Contornos, k => k.Layer == "ARM-FORMA" && k.Fechado && k.Vertices.Count == 6);
        // estribo da aba: fechado, abraça a alma e a aba (40 + 15 − 2 × 2,5 − ø); faixa de 550 − 2 × cobrimento com c/15
        var est = r.Barras.Single(b => b.Nome == "abaestribo");
        Assert.True(est.Estribo);
        Assert.InRange(est.Vertices.Max(p => p.X) - est.Vertices.Min(p => p.X), 49, 50);
        var faixa = r.Faixas.Single(f => f.Barra == "abaestribo" && f.Tipo == Modelo.TipoFaixa.Ortogonal);
        Assert.Equal(15, faixa.Espacamento);
        Assert.InRange(faixa.P1.X, 25, 30);
        Assert.InRange(faixa.P2.X, 570, 575);
        // longitudinais: 2 na ponta da aba, entre os dentes
        var lg = r.Barras.Single(b => b.Nome == "abalong");
        Assert.Equal(2, lg.Quantidade);
        Assert.Contains(r.Textos, t => t.Texto.Contains("ARMADURA ABA LAJE"));
        // indicações na seção: uma do estribo e uma das longitudinais
        Assert.Equal(2, r.Faixas.Count(f => f.Barra.StartsWith("aba") && f.Tipo == Modelo.TipoFaixa.Indicacao));
        Assert.Contains(r.Avisos, a => a.Contains("um lado só"));
        Assert.Contains(r.Avisos, a => a.Contains("suspensa"));
    }

    [Fact]
    public void TInvertidoRetoSomaAsDuasAbas()
    {
        var r = Gerar(v =>
        {
            v["Nome"] = "VLV"; v["B"] = "35"; v["H"] = "40"; v["L"] = "800"; v["ExtE"] = TemplateVigaPreMoldada.Reta; v["ExtD"] = TemplateVigaPreMoldada.Reta;
            v["Aba"] = TemplateVigaPreMoldada.AbaDois; v["AbaLargura"] = "15"; v["AbaAltura"] = "20"; v["AbaLongQtd"] = "2";
        });
        Assert.Equal(4, r.Barras.Single(b => b.Nome == "abalong").Quantidade);
        Assert.Contains(r.Contornos, k => k.Layer == "ARM-PILAR-SECAO" && k.Vertices.Count == 8);
        Assert.DoesNotContain(r.Avisos, a => a.Contains("um lado só"));
        // volume com as abas: (35 × 40 + 2 × 15 × 20) × 800 = 1,60 m³
        var prod = r.Tabelas.SelectMany(t => t.Linhas).SelectMany(l => l).ToList();
        Assert.Contains(prod, s => s != null && (s.Contains("1,60") || s.Contains("1.60")));
    }

    [Fact]
    public void AbaParcialMudaOCorteEOCG()
    {
        var a = new AbaViga { Esquerda = true, Largura = 20, Altura = 20, Ini = 0, Fim = 200 };
        double cg0 = TemplateVigaPreMoldada.CentroDeGravidade(20, 50, 600, Reta, Reta);
        double cg = TemplateVigaPreMoldada.CentroDeGravidade(20, 50, 600, Reta, Reta, a);
        Assert.Equal(300, cg0, 6);
        Assert.True(cg < 300 && cg > 250);
        // corte do vão no meio da aba (100), não no meio da viga
        var r = Gerar(v =>
        {
            v["L"] = "600"; v["ExtE"] = TemplateVigaPreMoldada.Reta; v["ExtD"] = TemplateVigaPreMoldada.Reta;
            v["Aba"] = TemplateVigaPreMoldada.AbaEsq; v["AbaLargura"] = "20"; v["AbaIni"] = "0"; v["AbaFim"] = "200";
        });
        Assert.Contains(r.Contornos, k => k.Layer == "ARM-PILAR-SECAO" && k.Vertices.Count == 6);
        Assert.True(TemCota(r, 200));
        Assert.True(TemCota(r, 400));
        // aba só do lado de trás da elevação: oculta
        Assert.Contains(r.Contornos, k => k.Layer == "ARM-FURO-OCULTO" && Math.Abs(k.Vertices[0].X - 0) < 1e-6 && Math.Abs(k.Vertices[1].X - 200) < 1e-6);
    }

    [Fact]
    public void AbaInvalidaAvisaEDesenhaSemAba()
    {
        var r = Gerar(v => { v["Aba"] = TemplateVigaPreMoldada.AbaDir; v["AbaLargura"] = "0"; });
        Assert.Contains(r.Avisos, a => a.Contains("sem aba"));
        Assert.DoesNotContain(r.Barras, b => b.Nome.StartsWith("aba"));
    }

    /// <summary>Viga de um modelo real: 19 × 70 com aba elevada de fundo inclinado, que passa pelos dentes (acima do recorte).</summary>
    private static void AbaElevadaInclinada(ValoresTemplate v)
    {
        v["Nome"] = "V119"; v["B"] = "19"; v["H"] = "70"; v["L"] = "457"; v["ExtE"] = "DG 45x25"; v["ExtD"] = "DG 45x25";
        v["Aba"] = TemplateVigaPreMoldada.AbaDir; v["AbaLargura"] = "20"; v["AbaAltura"] = "22"; v["AbaAlturaPonta"] = "15"; v["AbaBase"] = "25";
    }

    [Fact]
    public void AbaElevadaInclinadaPassaPeloDente()
    {
        var r = Gerar(AbaElevadaInclinada);
        Assert.DoesNotContain(r.Avisos, a => a.Contains("entra no dente"));
        // seção do vão: alma + trapézio da aba = 8 vértices; seção do dente (a parte de cima, 45 de altura) com a aba saindo do
        // canto de baixo dela = 7
        var secoes = r.Contornos.Where(k => k.Layer == "ARM-PILAR-SECAO" && k.Fechado).ToList();
        Assert.Contains(secoes, k => k.Vertices.Count == 8);
        Assert.Equal(2, secoes.Count(k => k.Vertices.Count == 7));
        // cotas: balanço 20, ponta 15, altura na face 22, fundo 25 (vão)
        foreach (var m in new[] { 20.0, 15, 22, 25 }) Assert.True(TemCota(r, m), $"cota {m}");
        // estribo da aba com o ramo de baixo inclinado: 5 lados
        Assert.Contains(r.Contornos, k => k.Layer == "ARM-BARRA-DETALHE" && k.Fechado && k.Vertices.Count == 5);
        // a faixa dos estribos da aba vai de ponta a ponta
        var faixa = r.Faixas.Single(f => f.Barra == "abaestribo" && f.Tipo == Modelo.TipoFaixa.Ortogonal);
        Assert.InRange(faixa.P1.X, 0, 5);
        Assert.InRange(faixa.P2.X, 452, 457);
        // volume: alma com os dentes + aba trapezoidal 20 × (22 + 15) / 2 × 457
        double alma = 19 * 70 * 457 - 2 * 19 * 25 * 25, aba = 20 * 18.5 * 457;
        var a = TemplateVigaPreMoldada.LerAba(Valores(AbaElevadaInclinada), 70, 457, new ExtremidadeViga(), new ExtremidadeViga(), null);
        Assert.Equal(aba, a.VolumeCm3, 3);
        Assert.True(alma > 0);
    }

    private static ValoresTemplate Valores(Action<ValoresTemplate> ajustar)
    {
        var v = new ValoresTemplate(T.Parametros);
        ajustar(v);
        return v;
    }

    [Fact]
    public void AbaAbaixoDoRecorteParaNoDente()
    {
        // aba no fundo (base 0) com dente de recorte 5: fica entre os dentes, como antes
        var a = TemplateVigaPreMoldada.LerAba(Valores(v => { v["H"] = "50"; v["Aba"] = TemplateVigaPreMoldada.AbaDir; }), 50, 600,
            new ExtremidadeViga { Dente = TipoDente.Padrao()[0] }, new ExtremidadeViga(), new List<string>());
        Assert.Equal((25.0, 600.0), (a.Ini, a.Fim));
        // na seção do dente: a aba que fica em parte acima do recorte aparece pendurada (fundo negativo); a que fica toda abaixo, não
        Assert.Equal(-5, a.NoDente(5).Base);
        Assert.Null(a.NoDente(20));
        Assert.Equal(20, new AbaViga { Direita = true, Largura = 20, Altura = 22, Base = 25, Ini = 0, Fim = 100 }.NoDente(5).Base);
    }

    /// <summary>Com ARM_PREVIA definida, grava as prévias das vigas com aba (tools/previa_template.py).</summary>
    [Fact]
    public void GravarPreviaDaVigaComAba()
    {
        var pasta = Environment.GetEnvironmentVariable("ARM_PREVIA");
        if (string.IsNullOrEmpty(pasta)) return;
        PreviaTemplateTests.Gravar(pasta, "previa_viga_L.json", Gerar(v =>
        {
            v["Nome"] = "V129"; v["B"] = "40"; v["H"] = "50"; v["L"] = "600"; v["ExtE"] = "DG 45x25"; v["ExtD"] = "DG 45x25";
            v["Aba"] = TemplateVigaPreMoldada.AbaDir; v["AbaLargura"] = "15"; v["AbaAltura"] = "20";
        }));
        PreviaTemplateTests.Gravar(pasta, "previa_viga_aba_elevada.json", Gerar(AbaElevadaInclinada));
        // V121 do modelo real: 30 × 50 com abas dos dois lados rentes ao topo (15 de balanço, 15 junto da alma, 12 na ponta)
        PreviaTemplateTests.Gravar(pasta, "previa_viga_v121.json", Gerar(v =>
        {
            v["Nome"] = "V121"; v["B"] = "30"; v["H"] = "50"; v["L"] = "457"; v["ExtE"] = "DG 45x25"; v["ExtD"] = "DG 45x25";
            v["Aba"] = TemplateVigaPreMoldada.AbaDois; v["AbaLargura"] = "15"; v["AbaAltura"] = "15"; v["AbaAlturaPonta"] = "12"; v["AbaBase"] = "35";
        }));
        PreviaTemplateTests.Gravar(pasta, "previa_viga_T.json", Gerar(v =>
        {
            v["Nome"] = "VLV"; v["B"] = "35"; v["H"] = "40"; v["L"] = "800"; v["ExtE"] = TemplateVigaPreMoldada.Reta; v["ExtD"] = TemplateVigaPreMoldada.Reta;
            v["Aba"] = TemplateVigaPreMoldada.AbaDois; v["AbaLargura"] = "15"; v["AbaAltura"] = "20";
        }));
    }
}
