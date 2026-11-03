using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using DEB.Core.Modelo;

namespace DEB.Core.Estribos;

/// <summary>Formatos do diálogo de estribo (seção 3.1).</summary>
public enum FormatoEstribo
{
    /// <summary>Fechado, 2 ramos.</summary>
    Simples = 0,
    /// <summary>Aberto: U com 2 ramos, ou meandro com mais ramos.</summary>
    Aberto = 1,
    /// <summary>Fechados sobrepostos: ramos/2 estribos iguais; só um é a armadura (repetições).</summary>
    Multiplo = 2,
    /// <summary>Um fechado na largura total + um "pente" (ou grampo) com os ramos internos.</summary>
    Pente = 3,
    /// <summary>Circular com ganchos para dentro.</summary>
    CircularExterno = 4,
    /// <summary>Circular com ganchos para fora (seção vazada).</summary>
    CircularInterno = 5,
}

/// <summary>Onde ficam os ganchos de um estribo fechado.</summary>
public enum PosicaoGanchos { SuperiorEsquerdo = 0, SuperiorDireito = 1, InferiorEsquerdo = 2, InferiorDireito = 3, ProximoVertice = 4 }

/// <summary>Opções do diálogo (a última escolha fica na configuração para o ARMESTRIBOREP).</summary>
[DataContract]
public sealed class OpcoesEstribo
{
    [DataMember(Order = 1)] public FormatoEstribo Formato { get; set; } = FormatoEstribo.Simples;
    [DataMember(Order = 2)] public FormatoSegmento Gancho { get; set; } = FormatoSegmento.Gancho90;
    [DataMember(Order = 3)] public int Ramos { get; set; } = 2;
    /// <summary>Dimensões fixas (Base/Altura ou Diâmetro) em vez de 2 pontos na inserção.</summary>
    [DataMember(Order = 4)] public bool DimensoesFixas { get; set; }
    [DataMember(Order = 5)] public double Base { get; set; } = 20;
    [DataMember(Order = 6)] public double Altura { get; set; } = 40;
    [DataMember(Order = 7)] public double Diametro { get; set; } = 30;
    /// <summary>Os pontos/dimensões são da FORMA; o estribo desconta o cobrimento.</summary>
    [DataMember(Order = 8)] public bool UsarSecao { get; set; } = true;
    [DataMember(Order = 9)] public double Cobrimento { get; set; } = 2.5;
    [DataMember(Order = 10)] public double Espacamento { get; set; } = 15;
    [DataMember(Order = 11)] public double Bitola { get; set; } = 5.0;
    [DataMember(Order = 12)] public string Aco { get; set; } = "CA-60";

    public bool Circular => Formato == FormatoEstribo.CircularExterno || Formato == FormatoEstribo.CircularInterno;

    /// <summary>Ramos usados pelo formato (Simples e circulares: 2).</summary>
    public int RamosEfetivos => Formato switch
    {
        FormatoEstribo.Simples or FormatoEstribo.CircularExterno or FormatoEstribo.CircularInterno => 2,
        FormatoEstribo.Multiplo => Math.Max(4, Ramos + Ramos % 2), // par, no mínimo 4
        FormatoEstribo.Pente => Math.Max(3, Ramos),
        _ => Math.Max(2, Ramos),
    };

    public OpcoesEstribo Clonar() => (OpcoesEstribo)MemberwiseClone();

    [OnDeserializing]
    private void AoDesserializar(StreamingContext _) => Configuracao.Padroes.Copiar(new OpcoesEstribo(), this);
}

public struct VerticeEstribo
{
    public double X, Y, Bulge;
    public VerticeEstribo(double x, double y, double bulge = 0) { X = x; Y = y; Bulge = bulge; }
}

