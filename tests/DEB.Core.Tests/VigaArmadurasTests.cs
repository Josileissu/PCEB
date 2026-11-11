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
        Assert.Equal(25 + 2.5 + 0.315 + 5 * 5, zonas[0].X1, 6);  // depois da suspensão do dente (5 c/5 a partir da face, a cobrimento)
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
        // + em cada dente: 3 tirantes, 4 grampos e 3 costuras (a perna de um lado) = 10
        Assert.Equal(2 * 2 + 8 + 2 * 10, r.Circulos.Count(k => k.Layer == "ARM-BARRA-SECAO"));
        Assert.Equal(3 + 2 + 2 * 4, r.Faixas.Count(f => f.Tipo == TipoFaixa.Indicacao));   // + dente: Gerber, tirante, grampo, costura na seção (na elevação, Gerber e suspensão em faixa com cota)
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

    [Fact]
    public void DenteGerberComAArmaduraDoCatalogo()
    {
        var (r, _) = Gerar(v =>
        {
            v["Nome"] = "V129"; v["B"] = "40"; v["H"] = "50"; v["L"] = "767"; v["ExtE"] = "DG 35x25"; v["ExtD"] = "Reta";
            v["Longitudinais"] = "Inferior|4|25|1|0|0||;Superior|2|16|1|0|0||";
        });
        var t = TemplateVigaPreMoldada.CatalogoDentes.First(x => x.Nome == "DG 35x25");
        // só a ponta esquerda tem dente
        foreach (var n in new[] { "gerber_E", "suspensao_E", "tirante_E", "grampo_E", "costura_E", "solda_tirante_E" })
            Assert.Contains(r.Barras, b => b.Nome == n);
        Assert.DoesNotContain(r.Barras, b => b.Nome.EndsWith("_D") && !b.Nome.StartsWith("solda_inf"));
        var tir = r.Barras.Single(b => b.Nome == "tirante_E");
        Assert.Equal(t.TiranteComp, tir.Vertices.Max(p => p.X) - tir.Vertices.Min(p => p.X), 6);
        Assert.Equal(t.TiranteQtd, tir.Quantidade);
        var gr = r.Barras.Single(b => b.Nome == "grampo_E");
        Assert.Equal(4, gr.Vertices.Count);                      // U: perna, base (largura − 2 cob), perna
        Assert.Equal(35, gr.Vertices.Max(p => p.Y) - gr.Vertices.Min(p => p.Y), 6);
        // estribo Gerber: altura do dente − cobrimentos; suspensão: altura toda
        var ge = r.Barras.Single(b => b.Nome == "gerber_E");
        Assert.Equal(t.EstGerberQtd, ge.Quantidade);
        Assert.Equal(35 - 5, ge.Vertices.Max(p => p.Y) - ge.Vertices.Min(p => p.Y), 0);
        Assert.Equal(t.SuspQtd, r.Barras.Single(b => b.Nome == "suspensao_E").Quantidade);
        // dente e suspensão em faixa com cota (padrão do escritório, 2026-11-11): o dente da ponta à face do recorte, a suspensão
        // logo depois, encadeada com a faixa dos estribos; a quantidade da faixa pela regra do plugin = a da barra, sem "c/"
        var cfg = new Configuracao.ConfiguracaoArmadura();
        var fg = r.Faixas.Single(f => f.Barra == "gerber_E" && f.Tipo == Modelo.TipoFaixa.Ortogonal);
        var fs = r.Faixas.Single(f => f.Barra == "suspensao_E" && f.Tipo == Modelo.TipoFaixa.Ortogonal);
        var fe = r.Faixas.Where(f => f.Barra == "estribo").OrderBy(f => f.P1.X).First();
        Assert.All(new[] { fg, fs }, f => { Assert.Equal(Modelo.TipoFaixa.Ortogonal, f.Tipo); Assert.False(f.MostrarEspacamento); });
        Assert.Equal((0.0, 25.0), (fg.P1.X, fg.P2.X));
        Assert.Equal(25.0, fs.P1.X);
        Assert.Equal(fe.P1.X, fs.P2.X, 6);
        Assert.Equal(fe.P1.Y, fg.P1.Y, 6);
        int Qtd(FaixaTemplate f) => Modelo.CalculoFaixa.Quantidade(f.P2.X - f.P1.X, f.Espacamento, cfg.EspacamentoInicialFaixa, cfg.ModoQuantidadeFaixa);
        Assert.Equal(t.EstGerberQtd, Qtd(fg));
        Assert.Equal(t.SuspQtd, Qtd(fs));
        foreach (var modo in new[] { Modelo.ModoQuantidadeFaixa.Teto, Modelo.ModoQuantidadeFaixa.TetoMaisUm })
        {
            var c2 = new Configuracao.ConfiguracaoArmadura { ModoQuantidadeFaixa = modo, EspacamentoInicialFaixa = 0.5 };
            var f2 = TemplateVigaPreMoldada.FaixaContada("x", 0, 25, 0, 4, 0, 50, c2);
            Assert.Equal(4, Modelo.CalculoFaixa.Quantidade(25, f2.Espacamento, 0.5, modo));
        }
        // inferior com barra soldada nas duas pontas, "VER DET. DE SOLDA"
        Assert.Contains(r.Barras, b => b.Nome == "solda_inf1_E");
        Assert.Contains(r.Barras, b => b.Nome == "solda_inf1_D");
        Assert.Contains(r.Textos, x => x.Texto == "VER DET. DE SOLDA");
        Assert.Contains(r.Textos, x => x.Texto.StartsWith("TIRANTE"));
        Assert.Contains(r.Textos, x => x.Texto.StartsWith("GRAMPO"));
        Assert.Contains(r.Textos, x => x.Texto.StartsWith("COSTURA"));
    }

    [Fact]
    public void AlcaNoFundoNaRelacaoEDetalhesDaBiblioteca()
    {
        var (r, v) = Gerar(v => { v["H"] = "75"; v["CobFundo"] = "2.5"; v["AlcaEmb"] = "0"; v["AlcaPernas"] = "2"; });
        Assert.Equal(72.5, TemplateVigaPreMoldada.EmbutimentoAlcaViga(v), 6);   // até o fundo: h − cobrimento do fundo
        var alca = r.Barras.Single(b => b.Nome == "alca");
        Assert.Equal("CP-190", alca.Aco);
        Assert.Equal(2, alca.Quantidade);
        Assert.Equal(2, alca.Repeticoes);                       // duas alças
        Assert.Contains(r.Textos, t => t.Texto == "ALÇA DE IÇAMENTO");
        // detalhes típicos pedidos à biblioteca (nunca desenhados por código), na coluna à direita da relação do aço
        Assert.Contains(BibliotecaDetalhes.RaioEmendasComprimento, r.DetalhesBiblioteca);
        Assert.Contains(BibliotecaDetalhes.SoldaTirante, r.DetalhesBiblioteca);
        Assert.True(r.ColunaDetalhes.Value.X > r.PosicaoRA.Value.X);
        Assert.DoesNotContain(r.Textos, t => t.Texto.StartsWith("RAIO (r) DE CURVATURA"));
    }

    [Fact]
    public void EstabilidadeLateralPelaNbr9062()
    {
        // 6.1.6: L/b ≤ 50 e L·h/b² ≤ 500, com L entre as alças
        var (ok, _) = TemplateVigaPreMoldada.EstabilidadeLateral(800, 20, 75);
        Assert.Equal(40, ok, 6);
        Assert.Equal(150, TemplateVigaPreMoldada.EstabilidadeLateral(800, 20, 75).Indice, 6);   // 800·75 / 20²
        var (r, _) = Gerar(v => { v["B"] = "15"; v["H"] = "90"; v["L"] = "1500"; v["ExtE"] = "Reta"; v["ExtD"] = "Reta"; });
        Assert.Contains(r.Avisos, a => a.StartsWith("Estabilidade lateral"));
        var (r2, _) = Gerar(v => { v["B"] = "40"; v["H"] = "50"; v["L"] = "767"; });
        Assert.DoesNotContain(r2.Avisos, a => a.StartsWith("Estabilidade lateral"));
    }
}
