using System.Collections.Generic;
using System.Linq;
using DEB.Core.Geometria.Secao;
using DEB.Core.Pecas;
using Xunit;

namespace DEB.Core.Tests.Geometria;

/// <summary>Reconhecimento da família (D28): ida e volta família → contorno → família, e o que fica como seção medida.</summary>
public class ReconhecimentoSecaoTests
{
    private static SecaoPoligonal Poli(params (double Y, double Z)[] p) => new SecaoPoligonal { Contorno = p.ToList() };

    private static readonly OrientacaoSecao Canonica = default;
    private static readonly OrientacaoSecao Horizontal = new OrientacaoSecao(true, false);
    private static readonly OrientacaoSecao Vertical = new OrientacaoSecao(false, true);
    private static readonly OrientacaoSecao Ambos = new OrientacaoSecao(true, true);

    /// <summary>Casos já na forma normal: a resposta tem de ser a mesma família, orientação e parâmetros.</summary>
    public static IEnumerable<object[]> FormaNormal()
    {
        yield return new object[] { SecaoTransversal.Retangular(20, 50) };
        yield return new object[] { SecaoTransversal.Retangular(26.5, 170) };
        yield return new object[] { SecaoTransversal.T(20, 60, 15, 20, 20) };
        yield return new object[] { SecaoTransversal.T(20, 60, 15, 20, 20, invertida: true) };
        yield return new object[] { SecaoTransversal.T(25, 80, 20, 10, 30, invertida: true) };   // balanços diferentes
        foreach (var o in new[] { Canonica, Horizontal, Vertical, Ambos })
            yield return new object[] { SecaoTransversal.L(26, 170, 20, 25, o) };
        yield return new object[] { SecaoTransversal.I(12, 80, 15, 19, 19, 20, 14, 14) };
        yield return new object[] { SecaoTransversal.I(12, 80, 15, 10, 19, 20, 14, 8) };
        yield return new object[] { SecaoTransversal.Trapezoidal(30, 20, 50) };
        yield return new object[] { SecaoTransversal.Trapezoidal(15, 40, 60) };
    }

    [Theory]
    [MemberData(nameof(FormaNormal))]
    public void IdaEVolta(SecaoTransversal s)
    {
        var r = ReconhecimentoSecao.Reconhecer(s.Contorno());
        Assert.Same(s.Familia, r.Familia);
        Assert.Equal(s.Orientacao, r.Orientacao);
        Assert.Equal(s.Valores.Keys, r.Valores.Keys);
        foreach (var k in s.Valores.Keys) Assert.Equal(s.Valor(k), r.Valor(k), 9);
    }

    [Fact]
    public void EspelhoQueViraTrocaDeParametros()
    {
        // T espelhada na horizontal = T com ae e ad trocados
        var t = ReconhecimentoSecao.Reconhecer(SecaoTransversal.DaFamilia(FamiliasSecao.T,
            SecaoTransversal.T(25, 80, 20, 10, 30).Valores, Horizontal).Contorno());
        Assert.Equal((Canonica, 30.0, 10.0), (t.Orientacao, t.Valor("ae"), t.Valor("ad")));
        // I invertida = I com as mesas trocadas
        var i0 = SecaoTransversal.I(12, 80, 15, 10, 19, 20, 14, 8);
        var i = ReconhecimentoSecao.Reconhecer(SecaoTransversal.DaFamilia(FamiliasSecao.I, i0.Valores, Vertical).Contorno());
        Assert.Equal(Canonica, i.Orientacao);
        Assert.Equal((20.0, 14.0, 8.0, 15.0, 10.0, 19.0),
                     (i.Valor("hs"), i.Valor("ase"), i.Valor("asd"), i.Valor("hi"), i.Valor("aie"), i.Valor("aid")));
        // trapézio invertido = bi e bs trocados
        var tr = ReconhecimentoSecao.Reconhecer(SecaoTransversal.DaFamilia(FamiliasSecao.Trapezoidal,
            SecaoTransversal.Trapezoidal(30, 20, 50).Valores, Vertical).Contorno());
        Assert.Equal((Canonica, 20.0, 30.0), (tr.Orientacao, tr.Valor("bi"), tr.Valor("bs")));
    }

