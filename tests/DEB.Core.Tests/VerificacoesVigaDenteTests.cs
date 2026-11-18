using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Documentacao;
using DEB.Core.Elementos.Pilar.PreMoldado;
using DEB.Core.Elementos.Viga.PreMoldada;
using DEB.Core.Motor;
using DEB.Core.Normas.Verificacao;
using Xunit;
using VV = DEB.Core.Normas.Verificacao.VerificacoesViga;

namespace DEB.Core.Tests;

/// <summary>Viga pré-moldada (V): Etapa 4 das verificações normativas, sem esforços.</summary>
public class VerificacoesVigaTests
{
    // viga 20 x 75 (V144), d ≈ 71 cm, C40
    private static DadosViga V144() => new DadosViga
    {
        B = 20, H = 75, D = 71, FckMPa = 40, AgregadoMm = 19, EstBitola = 6.3, EstDistRamos = 14.4,
        Zonas = new List<(string, double)> { ("vão", 15) },
    };

    private static BarraSecaoViga Barra(double x, double y, double phi, FuncaoBarraViga f = FuncaoBarraViga.Inferior) =>
        new BarraSecaoViga { X = x, Y = y, Bitola = phi, Funcao = f };

    [Fact]
    public void ArmaduraMinimaPelaTabela173EMaxima()
    {
        Assert.Equal(SituacaoVerificacao.Ok, VV.ArmaduraMinima(V144(), 6.03).Situacao);          // 0,40 % ≥ 0,179 %
        Assert.Equal(SituacaoVerificacao.NaoAtende, VV.ArmaduraMinima(V144(), 2.0).Situacao);    // 0,133 %
        var p = V144(); p.Protendida = true;                                                    // a ativa também conta
        Assert.Equal(SituacaoVerificacao.Conferir, VV.ArmaduraMinima(p, 2.0).Situacao);
        Assert.Equal(SituacaoVerificacao.NaoAtende, VV.ArmaduraMaxima(V144(), 70).Situacao);     // 4,67 % > 4 %
        Assert.Equal(SituacaoVerificacao.Ok, VV.ArmaduraMaxima(V144(), 50).Situacao);
    }

    [Fact]
    public void PeleSoAcimaDe60cmComAreaEEspacamento()
    {
        var baixa = V144(); baixa.H = 60;
        Assert.Empty(VV.Pele(baixa, new List<BarraSecaoViga>()));
        var sem = VV.Pele(V144(), new List<BarraSecaoViga>()).ToList();
        Assert.Single(sem);
        Assert.Equal(SituacaoVerificacao.NaoAtende, sem[0].Situacao);
        // 2 ø10 por face entre a inferior (y 4) e a superior (y 71): 22,3 cm entre barras > 20 cm
        var barras = new List<BarraSecaoViga>
        {
            Barra(4, 4, 16), Barra(16, 4, 16), Barra(3.6, 71, 10, FuncaoBarraViga.Superior), Barra(16.4, 71, 10, FuncaoBarraViga.Superior),
            Barra(3.6, 26.3, 10, FuncaoBarraViga.Pele), Barra(3.6, 48.6, 10, FuncaoBarraViga.Pele),
            Barra(16.4, 26.3, 10, FuncaoBarraViga.Pele), Barra(16.4, 48.6, 10, FuncaoBarraViga.Pele),
        };
        var r = VV.Pele(V144(), barras).ToList();
        Assert.Equal(SituacaoVerificacao.Ok, r[0].Situacao);                                     // 1,57 ≥ 1,50 cm²
        Assert.Equal(SituacaoVerificacao.NaoAtende, r[1].Situacao);
        Assert.Contains("22,", r[1].Valor);
    }

    [Fact]
    public void EspacamentoLivreHorizontalEVertical()
    {
        var ok = VV.EspacamentoLivre(V144(), new[] { Barra(4, 4, 16), Barra(10, 4, 16), Barra(16, 4, 16) }).ToList();
        Assert.All(ok, x => Assert.Equal(SituacaoVerificacao.Ok, x.Situacao));                   // 44 mm ≥ 22,8 mm
        var junto = VV.EspacamentoLivre(V144(), new[] { Barra(4, 4, 16), Barra(6, 4, 16) }).Single();
        Assert.Equal(SituacaoVerificacao.NaoAtende, junto.Situacao);                             // 4 mm
        var camadas = VV.EspacamentoLivre(V144(), new[] { Barra(4, 4, 16), Barra(16, 4, 16), Barra(4, 6.5, 16), Barra(16, 6.5, 16) }).ToList();
        Assert.Equal(SituacaoVerificacao.NaoAtende, camadas.Single(x => x.Descricao.Contains("vertical")).Situacao);   // 9 mm < 20 mm
    }

