using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Modelo;
using DEB.Core.Numeracao;
using DEB.Core.Persistencia;
using DEB.Core.RelacaoAco;
using DEB.Core.Templates;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Pedidos de 2026-10-27: escala da faixa, opções da tabela, templates.</summary>
public class NovidadesTests
{
    [Fact]
    public void FaixaGuardaEscalaPropria()
    {
        var f = new DadosFaixa { Tipo = TipoFaixa.Ortogonal, Escala = 25 };
        var x = CodecDados.DecodificarFaixa(CodecDados.Codificar(f));
        Assert.Equal(25, x.Escala);
        Assert.Equal(25, x.EscalaEfetiva(50));
        // faixa antiga (sem escala): acompanha a barra
        var antiga = CodecDados.DecodificarFaixa(CodecDados.Codificar(new DadosFaixa()));
        Assert.Equal(0, antiga.Escala);
        Assert.Equal(50, antiga.EscalaEfetiva(50));
    }

    [Fact]
    public void TabelaComColunaENomeDoDetalhamento()
    {
        var op = new OpcoesTabelaRA { ColunaElemento = ColunaElemento.Sempre, NomeNoTitulo = true };
        var x = CodecDados.DecodificarTabela(CodecDados.Codificar(op), new OpcoesTabelaRA());
        Assert.Equal(ColunaElemento.Sempre, x.ColunaElemento);
        Assert.True(x.NomeNoTitulo);
        Assert.True(x.ComElemento(TipoNumeracao.Global));
        Assert.False(new OpcoesTabelaRA { ColunaElemento = ColunaElemento.Nunca }.ComElemento(TipoNumeracao.Individual));
        Assert.True(new OpcoesTabelaRA().ComElemento(TipoNumeracao.Sequencial)); // automática

        var cfg = new ConfiguracaoArmadura();
        var r = RelacaoDoAco.Calcular(new[] { new ItemRA { Numero = 1, Aco = "CA-50", Bitola = 10, Quantidade = 2, ComprimentoUnitario = 100, Elemento = "V1" } }, cfg, true);
        var t = MontadorTabela.Montar(r, op, cfg, comElemento: true, detalhamentos: new[] { "V2", "V1" });
        Assert.Equal("RELAÇÃO DO AÇO - V1, V2", t.Linhas[0][0]);
        Assert.Equal("ELEMENTO", t.Linhas[1][0]);
        Assert.Equal("V1", t.Linhas[2][0]);
        // sem a opção, o título não muda
        op.NomeNoTitulo = false;
        Assert.Equal("RELAÇÃO DO AÇO", MontadorTabela.Montar(r, op, cfg, true, new[] { "V1" }).Linhas[0][0]);
    }

    private sealed class Exemplo : ITemplateArmadura
    {
        public string Id => "exemplo";
        public string Nome => "Exemplo";
        public CategoriaElemento Categoria => CategoriaElemento.Bloco1Estaca;
        public string Descricao => "";
        public IReadOnlyList<ParametroTemplate> Parametros { get; } = new[]
        {
            new ParametroTemplate { Nome = "B", Rotulo = "Largura", Padrao = "80", Minimo = 40, Maximo = 300 },
            new ParametroTemplate { Nome = "Calice", Rotulo = "Cálice", Tipo = TipoParametro.Opcao, Padrao = "Não", Opcoes = { "Sim", "Não" } },
        };
        public ResultadoTemplate Gerar(ValoresTemplate v, ConfiguracaoArmadura cfg) => new ResultadoTemplate();
    }

    [Fact]
    public void CatalogoValidaERegistraTemplates()
    {
        Assert.Equal(9, CatalogoTemplates.Categorias.Count); // + Reservatório (2026-11-05)
        var t = new Exemplo();
        CatalogoTemplates.Registrar(t);
        try
        {
            Assert.Same(t, CatalogoTemplates.Obter("EXEMPLO"));
            Assert.Single(CatalogoTemplates.DaCategoria(CategoriaElemento.Bloco1Estaca), x => x.Id == "exemplo");
            var v = new ValoresTemplate(t.Parametros);
            Assert.Equal(80, v.Numero("B"));
            Assert.Empty(CatalogoTemplates.Validar(t, v));
            v["B"] = "20";
            v["Calice"] = "Talvez";
            Assert.Equal(2, CatalogoTemplates.Validar(t, v).Count);
            v["B"] = "12,5";
            Assert.Equal(12.5, v.Numero("B"));
        }
        finally { CatalogoTemplates.Remover("exemplo"); }
        Assert.Null(CatalogoTemplates.Obter("exemplo"));
    }

