using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DEB.Cad.Infra;
using DEB.Core.Documentacao;

namespace DEB.Cad.Pdf;

/// <summary>Resultado de uma folha do lote.</summary>
internal sealed class ResultadoFolha
{
    public FolhaPdf Folha { get; set; }
    /// <summary>PDF gerado (null = não gerou).</summary>
    public string Pdf { get; set; }
    public string Erro { get; set; }
    public bool Pulada { get; set; }
    public string Info { get; set; }
}

/// <summary>
/// Executa o lote do ARMPDFLOTE: para cada desenho (aberto ou aberto só-leitura e fechado sem salvar no fim), plota cada folha
/// marcada na PDF24 com salvamento automático numa pasta temporária (ligado só durante cada folha), espera o PDF e o move para o destino com o nome certo.
/// Roda no contexto da aplicação (comando com CommandFlags.Session).
/// </summary>
internal sealed class ExecutorLotePdf
{
    public Func<bool> Cancelado { get; set; } = () => false;
    /// <summary>(feitas, total, texto).</summary>
    public Action<int, int, string> Progresso { get; set; } = (a, b, c) => { };
    /// <summary>Lista dos que já existem → true = sobrescrever todos, false = pular os existentes, null = cancelar o lote.</summary>
    public Func<IList<string>, bool?> PerguntarSobrescrever { get; set; } = _ => true;
    /// <summary>Mantém a janela de progresso viva durante as esperas.</summary>
    public Action Bombear { get; set; } = () => System.Windows.Forms.Application.DoEvents();

    public bool FoiCancelado { get; private set; }

