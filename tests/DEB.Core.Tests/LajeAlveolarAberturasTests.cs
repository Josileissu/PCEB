using System.Collections.Generic;
using System.Linq;
using DEB.Core.Lajes;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>
/// Aberturas do modelo na paginação de laje alveolar (2026-11-17): abertura por face/vertical e shaft viram furos do piso.
/// Piso padrão: retângulo 6,00 × 5,60 m em planta (metros).
/// </summary>
public class LajeAlveolarAberturasTests
{
    private static List<(double X, double Y)> Ret(double x0, double y0, double x1, double y1)
        => new List<(double X, double Y)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };

    private static List<List<(double X, double Y)>> Piso(params List<(double X, double Y)>[] furos)
    {
        var l = new List<List<(double X, double Y)>> { Ret(0, 0, 6, 5.6) };
        l.AddRange(furos);
        return l;
    }

    private static AberturaPlanta Ab(string id, List<(double X, double Y)> pts, bool shaft = false)
        => new AberturaPlanta { Id = id, Shaft = shaft, Pontos = pts };

    // ------------------------------------------------------------------ laços

    [Fact]
    public void LacosEncadeiaTrechosForaDeOrdemEInvertidos()
    {
        var trechos = new List<IReadOnlyList<(double X, double Y)>>
        {
            new List<(double, double)> { (1, 1), (2, 1) },
            new List<(double, double)> { (1, 2), (2, 2) },            // invertido: o laço passa de (2,2) para (1,2)
            new List<(double, double)> { (2.0005, 1), (2, 2) },       // ponta a 0,5 mm (dentro da tolerância)
            new List<(double, double)> { (1, 2), (1, 1) },
            // segundo laço (triângulo)
            new List<(double, double)> { (4, 1), (5, 1) },
            new List<(double, double)> { (5, 1), (4.5, 2) },
            new List<(double, double)> { (4.5, 2), (4, 1) },
        };
        var l = AberturasPiso.Lacos(trechos, out int soltos);
        Assert.Equal(0, soltos);
        Assert.Equal(2, l.Count);
        Assert.Equal(4, l[0].Count);
        Assert.Equal(3, l[1].Count);
        Assert.Equal(1.0, l[0].Min(p => p.X), 6); Assert.Equal(2.0, l[0].Max(p => p.Y), 6);
    }

    [Fact]
    public void LacosAceitaCurvaFechadaUnicaEDescartaTrechoSolto()
    {
        var circulo = Enumerable.Range(0, 13).Select(k => (System.Math.Cos(k * System.Math.PI / 6), System.Math.Sin(k * System.Math.PI / 6))).ToList();
        var trechos = new List<IReadOnlyList<(double X, double Y)>> { circulo, new List<(double, double)> { (5, 5), (6, 6) } };
        var l = AberturasPiso.Lacos(trechos, out int soltos);
        Assert.Single(l);
        Assert.Equal(12, l[0].Count);
        Assert.Equal(1, soltos);
    }

    // ------------------------------------------------------------------ classificação

    [Fact]
    public void ClassificaDentroForaEBorda()
    {
        var c = Ret(0, 0, 6, 5.6);
        Assert.Equal(PosicaoAbertura.Dentro, AberturasPiso.Classificar(Ret(2, 2, 3, 3), c));
        Assert.Equal(PosicaoAbertura.Fora, AberturasPiso.Classificar(Ret(7, 2, 8, 3), c));
        Assert.Equal(PosicaoAbertura.Borda, AberturasPiso.Classificar(Ret(5.5, 2, 6.5, 3), c));   // cruza a borda
        Assert.Equal(PosicaoAbertura.Borda, AberturasPiso.Classificar(Ret(5, 2, 6, 3), c));       // encosta na borda (entalhe)
        Assert.Equal(PosicaoAbertura.Borda, AberturasPiso.Classificar(Ret(-1, -1, 7, 7), c));     // cobre o piso inteiro
    }

    [Fact]
    public void ClassificaPisoEmLComAberturaNoCantoReentrante()
    {
        // L: tira o quadrante x > 3, y > 3
        var l = new List<(double X, double Y)> { (0, 0), (6, 0), (6, 3), (3, 3), (3, 6), (0, 6) };
        Assert.Equal(PosicaoAbertura.Fora, AberturasPiso.Classificar(Ret(4, 4, 5, 5), l));        // no recorte do L
        Assert.Equal(PosicaoAbertura.Borda, AberturasPiso.Classificar(Ret(2.5, 2.5, 3.5, 3.5), l));
        Assert.Equal(PosicaoAbertura.Dentro, AberturasPiso.Classificar(Ret(1, 4, 2, 5), l));
    }

    [Fact]
    public void MesmoPoligonoIndependeDaTesselacaoEDaTolerancia()
    {
        var a = Ret(2, 2, 3, 3);
        var b = new List<(double X, double Y)> { (2.005, 2), (2.5, 2), (3, 2), (3, 3), (2, 3.004), (2, 2.5) };
        Assert.True(AberturasPiso.MesmoPoligono(a, b));
        Assert.False(AberturasPiso.MesmoPoligono(a, Ret(2, 2, 3.05, 3)));
    }

    // ------------------------------------------------------------------ inclusão

    [Fact]
    public void IncluiAberturasDentroENotaResumo()
    {
        var r = AberturasPiso.Incluir(Piso(), new[]
        {
            Ab("101", Ret(1, 1, 1.5, 1.5)),
            Ab("102", Ret(2, 1, 2.5, 1.5)),
            Ab("200", Ret(4, 3, 4.6, 3.6), shaft: true),
            Ab("300", Ret(8, 1, 9, 2), shaft: true),          // fora: ignorado em silêncio
        });
        Assert.Equal(4, r.Loops.Count);
        Assert.Equal(2, r.AberturasFace);
        Assert.Equal(1, r.Shafts);
        Assert.Single(r.Notas);
        Assert.Contains("2 aberturas por face e 1 shaft", r.Notas[0]);
        Assert.Equal(6.0, r.Loops[0].Max(p => p.X), 6);        // contorno continua em 1º
    }

    [Fact]
    public void AberturaNaBordaNaoEntraEAvisa()
    {
        var r = AberturasPiso.Incluir(Piso(), new[] { Ab("55", Ret(5.5, 2, 6.5, 3)) });
        Assert.Single(r.Loops);
        Assert.Equal(0, r.AberturasFace);
        Assert.Contains(r.Notas, n => n == "abertura 55 na borda do piso: ajuste o contorno do piso");
    }

    [Fact]
    public void NaoDuplicaFuroDoCroqui()
    {
        var r = AberturasPiso.Incluir(Piso(Ret(2, 2, 3, 3)), new[] { Ab("7", Ret(2.004, 2, 3, 3.003)) });
        Assert.Equal(2, r.Loops.Count);
        Assert.Equal(0, r.AberturasFace);
        Assert.Contains(r.Notas, n => n.Contains("abertura 7") && n.Contains("croqui"));
    }

    [Fact]
    public void ShaftDeVariosLacosEAberturaRepetidaContamUmaVez()
    {
        // shaft com dois laços (mesmo id) e uma abertura por face no mesmo lugar que o 1º laço do shaft
        var r = AberturasPiso.Incluir(Piso(), new[]
        {
            Ab("9", Ret(1, 1, 2, 2), shaft: true),
            Ab("9", Ret(4, 1, 5, 2), shaft: true),
            Ab("10", Ret(1, 1, 2, 2)),
        });
        Assert.Equal(3, r.Loops.Count);
        Assert.Equal(1, r.Shafts);
        Assert.Equal(0, r.AberturasFace);
        Assert.Contains(r.Notas, n => n.Contains("1 shaft incluído como furo"));
    }

    [Fact]
    public void SobreposicaoEntraComAvisoEContidaEmFuroEhIgnorada()
    {
        var r = AberturasPiso.Incluir(Piso(Ret(2, 2, 3, 3)), new[]
        {
            Ab("1", Ret(2.5, 2.5, 3.5, 3.5)),       // sobrepõe o furo do croqui
            Ab("2", Ret(2.2, 2.2, 2.4, 2.4)),       // inteira dentro do furo do croqui
            Ab("3", Ret(3.4, 3.4, 4, 4)),           // sobrepõe a abertura 1
        });
        Assert.Equal(4, r.Loops.Count);
        Assert.Contains(r.Notas, n => n == "abertura 1 e furo do croqui se sobrepõem: confira os furos");
        Assert.Contains(r.Notas, n => n.Contains("abertura 2") && n.Contains("ignorada"));
        Assert.Contains(r.Notas, n => n == "abertura 3 e abertura 1 se sobrepõem: confira os furos");
        Assert.Equal(2, r.AberturasFace);
    }

    [Fact]
    public void FurosIncluidosChegamAoMotorComoAberturas()
    {
        // piso de 6 × 5,6 com vigas nas bordas u = 0 e 6: a abertura vira furo no motor (aberturas do pano)
        var r = AberturasPiso.Incluir(Piso(), new[] { Ab("44", Ret(2.5, 2, 3.5, 3)) });
        var loops = r.Loops.Select(l => (IReadOnlyList<PontoUV>)l.Select(p => new PontoUV(p.X, p.Y)).ToList()).ToList();
        var g = MotorPaginacao.ClassificarPano(loops, new List<PilarPano>());
        Assert.Contains(g.Aberturas, a => a.Ret.U0 >= 2.5 - 1e-6 && a.Ret.U1 <= 3.5 + 1e-6 && a.Ret.V0 >= 2 - 1e-6 && a.Ret.V1 <= 3 + 1e-6);
    }
}
