using DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D;
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
        // para a janela (23/11): MRd da proposta cobre o Md; VRd3 das pontas cobre o Vd; aℓ entre 0,5d e d; zona das pontas antes do meio
        Assert.True(r.MRd >= r.MdMax - 1e-6 && r.MRd < 1.6 * r.MdMax, $"MRd {r.MRd}");
        Assert.True(r.VRd3Ponta >= r.VdE - 1e-6 && r.VRd3Vao <= r.VRd3Ponta + 1e-6);
        Assert.InRange(r.Al, 0.5 * r.Inferior.D - 1e-6, r.Inferior.D + 1e-6);
        Assert.True(r.XZonaE <= r.L / 2 && r.XZonaD >= r.L / 2);
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
        // dente com o Fd conhecido: D7 decidido
        var vd = Viga(v => { v["ExtE"] = "DG 45x25"; v["H"] = "75"; });
        var rd = CalculoViga.Calcular(vd, Cargas());
        Assert.Contains(t.Gerar(rd.Proposta, new ConfiguracaoArmadura()).Verificacoes, x => x.Codigo == "D7" && x.Situacao == SituacaoVerificacao.Ok);
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

/// <summary>Barras 3D da viga para o Revit (D40): as mesmas quantidades e posições do detalhamento do CAD.</summary>
[Collection("Pilar")]
public class VigaRebarLayoutTests
{
    private static double Cm(double ft) => ft * 30.48;

    [Fact]
    public void ParidadeComOTemplate()
    {
        var v = new ValoresTemplate(new TemplateVigaPreMoldada().Parametros);
        v["B"] = "20"; v["H"] = "75"; v["L"] = "700"; v["ExtE"] = "DG 45x25"; v["ExtD"] = "Reta";
        var cfg = new ConfiguracaoArmadura();
        var r = new TemplateVigaPreMoldada().Gerar(v, cfg);
        var avisos = new List<string>();
        var bars = DEB.Core.Elementos.Viga.PreMoldada.Modelo3D.VigaRebarLayout.Compute(v, cfg, 5, 0, avisos);
        var lix = new List<string>();
        var e = TemplateVigaPreMoldada.LerExtremidade(v, true, TemplateVigaPreMoldada.CatalogoDentes, lix);
        var d = TemplateVigaPreMoldada.LerExtremidade(v, false, TemplateVigaPreMoldada.CatalogoDentes, lix);
        int nEst = TemplateVigaPreMoldada.ZonasEstribos(v, cfg, 700, e, d).Sum(z => z.Posicoes.Count);
        Assert.Equal(nEst, bars.Where(b => b.Role == DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D.BarRole.Stirrup).Sum(b => b.Count));
        // inferior: 3 ø16 do padrão
        var inf = bars.Where(b => b.Role == DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D.BarRole.Longitudinal && Math.Abs(b.DiameterMm - 16) < 1e-6).ToList();
        Assert.Equal(r.Barras.Single(b => b.Nome == "long1").Quantidade, inf.Count);
        // tudo dentro da alma (y de 5 a 25 cm), menos as alças, que saem pelo topo
        foreach (var b in bars.Where(b => b.Role != DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D.BarRole.LiftLoop))
            foreach (var p in b.Points)
            {
                Assert.InRange(Cm(p.B), 5 + 2.5 - 1e-6, 25 - 2.5 + 1e-6);
                Assert.InRange(Cm(p.C), 2.5 - 1e-6, 75 - 2.5 + 1e-6);
                Assert.InRange(Cm(p.A), -1e-6, 700 + 1e-6);
            }
        // dente só na ponta esquerda: suspensão e estribos do dente, tirante
        Assert.Contains(bars, b => b.Role == DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D.BarRole.BeamHanger);
        Assert.Contains(bars, b => b.Role == DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D.BarRole.BeamTie && b.Points.All(p => Cm(p.A) < 350));
        Assert.Equal(4, bars.Count(b => b.Role == DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D.BarRole.LiftLoop));   // 2 alças × 2 cordoalhas
        // girada 180°: o dente vai para a ponta direita
        var g = DEB.Core.Elementos.Viga.PreMoldada.Modelo3D.VigaRebarLayout.Girar180(bars, 700 / 30.48, 30 / 30.48);
        Assert.Contains(g, b => b.Role == DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D.BarRole.BeamTie && b.Points.All(p => Cm(p.A) > 350));
    }
}

