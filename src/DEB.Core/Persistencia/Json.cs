using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DEB.Core.Persistencia;

// =====================================================================================================================
// JSON mínimo e próprio (sem NuGet: o plugin roda em net48 dentro do CAD e o PC pode não ter rede).
// Árvore de valores (objeto, lista, texto, número, bool, null); leitura estrita (RFC 8259) com erro em linha/coluna;
// gravação indentada; números sempre em InvariantCulture ("1.5"; "1,5" NÃO é número); arquivos em UTF-8.
// =====================================================================================================================

public enum TipoJson { Nulo, Objeto, Lista, Texto, Numero, Booleano }

/// <summary>Um valor JSON. Objetos guardam os campos na ordem em que entraram (a gravação mantém a ordem).</summary>
public sealed class ValorJson
{
    private readonly List<KeyValuePair<string, ValorJson>> _campos;
    private readonly Dictionary<string, int> _indice;
    private readonly List<ValorJson> _itens;

    public TipoJson Tipo { get; }
    /// <summary>Valor quando <see cref="Tipo"/> = Texto (senão null).</summary>
    public string Texto { get; }
    /// <summary>Valor quando <see cref="Tipo"/> = Numero.</summary>
    public double Numero { get; }
    /// <summary>Valor quando <see cref="Tipo"/> = Booleano.</summary>
    public bool Booleano { get; }

    private ValorJson(TipoJson tipo, string texto = null, double numero = 0, bool booleano = false)
    {
        Tipo = tipo; Texto = texto; Numero = numero; Booleano = booleano;
        if (tipo == TipoJson.Objeto) { _campos = new List<KeyValuePair<string, ValorJson>>(); _indice = new Dictionary<string, int>(StringComparer.Ordinal); }
        if (tipo == TipoJson.Lista) _itens = new List<ValorJson>();
    }

    public static readonly ValorJson Nulo = new ValorJson(TipoJson.Nulo);
    public static ValorJson NovoObjeto() => new ValorJson(TipoJson.Objeto);
    public static ValorJson NovaLista() => new ValorJson(TipoJson.Lista);
    public static ValorJson De(string s) => s == null ? Nulo : new ValorJson(TipoJson.Texto, texto: s);
    public static ValorJson De(double d) => new ValorJson(TipoJson.Numero, numero: d);
    public static ValorJson De(bool b) => new ValorJson(TipoJson.Booleano, booleano: b);

    public bool EhObjeto => Tipo == TipoJson.Objeto;
    public bool EhLista => Tipo == TipoJson.Lista;
    public bool EhNulo => Tipo == TipoJson.Nulo;

    /// <summary>Campos do objeto, na ordem (vazio se não for objeto).</summary>
    public IReadOnlyList<KeyValuePair<string, ValorJson>> Campos => (IReadOnlyList<KeyValuePair<string, ValorJson>>)_campos ?? Array.Empty<KeyValuePair<string, ValorJson>>();
    /// <summary>Itens da lista (vazio se não for lista).</summary>
    public IReadOnlyList<ValorJson> Itens => (IReadOnlyList<ValorJson>)_itens ?? Array.Empty<ValorJson>();
    public int Quantidade => _campos?.Count ?? _itens?.Count ?? 0;

    /// <summary>Campo do objeto ou null se não existir (ou se este valor não for objeto).</summary>
    public ValorJson this[string campo] => _indice != null && _indice.TryGetValue(campo, out int i) ? _campos[i].Value : null;
    public ValorJson this[int i] => _itens != null && i >= 0 && i < _itens.Count ? _itens[i] : null;
    public bool Tem(string campo) => _indice != null && _indice.ContainsKey(campo);

    /// <summary>Define um campo do objeto (substitui se já existe, mantendo a posição). Devolve o próprio objeto.</summary>
    public ValorJson Definir(string campo, ValorJson valor)
    {
        if (_campos == null) throw new InvalidOperationException("Definir campo em um valor JSON que não é objeto.");
        if (campo == null) throw new ArgumentNullException(nameof(campo));
        valor ??= Nulo;
        if (_indice.TryGetValue(campo, out int i)) _campos[i] = new KeyValuePair<string, ValorJson>(campo, valor);
        else { _indice[campo] = _campos.Count; _campos.Add(new KeyValuePair<string, ValorJson>(campo, valor)); }
        return this;
    }
    public ValorJson Definir(string campo, string valor) => Definir(campo, De(valor));
    public ValorJson Definir(string campo, double valor) => Definir(campo, De(valor));
    public ValorJson Definir(string campo, bool valor) => Definir(campo, De(valor));

