using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DEB.Core.Pecas;

namespace DEB.Revit.Precast.UI
{
    /// <summary>Linha da lista de grupos (pública: o WPF só faz binding em tipos públicos).</summary>
    public sealed class ReviewRow
    {
        internal GrupoPecas Grupo { get; set; }
        public string Situacao { get; set; } = "";
        public string Marcas { get; set; } = "";
        public int Qtd { get; set; }
        public string Tipo { get; set; } = "";
        public string Secao { get; set; } = "";
        public string L { get; set; } = "";
        public int Trechos { get; set; }
    }

    /// <summary>O que a janela pode fazer no modelo (null = sem aquele controle; o AutoRun de leitura não passa nada).</summary>
    internal sealed class ReviewActions
    {
        /// <summary>Botão "Exportar para o CAD…".</summary>
        public System.Action Export { get; set; }
        /// <summary>Grava a marca nas peças do grupo; devolve a mensagem de erro ou null.</summary>
        public System.Func<GrupoPecas, string, string> SetMark { get; set; }
        /// <summary>Grava "não exportar" nas peças do grupo; devolve a mensagem de erro ou null.</summary>
        public System.Func<GrupoPecas, bool, string> SetSkip { get; set; }
    }

    /// <summary>
    /// "Conferir peças" (2026-11-09): abas Vigas e Pilares com os grupos de peças iguais pela geometria, o tipo do Revit, a
    /// seção medida, a situação e os avisos; à direita a elevação com os trechos e as seções cotadas. Etapa 3: a marca do grupo
    /// é editada aqui e gravada no modelo ("a marca é o nome da peça"), o grupo pode ser tirado da exportação e o botão
    /// "Exportar para o CAD…" grava o que está na janela. A função dos trechos ainda não é editada.
    /// </summary>
    internal sealed class PieceReviewWindow : Window
    {
        private readonly TabControl _tabs = new TabControl();
        private readonly List<(ListView List, PieceReviewPreview Preview, TextBlock Avisos)> _pages = new List<(ListView, PieceReviewPreview, TextBlock)>();
        private readonly List<GrupoPecas> _grupos;
        private readonly ReviewActions _actions;

        public PieceReviewWindow(List<GrupoPecas> grupos, List<string> notes, ReviewActions actions = null)
        {
            _grupos = grupos;
            _actions = actions ?? new ReviewActions();
            System.Action export = _actions.Export;
            Title = "Conferir peças — Detalhador Estrutural Brasileiro";
            Width = 1450; Height = 880;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI");

            var root = new DockPanel { Margin = new Thickness(8) };
            var bottom = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var close = new Button { Content = "Fechar", Width = 90, IsCancel = true, IsDefault = true };
            close.Click += (s, e) => Close();
            DockPanel.SetDock(close, Dock.Right);
            bottom.Children.Add(close);
            if (export != null)
            {
                var exp = new Button
                {
                    Content = "Exportar para o CAD…", Width = 150, Margin = new Thickness(0, 0, 8, 0),
                    ToolTip = "Grava o .json das peças que o CAD desenha (✔ e ⚠), com a marca do modelo como nome. As ✖ ficam de fora, com o motivo.",
                };
                exp.Click += (s, e) =>
                {
                    try { export(); }
                    catch (System.Exception ex) { MessageBox.Show(this, "Não foi possível exportar: " + ex.Message, "Exportar para o CAD"); }
                };
                DockPanel.SetDock(exp, Dock.Right);
                bottom.Children.Add(exp);
            }
            int ok = grupos.Count(g => g.Situacao == SituacaoPeca.Ok), at = grupos.Count(g => g.Situacao == SituacaoPeca.Atencao),
                ns = grupos.Count(g => g.Situacao == SituacaoPeca.NaoSuportada);
            string resumo = $"{grupos.Sum(g => g.Quantidade)} peça(s) em {grupos.Count} grupo(s): {ok} ok, {at} com atenção, {ns} com seção que o CAD ainda não desenha. " +
                            "A geometria define a peça; o nome do tipo só é conferido. A marca do modelo é o nome do detalhamento no CAD.";
            bottom.Children.Add(new TextBlock
            {
                Text = resumo + (notes.Count > 0 ? "\nNotas: " + string.Join(" · ", notes.Take(6)) + (notes.Count > 6 ? " …" : "") : ""),
                TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
            });
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);
            root.Children.Add(_tabs);
            Content = root;

