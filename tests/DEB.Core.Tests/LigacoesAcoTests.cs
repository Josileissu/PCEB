using System.Linq;
using DEB.Core.Elementos.Metalica;
using DEB.Core.Elementos.Metalica.Ligacoes;
using DEB.Core.Normas.Verificacao;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>
/// Ligações de aço (D48): componentes da NBR 8800:2024 conferidos com o exemplo II.A-17B do AISC (v15.1, vol. 1), em valores
/// nominais (as expressões de 2024 coincidem com as do AISC 360-16 nesses componentes): parafuso ø7/8" A325-N, alma W18×50,
/// chapa de ½" × 14½" em A572 Gr. 50. 1 kip = 4,448 kN.
/// </summary>
public class LigacoesAcoTests
{
    private const double Kip = 4.44822;
    private static readonly TipoParafuso A325 = new TipoParafuso { Nome = "A325 (120 ksi)", Fub = 827.37, AltaResistencia = true };

    [Fact]
    public void Componentes_batem_com_o_exemplo_IIA17B_do_AISC()
    {
        double d = 22.225, tw = 0.355 * 25.4, fu = 448.16, fy = 344.74;
        Assert.Equal(32.5, ComponentesLigacao.CisalhamentoParafuso(d, A325, true, 1.0) / Kip, 0.2);
        Assert.Equal(60.6, ComponentesLigacao.PressaoContato(99, tw, d, fu, false, 1.0) / Kip, 0.2);          // 3,0·d·t·Fu
        Assert.Equal(35.7, ComponentesLigacao.PressaoContato(1.03 * 25.4, tw, d, fu, false, 1.0) / Kip, 0.2); // 1,5·lc·t·Fu
        double agv = 14.5 * 25.4 * 0.5 * 25.4;
        Assert.Equal(218, 0.6 * fy * agv / 1000 / Kip, 0.5);
        Assert.Equal(1320, 0.5 * 25.4 * System.Math.Pow(14.5 * 25.4, 2) / 4 * fy / 1e3 / Kip / 25.4, 8.0);     // Zp·Fy (kip·in; o AISC arredonda Z = 26,3 in³)
    }

    [Fact]
    public void Furo_padrao_e_rasgamento()
    {
        Assert.Equal(18, ComponentesLigacao.Furo(16));          // NBR 8800:2024 Tab. 14
        Assert.Equal(30, ComponentesLigacao.Furo(27));
        Assert.Equal(19.05 + 1.5875, ComponentesLigacao.Furo(19.05), 6);   // ¾" → 13/16"
        Assert.Equal(39, ComponentesLigacao.Furo(36));
        // rasgamento limitado pelo escoamento da área bruta ao cisalhamento
        double r = ComponentesLigacao.Rasgamento(1000, 900, 200, 250, 400, 1.0, 1.0);
        Assert.Equal(System.Math.Min(0.6 * 400 * 900 + 400 * 200, 0.6 * 250 * 1000 + 400 * 200) / 1000, r, 6);
    }

    [Fact]
    public void Chapa_de_alma_verifica_e_aponta_o_caso_critico()
    {
        var d = new DadosChapaAlma
        {
            Viga = PerfilI.Buscar("W310X38.7"), AcoViga = AcoEstrutural.Buscar("A572"), AcoChapa = AcoEstrutural.Buscar("A36"),
            Db = 19.05, N = 3, S = 75, Tp = 9.5, Ev = 38, EhChapa = 38, EhViga = 38, A = 75, Perna = 6,
        };
        var v = LigacaoChapaAlma.Verificar(d, new[] { ("ELU 1", 80.0, 0.0), ("ELU 2", 90.0, 10.0) }, "Ligação teste", out var mem);
        Assert.Equal(9, v.Count);
        Assert.Contains("ELU 2", v.Single(x => x.Codigo == "LC1").Valor);
        Assert.All(v.Where(x => x.Codigo != "LC8" && x.Codigo != "LC9"), x => Assert.Equal(SituacaoVerificacao.Ok, x.Situacao));
        Assert.Contains(mem.Linhas, l => l.Contains("método elástico"));
        // carga excessiva: os parafusos não atendem
        var v2 = LigacaoChapaAlma.Verificar(d, new[] { ("ELU forte", 400.0, 0.0) }, "Ligação teste", out _);
        Assert.Equal(SituacaoVerificacao.NaoAtende, v2.Single(x => x.Codigo == "LC1").Situacao);
    }

