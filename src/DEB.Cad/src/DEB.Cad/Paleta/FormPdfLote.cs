using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DEB.Cad.Infra;
using DEB.Cad.Pdf;
using DEB.Core.Documentacao;
using FlowDirection = System.Windows.Forms.FlowDirection;
using Application = System.Windows.Forms.Application;
using Color = System.Drawing.Color;
using Font = System.Drawing.Font;

namespace DEB.Cad.Paleta;

/// <summary>
/// Janela do ARMPDFLOTE: origem das folhas (desenhos abertos, arquivos DWG, pasta), lista dos layouts com o formato reconhecido
/// (trocável no combo), impressora, estilo de plotagem, área e destino. As escolhas ficam em pdflote.json.
/// </summary>
internal sealed class FormPdfLote : Form
{
    private const string SemFormato = "?";
    private readonly ConfigLotePdf _cfg;
    private readonly List<FolhaPdf> _folhas = new List<FolhaPdf>();

    private readonly DataGridView _grade = new DataGridView
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
        EditMode = DataGridViewEditMode.EditOnEnter, BackgroundColor = SystemColors.Window,
    };
    private readonly CheckBox _subpastas = new CheckBox { Text = "Incluir subpastas", AutoSize = true, Margin = new Padding(8, 7, 3, 3) };
    private readonly ComboBox _impressora = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly ComboBox _estilo = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _area = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly RadioButton _naPasta = new RadioButton { Text = "Na pasta de cada desenho", AutoSize = true };
    private readonly RadioButton _outraPasta = new RadioButton { Text = "Nesta pasta:", AutoSize = true };
    private readonly TextBox _pasta = new TextBox { Width = 330 };
    private readonly CheckBox _sobrescrever = new CheckBox { Text = "Sobrescrever PDFs existentes sem perguntar", AutoSize = true };
    private readonly CheckBox _gravar = new CheckBox { Text = "Gravar impressora/folha/estilo no layout (só desenhos abertos; salve depois)", AutoSize = true };
    private readonly CheckBox _abrirPasta = new CheckBox { Text = "Abrir a pasta no fim", AutoSize = true };
    private readonly Label _status = new Label { AutoSize = true, Margin = new Padding(3, 8, 3, 3) };

    /// <summary>Folhas marcadas, na ordem da lista (depois do OK).</summary>
    public List<FolhaPdf> Selecionadas => _folhas.Where(f => f.Marcada).ToList();

    public FormPdfLote(ConfigLotePdf cfg)
    {
        _cfg = cfg;
        Text = "DEB - Exportar PDFs em lote";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        Size = new Size(1060, 660);
        MinimumSize = new Size(820, 480);
        Font = new Font("Segoe UI", 9f);

        // origem
        var origem = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 6, 6, 0) };
        origem.Controls.Add(Botao("Desenhos abertos", (s, e) => AdicionarAbertos()));
        origem.Controls.Add(Botao("Arquivos DWG…", (s, e) => AdicionarArquivos()));
        origem.Controls.Add(Botao("Pasta…", (s, e) => AdicionarPasta()));
        origem.Controls.Add(_subpastas);
        origem.Controls.Add(Botao("Limpar lista", (s, e) => { _folhas.Clear(); Preencher(); }));
        origem.Controls.Add(Botao("Marcar todas", (s, e) => Marcar(true)));
        origem.Controls.Add(Botao("Desmarcar todas", (s, e) => Marcar(false)));

        // grade
        _grade.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Marcada", HeaderText = "PDF", Width = 40 });
        _grade.Columns.Add(new DataGridViewTextBoxColumn { Name = "Desenho", HeaderText = "Desenho", Width = 250, ReadOnly = true });
        _grade.Columns.Add(new DataGridViewTextBoxColumn { Name = "Layout", HeaderText = "Folha (layout)", Width = 130, ReadOnly = true });
        _grade.Columns.Add(new DataGridViewTextBoxColumn { Name = "Medida", HeaderText = "Medida (mm)", Width = 110, ReadOnly = true });
        var formato = new DataGridViewComboBoxColumn { Name = "Formato", HeaderText = "Formato", Width = 110, FlatStyle = FlatStyle.Flat };
        formato.Items.Add(SemFormato);
        foreach (var fo in cfg.Formatos) formato.Items.Add(fo.Nome);
        _grade.Columns.Add(formato);
        _grade.Columns.Add(new DataGridViewTextBoxColumn { Name = "Origem", HeaderText = "Reconhecido por", Width = 220, ReadOnly = true });
        _grade.Columns.Add(new DataGridViewTextBoxColumn { Name = "Pasta", HeaderText = "Pasta", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        _grade.CurrentCellDirtyStateChanged += (s, e) => { if (_grade.IsCurrentCellDirty) _grade.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grade.CellValueChanged += AoMudarCelula;
        _grade.DataError += (s, e) => e.ThrowException = false;

        // opções
        foreach (string p in PrinterSettings.InstalledPrinters) _impressora.Items.Add(p);
        _impressora.Text = cfg.Impressora;
        var estilos = PlotadorPdf.EstilosDisponiveis().Where(e => e.EndsWith(".ctb", StringComparison.OrdinalIgnoreCase) || e.EndsWith(".stb", StringComparison.OrdinalIgnoreCase)).ToList();
        _estilo.Items.Add("(o de cada layout)");
        foreach (var e in estilos) _estilo.Items.Add(e);
        _area.Items.AddRange(new object[] { "Moldura reconhecida (janela)", "Extensão do layout", "Limites do layout" });
        _area.SelectedIndex = (int)cfg.Area;
        SelecionarEstilo(cfg.EstiloPlotagem);

        var plot = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Padding = new Padding(3) };
        void Linha(TableLayoutPanel t, string rotulo, Control c) { t.Controls.Add(new Label { Text = rotulo, AutoSize = true, Anchor = AnchorStyles.Left }); t.Controls.Add(c); }
        Linha(plot, "Impressora", _impressora);
        Linha(plot, "Estilo de plotagem", _estilo);
        Linha(plot, "Área", _area);
        var gPlot = new GroupBox { Text = "Plotagem (escala ajustada à folha, centralizado)", AutoSize = true, Padding = new Padding(6) };
        plot.Location = new Point(6, 20); // abaixo do título do GroupBox
        gPlot.Controls.Add(plot);

        var procurar = Botao("…", (s, e) => EscolherDestino());
        procurar.Width = 30;
        var linhaPasta = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        linhaPasta.Controls.Add(_outraPasta); linhaPasta.Controls.Add(_pasta); linhaPasta.Controls.Add(procurar);
        var dest = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(3) };
        dest.Controls.Add(_naPasta); dest.Controls.Add(linhaPasta); dest.Controls.Add(_sobrescrever); dest.Controls.Add(_gravar); dest.Controls.Add(_abrirPasta);
        var gDest = new GroupBox { Text = "Destino (um PDF por folha, com o nome do DWG)", AutoSize = true, Padding = new Padding(6) };
        dest.Location = new Point(6, 20);
        gDest.Controls.Add(dest);
        _naPasta.Checked = cfg.NaPastaDoDesenho;
        _outraPasta.Checked = !cfg.NaPastaDoDesenho;
        _pasta.Text = cfg.PastaDestino;
        _subpastas.Checked = cfg.Subpastas;
        _sobrescrever.Checked = cfg.SobrescreverTudo;
        _gravar.Checked = cfg.GravarNoLayout;
        _abrirPasta.Checked = cfg.AbrirPastaNoFim;

        var opcoes = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6, 0, 6, 0) };
        opcoes.Controls.Add(gPlot);
        opcoes.Controls.Add(gDest);

        var exportar = new Button { Text = "Exportar PDFs", Width = 120, Height = 28 };
        exportar.Click += (s, e) => Exportar();
        var fechar = new Button { Text = "Fechar", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
        var botoes = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(6) };
        botoes.Controls.Add(fechar); botoes.Controls.Add(exportar); botoes.Controls.Add(_status);
        CancelButton = fechar;

        Controls.Add(_grade);
        Controls.Add(origem);
        Controls.Add(opcoes);
        Controls.Add(botoes);
        FormClosing += (s, e) => GuardarEscolhas();
    }

    private static Button Botao(string texto, EventHandler click)
    {
        var b = new Button { Text = texto, AutoSize = true, Height = 26 };
        b.Click += click;
        return b;
    }

    private void SelecionarEstilo(string nome)
    {
        int i = string.IsNullOrEmpty(nome) ? -1 : _estilo.Items.IndexOf(_estilo.Items.Cast<object>().FirstOrDefault(o => string.Equals(o.ToString(), nome, StringComparison.OrdinalIgnoreCase)));
        _estilo.SelectedIndex = i < 0 ? 0 : i;
    }

    // ---------------- origem ----------------

    /// <summary>Acrescenta os layouts dos desenhos abertos neste CAD.</summary>
    public void AdicionarAbertos()
    {
        var docs = CadApp.DocumentManager.Cast<Document>().ToList();
        Carregar(docs.Select(d => (Func<List<FolhaPdf>>)(() => LeitorFolhas.DoDocumento(d, _cfg))).ToList(), docs.Select(d => d.Name).ToList());
    }

    private void AdicionarArquivos()
    {
        using var d = new OpenFileDialog
        {
            Title = "DWGs para exportar", Filter = "Desenhos (*.dwg)|*.dwg", Multiselect = true,
            InitialDirectory = Directory.Exists(_cfg.UltimaPastaDwg) ? _cfg.UltimaPastaDwg : "",
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _cfg.UltimaPastaDwg = Path.GetDirectoryName(d.FileNames[0]);
        AdicionarArquivos(d.FileNames);
    }

    private void AdicionarPasta()
    {
        using var d = new FolderBrowserDialog { Description = "Pasta com os DWGs", SelectedPath = Directory.Exists(_cfg.UltimaPastaDwg) ? _cfg.UltimaPastaDwg : "" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _cfg.UltimaPastaDwg = d.SelectedPath;
        string[] arqs;
        try { arqs = Directory.GetFiles(d.SelectedPath, "*.dwg", _subpastas.Checked ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly); }
        catch (Exception ex) { MessageBox.Show(this, "Não deu para ler a pasta: " + ex.Message, Text); return; }
        AdicionarArquivos(arqs.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    /// <summary>Acrescenta DWGs (um aberto neste CAD é lido do próprio documento).</summary>
    public void AdicionarArquivos(IList<string> arquivos)
    {
        var leitores = new List<Func<List<FolhaPdf>>>();
        foreach (var a in arquivos)
        {
            var caminho = Path.GetFullPath(a);
            var doc = ExecutorLotePdf.AbertoNesteCad(caminho);
            leitores.Add(doc != null ? () => LeitorFolhas.DoDocumento(doc, _cfg) : () => LeitorFolhas.DoArquivo(caminho, _cfg));
        }
        Carregar(leitores, arquivos.ToList());
    }

    private void Carregar(List<Func<List<FolhaPdf>>> leitores, List<string> nomes)
    {
        Cursor = Cursors.WaitCursor;
        var erros = new List<string>();
        try
        {
            for (int i = 0; i < leitores.Count; i++)
            {
                _status.Text = $"Lendo {i + 1}/{leitores.Count}: {Path.GetFileName(nomes[i])}";
                Application.DoEvents();
                try
                {
                    foreach (var f in leitores[i]())
                        if (!_folhas.Any(o => string.Equals(o.Arquivo, f.Arquivo, StringComparison.OrdinalIgnoreCase) && o.Layout == f.Layout))
                            _folhas.Add(f);
                }
                catch (Exception ex) { erros.Add(Path.GetFileName(nomes[i]) + ": " + ex.Message); Log.Erro("ARMPDFLOTE: ler " + nomes[i], ex); }
            }
        }
        finally { Cursor = Cursors.Default; }
        // estilo ainda não escolhido: o mais usado nos layouts lidos
        if (_estilo.SelectedIndex <= 0 && string.IsNullOrEmpty(_cfg.EstiloPlotagem))
        {
            var comum = _folhas.Where(f => f.EstiloAtual.Length > 0).GroupBy(f => f.EstiloAtual, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            if (comum != null) SelecionarEstilo(comum);
        }
        Preencher();
        if (erros.Count > 0) MessageBox.Show(this, "Não deu para ler:\n" + string.Join("\n", erros), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private bool _preenchendo;

    private void Preencher()
    {
        _preenchendo = true;
        _grade.Rows.Clear();
        foreach (var f in _folhas)
        {
            string medida = f.TemCaixa
                ? FormattableString.Invariant($"{f.Largura * (f.Reconhecido?.Fator > 0 ? f.Reconhecido.Fator : 1):0} × {f.Altura * (f.Reconhecido?.Fator > 0 ? f.Reconhecido.Fator : 1):0}")
                : "-";
            string origem = f.Erro != null ? "erro: " + f.Erro
                : f.Reconhecido?.Formato != null ? f.OrigemCaixa + (f.Reconhecido.Fator != 1 ? FormattableString.Invariant($" (papel em {(f.Reconhecido.Fator == 10 ? "cm" : f.Reconhecido.Fator == 1000 ? "m" : "pol")})") : "")
                : "não reconhecido: escolha o formato";
            int i = _grade.Rows.Add(f.Marcada, f.NomeDesenho + (f.Doc != null ? " (aberto)" : ""), f.Layout, medida, f.Formato?.Nome ?? SemFormato, origem, Path.GetDirectoryName(f.Arquivo));
            _grade.Rows[i].Tag = f;
            if (f.Formato == null) _grade.Rows[i].Cells["Formato"].Style.BackColor = Color.FromArgb(255, 235, 200);
        }
        _preenchendo = false;
        int sem = _folhas.Count(f => f.Marcada && f.Formato == null);
        _status.Text = $"{_folhas.Count} folha(s), {_folhas.Count(f => f.Marcada)} marcada(s)" + (sem > 0 ? $", {sem} sem formato (?)" : "");
    }

    private void AoMudarCelula(object sender, DataGridViewCellEventArgs e)
    {
        if (_preenchendo || e.RowIndex < 0) return;
        var row = _grade.Rows[e.RowIndex];
        if (!(row.Tag is FolhaPdf f)) return;
        var col = _grade.Columns[e.ColumnIndex].Name;
        if (col == "Marcada") f.Marcada = Convert.ToBoolean(row.Cells[e.ColumnIndex].Value);
        else if (col == "Formato")
        {
            f.Formato = FormatosFolha.PorNome(_cfg.Formatos, Convert.ToString(row.Cells[e.ColumnIndex].Value));
            row.Cells[e.ColumnIndex].Style.BackColor = f.Formato == null ? Color.FromArgb(255, 235, 200) : _grade.DefaultCellStyle.BackColor;
        }
        int sem = _folhas.Count(x => x.Marcada && x.Formato == null);
        _status.Text = $"{_folhas.Count} folha(s), {_folhas.Count(x => x.Marcada)} marcada(s)" + (sem > 0 ? $", {sem} sem formato (?)" : "");
    }

    private void Marcar(bool sim)
    {
        foreach (var f in _folhas) f.Marcada = sim;
        Preencher();
    }

    private void EscolherDestino()
    {
        using var d = new FolderBrowserDialog { Description = "Pasta dos PDFs", SelectedPath = Directory.Exists(_pasta.Text) ? _pasta.Text : "" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _pasta.Text = d.SelectedPath;
        _outraPasta.Checked = true;
    }

    // ---------------- exportar ----------------

    private void GuardarEscolhas()
    {
        _cfg.Impressora = _impressora.Text.Trim();
        _cfg.EstiloPlotagem = _estilo.SelectedIndex <= 0 ? "" : _estilo.SelectedItem.ToString();
        _cfg.Area = (AreaPlotLote)Math.Max(0, _area.SelectedIndex);
        _cfg.NaPastaDoDesenho = _naPasta.Checked;
        _cfg.PastaDestino = _pasta.Text.Trim();
        _cfg.Subpastas = _subpastas.Checked;
        _cfg.SobrescreverTudo = _sobrescrever.Checked;
        _cfg.GravarNoLayout = _gravar.Checked;
        _cfg.AbrirPastaNoFim = _abrirPasta.Checked;
    }

    private void Exportar()
    {
        _grade.EndEdit();
        GuardarEscolhas();
        var sel = Selecionadas;
        if (sel.Count == 0) { MessageBox.Show(this, "Marque ao menos uma folha.", Text); return; }
        var sem = sel.Where(f => f.Formato == null).ToList();
        if (sem.Count > 0)
        {
            MessageBox.Show(this, "Escolha o formato das folhas marcadas com \"?\":\n" +
                                  string.Join("\n", sem.Take(15).Select(f => $"{f.NomeDesenho} / {f.Layout}")), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!Pdf24.EhPdf24(_cfg.Impressora))
        {
            MessageBox.Show(this, "O lote sem diálogo usa a impressora PDF24 (salvamento automático). Escolha a PDF24.", Text);
            return;
        }
        if (!_cfg.NaPastaDoDesenho && !Directory.Exists(_cfg.PastaDestino))
        {
            if (string.IsNullOrEmpty(_cfg.PastaDestino)) { MessageBox.Show(this, "Escolha a pasta dos PDFs.", Text); return; }
            try { Directory.CreateDirectory(_cfg.PastaDestino); }
            catch (Exception ex) { MessageBox.Show(this, "Não deu para criar a pasta: " + ex.Message, Text); return; }
        }
        DialogResult = DialogResult.OK;
    }
}

/// <summary>Progresso do lote, com cancelar (entre uma folha e outra).</summary>
internal sealed class FormProgressoPdf : Form
{
    private readonly ProgressBar _barra = new ProgressBar { Width = 420, Height = 20 };
    private readonly Label _texto = new Label { Width = 420, Height = 34, AutoEllipsis = true };
    private readonly Button _cancelar = new Button { Text = "Cancelar", Width = 100 };
    public bool Cancelado { get; private set; }

    public FormProgressoPdf()
    {
        Text = "DEB - Exportando PDFs";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ControlBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(10);
        Font = new Font("Segoe UI", 9f);
        _cancelar.Click += (s, e) => { Cancelado = true; _cancelar.Enabled = false; _cancelar.Text = "Cancelando…"; };
        var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true };
        p.Controls.Add(_texto); p.Controls.Add(_barra); p.Controls.Add(_cancelar);
        Controls.Add(p);
    }

    public void Atualizar(int feitas, int total, string texto)
    {
        _barra.Maximum = Math.Max(1, total);
        _barra.Value = Math.Min(_barra.Maximum, Math.Max(0, feitas));
        if (!string.IsNullOrEmpty(texto)) _texto.Text = $"{Math.Min(feitas + 1, total)}/{total}  {texto}";
        Application.DoEvents();
    }
}

/// <summary>Relatório do fim do lote: PDFs gerados e erros.</summary>
internal sealed class FormRelatorioPdf : Form
{
    public FormRelatorioPdf(IList<ResultadoFolha> res, bool cancelado)
    {
        Text = "DEB - PDFs em lote: relatório";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(860, 480);
        Font = new Font("Segoe UI", 9f);
        int ok = res.Count(r => r.Pdf != null), puladas = res.Count(r => r.Pulada), erros = res.Count(r => r.Pdf == null && !r.Pulada);
        var linhas = new List<string>
        {
            $"{ok} PDF(s) gerado(s), {erros} erro(s), {puladas} pulado(s)" + (cancelado ? " — lote CANCELADO" : "") + ".", "",
        };
        foreach (var r in res)
        {
            if (r.Pdf != null) linhas.Add($"OK     {Path.GetFileName(r.Pdf)}   [{r.Folha.Formato?.Nome}]   {Path.GetDirectoryName(r.Pdf)}");
            else linhas.Add($"{(r.Pulada ? "PULADO" : "ERRO  ")} {r.Folha.NomeDesenho} / {r.Folha.Layout}: {r.Erro}");
        }
        var caixa = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9f), Text = string.Join(Environment.NewLine, linhas),
        };
        var pastas = res.Where(r => r.Pdf != null).Select(r => Path.GetDirectoryName(r.Pdf)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var abrir = new Button { Text = "Abrir pasta", Width = 110, Enabled = pastas.Count > 0 };
        abrir.Click += (s, e) => { foreach (var p in pastas.Take(3)) Arquivos.Abrir(p); };
        var fechar = new Button { Text = "Fechar", Width = 90, DialogResult = DialogResult.OK };
        var botoes = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(6) };
        botoes.Controls.Add(fechar); botoes.Controls.Add(abrir);
        AcceptButton = fechar; CancelButton = fechar;
        Shown += (s, e) => { caixa.SelectionStart = 0; caixa.SelectionLength = 0; fechar.Focus(); };
        Controls.Add(caixa);
        Controls.Add(botoes);
    }
}
