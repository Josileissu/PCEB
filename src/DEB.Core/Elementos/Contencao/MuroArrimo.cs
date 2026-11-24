using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao.Projeto;
using DEB.Core.Documentacao;
using DEB.Core.Elementos.Viga.PreMoldada.Calculo;
using DEB.Core.Normas.Verificacao;

namespace DEB.Core.Elementos.Contencao;

public enum MetodoEmpuxo { Rankine, Coulomb }

/// <summary>
/// Dados de um muro de arrimo em balanço de concreto armado (D47), por metro de muro. Unidades: m, kN, kPa (kN/m²), kN/m³ e graus.
/// Geometria no plano da seção: x da borda da ponta (frente) para o talão; y da face inferior da sapata para cima; o solo contido
/// fica do lado do talão. O fuste tem a face de trás vertical e a da frente inclinada (espessura do topo à base).
/// </summary>
public sealed class DadosMuro
{
    // ---- geometria (m)
    /// <summary>Altura do fuste, do topo da sapata ao topo do muro.</summary>
    public double AlturaFuste { get; set; } = 3.5;
    public double EspessuraTopo { get; set; } = 0.20;
    public double EspessuraBase { get; set; } = 0.35;
    /// <summary>Comprimento da ponta (frente do fuste até a borda da sapata do lado de fora).</summary>
    public double Ponta { get; set; } = 0.70;
    /// <summary>Comprimento do talão (face de trás do fuste até a borda da sapata do lado do solo).</summary>
    public double Talao { get; set; } = 1.65;
    public double EspessuraSapata { get; set; } = 0.40;
    /// <summary>Altura do terreno na frente, medida da face inferior da sapata (≥ espessura da sapata = terreno sobre a ponta).</summary>
    public double TerrenoFrente { get; set; } = 0.60;

    // ---- aterro (solo contido)
    public double GamaSolo { get; set; } = 18;
    public double GamaSat { get; set; } = 20;
    public double PhiSolo { get; set; } = 30;
    /// <summary>Inclinação do aterro sobre o muro (graus, 0 = horizontal).</summary>
    public double Beta { get; set; } = 0;
    /// <summary>Atrito solo-muro (graus), usado só por Coulomb.</summary>
    public double Delta { get; set; } = 20;
    /// <summary>Sobrecarga variável sobre o aterro (kPa).</summary>
    public double Sobrecarga { get; set; } = 10;
    /// <summary>Nível d'água atrás do muro, acima da face inferior da sapata (m); 0 = drenado.</summary>
    public double NivelAgua { get; set; } = 0;
    public MetodoEmpuxo Metodo { get; set; } = MetodoEmpuxo.Rankine;

    // ---- solo de fundação
    public double GamaFundacao { get; set; } = 18;
    public double PhiFundacao { get; set; } = 30;
    public double CoesaoFundacao { get; set; } = 0;
    /// <summary>Tensão admissível da sondagem (kPa); 0 = só a capacidade de carga calculada.</summary>
    public double TensaoAdmissivel { get; set; } = 0;
    /// <summary>Fração do empuxo passivo na frente considerada no deslizamento (0 = desprezado).</summary>
    public double FracaoPassivo { get; set; } = 0;
    /// <summary>Dente sob a sapata (m): altura abaixo da face inferior (0 = sem dente), largura e início medido da borda da ponta.</summary>
    public double DenteAltura { get; set; } = 0;
    public double DenteLargura { get; set; } = 0.30;
    public double DenteInicio { get; set; } = 0.70;
    /// <summary>Fração do passivo mobilizado na face do dente (abaixo da base: não se escava).</summary>
    public double FracaoDente { get; set; } = 1.0;

    public double Base => Ponta + EspessuraBase + Talao;
    public double AlturaTotal => EspessuraSapata + AlturaFuste;

    /// <summary>Valores de partida da configuração do projeto (grupo "solo").</summary>
    public static DadosMuro DaConfiguracao(ConfiguracaoProjeto cfg)
    {
        cfg ??= new ConfiguracaoProjeto();
        return new DadosMuro
        {
            GamaSolo = cfg.Numero("solo.gama"), GamaSat = cfg.Numero("solo.gamaSat"), PhiSolo = cfg.Numero("solo.phi"),
            Delta = cfg.Numero("solo.delta"), Sobrecarga = cfg.Numero("solo.sobrecarga"),
            GamaFundacao = cfg.Numero("solo.gamaFund"), PhiFundacao = cfg.Numero("solo.phiFund"), CoesaoFundacao = cfg.Numero("solo.cFund"),
            TensaoAdmissivel = cfg.Numero("solo.sigmaAdm"), FracaoPassivo = cfg.Numero("solo.fracaoPassivo"), FracaoDente = cfg.Numero("solo.fracaoDente"),
            Metodo = cfg.Texto("solo.metodo").StartsWith("Coulomb") ? MetodoEmpuxo.Coulomb : MetodoEmpuxo.Rankine,
        };
    }

