using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DEB.Core.Documentacao;

/// <summary>
/// .pc3/.pmp no formato do AutoCAD 2025+ (levantado em 17/11/2026 nos arquivos do AutoCAD 2026): cabeçalho
/// <c>PIAFILEVERSION_3.0,json\r\n</c> e um JSON sem compressão <c>{ "data" : { …a mesma árvore do formato 2.0… }, "subver" : "json",
/// "version" : 0.01 }</c>. Objetos viram blocos de <see cref="NoPia"/>; valores ficam como no formato 2.0 (string = aspas de abertura
/// + texto, booleano = TRUE/FALSE, número = o texto do número), então <see cref="TamanhosPc3"/> vale para os dois formatos.
/// Recuo de um espaço por nível, <c>"chave" : valor</c> e objeto aberto na linha seguinte, como o AutoCAD grava (ida e volta exata).
/// </summary>
internal static class PiaJson
{
    public const string Versao = "PIAFILEVERSION_3.0,json";

    // valor que não é string/número/booleano (lista, null): guardado cru com este prefixo e devolvido igual
    private const char Cru = '\u0001';

    /// <summary>Lê o JSON (sem o cabeçalho): devolve a árvore de "data" e os pares de fora dela (subver, version).</summary>
    public static NoPia Ler(string json, List<NoPia> envelope)
    {
        int i = 0;
        var raiz = Objeto(json, ref i, "");
        NoPia data = null;
        foreach (var f in raiz.Filhos)
            if (f.Chave == "data" && f.Bloco) data = f; else envelope.Add(f);
        if (data == null) throw new InvalidDataException("arquivo PIA 3.0 sem \"data\"");
        var r = new NoPia("");
        r.Filhos.AddRange(data.Filhos);
        return r;
    }

    public static string Escrever(NoPia raiz, IEnumerable<NoPia> envelope)
    {
        var topo = new NoPia("");
        var data = new NoPia("data");
        data.Filhos.AddRange(raiz.Filhos);
        topo.Filhos.Add(data);
        topo.Filhos.AddRange(envelope);
        var sb = new StringBuilder();
        Bloco(sb, topo, 0);
        return sb.ToString();
    }

    private static void Bloco(StringBuilder sb, NoPia n, int nivel)
    {
        sb.Append('{').Append('\n');
        for (int k = 0; k < n.Filhos.Count; k++)
        {
            var f = n.Filhos[k];
            var rec = new string(' ', nivel + 1);
            sb.Append(rec).Append(Texto(f.Chave)).Append(" : ");
            if (f.Bloco) { sb.Append('\n').Append(rec); Bloco(sb, f, nivel + 1); }
            else sb.Append(Valor(f.Valor));
            if (k < n.Filhos.Count - 1) sb.Append(',');
            sb.Append('\n');
        }
        sb.Append(new string(' ', nivel)).Append('}');
    }

    private static string Valor(string v)
    {
        if (v.Length > 0 && v[0] == Cru) return v.Substring(1);
        if (v.StartsWith("\"", StringComparison.Ordinal)) return Texto(v.Substring(1));
        if (v.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) return "true";
        if (v.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return "false";
        // número: o texto do formato 2.0 pode trazer o código opcional entre parênteses ("1121.3 (…)"): fica só o número
        int p = v.IndexOf(' ');
        var num = p > 0 ? v.Substring(0, p) : v;
        return double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? num : Texto(v);
    }

    private static string Texto(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    // ---------------- leitor ----------------

    private static void Espaco(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

    private static void Esperar(string s, ref int i, char c)
    {
        Espaco(s, ref i);
        if (i >= s.Length || s[i] != c) throw new InvalidDataException($"JSON do arquivo PIA: esperado '{c}' na posição {i}");
        i++;
    }

    private static NoPia Objeto(string s, ref int i, string chave)
    {
        Esperar(s, ref i, '{');
        var n = new NoPia(chave);
        Espaco(s, ref i);
        if (i < s.Length && s[i] == '}') { i++; return n; }
        while (true)
        {
            Espaco(s, ref i);
            var k = LerTexto(s, ref i);
            Esperar(s, ref i, ':');
            Espaco(s, ref i);
            if (i < s.Length && s[i] == '{') n.Filhos.Add(Objeto(s, ref i, k));
            else n.Filhos.Add(new NoPia(k, LerValor(s, ref i)));
            Espaco(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            Esperar(s, ref i, '}');
            return n;
        }
    }

    private static string LerValor(string s, ref int i)
    {
        if (i >= s.Length) throw new InvalidDataException("JSON do arquivo PIA truncado");
        char c = s[i];
        if (c == '"') return "\"" + LerTexto(s, ref i);
        if (c == '[')
        {
            int ini = i, prof = 0;
            bool aspas = false;
            for (; i < s.Length; i++)
            {
                if (aspas) { if (s[i] == '\\') i++; else if (s[i] == '"') aspas = false; continue; }
                if (s[i] == '"') aspas = true;
                else if (s[i] == '[' || s[i] == '{') prof++;
                else if ((s[i] == ']' || s[i] == '}') && --prof == 0) { i++; break; }
            }
            return Cru + s.Substring(ini, i - ini);
        }
        int a = i;
        while (i < s.Length && s[i] != ',' && s[i] != '}' && !char.IsWhiteSpace(s[i])) i++;
        var t = s.Substring(a, i - a);
        if (t == "true") return "TRUE";
        if (t == "false") return "FALSE";
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return t;
        return Cru + t; // null
    }

    private static string LerTexto(string s, ref int i)
    {
        Esperar(s, ref i, '"');
        var sb = new StringBuilder();
        while (i < s.Length && s[i] != '"')
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                i++;
                switch (s[i])
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                    default: sb.Append(s[i]); break; // \" \\ \/
                }
                i++;
            }
            else sb.Append(s[i++]);
        }
        if (i >= s.Length) throw new InvalidDataException("JSON do arquivo PIA: texto sem fim");
        i++;
        return sb.ToString();
    }
}
