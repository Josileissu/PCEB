using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Intercambio;
using DEB.Core.Persistencia;
using DEB.Core.Templates;
using DEB.Core.Templates.PilarPreMoldado;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Peças do Revit → template do pilar no CAD (2026-11-09).</summary>
[Collection("Pilar")]
public class IntercambioPecasTests
{
    private static readonly TemplatePilarPreMoldado T = new TemplatePilarPreMoldado();

    /// <summary>P61: 50×50, consolo duplo 40×23,5×25 nas faces 1 e 3, topo a 592,5 cm da base.</summary>
    private static PilarPeca P61() => new PilarPeca
    {
        Marca = "P1", B = 50, H = 50, L = 1390.5, CotaBase = -2.9, Embutimento = 100,
        Elementos = { new ElementoPeca { Id = "101", Marca = "P61" }, new ElementoPeca { Id = "102", Marca = "P62", Giro = 180 } },
        Niveis = new List<NivelPeca> { new NivelPeca { Nome = "TÉRREO", Cota = 0 }, new NivelPeca { Nome = "MEZANINO", Cota = 3.025 } },
        Consolos =
        {
            new ConsoloPeca { Face = 3, Inicio = 5, Fim = 45, Base = 569, Topo = 592.5, Balanco = 25, AlturaPonta = 23.5 },
            new ConsoloPeca { Face = 1, Inicio = 5, Fim = 45, Base = 569, Topo = 592.5, Balanco = 25, AlturaPonta = 23.5 },
        },
    };

    [Fact]
    public void ArquivoIdaEVolta()
    {
        var a = new ArquivoPecas { Origem = "Projeto1.rvt", Data = "2026-11-09T12:00:00", Pilares = { P61() }, Notas = { "pilar 7 ignorado" } };
        a.Pilares.Add(new PilarPeca { Marca = "P2", B = 40, H = 60, L = 800, Elementos = { new ElementoPeca { Id = "7" } } });
        var b = ArquivoPecas.DeJson(Json.Ler(Json.Gravar(a.ParaJson())));

        Assert.Equal("Projeto1.rvt", b.Origem);
        Assert.Equal(new[] { "pilar 7 ignorado" }, b.Notas);
        Assert.Equal(2, b.Pilares.Count);
        var p = b.Pilares[0];
        Assert.Equal(("P1", 50.0, 50.0, 1390.5, -2.9, 100.0), (p.Marca, p.B, p.H, p.L, p.CotaBase, p.Embutimento.Value));
        Assert.Equal(2, p.Quantidade);
        Assert.Equal(180, p.Elementos[1].Giro);
        Assert.Equal(new[] { "TÉRREO", "MEZANINO" }, p.Niveis.Select(n => n.Nome));
        Assert.Equal(2, p.Consolos.Count);
        Assert.Equal(23.5, p.Consolos[0].AlturaFace, 6);
        // sem embutimento e sem níveis no modelo: ficam null (vale o padrão do usuário)
        Assert.Null(b.Pilares[1].Embutimento);
        Assert.Null(b.Pilares[1].Niveis);
    }

    [Fact]
    public void ArquivoDeOutroFormatoRecusado()
    {
        var ex = Assert.Throws<FormatException>(() => ArquivoPecas.DeJson(Json.Ler("{\"formato\": \"outro\", \"versao\": 1}")));
        Assert.Contains("DEB.Pecas", ex.Message);
        Assert.Throws<FormatException>(() => ArquivoPecas.DeJson(Json.Ler(
            "{\"formato\": \"DEB.Pecas\", \"versao\": 1, \"pilares\": [{\"marca\": \"P1\", \"b\": 0, \"h\": 50, \"l\": 300}]}")));
    }

    [Fact]
    public void ConsolosIguaisEmFacesOpostasViramDuplo()
    {
        var avisos = new List<string>();
        var linhas = ConversorPilarPeca.LinhasConsolos(P61(), TipoConsolo.Padrao(), avisos);
        Assert.Empty(avisos);
        var l = Assert.Single(linhas);
        Assert.Equal(new[] { "CD 40x23,5x25", "1 e 3", "592.5", "0" }, l);
    }

