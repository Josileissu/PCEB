using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DEB.Core.Motor.Perfis;
// "Json" sozinho aqui resolveria para o namespace DEB.Core.Motor.Json: use o apelido.
using TextoJson = DEB.Core.Persistencia.Json;

namespace DEB.Core.Motor.Receitas;

/// <summary>Receita obtida e o que aconteceu ao montá-la.</summary>
public sealed class ResultadoReceita
{
    /// <summary>Receita pronta; null só se não há de fábrica nem arquivo válido.</summary>
    public Receita Receita { get; set; }
    /// <summary>Arquivo do usuário usado (null = fábrica).</summary>
    public string Arquivo { get; set; }
    public bool DeFabrica { get; set; }
    /// <summary>Avisos para o usuário. Nunca há exceção para o chamador.</summary>
    public List<string> Avisos { get; } = new List<string>();
}

/// <summary>
/// Receitas = fábrica (registradas por <see cref="RegistrarFabrica"/>) + arquivo do usuário &lt;pasta&gt;/&lt;tipo&gt;.json.
/// O arquivo SUBSTITUI a receita de fábrica inteira (receita é programa: não se mescla). Arquivo inválido (JSON, campo
/// ou fórmula com erro de sintaxe) → vale a de fábrica + aviso com linha/coluna ou caminho e posição.
/// </summary>
public sealed class RepositorioReceitas
{
    private static readonly Dictionary<string, Func<Receita>> _fabrica = new Dictionary<string, Func<Receita>>(StringComparer.Ordinal);

    /// <summary>Registra a receita de fábrica de um tipo (a função é chamada a cada obtenção: devolva uma receita nova).
    /// O último registro de um tipo vale.</summary>
    public static void RegistrarFabrica(string tipo, Func<Receita> criar)
    {
        if (string.IsNullOrWhiteSpace(tipo)) throw new ArgumentException("Tipo vazio.", nameof(tipo));
        if (criar == null) throw new ArgumentNullException(nameof(criar));
        lock (_fabrica) _fabrica[tipo] = criar;
    }

    public static IReadOnlyList<string> TiposFabrica { get { lock (_fabrica) return _fabrica.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList(); } }

    /// <summary>Receita de fábrica (cópia nova) ou null.</summary>
    public static Receita ObterFabrica(string tipo)
    {
        Func<Receita> f;
        lock (_fabrica) if (tipo == null || !_fabrica.TryGetValue(tipo, out f)) return null;
        var r = f();
        if (r == null) return null;
        if (string.IsNullOrEmpty(r.TipoElemento)) r.TipoElemento = tipo;
        return r;
    }

    /// <summary>Pasta dos arquivos do usuário (null = só fábrica).</summary>
    public string Pasta { get; }

    public RepositorioReceitas(string pasta) { Pasta = string.IsNullOrWhiteSpace(pasta) ? null : pasta; }

    /// <summary>%AppData%/PCEB/Receitas.</summary>
    public static string PastaPadrao() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCEB", "Receitas");

    public string CaminhoArquivo(string tipo) =>
        Pasta == null || !RepositorioPerfis.IdValido(tipo) ? null : Path.Combine(Pasta, tipo + ".json");

    /// <summary>Receita do tipo: arquivo do usuário (se válido) ou fábrica. Nunca lança exceção.</summary>
    public ResultadoReceita Obter(string tipo)
    {
        var r = new ResultadoReceita();
        try
        {
            var fabrica = ObterFabrica(tipo);
            r.Receita = fabrica;
            r.DeFabrica = fabrica != null;
            var arq = CaminhoArquivo(tipo);
            if (arq == null && Pasta != null)
                r.Avisos.Add($"Tipo \"{tipo}\" inválido como nome de arquivo; usada só a receita de fábrica.");
            else if (arq != null && File.Exists(arq))
            {
                string motivo = null;
                Receita doArquivo = null;
                try
                {
                    doArquivo = Ler(File.ReadAllText(arq, System.Text.Encoding.UTF8), tipo, r.Avisos);
                }
                catch (Exception e) when (e is FormatException || e is IOException || e is UnauthorizedAccessException)
                {
                    motivo = e.Message;
                }
                if (doArquivo != null)
                {
                    r.Receita = doArquivo;
                    r.Arquivo = arq;
                    r.DeFabrica = false;
                }
                else
                    r.Avisos.Add($"Receita \"{tipo}\": o arquivo {arq} não foi usado ({motivo})" +
                                 (fabrica != null ? "; vale a receita de fábrica." : "; não há receita de fábrica."));
            }
            if (r.Receita == null)
                r.Avisos.Add($"Receita \"{tipo}\" não existe (fábrica: {(TiposFabrica.Count == 0 ? "nenhuma" : string.Join(", ", TiposFabrica))}).");
        }
        catch (Exception e)
        {
            r.Avisos.Add($"Receita \"{tipo}\": erro inesperado ({e.Message}); vale a de fábrica.");
            try { r.Receita = ObterFabrica(tipo); r.DeFabrica = r.Receita != null; r.Arquivo = null; } catch { r.Receita = null; }
        }
        return r;
    }

    /// <summary>Lê o texto do arquivo e confere a sintaxe de todas as fórmulas. Lança FormatException (ErroJson tem
    /// linha/coluna) se algo estiver errado; avisos menores (tipo divergente) vão para <paramref name="avisos"/>.</summary>
    public static Receita Ler(string texto, string tipo, List<string> avisos)
    {
        var rec = SerializacaoReceita.Ler(texto);
        var erros = SerializacaoReceita.ErrosDeSintaxe(rec);
        if (erros.Count > 0)
            throw new FormatException("fórmula com erro: " + string.Join("; ", erros.Take(5)) + (erros.Count > 5 ? $"; e mais {erros.Count - 5}" : ""));
        if (string.IsNullOrEmpty(rec.TipoElemento)) rec.TipoElemento = tipo;
        else if (rec.TipoElemento != tipo)
        {
            avisos?.Add($"Receita \"{tipo}\": o arquivo diz \"tipoElemento\": \"{rec.TipoElemento}\"; vale o nome do arquivo (\"{tipo}\").");
            rec.TipoElemento = tipo;
        }
        return rec;
    }

    /// <summary>Grava a receita de fábrica em &lt;pasta&gt;/&lt;tipo&gt;.json para o usuário editar (não sobrescreve sem
    /// <paramref name="sobrescrever"/>).</summary>
    public bool ExportarFabrica(string tipo, out string caminho, out string erro, bool sobrescrever = false)
    {
        caminho = CaminhoArquivo(tipo);
        erro = null;
        Receita rec;
        try { rec = ObterFabrica(tipo); }
        catch (Exception e) { erro = $"Receita de fábrica \"{tipo}\" com erro: {e.Message}"; return false; }
        if (rec == null) { erro = $"Não há receita de fábrica \"{tipo}\"."; return false; }
        if (caminho == null) { erro = Pasta == null ? "Pasta de receitas não definida." : $"Tipo \"{tipo}\" inválido como nome de arquivo."; return false; }
        try
        {
            if (File.Exists(caminho) && !sobrescrever) { erro = $"O arquivo {caminho} já existe (não foi sobrescrito)."; return false; }
            TextoJson.GravarArquivo(caminho, SerializacaoReceita.ParaJson(rec));
            return true;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is ArgumentException)
        {
            erro = $"Não foi possível gravar {caminho}: {e.Message}";
            return false;
        }
    }
}