    /// <summary>Acrescenta um item à lista. Devolve a própria lista.</summary>
    public ValorJson Adicionar(ValorJson valor)
    {
        if (_itens == null) throw new InvalidOperationException("Adicionar item em um valor JSON que não é lista.");
        _itens.Add(valor ?? Nulo);
        return this;
    }

    public override string ToString() => Json.Gravar(this, indentado: false);
}

/// <summary>Erro de leitura com a posição (linha e coluna começam em 1).</summary>
public sealed class ErroJson : FormatException
{
    public int Linha { get; }
    public int Coluna { get; }
    public ErroJson(string mensagem, int linha, int coluna)
        : base(FormattableString.Invariant($"JSON inválido (linha {linha}, coluna {coluna}): {mensagem}"))
    { Linha = linha; Coluna = coluna; }
}

public static class Json
{
    /// <summary>Profundidade máxima de aninhamento aceita na leitura (protege a pilha).</summary>
    public const int ProfundidadeMaxima = 256;

    // ------------------------------------------------------------------ leitura

    /// <summary>Lê um texto JSON. Lança <see cref="ErroJson"/> com linha/coluna se for inválido.</summary>
    public static ValorJson Ler(string texto)
    {
        var l = new Leitor(texto ?? "");
        l.PularEspacos();
        if (l.Fim) throw l.Erro("texto vazio.");
        var v = l.LerValor(0);
        l.PularEspacos();
        if (!l.Fim) throw l.Erro($"conteúdo depois do fim do valor ('{l.Atual}').");
        return v;
    }

    /// <summary>Como <see cref="Ler"/>, sem exceção: devolve false e a mensagem.</summary>
    public static bool TentarLer(string texto, out ValorJson valor, out string erro)
    {
        try { valor = Ler(texto); erro = null; return true; }
        catch (ErroJson e) { valor = null; erro = e.Message; return false; }
    }

    /// <summary>Lê um arquivo UTF-8 (com ou sem BOM).</summary>
    public static ValorJson LerArquivo(string caminho) => Ler(File.ReadAllText(caminho, Encoding.UTF8));

    private sealed class Leitor
    {
        private readonly string _s;
        private int _p, _linha = 1, _inicioLinha;
        public Leitor(string s) { _s = s; if (_s.Length > 0 && _s[0] == (char)0xFEFF) _p = _inicioLinha = 1; }

        public bool Fim => _p >= _s.Length;
        public char Atual => _s[_p];

        public ErroJson Erro(string msg) => new ErroJson(msg, _linha, _p - _inicioLinha + 1);

        public void PularEspacos()
        {
            while (_p < _s.Length)
            {
                char ch = _s[_p];
                if (ch == '\n') { _p++; _linha++; _inicioLinha = _p; }
                else if (ch == ' ' || ch == '\t' || ch == '\r') _p++;
                else break;
            }
        }

        public ValorJson LerValor(int prof)
        {
            if (prof > ProfundidadeMaxima) throw Erro("aninhamento profundo demais.");
            if (Fim) throw Erro("fim inesperado do texto (falta um valor).");
            char ch = Atual;
            switch (ch)
            {
                case '{': return LerObjeto(prof);
                case '[': return LerLista(prof);
                case '"': return ValorJson.De(LerTexto());
                case 't': Palavra("true"); return ValorJson.De(true);
                case 'f': Palavra("false"); return ValorJson.De(false);
                case 'n': Palavra("null"); return ValorJson.Nulo;
                default:
                    if (ch == '-' || (ch >= '0' && ch <= '9')) return ValorJson.De(LerNumero());
                    throw Erro($"caractere inesperado '{ch}' (esperado um valor: objeto, lista, texto, número, true, false ou null).");
            }
        }

        private void Palavra(string w)
        {
            if (string.CompareOrdinal(_s, _p, w, 0, w.Length) != 0 || (_p + w.Length < _s.Length && char.IsLetterOrDigit(_s[_p + w.Length])))
                throw Erro($"palavra desconhecida (esperado '{w}'; textos vão entre aspas).");
            _p += w.Length;
        }

