using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using DEB.Core.Analise.Barras;
using Xunit;
using Xunit.Abstractions;

namespace DEB.Core.Tests;

/// <summary>
/// Exportação do modelo de barras (D50, item 8). Quando o OpenSees (openseespy) e o CalculiX estão instalados (caminhos em
/// %AppData%\PCEB\mef.txt), o modelo exportado roda neles e os deslocamentos são comparados com os do solver do PCEB.
/// </summary>
public class ExportacaoAnaliseTests
{
    private readonly ITestOutputHelper _saida;
    public ExportacaoAnaliseTests(ITestOutputHelper saida) => _saida = saida;

    private static readonly MaterialBarra Mat = new MaterialBarra { E = 3000, G = 1250, Peso = 25e-6 };
    private static readonly SecaoBarra Sec = new SecaoBarra { A = 20 * 50, Iy = 20 * Math.Pow(50, 3) / 12, Iz = 50 * Math.Pow(20, 3) / 12, J = 99805 };
    private static readonly SecaoBarra Pil = new SecaoBarra { A = 40 * 40, Iy = Math.Pow(40, 4) / 12, Iz = Math.Pow(40, 4) / 12, J = 0.141 * Math.Pow(40, 4) };

    /// <summary>Pilar engastado + viga com mola na ponta (aparelho), vínculo rígido com carga excêntrica, cargas parcial e concentrada.</summary>
    private static (ModeloBarras M, int Topo, int Ponta, int Meio) Modelo(bool rotula)
    {
        var m = new ModeloBarras();
        int b0 = m.No(0, 0, 0), t = m.No(0, 0, 300), j = m.No(500, 0, 300), k = m.NovoNo(500, 0, 300), r = m.No(0, 60, 300);
        m.Barra(b0, t, Pil, Mat, nome: "P1");
        var v = m.Barra(t, j, Sec, Mat, nome: "V1");
        if (rotula) { v.LiberaI[4] = true; v.LiberaI[5] = true; }
        m.Apoio(b0, ApoioNo.Engaste);
        m.Apoio(k, ApoioNo.Engaste);
        m.Mola(j, k, new double[] { 500, 800, 1500, 0, 0, 0 }, nome: "aparelho");
        m.Rigido(t, r, nome: "excentricidade");
        var c = m.Caso("G", NaturezaCaso.Permanente);
        c.PesoProprio = true;
        c.Distribuidas.Add(new CargaDistribuida { Barra = v.Id, Q = new Vetor3(0, 0, -0.05) });
        c.Distribuidas.Add(new CargaDistribuida { Barra = v.Id, Q = new Vetor3(0, 0.02, 0), A = 100, B = 300 });
        c.Pontuais.Add(new CargaPontualBarra { Barra = v.Id, F = new Vetor3(0, 0, -10), A = 250 });
        c.Nodais.Add((t, new double[] { 5, 0, 0, 0, 0, 0 }));
        c.Nodais.Add((r, new double[] { 0, 0, -3, 0, 0, 0 }));
        return (m, t, j, 0);
    }

    private static string Ferramenta(string chave)
    {
        string arq = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCEB", "mef.txt");
        if (!File.Exists(arq)) return null;
        string l = File.ReadAllLines(arq).FirstOrDefault(x => x.StartsWith(chave + "=", StringComparison.OrdinalIgnoreCase));
        string c = l?.Substring(chave.Length + 1).Trim();
        return c != null && File.Exists(c) ? c : null;
    }

    private static (int Codigo, string Saida) Rodar(string exe, string args, string pasta)
    {
        var psi = new ProcessStartInfo(exe, args) { WorkingDirectory = pasta, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi);
        string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(120000);
        return (p.ExitCode, o);
    }

