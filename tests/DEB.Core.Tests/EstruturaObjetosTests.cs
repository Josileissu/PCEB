using System;
using System.Linq;
using DEB.Core.Analise.Barras;
using DEB.Core.Analise.Objetos;
using DEB.Core.Analise.Dimensionamento;
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
        var e = Portico(null);   // articulação fixa pedida (sem aparelho)
        foreach (var lg in e.Ligacoes) lg.Representacao = RepresentacaoLigacao.BarraRigidaArticulada;
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
    public void SemAparelhoUsaOPadraoDaConfiguracao()
    {
        var e = Portico(null);
        e.Analisar();
        Assert.Contains(e.Avisos, a => a.Contains("aparelho padrão"));
        // elastômero 10 × 16 × 1 cm, G = 1 MPa: Kh = 0,1·160/1 = 16 kN/cm
        Perto(16, e.Ligacoes[0].Mola.K[0], 1e-9);
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

    /// <summary>Placa quadrada 6 × 6 m, h = 12 cm, apoiada nas bordas, q = 10 kN/m² (Timoshenko: w = 0,00406·q·a⁴/D;
    /// mx = 0,0368·(1 + ν)·q·a²). A grelha (Hambly) não tem o acoplamento de Poisson: tolerância maior.</summary>
    [Fact]
    public void LajeEmGrelhaComoPlacaApoiada()
    {
        var mat = new MaterialBarra { E = 3000, G = 3000 / 2.4 };
        var e = new EstruturaObjetos();
        var l = new LajeObjeto { Material = mat, Espessura = 12, Q = 10, Malha = 30, ApoiarBordas = true, PesoEspecifico = 0 };
        l.Contorno.AddRange(new[] { new Vetor3(0, 0, 0), new Vetor3(600, 0, 0), new Vetor3(600, 600, 0), new Vetor3(0, 600, 0) });
        e.Lajes.Add(l);
        e.Analisar();
        var r = e.Casos[EstruturaObjetos.CasoQ];
        double soma = r.Reacoes.Where(x => x != null).Sum(x => x[2]);
        Perto(360, soma, 1e-9);
        var (uz, mx, my, _) = l.Resultados(r);
        int c = l.Nx / 2;
        double d = 3000 * Math.Pow(12, 3) / (12 * (1 - 0.04));
        double wPlaca = 0.00406 * 0.001 * Math.Pow(600, 4) / d;
        Assert.InRange(-uz[c, c] / wPlaca, 0.9, 1.15);
        Assert.InRange(mx[c, c], 0.0368 * 10 * 36 * 0.9, 0.0368 * 1.2 * 10 * 36 * 1.05);
        Perto(mx[c, c], my[c, c], 1e-6);                       // simetria
    }

    [Fact]
    public void LajeSobreVigasEPilaresMonoliticos()
    {
        // pórtico moldado no local: 4 pilares 30×30 de 300 cm, 4 vigas 20×50 nas bordas, laje 12 cm em cima
        var mat = MaterialBarra.Concreto(30);
        var e = new EstruturaObjetos();
        var pts = new[] { (0.0, 0.0), (500.0, 0.0), (500.0, 400.0), (0.0, 400.0) };
        var pil = pts.Select((p, k) => new PilarObjeto
        {
            Dono = new Dono { Tipo = TipoDono.Pilar, Id = "P" + (k + 1), Nome = "P" + (k + 1) }, Base = new Vetor3(p.Item1, p.Item2, 0),
            Secao = SecaoPoligonal.Retangulo(30, 30), Comprimento = 300, Embutimento = 0, Material = mat,
        }).ToList();
        e.Pilares.AddRange(pil);
        for (int k = 0; k < 4; k++)
        {
            var a = pts[k]; var b = pts[(k + 1) % 4];
            // eixo da viga 25 cm abaixo do topo do pilar: face de baixo a 250
            var v = new VigaObjeto { Dono = new Dono { Tipo = TipoDono.Viga, Id = "V" + (k + 1), Nome = "V" + (k + 1) },
                ApoioI = new Vetor3(a.Item1, a.Item2, 250), ApoioJ = new Vetor3(b.Item1, b.Item2, 250), Secao = SecaoPoligonal.Retangulo(20, 50), Material = mat };
            e.Vigas.Add(v);
            e.LigacoesDiretas.Add(new LigacaoPilarViga { Pilar = pil[k], Viga = v, PontaI = true });
            e.LigacoesDiretas.Add(new LigacaoPilarViga { Pilar = pil[(k + 1) % 4], Viga = v, PontaI = false });
        }
        var laje = new LajeObjeto { Dono = new Dono { Tipo = TipoDono.Laje, Id = "L1", Nome = "L1" }, Material = mat, Espessura = 12, G = 1, Q = 3, Malha = 50 };
        laje.Contorno.AddRange(pts.Select(p => new Vetor3(p.Item1, p.Item2, 294)));
        e.Lajes.Add(laje);
        e.Analisar();
        // toda a carga da laje chega às bases dos pilares
        var q = e.Casos[EstruturaObjetos.CasoQ];
        double soma = pil.Sum(p => q.Reacoes[p.Nos[0].No][2]);
        Perto(3 * 5 * 4, soma, 1e-6);
        Assert.DoesNotContain(e.Avisos, a => a.Contains("borda livre"));
        // as vigas foram divididas onde a laje apoia
        Assert.True(e.Vigas[0].Barras.Count > 5);
        var (uz, mx, _, _) = laje.Resultados(q);
        Assert.True(mx[laje.Nx / 2, laje.Ny / 2] > 0);
        string json = VisualizacaoAnalise.Json(e, "pórtico com laje");
        Assert.Contains("\"lajes\"", json);
        Assert.Contains("\"dimensionamento\"", json);
        string pasta = Environment.GetEnvironmentVariable("PCEB_PREVIA");
        if (!string.IsNullOrEmpty(pasta))
            System.IO.File.WriteAllText(System.IO.Path.Combine(pasta, "previa_laje.html"),
                VisualizacaoAnalise.Pagina().Replace("<script>", "<script>window.DADOS_DEMO = " + json + ";</script><script>"));
    }

    [Fact]
    public void DimensionamentoPelaAnalise()
    {
        var e = Portico(Elastomero);
        e.Analisar();
        var res = DimensionamentoObjetos.Verificar(e);
        Assert.Equal(5, res.Count);
        // viga: Md+ ≈ 1,4·(pp + g + q)·L²/8 (biapoiada nos aparelhos; a mola horizontal muda pouco)
        var viga = res.First(x => x.Dono.Tipo == TipoDono.Viga);
        double q = 0.25 * 10 * 0 + 25e-6 * 1000 * 100 + 10 + 5;   // kN/m: pp 2,5 + 15
        double md = 1.4 * q * 7.3 * 7.3 / 8;
        var a1 = viga.Verificacoes.First(v => v.Codigo == "A1");
        double mdObtido = double.Parse(a1.Valor.Split('=')[1].Split('k')[0].Trim().Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(Math.Abs(mdObtido - md) < 0.02 * md, $"Md+ {mdObtido} × {md}; " + string.Join(" | ", viga.Memoria.Linhas));
        Assert.Contains(viga.Verificacoes, v => v.Codigo == "A3" && v.Situacao == DEB.Core.Normas.Verificacao.SituacaoVerificacao.Ok);
        // pilar em balanço no cálice: le = 2·600 = 1200; λ = 1200·√12/40 = 103,9 → conferir 2ª ordem
        var pil = res.First(x => x.Dono.Tipo == TipoDono.Pilar);
        Assert.Contains(pil.Memoria.Linhas, l => l.Contains("λ = 103,9"));
        Assert.Equal(DEB.Core.Normas.Verificacao.SituacaoVerificacao.Conferir, pil.Situacao);
        // consolo sem armadura informada: conferir; com armadura: o cálculo do núcleo
        var lig = res.First(x => x.Dono.Tipo == TipoDono.Ligacao);
        Assert.Contains(lig.Verificacoes, v => v.Codigo == "CC0");
        e.Ligacoes[0].Consolo = new DadosConsolo { Nome = "c", B = 40, A = 30, H = 40, H1 = 20, Cob = 2.5, TirN = 4, TirPhi = 12.5, CostN = 3, CostPhi = 8, VertN = 2, VertPhi = 8 };
        var lig2 = DimensionamentoObjetos.Consolo(e, e.Ligacoes[0], new DEB.Core.Configuracao.Projeto.ConfiguracaoProjeto());
        Assert.Contains(lig2.Verificacoes, v => v.Codigo == "CC3");
    }

    [Fact]
    public void VigaApoiadaEmViga()
    {
        // viga principal 600 cm biapoiada (apoios fixos nas pontas, sem pilar); secundária de 400 cm apoiada no meio dela e num pilar
        var mat = new MaterialBarra { E = 3000, G = 1250 };
        var e = new EstruturaObjetos();
        var principal = new VigaObjeto { Dono = new Dono { Tipo = TipoDono.Viga, Id = "VP", Nome = "VP" }, ApoioI = new Vetor3(0, 0, 0), ApoioJ = new Vetor3(600, 0, 0),
            Secao = SecaoPoligonal.Retangulo(20, 60), Material = mat };
        var sec = new VigaObjeto { Dono = new Dono { Tipo = TipoDono.Viga, Id = "VS", Nome = "VS" }, ApoioI = new Vetor3(300, 0, 10), ApoioJ = new Vetor3(300, 400, 10),
            Secao = SecaoPoligonal.Retangulo(15, 40), Material = mat, Q = 10 };
        e.Vigas.Add(principal); e.Vigas.Add(sec);
        e.LigacoesVigaViga.Add(new LigacaoVigaViga { Apoiada = sec, PontaI = true, Suporte = principal, T = 300 });
        e.Analisar();
        // a secundária leva metade de 10 kN/m × 4 m à principal: 20 kN no meio → reações de 10 kN em cada ponta da principal
        var q = e.Casos[EstruturaObjetos.CasoQ];
        Perto(10, q.Reacoes[principal.NoEixoI][2], 1e-6);
        Perto(10, q.Reacoes[principal.NoEixoJ][2], 1e-6);
        Assert.Equal(2, principal.Barras.Count);
        Assert.DoesNotContain(e.Avisos, a => a.Contains("VS: ponta I"));
    }

    [Fact]
    public void PilarComFlexoCompressaoOblicua()
    {
        var e = Portico(Elastomero, g: 40, q: 20);
        e.Analisar();
        var cfg = new DEB.Core.Configuracao.Projeto.ConfiguracaoProjeto();
        // sem armadura: proposta que passa
        var r = DimensionamentoObjetos.Pilar(e, e.Pilares[0], cfg);
        var p3 = r.Verificacoes.First(v => v.Codigo == "P3");
        Assert.Equal(DEB.Core.Normas.Verificacao.SituacaoVerificacao.Ok, p3.Situacao);
        Assert.Contains(r.Resumo, t => t.Contains("(proposta)"));
        // com armadura fraca (4ø10): não atende ou abaixo do mínimo
        foreach (var b in DimensionamentoObjetos.BarrasNoContorno(e.Pilares[0].Secao, 4, 10, 4)) e.Pilares[0].Armadura.Add(b);
        var r2 = DimensionamentoObjetos.Pilar(e, e.Pilares[0], cfg);
        Assert.Contains(r2.Resumo, t => t.Contains("4 ø10 (detalhamento)"));
        Assert.Contains(r2.Memoria.Linhas, l => l.Contains("2ª ordem local"));
    }

    [Fact]
    public void BarrasNoContornoSimetricas()
    {
        var b = DimensionamentoObjetos.BarrasNoContorno(SecaoPoligonal.Retangulo(30, 60), 10, 16, 4);
        Assert.Equal(10, b.Count);
        Assert.Equal(15, b.Average(x => x.Y), 6);
        Assert.Equal(30, b.Average(x => x.Z), 6);
    }
}
