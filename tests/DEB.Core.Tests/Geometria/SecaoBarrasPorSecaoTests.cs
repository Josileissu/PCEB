using System.Collections.Generic;
using System.Linq;
using DEB.Core.Geometria.Secao;
using DEB.Core.Motor;
using DEB.Core.Pecas;
using Xunit;

namespace DEB.Core.Tests.Geometria;

/// <summary><see cref="SecaoBarras"/> pela seção do contrato: igual à função antiga na retangular; erro claro nas outras.</summary>
public class SecaoBarrasPorSecaoTests
{
    public static IEnumerable<object[]> Casos()
    {
        // b, h, cob (face interna 1…4), φt, φcanto, por canto, s canto, φvizinha, n face b, n face h, φface
        yield return new object[] { 50.0, 50.0, new[] { 3.0, 3, 3, 3 }, 8.0, 25.0, 3, 5.0, 20.0, 2, 1, 16.0 };
        yield return new object[] { 40.0, 60.0, new[] { 3.0, 2.5, 4, 3.5 }, 6.3, 20.0, 1, 0.0, 20.0, 1, 3, 12.5 };
        yield return new object[] { 26.5, 170.0, new[] { 2.5, 2.5, 2.5, 2.5 }, 5.0, 16.0, 1, 0.0, 16.0, 0, 6, 10.0 };
    }

    [Theory]
    [MemberData(nameof(Casos))]
    public void RetangularDaExatamenteOResultadoAntigo(double b, double h, double[] cob, double phiT, double phiC, int porCanto,
        double sCanto, double phiV, int nB, int nH, double phiF)
    {
        var antigo = SecaoBarras.Posicoes(b, h, cob, phiT, phiC, porCanto, sCanto, phiV, nB, nH, phiF);
        var novo = SecaoBarras.Posicoes(SecaoTransversal.Retangular(b, h), FacesRetangulo.Cobrimentos(cob), phiT, phiC, porCanto, sCanto, phiV, nB, nH, phiF);
        Assert.True(novo.Definido);
        Assert.Null(novo.Erro);
        Assert.Equal(antigo.Select(x => (x.X, x.Y, x.Bitola, x.Grupo, x.Feixe, x.Arranjo)),
                     novo.Barras.Select(x => (x.X, x.Y, x.Bitola, x.Grupo, x.Feixe, x.Arranjo)));
    }

    [Fact]
    public void SecaoMedidaNaoTemArranjo()
    {
        // D31: T, L, I e trapezoidal têm arranjo (ArranjoFamiliasTests); a medida (poligonal) continua sem
        var cob = FacesRetangulo.Cobrimentos(new[] { 3.0, 3, 3, 3 });
        var s = SecaoTransversal.DaMedida(new SecaoPoligonal { Contorno = { (0, 0), (60, 0), (60, 40), (45, 40), (45, 10), (15, 10), (15, 40), (0, 40) } });
        var a = SecaoBarras.Posicoes(s, cob, 8, 20, 1, 0, 20, 1, 1, 16);
        Assert.False(a.Definido);
        Assert.Empty(a.Barras);
        Assert.Empty(a.Estribos);
        Assert.Contains("não definido para a seção " + s.Familia.Nome, a.Erro);
    }

    [Fact]
    public void RetangularSemEstribosNemAvisos()
    {
        var a = SecaoBarras.Posicoes(SecaoTransversal.Retangular(40, 60), FacesRetangulo.Cobrimentos(new[] { 3.0, 3, 3, 3 }), 8, 20, 3, 5, 16, 2, 2, 12.5);
        Assert.Empty(a.Estribos);
        Assert.Empty(a.Avisos);
        Assert.All(a.Barras, x => Assert.Null(x.Parte));
    }

    [Fact]
    public void FaltaDeCobrimentoEhErroClaro()
    {
        var cob = new Dictionary<ParteSecao, double> { [ParteSecao.Topo] = 3, [ParteSecao.Fundo] = 3, [ParteSecao.Esquerda] = 3 };
        var a = SecaoBarras.Posicoes(SecaoTransversal.Retangular(40, 40), cob, 8, 20, 1, 0, 20, 1, 1, 16);
        Assert.False(a.Definido);
        Assert.Contains("Direita", a.Erro);
    }
}
