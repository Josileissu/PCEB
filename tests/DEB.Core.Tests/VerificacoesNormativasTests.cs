using System.Linq;
using DEB.Core.Normas.Verificacao;
using Xunit;
using VP = DEB.Core.Normas.Verificacao.VerificacoesPilar;

namespace DEB.Core.Tests;

/// <summary>Regras únicas de verificação normativa (docs/04_arquitetura/verificacoes_normativas.md).</summary>
public class VerificacoesNormativasTests
{
    private static SituacaoVerificacao S(ResultadoVerificacao r) => r.Situacao;

    [Theory]   // NBR 6118:2023, Tabela 17.3 (transcrita da p. 131)
    [InlineData(20, 0.150)] [InlineData(30, 0.150)] [InlineData(35, 0.164)] [InlineData(40, 0.179)] [InlineData(50, 0.208)]
    [InlineData(55, 0.211)] [InlineData(70, 0.233)] [InlineData(90, 0.256)]
    [InlineData(42, 0.194)]   // entre colunas: a de fck acima
    public void Tabela173(double fck, double rho) => Assert.Equal(rho, TabelasNbr.RhoMinVigaPct(fck), 6);

    [Theory]   // Tabela 7.2 (Δc = 10 mm)
    [InlineData(ClasseAgressividade.I, ElementoCobrimento.VigaPilar, 25)]
    [InlineData(ClasseAgressividade.II, ElementoCobrimento.VigaPilar, 30)]
    [InlineData(ClasseAgressividade.IV, ElementoCobrimento.VigaPilar, 50)]
    [InlineData(ClasseAgressividade.III, ElementoCobrimento.Laje, 35)]
    [InlineData(ClasseAgressividade.II, ElementoCobrimento.ContatoSolo, 30)]
    public void Tabela72(ClasseAgressividade c, ElementoCobrimento e, double mm) => Assert.Equal(mm, TabelasNbr.CobrimentoNominalMm(c, e));

    [Fact]
    public void CobrimentoPreMoldadoAplicaAsReducoesDa9062()
    {
        // classe II viga/pilar: 30 → 25 (Δc = 5) → 20 (fck 40 e a/c ≤ 0,45), sem passar de 20
        var d = new DadosCobrimento { Classe = ClasseAgressividade.II, FckMPa = 40, AguaCimentoAte045 = true, CobrimentoMm = 20 };
        Assert.Equal(20, RegrasCobrimento.Exigido(d).Mm);
        var g = RegrasCobrimento.Verificar(d).First();
        Assert.Equal("G2", g.Codigo);
        Assert.Equal(SituacaoVerificacao.Conferir, g.Situacao);   // atende só pelas reduções: avisa
        Assert.Contains("9.2.1.1.2", g.Mensagem);
        // sem a/c declarado só a 1ª redução
        d.AguaCimentoAte045 = false;
        Assert.Equal(25, RegrasCobrimento.Exigido(d).Mm);
        Assert.Equal(SituacaoVerificacao.NaoAtende, S(RegrasCobrimento.Verificar(d).First()));
        // classe I: 25 − 5 − 5 = 15 → limitado a 20 (vigas/pilares de concreto armado)
        d = new DadosCobrimento { Classe = ClasseAgressividade.I, FckMPa = 45, AguaCimentoAte045 = true, CobrimentoMm = 35 };
        Assert.Equal(20, RegrasCobrimento.Exigido(d).Mm);
        Assert.Equal(SituacaoVerificacao.Ok, S(RegrasCobrimento.Verificar(d).First()));
        // moldado no local: Tabela 7.2 sem redução
        Assert.Equal(30, RegrasCobrimento.Exigido(new DadosCobrimento { PreMoldado = false, FckMPa = 50, AguaCimentoAte045 = true }).Mm);
    }

    [Fact]
    public void CobrimentoBitolaEAgregado()
    {
        var r = RegrasCobrimento.Verificar(new DadosCobrimento { CobrimentoMm = 30, MaiorBitolaMm = 32, AgregadoMm = 38 }).ToList();
        Assert.Equal(SituacaoVerificacao.NaoAtende, S(r.Single(x => x.Codigo == "G3")));
        Assert.Equal(SituacaoVerificacao.NaoAtende, S(r.Single(x => x.Codigo == "G4")));   // 38 > 1,2 × 30 = 36
    }

    [Fact]
    public void TaxaMaximaE8PorCentoComEmendas()
    {
        double ac = 400 * 400;
        Assert.Equal(SituacaoVerificacao.Ok, S(VP.TaxaMaxima(0.07 * ac, ac)));       // 7 %: antes o Revit acusava (> 4 %)
        Assert.Equal(SituacaoVerificacao.NaoAtende, S(VP.TaxaMaxima(0.081 * ac, ac)));
        Assert.Equal(SituacaoVerificacao.NaoAtende, S(VP.TaxaMinima(0.0039 * ac, ac)));
        Assert.Equal(SituacaoVerificacao.Ok, S(VP.TaxaMinima(0.004 * ac, ac)));
    }

