using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using DEB.Core.Elementos.Metalica.Ligacoes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DEB.Revit.Analise
{
    /// <summary>Janela da biblioteca de ligações (D49): a página do núcleo num WebView2; as mensagens vão à <see cref="SessaoBiblioteca"/>.</summary>
    internal sealed class JanelaBibliotecaLigacoes : Window
    {
        private readonly SessaoBiblioteca _sessao;
        private readonly WebView2 _web = new WebView2();
        private bool _fechando;

        public bool Gravar { get; private set; }
        public string Captura { get; set; }
        public string Erro { get; private set; }

        public JanelaBibliotecaLigacoes(SessaoBiblioteca sessao)
        {
            _sessao = sessao;
            Title = "PCEB — Biblioteca de ligações";
            Width = 1360; Height = 860; MinWidth = 760; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = _web;
            Loaded += async (s, e) => await Iniciar();
            Closing += (s, e) =>
            {
                if (_fechando || Captura != null || !_sessao.Alterado) return;
                var r = MessageBox.Show(this, "Há alterações não gravadas na biblioteca. Gravar no escritório antes de fechar?", Title,
                                        MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) e.Cancel = true;
                else { _fechando = true; Gravar = r == MessageBoxResult.Yes; }
            };
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
                    string resposta = _sessao.Receber(e.TryGetWebMessageAsString());
                    w.PostWebMessageAsString(resposta);
                    if (_sessao.Fim == null || Captura != null) return;
                    _fechando = true;
                    Gravar = _sessao.Fim == "salvar";
                    Close();
                };
                if (Captura != null)
                    w.NavigationCompleted += async (s, e) =>
                    {
                        await Task.Delay(900);
                        using (var fs = File.Create(Captura)) await w.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
                        _fechando = true;
                        Close();
                    };
                w.NavigateToString(SessaoBiblioteca.Pagina());
            }
            catch (Exception ex)
            {
                Erro = "Não foi possível abrir a janela (WebView2): " + ex.Message;
                _fechando = true;
                Close();
            }
        }
    }
}
