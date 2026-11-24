using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DEB.Core.Analise.Mef;

/// <summary>Pórtico plano (x horizontal, z vertical) para o OpenSees: nós, barras elásticas, apoios e casos de carga nodal. Unidades: cm, kN.</summary>
public sealed class Portico2D
{
    public List<(double X, double Z)> Nos { get; } = new List<(double, double)>();
    /// <summary>Barras: nós (índices de <see cref="Nos"/>), E (kN/cm²), A (cm²), I (cm⁴).</summary>
    public List<(int I, int J, double E, double A, double In)> Barras { get; } = new List<(int, int, double, double, double)>();
    /// <summary>Apoios: nó e graus travados (x, z, rotação).</summary>
    public List<(int No, bool Ux, bool Uz, bool Rot)> Apoios { get; } = new List<(int, bool, bool, bool)>();
    /// <summary>Casos de carga: nome → forças nodais (nó, Fx, Fz em kN; z para cima).</summary>
    public Dictionary<string, List<(int No, double Fx, double Fz)>> Casos { get; } = new Dictionary<string, List<(int, double, double)>>();

    /// <summary>Script do openseespy: um modelo por caso, grava deslocamentos (D), reações (R) e forças de extremidade das barras (F) em CSV.</summary>
    public string ScriptOpenSees(string arquivoSaida)
    {
        var ci = CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("R", ci);
        var sb = new StringBuilder();
        sb.AppendLine("# Gerado pelo PCEB (DEB.Core/Analise/Mef/Portico2D) — conferência por MEF");
        sb.AppendLine("import openseespy.opensees as ops");
        sb.AppendLine($"saida = open(r'{arquivoSaida}', 'w')");
        sb.AppendLine("def modelo():");
        sb.AppendLine("    ops.wipe(); ops.model('basic', '-ndm', 2, '-ndf', 3)");
        for (int i = 0; i < Nos.Count; i++) sb.AppendLine($"    ops.node({i + 1}, {F(Nos[i].X)}, {F(Nos[i].Z)})");
        foreach (var a in Apoios) sb.AppendLine($"    ops.fix({a.No + 1}, {(a.Ux ? 1 : 0)}, {(a.Uz ? 1 : 0)}, {(a.Rot ? 1 : 0)})");
        sb.AppendLine("    ops.geomTransf('Linear', 1)");
        for (int k = 0; k < Barras.Count; k++)
        {
            var b = Barras[k];
            sb.AppendLine($"    ops.element('elasticBeamColumn', {k + 1}, {b.I + 1}, {b.J + 1}, {F(b.A)}, {F(b.E)}, {F(b.In)}, 1)");
        }
        sb.AppendLine("def resolver(caso, cargas):");
        sb.AppendLine("    modelo()");
        sb.AppendLine("    ops.timeSeries('Linear', 1); ops.pattern('Plain', 1, 1)");
        sb.AppendLine("    for (n, fx, fz) in cargas: ops.load(n, fx, fz, 0.0)");
        sb.AppendLine("    ops.system('BandGeneral'); ops.numberer('RCM'); ops.constraints('Plain')");
        sb.AppendLine("    ops.integrator('LoadControl', 1.0); ops.algorithm('Linear'); ops.analysis('Static')");
        sb.AppendLine("    if ops.analyze(1) != 0: saida.write(f'ERRO,{caso}\\n'); return");
        sb.AppendLine("    ops.reactions()");
        sb.AppendLine($"    for n in range(1, {Nos.Count + 1}):");
        sb.AppendLine("        u = ops.nodeDisp(n); saida.write(f'D,{caso},{n},{u[0]!r},{u[1]!r},{u[2]!r}\\n')");
        foreach (var a in Apoios)
            sb.AppendLine($"    r = ops.nodeReaction({a.No + 1}); saida.write(f'R,{{caso}},{a.No + 1},{{r[0]!r}},{{r[1]!r}},{{r[2]!r}}\\n')");
        sb.AppendLine($"    for e in range(1, {Barras.Count + 1}):");
        sb.AppendLine("        f = ops.eleResponse(e, 'localForce'); saida.write('F,' + caso + ',' + str(e) + ',' + ','.join(repr(x) for x in f) + '\\n')");
        foreach (var c in Casos)
        {
            string lista = string.Join(", ", c.Value.Select(l => $"({l.No + 1}, {F(l.Fx)}, {F(l.Fz)})"));
            sb.AppendLine($"resolver('{c.Key}', [{lista}])");
        }
        sb.AppendLine("saida.write('FIM\\n'); saida.close()");
        return sb.ToString();
    }

