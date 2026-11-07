using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DEB.Core.Motor.Json;
using DEB.Core.Persistencia;
// "Json" sozinho aqui resolveria para o namespace DEB.Core.Motor.Json: use o apelido.
using TextoJson = DEB.Core.Persistencia.Json;

namespace DEB.Core.Motor.Perfis;

/// <summary>Perfil obtido e o que aconteceu ao montá-lo.</summary>
public sealed class ResultadoPerfil
{
    /// <summary>Perfil pronto para o motor; null só se o Id não existe nem na fábrica nem na pasta.</summary>
    public PerfilDetalhamento Perfil { get; set; }
    /// <summary>Arquivo do usuário que foi aplicado (null = só fábrica).</summary>
    public string Arquivo { get; set; }
    public bool DeFabrica { get; set; }
    /// <summary>Avisos para o usuário (arquivo inválido, Id divergente…). Nunca há exceção para o chamador.</summary>
    public List<string> Avisos { get; } = new List<string>();
}

/// <summary>Um perfil disponível (para listas de escolha).</summary>
public sealed class InfoPerfil
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public string TipoElemento { get; set; } = "";
    public bool DeFabrica { get; set; }
    /// <summary>Arquivo do usuário na pasta (null = não há).</summary>
    public string Arquivo { get; set; }
    /// <summary>Problema ao ler o arquivo (null = ok).</summary>
    public string Aviso { get; set; }
}

/// <summary>
/// Perfis = fábrica (<see cref="PerfisFabrica"/>) + sobreposição pelo arquivo &lt;pasta&gt;/&lt;id&gt;.json.
/// Mesclagem: cada chave de "padroes" e "regras" do arquivo substitui a da fábrica (as demais ficam); "folha" é
/// mesclada campo a campo (a lista "vistas", se vier, substitui a de fábrica); "nome" substitui o nome. Arquivo inválido → fábrica + aviso.
/// Um arquivo sem perfil de fábrica com o mesmo Id vale sozinho (perfil do usuário).
/// </summary>
public sealed class RepositorioPerfis
{
    /// <summary>Pasta dos arquivos do usuário (pode não existir; null/vazia = só fábrica).</summary>
    public string Pasta { get; }

    public RepositorioPerfis(string pasta) { Pasta = string.IsNullOrWhiteSpace(pasta) ? null : pasta; }

    /// <summary>%AppData%/PCEB/Perfis (o que o CAD usa).</summary>
    public static string PastaPadrao() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCEB", "Perfis");

    /// <summary>Caminho do arquivo do perfil (existindo ou não); null se o Id não serve como nome de arquivo.</summary>
    public string CaminhoArquivo(string id)
    {
        if (Pasta == null || !IdValido(id)) return null;
        return Path.Combine(Pasta, id + ".json");
    }

    /// <summary>Id aceito como nome de arquivo: não vazio, sem separadores de pasta nem caracteres inválidos.</summary>
    public static bool IdValido(string id) =>
        !string.IsNullOrWhiteSpace(id) && id.Trim() == id && id != "." && id != ".." &&
        id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && id.IndexOf('/') < 0 && id.IndexOf('\\') < 0;

    /// <summary>Perfil por Id: fábrica + arquivo. Nunca lança exceção (problemas vão para Avisos).</summary>
    public ResultadoPerfil Obter(string id)
    {
        var r = new ResultadoPerfil();
        try
        {
            var fabrica = PerfisFabrica.Obter(id);
            r.Perfil = fabrica;
            r.DeFabrica = fabrica != null;
            var arq = CaminhoArquivo(id);
            if (arq == null)
            {
                if (Pasta != null && !IdValido(id)) r.Avisos.Add($"Id de perfil \"{id}\" inválido como nome de arquivo; usado só o perfil de fábrica.");
            }
            else if (File.Exists(arq))
            {
                try
                {
                    var doc = TextoJson.LerArquivo(arq);
                    var m = Mesclar(fabrica, doc, id, r.Avisos);
                    r.Perfil = m;
                    r.Arquivo = arq;
                }
                catch (Exception e) when (e is FormatException || e is IOException || e is UnauthorizedAccessException)
                {
                    r.Avisos.Add($"Perfil \"{id}\": o arquivo {arq} não foi usado ({e.Message})" +
                                 (fabrica != null ? "; valem os valores de fábrica." : "."));
                }
            }
            if (r.Perfil == null) r.Avisos.Add($"Perfil \"{id}\" não existe (fábrica: {string.Join(", ", PerfisFabrica.Ids)}).");
        }
        catch (Exception e)
        {
            // rede de segurança: nada sobe para o CAD
            r.Avisos.Add($"Perfil \"{id}\": erro inesperado ({e.Message}); valem os valores de fábrica.");
            try { r.Perfil = PerfisFabrica.Obter(id); r.DeFabrica = r.Perfil != null; r.Arquivo = null; } catch { r.Perfil = null; }
        }
        return r;
    }