        private ValorJson LerObjeto(int prof)
        {
            var o = ValorJson.NovoObjeto();
            _p++; // {
            PularEspacos();
            if (!Fim && Atual == '}') { _p++; return o; }
            while (true)
            {
                PularEspacos();
                if (Fim) throw Erro("fim inesperado dentro do objeto (falta '}').");
                if (Atual != '"') throw Erro($"esperado o nome do campo entre aspas, encontrado '{Atual}'.");
                string k = LerTexto();
                PularEspacos();
                if (Fim || Atual != ':') throw Erro($"esperado ':' depois do campo \"{k}\".");
                _p++;
                PularEspacos();
                o.Definir(k, LerValor(prof + 1));   // campo repetido: vale o último
                PularEspacos();
                if (Fim) throw Erro("fim inesperado dentro do objeto (falta '}').");
                if (Atual == ',') { _p++; continue; }
                if (Atual == '}') { _p++; return o; }
                throw Erro($"esperado ',' ou '}}' no objeto, encontrado '{Atual}'.");
            }
        }

        private ValorJson LerLista(int prof)
        {
            var l = ValorJson.NovaLista();
            _p++; // [
            PularEspacos();
            if (!Fim && Atual == ']') { _p++; return l; }
            while (true)
            {
                PularEspacos();
                l.Adicionar(LerValor(prof + 1));
                PularEspacos();
                if (Fim) throw Erro("fim inesperado dentro da lista (falta ']').");
                if (Atual == ',') { _p++; continue; }
                if (Atual == ']') { _p++; return l; }
                throw Erro($"esperado ',' ou ']' na lista, encontrado '{Atual}'.");
            }
        }

        private string LerTexto()
        {
            _p++; // "
            var sb = new StringBuilder();
            while (true)
            {
                if (Fim) throw Erro("texto sem as aspas de fechamento.");
                char ch = _s[_p];
                if (ch == '"') { _p++; return sb.ToString(); }
                if (ch < 0x20) throw Erro(ch == '\n' ? "quebra de linha dentro de texto (use \\n)." : "caractere de controle dentro de texto.");
                if (ch != '\\') { sb.Append(ch); _p++; continue; }
                _p++;
                if (Fim) throw Erro("texto termina no meio de um escape.");
                char e = _s[_p];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        int cod = 0;
                        for (int i = 1; i <= 4; i++)
                        {
                            if (_p + i >= _s.Length) throw Erro("escape \\u incompleto.");
                            int h = Hex(_s[_p + i]);
                            if (h < 0) throw Erro("escape \\u com dígito hexadecimal inválido.");
                            cod = cod * 16 + h;
                        }
                        sb.Append((char)cod);
                        _p += 4;
                        break;
                    default: throw Erro($"escape inválido '\\{e}'.");
                }
                _p++;
            }
        }

        private static int Hex(char c) =>
            c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;

        private double LerNumero()
        {
            int ini = _p;
            if (Atual == '-') _p++;
            if (Fim || !Digito(Atual)) throw Erro("número inválido (falta dígito).");
            if (Atual == '0') { _p++; if (!Fim && Digito(Atual)) throw Erro("número inválido (zero à esquerda)."); }
            else while (!Fim && Digito(Atual)) _p++;
            if (!Fim && Atual == '.')
            {
                _p++;
                if (Fim || !Digito(Atual)) throw Erro("número inválido (falta dígito depois do ponto).");
                while (!Fim && Digito(Atual)) _p++;
            }
            if (!Fim && (Atual == 'e' || Atual == 'E'))
            {
                _p++;
                if (!Fim && (Atual == '+' || Atual == '-')) _p++;
                if (Fim || !Digito(Atual)) throw Erro("número inválido (expoente sem dígito).");
                while (!Fim && Digito(Atual)) _p++;
            }
            var txt = _s.Substring(ini, _p - ini);
            if (!double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || double.IsInfinity(d))
                throw Erro($"número fora do intervalo ({txt}).");
            return d;
        }

