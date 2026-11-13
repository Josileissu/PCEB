using System.Linq;
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
    public void LajeDeAlturaInteiraNaoJuntaAsFileiras()
    {
        // duas fileiras com lajes de altura inteira (4 e 5) que entram na de cima; a ordem esperada é a do índice + 1
        var esperado = new (double, double, double, double)[]
        {
            (12.3, 18.5, -129.7, -122.9), (18.9, 26.3, -129.7, -122.9), (26.7, 32.7, -129.7, -122.9), (33.1, 39.1, -134.9, -122.9),
            (39.5, 45.5, -134.9, -122.9), (45.9, 51.9, -129.7, -122.9), (52.3, 58.3, -129.7, -122.9), (58.7, 64.8, -131.3, -122.9),
            (12.3, 18.5, -134.9, -130.0), (18.9, 20.3, -134.8, -129.9), (24.3, 26.3, -134.9, -129.9), (26.7, 32.7, -134.9, -130.0),
            (45.9, 51.9, -134.9, -130.0), (52.3, 58.3, -134.9, -130.0), (58.7, 60.1, -134.9, -132.1),
        };
        // entrada embaralhada (a ordem de entrada não pode mudar o resultado)
        int[] mistura = { 14, 3, 8, 0, 12, 7, 5, 10, 1, 13, 4, 9, 2, 11, 6 };
        var caixas = mistura.Select(i => esperado[i]).ToArray();
        var r = OrdenacaoMarcas.Lajes(caixas);
        Assert.Equal(Enumerable.Range(0, 15).ToArray(), r.Ordem.Select(k => mistura[k]).ToArray());
        Assert.Equal(2, r.Linhas);
        // e na ordem original também
        Assert.Equal(Enumerable.Range(0, 15).ToArray(), OrdenacaoMarcas.Lajes(esperado).Ordem.ToArray());
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
