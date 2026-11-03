using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DEB.Revit.Precast.UI
{
    /// <summary>
    /// Pré-visualização dos pilares pré-moldados agrupados por similaridade e escolha da armadura de cada grupo.
    /// Janela modal: ao confirmar, <see cref="Selected"/> traz os grupos marcados para gerar.
    /// </summary>
    internal sealed class PrecastColumnsWindow : Window
    {
        private static readonly double[] Diameters = { 4.2, 5, 6.3, 8, 10, 12.5, 16, 20, 25, 32 };

        private readonly List<ColumnGroup> _groups;
        private readonly HashSet<ColumnGroup> _include;
        private readonly ListBox _list = new ListBox();
        private readonly ColumnPreview _preview = new ColumnPreview();
        private readonly StackPanel _fields = new StackPanel();
        private readonly StackPanel _checks = new StackPanel();
        private readonly TextBox _mark = new TextBox();
        private readonly ComboBox _presets = new ComboBox { MinWidth = 140 };
        private readonly CheckBox _writeMarks = new CheckBox { Content = "Gravar marcas nos pilares", IsChecked = true, Margin = new Thickness(0, 0, 18, 0) };
        private readonly CheckBox _detail = new CheckBox { Content = "Gerar detalhamento (vistas e folhas)", IsChecked = true, Margin = new Thickness(0, 0, 18, 0) };
        private bool _loading;

        public List<ColumnGroup> Selected => _groups.Where(_include.Contains).ToList();
        public PipelineOptions Options => new PipelineOptions { WriteMarks = _writeMarks.IsChecked == true, Detail = _detail.IsChecked == true };

        private ColumnGroup Current => (_list.SelectedItem as ListBoxItem)?.Tag as ColumnGroup;

        public PrecastColumnsWindow(List<ColumnGroup> groups, IEnumerable<string> notes)
        {
            _groups = groups;
            _include = new HashSet<ColumnGroup>(groups);

            Title = "Pilares pré-moldados — armaduras";
            Width = 1500; Height = 900;
            MinWidth = 1100; MinHeight = 650;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 12;

            var root = new Grid { Margin = new Thickness(10) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ---- Esquerda: grupos ----
            var left = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
            var header = new TextBlock
            {
                Text = $"{groups.Sum(g => g.Members.Count)} pilares em {groups.Count} peça(s) distinta(s)",
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6)
            };
            DockPanel.SetDock(header, Dock.Top);
            left.Children.Add(header);
            string noteText = string.Join("\n", notes ?? Enumerable.Empty<string>());
            if (noteText.Length > 0)
            {
                var nt = new TextBlock { Text = noteText, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkGoldenrod, Margin = new Thickness(0, 0, 0, 6) };
                DockPanel.SetDock(nt, Dock.Top);
                left.Children.Add(nt);
            }
            _list.SelectionChanged += (s, e) => LoadCurrent();
            left.Children.Add(_list);
            Grid.SetColumn(left, 0);
            root.Children.Add(left);

            // ---- Centro: desenho + legenda ----
            var center = new DockPanel();
            var legend = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var kv in ColumnPreview.Legend)
            {
                legend.Children.Add(new Border { Width = 18, Height = 3, Background = kv.Value.Brush, Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                legend.Children.Add(new TextBlock { Text = RebarFactory.RoleLabel(kv.Key) });
            }
            DockPanel.SetDock(legend, Dock.Bottom);
            center.Children.Add(legend);
            center.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Child = _preview });
            Grid.SetColumn(center, 1);
            root.Children.Add(center);

            // ---- Direita: marca, modelos, campos, verificações ----
            var right = new DockPanel { Margin = new Thickness(10, 0, 0, 0) };
            var top = new StackPanel();
            top.Children.Add(Row("Marca da peça", _mark));
            _mark.TextChanged += (s, e) =>
            {
                if (_loading || Current == null) return;
                Current.Mark = _mark.Text.Trim();
                RefreshItem(Current);
            };

            var presetRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            presetRow.Children.Add(_presets);
            presetRow.Children.Add(Button("Aplicar modelo", ApplyPreset));
            presetRow.Children.Add(Button("Salvar modelo", SavePreset));
            top.Children.Add(new TextBlock { Text = "Modelos de armadura", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
            top.Children.Add(presetRow);
            top.Children.Add(Button("Copiar esta armadura para todos os grupos", CopyToAll, new Thickness(0, 6, 0, 6)));
            DockPanel.SetDock(top, Dock.Top);
            right.Children.Add(top);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel();
            stack.Children.Add(_fields);
            stack.Children.Add(new TextBlock { Text = "Verificações (NBR 6118 / NBR 9062)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
            stack.Children.Add(_checks);
            scroll.Content = stack;
            right.Children.Add(scroll);
            Grid.SetColumn(right, 2);
            root.Children.Add(right);

            // ---- Rodapé ----
            var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            Button ok = Button("Gerar armaduras e detalhamento", () => { DialogResult = true; }, new Thickness(0, 0, 8, 0));
            ok.IsDefault = true;
            ok.FontWeight = FontWeights.SemiBold;
            buttons.Children.Add(ok);
            Button cancel = Button("Cancelar", () => { DialogResult = false; });
            cancel.IsCancel = true;
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Right);
            bottom.Children.Add(buttons);
            var opts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            opts.Children.Add(_writeMarks);
            opts.Children.Add(_detail);
            bottom.Children.Add(opts);
            Grid.SetRow(bottom, 1);
            Grid.SetColumnSpan(bottom, 3);
            root.Children.Add(bottom);

            Content = root;

            foreach (ColumnGroup g in groups) _list.Items.Add(new ListBoxItem { Tag = g, Content = ItemContent(g) });
            ReloadPresets();
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        // ------------------------------------------------------------------
        // Lista de grupos

        private UIElement ItemContent(ColumnGroup g)
        {
            PrecastColumn r = g.Representative;
            int warnings = g.Spec.Check(r).Count;
            var cb = new CheckBox { IsChecked = _include.Contains(g), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 6, 0) };
            cb.Checked += (s, e) => _include.Add(g);
            cb.Unchecked += (s, e) => _include.Remove(g);

            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = $"{g.Mark}   ×{g.Members.Count}",
                FontWeight = FontWeights.SemiBold, FontSize = 14
            });
            text.Children.Add(new TextBlock
            {
                Text = $"{r.SectionLabel} mm  ·  H = {U_.ToMm(r.Frame.H) / 1000:0.00} m  ·  {r.Corbels.Count} consolo(s)" +
                       (r.EmbedmentMm > 0 ? $"  ·  emb. {r.EmbedmentMm:0}" : "")
            });
            text.Children.Add(new TextBlock
            {
                Text = $"Pilares: {string.Join(", ", g.Members.Select(m => m.Id))}",
                Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, MaxWidth = 270
            });
            text.Children.Add(new TextBlock
            {
                Text = warnings == 0 ? "✓ verificações OK" : $"⚠ {warnings} aviso(s)",
                Foreground = warnings == 0 ? Brushes.SeaGreen : Brushes.DarkOrange
            });

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 4, 2, 4) };
            row.Children.Add(cb);
            row.Children.Add(text);
            return row;
        }

        private void RefreshItem(ColumnGroup g)
        {
            foreach (ListBoxItem it in _list.Items)
                if (it.Tag == g) it.Content = ItemContent(g);
        }

        // ------------------------------------------------------------------
        // Campos da armadura

        private void LoadCurrent()
        {
            ColumnGroup g = Current;
            _preview.Group = g;
            _fields.Children.Clear();
            if (g == null) return;

            _loading = true;
            _mark.Text = g.Mark;
            _loading = false;

            ColumnRebarSpec s = g.Spec;
            Section("Geral");
            Num("Cobrimento (mm)", () => s.CoverMm, v => s.CoverMm = v);

            Section("Longitudinal");
            Dia("Bitola", () => s.LongDiameterMm, v => s.LongDiameterMm = v);
            Int("Barras por face paralela a X (com cantos)", () => s.LongBarsAlongX, v => s.LongBarsAlongX = Math.Max(2, v));
            Int("Barras por face paralela a Y (com cantos)", () => s.LongBarsAlongY, v => s.LongBarsAlongY = Math.Max(2, v));
            Num("Cobrimento nas pontas (mm)", () => s.LongEndCoverMm, v => s.LongEndCoverMm = v);

            Section("Estribos");
            Dia("Bitola", () => s.StirrupDiameterMm, v => s.StirrupDiameterMm = v);
            Num("Espaçamento corrente (mm)", () => s.StirrupSpacingMm, v => s.StirrupSpacingMm = v);
            Num("1º estribo a partir das pontas (mm)", () => s.StirrupEndOffsetMm, v => s.StirrupEndOffsetMm = v);
            Bool("Grampos nas barras intermediárias", () => s.CrossTies, v => s.CrossTies = v);

            Section("Zonas de estribos");
            Num("Topo: comprimento (mm)", () => s.TopZoneLengthMm, v => s.TopZoneLengthMm = v);
            Num("Topo: espaçamento (mm)", () => s.TopZoneSpacingMm, v => s.TopZoneSpacingMm = v);
            Num("Base/embutimento: comprimento (mm)", () => s.BaseZoneLengthMm, v => s.BaseZoneLengthMm = v);
            Num("Base/embutimento: espaçamento (mm)", () => s.BaseZoneSpacingMm, v => s.BaseZoneSpacingMm = v);
            Num("Consolo: extensão acima/abaixo (mm)", () => s.CorbelZoneExtensionMm, v => s.CorbelZoneExtensionMm = v);
            Num("Consolo: espaçamento (mm)", () => s.CorbelZoneSpacingMm, v => s.CorbelZoneSpacingMm = v);

            Section("Consolo (NBR 9062)");
            Bool("Armar consolos", () => s.CorbelRebar, v => s.CorbelRebar = v);
            Dia("Tirante: bitola", () => s.TieDiameterMm, v => s.TieDiameterMm = v);
            Int("Tirante: nº de barras", () => s.TieCount, v => s.TieCount = Math.Max(1, v));
            Num("Tirante: perna de ancoragem no pilar (mm)", () => s.TieInnerLegMm, v => s.TieInnerLegMm = v);
            Dia("Costura: bitola", () => s.SewDiameterMm, v => s.SewDiameterMm = v);
            Int("Costura: nº de estribos horizontais", () => s.SewCount, v => s.SewCount = Math.Max(0, v));

            Section("Içamento / insertos");
            Int("Alças de içamento (0, 1 ou 2)", () => s.LiftLoops, v => s.LiftLoops = Math.Max(0, Math.Min(2, v)));
            Dia("Alça: bitola", () => s.LiftDiameterMm, v => s.LiftDiameterMm = v);
            Num("Alça: comprimento embutido (mm)", () => s.LiftEmbedMm, v => s.LiftEmbedMm = v);
            Num("Alça: saliência acima do topo (mm)", () => s.LiftProtrusionMm, v => s.LiftProtrusionMm = v);
            Num("Alça: largura (mm)", () => s.LiftWidthMm, v => s.LiftWidthMm = v);
            Num("Distância entre alças (mm)", () => s.LiftSpacingMm, v => s.LiftSpacingMm = v);
            Str("Família de inserto no topo (\"Família : Tipo\")", () => s.InsertFamily, v => s.InsertFamily = v);

            Section("Modelo");
            Str("Nome para salvar como modelo", () => s.Name, v => s.Name = v);

            UpdateChecks();
        }

        private void Changed()
        {
            if (_loading) return;
            _preview.Refresh();
            UpdateChecks();
            if (Current != null) RefreshItem(Current);
        }

        private void UpdateChecks()
        {
            _checks.Children.Clear();
            ColumnGroup g = Current;
            if (g == null) return;
            List<string> w = g.Spec.Check(g.Representative);
            if (w.Count == 0)
                _checks.Children.Add(new TextBlock { Text = "✓ Nenhum problema encontrado.", Foreground = Brushes.SeaGreen });
            foreach (string m in w)
                _checks.Children.Add(new TextBlock { Text = "⚠ " + m, Foreground = Brushes.DarkOrange, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 3) });

            ColumnRebarSpec s = g.Spec;
            double rho = s.LongBarCount * Math.PI * s.LongDiameterMm * s.LongDiameterMm / 4 / (U_.ToMm(g.Representative.Frame.Bx) * U_.ToMm(g.Representative.Frame.By)) * 100;
            _checks.Children.Add(new TextBlock
            {
                Text = $"{s.LongBarCount} Ø{RebarFactory.Fmt(s.LongDiameterMm)}  ·  ρ = {rho:0.00}%",
                Foreground = Brushes.DimGray, Margin = new Thickness(0, 6, 0, 0)
            });
        }

        private void Section(string title) =>
            _fields.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });

        private void Num(string label, Func<double> get, Action<double> set)
        {
            var tb = new TextBox { Text = get().ToString("0.##", CultureInfo.CurrentCulture), Width = 80 };
            tb.TextChanged += (s, e) =>
            {
                if (TryParse(tb.Text, out double v) && v >= 0) { set(v); tb.ClearValue(BackgroundProperty); Changed(); }
                else tb.Background = Brushes.MistyRose;
            };
            _fields.Children.Add(Row(label, tb));
        }

        private void Int(string label, Func<int> get, Action<int> set)
        {
            var tb = new TextBox { Text = get().ToString(CultureInfo.CurrentCulture), Width = 80 };
            tb.TextChanged += (s, e) =>
            {
                if (int.TryParse(tb.Text, out int v) && v >= 0) { set(v); tb.ClearValue(BackgroundProperty); Changed(); }
                else tb.Background = Brushes.MistyRose;
            };
            _fields.Children.Add(Row(label, tb));
        }

        private void Dia(string label, Func<double> get, Action<double> set)
        {
            var cb = new ComboBox { Width = 80, IsEditable = true };
            foreach (double d in Diameters) cb.Items.Add(d.ToString("0.#", CultureInfo.CurrentCulture));
            cb.Text = get().ToString("0.#", CultureInfo.CurrentCulture);
            void apply()
            {
                string t = cb.SelectedItem as string ?? cb.Text;
                if (TryParse(t, out double v) && v > 0) { set(v); Changed(); }
            }
            cb.SelectionChanged += (s, e) => apply();
            cb.LostFocus += (s, e) => apply();
            _fields.Children.Add(Row(label + " (mm)", cb));
        }

        private void Bool(string label, Func<bool> get, Action<bool> set)
        {
            var c = new CheckBox { Content = label, IsChecked = get(), Margin = new Thickness(0, 3, 0, 3) };
            c.Checked += (s, e) => { set(true); Changed(); };
            c.Unchecked += (s, e) => { set(false); Changed(); };
            _fields.Children.Add(c);
        }

        private void Str(string label, Func<string> get, Action<string> set)
        {
            var tb = new TextBox { Text = get() ?? "", Width = 150 };
            tb.TextChanged += (s, e) => { set(tb.Text); Changed(); };
            _fields.Children.Add(Row(label, tb));
        }

        private static bool TryParse(string s, out double v) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v) ||
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        private static UIElement Row(string label, FrameworkElement editor)
        {
            var g = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            DockPanel.SetDock(editor, Dock.Right);
            g.Children.Add(editor);
            g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) });
            return g;
        }

        private static Button Button(string text, Action click, Thickness? margin = null)
        {
            var b = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4), Margin = margin ?? new Thickness(6, 0, 0, 0) };
            b.Click += (s, e) => click();
            return b;
        }

        // ------------------------------------------------------------------
        // Modelos

        private void ReloadPresets()
        {
            _presets.Items.Clear();
            foreach (ColumnRebarSpec p in ColumnRebarSpec.LoadPresets()) _presets.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p });
            if (_presets.Items.Count > 0) _presets.SelectedIndex = 0;
        }

        private void ApplyPreset()
        {
            if (Current == null || !((_presets.SelectedItem as ComboBoxItem)?.Tag is ColumnRebarSpec p)) return;
            ColumnRebarSpec copy = p.Clone();
            // O tamanho do embutimento é da peça, não do modelo.
            if (copy.BaseZoneLengthMm <= 0) copy.BaseZoneLengthMm = Current.Spec.BaseZoneLengthMm;
            Current.Spec = copy;
            LoadCurrent();
            Changed();
        }

        private void SavePreset()
        {
            if (Current == null) return;
            if (string.IsNullOrWhiteSpace(Current.Spec.Name) || Current.Spec.Name == "Padrão")
            {
                MessageBox.Show(this, "Preencha \"Nome para salvar como modelo\" (seção Modelo) antes de salvar.", Title);
                return;
            }
            ColumnRebarSpec.SavePreset(Current.Spec);
            ReloadPresets();
            MessageBox.Show(this, $"Modelo \"{Current.Spec.Name}\" salvo em\n{ColumnRebarSpec.PresetsPath}", Title);
        }

        private void CopyToAll()
        {
            if (Current == null) return;
            foreach (ColumnGroup g in _groups.Where(g => g != Current))
            {
                double baseZone = g.Spec.BaseZoneLengthMm;
                g.Spec = Current.Spec.Clone();
                g.Spec.BaseZoneLengthMm = baseZone;
                RefreshItem(g);
            }
        }

        /// <summary>Renderiza o conteúdo da janela (sem exibi-la) em PNG — usado nos testes automáticos.</summary>
        public void RenderToPng(string path, int width = 1500, int height = 860)
        {
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

        // ------------------------------------------------------------------

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (DialogResult == true)
            {
                var marks = Selected.Select(g => g.Mark).ToList();
                if (marks.Any(string.IsNullOrWhiteSpace))
                {
                    MessageBox.Show(this, "Há grupo sem marca.", Title);
                    e.Cancel = true;
                }
                else if (marks.Count != marks.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                {
                    MessageBox.Show(this, "Há marcas repetidas entre grupos diferentes.", Title);
                    e.Cancel = true;
                }
                else if (Selected.Count == 0)
                {
                    MessageBox.Show(this, "Nenhum grupo marcado para gerar.", Title);
                    e.Cancel = true;
                }
            }
            base.OnClosing(e);
        }
    }
}
