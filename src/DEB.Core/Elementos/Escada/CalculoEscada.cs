using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Documentacao;
using DEB.Core.Elementos.Viga.PreMoldada.Calculo;
using DEB.Core.Normas;
using DEB.Core.Normas.Verificacao;

namespace DEB.Core.Elementos.Escada;

/// <summary>Ponto dos diagramas da escada (por metro de largura): x horizontal (cm), esforços de cálculo e flecha.</summary>
public sealed class PontoEscada
{
    public double X { get; set; }
    /// <summary>z do eixo (cm).</summary>
    public double Z { get; set; }
    /// <summary>Momento fletor de cálculo (kN·m/m; positivo = tração embaixo).</summary>
    public double Md { get; set; }
    /// <summary>Cortante de cálculo perpendicular ao eixo (kN/m).</summary>
    public double Vd { get; set; }
    /// <summary>Normal de cálculo ao longo do eixo (kN/m; compressão negativa).</summary>
    public double Nd { get; set; }
    /// <summary>Deslocamento vertical (cm, para baixo positivo), combinação quase permanente, rigidez elástica (Ic) — para comparar com o MEF.</summary>
    public double FlechaElastica { get; set; }
}

/// <summary>Barra proposta da escada na elevação (x, z em cm), com a quantidade na largura (ou ao longo, para as de distribuição).</summary>
public sealed class BarraEscada
{
    public string Nome { get; set; } = "";
    public string Funcao { get; set; } = "";
    public double Bitola { get; set; }
    public double Espacamento { get; set; }
    public int Quantidade { get; set; }
    /// <summary>Polilinha da barra na elevação. Para as transversais (distribuição) é a linha ao longo da qual elas se repetem.</summary>
    public List<(double X, double Z)> Pontos { get; set; } = new List<(double, double)>();
    /// <summary>Barra transversal (corre na largura): no desenho é um ponto em cada posição.</summary>
    public bool Transversal { get; set; }
    /// <summary>Forma da barra transversal no corte: (y na largura, deslocamento perpendicular à laje a partir da linha de
    /// <see cref="Pontos"/>), em cm; y = 0 no meio da largura. Vazio = reta de ponta a ponta da largura (menos o cobrimento).</summary>
    public List<(double Y, double N)> PerfilY { get; set; } = new List<(double, double)>();
    /// <summary>Comprimento de uma barra (cm).</summary>
    public double Comprimento { get; set; }
    public string Texto => $"{Nome} — {Quantidade} ø{Fmt.Virgula(Bitola, "0.#")} c/{Fmt.Virgula(Espacamento, "0.#")} ({Funcao})";
}

/// <summary>Resultado do cálculo da escada armada longitudinalmente (por metro de largura, salvo onde dito).</summary>
public sealed class ResultadoEscada
{
    public EscadaLaje Entrada { get; set; }
    public GeometriaEscada Geo { get; set; }
    public CoeficientesViga Coef { get; set; }

    // cargas características por m² de projeção horizontal
    public double GLance { get; set; }
    public double GPatamar { get; set; }
    public double GParapeito { get; set; }
    public double Q { get; set; }

    // reações características por metro de largura (kN/m) e totais (kN)
    public double RaG { get; set; }
    public double RaQ { get; set; }
    public double RbG { get; set; }
    public double RbQ { get; set; }
    public double Largura => Entrada.Largura / 100;
    /// <summary>Comprimento (m) que multiplica as reações por metro para dar o total: a largura (longitudinal) ou o comprimento
    /// inclinado do lance (transversal e balanço, reações nos apoios laterais).</summary>
    public double ComprimentoReacao => Entrada.Tipo == TipoEscada.Longitudinal ? Largura : Geo.LanceHorizontal / Geo.Cos / 100;
    /// <summary>Vão do cálculo (cm): horizontal entre apoios (longitudinal), eixo a eixo dos apoios laterais (transversal) ou da face do
    /// apoio à borda livre (balanço).</summary>
    public double VaoCalculo { get; set; }
    /// <summary>Momento característico no engaste do balanço (kN·m/m), permanente e variável.</summary>
    public double MEngG { get; set; }
    public double MEngQ { get; set; }

    public List<PontoEscada> Diagrama { get; } = new List<PontoEscada>();
    public double MdMax { get; set; }
    public double XMdMax { get; set; }
    public double VdMax { get; set; }
    public double NdMin { get; set; }
    public double NdMax { get; set; }

    // armaduras (cm²/m)
    public double AsNec { get; set; }
    public double AsMin { get; set; }
    public double AsDistNec { get; set; }
    public double AsNegNec { get; set; }
    public (double Bitola, double Esp) Principal { get; set; }
    public (double Bitola, double Esp) Distribuicao { get; set; }
    public (double Bitola, double Esp) Negativa { get; set; }
    public double AsPrincipal => Principal.Esp > 0 ? Math.PI * Principal.Bitola * Principal.Bitola / 400 * 100 / Principal.Esp : 0;
    public double D { get; set; }
    public double Lb { get; set; }
    public double LbNec { get; set; }
    public double VRd1 { get; set; }
    public double MRd { get; set; }

    // ELS
    public double FlechaImediata { get; set; }
    public double FlechaTotal { get; set; }
    public double FlechaElasticaMax { get; set; }
    public double Wk { get; set; }

    public List<BarraEscada> Barras { get; } = new List<BarraEscada>();
    /// <summary>Pré-moldada (D43): ligações das pontas, içamento e a armadura superior da peça.</summary>
    public List<LigacaoCalculada> Ligacoes { get; } = new List<LigacaoCalculada>();
    public IcamentoEscada Icamento { get; set; }
    public double AsSuperiorPm { get; set; }
    public (double Bitola, double Esp) SuperiorPm { get; set; }
    public ListaVerificacoes Verificacoes { get; } = new ListaVerificacoes();
    public List<PassoMemoria> Memoria { get; } = new List<PassoMemoria>();
    public List<string> Avisos { get; } = new List<string>();
    public List<string> Erros { get; } = new List<string>();
    /// <summary>Cargas características em trechos da projeção (kN por cm de vão e por metro de largura), as mesmas da estática.</summary>
    public List<(double X0, double X1, double W)> SegmentosG { get; } = new List<(double, double, double)>();
    public List<(double X0, double X1, double W)> SegmentosQ { get; } = new List<(double, double, double)>();

    /// <summary>Resumo da armadura proposta, uma linha por barra.</summary>
    public List<string> ResumoProposta() => Barras.Select(b => b.Texto).ToList();
}

