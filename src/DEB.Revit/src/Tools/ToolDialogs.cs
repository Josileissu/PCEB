using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DEB.Revit.Tools
{
    /// <summary>
    /// Janelas simples das ferramentas (WPF em código, como o resto do plugin): um formulário de campos e uma
    /// lista de marcar. Substituem os InputBox em sequência e os formulários WinForms dos scripts originais.
    /// </summary>
    internal static class ToolDialogs
    {
        /// <summary>Janela principal do Revit (dona das janelas modais).</summary>
        public static IntPtr Owner { get; set; }

        public static bool ShowModal(Window w)
        {
            if (Owner != IntPtr.Zero) new WindowInteropHelper(w) { Owner = Owner };
            return w.ShowDialog() == true;
        }

        public static Window NewWindow(string title, double width) => new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            FontFamily = new FontFamily("Segoe UI")
        };

        public static StackPanel Buttons(Window w, string okText, Func<bool> canClose = null)
        {
            var ok = new Button { Content = okText, IsDefault = true, MinWidth = 110, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 90, Padding = new Thickness(10, 3, 10, 3) };
            ok.Click += (s, e) => { if (canClose?.Invoke() ?? true) w.DialogResult = true; };
            var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            p.Children.Add(ok);
            p.Children.Add(cancel);
            return p;
        }
    }

    /// <summary>Formulário de campos (texto, lista, caixa de marcar) com validação no OK.</summary>
    internal sealed class FieldForm
    {
        private readonly Window _w;
        private readonly StackPanel _body = new StackPanel { Margin = new Thickness(14) };
        private readonly Dictionary<string, Control> _fields = new Dictionary<string, Control>();
        private Func<FieldForm, string> _validate;

        public FieldForm(string title, string intro = null, double width = 380)
        {
            _w = ToolDialogs.NewWindow(title, width);
            if (!string.IsNullOrEmpty(intro))
                _body.Children.Add(new TextBlock { Text = intro, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        }

        public FieldForm Text(string key, string label, string value)
        {
            var tb = new TextBox { Text = value ?? "", Padding = new Thickness(3) };
            return Add(key, label, tb);
        }

        public FieldForm Choice(string key, string label, IEnumerable<string> items, string selected)
        {
            var cb = new ComboBox { ItemsSource = items.ToList(), IsEditable = false };
            cb.SelectedItem = selected != null && cb.Items.Contains(selected) ? selected : cb.Items.Count > 0 ? cb.Items[0] : null;
            return Add(key, label, cb);
        }

        public FieldForm Check(string key, string label, bool value)
        {
            var c = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 2, 0, 2) };
            _fields[key] = c;
            _body.Children.Add(c);
            return this;
        }

        public FieldForm Validate(Func<FieldForm, string> validate) { _validate = validate; return this; }

        private FieldForm Add(string key, string label, Control c)
        {
            _fields[key] = c;
            _body.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 2) });
            _body.Children.Add(c);
            return this;
        }

        public string Get(string key) => _fields[key] switch
        {
            TextBox t => t.Text.Trim(),
            ComboBox c => c.SelectedItem as string,
            _ => null
        };

        public bool IsChecked(string key) => (_fields[key] as CheckBox)?.IsChecked == true;

        public bool Show(string okText = "OK")
        {
            _body.Children.Add(ToolDialogs.Buttons(_w, okText, () =>
            {
                string erro = _validate?.Invoke(this);
                if (erro == null) return true;
                MessageBox.Show(_w, erro, _w.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }));
            _w.Content = _body;
            _w.Loaded += (s, e) => _fields.Values.OfType<TextBox>().FirstOrDefault()?.Focus();
            return ToolDialogs.ShowModal(_w);
        }
    }

    /// <summary>
    /// Lista para marcar itens (as janelas "Selecionar fundações/montagens" dos scripts): clique marca/desmarca,
    /// Shift+clique marca a faixa, Espaço alterna; botões Todos, Nenhum, Inverter e filtro por texto.
    /// </summary>
    internal sealed class CheckListWindow<T>
    {
        private readonly List<(T Item, CheckBox Box)> _rows = new List<(T, CheckBox)>();
        private readonly Window _w;
        private readonly TextBlock _count = new TextBlock { FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 0) };
        private int _last = -1;

        public CheckListWindow(string title, IEnumerable<(T Item, string Text)> items, string itemNoun)
        {
            _w = ToolDialogs.NewWindow(title, 640);
            _w.SizeToContent = SizeToContent.Manual;
            _w.Height = 600;
            _w.ResizeMode = ResizeMode.CanResize;

            var list = new StackPanel();
            foreach (var (item, text) in items)
            {
                var box = new CheckBox { Content = text, Margin = new Thickness(2), Focusable = true };
                int index = _rows.Count;
                box.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    bool target = box.IsChecked != true;
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _last >= 0)
                        for (int i = Math.Min(_last, index); i <= Math.Max(_last, index); i++) _rows[i].Box.IsChecked = target;
                    else box.IsChecked = target;
                    _last = index;
                    Update(itemNoun);
                    e.Handled = true;
                };
                box.Checked += (s, e) => Update(itemNoun);
                box.Unchecked += (s, e) => Update(itemNoun);
                _rows.Add((item, box));
                list.Children.Add(box);
            }

            var filter = new TextBox { Width = 160, Margin = new Thickness(0, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };
            Button B(string t, Action a) { var b = new Button { Content = t, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) }; b.Click += (s, e) => { a(); Update(itemNoun); }; return b; }
            var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            tools.Children.Add(B("Todos", () => _rows.ForEach(r => r.Box.IsChecked = true)));
            tools.Children.Add(B("Nenhum", () => _rows.ForEach(r => r.Box.IsChecked = false)));
            tools.Children.Add(B("Inverter", () => _rows.ForEach(r => r.Box.IsChecked = r.Box.IsChecked != true)));
            tools.Children.Add(new TextBlock { Text = "Marcar os que contêm:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
            tools.Children.Add(filter);
            tools.Children.Add(B("Aplicar", () =>
            {
                string f = filter.Text.Trim();
                if (f.Length > 0) _rows.ForEach(r => r.Box.IsChecked = r.Box.Content.ToString().IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);
            }));

            var root = new DockPanel { Margin = new Thickness(12) };
            var top = new StackPanel();
            top.Children.Add(tools);
            top.Children.Add(new TextBlock { Text = "Dica: Shift+clique marca em lote; Espaço marca/desmarca.", Foreground = Brushes.DimGray });
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);
            var bottom = new StackPanel();
            bottom.Children.Add(_count);
            bottom.Children.Add(ToolDialogs.Buttons(_w, "Copiar marcados"));
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);
            root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 0) });
            _w.Content = root;
            Update(itemNoun);
        }

        private void Update(string noun) =>
            _count.Text = $"Total: {_rows.Count} {noun} | Marcados: {_rows.Count(r => r.Box.IsChecked == true)}";

        /// <summary>Itens marcados, ou lista vazia se cancelado.</summary>
        public List<T> Show() =>
            ToolDialogs.ShowModal(_w) ? _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Item).ToList() : new List<T>();
    }
}
