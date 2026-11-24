using System;
using System.Globalization;
using System.IO;
using System.Linq;
using DEB.Core.Elementos.Metalica;
using DEB.Core.Normas.Verificacao;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>
/// Perfis de aço e barras pela NBR 8800:2024 (D48). Gabaritos: o catálogo métrico do AISC instalado com o Revit (propriedades) e os
/// exemplos resolvidos do AISC (v15.1, vol. 1): E.1D (coluna W14×90) e F.1-3A (viga W18×50 com Lb = 17,5 ft, Cb = 1). Nesses casos
/// a NBR 8800:2024 usa as mesmas expressões nominais (χ·fy = Fcr; FLT com Cb = 1; Mr = 0,7·fy·W).
/// </summary>
public class PerfisAcoTests
{
    private static readonly AcoEstrutural A992 = new AcoEstrutural { Nome = "A992 (50 ksi)", Fy = 344.74, Fu = 448.2, E = 199948, G = 77221 };

    [Fact]
    public void Catalogo_tem_os_perfis_W_e_HP_e_le_os_nomes_brasileiros()
    {
        Assert.True(PerfilI.Catalogo.Count > 250);
        Assert.Equal("W310X38.7", PerfilI.Buscar("W 310 x 38,7").Nome);
        Assert.Equal("W310X38.7", PerfilI.Buscar("w310x38.7").Nome);
        Assert.Equal("HP250X62", PerfilI.Buscar("HP 250 x 62,0").Nome);
        var cvs = PerfilI.Buscar("CVS 400x200x9.5x6.3");
        Assert.True(cvs.Soldado);
        Assert.Equal(400, cvs.D);
        Assert.Null(PerfilI.Buscar("UDC 100"));
    }

    [Fact]
    public void Propriedades_calculadas_batem_com_o_catalogo_do_AISC()
    {
        string arq = @"C:\ProgramData\Autodesk\RVT 2025\Libraries\English\US\Structural Framing\Steel\AISC 15.0\M_W Shapes.txt";
        if (!File.Exists(arq)) return;   // gabarito só onde o Revit está instalado
        int n = 0; double piorJ = 0, piorI = 0;
        foreach (string linha in File.ReadLines(arq).Skip(1))
        {
            string[] c = linha.Split(',');
            if (c.Length < 18 || c[0] == "") continue;
            double V(int i) => double.Parse(c[i], CultureInfo.InvariantCulture);
            var p = PerfilI.Buscar(c[0]);
            Assert.NotNull(p);
            Assert.Equal(V(6) / 100, p.A, 0.012 * p.A);                       // A (mm² → cm²)
            piorI = Math.Max(piorI, Math.Abs(p.Ix - V(9) / 1e4) / p.Ix);
            Assert.Equal(V(9) / 1e4, p.Ix, 0.02 * p.Ix);
            Assert.Equal(V(10) / 1e4, p.Iy, 0.025 * p.Iy);
            Assert.Equal(V(13) / 1e3, p.Zx, 0.02 * p.Zx);
            Assert.Equal(V(14) / 1e3, p.Zy, 0.025 * p.Zy);
            piorJ = Math.Max(piorJ, Math.Abs(p.J - V(15) / 1e4) / p.J);
            Assert.Equal(V(15) / 1e4, p.J, 0.06 * p.J);
            Assert.Equal(V(16) / 1e6, p.Cw, 0.03 * p.Cw);
            n++;
        }
        Assert.True(n > 250);
    }

    [Fact]
    public void Coluna_W14x90_do_exemplo_E1D_do_AISC()
    {
        // W14×90 = W360X134; Lcx = 30 ft, Lcy = Lz = 15 ft; Pn = 1030 kips = 4582 kN (Fcr = 38,9 ksi)
        var p = PerfilI.Buscar("W360X134");
        var l = new ComprimentosBarra { Lx = 30 * 30.48, Ly = 15 * 30.48, Lz = 15 * 30.48, Lb = 15 * 30.48 };
        var r = BarraAco.Resistencias(p, A992, l);
        Assert.Equal("flexão em x", r.ModoNe);
        Assert.Equal(1.0, r.Q, 6);
        Assert.Equal(4582, r.NcRd * BarraAco.GamaA1, 0.015 * 4582);
    }

    [Fact]
    public void Viga_W18x50_do_exemplo_F13A_do_AISC()
    {
        // W18×50 = W460X74, Lb = 17,5 ft, Cb = 1: φMn = 222 kip·ft → Mn = 246,7 kip·ft = 334,5 kN·m; Mp = 421,1 kip·ft = 571 kN·m
        var p = PerfilI.Buscar("W460X74");
        var l = new ComprimentosBarra { Lx = 35 * 30.48, Ly = 17.5 * 30.48, Lz = 17.5 * 30.48, Lb = 17.5 * 30.48, Cb = 1 };
        var r = BarraAco.Resistencias(p, A992, l);
        Assert.Equal("FLT", r.GovernaMx);
        Assert.Equal(334.5, r.MxRd * BarraAco.GamaA1 / 100, 334.5 * 0.02);
        Assert.Equal(571, p.Zx * A992.Fy / 10 / 100, 571 * 0.015);
    }

    [Fact]
    public void Cb_da_viga_biapoiada_com_carga_uniforme_e_1_14()
    {
        // M(x) = 4·x·(1 − x) (máximo 1 no meio): M(¼) = M(¾) = 0,75
        Assert.Equal(1.136, BarraAco.Cb(1, 0.75, 1, 0.75), 3);
        Assert.Equal(1.0, BarraAco.Cb(1, 1, 1, 1), 6);
    }

    [Fact]
    public void Soldado_de_mesa_esbelta_tem_area_efetiva_menor()
    {
        // mesa 400 × 8: b/t = 25 > 0,64·√(kc·E/fy)
        var p = PerfilI.Buscar("VS 500x400x8x6.3");
        var r = BarraAco.Resistencias(p, AcoEstrutural.Buscar("A572"), new ComprimentosBarra { Lx = 300, Ly = 300, Lz = 300, Lb = 300 });
        Assert.True(r.Q < 0.95);
        Assert.Equal("FLM", r.GovernaMx);
    }

    [Fact]
    public void Verificacao_aponta_o_caso_e_a_interacao()
    {
        var p = PerfilI.Buscar("W310X38.7");
        var l = new ComprimentosBarra { Lx = 400, Ly = 200, Lz = 200, Lb = 200, Cb = 1.14 };
        var casos = new[]
        {
            new EsforcosAco { Nome = "ELU 1", N = -300, Mx = 8000, Vy = 60 },
            new EsforcosAco { Nome = "ELU 2", N = -50, Mx = 12000, Vy = 90 },
        };
        var v = BarraAco.Verificar(p, AcoEstrutural.Buscar("A572"), l, casos, "Viga V1", out var r);
        var inter = v.Single(x => x.Codigo == "MT6");
        Assert.Contains("ELU", inter.Valor);
        Assert.Equal(7, v.Count);
        // 2º caso: N/NRd < 0,2 → N/(2NRd) + M/MRd
        double eta2 = 50 / r.NcRd / 2 + 12000 / r.MxRd;
        double eta1 = 300 / r.NcRd + 8.0 / 9 * 8000 / r.MxRd;
        Assert.Contains(Fmt.Virgula(Math.Max(eta1, eta2), "0.00"), inter.Valor);
    }
}
