using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Pecas;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Conferência e subdivisão das peças, etapa 1 (núcleo): seção poligonal, trechos, nome do tipo (2026-11-09).</summary>
public class SubdivisaoPecasTests
{
    private static SecaoPoligonal Poli(params (double Y, double Z)[] p) => new SecaoPoligonal { Contorno = p.ToList() };

    /// <summary>"Seção retangular com aba laje 26x170" do Projeto1: alma 26 × 170 e aba de 25 × 20 embaixo, à direita.</summary>
    private static SecaoPoligonal ComAba() => Poli((0, 0), (51, 0), (51, 20), (26, 20), (26, 170), (0, 170));

    [Fact]
    public void PropriedadesDoRetangulo()
    {
        var s = SecaoPoligonal.Retangulo(20, 50);
        Assert.Equal(1000, s.Area, 6);
        Assert.Equal((10.0, 25.0), s.CG);
        Assert.Equal(20 * 50.0 * 50 * 50 / 12, s.Iy, 3);   // b h³ / 12
        Assert.Equal(50 * 20.0 * 20 * 20 / 12, s.Iz, 3);
        // sentido do contorno não importa
        var hor = Poli((0, 0), (0, 50), (20, 50), (20, 0));
        Assert.Equal(s.Iy, hor.Iy, 3);
        Assert.Equal(s.Area, hor.Area, 6);
    }

    [Fact]
    public void PropriedadesComAbaEFuro()
    {
        var s = ComAba();
        Assert.Equal(26 * 170 + 25 * 20, s.Area, 6);
        // CG pela soma das partes
        double a1 = 26 * 170, a2 = 25 * 20;
        Assert.Equal((a1 * 13 + a2 * 38.5) / (a1 + a2), s.CG.Y, 6);
        Assert.Equal((a1 * 85 + a2 * 10) / (a1 + a2), s.CG.Z, 6);
        double zg = s.CG.Z;
        double iy = 26 * 170.0 * 170 * 170 / 12 + a1 * (85 - zg) * (85 - zg) + 25 * 20.0 * 20 * 20 / 12 + a2 * (10 - zg) * (10 - zg);
        Assert.Equal(iy, s.Iy, 1);
        // furo de pino ø4 (quadrado equivalente 4 × 4 aqui) desconta área
        s.Furos.Add(new List<(double, double)> { (5, 100), (9, 100), (9, 104), (5, 104) });
        Assert.Equal(26 * 170 + 25 * 20 - 16, s.Area, 6);
    }

    [Fact]
    public void ClassificaAsFormas()
    {
        Assert.Equal(TipoSecao.Retangular, SecaoPoligonal.Retangulo(20, 50).Classificar().Tipo);
        var (t, d) = ComAba().Classificar();
        Assert.Equal(TipoSecao.L, t);
        Assert.Contains("alma 26", d);
        Assert.Contains("aba inferior à direita 25 × 20", d);
        Assert.Equal(26, ComAba().LarguraAlma, 6);
        // T invertido (alma 15 centrada numa mesa 35 × 20 embaixo) e T
        Assert.Equal(TipoSecao.TInvertido, Poli((0, 0), (35, 0), (35, 20), (25, 20), (25, 60), (10, 60), (10, 20), (0, 20)).Classificar().Tipo);
        Assert.Equal(TipoSecao.T, Poli((10, 0), (25, 0), (25, 40), (35, 40), (35, 60), (0, 60), (0, 40), (10, 40)).Classificar().Tipo);
        // I
        var i = Poli((0, 0), (30, 0), (30, 10), (20, 10), (20, 50), (30, 50), (30, 60), (0, 60), (0, 50), (10, 50), (10, 10), (0, 10));
        Assert.Equal(TipoSecao.I, i.Classificar().Tipo);
        // trapézio (lado inclinado) e U = poligonal
        Assert.Equal(TipoSecao.Poligonal, Poli((0, 0), (30, 0), (25, 50), (5, 50)).Classificar().Tipo);
        var u = Poli((0, 0), (40, 0), (40, 50), (30, 50), (30, 10), (10, 10), (10, 50), (0, 50));
        Assert.Null(u.Faixas());
        Assert.Equal(TipoSecao.Poligonal, u.Classificar().Tipo);
    }

