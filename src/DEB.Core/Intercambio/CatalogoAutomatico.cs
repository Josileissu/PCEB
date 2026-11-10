using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Templates.PilarPreMoldado;
using DEB.Core.Templates.VigaPreMoldada;

namespace DEB.Core.Intercambio;

/// <summary>
/// Dentes e consolos do modelo que faltam nos catálogos do escritório (decisão do usuário, 2026-11-09: opção b). Em vez de
/// descartar a viga ou deixar o consolo de fora, a importação cria a entrada com a GEOMETRIA do modelo e a armadura padrão
/// do tipo novo — e avisa para conferir a armadura no ARMDENTES/ARMCONSOLOS (ela vem do dimensionamento de cada projeto).
/// Um consolo com par igual na face oposta vira um tipo duplo (o tirante passa pelo pilar), como o conversor espera.
/// </summary>
public static class CatalogoAutomatico
{
    public const string Origem = DEB.Core.Templates.MarcaConferir.Origem;

    /// <summary>Tipos de dente a acrescentar ao catálogo para que todas as vigas tenham as pontas reconhecidas.</summary>
    public static List<TipoDente> Dentes(IEnumerable<VigaPeca> vigas, IReadOnlyList<TipoDente> catalogo)
    {
        var novos = new List<TipoDente>();
        foreach (var d in vigas.SelectMany(v => new[] { v.DenteE, v.DenteD }).Where(x => x != null))
        {
            if (catalogo.Concat(novos).Any(t => ConversorVigaPeca.Casa(t, d))) continue;
            string nome = NomeLivre($"DG {Num(d.Altura)}x{Num(d.Comprimento)}", catalogo.Select(t => t.Nome).Concat(novos.Select(t => t.Nome)));
            novos.Add(new TipoDente { Nome = nome, Descricao = Origem, Altura = d.Altura, Comprimento = d.Comprimento });
        }
        return novos;
    }

    /// <summary>Tipos de consolo a acrescentar ao catálogo para que todos os consolos dos pilares entrem no detalhamento.</summary>
    /// <param name="cobrimento">Cobrimento das faces do pilar (o do PADRÃO do usuário): a almofada do tipo novo cabe dentro dele.</param>
    public static List<TipoConsolo> Consolos(IEnumerable<PilarPeca> pilares, IReadOnlyList<TipoConsolo> catalogo, double cobrimento = 2.5)
    {
        var novos = new List<TipoConsolo>();
        foreach (var p in pilares)
        {
            var faltantes = new List<(ConsoloPeca C, bool ComPar)>();
            ConversorPilarPeca.LinhasConsolos(p, catalogo.Concat(novos).ToList(), new List<string>(), faltantes);
            foreach (var (c, duplo) in faltantes)
            {
                if (catalogo.Concat(novos).Any(t => t.Duplo == duplo && ConversorPilarPeca.Casa(t, c))) continue;
                double h = c.AlturaFace, h1 = Math.Min(c.AlturaPonta, h);
                bool trap = h - h1 > ConversorPilarPeca.Tolerancia;
                string nome = (duplo ? "CD " : trap ? "CT " : "CS ") + $"{Num(c.Largura)}x{Num(h)}" + (trap ? "-" + Num(h1) : "") + $"x{Num(c.Balanco)}";
                novos.Add(new TipoConsolo
                {
                    Nome = NomeLivre(nome, catalogo.Select(t => t.Nome).Concat(novos.Select(t => t.Nome))), Descricao = Origem,
                    Duplo = duplo, B = c.Largura, A = c.Balanco, H = h, H1 = h1,
                    // almofada do padrão (30 × 15) só se couber: largura e balanço menos o cobrimento de cada lado
                    AlmB = Math.Min(new TipoConsolo().AlmB, Math.Max(5, Math.Floor(c.Largura - 2 * cobrimento))),
                    AlmA = Math.Min(new TipoConsolo().AlmA, Math.Max(5, Math.Floor(c.Balanco - 2 * cobrimento))),
                });
            }
        }
        return novos;
    }

    private static string NomeLivre(string nome, IEnumerable<string> usados)
    {
        var u = new HashSet<string>(usados.Select(x => x?.Trim() ?? ""), StringComparer.OrdinalIgnoreCase);
        if (!u.Contains(nome)) return nome;
        for (int i = 2; ; i++) if (!u.Contains($"{nome} ({i})")) return $"{nome} ({i})";
    }

    private static string Num(double x) => TemplatePilarPreMoldado.Num(x, "0.#");
}
