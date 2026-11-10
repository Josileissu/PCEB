using System;
using System.Collections.Generic;
using DEB.Core.Templates.PilarPreMoldado;

namespace DEB.Core.Templates.VigaPreMoldada;

/// <summary>
/// Ligação por cantoneira para solda nas pontas (etapa 4, 2026-11-08), no padrão da prancha 1071 (VT-1, CAN-03): uma
/// cantoneira em cada canto lateral da face da ponta, perto do topo; a prancha da viga só a representa (retângulo na
/// elevação, L na planta), cota a posição e a altura e põe o nome — a peça é detalhada à parte e não entra na relação do aço.
/// </summary>
public sealed partial class TemplateVigaPreMoldada
{
    private static IEnumerable<ParametroTemplate> ParametrosCantoneira() => new[]
    {
        P(G3, "CantE", "Cantoneira para solda na ponta esquerda", "Não", tipo: TipoParametro.SimNao,
          descricao: "Uma cantoneira em cada canto lateral da face da ponta, perto do topo (como a CAN-03 da prancha 1071)."),
        P(G3, "CantD", "Cantoneira para solda na ponta direita", "Não", tipo: TipoParametro.SimNao),
        P(G3, "CantNome", "Cantoneira: nome", "CAN-03", tipo: TipoParametro.Texto),
        P(G3, "CantAlt", "Cantoneira: altura", "12", "cm", min: 1),
        P(G3, "CantTopo", "Cantoneira: distância ao topo", "5", "cm", min: 0),
        P(G3, "CantAba", "Cantoneira: aba", "5", "cm", min: 1),
    };

    private static bool TemCantoneira(ValoresTemplate v, bool esquerda) => v.SimNao(esquerda ? "CantE" : "CantD");

    /// <summary>Avisa a cantoneira que não cabe na face da ponta (no dente, a face tem a altura do dente).</summary>
    private static void ValidarCantoneiras(ValoresTemplate v, double b, double h, ExtremidadeViga e, ExtremidadeViga d, List<string> avisos)
    {
        foreach (var (x, esq, lado) in new[] { (e, true, "esquerda"), (d, false, "direita") })
        {
            if (!TemCantoneira(v, esq)) continue;
            double face = x.TemDente ? x.Dente.Altura : h;
            if (v.Numero("CantTopo") + v.Numero("CantAlt") > face + 1e-6)
                avisos.Add($"Cantoneira da ponta {lado}: distância ao topo + altura passa da face da ponta ({Num(face, "0.#")} cm).");
            if (2 * v.Numero("CantAba") > b + 1e-6)
                avisos.Add($"Cantoneira da ponta {lado}: as duas abas ({Num(v.Numero("CantAba"), "0.#")} cm) passam da largura da viga.");
        }
    }

    /// <summary>Elevação: retângulo da aba junto da face da ponta, cotas topo → cantoneira → altura e o nome com linha de chamada.</summary>
    private static void CantoneirasEmElevacao(Ctx c, ValoresTemplate v, double L, double yT)
    {
        double hv = c.hV, esc = c.escV, alt = v.Numero("CantAlt"), dt = v.Numero("CantTopo"), aba = v.Numero("CantAba");
        foreach (var esq in new[] { true, false })
        {
            if (!TemCantoneira(v, esq)) continue;
            double x0 = esq ? 0 : L - aba, x1 = esq ? aba : L, y1 = yT - dt, y0 = y1 - alt, xf = esq ? 0 : L;
            c.Retangulo(x0, y0, x1, y1, "ARM-INSERTO");
            // cotas por fora, depois da cadeia das alturas da ponta; cada uma numa coluna (em 1:50 os textos não cabem entre as linhas)
            double s = esq ? -1 : 1;
            c.Cota(xf, y1, xf, y0, xf + s * 7.5 * hv, (y1 + y0) / 2, Math.PI / 2, esc);
            if (dt > 0) c.Cota(xf, yT, xf, y1, xf + s * 10 * hv, (yT + y1) / 2, Math.PI / 2, esc);
            Rotulo(c, v["CantNome"], esq, new[] { ((x0 + x1) / 2, (y0 + y1) / 2) }, xf, yT + 5 * hv);
        }
    }

    /// <summary>Planta: um L em cada canto lateral da face da ponta e o nome com uma linha para cada uma.</summary>
    private static void CantoneirasEmPlanta(Ctx c, ValoresTemplate v, double b, double L, double yP)
    {
        double hv = c.hV, aba = v.Numero("CantAba"), t = Math.Min(1, aba / 5);   // espessura só de representação
        foreach (var esq in new[] { true, false })
        {
            if (!TemCantoneira(v, esq)) continue;
            double xf = esq ? 0 : L, s = esq ? 1 : -1;
            var pontas = new List<(double, double)>();
            foreach (var (yc, sy) in new[] { (yP, 1.0), (yP + b, -1.0) })
            {
                // aba na face da ponta (ao longo de b) e aba na face lateral (ao longo de L), por dentro do concreto
                c.R.Contornos.Add(new ContornoTemplate
                {
                    Fechado = true, Layer = "ARM-INSERTO",
                    Vertices = { (xf, yc), (xf + s * aba, yc), (xf + s * aba, yc + sy * t), (xf + s * t, yc + sy * t), (xf + s * t, yc + sy * aba), (xf, yc + sy * aba) },
                });
                pontas.Add((xf + s * t / 2, yc + sy * aba / 2));
            }
            Rotulo(c, v["CantNome"], esq, pontas, xf, yP + b + 5 * hv, 7);
        }
    }

    /// <summary>Nome da peça fora da ponta (acima), com patamar e uma linha até cada peça.</summary>
    private static void Rotulo(Ctx c, string nome, bool esq, IEnumerable<(double X, double Y)> alvos, double xf, double y, double afast = 3)
    {
        double hv = c.hV, w = Math.Max(4, 0.65 * hv * (nome ?? "").Length + hv);
        double xa = esq ? xf - afast * hv : xf + afast * hv, xb = esq ? xa - w : xa + w;   // patamar sob o texto
        c.Texto(nome ?? "", (xa + xb) / 2, y + 0.4 * hv, 0.9 * hv, 1, 0, "ARM-INSERTO");
        c.Linha(xa, y, xb, y, "ARM-INSERTO");
        foreach (var (x, yy) in alvos) c.Linha(xa, y, x, yy, "ARM-INSERTO");
    }
}
