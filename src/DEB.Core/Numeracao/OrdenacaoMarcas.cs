using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Numeracao;

/// <summary>Ordem em que as peças recebem as marcas (índices da lista de entrada) e quantas linhas foram formadas.</summary>
public sealed class ResultadoOrdenacao
{
    public List<int> Ordem { get; } = new List<int>();
    public int Linhas { get; set; }
    /// <summary>Vigas e paredes: quantas peças em cada direção.</summary>
    public int Horizontais { get; set; }
    public int Verticais { get; set; }
    public int Inclinadas { get; set; }
    public int LinhasHorizontais { get; set; }
    public int LinhasVerticais { get; set; }
}

/// <summary>
/// Ordem de numeração das marcas em planta, como nos scripts do escritório (Revit_Suelio, 2025-2026):
/// sempre de cima para baixo e da esquerda para a direita. Coordenadas em qualquer unidade (a mesma da tolerância).
/// Sem dependência de Revit/CAD: serve a todos os modeladores.
/// </summary>
public static class OrdenacaoMarcas
{
    /// <summary>
    /// Pilares, fundações e peças: referência do croqui = canto superior esquerdo (maior Y, menor X).
    /// Linhas por proximidade: a peça mais alta ainda livre abre uma linha e puxa as que estão a até
    /// <paramref name="tolerancia"/> dela em Y; linhas de cima para baixo (média de Y), cada uma da esquerda para a direita.
    /// </summary>
    public static ResultadoOrdenacao PorLinhas(IReadOnlyList<(double X, double Y)> referencias, double tolerancia)
    {
        var r = new ResultadoOrdenacao();
        var livres = Enumerable.Range(0, referencias.Count).OrderByDescending(i => referencias[i].Y).ToList();
        var usados = new HashSet<int>();
        var linhas = new List<List<int>>();
        foreach (int i in livres)
        {
            if (!usados.Add(i)) continue;
            var linha = new List<int> { i };
            foreach (int j in livres)
                if (!usados.Contains(j) && Math.Abs(referencias[i].Y - referencias[j].Y) <= tolerancia)
                {
                    linha.Add(j);
                    usados.Add(j);
                }
            linhas.Add(linha);
        }
        foreach (var linha in linhas.OrderByDescending(l => l.Average(i => referencias[i].Y)))
            r.Ordem.AddRange(linha.OrderBy(i => referencias[i].X));
        r.Linhas = linhas.Count;
        return r;
    }

    /// <summary>
    /// Vigas, pelo eixo (ponto médio): horizontais em linhas de cima para baixo (esquerda → direita), depois
    /// verticais em colunas da esquerda para a direita (baixo → cima), depois inclinadas (|dz| &gt; 0,01 na direção
    /// unitária) como as horizontais. Uma linha nova começa quando o salto para a peça anterior passa da tolerância.
    /// </summary>
    public static ResultadoOrdenacao Vigas(IReadOnlyList<((double X, double Y, double Z) A, (double X, double Y, double Z) B)> eixos,
        double tolerancia)
    {
        var info = eixos.Select((e, i) =>
        {
            double dx = e.B.X - e.A.X, dy = e.B.Y - e.A.Y, dz = e.B.Z - e.A.Z, l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            int grupo = l > 0 && Math.Abs(dz / l) > 0.01 ? 3 : Math.Abs(dx) > Math.Abs(dy) ? 1 : 2;
            return (I: i, X: (e.A.X + e.B.X) / 2, Y: (e.A.Y + e.B.Y) / 2, G: grupo);
        }).ToList();

        var r = new ResultadoOrdenacao();
        List<List<(int I, double X, double Y, int G)>> Linhas(int g, Func<(int I, double X, double Y, int G), double> chave) =>
            AgruparPorSalto(info.Where(v => v.G == g).ToList(), chave, tolerancia);

        var h = Linhas(1, v => v.Y);
        var v = Linhas(2, v => v.X);
        var inc = Linhas(3, v => v.Y);
        r.Ordem.AddRange(h.OrderByDescending(l => l.Average(x => x.Y)).SelectMany(l => l.OrderBy(x => x.X)).Select(x => x.I));
        r.Ordem.AddRange(v.OrderBy(l => l.Average(x => x.X)).SelectMany(l => l.OrderBy(x => x.Y)).Select(x => x.I));
        r.Ordem.AddRange(inc.OrderByDescending(l => l.Average(x => x.Y)).SelectMany(l => l.OrderBy(x => x.X)).Select(x => x.I));
        r.Horizontais = info.Count(x => x.G == 1);
        r.Verticais = info.Count(x => x.G == 2);
        r.Inclinadas = info.Count(x => x.G == 3);
        r.LinhasHorizontais = h.Count;
        r.LinhasVerticais = v.Count;
        r.Linhas = h.Count + v.Count + inc.Count;
        return r;
    }

