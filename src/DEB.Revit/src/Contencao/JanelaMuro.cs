using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using DEB.Core.Elementos.Contencao;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DEB.Revit.Contencao
{
    /// <summary>Janela do muro de arrimo (D47): a página do núcleo num WebView2; as mensagens vão à <see cref="SessaoMuro"/>.</summary>
    internal sealed class JanelaMuro : Window
    {
        private readonly SessaoMuro _sessao;
        private readonly WebView2 _web = new WebView2();

        /// <summary>Só no AutoRun: grava a imagem da página neste PNG e fecha (com <see cref="ModoCaptura"/> = aba).</summary>
        public string Captura { get; set; }
        public string ModoCaptura { get; set; } = "";
        public string Erro { get; private set; }

        public JanelaMuro(SessaoMuro sessao)
        {
            _sessao = sessao;
            Title = "PCEB — Muro de arrimo" + (string.IsNullOrEmpty(sessao.Titulo) ? "" : " · " + sessao.Titulo);
            Width = 1400; Height = 840; MinWidth = 760; MinHeight = 480;
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
                    if (m.Contains("\"fechar\"")) { Close(); return; }
                    string resposta = _sessao.Processar(m);
                    if (resposta.Length > 0) w.PostWebMessageAsString(resposta);
                    if (_sessao.Aplicar && Captura == null) Close();
                };
                if (Captura != null)
                    w.NavigationCompleted += async (s, e) =>
                    {
                        await Task.Delay(900);
                        if (ModoCaptura != "") await w.ExecuteScriptAsync($"document.querySelector('.seg button[data-aba=\"{ModoCaptura}\"]').click()");
                        await Task.Delay(400);
                        using (var fs = File.Create(Captura)) await w.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
                        Close();
                    };
                w.NavigateToString(SessaoMuro.Pagina());
            }
            catch (Exception ex)
            {
                Erro = "Não foi possível abrir a janela (WebView2): " + ex.Message;
                Close();
            }
        }
    }
}
