using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Analise.Barras;
using DEB.Core.Configuracao.Projeto;
using DEB.Core.Elementos.Consolo;
using DEB.Core.Intercambio;
using DEB.Core.Pecas;

namespace DEB.Core.Analise.Objetos;

// =====================================================================================================================
// Análise por objetos (D46): cada peça e cada ligação gera e guarda as suas partes no modelo de barras; os esforços voltam a
// elas. Unidades: cm, kN. Coordenadas do mundo: X, Y horizontais, Z para cima (as do Revit, convertidas em cm).
// =====================================================================================================================

/// <summary>Pilar: barras no eixo (CG da seção), nós nos níveis pedidos, engaste no topo do cálice (embutimento).</summary>
public sealed class PilarObjeto
{
    public Dono Dono { get; set; } = new Dono { Tipo = TipoDono.Pilar };
    /// <summary>Ponto do eixo (CG da seção) na base da peça (fundo do cálice), cm.</summary>
    public Vetor3 Base { get; set; }
    /// <summary>Direção do eixo (normalmente +Z).</summary>
    public Vetor3 Eixo { get; set; } = Vetor3.EixoZ;
    /// <summary>Direção, no mundo, do eixo y da seção (horizontal).</summary>
    public Vetor3 DirY { get; set; } = Vetor3.EixoX;
    /// <summary>Seção nos eixos dela (y, z); o CG vai para o eixo da barra.</summary>
    public SecaoPoligonal Secao { get; set; }
    public double Comprimento { get; set; }
    /// <summary>Comprimento embutido no cálice (cm): o engaste fica nessa altura a partir da base.</summary>
    public double Embutimento { get; set; }
    public MaterialBarra Material { get; set; }
    /// <summary>Alturas (cm a partir da base) onde o pilar precisa de nó: topo dos consolos, vigas apoiadas.</summary>
    public List<double> Niveis { get; } = new List<double>();
    /// <summary>Fator de rigidez à flexão (NBR 6118 15.7.3: 0,8 na 2ª ordem global); 1 = elástica.</summary>
    public double FatorFlexao { get; set; } = 1;

    // gerado
    public List<(double Z, int No)> Nos { get; } = new List<(double, int)>();
    public List<BarraPortico> Barras { get; } = new List<BarraPortico>();
    public Vetor3 DirZ => Eixo.Vetorial(DirY).Unitario;
    public Vetor3 Ponto(double z) => Base + Eixo.Unitario * z;

    internal void Gerar(ModeloBarras m)
    {
        var sec = PropriedadesSecao.Barra(Secao);
        var zs = new List<double> { Embutimento, Comprimento };
        zs.AddRange(Niveis.Where(z => z > Embutimento + 0.5 && z < Comprimento - 0.5));
        zs = zs.Select(z => Math.Round(z, 2)).Distinct().OrderBy(z => z).ToList();
        Nos.Clear(); Barras.Clear();
        foreach (double z in zs)
        {
            Vetor3 p = Ponto(z);
            Nos.Add((z, m.NovoNo(p.X, p.Y, p.Z, Dono, $"{Dono} z={z:0.#}")));
        }
        for (int i = 0; i + 1 < Nos.Count; i++)
        {
            var b = m.Barra(Nos[i].No, Nos[i + 1].No, sec, Material, Dono, $"{Dono} {Nos[i].Z:0}–{Nos[i + 1].Z:0}");
            b.VetorXZ = DirZ;
            b.FatorFlexao = FatorFlexao;
            Barras.Add(b);
        }
        m.Apoio(Nos[0].No, ApoioNo.Engaste, Dono);
    }

    /// <summary>Nó do pilar na altura z (o mais próximo).</summary>
    public int NoNoNivel(double z) => Nos.OrderBy(n => Math.Abs(n.Z - z)).First().No;
}

