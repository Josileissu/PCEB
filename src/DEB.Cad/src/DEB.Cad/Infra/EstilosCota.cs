using System;
using System.Globalization;
using DEB.Core.Configuracao;

namespace DEB.Cad.Infra;

/// <summary>
/// Estilos de cota do escritório (P56, 2026-10-30): ESTRUT50, ESTRUT25… — texto 9, tique oblíquo 9, prolongamento 9, afastamento
/// 5, folga 3, texto acima da linha, 1 casa; DIMSCALE = escala / 50 (desenho em cm). Se o desenho já tem o estilo (prancha do
/// escritório), usa o dele sem mudar nada.
/// </summary>
internal static class EstilosCota
{
    /// <param name="cfg">Unidade de apresentação (2026-11-10): o estilo mede em cm, m ou mm (DIMLFAC = unidades de apresentação por
    /// unidade do desenho) e o nome ganha o sufixo quando não é o padrão (desenho em cm, cotas em cm): ESTRUT50-MM.</param>
    public static ObjectId Garantir(Database db, Transaction tr, double escala, double cmPorUnidade, ConfiguracaoArmadura cfg = null)
    {
        if (escala <= 0) escala = 50;
        var ap = cfg?.UnidadeApresentacao ?? UnidadeApresentacao.Centimetro;
        double lfac = (cmPorUnidade > 0 ? cmPorUnidade : 1) * (cfg == null ? 1 : Core.Calculo.CalculoComprimento.FatorApresentacao(cfg));
        int dec = ap == UnidadeApresentacao.Metro ? 3 : ap == UnidadeApresentacao.Milimetro ? 0 : 1;
        string nome = "ESTRUT" + escala.ToString("0", CultureInfo.InvariantCulture) +
                      (Math.Abs(lfac - 1) < 1e-9 && ap == UnidadeApresentacao.Centimetro ? "" : "-" + Core.Calculo.CalculoComprimento.Sufixo(cfg).ToUpperInvariant() +
                       (Math.Abs((cmPorUnidade > 0 ? cmPorUnidade : 1) - 1) < 1e-9 ? "" : "-U" + (cmPorUnidade >= 10 ? "M" : "MM")));
        var t = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
        if (t.Has(nome)) return t[nome];
        double u = 1 / (cmPorUnidade > 0 ? cmPorUnidade : 1);
        var e = new DimStyleTableRecord
        {
            Name = nome,
            Dimtxt = 9 * u, Dimasz = 9 * u, Dimtsz = 9 * u, Dimexe = 9 * u, Dimexo = 5 * u, Dimgap = 3 * u,
            Dimscale = escala / 50, Dimtad = 1, Dimdec = dec, Dimlfac = lfac, Dimzin = 8, Dimtih = false, Dimtoh = false, // 8 = sem zeros à direita ("40", "28.5")
        };
        if (db.Textstyle.IsValid) e.Dimtxsty = db.Textstyle;
        t.UpgradeOpen();
        var id = t.Add(e);
        tr.AddNewlyCreatedDBObject(e, true);
        return id;
    }
}
