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

    /// <summary>
    /// "Conferir peças" (2026-11-09, etapa 2 — só leitura): abas Vigas e Pilares com os grupos de peças iguais pela geometria,
    /// o tipo do Revit, a seção medida, a situação e os avisos; à direita a elevação com os trechos e as seções cotadas.
    /// As edições (marca gravada no Revit, função dos trechos, aceitar/ignorar, exportar) são a etapa 3.
    /// </summary>
    internal sealed class PieceReviewWindow : Window
    {
        private readonly TabControl _tabs = new TabControl();
        private readonly List<(ListView List, PieceReviewPreview Preview, TextBlock Avisos)> _pages = new List<(ListView, PieceReviewPreview, TextBlock)>();

        public PieceReviewWindow(List<GrupoPecas> grupos, List<string> notes)
        {
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
            int ok = grupos.Count(g => g.Situacao == SituacaoPeca.Ok), at = grupos.Count(g => g.Situacao == SituacaoPeca.Atencao),
                ns = grupos.Count(g => g.Situacao == SituacaoPeca.NaoSuportada);
            string resumo = $"{grupos.Sum(g => g.Quantidade)} peça(s) em {grupos.Count} grupo(s): {ok} ok, {at} com atenção, {ns} com seção que o CAD ainda não desenha. " +
                            "A geometria define a peça; o nome do tipo só é conferido. Só leitura nesta versão.";
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
            list.ItemsSource = grupos.Select(g => new ReviewRow
            {
                Grupo = g,
                Situacao = g.Situacao == SituacaoPeca.Ok ? "✔" : g.Situacao == SituacaoPeca.Atencao ? "⚠" : "✖",
                Marcas = g.Marcas, Qtd = g.Quantidade, Tipo = g.Representante.FamiliaTipo,
                Secao = g.Representante.Principal?.Secao.Classificar().Descricao ?? "—",
                L = g.Representante.Comprimento.ToString("0.#", CultureInfo.GetCultureInfo("pt-BR")),
                Trechos = g.Representante.Trechos.Count,
            }).ToList();

            var preview = new PieceReviewPreview();
            var avisos = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), MinHeight = 40 };
            list.SelectionChanged += (s, e) =>
            {
                var g = (list.SelectedItem as ReviewRow)?.Grupo;
                preview.Group = g;
                avisos.Text = g == null ? "" : g.Avisos.Count == 0 ? "Sem avisos." : "Avisos:\n• " + string.Join("\n• ", g.Avisos);
                avisos.Foreground = g?.Situacao == SituacaoPeca.Ok ? Brushes.DarkGreen : Brushes.DarkRed;
            };

            var right = new DockPanel();
            DockPanel.SetDock(avisos, Dock.Bottom);
            right.Children.Add(avisos);
            right.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Child = preview });
            var grid = new DockPanel { Margin = new Thickness(4) };
            DockPanel.SetDock(list, Dock.Left);
            grid.Children.Add(list);
            grid.Children.Add(right);
            _tabs.Items.Add(new TabItem { Header = $"{name} ({grupos.Sum(g => g.Quantidade)})", Content = grid });
            _pages.Add((list, preview, avisos));
            if (grupos.Count > 0) list.SelectedIndex = 0;
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
