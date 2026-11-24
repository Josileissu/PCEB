using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DEB.Core.Analise.Barras;

/// <summary>Formatos de exportação do modelo de barras (D50, item 8).</summary>
public enum FormatoExportacao { OpenSees, CalculiX, Sap2000 }

/// <summary>
/// Exporta o modelo de barras do PCEB (nós, barras, molas dos aparelhos, vínculos rígidos, apoios e casos de carga) para o
/// OpenSees (script openseespy), o CalculiX (.inp) e o SAP2000 (.s2k, texto de tabelas). Unidades do PCEB: kN e cm.
/// Antes de escrever, as barras são divididas nos pontos de carga concentrada e nas pontas das cargas parciais: cada trecho fica
/// só com carga uniforme no comprimento todo (o que os três programas aceitam do mesmo jeito) e as concentradas viram nodais.
/// </summary>
public static class ExportacaoAnalise
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string N(double x) => x.ToString("0.0#####E+00", Inv);   // sempre com ponto: o CalculiX (Fortran) não lê "5E+2"
    private static string D(double x) => Math.Round(x, 6).ToString("0.######", Inv);

    // ------------------------------------------------------------------ modelo preparado

    /// <summary>Trecho de barra entre dois nós do modelo exportado.</summary>
    public sealed class Trecho
    {
        public int Barra, I, J;
        public bool[] LibI = new bool[6], LibJ = new bool[6];
        public Vetor3 X, Y, Z;
        public double L;
    }

    /// <summary>Modelo pronto para escrever: nós (originais + divisões), trechos e, por caso, cargas uniformes locais e nodais globais.</summary>
    public sealed class Preparado
    {
        public ModeloBarras M;
        public List<Vetor3> Nos = new List<Vetor3>();
        public List<Trecho> Trechos = new List<Trecho>();
        public List<(CasoCarga Caso, Dictionary<int, Vetor3> Uniformes, Dictionary<int, double[]> Nodais)> Casos = new List<(CasoCarga, Dictionary<int, Vetor3>, Dictionary<int, double[]>)>();
        /// <summary>Barra original → índices dos trechos, em ordem do nó I ao J.</summary>
        public Dictionary<int, List<int>> TrechosDaBarra = new Dictionary<int, List<int>>();
    }

    public static Preparado Preparar(ModeloBarras m)
    {
        var p = new Preparado { M = m };
        foreach (var n in m.Nos) p.Nos.Add(n.Ponto);
        foreach (var b in m.Barras)
        {
            var (x, y, z, L) = SolverBarras.Eixos(m, b);
            // pontos de divisão: concentradas e pontas das parciais de todos os casos
            var cortes = new SortedSet<double>();
            foreach (var c in m.Casos)
            {
                foreach (var d in c.Distribuidas.Where(d => d.Barra == b.Id))
                {
                    double a0 = Math.Max(0, d.A), b0 = d.B <= 0 ? L : Math.Min(L, d.B);
                    if (a0 > 1e-6 && a0 < L - 1e-6) cortes.Add(Math.Round(a0, 6));
                    if (b0 > 1e-6 && b0 < L - 1e-6) cortes.Add(Math.Round(b0, 6));
                }
                foreach (var q in c.Pontuais.Where(q => q.Barra == b.Id))
                    if (q.A > 1e-6 && q.A < L - 1e-6) cortes.Add(Math.Round(q.A, 6));
            }
            var pos = new List<double> { 0 }; pos.AddRange(cortes); pos.Add(L);
            var nos = new List<int> { b.NoI };
            Vetor3 pi = m.Nos[b.NoI].Ponto;
            foreach (double s in cortes) { p.Nos.Add(pi + x * s); nos.Add(p.Nos.Count - 1); }
            nos.Add(b.NoJ);
            var lista = new List<int>();
            for (int k = 0; k + 1 < nos.Count; k++)
            {
                var t = new Trecho { Barra = b.Id, I = nos[k], J = nos[k + 1], X = x, Y = y, Z = z, L = pos[k + 1] - pos[k] };
                if (k == 0) t.LibI = (bool[])b.LiberaI.Clone();
                if (k == nos.Count - 2) t.LibJ = (bool[])b.LiberaJ.Clone();
                p.Trechos.Add(t); lista.Add(p.Trechos.Count - 1);
            }
            p.TrechosDaBarra[b.Id] = lista;
        }
        foreach (var c in m.Casos)
        {
            var uni = new Dictionary<int, Vetor3>();
            var nod = new Dictionary<int, double[]>();
            void Nodal(int no, Vetor3 f, Vetor3 mo)
            {
                if (!nod.TryGetValue(no, out var v)) nod[no] = v = new double[6];
                v[0] += f.X; v[1] += f.Y; v[2] += f.Z; v[3] += mo.X; v[4] += mo.Y; v[5] += mo.Z;
            }
            foreach (var (no, f) in c.Nodais) Nodal(no, new Vetor3(f[0], f[1], f[2]), new Vetor3(f[3], f[4], f[5]));
            foreach (var b in m.Barras)
            {
                var ts = p.TrechosDaBarra[b.Id];
                var t0 = p.Trechos[ts[0]];
                Vetor3 Local(Vetor3 g) => new Vetor3(g.Ponto(t0.X), g.Ponto(t0.Y), g.Ponto(t0.Z));
                Vetor3 Global(Vetor3 l) => t0.X * l.X + t0.Y * l.Y + t0.Z * l.Z;
                double s0 = 0;
                foreach (int ti in ts)
                {
                    var t = p.Trechos[ti];
                    double meio = s0 + t.L / 2;
                    var q = new Vetor3(0, 0, 0);
                    if (c.PesoProprio && b.Material.Peso > 0) q += Local(new Vetor3(0, 0, -b.Material.Peso * b.Secao.A));
                    double L = s0 + t.L;
                    foreach (var d in c.Distribuidas.Where(d => d.Barra == b.Id))
                    {
                        double a0 = Math.Max(0, d.A), b0 = d.B <= 0 ? double.MaxValue : d.B;
                        if (meio > a0 && meio < b0) q += d.Local ? d.Q : Local(d.Q);
                    }
                    if (q.Modulo > 0) uni[ti] = q;
                    s0 += t.L;
                }
                double total = s0;
                foreach (var pc in c.Pontuais.Where(q => q.Barra == b.Id))
                {
                    double a = Math.Max(0, Math.Min(total, pc.A));
                    // o nó onde está a carga: o início do trecho que começa em a (ou o fim da barra)
                    double s = 0; int no = p.Trechos[ts[0]].I;
                    foreach (int ti in ts) { if (Math.Abs(s - a) < 1e-5) { no = p.Trechos[ti].I; break; } s += p.Trechos[ti].L; no = p.Trechos[ti].J; }
                    Nodal(no, pc.Local ? Global(pc.F) : pc.F, new Vetor3(0, 0, 0));
                }
            }
            p.Casos.Add((c, uni, nod));
        }
        return p;
    }

    public static string Exportar(ModeloBarras m, FormatoExportacao f, string titulo = "") => f switch
    {
        FormatoExportacao.OpenSees => OpenSees(m, titulo),
        FormatoExportacao.CalculiX => CalculiX(m, titulo),
        _ => Sap2000(m, titulo),
    };

    public static string Extensao(FormatoExportacao f) => f switch { FormatoExportacao.OpenSees => ".py", FormatoExportacao.CalculiX => ".inp", _ => ".s2k" };

    // ------------------------------------------------------------------ OpenSees (openseespy)

    /// <summary>
    /// Script openseespy: o modelo numa função, um padrão de carga por caso (cada caso numa análise linear separada) e os resultados
    /// de cada caso gravados em <c>resultados_&lt;caso&gt;.json</c> ao lado do script (deslocamentos, reações e esforços locais).
    /// </summary>
    public static string OpenSees(ModeloBarras m, string titulo = "")
    {
        var p = Preparar(m);
        var s = new StringBuilder();
        s.AppendLine($"# Modelo de análise exportado pelo PCEB (D50) — {titulo}");
        s.AppendLine("# Unidades: kN, cm, rad. Barras divididas nos pontos de carga; elasticBeamColumn 3D; aparelhos = zeroLength; vínculos = rigidLink/equalDOF.");
        s.AppendLine("import json, os");
        s.AppendLine("import openseespy.opensees as ops");
        s.AppendLine();
        s.AppendLine("def modelo():");
        s.AppendLine("    ops.wipe()");
        s.AppendLine("    ops.model('basic', '-ndm', 3, '-ndf', 6)");
        for (int i = 0; i < p.Nos.Count; i++) s.AppendLine($"    ops.node({i + 1}, {N(p.Nos[i].X)}, {N(p.Nos[i].Y)}, {N(p.Nos[i].Z)})");
        int extra = p.Nos.Count;
        // apoios (e molas de apoio: nó fixo auxiliar + zeroLength)
        int mat = 0, ele = 0;
        foreach (var a in m.Apoios)
        {
            s.AppendLine($"    ops.fix({a.No + 1}, {string.Join(", ", a.Travado.Select(t => t ? "1" : "0"))})");
            if (a.Mola.Any(k => k > 0))
            {
                int aux = ++extra; var pt = p.Nos[a.No];
                s.AppendLine($"    ops.node({aux}, {N(pt.X)}, {N(pt.Y)}, {N(pt.Z)}); ops.fix({aux}, 1, 1, 1, 1, 1, 1)");
                var mats = new List<int>(); var dirs = new List<int>();
                for (int d = 0; d < 6; d++) if (a.Mola[d] > 0 && !a.Travado[d]) { s.AppendLine($"    ops.uniaxialMaterial('Elastic', {++mat}, {N(a.Mola[d])})"); mats.Add(mat); dirs.Add(d + 1); }
                if (mats.Count > 0) s.AppendLine($"    ops.element('zeroLength', {100000 + ++ele}, {aux}, {a.No + 1}, '-mat', {string.Join(", ", mats)}, '-dir', {string.Join(", ", dirs)})");
            }
        }
        // barras
        for (int i = 0; i < p.Trechos.Count; i++)
        {
            var t = p.Trechos[i]; var b = m.Barras.First(x => x.Id == t.Barra);
            s.AppendLine($"    ops.geomTransf('Linear', {i + 1}, {N(t.Z.X)}, {N(t.Z.Y)}, {N(t.Z.Z)})");
            double iy = b.Secao.Iy * b.FatorFlexao, iz = b.Secao.Iz * b.FatorFlexao, j = b.Secao.J * b.FatorTorcao;
            int rz = (t.LibI[5] ? 1 : 0) + (t.LibJ[5] ? 2 : 0), ry = (t.LibI[4] ? 1 : 0) + (t.LibJ[4] ? 2 : 0);
            string lib = (rz > 0 ? $", '-releasez', {rz}" : "") + (ry > 0 ? $", '-releasey', {ry}" : "");
            s.AppendLine($"    ops.element('elasticBeamColumn', {i + 1}, {t.I + 1}, {t.J + 1}, {N(b.Secao.A)}, {N(b.Material.E)}, {N(b.Material.G)}, {N(j)}, {N(iy)}, {N(iz)}, {i + 1}{lib})" +
                         (b.Nome != "" ? $"  # {b.Nome}" : ""));
        }
        // molas dos aparelhos
        foreach (var mo in m.Molas)
        {
            var mats = new List<int>(); var dirs = new List<int>();
            for (int d = 0; d < 6; d++) if (mo.K[d] > 0) { s.AppendLine($"    ops.uniaxialMaterial('Elastic', {++mat}, {N(mo.K[d])})"); mats.Add(mat); dirs.Add(d + 1); }
            if (mats.Count == 0) continue;
            string ori = "";
            if (mo.EixoX != null)
            {
                var x = mo.EixoX.Value.Unitario; var v = mo.VetorXZ ?? (Math.Abs(x.Z) > 0.999 ? Vetor3.EixoX : Vetor3.EixoZ); var y = v.Vetorial(x).Unitario;
                ori = $", '-orient', {N(x.X)}, {N(x.Y)}, {N(x.Z)}, {N(y.X)}, {N(y.Y)}, {N(y.Z)}";
            }
            s.AppendLine($"    ops.element('zeroLength', {100000 + ++ele}, {mo.NoI + 1}, {mo.NoJ + 1}, '-mat', {string.Join(", ", mats)}, '-dir', {string.Join(", ", dirs)}{ori})" +
                         (mo.Nome != "" ? $"  # {mo.Nome}" : ""));
        }
        foreach (var v in m.Vinculos)
        {
            if (v.Graus.All(g => g)) s.AppendLine($"    ops.rigidLink('beam', {v.Mestre + 1}, {v.Escravo + 1})");
            else if (v.Graus.Take(3).All(g => g) && !v.Graus.Skip(3).Any(g => g)) s.AppendLine($"    ops.rigidLink('bar', {v.Mestre + 1}, {v.Escravo + 1})");
            else s.AppendLine($"    ops.equalDOF({v.Mestre + 1}, {v.Escravo + 1}, {string.Join(", ", Enumerable.Range(0, 6).Where(d => v.Graus[d]).Select(d => d + 1))})");
        }
        s.AppendLine();
        s.AppendLine("def caso(nome, cargas_nos, cargas_trechos):");
        s.AppendLine("    modelo()");
        s.AppendLine("    ops.timeSeries('Linear', 1)");
        s.AppendLine("    ops.pattern('Plain', 1, 1)");
        s.AppendLine("    for n, f in cargas_nos: ops.load(n, *f)");
        s.AppendLine("    for e, (qx, qy, qz) in cargas_trechos: ops.eleLoad('-ele', e, '-type', '-beamUniform', qy, qz, qx)");
        s.AppendLine("    ops.constraints('Transformation'); ops.numberer('RCM'); ops.system('UmfPack')");
        s.AppendLine("    ops.test('NormDispIncr', 1e-10, 10); ops.algorithm('Linear'); ops.integrator('LoadControl', 1.0); ops.analysis('Static')");
        s.AppendLine("    if ops.analyze(1) != 0: raise RuntimeError('análise do caso ' + nome + ' falhou')");
        s.AppendLine("    ops.reactions()");
        s.AppendLine($"    nos = range(1, {p.Nos.Count + 1})");
        s.AppendLine("    r = {'caso': nome, 'deslocamentos': {n: ops.nodeDisp(n) for n in nos}, 'reacoes': {n: ops.nodeReaction(n) for n in nos},");
        s.AppendLine($"         'esforcos': {{e: ops.eleResponse(e, 'localForce') for e in range(1, {p.Trechos.Count + 1})}}}}");
        s.AppendLine("    with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'resultados_' + nome + '.json'), 'w') as f: json.dump(r, f)");
        s.AppendLine("    return r");
        s.AppendLine();
        s.AppendLine("if __name__ == '__main__':");
        if (p.Casos.Count == 0) s.AppendLine("    modelo()");
        foreach (var (c, uni, nod) in p.Casos)
        {
            string nos = string.Join(", ", nod.Select(kv => $"({kv.Key + 1}, [{string.Join(", ", kv.Value.Select(N))}])"));
            string tre = string.Join(", ", uni.Select(kv => $"({kv.Key + 1}, ({N(kv.Value.X)}, {N(kv.Value.Y)}, {N(kv.Value.Z)}))"));
            s.AppendLine($"    caso({Py(c.Nome)}, [{nos}], [{tre}])");
        }
        return s.ToString();
    }

    private static string Py(string t) => "'" + (t ?? "").Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    // ------------------------------------------------------------------ CalculiX

    /// <summary>
    /// Arquivo .inp do CalculiX: vigas B32R com seção retangular equivalente (mesmas Iy e Iz; a área e a torção ficam aproximadas —
    /// o CalculiX só tem seções RECT, CIRC, PIPE e BOX), molas SPRING2 nas translações dos aparelhos, vínculos rígidos por vigas muito
    /// rígidas e rótulas das pontas por um nó duplicado ligado só nas translações (*EQUATION: solta as três rotações). Um *STEP por caso.
    /// Cargas transversais uniformes: *DLOAD P1/P2, que no CalculiX é pressão na face (dividida aqui pela largura da face).
    /// </summary>
    public static string CalculiX(ModeloBarras m, string titulo = "")
    {
        var p = Preparar(m);
        var s = new StringBuilder();
        s.AppendLine($"** Modelo de análise exportado pelo PCEB (D50) — {titulo}");
        s.AppendLine("** Unidades: kN, cm (E em kN/cm2). Seção retangular equivalente nas inércias; rótulas soltam as três rotações da ponta.");
        var nos = new List<Vetor3>(p.Nos);
        var eqs = new List<(int Escravo, int Mestre)>();
        // rótulas: nó novo na ponta liberada
        var pontas = new Dictionary<(int Trecho, bool Inicio), int>();
        for (int i = 0; i < p.Trechos.Count; i++)
        {
            var t = p.Trechos[i];
            if (t.LibI.Skip(3).Any(x => x)) { nos.Add(nos[t.I]); pontas[(i, true)] = nos.Count - 1; eqs.Add((nos.Count - 1, t.I)); }
            if (t.LibJ.Skip(3).Any(x => x)) { nos.Add(nos[t.J]); pontas[(i, false)] = nos.Count - 1; eqs.Add((nos.Count - 1, t.J)); }
        }
        // cada trecho em elementos quadráticos B32R (o B31 linear, um por trecho, erra muito na flexão): nós intermediários
        var cadeia = new Dictionary<int, List<int>>();
        for (int i = 0; i < p.Trechos.Count; i++)
        {
            var t = p.Trechos[i];
            int ni = pontas.TryGetValue((i, true), out int a1) ? a1 : t.I, nj = pontas.TryGetValue((i, false), out int a2) ? a2 : t.J;
            int nd = Math.Max(2, (int)Math.Ceiling(t.L / 50.0));
            var lista = new List<int> { ni };
            for (int k = 1; k < 2 * nd; k++) { nos.Add(nos[t.I] + t.X * (t.L * k / (2.0 * nd))); lista.Add(nos.Count - 1); }
            lista.Add(nj);
            cadeia[i] = lista;
        }
        var rigidos = new List<(int A, int Meio, int B)>();
        foreach (var v in m.Vinculos)
            if ((nos[v.Mestre] - nos[v.Escravo]).Modulo >= 1e-6) { nos.Add((nos[v.Mestre] + nos[v.Escravo]) * 0.5); rigidos.Add((v.Mestre, nos.Count - 1, v.Escravo)); }
        s.AppendLine("*NODE, NSET=NTODOS");
        for (int i = 0; i < nos.Count; i++) s.AppendLine($"{i + 1}, {N(nos[i].X)}, {N(nos[i].Y)}, {N(nos[i].Z)}");
        int el = 0;
        var secoes = new StringBuilder();
        var larguras = new Dictionary<int, (double By, double Hz)>();
        for (int i = 0; i < p.Trechos.Count; i++)
        {
            var t = p.Trechos[i]; var b = m.Barras.First(x => x.Id == t.Barra);
            s.AppendLine($"*ELEMENT, TYPE=B32R, ELSET=T{i + 1}");
            var ca = cadeia[i];
            for (int k = 0; k + 2 < ca.Count; k += 2) s.AppendLine($"{++el}, {ca[k] + 1}, {ca[k + 1] + 1}, {ca[k + 2] + 1}");
            double iy = Math.Max(1e-9, b.Secao.Iy * b.FatorFlexao), iz = Math.Max(1e-9, b.Secao.Iz * b.FatorFlexao);
            // retângulo com as mesmas inércias: h (direção z local) e b (direção y local)
            double r = Math.Sqrt(iy / iz), by = Math.Pow(12 * iy / Math.Pow(r, 3), 0.25), hz = r * by;
            larguras[i] = (by, hz);
            secoes.AppendLine($"*MATERIAL, NAME=M{i + 1}");
            secoes.AppendLine("*ELASTIC");
            secoes.AppendLine($"{N(b.Material.E)}, {D(Math.Max(0, Math.Min(0.49, b.Material.E / (2 * Math.Max(1e-9, b.Material.G)) - 1)))}");
            secoes.AppendLine($"*BEAM SECTION, ELSET=T{i + 1}, MATERIAL=M{i + 1}, SECTION=RECT");
            secoes.AppendLine($"{N(by)}, {N(hz)}");
            secoes.AppendLine($"{N(t.Y.X)}, {N(t.Y.Y)}, {N(t.Y.Z)}");
        }
        int rig = 0;
        foreach (var v in m.Vinculos.Where(v => (nos[v.Mestre] - nos[v.Escravo]).Modulo < 1e-6)) eqs.Add((v.Escravo, v.Mestre));
        foreach (var (va, vm, vb) in rigidos)
        {
            Vetor3 x = (nos[vb] - nos[va]).Unitario, w = Math.Abs(x.Z) > 0.999 ? Vetor3.EixoX : Vetor3.EixoZ, y = w.Vetorial(x).Unitario;
            s.AppendLine($"*ELEMENT, TYPE=B32R, ELSET=RIG{++rig}");
            s.AppendLine($"{++el}, {va + 1}, {vm + 1}, {vb + 1}");
            secoes.AppendLine($"*BEAM SECTION, ELSET=RIG{rig}, MATERIAL=RIGIDO, SECTION=RECT");
            secoes.AppendLine("50, 50");
            secoes.AppendLine($"{N(y.X)}, {N(y.Y)}, {N(y.Z)}");
        }
        if (rig > 0) { secoes.AppendLine("*MATERIAL, NAME=RIGIDO"); secoes.AppendLine("*ELASTIC"); secoes.AppendLine("1E+06, 0.2"); }
        // molas dos aparelhos: SPRING2 em cada translação com rigidez (no sistema global; aparelho com eixo próprio: conferir)
        int ms = 0;
        foreach (var mo in m.Molas)
            for (int d = 0; d < 3; d++)
            {
                if (mo.K[d] <= 0) continue;
                s.AppendLine($"*ELEMENT, TYPE=SPRING2, ELSET=MOLA{++ms}");
                s.AppendLine($"{++el}, {mo.NoI + 1}, {mo.NoJ + 1}");
                secoes.AppendLine($"*SPRING, ELSET=MOLA{ms}");
                secoes.AppendLine($"{d + 1}, {d + 1}");
                secoes.AppendLine(N(mo.K[d]));
            }
        foreach (var a in m.Apoios)
            for (int d = 0; d < 3; d++)
            {
                if (a.Travado[d] || a.Mola[d] <= 0) continue;
                s.AppendLine($"*ELEMENT, TYPE=SPRING1, ELSET=APM{++ms}");
                s.AppendLine($"{++el}, {a.No + 1}");
                secoes.AppendLine($"*SPRING, ELSET=APM{ms}");
                secoes.AppendLine($"{d + 1}");
                secoes.AppendLine(N(a.Mola[d]));
            }
        s.Append(secoes);
        foreach (var (e, mstr) in eqs)
            for (int d = 1; d <= 3; d++) { s.AppendLine("*EQUATION"); s.AppendLine("2"); s.AppendLine($"{e + 1}, {d}, 1., {mstr + 1}, {d}, -1."); }
        s.AppendLine("*BOUNDARY");
        foreach (var a in m.Apoios) for (int d = 0; d < 6; d++) if (a.Travado[d]) s.AppendLine($"{a.No + 1}, {d + 1}, {d + 1}");
        foreach (var (c, uni, nod) in p.Casos)
        {
            s.AppendLine($"** caso {c.Nome}");
            s.AppendLine("*STEP");
            s.AppendLine("*STATIC");
            var cl = new Dictionary<(int, int), double>();
            foreach (var kv in nod) for (int d = 0; d < 6; d++) if (Math.Abs(kv.Value[d]) > 0) cl[(kv.Key, d)] = kv.Value[d];
            // axial uniforme: metade em cada nó (exato para a resposta nodal); transversais: *DLOAD P1 (y local) e P2 (z local)
            var dl = new StringBuilder();
            foreach (var kv in uni)
            {
                var t = p.Trechos[kv.Key]; var q = kv.Value;
                if (Math.Abs(q.X) > 0)
                    foreach (int no in new[] { t.I, t.J })
                        for (int d = 0; d < 3; d++)
                        {
                            double f = q.X * t.L / 2 * (d == 0 ? t.X.X : d == 1 ? t.X.Y : t.X.Z);
                            if (Math.Abs(f) > 0) cl[(no, d)] = (cl.TryGetValue((no, d), out double f0) ? f0 : 0) + f;
                        }
                string eid = "T" + (kv.Key + 1);   // o conjunto de elementos do trecho
                // no CalculiX, P1/P2 da viga são pressões na face: força por comprimento ÷ largura da face (P1: hz; P2: by)
                var (by, hz) = larguras[kv.Key];
                if (Math.Abs(q.Y) > 0) dl.AppendLine($"{eid}, P1, {N(q.Y / hz)}");
                if (Math.Abs(q.Z) > 0) dl.AppendLine($"{eid}, P2, {N(q.Z / by)}");
            }
            if (cl.Count > 0) { s.AppendLine("*CLOAD"); foreach (var kv in cl) s.AppendLine($"{kv.Key.Item1 + 1}, {kv.Key.Item2 + 1}, {N(kv.Value)}"); }
            if (dl.Length > 0) { s.AppendLine("*DLOAD"); s.Append(dl); }
            s.AppendLine("*NODE PRINT, NSET=NTODOS");
            s.AppendLine("U, RF");
            s.AppendLine("*NODE FILE");
            s.AppendLine("U, RF");
            s.AppendLine("*EL FILE");
            s.AppendLine("S");
            s.AppendLine("*END STEP");
        }
        return s.ToString();
    }

    // ------------------------------------------------------------------ SAP2000 (.s2k)

    /// <summary>
    /// Texto de tabelas do SAP2000 (.s2k, "File → Import → SAP2000 .s2k"): unidades kN, cm; seções gerais com A, I33, I22 e J;
    /// eixos locais do PCEB levados pelo ângulo (o z do PCEB vira o eixo 2 do SAP); rótulas; aparelhos como links lineares de dois nós;
    /// vínculos rígidos como restrições de corpo; um padrão de carga por caso.
    /// </summary>
    public static string Sap2000(ModeloBarras m, string titulo = "")
    {
        var p = Preparar(m);
        var s = new StringBuilder();
        string Q(string t) => "\"" + (t ?? "").Replace("\"", "'") + "\"";
        string Yes(bool b) => b ? "Yes" : "No";
        s.AppendLine($"$ Modelo de análise exportado pelo PCEB (D50) — {titulo}");
        s.AppendLine("$ Unidades kN, cm. Barras divididas nos pontos de carga. Confira o relatório de importação do SAP2000.");
        s.AppendLine();
        s.AppendLine("TABLE:  \"PROGRAM CONTROL\"");
        s.AppendLine("   ProgramName=SAP2000   Version=24.0.0   CurrUnits=\"KN, cm, C\"   SteelCode=\"AISC 360-16\"   ConcCode=\"ACI 318-19\"");
        s.AppendLine();
        // materiais e seções (uma por combinação distinta)
        var mats = new Dictionary<MaterialBarra, string>();
        foreach (var b in m.Barras) if (!mats.ContainsKey(b.Material)) mats[b.Material] = "MAT" + (mats.Count + 1);
        s.AppendLine("TABLE:  \"MATERIAL PROPERTIES 01 - GENERAL\"");
        foreach (var kv in mats) s.AppendLine($"   Material={kv.Value}   Type=Other   SymType=Isotropic   TempDepend=No   Notes={Q(kv.Key.Descricao)}");
        s.AppendLine("   Material=RIGIDO   Type=Other   SymType=Isotropic   TempDepend=No");
        s.AppendLine();
        s.AppendLine("TABLE:  \"MATERIAL PROPERTIES 02 - BASIC MECHANICAL PROPERTIES\"");
        foreach (var kv in mats)
        {
            double nu = Math.Max(0, Math.Min(0.49, kv.Key.E / (2 * Math.Max(1e-9, kv.Key.G)) - 1));
            s.AppendLine($"   Material={kv.Value}   UnitWeight={N(kv.Key.Peso)}   UnitMass={N(kv.Key.Peso / 981)}   E1={N(kv.Key.E)}   G12={N(kv.Key.G)}   U12={D(nu)}   A1=1E-05");
        }
        s.AppendLine("   Material=RIGIDO   UnitWeight=0   UnitMass=0   E1=1E+08   G12=4E+07   U12=0.25   A1=0");
        s.AppendLine();
        var secs = new Dictionary<string, string>(); var secTxt = new StringBuilder();
        string Secao(BarraPortico b)
        {
            double iy = b.Secao.Iy * b.FatorFlexao, iz = b.Secao.Iz * b.FatorFlexao, j = b.Secao.J * b.FatorTorcao;
            string k = $"{mats[b.Material]}|{N(b.Secao.A)}|{N(iy)}|{N(iz)}|{N(j)}";
            if (secs.TryGetValue(k, out string n)) return n;
            n = "SEC" + (secs.Count + 1); secs[k] = n;
            double t3 = Math.Sqrt(12 * iy / Math.Max(1e-9, b.Secao.A)), t2 = Math.Sqrt(12 * iz / Math.Max(1e-9, b.Secao.A));
            secTxt.AppendLine($"   SectionName={n}   Material={mats[b.Material]}   Shape=General   t3={N(t3)}   t2={N(t2)}   Area={N(b.Secao.A)}   TorsConst={N(j)}   " +
                              $"I33={N(iy)}   I22={N(iz)}   AS2=0   AS3=0   Notes={Q(b.Secao.Descricao)}");
            return n;
        }
        var secDoTrecho = p.Trechos.Select(t => Secao(m.Barras.First(x => x.Id == t.Barra))).ToList();
        s.AppendLine("TABLE:  \"FRAME SECTION PROPERTIES 01 - GENERAL\"");
        s.Append(secTxt);
        s.AppendLine("   SectionName=RIGIDO   Material=RIGIDO   Shape=General   t3=50   t2=50   Area=2500   TorsConst=1E+06   I33=1E+06   I22=1E+06   AS2=0   AS3=0");
        s.AppendLine();
        s.AppendLine("TABLE:  \"JOINT COORDINATES\"");
        for (int i = 0; i < p.Nos.Count; i++)
            s.AppendLine($"   Joint={i + 1}   CoordSys=GLOBAL   CoordType=Cartesian   XorR={N(p.Nos[i].X)}   Y={N(p.Nos[i].Y)}   Z={N(p.Nos[i].Z)}   SpecialJt=No");
        s.AppendLine();
        s.AppendLine("TABLE:  \"CONNECTIVITY - FRAME\"");
        for (int i = 0; i < p.Trechos.Count; i++) s.AppendLine($"   Frame={i + 1}   JointI={p.Trechos[i].I + 1}   JointJ={p.Trechos[i].J + 1}   IsCurved=No");
        s.AppendLine();
        s.AppendLine("TABLE:  \"FRAME SECTION ASSIGNMENTS\"");
        for (int i = 0; i < p.Trechos.Count; i++) s.AppendLine($"   Frame={i + 1}   SectionType=General   AutoSelect=N.A.   AnalSect={secDoTrecho[i]}   MatProp=Default");
        s.AppendLine();
        // eixos: o eixo 2 padrão do SAP é o Z global projetado (barra vertical: o X global); o z do PCEB vira o 2 → ângulo em torno do 1
        s.AppendLine("TABLE:  \"FRAME LOCAL AXES ASSIGNMENTS 1 - TYPICAL\"");
        for (int i = 0; i < p.Trechos.Count; i++)
        {
            var t = p.Trechos[i];
            Vetor3 e2 = Math.Abs(t.X.Z) > 0.999 ? Vetor3.EixoX : Vetor3.EixoZ;
            e2 = (e2 - t.X * e2.Ponto(t.X)).Unitario;
            double ang = Math.Atan2(e2.Vetorial(t.Z).Ponto(t.X), e2.Ponto(t.Z)) * 180 / Math.PI;
            if (Math.Abs(ang) > 1e-6) s.AppendLine($"   Frame={i + 1}   Angle={D(ang)}   AdvanceAxes=No");
        }
        s.AppendLine();
        // rótulas: Vy→V3, Vz→V2, My→M3, Mz→M2 (y do PCEB = −3 do SAP, z = 2)
        var rot = Enumerable.Range(0, p.Trechos.Count).Where(i => p.Trechos[i].LibI.Any(x => x) || p.Trechos[i].LibJ.Any(x => x)).ToList();
        if (rot.Count > 0)
        {
            s.AppendLine("TABLE:  \"FRAME RELEASE ASSIGNMENTS 1 - GENERAL\"");
            foreach (int i in rot)
            {
                var a = p.Trechos[i].LibI; var b = p.Trechos[i].LibJ;
                s.AppendLine($"   Frame={i + 1}   PI={Yes(a[0])}   V2I={Yes(a[2])}   V3I={Yes(a[1])}   TI={Yes(a[3])}   M2I={Yes(a[5])}   M3I={Yes(a[4])}   " +
                             $"PJ={Yes(b[0])}   V2J={Yes(b[2])}   V3J={Yes(b[1])}   TJ={Yes(b[3])}   M2J={Yes(b[5])}   M3J={Yes(b[4])}   PartialFix=No");
            }
            s.AppendLine();
        }
        s.AppendLine("TABLE:  \"JOINT RESTRAINT ASSIGNMENTS\"");
        foreach (var a in m.Apoios.Where(a => a.Travado.Any(x => x)))
            s.AppendLine($"   Joint={a.No + 1}   U1={Yes(a.Travado[0])}   U2={Yes(a.Travado[1])}   U3={Yes(a.Travado[2])}   R1={Yes(a.Travado[3])}   R2={Yes(a.Travado[4])}   R3={Yes(a.Travado[5])}");
        s.AppendLine();
        var molasApoio = m.Apoios.Where(a => a.Mola.Any(k => k > 0)).ToList();
        if (molasApoio.Count > 0)
        {
            s.AppendLine("TABLE:  \"JOINT SPRING ASSIGNMENTS 1 - UNCOUPLED\"");
            foreach (var a in molasApoio)
                s.AppendLine($"   Joint={a.No + 1}   CoordSys=GLOBAL   U1={N(a.Mola[0])}   U2={N(a.Mola[1])}   U3={N(a.Mola[2])}   R1={N(a.Mola[3])}   R2={N(a.Mola[4])}   R3={N(a.Mola[5])}");
            s.AppendLine();
        }
        // aparelhos: links lineares
        if (m.Molas.Count > 0)
        {
            var props = new Dictionary<string, string>();
            var defs = new StringBuilder(); var lin = new StringBuilder(); var con = new StringBuilder(); var asg = new StringBuilder();
            int k = 0;
            foreach (var mo in m.Molas)
            {
                string chave = string.Join("|", mo.K.Select(N));
                if (!props.TryGetValue(chave, out string nome))
                {
                    nome = "APARELHO" + (props.Count + 1); props[chave] = nome;
                    defs.AppendLine($"   Link={nome}   LinkType=Linear   Mass=0   Weight=0   RotInert1=0   RotInert2=0   RotInert3=0   DefLength=1   DefArea=1   PDM2I=0.5   PDM3I=0.5");
                    string[] g = { "U1", "U2", "U3", "R1", "R2", "R3" };
                    for (int d = 0; d < 6; d++)
                        lin.AppendLine($"   Link={nome}   DOF={g[d]}   Fixed=No   TransKE={N(d < 3 ? mo.K[d] : 0)}   RotKE={N(d >= 3 ? mo.K[d] : 0)}   TransCE=0   RotCE=0   DJ2=0   DJ3=0");
                }
                con.AppendLine($"   Link={++k}   JointI={mo.NoI + 1}   JointJ={mo.NoJ + 1}");
                asg.AppendLine($"   Link={k}   LinkType=TwoJoint   LinkJoints=2   LinkProp={nome}");
            }
            s.AppendLine("TABLE:  \"LINK PROPERTY DEFINITIONS 01 - GENERAL\""); s.Append(defs); s.AppendLine();
            s.AppendLine("TABLE:  \"LINK PROPERTY DEFINITIONS 02 - LINEAR\""); s.Append(lin); s.AppendLine();
            s.AppendLine("TABLE:  \"CONNECTIVITY - LINK\""); s.Append(con); s.AppendLine();
            s.AppendLine("TABLE:  \"LINK PROPERTY ASSIGNMENTS\""); s.Append(asg); s.AppendLine();
        }
        if (m.Vinculos.Count > 0)
        {
            s.AppendLine("TABLE:  \"CONSTRAINT DEFINITIONS - BODY\"");
            for (int i = 0; i < m.Vinculos.Count; i++)
            {
                var g = m.Vinculos[i].Graus;
                s.AppendLine($"   Name=RIG{i + 1}   CoordSys=GLOBAL   UX={Yes(g[0])}   UY={Yes(g[1])}   UZ={Yes(g[2])}   RX={Yes(g[3])}   RY={Yes(g[4])}   RZ={Yes(g[5])}");
            }
            s.AppendLine();
            s.AppendLine("TABLE:  \"JOINT CONSTRAINT ASSIGNMENTS\"");
            for (int i = 0; i < m.Vinculos.Count; i++)
            {
                s.AppendLine($"   Joint={m.Vinculos[i].Mestre + 1}   Constraint=RIG{i + 1}   Type=Body");
                s.AppendLine($"   Joint={m.Vinculos[i].Escravo + 1}   Constraint=RIG{i + 1}   Type=Body");
            }
            s.AppendLine();
        }
        s.AppendLine("TABLE:  \"LOAD PATTERN DEFINITIONS\"");
        foreach (var (c, _, _) in p.Casos)
        {
            string tipo = c.Natureza switch { NaturezaCaso.PesoProprio => "Dead", NaturezaCaso.Permanente => "SuperDead", NaturezaCaso.Vento => "Wind", NaturezaCaso.Variavel => "Live", _ => "Other" };
            s.AppendLine($"   LoadPat={Q(c.Nome)}   DesignType={tipo}   SelfWtMult=0");
        }
        s.AppendLine();
        s.AppendLine("TABLE:  \"LOAD CASE DEFINITIONS\"");
        foreach (var (c, _, _) in p.Casos) s.AppendLine($"   Case={Q(c.Nome)}   Type=LinStatic   InitialCond=Zero   DesTypeOpt=\"Prog Det\"   RunCase=Yes");
        s.AppendLine();
        s.AppendLine("TABLE:  \"CASE - STATIC 1 - LOAD ASSIGNMENTS\"");
        foreach (var (c, _, _) in p.Casos) s.AppendLine($"   Case={Q(c.Nome)}   LoadType=\"Load pattern\"   LoadName={Q(c.Nome)}   LoadSF=1");
        s.AppendLine();
        s.AppendLine("TABLE:  \"FRAME LOADS - DISTRIBUTED\"");
        foreach (var (c, uni, _) in p.Casos)
            foreach (var kv in uni)
            {
                var t = p.Trechos[kv.Key]; Vetor3 g = t.X * kv.Value.X + t.Y * kv.Value.Y + t.Z * kv.Value.Z;   // de volta ao global
                foreach (var (dir, v) in new[] { ("X", g.X), ("Y", g.Y), ("Z", g.Z) })
                    if (Math.Abs(v) > 1e-12)
                        s.AppendLine($"   Frame={kv.Key + 1}   LoadPat={Q(c.Nome)}   CoordSys=GLOBAL   Type=Force   Dir={dir}   DistType=RelDist   RelDistA=0   RelDistB=1   " +
                                     $"AbsDistA=0   AbsDistB={N(t.L)}   FOverLA={N(v)}   FOverLB={N(v)}");
            }
        s.AppendLine();
        s.AppendLine("TABLE:  \"JOINT LOADS - FORCE\"");
        foreach (var (c, _, nod) in p.Casos)
            foreach (var kv in nod)
                s.AppendLine($"   Joint={kv.Key + 1}   LoadPat={Q(c.Nome)}   CoordSys=GLOBAL   F1={N(kv.Value[0])}   F2={N(kv.Value[1])}   F3={N(kv.Value[2])}   " +
                             $"M1={N(kv.Value[3])}   M2={N(kv.Value[4])}   M3={N(kv.Value[5])}");
        s.AppendLine();
        s.AppendLine("END TABLE DATA");
        return s.ToString();
    }
}