    [Fact]
    public void EstribosBitolaEspacamentoRamosETaxa()
    {
        SituacaoVerificacao S(DadosViga v, string cod) => VV.Estribos(v).First(x => x.Codigo == cod).Situacao;
        Assert.All(VV.Estribos(V144()), x => Assert.Equal(SituacaoVerificacao.Ok, x.Situacao));
        var v = V144(); v.EstBitola = 4.2;
        Assert.Equal(SituacaoVerificacao.NaoAtende, S(v, "V5"));
        v = V144(); v.EstBitola = 25;                                                           // > bw/10 = 20 mm
        Assert.Equal(SituacaoVerificacao.NaoAtende, S(v, "V5"));
        v = V144(); v.Zonas = new List<(string, double)> { ("vão", 25) };                        // 0,3d = 20 < 25 ≤ 0,6d ≤ 30
        Assert.Equal(SituacaoVerificacao.Conferir, S(v, "V6"));
        v.Zonas = new List<(string, double)> { ("vão", 35) };
        Assert.Equal(SituacaoVerificacao.NaoAtende, S(v, "V6"));
        v = V144(); v.EstDistRamos = 50;                                                        // 0,6d ≤ 35 < 50 ≤ d
        Assert.Equal(SituacaoVerificacao.Conferir, S(v, "V7"));
        v.EstDistRamos = 75;
        Assert.Equal(SituacaoVerificacao.NaoAtende, S(v, "V7"));
        // ø5 CA-60 c/30 em bw 40: ρsw = 0,033 % < 0,2·3,51/600 = 0,117 %
        v = new DadosViga { B = 40, H = 75, D = 71, FckMPa = 40, EstBitola = 5, AcoEstribo = "CA-60", EstDistRamos = 30, Zonas = { ("vão", 30) } };
        Assert.Equal(SituacaoVerificacao.NaoAtende, S(v, "V8"));
    }

    [Fact]
    public void FctmEAncoragemNoApoioReto()
    {
        Assert.Equal(3.509, TabelasNbr.Fctm(40), 3);
        Assert.Equal(4.586, TabelasNbr.Fctm(70), 3);
        Assert.Equal(SituacaoVerificacao.Ok, VV.AncoragemNoApoio("esquerda", 6, 2).Situacao);    // 1/3
        Assert.Equal(SituacaoVerificacao.Conferir, VV.AncoragemNoApoio("esquerda", 6, 1.5).Situacao);
    }

    [Fact]
    public void CobrimentoDoProtendido()
    {
        Assert.Equal(35, TabelasNbr.CobrimentoNominalMm(ClasseAgressividade.II, ElementoCobrimento.VigaPilar, protendido: true));
        Assert.Equal(30, RegrasCobrimento.Exigido(new DadosCobrimento { Classe = ClasseAgressividade.II, Protendido = true }).Mm);
        // classe I: 30 − 5 − 5 = 20 → limitado a 25 mm (protendido, NBR 9062)
        Assert.Equal(25, RegrasCobrimento.Exigido(new DadosCobrimento
            { Classe = ClasseAgressividade.I, Protendido = true, FckMPa = 40, AguaCimentoAte045 = true }).Mm);
    }
}

/// <summary>Dente Gerber (NBR 9062 7.4): D1–D8 com os dados do catálogo.</summary>
public class VerificacoesDenteTests
{
    // dente 45 x 25 do catálogo numa viga 20 x 75 (dvig = 71,07): tirante 3 ø16 C=200, grampo 4 ø10 e costura 3 ø10 com pernas de 100
    private static DadosDente DG45() => new DadosDente
    {
        Nome = "ponta esquerda, DG 45x25", B = 20, DVig = 71.07, Altura = 45, Comprimento = 25, CobTopo = 2.5, CobPontas = 2.5,
        GerberN = 4, GerberPhi = 8, TirN = 3, TirPhi = 16, TirComp = 200, GrampoN = 4, GrampoPhi = 10, GrampoPerna = 100,
        CosturaN = 3, CosturaPhi = 10, CosturaPerna = 100, SuspPhi = 10, TrechoSuspensao = 20,
        SuspPosicoes = TemplateVigaPreMoldada.PosicoesSuspensao(new TipoDente { Comprimento = 25, SuspQtd = 5, SuspBitola = 10 }, 2.5, true, 308, 20),
        Furos = 1, FckMPa = 40,
    };

    private static ResultadoVerificacao Cod(DadosDente t, string c, string trecho = "") =>
        VerificacoesDente.Verificar(t).First(x => x.Codigo == c && x.Descricao.Contains(trecho));

