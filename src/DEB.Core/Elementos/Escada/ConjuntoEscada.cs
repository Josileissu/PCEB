using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Elementos.Viga.PreMoldada;
using DEB.Core.Elementos.Viga.PreMoldada.Calculo;
using DEB.Core.Motor;

namespace DEB.Core.Elementos.Escada;

/// <summary>Ponta de uma peça (a de baixo ou a de cima, no sentido do x da peça).</summary>
public enum PontaPeca
{
    Inferior = 0,
    Superior = 1,
}

/// <summary>Como a borda da ponta apoiada fica sobre a peça que a suporta.</summary>
public enum DirecaoApoio
{
    /// <summary>A borda atravessa a largura do suporte (peças em linha, escada em L): a carga cai numa faixa estreita do vão do
    /// suporte, no comprimento de apoio.</summary>
    Transversal = 0,
    /// <summary>A borda corre ao longo do vão do suporte (escada em U: o lance chega pelo lado do patamar): a carga se espalha no
    /// vão, na largura da peça apoiada.</summary>
    AoLongo = 1,
}

/// <summary>Uma ponta de peça apoiada noutra peça ou numa viga do patamar.</summary>
public sealed class ApoioEntrePecas
{
    /// <summary>Nome da peça apoiada e qual ponta dela.</summary>
    public string Apoiada { get; set; } = "";
    public PontaPeca Ponta { get; set; }
    /// <summary>Nome da peça ou da viga que suporta.</summary>
    public string Suporte { get; set; } = "";
    /// <summary>Posição do centro da carga no suporte (cm): x horizontal a partir do eixo do apoio inferior da peça, ou a partir da
    /// ponta esquerda da viga.</summary>
    public double X { get; set; }
    public DirecaoApoio Direcao { get; set; } = DirecaoApoio.Transversal;
}

/// <summary>Viga do patamar (viga de escada): pré-moldada retangular biapoiada nos pilares ou consolos, calculada pela D40.</summary>
public sealed class VigaPatamar
{
    public string Nome { get; set; } = "VE";
    public double B { get; set; } = 20;
    public double H { get; set; } = 50;
    /// <summary>Comprimento da peça (cm) e distância de cada ponta ao eixo do apoio (cm).</summary>
    public double Comprimento { get; set; } = 360;
    public double ApoioE { get; set; } = 10;
    public double ApoioD { get; set; } = 10;
    public double Fck { get; set; } = 40;
    /// <summary>Cargas além das que vêm das peças da escada (alvenaria, outra laje).</summary>
    public double GAdicional { get; set; }
    public double QAdicional { get; set; }
    public double Vao => Comprimento - ApoioE - ApoioD;

    public ValoresTemplate Valores()
    {
        var v = new ValoresTemplate(new TemplateVigaPreMoldada().Parametros);
        v["B"] = Fmt.N(B); v["H"] = Fmt.N(H); v["L"] = Fmt.N(Comprimento); v["ExtE"] = "Reta"; v["ExtD"] = "Reta"; v["Fck"] = Fmt.N(Fck);
        return v;
    }
}

/// <summary>
/// Escada montada por peças (D43): lances e patamares pré-moldados (ou no local) e vigas do patamar, ligados pelas pontas. Cada
/// ponta que não aparece em <see cref="Apoios"/> assenta numa viga ou laje da estrutura (fora do conjunto). Esquemas cobertos:
/// lance com o patamar apoiado no patamar de outro lance (em linha ou em L), patamar intermediário como peça própria recebendo os
/// lances (em U, os lances chegam pelo lado), patamar apoiado numa viga do patamar.
/// </summary>
public sealed class ConjuntoEscada
{
    public List<(string Nome, EscadaLaje Peca)> Pecas { get; } = new List<(string, EscadaLaje)>();
    public List<VigaPatamar> Vigas { get; } = new List<VigaPatamar>();
    public List<ApoioEntrePecas> Apoios { get; } = new List<ApoioEntrePecas>();

    public EscadaLaje Peca(string nome) => Pecas.FirstOrDefault(p => p.Nome == nome).Peca;
}

