using System;
using System.Linq;
using DEB.Core.Elementos.Viga.PreMoldada;
using DEB.Core.Elementos.Viga.PreMoldada.Calculo;
using DEB.Core.Motor;
using DEB.Core.Normas.Verificacao;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>
/// Torção na viga pré-moldada (NBR 6118:2023, 17.5.1 e 17.7.2), conferida com os exemplos resolvidos de BASTOS, P.S.S., Torção em
/// Vigas de Concreto Armado, UNESP Bauru, 2024 (exemplos 2 e 3, θ = 45°). A apostila arredonda 2c1 = 7,9 cm; daí a tolerância de 2 %.
/// </summary>
[Collection("Pilar")]
public class VigaTorcaoTests
{
    private static void Perto(double esperado, double obtido, double tol = 0.02) =>
        Assert.True(Math.Abs(obtido - esperado) <= tol * Math.Abs(esperado), $"esperado {esperado}, obtido {obtido}");

    [Fact]
    public void Bastos_Exemplo2_Viga19x40_C30()
    {
        // c1 = 1,25/2 + 0,8 + 2,5 = 3,925 cm; A/u = 6,4 < 2c1 = 7,9: he = A/u e Ae pelos eixos das barras dos cantos
        var s = TorcaoViga.Secao(19, 40, 3.925);
        Assert.True(s.PelosCantos);
        Perto(6.4, s.He);
        Perto(356.3, s.Ae);
        Perto(86.4, s.Ue);
        double fcd = 3.0 / 1.4, fywd = 50 / 1.15;
        Perto(2150.0, TorcaoViga.TRd2(s, 30, fcd));               // kN·cm
        double td = 1.4 * 1026;                                   // TSd = 1.436 kN·cm
        Perto(0.0463, TorcaoViga.As90PorCm(s, td, fywd));         // cm²/cm por ramo
        Perto(0.0463, TorcaoViga.AslPorCm(s, td, fywd));          // cm²/cm de perímetro
        // mínima longitudinal: 20·fctm·he/fywk = 20·0,290·6,4/50 = 0,74 cm²/m
        Perto(0.0074, TorcaoViga.AslMinPorCm(s, 0.290, 50), 0.03);
        // cortante + torção nas bielas: 62,9/294,1 + 1436/2150 = 0,88 (o VRd2 do exemplo)
        Perto(0.88, 62.9 / 294.1 + td / TorcaoViga.TRd2(s, 30, fcd), 0.03);
    }

    [Fact]
    public void Bastos_Exemplo3_Viga19x60_C35()
    {
        var s = TorcaoViga.Secao(19, 60, 3.925);
        Assert.True(s.PelosCantos);
        Perto(7.2, s.He);
        Perto(581.5, s.Ae, 0.005);
        Perto(126.6, s.Ue, 0.005);
        Perto(4500.8, TorcaoViga.TRd2(s, 35, 3.5 / 1.4));
    }

    [Fact]
    public void SecaoLargaUsaHeIgualAU()
    {
        // 35 × 50, c1 = 4,125: A/u = 10,3 ≥ 2c1 = 8,25 → he = A/u e Ae = (b − he)(h − he)
        var s = TorcaoViga.Secao(35, 50, 4.125);
        Assert.False(s.PelosCantos);
        Perto(10.29, s.He, 0.005);
        Assert.Equal((35 - s.He) * (50 - s.He), s.Ae, 6);
    }

    [Fact]
    public void TorqueUniformeTemReacoesIguaisEZeroNoMeio()
    {
        var t = TorcaoViga.Diagrama(2, 0, 600, 15, 585, out double ta, out double tb);
        Assert.Equal(2 * 570 / 2.0 + 2 * 15, ta, 6);   // metade do vão + o trecho além do apoio
        Assert.Equal(ta, tb, 6);
        Assert.Equal(570, t(15.0001), 1);
        Assert.Equal(0, t(300), 6);
        Assert.Equal(-570, t(585), 1);
    }

