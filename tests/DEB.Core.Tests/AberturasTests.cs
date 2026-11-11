using System.Collections.Generic;
using System.Linq;
using DEB.Core.Intercambio;
using DEB.Core.Pecas;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>
/// Viga com aberturas na alma e rebaixo no topo (2026-11-10, relato do usuário: V215 de um modelo real — 20 × 150 com recorte de
/// 10 × 15 no topo e cinco furos de 25 × 20 — aparecia em 13 trechos, com seções de "h = 75" cortadas dentro dos furos).
/// </summary>
public class AberturasTests
{
    private static readonly List<AberturaPeca> Furos = new[] { 102.5, 206.5, 354, 482.5, 652.5 }
        .Select(x => new AberturaPeca { X0 = x, X1 = x + 25, Z0 = 55, Z1 = 75, Passante = true }).ToList();

    [Fact]
    public void BordasDosFurosNaoSaoEstacoes()
    {
        var xs = new List<double> { 0, 32, 748, 780 }.Concat(Furos.SelectMany(f => new[] { f.X0, f.X1 })).ToList();
        var e = Subdivisao.SemBordasDeAberturas(xs, Furos);
        Assert.Equal(new double[] { 0, 32, 748, 780 }, e);
        // furo redondo: os vértices do arco (dentro do furo) também não são estações (2026-11-11)
        var redondo = new[] { new AberturaPeca { X0 = 243, X1 = 253, Z0 = 20, Z1 = 30, Passante = true, Circular = true } };
        Assert.Equal(new double[] { 0, 25, 526, 551 }, Subdivisao.SemBordasDeAberturas(new double[] { 0, 25, 243.6, 244.4, 246.7, 252.3, 526, 551 }, redondo));
        var intervalos = Subdivisao.Intervalos(e, 780);
        Assert.Equal(3, intervalos.Count);   // dente | vão | dente
    }

    [Fact]
    public void CorteFogeDoFuro()
    {
        // meio do vão 32–748 = 390: fora dos furos, fica
        Assert.Equal(390, Subdivisao.PontoDeCorte(32, 748, Furos));
        // intervalo cujo meio cai num furo (354–379): vai para o meio do maior pedaço livre
        double x = Subdivisao.PontoDeCorte(300, 430, Furos);
        Assert.DoesNotContain(Furos, f => x > f.X0 && x < f.X1);
        Assert.Equal((300 + 354) / 2.0, x);
        // sem furos, o meio
        Assert.Equal(50, Subdivisao.PontoDeCorte(0, 100, new List<AberturaPeca>()));
    }

    private static SecaoPoligonal ComRebaixo(double zMin) => new SecaoPoligonal
    {
        Contorno = { (0, zMin), (20, zMin), (20, 150), (10, 150), (10, 135), (0, 135) },
    };

    [Fact]
    public void RebaixoNoTopoELidoComoRessalto()
    {
        // antes: alma 10 com uma "aba" de 10 × 135; o rebaixo do topo é a mesma forma do ressalto (faixa estreita no alto, rente a
        // uma face, onde a laje e a capa encostam): alma 20 × 135 + ressalto 10 × 15
        var i = SecaoComAba.Ler(ComRebaixo(0));
        Assert.Equal((20.0, 135.0, false, 10.0, 15.0, true), (i.B, i.H, i.TemAba, i.RessaltoLargura, i.RessaltoAltura, i.RessaltoEsquerda));   // y de 10 a 20 = lado de y maior
        // no dente cabem as duas leituras; a da mesma alma do vão existe
        Assert.Contains(SecaoComAba.Leituras(ComRebaixo(100)), x => x.B == 20 && x.TemRessalto && x.H == 35);
        // a aba de verdade (L com aba no fundo, 20 de 50) continua sendo aba
        Assert.NotNull(SecaoComAba.Ler(new SecaoPoligonal { Contorno = { (0, 0), (35, 0), (35, 20), (20, 20), (20, 50), (0, 50) } }));
    }

