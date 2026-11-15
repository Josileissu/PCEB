using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DEB.Core.Lajes;
using DEB.Revit.Tools;

namespace DEB.Revit.LajeAlveolar
{
    /// <summary>
    /// Prévia da paginação: planta desenhada (painéis por tipo, faixas maciças, recortes, juntas, tirantes, consolos a colocar) e o
    /// relatório com os avisos. "Inserir no modelo" confirma. Também serve para o relatório final (só leitura).
    /// </summary>
    internal static class PreviaPaginacaoWindow
    {
        private static readonly Dictionary<string, Color> Cores = new Dictionary<string, Color>
        {
            ["padrao"] = Color.FromRgb(0xFF, 0xF3, 0xC4), ["ajuste"] = Color.FromRgb(0xC8, 0xE6, 0xFF),
            ["abertura"] = Color.FromRgb(0xFF, 0xD0, 0xD0), ["abertura+ajuste"] = Color.FromRgb(0xFF, 0xB0, 0xB0),
        };

        /// <summary>true = o usuário confirmou a inserção.</summary>
        public static bool Mostrar(IList<PanoPaginado> panos, ConfigPaginacao cfg, string relatorio, IList<string> avisos)
        {
            var w = ToolDialogs.NewWindow("Paginar laje alveolar — prévia", 1250);
            w.SizeToContent = SizeToContent.Manual; w.Height = 820; w.ResizeMode = ResizeMode.CanResize;

            var texto = new StringBuilder();
            texto.AppendLine($"AVISOS ({avisos.Count})");
            foreach (var a in avisos) texto.AppendLine("• " + a);
            texto.AppendLine();
            texto.Append(relatorio);
            var tb = new TextBox
            {
                Text = texto.ToString(), IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 11,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap,
            };

            var grade = new Grid();
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            var desenho = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = Brushes.White, Child = Desenhar(panos, cfg) };
            grade.Children.Add(desenho);
            var sp = new GridSplitter { HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetColumn(sp, 1); grade.Children.Add(sp);
            Grid.SetColumn(tb, 2); grade.Children.Add(tb);

            var legenda = new TextBlock
            {
                Text = "amarelo = painel inteiro · azul = ajuste · vermelho = corte na abertura · laranja = com entalhe · cinza = faixa maciça · " +
                       "branco tracejado = recorte · roxo = junta · ponto laranja = tirante · magenta = consolo a colocar · (!) = ponta com alerta",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 6, 0, 0),
            };
            var bts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var salvar = new Button { Content = "Salvar relatório…", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };
            salvar.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Texto (*.txt)|*.txt", FileName = "paginacao_laje_alveolar.txt" };
                if (dlg.ShowDialog(w) == true) try { File.WriteAllText(dlg.FileName, tb.Text, Encoding.UTF8); } catch (Exception ex) { MessageBox.Show(w, ex.Message, w.Title); }
            };
            bts.Children.Add(salvar);
            var ok = ToolDialogs.Buttons(w, "Inserir no modelo");
            ok.Margin = new Thickness(0);
            bts.Children.Add(ok);