    private static ValoresTemplate VigaL(string aba)
    {
        var v = new ValoresTemplate(new TemplateVigaPreMoldada().Parametros);
        v["B"] = "25"; v["H"] = "60"; v["L"] = "600"; v["ExtE"] = "Reta"; v["ExtD"] = "Reta"; v["Fck"] = "40";
        v["Aba"] = aba; v["AbaLargura"] = "20"; v["AbaAltura"] = "20";
        return v;
    }

    [Fact]
    public void VigaLComLajeNaAbaTemTorcaoEVerificacoes()
    {
        var c = new CargasViga { G = 0, Q = 0, GAba = 15, QAba = 5, ApoioE = 15, ApoioD = 15 };
        var r = CalculoViga.Calcular(VigaL(TemplateVigaPreMoldada.AbaDir), c);
        Assert.True(r.TdMax > 0);
        // mt = 1,4·(15·22,5 + g,aba·e,aba) + 1,4·5·22,5 (kN·cm/m), e = 25/2 + 20/2 = 22,5 cm; Td no apoio ≈ mt·ℓ/2
        double gAba = 20 * 20 / 1e4 * 25, eAba = 12.5 + 10;
        double mt = (1.4 * (15 * 22.5 + gAba * eAba) + 1.4 * 5 * 22.5) / 100;   // kN·cm/cm
        Perto(mt * 300 / 100, r.TdE, 0.01);                       // kN·m (aba de ponta a ponta: metade do comprimento)
        Assert.Equal(r.TdE, r.TdD, 6);
        foreach (var cod in new[] { "K14", "K15", "K16", "K17", "K18", "K19", "K20" })
            Assert.Contains(r.Verificacoes, x => x.Codigo == cod);
        Assert.Contains(r.Memoria, m => m.Titulo.StartsWith("10. Torção"));
        // as faces laterais recebem barras (entram como pele) e o estribo cobre cortante + torção
        Assert.True(r.Pele.NPorFace > 0);
        Assert.True(r.Verificacoes.First(x => x.Codigo == "K15").Situacao == SituacaoVerificacao.Ok);
        Assert.Equal(SituacaoVerificacao.Conferir, r.Verificacoes.First(x => x.Codigo == "K19").Situacao);
        // a carga da aba soma nas verticais
        var semAba = CalculoViga.Calcular(VigaL(TemplateVigaPreMoldada.AbaDir), new CargasViga { G = 15, Q = 5, ApoioE = 15, ApoioD = 15 });
        Perto(semAba.MdMax, r.MdMax, 0.001);
        Assert.Equal(0, semAba.TdMax);
    }

    [Fact]
    public void TInvertidoUsaMontagemComLajeDeUmLado()
    {
        var c = new CargasViga { GAba = 15, QAba = 5, ApoioE = 15, ApoioD = 15 };
        var r = CalculoViga.Calcular(VigaL(TemplateVigaPreMoldada.AbaDois), c);
        // montagem: 1,4·15·22,5 > sobrecarga de um lado 1,4·5·22,5 → mt = 1,4·15·22,5
        Perto(1.4 * 15 * 22.5 / 100 * 300 / 100, r.TdE, 0.01);
        // verticais com os dois lados
        var ref2 = CalculoViga.Calcular(VigaL(TemplateVigaPreMoldada.AbaDois), new CargasViga { G = 30, Q = 10, ApoioE = 15, ApoioD = 15 });
        Perto(ref2.MdMax, r.MdMax, 0.001);
    }

    [Fact]
    public void AlmaEstreitaDemaisNaoPassaNasBielas()
    {
        var v = VigaL(TemplateVigaPreMoldada.AbaDir);
        v["B"] = "14"; v["H"] = "40";
        var r = CalculoViga.Calcular(v, new CargasViga { GAba = 60, QAba = 20, ApoioE = 15, ApoioD = 15 });
        Assert.Equal(SituacaoVerificacao.NaoAtende, r.Verificacoes.First(x => x.Codigo == "K14").Situacao);
    }
}
