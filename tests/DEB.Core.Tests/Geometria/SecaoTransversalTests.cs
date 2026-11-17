using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Geometria.Secao;
using DEB.Core.Pecas;
using Xunit;

namespace DEB.Core.Tests.Geometria;

/// <summary>Contrato de seção (D28, etapa 5 da O-2): famílias, parâmetros, orientação, partes nomeadas e as duas origens.</summary>
public class SecaoTransversalTests
{
    [Fact]
    public void RetangularDaAsMesmasPropriedadesDoRetanguloMedido()
    {
        var s = SecaoTransversal.Retangular(20, 50);
        var r = SecaoPoligonal.Retangulo(20, 50);
        Assert.Equal(OrigemSecao.Familia, s.Origem);
        Assert.Equal(r.Area, s.Area, 9);
        Assert.Equal(r.CG, s.CG);
        Assert.Equal(r.Iy, s.Iy, 6);
        Assert.Equal(r.Iz, s.Iz, 6);
        Assert.Equal("Retangular: b = 20, h = 50", s.Descricao);
    }

    [Fact]
    public void TInvertidoEhATComEspelhoVertical()
    {
        // alma 20, h 60, mesa 15 de espessura com 20 de cada lado
        var t = SecaoTransversal.T(20, 60, 15, 20, 20);
        var ti = SecaoTransversal.T(20, 60, 15, 20, 20, invertida: true);
        Assert.Same(FamiliasSecao.T, ti.Familia);
        Assert.Equal(OrientacaoSecao.Invertida, ti.Orientacao);
        Assert.Equal(60 * 15 + 20 * 45.0, t.Area, 9);
        Assert.Equal(t.Area, ti.Area, 9);
        Assert.Equal(60 - t.CG.Z, ti.CG.Z, 9);       // espelho: o CG desce
        Assert.Equal(t.Iy, ti.Iy, 6);
        Assert.Equal(TipoSecao.T, t.Contorno().Classificar().Tipo);
        Assert.Equal(TipoSecao.TInvertido, ti.Contorno().Classificar().Tipo);
        // o contorno espelhado continua anti-horário (área com sinal positivo)
        Assert.True(AreaComSinal(ti.Contorno().Contorno) > 0);
    }

    [Fact]
    public void PartesNomeadasNaPosicaoReal()
    {
        var ti = SecaoTransversal.T(20, 60, 15, 20, 20, invertida: true);
        // invertida: o topo é o topo da alma; a mesa fica embaixo
        var topo = ti.Parte(ParteSecao.Topo);
        Assert.Equal(TipoParte.Face, topo.Tipo);
        Assert.Equal(60, topo.Inicio.Z, 9);
        Assert.Equal(20, Math.Abs(topo.Fim.Y - topo.Inicio.Y), 9);
        var mesa = ti.Parte(ParteSecao.Mesa).Regiao;
        Assert.Equal((0.0, 15.0, 0.0, 60.0), (mesa.Z0, mesa.Z1, mesa.Y0, mesa.Y1));
        var alma = ti.Parte(ParteSecao.Alma).Regiao;
        Assert.Equal((15.0, 60.0, 20.0, 40.0), (alma.Z0, alma.Z1, alma.Y0, alma.Y1));
        Assert.Equal(20, ti.Parte(ParteSecao.LateralAlmaEsquerda).Inicio.Y, 9);
        Assert.Equal(40, ti.Parte(ParteSecao.LateralAlmaDireita).Inicio.Y, 9);
    }

    [Fact]
    public void LEspelhadaTrocaEsquerdaEDireita()
    {
        // alma 26 × 170, aba de 25 × 20 embaixo; espelhada: a aba sai para a esquerda
        var l = SecaoTransversal.L(26, 170, 20, 25, new OrientacaoSecao(true, false));
        var c = l.Contorno();
        Assert.Equal(51, c.B, 9);
        Assert.Equal(26 * 170 + 25 * 20.0, c.Area, 9);
        var esq = l.Parte(ParteSecao.Esquerda);   // a esquerda é a ponta da aba (y = 0, 20 de altura)…
        Assert.Equal((0.0, 0.0, 20.0), (esq.Inicio.Y, esq.Fim.Y, Math.Abs(esq.Fim.Z - esq.Inicio.Z)));
        var dir = l.Parte(ParteSecao.Direita);    // …e a direita, a face externa da alma
        Assert.Equal((51.0, 170.0), (dir.Inicio.Y, Math.Abs(dir.Fim.Z - dir.Inicio.Z)));
        Assert.Equal(25, l.Parte(ParteSecao.LateralAlmaEsquerda).Inicio.Y, 9);
        Assert.Null(l.Parte(ParteSecao.LateralAlmaDireita));
        var aba = l.Parte(ParteSecao.Aba).Regiao;
        Assert.Equal((0.0, 25.0), (aba.Y0, aba.Y1));
    }

