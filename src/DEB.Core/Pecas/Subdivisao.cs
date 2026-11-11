using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DEB.Core.Pecas;

/// <summary>Função de um trecho da peça (respostas do usuário, 2026-11-09).</summary>
public static class FuncaoTrecho
{
    public const string Dente = "Dente", ApoioLaje = "Apoio de laje", ApoioEscada = "Apoio de escada", ApoioViga = "Apoio de viga",
        SemApoio = "Sem apoio", Consolo = "Consolo", Corrente = "Corrente";
    /// <summary>Funções dos trechos de viga.</summary>
    public static readonly string[] Todas = { Dente, ApoioLaje, ApoioEscada, ApoioViga, SemApoio };
    /// <summary>Funções dos trechos de pilar.</summary>
    public static readonly string[] Pilar = { Corrente, Consolo };
}

/// <summary>Trecho da peça com seção constante, de <see cref="X0"/> a <see cref="X1"/> ao longo do eixo (cm).</summary>
public sealed class TrechoPeca
{
    public double X0 { get; set; }
    public double X1 { get; set; }
    public SecaoPoligonal Secao { get; set; }
    /// <summary>Função sugerida pela forma (o usuário confirma ou troca na janela de conferência).</summary>
    public string FuncaoSugerida { get; set; } = "";
    /// <summary>Função confirmada pelo usuário (vazio = ainda não conferida).</summary>
    public string Funcao { get; set; } = "";
    public double Comprimento => X1 - X0;
}

/// <summary>
/// Subdivisão de uma peça em trechos de seção constante (2026-11-09): estações nos x de todos os vértices, uma seção cortada
/// no meio de cada intervalo (no Revit), trechos vizinhos iguais juntados. Vale para vigas (ao longo do eixo) e pilares (ao
/// longo da altura). Ver docs/04_arquitetura/conferencia_e_subdivisao_pecas.md.
/// </summary>
public static class Subdivisao
{
    /// <summary>
    /// Intervalos entre as estações: os x dos vértices (mais 0 e L), sem repetidos mais próximos que <paramref name="tol"/>.
    /// Cada intervalo devolve o início, o fim e o meio (onde cortar a seção).
    /// </summary>
    public static List<(double X0, double X1, double Meio)> Intervalos(IEnumerable<double> xs, double L, double tol = SecaoPoligonal.Tolerancia)
    {
        var p = xs.Where(x => x > tol && x < L - tol).Concat(new[] { 0.0, L }).OrderBy(x => x).ToList();
        var e = new List<double>();
        foreach (var x in p) if (e.Count == 0 || x - e[e.Count - 1] > tol) e.Add(x);
        if (L - e[e.Count - 1] > 1e-9) e[e.Count - 1] = L;
        return e.Zip(e.Skip(1), (a, b) => (a, b, (a + b) / 2)).ToList();
    }

    /// <summary>
    /// Estações sem as das aberturas (2026-11-10): o x de um vértice que só existe por causa de um furo na alma não muda a seção da
    /// peça — sem isto, cada furo dividia a viga em mais dois trechos. Também as de DENTRO da abertura (2026-11-11): o furo redondo
    /// tem um vértice a cada passo do arco, e cada um virava um trecho falso cortado pelo furo.
    /// </summary>
    public static List<double> SemBordasDeAberturas(IEnumerable<double> xs, IEnumerable<AberturaPeca> aberturas, double tol = SecaoPoligonal.Tolerancia)
    {
        var ab = aberturas.ToList();
        return xs.Where(x => !ab.Any(a => x >= a.X0 - tol && x <= a.X1 + tol)).ToList();
    }

    /// <summary>
    /// Onde cortar o intervalo [x0, x1]: o meio, ou — se o meio cai numa abertura — o ponto livre mais perto dele (o meio do maior
    /// pedaço do intervalo fora das aberturas). Cortar dentro de um furo dava só o pedaço de baixo da peça.
    /// </summary>
    public static double PontoDeCorte(double x0, double x1, IEnumerable<AberturaPeca> aberturas)
    {
        double meio = (x0 + x1) / 2;
        var dentro = aberturas.Where(a => a.X1 > x0 && a.X0 < x1).OrderBy(a => a.X0).ToList();
        if (!dentro.Any(a => meio > a.X0 - 1e-6 && meio < a.X1 + 1e-6)) return meio;
        // pedaços livres do intervalo
        var livres = new List<(double A, double B)>();
        double c = x0;
        foreach (var a in dentro)
        {
            if (a.X0 > c) livres.Add((c, a.X0));
            c = System.Math.Max(c, a.X1);
        }
        if (c < x1) livres.Add((c, x1));
        if (livres.Count == 0) return meio;
        var maior = livres.OrderByDescending(l => l.B - l.A).ThenBy(l => System.Math.Abs((l.A + l.B) / 2 - meio)).First();
        return (maior.A + maior.B) / 2;
    }

