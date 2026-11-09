using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Templates;
using DEB.Core.Templates.PilarPreMoldado;
using DEB.Core.Templates.VigaPreMoldada;

namespace DEB.Core.Intercambio;

/// <summary>Uma viga medida no modelo, antes do agrupamento (cm, sistema local: x ao longo, z para cima).</summary>
public sealed class VigaMedida
{
    public string Id { get; set; } = "";
    public string Marca { get; set; } = "";
    public double B { get; set; }
    public double H { get; set; }
    public double L { get; set; }
    public DentePeca DenteE { get; set; }
    public DentePeca DenteD { get; set; }
}

/// <summary>
/// Dentes Gerber pela geometria (2026-11-09): vale para qualquer família, sem depender dos parâmetros dela. Recebe o perfil
/// da viga no plano vertical do eixo — os vértices em (x, z), cm, com x de 0 a L e z de 0 a h — e olha cada ponta: a face
/// da ponta mais baixa que h com o topo em h = dente (recorte embaixo); o comprimento vai da ponta até onde começa o fundo.
/// </summary>
public static class PerfilViga
{
    public const double Tolerancia = 0.5;

    /// <summary>Dente de cada ponta (null = reta). Formas que não são dente nem ponta reta vão para <paramref name="avisos"/>.</summary>
    public static (DentePeca E, DentePeca D) Dentes(IReadOnlyList<(double X, double Z)> pts, double L, double H, List<string> avisos)
    {
        var e = Ponta(pts, H, "esquerda", avisos);
        var d = Ponta(pts.Select(p => (L - p.X, p.Z)).ToList(), H, "direita", avisos);
        return (e, d);
    }

    private static DentePeca Ponta(IReadOnlyList<(double X, double Z)> pts, double H, string lado, List<string> avisos)
    {
        var face = pts.Where(p => p.X <= Tolerancia).ToList();
        if (face.Count == 0) { avisos.Add($"ponta {lado}: sem face vertical na ponta (chanfrada?); considerada reta."); return null; }
        double zMin = face.Min(p => p.Z), zMax = face.Max(p => p.Z);
        if (zMax - zMin >= H - Tolerancia) return null;
        if (zMax < H - Tolerancia)
        {
            avisos.Add($"ponta {lado}: a face da ponta não chega ao topo (vai de {N(zMin)} a {N(zMax)} cm); só o dente com recorte embaixo é reconhecido. Considerada reta.");
            return null;
        }
        var fundo = pts.Where(p => p.Z <= Tolerancia).ToList();
        if (fundo.Count == 0) { avisos.Add($"ponta {lado}: fundo da viga não encontrado; considerada reta."); return null; }
        return new DentePeca { Altura = Math.Round(zMax - zMin, 2), Comprimento = Math.Round(fundo.Min(p => p.X), 2) };
    }

    private static string N(double x) => x.ToString("0.#", CultureInfo.InvariantCulture);
}

/// <summary>
/// Vigas iguais = mesma seção, comprimento e dentes; invertidas (dente da esquerda ↔ direita) também são iguais, como os
/// pilares girados. O representante fica na orientação de menor assinatura; as invertidas recebem giro 180.
/// </summary>
public static class AgrupadorVigas
{
    public static List<VigaPeca> Agrupar(IEnumerable<VigaMedida> vigas, string prefixo = "V")
    {
        var itens = vigas.Select(v =>
        {
            string direta = Assinatura(v.B, v.H, v.L, v.DenteE, v.DenteD), inversa = Assinatura(v.B, v.H, v.L, v.DenteD, v.DenteE);
            bool inverter = string.CompareOrdinal(inversa, direta) < 0;
            return (v, chave: inverter ? inversa : direta, inverter);
        }).ToList();

        var grupos = itens.GroupBy(i => i.chave)
            .Select(g =>
            {
                var membros = g.OrderBy(i => i.v.Id, StringComparer.Ordinal).ToList();
                var r = membros[0];
                return new VigaPeca
                {
                    B = r.v.B, H = r.v.H, L = r.v.L,
                    DenteE = r.inverter ? r.v.DenteD : r.v.DenteE,
                    DenteD = r.inverter ? r.v.DenteE : r.v.DenteD,
                    Elementos = membros.Select(m => new ElementoPeca { Id = m.v.Id, Marca = m.v.Marca, Giro = m.inverter != r.inverter ? 180 : 0 }).ToList(),
                };
            })
            .OrderBy(p => p.B * p.H).ThenBy(p => p.L).ThenBy(p => p.DenteE == null ? 0 : 1).ThenBy(p => p.DenteD == null ? 0 : 1)
            .ToList();
        for (int i = 0; i < grupos.Count; i++) grupos[i].Marca = prefixo + (i + 1).ToString(CultureInfo.InvariantCulture);
        return grupos;
    }

    /// <summary>Medidas arredondadas ao milímetro, como a assinatura dos pilares.</summary>
    private static string Assinatura(double b, double h, double l, DentePeca e, DentePeca d)
    {
        string M(double cm) => Math.Round(cm * 10).ToString("0", CultureInfo.InvariantCulture);
        string D(DentePeca x) => x == null ? "R" : $"G{M(x.Altura)}x{M(x.Comprimento)}";
        return $"{M(b)}x{M(h)}x{M(l)}|{D(e)}|{D(d)}";
    }
}

/// <summary>Grupo de vigas do modelo → valores do template "viga-premoldada" (pontas pelo catálogo do ARMDENTES).</summary>
public static class ConversorVigaPeca
{
    public const double Tolerancia = 0.6;

    /// <summary>Valores do template; null se um dente não estiver no catálogo (não dá para desenhar a peça certa).</summary>
    public static ValoresTemplate Valores(VigaPeca p, ValoresTemplate valoresBase, IReadOnlyList<TipoDente> catalogo, List<string> avisos)
    {
        string extE = Extremidade(p, p.DenteE, "esquerda", catalogo, avisos), extD = Extremidade(p, p.DenteD, "direita", catalogo, avisos);
        if (extE == null || extD == null) return null;
        var v = new ValoresTemplate();
        foreach (var kv in valoresBase.Todos) v[kv.Key] = kv.Value;
        v["Nome"] = p.Marca;
        v["Quantidade"] = p.Quantidade.ToString(CultureInfo.InvariantCulture);
        v["B"] = N(p.B);
        v["H"] = N(p.H);
        v["L"] = N(p.L);
        v["ExtE"] = extE;
        v["ExtD"] = extD;
        return v;
    }

    private static string Extremidade(VigaPeca p, DentePeca d, string lado, IReadOnlyList<TipoDente> catalogo, List<string> avisos)
    {
        if (d == null) return TemplateVigaPreMoldada.Reta;
        var t = catalogo.FirstOrDefault(x => Math.Abs(x.Altura - d.Altura) <= Tolerancia && Math.Abs(x.Comprimento - d.Comprimento) <= Tolerancia);
        if (t != null) return t.Nome;
        avisos.Add($"{p.Marca}: dente da ponta {lado} ({TemplatePilarPreMoldado.Num(d.Altura, "0.#")} x {TemplatePilarPreMoldado.Num(d.Comprimento, "0.#")}: " +
                   "altura × comprimento) não está no catálogo (ARMDENTES); a viga não foi gerada.");
        return null;
    }

    private static string N(double x) => x.ToString("0.##", CultureInfo.InvariantCulture);
}
