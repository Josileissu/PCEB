using System;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Templates;
using DEB.Core.Templates.Blocos;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Blocos sobre estacas no padrão BPM (exemplos do usuário em info_auxiliar/templates/blocos, 2026-11-06).</summary>
[Collection("Pilar")]   // usa os detalhes padrão do pilar (catálogos estáticos)
public class BlocoEstacasTests
{
    private static double Comp(BarraTemplate b, int i) =>
        Math.Sqrt(Math.Pow(b.Vertices[i + 1].X - b.Vertices[i].X, 2) + Math.Pow(b.Vertices[i + 1].Y - b.Vertices[i].Y, 2));

    private static double Largura(BarraTemplate b) => b.Vertices.Max(p => p.X) - b.Vertices.Min(p => p.X);
    private static double Altura(BarraTemplate b) => b.Vertices.Max(p => p.Y) - b.Vertices.Min(p => p.Y);

    private static ResultadoTemplate Gerar(Action<ValoresTemplate> ajustar, bool uma = false)
    {
        var t = new TemplateBlocoEstacas(uma);
        var v = new ValoresTemplate(t.Parametros);
        ajustar?.Invoke(v);
        Assert.Empty(CatalogoTemplates.Validar(t, v));
        var r = t.Gerar(v, new ConfiguracaoArmadura());
        Assert.All(r.Barras, b => Assert.True(b.Vertices.Count >= 2, b.Nome));
        Assert.All(r.Faixas, f => Assert.Contains(r.Barras, b => b.Nome == f.Barra));
        Assert.Equal(r.Barras.Count, r.Barras.Select(b => b.Nome).Distinct().Count());
        return r;
    }

    /// <summary>B28: 2 estacas ø40 a 120, 190 × 70, cálice com colarinho 80 × 70 (paredes 21, folga 4), pilar 30 × 20.</summary>
    [Fact]
    public void B28CaliceComColarinho()
    {
        var r = Gerar(v => v["Niveis"] = "P106|-11.43");
        Assert.Equal("BLOCO B28", r.Detalhamento);
        Assert.Equal(6, r.DadosDetalhamento.Repeticoes);
        Assert.Contains(r.Textos, x => x.Texto == "BLOCO B28 - 6x - 2 ø 40");
        Assert.Contains(r.Cotas, k => Math.Abs(Math.Abs(k.P2.X - k.P1.X) - 190) < 1e-6);
        // principal: 5 ø16 c/5 em x, pernas pela regra = 55 − 4 − 5 = 46 (no B28: 45)
        var p = r.Barras.Single(b => b.Nome == "principal x");
        Assert.Equal(5, p.Quantidade); Assert.Equal(16, p.Bitola);
        Assert.Equal(46, Comp(p, 0), 1);
        Assert.Equal(180, Comp(p, 1), 1);           // 190 − 2·4 − 2·0,8 ≈ 180 (B28: 179)
        Assert.DoesNotContain(r.Barras, b => b.Nome == "principal y");
        // cálice: horizontais 72 × 62 (concentradas no topo 3 c/5.5), grampos com perna 21 − 4 − 2 = 15, verticais 2 por parede
        var topo = r.Barras.Single(b => b.Nome == "cálice: horizontais do topo");
        Assert.Equal(3, topo.Quantidade); Assert.Equal(5.5, topo.Espacamento);
        Assert.Equal(72, Largura(topo), 1); Assert.Equal(62, Altura(topo), 1);
        var gr = r.Barras.Single(b => b.Nome == "cálice: grampos do topo (x)");
        Assert.Equal(15, Comp(gr, 0), 1); Assert.Equal(72, Comp(gr, 1), 1); Assert.Equal(2, gr.Repeticoes);
        Assert.Equal(8, r.Barras.Single(b => b.Nome == "cálice: verticais").Quantidade);
        Assert.Contains(r.Barras, b => b.Nome == "suspensão");
        Assert.Contains(r.Barras, b => b.Nome == "cálice: horizontais");
        // superior interrompida no colarinho (largura do bloco: as barras em y não atravessam o colarinho)
        Assert.Contains(r.Barras, b => b.Nome == "superior x" || b.Nome == "superior y");
        // forma e detalhes
        Assert.Contains(r.Textos, x => x.Texto == "CORTE A-A");
        Assert.Contains(r.Textos, x => x.Texto == "CÁLICE");
        Assert.Contains(r.Textos, x => x.Texto == "P106=-11,43 m");
        Assert.Contains(r.Textos, x => x.Texto == "P106=-11,93 m");    // fundo do pilar: −11,43 − 0,50
        Assert.Contains(r.Textos, x => x.Texto == "(COTA DE ARRASAMENTO)");
        Assert.Contains(r.Textos, x => x.Texto.StartsWith("REGULARIZAÇÃO DE FUNDO"));
        Assert.Contains(r.Textos, x => x.Texto.StartsWith("DETALHE 01"));
        Assert.Contains(r.Tabelas, t => t.Titulo.StartsWith("INFORMAÇÕES"));
        Assert.NotNull(r.PosicaoRA);
    }

