using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Templates;

namespace DEB.Core.Motor.Prancha;

/// <summary>Retângulo envolvente (cm, coordenadas da folha).</summary>
public sealed class Retangulo
{
    public double X0 { get; private set; } = double.MaxValue;
    public double Y0 { get; private set; } = double.MaxValue;
    public double X1 { get; private set; } = double.MinValue;
    public double Y1 { get; private set; } = double.MinValue;
    public bool Vazio => X0 > X1 || Y0 > Y1;
    public double Largura => Vazio ? 0 : X1 - X0;
    public double Altura => Vazio ? 0 : Y1 - Y0;

    public void Incluir(double x, double y)
    {
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y)) return;
        X0 = Math.Min(X0, x); Y0 = Math.Min(Y0, y); X1 = Math.Max(X1, x); Y1 = Math.Max(Y1, y);
    }

    public void Incluir(Retangulo o)
    {
        if (o == null || o.Vazio) return;
        Incluir(o.X0, o.Y0); Incluir(o.X1, o.Y1);
    }

    public void Expandir(double d) { if (!Vazio) { X0 -= d; Y0 -= d; X1 += d; Y1 += d; } }

    public void Deslocar(double dx, double dy) { if (!Vazio) { X0 += dx; X1 += dx; Y0 += dy; Y1 += dy; } }

    /// <summary>Os interiores se cruzam (encostar não conta).</summary>
    public bool Intersecta(Retangulo o) => !Vazio && !o.Vazio && o.X0 < X1 && o.X1 > X0 && o.Y0 < Y1 && o.Y1 > Y0;

    public bool Contem(double x, double y, double tol = 1e-6) => x >= X0 - tol && x <= X1 + tol && y >= Y0 - tol && y <= Y1 + tol;

    public override string ToString() => FormattableString.Invariant($"[{X0:0.#}; {Y0:0.#}] – [{X1:0.#}; {Y1:0.#}]");
}

/// <summary>Plano de uma barra (base u, v para desenhá-la em verdadeira grandeza).</summary>
internal sealed class PlanoBarra
{
    /// <summary>Barra reta (todos os segmentos na mesma direção <see cref="D"/>).</summary>
    public bool Reta;
    /// <summary>false = barra não plana (ex.: grampo com as pernas dobradas de lado): N é o plano com mais comprimento,
    /// mas nenhuma vista a mostra em verdadeira grandeza; a vista de barras a desenha planificada (<see cref="Geo.Desenvolver"/>).</summary>
    public bool Plana = true;
    public P3 D, N, U, V;
    public (double X, double Y) Em2D(P3 p) => (Geo.Dot(p, U), Geo.Dot(p, V));
}

internal static class Geo
{
    public static readonly P3 Ex = new P3(1, 0, 0), Ey = new P3(0, 1, 0), Ez = new P3(0, 0, 1);

