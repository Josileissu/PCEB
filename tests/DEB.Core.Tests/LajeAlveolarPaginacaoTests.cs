using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Lajes;
using DEB.Core.Persistencia;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>
/// Paginação de lajes alveolares (2026-11-12): casos sintéticos no sistema do pano (u = vão, v = transversal), metros.
/// Pano padrão: 6,00 m de vão entre eixos de vigas em T invertido (alma 20 cm, aba até 25 cm do eixo, dos dois lados).
/// </summary>
public class LajeAlveolarPaginacaoTests
{
    private const double Tol = 1e-6;

    private static SecaoViga Secao(double meiaAlma, double meiaAba, bool comAba = true)
    {
        var cfg = new ConfigPaginacao();
        var s = new SecaoViga();
        s.Linhas.Add(new LinhaSecao { Z = cfg.LeituraViga.DzAlma, Trechos = { (-meiaAlma, meiaAlma) } });
        s.Linhas.Add(new LinhaSecao { Z = cfg.LeituraViga.DzAba, Trechos = { comAba ? (-meiaAba, meiaAba) : (-meiaAlma, meiaAlma) } });
        return s;
    }

    private static List<VigaTransversal> Apoios(double vMax, bool comAba = true) => new List<VigaTransversal>
    {
        new VigaTransversal { Id = "V1", Tipo = "T", UEixo = 0, Va = -1, Vb = vMax + 1, NU = 1, Secao = Secao(0.10, 0.25, comAba) },
        new VigaTransversal { Id = "V2", Tipo = "T", UEixo = 6, Va = -1, Vb = vMax + 1, NU = 1, Secao = Secao(0.10, 0.25, comAba) },
    };

    private static List<PontoUV> Ret(double u0, double v0, double u1, double v1)
        => new List<PontoUV> { new PontoUV(u0, v0), new PontoUV(u1, v0), new PontoUV(u1, v1), new PontoUV(u0, v1) };

    private static ResultadoPano Paginar(double vMax, ConfigPaginacao cfg = null, List<PontoUV> furo = null, List<PilarPano> pilares = null,
                                         List<VigaLongitudinal> vigasU = null, List<PontoUV> contorno = null, bool comAba = true)
    {
        var loops = new List<IReadOnlyList<PontoUV>> { contorno ?? Ret(0, 0, 6, vMax) };
        if (furo != null) loops.Add(furo);
        return MotorPaginacao.PaginarPano(loops, Apoios(vMax, comAba), pilares ?? new List<PilarPano>(), cfg ?? new ConfigPaginacao(), vigasU);
    }

    private static List<(double V0, double V1)> Larguras(ResultadoPano r) => r.Paineis.OrderBy(p => p.V0).Select(p => (p.V0, p.V1)).ToList();

    [Fact]
    public void PanoRetangularSimples()
    {
        var r = Paginar(5.6);
        var l = Larguras(r);
        Assert.Equal(5, l.Count);
        for (int i = 0; i < 4; i++) { Assert.Equal(i * 1.25, l[i].V0, 6); Assert.Equal(i * 1.25 + 1.245, l[i].V1, 6); }
        Assert.Equal(5.0, l[4].V0, 6); Assert.Equal(5.6, l[4].V1, 6);           // ajuste de 60 cm junto à borda v máx.
        Assert.Equal(4, r.Paineis.Count(p => p.Tipo == "padrao"));
        Assert.Equal("ajuste", r.Paineis.OrderBy(p => p.V0).Last().Tipo);
        // comprimento = face da alma − 1,5 cm em cada ponta; apoio = aba − alma − folga
        foreach (var p in r.Paineis)
        {
            Assert.Equal(0.115, p.U0, 6); Assert.Equal(5.885, p.U1, 6);
            Assert.Equal(0.135, p.Ini.Apoio.Value, 6); Assert.Null(p.Ini.Alerta); Assert.Null(p.Fim.Alerta);
            Assert.Single(p.Pecas);
        }
        Assert.Empty(r.FaixasMacicas);
        Assert.Equal(4, r.Juntas.Count);
        // tirantes: malha de 1,25 alinhada no meio do pano, margem de 30 cm → 4 por junta
        Assert.All(r.Juntas, j => Assert.Equal(new[] { 1.125, 2.375, 3.625, 4.875 }, j.Tirantes.Select(u => Math.Round(u, 6)).ToArray()));
        Assert.Equal(16, r.Tirantes.Count);
    }

