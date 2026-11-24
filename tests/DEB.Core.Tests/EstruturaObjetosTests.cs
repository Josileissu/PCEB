using System;
using System.Linq;
using DEB.Core.Analise.Barras;
using DEB.Core.Analise.Objetos;
using DEB.Core.Elementos.Consolo;
using DEB.Core.Intercambio;
using DEB.Core.Normas.Verificacao;
using DEB.Core.Pecas;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Análise por objetos (D46): pilares, viga e ligações por consolo geram as suas partes e recebem os esforços.</summary>
public class EstruturaObjetosTests
{
    private static void Perto(double esperado, double obtido, double tolRel = 1e-6, double tolAbs = 1e-6) =>
        Assert.True(Math.Abs(obtido - esperado) <= Math.Max(tolAbs, tolRel * Math.Abs(esperado)), $"esperado {esperado}, obtido {obtido}");

    /// <summary>Dois pilares 40×40 de 700 cm (100 embutidos), vão de 800 cm entre eixos, consolo com topo a 550 cm; viga 20×50
    /// apoiada a 20 + 15 cm do eixo de cada pilar (face + meio consolo), sobre elastômero 15×15×1 cm.</summary>
    private static EstruturaObjetos Portico(ApoioPeca aparelho, double g = 10, double q = 5)
    {
        var mat = MaterialBarra.Concreto(40);
        var e = new EstruturaObjetos();
        PilarObjeto P(string nome, double x) => new PilarObjeto
        {
            Dono = new Dono { Tipo = TipoDono.Pilar, Id = nome, Nome = nome }, Base = new Vetor3(x, 0, -100), Secao = SecaoPoligonal.Retangulo(40, 40),
            Comprimento = 700, Embutimento = 100, Material = mat,
        };
        var p1 = P("P1", 0); var p2 = P("P2", 800);
        e.Pilares.Add(p1); e.Pilares.Add(p2);
        var v = new VigaObjeto
        {
            Dono = new Dono { Tipo = TipoDono.Viga, Id = "V1", Nome = "V1" }, ApoioI = new Vetor3(35, 0, 452 + 1), ApoioJ = new Vetor3(765, 0, 452 + 1),
            Secao = SecaoPoligonal.Retangulo(20, 50), Material = mat, G = g, Q = q,
        };
        e.Vigas.Add(v);
        e.Ligacoes.Add(new LigacaoConsolo { Dono = new Dono { Tipo = TipoDono.Ligacao, Nome = "P1·V1" }, Pilar = p1, Viga = v, PontaI = true, ZTopoConsolo = 552, Aparelho = aparelho });
        e.Ligacoes.Add(new LigacaoConsolo { Dono = new Dono { Tipo = TipoDono.Ligacao, Nome = "P2·V1" }, Pilar = p2, Viga = v, PontaI = false, ZTopoConsolo = 552, Aparelho = aparelho });
        return e;
    }

    private static readonly ApoioPeca Elastomero = new ApoioPeca { Tipo = ApoioPeca.Neoprene, Comprimento = 15, Largura = 15, Espessura = 1 };

    [Fact]
    public void ReacaoDaVigaChegaAosConsolosEEquilibra()
    {
        var e = Portico(Elastomero);
        e.Analisar();
        var v = e.Vigas[0];
        double vao = 730;
        double ppViga = 25e-6 * 1000 * vao;            // 25 kN/m³ × 0,1 m² × 7,3 m
        foreach (var l in e.Ligacoes)
        {
            Perto(ppViga / 2, e.Esforco(l, e.Casos[EstruturaObjetos.CasoPP]).V, 1e-6);
            Perto(10 * 7.3 / 2, e.Esforco(l, e.Casos[EstruturaObjetos.CasoG]).V, 1e-6);
            Perto(5 * 7.3 / 2, e.Esforco(l, e.Casos[EstruturaObjetos.CasoQ]).V, 1e-6);
        }
        // equilíbrio vertical: reações dos dois pilares = pesos + cargas
        var r = e.Casos[EstruturaObjetos.CasoPP];
        double ppPilares = 2 * 25e-6 * 1600 * 600;
        double soma = e.Pilares.Sum(p => r.Reacoes[p.Nos[0].No][2]);
        Perto(ppViga + ppPilares, soma, 1e-9);
        // a viga gira no apoio e o eixo fica 25 cm acima: o aparelho empurra o consolo (H pequeno, mas existe)
        Assert.NotEqual(0, e.Esforco(e.Ligacoes[0], e.Casos[EstruturaObjetos.CasoG]).H);
        Assert.Empty(e.Avisos);
    }

