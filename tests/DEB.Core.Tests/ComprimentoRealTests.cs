using System;
using System.Linq;
using DEB.Core.Calculo;
using DEB.Core.Configuracao;
using DEB.Core.Modelo;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Comprimento real pelas curvaturas (planilha Estudo_comprimento_real_barra_vergalhao, Planilha1).</summary>
public class ComprimentoRealTests
{
    private static (System.Collections.Generic.List<GeoSegmento> geo, DadosArmadura d) Barra(
        double bitola, string aco, double[] medidas, double[] deflexoes, FormatoSegmento[] formatos = null)
    {
        var geo = medidas.Select((c, i) => new GeoSegmento(TipoSegmento.Reta, c, deflexoes[i])).ToList();
        var d = new DadosArmadura { Bitola = bitola, Aco = aco };
        for (int i = 0; i < medidas.Length; i++)
            d.Segmentos.Add(new Segmento { Formato = formatos?[i] ?? FormatoSegmento.Linha });
        return (geo, d);
    }

    [Fact]
    public void DobraDe90NoExemploDaPlanilha()
    {
        // ø10 CA-50: pino 5φ = 5; re2 = 3,5; re = 3 → 2·3,5 − 3·π/2 = 2,2876
        var (geo, d) = Barra(10, "CA-50", new[] { 100.0, 54.4 }, new[] { 0.0, 90 });
        Assert.Equal(2.287611019615311, CalculoComprimento.DescontoDobras(geo, d, new ConfiguracaoArmadura().Dobramento), 9);
    }

    [Fact]
    public void BarraEmUTemDuasDobras()
    {
        // 100 + 54,4 + 100 = 254,4 − 2 × 2,2876 = 249,82
        var cfg = new ConfiguracaoArmadura { CasasDecimais = 2 };
        var (geo, d) = Barra(10, "CA-50", new[] { 100.0, 54.4, 100 }, new[] { 0.0, 90, 90 });
        Assert.Equal(249.82, CalculoComprimento.ComprimentoUnitario(geo, cfg, d), 6);
    }

    [Fact]
    public void SemDadosOuDesligadoEhASomaExterna()
    {
        var cfg = new ConfiguracaoArmadura { CasasDecimais = 2 };
        var (geo, d) = Barra(10, "CA-50", new[] { 100.0, 54.4, 100 }, new[] { 0.0, 90, 90 });
        Assert.Equal(254.4, CalculoComprimento.ComprimentoUnitario(geo, cfg), 6);
        cfg.DescontarDobras = false;
        Assert.Equal(254.4, CalculoComprimento.ComprimentoUnitario(geo, cfg, d), 6);
    }

    [Fact]
    public void GanchoUsaOAnguloNominal()
    {
        // gancho 90° desenhado em qualquer ângulo: desconta 2·re2 − re·π/2; 180°: 2·re2 − re·π
        var regras = new ConfiguracaoArmadura().Dobramento;
        var (g90, d90) = Barra(10, "CA-50", new[] { 200.0, 11.5 }, new[] { 0.0, 45 }, new[] { FormatoSegmento.Linha, FormatoSegmento.Gancho90 });
        Assert.Equal(7 - 3 * Math.PI / 2, CalculoComprimento.DescontoDobras(g90, d90, regras), 9);
        var (g180, d180) = Barra(10, "CA-50", new[] { 200.0, 5.5 }, new[] { 0.0, 45 }, new[] { FormatoSegmento.Linha, FormatoSegmento.Gancho180 });
        Assert.Equal(7 - 3 * Math.PI, CalculoComprimento.DescontoDobras(g180, d180, regras), 9);
    }

    [Fact]
    public void ConfiguracaoV3GanhaRegenNaLista()
    {
        var v3 = new ConfiguracaoArmadura();
        v3.ComandosQueAtualizamRA.RemoveAll(c => c.StartsWith("REGEN"));
        v3.ComandosQueAtualizamRA.Add("MEUCOMANDO"); // lista personalizada do usuário é mantida
        var json = v3.ParaJson().Replace($"\"VersaoConfig\": {ConfiguracaoArmadura.VersaoAtual}", "\"VersaoConfig\": 3");
        var cfg = ConfiguracaoArmadura.DeJson(json);
        Assert.True(cfg.Migrada);
        Assert.Contains("REGEN", cfg.ComandosQueAtualizamRA);
        Assert.Contains("REGENALL", cfg.ComandosQueAtualizamRA);
        Assert.Contains("MEUCOMANDO", cfg.ComandosQueAtualizamRA);
        Assert.True(cfg.DescontarDobras);
    }

    [Fact]
    public void BarraRetaETrelicaNaoDescontam()
    {
        var regras = new ConfiguracaoArmadura().Dobramento;
        var (reta, d) = Barra(10, "CA-50", new[] { 300.0 }, new[] { 0.0 });
        Assert.Equal(0, CalculoComprimento.DescontoDobras(reta, d, regras));
        var (geo, dt) = Barra(10, "CA-50", new[] { 100.0, 50 }, new[] { 0.0, 90 });
        dt.Trelica = "TR 8644";
        Assert.Equal(0, CalculoComprimento.DescontoDobras(geo, dt, regras));
    }
}
