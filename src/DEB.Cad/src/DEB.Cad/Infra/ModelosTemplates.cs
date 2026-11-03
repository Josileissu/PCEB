using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DEB.Core.Templates;

namespace DEB.Cad.Infra;

/// <summary>
/// Arquivos de modelos dos templates (pedido do usuário, 2026-10-30): %AppData%\PCEB\DEB.Cad\Templates\&lt;id&gt;.csv, criados
/// com o padrão do plugin ao carregar o GstarCAD e relidos sempre que mudam (editados no Excel, importados…).
/// </summary>
internal static class ModelosTemplates
{
    public static string Pasta => Path.Combine(Log.PastaDados, "Templates");
    public static string Caminho(ITemplateArmadura t) => Path.Combine(Pasta, t.Id + ".csv");

    private static readonly Dictionary<string, (DateTime lido, ArquivoModelos arq)> Cache = new Dictionary<string, (DateTime, ArquivoModelos)>();

    /// <summary>Ao carregar o plugin: cria o arquivo de cada template que ainda não tem e lê todos.</summary>
    public static void Carregar()
    {
        foreach (var t in CatalogoTemplates.Todos)
        {
            try
            {
                var a = Obter(t);
                Log.Info($"Modelos do template {t.Id}: {a.Modelos.Count} (arquivo {Caminho(t)}).");
            }
            catch (Exception ex) { Log.Erro("Modelos do template " + t.Id, ex); }
        }
    }

    /// <summary>Arquivo do template (relido se mudou desde a última leitura).</summary>
    public static ArquivoModelos Obter(ITemplateArmadura t)
    {
        var caminho = Caminho(t);
        if (!File.Exists(caminho)) Gravar(t, ArquivoModelos.Novo(t));
        var quando = File.GetLastWriteTimeUtc(caminho);
        if (Cache.TryGetValue(t.Id, out var c) && c.lido == quando) return c.arq;
        var avisos = new List<string>();
        var arq = ArquivoModelos.Ler(File.ReadAllText(caminho, Encoding.UTF8), t, avisos);
        foreach (var a in avisos) Log.Info($"{Path.GetFileName(caminho)}: {a}");
        Cache[t.Id] = (quando, arq);
        return arq;
    }

    public static void Gravar(ITemplateArmadura t, ArquivoModelos arq)
    {
        Directory.CreateDirectory(Pasta);
        var caminho = Caminho(t);
        File.WriteAllText(caminho, arq.Escrever(t), new UTF8Encoding(true)); // com BOM: o Excel abre os acentos certos
        Cache[t.Id] = (File.GetLastWriteTimeUtc(caminho), arq);
    }

    /// <summary>Lê um arquivo qualquer (importação) com os tipos do template.</summary>
    public static ArquivoModelos LerArquivo(string caminho, ITemplateArmadura t, List<string> avisos) =>
        ArquivoModelos.Ler(File.ReadAllText(caminho, Encoding.UTF8), t, avisos);

    /// <summary>Template a que um arquivo pertence ("# template: id" na 1ª linha); null se não diz.</summary>
    public static ITemplateArmadura TemplateDoArquivo(string caminho)
    {
        var a = ArquivoModelos.Ler(File.ReadAllText(caminho, Encoding.UTF8), null);
        return CatalogoTemplates.Obter(a.TemplateId);
    }
}
