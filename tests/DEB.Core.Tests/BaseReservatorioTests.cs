using System;
using System.Linq;
using DEB.Core.Calculo;
using DEB.Core.Configuracao;
using DEB.Core.Modelo;
using DEB.Core.Persistencia;
using DEB.Core.RelacaoAco;
using DEB.Core.Templates;
using DEB.Core.Templates.BaseReservatorio;
using Xunit;

namespace DEB.Core.Tests;

public class BaseReservatorioTests
{
    private static (ResultadoTemplate r, ValoresTemplate v) Gerar(Action<ValoresTemplate> ajustar = null)
    {
        var t = new TemplateBaseReservatorio();
        var v = new ValoresTemplate(t.Parametros);
        ajustar?.Invoke(v);
        Assert.Empty(CatalogoTemplates.Validar(t, v));
        return (t.Gerar(v, new ConfiguracaoArmadura()), v);
    }

    [Fact]
    public void CircularGeraGruposVarComAsCordas()
    {
        // Ø700, cobrimento 4, malha ø12.5 c/14 com dobras de 50 (inferior) e 30 (superior)
        var (r, _) = Gerar();
        var inf = r.Barras.First(b => b.Nome.Contains("Malha inferior") && b.Nome.Contains("(x)"));
        Assert.True(inf.ComprimentosVar.Count > 40);
        Assert.Equal(inf.Quantidade, inf.ComprimentosVar.Count);
        // a maior barra é a do centro: corda ≈ 2·(350 − 4) + 2 dobras de 50
        Assert.InRange(inf.ComprimentosVar.Max(), 692 + 100 - 15, 692 + 100);
        Assert.Contains(1, inf.SegmentosVar); // o trecho reto muda; as dobras não
        // simétricas: o 1º e o último têm o mesmo comprimento
        Assert.Equal(inf.ComprimentosVar.First(), inf.ComprimentosVar.Last(), 1);
        Assert.Contains(r.Faixas, f => f.Barra == inf.Nome && f.Tipo == TipoFaixa.Ortogonal);
        Assert.Contains(r.Tabelas, t => t.Titulo.Contains("MALHA INFERIOR"));
        Assert.Contains(r.Barras, b => b.Nome == "Anel de borda" && b.Corrido && b.Quantidade == 4);
        Assert.Equal(8 + 6, r.Circulos.Count(c => c.Layer == "ARM-ESTACA"));
        Assert.Equal("BASE RESERVATÓRIO", r.Detalhamento);
        Assert.NotNull(r.PosicaoRA);
    }

    [Fact]
    public void FaixaCentralSeparaAsBitolas()
    {
        var (r, _) = Gerar(v => { v["CentralLargura"] = "200"; v["CentralBitola"] = "16"; });
        Assert.Contains(r.Barras, b => b.Nome.Contains("central") && b.Bitola == 16);
        Assert.Contains(r.Barras, b => b.Nome.Contains("lateral") && b.Bitola == 12.5);
        // a lateral tem 2 trechos (dos dois lados da central): 2 faixas por direção e camada
        var lat = r.Barras.First(b => b.Nome.Contains("Malha inferior lateral (x)"));
        Assert.Equal(2, r.Faixas.Count(f => f.Barra == lat.Nome));
    }

    [Fact]
    public void UmaPosicaoPorBarraNaoUsaVar()
    {
        var (r, _) = Gerar(v => v["VarModo"] = TemplateBaseReservatorio.VarPorBarra);
        Assert.DoesNotContain(r.Barras, b => b.ComprimentosVar.Count > 0);
        Assert.True(r.Barras.Count(b => b.Nome.Contains("Malha inferior") && b.Nome.Contains("(x)")) > 10);
    }

    [Fact]
    public void RetangularComUNasFacesEBarrasDeBorda()
    {
        // como o SPA: 680 × 680, h = 85, grade 5 × 5, U ø10 c/12, 3 barras de borda por face
        var (r, _) = Gerar(v =>
        {
            v["Forma"] = TemplateBaseReservatorio.Retangular; v["Arranjo"] = "Grade"; v["UEsp"] = "12";
            v["InfDobra"] = TemplateBaseReservatorio.Fixa; v["InfDobraValor"] = "40"; v["InfEsp"] = "12"; v["SupEsp"] = "12";
        });
        Assert.DoesNotContain(r.Barras, b => b.ComprimentosVar.Count > 0);
        var inf = r.Barras.First(b => b.Nome.Contains("Malha inferior (x)"));
        Assert.Equal(672, inf.Vertices[2].X - inf.Vertices[1].X, 6);       // 680 − 2 × 4
        Assert.Equal(4, r.Faixas.Count(f => f.Barra == "U de borda" && f.Tipo == TipoFaixa.Ortogonal)); // uma por face
        Assert.Equal(2, r.Barras.Count(b => b.Nome.Contains("Borda (")));
        Assert.Equal(25, r.Circulos.Count(c => c.Layer == "ARM-ESTACA"));
    }

    [Fact]
    public void VarSomaOsComprimentosReaisNaRelacao()
    {
        var cfg = new ConfiguracaoArmadura { CasasDecimais = 0 };
        var d = new DadosArmadura { Bitola = 10, Aco = "CA-50", ComprimentosVar = { 300, 400, 500 } };
        var geo = new[] { new GeoSegmento(TipoSegmento.Reta, 50), new GeoSegmento(TipoSegmento.Reta, 400, 90), new GeoSegmento(TipoSegmento.Reta, 50, 90) }.ToList();
        d.Segmentos.AddRange(new[] { new Segmento(), new Segmento { Var = true }, new Segmento() });
        var reais = CalculoComprimento.ComprimentosReaisVar(geo, cfg, d);
        double desconto = CalculoComprimento.DescontoDobras(geo, d, cfg.Dobramento); // 2 dobras de 90°
        Assert.Equal(new[] { Math.Round(300 - desconto), Math.Round(400 - desconto), Math.Round(500 - desconto) }, reais);
        Assert.True(d.TemVar);

        var item = new ItemRA { Numero = 1, Aco = "CA-50", Bitola = 10, Quantidade = 6, ComprimentoUnitario = 999, ComprimentosVar = reais };
        // quantidade 6 = o grupo de 3 barras × 2 (repetições): total = 2 × soma
        Assert.Equal(2 * reais.Sum(), item.ComprimentoTotal, 6);
        var ra = RelacaoDoAco.Calcular(new[] { item }, cfg);
        Assert.Equal(2 * reais.Sum(), ra.Linhas[0].ComprimentoTotal, 6);

        // gravação e numeração: listas diferentes → assinaturas diferentes
        var x = CodecDados.DecodificarBarra(CodecDados.Codificar(d));
        Assert.Equal(new[] { 300.0, 400, 500 }, x.ComprimentosVar);
        var outra = d.Clonar();
        outra.ComprimentosVar = new System.Collections.Generic.List<double> { 300, 400, 510 };
        Assert.NotEqual(Numeracao.Numerador.Assinatura(d, geo, cfg), Numeracao.Numerador.Assinatura(outra, geo, cfg));
    }
}