/// <summary>
/// Cálculo da escada em laje armada longitudinalmente (D42): viga de eixo quebrado biapoiada (um apoio fixo e outro móvel na
/// horizontal), carga por m² de projeção horizontal, momento pela projeção (M = pℓ²/8 com carga uniforme), V = Vh·cosα e
/// N = −Vh·senα no lance (apostila de escadas, 7.3.2.2). Dimensionamento de laje por metro de largura (NBR 6118: flexão 17.2,
/// mínimos 19.3.3.2 e Tabela 19.1, cisalhamento sem estribo 19.4.1, flecha 17.3.2.1, fissuras 17.3.3.2). A armadura positiva
/// cruza na quina reentrante da face inferior (lance com o patamar superior), com o comprimento de ancoragem depois do
/// cruzamento: a barra que acompanha a quina é arrancada pelo empuxo no vazio.
/// </summary>
public static partial class CalculoEscada
{
    public static readonly double[] BitolasPrincipal = { 6.3, 8, 10, 12.5, 16 };
    public static readonly double[] BitolasDistribuicao = { 5, 6.3, 8 };

    private static string N(double x, string f = "0.##") => Fmt.Virgula(x, f);
    private static double A(double phiMm) => Math.PI * phiMm * phiMm / 400;

    public static ResultadoEscada Calcular(EscadaLaje e, CoeficientesViga k = null)
    {
        k ??= new CoeficientesViga();
        bool lateral = e.Tipo != TipoEscada.Longitudinal;
        bool tirouPatamar = lateral && (e.PatamarInferior > 0 || e.PatamarSuperior > 0);
        if (tirouPatamar) { e = e.Copia(); e.PatamarInferior = 0; e.PatamarSuperior = 0; }
        bool soPat = e.SoPatamar;
        if (soPat && Math.Abs(e.Desnivel) > 1e-9) { e = e.Copia(); e.Desnivel = 0; }   // patamar: topo em z = 0 nas duas pontas
        bool tirouCargas = lateral && e.CargasApoiadas.Count > 0;
        if (tirouCargas) { e = e.Copia(); e.CargasApoiadas.Clear(); }
        var r = new ResultadoEscada { Entrada = e.Copia(), Coef = k };
        if (tirouCargas) r.Avisos.Add("Cargas de outras peças só entram na escada armada longitudinalmente (lance ou patamar); aqui foram ignoradas.");
        if (tirouPatamar) r.Avisos.Add("Escada armada transversalmente ou em balanço: os patamares são lajes à parte (calcule-os como laje); aqui entra só o lance.");
        var erros = new List<string>();
        var g = GeometriaEscada.De(e, erros);
        r.Geo = g;
        r.Erros.AddRange(erros);
        double gc = e.PesoEspecifico > 0 ? e.PesoEspecifico : 25;
        double h = g.H, hp = g.Hp, c = g.Cos, s = g.Sen, L = g.Vao;

        // ---------------- 1. geometria ----------------
        var m1 = new PassoMemoria { Titulo = "1. Geometria (apostila de escadas, 7.1; NBR 9077)" };
        if (soPat)
        {
            m1.Titulo = "1. Geometria do patamar (peça plana)";
            m1.Linhas.Add($"Patamar plano biapoiado: vão horizontal entre os eixos dos apoios ℓ = {N(L, "0.#")} cm; espessura hp = {N(hp, "0.#")} cm; largura {N(e.Largura, "0")} cm.");
            r.Memoria.Add(m1);
            r.Verificacoes.Add(K("E7", "Geometria", "Espessura mínima da laje", $"{N(hp, "0.#")} cm", "≥ 8 cm (laje de piso)", hp < 8 - 1e-9, "NBR 6118, 13.2.4.1"));
            foreach (string er in r.Erros) r.Avisos.Add(er);
            r.Avisos.AddRange(g.Avisos);
        }
        else
        {
        m1.Linhas.Add($"Desnível {N(e.Desnivel, "0.#")} cm em n = {g.N} espelhos: e = {N(g.Espelho, "0.00")} cm; piso p = {N(g.Piso, "0.#")} cm; " +
                      $"α = atan(e/p) = {N(g.Alfa * 180 / Math.PI, "0.0")}°.");
        m1.Linhas.Add($"Lance: projeção (n − 1)·p = {N(g.LanceHorizontal, "0.#")} cm; patamares {N(e.PatamarInferior, "0.#")} (inferior) e {N(e.PatamarSuperior, "0.#")} cm (superior) " +
                      $"→ vão horizontal entre os eixos dos apoios ℓ = {N(L, "0.#")} cm.");
        m1.Linhas.Add($"Espessura do lance h = {N(h, "0.#")} cm (perpendicular ao plano); h/cosα = {N(h / c, "0.0")} cm na vertical; patamares hp = {N(hp, "0.#")} cm.");
        double blondel = 2 * g.Espelho + g.Piso;
        m1.Linhas.Add($"Blondel: 2e + p = {N(blondel, "0.0")} cm (confortável de 61 a 65 cm).");
        r.Memoria.Add(m1);
        r.Verificacoes.Add(K("E1", "Geometria", "Fórmula de Blondel (2e + p)", $"{N(blondel, "0.0")} cm", "61 a 65 cm", blondel < 61 - 1e-9 || blondel > 65 + 1e-9,
            "Apostila 7.1; NBR 9077", SituacaoVerificacao.Conferir, "Ajuste o piso ou o número de espelhos."));
        r.Verificacoes.Add(K("E2", "Geometria", "Altura do espelho", $"{N(g.Espelho, "0.00")} cm", "16 a 19 cm", g.Espelho < 16 - 1e-9 || g.Espelho > 19 + 1e-9,
            "Apostila 7.1; NBR 9077", SituacaoVerificacao.Conferir, "Mude o número de espelhos."));
        r.Verificacoes.Add(K("E3", "Geometria", "Profundidade do piso", $"{N(g.Piso, "0.#")} cm", "28 a 32 cm", g.Piso < 28 - 1e-9 || g.Piso > 32 + 1e-9,
            "Apostila 7.1; NBR 9077", SituacaoVerificacao.Conferir));
        r.Verificacoes.Add(K("E4", "Geometria", "Espelhos no lance sem patamar", $"{g.N}", "≤ 19 (acima, patamar intermediário)", g.N > 19,
            "Apostila 7.1; NBR 9077", SituacaoVerificacao.Conferir, "Divida em dois lances com patamar intermediário."));
        r.Verificacoes.Add(K("E5", "Geometria", "Largura da escada", $"{N(e.Largura, "0")} cm", "≥ 80 cm (≥ 120 cm em edifícios de apartamentos, hotéis e escritórios)",
            e.Largura < 120 - 1e-9, "Apostila 7.1; NBR 9077", e.Largura < 80 - 1e-9 ? SituacaoVerificacao.NaoAtende : SituacaoVerificacao.Conferir,
            "Confira a largura exigida pela NBR 9077 para a ocupação."));
        if (e.AlturaLivre > 0)
            r.Verificacoes.Add(K("E6", "Geometria", "Altura livre sob o lance", $"{N(e.AlturaLivre, "0")} cm", "≥ 200 cm", e.AlturaLivre < 200 - 1e-9, "Apostila 7.1; NBR 9077"));
        r.Verificacoes.Add(K("E7", "Geometria", "Espessura mínima da laje", $"{N(Math.Min(h, hp), "0.#")} cm", "≥ 8 cm (laje de piso)", Math.Min(h, hp) < 8 - 1e-9, "NBR 6118, 13.2.4.1"));
        foreach (string er in r.Erros) r.Avisos.Add(er);
        r.Avisos.AddRange(g.Avisos);
        }

        // ---------------- 2. cargas ----------------
        r.GLance = gc * (h / c + g.Espelho / 2) / 100 + e.Revestimento;
        r.GPatamar = gc * hp / 100 + e.Revestimento;
        r.GParapeito = e.Parapeito > 0 && e.BordasComParapeito > 0 ? e.Parapeito * Math.Min(2, e.BordasComParapeito) / Math.Max(0.01, e.Largura / 100) : 0;
        r.Q = e.Sobrecarga;
        var m2 = new PassoMemoria { Titulo = "2. Cargas por m² de projeção horizontal (apostila 7.2)" };
        if (!soPat) m2.Linhas.Add($"Lance: peso próprio γ·(h/cosα + e/2) = {N(gc, "0.#")}·({N(h / c / 100, "0.000")} + {N(g.Espelho / 200, "0.000")}) = {N(gc * (h / c + g.Espelho / 2) / 100, "0.00")} kN/m²; " +
                      $"com revestimento {N(e.Revestimento)} → g = {N(r.GLance, "0.00")} kN/m².");
        m2.Linhas.Add($"Patamar: γ·hp = {N(gc * hp / 100, "0.00")} kN/m² + revestimento → g = {N(r.GPatamar, "0.00")} kN/m².");
        if (r.GParapeito > 0) m2.Linhas.Add($"Parapeito: {N(e.Parapeito)} kN/m × {Math.Min(2, e.BordasComParapeito)} borda(s) / {N(e.Largura / 100, "0.00")} m = {N(r.GParapeito, "0.00")} kN/m² (distribuído na largura).");
        m2.Linhas.Add($"Sobrecarga: q = {N(r.Q)} kN/m² (NB-5: 2,5 sem acesso ao público, 3,0 com acesso; conferir na NBR 6120:2019).");
        if (!soPat) m2.Linhas.Add("A carga por m² de superfície inclinada (gi = g·cosα) dá a mesma resultante; o cálculo usa a projeção horizontal, que é o usual.");
        foreach (var ca in e.CargasApoiadas)
            m2.Linhas.Add($"Apoiada nesta peça — {(string.IsNullOrEmpty(ca.Nome) ? "outra peça" : ca.Nome)}: G = {N(ca.G, "0.00")} + Q = {N(ca.Q, "0.00")} kN por metro de largura, " +
                          $"de x = {N(ca.X, "0")} a {N(ca.X + ca.Comprimento, "0")} cm.");
        r.Memoria.Add(m2);

        if (lateral) { CalcularLateral(r, k); return r; }

        // ---------------- 3. esforços (viga de eixo quebrado biapoiada) ----------------
        var cg = new List<(double X0, double X1, double W)>();
        var cq = new List<(double X0, double X1, double W)>();
        if (g.X1 > 1e-9) { cg.Add((0, g.X1, (r.GPatamar + r.GParapeito) / 100)); cq.Add((0, g.X1, r.Q / 100)); }
        cg.Add((g.X1, g.X2, (r.GLance + r.GParapeito) / 100)); cq.Add((g.X1, g.X2, r.Q / 100));
        if (L - g.X2 > 1e-9) { cg.Add((g.X2, L, (r.GPatamar + r.GParapeito) / 100)); cq.Add((g.X2, L, r.Q / 100)); }
        if (soPat) { cg.Clear(); cq.Clear(); cg.Add((0, L, (r.GPatamar + r.GParapeito) / 100)); cq.Add((0, L, r.Q / 100)); }
        foreach (var ca in e.CargasApoiadas)
        {
            // carga de outra peça: resultante por metro de largura espalhada no comprimento de apoio (dentro do vão)
            double x0 = Math.Max(0, Math.Min(L, ca.X)), x1 = Math.Max(0, Math.Min(L, ca.X + Math.Max(1, ca.Comprimento)));
            if (x1 - x0 < 1) { x0 = Math.Max(0, Math.Min(L - 1, ca.X)); x1 = x0 + 1; r.Avisos.Add($"Carga apoiada \"{ca.Nome}\" fora do vão: levada para dentro."); }
            cg.Add((x0, x1, ca.G / (x1 - x0))); cq.Add((x0, x1, ca.Q / (x1 - x0)));
        }
        r.SegmentosG.AddRange(cg); r.SegmentosQ.AddRange(cq);
        r.VaoCalculo = L;
        var eg = new EstaticaEscada(L, cg);
        var eq = new EstaticaEscada(L, cq);
        // cargas em kN/m² ÷ 100 = kN por m de largura e por cm de vão → reações em kN/m de largura, momentos em kN·cm/m
        r.RaG = eg.Ra; r.RbG = eg.Rb; r.RaQ = eq.Ra; r.RbQ = eq.Rb;
        int np = 200;
        for (int i = 0; i <= np; i++)
        {
            double x = L * i / np;
            var t = g.TrechoEm(x);
            double a = t.Inclinado ? g.Alfa : 0;
            double vh = k.GamaG * eg.Vh(x) + k.GamaQ * eq.Vh(x);   // kN/m (carga em kN/(m·cm) × cm)
            r.Diagrama.Add(new PontoEscada
            {
                X = x, Z = t.ZEm(x),
                Md = (k.GamaG * eg.M(x) + k.GamaQ * eq.M(x)) / 100,   // kN·cm/m → kN·m/m
                Vd = vh * Math.Cos(a),
                Nd = -vh * Math.Sin(a),
            });
        }
        var pmax = r.Diagrama.OrderByDescending(p => p.Md).First();
        r.MdMax = pmax.Md; r.XMdMax = pmax.X;
        r.VdMax = r.Diagrama.Max(p => Math.Abs(p.Vd));
        r.NdMin = r.Diagrama.Min(p => p.Nd); r.NdMax = r.Diagrama.Max(p => p.Nd);
        double pd = k.GamaG * (r.GLance + r.GParapeito) + k.GamaQ * r.Q;
        var m3 = new PassoMemoria { Titulo = "3. Esforços de cálculo por metro de largura (apostila 7.3.2.2; γg = γq = " + N(k.GamaG) + ")" };
        m3.Linhas.Add("Esquema: viga de eixo quebrado biapoiada, um apoio fixo e o outro móvel na horizontal (sem empuxo). " +
                      "M pela projeção horizontal; no lance V = Vh·cosα e N = −Vh·senα (compressão embaixo, tração em cima).");
        m3.Linhas.Add($"Reações características por metro de largura: inferior G = {N(r.RaG, "0.00")} + Q = {N(r.RaQ, "0.00")} kN/m; superior G = {N(r.RbG, "0.00")} + Q = {N(r.RbQ, "0.00")} kN/m " +
                      $"(escada inteira, × {N(r.Largura, "0.00")} m: {N((r.RaG + r.RaQ) * r.Largura, "0.0")} e {N((r.RbG + r.RbQ) * r.Largura, "0.0")} kN).");
        m3.Linhas.Add($"Md,máx = {N(r.MdMax, "0.00")} kN·m/m em x = {N(r.XMdMax, "0")} cm" +
                      (g.X1 < 1e-9 && L - g.X2 < 1e-9 ? $" (= pd·ℓ²/8 = {N(pd, "0.00")}·{N(L / 100, "0.00")}²/8)" : "") + ".");
        m3.Linhas.Add($"Vd,máx = {N(r.VdMax, "0.00")} kN/m; Nd de {N(r.NdMin, "0.00")} a {N(r.NdMax, "0.00")} kN/m (desprezado no dimensionamento à flexão, como na apostila).");
        r.Memoria.Add(m3);

        // ---------------- 4. flexão: armadura principal positiva ----------------
        double fcd = e.Fck / k.GamaC / 10, fyd = TabelasNbr.Fyk(e.Aco) / k.GamaS / 10;
        double fctm = TabelasNbr.Fctm(e.Fck) / 10, fctd = 0.7 * fctm / k.GamaC;
        var mat = new Flexao(e.Fck, fcd, fyd);
        double cob = e.Cobrimento, b = 100;
        double rhoMin = TabelasNbr.RhoMinVigaPct(e.Fck) / 100;
        double hMin = Math.Min(h, hp);
        // As necessária em cada ponto, com a espessura do trecho (o máximo pode cair no patamar mais fino)
        double phiEst = 10;
        double DD(double hh, double phi) => hh - cob - phi / 20;
        double AsEm(PontoEscada p, double phi)
        {
            if (p.Md <= 0) return 0;
            double hh = g.TrechoEm(p.X).H;
            return mat.Armadura(p.Md * 100, b, DD(hh, phi), cob).As;
        }
        r.AsMin = rhoMin * b * hMin;
        (double Bitola, double Esp) melhor = (0, 0);
        double melhorAs = double.MaxValue, asNecMelhor = 0;
        // espaçamento mínimo de 10 cm (prática de obra); só se nenhuma bitola couber, 7,5 cm
        foreach (double sMin in new[] { 10.0, 7.5 })
        {
            foreach (double phi in BitolasPrincipal)
            {
                if (phi > hMin * 10 / 8 + 1e-9) continue;   // φ ≤ h/8 (20.1)
                double nec = Math.Max(r.Diagrama.Max(p => AsEm(p, phi)), r.AsMin);
                double sMax = Math.Min(2 * hMin, 20);
                double sCalc = Math.Floor(A(phi) * 100 / nec / 2.5 + 1e-9) * 2.5;
                double esp = Math.Min(sMax, sCalc);
                if (esp < sMin - 1e-9) continue;
                double asEf = A(phi) * 100 / esp;
                if (asEf < melhorAs - 1e-6 || (Math.Abs(asEf - melhorAs) < 1e-6 && esp > melhor.Esp)) { melhorAs = asEf; melhor = (phi, esp); asNecMelhor = nec; phiEst = phi; }
            }
            if (melhor.Esp > 0) break;
        }
        var m4 = new PassoMemoria { Titulo = "4. Flexão — armadura principal (NBR 6118, 17.2.2; lajes 19.3.3.2 e 20.1)" };
        m4.Linhas.Add($"fcd = {N(fcd * 10, "0.00")} MPa; fyd = {N(fyd * 10, "0.0")} MPa; b = 100 cm; cobrimento {N(cob, "0.#")} cm.");
        m4.Linhas.Add($"As,mín = ρmín·b·h = {N(rhoMin * 100, "0.000")} % × 100 × {N(hMin, "0.#")} = {N(r.AsMin)} cm²/m (Tabela 19.1, armadura positiva de laje armada numa direção).");
        if (melhor.Esp <= 0)
        {
            r.Avisos.Add("Flexão: nenhuma bitola até ø16 com espaçamento ≥ 7,5 cm resolve — aumente a espessura da laje.");
            r.Verificacoes.Add(K("K1", "Flexão", "Armadura principal (ELU)", $"Md = {N(r.MdMax, "0.0")} kN·m/m", "bitola até ø16 c/ ≥ 7,5", true, "NBR 6118, 17.2.2", dica: "Aumente a espessura."));
        }
        else
        {
            r.Principal = melhor; r.AsNec = asNecMelhor;
            r.D = DD(g.TrechoEm(r.XMdMax).H, melhor.Bitola);
            var f = mat.Armadura(r.MdMax * 100, b, r.D, cob);
            double xr = Math.Min(r.AsPrincipal * fyd / (mat.AlfaC * fcd * b * mat.Lambda), mat.XdLim * r.D);
            r.MRd = r.AsPrincipal * fyd * (r.D - mat.Lambda * xr / 2) / 100;
            m4.Linhas.Add($"No ponto de Md,máx: d = h − c − φ/2 = {N(r.D, "0.0")} cm; x/d = {N(f.XsobreD, "0.000")}; As = {N(f.As)} cm²/m; envoltória ao longo do eixo (espessura de cada trecho): As,nec = {N(asNecMelhor)} cm²/m.");
            m4.Linhas.Add($"Proposta: ø{N(melhor.Bitola, "0.#")} c/{N(melhor.Esp, "0.#")} = {N(r.AsPrincipal)} cm²/m (s ≤ 2h e ≤ 20 cm; φ ≤ h/8); MRd = {N(r.MRd, "0.00")} kN·m/m.");
            r.Verificacoes.Add(K("K1", "Flexão", "Armadura principal positiva (ELU)", $"ø{N(melhor.Bitola, "0.#")} c/{N(melhor.Esp, "0.#")} = {N(r.AsPrincipal)} cm²/m", $"≥ {N(asNecMelhor)} cm²/m", false, "NBR 6118, 17.2.2"));
            r.Verificacoes.Add(K("K2", "Flexão", "Ductilidade: posição da linha neutra", $"x/d = {N(f.XsobreD, "0.000")}", $"≤ {N(mat.XdLim)}", f.AsCompr > 0, "NBR 6118, 14.6.4.3",
                dica: "Aumente a espessura da laje."));
            r.Verificacoes.Add(K("K3", "Flexão", "Espaçamento da armadura principal", $"{N(melhor.Esp, "0.#")} cm", $"≤ 2h = {N(2 * hMin, "0")} e ≤ 20 cm", melhor.Esp > Math.Min(2 * hMin, 20) + 1e-9, "NBR 6118, 20.1"));
        }
        r.Memoria.Add(m4);

        // ---------------- 5. distribuição e negativas construtivas ----------------
        r.AsDistNec = Math.Max(Math.Max(0.2 * r.AsPrincipal, 0.9), 0.5 * rhoMin * b * hMin);
        r.Distribuicao = Escolher(BitolasDistribuicao, r.AsDistNec, 33);
        r.AsNegNec = 0.67 * rhoMin * b * hMin;
        r.Negativa = Escolher(BitolasPrincipal.Where(p => p <= hMin * 10 / 8 + 1e-9).ToArray(), r.AsNegNec, Math.Min(2 * hMin, 20));
        var m5 = new PassoMemoria { Titulo = "5. Distribuição e armadura negativa nos apoios (NBR 6118, 19.3.3.2 e Tabela 19.1)" };
        m5.Linhas.Add($"Distribuição: ≥ 20 % da principal, ≥ 0,9 cm²/m e ≥ 0,5·ρmín·b·h → {N(r.AsDistNec)} cm²/m; s ≤ 33 cm → ø{N(r.Distribuicao.Bitola, "0.#")} c/{N(r.Distribuicao.Esp, "0.#")}.");
        m5.Linhas.Add($"Negativa nos apoios (bordas sem continuidade, apoio que restringe a rotação parcialmente): ρs ≥ 0,67·ρmín → {N(r.AsNegNec)} cm²/m → ø{N(r.Negativa.Bitola, "0.#")} c/{N(r.Negativa.Esp, "0.#")}, até 0,15·ℓ do apoio. " +
                      "Distribui e controla a fissura que aparece no apoio quando a viga restringe o giro (apostila 7.3.2.3).");
        r.Memoria.Add(m5);

        // ---------------- 6. cisalhamento sem armadura transversal ----------------
        double dV = r.D > 0 ? r.D : DD(hMin, 10);
        double kk = Math.Max(1, 1.6 - dV / 100);
        double rho1 = Math.Min(0.02, r.AsPrincipal / (b * dV));
        double tauRd = 0.25 * fctd;
        double nApoio = r.Diagrama.Where(p => p.X <= L * 0.05 || p.X >= L * 0.95).Select(p => -p.Nd).DefaultIfEmpty(0).Min();   // compressão positiva; a menor (pior)
        double sigCp = nApoio / (b * Math.Min(h, hp));   // kN/cm²
        r.VRd1 = (tauRd * kk * (1.2 + 40 * rho1) + 0.15 * sigCp) * b * dV;
        var m6 = new PassoMemoria { Titulo = "6. Cisalhamento sem armadura transversal (NBR 6118, 19.4.1)" };
        m6.Linhas.Add($"τRd = 0,25·fctd = {N(tauRd * 10, "0.000")} MPa; k = 1,6 − d = {N(kk, "0.000")} (≥ 1); ρ1 = {N(rho1 * 100, "0.000")} %; σcp = {N(sigCp * 10, "0.000")} MPa (normal no apoio, tração negativa).");
        m6.Linhas.Add($"VRd1 = [τRd·k·(1,2 + 40ρ1) + 0,15σcp]·bw·d = {N(r.VRd1, "0.0")} kN/m ≥ Vd,máx = {N(r.VdMax, "0.0")} kN/m.");
        r.Memoria.Add(m6);
        r.Verificacoes.Add(K("K4", "Cisalhamento", "Força cortante sem estribos (VRd1)", $"Vd = {N(r.VdMax, "0.0")} kN/m", $"≤ VRd1 = {N(r.VRd1, "0.0")} kN/m", r.VdMax > r.VRd1 + 1e-9,
            "NBR 6118, 19.4.1", dica: "Aumente a espessura ou a armadura principal."));

        // ---------------- 7. ancoragens e a quina reentrante ----------------
        var rt = new RegrasTraspasse { Fck = e.Fck, GamaC = k.GamaC, GamaS = k.GamaS };
        double phiP = r.Principal.Bitola > 0 ? r.Principal.Bitola : 10;
        r.Lb = rt.Lb(phiP, e.Aco);
        r.LbNec = Math.Max(Math.Max(r.Lb * Math.Min(1, r.AsNec / Math.Max(1e-9, r.AsPrincipal)), 0.3 * r.Lb), Math.Max(10 * phiP / 10, 10));
        r.LbNec = Math.Ceiling(r.LbNec - 1e-9);
        double ancApoio = e.LarguraApoio - cob;
        var m7 = new PassoMemoria { Titulo = "7. Ancoragens (NBR 6118, 9.4.2) e quina reentrante (apostila 7.3.2.2, Det. 1)" };
        m7.Linhas.Add($"lb = (φ/4)·(fyd/fbd) = {N(r.Lb, "0.0")} cm (boa aderência); lb,nec = lb·As,calc/As,ef ≥ máx(0,3lb; 10φ; 10 cm) = {N(r.LbNec, "0")} cm.");
        if (g.QuinaReentrante.HasValue)
            m7.Linhas.Add("Quina reentrante (face inferior do lance com o patamar superior): as barras inferiores do lance e do patamar SE CRUZAM e seguem retas " +
                          $"até a face oposta, com lb,nec = {N(r.LbNec, "0")} cm depois do cruzamento. A barra dobrada acompanhando a quina seria arrancada pelo empuxo no vazio.");
        if (!e.PreMoldada) m7.Linhas.Add($"Nos apoios a armadura positiva entra {N(ancApoio, "0")} cm (largura do apoio − cobrimento); exigido ≥ 10φ = {N(phiP, "0")} cm.");
        r.Memoria.Add(m7);
        if (!e.PreMoldada) r.Verificacoes.Add(K("K5", "Ancoragem", "Armadura positiva dentro do apoio", $"{N(ancApoio, "0")} cm", $"≥ 10φ = {N(phiP, "0")} cm", ancApoio < phiP - 1e-9, "NBR 6118, 18.3.2.4.1",
            dica: "Use gancho na ponta ou um apoio mais largo."));

        // ---------------- 8. ELS: flecha e fissuras ----------------
        var m8 = new PassoMemoria { Titulo = "8. Estados-limite de serviço (NBR 6118, 17.3)" };
        double eci = e.Fck <= 50 ? k.AlfaE * 5600 * Math.Sqrt(e.Fck) : 21500 * k.AlfaE * Math.Pow(e.Fck / 10 + 1.25, 1.0 / 3);
        double ecs = Math.Min(1, 0.8 + 0.2 * e.Fck / 80) * eci, ecKn = ecs / 10, es = 210000;
        double Ic(double hh) => b * hh * hh * hh / 12;
        var qp = new EstaticaEscada(L, cg.Select((t, i) => (t.X0, t.X1, t.W + k.Psi2 * cq[i].W)).ToList());
        var fr = new EstaticaEscada(L, cg.Select((t, i) => (t.X0, t.X1, t.W + k.Psi1 * cq[i].W)).ToList());
        double ma = Enumerable.Range(0, 201).Max(i => qp.M(L * i / 200.0));   // kN·cm por metro de largura
        double asPr = Math.Max(r.AsPrincipal, 1e-6);
        (double X, double I) EstII(double ae, double dd)
        {
            double aa = ae * asPr, x = (-aa + Math.Sqrt(aa * aa + 2 * b * aa * dd)) / b;
            return (x, b * x * x * x / 3 + aa * (dd - x) * (dd - x));
        }
        double IeDe(double hh)
        {
            double ic = Ic(hh), mr = 1.5 * fctm * ic / (hh / 2), dd = DD(hh, phiP);
            var (_, i2) = EstII(es / ecs, dd);
            double rel = ma > 0 ? Math.Min(1, Math.Pow(mr / ma, 3)) : 1;
            return Math.Min(ic, rel * ic + (1 - rel) * i2);
        }
        double ieL = IeDe(h), ieP = IeDe(hp);
        // flecha imediata (Branson) e elástica (Ic) ao longo do eixo
        double EI(double x, bool fissurada) { var t = g.TrechoEm(x); return ecKn * (fissurada ? (t.Inclinado ? ieL : ieP) : Ic(t.H)); }
        double CosEm(double x) => g.TrechoEm(x).Inclinado ? c : 1;
        double FlechaEm(EstaticaEscada est, double xi, bool fiss)
        {
            int n = 400; double hs = L / n, sum = 0;
            for (int i = 0; i <= n; i++)
            {
                double x = i * hs, mi = x <= xi ? (1 - xi / L) * x : xi * (1 - x / L);
                double f = est.M(x) * mi / EI(x, fiss) / CosEm(x);   // M e EI da faixa de 1 m; m = momento da carga unitária (cm)
                sum += (i == 0 || i == n) ? f / 2 : f;
            }
            return sum * hs;
        }
        foreach (var p in r.Diagrama.Where((p, i) => i % 4 == 0 || p == r.Diagrama.Last()))
            p.FlechaElastica = FlechaEm(qp, p.X, false);
        // interpola nos outros pontos
        var comFlecha = r.Diagrama.Where((p, i) => i % 4 == 0 || p == r.Diagrama.Last()).ToList();
        foreach (var p in r.Diagrama)
        {
            var a0 = comFlecha.LastOrDefault(q => q.X <= p.X) ?? comFlecha[0];
            var a1 = comFlecha.FirstOrDefault(q => q.X >= p.X) ?? comFlecha.Last();
            if (a1.X - a0.X > 1e-9) p.FlechaElastica = a0.FlechaElastica + (a1.FlechaElastica - a0.FlechaElastica) * (p.X - a0.X) / (a1.X - a0.X);
        }
        r.FlechaElasticaMax = r.Diagrama.Max(p => p.FlechaElastica);
        double xFl = r.Diagrama.OrderByDescending(p => p.FlechaElastica).First().X;
        r.FlechaImediata = FlechaEm(qp, xFl, true);
        double Xi(double t) => t > 70 ? 2 : 0.68 * Math.Pow(0.996, t) * Math.Pow(t, 0.32);
        double af = 2 - Xi(k.IdadeCarregamentoMeses);
        r.FlechaTotal = r.FlechaImediata * (1 + af);
        double lim = L / k.FlechaLimite;
        m8.Linhas.Add($"Ecs = {N(ecs, "0")} MPa; quase permanente (ψ2 = {N(k.Psi2)}): Ma = {N(ma / 100, "0.00")} kN·m/m; Branson: Ie = {N(ieL, "0")} cm⁴/m (lance), {N(ieP, "0")} cm⁴/m (patamar).");
        m8.Linhas.Add($"Flecha pelos trabalhos virtuais no eixo quebrado (ds = dx/cosα no lance): elástica (Ic) {N(r.FlechaElasticaMax, "0.000")} cm; imediata (Ie) {N(r.FlechaImediata, "0.000")} cm em x = {N(xFl, "0")} cm.");
        m8.Linhas.Add($"Fluência: αf = Δξ = {N(af, "0.00")} (ρ' = 0) → total = {N(r.FlechaTotal, "0.00")} cm; limite ℓ/{N(k.FlechaLimite, "0")} = {N(lim, "0.00")} cm (ℓ = vão horizontal).");
        r.Verificacoes.Add(K("K6", "Serviço", "Flecha total (imediata + diferida)", $"{N(r.FlechaTotal, "0.00")} cm", $"≤ ℓ/{N(k.FlechaLimite, "0")} = {N(lim, "0.00")} cm", r.FlechaTotal > lim + 1e-9,
            "NBR 6118, 17.3.2.1 e 13.3", dica: "Aumente a espessura da laje."));
        // fissuras (frequente) no ponto de máximo
        double mf = Enumerable.Range(0, 201).Max(i => fr.M(L * i / 200.0));
        double hF = g.TrechoEm(r.XMdMax).H, dF = DD(hF, phiP), mrF = 1.5 * fctm * Ic(hF) / (hF / 2);
        double wLim = TabelasNbr.Classe(e.ClasseAgressividade) switch { ClasseAgressividade.I => 0.4, ClasseAgressividade.IV => 0.2, _ => 0.3 };
        if (mf <= mrF || r.Principal.Esp <= 0)
        {
            r.Wk = 0;
            m8.Linhas.Add($"Frequente (ψ1 = {N(k.Psi1)}): M = {N(mf / 100, "0.00")} kN·m/m ≤ Mr = {N(mrF / 100, "0.00")}: não fissura.");
        }
        else
        {
            var (xf, ifs) = EstII(15, dF);
            double sig = 15 * mf * (dF - xf) / ifs * 10;
            double pc = phiP / 10, acr = r.Principal.Esp * Math.Min(cob + pc / 2 + 7.5 * pc, hF / 2), rr = A(phiP) / acr;
            double w1 = phiP / (12.5 * 2.25) * sig / es * 3 * sig / (fctm * 10), w2 = phiP / (12.5 * 2.25) * sig / es * (4 / rr + 45);
            r.Wk = Math.Min(w1, w2);
            m8.Linhas.Add($"Frequente (ψ1 = {N(k.Psi1)}): M = {N(mf / 100, "0.00")} kN·m/m; σs = {N(sig, "0")} MPa; wk = mín({N(w1, "0.000")}; {N(w2, "0.000")}) = {N(r.Wk, "0.000")} mm.");
        }
        r.Verificacoes.Add(K("K7", "Serviço", "Abertura de fissuras (combinação frequente)", $"wk = {N(r.Wk, "0.00")} mm", $"≤ {N(wLim, "0.0")} mm", r.Wk > wLim + 1e-9, "NBR 6118, 17.3.3.2",
            dica: "Use bitola menor com espaçamento menor."));
        r.Memoria.Add(m8);

        // ---------------- 9. barras na elevação ----------------
        if (e.PreMoldada) CalcularPreMoldada(r, k);
        else if (r.Principal.Esp > 0) GerarBarras(r);
        return r;
    }

    /// <summary>Menor área (cm²/m) que atende, com espaçamento múltiplo de 2,5 cm entre 7,5 e o máximo; empate = espaçamento maior.</summary>
    public static (double Bitola, double Esp) Escolher(IEnumerable<double> bitolas, double asNec, double sMax)
    {
        (double, double) m = (0, 0); double ma = double.MaxValue;
        foreach (double phi in bitolas)
        {
            double esp = Math.Min(sMax, Math.Floor(A(phi) * 100 / Math.Max(1e-9, asNec) / 2.5 + 1e-9) * 2.5);
            esp = Math.Floor(esp / 2.5 + 1e-9) * 2.5;
            if (esp < 7.5 - 1e-9) continue;
            double a = A(phi) * 100 / esp;
            if (a < ma - 1e-6 || (Math.Abs(a - ma) < 1e-6 && esp > m.Item2)) { ma = a; m = (phi, esp); }
        }
        return m;
    }

    // ------------------------------------------------------------------ barras

    /// <summary>Gera as barras na elevação: N1 positiva do lance, N2 positiva do patamar superior (cruzada com a N1 na quina
    /// reentrante), N3 distribuição, N4/N5 negativas nos apoios.</summary>
    public static void GerarBarras(ResultadoEscada r)
    {
        var e = r.Entrada; var g = r.Geo;
        if (e.SoPatamar) { GerarBarrasPatamar(r); return; }
        double cob = e.Cobrimento, h = g.H, hp = g.Hp, L = g.Vao, H = e.Desnivel;
        double c = g.Cos, s = g.Sen, tg = Math.Tan(g.Alfa);
        double phi = r.Principal.Bitola, co = cob + phi / 20;   // eixo da barra a partir da face
        bool pm = e.PreMoldada;
        // no local as positivas entram no apoio; na pré-moldada ficam na peça (depois do recorte do dente), com gancho para cima
        double xIni = pm ? g.XRecorteInf + cob + phi / 20 : -(e.LarguraApoio / 2 - cob), xFim = pm ? g.XRecorteSup - cob - phi / 20 : L + (e.LarguraApoio / 2 - cob);
        bool pi = g.X1 > 1e-9, ps = L - g.X2 > 1e-9;
        double FundoLance(double x) => (x - g.X1) * tg - h / c + co / c;   // eixo da barra inferior no lance
        double XFundoLance(double z) => g.X1 + (z + h / c - co / c) / tg;
        double XTopoLance(double z) => g.X1 + (z + co / c) / tg;
        int nLarg = QtReta(e.Largura - 2 * cob - phi / 10, r.Principal.Esp);

        // N1: positiva do patamar inferior + lance, cruzando na quina reentrante
        var n1 = new List<(double X, double Z)>();
        if (pm) n1.Add((xIni, -co));
        if (pi) { n1.Add((xIni, -hp + co)); n1.Add((XFundoLance(-hp + co), -hp + co)); }
        else n1.Add((xIni, FundoLance(xIni)));
        if (ps)
        {
            double zc = H - hp + co, xc = XFundoLance(zc);   // cruzamento com a inferior do patamar superior
            double zt = H - co, xt = XFundoLance(zt);
            double reto = (xt - xc) / c;
            if (reto >= r.LbNec) n1.Add((xc + r.LbNec * c, zc + r.LbNec * s));
            else { n1.Add((xt, zt)); n1.Add((xt + (r.LbNec - reto), zt)); }
        }
        else n1.Add((xFim, FundoLance(xFim)));
        r.Barras.Add(Barra("N1", "positiva do lance" + (pi ? " e do patamar inferior" : ""), phi, r.Principal.Esp, nLarg, n1));

        // N2: positiva do patamar superior, entra no lance e cruza a N1
        if (ps)
        {
            double zb = H - hp + co, xc = XFundoLance(zb), xe = XTopoLance(zb);
            var n2 = new List<(double X, double Z)>();
            if (pm) n2.Add((xFim, H - co));
            n2.Add((xFim, zb));
            double reto = xc - xe;
            if (reto >= r.LbNec) n2.Add((xc - r.LbNec, zb));
            else { n2.Add((xe, zb)); double rest = r.LbNec - reto; n2.Add((xe - rest * c, zb - rest * s)); }
            r.Barras.Add(Barra("N2", "positiva do patamar superior (cruza a N1 na quina)", phi, r.Principal.Esp, nLarg, n2));
        }

        // N3: distribuição, sobre as positivas
        var d = r.Distribuicao;
        if (d.Esp > 0)
        {
            double off = phi / 10 + d.Bitola / 20;   // logo acima da principal
            // ao longo da N1 até o cruzamento da quina; com patamar superior, outra linha sobre a N2 (do apoio até o cruzamento)
            var linha = new List<(double X, double Z)>();
            double zc = H - hp + co, xc = XFundoLance(zc);
            foreach (var p in pm ? n1.Skip(1) : n1)
            {
                if (ps && p.X > xc + 1e-6) { linha.Add((xc, zc)); break; }
                linha.Add(p);
            }
            var l3 = linha.Select(p => (p.X, p.Z + off)).ToList();
            r.Barras.Add(new BarraEscada
            {
                Nome = "N3", Funcao = "distribuição", Bitola = d.Bitola, Espacamento = d.Esp, Transversal = true,
                Quantidade = QtLinha(l3, d.Esp), Pontos = l3, Comprimento = e.Largura - 2 * cob,
            });
            if (ps)
            {
                var l3b = new List<(double X, double Z)> { (xFim, zc + off), (xc - d.Esp / 2, zc + off) };
                r.Barras.Add(new BarraEscada
                {
                    Nome = "N3", Funcao = "distribuição (patamar superior)", Bitola = d.Bitola, Espacamento = d.Esp, Transversal = true,
                    Quantidade = QtLinha(l3b, d.Esp), Pontos = l3b, Comprimento = e.Largura - 2 * cob,
                });
            }
        }

        // N4 / N5: negativas construtivas nos apoios, até 0,15·ℓ, com gancho dentro do apoio (só no local; a pré-moldada tem as suas)
        var ng = r.Negativa;
        if (ng.Esp > 0 && !pm)
        {
            double con = cob + ng.Bitola / 20, comp = 0.15 * L;
            int nL = QtReta(e.Largura - 2 * cob - ng.Bitola / 10, ng.Esp);
            var n4 = new List<(double X, double Z)>();
            if (pi)
            {
                double xf = Math.Min(comp, g.X1 - con);
                n4.Add((xIni, -hp + con)); n4.Add((xIni, -con)); n4.Add((Math.Max(xIni + 10, xf), -con));
            }
            else
            {
                double zt0 = (xIni - g.X1) * tg - con / c;
                n4.Add((xIni, zt0 - (h - 2 * con) / c)); n4.Add((xIni, zt0)); n4.Add((comp, (comp - g.X1) * tg - con / c));
            }
            r.Barras.Add(Barra("N4", "negativa no apoio inferior", ng.Bitola, ng.Esp, nL, n4));
            var n5 = new List<(double X, double Z)>();
            if (ps)
            {
                double xf = Math.Max(L - comp, g.X2 + con);
                n5.Add((xFim, H - hp + con)); n5.Add((xFim, H - con)); n5.Add((Math.Min(xFim - 10, xf), H - con));
            }
            else
            {
                double zt1 = (xFim - g.X1) * tg - con / c;
                n5.Add((xFim, zt1 - (h - 2 * con) / c)); n5.Add((xFim, zt1)); n5.Add((L - comp, (L - comp - g.X1) * tg - con / c));
            }
            r.Barras.Add(Barra("N5", "negativa no apoio superior", ng.Bitola, ng.Esp, nL, n5));
        }
    }

    /// <summary>Patamar plano: N1 positiva reta (com gancho para cima na pré-moldada, entrando no apoio no local), N3 distribuição
    /// e, no local, negativas N4/N5 nos apoios.</summary>
    private static void GerarBarrasPatamar(ResultadoEscada r)
    {
        var e = r.Entrada; var g = r.Geo;
        double cob = e.Cobrimento, hp = g.Hp, L = g.Vao, phi = r.Principal.Bitola, co = cob + phi / 20;
        bool pm = e.PreMoldada;
        double xIni = pm ? g.XRecorteInf + co : -(e.LarguraApoio / 2 - cob), xFim = pm ? g.XRecorteSup - co : L + (e.LarguraApoio / 2 - cob);
        int nLarg = QtReta(e.Largura - 2 * cob - phi / 10, r.Principal.Esp);
        var n1 = new List<(double X, double Z)> { (xIni, -hp + co), (xFim, -hp + co) };
        if (pm)
        {
            n1.Insert(0, (xIni, -co));
            n1.Add((xFim, -co));
            n1 = SemGanchoCurto(n1, phi);
        }
        r.Barras.Add(Barra("N1", "positiva do patamar", phi, r.Principal.Esp, nLarg, n1));
        var d = r.Distribuicao;
        if (d.Esp > 0)
        {
            double off = phi / 10 + d.Bitola / 20;
            var l3 = new List<(double X, double Z)> { (xIni, -hp + co + off), (xFim, -hp + co + off) };
            r.Barras.Add(new BarraEscada
            {
                Nome = "N3", Funcao = "distribuição", Bitola = d.Bitola, Espacamento = d.Esp, Transversal = true,
                Quantidade = QtLinha(l3, d.Esp), Pontos = l3, Comprimento = e.Largura - 2 * cob,
            });
        }
        var ng = r.Negativa;
        if (ng.Esp > 0 && !pm)
        {
            double con = cob + ng.Bitola / 20, comp = 0.15 * L;
            int nL = QtReta(e.Largura - 2 * cob - ng.Bitola / 10, ng.Esp);
            r.Barras.Add(Barra("N4", "negativa no apoio inferior", ng.Bitola, ng.Esp, nL, new List<(double X, double Z)> { (xIni, -hp + con), (xIni, -con), (comp, -con) }));
            r.Barras.Add(Barra("N5", "negativa no apoio superior", ng.Bitola, ng.Esp, nL, new List<(double X, double Z)> { (xFim, -hp + con), (xFim, -con), (L - comp, -con) }));
        }
    }

    /// <summary>Barras num comprimento com espaçamento máximo (as duas pontas incluídas): o mesmo número que o Revit cria.</summary>
    public static int QtReta(double comp, double esp) => comp <= 1e-9 ? 1 : (int)Math.Ceiling(comp / esp - 1e-9) + 1;

    /// <summary>Barras transversais ao longo de uma polilinha, trecho a trecho (como o Revit cria: o 1º trecho com a barra inicial;
    /// trecho com menos de meio espaçamento fica sem barra).</summary>
    public static int QtLinha(IReadOnlyList<(double X, double Z)> p, double esp)
    {
        if (p.Count == 2) return QtReta(Comprimento(p), esp);
        int n = 0; bool prim = true;
        for (int i = 1; i < p.Count; i++)
        {
            double l = Math.Sqrt(Math.Pow(p[i].X - p[i - 1].X, 2) + Math.Pow(p[i].Z - p[i - 1].Z, 2));
            if (l < esp / 2) continue;
            n += (int)Math.Ceiling(l / esp - 1e-9) + (prim ? 1 : 0);
            prim = false;
        }
        return Math.Max(1, n);
    }

    private static BarraEscada Barra(string nome, string funcao, double phi, double esp, int n, List<(double X, double Z)> pts) => new BarraEscada
    {
        Nome = nome, Funcao = funcao, Bitola = phi, Espacamento = esp, Quantidade = n, Pontos = pts, Comprimento = Math.Ceiling(Comprimento(pts)),
    };

    public static double Comprimento(IReadOnlyList<(double X, double Z)> p)
    {
        double s = 0;
        for (int i = 1; i < p.Count; i++) s += Math.Sqrt(Math.Pow(p[i].X - p[i - 1].X, 2) + Math.Pow(p[i].Z - p[i - 1].Z, 2));
        return s;
    }

    private static ResultadoVerificacao K(string cod, string grupo, string desc, string valor, string limite, bool falha, string refer,
        SituacaoVerificacao seFalha = SituacaoVerificacao.NaoAtende, string dica = "") => new ResultadoVerificacao
    {
        Codigo = cod, Grupo = "Escada — " + grupo, Descricao = desc, Valor = valor, Limite = limite, Referencia = refer,
        Situacao = falha ? seFalha : SituacaoVerificacao.Ok, Dica = falha ? dica : "",
    };
}

/// <summary>
/// Estática da viga biapoiada (apoios em x = 0 e x = L) com cargas distribuídas por trechos da projeção horizontal
/// (kN/cm por cm de largura... ou seja, a unidade que entrar). Momento em kN·cm (×unidade), cortante vertical Vh.
/// </summary>
public sealed class EstaticaEscada
{
    public double L { get; }
    public IReadOnlyList<(double X0, double X1, double W)> Cargas { get; }
    public double Ra { get; }
    public double Rb { get; }

    public EstaticaEscada(double L, IReadOnlyList<(double X0, double X1, double W)> cargas)
    {
        if (L < 1) throw new ArgumentException("Vão nulo.");
        this.L = L; Cargas = cargas;
        double tot = cargas.Sum(t => t.W * (t.X1 - t.X0));
        Rb = cargas.Sum(t => t.W * (t.X1 - t.X0) * (t.X0 + t.X1) / 2) / L;
        Ra = tot - Rb;
    }

    /// <summary>Resultante vertical das forças à esquerda de x (para cima positivo).</summary>
    public double Vh(double x) => Ra - Cargas.Sum(t => t.W * Math.Max(0, Math.Min(x, t.X1) - t.X0));

    public double M(double x)
    {
        double m = Ra * x;
        foreach (var t in Cargas)
        {
            double a = Math.Max(0, Math.Min(x, t.X1) - t.X0);
            if (a <= 0) continue;
            m -= t.W * a * (x - (t.X0 + a / 2));
        }
        return m;
    }
}
