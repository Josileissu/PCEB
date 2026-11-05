using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Templates;
using DEB.Core.Templates.Reservatorio;
using Xunit;

namespace DEB.Core.Tests;

public class ReservatorioTests
{
    private static double Comp(BarraTemplate b, int i) =>
        Math.Sqrt(Math.Pow(b.Vertices[i + 1].X - b.Vertices[i].X, 2) + Math.Pow(b.Vertices[i + 1].Y - b.Vertices[i].Y, 2));

    [Fact]
    public void TemplateGeraParedesFundoECortesNoPadraoDoExemplo()
    {
        var t = new TemplateReservatorio();
        var v = new ValoresTemplate(t.Parametros);
        v["Lx"] = "340"; v["Ly"] = "340"; v["t"] = "20"; v["H"] = "330"; v["hf"] = "15"; v["InternasY"] = "170";
        v["HBitola"] = "8"; v["HEsp"] = "16"; v["VBitola"] = "8"; v["VEsp"] = "20";
        v["Lajes"] = "L3|330|15|";
        Assert.Empty(CatalogoTemplates.Validar(t, v));
        var r = t.Gerar(v, new ConfiguracaoArmadura());

        // horizontal da parede sul: 340 − 2·4,5 = 331, ganchos fechando na outra face 20 − 9 = 11 (como no exemplo)
        var h = r.Barras.First(b => b.Nome.Contains("PAR SUL horizontal ext."));
        Assert.Equal(11, Comp(h, 0), 1); Assert.Equal(331, Comp(h, 1), 1); Assert.Equal(11, Comp(h, 2), 1);
        Assert.Equal(16, h.Espacamento);
        // vertical: 330 + 15 − 9 = 336 com ganchos de 90° da NBR (ø8: 9)
        var vert = r.Barras.First(b => b.Nome.Contains("PAR OESTE vertical"));
        Assert.Equal(336, Comp(vert, 1), 1); Assert.Equal(9, Comp(vert, 0), 1);
        // fundo: dobra 15 − 7 = 8 e volta de 30 na positiva
        var fp = r.Barras.First(b => b.Nome.Contains("fundo positiva (x)"));
        Assert.Equal(30, Comp(fp, 0), 1); Assert.Equal(8, Comp(fp, 1), 1);
        // L de canto (4 externos) e cantos das 2 células (8), arranques, borda, laje superior com negativa na interna
        Assert.Equal(4, r.Barras.Count(b => b.Nome.Contains("canto externo")));
        Assert.Equal(8, r.Barras.Count(b => b.Nome.Contains("canto interno")));
        Assert.Contains(r.Barras, b => b.Nome.Contains("arranque"));
        Assert.Contains(r.Barras, b => b.Nome.Contains("borda (x)") && b.Quantidade == 2);
        Assert.Contains(r.Barras, b => b.Nome.Contains("L3 negativa"));
        Assert.Contains(r.Textos, x => x.Texto == "CORTE A-A" && x.Layer == "TEXTOS");
        Assert.Contains(r.Textos, x => x.Texto == "ESC 1:50");
        Assert.NotNull(r.PosicaoRA);
        // perna do L (88) maior que o traspasse de ø8 em C25: sem aviso; arranque 111 idem
        Assert.DoesNotContain(r.Avisos, a => a.StartsWith("L dos cantos"));
    }

