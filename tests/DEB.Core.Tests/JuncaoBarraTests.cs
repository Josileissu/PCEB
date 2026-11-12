using System.Collections.Generic;
using System.Linq;
using DEB.Core.Calculo;
using DEB.Core.Modelo;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>JOIN / PEDIT→Juntar de armaduras e encadeamento de linhas soltas (2026-11-12).</summary>
public class JuncaoBarraTests
{
    private const double Tol = 1e-4;
    private static VerticePoli V(double x, double y, double b = 0) => new VerticePoli(x, y, b);

    // barra: gancho (0,11.5)→(0,0), reta (0,0)→(300,0)
    private static readonly VerticePoli[] Barra = { V(0, 11.5), V(0, 0), V(300, 0) };

    private static List<Segmento> Dados() => new List<Segmento>
    {
        new Segmento { Formato = FormatoSegmento.Gancho90, ComprimentoRegistrado = 11.5, HandleCota = "C0" },
        new Segmento { CotaMovida = true, CotaDx = 10, CotaDy = 5, Var = true, ComprimentoRegistrado = 300, HandleCota = "C1" },
    };

    [Fact]
    public void LinhaNoFim()
    {
        var j = new[] { V(0, 11.5), V(0, 0), V(300, 0), V(500, 0) };
        var pos = JuncaoBarra.Localizar(Barra, j, Tol);
        Assert.NotNull(pos);
        Assert.False(pos.Invertida);
        var (v, antes, depois) = JuncaoBarra.NoSentidoDaBarra(j, pos, Barra.Length);
        Assert.Equal((0, 1), (antes, depois));
        Assert.Equal(500, v[3].X);
        var s = JuncaoBarra.Realocar(Dados(), antes, depois);
        Assert.Equal(3, s.Count);
        Assert.Equal(FormatoSegmento.Gancho90, s[0].Formato); // o gancho do início continua
        Assert.True(s[1].CotaMovida && s[1].Var && s[1].HandleCota == "C1");
        Assert.True(s[2].Cotar && s[2].Formato == FormatoSegmento.Linha && s[2].HandleCota == "");
    }

    [Fact]
    public void LinhaNoInicioNaPontaDoGancho()
    {
        var j = new[] { V(-200, 11.5), V(0, 11.5), V(0, 0), V(300, 0) };
        var pos = JuncaoBarra.Localizar(Barra, j, Tol);
        var (_, antes, depois) = JuncaoBarra.NoSentidoDaBarra(j, pos, Barra.Length);
        Assert.Equal((1, 0), (antes, depois));
        var s = JuncaoBarra.Realocar(Dados(), antes, depois);
        Assert.Equal(FormatoSegmento.Linha, s[0].Formato);
        Assert.Equal(FormatoSegmento.Linha, s[1].Formato); // o gancho encostado vira reto
        Assert.Equal("C0", s[1].HandleCota);
        Assert.True(s[2].CotaMovida && s[2].HandleCota == "C1");
    }

    [Fact]
    public void LinhasNosDoisLados()
    {
        var j = new[] { V(-200, 11.5), V(0, 11.5), V(0, 0), V(300, 0), V(300, 80) };
        var pos = JuncaoBarra.Localizar(Barra, j, Tol);
        var (_, antes, depois) = JuncaoBarra.NoSentidoDaBarra(j, pos, Barra.Length);
        Assert.Equal((1, 1), (antes, depois));
        Assert.Equal(4, JuncaoBarra.Realocar(Dados(), antes, depois).Count);
    }

    [Fact]
    public void JuntadaInvertidaVoltaAoSentidoDaBarra()
    {
        // o CAD manteve o sentido da linha (300,80)→(300,0) e inverteu a barra
        var j = new[] { V(300, 80), V(300, 0), V(0, 0), V(0, 11.5) };
        var pos = JuncaoBarra.Localizar(Barra, j, Tol);
        Assert.True(pos.Invertida);
        var (v, antes, depois) = JuncaoBarra.NoSentidoDaBarra(j, pos, Barra.Length);
        Assert.Equal((0, 1), (antes, depois));
        Assert.Equal(new[] { (0.0, 11.5), (0.0, 0.0), (300.0, 0.0), (300.0, 80.0) }, v.Select(p => (p.X, p.Y)).ToArray());
    }

    [Fact]
    public void InverterTrocaOsBulges()
    {
        var v = new[] { V(0, 0, 0.5), V(10, 0, 0), V(10, 10, 0) };
        var r = JuncaoBarra.Inverter(v);
        Assert.Equal(new[] { 0.0, -0.5, 0.0 }, r.Select(p => p.Bulge).ToArray());
        Assert.Equal(0, r[2].X);
        // barra com arco achada numa juntada invertida
        var pos = JuncaoBarra.Localizar(v, new[] { V(10, 20), V(10, 10), V(10, 0, -0.5), V(0, 0) }, Tol);
        Assert.NotNull(pos);
        Assert.True(pos.Invertida);
    }

    [Fact]
    public void PolilinhaDeDoisTrechos()
    {
        var j = new[] { V(0, 11.5), V(0, 0), V(300, 0), V(450, 0), V(450, 60) };
        var pos = JuncaoBarra.Localizar(Barra, j, Tol);
        var (_, antes, depois) = JuncaoBarra.NoSentidoDaBarra(j, pos, Barra.Length);
        Assert.Equal((0, 2), (antes, depois));
        var s = JuncaoBarra.Realocar(Dados(), antes, depois);
        Assert.Equal(4, s.Count);
        Assert.All(s.Skip(2), x => Assert.True(x.Cotar && x.Formato == FormatoSegmento.Linha));
    }

