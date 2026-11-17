using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Elementos.Pilar.PreMoldado;
using DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D;
using DEB.Core.Geometria.Secao;
using DEB.Core.Motor;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>
/// Armadura por linha (D32): a entrada pela seção (SecaoTransversal + cobrimentos por parte) dá, na retangular, exatamente o
/// resultado da conta por b × h que o template do pilar usa; nas outras famílias, "não definido".
/// </summary>
public class ArmaduraPorLinhaTests
{
    private static readonly double[] Cob = { 3, 2.5, 3.5, 4 };

    private static List<LinhaLongitudinal> Linhas(string texto) =>
        ArmaduraPorLinha.Ler(ValoresTemplate.LerTabela(texto), 2.5, 597.5, new List<string>());

    public static IEnumerable<object[]> Casos() => new[]
    {
        new object[] { 40.0, 40.0, "Cantos||12.5|1||;Faces 2 e 4|1|12.5|1||;Faces 1 e 3|1|12.5|1||" },
        new object[] { 50.0, 70.0, "Cantos||25|1||;Ao lado dos cantos||20|1||;Faces 2 e 4|2|16|2||;Face 1|3|12.5|1||" },
        new object[] { 60.0, 45.0, "Cantos||20|3||;Cantos||16|1|300|;Face 3|2|10|1||;Face 4|1|20|2|0|320;Face 4|1|16|1|280|" },
    };

    [Theory]
    [MemberData(nameof(Casos))]
    public void RetangularPelaSecaoIgualAContaPorBxH(double b, double h, string texto)
    {
        var linhas = Linhas(texto);
        Assert.NotEmpty(linhas);
        var antes = ArmaduraPorLinha.Posicoes(b, h, Cob, 8, linhas, 5);
        var pela = ArmaduraPorLinha.Posicoes(SecaoTransversal.Retangular(b, h), FacesRetangulo.Cobrimentos(Cob), 8, linhas, 5);
        Assert.True(pela.Definido, pela.Erro);
        Assert.Equal(antes.Keys, pela.PorLinha.Keys);
        foreach (var k in antes.Keys)
        {
            Assert.Equal(antes[k].Count, pela.PorLinha[k].Count);
            for (int i = 0; i < antes[k].Count; i++)
            {
                BarraNaSecao x = antes[k][i], y = pela.PorLinha[k][i];
                Assert.Equal(x.X, y.X, 12); Assert.Equal(x.Y, y.Y, 12);
                Assert.Equal(x.Bitola, y.Bitola); Assert.Equal(x.Feixe, y.Feixe);
                Assert.Equal(x.Grupo, y.Grupo); Assert.Equal(x.Arranjo, y.Arranjo);
            }
        }
        // o atalho do template é a mesma conta
        var t = TemplatePilarPreMoldado.PosicoesLinhas(b, h, Cob, 8, linhas, 5);
        Assert.Equal(antes.Values.SelectMany(v => v).Select(p => (p.X, p.Y)), t.Values.SelectMany(v => v).Select(p => (p.X, p.Y)));
    }

    [Fact]
    public void OutrasFamiliasNaoDefinidas()
    {
        var linhas = Linhas("Cantos||16|1||");
        var cob = new Dictionary<ParteSecao, double> { [ParteSecao.Topo] = 3, [ParteSecao.Fundo] = 3, [ParteSecao.Esquerda] = 3, [ParteSecao.Direita] = 3 };
        foreach (var s in new[] { SecaoTransversal.T(20, 60, 12, 30, 30), SecaoTransversal.Trapezoidal(20, 30, 40) })
        {
            var r = ArmaduraPorLinha.Posicoes(s, cob, 8, linhas, 5);
            Assert.False(r.Definido);
            Assert.Contains("não definida para esta família", r.Erro);
            Assert.Empty(r.PorLinha);
        }
    }