    /// <summary>Desenho no formato do Eberick, em cm de papel (1:25 e 1:50), como o teste do usuário (2026-11-05).</summary>
    private static (List<LinhaDesenho>, List<TextoDesenho>) DesenhoEberick()
    {
        var ls = new List<LinhaDesenho>();
        var ts = new List<TextoDesenho>();
        const string F = "DT-Forma";
        void Ret(double x0, double y0, double x1, double y1, double k)
        {
            ls.Add(new LinhaDesenho(x0 / k, y0 / k, x1 / k, y0 / k, F)); ls.Add(new LinhaDesenho(x1 / k, y0 / k, x1 / k, y1 / k, F));
            ls.Add(new LinhaDesenho(x1 / k, y1 / k, x0 / k, y1 / k, F)); ls.Add(new LinhaDesenho(x0 / k, y1 / k, x0 / k, y0 / k, F));
        }
        void Txt(string s, double x, double y, double h, double rot = 0) => ts.Add(new TextoDesenho { Texto = s, X = x, Y = y, Altura = h, Rotacao = rot });

        // planta 396 × 402, paredes de 15, interna com eixo em y = 165,5 (em cm reais; desenho ÷ 25)
        Ret(0, 0, 396, 402, 25); Ret(15, 15, 381, 158, 25); Ret(15, 173, 381, 387, 25);
        Txt("PLANTA (199.0)", 17, -2, 0.7); Txt("Escala 1:25", 17, -2.5, 0.2);
        Txt("20 N14 ø6.3 c/20 C=405", 8, -1.0, 0.2); Txt("16 N14 ø6.3 c/25 C=405", 8, 17.0, 0.2);
        Txt("31 N17 ø6.3 c/13 C=411", -1.5, 8, 0.2, Math.PI / 2); Txt("31 N17 ø6.3 c/13 C=411", 17.2, 8, 0.2, Math.PI / 2);
        Txt("4 N30 ø8.0 C=407", 0.5, 0.5, 0.2);
        // corte em x (deslocado): fundo 456 × 15 (balanço 30), paredes de 398
        double ox = 2000;
        void RetC(double x0, double y0, double x1, double y1) => Ret(ox + x0, y0, ox + x1, y1, 25);
        RetC(0, 0, 456, 15); RetC(30, 15, 45, 413); RetC(411, 15, 426, 413);
        Txt("CORTE A-A", (ox + 460) / 25, -2, 0.7); Txt("Escala 1:25", (ox + 460) / 25, -2.5, 0.2);
        Txt("20 N16 ø6.3 c/20 C=420", (ox - 40) / 25, 8, 0.2, Math.PI / 2);
        Txt("30 N20 ø6.3 c/13 C=229", (ox + 100) / 25, -1, 0.2);
        // planta da laje de fundo, positiva, 1:50
        double oy = -1500;
        Ret(0, oy, 456, oy + 462, 50); Ret(30, oy + 30, 426, oy + 432, 50);
        Txt("ARMAÇÃO POSITIVA DAS LAJES (0.0)", 9.5, oy / 50 - 1.5, 0.7); Txt("Escala 1:50", 9.5, oy / 50 - 2, 0.2);
        Txt("11 N6 ø6.3 c/20 C=390", 4, oy / 50 + 4, 0.2); Txt("2 N11 ø6.3 C=456", 4, oy / 50 + 0.4, 0.2);
        // negativa sem diâmetro
        double ox2 = 800;
        Ret(ox2, oy, ox2 + 456, oy + 462, 50); Ret(ox2 + 30, oy + 30, ox2 + 426, oy + 432, 50);
        Txt("ARMAÇÃO NEGATIVA DAS LAJES (0.0)", (ox2 + 470) / 50, oy / 50 - 1.5, 0.7); Txt("Escala 1:50", (ox2 + 470) / 50, oy / 50 - 2, 0.2);
        Txt("19 N27 c/20", (ox2 + 200) / 50, oy / 50 + 4, 0.2);
        return (ls, ts);
    }

    [Fact]
    public void ReconheceReservatorioDoEberickEmCmDePapel()
    {
        var (ls, ts) = DesenhoEberick();
        var r = ReconhecedorReservatorio.Reconhecer(ls, ts);
        Assert.True(r.Ok, string.Join(" | ", r.Avisos.Concat(r.Relatorio)));
        var v = r.Valores;
        Assert.Equal(396, v.Numero("Lx"), 0); Assert.Equal(402, v.Numero("Ly"), 0); Assert.Equal(15, v.Numero("t"), 0);
        Assert.Equal("165.5", v["InternasY"]); Assert.Equal("", v["InternasX"]);
        Assert.Equal(15, v.Numero("hf"), 0); Assert.Equal(398, v.Numero("H"), 0); Assert.Equal(30, v.Numero("Balanco"), 0);
        Assert.Equal(6.3, v.Numero("HBitola")); Assert.Equal(20, v.Numero("HEsp")); Assert.Equal(13, v.Numero("HEspY"));
        Assert.Equal(20, v.Numero("VEsp")); Assert.Equal(13, v.Numero("ArrEsp"));
        Assert.Equal(20, v.Numero("FPosEsp")); Assert.Equal(6.3, v.Numero("FNegBitola")); // negativa sem ø: a da positiva
        Assert.Equal(2, v.Inteiro("BordaQtd")); Assert.Equal(8, v.Numero("CantoVBitola"));
        Assert.Contains(r.Avisos, a => a.Contains("não informa o diâmetro"));
        // e o template gera com esses valores
        var res = new TemplateReservatorio().Gerar(v, new ConfiguracaoArmadura());
        Assert.Contains(res.Barras, b => b.Nome.Contains("PAR INT 1 horizontal"));
        Assert.Equal(13, res.Barras.First(b => b.Nome.Contains("PAR OESTE horizontal")).Espacamento);
    }
}
