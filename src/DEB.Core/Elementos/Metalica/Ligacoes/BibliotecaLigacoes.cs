using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DEB.Core.Persistencia;

namespace DEB.Core.Elementos.Metalica.Ligacoes;

/// <summary>Tipos de ligação metálica da biblioteca do PCEB (D49).</summary>
public enum TipoLigacao { ChapaAlma, CantoneiraDupla, ChapaTopo, PlacaBase }

/// <summary>De onde vem o modelo de ligação: o PCEB (vem com o programa), o escritório ou o projeto (D45: o nível de baixo vale).</summary>
public enum OrigemLigacao { Pceb, Escritorio, Projeto }

/// <summary>Um parâmetro do esquema de um tipo de ligação: número (unidade, faixa; pode ser "auto") ou opção.</summary>
public sealed class ParametroLigacao
{
    public string Id { get; set; } = "";
    public string Rotulo { get; set; } = "";
    public string Grupo { get; set; } = "";
    public string Unidade { get; set; } = "";
    public string Padrao { get; set; } = "";
    public bool PodeAuto { get; set; }
    public IReadOnlyList<string> Opcoes { get; set; } = Array.Empty<string>();
    public double? Min { get; set; }
    public double? Max { get; set; }
    public string Explicacao { get; set; } = "";
}

/// <summary>
/// Modelo de ligação da biblioteca (D49): nome, tipo e os valores dos parâmetros (texto invariante; "auto" = o PCEB escolhe pela
/// peça). O mesmo modelo serve ao cálculo (NBR 8800:2024), à janela e, no Revit, às famílias PCEB que desenham a ligação.
/// </summary>
public sealed class ModeloLigacao
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public TipoLigacao Tipo { get; set; }
    public string Descricao { get; set; } = "";
    public OrigemLigacao Origem { get; set; }
    public Dictionary<string, string> Valores { get; } = new Dictionary<string, string>();

    public const string Auto = "auto";

    /// <summary>Valor do parâmetro (o do modelo ou o padrão do esquema).</summary>
    public string Texto(string id)
    {
        if (Valores.TryGetValue(id, out string v) && !string.IsNullOrWhiteSpace(v)) return v.Trim();
        return EsquemasLigacao.Parametro(Tipo, id).Padrao;
    }

    public bool EhAuto(string id) => string.Equals(Texto(id), Auto, StringComparison.OrdinalIgnoreCase);

    /// <summary>Número do parâmetro; "auto" devolve <paramref name="seAuto"/>.</summary>
    public double Numero(string id, double seAuto = double.NaN)
    {
        string t = Texto(id);
        if (string.Equals(t, Auto, StringComparison.OrdinalIgnoreCase)) return seAuto;
        return double.TryParse(t.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ? x : seAuto;
    }

    public ModeloLigacao Copia(OrigemLigacao origem)
    {
        var m = new ModeloLigacao { Id = Id, Nome = Nome, Tipo = Tipo, Descricao = Descricao, Origem = origem };
        foreach (var kv in Valores) m.Valores[kv.Key] = kv.Value;
        return m;
    }

    /// <summary>Valores fora da faixa ou opções que não existem (texto para a janela; vazio = ok).</summary>
    public List<string> Erros()
    {
        var e = new List<string>();
        foreach (var p in EsquemasLigacao.Do(Tipo))
        {
            string t = Texto(p.Id);
            if (p.Opcoes.Count > 0) { if (!p.Opcoes.Contains(t)) e.Add($"{p.Rotulo}: \"{t}\" não é uma opção"); continue; }
            if (p.PodeAuto && string.Equals(t, Auto, StringComparison.OrdinalIgnoreCase)) continue;
            double x = Numero(p.Id);
            if (double.IsNaN(x)) e.Add($"{p.Rotulo}: \"{t}\" não é número");
            else if (p.Min.HasValue && x < p.Min.Value - 1e-9 || p.Max.HasValue && x > p.Max.Value + 1e-9) e.Add($"{p.Rotulo}: {t} fora de {p.Min}…{p.Max}");
        }
        return e;
    }
}

/// <summary>Esquema de parâmetros de cada tipo de ligação (a janela monta o formulário daqui; o cálculo lê pelos ids).</summary>
public static class EsquemasLigacao
{
    private static readonly string[] Acos = AcoEstrutural.Lista.Select(a => a.Nome).ToArray();
    private static readonly string[] Parafusos = TipoParafuso.Lista.Select(a => a.Nome).ToArray();
    private static readonly string[] SimNao = { "Sim", "Não" };

