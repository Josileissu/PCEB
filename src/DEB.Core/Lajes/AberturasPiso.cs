using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Lajes;

// =====================================================================================================================
// Aberturas do modelo que furam o piso (2026-11-17): "abertura por face"/"abertura vertical" hospedadas no piso e shafts que
// atravessam a espessura dele. O adaptador lê os contornos (planta, metros); aqui ficam as regras puras:
// (a) encadear trechos soltos em laços fechados; (b) classificar cada laço em relação ao contorno do piso (dentro → furo,
// fora → ignora, na borda → aviso), sem duplicar furo que já está no croqui e avisando sobreposições.
// =====================================================================================================================

/// <summary>Um laço de abertura lido do modelo (planta, metros).</summary>
public sealed class AberturaPlanta
{
    /// <summary>Id do elemento no modelo (para os avisos).</summary>
    public string Id = "";
    /// <summary>true = shaft; false = abertura por face / vertical hospedada no piso.</summary>
    public bool Shaft;
    public List<(double X, double Y)> Pontos = new List<(double, double)>();
}

/// <summary>Posição de um laço de abertura em relação ao contorno do piso.</summary>
public enum PosicaoAbertura { Dentro, Fora, Borda }

/// <summary>Resultado de <see cref="AberturasPiso.Incluir"/>.</summary>
public sealed class ResultadoAberturas
{
    /// <summary>Laços do piso com os furos novos acrescentados (1º = contorno externo).</summary>
    public List<List<(double X, double Y)>> Loops = new List<List<(double, double)>>();
    /// <summary>Elementos (ids distintos) que viraram furo.</summary>
    public int AberturasFace, Shafts;
    public List<string> Notas = new List<string>();
}

public static class AberturasPiso
{
    /// <summary>Tolerância (m) para considerar um vértice sobre a borda do contorno.</summary>
    public const double TolBorda = 0.001;
    /// <summary>Tolerância (m) para considerar dois furos iguais (mesmo polígono).</summary>
    public const double TolDuplicata = 0.01;

    // ------------------------------------------------------------------ (a) laços a partir de trechos

    /// <summary>
    /// Junta polilinhas soltas (cada uma = pontos em ordem, ex.: curva tesselada) em laços fechados, encadeando pelas pontas
    /// (tolerância <paramref name="tol"/>, m; qualquer sentido). Cada laço sai sem o ponto final repetido. Trechos que não fecham
    /// um laço são descartados e contados em <paramref name="soltos"/>.
    /// </summary>
    public static List<List<(double X, double Y)>> Lacos(IEnumerable<IReadOnlyList<(double X, double Y)>> trechos, out int soltos, double tol = 0.002)
    {
        var resto = trechos.Where(t => t != null && t.Count >= 2).Select(t => t.ToList()).ToList();
        var r = new List<List<(double X, double Y)>>();
        soltos = 0;
        while (resto.Count > 0)
        {
            var laco = new List<(double X, double Y)>(resto[0]);
            resto.RemoveAt(0);
            while (!Perto(laco[0], laco[laco.Count - 1], tol) || laco.Count < 3)
            {
                var fim = laco[laco.Count - 1];
                int k = resto.FindIndex(t => Perto(t[0], fim, tol) || Perto(t[t.Count - 1], fim, tol));
                if (k < 0) break;
                var t = resto[k]; resto.RemoveAt(k);
                if (!Perto(t[0], fim, tol)) t.Reverse();
                laco.AddRange(t.Skip(1));
            }
            if (laco.Count >= 4 && Perto(laco[0], laco[laco.Count - 1], tol))
            {
                laco.RemoveAt(laco.Count - 1);
                r.Add(laco);
            }
            else soltos++;
        }
        return r;
    }

    private static bool Perto((double X, double Y) a, (double X, double Y) b, double tol)
        => Math.Abs(a.X - b.X) <= tol && Math.Abs(a.Y - b.Y) <= tol;

    // ------------------------------------------------------------------ (b) classificação

