using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DEB.Core.Elementos.Pilar.PreMoldado;
using DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D;
using DEB.Core.Normas.Verificacao;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Painel de verificações da janela dos pilares do Revit (etapa 3): selo, contagem e pendências, sem gravar arquivo.</summary>
public class PainelVerificacoesTests
{
    private static readonly ColumnBox F = new ColumnBox(U_.Mm(400), U_.Mm(400), U_.Mm(6000));
    private static readonly List<Corbel> SemConsolo = new List<Corbel>();

    [Fact]
    public void ContagemESeloBatemComOsResultados()
    {
        var s = ColumnRebarSpec.Default(F, 1000, SemConsolo);
        var p = PainelVerificacoes.De(s, F, SemConsolo);
        Assert.Equal(p.Resultados.Count + p.OutrosAvisos.Count, p.Ok + p.Conferir + p.NaoAtende);
        Assert.Contains(p.Resultados, x => x.Codigo == "P8" && x.Situacao == SituacaoVerificacao.Ok);
        // as pendências são os mesmos avisos que o Check devolve; nada se repete
        var avisos = s.Check(F, SemConsolo);
        foreach (var x in p.Pendencias) Assert.Contains(x.Mensagem, avisos);
        Assert.Equal(p.Resultados.Count, p.Resultados.Select(x => x.Mensagem).Distinct().Count());
        Assert.Equal(avisos.Distinct().Count(), p.Pendencias.Count() + p.OutrosAvisos.Count);
        Assert.Equal($"{p.Ok} OK · {p.Conferir} a conferir · {p.NaoAtende} não atende", p.Contagem);
    }

    [Fact]
    public void ClasseDeAgressividadeEFckMudamOSelo()
    {
        var s = ColumnRebarSpec.Default(F, 1000, SemConsolo);
        s.ExposureClass = "IV"; s.CoverMm = 25;                     // classe IV pede bem mais que 25 mm
        var p = PainelVerificacoes.De(s, F, SemConsolo);
        Assert.Equal(SituacaoVerificacao.NaoAtende, p.Geral);
        Assert.Equal("Não atende", PainelVerificacoes.Texto(p.Geral));
        Assert.Contains(p.Pendencias, x => x.Codigo.StartsWith("G"));
        Assert.Equal(SituacaoVerificacao.NaoAtende, p.Pendencias.First().Situacao);

        s.ExposureClass = "I"; s.CoverMm = 40;
        Assert.DoesNotContain(PainelVerificacoes.De(s, F, SemConsolo).Pendencias, x => x.Codigo.StartsWith("G") && x.Situacao == SituacaoVerificacao.NaoAtende);
    }

    [Fact]
    public void AvisoSemCodigoLevaAConferir()
    {
        var s = ColumnRebarSpec.Default(F, 1000, SemConsolo);
        s.ArmaduraPorLinha = new List<LinhaArmaduraPorLinha>();      // sem longitudinais: aviso sem código
        var p = PainelVerificacoes.De(s, F, SemConsolo);
        Assert.Contains(p.OutrosAvisos, a => a.StartsWith("Sem armadura longitudinal"));
        Assert.NotEqual(SituacaoVerificacao.Ok, p.Geral);
    }

    [Fact]
    public void ModeloSalvoSemOsCamposNovosCarregaComOsPadroes()
    {
        // modelo gravado antes da etapa 3 (sem classe, fck, a/c, agregado e aços)
        var antigo = JsonSerializer.Deserialize<ColumnRebarSpec>("{\"Name\":\"Antigo\",\"CoverMm\":30}");
        Assert.Equal("II", antigo.ExposureClass);
        Assert.Equal(40, antigo.FckMPa);
        Assert.False(antigo.WaterCement045);
        Assert.Equal(19, antigo.AggregateMm);
        Assert.Equal("CA-50", antigo.LongSteel);
        Assert.Equal("CA-50", antigo.StirrupSteel);

        var s = new ColumnRebarSpec { Name = "Novo", ExposureClass = "III", FckMPa = 50, WaterCement045 = true, AggregateMm = 25,
                                      LongSteel = "CA-50", StirrupSteel = "CA-60" };
        var lido = JsonSerializer.Deserialize<ColumnRebarSpec>(JsonSerializer.Serialize(s));
        Assert.Equal(("III", 50.0, true, 25.0, "CA-60"), (lido.ExposureClass, lido.FckMPa, lido.WaterCement045, lido.AggregateMm, lido.StirrupSteel));
    }
}
