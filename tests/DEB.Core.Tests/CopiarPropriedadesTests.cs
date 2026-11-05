using DEB.Core.Modelo;
using DEB.Core.RelacaoAco;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>MATCHPROP do plugin (pedido do usuário, 2026-11-05): o que passa de um objeto para o outro.</summary>
public class CopiarPropriedadesTests
{
    [Fact]
    public void Barra_copia_dados_e_mantem_geometria_partes_e_identidade()
    {
        var origem = new DadosArmadura
        {
            Bitola = 16, Aco = "CA-60", Quantidade = 7, Repeticoes = 2, Espacamento = 12.5, Estribo = true,
            Corrido = true, Visivel = false, Campos = CamposDescricao.Numero, Escala = 25, Camadas = "2",
        };
        var destino = new DadosArmadura { Numero = 3, HandleDescricao = "2AF", Bitola = 10 };
        destino.Segmentos.Add(new Segmento { ComprimentoRegistrado = 300, HandleCota = "2B0" });
        destino.Faixas.Add("3C1");
        var id = destino.Id;

        destino.CopiarPropriedadesDe(origem);

        Assert.Equal(16, destino.Bitola);
        Assert.Equal("CA-60", destino.Aco);
        Assert.Equal(7, destino.Quantidade);
        Assert.Equal(2, destino.Repeticoes);
        Assert.Equal(12.5, destino.Espacamento);
        Assert.True(destino.Estribo && destino.Corrido && !destino.Visivel);
        Assert.Equal(CamposDescricao.Numero, destino.Campos);
        Assert.Equal(25, destino.Escala);
        Assert.Equal("2", destino.Camadas);
        // não muda: identidade, N, geometria e partes
        Assert.Equal(id, destino.Id);
        Assert.Equal(3, destino.Numero);
        Assert.Equal("2AF", destino.HandleDescricao);
        Assert.Single(destino.Segmentos);
        Assert.Equal(300, destino.Segmentos[0].ComprimentoRegistrado);
        Assert.Equal("2B0", destino.Segmentos[0].HandleCota);
        Assert.Equal(new[] { "3C1" }, destino.Faixas);
    }

    [Fact]
    public void Barra_comum_nao_vira_trelica()
    {
        var destino = new DadosArmadura();
        destino.CopiarPropriedadesDe(new DadosArmadura { Trelica = "TG8L" });
        Assert.True(destino.EhBarraComum);

        var trelica = new DadosArmadura { Trelica = "TG8L" };
        trelica.CopiarPropriedadesDe(new DadosArmadura { Trelica = "TG12M" });
        Assert.Equal("TG12M", trelica.Trelica);
    }

    [Fact]
    public void Faixa_copia_opcoes_e_mantem_barra_tipo_e_pontos()
    {
        var origem = new DadosFaixa
        {
            Tipo = TipoFaixa.CorteChamada, Espacamento = 10, EspacamentoInicial = 0.5, Escala = 25, Repeticoes = 2,
            MostrarQuantidade = false, MostrarPosicao = false, MostrarDiametro = false, MostrarEspacamento = false,
            OcultarLinhas = true, Patamar = true,
        };
        var destino = new DadosFaixa { Tipo = TipoFaixa.CorteChamadaArco, HandleBarra = "1A", P1X = 5, Espacamento = 20 };
        destino.Partes.Add("1B");
        destino.LinhasApagadas.Add(0);

        destino.CopiarPropriedadesDe(origem);

        Assert.Equal(10, destino.Espacamento);
        Assert.Equal(0.5, destino.EspacamentoInicial);
        Assert.Equal(25, destino.Escala);
        Assert.Equal(2, destino.Repeticoes);
        Assert.False(destino.MostrarQuantidade || destino.MostrarPosicao || destino.MostrarDiametro || destino.MostrarEspacamento);
        Assert.True(destino.OcultarLinhas && destino.Patamar);
        Assert.Equal(TipoFaixa.CorteChamadaArco, destino.Tipo);
        Assert.Equal("1A", destino.HandleBarra);
        Assert.Equal(5, destino.P1X);
        Assert.Equal(new[] { "1B" }, destino.Partes);
        Assert.Equal(new[] { 0 }, destino.LinhasApagadas);
    }

    [Fact]
    public void Indicacao_recebe_campos_do_texto_pelos_Indicar()
    {
        var origem = new DadosFaixa { Tipo = TipoFaixa.CorteChamada }; // Mostrar… ligados (padrão)
        var indicacao = new DadosFaixa { Tipo = TipoFaixa.Indicacao, Espacamento = 15 };

        indicacao.CopiarPropriedadesDe(origem);

        Assert.True(indicacao.IndicarQuantidade && indicacao.IndicarDiametro && indicacao.IndicarEspacamento);
        Assert.Equal(15, indicacao.Espacamento); // indicação não distribui: espaçamento não muda
    }

    [Fact]
    public void Chamada_copia_quantidade_digitada_so_de_outra_chamada()
    {
        var chamada = new DadosFaixa { Tipo = TipoFaixa.Chamada, Quantidade = 4 };
        chamada.CopiarPropriedadesDe(new DadosFaixa { Tipo = TipoFaixa.Chamada, Quantidade = 9 });
        Assert.Equal(9, chamada.Quantidade);

        chamada.CopiarPropriedadesDe(new DadosFaixa { Tipo = TipoFaixa.CorteChamada, Quantidade = 30 });
        Assert.Equal(9, chamada.Quantidade);
    }

    [Fact]
    public void Faixas_em_cota_ortogonal_e_alinhada_nascem_sem_diametro()
    {
        Assert.False(DadosFaixa.MostrarDiametroAoCriar(TipoFaixa.Ortogonal));
        Assert.False(DadosFaixa.MostrarDiametroAoCriar(TipoFaixa.Alinhada));
        Assert.True(DadosFaixa.MostrarDiametroAoCriar(TipoFaixa.CorteCotaOrtogonal));
        Assert.True(DadosFaixa.MostrarDiametroAoCriar(TipoFaixa.CorteChamada));
        // faixas existentes (sem "MDIA" no XData) continuam mostrando o diâmetro
        Assert.True(new DadosFaixa { Tipo = TipoFaixa.Ortogonal }.MostrarDiametro);
    }

    [Fact]
    public void Tabela_copia_apresentacao_e_mantem_o_que_lista()
    {
        var origem = new OpcoesTabelaRA
        {
            ColunaElemento = ColunaElemento.Sempre, NomeNoTitulo = true, Apresentacao = ApresentacaoRA.SomenteTabela,
            MostrarPesoTotal = false, Escala = 50, DetalheDobra = true,
        };
        origem.Blocos.Add("V1");
        var destino = new OpcoesTabelaRA { PosX = 7 };
        destino.Blocos.Add("P2");
        destino.Partes.Add("4D");

        destino.CopiarPropriedadesDe(origem);

        Assert.Equal(ColunaElemento.Sempre, destino.ColunaElemento);
        Assert.True(destino.NomeNoTitulo && destino.DetalheDobra && !destino.MostrarPesoTotal);
        Assert.Equal(ApresentacaoRA.SomenteTabela, destino.Apresentacao);
        Assert.Equal(50, destino.Escala);
        Assert.Equal(new[] { "P2" }, destino.Blocos);
        Assert.Equal(new[] { "4D" }, destino.Partes);
        Assert.Equal(7, destino.PosX);
    }
}