    public static double Dot(P3 a, P3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static P3 Cross(P3 a, P3 b) => new P3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public static P3 Unit(P3 a) { double n = a.Comprimento; return n < 1e-12 ? new P3(0, 0, 0) : a * (1 / n); }

    /// <summary>Vetor com o primeiro componente não nulo positivo (para as barras dobradas não saírem espelhadas ao acaso).</summary>
    public static P3 Positivo(P3 u) =>
        u.X > 1e-6 || (Math.Abs(u.X) <= 1e-6 && (u.Y > 1e-6 || (Math.Abs(u.Y) <= 1e-6 && u.Z >= 0))) ? u : u * -1;

    /// <summary><see cref="Arcos.Amostrar"/>.</summary>
    public static List<P3> Amostrar(IReadOnlyList<P3> v, IReadOnlyList<double> bulges, P3 normal, bool fechada, int porArco = 16) =>
        Arcos.Amostrar(v, bulges, normal, fechada, porArco);

    /// <summary>
    /// Bulges da barra numa vista de eixos (<paramref name="u"/>, <paramref name="v"/>): só quando o plano dos arcos é o da
    /// vista (verdadeira grandeza); o sinal acompanha o sentido da vista. null = a vista não mostra os arcos em verdadeira
    /// grandeza (use <see cref="Amostrar"/>).
    /// </summary>
    public static List<double> BulgesNaVista(PosicaoArmadura p, P3 u, P3 v)
    {
        if (p == null || !p.TemArcos) return null;
        double s = Dot(Unit(p.NormalArcos), Unit(Cross(u, v)));
        if (Math.Abs(s) < 0.999) return null;
        return p.Bulges.Select(b => s > 0 ? b : -b).ToList();
    }

    /// <summary>Plano da barra, considerando os arcos (o plano deles manda).</summary>
    public static PlanoBarra Plano(PosicaoArmadura p) =>
        p.TemArcos ? PlanoDaNormal(Unit(p.NormalArcos)) : Plano(p.Vertices);

    /// <summary>Plano da barra; null = sem geometria.</summary>
    public static PlanoBarra Plano(IReadOnlyList<P3> v)
    {
        var dirs = new List<P3>();
        for (int i = 0; i + 1 < v.Count; i++) { var d = v[i + 1] - v[i]; if (d.Comprimento > 1e-6) dirs.Add(Unit(d)); }
        if (dirs.Count == 0) return null;
        P3? primeira = null;
        for (int i = 0; i < dirs.Count && primeira == null; i++)
            for (int j = i + 1; j < dirs.Count; j++)
            {
                var cr = Cross(dirs[i], dirs[j]);
                if (cr.Comprimento > 1e-3) { primeira = Unit(cr); break; }
            }
        if (primeira is P3 n0)
        {
            if (dirs.All(d => Math.Abs(Dot(d, n0)) < 1e-3)) return PlanoDaNormal(n0);
            var pn = PlanoDaNormal(PlanoPrincipal(v) ?? n0);
            pn.Plana = false;
            return pn;
        }
        var D = Positivo(dirs[0]);
        var p = new PlanoBarra { Reta = true, D = D };
        if (Math.Abs(D.Z) > 0.999) { p.U = Ex; p.V = Ez; p.N = Ey; }
        else
        {
            p.U = D;
            p.N = Unit(Cross(D, Ez));
            p.V = Unit(Cross(p.N, D));
            if (p.V.Z < 0) p.V = p.V * -1;
        }
        return p;
    }

    public static PlanoBarra PlanoDaNormal(P3 n)
    {
        var p = new PlanoBarra { N = n };
        if (Math.Abs(n.Z) > 0.999) { p.U = Ex; p.V = Ey; }
        else if (Math.Abs(n.Z) < 1e-3) { p.V = Ez; p.U = Positivo(Unit(Cross(Ez, n))); }
        else
        {
            p.U = Positivo(Unit(Cross(Ez, n)));
            p.V = Unit(Cross(n, p.U));
            if (p.V.Z < 0) p.V = p.V * -1;
        }
        return p;
    }

    /// <summary>
    /// Plano principal de uma barra não plana: o de dois trechos seguidos em que só as PONTAS ficam de fora (para poder
    /// planificar girando as pontas), com o maior comprimento; sem nenhum assim, o de maior comprimento. null = reta.
    /// </summary>
    public static P3? PlanoPrincipal(IReadOnlyList<P3> v)
    {
        int n = v.Count - 1;
        var d = new List<P3>(); var len = new List<double>();
        for (int k = 0; k < n; k++) { var a = v[k + 1] - v[k]; len.Add(a.Comprimento); d.Add(Unit(a)); }
        P3? melhor = null; double score = -1; bool melhorValido = false;
        for (int k = 0; k + 1 < n; k++)
        {
            var cr = Cross(d[k], d[k + 1]);
            if (cr.Comprimento < 1e-3) continue;
            var nn = Unit(cr);
            var dentro = Enumerable.Range(0, n).Select(j => len[j] < 1e-9 || Math.Abs(Dot(d[j], nn)) < 1e-3).ToArray();
            int ini = Array.IndexOf(dentro, true), fim = Array.LastIndexOf(dentro, true);
            bool valido = ini >= 0 && Enumerable.Range(ini, fim - ini + 1).All(j => dentro[j]);
            double sc = Enumerable.Range(0, n).Where(j => dentro[j]).Sum(j => len[j]);
            if ((valido && !melhorValido) || (valido == melhorValido && sc > score + 1e-9)) { melhor = nn; score = sc; melhorValido = valido; }
        }
        return melhor;
    }

    /// <summary>
    /// Forma da barra em 2D para a vista de barras (verdadeira grandeza). Barra plana: a projeção no plano dela. Barra NÃO
    /// plana (ex.: grampo vertical do cálice, com as pernas dobradas de lado): planifica no <see cref="PlanoPrincipal"/> e
    /// gira para dentro dele os trechos das pontas que saem do plano, mantendo o comprimento e o ângulo de dobra, com a ponta
    /// livre para fora (como o desenho do escritório). Trecho fora do plano no meio da barra: projetado.
    /// </summary>
    public static List<(double X, double Y)> Desenvolver(IReadOnlyList<P3> v, PlanoBarra plano)
    {
        if (plano == null) return v.Select(q => (q.X, q.Y)).ToList();
        var proj = v.Select(q => plano.Em2D(q)).ToList();
        if (plano.Reta || v.Count < 3) return proj;
        int n = v.Count - 1;
        var d = new P3[n]; var len = new double[n];
        for (int k = 0; k < n; k++) { len[k] = (v[k + 1] - v[k]).Comprimento; d[k] = Unit(v[k + 1] - v[k]); }
        bool NoPlano(P3 u, P3 nn) => Math.Abs(Dot(u, nn)) < 1e-3;
        if (Enumerable.Range(0, n).All(k => len[k] < 1e-9 || NoPlano(d[k], plano.N))) return proj;

        var melhor = PlanoPrincipal(v) ?? plano.N;
        var pl = PlanoDaNormal(melhor);
        var dentro = Enumerable.Range(0, n).Select(k => len[k] < 1e-9 || NoPlano(d[k], melhor)).ToArray();
        var dir = new (double X, double Y)[n];
        for (int k = 0; k < n; k++) dir[k] = pl.Em2D(d[k]);
        var pontosDentro = Enumerable.Range(0, n).Where(k => dentro[k]).SelectMany(k => new[] { pl.Em2D(v[k]), pl.Em2D(v[k + 1]) }).ToList();
        if (pontosDentro.Count == 0) return proj;
        var cen = Geometria2D.Centro(pontosDentro);
        (double X, double Y) Girar((double X, double Y) a, double t) => (a.X * Math.Cos(t) - a.Y * Math.Sin(t), a.X * Math.Sin(t) + a.Y * Math.Cos(t));
        double Dist2((double X, double Y) a) => (a.X - cen.X) * (a.X - cen.X) + (a.Y - cen.Y) * (a.Y - cen.Y);

        // pontas fora do plano: giradas para dentro dele (início: em relação ao trecho seguinte; fim: ao anterior)
        int ini = 0; while (ini < n && !dentro[ini]) ini++;
        int fim = n - 1; while (fim >= 0 && !dentro[fim]) fim--;
        if (ini > fim) return proj;
        var pos = new (double X, double Y)[v.Count];
        for (int k = ini; k <= fim + 1; k++) pos[k] = pl.Em2D(v[k]);
        for (int k = ini + 1; k <= fim; k++)   // trechos do meio fora do plano: projetados (posições já são as projeções)
            if (!dentro[k]) dir[k] = (pos[k + 1].X - pos[k].X, pos[k + 1].Y - pos[k].Y);
        for (int k = ini - 1; k >= 0; k--)
        {
            double th = Math.Acos(Math.Max(-1, Math.Min(1, Dot(d[k], d[k + 1]))));
            var ref_ = dir[k + 1];
            var a = Girar(ref_, th); var b = Girar(ref_, -th);
            var pa = (pos[k + 1].X - len[k] * a.X, pos[k + 1].Y - len[k] * a.Y);
            var pb = (pos[k + 1].X - len[k] * b.X, pos[k + 1].Y - len[k] * b.Y);
            bool usaA = Dist2(pa) >= Dist2(pb);
            dir[k] = usaA ? a : b; pos[k] = usaA ? pa : pb;
        }
        for (int k = fim + 1; k < n; k++)
        {
            double th = Math.Acos(Math.Max(-1, Math.Min(1, Dot(d[k - 1], d[k]))));
            var ref_ = dir[k - 1];
            var a = Girar(ref_, th); var b = Girar(ref_, -th);
            var pa = (pos[k].X + len[k] * a.X, pos[k].Y + len[k] * a.Y);
            var pb = (pos[k].X + len[k] * b.X, pos[k].Y + len[k] * b.Y);
            bool usaA = Dist2(pa) >= Dist2(pb);
            dir[k] = usaA ? a : b; pos[k + 1] = usaA ? pa : pb;
        }
        return pos.ToList();
    }

    /// <summary>A barra aparece em verdadeira grandeza numa vista de normal <paramref name="vn"/>.</summary>
    public static bool VerdadeiraGrandeza(PlanoBarra p, P3 vn) =>
        p != null && (p.Reta ? Math.Abs(Dot(p.D, vn)) < 1e-3 : p.Plana && Cross(p.N, vn).Comprimento < 1e-3);

    /// <summary>
    /// Contorno da união dos retângulos cheios menos os vazios (s × z), como laços fechados anti-horários. Serve para o corte
    /// de corpos extrudados: cada corpo cortado vira um retângulo; o contorno sai sem as linhas internas entre corpos.
    /// </summary>
    public static List<List<(double X, double Y)>> ContornoUniao(IReadOnlyList<(double S0, double S1, double Z0, double Z1, bool Vazio)> rs)
    {
        var laços = new List<List<(double X, double Y)>>();
        var xs = rs.SelectMany(r => new[] { r.S0, r.S1 }).Select(v => Math.Round(v, 4)).Distinct().OrderBy(v => v).ToList();
        var zs = rs.SelectMany(r => new[] { r.Z0, r.Z1 }).Select(v => Math.Round(v, 4)).Distinct().OrderBy(v => v).ToList();
        int nx = xs.Count - 1, nz = zs.Count - 1;
        if (nx < 1 || nz < 1) return laços;
        var f = new bool[nx, nz];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                double cx = (xs[i] + xs[i + 1]) / 2, cz = (zs[j] + zs[j + 1]) / 2;
                bool Dentro((double S0, double S1, double Z0, double Z1, bool Vazio) r) =>
                    cx > Math.Min(r.S0, r.S1) && cx < Math.Max(r.S0, r.S1) && cz > Math.Min(r.Z0, r.Z1) && cz < Math.Max(r.Z0, r.Z1);
                f[i, j] = rs.Any(r => !r.Vazio && Dentro(r)) && !rs.Any(r => r.Vazio && Dentro(r));
            }
        bool F(int i, int j) => i >= 0 && j >= 0 && i < nx && j < nz && f[i, j];
        var arestas = new List<((int I, int J) A, (int I, int J) B)>();
        for (int j = 0; j <= nz; j++)
            for (int i = 0; i < nx; i++)
            {
                bool baixo = F(i, j - 1), cima = F(i, j);
                if (baixo && !cima) arestas.Add(((i + 1, j), (i, j)));
                else if (!baixo && cima) arestas.Add(((i, j), (i + 1, j)));
            }
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j < nz; j++)
            {
                bool esq = F(i - 1, j), dir = F(i, j);
                if (esq && !dir) arestas.Add(((i, j), (i, j + 1)));
                else if (!esq && dir) arestas.Add(((i, j + 1), (i, j)));
            }
        var porInicio = arestas.GroupBy(a => a.A).ToDictionary(g => g.Key, g => g.ToList());
        var usadas = new HashSet<((int, int), (int, int))>();
        foreach (var e in arestas)
        {
            if (usadas.Contains(e)) continue;
            var pts = new List<(int I, int J)> { e.A };
            usadas.Add(e);
            var cur = e;
            int guarda = 0;
            while (cur.B != e.A && guarda++ < 100000)
            {
                pts.Add(cur.B);
                if (!porInicio.TryGetValue(cur.B, out var cands)) break;
                int k = cands.FindIndex(x => !usadas.Contains(x));
                if (k < 0) break;
                var prox = cands[k];
                usadas.Add(prox);
                cur = prox;
            }
            var laço = pts.Select(p => (xs[p.I], zs[p.J])).ToList();
            laço = SemColineares(laço);
            if (laço.Count >= 3) laços.Add(laço);
        }
        return laços;
    }

    private static List<(double X, double Y)> SemColineares(List<(double X, double Y)> p)
    {
        bool mudou = true;
        while (mudou && p.Count > 3)
        {
            mudou = false;
            for (int k = 0; k < p.Count; k++)
            {
                var a = p[(k - 1 + p.Count) % p.Count]; var b = p[k]; var c = p[(k + 1) % p.Count];
                double cr = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
                if (Math.Abs(cr) < 1e-9) { p.RemoveAt(k); mudou = true; break; }
            }
        }
        return p;
    }

    /// <summary>Trechos [a, b] em que a reta n = c (n = coordenada normal ao corte, s = ao longo) atravessa o polígono.</summary>
    public static List<(double A, double B)> Intervalos(IReadOnlyList<(double X, double Y)> poly, Func<(double X, double Y), double> n,
        Func<(double X, double Y), double> s, double c)
    {
        var cortes = new List<double>();
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i]; var q = poly[(i + 1) % poly.Count];
            double na = n(p) - c, nb = n(q) - c;
            if ((na <= 0 && nb > 0) || (nb <= 0 && na > 0))
            {
                double t = na / (na - nb);
                cortes.Add(s(p) + t * (s(q) - s(p)));
            }
        }
        cortes.Sort();
        var r = new List<(double, double)>();
        for (int i = 0; i + 1 < cortes.Count; i += 2) if (cortes[i + 1] - cortes[i] > 1e-6) r.Add((cortes[i], cortes[i + 1]));
        return r;
    }

    // ---------------- limites e deslocamento de um resultado ----------------

    /// <summary>Retângulo de um texto (largura estimada 0,8·h por caractere), girado em volta do ponto de inserção.</summary>
    public static void IncluirTexto(Retangulo r, string s, double x, double y, double h, int al, double ang)
    {
        int n = (s ?? "").Split('\n').Max(l => l.Length);
        double w = 0.8 * h * Math.Max(1, n);
        double dx0 = al == 1 ? -w / 2 : al == 2 ? -w : 0;
        double c = Math.Cos(ang), si = Math.Sin(ang);
        foreach (var (u, v) in new[] { (dx0, 0.0), (dx0 + w, 0.0), (dx0, h), (dx0 + w, h) })
            r.Incluir(x + u * c - v * si, y + u * si + v * c);
    }

    public static Retangulo Limites(ResultadoTemplate r, double ht)
    {
        var b = new Retangulo();
        foreach (var c in r.Contornos) foreach (var (x, y) in c.Vertices) b.Incluir(x, y);
        foreach (var c in r.Circulos) { b.Incluir(c.X - c.Raio, c.Y - c.Raio); b.Incluir(c.X + c.Raio, c.Y + c.Raio); }
        foreach (var h in r.Hachuras) foreach (var (x, y) in h.Vertices) b.Incluir(x, y);
        foreach (var t in r.Textos) IncluirTexto(b, t.Texto, t.X, t.Y, t.Altura, t.Alinhamento, t.Angulo);
        foreach (var c in r.Cotas)
        {
            b.Incluir(c.P1.X, c.P1.Y); b.Incluir(c.P2.X, c.P2.Y);
            b.Incluir(c.Posicao.X - ht, c.Posicao.Y - ht); b.Incluir(c.Posicao.X + ht, c.Posicao.Y + ht);
        }
        foreach (var f in r.Faixas)
        {
            b.Incluir(f.P1.X, f.P1.Y); b.Incluir(f.P2.X, f.P2.Y);
            foreach (var (x, y) in f.Referencias) b.Incluir(x, y);
            double w = f.Tipo == Modelo.TipoFaixa.Indicacao ? 10 * ht : 5 * ht;
            b.Incluir(f.Posicao.X - (f.Tipo == Modelo.TipoFaixa.Indicacao ? 0 : w), f.Posicao.Y - 1.5 * ht);
            b.Incluir(f.Posicao.X + w, f.Posicao.Y + 1.5 * ht);
        }
        foreach (var br in r.Barras)
        {
            foreach (var (x, y) in br.Vertices) b.Incluir(x, y);
            if (br.PosicaoDescricao is { } d)
                IncluirTexto(b, new string('x', 16), d.X, d.Y - 0.5 * ht, ht, 1, br.RotacaoDescricao);
        }
        foreach (var t in r.Tabelas)
        {
            double w = (t.Larguras.Count > 0 ? t.Larguras.Sum() : 6 * Math.Max(1, t.Linhas.Count > 0 ? t.Linhas.Max(l => l.Length) : 1)) * ht;
            b.Incluir(t.X, t.Y); b.Incluir(t.X + w, t.Y - t.AlturaEmTextos() * ht);
        }
        return b;
    }

    public static void Deslocar(ResultadoTemplate r, double dx, double dy)
    {
        (double X, double Y) M((double X, double Y) p) => (p.X + dx, p.Y + dy);
        foreach (var c in r.Contornos) for (int i = 0; i < c.Vertices.Count; i++) c.Vertices[i] = M(c.Vertices[i]);
        foreach (var c in r.Circulos) { c.X += dx; c.Y += dy; }
        foreach (var h in r.Hachuras) for (int i = 0; i < h.Vertices.Count; i++) h.Vertices[i] = M(h.Vertices[i]);
        foreach (var t in r.Textos) { t.X += dx; t.Y += dy; }
        foreach (var c in r.Cotas) { c.P1 = M(c.P1); c.P2 = M(c.P2); c.Posicao = M(c.Posicao); }
        foreach (var f in r.Faixas)
        {
            f.P1 = M(f.P1); f.P2 = M(f.P2); f.Posicao = M(f.Posicao);
            for (int i = 0; i < f.Referencias.Count; i++) f.Referencias[i] = M(f.Referencias[i]);
        }
        foreach (var b in r.Barras)
        {
            for (int i = 0; i < b.Vertices.Count; i++) b.Vertices[i] = M(b.Vertices[i]);
            if (b.PosicaoDescricao is { } d) b.PosicaoDescricao = M(d);
        }
        foreach (var t in r.Tabelas) { t.X += dx; t.Y += dy; }
        if (r.PosicaoRA is { } ra) r.PosicaoRA = M(ra);
    }

    /// <summary>Junta <paramref name="de"/> em <paramref name="em"/> (as listas, não os dados do detalhamento).</summary>
    public static void Juntar(ResultadoTemplate em, ResultadoTemplate de)
    {
        em.Hachuras.AddRange(de.Hachuras); em.Barras.AddRange(de.Barras); em.Contornos.AddRange(de.Contornos);
        em.Faixas.AddRange(de.Faixas); em.Textos.AddRange(de.Textos); em.Circulos.AddRange(de.Circulos);
        em.Cotas.AddRange(de.Cotas); em.Tabelas.AddRange(de.Tabelas); em.Avisos.AddRange(de.Avisos);
    }
}
