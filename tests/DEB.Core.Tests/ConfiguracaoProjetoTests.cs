using System;
using System.Linq;
using DEB.Core.Configuracao.Projeto;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Configuração do projeto em três níveis (D43): herança, origem, unidades, bitolas e valores fora da norma.</summary>
public class ConfiguracaoProjetoTests
{
    [Fact]
    public void CatalogoTemIdsUnicosEUmaUnidadePorGrandeza()
    {
        var ids = CatalogoConfiguracao.Campos.Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        foreach (Grandeza g in Enum.GetValues(typeof(Grandeza)))
            Assert.True(CatalogoConfiguracao.Existe(CatalogoConfiguracao.IdUnidade(g)), g.ToString());
        Assert.All(CatalogoConfiguracao.Campos, c => Assert.Contains(CatalogoConfiguracao.Grupos, g => g.Id == c.Grupo));
        Assert.All(CatalogoConfiguracao.Campos.Where(c => c.Tipo is TipoCampo.Opcao or TipoCampo.Unidade), c => Assert.Contains(c.Padrao, c.Opcoes));
    }

    [Fact]
    public void ValorVemDoNivelMaisBaixoQueODefine()
    {
        var cfg = new ConfiguracaoProjeto();
        Assert.Equal((40.0, NivelConfiguracao.Padrao), (cfg.Numero("materiais.fck"), cfg.Ler("materiais.fck").Origem));
        cfg.Definir(NivelConfiguracao.Escritorio, "materiais.fck", "35");
        cfg.Definir(NivelConfiguracao.Projeto, "materiais.fck", "45");
        Assert.Equal(NivelConfiguracao.Projeto, cfg.Ler("materiais.fck").Origem);
        cfg.Definir(NivelConfiguracao.Grupo, "materiais.fck", "50");
        Assert.Equal(50, cfg.Numero("materiais.fck"));
        cfg.Restaurar(NivelConfiguracao.Grupo, "materiais.fck");
        Assert.Equal(45, cfg.Numero("materiais.fck"));
        // igual ao herdado não grava nada
        cfg.Definir(NivelConfiguracao.Grupo, "materiais.fck", "45");
        Assert.Empty(cfg.Grupo.Valores);
    }

    [Fact]
    public void PreFabricadoMudaOsCoeficientesDePartida()
    {
        var cfg = new ConfiguracaoProjeto();
        Assert.Equal(1.4, cfg.Numero("normas.gamaC"));
        Assert.Equal(1.2, cfg.GamaN(false));
        cfg.Definir(NivelConfiguracao.Projeto, "normas.tipoElemento", CatalogoConfiguracao.PreFabricado);
        Assert.Equal(1.3, cfg.Numero("normas.gamaC"));
        Assert.Equal(1.1, cfg.Numero("normas.gamaS"));
        Assert.Equal(1.0, cfg.GamaN(true));
        Assert.Equal(1.1, cfg.GamaN(false));
        Assert.Empty(cfg.ForaDaNorma());
    }

    [Fact]
    public void ForaDaNormaEAceitoComJustificativa()
    {
        var cfg = new ConfiguracaoProjeto();
        // pré-moldado com γc = 1,3: abaixo da NBR 9062 8.1
        cfg.Definir(NivelConfiguracao.Projeto, "normas.gamaC", "1.3");
        var f = Assert.Single(cfg.ForaDaNorma());
        Assert.False(f.Justificado);
        cfg.Definir(NivelConfiguracao.Projeto, "normas.gamaC", "1.3", "controle de fábrica certificado pelo cliente");
        f = Assert.Single(cfg.ForaDaNorma());
        Assert.True(f.Justificado);
        Assert.Contains("justificativa", f.Texto);
    }

    [Fact]
    public void UnidadesConvertemESistemasPreenchem()
    {
        Assert.Equal(9.80665, Unidades.ParaBase(Grandeza.Forca, 1, "tf"), 6);
        Assert.Equal(7400, Unidades.DaBase(Grandeza.Momento, 74, "kN·cm"), 6);
        Assert.Equal(32.2, Unidades.DaBase(Grandeza.ModuloElasticidade, 32200, "GPa"), 6);
        Assert.Equal(25, Unidades.DaBase(Grandeza.Velocidade, 25 / 3.6, "km/h"), 6);
        var cfg = new ConfiguracaoProjeto();
        Assert.Equal("74,0 kN·m", cfg.Formatar(Grandeza.Momento, 74, 1));
        cfg.AplicarSistema(NivelConfiguracao.Projeto, "Técnico · tf, m");
        Assert.Equal("tf", cfg.Unidade(Grandeza.Forca));
        Assert.Equal("7,55 tf·m", cfg.Formatar(Grandeza.Momento, 74, 2));
        Assert.Throws<ArgumentException>(() => cfg.Definir(NivelConfiguracao.Projeto, CatalogoConfiguracao.IdUnidade(Grandeza.Forca), "kN·m"));
    }

    [Fact]
    public void BitolasGuardamOFornecimento()
    {
        var cfg = new ConfiguracaoProjeto();
        var b = cfg.Bitolas();
        Assert.Equal(Fornecimento.Rolo, b.First(x => x.Phi == 5).Fornecimento);
        Assert.Equal(Fornecimento.Barra, b.First(x => x.Phi == 16).Fornecimento);
        Assert.False(b.First(x => x.Phi == 32).Usar);
        b.First(x => x.Phi == 12.5).Fornecimento = Fornecimento.RoloEBarra;
        cfg.Definir(NivelConfiguracao.Escritorio, "det.bitolas", CatalogoConfiguracao.TextoBitolas(b));
        Assert.Equal(Fornecimento.RoloEBarra, cfg.Bitolas().First(x => x.Phi == 12.5).Fornecimento);
    }

    [Fact]
    public void NivelVaiEVoltaEmJson()
    {
        var cfg = new ConfiguracaoProjeto();
        cfg.Definir(NivelConfiguracao.Projeto, "durab.caa", "III");
        cfg.Definir(NivelConfiguracao.Projeto, "normas.gamaC", "1.3", "justificativa de teste");
        string json = cfg.Projeto.ParaJson();
        Assert.Contains("\"durab.caa\"", json);
        var volta = NivelValores.DeJson(json);
        Assert.Equal("III", volta.Valores["durab.caa"]);
        Assert.Equal("justificativa de teste", volta.Justificativas["normas.gamaC"]);
        Assert.Equal(NivelConfiguracao.Projeto, volta.Nivel);
    }
}
