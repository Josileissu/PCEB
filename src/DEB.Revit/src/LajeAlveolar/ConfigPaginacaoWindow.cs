using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DEB.Core.Lajes;
using DEB.Revit.Tools;

namespace DEB.Revit.LajeAlveolar
{
    /// <summary>
    /// Janela "Paginar laje alveolar — configuração" (WPF em código, como o resto do plugin; port da janela WinForms do protótipo).
    /// Medidas na tela em cm (painel) e mm (tirante, chapa, furo); no arquivo, em metros. "Restaurar padrão" volta os campos aos valores
    /// de fábrica (o arquivo só muda ao confirmar). Inclui a escolha da família de painel (quadro estrutural carregado no projeto).
    /// </summary>
    internal sealed class ConfigPaginacaoWindow
    {
        private sealed class Campo
        {
            public string Rotulo, Unidade, Ajuda; public double Fator;
            public Func<ConfigPaginacao, double> Ler; public Action<ConfigPaginacao, double> Gravar;
            public TextBox Caixa;
        }

        private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
        private readonly Window _w;
        private readonly ConfigPaginacao _cfg;
        private readonly Dictionary<string, List<string>> _familias;   // família → tipos
        private readonly List<(string Secao, Campo C)> _campos = new List<(string, Campo)>();
        private ComboBox _estr, _borda, _fam, _tipo, _barra, _catPainel;
        private CheckBox _suportes, _capa, _chaves, _tipoLargura;
        private TextBox _parLargura, _parEtapa, _prefP, _prefM, _prefJ, _matAco, _catArq;
        private readonly TextBox[] _etapas = new TextBox[3];
        private List<PainelCatalogo> _catalogo = new List<PainelCatalogo>();

        private static readonly (EstrategiaPaginacao V, string Texto, string Ajuda)[] Estrategias =
        {
            (EstrategiaPaginacao.AberturasPrimeiro, "Aberturas primeiro", "Junta no meio de cada abertura (dois painéis recortados); o resto com painéis inteiros e o ajuste junto à borda."),
            (EstrategiaPaginacao.MelhorDistribuicao, "Melhor distribuição", "Como 'aberturas primeiro', mas evita ajuste estreito: usa dois ajustes iguais (simétricos quando der)."),
            (EstrategiaPaginacao.DeUmLado, "De um lado", "Começa na borda escolhida com painéis inteiros; o ajuste fica no fim; aberturas viram entalhes."),
        };

        public ConfigPaginacaoWindow(ConfigPaginacao cfg, Dictionary<string, List<string>> familias, int nPisos)
        {
            _cfg = cfg; _familias = familias;
            _w = ToolDialogs.NewWindow("Paginar laje alveolar — configuração", 860);
            _w.SizeToContent = SizeToContent.Manual; _w.Height = 760; _w.ResizeMode = ResizeMode.CanResize;
            DefinirCampos();
            Montar(nPisos);
            Carregar(_cfg);
        }

