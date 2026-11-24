using System;
using System.Linq;
using DEB.Core.Analise.Mef;
using DEB.Core.Elementos.Escada;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Patamar como peça e escadas em L e U montadas por peças (D43).</summary>
public class EscadaConjuntoTests
{
    private static EscadaLaje Patamar(double vao = 150, double largura = 120, bool pm = false)
    {
        var e = new EscadaLaje { Peca = PecaEscada.Patamar, ComprimentoPatamar = vao, Largura = largura, Espessura = 15, Revestimento = 1, Sobrecarga = 2.5 };
        if (pm)
        {
            e.Execucao = ExecucaoEscada.PreMoldada;
            e.LigacaoInferior = new LigacaoEscada { Ponta = PontaPreMoldada.ApoioDireto, ComprimentoApoio = 15, Apoio = ApoioPreMoldado.Elastomero };
            e.LigacaoSuperior = new LigacaoEscada { Ponta = PontaPreMoldada.Dente, ComprimentoApoio = 15, AlturaDente = 10, ComprimentoDente = 25, Apoio = ApoioPreMoldado.Argamassa };
        }
        return e;
    }

    [Fact]
    public void PatamarPlanoConferidoAMao()
    {
        // g = 25·0,15 + 1 = 4,75 kN/m²; q = 2,5; ℓ = 1,5 m → Ra,g = 4,75·1,5/2; Md = 1,4·7,25·1,5²/8
        var r = CalculoEscada.Calcular(Patamar());
        Assert.Empty(r.Erros);
        Assert.Equal(4.75 * 1.5 / 2, r.RaG, 6);
        Assert.Equal(2.5 * 1.5 / 2, r.RbQ, 6);
        Assert.Equal(1.4 * 7.25 * 1.5 * 1.5 / 8, r.MdMax, 2);
        Assert.Single(r.Geo.Trechos);
        Assert.DoesNotContain(r.Verificacoes, v => v.Codigo == "E1");
        Assert.Contains(r.Barras, b => b.Nome == "N1");
        Assert.True(r.Principal.Esp > 0);
    }

    [Fact]
    public void CargaApoiadaEntraNaEstatica()
    {
        var e = Patamar();
        var sem = CalculoEscada.Calcular(e);
        e.CargasApoiadas.Add(new CargaApoiada { Nome = "lance", X = 70, Comprimento = 10, G = 10, Q = 4 });
        var com = CalculoEscada.Calcular(e);
        Assert.Equal(sem.RaG + 5, com.RaG, 6);
        Assert.Equal(sem.RbQ + 2, com.RbQ, 6);
        // no meio: P·ℓ/4 − P·c/8 com a carga espalhada em c = 10 cm
        double dM = 1.4 * (10 + 4) * (150.0 / 4 - 10.0 / 8) / 100;
        Assert.Equal(sem.MdMax + dM, com.MdMax, 2);
        Assert.Contains(com.Memoria.SelectMany(m => m.Linhas), l => l.Contains("Apoiada nesta peça"));
    }

    [Fact]
    public void PatamarPreModadoTemLigacoesEBarrasDentroDaPeca()
    {
        var r = CalculoEscada.Calcular(Patamar(pm: true));
        Assert.Empty(r.Erros);
        Assert.Equal(2, r.Ligacoes.Count);
        Assert.NotNull(r.Icamento);
        Assert.Equal(-10, r.Geo.ZAssentoSup, 6);
        Assert.True(EscadaTests.Area(r.Geo.Contorno) > 0);
        Assert.Contains(r.Barras, b => b.Funcao.StartsWith("tirante do dente superior"));
        foreach (var b in r.Barras.Where(b => !b.Transversal))
            for (int i = 1; i < b.Pontos.Count; i++)
                for (int t = 0; t <= 10; t++)
                {
                    var a = b.Pontos[i - 1]; var c = b.Pontos[i];
                    double x = a.X + (c.X - a.X) * t / 10, z = a.Z + (c.Z - a.Z) * t / 10;
                    Assert.True(EscadaTests.Dentro(r.Geo.Contorno, x, z), $"{b.Nome} ({b.Funcao}) fora da peça em ({x:0.0}; {z:0.0})");
                }
    }

