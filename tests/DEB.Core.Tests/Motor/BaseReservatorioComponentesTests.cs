using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Motor;
using DEB.Core.Motor.Componentes;
using DEB.Core.Templates;
using DEB.Core.Templates.BaseReservatorio;
using Xunit;

namespace DEB.Core.Tests.Motor;

/// <summary>Componentes para a base de reservatório no motor (etapa 7): forma dada com estacas em anéis, U e barras de borda.
/// Os números são comparados com o template atual da base.</summary>
public class BaseReservatorioComponentesTests
{
    private sealed class ElementoTeste : ElementoEstrutural { public override string Tipo => "teste"; }

    private static ContextoMotor Ctx(params ArmaduraCalculada[] arm)
    {
        var e = new ElementoTeste { Nome = "BASE" };
        e.Armaduras.AddRange(arm);
        return new ContextoMotor(e, null, null);
    }

    private static ArgumentosComponente Args() => new ArgumentosComponente();

    [Fact]
    public void EstacasEmAneisNaFormaCircular()
    {
        var ctx = Ctx();
        ComponentesPadrao.ApoiosEstacas.Aplicar(ctx, Args().Com("forma", "circular").Com("diametro", 700).Com("arranjo", "aneis")
            .Com("anel1_n", 8).Com("anel1_raio", 275).Com("anel1_angulo", 90).Com("anel2_n", 6).Com("anel2_raio", 130).Com("central", 1));
        var est = ctx.Dado<List<(double X, double Y)>>("estacas");
        Assert.Equal(15, est.Count);
        Assert.Equal(1, ctx.Var("circular", 0));
        Assert.Equal(350, ctx.Var("raio", 0));
        Assert.Equal(700, ctx.Var("largura_x", 0));
        // 1ª do anel externo a 90° (em cima); a central no centro
        Assert.Equal(350, est[0].X, 6); Assert.Equal(350 + 275, est[0].Y, 6);
        Assert.Equal((350.0, 350.0), (est[14].X, est[14].Y));
        // todos os vértices do contorno no círculo
        var cont = ctx.Dado<List<(double X, double Y)>>("contorno");
        Assert.All(cont, p => Assert.Equal(350, Math.Sqrt((p.X - 350) * (p.X - 350) + (p.Y - 350) * (p.Y - 350)), 6));
        Assert.True(Geometria2D.Area(cont) > 0);   // anti-horário
    }

    [Fact]
    public void GradeCentradaNaFormaRetangular()
    {
        var ctx = Ctx();
        ComponentesPadrao.ApoiosEstacas.Aplicar(ctx, Args().Com("forma", "retangular").Com("lx", 680).Com("ly", 500)
            .Com("nx", 5).Com("ny", 3).Com("esp_x", 150).Com("esp_y", 150));
        var est = ctx.Dado<List<(double X, double Y)>>("estacas");
        Assert.Equal(15, est.Count);
        Assert.Equal(340, est.Average(p => p.X), 6);
        Assert.Equal(250, est.Average(p => p.Y), 6);
        Assert.Equal(0, ctx.Var("circular", 1));
    }

    [Fact]
    public void BordaUCircularComoNoTemplate()
    {
        var ctx = Ctx(new ArmaduraCalculada { Funcao = "borda_u", Bitola = 10, Espacamento = 18 });
        ComponentesPadrao.ApoiosEstacas.Aplicar(ctx, Args().Com("forma", "circular").Com("diametro", 700).Com("arranjo", "nenhum"));
        ComponentesPadrao.BordaU.Aplicar(ctx, Args().Com("cobrimento", 4).Com("z_inferior", 5).Com("z_superior", 81).Com("aba", 100));
        var m = ctx.Concluir();
        var u = m.Posicao("borda_u");
        // o template: round(π·(D − 2·cob) / esp)
        Assert.Equal(Math.Round(Math.PI * (700 - 8) / 18), u.Parametros["quantidade"]);
        var b = u.Barras[0];
        Assert.Equal(4, b.Count);
        // perna vertical a cobrimento + φ/2 da face; abas de 100 (externas) para dentro; altura externa 76
        double rPerna = Math.Sqrt((b[1].X - 350) * (b[1].X - 350) + (b[1].Y - 350) * (b[1].Y - 350));
        Assert.Equal(350 - 4 - 0.5, rPerna, 6);
        double rAba = Math.Sqrt((b[0].X - 350) * (b[0].X - 350) + (b[0].Y - 350) * (b[0].Y - 350));
        Assert.Equal(350 - 4 - 100, rAba, 6);
        Assert.Equal(76 - 1, b[1].Z - b[2].Z, 6);
    }

