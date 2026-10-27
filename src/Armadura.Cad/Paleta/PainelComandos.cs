using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Armadura.Cad.Infra;
using Color = System.Drawing.Color;

namespace Armadura.Cad.Paleta;

/// <summary>
/// Paleta "Armadura - Comandos" (ARMCOMANDOS), separada da paleta de propriedades: botões agrupados
/// (o GstarCAD 2021 não expõe ribbon em .NET). Posição e encaixe ficam gravados pelo CAD (GUID fixo).
/// Layout manual (sem AutoSize/FlowLayoutPanel): o host da paleta re-encaixa e oculta o controle, e os
/// painéis de tamanho automático aninhados podiam colapsar para tamanho zero.
/// </summary>
internal static class PainelComandos
{
    private static readonly (string grupo, (string rotulo, string cmd)[] botoes)[] Grupos =
    {
        ("Barras", new[] { ("Barra", "ARMBARRA"), ("Converter", "ARMCONVERTER"), ("Offset", "ARMOFFSET"), ("Propriedades", "ARMPROP"),
                           ("Segmento", "ARMEDITSEG"), ("Recotar", "ARMRECOTA"), ("Rebater", "ARMREBATER"), ("Dividir", "ARMDIVIDIR") }),
        ("Estribos / treliças / telas", new[] { ("Estribo", "ARMESTRIBO"), ("Repetir estribo", "ARMESTRIBOREP"), ("Treliça", "ARMTRELICA"), ("Tela", "ARMTELA") }),
        ("Faixas", new[] { ("Faixa ort.", "ARMFAIXAORT"), ("Faixa ali.", "ARMFAIXAALI"), ("Corte+cota ort.", "ARMCORTECOTAORT"),
                           ("Corte+cota ali.", "ARMCORTECOTAALI"), ("Corte+cham.", "ARMCORTECHAMADA"), ("Corte em arco", "ARMCORTECHAMADAARCO"),
                           ("Chamada", "ARMCHAMADA"), ("Indicação", "ARMINDICACAO") }),
        ("Relação do aço", new[] { ("Inserir", "ARMRAINSERIR"), ("Atualizar", "ARMRAATUALIZAR"), ("Parcial", "ARMRAPARCIAL"),
                                   ("Associar", "ARMRAASSOCIAR"), ("Bloquear", "ARMRABLOQUEAR"), ("Corte e dobra", "ARMCORTEDOBRA") }),
        ("Detalhamentos", new[] { ("Criar", "ARMDETCRIAR"), ("Gerenciar", "ARMDETGERENCIAR"), ("Templates", "ARMTEMPLATE") }),
        ("Geral", new[] { ("Importar .cad", "ARMIMPORTARCAD"), ("Paleta prop.", "ARMPALETA"), ("Configuração", "ARMCONFIG"), ("Sobre", "ARMSOBRE") }),
    };

    private static GrxCAD.Windows.PaletteSet _ps;
    private static Painel _painel;

    public static bool Visivel => _ps != null && _ps.Visible;

    public static void Mostrar()
    {
        if (_ps == null) Criar();
        _ps.Visible = true;
        _painel.Reorganizar();
    }

    public static void Alternar()
    {
        if (Visivel) _ps.Visible = false;
        else Mostrar();
    }

    private static void Criar()
    {
        _ps = new GrxCAD.Windows.PaletteSet("Armadura - Comandos", new System.Guid("3B9E5C21-7D4F-4A86-B1C2-8E0F6A9D4C37"))
        {
            Style = GrxCAD.Windows.PaletteSetStyles.ShowCloseButton | GrxCAD.Windows.PaletteSetStyles.ShowAutoHideButton,
            MinimumSize = new Size(120, 150),
            Size = new Size(310, 600),
        };
        _painel = new Painel();
        _ps.Add("Comandos", _painel);
        // re-encaixe/redimensionamento pelo CAD: refaz a disposição dos botões
        _ps.StateChanged += (s, e) => _painel.Reorganizar();
        _ps.SizeChanged += (s, e) => _painel.Reorganizar();
        _ps.PaletteActivated += (s, e) => _painel.Reorganizar();
    }

    /// <summary>Executa o comando do botão; cancela antes o comando em andamento (senão o texto viraria resposta ao prompt).</summary>
    private static void Executar(string cmd)
    {
        var doc = CadApp.DocumentManager.MdiActiveDocument;
        if (doc == null) { Log.Info($"Paleta de comandos: {cmd} sem documento ativo."); return; }
        try
        {
            var prefixo = string.IsNullOrEmpty(doc.CommandInProgress) ? "" : "\x1B\x1B";
            Log.Info($"Paleta de comandos: {cmd} (em andamento: '{doc.CommandInProgress}')");
            doc.SendStringToExecute(prefixo + "_" + cmd + " ", true, false, true);
            try { GrxCAD.Internal.Utils.SetFocusToDwgView(); } catch { }
        }
        catch (System.Exception ex) { Log.Erro("Paleta de comandos: " + cmd, ex); }
    }

