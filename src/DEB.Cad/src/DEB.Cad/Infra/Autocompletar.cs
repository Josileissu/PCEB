using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace DEB.Cad.Infra;

/// <summary>
/// GstarCAD: as sugestões da linha de comando (AUTOCOMPLETE) vêm de uma lista fixa de nomes em
/// %AppData%\Gstarsoft\GstarCAD\R21\&lt;idioma&gt;\Support\alias.pgc — comandos de plugins .NET não
/// entram sozinhos. Ao carregar, o plugin acrescenta os seus comandos que faltarem (backup na 1ª vez).
/// A lista é lida quando o GstarCAD abre: as sugestões aparecem a partir da próxima abertura.
/// </summary>
internal static class Autocompletar
{
    /// <summary>Nomes dos comandos do plugin (sem os do autoteste).</summary>
    public static List<string> Comandos() =>
        typeof(Plugin).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .SelectMany(m => m.GetCustomAttributes(typeof(Rt.CommandMethodAttribute), false).Cast<Rt.CommandMethodAttribute>())
            .Select(a => a.GlobalName.ToUpperInvariant())
            .Where(n => !n.StartsWith("ARMTESTE", StringComparison.Ordinal))
            .Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>Arquivos de lista do perfil do usuário (um por idioma instalado).</summary>
    public static IEnumerable<string> Listas()
    {
        var raiz = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gstarsoft", "GstarCAD");
        if (!Directory.Exists(raiz)) yield break;
        foreach (var versao in Directory.GetDirectories(raiz))
            foreach (var idioma in Directory.GetDirectories(versao))
            {
                var f = Path.Combine(idioma, "Support", "alias.pgc");
                if (File.Exists(f)) yield return f;
            }
    }

    /// <summary>Acrescenta os comandos que faltarem. Retorna quantos nomes foram acrescentados no total.</summary>
    public static int Garantir()
    {
        int total = 0;
        var comandos = Comandos();
        foreach (var arq in Listas())
        {
            try
            {
                var texto = File.ReadAllText(arq, Encoding.UTF8);
                var existentes = new HashSet<string>(
                    texto.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim().Trim('"')),
                    StringComparer.OrdinalIgnoreCase);
                var faltam = comandos.Where(c => !existentes.Contains(c)).ToList();
                if (faltam.Count == 0) continue;
                var backup = arq + ".antes_armadura";
                if (!File.Exists(backup)) File.Copy(arq, backup);
                var sb = new StringBuilder(texto);
                if (!texto.EndsWith("\n")) sb.Append("\r\n");
                foreach (var c in faltam) sb.Append(c).Append("\r\n");
                File.WriteAllText(arq, sb.ToString(), new UTF8Encoding(false));
                total += faltam.Count;
                Log.Info($"Autocompletar: {faltam.Count} comando(s) acrescentado(s) em {arq}");
            }
            catch (Exception ex) { Log.Erro("Autocompletar " + arq, ex); }
        }
        return total;
    }
}
