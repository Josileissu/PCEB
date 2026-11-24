using System;
using System.Linq;
using DEB.Core.Analise.Barras;
using DEB.Core.Analise.Dimensionamento;
using DEB.Core.Analise.Objetos;
using DEB.Core.Configuracao.Projeto;
using DEB.Core.Normas.Verificacao;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Lajes da análise por objetos: aberturas e pré-fabricadas unidirecionais (alveolar, treliçada, vigota; sem verificação).</summary>
public class LajesTiposTests
{
    private static LajeObjeto Quadrada(double lado = 600)
    {
        var l = new LajeObjeto { Material = new MaterialBarra { E = 3000, G = 1250 }, Espessura = 15, Q = 10, Malha = 50, ApoiarBordas = true, PesoEspecifico = 0 };
        l.Contorno.AddRange(new[] { new Vetor3(0, 0, 0), new Vetor3(lado, 0, 0), new Vetor3(lado, lado, 0), new Vetor3(0, lado, 0) });
        return l;
    }

    [Fact]
    public void AberturaTiraNosECarga()
    {
        var e = new EstruturaObjetos();
        var l = Quadrada();
        l.Aberturas.Add(new[] { new Vetor3(200, 200, 0), new Vetor3(400, 200, 0), new Vetor3(400, 400, 0), new Vetor3(200, 400, 0) }.ToList());
        e.Lajes.Add(l);
        e.Analisar();
        var q = e.Casos[EstruturaObjetos.CasoQ];
        Assert.Equal(10 * (36 - 4), q.Reacoes.Where(x => x != null).Sum(x => x[2]), 6);
        Assert.Equal(-1, l.Nos[6, 6]);                    // centro (300, 300) fora
        Assert.True(l.Nos[4, 4] >= 0);                    // canto da abertura (200, 200) fica
        Assert.DoesNotContain(e.Avisos, a => a.Contains("borda livre"));
    }

    [Fact]
    public void AlveolarLevaACargaNaDirecaoDoVao()
    {
        var e = new EstruturaObjetos();
        var l = Quadrada();
        l.Tipo = TipoLaje.Alveolar; l.FracaoMacica = 0.55; l.DirX = Vetor3.EixoX;
        e.Lajes.Add(l);
        e.Analisar();
        var q = e.Casos[EstruturaObjetos.CasoQ];
        double total = q.Reacoes.Where(x => x != null).Sum(x => x[2]);
        // bordas x = 0 e x = 600 (apoios do vão) levam quase tudo
        double nasBordasDoVao = 0;
        for (int j = 0; j <= l.Ny; j++) nasBordasDoVao += q.Reacoes[l.Nos[0, j]][2] + q.Reacoes[l.Nos[l.Nx, j]][2];
        // maciça quadrada: 50 % por simetria; alveolar: as faixas internas vão inteiras às bordas do vão (as das bordas laterais
        // estão sobre o apoio e descarregam direto nele)
        Assert.True(nasBordasDoVao / total > 0.88, $"{nasBordasDoVao / total:0.000}");
        Assert.True(q.Reacoes[l.Nos[0, l.Ny / 2]][2] > 6 * q.Reacoes[l.Nos[l.Nx / 2, 0]][2], $"apoio do vão {q.Reacoes[l.Nos[0, l.Ny / 2]][2]:0.00} × borda lateral {q.Reacoes[l.Nos[l.Nx / 2, 0]][2]:0.00}; fração {nasBordasDoVao / total:0.000}");
        var r = DimensionamentoObjetos.Laje(e, l, new ConfiguracaoProjeto());
        var l0 = Assert.Single(r.Verificacoes);
        Assert.Equal("L0", l0.Codigo);
        Assert.Equal(SituacaoVerificacao.Conferir, l0.Situacao);
        // faixa biapoiada de 6 m: Md+ ≈ 1,4·10·6²/8 = 63 kN·m/m
        Assert.Contains(r.Memoria.Linhas, x => x.Contains("Md+ = 6"));
    }

    [Fact]
    public void MalhaPassaPelaVigaDoMeio()
    {
        // laje 1220 × 620 sobre 3 vigas na direção y (x = 0, 610, 1220), alveolar com vão em x: as faixas são contínuas na viga do meio
        var mat = new MaterialBarra { E = 3000, G = 1250 };
        var e = new EstruturaObjetos();
        foreach (double x in new[] { 0.0, 610, 1220 })
            e.Vigas.Add(new VigaObjeto { Dono = new Dono { Tipo = TipoDono.Viga, Id = "V" + x, Nome = "V" + x }, ApoioI = new Vetor3(x, 0, -50), ApoioJ = new Vetor3(x, 620, -50),
                Secao = DEB.Core.Pecas.SecaoPoligonal.Retangulo(20, 50), Material = mat });
        var l = new LajeObjeto { Material = mat, Espessura = 20, Q = 10, Malha = 50, PesoEspecifico = 0, Tipo = TipoLaje.Alveolar, DirX = Vetor3.EixoX };
        l.Contorno.AddRange(new[] { new Vetor3(0, 0, 10), new Vetor3(1220, 0, 10), new Vetor3(1220, 620, 10), new Vetor3(0, 620, 10) });
        e.Lajes.Add(l);
        e.Analisar();
        Assert.Contains(610.0, l.Xs);
        Assert.True(e.Vigas[1].Barras.Count > 5, "a viga do meio recebe a laje");
        var r = DimensionamentoObjetos.Laje(e, l, new ConfiguracaoProjeto());
        // pré-fabricada: dois vãos biapoiados de 6,1 m (rótula sobre a viga do meio): Md+ ≈ 1,4·10·6,1²/8 = 65 kN·m/m, Md− ≈ 0
        Assert.Contains(r.Memoria.Linhas, x => x.Contains("Md+ = 6") && x.Contains("Md− = 0"));
    }
}
