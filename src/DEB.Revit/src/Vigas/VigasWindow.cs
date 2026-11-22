using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D;
using DEB.Core.Elementos.Viga.PreMoldada.Calculo;
using DEB.Core.Normas.Verificacao;
using DEB.Revit.Core;
using DEB.Revit.Precast;
using WColor = System.Windows.Media.Color;
using WPoint = System.Windows.Point;

namespace DEB.Revit.Vigas
{
    /// <summary>
    /// Janela "Vigas pré-moldadas" (D40): grupos de vigas iguais, cargas digitadas por grupo, cálculo no núcleo e a armadura
    /// proposta com a prévia (elevação, seção no meio do vão e diagramas) — a prévia sai do MESMO modelo de barras que vai para o
    /// Revit (D26). Ao confirmar, <see cref="Selecionados"/> traz os grupos marcados para armar.
    /// </summary>
    internal sealed class VigasWindow : Window
    {
        private static readonly string[] Classes = { "I", "II", "III", "IV" };
        private readonly Autodesk.Revit.DB.Document _doc;
        private readonly List<GrupoViga> _grupos;
        private readonly ListBox _lista = new ListBox();
        private readonly VigaPreview _previa = new VigaPreview();
        private readonly TextBox _fck = new TextBox { Width = 60 }, _cob = new TextBox { Width = 60 };
        private readonly ComboBox _classe = new ComboBox { Width = 60, ItemsSource = Classes };
        private readonly TextBox _g = new TextBox(), _q = new TextBox(), _conc = new TextBox(), _apE = new TextBox(), _apD = new TextBox();
        private readonly StackPanel _resultado = new StackPanel(), _verif = new StackPanel();
        private readonly TextBlock _titulo = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold };
        private readonly CheckBox _vistas = new CheckBox { Content = "Criar vistas e tabelas da montagem", IsChecked = true, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        private bool _carregando;

        public List<GrupoViga> Selecionados => _grupos.Where(g => g.Selecionado && g.Base != null).ToList();
        public bool CriarVistas => _vistas.IsChecked == true;
        private GrupoViga Atual => (_lista.SelectedItem as ListBoxItem)?.Tag as GrupoViga;

        public VigasWindow(Autodesk.Revit.DB.Document doc, List<GrupoViga> grupos, IEnumerable<string> notas)
        {
            _doc = doc; _grupos = grupos;
            Title = "Vigas pré-moldadas — cálculo e armação";
            Width = 1500; Height = 900; MinWidth = 1150; MinHeight = 650;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Segoe UI"); FontSize = 12;

            var root = new Grid { Margin = new Thickness(10) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(420) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ---- esquerda: grupos ----
            var left = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
            var head = new TextBlock { Text = $"{grupos.Sum(g => g.Quantidade)} vigas em {grupos.Count} grupo(s) de peças iguais", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(head, Dock.Top); left.Children.Add(head);
            string nt = string.Join("\n", notas ?? Enumerable.Empty<string>());
            if (nt.Length > 0)
            {
                var t = new TextBlock { Text = nt, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkGoldenrod, Margin = new Thickness(0, 0, 0, 6), MaxHeight = 160 };
                DockPanel.SetDock(t, Dock.Top); left.Children.Add(t);
            }
            _lista.SelectionChanged += (s, e) => Carregar();
            left.Children.Add(_lista);
            Grid.SetColumn(left, 0); root.Children.Add(left);

            // ---- centro: prévia ----
            var center = new DockPanel();
            DockPanel.SetDock(_titulo, Dock.Top); center.Children.Add(_titulo);
            var leg = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var (txt, cor) in VigaPreview.Legenda)
            {
                leg.Children.Add(new Border { Width = 18, Height = 3, Background = new SolidColorBrush(cor), Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                leg.Children.Add(new TextBlock { Text = txt });
            }
            DockPanel.SetDock(leg, Dock.Bottom); center.Children.Add(leg);
            center.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Background = Brushes.White, Child = _previa, Margin = new Thickness(0, 6, 0, 0) });
            Grid.SetColumn(center, 1); root.Children.Add(center);

            // ---- direita: materiais, cargas, resultado ----
            var right = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            right.Children.Add(Titulo("Materiais (todos os grupos)"));
            var mat = new WrapPanel();
            mat.Children.Add(Rotulo("fck (MPa)")); mat.Children.Add(_fck);
            mat.Children.Add(Rotulo("  classe")); mat.Children.Add(_classe);
            mat.Children.Add(Rotulo("  cobrimento (cm)")); mat.Children.Add(_cob);
            right.Children.Add(mat);
            var g0 = grupos.FirstOrDefault(x => x.Base != null);
            _fck.Text = g0?.Base["Fck"] ?? "40"; _classe.SelectedItem = g0 != null && Classes.Contains(g0.Base["ClasseAgressividade"]) ? g0.Base["ClasseAgressividade"] : "II";
            _cob.Text = g0?.Base["CobFundo"] ?? "2.5";
            right.Children.Add(Botao("Aplicar materiais e recalcular todos", () => { AplicarMateriais(); RecalcularTodos(); }, new Thickness(0, 4, 0, 8)));

            right.Children.Add(Titulo("Cargas do grupo (características, além do peso próprio)"));
            right.Children.Add(Linha("Permanente g (kN/m)", _g));
            right.Children.Add(Linha("Variável q (kN/m)", _q));
            right.Children.Add(Linha("Concentradas (x: G + Q; …)", _conc));
            right.Children.Add(Linha("Apoio esquerdo (cm, 0 = auto)", _apE));
            right.Children.Add(Linha("Apoio direito (cm, 0 = auto)", _apD));
            var bts = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
            bts.Children.Add(Botao("Calcular este grupo", () => { LerCargas(); Recalcular(Atual); Carregar(); }));
            bts.Children.Add(Botao("Copiar cargas para todos", CopiarCargas, new Thickness(6, 0, 0, 0)));
            right.Children.Add(bts);

            right.Children.Add(Titulo("Armadura proposta"));
            right.Children.Add(_resultado);
            right.Children.Add(Botao("Abrir relatório de cálculo (HTML)", AbrirRelatorio, new Thickness(0, 6, 0, 8)));
            right.Children.Add(Titulo("Verificações"));
            right.Children.Add(new ScrollViewer { Content = _verif, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Grid.SetColumn(right, 2);
            root.Children.Add(new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Grid.SetColumn((UIElement)root.Children[root.Children.Count - 1], 2);

            // ---- rodapé ----
            var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var ok = Botao("Armar no Revit", () => DialogResult = true, new Thickness(0, 0, 8, 0));
            ok.IsDefault = true; ok.FontWeight = FontWeights.SemiBold;
            buttons.Children.Add(ok);
            var cancel = Botao("Cancelar", () => DialogResult = false); cancel.IsCancel = true;
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Right); bottom.Children.Add(buttons);
            var esq = new StackPanel { Orientation = Orientation.Horizontal };
            esq.Children.Add(_vistas);
            esq.Children.Add(new TextBlock { Text = "As cargas digitadas ficam gravadas nas vigas (parâmetros PCEB_ de Análise estrutural). A folha fica com você.", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
            bottom.Children.Add(esq);
            Grid.SetRow(bottom, 1); Grid.SetColumnSpan(bottom, 3); root.Children.Add(bottom);
            Content = root;

            foreach (GrupoViga g in grupos) _lista.Items.Add(new ListBoxItem { Tag = g, Content = Item(g) });
            if (_lista.Items.Count > 0) _lista.SelectedIndex = 0;
        }

        // ------------------------------------------------------------------

        private static UIElement Item(GrupoViga g)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            var cb = new CheckBox { IsChecked = g.Selecionado && g.Base != null, IsEnabled = g.Base != null, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            cb.Checked += (s, e) => g.Selecionado = true; cb.Unchecked += (s, e) => g.Selecionado = false;
            sp.Children.Add(cb);
            var (txt, fundo, tinta) = Selo(g.Base == null ? (SituacaoVerificacao?)null : g.Situacao);
            sp.Children.Add(new Border { Background = fundo, CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 0, 6, 0), Width = 78,
                                         Child = new TextBlock { Text = txt, Foreground = tinta, FontSize = 10, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center } });
            sp.Children.Add(new TextBlock { Text = $"{g.Marca}  ×{g.Quantidade}  {g.Secao}  L={Fmt(g.Peca?.L ?? 0)}", VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }

        private static (string, Brush, Brush) Selo(SituacaoVerificacao? s) => s switch
        {
            null => ("FORA", Brushes.Gainsboro, Brushes.DimGray),
            SituacaoVerificacao.Ok => ("OK", new SolidColorBrush(WColor.FromRgb(0xE6, 0xF4, 0xEA)), new SolidColorBrush(WColor.FromRgb(0x1F, 0x7A, 0x3F))),
            SituacaoVerificacao.Conferir => ("CONFERIR", new SolidColorBrush(WColor.FromRgb(0xFF, 0xF4, 0xDC)), new SolidColorBrush(WColor.FromRgb(0x8A, 0x5A, 0x00))),
            _ => ("NÃO ATENDE", new SolidColorBrush(WColor.FromRgb(0xFD, 0xE8, 0xE6)), new SolidColorBrush(WColor.FromRgb(0xA8, 0x22, 0x1A))),
        };

        private void AtualizarItem(GrupoViga g)
        {
            foreach (ListBoxItem it in _lista.Items) if (it.Tag == g) it.Content = Item(g);
        }

        private void Carregar()
        {
            var g = Atual;
            if (g == null) return;
            _carregando = true;
            _g.Text = Fmt(g.Cargas.G); _q.Text = Fmt(g.Cargas.Q); _conc.Text = CargasViga.TextoConcentradas(g.Cargas.Concentradas);
            _apE.Text = Fmt(g.Cargas.ApoioE); _apD.Text = Fmt(g.Cargas.ApoioD);
            _carregando = false;
            _titulo.Text = $"{g.Marca} — {g.Quantidade} peça(s), {g.Secao} cm, L = {Fmt(g.Peca?.L ?? 0)} cm, pontas {g.Pontas}";
            _resultado.Children.Clear(); _verif.Children.Clear();
            if (g.Base == null)
            {
                _resultado.Children.Add(new TextBlock { Text = "Grupo fora do cálculo: " + string.Join(" ", g.Avisos), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick });
                _previa.Mostrar(null, null);
                return;
            }
            var c = g.Calculo;
            if (c != null)
            {
                _resultado.Children.Add(new TextBlock { Text = $"Vão {Fmt(c.Vao)} cm · Md = {Fmt(c.MdMax)} kN·m · Vd = {Fmt(Math.Max(c.VdE, c.VdD))} kN · flecha {Fmt(c.FlechaTotal, "0.00")} cm", Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap });
                foreach (string l in c.ResumoProposta()) _resultado.Children.Add(new TextBlock { Text = "• " + l, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
                foreach (var v in c.Verificacoes.OrderByDescending(x => x.Situacao))
                {
                    var (_, fundo, tinta) = Selo(v.Situacao);
                    _verif.Children.Add(new Border
                    {
                        Background = v.Situacao == SituacaoVerificacao.Ok ? Brushes.Transparent : fundo, Padding = new Thickness(4, 1, 4, 1),
                        Child = new TextBlock { Text = $"{v.Codigo}  {v.Descricao}: {v.Valor} ({v.Limite})" + (v.Dica.Length > 0 ? " — " + v.Dica : ""), Foreground = tinta, TextWrapping = TextWrapping.Wrap },
                    });
                }
                foreach (string a in c.Avisos.Concat(g.Avisos))
                    _verif.Children.Add(new TextBlock { Text = "Atenção: " + a, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkGoldenrod });
            }
            var av = new List<string>();
            List<BarDef> bars = null;
            try { bars = VigasPreMoldadas.Barras(g, g.Representante, av); } catch (Exception ex) { av.Add(ex.Message); }
            foreach (string a in av.Distinct()) _verif.Children.Add(new TextBlock { Text = "Revit: " + a, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SteelBlue });
            _previa.Mostrar(g, bars);
        }

        private bool LerCargas()
        {
            var g = Atual;
            if (g == null || _carregando) return false;
            var erros = new List<string>();
            var c = new CargasViga
            {
                G = Num(_g.Text), Q = Num(_q.Text), ApoioE = Num(_apE.Text), ApoioD = Num(_apD.Text),
                Concentradas = CargasViga.LerConcentradas(_conc.Text, erros),
            };
            if (erros.Count > 0) MessageBox.Show(this, string.Join("\n", erros), Title);
            g.Cargas = c;
            foreach (var m in g.Membros) m.Cargas = c;
            return true;
        }

        private void Recalcular(GrupoViga g)
        {
            if (g == null) return;
            try { VigasPreMoldadas.Calcular(g); } catch (Exception ex) { MessageBox.Show(this, "Cálculo: " + ex.Message, Title); }
            AtualizarItem(g);
        }

        private void RecalcularTodos()
        {
            foreach (var g in _grupos) Recalcular(g);
            Carregar();
        }

        private void AplicarMateriais()
        {
            double fck = Num(_fck.Text), cob = Num(_cob.Text);
            foreach (var g in _grupos) VigasPreMoldadas.AjustarMateriais(g, fck, _classe.SelectedItem as string, cob);
        }

        private void CopiarCargas()
        {
            if (!LerCargas()) return;
            var c = Atual.Cargas;
            foreach (var g in _grupos)
            {
                // as concentradas são da peça (posição em cm): só vão para vigas do mesmo comprimento
                var copia = new CargasViga { G = c.G, Q = c.Q, ApoioE = c.ApoioE, ApoioD = c.ApoioD,
                    Concentradas = Math.Abs((g.Peca?.L ?? 0) - (Atual.Peca?.L ?? 0)) < 0.5 ? c.Concentradas.ToList() : g.Cargas.Concentradas };
                g.Cargas = copia;
                foreach (var m in g.Membros) m.Cargas = copia;
            }
            RecalcularTodos();
        }

        private void AbrirRelatorio()
        {
            var g = Atual;
            if (g?.Calculo == null) return;
            try
            {
                var rel = VigasPreMoldadas.Relatorio(_doc, g);
                string arq = System.IO.Path.Combine(System.IO.Path.GetTempPath(), DEB.Core.Documentacao.RelatorioHtml.NomeArquivo(g.Marca));
                System.IO.File.WriteAllText(arq, DEB.Core.Documentacao.RelatorioHtml.Gerar(rel), new System.Text.UTF8Encoding(true));
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(arq) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(this, "Relatório: " + ex.Message, Title); }
        }

        // ------------------------------------------------------------------

        private static TextBlock Titulo(string t) => new TextBlock { Text = t, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 4) };
        private static TextBlock Rotulo(string t) => new TextBlock { Text = t + " ", VerticalAlignment = VerticalAlignment.Center };
        private static Button Botao(string t, Action a, Thickness? m = null)
        {
            var b = new Button { Content = t, Padding = new Thickness(10, 3, 10, 3), Margin = m ?? new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
            b.Click += (s, e) => a();
            return b;
        }
        private static UIElement Linha(string rot, TextBox tb)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var l = new TextBlock { Text = rot, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(l); Grid.SetColumn(tb, 1); g.Children.Add(tb);
            return g;
        }
        private static double Num(string s) => double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
        private static string Fmt(double x, string f = "0.##") => x.ToString(f, CultureInfo.GetCultureInfo("pt-BR"));

        public static string Diagnostico;

        /// <summary>Renderiza a janela em PNG (sem exibi-la) — teste automático.</summary>
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
            int Px() { var px = new byte[4]; bmp.CopyPixels(new Int32Rect(2, 2, 1, 1), px, 4, 0); return px[3]; }
            Diagnostico = $"fundo alfa {Px()}";
            try { bmp.Render(root); Diagnostico += $"; janela alfa {Px()}; tamanho {root.ActualWidth}x{root.ActualHeight}"; }
            catch (Exception ex) { Diagnostico += "; erro " + ex; }
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using (var fs = System.IO.File.Create(path)) enc.Save(fs);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (DialogResult == true && Selecionados.Count == 0) { MessageBox.Show(this, "Nenhum grupo marcado para armar.", Title); e.Cancel = true; }
            else if (DialogResult == true && Selecionados.Any(g => g.Calculo == null))
            {
                foreach (var g in Selecionados.Where(x => x.Calculo == null)) VigasPreMoldadas.Calcular(g);
            }
            base.OnClosing(e);
        }
    }

    /// <summary>Prévia da viga: elevação com as barras, seção no meio do vão e diagramas de Md e Vd (do cálculo).</summary>
    internal sealed class VigaPreview : FrameworkElement
    {
        private GrupoViga _g;
        private List<BarDef> _bars;

        public static readonly (string, WColor)[] Legenda =
        {
            ("Longitudinal", WColor.FromRgb(0xC0, 0x1F, 0x1F)), ("Estribo", WColor.FromRgb(0x2E, 0x8B, 0x57)),
            ("Dente (estribos, suspensão)", WColor.FromRgb(0xD4, 0x8A, 0x1A)), ("Tirante, grampo, costura", WColor.FromRgb(0x1F, 0x5A, 0xA6)),
            ("Alça CP-190", WColor.FromRgb(0x8E, 0x44, 0xAD)),
        };

        private static Pen Caneta(BarRole r, double esp = 1)
        {
            WColor c = r switch
            {
                BarRole.Longitudinal or BarRole.BeamWelded => Legenda[0].Item2,
                BarRole.Stirrup => Legenda[1].Item2,
                BarRole.BeamGerber or BarRole.BeamHanger => Legenda[2].Item2,
                BarRole.LiftLoop => Legenda[4].Item2,
                _ => Legenda[3].Item2,
            };
            var p = new Pen(new SolidColorBrush(c), esp); p.Freeze(); return p;
        }

        public void Mostrar(GrupoViga g, List<BarDef> bars) { _g = g; _bars = bars; InvalidateVisual(); }

        private static double Cm(double ft) => ft * 30.48;

        /// <summary>Último erro do desenho da prévia (teste automático).</summary>
        public static string UltimoErro;

        protected override void OnRender(DrawingContext dc)
        {
            try { Desenhar(dc); }
            catch (Exception ex) { UltimoErro = ex.ToString(); }
        }

        private void Desenhar(DrawingContext dc)
        {
            double W = ActualWidth, H = ActualHeight;
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, W, H));
            if (_g?.Base == null || W < 50 || H < 50) return;
            var v = _g.Proposta;
            double b = v.Numero("B"), h = v.Numero("H"), L = v.Numero("L");
            var forma = new Pen(Brushes.Black, 1.2); forma.Freeze();
            var fina = new Pen(Brushes.Gray, 0.6); fina.Freeze();
            var tf = new Typeface("Segoe UI");
            void Txt(string s, double x, double y, double tam = 11, Brush cor = null, bool centro = false)
            {
                var ft = new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, tam, cor ?? Brushes.Black, 1.25);
                dc.DrawText(ft, new WPoint(centro ? x - ft.Width / 2 : x, y));
            }

            // ---- elevação (faixa de cima) ----
            double mx = 20, larg = W - 2 * mx, esc = larg / L;
            double hElev = Math.Min(h * esc * 2.2, H * 0.32);   // altura exagerada para ver as barras (escala vertical própria)
            double escV = hElev / h, yTop = 34, yBot = yTop + hElev;
            WPoint E(double x, double z) => new WPoint(mx + x * esc, yBot - (z - _g.AlmaZ0) * escV);
            Txt("ELEVAÇÃO (escala vertical ampliada)", mx, 8, 12, Brushes.DimGray);
            var e = _g.Peca;
            var contorno = new StreamGeometry();
            using (var sg = contorno.Open())
            {
                double hdE = e?.DenteE?.Altura ?? h, cdE = e?.DenteE?.Comprimento ?? 0, hdD = e?.DenteD?.Altura ?? h, cdD = e?.DenteD?.Comprimento ?? 0;
                sg.BeginFigure(E(0, _g.AlmaZ0 + h - hdE), false, true);
                sg.LineTo(E(cdE, _g.AlmaZ0 + h - hdE), true, false); sg.LineTo(E(cdE, _g.AlmaZ0), true, false);
                sg.LineTo(E(L - cdD, _g.AlmaZ0), true, false); sg.LineTo(E(L - cdD, _g.AlmaZ0 + h - hdD), true, false);
                sg.LineTo(E(L, _g.AlmaZ0 + h - hdD), true, false); sg.LineTo(E(L, _g.AlmaZ0 + h), true, false); sg.LineTo(E(0, _g.AlmaZ0 + h), true, false);
            }
            contorno.Freeze();
            dc.DrawGeometry(new SolidColorBrush(WColor.FromRgb(0xF2, 0xF2, 0xF2)), forma, contorno);
            if (_bars != null)
                foreach (BarDef bd in _bars)
                    foreach (var inst in bd.Instances())
                    {
                        var pts = inst.Select(p => E(Cm(p.A), Cm(p.C))).ToList();
                        if (bd.Role == BarRole.BeamHairpin || bd.Role == BarRole.BeamStitch || bd.Role == BarRole.BeamWelded)
                            pts = new List<WPoint> { pts.First(), new WPoint(pts.Max(q => q.X), pts[0].Y), pts.Last() };
                        var pen = Caneta(bd.Role, bd.Role == BarRole.Stirrup ? 0.7 : 1.3);
                        for (int i = 0; i + 1 < pts.Count; i++) dc.DrawLine(pen, pts[i], pts[i + 1]);
                        if (bd.Closed && pts.Count > 2) dc.DrawLine(pen, pts[^1], pts[0]);
                        if (bd.ArcMid.Count > 0) foreach (var kv in bd.ArcMid) { var m = E(Cm(kv.Value.A), Cm(kv.Value.C)); dc.DrawLine(pen, pts[kv.Key], m); dc.DrawLine(pen, m, pts[kv.Key + 1]); }
                    }
            // apoios
            var c = _g.Calculo;
            if (c != null)
                foreach (double xa in new[] { c.ApoioE, L - c.ApoioD })
                {
                    var p0 = E(xa, _g.AlmaZ0);
                    var tri = new StreamGeometry();
                    using (var sg = tri.Open()) { sg.BeginFigure(p0, true, true); sg.LineTo(new WPoint(p0.X - 6, p0.Y + 10), true, false); sg.LineTo(new WPoint(p0.X + 6, p0.Y + 10), true, false); }
                    tri.Freeze(); dc.DrawGeometry(Brushes.Black, null, tri);
                }
            Txt($"L = {L:0.#} cm", mx + larg / 2, yBot + 12, 11, Brushes.DimGray, true);

            // ---- seção no meio do vão (embaixo, à esquerda) ----
            double ySec = yBot + 40, hSecPx = H - ySec - 30, escS = Math.Min(hSecPx / (h + 10), 300 / (b + 10));
            double sx0 = mx + 30, sy1 = ySec + 22;
            Txt("SEÇÃO NO MEIO DO VÃO", mx, ySec, 12, Brushes.DimGray);
            WPoint S(double y, double z) => new WPoint(sx0 + (y - _g.AlmaY0) * escS, sy1 + (_g.AlmaZ0 + h - z) * escS);
            dc.DrawRectangle(new SolidColorBrush(WColor.FromRgb(0xF2, 0xF2, 0xF2)), forma, new Rect(S(_g.AlmaY0, _g.AlmaZ0 + h), S(_g.AlmaY0 + b, _g.AlmaZ0)));
            if (_bars != null)
            {
                double xm = L / 2;
                var est = _bars.Where(x => x.Role == BarRole.Stirrup)
                    .OrderBy(x => Math.Abs(Cm(x.Points[0].A) + Cm(x.Spacing) * (x.Count - 1) / 2 - xm)).FirstOrDefault();
                if (est != null)
                {
                    var pts = est.Points.Select(p => S(Cm(p.B), Cm(p.C))).ToList();
                    var pen = Caneta(BarRole.Stirrup, 1.2);
                    for (int i = 0; i + 1 < pts.Count; i++) dc.DrawLine(pen, pts[i], pts[i + 1]);
                    if (est.Closed) dc.DrawLine(pen, pts[^1], pts[0]);
                }
                foreach (var bd in _bars.Where(x => x.Role == BarRole.Longitudinal))
                {
                    double a0 = Cm(bd.Points.Min(p => p.A)), a1 = Cm(bd.Points.Max(p => p.A));
                    if (xm < a0 || xm > a1) continue;
                    var ctr = S(Cm(bd.Points[bd.Points.Count > 2 ? 1 : 0].B), Cm(bd.Points[bd.Points.Count > 2 ? 1 : 0].C));
                    double r = Math.Max(2.5, bd.DiameterMm / 20 * escS);
                    dc.DrawEllipse(new SolidColorBrush(Legenda[0].Item2), null, ctr, r, r);
                }
            }
            Txt($"{b:0.#} × {h:0.#} cm", sx0 + b * escS / 2, sy1 + h * escS + 6, 11, Brushes.DimGray, true);

            // ---- diagramas (embaixo, à direita) ----
            if (c == null || c.Diagrama.Count < 2) return;
            double dx0 = sx0 + b * escS + 80, dw = W - dx0 - mx;
            if (dw < 120) return;
            double dh = (hSecPx - 30) / 2;
            void Diagrama(string tit, Func<(double X, double Md, double Vd), double> f, double y0, bool baixo, WColor cor)
            {
                var pts = c.Diagrama.Where(p => p.X >= c.ApoioE - 1e-6 && p.X <= L - c.ApoioD + 1e-6).ToList();
                double vmax = Math.Max(1e-6, pts.Max(p => Math.Abs(f(p))));
                double yb = y0 + 18 + dh / 2;
                WPoint D(double x, double val) => new WPoint(dx0 + (x - c.ApoioE) / Math.Max(1, c.Vao) * dw, yb + (baixo ? 1 : -1) * val / vmax * (dh / 2 - 6));
                Txt(tit, dx0, y0, 12, Brushes.DimGray);
                dc.DrawLine(fina, new WPoint(dx0, yb), new WPoint(dx0 + dw, yb));
                var geo = new StreamGeometry();
                using (var sg = geo.Open())
                {
                    sg.BeginFigure(D(pts[0].X, 0), true, true);
                    foreach (var p in pts) sg.LineTo(D(p.X, f(p)), true, false);
                    sg.LineTo(D(pts[^1].X, 0), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(new SolidColorBrush(WColor.FromArgb(0x40, cor.R, cor.G, cor.B)), new Pen(new SolidColorBrush(cor), 1.2), geo);
                var pm = pts.OrderByDescending(p => Math.Abs(f(p))).First();
                Txt(Math.Abs(f(pm)).ToString("0.0", CultureInfo.GetCultureInfo("pt-BR")), D(pm.X, f(pm)).X, D(pm.X, f(pm)).Y + (baixo ? 2 : -16), 11, Brushes.Black, true);
            }
            Diagrama("Md (kN·m)", p => p.Md, ySec, true, WColor.FromRgb(0x1F, 0x5A, 0xA6));
            Diagrama("Vd (kN)", p => p.Vd, ySec + dh + 30, false, WColor.FromRgb(0xC0, 0x6A, 0x1F));
        }
    }
}
