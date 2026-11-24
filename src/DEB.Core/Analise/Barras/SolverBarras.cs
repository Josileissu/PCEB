using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Analise.Barras;

/// <summary>Resultado de um caso (ou de uma combinação): deslocamentos, reações, forças de extremidade das barras e das molas.</summary>
public sealed class ResultadoCaso
{
    public string Nome { get; set; } = "";
    /// <summary>Deslocamentos nodais globais (cm, rad): [nó][gdl].</summary>
    public double[][] Deslocamentos { get; set; }
    /// <summary>Reações nos nós com apoio (kN, kN·cm, globais): [nó][gdl]; nós sem apoio = null.</summary>
    public double[][] Reacoes { get; set; }
    /// <summary>
    /// Forças de extremidade de cada barra, sobre a barra, no sistema LOCAL: N, Vy, Vz, T, My, Mz no nó I (0–5) e no nó J (6–11)
    /// — a convenção do OpenSees ("localForce").
    /// </summary>
    public double[][] ForcasBarras { get; set; }
    /// <summary>Forças nas molas, nos eixos da mola: o que o nó J recebe da mola é −F; F = k·(uJ − uI).</summary>
    public double[][] ForcasMolas { get; set; }
    /// <summary>Deslocamentos das pontas de cada barra no sistema LOCAL (12), com os graus das rótulas já recuperados.</summary>
    public double[][] DeslocBarras { get; set; }

    /// <summary>Soma linear de resultados (superposição): Σ fator·caso.</summary>
    public static ResultadoCaso Combinar(string nome, IEnumerable<(ResultadoCaso R, double Fator)> partes)
    {
        var l = partes.ToList();
        ResultadoCaso b = l[0].R;
        double[][] Z(double[][] m) => m.Select(v => v == null ? null : new double[v.Length]).ToArray();
        var r = new ResultadoCaso
        {
            Nome = nome, Deslocamentos = Z(b.Deslocamentos), Reacoes = Z(b.Reacoes), ForcasBarras = Z(b.ForcasBarras), ForcasMolas = Z(b.ForcasMolas),
            DeslocBarras = b.DeslocBarras == null ? null : Z(b.DeslocBarras),
        };
        void Soma(double[][] dst, double[][] src, double f) { for (int i = 0; i < dst.Length; i++) if (dst[i] != null) for (int k = 0; k < dst[i].Length; k++) dst[i][k] += f * src[i][k]; }
        foreach (var (R, f) in l)
        {
            Soma(r.Deslocamentos, R.Deslocamentos, f); Soma(r.Reacoes, R.Reacoes, f);
            Soma(r.ForcasBarras, R.ForcasBarras, f); Soma(r.ForcasMolas, R.ForcasMolas, f);
            if (r.DeslocBarras != null && R.DeslocBarras != null) Soma(r.DeslocBarras, R.DeslocBarras, f);
        }
        return r;
    }
}

/// <summary>Esforços internos numa seção da barra (sistema local): N (tração +), Vy, Vz, T, My, Mz — o que a parte de depois da seção faz na de antes.</summary>
public readonly struct EsforcosSecao
{
    public double N { get; }
    public double Vy { get; }
    public double Vz { get; }
    public double T { get; }
    public double My { get; }
    public double Mz { get; }
    public EsforcosSecao(double n, double vy, double vz, double t, double my, double mz) { N = n; Vy = vy; Vz = vz; T = t; My = my; Mz = mz; }
}

/// <summary>Falha da análise com a causa em texto (mecanismo, dado inválido).</summary>
public sealed class ErroAnalise : Exception { public ErroAnalise(string m) : base(m) { } }

/// <summary>
/// Análise linear elástica de pórtico espacial (D46): rigidez direta, barras de Euler-Bernoulli com rótulas por condensação,
/// molas de comprimento zero, vínculos rígidos (mestre-escravo) e apoios com molas. Montagem em perfil (skyline) com os nós
/// renumerados por Cuthill-McKee reverso; LDLᵀ. Convenções de eixos locais iguais às do OpenSees (geomTransf com vecxz), para
/// conferir lá.
/// </summary>
public sealed class SolverBarras
{
    private readonly ModeloBarras _m;
    private int _nNos;
    // mapa de cada gdl (nó*6+g) para combinação de equações independentes: (equação, coeficiente); travado = vazio
    private List<(int Eq, double C)>[] _mapa;
    private bool[] _travado;
    private int _nEq;
    private Perfil _k;
    private readonly List<string> _avisos = new List<string>();
    public IReadOnlyList<string> Avisos => _avisos;

    public SolverBarras(ModeloBarras m) { _m = m; }

    // ------------------------------------------------------------------ geometria das barras

