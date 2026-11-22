using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Elementos.Viga.PreMoldada;
using DEB.Core.Elementos.Viga.PreMoldada.Calculo;
using DEB.Core.Motor;
using DEB.Core.Normas.Verificacao;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Cálculo da viga pré-moldada biapoiada de concreto armado (D40): esforços, flexão, cisalhamento, ELS e a proposta.</summary>
[Collection("Pilar")]
public class VigaCalculoTests
{
    private static ValoresTemplate Viga(Action<ValoresTemplate> ajustar = null)
    {
        var v = new ValoresTemplate(new TemplateVigaPreMoldada().Parametros);
        v["B"] = "20"; v["H"] = "60"; v["L"] = "600"; v["ExtE"] = "Reta"; v["ExtD"] = "Reta"; v["Fck"] = "40";
        ajustar?.Invoke(v);
        return v;
    }

    private static CargasViga Cargas(double g = 10, double q = 5) => new CargasViga { G = g, Q = q, ApoioE = 15, ApoioD = 15 };

    [Fact]
    public void EstaticaBiapoiadaComBalancos()
    {
        // 600 cm, apoios a 15 cm das pontas, 0,252 kN/cm: R = 75,6 kN; M no meio = 75,6·285 − 0,252·300²/2
        var e = new EstaticaViga(600, 15, 585, 0.252, new[] { (300.0, 10.0) });
        Assert.Equal(75.6 + 5, e.Ra, 6);
        Assert.Equal(e.Ra, e.Rb, 6);
        Assert.Equal(80.6 * 285 - 0.252 * 300 * 300 / 2, e.M(300), 3);
        Assert.Equal(80.6 - 0.252 * 15, e.VApoioE, 6);
        // flecha de carga uniforme com EI constante ≈ 5wℓ⁴/384EI (os balanços curtos aliviam um pouco)
        var u = new EstaticaViga(600, 0, 600, 0.1);
        Assert.Equal(5 * 0.1 * Math.Pow(600, 4) / (384 * 1e9), u.FlechaMeio(1e9), 4);
    }

    [Fact]
    public void FlexaoConferidaAMao()
    {
        // fck 40, b = 20, d = 56,07, Md = 102,06 kN·m → x = 4,85 cm; As = 4,34 cm²
        var f = new Flexao(40, 40 / 1.4 / 10, 500 / 1.15 / 10);
        var (As, comp, x, xd) = f.Armadura(10206, 20, 56.07, 4);
        Assert.InRange(x, 4.80, 4.90);
        Assert.InRange(As, 4.30, 4.38);
        Assert.Equal(0, comp);
        // além do limite x/d = 0,45 aparece armadura de compressão
        var dupla = f.Armadura(60000, 20, 56.07, 4);
        Assert.True(dupla.AsCompr > 0);
        Assert.Equal(0.45, dupla.XsobreD, 6);
    }

    [Fact]
    public void Viga20x60ProposiçãoEMemoria()
    {
        var r = CalculoViga.Calcular(Viga(), Cargas());
        // g1 = 0,2·0,6·25 = 3 kN/m; pd = 1,4·13 + 1,4·5 = 25,2 kN/m
        Assert.Equal(3.0, r.G1, 3);
        Assert.Equal(102.06, r.MdMax, 1);
        Assert.Equal(300, r.XMdMax, 0);
        Assert.Equal(71.82, r.VdE, 2);
        Assert.NotNull(r.Inferior);
        Assert.True(r.Inferior.Area >= r.AsNec && r.AsNec > 4.2 && r.AsNec < 4.5, $"As,nec = {r.AsNec}");
        Assert.Equal("4 ø12,5", r.Inferior.Texto);
        // Vd < Vc: só a armadura mínima; o padrão do escritório (c/10 nas pontas e c/15 no vão, ø6,3) é o mínimo
        Assert.True(r.Vc > r.VdE);
        Assert.Equal(6.3, r.EstBitola);
        Assert.Equal(10, r.EspPonta);
        Assert.Equal(15, r.EspVao);
        Assert.Equal(100, r.ZonaPonta);
        Assert.DoesNotContain(r.Verificacoes, x => x.Situacao == SituacaoVerificacao.NaoAtende);
        Assert.True(r.Memoria.Count >= 6);
        Assert.Contains(r.Memoria.SelectMany(m => m.Linhas), l => l.Contains("Md,máx = 102,1 kN·m"));
        // proposta nos valores do template: a inferior trocada, o resto do padrão mantido
        var linhas = r.Proposta.Tabela("Longitudinais");
        Assert.Contains(linhas, l => l[0] == "Inferior" && l[1] == "4" && l[2] == "12.5");
        Assert.Contains(linhas, l => l[0] == "Superior");
        Assert.Equal(1, linhas.Count(l => l[0] == "Inferior"));
    }

