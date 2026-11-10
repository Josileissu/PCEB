using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Templates;

/// <summary>
/// Tipos de catálogo (dente Gerber, consolo) criados pela importação do Revit com a GEOMETRIA do modelo e a ARMADURA PADRÃO
/// (2026-11-10): enquanto o usuário não conferir a armadura, o detalhamento que usa o tipo leva um aviso em vermelho na prancha
/// — antes o aviso só aparecia na linha de comando e o desenho saía igual ao de um tipo dimensionado. O aviso some quando o
/// usuário troca (ou apaga) a descrição do tipo no ARMDENTES / ARMCONSOLOS, que é como ele diz "conferi".
/// </summary>
public static class MarcaConferir
{
    /// <summary>Descrição com que a importação cria o tipo.</summary>
    public const string Origem = "Criado na importação do Revit: armadura padrão, conferir";
    public const string Layer = "ARM-CORTE";   // vermelho

    public static bool Pendente(string descricaoDoTipo) => descricaoDoTipo == Origem;

    /// <summary>Linhas do aviso (PT e EN conforme o idioma) para os tipos pendentes usados; vazio se não há nenhum.</summary>
    public static string[] Linhas(IEnumerable<string> tiposPendentes, System.Func<string, string, string[]> linhas)
    {
        var t = tiposPendentes.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        if (t.Count == 0) return new string[0];
        string lista = string.Join(", ", t);
        return linhas($"ATENÇÃO: {lista} — CRIADO NA IMPORTAÇÃO DO REVIT COM A ARMADURA PADRÃO: CONFERIR A ARMADURA",
                      $"WARNING: {lista} — CREATED BY THE REVIT IMPORT WITH DEFAULT REINFORCEMENT: CHECK THE REINFORCEMENT");
    }
}
