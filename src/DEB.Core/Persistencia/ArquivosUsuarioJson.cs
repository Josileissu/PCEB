using System;
using System.IO;
using System.Text;

namespace DEB.Core.Persistencia;

/// <summary>
/// O que os repositórios "fábrica + arquivo do usuário" (perfis, receitas) têm em comum: pasta em %AppData%/PCEB,
/// arquivo &lt;pasta&gt;/&lt;id&gt;.json, leitura que vira motivo de aviso e exportação sem sobrescrever.
/// </summary>
internal static class ArquivosUsuarioJson
{
    /// <summary>%AppData%/PCEB/&lt;subpasta&gt;.</summary>
    public static string PastaPadrao(string subpasta) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCEB", subpasta);

    /// <summary>Id aceito como nome de arquivo: não vazio, sem separadores de pasta nem caracteres inválidos.</summary>
    public static bool IdValido(string id) =>
        !string.IsNullOrWhiteSpace(id) && id.Trim() == id && id != "." && id != ".." &&
        id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && id.IndexOf('/') < 0 && id.IndexOf('\\') < 0;

    /// <summary>&lt;pasta&gt;/&lt;id&gt;.json (existindo ou não); null se não há pasta ou o Id não serve como nome de arquivo.</summary>
    public static string Caminho(string pasta, string id) =>
        pasta == null || !IdValido(id) ? null : Path.Combine(pasta, id + ".json");

    /// <summary>Lê o arquivo (UTF-8) e interpreta com <paramref name="ler"/>. Erro de formato ou de acesso → default e
    /// <paramref name="motivo"/> com a mensagem (o repositório transforma em aviso).</summary>
    public static T TentarLer<T>(string arquivo, Func<string, T> ler, out string motivo) where T : class
    {
        motivo = null;
        try { return ler(File.ReadAllText(arquivo, Encoding.UTF8)); }
        catch (Exception e) when (e is FormatException || e is IOException || e is UnauthorizedAccessException)
        {
            motivo = e.Message;
            return null;
        }
    }

    /// <summary>Grava o documento em <paramref name="caminho"/> (não sobrescreve sem <paramref name="sobrescrever"/>).</summary>
    public static bool Gravar(string caminho, Func<ValorJson> documento, bool sobrescrever, out string erro)
    {
        erro = null;
        try
        {
            if (File.Exists(caminho) && !sobrescrever) { erro = $"O arquivo {caminho} já existe (não foi sobrescrito)."; return false; }
            Json.GravarArquivo(caminho, documento());
            return true;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is ArgumentException)
        {
            erro = $"Não foi possível gravar {caminho}: {e.Message}";
            return false;
        }
    }
}
