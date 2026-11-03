using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Modelo;
using DEB.Core.Templates;
using DEB.Core.Templates.PilarPreMoldado;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Os testes do pilar mexem no catálogo de consolos (estático): rodam em sequência.</summary>
[CollectionDefinition("Pilar", DisableParallelization = true)]
public class ColecaoPilar { }

/// <summary>Etapa 3 do template do pilar: consolos do catálogo (medidas do detalhe "ARMADURAS consoles 40x23.5x25 - duplo" do P61).</summary>
[Collection("Pilar")]
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

    /// <summary>Roda com tipos extras no catálogo e volta ao padrão.</summary>
    private static void ComCatalogo(IEnumerable<TipoConsolo> extras, Action acao)
    {
        TemplatePilarPreMoldado.Catalogo = TipoConsolo.Padrao().Concat(extras).ToList();
        try { acao(); }
        finally { TemplatePilarPreMoldado.Catalogo = TipoConsolo.Padrao(); }
    }

    [Fact]
    public void CatalogoPadraoEOpcoesDoTemplate()
    {
        var tipos = TipoConsolo.Padrao();
        Assert.Equal(3, tipos.Count);
        Assert.True(tipos[0].Duplo);
        Assert.True(tipos[2].Trapezoidal);
        Assert.Equal("40x23,5x25 duplo", tipos[0].Resumo);
        var p = T.Parametros.Single(x => x.Nome == "Consolos");
        Assert.Equal(TipoParametro.Tabela, p.Tipo);
        Assert.Equal(tipos.Select(t => t.Nome), p.Colunas[0].Opcoes);
        Assert.Equal("CD 40x23,5x25|1 e 3|592.5|0", p.Padrao);
        // grupos com dois dígitos: o PropertyGrid ordena como texto
        Assert.All(T.Parametros, x => Assert.Matches(@"^\d\d\. ", x.Grupo));
        // serialização com a configuração (catálogo salvo no config.json do usuário)
        var cfg = new ConfiguracaoArmadura();
        Assert.Equal(3, cfg.Consolos.Count);
    }

    [Fact]
    public void FacesEmListaConformeOTipo()
    {
        var col = T.Parametros.Single(x => x.Nome == "Consolos").Colunas[1];
        Assert.Equal(TipoParametro.Opcao, col.Tipo);
        // duplo: a face escolhida já leva a oposta
        Assert.Equal(new[] { "1 e 3", "2 e 4" }, col.OpcoesDaLinha(new[] { "CD 40x23,5x25" }));
        Assert.Equal("1 e 3", col.Ajustar(new[] { "CD 40x23,5x25" }, "1"));
        Assert.Equal("1 e 3", col.Ajustar(new[] { "CD 40x23,5x25" }, "3"));
        Assert.Equal("2 e 4", col.Ajustar(new[] { "CD 40x23,5x25" }, "4"));
        Assert.Equal("1 e 3", col.Ajustar(new[] { "CD 40x23,5x25" }, "1,3")); // formato antigo
        // simples: uma face; trocar de duplo para simples fica com a 1ª
        Assert.Equal(new[] { "1", "2", "3", "4" }, col.OpcoesDaLinha(new[] { "CS 20x28,5x25" }));
        Assert.Equal("2", col.Ajustar(new[] { "CS 20x28,5x25" }, "2 e 4"));
        Assert.Equal("", col.Ajustar(new[] { "CS 20x28,5x25" }, "x"));
        // "1 e 3" é lido como as duas faces
        var v = new ValoresTemplate(T.Parametros);
        v["Consolos"] = "CD 40x23,5x25|2 e 4|592.5|0";
        var c = TemplatePilarPreMoldado.LerConsolos(v, TemplatePilarPreMoldado.Catalogo, 1000, new List<string>()).Single();
        Assert.Equal(new[] { 2, 4 }, c.Faces);
        Assert.True(c.Duplo);
    }

    [Fact]
    public void LeituraEVolume()
    {
        var v = new ValoresTemplate(T.Parametros);
        var avisos = new List<string>();
        var cs = TemplatePilarPreMoldado.LerConsolos(v, TemplatePilarPreMoldado.Catalogo, 1390.5, avisos);
        var c = Assert.Single(cs);
        Assert.Equal(new[] { 1, 3 }, c.Faces);
        Assert.True(c.Duplo);
        Assert.Equal("B", c.Letra);
        Assert.Equal(592.5, c.ZTopo);
        Assert.Equal(0.047, c.Volume, 6);
        Assert.Equal(3.523, TemplatePilarPreMoldado.VolumeTotal(50, 50, 1390.5, 0, cs), 3);
        double cg = TemplatePilarPreMoldado.AlturaCG(50, 50, 1390.5, 0, cs);
        Assert.True(cg < 1390.5 / 2 && cg > 1390.5 / 2 - 5);
        // face inválida; tipo duplo numa face só; tipo inexistente
        v["Consolos"] = "CD 40x23,5x25|7|592.5|0";
        Assert.Empty(TemplatePilarPreMoldado.LerConsolos(v, TemplatePilarPreMoldado.Catalogo, 1390.5, avisos));
        Assert.Contains(avisos, a => a.Contains("nenhuma face"));
        v["Consolos"] = "CD 40x23,5x25|1|592.5|0";
        Assert.Empty(TemplatePilarPreMoldado.LerConsolos(v, TemplatePilarPreMoldado.Catalogo, 1390.5, avisos));
        Assert.Contains(avisos, a => a.Contains("duas faces opostas"));
        // tipo simples em faces opostas: dois consolos ancorados (sem tirante passante)
        v["Consolos"] = "CS 20x28,5x25|1,3|592.5|0";
        var dois = Assert.Single(TemplatePilarPreMoldado.LerConsolos(v, TemplatePilarPreMoldado.Catalogo, 1390.5, avisos));
        Assert.False(dois.Duplo);
        Assert.Equal(2, dois.Sozinhas.Count);
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
        Assert.Equal(2, r.Hachuras.Count(hc => hc.Layer == "ARM-HACHURA")); // corte B-B (o detalhe típico tem as suas)
        Assert.Contains(r.Textos, t => t.Texto == "CORTE B-B");
        Assert.Equal(2, r.Textos.Count(t => t.Texto == "B" && t.Layer == "ARM-CORTE"));
        Assert.Contains(r.Textos, t => t.Texto == "ARMADURAS console 40x23.5x25 - 2un.");
        Assert.Contains(r.Textos, t => t.Texto.StartsWith("NEOPRENE 15x30x15 mm"));
        Assert.Contains(r.Faixas, f => f.Tipo == TipoFaixa.Indicacao && f.Barra == "cons1tir");
        Assert.DoesNotContain(r.Avisos, a => a.StartsWith("Consolo"));
    }

    [Fact]
    public void MesmoTipoEmDoisNiveisUmDetalheSo()
    {
        var r = Gerar(("Consolos", "CD 40x23,5x25|1,3|592.5|0;CD 40x23,5x25|2,4|1000|0"));
        Assert.Single(r.Textos, t => t.Texto.StartsWith("ARMADURAS console 40x23.5x25"));
        Assert.Equal(10, r.Barras.Single(b => b.Nome == "cons1tir").Quantidade); // 5 por par × 2 pares
        Assert.Contains(r.Textos, t => t.Texto == "CORTE C-C");
    }

    [Fact]
    public void ConsoloSozinhoComLacoETrapezoidal()
    {
        var laco = new TipoConsolo { Nome = "TESTE laço", Laco = true, H1 = 15 };
        ComCatalogo(new[] { laco }, () =>
        {
            var r = Gerar(("Consolos", "TESTE laço|2|592.5|0;CS 20x28,5x25|4|900|0"));
            Assert.Contains(r.Barras, b => b.Nome == "cons1laco" && b.Bulges.Contains(1.0));
            Assert.DoesNotContain(r.Barras, b => b.Nome == "cons1tr");
            Assert.Contains(r.Textos, t => t.Texto == "CORTE C-C");
            Assert.Contains(r.Textos, t => t.Texto.StartsWith("ARMADURAS console 40x23.5/15x25"));
            var tirs = r.Barras.Single(b => b.Nome == "cons2tirs");
            Assert.Contains(FormatoSegmento.Gancho90, tirs.Formatos);
        });
    }

    [Fact]
    public void AvisosDaNbr9062()
    {
        var ruim = new TipoConsolo { Nome = "TESTE ruim", A = 60, H = 12, H1 = 5, CostN = 0, TirPhi = 25, TirN = 2, Duplo = true };
        ComCatalogo(new[] { ruim }, () =>
        {
            var r = Gerar(("Consolos", "TESTE ruim|1,3|592.5|0"));
            Assert.Contains(r.Avisos, a => a.Contains("a/d"));
            Assert.Contains(r.Avisos, a => a.Contains("h/2"));
            Assert.Contains(r.Avisos, a => a.Contains("sem armadura de costura"));
            Assert.Contains(r.Avisos, a => a.Contains("1/6"));
        });
    }
}
