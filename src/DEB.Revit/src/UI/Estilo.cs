using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using DEB.Core.Normas.Verificacao;
using WColor = System.Windows.Media.Color;
using WPoint = System.Windows.Point;

namespace DEB.Revit.UI
{
    /// <summary>
    /// Kit visual comum das janelas de prévia do PCEB no Revit (23/11/2026): a identidade do PCEB (verde-mata e amarelo-ipê, decisão
    /// do usuário de 11/11) e a organização vista no estudo do Precast Design (docs/01_catalogo/programas.md) — cabeçalho com o selo,
    /// abas por etapa, cartões, tabela de verificações com utilizado × exigido × item da norma. Estilos implícitos em XAML (texto,
    /// sem arquivo .xaml: o projeto continua com as janelas escritas em código).
    /// </summary>
    internal static class Estilo
    {
        public static readonly WColor Mata = Hex("#123a2c"), Ipe = Hex("#e3a92b"), Ocre = Hex("#8a5a00"), Anil = Hex("#1d3f6e"),
                                      Concreto = Hex("#8a8f88"), Papel = Hex("#f2f1ec"), Tinta = Hex("#13201a"), Linha = Hex("#d9d7cf"),
                                      Suave = Hex("#5d665f"), Branco = Colors.White, MataClaro = Hex("#e7efe9");