    [Fact]
    public void DistribuicaoDaTela()
    {
        // Q-92: ø4.2 c/15 × c/15 num painel 245 × 600 (lado 1 = 245, lado 2 = 600)
        var m = new Tabelas.ModeloTela { Nome = "Q-92", EspLongitudinal = 15, EspTransversal = 15 };
        var fios = Tabelas.DistribuicaoTela.Fios(245, 600, m);
        int aoLongoDoComprimento = fios.Count(f => f.A.X == f.B.X); // paralelos ao lado 2 (o maior)
        Assert.Equal(17, aoLongoDoComprimento);                   // ⌊245/15⌋ + 1, sobra 5 → 2,5 de cada lado
        Assert.Equal(40, fios.Count - aoLongoDoComprimento);       // 600/15 = 40 exato → 40 (sem fio na borda)
        Assert.Equal(2.5, fios.First(f => f.A.X == f.B.X).A.X, 9);
        Assert.Equal(new[] { 5.0, 15, 25 }, Tabelas.DistribuicaoTela.Posicoes(30, 10));

        var d = new DadosArmadura { Tela = "Q-92", TelaDistribuicao = true, TelaCotas = false, TelaTextoDiagonal = false };
        var x = CodecDados.DecodificarBarra(CodecDados.Codificar(d));
        Assert.True(x.TelaDistribuicao);
        Assert.False(x.TelaCotas);
        Assert.False(x.TelaTextoDiagonal);
        var padrao = CodecDados.DecodificarBarra(CodecDados.Codificar(new DadosArmadura { Tela = "Q-92" }));
        Assert.True(padrao.TelaCotas && padrao.TelaTextoDiagonal && !padrao.TelaDistribuicao);
    }

    [Fact]
    public void RelacaoOrdenadaPeloN()
    {
        var cfg = new ConfiguracaoArmadura();
        var itens = new[]
        {
            new ItemRA { Numero = 3, Aco = "CA-50", Bitola = 10, Quantidade = 1, ComprimentoUnitario = 100 },
            new ItemRA { Numero = 1, Aco = "CA-60", Bitola = 5, Quantidade = 1, ComprimentoUnitario = 100 },
            new ItemRA { Numero = 2, Aco = "CA-50", Bitola = 8, Quantidade = 1, ComprimentoUnitario = 100 },
        };
        Assert.Equal(new[] { 1, 2, 3 }, RelacaoDoAco.Calcular(itens, cfg).Linhas.Select(l => l.Numero));
    }

    [Fact]
    public void CamposOpcionaisDoTextoDaFaixa()
    {
        var cfg = new ConfiguracaoArmadura();
        var barra = new DadosArmadura { Numero = 4, Bitola = 10 };
        var f = new DadosFaixa { Tipo = TipoFaixa.Ortogonal, Quantidade = 7, Espacamento = 15 };
        string completo = TextoFaixa.Formatar(f, barra, cfg);
        Assert.Equal(4, completo.Split(' ').Length);           // "7 N4 ø10.0 c/15"
        f.MostrarQuantidade = false; f.MostrarDiametro = false;
        Assert.Equal(cfg.PrefixoPosicao + "4 c/15", TextoFaixa.Formatar(f, barra, cfg));
        f.MostrarPosicao = false; f.MostrarEspacamento = false;
        Assert.Equal(cfg.PrefixoPosicao + "4", TextoFaixa.Formatar(f, barra, cfg)); // nunca vazio

        var x = CodecDados.DecodificarFaixa(CodecDados.Codificar(new DadosFaixa
        {
            MostrarQuantidade = false, MostrarEspacamento = false, LinhasApagadas = { 0, 2 },
        }));
        Assert.False(x.MostrarQuantidade || x.MostrarEspacamento);
        Assert.True(x.MostrarPosicao && x.MostrarDiametro);
        Assert.Equal(new[] { 0, 2 }, x.LinhasApagadas);
        Assert.False(x.MostraLinha(2));
        Assert.True(x.MostraLinha(1));
        var antiga = CodecDados.DecodificarFaixa(CodecDados.Codificar(new DadosFaixa()));
        Assert.True(antiga.MostrarQuantidade && antiga.MostrarPosicao && antiga.MostrarDiametro && antiga.MostrarEspacamento);
        Assert.Empty(antiga.LinhasApagadas);
        // comprimento da faixa (cota): ligado por padrão; só o desligado vai para o XData
        Assert.True(antiga.MostrarComprimento);
        Assert.False(CodecDados.DecodificarFaixa(CodecDados.Codificar(new DadosFaixa { MostrarComprimento = false })).MostrarComprimento);
        // repetições do detalhamento digitadas (0 = automático, não vai para o XData)
        Assert.Equal(0, CodecDados.DecodificarDetalhamento(CodecDados.Codificar(new DadosDetalhamento())).Repeticoes);
        Assert.Equal(5, CodecDados.DecodificarDetalhamento(CodecDados.Codificar(new DadosDetalhamento { Repeticoes = 5 })).Repeticoes);
        // patamar (padrão do escritório): só quando ligado vai para o XData
        Assert.False(antiga.Patamar);
        Assert.True(CodecDados.DecodificarFaixa(CodecDados.Codificar(new DadosFaixa { Patamar = true })).Patamar);
    }