    /// <summary>Junta os intervalos vizinhos com a mesma seção e sugere a função de cada trecho.</summary>
    public static List<TrechoPeca> Juntar(IEnumerable<(double X0, double X1, SecaoPoligonal Secao)> cortes, TipoPeca tipo = TipoPeca.Viga)
    {
        var r = new List<TrechoPeca>();
        foreach (var (x0, x1, s) in cortes.OrderBy(c => c.X0))
        {
            var ant = r.LastOrDefault();
            if (ant != null && ant.Secao.MesmaSecao(s)) ant.X1 = x1;
            else r.Add(new TrechoPeca { X0 = x0, X1 = x1, Secao = s });
        }
        if (r.Count == 0) return r;
        if (tipo == TipoPeca.Pilar)
        {
            // pilar: o trecho mais longo é o corrente; os de seção diferente (área maior) têm consolo
            var corrente = r.OrderByDescending(t => t.Comprimento).First().Secao;
            foreach (var t in r)
                t.FuncaoSugerida = t.Secao.MesmaSecao(corrente) ? FuncaoTrecho.Corrente : t.Secao.Area > corrente.Area ? FuncaoTrecho.Consolo : "";
            return r;
        }
        double hMax = r.Max(t => t.Secao.H);
        for (int i = 0; i < r.Count; i++) r[i].FuncaoSugerida = Sugerir(r[i], hMax, i == 0 || i == r.Count - 1);
        return r;
    }

    /// <summary>
    /// Função pela forma: na ponta, mais baixo que a peça = dente; com aba (L, T invertido) = apoio de laje (escada e viga
    /// apoiada o usuário escolhe); retangular cheio = sem apoio; o resto fica para o usuário.
    /// </summary>
    public static string Sugerir(TrechoPeca t, double hMax, bool ponta)
    {
        var (tipo, _) = t.Secao.Classificar();
        if (ponta && tipo == TipoSecao.Retangular && t.Secao.H < hMax - SecaoPoligonal.Tolerancia) return FuncaoTrecho.Dente;
        if (tipo == TipoSecao.L || tipo == TipoSecao.TInvertido) return FuncaoTrecho.ApoioLaje;
        if (tipo == TipoSecao.Retangular) return FuncaoTrecho.SemApoio;
        return "";
    }
}

/// <summary>
/// Nome do tipo × geometria (2026-11-09): o nome do tipo no Revit às vezes está errado; a geometria manda. Lê as dimensões do
/// nome ("26x170", "40 x 40", "20X50 cm") e compara com a seção medida: retangular = b × h; com aba = alma × h.
/// </summary>
public static class NomeTipo
{
    private static readonly Regex Dim = new Regex(@"(\d+(?:[.,]\d+)?)\s*[xX×]\s*(\d+(?:[.,]\d+)?)", RegexOptions.CultureInvariant);

    /// <summary>Último "A x B" do nome (cm), ou null.</summary>
    public static (double A, double B)? Dimensoes(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return null;
        var m = Dim.Matches(nome);
        if (m.Count == 0) return null;
        var g = m[m.Count - 1].Groups;
        double P(string s) => double.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);
        return (P(g[1].Value), P(g[2].Value));
    }

    /// <summary>Divergência entre o nome e a seção medida (texto para a janela), ou null se batem ou se o nome não traz medidas.</summary>
    public static string Conferir(string nome, SecaoPoligonal s, double tol = 1.0)
    {
        var d = Dimensoes(nome);
        if (d == null) return null;
        var (tipo, desc) = s.Classificar();
        double b = tipo == TipoSecao.Retangular ? s.B : s.LarguraAlma, h = s.H;   // com aba ou mísula: o nome dá a alma
        bool Bate(double x, double y) => Math.Abs(x - b) <= tol && Math.Abs(y - h) <= tol;
        if (Bate(d.Value.A, d.Value.B) || Bate(d.Value.B, d.Value.A)) return null;
        string N(double x) => x.ToString("0.#", CultureInfo.InvariantCulture);
        return $"o nome indica {N(d.Value.A)} × {N(d.Value.B)}, a geometria mede {desc}";
    }
}