/// <summary>Viga biapoiada sobre aparelhos: barra no eixo (CG), entre os pontos de apoio; nós de apoio presos ao eixo por vínculo rígido.</summary>
public sealed class VigaObjeto
{
    public Dono Dono { get; set; } = new Dono { Tipo = TipoDono.Viga };
    /// <summary>Pontos de apoio (centro do aparelho, na face de baixo da viga), cm.</summary>
    public Vetor3 ApoioI { get; set; }
    public Vetor3 ApoioJ { get; set; }
    /// <summary>Seção nos eixos dela (y horizontal, z vertical).</summary>
    public SecaoPoligonal Secao { get; set; }
    public MaterialBarra Material { get; set; }
    /// <summary>Cargas além do peso próprio (kN/m) e concentradas (X em cm a partir do apoio I; G e Q em kN).</summary>
    public double G { get; set; }
    public double Q { get; set; }
    public List<(double X, double G, double Q)> Concentradas { get; } = new List<(double, double, double)>();
    public double FatorFlexao { get; set; } = 1;
    /// <summary>Fator da rigidez à torção (0,15 = concreto fissurado, prática usual; 1 = elástica).</summary>
    public double FatorTorcao { get; set; } = 0.15;

    // gerado
    public int NoEixoI { get; private set; }
    public int NoEixoJ { get; private set; }
    public int NoApoioI { get; private set; }
    public int NoApoioJ { get; private set; }
    public BarraPortico Barra { get; private set; }
    /// <summary>Altura do CG acima da face de baixo (cm).</summary>
    public double AlturaEixo => Secao.CG.Z - Secao.ZMin;

    internal void Gerar(ModeloBarras m, CasoCarga g, CasoCarga q)
    {
        var up = Vetor3.EixoZ * AlturaEixo;
        Vetor3 ei = ApoioI + up, ej = ApoioJ + up;
        NoEixoI = m.NovoNo(ei.X, ei.Y, ei.Z, Dono, Dono + " eixo I");
        NoEixoJ = m.NovoNo(ej.X, ej.Y, ej.Z, Dono, Dono + " eixo J");
        NoApoioI = m.NovoNo(ApoioI.X, ApoioI.Y, ApoioI.Z, Dono, Dono + " apoio I");
        NoApoioJ = m.NovoNo(ApoioJ.X, ApoioJ.Y, ApoioJ.Z, Dono, Dono + " apoio J");
        Barra = m.Barra(NoEixoI, NoEixoJ, PropriedadesSecao.Barra(Secao), Material, Dono, Dono.ToString());
        Barra.VetorXZ = Vetor3.EixoZ;
        Barra.FatorFlexao = FatorFlexao;
        Barra.FatorTorcao = FatorTorcao;
        m.Rigido(NoEixoI, NoApoioI, Dono, Dono + " altura do eixo I");
        m.Rigido(NoEixoJ, NoApoioJ, Dono, Dono + " altura do eixo J");
        if (G != 0) g.Distribuidas.Add(new CargaDistribuida { Barra = Barra.Id, Q = new Vetor3(0, 0, -G / 100) });
        if (Q != 0) q.Distribuidas.Add(new CargaDistribuida { Barra = Barra.Id, Q = new Vetor3(0, 0, -Q / 100) });
        foreach (var (x, cg, cq) in Concentradas)
        {
            if (cg != 0) g.Pontuais.Add(new CargaPontualBarra { Barra = Barra.Id, A = x, F = new Vetor3(0, 0, -cg) });
            if (cq != 0) q.Pontuais.Add(new CargaPontualBarra { Barra = Barra.Id, A = x, F = new Vetor3(0, 0, -cq) });
        }
    }
}

/// <summary>Como a ligação aparece no modelo (D46, item 2).</summary>
public enum RepresentacaoLigacao
{
    /// <summary>Barra rígida do eixo do pilar ao topo do consolo + mola do aparelho de apoio (padrão).</summary>
    BarraRigidaEMola,
    /// <summary>Barra rígida + articulação fixa (translações presas, rotações livres, torção da viga presa).</summary>
    BarraRigidaArticulada,
}

