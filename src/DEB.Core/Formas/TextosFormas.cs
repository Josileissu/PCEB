using System;

namespace DEB.Core.Formas;

/// <summary>
/// Textos das pranchas de formas (D25, docs/04_arquitetura/pranchas_de_formas.md) gravados nos parâmetros <c>SD_</c> pelo comando
/// "Atualizar dados das pranchas" — o identificador do Revit só mostra o parâmetro, não faz conta. Medidas já convertidas pelo
/// plugin (cm ou m); aqui só a regra de formatação, com testes.
/// </summary>
public static class TextosFormas
{
    /// <summary>Seção "b x h" em cm ("40x40", "20x47.5"); vazio se faltar uma das medidas (pilar circular, família sem b/h).</summary>
    public static string Secao(double? bCm, double? hCm)
    {
        if (bCm == null || hCm == null || !(bCm > 0.05) || !(hCm > 0.05)) return "";
        return Fmt.N(Arred(bCm.Value, 0.1)) + "x" + Fmt.N(Arred(hCm.Value, 0.1));
    }

    /// <summary>Nível para a prancha: relativo ao zero do projeto e, entre parênteses, a cota absoluta (m): "12,00 m (907,80)".</summary>
    public static string Nivel(double relativoM, double absolutoM) =>
        Fmt.Virgula(Arred(relativoM, 0.01), "0.00") + " m (" + Fmt.Virgula(Arred(absolutoM, 0.01), "0.00") + ")";

    /// <summary>Comprimento em metros com duas casas: "13,20 m".</summary>
    public static string Comprimento(double m) => Fmt.Virgula(Arred(m, 0.01), "0.00") + " m";

    /// <summary>Elevação do topo da viga em relação ao nível (cm), como na referência: "e=-29.5", "e=0" (uma casa só quando precisa).</summary>
    public static string Elevacao(double cm) => "e=" + Fmt.N(Arred(cm, 0.1));

    /// <summary>Peso próprio (kgf/m²) de uma laje maciça: espessura (cm) × densidade (kgf/m³), arredondado a 0,1.</summary>
    public static double PesoProprio(double espessuraCm, double densidadeKgfM3) => Arred(espessuraCm / 100.0 * densidadeKgfM3, 0.1);

    /// <summary>Arredonda ao passo (0,1 cm; 0,01 m) sem devolver "-0".</summary>
    public static double Arred(double x, double passo)
    {
        double r = Math.Round(x / passo, MidpointRounding.AwayFromZero) * passo;
        r = Math.Round(r, 6);
        return Math.Abs(r) < passo / 2 ? 0 : r;
    }
}