    [Fact]
    public void SuportesDesligadosNaoGeramTirantes()
    {
        var r = Paginar(5.6, new ConfigPaginacao { SuportesInstalacoes = false });
        Assert.Empty(r.Tirantes);
        Assert.All(r.Juntas, j => Assert.Empty(j.Tirantes));
    }

    [Fact]
    public void AberturaCentralTemJuntaNoMeioEDoisPaineisRecortados()
    {
        var r = Paginar(5.6, furo: Ret(2, 2.0, 3, 2.6));
        var ps = r.Paineis.OrderBy(p => p.V0).ToList();
        Assert.Equal(5, ps.Count);
        var ab = ps.Where(p => p.Tipo == "abertura").ToList();
        Assert.Equal(2, ab.Count);
        Assert.Equal(2.3 - 0.0025 - 1.245, ab[0].V0, 6); Assert.Equal(2.2975, ab[0].V1, 6);   // junta centrada na abertura
        Assert.Equal(2.3025, ab[1].V0, 6); Assert.Equal(3.5475, ab[1].V1, 6);
        Assert.All(ab, p => Assert.Contains(p.Recortes, rc => rc.Tipo == "abertura"));
        Assert.Equal(2.3, r.Geo.Aberturas.Single().VCorte.Value, 6);
        // abaixo: um ajuste até a borda; acima: painel inteiro + ajuste junto à borda v máx.
        Assert.Equal((0.0, 1.0475), (Math.Round(ps[0].V0, 6), Math.Round(ps[0].V1, 6)));
        Assert.Equal("ajuste", ps[0].Tipo);
        Assert.Equal(3.5525, ps[3].V0, 6); Assert.Equal("padrao", ps[3].Tipo);
        Assert.Equal(4.8025, ps[4].V0, 6); Assert.Equal(5.6, ps[4].V1, 6);
        // a junta do meio é interrompida pela abertura (dois trechos)
        var meio = r.Juntas.Where(j => Math.Abs(j.V - 2.3) < 1e-6).ToList();
        Assert.Equal(2, meio.Count);
        Assert.Equal(2.0, meio.Min(j => j.U1), 6); Assert.Equal(3.0, meio.Max(j => j.U0), 6);
    }

    [Fact]
    public void AberturaJuntoABordaGeraFaixaMacicaEConsoloNaViga()
    {
        var vu = new List<VigaLongitudinal>
        {
            new VigaLongitudinal { Id = "V9", Tipo = "R", VEixo = -0.10, Ua = -1, Ub = 7, NV = 1, Secao = Secao(0.10, 0.10, false) },
        };
        var r = Paginar(5.6, furo: Ret(2, 0.05, 3, 0.45), vigasU: vu);
        var m = Assert.Single(r.FaixasMacicas);
        Assert.Equal(0, m.Ret.V0, 6); Assert.Equal(0.2475, m.Ret.V1, 6);         // painel cortado pela borda com 24,75 < 30
        Assert.Equal(0, m.Ret.U0, 6); Assert.Equal(6, m.Ret.U1, 6);
        Assert.Contains(m.Alertas, a => a.Contains("APOIO EM CONSOLO"));
        var c = Assert.Single(r.Consolos);
        Assert.Equal("V9", c.Viga);
        Assert.Contains(r.Alertas, a => a.Contains("FAIXA MACIÇA"));
        Assert.Contains(r.Paineis, p => p.Tipo == "abertura" && Math.Abs(p.V0 - 0.2525) < 1e-6);
    }

