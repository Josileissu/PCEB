using System.Linq;
using Armadura.Core.Numeracao;

namespace Armadura.Cad.Infra;

/// <summary>
/// Estado da relação do aço no desenho (Xrecord ARMADURA_BR_ESTADO no NOD): bloqueio (seção 4.3),
/// maior número já usado e o tipo/número inicial vigentes no bloqueio.
/// </summary>
internal sealed class EstadoRA
{
    public const string Chave = "ARMADURA_BR_ESTADO";

    public bool Bloqueada { get; set; }
    public int MaiorNumero { get; set; }
    public TipoNumeracao Tipo { get; set; }
    public int NumeroInicial { get; set; } = 1;

    public static EstadoRA Ler(Database db, Transaction tr)
    {
        var e = new EstadoRA();
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (!nod.Contains(Chave)) return e;
        var xr = (Xrecord)tr.GetObject(nod.GetAt(Chave), OpenMode.ForRead);
        using var rb = xr.Data;
        var v = rb?.AsArray().Select(t => System.Convert.ToInt32(t.Value)).ToArray() ?? new int[0];
        if (v.Length >= 4)
        {
            e.Bloqueada = v[0] != 0;
            e.MaiorNumero = v[1];
            e.Tipo = (TipoNumeracao)v[2];
            e.NumeroInicial = v[3];
        }
        return e;
    }

    public void Gravar(Database db, Transaction tr)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
        using var rb = new ResultBuffer(
            new TypedValue((short)DxfCode.Int32, Bloqueada ? 1 : 0),
            new TypedValue((short)DxfCode.Int32, MaiorNumero),
            new TypedValue((short)DxfCode.Int32, (int)Tipo),
            new TypedValue((short)DxfCode.Int32, NumeroInicial));
        if (nod.Contains(Chave))
        {
            var xr = (Xrecord)tr.GetObject(nod.GetAt(Chave), OpenMode.ForWrite);
            xr.Data = rb;
        }
        else
        {
            var xr = new Xrecord { Data = rb };
            nod.SetAt(Chave, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }
    }
}