        public static WColor Hex(string h) => (WColor)ColorConverter.ConvertFromString(h);
        public static SolidColorBrush B(WColor c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        private const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='Button'>
    <Setter Property='Foreground' Value='#13201a'/>
    <Setter Property='Background' Value='#ffffff'/>
    <Setter Property='BorderBrush' Value='#c9c6bb'/>
    <Setter Property='Padding' Value='12,4'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='4' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='#123a2c'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='bd' Property='Background' Value='#e7efe9'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='Primario' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'>
    <Setter Property='Background' Value='#123a2c'/>
    <Setter Property='Foreground' Value='#ffffff'/>
    <Setter Property='BorderBrush' Value='#123a2c'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
  </Style>
  <Style TargetType='TextBox'>
    <Setter Property='Padding' Value='4,2'/>
    <Setter Property='BorderBrush' Value='#c9c6bb'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
  </Style>
  <Style TargetType='TabItem'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TabItem'>
          <Border x:Name='bd' Padding='14,6' Margin='0,0,2,0' Background='Transparent' BorderThickness='0,0,0,3' BorderBrush='Transparent'>
            <ContentPresenter x:Name='hdr' ContentSource='Header' TextElement.FontSize='12.5'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsSelected' Value='True'>
              <Setter TargetName='bd' Property='BorderBrush' Value='#e3a92b'/>
              <Setter TargetName='bd' Property='Background' Value='#ffffff'/>
              <Setter TargetName='hdr' Property='TextElement.FontWeight' Value='SemiBold'/>
            </Trigger>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='#ffffff'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Setter Property='Foreground' Value='#13201a'/>
  </Style>
  <Style TargetType='TabControl'>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='Padding' Value='0'/>
    <Setter Property='Background' Value='Transparent'/>
  </Style>
  <Style TargetType='ListBoxItem'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ListBoxItem'>
          <Border x:Name='bd' Padding='8,6' Margin='0,0,0,4' Background='#ffffff' BorderBrush='#d9d7cf' BorderThickness='1' CornerRadius='4'>
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsSelected' Value='True'>
              <Setter TargetName='bd' Property='BorderBrush' Value='#123a2c'/>
              <Setter TargetName='bd' Property='BorderThickness' Value='2'/>
              <Setter TargetName='bd' Property='Background' Value='#e7efe9'/>
            </Trigger>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='#123a2c'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='ListBox'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Disabled'/>
  </Style>
</ResourceDictionary>";

        private static ResourceDictionary _rd;

        /// <summary>Fundo, fonte e estilos implícitos na janela.</summary>
        public static void Aplicar(Window w)
        {
            try
            {
                _rd ??= (ResourceDictionary)XamlReader.Parse(Xaml);
                w.Resources.MergedDictionaries.Add(_rd);
            }
            catch { /* sem os estilos a janela continua utilizável */ }
            w.Background = B(Papel);
            w.FontFamily = new FontFamily("Segoe UI");
            w.FontSize = 12;
            w.Foreground = B(Tinta);
        }

        public static Button Primario(string texto, Action acao)
        {
            var b = Botao(texto, acao);
            if (_rd != null && _rd.Contains("Primario")) b.Style = (Style)_rd["Primario"];
            else { b.Background = B(Mata); b.Foreground = Brushes.White; b.FontWeight = FontWeights.SemiBold; }
            return b;
        }

        public static Button Botao(string texto, Action acao, Thickness? margem = null)
        {
            var b = new Button { Content = texto, Margin = margem ?? new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
            b.Click += (s, e) => acao();
            return b;
        }

        /// <summary>Faixa do topo: símbolo, título, subtítulo e um espaço à direita (selo ou resumo).</summary>
        public static Border Cabecalho(string titulo, string subtitulo, UIElement direita = null)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(Simbolo(30));
            var tx = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            tx.Children.Add(new TextBlock { Text = titulo, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            if (!string.IsNullOrEmpty(subtitulo)) tx.Children.Add(new TextBlock { Text = subtitulo, FontSize = 11.5, Foreground = B(Hex("#c9d6cd")) });
            Grid.SetColumn(tx, 1); g.Children.Add(tx);
            if (direita != null) { Grid.SetColumn(direita, 2); g.Children.Add(direita); }
            return new Border
            {
                Background = B(Mata), Padding = new Thickness(14, 9, 14, 9), Child = g,
                BorderBrush = B(Ipe), BorderThickness = new Thickness(0, 0, 0, 3),
            };
        }

        /// <summary>O símbolo do PCEB: seção de viga (moldura papel, fundo mata) com o estribo e as cinco barras do Cruzeiro do Sul.</summary>
        public static FrameworkElement Simbolo(double tam)
        {
            var dv = new DrawingGroup();
            using (var dc = dv.Open())
            {
                dc.DrawRoundedRectangle(B(Papel), null, new Rect(0, 0, 30, 30), 4, 4);
                dc.DrawRectangle(B(Mata), null, new Rect(3, 3, 24, 24));
                var est = new Pen(B(Papel), 1.2); est.Freeze();
                dc.DrawRectangle(null, est, new Rect(6, 6, 18, 18));
                foreach (var (x, y, r) in new[] { (15.0, 8.8, 1.7), (15.0, 21.5, 1.9), (9.6, 14.2, 1.6), (20.8, 13.0, 1.5), (17.6, 17.6, 1.1) })
                    dc.DrawEllipse(B(Ipe), null, new WPoint(x, y), r, r);
            }
            dv.Freeze();
            return new Image { Source = new DrawingImage(dv), Width = tam, Height = tam, VerticalAlignment = VerticalAlignment.Center };
        }

        /// <summary>Cartão branco com título (pequeno, em maiúsculas) e o conteúdo.</summary>
        public static Border Cartao(string titulo, UIElement conteudo, Thickness? margem = null)
        {
            var sp = new StackPanel();
            if (!string.IsNullOrEmpty(titulo))
                sp.Children.Add(new TextBlock { Text = titulo.ToUpper(CultureInfo.GetCultureInfo("pt-BR")), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = B(Suave), Margin = new Thickness(0, 0, 0, 6) });
            sp.Children.Add(conteudo);
            return new Border
            {
                Background = Brushes.White, BorderBrush = B(Linha), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 10, 12, 10), Margin = margem ?? new Thickness(0, 0, 0, 10), Child = sp,
            };
        }

        public static (string Texto, WColor Fundo, WColor Tinta) Cores(SituacaoVerificacao? s) => s switch
        {
            null => ("FORA", Hex("#e5e4de"), Suave),
            SituacaoVerificacao.Ok => ("OK", Hex("#e6f4ea"), Hex("#1f7a3f")),
            SituacaoVerificacao.Conferir => ("CONFERIR", Hex("#fff4dc"), Ocre),
            _ => ("NÃO ATENDE", Hex("#fde8e6"), Hex("#a8221a")),
        };

        /// <summary>Selo da situação (OK / CONFERIR / NÃO ATENDE / FORA).</summary>
        public static Border Selo(SituacaoVerificacao? s, double largura = 82, double fonte = 10, string texto = null)
        {
            var (t, f, c) = Cores(s);
            return new Border
            {
                Background = B(f), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1, 6, 1), Width = largura,
                BorderBrush = B(c), BorderThickness = new Thickness(0.8), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = texto ?? t, Foreground = B(c), FontSize = fonte, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
            };
        }

        /// <summary>Linhas "rótulo ··· valor" (pares), em duas colunas alinhadas.</summary>
        public static Grid Pares(IEnumerable<(string Rotulo, string Valor)> pares, double colRotulo = 170, bool negrito = false)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(colRotulo) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int r = 0;
            foreach (var (rot, val) in pares)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var a = new TextBlock { Text = rot, Foreground = B(Suave), Margin = new Thickness(0, 1, 6, 1), TextWrapping = TextWrapping.Wrap };
                var b = new TextBlock { Text = val, Margin = new Thickness(0, 1, 0, 1), TextWrapping = TextWrapping.Wrap, FontWeight = negrito ? FontWeights.SemiBold : FontWeights.Normal };
                Grid.SetRow(a, r); Grid.SetRow(b, r); Grid.SetColumn(b, 1);
                g.Children.Add(a); g.Children.Add(b);
                r++;
            }
            return g;
        }

