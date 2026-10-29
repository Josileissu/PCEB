using System;
using System.Collections.Generic;
using System.Linq;
using Armadura.Core.Configuracao;
using Armadura.Core.Modelo;
using Armadura.Core.Templates;
using Armadura.Core.Templates.PilarPreMoldado;
using Xunit;

namespace Armadura.Core.Tests;

/// <summary>Etapa 3 do template do pilar: consolos (medidas do detalhe "ARMADURAS consoles 40x23.5x25 - duplo" do P61).</summary>
public class PilarConsolosTests
{
    private static readonly TemplatePilarPreMoldado T = new TemplatePilarPreMoldado();

    private static ResultadoTemplate Gerar(params (string k, string v)[] mudancas)
    {
        var v = new ValoresTemplate(T.Parametros);
        foreach (var (k, x) in mudancas) v[k] = x;
        Assert.Empty(CatalogoTemplates.Validar(T, v));
        return T.Gerar(v, new ConfiguracaoArmadura { AlturaTextoMm = 2 });
    }

    [Fact]
    public void LeituraEVolume()
    {
        var v = new ValoresTemplate(T.Parametros);
        var avisos = new List<string>();
        var cs = TemplatePilarPreMoldado.LerConsolos(v, -2.90, 1390.5, avisos);
        var c = Assert.Single(cs);
        Assert.Equal(new[] { 1, 3 }, c.Faces);
        Assert.True(c.Duplo);
        Assert.Equal("B", c.Letra);
        Assert.Equal(0.047, c.Volume, 6);
        Assert.Equal(3.523, TemplatePilarPreMoldado.VolumeTotal(50, 50, 1390.5, 0, cs), 3);
        // CG um pouco abaixo do meio (consolos a 5,8 m da base num pilar de 13,9 m)
        double cg = TemplatePilarPreMoldado.AlturaCG(50, 50, 1390.5, 0, -2.90, cs);
        Assert.True(cg < 1390.5 / 2 && cg > 1390.5 / 2 - 5);
        // face inválida e consolo fora do pilar
        v["C1Faces"] = "7";
        Assert.Empty(TemplatePilarPreMoldado.LerConsolos(v, -2.90, 1390.5, avisos));
        Assert.Contains(avisos, a => a.Contains("nenhuma face"));
    }

    [Theory]
    [InlineData(3.5, 16, false, 5.5)]   // c + φ = 5,1 → 5,5
    [InlineData(3.5, 16, true, 9.5)]    // c + 3,5φ = 9,1 → 9,5
    [InlineData(3.5, 20, true, 13.5)]   // c + 5φ = 13,5
    public void A2DaNbr9062(double cob, double phi, bool laco, double esperado) =>
        Assert.Equal(esperado, TemplatePilarPreMoldado.A2Minimo(cob, phi, laco), 6);

    [Fact]
    public void ArmadurasDoConsoloDuploComoNoP61()
    {
        var r = Gerar();
        double Comp(BarraTemplate b) => Enumerable.Range(0, b.Vertices.Count - 1)
            .Sum(i => Math.Sqrt(Math.Pow(b.Vertices[i + 1].X - b.Vertices[i].X, 2) + Math.Pow(b.Vertices[i + 1].Y - b.Vertices[i].Y, 2)));
        var tir = r.Barras.Single(b => b.Nome == "cons1tir");
        Assert.Equal(93, Comp(tir), 6);          // 25 + 50 + 25 − 2 × 3,5 (P61: 5 N13 ø16 C=93)
        Assert.Equal(5, tir.Quantidade);
        var tr = r.Barras.Single(b => b.Nome == "cons1tr");
        Assert.Equal(2, tr.Quantidade);           // uma barra soldada em cada ponta (P61: 2 N14)
        Assert.Equal(33, Comp(tr), 6);
        var cost = r.Barras.Single(b => b.Nome == "cons1cost");
        Assert.True(cost.Estribo);
        Assert.Equal(4, cost.Quantidade);          // P61: 4 N5 (em volta dos dois consolos)
        Assert.Equal(10, r.Barras.Single(b => b.Nome == "cons1vert").Quantidade);   // 2x5 N3
        Assert.Equal(10, r.Barras.Single(b => b.Nome == "cons1inf").Quantidade);    // 5 N6 por consolo
        var pino = r.Barras.Single(b => b.Nome == "cons1pino");
        Assert.Equal(4, pino.Quantidade);          // 2x2 N16
        Assert.Equal(85, Comp(pino), 6);           // 47 + 18 + 20 (externo)
        // corte B-B hachurado, marcas na VISTA 2, título do detalhe, almofada
        Assert.Equal(2, r.Hachuras.Count);
        Assert.Contains(r.Textos, t => t.Texto == "CORTE B-B");
        Assert.Equal(2, r.Textos.Count(t => t.Texto == "B" && t.Layer == "ARM-CORTE"));
        Assert.Contains(r.Textos, t => t.Texto.StartsWith("ARMADURAS CONSOLO 40x23,5x25 - DUPLO"));
        Assert.Contains(r.Textos, t => t.Texto.StartsWith("NEOPRENE 15x30x15 mm"));
        Assert.Contains(r.Faixas, f => f.Tipo == TipoFaixa.Indicacao && f.Barra == "cons1tir");
        Assert.DoesNotContain(r.Avisos, a => a.StartsWith("Consolo"));
    }

    [Fact]
    public void ConsoloSozinhoComLacoETrapezoidal()
    {
        var r = Gerar(("C1Faces", "2"), ("C1TirAnc", TemplatePilarPreMoldado.AncLaco), ("C1H1", "15"), ("C2", "Sim"), ("C2Faces", "4"));
        Assert.Contains(r.Barras, b => b.Nome == "cons1laco" && b.Bulges.Contains(1.0));
        Assert.DoesNotContain(r.Barras, b => b.Nome == "cons1tr");
        Assert.Contains(r.Textos, t => t.Texto == "CORTE C-C");
        Assert.Contains(r.Textos, t => t.Texto.StartsWith("ARMADURAS CONSOLO 40x23,5/15x25"));
        // consolo sozinho: tirante com gancho para ancorar no pilar (consolo 2, com barra soldada)
        var tirs = r.Barras.Single(b => b.Nome == "cons2tirs");
        Assert.Contains(FormatoSegmento.Gancho90, tirs.Formatos);
    }

    [Fact]
    public void AvisosDaNbr9062()
    {
        var r = Gerar(("C1A", "60"), ("C1H", "12"), ("C1H1", "5"), ("C1CostN", "0"), ("C1TirBitola", "25"), ("C1TirN", "2"));
        Assert.Contains(r.Avisos, a => a.Contains("a/d"));
        Assert.Contains(r.Avisos, a => a.Contains("h/2"));
        Assert.Contains(r.Avisos, a => a.Contains("sem armadura de costura"));
        Assert.Contains(r.Avisos, a => a.Contains("1/6"));
    }
}
