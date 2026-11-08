using System;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Templates;
using DEB.Core.Templates.VigaPreMoldada;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Viga pré-moldada, VP-3: protensão por pré-tração (grade de 5 cm, isolamento, nomenclatura, RA em CP-190).</summary>
[Collection("Pilar")]
public class VigaProtensaoTests
{
    private static (ResultadoTemplate R, ValoresTemplate V) Gerar(Action<ValoresTemplate> ajustar)
    {
        var t = new TemplateVigaPreMoldada();
        var v = new ValoresTemplate(t.Parametros);
        v["Protendida"] = "Sim";
        ajustar?.Invoke(v);
        Assert.Empty(CatalogoTemplates.Validar(t, v));
        return (t.Gerar(v, new ConfiguracaoArmadura()), v);
    }

    private const string Grupos = "1|3|12.7|0|0|14;1|2|12.7|100|120|14;2|2|12.7|0|0|14";

    [Fact]
    public void CordoalhasNaGradeDe5cmSimetricaENumeradas()
    {
        var (_, v) = Gerar(v => { v["B"] = "40"; v["Cordoalhas"] = Grupos; });
        var gs = TemplateVigaPreMoldada.LerCordoalhas(v, new System.Collections.Generic.List<string>());
        // fiada 1 (y = 5) com 5 nós: 0, ±5 para o 1º grupo (o do meio) e ±10 para o 2º; fiada 2 (y = 10) com ±2,5
        Assert.Equal(new[] { -5.0, 0, 5 }, gs[0].Cordoalhas.Select(c => c.X).OrderBy(x => x));
        Assert.Equal(new[] { -10.0, 10 }, gs[1].Cordoalhas.Select(c => c.X).OrderBy(x => x));
        Assert.All(gs[0].Cordoalhas, c => Assert.Equal(5, c.Y, 6));
        Assert.Equal(new[] { -2.5, 2.5 }, gs[2].Cordoalhas.Select(c => c.X).OrderBy(x => x));
        Assert.All(gs[2].Cordoalhas, c => Assert.Equal(10, c.Y, 6));
        // numeração de baixo para cima, da esquerda para a direita
        Assert.Equal(new[] { 1, 5 }, gs[1].Cordoalhas.Select(c => c.Numero));
        Assert.Equal(new[] { 2, 3, 4 }, gs[0].Cordoalhas.Select(c => c.Numero));
        Assert.Equal(new[] { 6, 7 }, gs[2].Cordoalhas.Select(c => c.Numero));
    }

    [Fact]
    public void ElevacaoComIsolamentoNaRelacaoEmCP190()
    {
        var (r, _) = Gerar(v => { v["B"] = "40"; v["H"] = "75"; v["L"] = "800"; v["ExtE"] = "Reta"; v["ExtD"] = "Reta"; v["Cordoalhas"] = Grupos; });
        var c1 = r.Barras.Single(b => b.Nome == "cord1");
        Assert.Equal("CP-190", c1.Aco);
        Assert.Equal(3, c1.Quantidade);
        Assert.Equal(800, c1.Vertices.Max(p => p.X) - c1.Vertices.Min(p => p.X), 6);   // de ponta a ponta (cortada na face)
        Assert.Equal(2, r.Hachuras.Count(h => h.Layer == "ARM-ISOLAMENTO"));           // grupo 2: isolado nas duas pontas
        var iso = r.Hachuras.Where(h => h.Layer == "ARM-ISOLAMENTO").Select(h => h.Vertices.Max(p => p.X) - h.Vertices.Min(p => p.X)).OrderBy(x => x);
        Assert.Equal(new[] { 100.0, 120 }, iso.Select(x => Math.Round(x, 6)));
        Assert.Contains(r.Textos, t => t.Texto == "ARMADURAS DE PROTENSÃO - 7 ø 12,7mm CP 190 RB");
        Assert.Contains(r.Textos, t => t.Texto.StartsWith("CORDOALHAS 1, 5 - ø12,7 - FORÇA DE PROTENSÃO = 14"));
        Assert.Contains(r.Textos, t => t.Texto.StartsWith("MANGUEIRA ISOLAMENTO"));
        Assert.Contains(r.Textos, t => t.Texto.StartsWith("NOMENCLATURA E POSICIONAMENTO DAS CORDOALHAS"));
        Assert.Equal(2, r.Circulos.Count(k => k.Layer == "ARM-CORDOALHA-ISOLADA"));
        Assert.Contains(r.Faixas, f => f.Barra == "cord1" && f.Tipo == DEB.Core.Modelo.TipoFaixa.Indicacao);
        Assert.Contains(r.Tabelas.SelectMany(t => t.Linhas), l => l[0].StartsWith("RESISTÊNCIA ATO DA PROT."));
        // grupos iguais na relação (mesma bitola e trecho) com uma indicação só por seção
        Assert.Equal(r.Textos.Count(t => t.Texto.StartsWith("SEÇÃO") && t.Texto.EndsWith("ARMADURA")), r.Faixas.Count(f => f.Barra.StartsWith("cord")));
        // a inferior passiva padrão (3 ø16 no fundo) encosta na 1ª fiada
        Assert.Contains(r.Avisos, a => a.Contains("encosta nas cordoalhas"));
    }

