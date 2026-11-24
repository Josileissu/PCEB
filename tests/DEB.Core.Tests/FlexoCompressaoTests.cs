using System;
using DEB.Core.Elementos.Pilar.Calculo;
using DEB.Core.Pecas;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Flexo-compressão oblíqua (D44 etapa 3) conferida com o concreteproperties 0.8 (Gabaritos/flexo_compressao_concreteproperties.py).</summary>
public class FlexoCompressaoTests
{
    private static SecaoArmada Pilar40()
    {
        var s = new SecaoArmada { Concreto = SecaoPoligonal.Retangulo(40, 40), FckMPa = 30, FykMPa = 500, GamaC = 1.4, GamaS = 1.15 };
        foreach (var (y, z) in new[] { (4.0, 4.0), (36.0, 4.0), (36.0, 36.0), (4.0, 36.0), (20.0, 4.0), (20.0, 36.0), (4.0, 20.0), (36.0, 20.0) })
            s.Barras.Add((y, z, 20));
        return s;
    }

    [Theory]
    [InlineData(1000, 0, 261.40, 0.00)]
    [InlineData(1000, 30, 205.74, -103.36)]
    [InlineData(1000, 45, 156.63, -156.63)]
    [InlineData(2000, 0, 230.18, 0.00)]
    [InlineData(2000, 30, 186.85, -94.04)]
    [InlineData(2000, 45, 144.01, -144.01)]
    public void IgualAoConcreteProperties(double nd, double anguloLN, double myKNm, double mzKNm)
    {
        var r = FlexoCompressaoObliqua.Resistente(Pilar40(), nd, anguloLN * Math.PI / 180).Value;
        double ref_ = Math.Sqrt(myKNm * myKNm + mzKNm * mzKNm) * 100;
        Assert.True(Math.Abs(r.My - myKNm * 100) < 0.005 * ref_, $"My {r.My / 100:0.00} × {myKNm}");
        Assert.True(Math.Abs(r.Mz - mzKNm * 100) < 0.005 * ref_, $"Mz {r.Mz / 100:0.00} × {mzKNm}");
    }

    [Fact]
    public void AproveitamentoNaDirecaoDoMomento()
    {
        var s = Pilar40();
        // no próprio ponto da curva: aproveitamento 1
        Assert.InRange(FlexoCompressaoObliqua.Aproveitamento(s, 1000, 26140, 0), 0.98, 1.02);
        Assert.InRange(FlexoCompressaoObliqua.Aproveitamento(s, 1000, 13070, 0), 0.49, 0.51);
        // força normal acima da resistente: infinito
        var (nmax, _) = FlexoCompressaoObliqua.Limites(s);
        Assert.True(double.IsPositiveInfinity(FlexoCompressaoObliqua.Aproveitamento(s, nmax * 1.01, 100, 0)));
    }

    [Fact]
    public void SegundaOrdemPelaRigidezKappa()
    {
        // Nd = 1000 kN, h = 40, le = 600, M1d,A = 3000 kN·cm: confere com a iteração da NBR 6118 15.8.3.3.3
        double nd = 1000, h = 40, le = 600, m1 = 3000, ac = 1600, fcd = 3 / 1.4;
        double nu = nd / (ac * fcd), lam = le * Math.Sqrt(12) / h;
        double m = m1;
        for (int i = 0; i < 200; i++) { double k = 32 * (1 + 5 * m / (h * nd)) * nu; m = m1 / (1 - lam * lam / (120 * k / nu)); }
        Assert.Equal(m, FlexoCompressaoObliqua.MomentoTotalKappa(nd, m1, h, le), 3);
        Assert.True(m > m1);
    }
}
