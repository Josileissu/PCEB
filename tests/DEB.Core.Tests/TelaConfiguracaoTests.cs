using System.IO;
using System.Linq;
using DEB.Core.Configuracao.Projeto;
using DEB.Core.Persistencia;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Sessão da tela de configuração (D45): mensagens da página → configuração → estado devolvido.</summary>
public class TelaConfiguracaoTests
{
    private static ValorJson Campo(string estado, string id) => Json.Ler(estado)["campos"].Itens.First(c => c["id"].Texto == id);

    [Fact]
    public void PaginaEstaEmbutidaEFalaComOHospedeiro()
    {
        string html = SessaoConfiguracao.Pagina();
        Assert.Contains("chrome.webview", html);
        Assert.Contains("Configuração do projeto", html);
    }

    [Fact]
    public void DefinirNumeroComVirgulaGravaInvarianteEMostraOrigem()
    {
        var s = new SessaoConfiguracao(new ConfiguracaoProjeto());
        string e = s.Receber("{\"acao\":\"definir\",\"nivel\":\"Projeto\",\"id\":\"materiais.fck\",\"valor\":\"45,5\"}");
        var c = Campo(e, "materiais.fck");
        Assert.Equal("45.5", c["valor"].Texto);
        Assert.Equal("Projeto", c["origem"].Texto);
        Assert.Equal("40", c["herdado"].Texto);
        Assert.True(c["alterado"].Booleano);
        Assert.True(s.Alterado);
        Assert.Equal(1, Json.Ler(e)["grupos"].Itens.First(g => g["id"].Texto == "materiais")["alterados"].Numero);
        // número inválido: erro e nada muda
        e = s.Receber("{\"acao\":\"definir\",\"nivel\":\"Projeto\",\"id\":\"materiais.fck\",\"valor\":\"abc\"}");
        Assert.NotNull(Json.Ler(e)["erro"]);
        Assert.Equal(45.5, s.Cfg.Numero("materiais.fck"));
    }

    [Fact]
    public void NivelDoEscritorioNaoEnxergaOProjeto()
    {
        var cfg = new ConfiguracaoProjeto();
        cfg.Definir(NivelConfiguracao.Projeto, "durab.caa", "III");
        var s = new SessaoConfiguracao(cfg);
        var c = Campo(s.Receber("{\"acao\":\"estado\",\"nivel\":\"Escritorio\"}"), "durab.caa");
        Assert.Equal("II", c["valor"].Texto);
        // gravar no escritório o mesmo valor que o projeto tem: grava (o herdado do escritório é o padrão "II")
        s.Receber("{\"acao\":\"definir\",\"nivel\":\"Escritorio\",\"id\":\"durab.caa\",\"valor\":\"III\"}");
        Assert.Equal("III", cfg.Escritorio.Valores["durab.caa"]);
    }

    [Fact]
    public void ForaDaNormaPedeJustificativaEPromoverLevaAoEscritorio()
    {
        var s = new SessaoConfiguracao(new ConfiguracaoProjeto());
        string e = s.Receber("{\"acao\":\"definir\",\"nivel\":\"Projeto\",\"id\":\"normas.gamaC\",\"valor\":\"1,3\"}");
        Assert.True(Campo(e, "normas.gamaC")["fora"].Booleano);
        Assert.Single(Json.Ler(e)["pendencias"].Itens);
        e = s.Receber("{\"acao\":\"justificar\",\"nivel\":\"Projeto\",\"id\":\"normas.gamaC\",\"justificativa\":\"controle de fábrica\"}");
        Assert.True(Campo(e, "normas.gamaC")["justificado"].Booleano);
        Assert.Empty(Json.Ler(e)["pendencias"].Itens);
        // mudar o valor mantém a justificativa dada no nível
        s.Receber("{\"acao\":\"definir\",\"nivel\":\"Projeto\",\"id\":\"normas.gamaC\",\"valor\":\"1.35\"}");
        Assert.Equal("controle de fábrica", s.Cfg.Projeto.Justificativas["normas.gamaC"]);
        s.Receber("{\"acao\":\"promover\",\"nivel\":\"Projeto\"}");
        Assert.Empty(s.Cfg.Projeto.Valores);
        Assert.Equal("1.35", s.Cfg.Escritorio.Valores["normas.gamaC"]);
        Assert.Equal("controle de fábrica", s.Cfg.Escritorio.Justificativas["normas.gamaC"]);
    }

