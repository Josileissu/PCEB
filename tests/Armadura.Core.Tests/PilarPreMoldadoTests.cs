using System.Linq;
using Armadura.Core.Configuracao;
using Armadura.Core.Templates;
using Armadura.Core.Templates.PilarPreMoldado;
using Xunit;

namespace Armadura.Core.Tests;

/// <summary>Template do pilar pré-moldado (etapa 1: forma), com as medidas do P61 do escritório.</summary>
public class PilarPreMoldadoTests
{
    private static readonly TemplatePilarPreMoldado T = new TemplatePilarPreMoldado();
    private static ConfiguracaoArmadura Cfg() => new ConfiguracaoArmadura { AlturaTextoMm = 2 };

    private static ResultadoTemplate Gerar(params (string k, string v)[] mudancas)
    {
        var v = new ValoresTemplate(T.Parametros);
        foreach (var (k, x) in mudancas) v[k] = x;
        Assert.Empty(CatalogoTemplates.Validar(T, v));
        return T.Gerar(v, Cfg());
    }

    [Fact]
    public void VolumeEPesoDoP61SemConsolos()
    {
        // 50 x 50 x 1390,5 = 3,476 m³ (o P61 tem 3,523 com os 2 consolos 40x25x23,5 = 0,047 — etapa 3)
        Assert.Equal(3.476, TemplatePilarPreMoldado.Volume(50, 50, 1390.5, 0), 3);
        var r = Gerar();
        var prod = Assert.Single(r.Tabelas);
        Assert.Equal("INFORMAÇÕES PARA PRODUÇÃO" + TabelaTemplate.Quebra + "(MANUFACTURING INFORMATION)", prod.Titulo);
        Assert.Contains(prod.Linhas, l => l[0] == "VOLUME DE CONCRETO:" + TabelaTemplate.Quebra + "(CONCRETE VOLUME)" && l[1] == "3,476 m³");
        Assert.Contains(prod.Linhas, l => l[0].StartsWith("PESO:") && l[1] == "8,691 ton.");
        Assert.Contains(prod.Linhas, l => l[1] == "fcj = 30 MPa");
        Assert.Contains(prod.Linhas, l => l[1] == "1 un.");
        // só EN: ponto decimal; sem fcj de montagem: a linha some
        var en = TemplatePilarPreMoldado.TabelaProducao(IdiomaDesenho.EN, 28, 0, 40, 1.209, 3.023, 1, 0, 0, 50);
        Assert.Equal("MANUFACTURING INFORMATION", en.Titulo);
        Assert.Equal(5, en.Linhas.Count);
        Assert.Contains(en.Linhas, l => l[0] == "CONCRETE VOLUME:" && l[1] == "1.209 m³");
        Assert.Equal(2.0 * 1 + 5 * 2.0, en.AlturaEmTextos(), 6);
        // chanfro de 2 cm tira 4 triângulos de 2x2/2
        Assert.Equal((2500 - 8) * 1390.5 / 1e6, TemplatePilarPreMoldado.Volume(50, 50, 1390.5, 2), 9);
    }

    [Theory]
    [InlineData(11.005, "+11,005")]
    [InlineData(3.025, "+3,025")]
    [InlineData(0, "0,00")]
    [InlineData(-2.9, "-2,90")]
    [InlineData(4.32, "+4,32")]
    public void CotasDeNivelNoPadraoDoP61(double m, string esperado) => Assert.Equal(esperado, TemplatePilarPreMoldado.FormatarCota(m));

    [Fact]
    public void QuatroVistasComAsLargurasDasFaces()
    {
        var r = Gerar(("B", "40"), ("H", "60"), ("Lemb", "0"));
        var vistas = r.Contornos.Where(c => c.Fechado && c.Vertices.Count == 4 && c.Vertices.Max(p => p.Y) - c.Vertices.Min(p => p.Y) > 1000).ToList();
        Assert.Equal(4, vistas.Count);
        var larguras = vistas.OrderBy(c => c.Vertices.Min(p => p.X)).Select(c => c.Vertices.Max(p => p.X) - c.Vertices.Min(p => p.X)).ToArray();
        Assert.Equal(new[] { 60.0, 40.0, 60.0, 40.0 }, larguras); // VISTA 1 e 3 olham h; 2 e 4 olham b
    }