    [Fact]
    public void ConsoloSimplesTrapezoidalComDeslocamento()
    {
        // seção 70 (x) × 50 (y); consolo na face 2 (−y) de x = 10 a 50: centro em 30, centro da face em 35 → −5
        var p = new PilarPeca
        {
            Marca = "P3", B = 70, H = 50, L = 900,
            Consolos = { new ConsoloPeca { Face = 2, Inicio = 10, Fim = 50, Base = 366.5, Topo = 400, Balanco = 25, AlturaPonta = 23.5 } },
        };
        var avisos = new List<string>();
        var l = Assert.Single(ConversorPilarPeca.LinhasConsolos(p, TipoConsolo.Padrao(), avisos));
        Assert.Empty(avisos);
        Assert.Equal(new[] { "CT 40x33,5-23,5x25", "2", "400", "-5" }, l);
    }

    [Fact]
    public void DeslocamentoNaConvencaoDoTemplate()
    {
        var p = new PilarPeca { B = 60, H = 40 };
        ConsoloPeca C(int face, double ini, double fim) => new ConsoloPeca { Face = face, Inicio = ini, Fim = fim };
        Assert.Equal(5, ConversorPilarPeca.Deslocamento(p, C(1, 15, 35)), 6);   // y: centro 25 − 20
        Assert.Equal(-5, ConversorPilarPeca.Deslocamento(p, C(3, 15, 35)), 6);  // face 3 conta ao contrário
        Assert.Equal(10, ConversorPilarPeca.Deslocamento(p, C(2, 30, 50)), 6);  // x: centro 40 − 30
        Assert.Equal(-10, ConversorPilarPeca.Deslocamento(p, C(4, 30, 50)), 6);
        Assert.Equal(new[] { 3, 4, 1, 2 }, new[] { 1, 2, 3, 4 }.Select(ConversorPilarPeca.Oposta));
    }

    [Fact]
    public void ConsoloForaDoCatalogoFicaDeForaComAviso()
    {
        var p = new PilarPeca
        {
            Marca = "P4", B = 50, H = 50, L = 900,
            Consolos = { new ConsoloPeca { Face = 4, Inicio = 0, Fim = 50, Base = 300, Topo = 340, Balanco = 35, AlturaPonta = 40 } },
        };
        var avisos = new List<string>();
        Assert.Empty(ConversorPilarPeca.LinhasConsolos(p, TipoConsolo.Padrao(), avisos));
        var a = Assert.Single(avisos);
        Assert.Contains("P4", a);
        Assert.Contains("face 4", a);
        Assert.Contains("ARMCONSOLOS", a);
    }

    [Fact]
    public void ValoresDoTemplateGeramOPilar()
    {
        var avisos = new List<string>();
        var v = ConversorPilarPeca.Valores(P61(), new ValoresTemplate(T.Parametros), TipoConsolo.Padrao(), avisos);
        Assert.Empty(avisos);
        Assert.Equal("P1", v["Nome"]);
        Assert.Equal("2", v["Quantidade"]);
        Assert.Equal(("50", "50", "1390.5", "-2.9", "100"), (v["B"], v["H"], v["L"], v["CotaBase"], v["Lemb"]));
        Assert.Equal("TÉRREO||0;MEZANINO||3.025", v["Niveis"]);
        Assert.Empty(CatalogoTemplates.Validar(T, v));

        var r = T.Gerar(v, new ConfiguracaoArmadura { AlturaTextoMm = 2 });
        Assert.Equal("P1", r.Detalhamento);
        Assert.DoesNotContain(r.Avisos, a => a.Contains("catálogo") || a.Contains("fora do pilar"));
        Assert.NotEmpty(r.Barras);
    }

    [Fact]
    public void SemNiveisNemEmbutimentoFicaOPadrao()
    {
        var padrao = new ValoresTemplate(T.Parametros);
        var p = new PilarPeca { Marca = "P9", B = 40, H = 40, L = 600 };
        var v = ConversorPilarPeca.Valores(p, padrao, TipoConsolo.Padrao(), new List<string>());
        Assert.Equal(padrao["Niveis"], v["Niveis"]);
        Assert.Equal(padrao["Lemb"], v["Lemb"]);
        Assert.Equal("", v["Consolos"]);   // o padrão tem um consolo; a peça não tem nenhum
        Assert.Equal("1", v["Quantidade"]);
    }
}