    /// <summary>Caminho do PDF de cada folha (pasta do DWG ou a escolhida; sufixo do layout só com 2+ layouts do mesmo DWG).</summary>
    public static Dictionary<FolhaPdf, string> Destinos(IList<FolhaPdf> folhas, ConfigLotePdf cfg)
    {
        var res = new Dictionary<FolhaPdf, string>();
        var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in folhas)
        {
            bool varios = folhas.Count(o => string.Equals(o.Arquivo, f.Arquivo, StringComparison.OrdinalIgnoreCase)) > 1;
            string pasta = cfg.NaPastaDoDesenho ? Path.GetDirectoryName(f.Arquivo) : cfg.PastaDestino;
            if (string.IsNullOrEmpty(pasta)) pasta = !string.IsNullOrEmpty(cfg.PastaDestino) ? cfg.PastaDestino
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var nome = FormatosFolha.NomePdf(f.NomeDesenho, f.Layout, varios);
            var caminho = Path.Combine(pasta, nome);
            for (int i = 2; usados.Contains(caminho); i++)
                caminho = Path.Combine(pasta, Path.GetFileNameWithoutExtension(nome) + $" ({i}).pdf");
            usados.Add(caminho);
            res[f] = caminho;
        }
        return res;
    }

    public List<ResultadoFolha> Executar(IList<FolhaPdf> folhas, ConfigLotePdf cfg)
    {
        var resultados = new List<ResultadoFolha>();
        var tipo = ImpressorasPdf.Tipo(cfg.ImpressoraLote);
        if (tipo == TipoImpressoraPdf.Outra)
            throw new InvalidOperationException($"o lote salva sem diálogo só com a PDF24 ou uma .pc3 de PDF (escolhida: {cfg.ImpressoraLote})");
        var destinos = Destinos(folhas, cfg);
        var existentes = destinos.Values.Where(File.Exists).ToList();
        bool sobrescrever = true;
        if (existentes.Count > 0 && !cfg.SobrescreverTudo)
        {
            var r = PerguntarSobrescrever(existentes);
            if (r == null) { FoiCancelado = true; return resultados; }
            sobrescrever = r.Value;
        }

        var dm = CadApp.DocumentManager;
        var docInicial = dm.MdiActiveDocument;
        object bgplot = null;
        try { bgplot = CadApp.GetSystemVariable("BACKGROUNDPLOT"); CadApp.SetSystemVariable("BACKGROUNDPLOT", (short)0); }
        catch (Exception ex) { Log.Erro("ARMPDFLOTE: BACKGROUNDPLOT", ex); }
        var temp = Path.Combine(Path.GetTempPath(), "DEB_PDF_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        int total = folhas.Count, feitas = 0;
        try
        {
            foreach (var grupo in folhas.GroupBy(f => f.Arquivo, StringComparer.OrdinalIgnoreCase))
            {
                if (Cancelado()) { FoiCancelado = true; break; }
                Document doc = grupo.First().Doc ?? AbertoNesteCad(grupo.Key);
                bool abriu = false;
                string layoutOriginal = null;
                try
                {
                    if (doc == null)
                    {
                        Progresso(feitas, total, "Abrindo " + Path.GetFileName(grupo.Key) + "…");
                        doc = dm.Open(grupo.Key, true);
                        abriu = true;
                    }
                    if (dm.MdiActiveDocument != doc) dm.MdiActiveDocument = doc;
                    using (doc.LockDocument())
                    {
                        layoutOriginal = LayoutManager.Current.CurrentLayout;
                        foreach (var f in grupo)
                        {
                            if (Cancelado()) { FoiCancelado = true; break; }
                            var res = new ResultadoFolha { Folha = f };
                            resultados.Add(res);
                            var destino = destinos[f];
                            Progresso(feitas, total, $"{f.NomeDesenho} — {f.Layout} ({f.Formato?.Nome ?? "?"})");
                            try
                            {
                                if (File.Exists(destino) && !sobrescrever) { res.Pulada = true; res.Erro = "já existe (pulado)"; continue; }
                                if (f.Formato == null) throw new InvalidOperationException("formato da folha não escolhido");
                                LimparPasta(temp);
                                var trabalho = Path.GetFileNameWithoutExtension(destino);
                                string gerado;
                                if (tipo == TipoImpressoraPdf.Pc3)
                                {
                                    // .pc3 (AutoCAD PDF): plotar para arquivo, sem diálogo; a PDF24 não é tocada
                                    res.Info = PlotadorPdf.Plotar(doc, f, cfg, trabalho, cfg.GravarNoLayout && f.Doc != null, Path.Combine(temp, trabalho + ".pdf"));
                                    gerado = EsperarPdf(temp, cfg.EsperaSegundos)
                                             ?? throw new TimeoutException($"a {cfg.ImpressoraLote} não gravou o PDF em {cfg.EsperaSegundos} s");
                                }
                                else
                                {
                                    // salvamento automático da PDF24 só durante esta folha (plotagem + espera do arquivo)
                                    using (Pdf24.Ativar(temp))
                                    {
                                        res.Info = PlotadorPdf.Plotar(doc, f, cfg, trabalho, cfg.GravarNoLayout && f.Doc != null);
                                        gerado = EsperarPdf(temp, cfg.EsperaSegundos)
                                                 ?? throw new TimeoutException($"a PDF24 não entregou o PDF em {cfg.EsperaSegundos} s");
                                    }
                                }
                                // a PDF24 grava tudo em pé: folha deitada → página marcada para abrir deitada (AjustePdf; só mexe em página em pé)
                                // (folha deitada: o CAD girou 90° anti-horário → exibir a 90°; A4 em pé com desenho deitado: horário → 270°)
                                if (f.Deitada && AjustePdf.Deitar(gerado, f.Formato.Paisagem ? 90 : 270) > 0) res.Info += ", página girada para abrir deitada";
                                Directory.CreateDirectory(Path.GetDirectoryName(destino));
                                File.Copy(gerado, destino, true);
                                File.Delete(gerado);
                                res.Pdf = destino;
                                Log.Info($"ARMPDFLOTE: {destino} ({res.Info})");
                            }
                            catch (Exception ex)
                            {
                                res.Erro = ex.Message;
                                Log.Erro($"ARMPDFLOTE: {f.Arquivo} / {f.Layout}", ex);
                            }
                            finally { feitas++; Progresso(feitas, total, ""); }
                        }
                        if (!abriu && layoutOriginal != null && LayoutManager.Current.CurrentLayout != layoutOriginal)
                        {
                            try { LayoutManager.Current.CurrentLayout = layoutOriginal; } catch (Exception ex) { Log.Erro("ARMPDFLOTE: voltar o layout", ex); }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Erro("ARMPDFLOTE: " + grupo.Key, ex);
                    foreach (var f in grupo.Where(f => resultados.All(r => r.Folha != f)))
                    {
                        resultados.Add(new ResultadoFolha { Folha = f, Erro = "não abriu o desenho: " + ex.Message });
                        feitas++;
                    }
                }
                finally
                {
                    if (abriu && doc != null)
                    {
                        try { doc.CloseAndDiscard(); } catch (Exception ex) { Log.Erro("ARMPDFLOTE: fechar " + grupo.Key, ex); }
                    }
                }
            }
        }
        finally
        {
            try { if (docInicial != null && dm.MdiActiveDocument != docInicial) dm.MdiActiveDocument = docInicial; } catch { }
            try { if (bgplot != null) CadApp.SetSystemVariable("BACKGROUNDPLOT", bgplot); } catch { }
            try { Directory.Delete(temp, true); } catch { }
        }
        return resultados;
    }

    /// <summary>Documento aberto neste CAD com esse caminho (null = fechado).</summary>
    internal static Document AbertoNesteCad(string caminho)
    {
        foreach (Document d in CadApp.DocumentManager)
        {
            try { if (string.Equals(Path.GetFullPath(d.Name), caminho, StringComparison.OrdinalIgnoreCase)) return d; } catch { }
        }
        return null;
    }

    private static void LimparPasta(string pasta)
    {
        Directory.CreateDirectory(pasta);
        foreach (var a in Directory.GetFiles(pasta)) { try { File.Delete(a); } catch { } }
    }

    /// <summary>Espera o PDF aparecer na pasta temporária com tamanho estável e livre para abrir.</summary>
    private string EsperarPdf(string pasta, int segundos)
    {
        var t0 = DateTime.Now;
        string atual = null; long tam = -1;
        while ((DateTime.Now - t0).TotalSeconds < segundos)
        {
            var arq = Directory.GetFiles(pasta, "*.pdf").FirstOrDefault();
            if (arq != null)
            {
                long t;
                try { t = new FileInfo(arq).Length; } catch { t = -1; }
                if (arq == atual && t > 0 && t == tam && Livre(arq)) return arq;
                atual = arq; tam = t;
            }
            for (int i = 0; i < 5; i++) { Bombear(); Thread.Sleep(60); }
        }
        return null;
    }

    private static bool Livre(string arq)
    {
        try { using (File.Open(arq, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) return true; }
        catch { return false; }
    }
}