    /// <summary>Escada em U: dois lances de 120 cm chegam pelo lado do patamar (255 × 150); o patamar apoia numa viga externa e na viga do patamar.</summary>
    private static ConjuntoEscada EmU()
    {
        var c = new ConjuntoEscada();
        var l1 = EscadaTests.ComPatamares(); l1.Largura = 120; l1.Desnivel = 144;
        var l2 = l1.Copia();
        c.Pecas.Add(("L1", l1));
        c.Pecas.Add(("L2", l2));
        c.Pecas.Add(("PAT", Patamar(150, 255)));
        c.Vigas.Add(new VigaPatamar { Nome = "VE", B = 20, H = 50, Comprimento = 320, ApoioE = 10, ApoioD = 10, Fck = 40 });
        c.Apoios.Add(new ApoioEntrePecas { Apoiada = "L1", Ponta = PontaPeca.Superior, Suporte = "PAT", X = 10 });
        c.Apoios.Add(new ApoioEntrePecas { Apoiada = "L2", Ponta = PontaPeca.Inferior, Suporte = "PAT", X = 10 });
        c.Apoios.Add(new ApoioEntrePecas { Apoiada = "PAT", Ponta = PontaPeca.Superior, Suporte = "VE", X = 160, Direcao = DirecaoApoio.AoLongo });
        return c;
    }

    private static double CargaPropria(ResultadoEscada r, Func<ResultadoEscada, System.Collections.Generic.List<(double X0, double X1, double W)>> seg, Func<CargaApoiada, double> ap) =>
        (seg(r).Sum(s => s.W * (s.X1 - s.X0)) - r.Entrada.CargasApoiadas.Sum(ap)) * r.Entrada.Largura / 100;

    [Fact]
    public void EscadaEmUFechaOEquilibrio()
    {
        var res = CalculoConjuntoEscada.Calcular(EmU());
        Assert.Empty(res.Erros);
        // o patamar é calculado depois dos lances; a viga recebe o patamar
        Assert.True(res.Ordem.IndexOf("PAT") > res.Ordem.IndexOf("L1") && res.Ordem.IndexOf("PAT") > res.Ordem.IndexOf("L2"));
        var pat = res.Pecas["PAT"];
        Assert.Equal(2, pat.Entrada.CargasApoiadas.Count);
        // equilíbrio: tudo o que as peças pesam sai pelas pontas externas ou vai para a viga
        double gProp = res.Pecas.Values.Sum(r => CargaPropria(r, x => x.SegmentosG, a => a.G));
        double qProp = res.Pecas.Values.Sum(r => CargaPropria(r, x => x.SegmentosQ, a => a.Q));
        var cv = res.CargasVigas["VE"];
        Assert.True(cv.G < 1e-9, "255 cm < 0,9·300: vai em concentradas");
        double gViga = cv.Concentradas.Sum(p => p.G), qViga = cv.Concentradas.Sum(p => p.Q);
        Assert.Equal(gProp, res.ReacoesExternas.Sum(x => x.G) + gViga, 6);
        Assert.Equal(qProp, res.ReacoesExternas.Sum(x => x.Q) + qViga, 6);
        // a viga do patamar foi calculada com essas cargas (reações = peso próprio + concentradas)
        var v = res.Vigas["VE"];
        Assert.True(v.MdMax > 0);
        Assert.Equal(v.G1 * 3.2 + gViga, v.RkE_G + v.RkD_G, 3);
        Assert.Equal(3, res.Transferencias.Count);
    }

    private static EscadaLaje PatamarCarregado()
    {
        var e = Patamar(150, 255);
        e.CargasApoiadas.Add(new CargaApoiada { Nome = "L1", X = 5, Comprimento = 15, G = 6, Q = 2 });
        e.CargasApoiadas.Add(new CargaApoiada { Nome = "L2", X = 60, Comprimento = 30, G = 4, Q = 1.5 });
        return e;
    }