/// <summary>Resultado do conjunto: cada peça calculada com as cargas das que se apoiam nela e as vigas do patamar.</summary>
public sealed class ResultadoConjunto
{
    public Dictionary<string, ResultadoEscada> Pecas { get; } = new Dictionary<string, ResultadoEscada>();
    public Dictionary<string, ResultadoCalculoViga> Vigas { get; } = new Dictionary<string, ResultadoCalculoViga>();
    public Dictionary<string, CargasViga> CargasVigas { get; } = new Dictionary<string, CargasViga>();
    /// <summary>Ordem do cálculo (das peças apoiadas para as que suportam).</summary>
    public List<string> Ordem { get; } = new List<string>();
    /// <summary>Texto de cada transferência de carga (de quem, para quem, quanto).</summary>
    public List<string> Transferencias { get; } = new List<string>();
    /// <summary>Reações características que saem do conjunto pelas pontas apoiadas na estrutura (kN, peça e ponta).</summary>
    public List<(string Peca, PontaPeca Ponta, double G, double Q)> ReacoesExternas { get; } = new List<(string, PontaPeca, double, double)>();
    public List<string> Avisos { get; } = new List<string>();
    public List<string> Erros { get; } = new List<string>();
}

public static class CalculoConjuntoEscada
{
    private static string N(double x, string f = "0.##") => Fmt.Virgula(x, f);

    /// <summary>Reação característica de uma ponta por metro de largura (kN/m): a da estática mais, na pré-moldada, o trecho da peça
    /// além do eixo do apoio.</summary>
    public static (double G, double Q) ReacaoPonta(ResultadoEscada r, PontaPeca p)
    {
        var e = r.Entrada;
        double g = p == PontaPeca.Inferior ? r.RaG : r.RbG, q = p == PontaPeca.Inferior ? r.RaQ : r.RbQ;
        if (e.PreMoldada)
        {
            var lig = p == PontaPeca.Inferior ? e.LigacaoInferior : e.LigacaoSuperior;
            double gc = e.PesoEspecifico > 0 ? e.PesoEspecifico : 25;
            g += gc * lig.AlturaNoApoio(r.Geo.Hp) / 100 * lig.Balanco / 100 + e.Revestimento * lig.Balanco / 100;
        }
        return (g, q);
    }

