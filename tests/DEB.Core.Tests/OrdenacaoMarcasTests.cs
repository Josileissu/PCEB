using DEB.Core.Numeracao;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Ordem das marcas em planta (scripts Revit_Suelio): cima → baixo, esquerda → direita.</summary>
public class OrdenacaoMarcasTests
{
    [Fact]
    public void PilaresEmDuasLinhasComPequenoDesnivel()
    {
        // linha de cima com 0,3 de desnível (dentro da tolerância 0,5); linha de baixo
        var refs = new[] { (10.0, 0.0), (0.0, 0.0), (5.0, 10.3), (0.0, 10.0), (10.0, 10.0) };
        var r = OrdenacaoMarcas.PorLinhas(refs, 0.5);
        Assert.Equal(new[] { 3, 2, 4, 1, 0 }, r.Ordem);
        Assert.Equal(2, r.Linhas);
    }

    [Fact]
    public void VigasHorizontaisDepoisVerticaisDepoisInclinadas()
    {
        var eixos = new[]
        {
            ((0.0, 0.0, 0.0), (0.0, 10.0, 0.0)),    // 0 vertical em x=0
            ((0.0, 10.0, 0.0), (10.0, 10.0, 0.0)),  // 1 horizontal no topo
            ((0.0, 0.0, 0.0), (10.0, 0.0, 0.0)),    // 2 horizontal embaixo
            ((10.0, 0.0, 0.0), (10.0, 10.0, 0.0)),  // 3 vertical em x=10
            ((0.0, 0.0, 0.0), (10.0, 0.0, 3.0)),    // 4 inclinada
        };
        var r = OrdenacaoMarcas.Vigas(eixos, 0.5);
        Assert.Equal(new[] { 1, 2, 0, 3, 4 }, r.Ordem);
        Assert.Equal((2, 2, 1), (r.Horizontais, r.Verticais, r.Inclinadas));
    }

    [Fact]
    public void LajesNaMesmaFaixaFicamNaMesmaLinha()
    {
        var caixas = new[]
        {
            (5.0, 10.0, 0.0, 4.0),   // 0 linha de baixo, direita
            (0.0, 5.0, 5.0, 10.0),   // 1 linha de cima, esquerda
            (0.0, 5.0, 0.0, 4.2),    // 2 linha de baixo, esquerda (topo um pouco diferente)
            (5.0, 10.0, 5.2, 10.0),  // 3 linha de cima, direita
        };
        var r = OrdenacaoMarcas.Lajes(caixas);
        Assert.Equal(new[] { 1, 3, 2, 0 }, r.Ordem);
        Assert.Equal(2, r.Linhas);
    }

    [Fact]
    public void ParedesPorDirecaoEFaixa()
    {
        var eixos = new[]
        {
            ((0.0, 0.0, 0.0), (0.0, 10.0, 0.0)),   // 0 vertical
            ((0.0, 0.0, 0.0), (10.0, 0.0, 0.0)),   // 1 horizontal embaixo
            ((0.0, 10.0, 0.0), (10.0, 10.0, 0.0)), // 2 horizontal em cima
        };
        Assert.Equal(new[] { 2, 1, 0 }, OrdenacaoMarcas.Paredes(eixos, 0.5).Ordem);
    }

    [Theory]
    [InlineData("P12", ModoFiltroMarca.ComecaCom, "p", true)]
    [InlineData("SF2", ModoFiltroMarca.ComecaCom, "F", false)]
    [InlineData("SF2", ModoFiltroMarca.Contem, "f", true)]
    [InlineData("F1", ModoFiltroMarca.Igual, "F1", true)]
    [InlineData("F10", ModoFiltroMarca.Igual, "F1", false)]
    [InlineData("PM3", ModoFiltroMarca.Regex, "^PM\\d+$", true)]
    [InlineData("", ModoFiltroMarca.Todas, "", false)]
    public void FiltroDeMarca(string marca, ModoFiltroMarca modo, string texto, bool esperado) =>
        Assert.Equal(esperado, FiltroMarca.Corresponde(marca, modo, texto));
}
