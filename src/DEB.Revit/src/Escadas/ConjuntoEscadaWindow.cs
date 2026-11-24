using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using DEB.Core.Elementos.Escada;
using DEB.Core.Normas.Verificacao;
using DEB.Revit.UI;

namespace DEB.Revit.Escadas
{
    /// <summary>
    /// Janela "Escada em L/U" (D43): monta a escada por peças a partir de um arranjo pronto (em linha, em L com o patamar de um lance
    /// sobre o do outro, em U com o patamar como peça e a viga do patamar), deixa editar cada peça na janela da escada e mostra a planta,
    /// as cargas entre peças, o resumo das peças, a viga do patamar e as verificações. No aceite, o comando cria todas as peças no Revit.
    /// </summary>
    internal sealed class ConjuntoEscadaWindow : Window
    {
        private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
        private static readonly string[] Modelos = { "Em U — patamar como peça", "Em L — patamar sobre patamar", "Em linha — patamar sobre patamar" };
        private readonly ComboBox _modelo = new ComboBox { ItemsSource = Modelos, SelectedIndex = 0 };
        private readonly TextBox _folga = new TextBox { Text = "15" }, _prof = new TextBox { Text = "150" }, _apoioPat = new TextBox { Text = "10" },
                                 _vB = new TextBox { Text = "20" }, _vH = new TextBox { Text = "50" }, _vFck = new TextBox { Text = "40" };
        private readonly CheckBox _comViga = new CheckBox { Content = "Patamar apoiado na viga do patamar", IsChecked = true, Margin = new Thickness(0, 4, 0, 4) };
        private readonly StackPanel _pecas = new StackPanel(), _apoios = new StackPanel(), _resumoDir = new StackPanel();
        private readonly ConjuntoPlanta _planta = new ConjuntoPlanta();
        private readonly ContentControl _cargas = new ContentControl(), _tabPecas = new ContentControl(), _viga = new ContentControl(), _verif = new ContentControl();
        private readonly TabControl _abas = new TabControl();
        private readonly ContentControl _selo = new ContentControl();
        private readonly TextBlock _subtitulo = new TextBlock { Foreground = Estilo.B(Estilo.Suave), TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly Dictionary<ApoioEntrePecas, TextBox> _xApoio = new Dictionary<ApoioEntrePecas, TextBox>();
        private EscadaLaje _l1, _l2, _pat;

        public ConjuntoEscada Conjunto { get; private set; }
        public ResultadoConjunto Resultado { get; private set; }
        public TabControl Abas => _abas;

        public ConjuntoEscadaWindow(EscadaLaje lance, string origem = null)
        {
            _l1 = lance.Copia(); _l2 = lance.Copia();
            Title = "PCEB — Escada em L/U por peças";
            Width = 1500; Height = 900; MinWidth = 1150; MinHeight = 650;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Estilo.Aplicar(this);
            var raiz = new DockPanel();
            var cab = Estilo.Cabecalho("Escada em L e U — conjunto de peças",
                "Lances e patamares (no local ou pré-moldados) ligados pelas pontas; as reações passam de uma peça para a outra e para a viga do patamar", _selo);
            DockPanel.SetDock(cab, Dock.Top); raiz.Children.Add(cab);

            var rodape = new Border { Background = Estilo.B(Estilo.Branco), BorderBrush = Estilo.B(Estilo.Linha), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(14, 8, 14, 8) };
            var rp = new DockPanel();
            var botoes = new StackPanel { Orientation = Orientation.Horizontal };
            var ok = Estilo.Primario("Criar no Revit", () => { if (Recalcular() && Resultado.Erros.Count == 0) DialogResult = true; else MessageBox.Show(this, string.Join("\n", Resultado?.Erros ?? new List<string>()), "Escada em L/U"); });
            ok.IsDefault = true; ok.Margin = new Thickness(0, 0, 8, 0); ok.Padding = new Thickness(18, 5, 18, 5);
            botoes.Children.Add(ok);
            var cancel = Estilo.Botao("Cancelar", () => DialogResult = false); cancel.IsCancel = true; cancel.Padding = new Thickness(14, 5, 14, 5);
            botoes.Children.Add(cancel);
            DockPanel.SetDock(botoes, Dock.Right); rp.Children.Add(botoes);
            rp.Children.Add(new TextBlock
            {
                Text = (origem != null ? origem + " · " : "") + "Cada peça entra como família PCEB com as barras; a viga do patamar entra como peça de concreto (arme pelo comando de vigas).",
                Foreground = Estilo.B(Estilo.Suave), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            rodape.Child = rp;
            DockPanel.SetDock(rodape, Dock.Bottom); raiz.Children.Add(rodape);

            var corpo = new Grid { Margin = new Thickness(12, 10, 12, 6) };
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });

            // ---- esquerda: arranjo, peças, apoios ----
            var esq = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            var arr = new StackPanel();
            arr.Children.Add(new TextBlock { Text = "Arranjo", Foreground = Estilo.B(Estilo.Suave), Margin = new Thickness(0, 0, 0, 2) });
            arr.Children.Add(_modelo); _modelo.Margin = new Thickness(0, 0, 0, 6);
            arr.Children.Add(Estilo.Campo("Folga entre lances (U)", _folga, "cm", 150));
            arr.Children.Add(Estilo.Campo("Profundidade do patamar (U)", _prof, "cm", 150));
            arr.Children.Add(Estilo.Campo("Apoio do patamar na estrutura", _apoioPat, "cm", 150));
            arr.Children.Add(_comViga);
            arr.Children.Add(Estilo.Campo("Viga do patamar: b", _vB, "cm", 150));
            arr.Children.Add(Estilo.Campo("Viga do patamar: h", _vH, "cm", 150));
            arr.Children.Add(Estilo.Campo("Viga do patamar: fck", _vFck, "MPa", 150));
            arr.Children.Add(Estilo.Primario("Montar o arranjo", () => Montar()));
            esq.Children.Add(Estilo.Cartao("Arranjo", arr));
            esq.Children.Add(Estilo.Cartao("Peças", _pecas));
            var ap = new StackPanel();
            ap.Children.Add(_apoios);
            ap.Children.Add(new TextBlock { Text = "x = centro da carga no suporte (cm), a partir do apoio de baixo da peça ou da ponta esquerda da viga.", Foreground = Estilo.B(Estilo.Concreto), FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) });
            ap.Children.Add(Estilo.Primario("Recalcular", () => Recalcular()));
            esq.Children.Add(Estilo.Cartao("Apoios entre peças", ap));
            var sve = new ScrollViewer { Content = esq, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetColumn(sve, 0); corpo.Children.Add(sve);

            // ---- centro ----
            var centro = new DockPanel();
            DockPanel.SetDock(_subtitulo, Dock.Top); _subtitulo.Margin = new Thickness(0, 0, 0, 6); centro.Children.Add(_subtitulo);
            _abas.Items.Add(new TabItem { Header = "Planta", Content = new Border { BorderBrush = Estilo.B(Estilo.Linha), BorderThickness = new Thickness(1), Child = _planta } });
            _abas.Items.Add(new TabItem { Header = "Cargas entre peças", Content = _cargas });
            _abas.Items.Add(new TabItem { Header = "Peças", Content = _tabPecas });
            _abas.Items.Add(new TabItem { Header = "Viga do patamar", Content = _viga });
            _abas.Items.Add(new TabItem { Header = "Verificações", Content = _verif });
            centro.Children.Add(_abas);
            Grid.SetColumn(centro, 1); corpo.Children.Add(centro);

            var svd = new ScrollViewer { Content = _resumoDir, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(svd, 2); corpo.Children.Add(svd);
            raiz.Children.Add(corpo);
            Content = raiz;
            Montar();
        }

        private static double Num(TextBox t, double padrao) =>
            double.TryParse((t.Text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : padrao;
        private static string F(double x, string f = "0.##") => x.ToString(f, Pt);

        /// <summary>Escolhe o arranjo pelo índice (0 = U, 1 = L, 2 = em linha) e monta — para o autoteste.</summary>
        public void Montar(int modelo) { _modelo.SelectedIndex = modelo; Montar(); }

        /// <summary>Monta o conjunto do arranjo escolhido com os lances (e o patamar) atuais e recalcula.</summary>
        public void Montar()
        {
            VigaPatamar viga = _comViga.IsChecked == true ? new VigaPatamar { Nome = "VE", B = Num(_vB, 20), H = Num(_vH, 50), Fck = Num(_vFck, 40) } : null;
            Conjunto = _modelo.SelectedIndex switch
            {
                1 => ModelosConjunto.EmL(_l1, _l2),
                2 => ModelosConjunto.EmLinha(_l1, _l2),
                _ => ModelosConjunto.EmU(_l1, _l2, Num(_folga, 15), Num(_prof, 150), Num(_apoioPat, 10), viga, _pat),
            };
            // o arranjo pode ajustar a entrada (patamar do L1 no L): guarda o que ficou
            _l1 = Conjunto.Peca("L1").Copia(); _l2 = Conjunto.Peca("L2").Copia();
            if (Conjunto.Peca("PAT") != null) _pat = Conjunto.Peca("PAT").Copia();
            MontarListas();
            Recalcular();
        }

        private void MontarListas()
        {
            _pecas.Children.Clear();
            foreach (var (nome, e) in Conjunto.Pecas)
            {
                var linha = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var bt = Estilo.Botao("Editar…", () => Editar(nome)); bt.Padding = new Thickness(8, 2, 8, 2);
                DockPanel.SetDock(bt, Dock.Right); linha.Children.Add(bt);
                string txt = e.SoPatamar ? $"{nome}: patamar {F(e.ComprimentoPatamar, "0")} × {F(e.Largura, "0")}" : $"{nome}: lance, sobe {F(e.Desnivel, "0")}, largura {F(e.Largura, "0")}";
                linha.Children.Add(new TextBlock { Text = txt + (e.PreMoldada ? " · pré-moldado" : ""), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                _pecas.Children.Add(linha);
            }
            foreach (var v in Conjunto.Vigas)
                _pecas.Children.Add(new TextBlock { Text = $"{v.Nome}: viga do patamar {F(v.B, "0")}×{F(v.H, "0")}, {F(v.Comprimento, "0")} cm", Margin = new Thickness(0, 4, 0, 2) });
            _apoios.Children.Clear(); _xApoio.Clear();
            foreach (var a in Conjunto.Apoios)
            {
                var tb = new TextBox { Text = F(a.X, "0.#") };
                _xApoio[a] = tb;
                _apoios.Children.Add(Estilo.Campo($"{a.Apoiada} ({(a.Ponta == PontaPeca.Inferior ? "baixo" : "cima")}) → {a.Suporte}" + (a.Direcao == DirecaoApoio.AoLongo ? " · ao longo" : ""), tb, "cm", 190));
            }
        }

        /// <summary>Abre a peça na janela da escada; as cargas das outras peças aparecem nela, mas quem as mantém é o conjunto.</summary>
        private void Editar(string nome)
        {
            var e = Resultado != null && Resultado.Pecas.TryGetValue(nome, out var r) ? r.Entrada : Conjunto.Peca(nome);
            var w = new EscadaWindow(e, $"Peça {nome} do conjunto (as cargas das outras peças vêm do conjunto)", "Aplicar à peça") { Owner = this };
            if (w.ShowDialog() != true) return;
            var nova = w.Entrada.Copia();
            nova.CargasApoiadas = Conjunto.Peca(nome).CargasApoiadas.Select(c => c.Copia()).ToList();
            if (nome == "L1") _l1 = nova; else if (nome == "L2") _l2 = nova; else if (nome == "PAT") _pat = nova;
            Montar();   // a geometria da peça muda as posições
        }

        public bool Recalcular()
        {
            foreach (var kv in _xApoio) kv.Key.X = Num(kv.Value, kv.Key.X);
            Resultado = CalculoConjuntoEscada.Calcular(Conjunto);
            Mostrar();
            return true;
        }

        private IEnumerable<ResultadoVerificacao> TodasVerificacoes()
        {
            foreach (var kv in Resultado.Pecas)
                foreach (var v in kv.Value.Verificacoes) yield return Copia(v, kv.Key);
            foreach (var kv in Resultado.Vigas)
                foreach (var v in kv.Value.Verificacoes) yield return Copia(v, kv.Key);
        }

        private static ResultadoVerificacao Copia(ResultadoVerificacao v, string peca) => new ResultadoVerificacao
        {
            Codigo = v.Codigo, Grupo = peca + " — " + v.Grupo, Descricao = v.Descricao, Valor = v.Valor, Limite = v.Limite, Referencia = v.Referencia,
            Situacao = v.Situacao, Dica = v.Dica,
        };

        private void Mostrar()
        {
            var res = Resultado;
            var todas = TodasVerificacoes().ToList();
            _selo.Content = Estilo.Selo(res.Erros.Count > 0 ? SituacaoVerificacao.NaoAtende : todas.Count == 0 ? (SituacaoVerificacao?)null : todas.Max(v => v.Situacao), 110, 11);
            _subtitulo.Text = $"{Conjunto.Modelo} · {Conjunto.Pecas.Count} peças{(Conjunto.Vigas.Count > 0 ? $" e {Conjunto.Vigas.Count} viga" : "")} · ordem do cálculo: {string.Join(" → ", res.Ordem)} · {Estilo.Contagem(todas)}";
            _planta.Conjunto = Conjunto; _planta.Resultado = res; _planta.InvalidateVisual();

            // cargas entre peças
            var sc = new StackPanel { Margin = new Thickness(4) };
            void Titulo(StackPanel p, string t) => p.Children.Add(new TextBlock { Text = t, FontWeight = FontWeights.SemiBold, Foreground = Estilo.B(Estilo.Mata), Margin = new Thickness(0, 8, 0, 4) });
            void Linha(StackPanel p, string t, WColorRef cor = null) => p.Children.Add(new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1), Foreground = cor?.Pincel ?? Estilo.B(Estilo.Tinta) });
            foreach (string er in res.Erros) Linha(sc, "Erro: " + er, new WColorRef(Estilo.B(Estilo.Hex("#a33a2a"))));
            Titulo(sc, "Transferências (valores característicos)");
            foreach (string t in res.Transferencias) Linha(sc, "• " + t);
            Titulo(sc, "O que sai do conjunto para a estrutura");
            foreach (var r in res.ReacoesExternas) Linha(sc, $"• {r.Peca}, ponta {(r.Ponta == PontaPeca.Inferior ? "de baixo" : "de cima")}: G = {F(r.G, "0.0")} + Q = {F(r.Q, "0.0")} kN");
            foreach (var kv in res.Vigas) Linha(sc, $"• {kv.Key} (viga do patamar), por apoio: G = {F(kv.Value.RkE_G, "0.0")} / {F(kv.Value.RkD_G, "0.0")} + Q = {F(kv.Value.RkE_Q, "0.0")} / {F(kv.Value.RkD_Q, "0.0")} kN (com o peso dela)");
            if (res.Avisos.Count > 0)
            {
                Titulo(sc, "Avisos");
                foreach (string a in res.Avisos) Linha(sc, "• " + a, new WColorRef(Estilo.B(Estilo.Ocre)));
            }
            _cargas.Content = new ScrollViewer { Content = sc, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            // peças
            var sp = new StackPanel { Margin = new Thickness(4) };
            foreach (var (nome, _) in Conjunto.Pecas)
            {
                if (!res.Pecas.TryGetValue(nome, out var r)) continue;
                var vs = r.Verificacoes.ToList();
                var cab = new DockPanel { Margin = new Thickness(0, 8, 0, 2) };
                var selo = Estilo.Selo(vs.Count == 0 ? (SituacaoVerificacao?)null : vs.Max(v => v.Situacao), 90, 10);
                DockPanel.SetDock(selo, Dock.Right); cab.Children.Add(selo);
                cab.Children.Add(new TextBlock { Text = nome + (r.Entrada.SoPatamar ? " — patamar" : " — lance"), FontWeight = FontWeights.SemiBold, Foreground = Estilo.B(Estilo.Mata) });
                sp.Children.Add(cab);
                var pares = new List<(string, string)>
                {
                    ("Vão de cálculo", $"{F(r.VaoCalculo, "0")} cm"),
                    ("Md,máx / Vd,máx", $"{F(r.MdMax)} kN·m/m / {F(r.VdMax, "0.0")} kN/m"),
                    ("Armadura principal", r.Principal.Esp > 0 ? $"ø{F(r.Principal.Bitola, "0.#")} c/{F(r.Principal.Esp, "0.#")} = {F(r.AsPrincipal)} cm²/m" : "—"),
                    ("Reações (G + Q) por metro", $"{F(r.RaG, "0.0")} + {F(r.RaQ, "0.0")} / {F(r.RbG, "0.0")} + {F(r.RbQ, "0.0")} kN/m"),
                    ("Flecha total", $"{F(r.FlechaTotal)} cm"),
                };
                foreach (var lc in r.Ligacoes) pares.Add(($"Ligação {lc.Lado}", $"Fd {F(lc.Fd, "0.0")} kN/m · Hd {F(lc.Hd, "0.0")} kN/m"));
                if (r.Icamento != null) pares.Add(("Içamento", $"peça {F(r.Icamento.Peso, "0.0")} kN · alça {F(r.Icamento.ForcaAlca, "0.0")} kN"));
                if (r.Entrada.CargasApoiadas.Count > 0) pares.Add(("Recebe", string.Join("; ", r.Entrada.CargasApoiadas.Select(c => $"{c.Nome} {F(c.G, "0.0")} + {F(c.Q, "0.0")} kN/m"))));
                sp.Children.Add(Estilo.Pares(pares, 190));
            }
            _tabPecas.Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            // viga do patamar
            var sv = new StackPanel { Margin = new Thickness(4) };
            if (res.Vigas.Count == 0) Linha(sv, "Sem viga do patamar neste arranjo.");
            foreach (var kv in res.Vigas)
            {
                var v = kv.Value; var cv = res.CargasVigas[kv.Key];
                Titulo(sv, $"{kv.Key} — {F(v.B, "0")} × {F(v.H, "0")} cm, comprimento {F(v.L, "0")} cm, vão {F(v.Vao, "0")} cm");
                sv.Children.Add(Estilo.Pares(new List<(string, string)>
                {
                    ("Cargas do patamar", cv.G > 0 ? $"distribuída G {F(cv.G)} + Q {F(cv.Q)} kN/m" : $"{cv.Concentradas.Count} concentradas, total G {F(cv.Concentradas.Sum(p => p.G), "0.0")} + Q {F(cv.Concentradas.Sum(p => p.Q), "0.0")} kN"),
                    ("Peso próprio", $"{F(v.G1)} kN/m"),
                    ("Md,máx", $"{F(v.MdMax, "0.0")} kN·m em x = {F(v.XMdMax, "0")} cm"),
                    ("Vd nos apoios", $"{F(v.VdE, "0.0")} / {F(v.VdD, "0.0")} kN"),
                    ("Armadura (proposta)", string.Join("; ", v.ResumoProposta())),
                    ("Flecha total", $"{F(v.FlechaTotal)} cm"),
                }, 170));
                foreach (string a in v.Avisos) Linha(sv, "• " + a, new WColorRef(Estilo.B(Estilo.Ocre)));
            }
            _viga.Content = new ScrollViewer { Content = sv, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _verif.Content = new ScrollViewer { Content = Estilo.TabelaVerificacoes(todas), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            // direita: resumo
            _resumoDir.Children.Clear();
            var tot = new List<(string, string)>
            {
                ("Peças", string.Join(", ", Conjunto.Pecas.Select(p => p.Nome))),
                ("Sai para a estrutura (G + Q)", $"{F(res.ReacoesExternas.Sum(r => r.G), "0.0")} + {F(res.ReacoesExternas.Sum(r => r.Q), "0.0")} kN"),
            };
            foreach (var kv in res.Vigas) tot.Add(($"{kv.Key}: apoios (G + Q)", $"{F(kv.Value.RkE_G + kv.Value.RkD_G, "0.0")} + {F(kv.Value.RkE_Q + kv.Value.RkD_Q, "0.0")} kN"));
            foreach (var kv in res.Pecas.Where(p => p.Value.Icamento != null)) tot.Add(($"{kv.Key}: peso", $"{F(kv.Value.Icamento.Peso, "0.0")} kN"));
            _resumoDir.Children.Add(Estilo.Cartao("Conjunto", Estilo.Pares(tot, 150)));
            var pend = new StackPanel();
            pend.Children.Add(new TextBlock
            {
                Text = "O encaixe (rebaixo) da peça que recebe a outra não é modelado: as peças ficam com o topo nivelado e a ponta apoiada ocupa o mesmo volume no apoio. " +
                       "Confira o detalhe da ligação.",
                TextWrapping = TextWrapping.Wrap, Foreground = Estilo.B(Estilo.Suave),
            });
            _resumoDir.Children.Add(Estilo.Cartao("Atenção", pend));
        }

        /// <summary>Pincel opcional (para não depender de Brush nulo nos parâmetros).</summary>
        private sealed class WColorRef { public System.Windows.Media.Brush Pincel; public WColorRef(System.Windows.Media.Brush b) { Pincel = b; } }
    }
}
