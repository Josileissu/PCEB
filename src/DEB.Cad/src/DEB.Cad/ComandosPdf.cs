using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DEB.Cad.Infra;
using DEB.Cad.Paleta;
using DEB.Cad.Pdf;
using DEB.Core.Documentacao;

[assembly: Rt.CommandClass(typeof(DEB.Cad.ComandosPdf))]

namespace DEB.Cad;

/// <summary>
/// ARMPDFLOTE (pedido do usuário, 2026-11-17): exporta um PDF por folha (layout) de vários desenhos de uma vez, na impressora
/// PDF24, com o formato da folha reconhecido pela moldura/carimbo, sem depender do page setup e sem o diálogo "salvar como" da
/// PDF24. Detalhes em docs/CONVENCOES.md ("PDFs em lote").
/// </summary>
public sealed class ComandosPdf
{
    internal static string CaminhoConfig => Path.Combine(Log.PastaDados, "pdflote.json");

    /// <summary>No AutoCAD o lote usa por padrão a "AutoCAD PDF (High Quality Print).pc3" (plotar para arquivo); nos outros, a PDF24.</summary>
#if AUTOCAD
    internal static readonly bool NoAutoCad = true;
#else
    internal static readonly bool NoAutoCad = false;
#endif

    // Session: o lote abre (só leitura) e fecha os DWGs que não estão abertos e troca o desenho ativo
    [Rt.CommandMethod("ARMPDFLOTE", Rt.CommandFlags.Session)]
    public void PdfLote()
    {
        var dm = CadApp.DocumentManager;
        if (Demo.Expirada) { dm.MdiActiveDocument?.Editor.WriteMessage("\n" + Demo.Mensagem + "\n"); return; }
        try
        {
            var cfg = ConfigLotePdf.Carregar(CaminhoConfig);
            cfg.NoAutoCad = NoAutoCad;
            System.Collections.Generic.List<FolhaPdf> folhas;
            using (var f = new FormPdfLote(cfg))
            {
                if (dm.Count > 0) f.Shown += (s, e) => f.AdicionarAbertos(); // começa com os desenhos abertos
                var r = CadApp.ShowModalDialog(f);
                SalvarConfig(cfg);
                if (r != DialogResult.OK) return;
                folhas = f.Selecionadas;
            }
            var (res, cancelado) = Executar(folhas, cfg, true);
            var ed = dm.MdiActiveDocument?.Editor;
            ed?.WriteMessage($"\nARMPDFLOTE: {res.Count(x => x.Pdf != null)} PDF(s) gerado(s), {res.Count(x => x.Pdf == null)} com erro/pulado(s)" +
                             (cancelado ? " (cancelado)" : "") + ".\n");
            using (var rel = new FormRelatorioPdf(res, cancelado)) CadApp.ShowModalDialog(rel);
            if (cfg.AbrirPastaNoFim)
                foreach (var p in res.Where(x => x.Pdf != null).Select(x => Path.GetDirectoryName(x.Pdf)).Distinct(StringComparer.OrdinalIgnoreCase).Take(3))
                    Arquivos.Abrir(p);
        }
        catch (Exception ex)
        {
            // barreira do comando: nada derruba o CAD; mensagem clara + detalhe no log
            var d = ErrosLotePdf.Classificar(ex, EtapaLotePdf.Configurar);
            Log.ErroDetalhado("ARMPDFLOTE", d.Detalhado(null, null, DateTime.Now));
            try
            {
                dm.MdiActiveDocument?.Editor.WriteMessage($"\nARMPDFLOTE: {d.Resumo} (detalhes em {Log.ArquivoHoje})\n");
                MessageBox.Show(d.Resumo + "\n\nDetalhes técnicos no log:\n" + Log.ArquivoHoje, "DEB - PDFs em lote", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch { }
        }
    }

    internal static void SalvarConfig(ConfigLotePdf cfg)
    {
        try { cfg.Salvar(CaminhoConfig); } catch (Exception ex) { Log.Erro("ARMPDFLOTE: salvar pdflote.json", ex); }
    }

    /// <summary>Roda o lote com a janela de progresso (ou sem ela, no autoteste).</summary>
    internal static (System.Collections.Generic.List<ResultadoFolha> Res, bool Cancelado) Executar(
        System.Collections.Generic.IList<FolhaPdf> folhas, ConfigLotePdf cfg, bool perguntar, Func<bool> cancelarTambem = null)
    {
        using var prog = new FormProgressoPdf();
        prog.Show();
        try
        {
            var ex = new ExecutorLotePdf
            {
                Cancelado = () => prog.Cancelado || (cancelarTambem?.Invoke() ?? false),
                Progresso = prog.Atualizar,
                PerguntarSobrescrever = lista =>
                {
                    if (!perguntar) return true;
                    var txt = $"{lista.Count} PDF(s) já existe(m):\n" + string.Join("\n", lista.Take(12).Select(Path.GetFileName)) +
                              (lista.Count > 12 ? "\n…" : "") +
                              "\n\nSim = sobrescrever todos\nNão = pular os que já existem\nCancelar = cancelar o lote";
                    var r = MessageBox.Show(prog, txt, "DEB - PDFs em lote", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    return r == DialogResult.Yes ? true : r == DialogResult.No ? false : (bool?)null;
                },
            };
            var res = ex.Executar(folhas, cfg);
            return (res, ex.FoiCancelado);
        }
        finally { prog.Close(); }
    }
}
