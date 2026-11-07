using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Motor;
using DEB.Core.Motor.Componentes;
using DEB.Core.Templates.PilarPreMoldado;
using Xunit;

namespace DEB.Core.Tests.Motor;

/// <summary>Componente <c>barras_secao_longitudinal</c> e o cálculo comum <see cref="SecaoBarras"/> (o mesmo do pilar).</summary>
public class SecaoLongitudinalTests
{
    private sealed class ElementoTeste : ElementoEstrutural { public override string Tipo => "teste"; }

    private static ContextoMotor Ctx(Sobrescrita[] s, params ArmaduraCalculada[] arm)
    {
        var e = new ElementoTeste { Nome = "T1", Fck = 40 };
        e.Armaduras.AddRange(arm);
        return new ContextoMotor(e, null, s);
    }

    private static ArmaduraCalculada A(string f, double phi) => new ArmaduraCalculada { Funcao = f, Bitola = phi };

    private static ArgumentosComponente Pilar50(double altura) => new ArgumentosComponente()
        .Com("centro_x", 100).Com("centro_y", 200).Com("bx", 50).Com("by", 50).Com("cobrimento", 3)
        .Com("funcao_estribo", "estribo").Com("z_inicio", 0).Com("z_fim", altura);

    [Fact]
    public void MesmasPosicoesDoPilarPreMoldado()
    {
        var cob = new[] { 3.0, 3, 3, 3 };
        var pilar = TemplatePilarPreMoldado.PosicoesBarras(50, 50, cob, 8, 25, 3, 5, 20, 2, 1, 16);
        var motor = SecaoBarras.Posicoes(50, 50, cob, 8, 25, 3, 5, 20, 2, 1, 16);
        Assert.Equal(pilar.Select(b => (b.X, b.Y, b.Bitola, b.Grupo)), motor.Select(b => (b.X, b.Y, b.Bitola, b.Grupo)));
    }

    [Fact]
    public void CantosEmLEFacesSemEmenda()
    {
        var ctx = Ctx(null, A("longitudinal", 25), A("vizinha", 20), A("face", 16), A("estribo", 8));
        ComponentesPadrao.SecaoLongitudinal.Aplicar(ctx, Pilar50(600).Com("por_canto", 3).Com("funcao_vizinha", "vizinha")
            .Com("funcao_face", "face").Com("n_face_x", 2).Com("n_face_y", 1));
        var m = ctx.Concluir();
        var canto = m.Posicao("longitudinal_canto");
        Assert.Equal(4, canto.Barras.Count);
        Assert.Equal(25, canto.Bitola);
        // canto inferior esquerdo: centro − 25 + cobrimento 3 + estribo 0,8 + raio 1,25
        Assert.Equal(100 - 25 + 3 + 0.8 + 1.25, canto.Barras[0][0].X, 6);
        Assert.Equal(200 - 25 + 3 + 0.8 + 1.25, canto.Barras[0][0].Y, 6);
        Assert.Equal(8, m.Posicao("longitudinal_vizinha").Barras.Count);
        Assert.Equal(4, m.Posicao("longitudinal_face_x").Barras.Count);   // 2 por face × 2 faces
        Assert.Equal(2, m.Posicao("longitudinal_face_y").Barras.Count);
        Assert.All(m.Posicoes, p => Assert.All(p.Barras, b => Assert.Equal(600, b[1].Z - b[0].Z, 6)));
        Assert.Empty(m.Avisos);
    }

