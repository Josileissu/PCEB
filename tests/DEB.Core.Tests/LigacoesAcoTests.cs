using System.Linq;
using DEB.Core.Elementos.Metalica;
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
        Assert.Equal(17.5, ComponentesLigacao.Furo(16));
        Assert.Equal(30, ComponentesLigacao.Furo(27));
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
}