        private void DefinirCampos()
        {
            void C(string sec, string rot, string un, double f, Func<ConfigPaginacao, double> l, Action<ConfigPaginacao, double> g, string aj)
                => _campos.Add((sec, new Campo { Rotulo = rot, Unidade = un, Fator = f, Ler = l, Gravar = g, Ajuda = aj }));
            const string P = "painel", V = "viga", T = "tirante", F = "fase3";
            C(P, "Módulo", "cm", 0.01, c => c.Modulo, (c, v) => c.Modulo = v, "Passo da paginação: largura do painel + junta (125 = 124,5 + 0,5).");
            C(P, "Largura do painel", "cm", 0.01, c => c.LarguraPainel, (c, v) => c.LarguraPainel = v, "Largura nominal do painel inteiro (catálogo do fornecedor).");
            C(P, "Altura do painel", "cm", 0.01, c => c.AlturaPainel, (c, v) => c.AlturaPainel = v, "Altura do painel; a base do painel = topo do piso − capa − altura.");
            C(P, "Capa", "cm", 0.01, c => c.Capa, (c, v) => c.Capa = v, "Espessura da capa (o piso do modelo inclui a capa).");
            C(P, "Junta entre painéis", "cm", 0.01, c => c.Junta, (c, v) => c.Junta = v, "Abertura no topo entre dois painéis (vira a chave de cisalhamento).");
            C(P, "Ajuste mínimo", "cm", 0.01, c => c.AjusteMin, (c, v) => c.AjusteMin = v, "Painel cortado mais estreito que isso não é usado: dois ajustes ou faixa maciça.");
            C(P, "Ajuste preferido", "cm", 0.01, c => c.AjusteBom, (c, v) => c.AjusteBom = v, "Só em 'melhor distribuição': ajuste menor que isso vira dois ajustes iguais.");
            C(P, "Faixa maciça mínima", "cm", 0.01, c => c.FaixaMacicaMin, (c, v) => c.FaixaMacicaMin = v, "Faixa maciça mais estreita que isso gera alerta de execução.");
            C(P, "Folga na alma (ponta)", "cm", 0.01, c => c.FolgaPonta, (c, v) => c.FolgaPonta = v, "Distância entre a ponta do painel e a face da alma da viga, em cada ponta.");
            C(P, "Folga em volta do pilar", "cm", 0.01, c => c.FolgaPilar, (c, v) => c.FolgaPilar = v, "Folga do recorte do painel em volta do pilar.");
            C(P, "Recorte mínimo", "cm", 0.01, c => c.RecorteMin, (c, v) => c.RecorteMin = v, "Recortes mais estreitos que isso (lascas) são desprezados.");
            C(P, "Apoio mínimo", "cm", 0.01, c => c.ApoioMin, (c, v) => c.ApoioMin = v, "Apoio do painel na aba da viga abaixo disso gera alerta.");
            C(P, "Consolo além do recorte", "cm", 0.01, c => c.ConsoloAlemDoRecorte, (c, v) => c.ConsoloAlemDoRecorte = v, "Peça cortada por furo junto à viga: quanto o consolo avança sob cada peça.");
            C(P, "Distância para apoio em consolo", "cm", 0.01, c => c.DistanciaConsolo, (c, v) => c.DistanciaConsolo = v, "Faixa maciça (ou peça cortada) a até esta distância da face da viga pede consolo.");
            C(V, "Leitura da alma", "cm", 0.01, c => c.LeituraViga.DzAlma, (c, v) => c.LeituraViga.DzAlma = v, "Cota (em relação à base do painel; negativo = abaixo) em que se mede a alma das vigas.");
            C(V, "Leitura da aba", "cm", 0.01, c => c.LeituraViga.DzAba, (c, v) => c.LeituraViga.DzAba = v, "Cota em que se mede a aba (logo abaixo da base do painel, onde ele apoia).");
            C(V, "Faixa do painel: de", "cm", 0.01, c => c.LeituraViga.DzPainelMin, (c, v) => c.LeituraViga.DzPainelMin = v, "Início da faixa (acima da base do painel) em que se vê se a viga sobe ao lado do painel.");
            C(V, "Faixa do painel: até", "cm", 0.01, c => c.LeituraViga.DzPainelMax, (c, v) => c.LeituraViga.DzPainelMax = v, "Fim dessa faixa.");
            C(T, "Diâmetro do tirante", "mm", 0.001, c => c.Tirante.Diametro, (c, v) => c.Tirante.Diametro = v, "Barra roscada que passa pela chave.");
            C(T, "Chapa: largura (transversal)", "mm", 0.001, c => c.Tirante.ChapaTransversal, (c, v) => c.Tirante.ChapaTransversal = v, "Dimensão da chapa atravessando a junta.");
            C(T, "Chapa: comprimento (na junta)", "mm", 0.001, c => c.Tirante.ChapaAoLongoJunta, (c, v) => c.Tirante.ChapaAoLongoJunta = v, "Dimensão da chapa no sentido da junta.");
            C(T, "Chapa: espessura", "mm", 0.001, c => c.Tirante.ChapaEspessura, (c, v) => c.Tirante.ChapaEspessura = v, "Espessura das chapas superior e inferior.");
            C(T, "Malha ao longo da junta", "m", 1, c => c.Tirante.MalhaLongitudinal, (c, v) => c.Tirante.MalhaLongitudinal = v, "Espaçamento dos tirantes em cada junta.");
            C(T, "Malha entre juntas", "m", 1, c => c.Tirante.MalhaTransversal, (c, v) => c.Tirante.MalhaTransversal = v, "Espaçamento transversal (hoje = todas as juntas).");
            C(T, "Margem à ponta da junta", "cm", 0.01, c => c.Tirante.MargemPontaJunta, (c, v) => c.Tirante.MargemPontaJunta = v, "Distância mínima do 1º/último tirante à ponta da junta (no mínimo meia chapa).");
            C(F, "Folga do furo em obra", "mm", 0.001, c => c.Fase3.FolgaFuro, (c, v) => c.Fase3.FolgaFuro = v, "Furo = diâmetro da barra + folga (padrão 3 mm).");
            C(F, "Passo do perfil da chave", "mm", 0.001, c => c.Fase3.PassoPerfilChave, (c, v) => c.Fase3.PassoPerfilChave = v, "Passo vertical da medição da junta nos sólidos dos painéis.");
        }