    [Fact]
    public void IntervalosNosVertices()
    {
        // dentes de 25 nas pontas e aba de 300 a 420, vértices repetidos e quase iguais
        var iv = Subdivisao.Intervalos(new[] { 0, 25, 25.2, 300, 420, 521, 546, 546 }, 546);
        Assert.Equal(new[] { 0.0, 25, 300, 420, 521 }, iv.Select(x => x.X0));
        Assert.Equal(546, iv.Last().X1, 6);
        Assert.Equal(12.5, iv[0].Meio, 6);
    }

    [Fact]
    public void JuntaTrechosIguaisESugereAFuncao()
    {
        var dente = SecaoPoligonal.Retangulo(20, 25, 0, 25);
        var cheia = SecaoPoligonal.Retangulo(20, 50);
        var aba = Poli((0, 0), (45, 0), (45, 20), (20, 20), (20, 50), (0, 50));
        var tr = Subdivisao.Juntar(new[]
        {
            (0.0, 25.0, dente), (25.0, 150.0, cheia), (150.0, 300.0, cheia), (300.0, 420.0, aba), (420.0, 521.0, cheia), (521.0, 546.0, dente),
        });
        Assert.Equal(new[] { 0.0, 25, 300, 420, 521 }, tr.Select(t => t.X0));
        Assert.Equal(new[] { FuncaoTrecho.Dente, FuncaoTrecho.SemApoio, FuncaoTrecho.ApoioLaje, FuncaoTrecho.SemApoio, FuncaoTrecho.Dente },
                     tr.Select(t => t.FuncaoSugerida));
        Assert.All(tr, t => Assert.Equal("", t.Funcao));   // o usuário confirma
    }

    [Fact]
    public void ChanfroNaPontaNaoFazOConsoloTrapezoidal()
    {
        // Teste_PM: consolo 23,5 × 25 com chanfro 2 × 2 na aresta inferior da ponta → retangular, ponta 23,5 (antes: 21,5 = trapezoidal)
        var m = PerfilConsolo.Medir(new[] { (0.0, 550.0), (23.0, 550.0), (25.0, 552.0), (25.0, 573.5), (0.0, 573.5) });
        Assert.Equal(23.5, m.AlturaNaFace, 6);
        Assert.Equal(23.5, m.AlturaPonta, 6);
        Assert.Equal((2.0, 2.0), (Math.Round(m.ChanfroInferior.D, 6), Math.Round(m.ChanfroInferior.C, 6)));
        Assert.Equal((0.0, 0.0), m.ChanfroSuperior);
        // trapezoidal de verdade (33,5 na face, 23,5 na ponta): o fundo inclinado ocupa todo o balanço
        var t = PerfilConsolo.Medir(new[] { (0.0, 0.0), (25.0, 10.0), (25.0, 33.5), (0.0, 33.5) });
        Assert.Equal(23.5, t.AlturaPonta, 6);
        Assert.Equal((0.0, 0.0), t.ChanfroInferior);
        // trapezoidal com chanfro: a reta principal prolongada até a ponta
        var tc = PerfilConsolo.Medir(new[] { (0.0, 0.0), (23.0, 9.2), (25.0, 12.0), (25.0, 33.5), (0.0, 33.5) });
        Assert.Equal(23.5, tc.AlturaPonta, 6);
    }

    [Fact]
    public void TrechosDoPilarComConsolo()
    {
        var corrente = SecaoPoligonal.Retangulo(40, 40);
        var comConsolo = Poli((0, 0), (40, 0), (40, 10), (65, 10), (65, 30), (40, 30), (40, 40), (0, 40));
        var tr = Subdivisao.Juntar(new[] { (0.0, 550.0, corrente), (550.0, 573.5, comConsolo), (573.5, 600.0, corrente) }, TipoPeca.Pilar);
        Assert.Equal(new[] { FuncaoTrecho.Corrente, FuncaoTrecho.Consolo, FuncaoTrecho.Corrente }, tr.Select(t => t.FuncaoSugerida));
    }

