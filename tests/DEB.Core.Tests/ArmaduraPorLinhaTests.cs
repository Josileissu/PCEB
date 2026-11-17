using System.Collections.Generic;
using System.Linq;
using DEB.Core.Elementos.Pilar.PreMoldado;
using DEB.Core.Geometria.Secao;
using DEB.Core.Motor;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>
/// Armadura por linha (D32): a entrada pela seção (SecaoTransversal + cobrimentos por parte) dá, na retangular, exatamente o
/// resultado da conta por b × h que o template do pilar usa; nas outras famílias, "não definido".
/// </summary>
public class ArmaduraPorLinhaTests
{
    private static readonly double[] Cob = { 3, 2.5, 3.5, 4 };

    private static List<LinhaLongitudinal> Linhas(string texto) =>
        ArmaduraPorLinha.Ler(ValoresTemplate.LerTabela(texto), 2.5, 597.5, new List<string>());

    public static IEnumerable<object[]> Casos() => new[]
    {
        new object[] { 40.0, 40.0, "Cantos||12.5|1||;Faces 2 e 4|1|12.5|1||;Faces 1 e 3|1|12.5|1||" },
        new object[] { 50.0, 70.0, "Cantos||25|1||;Ao lado dos cantos||20|1||;Faces 2 e 4|2|16|2||;Face 1|3|12.5|1||" },
        new object[] { 60.0, 45.0, "Cantos||20|3||;Cantos||16|1|300|;Face 3|2|10|1||;Face 4|1|20|2|0|320;Face 4|1|16|1|280|" },
    };

    [Theory]
    [MemberData(nameof(Casos))]
    public void RetangularPelaSecaoIgualAContaPorBxH(double b, double h, string texto)
    {
        var linhas = Linhas(texto);
        Assert.NotEmpty(linhas);
        var antes = ArmaduraPorLinha.Posicoes(b, h, Cob, 8, linhas, 5);
        var pela = ArmaduraPorLinha.Posicoes(SecaoTransversal.Retangular(b, h), FacesRetangulo.Cobrimentos(Cob), 8, linhas, 5);
        Assert.True(pela.Definido, pela.Erro);
        Assert.Equal(antes.Keys, pela.PorLinha.Keys);
        foreach (var k in antes.Keys)
        {
            Assert.Equal(antes[k].Count, pela.PorLinha[k].Count);
            for (int i = 0; i < antes[k].Count; i++)
            {
                BarraNaSecao x = antes[k][i], y = pela.PorLinha[k][i];
                Assert.Equal(x.X, y.X, 12); Assert.Equal(x.Y, y.Y, 12);
                Assert.Equal(x.Bitola, y.Bitola); Assert.Equal(x.Feixe, y.Feixe);
                Assert.Equal(x.Grupo, y.Grupo); Assert.Equal(x.Arranjo, y.Arranjo);
            }
        }
        // o atalho do template é a mesma conta
        var t = TemplatePilarPreMoldado.PosicoesLinhas(b, h, Cob, 8, linhas, 5);
        Assert.Equal(antes.Values.SelectMany(v => v).Select(p => (p.X, p.Y)), t.Values.SelectMany(v => v).Select(p => (p.X, p.Y)));
    }

    [Fact]
    public void OutrasFamiliasNaoDefinidas()
    {
        var linhas = Linhas("Cantos||16|1||");
        var cob = new Dictionary<ParteSecao, double> { [ParteSecao.Topo] = 3, [ParteSecao.Fundo] = 3, [ParteSecao.Esquerda] = 3, [ParteSecao.Direita] = 3 };
        foreach (var s in new[] { SecaoTransversal.T(20, 60, 12, 30, 30), SecaoTransversal.Trapezoidal(20, 30, 40) })
        {
            var r = ArmaduraPorLinha.Posicoes(s, cob, 8, linhas, 5);
            Assert.False(r.Definido);
            Assert.Contains("não definida para esta família", r.Erro);
            Assert.Empty(r.PorLinha);
        }
    }

    [Fact]
    public void CobrimentoFaltandoNaRetangular()
    {
        var cob = new Dictionary<ParteSecao, double> { [ParteSecao.Topo] = 3, [ParteSecao.Fundo] = 3, [ParteSecao.Esquerda] = 3 };
        var r = ArmaduraPorLinha.Posicoes(SecaoTransversal.Retangular(40, 40), cob, 8, Linhas("Cantos||16|1||"), 5);
        Assert.False(r.Definido);
        Assert.False(string.IsNullOrEmpty(r.Erro));
    }
}
