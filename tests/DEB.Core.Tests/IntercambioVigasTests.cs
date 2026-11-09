using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Intercambio;
using DEB.Core.Persistencia;
using DEB.Core.Templates;
using DEB.Core.Templates.VigaPreMoldada;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Vigas do Revit → template da viga no CAD (2026-11-09).</summary>
[Collection("Pilar")]
public class IntercambioVigasTests
{
    private static readonly TemplateVigaPreMoldada T = new TemplateVigaPreMoldada();

    /// <summary>Perfil (x, z) de uma viga h = 75, L = 466, com dente 45 x 25 à esquerda e ponta reta à direita (V143).</summary>
    private static List<(double, double)> PerfilV143() => new List<(double, double)>
    {
        (0, 30), (0, 75), (25, 30), (25, 0), (466, 0), (466, 75),
    };

    [Fact]
    public void DenteRecortadoEmbaixoEPontaReta()
    {
        var avisos = new List<string>();
        var (e, d) = PerfilViga.Dentes(PerfilV143(), 466, 75, avisos);
        Assert.Empty(avisos);
        Assert.Equal((45.0, 25.0), (e.Altura, e.Comprimento));
        Assert.Null(d);
    }

    [Fact]
    public void DentesNasDuasPontas()
    {
        var pts = new List<(double, double)> { (0, 15), (0, 50), (25, 15), (25, 0), (575, 0), (575, 15), (600, 15), (600, 50) };
        var avisos = new List<string>();
        var (e, d) = PerfilViga.Dentes(pts, 600, 50, avisos);
        Assert.Empty(avisos);
        Assert.Equal((35.0, 25.0), (e.Altura, e.Comprimento));
        Assert.Equal((35.0, 25.0), (d.Altura, d.Comprimento));
    }

    [Fact]
    public void PontaQueNaoChegaAoTopoAvisa()
    {
        // recorte em cima (dente invertido): não reconhecido, considerada reta com aviso
        var pts = new List<(double, double)> { (0, 0), (0, 30), (25, 30), (25, 50), (300, 50), (300, 0) };
        var avisos = new List<string>();
        var (e, d) = PerfilViga.Dentes(pts, 300, 50, avisos);
        Assert.Null(e);
        Assert.Null(d);
        Assert.Contains(avisos, a => a.Contains("ponta esquerda") && a.Contains("topo"));
    }

    [Fact]
    public void InvertidasNoMesmoGrupo()
    {
        var g = new DentePeca { Altura = 45, Comprimento = 25 };
        var grupos = AgrupadorVigas.Agrupar(new[]
        {
            new VigaMedida { Id = "10", B = 20, H = 75, L = 466, DenteE = g },
            new VigaMedida { Id = "11", B = 20, H = 75, L = 466, DenteD = new DentePeca { Altura = 45, Comprimento = 25 } },   // invertida
            new VigaMedida { Id = "12", B = 20, H = 75, L = 466 },                                                            // sem dente
            new VigaMedida { Id = "13", B = 40, H = 50, L = 600 },
        });
        Assert.Equal(3, grupos.Count);
        Assert.Equal(new[] { "V1", "V2", "V3" }, grupos.Select(x => x.Marca));
        var comDente = grupos.Single(x => x.Quantidade == 2);
        Assert.Equal(new[] { "10", "11" }, comDente.Elementos.Select(e => e.Id));
        Assert.Equal(comDente.Elementos[0].Giro == 0 ? 180 : 0, comDente.Elementos[1].Giro);
        Assert.True((comDente.DenteE == null) != (comDente.DenteD == null));
        Assert.Null(grupos[0].DenteE ?? grupos[0].DenteD);   // a seção menor e sem dente vem primeiro
    }

    [Fact]
    public void ArquivoComVigasIdaEVolta()
    {
        var a = new ArquivoPecas
        {
            Origem = "Projeto1.rvt",
            Vigas = { new VigaPeca { Marca = "V1", B = 20, H = 50, L = 596, DenteE = new DentePeca { Altura = 35, Comprimento = 25 },
                                     Elementos = { new ElementoPeca { Id = "797858" }, new ElementoPeca { Id = "797860", Giro = 180 } } } },
        };
        var b = ArquivoPecas.DeJson(Json.Ler(Json.Gravar(a.ParaJson())));
        var v = Assert.Single(b.Vigas);
        Assert.Equal(("V1", 20.0, 50.0, 596.0, 2), (v.Marca, v.B, v.H, v.L, v.Quantidade));
        Assert.Equal(35, v.DenteE.Altura);
        Assert.Null(v.DenteD);
        Assert.Empty(b.Pilares);
        // arquivo só com pilares (versão de antes das vigas) continua sendo lido
        Assert.Empty(ArquivoPecas.DeJson(Json.Ler("{\"formato\": \"DEB.Pecas\", \"versao\": 1, \"pilares\": []}")).Vigas);
    }

    [Fact]
    public void ValoresDaVigaPeloCatalogoDeDentes()
    {
        var p = new VigaPeca { Marca = "V7", B = 20, H = 75, L = 466, DenteE = new DentePeca { Altura = 45, Comprimento = 25 },
                               Elementos = { new ElementoPeca(), new ElementoPeca(), new ElementoPeca() } };
        var avisos = new List<string>();
        var v = ConversorVigaPeca.Valores(p, new ValoresTemplate(T.Parametros), TipoDente.Padrao(), avisos);
        Assert.Empty(avisos);
        Assert.Equal(("V7", "3", "20", "75", "466", "DG 45x25", TemplateVigaPreMoldada.Reta),
                     (v["Nome"], v["Quantidade"], v["B"], v["H"], v["L"], v["ExtE"], v["ExtD"]));
        Assert.Empty(CatalogoTemplates.Validar(T, v));
        var r = T.Gerar(v, new ConfiguracaoArmadura { AlturaTextoMm = 2 });
        Assert.Equal("V7", r.Detalhamento);
        Assert.NotEmpty(r.Barras);
    }

