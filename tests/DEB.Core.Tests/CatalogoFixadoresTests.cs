using System.Linq;
using DEB.Core.Elementos.Metalica;
using DEB.Core.Elementos.Metalica.Ligacoes;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Catálogo de fixadores (D50, item 6): parafusos das normas e chumbadores de catálogo.</summary>
public class CatalogoFixadoresTests
{
    private const string Json = @"{ ""chumbadores"": [
      { ""produto"": ""Químico X"", ""fabricante"": ""teste"", ""fonte"": ""tabela de teste"", ""natureza"": ""Cálculo"",
        ""linhas"": [ { ""db"": 19.05, ""hef"": 100, ""fck"": 20, ""n"": 20, ""v"": 40, ""scr"": 300, ""ccr"": 150, ""hmin"": 150 },
                      { ""db"": 19.05, ""hef"": 200, ""fck"": 20, ""n"": 60, ""v"": 90 },
                      { ""db"": 19.05, ""hef"": 100, ""fck"": 40, ""n"": 30, ""v"": 50 },
                      { ""db"": 19.05, ""hef"": 200, ""fck"": 40, ""n"": 80, ""v"": 110 } ] },
      { ""produto"": ""Ampola Y"", ""natureza"": ""Recomendada"", ""cobre"": ""ancoragem completa"",
        ""linhas"": [ { ""db"": 12.7, ""hef"": 110, ""fck"": 20, ""n"": 10, ""v"": 10.5 } ] } ] }";

    private static CatalogoFixadores Catalogo()
    {
        var c = CatalogoFixadores.Carregar(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nao_existe_fixadores.json"));
        c.LerJson(Json);
        return c;
    }

    [Fact]
    public void Resistencia_interpola_no_embutimento_e_no_fck_sem_extrapolar()
    {
        var c = Catalogo();
        var r = c.Resistencia("Químico X", 19.05, 150, 30);
        Assert.Equal((40 + 55) / 2.0, r.NRd, 6);          // hef 150: 40 (fck 20) e 55 (fck 40); fck 30: média
        Assert.Equal((65 + 80) / 2.0, r.VRd, 6);
        Assert.Equal(80, c.Resistencia("Químico X", 19.05, 0, 40).NRd, 6);       // hef auto = o maior
        Assert.Equal(0, c.Resistencia("Químico X", 19.05, 150, 15).NRd, 6);      // fck abaixo do catálogo
        Assert.Equal(0, c.Resistencia("Químico X", 19.05, 80, 30).NRd, 6);       // embutimento abaixo do catálogo
        Assert.Null(c.Resistencia("Químico X", 25.4, 150, 30));
        // recomendada (serviço) vira de cálculo × 1,4
        Assert.Equal(14, c.Resistencia("Ampola Y", 12.7, 110, 25).NRd, 6);
    }

    [Fact]
    public void Comprimento_comercial_do_parafuso_pela_pega()
    {
        var c = Catalogo();
        var p = c.Parafuso(19.05);
        Assert.Equal("3/4\"", p.Nome);
        // pega 25 + arruela 4 + porca 18,7 + 3 fios (2,54) = 55,3 mm → 2¼" = 57,15 mm
        Assert.Equal(2.25 * 25.4, p.ComprimentoPara(25), 6);
        Assert.Equal(55, c.Parafuso(16).ComprimentoPara(30), 6);    // M16: 30 + 3 + 14,8 + 3 × 2 = 53,8 → 55 mm
    }

    [Fact]
    public void Placa_de_base_confere_a_ancoragem_do_catalogo()
    {
        var m = new ModeloLigacao { Id = "T", Nome = "T", Tipo = TipoLigacao.PlacaBase };
        m.Valores["dChumb"] = "19.05"; m.Valores["hef"] = "200"; m.Valores["fck"] = "30";
        var d = DadosPlacaBase.DoModelo(m, PerfilI.Buscar("W250X32.7"), 1.4);
        d.ProdutoAncoragem = "Químico X"; d.Catalogo = Catalogo();
        var v = LigacaoPlacaBase.Verificar(d, new[] { ("tração", 60.0, 0.0, 0.0) }, "g", out var mem);
        var pb9 = v.Single(x => x.Codigo == "PB9");
        // 60 kN em 4 chumbadores = 15 kN; NRd (hef 200, fck 30) = 70 kN → η ≈ 0,21
        Assert.StartsWith("η = 0,2", pb9.Valor);
        Assert.Contains(v, x => x.Codigo == "PB10");
    }

    [Fact]
    public void Catalogo_do_PCEB_vem_embutido_e_o_escritorio_substitui_pelo_nome()
    {
        string arq = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pceb_fix_" + System.Guid.NewGuid().ToString("N") + ".json");
        var c = CatalogoFixadores.Carregar(arq + ".nao_existe");
        Assert.Contains("Hardinc VI 1101 + barra roscada", c.Produtos);
        Assert.Contains("Walsywa WQA (ampola)", c.Produtos);
        // Hardinc 5/8" (M16), hef 125, C30: 4.470 kgf de cálculo (Tabela 6) = 43,84 kN
        Assert.Equal(4470 * 0.00980665, c.Resistencia("Hardinc VI 1101 + barra roscada", 15.875, 125, 30).NRd, 1);
        // o escritório troca o produto inteiro pelo mesmo nome
        System.IO.File.WriteAllText(arq, @"{ ""chumbadores"": [ { ""produto"": ""Walsywa WQA (ampola)"", ""natureza"": ""Cálculo"", ""linhas"": [ { ""db"": 12.7, ""hef"": 110, ""fck"": 20, ""n"": 99, ""v"": 9 } ] } ] }");
        try
        {
            var c2 = CatalogoFixadores.Carregar(arq);
            Assert.Single(c2.Chumbadores, x => x.Produto == "Walsywa WQA (ampola)");
            Assert.Equal(99, c2.Resistencia("Walsywa WQA (ampola)", 12.7, 110, 25).NRd, 6);
            Assert.Contains("Hardinc VI 1101 + barra roscada", c2.Produtos);
        }
        finally { System.IO.File.Delete(arq); }
    }
}
