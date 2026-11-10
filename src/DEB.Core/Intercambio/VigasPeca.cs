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
        // aba (L / T invertido): a geometria vem do modelo; a armadura da aba fica a do PADRÃO do usuário
        // ressalto sobre a alma: geometria do modelo; a armadura (ressalto e ligação com a capa) fica a do PADRÃO do usuário
        if (p.Ressalto == null) v["Ressalto"] = TemplateVigaPreMoldada.RessaltoSem;
        else
        {
            v["Ressalto"] = p.Ressalto.Lado == AbaPeca.Esquerda ? TemplateVigaPreMoldada.RessaltoEsq : TemplateVigaPreMoldada.RessaltoDir;
            v["RessaltoLargura"] = N(p.Ressalto.Largura);
            v["RessaltoAltura"] = N(p.Ressalto.Altura);
        }
        if (p.Aba == null) v["Aba"] = TemplateVigaPreMoldada.AbaSem;
        else
        {
            v["Aba"] = p.Aba.Lado == AbaPeca.DoisLados ? TemplateVigaPreMoldada.AbaDois : p.Aba.Lado == AbaPeca.Esquerda ? TemplateVigaPreMoldada.AbaEsq : TemplateVigaPreMoldada.AbaDir;
            v["AbaLargura"] = N(p.Aba.Largura);
            v["AbaAltura"] = N(p.Aba.Altura);
            v["AbaAlturaPonta"] = p.Aba.AlturaPonta > 0 && p.Aba.AlturaPonta < p.Aba.Altura - 0.05 ? N(p.Aba.AlturaPonta) : "0";
            v["AbaBase"] = N(p.Aba.Base);
            // o trecho é o do modelo: se ele entra num dente, a aba passa pelos dentes (mesmo descendo abaixo do recorte)
            bool entra = (p.DenteE != null && p.Aba.Inicio < p.DenteE.Comprimento - 0.5) || (p.DenteD != null && p.Aba.Fim > p.L - p.DenteD.Comprimento + 0.5);
            v["AbaDentes"] = entra ? TemplateVigaPreMoldada.AbaDentesPassa : TemplateVigaPreMoldada.AbaDentesPara;
            v["AbaIni"] = N(p.Aba.Inicio);
            v["AbaFim"] = N(p.Aba.Fim);
        }
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
        faltam.Add($"dente da ponta {lado} ({KitDesenho.Num(d.Altura, "0.#")} x {KitDesenho.Num(d.Comprimento, "0.#")}: altura × comprimento)");
        return null;
    }

    private static string N(double x) => Fmt.N(x, "0.##");
}