    [Fact]
    public void VigaComFurosPassantesVaiParaOCadComAsAberturas()
    {
        var p = new PecaLevantada
        {
            Id = "1", Marca = "V215", Tipo = TipoPeca.Viga, PreMoldada = true, Comprimento = 780,
            Trechos = { new TrechoPeca { X0 = 0, X1 = 32, Secao = ComRebaixo(100) }, new TrechoPeca { X0 = 32, X1 = 748, Secao = ComRebaixo(0) },
                        new TrechoPeca { X0 = 748, X1 = 780, Secao = ComRebaixo(100) } },
            Aberturas = Furos,
        };
        ConferenciaPecas.Avaliar(p);
        // desde 2026-11-11 os furos passantes vão para o CAD (com o reforço do padrão do escritório)
        Assert.NotEqual(SituacaoPeca.NaoSuportada, p.Situacao);
        Assert.Contains(p.Avisos, a => a.StartsWith("5 abertura(s) na alma") && a.Contains("conferir no CAD"));
        var arq = ConversorLevantamento.Converter(new[] { p }, "m.rvt", "");
        var vf = Assert.Single(arq.Vigas);
        Assert.Equal(5, vf.Aberturas.Count);
        Assert.Equal((102.5, 127.5, 55.0, 75.0, false), (vf.Aberturas[0].X0, vf.Aberturas[0].X1, vf.Aberturas[0].Z0, vf.Aberturas[0].Z1, vf.Aberturas[0].Circular));
        Assert.Equal(4, arq.VersaoNecessaria);
        // ida e volta pelo JSON
        Assert.Null(vf.Aba);   // só ressalto: sem aba (antes saía uma aba de largura 0 que o leitor recusava)
        var lido = ArquivoPecas.DeJson(Persistencia.Json.Ler(Persistencia.Json.Gravar(arq.ParaJson())));
        Assert.Equal(5, lido.Vigas[0].Aberturas.Count);
        Assert.Equal(652.5, lido.Vigas[0].Aberturas[4].X0);
        // no template: "Retangular|115|25|55|20" (centro, largura, fundo, altura)
        var vt = ConversorVigaPeca.Valores(lido.Vigas[0], new Templates.ValoresTemplate(new Templates.VigaPreMoldada.TemplateVigaPreMoldada().Parametros),
            Templates.VigaPreMoldada.TemplateVigaPreMoldada.CatalogoDentes.Concat(CatalogoAutomatico.Dentes(lido.Vigas, Templates.VigaPreMoldada.TemplateVigaPreMoldada.CatalogoDentes)).ToList(),
            new List<string>());
        Assert.StartsWith("Retangular|115|25|55|20;", vt["Aberturas"]);
        // nicho (de um lado só): continua de fora, com o motivo
        var nicho = new PecaLevantada { Id = "9", Marca = "V9", Tipo = TipoPeca.Viga, PreMoldada = true, Comprimento = 780, Trechos = p.Trechos,
            Aberturas = { new AberturaPeca { X0 = 300, X1 = 320, Z0 = 50, Z1 = 70, Passante = false } } };
        ConferenciaPecas.Avaliar(nicho);
        Assert.Equal(SituacaoPeca.NaoSuportada, nicho.Situacao);
        Assert.Empty(ConversorLevantamento.Converter(new[] { nicho }, "m.rvt", "").Vigas);
        // a mesma viga sem os furos vai: alma 20 × 135, dentes de 35 e ressalto 10 × 15
        var sem = new PecaLevantada { Id = "3", Marca = "V216", Tipo = TipoPeca.Viga, PreMoldada = true, Comprimento = 780, Trechos = p.Trechos };
        var v = Assert.Single(ConversorLevantamento.Converter(new[] { sem }, "m.rvt", "").Vigas);
        Assert.Equal((20.0, 135.0, 35.0, 35.0, 10.0, 15.0), (v.B, v.H, v.DenteE.Altura, v.DenteD.Altura, v.Ressalto.Largura, v.Ressalto.Altura));
        // os furos entram na assinatura do grupo: a mesma viga sem furos é outra peça
        var semFuros = new PecaLevantada { Id = "2", Tipo = TipoPeca.Viga, PreMoldada = true, Comprimento = 780, Trechos = p.Trechos };
        ConferenciaPecas.Avaliar(semFuros);
        Assert.Equal(2, ConferenciaPecas.Agrupar(new[] { p, semFuros }).Count);
    }
}