            AddPage("Vigas", grupos.Where(g => g.Representante.Tipo == TipoPeca.Viga).ToList());
            AddPage("Pilares", grupos.Where(g => g.Representante.Tipo == TipoPeca.Pilar).ToList());
        }

        private void AddPage(string name, List<GrupoPecas> grupos)
        {
            var list = new ListView { Width = 600, Margin = new Thickness(0, 0, 8, 0) };
            var gv = new GridView();
            void Col(string h, string path, double w) => gv.Columns.Add(new GridViewColumn
            {
                Header = h, Width = w, DisplayMemberBinding = new System.Windows.Data.Binding(path),
            });
            Col("", nameof(ReviewRow.Situacao), 28);
            Col("Marca(s)", nameof(ReviewRow.Marcas), 80);
            Col("Qtd", nameof(ReviewRow.Qtd), 36);
            Col("Família: tipo (Revit)", nameof(ReviewRow.Tipo), 190);
            Col("Seção medida (trecho corrente)", nameof(ReviewRow.Secao), 150);
            Col("L (cm)", nameof(ReviewRow.L), 56);
            Col("Trechos", nameof(ReviewRow.Trechos), 52);
            list.View = gv;
            list.ItemsSource = grupos.Select(g => Fill(new ReviewRow
            {
                Grupo = g,
                Qtd = g.Quantidade, Tipo = g.Representante.FamiliaTipo,
                Secao = g.Representante.Principal?.Secao.Classificar().Descricao ?? "—",
                L = g.Representante.Comprimento.ToString("0.#", CultureInfo.GetCultureInfo("pt-BR")),
                Trechos = g.Representante.Trechos.Count,
            })).ToList();

            var preview = new PieceReviewPreview();
            var avisos = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), MinHeight = 40 };
            // ---- edições do grupo escolhido: marca (gravada no modelo) e "não exportar" ----
            var markBox = new TextBox { Width = 110, VerticalContentAlignment = VerticalAlignment.Center };
            var markBtn = new Button { Content = "Gravar marca no modelo", Margin = new Thickness(6, 0, 14, 0), Padding = new Thickness(8, 1, 8, 1) };
            var skipBox = new CheckBox { Content = "Não exportar este grupo", VerticalAlignment = VerticalAlignment.Center };
            var editMsg = new TextBlock { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DarkRed, TextWrapping = TextWrapping.Wrap };
            var edit = new DockPanel { Margin = new Thickness(0, 6, 0, 0), LastChildFill = true };
            foreach (UIElement c in new UIElement[] { new TextBlock { Text = "Marca do grupo: ", VerticalAlignment = VerticalAlignment.Center }, markBox, markBtn, skipBox })
            { DockPanel.SetDock(c, Dock.Left); edit.Children.Add(c); }
            edit.Children.Add(editMsg);
            markBox.IsEnabled = markBtn.IsEnabled = _actions.SetMark != null;
            skipBox.IsEnabled = _actions.SetSkip != null;
            bool loading = false;
            void Refresh(ReviewRow row)
            {
                Fill(row);
                list.Items.Refresh();
            }
            markBtn.Click += (s, e) =>
            {
                if (!(list.SelectedItem is ReviewRow row)) return;
                string m = markBox.Text.Trim();
                // a mesma marca em peças DIFERENTES não pode: o detalhamento do CAD leva o nome da marca
                var outro = _grupos.FirstOrDefault(o => o != row.Grupo && m.Length > 0 &&
                    o.Pecas.Any(p => string.Equals((p.Marca ?? "").Trim(), m, System.StringComparison.OrdinalIgnoreCase)));
                if (outro != null) { editMsg.Text = $"A marca {m} já é de outro grupo ({outro.Representante.FamiliaTipo}, L = {outro.Representante.Comprimento:0.#}): peças diferentes não podem ter a mesma marca."; return; }
                try { editMsg.Text = _actions.SetMark(row.Grupo, m) ?? ""; }
                catch (System.Exception ex) { editMsg.Text = "Marca não gravada: " + ex.Message; }
                Refresh(row);
            };
            markBox.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) { markBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; } };
            void Skip(bool v)
            {
                if (loading || !(list.SelectedItem is ReviewRow row)) return;
                try { editMsg.Text = _actions.SetSkip(row.Grupo, v) ?? ""; }
                catch (System.Exception ex) { editMsg.Text = "Não gravado: " + ex.Message; }
                Refresh(row);
            }
            skipBox.Checked += (s, e) => Skip(true);
            skipBox.Unchecked += (s, e) => Skip(false);

            list.SelectionChanged += (s, e) =>
            {
                var g = (list.SelectedItem as ReviewRow)?.Grupo;
                preview.Group = g;
                loading = true;
                var marcas = g?.Pecas.Select(p => (p.Marca ?? "").Trim()).Where(m => m.Length > 0).Distinct().ToList();
                markBox.Text = marcas != null && marcas.Count > 0 ? marcas[0] : "";
                skipBox.IsChecked = g != null && g.Pecas.All(p => p.NaoExportar);
                editMsg.Text = "";
                loading = false;
                avisos.Text = g == null ? "" : g.Avisos.Count == 0 ? "Sem avisos." : "Avisos:\n• " + string.Join("\n• ", g.Avisos);
                avisos.Foreground = g?.Situacao == SituacaoPeca.Ok ? Brushes.DarkGreen : Brushes.DarkRed;
            };

            var right = new DockPanel();
            DockPanel.SetDock(avisos, Dock.Bottom);
            right.Children.Add(avisos);
            DockPanel.SetDock(edit, Dock.Bottom);
            right.Children.Add(edit);
            right.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Child = preview });
            var grid = new DockPanel { Margin = new Thickness(4) };
            DockPanel.SetDock(list, Dock.Left);
            grid.Children.Add(list);
            grid.Children.Add(right);
            _tabs.Items.Add(new TabItem { Header = $"{name} ({grupos.Sum(g => g.Quantidade)})", Content = grid });
            _pages.Add((list, preview, avisos));
            if (grupos.Count > 0) list.SelectedIndex = 0;
        }

        /// <summary>O que muda com as edições: situação (⊘ = não exportar) e marcas.</summary>
        private static ReviewRow Fill(ReviewRow row)
        {
            GrupoPecas g = row.Grupo;
            row.Situacao = g.Pecas.All(p => p.NaoExportar) ? "⊘" : g.Situacao == SituacaoPeca.Ok ? "✔" : g.Situacao == SituacaoPeca.Atencao ? "⚠" : "✖";
            row.Marcas = g.Marcas;
            return row;
        }

        /// <summary>Para o AutoRun: escolhe a aba e o grupo e grava a janela em PNG.</summary>
        public void RenderToPng(string path, int tab, int index, int width = 1450, int height = 880)
        {
            _tabs.SelectedIndex = tab;
            var page = _pages[tab];
            if (index < page.List.Items.Count) page.List.SelectedIndex = index;
            var root = (FrameworkElement)Content;
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var bg = new DrawingVisual();
            using (DrawingContext dc = bg.RenderOpen()) dc.DrawRectangle(SystemColors.ControlBrush, null, new Rect(0, 0, width, height));
            bmp.Render(bg);
            bmp.Render(root);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using (var fs = System.IO.File.Create(path)) enc.Save(fs);
        }

        public int Count(int tab) => _pages[tab].List.Items.Count;
    }
}
