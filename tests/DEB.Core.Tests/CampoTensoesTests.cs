using System;
using System.Linq;
using DEB.Core.Analise.Chapas;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Campos de tensões (D50, item 9): casos com resposta conhecida.</summary>
public class CampoTensoesTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _o;
    public CampoTensoesTests(Xunit.Abstractions.ITestOutputHelper o) => _o = o;

    /// <summary>Painel 20 × 40 cm (espessura 10) apoiado embaixo, força no topo; malha de 2,5 cm.</summary>
    private static (ChapaPlana C, int[] Topo) Painel()
    {
        var c = ChapaPlana.Malha(0, 20, 0, 40, 2.5, (x, y) => true, (x, y) => 10);
        c.E = 3000;
        var topo = Enumerable.Range(0, c.Nos.Count).Where(i => Math.Abs(c.Nos[i].Y - 40) < 1e-6).ToArray();
        foreach (int i in Enumerable.Range(0, c.Nos.Count).Where(i => Math.Abs(c.Nos[i].Y) < 1e-6)) { c.Presos.Add((i, 1)); }
        c.Presos.Add((Enumerable.Range(0, c.Nos.Count).First(i => Math.Abs(c.Nos[i].Y) < 1e-6 && Math.Abs(c.Nos[i].X) < 1e-6), 0));
        return (c, topo);
    }

    private static void Distribuir(ChapaPlana c, int[] nos, double total)
    {
        double xmin = nos.Min(i => c.Nos[i].X), xmax = nos.Max(i => c.Nos[i].X);
        foreach (int i in nos)
        {
            bool borda = Math.Abs(c.Nos[i].X - xmin) < 1e-6 || Math.Abs(c.Nos[i].X - xmax) < 1e-6;
            c.Forca(i, 0, total / (nos.Length - 1) * (borda ? 0.5 : 1));
        }
    }

    [Fact]
    public void Compressao_uniforme_esmaga_no_fcd_reduzido()
    {
        var (c, topo) = Painel();
        Distribuir(c, topo, -200);                    // 200 kN em 20 × 10 cm = 1 kN/cm²
        var ct = new CampoTensoes(c) { Fck = 30, Fcd = 3.0 / 1.4, Ec = 3000 };
        ct.Analisar();
        Assert.NotNull(ct.Calculo);
        Assert.Equal(-1.0, ct.Calculo.S2.Average(), 2);
        // capacidade: fcd·área / 200 kN = 2,143·200/200 (ηfc = 1 para fck 30)
        Assert.InRange(ct.LambdaMax, 1.95, 2.2);
    }

    [Fact]
    public void Tracao_vai_toda_para_a_armadura_e_escoa_em_fyd_As()
    {
        var (c, topo) = Painel();
        // barra vertical no meio (x = 10) do pé ao topo, 2 cm²
        var linha = Enumerable.Range(0, c.Nos.Count).Where(i => Math.Abs(c.Nos[i].X - 10) < 1e-6).OrderBy(i => c.Nos[i].Y).ToList();
        for (int k = 0; k + 1 < linha.Count; k++) c.Barras.Add(new BarraChapa { I = linha[k], J = linha[k + 1], Area = 2 });
        foreach (int i in Enumerable.Range(0, c.Nos.Count).Where(i => Math.Abs(c.Nos[i].Y) < 1e-6)) c.Presos.Add((i, 0));
        c.Forca(linha[linha.Count - 1], 0, 40);       // 40 kN de tração na ponta da barra (sem tirante no topo, a carga espalhada seria um mecanismo)
        var ct = new CampoTensoes(c) { Fck = 30, Fcd = 3.0 / 1.4, Ec = 3000, Fyd = 43.48 };
        ct.Analisar();
        // o concreto não leva tração: a barra leva os 40 kN → σs ≈ 20 kN/cm² no meio da altura
        double sMeio = ct.Calculo.SigmaS[ct.Calculo.SigmaS.Length / 2];
        Assert.InRange(sMeio, 18, 22);
        Assert.True(ct.Calculo.S2.All(s => s <= 1e-6));
        // escoa em fyd·As = 86,96 kN → λ ≈ 2,17
        Assert.InRange(ct.LambdaMax, 2.0, 2.3);
    }
}
