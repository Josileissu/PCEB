using System;
using System.Linq;
using DEB.Core.Analise.Chapas;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Chapa em estado plano (D50, item 3): viga em balanço contra Timoshenko, equilíbrio e tração uniforme exata.</summary>
public class ChapaPlanaTests
{
    [Fact]
    public void Tracao_uniforme_e_exata()
    {
        // 20 × 10 cm, t = 2 cm, E = 3000, ν = 0: σx = P/A, alongamento = P·L/(E·A)
        var c = ChapaPlana.Malha(0, 20, 0, 10, 2.5, (x, y) => true, (x, y) => 2);
        c.Nu = 0;
        foreach (int i in Enumerable.Range(0, c.Nos.Count).Where(i => c.Nos[i].X < 1e-9)) { c.Presos.Add((i, 0)); if (c.Nos[i].Y < 1e-9) c.Presos.Add((i, 1)); }
        var dir = Enumerable.Range(0, c.Nos.Count).Where(i => Math.Abs(c.Nos[i].X - 20) < 1e-9).ToList();
        foreach (int i in dir) c.Forca(i, (c.Nos[i].Y < 1e-9 || c.Nos[i].Y > 10 - 1e-9 ? 0.5 : 1.0) * 60.0 / (dir.Count - 1), 0);
        c.Resolver();
        Assert.All(c.Elementos, e => Assert.Equal(3.0, e.Sx, 6));     // 60 kN / 20 cm²
        Assert.Equal(60 * 20 / (3000.0 * 20), c.U[dir[0] * 2], 8);
        Assert.Equal(-60, Enumerable.Range(0, c.Nos.Count).Sum(i => c.Reacoes[i * 2]), 6);
    }

    [Fact]
    public void Balanco_bate_com_Timoshenko()
    {
        // 100 × 10 cm, t = 1, E = 3000, ν = 0, P = 1 kN na ponta: δ = PL³/(3EI) + PL/(κGA) = 1,3333 + 0,0080 = 1,3413 cm
        var c = ChapaPlana.Malha(0, 100, -5, 5, 1, (x, y) => true, (x, y) => 1);
        c.Nu = 0;
        foreach (int i in Enumerable.Range(0, c.Nos.Count).Where(i => c.Nos[i].X < 1e-9)) { c.Presos.Add((i, 0)); c.Presos.Add((i, 1)); }
        var ponta = Enumerable.Range(0, c.Nos.Count).Where(i => Math.Abs(c.Nos[i].X - 100) < 1e-9).ToList();
        foreach (int i in ponta) c.Forca(i, 0, -(Math.Abs(Math.Abs(c.Nos[i].Y) - 5) < 1e-9 ? 0.5 : 1.0) / (ponta.Count - 1));
        c.Resolver();
        int meio = ponta.First(i => Math.Abs(c.Nos[i].Y) < 1e-9);
        Assert.Equal(-1.3413, c.U[meio * 2 + 1], 1.3413 * 0.08);
        Assert.Equal(1.0, Enumerable.Range(0, c.Nos.Count).Sum(i => c.Reacoes[i * 2 + 1]), 6);
        // tensão de flexão no engaste (fibra externa): M/W = 100/(10²/6) = 6 kN/cm² (o 1º elemento fica a meia célula do engaste)
        var canto = c.Elementos.Where(e => c.Nos[e.Nos[0]].X < 1e-9).OrderBy(e => c.Nos[e.Nos[0]].Y).First();
        Assert.Equal(-6 * 4.5 / 5 * 99.5 / 100, canto.Sx, 0.6);
    }
}
