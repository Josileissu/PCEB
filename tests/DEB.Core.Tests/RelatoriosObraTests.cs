using System;
using System.IO;
using System.Linq;
using DEB.Core.Documentacao;
using DEB.Core.Normas.Verificacao;
using DEB.Core.Persistencia;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Relatórios da obra (D37): dados dos grupos ao lado do HTML, pendências, aceite e índice.</summary>
public class RelatoriosObraTests
{
    private static ResultadoVerificacao V(string codigo, SituacaoVerificacao s, string valor = "1") => new ResultadoVerificacao
    {
        Codigo = codigo, Grupo = "Geral", Descricao = "Regra " + codigo, Valor = valor, Limite = "≥ 2", Situacao = s,
        Referencia = "NBR 6118, 18.4", Dica = s == SituacaoVerificacao.Ok ? "" : "Aumente \"x\".",
    };

    private static RelatorioGrupo Grupo(string marca, params ResultadoVerificacao[] v)
    {
        var g = new RelatorioGrupo { Marca = marca, Elemento = "Pilar pré-moldado", Origem = "teste", Data = new DateTime(2026, 11, 18, 20, 0, 0) };
        g.Dados.Add(new System.Collections.Generic.KeyValuePair<string, string>("Seção", "40 × 40 cm"));
        g.Verificacoes.AddRange(v);
        return g;
    }

    private static void NaPasta(Action<string> teste)
    {
        string pasta = Path.Combine(Path.GetTempPath(), "pceb_obra_" + Guid.NewGuid().ToString("N"));
        try { teste(pasta); }
        finally { if (Directory.Exists(pasta)) Directory.Delete(pasta, true); }
    }

    [Fact]
    public void DadosDoGrupoVaoEVoltam()
    {
        var a = Grupo("P61", V("P1", SituacaoVerificacao.Ok), V("G2", SituacaoVerificacao.Conferir), V("C3", SituacaoVerificacao.NaoAtende, "2,5 cm"));
        var b = RelatoriosObra.DeJson(Json.Ler(Json.Gravar(RelatoriosObra.ParaJson(a))));
        Assert.Equal((a.Marca, a.Elemento, a.Origem, a.Data), (b.Marca, b.Elemento, b.Origem, b.Data));
        Assert.Equal(a.Dados, b.Dados);
        Assert.Equal(a.Verificacoes.Select(r => (r.Codigo, r.Grupo, r.Descricao, r.Valor, r.Limite, r.Situacao, r.Referencia, r.Dica)),
                     b.Verificacoes.Select(r => (r.Codigo, r.Grupo, r.Descricao, r.Valor, r.Limite, r.Situacao, r.Referencia, r.Dica)));
        Assert.Equal(RelatorioHtml.Gerar(a), RelatorioHtml.Gerar(b));
    }

    [Fact]
    public void GravarGuardaOsDadosEOIndiceEAceitarRegravaTudo() => NaPasta(pasta =>
    {
        RelatorioHtml.Gravar(Grupo("P10", V("P1", SituacaoVerificacao.Ok), V("G2", SituacaoVerificacao.Conferir)), pasta);
        RelatorioHtml.Gravar(Grupo("P2", V("G2", SituacaoVerificacao.Conferir), V("C3", SituacaoVerificacao.NaoAtende)), pasta);
        Assert.Equal(new[] { "P2", "P10" }, RelatoriosObra.Ler(pasta).Select(g => g.Marca));   // números pelo valor
        string indice = Path.Combine(pasta, RelatorioHtml.Subpasta, RelatoriosObra.NomeIndice);
        Assert.Contains("class=\"selo na\"", File.ReadAllText(indice));

        var pend = RelatoriosObra.Pendencias(pasta);
        Assert.Equal(new[] { "P2", "P10" }, pend.Select(p => p.Marca));   // "Não atende" não é pendência para aceite
        Assert.All(pend, p => Assert.Null(p.Aceite));

        // aceita só a do P10
        var agora = new DateTime(2026, 11, 18, 21, 30, 0);
        RelatoriosObra.Aplicar(pasta, pend.Select(p => new DecisaoAceite { Pendencia = p, Aceitar = p.Marca == "P10", Comentario = " cobrimento conferido " }), "Eng. Teste", agora);
        pend = RelatoriosObra.Pendencias(pasta);
        var ac = pend.Single(p => p.Marca == "P10").Aceite;
        Assert.Equal(("Eng. Teste", "18/11/2026 21:30", "cobrimento conferido"), (ac.Engenheiro, ac.Data, ac.Comentario));
        Assert.Null(pend.Single(p => p.Marca == "P2").Aceite);
        string p10 = File.ReadAllText(Path.Combine(pasta, RelatorioHtml.Subpasta, RelatorioHtml.NomeArquivo("P10")));
        Assert.Contains("ACEITO", p10);
        Assert.Contains("class=\"selo ok\"", p10);
        string html = File.ReadAllText(indice);
        Assert.Contains("Pendências aceitas", html);
        Assert.Contains("Eng. Teste", html);
        Assert.Contains("Pendências em aberto", html);

        // aceitar de novo com o mesmo comentário mantém quem aceitou e a data; desmarcar retira
        RelatoriosObra.Aplicar(pasta, pend.Where(p => p.Marca == "P10").Select(p => new DecisaoAceite { Pendencia = p, Aceitar = true, Comentario = "cobrimento conferido" }), "Outro", agora.AddDays(1));
        Assert.Equal("Eng. Teste", RelatoriosObra.Pendencias(pasta).Single(p => p.Marca == "P10").Aceite.Engenheiro);
        RelatoriosObra.Aplicar(pasta, RelatoriosObra.Pendencias(pasta).Select(p => new DecisaoAceite { Pendencia = p, Aceitar = false }), "Outro");
        Assert.All(RelatoriosObra.Pendencias(pasta), p => Assert.Null(p.Aceite));
        Assert.Empty(AceitesObra.Ler(pasta).Aceites);
    });

    [Fact]
    public void AceiteDeixaDeValerQuandoOValorMuda() => NaPasta(pasta =>
    {
        RelatorioHtml.Gravar(Grupo("V1", V("D6", SituacaoVerificacao.Conferir, "1,2 cm")), pasta);
        RelatoriosObra.Aplicar(pasta, RelatoriosObra.Pendencias(pasta).Select(p => new DecisaoAceite { Pendencia = p, Aceitar = true }), "Eng.");
        Assert.NotNull(RelatoriosObra.Pendencias(pasta).Single().Aceite);
        RelatorioHtml.Gravar(Grupo("V1", V("D6", SituacaoVerificacao.Conferir, "0,8 cm")), pasta);   // a peça mudou
        Assert.Null(RelatoriosObra.Pendencias(pasta).Single().Aceite);
    });

    [Fact]
    public void ObraSemRelatorios() => NaPasta(pasta =>
    {
        Assert.Empty(RelatoriosObra.Ler(pasta));
        Assert.Empty(RelatoriosObra.Pendencias(pasta));
        Assert.Contains("class=\"selo ok\"", RelatoriosObra.GerarIndice(Array.Empty<RelatorioGrupo>(), null));
    });
}