    /// <summary>Matriz de rotação (linhas = eixos locais x, y, z em coordenadas globais) e comprimento.</summary>
    public static (Vetor3 X, Vetor3 Y, Vetor3 Z, double L) Eixos(ModeloBarras m, BarraPortico b)
    {
        Vetor3 d = m.Nos[b.NoJ].Ponto - m.Nos[b.NoI].Ponto;
        double L = d.Modulo;
        if (L < 1e-9) throw new ErroAnalise($"Barra {Nome(b)} com comprimento nulo (nós {b.NoI} e {b.NoJ} no mesmo ponto).");
        Vetor3 x = d * (1 / L);
        Vetor3 v = b.VetorXZ ?? (Math.Abs(x.Z) > 0.999 ? Vetor3.EixoX : Vetor3.EixoZ);
        Vetor3 y = v.Vetorial(x);
        if (y.Modulo < 1e-9) throw new ErroAnalise($"Barra {Nome(b)}: o vetor do plano x-z é paralelo à barra.");
        y = y.Unitario;
        Vetor3 z = x.Vetorial(y);
        return (x, y, z, L);
    }

    private static string Nome(BarraPortico b) => b.Nome != "" ? b.Nome : "#" + b.Id;

    /// <summary>Rigidez local 12×12 (Euler-Bernoulli), sem rótulas.</summary>
    public static double[,] RigidezLocal(BarraPortico b, double L)
    {
        double E = b.Material.E, G = b.Material.G, A = b.Secao.A;
        double Iy = b.Secao.Iy * b.FatorFlexao, Iz = b.Secao.Iz * b.FatorFlexao, J = b.Secao.J * b.FatorTorcao;
        var k = new double[12, 12];
        void S(int i, int j, double v) { k[i, j] += v; if (i != j) k[j, i] += v; }
        double ea = E * A / L, gj = G * J / L;
        S(0, 0, ea); S(6, 6, ea); S(0, 6, -ea);
        S(3, 3, gj); S(9, 9, gj); S(3, 9, -gj);
        double z = E * Iz / (L * L * L), L2 = L * L;
        // plano x-y (uy, rz)
        S(1, 1, 12 * z); S(7, 7, 12 * z); S(1, 7, -12 * z);
        S(1, 5, 6 * L * z); S(1, 11, 6 * L * z); S(7, 5, -6 * L * z); S(7, 11, -6 * L * z);
        S(5, 5, 4 * L2 * z); S(11, 11, 4 * L2 * z); S(5, 11, 2 * L2 * z);
        // plano x-z (uz, ry): ry = −duz/dx
        double y = E * Iy / (L * L * L);
        S(2, 2, 12 * y); S(8, 8, 12 * y); S(2, 8, -12 * y);
        S(2, 4, -6 * L * y); S(2, 10, -6 * L * y); S(8, 4, 6 * L * y); S(8, 10, 6 * L * y);
        S(4, 4, 4 * L2 * y); S(10, 10, 4 * L2 * y); S(4, 10, 2 * L2 * y);
        return k;
    }

    /// <summary>Forças nodais equivalentes (locais, 12) de uma força pontual local p a uma distância a do nó I (funções de forma exatas).</summary>
    private static void Equivalente(double[] f, Vetor3 p, double a, double L)
    {
        double s = Math.Max(0, Math.Min(1, a / L)), s2 = s * s, s3 = s2 * s;
        double n1 = 1 - 3 * s2 + 2 * s3, n2 = L * (s - 2 * s2 + s3), n3 = 3 * s2 - 2 * s3, n4 = L * (-s2 + s3);
        f[0] += p.X * (1 - s); f[6] += p.X * s;
        f[1] += p.Y * n1; f[5] += p.Y * n2; f[7] += p.Y * n3; f[11] += p.Y * n4;
        f[2] += p.Z * n1; f[4] -= p.Z * n2; f[8] += p.Z * n3; f[10] -= p.Z * n4;
    }

    /// <summary>Cargas da barra no caso, no sistema local: distribuídas uniformes (q, de a a b) e pontuais (a, P).</summary>
    private (List<(Vetor3 Q, double A, double B)> Dist, List<(double A, Vetor3 P)> Pont) CargasLocais(CasoCarga c, BarraPortico b, (Vetor3 X, Vetor3 Y, Vetor3 Z, double L) e)
    {
        Vetor3 ParaLocal(Vetor3 g) => new Vetor3(g.Ponto(e.X), g.Ponto(e.Y), g.Ponto(e.Z));
        var dist = new List<(Vetor3, double, double)>();
        var pont = new List<(double, Vetor3)>();
        if (c.PesoProprio && b.Material.Peso > 0) dist.Add((ParaLocal(new Vetor3(0, 0, -b.Material.Peso * b.Secao.A)), 0, e.L));
        foreach (var d in c.Distribuidas.Where(d => d.Barra == b.Id))
        {
            double a0 = Math.Max(0, d.A), b0 = d.B <= 0 ? e.L : Math.Min(e.L, d.B);
            if (b0 > a0) dist.Add((d.Local ? d.Q : ParaLocal(d.Q), a0, b0));
        }
        foreach (var p in c.Pontuais.Where(p => p.Barra == b.Id)) pont.Add((Math.Max(0, Math.Min(e.L, p.A)), p.Local ? p.F : ParaLocal(p.F)));
        return (dist, pont);
    }