    [Fact]
    public void CobrimentoFaltandoNaRetangular()
    {
        var cob = new Dictionary<ParteSecao, double> { [ParteSecao.Topo] = 3, [ParteSecao.Fundo] = 3, [ParteSecao.Esquerda] = 3 };
        var r = ArmaduraPorLinha.Posicoes(SecaoTransversal.Retangular(40, 40), cob, 8, Linhas("Cantos||16|1||"), 5);
        Assert.False(r.Definido);
        Assert.False(string.IsNullOrEmpty(r.Erro));
    }

    // ------------------------------------------------------------------ modelo 3D do Revit pela armadura por linha

    private const double CmPorPe = 30.48;

    private static (ColumnBox F, ColumnRebarSpec S) Pilar(double b = 40, double h = 40, double l = 600)
    {
        var f = new ColumnBox(U_.Mm(b * 10), U_.Mm(h * 10), U_.Mm(l * 10));
        return (f, ColumnRebarSpec.Default(f, 1000, new List<Corbel>()));
    }

    private static List<BarDef> Longs(ColumnBox f, ColumnRebarSpec s) =>
        ColumnRebarLayout.Compute(f, new List<Corbel>(), s).Where(b => b.Role == BarRole.Longitudinal).ToList();

    private static string Chave(BarDef b) => string.Join(";", new[] { b.Role.ToString(), b.DiameterMm.ToString("R"), b.Count.ToString(), b.Spacing.ToString("R") }
        .Concat(b.Points.Select(p => $"{p.A:R},{p.B:R},{p.C:R}")));

    /// <summary>Modelo salvo antes da D32 (sem a lista) = a lista convertida dos campos simples: as mesmas barras, todas.</summary>
    [Theory]
    [InlineData(40, 40, 12.5, 2, 2)]
    [InlineData(50, 50, 16, 3, 3)]
    [InlineData(60, 40, 20, 4, 2)]
    public void ModeloAntigoIgualAListaConvertida(double b, double h, double phi, int nx, int ny)
    {
        var (f, s) = Pilar(b, h);
        s.LongDiameterMm = phi; s.LongBarsAlongX = nx; s.LongBarsAlongY = ny;
        Assert.Null(s.ArmaduraPorLinha);
        var antigo = ColumnRebarLayout.Compute(f, new List<Corbel>(), s).Select(Chave).ToList();
        var s2 = s.Clone();
        s2.ArmaduraPorLinha = ArmaduraPorLinha.DaArmaduraSimples(phi, nx, ny);
        Assert.Equal(antigo, ColumnRebarLayout.Compute(f, new List<Corbel>(), s2).Select(Chave).ToList());
        Assert.Equal(s.Check(f, new List<Corbel>()), s2.Check(f, new List<Corbel>()));
    }

    /// <summary>JSON dos modelos salvos (System.Text.Json, por nome de propriedade, como o DEB.Revit): o antigo abre sem a lista.</summary>
    [Fact]
    public void JsonAntigoAbreENovoGuardaAsLinhas()
    {
        var antigo = System.Text.Json.JsonSerializer.Deserialize<ColumnRebarSpec>("{\"Name\":\"M1\",\"LongDiameterMm\":16,\"LongBarsAlongX\":3,\"LongBarsAlongY\":2}");
        Assert.Null(antigo.ArmaduraPorLinha);
        var l = antigo.Linhas();
        Assert.Equal(new[] { ArmaduraPorLinha.Cantos, ArmaduraPorLinha.Faces24 }, l.Select(x => x.Arranjo));
        Assert.All(l, x => Assert.Equal(16, x.Bitola));
        Assert.Equal(1, l[1].PorFace);

        antigo.ArmaduraPorLinha = new List<LinhaArmaduraPorLinha>
        {
            new LinhaArmaduraPorLinha { Arranjo = ArmaduraPorLinha.Cantos, Bitola = 20, Feixe = 2, Fim = 300 },
            new LinhaArmaduraPorLinha { Arranjo = ArmaduraPorLinha.Face3, PorFace = 2, Bitola = 10, Ini = 100 },
        };
        string json = System.Text.Json.JsonSerializer.Serialize(antigo);
        Assert.Contains("\"LongBarsAlongX\":3", json);   // os campos antigos continuam no arquivo
        var lido = System.Text.Json.JsonSerializer.Deserialize<ColumnRebarSpec>(json);
        Assert.Equal(2, lido.ArmaduraPorLinha.Count);
        Assert.Equal(new[] { "Cantos", "", "20", "2", "", "300" }, lido.ArmaduraPorLinha[0].Celulas());
        Assert.Equal(new[] { "Face 3", "2", "10", "1", "100", "" }, lido.ArmaduraPorLinha[1].Celulas());
    }