    private static ParametroLigacao N(string id, string rot, string g, string un, string padrao, double? min, double? max, bool auto = false, string ex = "") =>
        new ParametroLigacao { Id = id, Rotulo = rot, Grupo = g, Unidade = un, Padrao = padrao, Min = min, Max = max, PodeAuto = auto, Explicacao = ex };
    private static ParametroLigacao O(string id, string rot, string g, string[] ops, string padrao, string ex = "") =>
        new ParametroLigacao { Id = id, Rotulo = rot, Grupo = g, Opcoes = ops, Padrao = padrao, Explicacao = ex };

    private static readonly Dictionary<TipoLigacao, ParametroLigacao[]> _esq = new Dictionary<TipoLigacao, ParametroLigacao[]>
    {
        [TipoLigacao.ChapaAlma] = new[]
        {
            O("parafuso", "Parafuso", "Parafusos", Parafusos, "ASTM F3125 A325"),
            N("db", "Diâmetro", "Parafusos", "mm", "19.05", 12, 36),
            O("rosca", "Rosca no plano de corte", "Parafusos", SimNao, "Sim", "Sim = a favor da segurança (0,45·Ab·fub)."),
            N("n", "Número de parafusos", "Parafusos", "", "auto", 2, 12, true, "auto: o maior que cabe na altura livre da alma."),
            N("s", "Passo vertical", "Parafusos", "mm", "75", 30, 200),
            O("acoChapa", "Aço da chapa", "Chapa", Acos, "ASTM A36"),
            N("tp", "Espessura da chapa", "Chapa", "mm", "9.5", 4.75, 31.5),
            N("ev", "Borda vertical", "Chapa", "mm", "38", 15, 100),
            N("ehChapa", "Borda horizontal da chapa", "Chapa", "mm", "38", 15, 100),
            N("ehViga", "Borda horizontal da alma da viga", "Chapa", "mm", "38", 15, 100),
            N("a", "Excentricidade (solda à fila de parafusos)", "Chapa", "mm", "75", 40, 200),
            N("perna", "Perna do filete (cada lado)", "Solda", "mm", "6", 3, 16),
            N("fw", "Resistência do eletrodo", "Solda", "MPa", "485", 415, 550, false, "E60: 415; E70: 485; E80: 550 (Tab. A.4)."),
        },
        [TipoLigacao.CantoneiraDupla] = new[]
        {
            O("parafuso", "Parafuso", "Parafusos", Parafusos, "ASTM F3125 A325"),
            N("db", "Diâmetro", "Parafusos", "mm", "19.05", 12, 36),
            O("rosca", "Rosca no plano de corte", "Parafusos", SimNao, "Sim"),
            N("n", "Número de parafusos por aba", "Parafusos", "", "auto", 2, 12, true),
            N("s", "Passo vertical", "Parafusos", "mm", "75", 30, 200),
            O("acoCantoneira", "Aço das cantoneiras", "Cantoneiras", Acos, "ASTM A36"),
            N("aba", "Aba da cantoneira", "Cantoneiras", "mm", "76.2", 50, 152),
            N("t", "Espessura da cantoneira", "Cantoneiras", "mm", "7.94", 4.76, 19.05),
            N("ev", "Borda vertical", "Cantoneiras", "mm", "32", 15, 100),
            N("eh", "Borda horizontal (aba e alma)", "Cantoneiras", "mm", "32", 15, 100),
            O("noSuporte", "Ligação ao suporte", "Cantoneiras", new[] { "Parafusada", "Soldada" }, "Parafusada"),
            N("perna", "Perna do filete (se soldada)", "Solda", "mm", "6", 3, 16),
            N("fw", "Resistência do eletrodo", "Solda", "MPa", "485", 415, 550),
        },
        [TipoLigacao.ChapaTopo] = new[]
        {
            O("parafuso", "Parafuso", "Parafusos", Parafusos, "ASTM F3125 A325"),
            N("db", "Diâmetro", "Parafusos", "mm", "22.2", 12, 36),
            O("estendida", "Chapa estendida (parafusos fora da mesa tracionada)", "Chapa", SimNao, "Sim"),
            N("g", "Gabarito horizontal entre furos", "Parafusos", "mm", "100", 60, 300),
            N("pf", "Distância da mesa ao parafuso", "Parafusos", "mm", "45", 30, 120),
            N("linhasInternas", "Linhas de parafusos abaixo da mesa tracionada", "Parafusos", "", "1", 1, 6),
            O("acoChapa", "Aço da chapa", "Chapa", Acos, "ASTM A36"),
            N("tp", "Espessura da chapa", "Chapa", "mm", "auto", 9.5, 50, true, "auto: a menor que dispensa o efeito de alavanca."),
            N("bp", "Largura da chapa", "Chapa", "mm", "auto", 100, 600, true, "auto: largura da mesa + 25 mm."),
            N("pernaMesa", "Perna do filete nas mesas", "Solda", "mm", "auto", 5, 25, true, "auto: o que desenvolve a mesa tracionada."),
            N("pernaAlma", "Perna do filete na alma", "Solda", "mm", "6", 3, 16),
            N("fw", "Resistência do eletrodo", "Solda", "MPa", "485", 415, 550),
        },
        [TipoLigacao.PlacaBase] = new[]
        {
            O("acoPlaca", "Aço da placa", "Placa", Acos, "ASTM A36"),
            N("tp", "Espessura da placa", "Placa", "mm", "auto", 12.5, 100, true, "auto: a menor espessura comercial que atende."),
            N("b", "Largura da placa (paralela à mesa)", "Placa", "mm", "auto", 150, 1500, true, "auto: bf + 100 mm."),
            N("h", "Comprimento da placa (paralelo à alma)", "Placa", "mm", "auto", 150, 1500, true, "auto: d + 150 mm."),
            N("nChumb", "Número de chumbadores", "Chumbadores", "", "4", 4, 12),
            N("dChumb", "Diâmetro dos chumbadores", "Chumbadores", "mm", "25", 12, 64),
            O("acoChumb", "Aço dos chumbadores", "Chumbadores", new[] { "ASTM F1554 Gr. 36", "ASTM F1554 Gr. 55", "SAE 1020" }, "ASTM F1554 Gr. 36"),
            N("bordaChumb", "Distância do chumbador à borda da placa", "Chumbadores", "mm", "50", 30, 150),
            N("fck", "fck do bloco de concreto", "Apoio", "MPa", "25", 20, 90),
            N("a2a1", "Relação A2/A1 do apoio", "Apoio", "", "1", 1, 4, false, "Área do apoio confinada / área da placa (≤ 4)."),
            N("graute", "Espessura do graute", "Apoio", "mm", "25", 0, 75),
            N("perna", "Perna do filete pilar–placa", "Solda", "mm", "8", 3, 16),
            N("fw", "Resistência do eletrodo", "Solda", "MPa", "485", 415, 550),
        },
    };

