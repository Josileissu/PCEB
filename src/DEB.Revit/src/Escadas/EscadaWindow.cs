using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DEB.Core.Analise.Mef;
using DEB.Core.Elementos.Escada;
using DEB.Core.Normas.Verificacao;
using DEB.Revit.UI;
using WColor = System.Windows.Media.Color;

namespace DEB.Revit.Escadas
{
    /// <summary>
    /// Janela "Escada" (D42): entrada da escada em laje armada longitudinalmente, cálculo no núcleo e a prévia — elevação com as
    /// barras, esforços (M, V, N), reações e deformada, conferência por MEF (OpenSees e CalculiX) e as verificações. A prévia e o
    /// que vai para o Revit saem do mesmo <see cref="ResultadoEscada"/> (D26). Padrão visual D41 (UI/Estilo).
    /// </summary>
    internal sealed class EscadaWindow : Window
    {
        private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
        private static readonly string[] Classes = { "I", "II", "III", "IV" };
        private readonly Dictionary<string, TextBox> _campos = new Dictionary<string, TextBox>();
        private readonly ComboBox _classe = new ComboBox { ItemsSource = Classes };
        private readonly EscadaPreview _previa = new EscadaPreview(), _previaDef = new EscadaPreview { MostrarBarras = false };
        private readonly EscadaCorte _corte = new EscadaCorte(), _corteDef = new EscadaCorte { MostrarDeformada = true };
        private static readonly string[] Tipos = { "Armada longitudinalmente (apoios nas pontas)", "Armada transversalmente (apoios laterais)", "Em balanço (apoio de um lado)" };
        private readonly ComboBox _tipo = new ComboBox { ItemsSource = Tipos };
        private static readonly string[] Execucoes = { "Moldada no local", "Pré-moldada" };
        private static readonly string[] Pontas = { "Apoio direto", "Dente" };
        private static readonly string[] Apoios = Enum.GetValues(typeof(ApoioPreMoldado)).Cast<ApoioPreMoldado>()
            .Select(a => $"{PadroesPreMoldado.Nome(a)} · Hd = {PadroesPreMoldado.CoefHd(a).ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"))} Fd").ToArray();
        private readonly ComboBox _exec = new ComboBox { ItemsSource = Execucoes };
        private static readonly string[] PecasNomes = { "Lance (com os patamares das pontas)", "Patamar (peça plana)" };
        private readonly ComboBox _peca = new ComboBox { ItemsSource = PecasNomes };
        private readonly TextBox _cargasApoiadas = new TextBox { TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, MinHeight = 38 };
        private readonly Dictionary<string, (ComboBox Ponta, ComboBox Apoio, Dictionary<string, TextBox> Campos)> _lig = new Dictionary<string, (ComboBox, ComboBox, Dictionary<string, TextBox>)>();
        private readonly ContentControl _ligacoes = new ContentControl();
        private bool _carregando;
        private readonly ContentControl _esforcos = new ContentControl(), _reacoes = new ContentControl(), _mef = new ContentControl(),
                                        _verificacoes = new ContentControl(), _memoria = new ContentControl(), _otimizador = new ContentControl();
        private readonly TextBox _precoConc = new TextBox { Text = "650" }, _precoAco = new TextBox { Text = "9" }, _hMin = new TextBox { Text = "10" }, _hMax = new TextBox { Text = "22" };
        private readonly ContentControl _tabelaOtim = new ContentControl();
        private readonly StackPanel _proposta = new StackPanel(), _resumoMef = new StackPanel();
        private readonly ContentControl _seloGeral = new ContentControl();
        private readonly TextBlock _subtitulo = new TextBlock { Foreground = Estilo.B(Estilo.Suave) };
        private readonly TabControl _abas = new TabControl();
        private readonly Button _botaoMef;
        private Border _molduraDef, _molduraCorteDef;
        private ComparacaoMefEscada _cmp;
        private readonly string _origem;

        public EscadaLaje Entrada { get; private set; }
        public ResultadoEscada Resultado { get; private set; }
        public TabControl Abas => _abas;

        public EscadaWindow(EscadaLaje entrada, string origem = null, string textoOk = null)
        {
            Entrada = entrada.Copia(); _origem = origem;
            Title = "PCEB — Escada em laje";
            Width = 1500; Height = 900; MinWidth = 1150; MinHeight = 650;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Estilo.Aplicar(this);
            var raiz = new DockPanel();
            var cab = Estilo.Cabecalho("Escada em laje — cálculo e armação",
                "Cálculo pela apostila de escadas e pela NBR 6118, com conferência por elementos finitos (OpenSees e CalculiX)", _seloGeral);
            DockPanel.SetDock(cab, Dock.Top); raiz.Children.Add(cab);

            // rodapé
            var rodape = new Border { Background = Estilo.B(Estilo.Branco), BorderBrush = Estilo.B(Estilo.Linha), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(14, 8, 14, 8) };
            var rp = new DockPanel();
            var botoes = new StackPanel { Orientation = Orientation.Horizontal };
            var ok = Estilo.Primario(textoOk ?? "Criar no Revit", () => { if (Calcular()) DialogResult = true; });
            ok.IsDefault = true; ok.Margin = new Thickness(0, 0, 8, 0); ok.Padding = new Thickness(18, 5, 18, 5);
            botoes.Children.Add(ok);
            var cancel = Estilo.Botao("Cancelar", () => DialogResult = false); cancel.IsCancel = true; cancel.Padding = new Thickness(14, 5, 14, 5);
            botoes.Children.Add(cancel);
            DockPanel.SetDock(botoes, Dock.Right); rp.Children.Add(botoes);
            rp.Children.Add(new TextBlock
            {
                Text = (origem != null ? origem + " · " : "") + "A escada entra como família PCEB (modelo genérico estrutural) e recebe a armadura proposta.",
                Foreground = Estilo.B(Estilo.Suave), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            rodape.Child = rp;
            DockPanel.SetDock(rodape, Dock.Bottom); raiz.Children.Add(rodape);

            var corpo = new Grid { Margin = new Thickness(12, 10, 12, 6) };
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });

            // ---- esquerda: entrada ----
            var esq = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            var geo = new StackPanel();
            geo.Children.Add(new TextBlock { Text = "Tipo", Foreground = Estilo.B(Estilo.Suave), Margin = new Thickness(0, 0, 0, 2) });
            geo.Children.Add(_tipo);
            _tipo.Margin = new Thickness(0, 0, 0, 6);
            _tipo.SelectionChanged += (s, ev) => { if (!_carregando) Calcular(); };
            geo.Children.Add(new TextBlock { Text = "Peça", Foreground = Estilo.B(Estilo.Suave), Margin = new Thickness(0, 0, 0, 2) });
            geo.Children.Add(_peca);
            _peca.Margin = new Thickness(0, 0, 0, 6);
            _peca.SelectionChanged += (s, ev) => { if (!_carregando) Calcular(); };
            Campo(geo, "ComprimentoPatamar", "Vão do patamar (peça plana)", "cm");
            Campo(geo, "Desnivel", "Desnível", "cm");
            Campo(geo, "Espelhos", "Nº de espelhos (0 = auto)", "");
            Campo(geo, "EspelhoAlvo", "Espelho desejado", "cm");
            Campo(geo, "Piso", "Piso", "cm");
            Campo(geo, "Largura", "Largura", "cm");
            Campo(geo, "Espessura", "Espessura do lance", "cm");
            Campo(geo, "EspessuraPatamar", "Espessura do patamar", "cm");
            Campo(geo, "PatamarInferior", "Patamar inferior", "cm");
            Campo(geo, "PatamarSuperior", "Patamar superior", "cm");
            Campo(geo, "LarguraApoio", "Largura dos apoios", "cm");
            esq.Children.Add(Estilo.Cartao("Geometria", geo));
            var mat = new StackPanel();
            Campo(mat, "Fck", "fck", "MPa");
            mat.Children.Add(Estilo.Campo("Classe de agressividade", _classe, "", 150));
            Campo(mat, "Cobrimento", "Cobrimento", "cm");
            esq.Children.Add(Estilo.Cartao("Materiais", mat));
            var cg = new StackPanel();
            Campo(cg, "Revestimento", "Revestimento", "kN/m²");
            Campo(cg, "Sobrecarga", "Sobrecarga", "kN/m²");
            Campo(cg, "Parapeito", "Parapeito (por borda)", "kN/m");
            Campo(cg, "BordasComParapeito", "Bordas com parapeito", "");
            cg.Children.Add(new TextBlock { Text = "Por m² de projeção horizontal; o peso próprio é calculado.", Foreground = Estilo.B(Estilo.Concreto), FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) });
            cg.Children.Add(new TextBlock { Text = "Cargas de outras peças apoiadas nesta", Foreground = Estilo.B(Estilo.Suave), Margin = new Thickness(0, 4, 0, 2) });
            cg.Children.Add(_cargasApoiadas);
            cg.Children.Add(new TextBlock { Text = "x comprimento: G + Q; … (cm; kN por metro de largura; x = centro, a partir do apoio de baixo). Ex.: 10 15: 9,5 + 3", Foreground = Estilo.B(Estilo.Concreto), FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) });
            cg.Children.Add(Estilo.Primario("Calcular", () => Calcular()));
            esq.Children.Add(Estilo.Cartao("Cargas (características)", cg));
            var pm = new StackPanel();
            pm.Children.Add(new TextBlock { Text = "Execução", Foreground = Estilo.B(Estilo.Suave), Margin = new Thickness(0, 0, 0, 2) });
            pm.Children.Add(_exec); _exec.Margin = new Thickness(0, 0, 0, 6);
            _exec.SelectionChanged += (s, ev) => { if (!_carregando) Calcular(); };
            foreach (var (chave, titulo) in new[] { ("inf", "Ponta de baixo"), ("sup", "Ponta de cima") })
            {
                pm.Children.Add(new TextBlock { Text = titulo, FontWeight = FontWeights.SemiBold, Foreground = Estilo.B(Estilo.Mata), Margin = new Thickness(0, 6, 0, 2) });
                var cp = new ComboBox { ItemsSource = Pontas }; var ca = new ComboBox { ItemsSource = Apoios };
                pm.Children.Add(Estilo.Campo("Ponta", cp, "", 150));
                pm.Children.Add(Estilo.Campo("Apoio", ca, "", 150));
                var campos = new Dictionary<string, TextBox>();
                foreach (var (nome, rot, un) in new[] { ("ComprimentoApoio", "Comprimento de apoio", "cm"), ("AlturaDente", "Altura do dente", "cm"), ("ComprimentoDente", "Comprimento do dente", "cm"),
                                                        ("Pinos", "Pinos", ""), ("BitolaPino", "ø do pino", "mm"), ("FuroPino", "Furo do pino", "mm"), ("EmbutimentoPino", "Embutimento do pino", "cm") })
                {
                    var tb = new TextBox(); campos[nome] = tb;
                    pm.Children.Add(Estilo.Campo(rot, tb, un, 150));
                }
                _lig[chave] = (cp, ca, campos);
            }
            pm.Children.Add(new TextBlock { Text = "Içamento", FontWeight = FontWeights.SemiBold, Foreground = Estilo.B(Estilo.Mata), Margin = new Thickness(0, 6, 0, 2) });
            Campo(pm, "CoefDinamico", "Coeficiente dinâmico", "");
            Campo(pm, "FcjIcamento", "fcj no içamento (0 = 0,7fck)", "MPa");
            pm.Children.Add(Estilo.Primario("Calcular", () => Calcular()));
            esq.Children.Add(Estilo.Cartao("Pré-moldada (NBR 9062)", pm));
            var sve = new ScrollViewer { Content = esq, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetColumn(sve, 0); corpo.Children.Add(sve);

            // ---- centro: abas ----
            var centro = new DockPanel();
            var linha = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            linha.Children.Add(_subtitulo);
            DockPanel.SetDock(linha, Dock.Top); centro.Children.Add(linha);
            var leg = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var (txt, cor) in EscadaPreview.Legenda)
            {
                leg.Children.Add(new Border { Width = 18, Height = 3, Background = new SolidColorBrush(cor), Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                leg.Children.Add(new TextBlock { Text = txt, Foreground = Estilo.B(Estilo.Suave), FontSize = 11 });
            }
            var abaArm = new DockPanel();
            DockPanel.SetDock(leg, Dock.Bottom); abaArm.Children.Add(leg);
            var lado = new Grid();
            lado.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
            lado.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var me = Moldura(_previa); var mc = Moldura(_corte); mc.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(mc, 1); lado.Children.Add(me); lado.Children.Add(mc);
            abaArm.Children.Add(lado);
            _abas.Items.Add(new TabItem { Header = "Elevação e armação", Content = abaArm });
            _abas.Items.Add(new TabItem { Header = "Esforços (ELU)", Content = _esforcos });
            _abas.Items.Add(new TabItem { Header = "Reações e deformada", Content = _reacoes });
            _abas.Items.Add(new TabItem { Header = "MEF × cálculo", Content = _mef });
            _abas.Items.Add(new TabItem { Header = "Ligações e içamento", Content = _ligacoes });
            _abas.Items.Add(new TabItem { Header = "Otimizador", Content = _otimizador });
            _abas.Items.Add(new TabItem { Header = "Verificações", Content = _verificacoes });
            _abas.Items.Add(new TabItem { Header = "Memória", Content = _memoria });
            centro.Children.Add(_abas);
            Grid.SetColumn(centro, 1); corpo.Children.Add(centro);

            // ---- direita: proposta e MEF ----
            var dir = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            dir.Children.Add(Estilo.Cartao("Armadura proposta", _proposta));
            var mefc = new StackPanel();
            mefc.Children.Add(_resumoMef);
            _botaoMef = Estilo.Botao("Conferir por MEF", ConferirMef, new Thickness(0, 6, 0, 0));
            mefc.Children.Add(_botaoMef);
            dir.Children.Add(Estilo.Cartao("Conferência por elementos finitos", mefc));
            var svd = new ScrollViewer { Content = dir, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetColumn(svd, 2); corpo.Children.Add(svd);

            raiz.Children.Add(corpo);
            Content = raiz;
            MontarOtimizador();
            Preencher(Entrada);
            Calcular();
        }

        // ------------------------------------------------------------------ entrada

        private void Campo(Panel p, string chave, string rotulo, string unidade)
        {
            var tb = new TextBox();
            _campos[chave] = tb;
            p.Children.Add(Estilo.Campo(rotulo, tb, unidade, 150));
        }

        private void Preencher(EscadaLaje e)
        {
            foreach (var pr in typeof(EscadaLaje).GetProperties())
                if (_campos.TryGetValue(pr.Name, out var tb))
                {
                    object v = pr.GetValue(e);
                    tb.Text = v is double d ? d.ToString("0.##", Pt) : v?.ToString() ?? "";
                }
            _classe.SelectedItem = Classes.Contains(e.ClasseAgressividade) ? e.ClasseAgressividade : "II";
            _carregando = true; _tipo.SelectedIndex = (int)e.Tipo; _exec.SelectedIndex = (int)e.Execucao; _peca.SelectedIndex = (int)e.Peca;
            _cargasApoiadas.Text = CargaApoiada.Texto(e.CargasApoiadas);
            foreach (var (chave, lig) in new[] { ("inf", e.LigacaoInferior), ("sup", e.LigacaoSuperior) })
            {
                var (cp, ca, campos) = _lig[chave];
                cp.SelectedIndex = (int)lig.Ponta; ca.SelectedIndex = (int)lig.Apoio;
                foreach (var kv in campos)
                {
                    object v = typeof(LigacaoEscada).GetProperty(kv.Key).GetValue(lig);
                    kv.Value.Text = v is double d ? d.ToString("0.##", Pt) : v?.ToString() ?? "";
                }
            }
            _carregando = false;
        }

        private bool Ler(out string erro)
        {
            erro = null;
            var e = Entrada.Copia();
            foreach (var pr in typeof(EscadaLaje).GetProperties())
            {
                if (!_campos.TryGetValue(pr.Name, out var tb)) continue;
                string s = (tb.Text ?? "").Trim().Replace(',', '.');
                if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) { erro = $"Valor inválido em \"{pr.Name}\"."; return false; }
                if (pr.PropertyType == typeof(int)) pr.SetValue(e, (int)Math.Round(v)); else pr.SetValue(e, v);
            }
            e.ClasseAgressividade = _classe.SelectedItem as string ?? "II";
            e.Tipo = (TipoEscada)Math.Max(0, _tipo.SelectedIndex);
            e.Execucao = (ExecucaoEscada)Math.Max(0, _exec.SelectedIndex);
            e.Peca = (PecaEscada)Math.Max(0, _peca.SelectedIndex);
            var errosCa = new List<string>();
            var cas = CargaApoiada.LerTexto(_cargasApoiadas.Text, errosCa);
            if (errosCa.Count > 0) { erro = errosCa[0]; return false; }
            e.CargasApoiadas = cas;
            foreach (var (chave, lig) in new[] { ("inf", e.LigacaoInferior), ("sup", e.LigacaoSuperior) })
            {
                var (cp, ca, campos) = _lig[chave];
                lig.Ponta = (PontaPreMoldada)Math.Max(0, cp.SelectedIndex); lig.Apoio = (ApoioPreMoldado)Math.Max(0, ca.SelectedIndex);
                foreach (var kv in campos)
                {
                    string s = (kv.Value.Text ?? "").Trim().Replace(',', '.');
                    if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) { erro = $"Valor inválido em \"{kv.Key}\"."; return false; }
                    var pr = typeof(LigacaoEscada).GetProperty(kv.Key);
                    if (pr.PropertyType == typeof(int)) pr.SetValue(lig, (int)Math.Round(v)); else pr.SetValue(lig, v);
                }
            }
            Entrada = e;
            return true;
        }

        // ------------------------------------------------------------------ cálculo e apresentação

        public bool Calcular()
        {
            if (!Ler(out string erro)) { MessageBox.Show(this, erro, "Escada", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
            try { Resultado = CalculoEscada.Calcular(Entrada); }
            catch (Exception ex) { MessageBox.Show(this, "Não deu para calcular: " + ex.Message, "Escada", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
            _cmp = null;
            Mostrar();
            return Resultado.Erros.Count == 0;
        }

        private void Mostrar()
        {
            var r = Resultado; var g = r.Geo;
            var todas = r.Verificacoes.Concat(_cmp?.Verificacoes ?? Enumerable.Empty<ResultadoVerificacao>()).ToList();
            _seloGeral.Content = Estilo.Selo(todas.Count == 0 ? (SituacaoVerificacao?)null : todas.Max(v => v.Situacao), 110, 11);
            bool lat = r.Entrada.Tipo != TipoEscada.Longitudinal, bal = r.Entrada.Tipo == TipoEscada.Balanco;
            if (r.Entrada.SoPatamar)
                _subtitulo.Text = $"Patamar (peça plana){(r.Entrada.PreMoldada ? ", pré-moldado" : "")} · vão ℓ = {F(r.VaoCalculo, "0")} cm · hp = {F(g.Hp, "0.#")} cm · largura {F(r.Entrada.Largura, "0")} cm" +
                                  $"{(r.Entrada.CargasApoiadas.Count > 0 ? $" · {r.Entrada.CargasApoiadas.Count} carga(s) de outras peças" : "")} · {Estilo.Contagem(todas)}";
            else
            _subtitulo.Text = $"{Tipos[(int)r.Entrada.Tipo].Split('(')[0].Trim()} · {g.N} espelhos de {F(g.Espelho, "0.0#")} cm · piso {F(g.Piso, "0.#")} · α = {F(g.Alfa * 180 / Math.PI, "0.0")}° · " +
                              $"vão de cálculo ℓ = {F(r.VaoCalculo, "0")} cm{(lat ? " (na largura)" : "")} · h = {F(g.H, "0.#")} cm · largura {F(r.Entrada.Largura, "0")} cm · {Estilo.Contagem(todas)}";
            _previa.Resultado = r; _previa.InvalidateVisual();
            _previaDef.Resultado = r;
            _previaDef.Deformada.Clear();
            _previaDef.Deformada.AddRange(r.Diagrama.Select(p => (p.X, p.FlechaElastica)));
            _previaDef.InvalidateVisual();
            _corte.Resultado = r; _corte.InvalidateVisual();
            _corteDef.Resultado = r; _corteDef.InvalidateVisual();
            string rotX = lat ? (bal ? "y a partir do apoio (cm)" : "y na largura, de apoio a apoio (cm)") : "x (cm)";

            // esforços
            var grid = new Grid();
            for (int i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var marcas = g.Trechos.Skip(1).Select(t => (t.P0.X, "quina")).ToList();
            Grafico Gr(string tit, string un, bool baixo, Func<PontoEscada, double> f, WColor cor)
            {
                var gr = new Grafico { Titulo = tit, RotuloY = un, YParaBaixo = baixo, RotuloX = rotX, Margin = new Thickness(0, 0, 0, 6) };
                gr.Series.Add(new SerieGrafico { Nome = tit.Split(' ')[0], Pontos = r.Diagrama.Select(p => (p.X, f(p))).ToList(), Cor = cor, Preencher = true, RotularMaximo = true, Unidade = un });
                gr.Marcas.AddRange(marcas);
                return gr;
            }
            var gm = Gr("Md — momento fletor", "kN·m/m", true, p => p.Md, Estilo.Anil);
            double sm = bal ? -1 : 1;
            gm.Series.Add(new SerieGrafico { Nome = "MRd", Pontos = new List<(double, double)> { (0, sm * r.MRd), (r.VaoCalculo, sm * r.MRd) }, Cor = Estilo.Ocre, Tracejada = true, Espessura = 1.2 });
            var gv = Gr("Vd — cortante perpendicular ao eixo", "kN/m", false, p => p.Vd, Estilo.Mata);
            gv.Series.Add(new SerieGrafico { Nome = "VRd1", Pontos = new List<(double, double)> { (0, r.VRd1), (r.VaoCalculo, r.VRd1) }, Cor = Estilo.Ocre, Tracejada = true, Espessura = 1.2 });
            var gn = Gr("Nd — normal ao longo do eixo (compressão −)", "kN/m", false, p => p.Nd, Estilo.Hex("#8e44ad"));
            gn.Series[0].RotularPontas = true;
            Grid.SetRow(gm, 0); Grid.SetRow(gv, 1); Grid.SetRow(gn, 2);
            grid.Children.Add(gm); grid.Children.Add(gv);
            if (lat) grid.RowDefinitions.RemoveAt(2); else grid.Children.Add(gn);
            _esforcos.Content = Moldura(grid, 6);

            // reações e deformada
            var rd = new DockPanel();
            double B = r.ComprimentoReacao;
            var pares = new List<(string, string)>
            {
                (lat ? (bal ? "Apoio (engaste) — G / Q" : "Apoio esquerdo — G / Q") : "Apoio inferior — G / Q", $"{F(r.RaG)} / {F(r.RaQ)} kN/m  →  {F(r.RaG * B, "0.0")} / {F(r.RaQ * B, "0.0")} kN"),
                bal ? ("Momento no engaste — G / Q", $"{F(r.MEngG)} / {F(r.MEngQ)} kN·m/m  →  {F(r.MEngG * B, "0.0")} / {F(r.MEngQ * B, "0.0")} kN·m")
                    : (lat ? "Apoio direito — G / Q" : "Apoio superior — G / Q", $"{F(r.RbG)} / {F(r.RbQ)} kN/m  →  {F(r.RbG * B, "0.0")} / {F(r.RbQ * B, "0.0")} kN"),
                ("Carga em projeção", $"lance g = {F(r.GLance)}, patamar g = {F(r.GPatamar)}, q = {F(r.Q)} kN/m²"),
                ("Flecha elástica (QP, Ic)", $"{F(r.FlechaElasticaMax, "0.000")} cm"),
                ("Flecha imediata (Branson)", $"{F(r.FlechaImediata, "0.000")} cm"),
                ("Flecha total (com fluência)", $"{F(r.FlechaTotal, "0.00")} cm  (limite {(bal ? "2ℓ" : "ℓ")}/250 = {F((bal ? 2 : 1) * r.VaoCalculo / 250, "0.00")} cm)"),
            };
            var cart = Estilo.Cartao(lat ? "Reações por metro de lance (inclinado) e no lance inteiro (características)" : "Reações por metro de largura e na escada inteira (características)",
                Estilo.Pares(pares, 200), new Thickness(0, 0, 0, 8));
            DockPanel.SetDock(cart, Dock.Top); rd.Children.Add(cart);
            var gd = new Grafico { Titulo = "Deformada — quase permanente, rigidez elástica", RotuloY = "cm", YParaBaixo = true, RotuloX = rotX, Height = 220, Margin = new Thickness(0, 0, 0, 8) };
            gd.Series.Add(new SerieGrafico { Nome = "cálculo", Pontos = r.Diagrama.Select(p => (p.X, p.FlechaElastica)).ToList(), Cor = Estilo.Hex("#8e44ad"), RotularMaximo = true, Unidade = "cm" });
            gd.Marcas.AddRange(marcas);
            DockPanel.SetDock(gd, Dock.Bottom); rd.Children.Add(Moldura(gd, 4));
            _molduraDef ??= Moldura(_previaDef);
            _molduraCorteDef ??= Moldura(_corteDef);
            foreach (var mm in new[] { _molduraDef, _molduraCorteDef }) if (mm.Parent is Panel pai) pai.Children.Remove(mm);
            rd.Children.Add(lat ? _molduraCorteDef : _molduraDef);
            _reacoes.Content = rd;

            // proposta
            _proposta.Children.Clear();
            foreach (var b in r.Barras)
                _proposta.Children.Add(new TextBlock { Text = b.Texto + $" — {F(b.Comprimento, "0")} cm", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 3) });
            _proposta.Children.Add(Estilo.Pares(new[]
            {
                ("Md,máx", $"{F(r.MdMax)} kN·m/m"), ("As,nec / As,ef", $"{F(r.AsNec)} / {F(r.AsPrincipal)} cm²/m"),
                ("MRd", $"{F(r.MRd)} kN·m/m"), ("Vd / VRd1", $"{F(r.VdMax, "0.0")} / {F(r.VRd1, "0.0")} kN/m"),
                (lat ? "lb,nec (apoio)" : r.Entrada.SoPatamar ? "lb,nec" : "lb,nec (quina)", $"{F(r.LbNec, "0")} cm"),
            }, 110));
            foreach (string a in r.Avisos)
                _proposta.Children.Add(new Border { Background = Estilo.B(Estilo.Hex("#fff4dc")), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 4, 0, 0),
                    Child = new TextBlock { Text = a, TextWrapping = TextWrapping.Wrap, Foreground = Estilo.B(Estilo.Ocre), FontSize = 11 } });

            MostrarLigacoes(r);
            _verificacoes.Content = new ScrollViewer { Content = Estilo.TabelaVerificacoes(todas), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var mem = new StackPanel { Margin = new Thickness(4) };
            foreach (var m in r.Memoria)
            {
                mem.Children.Add(new TextBlock { Text = m.Titulo, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 3), Foreground = Estilo.B(Estilo.Mata) });
                foreach (string l in m.Linhas) mem.Children.Add(new TextBlock { Text = l, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 0, 0, 2) });
            }
            _memoria.Content = new ScrollViewer { Content = mem, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            MostrarMef();
        }

        // ------------------------------------------------------------------ MEF

        private void ConferirMef()
        {
            if (Resultado == null) return;
            var r = Resultado;
            _botaoMef.IsEnabled = false;
            _resumoMef.Children.Clear();
            _resumoMef.Children.Add(new TextBlock { Text = "Rodando o OpenSees e o CalculiX…", Foreground = Estilo.B(Estilo.Suave) });
            Task.Run(() => MefEscada.Comparar(r)).ContinueWith(t =>
            {
                _botaoMef.IsEnabled = true;
                if (t.IsFaulted) { _resumoMef.Children.Clear(); _resumoMef.Children.Add(new TextBlock { Text = "Falhou: " + t.Exception?.GetBaseException().Message, TextWrapping = TextWrapping.Wrap }); return; }
                if (Resultado != r) return;   // recalculou no meio
                _cmp = t.Result;
                Mostrar();
                _abas.SelectedIndex = 3;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>Roda a conferência agora (AutoRun: sem esperar a tarefa em segundo plano).</summary>
        public void ConferirMefAgora()
        {
            _cmp = MefEscada.Comparar(Resultado);
            Mostrar();
        }

        private void MostrarMef()
        {
            var ferr = FerramentasMef.Padrao;
            _resumoMef.Children.Clear();
            if (_cmp == null)
            {
                _resumoMef.Children.Add(new TextBlock
                {
                    Text = (ferr.TemOpenSees ? "OpenSees encontrado. " : "OpenSees não encontrado. ") + (ferr.TemCalculix ? "CalculiX encontrado." : "CalculiX não encontrado.") +
                           "\nCompara o cálculo com um pórtico no eixo (OpenSees) e com a chapa da laje, sem e com os degraus (CalculiX).",
                    TextWrapping = TextWrapping.Wrap, Foreground = Estilo.B(Estilo.Suave), FontSize = 11,
                });
                _mef.Content = new TextBlock { Text = "Clique em \"Conferir por MEF\" (à direita).", Margin = new Thickness(12), Foreground = Estilo.B(Estilo.Suave) };
                return;
            }
            var c = _cmp; var r = Resultado;
            foreach (string l in c.Linhas) _resumoMef.Children.Add(new TextBlock { Text = l, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4), FontSize = 11 });
            var grid = new Grid();
            for (int i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var marcas = c.QuinasX.Select(x => (x, "quina")).ToList();
            Grafico G(string tit, string un, bool baixo)
            {
                var gr = new Grafico { Titulo = tit, RotuloY = un, YParaBaixo = baixo, Margin = new Thickness(0, 0, 0, 6) };
                gr.Marcas.AddRange(marcas);
                return gr;
            }
            void S(Grafico gr, string nome, IEnumerable<(double, double?)> pts, WColor cor, bool trac, double esp = 1.6)
            {
                var l = pts.Where(p => p.Item2.HasValue).Select(p => (p.Item1, p.Item2.Value)).ToList();
                if (l.Count > 1) gr.Series.Add(new SerieGrafico { Nome = nome, Pontos = l, Cor = cor, Tracejada = trac, Espessura = esp });
            }
            var gm = G("Md — cálculo × MEF", "kN·m/m", true);
            S(gm, "cálculo", c.Pontos.Select(p => (p.X, (double?)p.MdCalculo)), Estilo.Anil, false, 2.4);
            S(gm, "OpenSees", c.Pontos.Select(p => (p.X, p.MdOpenSees)), Estilo.Ipe, true, 2);
            S(gm, "CalculiX (chapa)", c.Pontos.Select(p => (p.X, p.MdChapa)), Estilo.Hex("#c0392b"), true, 1.4);
            var gv = G("Vd e Nd — cálculo × MEF", "kN/m", false);
            S(gv, "V cálculo", c.Pontos.Select(p => (p.X, (double?)p.VdCalculo)), Estilo.Mata, false, 2.2);
            S(gv, "V OpenSees", c.Pontos.Select(p => (p.X, p.VdOpenSees)), Estilo.Ipe, true, 1.8);
            S(gv, "V CalculiX", c.Pontos.Select(p => (p.X, p.VdChapa)), Estilo.Hex("#c0392b"), true, 1.2);
            S(gv, "N cálculo", c.Pontos.Select(p => (p.X, (double?)p.NdCalculo)), Estilo.Hex("#8e44ad"), false, 1.6);
            S(gv, "N OpenSees", c.Pontos.Select(p => (p.X, p.NdOpenSees)), Estilo.Hex("#b07cc6"), true, 1.4);
            var gf = G("Flecha elástica (QP) — cálculo × MEF", "cm", true);
            S(gf, "cálculo", c.Pontos.Select(p => (p.X, (double?)p.FlechaCalculo)), Estilo.Anil, false, 2.4);
            S(gf, "OpenSees", c.Pontos.Select(p => (p.X, p.FlechaOpenSees)), Estilo.Ipe, true, 2);
            S(gf, "chapa sem degraus", c.Pontos.Select(p => (p.X, p.FlechaChapa)), Estilo.Hex("#c0392b"), true, 1.4);
            S(gf, "chapa com degraus", c.Pontos.Select(p => (p.X, p.FlechaChapaDegraus)), Estilo.Concreto, false, 1.4);
            Grid.SetRow(gm, 0); Grid.SetRow(gv, 1); Grid.SetRow(gf, 2);
            grid.Children.Add(gm); grid.Children.Add(gv); grid.Children.Add(gf);
            _mef.Content = Moldura(grid, 6);
        }

        // ------------------------------------------------------------------ pré-moldada

        private void MostrarLigacoes(ResultadoEscada r)
        {
            if (!r.Entrada.PreMoldada)
            {
                _ligacoes.Content = new TextBlock
                {
                    Text = r.Entrada.Execucao == ExecucaoEscada.PreMoldada ? "A pré-moldada está feita para a escada armada longitudinalmente; escolha esse tipo." : "Escada moldada no local: escolha \"Pré-moldada\" em Execução (à esquerda).",
                    Margin = new Thickness(12), Foreground = Estilo.B(Estilo.Suave), TextWrapping = TextWrapping.Wrap,
                };
                return;
            }
            var sp = new StackPanel { Margin = new Thickness(4) };
            double B = r.Entrada.Largura / 100;
            foreach (var lc in r.Ligacoes)
            {
                var l = lc.Ligacao; bool dente = l.Ponta == PontaPreMoldada.Dente;
                var pares = new List<(string, string)>
                {
                    ("Forma", dente ? $"dente {F(l.AlturaNoApoio(r.Geo.Hp), "0.#")} × {F(l.ComprimentoDente, "0")} cm" : "apoio direto (sem recorte)"),
                    ("Apoio", $"{PadroesPreMoldado.Nome(l.Apoio)} — comprimento {F(l.ComprimentoApoio, "0")} cm; Hd = {F(lc.CoefHd)}·Fd"),
                    ("Fd / Hd", $"{F(lc.Fd, "0.0")} / {F(lc.Hd, "0.0")} kN/m  →  {F(lc.Fd * B, "0.0")} / {F(lc.Hd * B, "0.0")} kN na ponta"),
                    (dente ? "Tirante do dente" : "Tirante (grampos em U)", $"{F(lc.AsTirante)} cm²/m → ø{F(lc.Tirante.Bitola, "0.#")} c/{F(lc.Tirante.Esp, "0.#")}" + (dente ? "" : $" (costura {F(lc.AsCostura)} cm²/m)")),
                };
                if (dente)
                {
                    pares.Add(("Suspensão", $"{F(lc.AsSuspensao)} cm²/m → grampos ø{F(lc.Suspensao.Bitola, "0.#")} c/{F(lc.Suspensao.Esp, "0.#")}"));
                    pares.Add(("Biela do dente", $"d = {F(lc.DDente, "0.0")} cm, a = {F(lc.ADente, "0.0")} cm; VRd2 = {F(lc.VRd2Dente, "0.0")} kN/m"));
                }
                pares.Add(("Pinos", $"{l.Pinos} ø{F(l.BitolaPino, "0.#")} (furo ø{F(l.FuroPino, "0")}, embutimento {F(l.EmbutimentoPino, "0")} cm): {F(l.Pinos * lc.RvdPino, "0.0")} kN ≥ Hd {F(lc.HTotal, "0.0")} kN"));
                sp.Children.Add(Estilo.Cartao($"Ponta {lc.Lado}", Estilo.Pares(pares, 170), new Thickness(0, 0, 0, 8)));
            }
            var ic = r.Icamento;
            if (ic != null)
                sp.Children.Add(Estilo.Cartao("Içamento e transporte", Estilo.Pares(new[]
                {
                    ("Peso da peça", $"{F(ic.Peso, "0.0")} kN ({F(ic.Peso * 101.97, "0")} kg)"),
                    ("Alças", $"4, a {F(ic.DistAlca, "0")} cm das pontas no eixo (0,207 de {F(ic.Comprimento, "0")} cm)"),
                    ("Força por alça", $"{F(ic.ForcaAlca, "0.0")} kN (peso × {F(r.Entrada.CoefDinamico)} / 3)"),
                    ("Md− / Md+", $"{F(ic.MdNeg)} / {F(ic.MdPos)} kN·m/m (fcj = {F(ic.Fcj, "0.#")} MPa)"),
                    ("Armadura superior", $"ø{F(r.SuperiorPm.Bitola, "0.#")} c/{F(r.SuperiorPm.Esp, "0.#")} (≥ {F(r.AsSuperiorPm)} cm²/m)"),
                }, 170)));
            _ligacoes.Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        // ------------------------------------------------------------------ otimizador

        private void MontarOtimizador()
        {
            var dp = new DockPanel();
            var crit = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var (rot, tb, un) in new[] { ("Concreto", _precoConc, "R$/m³"), ("Aço", _precoAco, "R$/kg"), ("h mín.", _hMin, "cm"), ("h máx.", _hMax, "cm") })
            {
                var c = Estilo.Campo(rot, tb, un, 70); c.Width = 210; c.Margin = new Thickness(0, 0, 14, 0);
                crit.Children.Add(c);
            }
            crit.Children.Add(Estilo.Primario("Otimizar", Otimizar));
            var topo = new StackPanel();
            topo.Children.Add(new TextBlock
            {
                Text = "Mantém o desnível e o vão entre os apoios; varia o número de espelhos, o piso (Blondel de 61 a 65 cm) e a espessura. " +
                       "A sobra do vão vai para os patamares. Só entram as opções que passam em todas as verificações de cálculo.",
                TextWrapping = TextWrapping.Wrap, Foreground = Estilo.B(Estilo.Suave), Margin = new Thickness(0, 0, 0, 8),
            });
            topo.Children.Add(crit);
            DockPanel.SetDock(topo, Dock.Top); dp.Children.Add(topo);
            _tabelaOtim.Content = new TextBlock { Text = "Clique em \"Otimizar\".", Foreground = Estilo.B(Estilo.Suave) };
            dp.Children.Add(new ScrollViewer { Content = _tabelaOtim, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            _otimizador.Content = new Border { Padding = new Thickness(10), Child = dp };
        }

        public void Otimizar()
        {
            if (!Ler(out string erro)) { MessageBox.Show(this, erro, "Escada"); return; }
            if (Entrada.SoPatamar) { _tabelaOtim.Content = new TextBlock { Text = "O otimizador é do lance (espelhos, piso e espessura). No patamar, ajuste a espessura e veja as verificações.", Foreground = Estilo.B(Estilo.Suave), TextWrapping = TextWrapping.Wrap }; return; }
            double Num(TextBox t, double padrao) => double.TryParse((t.Text ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : padrao;
            var c = new CriteriosOtimizacao { PrecoConcreto = Num(_precoConc, 650), PrecoAco = Num(_precoAco, 9), EspessuraMin = Num(_hMin, 10), EspessuraMax = Num(_hMax, 22) };
            var ops = OtimizadorEscada.Otimizar(Entrada, c);
            var atual = Resultado != null ? OtimizadorEscada.Consumo(Resultado) : (0, 0);
            var g = new Grid();
            string[] cab = { "", "Opção", "Armadura principal", "Concreto", "Aço", "Custo", "Flecha total", "" };
            double[] larg = { 30, 3, 1.6, 0.9, 0.8, 1, 1, 70 };
            for (int i = 0; i < cab.Length; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 || i == cab.Length - 1 ? new GridLength(larg[i]) : new GridLength(larg[i], GridUnitType.Star) });
            void Cel(int lin, int col, UIElement e) { Grid.SetRow(e, lin); Grid.SetColumn(e, col); g.Children.Add(e); }
            TextBlock T(string s, bool neg = false, WColor? cor = null) => new TextBlock
            {
                Text = s, Margin = new Thickness(4, 4, 4, 4), FontWeight = neg ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
                Foreground = Estilo.B(cor ?? Estilo.Tinta), VerticalAlignment = VerticalAlignment.Center,
            };
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int i = 0; i < cab.Length; i++) Cel(0, i, T(cab[i], true, Estilo.Suave));
            int l = 1;
            if (Resultado != null)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var r = Resultado;
                Cel(l, 0, T("•", true, Estilo.Ocre));
                Cel(l, 1, T("Atual: " + $"{r.Geo.N} × {F(r.Geo.Espelho, "0.0#")} / {F(r.Geo.Piso, "0")} · h = {F(r.Entrada.Espessura, "0")}", false, Estilo.Ocre));
                Cel(l, 2, T($"ø{F(r.Principal.Bitola, "0.#")} c/{F(r.Principal.Esp, "0.#")}", false, Estilo.Ocre));
                Cel(l, 3, T($"{F(atual.Item1, "0.000")} m³", false, Estilo.Ocre)); Cel(l, 4, T($"{F(atual.Item2, "0.0")} kg", false, Estilo.Ocre));
                Cel(l, 5, T($"R$ {F(atual.Item1 * c.PrecoConcreto + atual.Item2 * c.PrecoAco, "0")}", false, Estilo.Ocre));
                Cel(l, 6, T($"{F(r.FlechaTotal, "0.00")} cm", false, Estilo.Ocre));
                l++;
            }
            foreach (var o in ops)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var r = o.Resultado;
                Cel(l, 0, T((l - 1).ToString()));
                Cel(l, 1, T(o.Texto));
                Cel(l, 2, T($"ø{F(r.Principal.Bitola, "0.#")} c/{F(r.Principal.Esp, "0.#")}"));
                Cel(l, 3, T($"{F(o.Concreto, "0.000")} m³")); Cel(l, 4, T($"{F(o.Aco, "0.0")} kg"));
                Cel(l, 5, T($"R$ {F(o.Custo, "0")}", l == 2));
                Cel(l, 6, T($"{F(r.FlechaTotal, "0.00")} cm"));
                var ent = o.Entrada;
                var usar = Estilo.Botao("Usar", () => { Preencher(ent); Calcular(); _abas.SelectedIndex = 0; });
                usar.Margin = new Thickness(4, 2, 0, 2);
                Cel(l, 7, usar);
                l++;
            }
            if (ops.Count == 0) { g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Cel(l, 1, T("Nenhuma opção atende nas faixas escolhidas: aumente a espessura máxima.")); }
            _tabelaOtim.Content = g;
        }

        private static Border Moldura(UIElement e, double pad = 0) => new Border { BorderBrush = Estilo.B(Estilo.Linha), BorderThickness = new Thickness(1), Background = Brushes.White, Child = e, CornerRadius = new CornerRadius(4), Padding = new Thickness(pad) };
        private static string F(double v, string f = "0.##") => v.ToString(f, Pt);
    }
}
