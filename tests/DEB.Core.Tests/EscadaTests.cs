using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Elementos.Escada;
using DEB.Core.Normas.Verificacao;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Escada em laje armada longitudinalmente (D42): geometria, esforços conferidos à mão, armadura e barras.</summary>
public class EscadaTests
{
    /// <summary>Lance sem patamares: 16 espelhos de 18 cm, piso 28, ℓ = 15 × 28 = 420 cm, h = 12 cm, C25.</summary>
    public static EscadaLaje LanceSimples() => new EscadaLaje
    {
        Desnivel = 288, EspelhoAlvo = 18, Piso = 28, Largura = 120, Espessura = 12, Revestimento = 1, Sobrecarga = 2.5, Fck = 25,
    };

    /// <summary>Meio lance com patamares (inferior 120, superior 100), 8 espelhos de 18 cm — o caso que tem a quina reentrante.</summary>
    public static EscadaLaje ComPatamares() => new EscadaLaje
    {
        Desnivel = 144, Espelhos = 8, Piso = 28, Largura = 120, Espessura = 16, EspessuraPatamar = 16,
        PatamarInferior = 120, PatamarSuperior = 100, Revestimento = 1, Sobrecarga = 3, Fck = 25,
    };

    [Fact]
    public void LanceSimplesBateComAContaAMao()
    {
        var r = CalculoEscada.Calcular(LanceSimples());
        var g = r.Geo;
        Assert.Equal(16, g.N);
        Assert.Equal(18, g.Espelho, 6);
        Assert.Equal(420, g.Vao, 6);
        double c = Math.Cos(Math.Atan2(18, 28)), s = Math.Sin(Math.Atan2(18, 28));
        Assert.Equal(0.84117, c, 4);
        // g = 25·(0,12/cosα + 0,09) + 1 = 6,8165 kN/m²; pd = 1,4·(g + 2,5)
        Assert.Equal(6.8165, r.GLance, 3);
        double pd = 1.4 * (6.8165 + 2.5);
        Assert.Equal(pd * 4.2 * 4.2 / 8, r.MdMax, 2);          // 28,76 kN·m/m
        Assert.Equal(210, r.XMdMax, 6);
        Assert.Equal(pd * 2.1 * c, r.VdMax, 2);                // 23,04 kN/m
        Assert.Equal(-pd * 2.1 * s, r.Diagrama.First().Nd, 2); // −14,81 kN/m (compressão embaixo)
        Assert.Equal(pd * 2.1 * s, r.Diagrama.Last().Nd, 2);   // tração em cima
        Assert.Equal(r.GLance * 2.1, r.RaG, 6);
        Assert.Equal(2.5 * 2.1, r.RbQ, 3);
        // flecha elástica (quase permanente, Ic, Ecs = 24 150 MPa): 5wℓ⁴/(384EI)/cosα = 1,048 cm
        Assert.Equal(1.048, r.FlechaElasticaMax, 2);
        Assert.Equal(0, r.Diagrama.First().FlechaElastica, 6);
    }

    [Fact]
    public void ContornoTemAAreaDaLajeMaisOsDegraus()
    {
        var g = GeometriaEscada.De(LanceSimples());
        double c = Math.Cos(g.Alfa);
        double esperado = 420 * 12 / c + 15 * 28 * 18 / 2.0;   // paralelogramo + 15 degraus
        Assert.Equal(esperado, Math.Abs(Area(g.Contorno)), 1);
        Assert.True(Area(g.Contorno) > 0, "contorno anti-horário");
    }

