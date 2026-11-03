using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DEB.Core.Persistencia;

/// <summary>
/// CSV dos arquivos que o usuário edita no Excel (layers.csv, modelos dos templates): separador ";" (ou tabulação/vírgula,
/// conforme o Excel salvou — decidido pela 1ª linha que não é comentário), aspas para campos com separador, "#" = comentário.
/// </summary>
public static class Csv
{
    /// <summary>Registros, um por linha. Comentários ("#…") voltam como um campo só, sem interpretar aspas; linhas vazias, vazias.</summary>
    public static IEnumerable<List<string>> Registros(string texto, bool aceitarVirgula = false)
    {
        var linhas = (texto ?? "").Replace("\r\n", "\n").Split('\n');
        char sep = ';';
        foreach (var l in linhas)
        {
            var s = l.TrimStart('﻿').Trim();
            if (s.Length == 0 || s.StartsWith("#")) continue;
            sep = s.Contains(';') ? ';' : s.Contains('\t') ? '\t' : aceitarVirgula && s.Contains(',') ? ',' : ';';
            break;
        }
        foreach (var l in linhas)
        {
            var s = l.TrimStart('﻿');
            if (s.TrimStart().StartsWith("#")) { yield return new List<string> { s.Trim() }; continue; }
            var r = new List<string>();
            var campo = new StringBuilder();
            bool aspas = false;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (aspas)
                {
                    if (ch != '"') campo.Append(ch);
                    else if (i + 1 < s.Length && s[i + 1] == '"') { campo.Append('"'); i++; }
                    else aspas = false;
                }
                else if (ch == '"' && campo.ToString().Trim().Length == 0) { campo.Clear(); aspas = true; }
                else if (ch == sep) { r.Add(campo.ToString()); campo.Clear(); }
                else campo.Append(ch);
            }
            r.Add(campo.ToString());
            yield return r;
        }
    }

    /// <summary>Campo com aspas quando tem ";", aspas ou quebra de linha.</summary>
    public static string Campo(string s)
    {
        s ??= "";
        return s.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    public static string Linha(IEnumerable<string> celulas) => string.Join(";", celulas.Select(Campo));
}