    /// <summary>Botão que aceita o clique mesmo com a paleta inativa (o host "comia" o 1º clique).</summary>
    private sealed class BotaoPaleta : Button
    {
        private const int WM_MOUSEACTIVATE = 0x21, MA_ACTIVATE = 1, MA_ACTIVATEANDEAT = 2, MA_NOACTIVATEANDEAT = 4;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_MOUSEACTIVATE && ((int)m.Result == MA_ACTIVATEANDEAT || (int)m.Result == MA_NOACTIVATEANDEAT))
                m.Result = (System.IntPtr)MA_ACTIVATE;
        }
    }

    /// <summary>Painel com rolagem: títulos e botões posicionados à mão conforme a largura disponível.</summary>
    private sealed class Painel : Panel
    {
        private const int Margem = 6, Folga = 4, AlturaBotao = 26, LarguraMinBotao = 84, AlturaTitulo = 18;
        private readonly List<(Label titulo, List<Button> botoes)> _grupos = new();

        public Painel()
        {
            Dock = DockStyle.Fill;
            AutoScroll = true;
            bool escuro = false;
            try { escuro = System.Convert.ToInt32(CadApp.GetSystemVariable("COLORTHEME")) == 0; } catch { }
            var fundo = Color.FromArgb(59, 68, 83);
            var botaoFundo = Color.FromArgb(43, 49, 59);
            var texto = Color.FromArgb(230, 230, 230);
            if (escuro) BackColor = fundo;

            foreach (var (grupo, botoes) in Grupos)
            {
                var titulo = new Label { Text = grupo, AutoSize = false, Height = AlturaTitulo, Font = new Font(Font, FontStyle.Bold) };
                if (escuro) titulo.ForeColor = texto;
                Controls.Add(titulo);
                var lista = new List<Button>();
                foreach (var (rotulo, cmd) in botoes)
                {
                    var b = new BotaoPaleta { Text = rotulo, Height = AlturaBotao, Tag = cmd, UseVisualStyleBackColor = !escuro };
                    if (escuro)
                    {
                        b.FlatStyle = FlatStyle.Flat;
                        b.BackColor = botaoFundo;
                        b.ForeColor = texto;
                        b.FlatAppearance.BorderColor = Color.FromArgb(90, 100, 115);
                    }
                    b.Click += (s, e) => Executar((string)((Button)s).Tag);
                    Controls.Add(b);
                    lista.Add(b);
                }
                _grupos.Add((titulo, lista));
            }
        }

        protected override void OnResize(System.EventArgs e) { base.OnResize(e); Reorganizar(); }
        protected override void OnVisibleChanged(System.EventArgs e) { base.OnVisibleChanged(e); Reorganizar(); }
        protected override void OnParentChanged(System.EventArgs e) { base.OnParentChanged(e); Reorganizar(); }
        protected override void OnHandleCreated(System.EventArgs e) { base.OnHandleCreated(e); Reorganizar(); }

        public void Reorganizar()
        {
            if (IsDisposed) return;
            int largura = ClientSize.Width - 2 * Margem - SystemInformation.VerticalScrollBarWidth;
            if (largura < LarguraMinBotao) largura = LarguraMinBotao;
            int colunas = System.Math.Max(1, System.Math.Min(3, (largura + Folga) / (LarguraMinBotao + Folga)));
            int lb = (largura - (colunas - 1) * Folga) / colunas;
            int y = Margem + AutoScrollPosition.Y, x0 = Margem + AutoScrollPosition.X;

            SuspendLayout();
            foreach (var (titulo, botoes) in _grupos)
            {
                titulo.SetBounds(x0, y, largura, AlturaTitulo);
                titulo.Visible = true;
                y += AlturaTitulo;
                for (int i = 0; i < botoes.Count; i++)
                {
                    botoes[i].SetBounds(x0 + i % colunas * (lb + Folga), y + i / colunas * (AlturaBotao + Folga), lb, AlturaBotao);
                    botoes[i].Visible = true;
                }
                y += (botoes.Count + colunas - 1) / colunas * (AlturaBotao + Folga) + Margem;
            }
            AutoScrollMinSize = new Size(0, y - AutoScrollPosition.Y);
            ResumeLayout(false);
            Invalidate(true);
        }
    }
}
