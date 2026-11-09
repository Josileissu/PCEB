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
    /// <summary>Identificador estável do elemento no modelo (UniqueId do Revit); vazio = não informado.</summary>
    public string Uid { get; set; } = "";
    /// <summary>Pilar: cota da base (m), no referencial dos níveis do modelo.</summary>
    public double CotaBase { get; set; }
    /// <summary>Pilar: comprimento embutido no cálice (cm); null = o modelo não informa.</summary>
    public double? Embutimento { get; set; }
    /// <summary>Pilar: níveis do modelo entre a base e o topo (nome, cota em m); null = o modelo não informa.</summary>
    public List<(string Nome, double Cota)> Niveis { get; set; }
    public TipoPeca Tipo { get; set; }
    public string Marca { get; set; } = "";
    /// <summary>Decisão do usuário na conferência: a peça não vai para o CAD (gravada no elemento do modelo).</summary>
    public bool NaoExportar { get; set; }
    public string Familia { get; set; } = "";
    public string TipoRevit { get; set; } = "";
    public string Material { get; set; } = "";
    public bool PreMoldada { get; set; }
    /// <summary>Comprimento ao longo do eixo (viga) ou altura (pilar), cm.</summary>
    public double Comprimento { get; set; }
    public List<TrechoPeca> Trechos { get; set; } = new List<TrechoPeca>();
    /// <summary>Pilar: consolos (famílias separadas ou aninhadas noutra peça, reconhecidos como no botão "Pilares pré-moldados").</summary>
    public List<ConsoloLevantado> Consolos { get; set; } = new List<ConsoloLevantado>();
    /// <summary>Pilar: seção do pilar sem os consolos (largura em x = face 2/4, em y = face 1/3), cm.</summary>
    public double Bx { get; set; }
    public double By { get; set; }
    /// <summary>Avisos do levantamento (corte que falhou, seção em mais de um pedaço…) e da conferência.</summary>
    public List<string> Avisos { get; set; } = new List<string>();
    public SituacaoPeca Situacao { get; set; }

    public string FamiliaTipo => string.IsNullOrEmpty(TipoRevit) ? Familia : $"{Familia}: {TipoRevit}";

    /// <summary>Trecho mais longo (a seção "corrente").</summary>
    public TrechoPeca Principal => Trechos.OrderByDescending(t => t.Comprimento).FirstOrDefault();
}

/// <summary>
/// Consolo de um pilar (2026-11-09): face (1 = +x, 2 = −y, 3 = −x, 4 = +y, como no template), trecho ao longo da face
/// (<see cref="S0"/>–<see cref="S1"/>, cm no sistema do pilar: y nas faces 1 e 3, x nas 2 e 4) e o perfil exato cortado no meio
/// da largura — Y = distância para fora da face, Z = altura a partir da base do pilar (com chanfros).
/// </summary>
public sealed class ConsoloLevantado
{
    public int Face { get; set; }
    public double S0 { get; set; }
    public double S1 { get; set; }
    public SecaoPoligonal Perfil { get; set; }
    public PerfilConsolo.Medidas Medidas => PerfilConsolo.Medir(Perfil.Contorno.Select(p => (p.Y, p.Z)));
    public double Largura => S1 - S0;
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
                if (t.FuncaoSugerida == FuncaoTrecho.ApoioViga)
                {
                    naoSuportada = true;
                    p.Avisos.Add($"trecho {N(t.X0)}–{N(t.X1)}: consolo de apoio de outra viga — o CAD ainda não desenha consolo em viga");
                    continue;
                }
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
            string a = p.Tipo == TipoPeca.Pilar ? AssinaturaPilar(p).Assinatura : Assinatura(p, false);
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

