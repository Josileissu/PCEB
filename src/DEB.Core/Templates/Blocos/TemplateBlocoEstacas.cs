using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Calculo;
using DEB.Core.Configuracao;
using DEB.Core.Estribos;
using DEB.Core.Modelo;
using DEB.Core.Normas;

namespace DEB.Core.Templates.Blocos;

/// <summary>
/// Bloco de fundação sobre estacas com arranque do pilar (pedido do usuário, 2026-11-06). Padrão de apresentação do
/// exemplo do escritório (BLOCO B3, 2 estacas ø25, 110 × 50 × 45, lastro 5):
/// <list type="bullet">
/// <item>ARMADURA: título "BLOCO B3" + lista dos pilares "(19x)", planta com as barras no lugar e as marcas A/B,
/// estribo horizontal ao lado, CORTE A-A e CORTE B-B (lastro, topo das estacas, pilar interrompido, barras em ponto e
/// chamadas), estribo vertical ao lado do corte B-B e as barras dobradas embaixo (superior em cima da principal) com
/// os rótulos ARMADURA PRINCIPAL / SUPERIOR / SECUNDÁRIA;</item>
/// <item>FORMA: planta com as cotas em cadeia (face → estacas → eixo do pilar → face), estacas com eixos e quadrantes
/// hachurados, pilar, e a elevação com a altura, o lastro e "LASTRO DE CONC. MAGRO".</item>
/// </list>
/// Regras medidas no exemplo: estribo horizontal = L − 2c (104 × 44); barras longitudinais dentro dele, retas
/// arredondadas ao cm (110 − 6 − 2·0,63 = 103); estribo vertical (L⊥ − 2c) × (h − c_sup − c_inf) = 44 × 35; perna da
/// inferior 15 (dado), da superior = h − c_sup − c_inf (35); principal em faixa centrada na linha de estacas; secundárias
/// nos cantos. Estacas em grade (nx × ny): 1, 2, 3 em linha, 4, 6, 9… — a direção com 2 ou mais estacas leva a
/// principal. Nada de dimensionamento: bitolas, quantidades e espaçamentos são do engenheiro.
/// </summary>
public sealed class TemplateBlocoEstacas : ITemplateArmadura
{
    public const string IdUmaEstaca = "bloco_1_estaca", IdEstacas = "bloco_estacas";
    public const string ArrDetalhar = "Detalhar (com N)", ArrDesenhar = "Só desenhar", ArrSem = "Sem arranque";
    public const string PeDentro = "Para dentro", PeFora = "Para fora";
    public const string EstAuto = "Automática", EstYZ = "Transversais (planos yz)", EstXZ = "Longitudinais (planos xz)",
        EstAmbas = "Nas duas direções", EstNenhum = "Sem estribos verticais";

    private readonly bool _uma;

    public TemplateBlocoEstacas() : this(false) { }

    /// <param name="umaEstaca">true = bloco de 1 estaca (mesmo template, outros valores padrão e outra categoria).</param>
    public TemplateBlocoEstacas(bool umaEstaca)
    {
        _uma = umaEstaca;
        Parametros = CriarParametros(umaEstaca);
    }

    public string Id => _uma ? IdUmaEstaca : IdEstacas;
    public string Nome => _uma ? "Bloco de 1 estaca com arranque do pilar" : "Bloco sobre estacas com arranque do pilar";
    public CategoriaElemento Categoria => _uma ? CategoriaElemento.Bloco1Estaca : CategoriaElemento.Bloco2Estacas;
    public string Descricao => "Bloco retangular sobre estacas em grade (1, 2, 3 em linha, 4, 6…): forma, planta, cortes A-A e B-B, " +
                               "armadura principal, superior, secundária, estribos verticais e horizontais e arranque do pilar.";
    public IReadOnlyList<ParametroTemplate> Parametros { get; }

    private static ParametroTemplate P(string grupo, string nome, string rotulo, string padrao, string unidade = "",
        TipoParametro tipo = TipoParametro.Numero, double min = double.NegativeInfinity, double max = double.PositiveInfinity,
        string descricao = "", params string[] opcoes) => new ParametroTemplate
    {
        Grupo = grupo, Nome = nome, Rotulo = rotulo, Padrao = padrao, Unidade = unidade, Tipo = tipo, Minimo = min, Maximo = max,
        Descricao = descricao, Opcoes = opcoes.ToList(),
    };

    private const string G1 = "01. Identificação", G2 = "02. Bloco", G3 = "03. Estacas", G4 = "04. Pilar", G5 = "05. Cobrimentos e materiais",
        G6 = "06. Armadura principal (inferior)", G7 = "07. Armadura superior", G8 = "08. Armadura secundária (cantos)",
        G9 = "09. Estribos", G10 = "10. Arranque do pilar", G11 = "11. Desenho";