    [Fact]
    public void CloneCopiaAsLinhas()
    {
        var (_, s) = Pilar();
        s.ArmaduraPorLinha = ArmaduraPorLinha.DaArmaduraSimples(16, 3, 3);
        var c = s.Clone();
        c.ArmaduraPorLinha[0].Bitola = 25;
        c.ArmaduraPorLinha.RemoveAt(1);
        Assert.Equal(16, s.ArmaduraPorLinha[0].Bitola);
        Assert.Equal(3, s.ArmaduraPorLinha.Count);
    }

    /// <summary>Bitola por linha: cada linha com a sua, nas posições da conta do CAD (as de face pelo raio delas).</summary>
    [Fact]
    public void BitolaPorLinha()
    {
        var (f, s) = Pilar(50, 50);
        s.ArmaduraPorLinha = new List<LinhaArmaduraPorLinha>
        {
            new LinhaArmaduraPorLinha { Arranjo = ArmaduraPorLinha.Cantos, Bitola = 20 },
            new LinhaArmaduraPorLinha { Arranjo = ArmaduraPorLinha.Faces24, PorFace = 1, Bitola = 12.5 },
            new LinhaArmaduraPorLinha { Arranjo = ArmaduraPorLinha.Faces13, PorFace = 2, Bitola = 10 },
        };
        var l = Longs(f, s);
        Assert.Equal(4, l.Count(b => b.DiameterMm == 20));
        Assert.Equal(2, l.Count(b => b.DiameterMm == 12.5));
        Assert.Equal(4, l.Count(b => b.DiameterMm == 10));
        // mesmas posições que o template do CAD calcula (cobrimento 2,5, estribo 5 mm, canto em L 5 cm)
        var cad = TemplatePilarPreMoldado.PosicoesLinhas(50, 50, new[] { 2.5, 2.5, 2.5, 2.5 }, 5,
            ArmaduraPorLinha.Ler(s.ArmaduraPorLinha, 2.5, 597.5, new List<string>()), 5).Values.SelectMany(x => x).ToList();
        Assert.Equal(cad.Count, l.Count);
        foreach (var c in cad)
            Assert.Contains(l, b => Math.Abs(b.Points[0].A * CmPorPe - c.X) < 1e-6 && Math.Abs(b.Points[0].B * CmPorPe - c.Y) < 1e-6 && b.DiameterMm == c.Bitola);
        // o tirante e as alças param pela maior (como o BitolaMaxLongitudinal do CAD)
        Assert.Equal(20, s.MaxLongDiameterMm(), 9);
    }

    /// <summary>Redução com a altura: cada linha no seu trecho (cm da base), sem emenda automática; o mesmo arranjo na mesma posição.</summary>
    [Fact]
    public void ReducaoComAAltura()
    {
        var (f, s) = Pilar(40, 40, 900);
        s.ArmaduraPorLinha = new List<LinhaArmaduraPorLinha>
        {
            new LinhaArmaduraPorLinha { Arranjo = ArmaduraPorLinha.Cantos, Bitola = 20, Fim = 500 },
            new LinhaArmaduraPorLinha { Arranjo = ArmaduraPorLinha.Cantos, Bitola = 16, Ini = 420 },
        };
        var l = Longs(f, s);
        Assert.Equal(8, l.Count);
        var b20 = l.Where(b => b.DiameterMm == 20).ToList();
        var b16 = l.Where(b => b.DiameterMm == 16).ToList();
        Assert.All(b20, b => { Assert.Equal(2.5, b.Points[0].C * CmPorPe, 6); Assert.Equal(500, b.Points[1].C * CmPorPe, 6); });
        Assert.All(b16, b => { Assert.Equal(420, b.Points[0].C * CmPorPe, 6); Assert.Equal(897.5, b.Points[1].C * CmPorPe, 6); });
        // as duas linhas do mesmo arranjo ficam alinhadas pelo maior feixe do arranjo (posição não muda com a altura), como no CAD
        foreach (var b in b16) Assert.Contains(b20, a => Math.Abs(a.Points[0].A - b.Points[0].A) < 1e-9 && Math.Abs(a.Points[0].B - b.Points[0].B) < 1e-9);
        // verificação por trecho: 4 ø20 em baixo e 4 ø16 em cima; no trecho da emenda, as 8
        var trechos = ColumnRebarLayout.LongitudinalStretches(f, s).ToList();
        Assert.Equal(new[] { 4, 8, 4 }, trechos.Select(t => t.Count));
    }