            var raiz = new DockPanel { Margin = new Thickness(10) };
            var baixo = new StackPanel(); baixo.Children.Add(legenda); baixo.Children.Add(bts);
            DockPanel.SetDock(baixo, Dock.Bottom); raiz.Children.Add(baixo);
            raiz.Children.Add(grade);
            w.Content = raiz;
            return ToolDialogs.ShowModal(w);
        }

        /// <summary>Relatório final (texto), só leitura.</summary>
        public static void Relatorio(string titulo, string texto)
        {
            var w = ToolDialogs.NewWindow(titulo, 900);
            w.SizeToContent = SizeToContent.Manual; w.Height = 650; w.ResizeMode = ResizeMode.CanResize;
            var tb = new TextBox { Text = texto, IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 11, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
            var fechar = new Button { Content = "Fechar", IsCancel = true, IsDefault = true, Padding = new Thickness(14, 3, 14, 3), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            fechar.Click += (s, e) => w.DialogResult = true;
            var raiz = new DockPanel { Margin = new Thickness(10) };
            DockPanel.SetDock(fechar, Dock.Bottom); raiz.Children.Add(fechar); raiz.Children.Add(tb);
            w.Content = raiz;
            ToolDialogs.ShowModal(w);
        }

        private static UIElement Desenhar(IList<PanoPaginado> panos, ConfigPaginacao cfg)
        {
            var ok = panos.Where(p => p.Erro == null && p.Piso.Loops.Count > 0).ToList();
            if (ok.Count == 0) return new TextBlock { Text = "nada a desenhar", Margin = new Thickness(10) };
            var todos = ok.SelectMany(p => p.Piso.Loops[0]).ToList();
            double x0 = todos.Min(p => p.X) - 0.5, x1 = todos.Max(p => p.X) + 0.5, y0 = todos.Min(p => p.Y) - 0.5, y1 = todos.Max(p => p.Y) + 0.5;
            const double Esc = 40;   // px por metro (o Viewbox ajusta à janela)
            var cv = new Canvas { Width = (x1 - x0) * Esc, Height = (y1 - y0) * Esc, Background = Brushes.White };
            Point Pt(double x, double y) => new Point((x - x0) * Esc, (y1 - y) * Esc);
            Polygon Poli(IEnumerable<(double X, double Y)> pts, Brush fill, Brush stroke, double esp, DoubleCollection traco = null)
            {
                var pg = new Polygon { Fill = fill, Stroke = stroke, StrokeThickness = esp, StrokeDashArray = traco };
                foreach (var (x, y) in pts) pg.Points.Add(Pt(x, y));
                cv.Children.Add(pg);
                return pg;
            }
            void Reta((double X, double Y) a, (double X, double Y) b, Brush c, double esp)
            {
                var pa = Pt(a.X, a.Y); var pb = Pt(b.X, b.Y);
                cv.Children.Add(new Line { X1 = pa.X, Y1 = pa.Y, X2 = pb.X, Y2 = pb.Y, Stroke = c, StrokeThickness = esp });
            }
            void Texto(string t, (double X, double Y) p, double tam, Brush c, double ang = 0)
            {
                var tbk = new TextBlock { Text = t, FontSize = tam, Foreground = c };
                tbk.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var q = Pt(p.X, p.Y);
                tbk.RenderTransform = new RotateTransform(ang, tbk.DesiredSize.Width / 2, tbk.DesiredSize.Height / 2);
                Canvas.SetLeft(tbk, q.X - tbk.DesiredSize.Width / 2); Canvas.SetTop(tbk, q.Y - tbk.DesiredSize.Height / 2);
                cv.Children.Add(tbk);
            }
            var tracejado = new DoubleCollection { 4, 3 };
            foreach (var p in ok)
            {
                var S = p.Sistema;
                double ang = -Math.Atan2(S.DuY, S.DuX) * 180 / Math.PI;
                for (int k = 0; k < p.Piso.Loops.Count; k++) Poli(p.Piso.Loops[k], null, new SolidColorBrush(Color.FromRgb(0x22, 0xAA, 0x77)), 1.2, k == 0 ? null : tracejado);
                foreach (var m in p.Macicas)
                {
                    Poli(m.Cantos, new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)), Brushes.Black, 1);
                    var c = S.XY((m.Faixa.Ret.U0 + m.Faixa.Ret.U1) / 2, (m.Faixa.Ret.V0 + m.Faixa.Ret.V1) / 2);
                    Texto(m.Marca, c, 9, Brushes.Black, ang);
                }
                foreach (var s in p.Pecas)
                {
                    string baseT = s.Tipo.Split(new[] { "+entalhe", "+peca" }, StringSplitOptions.None)[0];
                    var cor = s.Tipo.Contains("+entalhe") ? Color.FromRgb(0xFF, 0xE0, 0xC0) : Cores.TryGetValue(baseT, out var cc) ? cc : Colors.WhiteSmoke;
                    Poli(s.Cantos, new SolidColorBrush(cor), Brushes.DimGray, 0.8);
                    foreach (var r in s.Peca.Recortes)
                        Poli(S.Cantos(r.Ret), Brushes.White, r.Tipo == "abertura" ? Brushes.Red : Brushes.RoyalBlue, 1, tracejado);
                    bool alerta = !string.IsNullOrEmpty(s.Peca.Ini?.Alerta) || !string.IsNullOrEmpty(s.Peca.Fim?.Alerta);
                    var c = S.XY((s.Peca.U0 + s.Peca.U1) / 2, (s.Peca.V0 + s.Peca.V1) / 2);
                    Texto($"{s.Marca}  b={DEB.Core.Fmt.Virgula(s.Peca.Largura * 100, "0.0")}  L={DEB.Core.Fmt.Virgula(s.Peca.Comprimento, "0.000")}" + (alerta ? "  (!)" : ""),
                          c, 9, alerta ? Brushes.DarkRed : Brushes.Black, ang);
                }
                foreach (var j in p.Juntas) Reta(j.Ini, j.Fim, new SolidColorBrush(Color.FromRgb(0x7B, 0x2C, 0xBF)), 1.4);
                foreach (var t in p.Tirantes)
                {
                    var q = Pt(t.X, t.Y);
                    var e = new Ellipse { Width = 5, Height = 5, Fill = Brushes.DarkOrange };
                    Canvas.SetLeft(e, q.X - 2.5); Canvas.SetTop(e, q.Y - 2.5); cv.Children.Add(e);
                }
                foreach (var c in p.Consolos) Reta(c.Ini, c.Fim, Brushes.Magenta, 4);
                var l0 = p.Piso.Loops[0];
                Texto($"pano {p.Numero} (piso {p.Piso.Id})", (l0.Min(q => q.X) + 1.2, l0.Max(q => q.Y) - 0.2), 11, Brushes.DarkGreen);
            }
            var vb = new Viewbox { Child = cv, Stretch = Stretch.Uniform };
            return new ScrollViewer { Content = vb, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }
    }
}
