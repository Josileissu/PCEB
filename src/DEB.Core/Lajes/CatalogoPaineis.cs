using System;
using System.Collections.Generic;
using DEB.Core.Persistencia;

namespace DEB.Core.Lajes;

/// <summary>Um painel de catálogo (medidas em metros).</summary>
public sealed class PainelCatalogo
{
    public string Nome = "";
    public double Altura, Largura, Junta;
    public override string ToString() => $"{Nome} (h = {Fmt.Virgula(Altura * 100, "0.#")} cm, b = {Fmt.Virgula(Largura * 100, "0.#")} cm)";
}

/// <summary>
/// Catálogo de painéis alveolares de um fornecedor. O arquivo fica FORA do repositório (pasta do usuário), apontado na
/// configuração. Formato (metros):
/// <code>{ "fornecedor": "…", "paineis": [ { "nome": "h16", "altura": 0.16, "largura": 1.245, "junta": 0.005 } ] }</code>
/// </summary>
public static class CatalogoPaineis
{
    public static List<PainelCatalogo> Ler(ValorJson raiz)
    {
        var r = new List<PainelCatalogo>();
        var lista = LeitorJson.Lista(LeitorJson.Objeto(raiz, "catálogo"), "paineis", "catálogo");
        for (int i = 0; i < lista.Itens.Count; i++)
        {
            var o = LeitorJson.Objeto(lista.Itens[i], $"catálogo.paineis[{i}]");
            string c = $"catálogo.paineis[{i}]";
            var p = new PainelCatalogo
            {
                Nome = LeitorJson.Txt(o, "nome", $"painel {i + 1}", c),
                Altura = LeitorJson.Num(o, "altura", 0, c),
                Largura = LeitorJson.Num(o, "largura", 0, c),
                Junta = LeitorJson.Num(o, "junta", 0.005, c),
            };
            if (p.Altura <= 0 || p.Largura <= 0) throw new FormatException($"{c}: altura e largura precisam ser positivas (em metros).");
            r.Add(p);
        }
        return r;
    }

    public static List<PainelCatalogo> LerArquivo(string caminho) => Ler(Json.LerArquivo(caminho));

    /// <summary>Aplica as medidas do painel à configuração (largura, junta, módulo, altura).</summary>
    public static void Aplicar(PainelCatalogo p, ConfigPaginacao cfg)
    {
        cfg.LarguraPainel = p.Largura;
        cfg.Junta = p.Junta;
        cfg.Modulo = Math.Round(p.Largura + p.Junta, 6);
        cfg.AlturaPainel = p.Altura;
    }
}