    [Fact]
    public void DuasArmaduras()
    {
        var a = Barra;
        var b = new[] { V(300, 0), V(600, 0), V(600, 11.5) };
        var j = new[] { V(0, 11.5), V(0, 0), V(300, 0), V(600, 0), V(600, 11.5) };
        var pa = JuncaoBarra.Localizar(a, j, Tol);
        var pb = JuncaoBarra.Localizar(b, j, Tol);
        Assert.Equal((0, false), (pa.Indice, pa.Invertida));
        Assert.Equal((2, false), (pb.Indice, pb.Invertida));
        // mantida = B (por exemplo, a mais longa): os trechos de A entram antes
        var (_, antes, depois) = JuncaoBarra.NoSentidoDaBarra(j, pb, b.Length);
        Assert.Equal((2, 0), (antes, depois));
        var dadosB = new List<Segmento> { new Segmento(), new Segmento { Formato = FormatoSegmento.Gancho90 } };
        var s = JuncaoBarra.Realocar(dadosB, antes, depois);
        Assert.Equal(FormatoSegmento.Gancho90, s[3].Formato); // gancho da ponta livre de B continua
        Assert.Equal(FormatoSegmento.Linha, s[0].Formato);    // o gancho de A não vem junto
    }

    [Fact]
    public void ForaDaJuntadaDaNull()
    {
        Assert.Null(JuncaoBarra.Localizar(Barra, new[] { V(320, 0), V(500, 0) }, Tol));
        Assert.Null(JuncaoBarra.Localizar(Barra, new[] { V(0, 11.5), V(0, 0), V(300, 0.1), V(500, 0) }, Tol));
    }

    [Fact]
    public void TrechosAntesPelosComprimentosRegistrados()
    {
        Assert.Equal(1, JuncaoBarra.TrechosAntes(new[] { 11.5, 300 }, new[] { 200.0, 11.5, 300 }, 0.05));
        Assert.Equal(0, JuncaoBarra.TrechosAntes(new[] { 11.5, 300 }, new[] { 11.5, 300, 200 }, 0.05));
        Assert.Null(JuncaoBarra.TrechosAntes(new[] { 11.5, 300 }, new[] { 11.5, 150, 150 }, 0.05)); // vértice no meio
        Assert.Null(JuncaoBarra.TrechosAntes(new[] { 0.0, 300 }, new[] { 200.0, 0, 300 }, 0.05));    // sem registro
    }

    // ---------------- linhas soltas ----------------

    private static ((double, double), (double, double)) L(double x1, double y1, double x2, double y2) => ((x1, y1), (x2, y2));

    [Fact]
    public void EncadeiaNaOrdemDasLigacoesComLinhasInvertidas()
    {
        // desenhadas fora de ordem e com sentidos trocados: (0,0)-(100,0)-(100,50)-(200,50)
        var linhas = new[] { L(100, 50, 100, 0), L(200, 50, 100, 50), L(0, 0, 100, 0.00001) };
        var c = JuncaoBarra.Encadear(linhas, 1e-3);
        Assert.Single(c);
        Assert.False(c[0].Fechada || c[0].Ramificada);
        var pts = c[0].Pontos.Select(p => (System.Math.Round(p.X), System.Math.Round(p.Y))).ToArray();
        Assert.Equal(4, pts.Length);
        // começa numa ponta livre: (200,50) (linha de menor índice com ponta livre é a 1)
        Assert.Equal(new[] { (200.0, 50.0), (100.0, 50.0), (100.0, 0.0), (0.0, 0.0) }, pts);
        Assert.Equal(new[] { 1, 0, 2 }, c[0].Linhas);
    }

    [Fact]
    public void LinhaIsoladaECadeiasSeparadas()
    {
        var c = JuncaoBarra.Encadear(new[] { L(0, 0, 100, 0), L(0, 100, 50, 100), L(50, 100, 50, 150) }, 1e-3);
        Assert.Equal(2, c.Count);
        Assert.Equal(2, c[0].Pontos.Count);
        Assert.Equal(3, c[1].Pontos.Count);
    }

    [Fact]
    public void CadeiaFechadaViraPoligono()
    {
        var c = JuncaoBarra.Encadear(new[] { L(0, 0, 20, 0), L(20, 50, 20, 0), L(20, 50, 0, 50), L(0, 50, 0, 0) }, 1e-3);
        Assert.Single(c);
        Assert.True(c[0].Fechada);
        Assert.Equal(4, c[0].Pontos.Count);
        Assert.Equal(4, c[0].Linhas.Count);
    }

    [Fact]
    public void RamificadaNaoEncadeia()
    {
        var c = JuncaoBarra.Encadear(new[] { L(0, 0, 100, 0), L(100, 0, 200, 0), L(100, 0, 100, 100), L(500, 500, 600, 500) }, 1e-3);
        Assert.Equal(2, c.Count);
        Assert.True(c[0].Ramificada);
        Assert.Equal(3, c[0].Linhas.Count);
        Assert.False(c[1].Ramificada);
    }
}
