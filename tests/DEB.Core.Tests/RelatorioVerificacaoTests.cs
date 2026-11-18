using System;
using System.IO;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Documentacao;
using System.Collections.Generic;
using DEB.Core.Elementos.Pilar.PreMoldado;
using DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D;
using DEB.Core.Motor;
using DEB.Core.Normas.Verificacao;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Relatório HTML de verificação por grupo (D37).</summary>
public class RelatorioVerificacaoTests
{
    private static readonly TemplatePilarPreMoldado T = new TemplatePilarPreMoldado();

    private static ResultadoTemplate Pilar(params (string k, string v)[] mudancas)
    {
        var v = new ValoresTemplate(T.Parametros);
        foreach (var (k, x) in mudancas) v[k] = x;
        return T.Gerar(v, new ConfiguracaoArmadura { AlturaTextoMm = 2 });
    }

    [Fact]
    public void PilarPreencheTodasAsVerificacoesEOCabecalho()
    {
        var r = Pilar(("Nome", "P61"));
        Assert.Equal("Pilar pré-moldado", r.ElementoRelatorio);
        Assert.Contains(r.DadosRelatorio, d => d.Key == "Seção");
        // a lista do relatório tem também as OK; os avisos, só as falhas
        Assert.Contains(r.Verificacoes, x => x.Situacao == SituacaoVerificacao.Ok && x.Codigo == "P8");
        Assert.Contains(r.Verificacoes, x => x.Codigo == "G1" || x.Codigo == "G2");
        Assert.Equal(r.Verificacoes.Count(x => x.Situacao != SituacaoVerificacao.Ok),
                     r.Avisos.Count(a => a.StartsWith("Não atende (") || a.StartsWith("Conferir (")));
        // a mesma regra conferida em várias seções aparece uma vez
        Assert.Equal(1, r.Verificacoes.Count(x => x.Codigo == "P8"));
    }

    [Fact]
    public void HtmlTemSeloTabelasEPendencias()
    {
        var g = RelatorioHtml.DoTemplate(Pilar(("Nome", "P61"), ("CobFace1", "1.5")), "P61", "teste");
        string html = RelatorioHtml.Gerar(g);
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("Verificação normativa — P61", html);
        Assert.Contains("NÃO ATENDE", html);                          // cobrimento de 15 mm < 20 mm
        Assert.Contains("<h2>Pendências</h2>", html);
        Assert.Contains("<h2>Cobrimento</h2>", html);
        Assert.Contains("NBR 6118, 17.3.5.3.2", html);                // a linha OK da taxa máxima também aparece
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public void AceiteTiraAPendenciaDoSelo()
    {
        var lista = new ListaVerificacoes();
        lista.Add(VerificacoesPilar.PilarParede(200, 1100));          // Conferir
        lista.Add(VerificacoesPilar.TaxaMaxima(1, 100));              // OK
        var g = new RelatorioGrupo { Marca = "PP1", Elemento = "Pilar pré-moldado", Verificacoes = lista };
        Assert.Contains("CONFERIR", RelatorioHtml.Gerar(g));
        var pend = lista[0];
        var ac = new AceitesObra();
        ac.Aceites.Add(new AceitePendencia { Marca = "pp1", Codigo = pend.Codigo, Descricao = pend.Descricao, Valor = pend.Valor,
                                             Engenheiro = "Eng. responsável", Data = "18/11/2026", Comentario = "pilar-parede verificado à parte" });
        string html = RelatorioHtml.Gerar(g, ac);
        Assert.Contains("ACEITO", html);
        Assert.Contains("pilar-parede verificado à parte", html);
        Assert.Contains("class=\"selo ok\"", html);
        // o valor mudou: o aceite não vale mais
        ac.Aceites[0].Valor = "outro";
        Assert.Contains("class=\"selo cf\"", RelatorioHtml.Gerar(g, ac));
    }

    [Fact]
    public void GravaNaPastaDaObraELeOsAceites()
    {
        string pasta = Path.Combine(Path.GetTempPath(), "pceb_rel_" + Guid.NewGuid().ToString("N"));
        try
        {
            var lista = new ListaVerificacoes { VerificacoesPilar.PilarParede(200, 1100) };
            var ac = new AceitesObra();
            ac.Aceites.Add(new AceitePendencia { Marca = "P1/2", Codigo = "P2", Descricao = lista[0].Descricao, Valor = lista[0].Valor, Engenheiro = "Eng." });
            ac.Salvar(pasta);
            string arq = RelatorioHtml.Gravar(new RelatorioGrupo { Marca = "P1/2", Elemento = "Pilar", Verificacoes = lista }, pasta);
            Assert.EndsWith(Path.Combine(RelatorioHtml.Subpasta, "Verificação - P1-2.html"), arq);   // "/" não vale em nome de arquivo
            Assert.Contains("ACEITO", File.ReadAllText(arq));
        }
        finally { if (Directory.Exists(pasta)) Directory.Delete(pasta, true); }
    }

    [Fact]
    public void RevitMontaORelatorioDoGrupoComAsMesmasRegras()
    {
        var f = new ColumnBox(U_.Mm(400), U_.Mm(400), U_.Mm(6000));
        var s = ColumnRebarSpec.Default(f, 1000, new List<Corbel>());
        var g = s.Relatorio(f, new List<Corbel>(), "P12", 4, "modelo do Revit");
        Assert.Equal("P12", g.Marca);
        Assert.Contains(g.Dados, d => d.Key == "Seção" && d.Value == "40 × 40 cm");
        Assert.Contains(g.Verificacoes, x => x.Codigo == "P8" && x.Situacao == SituacaoVerificacao.Ok);
        // as falhas da lista são os mesmos avisos que o Check devolve
        var avisos = s.Check(f, new List<Corbel>());
        foreach (var x in g.Verificacoes.Where(x => x.Situacao != SituacaoVerificacao.Ok)) Assert.Contains(x.Mensagem, avisos);
        Assert.Contains("Verificação normativa — P12", RelatorioHtml.Gerar(g));
    }

    /// <summary>Gera um relatório de exemplo em PCEB_RELATORIO_EXEMPLO (pasta) para olhar no navegador; sem a variável, não faz nada.</summary>
    [Fact]
    public void ExemploParaConferirNoNavegador()
    {
        string pasta = Environment.GetEnvironmentVariable("PCEB_RELATORIO_EXEMPLO");
        if (string.IsNullOrEmpty(pasta)) return;
        var g = RelatorioHtml.DoTemplate(Pilar(("Nome", "P61"), ("Quantidade", "3"), ("CobFace1", "2.0")), "P61", "exemplo dos testes");
        RelatorioHtml.Gravar(g, pasta);
    }
}
