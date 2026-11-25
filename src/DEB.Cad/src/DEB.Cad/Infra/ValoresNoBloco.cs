using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Motor;

namespace DEB.Cad.Infra;

/// <summary>
/// Valores do template guardados no BLOCO do detalhamento (D51, 25/11/2026: "a partir de um detalhamento fazer a análise da viga" e
/// "atualizar um detalhamento"): Xrecord "PCEB_TEMPLATE" no dicionário de extensão da definição do bloco, com a id do template, o
/// deslocamento entre o ponto de inserção do bloco e a origem do template (o bloco pode ser movido: a origem acompanha) e os
/// parâmetros (1 = nome, 3 = valor em pedaços de 200). O DWG abre sem o plugin (é um Xrecord comum).
/// </summary>
internal static class ValoresNoBloco
{
    public const string Chave = "PCEB_TEMPLATE";

    public static List<TypedValue> Parametros(ValoresTemplate v)
    {
        var tvs = new List<TypedValue>();
        foreach (var kv in v.Todos)
        {
            tvs.Add(new TypedValue((int)DxfCode.Text, kv.Key));
            string s = kv.Value ?? "";
            for (int i = 0; i < s.Length; i += 200) tvs.Add(new TypedValue(3, s.Substring(i, Math.Min(200, s.Length - i))));
        }
        return tvs;
    }

    public static ValoresTemplate LerParametros(IEnumerable<TypedValue> tvs, IEnumerable<ParametroTemplate> parametros)
    {
        var v = new ValoresTemplate(parametros);
        string nome = null;
        var valor = new System.Text.StringBuilder();
        void Fechar() { if (nome != null) v[nome] = valor.ToString(); valor.Clear(); }
        foreach (var tv in tvs)
        {
            if (tv.TypeCode == (short)DxfCode.Text) { Fechar(); nome = tv.Value as string; }
            else if (tv.TypeCode == 3) valor.Append(tv.Value as string);
        }
        Fechar();
        return v;
    }

    /// <summary>Grava no bloco (definição aberta para escrita).</summary>
    public static void Gravar(Transaction tr, BlockTableRecord btr, string templateId, ValoresTemplate v, Vector3d deslocamento)
    {
        if (btr.ExtensionDictionary.IsNull) { btr.UpgradeOpen(); btr.CreateExtensionDictionary(); }
        var dic = (DBDictionary)tr.GetObject(btr.ExtensionDictionary, OpenMode.ForWrite);
        var tvs = new List<TypedValue> { new TypedValue((int)DxfCode.BlockName, templateId ?? ""), new TypedValue((int)DxfCode.XCoordinate, new Point3d(deslocamento.X, deslocamento.Y, deslocamento.Z)) };
        tvs.AddRange(Parametros(v));
        using var rb = new ResultBuffer(tvs.ToArray());
        if (dic.Contains(Chave)) ((Xrecord)tr.GetObject(dic.GetAt(Chave), OpenMode.ForWrite)).Data = rb;
        else { var xr = new Xrecord { Data = rb }; dic.SetAt(Chave, xr); tr.AddNewlyCreatedDBObject(xr, true); }
    }

    /// <summary>Lê do bloco: id do template, valores e deslocamento; null se o bloco não tem os valores (detalhamento antigo).</summary>
    public static (string TemplateId, ValoresTemplate Valores, Vector3d Deslocamento)? Ler(Transaction tr, BlockTableRecord btr)
    {
        if (btr.ExtensionDictionary.IsNull) return null;
        var dic = (DBDictionary)tr.GetObject(btr.ExtensionDictionary, OpenMode.ForRead);
        if (!dic.Contains(Chave)) return null;
        using var rb = ((Xrecord)tr.GetObject(dic.GetAt(Chave), OpenMode.ForRead)).Data;
        var arr = rb?.AsArray() ?? Array.Empty<TypedValue>();
        string id = arr.Where(t => t.TypeCode == (short)DxfCode.BlockName).Select(t => t.Value as string).FirstOrDefault() ?? "";
        var p = arr.Where(t => t.TypeCode == (short)DxfCode.XCoordinate).Select(t => (Point3d)t.Value).FirstOrDefault();
        var t0 = CatalogoTemplates.Obter(id);
        if (t0 == null) return null;
        return (id, LerParametros(arr, t0.Parametros), new Vector3d(p.X, p.Y, p.Z));
    }
}
