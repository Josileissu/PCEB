using DEB.Core.Modelo;
using DEB.Core.Normas;
using DEB.Core.Tabelas;
using Xunit;

namespace DEB.Core.Tests;

public class NormasTests
{
    private readonly RegrasDobramento _r = new();

    [Theory]
    [InlineData(10.0, "CA-50", 5.0)]   // 5φ
    [InlineData(20.0, "CA-50", 16.0)]  // 8φ
    [InlineData(10.0, "CA-25", 4.0)]   // 4φ
    [InlineData(5.0, "CA-60", 3.0)]    // 6φ
    public void PinoLongitudinal(double bitola, string aco, double esperadoCm) =>
        Assert.Equal(esperadoCm, _r.DiametroPino(bitola, aco, estribo: false), 6);

    [Theory]
    [InlineData(5.0, "CA-60", 1.5)]    // φt ≤ 10: 3φt
    [InlineData(12.5, "CA-50", 6.25)]  // 10 < φt < 20: 5φt
    [InlineData(12.5, "CA-25", 5.0)]   // 4φt
    public void PinoEstribo(double bitola, string aco, double esperadoCm) =>
        Assert.Equal(esperadoCm, _r.DiametroPino(bitola, aco, estribo: true), 6);

    [Fact]
    public void PontaRetaEstriboRespeitaMinimo()
    {
        // 5φt = 2.5 cm < 5 cm → 5 cm
        Assert.Equal(5.0, _r.PontaReta(FormatoSegmento.Gancho45, 5.0, estribo: true), 6);
        // 10φt = 5 cm < 7 cm → 7 cm
        Assert.Equal(7.0, _r.PontaReta(FormatoSegmento.Gancho90, 5.0, estribo: true), 6);
    }

    [Fact]
    public void GanchoNoventaPhi10CA50()
    {
        // ponta 8φ = 8 cm + raio do pino 2.5 cm + φ 1 cm = 11.5 cm
        Assert.Equal(11.5, _r.ComprimentoGancho(FormatoSegmento.Gancho90, 10.0, "CA-50", false), 6);
    }

    [Fact]
    public void MassaLinearDaTabelaETeorica()
    {
        var t = TabelaBarras.Padrao();
        Assert.Equal(0.617, t.MassaLinear(10.0), 3);
        Assert.Equal(TabelaBarras.MassaTeorica(14.0), t.MassaLinear(14.0), 6); // fora da tabela
    }
}
