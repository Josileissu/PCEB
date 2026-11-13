using System.Collections.Generic;
using System.Linq;
using DEB.Core.Formas;
using DEB.Core.Numeracao;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Pranchas de formas (D25): textos calculados dos parâmetros SD_, ordem natural das marcas e CSV das cargas na fundação.</summary>
public class FormasTests
{
    [Theory]
    [InlineData(40.0, 40.0, "40x40")]
    [InlineData(30.0, 50.0, "30x50")]
    [InlineData(20.0, 47.5, "20x47.5")]
    [InlineData(39.99999, 40.00001, "40x40")]
    public void SecaoEmCm(double b, double h, string esperado) => Assert.Equal(esperado, TextosFormas.Secao(b, h));

    [Fact]
    public void SecaoSemMedidasFicaVazia()
    {
        Assert.Equal("", TextosFormas.Secao(null, 40));
        Assert.Equal("", TextosFormas.Secao(40, 0));
    }

    [Theory]
    [InlineData(12.0, 907.8, "12,00 m (907,80)")]
    [InlineData(-0.4, 895.4, "-0,40 m (895,40)")]
    [InlineData(-1.2, 894.6, "-1,20 m (894,60)")]
    [InlineData(-0.001, 895.799, "0,00 m (895,80)")]
    public void NivelRelativoEAbsoluto(double rel, double abs, string esperado) => Assert.Equal(esperado, TextosFormas.Nivel(rel, abs));

    [Fact]
    public void ComprimentoTotal() => Assert.Equal("13,20 m", TextosFormas.Comprimento(13.2));

    [Theory]
    [InlineData(-29.5, "e=-29.5")]
    [InlineData(-6.0, "e=-6")]
    [InlineData(0.04, "e=0")]
    [InlineData(-0.04, "e=0")]
    [InlineData(12.349, "e=12.3")]
    public void ElevacaoDaViga(double cm, string esperado) => Assert.Equal(esperado, TextosFormas.Elevacao(cm));

    [Fact]
    public void PesoProprioDaLaje() => Assert.Equal(587.5, TextosFormas.PesoProprio(23.5, 2500), 6);

    [Fact]
    public void OrdemNaturalDasMarcas()
    {
        var m = new[] { "P10", "P2", "p1", "V101", "P1A", "P1", "V12", "B3" };
        var ordenadas = m.OrderBy(x => x, ComparadorNatural.Instancia).ToList();
        Assert.Equal(new[] { "B3", "P1", "p1", "P1A", "P2", "P10", "V12", "V101" }, ordenadas);
        var pos = ComparadorNatural.Posicoes(new[] { "P10", "P2", "", "p2", "P1" });
        Assert.Equal(3, pos.Count);
        Assert.Equal(1, pos["P1"]);
        Assert.Equal(2, pos["P2"]);
        Assert.Equal(3, pos["P10"]);
    }

    [Fact]
    public void CsvComCabecalhoEVirgulaDecimal()
    {
        string txt = "Marca;Fz_Max;Fz_Min;Mx_Pos;Mx_Neg;My_Pos;My_Neg;Fx_Pos;Fx_Neg;Fy_Pos;Fy_Neg\n" +
                     "P1;120,5;80;1.234,5;-3;;0;1;2;3;4\n" +
                     "P2;10.25;x;1;1;1;1;1;1;1;1\n" +
                     ";1;2\n";
        var avisos = new List<string>();
        var c = CsvCargas.Ler(txt, avisos);
        Assert.Equal(2, c.Count);
        Assert.Equal(120.5, c[0].Valores[0]);
        Assert.Equal(1234.5, c[0].Valores[2]);
        Assert.Equal(-3, c[0].Valores[3]);
        Assert.Null(c[0].Valores[4]);                 // vazio = não mexe
        Assert.Equal(10.25, c[1].Valores[0]);
        Assert.Null(c[1].Valores[1]);                 // "x" não é número
        Assert.Equal(2, avisos.Count);                // "x" e a linha sem marca
    }

    [Fact]
    public void CsvComColunasEmOutraOrdemEComPrefixo()
    {
        var avisos = new List<string>();
        var c = CsvCargas.Ler("SD_Fz_Min;Marca;SD_Fz_Max\n5;P3;9\n", avisos);
        Assert.Single(c);
        Assert.Equal("P3", c[0].Marca);
        Assert.Equal(9, c[0].Valores[0]);
        Assert.Equal(5, c[0].Valores[1]);
        Assert.Single(avisos);                        // colunas ausentes
    }

    [Fact]
    public void CsvSemCabecalhoPelaPosicaoEMarcaRepetida()
    {
        var avisos = new List<string>();
        var c = CsvCargas.Ler("P1;1;2\nP1;3;4\n", avisos);
        Assert.Single(c);
        Assert.Equal(3, c[0].Valores[0]);
        Assert.Single(avisos);
    }

    [Fact]
    public void CsvIdaEVolta()
    {
        var a = new CargaFundacao { Marca = "P19" };
        a.Valores[0] = 100.125; a.Valores[9] = -2;
        string txt = CsvCargas.Gravar(new[] { a });
        Assert.StartsWith(CsvCargas.Cabecalho, txt);
        var b = CsvCargas.Ler(txt, new List<string>()).Single();
        Assert.Equal("P19", b.Marca);
        Assert.Equal(100.125, b.Valores[0]);
        Assert.Null(b.Valores[1]);
        Assert.Equal(-2, b.Valores[9]);
    }

    [Theory]
    [InlineData("12,5", 12.5)]
    [InlineData("12.5", 12.5)]
    [InlineData("1,234.5", 1234.5)]
    [InlineData(" -0,75 ", -0.75)]
    public void NumeroComVirgulaOuPonto(string s, double v)
    {
        Assert.True(CsvCargas.TentarNumero(s, out double x));
        Assert.Equal(v, x, 9);
    }
}
