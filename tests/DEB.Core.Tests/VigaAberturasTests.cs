using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Modelo;
using DEB.Core.Templates;
using DEB.Core.Templates.VigaPreMoldada;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Aberturas na alma da viga (pedido do usuário, 2026-11-11; regra aprovada por ele a partir das pranchas do escritório).</summary>
public class VigaAberturasTests
{
    /// <summary>Como a V139 das pranchas: 20 × 65, três aberturas 25 × 25, um furo circular pequeno e duas 20 × 20.</summary>
    internal static void V139(ValoresTemplate v)
    {
        v["Nome"] = "V139"; v["B"] = "20"; v["H"] = "65"; v["L"] = "817";
        v["Aberturas"] = "Retangular|227.5|25|20|25;Retangular|282.5|25|20|25;Retangular|337.5|25|20|25;Circular|430|10|27.5|10;" +
                         "Retangular|560|20|17.5|20;Retangular|655|20|17.5|20";
    }

    private static ResultadoTemplate Gerar(Action<ValoresTemplate> ajustar)
    {
        var t = new TemplateVigaPreMoldada();
        var v = new ValoresTemplate(t.Parametros);
        ajustar(v);
        return t.Gerar(v, new ConfiguracaoArmadura());
    }

    [Fact]
    public void ReforcoDasAberturasPelaRegraDoEscritorio()
    {
        var r = Gerar(V139);
        var cfg = new ConfiguracaoArmadura();
        Assert.Contains(r.Avisos, a => a.StartsWith("Aberturas na alma: reforço"));
        // 3 estribos da alma de cada lado de cada abertura reforçada (5): 10 faixas contadas de 3, na barra "estribo"
        var contadas = r.Faixas.Where(f => f.Barra == "estribo" && !f.MostrarEspacamento).ToList();
        Assert.Equal(10, contadas.Count);
        Assert.All(contadas, f => Assert.Equal(3, CalculoFaixa.Quantidade(f.P2.X - f.P1.X, f.Espacamento, cfg.EspacamentoInicialFaixa, cfg.ModoQuantidadeFaixa)));
        // nenhuma faixa de estribos da alma passa por dentro de uma abertura
        foreach (var (x0, x1) in new[] { (215.0, 240.0), (550.0, 570.0) })
            Assert.DoesNotContain(r.Faixas, f => f.Barra == "estribo" && f.P1.X < x1 - 1e-6 && f.P2.X > x0 + 1e-6);
        // estribos curtos: acima (65 − 45 − 2,5 − 2,5 = 15) e abaixo (20 − 2,5 − 2,5 = 15) iguais → uma barra 2x, c/5 na largura
        var curto = r.Barras.Single(b => b.Nome == "abert1curto");
        Assert.True(curto.Estribo);
        Assert.Equal(2, curto.Repeticoes);
        var fc = r.Faixas.Single(f => f.Barra == "abert1curto" && f.Tipo == TipoFaixa.Ortogonal);
        Assert.Equal((215.0, 240.0, 5.0, 2), (fc.P1.X, fc.P2.X, fc.Espacamento, fc.Repeticoes));
        // 20 × 20 a 17,5: em cima 65 − 37,5 − 5 = 22,5 e embaixo 17,5 − 5 = 12,5 → duas barras diferentes
        Assert.Contains(r.Barras, b => b.Nome == "abert5cima");
        Assert.Contains(r.Barras, b => b.Nome == "abert5baixo");
        // barras horizontais: 2 em cima e 2 embaixo, ø10, 25 + 2 × 40 = 105 cm
        var hz = r.Barras.Single(b => b.Nome == "abert1h");
        Assert.Equal((2, 2, 10.0), (hz.Quantidade, hz.Repeticoes, hz.Bitola));
        Assert.Equal(105, hz.Vertices[1].X - hz.Vertices[0].X, 6);
        Assert.Contains(r.Textos, t => t.Texto.StartsWith("REFORÇO ABERTURA"));
        // o furo circular de 10 cm não tem reforço, mas tem seção
        Assert.DoesNotContain(r.Barras, b => b.Nome.StartsWith("abert4"));
        // seções: A (dente), B (vão), C (dente) + uma por abertura (D…I)
        foreach (var id in new[] { "D", "E", "F", "G", "H", "I" }) Assert.Contains(r.Textos, t => t.Texto == $"SEÇÃO {id}-{id} - ARMADURA");
    }

    [Fact]
    public void SemAberturasNadaMuda()
    {
        var r = Gerar(v => { v["Nome"] = "V1"; v["B"] = "20"; v["H"] = "65"; v["L"] = "817"; });
        Assert.DoesNotContain(r.Barras, b => b.Nome.StartsWith("abert"));
        Assert.DoesNotContain(r.Avisos, a => a.Contains("Abertura"));
    }

    [Fact]
    public void AberturaForaDaVigaAvisa()
    {
        var r = Gerar(v => { v["B"] = "20"; v["H"] = "65"; v["L"] = "500"; v["Aberturas"] = "Retangular|490|30|20|25;Retangular|250|20|50|25"; });
        Assert.Contains(r.Avisos, a => a.StartsWith("Abertura 1: fora da viga"));
        Assert.Contains(r.Avisos, a => a.StartsWith("Abertura 2: fora da altura"));
    }

    [Fact]
    public void SegundoEstagioDeConcretagem()
    {
        // V139 "20 x 65+10" (pedido do usuário, 2026-11-11): nota, seção h+capa, estribos na capa, superior no 2º estágio e porta-estribo 2 ø6,3
        var r = Gerar(v => { V139(v); v["SegEstagio"] = "Sim"; v["SegEstagioCapa"] = "10"; });
        Assert.Contains(r.Textos, t => t.Texto == "V139 (20 x 65+10)");
        Assert.Contains(r.Textos, t => t.Texto.StartsWith("**VIGA COM SEGUNDO ESTÁGIO DE CONCRETAGEM**"));
        Assert.Contains(r.Textos, t => t.Texto.Contains("ARMADURA SUPERIOR (2º ESTÁGIO)"));
        var porta = r.Barras.Single(b => b.Nome == "portaestribo");
        Assert.Equal((2, 6.3), (porta.Quantidade, porta.Bitola));
        // estribo: 20 − 5 por 65 + 10 − 5 (sobe na capa)
        var est = r.Barras.Single(b => b.Nome == "estribo");
        Assert.Equal(70, est.Vertices.Max(q => q.Y) - est.Vertices.Min(q => q.Y), 1);
        // a capa em tracejado (elevação e seções) e a cota de 10
        Assert.True(r.Contornos.Count(k => k.Layer == "ARM-FURO-OCULTO" && k.Vertices.Count == 4) >= 3);
        Assert.Contains(r.Cotas, k => System.Math.Abs(System.Math.Abs(k.P2.Y - k.P1.Y) - 10) < 1e-6 && System.Math.Abs(k.P2.X - k.P1.X) < 1e-6);
        // sem o 2º estágio, nada disso
        var r0 = Gerar(V139);
        Assert.DoesNotContain(r0.Barras, b => b.Nome == "portaestribo");
        Assert.Contains(r0.Textos, t => t.Texto == "V139 (20 x 65)");
    }
}