    [Fact]
    public void ComPatamaresEquilibraEArmaComBarrasCruzadas()
    {
        var r = CalculoEscada.Calcular(ComPatamares());
        var g = r.Geo;
        Assert.Equal(3, g.Trechos.Count);
        Assert.Equal(120 + 7 * 28 + 100, g.Vao, 6);
        Assert.NotNull(g.QuinaReentrante);
        // equilíbrio: soma das reações = carga total
        double total = r.GPatamar * (1.2 + 1.0) + r.GLance * 7 * 0.28;
        Assert.Equal(total, r.RaG + r.RbG, 6);
        Assert.Equal(3 * (1.2 + 1.96 + 1.0), r.RaQ + r.RbQ, 6);
        // barras: N1 e N2 cruzam (N1 passa da quina e N2 entra no lance)
        var n1 = r.Barras.Single(b => b.Nome == "N1");
        var n2 = r.Barras.Single(b => b.Nome == "N2");
        double xq = g.QuinaReentrante.Value.X;
        Assert.True(n1.Pontos.Max(p => p.X) > xq, "N1 passa da quina");
        Assert.True(n2.Pontos.Min(p => p.X) < xq, "N2 entra no lance");
        Assert.True(Area(g.Contorno) > 0);
        Assert.Contains(r.Memoria, m => m.Linhas.Any(l => l.Contains("SE CRUZAM")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BarrasFicamDentroDoConcreto(bool patamares)
    {
        var r = CalculoEscada.Calcular(patamares ? ComPatamares() : LanceSimples());
        var k = r.Geo.Contorno;
        double xMin = k.Min(p => p.X), xMax = k.Max(p => p.X);
        foreach (var b in r.Barras.Where(b => !b.Transversal))
            for (int i = 1; i < b.Pontos.Count; i++)
                for (int t = 0; t <= 10; t++)
                {
                    var a = b.Pontos[i - 1]; var c = b.Pontos[i];
                    double x = a.X + (c.X - a.X) * t / 10, z = a.Z + (c.Z - a.Z) * t / 10;
                    if (x < xMin + 1e-6 || x > xMax - 1e-6) continue;   // trecho dentro do apoio (viga)
                    Assert.True(Dentro(k, x, z), $"{b.Nome} fora do concreto em ({x:0.0}; {z:0.0})");
                }
    }

    [Fact]
    public void PropostaAtendeAsVerificacoesDeUmaEscadaUsual()
    {
        var r = CalculoEscada.Calcular(ComPatamares());   // ℓ = 4,16 m, h = 16 cm (ℓ/26); com h = 14 a flecha diferida não passa
        Assert.DoesNotContain(r.Verificacoes, v => v.Situacao == SituacaoVerificacao.NaoAtende && v.Codigo.StartsWith("K"));
        Assert.True(r.AsPrincipal >= r.AsNec - 1e-9);
        Assert.True(r.MRd >= r.MdMax - 1e-6);
        Assert.True(r.Principal.Esp <= 20);
        Assert.Contains(r.Barras, b => b.Nome == "N3" && b.Transversal);
    }

    [Fact]
    public void LajeFinaDemaisAcusaAFlecha()
    {
        var e = LanceSimples(); e.Espessura = 8; e.Desnivel = 340; e.Piso = 30;
        var r = CalculoEscada.Calcular(e);
        Assert.Contains(r.Verificacoes, v => v.Codigo == "K6" && v.Situacao == SituacaoVerificacao.NaoAtende);
    }

    [Fact]
    public void OtimizadorDevolveOpcoesQueAtendemEmOrdemDeCusto()
    {
        var e = ComPatamares();
        var ops = OtimizadorEscada.Otimizar(e);
        Assert.NotEmpty(ops);
        double vao = GeometriaEscada.De(e).Vao;
        foreach (var o in ops)
        {
            Assert.DoesNotContain(o.Resultado.Verificacoes, v => v.Codigo.StartsWith("K") && v.Situacao != SituacaoVerificacao.Ok);
            Assert.InRange(o.Blondel, 61 - 1e-9, 65 + 1e-9);
            Assert.Equal(vao, o.Resultado.Geo.Vao, 6);                 // os apoios não mudam
            Assert.Equal(144, o.Resultado.Geo.N * o.Resultado.Geo.Espelho, 6);
        }
        for (int i = 1; i < ops.Count; i++) Assert.True(ops[i].Custo >= ops[i - 1].Custo - 1e-9);
        // a melhor não custa mais que a escada digitada
        var r0 = CalculoEscada.Calcular(e); var (c0, a0) = OtimizadorEscada.Consumo(r0);
        Assert.True(ops[0].Custo <= c0 * 650 + a0 * 9 + 1e-6);
    }

    [Fact]
    public void TransversalBateComAContaAMao()
    {
        var e = LanceSimples(); e.Tipo = TipoEscada.Transversal; e.LarguraApoio = 20;
        var r = CalculoEscada.Calcular(e);
        double c = Math.Cos(Math.Atan2(18, 28)), pd1 = 1.4 * (r.GLance + 2.5) * c;
        Assert.Equal(140, r.VaoCalculo, 6);
        Assert.Equal(pd1 * 1.4 * 1.4 / 8, r.MdMax, 3);              // 2,688 kN·m/m
        Assert.Equal(pd1 * 1.4 / 2, r.VdMax, 3);
        Assert.Equal(r.GLance * c * 1.4 / 2, r.RaG, 6);
        double w = (r.GLance + 0.3 * 2.5) * c / 100, ei = 0.8625 * 28000 / 10 * 100 * 1728.0 / 12;
        Assert.Equal(5 * w * Math.Pow(140, 4) / (384 * ei), r.FlechaElasticaMax, 4);
        var n1 = r.Barras.Single(b => b.Nome == "N1");
        Assert.True(n1.Transversal);
        Assert.Equal(2 * (60 + 20 - 2.5), n1.Comprimento, 0);          // de apoio a apoio, dentro dos dois
        Assert.Contains(r.Barras, b => b.Nome == "N3" && b.PerfilY.Count == 3);   // negativa com a perna dentro do apoio
        Assert.DoesNotContain(r.Verificacoes, v => v.Codigo.StartsWith("K") && v.Situacao == SituacaoVerificacao.NaoAtende);
    }

    [Fact]
    public void BalancoBateComAContaAMaoETemBarraNoCantoDosDegraus()
    {
        var e = LanceSimples(); e.Tipo = TipoEscada.Balanco; e.LarguraApoio = 20; e.Espessura = 12;
        var r = CalculoEscada.Calcular(e);
        double c = Math.Cos(Math.Atan2(18, 28)), pd1 = 1.4 * (r.GLance + 2.5) * c;
        Assert.Equal(120, r.VaoCalculo, 6);
        Assert.Equal(pd1 * 1.2 * 1.2 / 2, r.MdMax, 3);              // 7,899 kN·m/m (negativo no engaste)
        Assert.True(r.Diagrama.First().Md < 0);
        Assert.Equal(r.GLance * c * 1.2, r.RaG, 6);
        Assert.Equal(r.GLance * c * 1.2 * 1.2 / 2, r.MEngG, 6);
        double w = (r.GLance + 0.3 * 2.5) * c / 100, ei = 0.8625 * 28000 / 10 * 100 * 1728.0 / 12;
        Assert.Equal(w * Math.Pow(120, 4) / (8 * ei), r.FlechaElasticaMax, 4);
        var n3 = r.Barras.Single(b => b.Nome == "N3");
        Assert.Equal(15, n3.Quantidade);                             // um por degrau (16 espelhos, 15 pisos)
        var n1 = r.Barras.Single(b => b.Nome == "N1");
        Assert.True(n1.PerfilY.Last().N < 0, "dobra para baixo na borda livre");
        Assert.Contains(r.Verificacoes, v => v.Codigo == "K5");
    }

    [Fact]
    public void TransversalIgnoraOsPatamaresComAviso()
    {
        var e = ComPatamares(); e.Tipo = TipoEscada.Transversal;
        var r = CalculoEscada.Calcular(e);
        Assert.Equal(7 * 28, r.Geo.Vao, 6);
        Assert.Contains(r.Avisos, a => a.Contains("patamares"));
    }

    internal static double Area(IReadOnlyList<(double X, double Z)> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count; i++) { var u = p[i]; var v = p[(i + 1) % p.Count]; a += u.X * v.Z - v.X * u.Z; }
        return a / 2;
    }

    internal static bool Dentro(IReadOnlyList<(double X, double Z)> p, double x, double z)
    {
        bool d = false;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
            if ((p[i].Z > z) != (p[j].Z > z) && x < (p[j].X - p[i].X) * (z - p[i].Z) / (p[j].Z - p[i].Z) + p[i].X) d = !d;
        return d;
    }
}
