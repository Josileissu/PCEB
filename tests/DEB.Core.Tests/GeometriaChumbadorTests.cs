using DEB.Core.Elementos.Metalica;
using DEB.Core.Elementos.Metalica.Ligacoes;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Chumbador da placa de base (D50): a forma pela opção ou pelo produto do catálogo, e as medidas da família.</summary>
public class GeometriaChumbadorTests
{
    private static DadosPlacaBase Placa(params (string, string)[] v)
    {
        var m = new ModeloLigacao { Id = "T", Nome = "T", Tipo = TipoLigacao.PlacaBase };
        foreach (var (k, x) in v) m.Valores[k] = x;
        return DadosPlacaBase.DoModelo(m, PerfilI.Buscar("W250X32.7"), 1.4);
    }

    [Fact]
    public void Pre_concretado_e_gancho_com_embutimento_de_20_d()
    {
        var g = GeometriaChumbador.DaPlaca(Placa(("dChumb", "25")), CatalogoFixadores.Carregar(), tp: 19);
        Assert.Equal(FormaChumbador.Gancho, g.Forma);
        Assert.Equal(500, g.Embutimento, 6);           // 20·25
        Assert.Equal(19 + 25, g.Pega, 6);             // placa + graute
        Assert.Equal(2.5 * 25, g.RaioGancho, 6);
        Assert.Equal("PCEB Chumbador J ø25 L500 P44", g.NomeFamilia());
        Assert.Equal(FormaChumbador.RetoComPorca, GeometriaChumbador.DaPlaca(Placa(("formaChumbador", "Reto com porca de ancoragem")), null, 19).Forma);
    }

    [Fact]
    public void Produto_do_catalogo_da_a_forma_e_o_furo()
    {
        var d = Placa(("dChumb", "15.875"), ("fck", "30"));
        d.ProdutoAncoragem = "Hardinc VI 1101 + barra roscada";
        var g = GeometriaChumbador.DaPlaca(d, CatalogoFixadores.Carregar(), 16);
        Assert.Equal(FormaChumbador.Quimico, g.Forma);
        Assert.Equal(190, g.Embutimento, 6);          // o maior do catálogo para 5/8"
        Assert.True(g.DiametroFuro > g.Db);
    }
}