    /// <summary>B15: 3 estacas ø50 em triângulo de lado 150, faces a 40 → hexágono 210 × 242,4; pilar 40 × 40 no baricentro.</summary>
    [Fact]
    public void B15Triangular()
    {
        var r = Gerar(v =>
        {
            v["Nome"] = "B15"; v["Arranjo"] = TemplateBlocoEstacas.Triangulo; v["EspX"] = "150"; v["DistFace"] = "40"; v["DiamEstaca"] = "50";
            v["PilarBx"] = "40"; v["PilarBy"] = "40"; v["tParede"] = "25"; v["hBase"] = "105"; v["hColar"] = "30"; v["Lemb"] = "65";
        });
        var forma = r.Contornos.First(k => k.Layer == "ARM-FORMA" && k.Fechado && k.Vertices.Count == 6);
        Assert.Equal(210, forma.Vertices.Max(p => p.X) - forma.Vertices.Min(p => p.X), 0);
        Assert.Equal(242.4, forma.Vertices.Max(p => p.Y) - forma.Vertices.Min(p => p.Y), 1);
        // uma faixa principal por lado do triângulo
        Assert.Equal(3, r.Barras.Count(b => b.Nome.StartsWith("principal")));
        // malha no hexágono: comprimentos variáveis (VAR) e o quadro "INDICAÇÃO BARRA VARIÁVEL"
        Assert.Contains(r.Barras, b => b.Nome.StartsWith("secundária") && b.ComprimentosVar.Count > 1);
        Assert.Contains(r.Textos, x => x.Texto == "INDICAÇÃO BARRA VARIÁVEL");
        Assert.Contains(r.Barras, b => b.Nome == "lateral horizontal" && b.Estribo);
        Assert.DoesNotContain(r.Barras, b => b.Nome == "estribo vertical");
        Assert.Contains(r.Textos, x => x.Texto == "BLOCO B15 - 6x - 3 ø 50");
    }

    /// <summary>B31: 4 estacas ø50 a 150, faces a 40 → 230 × 230; principal nas duas direções, 2 faixas cada.</summary>
    [Fact]
    public void B31QuatroEstacas()
    {
        var r = Gerar(v =>
        {
            v["Nome"] = "B31"; v["NEstY"] = "2"; v["EspX"] = "150"; v["EspY"] = "150"; v["DistFace"] = "40"; v["DiamEstaca"] = "50";
            v["PilarBx"] = "60"; v["PilarBy"] = "60"; v["tParede"] = "30"; v["hBase"] = "110"; v["hColar"] = "50"; v["Lemb"] = "95";
            v["PrincBitola"] = "20"; v["PrincQtd"] = "7"; v["PrincEsp"] = "14"; v["Pilares"] = "P113/ P114";
        });
        Assert.Equal(14, r.Barras.Single(b => b.Nome == "principal x").Quantidade);
        Assert.Equal(14, r.Barras.Single(b => b.Nome == "principal y").Quantidade);
        var topo = r.Barras.Single(b => b.Nome == "cálice: horizontais do topo");
        Assert.Equal(120, Largura(topo), 1);          // colarinho 128 − 2·4
        Assert.Equal(2, r.DadosDetalhamento.Repeticoes);
        Assert.Contains(r.Barras, b => b.Nome == "estribo vertical");
        Assert.DoesNotContain(r.Avisos, a => a.Contains("Embutimento") && a.Contains("40 cm"));
    }

    [Fact]
    public void ArranqueSemCalice()
    {
        var r = Gerar(v => v["Ligacao"] = TemplateBlocoEstacas.Arranque);
        Assert.Contains(r.Barras, b => b.Nome == "arranque" && b.Quantidade == 4);
        Assert.Contains(r.Barras, b => b.Nome == "estribo do pilar no bloco");
        Assert.DoesNotContain(r.Barras, b => b.Nome.StartsWith("cálice"));
        Assert.DoesNotContain(r.Textos, x => x.Texto == "CÁLICE");
    }

    [Fact]
    public void UmaEstacaComCaliceEmbutido()
    {
        var r = Gerar(v => { v["Ligacao"] = TemplateBlocoEstacas.Embutido; v["Lemb"] = "45"; }, uma: true);
        Assert.DoesNotContain(r.Barras, b => b.Nome.StartsWith("principal"));
        Assert.Contains(r.Barras, b => b.Nome == "secundária x");
        Assert.Contains(r.Barras, b => b.Nome == "cálice: horizontais do topo");
        Assert.Equal(TemplateBlocoEstacas.IdUmaEstaca, new TemplateBlocoEstacas(true).Id);
    }

    [Fact]
    public void ListaDePilares()
    {
        Assert.Equal(new[] { "P1", "P2", "P5" }, TemplateBlocoEstacas.LerPilares("P1, P2 e P5"));
        Assert.Equal(new[] { "P113", "P114" }, TemplateBlocoEstacas.LerPilares("P113/ P114"));
        Assert.Equal("P1, P2 e P5", TemplateBlocoEstacas.TextoPilares(TemplateBlocoEstacas.LerPilares("P1;P2;P5")));
    }
}