    [Fact]
    public void NiveisCotasEComprimentoTotal()
    {
        var r = Gerar();
        var textos = r.Textos.Select(t => t.Texto).ToList();
        foreach (var s in new[] { "+11,005", "0,00", "-2,90", "+3,025", "TOPO PILAR", "(TOP COLUMN)", "BASE PILAR", "TÉRREO", "(GROUND FLOOR)", "WAREHOUSE" })
            Assert.Contains(s, textos);
        Assert.Contains(r.Cotas, c => c.Texto.StartsWith("COMPRIMENTO TOTAL DO PILAR") && System.Math.Abs(c.P2.Y - c.P1.Y - 1390.5) < 1e-9);
        // cadeia: base→térreo 290, térreo→warehouse 302,5, warehouse→topo 798 (como no P61)
        var cadeia = r.Cotas.Where(c => c.Texto == "" && System.Math.Abs(c.Angulo - System.Math.PI / 2) < 1e-9)
            .Select(c => System.Math.Round(c.P2.Y - c.P1.Y, 1)).ToList();
        Assert.Contains(290.0, cadeia);
        Assert.Contains(302.5, cadeia);
        Assert.Contains(798.0, cadeia);
        Assert.Contains(100.0, cadeia); // embutimento
    }

    [Fact]
    public void IdiomaSoPTouSoEN()
    {
        var pt = Gerar(("Idioma", "PT")).Textos.Select(t => t.Texto).ToList();
        Assert.Contains("VISTA 1", pt);
        Assert.DoesNotContain(pt, s => s.Contains("VIEW") || s.Contains("MANUFACTURING"));
        var en = Gerar(("Idioma", "EN")).Textos.Select(t => t.Texto).ToList();
        Assert.Contains("VIEW 1", en);
        Assert.Contains("TOP COLUMN", en);
        Assert.DoesNotContain(en, s => s.StartsWith("VISTA") || s.Contains("PRODUÇÃO"));
    }

    [Fact]
    public void ChavesDeCisalhamentoNoEmbutimento()
    {
        var r = Gerar(("Lemb", "100"), ("ChavePasso", "5"), ("ChaveDente", "4"));
        // 20 passos × 2 linhas × 4 vistas
        int linhas = r.Contornos.Count(c => !c.Fechado && c.Vertices.Count == 2 && c.Vertices[0].Y == c.Vertices[1].Y
                                             && c.Vertices[0].Y > 0 && c.Vertices[0].Y <= 100 && c.Layer == "ARM-FORMA");
        Assert.Equal(160, linhas);
        Assert.Empty(r.Avisos);
        var lisa = Gerar(("Interface", TemplatePilarPreMoldado.Lisa), ("Lemb", "30"));
        Assert.Contains(lisa.Avisos, a => a.Contains("< 40 cm"));
        Assert.Contains(lisa.Avisos, a => a.Contains("1,5h"));
    }

    [Fact]
    public void NotasComCobrimentoPorFaceETolerancia()
    {
        var r = Gerar(("CobFace3", "5"), ("Idioma", "PT"));
        var textos = r.Textos.Select(t => t.Texto).ToList();
        Assert.Contains(textos, s => s.Contains("FACE 3 = 5,0") && s.Contains("FACE 1 = 3,5"));
        Assert.Contains(textos, s => s.Contains("COMPRIMENTO ±20 mm"));
        Assert.Equal(15, TemplatePilarPreMoldado.ToleranciaComprimento(665));
        Assert.Equal("P61", r.Detalhamento);
    }
}
