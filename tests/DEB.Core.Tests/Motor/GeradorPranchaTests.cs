using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DEB.Core.Modelo;
using DEB.Core.Motor;
using DEB.Core.Motor.Prancha;
using DEB.Core.Templates;
using Xunit;

namespace DEB.Core.Tests.Motor;

/// <summary>
/// Gerador de prancha (frente C) com um modelo feito à mão (sem depender do motor do bloco): bloco 2 estacas 190 × 70 × 55,
/// estacas ø40, principal U em x distribuída em y, estribo vertical fechado distribuído em x e malha VAR em y.
/// Usa os detalhes padrão do pilar → coleção "Pilar".
/// </summary>
[Collection("Pilar")]
public class GeradorPranchaTests
{
    private static List<(double X, double Y)> Ret(double x0, double y0, double x1, double y1) =>
        new List<(double X, double Y)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };

    internal static ElementoBloco Elemento(int pilares = 6, LigacaoPilar lig = LigacaoPilar.Arranque)
    {
        var e = new ElementoBloco { Nome = "B28", Ligacao = lig, DiamEstaca = 40, Fck = 30 };
        foreach (var p in new[] { "P106", "P118", "P119", "P120", "P121", "P122" }.Take(pilares)) e.Pilares.Add(p);
        e.Niveis.Add(new NivelPilar { Pilar = "P106", CotaM = -11.43 });
        e.Niveis.Add(new NivelPilar { Pilar = "P118", CotaM = -11.43 });
        return e;
    }

    internal static ModeloArmadura Modelo()
    {
        var m = new ModeloArmadura { TipoElemento = ElementoBloco.TipoId, Nome = "B28" };
        m.Corpos.Add(new CorpoConcreto { Papel = "bloco", Planta = Ret(0, 0, 190, 70), Z0 = 0, Z1 = 55 });
        m.Corpos.Add(new CorpoConcreto { Papel = "lastro", Planta = Ret(0, 0, 190, 70), Z0 = -5, Z1 = 0 });
        m.Corpos.Add(new CorpoConcreto { Papel = "estaca", Centro = (35, 35), Raio = 20, Z0 = -800, Z1 = 5 });
        m.Corpos.Add(new CorpoConcreto { Papel = "estaca", Centro = (155, 35), Raio = 20, Z0 = -800, Z1 = 5 });
        m.Corpos.Add(new CorpoConcreto { Papel = "pilar", Planta = Ret(80, 25, 110, 45), Z0 = 55, Z1 = 400 });
        m.Niveis["fundo"] = 0; m.Niveis["topo"] = 55; m.Niveis["arrasamento"] = 5;

        m.Posicoes.Add(new PosicaoArmadura
        {
            Chave = "principal_x", Funcao = Funcoes.Principal, Nome = "principal x", Forma = FormaBarra.U, Bitola = 16,
            Vertices = { new P3(4, 25, 46), new P3(4, 25, 6), new P3(186, 25, 6), new P3(186, 25, 46) },
            Distribuicao = new Distribuicao { Inicio = new P3(95, 25, 6), Fim = new P3(95, 45, 6), Quantidade = 5, Espacamento = 5 },
        });
        m.Posicoes.Add(new PosicaoArmadura
        {
            Chave = "estribo_vertical", Funcao = Funcoes.EstriboVertical, Nome = "estribo vertical", Forma = FormaBarra.Estribo, Bitola = 8,
            Fechada = true,
            Vertices = { new P3(10, 4, 4), new P3(10, 66, 4), new P3(10, 66, 51), new P3(10, 4, 51) },
            Distribuicao = new Distribuicao { Inicio = new P3(10, 35, 4), Fim = new P3(178, 35, 4), Quantidade = 15, Espacamento = 12 },
        });
        var malha = new PosicaoArmadura
        {
            Chave = "secundaria_y", Funcao = Funcoes.Secundaria, Nome = "malha VAR", Forma = FormaBarra.Reta, Bitola = 10,
            Vertices = { new P3(20, 4, 45), new P3(20, 66, 45) },
            Distribuicao = new Distribuicao { Inicio = new P3(20, 4, 45), Fim = new P3(180, 4, 45), Quantidade = 5, Espacamento = 40 },
            SegmentosVar = { 0 },
        };
        for (int j = 0; j < 5; j++)
        {
            double x = 20 + 40 * j, l = 62 - 5 * j;
            malha.VerticesVar.Add(new List<P3> { new P3(x, 4, 45), new P3(x, 4 + l, 45) });
            malha.ComprimentosVar.Add(l);
        }
        m.Posicoes.Add(malha);
        return m;
    }

    internal static PerfilDetalhamento Perfil(string idioma = "PT")
    {
        var p = new PerfilDetalhamento { Id = "teste", TipoElemento = ElementoBloco.TipoId };
        p.Padroes["cobrimento.solo"] = 4; p.Padroes["cobrimento.calice"] = 2;
        p.Folha.Idioma = idioma;
        p.Folha.Escala = 20;
        p.Folha.Titulo = "BLOCO {nome}[ - {repeticoes?}x] - {estacas} ø {diametro}";
        p.Folha.Vistas.Add(new VistaPerfil { Id = "forma_planta", Titulo = "PLANTA|PLAN", Tipo = TipoVista.FormaPlanta, Quadro = 0, Linha = 0 });
        p.Folha.Vistas.Add(new VistaPerfil { Id = "forma_corte", Titulo = "CORTE A-A|SECTION A-A", Tipo = TipoVista.FormaCorte, Plano = "x", Quadro = 0, Linha = 1 });
        p.Folha.Vistas.Add(new VistaPerfil
        {
            Id = "planta", Titulo = "VISTA SUPERIOR - ARMADURA INFERIOR|TOP VIEW - BOTTOM REINFORCEMENT", Tipo = TipoVista.Planta,
            Posicoes = { "principal", "secundaria_*" }, BarrasAbaixo = true, Quadro = 1, Linha = 0, Ordem = 0,
        });
        p.Folha.Vistas.Add(new VistaPerfil { Id = "estribos", Titulo = "ESTRIBOS|STIRRUPS", Tipo = TipoVista.Barras, Posicoes = { "estribo_vertical" }, Quadro = 1, Linha = 0, Ordem = 1 });
        p.Folha.Vistas.Add(new VistaPerfil { Id = "corte_b", Titulo = "CORTE B-B|SECTION B-B", Tipo = TipoVista.Corte, Plano = "y", Quadro = 1, Linha = 1 });
        p.Folha.Detalhes.AddRange(new[] { "chaves", "raio_curvatura", "emendas", "comprimento_barras", "ancoragem_estacas", "barra_variavel" });
        p.Folha.Notas.Add("CONCRETO DO BLOCO: fck = {fck} MPa;");
        p.Folha.Notas.Add("COBRIMENTOS: ELEMENTOS EM CONTATO COM O SOLO {cobrimento.solo} cm{calice:; INTERNO DO CÁLICE {cobrimento.calice} cm};");
        return p;
    }

    private static (ResultadoTemplate R, GeradorPrancha G) Gerar(string idioma = "PT", int pilares = 6, LigacaoPilar lig = LigacaoPilar.Arranque)
    {
        var g = new GeradorPrancha();
        return (g.Gerar(Elemento(pilares, lig), Modelo(), Perfil(idioma)), g);
    }

    private static Retangulo Vista(GeradorPrancha g, string id) => g.Vistas.Single(v => v.Id == id).Limites;

    [Fact]
    public void UmaBarraPorPosicao_NomesUnicos_FaixasApontamParaBarras()
    {
        var (r, _) = Gerar();
        Assert.Equal(new[] { "principal_x", "estribo_vertical", "secundaria_y" }, r.Barras.Select(b => b.Nome).ToArray());
        Assert.Equal(r.Barras.Count, r.Barras.Select(b => b.Nome).Distinct().Count());
        Assert.NotEmpty(r.Faixas);
        var nomes = new HashSet<string>(r.Barras.Select(b => b.Nome));
        Assert.All(r.Faixas, f => Assert.Contains(f.Barra, nomes));
        // faixa de distribuição da principal (em y) e da malha (em x) na planta
        Assert.Contains(r.Faixas, f => f.Barra == "principal_x" && f.Tipo == TipoFaixa.Ortogonal && Math.Abs(Math.Abs(f.P2.Y - f.P1.Y) - 20) < 1e-6);
        Assert.Contains(r.Faixas, f => f.Barra == "secundaria_y" && f.Tipo == TipoFaixa.Ortogonal && Math.Abs(Math.Abs(f.P2.X - f.P1.X) - 160) < 1e-6);
        var est = r.Barras.Single(b => b.Nome == "estribo_vertical");
        Assert.True(est.Estribo);
        Assert.Contains(FormatoSegmento.Gancho90, est.Formatos);   // ganchos gerados no canto superior esquerdo
        Assert.Equal(15, est.Quantidade);
        var var = r.Barras.Single(b => b.Nome == "secundaria_y");
        Assert.Equal(5, var.ComprimentosVar.Count);
        Assert.Contains(0, var.SegmentosVar);
        var pr = r.Barras.Single(b => b.Nome == "principal_x");
        Assert.Equal(4, pr.Vertices.Count);
        Assert.Equal(182, pr.Vertices.Max(v => v.X) - pr.Vertices.Min(v => v.X), 6);
        Assert.Equal(40, pr.Vertices.Max(v => v.Y) - pr.Vertices.Min(v => v.Y), 6);
    }

    [Fact]
    public void TitulosDasVistas_PorIdioma()
    {
        var pt = Gerar("PT").R.Textos.Select(t => t.Texto).ToList();
        Assert.Contains("CORTE B-B", pt);
        Assert.DoesNotContain("SECTION B-B", pt);
        Assert.DoesNotContain("(SECTION B-B)", pt);
        var en = Gerar("EN").R.Textos.Select(t => t.Texto).ToList();
        Assert.Contains("SECTION B-B", en);
        Assert.DoesNotContain("CORTE B-B", en);
        Assert.Contains("PLAN", en);
        var ambos = Gerar("PT/EN").R.Textos.Select(t => t.Texto).ToList();
        Assert.Contains("CORTE B-B", ambos);
        Assert.Contains("(SECTION B-B)", ambos);
        Assert.Contains("ESC 1:20", pt);
    }

    [Fact]
    public void TituloDaFolha_ComCampos()
    {
        var (r, _) = Gerar();
        var txt = r.Textos.Select(t => t.Texto).ToList();
        Assert.Contains("BLOCO B28 - 6x - 2 ø 40", txt);
        Assert.Contains("P106, P118, P119, P120, P121 e P122", txt);
        Assert.Equal("BLOCO B28", r.Detalhamento);
        Assert.Equal(6, r.DadosDetalhamento.Repeticoes);
        Assert.Equal("Fundações", r.DadosDetalhamento.Grupo);
        Assert.Equal("C30", r.DadosDetalhamento.Classe);
        var um = Gerar(pilares: 1).R.Textos.Select(t => t.Texto).ToList();
        Assert.Contains("BLOCO B28 - 2 ø 40", um);
    }

    [Fact]
    public void Vistas_SemSobreposicao()
    {
        var (r, g) = Gerar();
        Assert.Equal(5, g.Vistas.Count);
        for (int i = 0; i < g.Vistas.Count; i++)
            for (int j = i + 1; j < g.Vistas.Count; j++)
                Assert.False(g.Vistas[i].Limites.Intersecta(g.Vistas[j].Limites), $"{g.Vistas[i].Id} {g.Vistas[i].Limites} × {g.Vistas[j].Id} {g.Vistas[j].Limites}");
        // forma (quadro 0) à esquerda da armadura (quadro 1)
        double xForma = g.Vistas.Where(v => v.Quadro == 0).Max(v => v.Limites.X1);
        Assert.True(g.Vistas.Where(v => v.Quadro == 1).All(v => v.Limites.X0 > xForma));
        // tudo da planta de armadura dentro do retângulo dela (a principal dobrada embaixo)
        var lp = Vista(g, "planta");
        var pr = r.Barras.Single(b => b.Nome == "principal_x");
        Assert.All(pr.Vertices, v => Assert.True(lp.Contem(v.X, v.Y)));
        var le = Vista(g, "estribos");
        Assert.All(r.Barras.Single(b => b.Nome == "estribo_vertical").Vertices, v => Assert.True(le.Contem(v.X, v.Y)));
    }

    [Fact]
    public void PlantaDeForma_Cota190_E_EstacasHachuradas()
    {
        var (r, g) = Gerar();
        var l = Vista(g, "forma_planta");
        var cotas = r.Cotas.Where(c => l.Contem(c.P1.X, c.P1.Y) && l.Contem(c.P2.X, c.P2.Y)).ToList();
        Assert.Contains(cotas, c => Math.Abs(Math.Abs(c.P2.X - c.P1.X) - 190) < 1e-6 && c.Angulo == 0);
        Assert.Contains(cotas, c => Math.Abs(Math.Abs(c.P2.Y - c.P1.Y) - 70) < 1e-6);
        Assert.Contains(cotas, c => Math.Abs(Math.Abs(c.P2.X - c.P1.X) - 120) < 1e-6);   // entre as estacas
        Assert.Equal(4, r.Hachuras.Count(h => h.Padrao == "SOLID" && l.Contem(h.Vertices[0].X, h.Vertices[0].Y)));
        Assert.Contains(r.Textos, t => t.Texto == "A" && t.Layer == "ARM-CORTE" && l.Contem(t.X, t.Y));
    }

    [Fact]
    public void CorteDaForma_Niveis()
    {
        var (r, g) = Gerar();
        var l = Vista(g, "forma_corte");
        var txt = r.Textos.Where(t => l.Contem(t.X, t.Y)).Select(t => t.Texto).ToList();
        Assert.Contains("(TOPO DO BLOCO)", txt);
        Assert.Contains("(COTA DE ARRASAMENTO)", txt);
        Assert.Contains("P106=-11,43 m", txt);
        Assert.Contains("P106=-11,93 m", txt);   // arrasamento: 50 cm abaixo do topo
        Assert.Contains("CONCRETO MAGRO e=5 cm", txt);
        Assert.Contains(r.Cotas, c => l.Contem(c.P1.X, c.P1.Y) && Math.Abs(Math.Abs(c.P2.Y - c.P1.Y) - 55) < 1e-6);
        // contorno do bloco cortado (190 × 55)
        Assert.Contains(r.Contornos, c => c.Fechado && c.Layer == "ARM-FORMA" && l.Contem(c.Vertices[0].X, c.Vertices[0].Y)
                                          && Math.Abs(c.Vertices.Max(v => v.X) - c.Vertices.Min(v => v.X) - 190) < 1e-6
                                          && Math.Abs(c.Vertices.Max(v => v.Y) - c.Vertices.Min(v => v.Y) - 55) < 1e-6);
    }

    [Fact]
    public void InformacoesDeProducao()
    {
        var (r, _) = Gerar();
        Assert.Equal(190.0 * 70 * 55 / 1e6, r.DadosDetalhamento.Volume, 3);
        Assert.Equal((2 * (190 + 70)) * 55 / 1e4, r.DadosDetalhamento.AreaForma, 2);
        var t = r.Tabelas.Single(x => x.Titulo == "INFORMAÇÕES P/ PRODUÇÃO");
        Assert.Contains(t.Linhas, l => l[0] == "QUANTIDADE:" && l[1] == "6 un.");
        Assert.Contains(t.Linhas, l => l[0] == "VOLUME DE CONCRETO (1 BLOCO):" && l[1].StartsWith("0,73"));
        // peso: 5 × (40+182+40) ø16 + 15 estribos ø8 (com ganchos) + (62+57+52+47+42) ø10
        var pesoTxt = t.Linhas.Single(l => l[0] == "PESO DE AÇO (1 BLOCO):")[1];
        double peso = double.Parse(pesoTxt.Replace(" kg", "").Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
        double minimo = 5 * 2.62 * 1.578 + 15 * 2.18 * 0.395 + 2.60 * 0.617;
        Assert.InRange(peso, minimo, minimo * 1.15);
        Assert.NotNull(r.PosicaoRA);
    }

    [Fact]
    public void Corte_MostraPrincipalCortada()
    {
        var (r, g) = Gerar();
        var l = Vista(g, "corte_b");
        var pontos = r.Circulos.Where(c => c.Layer == "ARM-BARRA-SECAO" && l.Contem(c.X, c.Y)).ToList();
        Assert.Equal(5, pontos.Count);
        Assert.Single(pontos.Select(p => Math.Round(p.Y, 3)).Distinct());          // todos na mesma camada
        Assert.Equal(20, pontos.Max(p => p.X) - pontos.Min(p => p.X), 6);         // 5 c/5
        Assert.Contains(r.Faixas, f => f.Barra == "principal_x" && f.Tipo == TipoFaixa.Indicacao && l.Contem(f.Posicao.X, f.Posicao.Y)
                                       && f.Referencias.Count == 3);
        // estribo e malha no plano do corte: verdadeira grandeza (a mais próxima) com indicação
        Assert.Contains(r.Faixas, f => f.Barra == "estribo_vertical" && f.Tipo == TipoFaixa.Indicacao && l.Contem(f.Posicao.X, f.Posicao.Y));
        Assert.Contains(r.Faixas, f => f.Barra == "secundaria_y" && f.Tipo == TipoFaixa.Indicacao && l.Contem(f.Posicao.X, f.Posicao.Y));
    }

    [Fact]
    public void BarraVariavel_TextosL_E_Quadro()
    {
        var (r, g) = Gerar();
        var l = Vista(g, "planta");
        var ls = r.Textos.Where(t => t.Layer == "ARM-TEXTO" && l.Contem(t.X, t.Y)).Select(t => t.Texto).ToList();
        foreach (var s in new[] { "62", "57", "52", "47", "42" }) Assert.Contains(s, ls);
        Assert.Contains(r.Textos, t => t.Texto == "INDICAÇÃO BARRA VARIÁVEL");
        Assert.Contains(r.Textos, t => t.Texto == "ANCORAGEM DAS ARMADURAS DAS ESTACAS");
        Assert.Contains(r.Textos, t => t.Texto.Contains("CHAVES"));
    }

    [Fact]
    public void Notas_ComCamposECondicao()
    {
        var arr = Gerar(lig: LigacaoPilar.Arranque).R.Textos.Select(t => t.Texto).ToList();
        Assert.Contains("NOTAS:", arr);
        Assert.Contains("1) CONCRETO DO BLOCO: fck = 30 MPa;", arr);
        Assert.Contains("2) COBRIMENTOS: ELEMENTOS EM CONTATO COM O SOLO 4 cm;", arr);
        var cal = Gerar(lig: LigacaoPilar.CaliceColarinho).R.Textos.Select(t => t.Texto).ToList();
        Assert.Contains("2) COBRIMENTOS: ELEMENTOS EM CONTATO COM O SOLO 4 cm; INTERNO DO CÁLICE 2 cm;", cal);
    }

    [Fact]
    public void Formatador_CamposOpcionaisGruposECondicoes()
    {
        var faltaram = new List<string>();
        var f = new FormatadorCampos(new Dictionary<string, string> { ["nome"] = "B1", ["rep"] = "1", ["n"] = "3", ["sim"] = "1", ["nao"] = "0", ["c"] = "2,5" },
            x => faltaram.Add(x));
        Assert.Equal("B1", f.Formatar("{nome}[ - {rep?}x]"));
        Assert.Equal("B1 - 3x", f.Formatar("{NOME}[ - {n?}x]"));
        Assert.Equal("a; b 2,5 cm.", f.Formatar("a{sim:; b {c} cm}{nao:; nunca}."));
        Assert.Equal("x", f.Formatar("x[ {zz}]"));
        Assert.Contains("zz", faltaram);
        Assert.Equal("{aberto", f.Formatar("{aberto"));
        Assert.Equal("", f.Formatar(null));
    }

    [Fact]
    public void Robusto_SemVistas_SemGeometria_SemModelo()
    {
        var g = new GeradorPrancha();
        var p = Perfil(); p.Folha.Vistas.Clear();
        var m = Modelo();
        m.Posicoes.Add(new PosicaoArmadura { Chave = "vazia", Funcao = "lateral", Bitola = 8 });
        m.Posicoes.Add(new PosicaoArmadura { Chave = "principal_x", Funcao = "principal", Bitola = 10, Vertices = { new P3(0, 0, 0), new P3(10, 0, 0) } });
        var r = g.Gerar(Elemento(), m, p);
        Assert.Contains(r.Avisos, a => a.Contains("não tem vistas"));
        Assert.Contains(r.Avisos, a => a.Contains("\"vazia\" sem geometria"));
        Assert.Contains(r.Avisos, a => a.Contains("repetida"));
        Assert.Equal(4, r.Barras.Count);                          // na vista BARRAS automática
        Assert.Equal(r.Barras.Count, r.Barras.Select(b => b.Nome).Distinct().Count());
        Assert.Contains(r.Textos, t => t.Texto == "BARRAS");

        var r2 = g.Gerar(null, null, null);
        Assert.NotEmpty(r2.Avisos);
        var p3 = Perfil();
        p3.Folha.Vistas.Add(new VistaPerfil { Id = "nada", Titulo = "NADA", Tipo = TipoVista.Planta, Posicoes = { "inexistente" } });
        p3.Folha.Detalhes.Add("desconhecido");
        var r3 = g.Gerar(Elemento(), Modelo(), p3);
        Assert.Contains(r3.Avisos, a => a.Contains("\"nada\""));
        Assert.Contains(r3.Avisos, a => a.Contains("desconhecido"));
        var r4 = g.Gerar(Elemento(), new ModeloArmadura(), Perfil());
        Assert.Empty(r4.Barras);
        Assert.NotEmpty(r4.Avisos);
    }

    [Fact]
    public void BarrasDoModelo_UsadasNasVistas()
    {
        // malha interrompida: o motor manda a geometria de cada barra em PosicaoArmadura.Barras
        var m = Modelo();
        var pr = m.Posicao("principal_x");
        pr.Distribuicao.Repeticoes = 2;
        foreach (var y in new[] { 10.0, 15.0, 55.0, 60.0 })
            pr.Barras.Add(new List<P3> { new P3(4, y, 46), new P3(4, y, 6), new P3(186, y, 6), new P3(186, y, 46) });
        var g = new GeradorPrancha();
        var r = g.Gerar(Elemento(), m, Perfil());
        var l = Vista(g, "corte_b");
        Assert.Equal(4, r.Circulos.Count(c => c.Layer == "ARM-BARRA-SECAO" && l.Contem(c.X, c.Y)));
        Assert.Equal(2, r.Barras.Single(b => b.Nome == "principal_x").Repeticoes);
    }

    internal static ModeloArmadura ModeloCalice()
    {
        var m = Modelo();
        m.Corpos.RemoveAll(c => c.Papel == "pilar");
        m.Corpos.Add(new CorpoConcreto { Papel = "colarinho", Planta = Ret(55, 0, 135, 70), Z0 = 55, Z1 = 95 });
        m.Corpos.Add(new CorpoConcreto { Papel = "furo_calice", Planta = Ret(76, 21, 114, 49), Z0 = 40, Z1 = 95, Vazio = true });
        m.Corpos.Add(new CorpoConcreto { Papel = "regularizacao", Planta = Ret(76, 21, 114, 49), Z0 = 40, Z1 = 45 });
        m.Corpos.Add(new CorpoConcreto { Papel = "pilar", Planta = Ret(80, 25, 110, 45), Z0 = 45, Z1 = 400 });
        m.Niveis["topo"] = 95; m.Niveis["fundo_pilar"] = 45;
        return m;
    }

    [Fact]
    public void Calice_ContornoDoCorteComFuro_VolumeDescontaVazio()
    {
        var g = new GeradorPrancha();
        var r = g.Gerar(Elemento(lig: LigacaoPilar.CaliceColarinho), ModeloCalice(), Perfil());
        double vol = (190.0 * 70 * 55 + 80 * 70 * 40 - 38 * 28 * 55) / 1e6;
        Assert.Equal(vol, r.DadosDetalhamento.Volume, 3);
        var l = Vista(g, "forma_corte");
        // um laço só: base + colarinho com o furo aberto em cima (12 vértices, como o template)
        var laços = r.Contornos.Where(c => c.Fechado && c.Layer == "ARM-FORMA" && l.Contem(c.Vertices[0].X, c.Vertices[0].Y)
                                           && c.Vertices.Max(v => v.X) - c.Vertices.Min(v => v.X) > 150
                                           && c.Vertices.Max(v => v.Y) - c.Vertices.Min(v => v.Y) > 50).ToList();
        Assert.Single(laços);
        Assert.Equal(12, laços[0].Vertices.Count);
        Assert.Equal(95, laços[0].Vertices.Max(v => v.Y) - laços[0].Vertices.Min(v => v.Y), 6);
        var txt = r.Textos.Where(t => l.Contem(t.X, t.Y)).Select(t => t.Texto).ToList();
        Assert.Contains("REGULARIZAÇÃO DE FUNDO e=5 cm", txt);
        Assert.Contains("(FUNDO DO PILAR)", txt);
        Assert.Contains("P106=-11,93 m", txt);   // fundo do pilar 50 abaixo do topo
        Assert.Contains("P106=-12,33 m", txt);   // arrasamento 90 abaixo do topo
        Assert.Contains(r.Hachuras, h => h.Padrao == "AR-CONC" && l.Contem(h.Vertices[0].X, h.Vertices[0].Y));
        if (Environment.GetEnvironmentVariable("ARM_PREVIA") is { Length: > 0 } pasta) Gravar(pasta, "previa_prancha_motor_calice.json", r);
    }

    // ---------------- prévia JSON (ARM_PREVIA=<pasta>; para tools/previa_template.py) ----------------

    [Fact]
    public void GravarPreviaDaPrancha()
    {
        var pasta = Environment.GetEnvironmentVariable("ARM_PREVIA");
        if (string.IsNullOrEmpty(pasta)) return;
        Gravar(pasta, "previa_prancha_motor.json", Gerar("PT").R);
        Gravar(pasta, "previa_prancha_motor_pten.json", Gerar("PT/EN").R);
    }

    /// <summary>Mesmo formato do PreviaTemplateTests.Gravar (copiado; remover na integração).</summary>
    private static void Gravar(string pasta, string arquivo, ResultadoTemplate r)
    {
        string P((double X, double Y) p) => FormattableString.Invariant($"[{p.X:0.###},{p.Y:0.###}]");
        string Ps(IEnumerable<(double X, double Y)> ps) => "[" + string.Join(",", ps.Select(P)) + "]";
        string S(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ") + "\"";
        var sb = new StringBuilder("{");
        sb.Append("\"barras\":[" + string.Join(",", r.Barras.Select(b => Ps(b.Vertices))) + "],");
        sb.Append("\"contornos\":[" + string.Join(",", r.Contornos.Select(c => Ps(c.Fechado ? c.Vertices.Concat(c.Vertices.Take(1)) : c.Vertices))) + "],");
        sb.Append("\"layers\":[" + string.Join(",", r.Contornos.Select(c => S(c.Layer))) + "],");
        sb.Append("\"textos\":[" + string.Join(",", r.Textos.Select(x => FormattableString.Invariant(
            $"{{\"t\":{S(x.Texto)},\"x\":{x.X:0.###},\"y\":{x.Y:0.###},\"h\":{x.Altura:0.###},\"a\":{x.Angulo:0.####},\"al\":{x.Alinhamento}}}"))) + "],");
        sb.Append("\"circulos\":[" + string.Join(",", r.Circulos.Select(c => FormattableString.Invariant($"[{c.X:0.###},{c.Y:0.###},{c.Raio:0.###}]"))) + "],");
        sb.Append("\"cotas\":[" + string.Join(",", r.Cotas.Select(c => "[" + P(c.P1) + "," + P(c.P2) + "," + P(c.Posicao) + "]")) + "],");
        sb.Append("\"faixas\":[" + string.Join(",", r.Faixas.Select(f => "[" + P(f.P1) + "," + P(f.P2) + "," + P(f.Posicao) + "," + Ps(f.Referencias) + "]")) + "],");
        sb.Append("\"tabelas\":[" + string.Join(",", r.Tabelas.Select(x => FormattableString.Invariant($"[{x.X:0.###},{x.Y:0.###},{x.Larguras.Sum() * x.Escala / 10:0.###},{(x.Linhas.Count + 1) * 0.8 * x.Escala:0.###}]"))) + "],");
        sb.Append("\"ra\":" + (r.PosicaoRA is { } ra ? P(ra) : "null"));
        sb.Append("}");
        Directory.CreateDirectory(pasta);
        File.WriteAllText(Path.Combine(pasta, arquivo), sb.ToString(), new UTF8Encoding(false));
    }
}
