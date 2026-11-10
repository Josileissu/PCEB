using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Pecas;

/// <summary>
/// Leitura de uma seção de viga como ALMA retangular + ABA lateral (2026-11-09, VP-4): L (aba de um lado), T invertido (dos dois
/// lados) ou aba elevada (consolo corrido acima do fundo), retangular ou com o fundo inclinado (mais alta junto da alma, com o
/// topo plano — as vigas do modelo Nordika). É a forma que o template da viga do CAD desenha; o que não couber nela (I, mesa
/// superior, mísula em toda a altura, duas abas no mesmo lado, abas diferentes dos dois lados) continua "seção que o CAD ainda não desenha".
/// Lados: "esquerda" = y maior (à esquerda de quem olha ao longo do eixo da viga), "direita" = y menor.
/// </summary>
public sealed class SecaoComAba
{
    /// <summary>Alma: largura e posição (y mínimo) no sistema da seção.</summary>
    public double B { get; private set; }
    public double Y0 { get; private set; }
    /// <summary>Altura total e fundo da seção.</summary>
    public double H { get; private set; }
    public double Z0 { get; private set; }
    /// <summary>Balanço da aba de cada lado (0 = sem aba daquele lado).</summary>
    public double AbaEsquerda { get; private set; }
    public double AbaDireita { get; private set; }
    /// <summary>Altura da aba junto da alma e na ponta (iguais = retangular).</summary>
    public double AbaAltura { get; private set; }
    public double AbaAlturaPonta { get; private set; }
    /// <summary>Fundo da aba (junto da alma) acima do fundo da seção.</summary>
    public double AbaBase { get; private set; }
    /// <summary>Do topo da aba ao topo da seção (não muda no dente, onde o fundo da seção sobe).</summary>
    public double DistanciaDoTopo => H - AbaBase - AbaAltura;
    /// <summary>
    /// A "aba" toma TODA a altura da seção (do fundo ao topo): é um alargamento ou uma mísula, não uma aba — o CAD ainda não
    /// desenha. A aba rente ao topo (abas de laje dos dois lados em cima, como as 30x50 do Nordika) é aba e é desenhada.
    /// </summary>
    public bool AlturaToda { get; private set; }

    public bool TemAba => AbaEsquerda > 0 || AbaDireita > 0;
    public double AbaLargura => Math.Max(AbaEsquerda, AbaDireita);

    /// <summary>
    /// Chave da aba ao milímetro, medida a partir do TOPO (trechos com a mesma aba têm a mesma chave, no vão e no dente);
    /// "" = sem aba.
    /// </summary>
    public string Chave => !TemAba ? "" : FormattableString.Invariant(
        $"{M(AbaEsquerda)}|{M(AbaDireita)}|{M(AbaAltura)}|{M(AbaAlturaPonta)}|{M(DistanciaDoTopo)}");