/// <summary>Aba e ressalto no Revit (D40): as barras saem das funções da seção do CAD e ficam dentro do concreto.</summary>
public class VigaAbaRessaltoLayoutTests
{
    private static double Cm(double ft) => ft * 30.48;
    private const double Y0 = 7;

    private static ValoresTemplate Viga(Action<ValoresTemplate> ajustar)
    {
        var v = new ValoresTemplate(new TemplateVigaPreMoldada().Parametros);
        v["B"] = "20"; v["H"] = "60"; v["L"] = "600"; v["ExtE"] = "DG 45x25"; v["ExtD"] = "DG 45x25";
        ajustar(v);
        return v;
    }

    /// <summary>Distância (com sinal: + dentro) do ponto ao contorno do polígono.</summary>
    private static double Folga(List<(double X, double Y)> pol, double x, double y)
    {
        bool dentro = false; double dmin = double.MaxValue;
        for (int i = 0, j = pol.Count - 1; i < pol.Count; j = i++)
        {
            var (xi, yi) = pol[i]; var (xj, yj) = pol[j];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) dentro = !dentro;
            double dx = xj - xi, dy = yj - yi, t = Math.Max(0, Math.Min(1, ((x - xi) * dx + (y - yi) * dy) / (dx * dx + dy * dy)));
            dmin = Math.Min(dmin, Math.Sqrt(Math.Pow(x - xi - t * dx, 2) + Math.Pow(y - yi - t * dy, 2)));
        }
        return dentro ? dmin : -dmin;
    }

    /// <summary>Todo ponto de barra (menos a alça e a perna da ligação que sai para a capa) a pelo menos 1 cm dentro da seção em x.</summary>
    private static void TudoDentro(ValoresTemplate v, List<BarDef> bars)
    {
        double b = v.Numero("B"), h = v.Numero("H"), L = v.Numero("L");
        var lix = new List<string>();
        var e = TemplateVigaPreMoldada.LerExtremidade(v, true, TemplateVigaPreMoldada.CatalogoDentes, lix);
        var d = TemplateVigaPreMoldada.LerExtremidade(v, false, TemplateVigaPreMoldada.CatalogoDentes, lix);
        var aba = TemplateVigaPreMoldada.LerAba(v, h, L, e, d, null);
        var rs = TemplateVigaPreMoldada.LerRessalto(v, b, h, null);
        foreach (var bd in bars.Where(x => x.Role != BarRole.LiftLoop))
            foreach (var inst in bd.Instances())
                foreach (var p in bd.Role == BarRole.BeamTopping ? inst.Take(2) : inst)
                {
                    double x = Cm(p.A), zb = e.TemDente && x < e.CompDente ? h - e.Dente.Altura : d.TemDente && x > L - d.CompDente ? h - d.Dente.Altura : 0;
                    AbaViga ab = aba.Em(x) ? (zb > 0 ? aba.NoDente(zb) : aba) : null;
                    var pol = TemplateVigaPreMoldada.ContornoSecao(b, h - zb, ab, rs.Existe ? rs : null, 0, 0);
                    double f = Folga(pol, Y0 + b - Cm(p.B), Cm(p.C) - zb);
                    Assert.True(f >= 1, $"{bd.Role} fora do concreto: x={x:0.#} y={Cm(p.B):0.#} z={Cm(p.C):0.#} (folga {f:0.##} cm)");
                }
    }

    [Fact]
    public void AbaEmLComGrampos()
    {
        var v = Viga(v => { v["Aba"] = TemplateVigaPreMoldada.AbaDir; v["AbaLargura"] = "15"; v["AbaAltura"] = "20"; });
        var cfg = new ConfiguracaoArmadura();
        var avisos = new List<string>();
        var bars = DEB.Core.Elementos.Viga.PreMoldada.Modelo3D.VigaRebarLayout.Compute(v, cfg, Y0, 0, avisos);
        TudoDentro(v, bars);
        var lix = new List<string>();
        var e = TemplateVigaPreMoldada.LerExtremidade(v, true, TemplateVigaPreMoldada.CatalogoDentes, lix);
        var d = TemplateVigaPreMoldada.LerExtremidade(v, false, TemplateVigaPreMoldada.CatalogoDentes, lix);
        var aba = TemplateVigaPreMoldada.LerAba(v, 60, 600, e, d, null);
        // padrão (23/11): estribo próprio da aba, fechado — dois estribos na seção em L
        Assert.Equal(TemplateVigaPreMoldada.PosicoesEstribosAba(v, cfg, aba).Count, bars.Where(b => b.Role == BarRole.BeamLedge && b.Closed).Sum(b => b.Count));
        // grampo (padrão anterior do escritório): a mesma forma aberta
        v["AbaEstForma"] = TemplateVigaPreMoldada.AbaGrampo;
        var g = DEB.Core.Elementos.Viga.PreMoldada.Modelo3D.VigaRebarLayout.Compute(v, cfg, Y0, 0);
        TudoDentro(v, g);
        Assert.Equal(TemplateVigaPreMoldada.PosicoesEstribosAba(v, cfg, aba).Count, g.Where(b => b.Role == BarRole.BeamLedgeHairpin && !b.Closed).Sum(b => b.Count));
        v["AbaEstForma"] = TemplateVigaPreMoldada.AbaEstribo;
        Assert.Equal(2, bars.Count(b => b.Role == BarRole.BeamLedgeBar));
        // aba direita = y menor: a ponta dos grampos e as longitudinais da aba ficam abaixo de y0
        Assert.All(bars.Where(b => b.Role == BarRole.BeamLedgeBar), b => Assert.True(Cm(b.Points[0].B) < Y0));
        Assert.DoesNotContain(avisos, a => a.Contains("ainda não é criada"));
    }

    [Fact]
    public void TInvertidoComEstriboFechadoERetas()
    {
        var v = Viga(v =>
        {
            v["ExtE"] = "Reta"; v["ExtD"] = "Reta"; v["Aba"] = TemplateVigaPreMoldada.AbaDois; v["AbaLargura"] = "12"; v["AbaAltura"] = "20";
            v["AbaAlturaPonta"] = "14"; v["AbaEstForma"] = TemplateVigaPreMoldada.AbaFechado;
        });
        var bars = DEB.Core.Elementos.Viga.PreMoldada.Modelo3D.VigaRebarLayout.Compute(v, new ConfiguracaoArmadura(), Y0, 0);
        TudoDentro(v, bars);
        Assert.Contains(bars, b => b.Role == BarRole.BeamLedge && b.Closed);
        Assert.Equal(4, bars.Count(b => b.Role == BarRole.BeamLedgeBar));
        Assert.Contains(bars, b => b.Role == BarRole.BeamLedgeBar && Cm(b.Points[0].B) > Y0 + 20);
        Assert.Contains(bars, b => b.Role == BarRole.BeamLedgeBar && Cm(b.Points[0].B) < Y0);
    }

    [Fact]
    public void RessaltoComAbaEDentes()
    {
        var v = Viga(v =>
        {
            v["B"] = "30"; v["H"] = "50"; v["Aba"] = TemplateVigaPreMoldada.AbaDir; v["AbaLargura"] = "15"; v["AbaAltura"] = "20";
            v["Ressalto"] = TemplateVigaPreMoldada.RessaltoEsq; v["RessaltoLargura"] = "13"; v["RessaltoAltura"] = "27.5";
        });
        var avisos = new List<string>();
        var bars = DEB.Core.Elementos.Viga.PreMoldada.Modelo3D.VigaRebarLayout.Compute(v, new ConfiguracaoArmadura(), Y0, 0, avisos);
        TudoDentro(v, bars);
        Assert.Contains(bars, b => b.Role == BarRole.BeamUpstand);
        Assert.Equal(4, bars.Count(b => b.Role == BarRole.BeamUpstandBar));
        // ressalto à esquerda (y maior); a ligação sai pela face de dentro, para o lado da laje (y menor), 70 cm
        var lig = bars.First(b => b.Role == BarRole.BeamTopping);
        Assert.True(Cm(lig.Points[0].B) > Y0 + 30 - 13);
        Assert.InRange(Cm(lig.Points[2].B), Y0 + 30 - 13 - 70 - 0.1, Y0 + 30 - 13 - 70 + 0.1);
        // no dente o estribo do ressalto para no fundo do dente (há estribos de duas alturas)
        Assert.True(bars.Where(b => b.Role == BarRole.BeamUpstand).Select(b => Math.Round(Cm(b.Points[0].C), 1)).Distinct().Count() >= 2);
        string svg = DEB.Core.Elementos.Viga.PreMoldada.Modelo3D.DesenhoArmacaoSvg.Secao(v, bars, 300, Y0, 0, "MEIO");
        Assert.Contains("#0e8a8a", svg);
    }
}

