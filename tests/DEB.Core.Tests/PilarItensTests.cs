using System;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Templates;
using DEB.Core.Templates.PilarPreMoldado;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Etapa 4 do template do pilar: içamento, montagem, inserto e detalhes padrão.</summary>
[Collection("Pilar")]
public class PilarItensTests
{
    private static readonly TemplatePilarPreMoldado T = new TemplatePilarPreMoldado();

    private static ResultadoTemplate Gerar(params (string k, string v)[] mudancas)
    {
        var v = new ValoresTemplate(T.Parametros);
        foreach (var (k, x) in mudancas) v[k] = x;
        Assert.Empty(CatalogoTemplates.Validar(T, v));
        return T.Gerar(v, new ConfiguracaoArmadura { AlturaTextoMm = 2 });
    }

    [Fact]
    public void AlcasSimetricasAoCG()
    {
        // CG no meio: alças a 20 % das pontas
        var (z1, z2) = TemplatePilarPreMoldado.PosicaoAlcas(1000, 500, 20);
        Assert.Equal(200, z1, 6);
        Assert.Equal(800, z2, 6);
        // CG mais baixo (consolos): as duas descem juntas, o CG fica no meio delas
        (z1, z2) = TemplatePilarPreMoldado.PosicaoAlcas(1000, 470, 20);
        Assert.Equal(470, (z1 + z2) / 2, 6);
        Assert.Equal(600, z2 - z1, 6);
        // valores digitados mandam
        (z1, z2) = TemplatePilarPreMoldado.PosicaoAlcas(1000, 470, 20, 365, 0);
        Assert.Equal(365, z1, 6);
        Assert.Equal(8, TemplatePilarPreMoldado.RaioGrampoFuro(100), 6);
    }

    [Fact]
    public void AlcaFuroInsertoEDetalhesDoP61()
    {
        var r = Gerar();
        var alca = r.Barras.Single(b => b.Nome == "alca");
        Assert.Equal("CP-190", alca.Aco);             // NBR 9062, 5.3.3.5: alça de cordoalha (nunca CA-50)
        Assert.Equal(12.7, alca.Bitola);
        Assert.Equal(2, alca.Quantidade);             // 2x2 ø12,7 (2 cordoalhas × 2 alças)
        Assert.Equal(2, alca.Repeticoes);
        Assert.Contains(-1.0, alca.Bulges);
        var reforco = r.Barras.Single(b => b.Nome == "reforcofuro");
        Assert.Equal(8, reforco.Quantidade);
        Assert.Equal(3, r.Circulos.Count(c => c.Layer == "ARM-FURO" && Math.Abs(c.Raio - 5) < 1e-9)); // vistas 1 e 3 + detalhe do reforço
        Assert.Contains(r.Textos, t => t.Texto == "FURO ø100mm");
        Assert.Contains(r.Textos, t => t.Texto == "25CB660");
        Assert.Contains(r.Textos, t => t.Texto == "GABARITO GC1A");
        Assert.Contains(r.Textos, t => t.Texto == "DETALHE 01 - CHAVES DE CISALHAMENTO");
        Assert.Contains(r.Textos, t => t.Texto == "ESPAÇAMENTO BARRAS");
        Assert.Contains(r.Textos, t => t.Texto == "REFORÇO NOS FUROS DOS PILARES");
        Assert.Contains(r.Textos, t => t.Texto == "ATENÇÃO!");
        // detalhe típico (raio de curvatura, emendas, comprimento das barras) removido do pilar (pedido do usuário, 2026-11-07)
        Assert.DoesNotContain(r.Textos, t => t.Texto == "RAIO (r) DE CURVATURA PARA GANCHOS");
        Assert.DoesNotContain(r.Tabelas, t => t.Titulo.StartsWith("BITOLA (Ø)"));
        // alças pelo CG: cotas base → alça → alça → topo somam o comprimento
        var cadeia = r.Cotas.Where(c => Math.Abs(c.Angulo - Math.PI / 2) < 1e-9 && c.Texto == "").ToList();
        Assert.Contains(cadeia, c => Math.Abs(c.P1.Y) < 1e-9 && c.P2.Y > 200 && c.P2.Y < 300);
    }

