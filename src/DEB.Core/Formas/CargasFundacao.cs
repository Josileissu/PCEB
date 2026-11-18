using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DEB.Core.Persistencia;

namespace DEB.Core.Formas;

/// <summary>Esforços de um pilar na fundação (tf e tf.m), na ordem de <see cref="CsvCargas.Colunas"/>. null = sem valor no arquivo.</summary>
public sealed class CargaFundacao
{
    public string Marca { get; set; } = "";
    public double?[] Valores { get; } = new double?[CsvCargas.Colunas.Length];
}

/// <summary>
/// CSV das cargas na fundação (pranchas de formas): <c>Marca;Fz_Max;Fz_Min;Mx_Pos;Mx_Neg;My_Pos;My_Neg;Fx_Pos;Fx_Neg;Fy_Pos;Fy_Neg</c>.
/// Separador ";" (ou tabulação), vírgula ou ponto decimal. Com cabeçalho, as colunas valem pelo nome (com ou sem "PCEB_" (ou o antigo "SD_"), em qualquer
/// ordem); sem cabeçalho, pela posição. Célula vazia = não mexe no valor do modelo.
/// </summary>
public static class CsvCargas
{
    /// <summary>Colunas de esforço; o parâmetro do Revit é "PCEB_" + coluna (D38).</summary>
    public static readonly string[] Colunas = { "Fz_Max", "Fz_Min", "Mx_Pos", "Mx_Neg", "My_Pos", "My_Neg", "Fx_Pos", "Fx_Neg", "Fy_Pos", "Fy_Neg" };

    public static string Cabecalho => "Marca;" + string.Join(";", Colunas);

    /// <summary>Lê o texto do arquivo. Linhas com problema vão para <paramref name="avisos"/> (a leitura continua).</summary>
    public static List<CargaFundacao> Ler(string texto, List<string> avisos)
    {
        var r = new List<CargaFundacao>();
        var porMarca = new Dictionary<string, CargaFundacao>(StringComparer.OrdinalIgnoreCase);
        int[] mapa = null;   // coluna do arquivo de cada esforço (-1 = ausente); índice 0 do arquivo = marca
        int colMarca = 0, linha = 0;
        foreach (List<string> reg in Csv.Registros(texto))
        {
            linha++;
            if (reg.Count == 1 && reg[0].TrimStart().StartsWith("#")) continue;
            if (reg.All(c => string.IsNullOrWhiteSpace(c))) continue;
            if (mapa == null)
            {
                mapa = new int[Colunas.Length];
                string Nome(string c) => (c ?? "").Trim().TrimStart('﻿');
                if (reg.Any(c => Nome(c).Equals("Marca", StringComparison.OrdinalIgnoreCase)))
                {
                    var nomes = reg.Select(c => SemPrefixo(Nome(c))).ToList();
                    colMarca = nomes.FindIndex(c => c.Equals("Marca", StringComparison.OrdinalIgnoreCase));
                    for (int k = 0; k < Colunas.Length; k++)
                        mapa[k] = nomes.FindIndex(c => c.Equals(Colunas[k], StringComparison.OrdinalIgnoreCase));
                    var faltam = Colunas.Where((c, k) => mapa[k] < 0).ToList();
                    if (faltam.Count > 0) avisos.Add($"Colunas ausentes no cabeçalho (ignoradas): {string.Join(", ", faltam)}.");
                    continue;
                }
                for (int k = 0; k < Colunas.Length; k++) mapa[k] = k + 1;
            }

            string marca = colMarca < reg.Count ? (reg[colMarca] ?? "").Trim() : "";
            if (marca.Length == 0) { avisos.Add($"Linha {linha}: sem marca (ignorada)."); continue; }
            var c = new CargaFundacao { Marca = marca };
            for (int k = 0; k < Colunas.Length; k++)
            {
                int col = mapa[k];
                if (col < 0 || col >= reg.Count || string.IsNullOrWhiteSpace(reg[col])) continue;
                if (TentarNumero(reg[col], out double v)) c.Valores[k] = v;
                else avisos.Add($"Linha {linha} ({marca}): \"{reg[col].Trim()}\" em {Colunas[k]} não é número (ignorado).");
            }
            if (porMarca.ContainsKey(marca))
            {
                avisos.Add($"Linha {linha}: marca {marca} repetida; vale a última.");
                r.Remove(porMarca[marca]);
            }
            porMarca[marca] = c;
            r.Add(c);
        }
        return r;
    }

    /// <summary>Texto do arquivo (cabeçalho + uma linha por pilar), ponto decimal.</summary>
    public static string Gravar(IEnumerable<CargaFundacao> cargas)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Cabecalho);
        foreach (CargaFundacao c in cargas)
            sb.AppendLine(Csv.Linha(new[] { c.Marca }.Concat(c.Valores.Select(v => v == null ? "" : Fmt.N(v.Value, "0.###")))));
        return sb.ToString();
    }

    /// <summary>Número com vírgula ou ponto decimal ("12,5", "12.5", "1.234,5", "1,234.5", "-3").</summary>
    public static bool TentarNumero(string s, out double v)
    {
        s = (s ?? "").Trim().Replace(" ", "");
        int pv = s.LastIndexOf(','), pp = s.LastIndexOf('.');
        if (pv >= 0 && pp >= 0)
            s = pv > pp ? s.Replace(".", "").Replace(',', '.') : s.Replace(",", "");
        else if (pv >= 0)
            s = s.Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsNaN(v) && !double.IsInfinity(v);
    }

    /// <summary>Tira o prefixo do parâmetro do cabeçalho: "PCEB_Fz_Max" → "Fz_Max" (aceita também planilhas antigas com "SD_").</summary>
    private static string SemPrefixo(string n) =>
        n.StartsWith("PCEB_", StringComparison.OrdinalIgnoreCase) ? n.Substring(5)
        : n.StartsWith("SD_", StringComparison.OrdinalIgnoreCase) ? n.Substring(3) : n;
}
