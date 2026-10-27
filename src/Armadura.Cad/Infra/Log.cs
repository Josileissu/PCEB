using System;
using System.IO;
using System.Text;

namespace Armadura.Cad.Infra;

/// <summary>Log em %AppData%\ArmaduraBR\logs\armadura_AAAAMMDD.log. Nunca lança exceção.</summary>
internal static class Log
{
    public static string PastaDados =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ArmaduraBR");

    public static string PastaLogs => Path.Combine(PastaDados, "logs");

    private static readonly object Trava = new object();

    public static void Info(string msg) => Escrever("INFO", msg);

    public static void Erro(string contexto, Exception ex) => Escrever("ERRO", contexto + ": " + ex);

    private static void Escrever(string nivel, string msg)
    {
        try
        {
            lock (Trava)
            {
                Directory.CreateDirectory(PastaLogs);
                var arq = Path.Combine(PastaLogs, $"armadura_{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(arq, $"{DateTime.Now:HH:mm:ss.fff} [{nivel}] {msg}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch { /* log nunca derruba o CAD */ }
    }
}