    [Fact]
    public void AlcaDeTopoSemFuroESemDetalhes()
    {
        var r = Gerar(("Montagem", TemplatePilarPreMoldado.MontagemAlcaTopo), ("DetalhesPadrao", "Não"), ("Insertos", ""));
        Assert.Equal(3, r.Barras.Single(b => b.Nome == "alca").Repeticoes);
        Assert.DoesNotContain(r.Barras, b => b.Nome == "reforcofuro");
        Assert.DoesNotContain(r.Textos, t => t.Texto.StartsWith("DETALHE 01"));
        Assert.DoesNotContain(r.Textos, t => t.Texto == "25CB660");
    }

    [Fact]
    public void InsertosDoCatalogoNoTopoENaFace()
    {
        var r = Gerar(("Insertos", "25CB660|Topo|0|0;CHAPA 20x20|Face 4|800|0"));
        Assert.Contains(r.Textos, t => t.Texto == "25CB660");
        Assert.Contains(r.Textos, t => t.Texto == "INSERTO CHAPA 20x20");
        // chumbadores: 2 por vista × 4 vistas; chapa: retângulo de frente e espessura nas duas vistas de lado
        Assert.True(r.Contornos.Count(c => c.Layer == "ARM-INSERTO" && !c.Fechado) >= 8);
        Assert.Equal(1 + 3, r.Contornos.Count(c => c.Layer == "ARM-INSERTO" && c.Fechado)); // gabarito + chapa de frente + 2 de lado
        var avisos = new System.Collections.Generic.List<string>();
        var v = new ValoresTemplate(T.Parametros) { ["Insertos"] = "CHAPA 20x20|Topo|0|0;XX|Topo|0|0;25CB660|Face 9|10|0" };
        var lidos = TemplatePilarPreMoldado.LerInsertos(v, TemplatePilarPreMoldado.CatalogoInsertos, 1000, avisos);
        Assert.Single(lidos);
        Assert.Contains(avisos, a => a.Contains("de face") && a.Contains("no topo"));
        Assert.Contains(avisos, a => a.Contains("não existe"));
        Assert.Contains(avisos, a => a.Contains("inválido"));
    }

    [Fact]
    public void PerfilDasChavesComoODetalhe01()
    {
        // passo 5: retos de 4 alternando recuado (−1) e na face (0), chanfros de 1 (referencia_chaves_cisalhamento.png)
        var (perfil, arestas) = TemplatePilarPreMoldado.PerfilChaves(5, 4, 1, 2);
        Assert.Equal(new (double, double)[] { (-1, 0), (-1, 4), (0, 5), (0, 9), (-1, 10) }, perfil);
        Assert.Equal(new (double, double)[] { (-1, 4), (0, 5), (0, 9), (-1, 10) }, arestas); // duas arestas por chanfro
    }

    [Fact]
    public void AvisoDoFuroForaDaFaixa()
    {
        var r = Gerar(("FuroPct", "45"));
        Assert.Contains(r.Avisos, a => a.Contains("25 % a 35 %"));
    }

    [Fact]
    public void AlcaAncoraNoFundoDoPilar()
    {
        // decisão do usuário (2026-11-08): com embutimento 0 as pernas vão até a face oposta menos o cobrimento dela
        var t = new TemplatePilarPreMoldado();
        var v = new ValoresTemplate(t.Parametros);
        v["AlcaFace"] = "2"; v["CobFace4"] = "3";
        Assert.Equal(60 - 3, TemplatePilarPreMoldado.EmbutimentoAlca(v, 40, 60));
        v["AlcaFace"] = "1"; v["CobFace3"] = "2.5";
        Assert.Equal(40 - 2.5, TemplatePilarPreMoldado.EmbutimentoAlca(v, 40, 60));
        v["AlcaEmb"] = "45";
        Assert.Equal(45, TemplatePilarPreMoldado.EmbutimentoAlca(v, 40, 60));
    }
}