    [Fact]
    public void IETrapezoidal()
    {
        var i = SecaoTransversal.I(12, 80, 15, 19, 19, 20, 14, 14);
        Assert.Equal(50, i.B, 9);
        Assert.Equal(50 * 15 + 40 * 20 + 12 * 45.0, i.Area, 9);
        Assert.Equal(TipoSecao.I, i.Contorno().Classificar().Tipo);
        Assert.Equal(new[] { ParteSecao.Topo, ParteSecao.Fundo, ParteSecao.LateralAlmaEsquerda, ParteSecao.LateralAlmaDireita,
                             ParteSecao.Alma, ParteSecao.MesaSuperior, ParteSecao.MesaInferior }, i.Partes().Select(p => p.Parte));

        var tr = SecaoTransversal.Trapezoidal(30, 20, 50);
        Assert.Equal(25 * 50.0, tr.Area, 9);
        Assert.Equal(30, tr.B, 9);
        var esq = tr.Parte(ParteSecao.Esquerda);
        Assert.Equal(((0.0, 0.0), (5.0, 50.0)), (esq.Inicio, esq.Fim));
    }

    [Fact]
    public void SecaoMedidaGuardaOPoligono()
    {
        var p = new SecaoPoligonal { Contorno = { (10, 5), (40, 5), (35, 45), (12, 45), (10, 30) } };
        var s = SecaoTransversal.DaMedida(p);
        Assert.Equal(OrigemSecao.Medida, s.Origem);
        Assert.Same(FamiliasSecao.Poligonal, s.Familia);
        Assert.Equal(p.Contorno, s.Contorno().Contorno);
        Assert.NotSame(p, s.Contorno());
        Assert.Equal(p.Area, s.Area, 9);
        Assert.Empty(s.Partes());
        Assert.Empty(s.Valores);
    }

    [Fact]
    public void ValoresInvalidosDaoErroClaro()
    {
        var e = Assert.Throws<ArgumentException>(() => SecaoTransversal.T(20, 60, 60, 20, 20));
        Assert.Contains("hf deve ser menor que h", e.Message);
        Assert.Throws<ArgumentException>(() => SecaoTransversal.T(20, 60, 15, 0, 20));   // balanço zero é L
        e = Assert.Throws<ArgumentException>(() => SecaoTransversal.DaFamilia(FamiliasSecao.L, new Dictionary<string, double> { ["bw"] = 20 }));
        Assert.Contains("falta o parâmetro \"h\"", e.Message);
        Assert.Throws<ArgumentException>(() => SecaoTransversal.DaFamilia(FamiliasSecao.Poligonal, new Dictionary<string, double>()));
        Assert.Throws<KeyNotFoundException>(() => SecaoTransversal.Retangular(20, 50).Valor("bw"));
    }

    [Fact]
    public void FamiliasPorIdentificador()
    {
        Assert.Equal(new[] { "retangular", "T", "L", "I", "trapezoidal", "poligonal" }, FamiliasSecao.Todas.Select(f => f.Id));
        Assert.Same(FamiliasSecao.Trapezoidal, FamiliasSecao.PorId("Trapezoidal"));
        Assert.Null(FamiliasSecao.PorId("TInvertido"));   // invertido é orientação, não família
        Assert.All(FamiliasSecao.Todas.SelectMany(f => f.Parametros), p => Assert.Equal("cm", p.Unidade));
    }

    private static double AreaComSinal(IReadOnlyList<(double Y, double Z)> p) =>
        Enumerable.Range(0, p.Count).Sum(i => p[i].Y * p[(i + 1) % p.Count].Z - p[(i + 1) % p.Count].Y * p[i].Z) / 2;
}