    private static IReadOnlyList<ParametroTemplate> CriarParametros(bool uma) => new[]
    {
        P(G1, "Nome", "Nome do bloco", uma ? "B1" : "B3", tipo: TipoParametro.Texto, descricao: "O título fica \"BLOCO <nome>\"."),
        P(G1, "Pilares", "Pilares (lista)", uma ? "P1" : "P1, P2, P3", tipo: TipoParametro.Texto,
          descricao: "Pilares que usam este bloco, separados por vírgula ou \"e\" (ex.: P1, P2 e P5). A quantidade sai da lista."),
        P(G1, "Quantidade", "Quantidade de blocos", "0", tipo: TipoParametro.Inteiro, min: 0,
          descricao: "0 = o número de pilares da lista. Multiplica a relação do aço (detalhamento com repetições)."),

        P(G2, "Lx", "Comprimento (x)", uma ? "60" : "110", "cm", min: 20),
        P(G2, "Ly", "Largura (y)", uma ? "60" : "50", "cm", min: 20),
        P(G2, "h", "Altura", uma ? "50" : "45", "cm", min: 15),
        P(G2, "Lastro", "Lastro de concreto magro", "5", "cm", min: 0, descricao: "0 = sem lastro."),

        P(G3, "NEstX", "Estacas em x (colunas)", uma ? "1" : "2", tipo: TipoParametro.Inteiro, min: 1, max: 10),
        P(G3, "NEstY", "Estacas em y (linhas)", "1", tipo: TipoParametro.Inteiro, min: 1, max: 10),
        P(G3, "EspX", "Distância entre eixos em x", "75", "cm", min: 0),
        P(G3, "EspY", "Distância entre eixos em y", "75", "cm", min: 0),
        P(G3, "DiamEstaca", "Diâmetro das estacas", uma ? "30" : "25", "cm", min: 10),

        P(G4, "PilarBx", "Pilar: dimensão em x", uma ? "20" : "14", "cm", min: 10),
        P(G4, "PilarBy", "Pilar: dimensão em y", uma ? "20" : "26", "cm", min: 10),
        P(G4, "PilarEx", "Pilar: deslocamento do eixo em x", "0", "cm", descricao: "Em relação ao centro do bloco."),
        P(G4, "PilarEy", "Pilar: deslocamento do eixo em y", "0", "cm"),

        P(G5, "CobLateral", "Cobrimento lateral", "3", "cm", min: 1, max: 10, descricao: "Até a face externa dos estribos (exemplo: 110 − 2·3 = 104)."),
        P(G5, "CobSuperior", "Cobrimento superior", "5", "cm", min: 1, max: 15),
        P(G5, "CobInferior", "Cobrimento inferior", "5", "cm", min: 1, max: 20, descricao: "Do fundo do bloco ao estribo (acima do topo das estacas)."),
        P(G5, "Fck", "fck (para lb e traspasse)", "25", "MPa", min: 20, max: 90),
        P(G5, "Aco", "Aço", "CA-50", tipo: TipoParametro.Opcao, opcoes: new[] { "CA-50", "CA-60" }),

        P(G6, "PrincBitola", "Bitola", uma ? "10" : "12.5", "mm", TipoParametro.Bitola, 5, 40),
        P(G6, "PrincQtd", "Barras por faixa (sobre cada linha de estacas)", "4", tipo: TipoParametro.Inteiro, min: 0,
          descricao: "Bloco de 1 estaca não tem faixa principal (0 ou qualquer valor: só as secundárias)."),
        P(G6, "PrincEsp", "Espaçamento na faixa", "10", "cm", min: 2),
        P(G6, "PrincPerna", "Perna (dobra para cima)", "15", "cm", min: 0, descricao: "Exemplo: 15 (barra 103 + 2 × 15 = 133)."),
        P(G6, "PrincYBitola", "Direção y: bitola", "0", "mm", min: 0, max: 40, descricao: "0 = a mesma da direção x. Só com 2 ou mais linhas de estacas."),
        P(G6, "PrincYQtd", "Direção y: barras por faixa", "0", tipo: TipoParametro.Inteiro, min: 0, descricao: "0 = a mesma da direção x."),
        P(G6, "PrincYEsp", "Direção y: espaçamento", "0", "cm", min: 0, descricao: "0 = o mesmo da direção x."),

        P(G7, "SupBitola", "Bitola", "6.3", "mm", min: 0, max: 40, descricao: "0 = sem armadura superior."),
        P(G7, "SupQtd", "Barras por faixa", "4", tipo: TipoParametro.Inteiro, min: 0, descricao: "Na mesma faixa da principal."),
        P(G7, "SupEsp", "Espaçamento", "10", "cm", min: 2),
        P(G7, "SupPerna", "Perna (dobra para baixo)", "0", "cm", min: 0, descricao: "0 = altura do estribo vertical (h − cobrimentos; exemplo 35)."),

        P(G8, "SecBitola", "Inferior: bitola (1 barra por canto)", "6.3", "mm", min: 0, max: 40, descricao: "0 = sem."),
        P(G8, "SecSupBitola", "Superior: bitola (1 barra por canto)", "6.3", "mm", min: 0, max: 40, descricao: "0 = sem."),

        P(G9, "EstVBitola", "Verticais: bitola", "6.3", "mm", min: 0, max: 25, descricao: "0 = sem estribos verticais."),
        P(G9, "EstVEsp", "Verticais: espaçamento", "20", "cm", min: 5),
        P(G9, "EstVDirecao", "Verticais: direção", EstAuto, tipo: TipoParametro.Opcao,
          descricao: "Automática: transversais quando as estacas estão em linha em x; longitudinais em linha em y; nas duas direções no bloco de 1 estaca e em grade.",
          opcoes: new[] { EstAuto, EstYZ, EstXZ, EstAmbas, EstNenhum }),
        P(G9, "EstHBitola", "Horizontais (em volta): bitola", "6.3", "mm", min: 0, max: 25, descricao: "0 = sem estribos horizontais."),
        P(G9, "EstHEsp", "Horizontais: espaçamento", "15", "cm", min: 5),

        P(G10, "ArrModo", "Arranque", ArrDetalhar, tipo: TipoParametro.Opcao,
          descricao: "Detalhar = barras com N na relação do aço; Só desenhar = linhas nos cortes (detalhadas com o pilar).",
          opcoes: new[] { ArrDetalhar, ArrDesenhar, ArrSem }),
        P(G10, "ArrBitola", "Bitola", "10", "mm", TipoParametro.Bitola, 5, 40),
        P(G10, "ArrNx", "Barras por face paralela a x (com os cantos)", "2", tipo: TipoParametro.Inteiro, min: 1, max: 20),
        P(G10, "ArrNy", "Barras por face paralela a y (com os cantos)", "2", tipo: TipoParametro.Inteiro, min: 1, max: 20),
        P(G10, "ArrCob", "Cobrimento do pilar", "2.5", "cm", min: 1, max: 10),
        P(G10, "ArrEspera", "Espera acima do bloco", "0", "cm", min: 0, descricao: "0 = traspasse da NBR 6118 (100 % emendadas, fck do bloco), arredondado a 5 cm."),
        P(G10, "ArrPe", "Pé (dobra no fundo)", "10", "cm", min: 0),
        P(G10, "ArrPeSentido", "Sentido do pé", PeDentro, tipo: TipoParametro.Opcao, opcoes: new[] { PeDentro, PeFora }),
        P(G10, "ArrEstBitola", "Estribos do pilar no bloco: bitola", "5", "mm", min: 0, max: 16, descricao: "0 = sem."),
        P(G10, "ArrEstQtd", "Estribos do pilar no bloco: quantidade", "2", tipo: TipoParametro.Inteiro, min: 0),

        P(G11, "Escala", "Escala (1:n)", "25", min: 1),
        P(G11, "Forma", "Desenhar a forma", "Sim", tipo: TipoParametro.SimNao),
    };

    // ======================== modelo ========================

    /// <summary>Família de barras longitudinais (U) numa direção.</summary>
    private sealed class Familia
    {
        public string Nome = "", Rotulo = "";
        public bool EmX, Superior, Secundaria;
        public double Bitola, Esp, Reto, Perna;
        /// <summary>Coordenada transversal (y para as de x; x para as de y) de cada barra.</summary>
        public List<double> Posicoes = new List<double>();
        /// <summary>Altura (z, a partir do fundo do bloco) do trecho reto.</summary>
        public double Z;
        public BarraTemplate Barra;
        public string LayerLugar => Secundaria ? "ARM-BARRA-2" : "ARM-BARRA-DETALHE";
    }

    private sealed class Ctx
    {
        public ValoresTemplate V;
        public ConfiguracaoArmadura Cfg;
        public ResultadoTemplate R;
        public double Lx, Ly, h, lastro, c, cs, ci, esc, ht, D, Lp;
        public string Aco = "CA-50", Titulo = "", ListaPilares = "";
        public int Blocos;
        public List<double> ColX = new List<double>(), LinY = new List<double>();
        public double Px, Py, Bx, By;
        // armaduras
        public List<Familia> Familias = new List<Familia>();
        public double PhiV, PhiH, EspV, EspH, AltEst;
        public bool EstYZ, EstXZ;
        public BarraTemplate BarEstYZ, BarEstXZ, BarEstH, BarArr, BarEstPilar;
        public string ArrModo = ArrDetalhar;
        public double PhiA, PhiAE, ArrInterno, ArrEspera, ArrPe;
        public bool PeDentro = true;
        public List<double> ArrXs = new List<double>(), ArrYs = new List<double>();
        public List<(double X, double Y)> ArrPontos = new List<(double, double)>();
        public double XMax, YA, XB, ZFundoCortes;
        public Dictionary<BarraTemplate, int> Ordem = new Dictionary<BarraTemplate, int>();
    }

    private static string Num(double x) => CalculoComprimento.NumeroCompacto(x);

