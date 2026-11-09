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
    public void NomeDoTipoContraAGeometria()
    {
        Assert.Equal((26.0, 170.0), NomeTipo.Dimensoes("Viga Pré-Moldada - seção retangular com aba laje : 26x170"));
        Assert.Equal((12.5, 40.0), NomeTipo.Dimensoes("Pilar 12,5 X 40 cm"));
        Assert.Null(NomeTipo.Dimensoes("Consolo"));
        // com aba: o nome dá a alma × h — bate
        Assert.Null(NomeTipo.Conferir("seção retangular com aba laje : 26x170", ComAba()));
        // retangular nas duas ordens
        Assert.Null(NomeTipo.Conferir("40x20", SecaoPoligonal.Retangulo(20, 40)));
        // nome errado
        var msg = NomeTipo.Conferir("Viga 20x50", SecaoPoligonal.Retangulo(20, 60));
        Assert.NotNull(msg);
        Assert.Contains("20 × 60", msg);
    }
}
