using System;
using System.Linq;
using DEB.Core.Configuracao.Projeto;
using DEB.Core.Elementos.Consolo;
using DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D;
using DEB.Core.Intercambio;
using DEB.Core.Normas.Verificacao;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Cálculo do consolo (D42, etapa 1) conferido com o exemplo A.3 de EL DEBS, Concreto pré-moldado (2017).</summary>
public class CalculoConsoloTests
{
    private static void Perto(double esperado, double obtido, double tol = 0.01) =>
        Assert.True(Math.Abs(obtido - esperado) <= tol * Math.Abs(esperado), $"esperado {esperado}, obtido {obtido}");

    /// <summary>Consolo do exemplo A.3: b = 400, h = 350, c = 300 mm, d = 300 mm (cobrimento ajustado para o d do livro), C40, CA-50,
    /// pré-fabricado; tirante 4ø25, costura 5 grampos ø10, 3 estribos ø10 (o arranjo adotado no livro).</summary>
    private static DadosConsolo ConsoloA3() => new DadosConsolo
    {
        Nome = "A.3", B = 40, A = 30, H = 35, H1 = 20, Cob = 3.75, TirN = 4, TirPhi = 25, CostN = 5, CostPhi = 10, VertN = 3, VertPhi = 10,
        FckMPa = 40, FykMPa = 500,
    };

    private static ConfiguracaoProjeto PreFabricado()
    {
        var cfg = new ConfiguracaoProjeto();
        cfg.Definir(NivelConfiguracao.Projeto, "normas.tipoElemento", CatalogoConfiguracao.PreFabricado);
        return cfg;
    }

    [Fact]
    public void ElDebs_ExemploA3()
    {
        var r = CalculoConsolo.Calcular(ConsoloA3(), new CargaConsolo { Vd = 691, Hd = 0.2 * 691 }, PreFabricado());
        Assert.Equal(30, r.D, 6);
        Assert.Equal(22.5, r.A, 6);                 // ¾ de 300 mm
        Perto(0.75, r.AsobreD);
        Perto(17.05, r.AsTirNec);                   // livro: 17,05 cm²
        Perto(6.62, r.AsCostNec);                   // livro: 6,62 cm²
        Perto(3.41, r.AsVertNec);                   // livro: 3,41 cm² (0,2·As,tir)
        // concreto: τwd = 691/(40·30) = 5,76 MPa ≤ 0,2·40/1,3 = 6,15 MPa
        Perto(5.76, r.TauWd);
        Perto(6.15, r.TauLim);
        Assert.All(r.Verificacoes, v => Assert.Equal(SituacaoVerificacao.Ok, v.Situacao));
        Assert.Contains(r.Memoria.Linhas, l => l.Contains("El Debs"));
    }

    [Fact]
    public void ForcaMaximaDoLivroEstaNoLimiteDoConcreto()
    {
        // o livro tira Vd,máx do consolo = 0,2·fcd·b·d = 739 kN: logo acima disso a biela não passa
        var r = CalculoConsolo.Calcular(ConsoloA3(), new CargaConsolo { Vd = 760, Hd = 152 }, PreFabricado());
        Assert.Equal(SituacaoVerificacao.NaoAtende, r.Verificacoes.First(v => v.Codigo == "CC2").Situacao);
    }

    [Fact]
    public void CargaCaracteristicaUsaGamaNDaConfiguracao()
    {
        var c = ConsoloA3();
        var preMoldado = CalculoConsolo.Calcular(c, new CargaConsolo { Vg = 200, Vq = 100 });
        Assert.Equal(1.1, preMoldado.GamaN, 6);                        // pré-moldado, permanente preponderante
        Perto(1.1 * (1.4 * 200 + 1.4 * 100), preMoldado.Vd);
        // Hd = máx(0,16 do elastômero; 0,2 mínimo) · Vd
        Perto(0.2 * preMoldado.Vd, preMoldado.Hd);
        var pf = CalculoConsolo.Calcular(c, new CargaConsolo { Vg = 100, Vq = 200 }, PreFabricado());
        Assert.Equal(1.1, pf.GamaN, 6);                                // pré-fabricado, variável preponderante
    }

