using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DEB.Core.Pecas;

public enum TipoPeca { Viga, Pilar }

/// <summary>Situação da peça na conferência: ok; atenção (o usuário confere); seção que o CAD ainda não desenha.</summary>
public enum SituacaoPeca { Ok, Atencao, NaoSuportada }

/// <summary>
/// Peça levantada do modelo para a conferência (2026-11-09): o que o Revit diz (família, tipo, marca, material) e o que a
/// geometria mede (comprimento e trechos de seção constante). Ver docs/04_arquitetura/conferencia_e_subdivisao_pecas.md.
/// </summary>
public sealed class PecaLevantada
{
    public string Id { get; set; } = "";
    public TipoPeca Tipo { get; set; }
    public string Marca { get; set; } = "";
    public string Familia { get; set; } = "";
    public string TipoRevit { get; set; } = "";
    public string Material { get; set; } = "";
    public bool PreMoldada { get; set; }
    /// <summary>Comprimento ao longo do eixo (viga) ou altura (pilar), cm.</summary>
    public double Comprimento { get; set; }
    public List<TrechoPeca> Trechos { get; set; } = new List<TrechoPeca>();
    /// <summary>Avisos do levantamento (corte que falhou, seção em mais de um pedaço…) e da conferência.</summary>
    public List<string> Avisos { get; set; } = new List<string>();
    public SituacaoPeca Situacao { get; set; }

    public string FamiliaTipo => string.IsNullOrEmpty(TipoRevit) ? Familia : $"{Familia}: {TipoRevit}";

    /// <summary>Trecho mais longo (a seção "corrente").</summary>
    public TrechoPeca Principal => Trechos.OrderByDescending(t => t.Comprimento).FirstOrDefault();
}

/// <summary>Peças iguais (mesmos trechos; vigas invertidas também) — o representante é a primeira.</summary>
public sealed class GrupoPecas
{
    public List<PecaLevantada> Pecas { get; } = new List<PecaLevantada>();
    public PecaLevantada Representante => Pecas[0];
    public int Quantidade => Pecas.Count;
    public string Marcas => string.Join(", ", Pecas.Select(p => string.IsNullOrWhiteSpace(p.Marca) ? "(sem marca)" : p.Marca).Distinct());
    public List<string> Avisos { get; } = new List<string>();
    public SituacaoPeca Situacao { get; set; }
}

/// <summary>Verificações e agrupamento da conferência (sem Revit: testável).</summary>
public static class ConferenciaPecas
{
    /// <summary>Preenche os avisos e a situação da peça.</summary>
    public static void Avaliar(PecaLevantada p)
    {
        if (p.Trechos.Count == 0)
        {
            p.Avisos.Add("sem seção: a geometria não pôde ser cortada");
            p.Situacao = SituacaoPeca.Atencao;
            return;
        }
        if (!p.PreMoldada) p.Avisos.Add($"material \"{p.Material}\" não é concreto pré-moldado");
        var nome = NomeTipo.Conferir(p.TipoRevit, p.Principal.Secao);
        if (nome != null) p.Avisos.Add("nome do tipo: " + nome);
        bool naoSuportada = false;
        if (p.Tipo == TipoPeca.Viga)
            foreach (var t in p.Trechos)
            {
                var (tipo, desc) = t.Secao.Classificar();
                if (tipo == TipoSecao.Retangular) continue;
                naoSuportada = true;
                p.Avisos.Add($"trecho {N(t.X0)}–{N(t.X1)}: {desc} — o CAD ainda só desenha a seção retangular (VP-4)");
            }
        p.Situacao = naoSuportada ? SituacaoPeca.NaoSuportada : p.Avisos.Count > 0 ? SituacaoPeca.Atencao : SituacaoPeca.Ok;
    }

    /// <summary>Agrupa as peças iguais pela geometria (o nome não conta: nomes diferentes no mesmo grupo viram aviso).</summary>
    public static List<GrupoPecas> Agrupar(IEnumerable<PecaLevantada> pecas)
    {
        var grupos = new Dictionary<string, GrupoPecas>();
        foreach (var p in pecas)
        {
            string a = Assinatura(p, false);
            if (p.Tipo == TipoPeca.Viga) { string inv = Assinatura(p, true); if (string.CompareOrdinal(inv, a) < 0) a = inv; }
            if (!grupos.TryGetValue(a, out var g)) grupos[a] = g = new GrupoPecas();
            g.Pecas.Add(p);
        }
        var r = grupos.Values.ToList();
        foreach (var g in r)
        {
            g.Pecas.Sort((x, y) => string.CompareOrdinal(x.Marca + "|" + x.Id, y.Marca + "|" + y.Id));
            foreach (var av in g.Pecas.SelectMany(p => p.Avisos).Distinct()) g.Avisos.Add(av);
            var tipos = g.Pecas.Select(p => p.FamiliaTipo).Distinct().ToList();
            if (tipos.Count > 1) g.Avisos.Add("peças iguais com tipos diferentes no Revit: " + string.Join(" | ", tipos));
            g.Situacao = g.Pecas.Max(p => p.Situacao);
            if (tipos.Count > 1 && g.Situacao == SituacaoPeca.Ok) g.Situacao = SituacaoPeca.Atencao;
        }
        return r.OrderBy(g => g.Representante.Tipo).ThenBy(g => g.Representante.Marca, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Representante.Comprimento).ToList();
    }

    /// <summary>Trechos (comprimento, caixa, área, CG) ao milímetro; invertida = trechos de trás para a frente.</summary>
    private static string Assinatura(PecaLevantada p, bool invertida)
    {
        string M(double cm) => Math.Round(cm * 10).ToString("0", CultureInfo.InvariantCulture);
        var t = invertida ? Enumerable.Reverse(p.Trechos) : p.Trechos;
        return $"{p.Tipo}|{M(p.Comprimento)}|" + string.Join(";", t.Select(x =>
        {
            var s = x.Secao;
            double cgy = s.CG.Y - s.YMin;
            if (invertida) cgy = s.B - cgy;   // virada de ponta a ponta: a largura espelha
            return $"{M(x.Comprimento)}:{M(s.B)}x{M(s.H)}:{Math.Round(s.Area):0}:{M(cgy)}:{M(s.CG.Z - s.ZMin)}";
        }));
    }

    private static string N(double x) => x.ToString("0.#", CultureInfo.InvariantCulture);
}