    [Fact]
    public void Biblioteca_traz_as_ligacoes_do_PCEB_e_o_escritorio_substitui()
    {
        string arq = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pceb_ligacoes_" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var b = BibliotecaLigacoes.Carregar(null, arq);
            Assert.Equal(17, b.Pceb.Count);
            foreach (TipoLigacao t in System.Enum.GetValues(typeof(TipoLigacao))) Assert.NotNull(b.Padrao(t));
            Assert.All(b.Pceb, m => Assert.Empty(m.Erros()));
            // escritório: muda a chapa padrão para 8 mm e cria uma ligação nova
            var minha = b.Buscar("PCEB-CA-19").Copia(OrigemLigacao.Escritorio);
            minha.Valores["tp"] = "8";
            b.Escritorio.Add(minha);
            b.Escritorio.Add(new ModeloLigacao { Id = "ESC-CA-16", Nome = "Chapa de alma ø16", Tipo = TipoLigacao.ChapaAlma, Origem = OrigemLigacao.Escritorio, Valores = { ["db"] = "16" } });
            b.GravarEscritorio(arq);
            var b2 = BibliotecaLigacoes.Carregar(null, arq);
            Assert.Equal(8.0, b2.Buscar("PCEB-CA-19").Numero("tp"));
            Assert.Equal(OrigemLigacao.Escritorio, b2.Buscar("PCEB-CA-19").Origem);
            Assert.Equal(b2.Pceb.Count + 1, b2.Todos.Count);
            Assert.Equal("ASTM F3125 A325", b2.Buscar("ESC-CA-16").Texto("parafuso"));   // padrão do esquema
            // valor inválido aparece nos erros
            var ruim = new ModeloLigacao { Tipo = TipoLigacao.ChapaAlma, Valores = { ["tp"] = "100", ["parafuso"] = "X" } };
            Assert.Equal(2, ruim.Erros().Count);
        }
        finally { if (System.IO.File.Exists(arq)) System.IO.File.Delete(arq); }
    }

    [Fact]
    public void Chapa_de_alma_do_modelo_escolhe_os_parafusos_pela_viga()
    {
        var b = BibliotecaLigacoes.Carregar(null, "nao_existe.json");
        var m = b.Buscar("PCEB-CA-19");
        var baixa = DadosChapaAlma.DoModelo(m, PerfilI.Buscar("W200X15"), AcoEstrutural.Buscar("A572"));
        var alta = DadosChapaAlma.DoModelo(m, PerfilI.Buscar("W530X66"), AcoEstrutural.Buscar("A572"));
        Assert.Equal(2, baixa.N);
        Assert.True(alta.N >= 6);
        Assert.Equal(9.5, alta.Tp);
        Assert.Equal("ASTM A36", alta.AcoChapa.Nome);
    }

    [Fact]
    public void Placa_de_base_caso_C1_bate_com_o_exemplo_41_do_Design_Guide_1()
    {
        // W12×96 (d = 12,7", bf = 12,2"), placa 22" × 20", Pu = 700 kips, Fy = 36 ksi, fc = 3 ksi, φ = 0,65 (concreto) e 0,9 (placa):
        // tmín = 1,60" = 40,6 mm. A NBR 8800:2024 tem a mesma estrutura (ℓmáx = máx(m, n, λ·n0)); aqui com γ = 1/φ.
        var d = new DadosPlacaBase
        {
            Pilar = new PerfilI("W12x96", 12.7 * 25.4, 12.2 * 25.4, 0.9 * 25.4, 0.55 * 25.4, 0, true),
            AcoPlaca = new AcoEstrutural { Nome = "36 ksi", Fy = 248.2, Fu = 400 }, Lx = 22 * 25.4, Ly = 20 * 25.4,
            Fck = 20.684, GamaC = 1 / 0.65, A2A1 = 1, Db = 19, Nb = 4,
        };
        var c = LigacaoPlacaBase.Calcular(d, "Pu", -700 * Kip, 0, 0, gamaA1: 1 / 0.9);
        Assert.Equal("C1", c.Tipo);
        Assert.Equal(5.12 * 25.4, c.Lmax, 0.5);
        Assert.Equal(1.60 * 25.4, c.TpMin, 0.4);
        Assert.True(c.SigmaSd < c.SigmaRd);   // 700 < φPp = 729 kips
    }

    [Fact]
    public void Placa_de_base_passa_pelos_casos_de_compressao_e_tracao()
    {
        var b = BibliotecaLigacoes.Carregar(null, "nao_existe.json");
        var d = DadosPlacaBase.DoModelo(b.Buscar("PCEB-PB-25"), PerfilI.Buscar("W250X32.7"), 1.4);
        Assert.Equal(d.Pilar.D + 4 * 50, d.LxEf, 6);           // d + 4·a1 (Tab. 18, ø25: a1 = 50)
        Assert.Equal("C2", LigacaoPlacaBase.Calcular(d, "x", -300, 5, 10).Tipo);
        var c3 = LigacaoPlacaBase.Calcular(d, "x", -100, 40, 10);
        Assert.Equal("C3", c3.Tipo);
        Assert.True(c3.Ft > 0 && c3.DbMin > 0);
        Assert.Equal("T1", LigacaoPlacaBase.Calcular(d, "x", 80, 0, 0).Tipo);
        var t2 = LigacaoPlacaBase.Calcular(d, "x", 80, 5, 0);
        Assert.Equal("T2", t2.Tipo);
        Assert.Equal(80000 / 4.0 + 5e6 / (d.A * 4), t2.Ft, 1);
        Assert.Equal("T3", LigacaoPlacaBase.Calcular(d, "x", 10, 40, 0).Tipo);
        var v = LigacaoPlacaBase.Verificar(d, new[] { ("ELU 1", -300.0, 20.0, 15.0), ("ELU 2", 40.0, 5.0, 5.0) }, "Base", out var mem);
        Assert.Contains(v, x => x.Codigo == "PB1" && x.Situacao == SituacaoVerificacao.Ok);   // espessura "auto": a comercial que atende
        Assert.Contains(v, x => x.Codigo == "PB5" && x.Situacao == SituacaoVerificacao.Conferir);   // tração: precisa de dispositivo ao cortante
        Assert.Contains(mem.Linhas, l => l.Contains("chapa comercial"));
    }

    [Fact]
    public void Chapa_de_topo_pelo_efeito_de_alavanca_da_NBR_8800()
    {
        var b = BibliotecaLigacoes.Carregar(null, "nao_existe.json");
        var d = DadosChapaTopo.DoModelo(b.Buscar("PCEB-CT-22"), PerfilI.Buscar("W310X38.7"));
        Assert.Equal(4, d.NTracionados);
        Assert.Equal(6, d.NTotal);
        var v = LigacaoChapaTopo.Verificar(d, new[] { ("ELU 1", 60.0, 50.0, 0.0) }, "Topo", out var mem);
        // força por parafuso: M/(d − tf)/4 e a espessura rígida de 6.3.5.3 com b = pf = 45, p = mín(bp/2; 3,5·b)
        double ft0 = 60e3 / (310 - 9.65) / 4, p = System.Math.Min((165 + 25) / 2.0, 3.5 * 45);
        double tRig = System.Math.Sqrt(4 * (45 - 0.5 * 22.2) * ft0 * 1000 * 1.10 / (p * 400));
        Assert.Contains(mem.Linhas, l => l.Contains(Fmt.Virgula(tRig, "0.0") + " mm"));
        Assert.Equal(SituacaoVerificacao.Ok, v.Single(x => x.Codigo == "CT1").Situacao);
        Assert.Equal(SituacaoVerificacao.Conferir, v.Single(x => x.Codigo == "CT8").Situacao);   // lado do pilar: ainda não
        var forte = LigacaoChapaTopo.Verificar(d, new[] { ("ELU forte", 400.0, 50.0, 0.0) }, "Topo", out _);
        Assert.Equal(SituacaoVerificacao.NaoAtende, forte.Single(x => x.Codigo == "CT3").Situacao);
    }

    [Fact]
    public void Cantoneira_dupla_em_corte_duplo()
    {
        var b = BibliotecaLigacoes.Carregar(null, "nao_existe.json");
        var d = DadosCantoneira.DoModelo(b.Buscar("PCEB-CD-76"), PerfilI.Buscar("W410X46.1"), AcoEstrutural.Buscar("A572"));
        Assert.True(d.N >= 4);
        var v = LigacaoCantoneira.Verificar(d, new[] { ("ELU 1", 100.0, 0.0) }, "Cantoneira", out _);
        Assert.Equal(8, v.Count);
        Assert.All(v, x => Assert.Equal(SituacaoVerificacao.Ok, x.Situacao));
        d.SoldadaNoSuporte = true;
        var vs = LigacaoCantoneira.Verificar(d, new[] { ("ELU 1", 100.0, 0.0) }, "Cantoneira", out var mem);
        Assert.Contains(vs, x => x.Codigo == "CD2" && x.Descricao.Contains("Solda"));
    }

    [Fact]
    public void Janela_da_biblioteca_edita_o_escritorio_sem_mudar_o_PCEB()
    {
        var b = BibliotecaLigacoes.Carregar(null, "nao_existe.json");
        var s = new SessaoBiblioteca(b);
        Assert.Contains("\"modelos\"", s.Receber("{\"acao\":\"estado\"}"));
        string r = s.Receber("{\"acao\":\"definir\",\"id\":\"PCEB-CA-19\",\"param\":\"tp\",\"valor\":\"8\"}");
        Assert.Equal("9.5", b.Pceb.Single(x => x.Id == "PCEB-CA-19").Texto("tp"));       // o do PCEB fica
        Assert.Equal(8.0, b.Buscar("PCEB-CA-19").Numero("tp"));                           // vale o do escritório
        Assert.True(s.Alterado);
        Assert.Contains("\"desenho\"", r);
        s.Receber("{\"acao\":\"restaurar\",\"id\":\"PCEB-CA-19\"}");
        Assert.Equal(9.5, b.Buscar("PCEB-CA-19").Numero("tp"));
        s.Receber("{\"acao\":\"novo\",\"tipo\":\"PlacaBase\"}");
        Assert.Equal("ESC-PB-1", s.Selecionado);
        Assert.Contains("erro", s.Receber("{\"acao\":\"excluir\",\"id\":\"PCEB-PB-25\"}"));
        foreach (var m in b.Todos) { s.Receber("{\"acao\":\"selecionar\",\"id\":\"" + m.Id + "\"}"); Assert.Contains("\"desenho\"", s.Estado(null)); }
        // prévia para captura de tela (fora do Git): $env:PCEB_PREVIA_LIGACOES = "<pasta>"
        string pasta = System.Environment.GetEnvironmentVariable("PCEB_PREVIA_LIGACOES");
        if (!string.IsNullOrEmpty(pasta))
            foreach (var id in new[] { "PCEB-CA-19", "PCEB-CT-22", "PCEB-PB-25", "PCEB-CD-76", "PCEB-AT-12", "PCEB-CN-L63", "PCEB-TF-101", "PCEB-ET-19", "PCEB-IN-19" })
            {
                s.Receber("{\"acao\":\"selecionar\",\"id\":\"" + id + "\"}");
                System.IO.File.WriteAllText(System.IO.Path.Combine(pasta, "ligacao_" + id + ".html"), s.PaginaComDados());
            }
        Assert.Equal("salvar", (s.Receber("{\"acao\":\"salvar\"}") != null ? s.Fim : ""));
    }

    [Fact]
    public void Geometria_das_ligacoes_tem_os_parafusos_e_os_perfis_transparentes()
    {
        var viga = PerfilI.Buscar("W310X38.7"); var pilar = PerfilI.Buscar("W250X32.7");
        foreach (var m in BibliotecaLigacoes.Carregar(null, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nao_existe_pceb.json")).Todos)
        {
            var cx = GeometriaLigacao.Montar(m, viga, pilar);
            Assert.Contains(cx, c => c.Papel == PapelSolido.Membro);
            Assert.Contains(cx, c => c.Papel == PapelSolido.Chapa);
            Assert.True(cx.Count(c => c.Papel == PapelSolido.Parafuso) >= 2 || m.Tipo == TipoLigacao.TuboFendido && cx.Count(c => c.Papel == PapelSolido.Solda) >= 4, m.Id);
            Assert.All(cx, c => Assert.True(c.X1 > c.X0 && c.Y1 > c.Y0 && c.Z1 > c.Z0, m.Id + " " + c.Rotulo));
        }
    }

    private static ModeloLigacao Modelo(TipoLigacao t, params (string, string)[] v)
    {
        var m = new ModeloLigacao { Id = "T", Nome = "T", Tipo = t };
        foreach (var (k, x) in v) m.Valores[k] = x;
        return m;
    }

    [Fact]
    public void Tubo_em_fenda_reduz_a_secao_liquida_so_com_solda_curta()
    {
        var d = DadosTuboFendido.DoModelo(Modelo(TipoLigacao.TuboFendido, ("lw", "110")));
        var v = LigacaoTuboFendido.Verificar(d, new[] { new EsforcoLigacao("T", 300) }, "g", out var mem);
        // Ct = 1 − (D/π)/Lw = 1 − 32,34/110 = 0,706; An = Ag − 2·(12,5 + 2)·6
        Assert.Contains(mem.Linhas, l => l.Contains("Ct = 0,71"));
        double ag = System.Math.PI * (101.6 * 101.6 - 89.6 * 89.6) / 4, an = ag - 2 * 14.5 * 6, nt = System.Math.Min(ag * 345 / 1.10, 0.706 * an * 450 / 1.35) / 1000;
        var tf2 = v.Single(x => x.Codigo == "TF2");
        Assert.Equal(System.Math.Round(300 / nt, 2), double.Parse(tf2.Valor.Substring(4, 4).Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture), 2);
        var longo = DadosTuboFendido.DoModelo(Modelo(TipoLigacao.TuboFendido, ("lw", "140")));
        LigacaoTuboFendido.Verificar(longo, new[] { new EsforcoLigacao("T", 300) }, "g", out var mem2);
        Assert.Contains(mem2.Linhas, l => l.Contains("Ct = 1,00"));
    }

    [Fact]
    public void Capacidade_por_bissecao_bate_com_a_verificacao_linear()
    {
        // contraventamento: a resistência à tração é o menor dos limites lineares; com Nd = R, o pior η = 1
        var d = DadosChapaNo.DoModelo(Modelo(TipoLigacao.ChapaNo));
        var (r, gov) = FuncoesLigacao.Capacidade(s => LigacaoChapaNo.Verificar(d, new[] { new EsforcoLigacao("", s) }, "", out _));
        Assert.False(string.IsNullOrEmpty(gov));
        var v = LigacaoChapaNo.Verificar(d, new[] { new EsforcoLigacao("", r * 0.999) }, "", out _);
        Assert.DoesNotContain(v, x => x.Valor.StartsWith("η") && x.Situacao == DEB.Core.Normas.Verificacao.SituacaoVerificacao.NaoAtende);
        Assert.Equal(2 * 120 * System.Math.Tan(System.Math.PI / 6), d.Whitmore, 6);
    }

    [Fact]
    public void Emenda_divide_o_momento_entre_mesas_e_alma()
    {
        var viga = PerfilI.Buscar("W310X38.7");
        var d = DadosEmendaTalas.DoModelo(Modelo(TipoLigacao.EmendaTalas), viga, AcoEstrutural.Buscar("A572"));
        var v = LigacaoEmendaTalas.Verificar(d, new[] { new EsforcoLigacao("M", 0, 0, 50) }, "g", out var mem);
        double hw = viga.D - 2 * viga.Tf, im = 2 * viga.Bf * viga.Tf * System.Math.Pow((viga.D - viga.Tf) / 2, 2), ia = viga.Tw * hw * hw * hw / 12, fa = ia / (im + ia);
        double ff = 50000 * (1 - fa) / (viga.D - viga.Tf), fv = ComponentesLigacao.CisalhamentoParafuso(19.05, TipoParafuso.Buscar("A325"), true);
        var et1 = v.Single(x => x.Codigo == "ET1");
        Assert.StartsWith("η = " + DEB.Core.Fmt.Virgula(ff / 6 / fv, "0.00"), et1.Valor);
        Assert.True(d.Na >= 2);
    }

    [Fact]
    public void Apoio_de_terca_combina_corte_e_tracao_nos_parafusos()
    {
        var d = DadosApoioTerca.DoModelo(Modelo(TipoLigacao.ApoioTerca));
        double fv = ComponentesLigacao.CisalhamentoParafuso(12.7, TipoParafuso.Buscar("A307"), true), ft = ComponentesLigacao.TracaoParafuso(12.7, TipoParafuso.Buscar("A307"));
        var v = LigacaoApoioTerca.Verificar(d, new[] { new EsforcoLigacao("C", 4, 6, 0, 0) }, "g", out _);
        double eta = System.Math.Pow(6.0 / 2 / fv, 2) + System.Math.Pow(4.0 / 2 / ft, 2);
        Assert.StartsWith("η = " + DEB.Core.Fmt.Virgula(eta, "0.00"), v.Single(x => x.Codigo == "AT1").Valor);
        Assert.True(d.Hp >= 0.75 * 150 - 1e-9);
    }

    [Fact]
    public void Insert_cone_de_concreto_do_grupo_pela_EN_1992_4()
    {
        var d = DadosChapaEmbutida.DoModelo(Modelo(TipoLigacao.ChapaEmbutida, ("db", "19"), ("hef", "100"), ("nx", "2"), ("ny", "2"), ("sx", "150"), ("sy", "150"), ("fck", "30")));
        // N0 = 7,7·√30·100^1,5 = 42,17 kN; Ac,N/A0 = 450²/300² = 2,25; γMc = 1,5 → NRd,c = 63,26 kN
        double nrdc = 7.7 * System.Math.Sqrt(30) * 1000 / 1000 * 2.25 / 1.5;
        var v = LigacaoChapaEmbutida.Verificar(d, new[] { new EsforcoLigacao("N", nrdc) }, "g", out var mem);
        Assert.StartsWith("η = 1,00", v.Single(x => x.Codigo == "IN2").Valor);
        // aço do pino: As·fu/γMs com γMs = 1,2·450/350 = 1,543 → 82,7 kN por pino; 4 pinos com 63 kN no grupo: 15,8 kN cada
        Assert.StartsWith("η = 0,19", v.Single(x => x.Codigo == "IN1").Valor);
        // perto da borda aparece a ruptura da borda na cortante
        var db = DadosChapaEmbutida.DoModelo(Modelo(TipoLigacao.ChapaEmbutida, ("borda", "80")));
        Assert.Contains(LigacaoChapaEmbutida.Verificar(db, new[] { new EsforcoLigacao("V", 0, 20) }, "g", out _), x => x.Codigo == "IN6");
        Assert.Contains(GeometriaLigacao.Montar(Modelo(TipoLigacao.ChapaEmbutida), PerfilI.Buscar("W310X38.7"), PerfilI.Buscar("W250X32.7")), c => c.Papel == PapelSolido.Concreto);
    }
}
