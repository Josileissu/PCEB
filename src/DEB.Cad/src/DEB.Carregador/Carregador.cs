using System;
using System.IO;
using System.Linq;
using GrxCAD.ApplicationServices;
using CadApp = GrxCAD.ApplicationServices.Application;

[assembly: GrxCAD.Runtime.ExtensionApplication(typeof(DEB.Carregador.Carregador))]

namespace DEB.Carregador;

/// <summary>
/// Carregado na abertura do GstarCAD (registro Applications). Copia bin\GstarCAD2021\*.dll para
/// %LocalAppData%\PCEB\DEB.Cad\run\&lt;data da DLL&gt;\ e faz NETLOAD da cópia quando o CAD fica ocioso.
/// </summary>
public sealed class Carregador : GrxCAD.Runtime.IExtensionApplication
{
    private static string _alvo;

    public void Initialize()
    {
        try
        {
            var aqui = Path.GetDirectoryName(typeof(Carregador).Assembly.Location);
            var origem = Path.GetFullPath(Path.Combine(aqui, "..", "GstarCAD2021"));
            var dll = Path.Combine(origem, "DEB.GstarCAD.dll");
            if (!File.Exists(dll)) return;
            var raiz = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCEB", "DEB.Cad", "run");
            var destino = Path.Combine(raiz, File.GetLastWriteTime(dll).ToString("yyyyMMdd_HHmmss"));
            if (!Directory.Exists(destino))
            {
                Directory.CreateDirectory(destino);
                foreach (var f in Directory.GetFiles(origem, "*.dll")) File.Copy(f, Path.Combine(destino, Path.GetFileName(f)), true);
            }
            // limpa versões antigas (as em uso ficam presas e são ignoradas)
            foreach (var d in Directory.GetDirectories(raiz).Where(d => !string.Equals(d, destino, StringComparison.OrdinalIgnoreCase)))
                try { Directory.Delete(d, true); } catch { }
            _alvo = Path.Combine(destino, "DEB.GstarCAD.dll").Replace('\\', '/');
            CadApp.Idle += AoFicarOcioso;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    private static void AoFicarOcioso(object sender, EventArgs e)
    {
        var doc = CadApp.DocumentManager.MdiActiveDocument;
        if (doc == null || _alvo == null) return;
        CadApp.Idle -= AoFicarOcioso;
        doc.SendStringToExecute($"(command \"_.NETLOAD\" \"{_alvo}\") ", true, false, false);
    }

    public void Terminate() { }
}
