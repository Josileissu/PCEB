using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DEB.Core.Motor.Prancha;

/// <summary>
/// Troca os marcadores dos textos do perfil (título da folha, títulos das vistas, rótulos, notas):
/// <list type="bullet">
/// <item><c>{campo}</c> = valor do campo (sem o campo: vazio e aviso);</item>
/// <item><c>{campo?}</c> = vazio quando o valor é "", "0" ou "1" (ex.: "{repeticoes?}x" some com 1 peça);</item>
/// <item><c>[ … ]</c> = o trecho some se algum campo dentro dele ficar vazio ("BLOCO {nome}[ - {repeticoes?}x]");</item>
/// <item><c>{campo:texto}</c> = o texto (que pode ter outros marcadores) só entra se o campo for verdadeiro
/// ("1", "sim", "true" ou número ≠ 0), ex.: "{calice:; INTERNO DO CÁLICE {cobrimento.calice} cm}".</item>
/// </list>
/// Campos sem diferença de maiúsculas. Nunca lança exceção: chave ou colchete sem fechar vira texto.
/// </summary>
public sealed class FormatadorCampos
{
    private readonly IDictionary<string, string> _campos;
    private readonly Action<string> _faltou;

    public FormatadorCampos(IDictionary<string, string> campos, Action<string> faltou = null)
    {
        _campos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (campos != null) foreach (var kv in campos) _campos[kv.Key] = kv.Value ?? "";
        _faltou = faltou;
    }

    public string Formatar(string s, IDictionary<string, string> extras = null)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int i = 0;
        return Sequencia(s, ref i, '\0', extras, out _);
    }

    public static bool Verdadeiro(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return false;
        v = v.Trim();
        if (v.Equals("sim", StringComparison.OrdinalIgnoreCase) || v.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        return double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && Math.Abs(d) > 1e-12;
    }

    private bool Valor(string chave, IDictionary<string, string> extras, out string v)
    {
        if (extras != null && extras.TryGetValue(chave, out v)) return true;
        return _campos.TryGetValue(chave, out v);
    }

    private string Sequencia(string s, ref int i, char fim, IDictionary<string, string> extras, out bool vazio)
    {
        vazio = false;
        var sb = new StringBuilder();
        while (i < s.Length)
        {
            char ch = s[i];
            if (fim != '\0' && ch == fim) { i++; return sb.ToString(); }
            if (ch == '{')
            {
                int j = i + 1;
                while (j < s.Length && s[j] != '}' && s[j] != ':' && s[j] != '{') j++;
                if (j >= s.Length || s[j] == '{') { sb.Append(ch); i++; continue; }
                string nome = s.Substring(i + 1, j - i - 1).Trim();
                if (s[j] == ':')
                {
                    i = j + 1;
                    string dentro = Sequencia(s, ref i, '}', extras, out _);
                    bool achou = Valor(nome, extras, out var cond);
                    if (!achou) _faltou?.Invoke(nome);
                    if (achou && Verdadeiro(cond)) sb.Append(dentro);
                    continue;
                }
                i = j + 1;
                bool opcional = nome.EndsWith("?");
                if (opcional) nome = nome.Substring(0, nome.Length - 1).Trim();
                if (!Valor(nome, extras, out var val)) { _faltou?.Invoke(nome); val = ""; }
                if (opcional && (val == "0" || val == "1")) val = "";
                if (val.Length == 0) vazio = true;
                sb.Append(val);
                continue;
            }
            if (ch == '[')
            {
                int fecha = s.IndexOf(']', i + 1);
                if (fecha < 0) { sb.Append(ch); i++; continue; }
                i++;
                string dentro = Sequencia(s, ref i, ']', extras, out bool v2);
                if (!v2) sb.Append(dentro);
                continue;
            }
            sb.Append(ch);
            i++;
        }
        return sb.ToString();
    }
}