    [Fact]
    public void DimensaoMinimaEPilarParede()
    {
        Assert.Equal(SituacaoVerificacao.Ok, S(VP.DimensaoMinima(200, 400)));
        var r = VP.DimensaoMinima(160, 400);
        Assert.Equal(SituacaoVerificacao.Conferir, r.Situacao);
        Assert.Contains("1,15", r.Dica);                                             // γn = 1,95 − 0,05·16
        Assert.Equal(SituacaoVerificacao.NaoAtende, S(VP.DimensaoMinima(130, 400)));
        Assert.Equal(SituacaoVerificacao.NaoAtende, S(VP.DimensaoMinima(150, 200)));   // 300 cm² < 360
        Assert.Equal(SituacaoVerificacao.Conferir, S(VP.PilarParede(200, 1100)));
    }

    [Fact]
    public void EstribosDoPilar()
    {
        // CA-50: 12φ; CA-25: 24φ (18.4.3)
        Assert.Equal(SituacaoVerificacao.NaoAtende, S(VP.EspacamentoEstribos(160, 400, 12.5, "CA-50")));   // 12 × 12,5 = 150
        Assert.Equal(SituacaoVerificacao.Ok, S(VP.EspacamentoEstribos(160, 400, 12.5, "CA-25")));          // 24 × 12,5 = 300 → 200
        Assert.Equal(SituacaoVerificacao.Ok, S(VP.BitolaEstribo(6.3, 25)));
        Assert.Equal(SituacaoVerificacao.NaoAtende, S(VP.BitolaEstribo(5, 25, 200)));   // φ/4 = 6,25 e s > 90000·25/(25·500) = 180
        var alt = VP.BitolaEstribo(5, 25, 150);                                          // s ≤ 180: admitido, com aviso
        Assert.Equal("P12", alt.Codigo);
        Assert.Equal(SituacaoVerificacao.Conferir, alt.Situacao);
        Assert.Equal(SituacaoVerificacao.Conferir, S(VP.ConcretoAltaResistencia(60)));
    }

    [Fact]
    public void EspacamentosDasLongitudinais()
    {
        Assert.Equal(SituacaoVerificacao.NaoAtende, S(VP.EspacamentoLivre(22, 20, 19)));   // 1,2 × 19 = 22,8
        Assert.Equal(SituacaoVerificacao.Ok, S(VP.EspacamentoLivre(23, 20, 19)));
        Assert.Equal(SituacaoVerificacao.NaoAtende, S(VP.EspacamentoEixos(410, 400)));     // ≤ 400
        Assert.Equal(SituacaoVerificacao.NaoAtende, S(VP.EspacamentoEixos(390, 190)));     // ≤ 2 × 190
    }

    [Fact]
    public void ListaDaASituacaoGeralESoAvisaFalhas()
    {
        var l = new ListaVerificacoes();
        l.Add(VP.TaxaMaxima(1, 100), true);              // OK: não entra
        Assert.Empty(l);
        l.Add(VP.PilarParede(200, 1100), true);
        l.Add(VP.DimensaoMinima(130, 400), true);
        Assert.Equal(SituacaoVerificacao.NaoAtende, l.Geral);
        Assert.Equal(2, l.Avisos().Count());
        Assert.StartsWith("Não atende (P1)", l.Avisos().Last());
    }
}

/// <summary>Suspensão do dente Gerber concentrada no trecho cobrimento + dvig/4 (D36, NBR 9062 7.4.6.2).</summary>
public class SuspensaoDenteTests
{
    [Theory]
    [InlineData(2.5, 46.07, 14)]    // 2,5 + 11,52 = 14,02 → 14
    [InlineData(3.0, 72.0, 21)]     // 3 + 18 = 21 (exato fica)
    [InlineData(2.5, 70.0, 20)]     // 2,5 + 17,5 = 20
    public void TrechoArredondadoParaBaixo(double cob, double dvig, double esperado) =>
        Assert.Equal(esperado, DEB.Core.Elementos.Viga.PreMoldada.TemplateVigaPreMoldada.TrechoSuspensao(cob, dvig), 6);

    [Fact]
    public void EstribosNoTrechoComOMesmoEspacamentoAteOEstriboDaAlma()
    {
        var t = new DEB.Core.Elementos.Viga.PreMoldada.TipoDente { Comprimento = 25, SuspQtd = 3, SuspBitola = 10 };
        var xs = DEB.Core.Elementos.Viga.PreMoldada.TemplateVigaPreMoldada.PosicoesSuspensao(t, 2.5, true, 500, 20);
        Assert.Equal(3, xs.Count);
        Assert.Equal(28, xs[0], 6);                                   // face 25 + cobrimento 2,5 + φ/2
        double e = xs[1] - xs[0];
        Assert.Equal(e, xs[2] - xs[1], 6);
        Assert.Equal(25 + 20, xs[2] + e, 6);                          // o próximo passo cai no 1º estribo da alma, no fim do trecho
    }
}
