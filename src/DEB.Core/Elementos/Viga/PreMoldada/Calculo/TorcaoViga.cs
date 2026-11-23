using System;

namespace DEB.Core.Elementos.Viga.PreMoldada.Calculo;

/// <summary>
/// Seção vazada equivalente de uma seção retangular cheia (NBR 6118, 17.5.1.4.1): espessura he, área Ae limitada pela linha média
/// da parede e perímetro ue dessa área. Unidades: cm e cm².
/// </summary>
public sealed class SecaoTorcao
{
    public double B { get; set; }
    public double H { get; set; }
    /// <summary>Distância do eixo da barra longitudinal do canto à face (cm).</summary>
    public double C1 { get; set; }
    public double He { get; set; }
    /// <summary>Lados do contorno de Ae (cm): horizontal (largura) e vertical (altura).</summary>
    public double LadoB { get; set; }
    public double LadoH { get; set; }
    public double Ae => LadoB * LadoH;
    public double Ue => 2 * (LadoB + LadoH);
    /// <summary>A/u &lt; 2c1: he = A/u e Ae pelos eixos das barras dos cantos (a alternativa de 17.5.1.4.1).</summary>
    public bool PelosCantos { get; set; }
}

/// <summary>
/// Torção uniforme no ELU (NBR 6118:2023, 17.5.1; solicitações combinadas, 17.7; disposições, 18.3.4), com as bielas a 45° — o
/// mesmo ângulo do modelo I do cortante (17.7.2.1). Roteiro e exemplos de conferência: BASTOS, P.S.S., Torção em Vigas de Concreto
/// Armado, UNESP Bauru, 2024 (exemplos 2 e 3 nos testes). Unidades: cm, kN, kN/cm² e kN·cm.
/// </summary>
public static class TorcaoViga
{
    /// <summary>
    /// Seção equivalente da seção retangular b × h: he = A/u, que deve ficar entre 2c1 e A/u; quando A/u &lt; 2c1, adota-se
    /// he = A/u ≤ b − 2c1 e Ae pelos eixos das barras dos cantos (17.5.1.4.1).
    /// </summary>
    public static SecaoTorcao Secao(double b, double h, double c1)
    {
        double au = b * h / (2 * (b + h));
        var s = new SecaoTorcao { B = b, H = h, C1 = c1 };
        if (au >= 2 * c1 - 1e-9)
        {
            s.He = au; s.LadoB = b - au; s.LadoH = h - au;
        }
        else
        {
            s.PelosCantos = true;
            s.He = Math.Min(au, b - 2 * c1);
            s.LadoB = b - 2 * c1; s.LadoH = h - 2 * c1;
        }
        return s;
    }

    /// <summary>TRd,2 = 0,5·αv2·fcd·Ae·he·sen 2θ, com θ = 45° (17.5.1.5). fck em MPa, fcd em kN/cm² → kN·cm.</summary>
    public static double TRd2(SecaoTorcao s, double fck, double fcd) => 0.5 * (1 - fck / 250) * fcd * s.Ae * s.He;

    /// <summary>Estribo de torção POR RAMO contido na parede: A90/s = Td/(2·Ae·fywd) (17.5.1.6, θ = 45°), cm²/cm.</summary>
    public static double As90PorCm(SecaoTorcao s, double td, double fywd) => td <= 0 ? 0 : td / (2 * s.Ae * fywd);

    /// <summary>Longitudinal de torção por cm de perímetro ue: Asℓ/ue = Td/(2·Ae·fyd) (17.5.1.6, θ = 45°), cm²/cm.</summary>
    public static double AslPorCm(SecaoTorcao s, double td, double fyd) => td <= 0 ? 0 : td / (2 * s.Ae * fyd);

    /// <summary>Longitudinal mínima por cm de perímetro: 0,2·fctm/fywk·he (17.5.1.2), fctm e fywk na mesma unidade.</summary>
    public static double AslMinPorCm(SecaoTorcao s, double fctm, double fywk) => 0.2 * fctm / fywk * s.He;

    /// <summary>
    /// Torque distribuído uniforme <paramref name="mt"/> (kN·cm/cm) no trecho [a1, a2] de uma viga com a rotação presa nos dois
    /// apoios (xa, xb): reação no apoio esquerdo Ta = ∫mt·(xb − x)/ℓ dx (rigidez à torção constante) e o torque em x.
    /// O trecho fora dos apoios é levado ao apoio mais próximo.
    /// </summary>
    public static Func<double, double> Diagrama(double mt, double a1, double a2, double xa, double xb, out double ta, out double tb)
    {
        double l = xb - xa;
        double i1 = Math.Max(a1, xa), i2 = Math.Min(a2, xb);
        double dentro = i2 > i1 ? mt * (i2 - i1) : 0;
        double foraE = mt * Math.Max(0, Math.Min(a2, xa) - a1), foraD = mt * Math.Max(0, a2 - Math.Max(a1, xb));
        double taIn = i2 > i1 ? mt / l * ((xb - i1) * (xb - i1) - (xb - i2) * (xb - i2)) / 2 : 0;
        double t0 = taIn;                 // torque logo à direita do apoio esquerdo (só a parte do vão)
        ta = taIn + foraE;
        tb = dentro - taIn + foraD;
        double TA = t0;
        return x =>
        {
            if (x <= xa) return TA;
            double acc = i2 > i1 ? mt * Math.Max(0, Math.Min(x, i2) - i1) : 0;
            return TA - acc;
        };
    }
}