    [Fact]
    public void EmendasAlternadasComoNoPilar()
    {
        var cfg = new ConfiguracaoArmadura();
        var ctx = Ctx(null, A("longitudinal", 20), A("estribo", 6.3));
        ComponentesPadrao.SecaoLongitudinal.Aplicar(ctx, Pilar50(1500).Com("comercial", 1200).Com("n_face_x", 1));
        var m = ctx.Concluir();
        double lap = SecaoBarras.Traspasse(20, "CA-50", 40, false, cfg.Barras);
        Assert.Equal(lap, m.Posicao("longitudinal_canto").Parametros["traspasse"]);
        // grupo A: 1200 + resto com traspasse; grupo B: começa com a 1ª alternada
        var a1 = m.Posicao("longitudinal_canto");
        var a2 = m.Posicao("longitudinal_canto_2");
        var b1 = m.Posicao("longitudinal_canto_b1");
        Assert.Equal(1200, a1.Parametros["comprimento"], 6);
        Assert.Equal(1200 - lap, a2.Barras[0][0].Z, 6);
        Assert.Equal(SecaoBarras.PrimeiraAlternada(1200, lap), b1.Parametros["comprimento"], 6);
        Assert.Equal(2, a1.Barras.Count);                  // 4 cantos: 2 no A, 2 no B
        Assert.Equal(2, b1.Barras.Count);
        // as emendas do A e do B ficam afastadas de pelo menos 1,3 traspasse
        Assert.True(Math.Abs(a1.Parametros["comprimento"] - b1.Parametros["comprimento"]) >= 1.3 * lap - 1e-6);
        // as barras A e B não ocupam o mesmo ponto da seção
        var xyA = a1.Barras.Select(b => (b[0].X, b[0].Y)).ToList();
        Assert.DoesNotContain(b1.Barras, b => xyA.Contains((b[0].X, b[0].Y)));
    }

    [Fact]
    public void MesmaSecaoESobrescritas()
    {
        var s = new[]
        {
            new Sobrescrita { Posicao = "longitudinal_canto", Parametro = "bitola", Valor = 16 },
            new Sobrescrita { Posicao = "longitudinal_canto", Parametro = "traspasse", Valor = 100 },
        };
        var ctx = Ctx(s, A("longitudinal", 20), A("estribo", 6.3));
        ComponentesPadrao.SecaoLongitudinal.Aplicar(ctx, Pilar50(1500).Com("comercial", 1200).Com("mesma_secao", 1));
        var m = ctx.Concluir();
        var a1 = m.Posicao("longitudinal_canto");
        var a2 = m.Posicao("longitudinal_canto_2");
        Assert.Equal(16, a1.Bitola);
        Assert.Contains("bitola", a1.Manuais);
        Assert.Equal(4, a1.Barras.Count);
        Assert.Null(m.Posicao("longitudinal_canto_b1"));
        Assert.Equal(1200 - 100, a2.Barras[0][0].Z, 6);
        Assert.Equal(100 - 25 + 3 + 0.63 + 0.8, a1.Barras[0][0].X, 6);   // raio do ø16
        Assert.DoesNotContain(m.Avisos, x => x.StartsWith("Sobrescrita ignorada"));
    }

    [Fact]
    public void GramposAmarramAsIntermediariasComGanchos135ParaDentro()
    {
        var ctx = Ctx(null, A("longitudinal", 20), A("estribo", 6.3), new ArmaduraCalculada { Funcao = "grampo", Bitola = 6.3, Espacamento = 15 });
        ComponentesPadrao.GramposSecao.Aplicar(ctx, new ArgumentosComponente()
            .Com("centro_x", 100).Com("centro_y", 200).Com("bx", 50).Com("by", 50).Com("cobrimento", 3).Com("funcao_estribo", "estribo")
            .Com("n_face_y", 2).Com("z_inicio", 0).Com("z_fim", 150));
        var m = ctx.Concluir();
        var gx = m.Posicao("grampo_x");
        var gy = m.Posicao("grampo_y");
        Assert.Equal(2, gx.Parametros["linhas"]);          // um por par de intermediárias das faces ±x
        Assert.Equal(1, gy.Parametros["linhas"]);          // faces ±y sem intermediárias: no meio
        int n = (int)gx.Parametros["quantidade"];
        Assert.Equal(n * 2, gx.Barras.Count);
        Assert.Equal(44, gx.Parametros["comprimento_reto"], 6);   // 50 − 2·3: medida externa do estribo
        var b = gx.Barras[0];
        Assert.Equal(4, b.Count);
        Assert.Equal(44 - 0.63, (b[2] - b[1]).Comprimento, 6);    // eixo do trecho reto
        // as y das intermediárias da face (mesmo cálculo das longitudinais)
        var faces = SecaoBarras.Posicoes(50, 50, new[] { 3.0, 3, 3, 3 }, 6.3, 20, 1, 5, 20, 0, 2, 20).Where(s => s.Grupo == "faceH").Select(s => 200 - 25 + s.Y);
        Assert.Contains(faces, y => Math.Abs(y - b[1].Y) < 1e-6);
        // ganchos a 45°, para dentro (rumo ao centro da seção) e todos no mesmo z
        var h0 = b[0] - b[1];
        Assert.Equal(h0.X, Math.Abs(h0.Y) * Math.Sign(h0.X), 6);
        Assert.Equal(Math.Sign(200 - b[1].Y), Math.Sign(h0.Y));
        Assert.All(b, q => Assert.Equal(b[0].Z, q.Z, 9));
        Assert.Equal(new[] { DEB.Core.Modelo.FormatoSegmento.Gancho45, DEB.Core.Modelo.FormatoSegmento.Linha, DEB.Core.Modelo.FormatoSegmento.Gancho45 }, gx.Formatos);
    }

