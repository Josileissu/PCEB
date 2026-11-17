using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Documentacao;

/// <summary>Retângulo alinhado aos eixos (unidades do desenho).</summary>
public readonly struct CaixaPdf
{
    public double X0 { get; }
    public double Y0 { get; }
    public double X1 { get; }
    public double Y1 { get; }
    public CaixaPdf(double x0, double y0, double x1, double y1)
    {
        X0 = Math.Min(x0, x1); Y0 = Math.Min(y0, y1); X1 = Math.Max(x0, x1); Y1 = Math.Max(y0, y1);
    }
    public double Largura => X1 - X0;
    public double Altura => Y1 - Y0;
    public double Area => Largura * Altura;
    public double Maior => Math.Max(Largura, Altura);

    /// <summary>Contém <paramref name="o"/> (com folga relativa ao tamanho desta caixa).</summary>
    public bool Contem(CaixaPdf o, double folgaRelativa = 1e-3)
    {
        double t = folgaRelativa * Math.Max(1e-9, Maior);
        return o.X0 >= X0 - t && o.Y0 >= Y0 - t && o.X1 <= X1 + t && o.Y1 <= Y1 + t;
    }
}

/// <summary>Candidato a moldura (polilinha retangular ou bloco) lido do desenho.</summary>
public sealed class CandidatoMoldura
{
    public CaixaPdf Caixa { get; set; }
    public string Origem { get; set; } = "";
    public CandidatoMoldura() { }
    public CandidatoMoldura(CaixaPdf caixa, string origem) { Caixa = caixa; Origem = origem; }
}

/// <summary>Moldura do Model reconhecida como folha.</summary>
public sealed class MolduraReconhecida
{
    public CaixaPdf Caixa { get; set; }
    public string Origem { get; set; } = "";
    public FormatoReconhecido Reconhecido { get; set; }
}

/// <summary>O que o leitor achou num layout (para a regra layout × Model).</summary>
public sealed class LayoutLido
{
    public string Nome { get; set; } = "";
    /// <summary>Sem nada visível além das viewports.</summary>
    public bool Vazio { get; set; }
    /// <summary>Moldura/carimbo com formato da lista.</summary>
    public bool Reconhecido { get; set; }
}

/// <summary>Decisão de quais folhas um DWG dá.</summary>
public sealed class PlanoFolhasDesenho
{
    /// <summary>Plotar as molduras do Model (uma folha por moldura).</summary>
    public bool UsarModelo { get; set; }
    /// <summary>Layouts que entram marcados na lista.</summary>
    public HashSet<string> LayoutsMarcados { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    /// <summary>Nenhuma moldura/carimbo em lugar nenhum e todos os layouts vazios (ou nenhum layout): vira uma linha de erro.</summary>
    public bool SemMoldura { get; set; }
}

/// <summary>
/// Folhas desenhadas no Model (pedido do usuário de 17/11/2026: DWG sem layout ou com layouts vazios, recebido de outro escritório).
/// Regra (<see cref="Decidir"/>): algum layout com moldura reconhecida → só os layouts (o Model não é lido); nenhum → as molduras
/// do Model, uma folha por moldura; nada em lugar nenhum → erro "nenhuma moldura/carimbo encontrado no Model nem nos layouts".
/// No Model a moldura está desenhada em escala: o formato é reconhecido tentando unidade (mm, cm, m) × escala usual.
/// </summary>
public static class FolhasModeloPdf
{
    /// <summary>Escalas usuais (1:n) tentadas no Model.</summary>
    public static readonly double[] Escalas = { 1, 2, 2.5, 5, 7.5, 10, 12.5, 15, 20, 25, 30, 40, 50, 60, 75, 100, 125, 150, 200, 250, 300, 400, 500, 750, 1000 };
    /// <summary>mm por unidade do desenho: mm, cm, m.</summary>
    public static readonly double[] Unidades = { 1, 10, 1000 };
    /// <summary>Moldura menor que esta fração da maior moldura achada é descartada (seção de pilar, detalhe…).</summary>
    public const double FracaoMinima = 0.2;

    /// <summary>Precisa ler o Model? Só quando nenhum layout tem moldura reconhecida.</summary>
    public static bool PrecisaModelo(IEnumerable<LayoutLido> layouts) => !(layouts ?? Enumerable.Empty<LayoutLido>()).Any(l => l.Reconhecido);

    public static PlanoFolhasDesenho Decidir(IEnumerable<LayoutLido> layouts, IList<MolduraReconhecida> modelo)
    {
        var lista = (layouts ?? Enumerable.Empty<LayoutLido>()).ToList();
        var p = new PlanoFolhasDesenho();
        if (lista.Any(l => l.Reconhecido))
        {
            foreach (var l in lista.Where(l => !l.Vazio)) p.LayoutsMarcados.Add(l.Nome);
            return p;
        }
        if (modelo != null && modelo.Count > 0) { p.UsarModelo = true; return p; }
        foreach (var l in lista.Where(l => !l.Vazio)) p.LayoutsMarcados.Add(l.Nome); // conteúdo sem moldura: "?" (o usuário escolhe)
        p.SemMoldura = p.LayoutsMarcados.Count == 0;
        return p;
    }