    [Fact]
    public void PilarNoCantoViraRecorte()
    {
        var contorno = new List<PontoUV> { new(0.25, 0), new(6, 0), new(6, 5.6), new(0, 5.6), new(0, 0.25), new(0.25, 0.25) };
        var pil = new List<PilarPano> { new PilarPano { Id = "P1", Ret = new RetUV(-0.15, -0.15, 0.25, 0.25) } };
        var r = Paginar(5.6, pilares: pil, contorno: contorno);
        Assert.Empty(r.Geo.Aberturas);
        var p0 = r.Paineis.OrderBy(p => p.V0).First();
        var rc = Assert.Single(p0.Recortes);
        Assert.Equal("pilar", rc.Tipo); Assert.Equal("P1", rc.Ref);
        Assert.Equal(0.115, rc.Ret.U0, 6); Assert.Equal(0.265, rc.Ret.U1, 6);   // pilar + folga de 1,5 cm
        Assert.Equal(0, rc.Ret.V0, 6); Assert.Equal(0.265, rc.Ret.V1, 6);
        Assert.Single(p0.Pecas);
        // os outros painéis não tocam o pilar
        Assert.All(r.Paineis.Where(p => p != p0), p => Assert.Empty(p.Recortes));
    }

    [Fact]
    public void RuidoNasCoordenadasDoModeloNaoTiraOCorpoDoPano()
    {
        // teste no Revit (modelo real): contorno em L com entalhes de pilar e coordenadas com ruído de 1e-9 (pés → metros). O limite
        // exato e o arredondado viravam duas linhas da grade; a célula fininha no contorno ligava o vazio ao corpo do pano, que saía
        // inteiro como "fora do contorno" (0 peças).
        var rnd = new Random(1);
        PontoUV P(double u, double v) => new PontoUV(u + (rnd.NextDouble() - 0.5) * 2e-8, v + (rnd.NextDouble() - 0.5) * 2e-8);
        var contorno = new List<PontoUV> { P(0, 0.15), P(0.05, 0.15), P(0.05, 0), P(6, 0), P(6, 4.93), P(0.05, 4.93), P(0.05, 4.73), P(0, 4.73) };
        var pil = new List<PilarPano>
        {
            new PilarPano { Id = "P1", Ret = new RetUV(-0.36, -0.26, 0.04, 0.14) },
            new PilarPano { Id = "P2", Ret = new RetUV(-0.36, 4.74, 0.04, 5.14) },
        };
        var r = Paginar(4.93, pilares: pil, contorno: contorno);
        Assert.Empty(r.Geo.Fora);
        Assert.Equal(4, r.Pecas.Count());
        Assert.DoesNotContain(r.Alertas, a => a.Contains("0 peça"));
    }

    [Fact]
    public void SobraMenorQueAjusteMinimoViraDoisAjustes()
    {
        var r = Paginar(5.2);
        var l = Larguras(r);
        Assert.Equal(5, l.Count);
        double dois = (0.2 + 1.245) / 2;
        Assert.Equal(dois, l[3].V1 - l[3].V0, 6);
        Assert.Equal(dois, l[4].V1 - l[4].V0, 6);
        Assert.Equal(5.2, l[4].V1, 6);
        Assert.Equal(2, r.Paineis.Count(p => p.Tipo == "ajuste"));
        Assert.Contains(r.Alertas, a => a.Contains("dois ajustes"));
    }

    [Fact]
    public void VaoMenorQueAjusteSemComoRedistribuirViraFaixaMacica()
    {
        var r = Paginar(0.25);
        Assert.Empty(r.Paineis);
        var m = Assert.Single(r.FaixasMacicas);
        Assert.Equal(0.25, m.Ret.LarguraV, 6);
    }

    [Fact]
    public void EstrategiaMelhorDistribuicaoUsaDoisAjustesSimetricos()
    {
        var r = Paginar(5.5, new ConfigPaginacao { Estrategia = EstrategiaPaginacao.MelhorDistribuicao });
        var l = Larguras(r);
        double dois = (0.5 + 1.245) / 2;
        Assert.Equal(5, l.Count);
        Assert.Equal(0, l[0].V0, 6); Assert.Equal(dois, l[0].V1, 6);
        Assert.Equal(5.5 - dois, l[4].V0, 6); Assert.Equal(5.5, l[4].V1, 6);
        // aberturas primeiro: um ajuste de 50 cm no fim
        var a = Larguras(Paginar(5.5));
        Assert.Equal(0.5, a[4].V1 - a[4].V0, 6);
    }

