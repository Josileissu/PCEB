using System.Collections.Generic;
using System.Linq;
using DEB.Core.Intercambio;
using DEB.Core.Pecas;
using DEB.Core.Persistencia;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Conferência (peças levantadas) → arquivo de troca do CAD: o caminho único da exportação (2026-11-09).</summary>
public class ConversorLevantamentoTests
{
    private static TrechoPeca T(double x0, double x1, SecaoPoligonal s) => new TrechoPeca { X0 = x0, X1 = x1, Secao = s };

    /// <summary>Viga 20 × 50, L = 546, com dentes 25 (altura) × 25 nas pontas pedidas; o dente fica em cima (z de 25 a 50).</summary>
    private static PecaLevantada Viga(string id, string marca, bool denteE, bool denteD, double L = 546, bool pm = true)
    {
        var t = new List<TrechoPeca>();
        double a = denteE ? 25 : 0, b = denteD ? L - 25 : L;
        if (denteE) t.Add(T(0, 25, SecaoPoligonal.Retangulo(20, 25, 0, 25)));
        t.Add(T(a, b, SecaoPoligonal.Retangulo(20, 50)));
        if (denteD) t.Add(T(b, L, SecaoPoligonal.Retangulo(20, 25, 0, 25)));
        var p = new PecaLevantada { Id = id, Uid = "u" + id, Marca = marca, Tipo = TipoPeca.Viga, PreMoldada = pm, Comprimento = L, Trechos = t, TipoRevit = "20x50" };
        ConferenciaPecas.Avaliar(p);
        return p;
    }

    /// <summary>Perfil de um consolo: balanço 25, de 550 a 573,5 junto à face, com chanfro 2 × 2 na ponta de baixo.</summary>
    private static SecaoPoligonal PerfilConsolo() => new SecaoPoligonal
    {
        Contorno = { (0, 550), (23, 550), (25, 552), (25, 573.5), (0, 573.5) },
    };

    private static PecaLevantada Pilar(string id, string marca, double b = 40, params int[] faces)
    {
        var p = new PecaLevantada
        {
            Id = id, Uid = "u" + id, Marca = marca, Tipo = TipoPeca.Pilar, PreMoldada = true, Comprimento = 600, Bx = b, By = b,
            CotaBase = -0.5, Embutimento = 60, Niveis = new List<(string, double)> { ("Nível 2", 3) },
            Trechos = { T(0, 600, SecaoPoligonal.Retangulo(b, b)) },
        };
        foreach (int f in faces) p.Consolos.Add(new ConsoloLevantado { Face = f, S0 = b / 2 - 10, S1 = b / 2 + 10, Perfil = PerfilConsolo() });
        ConferenciaPecas.Avaliar(p);
        return p;
    }

    [Fact]
    public void MarcaDoModeloValeEASemMarcaRecebeNumeroLivre()
    {
        var notas = new List<string>();
        var m = MarcasPecas.Definir(new[] { new[] { "V2", "V2" }, new[] { "", "" }, new[] { "V1" }, new string[0] }, "V", notas);
        Assert.Equal(new[] { "V2", "V3", "V1", "V4" }, m);   // V1 e V2 são do modelo: as geradas pulam para V3 e V4
        Assert.Empty(notas);
    }

    [Fact]
    public void MarcasDiferentesNoGrupoEMarcaRepetidaAvisam()
    {
        var notas = new List<string>();
        var m = MarcasPecas.Definir(new[] { new[] { "V212", "V112" }, new[] { "V112" } }, "V", notas);
        Assert.Equal(new[] { "V112", "V112 (2)" }, m);
        Assert.Contains(notas, n => n.Contains("V112, V212"));
        Assert.Contains(notas, n => n.Contains("DIFERENTES"));
    }

    [Fact]
    public void VigaComDentesEInvertidaNoMesmoGrupo()
    {
        var arq = ConversorLevantamento.Converter(new[]
        {
            Viga("10", "V7", true, false), Viga("11", "V7", false, true),   // invertida
            Viga("12", "", true, true),
        }, "m.rvt", "2026-11-09T00:00:00");
        Assert.Equal(2, arq.Vigas.Count);
        var v7 = arq.Vigas.Single(v => v.Marca == "V7");
        Assert.Equal(2, v7.Quantidade);
        Assert.Equal((20.0, 50.0, 546.0), (v7.B, v7.H, v7.L));
        Assert.Equal((25.0, 25.0), (v7.DenteE.Altura, v7.DenteE.Comprimento));
        Assert.Null(v7.DenteD);
        Assert.Equal(new[] { 0, 180 }, v7.Elementos.Select(e => e.Giro));
        Assert.Equal("u10", v7.Elementos[0].Uid);
        var v1 = arq.Vigas.Single(v => v.Marca == "V1");   // sem marca no modelo: gerada
        Assert.NotNull(v1.DenteE);
        Assert.NotNull(v1.DenteD);
    }