    /// <summary>As cargas como pontuais equivalentes para as forças nodais: Gauss de 3 pontos por trecho (exato p/ cúbica × constante).</summary>
    private IEnumerable<(double A, Vetor3 P)> CargasPontuaisEquivalentes(CasoCarga c, BarraPortico b, (Vetor3 X, Vetor3 Y, Vetor3 Z, double L) e)
    {
        var (dist, pont) = CargasLocais(c, b, e);
        double[] xg = { -Math.Sqrt(0.6), 0, Math.Sqrt(0.6) }, wg = { 5.0 / 9, 8.0 / 9, 5.0 / 9 };
        foreach (var (q, a, bb) in dist)
        {
            double h = (bb - a) / 2, m = (bb + a) / 2;
            for (int i = 0; i < 3; i++) yield return (m + h * xg[i], q * (wg[i] * h));
        }
        foreach (var p in pont) yield return p;
    }

    /// <summary>Forças de engastamento perfeito (locais, sobre a barra) = − forças nodais equivalentes.</summary>
    private double[] Engastamento(CasoCarga c, BarraPortico b, (Vetor3 X, Vetor3 Y, Vetor3 Z, double L) e)
    {
        var f = new double[12];
        foreach (var (a, p) in CargasPontuaisEquivalentes(c, b, e)) Equivalente(f, p, a, e.L);
        for (int i = 0; i < 12; i++) f[i] = -f[i];
        return f;
    }

    /// <summary>Condensa as rótulas: devolve K e F0 com as linhas/colunas liberadas zeradas, mais o necessário para recuperar.</summary>
    private static (double[,] K, double[] F0) Condensar(double[,] k, double[] f0, bool[] lib)
    {
        var c = Enumerable.Range(0, 12).Where(i => lib[i]).ToList();
        if (c.Count == 0) return (k, f0);
        var r = Enumerable.Range(0, 12).Where(i => !lib[i]).ToList();
        var kcc = new double[c.Count, c.Count];
        for (int i = 0; i < c.Count; i++) for (int j = 0; j < c.Count; j++) kcc[i, j] = k[c[i], c[j]];
        var inv = Inverter(kcc);
        var kc = new double[12, 12];
        var fc = new double[12];
        foreach (int i in r)
        {
            foreach (int j in r)
            {
                double s = k[i, j];
                for (int p = 0; p < c.Count; p++) for (int q = 0; q < c.Count; q++) s -= k[i, c[p]] * inv[p, q] * k[c[q], j];
                kc[i, j] = s;
            }
            double sf = f0[i];
            for (int p = 0; p < c.Count; p++) for (int q = 0; q < c.Count; q++) sf -= k[i, c[p]] * inv[p, q] * f0[c[q]];
            fc[i] = sf;
        }
        return (kc, fc);
    }

    private static double[,] Inverter(double[,] a)
    {
        int n = a.GetLength(0);
        var m = (double[,])a.Clone();
        var inv = new double[n, n];
        for (int i = 0; i < n; i++) inv[i, i] = 1;
        for (int c = 0; c < n; c++)
        {
            int p = c;
            for (int i = c + 1; i < n; i++) if (Math.Abs(m[i, c]) > Math.Abs(m[p, c])) p = i;
            if (Math.Abs(m[p, c]) < 1e-14) throw new ErroAnalise("Rótulas demais numa barra: a barra vira mecanismo (ex.: torção liberada nas duas pontas).");
            if (p != c) for (int j = 0; j < n; j++) { (m[c, j], m[p, j]) = (m[p, j], m[c, j]); (inv[c, j], inv[p, j]) = (inv[p, j], inv[c, j]); }
            double d = m[c, c];
            for (int j = 0; j < n; j++) { m[c, j] /= d; inv[c, j] /= d; }
            for (int i = 0; i < n; i++)
            {
                if (i == c || m[i, c] == 0) continue;
                double f = m[i, c];
                for (int j = 0; j < n; j++) { m[i, j] -= f * m[c, j]; inv[i, j] -= f * inv[c, j]; }
            }
        }
        return inv;
    }

    /// <summary>Transformação local → global de 12 gdl (T, com d_local = T·d_global).</summary>
    private static double[,] Transformacao((Vetor3 X, Vetor3 Y, Vetor3 Z, double L) e)
    {
        var t = new double[12, 12];
        Vetor3[] r = { e.X, e.Y, e.Z };
        for (int bloco = 0; bloco < 4; bloco++)
            for (int i = 0; i < 3; i++)
            {
                t[bloco * 3 + i, bloco * 3 + 0] = r[i].X;
                t[bloco * 3 + i, bloco * 3 + 1] = r[i].Y;
                t[bloco * 3 + i, bloco * 3 + 2] = r[i].Z;
            }
        return t;
    }

