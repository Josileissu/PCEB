using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D;
using DEB.Core.Elementos.Viga.PreMoldada;
using DEB.Core.Elementos.Viga.PreMoldada.Calculo;
using DEB.Core.Normas.Verificacao;
using DEB.Revit.Core;
using DEB.Revit.Precast;
using DEB.Revit.UI;
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
        private readonly TextBox _fck = new TextBox(), _cob = new TextBox();
        private readonly ComboBox _classe = new ComboBox { ItemsSource = Classes };
        private readonly TextBox _g = new TextBox(), _q = new TextBox(), _conc = new TextBox(), _apE = new TextBox(), _apD = new TextBox();
        private readonly StackPanel _resultado = new StackPanel();
        private readonly TextBlock _titulo = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _subtitulo = new TextBlock { Foreground = UI.Estilo.B(UI.Estilo.Suave), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 2, 0, 0) };
        private readonly ContentControl _seloGrupo = new ContentControl { Margin = new Thickness(0, 0, 10, 0) };
        private readonly TabControl _abas = new TabControl();
        private readonly ContentControl _esforcos = new ContentControl(), _longitudinal = new ContentControl(), _transversal = new ContentControl(),
                                        _dente = new ContentControl(), _verificacoes = new ContentControl();
        private readonly CheckBox _vistas = new CheckBox { Content = "Criar vistas e tabelas da montagem", IsChecked = true, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        private bool _carregando;

        public List<GrupoViga> Selecionados => _grupos.Where(g => g.Selecionado && g.Base != null).ToList();
        public bool CriarVistas => _vistas.IsChecked == true;
        private GrupoViga Atual => (_lista.SelectedItem as ListBoxItem)?.Tag as GrupoViga;

        public VigasWindow(Autodesk.Revit.DB.Document doc, List<GrupoViga> grupos, IEnumerable<string> notas)
        {
            _doc = doc; _grupos = grupos;
            Title = "PCEB — Vigas pré-moldadas: cálculo e armação";
            Width = 1500; Height = 900; MinWidth = 1150; MinHeight = 650;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            UI.Estilo.Aplicar(this);

            var raiz = new DockPanel();
            // ---- cabeçalho: título, resumo e o selo geral ----
            var todos = grupos.Where(g => g.Calculo != null).SelectMany(g => g.Calculo.Verificacoes).ToList();
            var geral = grupos.Any(g => g.Base != null) ? grupos.Where(g => g.Base != null).Max(g => g.Situacao) : (SituacaoVerificacao?)null;
            var cab = UI.Estilo.Cabecalho("Vigas pré-moldadas — cálculo e armação",
                $"{grupos.Sum(g => g.Quantidade)} vigas em {grupos.Count} grupo(s) de peças iguais · concreto armado, biapoiadas (NBR 6118 e NBR 9062)",
                UI.Estilo.Selo(geral, 110, 11));
            DockPanel.SetDock(cab, Dock.Top); raiz.Children.Add(cab);

            // ---- rodapé ----
            var rodape = new Border { Background = UI.Estilo.B(UI.Estilo.Branco), BorderBrush = UI.Estilo.B(UI.Estilo.Linha), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(14, 8, 14, 8) };
            var rp = new DockPanel();
            var botoes = new StackPanel { Orientation = Orientation.Horizontal };
            var ok = UI.Estilo.Primario("Armar no Revit", () => DialogResult = true);
            ok.IsDefault = true; ok.Margin = new Thickness(0, 0, 8, 0); ok.Padding = new Thickness(18, 5, 18, 5);
            botoes.Children.Add(ok);
            var cancel = UI.Estilo.Botao("Cancelar", () => DialogResult = false); cancel.IsCancel = true; cancel.Padding = new Thickness(14, 5, 14, 5);
            botoes.Children.Add(cancel);
            DockPanel.SetDock(botoes, Dock.Right); rp.Children.Add(botoes);
            var esq = new StackPanel { Orientation = Orientation.Horizontal };
            esq.Children.Add(_vistas);
            esq.Children.Add(new TextBlock { Text = "As cargas digitadas ficam gravadas nas vigas (parâmetros PCEB_ de Análise estrutural). A folha fica com você.", Foreground = UI.Estilo.B(UI.Estilo.Suave), VerticalAlignment = VerticalAlignment.Center });
            rp.Children.Add(esq);
            rodape.Child = rp;
            DockPanel.SetDock(rodape, Dock.Bottom); raiz.Children.Add(rodape);

            var corpo = new Grid { Margin = new Thickness(12, 10, 12, 6) };
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });

            // ---- esquerda: grupos ----
            var left = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
            var head = new TextBlock { Text = "GRUPOS DE PEÇAS IGUAIS", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = UI.Estilo.B(UI.Estilo.Suave), Margin = new Thickness(0, 4, 0, 8) };
            DockPanel.SetDock(head, Dock.Top); left.Children.Add(head);
            string nt = string.Join("\n", notas ?? Enumerable.Empty<string>());
            if (nt.Length > 0)
            {
                var t = new Border
                {
                    Background = UI.Estilo.B(UI.Estilo.Hex("#fff4dc")), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 8),
                    Child = new TextBlock { Text = nt, TextWrapping = TextWrapping.Wrap, Foreground = UI.Estilo.B(UI.Estilo.Ocre), FontSize = 11 }, MaxHeight = 140,
                };
                DockPanel.SetDock(t, Dock.Top); left.Children.Add(t);
            }
            var cont = new TextBlock { Text = todos.Count > 0 ? "Verificações: " + UI.Estilo.Contagem(todos) : "", Foreground = UI.Estilo.B(UI.Estilo.Suave), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            DockPanel.SetDock(cont, Dock.Bottom); left.Children.Add(cont);
            _lista.SelectionChanged += (s, e) => Carregar();
            left.Children.Add(_lista);
            Grid.SetColumn(left, 0); corpo.Children.Add(left);

            // ---- centro: título do grupo e abas ----
            var centro = new DockPanel();
            var linhaTit = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            linhaTit.Children.Add(_seloGrupo); linhaTit.Children.Add(_titulo); linhaTit.Children.Add(_subtitulo);
            DockPanel.SetDock(linhaTit, Dock.Top); centro.Children.Add(linhaTit);
            var leg = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var (txt, cor) in VigaPreview.Legenda)
            {
                leg.Children.Add(new Border { Width = 18, Height = 3, Background = new SolidColorBrush(cor), Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                leg.Children.Add(new TextBlock { Text = txt, Foreground = UI.Estilo.B(UI.Estilo.Suave), FontSize = 11 });
            }
            var abaArm = new DockPanel();
            DockPanel.SetDock(leg, Dock.Bottom); abaArm.Children.Add(leg);
            abaArm.Children.Add(Moldura(_previa));
            _abas.Items.Add(new TabItem { Header = "Armação", Content = abaArm });
            _abas.Items.Add(new TabItem { Header = "Esforços (ELU)", Content = _esforcos });
            _abas.Items.Add(new TabItem { Header = "Longitudinal", Content = _longitudinal });
            _abas.Items.Add(new TabItem { Header = "Transversal", Content = _transversal });
            _abas.Items.Add(new TabItem { Header = "Dente, içamento e ELS", Content = _dente });
            _abas.Items.Add(new TabItem { Header = "Verificações", Content = _verificacoes });
            centro.Children.Add(_abas);
            Grid.SetColumn(centro, 1); corpo.Children.Add(centro);

            // ---- direita: materiais, cargas e a armadura proposta ----
            var right = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            var mat = new StackPanel();
            mat.Children.Add(UI.Estilo.Campo("fck", _fck, "MPa", 130));
            mat.Children.Add(UI.Estilo.Campo("Classe de agressividade", _classe, "", 130));
            mat.Children.Add(UI.Estilo.Campo("Cobrimento", _cob, "cm", 130));
            mat.Children.Add(UI.Estilo.Botao("Aplicar e recalcular todos", () => { AplicarMateriais(); RecalcularTodos(); }, new Thickness(0, 6, 0, 0)));
            right.Children.Add(UI.Estilo.Cartao("Materiais (todos os grupos)", mat));
            var g0 = grupos.FirstOrDefault(x => x.Base != null);
            _fck.Text = g0?.Base["Fck"] ?? "40"; _classe.SelectedItem = g0 != null && Classes.Contains(g0.Base["ClasseAgressividade"]) ? g0.Base["ClasseAgressividade"] : "II";
            _cob.Text = g0?.Base["CobFundo"] ?? "2.5";

            var cg = new StackPanel();
            cg.Children.Add(new TextBlock { Text = "Características, além do peso próprio.", Foreground = UI.Estilo.B(UI.Estilo.Suave), FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
            cg.Children.Add(UI.Estilo.Campo("Permanente g", _g, "kN/m", 130));
            cg.Children.Add(UI.Estilo.Campo("Variável q", _q, "kN/m", 130));
            cg.Children.Add(UI.Estilo.Campo("Concentradas", _conc, "x: G+Q", 130));
            cg.Children.Add(UI.Estilo.Campo("Apoio esquerdo", _apE, "cm", 130));
            cg.Children.Add(UI.Estilo.Campo("Apoio direito", _apD, "cm", 130));
            cg.Children.Add(new TextBlock { Text = "Apoio 0 = automático (furo do pino, meio do dente ou 10 cm).", Foreground = UI.Estilo.B(UI.Estilo.Concreto), FontSize = 10.5, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
            var bts = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            bts.Children.Add(UI.Estilo.Primario("Calcular este grupo", () => { LerCargas(); Recalcular(Atual); Carregar(); }));
            bts.Children.Add(UI.Estilo.Botao("Copiar para todos", CopiarCargas, new Thickness(6, 0, 0, 0)));
            cg.Children.Add(bts);
            right.Children.Add(UI.Estilo.Cartao("Cargas do grupo", cg));

            var res = new StackPanel();
            res.Children.Add(_resultado);
            res.Children.Add(UI.Estilo.Botao("Abrir relatório de cálculo (HTML)", AbrirRelatorio, new Thickness(0, 8, 0, 0)));
            right.Children.Add(UI.Estilo.Cartao("Armadura proposta", res));
            var sv = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetColumn(sv, 2); corpo.Children.Add(sv);

            raiz.Children.Add(corpo);
            Content = raiz;

            foreach (GrupoViga g in grupos) _lista.Items.Add(new ListBoxItem { Tag = g, Content = Item(g) });
            if (_lista.Items.Count > 0) _lista.SelectedIndex = 0;
        }

        // ------------------------------------------------------------------

        private static Border Moldura(UIElement e) => new Border { BorderBrush = UI.Estilo.B(UI.Estilo.Linha), BorderThickness = new Thickness(1), Background = Brushes.White, Child = e, CornerRadius = new CornerRadius(4) };

        private static UIElement Item(GrupoViga g)
        {
            var dp = new DockPanel();
            var cb = new CheckBox { IsChecked = g.Selecionado && g.Base != null, IsEnabled = g.Base != null, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 8, 0) };
            cb.Checked += (s, e) => g.Selecionado = true; cb.Unchecked += (s, e) => g.Selecionado = false;
            DockPanel.SetDock(cb, Dock.Left); dp.Children.Add(cb);
            var tx = new StackPanel();
            var l1 = new DockPanel { LastChildFill = true };
            var selo = UI.Estilo.Selo(g.Base == null ? (SituacaoVerificacao?)null : g.Situacao, 74, 9.5);
            DockPanel.SetDock(selo, Dock.Right); l1.Children.Add(selo);
            l1.Children.Add(new TextBlock { Text = $"{g.Marca}  ×{g.Quantidade}", FontWeight = FontWeights.SemiBold, FontSize = 13 });
            tx.Children.Add(l1);
            tx.Children.Add(new TextBlock { Text = $"{g.Secao} cm · L = {Fmt(g.Peca?.L ?? 0)} cm · {g.Pontas}", Foreground = UI.Estilo.B(UI.Estilo.Suave), FontSize = 11 });
            if (g.Calculo?.Inferior != null)
                tx.Children.Add(new TextBlock { Text = $"inferior {g.Calculo.Inferior.Texto} · ø{Fmt(g.Calculo.EstBitola, "0.#")} c/{Fmt(g.Calculo.EspPonta, "0.#")}/{Fmt(g.Calculo.EspVao, "0.#")}", Foreground = UI.Estilo.B(UI.Estilo.Suave), FontSize = 11 });
            dp.Children.Add(tx);
            return dp;
        }

        private void AtualizarItem(GrupoViga g)
        {
            foreach (ListBoxItem it in _lista.Items) if (it.Tag == g) it.Content = Item(g);
        }

        private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

        private void Carregar()
        {
            var g = Atual;
            if (g == null) return;
            _carregando = true;
            _g.Text = Fmt(g.Cargas.G); _q.Text = Fmt(g.Cargas.Q); _conc.Text = CargasViga.TextoConcentradas(g.Cargas.Concentradas);
            _apE.Text = Fmt(g.Cargas.ApoioE); _apD.Text = Fmt(g.Cargas.ApoioD);
            _carregando = false;
            _titulo.Text = $"{g.Marca} — {g.Quantidade} peça(s)";
            _subtitulo.Text = $"{g.Secao} cm · L = {Fmt(g.Peca?.L ?? 0)} cm · pontas {g.Pontas}";
            _seloGrupo.Content = UI.Estilo.Selo(g.Base == null ? (SituacaoVerificacao?)null : g.Situacao, 96, 11);
            _resultado.Children.Clear();
            foreach (var cc in new[] { _esforcos, _longitudinal, _transversal, _dente, _verificacoes }) cc.Content = null;
            if (g.Base == null)
            {
                _resultado.Children.Add(new TextBlock { Text = "Grupo fora do cálculo: " + string.Join(" ", g.Avisos), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick });
                _previa.Mostrar(null, null);
                return;
            }
            var c = g.Calculo;
            var av = new List<string>();
            List<BarDef> bars = null;
            try { bars = VigasPreMoldadas.Barras(g, g.Representante, av); } catch (Exception ex) { av.Add(ex.Message); }
            _previa.Mostrar(g, bars);
            if (c == null) return;

            // armadura proposta (direita)
            var props = c.ResumoProposta().Select(l => { int i = l.IndexOf(':'); return i > 0 ? (l.Substring(0, i), l.Substring(i + 1).Trim()) : ("", l); }).ToList();
            _resultado.Children.Add(UI.Estilo.Pares(props, 80, true));
            _resultado.Children.Add(new Border { Height = 1, Background = UI.Estilo.B(UI.Estilo.Linha), Margin = new Thickness(0, 8, 0, 6) });
            _resultado.Children.Add(UI.Estilo.Pares(new[]
            {
                ("Vão de cálculo", $"{Fmt(c.Vao, "0.#")} cm"),
                ("Md,máx", $"{Fmt(c.MdMax, "0.0")} kN·m  (MRd = {Fmt(c.MRd, "0.0")})"),
                ("Vd (E / D)", $"{Fmt(c.VdE, "0.0")} / {Fmt(c.VdD, "0.0")} kN"),
                ("Flecha total", $"{Fmt(c.FlechaTotal, "0.00")} cm" + (c.Contraflecha > 0 ? $"  (contraflecha {Fmt(c.Contraflecha, "0.0")})" : "")),
                ("Verificações", UI.Estilo.Contagem(c.Verificacoes)),
            }, 100));
            foreach (string a in c.Avisos.Concat(g.Avisos).Concat(av.Distinct().Select(x => "Revit: " + x)))
                _resultado.Children.Add(new TextBlock { Text = "• " + a, TextWrapping = TextWrapping.Wrap, Foreground = UI.Estilo.B(UI.Estilo.Ocre), FontSize = 11, Margin = new Thickness(0, 4, 0, 0) });

            _esforcos.Content = AbaEsforcos(c);
            _longitudinal.Content = AbaLongitudinal(c);
            _transversal.Content = AbaTransversal(c);
            _dente.Content = AbaVerificacoes(c.Verificacoes.Where(v => !Longitudinal(v) && !Transversal(v)), "Dente Gerber, içamento, fissuração e flecha");
            _verificacoes.Content = AbaVerificacoes(c.Verificacoes, "Todas as verificações do grupo");
        }

        private static bool Longitudinal(ResultadoVerificacao v) => v.Grupo.EndsWith("Flexão") || v.Grupo.EndsWith("Ancoragem");
        private static bool Transversal(ResultadoVerificacao v) => v.Grupo.EndsWith("Cisalhamento");

        // ---------------- abas ----------------

        private static List<(double X, double Y)> NoVao(ResultadoCalculoViga c, Func<(double X, double Md, double Vd), double> f) =>
            c.Diagrama.Where(p => p.X >= c.ApoioE - 1e-6 && p.X <= c.L - c.ApoioD + 1e-6).Select(p => (p.X, f(p))).ToList();

        private static Grafico NovoGrafico(ResultadoCalculoViga c, string titulo, string unidade, bool paraBaixo, double altura)
        {
            var gr = new Grafico { Titulo = titulo, RotuloY = unidade, YParaBaixo = paraBaixo, Height = altura, Margin = new Thickness(0, 0, 0, 8) };
            gr.Marcas.Add((c.ApoioE, "apoio"));
            gr.Marcas.Add((c.L - c.ApoioD, "apoio"));
            return gr;
        }

        private UIElement Rolar(UIElement e) => new ScrollViewer { Content = e, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 4, 6, 0) };

        private UIElement AbaEsforcos(ResultadoCalculoViga c)
        {
            var sp = new StackPanel();
            var md = NovoGrafico(c, "Momento fletor de cálculo Md", "kN·m", true, 230);
            md.Series.Add(new SerieGrafico { Nome = "Md", Pontos = NoVao(c, p => p.Md), Cor = UI.Estilo.Anil, Preencher = true, RotularMaximo = true, Unidade = "kN·m" });
            var vd = NovoGrafico(c, "Força cortante de cálculo Vd", "kN", false, 230);
            vd.Series.Add(new SerieGrafico { Nome = "Vd", Pontos = NoVao(c, p => p.Vd), Cor = UI.Estilo.Hex("#c06a1f"), Preencher = true, RotularPontas = true, Unidade = "kN" });
            sp.Children.Add(Moldura(md)); sp.Children.Add(new Border { Height = 8 }); sp.Children.Add(Moldura(vd));
            var t = UI.Estilo.Pares(new[]
            {
                ("Vão de cálculo", $"{Fmt(c.Vao, "0.#")} cm (apoios a {Fmt(c.ApoioE, "0.#")} e {Fmt(c.ApoioD, "0.#")} cm das pontas)"),
                ("Peso próprio g1", $"{Fmt(c.G1, "0.00")} kN/m" + (c.GCapa > 0 ? $"; capa {Fmt(c.GCapa, "0.00")} kN/m" : "")),
                ("Cargas digitadas", $"g = {Fmt(c.Cargas.G)} kN/m; q = {Fmt(c.Cargas.Q)} kN/m" + (c.Cargas.Concentradas.Count > 0 ? "; concentradas " + CargasViga.TextoConcentradas(c.Cargas.Concentradas) : "")),
                ("Coeficientes", $"γg = {Fmt(c.Coef.GamaG)}; γq = {Fmt(c.Coef.GamaQ)}; ψ1 = {Fmt(c.Coef.Psi1)}; ψ2 = {Fmt(c.Coef.Psi2)}"),
                ("Md,máx", $"{Fmt(c.MdMax, "0.0")} kN·m em x = {Fmt(c.XMdMax, "0")} cm"),
                ("Vd nos apoios", $"esquerdo {Fmt(c.VdE, "0.0")} kN; direito {Fmt(c.VdD, "0.0")} kN"),
                ("Reações características", $"esquerda G {Fmt(c.RkE_G, "0.0")} + Q {Fmt(c.RkE_Q, "0.0")} kN; direita G {Fmt(c.RkD_G, "0.0")} + Q {Fmt(c.RkD_Q, "0.0")} kN"),
            }, 160);
            sp.Children.Add(new Border { Height = 8 });
            sp.Children.Add(UI.Estilo.Cartao("Resumo dos esforços", t));
            return Rolar(sp);
        }

        private UIElement AbaLongitudinal(ResultadoCalculoViga c)
        {
            var sp = new StackPanel();
            var gr = NovoGrafico(c, "Md decalado × MRd da armadura proposta", "kN·m", true, 300);
            var md = NoVao(c, p => p.Md);
            // decalagem: o diagrama "deslocado" de aℓ para os lados (envoltória de Md(x ± aℓ)), NBR 6118 17.4.2.2 c e 18.3.2.3.1
            var dec = md.Select(p => (p.X, md.Where(q => Math.Abs(q.X - p.X) <= c.Al + 1e-6).Max(q => q.Y))).ToList();
            gr.Series.Add(new SerieGrafico { Nome = "Md", Pontos = md, Cor = UI.Estilo.Anil, Preencher = true });
            gr.Series.Add(new SerieGrafico { Nome = $"Md decalado (aℓ = {Fmt(c.Al, "0.0")} cm)", Pontos = dec, Cor = UI.Estilo.Anil, Tracejada = true, Espessura = 1.4 });
            if (c.MRd > 0)
                gr.Series.Add(new SerieGrafico { Nome = "MRd", Pontos = new List<(double, double)> { (c.ApoioE, c.MRd), (c.L - c.ApoioD, c.MRd) }, Cor = UI.Estilo.Hex("#1f7a3f"), Espessura = 2.2, RotularPontas = false, RotularMaximo = true, Unidade = "kN·m" });
            sp.Children.Add(Moldura(gr));
            sp.Children.Add(new TextBlock
            {
                Text = "Toda a armadura inferior vai de apoio a apoio (nenhuma barra é cortada no vão), então o MRd cobre o diagrama decalado em todo o vão. " +
                       "A decalagem entra também na força a ancorar nos apoios (verificação K8).",
                TextWrapping = TextWrapping.Wrap, Foreground = UI.Estilo.B(UI.Estilo.Suave), FontSize = 11, Margin = new Thickness(2, 6, 2, 8),
            });
            var inf = c.Inferior;
            sp.Children.Add(UI.Estilo.Cartao("Flexão no ELU", UI.Estilo.Pares(new[]
            {
                ("As,nec / As,mín", $"{Fmt(c.AsNec, "0.00")} / {Fmt(c.AsMin, "0.00")} cm²"),
                ("Proposta", inf == null ? "—" : $"{inf.Texto} = {Fmt(inf.Area, "0.00")} cm² (d = {Fmt(inf.D, "0.0")} cm)"),
                ("MRd / Md,máx", $"{Fmt(c.MRd, "0.0")} / {Fmt(c.MdMax, "0.0")} kN·m = {Fmt(c.MdMax > 0 ? c.MRd / c.MdMax : 0, "0.00")}"),
                ("Armadura de compressão", c.AsComprNec > 0 ? $"As' = {Fmt(c.AsComprNec, "0.00")} cm²" : "não precisa (x/d dentro do limite)"),
                ("Superior / pele", $"{c.Superior.N} ø{Fmt(c.Superior.Bitola, "0.#")}" + (c.Pele.NPorFace > 0 ? $"; pele {c.Pele.NPorFace} ø{Fmt(c.Pele.Bitola, "0.#")} por face" : "")),
            }, 170)));
            sp.Children.Add(UI.Estilo.Cartao("Verificações da armadura longitudinal", UI.Estilo.TabelaVerificacoes(c.Verificacoes.Where(Longitudinal), false)));
            return Rolar(sp);
        }

        private UIElement AbaTransversal(ResultadoCalculoViga c)
        {
            var sp = new StackPanel();
            var gr = NovoGrafico(c, "|Vd| × VRd3 dos estribos propostos e VRd2", "kN", false, 300);
            gr.Series.Add(new SerieGrafico { Nome = "|Vd|", Pontos = NoVao(c, p => Math.Abs(p.Vd)), Cor = UI.Estilo.Hex("#c06a1f"), Preencher = true, RotularMaximo = true, Unidade = "kN" });
            double xa = c.ApoioE, xb = c.L - c.ApoioD, ze = Math.Max(xa, Math.Min(c.XZonaE, xb)), zd = Math.Min(xb, Math.Max(c.XZonaD, xa));
            var vr3 = new List<(double, double)> { (xa, c.VRd3Ponta), (ze, c.VRd3Ponta), (ze, c.VRd3Vao), (zd, c.VRd3Vao), (zd, c.VRd3Ponta), (xb, c.VRd3Ponta) };
            gr.Series.Add(new SerieGrafico { Nome = "VRd3", Pontos = vr3, Cor = UI.Estilo.Hex("#1f7a3f"), Espessura = 2.2 });
            // VRd2 muito acima (viga folgada nas bielas) achataria o gráfico: só entra quando está perto dos outros valores
            if (c.VRd2 < 2.5 * Math.Max(c.VRd3Ponta, Math.Max(c.VdE, c.VdD)))
                gr.Series.Add(new SerieGrafico { Nome = "VRd2", Pontos = new List<(double, double)> { (xa, c.VRd2), (xb, c.VRd2) }, Cor = UI.Estilo.Hex("#a8221a"), Tracejada = true, Espessura = 1.4, RotularMaximo = true, Unidade = "kN" });
            gr.Titulo = c.VRd2 < 2.5 * Math.Max(c.VRd3Ponta, Math.Max(c.VdE, c.VdD)) ? gr.Titulo : $"|Vd| × VRd3 dos estribos propostos (VRd2 = {Fmt(c.VRd2, "0")} kN, fora da escala)";
            sp.Children.Add(Moldura(gr));
            sp.Children.Add(new Border { Height = 8 });
            sp.Children.Add(UI.Estilo.Cartao("Cisalhamento no ELU — modelo I", UI.Estilo.Pares(new[]
            {
                ("Estribos", $"ø{Fmt(c.EstBitola, "0.#")} c/{Fmt(c.EspPonta, "0.#")} nas pontas" + (c.ZonaPonta > 0 ? $" ({Fmt(c.ZonaPonta, "0")} cm)" : "") + $" e c/{Fmt(c.EspVao, "0.#")} no vão"),
                ("Vc (concreto)", $"{Fmt(c.Vc, "0.0")} kN"),
                ("VRd3 pontas / vão", $"{Fmt(c.VRd3Ponta, "0.0")} / {Fmt(c.VRd3Vao, "0.0")} kN"),
                ("VRd2 (bielas)", $"{Fmt(c.VRd2, "0.0")} kN — Vd/VRd2 = {Fmt(c.VdSobreVRd2, "0.00")}"),
            }, 170)));
            sp.Children.Add(UI.Estilo.Cartao("Verificações da armadura transversal", UI.Estilo.TabelaVerificacoes(c.Verificacoes.Where(Transversal), false)));
            return Rolar(sp);
        }

        private UIElement AbaVerificacoes(IEnumerable<ResultadoVerificacao> itens, string titulo)
        {
            var l = itens.ToList();
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = $"{titulo}: {UI.Estilo.Contagem(l)}", Foreground = UI.Estilo.B(UI.Estilo.Suave), Margin = new Thickness(2, 0, 0, 8) });
            sp.Children.Add(UI.Estilo.Cartao(null, UI.Estilo.TabelaVerificacoes(l)));
            return Rolar(sp);
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

        /// <summary>
        /// Renderiza a janela em PNG (sem exibi-la) — teste automático: uma imagem por aba (<paramref name="path"/> = a da 1ª aba;
        /// as outras ganham _2, _3...). Com a sessão do Windows bloqueada o WPF não desenha (imagem vazia).
        /// </summary>
        public void RenderToPng(string path, int width = 1500, int height = 860)
        {
            var root = (FrameworkElement)Content;
            Diagnostico = "";
            for (int t = 0; t < Math.Max(1, _abas.Items.Count); t++)
            {
                if (_abas.Items.Count > 0) _abas.SelectedIndex = t;
                root.Measure(new Size(width, height));
                root.Arrange(new Rect(0, 0, width, height));
                root.UpdateLayout();
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                var bg = new DrawingVisual();
                using (DrawingContext dc = bg.RenderOpen()) dc.DrawRectangle(Background ?? SystemColors.ControlBrush, null, new Rect(0, 0, width, height));
                bmp.Render(bg);
                int Px() { var px = new byte[4]; bmp.CopyPixels(new Int32Rect(2, 2, 1, 1), px, 4, 0); return px[3]; }
                if (t == 0) Diagnostico = $"fundo alfa {Px()}";
                try { bmp.Render(root); if (t == 0) Diagnostico += $"; janela alfa {Px()}; tamanho {root.ActualWidth}x{root.ActualHeight}"; }
                catch (Exception ex) { Diagnostico += "; erro " + ex; }
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                string arq = t == 0 ? path : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), System.IO.Path.GetFileNameWithoutExtension(path) + "_" + (t + 1) + ".png");
                using (var fs = System.IO.File.Create(arq)) enc.Save(fs);
            }
            if (_abas.Items.Count > 0) _abas.SelectedIndex = 0;
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
            ("Alça CP-190", WColor.FromRgb(0x8E, 0x44, 0xAD)), ("Aba e ressalto", WColor.FromRgb(0x0E, 0x8A, 0x8A)),
        };

        private static Pen Caneta(BarRole r, double esp = 1)
        {
            WColor c = r switch
            {
                BarRole.Longitudinal or BarRole.BeamWelded => Legenda[0].Item2,
                BarRole.Stirrup => Legenda[1].Item2,
                BarRole.BeamGerber or BarRole.BeamHanger => Legenda[2].Item2,
                BarRole.LiftLoop => Legenda[4].Item2,
                BarRole.BeamLedge or BarRole.BeamLedgeHairpin or BarRole.BeamLedgeBar or BarRole.BeamUpstand or BarRole.BeamUpstandBar
                    or BarRole.BeamTopping => Legenda[5].Item2,
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

            var pt = CultureInfo.GetCultureInfo("pt-BR");
            string Nf(double x, string f = "0.#") => x.ToString(f, pt);
            var c = _g.Calculo;
            var tracejada = new Pen(Brushes.Gray, 0.8) { DashStyle = DashStyles.Dash }; tracejada.Freeze();
            var lixo = new List<string>();
            var exE = TemplateVigaPreMoldada.LerExtremidade(v, true, TemplateVigaPreMoldada.CatalogoDentes, lixo);
            var exD = TemplateVigaPreMoldada.LerExtremidade(v, false, TemplateVigaPreMoldada.CatalogoDentes, lixo);
            var aba = TemplateVigaPreMoldada.LerAba(v, h, L, exE, exD, null);
            var rs = TemplateVigaPreMoldada.LerRessalto(v, b, h, null);
            double hr = rs.Existe ? rs.Altura : 0;

            // ---- elevação (faixa de cima): espaço para as alças acima da viga; escala vertical ampliada ----
            double z0 = _g.AlmaZ0, zTopoBarras = _bars == null || _bars.Count == 0 ? z0 + h : _bars.SelectMany(x => x.Points).Max(p => Cm(p.C));
            double acima = Math.Max(hr, zTopoBarras - (z0 + h));
            double mx = 24, larg = W - 2 * mx - 40, esc = larg / L;
            double hElev = Math.Min(h * esc * 2.2, H * 0.34), escV = hElev / h;
            if (acima * escV > 60) escV = 60 / Math.Max(acima, 1);
            hElev = h * escV;
            double yTop = 30 + acima * escV, yBot = yTop + hElev;
            WPoint E(double x, double z) => new WPoint(mx + x * esc, yTop + (z0 + h - z) * escV);
            Txt("ELEVAÇÃO", mx, 6, 12, Brushes.DimGray);
            Txt("escala vertical ampliada " + Nf(escV / esc, "0.#") + "×", W - mx - 190, 8, 10, Brushes.Gray);
            var contorno = new StreamGeometry();
            using (var sg = contorno.Open())
            {
                double hdE = exE.TemDente ? exE.Dente.Altura : h, cdE = exE.TemDente ? exE.CompDente : 0, hdD = exD.TemDente ? exD.Dente.Altura : h, cdD = exD.TemDente ? exD.CompDente : 0;
                sg.BeginFigure(E(0, z0 + h - hdE), true, true);
                sg.LineTo(E(cdE, z0 + h - hdE), true, false); sg.LineTo(E(cdE, z0), true, false);
                sg.LineTo(E(L - cdD, z0), true, false); sg.LineTo(E(L - cdD, z0 + h - hdD), true, false);
                sg.LineTo(E(L, z0 + h - hdD), true, false); sg.LineTo(E(L, z0 + h + hr), true, false); sg.LineTo(E(0, z0 + h + hr), true, false);
            }
            contorno.Freeze();
            dc.DrawGeometry(new SolidColorBrush(WColor.FromRgb(0xF4, 0xF4, 0xF4)), forma, contorno);
            if (hr > 0) dc.DrawLine(fina, E(0, z0 + h), E(L, z0 + h));                       // topo da alma (o ressalto em cima)
            if (aba.Existe)                                                                   // aba: topo e fundo, tracejados
            {
                dc.DrawLine(tracejada, E(aba.Ini, z0 + aba.Base + aba.Altura), E(aba.Fim, z0 + aba.Base + aba.Altura));
                if (aba.Base > 0) dc.DrawLine(tracejada, E(aba.Ini, z0 + aba.Base), E(aba.Fim, z0 + aba.Base));
            }
            if (_bars != null)
                foreach (BarDef bd in _bars.OrderBy(x => x.Normal.A != 0 ? 0 : 1))      // estribos por baixo, longitudinais por cima
                    foreach (var inst in bd.Instances())
                    {
                        var pts = inst.Select(p => E(Cm(p.A), Cm(p.C))).ToList();
                        // U horizontais (grampo e costura do dente) e a barra soldada: vistos de lado, um traço de ponta a ponta
                        if (bd.Role == BarRole.BeamHairpin || bd.Role == BarRole.BeamStitch || bd.Role == BarRole.BeamWelded)
                            pts = new List<WPoint> { new WPoint(pts.Min(q => q.X), pts[0].Y), new WPoint(pts.Max(q => q.X), pts[0].Y) };
                        bool plano = bd.Normal.A != 0 && bd.Role != BarRole.BeamWelded;
                        var pen = Caneta(bd.Role, plano ? 0.7 : 1.4);
                        for (int i = 0; i + 1 < pts.Count; i++) dc.DrawLine(pen, pts[i], pts[i + 1]);
                        if (bd.Closed && pts.Count > 2) dc.DrawLine(pen, pts[^1], pts[0]);
                        if (bd.ArcMid.Count > 0) foreach (var kv in bd.ArcMid) { var m = E(Cm(kv.Value.A), Cm(kv.Value.C)); dc.DrawLine(pen, pts[kv.Key], m); dc.DrawLine(pen, m, pts[kv.Key + 1]); }
                    }
            // apoios e cotas
            if (c != null)
                foreach (double xa in new[] { c.ApoioE, L - c.ApoioD })
                {
                    var p0 = E(xa, z0);
                    var tri = new StreamGeometry();
                    using (var sg = tri.Open()) { sg.BeginFigure(p0, true, true); sg.LineTo(new WPoint(p0.X - 6, p0.Y + 10), true, false); sg.LineTo(new WPoint(p0.X + 6, p0.Y + 10), true, false); }
                    tri.Freeze(); dc.DrawGeometry(Brushes.Black, null, tri);
                }
            double yCota = yBot + 20;
            dc.DrawLine(fina, new WPoint(E(0, z0).X, yCota), new WPoint(E(L, z0).X, yCota));
            foreach (double xx in new[] { 0.0, L }) dc.DrawLine(fina, new WPoint(E(xx, z0).X, yCota - 4), new WPoint(E(xx, z0).X, yCota + 4));
            Txt($"L = {Nf(L)} cm" + (c != null ? $"   (vão de cálculo {Nf(c.Vao)} cm)" : ""), mx + larg / 2, yCota + 2, 11, Brushes.DimGray, true);
            double xh = E(L, z0).X + 10;
            dc.DrawLine(fina, new WPoint(xh, E(L, z0 + h).Y), new WPoint(xh, E(L, z0).Y));
            Txt($"h = {Nf(h)}", xh + 3, (E(L, z0 + h).Y + E(L, z0).Y) / 2 - 7, 10, Brushes.DimGray);

            // ---- embaixo: seção no dente (ou junto ao apoio) e seção no meio do vão, lado a lado ----
            double yBaixo = yCota + 30, hBaixo = H - yBaixo - 10;
            double xDente = exE.TemDente ? exE.CompDente / 2 : Math.Min(L / 4, 30);
            var secoes = new[] { (xDente, exE.TemDente ? "A — SEÇÃO NO DENTE" : "A — SEÇÃO JUNTO AO APOIO"), (L / 2, "B — SEÇÃO NO MEIO DO VÃO") };
            // a mesma escala nas duas seções (a maior forma manda)
            double bMax = b + (aba.Existe ? aba.LargE + aba.LargD : 0), hMax = h + hr - (aba.Existe ? Math.Min(0, aba.Base) : 0);
            double larguraCol = (W - 2 * mx) / 2;
            double escS = Math.Min((hBaixo - 64) / hMax, (larguraCol - 120) / bMax);
            // trecho da peça em x: fundo do concreto e aba (a seção só mostra barras planas do mesmo trecho)
            (double, bool) Trecho(double x) => (exE.TemDente && x < exE.CompDente ? 1 : exD.TemDente && x > L - exD.CompDente ? 2 : 0, aba.Em(x));
            for (int k = 0; k < secoes.Length; k++)
            {
                var (xm, titulo) = secoes[k];
                double zb = z0;
                if (exE.TemDente && xm < exE.CompDente) zb = z0 + h - exE.Dente.Altura;
                if (exD.TemDente && xm > L - exD.CompDente) zb = z0 + h - exD.Dente.Altura;
                AbaViga ab = aba.Em(xm) ? (zb > z0 ? aba.NoDente(zb - z0) : aba) : null;
                double hSec = z0 + h - zb, le = ab?.LargE ?? 0, ld = ab?.LargD ?? 0, bTot = b + le + ld;
                double colX = mx + k * larguraCol;
                double sx0 = colX + (larguraCol - 60 - bTot * escS) / 2, sy1 = yBaixo + 26;
                Txt(titulo, colX, yBaixo, 12, Brushes.DimGray);
                WPoint S(double y, double z) => new WPoint(sx0 + (_g.AlmaY0 + b - y + le) * escS, sy1 + (z0 + h + hr - z) * escS);
                var cont = TemplateVigaPreMoldada.ContornoSecao(b, hSec, ab, rs.Existe ? rs : null, 0, 0);
                var geoS = new StreamGeometry();
                using (var sg = geoS.Open())
                {
                    sg.BeginFigure(S(_g.AlmaY0 + b - cont[0].X, zb + cont[0].Y), true, true);
                    foreach (var q in cont.Skip(1)) sg.LineTo(S(_g.AlmaY0 + b - q.X, zb + q.Y), true, false);
                }
                geoS.Freeze();
                dc.DrawGeometry(new SolidColorBrush(WColor.FromRgb(0xF4, 0xF4, 0xF4)), forma, geoS);
                if (_bars != null)
                {
                    // barras planas: de cada papel e lado, a mais perto do corte
                    var perto = new Dictionary<(BarRole, int), (double Dist, BarDef Bd, List<Loc> Inst)>();
                    foreach (var bd in _bars.Where(x => x.Normal.A != 0 && x.Role != BarRole.BeamWelded))
                        foreach (var inst in bd.Instances())
                        {
                            double dd = Math.Abs(Cm(inst[0].A) - xm);
                            var chave = (bd.Role, Math.Sign(Math.Round(inst.Average(p => Cm(p.B)) - (_g.AlmaY0 + b / 2))));
                            double xi = Cm(inst[0].A);
                            if (dd < 30 && Trecho(xi) == Trecho(xm) && (!perto.TryGetValue(chave, out var o) || dd < o.Dist)) perto[chave] = (dd, bd, inst);
                        }
                    foreach (var (_, bd, inst) in perto.Values)
                    {
                        var pts = inst.Select(p => S(Cm(p.B), Cm(p.C))).ToList();
                        var pen = Caneta(bd.Role, 1.4);
                        for (int i = 0; i + 1 < pts.Count; i++) dc.DrawLine(pen, pts[i], pts[i + 1]);
                        if (bd.Closed) dc.DrawLine(pen, pts[^1], pts[0]);
                    }
                    // barras cortadas (e as pernas dos U horizontais do dente)
                    foreach (var bd in _bars.Where(x => x.Normal.A == 0 && x.Role != BarRole.LiftLoop))
                        foreach (var inst in bd.Instances())
                            for (int i = 0; i + 1 < inst.Count; i++)
                            {
                                double a0 = Cm(inst[i].A), a1 = Cm(inst[i + 1].A);
                                if (Math.Abs(a1 - a0) < 1e-6 || xm < Math.Min(a0, a1) || xm > Math.Max(a0, a1)) continue;
                                double t = (xm - a0) / (a1 - a0);
                                var ctr = S(Cm(inst[i].B) + t * (Cm(inst[i + 1].B) - Cm(inst[i].B)), Cm(inst[i].C) + t * (Cm(inst[i + 1].C) - Cm(inst[i].C)));
                                double r = Math.Max(2.5, bd.DiameterMm / 20 * escS);
                                dc.DrawEllipse(Caneta(bd.Role).Brush, null, ctr, r, r);
                            }
                }
                // cotas: largura embaixo, altura à direita
                double ySecB = S(0, zb).Y + 10, xSecD = sx0 + bTot * escS + 10, yTopoS = S(0, z0 + h + hr).Y;
                dc.DrawLine(fina, new WPoint(sx0, ySecB), new WPoint(sx0 + bTot * escS, ySecB));
                Txt(ab != null ? $"{Nf(bTot)} (alma {Nf(b)})" : Nf(b), sx0 + bTot * escS / 2, ySecB + 1, 10, Brushes.DimGray, true);
                dc.DrawLine(fina, new WPoint(xSecD, yTopoS), new WPoint(xSecD, S(0, zb).Y));
                Txt(Nf(hSec + hr), xSecD + 3, (yTopoS + S(0, zb).Y) / 2 - 7, 10, Brushes.DimGray);
                // marca do corte na elevação
                var pe1 = E(xm, z0 + h + hr); var pe2 = E(xm, zb);
                var marca = new Pen(Brushes.Black, 1.6); marca.Freeze();
                dc.DrawLine(marca, new WPoint(pe1.X, pe1.Y - 12), new WPoint(pe1.X, pe1.Y - 4));
                dc.DrawLine(marca, new WPoint(pe2.X, pe2.Y + 4), new WPoint(pe2.X, pe2.Y + 12));
                Txt(k == 0 ? "A" : "B", pe1.X + 3, pe1.Y - 16, 10, Brushes.Black);
            }
        }
    }
}
