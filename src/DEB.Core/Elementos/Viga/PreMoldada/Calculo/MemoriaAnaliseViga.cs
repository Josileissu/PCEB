using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Analise.Objetos;
using DEB.Core.Documentacao;
using DEB.Core.Motor;
using DEB.Core.Normas.Verificacao;

namespace DEB.Core.Elementos.Viga.PreMoldada.Calculo;

/// <summary>
/// Memória de cálculo da viga no CAD (D51, etapa 5: "tudo que for fazendo entra na memória"): os passos da ANÁLISE (seções,
/// materiais, cargas, modelos de ligação, esforços, serviço) seguidos dos passos do DIMENSIONAMENTO (CalculoViga), do quadro de
/// verificações e dos avisos — escritos no espaço reservado abaixo dos diagramas, nas unidades da configuração.
/// </summary>
public static class MemoriaAnaliseViga
{
    private static string N(double x, string f = "0.##") => Fmt.Virgula(x, f);

    /// <summary>Passos da análise (A1 a A5), com as contas e os números de cada um.</summary>
    public static List<PassoMemoria> PassosAnalise(ResultadoAnaliseViga r, EntradaAnaliseViga e)
    {
        var l = new List<PassoMemoria>();
        var a1 = new PassoMemoria { Titulo = "A1. Seções do modelo e materiais" };
        char letra = 'A';
        foreach (var t in r.Trechos)
            a1.Linhas.Add($"Seção {letra++} ({t.Secao.Replace("×", " x ")}), de x = {N(t.X0, "0.#")} a {N(t.X1, "0.#")} cm: A = {N(t.A, "0")} cm², I = {N(t.I, "0")} cm⁴.");
        a1.Linhas.Add($"Eci = {N(r.Eci * 10, "0")} MPa; Ecs = {N(r.Ecs * 10, "0")} MPa (NBR 6118 8.2.8). Rigidez secante (NBR 6118 15.7.3): (EI)sec = {N(r.FatorRigidezSecante)}·Eci·Ic = " +
                      $"{N(r.EIsec / 1e4, "0")} kN·m² (Ic = {N(r.Ic, "0")} cm⁴, seção do vão) — usada nos esforços e no αR; a flecha usa Ecs·Ic.");
        l.Add(a1);

        var a2 = new PassoMemoria { Titulo = "A2. Cargas (valores característicos)" };
        foreach (var (nome, c) in new[] { ("PP,laje", e.LajePP), ("CP,laje", e.LajeCP), ("SC,laje", e.LajeSC) })
            if (Math.Abs(c.Valor) > 0) a2.Linhas.Add($"{nome} = {N(c.Valor)} kN/m² × vão de influência {N(c.VaoInfluencia)} m = {N(c.Linear)} kN/m.");
        a2.Linhas.Add($"PP,viga = volume × peso específico / L = {N(r.PPViga)} kN/m (calculado); CP,viga = {N(e.VigaCP)} kN/m; SC,viga = {N(e.VigaSC)} kN/m (digitadas).");
        a2.Linhas.Add($"Totais por caso: PP = {N(r.Linear[CasoViga.PP])} kN/m; CP = {N(r.Linear[CasoViga.CP])} kN/m; SC = {N(r.Linear[CasoViga.SC])} kN/m.");
        foreach (var p in e.Pontuais) a2.Linhas.Add($"Carga pontual em x = {N(p.X, "0.#")} cm: G = {N(p.G)} kN (caso CP), Q = {N(p.Q)} kN (caso SC).");
        l.Add(a2);

        var a3 = new PassoMemoria { Titulo = "A3. Modelos de ligação (envoltória conservadora: rotulado + semirrígido)" };
        a3.Linhas.Add($"Apoios a {N(r.ApoioE, "0.#")} e {N(r.ApoioD, "0.#")} cm das pontas (0 = na ponta, a favor da segurança) → vão ℓ = {N(r.Vao, "0.#")} cm.");
        foreach (var m in r.Modelos)
            a3.Linhas.Add(m.AlfaRE <= 0 && m.AlfaRD <= 0 ? $"{m.Nome}: rótulas nos apoios."
                : $"{m.Nome}: Rsec = 3·(EI)sec/(ℓ·(1/αR − 1)) (NBR 9062, fator de restrição αR) = {(double.IsInfinity(m.RsecE) ? "engaste" : N(m.RsecE, "0") + " kN·m/rad")} (esq.) e " +
                  $"{(double.IsInfinity(m.RsecD) ? "engaste" : N(m.RsecD, "0") + " kN·m/rad")} (dir.).");
        a3.Linhas.Add("Barras de pórtico pelas seções acima (o dente com a altura dele), resolvidas no núcleo do PCEB; conferido com o PyNite.");
        l.Add(a3);

        var a4 = new PassoMemoria { Titulo = "A4. Esforços (ELU, combinação normal: γg·(PP + CP) + γq·SC)" };
        foreach (var m in r.Modelos)
        {
            string R(CasoViga c) => $"{c} {N(m.Reacoes[c].E, "0.0")} / {N(m.Reacoes[c].D, "0.0")}";
            a4.Linhas.Add($"{m.Nome}: reações características (kN, esq. / dir.): {R(CasoViga.PP)}; {R(CasoViga.CP)}; {R(CasoViga.SC)}. " +
                          $"Md,máx = {N(m.Elu.Max(p => p.M), "0.0")} kN·m; Md,mín = {N(m.Elu.Min(p => p.M), "0.0")} kN·m; |Vd|máx = {N(m.Elu.Max(p => Math.Abs(p.V)), "0.0")} kN.");
        }
        a4.Linhas.Add($"Envoltória usada no dimensionamento: Md,máx = {N(r.MdMax, "0.0")} kN·m; Md,mín = {N(r.MdMin, "0.0")} kN·m; Vd,máx = {N(r.VdMax, "0.0")} kN.");
        l.Add(a4);

        var a5 = new PassoMemoria { Titulo = "A5. Serviço: flecha (quase permanente: PP + CP + ψ2·SC)" };
        a5.Linhas.Add($"Mr = 1,5·fctm·Ic/yt = {N(r.Mr, "0.0")} kN·m; As (template) = {N(r.As)} cm²; As' = {N(r.AsLinha)} cm²; fluência αf = {N(r.AlfaF)} (NBR 6118 17.3.2.1.2).");
        foreach (var m in r.Modelos)
        {
            double el = m.Els.Max(p => p.Flecha);
            a5.Linhas.Add(m.FlechaTotal.Count == 0 ? $"{m.Nome}: flecha elástica = {N(el * 10)} mm."
                : $"{m.Nome}: Ma = {N(m.Ma, "0.0")} kN·m; Ie (Branson) = {N(m.Ie, "0")} cm⁴; flecha elástica = {N(el * 10)} mm; com fissuração e fluência = " +
                  $"{N(m.FlechaTotal.Max(p => p.Flecha) * 10)} mm ≤ ℓ/250 = {N(r.Vao / 250 * 10, "0.0")} mm.");
        }
        l.Add(a5);
        return l;
    }

    /// <summary>A memória inteira, linha a linha (título, passos, verificações, avisos), nas unidades da configuração.</summary>
    public static List<(string Texto, bool Titulo)> Linhas(ResultadoAnaliseViga r, EntradaAnaliseViga e, ResultadoCalculoViga calc, string nomeViga,
        UnidadesAnalise u = null, int largura = 170)
    {
        u ??= new UnidadesAnalise(null);
        var o = new List<(string, bool)> { ($"MEMÓRIA DE CÁLCULO — {nomeViga}".Trim(' ', '—'), true) };
        void Passo(PassoMemoria p)
        {
            o.Add((p.Titulo, true));
            foreach (var linha in p.Linhas)
                foreach (var pedaco in Quebrar(u.ConverterTexto(linha), largura)) o.Add(("  " + pedaco, false));
        }
        foreach (var p in PassosAnalise(r, e)) Passo(p);
        if (calc != null)
        {
            foreach (var p in calc.Memoria) Passo(p);
            o.Add(("Verificações", true));
            foreach (var x in calc.Verificacoes)
            {
                string sit = x.Situacao == SituacaoVerificacao.Ok ? "OK" : x.Situacao == SituacaoVerificacao.Conferir ? "CONFERIR" : "NÃO ATENDE";
                foreach (var pedaco in Quebrar(u.ConverterTexto($"{x.Codigo} [{sit}] {x.Descricao}: {x.Valor} {x.Limite} ({x.Referencia})" +
                                                                (string.IsNullOrWhiteSpace(x.Dica) || x.Situacao == SituacaoVerificacao.Ok ? "" : " — " + x.Dica)), largura))
                    o.Add(("  " + pedaco, false));
            }
            var avisos = calc.Avisos.Concat(r.Avisos).Distinct().ToList();
            if (avisos.Count > 0)
            {
                o.Add(("Avisos", true));
                foreach (var a in avisos) foreach (var pedaco in Quebrar(a, largura)) o.Add(("  " + pedaco, false));
            }
        }
        return o;
    }

    /// <summary>Quebra a linha em pedaços de até <paramref name="largura"/> caracteres, nos espaços.</summary>
    public static List<string> Quebrar(string s, int largura)
    {
        var r = new List<string>();
        s = (s ?? "").Trim();
        while (s.Length > largura)
        {
            int i = s.LastIndexOf(' ', largura);
            if (i <= 0) i = largura;
            r.Add(s.Substring(0, i).TrimEnd());
            s = s.Substring(i).TrimStart();
        }
        if (s.Length > 0) r.Add(s);
        return r;
    }
}