/// <summary>Uma polilinha do estribo, em coordenadas locais (origem no canto superior esquerdo, y para cima).</summary>
public sealed class FormaEstribo
{
    public string Nome { get; set; } = "";
    public List<VerticeEstribo> Vertices { get; } = new List<VerticeEstribo>();
    public List<FormatoSegmento> Formatos { get; } = new List<FormatoSegmento>();
    /// <summary>É uma armadura (entra na RA) ou só representação (estribos múltiplos sobrepostos).</summary>
    public bool Armadura { get; set; } = true;
    public int Repeticoes { get; set; } = 1;
    /// <summary>Segmentos desenhados sobre outro (passagem dos ganchos pelo canto): não somam nem são cotados.</summary>
    public HashSet<int> Sobrepostos { get; } = new HashSet<int>();
    /// <summary>Segmentos cotados no estribo fechado (uma largura e uma altura); vazio = todos os retos.</summary>
    public HashSet<int> Cotados { get; } = new HashSet<int>();

    /// <summary>Comprimento de cada segmento (cm).</summary>
    public List<double> Comprimentos()
    {
        var r = new List<double>();
        for (int i = 0; i + 1 < Vertices.Count; i++)
        {
            var a = Vertices[i];
            var b = Vertices[i + 1];
            double corda = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            if (Math.Abs(a.Bulge) < 1e-12) { r.Add(corda); continue; }
            double theta = 4 * Math.Atan(Math.Abs(a.Bulge));
            double raio = corda / (2 * Math.Sin(theta / 2));
            r.Add(raio * theta);
        }
        return r;
    }
}

/// <summary>
/// Gera as formas dos estribos. As medidas são EXTERNAS do estribo (o chamador desconta o
/// cobrimento quando os pontos são da forma). Os ganchos são desenhados como traços a 45° para
/// dentro (ou para fora no circular interno); o comprimento deles vem da norma (configuração).
/// No fechado, os ganchos ficam no canto superior direito, cada um passando do canto por um
/// afastamento para não se sobreporem (padrão do escritório: Drawing1 de 2026-10-27).
/// </summary>
public static class GeradorEstribo
{
    private static readonly double R2 = Math.Sqrt(0.5);

    /// <param name="b">largura externa (cm)</param>
    /// <param name="h">altura externa (cm)</param>
    /// <param name="diametro">diâmetro externo (cm), estribos circulares</param>
    /// <param name="gancho">comprimento de cada gancho (cm)</param>
    /// <param name="afastamento">quanto cada gancho do fechado passa do canto (cm): pino + 2φ (2,5 no ø5 CA-60)</param>
    public static List<FormaEstribo> Gerar(OpcoesEstribo o, double b, double h, double diametro, double gancho,
        double afastamento = 2.5)
    {
        var r = new List<FormaEstribo>();
        int ramos = o.RamosEfetivos;
        FormaEstribo Fechado(double x0, double w) => GeradorEstribo.Fechado(x0, w, h, o.Gancho, gancho, afastamento);
        switch (o.Formato)
        {
            case FormatoEstribo.Simples:
                r.Add(Fechado(0, b));
                break;

            case FormatoEstribo.Aberto:
                r.Add(Meandro(Pernas(0, b, ramos), h, o.Gancho, gancho, "aberto"));
                break;

            case FormatoEstribo.Multiplo:
            {
                int k = ramos / 2;
                double s = b / (ramos - 1);
                for (int i = 0; i < k; i++)
                {
                    var f = Fechado(i * s, k * s);
                    f.Nome = $"fechado {i + 1}/{k}";
                    f.Armadura = i == 0;
                    f.Repeticoes = i == 0 ? k : 1;
                    r.Add(f);
                }
                break;
            }

            case FormatoEstribo.Pente:
            {
                r.Add(Fechado(0, b));
                var internas = Pernas(0, b, ramos).Skip(1).Take(ramos - 2).ToList();
                r.Add(internas.Count == 1
                    ? Grampo(internas[0], h, o.Gancho, gancho)
                    : Meandro(internas, h, o.Gancho, gancho, "pente"));
                break;
            }

            case FormatoEstribo.CircularExterno:
            case FormatoEstribo.CircularInterno:
                r.Add(Circular(diametro / 2, o.Formato == FormatoEstribo.CircularExterno, o.Gancho, gancho));
                break;
        }
        return r;
    }

    private static List<double> Pernas(double x0, double largura, int ramos) =>
        Enumerable.Range(0, ramos).Select(j => x0 + j * largura / (ramos - 1)).ToList();

