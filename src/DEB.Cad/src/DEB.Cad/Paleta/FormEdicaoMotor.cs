using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using DEB.Core.Calculo;
using DEB.Core.Motor;

namespace DEB.Cad.Paleta;

/// <summary>
/// ARMDETREGENERAR: os parâmetros do detalhamento gerado pelo motor (posição, parâmetro, regra, valor pela regra) com
/// a coluna "Manual" editável. Vazio = vale a regra. "Regenerar" grava as edições e redesenha o detalhamento.
/// </summary>
internal sealed class FormEdicaoMotor : Form
{
    private readonly DataGridView _grade = new DataGridView
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.CellSelect,
    };
    private readonly List<ParametroEditavel> _ps;
    private const int ColManual = 4;

    public FormEdicaoMotor(string detalhamento, List<ParametroEditavel> parametros, IEnumerable<string> avisos)
    {
        _ps = parametros;
        Text = $"Regenerar detalhamento — {detalhamento}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new System.Drawing.Size(900, 560);

        DataGridViewTextBoxColumn Col(string nome, bool so) =>
            new DataGridViewTextBoxColumn { HeaderText = nome, ReadOnly = so, SortMode = DataGridViewColumnSortMode.NotSortable };
        _grade.Columns.Add(Col("Posição", true));
        _grade.Columns.Add(Col("Parâmetro", true));
        _grade.Columns.Add(Col("Regra", true));
        _grade.Columns.Add(Col("Pela regra", true));
        _grade.Columns.Add(Col("Manual", false));
        string anterior = null;
        foreach (var p in parametros)
        {
            string pos = p.Posicao == anterior ? "" : $"{p.NomePosicao} ({p.Posicao})";
            anterior = p.Posicao;
            _grade.Rows.Add(pos, p.Parametro, p.Regra, CalculoComprimento.NumeroVirgula(p.PelaRegra),
                p.Manual.HasValue ? CalculoComprimento.NumeroVirgula(p.Manual.Value) : "");
        }
        Destacar();
        _grade.CellValidating += AoValidar;
        _grade.CellEndEdit += (s, e) => Destacar();

        var aviso = new Label
        {
            Dock = DockStyle.Top, AutoSize = false, Height = 40, Padding = new Padding(6, 4, 6, 0),
            Text = "Digite em \"Manual\" o valor que deve substituir a regra (cm ou contagem). Vazio = vale a regra. " +
                   "O detalhamento é redesenhado com o perfil e a receita atuais; o que foi mudado à mão no desenho se perde.",
        };
        var ok = new Button { Text = "Regenerar", DialogResult = DialogResult.OK, Width = 100 };
        var cancelar = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Width = 90 };
        var limpar = new Button { Text = "Tudo pela regra", Width = 120 };
        limpar.Click += (s, e) => { foreach (DataGridViewRow r in _grade.Rows) r.Cells[ColManual].Value = ""; Destacar(); };
        var botoes = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft, Height = 38 };
        botoes.Controls.Add(cancelar);
        botoes.Controls.Add(ok);
        botoes.Controls.Add(limpar);
        Controls.Add(_grade);
        Controls.Add(aviso);
        Controls.Add(botoes);
        var lista = string.Join("\n", avisos ?? new string[0]);
        if (lista.Length > 0)
            Controls.Add(new TextBox { Dock = DockStyle.Bottom, Multiline = true, ReadOnly = true, Height = 70, ScrollBars = ScrollBars.Vertical, Text = lista.Replace("\n", "\r\n") });
        AcceptButton = ok;
        CancelButton = cancelar;
        FormClosing += (s, e) => { if (DialogResult == DialogResult.OK) Gravar(); };
    }

    private static bool Ler(object v, out double? x)
    {
        x = null;
        var t = (v as string ?? "").Trim();
        if (t.Length == 0) return true;
        if (double.TryParse(t.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) { x = d; return true; }
        return false;
    }

    private void AoValidar(object s, DataGridViewCellValidatingEventArgs e)
    {
        if (e.ColumnIndex != ColManual) return;
        if (!Ler(e.FormattedValue, out _))
        {
            _grade.Rows[e.RowIndex].ErrorText = "Número inválido (use 25 ou 12,5; vazio = regra).";
            e.Cancel = true;
        }
        else _grade.Rows[e.RowIndex].ErrorText = "";
    }

    /// <summary>Linhas com valor manual em negrito.</summary>
    private void Destacar()
    {
        var negrito = new System.Drawing.Font(_grade.Font, System.Drawing.FontStyle.Bold);
        foreach (DataGridViewRow r in _grade.Rows)
        {
            bool manual = Ler(r.Cells[ColManual].Value, out var x) && x.HasValue;
            r.DefaultCellStyle.Font = manual ? negrito : _grade.Font;
        }
    }

    private void Gravar()
    {
        _grade.EndEdit();
        for (int i = 0; i < _ps.Count && i < _grade.Rows.Count; i++)
            if (Ler(_grade.Rows[i].Cells[ColManual].Value, out var x)) _ps[i].Manual = x;
    }
}