/// <summary>Ligação viga–pilar por consolo (D46): nó do pilar no topo do consolo → vínculo rígido até o ponto de apoio → mola do aparelho → nó de apoio da viga.</summary>
public sealed class LigacaoConsolo
{
    public Dono Dono { get; set; } = new Dono { Tipo = TipoDono.Ligacao };
    public PilarObjeto Pilar { get; set; }
    public VigaObjeto Viga { get; set; }
    /// <summary>Ponta da viga que apoia neste consolo (true = I).</summary>
    public bool PontaI { get; set; }
    /// <summary>Altura do topo do consolo a partir da base do pilar (cm).</summary>
    public double ZTopoConsolo { get; set; }
    /// <summary>Aparelho de apoio (D36): tipo e medidas (cm); null = sem informação (articulação fixa).</summary>
    public ApoioPeca Aparelho { get; set; }
    public RepresentacaoLigacao Representacao { get; set; } = RepresentacaoLigacao.BarraRigidaEMola;

    // gerado
    public int NoPilar { get; private set; }
    public int NoConsolo { get; private set; }
    public MolaLigacao Mola { get; private set; }
    public string Observacao { get; private set; } = "";

    /// <summary>Direção da viga, do apoio desta ponta para o outro (horizontal).</summary>
    public Vetor3 DirecaoViga
    {
        get { Vetor3 d = (PontaI ? Viga.ApoioJ - Viga.ApoioI : Viga.ApoioI - Viga.ApoioJ); return new Vetor3(d.X, d.Y, 0).Unitario; }
    }

    /// <summary>Rigidez do aparelho nos eixos x' (ao longo da viga), y' (transversal), z' (vertical): translações e rotações.</summary>
    public static double[] Rigidez(ApoioPeca a, double gKNcm2, RepresentacaoLigacao rep, out string obs)
    {
        const double Rigido = 1e8, Garfo = 1e10;
        bool elastomero = rep == RepresentacaoLigacao.BarraRigidaEMola && a != null && a.Tipo == ApoioPeca.Neoprene &&
                          a.Comprimento > 0 && a.Largura > 0 && a.Espessura > 0;
        if (!elastomero)
        {
            obs = a == null ? "sem aparelho informado: articulação fixa" :
                  a.Tipo == ApoioPeca.Neoprene ? "elastômero sem medidas: articulação fixa" : $"apoio {a.NomeTipo}: articulação fixa";
            return new[] { Rigido, Rigido, Rigido, Garfo, 0, 0 };
        }
        double la = a.Comprimento, lb = a.Largura, t = a.Espessura, area = la * lb;
        double kh = gKNcm2 * area / t;
        double s = area / (2 * t * (la + lb));          // fator de forma
        double ec = 5 * gKNcm2 * s * s;                  // módulo de compressão aparente (El Debs 2017, 3.3)
        double kv = ec * area / t;
        double kRot = ec * lb * Math.Pow(la, 3) / 12 / t;   // giro da viga no plano vertical dela
        double kYaw = gKNcm2 * area * (la * la + lb * lb) / 12 / t;
        obs = $"elastômero {la:0.#}×{lb:0.#}×{t:0.#} cm: Kh = {kh:0.#} kN/cm, Kv = {kv:0} kN/cm";
        return new[] { kh, kh, kv, Garfo, kRot, kYaw };
    }

    internal void Gerar(ModeloBarras m, double gKNcm2)
    {
        NoPilar = Pilar.NoNoNivel(ZTopoConsolo);
        int noApoioViga = PontaI ? Viga.NoApoioI : Viga.NoApoioJ;
        var pv = m.Nos[noApoioViga];
        // topo do consolo sob o aparelho: o mesmo ponto em planta, na altura do consolo
        Vetor3 topo = Pilar.Ponto(ZTopoConsolo);
        NoConsolo = m.NovoNo(pv.X, pv.Y, topo.Z, Dono, Dono + " topo do consolo");
        m.Rigido(NoPilar, NoConsolo, Dono, Dono + " consolo");
        Mola = m.Mola(NoConsolo, noApoioViga, Rigidez(Aparelho, gKNcm2, Representacao, out string obs), Dono, Dono + " aparelho");
        Mola.EixoX = DirecaoViga;
        Mola.VetorXZ = Vetor3.EixoZ;
        Observacao = obs;
    }
}

/// <summary>Esforços de uma ligação por combinação: o que a viga põe no consolo.</summary>
public sealed class EsforcoLigacao
{
    public string Combinacao { get; set; } = "";
    /// <summary>Força vertical para baixo no consolo (kN).</summary>
    public double V { get; set; }
    /// <summary>Força horizontal ao longo da viga (kN; + puxa o consolo para a viga) e transversal.</summary>
    public double H { get; set; }
    public double Ht { get; set; }
}