        private static bool Digito(char c) => c >= '0' && c <= '9';
    }

    // ------------------------------------------------------------------ gravação

    /// <summary>Grava o valor. Indentado: 2 espaços; listas só de valores simples ficam numa linha ([x, y, z]).</summary>
    public static string Gravar(ValorJson valor, bool indentado = true)
    {
        var sb = new StringBuilder();
        Escrever(sb, valor ?? ValorJson.Nulo, indentado, 0);
        return sb.ToString();
    }

    /// <summary>Grava em arquivo UTF-8 sem BOM (cria a pasta se faltar), com quebra de linha no fim.</summary>
    public static void GravarArquivo(string caminho, ValorJson valor)
    {
        var pasta = Path.GetDirectoryName(Path.GetFullPath(caminho));
        if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);
        File.WriteAllText(caminho, Gravar(valor) + "\n", new UTF8Encoding(false));
    }

    private static void Escrever(StringBuilder sb, ValorJson v, bool ind, int nivel)
    {
        switch (v.Tipo)
        {
            case TipoJson.Nulo: sb.Append("null"); return;
            case TipoJson.Booleano: sb.Append(v.Booleano ? "true" : "false"); return;
            case TipoJson.Numero: sb.Append(Numero(v.Numero)); return;
            case TipoJson.Texto: EscreverTexto(sb, v.Texto); return;
            case TipoJson.Lista:
                if (v.Quantidade == 0) { sb.Append("[]"); return; }
                bool simples = true;
                foreach (var it in v.Itens) if (it.EhObjeto || it.EhLista) { simples = false; break; }
                if (!ind || simples)
                {
                    sb.Append('[');
                    for (int i = 0; i < v.Itens.Count; i++) { if (i > 0) sb.Append(ind ? ", " : ","); Escrever(sb, v.Itens[i], ind, nivel + 1); }
                    sb.Append(']');
                    return;
                }
                sb.Append('[');
                for (int i = 0; i < v.Itens.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    NovaLinha(sb, nivel + 1);
                    Escrever(sb, v.Itens[i], ind, nivel + 1);
                }
                NovaLinha(sb, nivel);
                sb.Append(']');
                return;
            case TipoJson.Objeto:
                if (v.Quantidade == 0) { sb.Append("{}"); return; }
                sb.Append('{');
                for (int i = 0; i < v.Campos.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    if (ind) NovaLinha(sb, nivel + 1);
                    EscreverTexto(sb, v.Campos[i].Key);
                    sb.Append(ind ? ": " : ":");
                    Escrever(sb, v.Campos[i].Value, ind, nivel + 1);
                }
                if (ind) NovaLinha(sb, nivel);
                sb.Append('}');
                return;
        }
    }

    private static void NovaLinha(StringBuilder sb, int nivel) { sb.Append('\n'); sb.Append(' ', 2 * nivel); }

    /// <summary>Número em InvariantCulture, o mais curto que volta ao mesmo double. NaN/infinito viram null.</summary>
    public static string Numero(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return "null";
        if (d == Math.Floor(d) && Math.Abs(d) < 1e15) return ((long)d).ToString(CultureInfo.InvariantCulture);
        var s = d.ToString("R", CultureInfo.InvariantCulture);
        if (double.Parse(s, CultureInfo.InvariantCulture) != d) s = d.ToString("G17", CultureInfo.InvariantCulture);
        return s;
    }

    private static void EscreverTexto(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}

/// <summary>
/// Leitura tipada de campos com o caminho do campo nas mensagens ("perfil.padroes.cobrimento: esperado um número…").
/// Campo ausente ou null → padrão; tipo errado → <see cref="FormatException"/> com o caminho.
/// </summary>
public static class LeitorJson
{
    /// <summary>O próprio valor, se for objeto; senão FormatException.</summary>
    public static ValorJson Objeto(ValorJson o, string caminho)
    {
        if (o == null || !o.EhObjeto) throw new FormatException($"{caminho}: esperado um objeto {{ … }}.");
        return o;
    }

    /// <summary>Campo existe e não é null.</summary>
    public static bool Presente(ValorJson o, string k) { var v = o[k]; return v != null && !v.EhNulo; }

    /// <summary>Campo lista; null se ausente ou null.</summary>
    public static ValorJson Lista(ValorJson o, string k, string caminho)
    {
        var v = o[k];
        if (v == null || v.EhNulo) return null;
        if (!v.EhLista) throw new FormatException($"{caminho}.{k}: esperado uma lista [ … ].");
        return v;
    }

