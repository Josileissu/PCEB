using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Documentacao;
using DEB.Core.Motor;
using DEB.Core.Normas.Verificacao;

namespace DEB.Core.Elementos.Viga.PreMoldada.Calculo;


/// <summary>Arranjo proposto para a armadura inferior: bitola e barras na 1ª e na 2ª camada.</summary>
public sealed class ArranjoInferior
{
    public double Bitola { get; set; }
    public int N1 { get; set; }
    public int N2 { get; set; }
    public int N => N1 + N2;
    public double Area => N * Math.PI * Bitola * Bitola / 400;
    /// <summary>Altura útil (cm) do arranjo: até o centro de gravidade das barras.</summary>
    public double D { get; set; }
    public string Texto => N2 > 0 ? $"{N1} + {N2} ø{Fmt.Virgula(Bitola, "0.#")} (2 camadas)" : $"{N1} ø{Fmt.Virgula(Bitola, "0.#")}";
}

/// <summary>Resultado do cálculo de uma viga (ou da envoltória de um grupo de vigas iguais).</summary>
public sealed class ResultadoCalculoViga
{
    // geometria e cargas
    public double L { get; set; }
    public double ApoioE { get; set; }
    public double ApoioD { get; set; }
    public double Vao => L - ApoioE - ApoioD;
    public double B { get; set; }
    public double H { get; set; }
    public double Capa { get; set; }
    /// <summary>Peso próprio da peça (kN/m, distribuído no comprimento) e da capa sobre a viga.</summary>
    public double G1 { get; set; }
    public double GCapa { get; set; }
    public CargasViga Cargas { get; set; } = new CargasViga();
    public CoeficientesViga Coef { get; set; } = new CoeficientesViga();

    // esforços de cálculo
    public double MdMax { get; set; }        // kN·m
    public double XMdMax { get; set; }       // cm
    /// <summary>Momento negativo de cálculo (kN·m, negativo; 0 = sem negativo) da envoltória da análise (ligação semirrígida, D51).</summary>
    public double MdMin { get; set; }
    /// <summary>Envoltória do momento negativo: x (cm), Md− (kN·m).</summary>
    public List<(double X, double Md)> DiagramaNegativo { get; set; } = new List<(double, double)>();
    /// <summary>Armadura negativa proposta em cada ponta (D51): barras, bitola, trecho (cm da ponta esquerda) e área necessária (cm²).</summary>
    public List<(string Lado, int N, double Bitola, double Ini, double Fim, double AsNec, int Camada)> Negativas { get; } = new List<(string, int, double, double, double, double, int)>();
    /// <summary>Dentes calculados (D51, parte 3): tipos NOVOS para o catálogo ("DG 75x25 – V101") e o tipo de cada ponta na proposta.</summary>
    public List<TipoDente> DentesPropostos { get; } = new List<TipoDente>();
    public string DenteE { get; set; }
    public string DenteD { get; set; }

    /// <summary>Catálogo com os dentes calculados acrescentados (o template acha o tipo da proposta pelo nome); o original não muda.</summary>
    public List<TipoDente> CatalogoComPropostos(IEnumerable<TipoDente> catalogo) =>
        catalogo.Where(t => DentesPropostos.All(n => n.Nome != t.Nome)).Concat(DentesPropostos).ToList();

    /// <summary>Esforços da análise da viga (modelos rotulado/semirrígido e envoltória) no lugar da biapoiada; null = biapoiada.</summary>
    public string ModelosAnalise { get; set; }
    public double VdE { get; set; }          // kN (junto ao apoio)
    public double VdD { get; set; }
    public double RkE_G { get; set; }        // reações características (kN)
    public double RkE_Q { get; set; }
    public double RkD_G { get; set; }
    public double RkD_Q { get; set; }
    /// <summary>Diagrama de cálculo no vão: x (cm), Md (kN·m), Vd (kN).</summary>
    public List<(double X, double Md, double Vd)> Diagrama { get; set; } = new List<(double, double, double)>();

    // armaduras
    public double AsNec { get; set; }
    public double AsMin { get; set; }
    public double AsComprNec { get; set; }
    public double AsSupIcamento { get; set; }
    public ArranjoInferior Inferior { get; set; }
    public (int N, double Bitola) Superior { get; set; }
    public (int NPorFace, double Bitola) Pele { get; set; }
    public double EstBitola { get; set; }
    public double EspPonta { get; set; }
    public double EspVao { get; set; }
    public double ZonaPonta { get; set; }
    public double VRd2 { get; set; }
    public double Vc { get; set; }
    public double VdSobreVRd2 => VRd2 > 0 ? Math.Max(VdE, VdD) / VRd2 : 0;
    /// <summary>Momento resistente de cálculo da armadura inferior proposta (kN·m), com a altura útil dela.</summary>
    public double MRd { get; set; }
    /// <summary>Decalagem do diagrama de Md (cm), modelo I: aℓ = d·Vd,máx/(2·(Vd,máx − Vc)), entre 0,5d e d (NBR 6118, 17.4.2.2 c).</summary>
    public double Al { get; set; }
    /// <summary>Cortante resistente com os estribos propostos (kN): VRd3 = Vc + Vsw, nas pontas e no vão.</summary>
    public double VRd3Ponta { get; set; }
    public double VRd3Vao { get; set; }
    /// <summary>Fim da zona das pontas (x, cm da ponta esquerda): à esquerda até <see cref="XZonaE"/>, à direita a partir de <see cref="XZonaD"/>.</summary>
    public double XZonaE { get; set; }
    public double XZonaD { get; set; }

    // torção (NBR 6118, 17.5.1 e 17.7.2; θ = 45°, o mesmo do modelo I do cortante)
    /// <summary>Momento de torção de cálculo junto aos apoios (kN·m); 0 = sem torção (sem carga na aba).</summary>
    public double TdE { get; set; }
    public double TdD { get; set; }
    public double TdMax => Math.Max(TdE, TdD);
    /// <summary>Seção vazada equivalente da alma: espessura he, área Ae e perímetro ue (cm, cm², cm).</summary>
    public double He { get; set; }
    public double Ae { get; set; }
    public double Ue { get; set; }
    /// <summary>Torção máxima resistida pelas bielas (kN·m).</summary>
    public double TRd2 { get; set; }
    /// <summary>Estribo de torção por ramo (cm²/m) e armadura longitudinal de torção por metro de perímetro (cm²/m), no apoio.</summary>
    public double As90 { get; set; }
    public double AslPorM { get; set; }
    /// <summary>Diagrama do momento de torção de cálculo: x (cm), Td (kN·m).</summary>
    public List<(double X, double Td)> DiagramaTorcao { get; set; } = new List<(double, double)>();

    // aba de laje como consolo corrido (D51, parte 2)
    /// <summary>Força de cálculo na aba por metro (kN/m, por lado), a/d, tirante (cm²/m), estribo da aba proposto e suspensão na alma (cm²/m).</summary>
    public double FdAba { get; set; }
    public double AdAba { get; set; }
    public double AsTirAba { get; set; }
    public double AbaEstBitola { get; set; }
    public double AbaEstEsp { get; set; }
    public double AsSuspAba { get; set; }

    // ELS
    public double FlechaImediata { get; set; }
    public double FlechaTotal { get; set; }
    public double Contraflecha { get; set; }
    public double Wk { get; set; }

    public ListaVerificacoes Verificacoes { get; } = new ListaVerificacoes();
    public List<PassoMemoria> Memoria { get; } = new List<PassoMemoria>();
    public List<string> Avisos { get; } = new List<string>();
    /// <summary>Valores do template com a armadura proposta (o detalhamento sai deles, no CAD e no Revit).</summary>
    public ValoresTemplate Proposta { get; set; }

    /// <summary>Resumo da armadura proposta, uma linha por item (janela do Revit e relatório).</summary>
    public List<string> ResumoProposta()
    {
        string Nm(double x, string f = "0.#") => Fmt.Virgula(x, f);
        var l = new List<string>();
        if (Inferior != null) l.Add($"Inferior: {Inferior.Texto} ({Nm(Inferior.Area, "0.00")} cm² ≥ {Nm(Math.Max(AsNec, AsMin), "0.00")} cm²)");
        if (Superior.N > 0 && Capa <= 0) l.Add($"Superior: {Superior.N} ø{Nm(Superior.Bitola)}");
        foreach (var ng in Negativas) l.Add($"Negativa ({ng.Lado}): {ng.N} ø{Nm(ng.Bitola)}, de {Nm(ng.Ini, "0")} a {Nm(ng.Fim, "0")} cm");
        if (Pele.NPorFace > 0) l.Add($"Pele: {Pele.NPorFace} ø{Nm(Pele.Bitola)} por face");
        if (EstBitola > 0)
            l.Add(ZonaPonta > 0 ? $"Estribos: ø{Nm(EstBitola)} c/{Nm(EspPonta)} nas pontas ({Nm(ZonaPonta, "0")} cm) e c/{Nm(EspVao)} no vão"
                                : $"Estribos: ø{Nm(EstBitola)} c/{Nm(EspVao)}");
        if (AbaEstBitola > 0) l.Add($"Estribo da aba: ø{Nm(AbaEstBitola)} c/{Nm(AbaEstEsp)}");
        foreach (var t in DentesPropostos)
            l.Add($"Dente {t.Nome}: tirante {t.TiranteQtd} ø{Nm(t.TiranteBitola)} C={Nm(t.TiranteComp, "0")}; suspensão {t.SuspQtd} ø{Nm(t.SuspBitola)}; " +
                  $"costura {t.CosturaQtd} ø{Nm(t.CosturaBitola)} (pernas {Nm(t.CosturaPerna, "0")}); estribos do dente {t.EstGerberQtd} ø{Nm(t.EstGerberBitola)}; grampo {t.GrampoQtd} ø{Nm(t.GrampoBitola)} (pernas {Nm(t.GrampoPerna, "0")})");
        if (Contraflecha > 0) l.Add($"Contraflecha: {Nm(Contraflecha)} cm");
        return l;
    }

    /// <summary>Leva o cálculo para o relatório do grupo (D37): cargas e proposta nos dados, verificações K, memória e diagramas.</summary>
    public void PreencherRelatorio(RelatorioGrupo g)
    {
        string Nm(double x, string f = "0.##") => Fmt.Virgula(x, f);
        void D(string k, string v) => g.Dados.Add(new KeyValuePair<string, string>(k, v));
        D("Vão de cálculo", $"{Nm(Vao, "0.#")} cm (apoios a {Nm(ApoioE, "0.#")} e {Nm(ApoioD, "0.#")} cm das pontas)");
        D("Cargas (características)", $"g1 = {Nm(G1)} kN/m (peso próprio)" + (GCapa > 0 ? $"; capa {Nm(GCapa)} kN/m" : "") +
                                      $"; g2 = {Nm(Cargas.G)} kN/m; q = {Nm(Cargas.Q)} kN/m" +
                                      (Cargas.Concentradas.Count > 0 ? $"; concentradas: {CargasViga.TextoConcentradas(Cargas.Concentradas)}" : ""));
        if (Cargas.GAba > 0 || Cargas.QAba > 0)
            D("Cargas na aba (por lado)", $"g = {Nm(Cargas.GAba)} kN/m; q = {Nm(Cargas.QAba)} kN/m");
        D("Esforços de cálculo", $"Md,máx = {Nm(MdMax, "0.0")} kN·m; Vd = {Nm(VdE, "0.0")} / {Nm(VdD, "0.0")} kN (apoios esquerdo / direito)" +
                                 (TdMax > 0 ? $"; Td = {Nm(TdE, "0.0")} / {Nm(TdD, "0.0")} kN·m" : ""));
        D("Reações características", $"esquerda G {Nm(RkE_G, "0.0")} + Q {Nm(RkE_Q, "0.0")} kN; direita G {Nm(RkD_G, "0.0")} + Q {Nm(RkD_Q, "0.0")} kN");
        D("Armadura proposta", string.Join("; ", ResumoProposta()));
        g.Verificacoes.Juntar(Verificacoes);
        g.Memoria.AddRange(Memoria);
        var vao = Diagrama.Where(p => p.X >= ApoioE - 1e-6 && p.X <= L - ApoioD + 1e-6).ToList();
        g.Diagramas.Add(new DiagramaRelatorio
        {
            Titulo = "Momento fletor de cálculo Md (kN·m)", Unidade = "kN·m", PositivoParaBaixo = true,
            Pontos = vao.Select(p => (p.X, p.Md)).ToList(), Apoios = { ApoioE, L - ApoioD },
        });
        g.Diagramas.Add(new DiagramaRelatorio
        {
            Titulo = "Força cortante de cálculo Vd (kN)", Unidade = "kN",
            Pontos = vao.Select(p => (p.X, p.Vd)).ToList(), Apoios = { ApoioE, L - ApoioD },
        });
        if (TdMax > 0)
            g.Diagramas.Add(new DiagramaRelatorio
            {
                Titulo = "Momento de torção de cálculo Td (kN·m)", Unidade = "kN·m",
                Pontos = DiagramaTorcao.ToList(), Apoios = { ApoioE, L - ApoioD },
            });
    }
}

/// <summary>
/// Cálculo da viga pré-moldada de concreto armado biapoiada (D40, 22/11/2026), sem protensão: esforços pelas cargas digitadas,
/// ELU de flexão (NBR 6118 17.2) e de cisalhamento (modelo I, 17.4.2.2), armadura mínima (17.3.5.2), pele (17.3.5.2.3), ancoragem no
/// apoio (18.3.2.4), dente Gerber (NBR 9062 7.4, tirante pelo consolo 7.3 e suspensão), içamento (momento negativo nas alças), ELS de
/// flecha (17.3.2.1, Branson + fluência) e de abertura de fissuras (17.3.3.2). Devolve a armadura PROPOSTA nos valores do template
/// (longitudinais, estribos e zonas), a memória de cálculo e as verificações (códigos K) para o relatório (D37).
/// O padrão do escritório (valores de partida) é o mínimo: a proposta não afrouxa espaçamentos nem tira barras do padrão.
/// </summary>
public static class CalculoViga
{
    public static readonly double[] BitolasLong = { 10, 12.5, 16, 20, 25, 32 };
    public static readonly double[] BitolasEst = { 5, 6.3, 8, 10, 12.5 };
    public static readonly double[] BitolasPele = { 8, 10, 12.5 };

