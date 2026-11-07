using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Calculo;
using DEB.Core.Configuracao;
using DEB.Core.Estribos;
using DEB.Core.Modelo;
using DEB.Core.Normas;
using DEB.Core.Tabelas;
using DEB.Core.Templates.PilarPreMoldado;

namespace DEB.Core.Templates.Blocos;

/// <summary>
/// Bloco de fundação sobre estacas (pedidos do usuário, 2026-11-06): retangular com estacas em grade (1, 2, 3 em linha,
/// 4, 6…) ou triangular de 3 estacas; ligação do pilar por cálice com colarinho, cálice embutido ou arranque.
/// Apresentação no padrão das folhas BPM/Marquezin (decisão do usuário: "BPM para todos") — ver
/// <c>src/DEB.Cad/docs/BLOCOS_REQUISITOS.md</c>:
/// <list type="bullet">
/// <item>FORMA: "BLOCO B15 - 24x - 3 ø 50" + pilares, PLANTA cotada (estacas com quadrantes hachurados, colarinho,
/// pilar) e CORTE A-A com regularização, concreto magro e os níveis por pilar;</item>
/// <item>ARMADURA: vistas superiores da armadura inferior principal, inferior secundária e superior, CÁLICE, CORTES A-A e
/// B-B, armadura lateral horizontal, barras dobradas embaixo de cada vista, relação do aço, informações p/ produção,
/// detalhes padrão (chaves, raio de curvatura, emendas, comprimento das barras), ancoragem das armaduras das estacas e
/// indicação de barra variável.</item>
/// </list>
/// Regras: bloco a "e" dos eixos das estacas (triangular: 6 faces a "e", 3 nos lados e 3 nos cantos); principal em faixas
/// sobre as estacas com pernas até o topo da base menos o cobrimento (decisão do usuário; valor digitado manda);
/// horizontais do cálice = colarinho − 2c (concentradas no topo + restante), grampos U com perna t − c − c_cálice,
/// verticais em grampo e suspensão (NBR 9062 7.7.3.7). Nada de dimensionamento: bitolas e quantidades são do engenheiro.
/// </summary>
public sealed class TemplateBlocoEstacas : ITemplateArmadura
{
    public const string IdUmaEstaca = "bloco_1_estaca", IdEstacas = "bloco_estacas";
    public const string Grade = "Retangular (grade nx × ny)", Triangulo = "Triangular (3 estacas)";
    public const string Colarinho = "Cálice com colarinho", Embutido = "Cálice embutido no bloco", Arranque = "Arranque (moldado in loco)";
    public const string Lisa = "Lisa", Rugosa = "Rugosa", Chaves = "Com chaves de cisalhamento";
    public const string PeDentro = "Para dentro", PeFora = "Para fora";

    private readonly bool _uma;
    public TemplateBlocoEstacas() : this(false) { }
    public TemplateBlocoEstacas(bool umaEstaca) { _uma = umaEstaca; Parametros = CriarParametros(umaEstaca); }

    public string Id => _uma ? IdUmaEstaca : IdEstacas;
    public string Nome => _uma ? "Bloco de 1 estaca" : "Bloco sobre estacas (grade ou triangular)";
    public CategoriaElemento Categoria => _uma ? CategoriaElemento.Bloco1Estaca : CategoriaElemento.Bloco2Estacas;
    public string Descricao => "Bloco retangular (estacas em grade) ou triangular (3 estacas), com cálice (colarinho ou embutido) ou " +
                               "arranque do pilar, no padrão das folhas BPM: forma, vistas das armaduras, cálice, cortes, produção e detalhes.";
    public IReadOnlyList<ParametroTemplate> Parametros { get; }

    private static ParametroTemplate P(string grupo, string nome, string rotulo, string padrao, string unidade = "",
        TipoParametro tipo = TipoParametro.Numero, double min = double.NegativeInfinity, double max = double.PositiveInfinity,
        string descricao = "", params string[] opcoes) => new ParametroTemplate
    {
        Grupo = grupo, Nome = nome, Rotulo = rotulo, Padrao = padrao, Unidade = unidade, Tipo = tipo, Minimo = min, Maximo = max,
        Descricao = descricao, Opcoes = opcoes.ToList(),
    };

    private const string G1 = "01. Identificação", G2 = "02. Estacas", G3 = "03. Bloco", G4 = "04. Pilar e ligação",
        G5 = "05. Cobrimentos e materiais", G6 = "06. Armadura inferior principal", G7 = "07. Armadura inferior secundária",
        G8 = "08. Armadura superior", G9 = "09. Armadura lateral e estribos", G10 = "10. Cálice", G11 = "11. Arranque",
        G12 = "12. Desenho";

    private static IReadOnlyList<ParametroTemplate> CriarParametros(bool uma) => new[]
    {
        P(G1, "Nome", "Nome do bloco", uma ? "B1" : "B28", tipo: TipoParametro.Texto, descricao: "O título fica \"BLOCO <nome>\"."),
        P(G1, "Pilares", "Pilares (lista)", uma ? "P1" : "P106, P118, P119, P120, P121 e P122", tipo: TipoParametro.Texto,
          descricao: "Separados por vírgula, barra ou \"e\". A quantidade de blocos sai da lista."),
        P(G1, "Quantidade", "Quantidade de blocos", "0", tipo: TipoParametro.Inteiro, min: 0, descricao: "0 = o número de pilares da lista."),
        new ParametroTemplate
        {
            Grupo = G1, Nome = "Niveis", Rotulo = "Níveis (topo do bloco por pilar)", Tipo = TipoParametro.Tabela, Padrao = "", NomeItem = "nível",
            Descricao = "Pilar e cota do TOPO DO BLOCO (m). O fundo do pilar e a cota de arrasamento saem da geometria.",
            Colunas =
            {
                new ColunaTabela { Nome = "Pilar", Rotulo = "Pilar", Tipo = TipoParametro.Texto, Largura = 90 },
                new ColunaTabela { Nome = "Topo", Rotulo = "Topo do bloco (m)", Tipo = TipoParametro.Numero, Largura = 120 },
            },
        },

        P(G2, "Arranjo", "Arranjo das estacas", Grade, tipo: TipoParametro.Opcao, opcoes: new[] { Grade, Triangulo }),
        P(G2, "NEstX", "Estacas em x (grade)", uma ? "1" : "2", tipo: TipoParametro.Inteiro, min: 1, max: 10),
        P(G2, "NEstY", "Estacas em y (grade)", "1", tipo: TipoParametro.Inteiro, min: 1, max: 10),
        P(G2, "EspX", "Distância entre eixos em x (triangular: lado)", uma ? "0" : "120", "cm", min: 0),
        P(G2, "EspY", "Distância entre eixos em y", "120", "cm", min: 0),
        P(G2, "DiamEstaca", "Diâmetro das estacas", uma ? "50" : "40", "cm", min: 10),
        P(G2, "DistFace", "Distância do eixo da estaca à face do bloco", "35", "cm", min: 5,
          descricao: "Exemplos: B28 35, B15/B31 40, B4 45. No triangular vale para as 6 faces."),
        P(G2, "EmbEstaca", "Embutimento das estacas no bloco", "5", "cm", min: 0),

        P(G3, "hBase", "Altura da base do bloco", uma ? "70" : "55", "cm", min: 20),
        P(G3, "Lastro", "Concreto magro", "5", "cm", min: 0),

        P(G4, "PilarBx", "Pilar: dimensão em x", "30", "cm", min: 10),
        P(G4, "PilarBy", "Pilar: dimensão em y", "20", "cm", min: 10),
        P(G4, "PilarEx", "Pilar: deslocamento em x", "0", "cm", descricao: "Em relação ao centro das estacas."),
        P(G4, "PilarEy", "Pilar: deslocamento em y", "0", "cm"),
        P(G4, "Ligacao", "Ligação do pilar", Colarinho, tipo: TipoParametro.Opcao, opcoes: new[] { Colarinho, Embutido, Arranque }),
        P(G4, "hColar", "Colarinho: altura acima da base", "40", "cm", min: 0),
        P(G4, "tParede", "Cálice: espessura das paredes", "21", "cm", min: 10, descricao: "Colarinho: parede de concreto (NBR 9062: ≥ 15). Embutido: faixa das armaduras em volta."),
        P(G4, "Folga", "Cálice: folga entre o pilar e a parede", "4", "cm", min: 0),
        P(G4, "Lemb", "Embutimento do pilar (topo do bloco → base do pilar)", "50", "cm", min: 0),
        P(G4, "Regularizacao", "Regularização de fundo do cálice", "5", "cm", min: 0),
        P(G4, "Interface", "Interface do cálice", Chaves, tipo: TipoParametro.Opcao, opcoes: new[] { Lisa, Rugosa, Chaves }),

        P(G5, "Cob", "Cobrimento (contato com o solo)", "4", "cm", min: 1, max: 10),
        P(G5, "CobCalice", "Cobrimento interno do cálice", "2", "cm", min: 1, max: 10),
        P(G5, "Fck", "fck", "30", "MPa", min: 20, max: 90),
        P(G5, "Aco", "Aço", "CA-50", tipo: TipoParametro.Opcao, opcoes: new[] { "CA-50", "CA-60" }),

        P(G6, "PrincBitola", "Bitola", "16", "mm", min: 0, max: 40, descricao: "0 = sem. Bloco de 1 estaca: não há faixa principal."),
        P(G6, "PrincQtd", "Barras por faixa (sobre cada linha de estacas)", "5", tipo: TipoParametro.Inteiro, min: 0),
        P(G6, "PrincEsp", "Espaçamento na faixa", "5", "cm", min: 2),
        P(G6, "PrincPerna", "Perna", "0", "cm", min: 0, descricao: "0 = até o topo da base menos o cobrimento (regra do usuário; ajustável depois na edição do bloco)."),

        P(G7, "SecBitola", "Bitola (malha nas duas direções)", "10", "mm", min: 0, max: 40, descricao: "0 = sem."),
        P(G7, "SecEsp", "Espaçamento", "15", "cm", min: 5),
        P(G7, "SecPerna", "Perna", "20", "cm", min: 0, descricao: "Valor padrão do template (não vem do cálculo)."),

        P(G8, "SupBitola", "Bitola (malha nas duas direções)", "8", "mm", min: 0, max: 40, descricao: "0 = sem. Interrompida no colarinho/cálice."),
        P(G8, "SupEsp", "Espaçamento", "12", "cm", min: 5),
        P(G8, "SupPerna", "Perna", "20", "cm", min: 0, descricao: "Valor padrão do template (não vem do cálculo)."),

        P(G9, "LatBitola", "Lateral horizontal (em volta): bitola", "8", "mm", min: 0, max: 25, descricao: "0 = sem."),
        P(G9, "LatEsp", "Lateral horizontal: espaçamento", "15", "cm", min: 5),
        P(G9, "EstVBitola", "Estribos verticais da base: bitola", "8", "mm", min: 0, max: 25, descricao: "0 = sem (bloco triangular: sem)."),
        P(G9, "EstVEsp", "Estribos verticais: espaçamento", "12", "cm", min: 5),

        P(G10, "CalTopoBitola", "Horizontais concentradas no topo: bitola", "12.5", "mm", min: 0, max: 32, descricao: "NBR 9062 7.7.4.3: pressões no topo (Lemb/3)."),
        P(G10, "CalTopoQtd", "Horizontais no topo: quantidade", "3", tipo: TipoParametro.Inteiro, min: 0),
        P(G10, "CalTopoEsp", "Horizontais no topo: espaçamento", "5.5", "cm", min: 2),
        P(G10, "CalBitola", "Horizontais restantes: bitola", "10", "mm", min: 0, max: 32),
        P(G10, "CalEsp", "Horizontais restantes: espaçamento", "12", "cm", min: 4),
        P(G10, "CalGrampos", "Grampos U nas paredes (com as horizontais)", "Sim", tipo: TipoParametro.SimNao),
        P(G10, "CalVertBitola", "Verticais (grampo na parede): bitola", "10", "mm", min: 0, max: 32),
        P(G10, "CalVertQtd", "Verticais: quantidade por parede", "2", tipo: TipoParametro.Inteiro, min: 0),
        P(G10, "SuspBitola", "Suspensão: bitola", "10", "mm", min: 0, max: 32, descricao: "0 = sem. NBR 9062 7.7.3.7."),
        P(G10, "SuspQtd", "Suspensão: quantidade", "4", tipo: TipoParametro.Inteiro, min: 0),
        P(G10, "SuspEsp", "Suspensão: espaçamento", "12", "cm", min: 4),
        P(G10, "FundoBitola", "Barras sob a base do pilar: bitola", "10", "mm", min: 0, max: 32, descricao: "0 = sem."),
        P(G10, "FundoQtd", "Barras sob a base do pilar: quantidade", "7", tipo: TipoParametro.Inteiro, min: 0),
        P(G10, "FundoComp", "Barras sob a base do pilar: comprimento", "100", "cm", min: 10),

        P(G11, "ArrBitola", "Bitola", "12.5", "mm", min: 5, max: 40),
        P(G11, "ArrNx", "Barras por face paralela a x (com os cantos)", "2", tipo: TipoParametro.Inteiro, min: 1, max: 20),
        P(G11, "ArrNy", "Barras por face paralela a y (com os cantos)", "2", tipo: TipoParametro.Inteiro, min: 1, max: 20),
        P(G11, "ArrCob", "Cobrimento do pilar", "3", "cm", min: 1, max: 10),
        P(G11, "ArrEspera", "Espera acima do bloco", "0", "cm", min: 0, descricao: "0 = traspasse da NBR 6118 (100 %), a cada 5 cm."),
        P(G11, "ArrPe", "Pé (dobra no fundo)", "20", "cm", min: 0),
        P(G11, "ArrPeSentido", "Sentido do pé", PeFora, tipo: TipoParametro.Opcao, opcoes: new[] { PeDentro, PeFora }),
        P(G11, "ArrEstBitola", "Estribos do pilar no bloco: bitola", "6.3", "mm", min: 0, max: 16),
        P(G11, "ArrEstQtd", "Estribos do pilar no bloco: quantidade", "3", tipo: TipoParametro.Inteiro, min: 0),

        P(G12, "Escala", "Escala (1:n)", "20", min: 1),
        P(G12, "Idioma", "Idioma", "PT", tipo: TipoParametro.Opcao, opcoes: new[] { "PT", "EN", "PT/EN" }),
        P(G12, "Forma", "Desenhar a forma", "Sim", tipo: TipoParametro.SimNao),
        P(G12, "Detalhes", "Detalhes padrão (chaves, ganchos, emendas, estacas)", "Sim", tipo: TipoParametro.SimNao),
    };