    [Fact]
    public void ClassesPeloAsobreD()
    {
        var c = ConsoloA3();
        Assert.Contains("muito curto", CalculoConsolo.Calcular(c, new CargaConsolo { Vd = 300, A = 9 }).Classe);
        var longo = CalculoConsolo.Calcular(c, new CargaConsolo { Vd = 100, A = 45 });
        Assert.Equal(SituacaoVerificacao.Conferir, longo.Verificacoes.First(v => v.Codigo == "CC1").Situacao);
        var cfg = new ConfiguracaoProjeto();
        cfg.Definir(NivelConfiguracao.Projeto, "dim.consoloCurto", "NBR 9062 7.3");
        var nbr = CalculoConsolo.Calcular(c, new CargaConsolo { Vd = 400, Hd = 80 }, cfg);
        // (0,1 + 0,75)·400/43,48 + 80/43,48
        Perto((0.85 * 400 + 80) / 43.478, nbr.AsTirNec);
        Assert.Equal(SituacaoVerificacao.Ok, nbr.Verificacoes.First(v => v.Codigo == "CC2").Situacao);
    }

    /// <summary>Pilar 40×40×600 cm com um consolo 40×40×30 cm na face +X, topo a 350 cm.</summary>
    private static (ColumnBox F, Corbel K) PilarComConsolo()
    {
        var f = new ColumnBox(U_.Mm(400), U_.Mm(400), U_.Mm(6000));
        var k = new Corbel
        {
            Face = ColumnFace.PX, S0 = 0, S1 = U_.Mm(400), ZBottom = U_.Mm(3100), ZTop = U_.Mm(3500), Projection = U_.Mm(300), TipHeight = U_.Mm(200),
        };
        return (f, k);
    }

    [Fact]
    public void ReacaoDaVigaEntraNasVerificacoesENoRelatorioDoPilar()
    {
        var (f, k) = PilarComConsolo();
        var s = ColumnRebarSpec.Default(f, 1000, new[] { k });
        Assert.DoesNotContain(s.Verificacoes(f, new[] { k }), v => v.Codigo == "CC3");          // sem viga: só as regras do desenho
        k.Carga = new CargaConsolo { Vg = 150, Vq = 60, FatorHd = CargaConsolo.FatorDoApoio(new ApoioPeca { Tipo = ApoioPeca.Neoprene }), Origem = "reação da viga V1, ponta esquerda" };
        var rel = s.Relatorio(f, new[] { k }, "P1", 2, "teste");
        foreach (string cod in new[] { "CC2", "CC3", "CC4", "CC5", "CC6" })
            Assert.Contains(rel.Verificacoes, v => v.Codigo == cod && v.Grupo.Contains("face 3"));
        var mem = Assert.Single(rel.Memoria);
        Assert.Contains(mem.Linhas, l => l.Contains("viga V1"));
        Assert.Contains(mem.Linhas, l => l.Contains("aparelho de apoio"));
        // a configuração do projeto chega ao cálculo (pré-fabricado: γn = 1,0 com permanente preponderante)
        var r = CalculoConsolo.Calcular(new DadosConsolo { B = 40, A = 30, H = 40, H1 = 20, Cob = 2.5, TirN = 4, TirPhi = 16, CostN = 3, CostPhi = 10, VertN = 2, VertPhi = 8 },
                                        k.Carga, PreFabricado());
        s.UsarConfiguracao(PreFabricado());
        rel = s.Relatorio(f, new[] { k }, "P1", 2, "teste");
        Assert.Contains(rel.Memoria[0].Linhas, l => l.Contains($"= {DEB.Core.Fmt.Virgula(r.Vd, "0.0")} kN"));
    }

    [Fact]
    public void HdPeloAparelhoDeApoioEEnvoltoriaDoGrupo()
    {
        Assert.Equal(0, CargaConsolo.FatorDoApoio(null));
        Assert.Equal(0.5, CargaConsolo.FatorDoApoio(new ApoioPeca { Tipo = ApoioPeca.Argamassa }));
        Assert.Equal(0.8, CargaConsolo.FatorDoApoio(new ApoioPeca { Tipo = ApoioPeca.Direto }));
        // argamassa: Hd = 0,5·Vd (acima do mínimo de 0,2)
        var r = CalculoConsolo.Calcular(ConsoloA3(), new CargaConsolo { Vd = 300, FatorHd = 0.5 });
        Perto(150, r.Hd);
        var m = CargaConsolo.Maior(new[] { new CargaConsolo { Vg = 100, Vq = 20, Origem = "V1" }, null, new CargaConsolo { Vg = 90, Vq = 50, Origem = "V2" } });
        Assert.Equal(90, m.Vg);
        Assert.Contains("V2", m.Origem);
        Assert.Contains("2 reações", m.Origem);
        Assert.Null(CargaConsolo.Maior(new CargaConsolo[] { null }));
    }
}
