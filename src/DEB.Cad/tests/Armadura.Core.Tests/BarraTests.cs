using System.Collections.Generic;
using System.Linq;
using Armadura.Core.Calculo;
using Armadura.Core.Configuracao;
using Armadura.Core.Descricao;
using Armadura.Core.Modelo;
using Armadura.Core.Numeracao;
using Armadura.Core.Persistencia;
using Armadura.Core.RelacaoAco;
using Xunit;

namespace Armadura.Core.Tests;

public class BarraTests
{
    private static ConfiguracaoArmadura Cfg() => new() { SimboloDiametro = "ø" };

    private static List<GeoSegmento> L(params (double comp, double defl)[] s) =>
        s.Select(x => new GeoSegmento(TipoSegmento.Reta, x.comp, x.defl)).ToList();

    private static DadosArmadura Barra(int nSeg, double bitola = 10) =>
        new() { Bitola = bitola, Segmentos = Enumerable.Range(0, nSeg).Select(_ => new Segmento()).ToList() };

    [Theory]
    [InlineData(199.5, 0, ModoArredondamento.Proximo, 200)]
    [InlineData(200.2, 0, ModoArredondamento.ParaCima, 201)]
    [InlineData(200.0000001, 0, ModoArredondamento.ParaCima, 200)]
    [InlineData(12.34, 1, ModoArredondamento.Proximo, 12.3)]
    [InlineData(132.49999999, 0, ModoArredondamento.Proximo, 133)] // erro de ponto flutuante do CAD em 132,5
    [InlineData(132.4, 0, ModoArredondamento.Proximo, 132)]
    public void Arredondamento(double v, int casas, ModoArredondamento modo, double esperado) =>
        Assert.Equal(esperado, CalculoComprimento.Arredondar(v, casas, modo), 6);

    [Fact]
    public void ComprimentoBarraEmL()
    {
        var geo = L((300, 0), (50, 90));
        Assert.Equal(350, CalculoComprimento.ComprimentoUnitario(geo, Cfg()));
    }

    [Fact]
    public void DescricaoNosTresPadroes()
    {
        var d = new DadosArmadura { Numero = 3, Quantidade = 4, Bitola = 8, Espacamento = 10 };
        var cfg = Cfg();
        Assert.Equal("4 N3 ø8.0 c/10 C=200", FormatadorDescricao.Formatar(d, 200, cfg));
        cfg.PadraoDescricao = PadraoDescricao.NumeroPrimeiro;
        Assert.Equal("N3 – 4ø8.0 c/10 C=200", FormatadorDescricao.Formatar(d, 200, cfg));
        cfg.PadraoDescricao = PadraoDescricao.NumeroNoFim;
        Assert.Equal("4 ø8.0c/10 C=200 (N3)", FormatadorDescricao.Formatar(d, 200, cfg));
    }

    [Fact]
    public void DescricaoVarRepeticoesESemEspacamento()
    {
        var d = new DadosArmadura { Numero = 0, Quantidade = 2, Bitola = 12.5, Repeticoes = 3 };
        d.Segmentos.Add(new Segmento { Var = true });
        Assert.Equal("3x2 N0 ø12.5 C=VAR", FormatadorDescricao.Formatar(d, 150, Cfg()));
    }

    [Fact]
    public void BarraInvertidaEEspelhadaSaoIguais()
    {
        var cfg = Cfg();
        var u = Barra(3);
        // U: 50 → vira 90° à esquerda → 300 → vira 90° à esquerda → 50 (gancho só numa ponta)
        u.Segmentos[0].Formato = FormatoSegmento.Gancho90;
        var a = Numerador.Assinatura(u, L((50, 0), (300, 90), (60, 90)), cfg);

        var invertida = Barra(3);
        invertida.Segmentos[2].Formato = FormatoSegmento.Gancho90;
        // desenhada ao contrário: 60 → 300 → 50, virando à direita
        var b = Numerador.Assinatura(invertida, L((60, 0), (300, -90), (50, -90)), cfg);
        Assert.Equal(a, b);

        var espelhada = Barra(3);
        espelhada.Segmentos[0].Formato = FormatoSegmento.Gancho90;
        var c = Numerador.Assinatura(espelhada, L((50, 0), (300, -90), (60, -90)), cfg);
        Assert.Equal(a, c);

        cfg.EspelhadasIguais = false;
        Assert.NotEqual(Numerador.Assinatura(u, L((50, 0), (300, 90), (60, 90)), cfg),
                        Numerador.Assinatura(espelhada, L((50, 0), (300, -90), (60, -90)), cfg));
    }

