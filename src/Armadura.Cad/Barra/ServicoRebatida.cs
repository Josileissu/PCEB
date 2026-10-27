using System;
using System.Collections.Generic;
using System.Linq;
using Armadura.Cad.Infra;
using Armadura.Core.Modelo;
using Armadura.Core.Persistencia;

namespace Armadura.Cad.Barra;

/// <summary>
/// Armadura rebatida (seção 2.2): polilinha "espelho" vinculada à barra, deslocada de (Dx, Dy).
/// A forma é sempre a mesma da barra: editar uma propaga para a outra; mover a rebatida só muda o
/// deslocamento guardado.
/// </summary>
internal static class ServicoRebatida
{
    /// <summary>
    /// ARMREBATER: a barra (com descrição) vai para o destino e fica uma rebatida na posição original.
    /// </summary>
    public static ObjectId Rebater(Transaction tr, Polyline barra, Vector2d deslocamento)
    {
        var db = barra.Database;
        var d = XDataCad.LerBarra(barra);
        string hBarra = XDataCad.Hex(barra.ObjectId);

        var reb = (Polyline)barra.Clone(); // o XData da barra é substituído logo abaixo
        var owner = (BlockTableRecord)tr.GetObject(barra.OwnerId, OpenMode.ForWrite);
        owner.AppendEntity(reb);
        tr.AddNewlyCreatedDBObject(reb, true);
        XDataCad.Gravar(reb, CodecDados.Codificar(new Vinculo
        {
            Tipo = TipoParte.Rebatida, HandleBarra = hBarra, Dx = deslocamento.X, Dy = deslocamento.Y,
        }));

        // a barra e a descrição vão para o destino (as cotas acompanham na atualização)
        var m = Matrix3d.Displacement(new Vector3d(deslocamento.X, deslocamento.Y, 0));
        if (!barra.IsWriteEnabled) barra.UpgradeOpen();
        barra.TransformBy(m);
        var desc = ServicoBarra.Parte<MText>(tr, db, d.HandleDescricao, TipoParte.Descricao, hBarra);
        if (desc != null) { desc.UpgradeOpen(); desc.TransformBy(m); }

        d.Rebatidas.Add(XDataCad.Hex(reb.ObjectId));
        XDataCad.Gravar(barra, CodecDados.Codificar(d));
        return reb.ObjectId;
    }

