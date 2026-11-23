using System;
using System.IO;
using System.Linq;
using DEB.Core.Analise.Mef;
using DEB.Core.Elementos.Escada;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>
/// Conferência por MEF da escada (D42). A malha é testada sempre; os testes que rodam o OpenSees e o CalculiX só comparam
/// quando os programas existem neste computador (no GitHub não existem: o teste passa sem rodar).
/// </summary>
public class EscadaMefTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MalhaDaChapaNaoTemElementoInvertido(bool patamares, bool degraus)
    {
        var r = CalculoEscada.Calcular(patamares ? EscadaTests.ComPatamares() : EscadaTests.LanceSimples());
        var ch = MefEscada.Chapa(r, degraus);
        for (int e = 0; e < ch.Elementos.Count; e++)
            Assert.True(ch.AreaElemento(e) > 1e-6, $"elemento {e + 1} invertido ou degenerado (área {ch.AreaElemento(e):0.###})");
        // a área da malha = área da laje (+ degraus)
        double area = Enumerable.Range(0, ch.Elementos.Count).Sum(ch.AreaElemento);
        if (degraus) Assert.Equal(Math.Abs(EscadaTests.Area(r.Geo.Contorno)), area, 0);
        Assert.Equal(2, ch.Apoios.Count);
        Assert.True(ch.Cortes.Count > 20);
    }

    [Fact]
    public void PorticoTemOsApoiosEACargaTotal()
    {
        var r = CalculoEscada.Calcular(EscadaTests.ComPatamares());
        var p = MefEscada.Portico(r);
        double total = -p.Casos[MefEscada.CasoElu].Sum(c => c.Fz);
        Assert.Equal(1.4 * (r.RaG + r.RbG + r.RaQ + r.RbQ), total, 6);
        Assert.Equal(p.Nos.Count - 1, p.Barras.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenSeesBateComOCalculo(bool patamares)
    {
        var f = FerramentasMef.Padrao;
        if (!f.TemOpenSees) return;
        var r = CalculoEscada.Calcular(patamares ? EscadaTests.ComPatamares() : EscadaTests.LanceSimples());
        var c = MefEscada.Comparar(r, f, openSees: true, calculix: false);
        Assert.True(c.TemOpenSees, c.ErroOpenSees);
        Assert.True(c.DifMdOpenSees < 1.0, $"Md difere {c.DifMdOpenSees:0.00} %");
        Assert.Equal(c.ReacoesCalculo.A, c.ReacoesOpenSees.Value.A, 2);
        Assert.Equal(c.ReacoesCalculo.B, c.ReacoesOpenSees.Value.B, 2);
        foreach (var p in c.Pontos.Where(p => p.X > 5 && p.X < r.Geo.Vao - 5))
        {
            Assert.True(Math.Abs(p.VdOpenSees.Value - p.VdCalculo) < 0.03 * r.VdMax + 0.2, $"V em x = {p.X}: {p.VdOpenSees} × {p.VdCalculo}");
            Assert.True(Math.Abs(p.NdOpenSees.Value - p.NdCalculo) < 0.03 * r.VdMax + 0.2, $"N em x = {p.X}: {p.NdOpenSees} × {p.NdCalculo}");
        }
        // flecha elástica: o OpenSees inclui a deformação axial (desprezível) — até 3 %
        Assert.True(Math.Abs(c.FlechaMaxOpenSees.Value - c.FlechaMaxCalculo) < 0.03 * c.FlechaMaxCalculo, $"flecha {c.FlechaMaxOpenSees} × {c.FlechaMaxCalculo}");
    }

    [Fact]
    public void CalculixConfereOMomentoEMostraORigidezDosDegraus()
    {
        var f = FerramentasMef.Padrao;
        if (!f.TemCalculix) return;
        var r = CalculoEscada.Calcular(EscadaTests.ComPatamares());
        var c = MefEscada.Comparar(r, f, openSees: false, calculix: true);
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "escada_mef.txt"), c.Linhas.Concat(c.Pontos.Select(p =>
            $"x={p.X:0} Md {p.MdCalculo:0.00}/{p.MdChapa:0.00} V {p.VdCalculo:0.00}/{p.VdChapa:0.00} N {p.NdCalculo:0.00}/{p.NdChapa:0.00} f {p.FlechaCalculo:0.000}/{p.FlechaChapa:0.000}/{p.FlechaChapaDegraus:0.000}")));
        Assert.True(c.TemChapa, c.ErroChapa);
        Assert.True(c.TemChapaDegraus, c.ErroChapaDegraus);
        Assert.True(c.DifMdChapa < 5, $"Md da chapa difere {c.DifMdChapa:0.0} %");
        Assert.Equal(c.ReacoesCalculo.A, c.ReacoesChapa.Value.A, 1);
        Assert.True(c.FlechaMaxChapaDegraus < c.FlechaMaxChapa, "os degraus enrijecem");
        // cortante pela derivada do momento: bate com o cálculo no meio do lance e dos patamares
        foreach (var p in c.Pontos.Where(p => p.VdChapa.HasValue && c.QuinasX.All(q => Math.Abs(p.X - q) > 30) && p.X > 30 && p.X < r.Geo.Vao - 30))
            Assert.True(Math.Abs(p.VdChapa.Value - p.VdCalculo) < 0.05 * r.VdMax + 0.3, $"V em x = {p.X:0}: {p.VdChapa:0.00} × {p.VdCalculo:0.00}");
    }
}