    /// <summary>
    /// Retângulo fechado com os ganchos no canto superior ESQUERDO (decisão do usuário, 2026-10-27: o
    /// mesmo canto das poligonais convertidas; o exemplo do Drawing1, no canto direito, espelhado).
    /// Segmentos: 0 gancho, 1 passagem (sobreposta), 2 cima, 3 direita, 4 baixo, 5 esquerda, 6 passagem, 7 gancho.
    /// </summary>
    private static FormaEstribo Fechado(double x0, double w, double h, FormatoSegmento g, double lg, double afastamento) =>
        FechadoPoligono(new[] { (x0, 0.0), (x0 + w, 0.0), (x0 + w, -h), (x0, -h) }, 0, g, lg, afastamento);

    /// <summary>
    /// Estribo fechado a partir de uma poligonal fechada qualquer, com os ganchos no vértice
    /// <paramref name="canto"/> (C). Percorre a poligonal no sentido horário: o 1º gancho sai do lado
    /// que chega em C, a "afastamento" dele; o estribo dá a volta completa, passa por C, segue
    /// "afastamento" pelo lado que sai de C e sai o 2º gancho. Gancho 90°: perpendicular ao lado, para
    /// dentro; 45°/180°: diagonal para dentro. Segmentos: 0 gancho, 1 passagem (sobreposta),
    /// 2..n+1 lados (a partir do que sai de C), n+2 passagem, n+3 gancho.
    /// </summary>
    /// <param name="poligono">vértices (sem repetir o primeiro no fim), em qualquer sentido</param>
    public static FormaEstribo FechadoPoligono(IReadOnlyList<(double X, double Y)> poligono, int canto,
        FormatoSegmento g, double lg, double afastamento, string nome = "fechado")
    {
        var p = Limpar(poligono);
        int n = p.Count;
        if (n < 3) throw new ArgumentException("A poligonal do estribo precisa de pelo menos 3 vértices.");
        canto = ((canto % n) + n) % n;
        if (AreaComSinal(p) > 0) // anti-horário: inverte (o vértice escolhido continua o mesmo)
        {
            var c = p[canto];
            p.Reverse();
            canto = p.IndexOf(c);
        }
        var C = p[canto];
        var ant = p[(canto - 1 + n) % n];
        var prox = p[(canto + 1) % n];
        var (uIn, lIn) = Unitario(C.X - ant.X, C.Y - ant.Y);
        var (uOut, lOut) = Unitario(prox.X - C.X, prox.Y - C.Y);
        double d = Math.Max(0, Math.Min(afastamento, 0.3 * Math.Min(lIn, lOut)));
        // sentido horário: o interior fica à direita do sentido de percurso
        var nIn = (X: uIn.Y, Y: -uIn.X);
        var nOut = (X: uOut.Y, Y: -uOut.X);
        bool reto = g == FormatoSegmento.Gancho90;
        var dir1 = reto ? nIn : Unitario(nIn.X - uIn.X, nIn.Y - uIn.Y).u;
        var dir2 = reto ? nOut : Unitario(nOut.X + uOut.X, nOut.Y + uOut.Y).u;
        var a1 = (X: C.X - uIn.X * d, Y: C.Y - uIn.Y * d);
        var a2 = (X: C.X + uOut.X * d, Y: C.Y + uOut.Y * d);

        var f = new FormaEstribo { Nome = nome };
        f.Vertices.Add(new VerticeEstribo(a1.X + dir1.X * lg, a1.Y + dir1.Y * lg));
        f.Vertices.Add(new VerticeEstribo(a1.X, a1.Y));
        for (int k = 0; k < n; k++)
        {
            var v = p[(canto + k) % n];
            f.Vertices.Add(new VerticeEstribo(v.X, v.Y));
        }
        f.Vertices.Add(new VerticeEstribo(C.X, C.Y));
        f.Vertices.Add(new VerticeEstribo(a2.X, a2.Y));
        f.Vertices.Add(new VerticeEstribo(a2.X + dir2.X * lg, a2.Y + dir2.Y * lg));

        f.Formatos.Add(g);
        for (int k = 0; k < n + 2; k++) f.Formatos.Add(FormatoSegmento.Linha);
        f.Formatos.Add(g);
        f.Sobrepostos.Add(1);
        f.Sobrepostos.Add(n + 2);
        if (Retangulo(p))
        {
            f.Cotados.Add(2);     // lado que sai do canto
            f.Cotados.Add(n + 1); // lado que chega no canto
        }
        else for (int k = 2; k <= n + 1; k++) f.Cotados.Add(k);
        return f;
    }