    // ======================== modelo ========================

    private sealed class Linha3
    {
        public double T, A, B;   // reta t (ao longo da normal); trecho [A, B] ao longo da direção
    }

    /// <summary>Família de barras retas (com pernas) numa direção da planta.</summary>
    private sealed class Familia
    {
        public string Nome = "", Vista = "";
        public double Ang;                   // direção (rad)
        public bool Superior, Var;
        public double Bitola, Esp, Perna, Z;
        public List<Linha3> Linhas = new List<Linha3>();
        public string LayerLugar = "ARM-BARRA-DETALHE";
        public BarraTemplate Barra;
        public (double X, double Y) U => (Math.Cos(Ang), Math.Sin(Ang));
        public (double X, double Y) N => (-Math.Sin(Ang), Math.Cos(Ang));
        public (double X, double Y) Ponto(double t, double s) => (t * N.X + s * U.X, t * N.Y + s * U.Y);
    }

    private sealed class Ctx
    {
        public ValoresTemplate V;
        public ConfiguracaoArmadura Cfg;
        public ResultadoTemplate R;
        public IdiomaDesenho Idioma;
        public string Aco = "CA-50", Titulo = "", ListaPilares = "";
        public int Blocos;
        public double esc, ht, c, cc, e, D, emb, hB, hC, hT, lastro, Lemb, reg, folga, t;
        public bool Tri, Calice, Colar;
        public List<(double X, double Y)> Contorno = new List<(double, double)>();
        public List<(double X, double Y)> Estacas = new List<(double, double)>();
        public double W, H, Px, Py, Bx, By;
        // cálice (planta): colarinho/abertura, centrados no pilar
        public double Cx0, Cy0, Cx1, Cy1, Ox0, Oy0, Ox1, Oy1;
        public List<Familia> Fam = new List<Familia>();
        public BarraTemplate Lateral, EstV, CalTopo, CalResto, GrTopoX, GrTopoY, GrRestoX, GrRestoY, CalVert, Susp, Fundo, Arr, ArrEst;
        public List<double> ZLateral = new List<double>(), ZCalTopo = new List<double>(), ZCalResto = new List<double>();
        public List<double> XEstV = new List<double>();
        public List<(double X, double Y)> ArrPontos = new List<(double, double)>();
        public List<double> ArrXs = new List<double>(), ArrYs = new List<double>();
        public double ArrInterno, ArrEspera, ArrPe;
        public bool PeDentro;
        public double XMax, YMin;
        public int Ordem;
        public Dictionary<BarraTemplate, int> Ordens = new Dictionary<BarraTemplate, int>();
        public bool TemVar;
        public double Lp => Math.Max(25, 1.2 * D);

        public string[] L(string pt, string en) => Idioma switch
        {
            IdiomaDesenho.PT => new[] { pt },
            IdiomaDesenho.EN => new[] { en },
            _ => new[] { pt, "(" + en + ")" },
        };
    }

    private static string Num(double x) => CalculoComprimento.NumeroCompacto(x);
    private static string NumV(double x) => x.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');

    public static List<string> LerPilares(string s) =>
        (s ?? "").Replace(" e ", ",").Replace(" E ", ",").Split(new[] { ',', ';', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim()).Where(p => p.Length > 0).ToList();

    public static string TextoPilares(List<string> ps) =>
        ps.Count == 0 ? "" : ps.Count == 1 ? ps[0] : string.Join(", ", ps.Take(ps.Count - 1)) + " e " + ps[ps.Count - 1];

    public ResultadoTemplate Gerar(ValoresTemplate v, ConfiguracaoArmadura cfg)
    {
        var c = new Ctx { V = v, Cfg = cfg, R = new ResultadoTemplate() };
        Ler(c);
        Armaduras(c);
        double xArm = 0;
        if (v.SimNao("Forma")) xArm = Forma(c);
        Armadura(c, xArm);
        Verificar(c);
        var ord = c.R.Barras.OrderBy(b => c.Ordens.TryGetValue(b, out int k) ? k : 999).ToList();
        c.R.Barras.Clear(); c.R.Barras.AddRange(ord);
        return c.R;
    }

    // ======================== leitura e geometria ========================

    private static void Ler(Ctx c)
    {
        var v = c.V;
        c.esc = Math.Max(1, v.Numero("Escala"));
        c.ht = (c.Cfg.AlturaTextoMm > 0 ? c.Cfg.AlturaTextoMm : 2) / 10.0 * c.esc;
        c.Idioma = v["Idioma"] == "EN" ? IdiomaDesenho.EN : v["Idioma"] == "PT/EN" ? IdiomaDesenho.PTEN : IdiomaDesenho.PT;
        c.Aco = string.IsNullOrWhiteSpace(v["Aco"]) ? "CA-50" : v["Aco"];
        c.c = v.Numero("Cob"); c.cc = v.Numero("CobCalice");
        c.e = v.Numero("DistFace"); c.D = v.Numero("DiamEstaca"); c.emb = Math.Max(0, v.Numero("EmbEstaca"));
        c.hB = v.Numero("hBase"); c.lastro = Math.Max(0, v.Numero("Lastro"));
        c.Bx = v.Numero("PilarBx"); c.By = v.Numero("PilarBy");
        string lig = string.IsNullOrWhiteSpace(v["Ligacao"]) ? Colarinho : v["Ligacao"];
        c.Calice = lig != Arranque; c.Colar = lig == Colarinho;
        c.hC = c.Colar ? Math.Max(0, v.Numero("hColar")) : 0;
        c.hT = c.hB + c.hC;
        c.Lemb = c.Calice ? v.Numero("Lemb") : 0; c.reg = c.Calice ? Math.Max(0, v.Numero("Regularizacao")) : 0;
        c.folga = v.Numero("Folga"); c.t = v.Numero("tParede");
        c.Tri = v["Arranjo"] == Triangulo;

        // estacas e contorno (origem no canto inferior esquerdo da caixa envolvente)
        double e = c.e;
        if (c.Tri)
        {
            double s = v.Numero("EspX"), h3 = s * Math.Sqrt(3) / 2;
            var est = new List<(double X, double Y)> { (0, 0), (h3, -s / 2), (h3, s / 2) };
            var g = (X: est.Average(p => p.X), Y: est.Average(p => p.Y));
            var semi = new List<((double X, double Y) n, double d)>();
            for (int i = 0; i < 3; i++)
            {
                var a = est[i]; var b = est[(i + 1) % 3]; var o = est[(i + 2) % 3];
                var n = Unit((b.Y - a.Y, -(b.X - a.X)));
                if (Dot(n, (o.X - a.X, o.Y - a.Y)) > 0) n = (-n.X, -n.Y);
                semi.Add((n, Dot(n, a) + e));
                var dv = Unit((a.X - g.X, a.Y - g.Y));
                semi.Add((dv, Dot(dv, a) + e));
            }
            var poly = new List<(double X, double Y)> { (-1e4, -1e4), (1e4, -1e4), (1e4, 1e4), (-1e4, 1e4) };
            foreach (var (n, d) in semi) poly = Recortar(poly, n, d);
            double mx = poly.Min(p => p.X), my = poly.Min(p => p.Y);
            c.Contorno = poly.Select(p => (p.X - mx, p.Y - my)).ToList();
            c.Estacas = est.Select(p => (p.X - mx, p.Y - my)).ToList();
        }
        else
        {
            int nx = Math.Max(1, v.Inteiro("NEstX")), ny = Math.Max(1, v.Inteiro("NEstY"));
            double ex = v.Numero("EspX"), ey = v.Numero("EspY");
            double W = (nx - 1) * ex + 2 * e, H = (ny - 1) * ey + 2 * e;
            c.Contorno = new List<(double, double)> { (0, 0), (W, 0), (W, H), (0, H) };
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) c.Estacas.Add((e + i * ex, e + j * ey));
        }
        c.W = c.Contorno.Max(p => p.X); c.H = c.Contorno.Max(p => p.Y);
        c.Px = c.Estacas.Average(p => p.X) + v.Numero("PilarEx");
        c.Py = c.Estacas.Average(p => p.Y) + v.Numero("PilarEy");
        double ax = c.Bx / 2 + c.folga, ay = c.By / 2 + c.folga;
        c.Ox0 = c.Px - ax; c.Ox1 = c.Px + ax; c.Oy0 = c.Py - ay; c.Oy1 = c.Py + ay;
        c.Cx0 = c.Ox0 - c.t; c.Cx1 = c.Ox1 + c.t; c.Cy0 = c.Oy0 - c.t; c.Cy1 = c.Oy1 + c.t;
        if (c.Colar)
        {
            // colarinho limitado ao bloco (B28: largura do colarinho = largura do bloco)
            c.Cx0 = Math.Max(c.Cx0, 0); c.Cy0 = Math.Max(c.Cy0, 0); c.Cx1 = Math.Min(c.Cx1, c.W); c.Cy1 = Math.Min(c.Cy1, c.H);
        }

        string nome = string.IsNullOrWhiteSpace(v["Nome"]) ? "B1" : v["Nome"].Trim();
        c.Titulo = nome.StartsWith("BLOCO", StringComparison.OrdinalIgnoreCase) ? nome.ToUpperInvariant() : "BLOCO " + nome.ToUpperInvariant();
        var ps = LerPilares(v["Pilares"]);
        c.Blocos = v.Inteiro("Quantidade") > 0 ? v.Inteiro("Quantidade") : Math.Max(1, ps.Count);
        c.ListaPilares = TextoPilares(ps);
        c.R.Escala = c.esc;
        c.R.Detalhamento = c.Titulo;
        c.R.DadosDetalhamento = new DadosDetalhamento
        {
            Grupo = "Fundações", Repeticoes = c.Blocos, Classe = "C" + Num(v.Numero("Fck")),
            Volume = Volume(c), AreaForma = AreaForma(c), Comentarios = "Gerado pelo template de bloco sobre estacas.",
        };
    }