    [Fact]
    public void UeZSaoDiferentes()
    {
        var cfg = Cfg();
        var u = Numerador.Assinatura(Barra(3), L((50, 0), (300, 90), (50, 90)), cfg);
        var z = Numerador.Assinatura(Barra(3), L((50, 0), (300, 90), (50, -90)), cfg);
        Assert.NotEqual(u, z);
    }

    [Fact]
    public void NumeracaoGlobalPorBitolaEOrdem()
    {
        var itens = new[]
        {
            new ItemNumeracao { Chave = "a", Aco = "CA-50", Bitola = 12.5, Assinatura = "X", Ordem = 1 },
            new ItemNumeracao { Chave = "b", Aco = "CA-50", Bitola = 8.0, Assinatura = "Y", Ordem = 2 },
            new ItemNumeracao { Chave = "c", Aco = "CA-50", Bitola = 12.5, Assinatura = "X", Ordem = 3 }, // cópia de "a"
            new ItemNumeracao { Chave = "d", Aco = "CA-50", Bitola = 8.0, Assinatura = "Z", Ordem = 4 },
        };
        var n = Numerador.NumerarGlobal(itens, 1);
        Assert.Equal(1, n["b"]);
        Assert.Equal(2, n["d"]);
        Assert.Equal(3, n["a"]);
        Assert.Equal(3, n["c"]);
    }

    [Fact]
    public void NumeracaoPelaBitolaAntesDoAco()
    {
        var n = Numerador.NumerarGlobal(new[]
        {
            new ItemNumeracao { Chave = "ca50", Aco = "CA-50", Bitola = 8.0, Assinatura = "X", Ordem = 1 },
            new ItemNumeracao { Chave = "ca60", Aco = "CA-60", Bitola = 5.0, Assinatura = "Y", Ordem = 2 },
        }, 1);
        Assert.Equal(1, n["ca60"]);
        Assert.Equal(2, n["ca50"]);
    }

    [Fact]
    public void RelacaoEResumo()
    {
        var cfg = Cfg();
        cfg.AcrescimoPesoPercentual = 10;
        var r = RelacaoDoAco.Calcular(new[]
        {
            new ItemRA { Numero = 1, Aco = "CA-50", Bitola = 10, Quantidade = 4, ComprimentoUnitario = 350 },
            new ItemRA { Numero = 1, Aco = "CA-50", Bitola = 10, Quantidade = 2, ComprimentoUnitario = 350 },
            new ItemRA { Numero = 2, Aco = "CA-60", Bitola = 5, Quantidade = 20, ComprimentoUnitario = 150 },
        }, cfg);

        Assert.Equal(2, r.Linhas.Count);
        var n1 = r.Linhas.Single(l => l.Numero == 1);
        Assert.Equal(6, n1.Quantidade);
        Assert.Equal(2100, n1.ComprimentoTotal);

        var r10 = r.Resumo.Single(l => l.Bitola == 10);
        Assert.Equal(21.0, r10.ComprimentoTotalM, 2);
        Assert.Equal(14.3, r10.Peso, 1);   // 21 m × 0.617 × 1.10 = 14.25 → 14.3
        Assert.Equal(2, r10.Barras);       // 2100 / 1200 → 2
        Assert.Null(r.Resumo.Single(l => l.Bitola == 5).Barras); // rolo
    }

