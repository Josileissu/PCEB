using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Templates;
using DEB.Core.Templates.PilarPreMoldado;

namespace DEB.Core.Intercambio;

/// <summary>
/// Grupo de pilares do modelo → valores do template "pilar-premoldado" (2026-11-09). Parte dos valores de base (o PADRÃO do
/// usuário) e troca o que o modelo informa: nome, quantidade, seção, comprimento, cota da base, embutimento, níveis e
/// consolos. Os consolos são reconhecidos pelo catálogo (ARMCONSOLOS) pelas medidas; dois iguais em faces opostas na
/// mesma altura viram um tipo duplo, se houver. O que não casar fica de fora, com aviso — nada é inventado.
/// </summary>
public static class ConversorPilarPeca
{
    /// <summary>Tolerância para casar as medidas do modelo com as do catálogo (cm).</summary>
    public const double Tolerancia = 0.6;

    public static ValoresTemplate Valores(PilarPeca p, ValoresTemplate valoresBase, IReadOnlyList<TipoConsolo> catalogo, List<string> avisos)
    {
        var v = new ValoresTemplate();
        foreach (var kv in valoresBase.Todos) v[kv.Key] = kv.Value;

        v["Nome"] = p.Marca;
        v["Quantidade"] = p.Quantidade.ToString(CultureInfo.InvariantCulture);
        v["B"] = N(p.B);
        v["H"] = N(p.H);
        v["L"] = N(p.L);
        v["CotaBase"] = N(p.CotaBase, "0.###");
        if (p.Embutimento.HasValue) v["Lemb"] = N(Math.Min(p.Embutimento.Value, p.L));
        if (p.Niveis != null)
            v["Niveis"] = ValoresTemplate.JuntarTabela(p.Niveis.OrderBy(n => n.Cota).Select(n => new[] { n.Nome, "", N(n.Cota, "0.###") }));
        v["Consolos"] = ValoresTemplate.JuntarTabela(LinhasConsolos(p, catalogo, avisos));
        return v;
    }

    /// <summary>Linhas da tabela "Consolos" do template: tipo | faces | base → topo | deslocamento.</summary>
    /// <param name="faltantes">Se dado, recebe os consolos sem tipo no catálogo e se têm par na face oposta (para criar o tipo).</param>
    public static List<string[]> LinhasConsolos(PilarPeca p, IReadOnlyList<TipoConsolo> catalogo, List<string> avisos,
        List<(ConsoloPeca C, bool ComPar)> faltantes = null)
    {
        var linhas = new List<string[]>();
        var usados = new HashSet<ConsoloPeca>();
        var ordem = p.Consolos.OrderBy(c => c.Topo).ThenBy(c => c.Face).ToList();
        foreach (var c in ordem)
        {
            if (usados.Contains(c)) continue;
            usados.Add(c);
            double desl = Deslocamento(p, c);

            // par na face oposta, mesma altura e medidas, mesmo deslocamento (o template espelha o duplo pelo centro)
            var par = ordem.FirstOrDefault(o => !usados.Contains(o) && o.Face == Oposta(c.Face) && MesmaForma(o, c) &&
                                                Math.Abs(Deslocamento(p, o) - desl) <= Tolerancia);
            var duplo = par == null ? null : catalogo.FirstOrDefault(t => t.Duplo && Casa(t, c));
            if (duplo != null)
            {
                usados.Add(par);
                linhas.Add(new[] { duplo.Nome, c.Face % 2 == 1 ? "1 e 3" : "2 e 4", N(c.Topo), N(desl) });
                continue;
            }
            var simples = catalogo.FirstOrDefault(t => !t.Duplo && Casa(t, c));
            if (simples != null) { linhas.Add(new[] { simples.Nome, c.Face.ToString(CultureInfo.InvariantCulture), N(c.Topo), N(desl) }); continue; }
            faltantes?.Add((c, par != null));
            avisos.Add($"{p.Marca}: consolo da face {c.Face} com topo a {N(c.Topo)} cm da base ({Resumo(c)}) não está no catálogo " +
                       "(ARMCONSOLOS) e ficou de fora.");
        }
        return linhas;
    }

    /// <summary>
    /// Deslocamento lateral do centro do consolo em relação ao centro da face, na convenção do template:
    /// face 1 → +y, face 2 → +x, face 3 → −y, face 4 → −x.
    /// </summary>
    public static double Deslocamento(PilarPeca p, ConsoloPeca c)
    {
        double meio = (c.Inicio + c.Fim) / 2;
        return c.Face switch
        {
            1 => meio - p.H / 2,
            2 => meio - p.B / 2,
            3 => p.H / 2 - meio,
            _ => p.B / 2 - meio,
        };
    }

    public static int Oposta(int face) => (face + 1) % 4 + 1;

    public static bool Casa(TipoConsolo t, ConsoloPeca c) =>
        Perto(t.B, c.Largura) && Perto(t.A, c.Balanco) && Perto(t.H, c.AlturaFace) && Perto(Math.Min(t.H1, t.H), c.AlturaPonta);

    private static bool MesmaForma(ConsoloPeca a, ConsoloPeca b) =>
        Perto(a.Topo, b.Topo) && Perto(a.Base, b.Base) && Perto(a.Largura, b.Largura) && Perto(a.Balanco, b.Balanco) && Perto(a.AlturaPonta, b.AlturaPonta);

    private static bool Perto(double a, double b) => Math.Abs(a - b) <= Tolerancia;

    /// <summary>"largura 40 × altura 23,5 (ponta 23,5) × balanço 25".</summary>
    public static string Resumo(ConsoloPeca c) =>
        $"largura {KitDesenho.Num(c.Largura, "0.#")} × altura {KitDesenho.Num(c.AlturaFace, "0.#")}" +
        (Math.Abs(c.AlturaPonta - c.AlturaFace) > Tolerancia ? $" (ponta {KitDesenho.Num(c.AlturaPonta, "0.#")})" : "") +
        $" × balanço {KitDesenho.Num(c.Balanco, "0.#")}";

    private static string N(double x, string formato = "0.##") => Fmt.N(x, formato);
}
