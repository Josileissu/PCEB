using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Armadura.Core.Configuracao;

namespace Armadura.Cad.Infra;

/// <summary>
/// Padrões de layer do arquivo layers.csv (pasta de dados do plugin; criado com o padrão de fábrica se não existir). O arquivo
/// é relido quando muda, sem reabrir o CAD.
/// </summary>
internal static class Layers
{
    public static string Caminho => Path.Combine(Log.PastaDados, "layers.csv");

    private static Dictionary<string, PadraoLayer> _cache;
    private static DateTime _lido;

    /// <summary>Cria o arquivo com o padrão de fábrica, se ainda não existe.</summary>
    public static void GarantirArquivo()
    {
        if (File.Exists(Caminho)) return;
        Directory.CreateDirectory(Log.PastaDados);
        // UTF-8 com BOM: o Excel abre os acentos certos
        File.WriteAllText(Caminho, PadroesLayers.Escrever(PadroesLayers.Padrao()), new UTF8Encoding(true));
    }

    /// <summary>Padrão do layer (null se o arquivo não o lista).</summary>
    public static PadraoLayer Obter(string nome)
    {
        try
        {
            GarantirArquivo();
            var t = File.GetLastWriteTimeUtc(Caminho);
            if (_cache == null || t != _lido)
            {
                var avisos = new List<string>();
                _cache = PadroesLayers.Ler(File.ReadAllText(Caminho, Encoding.UTF8), avisos)
                    .ToDictionary(l => l.Nome, StringComparer.OrdinalIgnoreCase);
                _lido = t;
                foreach (var a in avisos) Log.Info(a);
            }
        }
        catch (Exception ex)
        {
            Log.Erro("layers.csv", ex);
            _cache ??= PadroesLayers.Padrao().ToDictionary(l => l.Nome, StringComparer.OrdinalIgnoreCase);
        }
        return _cache.TryGetValue(nome, out var p) ? p : null;
    }

    /// <summary>Aplica cor, espessura e tipo de linha ao layer (o padrão do arquivo, ou a cor dada com 0,00 mm).</summary>
    public static void Aplicar(Database db, Transaction tr, LayerTableRecord l, PadraoLayer p)
    {
        l.Color = Color.FromColorIndex(ColorMethod.ByAci, p.Cor);
        l.LineWeight = (LineWeight)PadroesLayers.Centesimos(p.EspessuraMm);
        var tipo = TipoLinha(db, tr, p.TipoLinha);
        if (!tipo.IsNull) l.LinetypeObjectId = tipo;
    }

    /// <summary>Tipo de linha do desenho (carregado do gcadiso.lin/gcad.lin se faltar); vazio = Continuous.</summary>
    private static ObjectId TipoLinha(Database db, Transaction tr, string nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) nome = "Continuous";
        var lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        if (lt.Has(nome)) return lt[nome];
        foreach (var arq in new[] { "gcadiso.lin", "gcad.lin", "acadiso.lin", "acad.lin" })
        {
            try
            {
                db.LoadLineTypeFile(nome, arq);
                lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (lt.Has(nome)) return lt[nome];
            }
            catch (Exception) { }
        }
        Log.Info($"layers.csv: tipo de linha \"{nome}\" não encontrado; o layer fica contínuo.");
        return ObjectId.Null;
    }
}
