using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DEB.Core.Elementos.Pilar.PreMoldado;
using DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D;
using DEB.Core.Normas.Verificacao;

namespace DEB.Revit.Precast.UI
{
    /// <summary>
    /// Pré-visualização dos pilares pré-moldados agrupados por similaridade e escolha da armadura de cada grupo.
    /// Janela modal: ao confirmar, <see cref="Selected"/> traz os grupos marcados para gerar.
    /// </summary>
    internal sealed class PrecastColumnsWindow : Window
    {
        private static readonly double[] Diameters = { 4.2, 5, 6.3, 8, 10, 12.5, 16, 20, 25, 32 };
        /// <summary>Classes de agressividade e aços aceitos pelas regras do núcleo (TabelasNbr.Classe e TabelasNbr.Fyk).</summary>
        private static readonly string[] ExposureClasses = { "I", "II", "III", "IV" };
        private static readonly string[] Steels = { "CA-25", "CA-50", "CA-60" };

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
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });   // cabe a tabela da armadura por linha
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

            // painel de verificações embaixo, sempre à vista (rolagem própria): muda junto com os campos e com a prévia
            var checksBox = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var checksTitle = new TextBlock { Text = "Verificações (NBR 6118 / NBR 9062)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(checksTitle, Dock.Top);
            checksBox.Children.Add(checksTitle);
            checksBox.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 260, Content = _checks });
            DockPanel.SetDock(checksBox, Dock.Bottom);
            right.Children.Add(checksBox);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            scroll.Content = _fields;
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
            PainelVerificacoes painel = PainelVerificacoes.De(g.Spec, r.Frame, r.Corbels);
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
                Text = painel.Geral == SituacaoVerificacao.Ok ? "✓ verificações OK"
                     : $"⚠ {PainelVerificacoes.Texto(painel.Geral)}: {painel.Conferir} a conferir · {painel.NaoAtende} não atende",
                Foreground = SealBrush(painel.Geral)
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
            // dados das verificações (cobrimento G1–G4, espaçamentos, consolo): só entram nas regras, não mudam as barras
            Choice("Classe de agressividade ambiental", ExposureClasses, () => TabelasNbr.Romano(TabelasNbr.Classe(s.ExposureClass)), v => s.ExposureClass = v);
            Num("fck do concreto (MPa)", () => s.FckMPa, v => s.FckMPa = v);
            Bool("Relação a/c ≤ 0,45 (2ª redução do cobrimento)", () => s.WaterCement045, v => s.WaterCement045 = v);
            Num("Agregado graúdo: dmáx (mm)", () => s.AggregateMm, v => s.AggregateMm = v);
            Choice("Aço das longitudinais", Steels, () => s.LongSteel, v => s.LongSteel = v);
            Choice("Aço dos estribos", Steels, () => s.StirrupSteel, v => s.StirrupSteel = v);

            Section("Armadura por linha (longitudinais)");
            // modelo salvo antes da D32: as linhas nascem dos campos simples (as mesmas barras) e passam a ser as editadas
            if (s.ArmaduraPorLinha == null) s.ArmaduraPorLinha = s.Linhas();
            _fields.Children.Add(LinesTable(s));
            Num("Cobrimento nas pontas (mm)", () => s.LongEndCoverMm, v => s.LongEndCoverMm = v);
            Num("Canto em \"L\": distância entre eixos (mm)", () => s.LongCornerSpacingMm, v => s.LongCornerSpacingMm = Math.Max(20, v));

            Section("Estribos");
            Dia("Bitola", () => s.StirrupDiameterMm, v => s.StirrupDiameterMm = v);
            Num("Espaçamento corrente (mm)", () => s.StirrupSpacingMm, v => s.StirrupSpacingMm = v);
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
            Bool("Tirante com barra de ancoragem soldada na ponta", () => s.TieWeldedBar, v => s.TieWeldedBar = v);
            Dia("Estribos verticais: bitola", () => s.CorbelStirrupDiameterMm, v => s.CorbelStirrupDiameterMm = v);
            Int("Estribos verticais: quantidade (0 = sem)", () => s.CorbelStirrupCount, v => s.CorbelStirrupCount = Math.Max(0, v));
            Dia("Armadura inferior: bitola", () => s.CorbelBottomDiameterMm, v => s.CorbelBottomDiameterMm = v);
            Int("Armadura inferior: nº de barras (0 = sem)", () => s.CorbelBottomCount, v => s.CorbelBottomCount = Math.Max(0, v));
            Dia("Pinos: bitola", () => s.CorbelPinDiameterMm, v => s.CorbelPinDiameterMm = v);
            Int("Pinos: quantidade (0 = sem)", () => s.CorbelPinCount, v => s.CorbelPinCount = Math.Max(0, v));
            Num("Pinos: comprimento acima do consolo (mm)", () => s.CorbelPinAboveMm, v => s.CorbelPinAboveMm = v);
            Num("Pinos: comprimento embutido (mm)", () => s.CorbelPinEmbedMm, v => s.CorbelPinEmbedMm = v);
            Num("Pinos: pé (mm)", () => s.CorbelPinFootMm, v => s.CorbelPinFootMm = v);

            Section("Içamento / insertos");
            Int("Cordoalhas por ponto de içamento (0, 1 ou 2; pontos simétricos ao CG)", () => s.LiftLoops, v => s.LiftLoops = Math.Max(0, Math.Min(2, v)));
            Int("Alça: face (1 = oeste, 2 = sul, 3 = leste, 4 = norte; 0 = automática)", () => s.LiftFaceNumber, v => s.LiftFaceNumber = Math.Max(0, Math.Min(4, v)));
            Num("Alça: distância às pontas (% do comprimento, a partir do CG)", () => s.LiftEndPercent, v => s.LiftEndPercent = Math.Max(5, Math.Min(45, v)));
            Dia("Alça: cordoalha CP-190, diâmetro (mm)", () => s.LiftDiameterMm, v => s.LiftDiameterMm = v);
            Num("Alça: embutimento das pernas (mm; limitado por dentro das longitudinais da face oposta)", () => s.LiftEmbedMm, v => s.LiftEmbedMm = v);
            Num("Alça: altura acima da face (mm)", () => s.LiftProtrusionMm, v => s.LiftProtrusionMm = v);
            Num("Alça: distância entre as pernas (mm)", () => s.LiftWidthMm, v => s.LiftWidthMm = v);
            Num("Alça: pé no fim das pernas (mm)", () => s.LiftFootMm, v => s.LiftFootMm = v);
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

        /// <summary>
        /// Painel de verificações (etapa 3): selo geral, contagem e uma linha por pendência, das regras do núcleo
        /// (<see cref="PainelVerificacoes"/>, as mesmas do relatório do grupo); atualizado junto com a prévia.
        /// </summary>
        private void UpdateChecks()
        {
            _checks.Children.Clear();
            ColumnGroup g = Current;
            if (g == null) return;
            PainelVerificacoes p = PainelVerificacoes.De(g.Spec, g.Representative.Frame, g.Representative.Corbels);

            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            head.Children.Add(new Border
            {
                Background = SealBrush(p.Geral), CornerRadius = new CornerRadius(3), Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = PainelVerificacoes.Texto(p.Geral).ToUpperInvariant(), Foreground = Brushes.White, FontWeight = FontWeights.Bold }
            });
            head.Children.Add(new TextBlock { Text = p.Contagem, VerticalAlignment = VerticalAlignment.Center });
            _checks.Children.Add(head);

            if (p.Geral == SituacaoVerificacao.Ok)
                _checks.Children.Add(new TextBlock { Text = "✓ Nenhuma pendência.", Foreground = Brushes.SeaGreen });
            foreach (ResultadoVerificacao r in p.Pendencias)
            {
                string vl = r.Valor.Length > 0 && r.Limite.Length > 0 ? $": {r.Valor} (limite {r.Limite})" : r.Valor.Length > 0 ? $": {r.Valor}" : "";
                CheckLine(r.Codigo, $"{r.Descricao}{vl} ({r.Referencia}).", r.Dica, SealBrush(r.Situacao));
            }
            foreach (string m in p.OutrosAvisos) CheckLine("—", m, "", SealBrush(SituacaoVerificacao.Conferir));

            // armadura por linha: barras por bitola (feixe = cada barra) e a taxa do trecho menos e do mais armado
            ColumnRebarSpec s = g.Spec;
            ColumnBox frame = g.Representative.Frame;
            var longs = ColumnRebarLayout.LongitudinalBars(frame, s);
            string barras = string.Join(" + ", longs.GroupBy(b => b.Bitola).OrderByDescending(x => x.Key)
                .Select(x => $"{x.Sum(b => b.Feixe)} Ø{RebarFactory.Fmt(x.Key)}"));
            double ac = U_.ToMm(frame.Bx) * U_.ToMm(frame.By);
            var rhos = ColumnRebarLayout.LongitudinalStretches(frame, s)
                .Select(t => t.Sum(b => b.Feixe * Math.PI * b.Bitola * b.Bitola / 4) / ac * 100).DefaultIfEmpty(0).ToList();
            double r0 = rhos.Min(), r1 = rhos.Max();
            _checks.Children.Add(new TextBlock
            {
                Text = $"{(barras.Length > 0 ? barras : "sem longitudinais")}  ·  ρ = " + (r1 - r0 < 0.005 ? $"{r0:0.00}%" : $"{r0:0.00}% a {r1:0.00}%"),
                Foreground = Brushes.DimGray, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap
            });
        }

        /// <summary>Uma pendência do painel: código (na cor da situação), mensagem e, abaixo, a dica.</summary>
        private void CheckLine(string code, string message, string tip, Brush color)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var c = new TextBlock { Text = code, FontWeight = FontWeights.Bold, Foreground = color };
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrWhiteSpace(tip))
                text.Children.Add(new TextBlock { Text = tip, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Brushes.DimGray });
            Grid.SetColumn(text, 1);
            g.Children.Add(c);
            g.Children.Add(text);
            _checks.Children.Add(g);
        }

        private static Brush SealBrush(SituacaoVerificacao s) =>
            s == SituacaoVerificacao.NaoAtende ? Brushes.Firebrick : s == SituacaoVerificacao.Conferir ? Brushes.DarkOrange : Brushes.SeaGreen;

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

        /// <summary>Lista fechada (classe de agressividade, aço). Valor gravado fora da lista: fica como está até o usuário escolher.</summary>
        private void Choice(string label, string[] options, Func<string> get, Action<string> set)
        {
            var cb = new ComboBox { Width = 80 };
            foreach (string o in options) cb.Items.Add(o);
            cb.SelectedItem = options.FirstOrDefault(o => string.Equals(o, (get() ?? "").Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
            cb.SelectionChanged += (s, e) => { if (cb.SelectedItem is string v) { set(v); Changed(); } };
            _fields.Children.Add(Row(label, cb));
        }

        private void Str(string label, Func<string> get, Action<string> set)
        {
            var tb = new TextBox { Text = get() ?? "", Width = 150 };
            tb.TextChanged += (s, e) => { set(tb.Text); Changed(); };
            _fields.Children.Add(Row(label, tb));
        }

        /// <summary>Bitolas da coluna "Bitola" da armadura por linha: as do template do pilar do CAD.</summary>
        private static readonly double[] LongDiameters = { 10, 12.5, 16, 20, 22, 25, 32, 40 };

        /// <summary>
        /// Tabela da armadura por linha (D32): as colunas e os nomes de arranjo da tabela "Longitudinais" do template do pilar do
        /// CAD — arranjo, barras por face, bitola, feixe, início e fim (cm da base; vazio = o pilar todo).
        /// </summary>
        private UIElement LinesTable(ColumnRebarSpec s)
        {
            var box = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            void Build()
            {
                box.Children.Clear();
                var grid = new Grid();
                double[] widths = { 116, 48, 58, 42, 52, 52, 24 };
                foreach (double w in widths) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
                string[] heads = { "Arranjo", "Barras por face", "Bitola (mm)", "Feixe", "Início (cm da base)", "Fim (cm da base)", "" };
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int c = 0; c < heads.Length; c++)
                    Cell(grid, new TextBlock { Text = heads[c], TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Bottom }, 0, c);
                for (int i = 0; i < s.ArmaduraPorLinha.Count; i++)
                {
                    LinhaArmaduraPorLinha l = s.ArmaduraPorLinha[i];
                    int row = i + 1;
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    var arr = new ComboBox();
                    foreach (string a in ArmaduraPorLinha.Arranjos) arr.Items.Add(a);
                    arr.SelectedItem = ArmaduraPorLinha.Arranjos.FirstOrDefault(a => string.Equals(a, l.Arranjo, StringComparison.OrdinalIgnoreCase));
                    bool canto = ArmaduraPorLinha.ArranjoDeCanto(l.Arranjo);
                    var porFace = new TextBox { Text = canto ? "" : l.PorFace.ToString(CultureInfo.CurrentCulture), IsEnabled = !canto };
                    arr.SelectionChanged += (o, e) =>
                    {
                        if (!(arr.SelectedItem is string a)) return;
                        l.Arranjo = a;
                        bool c = ArmaduraPorLinha.ArranjoDeCanto(a);
                        porFace.IsEnabled = !c;
                        if (!c && l.PorFace < 1) l.PorFace = 1;
                        porFace.Text = c ? "" : l.PorFace.ToString(CultureInfo.CurrentCulture);
                        Changed();
                    };
                    porFace.TextChanged += (o, e) =>
                    {
                        if (!porFace.IsEnabled) return;
                        if (int.TryParse(porFace.Text, out int v) && v >= 1) { l.PorFace = v; porFace.ClearValue(BackgroundProperty); Changed(); }
                        else porFace.Background = Brushes.MistyRose;
                    };

                    var phi = new ComboBox { IsEditable = true };
                    foreach (double d in LongDiameters) phi.Items.Add(d.ToString("0.#", CultureInfo.CurrentCulture));
                    phi.Text = l.Bitola.ToString("0.#", CultureInfo.CurrentCulture);
                    void applyPhi()
                    {
                        string t = phi.SelectedItem as string ?? phi.Text;
                        if (TryParse(t, out double v) && v > 0 && Math.Abs(v - l.Bitola) > 1e-9) { l.Bitola = v; Changed(); }
                    }
                    phi.SelectionChanged += (o, e) => applyPhi();
                    phi.LostFocus += (o, e) => applyPhi();

                    var fx = new ComboBox();
                    foreach (string n in new[] { "1", "2", "3" }) fx.Items.Add(n);
                    fx.SelectedItem = Math.Max(1, Math.Min(3, l.Feixe)).ToString(CultureInfo.InvariantCulture);
                    fx.SelectionChanged += (o, e) => { if (fx.SelectedItem is string n) { l.Feixe = int.Parse(n, CultureInfo.InvariantCulture); Changed(); } };

                    TextBox Optional(double? get, Action<double?> set)
                    {
                        var tb = new TextBox { Text = get.HasValue ? get.Value.ToString("0.##", CultureInfo.CurrentCulture) : "" };
                        tb.TextChanged += (o, e) =>
                        {
                            if (string.IsNullOrWhiteSpace(tb.Text)) { set(null); tb.ClearValue(BackgroundProperty); Changed(); }
                            else if (TryParse(tb.Text, out double v) && v >= 0) { set(v); tb.ClearValue(BackgroundProperty); Changed(); }
                            else tb.Background = Brushes.MistyRose;
                        };
                        return tb;
                    }

                    var del = new Button { Content = "×", Padding = new Thickness(4, 0, 4, 0), ToolTip = "Excluir a linha" };
                    del.Click += (o, e) => { s.ArmaduraPorLinha.Remove(l); Build(); Changed(); };

                    Cell(grid, arr, row, 0);
                    Cell(grid, porFace, row, 1);
                    Cell(grid, phi, row, 2);
                    Cell(grid, fx, row, 3);
                    Cell(grid, Optional(l.Ini, v => l.Ini = v), row, 4);
                    Cell(grid, Optional(l.Fim, v => l.Fim = v), row, 5);
                    Cell(grid, del, row, 6);
                }
                box.Children.Add(grid);
                Button add = Button("+ linha", () =>
                {
                    // linha nova como a do CAD: faces 2 e 4, 1 barra por face, ø20
                    s.ArmaduraPorLinha.Add(new LinhaArmaduraPorLinha { Arranjo = ArmaduraPorLinha.Faces24, PorFace = 1, Bitola = 20 });
                    Build();
                    Changed();
                }, new Thickness(0, 4, 0, 0));
                add.HorizontalAlignment = HorizontalAlignment.Left;
                box.Children.Add(add);
                box.Children.Add(new TextBlock
                {
                    Text = "Cantos = 4 barras; ao lado dos cantos = 8, em \"L\"; faces = barras por face. Para reduzir com a altura, termine " +
                           "uma linha e comece outra do mesmo arranjo antes do fim dela: a sobreposição é o traspasse (sem emenda automática " +
                           "pelo comprimento comercial).",
                    TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 0)
                });
            }
            Build();
            return box;
        }

        private static void Cell(Grid grid, FrameworkElement e, int row, int col)
        {
            e.Margin = new Thickness(0, 1, 3, 1);
            Grid.SetRow(e, row);
            Grid.SetColumn(e, col);
            grid.Children.Add(e);
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
            foreach (ColumnRebarSpec p in ColumnRebarPresets.LoadPresets()) _presets.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p });
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
            ColumnRebarPresets.SavePreset(Current.Spec);
            ReloadPresets();
            MessageBox.Show(this, $"Modelo \"{Current.Spec.Name}\" salvo em\n{ColumnRebarPresets.PresetsPath}", Title);
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