    [Fact]
    public void BordaRetangularPorLado()
    {
        var ctx = Ctx(new ArmaduraCalculada { Funcao = "borda_u", Bitola = 10, Espacamento = 18 },
                      new ArmaduraCalculada { Funcao = "borda", Bitola = 16, Quantidade = 3 });
        ComponentesPadrao.ApoiosEstacas.Aplicar(ctx, Args().Com("forma", "retangular").Com("lx", 680).Com("ly", 500).Com("arranjo", "nenhum"));
        ComponentesPadrao.BordaU.Aplicar(ctx, Args().Com("cobrimento", 4).Com("z_inferior", 5).Com("z_superior", 81));
        ComponentesPadrao.BarrasBorda.Aplicar(ctx, Args().Com("cobrimento", 4).Com("bitola_u", 10).Com("z_inferior", 5).Com("z_superior", 81));
        var m = ctx.Concluir();
        var cfg = new ConfiguracaoArmadura();
        // divisão igual de cobrimento a cobrimento (2026-11-08): teto(L / esp) + 1
        int Q(double L) => (int)Math.Ceiling(L / 18 - 1e-6) + 1;
        Assert.Equal(2 * Q(672) + 2 * Q(492), m.Posicao("borda_u").Parametros["quantidade"]);
        var bx = m.Posicao("borda_x"); var by = m.Posicao("borda_y");
        Assert.Equal(6, bx.Barras.Count);                   // 3 por lado × 2 lados
        Assert.Equal(6, by.Barras.Count);
        Assert.Equal(680 - 8, bx.Parametros["comprimento_reto"]);   // como o template: W − 2·cob
        Assert.All(bx.Barras, b => Assert.Equal(672, (b[1] - b[0]).Comprimento, 6));
        // por dentro da perna do U
        Assert.Equal(4 + 1 + 0.8, bx.Barras[0][0].Y, 6);
    }

