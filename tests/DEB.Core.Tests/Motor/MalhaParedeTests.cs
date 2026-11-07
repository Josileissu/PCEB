using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Motor;
using DEB.Core.Motor.Componentes;
using Xunit;

namespace DEB.Core.Tests.Motor;

/// <summary>Componente <c>malha_parede</c> (etapa 8).</summary>
public class MalhaParedeTests
{
    private sealed class ElementoTeste : ElementoEstrutural { public override string Tipo => "teste"; }

    private static ContextoMotor Ctx(Sobrescrita[] s = null)
    {
        var e = new ElementoTeste { Nome = "R1", Fck = 30 };
        e.Armaduras.Add(new ArmaduraCalculada { Funcao = "parede_horizontal", Bitola = 10, Espacamento = 15 });
        e.Armaduras.Add(new ArmaduraCalculada { Funcao = "parede_vertical", Bitola = 8, Espacamento = 20 });
        return new ContextoMotor(e, null, s);
    }

    /// <summary>Parede de 300 cm em x, 20 de espessura, 0 a 250 de altura.</summary>
    private static ArgumentosComponente Parede() => new ArgumentosComponente()
        .Com("x0", 0).Com("y0", 0).Com("x1", 300).Com("y1", 0).Com("espessura", 20).Com("z0", 0).Com("z1", 250).Com("cobrimento", 3);

    [Fact]
    public void ParedeLivreComFechamentos()
    {
        var ctx = Ctx();
        ComponentesPadrao.MalhaParede.Aplicar(ctx, Parede());
        var m = ctx.Concluir();
        var ha = m.Posicao("parede_horizontal_a"); var hb = m.Posicao("parede_horizontal_b");
        var va = m.Posicao("parede_vertical_a");
        Assert.NotNull(ha); Assert.NotNull(hb); Assert.NotNull(va); Assert.NotNull(m.Posicao("parede_vertical_b"));
        // horizontais junto da face (y = 10 − 3 − 0,5), verticais por dentro (10 − 3 − 1 − 0,4)
        Assert.Equal(6.5, ha.Barras[0][0].Y, 6);
        Assert.Equal(-6.5, hb.Barras[0][0].Y, 6);
        Assert.Equal(5.6, va.Barras[0][0].Y, 6);
        // ponta livre: horizontais a cobrimento das pontas; reta
        Assert.Equal(2, ha.Barras[0].Count);
        Assert.Equal(300 - 6 - 1, (ha.Barras[0][1] - ha.Barras[0][0]).Comprimento, 6);
        // U do topo: um por vertical; das pontas: um por horizontal
        Assert.Equal(va.Barras.Count, m.Posicao("parede_fechamento_topo").Barras.Count);
        Assert.Equal(ha.Barras.Count, m.Posicao("parede_fechamento_inicio").Barras.Count);
        Assert.Equal(ha.Barras.Count, m.Posicao("parede_fechamento_fim").Barras.Count);
        var u = m.Posicao("parede_fechamento_topo").Barras[0];
        Assert.Equal(4, u.Count);
        Assert.True(u[0].Z < u[1].Z);                       // pernas para baixo
        Assert.Equal(u[1].Z, u[2].Z, 9);
        // verticais: do pé ao topo, por baixo das U
        Assert.True(va.Barras.All(b => b[b.Count - 1].Z < u[1].Z));
        Assert.Empty(m.Avisos);
    }