    [Fact]
    public void DenteDoCatalogoAtende()
    {
        var r = VerificacoesDente.Verificar(DG45());
        Assert.DoesNotContain(r, x => x.Situacao == SituacaoVerificacao.NaoAtende);
        Assert.Equal(SituacaoVerificacao.Conferir, r.Single(x => x.Codigo == "D7").Situacao);   // precisa de Fd
        // D5: as regras do consolo, sem as do aparelho de apoio e sem a C15 (é a D4)
        Assert.Contains(r, x => x.Codigo == "D5" && x.Descricao.Contains("regra C9"));
        Assert.DoesNotContain(r, x => x.Descricao.Contains("regra C1 ") || x.Descricao.Contains("regra C15"));
        Assert.Equal(62.9, VerificacoesDente.LbMa(16, 40, "CA-50"), 1);
    }

    [Fact]
    public void AncoragemDoTiranteEPenetracaoDosEstribosHorizontais()
    {
        var t = DG45(); t.TirComp = 100;     // começa em 28 + 30,2: 44,3 cm < lb = 62,9 e ≥ lb,mín = 18,9
        Assert.Equal(SituacaoVerificacao.Conferir, Cod(t, "D2").Situacao);
        t.TirComp = 60;
        Assert.Equal(SituacaoVerificacao.NaoAtende, Cod(t, "D2").Situacao);
        t = DG45(); t.GrampoPerna = 60;      // 2,5 + 60 − 25 = 37,5 < 1,5·lb = 59
        Assert.Equal(SituacaoVerificacao.NaoAtende, Cod(t, "D3", "grampo").Situacao);
        Assert.Equal(SituacaoVerificacao.Ok, Cod(t, "D3", "costura").Situacao);
        t = DG45(); t.TirN = 0;
        Assert.Equal(SituacaoVerificacao.NaoAtende, Cod(t, "D1").Situacao);
    }

    [Fact]
    public void SuspensaoEFuros()
    {
        var t = DG45(); t.SuspPosicoes = new List<double> { 28, 31, 34, 37, 50 };   // o último sai do trecho de 20 cm
        Assert.Equal(SituacaoVerificacao.NaoAtende, Cod(t, "D6", "Suspensão").Situacao);
        t.SuspPosicoes = new List<double>();
        Assert.Equal(SituacaoVerificacao.NaoAtende, Cod(t, "D6").Situacao);
        t = DG45(); t.GerberN = 0;
        Assert.Equal(SituacaoVerificacao.NaoAtende, Cod(t, "D4").Situacao);
        t = DG45(); t.Furos = 2;                                                    // b = 20 < 30: 1 furo
        Assert.Equal(SituacaoVerificacao.NaoAtende, Cod(t, "D8").Situacao);
        t.B = 30;
        Assert.Equal(SituacaoVerificacao.Ok, Cod(t, "D8").Situacao);
    }
}

/// <summary>O que entrou no consolo: C12, C14, C16, C17.</summary>
public class VerificacoesConsoloSuporteTests
{
    private static DadosConsolo P61() => new DadosConsolo
    {
        Nome = "CD 40x23,5x25", B = 40, A = 25, H = 23.5, H1 = 23.5, Cob = 3.5, TirN = 5, TirPhi = 16, CostN = 4, CostPhi = 8,
        VertN = 5, VertPhi = 6.3, FckMPa = 40, FykMPa = 500,
    };

    private static DadosConsolo CS20() => new DadosConsolo
    {
        Nome = "CS 20x28,5x25", B = 20, A = 25, H = 28.5, H1 = 28.5, Cob = 3.5, TirN = 2, TirPhi = 12.5, CostN = 4, CostPhi = 6.3, FckMPa = 40,
    };

    [Fact]
    public void DispensaDeCosturaETiranteSemReducao()
    {
        var r = VerificacoesConsolo.Verificar(P61());
        var c12 = r.Single(x => x.Codigo == "C12");
        Assert.Equal(SituacaoVerificacao.Ok, c12.Situacao);
        Assert.Contains("não se aplica", c12.Descricao);                       // d = 19,2 ≤ 4a = 100
        Assert.Equal(SituacaoVerificacao.Ok, r.Single(x => x.Codigo == "C14").Situacao);
    }