    [Fact]
    public void CordoalhasSuperioresAcimaDasPassivas()
    {
        // h = 95: fiada 18 (y = 90) é superior → linha à parte acima da armadura superior passiva; as de baixo abaixo da inferior
        var (r, _) = Gerar(v =>
        {
            v["B"] = "40"; v["H"] = "95"; v["L"] = "1150"; v["ExtE"] = "Reta"; v["ExtD"] = "Reta";
            v["Cordoalhas"] = "1|5|15.2|0|0|20.5;18|2|12.7|0|0|10";
        });
        double yC(string n) => r.Barras.Single(b => b.Nome == n).Vertices[0].Y;
        double ySupPassiva = yC("long2"), yInfPassiva = yC("long1");
        Assert.True(yC("cord2") > ySupPassiva);
        Assert.True(yC("cord1") < yInfPassiva);
        Assert.Contains(r.Textos, t => t.Texto == "ARMADURAS DE PROTENSÃO SUPERIORES - 2 ø 12,7mm CP 190 RB");
        Assert.Contains(r.Textos, t => t.Texto == "ARMADURAS DE PROTENSÃO - 5 ø 15,2mm CP 190 RB");
    }

    [Fact]
    public void FiadaNoRecorteDoDenteParaNaFace()
    {
        // V144: 20 x 75 com dentes 45 x 25 (recorte de 30): a fiada 1 (y = 5) é cortada na face do recorte; a fiada 7 (y = 35) não
        var (r, _) = Gerar(v => { v["L"] = "466"; v["Cordoalhas"] = "1|2|12.7|0|0|14;7|1|12.7|0|0|14"; });
        var c1 = r.Barras.Single(b => b.Nome == "cord1");
        Assert.Equal(466 - 50, c1.Vertices.Max(p => p.X) - c1.Vertices.Min(p => p.X), 6);
        var c2 = r.Barras.Single(b => b.Nome == "cord2");
        Assert.Equal(466, c2.Vertices.Max(p => p.X) - c2.Vertices.Min(p => p.X), 6);
        Assert.Contains(r.Avisos, a => a.Contains("passa no recorte do dente"));
    }

    [Fact]
    public void AvisosDeLarguraEFcjDeLiberacao()
    {
        // b = 20: cabem 3 por fiada (0, ±5); 5 saem da largura. fcj 18 < 21 MPa (NBR 9062 9.2.5.3)
        var (r, _) = Gerar(v => { v["B"] = "20"; v["Cordoalhas"] = "1|5|12.7|0|0|14"; v["FcjSaque"] = "18"; v["ExtE"] = "Reta"; v["ExtD"] = "Reta"; });
        Assert.Contains(r.Avisos, a => a.Contains("não cabe na largura"));
        Assert.Contains(r.Avisos, a => a.Contains("< 21 MPa"));
    }

    [Fact]
    public void CantoneirasNasPontasComoACan03()
    {
        // VT-1 da 1071: CAN-03 a 5 cm do topo, 12 de altura, nos dois cantos de cada ponta
        var (r, _) = Gerar(v => { v["Protendida"] = "Não"; v["CantE"] = "Sim"; v["CantD"] = "Sim"; });
        Assert.Equal(4, r.Textos.Count(t => t.Texto == "CAN-03" && t.Layer == "ARM-INSERTO"));       // elevação + planta, 2 pontas
        Assert.Equal(6, r.Contornos.Count(k => k.Layer == "ARM-INSERTO" && k.Fechado));                         // 1 na elevação + 2 L na planta, por ponta
        Assert.DoesNotContain(r.Barras, b => b.Nome.StartsWith("can"));                            // não entra na relação do aço
        var (r2, _) = Gerar(v => { v["Protendida"] = "Não"; v["CantE"] = "Sim"; v["CantAlt"] = "45"; });
        Assert.Contains(r2.Avisos, a => a.StartsWith("Cantoneira da ponta esquerda"));
    }

    [Fact]
    public void VigaArmadaSemCordoalhas()
    {
        var (r, _) = Gerar(v => v["Protendida"] = "Não");
        Assert.DoesNotContain(r.Barras, b => b.Nome.StartsWith("cord"));
        Assert.Contains(r.Tabelas.SelectMany(t => t.Linhas), l => l[0].StartsWith("RESISTÊNCIA AO SAQUE"));
    }
}
