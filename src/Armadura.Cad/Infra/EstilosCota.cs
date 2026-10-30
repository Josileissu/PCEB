using System;
using System.Globalization;

namespace Armadura.Cad.Infra;

/// <summary>
/// Estilos de cota do escritório (P56, 2026-10-30): ESTRUT50, ESTRUT25… — texto 9, tique oblíquo 9, prolongamento 9, afastamento
/// 5, folga 3, texto acima da linha, 1 casa; DIMSCALE = escala / 50 (desenho em cm). Se o desenho já tem o estilo (prancha do
/// escritório), usa o dele sem mudar nada.
/// </summary>
internal static class EstilosCota
{
    public static ObjectId Garantir(Database db, Transaction tr, double escala, double cmPorUnidade)
    {
        if (escala <= 0) escala = 50;
        string nome = "ESTRUT" + escala.ToString("0", CultureInfo.InvariantCulture);
        var t = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
        if (t.Has(nome)) return t[nome];
        double u = 1 / (cmPorUnidade > 0 ? cmPorUnidade : 1);
        var e = new DimStyleTableRecord
        {
            Name = nome,
            Dimtxt = 9 * u, Dimasz = 9 * u, Dimtsz = 9 * u, Dimexe = 9 * u, Dimexo = 5 * u, Dimgap = 3 * u,
            Dimscale = escala / 50, Dimtad = 1, Dimdec = 1, Dimzin = 8, Dimtih = false, Dimtoh = false, // 8 = sem zeros à direita ("40", "28.5")
        };
        if (db.Textstyle.IsValid) e.Dimtxsty = db.Textstyle;
        t.UpgradeOpen();
        var id = t.Add(e);
        tr.AddNewlyCreatedDBObject(e, true);
        return id;
    }
}
