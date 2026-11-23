using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace DEB.Core.Analise.Mef;

/// <summary>
/// Programas externos de elementos finitos usados para CONFERIR os cálculos do núcleo (D42): OpenSees (openseespy no
/// Python 3.12) e CalculiX (ccx). Ficam fora do Git e do instalador; sem eles a conferência por MEF fica indisponível e o
/// cálculo continua. Caminhos: variáveis PCEB_PYTHON_OPENSEES (exe do Python com o openseespy) e PCEB_CCX (ccx_static.exe);
/// sem elas, procura `programas/calculix/**/ccx*.exe` subindo as pastas e o `py -3.12` do Windows.
/// </summary>
public sealed class FerramentasMef
{
    /// <summary>Executável do Python (ou "py") e os argumentos antes do script (ex.: "-3.12").</summary>
    public string Python { get; set; }
    public string PythonArgs { get; set; } = "";
    public string Ccx { get; set; }
    public int TempoLimiteSegundos { get; set; } = 180;

    public bool TemOpenSees => !string.IsNullOrEmpty(Python);
    public bool TemCalculix => !string.IsNullOrEmpty(Ccx) && File.Exists(Ccx);

    private static FerramentasMef _padrao;
    /// <summary>Procura uma vez por sessão.</summary>
    public static FerramentasMef Padrao => _padrao ??= Procurar();

    public static FerramentasMef Procurar(string pastaInicial = null)
    {
        var f = new FerramentasMef();
        string py = Environment.GetEnvironmentVariable("PCEB_PYTHON_OPENSEES");
        if (!string.IsNullOrEmpty(py) && File.Exists(py)) f.Python = py;
        else
        {
            // py launcher do Windows com o 3.12 (o openseespy do Windows só carrega no 3.12)
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string p312 = Path.Combine(local, "Programs", "Python", "Python312", "python.exe");
            if (File.Exists(p312)) f.Python = p312;
        }
        string ccx = Environment.GetEnvironmentVariable("PCEB_CCX");
        if (!string.IsNullOrEmpty(ccx) && File.Exists(ccx)) f.Ccx = ccx;
        else f.Ccx = ProcurarCcx(pastaInicial ?? AppContext.BaseDirectory);
        return f;
    }

    private static string ProcurarCcx(string inicio)
    {
        try
        {
            var d = new DirectoryInfo(inicio);
            for (int i = 0; d != null && i < 10; i++, d = d.Parent)
                foreach (string cand in new[] { Path.Combine(d.FullName, "programas", "calculix"), Path.Combine(d.FullName, "PCEB", "programas", "calculix") })
                {
                    if (!Directory.Exists(cand)) continue;
                    string exe = Directory.GetFiles(cand, "ccx_static.exe", SearchOption.AllDirectories).FirstOrDefault()
                                 ?? Directory.GetFiles(cand, "ccx*.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (exe != null) return exe;
                }
        }
        catch (Exception) { }
        string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCEB", "calculix", "ccx_static.exe");
        return File.Exists(local) ? local : null;
    }

    /// <summary>Roda um programa na pasta e devolve (código de saída, saída + erros). Estoura o tempo limite = mata e devolve -1.</summary>
    public (int Codigo, string Saida) Rodar(string exe, string args, string pasta)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            WorkingDirectory = pasta, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        var sb = new StringBuilder();
        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, a) => { if (a.Data != null) lock (sb) sb.AppendLine(a.Data); };
        p.ErrorDataReceived += (_, a) => { if (a.Data != null) lock (sb) sb.AppendLine(a.Data); };
        p.Start();
        p.BeginOutputReadLine(); p.BeginErrorReadLine();
        if (!p.WaitForExit(TempoLimiteSegundos * 1000))
        {
            try { p.Kill(); } catch (Exception) { }
            return (-1, sb + "\n(tempo limite estourado)");
        }
        p.WaitForExit();
        return (p.ExitCode, sb.ToString());
    }

    /// <summary>Pasta de trabalho nova em %TEMP%\PCEB_MEF\(prefixo)_(data).</summary>
    public static string NovaPasta(string prefixo)
    {
        string raiz = Path.Combine(Path.GetTempPath(), "PCEB_MEF");
        try
        {
            // as pastas de rodadas anteriores (mais de um dia) saem
            if (Directory.Exists(raiz))
                foreach (var v in new DirectoryInfo(raiz).GetDirectories().Where(x => x.CreationTime < DateTime.Now.AddDays(-1)))
                    v.Delete(true);
        }
        catch (Exception) { }
        string d = Path.Combine(raiz, prefixo + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
        Directory.CreateDirectory(d);
        return d;
    }
}
