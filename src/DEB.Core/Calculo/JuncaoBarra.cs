using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Modelo;

namespace DEB.Core.Calculo;

/// <summary>Vértice de polilinha (coordenadas do desenho) com o bulge do trecho que sai dele.</summary>
public readonly struct VerticePoli
{
    public double X { get; }
    public double Y { get; }
    public double Bulge { get; }
    public VerticePoli(double x, double y, double bulge = 0) { X = x; Y = y; Bulge = bulge; }
    public double Distancia(VerticePoli o) => Math.Sqrt((X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y));
}

/// <summary>Onde a barra antiga ficou dentro da polilinha juntada.</summary>
public sealed class PosicaoNaJuncao
{
    /// <summary>Índice, na polilinha juntada (no sentido dela), do vértice que corresponde ao 1º vértice da barra.</summary>
    public int Indice { get; set; }
    /// <summary>A polilinha juntada percorre a barra ao contrário.</summary>
    public bool Invertida { get; set; }
}

/// <summary>Cadeia de linhas soltas ligadas ponta com ponta (ARMCONVERTER com LINEs).</summary>
public sealed class CadeiaLinhas
{
    /// <summary>Vértices na ordem do encadeamento (fechada: sem repetir o 1º no fim).</summary>
    public List<(double X, double Y)> Pontos { get; } = new List<(double X, double Y)>();
    /// <summary>Índices das linhas de entrada que fazem parte da cadeia, na ordem.</summary>
    public List<int> Linhas { get; } = new List<int>();
    public bool Fechada { get; set; }
    /// <summary>3 ou mais linhas no mesmo ponto: não dá para saber o caminho — o grupo não é convertido.</summary>
    public bool Ramificada { get; set; }
}

/// <summary>
/// JOIN / PEDIT→Juntar de armaduras (pedido do usuário, 2026-11-12): localizar a barra dentro da polilinha juntada,
/// pôr a geometria no sentido da barra e realocar os dados por trecho (gancho, cota, VAR…) para os índices novos.
/// Também encadeia linhas soltas (ARMCONVERTER). Sem tipos do CAD.
/// </summary>
public static class JuncaoBarra
{
    /// <summary>
    /// Procura a sequência de vértices da barra dentro da polilinha juntada, no sentido direto ou inverso.
    /// Null se não estiver lá (a barra não fez parte da junção).
    /// </summary>
    public static PosicaoNaJuncao Localizar(IReadOnlyList<VerticePoli> barra, IReadOnlyList<VerticePoli> juntada, double tol)
    {
        int n = barra.Count, m = juntada.Count;
        if (n < 2 || m < n) return null;
        for (int i = 0; i + n <= m; i++)
        {
            bool direta = true, inversa = true;
            for (int k = 0; k < n && (direta || inversa); k++)
            {
                if (direta && juntada[i + k].Distancia(barra[k]) > tol) direta = false;
                if (inversa && juntada[i + k].Distancia(barra[n - 1 - k]) > tol) inversa = false;
            }
            // os arcos também têm de ser os mesmos (no inverso, o bulge troca de sinal)
            if (direta && Bulges(barra, juntada, i, false, tol)) return new PosicaoNaJuncao { Indice = i };
            if (inversa && Bulges(barra, juntada, i, true, tol)) return new PosicaoNaJuncao { Indice = i + n - 1, Invertida = true };
        }
        return null;
    }

    private static bool Bulges(IReadOnlyList<VerticePoli> barra, IReadOnlyList<VerticePoli> juntada, int i, bool inversa, double tol)
    {
        int n = barra.Count;
        for (int k = 0; k < n - 1; k++)
        {
            double b = inversa ? -barra[n - 2 - k].Bulge : barra[k].Bulge;
            if (Math.Abs(juntada[i + k].Bulge - b) > Math.Max(1e-6, tol)) return false;
        }
        return true;
    }

    /// <summary>Mesma polilinha percorrida ao contrário (os bulges trocam de trecho e de sinal; o último fica 0).</summary>
    public static List<VerticePoli> Inverter(IReadOnlyList<VerticePoli> v)
    {
        int m = v.Count;
        var r = new List<VerticePoli>(m);
        for (int i = 0; i < m; i++)
        {
            var p = v[m - 1 - i];
            double bulge = i < m - 1 ? -v[m - 2 - i].Bulge : 0;
            r.Add(new VerticePoli(p.X, p.Y, bulge));
        }
        return r;
    }

    /// <summary>
    /// Geometria juntada no sentido da barra e quantos trechos entraram antes e depois dela.
    /// </summary>
    public static (List<VerticePoli> vertices, int antes, int depois) NoSentidoDaBarra(
        IReadOnlyList<VerticePoli> juntada, PosicaoNaJuncao pos, int verticesDaBarra)
    {
        var v = pos.Invertida ? Inverter(juntada) : juntada.ToList();
        int inicio = pos.Invertida ? juntada.Count - 1 - pos.Indice : pos.Indice;
        int antes = inicio;
        int depois = (v.Count - 1) - (inicio + verticesDaBarra - 1);
        return (v, antes, depois);
    }

    /// <summary>
    /// Dados por trecho depois da junção: os antigos vão para os índices novos (com a cota, o VAR, a cota movida…);
    /// os trechos que entraram são retos, cotados e sem gancho. O gancho em que a linha encostou vira trecho reto
    /// (como o gancho esticado).
    /// </summary>
    public static List<Segmento> Realocar(IReadOnlyList<Segmento> antigos, int antes, int depois)
    {
        var r = new List<Segmento>();
        for (int i = 0; i < antes; i++) r.Add(new Segmento());
        for (int k = 0; k < antigos.Count; k++)
        {
            var s = antigos[k].Clonar();
            bool ponta = (k == 0 && antes > 0) || (k == antigos.Count - 1 && depois > 0);
            if (ponta && Segmento.EhGancho(s.Formato)) s.Formato = FormatoSegmento.Linha;
            r.Add(s);
        }
        for (int i = 0; i < depois; i++) r.Add(new Segmento());
        return r;
    }

