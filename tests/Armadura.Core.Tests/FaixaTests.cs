using Armadura.Core.Configuracao;
using Armadura.Core.Modelo;
using Armadura.Core.Persistencia;
using Xunit;

namespace Armadura.Core.Tests;

public class FaixaTests
{
    [Theory]
    [InlineData(400, 15, 0, ModoQuantidadeFaixa.PisoMaisUm, 27)]   // ⌊26,67⌋ + 1
    [InlineData(400, 15, 0, ModoQuantidadeFaixa.TetoMaisUm, 28)]
    [InlineData(400, 15, 0, ModoQuantidadeFaixa.Teto, 27)]
    [InlineData(300, 15, 0, ModoQuantidadeFaixa.PisoMaisUm, 21)]   // exato: 20 + 1
    [InlineData(300, 15, 0.5, ModoQuantidadeFaixa.PisoMaisUm, 20)] // e0 = 7,5 nas duas pontas
    [InlineData(10, 15, 0, ModoQuantidadeFaixa.PisoMaisUm, 1)]
    public void QuantidadeNaFaixa(double l, double s, double e0, ModoQuantidadeFaixa modo, int esperado) =>
        Assert.Equal(esperado, CalculoFaixa.Quantidade(l, s, e0, modo));

    [Fact]
    public void QuantidadeNaCircunferencia() =>
        Assert.Equal(8, CalculoFaixa.Quantidade(2 * System.Math.PI * 20, 15.7, 0, ModoQuantidadeFaixa.PisoMaisUm, fechada: true));

    [Fact]
    public void PosicoesRespeitamOEspacamentoENaoPassamDoFim()
    {
        var p = CalculoFaixa.Posicoes(400, 27, 15, 0);
        Assert.Equal(0, p[0], 6);
        Assert.Equal(15, p[1] - p[0], 6);
        Assert.Equal(390, p[26], 6);

        // teto + 1 = 28 barras: redistribui para a última ficar no fim
        var q = CalculoFaixa.Posicoes(400, 28, 15, 0);
        Assert.Equal(400, q[27], 6);
        Assert.True(q[1] - q[0] < 15);
    }

    [Fact]
    public void TextosDasFaixas()
    {
        var cfg = new ConfiguracaoArmadura { SimboloDiametro = "ø" };
        var barra = new DadosArmadura { Numero = 3, Bitola = 5 };
        Assert.Equal("27 N3 ø5.0 c/15", TextoFaixa.Formatar(new DadosFaixa { Quantidade = 27, Espacamento = 15 }, barra, cfg));
        Assert.Equal("3 N3", TextoFaixa.Formatar(new DadosFaixa { Tipo = TipoFaixa.Chamada, Quantidade = 3 }, barra, cfg));
        Assert.Equal("N3", TextoFaixa.Formatar(new DadosFaixa { Tipo = TipoFaixa.Indicacao }, barra, cfg));
    }

    [Fact]
    public void FaixaNoXData()
    {
        var f = new DadosFaixa
        {
            Tipo = TipoFaixa.CorteChamadaArco, HandleBarra = "2A", Espacamento = 12.5, EspacamentoInicial = 0.5,
            Quantidade = 9, LadoCorte = -1, P1X = 1, P1Y = 2, P2X = 3, P2Y = 4, PontoChamada = 2,
            CX = 10, CY = 20, Raio = 30, AnguloIni = 0.5, AnguloFim = 2.5,
        };
        f.Referencias.Add((5, 6));
        f.Referencias.Add((7, 8));
        f.Partes.Add("3F");
        f.Partes.Add("40");
        var x = CodecDados.DecodificarFaixa(CodecDados.Codificar(f));
        Assert.Equal(TipoFaixa.CorteChamadaArco, x.Tipo);
        Assert.Equal("2A", x.HandleBarra);
        Assert.Equal(12.5, x.Espacamento);
        Assert.Equal(0.5, x.EspacamentoInicial);
        Assert.Equal(9, x.Quantidade);
        Assert.Equal(-1, x.LadoCorte);
        Assert.Equal(4, x.P2Y);
        Assert.Equal(2, x.PontoChamada);
        Assert.Equal(30, x.Raio);
        Assert.Equal(2.5, x.AnguloFim);
        Assert.Equal(new[] { (5.0, 6.0), (7.0, 8.0) }, x.Referencias);
        Assert.Equal(new[] { "3F", "40" }, x.Partes);
        Assert.Equal(TipoParte.Faixa, CodecDados.LerTipo(CodecDados.Codificar(f)));

        var barra = new DadosArmadura();
        barra.Faixas.Add("3F");
        barra.Rebatidas.Add("50");
        var b = CodecDados.DecodificarBarra(CodecDados.Codificar(barra));
        Assert.Equal(new[] { "3F" }, b.Faixas);
        Assert.Equal(new[] { "50" }, b.Rebatidas);
    }
}