    [Fact]
    public void CodecIdaEVolta()
    {
        var d = new DadosArmadura
        {
            Numero = 7, Bitola = 16, Aco = "CA-50", Quantidade = 3, Repeticoes = 2, Espacamento = 12.5,
            Estribo = true, Corrido = true, Visivel = false, Campos = CamposDescricao.Numero | CamposDescricao.Bitola,
            HandleDescricao = "2AF", TemPosicaoDescricao = true, DescricaoDx = 10.5, DescricaoDy = -3,
        };
        d.Segmentos.Add(new Segmento { Formato = FormatoSegmento.Gancho90, Cotar = false, ComprimentoRegistrado = 20, HandleCota = "2B0" });
        d.Segmentos.Add(new Segmento { Var = true, ComprimentoRegistrado = 300, Sobreposto = true });

        var x = CodecDados.DecodificarBarra(CodecDados.Codificar(d));
        Assert.False(x.Segmentos[0].Sobreposto);
        Assert.True(x.Segmentos[1].Sobreposto);
        Assert.Equal(d.Id, x.Id);
        Assert.Equal(7, x.Numero);
        Assert.Equal(16, x.Bitola);
        Assert.Equal(12.5, x.Espacamento);
        Assert.True(x.Estribo && x.Corrido && !x.Visivel);
        Assert.Equal(d.Campos, x.Campos);
        Assert.Equal("2AF", x.HandleDescricao);
        Assert.Equal(-3, x.DescricaoDy);
        Assert.Equal(2, x.Segmentos.Count);
        Assert.Equal(FormatoSegmento.Gancho90, x.Segmentos[0].Formato);
        Assert.False(x.Segmentos[0].Cotar);
        Assert.Equal("2B0", x.Segmentos[0].HandleCota);
        Assert.True(x.Segmentos[1].Var);
        Assert.Equal(300, x.Segmentos[1].ComprimentoRegistrado);

        var v = CodecDados.DecodificarVinculo(CodecDados.Codificar(new Vinculo { Tipo = TipoParte.Cota, HandleBarra = "1A", Indice = 2 }));
        Assert.Equal(TipoParte.Cota, v.Tipo);
        Assert.Equal("1A", v.HandleBarra);
        Assert.Equal(2, v.Indice);
        Assert.Null(CodecDados.DecodificarBarra(CodecDados.Codificar(new Vinculo { Tipo = TipoParte.Descricao })));
    }

    [Theory]
    [InlineData(10, 2.0)]
    [InlineData(25, 5.0)]
    [InlineData(50, 10.0)]
    [InlineData(0, 2.0)] // 0 = escala base (1:10)
    public void AlturaDoTextoPelaEscala(double escala, double esperado) =>
        Assert.Equal(esperado, new ConfiguracaoArmadura().AlturaTexto(1.0, escala), 9);

    [Fact]
    public void ConfiguracaoAntigaMigraParaCmEEscala10()
    {
        var cfg = ConfiguracaoArmadura.DeJson("{\"Escala\": 50, \"AlturaTextoMm\": 2.5}");
        Assert.Equal(10, cfg.Escala);
        Assert.Equal(2.0, cfg.AlturaTextoMm);
        Assert.Equal(UnidadeDesenho.Centimetro, cfg.UnidadeDesenho);
        Assert.Equal(ConfiguracaoArmadura.VersaoAtual, cfg.VersaoConfig);

        // arquivo já na versão nova: a escala escolhida é respeitada
        var nova = ConfiguracaoArmadura.DeJson(new ConfiguracaoArmadura { Escala = 25 }.ParaJson());
        Assert.Equal(25, nova.Escala);
    }

    [Fact]
    public void EscalaDaBarraNoXData()
    {
        var d = new DadosArmadura { Escala = 25 };
        Assert.Equal(25, CodecDados.DecodificarBarra(CodecDados.Codificar(d)).Escala);
    }

    [Fact]
    public void ConfiguracaoJsonMantemPadroesAusentes()
    {
        var cfg = ConfiguracaoArmadura.DeJson("{\"Escala\": 25, \"VersaoConfig\": 2}");
        Assert.Equal(25, cfg.Escala);
        Assert.Equal("N", cfg.PrefixoPosicao);
        Assert.Equal(1, cfg.NumeroInicial);
        Assert.Equal(8, cfg.Dobramento.PontaGancho90);
        Assert.NotEmpty(cfg.Barras.Bitolas);

        var volta = ConfiguracaoArmadura.DeJson(new ConfiguracaoArmadura { AcrescimoPesoPercentual = 5 }.ParaJson());
        Assert.Equal(5, volta.AcrescimoPesoPercentual);
        Assert.Equal(16, volta.Barras.Bitolas.Count); // 13 bitolas CA + 3 cordoalhas CP-190
        Assert.True(volta.Barras.Bitolas.Single(b => b.Diametro == 10).Ativa);
    }
}
