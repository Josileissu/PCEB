using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Elementos.Consolo;
using DEB.Core.Elementos.Pilar.PreMoldado;
using DEB.Core.Elementos.Viga.PreMoldada;
using DEB.Core.Intercambio;
using DEB.Core.Motor;
using DEB.Core.Normas.Verificacao;
using DEB.Core.Persistencia;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Apoio no consolo e furos dos pinos da viga (D36) do modelo até o CAD, e as verificações C3–C5.</summary>
public class ApoioFurosTests
{
    private static ApoioPeca Neoprene() => new ApoioPeca { Tipo = ApoioPeca.Neoprene, Comprimento = 15, Largura = 15, Espessura = 1.5, Afastamento = 2.5 };

    private static PilarPeca P61(ApoioPeca a) => new PilarPeca
    {
        Marca = "P1", B = 50, H = 50, L = 1390.5, CotaBase = -2.9, Elementos = { new ElementoPeca { Id = "101" } },
        Consolos =
        {
            new ConsoloPeca { Face = 3, Inicio = 5, Fim = 45, Base = 569, Topo = 592.5, Balanco = 25, AlturaPonta = 23.5, Apoio = a },
            new ConsoloPeca { Face = 1, Inicio = 5, Fim = 45, Base = 569, Topo = 592.5, Balanco = 25, AlturaPonta = 23.5, Apoio = a },
        },
    };

    [Fact]
    public void ArquivoLevaApoioEFuros()
    {
        var a = new ArquivoPecas { Pilares = { P61(Neoprene()) } };
        a.Vigas.Add(new VigaPeca { Marca = "V1", B = 40, H = 50, L = 600, DenteE = new DentePeca { Altura = 25, Comprimento = 25 },
                                   Furos = new FurosPeca { Quantidade = 2, DistE = 12.5, DistD = 11, Diametro = 32 }, Elementos = { new ElementoPeca { Id = "9" } } });
        var b = ArquivoPecas.DeJson(Json.Ler(Json.Gravar(a.ParaJson())));
        var ap = b.Pilares[0].Consolos[0].Apoio;
        Assert.Equal((1, 15.0, 15.0, 1.5, 2.5), (ap.Tipo, ap.Comprimento, ap.Largura, ap.Espessura, ap.Afastamento));
        var f = b.Vigas[0].Furos;
        Assert.Equal((2, 12.5, 11.0, 32.0), (f.Quantidade, f.DistE, f.DistD, f.Diametro));
        // arquivo sem apoio e sem furos (antes da D36) continua igual
        var c = ArquivoPecas.DeJson(Json.Ler(Json.Gravar(new ArquivoPecas { Pilares = { P61(null) } }.ParaJson())));
        Assert.Null(c.Pilares[0].Consolos[0].Apoio);
    }

    [Fact]
    public void ImportacaoPoeOApoioNaTabelaDoPilarEAVerificacaoUsa()
    {
        var avisos = new List<string>();
        var linhas = ConversorPilarPeca.LinhasConsolos(P61(Neoprene()), TipoConsolo.Padrao(), avisos);
        var l = Assert.Single(linhas);                                       // duplo
        Assert.Equal(new[] { "Neoprene", "15", "15", "1.5", "2.5" }, l.Skip(4).ToArray());
        var t = new TemplatePilarPreMoldado();
        var v = ConversorPilarPeca.Valores(P61(Neoprene()), new ValoresTemplate(t.Parametros), TipoConsolo.Padrao(), avisos);
        var r = t.Gerar(v, new ConfiguracaoArmadura { AlturaTextoMm = 2 });
        // tirante com barra soldada: a2 ≥ c + φ = 3,5 + 1,6 = 5,1 > 2,5 → não atende
        Assert.Contains(r.Verificacoes, x => x.Codigo == "C3" && x.Situacao == SituacaoVerificacao.NaoAtende);
        Assert.Contains(r.Avisos, x => x.StartsWith("Não atende (C3)"));
        // linha sem as colunas do apoio (tabelas antigas) é lida sem apoio
        var semApoio = ConversorPilarPeca.Valores(P61(null), new ValoresTemplate(t.Parametros), TipoConsolo.Padrao(), avisos);
        Assert.DoesNotContain(t.Gerar(semApoio, new ConfiguracaoArmadura { AlturaTextoMm = 2 }).Verificacoes, x => x.Codigo == "C3");
    }

    [Fact]
    public void FurosDoModeloViramQuantidadeDigitada()
    {
        var T = new TemplateVigaPreMoldada();
        var p = new VigaPeca { Marca = "V7", B = 40, H = 75, L = 466, DenteE = new DentePeca { Altura = 45, Comprimento = 25 },
                               Furos = new FurosPeca { Quantidade = 1, DistE = 12.5, DistD = 12.5, Diametro = 40 }, Elementos = { new ElementoPeca() } };
        var v = ConversorVigaPeca.Valores(p, new ValoresTemplate(T.Parametros), TipoDente.Padrao(), new List<string>());
        Assert.Equal((TemplateVigaPreMoldada.FurosDigitados, "1", "0", "12.5", "40"), (v["FurosModo"], v["FurosE"], v["FurosD"], v["FuroDistE"], v["FuroDiam"]));
        Assert.Empty(CatalogoTemplates.Validar(T, v));
    }

    private static DadosConsolo Consolo(bool alca, double a2) => new DadosConsolo
    {
        Nome = "C", B = 40, A = 25, H = 30, H1 = 30, Cob = 2.5, TirN = 4, TirPhi = 12.5, TirAlca = alca, CostN = 4, CostPhi = 8,
        VertN = 5, VertPhi = 6.3, Apoio = new ApoioPeca { Tipo = 1, Comprimento = 15, Largura = 30, Espessura = 1.5, Afastamento = a2 },
    };

    [Fact]
    public void C3C4C5PeloApoio()
    {
        // soldada: a2 ≥ 2,5 + 1,25 = 3,75
        Assert.Equal(SituacaoVerificacao.Ok, VerificacoesConsolo.Verificar(Consolo(false, 4)).First(x => x.Codigo == "C3").Situacao);
        Assert.Equal(SituacaoVerificacao.NaoAtende, VerificacoesConsolo.Verificar(Consolo(false, 3)).First(x => x.Codigo == "C3").Situacao);
        // alça φ < 20: a2 ≥ 2,5 + 3,5·1,25 = 6,875; e C5: entre 2,5 e 6,25 → a2 = 7 passa C3 e sai de C5 (conferir)
        var r = VerificacoesConsolo.Verificar(Consolo(true, 7));
        Assert.Equal(SituacaoVerificacao.Ok, r.First(x => x.Codigo == "C3").Situacao);
        Assert.Equal(SituacaoVerificacao.Conferir, r.First(x => x.Codigo == "C5").Situacao);
        // C4: lateral (40 − 30)/2 = 5 > 2,5 → conferir o fendilhamento
        Assert.Equal(SituacaoVerificacao.Conferir, r.First(x => x.Codigo == "C4").Situacao);
        // C2 com a do centro do apoio: 25 − 7 − 7,5 = 10,5
        Assert.Contains("10,5", r.First(x => x.Codigo == "C2").Valor);
    }
}