    [Fact]
    public void DenteForaDoCatalogoNaoGeraAViga()
    {
        var p = new VigaPeca { Marca = "V8", B = 20, H = 50, L = 600, DenteD = new DentePeca { Altura = 30, Comprimento = 20 } };
        var avisos = new List<string>();
        Assert.Null(ConversorVigaPeca.Valores(p, new ValoresTemplate(T.Parametros), TipoDente.Padrao(), avisos));
        var a = Assert.Single(avisos);
        Assert.Contains("V8", a);
        Assert.Contains("ponta direita", a);
        Assert.Contains("ARMDENTES", a);
    }

    [Fact]
    public void DuasPontasForaDoCatalogoNumAvisoSo()
    {
        var g = new DentePeca { Altura = 25, Comprimento = 25 };
        var p = new VigaPeca { Marca = "V9", B = 20, H = 50, L = 551, DenteE = g, DenteD = g };
        var avisos = new List<string>();
        Assert.Null(ConversorVigaPeca.Valores(p, new ValoresTemplate(T.Parametros), TipoDente.Padrao(), avisos));
        var a = Assert.Single(avisos);
        Assert.Contains("ponta esquerda", a);
        Assert.Contains("ponta direita", a);
    }

    [Fact]
    public void DentesQueFaltamSaoCriadosNoCatalogo()
    {
        // Teste_PM.rvt: três grupos 20 x 50 com dente 25 x 25 nas duas pontas → um tipo só, com a armadura padrão
        var g = new DentePeca { Altura = 25, Comprimento = 25 };
        var vigas = new[] { 546, 551, 556 }.Select(l => new VigaPeca { Marca = "V", B = 20, H = 50, L = l, DenteE = g, DenteD = g }).ToList();
        vigas.Add(new VigaPeca { Marca = "VR", B = 20, H = 75, L = 466, DenteE = new DentePeca { Altura = 45.3, Comprimento = 25 } }); // já existe (DG 45x25)
        var cat = TipoDente.Padrao();
        var novos = CatalogoAutomatico.Dentes(vigas, cat);
        var n = Assert.Single(novos);
        Assert.Equal(("DG 25x25", 25.0, 25.0), (n.Nome, n.Altura, n.Comprimento));
        Assert.Equal(new TipoDente().TiranteQtd, n.TiranteQtd);
        cat.AddRange(novos);
        var avisos = new List<string>();
        var v = ConversorVigaPeca.Valores(vigas[0], new ValoresTemplate(T.Parametros), cat, avisos);
        Assert.Empty(avisos);
        Assert.Equal(("DG 25x25", "DG 25x25"), (v["ExtE"], v["ExtD"]));
    }

    [Fact]
    public void ConsolosQueFaltamSaoCriadosSimplesOuDuplos()
    {
        // Teste_PM P2: consolos 20 × 23,5 (ponta 21,5) × 25 nas faces 1, 2 e 3 → 1 e 3 = par (duplo); 2 = um só, trapezoidal
        ConsoloPeca C(int f) => new ConsoloPeca { Face = f, Inicio = 15, Fim = 35, Base = 550, Topo = 573.5, Balanco = 25, AlturaPonta = 21.5 };
        var p = new PilarPeca { Marca = "P2", B = 50, H = 50, L = 600, Consolos = { C(3), C(2), C(1) } };
        var cat = Templates.PilarPreMoldado.TipoConsolo.Padrao();
        var novos = CatalogoAutomatico.Consolos(new[] { p }, cat);
        Assert.Equal(new[] { "CD 20x23,5-21,5x25", "CT 20x23,5-21,5x25" }, novos.Select(t => t.Nome).OrderBy(x => x));
        Assert.True(novos.Single(t => t.Nome.StartsWith("CD")).Duplo);
        Assert.All(novos, t => Assert.Equal(15, t.AlmB));   // almofada cabe no consolo de 20 (20 − 2 × 2,5)
        cat.AddRange(novos);
        var avisos = new List<string>();
        var linhas = ConversorPilarPeca.LinhasConsolos(p, cat, avisos);
        Assert.Empty(avisos);
        Assert.Equal(2, linhas.Count);   // o duplo nas faces 1 e 3 e o simples na face 2
        Assert.Empty(CatalogoAutomatico.Consolos(new[] { p }, cat));
    }

    [Fact]
    public void SecaoNaoRetangularPeloVolume()
    {
        // Projeto1.rvt: "seção retangular com aba laje 26x170" medida como caixa 51 × 170 → volume bem menor que b × h
        double alma = 26 * 170 * 356.0, aba = 25 * 20 * 356.0;
        Assert.NotNull(PerfilViga.ConferirSecao(alma + aba, 51, 170, 356, null, null));
        // retangular com dentes 25 x 25 e dois furos de pino ø3,2: passa
        var g = new DentePeca { Altura = 25, Comprimento = 25 };
        double cheio = 20 * 50 * 551.0 - 2 * 20 * 25 * 25 - 2 * 3.1416 * 1.6 * 1.6 * 25;
        Assert.Null(PerfilViga.ConferirSecao(cheio, 20, 50, 551, g, g));
        // volume a mais (consolo aninhado na viga) não muda a seção
        Assert.Null(PerfilViga.ConferirSecao(cheio * 1.1, 20, 50, 551, g, g));
    }
}
