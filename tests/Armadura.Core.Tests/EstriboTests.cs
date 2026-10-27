using System;
using System.Linq;
using Armadura.Core.Configuracao;
using Armadura.Core.Estribos;
using Armadura.Core.Modelo;
using Xunit;

namespace Armadura.Core.Tests;

public class EstriboTests
{
    private static OpcoesEstribo Op(FormatoEstribo f, int ramos = 2) => new() { Formato = f, Ramos = ramos };

    [Fact]
    public void SimplesFechadoComDoisGanchos()
    {
        var formas = GeradorEstribo.Gerar(Op(FormatoEstribo.Simples), 15, 45, 0, 5);
        var f = Assert.Single(formas);
        Assert.True(f.Armadura);
        // gancho, passagem do canto, cima, direita, baixo, esquerda, passagem, gancho
        Assert.Equal(new[] { 5.0, 2.5, 15, 45, 15, 45, 2.5, 5 }, f.Comprimentos().Select(c => Math.Round(c, 6)));
        Assert.Equal(0, f.Vertices[2].X, 9); // ganchos no canto superior esquerdo
        Assert.Equal(0, f.Vertices[2].Y, 9);
        Assert.Equal(FormatoSegmento.Gancho90, f.Formatos.First()); // padrão: gancho 90°
        Assert.Equal(FormatoSegmento.Gancho90, f.Formatos.Last());
        Assert.Equal(f.Vertices.Count - 1, f.Formatos.Count);
        Assert.Equal(new[] { 1, 6 }, f.Sobrepostos.OrderBy(i => i));
        Assert.Equal(new[] { 2, 5 }, f.Cotados.OrderBy(i => i));
    }

    /// <summary>
    /// Exemplo do escritório (Drawing1, 2026-10-27): 56 × 13,32, gancho 90° de 6,25, passagem 2,5 — espelhado
    /// para o canto superior esquerdo (decisão do usuário: todos os estribos com ganchos nesse canto).
    /// </summary>
    [Fact]
    public void FechadoSegueOExemploDoEscritorio()
    {
        var o = Op(FormatoEstribo.Simples);
        o.Gancho = FormatoSegmento.Gancho90;
        var f = GeradorEstribo.Gerar(o, 56, 13.32, 0, 6.25, 2.5)[0];
        // exemplo com origem no canto inferior esquerdo; aqui a origem é o superior esquerdo (y − 13,32)
        var esperado = new (double x, double y)[]
        {
            (6.25, 10.82), (0, 10.82), (0, 13.32), (56, 13.32), (56, 0), (0, 0), (0, 13.32), (2.5, 13.32), (2.5, 7.07),
        };
        Assert.Equal(esperado.Length, f.Vertices.Count);
        for (int i = 0; i < esperado.Length; i++)
        {
            Assert.Equal(esperado[i].x, f.Vertices[i].X, 6);
            Assert.Equal(esperado[i].y - 13.32, f.Vertices[i].Y, 6);
        }
    }

    private static bool Dentro(System.Collections.Generic.IReadOnlyList<(double X, double Y)> p, double x, double y)
    {
        bool r = false;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
            if ((p[i].Y > y) != (p[j].Y > y) && x < (p[j].X - p[i].X) * (y - p[i].Y) / (p[j].Y - p[i].Y) + p[i].X) r = !r;
        return r;
    }

    [Fact]
    public void PoligonalQualquerViraEstriboComGanchosParaDentro()
    {
        // "L" desenhado no sentido anti-horário; ganchos no canto superior mais à esquerda (0, 60)
        var l = new[] { (0.0, 0.0), (40.0, 0.0), (40.0, 20.0), (20.0, 20.0), (20.0, 60.0), (0.0, 60.0) };
        int canto = GeradorEstribo.CantoPadrao(l);
        Assert.Equal(5, canto);
        foreach (var g in new[] { FormatoSegmento.Gancho90, FormatoSegmento.Gancho45 })
        {
            var f = GeradorEstribo.FechadoPoligono(l, canto, g, 7, 2.5);
            Assert.Equal(l.Length + 5, f.Vertices.Count);
            Assert.Equal(l.Length + 4, f.Formatos.Count);
            Assert.Equal(0, f.Vertices[2].X, 9);   // passa pelo canto escolhido
            Assert.Equal(60, f.Vertices[2].Y, 9);
            Assert.True(Dentro(l, f.Vertices[0].X, f.Vertices[0].Y), "1º gancho para dentro");
            Assert.True(Dentro(l, f.Vertices[^1].X, f.Vertices[^1].Y), "2º gancho para dentro");
            Assert.Equal(new[] { 1, l.Length + 2 }, f.Sobrepostos.OrderBy(i => i));
            Assert.Equal(l.Length, f.Cotados.Count); // não retangular: todos os lados cotados
            // a volta tem o perímetro da poligonal
            var c = f.Comprimentos();
            Assert.Equal(200, c.Skip(2).Take(l.Length).Sum(), 6);
        }
    }