    public static IReadOnlyList<ParametroLigacao> Do(TipoLigacao t) => _esq[t];

    public static ParametroLigacao Parametro(TipoLigacao t, string id) =>
        _esq[t].FirstOrDefault(p => p.Id == id) ?? throw new KeyNotFoundException($"Parâmetro desconhecido em {t}: {id}");

    public static string NomeTipo(TipoLigacao t) => t switch
    {
        TipoLigacao.ChapaAlma => "Chapa de alma (flexível)",
        TipoLigacao.CantoneiraDupla => "Cantoneira dupla (flexível)",
        TipoLigacao.ChapaTopo => "Chapa de topo (rígida)",
        _ => "Placa de base",
    };
}

/// <summary>
/// Biblioteca de ligações (D49): as do PCEB (embutidas no núcleo, vão com o programa), as do escritório (arquivo comum ao CAD e ao
/// Revit, ao lado da configuração) e as do projeto (gravadas no modelo). Mesmo id: o nível de baixo substitui o de cima.
/// </summary>
public sealed class BibliotecaLigacoes
{
    public List<ModeloLigacao> Pceb { get; } = new List<ModeloLigacao>();
    public List<ModeloLigacao> Escritorio { get; } = new List<ModeloLigacao>();
    public List<ModeloLigacao> Projeto { get; } = new List<ModeloLigacao>();

    /// <summary>Arquivo do escritório: %AppData%\PCEB\padroes\ligacoes.json.</summary>
    public static string CaminhoEscritorio => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCEB", "padroes", "ligacoes.json");