    /// <summary>
    /// Formato de uma moldura do Model <paramref name="largura"/> × <paramref name="altura"/> (unidades do desenho): o par
    /// unidade × escala que dá a menor diferença (mm de papel) dentro da tolerância. <see cref="FormatoReconhecido.Fator"/> = mm
    /// de papel por unidade do desenho.
    /// </summary>
    public static FormatoReconhecido Reconhecer(double largura, double altura, IEnumerable<FormatoFolha> formatos, double tolerancia = 10)
    {
        var lista = (formatos ?? FormatosFolha.Padrao()).Where(f => f != null && f.Maior > 0).ToList();
        double maior = Math.Max(Math.Abs(largura), Math.Abs(altura)), menor = Math.Min(Math.Abs(largura), Math.Abs(altura));
        if (maior <= 0 || lista.Count == 0) return new FormatoReconhecido();
        FormatoReconhecido melhor = null;
        foreach (var u in Unidades)
            foreach (var s in Escalas)
            {
                double fator = u / s;
                foreach (var f in lista)
                {
                    double dif = Math.Max(Math.Abs(maior * fator - f.Maior), Math.Abs(menor * fator - f.Menor));
                    if (dif > tolerancia) continue;
                    // compara em fração do formato (a tolerância em mm pesa mais nas folhas pequenas)
                    if (melhor == null || dif / f.Maior < melhor.Diferenca / melhor.Formato.Maior - 1e-12)
                        melhor = new FormatoReconhecido { Formato = f, Diferenca = dif, Fator = fator };
                }
            }
        return melhor ?? new FormatoReconhecido();
    }

    /// <summary>
    /// Molduras do Model entre os candidatos: as que têm formato reconhecido, sem as que estão dentro de outra reconhecida (a
    /// polilinha da moldura dentro do bloco do carimbo, por exemplo) e sem as pequenas demais (&lt; <see cref="FracaoMinima"/> da maior).
    /// Ordem de leitura: de cima para baixo, da esquerda para a direita.
    /// </summary>
    public static List<MolduraReconhecida> Selecionar(IEnumerable<CandidatoMoldura> candidatos, IEnumerable<FormatoFolha> formatos, double tolerancia = 10)
    {
        var fs = (formatos ?? FormatosFolha.Padrao()).ToList();
        var rec = (candidatos ?? Enumerable.Empty<CandidatoMoldura>())
            .Where(c => c != null && c.Caixa.Largura > 0 && c.Caixa.Altura > 0)
            .Select(c => new MolduraReconhecida { Caixa = c.Caixa, Origem = c.Origem, Reconhecido = Reconhecer(c.Caixa.Largura, c.Caixa.Altura, fs, tolerancia) })
            .Where(m => m.Reconhecido.Formato != null)
            .OrderByDescending(m => m.Caixa.Area)
            .ToList();
        var fora = new List<MolduraReconhecida>();
        foreach (var m in rec)
            if (!fora.Any(o => o.Caixa.Contem(m.Caixa))) fora.Add(m); // a maior fica (em empate, o 1º candidato: o bloco do carimbo)
        if (fora.Count == 0) return fora;
        double maior = fora.Max(m => m.Caixa.Maior);
        fora = fora.Where(m => m.Caixa.Maior >= FracaoMinima * maior).ToList();
        return Ordenar(fora);
    }

    /// <summary>Ordem de leitura: faixas horizontais (de cima para baixo), cada uma da esquerda para a direita.</summary>
    public static List<MolduraReconhecida> Ordenar(IEnumerable<MolduraReconhecida> molduras)
    {
        var res = new List<MolduraReconhecida>();
        var resto = molduras.OrderByDescending(m => m.Caixa.Y1).ToList();
        while (resto.Count > 0)
        {
            var topo = resto[0];
            double yc = (topo.Caixa.Y0 + topo.Caixa.Y1) / 2;
            var faixa = resto.Where(m => m.Caixa.Y0 <= yc && m.Caixa.Y1 >= yc).OrderBy(m => m.Caixa.X0).ToList();
            res.AddRange(faixa);
            resto = resto.Except(faixa).ToList();
        }
        return res;
    }

    /// <summary>Rótulo da folha do Model (vai para a lista e, com 2+ folhas do mesmo DWG, para o nome do PDF).</summary>
    public static string Rotulo(int indice, int total) => total > 1 ? $"Model {indice}" : "Model";
}