    [Fact]
    public void CantosDosGanchos()
    {
        var ret = new[] { (0.0, 0.0), (20.0, 0.0), (20.0, 40.0), (0.0, 40.0) };
        Assert.Equal(3, GeradorEstribo.Canto(ret, PosicaoGanchos.SuperiorEsquerdo));
        Assert.Equal(2, GeradorEstribo.Canto(ret, PosicaoGanchos.SuperiorDireito));
        Assert.Equal(0, GeradorEstribo.Canto(ret, PosicaoGanchos.InferiorEsquerdo));
        Assert.Equal(1, GeradorEstribo.Canto(ret, PosicaoGanchos.InferiorDireito));
        Assert.Equal(0, GeradorEstribo.Canto(ret, PosicaoGanchos.ProximoVertice, 3));
        Assert.Equal(PosicaoGanchos.SuperiorDireito, GeradorEstribo.PosicaoDoCanto(ret, 2));
    }

    [Fact]
    public void VoltaDoEstriboRecuperaAPoligonal()
    {
        var ret = new[] { (0.0, 0.0), (0.0, 40.0), (20.0, 40.0), (20.0, 0.0) };
        var f = GeradorEstribo.FechadoPoligono(ret, GeradorEstribo.CantoPadrao(ret), FormatoSegmento.Gancho90, 8, 2.5);
        var volta = GeradorEstribo.PoligonoDoEstribo(f.Vertices.Select(v => (v.X, v.Y)).ToList());
        Assert.NotNull(volta);
        Assert.Equal(4, volta.Count);
        Assert.Equal((0.0, 40.0), volta[0]); // começa no canto dos ganchos
        Assert.Equal(2, f.Cotados.Count);   // retângulo: uma largura e uma altura
        Assert.Null(GeradorEstribo.PoligonoDoEstribo(new[] { (0.0, 0.0), (1.0, 0.0), (2.0, 0.0), (3.0, 0.0), (4.0, 0.0), (5.0, 0.0), (6.0, 1.0), (7.0, 0.0) }));
    }

    [Fact]
    public void GanchosDoFechadoNaoSeSobrepoem()
    {
        foreach (var g in new[] { FormatoSegmento.Gancho45, FormatoSegmento.Gancho90, FormatoSegmento.Gancho180 })
        {
            var o = Op(FormatoEstribo.Simples);
            o.Gancho = g;
            var f = GeradorEstribo.Gerar(o, 20, 40, 0, 6)[0];
            var v = f.Vertices;
            // pontas dos ganchos distintas e o 1º trecho de cada gancho em pontos diferentes
            Assert.True(Math.Abs(v[0].X - v[8].X) + Math.Abs(v[0].Y - v[8].Y) > 1);
            Assert.True(Math.Abs(v[1].X - v[7].X) + Math.Abs(v[1].Y - v[7].Y) > 1);
        }
    }

