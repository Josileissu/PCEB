using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Pecas;

/// <summary>
/// Leitura de uma seção de viga como ALMA retangular + ABA lateral (2026-11-09, VP-4): L (aba de um lado), T invertido (dos dois
/// lados) ou aba elevada (consolo corrido acima do fundo), retangular ou com o fundo inclinado (mais alta junto da alma, com o
/// topo plano — as vigas de um modelo real). É a forma que o template da viga do CAD desenha; o que não couber nela (I, mesa
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
    /// desenha. A aba rente ao topo (abas de laje dos dois lados em cima, como as 30x50 do modelo real) é aba e é desenhada.
    /// </summary>
    public bool AlturaToda { get; private set; }

    public bool TemAba => AbaEsquerda > 0 || AbaDireita > 0;
    public double AbaLargura => Math.Max(AbaEsquerda, AbaDireita);

    /// <summary>
    /// Ressalto sobre a alma (2026-11-10): faixa estreita acima do topo da alma, rente a uma das faces dela — o "aba laje + aba
    /// superior" de um modelo real (a capa da laje encosta nele). 0 = sem ressalto. <see cref="H"/> é a altura da ALMA, sem ele.
    /// </summary>
    public double RessaltoLargura { get; private set; }
    public double RessaltoAltura { get; private set; }
    /// <summary>Rente à face de y maior (esquerda) ou de y menor (direita).</summary>
    public bool RessaltoEsquerda { get; private set; }
    public bool TemRessalto => RessaltoLargura > 0 && RessaltoAltura > 0;

    /// <summary>
    /// Chave da aba ao milímetro, medida a partir do TOPO (trechos com a mesma aba têm a mesma chave, no vão e no dente);
    /// "" = sem aba.
    /// </summary>
    public string Chave => (!TemAba ? "" : FormattableString.Invariant(
        $"{M(AbaEsquerda)}|{M(AbaDireita)}|{M(AbaAltura)}|{M(AbaAlturaPonta)}|{M(DistanciaDoTopo)}")) +
        (TemRessalto ? FormattableString.Invariant($"|R{(RessaltoEsquerda ? "E" : "D")}{M(RessaltoLargura)}x{M(RessaltoAltura)}") : "");

    /// <summary>Diferença de largura (cm) que ainda conta como a mesma face: modelos reais têm degraus de 1 cm entre famílias.</summary>
    public const double FolgaFace = 1.5;

    private static string M(double cm) => Fmt.Mm(cm);

    /// <summary>Lê a seção; null se ela não é alma retangular com (ou sem) uma aba lateral e (ou sem) um ressalto em cima.</summary>
    public static SecaoComAba Ler(SecaoPoligonal s, double tol = SecaoPoligonal.Tolerancia) => LerSimples(s, tol) ?? LerComRessalto(s, tol);

    /// <summary>
    /// As leituras possíveis da seção (2026-11-10): a mesma forma às vezes cabe nas duas — no dente de uma viga de 20 com rebaixo
    /// de 10 × 15 no topo, "alma 10 + aba 10 × 35" ou "alma 20 + ressalto 10 × 15". Quem lê os trechos de uma peça escolhe a que
    /// tem a mesma alma do trecho corrente.
    /// </summary>
    public static IEnumerable<SecaoComAba> Leituras(SecaoPoligonal s, double tol = SecaoPoligonal.Tolerancia)
    {
        var a = LerSimples(s, tol);
        if (a != null) yield return a;
        var b = LerComRessalto(s, tol);
        if (b != null) yield return b;
    }

    /// <summary>
    /// Seção com uma faixa mais estreita no topo (2026-11-10): separa a faixa de cima (o ressalto) e lê o que fica embaixo como
    /// alma + aba. Faixa de cima tão larga quanto a alma (até <see cref="FolgaFace"/>) = é a própria alma continuando (degrau de
    /// modelagem: a viga de escada do modelo real tem a alma 1 cm mais larga acima da aba). Faixa mais estreita rente a uma face
    /// da alma = ressalto. No meio da alma ou em mais de um degrau = null.
    /// </summary>
    private static SecaoComAba LerComRessalto(SecaoPoligonal s, double tol)
    {
        if (s == null || s.Furos.Count > 0 || s.Contorno.Count < 3) return null;
        double h = s.H, zMin = s.ZMin;
        const int n = 80;
        var cortes = new List<(double A, double B)>();
        for (int k = 0; k < n; k++)
        {
            var c = s.CortesHorizontais(zMin + h * (k + 0.5) / n);
            if (c.Count != 2) return null;
            cortes.Add((c[0], c[1]));
        }
        var topo = cortes[n - 1];
        int k0 = n - 1;
        while (k0 > 0 && Math.Abs(cortes[k0 - 1].A - topo.A) <= tol && Math.Abs(cortes[k0 - 1].B - topo.B) <= tol) k0--;
        if (k0 == 0) return null;
        var abaixo = cortes[k0 - 1];
        if (abaixo.A > topo.A + tol || abaixo.B < topo.B - tol || abaixo.B - abaixo.A <= topo.B - topo.A + tol) return null;
        // base da faixa de cima: os vértices da face dela que não continua para baixo
        double borda = Math.Abs(topo.B - abaixo.B) > tol ? topo.B : topo.A;
        var naBorda = s.Contorno.Where(p => Math.Abs(p.Y - borda) <= tol).Select(p => p.Z).ToList();
        if (naBorda.Count == 0) return null;
        double zBase = naBorda.Min();
        if (zBase <= zMin + tol || zBase >= s.ZMax - tol) return null;
        var embaixo = new SecaoPoligonal { Contorno = CortarAcima(s.Contorno, zBase) };
        if (embaixo.Contorno.Count < 3) return null;
        var b = LerSimples(embaixo, tol);
        if (b == null || b.AlturaToda) return null;
        double hr = s.ZMax - zBase, br = topo.B - topo.A;
        var r = (SecaoComAba)b.MemberwiseClone();
        if (br >= b.B - FolgaFace && topo.A <= b.Y0 + FolgaFace && topo.B >= b.Y0 + b.B - FolgaFace)
        {
            r.H = b.H + hr;   // a alma continua (degrau de modelagem): a aba fica mais longe do topo
            return r;
        }
        bool direita = Math.Abs(topo.A - b.Y0) <= tol, esquerda = Math.Abs(topo.B - (b.Y0 + b.B)) <= tol;
        if (direita == esquerda) return null;                                 // no meio da alma (ou da largura toda)
        r.RessaltoLargura = br;
        r.RessaltoAltura = hr;
        r.RessaltoEsquerda = esquerda;
        return r;
    }

    /// <summary>A parte do polígono com z ≤ <paramref name="z"/> (Sutherland–Hodgman), sem vértices repetidos ou colineares.</summary>
    private static List<(double Y, double Z)> CortarAcima(List<(double Y, double Z)> p, double z)
    {
        var r = new List<(double Y, double Z)>();
        for (int i = 0; i < p.Count; i++)
        {
            var a = p[i];
            var c = p[(i + 1) % p.Count];
            bool da = a.Z <= z + 1e-9, dc = c.Z <= z + 1e-9;
            if (da) r.Add(a);
            if (da != dc && Math.Abs(c.Z - a.Z) > 1e-12) r.Add((a.Y + (z - a.Z) / (c.Z - a.Z) * (c.Y - a.Y), z));
        }
        return Limpar(r);
    }

    private static SecaoComAba LerSimples(SecaoPoligonal s, double tol)
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
        // "aba" com mais de 3/4 da altura = a viga cheia com um rebaixo no topo (2026-11-10, viga de 20 × 150 com recorte de 10 × 15
        // lida como alma de 10 com aba de 135): não é aba
        if (!r.AlturaToda && r.AbaAltura > 0.75 * r.H) return null;
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
        return Limpar(r);
    }

    /// <summary>
    /// Tira os "espinhos" sem área que os recortes deixam (idas e voltas sobre a linha do limite): vértices repetidos e colineares,
    /// até sobrar só o contorno.
    /// </summary>
    private static List<(double Y, double Z)> Limpar(List<(double Y, double Z)> r)
    {
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
