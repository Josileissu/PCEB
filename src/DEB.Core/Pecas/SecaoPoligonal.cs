using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DEB.Core.Pecas;

/// <summary>Forma reconhecida da seção transversal.</summary>
public enum TipoSecao { Retangular, L, T, TInvertido, I, Poligonal }

/// <summary>Faixa horizontal da seção com largura constante: de <see cref="Z0"/> a <see cref="Z1"/>, ocupando de <see cref="Y0"/> a <see cref="Y1"/>.</summary>
public sealed class FaixaSecao
{
    public double Z0 { get; set; }
    public double Z1 { get; set; }
    public double Y0 { get; set; }
    public double Y1 { get; set; }
    public double Altura => Z1 - Z0;
    public double Largura => Y1 - Y0;
}

/// <summary>
/// Seção transversal de um trecho de peça (2026-11-09, conferência e subdivisão das peças): contorno e furos no plano da seção,
/// em cm — y na horizontal (largura), z na vertical (altura). Calcula as propriedades (área, CG, inércias), que servem às
/// barras da análise, e reconhece a forma (retangular, L, T, T invertido, I ou poligonal) pelas faixas de largura ao longo da
/// altura. Ver docs/04_arquitetura/conferencia_e_subdivisao_pecas.md.
/// </summary>
public sealed class SecaoPoligonal
{
    public const double Tolerancia = 0.5;

    public List<(double Y, double Z)> Contorno { get; set; } = new List<(double, double)>();
    public List<List<(double Y, double Z)>> Furos { get; set; } = new List<List<(double, double)>>();

    public static SecaoPoligonal Retangulo(double b, double h, double y0 = 0, double z0 = 0) => new SecaoPoligonal
    {
        Contorno = { (y0, z0), (y0 + b, z0), (y0 + b, z0 + h), (y0, z0 + h) },
    };

    // ---------------- propriedades ----------------

    public double YMin => Contorno.Min(p => p.Y);
    public double YMax => Contorno.Max(p => p.Y);
    public double ZMin => Contorno.Min(p => p.Z);
    public double ZMax => Contorno.Max(p => p.Z);
    /// <summary>Largura e altura totais (caixa envolvente).</summary>
    public double B => YMax - YMin;
    public double H => ZMax - ZMin;

    /// <summary>Área (cm²), descontados os furos.</summary>
    public double Area => Integrais(Contorno).A - Furos.Sum(f => Integrais(f).A);

    /// <summary>Centro de gravidade (cm), no sistema da seção.</summary>
    public (double Y, double Z) CG
    {
        get
        {
            var c = Integrais(Contorno);
            double a = c.A, sy = c.Sy, sz = c.Sz;
            foreach (var f in Furos) { var k = Integrais(f); a -= k.A; sy -= k.Sy; sz -= k.Sz; }
            return (sy / a, sz / a);
        }
    }

    /// <summary>Inércia em torno do eixo horizontal pelo CG, ∫(z − zg)² dA (cm⁴): flexão no plano vertical (eixo forte da viga).</summary>
    public double Iy
    {
        get
        {
            double izz = Integrais(Contorno).Izz - Furos.Sum(f => Integrais(f).Izz);
            return izz - Area * Math.Pow(CG.Z, 2);
        }
    }

    /// <summary>Inércia em torno do eixo vertical pelo CG, ∫(y − yg)² dA (cm⁴).</summary>
    public double Iz
    {
        get
        {
            double iyy = Integrais(Contorno).Iyy - Furos.Sum(f => Integrais(f).Iyy);
            return iyy - Area * Math.Pow(CG.Y, 2);
        }
    }

    /// <summary>Área, momentos estáticos e de inércia (em torno da origem) de um polígono simples, sempre positivos.</summary>
    private static (double A, double Sy, double Sz, double Iyy, double Izz) Integrais(IReadOnlyList<(double Y, double Z)> p)
    {
        double a = 0, sy = 0, sz = 0, iyy = 0, izz = 0;
        for (int i = 0; i < p.Count; i++)
        {
            var (y0, z0) = p[i];
            var (y1, z1) = p[(i + 1) % p.Count];
            double cr = y0 * z1 - y1 * z0;
            a += cr;
            sy += (y0 + y1) * cr;
            sz += (z0 + z1) * cr;
            iyy += (y0 * y0 + y0 * y1 + y1 * y1) * cr;
            izz += (z0 * z0 + z0 * z1 + z1 * z1) * cr;
        }
        double s = a < 0 ? -1 : 1;   // sentido horário ou anti-horário
        return (s * a / 2, s * sy / 6, s * sz / 6, s * iyy / 12, s * izz / 12);
    }

    // ---------------- forma ----------------

    /// <summary>
    /// Faixas de largura constante, de baixo para cima (o contorno cortado por uma horizontal no meio de cada intervalo entre
    /// as alturas dos vértices; faixas vizinhas iguais se juntam). null se alguma horizontal corta a seção em mais de um
    /// pedaço (U, caixão…) ou se há lados inclinados — aí a seção é poligonal.
    /// </summary>
    public List<FaixaSecao> Faixas()
    {
        if (Contorno.Count < 3) return null;
        for (int i = 0; i < Contorno.Count; i++)
        {
            var (y0, z0) = Contorno[i];
            var (y1, z1) = Contorno[(i + 1) % Contorno.Count];
            if (Math.Abs(y1 - y0) > 1e-6 && Math.Abs(z1 - z0) > 1e-6) return null;   // lado inclinado
        }
        var zs = Contorno.Select(p => p.Z).Distinct().OrderBy(z => z).ToList();
        var r = new List<FaixaSecao>();
        for (int i = 0; i + 1 < zs.Count; i++)
        {
            if (zs[i + 1] - zs[i] < 1e-6) continue;
            var cortes = Cortes((zs[i] + zs[i + 1]) / 2);
            if (cortes.Count != 2) return null;
            var f = new FaixaSecao { Z0 = zs[i], Z1 = zs[i + 1], Y0 = cortes[0], Y1 = cortes[1] };
            var ant = r.LastOrDefault();
            if (ant != null && Math.Abs(ant.Y0 - f.Y0) <= 1e-6 && Math.Abs(ant.Y1 - f.Y1) <= 1e-6) ant.Z1 = f.Z1;
            else r.Add(f);
        }
        return r;
    }

