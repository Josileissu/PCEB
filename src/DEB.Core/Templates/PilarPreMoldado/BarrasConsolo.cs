namespace DEB.Core.Templates.PilarPreMoldado;

/// <summary>
/// Onde o tirante e a costura do consolo terminam dentro do pilar — regra única do desenho do CAD
/// (<see cref="TemplatePilarPreMoldado"/>, detalhe "ARMADURAS console") e do modelo 3D do Revit
/// (<see cref="Modelo3D.ColumnRebarLayout"/>). Decisão do dono (D30, item 3, 16/11/2026): vale a solução do modelo 3D, que
/// não bate nas longitudinais — a barra para por dentro das longitudinais da face oposta (face oposta − cobrimento − φ do
/// estribo − φ da longitudinal), também com consolos opostos (cada consolo com o seu tirante e a sua costura, sem barra
/// passante). Medidas em cm, externas (convenção do CAD, D7); bitolas em mm.
/// </summary>
public static class BarrasConsolo
{
    /// <summary>
    /// Distância da face do pilar onde está o consolo até a face externa da barra no outro lado: a face interna das
    /// longitudinais da face oposta.
    /// </summary>
    /// <param name="profundidade">lado do pilar na direção do balanço</param>
    /// <param name="cobOposta">cobrimento da face oposta (até o estribo)</param>
    public static double FimNoPilar(double profundidade, double cobOposta, double phiEstriboMm, double phiLongMm) =>
        profundidade - cobOposta - phiEstriboMm / 10 - phiLongMm / 10;

    /// <summary>
    /// Trecho ao longo do balanço do tirante e da costura (medida externa): da ponta do consolo menos o cobrimento até
    /// <see cref="FimNoPilar"/>.
    /// </summary>
    public static double Horizontal(double balanco, double cobPonta, double fimNoPilar) => balanco - cobPonta + fimNoPilar;
}