    /// <summary>Vértice "superior mais à esquerda" (maior Y; empate: menor X), onde ficam os ganchos por padrão.</summary>
    public static int CantoPadrao(IReadOnlyList<(double X, double Y)> poligono) =>
        Canto(poligono, PosicaoGanchos.SuperiorEsquerdo);

    /// <summary>Índice do vértice para os ganchos: o mais alto/baixo e, no empate, o mais à esquerda/direita.</summary>
    /// <param name="atual">vértice atual (para <see cref="PosicaoGanchos.ProximoVertice"/>)</param>
    public static int Canto(IReadOnlyList<(double X, double Y)> poligono, PosicaoGanchos pos, int atual = 0)
    {
        int n = poligono.Count;
        if (pos == PosicaoGanchos.ProximoVertice) return (atual + 1) % n;
        bool cima = pos == PosicaoGanchos.SuperiorEsquerdo || pos == PosicaoGanchos.SuperiorDireito;
        bool esquerda = pos == PosicaoGanchos.SuperiorEsquerdo || pos == PosicaoGanchos.InferiorEsquerdo;
        double tol = 1e-6 * Math.Max(1, poligono.Max(v => Math.Max(Math.Abs(v.X), Math.Abs(v.Y))));
        int melhor = 0;
        for (int i = 1; i < n; i++)
        {
            var v = poligono[i];
            var m = poligono[melhor];
            double dy = cima ? v.Y - m.Y : m.Y - v.Y;
            double dx = esquerda ? m.X - v.X : v.X - m.X;
            if (dy > tol || (Math.Abs(dy) <= tol && dx > tol)) melhor = i;
        }
        return melhor;
    }

    /// <summary>Em que posição o vértice <paramref name="indice"/> está (null = nenhum dos 4 cantos).</summary>
    public static PosicaoGanchos? PosicaoDoCanto(IReadOnlyList<(double X, double Y)> poligono, int indice)
    {
        foreach (var p in new[] { PosicaoGanchos.SuperiorEsquerdo, PosicaoGanchos.SuperiorDireito, PosicaoGanchos.InferiorEsquerdo, PosicaoGanchos.InferiorDireito })
            if (Canto(poligono, p) == indice) return p;
        return null;
    }

    /// <summary>
    /// Volta de um estribo fechado gerado por <see cref="FechadoPoligono"/> (vértices da polilinha):
    /// a poligonal começa no canto dos ganchos. null se a forma não for desse tipo.
    /// </summary>
    public static List<(double X, double Y)> PoligonoDoEstribo(IReadOnlyList<(double X, double Y)> vertices)
    {
        int n = vertices.Count - 5;
        if (n < 3) return null;
        var c = vertices[2];
        var fim = vertices[n + 2];
        if (Math.Abs(c.X - fim.X) > 1e-6 || Math.Abs(c.Y - fim.Y) > 1e-6) return null; // não passa duas vezes pelo canto
        return vertices.Skip(2).Take(n).ToList();
    }

    private static List<(double X, double Y)> Limpar(IReadOnlyList<(double X, double Y)> pts)
    {
        var r = new List<(double X, double Y)>();
        foreach (var v in pts)
            if (r.Count == 0 || Math.Abs(v.X - r[r.Count - 1].X) > 1e-9 || Math.Abs(v.Y - r[r.Count - 1].Y) > 1e-9) r.Add(v);
        if (r.Count > 1 && Math.Abs(r[0].X - r[r.Count - 1].X) < 1e-9 && Math.Abs(r[0].Y - r[r.Count - 1].Y) < 1e-9)
            r.RemoveAt(r.Count - 1);
        return r;
    }