    /// <summary>y onde a horizontal z corta o contorno, em ordem (2 valores = a seção num pedaço só naquela altura).</summary>
    public List<double> CortesHorizontais(double z) => Cortes(z);

    /// <summary>y onde a horizontal z corta o contorno, em ordem.</summary>
    private List<double> Cortes(double z)
    {
        var ys = new List<double>();
        for (int i = 0; i < Contorno.Count; i++)
        {
            var (y0, z0) = Contorno[i];
            var (y1, z1) = Contorno[(i + 1) % Contorno.Count];
            if ((z0 <= z && z1 > z) || (z1 <= z && z0 > z)) ys.Add(y0 + (z - z0) / (z1 - z0) * (y1 - y0));
        }
        ys.Sort();
        return ys;
    }

    /// <summary>Forma reconhecida e uma descrição com as dimensões principais (cm).</summary>
    public (TipoSecao Tipo, string Descricao) Classificar()
    {
        var f = Faixas();
        string N(double x) => x.ToString("0.#", CultureInfo.InvariantCulture);
        if (f == null || f.Count == 0 || f.Count > 3)
            return (TipoSecao.Poligonal, $"poligonal {N(B)} × {N(H)} (A = {N(Area)} cm²)");
        if (f.Count == 1) return (TipoSecao.Retangular, $"{N(B)} × {N(H)}");
        bool Dentro(FaixaSecao a, FaixaSecao b) => a.Y0 >= b.Y0 - Tolerancia && a.Y1 <= b.Y1 + Tolerancia;   // a cabe em b
        bool Centrada(FaixaSecao a, FaixaSecao b) => a.Y0 - b.Y0 > Tolerancia && b.Y1 - a.Y1 > Tolerancia;
        if (f.Count == 2)
        {
            var (baixo, cima) = (f[0], f[1]);
            bool abaEmbaixo = baixo.Largura > cima.Largura;
            var (aba, alma) = abaEmbaixo ? (baixo, cima) : (cima, baixo);
            if (!Dentro(alma, aba)) return (TipoSecao.Poligonal, $"poligonal {N(B)} × {N(H)} (A = {N(Area)} cm²)");
            string pos = abaEmbaixo ? "inferior" : "superior";
            if (Centrada(alma, aba))
                return (abaEmbaixo ? TipoSecao.TInvertido : TipoSecao.T,
                        $"{(abaEmbaixo ? "T invertido" : "T")}: alma {N(alma.Largura)}, mesa {N(aba.Largura)} × {N(aba.Altura)}, h = {N(H)}");
            string lado = Math.Abs(alma.Y0 - aba.Y0) <= Tolerancia ? "direita" : "esquerda";
            return (TipoSecao.L, $"L: alma {N(alma.Largura)}, aba {pos} à {lado} {N(aba.Largura - alma.Largura)} × {N(aba.Altura)}, b = {N(B)}, h = {N(H)}");
        }
        var (fb, fm, ft) = (f[0], f[1], f[2]);
        if (Dentro(fm, fb) && Dentro(fm, ft) && Centrada(fm, fb) && Centrada(fm, ft))
            return (TipoSecao.I, $"I: alma {N(fm.Largura)}, mesa inferior {N(fb.Largura)} × {N(fb.Altura)}, superior {N(ft.Largura)} × {N(ft.Altura)}, h = {N(H)}");
        return (TipoSecao.Poligonal, $"poligonal {N(B)} × {N(H)} (A = {N(Area)} cm²)");
    }

    /// <summary>
    /// Largura da alma: a faixa mais estreita; em seção com lado inclinado, a menor largura entre as horizontais que cortam a
    /// seção num pedaço só (amostradas ao longo da altura); a largura total se nenhuma servir.
    /// </summary>
    public double LarguraAlma
    {
        get
        {
            var f = Faixas();
            if (f != null && f.Count > 0) return f.Min(x => x.Largura);
            double min = double.MaxValue;
            for (int i = 1; i < 100; i++)
            {
                var c = Cortes(ZMin + H * i / 100.0);
                if (c.Count == 2) min = Math.Min(min, c[1] - c[0]);
            }
            return min < double.MaxValue ? min : B;
        }
    }

    /// <summary>Mesma seção (posição relativa à caixa, área, inércias e caixa dentro da tolerância)?</summary>
    public bool MesmaSecao(SecaoPoligonal o, double tol = Tolerancia)
    {
        bool P(double a, double b, double rel) => Math.Abs(a - b) <= Math.Max(tol, rel * Math.Max(Math.Abs(a), Math.Abs(b)));
        return P(B, o.B, 0) && P(H, o.H, 0) && P(Area, o.Area, 0.01) && P(CG.Y - YMin, o.CG.Y - o.YMin, 0) &&
               P(CG.Z - ZMin, o.CG.Z - o.ZMin, 0) && P(Iy, o.Iy, 0.01) && P(Iz, o.Iz, 0.01);
    }
}
