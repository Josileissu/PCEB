using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DEB.Core.Templates;

/// <summary>
/// Biblioteca de detalhes típicos do escritório (decisão do usuário, 2026-11-08): um DWG por detalhe numa pasta; os
/// templates só PEDEM o detalhe pelo nome (<see cref="ResultadoTemplate.DetalhesBiblioteca"/>) e o CAD (depois o Revit)
/// insere o arquivo como bloco, empilhado numa coluna à direita da folha (<see cref="ResultadoTemplate.ColunaDetalhes"/>),
/// que o usuário ajusta depois. Nada é desenhado por código.
/// <para>Unidades: o arquivo "detalhes.csv" da pasta (Nome;Unidade) diz quais estão em unidades de FOLHA (mm do papel,
/// multiplicados pela escala do desenho ao inserir); os outros estão em cm do desenho (entram em tamanho real).</para>
/// </summary>
public static class BibliotecaDetalhes
{
    public const string ChavesCisalhamento = "chaves_cisalhamento", RaioEmendasComprimento = "raio_curvatura_emendas_comprimento",
        Spda = "spda", AncoragemEstacas = "ancoragem_estacas", AterramentoPilar = "aterramento_pilar", SoldaTirante = "solda_tirante";

    /// <summary>%AppData%\PCEB\DEB.Cad\Detalhes.</summary>
    public static string PastaPadrao() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCEB", "DEB.Cad", "Detalhes");

    public static string Arquivo(string nome, string pasta = null) => Path.Combine(pasta ?? PastaPadrao(), nome + ".dwg");

    /// <summary>Se o detalhe está em unidades de folha (detalhes.csv da pasta: "nome;folha"). Sem o arquivo: não.</summary>
    public static bool EmUnidadesDeFolha(string nome, string pasta = null)
    {
        try
        {
            var csv = Path.Combine(pasta ?? PastaPadrao(), "detalhes.csv");
            if (!File.Exists(csv)) return false;
            return File.ReadAllLines(csv).Select(l => l.Split(';')).Any(c => c.Length >= 2
                && string.Equals(c[0].Trim().TrimStart('﻿'), nome, StringComparison.OrdinalIgnoreCase)
                && c[1].Trim().Equals("folha", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    /// <summary>Fator do arquivo (3ª coluna do detalhes.csv; ex.: 0.4 quando o bloco estava inserido com escala 0,4 na
    /// folha de origem). Sem o valor: 1.</summary>
    public static double FatorArquivo(string nome, string pasta = null)
    {
        try
        {
            var csv = Path.Combine(pasta ?? PastaPadrao(), "detalhes.csv");
            if (!File.Exists(csv)) return 1;
            foreach (var c in File.ReadAllLines(csv).Select(l => l.Split(';')))
                if (c.Length >= 3 && string.Equals(c[0].Trim().TrimStart('﻿'), nome, StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(c[2].Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) && f > 0)
                    return f;
        }
        catch { }
        return 1;
    }

    /// <summary>Pede o detalhe (sem repetir).</summary>
    public static void Pedir(ResultadoTemplate r, string nome)
    {
        if (!r.DetalhesBiblioteca.Contains(nome, StringComparer.OrdinalIgnoreCase)) r.DetalhesBiblioteca.Add(nome);
    }

    /// <summary>Coluna dos detalhes: à direita de tudo o que o template desenhou, a partir do topo. Chamar no fim.</summary>
    public static void PosicionarColuna(ResultadoTemplate r, double alturaTexto)
    {
        if (r.DetalhesBiblioteca.Count == 0) return;
        var lim = DEB.Core.Motor.Prancha.Geo.Limites(r, alturaTexto);
        r.ColunaDetalhes = lim.Vazio ? (0, 0) : (lim.X1 + 12 * alturaTexto, lim.Y1);
    }
}