    /// <summary>
    /// Aplica o documento do arquivo sobre a fábrica (que pode ser null: perfil só do usuário). Lança
    /// <see cref="FormatException"/> se o documento for inválido (o chamador decide; <see cref="Obter"/> vira aviso).
    /// </summary>
    public static PerfilDetalhamento Mesclar(PerfilDetalhamento fabrica, ValorJson documento, string id, List<string> avisos)
    {
        SerializacaoMotor.ConferirVersao(documento, "perfil");
        var doArquivo = SerializacaoMotor.PerfilDeJson(documento, "perfil");   // valida tudo antes de mesclar
        var idArq = documento["id"]?.Texto;
        if (!string.IsNullOrEmpty(idArq) && idArq != id)
            avisos?.Add($"Perfil \"{id}\": o arquivo diz \"id\": \"{idArq}\"; vale o nome do arquivo (\"{id}\").");

        if (fabrica == null)
        {
            doArquivo.Id = id;
            return doArquivo;
        }

        var r = fabrica;   // PerfisFabrica.Obter já devolve cópia nova
        r.Id = id;
        if (documento.Tem("nome") && !documento["nome"].EhNulo) r.Nome = doArquivo.Nome;
        if (documento.Tem("tipoElemento") && !documento["tipoElemento"].EhNulo && doArquivo.TipoElemento != fabrica.TipoElemento)
            avisos?.Add($"Perfil \"{id}\": \"tipoElemento\": \"{doArquivo.TipoElemento}\" difere do de fábrica (\"{fabrica.TipoElemento}\"); mantido o de fábrica.");
        if (documento.Tem("padroes") && !documento["padroes"].EhNulo)
            foreach (var kv in doArquivo.Padroes) r.Padroes[kv.Key] = kv.Value;
        if (documento.Tem("regras") && !documento["regras"].EhNulo)
            foreach (var kv in doArquivo.Regras) r.Regras[kv.Key] = kv.Value;
        // folha campo a campo (decisão de 2026-11-07: mudar só a escala sem copiar a folha inteira); "vistas", se vier, substitui a lista
        if (documento.Tem("folha") && !documento["folha"].EhNulo)
            r.Folha = DEB.Core.Motor.Json.SerializacaoMotor.FolhaDeJson(documento["folha"], "folha", fabrica.Folha);
        return r;
    }

    /// <summary>
    /// Grava o perfil de fábrica em &lt;pasta&gt;/&lt;id&gt;.json para o usuário editar. Não sobrescreve um arquivo
    /// existente, a não ser que <paramref name="sobrescrever"/>. Devolve false e a mensagem em caso de problema.
    /// </summary>
    public bool ExportarFabrica(string id, out string caminho, out string erro, bool sobrescrever = false)
    {
        caminho = CaminhoArquivo(id);
        erro = null;
        var p = PerfisFabrica.Obter(id);
        if (p == null) { erro = $"Não há perfil de fábrica \"{id}\"."; return false; }
        if (caminho == null) { erro = Pasta == null ? "Pasta de perfis não definida." : $"Id \"{id}\" inválido como nome de arquivo."; return false; }
        try
        {
            if (File.Exists(caminho) && !sobrescrever) { erro = $"O arquivo {caminho} já existe (não foi sobrescrito)."; return false; }
            TextoJson.GravarArquivo(caminho, SerializacaoMotor.Documento(SerializacaoMotor.ParaJson(p)));
            return true;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is ArgumentException)
        {
            erro = $"Não foi possível gravar {caminho}: {e.Message}";
            return false;
        }
    }

    /// <summary>Perfis disponíveis: os de fábrica (com o arquivo, se houver) e os arquivos *.json da pasta.</summary>
    public List<InfoPerfil> Listar()
    {
        var lista = new List<InfoPerfil>();
        foreach (var p in PerfisFabrica.Todos())
            lista.Add(new InfoPerfil { Id = p.Id, Nome = p.Nome, TipoElemento = p.TipoElemento, DeFabrica = true });
        string[] arquivos;
        try { arquivos = Pasta != null && Directory.Exists(Pasta) ? Directory.GetFiles(Pasta, "*.json") : new string[0]; }
        catch (Exception) { arquivos = new string[0]; }
        foreach (var arq in arquivos.OrderBy(a => a, StringComparer.OrdinalIgnoreCase))
        {
            var id = Path.GetFileNameWithoutExtension(arq);
            var info = lista.FirstOrDefault(i => i.Id == id);
            if (info == null) { info = new InfoPerfil { Id = id }; lista.Add(info); }
            info.Arquivo = arq;
            var r = Obter(id);
            if (r.Perfil != null) { info.Nome = r.Perfil.Nome; info.TipoElemento = r.Perfil.TipoElemento; }
            if (r.Avisos.Count > 0) info.Aviso = string.Join(" ", r.Avisos);
        }
        return lista;
    }
}