    [Fact]
    public void PoligonoMedidoForaDaOrigemESentidoHorario()
    {
        // "seção com aba laje 26x170": alma 26 × 170, aba 25 × 20 embaixo à direita; deslocada e no sentido horário
        var p = Poli((100, 10), (100, 180), (126, 180), (126, 30), (151, 30), (151, 10));
        Assert.Equal(TipoSecao.L, p.Classificar().Tipo);
        var r = ReconhecimentoSecao.Reconhecer(p);
        Assert.Equal("L: bw = 26, h = 170, hf = 20, ba = 25", r.Descricao);
        Assert.True(r.Contorno().MesmaSecao(p));
    }

    [Fact]
    public void TrapezioComVerticeNoMeioDoLado()
    {
        var p = Poli((0, 0), (15, 0), (30, 0), (25, 50), (5, 50), (2.5, 25));
        Assert.Equal(TipoSecao.Poligonal, p.Classificar().Tipo);   // o Classificar não muda
        var r = ReconhecimentoSecao.Reconhecer(p);
        Assert.Equal("Trapezoidal: bi = 30, bs = 20, h = 50", r.Descricao);
    }

    [Fact]
    public void ToleranciaDoReconhecimento()
    {
        // alma 0,3 cm além da face da aba (dentro da tolerância de 0,5): ainda L
        var quase = Poli((0, 0), (51, 0), (51, 20), (26, 20), (26, 170), (-0.3, 170), (-0.3, 20), (0, 20));
        Assert.Same(FamiliasSecao.Retangular, ReconhecimentoSecao.Reconhecer(Poli((0, 0), (20, 0), (20, 50), (0, 50))).Familia);
        Assert.Same(FamiliasSecao.L, ReconhecimentoSecao.Reconhecer(quase).Familia);
        // trapézio com 0,3 cm de diferença entre os balanços: simétrico na tolerância; com 2 cm, não
        Assert.Same(FamiliasSecao.Trapezoidal, ReconhecimentoSecao.Reconhecer(Poli((0, 0), (30, 0), (25.3, 50), (5, 50))).Familia);
        Assert.Same(FamiliasSecao.Poligonal, ReconhecimentoSecao.Reconhecer(Poli((0, 0), (30, 0), (27, 50), (5, 50))).Familia);
    }

    [Fact]
    public void OQueNaoCasaFicaComoSecaoMedida()
    {
        var u = Poli((0, 0), (60, 0), (60, 40), (45, 40), (45, 10), (15, 10), (15, 40), (0, 40));   // U
        var r = ReconhecimentoSecao.Reconhecer(u);
        Assert.Equal(OrigemSecao.Medida, r.Origem);
        Assert.Equal(u.Contorno, r.Contorno().Contorno);

        var comFuro = SecaoPoligonal.Retangulo(40, 40);
        comFuro.Furos.Add(new List<(double, double)> { (10, 10), (30, 10), (30, 30), (10, 30) });
        Assert.Equal(OrigemSecao.Medida, ReconhecimentoSecao.Reconhecer(comFuro).Origem);

        var misula = Poli((0, 0), (20, 0), (20, 40), (35, 50), (0, 50));   // lado inclinado
        Assert.Equal(OrigemSecao.Medida, ReconhecimentoSecao.Reconhecer(misula).Origem);
    }

    [Fact]
    public void ConcordaComOClassificarNoQueEleJaReconhece()
    {
        var casos = new[]
        {
            (SecaoTransversal.Retangular(20, 50), TipoSecao.Retangular),
            (SecaoTransversal.T(20, 60, 15, 20, 20), TipoSecao.T),
            (SecaoTransversal.T(20, 60, 15, 20, 20, invertida: true), TipoSecao.TInvertido),
            (SecaoTransversal.L(26, 170, 20, 25, Ambos), TipoSecao.L),
            (SecaoTransversal.I(12, 80, 15, 19, 19, 20, 14, 14), TipoSecao.I),
            (SecaoTransversal.Trapezoidal(30, 20, 50), TipoSecao.Poligonal),   // trapezoidal: o enum não tem
        };
        foreach (var (s, tipo) in casos) Assert.Equal(tipo, s.Contorno().Classificar().Tipo);
    }
}
