using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Templates;
using DEB.Core.Templates.PilarPreMoldado;
using DEB.Core.Templates.VigaPreMoldada;

namespace DEB.Core.Intercambio;

/// <summary>Grupo de vigas do modelo → valores do template "viga-premoldada" (pontas pelo catálogo do ARMDENTES).</summary>
public static class ConversorVigaPeca
{
    public const double Tolerancia = 0.6;

    /// <summary>Valores do template; null se um dente não estiver no catálogo (não dá para desenhar a peça certa).</summary>
    public static ValoresTemplate Valores(VigaPeca p, ValoresTemplate valoresBase, IReadOnlyList<TipoDente> catalogo, List<string> avisos)
    {
        // um aviso só por viga, com as pontas que faltam
        var faltam = new List<string>();
        string extE = Extremidade(p.DenteE, "esquerda", catalogo, faltam), extD = Extremidade(p.DenteD, "direita", catalogo, faltam);
        if (faltam.Count > 0)
        {
            avisos.Add($"{p.Marca}: {string.Join("; ", faltam)} não está no catálogo (ARMDENTES); a viga não foi gerada.");
            return null;
        }
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

    /// <summary>O tipo do catálogo tem as medidas do dente do modelo (altura e comprimento, dentro da tolerância)?</summary>
    public static bool Casa(TipoDente t, DentePeca d) =>
        Math.Abs(t.Altura - d.Altura) <= Tolerancia && Math.Abs(t.Comprimento - d.Comprimento) <= Tolerancia;

    private static string Extremidade(DentePeca d, string lado, IReadOnlyList<TipoDente> catalogo, List<string> faltam)
    {
        if (d == null) return TemplateVigaPreMoldada.Reta;
        var t = catalogo.FirstOrDefault(x => Casa(x, d));
        if (t != null) return t.Nome;
        faltam.Add($"dente da ponta {lado} ({TemplatePilarPreMoldado.Num(d.Altura, "0.#")} x {TemplatePilarPreMoldado.Num(d.Comprimento, "0.#")}: altura × comprimento)");
        return null;
    }

    private static string N(double x) => x.ToString("0.##", CultureInfo.InvariantCulture);
}
