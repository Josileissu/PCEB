using System;
using System.IO;
using System.Text;

namespace DEB.Cad.Infra;

/// <summary>Log em %AppData%\PCEB\DEB.Cad\logs\deb_AAAAMMDD.log. Nunca lança exceção.</summary>
internal static class Log
{
    /// <summary>
    /// Dados do usuário (config, layers, templates, logs): %AppData%\PCEB\DEB.Cad. Na 1ª vez, copia o conteúdo da
    /// pasta do nome antigo (%AppData%\ArmaduraBR), que fica intacta.
    /// </summary>
    public static string PastaDados => _pastaDados.Value;

    private static readonly Lazy<string> _pastaDados = new Lazy<string>(() =>
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var nova = Path.Combine(appData, "PCEB", "DEB.Cad");
        var antiga = Path.Combine(appData, "ArmaduraBR");
        try
        {
            if (!Directory.Exists(nova) && Directory.Exists(antiga))
            {
                CopiarPasta(antiga, nova);
                File.WriteAllText(Path.Combine(nova, "migrado_de_ArmaduraBR.txt"),
                    $"Copiado de {antiga} em {DateTime.Now:dd/MM/yyyy HH:mm}. A pasta antiga pode ser apagada.", Encoding.UTF8);
            }
        }
        catch { /* migração nunca derruba o CAD; no pior caso começa com a configuração padrão */ }
        return nova;
    });

    private static void CopiarPasta(string origem, string destino)
    {
        Directory.CreateDirectory(destino);
        foreach (var f in Directory.GetFiles(origem))
            File.Copy(f, Path.Combine(destino, Path.GetFileName(f)), false);
        foreach (var d in Directory.GetDirectories(origem))
            CopiarPasta(d, Path.Combine(destino, Path.GetFileName(d)));
    }

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
                var arq = Path.Combine(PastaLogs, $"deb_{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(arq, $"{DateTime.Now:HH:mm:ss.fff} [{nivel}] {msg}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch { /* log nunca derruba o CAD */ }
    }
}
