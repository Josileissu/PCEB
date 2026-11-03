using System.Linq;
using DEB.Core.Calculo;
using DEB.Core.Configuracao;
using DEB.Core.Estribos;
using DEB.Core.Modelo;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Barra descrita pelo eixo (Revit e outros modeladores 3D) dá o mesmo comprimento real do CAD.</summary>
public class ComprimentoPorEixoTests
{
    private static readonly ConfiguracaoArmadura Cfg = new ConfiguracaoArmadura { CasasDecimais = 2 };

    [Fact]
    public void BarraRetaTemOComprimentoDoEixo()
    {
        var eixo = new[] { (0.0, 0.0, 0.0), (0.0, 0.0, 565.0) };
        Assert.Equal(565, ComprimentoPorEixo.Calcular(eixo, false, 12.5, "CA-50", false, false, Cfg), 6);
    }

    [Fact]
    public void BarraEmUIgualAoExemploDaPlanilha()
    {
        // mesmo caso de ComprimentoRealTests: externas 100 + 54,4 + 100 (ø10) = eixo 99,5 + 53,4 + 99,5 → 249,82
        var eixo = new[] { (0.0, 99.5, 0.0), (0.0, 0.0, 0.0), (53.4, 0.0, 0.0), (53.4, 99.5, 0.0) };
        Assert.Equal(249.82, ComprimentoPorEixo.Calcular(eixo, false, 10, "CA-50", false, false, Cfg), 6);
    }

    [Fact]
    public void EstriboFechadoIgualAoGeradoNoCad()
    {
        // eixo 35 × 35 (ø5 CA-60) = externo 35,5 × 35,5 no gerador de estribos do CAD
        var regras = Cfg.Dobramento;
        double lg = regras.ComprimentoGancho(FormatoSegmento.Gancho45, 5, "CA-60", true);
        double passagem = regras.DiametroPino(5, "CA-60", true) + 2 * 0.5;
        var o = new OpcoesEstribo { Formato = FormatoEstribo.Simples, Gancho = FormatoSegmento.Gancho45, Bitola = 5, Aco = "CA-60" };
        var forma = GeradorEstribo.Gerar(o, 35.5, 35.5, 0, lg, passagem)[0];
        var geoCad = ComprimentoPorEixo.Segmentos(forma.Vertices, false);
        var dCad = new DadosArmadura { Bitola = 5, Aco = "CA-60", Estribo = true };
        for (int i = 0; i < geoCad.Count; i++) dCad.Segmentos.Add(new Segmento { Formato = forma.Formatos[i] });
        double cad = CalculoComprimento.ComprimentoUnitario(geoCad, Cfg, dCad);

        // eixo 3D, num plano horizontal a 2 m de altura, em qualquer sentido
        var eixo = new[] { (0.0, 0.0, 200.0), (0.0, 35.0, 200.0), (35.0, 35.0, 200.0), (35.0, 0.0, 200.0) };
        double revit = ComprimentoPorEixo.Calcular(eixo, true, 5, "CA-60", true, true, Cfg);
        Assert.Equal(cad, revit, 6);
        Assert.True(revit > 4 * 35, "o estribo com ganchos e passagens é maior que o perímetro do eixo");
    }

    [Fact]
    public void GrampoTemDoisGanchosDe135()
    {
        var regras = Cfg.Dobramento;
        double lg = regras.ComprimentoGancho(FormatoSegmento.Gancho45, 5, "CA-60", true);
        var (geo, d) = ComprimentoPorEixo.Montar(new[] { (0.0, 0.0, 0.0), (30.0, 0.0, 0.0) }, false, 5, "CA-60", true, true, Cfg);
        Assert.Equal(new[] { lg, 30.5, lg }, geo.Select(s => s.Comprimento).ToArray());
        Assert.Equal(new[] { FormatoSegmento.Gancho45, FormatoSegmento.Linha, FormatoSegmento.Gancho45 }, d.Segmentos.Select(s => s.Formato).ToArray());
    }
}