    [Fact]
    public void CantoComLEPeDasVerticais()
    {
        var ctx = Ctx();
        ComponentesPadrao.MalhaParede.Aplicar(ctx, Parede().Com("extremo_fim", "canto").Com("lado_canto_fim", 1).Com("perna_canto", 50)
            .Com("pe", 30).Com("faces", 2));
        var m = ctx.Concluir();
        var ha = m.Posicao("parede_horizontal_a"); var hb = m.Posicao("parede_horizontal_b");
        Assert.Null(m.Posicao("parede_fechamento_fim"));
        Assert.NotNull(m.Posicao("parede_fechamento_inicio"));
        // as duas faces vão até a face de fora da vizinha (300 + 10 − 3 − 0,5) e dobram 50 para +y
        foreach (var h in new[] { ha, hb })
        {
            var b = h.Barras[0];
            Assert.Equal(3, b.Count);
            Assert.Equal(300 + 10 - 3 - 0.5, b[1].X, 6);
            Assert.Equal(b[1].X, b[2].X, 9);
            Assert.Equal(50, b[2].Y - b[1].Y, 6);
            Assert.Equal(50, h.Parametros["perna_canto_fim"]);
        }
        // verticais param na face de dentro da vizinha; pé de 30 para fora
        var va = m.Posicao("parede_vertical_a");
        Assert.True(va.Barras.Max(b => b[1].X) <= 300 - 10 - 3 - 1 - 0.4 + 1e-6);
        Assert.Equal(30, va.Barras[0][0].Y - va.Barras[0][1].Y, 6);   // face a: para fora = +y
        var vb = m.Posicao("parede_vertical_b");
        Assert.Equal(-30, vb.Barras[0][0].Y - vb.Barras[0][1].Y, 6);
    }

    [Fact]
    public void EncontroAbreAsPernasESobrescritas()
    {
        var s = new[] { new Sobrescrita { Posicao = "parede_horizontal_a", Parametro = "espacamento", Valor = 10 } };
        var ctx = Ctx(s);
        ComponentesPadrao.MalhaParede.Aplicar(ctx, Parede().Com("extremo_inicio", "encontro").Com("espessura_vizinha", 25));
        var m = ctx.Concluir();
        var ha = m.Posicao("parede_horizontal_a"); var hb = m.Posicao("parede_horizontal_b");
        // encontro: cada face dobra para o seu lado; perna = traspasse (a cada 5 cm)
        Assert.True(ha.Barras[0][0].Y > ha.Barras[0][1].Y);
        Assert.True(hb.Barras[0][0].Y < hb.Barras[0][1].Y);
        Assert.Equal(-12.5 + 3 + 0.5, ha.Barras[0][1].X, 6);
        Assert.Equal(0, ha.Parametros["perna_canto_inicio"] % 5, 9);
        // espaçamento manual só na face a
        Assert.True(ha.Barras.Count > hb.Barras.Count);
        Assert.Contains("espacamento", ha.Manuais);
        Assert.DoesNotContain(m.Avisos, x => x.StartsWith("Sobrescrita ignorada"));
    }

    [Fact]
    public void ParedeInclinadaEUmaFace()
    {
        var ctx = Ctx();
        ComponentesPadrao.MalhaParede.Aplicar(ctx, new ArgumentosComponente()
            .Com("x0", 100).Com("y0", 100).Com("x1", 100).Com("y1", 400).Com("espessura", 20).Com("z0", 0).Com("z1", 200).Com("faces", 1));
        var m = ctx.Concluir();
        Assert.Null(m.Posicao("parede_horizontal_b"));
        Assert.Null(m.Posicao("parede_fechamento_topo"));
        var ha = m.Posicao("parede_horizontal_a");
        // eixo em y: face a = lado esquerdo de quem anda em +y = −x
        Assert.Equal(100 - 6.5, ha.Barras[0][0].X, 6);
        Assert.Equal(100 + 3 + 0.5, ha.Barras[0][0].Y, 6);
    }

    [Fact]
    public void PranchaDesenhaAParede()
    {
        var cfg = new ConfiguracaoArmadura();
        var ctx = Ctx();
        ComponentesPadrao.CorpoPrismatico.Aplicar(ctx, new ArgumentosComponente().Com("papel", "parede").Com("x0", 0).Com("y0", -10).Com("x1", 300).Com("y1", 10).Com("z0", 0).Com("z1", 250));
        ComponentesPadrao.MalhaParede.Aplicar(ctx, Parede().Com("extremo_fim", "canto"));
        var m = ctx.Concluir();
        var r = new DEB.Core.Motor.Prancha.GeradorPrancha(cfg).Gerar(ctx.Elemento, m, new PerfilDetalhamento { TipoElemento = "teste" });
        Assert.DoesNotContain(r.Avisos, x => x.Contains("erro"));
        foreach (var p in m.Posicoes) Assert.Contains(r.Barras, b => b.Nome == p.Chave);
    }
}
