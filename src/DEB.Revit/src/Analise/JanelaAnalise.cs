using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using DEB.Core.Analise.Objetos;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DEB.Revit.Analise
{
    /// <summary>
    /// Janela do modelo de análise (D46): a página do núcleo (<see cref="VisualizacaoAnalise.Pagina"/>) num WebView2 (o do Revit),
    /// com o JSON da estrutura analisada. O modelo é do PCEB e não escreve no Revit; a janela só entrega os dados à página.
    /// </summary>
    internal sealed class JanelaAnalise : Window
    {
        private readonly string _json;
        private readonly WebView2 _web = new WebView2();

        /// <summary>Só no AutoRun: grava a imagem da página neste PNG (com o modo do sufixo, ex. "#diagrama") e fecha.</summary>
        public string Captura { get; set; }
        public string ModoCaptura { get; set; } = "";
        public string Erro { get; private set; }
        /// <summary>Sessão com ajustes (D50): as mensagens "ajustar"/"restaurar" vão para ela e a página recebe o JSON novo.</summary>
        public SessaoAnalise Sessao { get; set; }

        public JanelaAnalise(string json, string titulo)
        {
            _json = json;
            Title = "PCEB — Modelo de análise" + (string.IsNullOrEmpty(titulo) ? "" : " · " + titulo);
            Width = 1360; Height = 820; MinWidth = 760; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = _web;
            Loaded += async (s, e) => await Iniciar();
        }

        private async Task Iniciar()
        {
            try
            {
                string dados = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCEB", "WebView2");
                var env = await CoreWebView2Environment.CreateAsync(null, dados);
                await _web.EnsureCoreWebView2Async(env);
                CoreWebView2 w = _web.CoreWebView2;
                w.Settings.AreDevToolsEnabled = false;
                w.Settings.IsStatusBarEnabled = false;
                w.Settings.AreDefaultContextMenusEnabled = false;
                w.WebMessageReceived += (s, e) =>
                {
                    string m = e.TryGetWebMessageAsString() ?? "";
                    if (m.Contains("\"pronto\"")) w.PostWebMessageAsString(Sessao != null ? Sessao.Json() : _json);
                    else if (m.Contains("\"fechar\"")) Close();
                    else if (Sessao != null)
                    {
                        Cursor = System.Windows.Input.Cursors.Wait;
                        try { string r = Sessao.Receber(m); if (r.Length > 0) w.PostWebMessageAsString(r); }
                        finally { Cursor = null; }
                    }
                };
                string pagina = VisualizacaoAnalise.Pagina();
                if (Captura != null)
                {
                    // abre direto no modo pedido (a página lê o # da URL só ao navegar por arquivo; aqui injeta o clique)
                    w.NavigationCompleted += async (s, e) =>
                    {
                        await Task.Delay(900);
                        if (ModoCaptura == "consolo" || ModoCaptura == "campo")
                        {
                            // detalhe do consolo (D50, item 3): seleciona o primeiro consolo e abre o 2D da peça; o detalhe vem da sessão
                            await w.ExecuteScriptAsync("(() => { const c = D.molas.map(m => m.dono)[0]; if (c) { sel = c; lateral(); } document.querySelector('[data-aba=\"peca\"]').click(); })()");
                            await Task.Delay(2500);
                            if (ModoCaptura == "campo") { await w.ExecuteScriptAsync("campoConsolo = 'C'; desenhar();"); await Task.Delay(20000); }
                        }
                        else if (ModoCaptura != "") await w.ExecuteScriptAsync($"(document.querySelector('[data-modo=\"{ModoCaptura}\"]') || document.querySelector('[data-aba=\"{ModoCaptura}\"]')).click()");
                        await Task.Delay(400);
                        using (var fs = File.Create(Captura)) await w.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
                        Close();
                    };
                }
                w.NavigateToString(pagina);
            }
            catch (Exception ex)
            {
                Erro = "Não foi possível abrir a janela (WebView2): " + ex.Message;
                Close();
            }
        }
    }
}
