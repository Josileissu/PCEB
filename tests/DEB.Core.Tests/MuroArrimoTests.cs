using System.Linq;
using DEB.Core.Configuracao.Projeto;
using DEB.Core.Elementos.Contencao;
using DEB.Core.Normas.Verificacao;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>
/// Muro de arrimo em balanço (D47). Gabarito em forma fechada (Gabaritos/muro_arrimo_gabarito.py): β = 0, sem água, Rankine;
/// coeficientes de Coulomb e de Rankine com aterro inclinado conferidos com as tabelas de Das (Principles of Geotechnical Engineering).
/// </summary>
public class MuroArrimoTests
{
    private static ResultadoMuro Padrao(System.Action<DadosMuro> ajuste = null)
    {
        var d = new DadosMuro();
        ajuste?.Invoke(d);
        return CalculoMuro.Calcular(d, new ConfiguracaoProjeto());
    }

    [Fact]
    public void Coeficientes_de_empuxo_batem_com_as_tabelas()
    {
        Assert.Equal(1.0 / 3, CalculoMuro.KaRankine(30, 0), 6);
        Assert.Equal(0.3495, CalculoMuro.KaRankine(30, 10), 4);       // Das, tabela de Rankine para aterro inclinado
        Assert.Equal(0.2973, CalculoMuro.KaCoulomb(30, 0, 20), 4);     // Das, tabela de Coulomb (α = 90°, β = 0)
        Assert.Equal(3.0, CalculoMuro.KpRankine(30), 6);
        var (nc, nq, ng) = CalculoMuro.FatoresCapacidade(30);
        Assert.Equal(30.14, nc, 1); Assert.Equal(18.40, nq, 1); Assert.Equal(22.40, ng, 1);  // Vesic
    }

    [Fact]
    public void Estabilidade_bate_com_o_gabarito_em_forma_fechada()
    {
        var r = Padrao();
        Assert.Equal(45.63, -r.Forcas.Single(f => f.Nome == "Empuxo do solo").Fx, 2);
        Assert.Equal(13.00, -r.Forcas.Single(f => f.Nome == "Empuxo da sobrecarga").Fx, 2);
        Assert.Equal(3.0013, r.FsTombamento, 3);
        Assert.Equal(0.9833, r.FsDeslizamento, 3);
        // a base com a sobrecarga sobre o talão é o caso de maior tensão? não: sem ela σmáx = 93,9 > 92,9 kPa
        Assert.Equal(93.92, r.SigmaMax, 1);
        Assert.Equal(SituacaoVerificacao.NaoAtende, r.Verificacoes.Single(v => v.Codigo == "MA2").Situacao);
        Assert.Equal(SituacaoVerificacao.Ok, r.Verificacoes.Single(v => v.Codigo == "MA1").Situacao);
    }

    [Fact]
    public void Fuste_tem_o_momento_do_balanco_triangular_mais_o_retangular()
    {
        var r = Padrao();
        var f = r.Partes.Single(p => p.Nome == "Fuste (base)");
        Assert.Equal(63.29, f.Mk, 1);     // Ka·γ·h³/6 + Ka·q·h²/2
        Assert.Equal(88.61, f.Md, 1);
        Assert.Equal(67.78, f.Vd, 1);
        Assert.True(f.As > f.AsMin);
        Assert.StartsWith("ø", f.Arranjo);
    }

    [Fact]
    public void Dente_sob_a_sapata_resolve_o_deslizamento()
    {
        var sem = Padrao();
        var com = Padrao(d => { d.DenteAltura = 0.6; d.DenteLargura = 0.35; d.DenteInicio = 0.70; });
        Assert.True(com.FsDeslizamento > sem.FsDeslizamento + 0.4);
        Assert.Contains(com.Memoria[0].Linhas, l => l.Contains("no dente"));
    }

    [Fact]
    public void Agua_atras_do_muro_aumenta_o_empuxo_e_reduz_a_seguranca()
    {
        var seco = Padrao();
        var agua = Padrao(d => d.NivelAgua = 2.0);
        Assert.Contains(agua.Forcas, f => f.Tipo == "Agua" && f.Fy > 0);       // subpressão
        Assert.True(agua.FsTombamento < seco.FsTombamento);
        Assert.True(agua.Partes.Single(p => p.Nome == "Fuste (base)").Md > seco.Partes.Single(p => p.Nome == "Fuste (base)").Md);
    }

    [Fact]
    public void Base_descomprimida_alem_de_um_terco_nao_atende()
    {
        var r = Padrao(d => { d.Ponta = 0.0; d.Talao = 0.6; });
        Assert.True(r.BaseComprimida < r.Dados.Base);
        Assert.Equal(SituacaoVerificacao.NaoAtende, r.Verificacoes.Single(v => v.Codigo == "MA3").Situacao);
    }

    [Fact]
    public void Janela_recalcula_com_os_dados_da_pagina()
    {
        var s = new SessaoMuro(new DadosMuro { DenteAltura = 0.6, DenteLargura = 0.35, Ponta = 0.9, Talao = 1.9 }, new ConfiguracaoProjeto(), "Muro de teste");
        string pronto = s.Processar("{\"tipo\":\"pronto\"}");
        Assert.Contains("\"tipo\":\"muro\"", pronto);
        Assert.Contains("\"campos\"", pronto);
        string novo = s.Processar("{\"tipo\":\"calcular\",\"dados\":{\"h\":4.5,\"metodo\":\"Coulomb\"}}");
        Assert.Equal(4.5, s.Dados.AlturaFuste);
        Assert.Equal(MetodoEmpuxo.Coulomb, s.Dados.Metodo);
        Assert.Equal(0.6, s.Dados.DenteAltura);                   // o resto dos dados fica
        Assert.Contains("\"metodo\":\"Coulomb\"", novo);
        Assert.Equal("", s.Processar("{\"tipo\":\"aplicar\"}"));
        Assert.True(s.Aplicar);
        Assert.Equal("", s.Processar("lixo"));
        Assert.DoesNotContain("/*DADOS*/null", s.PaginaComDados());
        // prévia para captura de tela (fora do Git): $env:PCEB_PREVIA_MURO = "<pasta>"
        string pasta = System.Environment.GetEnvironmentVariable("PCEB_PREVIA_MURO");
        if (!string.IsNullOrEmpty(pasta))
        {
            var p2 = new SessaoMuro(new DadosMuro { DenteAltura = 0.6, DenteLargura = 0.35, Ponta = 0.9, Talao = 1.9, NivelAgua = 1.2, Beta = 10 }, new ConfiguracaoProjeto(), "Muro de teste");
            System.IO.File.WriteAllText(System.IO.Path.Combine(pasta, "muro.html"), p2.PaginaComDados());
        }
    }

    [Fact]
    public void Dados_invalidos_nao_calculam()
    {
        var r = Padrao(d => d.Beta = 35);
        Assert.All(r.Verificacoes, v => Assert.Equal("MA0", v.Codigo));
        Assert.Equal(SituacaoVerificacao.NaoAtende, r.Situacao);
    }
}