    /// <summary>
    /// Pilar: girados 90/180/270° = iguais (decisão do usuário; quadrado testa os 4 giros, retangular só 0 e 180°). Trechos pela
    /// altura, área e lados (sem orientação); consolos renumerados a cada giro — face 1→4, 2→1, 3→2, 4→3, com a posição ao longo
    /// da face espelhada nas faces que trocam de eixo — e a menor assinatura vale.
    /// </summary>
    /// <summary>
    /// Giro (graus) de <paramref name="p"/> em relação a <paramref name="representante"/>, do mesmo grupo: pilar = 0/90/180/270
    /// (diferença dos giros que levam cada um à assinatura do grupo); viga = 180 se uma está invertida em relação à outra.
    /// </summary>
    public static int GiroRelativo(PecaLevantada p, PecaLevantada representante)
    {
        if (ReferenceEquals(p, representante)) return 0;
        if (p.Tipo == TipoPeca.Pilar)
            return ((AssinaturaPilar(p).Giros - AssinaturaPilar(representante).Giros) % 4 + 4) % 4 * 90;
        bool Inv(PecaLevantada x) => string.CompareOrdinal(Assinatura(x, true), Assinatura(x, false)) < 0;
        return Inv(p) != Inv(representante) ? 180 : 0;
    }

    private static (string Assinatura, int Giros) AssinaturaPilar(PecaLevantada p)
    {
        string M(double cm) => Math.Round(cm * 10).ToString("0", CultureInfo.InvariantCulture);
        string trechos = string.Join(";", p.Trechos.Select(x =>
            $"{M(x.Comprimento)}:{M(Math.Min(x.Secao.B, x.Secao.H))}x{M(Math.Max(x.Secao.B, x.Secao.H))}:{Math.Round(x.Secao.Area):0}"));
        var cs = p.Consolos.Select(c => (c.Face, c.S0, c.S1, c)).ToList();
        double bx = p.Bx, by = p.By;
        bool quadrado = Math.Abs(bx - by) < 0.1;
        string melhor = null;
        int giros = 0;
        // o embutimento no cálice faz parte da peça (muda as zonas de estribos): pilares com embutimentos diferentes não são iguais
        string emb = p.Embutimento.HasValue ? M(p.Embutimento.Value) : "-";
        for (int k = 0; k < 4; k++)
        {
            if (quadrado || k % 2 == 0)
            {
                string a = $"Pilar|{M(Math.Min(bx, by))}x{M(Math.Max(bx, by))}|{M(p.Comprimento)}|e{emb}|{trechos}|" + string.Join(";", cs
                    .OrderBy(c => c.Face).ThenBy(c => c.c.Perfil.ZMax).ThenBy(c => c.S0)
                    .Select(c => $"{c.Face}:{M(c.S0)}-{M(c.S1)}:{M(c.c.Perfil.ZMax)}:{M(c.c.Medidas.Balanco)}:{M(c.c.Medidas.AlturaPonta)}"));
                if (melhor == null || string.CompareOrdinal(a, melhor) < 0) { melhor = a; giros = k; }
            }
            // gira 90°: (x, y) → (by − y, x)
            cs = cs.Select(c => c.Face switch
            {
                1 => (4, by - c.S1, by - c.S0, c.c),
                2 => (1, c.S0, c.S1, c.c),
                3 => (2, by - c.S1, by - c.S0, c.c),
                _ => (3, c.S0, c.S1, c.c),
            }).ToList();
            (bx, by) = (by, bx);
        }
        return (melhor, giros);
    }

    /// <summary>Trechos (comprimento, caixa, área, CG) ao milímetro; invertida = trechos de trás para a frente.</summary>
    private static string Assinatura(PecaLevantada p, bool invertida)
    {
        string M(double cm) => Math.Round(cm * 10).ToString("0", CultureInfo.InvariantCulture);
        var t = invertida ? Enumerable.Reverse(p.Trechos) : p.Trechos;
        string consolos = string.Join(";", p.Consolos.OrderBy(c => c.Face).ThenBy(c => c.Perfil.ZMax).ThenBy(c => c.S0)
            .Select(c => $"{c.Face}:{M(c.S0)}-{M(c.S1)}:{M(c.Perfil.ZMax)}:{M(c.Medidas.Balanco)}:{M(c.Medidas.AlturaPonta)}"));
        return $"{p.Tipo}|{M(p.Comprimento)}|{consolos}|" + string.Join(";", t.Select(x =>
        {
            var s = x.Secao;
            double cgy = s.CG.Y - s.YMin;
            if (invertida) cgy = s.B - cgy;   // virada de ponta a ponta: a largura espelha
            return $"{M(x.Comprimento)}:{M(s.B)}x{M(s.H)}:{Math.Round(s.Area):0}:{M(cgy)}:{M(s.CG.Z - s.ZMin)}";
        }));
    }

    private static string N(double x) => x.ToString("0.#", CultureInfo.InvariantCulture);
}
