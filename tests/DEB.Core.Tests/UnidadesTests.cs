using DEB.Core.Calculo;
using DEB.Core.Configuracao;
using DEB.Core.Descricao;
using DEB.Core.Modelo;
using DEB.Core.RelacaoAco;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Unidades do desenho e da apresentação das medidas (pedido do usuário, 2026-11-10).</summary>
public class UnidadesTests
{
    private static ConfiguracaoArmadura Cfg(UnidadeApresentacao a, int casas = 0) =>
        new ConfiguracaoArmadura { UnidadeApresentacao = a, CasasDecimais = casas };

    [Fact]
    public void ComprimentoNaUnidadeDeApresentacao()
    {
        Assert.Equal("147", CalculoComprimento.Comprimento(147, Cfg(UnidadeApresentacao.Centimetro)));
        Assert.Equal("1.47", CalculoComprimento.Comprimento(147, Cfg(UnidadeApresentacao.Metro)));
        Assert.Equal("1470", CalculoComprimento.Comprimento(147, Cfg(UnidadeApresentacao.Milimetro)));
        // uma casa em cm = 3 em m e 0 em mm
        Assert.Equal("1.475", CalculoComprimento.Comprimento(147.5, Cfg(UnidadeApresentacao.Metro, 1)));
        Assert.Equal("1475", CalculoComprimento.Comprimento(147.5, Cfg(UnidadeApresentacao.Milimetro, 1)));
        Assert.Equal("VAR", CalculoComprimento.TextoCota(147, true, Cfg(UnidadeApresentacao.Milimetro)));
        Assert.Equal(("cm", "m", "mm"), (CalculoComprimento.Sufixo(Cfg(UnidadeApresentacao.Centimetro)), CalculoComprimento.Sufixo(Cfg(UnidadeApresentacao.Metro)),
            CalculoComprimento.Sufixo(Cfg(UnidadeApresentacao.Milimetro))));
    }

    [Fact]
    public void EspacamentoSegueAUnidade()
    {
        Assert.Equal("15", CalculoComprimento.Espacamento(15, Cfg(UnidadeApresentacao.Centimetro)));
        Assert.Equal("0.15", CalculoComprimento.Espacamento(15, Cfg(UnidadeApresentacao.Metro)));
        Assert.Equal("0.125", CalculoComprimento.Espacamento(12.5, Cfg(UnidadeApresentacao.Metro)));
        Assert.Equal("150", CalculoComprimento.Espacamento(15, Cfg(UnidadeApresentacao.Milimetro)));
    }

    [Fact]
    public void DescricaoEmMilimetros()
    {
        var d = new DadosArmadura { Numero = 1, Quantidade = 10, Bitola = 10, Espacamento = 15 };
        string t = FormatadorDescricao.Formatar(d, 147, Cfg(UnidadeApresentacao.Milimetro));
        Assert.Contains("C=1470", t);
        Assert.Contains("c/150", t);
        Assert.Contains("C=147", FormatadorDescricao.Formatar(d, 147, Cfg(UnidadeApresentacao.Centimetro)));
    }

    [Fact]
    public void RelacaoDoAcoNaUnidade()
    {
        var r = new ResultadoRA();
        r.Linhas.Add(new LinhaRA { Aco = "CA-50", Numero = 1, Bitola = 10, Quantidade = 2, ComprimentoUnitario = 147, ComprimentoTotal = 294 });
        var t = MontadorTabela.Montar(r, new OpcoesTabelaRA { Apresentacao = ApresentacaoRA.SomenteTabela }, Cfg(UnidadeApresentacao.Metro));
        Assert.Equal(new[] { "AÇO", "N", "DIAM (mm)", "QUANT", "UNIT (m)", "C.TOTAL (m)" }, t.Linhas[1]);
        Assert.Equal(("1.47", "2.94"), (t.Linhas[2][4], t.Linhas[2][5]));
    }

    [Fact]
    public void UnidadeDoDesenho()
    {
        Assert.Equal(1.0, CalculoComprimento.CmPorUnidade(UnidadeDesenho.Centimetro));
        Assert.Equal(100.0, CalculoComprimento.CmPorUnidade(UnidadeDesenho.Metro));
        Assert.Equal(0.1, CalculoComprimento.CmPorUnidade(UnidadeDesenho.Milimetro));
        Assert.Null(CalculoComprimento.CmPorUnidade(UnidadeDesenho.Automatica));
        // config.json antigo (sem a apresentação): cm
        Assert.Equal(UnidadeApresentacao.Centimetro, ConfiguracaoArmadura.DeJson("{\"VersaoConfig\": 4}").UnidadeApresentacao);
    }
}