    public static double NumDeValor(ValorJson v, string caminho)
    {
        if (v == null || v.Tipo != TipoJson.Numero) throw new FormatException($"{caminho}: esperado um número (com ponto decimal, ex.: 2.5).");
        return v.Numero;
    }

    public static int IntDeValor(ValorJson v, string caminho)
    {
        double d = NumDeValor(v, caminho);
        if (d != Math.Floor(d) || d > int.MaxValue || d < int.MinValue) throw new FormatException($"{caminho}: esperado um número inteiro.");
        return (int)d;
    }

    public static double Num(ValorJson o, string k, double padrao, string caminho) =>
        Presente(o, k) ? NumDeValor(o[k], $"{caminho}.{k}") : padrao;

    public static int Int(ValorJson o, string k, int padrao, string caminho) =>
        Presente(o, k) ? IntDeValor(o[k], $"{caminho}.{k}") : padrao;

    public static bool Bool(ValorJson o, string k, bool padrao, string caminho)
    {
        if (!Presente(o, k)) return padrao;
        var v = o[k];
        if (v.Tipo != TipoJson.Booleano) throw new FormatException($"{caminho}.{k}: esperado true ou false.");
        return v.Booleano;
    }

    public static string Txt(ValorJson o, string k, string padrao, string caminho)
    {
        if (!Presente(o, k)) return padrao;
        var v = o[k];
        if (v.Tipo != TipoJson.Texto) throw new FormatException($"{caminho}.{k}: esperado um texto entre aspas.");
        return v.Texto;
    }

    public static List<string> ListaTxt(ValorJson o, string k, List<string> padrao, string caminho)
    {
        var l = Lista(o, k, caminho);
        if (l == null) return padrao;
        var r = new List<string>();
        for (int i = 0; i < l.Quantidade; i++)
        {
            if (l[i].Tipo != TipoJson.Texto) throw new FormatException($"{caminho}.{k}[{i}]: esperado um texto entre aspas.");
            r.Add(l[i].Texto);
        }
        return r;
    }

    public static Dictionary<string, double> DicNum(ValorJson o, string k, Dictionary<string, double> padrao, string caminho)
    {
        if (!Presente(o, k)) return padrao;
        var v = Objeto(o[k], $"{caminho}.{k}");
        var r = new Dictionary<string, double>();
        foreach (var kv in v.Campos) r[kv.Key] = NumDeValor(kv.Value, $"{caminho}.{k}.{kv.Key}");
        return r;
    }

    public static Dictionary<string, string> DicTxt(ValorJson o, string k, Dictionary<string, string> padrao, string caminho)
    {
        if (!Presente(o, k)) return padrao;
        var v = Objeto(o[k], $"{caminho}.{k}");
        var r = new Dictionary<string, string>();
        foreach (var kv in v.Campos)
        {
            if (kv.Value.Tipo != TipoJson.Texto) throw new FormatException($"{caminho}.{k}.{kv.Key}: esperado um texto entre aspas.");
            r[kv.Key] = kv.Value.Texto;
        }
        return r;
    }

    /// <summary>Enum pelo nome (sem diferenciar maiúsculas). Números não são aceitos (o arquivo fica legível).</summary>
    public static T EnumDeValor<T>(ValorJson v, string caminho) where T : struct
    {
        if (v == null || v.Tipo != TipoJson.Texto)
            throw new FormatException($"{caminho}: esperado um texto, um de: {string.Join(", ", Enum.GetNames(typeof(T)))}.");
        var s = v.Texto.Trim();
        foreach (var nome in Enum.GetNames(typeof(T)))
            if (string.Equals(nome, s, StringComparison.OrdinalIgnoreCase)) return (T)Enum.Parse(typeof(T), nome);
        throw new FormatException($"{caminho}: valor \"{v.Texto}\" desconhecido; use um de: {string.Join(", ", Enum.GetNames(typeof(T)))}.");
    }

    /// <summary>Campo enum (ver <see cref="EnumDeValor{T}"/>).</summary>
    public static T EnumCampo<T>(ValorJson o, string k, T padrao, string caminho) where T : struct =>
        Presente(o, k) ? EnumDeValor<T>(o[k], $"{caminho}.{k}") : padrao;
}