    /// <summary>
    /// Lajes, pela caixa do contorno da face superior: uma laje entra na linha com que mais se sobrepõe em Y, desde que
    /// a sobreposição cubra pelo menos <paramref name="sobreposicaoMinima"/> da altura dela; linhas de cima para baixo
    /// (topo), cada uma da esquerda para a direita (XMin).
    /// </summary>
    public static ResultadoOrdenacao Lajes(IReadOnlyList<(double XMin, double XMax, double YMin, double YMax)> caixas,
        double sobreposicaoMinima = 0.5)
    {
        var linhas = new List<(List<int> Itens, double YMin, double YMax)>();
        foreach (int i in Enumerable.Range(0, caixas.Count).OrderByDescending(i => caixas[i].YMax))
        {
            var c = caixas[i];
            double altura = c.YMax - c.YMin;
            int melhor = -1; double melhorFracao = 0;
            for (int k = 0; k < linhas.Count; k++)
            {
                double s = Math.Min(linhas[k].YMax, c.YMax) - Math.Max(linhas[k].YMin, c.YMin);
                if (s <= 0) continue;
                double f = altura > 0 ? s / altura : 1.0;
                if (f >= sobreposicaoMinima && f > melhorFracao) { melhor = k; melhorFracao = f; }
            }
            if (melhor < 0) { linhas.Add((new List<int>(), double.MaxValue, double.MinValue)); melhor = linhas.Count - 1; }
            var l = linhas[melhor];
            l.Itens.Add(i);
            linhas[melhor] = (l.Itens, Math.Min(l.YMin, c.YMin), Math.Max(l.YMax, c.YMax));
        }
        var r = new ResultadoOrdenacao { Linhas = linhas.Count };
        foreach (var l in linhas.OrderByDescending(l => l.YMax))
            r.Ordem.AddRange(l.Itens.OrderBy(i => caixas[i].XMin));
        return r;
    }

    /// <summary>
    /// Paredes, pela linha de locação: horizontais (dy ≈ 0), depois verticais (dx ≈ 0), depois inclinadas (em planta ou
    /// com desnível); dentro de cada grupo, faixas de Y arredondadas pela tolerância (cima → baixo) e X do ponto médio.
    /// </summary>
    public static ResultadoOrdenacao Paredes(IReadOnlyList<((double X, double Y, double Z) A, (double X, double Y, double Z) B)> eixos,
        double tolerancia, double tolDirecao = 0.01)
    {
        var info = eixos.Select((e, i) =>
        {
            double dx = Math.Abs(e.B.X - e.A.X), dy = Math.Abs(e.B.Y - e.A.Y), dz = Math.Abs(e.B.Z - e.A.Z);
            int g = dz > tolDirecao ? 3 : dy < tolDirecao ? 1 : dx < tolDirecao ? 2 : 3;
            double y = (e.A.Y + e.B.Y) / 2;
            return (I: i, X: (e.A.X + e.B.X) / 2, Faixa: Math.Round(y / tolerancia), G: g);
        }).ToList();
        var r = new ResultadoOrdenacao
        {
            Horizontais = info.Count(x => x.G == 1),
            Verticais = info.Count(x => x.G == 2),
            Inclinadas = info.Count(x => x.G == 3)
        };
        r.Ordem.AddRange(info.OrderBy(x => x.G).ThenByDescending(x => x.Faixa).ThenBy(x => x.X).Select(x => x.I));
        return r;
    }

    /// <summary>Ordena pela chave e abre um grupo novo quando o salto para o anterior passa da tolerância.</summary>
    private static List<List<T>> AgruparPorSalto<T>(List<T> itens, Func<T, double> chave, double tolerancia)
    {
        var grupos = new List<List<T>>();
        if (itens.Count == 0) return grupos;
        var ordenados = itens.OrderBy(chave).ToList();
        var atual = new List<T> { ordenados[0] };
        double ultimo = chave(ordenados[0]);
        for (int i = 1; i < ordenados.Count; i++)
        {
            double v = chave(ordenados[i]);
            if (v - ultimo <= tolerancia) atual.Add(ordenados[i]);
            else { grupos.Add(atual); atual = new List<T> { ordenados[i] }; }
            ultimo = v;
        }
        grupos.Add(atual);
        return grupos;
    }
}