        private void Montar(int nPisos)
        {
            var corpo = new StackPanel { Margin = new Thickness(12) };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            corpo.Children.Add(new TextBlock { Text = $"{nPisos} piso(s) selecionado(s). Escolha a família do painel e confira as medidas.", Margin = new Thickness(0, 0, 0, 6) });
            corpo.Children.Add(g);

            void Titulo(string t)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var tb = new TextBlock { Text = t, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 4) };
                Grid.SetRow(tb, g.RowDefinitions.Count - 1); Grid.SetColumnSpan(tb, 3); g.Children.Add(tb);
            }
            void Linha(string rotulo, FrameworkElement c, string ajuda)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                int r = g.RowDefinitions.Count - 1;
                var l = new TextBlock { Text = rotulo, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 6, 3) };
                Grid.SetRow(l, r); g.Children.Add(l);
                c.Margin = new Thickness(0, 3, 6, 3); Grid.SetRow(c, r); Grid.SetColumn(c, 1); g.Children.Add(c);
                var a = new TextBlock { Text = ajuda ?? "", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 0, 3) };
                Grid.SetRow(a, r); Grid.SetColumn(a, 2); g.Children.Add(a);
                if (!string.IsNullOrEmpty(ajuda)) c.ToolTip = ajuda;
            }
            TextBox Caixa() => new TextBox { TextAlignment = TextAlignment.Right, Padding = new Thickness(2) };
            void Campos(string sec) { foreach (var (s, c) in _campos.Where(x => x.Secao == sec)) Linha($"{c.Rotulo} ({c.Unidade})", c.Caixa = Caixa(), c.Ajuda); }

            Titulo("Família do painel e inserção");
            _fam = new ComboBox { ItemsSource = _familias.Keys.OrderBy(k => k).ToList() };
            _tipo = new ComboBox();
            _fam.SelectionChanged += (s, e) => { _tipo.ItemsSource = _fam.SelectedItem is string f && _familias.TryGetValue(f, out var t) ? t : new List<string>(); if (_tipo.Items.Count > 0) _tipo.SelectedIndex = 0; };
            Linha("Família (quadro estrutural)", _fam, "Família de quadro estrutural carregada no projeto, usada para cada painel.");
            Linha("Tipo", _tipo, "Tipo do painel inteiro.");
            _tipoLargura = new CheckBox { Content = "criar tipo por largura", VerticalAlignment = VerticalAlignment.Center };
            Linha("Largura dos ajustes", _tipoLargura, "Marcado: um tipo por largura (parâmetro abaixo). Desmarcado: o painel inteiro recortado na lateral.");
            Linha("Parâmetro de largura (tipo)", _parLargura = new TextBox(), "Nome do parâmetro de tipo com a largura do painel.");
            Linha("Parâmetro da etapa", _parEtapa = new TextBox(), "Parâmetro de instância que recebe a etapa de concretagem.");
            Linha("Prefixo: painéis", _prefP = new TextBox(), "Marca dos painéis: prefixo + nº do pano + nº do painel (ex.: L01-03).");
            Linha("Prefixo: faixas maciças", _prefM = new TextBox(), "Marca das faixas maciças (ex.: M01-01).");
            Linha("Prefixo: juntas", _prefJ = new TextBox(), "Marca das juntas (chaves = CH-, tirantes = TS-).");

            Titulo("Estratégia de paginação");
            _estr = new ComboBox { ItemsSource = Estrategias.Select(e => e.Texto).ToList() };
            Linha("Estratégia", _estr, "");
            var lAj = (TextBlock)g.Children[g.Children.Count - 1];
            _borda = new ComboBox { ItemsSource = new List<string> { "início (v mínimo)", "fim (v máximo)" } };
            _estr.SelectionChanged += (s, e) => { int i = Math.Max(0, _estr.SelectedIndex); lAj.Text = Estrategias[i].Ajuda; _borda.IsEnabled = Estrategias[i].V == EstrategiaPaginacao.DeUmLado; };
            Linha("Borda de início ('de um lado')", _borda, "Borda onde começam os painéis inteiros (v = direção transversal ao vão).");

            Titulo("Painel, juntas e apoios");
            Campos("painel");
            Titulo("Catálogo do fornecedor (arquivo fora do repositório, opcional)");
            var linhaCat = new DockPanel();
            var btProcurar = new Button { Content = "Procurar…", Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(4, 0, 0, 0) };
            DockPanel.SetDock(btProcurar, Dock.Right); linhaCat.Children.Add(btProcurar);
            linhaCat.Children.Add(_catArq = new TextBox());
            Linha("Arquivo do catálogo", linhaCat, "JSON com os painéis do fornecedor: { \"paineis\": [ { \"nome\", \"altura\", \"largura\", \"junta\" } ] } (metros).");
            var linhaP = new DockPanel();
            var btAplicar = new Button { Content = "Aplicar", Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(4, 0, 0, 0) };
            DockPanel.SetDock(btAplicar, Dock.Right); linhaP.Children.Add(btAplicar);
            linhaP.Children.Add(_catPainel = new ComboBox());
            Linha("Painel do catálogo", linhaP, "Aplica largura, junta, módulo e altura do painel escolhido.");
            btProcurar.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Catálogo (*.json)|*.json|Todos (*.*)|*.*", Title = "Catálogo de painéis" };
                if (dlg.ShowDialog(_w) == true) { _catArq.Text = dlg.FileName; LerCatalogo(true); }
            };
            btAplicar.Click += (s, e) =>
            {
                if (_catPainel.SelectedIndex < 0 || _catPainel.SelectedIndex >= _catalogo.Count) return;
                var tmp = Ler(out _) ?? _cfg.Clonar();
                CatalogoPaineis.Aplicar(_catalogo[_catPainel.SelectedIndex], tmp);
                foreach (var (_, c) in _campos) c.Caixa.Text = Fmt(c.Ler(tmp) / c.Fator);
            };

            Titulo("Leitura das vigas (cotas em relação à base do painel)");
            Campos("viga");
            Titulo("Suportes de instalações (tirante na chave)");
            _suportes = new CheckBox { Content = "ligados", VerticalAlignment = VerticalAlignment.Center };
            Linha("Suportes de instalações", _suportes, "Liga/desliga os tirantes com chapas nas chaves (paginação e inserção).");
            Campos("tirante");
            _suportes.Checked += (s, e) => HabilitarTirante(); _suportes.Unchecked += (s, e) => HabilitarTirante();

            Titulo("Etapas de concretagem");
            string[] nomesEt = { "Painéis", "Chaves e faixas maciças", "Capa" };
            for (int i = 0; i < 3; i++) Linha("Etapa: " + nomesEt[i], _etapas[i] = Caixa(), i == 0 ? "Valor gravado no parâmetro da etapa." : null);

            Titulo("Fase 3 (depois dos painéis)");
            _capa = new CheckBox { Content = "trocar o piso para capa", VerticalAlignment = VerticalAlignment.Center };
            Linha("Capa", _capa, "O piso paginado passa a um tipo 'CAPA' de camada única (mesmo topo), etapa da capa.");
            _chaves = new CheckBox { Content = "criar chaves", VerticalAlignment = VerticalAlignment.Center };
            Linha("Chaves de cisalhamento", _chaves, "Forma medida entre os painéis do modelo; etapa das chaves.");
            _barra = new ComboBox { ItemsSource = new List<string> { "furo em obra", "inserir e avisar", "não inserir" } };
            Linha("Barra na junta estreita", _barra, "Quando a barra não cabe na parte inferior da junta. Furo em obra: na borda dos dois painéis, sem recortar a geometria; vai no tirante e no relatório.");
            Campos("fase3");
            Linha("Material dos tirantes", _matAco = new TextBox(), "Nome do material (vazio = procurar um aço no projeto).");

            var rodape = new DockPanel { Margin = new Thickness(12, 6, 12, 10) };
            var btPadrao = new Button { Content = "Restaurar padrão", Padding = new Thickness(10, 3, 10, 3) };
            btPadrao.Click += (s, e) =>
            {
                if (MessageBox.Show(_w, "Voltar todos os campos para os valores padrão? (o arquivo só muda ao confirmar)", _w.Title, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    var p = new ConfigPaginacao();
                    p.Insercao.FamiliaPainel = _fam.SelectedItem as string ?? ""; p.Insercao.TipoPainel = _tipo.SelectedItem as string ?? "";
                    Carregar(p);
                }
            };
            DockPanel.SetDock(btPadrao, Dock.Left); rodape.Children.Add(btPadrao);
            var bts = ToolDialogs.Buttons(_w, "Paginar…", Validar);
            bts.Margin = new Thickness(0);
            rodape.Children.Add(bts);

            var raiz = new DockPanel();
            DockPanel.SetDock(rodape, Dock.Bottom); raiz.Children.Add(rodape);
            raiz.Children.Add(new ScrollViewer { Content = corpo, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            _w.Content = raiz;
        }

        private void HabilitarTirante() { foreach (var (s, c) in _campos.Where(x => x.Secao == "tirante")) c.Caixa.IsEnabled = _suportes.IsChecked == true; }

        private static string Fmt(double v) => v.ToString("0.###", Br);

        private static bool Num(string s, out double v)
            => double.TryParse((s ?? "").Trim(), NumberStyles.Float, Br, out v) || double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        private void Carregar(ConfigPaginacao c)
        {
            if (_familias.ContainsKey(c.Insercao.FamiliaPainel ?? "")) _fam.SelectedItem = c.Insercao.FamiliaPainel;
            else if (_fam.Items.Count > 0 && _fam.SelectedIndex < 0) _fam.SelectedIndex = 0;
            if (_tipo.Items.Contains(c.Insercao.TipoPainel ?? "")) _tipo.SelectedItem = c.Insercao.TipoPainel;
            _tipoLargura.IsChecked = c.Insercao.CriarTipoPorLargura;
            _parLargura.Text = c.Insercao.ParametroLargura; _parEtapa.Text = c.Insercao.ParametroEtapa;
            _prefP.Text = c.Insercao.PrefixoPainel; _prefM.Text = c.Insercao.PrefixoMacica; _prefJ.Text = c.Insercao.PrefixoJunta;
            _estr.SelectedIndex = Math.Max(0, Array.FindIndex(Estrategias, e => e.V == c.Estrategia));
            _borda.SelectedIndex = c.BordaInicioVMin ? 0 : 1;
            foreach (var (_, f) in _campos) f.Caixa.Text = Fmt(f.Ler(c) / f.Fator);
            _suportes.IsChecked = c.SuportesInstalacoes; HabilitarTirante();
            _etapas[0].Text = c.Etapas.Paineis.ToString(Br); _etapas[1].Text = c.Etapas.ChavesEFaixasMacicas.ToString(Br); _etapas[2].Text = c.Etapas.Capa.ToString(Br);
            _capa.IsChecked = c.Fase3.Capa; _chaves.IsChecked = c.Fase3.Chaves;
            _barra.SelectedIndex = c.Fase3.BarraNaoCabe == TratamentoBarraJunta.FuroEmObra ? 0 : c.Fase3.BarraNaoCabe == TratamentoBarraJunta.InserirEAvisar ? 1 : 2;
            _matAco.Text = c.Fase3.MaterialAco;
            _catArq.Text = c.ArquivoCatalogo ?? "";
            LerCatalogo(false);
        }

        private void LerCatalogo(bool avisar)
        {
            _catalogo = new List<PainelCatalogo>();
            string arq = _catArq.Text.Trim();
            if (arq.Length > 0)
                try { _catalogo = CatalogoPaineis.LerArquivo(arq); }
                catch (Exception ex) { if (avisar) MessageBox.Show(_w, "Não consegui ler o catálogo:\n" + ex.Message, _w.Title); }
            _catPainel.ItemsSource = _catalogo.Select(p => p.ToString()).ToList();
            if (_catalogo.Count > 0) _catPainel.SelectedIndex = 0;
        }

        /// <summary>Lê a tela numa cópia da configuração; null + erros se algum número é inválido.</summary>
        private ConfigPaginacao Ler(out List<string> erros)
        {
            erros = new List<string>();
            var c = _cfg.Clonar();
            c.Insercao.FamiliaPainel = _fam.SelectedItem as string ?? "";
            c.Insercao.TipoPainel = _tipo.SelectedItem as string ?? "";
            c.Insercao.CriarTipoPorLargura = _tipoLargura.IsChecked == true;
            c.Insercao.ParametroLargura = _parLargura.Text.Trim(); c.Insercao.ParametroEtapa = _parEtapa.Text.Trim();
            c.Insercao.PrefixoPainel = _prefP.Text.Trim(); c.Insercao.PrefixoMacica = _prefM.Text.Trim(); c.Insercao.PrefixoJunta = _prefJ.Text.Trim();
            c.Estrategia = Estrategias[Math.Max(0, _estr.SelectedIndex)].V;
            c.BordaInicioVMin = _borda.SelectedIndex != 1;
            foreach (var (_, f) in _campos)
            {
                if (!Num(f.Caixa.Text, out double v)) { erros.Add(f.Rotulo + ": número inválido"); continue; }
                f.Gravar(c, Math.Round(v * f.Fator, 6));
            }
            c.SuportesInstalacoes = _suportes.IsChecked == true;
            var et = new int[3];
            for (int i = 0; i < 3; i++) if (!int.TryParse(_etapas[i].Text.Trim(), out et[i])) erros.Add("Etapa: número inteiro inválido");
            c.Etapas.Paineis = et[0]; c.Etapas.ChavesEFaixasMacicas = et[1]; c.Etapas.Capa = et[2];
            c.Fase3.Capa = _capa.IsChecked == true; c.Fase3.Chaves = _chaves.IsChecked == true;
            c.Fase3.BarraNaoCabe = _barra.SelectedIndex == 1 ? TratamentoBarraJunta.InserirEAvisar : _barra.SelectedIndex == 2 ? TratamentoBarraJunta.NaoInserir : TratamentoBarraJunta.FuroEmObra;
            c.Fase3.MaterialAco = _matAco.Text.Trim();
            c.ArquivoCatalogo = _catArq.Text.Trim();
            return erros.Count > 0 ? null : c;
        }

        private ConfigPaginacao _resultado;

        private bool Validar()
        {
            var c = Ler(out var erros);
            if (c != null)
            {
                erros.AddRange(c.Validar());
                if (string.IsNullOrEmpty(c.Insercao.FamiliaPainel) || string.IsNullOrEmpty(c.Insercao.TipoPainel)) erros.Add("Escolha a família e o tipo do painel.");
                if (string.IsNullOrEmpty(c.Insercao.PrefixoPainel) || string.IsNullOrEmpty(c.Insercao.PrefixoMacica) || string.IsNullOrEmpty(c.Insercao.PrefixoJunta)) erros.Add("Informe os prefixos das marcas.");
            }
            if (erros.Count > 0)
            {
                MessageBox.Show(_w, "Corrija antes de continuar:\n\n• " + string.Join("\n• ", erros), _w.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            _resultado = c;
            return true;
        }

        /// <summary>Mostra a janela; devolve a configuração confirmada (null = cancelado).</summary>
        public ConfigPaginacao Mostrar() => ToolDialogs.ShowModal(_w) ? _resultado : null;
    }
}
