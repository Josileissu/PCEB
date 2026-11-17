using DEB.Core.Documentacao;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>ARMPDFLOTE: reconhecimento do formato da folha pela moldura e nome dos PDFs.</summary>
public class LotePdfTests
{
    [Theory]
    [InlineData(1486.3, 841, "A0++")]
    [InlineData(1189, 841, "A0")]
    [InlineData(841, 1189, "A0")]          // moldura em pé: a orientação não importa
    [InlineData(1337.6, 841, "A0+")]
    [InlineData(1121.3, 594, "A1++")]
    [InlineData(911.1, 594, "A1+")]
    [InlineData(841, 594, "A1 (ISO A1)")]
    [InlineData(594, 420, "A2 (ISO A2)")]
    [InlineData(420, 297, "A3 (ISO A3)")]  // formulário de 295: dentro da tolerância
    [InlineData(297, 210, "A4 (ISO A4)")]
    [InlineData(1180, 835, "A0")]          // 9 mm a menos: ainda reconhece
    public void ReconheceEmMm(double l, double a, string esperado)
    {
        var r = FormatosFolha.Reconhecer(l, a, FormatosFolha.Padrao());
        Assert.Equal(esperado, r.Formato?.Nome);
        Assert.Equal(1, r.Fator);
    }

    [Fact]
    public void ReconheceLayoutEmCentimetros()
    {
        // pranchas do escritório: carimbo dinâmico em cm (148,63 × 84,1)
        var r = FormatosFolha.Reconhecer(148.63, 84.1, FormatosFolha.Padrao());
        Assert.Equal("A0++", r.Formato?.Nome);
        Assert.Equal(10, r.Fator);
    }

    [Fact]
    public void ForaDaToleranciaFicaSemFormato()
    {
        var r = FormatosFolha.Reconhecer(1000, 700, FormatosFolha.Padrao());
        Assert.Null(r.Formato);
        Assert.True(r.Diferenca > 10);
        // moldura interna com margem de 25 mm não é a folha
        Assert.Null(FormatosFolha.Reconhecer(1451.3, 821, FormatosFolha.Padrao()).Formato);
    }

    [Fact]
    public void NomeDoPdf()
    {
        Assert.Equal("00000-PLN-009-01.pdf", FormatosFolha.NomePdf(@"C:\obra\00000-PLN-009-01.dwg", "Layout1", false));
        Assert.Equal("PLANTA - Folha 2.pdf", FormatosFolha.NomePdf("PLANTA.dwg", "Folha 2", true));
        Assert.Equal("PLANTA - A_B.pdf", FormatosFolha.NomePdf("PLANTA.dwg", "A/B", true));
    }

    [Fact]
    public void ConfigGuardaEscolhas()
    {
        var arq = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "deb_pdflote_teste.json");
        var c = new ConfigLotePdf { EstiloPlotagem = "teste.ctb", NaPastaDoDesenho = false, PastaDestino = @"C:\pdf", Area = AreaPlotLote.Extensao };
        c.Salvar(arq);
        var l = ConfigLotePdf.Carregar(arq);
        System.IO.File.Delete(arq);
        Assert.Equal("teste.ctb", l.EstiloPlotagem);
        Assert.False(l.NaPastaDoDesenho);
        Assert.Equal(@"C:\pdf", l.PastaDestino);
        Assert.Equal(AreaPlotLote.Extensao, l.Area);
        Assert.Equal(9, l.Formatos.Count);
        Assert.Equal("PDF24", l.Impressora);
    }
}