    private static string Pasta()
    {
        string d = Path.Combine(Path.GetTempPath(), "pceb_export_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void Preparar_divide_as_barras_nos_pontos_de_carga_e_conserva_a_resultante()
    {
        var (m, _, _, _) = Modelo(true);
        var p = ExportacaoAnalise.Preparar(m);
        // a viga (500 cm) é dividida em 100, 250 e 300 → 4 trechos; o pilar fica inteiro
        Assert.Equal(5, p.Trechos.Count);
        Assert.Equal(m.Nos.Count + 3, p.Nos.Count);
        var (c, uni, nod) = p.Casos[0];
        // resultante vertical: peso próprio das duas barras + 0,05·500 + 10 + 3
        double fz = uni.Sum(kv => { var t = p.Trechos[kv.Key]; return (t.X * kv.Value.X + t.Y * kv.Value.Y + t.Z * kv.Value.Z).Z * t.L; }) + nod.Values.Sum(v => v[2]);
        Perto(-(25e-6 * (1000 * 500 + 1600 * 300) + 0.05 * 500 + 10 + 3), fz, 1e-9);
        double fy = uni.Sum(kv => { var t = p.Trechos[kv.Key]; return (t.X * kv.Value.X + t.Y * kv.Value.Y + t.Z * kv.Value.Z).Y * t.L; });
        Perto(0.02 * 200, fy, 1e-9);
        // a rótula fica só no primeiro trecho da viga
        var tv = p.TrechosDaBarra[1];
        Assert.True(p.Trechos[tv[0]].LibI[5]); Assert.False(p.Trechos[tv[1]].LibI[5]);
    }

    [Fact]
    public void Sap2000_tem_as_tabelas_do_modelo()
    {
        var (m, _, _, _) = Modelo(true);
        string s = ExportacaoAnalise.Sap2000(m, "teste");
        foreach (string t in new[] { "JOINT COORDINATES", "CONNECTIVITY - FRAME", "FRAME SECTION PROPERTIES 01 - GENERAL", "FRAME RELEASE ASSIGNMENTS 1 - GENERAL",
                                     "JOINT RESTRAINT ASSIGNMENTS", "LINK PROPERTY DEFINITIONS 02 - LINEAR", "CONSTRAINT DEFINITIONS - BODY", "LOAD PATTERN DEFINITIONS",
                                     "FRAME LOADS - DISTRIBUTED", "JOINT LOADS - FORCE", "END TABLE DATA" })
            Assert.Contains("TABLE:  \"" + t + "\"", s.Replace("END TABLE DATA", "TABLE:  \"END TABLE DATA\""));
        Assert.Equal(5, s.Split('\n').Count(l => l.TrimStart().StartsWith("Frame=") && l.Contains("JointI=")));
        Assert.DoesNotContain(",", s.Split('\n').Where(l => l.Contains("XorR=")).First());   // números com ponto
    }

    private static void Perto(double esperado, double obtido, double tolRel, double tolAbs = 1e-9) =>
        Assert.True(Math.Abs(obtido - esperado) <= Math.Max(tolAbs, tolRel * Math.Abs(esperado)), $"esperado {esperado}, obtido {obtido}");

    [Fact]
    public void OpenSees_reproduz_os_deslocamentos_do_PCEB()
    {
        string py = Ferramenta("python");
        if (py == null) { _saida.WriteLine("Python/openseespy não configurado: só gera o script."); }
        var (m, topo, ponta, _) = Modelo(true);
        string script = ExportacaoAnalise.OpenSees(m, "teste");
        Assert.Contains("ops.element('elasticBeamColumn'", script);
        if (py == null) return;
        string dir = Pasta();
        File.WriteAllText(Path.Combine(dir, "modelo.py"), script);
        var (cod, saida) = Rodar(py, "-I modelo.py", dir);
        Assert.True(cod == 0, saida);
        var r = DEB.Core.Persistencia.Json.Ler(File.ReadAllText(Path.Combine(dir, "resultados_G.json")));
        var pceb = new SolverBarras(m).Resolver()["G"];
        foreach (int no in new[] { topo, ponta })
            for (int d = 0; d < 6; d++)
            {
                double os = r["deslocamentos"][(no + 1).ToString(CultureInfo.InvariantCulture)][d].Numero;
                _saida.WriteLine($"nó {no} gdl {d}: PCEB {pceb.Deslocamentos[no][d]:E6}  OpenSees {os:E6}");
                Perto(pceb.Deslocamentos[no][d], os, 1e-5, 1e-9);
            }
    }

    [Fact]
    public void CalculiX_reproduz_os_deslocamentos_do_PCEB()
    {
        string ccx = Ferramenta("ccx");
        var (m, topo, ponta, _) = Modelo(false);
        string inp = ExportacaoAnalise.CalculiX(m, "teste");
        Assert.Contains("*ELEMENT, TYPE=B32R", inp);
        if (ccx == null) { _saida.WriteLine("CalculiX não configurado: só gera o .inp."); return; }
        string dir = Pasta();
        File.WriteAllText(Path.Combine(dir, "modelo.inp"), inp);
        var (cod, saida) = Rodar(ccx, "modelo", dir);
        string dat = Path.Combine(dir, "modelo.dat");
        Assert.True(File.Exists(dat), saida);
        var u = new System.Collections.Generic.Dictionary<int, double[]>();
        bool bloco = false;
        foreach (string l in File.ReadAllLines(dat))
        {
            if (l.Contains("displacements")) { bloco = true; continue; }
            if (l.Contains("forces")) bloco = false;
            var c = l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (bloco && c.Length == 4 && int.TryParse(c[0], out int n))
                u[n] = c.Skip(1).Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        }
        var pceb = new SolverBarras(m).Resolver()["G"];
        foreach (int no in new[] { topo, ponta })
            for (int d = 0; d < 3; d++)
            {
                _saida.WriteLine($"nó {no} gdl {d}: PCEB {pceb.Deslocamentos[no][d]:E6}  CalculiX {u[no + 1][d]:E6}");
                // seção retangular equivalente e viga expandida em sólido: diferença pequena (área, cisalhamento, torção)
                Perto(pceb.Deslocamentos[no][d], u[no + 1][d], 0.03, 1e-4);
            }
    }
}
