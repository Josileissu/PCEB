using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Templates;
using DEB.Core.Templates.VigaPreMoldada;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Viga pré-moldada no padrão do escritório (pranchas 316-6219 V143/V144 e 316-6208 V129, 2026-11-07).</summary>
[Collection("Pilar")]   // usa o contexto de desenho e o catálogo estático
public class VigaPreMoldadaTests
{
    private static ResultadoTemplate Gerar(Action<ValoresTemplate> ajustar)
    {
        var t = new TemplateVigaPreMoldada();
        var v = new ValoresTemplate(t.Parametros);
        ajustar?.Invoke(v);
        Assert.Empty(CatalogoTemplates.Validar(t, v));
        return t.Gerar(v, new ConfiguracaoArmadura());
    }

    private static bool TemCota(ResultadoTemplate r, double medida) =>
        r.Cotas.Any(k => Math.Abs(Math.Sqrt(Math.Pow(k.P2.X - k.P1.X, 2) + Math.Pow(k.P2.Y - k.P1.Y, 2)) - medida) < 1e-6);

    /// <summary>V144: 20 × 75, L = 308, dente 45 × 25 nas duas pontas, 1 furo ø32 a 11,5 cm — prancha: 0,432 m³ e 1,080 t.</summary>
    [Fact]
    public void V144FormaComDentes()
    {
        var r = Gerar(v => { v["Nome"] = "V144"; v["L"] = "308"; });
        Assert.Equal("V144", r.Detalhamento);
        Assert.Contains(r.Textos, x => x.Texto == "V144 (20 x 75)");
        Assert.Contains(r.Textos, x => x.Texto == "FÔRMAS EM PLANTA");
        Assert.Contains(r.Textos, x => x.Texto == "FÔRMAS EM ELEVAÇÃO");
        // cotas da prancha: 11.5 | 13.5 (furo), 25 | 258 | 25, 308; alturas 30 + 45; furo no meio da largura (10 | 10)
        foreach (var m in new[] { 11.5, 13.5, 25, 258, 308, 30, 45, 10, 75, 20 }) Assert.True(TemCota(r, m), $"cota {m}");
        // contorno da elevação com os dois recortes: 8 vértices
        Assert.Contains(r.Contornos, k => k.Layer == "ARM-FORMA" && k.Fechado && k.Vertices.Count == 8);
        Assert.Equal(2, r.Circulos.Count(k => k.Layer == "ARM-FURO"));
        // seções A-A (dente), B-B (vão), C-C (dente)
        foreach (var s in new[] { "SEÇÃO A-A", "SEÇÃO B-B", "SEÇÃO C-C" }) Assert.Contains(r.Textos, x => x.Texto == s);
        var prod = r.Tabelas.Single(t => t.Titulo.StartsWith("INFORMAÇÕES"));
        Assert.Contains(prod.Linhas, l => l[1] == "0,432 m³");
        Assert.Contains(prod.Linhas, l => l[1] == "1,080 ton.");
        Assert.Contains(r.Textos, x => x.Texto == "4) UTILIZAR NEOPRENE 15 mm;");
        Assert.NotNull(r.PosicaoRA);
        Assert.Empty(r.Avisos);
    }

    [Fact]
    public void VolumeECgComDentes()
    {
        var dente = new TipoDente { Altura = 45, Comprimento = 25 };
        var e = new ExtremidadeViga { Dente = dente };
        var d = new ExtremidadeViga();
        // 20 × 75 × 466 − 20 × 30 × 25 (só a ponta esquerda com dente)
        Assert.Equal((20 * 75 * 466 - 20 * 30 * 25) / 1e6, TemplateVigaPreMoldada.Volume(20, 75, 466, e, d), 9);
        // o CG anda para a direita (o recorte tira concreto da esquerda)
        double cg = TemplateVigaPreMoldada.CentroDeGravidade(20, 75, 466, e, d);
        Assert.True(cg > 233);
        Assert.Equal(233, TemplateVigaPreMoldada.CentroDeGravidade(20, 75, 466, e, new ExtremidadeViga { Dente = dente }), 6);
    }

    [Fact]
    public void FurosDePinoPelaLargura()
    {
        // padrão do escritório (2026-11-11): até 25 cm, 1 furo; 30 cm ou mais, 2
        Assert.Equal((1, 1, 2, 2), (TemplateVigaPreMoldada.FurosPorLargura(20), TemplateVigaPreMoldada.FurosPorLargura(25),
                                    TemplateVigaPreMoldada.FurosPorLargura(30), TemplateVigaPreMoldada.FurosPorLargura(40)));
        var r20 = Gerar(v => { v["B"] = "20"; });
        var r30 = Gerar(v => { v["B"] = "30"; });
        Assert.Equal(2, r20.Circulos.Count(k => k.Layer == "ARM-FURO"));   // um por ponta
        Assert.Equal(4, r30.Circulos.Count(k => k.Layer == "ARM-FURO"));   // dois por ponta
        Assert.Contains(r30.Textos, t => t.Texto.StartsWith("2 Furos"));
    }

    [Fact]
    public void PontasRetasSemFurosTemUmaSecao()
    {
        var r = Gerar(v => { v["ExtE"] = TemplateVigaPreMoldada.Reta; v["ExtD"] = TemplateVigaPreMoldada.Reta; v["FurosModo"] = TemplateVigaPreMoldada.FurosDigitados; v["FurosE"] = "0"; v["FurosD"] = "0"; });
        Assert.Contains(r.Textos, x => x.Texto == "SEÇÃO A-A");
        Assert.DoesNotContain(r.Textos, x => x.Texto == "SEÇÃO B-B");
        Assert.Contains(r.Contornos, k => k.Layer == "ARM-FORMA" && k.Fechado && k.Vertices.Count == 4);
        Assert.DoesNotContain(r.Circulos, k => k.Layer == "ARM-FURO");
        Assert.Contains(r.Tabelas.Single().Linhas, l => l[1] == "0,699 m³");
    }

    [Fact]
    public void DenteForaDoCatalogoViraRetaComAviso()
    {
        var t = new TemplateVigaPreMoldada();
        var v = new ValoresTemplate(t.Parametros);
        v["ExtE"] = "XYZ";
        var avisos = new List<string>();
        var e = TemplateVigaPreMoldada.LerExtremidade(v, true, TemplateVigaPreMoldada.CatalogoDentes, avisos);
        Assert.False(e.TemDente);
        Assert.Single(avisos);
    }

    [Fact]
    public void AlcasSimetricasAoCg()
    {
        var t = new TemplateVigaPreMoldada();
        var v = new ValoresTemplate(t.Parametros);
        var e = new ExtremidadeViga { Dente = TipoDente.Padrao()[0] };
        var xa = TemplateVigaPreMoldada.PosicaoAlcas(v, 20, 466, e, e);
        Assert.Equal(466 * 0.2, xa[0], 1);
        Assert.Equal(466 * 0.8, xa[1], 1);
    }
}