    [Fact]
    public void RepeticoesDaFaixa()
    {
        var cfg = new ConfiguracaoArmadura();
        var barra = new DadosArmadura { Numero = 4, Bitola = 10 };
        var f = new DadosFaixa { Tipo = TipoFaixa.Ortogonal, Quantidade = 7, Espacamento = 15, Repeticoes = 2 };
        Assert.Equal(14, f.QuantidadeTotal);
        Assert.StartsWith("2x7 " + cfg.PrefixoPosicao + "4", TextoFaixa.Formatar(f, barra, cfg));
        Assert.Equal(2, CodecDados.DecodificarFaixa(CodecDados.Codificar(f)).Repeticoes);
        Assert.Equal(1, CodecDados.DecodificarFaixa(CodecDados.Codificar(new DadosFaixa())).Repeticoes);
        var ind = new DadosFaixa { Tipo = TipoFaixa.Indicacao, Repeticoes = 3 };
        Assert.Equal(0, ind.QuantidadeTotal);                                       // indicação não soma
        Assert.Equal(cfg.PrefixoPosicao + "4", TextoFaixa.Formatar(ind, barra, cfg)); // nem repete
    }

    [Fact]
    public void TelaIdentificada()
    {
        var d = new DadosArmadura { Tela = "Q-92", Quantidade = 1, Repeticoes = 2, TelaIdentificada = true, NumeroTela = 3 };
        var (l1, l2) = Descricao.FormatadorDescricao.TextoTela(d, 348, 245);
        Assert.Equal("T3 - 2x Q-92", l1);
        Assert.Equal("245 x 348", l2); // menor × maior
        Assert.Equal("T? - Q-92", Descricao.FormatadorDescricao.TextoTela(new DadosArmadura { Tela = "Q-92", Quantidade = 1 }, 100, 50).linha1);
        // painel girado (lados trocados) = mesmo T
        Assert.Equal(Descricao.FormatadorDescricao.AssinaturaTela(d, 348, 245), Descricao.FormatadorDescricao.AssinaturaTela(d, 245, 348));
        Assert.NotEqual(Descricao.FormatadorDescricao.AssinaturaTela(d, 348, 245), Descricao.FormatadorDescricao.AssinaturaTela(d, 348, 213));
        var x = CodecDados.DecodificarBarra(CodecDados.Codificar(d));
        Assert.True(x.TelaIdentificada);
        Assert.Equal(3, x.NumeroTela);
        Assert.False(CodecDados.DecodificarBarra(CodecDados.Codificar(new DadosArmadura { Tela = "Q-92" })).TelaIdentificada);
    }

    [Fact]
    public void QuebrasDeLinhaDoUsuario()
    {
        // Enter depois da 2ª palavra, com formatação do editor no meio
        var q = Descricao.QuebrasTexto.Ler("{\\fArial|b0|i0|c0|p34;4 N3}\\P%%c10.0 c/15 C=309");
        Assert.Equal(new[] { 2 }, q);
        Assert.Equal("4 N3\\P%%c10.0 c/15 C=309", Descricao.QuebrasTexto.Aplicar("4 N3 %%c10.0 c/15 C=309", q));
        // o texto muda (quantidade, C=) e a quebra continua no mesmo ponto
        Assert.Equal("12 N3\\P%%c10.0 c/15 C=410", Descricao.QuebrasTexto.Aplicar("12 N3 %%c10.0 c/15 C=410", q));
        Assert.Empty(Descricao.QuebrasTexto.Ler("4 N3 %%c10.0 c/15"));               // sem Enter: uma linha
        Assert.Equal(new[] { 1, 3 }, Descricao.QuebrasTexto.Ler("4\r\nN3 %%c10.0\nc/15"));
        Assert.Equal("4 N3", Descricao.QuebrasTexto.Aplicar("4 N3", new[] { 5 }));    // fora do texto: ignora
        Assert.Equal("T1 - Q-92\\P245 x 348", Descricao.QuebrasTexto.Aplicar("T1 - Q-92\\P245 x 348", new[] { 1 }));

        var d = CodecDados.DecodificarBarra(CodecDados.Codificar(new DadosArmadura { QuebrasDescricao = { 2 } }));
        Assert.Equal(new[] { 2 }, d.QuebrasDescricao);
        var f = CodecDados.DecodificarFaixa(CodecDados.Codificar(new DadosFaixa { Quebras = { 1 } }));
        Assert.Equal(new[] { 1 }, f.Quebras);
    }

    [Fact]
    public void EscalaDaFaixaNaConfiguracao()
    {
        var cfg = ConfiguracaoArmadura.DeJson(new ConfiguracaoArmadura { EscalaFaixa = 25 }.ParaJson());
        Assert.Equal(25, cfg.EscalaFaixa);
        Assert.Equal(0, ConfiguracaoArmadura.DeJson("{\"VersaoConfig\": 4}").EscalaFaixa);
    }
}
