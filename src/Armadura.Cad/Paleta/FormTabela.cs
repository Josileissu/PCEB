using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.Design;
using Armadura.Core.Templates;

namespace Armadura.Cad.Paleta;

/// <summary>
/// Janela de uma lista do template (consolos, níveis, insertos — pedido do usuário, 2026-10-29): uma linha por item,
/// colunas do parâmetro, adicionar/remover/subir/descer e Ctrl+V do Excel (colunas separadas por tabulação).
/// </summary>
internal sealed class FormTabela : Form
{
    private readonly ParametroTemplate _p;
    private readonly DataGridView _grade = new DataGridView
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, RowHeadersWidth = 28, SelectionMode = DataGridViewSelectionMode.CellSelect,
        EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
    };

    public string Valor { get; private set; }

    public FormTabela(ParametroTemplate p, string valor)
    {
        _p = p;
        Valor = valor;
        Text = p.Rotulo;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(Math.Max(560, p.Colunas.Sum(cl => cl.Largura) + 190), 420);
        ShowInTaskbar = false;
        MinimizeBox = false;

        foreach (var col in p.Colunas)
        {
            DataGridViewColumn c;
            if (col.Tipo == TipoParametro.Opcao)
            {
                var cb = new DataGridViewComboBoxColumn { FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton };
                cb.Items.AddRange(col.Opcoes.Cast<object>().ToArray());
                c = cb;
            }
            else c = new DataGridViewTextBoxColumn();
            c.Name = col.Nome;
            c.HeaderText = col.Rotulo;
            c.Width = col.Largura;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grade.Columns.Add(c);
        }
        foreach (var l in ValoresTemplate.LerTabela(valor)) AdicionarLinha(l);

        var ajuda = new Label
        {
            Dock = DockStyle.Top, Height = 50, Padding = new Padding(6),
            Text = p.Descricao + "  Ctrl+V cola linhas copiadas do Excel.",
        };
        var lateral = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 100, FlowDirection = System.Windows.Forms.FlowDirection.TopDown, Padding = new Padding(4) };
        Button B(string t) { var b = new Button { Text = t, Width = 88, Height = 26 }; lateral.Controls.Add(b); return b; }
        var add = B("Adicionar");
        var rem = B("Remover");
        var sobe = B("Subir");
        var desce = B("Descer");
        var botoes = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft, Padding = new Padding(4) };
        var cancelar = new Button { Text = "Cancelar", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "OK", Width = 90, Height = 28 };
        botoes.Controls.Add(cancelar);
        botoes.Controls.Add(ok);
        CancelButton = cancelar;

        add.Click += (s, e) => { AdicionarLinha(p.Colunas.Select(c => c.Padrao).ToArray()); _grade.CurrentCell = _grade.Rows[_grade.Rows.Count - 1].Cells[0]; };
        rem.Click += (s, e) =>
        {
            foreach (var i in LinhasSelecionadas().OrderByDescending(i => i)) _grade.Rows.RemoveAt(i);
        };
        sobe.Click += (s, e) => Mover(-1);
        desce.Click += (s, e) => Mover(1);
        _grade.KeyDown += (s, e) =>
        {
            if (e.Control && e.KeyCode == Keys.V) { Colar(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete && !_grade.IsCurrentCellInEditMode)
            {
                foreach (DataGridViewCell c in _grade.SelectedCells) if (!(c.OwningColumn is DataGridViewComboBoxColumn)) c.Value = "";
                e.Handled = true;
            }
        };
        _grade.DataError += (s, e) => e.ThrowException = false; // valor fora da lista de um combo: fica em branco
        ok.Click += (s, e) =>
        {
            _grade.EndEdit();
            var erro = Validar();
            if (erro != null) { MessageBox.Show(erro, "Armadura", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            Valor = ValoresTemplate.JuntarTabela(_grade.Rows.Cast<DataGridViewRow>()
                .Select(r => r.Cells.Cast<DataGridViewCell>().Select(c => Convert.ToString(c.Value, CultureInfo.InvariantCulture) ?? "")));
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.Add(_grade);
        Controls.Add(lateral);
        Controls.Add(ajuda);
        Controls.Add(botoes);
    }

    private void AdicionarLinha(string[] valores)
    {
        int i = _grade.Rows.Add();
        for (int c = 0; c < _p.Colunas.Count; c++)
        {
            string v = c < valores.Length ? valores[c] : _p.Colunas[c].Padrao;
            var col = _p.Colunas[c];
            if (col.Tipo == TipoParametro.Opcao)
            {
                // aceita o valor sem diferenciar maiúsculas; fora da lista fica em branco
                v = col.Opcoes.FirstOrDefault(o => string.Equals(o, v, StringComparison.OrdinalIgnoreCase)) ?? "";
            }
            _grade.Rows[i].Cells[c].Value = v;
        }
    }

    private List<int> LinhasSelecionadas() =>
        _grade.SelectedCells.Cast<DataGridViewCell>().Select(c => c.RowIndex).Concat(_grade.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index))
            .Where(i => i >= 0).Distinct().ToList();

    private void Mover(int d)
    {
        var cel = _grade.CurrentCell;
        if (cel == null) return;
        int i = cel.RowIndex, j = i + d;
        if (j < 0 || j >= _grade.Rows.Count) return;
        var linha = _grade.Rows[i];
        _grade.Rows.RemoveAt(i);
        _grade.Rows.Insert(j, linha);
        _grade.CurrentCell = _grade.Rows[j].Cells[cel.ColumnIndex];
    }

    /// <summary>Cola do Excel: linhas por quebra, colunas por tabulação, a partir da célula atual (cria linhas se faltar).</summary>
    private void Colar()
    {
        string texto = Clipboard.ContainsText() ? Clipboard.GetText() : "";
        if (texto.Length == 0) return;
        var linhas = texto.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        int r0 = _grade.CurrentCell?.RowIndex ?? _grade.Rows.Count, c0 = _grade.CurrentCell?.ColumnIndex ?? 0;
        for (int i = 0; i < linhas.Length; i++)
        {
            int r = r0 + i;
            if (r >= _grade.Rows.Count) AdicionarLinha(_p.Colunas.Select(c => c.Padrao).ToArray());
            var cels = linhas[i].Split('\t');
            for (int j = 0; j < cels.Length && c0 + j < _p.Colunas.Count; j++)
            {
                var col = _p.Colunas[c0 + j];
                string v = cels[j].Trim();
                if (col.Tipo == TipoParametro.Opcao) v = col.Opcoes.FirstOrDefault(o => string.Equals(o, v, StringComparison.OrdinalIgnoreCase)) ?? "";
                _grade.Rows[r].Cells[c0 + j].Value = v;
            }
        }
    }

    private string Validar()
    {
        for (int r = 0; r < _grade.Rows.Count; r++)
            for (int c = 0; c < _p.Colunas.Count; c++)
            {
                var col = _p.Colunas[c];
                string v = Convert.ToString(_grade.Rows[r].Cells[c].Value, CultureInfo.InvariantCulture) ?? "";
                if (col.Tipo == TipoParametro.Numero && v.Length > 0 &&
                    !double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return $"Linha {r + 1}, \"{col.Rotulo}\": informe um número.";
                if (col.Tipo == TipoParametro.Opcao && v.Length == 0)
                    return $"Linha {r + 1}, \"{col.Rotulo}\": escolha da lista.";
            }
        return null;
    }
}

/// <summary>Mostra a lista como resumo ("3 consolos") no PropertyGrid; o texto não é digitado ali.</summary>
internal sealed class ResumoTabelaConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType) => false;
    public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType) => destinationType == typeof(string);
    public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
    {
        int n = ValoresTemplate.LerTabela(value as string).Count;
        string item = (context?.PropertyDescriptor as IParametroDescritor)?.Parametro.NomeItem ?? "item";
        string plural = item.EndsWith("l") ? item.Substring(0, item.Length - 1) + "is" : item + "s"; // nível → níveis
        return n == 0 ? "(nenhum)" : n == 1 ? $"1 {item}" : $"{n} {plural}";
    }
}

/// <summary>Descritor do PropertyGrid que conhece o parâmetro do template.</summary>
internal interface IParametroDescritor
{
    ParametroTemplate Parametro { get; }
}

/// <summary>Botão "…" que abre a <see cref="FormTabela"/>.</summary>
internal sealed class EditorTabela : UITypeEditor
{
    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) => UITypeEditorEditStyle.Modal;

    public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
    {
        if (!(context?.PropertyDescriptor is IParametroDescritor d)) return value;
        using var f = new FormTabela(d.Parametro, value as string ?? "");
        var svc = provider?.GetService(typeof(IWindowsFormsEditorService)) as IWindowsFormsEditorService;
        var r = svc != null ? svc.ShowDialog(f) : f.ShowDialog();
        return r == DialogResult.OK ? f.Valor : value;
    }
}
