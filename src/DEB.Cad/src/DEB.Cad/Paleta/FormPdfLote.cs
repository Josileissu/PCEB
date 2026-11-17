using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    /// <summary>Folhas (nomes locais) da impressora escolhida; null = não deu para ler (não avisa).</summary>
    private HashSet<string> _midias;
    private string _midiasDe;
    private static readonly Color CorAviso = Color.FromArgb(255, 235, 200);

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
        if (ComandosPdf.NoAutoCad)
        {
            // AutoCAD: a AutoCAD PDF (plotar para arquivo; padrão) e a PDF24, se estiver instalada
            var dispositivos = PlotadorPdf.Dispositivos();
            _impressora.Items.Add(dispositivos.FirstOrDefault(d => string.Equals(d, ImpressorasPdf.AutoCadPdf, StringComparison.OrdinalIgnoreCase)) ?? ImpressorasPdf.AutoCadPdf);
            foreach (string p in PrinterSettings.InstalledPrinters)
                if (ImpressorasPdf.Tipo(p) == TipoImpressoraPdf.Pdf24) _impressora.Items.Add(p);
        }
        else foreach (string p in PrinterSettings.InstalledPrinters) _impressora.Items.Add(p);
        _impressora.Text = cfg.ImpressoraLote;
        _impressora.SelectedIndexChanged += (s, e) => { if (!_preenchendo) Preencher(); };
        _impressora.Leave += (s, e) => Preencher();
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
        var criar = Botao("Criar folhas na impressora", (s, e) => CriarFolhas());
        Linha(plot, "Folhas", criar);
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

    /// <summary>Lê de novo as folhas da impressora escolhida (se ela mudou ou se <paramref name="forcar"/>).</summary>
    private void AtualizarMidias(bool forcar = false)
    {
        var imp = _impressora.Text.Trim();
        if (!forcar && string.Equals(imp, _midiasDe, StringComparison.OrdinalIgnoreCase)) return;
        _midiasDe = imp;
        _midias = PlotadorPdf.NomesMidias(imp);
        if (_midias == null && ImpressorasPdf.Tipo(imp) != TipoImpressoraPdf.Pc3)
        {
            var papeis = FormulariosWindows.PapeisDaImpressora(imp); // sem desenho aberto: pergunta ao driver
            _midias = papeis.Count > 0 ? papeis : null;
        }
    }

    /// <summary>A impressora escolhida tem a folha deste formato? (sem a lista: considera que sim)</summary>
    private bool TemFolha(FormatoFolha f) => f == null || _midias == null || _midias.Contains(f.Nome.Trim());

    private const string AvisoSemFolha = "a impressora não tem esta folha: crie as folhas na impressora";

    private void Preencher()
    {
        AtualizarMidias();
        _preenchendo = true;
        _grade.Rows.Clear();
        foreach (var f in _folhas)
        {
            string medida = f.TemCaixa
                ? FormattableString.Invariant($"{f.Largura * (f.Reconhecido?.Fator > 0 ? f.Reconhecido.Fator : 1):0} × {f.Altura * (f.Reconhecido?.Fator > 0 ? f.Reconhecido.Fator : 1):0}")
                : "-";
            string origem = f.Erro != null ? "erro: " + f.Erro
                : f.Formato != null && !TemFolha(f.Formato) ? AvisoSemFolha
                : f.Reconhecido?.Formato != null ? f.OrigemCaixa + (f.Reconhecido.Fator != 1 ? FormattableString.Invariant($" (papel em {(f.Reconhecido.Fator == 10 ? "cm" : f.Reconhecido.Fator == 1000 ? "m" : "pol")})") : "")
                : "não reconhecido: escolha o formato";
            int i = _grade.Rows.Add(f.Marcada, f.NomeDesenho + (f.Doc != null ? " (aberto)" : ""), f.Layout, medida, f.Formato?.Nome ?? SemFormato, origem, Path.GetDirectoryName(f.Arquivo));
            _grade.Rows[i].Tag = f;
            if (f.Formato == null || !TemFolha(f.Formato)) _grade.Rows[i].Cells["Formato"].Style.BackColor = CorAviso;
        }
        _preenchendo = false;
        AtualizarStatus();
    }

    private void AtualizarStatus()
    {
        int sem = _folhas.Count(f => f.Marcada && f.Formato == null);
        int semFolha = _folhas.Count(f => f.Marcada && f.Formato != null && !TemFolha(f.Formato));
        _status.Text = $"{_folhas.Count} folha(s), {_folhas.Count(f => f.Marcada)} marcada(s)" + (sem > 0 ? $", {sem} sem formato (?)" : "") +
                       (semFolha > 0 ? $", {semFolha} sem a folha na impressora" : "");
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
            row.Cells[e.ColumnIndex].Style.BackColor = f.Formato == null || !TemFolha(f.Formato) ? CorAviso : _grade.DefaultCellStyle.BackColor;
            if (f.Formato != null && !TemFolha(f.Formato)) row.Cells["Origem"].Value = AvisoSemFolha;
        }
        AtualizarStatus();
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

    // ---------------- criar folhas ----------------

    /// <summary>
    /// "Criar folhas na impressora": PDF24 (ou outra impressora do Windows) = formulários de papel do Windows da lista de formatos,
    /// sem margem e em ordem crescente (plano no núcleo: <see cref="FormulariosPapel"/>); .pc3 do AutoCAD = instruções para criar
    /// os tamanhos personalizados à mão (o .pmp não é gravado).
    /// </summary>
    private void CriarFolhas()
    {
        GuardarEscolhas();
        var imp = _cfg.ImpressoraLote;
        const string titulo = "DEB - Criar folhas na impressora";
        if (ImpressorasPdf.Tipo(imp) == TipoImpressoraPdf.Pc3)
        {
            MessageBox.Show(this, FormulariosPapel.InstrucoesPc3(_cfg.Formatos, imp), titulo, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            List<FormularioWindows> existentes;
            using (var srv = FormulariosWindows.Abrir(false)) existentes = srv.Listar();
            var plano = FormulariosPapel.Planejar(_cfg.Formatos, existentes, true);
            var conflitos = plano.ConflitosImpressora;
            if (conflitos.Count > 0)
            {
                var txt = "Já existe formulário instalado pelo driver de uma impressora com o mesmo nome e outro tamanho:\n\n" +
                          string.Join("\n", conflitos.Select(p => $"   {p.Formato.Nome}: o do driver tem {FormulariosPapel.Mm(p.Existente.Largura)} × " +
                                                                  $"{FormulariosPapel.Mm(p.Existente.Altura)} mm; a lista pede {FormulariosPapel.Mm(p.Largura)} × {FormulariosPapel.Mm(p.Altura)} mm")) +
                          "\n\nPode substituir?\nSim = apagar o do driver e criar o da lista\nNão = deixar o do driver e pular essa folha\nCancelar = não fazer nada";
                var r = MessageBox.Show(this, txt, titulo, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.No) plano = FormulariosPapel.Planejar(_cfg.Formatos, existentes, false);
            }
            if (plano.NadaAFazer)
            {
                MessageBox.Show(this, plano.TextoConfirmacao(), titulo, MessageBoxButtons.OK, MessageBoxIcon.Information);
                AtualizarMidias(true); Preencher();
                return;
            }
            if (MessageBox.Show(this, plano.TextoConfirmacao() + "\n\nContinuar?", titulo, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            ResultadoFormularios res;
            Cursor = Cursors.WaitCursor;
            try
            {
                using var srv = FormulariosWindows.Abrir(true);
                res = FormulariosPapel.Aplicar(plano, srv);
            }
            finally { Cursor = Cursors.Default; }
            Log.Info("ARMPDFLOTE: criar folhas na impressora — " + res.Texto().Replace(Environment.NewLine, " | "));

            AtualizarMidias(true);
            Preencher();
            var faltam = FormulariosPapel.EmOrdem(_cfg.Formatos).Where(f => !TemFolha(f)).Select(f => f.Nome).ToList();
            string conferencia = _midias == null ? ""
                : faltam.Count == 0 ? $"\n\nA impressora \"{imp}\" já lista todas as folhas."
                : $"\n\nA impressora \"{imp}\" ainda não lista: {string.Join(", ", faltam)}. Se acabaram de ser criadas, feche e abra o CAD.";
            MessageBox.Show(this, res.Texto() + conferencia, titulo, MessageBoxButtons.OK,
                res.Erros.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == FormulariosPapel.ErroAcessoNegado)
        {
            Log.Erro("ARMPDFLOTE: criar folhas na impressora", ex);
            MessageBox.Show(this, "O Windows negou acesso aos formulários de papel (" + ex.Message + ").\n\n" +
                                  "Feche o CAD, abra-o como administrador (botão direito → Executar como administrador) e use o botão de novo.",
                titulo, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            Log.Erro("ARMPDFLOTE: criar folhas na impressora", ex);
            MessageBox.Show(this, "Não deu para criar as folhas: " + ex.Message, titulo, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---------------- exportar ----------------

    private void GuardarEscolhas()
    {
        _cfg.ImpressoraLote = _impressora.Text.Trim();
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
        var tipo = ImpressorasPdf.Tipo(_cfg.ImpressoraLote);
        if (tipo == TipoImpressoraPdf.Outra || (tipo == TipoImpressoraPdf.Pc3 && !ComandosPdf.NoAutoCad))
        {
            MessageBox.Show(this, ComandosPdf.NoAutoCad
                ? "O lote sem diálogo usa a \"" + ImpressorasPdf.AutoCadPdf + "\" (plotar para arquivo) ou a PDF24 (salvamento automático). Escolha uma delas."
                : "O lote sem diálogo usa a impressora PDF24 (salvamento automático). Escolha a PDF24.", Text);
            return;
        }
        AtualizarMidias();
        var semFolha = sel.Where(f => !TemFolha(f.Formato)).Select(f => f.Formato.Nome).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (semFolha.Count > 0 &&
            MessageBox.Show(this, $"A impressora \"{_cfg.ImpressoraLote}\" não tem a(s) folha(s): {string.Join(", ", semFolha)}.\n" +
                                  "Use \"Criar folhas na impressora\" antes. Essas folhas vão dar erro no lote.\n\nExportar mesmo assim?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
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