    /// <summary>Pilares da lista "P1, P2, P3 e P5" (vírgula, ponto e vírgula ou " e ").</summary>
    public static List<string> LerPilares(string s) =>
        (s ?? "").Replace(" e ", ",").Replace(" E ", ",").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim()).Where(p => p.Length > 0).ToList();

    /// <summary>Lista no padrão do título: "P1, P2 e P5 (3x)".</summary>
    public static string TextoPilares(List<string> ps, int qtd)
    {
        if (ps.Count == 0) return qtd > 1 ? $"({qtd}x)" : "";
        string lista = ps.Count == 1 ? ps[0] : string.Join(", ", ps.Take(ps.Count - 1)) + " e " + ps[ps.Count - 1];
        return $"{lista} ({qtd}x)";
    }

    public ResultadoTemplate Gerar(ValoresTemplate v, ConfiguracaoArmadura cfg)
    {
        var c = new Ctx { V = v, Cfg = cfg, R = new ResultadoTemplate() };
        Ler(c);
        Armaduras(c);
        PlantaArmadura(c);
        Corte(c, true);
        Corte(c, false);
        DobradasEmbaixo(c);
        if (v.SimNao("Forma")) Forma(c);
        Verificar(c);
        // numeração na ordem do exemplo: principal, superior, secundárias, estribos verticais, horizontal, arranque
        var ordenadas = c.R.Barras.OrderBy(b => c.Ordem.TryGetValue(b, out int k) ? k : 999).ToList();
        c.R.Barras.Clear();
        c.R.Barras.AddRange(ordenadas);
        c.R.PosicaoRA = (c.XMax + 12 * c.ht, c.Ly + 12 * c.ht);
        return c.R;
    }

    private static void Ler(Ctx c)
    {
        var v = c.V;
        c.Lx = v.Numero("Lx"); c.Ly = v.Numero("Ly"); c.h = v.Numero("h"); c.lastro = Math.Max(0, v.Numero("Lastro"));
        c.c = v.Numero("CobLateral"); c.cs = v.Numero("CobSuperior"); c.ci = v.Numero("CobInferior");
        c.esc = Math.Max(1, v.Numero("Escala"));
        c.ht = c.Cfg.AlturaTextoMm / 10.0 * c.esc;
        c.Aco = string.IsNullOrWhiteSpace(v["Aco"]) ? "CA-50" : v["Aco"];
        c.D = v.Numero("DiamEstaca");
        c.Lp = Math.Max(25, 1.2 * c.D);
        int nx = Math.Max(1, v.Inteiro("NEstX")), ny = Math.Max(1, v.Inteiro("NEstY"));
        double ex = v.Numero("EspX"), ey = v.Numero("EspY");
        for (int i = 0; i < nx; i++) c.ColX.Add(c.Lx / 2 + (i - (nx - 1) / 2.0) * ex);
        for (int j = 0; j < ny; j++) c.LinY.Add(c.Ly / 2 + (j - (ny - 1) / 2.0) * ey);
        c.Bx = v.Numero("PilarBx"); c.By = v.Numero("PilarBy");
        c.Px = c.Lx / 2 + v.Numero("PilarEx"); c.Py = c.Ly / 2 + v.Numero("PilarEy");

        string nome = string.IsNullOrWhiteSpace(v["Nome"]) ? "B1" : v["Nome"].Trim();
        c.Titulo = nome.StartsWith("BLOCO", StringComparison.OrdinalIgnoreCase) ? nome.ToUpperInvariant() : "BLOCO " + nome.ToUpperInvariant();
        var ps = LerPilares(v["Pilares"]);
        c.Blocos = v.Inteiro("Quantidade") > 0 ? v.Inteiro("Quantidade") : Math.Max(1, ps.Count);
        c.ListaPilares = TextoPilares(ps, c.Blocos);

        c.R.Escala = c.esc;
        c.R.Detalhamento = c.Titulo;
        c.R.DadosDetalhamento = new DadosDetalhamento
        {
            Grupo = "Fundações", Repeticoes = c.Blocos, Classe = "C" + Num(v.Numero("Fck")),
            Volume = c.Lx * c.Ly * c.h / 1e6, AreaForma = 2 * (c.Lx + c.Ly) * c.h / 1e4,
            Comentarios = "Gerado pelo template de bloco sobre estacas.",
        };
    }

    // ======================== armaduras (dados e formas dobradas) ========================

    private static int Qtd(Ctx c, double vao, double esp) =>
        CalculoFaixa.Quantidade(vao, esp, c.Cfg.EspacamentoInicialFaixa, c.Cfg.ModoQuantidadeFaixa);

    private static List<double> Posicoes(Ctx c, double vao, int n, double esp) =>
        CalculoFaixa.Posicoes(vao, n, esp, c.Cfg.EspacamentoInicialFaixa);

    private static void Armaduras(Ctx c)
    {
        var v = c.V;
        c.AltEst = c.h - c.cs - c.ci;
        c.PhiH = v.Numero("EstHBitola"); c.EspH = v.Numero("EstHEsp");
        c.PhiV = v.Numero("EstVBitola"); c.EspV = v.Numero("EstVEsp");
        bool linhaX = c.ColX.Count >= 2, linhaY = c.LinY.Count >= 2, uma = !linhaX && !linhaY;
        string dir = v["EstVDirecao"];
        if (c.PhiV > 0 && dir != EstNenhum)
        {
            if (dir == EstYZ) c.EstYZ = true;
            else if (dir == EstXZ) c.EstXZ = true;
            else if (dir == EstAmbas) c.EstYZ = c.EstXZ = true;
            else { c.EstYZ = linhaX || uma; c.EstXZ = linhaY || uma; }
        }

        // ---- longitudinais em U: direção x primeiro, depois y ----
        double phiP = v.Numero("PrincBitola"), espP = v.Numero("PrincEsp"); int qP = v.Inteiro("PrincQtd");
        double phiPy = v.Numero("PrincYBitola") > 0 ? v.Numero("PrincYBitola") : phiP;
        int qPy = v.Inteiro("PrincYQtd") > 0 ? v.Inteiro("PrincYQtd") : qP;
        double espPy = v.Numero("PrincYEsp") > 0 ? v.Numero("PrincYEsp") : espP;
        double phiS = v.Numero("SupBitola"), espS = v.Numero("SupEsp"); int qS = v.Inteiro("SupQtd");
        double pernaI = v.Numero("PrincPerna"), pernaS = v.Numero("SupPerna") > 0 ? v.Numero("SupPerna") : c.AltEst;
        double phiSec = v.Numero("SecBitola"), phiSecS = v.Numero("SecSupBitola");
        int ordem = 0;

        foreach (bool emX in new[] { true, false })
        {
            bool principal = emX ? linhaX : linhaY;
            bool cantos = principal || uma;
            double comp = emX ? c.Lx : c.Ly, larg = emX ? c.Ly : c.Lx;
            double reto = Math.Round(comp - 2 * c.c - 2 * c.PhiH / 10);
            // estribo vertical que envolve estas barras (o do plano perpendicular a elas)
            double phiEnv = (emX ? c.EstYZ : c.EstXZ) ? c.PhiV : 0;
            double tMin = c.c + phiEnv / 10, tMax = larg - c.c - phiEnv / 10;
            var linhas = emX ? c.LinY : c.ColX;
            // segunda camada (direção y) fica acima/abaixo da primeira
            double camada = emX ? 0 : (phiP / 10);

            List<double> Faixa(int n, double esp, double phi)
            {
                var r = new List<double>();
                foreach (var l in linhas)
                    for (int i = 0; i < n; i++)
                        r.Add(Math.Max(tMin + phi / 20, Math.Min(tMax - phi / 20, l + (i - (n - 1) / 2.0) * esp)));
                return r;
            }

            string sufixo = emX ? " x" : " y";
            if (principal && qP > 0 && (emX ? phiP : phiPy) > 0)
            {
                double phi = emX ? phiP : phiPy;
                c.Familias.Add(new Familia
                {
                    Nome = "principal" + sufixo, Rotulo = "ARMADURA PRINCIPAL", EmX = emX, Bitola = phi, Esp = emX ? espP : espPy,
                    Reto = reto, Perna = pernaI, Posicoes = Faixa(emX ? qP : qPy, emX ? espP : espPy, phi),
                    Z = c.ci + phiEnv / 10 + camada + phi / 20,
                });
            }
            if (principal && qS > 0 && phiS > 0)
                c.Familias.Add(new Familia
                {
                    Nome = "superior" + sufixo, Rotulo = "ARMADURA SUPERIOR", EmX = emX, Superior = true, Bitola = phiS, Esp = espS,
                    Reto = reto, Perna = pernaS, Posicoes = Faixa(qS, espS, phiS), Z = c.h - c.cs - phiEnv / 10 - (emX ? 0 : phiS / 10) - phiS / 20,
                });
            if (cantos && phiSec > 0)
                c.Familias.Add(new Familia
                {
                    Nome = "secundária inferior" + sufixo, Rotulo = "ARMADURA SECUNDÁRIA", EmX = emX, Secundaria = true, Bitola = phiSec,
                    Reto = reto, Perna = pernaI, Posicoes = { tMin + phiSec / 20, tMax - phiSec / 20 }, Z = c.ci + phiEnv / 10 + camada + phiSec / 20,
                });
            if (cantos && phiSecS > 0)
                c.Familias.Add(new Familia
                {
                    Nome = "secundária superior" + sufixo, Rotulo = "ARMADURA SECUNDÁRIA SUPERIOR", EmX = emX, Superior = true, Secundaria = true,
                    Bitola = phiSecS, Reto = reto, Perna = pernaS, Posicoes = { tMin + phiSecS / 20, tMax - phiSecS / 20 },
                    Z = c.h - c.cs - phiEnv / 10 - (emX ? 0 : phiSecS / 10) - phiSecS / 20,
                });
        }
        foreach (var f in c.Familias)
        {
            var b = new BarraTemplate
            {
                Nome = f.Nome, Bitola = f.Bitola, Aco = c.Aco, Quantidade = Math.Max(1, f.Posicoes.Count), Espacamento = f.Secundaria ? 0 : f.Esp,
                Escala = c.esc, Layer = f.Secundaria ? "ARM-BARRA-2" : "",
            };
            f.Barra = b;
            c.R.Barras.Add(b);
            c.Ordem[b] = ordem++;
        }

        // ---- estribos ----
        if (c.EstYZ)
        {
            int n = Qtd(c, c.Lx - 2 * c.c, c.EspV);
            c.BarEstYZ = Estribo(c, "estribo vertical (yz)", c.Ly - 2 * c.c, c.AltEst, c.PhiV, n, c.EspV);
            c.Ordem[c.BarEstYZ] = ordem++;
        }
        if (c.EstXZ)
        {
            int n = Qtd(c, c.Ly - 2 * c.c, c.EspV);
            c.BarEstXZ = Estribo(c, "estribo vertical (xz)", c.Lx - 2 * c.c, c.AltEst, c.PhiV, n, c.EspV);
            c.Ordem[c.BarEstXZ] = ordem++;
        }
        if (c.PhiH > 0)
        {
            int n = Qtd(c, c.AltEst, c.EspH);
            c.BarEstH = Estribo(c, "estribo horizontal", c.Lx - 2 * c.c, c.Ly - 2 * c.c, c.PhiH, n, c.EspH);
            c.Ordem[c.BarEstH] = ordem++;
        }

        // ---- arranque do pilar ----
        c.ArrModo = string.IsNullOrWhiteSpace(v["ArrModo"]) ? ArrDetalhar : v["ArrModo"];
        if (c.ArrModo == ArrSem) return;
        c.PhiA = v.Numero("ArrBitola"); c.PhiAE = v.Numero("ArrEstBitola");
        double d = v.Numero("ArrCob") + c.PhiAE / 10 + c.PhiA / 20;
        int ax = Math.Max(1, v.Inteiro("ArrNx")), ay = Math.Max(1, v.Inteiro("ArrNy"));
        for (int i = 0; i < ax; i++) c.ArrXs.Add(ax == 1 ? c.Px : c.Px - c.Bx / 2 + d + i * (c.Bx - 2 * d) / (ax - 1));
        for (int j = 0; j < ay; j++) c.ArrYs.Add(ay == 1 ? c.Py : c.Py - c.By / 2 + d + j * (c.By - 2 * d) / (ay - 1));
        for (int i = 0; i < ax; i++)
            for (int j = 0; j < ay; j++)
                if (i == 0 || j == 0 || i == ax - 1 || j == ay - 1) c.ArrPontos.Add((c.ArrXs[i], c.ArrYs[j]));
        // apoiado sobre a principal (ou o estribo vertical)
        double fundo = c.Familias.Where(f => !f.Superior).Select(f => f.Z + f.Bitola / 20).DefaultIfEmpty(c.ci + c.PhiV / 10).Max();
        c.ArrInterno = Math.Round(c.h - fundo);
        var tr = new RegrasTraspasse { Fck = v.Numero("Fck"), PercentualEmendado = 100 };
        c.ArrEspera = v.Numero("ArrEspera") > 0 ? v.Numero("ArrEspera") : Math.Ceiling(tr.Comprimento(c.PhiA, c.Aco) / 5) * 5;
        c.ArrPe = v.Numero("ArrPe");
        c.PeDentro = v["ArrPeSentido"] != PeFora;
        if (c.ArrModo == ArrDetalhar)
        {
            c.BarArr = new BarraTemplate { Nome = "arranque", Bitola = c.PhiA, Aco = c.Aco, Quantidade = c.ArrPontos.Count, Escala = c.esc };
            c.R.Barras.Add(c.BarArr);
            c.Ordem[c.BarArr] = ordem++;
            int ne = v.Inteiro("ArrEstQtd");
            if (c.PhiAE > 0 && ne > 0)
            {
                double cp = v.Numero("ArrCob");
                c.BarEstPilar = Estribo(c, "estribo do pilar no bloco", c.Bx - 2 * cp, c.By - 2 * cp, c.PhiAE, ne, 0);
                c.Ordem[c.BarEstPilar] = ordem++;
            }
        }
    }

    /// <summary>Estribo fechado (medidas externas w × h) com ganchos de 90° no canto superior esquerdo; posição acertada depois.</summary>
    private static BarraTemplate Estribo(Ctx c, string nome, double w, double h, double phi, int qtd, double esp)
    {
        var poligono = new List<(double X, double Y)> { (0, 0), (w, 0), (w, h), (0, h) };
        int canto = GeradorEstribo.Canto(poligono, PosicaoGanchos.SuperiorEsquerdo);
        double lg = c.Cfg.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho90, phi, c.Aco, estribo: true);
        double passagem = c.Cfg.Dobramento.DiametroPino(phi, c.Aco, estribo: true) + 2 * phi / 10;
        var f = GeradorEstribo.FechadoPoligono(poligono, canto, FormatoSegmento.Gancho90, lg, passagem);
        var b = new BarraTemplate
        {
            Nome = nome, Bitola = phi, Aco = c.Aco, Estribo = true, Escala = c.esc, Quantidade = Math.Max(1, qtd), Espacamento = esp,
        };
        b.Vertices.AddRange(f.Vertices.Select(p => (p.X, p.Y)));
        b.Formatos.AddRange(f.Formatos);
        foreach (var s in f.Sobrepostos) b.Sobrepostos.Add(s);
        foreach (var s in f.Cotados) b.Cotados.Add(s);
        c.R.Barras.Add(b);
        return b;
    }

    /// <summary>Leva o estribo (gerado na origem) para o canto inferior esquerdo (x, y) e põe a descrição embaixo.</summary>
    private static void Posicionar(Ctx c, BarraTemplate b, double x, double y, double w)
    {
        for (int i = 0; i < b.Vertices.Count; i++) b.Vertices[i] = (b.Vertices[i].X + x, b.Vertices[i].Y + y);
        b.PosicaoDescricao = (x + w / 2, y - 1.8 * c.ht);
        c.XMax = Math.Max(c.XMax, x + w + 6 * c.ht);
    }

    // ======================== auxiliares de desenho ========================

    private static void Texto(Ctx c, string s, double x, double y, double ht, int alinh = 0, string layer = "ARM-TEXTO-FORMA", double ang = 0) =>
        c.R.Textos.Add(new TextoTemplate { Texto = s, X = x, Y = y, Altura = ht, Alinhamento = alinh, Layer = layer, Angulo = ang });

    private static void Retangulo(Ctx c, double x0, double y0, double x1, double y1, string layer = "ARM-FORMA") =>
        c.R.Contornos.Add(new ContornoTemplate { Fechado = true, Layer = layer, Vertices = { (x0, y0), (x1, y0), (x1, y1), (x0, y1) } });

    private static void Linha(Ctx c, string layer, params (double, double)[] pts) =>
        c.R.Contornos.Add(new ContornoTemplate { Layer = layer, Vertices = pts.ToList() });

    private static void Ponto(Ctx c, double x, double y, double phi, string layer = "ARM-BARRA-SECAO") =>
        c.R.Circulos.Add(new CirculoTemplate { X = x, Y = y, Raio = Math.Max(phi / 20, 0.25), Layer = layer });

    private static void Cota(Ctx c, (double, double) p1, (double, double) p2, (double, double) pos, bool vertical) =>
        c.R.Cotas.Add(new CotaTemplate { P1 = p1, P2 = p2, Posicao = pos, Angulo = vertical ? Math.PI / 2 : 0, Escala = c.esc });

    private static void Indicacao(Ctx c, BarraTemplate b, (double, double) texto, IEnumerable<(double, double)> refs)
    {
        if (b == null) return;
        var f = new FaixaTemplate { Barra = b.Nome, Tipo = TipoFaixa.Indicacao, Escala = c.esc, Posicao = texto };
        f.Referencias.AddRange(refs);
        if (f.Referencias.Count > 0) c.R.Faixas.Add(f);
    }

    /// <summary>Título: "BLOCO B3" grande e a lista dos pilares embaixo (quebrada em linhas de até <paramref name="largura"/>).</summary>
    private static double Titulo(Ctx c, double x, double yBase, double largura)
    {
        double ht = c.ht;
        var linhas = new List<string>();
        if (c.ListaPilares.Length > 0)
        {
            int max = double.IsInfinity(largura) ? int.MaxValue : Math.Max(20, (int)(largura / (0.75 * ht)));
            string atual = "";
            foreach (var palavra in c.ListaPilares.Split(' '))
            {
                if (atual.Length > 0 && atual.Length + 1 + palavra.Length > max) { linhas.Add(atual); atual = palavra; }
                else atual = atual.Length == 0 ? palavra : atual + " " + palavra;
            }
            if (atual.Length > 0) linhas.Add(atual);
        }
        double y = yBase + linhas.Count * 1.6 * ht;
        Texto(c, c.Titulo, x, y + 0.8 * ht, 2.2 * ht);
        foreach (var l in linhas) { y -= 1.6 * ht; Texto(c, l, x, y + 0.8 * ht, ht); }
        return yBase + linhas.Count * 1.6 * ht + 3.5 * ht;
    }

    /// <summary>Pilar interrompido: duas faces do nível <paramref name="z0"/> até <paramref name="z1"/> e a linha de interrupção.</summary>
    private static void PilarInterrompido(Ctx c, double a, double b, double z0, double z1, string layer)
    {
        double ht = c.ht, m = (a + b) / 2 + 0.25 * (b - a);
        Linha(c, layer, (a, z0), (a, z1));
        Linha(c, layer, (b, z0), (b, z1));
        Linha(c, "ARM-FORMA", (a - 2.5 * ht, z1), (m - 0.3 * ht, z1), (m - 0.1 * ht, z1 + 0.7 * ht), (m + 0.1 * ht, z1 - 0.7 * ht), (m + 0.3 * ht, z1), (b + 2.5 * ht, z1));
    }

    /// <summary>Lastro (hachurado entre as estacas) e as estacas cortadas abaixo do bloco (de x0 a x1 do corte).</summary>
    private static void LastroEEstacas(Ctx c, double x0, double x1, double zb, IEnumerable<double> centros)
    {
        double R = c.D / 2, zl = zb - c.lastro, zp = zb - c.lastro - c.Lp;
        var cs = centros.OrderBy(x => x).ToList();
        if (c.lastro > 0)
        {
            Linha(c, "ARM-FORMA", (x0, zl), (x1, zl));
            Linha(c, "ARM-FORMA", (x0, zb), (x0, zl));
            Linha(c, "ARM-FORMA", (x1, zb), (x1, zl));
            double a = x0;
            foreach (var xc in cs.Concat(new[] { double.PositiveInfinity }))
            {
                double b = Math.Min(x1, xc - R);
                if (b - a > 0.5)
                    c.R.Hachuras.Add(new HachuraTemplate { Vertices = { (a, zl), (b, zl), (b, zb), (a, zb) }, Padrao = "AR-CONC", EscalaPadrao = c.esc / 50, Layer = "ARM-HACHURA-DETALHE" });
                a = Math.Max(a, xc + R);
                if (a >= x1) break;
            }
        }
        foreach (var xc in cs)
        {
            Linha(c, "ARM-ESTACA", (xc - R, zb), (xc - R, zp));
            Linha(c, "ARM-ESTACA", (xc + R, zb), (xc + R, zp));
            Linha(c, "ARM-ESTACA", (xc - R / 2, zb), (xc - R / 2, zp));
            Linha(c, "ARM-ESTACA", (xc + R / 2, zb), (xc + R / 2, zp));
            // interrupção da estaca: dois arcos (S)
            c.R.Contornos.Add(new ContornoTemplate { Layer = "ARM-ESTACA", Vertices = { (xc - R, zp), (xc, zp), (xc + R, zp) }, Bulges = { 0.5, -0.5, 0 } });
            c.R.Contornos.Add(new ContornoTemplate { Layer = "ARM-ESTACA", Vertices = { (xc - R, zp), (xc, zp) }, Bulges = { -0.5, 0 } });
        }
    }

    // ======================== planta da armadura ========================

    private static void PlantaArmadura(Ctx c)
    {
        double ht = c.ht, Lx = c.Lx, Ly = c.Ly;
        // título
        Titulo(c, 0, Ly + 7 * ht, double.PositiveInfinity);   // numa linha só, como no exemplo
        Retangulo(c, 0, 0, Lx, Ly);
        foreach (var x in c.ColX) foreach (var y in c.LinY) c.R.Circulos.Add(new CirculoTemplate { X = x, Y = y, Raio = c.D / 2, Layer = "ARM-FORMA" });
        // estribo horizontal no lugar
        if (c.BarEstH != null) Retangulo(c, c.c, c.c, Lx - c.c, Ly - c.c, "ARM-BARRA-DETALHE");
        // barras longitudinais no lugar
        foreach (var f in c.Familias)
        {
            double a0 = ((f.EmX ? Lx : Ly) - f.Reto) / 2, a1 = a0 + f.Reto;
            foreach (var t in f.Posicoes.Distinct())
                if (f.EmX) Linha(c, f.LayerLugar, (a0, t), (a1, t));
                else Linha(c, f.LayerLugar, (t, a0), (t, a1));
        }
        // arranque: barras em ponto e o estribo do pilar
        if (c.ArrPontos.Count > 0)
        {
            foreach (var (x, y) in c.ArrPontos) Ponto(c, x, y, c.PhiA);
            if (c.PhiAE > 0)
            {
                double cp = c.V.Numero("ArrCob");
                Retangulo(c, c.Px - c.Bx / 2 + cp, c.Py - c.By / 2 + cp, c.Px + c.Bx / 2 - cp, c.Py + c.By / 2 - cp, "ARM-ESTRIBO-SECAO");
            }
        }
        // cotas totais
        Cota(c, (0, Ly), (Lx, Ly), (Lx / 2, Ly + 3 * ht), false);
        Cota(c, (0, 0), (0, Ly), (-3 * ht, Ly / 2), true);

        // marcas dos cortes: A na linha de estacas mais perto do meio; B na última coluna de estacas
        c.YA = c.LinY.OrderBy(y => Math.Abs(y - Ly / 2)).First();
        if (Math.Abs(c.YA - c.Py) < 1e-6 && c.LinY.Count == 1) c.YA = c.LinY[0];
        c.XB = c.ColX.Count > 1 ? c.ColX.Max() : c.Lx / 2;
        double e = 4 * ht;
        Linha(c, "ARM-CORTE", (-e, c.YA), (Lx + e, c.YA));
        Linha(c, "ARM-CORTE", (c.XB, -e), (c.XB, Ly + e));
        foreach (var x in new[] { -e + ht, Lx + e - ht })
        {
            Linha(c, "ARM-CORTE", (x - 0.8 * ht, c.YA - 1.2 * ht), (x + 0.8 * ht, c.YA - 1.2 * ht), (x, c.YA), (x - 0.8 * ht, c.YA - 1.2 * ht));
            Texto(c, "A", x, c.YA + 0.6 * ht, 1.5 * ht, 1, "ARM-CORTE");
        }
        foreach (var y in new[] { -e + ht, Ly + e - ht })
        {
            Linha(c, "ARM-CORTE", (c.XB - 1.2 * ht, y - 0.8 * ht), (c.XB - 1.2 * ht, y + 0.8 * ht), (c.XB, y), (c.XB - 1.2 * ht, y - 0.8 * ht));
            Texto(c, "B", c.XB - 1.6 * ht, y - 0.7 * ht, 1.5 * ht, 2, "ARM-CORTE", Math.PI / 2);
        }

        // estribo horizontal dobrado ao lado da planta
        double xd = Lx + 14 * ht;
        if (c.BarEstH != null) Posicionar(c, c.BarEstH, xd, c.c, Lx - 2 * c.c);
        c.XMax = Math.Max(c.XMax, Lx + 8 * ht);
    }

    // ======================== cortes A-A e B-B ========================

    /// <summary>
    /// Corte A-A (plano xz em y = yA, desenhado em x a partir de 0) ou B-B (plano yz em x = xB, desenhado a partir de
    /// Lx + 14 alturas). Fundo do bloco em zb (abaixo da planta).
    /// </summary>
    private static void Corte(Ctx c, bool emA)
    {
        double ht = c.ht, h = c.h;
        double stub = Math.Max(20, 4 * ht);
        double zt = -12 * ht - stub, zb = zt - h;
        c.ZFundoCortes = zb;
        double L = emA ? c.Lx : c.Ly, ox = emA ? 0 : c.Lx + 14 * ht;
        double X(double s) => ox + s;
        double Z(double z) => zb + z;

        Texto(c, emA ? "CORTE A-A" : "CORTE B-B", X(0), zt + stub + 1.5 * ht, 1.2 * ht);
        Retangulo(c, X(0), zb, X(L), zt);
        // pilar acima (interrompido)
        double pa = emA ? c.Px - c.Bx / 2 : c.Py - c.By / 2, pb = emA ? c.Px + c.Bx / 2 : c.Py + c.By / 2;
        PilarInterrompido(c, X(pa), X(pb), zt, zt + stub, "ARM-FORMA");
        // estacas cortadas: as da linha (A) ou da coluna (B) do corte
        LastroEEstacas(c, X(0), X(L), zb, (emA ? c.ColX : c.LinY).Select(X));

        // ---- estribos ----
        var estPar = emA ? c.BarEstXZ : c.BarEstYZ;     // no plano do corte: aparece como retângulo
        var estPerp = emA ? c.BarEstYZ : c.BarEstXZ;    // perpendicular: linhas verticais
        double zi = c.ci, zs = c.h - c.cs;
        if (estPar != null) Retangulo(c, X(c.c), Z(zi), X(L - c.c), Z(zs), "ARM-ESTRIBO-SECAO");
        if (estPerp != null)
        {
            double vao = (emA ? c.Lx : c.Ly) - 2 * c.c;
            var ps = Posicoes(c, vao, estPerp.Quantidade, c.EspV).Select(p => p + c.c).ToList();
            foreach (var p in ps) Linha(c, "ARM-ESTRIBO-SECAO", (X(p), Z(zi)), (X(p), Z(zs)));
            // chamada no meio, apontando para os dois estribos mais perto do pilar
            var perto = ps.OrderBy(p => Math.Abs(X(p) - X((pa + pb) / 2))).Take(2).ToList();
            Indicacao(c, estPerp, (X(Math.Min(perto.Min(), (pa + pb) / 2) - 3 * ht), Z(zs - 0.25 * c.AltEst)),
                perto.Select(p => (X(p), Z(zi + 0.6 * c.AltEst))));
        }
        // estribo horizontal: pontos nas duas faces, chamadas dos dois lados
        if (c.BarEstH != null)
        {
            var zs3 = Posicoes(c, c.AltEst, c.BarEstH.Quantidade, c.EspH).Select(z => z + c.ci).ToList();
            double r = c.PhiH / 20;
            foreach (var s in new[] { c.c + r, L - c.c - r })
                foreach (var z in zs3) Ponto(c, X(s), Z(z), c.PhiH);
            Indicacao(c, c.BarEstH, (X(0) - 5 * ht, zt + 1.5 * ht), zs3.Select(z => (X(c.c + r), Z(z))));
            Indicacao(c, c.BarEstH, (X(L) + 5 * ht, zt + 1.5 * ht), zs3.Select(z => (X(L - c.c - r), Z(z))));
        }

        // ---- longitudinais ----
        foreach (var f in c.Familias)
        {
            bool paralela = f.EmX == emA;
            if (paralela)
            {
                // U no plano do corte (todas as barras da família se sobrepõem)
                double a0 = (L - f.Reto) / 2, a1 = a0 + f.Reto, z = f.Z, zp = f.Superior ? z - f.Perna : z + f.Perna;
                // sem chamada aqui: como no exemplo, a família é chamada no corte em que aparece em ponto
                Linha(c, f.LayerLugar, (X(a0), Z(zp)), (X(a0), Z(z)), (X(a1), Z(z)), (X(a1), Z(zp)));
            }
            else
            {
                // barras em ponto; uma chamada com linhas para todas
                var pts = f.Posicoes.Select(t => (X(t), Z(f.Z))).ToList();
                foreach (var (px, pz) in pts) Ponto(c, px, pz, f.Bitola);
                if (f.Secundaria)
                {
                    // cantos: uma chamada por canto, fora do bloco (como N3/N4 do exemplo)
                    foreach (var (px, pz) in pts)
                    {
                        bool esq = px < X(L / 2);
                        double ty = f.Superior ? zt + 4 * ht : zb - c.lastro - 1.5 * ht;
                        Indicacao(c, f.Barra, (esq ? X(0) - 5 * ht : X(L) + 5 * ht, ty), new[] { (px, pz) });
                    }
                }
                else
                {
                    double ty = f.Superior ? Z(c.h * 0.62) : Z(c.h * 0.38);
                    Indicacao(c, f.Barra, (X(Math.Max(3 * ht, L * 0.22)), ty), pts);
                }
            }
        }

        // ---- arranque ----
        if (c.ArrModo != ArrSem && c.ArrPontos.Count > 0)
        {
            var ts = (emA ? c.ArrXs : c.ArrYs).ToList();
            double centro = emA ? c.Px : c.Py;
            double zf = c.h - c.ArrInterno;
            string layer = "ARM-BARRA-DETALHE";
            foreach (var t in ts)
            {
                double s = Math.Abs(t - centro) < 1e-6 ? 1 : Math.Sign(centro - t) * (c.PeDentro ? 1 : -1);
                double pe = Math.Min(c.ArrPe, c.PeDentro && ts.Count > 1 ? Math.Abs(t - centro) * 0.95 : c.ArrPe);
                Linha(c, layer, (X(t + s * pe), Z(zf)), (X(t), Z(zf)), (X(t), zt + stub));
            }
            if (c.BarEstPilar != null)
            {
                int ne = c.BarEstPilar.Quantidade;
                double cp = c.V.Numero("ArrCob");
                for (int i = 0; i < ne; i++)
                {
                    double z = c.h - 5 - i * Math.Max(5, (c.ArrInterno - 10) / Math.Max(1, ne));
                    Linha(c, "ARM-ESTRIBO-SECAO", (X(pa + cp), Z(z)), (X(pb - cp), Z(z)));
                }
            }
            if (c.BarArr != null)
                Indicacao(c, c.BarArr, (X(pb) + 4 * ht, zt + stub * 0.6), new[] { (X(ts.Max()), zt + stub * 0.4) });
        }

        // estribo vertical (do plano do corte B) dobrado ao lado do corte B-B
        if (!emA)
        {
            double xd = X(L) + 14 * ht;
            if (c.BarEstYZ != null) { Posicionar(c, c.BarEstYZ, xd, Z(c.ci), c.Ly - 2 * c.c); xd += c.Ly - 2 * c.c + 14 * ht; }
            if (c.BarEstXZ != null) Posicionar(c, c.BarEstXZ, xd, Z(c.ci), c.Lx - 2 * c.c);
        }
        c.XMax = Math.Max(c.XMax, X(L) + 8 * ht);
    }

    // ======================== barras dobradas (embaixo dos cortes) ========================

    private static void DobradasEmbaixo(Ctx c)
    {
        double ht = c.ht;
        double yTopo = c.ZFundoCortes - c.lastro - c.Lp - 10 * ht;
        // colunas: (superior, inferior) por direção e tipo — como no exemplo (N2 sobre N1, N4 sobre N3)
        var colunas = new List<(Familia Sup, Familia Inf)>();
        foreach (bool emX in new[] { true, false })
            foreach (bool sec in new[] { false, true })
            {
                var sup = c.Familias.FirstOrDefault(f => f.EmX == emX && f.Secundaria == sec && f.Superior);
                var inf = c.Familias.FirstOrDefault(f => f.EmX == emX && f.Secundaria == sec && !f.Superior);
                if (sup != null || inf != null) colunas.Add((sup, inf));
            }
        double x = 0, yMin = yTopo;
        foreach (var (sup, inf) in colunas)
        {
            double larg = Math.Max(sup?.Reto ?? 0, inf?.Reto ?? 0);
            double y = yTopo;
            if (sup != null)
            {
                // U invertido: pernas para baixo
                var b = sup.Barra;
                b.Vertices.AddRange(new[] { (x, y - sup.Perna), (x, y), (x + sup.Reto, y), (x + sup.Reto, y - sup.Perna) });
                b.Formatos.AddRange(new[] { FormatoSegmento.Linha, FormatoSegmento.Linha, FormatoSegmento.Linha });
                b.PosicaoDescricao = (x + sup.Reto / 2, y - 1.6 * ht);
                Texto(c, sup.Rotulo, x + sup.Reto / 2, y - 3.6 * ht, 0.8 * ht, 1);
                y -= sup.Perna + 6 * ht;
            }
            if (inf != null)
            {
                double yb = y - inf.Perna;
                var b = inf.Barra;
                b.Vertices.AddRange(new[] { (x, yb + inf.Perna), (x, yb), (x + inf.Reto, yb), (x + inf.Reto, yb + inf.Perna) });
                b.Formatos.AddRange(new[] { FormatoSegmento.Linha, FormatoSegmento.Linha, FormatoSegmento.Linha });
                // a cota do trecho reto fica embaixo (fora do U): descrição e rótulo abaixo dela
                b.PosicaoDescricao = (x + inf.Reto / 2, yb - 3.4 * ht);
                Texto(c, inf.Rotulo, x + inf.Reto / 2, yb - 5.6 * ht, 0.8 * ht, 1);
                y = yb - 8 * ht;
            }
            yMin = Math.Min(yMin, y);
            x += larg + 16 * ht;
        }
        // arranque e estribo do pilar
        if (c.BarArr != null)
        {
            double lv = c.ArrInterno + c.ArrEspera, x0 = x + 2 * ht;
            c.BarArr.Vertices.AddRange(new[] { (x0 + c.ArrPe, yTopo - lv), (x0, yTopo - lv), (x0, yTopo) });
            c.BarArr.Formatos.AddRange(new[] { FormatoSegmento.Linha, FormatoSegmento.Linha });
            if (c.ArrPe <= 0) { c.BarArr.Vertices.RemoveAt(0); c.BarArr.Formatos.RemoveAt(0); }
            c.BarArr.PosicaoDescricao = (x0 + 1.8 * ht, yTopo - lv / 2);   // a cota fica do outro lado
            c.BarArr.RotacaoDescricao = Math.PI / 2;
            Texto(c, "ARRANQUE DO PILAR", x0, yTopo - lv - 4 * ht, 0.8 * ht, 0);
            x = x0 + Math.Max(c.ArrPe, 6 * ht) + 12 * ht;
            yMin = Math.Min(yMin, yTopo - lv - 6 * ht);
        }
        if (c.BarEstPilar != null)
        {
            double cp = c.V.Numero("ArrCob"), w = c.Bx - 2 * cp, hh = c.By - 2 * cp;
            Posicionar(c, c.BarEstPilar, x, yTopo - hh, w);
            x += w + 14 * ht;
        }
        c.XMax = Math.Max(c.XMax, x);
    }

    // ======================== forma ========================

    /// <summary>Forma à esquerda da armadura: planta cotada e elevação com o lastro (como o desenho de forma do exemplo).</summary>
    private static void Forma(Ctx c)
    {
        double ht = c.ht, Lx = c.Lx, Ly = c.Ly, h = c.h, R = c.D / 2;
        double ox = -(Lx + 30 * ht);
        double X(double x) => ox + x;
        Titulo(c, X(-6 * ht), Ly + 11 * ht, Lx + 12 * ht);

        // planta
        Retangulo(c, X(0), 0, X(Lx), Ly);
        foreach (var xc in c.ColX)
            foreach (var yc in c.LinY)
            {
                c.R.Circulos.Add(new CirculoTemplate { X = X(xc), Y = yc, Raio = R, Layer = "ARM-FORMA" });
                // quadrantes hachurados (superior direito e inferior esquerdo)
                foreach (var (a0, a1) in new[] { (0.0, Math.PI / 2), (Math.PI, 1.5 * Math.PI) })
                {
                    var hc = new HachuraTemplate { Padrao = "SOLID", Layer = "ARM-HACHURA-DETALHE" };
                    hc.Vertices.Add((X(xc), yc));
                    for (int i = 0; i <= 12; i++) { double a = a0 + (a1 - a0) * i / 12; hc.Vertices.Add((X(xc) + R * Math.Cos(a), yc + R * Math.Sin(a))); }
                    c.R.Hachuras.Add(hc);
                }
                Linha(c, "ARM-NIVEL", (X(xc - R - 0.6 * ht), yc), (X(xc + R + 0.6 * ht), yc));
                Linha(c, "ARM-NIVEL", (X(xc), yc - R - 0.6 * ht), (X(xc), yc + R + 0.6 * ht));
            }
        Retangulo(c, X(c.Px - c.Bx / 2), c.Py - c.By / 2, X(c.Px + c.Bx / 2), c.Py + c.By / 2, "ARM-PILAR-SECAO");

        // cotas em cadeia: face → estacas → eixo do pilar → face
        var px = new List<double> { 0, Lx, c.Px }.Concat(c.ColX).Distinct().OrderBy(v => v).ToList();
        var py = new List<double> { 0, Ly, c.Py }.Concat(c.LinY).Distinct().OrderBy(v => v).ToList();
        px = px.Where((v, i) => i == 0 || v - px[i - 1] > 0.01).ToList();
        py = py.Where((v, i) => i == 0 || v - py[i - 1] > 0.01).ToList();
        for (int i = 0; i + 1 < px.Count; i++) Cota(c, (X(px[i]), Ly), (X(px[i + 1]), Ly), (X((px[i] + px[i + 1]) / 2), Ly + 3 * ht), false);
        Cota(c, (X(0), Ly), (X(Lx), Ly), (X(Lx / 2), Ly + 6.5 * ht), false);
        for (int i = 0; i + 1 < py.Count; i++) Cota(c, (X(0), py[i]), (X(0), py[i + 1]), (X(-3 * ht), (py[i] + py[i + 1]) / 2), true);
        Cota(c, (X(0), 0), (X(0), Ly), (X(-6.5 * ht), Ly / 2), true);

        // elevação (alinhada com os cortes)
        double stub = Math.Max(20, 4 * ht);
        double zt = -12 * ht - stub, zb = zt - h;
        Retangulo(c, X(0), zb, X(Lx), zt);
        Linha(c, "ARM-PILAR-SECAO", (X(c.Px - c.Bx / 2), zt), (X(c.Px - c.Bx / 2), zt + stub));
        Linha(c, "ARM-PILAR-SECAO", (X(c.Px + c.Bx / 2), zt), (X(c.Px + c.Bx / 2), zt + stub));
        Linha(c, "ARM-FORMA", (X(c.Px - c.Bx / 2 - 6 * ht), zt + stub), (X(c.Px - 0.3 * ht), zt + stub), (X(c.Px - 0.1 * ht), zt + stub + 0.7 * ht),
            (X(c.Px + 0.1 * ht), zt + stub - 0.7 * ht), (X(c.Px + 0.3 * ht), zt + stub), (X(c.Px + c.Bx / 2 + 6 * ht), zt + stub));
        LastroEEstacas(c, X(0), X(Lx), zb, c.ColX.Select(X));
        Cota(c, (X(0), zb), (X(0), zt), (X(-3 * ht), (zb + zt) / 2), true);
        if (c.lastro > 0)
        {
            Cota(c, (X(0), zb - c.lastro), (X(0), zb), (X(-3 * ht), zb - c.lastro / 2), true);
            double ty = zb - c.lastro - 4 * ht;
            Linha(c, "ARM-FORMA", (X(-1 * ht), ty + 0.5 * ht), (X(1.5 * ht), zb - c.lastro / 2));
            Texto(c, "LASTRO DE", X(-1.5 * ht), ty, ht, 2);
            Texto(c, "CONC. MAGRO", X(-1.5 * ht), ty - 1.6 * ht, ht, 2);
        }
    }

    // ======================== verificações ========================

    private static void Verificar(Ctx c)
    {
        var v = c.V;
        double R = c.D / 2;
        foreach (var x in c.ColX)
            foreach (var y in c.LinY)
            {
                double d = Math.Min(Math.Min(x, c.Lx - x), Math.Min(y, c.Ly - y));
                if (d < R) { c.R.Avisos.Add($"Estaca em ({Num(x)}; {Num(y)}) sai do bloco: distância do eixo à face {Num(d)} cm < raio {Num(R)} cm."); break; }
            }
        if (c.Px - c.Bx / 2 < 0 || c.Px + c.Bx / 2 > c.Lx || c.Py - c.By / 2 < 0 || c.Py + c.By / 2 > c.Ly)
            c.R.Avisos.Add("O pilar sai do bloco.");
        if (c.AltEst < 10) c.R.Avisos.Add($"Bloco baixo demais para os cobrimentos: h − c_sup − c_inf = {Num(c.AltEst)} cm.");
        if (c.ColX.Count == 1 && c.LinY.Count == 1 && v.Inteiro("PrincQtd") > 0 && v.Numero("SecBitola") <= 0)
            c.R.Avisos.Add("Bloco de 1 estaca: não há faixa principal; com a secundária inferior em 0 ficam só os estribos.");
        if (c.ArrModo != ArrSem && c.PhiA > 0)
        {
            var tr = new RegrasTraspasse { Fck = v.Numero("Fck"), PercentualEmendado = 100 };
            double lb = Math.Ceiling(tr.Lb(c.PhiA, c.Aco));
            if (c.ArrInterno < lb)
                c.R.Avisos.Add($"Arranque: trecho reto dentro do bloco {Num(c.ArrInterno)} cm menor que lb = {Num(lb)} cm (ø{Num(c.PhiA)}, C{Num(v.Numero("Fck"))}); " +
                               $"com o pé (gancho) a NBR 6118 9.4.2.5 permite reduzir para 0,7·lb = {Num(Math.Ceiling(0.7 * lb))} cm — conferir.");
            if (v.Numero("ArrEspera") > 0)
            {
                double l0 = tr.Comprimento(c.PhiA, c.Aco);
                if (c.ArrEspera < l0) c.R.Avisos.Add($"Arranque: espera {Num(c.ArrEspera)} cm menor que o traspasse da NBR 6118 ({Num(l0)} cm).");
            }
        }
    }
}
