namespace DEB.Cad.Infra;

/// <summary>
/// Diferenças de API entre os CADs/versões, num lugar só (o resto do código chama estes métodos).
/// ZWCAD 2024: sem StartOpenCloseTransaction, sem Editor.Command e DxfOut com um parâmetro a mais.
/// </summary>
internal static class Compat
{
    /// <summary>Transação leve para ler/alterar objetos (OpenClose onde existe).</summary>
    public static Transaction TransacaoLeve(this Database db) =>
#if ZWCAD2024
        db.TransactionManager.StartTransaction();
#else
        db.TransactionManager.StartOpenCloseTransaction();
#endif

    /// <summary>Zoom extensão a partir de um comando.</summary>
    public static void ZoomExtensao(this Editor ed)
    {
#if ZWCAD2024
        ed.Document.SendStringToExecute("_.ZOOM _E ", true, false, false);
#else
        ed.Command("_.ZOOM", "_E");
#endif
    }

    public static void SalvarDxf(this Database db, string caminho, int precisao) =>
#if ZWCAD2024
        db.DxfOut(caminho, precisao, DwgVersion.Current, false);
#else
        db.DxfOut(caminho, precisao, DwgVersion.Current);
#endif
}