    /// <summary>Roda no OpenSees e lê o resultado. Sem o Python com o openseespy, devolve o erro.</summary>
    public ResultadoPortico2D RodarOpenSees(FerramentasMef ferr = null, string pasta = null)
    {
        ferr ??= FerramentasMef.Padrao;
        var res = new ResultadoPortico2D();
        if (!ferr.TemOpenSees) { res.Erro = "OpenSees indisponível: falta o Python 3.12 com o openseespy (variável PCEB_PYTHON_OPENSEES)."; return res; }
        pasta ??= FerramentasMef.NovaPasta("opensees");
        string csv = Path.Combine(pasta, "resultado.csv"), py = Path.Combine(pasta, "modelo.py");
        File.WriteAllText(py, ScriptOpenSees(csv), new UTF8Encoding(false));
        var (cod, saida) = ferr.Rodar(ferr.Python, (ferr.PythonArgs + " \"" + py + "\"").Trim(), pasta);
        res.Pasta = pasta; res.Log = saida;
        if (cod != 0 || !File.Exists(csv)) { res.Erro = $"OpenSees terminou com código {cod}: {Ultimas(saida)}"; return res; }
        LerCsv(File.ReadAllLines(csv), res);
        if (!res.Completo) res.Erro = "Resultado do OpenSees incompleto.";
        return res;
    }

    public static void LerCsv(IEnumerable<string> linhas, ResultadoPortico2D res)
    {
        var ci = CultureInfo.InvariantCulture;
        foreach (string l in linhas)
        {
            var p = l.Split(',');
            if (p[0] == "FIM") { res.Completo = true; continue; }
            if (p[0] == "ERRO") { res.Erro = "OpenSees não convergiu no caso " + p[1]; continue; }
            if (p.Length < 6) continue;
            var c = res.Caso(p[1]);
            int n = int.Parse(p[2], ci) - 1;
            double[] v = p.Skip(3).Select(x => double.Parse(x, ci)).ToArray();
            switch (p[0])
            {
                case "D": c.Deslocamentos[n] = (v[0], v[1], v[2]); break;
                case "R": c.Reacoes[n] = (v[0], v[1], v[2]); break;
                case "F": c.Forcas[n] = v; break;
            }
        }
    }

    private static string Ultimas(string s) => string.Join(" | ", (s ?? "").Split('\n').Where(x => x.Trim().Length > 0).Reverse().Take(3).Reverse());
}

public sealed class CasoPortico2D
{
    public Dictionary<int, (double Ux, double Uz, double Rot)> Deslocamentos { get; } = new Dictionary<int, (double, double, double)>();
    public Dictionary<int, (double Fx, double Fz, double M)> Reacoes { get; } = new Dictionary<int, (double, double, double)>();
    /// <summary>Forças de extremidade no sistema local da barra (sobre a barra): N1, V1, M1, N2, V2, M2.</summary>
    public Dictionary<int, double[]> Forcas { get; } = new Dictionary<int, double[]>();

    /// <summary>Esforços internos na extremidade j da barra (convenção de viga: N tração +, V que gira no sentido horário +,
    /// M que traciona a face de baixo (−y local) +).</summary>
    public (double N, double V, double M) InternoJ(int barra) { var f = Forcas[barra]; return (f[3], -f[4], f[5]); }
    public (double N, double V, double M) InternoI(int barra) { var f = Forcas[barra]; return (-f[0], f[1], -f[2]); }
}

public sealed class ResultadoPortico2D
{
    public Dictionary<string, CasoPortico2D> Casos { get; } = new Dictionary<string, CasoPortico2D>();
    public string Erro { get; set; }
    public string Pasta { get; set; }
    public string Log { get; set; }
    public bool Completo { get; set; }
    public bool Ok => Erro == null && Completo;
    public CasoPortico2D Caso(string nome)
    {
        if (!Casos.TryGetValue(nome, out var c)) Casos[nome] = c = new CasoPortico2D();
        return c;
    }
}