/// <summary>Esforços num nível do pilar (kN, kN·cm): N de compressão positivo; momentos nos eixos y e z da seção.</summary>
public sealed class EsforcoPilar
{
    public string Combinacao { get; set; } = "";
    public double Z { get; set; }
    public double N { get; set; }
    public double Vy { get; set; }
    public double Vz { get; set; }
    public double My { get; set; }
    public double Mz { get; set; }
}

/// <summary>Combinação (NBR 8681): nome, tipo e fatores por caso.</summary>
public sealed class CombinacaoCasos
{
    public string Nome { get; set; } = "";
    public string Tipo { get; set; } = "ELU";
    public List<(string Caso, double Fator)> Fatores { get; } = new List<(string, double)>();
}

/// <summary>A estrutura montada por objetos: gera o modelo, analisa, combina e devolve os esforços a cada objeto.</summary>
public sealed class EstruturaObjetos
{
    public const string CasoPP = "PP", CasoG = "G", CasoQ = "Q";
    public List<PilarObjeto> Pilares { get; } = new List<PilarObjeto>();
    public List<VigaObjeto> Vigas { get; } = new List<VigaObjeto>();
    public List<LigacaoConsolo> Ligacoes { get; } = new List<LigacaoConsolo>();
    public ModeloBarras Modelo { get; private set; }
    public Dictionary<string, ResultadoCaso> Casos { get; private set; }
    public Dictionary<string, ResultadoCaso> Combinacoes { get; } = new Dictionary<string, ResultadoCaso>();
    public List<CombinacaoCasos> DefinicaoCombinacoes { get; } = new List<CombinacaoCasos>();
    public List<string> Avisos { get; } = new List<string>();
    private SolverBarras _solver;

    /// <summary>Gera o modelo de barras a partir dos objetos (pilares antes, para os nós dos níveis existirem).</summary>
    public ModeloBarras Gerar(ConfiguracaoProjeto cfg = null)
    {
        cfg ??= new ConfiguracaoProjeto();
        var m = new ModeloBarras();
        var pp = m.Caso(CasoPP, NaturezaCaso.PesoProprio); pp.PesoProprio = true;
        var g = m.Caso(CasoG, NaturezaCaso.Permanente);
        var q = m.Caso(CasoQ, NaturezaCaso.Variavel);
        foreach (var l in Ligacoes) if (!l.Pilar.Niveis.Contains(l.ZTopoConsolo)) l.Pilar.Niveis.Add(l.ZTopoConsolo);
        foreach (var p in Pilares) p.Gerar(m);
        foreach (var v in Vigas) v.Gerar(m, g, q);
        double gEl = cfg.Numero("acoes.gElastomero") / 10;   // MPa → kN/cm²
        foreach (var l in Ligacoes)
        {
            l.Gerar(m, gEl);
            if (l.Observacao.Contains("articulação")) Avisos.Add($"{l.Dono}: {l.Observacao}.");
        }
        // viga sem ligação numa ponta: apoio articulado fixo no próprio nó (não fica solta)
        foreach (var v in Vigas)
            foreach (bool i in new[] { true, false })
                if (!Ligacoes.Any(l => l.Viga == v && l.PontaI == i))
                {
                    m.Apoio(i ? v.NoApoioI : v.NoApoioJ, new[] { true, true, true, true, false, false }, v.Dono);
                    Avisos.Add($"{v.Dono}: ponta {(i ? "I" : "J")} sem consolo ligado — apoio articulado fixo.");
                }
        Modelo = m;
        DefinicaoCombinacoes.Clear();
        DefinicaoCombinacoes.AddRange(Combinacoes8681(cfg));
        return m;
    }