    /// <summary>
    /// Dentro: todos os vértices dentro do contorno, longe da borda, e nenhuma aresta cruza o contorno.
    /// Fora: todos fora, nenhuma aresta cruza e o contorno não está dentro da abertura. O resto (cruza, encosta ou cobre o piso) = Borda.
    /// </summary>
    public static PosicaoAbertura Classificar(IReadOnlyList<(double X, double Y)> abertura, IReadOnlyList<(double X, double Y)> contorno, double tolBorda = TolBorda)
    {
        if (ArestasCruzam(abertura, contorno)) return PosicaoAbertura.Borda;
        bool algumDentro = false, algumFora = false;
        foreach (var p in abertura)
        {
            if (DistanciaBorda(p, contorno) <= tolBorda) return PosicaoAbertura.Borda;
            if (Dentro(p, contorno)) algumDentro = true; else algumFora = true;
        }
        if (algumDentro && algumFora) return PosicaoAbertura.Borda;
        if (algumDentro) return PosicaoAbertura.Dentro;
        // todos fora: o piso pode estar inteiro dentro da abertura
        return contorno.Any(q => Dentro(q, abertura)) ? PosicaoAbertura.Borda : PosicaoAbertura.Fora;
    }

    /// <summary>Mesmo polígono, a menos de <paramref name="tol"/>: cada vértice de um fica sobre a borda do outro (independe da tesselação).</summary>
    public static bool MesmoPoligono(IReadOnlyList<(double X, double Y)> a, IReadOnlyList<(double X, double Y)> b, double tol = TolDuplicata)
        => a.All(p => DistanciaBorda(p, b) <= tol) && b.All(p => DistanciaBorda(p, a) <= tol);

    /// <summary>As áreas dos dois polígonos se sobrepõem (arestas que se cruzam ou um dentro do outro). Encostar não conta.</summary>
    public static bool Sobrepoem(IReadOnlyList<(double X, double Y)> a, IReadOnlyList<(double X, double Y)> b)
    {
        if (ArestasCruzam(a, b)) return true;
        return a.Any(p => DistanciaBorda(p, b) > TolBorda && Dentro(p, b)) || b.Any(p => DistanciaBorda(p, a) > TolBorda && Dentro(p, a));
    }

    /// <summary>Todo o polígono <paramref name="a"/> está dentro de <paramref name="b"/> (vértices dentro ou na borda, sem cruzar).</summary>
    public static bool ContidoEm(IReadOnlyList<(double X, double Y)> a, IReadOnlyList<(double X, double Y)> b)
        => !ArestasCruzam(a, b) && a.All(p => DistanciaBorda(p, b) <= TolBorda || Dentro(p, b));

