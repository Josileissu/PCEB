using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Armadura.Core.Modelo;

namespace Armadura.Cad.Paleta;

/// <summary>Uma linha do ARMDETGERENCIAR.</summary>
internal sealed class LinhaDetalhamento
{
    public ObjectId Id { get; }
    public string Nome { get; set; }
    public DadosDetalhamento Dados { get; }
    public int Repeticoes { get; }
    public bool Alterado { get; set; }

    public LinhaDetalhamento(ObjectId id, string nome, DadosDetalhamento d, int repeticoes)
    {
        Id = id; Nome = nome; Dados = d; Repeticoes = repeticoes;
    }
}

/// <summary>
/// Grade dos detalhamentos (seção 3.6): Nome, Grupo, Pavimento, Repetições (inserções), Volume,
/// Área de forma, Classe, Sistema construtivo, Comentários. F5 copia o valor da célula para a coluna
/// inteira (exceto Nome); linhas com comentários ficam em negrito.
/// </summary>
internal sealed class FormDetalhamentos : Form
{
    private readonly DataGridView _grade = new DataGridView
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells, RowHeadersVisible = false,
    };
    private readonly List<LinhaDetalhamento> _linhas;
    private static readonly string[] Sistemas = { "Moldado in loco", "Pré-moldado", "Alvenaria estrutural" };

    public FormDetalhamentos(List<LinhaDetalhamento> linhas)
    {
        _linhas = linhas;
        Text = "Detalhamentos";
        StartPosition = FormStartPosition.CenterParent;
        Size = new System.Drawing.Size(1000, 450);

        DataGridViewTextBoxColumn Txt(string nome, bool so = false) =>
            new DataGridViewTextBoxColumn { HeaderText = nome, ReadOnly = so, SortMode = DataGridViewColumnSortMode.NotSortable };
        _grade.Columns.Add(Txt("Nome"));
        _grade.Columns.Add(Txt("Grupo"));
        _grade.Columns.Add(Txt("Pavimento"));
        _grade.Columns.Add(Txt("Repetições", true));
        _grade.Columns.Add(Txt("Volume (m³)"));
        _grade.Columns.Add(Txt("Área de forma (m²)"));
        _grade.Columns.Add(Txt("Classe"));
        var sis = new DataGridViewComboBoxColumn { HeaderText = "Sistema construtivo", FlatStyle = FlatStyle.Flat };
        sis.Items.AddRange(Sistemas);
        _grade.Columns.Add(sis);
        _grade.Columns.Add(Txt("Comentários"));

        foreach (var l in linhas)
        {
            var d = l.Dados;
            _grade.Rows.Add(l.Nome, d.Grupo, d.Pavimento, l.Repeticoes.ToString(CultureInfo.InvariantCulture),
                N(d.Volume), N(d.AreaForma), d.Classe, Sistemas[(int)d.Sistema], d.Comentarios);
        }
        Negrito();
        _grade.CellEndEdit += (s, e) => Negrito();
        _grade.KeyDown += AoTecla;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90 };
        var cancelar = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Width = 90 };
        var dica = new Label { Text = "F5: copia o valor da célula para a coluna inteira (exceto Nome).", AutoSize = true, Padding = new Padding(0, 8, 20, 0) };
        var botoes = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft, Height = 38 };
        botoes.Controls.Add(cancelar);
        botoes.Controls.Add(ok);
        botoes.Controls.Add(dica);
        Controls.Add(_grade);
        Controls.Add(botoes);
        AcceptButton = ok;
        CancelButton = cancelar;
        FormClosing += AoFechar;
    }

    private static string N(double v) => v == 0 ? "" : v.ToString("0.###", CultureInfo.InvariantCulture);

    private void Negrito()
    {
        foreach (DataGridViewRow r in _grade.Rows)
        {
            bool temObs = !string.IsNullOrWhiteSpace(Convert.ToString(r.Cells[8].Value));
            r.DefaultCellStyle.Font = temObs ? new System.Drawing.Font(_grade.Font, System.Drawing.FontStyle.Bold) : _grade.Font;
        }
    }

    private void AoTecla(object sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.F5 || _grade.CurrentCell == null) return;
        int c = _grade.CurrentCell.ColumnIndex;
        if (c == 0 || _grade.Columns[c].ReadOnly) return;
        _grade.EndEdit();
        var v = _grade.CurrentCell.Value;
        foreach (DataGridViewRow r in _grade.Rows) r.Cells[c].Value = v;
        Negrito();
        e.Handled = true;
    }

    private static bool Num(object v, out double r)
    {
        var s = Convert.ToString(v)?.Trim().Replace(',', '.') ?? "";
        if (s.Length == 0) { r = 0; return true; }
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out r) && r >= 0;
    }

    private void AoFechar(object sender, FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK) return;
        _grade.EndEdit();
        var erros = new List<string>();
        var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _linhas.Count; i++)
        {
            var r = _grade.Rows[i];
            string nome = Convert.ToString(r.Cells[0].Value)?.Trim() ?? "";
            if (nome.Length == 0) erros.Add($"Linha {i + 1}: nome vazio.");
            else if (!nomes.Add(nome)) erros.Add($"Nome repetido: {nome}.");
            if (!Num(r.Cells[4].Value, out _)) erros.Add($"{nome}: volume inválido.");
            if (!Num(r.Cells[5].Value, out _)) erros.Add($"{nome}: área de forma inválida.");
        }
        if (erros.Count > 0)
        {
            MessageBox.Show(string.Join("\n", erros), "Detalhamentos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
            return;
        }
        for (int i = 0; i < _linhas.Count; i++)
        {
            var r = _grade.Rows[i];
            var l = _linhas[i];
            var d = l.Dados;
            Num(r.Cells[4].Value, out var vol);
            Num(r.Cells[5].Value, out var forma);
            string Txt(int c) => Convert.ToString(r.Cells[c].Value)?.Trim() ?? "";
            var sis = (SistemaConstrutivo)Math.Max(0, Array.IndexOf(Sistemas, Txt(7)));
            bool mudou = l.Nome != Txt(0) || d.Grupo != Txt(1) || d.Pavimento != Txt(2) || d.Volume != vol
                         || d.AreaForma != forma || d.Classe != Txt(6) || d.Sistema != sis || d.Comentarios != Txt(8);
            if (!mudou) continue;
            l.Nome = Txt(0);
            d.Grupo = Txt(1); d.Pavimento = Txt(2); d.Volume = vol; d.AreaForma = forma;
            d.Classe = Txt(6); d.Sistema = sis; d.Comentarios = Txt(8);
            l.Alterado = true;
        }
    }
}