    [Fact]
    public void EstrategiaDeUmLadoComecaNaBordaEscolhida()
    {
        var r = Paginar(5.5, new ConfigPaginacao { Estrategia = EstrategiaPaginacao.DeUmLado, BordaInicioVMin = false });
        var l = Larguras(r);
        Assert.Equal(5.5 - 1.245, l[4].V0, 6); Assert.Equal(5.5, l[4].V1, 6);
        Assert.Equal(0, l[0].V0, 6); Assert.Equal(0.5, l[0].V1, 6);              // ajuste no fim (v mínimo)
        // aberturas viram entalhes de quem estiver por cima
        var e = Paginar(5.6, new ConfigPaginacao { Estrategia = EstrategiaPaginacao.DeUmLado }, furo: Ret(2, 2.0, 3, 2.6));
        Assert.DoesNotContain(e.Paineis, p => p.Tipo.StartsWith("abertura"));
        Assert.Contains(e.Paineis, p => p.Tipo.EndsWith("+entalhe_abertura"));
        Assert.Null(e.Geo.Aberturas.Single().VCorte);
    }

    [Fact]
    public void VigaSemAbaDaAlertaDeSemApoio()
    {
        var r = Paginar(2.5, comAba: false);
        Assert.All(r.Paineis, p =>
        {
            Assert.Equal(0.115, p.U0, 6);                                       // mesma folga na face da alma
            Assert.StartsWith("SEM APOIO", p.Ini.Alerta);
            Assert.StartsWith("SEM APOIO", p.Fim.Alerta);
        });
    }

    [Fact]
    public void FuroAtravessandoOPainelDivideEmPecas()
    {
        // abertura de 2,50 m de largura em v: os dois painéis da abertura são atravessados (peças apoiadas só de um lado)
        var r = Paginar(5.6, furo: Ret(2.5, 1.5, 3.5, 4.0));
        var ab = r.Paineis.Where(p => p.Tipo.StartsWith("abertura")).ToList();
        Assert.Equal(2, ab.Count);
        Assert.All(ab, p => Assert.Equal(2, p.Pecas.Count));
        Assert.Contains(ab.SelectMany(p => p.Pecas), q => q.Fim.Alerta != null && q.Fim.Alerta.Contains("recorte"));
    }

    [Fact]
    public void SistemaDoPanoSegueOProtótipoEmXeY()
    {
        var sx = new SistemaPano(Math.PI);          // vão em X (180°)
        Assert.Equal(3.0, sx.UV(3, 4).U, 9); Assert.Equal(4.0, sx.UV(3, 4).V, 9);
        var sy = new SistemaPano(-Math.PI / 2);     // vão em Y
        Assert.Equal(4.0, sy.UV(3, 4).U, 9); Assert.Equal(3.0, sy.UV(3, 4).V, 9);
        var (x, y) = sy.XY(4, 3); Assert.Equal(3.0, x, 9); Assert.Equal(4.0, y, 9);
        var s30 = new SistemaPano(Math.PI / 6);
        var p = s30.UV(1.7, -2.3); var q = s30.XY(p.U, p.V);
        Assert.Equal(1.7, q.X, 9); Assert.Equal(-2.3, q.Y, 9);
    }

    [Fact]
    public void PaginacaoDaObraDaMarcasEJuntasEntrePecas()
    {
        SecaoViga sec = Secao(0.10, 0.25);
        var piso = new PisoPlanta
        {
            Id = "100", AnguloVao = Math.PI / 2, ZTopo = 3.0,                 // vão em Y: u = y, v = x
            Loops = { new List<(double, double)> { (10, 20), (12.6, 20), (12.6, 26), (10, 26) } },
            Vigas =
            {
                new VigaPlanta { Id = "1", Tipo = "T", P0 = (9, 20), P1 = (14, 20), Secao = sec },
                new VigaPlanta { Id = "2", Tipo = "T", P0 = (14, 26), P1 = (9, 26), Secao = sec },
            },
        };
        var cfg = new ConfigPaginacao();
        var r = PaginacaoObra.Paginar(new[] { piso }, cfg, numeroInicial: 7);
        var p = Assert.Single(r);
        Assert.Null(p.Erro);
        Assert.Equal(new[] { "L07-01", "L07-02", "L07-03" }, p.Pecas.Select(s => s.Marca).ToArray());
        Assert.Equal(2.925, p.ZTopoPainel, 6); Assert.Equal(2.765, p.ZBasePainel, 6);
        var j = p.Juntas.First();
        Assert.Equal("J07-01", j.Marca); Assert.Equal("L07-01", j.EntreA); Assert.Equal("L07-02", j.EntreB);
        Assert.Equal(20.115, p.Pecas[0].EixoIni.Y, 6); Assert.Equal(25.885, p.Pecas[0].EixoFim.Y, 6);
        Assert.StartsWith("J07-01-T1", p.Tirantes[0].Marca);
        Assert.Contains("TOTAL: 1 pano(s), 3 peça(s)", PaginacaoObra.Relatorio(r, cfg));
    }

