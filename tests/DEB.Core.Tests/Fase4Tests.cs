using System;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Descricao;
using DEB.Core.Modelo;
using DEB.Core.Normas;
using DEB.Core.RelacaoAco;
using DEB.Core.Tabelas;
using Xunit;

namespace DEB.Core.Tests;

public class Fase4Tests
{
    // ---------- cordoalhas ----------

    [Fact]
    public void CordoalhasNaTabelaPadrao()
    {
        var t = TabelaBarras.Padrao();
        var c = t.Obter(12.7, "CP-190");
        Assert.NotNull(c);
        Assert.True(TabelaBarras.EhCordoalha(c.Aco));
        Assert.Equal(0.792, t.MassaLinear(12.7, "CP-190"), 3);
        Assert.Equal(Fabricacao.Rolo, c.Fabricacao);
        Assert.Equal(3, t.Bitolas.Count(b => TabelaBarras.EhCordoalha(b.Aco)));
    }

    [Fact]
    public void ConfiguracaoV2GanhaCordoalhas()
    {
        // arquivo da versão 2 com uma tabela sem cordoalhas
        var v2 = new ConfiguracaoArmadura();
        v2.Barras.Bitolas.RemoveAll(b => TabelaBarras.EhCordoalha(b.Aco));
        var json = v2.ParaJson().Replace($"\"VersaoConfig\": {ConfiguracaoArmadura.VersaoAtual}", "\"VersaoConfig\": 2");
        var cfg = ConfiguracaoArmadura.DeJson(json);
        Assert.True(cfg.Migrada);
        Assert.Equal(3, cfg.Barras.Bitolas.Count(b => TabelaBarras.EhCordoalha(b.Aco)));
        Assert.False(ConfiguracaoArmadura.DeJson(cfg.ParaJson()).Migrada);
    }

    [Fact]
    public void DescricaoDaCordoalhaMostraOAco()
    {
        var d = new DadosArmadura { Numero = 2, Quantidade = 6, Bitola = 12.7, Aco = "CP-190" };
        Assert.Equal("6 N2 ø12.7 CP-190 C=1500", FormatadorDescricao.Formatar(d, 1500, new ConfiguracaoArmadura { SimboloDiametro = "ø" }));
    }

    [Fact]
    public void CordoalhaTemPesoTotalProprioEBobina()
    {
        var cfg = new ConfiguracaoArmadura();
        var r = RelacaoDoAco.Calcular(new[]
        {
            new ItemRA { Numero = 1, Aco = "CA-50", Bitola = 10, Quantidade = 1, ComprimentoUnitario = 1000 },
            new ItemRA { Numero = 2, Aco = "CP-190", Bitola = 12.7, Quantidade = 6, ComprimentoUnitario = 1500 },
        }, cfg);
        Assert.Equal(Math.Round(90 * 0.792, 1), r.PesoPorAco["CP-190"], 1);
        Assert.Null(r.Resumo.Single(s => s.Aco == "CP-190").Barras); // bobina: sem barras comerciais
    }

    // ---------- traspasse ----------

    [Fact]
    public void TraspassePhi10CA50C25BoaAderencia()
    {
        var t = new RegrasTraspasse();
        Assert.Equal(37.66, t.Lb(10, "CA-50"), 1);        // ≈ 38φ
        Assert.Equal(68, t.Comprimento(10, "CA-50"));      // 1,8 × 37,66 = 67,8 → 68
        t.Aderencia = SituacaoAderencia.Ma;
        Assert.Equal(97, t.Comprimento(10, "CA-50"));      // lb/0,7 = 53,8 × 1,8 = 96,8 → 97
        t.Aderencia = SituacaoAderencia.Boa;
        t.PercentualEmendado = 100;
        Assert.Equal(76, t.Comprimento(10, "CA-50"));      // α0t = 2,0
    }

    [Fact]
    public void TraspasseFixoDaTabelaECordoalhaSemTraspasse()
    {
        var tabela = TabelaBarras.Padrao();
        tabela.Obter(10, "CA-50").TraspasseFixo = 55;
        var t = new RegrasTraspasse { Modo = ModoTraspasse.Fixo };
        Assert.Equal(55, t.Comprimento(10, "CA-50", tabela));
        Assert.Equal(new RegrasTraspasse().Comprimento(12.5, "CA-50"), t.Comprimento(12.5, "CA-50", tabela)); // sem fixo: calcula
        Assert.Throws<InvalidOperationException>(() => t.Comprimento(12.7, "CP-190", tabela));
    }

    [Fact]
    public void MinimoDe20cmE15Phi()
    {
        var t = new RegrasTraspasse { Fck = 50, PercentualEmendado = 10 };
        // φ5 CA-50, C50, α0t = 1,2: α·lb = 14,2 cm → vale o mínimo de 20 cm
        Assert.Equal(20, t.Comprimento(5, "CA-50"));
        // φ5 CA-60 (η1 = 1,4): lb = 22,9 cm → 1,2 × 22,9 = 27,5 → 28 cm
        Assert.Equal(28, t.Comprimento(5, "CA-60"));
    }

