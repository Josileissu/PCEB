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
    public void VigaComMisulaOuRecorteEmCimaFicaDeFora()
    {
        var misula = Viga("10", "V1", false, false);
        misula.Trechos.Insert(0, T(0, 100, new SecaoPoligonal { Contorno = { (0, 0), (20, 0), (40, 30), (40, 50), (0, 50) } }));   // lado inclinado
        misula.Trechos[1].X0 = 100;
        var emCima = Viga("11", "V2", false, false);
        emCima.Trechos.Insert(0, T(0, 25, SecaoPoligonal.Retangulo(20, 25)));   // dente com o recorte EM CIMA
        emCima.Trechos[1].X0 = 25;
        var mesa = Viga("12", "V3", false, false);                               // I: mesas em cima e embaixo
        mesa.Trechos[0].Secao = new SecaoPoligonal { Contorno = { (0, 0), (40, 0), (40, 10), (30, 10), (30, 40), (40, 40), (40, 50), (0, 50), (0, 40), (10, 40), (10, 10), (0, 10) } };
        var arq = ConversorLevantamento.Converter(new[] { misula, emCima, mesa }, "m.rvt", "");
        Assert.Empty(arq.Vigas);
        Assert.Equal(3, arq.Notas.Count(n => n.Contains("não exportada")));
    }

    /// <summary>L: alma 20 × 50 (y de 0 a 20) com aba 15 × 20 para o y maior (esquerda), no fundo.</summary>
    private static SecaoPoligonal SecaoL() => new SecaoPoligonal { Contorno = { (0, 0), (35, 0), (35, 20), (20, 20), (20, 50), (0, 50) } };

    [Fact]
    public void VigaLComAbaEntreOsDentesVaiComAAba()
    {
        var p = Viga("10", "V129", true, true);
        p.Trechos[1].Secao = SecaoL();
        ConferenciaPecas.Avaliar(p);
        Assert.NotEqual(SituacaoPeca.NaoSuportada, p.Situacao);
        var arq = ConversorLevantamento.Converter(new[] { p }, "m.rvt", "");
        var v = Assert.Single(arq.Vigas);
        Assert.Equal((20.0, 50.0), (v.B, v.H));   // b = a alma
        Assert.NotNull(v.DenteE);
        Assert.Equal((AbaPeca.Esquerda, 15.0, 20.0, 0.0, 25.0, 521.0), (v.Aba.Lado, v.Aba.Largura, v.Aba.Altura, v.Aba.Base, v.Aba.Inicio, v.Aba.Fim));
        // com aba o arquivo sai na versão 2 (um CAD antigo recusa em vez de desenhar sem a aba) e a aba volta na leitura
        var json = arq.ParaJson();
        Assert.Equal(2, LeitorJson.Int(json, "versao", 0, "t"));
        var lido = ArquivoPecas.DeJson(Json.Ler(Json.Gravar(json)));
        Assert.Equal((AbaPeca.Esquerda, 15.0, 25.0, 521.0), (lido.Vigas[0].Aba.Lado, lido.Vigas[0].Aba.Largura, lido.Vigas[0].Aba.Inicio, lido.Vigas[0].Aba.Fim));
    }

    [Fact]
    public void TInvertidoComAbaSoNumTrechoEAbaElevada()
    {
        // 0–200 T invertido (alma 20 em y 15..35, abas de 15 dos dois lados, 20 de altura); 200–546 só a alma
        var p = Viga("10", "VT", false, false);
        var t = new SecaoPoligonal { Contorno = { (0, 0), (50, 0), (50, 20), (35, 20), (35, 50), (15, 50), (15, 20), (0, 20) } };
        p.Trechos = new List<TrechoPeca> { T(0, 200, t), T(200, 546, SecaoPoligonal.Retangulo(20, 50, 15, 0)) };
        var v = Assert.Single(ConversorLevantamento.Converter(new[] { p }, "m.rvt", "").Vigas);
        Assert.Equal((AbaPeca.DoisLados, 15.0, 0.0, 200.0), (v.Aba.Lado, v.Aba.Largura, v.Aba.Inicio, v.Aba.Fim));
        Assert.Equal(20, v.B);
        // aba elevada (consolo corrido a 60 do fundo), para o y menor = direita
        var e = SecaoComAba.Ler(new SecaoPoligonal { Contorno = { (25, 0), (51, 0), (51, 170), (25, 170), (25, 80), (0, 80), (0, 60), (25, 60) } });
        Assert.Equal((26.0, 25.0, 0.0, 20.0, 60.0), (e.B, e.AbaDireita, e.AbaEsquerda, e.AbaAltura, e.AbaBase));
        // dois trechos de aba separados: o CAD desenha um só
        p.Trechos = new List<TrechoPeca> { T(0, 100, t), T(100, 400, SecaoPoligonal.Retangulo(20, 50, 15, 0)), T(400, 546, t) };
        var arq = ConversorLevantamento.Converter(new[] { p }, "m.rvt", "");
        Assert.Empty(arq.Vigas);
        Assert.Contains(arq.Notas, n => n.Contains("mais de um trecho"));
    }

    /// <summary>
    /// Vigas de um modelo real ("19x65 com aba"): aba elevada com o fundo inclinado (22 junto da alma, 15 na ponta), à esquerda,
    /// que passa pelos dentes — a seção do dente é a parte de cima (40) com a mesma aba.
    /// </summary>
    [Fact]
    public void AbaInclinadaElevadaQuePassaPelosDentes()
    {
        var vao = new SecaoPoligonal { Contorno = { (0, 0), (19, 0), (19, 25), (39, 32), (39, 47), (19, 47), (19, 65), (0, 65) } };
        var dente = new SecaoPoligonal { Contorno = { (0, 25), (19, 25), (39, 32), (39, 47), (19, 47), (19, 65), (0, 65) } };
        var i = SecaoComAba.Ler(vao);
        Assert.Equal((19.0, 20.0, 0.0, 22.0, 15.0, 25.0), (i.B, i.AbaEsquerda, i.AbaDireita, i.AbaAltura, i.AbaAlturaPonta, i.AbaBase));
        Assert.Equal(i.Chave, SecaoComAba.Ler(dente).Chave);   // a mesma aba, medida a partir do topo
        var p = new PecaLevantada
        {
            Id = "1", Marca = "V119", Tipo = TipoPeca.Viga, PreMoldada = true, Comprimento = 295.5,
            Trechos = { T(0, 25, dente), T(25, 270.5, vao), T(270.5, 295.5, dente) },
        };
        ConferenciaPecas.Avaliar(p);
        Assert.NotEqual(SituacaoPeca.NaoSuportada, p.Situacao);
        var v = Assert.Single(ConversorLevantamento.Converter(new[] { p }, "m.rvt", "").Vigas);
        Assert.Equal((19.0, 65.0, 40.0, 40.0), (v.B, v.H, v.DenteE.Altura, v.DenteD.Altura));
        Assert.Equal((AbaPeca.Esquerda, 20.0, 22.0, 15.0, 25.0, 0.0, 295.5),
            (v.Aba.Lado, v.Aba.Largura, v.Aba.Altura, v.Aba.AlturaPonta, v.Aba.Base, v.Aba.Inicio, v.Aba.Fim));
        // duas abas no mesmo lado (aba de laje + aba superior): ainda não
        Assert.Null(SecaoComAba.Ler(new SecaoPoligonal { Contorno = { (0, 0), (19, 0), (19, 20), (39, 20), (39, 30), (19, 30), (19, 50), (39, 50), (39, 60), (19, 60), (19, 65), (0, 65) } }));
    }

    /// <summary>Vigas "30x50 com aba esq e dir laje" do modelo real: abas dos dois lados rentes ao TOPO, com o fundo inclinado, inclusive nos dentes.</summary>
    [Fact]
    public void AbasDosDoisLadosRentesAoTopo()
    {
        var vao = new SecaoPoligonal { Contorno = { (15, 0), (45, 0), (45, 35), (60, 38), (60, 50), (0, 50), (0, 38), (15, 35) } };
        var dente = new SecaoPoligonal { Contorno = { (15, 25), (45, 25), (45, 35), (60, 38), (60, 50), (0, 50), (0, 38), (15, 35) } };
        var p = new PecaLevantada
        {
            Id = "1", Marca = "V121", Tipo = TipoPeca.Viga, PreMoldada = true, Comprimento = 597,
            Trechos = { T(0, 25, dente), T(25, 572, vao), T(572, 597, dente) },
        };
        ConferenciaPecas.Avaliar(p);
        Assert.NotEqual(SituacaoPeca.NaoSuportada, p.Situacao);
        var v = Assert.Single(ConversorLevantamento.Converter(new[] { p }, "m.rvt", "").Vigas);
        Assert.Equal((30.0, 50.0, 25.0), (v.B, v.H, v.DenteE.Altura));
        Assert.Equal((AbaPeca.DoisLados, 15.0, 15.0, 12.0, 35.0, 0.0, 597.0),
            (v.Aba.Lado, v.Aba.Largura, v.Aba.Altura, v.Aba.AlturaPonta, v.Aba.Base, v.Aba.Inicio, v.Aba.Fim));
        // e o template desenha: a aba no topo passa pelos dentes
        var t = new DEB.Core.Templates.VigaPreMoldada.TemplateVigaPreMoldada();
        var cat = DEB.Core.Templates.VigaPreMoldada.TipoDente.Padrao();
        cat.AddRange(CatalogoAutomatico.Dentes(new[] { v }, cat));
        var avisos = new List<string>();
        var antes = DEB.Core.Templates.VigaPreMoldada.TemplateVigaPreMoldada.CatalogoDentes;
        try
        {
            DEB.Core.Templates.VigaPreMoldada.TemplateVigaPreMoldada.CatalogoDentes = cat;
            var val = ConversorVigaPeca.Valores(v, new DEB.Core.Templates.ValoresTemplate(t.Parametros), cat, avisos);
            Assert.Empty(avisos);
            var r = t.Gerar(val, new DEB.Core.Configuracao.ConfiguracaoArmadura());
            Assert.DoesNotContain(r.Avisos, a => a.Contains("entra no dente"));
            Assert.Equal(4, r.Barras.Single(b => b.Nome == "abalong").Quantidade);
        }
        finally { DEB.Core.Templates.VigaPreMoldada.TemplateVigaPreMoldada.CatalogoDentes = antes; }
    }

    /// <summary>
    /// Viga de escada do modelo real ("19x55"): a aba passa pelos dentes descendo ABAIXO do recorte — na seção do dente (a alma de 30 em
    /// cima) a aba fica pendurada 3 cm abaixo do fundo do dente.
    /// </summary>
    [Fact]
    public void AbaQuePassaPeloDenteAbaixoDoRecorte()
    {
        var vao = new SecaoPoligonal { Contorno = { (0, 0), (19, 0), (19, 22), (39, 28), (39, 40), (19, 40), (19, 55), (0, 55) } };
        var dente = new SecaoPoligonal { Contorno = { (0, 25), (19, 25), (19, 22), (39, 28), (39, 40), (19, 40), (19, 55), (0, 55) } };
        var id = SecaoComAba.Ler(dente);
        Assert.NotNull(id);
        Assert.Equal((19.0, 30.0, 25.0, -3.0, 18.0, 12.0), (id.B, id.H, id.Z0, id.AbaBase, id.AbaAltura, id.AbaAlturaPonta));
        Assert.Equal(SecaoComAba.Ler(vao).Chave, id.Chave);
        var p = new PecaLevantada
        {
            Id = "1", Marca = "VE1", Tipo = TipoPeca.Viga, PreMoldada = true, Comprimento = 494,
            Trechos = { T(0, 25, dente), T(25, 469, vao), T(469, 494, dente) },
        };
        ConferenciaPecas.Avaliar(p);
        Assert.NotEqual(SituacaoPeca.NaoSuportada, p.Situacao);
        var v = Assert.Single(ConversorLevantamento.Converter(new[] { p }, "m.rvt", "").Vigas);
        Assert.Equal((55.0, 30.0, 22.0, 18.0, 12.0, 0.0, 494.0), (v.H, v.DenteE.Altura, v.Aba.Base, v.Aba.Altura, v.Aba.AlturaPonta, v.Aba.Inicio, v.Aba.Fim));
        // o template desenha a seção do dente com a aba pendurada e avisa
        var t = new DEB.Core.Templates.VigaPreMoldada.TemplateVigaPreMoldada();
        var cat = DEB.Core.Templates.VigaPreMoldada.TipoDente.Padrao();
        cat.AddRange(CatalogoAutomatico.Dentes(new[] { v }, cat));
        var antes = DEB.Core.Templates.VigaPreMoldada.TemplateVigaPreMoldada.CatalogoDentes;
        try
        {
            DEB.Core.Templates.VigaPreMoldada.TemplateVigaPreMoldada.CatalogoDentes = cat;
            var r = t.Gerar(ConversorVigaPeca.Valores(v, new DEB.Core.Templates.ValoresTemplate(t.Parametros), cat, new List<string>()), new DEB.Core.Configuracao.ConfiguracaoArmadura());
            Assert.Contains(r.Avisos, a => a.Contains("pendurada abaixo do dente"));
            // seções de forma: o vão (8 vértices) e os dois dentes com a aba (8 cada: o canto de baixo da alma e a aba descendo dele)
            Assert.Equal(3, r.Contornos.Count(k => k.Layer == "ARM-PILAR-SECAO" && k.Fechado && k.Vertices.Count == 8));
        }
        finally { DEB.Core.Templates.VigaPreMoldada.TemplateVigaPreMoldada.CatalogoDentes = antes; }
    }

    [Fact]
    public void AbaDoModeloViraParametrosDoTemplate()
    {
        var t = new DEB.Core.Templates.VigaPreMoldada.TemplateVigaPreMoldada();
        var p = new VigaPeca { Marca = "V129", B = 40, H = 50, L = 600, Aba = new AbaPeca { Lado = AbaPeca.DoisLados, Largura = 15, Altura = 20, Inicio = 0, Fim = 600 } };
        var avisos = new List<string>();
        var v = ConversorVigaPeca.Valores(p, new DEB.Core.Templates.ValoresTemplate(t.Parametros), DEB.Core.Templates.VigaPreMoldada.TipoDente.Padrao(), avisos);
        Assert.Equal((DEB.Core.Templates.VigaPreMoldada.TemplateVigaPreMoldada.AbaDois, "15", "20", "600"), (v["Aba"], v["AbaLargura"], v["AbaAltura"], v["AbaFim"]));
        Assert.Empty(DEB.Core.Templates.CatalogoTemplates.Validar(t, v));
        var r = t.Gerar(v, new DEB.Core.Configuracao.ConfiguracaoArmadura());
        Assert.Contains(r.Barras, b => b.Nome == "abaestribo");
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
        Assert.Equal(1, LeitorJson.Int(arq.ParaJson(), "versao", 0, "t"));   // sem aba: versão 1, o DEB.Cad já instalado continua lendo
        arq.Vigas[0].Elementos[0].Uid = "";
        Assert.DoesNotContain("uid", Json.Gravar(new ArquivoPecas { Vigas = arq.Vigas }.ParaJson()));
    }
}
