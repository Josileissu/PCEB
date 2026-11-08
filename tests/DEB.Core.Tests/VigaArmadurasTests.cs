using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Modelo;
using DEB.Core.Templates;
using DEB.Core.Templates.VigaPreMoldada;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Viga pré-moldada, etapa 2: armaduras passivas (padrão das pranchas 316-6208/6219 e GRU 702).</summary>
[Collection("Pilar")]
public class VigaArmadurasTests
{
    private static (ResultadoTemplate R, ValoresTemplate V) Gerar(Action<ValoresTemplate> ajustar)
    {
        var t = new TemplateVigaPreMoldada();
        var v = new ValoresTemplate(t.Parametros);
        ajustar?.Invoke(v);
        Assert.Empty(CatalogoTemplates.Validar(t, v));
        return (t.Gerar(v, new ConfiguracaoArmadura()), v);
    }

    [Fact]
    public void LongitudinaisForaDaFormaComAFuncao()
    {
        // V129: 40 x 50, L = 767, dentes 35 x 25: inferior entre os dentes (C = 767 − 2·25 − 2·2,5 = 712), superior de ponta a ponta (762)
        var (r, _) = Gerar(v =>
        {
            v["Nome"] = "V129"; v["B"] = "40"; v["H"] = "50"; v["L"] = "767"; v["ExtE"] = "DG 35x25"; v["ExtD"] = "DG 35x25";
            v["Longitudinais"] = "Inferior|4|25|1|0|0||;Superior|2|16|1|0|0||;Pele|2|12.5|1|0|0||";
        });
        var inf = r.Barras.Single(b => b.Nome == "long1");
        Assert.Equal(712, inf.Vertices.Max(p => p.X) - inf.Vertices.Min(p => p.X), 6);
        Assert.Equal(4, inf.Quantidade);
        var sup = r.Barras.Single(b => b.Nome == "long2");
        Assert.Equal(762, sup.Vertices.Max(p => p.X) - sup.Vertices.Min(p => p.X), 6);
        var pele = r.Barras.Single(b => b.Nome == "long3");
        Assert.Equal(2, pele.Repeticoes);                       // "2x2 N… (PELE)": por face × 2 faces
        Assert.Contains(r.Textos, t => t.Texto.StartsWith("ARMADURA INFERIOR"));
        Assert.Contains(r.Textos, t => t.Texto.StartsWith("ARMADURA SUPERIOR"));
        Assert.Contains(r.Textos, t => t.Texto.StartsWith("(PELE)"));
        Assert.Contains(r.Textos, t => t.Texto == "ARMADURAS EM ELEVAÇÃO");
        // a inferior fica abaixo da forma da elevação das armaduras, a superior acima
        Assert.True(inf.Vertices[0].Y < sup.Vertices[0].Y);
    }

    [Fact]
    public void EstribosPorZonaComFaixasEmCadeia()
    {
        var cfg = new ConfiguracaoArmadura();
        var (r, v) = Gerar(v =>
        {
            v["B"] = "40"; v["H"] = "50"; v["L"] = "767"; v["ExtE"] = "DG 35x25"; v["ExtD"] = "DG 35x25";
            v["EstZonaPonta"] = "100"; v["EstEspPonta"] = "10"; v["EstEspVao"] = "15";
        });
        var e = TemplateVigaPreMoldada.LerExtremidade(v, true, TemplateVigaPreMoldada.CatalogoDentes, new List<string>());
        var d = TemplateVigaPreMoldada.LerExtremidade(v, false, TemplateVigaPreMoldada.CatalogoDentes, new List<string>());
        var zonas = TemplateVigaPreMoldada.ZonasEstribos(v, cfg, 767, e, d);
        Assert.Equal(3, zonas.Count);
        Assert.Equal(25 + 2.5 + 0.315, zonas[0].X1, 6);         // a partir da face do dente, a cobrimento
        Assert.Equal(100, zonas[0].X2 - zonas[0].X1, 6);
        Assert.Equal(zonas[0].X2, zonas[1].X1, 6);
        Assert.All(zonas, z => Assert.Equal(CalculoFaixa.Quantidade(z.X2 - z.X1, z.Esp, cfg.EspacamentoInicialFaixa, cfg.ModoQuantidadeFaixa), z.Posicoes.Count));
        var faixas = r.Faixas.Where(f => f.Barra == "estribo" && f.Tipo == TipoFaixa.Ortogonal).ToList();
        Assert.Equal(3, faixas.Count);
        Assert.Equal(new[] { 10.0, 15.0, 10.0 }, faixas.Select(f => f.Espacamento));
        var est = r.Barras.Single(b => b.Nome == "estribo");
        Assert.True(est.Estribo);
        // forma do estribo: medida externa 40 − 2·2,5 por 50 − 2·2,5
        Assert.Equal(35, est.Vertices.Max(p => p.X) - est.Vertices.Min(p => p.X), 1);
    }

    [Fact]
    public void SecaoDeArmaduraComBarrasEmPontoEIndicacoes()
    {
        var (r, _) = Gerar(v =>
        {
            v["B"] = "40"; v["H"] = "50"; v["L"] = "767"; v["ExtE"] = "DG 35x25"; v["ExtD"] = "DG 35x25";
            v["Longitudinais"] = "Inferior|4|25|1|0|0||;Inferior|2|20|2|0|0||;Superior|2|16|1|0|0||";
        });
        Assert.Contains(r.Textos, t => t.Texto == "SEÇÃO B-B - ARMADURA");
        // no vão: 4 + 2 inferiores e 2 superiores; nos dentes: só as superiores (as inferiores param antes dos dentes)
        Assert.Equal(2 * 2 + 8, r.Circulos.Count(k => k.Layer == "ARM-BARRA-SECAO"));
        Assert.Equal(3 + 2, r.Faixas.Count(f => f.Tipo == TipoFaixa.Indicacao));
        // 2ª camada acima da 1ª, com folga de pelo menos 2 cm entre as faces
        var pts = TemplateVigaPreMoldada.PontosNaSecao(
            new LinhaLongViga { Funcao = TemplateVigaPreMoldada.FInferior, Quantidade = 2, Bitola = 20, Camada = 2 }, 40, 50, 2.5, 2.5, 2.5, 6.3,
            new[] { new LinhaLongViga { Funcao = TemplateVigaPreMoldada.FInferior, Quantidade = 4, Bitola = 25, Camada = 1 } });
        double y1 = 2.5 + 0.63 + 1.25;
        Assert.Equal(y1 + 1.25 + 2.5 + 1.0, pts[0].Y, 6);
    }

    [Fact]
    public void AvisaSemPeleEmVigaAlta()
    {
        var (r, _) = Gerar(v => { v["H"] = "95"; v["Longitudinais"] = "Inferior|4|20|1|0|0||;Superior|2|10|1|0|0||"; });
        Assert.Contains(r.Avisos, a => a.Contains("pele"));
    }
}