    [Fact]
    public void AnelCircularComArcosAteAPrancha()
    {
        var cfg = new ConfiguracaoArmadura();
        var ctx = Ctx(new ArmaduraCalculada { Funcao = "borda", Bitola = 12.5, Quantidade = 4 });
        ComponentesPadrao.ApoiosEstacas.Aplicar(ctx, Args().Com("forma", "circular").Com("diametro", 700).Com("arranjo", "nenhum"));
        ComponentesPadrao.CorpoPrismatico.Aplicar(ctx, Args().Com("papel", "bloco").Com("z0", 0).Com("z1", 85));
        ComponentesPadrao.BarrasBorda.Aplicar(ctx, Args().Com("cobrimento", 4).Com("bitola_u", 10).Com("z_inferior", 5).Com("z_superior", 81)
            .Com("emenda", 80));
        var m = ctx.Concluir();
        var anel = m.Posicao("borda_anel");
        Assert.NotNull(anel);
        Assert.Equal(4, anel.Barras.Count);
        // raio como no template: R − cob − φU − φ/2
        double ra = 350 - 4 - 1.0 - 0.625;
        Assert.Equal(ra, anel.Parametros["raio"], 6);
        Assert.True(anel.TemArcos && anel.Fechada);
        Assert.Equal(80, anel.Parametros["emenda"]);

        // arcos no JSON do modelo
        var lido = DEB.Core.Motor.Json.SerializacaoMotor.LerModelo(DEB.Core.Motor.Json.SerializacaoMotor.GravarModelo(m)).Posicao("borda_anel");
        Assert.Equal(anel.Bulges, lido.Bulges);
        Assert.Equal(anel.NormalArcos, lido.NormalArcos);

        // prancha: a barra desenhada é um círculo (2 arcos) e o aço é o de um anel de raio ra
        var r = new DEB.Core.Motor.Prancha.GeradorPrancha(cfg).Gerar(ctx.Elemento, m, new PerfilDetalhamento { TipoElemento = "teste" });
        var b = r.Barras.First(x => x.Nome == "borda_anel");
        Assert.Equal(2, b.Vertices.Count);
        Assert.Equal(2, b.Bulges.Count);
        Assert.All(b.Bulges, g => Assert.Equal(1, Math.Abs(g), 9));
        Assert.True(b.Fechada);
        double L = MedidaBarra.Unitario(b, cfg);
        Assert.InRange(L, 2 * Math.PI * ra * 0.98, 2 * Math.PI * (ra + 1) * 1.02);
        Assert.DoesNotContain(r.Avisos, x => x.Contains("erro"));
    }

    [Fact]
    public void AmostrarArcoNoEspaco()
    {
        // semicírculo de (10,0,0) a (-10,0,0) anti-horário visto de +z passa por (0,10,0); visto de -z, por (0,-10,0)
        var v = new List<P3> { new P3(10, 0, 0), new P3(-10, 0, 0) };
        var a = Arcos.Amostrar(v, new[] { 1.0 }, new P3(0, 0, 1), false, 4);
        Assert.Equal(5, a.Count);
        Assert.Equal(10, a[2].Y, 9);
        Assert.All(a, q => Assert.Equal(10, q.Comprimento, 9));
        var b = Arcos.Amostrar(v, new[] { 1.0 }, new P3(0, 0, -1), false, 4);
        Assert.Equal(-10, b[2].Y, 9);
        // quarto de círculo (bulge tan(22,5°)) num plano vertical
        var c = Arcos.Amostrar(new List<P3> { new P3(0, 0, 0), new P3(10, 0, 10) },
            new[] { Math.Tan(Math.PI / 8) }, new P3(0, -1, 0), false, 8);
        Assert.All(c, q => Assert.Equal(10, (q - new P3(0, 0, 10)).Comprimento, 6));
    }

    [Fact]
    public void MalhaInferiorNaBaseCircularTemOsComprimentosDoTemplate()
    {
        // com cobrimento na normal da face (como o template: corda do círculo R − cobrimento), recortando pelo polígono de
        // 360 lados (cordas a menos de 0,1 cm das do círculo); a malha arredonda ao cm
        var ctx = Ctx(new ArmaduraCalculada { Funcao = "inferior", Bitola = 12.5, Espacamento = 14 });
        ComponentesPadrao.ApoiosEstacas.Aplicar(ctx, Args().Com("forma", "circular").Com("diametro", 700).Com("arranjo", "nenhum"));
        ComponentesPadrao.MalhaFace.Aplicar(ctx, Args().Com("funcao", "inferior").Com("chave", "inf_x").Com("cobrimento", 4).Com("z_face", 5)
            .Com("cobrimento_normal", 1));
        var p = ctx.Concluir().Posicao("inf_x");
        double r = 350 - 4;
        foreach (var b in p.Barras)
        {
            double t = b[0].Y - 350;
            double L = (b[b.Count - 1] - b[0]).Comprimento;
            double corda = 2 * Math.Sqrt(r * r - t * t);
            Assert.InRange(L, corda - 0.6, corda + 0.5);
        }
    }
}