    [Fact]
    public void PranchaDesenhaLongitudinaisEGrampos()
    {
        var cfg = new ConfiguracaoArmadura();
        var ctx = Ctx(null, A("longitudinal", 20), A("estribo", 6.3), new ArmaduraCalculada { Funcao = "grampo", Bitola = 6.3, Espacamento = 15 });
        ComponentesPadrao.CorpoPrismatico.Aplicar(ctx, new ArgumentosComponente().Com("papel", "pilar").Com("x0", 75).Com("y0", 175).Com("x1", 125).Com("y1", 225).Com("z0", 0).Com("z1", 1500));
        ComponentesPadrao.SecaoLongitudinal.Aplicar(ctx, Pilar50(1500).Com("comercial", 1200).Com("n_face_y", 2));
        ComponentesPadrao.GramposSecao.Aplicar(ctx, new ArgumentosComponente()
            .Com("centro_x", 100).Com("centro_y", 200).Com("bx", 50).Com("by", 50).Com("funcao_estribo", "estribo")
            .Com("n_face_y", 2).Com("z_inicio", 0).Com("z_fim", 1500));
        var m = ctx.Concluir();
        var r = new DEB.Core.Motor.Prancha.GeradorPrancha(cfg).Gerar(ctx.Elemento, m, new PerfilDetalhamento { TipoElemento = "teste" });
        Assert.DoesNotContain(r.Avisos, x => x.Contains("erro"));
        foreach (var p in m.Posicoes)
            Assert.Contains(r.Barras, b => b.Nome == p.Chave);
        double aco = r.Barras.Sum(b => DEB.Core.Templates.MedidaBarra.Peso(b, cfg));
        Assert.True(aco > 0);
    }

    [Fact]
    public void FeixesComoNoPilar()
    {
        var ctx = Ctx(null, A("longitudinal", 20), A("estribo", 8));
        ComponentesPadrao.SecaoLongitudinal.Aplicar(ctx, Pilar50(600).Com("feixe", 2).Com("n_face_x", 1).Com("feixe_face", 3));
        var m = ctx.Concluir();
        var canto = m.Posicao("longitudinal_canto");
        Assert.Equal(8, canto.Parametros["quantidade"]);
        Assert.Equal(8, canto.Barras.Count);
        Assert.Equal(2, canto.Parametros["feixe"]);
        // centro do feixe do canto: cobrimento + estribo + raio do feixe (= φ para 2 barras); barras a ±φ/2 dele
        double c0 = 100 - 25 + 3 + 0.8 + SecaoBarras.RaioFeixe(20, 2);
        Assert.Equal(c0 - 1, canto.Barras[0][0].X, 6);
        Assert.Equal(c0 + 1, canto.Barras[1][0].X, 6);
        // mesmas posições da seção do pilar (PosicoesLinhas usa o mesmo raio de feixe)
        Assert.Equal(TemplatePilarPreMoldado.RaioFeixe(16, 3), SecaoBarras.RaioFeixe(16, 3));
        Assert.Equal(6, m.Posicao("longitudinal_face_x").Barras.Count);   // 1 por face × 2 faces × 3
    }

    [Fact]
    public void SemEmendaAvisaQuandoPassaDoComercial()
    {
        var ctx = Ctx(null, A("longitudinal", 20));
        ComponentesPadrao.SecaoLongitudinal.Aplicar(ctx, Pilar50(1500).Com("comercial", 1200).Com("emendas", 0));
        var m = ctx.Concluir();
        Assert.Single(m.Posicoes);
        Assert.Contains(m.Avisos, x => x.Contains("sem emendas"));
    }
}