    /// <summary>Os modelos em vigor: PCEB, sobrepostos pelo escritório e depois pelo projeto.</summary>
    public IReadOnlyList<ModeloLigacao> Todos
    {
        get
        {
            var d = new Dictionary<string, ModeloLigacao>();
            var ordem = new List<string>();
            foreach (var m in Pceb.Concat(Escritorio).Concat(Projeto)) { if (!d.ContainsKey(m.Id)) ordem.Add(m.Id); d[m.Id] = m; }
            return ordem.Select(i => d[i]).ToList();
        }
    }

    public ModeloLigacao Buscar(string id) => Todos.FirstOrDefault(m => m.Id == id);

    /// <summary>O primeiro modelo do tipo (o padrão do tipo é o primeiro da lista em vigor).</summary>
    public ModeloLigacao Padrao(TipoLigacao t) => Todos.FirstOrDefault(m => m.Tipo == t);

    /// <summary>Biblioteca com as ligações do PCEB e, se houver, as do escritório (avisos de leitura em <paramref name="avisos"/>).</summary>
    public static BibliotecaLigacoes Carregar(List<string> avisos = null, string arquivoEscritorio = null)
    {
        var b = new BibliotecaLigacoes();
        using (Stream s = typeof(BibliotecaLigacoes).GetTypeInfo().Assembly.GetManifestResourceStream("DEB.Core.LigacoesPceb.json"))
        using (var r = new StreamReader(s, Encoding.UTF8))
            b.Pceb.AddRange(DeJson(r.ReadToEnd(), OrigemLigacao.Pceb));
        string arq = arquivoEscritorio ?? CaminhoEscritorio;
        try { if (File.Exists(arq)) b.Escritorio.AddRange(DeJson(File.ReadAllText(arq, Encoding.UTF8), OrigemLigacao.Escritorio)); }
        catch (Exception ex) { avisos?.Add($"Ligações do escritório não lidas ({arq}): {ex.Message}. Valem as do PCEB."); }
        return b;
    }

    public void GravarEscritorio(string arquivo = null)
    {
        string arq = arquivo ?? CaminhoEscritorio;
        Directory.CreateDirectory(Path.GetDirectoryName(arq));
        File.WriteAllText(arq, ParaJson(Escritorio), new UTF8Encoding(false));
    }

    // ---------------------------------------------------------------- JSON: {"ligacoes":[{"id","nome","tipo","descricao","valores":{…}}]}

    public static string ParaJson(IEnumerable<ModeloLigacao> l)
    {
        var raiz = ValorJson.NovoObjeto();
        var lista = ValorJson.NovaLista(); raiz.Definir("ligacoes", lista);
        foreach (var m in l)
        {
            var o = ValorJson.NovoObjeto(); lista.Adicionar(o);
            o.Definir("id", m.Id); o.Definir("nome", m.Nome); o.Definir("tipo", m.Tipo.ToString()); o.Definir("descricao", m.Descricao);
            var v = ValorJson.NovoObjeto(); o.Definir("valores", v);
            foreach (var kv in m.Valores.OrderBy(x => x.Key)) v.Definir(kv.Key, kv.Value);
        }
        return Json.Gravar(raiz, indentado: true);
    }

    public static List<ModeloLigacao> DeJson(string json, OrigemLigacao origem)
    {
        var r = new List<ModeloLigacao>();
        var raiz = Json.Ler(json);
        foreach (var o in raiz["ligacoes"]?.Itens ?? Array.Empty<ValorJson>())
        {
            if (!Enum.TryParse(o["tipo"]?.Texto ?? "", out TipoLigacao t)) continue;
            var m = new ModeloLigacao { Id = o["id"]?.Texto ?? "", Nome = o["nome"]?.Texto ?? "", Tipo = t, Descricao = o["descricao"]?.Texto ?? "", Origem = origem };
            foreach (var kv in o["valores"]?.Campos ?? Array.Empty<KeyValuePair<string, ValorJson>>())
                m.Valores[kv.Key] = kv.Value.Tipo == TipoJson.Numero ? kv.Value.Numero.ToString(CultureInfo.InvariantCulture) : kv.Value.Texto ?? "";
            if (m.Id != "") r.Add(m);
        }
        return r;
    }
}
