using DEB.Core.Modelo;
using DEB.Core.Persistencia;
using DEB.Core.Reconhecimento;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Pedido de 2026-10-29: reconhecer barras pelo texto próximo e mover as cotas.</summary>
public class ReconhecimentoTests
{
    [Theory]
    [InlineData("25 N0 ø16.0 c/10 C=73", 25, 1, 0, 16.0, 10.0, 73.0)]
    [InlineData("12 N3 %%c10 C=350", 12, 1, 3, 10.0, 0.0, 350.0)]
    [InlineData("N5 22%%c5.0 c/15 C=114", 22, 1, 5, 5.0, 15.0, 114.0)]
    [InlineData("N3 – 2x4ø8.0", 4, 2, 3, 8.0, 0.0, null)]
    [InlineData("2x11 N1 Ø10.0 c/15", 11, 2, 1, 10.0, 15.0, null)]
    [InlineData("2x(11 N1 ø10.0 c/15)", 11, 2, 1, 10.0, 15.0, null)]
    [InlineData("4ø10 C=320", 4, 1, 0, 10.0, 0.0, 320.0)]
    [InlineData("25N1ø12,5c/20C=300", 25, 1, 1, 12.5, 20.0, 300.0)]
    [InlineData("{\\fArial|b0;\\H2.5;6 N7 \\U+2205 8.0}", 6, 1, 7, 8.0, 0.0, null)]
    [InlineData("N2 ø6.3 C=VAR", 0, 1, 2, 6.3, 0.0, null)]
    [InlineData("N12 ø6.3 C=150", 0, 1, 12, 6.3, 0.0, 150.0)]
    [InlineData("N5-22 ø5.0", 22, 1, 5, 5.0, 0.0, null)]
    public void InterpretaFormatosComuns(string texto, int qtd, int rep, int n, double bitola, double esp, double? c)
    {
        var t = TextoArmadura.Interpretar(texto);
        Assert.NotNull(t);
        Assert.Equal(qtd, t.Quantidade);
        Assert.Equal(rep, t.Repeticoes);
        Assert.Equal(n, t.Numero);
        Assert.Equal(bitola, t.Bitola, 3);
        Assert.Equal(esp, t.Espacamento, 3);
        Assert.Equal(c, t.Comprimento);
    }

    [Fact]
    public void VarEMTextComQuebra()
    {
        var t = TextoArmadura.Interpretar("N2 ø6.3 C=VAR");
        Assert.True(t.Var);
        var m = TextoArmadura.Interpretar("10 N4 ø8.0 c/20\\PC=250");
        Assert.Equal(250, m.Comprimento);
        Assert.Equal(20, m.Espacamento);
    }

    [Theory]
    [InlineData("ø10")]          // só o diâmetro
    [InlineData("PILAR P1")]
    [InlineData("35")]
    [InlineData("ARM-BARRA")]
    [InlineData("ø100 C=10")]   // diâmetro fora da faixa de barras
    public void IgnoraTextosQueNaoSaoDeBarra(string texto) => Assert.Null(TextoArmadura.Interpretar(texto));

    [Fact]
    public void MedidasDosTrechos()
    {
        Assert.True(TextoArmadura.EhMedida("35", out var v) && v == 35);
        Assert.True(TextoArmadura.EhMedida(" 42,5 ", out v) && v == 42.5);
        Assert.True(TextoArmadura.EhMedida("120 cm", out v) && v == 120);
        Assert.False(TextoArmadura.EhMedida("N1", out _));
        Assert.True(TextoArmadura.MedidaConfere(35, 35.4));
        Assert.False(TextoArmadura.MedidaConfere(35, 42));
        Assert.True(TextoArmadura.ComprimentoConfere(300, 296));
        Assert.False(TextoArmadura.ComprimentoConfere(300, 200));
    }

    [Fact]
    public void AssociaPeloMenorCustoUmParaUm()
    {
        // texto 0 está perto das duas polilinhas; a 1 tem o texto 1 também perto
        var r = TextoArmadura.Associar(new[] { (0, 0, 1.0), (1, 0, 1.5), (1, 1, 2.0), (0, 1, 5.0) });
        Assert.Contains((0, 0), r);
        Assert.Contains((1, 1), r);
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void FaixaGuardaAncorasEDeslocaTudo()
    {
        var f = new DadosFaixa { Tipo = TipoFaixa.Indicacao, P1X = 1, P1Y = 2, Referencias = { (10, 20) }, TemAncora = true, AncoraX = 5, AncoraY = 6 };
        var x = CodecDados.DecodificarFaixa(CodecDados.Codificar(f));
        Assert.True(x.TemAncora);
        Assert.Equal(5, x.AncoraX);
        Assert.False(x.TemCirculo0);
        x.Deslocar(100, -50);
        Assert.Equal((110.0, -30.0), x.Referencias[0]);
        Assert.Equal(105, x.AncoraX);
        Assert.Equal(-48, x.P1Y);
        // faixa antiga (sem âncora) continua lendo
        Assert.False(CodecDados.DecodificarFaixa(CodecDados.Codificar(new DadosFaixa())).TemAncora);
    }

    [Fact]
    public void CotaMovidaVaiParaOXData()
    {
        var d = new DadosArmadura();
        d.Segmentos.Add(new Segmento());
        d.Segmentos.Add(new Segmento { CotaMovida = true, CotaDx = 3.5, CotaDy = -2 });
        var x = CodecDados.DecodificarBarra(CodecDados.Codificar(d));
        Assert.False(x.Segmentos[0].CotaMovida);
        Assert.True(x.Segmentos[1].CotaMovida);
        Assert.Equal(3.5, x.Segmentos[1].CotaDx);
        Assert.Equal(-2, x.Segmentos[1].CotaDy);
    }
}
