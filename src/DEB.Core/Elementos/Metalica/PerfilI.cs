using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace DEB.Core.Elementos.Metalica;

/// <summary>Aço estrutural (NBR 8800:2024 Anexo A, Tab. A.1/A.2): resistências em MPa.</summary>
public sealed class AcoEstrutural
{
    public string Nome { get; set; } = "";
    public double Fy { get; set; }
    public double Fu { get; set; }
    public double E { get; set; } = 200000;
    public double G { get; set; } = 77000;

    public static IReadOnlyList<AcoEstrutural> Lista { get; } = new[]
    {
        new AcoEstrutural { Nome = "ASTM A572 Gr. 50", Fy = 345, Fu = 450 },
        new AcoEstrutural { Nome = "ASTM A992", Fy = 345, Fu = 450 },
        new AcoEstrutural { Nome = "ASTM A36", Fy = 250, Fu = 400 },
        new AcoEstrutural { Nome = "NBR 7007 MR250", Fy = 250, Fu = 400 },
        new AcoEstrutural { Nome = "NBR 7007 AR350", Fy = 350, Fu = 450 },
        new AcoEstrutural { Nome = "NBR 7007 AR350 COR", Fy = 350, Fu = 485 },
        new AcoEstrutural { Nome = "NBR 7007 AR415", Fy = 415, Fu = 520 },
        new AcoEstrutural { Nome = "ASTM A588", Fy = 345, Fu = 485 },
    };

    /// <summary>Pelo nome (início; sem acento e caixa): "A572", "MR250", "ASTM A36"… null se não houver.</summary>
    public static AcoEstrutural Buscar(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return null;
        string n = nome.Replace(" ", "").ToUpperInvariant();
        return Lista.FirstOrDefault(a => a.Nome.Replace(" ", "").ToUpperInvariant() == n)
            ?? Lista.FirstOrDefault(a => a.Nome.Replace(" ", "").ToUpperInvariant().Contains(n));
    }
}

/// <summary>
/// Perfil I ou H duplamente simétrico (D48): laminado de abas paralelas (W, HP; com raio de concordância r) ou soldado (CVS, VS,
/// CS; r = 0). Dimensões em mm; propriedades em cm (A cm², I cm⁴, W e Z cm³, Cw cm⁶) calculadas pelo PCEB — inclusive o raio de
/// concordância nas inércias e nos módulos e a constante de torção de El Darwish e Johnston (AISC Design Guide 9, eq. 3.4) para os
/// laminados. Conferido com o catálogo métrico do AISC (testes).
/// </summary>
public sealed class PerfilI
{
    public string Nome { get; set; } = "";
    public double D { get; set; }     // mm
    public double Bf { get; set; }
    public double Tf { get; set; }
    public double Tw { get; set; }
    public double R { get; set; }
    public bool Soldado { get; set; }

    // ---- propriedades (cm)
    public double A { get; private set; }
    public double Ix { get; private set; }
    public double Iy { get; private set; }
    public double Wx { get; private set; }
    public double Wy { get; private set; }
    public double Zx { get; private set; }
    public double Zy { get; private set; }
    public double Rx { get; private set; }
    public double Ry { get; private set; }
    public double J { get; private set; }
    public double Cw { get; private set; }
    /// <summary>Massa linear (kg/m), aço a 7850 kg/m³.</summary>
    public double Massa => A * 0.785;
    /// <summary>Altura plana da alma h (mm): laminado d − 2tf − 2r; soldado d − 2tf (NBR 8800 5.1.2.2.3).</summary>
    public double H => D - 2 * Tf - 2 * R;

    public PerfilI(string nome, double d, double bf, double tf, double tw, double r, bool soldado)
    {
        Nome = nome; D = d; Bf = bf; Tf = tf; Tw = tw; R = soldado ? 0 : r; Soldado = soldado;
        Calcular();
    }

