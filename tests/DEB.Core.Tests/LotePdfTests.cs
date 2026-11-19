using DEB.Core.Documentacao;
using System.Linq;
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

    /// <summary>Escala fixa pela unidade: folha em cm = 10 mm de papel por unidade (1 mm = 0,1), em mm = 1, mesmo com a moldura fora da medida.</summary>
    [Theory]
    [InlineData(148.63, 84.1, 10)]   // A0++ em cm
    [InlineData(148.9, 84.3, 10)]    // moldura um pouco maior: a escala não muda
    [InlineData(1486.3, 841, 1)]     // em mm
    [InlineData(1.4863, 0.841, 1000)] // em m
    public void EscalaFixaPelaUnidade(double l, double a, double esperado)
    {
        var r = FormatosFolha.Reconhecer(l, a, FormatosFolha.Padrao());
        Assert.Equal("A0++", r.Formato.Nome);
        Assert.Equal(esperado, FormatosFolha.EscalaPlot(r));
    }

    [Fact]
    public void EscalaSemMolduraEhAjustar()
    {
        Assert.Equal(0, FormatosFolha.EscalaPlot(null));
        Assert.Equal(0, FormatosFolha.EscalaPlot(new FormatoReconhecido()));
        Assert.Equal(0, FormatosFolha.EscalaPlot(FormatosFolha.Reconhecer(1000, 700, FormatosFolha.Padrao())));
    }

    /// <summary>Folga: o papel cresce 2 × folga na largura e na altura; o nome e a lista nominal (reconhecimento) não mudam.</summary>
    [Fact]
    public void FolgaAumentaSoOPapel()
    {
        var cfg = new ConfigLotePdf { FolgaPapel = 1 };
        var papel = cfg.FormatosDoPapel;
        var a0 = FormatosFolha.PorNome(papel, "A0++");
        Assert.Equal(1488.3, a0.Largura, 6);
        Assert.Equal(843, a0.Altura, 6);
        Assert.Equal(1486.3, FormatosFolha.PorNome(cfg.Formatos, "A0++").Largura, 6);
        Assert.Equal(cfg.Formatos.Select(f => f.Nome), papel.Select(f => f.Nome));
        // a moldura continua reconhecida pela medida nominal e a escala é a mesma
        var r = FormatosFolha.Reconhecer(148.63, 84.1, cfg.Formatos, cfg.Tolerancia);
        Assert.Equal(0, r.Diferenca, 6);
        Assert.Equal(10, FormatosFolha.EscalaPlot(r));
        cfg.FolgaPapel = 0;
        Assert.Equal(1486.3, FormatosFolha.PorNome(cfg.FormatosDoPapel, "A0++").Largura, 6);
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(2.5, 2.5)]
    [InlineData(99, 10)]
    public void FolgaValida(double folga, double esperado) => Assert.Equal(esperado, FormatosFolha.FolgaValida(folga));

    /// <summary>pdflote.json antigo (sem o campo) ganha a folga padrão; o valor gravado é lembrado e saneado.</summary>
    [Fact]
    public void ConfigFolgaPapel()
    {
        var arq = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "deb_pdflote_folga_teste.json");
        try
        {
            System.IO.File.WriteAllText(arq, "{\"Impressora\":\"PDF24\"}");
            Assert.Equal(1, ConfigLotePdf.Carregar(arq).FolgaPapel);
            new ConfigLotePdf { FolgaPapel = 2.5 }.Salvar(arq);
            Assert.Equal(2.5, ConfigLotePdf.Carregar(arq).FolgaPapel);
            new ConfigLotePdf { FolgaPapel = 0 }.Salvar(arq);
            Assert.Equal(0, ConfigLotePdf.Carregar(arq).FolgaPapel);
            new ConfigLotePdf { FolgaPapel = -4 }.Salvar(arq);
            Assert.Equal(0, ConfigLotePdf.Carregar(arq).FolgaPapel);
        }
        finally { System.IO.File.Delete(arq); }
    }
}
