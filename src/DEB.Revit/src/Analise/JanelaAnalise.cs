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
                    if (m.Contains("\"pronto\"")) w.PostWebMessageAsString(_json);
                    else if (m.Contains("\"fechar\"")) Close();
                };
                string pagina = VisualizacaoAnalise.Pagina();
                if (Captura != null)
                {
                    // abre direto no modo pedido (a página lê o # da URL só ao navegar por arquivo; aqui injeta o clique)
                    w.NavigationCompleted += async (s, e) =>
                    {
                        await Task.Delay(900);
                        if (ModoCaptura != "") await w.ExecuteScriptAsync($"(document.querySelector('[data-modo=\"{ModoCaptura}\"]') || document.querySelector('[data-aba=\"{ModoCaptura}\"]')).click()");
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
