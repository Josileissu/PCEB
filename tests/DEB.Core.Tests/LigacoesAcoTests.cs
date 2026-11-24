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
            Assert.Equal(8, b.Pceb.Count);
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
            Assert.Equal(9, b2.Todos.Count);
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
}
