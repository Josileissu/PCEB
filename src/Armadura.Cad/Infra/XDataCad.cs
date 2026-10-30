using System;
using System.Collections.Generic;
using System.Linq;
using Armadura.Core.Modelo;
using Armadura.Core.Persistencia;

namespace Armadura.Cad.Infra;

/// <summary>Converte os dados do núcleo (CampoXData) para XData da RegApp ARMADURA_BR e vice-versa.</summary>
internal static class XDataCad
{
    public const string App = CodecDados.RegApp;

    public static void GarantirRegApp(Database db, Transaction tr)
    {
        var t = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
        if (t.Has(App)) return;
        t.UpgradeOpen();
        var r = new RegAppTableRecord { Name = App };
        t.Add(r);
        tr.AddNewlyCreatedDBObject(r, true);
    }

    public static bool TemDados(DBObject o)
    {
        using var rb = o.GetXDataForApplication(App);
        return rb != null;
    }

    /// <summary>Campos depois do 1001 da RegApp; null se o objeto não tiver dados do plugin.</summary>
    public static List<CampoXData> Ler(DBObject o)
    {
        using var rb = o.GetXDataForApplication(App);
        if (rb == null) return null;
        return rb.AsArray()
            .Where(tv => tv.TypeCode != (short)DxfCode.ExtendedDataRegAppName)
            .Select(tv => new CampoXData(tv.TypeCode, tv.TypeCode == CampoXData.Handle ? HandleTexto(tv.Value) : tv.Value))
            .ToList();
    }

    public static void Gravar(DBObject o, List<CampoXData> campos)
    {
        var tvs = new List<TypedValue> { new TypedValue((short)DxfCode.ExtendedDataRegAppName, App) };
        foreach (var c in campos)
        {
            object v = c.Codigo switch
            {
                CampoXData.Inteiro16 => Convert.ToInt16(c.Valor),
                CampoXData.Inteiro32 => Convert.ToInt32(c.Valor),
                CampoXData.Real => Convert.ToDouble(c.Valor),
                CampoXData.Handle => new Handle(Convert.ToInt64(c.Valor.ToString(), 16)),
                _ => c.Valor?.ToString() ?? "",
            };
            tvs.Add(new TypedValue(c.Codigo, v));
        }
        using var rb = new ResultBuffer(tvs.ToArray());
        o.XData = rb;
    }

    public static TipoParte Tipo(DBObject o)
    {
        var c = Ler(o);
        return c == null ? TipoParte.Nenhum : CodecDados.LerTipo(c);
    }

    public static DadosArmadura LerBarra(DBObject o)
    {
        var c = Ler(o);
        return c == null ? null : CodecDados.DecodificarBarra(c);
    }

    public static Vinculo LerVinculo(DBObject o)
    {
        var c = Ler(o);
        return c == null ? null : CodecDados.DecodificarVinculo(c);
    }

    /// <summary>Grava só se mudou (evita modificar o objeto e gerar undo/eventos à toa).</summary>
    // atalhos: gravar os dados do núcleo direto (antes: Gravar(o, CodecDados.Codificar(x)) em 45 lugares)
    public static void Gravar(DBObject o, DadosArmadura d) => Gravar(o, CodecDados.Codificar(d));
    public static void Gravar(DBObject o, DadosFaixa f) => Gravar(o, CodecDados.Codificar(f));
    public static void Gravar(DBObject o, Vinculo v) => Gravar(o, CodecDados.Codificar(v));
    public static void Gravar(DBObject o, DadosDetalhamento d) => Gravar(o, CodecDados.Codificar(d));
    public static void Gravar(DBObject o, Armadura.Core.RelacaoAco.OpcoesTabelaRA op) => Gravar(o, CodecDados.Codificar(op));
    public static bool GravarSeMudou(DBObject o, DadosArmadura d) => GravarSeMudou(o, CodecDados.Codificar(d));
    public static bool GravarSeMudou(DBObject o, DadosFaixa f) => GravarSeMudou(o, CodecDados.Codificar(f));

    /// <summary>Barra (polilinha com dados de barra) do handle guardado numa parte; null se não existe ou não é barra.</summary>
    public static Polyline BarraDoHandle(Transaction tr, Database db, string handle)
    {
        var id = IdDoHandle(db, handle);
        if (id.IsNull || id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Polyline pl)) return null;
        return Tipo(pl) == TipoParte.Barra ? pl : null;
    }

    /// <summary>A lista de handles (da barra/faixa) contém o handle do objeto? (vínculo recíproco)</summary>
    public static bool Lista(IEnumerable<string> handles, ObjectId id)
    {
        string h = Hex(id);
        return handles != null && handles.Any(x => string.Equals(x, h, StringComparison.OrdinalIgnoreCase));
    }

    public static bool GravarSeMudou(DBObject o, List<CampoXData> novos)
    {
        var atuais = Ler(o);
        if (atuais != null && Iguais(atuais, novos)) return false;
        if (!o.IsWriteEnabled) o.UpgradeOpen();
        Gravar(o, novos);
        return true;
    }

    private static bool Iguais(List<CampoXData> a, List<CampoXData> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Codigo != b[i].Codigo) return false;
            if (a[i].Codigo == CampoXData.Real)
            {
                if (Math.Abs(Convert.ToDouble(a[i].Valor) - Convert.ToDouble(b[i].Valor)) > 1e-9) return false;
            }
            else if (!Equals(Convert.ToString(a[i].Valor), Convert.ToString(b[i].Valor))) return false;
        }
        return true;
    }

    /// <summary>Valor de um 1005 lido do CAD (Handle ou texto) → hex.</summary>
    private static string HandleTexto(object v) => v switch
    {
        Handle h => h.Value.ToString("X"),
        long l => l.ToString("X"),
        _ => (v?.ToString() ?? "").Trim(),
    };

    // ---------- handles ----------

    public static string Hex(ObjectId id) => id.Handle.Value.ToString("X");

    public static ObjectId IdDoHandle(Database db, string hex)
    {
        if (string.IsNullOrEmpty(hex)) return ObjectId.Null;
        try
        {
            var h = new Handle(Convert.ToInt64(hex, 16));
            return db.TryGetObjectId(h, out var id) ? id : ObjectId.Null;
        }
        catch { return ObjectId.Null; }
    }
}