    private static double[,] TtKT(double[,] t, double[,] k)
    {
        int n = k.GetLength(0);
        var kt = new double[n, n];
        for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) { double s = 0; for (int p = 0; p < n; p++) s += k[i, p] * t[p, j]; kt[i, j] = s; }
        var r = new double[n, n];
        for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) { double s = 0; for (int p = 0; p < n; p++) s += t[p, i] * kt[p, j]; r[i, j] = s; }
        return r;
    }

    private static double[] Tt(double[,] t, double[] f)
    {
        int n = f.Length; var r = new double[n];
        for (int i = 0; i < n; i++) { double s = 0; for (int p = 0; p < n; p++) s += t[p, i] * f[p]; r[i] = s; }
        return r;
    }

    private static double[] Mult(double[,] t, double[] d)
    {
        int n = d.Length; var r = new double[n];
        for (int i = 0; i < n; i++) { double s = 0; for (int p = 0; p < n; p++) s += t[i, p] * d[p]; r[i] = s; }
        return r;
    }

    /// <summary>Eixos da mola (linhas = x', y', z').</summary>
    private static (Vetor3 X, Vetor3 Y, Vetor3 Z, double L) EixosMola(MolaLigacao m)
    {
        if (m.EixoX == null) return (Vetor3.EixoX, Vetor3.EixoY, Vetor3.EixoZ, 1);
        Vetor3 x = m.EixoX.Value.Unitario;
        Vetor3 v = m.VetorXZ ?? (Math.Abs(x.Z) > 0.999 ? Vetor3.EixoX : Vetor3.EixoZ);
        Vetor3 y = v.Vetorial(x).Unitario;
        return (x, y, x.Vetorial(y), 1);
    }

    // ------------------------------------------------------------------ numeração

    private void Numerar()
    {
        _nNos = _m.Nos.Count;
        int n = _nNos * 6;
        _travado = new bool[n];
        foreach (var a in _m.Apoios)
            for (int g = 0; g < 6; g++) if (a.Travado[g]) _travado[a.No * 6 + g] = true;

        // escravos: gdl → expressão em gdl do mestre
        var expr = new Dictionary<int, List<(int Gdl, double C)>>();
        foreach (var v in _m.Vinculos)
        {
            Vetor3 r = _m.Nos[v.Escravo].Ponto - _m.Nos[v.Mestre].Ponto;
            int m0 = v.Mestre * 6, s0 = v.Escravo * 6;
            bool G(int g) => v.Graus[g];
            void Def(int g, List<(int, double)> e)
            {
                if (expr.ContainsKey(s0 + g)) throw new ErroAnalise($"Nó {v.Escravo} preso a dois vínculos rígidos no mesmo grau ({(Gdl)g}).");
                if (_travado[s0 + g]) throw new ErroAnalise($"Nó {v.Escravo}: grau {(Gdl)g} com apoio e vínculo rígido ao mesmo tempo; ponha o apoio no nó mestre.");
                expr[s0 + g] = e;
            }
            // u_e = u_m + θ_m × r, com θ×r = (θy·rz − θz·ry, θz·rx − θx·rz, θx·ry − θy·rx); só entram as rotações que o vínculo prende
            if (G(0)) Def(0, new List<(int, double)> { (m0 + 0, 1), (m0 + 4, G(4) ? r.Z : 0), (m0 + 5, G(5) ? -r.Y : 0) });
            if (G(1)) Def(1, new List<(int, double)> { (m0 + 1, 1), (m0 + 5, G(5) ? r.X : 0), (m0 + 3, G(3) ? -r.Z : 0) });
            if (G(2)) Def(2, new List<(int, double)> { (m0 + 2, 1), (m0 + 3, G(3) ? r.Y : 0), (m0 + 4, G(4) ? -r.X : 0) });
            for (int g = 3; g < 6; g++) if (G(g)) Def(g, new List<(int, double)> { (m0 + g, 1) });
        }

        // ordem dos nós: Cuthill-McKee reverso pelo grafo de ligações
        var viz = Enumerable.Range(0, _nNos).Select(_ => new HashSet<int>()).ToArray();
        void Liga(int a, int b) { if (a == b) return; viz[a].Add(b); viz[b].Add(a); }
        foreach (var b in _m.Barras) Liga(b.NoI, b.NoJ);
        foreach (var mo in _m.Molas) Liga(mo.NoI, mo.NoJ);
        foreach (var v in _m.Vinculos) Liga(v.Mestre, v.Escravo);
        var ordem = new List<int>();
        var visto = new bool[_nNos];
        foreach (int inicio in Enumerable.Range(0, _nNos).OrderBy(i => viz[i].Count))
        {
            if (visto[inicio]) continue;
            var fila = new Queue<int>(); fila.Enqueue(inicio); visto[inicio] = true;
            while (fila.Count > 0)
            {
                int a = fila.Dequeue(); ordem.Add(a);
                foreach (int b in viz[a].Where(x => !visto[x]).OrderBy(x => viz[x].Count)) { visto[b] = true; fila.Enqueue(b); }
            }
        }
        ordem.Reverse();

        var eqDe = new int[n];
        for (int i = 0; i < n; i++) eqDe[i] = -1;
        _nEq = 0;
        foreach (int no in ordem)
            for (int g = 0; g < 6; g++)
            {
                int k = no * 6 + g;
                if (!_travado[k] && !expr.ContainsKey(k)) eqDe[k] = _nEq++;
            }

        // expressões finais (escravo de escravo resolvido por substituição)
        _mapa = new List<(int, double)>[n];
        List<(int Eq, double C)> Resolver(int k, int prof)
        {
            if (_mapa[k] != null) return _mapa[k];
            if (prof > 50) throw new ErroAnalise("Vínculos rígidos em ciclo.");
            var r = new List<(int, double)>();
            if (_travado[k]) { }
            else if (!expr.TryGetValue(k, out var e)) r.Add((eqDe[k], 1));
            else
                foreach (var (gdl, c) in e)
                {
                    if (c == 0) continue;
                    foreach (var (eq, c2) in Resolver(gdl, prof + 1)) r.Add((eq, c * c2));
                }
            _mapa[k] = r.GroupBy(x => x.Item1).Select(gq => (gq.Key, gq.Sum(x => x.Item2))).ToList();
            return _mapa[k];
        }
        for (int k = 0; k < n; k++) Resolver(k, 0);
    }

    // ------------------------------------------------------------------ montagem e solução

    private sealed class Perfil
    {
        public readonly int N;
        public readonly int[] Topo;        // primeira linha guardada em cada coluna
        public readonly double[][] Col;    // Col[j][i − Topo[j]] = K[i, j], i ≤ j
        public Perfil(int n, int[] topo) { N = n; Topo = topo; Col = new double[n][]; for (int j = 0; j < n; j++) Col[j] = new double[j - topo[j] + 1]; }
        public void Somar(int i, int j, double v) { if (i > j) (i, j) = (j, i); Col[j][i - Topo[j]] += v; }
        public double Diag(int j) => Col[j][j - Topo[j]];
        public double Get(int i, int j) { if (i > j) (i, j) = (j, i); return i < Topo[j] ? 0 : Col[j][i - Topo[j]]; }
    }

    private IEnumerable<int[]> GruposGdl()
    {
        foreach (var b in _m.Barras) yield return Gdls(b.NoI, b.NoJ);
        foreach (var mo in _m.Molas) yield return Gdls(mo.NoI, mo.NoJ);
        foreach (var a in _m.Apoios) yield return Enumerable.Range(a.No * 6, 6).ToArray();
    }

    private static int[] Gdls(int i, int j) => Enumerable.Range(i * 6, 6).Concat(Enumerable.Range(j * 6, 6)).ToArray();

    private void MontarPerfil()
    {
        var topo = new int[_nEq];
        for (int j = 0; j < _nEq; j++) topo[j] = j;
        foreach (int[] g in GruposGdl())
        {
            var eqs = g.SelectMany(k => _mapa[k].Select(x => x.Eq)).Distinct().ToList();
            if (eqs.Count == 0) continue;
            int min = eqs.Min();
            foreach (int e in eqs) topo[e] = Math.Min(topo[e], min);
        }
        _k = new Perfil(_nEq, topo);
    }

    private void Espalhar(int[] gdls, double[,] kg, double[] fg, double[] F)
    {
        for (int a = 0; a < gdls.Length; a++)
        {
            var ma = _mapa[gdls[a]];
            if (fg != null && fg[a] != 0) foreach (var (ea, ca) in ma) F[ea] += ca * fg[a];
            if (kg == null) continue;
            for (int b = 0; b < gdls.Length; b++)
            {
                double v = kg[a, b];
                if (v == 0) continue;
                var mb = _mapa[gdls[b]];
                foreach (var (ea, ca) in ma)
                    foreach (var (eb, cb) in mb)
                        if (ea <= eb) _k.Somar(ea, eb, ca * cb * v);
            }
        }
    }

    private (double[,] Kg, double[,] T, double[,] Kl, double[] F0l, (Vetor3, Vetor3, Vetor3, double) E) Barra(BarraPortico b, CasoCarga caso)
    {
        var e = Eixos(_m, b);
        double[,] kl = RigidezLocal(b, e.L);
        double[] f0 = caso == null ? new double[12] : Engastamento(caso, b, e);
        bool[] lib = b.LiberaI.Concat(b.LiberaJ).ToArray();
        var (kc, fc) = Condensar(kl, f0, lib);
        double[,] t = Transformacao(e);
        return (TtKT(t, kc), t, kl, f0, e);
    }

    private double[,] Mola(MolaLigacao mo, out double[,] t)
    {
        var e = EixosMola(mo);
        var kl = new double[12, 12];
        for (int g = 0; g < 6; g++)
        {
            double k = mo.K[g];
            kl[g, g] += k; kl[g + 6, g + 6] += k; kl[g, g + 6] -= k; kl[g + 6, g] -= k;
        }
        t = Transformacao(e);
        return TtKT(t, kl);
    }

    /// <summary>Monta, fatora e resolve todos os casos do modelo.</summary>
    public Dictionary<string, ResultadoCaso> Resolver()
    {
        Validar();
        Numerar();
        MontarPerfil();
        if (_nEq == 0) throw new ErroAnalise("Modelo sem graus livres.");

        var cacheBarras = _m.Barras.Select(b => Barra(b, null)).ToList();
        for (int i = 0; i < _m.Barras.Count; i++) Espalhar(Gdls(_m.Barras[i].NoI, _m.Barras[i].NoJ), cacheBarras[i].Kg, null, null);
        var tMolas = new double[_m.Molas.Count][,];
        for (int i = 0; i < _m.Molas.Count; i++) Espalhar(Gdls(_m.Molas[i].NoI, _m.Molas[i].NoJ), Mola(_m.Molas[i], out tMolas[i]), null, null);
        foreach (var a in _m.Apoios)
        {
            var km = new double[6, 6];
            for (int g = 0; g < 6; g++) if (!a.Travado[g]) km[g, g] = a.Mola[g];
            Espalhar(Enumerable.Range(a.No * 6, 6).ToArray(), km, null, null);
        }
        Fatorar();

        var r = new Dictionary<string, ResultadoCaso>();
        foreach (var caso in _m.Casos) r[caso.Nome] = ResolverCaso(caso, tMolas);
        return r;
    }

    private void Validar()
    {
        int n = _m.Nos.Count;
        foreach (var b in _m.Barras)
        {
            if (b.NoI < 0 || b.NoI >= n || b.NoJ < 0 || b.NoJ >= n) throw new ErroAnalise($"Barra {Nome(b)} com nó inexistente.");
            if (b.Secao == null || b.Material == null || b.Secao.A <= 0 || b.Material.E <= 0)
                throw new ErroAnalise($"Barra {Nome(b)} sem seção ou material válido.");
            if (b.LiberaI[0] && b.LiberaJ[0]) throw new ErroAnalise($"Barra {Nome(b)}: força normal liberada nas duas pontas (mecanismo).");
            if (b.LiberaI[3] && b.LiberaJ[3]) throw new ErroAnalise($"Barra {Nome(b)}: torção liberada nas duas pontas (mecanismo).");
        }
    }

    private const double TolPivo = 1e-11;

    private void Fatorar()
    {
        int n = _nEq;
        var d0 = new double[n];
        for (int j = 0; j < n; j++) d0[j] = Math.Abs(_k.Diag(j));
        double escala = d0.DefaultIfEmpty(1).Max();
        // LDLᵀ em perfil (Bathe, COLSOL)
        for (int j = 0; j < n; j++)
        {
            double[] cj = _k.Col[j];
            int tj = _k.Topo[j];
            for (int i = tj; i < j; i++)
            {
                double[] ci = _k.Col[i];
                int ti = _k.Topo[i];
                int ini = Math.Max(ti, tj);
                double s = cj[i - tj];
                for (int p = ini; p < i; p++) s -= ci[p - ti] * cj[p - tj];
                cj[i - tj] = s;
            }
            double dj = cj[j - tj];
            for (int i = tj; i < j; i++)
            {
                double gij = cj[i - tj];
                double lij = gij / _k.Col[i][i - _k.Topo[i]];
                dj -= lij * gij;
                cj[i - tj] = lij;
            }
            if (dj <= TolPivo * Math.Max(d0[j], escala * 1e-6))
                throw new ErroAnalise("Estrutura instável (mecanismo): " + DescreverEquacao(j) + ". Confira apoios, rótulas e ligações.");
            cj[j - tj] = dj;
        }
    }

    private string DescreverEquacao(int eq)
    {
        for (int k = 0; k < _mapa.Length; k++)
            if (_mapa[k].Count == 1 && _mapa[k][0].Eq == eq && Math.Abs(_mapa[k][0].C - 1) < 1e-12)
            {
                var no = _m.Nos[k / 6];
                return $"nó {no.Id}{(no.Nome != "" ? " (" + no.Nome + ")" : "")}{(no.Dono != null ? " de " + no.Dono : "")}, grau {(Gdl)(k % 6)}";
            }
        return "equação " + eq;
    }

    private double[] Substituir(double[] f)
    {
        int n = _nEq;
        var x = (double[])f.Clone();
        for (int j = 0; j < n; j++)
        {
            int tj = _k.Topo[j]; double[] cj = _k.Col[j];
            double s = x[j];
            for (int i = tj; i < j; i++) s -= cj[i - tj] * x[i];
            x[j] = s;
        }
        for (int j = 0; j < n; j++) x[j] /= _k.Diag(j);
        for (int j = n - 1; j >= 0; j--)
        {
            int tj = _k.Topo[j]; double[] cj = _k.Col[j];
            for (int i = tj; i < j; i++) x[i] -= cj[i - tj] * x[j];
        }
        return x;
    }

    private ResultadoCaso ResolverCaso(CasoCarga caso, double[][,] tMolas)
    {
        int nn = _m.Nos.Count;
        var F = new double[_nEq];
        // cargas nodais
        var cargaNo = new double[nn * 6];
        foreach (var (no, f) in caso.Nodais) for (int g = 0; g < 6; g++) cargaNo[no * 6 + g] += f[g];
        for (int k = 0; k < nn * 6; k++) if (cargaNo[k] != 0) foreach (var (eq, c) in _mapa[k]) F[eq] += c * cargaNo[k];
        // cargas nas barras: −F0 condensado, em global
        var dadosBarras = new List<(double[,] T, double[,] Kl, double[] F0, bool[] Lib)>();
        foreach (var b in _m.Barras)
        {
            var e = Eixos(_m, b);
            double[,] kl = RigidezLocal(b, e.L);
            double[] f0 = Engastamento(caso, b, e);
            bool[] lib = b.LiberaI.Concat(b.LiberaJ).ToArray();
            var (_, fc) = Condensar(kl, f0, lib);
            double[,] t = Transformacao(e);
            double[] fg = Tt(t, fc);
            int[] gd = Gdls(b.NoI, b.NoJ);
            for (int a = 0; a < 12; a++) if (fg[a] != 0) foreach (var (eq, c) in _mapa[gd[a]]) F[eq] -= c * fg[a];
            dadosBarras.Add((t, kl, f0, lib));
        }

        double[] x = Substituir(F);
        var u = new double[nn * 6];
        for (int k = 0; k < u.Length; k++) foreach (var (eq, c) in _mapa[k]) u[k] += c * x[eq];

        var res = new ResultadoCaso
        {
            Nome = caso.Nome,
            Deslocamentos = Enumerable.Range(0, nn).Select(i => u.Skip(i * 6).Take(6).ToArray()).ToArray(),
            ForcasBarras = new double[_m.Barras.Count][],
            ForcasMolas = new double[_m.Molas.Count][],
            DeslocBarras = new double[_m.Barras.Count][],
            Reacoes = new double[nn][],
        };
        // força interna em cada nó (soma do que os elementos recebem) para as reações
        var interna = new double[nn * 6];
        for (int i = 0; i < _m.Barras.Count; i++)
        {
            var b = _m.Barras[i];
            var (t, kl, f0, lib) = dadosBarras[i];
            int[] gd = Gdls(b.NoI, b.NoJ);
            double[] dl = Mult(t, gd.Select(k => u[k]).ToArray());
            // rótulas: deslocamento relativo liberado recuperado pela condensação (F_c = 0)
            var c = Enumerable.Range(0, 12).Where(k => lib[k]).ToList();
            if (c.Count > 0)
            {
                var r = Enumerable.Range(0, 12).Where(k => !lib[k]).ToList();
                var kcc = new double[c.Count, c.Count];
                for (int p = 0; p < c.Count; p++) for (int q = 0; q < c.Count; q++) kcc[p, q] = kl[c[p], c[q]];
                var inv = Inverter(kcc);
                var rhs = c.Select(ci => f0[ci] + r.Sum(rj => kl[ci, rj] * dl[rj])).ToArray();
                for (int p = 0; p < c.Count; p++) { double s = 0; for (int q = 0; q < c.Count; q++) s -= inv[p, q] * rhs[q]; dl[c[p]] = s; }
            }
            var fl = new double[12];
            for (int a = 0; a < 12; a++) { double s = f0[a]; for (int bb = 0; bb < 12; bb++) s += kl[a, bb] * dl[bb]; fl[a] = s; }
            for (int k = 0; k < 12; k++) if (lib[k]) fl[k] = 0;
            res.ForcasBarras[i] = fl;
            res.DeslocBarras[i] = dl;
            double[] fg = Tt(t, fl);
            for (int a = 0; a < 12; a++) interna[gd[a]] += fg[a];
        }
        for (int i = 0; i < _m.Molas.Count; i++)
        {
            var mo = _m.Molas[i];
            int[] gd = Gdls(mo.NoI, mo.NoJ);
            double[] dl = Mult(tMolas[i], gd.Select(k => u[k]).ToArray());
            var f = new double[6];
            for (int g = 0; g < 6; g++) f[g] = mo.K[g] * (dl[g + 6] - dl[g]);
            res.ForcasMolas[i] = f;
            var fl = new double[12];
            for (int g = 0; g < 6; g++) { fl[g] = -f[g]; fl[g + 6] = f[g]; }
            double[] fg = Tt(tMolas[i], fl);
            for (int a = 0; a < 12; a++) interna[gd[a]] += fg[a];
        }
        // vínculos rígidos: o desequilíbrio do escravo (força interna − carga) vai ao mestre, com o momento do braço; das pontas da
        // cadeia para o começo (um escravo que é mestre de outro só passa adiante depois de receber)
        var pendentes = _m.Vinculos.ToList();
        while (pendentes.Count > 0)
        {
            var v = pendentes.FirstOrDefault(x => !pendentes.Any(y => y != x && y.Mestre == x.Escravo)) ?? pendentes[0];
            pendentes.Remove(v);
            Vetor3 r = _m.Nos[v.Escravo].Ponto - _m.Nos[v.Mestre].Ponto;
            int s0 = v.Escravo * 6, m0 = v.Mestre * 6;
            double Net(int g) => v.Graus[g] ? interna[s0 + g] - cargaNo[s0 + g] : 0;
            var f = new Vetor3(Net(0), Net(1), Net(2));
            var mt = new Vetor3(Net(3), Net(4), Net(5)) + r.Vetorial(f);
            interna[m0] += f.X; interna[m0 + 1] += f.Y; interna[m0 + 2] += f.Z;
            interna[m0 + 3] += mt.X; interna[m0 + 4] += mt.Y; interna[m0 + 5] += mt.Z;
            for (int g = 0; g < 6; g++) if (v.Graus[g]) interna[s0 + g] = cargaNo[s0 + g];
        }
        foreach (var a in _m.Apoios)
        {
            var rr = new double[6];
            for (int g = 0; g < 6; g++)
            {
                int k = a.No * 6 + g;
                rr[g] = a.Travado[g] ? interna[k] - cargaNo[k] : -a.Mola[g] * u[k];   // mola: reação = −k·u
            }
            res.Reacoes[a.No] = rr;
        }
        return res;
    }

    // ------------------------------------------------------------------ deformada ao longo da barra

    /// <summary>
    /// Deslocamento (global, cm) num ponto a x do nó I: Hermite com os deslocamentos e rotações locais das pontas (rótulas incluídas)
    /// mais a solução particular de engastamento perfeito das cargas do vão (exata para pontuais; distribuídas por Gauss). Para
    /// combinação, some os casos (linear).
    /// </summary>
    public Vetor3 Deformada(ResultadoCaso r, CasoCarga caso, BarraPortico b, double x)
    {
        var e = Eixos(_m, b);
        double L = e.L, s = Math.Max(0, Math.Min(1, x / L)), s2 = s * s, s3 = s2 * s;
        double[] d = r.DeslocBarras[b.Id];
        double n1 = 1 - 3 * s2 + 2 * s3, n2 = L * (s - 2 * s2 + s3), n3 = 3 * s2 - 2 * s3, n4 = L * (-s2 + s3);
        double u = d[0] * (1 - s) + d[6] * s;
        double v = d[1] * n1 + d[5] * n2 + d[7] * n3 + d[11] * n4;
        double w = d[2] * n1 - d[4] * n2 + d[8] * n3 - d[10] * n4;
        if (caso != null)
        {
            double E = b.Material.E, eiy = E * b.Secao.Iy * b.FatorFlexao, eiz = E * b.Secao.Iz * b.FatorFlexao;
            var (dist, pont) = CargasLocais(caso, b, e);
            void Ponto(double a, Vetor3 p)
            {
                double g = GreenEngastada(x, a, L);
                if (eiz > 0) v += p.Y * g / eiz;
                if (eiy > 0) w += p.Z * g / eiy;
            }
            foreach (var (a, p) in pont) Ponto(a, p);
            // distribuídas: Gauss de 3 pontos em cada lado de x (a função de Green é polinomial de cada lado: exato)
            double[] xg = { -Math.Sqrt(0.6), 0, Math.Sqrt(0.6) }, wg = { 5.0 / 9, 8.0 / 9, 5.0 / 9 };
            foreach (var (q, a0, b0) in dist)
                foreach (var (i0, i1) in new[] { (a0, Math.Min(b0, Math.Max(a0, x))), (Math.Max(a0, Math.Min(b0, x)), b0) })
                {
                    if (i1 - i0 < 1e-12) continue;
                    double h = (i1 - i0) / 2, m = (i1 + i0) / 2;
                    for (int k = 0; k < 3; k++) Ponto(m + h * xg[k], q * (wg[k] * h));
                }
        }
        return e.X * u + e.Y * v + e.Z * w;
    }

    /// <summary>Flecha da viga biengastada por carga unitária em a, no ponto x, vezes EI (Roark): P·b²·x²·(3aL − (3a + b)x)/(6L³).</summary>
    private static double GreenEngastada(double x, double a, double L)
    {
        if (x > a) { x = L - x; a = L - a; }
        double b = L - a;
        return b * b * x * x * (3 * a * L - (3 * a + b) * x) / (6 * L * L * L);
    }

    // ------------------------------------------------------------------ esforços ao longo da barra

    /// <summary>
    /// Esforços internos a uma distância x do nó I (sistema local): o que a parte de depois de x faz na de antes. N &gt; 0 = tração.
    /// Convenção vetorial (a do OpenSees): numa viga horizontal com z local para cima, My &gt; 0 traciona a face de CIMA (+z);
    /// o momento positivo da viga biapoiada sai negativo.
    /// </summary>
    public EsforcosSecao Esforcos(ResultadoCaso r, CasoCarga caso, BarraPortico b, double x)
    {
        var e = Eixos(_m, b);
        double[] f = r.ForcasBarras[b.Id];
        // resultante do que age na parte [0, x]: força da ponta I + cargas antes de x
        var F = new Vetor3(f[0], f[1], f[2]);
        var M = new Vetor3(f[3], f[4], f[5]);
        // momento das forças da ponta I em relação a x: r = (0 − x) no eixo local
        M = M + new Vetor3(-x, 0, 0).Vetorial(F);
        if (caso != null)
        {
            var (dist, pont) = CargasLocais(caso, b, e);
            foreach (var (q, a, bb) in dist)
            {
                double fim = Math.Min(bb, x);
                if (fim <= a) continue;
                Vetor3 p = q * (fim - a);
                F = F + p;
                M = M + new Vetor3((a + fim) / 2 - x, 0, 0).Vetorial(p);   // resultante no meio do trecho carregado
            }
            foreach (var (a, p) in pont)
            {
                if (a > x + 1e-9) continue;
                F = F + p;
                M = M + new Vetor3(a - x, 0, 0).Vetorial(p);
            }
        }
        // a parte de depois faz −(resultante) na de antes; N de tração = componente x de −F … com a face negativa: N = −Fx
        return new EsforcosSecao(-F.X, -F.Y, -F.Z, -M.X, -M.Y, -M.Z);
    }
}