    /// <summary>Feixe: cada posição vira 2 ou 3 barras, pelos centros do núcleo (CentrosFeixe).</summary>
    [Fact]
    public void FeixeComOsCentrosDoNucleo()
    {
        var (f, s) = Pilar(50, 50);
        s.ArmaduraPorLinha = new List<LinhaArmaduraPorLinha>
        {
            new LinhaArmaduraPorLinha { Arranjo = ArmaduraPorLinha.Cantos, Bitola = 16, Feixe = 2 },
            new LinhaArmaduraPorLinha { Arranjo = ArmaduraPorLinha.Face2, PorFace = 1, Bitola = 12.5, Feixe = 3 },
        };
        var l = Longs(f, s);
        Assert.Equal(4 * 2 + 1 * 3, l.Count);
        var linhas = ArmaduraPorLinha.Ler(s.ArmaduraPorLinha, 2.5, 597.5, new List<string>());
        var pos = ArmaduraPorLinha.Posicoes(50, 50, new[] { 2.5, 2.5, 2.5, 2.5 }, 5, linhas, 5);
        foreach (var lin in linhas)
            foreach (var c in pos[lin.Linha])
                foreach (var (dx, dy) in SecaoBarras.CentrosFeixe(lin.Bitola, lin.Feixe))
                    Assert.Contains(l, b => Math.Abs(b.Points[0].A * CmPorPe - (c.X + dx)) < 1e-6 && Math.Abs(b.Points[0].B * CmPorPe - (c.Y + dy)) < 1e-6);
        // as barras do feixe se tocam (eixos a uma bitola)
        double yMin = l.Where(b => b.DiameterMm == 16).Min(b => b.Points[0].B);
        var canto = l.Where(b => b.DiameterMm == 16 && Math.Abs(b.Points[0].B - yMin) < 1e-9).OrderBy(b => b.Points[0].A).Take(2).ToList();
        Assert.Equal(1.6, Math.Abs(canto[1].Points[0].A - canto[0].Points[0].A) * CmPorPe, 6);
        Assert.Equal(20 * SecaoBarras.RaioFeixe(16, 2), s.MaxLongDiameterMm(), 9);
    }

    [Fact]
    public void AvisosDaLeituraNaVerificacao()
    {
        var (f, s) = Pilar();
        s.ArmaduraPorLinha = new List<LinhaArmaduraPorLinha>
        {
            new LinhaArmaduraPorLinha { Arranjo = ArmaduraPorLinha.Cantos, Bitola = 16 },
            new LinhaArmaduraPorLinha { Arranjo = ArmaduraPorLinha.Cantos, Bitola = 12.5, Ini = 100, Fim = 200 },
            new LinhaArmaduraPorLinha { Arranjo = "Meio", Bitola = 12.5 },
        };
        var w = s.Check(f, new List<Corbel>());
        Assert.Contains(w, x => x.Contains("fica toda dentro"));
        Assert.Contains(w, x => x.Contains("desconhecido"));
        s.ArmaduraPorLinha = new List<LinhaArmaduraPorLinha>();
        Assert.Empty(Longs(f, s));
        Assert.Contains(s.Check(f, new List<Corbel>()), x => x.Contains("Sem armadura longitudinal"));
    }
}