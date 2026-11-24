using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using DEB.Core.Configuracao.Projeto;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DEB.Revit.Configuracao
{
    /// <summary>
    /// Janela da configuração do projeto (D43): a página do núcleo (<see cref="SessaoConfiguracao.Pagina"/>) num WebView2 (o do
    /// próprio Revit 2025/2026). A janela só repassa as mensagens à sessão e devolve o estado; nenhuma regra aqui (D26).
    /// <see cref="Gravar"/> = true quando a página pede "Gravar" (ou quando, ao fechar com alterações, o usuário escolhe gravar).
    /// </summary>
    internal sealed class JanelaConfiguracao : Window
    {
        private readonly SessaoConfiguracao _sessao;
        private readonly WebView2 _web = new WebView2();
        private bool _fechando;

        /// <summary>O usuário pediu para gravar.</summary>
        public bool Gravar { get; private set; }
        /// <summary>Só no AutoRun: grava a imagem da página neste PNG e fecha.</summary>
        public string Captura { get; set; }
        /// <summary>Erro ao iniciar o WebView2 (a janela fecha sozinha e o comando mostra a mensagem).</summary>
        public string Erro { get; private set; }

        public JanelaConfiguracao(SessaoConfiguracao sessao)
        {
            _sessao = sessao;
            Title = "PCEB — Configuração do projeto";
            Width = 1180; Height = 760; MinWidth = 720; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = _web;
            Loaded += async (s, e) => await Iniciar();
            Closing += (s, e) =>
            {
                if (_fechando || Captura != null || !_sessao.Alterado) return;
                var r = MessageBox.Show(this, "Há alterações não gravadas. Gravar antes de fechar?", Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) e.Cancel = true;
                else { _fechando = true; Gravar = r == MessageBoxResult.Yes; }
            };
        }

        private async Task Iniciar()
        {
            try
            {
                // pasta própria de dados do navegador (a do Revit é dele; a da DLL pode não ter escrita)
                string dados = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCEB", "WebView2");
                var env = await CoreWebView2Environment.CreateAsync(null, dados);
                await _web.EnsureCoreWebView2Async(env);
                CoreWebView2 w = _web.CoreWebView2;
                w.Settings.AreDevToolsEnabled = false;
                w.Settings.IsStatusBarEnabled = false;
                w.Settings.AreDefaultContextMenusEnabled = false;
                w.WebMessageReceived += Mensagem;
                if (Captura != null) w.NavigationCompleted += async (s, e) => await Capturar();
                w.NavigateToString(SessaoConfiguracao.Pagina());
            }
            catch (Exception ex)
            {
                Erro = "Não foi possível abrir a tela (WebView2): " + ex.Message;
                _fechando = true;
                Close();
            }
        }

        private void Mensagem(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string resposta = _sessao.Receber(e.TryGetWebMessageAsString());
            _web.CoreWebView2.PostWebMessageAsString(resposta);
            if (_sessao.Fim == null || Captura != null) return;
            _fechando = true;
            Gravar = _sessao.Fim == "salvar";
            Close();
        }

        private async Task Capturar()
        {
            await Task.Delay(800);   // a página pede o estado ao carregar e desenha
            using (var fs = File.Create(Captura))
                await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
            _fechando = true;
            Close();
        }
    }
}
