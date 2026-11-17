using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using DEB.Core.Documentacao;

namespace DEB.Cad.Pdf;

/// <summary>
/// Plota UM layout de um documento aberto e ativo na impressora do lote, com uma configuração TEMPORÁRIA (PlotInfo.OverrideSettings):
/// o page setup salvo no layout não muda, salvo com "gravar no layout".
/// </summary>
internal static class PlotadorPdf
{
    /// <summary>Nome canônico (UserNNNNN…) da folha cujo nome LOCAL é <paramref name="nomeLocal"/>; null se a impressora não tiver.</summary>
    public static string MidiaCanonica(PlotSettingsValidator v, PlotSettings ps, string nomeLocal)
    {
        StringCollection lista = v.GetCanonicalMediaNameList(ps);
        string porCanonico = null;
        foreach (string c in lista)
        {
            string local;
            try { local = v.GetLocaleMediaName(ps, c); } catch { local = null; }
            if (string.Equals((local ?? "").Trim(), nomeLocal.Trim(), StringComparison.OrdinalIgnoreCase)) return c;
            if (string.Equals(c.Trim(), nomeLocal.Trim(), StringComparison.OrdinalIgnoreCase)) porCanonico = c;
        }
        return porCanonico;
    }

    /// <summary>Lista "canônico = local" das folhas da impressora (diagnóstico no log).</summary>
    public static List<string> ListarMidias(string impressora)
    {
        var res = new List<string>();
        var db = HostApplicationServices.WorkingDatabase;
        using var tr = db.TransactionManager.StartTransaction();
        var lay = (Layout)tr.GetObject(LayoutManager.Current.GetLayoutId(LayoutManager.Current.CurrentLayout), OpenMode.ForRead);
        using var ps = new PlotSettings(lay.ModelType);
        ps.CopyFrom(lay);
        var v = PlotSettingsValidator.Current;
        v.SetPlotConfigurationName(ps, impressora, null);
        v.RefreshLists(ps);
        foreach (string c in v.GetCanonicalMediaNameList(ps))
        {
            string local;
            try { local = v.GetLocaleMediaName(ps, c); } catch (Exception ex) { local = "(erro: " + ex.Message + ")"; }
            res.Add(c + " = " + local);
        }
        tr.Commit();
        return res;
    }

    public static List<string> EstilosDisponiveis()
    {
        var res = new List<string>();
        try { foreach (string s in PlotSettingsValidator.Current.GetPlotStyleSheetList()) res.Add(s); }
        catch (Exception ex) { Infra.Log.Erro("ARMPDFLOTE: lista de estilos", ex); }
        return res;
    }

    /// <summary>
    /// Plota <paramref name="f"/> (documento já ativo e travado pelo chamador). <paramref name="nomeTrabalho"/> = nome do trabalho
    /// de impressão (a PDF24 usa como nome do arquivo). Devolve um texto com o papel e a área usados (para o log).
    /// </summary>
    public static string Plotar(Document doc, FolhaPdf f, ConfigLotePdf cfg, string nomeTrabalho, bool gravarNoLayout)
    {
        var db = doc.Database;
        var lm = LayoutManager.Current;
        if (!string.Equals(lm.CurrentLayout, f.Layout, StringComparison.OrdinalIgnoreCase)) lm.CurrentLayout = f.Layout;
        string info;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var layId = lm.GetLayoutId(f.Layout);
            var lay = (Layout)tr.GetObject(layId, OpenMode.ForRead);
            using var ps = new PlotSettings(lay.ModelType);
            ps.CopyFrom(lay);
            var v = PlotSettingsValidator.Current;
            v.SetPlotConfigurationName(ps, cfg.Impressora, null);
            v.RefreshLists(ps);
            var canon = MidiaCanonica(v, ps, f.Formato.Nome)
                        ?? throw new InvalidOperationException($"a impressora \"{cfg.Impressora}\" não tem a folha \"{f.Formato.Nome}\"");
            v.SetPlotConfigurationName(ps, cfg.Impressora, canon);
            v.SetPlotPaperUnits(ps, PlotPaperUnit.Millimeters);
            if (cfg.Area == AreaPlotLote.Moldura && f.TemCaixa)
            {
                v.SetPlotWindowArea(ps, f.Caixa);
                v.SetPlotType(ps, PlotType.Window);
            }
            else if (cfg.Area == AreaPlotLote.Layout) v.SetPlotType(ps, PlotType.Layout);
            else v.SetPlotType(ps, PlotType.Extents);
            v.SetUseStandardScale(ps, true);
            v.SetStdScaleType(ps, StdScaleType.ScaleToFit);
            // folha deitada x desenho deitado: gira 90° quando as orientações não batem
            v.SetPlotRotation(ps, f.Deitada == f.Formato.Paisagem ? PlotRotation.Degrees000 : PlotRotation.Degrees090);
            v.SetPlotCentered(ps, true);
            if (!string.IsNullOrEmpty(cfg.EstiloPlotagem)) v.SetCurrentStyleSheet(ps, cfg.EstiloPlotagem);
            ps.PlotPlotStyles = true;
            info = FormattableString.Invariant(
                $"papel {canon} = \"{f.Formato.Nome}\" {ps.PlotPaperSize.X:0.#}x{ps.PlotPaperSize.Y:0.#} mm, giro {ps.PlotRotation}, área {ps.PlotType}, estilo {ps.CurrentStyleSheet}");

            var pi = new Plt.PlotInfo { Layout = layId, OverrideSettings = ps };
            var piv = new Plt.PlotInfoValidator { MediaMatchingPolicy = Plt.MatchingPolicy.MatchEnabled };
            piv.Validate(pi);

            if (Plt.PlotFactory.ProcessPlotState != Plt.ProcessPlotState.NotPlotting)
                throw new InvalidOperationException("o CAD já está plotando outro trabalho");
            using (var pe = Plt.PlotFactory.CreatePublishEngine())
            {
                pe.BeginPlot(null, null);
                pe.BeginDocument(pi, nomeTrabalho, null, 1, false, "");
                var ppi = new Plt.PlotPageInfo();
                pe.BeginPage(ppi, pi, true, null);
                pe.BeginGenerateGraphics(null);
                pe.EndGenerateGraphics(null);
                pe.EndPage(null);
                pe.EndDocument(null);
                pe.EndPlot(null);
            }

            if (gravarNoLayout)
            {
                lay.UpgradeOpen();
                lay.CopyFrom(ps);
            }
            tr.Commit();
        }
        return info;
    }
}