    [Fact]
    public void CargaAltaPedeEstribosMaisFechadosNasPontas()
    {
        var r = CalculoViga.Calcular(Viga(v => v["H"] = "50"), Cargas(25, 12));
        Assert.True(r.VdE > r.Vc);
        Assert.True(r.EspPonta <= 10 + 1e-9);
        Assert.True(r.ZonaPonta >= 100);
        Assert.True(r.Inferior.Area >= r.AsNec);
        // o espaçamento da ponta resolve a cortante do apoio
        Assert.True(2 * Math.PI * r.EstBitola * r.EstBitola / 400 / r.EspPonta + 1e-9 >= (r.VdE - r.Vc) / (0.9 * r.Inferior.D * 43.48));
    }

    [Fact]
    public void SecaoInsuficienteAvisa()
    {
        var r = CalculoViga.Calcular(Viga(v => { v["H"] = "30"; v["L"] = "900"; }), Cargas(80, 40));
        Assert.Contains(r.Verificacoes, x => x.Situacao == SituacaoVerificacao.NaoAtende || x.Situacao == SituacaoVerificacao.Conferir);
    }

    [Fact]
    public void PeleEmVigaAlta()
    {
        var r = CalculoViga.Calcular(Viga(v => { v["H"] = "90"; v["B"] = "25"; v["L"] = "1000"; }), Cargas(20, 10));
        Assert.True(r.Pele.NPorFace >= 3);
        Assert.True(r.Pele.NPorFace * Math.PI * r.Pele.Bitola * r.Pele.Bitola / 400 >= 0.001 * 25 * 90 - 1e-9);
        Assert.Contains(r.Proposta.Tabela("Longitudinais"), l => l[0] == "Pele");
    }

    [Fact]
    public void DenteGerberUsaAReacao()
    {
        var r = CalculoViga.Calcular(Viga(v => { v["H"] = "75"; v["L"] = "700"; v["ExtE"] = "DG 45x25"; v["ExtD"] = "DG 45x25"; }), new CargasViga { G = 25, Q = 10 });
        Assert.Contains(r.Verificacoes, x => x.Codigo == "K9");
        Assert.Contains(r.Verificacoes, x => x.Codigo == "K10");
        // apoio automático no pino (11,5 cm da ponta)
        Assert.Equal(11.5, r.ApoioE, 6);
    }

    [Fact]
    public void ProposicaoGeraODetalhamentoSemConferirDeVd()
    {
        var r = CalculoViga.Calcular(Viga(v => v["EstEspVao"] = "20"), Cargas());
        var t = new TemplateVigaPreMoldada();
        var sem = t.Gerar(Viga(v => v["EstEspVao"] = "20"), new ConfiguracaoArmadura());
        var com = t.Gerar(r.Proposta, new ConfiguracaoArmadura());
        Assert.Contains(com.Barras, b => b.Nome == "long1" && b.Quantidade == 4 && Math.Abs(b.Bitola - 12.5) < 1e-6);
        // sem cálculo, V6/V7 do vão são "Conferir" (limite mais rigoroso); com o Vd conhecido, decididos
        Assert.Contains(sem.Verificacoes, x => (x.Codigo == "V6" || x.Codigo == "V7") && x.Situacao == SituacaoVerificacao.Conferir);
        Assert.DoesNotContain(com.Verificacoes, x => (x.Codigo == "V6" || x.Codigo == "V7") && x.Situacao == SituacaoVerificacao.Conferir);
    }

    [Fact]
    public void ConcentradasPorTexto()
    {
        var erros = new List<string>();
        var l = CargasViga.LerConcentradas("250: 30 + 12; 480,5: 18; lixo", erros);
        Assert.Equal(2, l.Count);
        Assert.Equal(480.5, l[1].X, 6);
        Assert.Equal(0, l[1].Q);
        Assert.Single(erros);
        Assert.Equal("250: 30 + 12; 480,5: 18", CargasViga.TextoConcentradas(l));
        var env = CargasViga.Envoltoria(new[] { new CargasViga { G = 5, Q = 2 }, new CargasViga { G = 3, Q = 4 } });
        Assert.Equal(5, env.G); Assert.Equal(4, env.Q);
    }
}