    [Fact]
    public void ArmaduraNaFaceDoPilarEAncoragemDoTirante()
    {
        Assert.Equal(SituacaoVerificacao.NaoAtende, VerificacoesConsolo.ArmaduraNaFaceDoSuporte(P61(), "1", 5).Situacao);   // < 10,05 cm²
        Assert.Equal(SituacaoVerificacao.Ok, VerificacoesConsolo.ArmaduraNaFaceDoSuporte(P61(), "1", 12).Situacao);
        // ø12,5, C40: 0,7·lb(má) = 34,4 cm; lb,mín = máx(0,3·34,4; 12,5; 10) = 12,5 cm
        Assert.Equal(SituacaoVerificacao.Ok, VerificacoesConsolo.AncoragemNoSuporte(CS20(), false, 40, true, "CA-50").Situacao);
        Assert.Equal(SituacaoVerificacao.Conferir, VerificacoesConsolo.AncoragemNoSuporte(CS20(), false, 20, true, "CA-50").Situacao);
        Assert.Equal(SituacaoVerificacao.NaoAtende, VerificacoesConsolo.AncoragemNoSuporte(CS20(), false, 10, true, "CA-50").Situacao);
        Assert.Equal(SituacaoVerificacao.Ok, VerificacoesConsolo.AncoragemNoSuporte(CS20(), true, 0, true, "CA-50").Situacao);
    }
}

/// <summary>Templates alimentando o relatório: a viga (V, D, G) e o pilar como suporte do consolo (C16, C17).</summary>
[Collection("Pilar")]
public class VerificacoesTemplatesVigaTests
{
    private static ResultadoTemplate Viga(params (string k, string v)[] mudancas)
    {
        var t = new TemplateVigaPreMoldada();
        var v = new ValoresTemplate(t.Parametros);
        v["Nome"] = "V144"; v["L"] = "308";
        foreach (var (k, x) in mudancas) v[k] = x;
        Assert.Empty(CatalogoTemplates.Validar(t, v));
        return t.Gerar(v, new ConfiguracaoArmadura());
    }

    [Fact]
    public void VigaPreencheORelatorioEAvisaSoAsFalhas()
    {
        var r = Viga();
        Assert.Equal("Viga pré-moldada", r.ElementoRelatorio);
        Assert.Contains(r.DadosRelatorio, d => d.Key == "Classe de agressividade" && d.Value == "II");
        foreach (var c in new[] { "V1", "V2", "V3", "V4", "V5", "V6", "V7", "V8", "G2", "G3", "G4", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8" })
            Assert.Contains(r.Verificacoes, x => x.Codigo == c);
        // padrão do escritório (2 ø10 de pele por face em h = 75): 22,6 cm entre barras > 20 cm
        Assert.Contains(r.Avisos, a => a.StartsWith("Não atende (V3)"));
        Assert.DoesNotContain(r.Avisos, a => a.StartsWith("OK ("));
        Assert.NotNull(RelatorioHtml.DoTemplate(r, "V144", "teste"));
    }

    [Fact]
    public void FurosDigitadosEPontasRetas()
    {
        var r = Viga(("FurosModo", TemplateVigaPreMoldada.FurosDigitados), ("FurosE", "2"), ("FurosD", "1"));
        Assert.Equal(SituacaoVerificacao.NaoAtende, r.Verificacoes.Single(x => x.Codigo == "D8" && x.Grupo.Contains("esquerda")).Situacao);
        Assert.Contains(r.Avisos, a => a.StartsWith("Não atende (D8)"));
        var reta = Viga(("ExtE", TemplateVigaPreMoldada.Reta), ("ExtD", TemplateVigaPreMoldada.Reta));
        Assert.DoesNotContain(reta.Verificacoes, x => x.Codigo.StartsWith("D"));
        Assert.Equal(2, reta.Verificacoes.Count(x => x.Codigo == "V11"));
        var iv = Viga(("ClasseAgressividade", "IV"));                         // 25 mm < 50 − 5 = 45 mm
        Assert.Equal(SituacaoVerificacao.NaoAtende, iv.Verificacoes.Single(x => x.Codigo == "G2").Situacao);
    }

    [Fact]
    public void PilarComoSuporteDoConsolo()
    {
        var t = new TemplatePilarPreMoldado();
        var v = new ValoresTemplate(t.Parametros);
        v["Consolos"] = "CS 20x28,5x25|1|592.5|0";
        var r = t.Gerar(v, new ConfiguracaoArmadura { AlturaTextoMm = 2 });
        Assert.Equal(SituacaoVerificacao.Ok, r.Verificacoes.Single(x => x.Codigo == "C16").Situacao);
        var c17 = r.Verificacoes.Single(x => x.Codigo == "C17");
        Assert.Equal(SituacaoVerificacao.Ok, c17.Situacao);                   // 50 − 3,5 − φt − φl ≥ 0,7·lb(má) = 34,4
        Assert.DoesNotContain("passante", c17.Descricao);
    }
}