    private static double AreaComSinal(IReadOnlyList<(double X, double Y)> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count; i++)
        {
            var u = p[i];
            var v = p[(i + 1) % p.Count];
            a += u.X * v.Y - v.X * u.Y;
        }
        return a / 2;
    }

    private static ((double X, double Y) u, double l) Unitario(double x, double y)
    {
        double l = Math.Sqrt(x * x + y * y);
        return l < 1e-12 ? ((0, 0), 0) : ((x / l, y / l), l);
    }

    private static bool Retangulo(IReadOnlyList<(double X, double Y)> p)
    {
        if (p.Count != 4) return false;
        for (int i = 0; i < 4; i++)
        {
            var a = p[(i + 3) % 4];
            var b = p[i];
            var c = p[(i + 1) % 4];
            var (u, _) = Unitario(b.X - a.X, b.Y - a.Y);
            var (v, _) = Unitario(c.X - b.X, c.Y - b.Y);
            if (Math.Abs(u.X * v.X + u.Y * v.Y) > 1e-6) return false;
        }
        return true;
    }

    /// <summary>Pernas verticais ligadas alternadamente embaixo e em cima, com ganchos nas pontas.</summary>
    private static FormaEstribo Meandro(List<double> xs, double h, FormatoSegmento g, double lg, string nome)
    {
        var f = new FormaEstribo { Nome = nome };
        f.Vertices.Add(new VerticeEstribo(xs[0] + lg * R2, -lg * R2));
        f.Formatos.Add(g);
        bool emCima = true;
        for (int i = 0; i < xs.Count; i++)
        {
            double y1 = emCima ? 0 : -h, y2 = emCima ? -h : 0;
            if (i == 0) f.Vertices.Add(new VerticeEstribo(xs[i], y1));
            else { f.Vertices.Add(new VerticeEstribo(xs[i], y1)); f.Formatos.Add(FormatoSegmento.Linha); }
            f.Vertices.Add(new VerticeEstribo(xs[i], y2));
            f.Formatos.Add(FormatoSegmento.Linha);
            emCima = !emCima;
        }
        // termina embaixo (emCima = true agora) ou em cima: gancho para dentro do último vão
        var ult = f.Vertices[f.Vertices.Count - 1];
        bool terminouEmCima = Math.Abs(ult.Y) < 1e-9;
        f.Vertices.Add(new VerticeEstribo(ult.X - lg * R2, ult.Y + (terminouEmCima ? -lg * R2 : lg * R2)));
        f.Formatos.Add(g);
        return f;
    }

    /// <summary>Grampo: uma perna com ganchos nas duas pontas.</summary>
    private static FormaEstribo Grampo(double x, double h, FormatoSegmento g, double lg)
    {
        var f = new FormaEstribo { Nome = "grampo" };
        f.Vertices.Add(new VerticeEstribo(x + lg * R2, -lg * R2));
        f.Vertices.Add(new VerticeEstribo(x, 0));
        f.Vertices.Add(new VerticeEstribo(x, -h));
        f.Vertices.Add(new VerticeEstribo(x + lg * R2, -h + lg * R2));
        f.Formatos.AddRange(new[] { g, FormatoSegmento.Linha, g });
        return f;
    }

    /// <summary>Circunferência (2 meias-voltas) a partir do ponto de cima, ganchos para dentro ou para fora.</summary>
    private static FormaEstribo Circular(double raio, bool ganchoParaDentro, FormatoSegmento g, double lg)
    {
        var f = new FormaEstribo { Nome = "circular" };
        double sy = ganchoParaDentro ? -1 : 1;
        var gancho = new VerticeEstribo(raio + lg * R2, sy * lg * R2);
        f.Vertices.Add(gancho);
        f.Vertices.Add(new VerticeEstribo(raio, 0, -1));          // de cima para baixo pela direita (horário)
        f.Vertices.Add(new VerticeEstribo(raio, -2 * raio, -1));  // de baixo para cima pela esquerda
        f.Vertices.Add(new VerticeEstribo(raio, 0));
        f.Vertices.Add(gancho);
        f.Formatos.AddRange(new[] { g, FormatoSegmento.Curva, FormatoSegmento.Curva, g });
        return f;
    }
}