    /// <summary>Erros de dados que impedem o cálculo (vazio = ok).</summary>
    public List<string> Erros()
    {
        var e = new List<string>();
        if (AlturaFuste <= 0) e.Add("altura do fuste deve ser positiva");
        if (EspessuraTopo <= 0 || EspessuraBase < EspessuraTopo) e.Add("espessura do fuste: topo > 0 e base ≥ topo");
        if (EspessuraSapata <= 0) e.Add("espessura da sapata deve ser positiva");
        if (Ponta < 0 || Talao < 0) e.Add("ponta e talão não podem ser negativos");
        if (PhiSolo <= 0 || PhiSolo >= 50) e.Add("φ do aterro fora de 0° a 50°");
        if (Beta < 0 || Beta >= PhiSolo) e.Add("inclinação do aterro deve ser menor que φ (Rankine/Coulomb sem solução)");
        if (NivelAgua < 0 || NivelAgua > AlturaTotal + 1e-9) e.Add("nível d'água entre a base e o topo do muro");
        if (TerrenoFrente < 0) e.Add("terreno na frente não pode ser negativo");
        if (DenteAltura < 0 || (DenteAltura > 0 && (DenteLargura <= 0 || DenteInicio < 0 || DenteInicio + DenteLargura > Base + 1e-9)))
            e.Add("dente: altura ≥ 0 e largura dentro da base");
        return e;
    }
}

/// <summary>Uma força por metro de muro: componentes (kN/m), ponto de aplicação (m) e o que é (para a janela e o memorial).</summary>
public sealed class ForcaMuro
{
    public string Nome { get; set; } = "";
    public double Fx { get; set; }
    public double Fy { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Peso, Empuxo, Agua, Sobrecarga, Passivo.</summary>
    public string Tipo { get; set; } = "";
    /// <summary>Variável (sobrecarga): entra só onde desfavorece.</summary>
    public bool Variavel { get; set; }
    /// <summary>Momento em relação à borda da ponta (x = 0, y = 0), positivo tombando o muro para a frente (kN·m/m).</summary>
    public double MomentoTombamento => X * Fy - Y * Fx;
}

/// <summary>Uma parte armada do muro (fuste, talão, ponta) dimensionada por metro.</summary>
public sealed class ParteMuro
{
    public string Nome { get; set; } = "";
    public string Face { get; set; } = "";
    public double Espessura { get; set; }   // m
    public double D { get; set; }           // cm
    public double Mk { get; set; }          // kN·m/m
    public double Md { get; set; }
    public double Vd { get; set; }          // kN/m
    public double VRd1 { get; set; }
    public double As { get; set; }          // cm²/m (cálculo)
    public double AsMin { get; set; }
    public double AsDistribuicao { get; set; }
    public double XsobreD { get; set; }
    public string Arranjo { get; set; } = "";
    public string ArranjoDistribuicao { get; set; } = "";
}

/// <summary>Resultado do muro: forças, estabilidade, tensões no solo, armaduras, diagramas e verificações MA1…MA9.</summary>
public sealed class ResultadoMuro
{
    public DadosMuro Dados { get; set; }
    public List<ForcaMuro> Forcas { get; } = new List<ForcaMuro>();
    public double Ka { get; set; }
    public double Kp { get; set; }
    public double FsTombamento { get; set; }
    public double FsDeslizamento { get; set; }
    public double FsCapacidade { get; set; }
    public double Excentricidade { get; set; }
    public double SigmaMax { get; set; }
    public double SigmaMin { get; set; }
    /// <summary>Comprimento comprimido da base (m) — menor que B quando a resultante sai do núcleo central.</summary>
    public double BaseComprimida { get; set; }
    public double CapacidadeCarga { get; set; }
    public double NormalBase { get; set; }
    public double HorizontalBase { get; set; }
    public List<ParteMuro> Partes { get; } = new List<ParteMuro>();
    /// <summary>Diagrama do fuste: (y acima do topo da sapata, pressão horizontal kPa, Vk kN/m, Mk kN·m/m).</summary>
    public List<(double Y, double P, double V, double M)> DiagramaFuste { get; } = new List<(double, double, double, double)>();
    /// <summary>Empuxo no plano vertical pelo fim do talão: (y, pressão horizontal do solo e sobrecarga, da água) kPa.</summary>
    public List<(double Y, double PSolo, double PAgua)> DiagramaEmpuxo { get; } = new List<(double, double, double)>();
    public ListaVerificacoes Verificacoes { get; } = new ListaVerificacoes();
    public List<PassoMemoria> Memoria { get; } = new List<PassoMemoria>();
    public SituacaoVerificacao Situacao => Verificacoes.Count == 0 ? SituacaoVerificacao.Conferir : Verificacoes.Geral;
}

/// <summary>
/// Muro de arrimo em balanço (D47, primeira contenção do PCEB). Empuxo ativo por Rankine (aterro inclinado, empuxo paralelo ao
/// terreno) ou Coulomb (atrito δ), no plano vertical pelo fim do talão — o solo sobre o talão é parte do muro; sobrecarga e água
/// (pressão neutra, γ submerso, subpressão na base) integradas por fatias. Estabilidade: tombamento em torno da borda da ponta,
/// deslizamento na base (tg φ e adesão reduzidos, passivo opcional), excentricidade e tensões na base, capacidade de carga de
/// Meyerhof/Vesic com base efetiva B' = B − 2e e fatores de inclinação. Peças: fuste, talão e ponta em balanço, flexão pela
/// NBR 6118 17.2 (b = 1 m), cortante sem armadura transversal (19.4.1), armadura mínima (17.3.5.2.1) e de distribuição (20 %).
/// </summary>
public static class CalculoMuro
{
    private const double GamaAgua = 10;
    private const int Fatias = 400;
    private static string N(double x, string f = "0.##") => Fmt.Virgula(x, f);
    private static double Rad(double g) => g * Math.PI / 180;

    // ---------------------------------------------------------------- coeficientes de empuxo