    private static (double X, double Y) Unit((double X, double Y) a) { double n = Math.Sqrt(a.X * a.X + a.Y * a.Y); return (a.X / n, a.Y / n); }
    private static double Dot((double X, double Y) a, (double X, double Y) b) => a.X * b.X + a.Y * b.Y;

    /// <summary>Sutherland–Hodgman: mantém n·p ≤ d.</summary>
    private static List<(double X, double Y)> Recortar(List<(double X, double Y)> poly, (double X, double Y) n, double d)
    {
        var r = new List<(double X, double Y)>();
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count];
            double fa = Dot(n, a) - d, fb = Dot(n, b) - d;
            if (fa <= 0) r.Add(a);
            if (fa * fb < 0) { double t = fa / (fa - fb); r.Add((a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y))); }
        }
        return r;
    }

    /// <summary>Trecho [a, b] da reta p(s) = o + s·u dentro do contorno (convexo, anti-horário); null = fora.</summary>
    private static (double A, double B)? Corda(List<(double X, double Y)> poly, (double X, double Y) o, (double X, double Y) u)
    {
        double a = double.NegativeInfinity, b = double.PositiveInfinity;
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i]; var q = poly[(i + 1) % poly.Count];
            var ed = (X: q.X - p.X, Y: q.Y - p.Y);
            double f0 = ed.X * (o.Y - p.Y) - ed.Y * (o.X - p.X);   // cross(ed, o − p) ≥ 0 dentro
            double f1 = ed.X * u.Y - ed.Y * u.X;
            if (Math.Abs(f1) < 1e-12) { if (f0 < 0) return null; continue; }
            double s = -f0 / f1;
            if (f1 > 0) a = Math.Max(a, s); else b = Math.Min(b, s);
        }
        return b > a ? (a, b) : ((double, double)?)null;
    }

    private static double Area(List<(double X, double Y)> p)
    {
        double s = 0;
        for (int i = 0; i < p.Count; i++) { var a = p[i]; var b = p[(i + 1) % p.Count]; s += a.X * b.Y - b.X * a.Y; }
        return Math.Abs(s) / 2;
    }

    private static double Perimetro(List<(double X, double Y)> p)
    {
        double s = 0;
        for (int i = 0; i < p.Count; i++) { var a = p[i]; var b = p[(i + 1) % p.Count]; s += Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)); }
        return s;
    }

    private static double Volume(Ctx c)
    {
        double v = Area(c.Contorno) * c.hB + (c.Cx1 - c.Cx0) * (c.Cy1 - c.Cy0) * c.hC;
        if (c.Calice) v -= (c.Ox1 - c.Ox0) * (c.Oy1 - c.Oy0) * (c.Lemb + c.reg);
        return Math.Round(v / 1e6, 3);
    }

    private static double AreaForma(Ctx c) =>
        Math.Round((Perimetro(c.Contorno) * c.hB + 2 * ((c.Cx1 - c.Cx0) + (c.Cy1 - c.Cy0)) * c.hC
                    + (c.Calice ? 2 * ((c.Ox1 - c.Ox0) + (c.Oy1 - c.Oy0)) * (c.Lemb + c.reg) : 0)) / 1e4, 2);

    // ======================== armaduras ========================

    private static int Qtd(Ctx c, double vao, double esp) =>
        CalculoFaixa.Quantidade(vao, esp, c.Cfg.EspacamentoInicialFaixa, c.Cfg.ModoQuantidadeFaixa);

    private static List<double> Posicoes(Ctx c, double vao, int n, double esp) =>
        CalculoFaixa.Posicoes(vao, n, esp, c.Cfg.EspacamentoInicialFaixa);

    private static void Registrar(Ctx c, BarraTemplate b) { c.R.Barras.Add(b); c.Ordens[b] = c.Ordem++; }

    private static void Armaduras(Ctx c)
    {
        var v = c.V;
        double cb = c.c, phiLat = v.Numero("LatBitola");
        double zInf = Math.Max(cb, c.emb);                 // face inferior das barras: acima do topo das estacas
        // ---- principal: faixas sobre as estacas ----
        double phiP = v.Numero("PrincBitola"), espP = v.Numero("PrincEsp"); int qP = v.Inteiro("PrincQtd");
        double pernaP = v.Numero("PrincPerna") > 0 ? v.Numero("PrincPerna") : Math.Floor(c.hB - cb - zInf);
        var faixas = new List<(double Ang, double T)>();   // reta (direção, offset) que liga estacas
        if (c.Tri)
            for (int i = 0; i < 3; i++)
            {
                var a = c.Estacas[i]; var b = c.Estacas[(i + 1) % 3];
                double ang = Math.Atan2(b.Y - a.Y, b.X - a.X);
                if (ang < -1e-9) ang += Math.PI;
                if (ang >= Math.PI - 1e-9) ang -= Math.PI;
                var n = (-Math.Sin(ang), Math.Cos(ang));
                faixas.Add((ang, Dot(n, a)));
            }
        else
        {
            if (c.Estacas.Select(p => Math.Round(p.X, 3)).Distinct().Count() >= 2)
                foreach (var y in c.Estacas.Select(p => Math.Round(p.Y, 6)).Distinct()) faixas.Add((0, y));
            if (c.Estacas.Select(p => Math.Round(p.Y, 3)).Distinct().Count() >= 2)
                foreach (var x in c.Estacas.Select(p => Math.Round(p.X, 6)).Distinct()) faixas.Add((Math.PI / 2, -x));
        }
        if (phiP > 0 && qP > 0)
            foreach (var grupo in faixas.GroupBy(f => Math.Round(f.Ang, 6)))
            {
                var f = new Familia
                {
                    Nome = "principal" + Sufixo(grupo.Key), Vista = "P", Ang = grupo.Key, Bitola = phiP, Esp = espP, Perna = pernaP,
                    Z = zInf + phiP / 20 + (Math.Abs(Math.Sin(grupo.Key)) > 0.99 && !c.Tri ? phiP / 10 : 0),
                };
                // faixa centrada na linha de estacas; se passar da face, desloca para dentro (como o B31: 7 c/14 com face a 40)
                double lo = c.Contorno.Min(p => Dot(f.N, p)) + c.c + phiLat / 10 + phiP / 20;
                double hi = c.Contorno.Max(p => Dot(f.N, p)) - c.c - phiLat / 10 - phiP / 20;
                foreach (var (_, T) in grupo)
                {
                    double t0 = T - (qP - 1) / 2.0 * espP, t1 = T + (qP - 1) / 2.0 * espP, d = 0;
                    if (t0 < lo) d = lo - t0; else if (t1 > hi) d = hi - t1;
                    double passo = qP > 1 && t1 - t0 > hi - lo ? (hi - lo) / (qP - 1) : espP;
                    for (int i = 0; i < qP; i++) AdicionarLinha(c, f, passo == espP ? t0 + d + i * espP : lo + i * passo);
                }
                if (f.Linhas.Count > 0) c.Fam.Add(f);
            }

        // ---- secundária inferior e superior: malhas nas duas direções (x e y) ----
        double phiS = v.Numero("SecBitola"), espS = v.Numero("SecEsp");
        double phiU = v.Numero("SupBitola"), espU = v.Numero("SupEsp");
        foreach (double ang in new[] { 0.0, Math.PI / 2 })
        {
            if (phiS > 0)
            {
                var f = new Familia
                {
                    Nome = "secundária" + Sufixo(ang), Vista = "S", Ang = ang, Bitola = phiS, Esp = espS, Perna = v.Numero("SecPerna"),
                    Z = zInf + phiP / 10 * 2 + phiS / 20 + (ang > 0 ? phiS / 10 : 0), LayerLugar = "ARM-BARRA-2",
                };
                Malha(c, f, false);
                if (f.Linhas.Count > 0) c.Fam.Add(f);
            }
            if (phiU > 0)
            {
                var f = new Familia
                {
                    Nome = "superior" + Sufixo(ang), Vista = "U", Ang = ang, Superior = true, Bitola = phiU, Esp = espU, Perna = v.Numero("SupPerna"),
                    Z = c.hB - cb - phiLat / 10 - phiU / 20 - (ang > 0 ? phiU / 10 : 0),
                };
                Malha(c, f, true);
                if (f.Linhas.Count > 0) c.Fam.Add(f);
            }
        }
        foreach (var f in c.Fam) CriarBarra(c, f);

        // ---- lateral horizontal (polígono do contorno − c), estribos verticais da base ----
        if (phiLat > 0)
        {
            double alt = c.hB - 2 * cb;
            int n = Qtd(c, alt, v.Numero("LatEsp"));
            c.ZLateral = Posicoes(c, alt, n, v.Numero("LatEsp")).Select(z => z + cb).ToList();
            var pol = Encolher(c.Contorno, cb);
            c.Lateral = EstriboPoligono(c, "lateral horizontal", pol, phiLat, n, v.Numero("LatEsp"));
            Registrar(c, c.Lateral);
        }
        double phiV = v.Numero("EstVBitola");
        if (phiV > 0 && !c.Tri)
        {
            double vao = c.W - 2 * cb, espV = v.Numero("EstVEsp");
            int n = Qtd(c, vao, espV);
            c.XEstV = Posicoes(c, vao, n, espV).Select(x => x + cb).Where(x => !c.Calice || x < c.Cx0 || x > c.Cx1).ToList();
            if (c.XEstV.Count > 0)
            {
                c.EstV = EstriboRet(c, "estribo vertical", c.H - 2 * cb, c.hB - 2 * cb, phiV, c.XEstV.Count, espV);
                Registrar(c, c.EstV);
            }
        }

        // ---- cálice ou arranque ----
        if (c.Calice) ArmadurasCalice(c);
        else ArmadurasArranque(c);
    }

    private static string Sufixo(double ang) => Math.Abs(ang) < 1e-6 ? " x" : Math.Abs(ang - Math.PI / 2) < 1e-6 ? " y" : $" {Math.Round(ang * 180 / Math.PI):0}°";

    /// <summary>Uma barra da família na reta t (corda do contorno − cobrimento).</summary>
    private static void AdicionarLinha(Ctx c, Familia f, double t)
    {
        var o = (X: t * f.N.X, Y: t * f.N.Y);
        var cd = Corda(c.Contorno, o, f.U);
        if (cd == null) return;
        if (Corda(Encolher(c.Contorno, c.c + 0.5), o, f.U) == null) return;   // a reta fica fora do cobrimento
        double lat = c.V.Numero("LatBitola") / 10;
        double a = cd.Value.A + c.c + lat, b = cd.Value.B - c.c - lat;
        if (b - a > 5) f.Linhas.Add(new Linha3 { T = t, A = a, B = b });
    }

    /// <summary>Malha: retas a cada espaçamento; a superior é interrompida no colarinho/cálice (dois trechos).</summary>
    private static void Malha(Ctx c, Familia f, bool superior)
    {
        double t0 = c.Contorno.Min(p => Dot(f.N, p)), t1 = c.Contorno.Max(p => Dot(f.N, p));
        double vao = t1 - t0 - 2 * c.c;
        int n = Qtd(c, vao, f.Esp);
        bool emX = Math.Abs(f.Ang) < 1e-6;
        foreach (var p in Posicoes(c, vao, n, f.Esp))
        {
            double t = t0 + c.c + p;
            int antes = f.Linhas.Count;
            AdicionarLinha(c, f, t);
            if (!superior || !c.Calice || f.Linhas.Count == antes) continue;
            // interrompe no colarinho (ou na abertura do cálice embutido), com cobrimento
            var ln = f.Linhas[f.Linhas.Count - 1];
            double coord = emX ? t : -t;   // em x: t = y; em y: N = (−1, 0) → t = −x
            double lo = emX ? (c.Colar ? c.Cy0 : c.Oy0) : (c.Colar ? c.Cx0 : c.Ox0), hi = emX ? (c.Colar ? c.Cy1 : c.Oy1) : (c.Colar ? c.Cx1 : c.Ox1);
            if (coord <= lo - c.c || coord >= hi + c.c) continue;
            double s0 = emX ? (c.Colar ? c.Cx0 : c.Ox0) : (c.Colar ? c.Cy0 : c.Oy0), s1 = emX ? (c.Colar ? c.Cx1 : c.Ox1) : (c.Colar ? c.Cy1 : c.Oy1);
            f.Linhas.RemoveAt(f.Linhas.Count - 1);
            if (s0 - c.c - ln.A > 10) f.Linhas.Add(new Linha3 { T = t, A = ln.A, B = s0 - c.c });
            if (ln.B - (s1 + c.c) > 10) f.Linhas.Add(new Linha3 { T = t, A = s1 + c.c, B = ln.B });
        }
        f.Var = f.Linhas.Select(l => Math.Round(l.B - l.A)).Distinct().Count() > 1;
    }

    /// <summary>Barra representativa da família (a forma dobrada é desenhada depois); VAR quando os comprimentos variam.</summary>
    private static void CriarBarra(Ctx c, Familia f)
    {
        var comps = f.Linhas.Select(l => Math.Round(l.B - l.A)).ToList();
        var b = new BarraTemplate
        {
            Nome = f.Nome, Bitola = f.Bitola, Aco = c.Aco, Quantidade = Math.Max(1, f.Linhas.Count), Espacamento = f.Esp, Escala = c.esc,
            Layer = f.LayerLugar == "ARM-BARRA-2" ? "ARM-BARRA-2" : "",
        };
        if (f.Var)
        {
            b.ComprimentosVar = comps.Select(x => x + 2 * f.Perna).ToList();
            c.TemVar = true;
        }
        f.Barra = b;
        Registrar(c, b);
    }

    /// <summary>Contorno convexo recuado de d (offset para dentro).</summary>
    private static List<(double X, double Y)> Encolher(List<(double X, double Y)> p, double d)
    {
        var poly = new List<(double X, double Y)> { (-1e4, -1e4), (1e4, -1e4), (1e4, 1e4), (-1e4, 1e4) };
        for (int i = 0; i < p.Count; i++)
        {
            var a = p[i]; var b = p[(i + 1) % p.Count];
            var n = Unit((b.Y - a.Y, -(b.X - a.X)));   // anti-horário: normal externa = direita da aresta
            poly = Recortar(poly, n, Dot(n, a) - d);
        }
        return poly;
    }

    private static BarraTemplate EstriboPoligono(Ctx c, string nome, List<(double X, double Y)> pol, double phi, int qtd, double esp)
    {
        double mx = pol.Min(p => p.X), my = pol.Min(p => p.Y);
        var p0 = pol.Select(p => (p.X - mx, p.Y - my)).ToList();
        int canto = GeradorEstribo.Canto(p0, PosicaoGanchos.SuperiorEsquerdo);
        double lg = c.Cfg.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho90, phi, c.Aco, estribo: true);
        double passagem = c.Cfg.Dobramento.DiametroPino(phi, c.Aco, estribo: true) + 2 * phi / 10;
        var fo = GeradorEstribo.FechadoPoligono(p0, canto, FormatoSegmento.Gancho90, lg, passagem);
        var b = new BarraTemplate { Nome = nome, Bitola = phi, Aco = c.Aco, Estribo = true, Escala = c.esc, Quantidade = Math.Max(1, qtd), Espacamento = esp };
        b.Vertices.AddRange(fo.Vertices.Select(p => (p.X, p.Y)));
        b.Formatos.AddRange(fo.Formatos);
        foreach (var s in fo.Sobrepostos) b.Sobrepostos.Add(s);
        foreach (var s in fo.Cotados) b.Cotados.Add(s);
        return b;
    }

    private static BarraTemplate EstriboRet(Ctx c, string nome, double w, double h, double phi, int qtd, double esp) =>
        EstriboPoligono(c, nome, new List<(double X, double Y)> { (0, 0), (w, 0), (w, h), (0, h) }, phi, qtd, esp);

    private static BarraTemplate Aberta(Ctx c, string nome, double phi, int qtd, double esp, params (double X, double Y)[] pts)
    {
        var b = new BarraTemplate { Nome = nome, Bitola = phi, Aco = c.Aco, Quantidade = Math.Max(1, qtd), Espacamento = esp, Escala = c.esc };
        b.Vertices.AddRange(pts);
        for (int i = 0; i + 1 < pts.Length; i++) b.Formatos.Add(FormatoSegmento.Linha);
        return b;
    }

    private static void ArmadurasCalice(Ctx c)
    {
        var v = c.V;
        double cb = c.c;
        // horizontais fechadas em volta do cálice: do topo para baixo, concentradas + restantes até a base do pilar
        double w = (c.Colar ? c.Cx1 - c.Cx0 : c.Ox1 - c.Ox0 + 2 * c.t) - 2 * cb, h = (c.Colar ? c.Cy1 - c.Cy0 : c.Oy1 - c.Oy0 + 2 * c.t) - 2 * cb;
        double phiT = v.Numero("CalTopoBitola"), espT = v.Numero("CalTopoEsp"), phiR = v.Numero("CalBitola"), espR = v.Numero("CalEsp");
        int nT = phiT > 0 ? v.Inteiro("CalTopoQtd") : 0;
        double zTopo = c.hT - cb - Math.Max(phiT, phiR) / 20;
        for (int i = 0; i < nT; i++) c.ZCalTopo.Add(zTopo - i * espT);
        double zIni = nT > 0 ? c.ZCalTopo.Last() - espR : zTopo, zFim = c.hT - c.Lemb;
        if (phiR > 0 && zIni > zFim) { int n = Qtd(c, zIni - zFim, espR); c.ZCalResto = Posicoes(c, zIni - zFim, n, espR).Select(z => zIni - z).ToList(); }
        bool gr = v.SimNao("CalGrampos");
        double pernaG = Math.Round(c.t - cb - c.cc);
        if (nT > 0)
        {
            c.CalTopo = EstriboRet(c, "cálice: horizontais do topo", w, h, phiT, nT, espT); Registrar(c, c.CalTopo);
            if (gr)
            {
                c.GrTopoX = Aberta(c, "cálice: grampos do topo (x)", phiT, nT, espT, (0, pernaG), (0, 0), (w, 0), (w, pernaG)); c.GrTopoX.Repeticoes = 2; Registrar(c, c.GrTopoX);
                c.GrTopoY = Aberta(c, "cálice: grampos do topo (y)", phiT, nT, espT, (0, pernaG), (0, 0), (h, 0), (h, pernaG)); c.GrTopoY.Repeticoes = 2; Registrar(c, c.GrTopoY);
            }
        }
        if (c.ZCalResto.Count > 0)
        {
            c.CalResto = EstriboRet(c, "cálice: horizontais", w, h, phiR, c.ZCalResto.Count, espR); Registrar(c, c.CalResto);
            if (gr)
            {
                c.GrRestoX = Aberta(c, "cálice: grampos (x)", phiR, c.ZCalResto.Count, espR, (0, pernaG), (0, 0), (w, 0), (w, pernaG)); c.GrRestoX.Repeticoes = 2; Registrar(c, c.GrRestoX);
                c.GrRestoY = Aberta(c, "cálice: grampos (y)", phiR, c.ZCalResto.Count, espR, (0, pernaG), (0, 0), (h, 0), (h, pernaG)); c.GrRestoY.Repeticoes = 2; Registrar(c, c.GrRestoY);
            }
        }
        // verticais em grampo nas paredes: da face de cima até acima da principal
        double zPrinc = c.Fam.Where(f => !f.Superior).Select(f => f.Z + f.Bitola / 20).DefaultIfEmpty(c.c).Max();
        double phiV = v.Numero("CalVertBitola"); int nV = v.Inteiro("CalVertQtd");
        double larg = Math.Max(4, Math.Round(c.t - cb - c.cc - 2 * Math.Max(phiT, phiR) / 10));
        double alt = Math.Round(c.hT - cb - zPrinc);
        if (phiV > 0 && nV > 0)
        {
            double g = Math.Round(c.Cfg.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho90, phiV, c.Aco, false));
            c.CalVert = Aberta(c, "cálice: verticais", phiV, 4 * nV, 0, (-g, alt), (0, alt), (0, 0), (larg, 0), (larg, alt), (larg + g, alt));
            Registrar(c, c.CalVert);
        }
        // suspensão (NBR 9062 7.7.3.7): desce numa parede, passa sob o pilar e sobe na oposta
        double phiS = v.Numero("SuspBitola"); int nS = v.Inteiro("SuspQtd");
        if (phiS > 0 && nS > 0)
        {
            double fundo = Math.Round(c.Ox1 - c.Ox0 + 2 * cb), hi = alt, ho = hi - 6;
            c.Susp = Aberta(c, "suspensão", phiS, nS, v.Numero("SuspEsp"),
                (-larg, hi - ho), (-larg, hi), (0, hi), (0, 0), (fundo, 0), (fundo, hi), (fundo + larg, hi), (fundo + larg, hi - ho));
            Registrar(c, c.Susp);
        }
        double phiF = v.Numero("FundoBitola"); int nF = v.Inteiro("FundoQtd");
        if (phiF > 0 && nF > 0) { c.Fundo = Aberta(c, "sob a base do pilar", phiF, nF, 0, (0, 0), (v.Numero("FundoComp"), 0)); Registrar(c, c.Fundo); }
    }

    private static void ArmadurasArranque(Ctx c)
    {
        var v = c.V;
        double phiA = v.Numero("ArrBitola"), phiE = v.Numero("ArrEstBitola");
        double d = v.Numero("ArrCob") + phiE / 10 + phiA / 20;
        int ax = Math.Max(1, v.Inteiro("ArrNx")), ay = Math.Max(1, v.Inteiro("ArrNy"));
        for (int i = 0; i < ax; i++) c.ArrXs.Add(ax == 1 ? c.Px : c.Px - c.Bx / 2 + d + i * (c.Bx - 2 * d) / (ax - 1));
        for (int j = 0; j < ay; j++) c.ArrYs.Add(ay == 1 ? c.Py : c.Py - c.By / 2 + d + j * (c.By - 2 * d) / (ay - 1));
        for (int i = 0; i < ax; i++) for (int j = 0; j < ay; j++)
            if (i == 0 || j == 0 || i == ax - 1 || j == ay - 1) c.ArrPontos.Add((c.ArrXs[i], c.ArrYs[j]));
        double fundo = c.Fam.Where(f => !f.Superior).Select(f => f.Z + f.Bitola / 20).DefaultIfEmpty(c.c).Max();
        c.ArrInterno = Math.Round(c.hT - fundo);
        var tr = new RegrasTraspasse { Fck = v.Numero("Fck"), PercentualEmendado = 100 };
        c.ArrEspera = v.Numero("ArrEspera") > 0 ? v.Numero("ArrEspera") : Math.Ceiling(tr.Comprimento(phiA, c.Aco) / 5) * 5;
        c.ArrPe = v.Numero("ArrPe"); c.PeDentro = v["ArrPeSentido"] == PeDentro;
        var pts = new List<(double X, double Y)> { (c.ArrPe, 0), (0, 0), (0, c.ArrInterno + c.ArrEspera) };
        if (c.ArrPe <= 0) pts.RemoveAt(0);
        c.Arr = Aberta(c, "arranque", phiA, c.ArrPontos.Count, 0, pts.ToArray());
        Registrar(c, c.Arr);
        int ne = v.Inteiro("ArrEstQtd");
        if (phiE > 0 && ne > 0)
        {
            double cp = v.Numero("ArrCob");
            c.ArrEst = EstriboRet(c, "estribo do pilar no bloco", c.Bx - 2 * cp, c.By - 2 * cp, phiE, ne, 0);
            Registrar(c, c.ArrEst);
        }
    }

    // ======================== auxiliares de desenho ========================

    private static void Texto(Ctx c, string s, double x, double y, double h, int al = 0, string layer = "ARM-TEXTO-FORMA", double ang = 0) =>
        c.R.Textos.Add(new TextoTemplate { Texto = s, X = x, Y = y, Altura = h, Alinhamento = al, Layer = layer, Angulo = ang });

    private static double Linhas(Ctx c, string pt, string en, double x, double y, double h, int al = 0)
    {
        foreach (var l in c.L(pt, en)) { Texto(c, l, x, y, h, al); y -= 1.5 * h; }
        return y;
    }

    private static void Polilinha(Ctx c, string layer, bool fechada, IEnumerable<(double X, double Y)> pts) =>
        c.R.Contornos.Add(new ContornoTemplate { Layer = layer, Fechado = fechada, Vertices = pts.ToList() });

    private static void Linha(Ctx c, string layer, params (double X, double Y)[] pts) => Polilinha(c, layer, false, pts);

    private static void Ret(Ctx c, double x0, double y0, double x1, double y1, string layer = "ARM-FORMA") =>
        Polilinha(c, layer, true, new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) });

    private static void Ponto(Ctx c, double x, double y, double phi) =>
        c.R.Circulos.Add(new CirculoTemplate { X = x, Y = y, Raio = Math.Max(phi / 20, 0.3), Layer = "ARM-BARRA-SECAO" });

    private static void Cota(Ctx c, (double, double) p1, (double, double) p2, (double, double) pos, bool vertical, string texto = "") =>
        c.R.Cotas.Add(new CotaTemplate { P1 = p1, P2 = p2, Posicao = pos, Angulo = vertical ? Math.PI / 2 : 0, Escala = c.esc, Texto = texto });

    private static void Indicacao(Ctx c, BarraTemplate b, (double, double) texto, IEnumerable<(double, double)> refs)
    {
        if (b == null) return;
        var f = new FaixaTemplate { Barra = b.Nome, Tipo = TipoFaixa.Indicacao, Escala = c.esc, Posicao = texto };
        f.Referencias.AddRange(refs);
        if (f.Referencias.Count > 0) c.R.Faixas.Add(f);
    }

    /// <summary>Título de vista: nome e "ESC 1:n" embaixo; devolve o y abaixo.</summary>
    private static double TituloVista(Ctx c, string pt, string en, double x, double y)
    {
        y = Linhas(c, pt, en, x, y, 1.1 * c.ht);
        Texto(c, "ESC 1:" + Num(c.esc), x, y + 0.3 * c.ht, 0.7 * c.ht);
        return y - 1.2 * c.ht;
    }

    /// <summary>Título do bloco: "BLOCO B15 - 24x - 3 ø 50" e a lista dos pilares quebrada na largura.</summary>
    private static double TituloBloco(Ctx c, double x, double yTopo, double largura, bool comEstacas)
    {
        string t = c.Titulo + (c.Blocos > 1 ? $" - {c.Blocos}x" : "") + (comEstacas ? $" - {c.Estacas.Count} ø {Num(c.D)}" : "");
        Texto(c, t, x, yTopo, 1.8 * c.ht);
        double y = yTopo - 2.2 * c.ht;
        int max = Math.Max(20, (int)(largura / (0.75 * 1.1 * c.ht)));
        string atual = "";
        foreach (var p in c.ListaPilares.Split(' '))
        {
            if (atual.Length > 0 && atual.Length + 1 + p.Length > max) { Texto(c, atual, x, y, 1.1 * c.ht); y -= 1.6 * c.ht; atual = p; }
            else atual = atual.Length == 0 ? p : atual + " " + p;
        }
        if (atual.Length > 0) { Texto(c, atual, x, y, 1.1 * c.ht); y -= 1.6 * c.ht; }
        return y - c.ht;
    }

    private static IEnumerable<(double X, double Y)> Desloca(IEnumerable<(double X, double Y)> pts, double dx, double dy) => pts.Select(p => (p.X + dx, p.Y + dy));

    /// <summary>Contorno do bloco em planta, estacas e colarinho/abertura.</summary>
    private static void PlantaBase(Ctx c, double ox, double oy, bool estacas, bool calice)
    {
        Polilinha(c, "ARM-FORMA", true, Desloca(c.Contorno, ox, oy));
        if (estacas) foreach (var (x, y) in c.Estacas) c.R.Circulos.Add(new CirculoTemplate { X = ox + x, Y = oy + y, Raio = c.D / 2, Layer = "ARM-ESTACA" });
        if (calice && c.Calice)
        {
            if (c.Colar) Ret(c, ox + c.Cx0, oy + c.Cy0, ox + c.Cx1, oy + c.Cy1);
            Ret(c, ox + c.Ox0, oy + c.Oy0, ox + c.Ox1, oy + c.Oy1);
            Linha(c, "ARM-FORMA", (ox + c.Ox0, oy + c.Oy0), (ox + c.Ox1, oy + c.Oy1));
            Linha(c, "ARM-FORMA", (ox + c.Ox0, oy + c.Oy1), (ox + c.Ox1, oy + c.Oy0));
        }
    }

    // ======================== forma ========================

    /// <summary>Quadro de forma à esquerda (planta + corte A-A com níveis); devolve o x onde começa a armadura.</summary>
    private static double Forma(Ctx c)
    {
        double ht = c.ht, W = c.W, H = c.H, R = c.D / 2;
        double yT = TituloBloco(c, -10 * ht, H + 22 * ht, W + 20 * ht, true);
        Texto(c, "1:" + Num(c.esc), -10 * ht, yT + 0.2 * ht, 0.8 * ht);
        Linhas(c, "PLANTA", "PLAN", -10 * ht, yT - 1.4 * ht, ht);
        PlantaBase(c, 0, 0, false, true);
        foreach (var (x, y) in c.Estacas)
        {
            c.R.Circulos.Add(new CirculoTemplate { X = x, Y = y, Raio = R, Layer = "ARM-FORMA" });
            foreach (var (a0, a1) in new[] { (0.0, Math.PI / 2), (Math.PI, 1.5 * Math.PI) })
            {
                var hc = new HachuraTemplate { Padrao = "SOLID", Layer = "ARM-HACHURA-DETALHE" };
                hc.Vertices.Add((x, y));
                for (int i = 0; i <= 12; i++) { double a = a0 + (a1 - a0) * i / 12; hc.Vertices.Add((x + R * Math.Cos(a), y + R * Math.Sin(a))); }
                c.R.Hachuras.Add(hc);
            }
            Linha(c, "ARM-NIVEL", (x - R - 0.6 * ht, y), (x + R + 0.6 * ht, y));
            Linha(c, "ARM-NIVEL", (x, y - R - 0.6 * ht), (x, y + R + 0.6 * ht));
        }
        Ret(c, c.Px - c.Bx / 2, c.Py - c.By / 2, c.Px + c.Bx / 2, c.Py + c.By / 2, "ARM-PILAR-SECAO");
        // cadeias: faces, estacas, colarinho e pilar
        var xs = new List<double> { 0, W }.Concat(c.Estacas.Select(p => p.X)).ToList();
        var ys = new List<double> { 0, H }.Concat(c.Estacas.Select(p => p.Y)).ToList();
        if (c.Colar) { xs.AddRange(new[] { c.Cx0, c.Cx1 }); ys.AddRange(new[] { c.Cy0, c.Cy1 }); }
        xs.AddRange(new[] { c.Px - c.Bx / 2, c.Px + c.Bx / 2 }); ys.AddRange(new[] { c.Py - c.By / 2, c.Py + c.By / 2 });
        Cadeia(c, xs, false, -3 * ht);
        Cadeia(c, new List<double> { 0, W }.Concat(c.Estacas.Select(p => p.X)).ToList(), false, -6.5 * ht);
        Cota(c, (0, 0), (W, 0), (W / 2, -10 * ht), false);
        Cadeia(c, ys, true, W + 3 * ht);
        Cota(c, (W, 0), (W, H), (W + 7 * ht, H / 2), true);
        // marca do corte A (horizontal, pelo pilar)
        foreach (var x in new[] { -3 * ht, W + 10 * ht })
        {
            Polilinha(c, "ARM-CORTE", true, new[] { (x - 0.8 * ht, c.Py + 1.2 * ht), (x + 0.8 * ht, c.Py + 1.2 * ht), (x, c.Py) });
            Texto(c, "A", x, c.Py + 1.6 * ht, 1.2 * ht, 1, "ARM-CORTE");
        }

        // corte A-A (plano xz pelo pilar), abaixo da planta
        double stub = Math.Max(20, 4 * ht);
        double zb = -24 * ht - c.hT - stub;
        Linhas(c, "CORTE A-A", "SECTION A-A", -10 * ht, zb + c.hT + stub + 4 * ht, ht);
        var cd = Corda(c.Contorno, (0, c.Py), (1, 0)) ?? (0, W);
        Secao(c, true, 0, zb, cd.A, cd.B);
        double xl = cd.A - 4 * ht;
        if (c.lastro > 0) Cota(c, (cd.A, zb - c.lastro), (cd.A, zb), (xl, zb - c.lastro / 2), true);
        Cota(c, (cd.A, zb), (cd.A, zb + c.hB), (xl, zb + c.hB / 2), true);
        if (c.hC > 0) Cota(c, (c.Cx0, zb + c.hB), (c.Cx0, zb + c.hT), (c.Cx0 - 4 * ht, zb + c.hB + c.hC / 2), true);
        Cota(c, (cd.A, zb), (cd.A, zb + c.hT), (xl - 3.5 * ht, zb + c.hT / 2), true);
        if (c.Calice)
        {
            double xr = c.Ox1;
            Cota(c, (xr, zb + c.hT - c.Lemb), (xr, zb + c.hT), (xr + 3 * ht, zb + c.hT - c.Lemb / 2), true);
            if (c.reg > 0) Cota(c, (xr, zb + c.hT - c.Lemb - c.reg), (xr, zb + c.hT - c.Lemb), (xr + 3 * ht, zb + c.hT - c.Lemb - c.reg / 2), true);
            Cota(c, (xr, zb), (xr, zb + c.hT - c.Lemb - c.reg), (xr + 3 * ht, zb + (c.hT - c.Lemb - c.reg) / 2), true);
            Linhas(c, "REGULARIZAÇÃO DE FUNDO e=" + Num(c.reg) + " cm", "BOTTOM LEVELING", c.Px, zb + c.hT - c.Lemb - c.reg - 2 * ht, 0.7 * ht, 1);
        }
        if (c.lastro > 0)
            Linhas(c, "CONCRETO MAGRO e=" + Num(c.lastro) + " cm", "LEAN CONCRETE", cd.A - 2 * ht, zb - c.lastro - 3 * ht, 0.8 * ht, 2);
        Niveis(c, cd.B + 8 * ht, zb);
        c.YMin = Math.Min(c.YMin, zb - c.lastro - c.Lp - 6 * ht);
        return W + 50 * ht;
    }

    private static void Cadeia(Ctx c, List<double> vs, bool vertical, double pos)
    {
        var s = vs.Select(v => Math.Round(v, 2)).Distinct().OrderBy(v => v).ToList();
        for (int i = 0; i + 1 < s.Count; i++)
        {
            if (s[i + 1] - s[i] < 0.5) continue;
            if (vertical) Cota(c, (pos - 2 * c.ht, s[i]), (pos - 2 * c.ht, s[i + 1]), (pos, (s[i] + s[i + 1]) / 2), true);
            else Cota(c, (s[i], pos + 2 * c.ht), (s[i + 1], pos + 2 * c.ht), ((s[i] + s[i + 1]) / 2, pos), false);
        }
    }

    /// <summary>Níveis à direita do corte: TOPO DO BLOCO, FUNDO DO PILAR e COTA DE ARRASAMENTO, uma linha por pilar.</summary>
    private static void Niveis(Ctx c, double x, double zb)
    {
        var lin = c.V.Tabela("Niveis").Where(l => l.Length >= 2).ToList();
        var niveis = new List<(double Z, string Rot, string RotEn, Func<double, double> F)>
        {
            (zb + c.hT, "(TOPO DO BLOCO)", "(TOP OF PILE CAP)", t => t),
        };
        if (c.Calice) niveis.Add((zb + c.hT - c.Lemb, "(FUNDO DO PILAR)", "(COLUMN BASE)", t => t - c.Lemb / 100));
        niveis.Add((zb + c.emb, "(COTA DE ARRASAMENTO)", "(PILE CUT-OFF LEVEL)", t => t - (c.hT - c.emb) / 100));
        foreach (var (z, rot, rotEn, f) in niveis)
        {
            Linha(c, "ARM-NIVEL", (x - 6 * c.ht, z), (x + 14 * c.ht, z));
            Polilinha(c, "ARM-NIVEL", true, new[] { (x - 0.7 * c.ht, z + 1.2 * c.ht), (x + 0.7 * c.ht, z + 1.2 * c.ht), (x, z) });
            double y = z + 2.4 * c.ht + 1.3 * c.ht * (lin.Count - 1);
            foreach (var l in lin)
            {
                Texto(c, $"{ValoresTemplate.Celula(l, 0)}={NumV(f(ValoresTemplate.NumeroCelula(l, 1)))} m", x + 1.5 * c.ht, y, 0.8 * c.ht);
                y -= 1.3 * c.ht;
            }
            Linhas(c, rot, rotEn, x + 1.5 * c.ht, z + 1.1 * c.ht, 0.8 * c.ht);
        }
    }

    /// <summary>
    /// Seção vertical do bloco pela reta do corte (emX: plano xz em y = Py; senão yz em x = Px), com o pilar interrompido,
    /// lastro, regularização e estacas. Coordenada s ao longo do corte desenhada em x = ox + s; z = zb + z.
    /// </summary>
    private static void Secao(Ctx c, bool emX, double ox, double zb, double a, double b)
    {
        double ht = c.ht;
        double S(double s) => ox + s;
        double Z(double z) => zb + z;
        double c0 = emX ? c.Cx0 : c.Cy0, c1 = emX ? c.Cx1 : c.Cy1, o0 = emX ? c.Ox0 : c.Oy0, o1 = emX ? c.Ox1 : c.Oy1;
        double p0 = emX ? c.Px - c.Bx / 2 : c.Py - c.By / 2, p1 = emX ? c.Px + c.Bx / 2 : c.Py + c.By / 2;
        double zF = c.hT - c.Lemb - c.reg, zP = c.hT - c.Lemb;
        var pts = new List<(double, double)> { (S(a), Z(0)), (S(b), Z(0)) };
        if (c.Colar)
            pts.AddRange(new[] { (S(b), Z(c.hB)), (S(c1), Z(c.hB)), (S(c1), Z(c.hT)), (S(o1), Z(c.hT)), (S(o1), Z(zF)), (S(o0), Z(zF)), (S(o0), Z(c.hT)), (S(c0), Z(c.hT)), (S(c0), Z(c.hB)), (S(a), Z(c.hB)) });
        else if (c.Calice)
            pts.AddRange(new[] { (S(b), Z(c.hT)), (S(o1), Z(c.hT)), (S(o1), Z(zF)), (S(o0), Z(zF)), (S(o0), Z(c.hT)), (S(a), Z(c.hT)) });
        else pts.AddRange(new[] { (S(b), Z(c.hT)), (S(a), Z(c.hT)) });
        Polilinha(c, "ARM-FORMA", true, pts);
        // pilar (da base no cálice, ou nascendo no topo) interrompido acima
        double stub = Math.Max(20, 4 * ht), zp0 = c.Calice ? zP : c.hT;
        Linha(c, "ARM-PILAR-SECAO", (S(p0), Z(zp0)), (S(p0), Z(c.hT + stub)));
        Linha(c, "ARM-PILAR-SECAO", (S(p1), Z(zp0)), (S(p1), Z(c.hT + stub)));
        if (c.Calice) Linha(c, "ARM-PILAR-SECAO", (S(p0), Z(zP)), (S(p1), Z(zP)));
        double m = (p0 + p1) / 2;
        Linha(c, "ARM-FORMA", (S(p0 - 2 * ht), Z(c.hT + stub)), (S(m - 0.3 * ht), Z(c.hT + stub)), (S(m - 0.1 * ht), Z(c.hT + stub + 0.7 * ht)),
            (S(m + 0.1 * ht), Z(c.hT + stub - 0.7 * ht)), (S(m + 0.3 * ht), Z(c.hT + stub)), (S(p1 + 2 * ht), Z(c.hT + stub)));
        if (c.Calice && c.reg > 0)
            c.R.Hachuras.Add(new HachuraTemplate { Vertices = { (S(o0), Z(zF)), (S(o1), Z(zF)), (S(o1), Z(zP)), (S(o0), Z(zP)) }, Padrao = "AR-CONC", EscalaPadrao = c.esc / 50, Layer = "ARM-HACHURA-DETALHE" });
        // lastro e estacas (as atravessadas pelo corte; sem nenhuma, as projeções de todas)
        double R = c.D / 2, zl = -c.lastro, zp = -c.lastro - c.Lp;
        var est = c.Estacas.Where(p => Math.Abs(emX ? p.Y - c.Py : p.X - c.Px) < R).Select(p => emX ? p.X : p.Y).OrderBy(s => s).ToList();
        if (est.Count == 0) est = c.Estacas.Select(p => Math.Round(emX ? p.X : p.Y, 3)).Distinct().OrderBy(s => s).ToList();
        if (c.lastro > 0)
        {
            double s0 = a;
            foreach (var xc in est.Concat(new[] { double.PositiveInfinity }))
            {
                double s1 = Math.Min(b, xc - R);
                if (s1 - s0 > 0.5)
                    c.R.Hachuras.Add(new HachuraTemplate { Vertices = { (S(s0), Z(zl)), (S(s1), Z(zl)), (S(s1), Z(0)), (S(s0), Z(0)) }, Padrao = "AR-CONC", EscalaPadrao = c.esc / 50, Layer = "ARM-HACHURA-DETALHE" });
                s0 = Math.Max(s0, xc + R);
                if (s0 >= b) break;
            }
            Ret(c, S(a), Z(zl), S(b), Z(0));
        }
        foreach (var xc in est)
        {
            foreach (double d in new[] { -R, -R / 2, R / 2, R }) Linha(c, "ARM-ESTACA", (S(xc + d), Z(c.emb)), (S(xc + d), Z(zp)));
            c.R.Contornos.Add(new ContornoTemplate { Layer = "ARM-ESTACA", Vertices = { (S(xc - R), Z(zp)), (S(xc), Z(zp)), (S(xc + R), Z(zp)) }, Bulges = { 0.5, -0.5, 0 } });
        }
    }

    // ======================== armadura (vistas) ========================

    private static void Armadura(Ctx c, double x0)
    {
        double ht = c.ht, W = c.W, H = c.H, gap = 22 * ht;
        double yTopo = H + 22 * ht;
        TituloBloco(c, x0, yTopo, 4 * W + 60 * ht, false);
        double x = x0, yShapes = -14 * ht;
        // ---- vistas superiores: principal, secundária, superior ----
        foreach (var (vista, pt, en) in new[] { ("P", "ARMADURA INFERIOR PRINCIPAL", "MAIN BOTTOM REINFORCEMENT"),
                                                 ("S", "ARMADURA INFERIOR SECUNDÁRIA", "SECONDARY BOTTOM REINFORCEMENT"),
                                                 ("U", "ARMADURA SUPERIOR", "TOP REINFORCEMENT") })
        {
            var fams = c.Fam.Where(f => f.Vista == vista).ToList();
            if (fams.Count == 0) continue;
            double yt = Linhas(c, "VISTA SUPERIOR", "TOP VIEW", x, H + 12 * ht, 0.8 * ht);
            TituloVista(c, pt, en, x, yt);
            PlantaBase(c, x, 0, true, vista == "U");
            double yb = yShapes;
            foreach (var f in fams) yb = VistaFamilia(c, f, x, yb);
            c.YMin = Math.Min(c.YMin, yb);
            x += W + gap;
        }
        // ---- cálice ----
        if (c.Calice) x = VistaCalice(c, x, H, yShapes) + gap;
        // ---- lateral horizontal e estribo vertical ----
        double yL = 0;
        if (c.Lateral != null)
        {
            Linhas(c, "ARM. LATERAL HORIZONTAL", "HORIZONTAL SIDE REINFORCEMENT", x, H + 8 * ht, 1.1 * ht);
            Posicionar(c, c.Lateral, x, 0);
            yL = -8 * ht;
        }
        if (c.EstV != null) { Posicionar(c, c.EstV, x, yL - 6 * ht - (c.hB - 2 * c.c)); yL -= c.hB + 12 * ht; }
        if (c.Lateral != null || c.EstV != null) x += Math.Max(W, c.H) + gap;

        // ---- cortes A-A (xz) e B-B (yz), abaixo das vistas ----
        double stub = Math.Max(20, 4 * ht);
        double zb = Math.Min(c.YMin, yL) - 16 * ht - c.hT - stub;
        double xc = x0;
        foreach (bool emX in new[] { true, false })
        {
            var cd = emX ? Corda(c.Contorno, (0, c.Py), (1, 0)) ?? (0, W) : Corda(c.Contorno, (c.Px, 0), (0, 1)) ?? (0, H);
            TituloVista(c, emX ? "CORTE A-A" : "CORTE B-B", emX ? "SECTION A-A" : "SECTION B-B", xc, zb + c.hT + stub + 5 * ht);
            Secao(c, emX, xc, zb, cd.A, cd.B);
            ArmaduraNoCorte(c, emX, xc, zb, cd.A, cd.B);
            xc += cd.B + 34 * ht;
        }
        // arranque e estribo do pilar ao lado dos cortes
        if (c.Arr != null)
        {
            double lv = c.ArrInterno + c.ArrEspera;
            Mover(c.Arr, xc, zb);
            c.Arr.PosicaoDescricao = (xc + 1.8 * ht, zb + lv / 2); c.Arr.RotacaoDescricao = Math.PI / 2;
            Linhas(c, "ARRANQUE DO PILAR", "COLUMN STARTER BARS", xc, zb - 4 * ht, 0.8 * ht);
            xc += Math.Max(c.ArrPe, 6 * ht) + 12 * ht;
            if (c.ArrEst != null) { Posicionar(c, c.ArrEst, xc, zb); xc += c.Bx + 14 * ht; }
        }
        double yFimCortes = zb - c.lastro - c.Lp - 10 * ht;
        c.XMax = Math.Max(c.XMax, Math.Max(x, xc));

        // ---- relação do aço e informações para produção ----
        c.R.PosicaoRA = (c.XMax + 6 * ht, yTopo);
        Producao(c, c.XMax + 6 * ht, Math.Min(-10 * ht, yTopo - 70 * ht));

        // ---- detalhes padrão ----
        if (c.V.SimNao("Detalhes")) Detalhes(c, x0, Math.Min(yFimCortes, c.YMin) - 8 * ht);
    }

    private static void Mover(BarraTemplate b, double dx, double dy)
    {
        for (int i = 0; i < b.Vertices.Count; i++) b.Vertices[i] = (b.Vertices[i].X + dx, b.Vertices[i].Y + dy);
    }

    /// <summary>Barra gerada em qualquer lugar levada ao canto inferior esquerdo (x, y); descrição embaixo.</summary>
    private static void Posicionar(Ctx c, BarraTemplate b, double x, double y)
    {
        double mx = b.Vertices.Min(p => p.X), my = b.Vertices.Min(p => p.Y), w = b.Vertices.Max(p => p.X) - mx;
        Mover(b, x - mx, y - my);
        b.PosicaoDescricao = (x + w / 2, y - 1.8 * c.ht);
        c.XMax = Math.Max(c.XMax, x + w + 6 * c.ht);
        c.YMin = Math.Min(c.YMin, y - 4 * c.ht);
    }

    /// <summary>Uma família na vista: barras no lugar, valores "L" das VAR, faixa; e a forma dobrada embaixo.</summary>
    private static double VistaFamilia(Ctx c, Familia f, double ox, double yb)
    {
        double ht = c.ht;
        foreach (var l in f.Linhas)
        {
            var p = f.Ponto(l.T, l.A); var q = f.Ponto(l.T, l.B);
            Linha(c, f.LayerLugar, (ox + p.X, p.Y), (ox + q.X, q.Y));
            if (f.Var) Texto(c, Num(Math.Round(l.B - l.A)), ox + q.X + 0.6 * ht * f.U.X, q.Y + 0.6 * ht * f.U.Y - 0.3 * ht, 0.6 * ht, 0, "ARM-TEXTO", f.Ang);
        }
        // faixa perpendicular às barras (a 30 % do comprimento)
        if (f.Linhas.Count > 1)
        {
            double s = f.Linhas.Average(l => l.A + 0.3 * (l.B - l.A));
            var ts = f.Linhas.Select(l => l.T).ToList();
            var p1 = f.Ponto(ts.Min(), s); var p2 = f.Ponto(ts.Max(), s);
            bool eixo = Math.Abs(Math.Sin(f.Ang)) < 1e-6 || Math.Abs(Math.Cos(f.Ang)) < 1e-6;
            c.R.Faixas.Add(new FaixaTemplate
            {
                Barra = f.Barra.Nome, Tipo = eixo ? TipoFaixa.Ortogonal : TipoFaixa.Alinhada, Espacamento = f.Esp, Escala = c.esc, MostrarDiametro = false,
                P1 = (ox + p1.X, p1.Y), P2 = (ox + p2.X, p2.Y),
                Posicao = (ox + (p1.X + p2.X) / 2 - 2 * ht * f.U.X, (p1.Y + p2.Y) / 2 - 2 * ht * f.U.Y),
            });
        }
        // forma dobrada (a maior), embaixo da vista
        double L = Math.Round(f.Linhas.Max(l => l.B - l.A));
        var b = f.Barra;
        double y = yb - (f.Superior ? 0 : f.Perna);
        if (f.Superior) b.Vertices.AddRange(new[] { (ox, y - f.Perna), (ox, y), (ox + L, y), (ox + L, y - f.Perna) });
        else b.Vertices.AddRange(new[] { (ox, y + f.Perna), (ox, y), (ox + L, y), (ox + L, y + f.Perna) });
        if (f.Perna <= 0) { b.Vertices.RemoveAt(3); b.Vertices.RemoveAt(0); }
        for (int i = 0; i + 1 < b.Vertices.Count; i++) b.Formatos.Add(FormatoSegmento.Linha);
        if (f.Var) b.SegmentosVar.Add(f.Perna > 0 ? 1 : 0);
        b.PosicaoDescricao = (ox + L / 2, (f.Superior ? y - f.Perna : y) - 3.2 * ht);
        c.XMax = Math.Max(c.XMax, ox + L + 6 * ht);
        return (f.Superior ? y - f.Perna : y) - 9 * ht;
    }

    /// <summary>CÁLICE: planta das paredes com as horizontais e os grampos no lugar; formas das barras embaixo.</summary>
    private static double VistaCalice(Ctx c, double x, double H, double yShapes)
    {
        double ht = c.ht, cb = c.c;
        double w = c.Colar ? c.Cx1 - c.Cx0 : c.Ox1 - c.Ox0 + 2 * c.t, h = c.Colar ? c.Cy1 - c.Cy0 : c.Oy1 - c.Oy0 + 2 * c.t;
        double xw0 = c.Colar ? c.Cx0 : c.Ox0 - c.t, yw0 = c.Colar ? c.Cy0 : c.Oy0 - c.t;
        TituloVista(c, "CÁLICE", "SOCKET", x, H + 8 * ht);
        double ox = x - xw0, oy = H / 2 - (yw0 + h / 2);
        double X0 = x, Y0 = oy + yw0;
        Ret(c, X0, Y0, X0 + w, Y0 + h);
        Ret(c, ox + c.Ox0, oy + c.Oy0, ox + c.Ox1, oy + c.Oy1);
        Linha(c, "ARM-FORMA", (ox + c.Ox0, oy + c.Oy0), (ox + c.Ox1, oy + c.Oy1));
        Linha(c, "ARM-FORMA", (ox + c.Ox0, oy + c.Oy1), (ox + c.Ox1, oy + c.Oy0));
        if (c.CalTopo != null || c.CalResto != null) Ret(c, X0 + cb, Y0 + cb, X0 + w - cb, Y0 + h - cb, "ARM-BARRA-DETALHE");
        double pg = Math.Round(c.t - cb - c.cc);
        if (c.GrTopoX != null || c.GrRestoX != null)
        {
            Linha(c, "ARM-BARRA-2", (X0 + cb, Y0 + cb + pg), (X0 + cb, Y0 + cb), (X0 + w - cb, Y0 + cb), (X0 + w - cb, Y0 + cb + pg));
            Linha(c, "ARM-BARRA-2", (X0 + cb, Y0 + h - cb - pg), (X0 + cb, Y0 + h - cb), (X0 + w - cb, Y0 + h - cb), (X0 + w - cb, Y0 + h - cb - pg));
            Linha(c, "ARM-BARRA-2", (X0 + cb + pg, Y0 + cb), (X0 + cb, Y0 + cb), (X0 + cb, Y0 + h - cb), (X0 + cb + pg, Y0 + h - cb));
            Linha(c, "ARM-BARRA-2", (X0 + w - cb - pg, Y0 + cb), (X0 + w - cb, Y0 + cb), (X0 + w - cb, Y0 + h - cb), (X0 + w - cb - pg, Y0 + h - cb));
        }
        if (c.CalVert != null)
        {
            int n = c.V.Inteiro("CalVertQtd");
            double tm = c.t / 2;
            var vert = new List<(double, double)>();
            for (int i = 0; i < n; i++)
            {
                double fr = (i + 1.0) / (n + 1);
                foreach (var (px, py) in new[] { (X0 + w * fr, Y0 + tm), (X0 + w * fr, Y0 + h - tm), (X0 + tm, Y0 + h * fr), (X0 + w - tm, Y0 + h * fr) })
                {
                    vert.Add((px, py));
                    Ponto(c, px, py, c.CalVert.Bitola);
                }
            }
            Indicacao(c, c.CalVert, (X0 - 4 * ht, Y0 + h + 3 * ht), vert.Take(2));
        }
        if (c.CalTopo != null) Indicacao(c, c.CalTopo, (X0 + w + 4 * ht, Y0 + h + 2 * ht), new[] { (X0 + w - cb, Y0 + h * 0.7) });
        if (c.CalResto != null) Indicacao(c, c.CalResto, (X0 + w + 4 * ht, Y0 - 2 * ht), new[] { (X0 + w - cb, Y0 + h * 0.3) });
        // formas embaixo: fechadas e grampos numa coluna; verticais, suspensão e fundo ao lado
        double y = yShapes;
        foreach (var b in new[] { c.CalTopo, c.CalResto })
            if (b != null) { double hh = b.Vertices.Max(p => p.Y) - b.Vertices.Min(p => p.Y); Posicionar(c, b, x, y - hh); y -= hh + 9 * ht; }
        foreach (var b in new[] { c.GrTopoX, c.GrTopoY, c.GrRestoX, c.GrRestoY })
            if (b != null)
            {
                double hh = b.Vertices.Max(p => p.Y) - b.Vertices.Min(p => p.Y);
                Posicionar(c, b, x, y - hh);
                y -= hh + 7 * ht;
            }
        double xr = x + Math.Max(w, h) + 12 * ht, yr = yShapes;
        foreach (var b in new[] { c.CalVert, c.Susp, c.Fundo })
            if (b != null)
            {
                double hh = b.Vertices.Max(p => p.Y) - b.Vertices.Min(p => p.Y), ww = b.Vertices.Max(p => p.X) - b.Vertices.Min(p => p.X);
                Posicionar(c, b, xr, yr - hh);
                if (b == c.CalVert) { b.PosicaoDescricao = (xr - 2 * ht, yr - hh / 2); b.RotacaoDescricao = Math.PI / 2; }
                xr += ww + 14 * ht;
            }
        c.XMax = Math.Max(c.XMax, xr);
        c.YMin = Math.Min(c.YMin, y);
        return Math.Max(x + Math.Max(w, h) + 10 * ht, xr);
    }

    /// <summary>Armaduras no corte: famílias paralelas em U, perpendiculares em ponto (com chamada), lateral, cálice.</summary>
    private static void ArmaduraNoCorte(Ctx c, bool emX, double ox, double zb, double a, double b)
    {
        double ht = c.ht;
        double S(double s) => ox + s;
        double Z(double z) => zb + z;
        var dir = emX ? (X: 1.0, Y: 0.0) : (X: 0.0, Y: 1.0);
        var o = emX ? (X: 0.0, Y: c.Py) : (X: c.Px, Y: 0.0);
        int kInf = 0, kSup = 0;
        foreach (var f in c.Fam)
        {
            double cr = Math.Abs(f.U.X * dir.Y - f.U.Y * dir.X);
            if (cr < 0.05)
            {
                // paralela: os trechos (superior interrompida = dois) da reta mais perto do plano do corte
                double tPerto = f.Linhas.OrderBy(q => Math.Abs(q.T - Dot(f.N, o))).First().T;
                foreach (var l in f.Linhas.Where(q => Math.Abs(q.T - tPerto) < 1e-6))
                {
                    double sgn = Dot(f.U, dir) >= 0 ? 1 : -1;
                    double sa = sgn > 0 ? l.A : -l.B, sb2 = sgn > 0 ? l.B : -l.A;
                    double zp = f.Superior ? f.Z - f.Perna : f.Z + f.Perna;
                    Linha(c, f.LayerLugar, (S(sa), Z(zp)), (S(sa), Z(f.Z)), (S(sb2), Z(f.Z)), (S(sb2), Z(zp)));
                }
            }
            else
            {
                // perpendicular ou oblíqua: interseção de cada linha com o plano do corte
                var pts = new List<(double, double)>();
                foreach (var l in f.Linhas)
                {
                    double den = Dot(f.N, dir);
                    if (Math.Abs(den) < 1e-9) continue;
                    double s = (l.T - Dot(f.N, o)) / den;
                    var p = (X: o.X + s * dir.X, Y: o.Y + s * dir.Y);
                    double u = Dot(f.U, p);
                    if (u < l.A - 1e-6 || u > l.B + 1e-6) continue;
                    pts.Add((S(s), Z(f.Z)));
                }
                foreach (var (px, pz) in pts) Ponto(c, px, pz, f.Bitola);
                if (pts.Count > 0)
                {
                    // chamada curta à esquerda da face, na altura da camada; linhas só para as primeiras barras
                    double ty = f.Superior ? Z(f.Z) + (1.5 + 1.8 * kSup++) * ht : Z(f.Z) - (1.5 + 1.8 * kInf++) * ht;
                    Indicacao(c, f.Barra, (S(a) - 5 * ht, ty), pts.OrderBy(p => p.Item1).Take(3));
                }
            }
        }
        // lateral horizontal: pontos nas duas faces
        if (c.Lateral != null)
        {
            double r = c.Lateral.Bitola / 20;
            var refs = new List<(double, double)>();
            foreach (var z in c.ZLateral)
            {
                Ponto(c, S(a + c.c + r), Z(z), c.Lateral.Bitola); Ponto(c, S(b - c.c - r), Z(z), c.Lateral.Bitola);
                refs.Add((S(b - c.c - r), Z(z)));
            }
            Indicacao(c, c.Lateral, (S(b) + 5 * ht, Z(c.hB * 0.5)), refs);
        }
        // estribos verticais da base: linhas no A-A, retângulo no B-B
        if (c.EstV != null)
        {
            if (emX)
            {
                foreach (var x in c.XEstV) Linha(c, "ARM-ESTRIBO-SECAO", (S(x), Z(c.c)), (S(x), Z(c.hB - c.c)));
                Indicacao(c, c.EstV, (S(c.XEstV[0]) - 3 * ht, Z(c.hT) + 3 * ht), new[] { (S(c.XEstV[0]), Z(c.hB * 0.7)) });
            }
            else Ret(c, S(a + c.c), Z(c.c), S(b - c.c), Z(c.hB - c.c), "ARM-ESTRIBO-SECAO");
        }
        // cálice: horizontais em ponto nas paredes, verticais e suspensão
        if (c.Calice)
        {
            double c0 = emX ? (c.Colar ? c.Cx0 : c.Ox0 - c.t) : (c.Colar ? c.Cy0 : c.Oy0 - c.t);
            double c1 = emX ? (c.Colar ? c.Cx1 : c.Ox1 + c.t) : (c.Colar ? c.Cy1 : c.Oy1 + c.t);
            double o0 = emX ? c.Ox0 : c.Oy0, o1 = emX ? c.Ox1 : c.Oy1;
            foreach (var (bar, zs) in new[] { (c.CalTopo, c.ZCalTopo), (c.CalResto, c.ZCalResto) })
            {
                if (bar == null) continue;
                var refs = new List<(double, double)>();
                foreach (var z in zs)
                    foreach (var s in new[] { c0 + c.c + bar.Bitola / 20, c1 - c.c - bar.Bitola / 20 })
                    {
                        Ponto(c, S(s), Z(z), bar.Bitola);
                        if (s > (c0 + c1) / 2) refs.Add((S(s), Z(z)));
                    }
                Indicacao(c, bar, (S(Math.Max(c1, b)) + 6 * ht, Z(zs.Average()) + (bar == c.CalTopo ? 3 : -1) * ht), refs);
            }
            if (c.CalVert != null)
            {
                var v = c.CalVert.Vertices;
                double alt = v[1].Y - v[2].Y, larg = v[3].X - v[2].X, z0 = c.hT - c.c - alt;
                foreach (var (sOut, sgn) in new[] { (c0 + c.c, 1), (c1 - c.c, -1) })
                    Linha(c, "ARM-BARRA-DETALHE", (S(sOut), Z(c.hT - c.c)), (S(sOut), Z(z0)), (S(sOut + sgn * larg), Z(z0)), (S(sOut + sgn * larg), Z(c.hT - c.c)));
                Indicacao(c, c.CalVert, (S(c0) - 5 * ht, Z(c.hT) + 5 * ht), new[] { (S(c0 + c.c), Z(c.hT - c.c - alt / 3)) });
            }
            if (c.Susp != null && emX)
            {
                var v = c.Susp.Vertices;
                double dx = S(o0 - c.c) - v[3].X, dz = Z(c.hT - c.c) - v[2].Y;
                Linha(c, "ARM-BARRA-2", v.Select(p => (p.X + dx, p.Y + dz)).ToArray());
                Indicacao(c, c.Susp, (S((o0 + o1) / 2) + 6 * ht, Z(-c.lastro) - 3 * ht), new[] { ((v[3].X + v[4].X) / 2 + dx, v[3].Y + dz) });
            }
        }
        // arranque: barras do pilar entrando no bloco, com o pé
        if (c.Arr != null)
        {
            var ts = emX ? c.ArrXs : c.ArrYs;
            double centro = emX ? c.Px : c.Py, zf = c.hT - c.ArrInterno, stub = Math.Max(20, 4 * ht);
            foreach (var t in ts)
            {
                double s = Math.Abs(t - centro) < 1e-6 ? 1 : Math.Sign(centro - t) * (c.PeDentro ? 1 : -1);
                Linha(c, "ARM-BARRA-DETALHE", (S(t + s * c.ArrPe), Z(zf)), (S(t), Z(zf)), (S(t), Z(c.hT + stub)));
            }
            Indicacao(c, c.Arr, (S(ts.Max()) + 5 * ht, Z(c.hT + stub * 0.7)), new[] { (S(ts.Max()), Z(c.hT + stub * 0.4)) });
        }
    }

    // ======================== produção e detalhes ========================

    private static void Producao(Ctx c, double x, double y)
    {
        double peso = c.R.Barras.Sum(b => MedidaBarra.Peso(b, c.Cfg));   // mesmas regras da RA do CAD
        double vol = c.R.DadosDetalhamento.Volume;
        var t = new TabelaTemplate { Titulo = c.L("INFORMAÇÕES P/ PRODUÇÃO", "MANUFACTURING INFORMATION")[0], X = x, Y = y, Escala = c.esc };
        t.Larguras.AddRange(new[] { 24.0, 11.0 });
        t.Alinhamentos.AddRange(new[] { 0, 2 });
        t.Linhas.Add(new[] { "QUANTIDADE:", $"{c.Blocos} un." });
        t.Linhas.Add(new[] { "VOLUME DE CONCRETO (1 BLOCO):", $"{NumV(vol)} m³" });
        t.Linhas.Add(new[] { "PESO DE AÇO (1 BLOCO):", $"{NumV(Math.Round(peso, 1))} kg" });
        t.Linhas.Add(new[] { "PESO TOTAL DE AÇO:", $"{NumV(Math.Round(peso * c.Blocos, 1))} kg" });
        t.Linhas.Add(new[] { "TAXA:", vol > 0 ? $"{NumV(Math.Round(peso / vol, 1))} kg/m³" : "-" });
        t.Linhas.Add(new[] { "ÁREA DE FORMAS (1 BLOCO):", $"{NumV(c.R.DadosDetalhamento.AreaForma)} m²" });
        c.R.Tabelas.Add(t);
    }

    private static void Detalhes(Ctx c, double x0, double yTopo)
    {
        double ht = c.ht;
        bool chaves = c.Calice && c.V["Interface"] == Chaves;
        double x = TemplatePilarPreMoldado.DesenharDetalhesComuns(c.R, c.Idioma, c.esc, c.Cfg.AlturaTextoMm, x0, yTopo, chaves, 5, 4, 1, out _);
        // ancoragem das armaduras das estacas (esquemático)
        x += 6 * ht;
        double y = Linhas(c, "ANCORAGEM DAS ARMADURAS DAS ESTACAS", "ANCHORAGE OF PILE REINFORCEMENT", x, yTopo, 1.1 * ht);
        Texto(c, "SEM ESCALA", x, y + 0.3 * ht, 0.7 * ht);
        double bw = 16 * ht, bh = 8 * ht, bx = x, by = y - 4 * ht - bh;
        Ret(c, bx, by, bx + bw, by + bh);
        foreach (double fr in new[] { 0.2, 0.8 })
        {
            double ex = bx + fr * bw, r = 1.5 * ht;
            Ret(c, ex - r, by - 6 * ht, ex + r, by + 0.5 * ht, "ARM-ESTACA");
            foreach (double d in new[] { -0.6 * r, 0.6 * r })
                Linha(c, "ARM-BARRA-DETALHE", (ex + d, by - 6 * ht), (ex + d, by + bh - 0.6 * ht), (ex + d + Math.Sign(d) * 1.2 * ht, by + bh - 0.6 * ht));
        }
        double yt = by - 8 * ht;
        foreach (var l in c.L("ANCORAR AS ARMADURAS DAS ESTACAS NO TOPO DO BLOCO,", "ANCHOR THE PILE REINFORCEMENT AT THE TOP OF THE PILE CAP,")
                     .Concat(c.L("ACIMA DAS ARMADURAS PRINCIPAIS", "ABOVE THE MAIN REINFORCEMENT")))
        { Texto(c, l, bx, yt, 0.7 * ht); yt -= 1.2 * ht; }
        x += 44 * ht;
        // indicação de barra variável
        if (c.TemVar)
        {
            double yv = Linhas(c, "INDICAÇÃO BARRA VARIÁVEL", "VARIABLE BAR INDICATION", x, yTopo, 1.1 * ht);
            Texto(c, "ESC 1:" + Num(c.esc), x, yv + 0.3 * ht, 0.7 * ht);
            Ret(c, x, yv - 6 * ht, x + 42 * ht, yv - 1 * ht, "ARM-TEXTO-FORMA");
            Texto(c, "\"L\" - TRECHO RETO DA BARRA QUE É VARIÁVEL", x + ht, yv - 3 * ht, 0.7 * ht);
            Texto(c, "ESTÁ INDICADO NAS VISTAS EM PLANTA", x + ht, yv - 4.5 * ht, 0.7 * ht);
            x += 48 * ht;
        }
        // notas
        var notas = new List<string>
        {
            "NOTAS:",
            $"1) CONCRETO DO BLOCO: fck = {Num(c.V.Numero("Fck"))} MPa;",
            "2) COTAS EM CENTÍMETROS E NÍVEIS EM METROS;",
            $"3) AÇO {c.Aco};",
            $"4) COBRIMENTOS: ELEMENTOS EM CONTATO COM O SOLO {NumV(c.c)} cm" + (c.Calice ? $"; INTERNO DO CÁLICE {NumV(c.cc)} cm;" : ";"),
            "5) CONFERIR TODAS AS MEDIDAS NO LOCAL ANTES DA EXECUÇÃO.",
        };
        double yn = yTopo;
        foreach (var n in notas) { Texto(c, n, x, yn, 0.8 * ht); yn -= 1.4 * ht; }
        c.XMax = Math.Max(c.XMax, x + 50 * ht);
    }

    // ======================== verificações ========================

    private static void Verificar(Ctx c)
    {
        var v = c.V;
        var avisos = c.R.Avisos;
        double R = c.D / 2;
        if (c.e < R) avisos.Add($"Distância do eixo da estaca à face ({Num(c.e)} cm) menor que o raio da estaca ({Num(R)} cm).");
        if (c.Px - c.Bx / 2 < 0 || c.Px + c.Bx / 2 > c.W || c.Py - c.By / 2 < 0 || c.Py + c.By / 2 > c.H) avisos.Add("O pilar sai do bloco.");
        if (c.Calice)
        {
            double h = Math.Max(c.Bx, c.By);
            if (c.Lemb < 40) avisos.Add($"Embutimento {Num(c.Lemb)} cm < 40 cm (NBR 9062 7.7.2.4).");
            double fator = v["Interface"] == Chaves ? 1.2 : 1.5;
            if (c.Lemb < fator * h)
                avisos.Add($"Embutimento {Num(c.Lemb)} cm < {NumV(fator)}·h = {Num(Math.Ceiling(fator * h))} cm (menor valor da NBR 9062 Tabela 15) — conferir com Md/(Nd·h).");
            if (c.Colar && c.t < 15) avisos.Add($"Parede do colarinho {Num(c.t)} cm < 15 cm (NBR 9062 Fig. 25).");
            double fundo = c.hT - c.Lemb - c.reg;
            if (fundo < 20) avisos.Add($"Altura sob o cálice {Num(fundo)} cm < 20 cm (NBR 9062 Fig. 25).");
            if (c.Colar && (c.Ox0 - c.t < -1e-6 || c.Ox1 + c.t > c.W + 1e-6 || c.Oy0 - c.t < -1e-6 || c.Oy1 + c.t > c.H + 1e-6))
                avisos.Add("Colarinho maior que o bloco numa direção: limitado à face do bloco (como no B28).");
        }
        if (c.Arr != null)
        {
            var tr = new RegrasTraspasse { Fck = v.Numero("Fck"), PercentualEmendado = 100 };
            double lb = Math.Ceiling(tr.Lb(c.Arr.Bitola, c.Aco));
            if (c.ArrInterno < lb)
                avisos.Add($"Arranque: trecho reto no bloco {Num(c.ArrInterno)} cm < lb = {Num(lb)} cm; com o pé (gancho) a NBR 6118 9.4.2.5 permite 0,7·lb = {Num(Math.Ceiling(0.7 * lb))} cm — conferir.");
        }
        if (c.Tri && v.Numero("EstVBitola") > 0) avisos.Add("Bloco triangular: estribos verticais da base não são gerados (só a lateral horizontal em volta).");
    }
}