    [Fact]
    public void PilaresGiradosSaoIguais()
    {
        SecaoPoligonal Perfil() => Poli((0, 550), (23, 550), (25, 552), (25, 573.5), (0, 573.5));
        PecaLevantada Pilar(string id, params (int Face, double S0, double S1)[] cs)
        {
            var p = new PecaLevantada { Id = id, Tipo = TipoPeca.Pilar, Bx = 40, By = 40, Comprimento = 600, PreMoldada = true,
                Trechos = Subdivisao.Juntar(new[] { (0.0, 600.0, SecaoPoligonal.Retangulo(40, 40)) }, TipoPeca.Pilar) };
            foreach (var (f, a, b) in cs) p.Consolos.Add(new ConsoloLevantado { Face = f, S0 = a, S1 = b, Perfil = Perfil() });
            ConferenciaPecas.Avaliar(p);
            return p;
        }
        // consolos nas faces 2 e 3 (Teste_PM P1); girado 90°: faces 1 e 2; 180°: 4 e 1; e um com as faces 1 e 3 (outro)
        var g = ConferenciaPecas.Agrupar(new[]
        {
            Pilar("1", (2, 10, 30), (3, 10, 30)), Pilar("2", (1, 10, 30), (2, 10, 30)), Pilar("3", (4, 10, 30), (1, 10, 30)),
            Pilar("4", (1, 10, 30), (3, 10, 30)),
        });
        Assert.Equal(2, g.Count);
        Assert.Equal(3, g.Max(x => x.Quantidade));
    }

    [Fact]
    public void VigaQueRecebeOutraVigaNoConsolo()
    {
        // trecho com consolo de apoio de outra viga (função marcada no levantamento): aviso próprio e "o CAD ainda não desenha"
        var cheia = SecaoPoligonal.Retangulo(19, 75);
        var comConsolo = Poli((0, 0), (19, 0), (19, 30), (44, 30), (44, 55), (19, 55), (19, 75), (0, 75));
        var p = new PecaLevantada { Tipo = TipoPeca.Viga, TipoRevit = "19x75", PreMoldada = true, Comprimento = 568,
            Trechos = Subdivisao.Juntar(new[] { (0.0, 250.0, cheia), (250.0, 290.0, comConsolo), (290.0, 568.0, cheia) }) };
        p.Trechos[1].FuncaoSugerida = FuncaoTrecho.ApoioViga;
        ConferenciaPecas.Avaliar(p);
        Assert.Equal(SituacaoPeca.NaoSuportada, p.Situacao);
        Assert.Contains(p.Avisos, a => a.StartsWith("trecho 250–290: consolo de apoio de outra viga"));
        Assert.DoesNotContain(p.Avisos, a => a.StartsWith("nome do tipo"));
    }