    [Fact]
    public void PilarRecebeOMomentoDaExcentricidadeDoConsolo()
    {
        var e = Portico(null);   // sem aparelho: articulação fixa (aviso)
        e.Analisar();
        Assert.Contains(e.Avisos, a => a.Contains("articulação fixa"));
        var p1 = e.Pilares[0];
        var g = e.Casos[EstruturaObjetos.CasoG];
        var esf = e.Esforcos(p1, g);
        double v = 10 * 7.3 / 2;
        // logo abaixo do consolo: N = reação da viga; o momento no pilar soma V·35 (excentricidade) e o efeito do H da articulação
        var abaixo = esf.Where(x => Math.Abs(x.Z - 552) < 1e-6).OrderBy(x => x.N).Last();
        Perto(v, abaixo.N, 1e-6);
        var l = e.Ligacoes[0];
        var h = e.Esforco(l, g).H;
        Assert.True(Math.Abs(h) > 0.01, "articulação fixa trava a viga entre os pilares: há H");
        // equilíbrio de momentos do trecho do pilar acima do nível: zero (sem carga acima); no nó: ΔM = V·e − H·(altura do apoio − nível)
        var acima = esf.Where(x => Math.Abs(x.Z - 552) < 1e-6).OrderBy(x => x.N).First();
        Perto(0, acima.N, 1e-6, 1e-6);
    }

    [Fact]
    public void CombinacoesECargaDoConsoloParaOCalculo()
    {
        var e = Portico(Elastomero);
        e.Analisar();
        Assert.Equal(5, e.Combinacoes.Count);
        var elu1 = e.Combinacoes[e.DefinicaoCombinacoes[0].Nome];
        var l = e.Ligacoes[0];
        double ppV = 25e-6 * 1000 * 730 / 2, gV = 36.5, qV = 18.25;
        Perto(1.4 * (ppV + gV) + 1.4 * qV, e.Esforco(l, elu1).V, 1e-6);
        var carga = e.CargaDoConsolo(l);
        Perto(ppV + gV, carga.Vg, 1e-6);
        Perto(qV, carga.Vq, 1e-6);
        // o cálculo do consolo usa a carga da análise: Hd ≥ 0,2·Vd (o aparelho deu menos)
        var dados = new DadosConsolo { Nome = "teste", B = 40, A = 30, H = 40, H1 = 20, Cob = 2.5, TirN = 4, TirPhi = 12.5, CostN = 3, CostPhi = 8, VertN = 2, VertPhi = 8 };
        var r = CalculoConsolo.Calcular(dados, carga);
        Perto(0.2 * r.Vd, r.Hd, 1e-6);
        Assert.Contains(r.Memoria.Linhas, x => x.Contains("Hd da análise"));
    }

    [Fact]
    public void RigidezDoElastomero()
    {
        var k = LigacaoConsolo.Rigidez(Elastomero, 0.1, RepresentacaoLigacao.BarraRigidaEMola, out string obs);
        Perto(0.1 * 225 / 1, k[0]);          // Kh = G·a·b/t
        double s = 225.0 / (2 * 1 * 30);       // fator de forma 3,75
        Perto(5 * 0.1 * s * s * 225, k[2]);    // Kv = 5·G·S²·A/t
        Assert.Contains("Kh", obs);
        var art = LigacaoConsolo.Rigidez(new ApoioPeca { Tipo = ApoioPeca.Argamassa }, 0.1, RepresentacaoLigacao.BarraRigidaEMola, out obs);
        Assert.Equal(0, art[4]);
        Assert.Contains("argamassa", obs);
    }

    [Fact]
    public void DadosDaJanelaDoModeloDeAnalise()
    {
        var e = Portico(Elastomero);
        e.Analisar();
        string json = VisualizacaoAnalise.Json(e, "pórtico de teste");
        var d = DEB.Core.Persistencia.Json.Ler(json);
        Assert.Equal(e.Modelo.Nos.Count, d["nos"].Quantidade);
        Assert.Equal(e.Modelo.Barras.Count, d["barras"].Quantidade);
        Assert.Equal(5, d["objetos"].Quantidade);                       // 2 pilares, 1 viga, 2 ligações
        Assert.Equal(3 + 5, d["resultados"].Quantidade);                // casos + combinações
        // estações da combinação = soma das estações dos casos (superposição): momento no meio da viga
        int ib = e.Vigas[0].Barra.Id, meio = VisualizacaoAnalise.Estacoes / 2;
        double M(string nome) => d["resultados"].Itens.First(r => r["nome"].Texto == nome)["estacoes"][ib][meio][5].Numero;
        double elu = M(e.DefinicaoCombinacoes[0].Nome);
        Perto(1.4 * (M("PP") + M("G")) + 1.4 * M("Q"), elu, 1e-6);
        Assert.Contains("chrome.webview", VisualizacaoAnalise.Pagina());
        // prévia para olhar no navegador (PCEB_PREVIA = pasta)
        string pasta = Environment.GetEnvironmentVariable("PCEB_PREVIA");
        if (!string.IsNullOrEmpty(pasta))
            System.IO.File.WriteAllText(System.IO.Path.Combine(pasta, "previa_analise.html"),
                VisualizacaoAnalise.Pagina().Replace("<script>", "<script>window.DADOS_DEMO = " + json + ";</script><script>"));
    }
}
