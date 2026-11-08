using System;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Motor.Fundacoes;
using DEB.Core.Templates;
using Xunit;

namespace DEB.Core.Tests.Motor;

/// <summary>Blocos separados por número de estacas e variante no ARMTEMPLATE (pedido do usuário, 2026-11-08).</summary>
[Collection("Pilar")]
public class TemplateBlocoVarianteTests
{
    [Fact]
    public void VinteEntradasComIdsUnicosNasCategoriasCertas()
    {
        var ts = TemplateBlocoVariante.Todas().ToList();
        Assert.Equal(20, ts.Count);
        Assert.Equal(20, ts.Select(t => t.Id).Distinct().Count());
        Assert.Equal(4, ts.Count(t => t.Categoria == CategoriaElemento.Bloco1Estaca));
        Assert.Equal(4, ts.Count(t => t.Categoria == CategoriaElemento.Bloco4Estacas));
        Assert.All(ts, t => Assert.DoesNotContain(t.Parametros, p => p.Nome == "Ligacao" || p.Nome == "NEstX" || p.Nome == "Arranjo"));
        Assert.Contains(CatalogoTemplates.Categorias, c => c.Categoria == CategoriaElemento.Bloco5Estacas);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void CadaVarianteGeraSoOQueEDela(int estacas)
    {
        var cfg = new ConfiguracaoArmadura();
        foreach (var variante in new[] { VarianteBloco.Simples, VarianteBloco.Arranque, VarianteBloco.Embutimento, VarianteBloco.EmbutimentoColarinho })
        {
            var t = new TemplateBlocoVariante(estacas, variante);
            var r = t.Gerar(new ValoresTemplate(t.Parametros), cfg);
            string ctx = $"{estacas} estaca(s), {variante}";
            Assert.DoesNotContain(r.Avisos, a => a.Contains("erro inesperado") || a.Contains("inválidos"));
            Assert.NotEmpty(r.Barras);
            bool temCalice = r.Barras.Any(b => b.Nome.StartsWith("calice") || b.Nome == "suspensao");
            bool temArranque = r.Barras.Any(b => b.Nome.StartsWith("arranque"));
            Assert.True(temCalice == (variante == VarianteBloco.Embutimento || variante == VarianteBloco.EmbutimentoColarinho), ctx + ": cálice");
            Assert.True(temArranque == (variante == VarianteBloco.Arranque), ctx + ": arranque");
            Assert.Equal(estacas == 1, r.Barras.Any(b => b.Nome == "estribo_vertical_y"));
            if (estacas == 1) Assert.DoesNotContain(r.Barras, b => b.Nome.StartsWith("principal"));
        }
    }

    [Fact]
    public void JanelaMostraSoOsCamposDaVariante()
    {
        var simples = new TemplateBlocoVariante(1, VarianteBloco.Simples);
        Assert.DoesNotContain(simples.Parametros, p => p.Nome == "PilarBx" || p.Nome == "Lemb" || p.Nome == "ArrBitola");
        var arr = new TemplateBlocoVariante(2, VarianteBloco.Arranque);
        Assert.Contains(arr.Parametros, p => p.Nome == "ArrBitola");
        Assert.DoesNotContain(arr.Parametros, p => p.Nome == "Lemb" || p.Nome == "CalTopoBitola");
        var emb = new TemplateBlocoVariante(4, VarianteBloco.Embutimento);
        Assert.Contains(emb.Parametros, p => p.Nome == "Lemb");
        Assert.DoesNotContain(emb.Parametros, p => p.Nome == "hColar" || p.Nome == "ArrBitola");
        Assert.Contains(new TemplateBlocoVariante(4, VarianteBloco.EmbutimentoColarinho).Parametros, p => p.Nome == "hColar");
        // valores digitados chegam ao gerador; os fixos não podem ser trocados
        var v = new ValoresTemplate(emb.Parametros);
        v["hBase"] = "80";
        var completo = emb.Completar(v);
        Assert.Equal("80", completo["hBase"]);
        Assert.Equal("2", completo["NEstX"]);
        Assert.Equal("2", completo["NEstY"]);
        Assert.Equal(DEB.Core.Templates.Blocos.TemplateBlocoEstacas.Embutido, completo["Ligacao"]);
    }
}