    [Fact]
    public void ConfiguracaoGravaLeEPreservaChavesDesconhecidas()
    {
        Assert.Empty(new ConfigPaginacao().Validar());
        var o = Json.Ler("{\"estrategia\":\"de_um_lado\",\"borda_inicio\":\"v_max\",\"ajuste_min\":0.35,\"minha_chave\":42,\"tirante\":{\"diametro\":0.016}}");
        var c = ConfigPaginacao.DeJson(o);
        Assert.Equal(EstrategiaPaginacao.DeUmLado, c.Estrategia);
        Assert.False(c.BordaInicioVMin);
        Assert.Equal(0.35, c.AjusteMin);
        Assert.Equal(0.016, c.Tirante.Diametro);
        Assert.Equal(1.245, c.LarguraPainel);                                  // ausente = padrão
        c.Insercao.FamiliaPainel = "Painel"; c.Fase3.BarraNaoCabe = TratamentoBarraJunta.NaoInserir;
        var volta = ConfigPaginacao.DeJson(Json.Ler(Json.Gravar(c.ParaJson())));
        Assert.Equal(42, volta.ParaJson()["minha_chave"].Numero);
        Assert.Equal("Painel", volta.Insercao.FamiliaPainel);
        Assert.Equal(TratamentoBarraJunta.NaoInserir, volta.Fase3.BarraNaoCabe);
        Assert.Equal(0.35, volta.AjusteMin);
        var ruim = new ConfigPaginacao { Modulo = 1.30 };
        Assert.Contains(ruim.Validar(), e => e.Contains("módulo"));
        var etapas = new ConfigPaginacao(); etapas.Etapas.Capa = 1;
        Assert.Contains(etapas.Validar(), e => e.Contains("etapas"));
    }

    [Fact]
    public void BarraNaJuntaEstreitaPedeFuroEmObra()
    {
        var f = new ConfigFase3();
        Assert.Equal(TratamentoBarraJunta.FuroEmObra, f.BarraNaoCabe);
        var (ins, furo, txt) = f.AvaliarBarra(0.006, 0.0125);
        Assert.True(ins); Assert.True(furo);
        Assert.Contains("Furo Ø15,5 em obra", txt);
        Assert.Equal((true, false, (string)null), f.AvaliarBarra(0.02, 0.0125));
        f.BarraNaoCabe = TratamentoBarraJunta.NaoInserir;
        Assert.False(f.AvaliarBarra(0.006, 0.0125).Inserir);
        var furos = ConfigFase3.FurosPorPeca(new[] { ("L01-01", "L01-02"), ("L01-01", "L01-02"), ("L01-02", "L01-03") });
        Assert.Equal(2, furos["L01-01"]); Assert.Equal(3, furos["L01-02"]); Assert.Equal(1, furos["L01-03"]);
    }

    [Fact]
    public void CatalogoDePaineisAplicaMedidas()
    {
        var lista = CatalogoPaineis.Ler(Json.Ler("{\"paineis\":[{\"nome\":\"h20\",\"altura\":0.20,\"largura\":1.195,\"junta\":0.005}]}"));
        var p = Assert.Single(lista);
        var cfg = new ConfigPaginacao();
        CatalogoPaineis.Aplicar(p, cfg);
        Assert.Equal(1.20, cfg.Modulo, 9); Assert.Equal(0.20, cfg.AlturaPainel, 9);
        Assert.Empty(cfg.Validar());
        Assert.Throws<FormatException>(() => CatalogoPaineis.Ler(Json.Ler("{\"paineis\":[{\"nome\":\"x\"}]}")));
    }
}