        /// <summary>Campo com rótulo à esquerda e unidade à direita.</summary>
        public static Grid Campo(string rotulo, FrameworkElement controle, string unidade = "", double colRotulo = 150)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(colRotulo) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            var l = new TextBlock { Text = rotulo, VerticalAlignment = VerticalAlignment.Center, Foreground = B(Suave) };
            g.Children.Add(l);
            Grid.SetColumn(controle, 1); g.Children.Add(controle);
            var u = new TextBlock { Text = unidade, VerticalAlignment = VerticalAlignment.Center, Foreground = B(Concreto), Margin = new Thickness(6, 0, 0, 0) };
            Grid.SetColumn(u, 2); g.Children.Add(u);
            return g;
        }

        /// <summary>
        /// Tabela das verificações (como no estudo do Precast Design): situação, código, verificação (com a dica embaixo), utilizado,
        /// exigido e o item da norma; agrupada pelo grupo da regra, as pendências primeiro dentro de cada grupo.
        /// </summary>
        public static UIElement TabelaVerificacoes(IEnumerable<ResultadoVerificacao> itens, bool agrupar = true)
        {
            var g = new Grid();
            // situação e código fixos; as outras proporcionais à largura (a descrição fica com a maior parte)
            double[] larg = { 88, 40, -2.4, -1, -1.2, -1 };
            for (int i = 0; i < larg.Length; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = larg[i] > 0 ? new GridLength(larg[i]) : new GridLength(-larg[i], GridUnitType.Star), MinWidth = larg[i] > 0 ? 0 : 60 });
            int r = 0;
            void NovaLinha(UIElement[] cel, Brush fundo, bool cabecalho = false)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var bg = new Border { Background = fundo, BorderBrush = B(Linha), BorderThickness = new Thickness(0, 0, 0, 1) };
                Grid.SetRow(bg, r); Grid.SetColumnSpan(bg, larg.Length); g.Children.Add(bg);
                for (int i = 0; i < cel.Length; i++)
                {
                    if (cel[i] == null) continue;
                    if (cel[i] is FrameworkElement fe) fe.Margin = new Thickness(6, cabecalho ? 4 : 5, 6, cabecalho ? 4 : 5);
                    Grid.SetRow(cel[i], r); Grid.SetColumn(cel[i], i); g.Children.Add(cel[i]);
                }
                r++;
            }
            TextBlock T(string s, bool neg = false, Brush cor = null, bool wrap = true) =>
                new TextBlock { Text = s ?? "", TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, FontWeight = neg ? FontWeights.SemiBold : FontWeights.Normal, Foreground = cor ?? B(Tinta), VerticalAlignment = VerticalAlignment.Top };
            NovaLinha(new UIElement[] { T("Situação", true, B(Suave)), T("Cód.", true, B(Suave)), T("Verificação", true, B(Suave)), T("Utilizado", true, B(Suave)), T("Exigido", true, B(Suave)), T("Norma", true, B(Suave)) },
                  B(Hex("#ebe9e2")), true);
            var lista = itens.ToList();
            var grupos = agrupar ? lista.GroupBy(x => string.IsNullOrEmpty(x.Grupo) ? "Outras" : x.Grupo).ToList()
                                 : new List<IGrouping<string, ResultadoVerificacao>> { lista.GroupBy(x => "").FirstOrDefault() }.Where(x => x != null).ToList();
            foreach (var gr in grupos)
            {
                if (agrupar)
                {
                    var tg = T(gr.Key, true, B(Mata));
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    tg.Margin = new Thickness(6, 10, 6, 3);
                    Grid.SetRow(tg, r); Grid.SetColumnSpan(tg, larg.Length); g.Children.Add(tg);
                    r++;
                }
                foreach (var v in gr.OrderByDescending(x => x.Situacao))
                {
                    var desc = new StackPanel();
                    desc.Children.Add(T(v.Descricao));
                    if (!string.IsNullOrEmpty(v.Dica)) desc.Children.Add(new TextBlock { Text = v.Dica, TextWrapping = TextWrapping.Wrap, Foreground = B(Suave), FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
                    var selo = Selo(v.Situacao, 80, 9.5);
                    selo.VerticalAlignment = VerticalAlignment.Top; selo.HorizontalAlignment = HorizontalAlignment.Left;
                    NovaLinha(new UIElement[] { selo, T(v.Codigo, true, B(Suave)), desc, T(v.Valor), T(v.Limite), T(v.Referencia, false, B(Suave)) },
                          v.Situacao == SituacaoVerificacao.Ok ? Brushes.White : B(Cores(v.Situacao).Fundo));
                }
            }
            return g;
        }

        /// <summary>Grava o conteúdo de uma janela em PNG sem mostrá-la (testes automáticos; com a sessão do Windows bloqueada sai vazio).</summary>
        public static void Renderizar(Window w, string arquivo, int largura, int altura)
        {
            var root = (FrameworkElement)w.Content;
            root.Measure(new Size(largura, altura));
            root.Arrange(new Rect(0, 0, largura, altura));
            root.UpdateLayout();
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(largura, altura, 96, 96, PixelFormats.Pbgra32);
            var bg = new DrawingVisual();
            using (DrawingContext dc = bg.RenderOpen()) dc.DrawRectangle(w.Background ?? Brushes.White, null, new Rect(0, 0, largura, altura));
            bmp.Render(bg);
            bmp.Render(root);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using (var fs = System.IO.File.Create(arquivo)) enc.Save(fs);
        }

        /// <summary>Resumo: n OK · n a conferir · n não atende.</summary>
        public static string Contagem(IEnumerable<ResultadoVerificacao> itens)
        {
            var l = itens.ToList();
            int ok = l.Count(x => x.Situacao == SituacaoVerificacao.Ok), cf = l.Count(x => x.Situacao == SituacaoVerificacao.Conferir), na = l.Count - ok - cf;
            return $"{ok} OK · {cf} a conferir · {na} não atende";
        }
    }

    /// <summary>Série de um <see cref="Grafico"/>: pontos (x, y), cor, estilo.</summary>
    internal sealed class SerieGrafico
    {
        public string Nome { get; set; } = "";
        public List<(double X, double Y)> Pontos { get; set; } = new List<(double, double)>();
        public WColor Cor { get; set; } = Estilo.Anil;
        public double Espessura { get; set; } = 1.6;
        public bool Tracejada { get; set; }
        public bool Preencher { get; set; }
        /// <summary>Rótulo no ponto de maior |y| (e nas pontas, se <see cref="RotularPontas"/>).</summary>
        public bool RotularMaximo { get; set; }
        public bool RotularPontas { get; set; }
        public string Unidade { get; set; } = "";
    }

    /// <summary>
    /// Gráfico de linhas simples para as janelas (diagramas de esforços, resistentes × solicitantes): eixos com escala "redonda",
    /// grade leve, legenda e rótulos de valores. <see cref="YParaBaixo"/> = convenção dos diagramas de momento (positivo para baixo).
    /// </summary>
    internal sealed class Grafico : FrameworkElement
    {
        public string Titulo { get; set; } = "";
        public string RotuloX { get; set; } = "x (cm)";
        public string RotuloY { get; set; } = "";
        public bool YParaBaixo { get; set; }
        public List<SerieGrafico> Series { get; } = new List<SerieGrafico>();
        /// <summary>Marcas verticais no eixo x (apoios, faces dos dentes...): (x, texto).</summary>
        public List<(double X, string Texto)> Marcas { get; } = new List<(double, string)>();

        private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
        private static readonly Typeface Tf = new Typeface("Segoe UI");

        public void Atualizar() => InvalidateVisual();

        private static double Redondo(double v)
        {
            if (v <= 0) return 1;
            double e = Math.Pow(10, Math.Floor(Math.Log10(v))), f = v / e;
            return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 5 ? 5 : 10) * e;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double W = ActualWidth, H = ActualHeight;
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, W, H));
            var pts = Series.SelectMany(s => s.Pontos).ToList();
            if (pts.Count < 2 || W < 120 || H < 80) return;
            FormattedText Ft(string s, double tam, Brush c, bool neg = false) =>
                new FormattedText(s, Pt, FlowDirection.LeftToRight, neg ? new Typeface(Tf.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal) : Tf, tam, c, 1.25);
            var cinza = Estilo.B(Estilo.Suave);
            dc.DrawText(Ft(Titulo + (string.IsNullOrEmpty(RotuloY) ? "" : $" ({RotuloY})"), 12.5, Estilo.B(Estilo.Tinta), true), new WPoint(8, 4));
            // legenda à direita do título
            double xl = W - 10;
            foreach (var s in Enumerable.Reverse(Series).Where(s => !string.IsNullOrEmpty(s.Nome)))
            {
                var ft = Ft(s.Nome, 11, cinza);
                xl -= ft.Width;
                dc.DrawText(ft, new WPoint(xl, 6));
                var pen = new Pen(Estilo.B(s.Cor), 2.2) { DashStyle = s.Tracejada ? DashStyles.Dash : DashStyles.Solid };
                dc.DrawLine(pen, new WPoint(xl - 24, 14), new WPoint(xl - 6, 14));
                xl -= 36;
            }
            double ml = 54, mr = 16, mt = 30, mb = 34;
            double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X);
            foreach (var m in Marcas) { x0 = Math.Min(x0, m.X); x1 = Math.Max(x1, m.X); }
            double y0 = Math.Min(0, pts.Min(p => p.Y)), y1 = Math.Max(0, pts.Max(p => p.Y));
            if (y1 - y0 < 1e-9) y1 = y0 + 1;
            double passo = Redondo((y1 - y0) / 4);
            y0 = Math.Floor(y0 / passo + 1e-9) * passo; y1 = Math.Ceiling(y1 / passo - 1e-9) * passo;
            if (y1 - y0 < 1e-9) y1 = y0 + passo;
            double pw = W - ml - mr, ph = H - mt - mb;
            WPoint P(double x, double y) => new WPoint(ml + (x - x0) / Math.Max(1e-9, x1 - x0) * pw,
                                                      YParaBaixo ? mt + (y - y0) / (y1 - y0) * ph : mt + (y1 - y) / (y1 - y0) * ph);
            var grade = new Pen(Estilo.B(Estilo.Hex("#ecebe5")), 1); grade.Freeze();
            var eixo = new Pen(Estilo.B(Estilo.Concreto), 1); eixo.Freeze();
            for (double y = y0; y <= y1 + passo / 2; y += passo)
            {
                var a = P(x0, y); var b = P(x1, y);
                dc.DrawLine(Math.Abs(y) < passo / 1000 ? eixo : grade, a, b);
                var ft = Ft(y.ToString("0.##", Pt), 10, cinza);
                dc.DrawText(ft, new WPoint(ml - 6 - ft.Width, a.Y - ft.Height / 2));
            }
            double px = Redondo((x1 - x0) / 6);
            for (double x = Math.Ceiling(x0 / px) * px; x <= x1 + 1e-6; x += px)
            {
                var a = P(x, y0); var b = P(x, y1);
                dc.DrawLine(grade, a, b);
                var ft = Ft(x.ToString("0", Pt), 10, cinza);
                dc.DrawText(ft, new WPoint(a.X - ft.Width / 2, mt + ph + 3));
            }
            var rx = Ft(RotuloX, 10, cinza); dc.DrawText(rx, new WPoint(ml + pw - rx.Width, H - rx.Height - 2));
            foreach (var (x, t) in Marcas)
            {
                var a = P(x, y0); var b = P(x, y1);
                var pm = new Pen(Estilo.B(Estilo.Concreto), 1) { DashStyle = DashStyles.Dot }; pm.Freeze();
                dc.DrawLine(pm, a, b);
                if (!string.IsNullOrEmpty(t)) { var ft = Ft(t, 9.5, cinza); dc.DrawText(ft, new WPoint(a.X + 3 + ft.Width > ml + pw ? a.X - 3 - ft.Width : a.X + 3, mt + 2)); }
            }
            foreach (var s in Series)
            {
                if (s.Pontos.Count < 2) continue;
                var pen = new Pen(Estilo.B(s.Cor), s.Espessura) { DashStyle = s.Tracejada ? DashStyles.Dash : DashStyles.Solid, LineJoin = PenLineJoin.Round };
                pen.Freeze();
                var geo = new StreamGeometry();
                using (var sg = geo.Open())
                {
                    if (s.Preencher) { sg.BeginFigure(P(s.Pontos[0].X, 0), true, true); sg.LineTo(P(s.Pontos[0].X, s.Pontos[0].Y), true, true); }
                    else sg.BeginFigure(P(s.Pontos[0].X, s.Pontos[0].Y), false, false);
                    foreach (var p in s.Pontos.Skip(1)) sg.LineTo(P(p.X, p.Y), true, true);
                    if (s.Preencher) sg.LineTo(P(s.Pontos[^1].X, 0), true, true);
                }
                geo.Freeze();
                dc.DrawGeometry(s.Preencher ? Estilo.B(WColor.FromArgb(0x38, s.Cor.R, s.Cor.G, s.Cor.B)) : null, pen, geo);
                void Rot((double X, double Y) p, bool unidade)
                {
                    var q = P(p.X, p.Y);
                    var ft = Ft(p.Y.ToString("0.0", Pt) + (unidade && s.Unidade != "" ? " " + s.Unidade : ""), 10.5, Estilo.B(s.Cor), true);
                    bool baixo = YParaBaixo ? p.Y >= 0 : p.Y < 0;
                    double xx = Math.Max(ml, Math.Min(ml + pw - ft.Width, q.X - ft.Width / 2));
                    double yy = baixo ? q.Y + 3 : q.Y - ft.Height - 3;
                    if (yy < mt) yy = q.Y + 3;                       // sem passar por cima do título
                    if (yy + ft.Height > mt + ph) yy = q.Y - ft.Height - 3;
                    dc.DrawText(ft, new WPoint(xx, yy));
                }
                if (s.RotularMaximo) Rot(s.Pontos.OrderByDescending(p => Math.Abs(p.Y)).First(), true);
                if (s.RotularPontas) { Rot(s.Pontos[0], false); Rot(s.Pontos[^1], false); }
            }
        }
    }
}
