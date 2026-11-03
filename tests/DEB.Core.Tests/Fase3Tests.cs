using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Numeracao;
using DEB.Core.Persistencia;
using DEB.Core.RelacaoAco;
using Xunit;

namespace DEB.Core.Tests;

public class Fase3Tests
{
    private static ItemNumeracao I(string chave, string grupo, string assinatura, double bitola = 10, long ordem = 0,
        bool travada = false, int atual = 0) =>
        new() { Chave = chave, Grupo = grupo, Assinatura = assinatura, Aco = "CA-50", Bitola = bitola, Ordem = ordem, Travada = travada, NumeroAtual = atual };

    // V1 (bloco) e V2 (bloco) têm a mesma barra X; V2 tem ainda Y; uma barra Z fora de bloco
    private static ItemNumeracao[] Itens() => new[]
    {
        I("z", "", "Z", 8, 1),
        I("v1x", "V1", "X", 10, 2),
        I("v2x", "V2", "X", 10, 3),
        I("v2y", "V2", "Y", 12.5, 4),
    };

    [Fact]
    public void GlobalBarrasIguaisMesmoNEmBlocosDiferentes()
    {
        var r = Numerador.Numerar(Itens(), TipoNumeracao.Global, 1);
        Assert.Equal(r.Numeros["v1x"], r.Numeros["v2x"]);
        Assert.Equal(new[] { 1, 2, 2, 3 }, new[] { "z", "v1x", "v2x", "v2y" }.Select(k => r.Numeros[k]));
    }

    [Fact]
    public void IndividualRecomecaEmCadaDetalhamento()
    {
        var r = Numerador.Numerar(Itens(), TipoNumeracao.Individual, 1);
        Assert.Equal(1, r.Numeros["z"]);
        Assert.Equal(1, r.Numeros["v1x"]);
        Assert.Equal(1, r.Numeros["v2x"]);
        Assert.Equal(2, r.Numeros["v2y"]);
    }

    [Fact]
    public void SequencialNaoRepeteNumeros()
    {
        var r = Numerador.Numerar(Itens(), TipoNumeracao.Sequencial, 1);
        // "" (fora de detalhamento) → V1 → V2; X em V1 e em V2 são posições diferentes
        Assert.Equal(new[] { 1, 2, 3, 4 }, new[] { "z", "v1x", "v2x", "v2y" }.Select(k => r.Numeros[k]));
    }

    [Fact]
    public void BloqueioMantemNumerosENovasFicamAcimaDoMaior()
    {
        var itens = new[]
        {
            I("a", "", "A", 10, 1, travada: true, atual: 1),
            I("b", "", "B", 12.5, 2, travada: true, atual: 2),
            I("copiaA", "", "A", 10, 3),          // cópia sem mudança: continua N1
            I("alterada", "", "C", 8, 4),         // barra alterada (forma nova): número novo
        };
        // N3 já foi usado e apagado depois do bloqueio: a nova vai para 4
        var r = Numerador.Numerar(itens, TipoNumeracao.Global, 1, bloqueada: true, maiorNoBloqueio: 3);
        Assert.Equal(1, r.Numeros["a"]);
        Assert.Equal(2, r.Numeros["b"]);
        Assert.Equal(1, r.Numeros["copiaA"]);
        Assert.Equal(4, r.Numeros["alterada"]);
        Assert.Equal(new object[] { "alterada" }, r.Novas.ToArray());
        Assert.Equal(4, r.MaiorNumero);
    }

    [Fact]
    public void TabelaComElementoEDestaque()
    {
        var cfg = new ConfiguracaoArmadura();
        var r = RelacaoDoAco.Calcular(new[]
        {
            new ItemRA { Numero = 1, Aco = "CA-50", Bitola = 10, Quantidade = 2, ComprimentoUnitario = 100, Elemento = "V1" },
            new ItemRA { Numero = 1, Aco = "CA-50", Bitola = 10, Quantidade = 3, ComprimentoUnitario = 200, Elemento = "V2", Nova = true },
        }, cfg, porElemento: true);
        Assert.Equal(2, r.Linhas.Count); // mesmo N em elementos diferentes = linhas diferentes
        var t = MontadorTabela.Montar(r, new OpcoesTabelaRA(), cfg, comElemento: true);
        Assert.Equal(7, t.Colunas);
        Assert.Equal("ELEMENTO", t.Linhas[1][0]);
        Assert.Equal(new[] { "V1", "V2" }, t.Linhas.Skip(2).Take(2).Select(l => l[0]));
        Assert.Equal("1*", t.Linhas[3][2]);
        Assert.Contains(t.Linhas, l => l[0].StartsWith("* posição nova"));
        // resumo em 7 colunas: PESO nas duas últimas
        int cab = t.Linhas.FindIndex(l => l[5] == "PESO (kg)");
        Assert.Contains((cab, 5, 6), t.Mesclas);
        Assert.Contains((cab, 2, 4), t.Mesclas);
    }

    [Fact]
    public void TabelaParcialGuardaOsBlocos()
    {
        var op = new OpcoesTabelaRA();
        op.Blocos.Add("V1");
        op.Blocos.Add("P3 (pilar)");
        var volta = CodecDados.DecodificarTabela(CodecDados.Codificar(op), new OpcoesTabelaRA());
        Assert.True(volta.Parcial);
        Assert.Equal(new[] { "V1", "P3 (pilar)" }, volta.Blocos);
    }
}