    [Fact]
    public void FuroDePinoNoDenteNaoImpedeAExportacao()
    {
        var p = Viga("10", "V1", true, false);
        // o corte no furo pega só um lado da seção (7 de largura): trecho curto dentro da caixa
        p.Trechos = new List<TrechoPeca>
        {
            T(0, 10, SecaoPoligonal.Retangulo(20, 25, 0, 25)), T(10, 13, SecaoPoligonal.Retangulo(7, 25, 0, 25)),
            T(13, 25, SecaoPoligonal.Retangulo(20, 25, 0, 25)), T(25, 546, SecaoPoligonal.Retangulo(20, 50)),
        };
        var arq = ConversorLevantamento.Converter(new[] { p }, "m.rvt", "");
        var v = Assert.Single(arq.Vigas);
        Assert.Equal((25.0, 25.0), (v.DenteE.Altura, v.DenteE.Comprimento));
        Assert.Contains(arq.Notas, n => n.Contains("furo") && n.Contains("10–13"));
    }

    [Fact]
    public void VigaComAbaOuRecorteEmCimaFicaDeFora()
    {
        var aba = Viga("10", "V1", false, false);
        aba.Trechos.Insert(0, T(0, 100, new SecaoPoligonal { Contorno = { (0, 0), (40, 0), (40, 15), (20, 15), (20, 50), (0, 50) } }));
        aba.Trechos[1].X0 = 100;
        var emCima = Viga("11", "V2", false, false);
        emCima.Trechos.Insert(0, T(0, 25, SecaoPoligonal.Retangulo(20, 25)));   // dente com o recorte EM CIMA
        emCima.Trechos[1].X0 = 25;
        var arq = ConversorLevantamento.Converter(new[] { aba, emCima }, "m.rvt", "");
        Assert.Empty(arq.Vigas);
        Assert.Equal(2, arq.Notas.Count(n => n.Contains("não exportada")));
    }

    [Fact]
    public void SemMaterialPreMoldadoTodasEntramComNota()
    {
        var arq = ConversorLevantamento.Converter(new[] { Viga("10", "", false, false, pm: false) }, "m.rvt", "");
        Assert.Single(arq.Vigas);
        Assert.Contains(arq.Notas, n => n.Contains("Nenhum(a) viga"));
        // havendo pré-moldadas, as outras ficam de fora
        arq = ConversorLevantamento.Converter(new[] { Viga("10", "", false, false, pm: false), Viga("11", "", false, false, 600) }, "m.rvt", "");
        Assert.Equal(600, Assert.Single(arq.Vigas).L);
        Assert.Contains(arq.Notas, n => n.Contains("ficaram de fora"));
    }

    [Fact]
    public void PilarComConsolosGiradosNoMesmoGrupo()
    {
        var arq = ConversorLevantamento.Converter(new[] { Pilar("1", "P5", 40, 1, 2), Pilar("2", "P5", 40, 2, 3), Pilar("3", "", 50) }, "m.rvt", "");
        Assert.Equal(2, arq.Pilares.Count);
        var p5 = arq.Pilares.Single(p => p.Marca == "P5");
        Assert.Equal(2, p5.Quantidade);
        Assert.NotEqual(p5.Elementos[0].Giro, p5.Elementos[1].Giro);
        Assert.Equal((-0.5, 60.0), (p5.CotaBase, p5.Embutimento.Value));
        Assert.Equal("Nível 2", Assert.Single(p5.Niveis).Nome);
        var c = p5.Consolos.First();
        // o chanfro na ponta não faz o consolo trapezoidal
        Assert.Equal((550.0, 573.5, 25.0, 23.5), (c.Base, c.Topo, c.Balanco, c.AlturaPonta));
        Assert.Equal((10.0, 30.0), (c.Inicio, c.Fim));
        Assert.Equal("P1", arq.Pilares.Single(p => p.B == 50).Marca);
    }

    [Fact]
    public void PilarNaoRetangularFicaDeForaEChanfradoPassa()
    {
        var chanfrado = Pilar("1", "P1");
        chanfrado.Trechos[0].Secao = new SecaoPoligonal { Contorno = { (2, 0), (38, 0), (40, 2), (40, 38), (38, 40), (2, 40), (0, 38), (0, 2) } };
        var emL = Pilar("2", "P2");
        emL.Trechos[0].Secao = new SecaoPoligonal { Contorno = { (0, 0), (40, 0), (40, 20), (20, 20), (20, 40), (0, 40) } };
        var arq = ConversorLevantamento.Converter(new[] { chanfrado, emL }, "m.rvt", "");
        Assert.Equal("P1", Assert.Single(arq.Pilares).Marca);
        Assert.Contains(arq.Notas, n => n.Contains("P2") && n.Contains("não exportado"));
    }

    [Fact]
    public void UidVaiEVoltaNoJsonEArquivoAntigoSemUidAindaLe()
    {
        var arq = ConversorLevantamento.Converter(new[] { Viga("10", "V7", true, true), Pilar("1", "P5", 40, 1) }, "m.rvt", "");
        var lido = ArquivoPecas.DeJson(Json.Ler(Json.Gravar(arq.ParaJson())));
        Assert.Equal("u10", lido.Vigas[0].Elementos[0].Uid);
        Assert.Equal("u1", lido.Pilares[0].Elementos[0].Uid);
        Assert.Equal(ArquivoPecas.VersaoAtual, 1);   // campo opcional: o DEB.Cad já instalado continua lendo
        arq.Vigas[0].Elementos[0].Uid = "";
        Assert.DoesNotContain("uid", Json.Gravar(new ArquivoPecas { Vigas = arq.Vigas }.ParaJson()));
    }
}