    /// <summary>Estribo ø5 CA-60 15×45, ganchos 90°: a passagem do canto é aço e tem 7 dobras.</summary>
    [Fact]
    public void ComprimentoRealDoEstriboContaAsPassagens()
    {
        var cfg = new ConfiguracaoArmadura { CasasDecimais = 2 };
        double lg = cfg.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho90, 5, "CA-60", true); // 7 + 1,25 = 8,25
        var medidas = new[] { lg, 2.5, 45, 15, 45, 15, 2.5, lg };
        var d = new DadosArmadura { Bitola = 5, Aco = "CA-60", Estribo = true };
        var geo = medidas.Select((c, i) => new GeoSegmento(TipoSegmento.Reta, c, i == 0 ? 0 : 90)).ToList();
        for (int i = 0; i < medidas.Length; i++)
            d.Segmentos.Add(new Segmento { Formato = i == 0 || i == 7 ? FormatoSegmento.Gancho90 : FormatoSegmento.Linha, Sobreposto = i == 1 || i == 6 });
        // pino 3φ = 1,5: re2 = 1,25, re = 1,0 → cada dobra de 90° desconta 2,5 − π/2
        double esperado = medidas.Sum() - 7 * (2.5 - Math.PI / 2);
        Assert.Equal(Math.Round(esperado, 2), Armadura.Core.Calculo.CalculoComprimento.ComprimentoUnitario(geo, cfg, d), 6);
    }

    [Fact]
    public void AbertoDoisRamosEhUmU()
    {
        var f = Assert.Single(GeradorEstribo.Gerar(Op(FormatoEstribo.Aberto), 20, 40, 0, 7));
        Assert.Equal(new[] { 7.0, 40, 20, 40, 7 }, f.Comprimentos().Select(c => Math.Round(c, 6)));
    }

    [Fact]
    public void AbertoTresRamosEhMeandro()
    {
        var f = Assert.Single(GeradorEstribo.Gerar(Op(FormatoEstribo.Aberto, 3), 20, 40, 0, 7));
        // gancho, perna, vão 10, perna, vão 10, perna, gancho
        Assert.Equal(new[] { 7.0, 40, 10, 40, 10, 40, 7 }, f.Comprimentos().Select(c => Math.Round(c, 6)));
    }

    [Fact]
    public void MultiploQuatroRamos()
    {
        var formas = GeradorEstribo.Gerar(Op(FormatoEstribo.Multiplo, 4), 30, 50, 0, 5);
        Assert.Equal(2, formas.Count);
        Assert.Single(formas, f => f.Armadura);
        Assert.Equal(2, formas.First(f => f.Armadura).Repeticoes);
        // ramos a cada 10: cada fechado tem 20 de largura; o 2º começa em x = 10
        Assert.Equal(20, formas[0].Comprimentos()[2], 6); // lado de cima
        Assert.Equal(10, formas[1].Vertices[2].X, 6);      // canto superior esquerdo (ganchos)
    }

    [Fact]
    public void PenteTresRamosTemGrampo()
    {
        var formas = GeradorEstribo.Gerar(Op(FormatoEstribo.Pente, 3), 30, 50, 0, 5);
        Assert.Equal(2, formas.Count);
        Assert.All(formas, f => Assert.True(f.Armadura));
        Assert.Equal("grampo", formas[1].Nome);
        Assert.Equal(15, formas[1].Vertices[1].X, 6);
        Assert.Equal(new[] { 5.0, 50, 5 }, formas[1].Comprimentos().Select(c => Math.Round(c, 6)));
    }

    [Fact]
    public void PenteQuatroRamosTemMeandroInterno()
    {
        var formas = GeradorEstribo.Gerar(Op(FormatoEstribo.Pente, 4), 30, 50, 0, 5);
        Assert.Equal("pente", formas[1].Nome);
        Assert.Equal(new[] { 5.0, 50, 10, 50, 5 }, formas[1].Comprimentos().Select(c => Math.Round(c, 6)));
    }

    [Fact]
    public void CircularDuasMeiasVoltas()
    {
        var f = Assert.Single(GeradorEstribo.Gerar(Op(FormatoEstribo.CircularExterno), 0, 0, 40, 6));
        var c = f.Comprimentos();
        Assert.Equal(Math.PI * 20, c[1], 6);
        Assert.Equal(Math.PI * 20, c[2], 6);
        Assert.Equal(FormatoSegmento.Curva, f.Formatos[1]);
        // externo: gancho para dentro (abaixo do ponto de cima); interno: para fora
        Assert.True(f.Vertices[0].Y < 0);
        Assert.True(GeradorEstribo.Gerar(Op(FormatoEstribo.CircularInterno), 0, 0, 40, 6)[0].Vertices[0].Y > 0);
    }

    [Fact]
    public void UltimoEstiboNaConfiguracao()
    {
        var cfg = new ConfiguracaoArmadura();
        cfg.UltimoEstribo.Formato = FormatoEstribo.Pente;
        cfg.UltimoEstribo.Ramos = 5;
        var volta = ConfiguracaoArmadura.DeJson(cfg.ParaJson());
        Assert.Equal(FormatoEstribo.Pente, volta.UltimoEstribo.Formato);
        Assert.Equal(5, volta.UltimoEstribo.Ramos);
        Assert.Equal(2.5, volta.UltimoEstribo.Cobrimento);
    }
}