    public static ResultadoConjunto Calcular(ConjuntoEscada c, CoeficientesViga k = null)
    {
        k ??= new CoeficientesViga();
        var res = new ResultadoConjunto();
        var nomes = c.Pecas.Select(p => p.Nome).ToList();
        var vigas = c.Vigas.ToDictionary(v => v.Nome);
        foreach (var d in nomes.GroupBy(n => n).Where(g => g.Count() > 1)) res.Erros.Add($"Peça repetida: {d.Key}.");
        foreach (var a in c.Apoios)
        {
            if (!nomes.Contains(a.Apoiada)) res.Erros.Add($"Apoio: a peça \"{a.Apoiada}\" não existe.");
            if (!nomes.Contains(a.Suporte) && !vigas.ContainsKey(a.Suporte)) res.Erros.Add($"Apoio: o suporte \"{a.Suporte}\" não é peça nem viga do conjunto.");
            if (a.Apoiada == a.Suporte) res.Erros.Add($"Apoio: a peça \"{a.Apoiada}\" não pode apoiar nela mesma.");
        }
        foreach (var d in c.Apoios.GroupBy(a => (a.Apoiada, a.Ponta)).Where(g => g.Count() > 1))
            res.Erros.Add($"A ponta {(d.Key.Ponta == PontaPeca.Inferior ? "inferior" : "superior")} de \"{d.Key.Apoiada}\" tem mais de um apoio.");
        if (res.Erros.Count > 0) return res;

        // ordem: uma peça só é calculada depois de todas as que se apoiam nela
        var pendentes = new List<string>(nomes);
        while (pendentes.Count > 0)
        {
            var livre = pendentes.FirstOrDefault(p => !c.Apoios.Any(a => a.Suporte == p && pendentes.Contains(a.Apoiada)));
            if (livre == null) { res.Erros.Add("As peças se apoiam umas nas outras em ciclo: " + string.Join(", ", pendentes) + "."); return res; }
            res.Ordem.Add(livre); pendentes.Remove(livre);
        }

        foreach (string nome in res.Ordem)
        {
            var e = c.Peca(nome).Copia();
            foreach (var a in c.Apoios.Where(a => a.Suporte == nome))
            {
                var ra = res.Pecas[a.Apoiada]; var ea = ra.Entrada;
                var (g, q) = ReacaoPonta(ra, a.Ponta);
                double bA = ea.Largura, bS = e.Largura;
                double comp = a.Direcao == DirecaoApoio.AoLongo ? bA
                    : Math.Max(5, (a.Ponta == PontaPeca.Inferior ? ea.LigacaoInferior : ea.LigacaoSuperior).ComprimentoApoio);
                // resultante da ponta (kN) dividida pela largura do suporte = carga por metro de largura da faixa
                double gS = g * bA / bS, qS = q * bA / bS;
                e.CargasApoiadas.Add(new CargaApoiada { Nome = $"{a.Apoiada} ({Lado(a.Ponta)})", X = a.X - comp / 2, Comprimento = comp, G = gS, Q = qS });
                res.Transferencias.Add($"{a.Apoiada}, ponta {Lado(a.Ponta)} → {nome}: {N(g, "0.00")} + {N(q, "0.00")} kN/m × {N(bA / 100, "0.00")} m = " +
                                       $"{N(g * bA / 100, "0.0")} + {N(q * bA / 100, "0.0")} kN, em x = {N(a.X, "0")} cm " +
                                       (a.Direcao == DirecaoApoio.AoLongo ? $"ao longo de {N(comp, "0")} cm do vão" : $"no apoio de {N(comp, "0")} cm") +
                                       $" ({N(gS, "0.00")} + {N(qS, "0.00")} kN por metro de largura de {nome}).");
                if (a.Direcao == DirecaoApoio.Transversal && bA < 0.9 * bS)
                    res.Avisos.Add($"{nome}: a carga de {a.Apoiada} ocupa {N(bA, "0")} cm da largura de {N(bS, "0")} cm — o cálculo por faixa de 1 m usa a média; confira a faixa mais carregada.");
            }
            var r = CalculoEscada.Calcular(e, k);
            res.Pecas[nome] = r;
            foreach (string er in r.Erros) res.Erros.Add($"{nome}: {er}");
            foreach (var p in new[] { PontaPeca.Inferior, PontaPeca.Superior })
                if (!c.Apoios.Any(a => a.Apoiada == nome && a.Ponta == p))
                {
                    var (g, q) = ReacaoPonta(r, p);
                    res.ReacoesExternas.Add((nome, p, g * e.Largura / 100, q * e.Largura / 100));
                }
        }

        foreach (var v in c.Vigas)
        {
            var cv = new CargasViga { G = v.GAdicional, Q = v.QAdicional, ApoioE = v.ApoioE, ApoioD = v.ApoioD };
            foreach (var a in c.Apoios.Where(a => a.Suporte == v.Nome))
            {
                var ra = res.Pecas[a.Apoiada];
                var (g, q) = ReacaoPonta(ra, a.Ponta);
                double bA = ra.Entrada.Largura;
                if (a.Direcao == DirecaoApoio.AoLongo && bA >= 0.9 * v.Vao)
                {
                    cv.G += g; cv.Q += q;   // a borda cobre o vão: carga distribuída (kN/m)
                    res.Transferencias.Add($"{a.Apoiada}, ponta {Lado(a.Ponta)} → {v.Nome}: {N(g, "0.00")} + {N(q, "0.00")} kN/m distribuída na viga.");
                }
                else
                {
                    // borda parcial (ou ponta atravessando a viga): resultante em concentradas iguais na largura apoiada
                    int n = a.Direcao == DirecaoApoio.AoLongo ? Math.Max(2, (int)Math.Ceiling(bA / 25)) : 1;
                    double gt = g * bA / 100, qt = q * bA / 100;
                    for (int i = 0; i < n; i++)
                    {
                        double x = a.Direcao == DirecaoApoio.AoLongo ? a.X - bA / 2 + bA * (i + 0.5) / n : a.X;
                        cv.Concentradas.Add(new CargaConcentrada { X = x, G = gt / n, Q = qt / n });
                    }
                    res.Transferencias.Add($"{a.Apoiada}, ponta {Lado(a.Ponta)} → {v.Nome}: {N(gt, "0.0")} + {N(qt, "0.0")} kN em {n} concentrada(s) centradas em x = {N(a.X, "0")} cm.");
                }
                if (a.X < 0 || a.X > v.Comprimento) res.Avisos.Add($"{v.Nome}: a carga de {a.Apoiada} está fora da viga (x = {N(a.X, "0")} cm).");
            }
            res.CargasVigas[v.Nome] = cv;
            try { res.Vigas[v.Nome] = CalculoViga.Calcular(v.Valores(), cv, k); }
            catch (Exception ex) { res.Erros.Add($"{v.Nome}: {ex.Message}"); }
        }
        return res;
    }

    private static string Lado(PontaPeca p) => p == PontaPeca.Inferior ? "inferior" : "superior";
}