    /// <summary>Rankine com aterro inclinado β (empuxo paralelo ao terreno; Das, Principles of Geotechnical Engineering, 13.9).</summary>
    public static double KaRankine(double phi, double beta)
    {
        double cb = Math.Cos(Rad(beta)), cp = Math.Cos(Rad(phi));
        double r = Math.Sqrt(Math.Max(0, cb * cb - cp * cp));
        return cb * (cb - r) / (cb + r);
    }

    /// <summary>Coulomb, face de trás vertical, aterro inclinado β e atrito δ (empuxo inclinado de δ com a horizontal).</summary>
    public static double KaCoulomb(double phi, double beta, double delta)
    {
        double f = Rad(phi), b = Rad(beta), d = Rad(delta);
        double s = Math.Sqrt(Math.Max(0, Math.Sin(f + d) * Math.Sin(f - b) / (Math.Cos(d) * Math.Cos(b))));
        return Math.Cos(f) * Math.Cos(f) / (Math.Cos(d) * Math.Pow(1 + s, 2));
    }

    public static double KpRankine(double phi) => Math.Pow(Math.Tan(Rad(45 + phi / 2)), 2);

    /// <summary>Fatores de capacidade de carga (Vesic): Nc, Nq, Nγ.</summary>
    public static (double Nc, double Nq, double Ng) FatoresCapacidade(double phi)
    {
        if (phi < 1e-6) return (5.14, 1, 0);
        double t = Math.Tan(Rad(phi));
        double nq = Math.Exp(Math.PI * t) * Math.Pow(Math.Tan(Rad(45 + phi / 2)), 2);
        return ((nq - 1) / t, nq, 2 * (nq + 1) * t);
    }

    // ---------------------------------------------------------------- cálculo