    [Fact]
    public void SistemaRestaurarGrupoESalvar()
    {
        var s = new SessaoConfiguracao(new ConfiguracaoProjeto());
        s.Receber("{\"acao\":\"sistema\",\"nivel\":\"Projeto\",\"sistema\":\"Técnico de prancha · kgf, cm\"}");
        Assert.Equal("kgf·cm", s.Cfg.Unidade(Grandeza.Torcao));
        s.Receber("{\"acao\":\"restaurarGrupo\",\"nivel\":\"Projeto\",\"grupo\":\"unid\"}");
        Assert.Equal("tf", s.Cfg.Unidade(Grandeza.Forca));     // padrão do PCEB
        Assert.Null(s.Fim);
        s.Receber("{\"acao\":\"salvar\",\"nivel\":\"Projeto\"}");
        Assert.Equal("salvar", s.Fim);
        // sem projeto aberto: tudo vai ao escritório
        var so = new SessaoConfiguracao(new ConfiguracaoProjeto()) { ComProjeto = false };
        so.Receber("{\"acao\":\"definir\",\"nivel\":\"Projeto\",\"id\":\"materiais.fck\",\"valor\":\"50\"}");
        Assert.Equal("50", so.Cfg.Escritorio.Valores["materiais.fck"]);
    }

    [Fact]
    public void Campo_numerico_aparece_e_entra_na_unidade_escolhida()
    {
        var s = new SessaoConfiguracao(new ConfiguracaoProjeto());
        // padrão do PCEB: peso específico em kgf/m³ → 25 kN/m³ aparece como 2549,3 kgf/m³
        string e = s.Receber("{\"acao\":\"estado\",\"nivel\":\"Projeto\"}");
        Assert.Contains("\"unidade\":\"kgf/m³\"", e);
        Assert.Contains("2549.29", e);
        // digitado em kgf/m³, guardado em kN/m³
        s.Receber("{\"acao\":\"definir\",\"nivel\":\"Projeto\",\"id\":\"materiais.pesoEspecifico\",\"valor\":\"2500\"}");
        Assert.Equal(24.516625, s.Cfg.Numero("materiais.pesoEspecifico"), 5);
        // trocando a unidade para kN/m³ a tela mostra o número guardado
        s.Receber("{\"acao\":\"definir\",\"nivel\":\"Projeto\",\"id\":\"unid.PesoEspecifico\",\"valor\":\"kN/m³\"}");
        Assert.Contains("24.516625", s.Receber("{\"acao\":\"estado\",\"nivel\":\"Projeto\"}"));
        Assert.Equal(78.5, s.Cfg.Numero("materiais.pesoCA50"));
        Assert.Equal(78.5, s.Cfg.Numero("materiais.pesoCP190"));
    }

    [Fact]
    public void ArquivoDoEscritorioVaiEVoltaEIgnoraCampoAntigo()
    {
        string arq = Path.Combine(Path.GetTempPath(), "pceb_esc_" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var n = new NivelValores { Nivel = NivelConfiguracao.Escritorio };
            n.Valores["materiais.fck"] = "35";
            n.Valores["campo.queSaiu"] = "x";
            ArquivoEscritorio.Gravar(n, arq);
            ArquivoEscritorio.Gravar(n, arq);   // segunda gravação substitui
            var lido = ArquivoEscritorio.Ler(out string aviso, arq);
            Assert.Equal("35", lido.Valores["materiais.fck"]);
            Assert.False(lido.Valores.ContainsKey("campo.queSaiu"));
            Assert.Contains("campo.queSaiu", aviso);
            File.WriteAllText(arq, "{ quebrado");
            Assert.Empty(ArquivoEscritorio.Ler(out aviso, arq).Valores);
            Assert.NotNull(aviso);
        }
        finally { File.Delete(arq); }
    }
}