    /// <summary>
    /// Acrescenta as aberturas como furos aos laços do piso (<paramref name="loops"/>: 1º = contorno, demais = furos do croqui).
    /// Dentro → furo (a menos que repita um furo do croqui ou de outra abertura, ou caia inteiro dentro de um furo); fora → ignora;
    /// borda → não entra, aviso para ajustar o contorno; sobreposição com outro furo → entra, com aviso.
    /// </summary>
    public static ResultadoAberturas Incluir(IReadOnlyList<List<(double X, double Y)>> loops, IEnumerable<AberturaPlanta> aberturas)
    {
        var r = new ResultadoAberturas { Loops = loops.Select(l => l.ToList()).ToList() };
        if (r.Loops.Count == 0) return r;
        var contorno = r.Loops[0];
        int nCroqui = r.Loops.Count;
        var origem = new List<string>();                         // id da abertura de cada furo novo (índice nCroqui + k)
        var avisadosBorda = new HashSet<string>();
        var incluidas = new HashSet<string>();
        var shafts = new HashSet<string>();
        foreach (var a in aberturas)
        {
            if (a == null || a.Pontos.Count < 3) continue;
            var pos = Classificar(a.Pontos, contorno);
            if (pos == PosicaoAbertura.Fora) continue;
            if (pos == PosicaoAbertura.Borda)
            {
                if (avisadosBorda.Add(a.Id)) r.Notas.Add($"abertura {a.Id} na borda do piso: ajuste o contorno do piso");
                continue;
            }
            int dup = -1;
            for (int i = 1; i < r.Loops.Count && dup < 0; i++) if (MesmoPoligono(a.Pontos, r.Loops[i])) dup = i;
            if (dup > 0)
            {
                if (dup < nCroqui) r.Notas.Add($"abertura {a.Id}: o furo já está no croqui do piso (não duplicado)");
                continue;
            }
            int dono = -1;
            for (int i = 1; i < r.Loops.Count && dono < 0; i++) if (ContidoEm(a.Pontos, r.Loops[i])) dono = i;
            if (dono > 0)
            {
                r.Notas.Add($"abertura {a.Id}: fica dentro de outro furo ({Nome(dono)}) (ignorada)");
                continue;
            }
            for (int i = 1; i < r.Loops.Count; i++)
                if (Sobrepoem(a.Pontos, r.Loops[i])) r.Notas.Add($"abertura {a.Id} e {Nome(i)} se sobrepõem: confira os furos");
            r.Loops.Add(a.Pontos.ToList());
            origem.Add(a.Id);
            if (incluidas.Add(a.Id) && a.Shaft) shafts.Add(a.Id);
        }
        r.Shafts = shafts.Count;
        r.AberturasFace = incluidas.Count - shafts.Count;
        if (incluidas.Count > 0)
        {
            var partes = new List<string>();
            if (r.AberturasFace > 0) partes.Add(r.AberturasFace == 1 ? "1 abertura por face" : $"{r.AberturasFace} aberturas por face");
            if (r.Shafts > 0) partes.Add(r.Shafts == 1 ? "1 shaft" : $"{r.Shafts} shafts");
            r.Notas.Add(string.Join(" e ", partes) + (incluidas.Count == 1 ? " incluído como furo" : " incluídos como furos"));
        }
        return r;

        string Nome(int i) => i < nCroqui ? "furo do croqui" : $"abertura {origem[i - nCroqui]}";
    }

    // ------------------------------------------------------------------ utilidades

    /// <summary>Par-ímpar (raio em +x).</summary>
    public static bool Dentro((double X, double Y) p, IReadOnlyList<(double X, double Y)> pol)
    {
        bool d = false;
        int n = pol.Count;
        for (int i = 0; i < n; i++)
        {
            var a = pol[i]; var b = pol[(i + 1) % n];
            if ((a.Y > p.Y) != (b.Y > p.Y) && a.X + (p.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y) > p.X) d = !d;
        }
        return d;
    }

    /// <summary>Menor distância do ponto às arestas do polígono.</summary>
    public static double DistanciaBorda((double X, double Y) p, IReadOnlyList<(double X, double Y)> pol)
    {
        double m = double.MaxValue;
        int n = pol.Count;
        for (int i = 0; i < n; i++)
        {
            var a = pol[i]; var b = pol[(i + 1) % n];
            double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            double t = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2));
            double qx = a.X + t * dx - p.X, qy = a.Y + t * dy - p.Y;
            m = Math.Min(m, Math.Sqrt(qx * qx + qy * qy));
        }
        return m;
    }

    /// <summary>Alguma aresta de um cruza propriamente (no interior das duas) alguma aresta do outro.</summary>
    private static bool ArestasCruzam(IReadOnlyList<(double X, double Y)> a, IReadOnlyList<(double X, double Y)> b)
    {
        for (int i = 0; i < a.Count; i++)
            for (int j = 0; j < b.Count; j++)
                if (Cruzam(a[i], a[(i + 1) % a.Count], b[j], b[(j + 1) % b.Count])) return true;
        return false;
    }

    private static bool Cruzam((double X, double Y) p1, (double X, double Y) p2, (double X, double Y) q1, (double X, double Y) q2)
    {
        const double e = 1e-12;
        double d1 = Cr(q1, q2, p1), d2 = Cr(q1, q2, p2), d3 = Cr(p1, p2, q1), d4 = Cr(p1, p2, q2);
        return ((d1 > e && d2 < -e) || (d1 < -e && d2 > e)) && ((d3 > e && d4 < -e) || (d3 < -e && d4 > e));
    }

    private static double Cr((double X, double Y) o, (double X, double Y) a, (double X, double Y) b)
        => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
}
