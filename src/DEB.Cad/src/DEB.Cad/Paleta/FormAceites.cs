using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DEB.Cad.Infra;
using DEB.Core.Documentacao;

namespace DEB.Cad.Paleta;

/// <summary>
/// Janela do ARMACEITES (D37): as pendências "Conferir" dos relatórios de verificação de uma obra, uma por linha, para o
/// engenheiro aceitar com um comentário. Só mostra e recolhe as decisões; quem grava é o núcleo (<see cref="RelatoriosObra"/>).
/// </summary>
internal sealed class FormAceites : Form
{
    private readonly DataGridView _grade = new DataGridView
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.CellSelect, EditMode = DataGridViewEditMode.EditOnEnter,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, BackgroundColor = SystemColors.Window, Name = "grade",
    };
    private readonly TextBox _engenheiro = new TextBox { Width = 260, Name = "engenheiro" };
    private readonly Label _pasta = new Label { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    private readonly Label _resumo = new Label { AutoSize = true, Padding = new Padding(0, 6, 12, 0), Name = "resumo" };
    private List<PendenciaObra> _pendencias = new List<PendenciaObra>();

    /// <summary>Pasta da obra (onde ficam "Relatórios PCEB" e o arquivo dos aceites).</summary>
    public string PastaObra { get; private set; }
    public string Engenheiro { get => _engenheiro.Text.Trim(); set => _engenheiro.Text = value ?? ""; }

    public FormAceites(string pastaObra)
    {
        Text = "DEB - Aceite das pendências de verificação";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1100, 520);
        MinimumSize = new Size(760, 320);
        Font = new Font("Segoe UI", 9f);
        ShowInTaskbar = false;
        MinimizeBox = false;

        _grade.Columns.Add(new DataGridViewCheckBoxColumn { Name = "aceitar", HeaderText = "Aceitar", Width = 56 });
        Coluna("marca", "Marca", 60);
        Coluna("codigo", "Código", 56);
        Coluna("verificacao", "Verificação", 260, quebra: true);
        Coluna("valor", "Valor", 110, quebra: true);
        Coluna("limite", "Limite", 130, quebra: true);
        Coluna("comentario", "Comentário (por que aceita)", 220, quebra: true, editavel: true);
        Coluna("aceito", "Aceito por", 130, quebra: true);
        _grade.Columns["verificacao"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;   // ocupa a sobra: sem rolagem lateral
        // a caixa "Aceitar" vale no clique (sem esperar sair da célula), para o resumo acompanhar
        _grade.CurrentCellDirtyStateChanged += (s, e) => { if (_grade.IsCurrentCellDirty && _grade.CurrentCell is DataGridViewCheckBoxCell) _grade.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grade.CellValueChanged += (s, e) => AtualizarResumo();

        var topo = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6), WrapContents = true };
        var trocar = new Button { Text = "Outra pasta…", AutoSize = true, Name = "trocar" };
        trocar.Click += (s, e) => TrocarPasta();
        topo.Controls.Add(new Label { Text = "Engenheiro:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        topo.Controls.Add(_engenheiro);
        topo.Controls.Add(trocar);
        topo.Controls.Add(_pasta);

        var gravar = new Button { Text = "Gravar", Width = 90, Name = "gravar" };
        gravar.Click += (s, e) => { if (Validar()) { DialogResult = DialogResult.OK; Close(); } };
        var cancelar = new Button { Text = "Cancelar", Width = 90, DialogResult = DialogResult.Cancel };
        var indice = new Button { Text = "Abrir o índice da obra", AutoSize = true, Name = "indice" };
        indice.Click += (s, e) =>
        {
            try { Arquivos.Abrir(RelatoriosObra.GravarIndice(PastaObra)); }
            catch (Exception ex) { MessageBox.Show(this, "Não deu para abrir o índice: " + ex.Message, Text); }
        };
        var todas = new Button { Text = "Marcar todas", AutoSize = true, Name = "todas" };
        todas.Click += (s, e) => { foreach (DataGridViewRow l in _grade.Rows) l.Cells["aceitar"].Value = true; };
        var botoes = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(6) };
        botoes.Controls.Add(cancelar); botoes.Controls.Add(gravar); botoes.Controls.Add(indice); botoes.Controls.Add(todas); botoes.Controls.Add(_resumo);
        CancelButton = cancelar;

        Controls.Add(_grade);
        Controls.Add(topo);
        Controls.Add(botoes);
        Carregar(pastaObra);
    }

    private void Coluna(string nome, string titulo, int largura, bool quebra = false, bool editavel = false)
    {
        var c = new DataGridViewTextBoxColumn { Name = nome, HeaderText = titulo, Width = largura, ReadOnly = !editavel, SortMode = DataGridViewColumnSortMode.NotSortable };
        if (quebra) c.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        if (!editavel) c.DefaultCellStyle.BackColor = SystemColors.Control;
        _grade.Columns.Add(c);
    }

    /// <summary>Lê as pendências da pasta e preenche a grade (aceites já gravados vêm marcados).</summary>
    internal void Carregar(string pastaObra)
    {
        PastaObra = pastaObra;
        _pasta.Text = "Obra: " + pastaObra;
        _pendencias = RelatoriosObra.Pendencias(pastaObra);
        _grade.Rows.Clear();
        foreach (var p in _pendencias)
        {
            var r = p.Verificacao;
            _grade.Rows.Add(p.Aceite != null, p.Marca, r.Codigo, r.Descricao + (r.Dica.Length > 0 ? "\n" + r.Dica : "") + "\n(" + r.Referencia + ")",
                            r.Valor, r.Limite, p.Aceite?.Comentario ?? "", p.Aceite != null ? p.Aceite.Engenheiro + "\n" + p.Aceite.Data : "");
        }
        if (Engenheiro.Length == 0)
            Engenheiro = AceitesObra.Ler(pastaObra).Aceites.LastOrDefault(a => !string.IsNullOrWhiteSpace(a.Engenheiro))?.Engenheiro ?? "";
        AtualizarResumo();
    }

    private void AtualizarResumo()
    {
        int n = _grade.Rows.Count, ac = _grade.Rows.Cast<DataGridViewRow>().Count(l => Equals(l.Cells["aceitar"].Value, true));
        _resumo.Text = n == 0 ? "Nenhuma pendência \"Conferir\" nos relatórios desta pasta." : $"{n} pendência(s), {ac} aceita(s).";
    }

    private void TrocarPasta()
    {
        using var d = new FolderBrowserDialog { Description = "Pasta da obra (a que tem a subpasta \"" + RelatorioHtml.Subpasta + "\")", SelectedPath = PastaObra };
        if (d.ShowDialog(this) == DialogResult.OK) Carregar(d.SelectedPath);
    }

    /// <summary>Aceite novo precisa do nome de quem aceita.</summary>
    private bool Validar()
    {
        _grade.EndEdit();
        if (Engenheiro.Length > 0 || !Decisoes().Any(d => d.Aceitar && d.Pendencia.Aceite == null)) return true;
        MessageBox.Show(this, "Preencha o nome do engenheiro que aceita as pendências.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        _engenheiro.Focus();
        return false;
    }

    /// <summary>Decisão de cada linha, na ordem da grade.</summary>
    internal List<DecisaoAceite> Decisoes() =>
        _grade.Rows.Cast<DataGridViewRow>().Select(l => new DecisaoAceite
        {
            Pendencia = _pendencias[l.Index], Aceitar = Equals(l.Cells["aceitar"].Value, true),
            Comentario = Convert.ToString(l.Cells["comentario"].Value) ?? "",
        }).ToList();

    /// <summary>Para o autoteste: marca a linha e escreve o comentário como o usuário faria.</summary>
    internal void Marcar(int linha, bool aceitar, string comentario)
    {
        _grade.Rows[linha].Cells["aceitar"].Value = aceitar;
        _grade.Rows[linha].Cells["comentario"].Value = comentario;
    }

    internal int Linhas => _grade.Rows.Count;
    internal string Resumo => _resumo.Text;
}
