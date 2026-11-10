using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.RelacaoAco;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Coluna do detalhamento na relação do aço (pedido do usuário, 2026-11-10).</summary>
public class TabelaElementoTests
{
    private static ResultadoRA Resultado(params (string Elemento, int N)[] linhas)
    {
        var r = new ResultadoRA();
        foreach (var (e, n) in linhas)
            r.Linhas.Add(new LinhaRA { Aco = "CA-50", Numero = n, Bitola = 10, Quantidade = 2, ComprimentoUnitario = 100, ComprimentoTotal = 200, Elemento = e });
        return r;
    }

    [Fact]
    public void TabelaNovaNasceSemAColunaDoDetalhamento()
    {
        var cfg = new ConfiguracaoArmadura();
        Assert.Equal(ColunaElemento.Nunca, cfg.ColunaElementoPadrao);
        var op = OpcoesTabelaRA.Padrao(cfg);
        Assert.Equal(ColunaElemento.Nunca, op.ColunaElemento);
        Assert.False(op.ComElemento(Numeracao.TipoNumeracao.Individual));
        // o usuário pode escolher outro padrão na configuração
        cfg.ColunaElementoPadrao = ColunaElemento.Automatica;
        Assert.True(OpcoesTabelaRA.Padrao(cfg).ComElemento(Numeracao.TipoNumeracao.Individual));
    }

    [Fact]
    public void NomeSoNaLinhaDoN1EAsDeBaixoMescladasAteOProximoDetalhamento()
    {
        var r = Resultado(("V1", 1), ("V1", 2), ("V1", 3), ("P1", 1), ("P2", 1), ("P2", 2));
        var t = MontadorTabela.Montar(r, new OpcoesTabelaRA { Apresentacao = ApresentacaoRA.SomenteTabela }, new ConfiguracaoArmadura(), comElemento: true);
        // título, cabeçalho e as 6 linhas
        var dados = t.Linhas.Skip(2).Take(6).Select(l => (l[0], l[2])).ToList();
        Assert.Equal(new[] { ("V1", "1"), ("", "2"), ("", "3"), ("P1", "1"), ("P2", "1"), ("", "2") }, dados);
        // mesclas na coluna 0: V1 (linhas 2 a 4) e P2 (6 e 7); P1 tem uma linha só e não mescla
        Assert.Equal(new[] { (0, 2, 4), (0, 6, 7) }, t.MesclasVerticais);
        // sem a coluna, nada disso
        var sem = MontadorTabela.Montar(r, new OpcoesTabelaRA { Apresentacao = ApresentacaoRA.SomenteTabela }, new ConfiguracaoArmadura(), comElemento: false);
        Assert.Empty(sem.MesclasVerticais);
    }

    [Fact]
    public void EscalasDaListaSaoDaConfiguracao()
    {
        var cfg = new ConfiguracaoArmadura();
        Assert.Equal(new double[] { 1, 2, 5, 10, 20, 25, 50, 75, 100 }, cfg.EscalasUsuais);
        // uma lista gravada volta igual; o config.json antigo (sem a lista e sem a coluna) recebe os padrões novos
        cfg.EscalasUsuais = new System.Collections.Generic.List<double> { 10, 25, 50 };
        cfg.ColunaElementoPadrao = ColunaElemento.Sempre;
        var volta = ConfiguracaoArmadura.DeJson(cfg.ParaJson());
        Assert.Equal(new double[] { 10, 25, 50 }, volta.EscalasUsuais);
        Assert.Equal(ColunaElemento.Sempre, volta.ColunaElementoPadrao);
        var antigo = ConfiguracaoArmadura.DeJson("{\"VersaoConfig\": 4}");
        Assert.Equal(9, antigo.EscalasUsuais.Count);
        Assert.Equal(ColunaElemento.Nunca, antigo.ColunaElementoPadrao);
    }
}