    private static string N(double x, string f = "0.##") => Fmt.Virgula(x, f);
    private static double A(double phiMm) => Math.PI * phiMm * phiMm / 400;   // cm²

    /// <summary>Distância da ponta ao eixo do apoio (cm) quando não foi digitada: o furo do pino; sem furo, o meio do dente; ponta reta, 10 cm.</summary>
    public static double ApoioAutomatico(ExtremidadeViga x) =>
        x.Furos > 0 && x.DistFuro > 0 ? x.DistFuro : x.TemDente ? x.CompDente / 2 : 10;

    /// <param name="analise">Análise da viga (D51, CAD): a envoltória dos modelos rotulado e semirrígido dá Md (positivo e negativo), Vd e
    /// as reações; os apoios são os dela. Null = biapoiada pelas cargas (o cálculo do Revit, D40).</param>
    public static ResultadoCalculoViga Calcular(ValoresTemplate v, CargasViga cargas, CoeficientesViga k = null, IReadOnlyList<TipoDente> catalogo = null,
        ResultadoAnaliseViga analise = null)
    {
        k ??= new CoeficientesViga();
        cargas ??= new CargasViga();
        catalogo ??= TemplateVigaPreMoldada.CatalogoDentes;
        var r = new ResultadoCalculoViga { Cargas = cargas, Coef = k };
        var avisosLeitura = new List<string>();
        double b = v.Numero("B"), h = v.Numero("H"), L = v.Numero("L");
        var e = TemplateVigaPreMoldada.LerExtremidade(v, true, catalogo, avisosLeitura);
        var d = TemplateVigaPreMoldada.LerExtremidade(v, false, catalogo, avisosLeitura);
        var aba = TemplateVigaPreMoldada.LerAba(v, h, L, e, d, avisosLeitura);
        var ress = TemplateVigaPreMoldada.LerRessalto(v, b, h, avisosLeitura);
        double capa = TemplateVigaPreMoldada.Capa(v);
        r.L = L; r.B = b; r.H = h; r.Capa = capa;

        double fck = v.Numero("Fck"), fcd = fck / k.GamaC / 10;                 // kN/cm²
        string acoL = string.IsNullOrWhiteSpace(v["LongAco"]) ? "CA-50" : v["LongAco"], acoT = string.IsNullOrWhiteSpace(v["EstAco"]) ? "CA-50" : v["EstAco"];
        double fyd = TabelasNbr.Fyk(acoL) / k.GamaS / 10, fywk = TabelasNbr.Fyk(acoT), fywd = Math.Min(fywk / k.GamaS, 435) / 10;
        double fctm = TabelasNbr.Fctm(fck) / 10, fctd = 0.7 * fctm / k.GamaC;   // kN/cm²
        double cobF = v.Numero("CobFundo"), cobT = v.Numero("CobTopo"), cobL = v.Numero("CobLateral"), cobP = v.Numero("CobPontas");
        double gc = v.Numero("PesoEspecifico") > 0 ? v.Numero("PesoEspecifico") / 100 : 25;   // kgf/m³ → kN/m³ (1 kgf ≈ 1 daN)
        double dagg = v.Numero("DiamAgregado") > 0 ? v.Numero("DiamAgregado") : 19;
        var padrao = TemplateVigaPreMoldada.LerLongitudinais(v, L, cobP, e, d, new List<string>());
        double phiT0 = v.Numero("EstBitola") > 0 ? v.Numero("EstBitola") : 6.3;

        // ---------------- 1. geometria, apoios e cargas ----------------
        r.ApoioE = analise != null ? analise.ApoioE : cargas.ApoioE > 0 ? cargas.ApoioE : ApoioAutomatico(e);
        r.ApoioD = analise != null ? analise.ApoioD : cargas.ApoioD > 0 ? cargas.ApoioD : ApoioAutomatico(d);
        double vol = TemplateVigaPreMoldada.Volume(b, h, L, e, d) + aba.VolumeCm3 / 1e6 + ress.VolumeCm3(L) / 1e6;   // m³
        r.G1 = vol * gc / (L / 100);
        r.GCapa = capa > 0 ? b * capa / 1e4 * gc : 0;
        // carga da laje na aba (por lado): entra nas verticais como distribuída no comprimento todo (a favor da segurança quando a
        // aba é parcial) e gera a torção do passo 10
        int ladosAba = aba.Existe ? aba.Lados : 0;
        if (!aba.Existe && (cargas.GAba > 0 || cargas.QAba > 0))
            r.Avisos.Add("Há carga na aba, mas a viga não tem aba no template: a carga entrou como vertical no eixo, sem torção — conferir.");
        double nAba = aba.Existe ? ladosAba : 1;
        double gTot = r.G1 + r.GCapa + cargas.G + nAba * cargas.GAba, qTot = cargas.Q + nAba * cargas.QAba;   // kN/m
        var pg = cargas.Concentradas.Select(p => (p.X, p.G)).ToList();
        var pq = cargas.Concentradas.Select(p => (p.X, p.Q)).ToList();
        double xa = r.ApoioE, xb = L - r.ApoioD;
        var eg = new EstaticaViga(L, xa, xb, gTot / 100, pg);
        var eq = new EstaticaViga(L, xa, xb, qTot / 100, pq);
        var eg1 = new EstaticaViga(L, xa, xb, gTot / 100, pg);   // 1º estágio (peça sozinha): todas as permanentes
        r.RkE_G = eg.Ra; r.RkD_G = eg.Rb; r.RkE_Q = eq.Ra; r.RkD_Q = eq.Rb;
        if (analise != null)
        {
            // reações da análise: o maior dos modelos, G = PP + CP e Q = SC
            r.RkE_G = analise.Modelos.Max(m => m.Reacoes[CasoViga.PP].E + m.Reacoes[CasoViga.CP].E);
            r.RkD_G = analise.Modelos.Max(m => m.Reacoes[CasoViga.PP].D + m.Reacoes[CasoViga.CP].D);
            r.RkE_Q = analise.Modelos.Max(m => m.Reacoes[CasoViga.SC].E);
            r.RkD_Q = analise.Modelos.Max(m => m.Reacoes[CasoViga.SC].D);
            r.ModelosAnalise = string.Join(" + ", analise.Modelos.Select(m => m.Nome));
        }

        var m1 = new PassoMemoria { Titulo = "1. Geometria, apoios e cargas (valores característicos)" };
        m1.Linhas.Add($"Seção {N(b, "0.#")} × {N(h, "0.#")} cm" + (capa > 0 ? $" + capa de {N(capa, "0.#")} cm (2º estágio)" : "") +
                      (aba.Existe ? "; seção com aba: o cálculo usa a alma (b × h), a favor da segurança" : "") + $"; comprimento L = {N(L, "0.#")} cm.");
        m1.Linhas.Add($"Apoios: {N(r.ApoioE, "0.#")} cm da ponta esquerda e {N(r.ApoioD, "0.#")} cm da direita" +
                      (cargas.ApoioE > 0 || cargas.ApoioD > 0 ? " (digitados)" : " (eixo do pino; sem pino, meio do dente; ponta reta, 10 cm)") +
                      $" → vão de cálculo ℓ = {N(r.Vao, "0.#")} cm.");
        m1.Linhas.Add($"Peso próprio: volume {N(vol, "0.000")} m³ × {N(gc, "0.#")} kN/m³ / {N(L / 100, "0.00")} m = g1 = {N(r.G1, "0.00")} kN/m.");
        if (capa > 0) m1.Linhas.Add($"Capa sobre a viga: {N(b / 100, "0.00")} × {N(capa / 100, "0.00")} m × {N(gc, "0.#")} kN/m³ = {N(r.GCapa, "0.00")} kN/m.");
        m1.Linhas.Add($"Permanente adicional (digitada): g2 = {N(cargas.G, "0.00")} kN/m; variável: q = {N(cargas.Q, "0.00")} kN/m.");
        if (cargas.GAba > 0 || cargas.QAba > 0)
            m1.Linhas.Add($"Na aba (por lado{(ladosAba == 2 ? ", dois lados" : "")}): g = {N(cargas.GAba, "0.00")} kN/m, q = {N(cargas.QAba, "0.00")} kN/m → " +
                          $"somadas às verticais: g = {N(nAba * cargas.GAba, "0.00")} e q = {N(nAba * cargas.QAba, "0.00")} kN/m.");
        foreach (var p in cargas.Concentradas)
            m1.Linhas.Add($"Concentrada em x = {N(p.X, "0.#")} cm: G = {N(p.G, "0.##")} kN, Q = {N(p.Q, "0.##")} kN.");
        m1.Linhas.Add($"Reações características: esquerda G = {N(eg.Ra, "0.0")} kN, Q = {N(eq.Ra, "0.0")} kN; direita G = {N(eg.Rb, "0.0")} kN, Q = {N(eq.Rb, "0.0")} kN.");
        r.Memoria.Add(m1);
        if (cargas.Vazia) r.Avisos.Add("Sem cargas digitadas: o cálculo usa só o peso próprio da peça.");
        foreach (var p in cargas.Concentradas.Where(p => p.X < xa || p.X > xb))
            r.Avisos.Add($"Carga concentrada em x = {N(p.X, "0.#")} cm fica fora dos apoios (balanço): conferir.");

        // ---------------- 2. esforços de cálculo (ELU, combinação normal) ----------------
        double Md(double x) => (k.GamaG * eg.M(x) + k.GamaQ * eq.M(x)) / 100;          // kN·m
        double Vd(double x) => k.GamaG * eg.V(x) + k.GamaQ * eq.V(x);                   // kN
        if (analise == null)
        {
            foreach (double x in eg.Pontos().Concat(eq.Pontos()).Distinct().OrderBy(x => x))
                r.Diagrama.Add((x, Md(x), Vd(x)));
        }
        else
        {
            // envoltória da análise (D51): o maior Md positivo e o Vd de maior módulo em cada x; o negativo à parte
            foreach (var p in analise.Envoltoria.Where(p => p.X >= xa - 1e-6 && p.X <= xb + 1e-6))
                r.Diagrama.Add((p.X, p.MMax, Math.Abs(p.VMax) >= Math.Abs(p.VMin) ? p.VMax : p.VMin));
            foreach (var p in analise.Envoltoria.Where(p => p.MMin < -1e-6)) r.DiagramaNegativo.Add((p.X, p.MMin));
            r.MdMin = r.DiagramaNegativo.Count > 0 ? r.DiagramaNegativo.Min(p => p.Md) : 0;
        }
        var max = r.Diagrama.OrderByDescending(p => p.Md).First();
        r.MdMax = max.Md; r.XMdMax = max.X;
        r.VdE = analise == null ? Math.Abs(Vd(xa + 1e-6)) : r.Diagrama.Where(p => p.X <= (xa + xb) / 2).Max(p => Math.Abs(p.Vd));
        r.VdD = analise == null ? Math.Abs(Vd(xb - 1e-6)) : r.Diagrama.Where(p => p.X >= (xa + xb) / 2).Max(p => Math.Abs(p.Vd));
        double MdG1 = k.GamaG * r.Diagrama.Max(p => eg1.M(p.X)) / 100;   // 1º estágio
        var m2 = new PassoMemoria { Titulo = "2. Esforços de cálculo (ELU, combinação normal: Fd = γg·G + γq·Q)" };
        m2.Linhas.Add($"γg = {N(k.GamaG)}; γq = {N(k.GamaQ)} (NBR 6118, Tabela 11.1).");
        if (analise != null)
            m2.Linhas.Add($"Esforços da análise da viga (envoltória de: {r.ModelosAnalise}); Md,mín = {N(r.MdMin, "0.0")} kN·m (negativo das ligações).");
        m2.Linhas.Add($"Md,máx = {N(r.MdMax, "0.0")} kN·m em x = {N(r.XMdMax, "0")} cm" +
                      (cargas.Concentradas.Count == 0 && analise == null ? $" (= (γg·g + γq·q)·ℓ²/8 = ({N(k.GamaG * gTot, "0.00")} + {N(k.GamaQ * qTot, "0.00")})·{N(r.Vao / 100, "0.00")}²/8)" : "") + ".");
        m2.Linhas.Add($"Vd junto aos apoios: esquerdo {N(r.VdE, "0.0")} kN; direito {N(r.VdD, "0.0")} kN (no eixo do apoio, sem a redução de 17.4.1.2.1, a favor da segurança).");
        if (capa > 0) m2.Linhas.Add($"1º estágio (peça sozinha, todas as permanentes): Md,1 = {N(MdG1, "0.0")} kN·m.");
        r.Memoria.Add(m2);

        // ---------------- 3. flexão: armadura inferior ----------------
        var mat = new Flexao(fck, fcd, fyd);
        double phiTcm = phiT0 / 10;
        r.AsMin = TabelasNbr.RhoMinVigaPct(fck) / 100 * b * h;
        double asSupPadrao = padrao.Where(l => l.Funcao == TemplateVigaPreMoldada.FSuperior || l.Funcao == TemplateVigaPreMoldada.FConstrutiva)
            .Sum(l => l.Quantidade * A(l.Bitola));
        double AsNec(double dd, out double asCompr, out double xd)
        {
            var f2 = mat.Armadura(r.MdMax * 100, b, dd + capa, cobT + phiTcm + 0.8);
            asCompr = f2.AsCompr; xd = f2.XsobreD;
            double a = f2.As;
            if (capa > 0)
            {
                var f1 = mat.Armadura(MdG1 * 100, b, dd, cobT + phiTcm + 0.8);
                if (f1.As > a) { a = f1.As; asCompr = Math.Max(asCompr, f1.AsCompr); xd = f1.XsobreD; }
            }
            return a;
        }
        ArranjoInferior melhor = null;
        double melhorNota = double.MaxValue, asNecMelhor = 0, asComprMelhor = 0, xdMelhor = 0;
        double ah = Math.Max(2, 1.2 * dagg / 10), wUtil = b - 2 * cobL - 2 * phiTcm;
        double phiPadrao = padrao.Where(l => l.Inferior).Select(l => l.Bitola).DefaultIfEmpty(0).Max();
        foreach (double phi in BitolasLong)
        {
            double pc = phi / 10, ahP = Math.Max(ah, pc);
            int nMax = (int)Math.Floor((wUtil + ahP) / (pc + ahP) + 1e-9);
            if (nMax < 2) continue;
            for (int n = 2; n <= 2 * nMax; n++)
            {
                int n1 = Math.Min(n, nMax), n2 = n - n1;
                if (n2 == 1) { n1 = n - 2; n2 = 2; }   // 2ª camada com pelo menos 2 barras (nos cantos do estribo)
                if (n1 < n2 || n1 < 2) continue;
                double y1 = cobF + phiTcm + pc / 2, y2 = y1 + pc + Math.Max(2, pc);
                double ycg = (n1 * y1 + n2 * y2) / n;
                var arr = new ArranjoInferior { Bitola = phi, N1 = n1, N2 = n2, D = h - ycg };
                double nec = AsNec(arr.D, out double ac, out double xd);
                if (arr.Area < Math.Max(nec, r.AsMin) - 1e-9) continue;
                if (arr.Area > 0.04 * b * h) continue;
                // nota: área (cm²) + penalidade da 2ª camada e do número de barras; bitola menor que a do padrão só se for bem melhor
                double nota = arr.Area * (n2 > 0 ? 1.06 : 1) + 0.25 * n + (phi < phiPadrao - 1e-6 ? 0.5 : 0);
                if (nota < melhorNota - 1e-9) { melhorNota = nota; melhor = arr; asNecMelhor = nec; asComprMelhor = ac; xdMelhor = xd; }
                break;   // mais barras da mesma bitola só pioram a nota
            }
        }
        var m3 = new PassoMemoria { Titulo = "3. Flexão no ELU — armadura inferior (NBR 6118, 17.2.2)" };
        m3.Linhas.Add($"fcd = fck/γc = {N(fck)}/{N(k.GamaC)} = {N(fcd * 10, "0.00")} MPa; fyd = {N(fyd * 10, "0.0")} MPa; λ = {N(mat.Lambda, "0.00")}; αc = {N(mat.AlfaC, "0.00")}; x/d ≤ {N(mat.XdLim)} (14.6.4.3).");
        m3.Linhas.Add($"As,mín = ρmín·b·h = {N(TabelasNbr.RhoMinVigaPct(fck), "0.000")} % × {N(b, "0.#")} × {N(h, "0.#")} = {N(r.AsMin)} cm² (Tabela 17.3).");
        if (melhor == null)
        {
            r.Avisos.Add("Flexão: nenhum arranjo de até duas camadas resolve a armadura inferior nesta seção — aumentar a altura ou a largura da viga.");
            r.Verificacoes.Add(K("K1", "Flexão", "Armadura inferior de flexão (ELU)", $"Md = {N(r.MdMax, "0.0")} kN·m", "arranjo em até 2 camadas", true,
                "NBR 6118, 17.2.2", dica: "Aumente a seção."));
            m3.Linhas.Add("Nenhum arranjo de barras cabe na seção: aumentar a seção.");
        }
        else
        {
            r.Inferior = melhor; r.AsNec = asNecMelhor; r.AsComprNec = asComprMelhor;
            double dd = melhor.D;
            {
                // MRd da armadura proposta (só a tracionada; a linha neutra limitada ao x/d de ductilidade)
                double dm = dd + capa, xr = Math.Min(melhor.Area * fyd / (mat.AlfaC * fcd * b * mat.Lambda), mat.XdLim * dm);
                r.MRd = Math.Min(melhor.Area * fyd, mat.AlfaC * fcd * b * mat.Lambda * xr) * (dm - mat.Lambda * xr / 2) / 100;
            }
            var f = mat.Armadura(r.MdMax * 100, b, dd + capa, cobT + phiTcm + 0.8);
            m3.Linhas.Add($"d = h − (cobrimento + estribo + CG das barras) = {N(dd, "0.0")} cm" + (capa > 0 ? $" (peça); com a capa, d = {N(dd + capa, "0.0")} cm" : "") + ".");
            m3.Linhas.Add($"x = (d/λ)·[1 − √(1 − 2·Md/(αc·fcd·b·d²))] = {N(f.X, "0.0")} cm → x/d = {N(f.XsobreD, "0.000")}" +
                          (f.AsCompr > 0 ? $" > {N(mat.XdLim)}: armadura dupla, As' = {N(f.AsCompr)} cm²" : "") + ".");
            m3.Linhas.Add($"As = Md/(fyd·(d − λx/2)) = {N(f.As)} cm²" + (capa > 0 ? $"; 1º estágio: {N(mat.Armadura(MdG1 * 100, b, dd, cobT + phiTcm + 0.8).As)} cm²" : "") +
                          $" → As,nec = {N(Math.Max(asNecMelhor, r.AsMin))} cm².");
            m3.Linhas.Add($"Proposta: {melhor.Texto} = {N(melhor.Area)} cm² (espaçamento livre ≥ {N(ah, "0.0#")} cm, 18.3.2.2).");
            r.Verificacoes.Add(K("K1", "Flexão", "Armadura inferior de flexão (ELU)", $"{melhor.Texto} = {N(melhor.Area)} cm²",
                $"≥ As,nec = {N(asNecMelhor)} cm²", false, "NBR 6118, 17.2.2"));
            r.Verificacoes.Add(K("K2", "Flexão", "Armadura mínima de flexão", $"{N(melhor.Area)} cm²", $"≥ As,mín = {N(r.AsMin)} cm²", false, "NBR 6118, 17.3.5.2.1"));
            r.Verificacoes.Add(K("K3", "Flexão", "Ductilidade: posição da linha neutra", $"x/d = {N(xdMelhor, "0.000")}", $"≤ {N(mat.XdLim)}",
                asComprMelhor > 0, "NBR 6118, 14.6.4.3", SituacaoVerificacao.Conferir,
                asComprMelhor > 0 ? $"Com armadura dupla (As' = {N(asComprMelhor)} cm²); prefira aumentar a altura da viga." : ""));
        }
        r.Memoria.Add(m3);

        // ---------------- 4. içamento (alças) e armadura superior ----------------
        var m4 = new PassoMemoria { Titulo = "4. Içamento e armadura superior (NBR 9062, 5.3)" };
        double phiSup = padrao.Where(l => l.Funcao == TemplateVigaPreMoldada.FSuperior).Select(l => l.Bitola).DefaultIfEmpty(10).Max();
        int nSup = padrao.Where(l => l.Funcao == TemplateVigaPreMoldada.FSuperior).Select(l => l.Quantidade).DefaultIfEmpty(2).Max();
        double asSupNec = r.AsComprNec;
        if (v.SimNao("Alcas"))
        {
            var xs = TemplateVigaPreMoldada.PosicaoAlcas(v, b, L, e, d);
            double fcj = v.Numero("FcjSaque") > 0 ? v.Numero("FcjSaque") : fck;
            var matJ = new Flexao(fcj, fcj / k.GamaC / 10, fyd);
            var ei = new EstaticaViga(L, Math.Min(xs[0], xs[1]), Math.Max(xs[0], xs[1]), r.G1 / 100 * k.CoefDinamico);
            double mNeg = Math.Max(Math.Abs(ei.M(ei.Xa)), Math.Abs(ei.M(ei.Xb))) * k.GamaG / 100;   // kN·m
            double mPos = Math.Max(0, ei.M((ei.Xa + ei.Xb) / 2)) * k.GamaG / 100;
            double dSup = h - cobT - phiTcm - phiSup / 20;
            var fs = matJ.Armadura(mNeg * 100, b, dSup, cobF + phiTcm + 0.8);
            r.AsSupIcamento = fs.As;
            asSupNec = Math.Max(asSupNec, fs.As);
            m4.Linhas.Add($"Alças a {N(Math.Min(xs[0], xs[1]), "0")} e {N(Math.Max(xs[0], xs[1]), "0")} cm da ponta esquerda; carga = γg·βdin·g1 = {N(k.GamaG)}·{N(k.CoefDinamico)}·{N(r.G1, "0.00")} kN/m; fcj = {N(fcj)} MPa.");
            m4.Linhas.Add($"Momento negativo nas alças: Md− = {N(mNeg, "0.00")} kN·m → As,sup = {N(fs.As)} cm² (d = {N(dSup, "0.0")} cm). Positivo entre as alças: {N(mPos, "0.00")} kN·m.");
        }
        else m4.Linhas.Add("Viga sem alças no template: içamento não verificado.");
        if (r.AsComprNec > 0) m4.Linhas.Add($"Armadura de compressão da flexão: As' = {N(r.AsComprNec)} cm².");
        r.Superior = (nSup, phiSup);
        if (capa > 0)
        {
            double asPorta = Math.Max(0, v.Inteiro("PortaEstriboQtd")) * A(v.Numero("PortaEstriboBitola"));
            bool falta = asSupNec > asPorta + 1e-9;
            m4.Linhas.Add($"Viga com 2º estágio: a peça leva só o porta-estribo ({N(asPorta)} cm²) — a superior vai no capeamento.");
            r.Verificacoes.Add(K("K4", "Içamento", "Armadura superior da peça no içamento (porta-estribo)", $"{N(asPorta)} cm²", $"≥ {N(asSupNec)} cm²", falta,
                "NBR 9062, 5.3", SituacaoVerificacao.Conferir, falta ? "Aumente o porta-estribo (bitola) ou leve as alças para mais perto das pontas." : ""));
        }
        else
        {
            if (asSupNec > asSupPadrao + 1e-9)
            {
                // menor bitola com 2 (ou 3) barras que resolve
                var opc = BitolasLong.SelectMany(p => new[] { 2, 3 }.Select(n => (n, p))).Where(o => o.n * A(o.p) >= asSupNec)
                    .OrderBy(o => o.n * A(o.p)).ThenBy(o => o.n).FirstOrDefault();
                if (opc.n > 0) r.Superior = opc;
                m4.Linhas.Add($"Superior do padrão ({N(asSupPadrao)} cm²) < {N(asSupNec)} cm² → proposta: {r.Superior.N} ø{N(r.Superior.Bitola, "0.#")} = {N(r.Superior.N * A(r.Superior.Bitola))} cm².");
            }
            else m4.Linhas.Add($"Superior do padrão: {N(asSupPadrao)} cm² ≥ {N(asSupNec)} cm² — mantida.");
            double asSup = Math.Max(asSupPadrao, r.Superior.N * A(r.Superior.Bitola));
            r.Verificacoes.Add(K("K4", "Içamento", "Armadura superior (içamento e compressão)", $"{N(asSup)} cm²", $"≥ {N(asSupNec)} cm²", asSup < asSupNec - 1e-9, "NBR 9062, 5.3"));
        }
        r.Memoria.Add(m4);

        // ---------------- torção: torque da carga na aba e seção vazada equivalente (verificações no passo 10) ----------------
        // Só com carga digitada na aba: a laje fora do eixo da alma é torção de EQUILÍBRIO (17.5.1.2), presa nos dois apoios.
        Func<double, double> tdEm = _ => 0;   // kN·cm, em módulo
        SecaoTorcao st = null;
        double eAba = 0, ePP = 0, gAbaPP = 0, mtG = 0, mtQ = 0;
        bool torcaoNaMontagem = false;
        if (aba.Existe && cargas.Torcao && (cargas.GAba > 0 || cargas.QAba > 0))
        {
            eAba = cargas.ExcAba > 0 ? cargas.ExcAba : b / 2 + aba.Largura / 2;
            double h0 = aba.Altura, h1 = aba.AltPonta, w = aba.Largura;
            gAbaPP = w * (h0 + h1) / 2 / 1e4 * gc;                       // peso da aba (kN/m), por lado
            ePP = b / 2 + w * (h0 + 2 * h1) / (3 * (h0 + h1));          // CG do trapézio da aba até o eixo da alma (cm)
            // torque por metro (kN·cm/m): seção L = a aba toda de um lado; T invertido = equilibrado, salvo a montagem
            // (laje de um lado só, sem sobrecarga) e a sobrecarga de um lado só — vale o pior dos dois
            mtG = ladosAba == 1 ? cargas.GAba * eAba + gAbaPP * ePP : cargas.GAba * eAba;
            mtQ = cargas.QAba * eAba;
            double mtd;
            if (ladosAba == 1) mtd = (k.GamaG * mtG + k.GamaQ * mtQ) / 100;
            else
            {
                double mont = k.GamaG * mtG, serv = k.GamaQ * mtQ;
                torcaoNaMontagem = mont >= serv;
                mtd = Math.Max(mont, serv) / 100;                         // kN·cm/cm
            }
            if (mtd > 1e-9)
            {
                var f = TorcaoViga.Diagrama(mtd, aba.Ini, aba.Fim, xa, xb, out double ta, out double tb);
                tdEm = x => Math.Abs(f(x));
                r.TdE = ta / 100; r.TdD = tb / 100;                       // kN·m
                double phiL = r.Inferior?.Bitola ?? 12.5;
                st = TorcaoViga.Secao(b, h, cobL + phiT0 / 10 + phiL / 20);
                r.He = st.He; r.Ae = st.Ae; r.Ue = st.Ue;
                r.TRd2 = TorcaoViga.TRd2(st, fck, fcd) / 100;
                foreach (var p in r.Diagrama) r.DiagramaTorcao.Add((p.X, f(p.X) / 100));
            }
        }
        // estribo de torção por ramo (cm²/cm) na seção x
        double As90Em(double x) => st == null ? 0 : TorcaoViga.As90PorCm(st, tdEm(x), fywd);

        // ---------------- 5. cisalhamento: estribos (modelo I) ----------------
        double dV = (r.Inferior?.D ?? h - cobF - phiTcm - 1) + (capa > 0 ? 0 : 0);   // a peça resiste sozinha à cortante do 1º estágio: d da peça
        double av2 = 1 - fck / 250;
        r.VRd2 = 0.27 * av2 * fcd * b * dV;
        r.Vc = 0.6 * fctd * b * dV;
        double vdMax = Math.Max(r.VdE, r.VdD);
        double rhoMin = 0.2 * fctm * 10 / fywk;   // ρsw,mín
        double aswMin = rhoMin * b;               // cm²/cm
        double AswS(double vd) => Math.Max(aswMin, Math.Max(0, vd - r.Vc) / (0.9 * dV * fywd));
        // cortante + torção (17.7.2.2): soma por ramo — os 2 ramos do cortante + 2 × o ramo da torção (um em cada parede)
        // aba de laje (consolo corrido): a carga da laje sobe pelos estribos da alma no trecho da aba (suspensão, NBR 6118 18.3.6)
        double fdAbaPorCm = 0;   // kN/cm, todos os lados
        if (aba.Existe && !aba.Consolo && (cargas.GAba > 0 || cargas.QAba > 0))
        {
            double gAbaProprio = aba.Largura * (aba.Altura + aba.AltPonta) / 2 / 1e4 * gc;   // kN/m por lado
            r.FdAba = k.GamaG * (cargas.GAba + gAbaProprio) + k.GamaQ * cargas.QAba;       // kN/m por lado
            fdAbaPorCm = ladosAba * r.FdAba / 100;
            r.AsSuspAba = fdAbaPorCm / fywd * 100;                                           // cm²/m
        }
        double SuspEm(double x) => fdAbaPorCm > 0 && aba.Em(x) ? fdAbaPorCm / fywd : 0;
        double AswTot(double vd, double x) => Math.Max(aswMin, Math.Max(0, vd - r.Vc) / (0.9 * dV * fywd) + 2 * As90Em(x)) + SuspEm(x);
        double aswPonta = Math.Max(AswTot(r.VdE, xa + 1e-6), AswTot(r.VdD, xb - 1e-6));
        bool rigoroso = vdMax > 0.67 * r.VRd2;
        double sMax = rigoroso ? Math.Min(0.3 * dV, 20) : Math.Min(0.6 * dV, 30);
        double espPadP = v.Numero("EstEspPonta") > 0 ? v.Numero("EstEspPonta") : 10, espPadV = v.Numero("EstEspVao") > 0 ? v.Numero("EstEspVao") : 15;
        double Arred(double s) => Math.Floor(s / 2.5 + 1e-9) * 2.5;
        double phiEst = phiT0, sP = 0;
        foreach (double p in BitolasEst.Where(x => x >= phiT0 - 1e-6).DefaultIfEmpty(phiT0))
        {
            phiEst = p;
            sP = Math.Min(Math.Min(Arred(2 * A(p) / aswPonta), sMax), espPadP);
            if (sP >= 7.5 - 1e-9) break;
        }
        if (sP < 5) { r.Avisos.Add($"Estribos: com ø{N(phiEst, "0.#")} o espaçamento nas pontas fica {N(sP, "0.#")} cm — usar 4 ramos ou aumentar a seção."); sP = Math.Max(sP, 2.5); }
        double sV = Math.Min(Math.Min(Arred(2 * A(phiEst) / aswMin), sMax), espPadV);
        if (sV < sP) sV = sP;
        // zona das pontas: até onde a cortante pede mais que o espaçamento do vão (das duas pontas, a maior; o template é simétrico)
        double capV = 2 * A(phiEst) / sV;
        double xFimE = xa, xIniD = xb;
        foreach (var p in r.Diagrama)
        {
            if (AswTot(Math.Abs(p.Vd), p.X) <= capV + 1e-12) continue;
            if (p.X <= (xa + xb) / 2) xFimE = Math.Max(xFimE, p.X); else xIniD = Math.Min(xIniD, p.X);
        }
        double x0E = (e.TemDente ? e.CompDente : 0) + cobP, x0D = (d.TemDente ? d.CompDente : 0) + cobP;
        double zona = Math.Max(Math.Max(xFimE - x0E, (L - xIniD) - x0D), 0);
        zona = Math.Ceiling(zona / 10 - 1e-9) * 10;
        double zonaPad = Math.Max(0, v.Numero("EstZonaPonta"));
        if (zona > 0 || zonaPad > 0) zona = Math.Max(zona, zonaPad);
        if (sV <= sP + 1e-9) zona = 0;   // um espaçamento só
        r.EstBitola = phiEst; r.EspPonta = sP; r.EspVao = sV; r.ZonaPonta = zona;
        var m5 = new PassoMemoria { Titulo = "5. Cisalhamento no ELU — estribos verticais, modelo I (NBR 6118, 17.4.2.2)" };
        m5.Linhas.Add($"d = {N(dV, "0.0")} cm; αv2 = 1 − fck/250 = {N(av2, "0.000")}; VRd2 = 0,27·αv2·fcd·b·d = {N(r.VRd2, "0.0")} kN; Vd,máx = {N(vdMax, "0.0")} kN (Vd/VRd2 = {N(r.VdSobreVRd2, "0.00")}).");
        m5.Linhas.Add($"Vc = Vc0 = 0,6·fctd·b·d = 0,6·{N(fctd * 10, "0.000")}·{N(b, "0.#")}·{N(dV, "0.0")}/10 = {N(r.Vc, "0.0")} kN (flexão simples, 17.4.2.2 b).");
        m5.Linhas.Add($"Asw/s = (Vsd − Vc)/(0,9·d·fywd), fywd = {N(fywd * 10, "0")} MPa; mínimo ρsw = 0,2·fctm/fywk = {N(rhoMin * 100, "0.000")} % → {N(aswMin * 100, "0.00")} cm²/m.");
        if (fdAbaPorCm > 0)
            m5.Linhas.Add($"Suspensão da carga da aba no trecho dela (18.3.6): Fd = {N(fdAbaPorCm * 100, "0.0")} kN/m ÷ fywd = {N(r.AsSuspAba, "0.00")} cm²/m, somada aos estribos da alma (a favor da segurança: sem contar o ramo do estribo da aba na face oposta).");
        m5.Linhas.Add($"Na ponta: Asw/s = {N(aswPonta * 100, "0.00")} cm²/m" + (st != null || fdAbaPorCm > 0 ? $" (cortante {N(AswS(vdMax) * 100, "0.00")}" + (st != null ? $" + torção 2 × {N(As90Em(xa + 1e-6) * 100, "0.00")}, passo 10" : "") +
                       (SuspEm(xa + 1e-6) > 0 ? $" + suspensão da aba {N(SuspEm(xa + 1e-6) * 100, "0.00")}" : "") + ")" : "") +
                      $" → ø{N(phiEst, "0.#")} c/{N(sP, "0.#")} (2 ramos = {N(2 * A(phiEst) / sP * 100, "0.00")} cm²/m).");
        m5.Linhas.Add($"No vão: ø{N(phiEst, "0.#")} c/{N(sV, "0.#")}; s,máx = {(rigoroso ? "0,3d ≤ 20 cm" : "0,6d ≤ 30 cm")} = {N(sMax, "0.#")} cm (18.3.3.2)" +
                      (zona > 0 ? $"; zona das pontas: {N(zona, "0")} cm a partir da face do dente (ou da ponta)." : "."));
        m5.Linhas.Add($"O padrão do escritório é o mínimo: espaçamentos até {N(espPadP, "0.#")} (pontas) e {N(espPadV, "0.#")} cm (vão), bitola a partir de ø{N(phiT0, "0.#")}.");
        r.Memoria.Add(m5);
        // para a janela: VRd3 dos estribos propostos, zona das pontas e decalagem aℓ (modelo I)
        r.VRd3Ponta = r.Vc + 2 * A(phiEst) / sP * 0.9 * dV * fywd;
        r.VRd3Vao = r.Vc + 2 * A(phiEst) / sV * 0.9 * dV * fywd;
        r.XZonaE = zona > 0 ? x0E + zona : L / 2; r.XZonaD = zona > 0 ? L - x0D - zona : L / 2;
        r.Al = vdMax > r.Vc + 1e-9 ? Math.Min(dV, Math.Max(0.5 * dV, dV * vdMax / (2 * (vdMax - r.Vc)))) : dV;
        m5.Linhas.Add($"VRd3 = Vc + Vsw: pontas {N(r.VRd3Ponta, "0.0")} kN, vão {N(r.VRd3Vao, "0.0")} kN. Decalagem do diagrama de Md: aℓ = {N(r.Al, "0.0")} cm (17.4.2.2 c).");
        r.Verificacoes.Add(K("K5", "Cisalhamento", "Compressão das bielas (VRd2)", $"Vd = {N(vdMax, "0.0")} kN", $"≤ VRd2 = {N(r.VRd2, "0.0")} kN", vdMax > r.VRd2, "NBR 6118, 17.4.2.2",
            dica: "Aumente a largura (b) ou a altura da viga."));
        r.Verificacoes.Add(K("K6", "Cisalhamento", st != null ? "Estribos nas pontas (Asw/s, cortante + torção)" : "Estribos nas pontas (Asw/s)", $"{N(2 * A(phiEst) / sP * 100, "0.00")} cm²/m",
            $"≥ {N(aswPonta * 100, "0.00")} cm²/m", 2 * A(phiEst) / sP < aswPonta - 1e-12, st != null ? "NBR 6118, 17.4.2.2 e 17.7.2.2" : "NBR 6118, 17.4.2.2"));
        r.Verificacoes.Add(K("K7", "Cisalhamento", "Espaçamento máximo dos estribos com o Vd", $"{N(Math.Max(sP, sV), "0.#")} cm",
            $"≤ {N(sMax, "0.#")} cm ({(rigoroso ? "Vd > 0,67·VRd2" : "Vd ≤ 0,67·VRd2")})", Math.Max(sP, sV) > sMax + 1e-9, "NBR 6118, 18.3.3.2"));

        // ---------------- 5b. armadura negativa nas pontas (ligação semirrígida, D51) ----------------
        if (r.DiagramaNegativo.Count > 0 && capa <= 0)
        {
            var m5b = new PassoMemoria { Titulo = "5b. Armadura negativa nas pontas — momento da ligação semirrígida (NBR 6118, 17.2, 17.4.2.2 c e 9.4)" };
            var regra = new Normas.RegrasTraspasse { Fck = fck, Aderencia = Normas.SituacaoAderencia.Ma };
            double phiSupCanto = padrao.Where(l => l.Funcao == TemplateVigaPreMoldada.FSuperior).Select(l => l.Bitola).DefaultIfEmpty(10).Max();
            int nSupCanto = padrao.Where(l => l.Funcao == TemplateVigaPreMoldada.FSuperior).Sum(l => l.Quantidade);
            double wUtilT = b - 2 * cobL - 2 * phiTcm;
            foreach (var (lado, noLado) in new (string, Func<double, bool>)[] { ("esquerda", x => x <= L / 2), ("direita", x => x > L / 2) })
            {
                var pts = r.DiagramaNegativo.Where(p => noLado(p.X)).ToList();
                if (pts.Count == 0) continue;
                // As necessária ponto a ponto, com a altura da seção em x (no dente, a do dente); na 2ª camada, d menor
                double AsEm(double x, double md, double phiMm, int camada)
                {
                    double hx = e.TemDente && x < e.CompDente ? e.Dente.Altura : d.TemDente && x > L - d.CompDente ? d.Dente.Altura : h;
                    double dx = hx - cobT - phiTcm - phiMm / 20;
                    if (camada == 2) dx -= phiSupCanto / 20 + Math.Max(2, Math.Max(phiSupCanto, phiMm) / 10) + phiMm / 20;
                    return dx > 0 ? mat.Armadura(Math.Abs(md) * 100, b, dx, cobF + phiTcm + 0.8).As : double.MaxValue;
                }
                // arranjo: na 1ª camada entre as superiores dos cantos; se não couber, na 2ª camada (a largura toda); menor área,
                // bitola não menor que 10 mm; espaçamento livre ≥ 2 cm, ø e 1,2·dmáx (18.3.2.2)
                (int N, double Phi, double Nec, int Camada) arr = (0, 0, 0, 0);
                foreach (int camada in new[] { 1, 2 })
                {
                    foreach (double phi in BitolasLong.Where(f => f >= 10 - 1e-6))
                    {
                        double nec = Math.Max(pts.Max(p => AsEm(p.X, p.Md, phi, camada)), 0);
                        int n = Math.Max(2, (int)Math.Ceiling(nec / A(phi) - 1e-9));
                        double pc = phi / 10, ahP = Math.Max(ah, pc);
                        int cabem = (int)Math.Floor((wUtilT + ahP) / (pc + ahP) + 1e-9) - (camada == 1 ? nSupCanto : 0);
                        if (n > cabem) continue;
                        if (arr.N == 0 || n * A(phi) < arr.N * A(arr.Phi) - 1e-9) arr = (n, phi, nec, camada);
                    }
                    if (arr.N > 0) break;
                }
                double xPico = pts.OrderBy(p => p.Md).First().X, mdPico = pts.Min(p => p.Md);
                if (arr.N == 0)
                {
                    r.Avisos.Add($"Negativa da ponta {lado}: não cabe numa camada (nem entre as superiores, nem na 2ª camada) — aumentar a largura ou reduzir o αR.");
                    r.Verificacoes.Add(K("K22", "Negativa", $"Armadura negativa da ponta {lado}", $"Md− = {N(mdPico, "0.0")} kN·m", "numa camada", true, "NBR 6118, 17.2.2"));
                    continue;
                }
                // comprimento: da ponta (a cobrimento) até onde o negativo acaba, mais a decalagem aℓ e o comprimento de ancoragem lb,nec
                double xZero = lado == "esquerda" ? pts.Max(p => p.X) : pts.Min(p => p.X);
                double lb = regra.Lb(arr.Phi, acoL), lbNec = Math.Max(lb * arr.Nec / (arr.N * A(arr.Phi)), Math.Max(0.3 * lb, Math.Max(10 * arr.Phi / 10, 10)));
                double compr = Math.Ceiling(((lado == "esquerda" ? xZero : L - xZero) + r.Al + lbNec - cobP) / 5 - 1e-9) * 5;
                double ini = lado == "esquerda" ? cobP : Math.Max(cobP, L - cobP - compr), fim = lado == "esquerda" ? Math.Min(L - cobP, cobP + compr) : L - cobP;
                r.Negativas.Add((lado, arr.N, arr.Phi, ini, fim, arr.Nec, arr.Camada));
                m5b.Linhas.Add($"Ponta {lado}: Md− = {N(mdPico, "0.0")} kN·m em x = {N(xPico, "0")} cm (envoltória: {r.ModelosAnalise}); As,nec = {N(arr.Nec)} cm² " +
                               $"(d da seção em cada x; no dente, a altura do dente) → {arr.N} ø{N(arr.Phi, "0.#")} = {N(arr.N * A(arr.Phi))} cm², " +
                               (arr.Camada == 1 ? "na 1ª camada, entre as superiores." : "na 2ª camada de cima (entre as superiores não cabe)."));
                m5b.Linhas.Add($"Comprimento: o negativo acaba em x = {N(xZero, "0")} cm; + aℓ = {N(r.Al, "0.0")} cm; + lb,nec = lb·As,nec/As,ef = {N(lb, "0")}·{N(arr.Nec)}/{N(arr.N * A(arr.Phi))} = {N(lbNec, "0")} cm " +
                               $"(má aderência, 9.3.1; mín. 0,3·lb, 10ø e 10 cm) → barra de {N(ini, "0")} a {N(fim, "0")} cm ({N(fim - ini, "0")} cm). Não soma com a superior (decisão do usuário).");
                r.Verificacoes.Add(K("K22", "Negativa", $"Armadura negativa da ponta {lado}", $"{arr.N} ø{N(arr.Phi, "0.#")} = {N(arr.N * A(arr.Phi))} cm²", $"≥ {N(arr.Nec)} cm²",
                    false, "NBR 6118, 17.2.2"));
                r.Verificacoes.Add(K("K23", "Negativa", $"Ancoragem da negativa na ligação (ponta {lado})", "barra até a ponta", "dimensionar a ligação", true, "NBR 9062, 7",
                    SituacaoVerificacao.Conferir, "Na ponta a negativa entrega o momento à ligação (solda, chumbador, graute): confira a ancoragem nela."));
            }
            if (m5b.Linhas.Count > 0) r.Memoria.Add(m5b);
        }
        else if (r.DiagramaNegativo.Count > 0 && capa > 0)
            r.Avisos.Add("Momento negativo nas pontas com 2º estágio: a negativa vai no detalhe do capeamento da laje (não entra na peça).");

        // ---------------- 5c. aba de laje como consolo corrido (D51, parte 2) ----------------
        if (r.FdAba > 0)
        {
            var m5c = new PassoMemoria { Titulo = "5c. Aba de laje — consolo corrido (NBR 9062, 7.3) e suspensão na alma (NBR 6118, 18.3.6)" };
            double phiA0 = v.Numero("AbaEstBitola") > 0 ? v.Numero("AbaEstBitola") : 8, espA0 = v.Numero("AbaEstEsp") > 0 ? v.Numero("AbaEstEsp") : 15;
            double eLaje = cargas.ExcAba > 0 ? cargas.ExcAba - b / 2 : aba.Largura / 2;            // da face da alma à linha da carga
            bool neoAba = v.Numero("Neoprene") > 0;
            double hdAba = (neoAba ? 0.16 : 0.5) * r.FdAba;                                           // kN/m
            double phiA = phiA0, sA = espA0, asTir = 0, dA = 0, ad = 0;
            foreach (double phi in BitolasEst.Where(x => x >= phiA0 - 1e-6).DefaultIfEmpty(phiA0))
            {
                dA = aba.Altura - cobT - phi / 20;                                                     // na face da alma
                ad = dA > 0 ? eLaje / dA : 99;
                asTir = (ad <= 0.5 ? 0.8 * r.FdAba / (fyd * 1.4) : (0.1 + Math.Min(ad, 1)) * r.FdAba / fyd) + hdAba / fyd;   // cm²/m
                phiA = phi;
                sA = Math.Min(espA0, Math.Floor(A(phi) / (asTir / 100) / 2.5 + 1e-9) * 2.5);
                if (sA >= 7.5 - 1e-9) break;
            }
            r.AdAba = ad; r.AsTirAba = asTir; r.AbaEstBitola = phiA; r.AbaEstEsp = sA;
            m5c.Linhas.Add($"Carga na aba (por lado): Fd = γg·(g,laje + peso da aba) + γq·q,laje = {N(r.FdAba, "0.0")} kN/m, a {N(eLaje, "0.0")} cm da face da alma; " +
                           $"Hd = {N(neoAba ? 0.16 : 0.5)}·Fd = {N(hdAba, "0.0")} kN/m ({(neoAba ? "elastômero" : "argamassa")}).");
            m5c.Linhas.Add($"d da aba na face da alma = {N(dA, "0.0")} cm; a/d = {N(ad, "0.00")} ({(ad <= 0.5 ? "consolo muito curto: atrito-cisalhamento, μ = 1,4" : ad <= 1 ? "consolo curto" : "a/d > 1: balanço — conferir")}).");
            m5c.Linhas.Add($"Tirante (ramo de cima do estribo da aba): As = {(ad <= 0.5 ? "0,8·Fd/(fyd·μ)" : "(0,1 + a/d)·Fd/fyd")} + Hd/fyd = {N(asTir)} cm²/m → ø{N(phiA, "0.#")} c/{N(sA, "0.#")} " +
                           $"= {N(A(phiA) / sA * 100)} cm²/m (padrão do escritório: ø{N(phiA0, "0.#")} c/{N(espA0, "0.#")}, o mínimo).");
            r.Verificacoes.Add(K("K24", "Aba", "Tirante da aba de laje (estribo da aba, por metro)", $"{N(A(phiA) / sA * 100)} cm²/m", $"≥ {N(asTir)} cm²/m",
                A(phiA) / sA * 100 < asTir - 1e-9 || ad > 1, "NBR 9062, 7.3", ad > 1 ? SituacaoVerificacao.Conferir : SituacaoVerificacao.NaoAtende,
                "Aumente a altura da aba ou a bitola do estribo dela."));
            double vrd2Aba = 0.27 * av2 * fcd * 100 * dA;                                              // kN por metro de aba
            r.Verificacoes.Add(K("K25", "Aba", "Biela da aba (estimativa: VRd2 por metro com o d da aba)", $"Fd = {N(r.FdAba, "0.0")} kN/m", $"≤ {N(vrd2Aba, "0.0")} kN/m",
                r.FdAba > vrd2Aba, "NBR 9062, 7.3", SituacaoVerificacao.Conferir, "Aumente a altura da aba."));
            r.Verificacoes.Add(K("K26", "Aba", "Suspensão da carga da aba pelos estribos da alma (somada ao cortante)", $"{N(r.AsSuspAba)} cm²/m", "nos estribos da alma (passo 5)",
                false, "NBR 6118, 18.3.6"));
            r.Memoria.Add(m5c);
        }

        // ---------------- 6. ancoragem no apoio (pontas retas) ----------------
        if (r.Inferior != null)
        {
            var m6 = new PassoMemoria { Titulo = "6. Armadura inferior no apoio (NBR 6118, 18.3.2.4)" };
            foreach (var (x, vd, lado) in new[] { (e, r.VdE, "esquerda"), (d, r.VdD, "direita") })
            {
                if (x.TemDente) { m6.Linhas.Add($"Ponta {lado}: dente Gerber — a força no apoio vai para o tirante e a suspensão (item 7)."); continue; }
                double al = vd > r.Vc + 1e-9 ? Math.Min(dV, Math.Max(0.5 * dV, dV * vd / (2 * (vd - r.Vc)))) : dV;
                double asApoio = al / dV * vd / fyd;
                m6.Linhas.Add($"Ponta {lado}: aℓ = {N(al, "0.0")} cm; As,apoio ≥ (aℓ/d)·Vd/fyd = {N(asApoio)} cm²; chegam {N(r.Inferior.Area)} cm² (toda a inferior).");
                r.Verificacoes.Add(K("K8", "Ancoragem", $"Armadura inferior no apoio ({lado})", $"{N(r.Inferior.Area)} cm²", $"≥ {N(asApoio)} cm²",
                    r.Inferior.Area < asApoio - 1e-9, "NBR 6118, 18.3.2.4"));
            }
            r.Memoria.Add(m6);
        }

        // ---------------- 7. dentes Gerber (D51, parte 3: armadura calculada pela NBR 9062, 7.3 e 7.4) ----------------
        // O dente é um consolo (7.4.2): tirante no topo (7.3.5), costura em 2/3 d junto dele (7.3.6, 7.4.5.1), estribos verticais
        // (7.3.7.2), suspensão concentrada junto da face (7.4.6) e o grampo no fundo, no canto do recorte, completando a ancoragem da
        // inferior (pedido do usuário, 25/11/2026). O resultado vira um TIPO NOVO no catálogo, por viga ("DG 75x25 – V101"): depois de
        // conferidos, os dentes são padronizados (decisão do usuário). O padrão do catálogo é o mínimo (bitolas e quantidades).
        var m7 = new PassoMemoria { Titulo = "7. Dentes Gerber (NBR 9062, 7.4: consolo, 7.3; suspensão, 7.4.6)" };
        bool neoprene = v.Numero("Neoprene") > 0;
        double fHd = neoprene ? 0.16 : 0.5;
        var regraMa = new Normas.RegrasTraspasse { Fck = fck, Aderencia = Normas.SituacaoAderencia.Ma };
        var regraBoa = new Normas.RegrasTraspasse { Fck = fck, Aderencia = Normas.SituacaoAderencia.Boa, PercentualEmendado = 100 };
        double dVig = TemplateVigaPreMoldada.AlturaUtil(v);
        string nomeViga = string.IsNullOrWhiteSpace(v["Nome"]) ? "calculado" : v["Nome"].Trim();
        var propostosPorLado = new Dictionary<bool, TipoDente>();
        // só a análise do CAD (D51) propõe tipos novos de dente; no cálculo do Revit (D40) o dente é o do catálogo, conferido
        bool proporDente = analise != null;
        foreach (var (x, vd, lado, apoio, esq) in new[] { (e, r.VdE, "esquerda", r.ApoioE, true), (d, r.VdD, "direita", r.ApoioD, false) })
        {
            if (!x.TemDente) continue;
            var t0 = x.Dente;
            var t = t0.Clonar();
            string id = $"ponta {lado}, {t0.Nome}";
            double hd = t0.Altura, bMin = Math.Min(b, hd);
            double phiG = t0.EstGerberQtd > 0 ? t0.EstGerberBitola / 10 : 0;
            double a = Math.Max(0, t0.Comprimento - apoio + cobP);            // do eixo do apoio até a suspensão (face do recorte)
            double Hd = fHd * vd;

            // --- tirante: área pela relação a/d, mínimo ω ≥ 0,04, ø ≤ menor dimensão/6 e ≤ 25 mm, uma camada no topo
            (int N, double Phi, double D, double As) tir = (0, 0, 0, 0);
            double asTirNec = 0, adTir = 99;
            // bitolas pela menor área que atende (o usuário liberou ajustar o catálogo, 25/11/2026)
            foreach (double phi in BitolasLong.Where(f => f >= 10 - 1e-6 && f <= Math.Min(25, bMin * 10 / 6) + 1e-6))
            {
                double dd = hd - cobF - phiG - phi / 20;                       // do topo do dente ao tirante, junto da face de baixo
                double ad = dd > 0 ? a / dd : 99;
                double nec = (ad <= 0.5 ? 0.8 * vd / (fyd * 1.4) : (0.1 + Math.Min(ad, 1)) * vd / fyd) + Hd / fyd;
                nec = Math.Max(nec, 0.04 * b * dd * fck / TabelasNbr.Fyk(acoL));
                int n = Math.Max(2, (int)Math.Ceiling(nec / A(phi) - 1e-9));
                double pc = phi / 10, ahP = Math.Max(ah, pc);
                if (n > (int)Math.Floor((b - 2 * cobL - 2 * phiG + ahP) / (pc + ahP) + 1e-9)) continue;
                if (tir.N == 0 || n * A(phi) < tir.As - 1e-9) { tir = (n, phi, dd, n * A(phi)); asTirNec = nec; adTir = ad; }
            }
            if (tir.N == 0)
            {
                r.Avisos.Add($"Dente da {id}: o tirante não cabe numa camada (ø ≤ {N(Math.Min(25, bMin * 10 / 6), "0.#")} mm) — aumentar a largura ou a altura do dente.");
                r.Verificacoes.Add(K("K9", "Dente " + id, $"Tirante do dente da ponta {lado}", "não cabe", "uma camada no topo", true, "NBR 9062, 7.3.3.7"));
                continue;
            }
            double dd0 = tir.D;
            // comprimento: a ancoragem começa a (dvig − d) do primeiro estribo de suspensão (7.4.4.2), com má aderência
            double lbTir = regraMa.Lb(tir.Phi, acoL), lbTirNec = Math.Max(lbTir * asTirNec / tir.As, Math.Max(0.3 * lbTir, Math.Max(tir.Phi, 10)));
            double compTir = Math.Ceiling((t0.Comprimento + cobP + Math.Max(0, dVig - dd0) + lbTirNec - cobP) / 5 - 1e-9) * 5;
            t.TiranteQtd = tir.N; t.TiranteBitola = tir.Phi; t.TiranteComp = compTir;

            // --- suspensão: toda a Fd em estribos fechados (2 ramos), concentrados no trecho da face (≤ dvig/4, 7.4.6.2)
            double asSusp = vd / Math.Min(fywd, 43.5);
            double trecho = x.TrechoSuspensao > 0 ? x.TrechoSuspensao : cobP + dVig / 4;
            (int N, double Phi) sus = (0, 0);
            foreach (double phi in BitolasEst.Where(f => f >= 6.3 - 1e-6))   // estribos fechados: até 12,5 mm (dobra)
            {
                int n = Math.Max(2, (int)Math.Ceiling(asSusp / (2 * A(phi)) - 1e-9));
                double esp = (trecho - cobP - phi / 20) / n;                       // a regra de posições do template (D36)
                if (esp < Math.Max(5, phi / 10 + 2) - 1e-9) continue;              // folga para o concreto passar
                if (sus.N == 0 || n * A(phi) < sus.N * A(sus.Phi) - 1e-9) sus = (n, phi);
            }
            if (sus.N == 0)
            {
                r.Avisos.Add($"Dente da {id}: a suspensão ({N(asSusp)} cm²) não cabe no trecho de {N(trecho, "0")} cm junto da face — usar 4 ramos ou aumentar a altura da viga.");
                sus = (t0.SuspQtd, t0.SuspBitola);
            }
            t.SuspQtd = sus.N; t.SuspBitola = sus.Phi;

            // --- costura: em 2/3 d junto do tirante, (As/s) ≥ 0,5 (a/d ≤ 0,5) ou 0,4 · As,tir/d, mínimo 0,15 % b; ø ≤ menor/15,
            //     espaçamento vertical ≤ 10 cm e ≤ a; U horizontal (2 ramos) com as pernas 1,5·lb dentro da viga (7.4.5.1)
            double kCost = adTir <= 0.5 ? 0.5 : 0.4, zCost = 2.0 / 3.0 * dd0;
            double asCost = Math.Max(kCost * tir.As / dd0 * zCost, 0.0015 * b * zCost);
            int nivMin = Math.Max(1, (int)Math.Ceiling(zCost / Math.Min(10, Math.Max(a, 1)) - 1e-9));
            (int N, double Phi) cos = (0, 0);
            foreach (double phi in BitolasEst.Where(f => f >= 6.3 - 1e-6 && f <= bMin * 10 / 15 + 1e-6))
            {
                int n = Math.Max(nivMin, (int)Math.Ceiling(asCost / (2 * A(phi)) - 1e-9));
                if (cos.N == 0 || n * A(phi) < cos.N * A(cos.Phi) - 1e-9) cos = (n, phi);
            }
            if (cos.N == 0) cos = (Math.Max(nivMin, t0.CosturaQtd), t0.CosturaBitola);
            double lbCost = regraMa.Lb(cos.Phi, acoT);
            double pernaCost = Math.Ceiling((t0.Comprimento - cobP + 1.5 * lbCost) / 5 - 1e-9) * 5;
            t.CosturaQtd = cos.N; t.CosturaBitola = cos.Phi; t.CosturaPerna = pernaCost;

            // --- estribos verticais do dente: 0,15 % · b · h do dente (7.3.7.2), 2 ramos
            double asVert = 0.0015 * b * hd;
            (int N, double Phi) ger = (0, 0);
            int cabemG = Math.Max(2, (int)Math.Floor((t0.Comprimento - 2 * cobP) / 5 + 1e-9) + 1);   // no comprimento do dente, a ≥ 5 cm
            foreach (double phi in BitolasEst.Where(f => f >= 6.3 - 1e-6))
            {
                int n = Math.Max(2, (int)Math.Ceiling(asVert / (2 * A(phi)) - 1e-9));
                if (n > cabemG) continue;
                if (ger.N == 0 || n * A(phi) < ger.N * A(ger.Phi) - 1e-9) ger = (n, phi);
            }
            if (ger.N == 0) ger = (t0.EstGerberQtd, t0.EstGerberBitola);
            int nG = ger.N;
            t.EstGerberQtd = ger.N; t.EstGerberBitola = ger.Phi;

            // --- grampo: U no fundo, no canto do recorte, traspassando a inferior (pernas = traspasse, boa aderência). A força que ele
            //     precisa ancorar fica para o engenheiro conferir (a inferior já chega com a barra soldada na face do recorte).
            double phiGr = t0.GrampoBitola > 0 ? t0.GrampoBitola : 10;
            double l0t = regraBoa.Comprimento(phiGr, acoL);
            double pernaGr = Math.Ceiling((l0t + cobP) / 5 - 1e-9) * 5;
            t.GrampoPerna = pernaGr;
            double alV = vd > r.Vc + 1e-9 ? Math.Min(dV, Math.Max(0.5 * dV, dV * vd / (2 * (vd - r.Vc)))) : dV;
            double asApoio = alV / dV * vd / fyd + Hd / fyd, asGr = t0.GrampoQtd * 2 * A(phiGr);

            t.Nome = $"{t0.Nome} – {nomeViga}";
            t.Descricao = $"Calculado para a {nomeViga} (ponta {lado}), a partir do {t0.Nome}.";
            if (proporDente) propostosPorLado[esq] = t;

            m7.Linhas.Add($"Dente da {id}: Fd = Vd = {N(vd, "0.0")} kN; Hd = {N(fHd)}·Fd = {N(Hd, "0.0")} kN ({(neoprene ? "elastômero" : "argamassa")}, 7.3.9); a = {N(a, "0.0")} cm " +
                          $"(do eixo do apoio à face do recorte); d = {N(dd0, "0.0")} cm → a/d = {N(adTir, "0.00")} ({(adTir <= 0.5 ? "muito curto: μ = 1,4" : adTir <= 1 ? "curto" : "a/d > 1: viga — conferir")}).");
            m7.Linhas.Add($"Tirante: As = {(adTir <= 0.5 ? "0,8·Fd/(fyd·μ)" : "(0,1 + a/d)·Fd/fyd")} + Hd/fyd = {N(asTirNec)} cm² (ω ≥ 0,04 incluído) → {tir.N} ø{N(tir.Phi, "0.#")} = {N(tir.As)} cm² " +
                          $"(ø ≤ {N(Math.Min(25, bMin * 10 / 6), "0.#")} mm, 7.3.3.7; junto da face de baixo do dente, sobre o apoio: 7.3.3.11 e Fig. 23). Comprimento: dente {N(t0.Comprimento, "0")} + (dvig − d) = {N(Math.Max(0, dVig - dd0), "0")} " +
                          $"+ lb,nec = {N(lbTirNec, "0")} cm (má aderência, 7.4.4.2) → C = {N(compTir, "0")} cm, ancorado na ponta pela barra transversal soldada (7.4.4.1).");
            m7.Linhas.Add($"Suspensão: As = Fd/fyd = {N(asSusp)} cm² em estribos fechados no trecho de {N(trecho, "0")} cm junto da face (≤ dvig/4, 7.4.6) → {sus.N} ø{N(sus.Phi, "0.#")} (2 ramos) = {N(sus.N * 2 * A(sus.Phi))} cm².");
            m7.Linhas.Add($"Costura: (As/s) ≥ {N(kCost)}·As,tir/d numa faixa de 2/3·d = {N(zCost, "0")} cm acima do eixo do tirante (Fig. 23), mínimo 0,15 %·b → {N(asCost)} cm²; espaçamento ≤ 10 cm e ≤ a ({nivMin} níveis), ø ≤ {N(bMin * 10 / 15, "0.#")} mm → " +
                          $"{cos.N} U ø{N(cos.Phi, "0.#")} (2 ramos) = {N(cos.N * 2 * A(cos.Phi))} cm²; pernas = dente + 1,5·lb = {N(pernaCost, "0")} cm (7.4.5.1).");
            m7.Linhas.Add($"Estribos verticais do dente: 0,15 %·b·h = {N(asVert)} cm² → {nG} ø{N(ger.Phi, "0.#")} (7.3.7.2). Grampo no canto do recorte: pernas = traspasse com a inferior = {N(pernaGr, "0")} cm; " +
                          $"{t0.GrampoQtd} U ø{N(phiGr, "0.#")} = {N(asGr)} cm² (força a ancorar no canto: (aℓ/d)·Vd + Hd = {N(asApoio)} cm² de aço — conferir com a barra soldada da inferior).");
            var tem = proporDente ? t : t0;
            double asTirTem = tem.TiranteQtd * A(tem.TiranteBitola), asSusTem = tem.SuspQtd * 2 * A(tem.SuspBitola), asCosTem = tem.CosturaQtd * 2 * A(tem.CosturaBitola);
            bool faltaTir = asTirTem < asTirNec - 1e-9;
            r.Verificacoes.Add(K("K9", "Dente " + id, $"Tirante do dente da ponta {lado}", $"{tem.TiranteQtd} ø{N(tem.TiranteBitola, "0.#")} = {N(asTirTem)} cm²", $"≥ {N(asTirNec)} cm²",
                faltaTir || adTir > 1, "NBR 9062, 7.3.5 e 7.4.4", faltaTir ? SituacaoVerificacao.NaoAtende : SituacaoVerificacao.Conferir,
                faltaTir ? "Troque o dente no catálogo (ARMDENTES) por um com mais tirante." : adTir > 1 ? "a/d > 1: calcular o dente como viga (7.3.7.1)." : ""));
            r.Verificacoes.Add(K("K10", "Dente " + id, $"Armadura de suspensão do dente da ponta {lado}", $"{N(asSusTem)} cm²", $"≥ {N(asSusp)} cm²",
                asSusTem < asSusp - 1e-9, "NBR 9062, 7.4.6", dica: proporDente ? "Use 4 ramos ou aumente a altura da viga." : "Troque o dente no catálogo (ARMDENTES) por um com mais suspensão."));
            double vrd2d = 0.27 * av2 * fcd * b * dd0;
            r.Verificacoes.Add(K("K11", "Dente " + id, $"Compressão da biela do dente da ponta {lado} (estimativa: VRd2 com o d do dente)", $"Vd = {N(vd, "0.0")} kN", $"≤ {N(vrd2d, "0.0")} kN",
                true, "NBR 9062, 7.4.7", SituacaoVerificacao.Conferir, vd <= vrd2d ? "Atende a estimativa; confirme a biela pelo modelo do dente." : "Não atende a estimativa: aumente a altura do dente ou a largura."));
            r.Verificacoes.Add(K("K27", "Dente " + id, $"Costura do dente da ponta {lado}", $"{N(asCosTem)} cm²", $"≥ {N(asCost)} cm²", asCosTem < asCost - 1e-9, "NBR 9062, 7.3.6 e 7.4.5"));
            r.Verificacoes.Add(K("K28", "Dente " + id, $"Grampo no canto do recorte (ponta {lado})", $"{N(asGr)} cm²", $"força do canto ≈ {N(asApoio)} cm²", true, "NBR 6118, 18.3.2.4",
                SituacaoVerificacao.Conferir, "Confira quanto a barra soldada da inferior já ancora no canto; o grampo completa o resto."));
            if (!neoprene)
                r.Verificacoes.Add(K("K29", "Dente " + id, $"Força horizontal no apoio (ponta {lado})", $"Hd = {N(Hd, "0.0")} kN (0,5·Fd)", "estudo da ligação", true, "NBR 9062, 7.3.9 g",
                    SituacaoVerificacao.Conferir, "Ligação com solda ou graute: a norma pede o estudo detalhado da força horizontal."));
        }
        if (m7.Linhas.Count > 0) r.Memoria.Add(m7);
        if (propostosPorLado.TryGetValue(true, out var pe) && propostosPorLado.TryGetValue(false, out var pd) && pe.Nome == pd.Nome)
        {
            string Assinatura(TipoDente t) => $"{t.TiranteQtd}|{t.TiranteBitola}|{t.TiranteComp}|{t.SuspQtd}|{t.SuspBitola}|{t.CosturaQtd}|{t.CosturaBitola}|{t.CosturaPerna}|{t.EstGerberQtd}|{t.GrampoPerna}";
            if (Assinatura(pe) != Assinatura(pd)) { pe.Nome += " E"; pd.Nome += " D"; }
        }
        foreach (var t in propostosPorLado.Values.GroupBy(t => t.Nome).Select(g => g.First())) r.DentesPropostos.Add(t);
        r.DenteE = propostosPorLado.TryGetValue(true, out var tE) ? tE.Nome : null;
        r.DenteD = propostosPorLado.TryGetValue(false, out var tD) ? tD.Nome : null;

        // ---------------- 8. pele ----------------
        r.Pele = (0, 0);
        var pelePad = padrao.FirstOrDefault(l => l.Pele);
        if (h > 60 + 1e-9)
        {
            double asFace = 0.001 * b * h;
            double hLivre = h - cobF - cobT - 2 * phiTcm;
            int nEsp = (int)Math.Ceiling(hLivre / Math.Min(20, dV / 3) - 1e-9) - 1;
            (int, double) melhorP = (0, 0);
            foreach (double p in BitolasPele)
            {
                int n = Math.Max(nEsp, (int)Math.Ceiling(asFace / A(p) - 1e-9));
                if (melhorP.Item1 == 0 || n * A(p) < melhorP.Item1 * A(melhorP.Item2) - 1e-9) melhorP = (n, p);
            }
            if (pelePad != null && pelePad.Quantidade * A(pelePad.Bitola) >= melhorP.Item1 * A(melhorP.Item2) - 1e-9 && pelePad.Quantidade >= nEsp)
                melhorP = (pelePad.Quantidade, pelePad.Bitola);
            r.Pele = melhorP;
            r.Memoria.Add(new PassoMemoria
            {
                Titulo = "8. Armadura de pele (NBR 6118, 17.3.5.2.3)",
                Linhas =
                {
                    $"h = {N(h, "0.#")} cm > 60 cm: 0,10 %·b·h = {N(asFace)} cm² por face, espaçamento ≤ d/3 e ≤ 20 cm → {melhorP.Item1} ø{N(melhorP.Item2, "0.#")} por face.",
                },
            });
        }
        else if (pelePad != null) r.Pele = (pelePad.Quantidade, pelePad.Bitola);

        // ---------------- 9. ELS: flecha e fissuras ----------------
        if (r.Inferior != null)
        {
            var m9 = new PassoMemoria { Titulo = "9. Estados-limite de serviço (NBR 6118, 17.3)" };
            double eci = fck <= 50 ? k.AlfaE * 5600 * Math.Sqrt(fck) : 21500 * k.AlfaE * Math.Pow(fck / 10 + 1.25, 1.0 / 3);
            double ecs = Math.Min(1, 0.8 + 0.2 * fck / 80) * eci;   // MPa
            double es = 210000, dd = r.Inferior.D, As = r.Inferior.Area;
            double ic = b * Math.Pow(h, 3) / 12, mr = 1.5 * fctm * ic / (h / 2);   // kN·cm
            (double X, double I) EstII(double ae)
            {
                double aa = ae * As, x = (-aa + Math.Sqrt(aa * aa + 2 * b * aa * dd)) / b;
                return (x, b * x * x * x / 3 + aa * (dd - x) * (dd - x));
            }
            var qp = new EstaticaViga(L, xa, xb, (gTot + k.Psi2 * qTot) / 100, cargas.Concentradas.Select(p => (p.X, p.G + k.Psi2 * p.Q)));
            var fr = new EstaticaViga(L, xa, xb, (gTot + k.Psi1 * qTot) / 100, cargas.Concentradas.Select(p => (p.X, p.G + k.Psi1 * p.Q)));
            double ma = qp.Pontos().Max(qp.M);
            var (x2, i2) = EstII(es / ecs);
            double rel = ma > 0 ? Math.Min(1, Math.Pow(mr / ma, 3)) : 1;
            double ie = Math.Min(ic, rel * ic + (1 - rel) * i2);
            double ecKn = ecs / 10;   // kN/cm²
            r.FlechaImediata = qp.FlechaMeio(ecKn * ie);
            double Xi(double t) => t > 70 ? 2 : 0.68 * Math.Pow(0.996, t) * Math.Pow(t, 0.32);
            double rho2 = (capa > 0 ? 0 : Math.Max(asSupPadrao, r.Superior.N * A(r.Superior.Bitola))) / (b * dd);
            double af = (2 - Xi(k.IdadeCarregamentoMeses)) / (1 + 50 * rho2);
            r.FlechaTotal = r.FlechaImediata * (1 + af);
            double lim = r.Vao / k.FlechaLimite, limCf = r.Vao / 350;
            m9.Linhas.Add($"Ecs = αi·Eci = {N(ecs, "0")} MPa; Ic = {N(ic, "0")} cm⁴; Mr = 1,5·fctm·Ic/yt = {N(mr / 100, "0.0")} kN·m (17.3.1).");
            m9.Linhas.Add($"Quase permanente (ψ2 = {N(k.Psi2)}): Ma = {N(ma / 100, "0.0")} kN·m; estádio II: x = {N(x2, "0.0")} cm, III = {N(i2, "0")} cm⁴; Branson: Ie = {N(ie, "0")} cm⁴.");
            m9.Linhas.Add($"Flecha imediata = {N(r.FlechaImediata, "0.00")} cm; fluência: αf = Δξ/(1 + 50ρ') = {N(af, "0.00")} (t0 = {N(k.IdadeCarregamentoMeses, "0.#")} mês) → total = {N(r.FlechaTotal, "0.00")} cm; limite ℓ/{N(k.FlechaLimite, "0")} = {N(lim, "0.00")} cm.");
            bool passa = r.FlechaTotal <= lim + 1e-9;
            if (!passa && r.FlechaTotal - limCf <= lim + 1e-9)
            {
                r.Contraflecha = Math.Ceiling(Math.Min(limCf, r.FlechaTotal - lim + 0.1) * 2) / 2;   // meio centímetro
                m9.Linhas.Add($"Com contraflecha de {N(r.Contraflecha, "0.#")} cm (≤ ℓ/350 = {N(limCf, "0.00")} cm, 13.3) a flecha fica dentro do limite.");
            }
            r.Verificacoes.Add(K("K12", "Serviço", "Flecha total (imediata + diferida)", $"{N(r.FlechaTotal, "0.00")} cm", $"≤ ℓ/{N(k.FlechaLimite, "0")} = {N(lim, "0.00")} cm",
                !passa, "NBR 6118, 17.3.2.1 e 13.3", r.Contraflecha > 0 ? SituacaoVerificacao.Conferir : SituacaoVerificacao.NaoAtende,
                r.Contraflecha > 0 ? $"Atende com contraflecha de {N(r.Contraflecha, "0.#")} cm (proposta na nota da prancha)." : "Aumente a altura da viga."));

            // abertura de fissuras (combinação frequente)
            double mf = fr.Pontos().Max(fr.M);
            double wLim = TabelasNbr.Classe(v["ClasseAgressividade"]) switch { ClasseAgressividade.I => 0.4, ClasseAgressividade.IV => 0.2, _ => 0.3 };
            if (mf <= mr)
            {
                r.Wk = 0;
                m9.Linhas.Add($"Frequente (ψ1 = {N(k.Psi1)}): Md,ser = {N(mf / 100, "0.0")} kN·m ≤ Mr: a seção não fissura.");
            }
            else
            {
                var (xf, ifs) = EstII(15);
                double sig = 15 * mf * (dd - xf) / ifs * 10;   // MPa
                double phi = r.Inferior.Bitola, pc = phi / 10;
                double acr = Math.Min(b / Math.Max(1, r.Inferior.N1), 15 * pc) * Math.Min(cobF + phiTcm + pc / 2 + 7.5 * pc, h / 2);
                double rr = A(phi) / acr;
                double w1 = phi / (12.5 * 2.25) * sig / es * 3 * sig / (fctm * 10);
                double w2 = phi / (12.5 * 2.25) * sig / es * (4 / rr + 45);
                r.Wk = Math.Min(w1, w2);
                m9.Linhas.Add($"Frequente (ψ1 = {N(k.Psi1)}): Md,ser = {N(mf / 100, "0.0")} kN·m; σs = αe·M·(d − x)/III = {N(sig, "0")} MPa (αe = 15); ρr = {N(rr * 100, "0.00")} %.");
                m9.Linhas.Add($"wk = mín(φ/(12,5η1)·σs/Es·3σs/fctm; φ/(12,5η1)·σs/Es·(4/ρr + 45)) = mín({N(w1, "0.000")}; {N(w2, "0.000")}) = {N(r.Wk, "0.000")} mm.");
            }
            r.Verificacoes.Add(K("K13", "Serviço", "Abertura de fissuras (combinação frequente)", $"wk = {N(r.Wk, "0.00")} mm", $"≤ {N(wLim, "0.0")} mm (Tabela 13.4)",
                r.Wk > wLim + 1e-9, "NBR 6118, 17.3.3.2", dica: "Use mais barras de bitola menor na armadura inferior."));
            r.Memoria.Add(m9);
        }

        // ---------------- 10. torção de equilíbrio (carga na aba) ----------------
        if (st != null)
        {
            var m10 = new PassoMemoria { Titulo = "10. Torção de equilíbrio — laje apoiada na aba (NBR 6118, 17.5.1, 17.7.2 e 18.3.4)" };
            double xCrit = r.TdE >= r.TdD ? xa + 1e-6 : xb - 1e-6, vdCrit = r.TdE >= r.TdD ? r.VdE : r.VdD;
            double tdMax = r.TdMax * 100;   // kN·cm
            m10.Linhas.Add($"Excentricidade da carga da aba: e = {N(eAba, "0.0")} cm do eixo da alma" + (cargas.ExcAba > 0 ? " (digitada)" : " (b/2 + meio do balanço da aba)") +
                           (ladosAba == 1 ? $"; peso da aba {N(gAbaPP, "0.00")} kN/m com o CG a {N(ePP, "0.0")} cm." : "."));
            m10.Linhas.Add(ladosAba == 1
                ? $"Seção L: mt = γg·(g·e + g,aba·e,aba) + γq·q·e = {N(k.GamaG)}·{N(mtG / 100, "0.00")} + {N(k.GamaQ)}·{N(mtQ / 100, "0.00")} = {N((k.GamaG * mtG + k.GamaQ * mtQ) / 100, "0.00")} kN·m/m."
                : $"T invertido: as abas se equilibram em serviço; vale o pior entre a montagem com laje de um lado só (γg·g·e = {N(k.GamaG * mtG / 100, "0.00")} kN·m/m) " +
                  $"e a sobrecarga de um lado só (γq·q·e = {N(k.GamaQ * mtQ / 100, "0.00")} kN·m/m) → {(torcaoNaMontagem ? "montagem" : "sobrecarga")}.");
            m10.Linhas.Add($"Torção presa nos dois apoios (a ligação resiste a ela): Td = {N(r.TdE, "0.00")} kN·m no apoio esquerdo e {N(r.TdD, "0.00")} kN·m no direito, " +
                           $"diminuindo até o meio do trecho com aba.");
            double au = b * h / (2 * (b + h));
            m10.Linhas.Add($"Seção vazada equivalente da alma (17.5.1.4.1): A/u = {N(b, "0.#")}·{N(h, "0.#")}/(2·({N(b, "0.#")} + {N(h, "0.#")})) = {N(au, "0.0")} cm; 2c1 = {N(2 * st.C1, "0.0")} cm" +
                           (st.PelosCantos ? $" > A/u: he = A/u = {N(st.He, "0.0")} cm e Ae pelos eixos das barras dos cantos" : $": he = A/u = {N(st.He, "0.0")} cm") +
                           $" → Ae = {N(st.LadoB, "0.0")} × {N(st.LadoH, "0.0")} = {N(st.Ae, "0.0")} cm²; ue = {N(st.Ue, "0.0")} cm.");
            m10.Linhas.Add($"Bielas (θ = 45°, o mesmo do modelo I): TRd,2 = 0,5·αv2·fcd·Ae·he = {N(r.TRd2, "0.00")} kN·m (17.5.1.5).");
            double inter = vdCrit / r.VRd2 + tdMax / (r.TRd2 * 100);
            m10.Linhas.Add($"Cortante com torção (17.7.2.2): Vd/VRd2 + Td/TRd2 = {N(vdCrit, "0.0")}/{N(r.VRd2, "0.0")} + {N(r.TdMax, "0.00")}/{N(r.TRd2, "0.00")} = {N(inter, "0.00")} ≤ 1.");
            r.Verificacoes.Add(K("K14", "Torção", "Bielas com cortante e torção (Vd/VRd2 + Td/TRd2)", N(inter, "0.00"), "≤ 1", inter > 1 + 1e-9, "NBR 6118, 17.7.2.2",
                dica: "Aumente a largura da alma — na torção é o mais eficiente; aumentar a altura ajuda pouco."));

            // estribos: por ramo, cortante + torção
            r.As90 = TorcaoViga.As90PorCm(st, tdMax, fywd) * 100;
            double ramoNec = Math.Max(aswMin, Math.Max(0, vdCrit - r.Vc) / (0.9 * dV * fywd) + 2 * TorcaoViga.As90PorCm(st, tdMax, fywd)) / 2, ramoTem = A(phiEst) / sP;
            m10.Linhas.Add($"Estribos (17.5.1.6): A90/s = Td/(2·Ae·fywd) = {N(r.As90, "0.00")} cm²/m por ramo; somado ao cortante (17.7.2.2): " +
                           $"{N(ramoNec * 100, "0.00")} cm²/m por ramo; ø{N(phiEst, "0.#")} c/{N(sP, "0.#")} = {N(ramoTem * 100, "0.00")} cm²/m por ramo.");
            r.Verificacoes.Add(K("K15", "Torção", "Estribo por ramo nas pontas (cortante + torção)", $"{N(ramoTem * 100, "0.00")} cm²/m", $"≥ {N(ramoNec * 100, "0.00")} cm²/m",
                ramoTem < ramoNec - 1e-12, "NBR 6118, 17.5.1.6 e 17.7.2.2", dica: "Aumente a bitola do estribo ou use 4 ramos."));

            // longitudinal: Asℓ/ue no perímetro de Ae, somada à flexão em cada face (17.7.1.2), conferida no apoio (Td máximo)
            double asl = Math.Max(TorcaoViga.AslPorCm(st, tdMax, fyd), TorcaoViga.AslMinPorCm(st, fctm, fywk));
            r.AslPorM = asl * 100;
            double faceB = asl * st.LadoB, faceH = asl * st.LadoH;
            m10.Linhas.Add($"Longitudinal (17.5.1.6): Asℓ/ue = Td/(2·Ae·fyd) = {N(TorcaoViga.AslPorCm(st, tdMax, fyd) * 100, "0.00")} cm²/m; mínima 0,2·fctm/fywk·he = " +
                           $"{N(TorcaoViga.AslMinPorCm(st, fctm, fywk) * 100, "0.00")} cm²/m → por face: topo e fundo {N(faceB)} cm², laterais {N(faceH)} cm² cada; total {N(asl * st.Ue)} cm².");
            // fundo: a ancoragem no apoio (18.3.2.4) + a parcela da torção
            double alE = vdCrit > r.Vc + 1e-9 ? Math.Min(dV, Math.Max(0.5 * dV, dV * vdCrit / (2 * (vdCrit - r.Vc)))) : dV;
            double fundoNec = alE / dV * vdCrit / fyd + faceB, fundoTem = r.Inferior?.Area ?? 0;
            r.Verificacoes.Add(K("K16", "Torção", "Longitudinal do fundo no apoio (ancoragem + torção)", $"{N(fundoTem)} cm²", $"≥ {N(fundoNec)} cm²",
                fundoTem < fundoNec - 1e-9, "NBR 6118, 17.7.1.2", dica: "Leve mais barras inferiores até os apoios."));
            // no meio do trecho: flexão + o que sobra da torção
            double tdMeio = tdEm(r.XMdMax);
            if (tdMeio > 1e-6 && r.Inferior != null)
            {
                double meioNec = Math.Max(r.AsNec, r.AsMin) + Math.Max(TorcaoViga.AslPorCm(st, tdMeio, fyd), TorcaoViga.AslMinPorCm(st, fctm, fywk)) * st.LadoB;
                r.Verificacoes.Add(K("K21", "Torção", "Longitudinal do fundo no Md máximo (flexão + torção)", $"{N(r.Inferior.Area)} cm²", $"≥ {N(meioNec)} cm²",
                    r.Inferior.Area < meioNec - 1e-9, "NBR 6118, 17.7.1.2", dica: "Aumente a armadura inferior."));
            }
            // topo
            double topoTem = capa > 0 ? Math.Max(0, v.Inteiro("PortaEstriboQtd")) * A(v.Numero("PortaEstriboBitola")) : Math.Max(asSupPadrao, r.Superior.N * A(r.Superior.Bitola));
            if (capa <= 0 && topoTem < faceB - 1e-9)
            {
                var opc = BitolasLong.SelectMany(p => new[] { 2, 3 }.Select(n => (n, p))).Where(o => o.n * A(o.p) >= faceB)
                    .OrderBy(o => o.n * A(o.p)).ThenBy(o => o.n).FirstOrDefault();
                if (opc.n > 0) { r.Superior = opc; topoTem = opc.n * A(opc.p); }
            }
            r.Verificacoes.Add(K("K17", "Torção", capa > 0 ? "Longitudinal do topo no apoio (porta-estribo da peça)" : "Longitudinal do topo no apoio (torção)",
                $"{N(topoTem)} cm²", $"≥ {N(faceB)} cm²", topoTem < faceB - 1e-9, "NBR 6118, 17.5.1.6", capa > 0 ? SituacaoVerificacao.Conferir : SituacaoVerificacao.NaoAtende,
                capa > 0 ? "Na montagem a capa ainda não existe: aumente o porta-estribo da peça." : ""));
            // laterais: barras de pele por face, espaçadas até 35 cm (18.3.4)
            int nEspTor = Math.Max(0, (int)Math.Ceiling(st.LadoH / 35 - 1e-9) - 1);
            (int N, double P) peleTor = (0, 0);
            if (faceH > 1e-9)
                foreach (double p in BitolasPele)
                {
                    int n = Math.Max(Math.Max(nEspTor, 1), (int)Math.Ceiling(faceH / A(p) - 1e-9));
                    if (peleTor.N == 0 || n * A(p) < peleTor.N * A(peleTor.P) - 1e-9) peleTor = (n, p);
                }
            double peleTem = r.Pele.NPorFace * A(r.Pele.Bitola);
            if (peleTor.N > 0 && (peleTem < faceH - 1e-9 || r.Pele.NPorFace < Math.Max(nEspTor, 1)))
            {
                r.Pele = (Math.Max(peleTor.N, r.Pele.NPorFace), Math.Max(peleTor.P, r.Pele.Bitola));
                peleTem = r.Pele.NPorFace * A(r.Pele.Bitola);
                m10.Linhas.Add($"Faces laterais: {r.Pele.NPorFace} ø{N(r.Pele.Bitola, "0.#")} por face = {N(peleTem)} cm² ≥ {N(faceH)} cm² (barras a até 35 cm, 18.3.4; entram como pele).");
            }
            r.Verificacoes.Add(K("K18", "Torção", "Longitudinal das faces laterais (por face)", $"{N(peleTem)} cm²", $"≥ {N(faceH)} cm²", peleTem < faceH - 1e-9, "NBR 6118, 17.5.1.6 e 18.3.4"));
            // ligação e detalhe: sempre para o engenheiro conferir
            r.Verificacoes.Add(K("K19", "Torção", "Ligação no apoio resiste à torção (pino, chumbador, consolo)", $"Td = {N(r.TdMax, "0.00")} kN·m", "dimensionar a ligação",
                true, "NBR 9062, 7", SituacaoVerificacao.Conferir, "A torção de equilíbrio precisa de vínculo no apoio: confira a ligação e a estabilidade na montagem."));
            r.Verificacoes.Add(K("K20", "Torção", "Estribos fechados com ganchos a 45° (135°) e uma barra em cada canto", "detalhe da armação", "18.3.4",
                true, "NBR 6118, 18.3.4", SituacaoVerificacao.Conferir, "Confira o gancho dos estribos no desenho (o template ainda não troca o gancho sozinho)."));
            m10.Linhas.Add($"No meio do Md máximo a torção vale {N(tdMeio / 100, "0.00")} kN·m. A ligação no apoio deve resistir a Td = {N(r.TdMax, "0.00")} kN·m (NBR 9062).");
            r.Memoria.Add(m10);
        }

        // ---------------- proposta nos valores do template ----------------
        r.Proposta = Aplicar(v, r, padrao);
        return r;
    }

    /// <summary>Copia os valores e troca a armadura pela proposta (longitudinais, estribos, zonas, contraflecha, Vd/VRd2 das verificações).</summary>
    public static ValoresTemplate Aplicar(ValoresTemplate v, ResultadoCalculoViga r, List<LinhaLongViga> padrao)
    {
        var p = new ValoresTemplate();
        foreach (var kv in v.Todos) p[kv.Key] = kv.Value;
        var linhas = new List<string[]>();
        var infPad = v.Tabela("Longitudinais").FirstOrDefault(l => ValoresTemplate.Celula(l, 0).Equals(TemplateVigaPreMoldada.FInferior, StringComparison.OrdinalIgnoreCase));
        string dobE = infPad != null ? ValoresTemplate.Celula(infPad, 4) : "0", dobD = infPad != null ? ValoresTemplate.Celula(infPad, 5) : "0";
        if (r.Inferior != null)
        {
            linhas.Add(new[] { TemplateVigaPreMoldada.FInferior, r.Inferior.N1.ToString(), Fmt.N(r.Inferior.Bitola), "1", dobE, dobD, "", "" });
            if (r.Inferior.N2 > 0) linhas.Add(new[] { TemplateVigaPreMoldada.FInferior, r.Inferior.N2.ToString(), Fmt.N(r.Inferior.Bitola), "2", dobE, dobD, "", "" });
        }
        else linhas.AddRange(v.Tabela("Longitudinais").Where(l => ValoresTemplate.Celula(l, 0).Equals(TemplateVigaPreMoldada.FInferior, StringComparison.OrdinalIgnoreCase)));
        bool supTrocada = false;
        foreach (var l in v.Tabela("Longitudinais"))
        {
            string f = ValoresTemplate.Celula(l, 0);
            if (f.Equals(TemplateVigaPreMoldada.FInferior, StringComparison.OrdinalIgnoreCase) || f.Equals(TemplateVigaPreMoldada.FPele, StringComparison.OrdinalIgnoreCase)) continue;
            // com a análise, as negativas são as calculadas (as do template saem)
            if (r.ModelosAnalise != null && f.Equals(TemplateVigaPreMoldada.FNegativa, StringComparison.OrdinalIgnoreCase)) continue;
            if (f.Equals(TemplateVigaPreMoldada.FSuperior, StringComparison.OrdinalIgnoreCase) && r.Superior.N > 0 && !supTrocada)
            {
                double asPad = ValoresTemplate.NumeroCelula(l, 1) * A(ValoresTemplate.NumeroCelula(l, 2));
                if (r.Superior.N * A(r.Superior.Bitola) > asPad + 1e-9)
                {
                    var nl = (string[])l.Clone();
                    if (nl.Length < 8) Array.Resize(ref nl, 8);
                    nl[1] = r.Superior.N.ToString(); nl[2] = Fmt.N(r.Superior.Bitola);
                    linhas.Add(nl.Select(c => c ?? "").ToArray());
                    supTrocada = true;
                    continue;
                }
            }
            linhas.Add(l);
        }
        foreach (var ng in r.Negativas)
            linhas.Add(new[] { TemplateVigaPreMoldada.FNegativa, ng.N.ToString(), Fmt.N(ng.Bitola), ng.Camada.ToString(), "0", "0", Fmt.N(ng.Ini), Fmt.N(ng.Fim) });
        if (r.Pele.NPorFace > 0) linhas.Add(new[] { TemplateVigaPreMoldada.FPele, r.Pele.NPorFace.ToString(), Fmt.N(r.Pele.Bitola), "1", "0", "0", "", "" });
        p["Longitudinais"] = ValoresTemplate.JuntarTabela(linhas);
        if (r.EstBitola > 0)
        {
            p["EstBitola"] = Fmt.N(r.EstBitola);
            p["EstEspPonta"] = Fmt.N(r.EspPonta);
            p["EstEspVao"] = Fmt.N(r.EspVao);
            p["EstZonaPonta"] = Fmt.N(r.ZonaPonta);
        }
        if (r.AbaEstBitola > 0) { p["AbaEstBitola"] = Fmt.N(r.AbaEstBitola); p["AbaEstEsp"] = Fmt.N(r.AbaEstEsp); }
        if (r.DenteE != null) p["ExtE"] = r.DenteE;
        if (r.DenteD != null) p["ExtD"] = r.DenteD;
        if (r.Contraflecha > 0) p["Contraflecha"] = Fmt.N(r.Contraflecha);
        p[ChaveVdVRd2] = Fmt.N(r.VdSobreVRd2, "0.####");
        p[ChaveVdE] = Fmt.N(r.VdE, "0.##");
        p[ChaveVdD] = Fmt.N(r.VdD, "0.##");
        return p;
    }

    /// <summary>Valor oculto do template: Vd/VRd2 do cálculo (as verificações V6/V7 deixam de ser "Conferir").</summary>
    public const string ChaveVdVRd2 = "CalcVdVRd2";
    /// <summary>Valores ocultos: Vd (kN) junto a cada apoio — o Fd das regras do dente (D7).</summary>
    public const string ChaveVdE = "CalcVdE", ChaveVdD = "CalcVdD";

    private static ResultadoVerificacao K(string cod, string grupo, string desc, string valor, string limite, bool falha, string refer,
        SituacaoVerificacao seFalha = SituacaoVerificacao.NaoAtende, string dica = "") => new ResultadoVerificacao
    {
        Codigo = cod, Grupo = "Cálculo — " + grupo, Descricao = desc, Valor = valor, Limite = limite, Referencia = refer,
        Situacao = falha ? seFalha : SituacaoVerificacao.Ok, Dica = falha ? dica : "",
    };
}

/// <summary>Flexão simples de seção retangular no ELU (NBR 6118, 17.2.2: diagrama retangular; armadura dupla além de x/d limite).</summary>
public sealed class Flexao
{
    public double Fck { get; }
    public double Fcd { get; }   // kN/cm²
    public double Fyd { get; }   // kN/cm²
    public double Lambda { get; }
    public double AlfaC { get; }
    public double XdLim { get; }

    public Flexao(double fck, double fcd, double fyd)
    {
        Fck = fck; Fcd = fcd; Fyd = fyd;
        Lambda = fck <= 50 ? 0.8 : 0.8 - (fck - 50) / 400;
        AlfaC = fck <= 50 ? 0.85 : 0.85 * (1 - (fck - 50) / 200);
        XdLim = fck <= 50 ? 0.45 : 0.35;
    }

    /// <summary>Armadura para o momento <paramref name="md"/> (kN·cm): As tracionada, As' comprimida (cm²) e a linha neutra.</summary>
    public (double As, double AsCompr, double X, double XsobreD) Armadura(double md, double b, double d, double dLinha)
    {
        if (md <= 0 || d <= 0) return (0, 0, 0, 0);
        double xl = XdLim * d;
        double mlim = AlfaC * Fcd * b * Lambda * xl * (d - Lambda * xl / 2);
        if (md <= mlim)
        {
            double x = d / Lambda * (1 - Math.Sqrt(Math.Max(0, 1 - 2 * md / (AlfaC * Fcd * b * d * d))));
            return (md / (Fyd * (d - Lambda * x / 2)), 0, x, x / d);
        }
        double asl = (md - mlim) / (Fyd * Math.Max(1, d - dLinha));
        return (mlim / (Fyd * (d - Lambda * xl / 2)) + asl, asl, xl, XdLim);
    }
}