    [Fact]
    public void NomeDoTipoContraAGeometria()
    {
        Assert.Equal((26.0, 170.0), NomeTipo.Dimensoes("Viga Pré-Moldada - seção retangular com aba laje : 26x170"));
        Assert.Equal((12.5, 40.0), NomeTipo.Dimensoes("Pilar 12,5 X 40 cm"));
        Assert.Null(NomeTipo.Dimensoes("Consolo"));
        // com aba: o nome dá a alma × h — bate
        Assert.Null(NomeTipo.Conferir("seção retangular com aba laje : 26x170", ComAba()));
        // mísula com o fundo inclinado (Projeto1): poligonal, mas o nome confere com a alma
        var misula = Poli((0, 0), (26, 0), (26, 100), (51, 110), (51, 125), (26, 125), (26, 170), (0, 170));
        Assert.Equal(TipoSecao.Poligonal, misula.Classificar().Tipo);
        Assert.Equal(26, misula.LarguraAlma, 6);
        Assert.Null(NomeTipo.Conferir("seção retangular com aba laje : 26x170", misula));
        // retangular nas duas ordens
        Assert.Null(NomeTipo.Conferir("40x20", SecaoPoligonal.Retangulo(20, 40)));
        // nome errado
        var msg = NomeTipo.Conferir("Viga 20x50", SecaoPoligonal.Retangulo(20, 60));
        Assert.NotNull(msg);
        Assert.Contains("20 × 60", msg);
    }
    private static PecaLevantada Viga(string id, string marca, string tipo, bool invertida = false, double hDente = 25)
    {
        // 20 x 50, L = 551, dente 25 x 25 na esquerda e aba de laje (45 de largura total) de 300 a 420
        var dente = SecaoPoligonal.Retangulo(20, 50 - hDente, 0, hDente);
        var cheia = SecaoPoligonal.Retangulo(20, 50);
        var aba = Poli((0, 0), (45, 0), (45, 20), (20, 20), (20, 50), (0, 50));
        var c = new List<(double, double, SecaoPoligonal)> { (0, 25, dente), (25, 300, cheia), (300, 420, aba), (420, 551, cheia) };
        if (invertida)
        {
            var abaInv = Poli((-25, 0), (20, 0), (20, 50), (0, 50), (0, 20), (-25, 20));   // girada 180°: a aba vai para o outro lado
            c = new List<(double, double, SecaoPoligonal)> { (0, 131, cheia), (131, 251, abaInv), (251, 526, cheia), (526, 551, dente) };
        }
        var p = new PecaLevantada { Id = id, Tipo = TipoPeca.Viga, Marca = marca, Familia = "Viga PM", TipoRevit = tipo, PreMoldada = true,
                                    Comprimento = 551, Trechos = Subdivisao.Juntar(c) };
        ConferenciaPecas.Avaliar(p);
        return p;
    }

    [Fact]
    public void AvaliaASituacaoDaPeca()
    {
        var v = Viga("1", "V1", "20x50");
        Assert.Equal(SituacaoPeca.NaoSuportada, v.Situacao);      // trecho com aba: o CAD ainda não desenha
        Assert.Contains(v.Avisos, a => a.StartsWith("trecho 300–420: L"));
        var reta = new PecaLevantada { Tipo = TipoPeca.Viga, TipoRevit = "20x60", PreMoldada = false, Material = "Concreto moldado",
            Comprimento = 500, Trechos = Subdivisao.Juntar(new[] { (0.0, 500.0, SecaoPoligonal.Retangulo(20, 50)) }) };
        ConferenciaPecas.Avaliar(reta);
        Assert.Equal(SituacaoPeca.Atencao, reta.Situacao);
        Assert.Contains(reta.Avisos, a => a.Contains("não é concreto pré-moldado"));
        Assert.Contains(reta.Avisos, a => a.StartsWith("nome do tipo: o nome indica 20 × 60"));
        var pilar = new PecaLevantada { Tipo = TipoPeca.Pilar, TipoRevit = "40x40", PreMoldada = true, Comprimento = 600,
            Trechos = Subdivisao.Juntar(new[] { (0.0, 600.0, SecaoPoligonal.Retangulo(40, 40)) }) };
        ConferenciaPecas.Avaliar(pilar);
        Assert.Equal(SituacaoPeca.Ok, pilar.Situacao);
    }

    [Fact]
    public void AgrupaPelaGeometriaComInvertidasEAvisaNomesDiferentes()
    {
        var g = ConferenciaPecas.Agrupar(new[]
        {
            Viga("1", "V1", "20x50"), Viga("2", "V1", "20x50", invertida: true), Viga("3", "V2", "Viga 20x50 (cópia)"),
            Viga("4", "V3", "20x50", hDente: 30),
        });
        Assert.Equal(2, g.Count);
        var maior = g.Single(x => x.Quantidade == 3);
        Assert.Equal("V1, V2", maior.Marcas);
        Assert.Contains(maior.Avisos, a => a.StartsWith("peças iguais com tipos diferentes"));
        Assert.Equal(1, g.Single(x => x.Quantidade == 1).Quantidade);
    }
}