    private void Calcular()
    {
        // em mm; converte no fim
        double d = D, b = Bf, tf = Tf, tw = Tw, r = R, hw = d - 2 * tf;
        double aMesa = b * tf, aAlma = hw * tw;
        // filete (concordância): área (1 − π/4)r², centroide a c = r(10 − 3π)/(12 − 3π) das duas faces retas
        double af = (1 - Math.PI / 4) * r * r, c = r > 0 ? r * (10 - 3 * Math.PI) / (12 - 3 * Math.PI) : 0;
        double iProprio = Math.Max(0, iBordaCentroide(r, af, c));
        double area = 2 * aMesa + aAlma + 4 * af;
        double yF = hw / 2 - c, xF = tw / 2 + c;
        double ix = 2 * (b * Math.Pow(tf, 3) / 12 + aMesa * Math.Pow(d / 2 - tf / 2, 2)) + tw * Math.Pow(hw, 3) / 12 + 4 * (iProprio + af * yF * yF);
        double iy = 2 * (tf * Math.Pow(b, 3) / 12) + hw * Math.Pow(tw, 3) / 12 + 4 * (iProprio + af * xF * xF);
        // módulos plásticos: metade da seção × distância do centroide da metade
        double zx = 2 * (aMesa * (d / 2 - tf / 2) + tw * (hw / 2) * (hw / 4) + 2 * af * yF);
        double zy = 2 * (2 * tf * (b / 2) * (b / 4) + hw * (tw / 2) * (tw / 4) + 2 * af * xF);
        // torção
        double j;
        if (r > 0)
        {
            double alfa = -0.042 + 0.2204 * tw / tf + 0.1355 * r / tf - 0.0865 * r * tw / (tf * tf) - 0.0725 * tw * tw / (tf * tf);
            double dd = (Math.Pow(tf + r, 2) + tw * (r + tw / 4)) / (2 * r + tf);
            j = 2.0 / 3 * b * Math.Pow(tf, 3) + 1.0 / 3 * (d - 2 * tf) * Math.Pow(tw, 3) + 2 * alfa * Math.Pow(dd, 4) - 0.420 * Math.Pow(tf, 4);
        }
        else j = (2 * b * Math.Pow(tf, 3) + (d - tf) * Math.Pow(tw, 3)) / 3;   // soldado (NBR 8800: J = Σ b t³/3, alma até as linhas médias das mesas)
        double cw = iy * Math.Pow(d - tf, 2) / 4;                                 // NBR 8800 D.2.8-a)

        A = area / 100; Ix = ix / 1e4; Iy = iy / 1e4; Wx = Ix / (d / 20); Wy = Iy / (b / 20); Zx = zx / 1e3; Zy = zy / 1e3;
        Rx = Math.Sqrt(Ix / A); Ry = Math.Sqrt(Iy / A); J = j / 1e4; Cw = cw / 1e6;
    }

    /// <summary>Inércia do filete em relação ao próprio centroide, paralela a uma face reta.</summary>
    /// (quadrado r × r menos o quarto de círculo: I em relação a uma face reta = r⁴(1 − 5π/16); menos a·c²).
    private static double iBordaCentroide(double r, double af, double c) => r <= 0 ? 0 : Math.Pow(r, 4) * (1 - 5 * Math.PI / 16) - af * c * c;

    public override string ToString() => Nome;

    // ---------------------------------------------------------------- catálogo e nomes

    private static List<PerfilI> _catalogo;

    /// <summary>Perfis W e HP laminados (dimensões da ASTM A6/A6M; os mesmos da Gerdau).</summary>
    public static IReadOnlyList<PerfilI> Catalogo
    {
        get
        {
            if (_catalogo != null) return _catalogo;
            var l = new List<PerfilI>();
            using Stream s = typeof(PerfilI).GetTypeInfo().Assembly.GetManifestResourceStream("DEB.Core.PerfisLaminados.txt");
            using var rd = new StreamReader(s, Encoding.UTF8);
            string linha;
            while ((linha = rd.ReadLine()) != null)
            {
                if (linha.StartsWith("#") || linha.Trim() == "") continue;
                string[] c = linha.Split(';');
                double N(int i) => double.Parse(c[i], CultureInfo.InvariantCulture);
                l.Add(new PerfilI(c[0], N(1), N(2), N(3), N(4), N(5), false));
            }
            return _catalogo = l;
        }
    }

    private static string Chave(string n) => Regex.Replace((n ?? "").ToUpperInvariant().Replace(',', '.').Replace('×', 'X').Replace('*', 'X'), @"\s", "");

    /// <summary>
    /// Perfil pelo nome: do catálogo ("W310X38.7", "W 310 x 38,7", "HP 250 x 62"), ou soldado pelas dimensões em mm
    /// ("CVS 400x200x9.5x6.3" ou "I 400x200x9,5x6,3" = d × bf × tf × tw). null se não reconhecer.
    /// </summary>
    public static PerfilI Buscar(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return null;
        string k = Chave(nome);
        var cat = Catalogo.FirstOrDefault(p => Chave(p.Nome) == k);
        if (cat != null) return cat;
        // "W310X38.7" escrito como "W 310 X 38.70": tenta pelo número
        var m = Regex.Match(k, @"^(W|HP)(\d+)X(\d+(\.\d+)?)$");
        if (m.Success)
        {
            double massa = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            var c2 = Catalogo.FirstOrDefault(p => p.Nome.StartsWith(m.Groups[1].Value + m.Groups[2].Value + "X") &&
                                                  Math.Abs(double.Parse(p.Nome.Substring(p.Nome.IndexOf('X') + 1), CultureInfo.InvariantCulture) - massa) < 0.06);
            if (c2 != null) return c2;
        }
        var s = Regex.Match(k, @"^(CVS|VS|CS|PS|I|H)(\d+(\.\d+)?)X(\d+(\.\d+)?)X(\d+(\.\d+)?)X(\d+(\.\d+)?)$");
        if (s.Success)
        {
            double V(int g) => double.Parse(s.Groups[g].Value, CultureInfo.InvariantCulture);
            return new PerfilI(nome.Trim(), V(2), V(4), V(6), V(8), 0, true);
        }
        return null;
    }
}