    /// <summary>
    /// Combinações PROVISÓRIAS (o usuário deixou as combinações para depois, 24/11): ELU normal com o γ da configuração
    /// (permanentes desfavoráveis e favoráveis), ELS frequente (ψ1) e quase permanente (ψ2), com uma só ação variável.
    /// </summary>
    public static List<CombinacaoCasos> Combinacoes8681(ConfiguracaoProjeto cfg)
    {
        double gg = cfg.Numero("normas.gamaG"), gq = cfg.Numero("normas.gamaQ"), p1 = cfg.Numero("normas.psi1"), p2 = cfg.Numero("normas.psi2");
        CombinacaoCasos C(string nome, string tipo, double fg, double fq)
        {
            var c = new CombinacaoCasos { Nome = nome, Tipo = tipo };
            c.Fatores.Add((CasoPP, fg)); c.Fatores.Add((CasoG, fg)); if (fq != 0) c.Fatores.Add((CasoQ, fq));
            return c;
        }
        return new List<CombinacaoCasos>
        {
            C($"ELU 1: {gg:0.##}·G + {gq:0.##}·Q", "ELU", gg, gq),
            C($"ELU 2: 1,0·G + {gq:0.##}·Q", "ELU", 1.0, gq),
            C($"ELU 3: {gg:0.##}·G", "ELU", gg, 0),
            C($"ELS freq.: G + {p1:0.##}·Q", "ELS", 1.0, p1),
            C($"ELS q. perm.: G + {p2:0.##}·Q", "ELS", 1.0, p2),
        };
    }

    /// <summary>Gera (se preciso), analisa os casos e monta as combinações.</summary>
    public void Analisar(ConfiguracaoProjeto cfg = null)
    {
        if (Modelo == null) Gerar(cfg);
        _solver = new SolverBarras(Modelo);
        Casos = _solver.Resolver();
        Combinacoes.Clear();
        foreach (var c in DefinicaoCombinacoes)
            Combinacoes[c.Nome] = ResultadoCaso.Combinar(c.Nome, c.Fatores.Select(f => (Casos[f.Caso], f.Fator)));
    }

    /// <summary>O que a viga põe no consolo, num resultado (caso ou combinação): mola nos eixos dela (x' ao longo da viga, z' vertical).</summary>
    public EsforcoLigacao Esforco(LigacaoConsolo l, ResultadoCaso r)
    {
        double[] f = r.ForcasMolas[l.Mola.Id];   // F = k·(u_viga − u_consolo): o que o consolo recebe é +F
        return new EsforcoLigacao { Combinacao = r.Nome, V = -f[2], H = f[0], Ht = f[1] };
    }

    /// <summary>
    /// Carga do consolo para o cálculo (D44 etapa 1): Vg e Hg dos casos permanentes, Vq e Hq do variável — o cálculo do consolo
    /// aplica γn e γf da configuração (NBR 9062 7.3.1.1), com o mínimo de Hd.
    /// </summary>
    public CargaConsolo CargaDoConsolo(LigacaoConsolo l)
    {
        var pp = Esforco(l, Casos[CasoPP]); var g = Esforco(l, Casos[CasoG]); var q = Esforco(l, Casos[CasoQ]);
        return new CargaConsolo
        {
            Vg = pp.V + g.V, Vq = Math.Max(0, q.V), Hg = pp.H + g.H, Hq = q.H,
            Origem = $"análise por objetos: {l.Viga.Dono}, ponta {(l.PontaI ? "I" : "J")} ({l.Observacao})",
        };
    }

    /// <summary>Esforços do pilar nos nós (abaixo e acima de cada nível), num resultado: eixos da seção (y = DirY, z = DirZ).</summary>
    public List<EsforcoPilar> Esforcos(PilarObjeto p, ResultadoCaso r)
    {
        var l = new List<EsforcoPilar>();
        foreach (var b in p.Barras)
        {
            double[] f = r.ForcasBarras[b.Id];
            double zi = p.Nos.First(n => n.No == b.NoI).Z, zj = p.Nos.First(n => n.No == b.NoJ).Z;
            // ponta I: esforço interno = −(força sobre a barra); ponta J: = +(força sobre a barra)
            l.Add(new EsforcoPilar { Combinacao = r.Nome, Z = zi, N = f[0], Vy = -f[1], Vz = -f[2], My = -f[4], Mz = -f[5] });
            l.Add(new EsforcoPilar { Combinacao = r.Nome, Z = zj, N = -f[6], Vy = f[7], Vz = f[8], My = f[10], Mz = f[11] });
        }
        return l;
    }
}