/// <summary>Relatório de cálculo da viga (D37 + D40): memória, diagramas e o desenho da armação proposta.</summary>
[Collection("Pilar")]
public class VigaRelatorioCalculoTests
{
    [Fact]
    public void RelatorioComMemoriaDiagramasEDesenho()
    {
        var v = new ValoresTemplate(new TemplateVigaPreMoldada().Parametros);
        v["Nome"] = "V1"; v["B"] = "20"; v["H"] = "60"; v["L"] = "650"; v["ExtE"] = "DG 45x25"; v["ExtD"] = "DG 45x25";
        var calc = CalculoViga.Calcular(v, new CargasViga { G = 15, Q = 8, Concentradas = { new CargaConcentrada { X = 325, G = 20, Q = 10 } } });
        var r = new TemplateVigaPreMoldada().Gerar(calc.Proposta, new ConfiguracaoArmadura());
        var rel = DEB.Core.Documentacao.RelatorioHtml.DoTemplate(r, "V1", "teste");
        calc.PreencherRelatorio(rel);
        var bars = DEB.Core.Elementos.Viga.PreMoldada.Modelo3D.VigaRebarLayout.Compute(calc.Proposta, new ConfiguracaoArmadura(), 0, 0);
        foreach (var (t, s) in DEB.Core.Elementos.Viga.PreMoldada.Modelo3D.DesenhoArmacaoSvg.Figuras(calc.Proposta, bars, 0, 0))
            rel.Figuras.Add(new KeyValuePair<string, string>(t, s));
        string html = DEB.Core.Documentacao.RelatorioHtml.Gerar(rel);
        Assert.Contains("Cálculo e verificação — V1", html);
        Assert.Contains("Memória de cálculo", html);
        Assert.Contains("Armação proposta", html);
        Assert.Contains("<svg class=\"diag\"", html);
        Assert.Contains("<svg class=\"arm\"", html);
        Assert.Contains("K1", html);
        string saida = Environment.GetEnvironmentVariable("ARM_SAIDA_RELATORIO");
        if (!string.IsNullOrEmpty(saida)) System.IO.File.WriteAllText(saida, html, new System.Text.UTF8Encoding(true));
    }
}
