using System.Linq;
using Armadura.Core.Configuracao;
using Armadura.Core.Persistencia;
using Armadura.Core.RelacaoAco;
using Xunit;

namespace Armadura.Core.Tests;

public class TabelaTests
{
    private static ResultadoRA Resultado(ConfiguracaoArmadura cfg) => RelacaoDoAco.Calcular(new[]
    {
        new ItemRA { Numero = 1, Aco = "CA-60", Bitola = 5, Quantidade = 20, ComprimentoUnitario = 150 },
        new ItemRA { Numero = 2, Aco = "CA-50", Bitola = 10, Quantidade = 4, ComprimentoUnitario = 350 },
    }, cfg);

    private static string[] Coluna(ConteudoTabela t, int c) => t.Linhas.Select(l => l[c]).ToArray();

    [Fact]
    public void PadraoMostraSoPesoNoResumo()
    {
        var cfg = new ConfiguracaoArmadura();
        var t = MontadorTabela.Montar(Resultado(cfg), OpcoesTabelaRA.Padrao(cfg), cfg);
        var todas = t.Linhas.SelectMany(l => l).ToList();
        Assert.Contains("PESO (kg)", todas);
        Assert.DoesNotContain("BARRAS", todas);
        Assert.DoesNotContain("rolo", todas);
        // relação: título + cabeçalho + 2; resumo: título + cabeçalho + 2; pesos: 2 aços + total geral
        Assert.Equal(4 + 4 + 3, t.Linhas.Count);
        Assert.Contains("PESO TOTAL (kg)", Coluna(t, 0));
        // resumo só com peso: PESO ocupa as colunas 4-5
        int cab = t.Linhas.FindIndex(l => l[4] == "PESO (kg)");
        Assert.Contains((cab, 4, 5), t.Mesclas);
    }

    [Fact]
    public void ResumoBarrasEPeso()
    {
        var cfg = new ConfiguracaoArmadura();
        var op = new OpcoesTabelaRA { Resumo = ApresentacaoResumo.BarrasEPeso };
        var t = MontadorTabela.Montar(Resultado(cfg), op, cfg);
        int cab = t.Linhas.FindIndex(l => l[4] == "PESO (kg)");
        Assert.Equal("BARRAS", t.Linhas[cab][5]);
        Assert.DoesNotContain((cab, 4, 5), t.Mesclas);
        Assert.Contains("rolo", Coluna(t, 5)); // ø5.0 CA-60 é fornecido em rolo
    }

    [Fact]
    public void SomenteBarrasNaoTemPeso()
    {
        var cfg = new ConfiguracaoArmadura();
        var t = MontadorTabela.Montar(Resultado(cfg), new OpcoesTabelaRA { Resumo = ApresentacaoResumo.Barras }, cfg);
        var todas = t.Linhas.SelectMany(l => l).ToList();
        Assert.Contains("BARRAS", todas);
        Assert.DoesNotContain("PESO (kg)", todas);
        Assert.DoesNotContain(todas, s => s.StartsWith("PESO TOTAL"));
    }

    [Fact]
    public void SomenteTabelaESomenteResumo()
    {
        var cfg = new ConfiguracaoArmadura();
        var r = Resultado(cfg);
        var tab = MontadorTabela.Montar(r, new OpcoesTabelaRA { Apresentacao = ApresentacaoRA.SomenteTabela }, cfg);
        Assert.Equal(4, tab.Linhas.Count);
        Assert.Equal("RELAÇÃO DO AÇO", tab.Linhas[0][0]);

        var res = MontadorTabela.Montar(r, new OpcoesTabelaRA { Apresentacao = ApresentacaoRA.SomenteResumo, MostrarPesoTotal = false }, cfg);
        Assert.Equal("RESUMO DO AÇO", res.Linhas[0][0]);
        Assert.Equal(4, res.Linhas.Count);
        Assert.Equal(new[] { 0 }, res.Titulos.ToArray());
    }

    [Fact]
    public void OpcoesDaTabelaNoXData()
    {
        var op = new OpcoesTabelaRA { Apresentacao = ApresentacaoRA.SomenteResumo, Resumo = ApresentacaoResumo.BarrasEPeso, MostrarPesoTotal = false };
        var volta = CodecDados.DecodificarTabela(CodecDados.Codificar(op), new OpcoesTabelaRA());
        Assert.Equal(ApresentacaoRA.SomenteResumo, volta.Apresentacao);
        Assert.Equal(ApresentacaoResumo.BarrasEPeso, volta.Resumo);
        Assert.False(volta.MostrarPesoTotal);

        // tabela da versão anterior (só o tipo): usa o padrão informado
        var antiga = CodecDados.Codificar(new Vinculo { Tipo = TipoParte.Tabela });
        var padrao = new OpcoesTabelaRA { Resumo = ApresentacaoResumo.Barras };
        Assert.Equal(ApresentacaoResumo.Barras, CodecDados.DecodificarTabela(antiga, padrao).Resumo);
    }

    [Fact]
    public void ConfiguracaoFatiadaRemonta()
    {
        var json = new ConfiguracaoArmadura { PrefixoPosicao = "P" }.ParaJson();
        var partes = CodecDados.Fatiar(json);
        Assert.All(partes, p => Assert.True(p.Length <= 250));
        Assert.Equal("P", ConfiguracaoArmadura.DeJson(string.Concat(partes)).PrefixoPosicao);
    }
}