    [Fact]
    public void MefDoPatamarRecebeAsCargasApoiadas()
    {
        var r = CalculoEscada.Calcular(PatamarCarregado());
        var p = MefEscada.Portico(r);
        Assert.Equal(1.4 * (r.RaG + r.RbG + r.RaQ + r.RbQ), -p.Casos[MefEscada.CasoElu].Sum(c => c.Fz), 6);
        foreach (bool degraus in new[] { false, true })
        {
            var ch = MefEscada.Chapa(r, degraus);
            for (int i = 0; i < ch.Elementos.Count; i++) Assert.True(ch.AreaElemento(i) > 1e-6, $"elemento {i + 1} degenerado");
            Assert.Equal(1.4 * (r.RaG + r.RbG + r.RaQ + r.RbQ), -ch.Casos[MefEscada.CasoElu].Sum(c => c.Fz), 6);
        }
        var f = FerramentasMef.Padrao;
        if (!f.TemOpenSees && !f.TemCalculix) return;
        var c = MefEscada.Comparar(r, f, openSees: f.TemOpenSees, calculix: f.TemCalculix);
        if (f.TemOpenSees)
        {
            Assert.True(c.TemOpenSees, c.ErroOpenSees);
            Assert.True(c.DifMdOpenSees < 1.0, $"OpenSees: Md difere {c.DifMdOpenSees:0.00} %");
            Assert.Equal(c.ReacoesCalculo.A, c.ReacoesOpenSees.Value.A, 2);
        }
        if (f.TemCalculix)
        {
            Assert.True(c.TemChapa, c.ErroChapa);
            Assert.True(c.DifMdChapa < 3.0, $"CalculiX: Md difere {c.DifMdChapa:0.00} %");
        }
    }

    [Fact]
    public void PatamarDeUmLanceApoiadoNoPatamarDoOutro()
    {
        // em linha: a ponta inferior (pré-moldada, com dente) de L2 assenta no patamar superior de L1
        var c = new ConjuntoEscada();
        var l1 = EscadaTests.PreMoldada();
        var l2 = EscadaTests.PreMoldada();
        l2.LigacaoInferior = new LigacaoEscada { Ponta = PontaPreMoldada.Dente, ComprimentoApoio = 15, AlturaDente = 9, ComprimentoDente = 25, Apoio = ApoioPreMoldado.Argamassa };
        c.Pecas.Add(("L1", l1)); c.Pecas.Add(("L2", l2));
        var sozinho = CalculoEscada.Calcular(l1);
        double x = sozinho.Geo.Vao - 20;
        c.Apoios.Add(new ApoioEntrePecas { Apoiada = "L2", Ponta = PontaPeca.Inferior, Suporte = "L1", X = x });
        var res = CalculoConjuntoEscada.Calcular(c);
        Assert.Empty(res.Erros);
        Assert.Equal(new[] { "L2", "L1" }, res.Ordem);
        var (g2, q2) = CalculoConjuntoEscada.ReacaoPonta(res.Pecas["L2"], PontaPeca.Inferior);
        var r1 = res.Pecas["L1"];
        Assert.Equal(sozinho.RbG + g2 * x / sozinho.Geo.Vao, r1.RbG, 6);
        Assert.Equal(sozinho.RaQ + q2 * (1 - x / sozinho.Geo.Vao), r1.RaQ, 6);
        Assert.True(r1.Ligacoes[1].Fd > sozinho.Ligacoes[1].Fd);
        // só três pontas saem do conjunto
        Assert.Equal(3, res.ReacoesExternas.Count);
    }

    [Fact]
    public void ApoioEmCicloEReferenciaErradaDaoErro()
    {
        var c = new ConjuntoEscada();
        c.Pecas.Add(("A", Patamar())); c.Pecas.Add(("B", Patamar()));
        c.Apoios.Add(new ApoioEntrePecas { Apoiada = "A", Ponta = PontaPeca.Superior, Suporte = "B", X = 10 });
        c.Apoios.Add(new ApoioEntrePecas { Apoiada = "B", Ponta = PontaPeca.Superior, Suporte = "A", X = 10 });
        Assert.Contains(CalculoConjuntoEscada.Calcular(c).Erros, e => e.Contains("ciclo"));
        c.Apoios.Add(new ApoioEntrePecas { Apoiada = "A", Ponta = PontaPeca.Inferior, Suporte = "X", X = 10 });
        Assert.Contains(CalculoConjuntoEscada.Calcular(c).Erros, e => e.Contains("\"X\""));
    }
}
