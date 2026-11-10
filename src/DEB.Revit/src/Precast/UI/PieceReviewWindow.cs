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
    public sealed class ReviewRow : System.ComponentModel.INotifyPropertyChanged
    {
        internal GrupoPecas Grupo { get; set; }
        private bool _exportar;
        /// <summary>Marcado para ir nesta exportação (caixa da 1ª coluna; não é gravado no modelo).</summary>
        public bool Exportar
        {
            get => _exportar;
            set { if (_exportar == value) return; _exportar = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Exportar))); }
        }
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
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
        /// <summary>Botão "Exportar para o CAD…": as peças dos grupos marcados e quantos grupos ficaram desmarcados.</summary>
        public System.Action<List<PecaLevantada>, int> Export { get; set; }
        /// <summary>Ids das peças selecionadas no Revit quando a janela abriu (vazio = nada selecionado).</summary>
        public HashSet<string> SelecaoRevit { get; set; } = new HashSet<string>();
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
        private readonly List<ReviewRow> _rows = new List<ReviewRow>();
        private readonly TextBlock _contagem = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), FontWeight = FontWeights.SemiBold };

        public PieceReviewWindow(List<GrupoPecas> grupos, List<string> notes, ReviewActions actions = null)
        {
            _grupos = grupos;
            _actions = actions ?? new ReviewActions();
            var export = _actions.Export;
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
                    ToolTip = "Grava o .json dos grupos MARCADOS (1ª coluna) que o CAD desenha, com a marca do modelo como nome. As ✖ ficam de fora, com o motivo.",
                };
                exp.Click += (s, e) =>
                {
                    var marcados = _rows.Where(r => r.Exportar).ToList();
                    if (marcados.Count == 0) { MessageBox.Show(this, "Nenhum grupo marcado para exportar (1ª coluna das abas Vigas e Pilares).", "Exportar para o CAD"); return; }
                    try { export(marcados.SelectMany(r => r.Grupo.Pecas).ToList(), _rows.Count - marcados.Count); }
                    catch (System.Exception ex) { MessageBox.Show(this, "Não foi possível exportar: " + ex.Message, "Exportar para o CAD"); }
                };
                DockPanel.SetDock(exp, Dock.Right);
                bottom.Children.Add(exp);
                DockPanel.SetDock(_contagem, Dock.Right);
                bottom.Children.Add(_contagem);
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
            Contar();
        }

        /// <summary>Marcação inicial: com peças selecionadas no Revit, só os grupos delas; senão todos os que o CAD desenha e não estão "nunca exportar".</summary>
        private bool MarcadoNoInicio(GrupoPecas g) =>
            !g.Pecas.All(p => p.NaoExportar) && g.Situacao != SituacaoPeca.NaoSuportada &&
            (_actions.SelecaoRevit.Count == 0 || g.Pecas.Any(p => _actions.SelecaoRevit.Contains(p.Id)));

        private void Contar()
        {
            int m = _rows.Count(r => r.Exportar);
            _contagem.Text = $"{m} de {_rows.Count} grupo(s) marcados ({_rows.Where(r => r.Exportar).Sum(r => r.Grupo.Quantidade)} peça(s))" +
                             (_actions.SelecaoRevit.Count > 0 ? " — começou pela seleção do Revit" : "");
        }

        private void AddPage(string name, List<GrupoPecas> grupos)
        {
            var list = new ListView { Width = 630 };
            var gv = new GridView();
            // 1ª coluna: caixa "exportar" (só desta exportação; o "nunca exportar" gravado no modelo fica embaixo, à direita)
            var caixa = new FrameworkElementFactory(typeof(CheckBox));
            caixa.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                new System.Windows.Data.Binding(nameof(ReviewRow.Exportar)) { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
            caixa.SetValue(FrameworkElement.ToolTipProperty, "Exportar este grupo agora");
            gv.Columns.Add(new GridViewColumn { Header = "⇪", Width = 30, CellTemplate = new DataTemplate { VisualTree = caixa } });
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
            var rows = grupos.Select(g => Fill(new ReviewRow
            {
                Grupo = g, Exportar = MarcadoNoInicio(g),
                Qtd = g.Quantidade, Tipo = g.Representante.FamiliaTipo,
                Secao = g.Representante.Principal?.Secao.Classificar().Descricao ?? "—",
                L = g.Representante.Comprimento.ToString("0.#", CultureInfo.GetCultureInfo("pt-BR")),
                Trechos = g.Representante.Trechos.Count,
            })).ToList();
            foreach (var r in rows) r.PropertyChanged += (s, e) => Contar();
            _rows.AddRange(rows);
            list.ItemsSource = rows;

            // marcar em conjunto: todos, nenhum, só os que o CAD desenha sem atenção, só a seleção do Revit
            var barra = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            Button Botao(string t, string dica, System.Func<ReviewRow, bool> marcar)
            {
                var b = new Button { Content = t, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 1, 8, 1), ToolTip = dica };
                b.Click += (s, e) => { foreach (var r in rows) r.Exportar = marcar(r); };
                barra.Children.Add(b);
                return b;
            }
            Botao("Todos", "Marca todos os grupos desta aba", r => true);
            Botao("Nenhum", "Desmarca todos os grupos desta aba (exportar só a outra aba)", r => false);
            Botao("Só ✔", "Só os grupos sem nenhum aviso", r => r.Grupo.Situacao == SituacaoPeca.Ok && !r.Grupo.Pecas.All(p => p.NaoExportar));
            var bSel = Botao("Seleção do Revit", "Só os grupos com peças que estavam selecionadas no Revit ao abrir a janela",
                r => r.Grupo.Pecas.Any(p => _actions.SelecaoRevit.Contains(p.Id)));
            bSel.IsEnabled = _actions.SelecaoRevit.Count > 0;
            var esquerda = new DockPanel { Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(barra, Dock.Top);
            esquerda.Children.Add(barra);
            esquerda.Children.Add(list);

            var preview = new PieceReviewPreview();
            var avisos = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), MinHeight = 40 };
            // ---- edições do grupo escolhido: marca (gravada no modelo) e "não exportar" ----
            var markBox = new TextBox { Width = 110, VerticalContentAlignment = VerticalAlignment.Center };
            var markBtn = new Button { Content = "Gravar marca no modelo", Margin = new Thickness(6, 0, 14, 0), Padding = new Thickness(8, 1, 8, 1) };
            var skipBox = new CheckBox { Content = "Nunca exportar este grupo (gravado no modelo)", VerticalAlignment = VerticalAlignment.Center };
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
                if (v) row.Exportar = false;
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
            DockPanel.SetDock(esquerda, Dock.Left);
            grid.Children.Add(esquerda);
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