    /// <summary>
    /// Trechos que entraram numa barra editada no lugar (PEDIT→Juntar na própria armadura): procura onde os comprimentos
    /// registrados na última atualização aparecem, em sequência, na geometria nova. Prefere o início (0 = tudo no fim).
    /// Null se não achar (vértice inserido no meio, comprimentos mudados) ou se não houver registro.
    /// </summary>
    public static int? TrechosAntes(IReadOnlyList<double> registrados, IReadOnlyList<double> novos, double tol)
    {
        int n = registrados.Count, m = novos.Count;
        if (n == 0 || m <= n || registrados.Any(c => c <= 0)) return null;
        for (int a = 0; a + n <= m; a++)
        {
            bool ok = true;
            for (int k = 0; k < n && ok; k++) ok = Math.Abs(novos[a + k] - registrados[k]) <= tol;
            if (ok) return a;
        }
        return null;
    }

    /// <summary>
    /// Encadeia linhas soltas que se encostam ponta com ponta (tolerância tol). Cada grupo ligado vira uma cadeia:
    /// aberta (começa numa ponta livre), fechada (todas as ligações com 2 linhas) ou ramificada (3+ linhas num ponto).
    /// Linhas de comprimento ≤ tol são ignoradas. As cadeias saem na ordem da 1ª linha de cada uma.
    /// </summary>
    public static List<CadeiaLinhas> Encadear(IReadOnlyList<((double X, double Y) A, (double X, double Y) B)> linhas, double tol)
    {
        var nos = new List<(double X, double Y)>();
        int No((double X, double Y) p)
        {
            for (int i = 0; i < nos.Count; i++)
                if (Math.Abs(nos[i].X - p.X) <= tol && Math.Abs(nos[i].Y - p.Y) <= tol
                    && Math.Sqrt((nos[i].X - p.X) * (nos[i].X - p.X) + (nos[i].Y - p.Y) * (nos[i].Y - p.Y)) <= tol) return i;
            nos.Add(p);
            return nos.Count - 1;
        }
        var ponta = new List<(int a, int b)>();
        var validas = new List<int>();
        for (int i = 0; i < linhas.Count; i++)
        {
            var (a, b) = linhas[i];
            double len = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
            if (len <= tol) { ponta.Add((-1, -1)); continue; }
            ponta.Add((No(a), No(b)));
            validas.Add(i);
        }
        var incid = new Dictionary<int, List<int>>();
        foreach (var i in validas)
            foreach (var no in new[] { ponta[i].a, ponta[i].b })
            {
                if (!incid.TryGetValue(no, out var l)) incid[no] = l = new List<int>();
                l.Add(i);
            }

        var usada = new HashSet<int>();
        var r = new List<CadeiaLinhas>();
        foreach (var i0 in validas)
        {
            if (usada.Contains(i0)) continue;
            // componente ligado
            var comp = new List<int>();
            var fila = new Queue<int>();
            fila.Enqueue(i0); usada.Add(i0);
            while (fila.Count > 0)
            {
                int i = fila.Dequeue();
                comp.Add(i);
                foreach (var no in new[] { ponta[i].a, ponta[i].b })
                    foreach (var j in incid[no])
                        if (usada.Add(j)) fila.Enqueue(j);
            }
            comp.Sort();
            var cadeia = new CadeiaLinhas();
            var nosComp = comp.SelectMany(i => new[] { ponta[i].a, ponta[i].b }).Distinct().ToList();
            if (nosComp.Any(no => incid[no].Count >= 3))
            {
                cadeia.Ramificada = true;
                cadeia.Linhas.AddRange(comp);
                r.Add(cadeia);
                continue;
            }
            // aberta: começa na ponta livre da linha de menor índice que tem uma; fechada: no início da 1ª linha
            int inicio = -1;
            foreach (var i in comp)
            {
                if (incid[ponta[i].a].Count == 1) { inicio = ponta[i].a; break; }
                if (incid[ponta[i].b].Count == 1) { inicio = ponta[i].b; break; }
            }
            cadeia.Fechada = inicio < 0;
            if (inicio < 0) inicio = ponta[comp[0]].a;
            int atual = inicio, anterior = -1;
            cadeia.Pontos.Add(nos[atual]);
            // fechada: sai pela 1ª linha, no sentido em que foi desenhada
            int proxima = cadeia.Fechada ? comp[0] : incid[atual][0];
            while (proxima >= 0 && !cadeia.Linhas.Contains(proxima))
            {
                cadeia.Linhas.Add(proxima);
                anterior = proxima;
                atual = ponta[proxima].a == atual ? ponta[proxima].b : ponta[proxima].a;
                if (cadeia.Fechada && atual == inicio) break;
                cadeia.Pontos.Add(nos[atual]);
                proxima = incid[atual].FirstOrDefault(j => j != anterior && !cadeia.Linhas.Contains(j), -1);
            }
            r.Add(cadeia);
        }
        return r;
    }

    private static int FirstOrDefault(this IEnumerable<int> l, Func<int, bool> pred, int padrao)
    {
        foreach (var x in l) if (pred(x)) return x;
        return padrao;
    }
}
