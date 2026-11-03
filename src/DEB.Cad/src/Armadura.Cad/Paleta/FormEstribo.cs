using System;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Armadura.Core.Configuracao;
using Armadura.Core.Estribos;
using Armadura.Core.Modelo;
using FlowDirection = System.Windows.Forms.FlowDirection;

namespace Armadura.Cad.Paleta;

/// <summary>Diálogo do ARMESTRIBO (seção 3.1): formato, ramos, gancho, bitola, dimensões, seção e cobrimento.</summary>
internal sealed class FormEstribo : Form
{
    private readonly ComboBox _formato = Combo("Simples", "Aberto", "Múltiplo", "Pente", "Circular externo", "Circular interno");
    private readonly NumericUpDown _ramos = new NumericUpDown { Minimum = 2, Maximum = 20, Width = 60 };
    private readonly ComboBox _gancho = Combo("45° (135°)", "90°", "180°");
    private readonly ComboBox _bitola = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 80 };
    private readonly ComboBox _aco = Combo("CA-25", "CA-50", "CA-60");
    private readonly RadioButton _naInsercao = new RadioButton { Text = "Definir na inserção (pontos)", AutoSize = true };
    private readonly RadioButton _fixo = new RadioButton { Text = "Usar fixo", AutoSize = true };
    private readonly TextBox _base = Numero(), _altura = Numero(), _diametro = Numero();
    private readonly CheckBox _secao = new CheckBox { Text = "Inserir usando seção (pontos/medidas da forma)", AutoSize = true };
    private readonly TextBox _cobrimento = Numero(), _espacamento = Numero();
    private readonly Label _lBase = new Label { Text = "Base (cm)", AutoSize = true };
    private readonly Label _lAltura = new Label { Text = "Altura (cm)", AutoSize = true };
    private readonly Label _lDiametro = new Label { Text = "Diâmetro (cm)", AutoSize = true };

    public OpcoesEstribo Opcoes { get; }

    public FormEstribo(OpcoesEstribo o, ConfiguracaoArmadura cfg)
    {
        Opcoes = o;
        Text = "Estribo";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(10);

        _bitola.Items.AddRange(cfg.Barras.Ativas.Select(b => (object)b.Diametro.ToString("0.0", CultureInfo.InvariantCulture)).Distinct().ToArray());

        var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        void Linha(Control a, Control b) { t.Controls.Add(a); t.Controls.Add(b ?? new Label()); }
        Label L(string s) => new Label { Text = s, AutoSize = true, Anchor = AnchorStyles.Left };
        Linha(L("Formato"), _formato);
        Linha(L("Número de ramos"), _ramos);
        Linha(L("Gancho"), _gancho);
        Linha(L("Bitola (mm)"), _bitola);
        Linha(L("Aço"), _aco);
        Linha(L("Dimensões"), _naInsercao);
        Linha(new Label(), _fixo);
        Linha(_lBase, _base);
        Linha(_lAltura, _altura);
        Linha(_lDiametro, _diametro);
        Linha(new Label(), _secao);
        Linha(L("Cobrimento (cm)"), _cobrimento);
        Linha(L("Espaçamento das faixas (cm)"), _espacamento);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 80 };
        var cancelar = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Width = 80 };
        var botoes = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Bottom };
        botoes.Controls.Add(cancelar);
        botoes.Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = cancelar;

        var raiz = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
        raiz.Controls.Add(t);
        raiz.Controls.Add(botoes);
        Controls.Add(raiz);

        // valores
        _formato.SelectedIndex = (int)o.Formato;
        _ramos.Value = Math.Max(2, Math.Min(20, o.Ramos));
        _gancho.SelectedIndex = o.Gancho == FormatoSegmento.Gancho90 ? 1 : o.Gancho == FormatoSegmento.Gancho180 ? 2 : 0;
        _bitola.Text = o.Bitola.ToString("0.0", CultureInfo.InvariantCulture);
        _aco.SelectedItem = o.Aco;
        if (_aco.SelectedIndex < 0) _aco.SelectedIndex = 2;
        _naInsercao.Checked = !o.DimensoesFixas;
        _fixo.Checked = o.DimensoesFixas;
        _base.Text = F(o.Base);
        _altura.Text = F(o.Altura);
        _diametro.Text = F(o.Diametro);
        _secao.Checked = o.UsarSecao;
        _cobrimento.Text = F(o.Cobrimento);
        _espacamento.Text = F(o.Espacamento);

        _formato.SelectedIndexChanged += (s, e) => Habilitar();
        _fixo.CheckedChanged += (s, e) => Habilitar();
        _secao.CheckedChanged += (s, e) => Habilitar();
        Habilitar();
        FormClosing += AoFechar;
    }

    private static ComboBox Combo(params string[] itens)
    {
        var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        c.Items.AddRange(itens.Cast<object>().ToArray());
        return c;
    }

    private static TextBox Numero() => new TextBox { Width = 80 };

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static bool Ler(Control t, out double v) =>
        double.TryParse(t.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private void Habilitar()
    {
        var f = (FormatoEstribo)_formato.SelectedIndex;
        bool circular = f == FormatoEstribo.CircularExterno || f == FormatoEstribo.CircularInterno;
        _ramos.Enabled = f == FormatoEstribo.Aberto || f == FormatoEstribo.Multiplo || f == FormatoEstribo.Pente;
        _base.Enabled = _altura.Enabled = _fixo.Checked && !circular;
        _diametro.Enabled = _fixo.Checked && circular;
        _cobrimento.Enabled = _secao.Checked;
    }

    private void AoFechar(object sender, FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK) return;
        var erros = new System.Collections.Generic.List<string>();
        var f = (FormatoEstribo)_formato.SelectedIndex;
        int ramos = (int)_ramos.Value;
        if (f == FormatoEstribo.Multiplo && ramos % 2 != 0) erros.Add("Estribo múltiplo precisa de número par de ramos.");
        if (f == FormatoEstribo.Multiplo && ramos < 4) erros.Add("Estribo múltiplo tem no mínimo 4 ramos.");
        if (f == FormatoEstribo.Pente && ramos < 3) erros.Add("Pente tem no mínimo 3 ramos.");
        if (!Ler(_bitola, out var bitola) || bitola <= 0) erros.Add("Bitola inválida.");
        if (!Ler(_base, out var b) || b <= 0) erros.Add("Base inválida.");
        if (!Ler(_altura, out var h) || h <= 0) erros.Add("Altura inválida.");
        if (!Ler(_diametro, out var d) || d <= 0) erros.Add("Diâmetro inválido.");
        if (!Ler(_cobrimento, out var c) || c < 0) erros.Add("Cobrimento inválido.");
        if (!Ler(_espacamento, out var s) || s <= 0) erros.Add("Espaçamento inválido.");
        if (erros.Count > 0)
        {
            MessageBox.Show(string.Join("\n", erros), "Estribo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
            return;
        }
        Opcoes.Formato = f;
        Opcoes.Ramos = ramos;
        Opcoes.Gancho = _gancho.SelectedIndex == 1 ? FormatoSegmento.Gancho90 : _gancho.SelectedIndex == 2 ? FormatoSegmento.Gancho180 : FormatoSegmento.Gancho45;
        Opcoes.Bitola = bitola;
        Opcoes.Aco = (string)_aco.SelectedItem;
        Opcoes.DimensoesFixas = _fixo.Checked;
        Opcoes.Base = b;
        Opcoes.Altura = h;
        Opcoes.Diametro = d;
        Opcoes.UsarSecao = _secao.Checked;
        Opcoes.Cobrimento = c;
        Opcoes.Espacamento = s;
    }
}
