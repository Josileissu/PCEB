using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Motor;
using DEB.Core.Motor.Componentes;
using DEB.Core.Motor.Fundacoes;
using Xunit;

namespace DEB.Core.Tests.Motor;

/// <summary>Componentes do motor (frente K) isolados, num elemento genérico (não é bloco: prova o reuso).</summary>
public class ComponentesTests
{
    private sealed class ElementoTeste : ElementoEstrutural { public override string Tipo => "teste"; }

    private static ArmaduraCalculada A(string f, double phi, int q = 0, double esp = 0) =>
        new ArmaduraCalculada { Funcao = f, Bitola = phi, Quantidade = q, Espacamento = esp };

    private static ContextoMotor Ctx(params ArmaduraCalculada[] arm) => Ctx(null, arm);

    private static ContextoMotor Ctx(Sobrescrita[] s, params ArmaduraCalculada[] arm)
    {
        var e = new ElementoTeste { Nome = "T1" };
        e.Armaduras.AddRange(arm);
        return new ContextoMotor(e, null, s);
    }

    private static ArgumentosComponente Args() => new ArgumentosComponente();
    private static List<(double X, double Y)> Ret(double w, double h) => DEB.Core.Motor.Geometria2D.Ret(0, 0, w, h);
    /// <summary>Ponto dentro do polígono convexo anti-horário (tolerância 1e-3 cm).</summary>
    private static bool Dentro(List<(double X, double Y)> pol, double x, double y)
    {
        for (int i = 0; i < pol.Count; i++)
        {
            var a = pol[i]; var b = pol[(i + 1) % pol.Count];
            double cr = (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
            if (cr / Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)) < -1e-3) return false;
        }
        return true;
    }

    private static double Seg(List<P3> v, int i) => (v[i + 1] - v[i]).Comprimento;

    [Fact]
    public void MalhaFaceNumRetanguloComAbertura()
    {
        var ctx = Ctx(A(Funcoes.Superior, 10, 0, 15));
        ctx.Dados["contorno"] = Ret(200, 100);
        ctx.Dados["abertura"] = new Retangulo(80, 30, 120, 70);
        ComponentesPadrao.MalhaFace.Aplicar(ctx, Args().Com("funcao", Funcoes.Superior).Com("chave", "sup_x").Com("angulo", 0)
            .Com("cobrimento", 4).Com("z_face", 50).Com("superior", 1).Com("perna", 15).Com("abertura", "abertura"));
        var p = ctx.Concluir().Posicao("sup_x");
        Assert.NotNull(p);
        int n = p.Parametros.ContainsKey("quantidade") ? (int)p.Parametros["quantidade"] : 0;
        Assert.True(n > 0);
        // as barras que passam na faixa da abertura (y ∈ (26, 74)) viram dois trechos; nenhuma atravessa x ∈ (76, 124)
        int cortadas = 0;
        foreach (var b in p.Barras)
        {
            double y = b[1].Y, x0 = Math.Min(b[1].X, b[2].X), x1 = Math.Max(b[1].X, b[2].X);
            if (y > 26 && y < 74) { cortadas++; Assert.True(x1 <= 76 + 1e-6 || x0 >= 124 - 1e-6); }
            else Assert.Equal(192, x1 - x0, 6);
            Assert.Equal(50, b[1].Z, 6);                 // superior: face de cima do trecho reto = z_face
            Assert.Equal(15, Seg(b, 0), 6);              // perna para baixo
            Assert.True(b[0].Z < b[1].Z);
        }
        Assert.True(cortadas >= 2 && cortadas % 2 == 0);
        // retas na linha do cobrimento (início/fim da distribuição) são descartadas, como no template
        Assert.InRange(p.Barras.Count, n - 2 + cortadas / 2, n + cortadas / 2);
        Assert.NotEmpty(p.ComprimentosVar);              // trechos menores que a barra inteira: VAR
        Assert.False(ctx.Variaveis.ContainsKey("z_topo_inferior"));   // superior não publica por padrão
    }

    [Fact]
    public void MalhaFaceInferiorEmYPublicaOTopoDaCamada()
    {
        var ctx = Ctx(A(Funcoes.Secundaria, 10, 0, 20));
        ctx.Dados["contorno"] = Ret(100, 100);
        ComponentesPadrao.MalhaFace.Aplicar(ctx, Args().Com("funcao", Funcoes.Secundaria).Com("chave", "s_y").Com("angulo", 90)
            .Com("cobrimento", 5).Com("z_face", 5).Com("camadas", 1));
        var p = ctx.Concluir().Posicao("s_y");
        Assert.Equal(FormaBarra.Reta, p.Forma);          // perna 0
        Assert.All(p.Barras, b => Assert.Equal(b[0].X, b[1].X, 6));   // barras em y
        Assert.Equal(6, p.Vertices[0].Z, 6);             // uma bitola (1 cm) acima da face
        Assert.Equal(7, ctx.Variaveis["z_topo_inferior"], 6);
    }

    [Fact]
    public void FaixaSobreDoisApoios()
    {
        var ctx = Ctx(A(Funcoes.Principal, 16, 5, 5));
        ctx.Dados["contorno"] = Ret(190, 70);
        ctx.Dados["estacas"] = new List<(double X, double Y)> { (35, 35), (155, 35) };
        ComponentesPadrao.FaixaSobreApoios.Aplicar(ctx, Args().Com("cobrimento", 4).Com("z_face", 5).Com("perna", 40));
        var m = ctx.Concluir();
        var p = m.Posicao("principal_x");
        Assert.NotNull(p);
        Assert.Null(m.Posicao("principal_y"));          // uma linha de apoios só, em x
        Assert.Equal(5, p.Distribuicao.Quantidade); Assert.Equal(1, p.Distribuicao.Repeticoes); Assert.Equal(5, p.Distribuicao.Espacamento);
        Assert.Equal(182, p.Parametros["comprimento_reto"]);   // 190 − 2·4 (sem lateral)
        Assert.Equal(40, Seg(p.Vertices, 0), 6);
        Assert.Equal(5, p.Vertices[1].Z, 6);
        Assert.Equal(35, p.Barras.Average(b => b[1].Y), 6);    // centrada na linha de apoios
        Assert.Equal(16, ctx.Variaveis["bitola_principal"]);
        Assert.Equal(6.6, ctx.Variaveis["z_topo_inferior"], 6);
    }

    [Fact]
    public void FaixaQueNaoCabeReduzOPassoESobrescritaManda()
    {
        var s = new[] { new Sobrescrita { Posicao = "f_x", Parametro = "perna", Valor = 80 } };
        var ctx = Ctx(s, A(Funcoes.Principal, 20, 9, 10));
        ctx.Dados["contorno"] = Ret(200, 60);
        ctx.Dados["estacas"] = new List<(double X, double Y)> { (30, 30), (170, 30) };
        ComponentesPadrao.FaixaSobreApoios.Aplicar(ctx, Args().Com("chave", "f").Com("cobrimento", 4).Com("z_face", 5).Com("perna", 30)
            .Com("limite_perna_z", 56).Com("texto_limite_perna", "do topo"));
        var m = ctx.Concluir();
        var p = m.Posicao("f_x");
        Assert.Equal(9, p.Barras.Count);
        Assert.True(p.Distribuicao.Espacamento < 10);    // 60 − 2·4 − ø não comporta 8 × 10
        Assert.Contains("perna", p.Manuais);
        Assert.Contains(m.Avisos, a => a.StartsWith("f_x: perna manual 80 cm passa do topo"));
    }

    [Fact]
    public void ContornoLateralNumHexagono()
    {
        var ctx = Ctx(A(Funcoes.Lateral, 8, 0, 15));
        var (cont, _) = GeometriaPlanta.Triangular(150, 40);
        ctx.Dados["contorno"] = cont;
        ComponentesPadrao.ContornoLateral.Aplicar(ctx, Args().Com("cobrimento", 4).Com("z1", 105));
        var p = ctx.Concluir().Posicao("lateral");
        Assert.Equal(FormaBarra.Estribo, p.Forma);
        Assert.Equal(ctx.Quantidade(97, 15), p.Barras.Count);
        var dentro = DEB.Core.Motor.Geometria2D.Encolher(cont, 4);
        foreach (var b in p.Barras)
        {
            Assert.All(b, q => Assert.Equal(b[0].Z, q.Z, 9));
            Assert.All(b, q => Assert.True(Dentro(dentro, q.X, q.Y), $"({q.X}; {q.Y}) fora do contorno − cobrimento"));
        }
        Assert.True(p.Barras[0][0].Z >= 4 && p.Barras.Last()[0].Z <= 101);
        Assert.Equal(2, p.Sobrepostos.Count);             // ganchos passando pelo canto
    }

    [Fact]
    public void EstribosVerticaisExcluemOTrecho()
    {
        var ctx = Ctx(A(Funcoes.EstriboVertical, 8, 0, 10));
        ComponentesPadrao.EstribosVerticais.Aplicar(ctx, Args().Com("cobrimento", 4).Com("comprimento", 200).Com("largura", 60).Com("altura", 50)
            .Com("excluir_x0", 80).Com("excluir_x1", 120));
        var p = ctx.Concluir().Posicao("estribo_vertical_x");
        Assert.All(p.Barras, b => Assert.True(b[0].X < 80 || b[0].X > 120));
        Assert.Equal(p.Barras.Count, (int)p.Parametros["quantidade"]);
        Assert.Equal(52, p.Parametros["largura"]); Assert.Equal(42, p.Parametros["altura"]);
    }

    [Fact]
    public void CaliceEmbutidoDePilar30x20()
    {
        var ctx = Ctx(A(Funcoes.CaliceTopo, 12.5, 3, 5), A(Funcoes.Calice, 10, 0, 10), A(Funcoes.CaliceVertical, 10, 2),
            A(Funcoes.Suspensao, 10, 2, 10), A(Funcoes.FundoCalice, 10, 3));
        ctx.Dados["contorno"] = Ret(200, 200);
        ComponentesPadrao.CaliceForma.Aplicar(ctx, Args().Com("centro_x", 100).Com("centro_y", 100).Com("pilar_bx", 30).Com("pilar_by", 20)
            .Com("folga", 4).Com("parede", 20).Com("colarinho", 0).Com("z_topo", 100).Com("embutimento", 50).Com("regularizacao", 5));
        var furo = ctx.Dado<Retangulo>("calice_furo");
        Assert.Equal(38, furo.X1 - furo.X0, 9); Assert.Equal(28, furo.Y1 - furo.Y0, 9);
        Assert.Same(furo, ctx.Dado<Retangulo>("abertura"));   // embutido: a malha é interrompida no furo
        Assert.Equal(50, ctx.Variaveis["z_fundo_pilar"]); Assert.Equal(45, ctx.Variaveis["z_fundo_calice"]);
        ComponentesPadrao.CaliceArmaduras.Aplicar(ctx, Args().Com("colarinho", 0).Com("cobrimento", 4).Com("cobrimento_calice", 2).Com("parede", 20)
            .Com("centro_x", 100).Com("centro_y", 100).Com("z_topo", 100).Com("embutimento", 50).Com("regularizacao", 5).Com("z_topo_inferior", 10));
        var m = ctx.Concluir();
        Assert.Contains(m.Corpos, k => k.Papel == "furo_calice" && k.Vazio && k.Z0 == 45 && k.Z1 == 100);
        Assert.Contains(m.Corpos, k => k.Papel == "regularizacao");
        Assert.DoesNotContain(m.Corpos, k => k.Papel == "colarinho");
        var topo = m.Posicao("calice_topo");
        Assert.Equal(70, topo.Vertices.Max(v => v.X) - topo.Vertices.Min(v => v.X), 6);   // 38 + 2·20 − 2·4
        Assert.Equal(60, topo.Vertices.Max(v => v.Y) - topo.Vertices.Min(v => v.Y), 6);
        Assert.Equal(3, topo.Barras.Count);
        foreach (var k in new[] { "calice_grampos_topo_x", "calice_grampos_topo_y", "calice", "calice_grampos_x", "calice_grampos_y", "calice_vertical", "suspensao", "fundo_calice" })
            Assert.NotNull(m.Posicao(k));
        Assert.Equal(8, m.Posicao("calice_vertical").Barras.Count);
        Assert.Equal(86, m.Posicao("calice_vertical").Parametros["altura"]);   // 100 − 4 − 10
        Assert.Equal(41, m.Posicao("fundo_calice").Vertices[0].Z, 6);          // 100 − 50 − 5 − 4
    }

    [Fact]
    public void VerificacoesDoCaliceSeparadas()
    {
        var ctx = Ctx();
        ctx.Dados["contorno"] = Ret(100, 100);
        ctx.Dados["calice_furo"] = new Retangulo(-5, 30, 60, 70);
        ComponentesPadrao.VerificacoesCalice.Aplicar(ctx, Args().Com("pilar_bx", 30).Com("pilar_by", 20).Com("embutimento", 30)
            .Com("interface", "lisa").Com("parede", 12).Com("z_topo", 45));
        var av = ctx.Concluir().Avisos;
        Assert.Contains(av, a => a.StartsWith("Embutimento 30 cm < 40 cm"));
        Assert.Contains(av, a => a.Contains("1,5·h = 45 cm"));
        Assert.Contains(av, a => a.StartsWith("Parede do colarinho 12 cm"));
        Assert.Contains(av, a => a.StartsWith("Altura sob o cálice 15 cm"));
        Assert.Contains(av, a => a.StartsWith("Colarinho maior"));
    }

    [Fact]
    public void ArranqueDePilarNumaLaje()
    {
        var ctx = Ctx(A(Funcoes.Arranque, 10), A(Funcoes.ArranqueEstribo, 5, 2));
        ComponentesPadrao.Arranque.Aplicar(ctx, Args().Com("centro_x", 50).Com("centro_y", 50).Com("pilar_bx", 40).Com("pilar_by", 20)
            .Com("nx", 3).Com("ny", 2).Com("z_topo", 30).Com("z_topo_inferior", 6).Com("espera", 60).Com("pe", 15));
        var m = ctx.Concluir();
        var a = m.Posicao("arranque");
        Assert.Equal(6, a.Barras.Count);
        Assert.Equal(90, a.Vertices.Last().Z, 6);          // topo + espera
        Assert.Equal(6, a.Vertices[1].Z, 6);               // pé sobre a armadura inferior
        Assert.Contains(m.Avisos, x => x.StartsWith("Arranque: trecho reto"));   // 24 cm < lb
        Assert.Equal(2, m.Posicao("arranque_estribo").Barras.Count);
    }

    [Fact]
    public void BitolaEfetivaLeASobrescritaSemConsumir()
    {
        var s = new[] { new Sobrescrita { Posicao = "lateral", Parametro = "bitola", Valor = 12.5 } };
        var ctx = Ctx(s, A(Funcoes.Lateral, 8, 0, 15));
        ComponentesPadrao.BitolaEfetiva.Aplicar(ctx, Args().Com("funcao", Funcoes.Lateral));
        Assert.Equal(12.5, ctx.Variaveis["bitola_lateral"]);
        Assert.Contains(ctx.Concluir().Avisos, x => x.StartsWith("Sobrescrita ignorada"));   // não foi consumida
    }

    [Fact]
    public void AtivoZeroPulaOComponente()
    {
        var ctx = Ctx(A(Funcoes.Lateral, 8, 0, 15));
        ComponentesPadrao.ContornoLateral.Aplicar(ctx, Args().Com("ativo", 0).Com("z1", 50));   // nem precisa do contorno
        ComponentesPadrao.Aviso.Aplicar(ctx, Args().Com("ativo", 0).Com("texto", "x"));
        Assert.Empty(ctx.Concluir().Posicoes);
        Assert.Empty(ctx.Modelo.Avisos);
    }

    [Fact]
    public void ApoiosCorposENiveis()
    {
        var ctx = Ctx();
        ComponentesPadrao.ApoiosEstacas.Aplicar(ctx, Args().Com("nx", 3).Com("ny", 2).Com("esp_x", 100).Com("esp_y", 90).Com("dist_face", 30));
        Assert.Equal(260, ctx.Variaveis["largura_x"]); Assert.Equal(150, ctx.Variaveis["largura_y"]);
        Assert.Equal(6, ctx.Dado<List<(double X, double Y)>>("estacas").Count);
        Assert.Equal("grade", ctx.Dado<string>("arranjo"));
        ComponentesPadrao.CorpoPrismatico.Aplicar(ctx, Args().Com("papel", "sapata").Com("z1", 60));
        ComponentesPadrao.CorposCirculares.Aplicar(ctx, Args().Com("raio", 20).Com("z0", -100).Com("z1", 5));
        ComponentesPadrao.Niveis.Aplicar(ctx, Args().Com("fundo", 0).Com("topo", 60));
        var m = ctx.Concluir();
        Assert.Equal(260, m.Corpos.Single(k => k.Papel == "sapata").Planta.Max(q => q.X));
        Assert.Equal(6, m.Corpos.Count(k => k.Papel == "estaca"));
        Assert.Equal(new[] { "fundo", "topo" }, m.Niveis.Keys);
        Assert.Throws<InvalidOperationException>(() =>
            ComponentesPadrao.MalhaFace.Aplicar(Ctx(A(Funcoes.Secundaria, 10, 0, 15)), Args().Com("funcao", Funcoes.Secundaria)));   // falta "contorno"
    }

    [Fact]
    public void CatalogoDocumentaArgumentosEVariaveis()
    {
        var cat = ComponentesPadrao.Catalogo();
        Assert.Equal(16, cat.Tipos.Count);
        foreach (var t in cat.Tipos)
        {
            var c = cat.Obter(t);
            Assert.Contains("ativo", c.Argumentos.Keys);
            Assert.True(c.Argumentos.Count >= 2, t);
            Assert.All(((ComponenteBase)c).VariaveisPublicadas, n => Assert.Matches("^[a-z0-9_]+$", n));
        }
        Assert.Contains("z_topo_inferior", ((ComponenteBase)cat.Obter("malha_face")).VariaveisPublicadas);
        Assert.Equal(new[] { "bitola_lateral" }, ((ComponenteBase)cat.Obter("bitola_efetiva")).VariaveisPublicadasCom(Args().Com("funcao", "lateral")));
    }
}