    private static string M(double cm) => Math.Round(cm * 10).ToString("0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Lê a seção; null se ela não é alma retangular com (ou sem) uma aba lateral.</summary>
    public static SecaoComAba Ler(SecaoPoligonal s, double tol = SecaoPoligonal.Tolerancia)
    {
        if (s == null || s.Furos.Count > 0 || s.Contorno.Count < 3) return null;
        double h = s.H, zMin = s.ZMin;
        if (h <= tol) return null;
        // alma = o corte horizontal mais estreito; todos os cortes (um pedaço só) têm de contê-la
        const int n = 60;
        var cortes = new List<(double A, double B)>();
        for (int k = 0; k < n; k++)
        {
            var c = s.CortesHorizontais(zMin + h * (k + 0.5) / n);
            if (c.Count != 2) return null;
            cortes.Add((c[0], c[1]));
        }
        // a alma chega ao topo: é o corte do topo estreitado pelos cortes que a contêm. Embaixo, a seção pode continuar só com a aba
        // (no dente, a aba que desce abaixo do recorte fica pendurada): esses cortes ficam inteiros de um lado da alma.
        var topo = cortes[n - 1];
        var alma = cortes.Where(c => c.A <= topo.A + tol && c.B >= topo.B - tol || (c.A >= topo.A - tol && c.B <= topo.B + tol))
                         .OrderBy(c => c.B - c.A).First();
        bool Contem((double A, double B) c) => c.A <= alma.A + tol && c.B >= alma.B - tol;
        bool DeLado((double A, double B) c) => c.B <= alma.A + tol || c.A >= alma.B - tol;
        if (cortes.Any(c => !Contem(c) && !DeLado(c))) return null;
        int k0 = cortes.FindIndex(Contem);
        if (k0 < 0 || cortes.Skip(k0).Any(c => !Contem(c))) return null;        // a alma é um trecho só, até o topo
        // fundo da alma: a altura dos vértices do contorno na face da alma mais próxima do 1º corte que a contém
        double zAlma = k0 == 0 ? zMin : s.Contorno.Where(p => p.Y >= alma.A - tol && p.Y <= alma.B + tol).Select(p => p.Z)
            .Where(z => z <= zMin + h * (k0 + 0.5) / n + 1e-9).DefaultIfEmpty(zMin).Max();
        double zMinTotal = zMin;
        zMin = zAlma;
        h = s.ZMax - zAlma;
        if (h <= tol) return null;
        var r = new SecaoComAba { B = alma.B - alma.A, Y0 = alma.A, H = h, Z0 = zMin };

        // o que sobra de cada lado da alma
        var dir = Lado(Recortar(s.Contorno, alma.A, menor: true), p => alma.A - p.Y, zMin, tol);
        var esq = Lado(Recortar(s.Contorno, alma.B, menor: false), p => p.Y - alma.B, zMin, tol);
        if (dir.Invalido || esq.Invalido) return null;
        // a alma tem de ser cheia (chanfros de canto passam)
        if (s.Area - dir.Area - esq.Area < 0.97 * r.B * h) return null;
        if (dir.Medidas == null && esq.Medidas == null) return r;
        var m = dir.Medidas ?? esq.Medidas;
        if (dir.Medidas != null && esq.Medidas != null)
        {
            var o = esq.Medidas;
            bool iguais = Math.Abs(m.Balanco - o.Balanco) <= tol && Math.Abs(m.AlturaNaFace - o.AlturaNaFace) <= tol &&
                          Math.Abs(m.AlturaPonta - o.AlturaPonta) <= tol && Math.Abs(m.BaseNaFace - o.BaseNaFace) <= tol;
            if (!iguais) return null;                                        // abas diferentes dos dois lados
        }
        r.AbaDireita = dir.Medidas?.Balanco ?? 0;
        r.AbaEsquerda = esq.Medidas?.Balanco ?? 0;
        r.AbaAltura = m.AlturaNaFace;
        r.AbaAlturaPonta = Math.Min(m.AlturaPonta, m.AlturaNaFace);
        r.AbaBase = m.BaseNaFace;
        r.AlturaToda = r.DistanciaDoTopo <= tol && r.AbaBase <= tol && zMinTotal >= zMin - tol;
        return r;
    }

    /// <summary>
    /// Medidas da saliência de um lado (pontos: distância da face da alma × altura a partir do fundo da seção), como as do
    /// consolo (<see cref="PerfilConsolo"/>: chanfro na ponta não conta). Inválido = não é um trapézio de topo plano (duas abas
    /// no mesmo lado, topo inclinado…).
    /// </summary>
    private static (PerfilConsolo.Medidas Medidas, double Area, bool Invalido) Lado(List<(double Y, double Z)> pol, Func<(double Y, double Z), double> dist,
        double zMin, double tol)
    {
        double area = Math.Abs(AreaDe(pol));
        if (pol.Count < 3 || area < 1) return (null, 0, false);
        var m = PerfilConsolo.Medir(pol.Select(p => (dist(p), p.Z - zMin)));
        if (m.Balanco <= tol) return (null, area, false);
        double trapezio = m.Balanco * (m.AlturaNaFace + m.AlturaPonta) / 2;
        bool topoPlano = pol.Max(p => p.Z) - zMin <= m.TopoNaFace + tol && m.AlturaPonta <= m.AlturaNaFace + tol;
        bool ok = topoPlano && m.AlturaPonta > 0 && Math.Abs(area - trapezio) <= Math.Max(0.04 * trapezio, 2);
        return (m, area, !ok);
    }

    private static double AreaDe(List<(double Y, double Z)> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count; i++) { var (y0, z0) = p[i]; var (y1, z1) = p[(i + 1) % p.Count]; a += y0 * z1 - y1 * z0; }
        return a / 2;
    }

    /// <summary>A parte do polígono com y ≤ limite (<paramref name="menor"/>) ou y ≥ limite (Sutherland–Hodgman).</summary>
    private static List<(double Y, double Z)> Recortar(List<(double Y, double Z)> p, double limite, bool menor)
    {
        bool Dentro((double Y, double Z) q) => menor ? q.Y <= limite + 1e-9 : q.Y >= limite - 1e-9;
        var r = new List<(double Y, double Z)>();
        for (int i = 0; i < p.Count; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Count];
            bool da = Dentro(a), db = Dentro(b);
            if (da) r.Add(a);
            if (da != db && Math.Abs(b.Y - a.Y) > 1e-12)
            {
                double t = (limite - a.Y) / (b.Y - a.Y);
                r.Add((limite, a.Z + t * (b.Z - a.Z)));
            }
        }
        // o recorte deixa "espinhos" sem área ao longo da face da alma (idas e voltas sobre a linha do limite): tira os vértices
        // repetidos e os colineares até sobrar só o contorno da saliência
        bool mudou = true;
        while (mudou && r.Count >= 3)
        {
            mudou = false;
            for (int i = 0; i < r.Count && r.Count >= 3; i++)
            {
                var (ay, az) = r[(i + r.Count - 1) % r.Count];
                var (by, bz) = r[i];
                var (cy, cz) = r[(i + 1) % r.Count];
                bool repetido = Math.Abs(by - ay) < 1e-9 && Math.Abs(bz - az) < 1e-9;
                if (repetido || Math.Abs((by - ay) * (cz - az) - (bz - az) * (cy - ay)) < 1e-9) { r.RemoveAt(i); i--; mudou = true; }
            }
        }
        return r;
    }
}