    /// <summary>Rebatida válida da barra (tipo certo e apontando para ela).</summary>
    public static Polyline Ler(Transaction tr, Database db, string handle, string hBarra, out Vinculo v)
    {
        v = null;
        var id = XDataCad.IdDoHandle(db, handle);
        if (id.IsNull || id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Polyline pl)) return null;
        v = XDataCad.LerVinculo(pl);
        return v != null && v.Tipo == TipoParte.Rebatida
               && string.Equals(v.HandleBarra, hBarra, StringComparison.OrdinalIgnoreCase) ? pl : null;
    }

    /// <summary>Barra apontada pela rebatida (mesmo que ainda não a liste — caso das cópias).</summary>
    public static Polyline BarraApontada(Transaction tr, Entity reb)
    {
        var v = XDataCad.LerVinculo(reb);
        if (v == null || v.Tipo != TipoParte.Rebatida) return null;
        var id = XDataCad.IdDoHandle(reb.Database, v.HandleBarra);
        if (id.IsNull || id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Polyline pl)) return null;
        return XDataCad.Tipo(pl) == TipoParte.Barra ? pl : null;
    }

    /// <summary>Barra da rebatida, se a barra listar esta rebatida.</summary>
    public static Polyline BarraDaRebatida(Transaction tr, Entity reb)
    {
        var pl = BarraApontada(tr, reb);
        if (pl == null) return null;
        string h = XDataCad.Hex(reb.ObjectId);
        return XDataCad.LerBarra(pl).Rebatidas.Any(x => string.Equals(x, h, StringComparison.OrdinalIgnoreCase)) ? pl : null;
    }

    /// <summary>
    /// A rebatida foi editada pelo usuário: se só mudou de lugar, guarda o novo deslocamento; se a
    /// forma mudou, leva a forma para a barra.
    /// </summary>
    public static void AoModificar(Transaction tr, Polyline reb)
    {
        var barra = BarraDaRebatida(tr, reb);
        if (barra == null) return;
        var v = XDataCad.LerVinculo(reb);
        if (MesmaForma(barra, reb))
        {
            var novo = barra.GetPoint2dAt(0) - reb.GetPoint2dAt(0);
            if ((novo - new Vector2d(v.Dx, v.Dy)).Length > 1e-9)
            {
                v.Dx = novo.X;
                v.Dy = novo.Y;
                reb.UpgradeOpen();
                XDataCad.Gravar(reb, CodecDados.Codificar(v));
            }
            return;
        }
        barra.UpgradeOpen();
        Copiar(reb, barra, new Vector2d(v.Dx, v.Dy));
    }

    /// <summary>Barra → rebatidas: mesma forma, cada uma no seu deslocamento. Retorna as válidas.</summary>
    public static List<string> Atualizar(Transaction tr, Polyline barra, DadosArmadura d, string hBarra)
    {
        var validas = new List<string>();
        foreach (var h in d.Rebatidas.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var reb = Ler(tr, barra.Database, h, hBarra, out var v);
            if (reb == null) continue;
            validas.Add(h);
            var desl = new Vector2d(-v.Dx, -v.Dy);
            if (!Coincide(barra, reb, desl))
            {
                reb.UpgradeOpen();
                Copiar(barra, reb, desl);
            }
            if (reb.Visible != d.Visivel || reb.LayerId != barra.LayerId)
            {
                if (!reb.IsWriteEnabled) reb.UpgradeOpen();
                reb.Visible = d.Visivel;
                reb.LayerId = barra.LayerId;
            }
        }
        return validas;
    }

    public static void ApagarTodas(Transaction tr, Database db, DadosArmadura d, string hBarra)
    {
        foreach (var h in d.Rebatidas)
        {
            var reb = Ler(tr, db, h, hBarra, out _);
            if (reb != null && !reb.IsErased) { reb.UpgradeOpen(); reb.Erase(); }
        }
    }

    // ---------- geometria ----------

    /// <summary>Mesma forma (vértices relativos ao 1º e bulges), em qualquer posição.</summary>
    private static bool MesmaForma(Polyline a, Polyline b) =>
        a.NumberOfVertices == b.NumberOfVertices && a.Closed == b.Closed &&
        Coincide(a, b, b.GetPoint2dAt(0) - a.GetPoint2dAt(0));

    /// <summary>b = a + deslocamento, vértice a vértice (e mesmos bulges).</summary>
    private static bool Coincide(Polyline a, Polyline b, Vector2d desl)
    {
        if (a.NumberOfVertices != b.NumberOfVertices || a.Closed != b.Closed) return false;
        for (int i = 0; i < a.NumberOfVertices; i++)
        {
            if ((a.GetPoint2dAt(i) + desl).GetDistanceTo(b.GetPoint2dAt(i)) > 1e-6) return false;
            if (Math.Abs(a.GetBulgeAt(i) - b.GetBulgeAt(i)) > 1e-9) return false;
        }
        return true;
    }

    /// <summary>Faz <paramref name="destino"/> = <paramref name="origem"/> + deslocamento (vértices, bulges, fechada).</summary>
    private static void Copiar(Polyline origem, Polyline destino, Vector2d desl)
    {
        int n = origem.NumberOfVertices;
        while (destino.NumberOfVertices > n) destino.RemoveVertexAt(destino.NumberOfVertices - 1);
        for (int i = 0; i < n; i++)
        {
            var p = origem.GetPoint2dAt(i) + desl;
            if (i < destino.NumberOfVertices)
            {
                destino.SetPointAt(i, p);
                destino.SetBulgeAt(i, origem.GetBulgeAt(i));
            }
            else destino.AddVertexAt(i, p, origem.GetBulgeAt(i), 0, 0);
        }
        destino.Closed = origem.Closed;
    }
}