    public static ResultadoMuro Calcular(DadosMuro d, ConfiguracaoProjeto cfg = null)
    {
        cfg ??= new ConfiguracaoProjeto();
        var r = new ResultadoMuro { Dados = d };
        var erros = d.Erros();
        if (erros.Count > 0)
        {
            foreach (string e in erros)
                r.Verificacoes.Add(new ResultadoVerificacao { Codigo = "MA0", Grupo = "Muro — dados", Descricao = "Dados do muro: " + e,
                    Situacao = SituacaoVerificacao.NaoAtende, Referencia = "—" });
            return r;
        }

        double gc = cfg.Numero("materiais.pesoEspecifico");
        double B = d.Base, es = d.EspessuraSapata, H = d.AlturaTotal, h = d.AlturaFuste;
        double xf = d.Ponta, xt = d.Ponta + d.EspessuraBase;      // frente do fuste na base; face de trás
        double tb = Math.Tan(Rad(d.Beta));
        double hv = H + d.Talao * tb;                               // altura do plano vertical pelo fim do talão

        // ---- pesos (kN/m) e pontos de aplicação
        void Peso(string nome, double area, double x, double y, double gama, string tipo = "Peso") =>
            r.Forcas.Add(new ForcaMuro { Nome = nome, Fy = -area * gama, X = x, Y = y, Tipo = tipo });
        double ttop = d.EspessuraTopo, tbase = d.EspessuraBase;
        // fuste: retângulo do topo (encostado na face de trás) + triângulo da inclinação da frente
        Peso("Fuste (retângulo)", ttop * h, xt - ttop / 2, es + h / 2, gc);
        if (tbase > ttop) Peso("Fuste (inclinação)", (tbase - ttop) * h / 2, xf + 2 * (tbase - ttop) / 3, es + h / 3, gc);
        Peso("Sapata", B * es, B / 2, es / 2, gc);
        if (d.DenteAltura > 0) Peso("Dente", d.DenteLargura * d.DenteAltura, d.DenteInicio + d.DenteLargura / 2, -d.DenteAltura / 2, gc);
        // solo sobre o talão: retângulo até o topo + triângulo do aterro inclinado; abaixo do NA com γsat
        if (d.Talao > 0)
        {
            double ySeco0 = Math.Max(es, d.NivelAgua);
            if (d.NivelAgua > es) Peso("Solo saturado sobre o talão", d.Talao * (Math.Min(d.NivelAgua, H) - es), xt + d.Talao / 2, (es + Math.Min(d.NivelAgua, H)) / 2, d.GamaSat, "Solo");
            if (H > ySeco0) Peso("Solo sobre o talão", d.Talao * (H - ySeco0), xt + d.Talao / 2, (ySeco0 + H) / 2, d.GamaSolo, "Solo");
            if (tb > 0) Peso("Solo do aterro inclinado", d.Talao * d.Talao * tb / 2, xt + 2 * d.Talao / 3, H + d.Talao * tb / 3, d.GamaSolo, "Solo");
            if (d.Sobrecarga > 0)
                r.Forcas.Add(new ForcaMuro { Nome = "Sobrecarga sobre o talão", Fy = -d.Sobrecarga * d.Talao, X = xt + d.Talao / 2, Y = hv, Tipo = "Sobrecarga", Variavel = true });
        }
        if (d.TerrenoFrente > es && d.Ponta > 0)
            Peso("Solo sobre a ponta", d.Ponta * (d.TerrenoFrente - es), d.Ponta / 2, (es + d.TerrenoFrente) / 2, d.GamaFundacao, "Solo");

        // ---- empuxo no plano vertical x = B (fatias): solo e sobrecarga paralelos ao terreno (Rankine) ou a δ (Coulomb); água horizontal
        double ka = d.Metodo == MetodoEmpuxo.Rankine ? KaRankine(d.PhiSolo, d.Beta) : KaCoulomb(d.PhiSolo, d.Beta, d.Delta);
        double ang = d.Metodo == MetodoEmpuxo.Rankine ? Rad(d.Beta) : Rad(d.Delta);
        r.Ka = ka;
        var (eS, yS) = Integrar(d, hv, 0, ka, false, r.DiagramaEmpuxo);
        var (eQ, yQ) = Integrar(d, hv, 0, ka, true, null);
        r.Forcas.Add(new ForcaMuro { Nome = "Empuxo do solo", Fx = -eS * Math.Cos(ang), Fy = -eS * Math.Sin(ang), X = B, Y = yS, Tipo = "Empuxo" });
        if (eQ > 0) r.Forcas.Add(new ForcaMuro { Nome = "Empuxo da sobrecarga", Fx = -eQ * Math.Cos(ang), Fy = -eQ * Math.Sin(ang), X = B, Y = yQ, Tipo = "Sobrecarga", Variavel = true });
        if (d.NivelAgua > 0)
        {
            double hw = d.NivelAgua;
            r.Forcas.Add(new ForcaMuro { Nome = "Água (pressão neutra)", Fx = -GamaAgua * hw * hw / 2, X = B, Y = hw / 3, Tipo = "Agua" });
            // subpressão: γw·hw no fim do talão, zero na borda da ponta (frente drenada)
            r.Forcas.Add(new ForcaMuro { Nome = "Subpressão na base", Fy = GamaAgua * hw * B / 2, X = 2 * B / 3, Y = 0, Tipo = "Agua" });
        }
        // passivo na frente (só a fração pedida, só no deslizamento)
        r.Kp = KpRankine(d.PhiFundacao);
        double df = d.TerrenoFrente, dd = df + d.DenteAltura, sKp = Math.Sqrt(r.Kp), cf = d.CoesaoFundacao;
        double ppFrente = d.FracaoPassivo * (0.5 * r.Kp * d.GamaFundacao * df * df + 2 * cf * sKp * df);
        double ppDente = d.DenteAltura > 0 ? d.FracaoDente * (0.5 * r.Kp * d.GamaFundacao * (dd * dd - df * df) + 2 * cf * sKp * d.DenteAltura) : 0;
        double pp = ppFrente + ppDente;

        // ---- estabilidade (valores característicos)
        var semQ = r.Forcas.Where(f => !(f.Variavel && f.Fy != 0)).ToList();   // sobrecarga sobre o talão é favorável: fora
        double mTomb = semQ.Where(f => f.MomentoTombamento > 0).Sum(f => f.MomentoTombamento);
        double mRes = -semQ.Where(f => f.MomentoTombamento < 0).Sum(f => f.MomentoTombamento);
        r.FsTombamento = mTomb > 1e-9 ? mRes / mTomb : double.PositiveInfinity;

        double nBase = -semQ.Sum(f => f.Fy), hBase = -semQ.Sum(f => f.Fx);
        double k1 = cfg.Numero("solo.k1"), k2 = cfg.Numero("solo.k2");
        double resist = Math.Max(0, nBase) * Math.Tan(Rad(k1 * d.PhiFundacao)) + k2 * d.CoesaoFundacao * B + pp;
        r.FsDeslizamento = hBase > 1e-9 ? resist / hBase : double.PositiveInfinity;

        // base: com a sobrecarga sobre o talão (o caso de maior tensão é o com todas as forças)
        double TensaoCaso(List<ForcaMuro> fs, out double e, out double smax, out double smin, out double bc, out double nb, out double hb)
        {
            nb = -fs.Sum(f => f.Fy); hb = -fs.Sum(f => f.Fx);
            double m0 = fs.Sum(f => f.MomentoTombamento);           // momento em torno da borda da ponta
            double xr = nb > 1e-9 ? -m0 / nb : 0;                   // posição da resultante a partir da ponta
            e = B / 2 - xr;
            if (Math.Abs(e) <= B / 6) { smax = nb / B * (1 + 6 * Math.Abs(e) / B); smin = nb / B * (1 - 6 * Math.Abs(e) / B); bc = B; }
            else { bc = 3 * Math.Max(1e-6, B / 2 - Math.Abs(e)); smax = 2 * nb / bc; smin = 0; }
            return smax;
        }
        TensaoCaso(r.Forcas, out double eT, out double sMaxT, out double sMinT, out double bcT, out double nT, out double hT);
        TensaoCaso(semQ, out double eS2, out double sMaxS, out double sMinS, out double bcS, out double nS, out double hS);
        bool usarTodos = sMaxT >= sMaxS;
        r.Excentricidade = usarTodos ? eT : eS2; r.SigmaMax = usarTodos ? sMaxT : sMaxS; r.SigmaMin = usarTodos ? sMinT : sMinS;
        r.BaseComprimida = usarTodos ? bcT : bcS; r.NormalBase = usarTodos ? nT : nS; r.HorizontalBase = usarTodos ? hT : hS;
        // capacidade de carga (Meyerhof/Vesic) na base efetiva, sem fatores de profundidade (a favor da segurança)
        {
            double e = Math.Abs(r.Excentricidade), bEf = Math.Max(1e-6, B - 2 * e), n = r.NormalBase;
            double theta = n > 1e-9 ? Math.Atan(Math.Abs(r.HorizontalBase) / n) * 180 / Math.PI : 0;
            var (nc, nq, ng) = FatoresCapacidade(d.PhiFundacao);
            double ic = Math.Pow(Math.Max(0, 1 - theta / 90), 2), ig = d.PhiFundacao > 0 ? Math.Pow(Math.Max(0, 1 - theta / d.PhiFundacao), 2) : 0;
            double q0 = d.GamaFundacao * d.TerrenoFrente;
            double qu = d.CoesaoFundacao * nc * ic + q0 * nq * ic + 0.5 * d.GamaFundacao * bEf * ng * ig;
            r.CapacidadeCarga = qu;
            r.FsCapacidade = n > 1e-9 ? qu * bEf / n : double.PositiveInfinity;
            var mem = new PassoMemoria { Titulo = "Capacidade de carga da base (Meyerhof/Vesic)" };
            mem.Linhas.Add($"B' = B − 2e = {N(B)} − 2·{N(e, "0.###")} = {N(bEf, "0.###")} m; inclinação da resultante θ = {N(theta, "0.#")}°.");
            mem.Linhas.Add($"φ = {N(d.PhiFundacao)}°: Nc = {N(nc, "0.##")}, Nq = {N(nq, "0.##")}, Nγ = {N(ng, "0.##")}; ic = iq = (1 − θ/90°)² = {N(ic, "0.###")}, iγ = (1 − θ/φ)² = {N(ig, "0.###")}.");
            mem.Linhas.Add($"qu = c·Nc·ic + q·Nq·iq + ½·γ·B'·Nγ·iγ = {N(qu, "0")} kPa (q = γ·D = {N(q0, "0.#")} kPa; sem fatores de profundidade e de forma — muro corrido).");
            mem.Linhas.Add($"FS = qu·B'/N = {N(qu, "0")}·{N(bEf, "0.###")}/{N(n, "0.#")} = {N(r.FsCapacidade, "0.00")}.");
            r.Memoria.Add(mem);
        }

        // ---- memória da estabilidade
        {
            var mem = new PassoMemoria { Titulo = "Empuxo e estabilidade do muro (por metro)" };
            mem.Linhas.Add($"Geometria: fuste h = {N(h)} m ({N(ttop)} a {N(tbase)} m), sapata B = {N(B)} m × {N(es)} m (ponta {N(d.Ponta)} m, talão {N(d.Talao)} m); terreno na frente a {N(d.TerrenoFrente)} m da base.");
            string met = d.Metodo == MetodoEmpuxo.Rankine
                ? $"Rankine com aterro a β = {N(d.Beta)}°: Ka = cos β·(cos β − √(cos²β − cos²φ))/(cos β + √(cos²β − cos²φ)) = {N(ka, "0.####")}; empuxo paralelo ao terreno"
                : $"Coulomb (face vertical, δ = {N(d.Delta)}°, β = {N(d.Beta)}°): Ka = {N(ka, "0.####")}; empuxo inclinado de δ";
            mem.Linhas.Add($"{met}; aterro γ = {N(d.GamaSolo)} kN/m³, φ = {N(d.PhiSolo)}°, no plano vertical pelo fim do talão (altura {N(hv, "0.###")} m) — o solo sobre o talão é parte do muro.");
            if (d.NivelAgua > 0) mem.Linhas.Add($"Água a {N(d.NivelAgua)} m da base: γ submerso = γsat − γw abaixo do NA, pressão neutra γw·z e subpressão triangular na base (frente drenada).");
            foreach (var f in r.Forcas)
            {
                var partes = new List<string>();
                if (Math.Abs(f.Fx) > 1e-9) partes.Add($"H = {N(-f.Fx, "0.##")} kN/m");
                if (Math.Abs(f.Fy) > 1e-9) partes.Add(f.Fy > 0 ? $"V = {N(f.Fy, "0.##")} kN/m para cima" : $"V = {N(-f.Fy, "0.##")} kN/m");
                mem.Linhas.Add($"  {f.Nome}: {string.Join(", ", partes)} em x = {N(f.X, "0.###")} m, y = {N(f.Y, "0.###")} m" + (f.Variavel ? " (variável)" : ""));
            }
            mem.Linhas.Add($"Tombamento (em torno da borda da ponta, sem a sobrecarga sobre o talão): Mres = {N(mRes, "0.#")} kN·m/m, Mtomb = {N(mTomb, "0.#")} kN·m/m → FS = {N(r.FsTombamento, "0.00")}.");
            mem.Linhas.Add($"Deslizamento: N·tg({N(k1, "0.##")}·φ) + {N(k2, "0.##")}·c·B" + (ppFrente > 0 ? $" + {N(d.FracaoPassivo, "0.##")}·Ep da frente ({N(ppFrente, "0.#")} kN/m)" : "") +
                            (ppDente > 0 ? $" + {N(d.FracaoDente, "0.##")}·Ep no dente de {N(d.DenteAltura)} m ({N(ppDente, "0.#")} kN/m)" : "") +
                            (pp > 0 ? $" (Kp = tg²(45° + φ/2) = {N(r.Kp, "0.##")})" : "") +
                            $" = {N(resist, "0.#")} kN/m contra H = {N(hBase, "0.#")} kN/m → FS = {N(r.FsDeslizamento, "0.00")}.");
            mem.Linhas.Add($"Base ({(usarTodos ? "com" : "sem")} a sobrecarga sobre o talão, o caso de maior tensão): N = {N(r.NormalBase, "0.#")} kN/m, e = {N(r.Excentricidade, "0.###")} m " +
                            $"(B/6 = {N(B / 6, "0.###")} m) → σmáx = {N(r.SigmaMax, "0.#")} kPa, σmín = {N(r.SigmaMin, "0.#")} kPa" +
                            (r.BaseComprimida < B - 1e-9 ? $", base comprimida {N(r.BaseComprimida, "0.##")} m." : "."));
            r.Memoria.Insert(0, mem);
        }

        // ---- verificações de estabilidade
        double fsT = cfg.Numero("solo.fsTombamento"), fsD = cfg.Numero("solo.fsDeslizamento"), fsC = cfg.Numero("solo.fsCapacidade");
        string refFs = "NBR 11682 (conferir na edição vigente)";
        K(r, "MA1", "Segurança ao tombamento", $"FS = {N(r.FsTombamento, "0.00")}", $"≥ {N(fsT, "0.0")}", r.FsTombamento < fsT, refFs, dica: "Aumente o talão ou a base.");
        K(r, "MA2", "Segurança ao deslizamento", $"FS = {N(r.FsDeslizamento, "0.00")}", $"≥ {N(fsD, "0.0")}", r.FsDeslizamento < fsD, refFs,
          dica: "Aumente a base, faça um dente sob a sapata ou considere o passivo na frente.");
        K(r, "MA3", "Área comprimida da base (solicitações características)",
          $"{N(r.BaseComprimida, "0.##")} m de {N(B, "0.##")} m (e = {N(Math.Abs(r.Excentricidade), "0.###")} m; B/6 = {N(B / 6, "0.###")} m)", "≥ 2/3 da base",
          r.BaseComprimida < 2 * B / 3 - 1e-9, "NBR 6122 7.6.2", dica: "Aumente a ponta ou a base.");
        K(r, "MA4", "Capacidade de carga da fundação", $"FS = {N(r.FsCapacidade, "0.00")}", $"≥ {N(fsC, "0.0")}", r.FsCapacidade < fsC, "NBR 6122 6.2.1.1 (Tab. 1)",
          dica: "Aumente a base ou confira o solo.");
        if (d.TensaoAdmissivel > 0)
            K(r, "MA5", "Tensão máxima no solo", $"σmáx = {N(r.SigmaMax, "0.#")} kPa", $"≤ σadm = {N(d.TensaoAdmissivel, "0")} kPa", r.SigmaMax > d.TensaoAdmissivel,
              "NBR 6122 6.3 · sondagem", dica: "Aumente a base.");
        r.Verificacoes.Add(new ResultadoVerificacao { Codigo = "MA6", Grupo = "Muro — estabilidade", Descricao = "Estabilidade global (ruptura do maciço)",
            Valor = "não calculada nesta etapa", Situacao = SituacaoVerificacao.Conferir, Referencia = "NBR 11682",
            Dica = "Verifique por superfície de ruptura (Bishop) quando houver talude, solo mole ou muro alto." });

        // ---- peças (ELU): fuste, talão e ponta em balanço
        Pecas(d, cfg, r, ka, ang, hv, B, es, xf, xt, tb);
        return r;
    }

    /// <summary>
    /// Integra o empuxo (kN/m) e a sua altura de aplicação (m, acima da base) no plano vertical de altura <paramref name="altura"/>
    /// com topo em y = <paramref name="altura"/> + y0: só o solo (γ ou γ submerso) ou só a sobrecarga. Pressão paralela ao terreno.
    /// </summary>
    private static (double E, double Y) Integrar(DadosMuro d, double altura, double y0, double ka, bool sobrecarga, List<(double, double, double)> diag)
    {
        double e = 0, me = 0, dz = altura / Fatias, topo = y0 + altura, sv = 0;
        for (int i = 0; i < Fatias; i++)
        {
            double yM = topo - (i + 0.5) * dz;
            double g = yM < d.NivelAgua ? d.GamaSat - GamaAgua : d.GamaSolo;
            double svM = sv + g * dz / 2;                       // tensão vertical efetiva no meio da fatia
            sv += g * dz;
            double p = sobrecarga ? ka * d.Sobrecarga : ka * svM;
            e += p * dz; me += p * dz * yM;
            if (diag != null && i % 20 == 0)
            {
                double pw = yM < d.NivelAgua ? GamaAgua * (d.NivelAgua - yM) : 0;
                diag.Add((yM, ka * (svM + d.Sobrecarga), pw));
            }
        }
        return (e, e > 1e-12 ? me / e : 0);
    }

    private static void Pecas(DadosMuro d, ConfiguracaoProjeto cfg, ResultadoMuro r, double ka, double ang, double hv, double B, double es, double xf, double xt, double tb)
    {
        double fck = cfg.Numero("materiais.fckMuro"), gC = cfg.Numero("normas.gamaC"), gS = cfg.Numero("normas.gamaS");
        double gg = cfg.Numero("normas.gamaG"), gq = cfg.Numero("normas.gamaQ"), gc = cfg.Numero("materiais.pesoEspecifico");
        double fyk = TabelasNbr.Fyk(cfg.Texto("materiais.aco"));
        double cob = cfg.Numero("durab.cobMuro") * 100;      // cm
        var fl = new Flexao(fck, fck / gC / 10, fyk / gS / 10);
        double rhoMin = TabelasNbr.RhoMinVigaPct(fck) / 100;
        double fctd = 0.7 * TabelasNbr.Fctm(fck) / gC;         // MPa
        double h = d.AlturaFuste, cosA = Math.Cos(ang);

        // ---- fuste: pressão horizontal na face de trás, profundidade a partir do topo do fuste (aterro inclinado: Ka já o considera)
        double gw = GamaAgua;
        double Pressao(double y, out double pSolo, out double pQ, out double pW)
        {
            // y acima do topo da sapata; tensão vertical efetiva por camadas
            double yAbs = es + y, z = h - y, sv = 0;
            double yNa = d.NivelAgua;
            double zSeco = Math.Max(0, Math.Min(z, es + h - Math.Max(yNa, yAbs)));
            double zSub = z - zSeco;
            sv = d.GamaSolo * zSeco + (d.GamaSat - gw) * zSub;
            pSolo = ka * sv * cosA; pQ = ka * d.Sobrecarga * cosA; pW = yAbs < yNa ? gw * (yNa - yAbs) : 0;
            return pSolo + pQ + pW;
        }
        // esforços no fuste por integração de cima para baixo
        int n = 200;
        double dy = h / n, vg = 0, vq = 0, mg = 0, mq = 0;
        r.DiagramaFuste.Add((h, Pressao(h, out _, out _, out _), 0, 0));
        double vgB, vqB, mgB, mqB;
        for (int i = 0; i < n; i++)
        {
            double yM = h - (i + 0.5) * dy;
            Pressao(yM, out double ps, out double pq, out double pw);
            // momento: a carga acumulada até aqui (V) atua no trecho dy
            mg += vg * dy + (ps + pw) * dy * dy / 2; mq += vq * dy + pq * dy * dy / 2;
            vg += (ps + pw) * dy; vq += pq * dy;
            double y = h - (i + 1) * dy;
            if ((i + 1) % 10 == 0) r.DiagramaFuste.Add((y, Pressao(y, out _, out _, out _), vg + vq, mg + mq));
        }
        vgB = vg; vqB = vq; mgB = mg; mqB = mq;
        Parte(r, "Fuste (base)", "face do aterro, vertical", d.EspessuraBase, mgB + mqB, gg * mgB + gq * mqB, gg * vgB + gq * vqB);
        {
            // meio do fuste: para o corte das barras
            double vgM = 0, vqM = 0, mgM = 0, mqM = 0; int nm = n / 2;
            for (int i = 0; i < nm; i++)
            {
                double yM = h - (i + 0.5) * dy; Pressao(yM, out double ps, out double pq, out double pw);
                mgM += vgM * dy + (ps + pw) * dy * dy / 2; mqM += vqM * dy + pq * dy * dy / 2; vgM += (ps + pw) * dy; vqM += pq * dy;
            }
            double tM = d.EspessuraTopo + (d.EspessuraBase - d.EspessuraTopo) / 2;
            Parte(r, "Fuste (meia altura)", "face do aterro, vertical", tM, mgM + mqM, gg * mgM + gq * mqM, gg * vgM + gq * vqM);
        }

        // ---- talão e ponta: tensões na base do caso de maior tensão (característico), cargas de cima, balanço a partir das faces do fuste
        double bc = r.BaseComprimida;
        double Sigma(double x)
        {
            if (bc >= B - 1e-9) return r.SigmaMin + (r.SigmaMax - r.SigmaMin) * (r.Excentricidade >= 0 ? (B - x) / B : x / B);
            // descomprimida: triângulo a partir do lado mais carregado
            double s = r.Excentricidade >= 0 ? r.SigmaMax * (1 - x / bc) : r.SigmaMax * (1 - (B - x) / bc);
            return Math.Max(0, s);
        }
        // talão: x de xt a B; para baixo: solo, sobrecarga, peso próprio, componente vertical do empuxo; para cima: σ do solo e subpressão
        {
            double L = d.Talao, mk = 0, vk = 0, mkQ = 0, vkQ = 0; int m = 100; double dx = L / Math.Max(1, m);
            for (int i = 0; i < m && L > 0; i++)
            {
                double x = xt + (i + 0.5) * dx, braco = x - xt;
                double hSolo = d.AlturaTotal - es + (x - xt) * tb;
                double hSub = Math.Max(0, Math.Min(d.NivelAgua, d.AlturaTotal) - es);
                double peso = gc * es + d.GamaSolo * (hSolo - hSub) + d.GamaSat * hSub;
                double sub = d.NivelAgua > 0 ? gw * d.NivelAgua * x / B : 0;
                double q = peso - Sigma(x) - sub;
                vk += q * dx; mk += q * dx * braco;
                vkQ += d.Sobrecarga * dx; mkQ += d.Sobrecarga * dx * braco;
            }
            var ev = r.Forcas.Where(f => f.Tipo == "Empuxo" || (f.Tipo == "Sobrecarga" && f.Fx != 0)).Sum(f => -f.Fy);
            mk += ev * L; vk += ev;
            Parte(r, "Talão", "face superior", es, mk + mkQ, gg * mk + gq * mkQ, gg * vk + gq * vkQ);
        }
        // ponta: x de 0 a xf; para cima σ; para baixo só o peso próprio (o solo sobre a ponta pode ser escavado: a favor da segurança)
        {
            double L = d.Ponta, mk = 0, vk = 0; int m = 100; double dx = L / Math.Max(1, m);
            for (int i = 0; i < m && L > 0; i++)
            {
                double x = (i + 0.5) * dx, braco = xf - x;
                double peso = gc * es;
                double sub = d.NivelAgua > 0 ? gw * d.NivelAgua * x / B : 0;
                double q = Sigma(x) + sub - peso;
                vk += q * dx; mk += q * dx * braco;
            }
            Parte(r, "Ponta", "face inferior", es, mk, gg * mk, gg * vk);
        }

        // dimensionamento de cada parte
        foreach (var p in r.Partes)
        {
            double hcm = p.Espessura * 100, dcm = hcm - cob - 1.0;
            p.D = dcm;
            var a = fl.Armadura(Math.Abs(p.Md) * 100, 100, dcm, cob + 1);
            p.As = a.As; p.XsobreD = a.XsobreD;
            p.AsMin = rhoMin * 100 * hcm;
            double asUsar = Math.Max(p.As, p.AsMin);
            p.Arranjo = Arranjo(asUsar, hcm, out double asEf);
            p.AsDistribuicao = Math.Max(Math.Max(0.2 * asEf, 0.9), 0.5 * p.AsMin);
            p.ArranjoDistribuicao = Arranjo(p.AsDistribuicao, hcm, out _);
            // VRd1 (19.4.1), sem protensão: k = |1,6 − d| ≥ 1 (metade da armadura chega ao apoio: no engaste chega toda)
            double k = Math.Max(1, Math.Abs(1.6 - dcm / 100));
            double rho1 = Math.Min(0.02, asEf / (100 * dcm));
            double tauRd = 0.25 * fctd;                       // MPa
            p.VRd1 = tauRd * 1000 * k * (1.2 + 40 * rho1) * 1.0 * dcm / 100;   // kN/m (MPa × m² = MN → ×1000 kN)
            string onde = p.Nome;
            K(r, "MA7", $"{onde}: flexão (x/d)", $"Md = {N(p.Md, "0.#")} kN·m/m; x/d = {N(p.XsobreD, "0.00")}", $"≤ {N(fl.XdLim, "0.00")}",
              p.XsobreD > fl.XdLim + 1e-6, "NBR 6118 14.6.4.3 e 17.2", dica: "Aumente a espessura.");
            K(r, "MA8", $"{onde}: cortante sem armadura transversal", $"Vd = {N(p.Vd, "0.#")} kN/m", $"≤ VRd1 = {N(p.VRd1, "0.#")} kN/m",
              p.Vd > p.VRd1, "NBR 6118 19.4.1", dica: "Aumente a espessura.");
        }
        var memP = new PassoMemoria { Titulo = "Armaduras do muro (ELU, por metro)" };
        memP.Linhas.Add($"fck = {N(fck)} MPa, {cfg.Texto("materiais.aco")}, cobrimento {N(cob, "0.#")} cm; γg = {N(gg)} (peso, solo e empuxo), γq = {N(gq)} (sobrecarga); " +
                        $"ρmín = {N(rhoMin * 100, "0.###")} % (NBR 6118 Tab. 17.3); distribuição ≥ 20 % da principal e ≥ 0,9 cm²/m (20.1).");
        foreach (var p in r.Partes)
            memP.Linhas.Add($"{p.Nome} ({p.Face}): h = {N(p.Espessura * 100, "0")} cm, d = {N(p.D, "0.#")} cm; Mk = {N(p.Mk, "0.#")} → Md = {N(p.Md, "0.#")} kN·m/m; " +
                            $"As = {N(p.As, "0.00")} cm²/m (mín. {N(p.AsMin, "0.00")}) → {p.Arranjo}; distribuição {p.ArranjoDistribuicao}; Vd = {N(p.Vd, "0.#")} ≤ VRd1 = {N(p.VRd1, "0.#")} kN/m.");
        memP.Linhas.Add("Face externa do fuste e face oposta da sapata: armadura mínima de distribuição nas duas direções (malha construtiva).");
        r.Memoria.Add(memP);
        K(r, "MA9", "Abertura de fissuras (ELS)", "não calculada nesta etapa", "wk da Tab. 13.4", true, "NBR 6118 17.3.3",
          SituacaoVerificacao.Conferir, "Muro em contato com o solo: confira wk na combinação frequente.");
    }

    private static void Parte(ResultadoMuro r, string nome, string face, double esp, double mk, double md, double vd) =>
        r.Partes.Add(new ParteMuro { Nome = nome, Face = md >= 0 ? face : "face oposta (momento invertido)", Espessura = esp, Mk = mk, Md = Math.Abs(md), Vd = Math.Abs(vd) });

    /// <summary>Barras por metro: a menor área que passa, ø8 a ø25, espaçamento de 10 a 20 cm (e ≤ 2h), múltiplo de 2,5 cm.</summary>
    public static string Arranjo(double asPorMetro, double hcm, out double asEf)
    {
        double sMax = Math.Min(20, 2 * hcm);
        (double Phi, double S, double A) melhor = (0, 0, double.MaxValue);
        foreach (double phi in new[] { 8, 10, 12.5, 16, 20, 25 })
        {
            double ab = Math.PI * phi * phi / 400;
            double s = Math.Floor(Math.Min(sMax, 100 * ab / Math.Max(1e-9, asPorMetro)) / 2.5) * 2.5;
            if (s < 10) continue;
            double a = 100 * ab / s;
            if (a < melhor.A - 1e-9) melhor = (phi, s, a);
        }
        if (melhor.Phi == 0) { asEf = 100 * Math.PI * 625 / 400 / 10; return $"ø25 c/10 ({N(asEf, "0.00")} cm²/m — insuficiente, aumente a espessura)"; }
        asEf = melhor.A;
        return $"ø{N(melhor.Phi, "0.#")} c/{N(melhor.S, "0.#")} ({N(melhor.A, "0.00")} cm²/m)";
    }

    private static void K(ResultadoMuro r, string cod, string desc, string valor, string limite, bool falha, string refer,
                          SituacaoVerificacao seFalha = SituacaoVerificacao.NaoAtende, string dica = "") =>
        r.Verificacoes.Add(new ResultadoVerificacao
        {
            Codigo = cod, Grupo = cod.CompareTo("MA7") >= 0 ? "Muro — peças" : "Muro — estabilidade", Descricao = desc, Valor = valor, Limite = limite,
            Referencia = refer, Situacao = falha ? seFalha : SituacaoVerificacao.Ok, Dica = falha ? dica : "",
        });
}
