using System;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Templates;
using DEB.Core.Templates.Blocos;
using Xunit;

namespace DEB.Core.Tests;

public class BlocoEstacasTests
{
    private static double Comp(BarraTemplate b, int i) =>
        Math.Sqrt(Math.Pow(b.Vertices[i + 1].X - b.Vertices[i].X, 2) + Math.Pow(b.Vertices[i + 1].Y - b.Vertices[i].Y, 2));

    private static double Largura(BarraTemplate b) => b.Vertices.Max(p => p.X) - b.Vertices.Min(p => p.X);

    /// <summary>BLOCO B3 do exemplo do usuário (2026-11-06): 2 estacas ø25 a 75, 110 × 50 × 45, 19 pilares.</summary>
    [Fact]
    public void BlocoB3DoExemplo()
    {
        var t = new TemplateBlocoEstacas();
        var v = new ValoresTemplate(t.Parametros);
        v["Pilares"] = "P1, P2, P3, P5, P6, P7, P8, P9, P11, P14, P16, P17, P18, P19, P20, P21, P26, P28 e P30";
        Assert.Empty(CatalogoTemplates.Validar(t, v));
        var r = t.Gerar(v, new ConfiguracaoArmadura());

        Assert.Equal("BLOCO B3", r.Detalhamento);
        Assert.Equal(19, r.DadosDetalhamento.Repeticoes);
        Assert.Contains(r.Textos, x => x.Texto.EndsWith("P28 e P30 (19x)"));

        // N1: 4 ø12.5 c/10, 103 + pernas de 15 (C = 133)
        var n1 = r.Barras[0];
        Assert.Equal("principal x", n1.Nome);
        Assert.Equal(12.5, n1.Bitola); Assert.Equal(4, n1.Quantidade); Assert.Equal(10, n1.Espacamento);
        Assert.Equal(15, Comp(n1, 0), 1); Assert.Equal(103, Comp(n1, 1), 1); Assert.Equal(15, Comp(n1, 2), 1);
        // N2: 4 ø6.3 c/10, 103 + pernas de 35 (C = 173)
        var n2 = r.Barras[1];
        Assert.Equal("superior x", n2.Nome);
        Assert.Equal(4, n2.Quantidade); Assert.Equal(35, Comp(n2, 0), 1); Assert.Equal(103, Comp(n2, 1), 1);
        // N3/N4: 2 ø6.3 nos cantos (azul)
        Assert.Equal("secundária inferior x", r.Barras[2].Nome); Assert.Equal(2, r.Barras[2].Quantidade); Assert.Equal(15, Comp(r.Barras[2], 0), 1);
        Assert.Equal("secundária superior x", r.Barras[3].Nome); Assert.Equal(2, r.Barras[3].Quantidade); Assert.Equal(35, Comp(r.Barras[3], 0), 1);
        Assert.Equal("ARM-BARRA-2", r.Barras[3].Layer);
        // N5: 6 estribos verticais 44 × 35 c/20; N6: 3 horizontais 104 × 44 c/15
        var n5 = r.Barras[4];
        Assert.Equal("estribo vertical (yz)", n5.Nome);
        Assert.Equal(6, n5.Quantidade); Assert.Equal(20, n5.Espacamento); Assert.Equal(44, Largura(n5), 1);
        Assert.Equal(35, n5.Vertices.Max(p => p.Y) - n5.Vertices.Min(p => p.Y), 1);
        var n6 = r.Barras[5];
        Assert.Equal("estribo horizontal", n6.Nome);
        Assert.Equal(3, n6.Quantidade); Assert.Equal(15, n6.Espacamento); Assert.Equal(104, Largura(n6), 1);
        Assert.True(n5.Estribo && n6.Estribo);
        // arranque detalhado: 4 barras ø10 com espera = traspasse
        var arr = r.Barras.Single(b => b.Nome == "arranque");
        Assert.Equal(4, arr.Quantidade);
        Assert.DoesNotContain(r.Barras, b => b.Nome.EndsWith(" y"));

        Assert.Contains(r.Textos, x => x.Texto == "CORTE A-A");
        Assert.Contains(r.Textos, x => x.Texto == "CORTE B-B");
        Assert.Contains(r.Textos, x => x.Texto == "LASTRO DE");
        Assert.Contains(r.Hachuras, x => x.Padrao == "SOLID");
        Assert.All(r.Faixas, f => Assert.Contains(r.Barras, b => b.Nome == f.Barra));
        Assert.NotNull(r.PosicaoRA);
        // cadeia da forma 17,5 | 37,5 | 37,5 | 17,5
        Assert.Contains(r.Cotas, k => Math.Abs(Math.Abs(k.P2.X - k.P1.X) - 17.5) < 1e-6);
        Assert.Contains(r.Cotas, k => Math.Abs(Math.Abs(k.P2.X - k.P1.X) - 37.5) < 1e-6);
    }

    [Fact]
    public void QuatroEstacasTemPrincipalNasDuasDirecoes()
    {
        var t = new TemplateBlocoEstacas();
        var v = new ValoresTemplate(t.Parametros);
        v["Lx"] = "130"; v["Ly"] = "130"; v["h"] = "60"; v["NEstY"] = "2"; v["EspY"] = "75"; v["PilarBy"] = "20"; v["PilarBx"] = "20";
        var r = t.Gerar(v, new ConfiguracaoArmadura());
        Assert.Equal(8, r.Barras.Single(b => b.Nome == "principal x").Quantidade);   // 4 por linha de estacas × 2
        Assert.Equal(8, r.Barras.Single(b => b.Nome == "principal y").Quantidade);
        Assert.Contains(r.Barras, b => b.Nome == "estribo vertical (yz)");
        Assert.Contains(r.Barras, b => b.Nome == "estribo vertical (xz)");
        Assert.DoesNotContain(r.Avisos, a => a.Contains("sai do bloco"));
    }

    [Fact]
    public void UmaEstacaSoSecundariasEEstribos()
    {
        var t = new TemplateBlocoEstacas(umaEstaca: true);
        Assert.Equal(TemplateBlocoEstacas.IdUmaEstaca, t.Id);
        var v = new ValoresTemplate(t.Parametros);
        v["ArrModo"] = TemplateBlocoEstacas.ArrDesenhar;
        var r = t.Gerar(v, new ConfiguracaoArmadura());
        Assert.DoesNotContain(r.Barras, b => b.Nome.StartsWith("principal"));
        Assert.Contains(r.Barras, b => b.Nome == "secundária inferior x");
        Assert.Contains(r.Barras, b => b.Nome == "secundária inferior y");
        Assert.DoesNotContain(r.Barras, b => b.Nome == "arranque");
        Assert.All(r.Barras, b => Assert.True(b.Vertices.Count >= 2, b.Nome));
        Assert.Equal(1, r.DadosDetalhamento.Repeticoes);
    }

    [Fact]
    public void ListaDePilares()
    {
        Assert.Equal(new[] { "P1", "P2", "P5" }, TemplateBlocoEstacas.LerPilares("P1, P2 e P5"));
        Assert.Equal("P1, P2 e P5 (3x)", TemplateBlocoEstacas.TextoPilares(TemplateBlocoEstacas.LerPilares("P1;P2;P5"), 3));
    }
}