    // ---------- treliças e telas ----------

    [Fact]
    public void CatalogosEDescricoes()
    {
        var cfg = new ConfiguracaoArmadura { SimboloDiametro = "ø" };
        Assert.Equal(8, cfg.Trelicas.Trelica("tr8644").Altura);
        Assert.Equal(2.20, cfg.Telas.Tela("Q138").PesoM2, 2);
        var tr = new DadosArmadura { Numero = 5, Quantidade = 4, Trelica = "TR 8644", Espacamento = 42 };
        Assert.Equal("4 N5 TR 8644 c/42 C=300", FormatadorDescricao.Formatar(tr, 300, cfg));
        var tela = new DadosArmadura { Quantidade = 2, Tela = "Q-138" };
        Assert.Equal("2 Q-138", FormatadorDescricao.Formatar(tela, 0, cfg));
    }

    [Fact]
    public void SinusoideDaTrelica()
    {
        var s = Catalogos.Sinusoide(45, 8, 20);
        // 0, 10(top), 20, 30(top), 40, 45 → no fim, metade do caminho de 0 até 8
        Assert.Equal(new[] { 0.0, 10, 20, 30, 40, 45 }, s.Select(p => p.X));
        Assert.Equal(new[] { 0.0, 8, 0, 8, 0, 4 }, s.Select(p => Math.Round(p.Y, 6)));
    }

    [Fact]
    public void TrelicasETelasNaRelacao()
    {
        var cfg = new ConfiguracaoArmadura();
        var r = RelacaoDoAco.Calcular(new[]
        {
            new ItemRA { Numero = 1, Aco = "CA-60", Bitola = 5, Quantidade = 10, ComprimentoUnitario = 100 },
            new ItemRA { Numero = 2, Aco = "CA-60", Trelica = "TR 8644", Quantidade = 4, ComprimentoUnitario = 300, MassaLinear = 0.735 },
        }, cfg, telas: new[] { new ItemTela { Modelo = "Q-138", AreaM2 = 2.45 * 6.0, Quantidade = 2, MassaM2 = 2.20 } });

        var linhaTr = r.Linhas.Single(l => l.Numero == 2);
        Assert.True(linhaTr.EhTrelica);
        Assert.Equal("TR 8644", linhaTr.Aco);
        Assert.Equal(Math.Round(12 * 0.735, 1), r.PesoPorAco[RelacaoDoAco.GrupoTrelicas], 1);
        // tela: 29,4 m² × 2,20 = 64,7 kg, somando no CA-60 junto com as barras ø5 (10 m × 0,154 = 1,5)
        var tela = r.Resumo.Single(s => s.Diametro == "Q-138");
        Assert.Equal("29.40 m²", tela.Total);
        Assert.Equal(64.7, tela.Peso, 1);
        Assert.Equal(64.7 + 1.5, r.PesoPorAco["CA-60"], 1);

        var t = MontadorTabela.Montar(r, new OpcoesTabelaRA(), cfg);
        Assert.Contains(t.Linhas, l => l[0] == "TR 8644" && l[2] == "-");
        Assert.Contains(t.Linhas, l => l[1] == "Q-138" && l[2] == "29.40 m²");
        Assert.Contains(t.Linhas, l => l[0] == "PESO TOTAL TRELIÇAS (kg)");
    }

    // ---------- divisão ----------

    private static GeoSegmento R(double l) => new GeoSegmento(TipoSegmento.Reta, l);

    [Fact]
    public void IntervalosValidosSoEmTrechosRetosQueCabemOTraspasse()
    {
        var geo = new[] { R(20), R(1500), R(20) }; // gancho, reta, gancho
        var v = PlanoDivisao.IntervalosValidos(geo, 68);
        var (a, b) = Assert.Single(v);
        Assert.Equal(20 + 34, a, 6);
        Assert.Equal(20 + 1500 - 34, b, 6);
        Assert.Equal(54, PlanoDivisao.MaisProximo(geo, 68, 5).Value, 6); // clique perto da ponta: vai para o 1º válido
    }

    [Fact]
    public void DivisaoAutomaticaPeloComprimentoComercial()
    {
        var geo = new[] { R(2500) };
        var cortes = PlanoDivisao.Automatica(geo, 68, 1200);
        // peças: [0, 1200], [1132, 2332], [2264, 2500] → 3 peças, nenhuma > 1200
        Assert.Equal(2, cortes.Count);
        Assert.Equal(1166, cortes[0], 6);
        Assert.Equal(2298, cortes[1], 6);
        double inicio = 0;
        foreach (var s in cortes) { Assert.True(s + 34 - inicio <= 1200 + 1e-6); inicio = s - 34; }
        Assert.True(2500 - inicio <= 1200);

        Assert.Empty(PlanoDivisao.Automatica(new[] { R(1100) }, 68, 1200)); // já cabe
    }
}
