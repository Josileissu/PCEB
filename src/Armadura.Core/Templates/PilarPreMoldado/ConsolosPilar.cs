using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Armadura.Core.Configuracao;
using Armadura.Core.Estribos;
using Armadura.Core.Modelo;

namespace Armadura.Core.Templates.PilarPreMoldado;

/// <summary>Consolo de um nível (uma ou mais faces), com a armadura padrão do escritório (P61/PP127).</summary>
public sealed class ConsoloPilar
{
    public int Slot { get; set; }
    /// <summary>Letra do corte de forma (B, C…).</summary>
    public string Letra { get; set; } = "B";
    public List<int> Faces { get; set; } = new List<int>();
    /// <summary>Cota do topo (m).</summary>
    public double CotaTopo { get; set; }
    /// <summary>Largura (ao longo da face), balanço, altura no pilar e na ponta, deslocamento lateral (cm).</summary>
    public double B { get; set; }
    public double A { get; set; }
    public double H { get; set; }
    public double H1 { get; set; }
    public double Desl { get; set; }
    public int TirN { get; set; }
    public double TirPhi { get; set; }
    public bool Laco { get; set; }
    public int CostN { get; set; }
    public double CostPhi { get; set; }
    public int VertN { get; set; }
    public double VertPhi { get; set; }
    public int InfN { get; set; }
    public double InfPhi { get; set; }
    public double InfAnc { get; set; }
    public int PinoN { get; set; }
    public double PinoPhi { get; set; }
    public double PinoAcima { get; set; }
    public double PinoEmb { get; set; }
    public double PinoPe { get; set; }
    public double AlmA { get; set; }
    public double AlmB { get; set; }
    public double AlmE { get; set; }

    /// <summary>Pares de faces opostas (tirante passante) e faces sozinhas (tirante ancorado no pilar).</summary>
    public List<(int F1, int F2)> Pares => new[] { (1, 3), (2, 4) }.Where(p => Faces.Contains(p.Item1) && Faces.Contains(p.Item2)).ToList();
    public List<int> Sozinhas => Faces.Where(f => !Pares.Any(p => p.F1 == f || p.F2 == f)).ToList();
    public bool Duplo => Pares.Count > 0;

    /// <summary>Volume dos consolos deste nível (m³).</summary>
    public double Volume => Faces.Count * B * A * (H + H1) / 2 / 1e6;
}

/// <summary>
/// Etapa 3 do pilar (2026-10-29): consolos — forma nas 4 vistas (de frente, de lado e trapezoidal), pinos e almofada,
/// corte de forma por nível (B-B, C-C, hachurado), detalhe "ARMADURAS CONSOLO b x h x a" com corte, planta e as formas
/// das barras (tirante com barra transversal soldada ou em laço, costura, estribos verticais, armadura inferior,
/// pinos), volume e CG. Regras da NBR 9062, 7.3 (avisos).
/// </summary>
public sealed partial class TemplatePilarPreMoldado
{
    public const string AncSoldada = "Barra transversal soldada", AncLaco = "Laço (alça horizontal)";
    private static readonly string[] LetrasCorte = { "B", "C", "D", "E" };

    private static readonly ParametroTemplate[] ParametrosConsolos = Enumerable.Range(1, 2).SelectMany(ParametrosConsolo).ToArray();

    private static IEnumerable<ParametroTemplate> ParametrosConsolo(int k)
    {
        string g = $"{11 + k}. Consolo {k} (corte {LetrasCorte[k - 1]}-{LetrasCorte[k - 1]})", p = $"C{k}";
        yield return P(g, p, "Consolo", k == 1 ? "Sim" : "Não", tipo: TipoParametro.SimNao);
        yield return P(g, p + "Faces", "Faces (ex.: 1,3 = duplo)", k == 1 ? "1,3" : "2", tipo: TipoParametro.Texto,
            descricao: "Faces do pilar (1 = +x, 2 = −y, 3 = −x, 4 = +y) que recebem este consolo, no mesmo nível.");
        yield return P(g, p + "Nivel", "Cota do topo do consolo", k == 1 ? "3.025" : "6.50", "m");
        yield return P(g, p + "B", "Largura (ao longo da face)", "40", "cm", min: 10);
        yield return P(g, p + "A", "Balanço", "25", "cm", min: 5);
        yield return P(g, p + "H", "Altura junto ao pilar", "23.5", "cm", min: 10);
        yield return P(g, p + "H1", "Altura na ponta", "23.5", "cm", min: 5, descricao: "Menor que a altura junto ao pilar = consolo trapezoidal.");
        yield return P(g, p + "Desl", "Deslocamento lateral (do centro da face)", "0", "cm");
        yield return P(g, p + "TirN", "Tirante: barras", "5", tipo: TipoParametro.Inteiro, min: 1, max: 20);
        yield return P(g, p + "TirBitola", "Tirante: bitola", "16", "mm", TipoParametro.Bitola, 6.3, 25);
        yield return P(g, p + "TirAnc", "Tirante: ancoragem na ponta", AncSoldada, tipo: TipoParametro.Opcao, opcoes: new[] { AncSoldada, AncLaco });
        yield return P(g, p + "CostN", "Costura: estribos horizontais", "4", tipo: TipoParametro.Inteiro, min: 0, max: 20);
        yield return P(g, p + "CostBitola", "Costura: bitola", "8", "mm", TipoParametro.Bitola, 5, 16);
        yield return P(g, p + "VertN", "Estribos verticais (por consolo)", "5", tipo: TipoParametro.Inteiro, min: 0, max: 20);
        yield return P(g, p + "VertBitola", "Estribos verticais: bitola", "6.3", "mm", TipoParametro.Bitola, 5, 16);
        yield return P(g, p + "InfN", "Armadura inferior (por consolo)", "5", tipo: TipoParametro.Inteiro, min: 0, max: 20);
        yield return P(g, p + "InfBitola", "Armadura inferior: bitola", "10", "mm", TipoParametro.Bitola, 5, 20);
        yield return P(g, p + "InfAnc", "Armadura inferior: comprimento dentro do pilar", "30", "cm", min: 0);
        yield return P(g, p + "PinoN", "Pinos (por consolo)", "2", tipo: TipoParametro.Inteiro, min: 0, max: 8);
        yield return P(g, p + "PinoBitola", "Pinos: bitola", "20", "mm", TipoParametro.Bitola, 10, 32);
        yield return P(g, p + "PinoAcima", "Pinos: altura acima do consolo", "47", "cm", min: 0);
        yield return P(g, p + "PinoEmb", "Pinos: embutimento no consolo", "18", "cm", min: 5);
        yield return P(g, p + "PinoPe", "Pinos: pé (dobra)", "20", "cm", min: 0);
        yield return P(g, p + "AlmA", "Almofada: no sentido do balanço", "15", "cm", min: 0, descricao: "0 = sem almofada.");
        yield return P(g, p + "AlmB", "Almofada: no sentido da largura", "30", "cm", min: 0);
        yield return P(g, p + "AlmE", "Almofada: espessura", "1.5", "cm", min: 0);
    }

    // ======================== leitura e cálculos (testáveis) ========================

    public static List<ConsoloPilar> LerConsolos(ValoresTemplate v, double cotaBase, double L, List<string> avisos)
    {
        var r = new List<ConsoloPilar>();
        for (int k = 1; k <= 2; k++)
        {
            string p = $"C{k}";
            if (!v.SimNao(p)) continue;
            var faces = (v[p + "Faces"] ?? "").Split(new[] { ',', ';', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var f) ? f : 0)
                .Where(f => f >= 1 && f <= 4).Distinct().OrderBy(f => f).ToList();
            if (faces.Count == 0) { avisos.Add($"Consolo {k}: nenhuma face válida (use 1 a 4)."); continue; }
            var c = new ConsoloPilar
            {
                Slot = k, Letra = LetrasCorte[r.Count], Faces = faces, CotaTopo = v.Numero(p + "Nivel"),
                B = v.Numero(p + "B"), A = v.Numero(p + "A"), H = v.Numero(p + "H"), H1 = Math.Min(v.Numero(p + "H1"), v.Numero(p + "H")),
                Desl = v.Numero(p + "Desl"),
                TirN = Math.Max(1, v.Inteiro(p + "TirN")), TirPhi = v.Numero(p + "TirBitola"), Laco = v[p + "TirAnc"] == AncLaco,
                CostN = v.Inteiro(p + "CostN"), CostPhi = v.Numero(p + "CostBitola"),
                VertN = v.Inteiro(p + "VertN"), VertPhi = v.Numero(p + "VertBitola"),
                InfN = v.Inteiro(p + "InfN"), InfPhi = v.Numero(p + "InfBitola"), InfAnc = v.Numero(p + "InfAnc"),
                PinoN = v.Inteiro(p + "PinoN"), PinoPhi = v.Numero(p + "PinoBitola"), PinoAcima = v.Numero(p + "PinoAcima"),
                PinoEmb = v.Numero(p + "PinoEmb"), PinoPe = v.Numero(p + "PinoPe"),
                AlmA = v.Numero(p + "AlmA"), AlmB = v.Numero(p + "AlmB"), AlmE = v.Numero(p + "AlmE"),
            };
            double z = (c.CotaTopo - cotaBase) * 100;
            if (z - c.H < 0 || z > L) { avisos.Add($"Consolo {k}: fora do pilar (topo na cota {FormatarCota(c.CotaTopo)})."); continue; }
            r.Add(c);
        }
        return r;
    }

    /// <summary>
    /// Distância mínima a2 da face externa do aparelho de apoio à ponta do consolo (NBR 9062, 7.3.3.4), arredondada a 0,5 cm:
    /// c + φ (barra soldada); c + 3,5φ (laço, φ &lt; 20); c + 5φ (laço, φ ≥ 20).
    /// </summary>
    public static double A2Minimo(double cob, double phiTirante, bool laco)
    {
        double f = !laco ? 1 : phiTirante < 20 ? 3.5 : 5;
        return Math.Ceiling((cob + f * phiTirante / 10) * 2 - 1e-9) / 2;
    }

    /// <summary>Volume total (m³): pilar com chanfros + consolos.</summary>
    public static double VolumeTotal(double b, double h, double l, double chanfro, IEnumerable<ConsoloPilar> consolos) =>
        Volume(b, h, l, chanfro) + consolos.Sum(c => c.Volume);

    /// <summary>Altura do CG (cm, a partir da base): pilar no meio do comprimento, consolos no seu centro.</summary>
    public static double AlturaCG(double b, double h, double l, double chanfro, double cotaBase, IEnumerable<ConsoloPilar> consolos)
    {
        double v0 = Volume(b, h, l, chanfro), m = v0 * l / 2, vt = v0;
        foreach (var c in consolos)
        {
            double z = (c.CotaTopo - cotaBase) * 100 - c.H / 2;
            m += c.Volume * z;
            vt += c.Volume;
        }
        return vt > 0 ? m / vt : l / 2;
    }

    // ======================== vistas ========================

    /// <summary>Face à esquerda e à direita de quem olha a face n (VISTA n).</summary>
    private static int FaceEsquerda(int vista) => vista % 4 + 1;
    private static int FaceDireita(int vista) => (vista + 2) % 4 + 1;

    /// <summary>Posições dos pinos ao longo da largura (a ±b/4 para 2, como no P61) e a distância deles à face.</summary>
    private static List<double> PinosU(ConsoloPilar c)
    {
        if (c.PinoN <= 0) return new List<double>();
        if (c.PinoN == 1) return new List<double> { 0 };
        double ext = c.B / 4;
        return Enumerable.Range(0, c.PinoN).Select(i => -ext + i * 2 * ext / (c.PinoN - 1)).ToList();
    }

    /// <summary>Faixa da almofada no sentido do balanço (distância à face do pilar) e o centro (onde ficam os pinos).</summary>
    private static (double n0, double n1, double centro) Almofada(ConsoloPilar c, double cob)
    {
        double a2 = A2Minimo(cob, c.TirPhi, c.Laco);
        double n1 = c.A - a2, n0 = Math.Max(0, n1 - c.AlmA);
        return (n0, n1, c.AlmA > 0 ? (n0 + n1) / 2 : c.A / 2);
    }

    private void DesenharConsolosNasVistas(Ctx c, List<ConsoloPilar> consolos, double[] xs, double[] larguras, double cotaBase, double[] cob)
    {
        double hv = c.hV;
        foreach (var co in consolos)
        {
            double zTop = (co.CotaTopo - cotaBase) * 100;
            foreach (int f in co.Faces)
            {
                double cf = cob[f - 1];
                var (n0, n1, nPino) = Almofada(co, cf);
                for (int vista = 1; vista <= 4; vista++)
                {
                    double x0 = xs[vista - 1], x1 = x0 + larguras[vista - 1];
                    if (vista == f)
                    {
                        // de frente: retângulo b × h (e a aresta da ponta, se trapezoidal), pinos e almofada
                        double um = (x0 + x1) / 2 + co.Desl;
                        c.Retangulo(um - co.B / 2, zTop - co.H, um + co.B / 2, zTop);
                        if (co.H1 < co.H - 1e-6) c.Linha(um - co.B / 2, zTop - co.H1, um + co.B / 2, zTop - co.H1);
                        foreach (double u in PinosU(co)) c.Linha(um + u, zTop, um + u, zTop + co.PinoAcima, "ARM-PINO");
                        if (co.AlmA > 0 && co.AlmE > 0) c.Retangulo(um - co.AlmB / 2, zTop, um + co.AlmB / 2, zTop + co.AlmE, "ARM-ALMOFADA");
                    }
                    else if (FaceEsquerda(vista) == f || FaceDireita(vista) == f)
                    {
                        // de lado: o consolo sai da aresta (trapézio se h1 < h)
                        bool esq = FaceEsquerda(vista) == f;
                        double xf = esq ? x0 : x1, s = esq ? -1 : 1;
                        R(c).Contornos.Add(new ContornoTemplate
                        {
                            Vertices = { (xf, zTop), (xf + s * co.A, zTop), (xf + s * co.A, zTop - co.H1), (xf, zTop - co.H) }, Fechado = true, Layer = "ARM-FORMA",
                        });
                        if (co.PinoN > 0) c.Linha(xf + s * nPino, zTop, xf + s * nPino, zTop + co.PinoAcima, "ARM-PINO");
                        if (co.AlmA > 0 && co.AlmE > 0) c.Retangulo(Math.Min(xf + s * n0, xf + s * n1), zTop, Math.Max(xf + s * n0, xf + s * n1), zTop + co.AlmE, "ARM-ALMOFADA");
                        // cotas do consolo na primeira vista de lado: altura e balanço
                        if (vista == 2 || (f == 2 || f == 4) && vista == 1)
                        {
                            double xc = xf + s * (co.A + 2 * hv);
                            c.Cota(xf + s * co.A, zTop - co.H1, xf + s * co.A, zTop, xc, zTop - co.H1 / 2, Math.PI / 2, c.escV);
                            c.Cota(xf, zTop, xf + s * co.A, zTop, xf + s * co.A / 2, zTop + 2.5 * hv, 0, c.escV);
                        }
                    }
                }
            }
        }
    }

    private static ResultadoTemplate R(Ctx c) => c.R;

    // ======================== cortes de forma (B-B, C-C) ========================

    /// <summary>Retângulo do consolo na planta da seção (origem no canto inferior esquerdo da seção).</summary>
    private static (double x0, double y0, double x1, double y1) RetanguloPlanta(ConsoloPilar co, int f, double b, double h, double na, double nb)
    {
        // na..nb: faixa de distância à face (0 = face, A = ponta); a largura fica centrada (com o deslocamento)
        switch (f)
        {
            case 1: { double ym = h / 2 + co.Desl; return (b + na, ym - co.B / 2, b + nb, ym + co.B / 2); }
            case 2: { double xm = b / 2 + co.Desl; return (xm - co.B / 2, -nb, xm + co.B / 2, -na); }
            case 3: { double ym = h / 2 - co.Desl; return (-nb, ym - co.B / 2, -na, ym + co.B / 2); }
            default: { double xm = b / 2 - co.Desl; return (xm - co.B / 2, h + na, xm + co.B / 2, h + nb); }
        }
    }

    /// <summary>Ponto da planta: distância n à face f e posição u ao longo da largura (a partir do centro do consolo).</summary>
    private static (double X, double Y) PontoPlanta(ConsoloPilar co, int f, double b, double h, double n, double u) => f switch
    {
        1 => (b + n, h / 2 + co.Desl + u),
        2 => (b / 2 + co.Desl + u, -n),
        3 => (-n, h / 2 - co.Desl - u),
        _ => (b / 2 - co.Desl - u, h + n),
    };

    private double CortesConsolo(Ctx c, List<ConsoloPilar> consolos, double b, double h, double xD, double yCur, double[] cob)
    {
        double hc = c.hC;
        foreach (var co in consolos)
        {
            foreach (var t in c.Linhas($"CORTE {co.Letra}-{co.Letra}", $"SECTION {co.Letra}-{co.Letra}")) { c.Texto(t, xD, yCur, 1.4 * hc); yCur -= 2.2 * hc; }
            c.Texto($"ESC 1:{c.escC:0}", xD, yCur, 0.8 * hc);
            bool f3 = co.Faces.Contains(3), f1 = co.Faces.Contains(1), f2 = co.Faces.Contains(2), f4 = co.Faces.Contains(4);
            double ox = xD + 6 * hc + (f3 ? co.A : 0);
            double topo = yCur - 6 * hc - (f4 ? co.A : 0);
            double oy = topo - h;
            c.Retangulo(ox, oy, ox + b, oy + h);
            foreach (int f in co.Faces)
            {
                var (x0, y0, x1, y1) = RetanguloPlanta(co, f, b, h, 0, co.A);
                var ret = new List<(double, double)> { (ox + x0, oy + y0), (ox + x1, oy + y0), (ox + x1, oy + y1), (ox + x0, oy + y1) };
                R(c).Contornos.Add(new ContornoTemplate { Vertices = ret, Fechado = true, Layer = "ARM-FORMA" });
                R(c).Hachuras.Add(new HachuraTemplate { Vertices = ret, EscalaPadrao = 0.4 * hc });
                var (n0, n1, nPino) = Almofada(co, cob[f - 1]);
                if (co.AlmA > 0)
                {
                    var (ax0, ay0, ax1, ay1) = RetanguloPlantaLargura(co, f, b, h, n0, n1, co.AlmB);
                    c.Retangulo(ox + ax0, oy + ay0, ox + ax1, oy + ay1, "ARM-ALMOFADA");
                }
                foreach (double u in PinosU(co))
                {
                    var (px, py) = PontoPlanta(co, f, b, h, nPino, u);
                    R(c).Circulos.Add(new CirculoTemplate { X = ox + px, Y = oy + py, Raio = co.PinoPhi / 20, Layer = "ARM-PINO" });
                }
                // cotas: balanço e largura do consolo
                if (f == 1 || f == 3)
                {
                    double xa = ox + (f == 1 ? b : -co.A), xb = xa + co.A;
                    c.Cota(xa, oy + y0, xb, oy + y0, (xa + xb) / 2, oy - 3 * hc, 0, c.escC);
                    double xc = f == 1 ? ox + b + co.A : ox - co.A;
                    c.Cota(xc, oy + y0, xc, oy + y1, xc + (f == 1 ? 3 : -3) * hc, oy + (y0 + y1) / 2, Math.PI / 2, c.escC);
                }
                else
                {
                    double ya = oy + (f == 2 ? -co.A : h), yb = ya + co.A;
                    c.Cota(ox + x0, ya, ox + x0, yb, ox - 3 * hc, (ya + yb) / 2, Math.PI / 2, c.escC);
                    double yc = f == 2 ? oy - co.A : oy + h + co.A;
                    c.Cota(ox + x0, yc, ox + x1, yc, ox + (x0 + x1) / 2, yc + (f == 2 ? -3 : 3) * hc, 0, c.escC);
                }
            }
            c.Cota(ox, oy + h, ox + b, oy + h, ox + b / 2, oy + h + (f4 ? co.A : 0) + 3 * hc, 0, c.escC);
            double baixo = oy - (f2 ? co.A : 0);
            RotuloVista(c, 4, ox + b / 2, oy + h + (f4 ? co.A : 0) + 6 * hc, 0);
            RotuloVista(c, 2, ox + b / 2, baixo - 7 * hc, 0);
            RotuloVista(c, 3, ox - (f3 ? co.A : 0) - 8 * hc, oy + h / 2, Math.PI / 2);
            RotuloVista(c, 1, ox + b + (f1 ? co.A : 0) + 7 * hc, oy + h / 2, Math.PI / 2);
            yCur = baixo - 14 * hc;
        }
        return yCur;
    }

    /// <summary>Retângulo na planta entre as distâncias n0..n1 da face e com a largura dada (centrada no consolo).</summary>
    private static (double x0, double y0, double x1, double y1) RetanguloPlantaLargura(ConsoloPilar co, int f, double b, double h, double n0, double n1, double largura)
    {
        var p0 = PontoPlanta(co, f, b, h, n0, -largura / 2);
        var p1 = PontoPlanta(co, f, b, h, n1, largura / 2);
        return (Math.Min(p0.X, p1.X), Math.Min(p0.Y, p1.Y), Math.Max(p0.X, p1.X), Math.Max(p0.Y, p1.Y));
    }

    // ======================== detalhe das armaduras do consolo ========================

    /// <summary>
    /// Detalhe "ARMADURAS CONSOLO b x h x a" (como no P61), abaixo das vistas: corte pelo consolo com as barras e
    /// indicações, e as formas das barras (as armaduras do plugin, com descrição e cotas). Devolve o y livre abaixo.
    /// </summary>
    private double DetalheConsolo(Ctx c, ConfiguracaoArmadura cfg, ConsoloPilar co, double b, double h, double[] cob, double x0, double yTopo, string acoLong, string acoEst)
    {
        var r = c.R;
        double hc = c.hC;
        double cc = co.Faces.Select(f => cob[f - 1]).Min();
        string tipo = co.Duplo ? " - DUPLO" : "";
        string titulo = $"ARMADURAS CONSOLO {Num(co.B, "0.#")}x{Num(co.H, "0.#")}" + (co.H1 < co.H - 1e-6 ? $"/{Num(co.H1, "0.#")}" : "") + $"x{Num(co.A, "0.#")}";
        double y = yTopo;
        foreach (var t in c.Linhas(titulo + tipo, $"CORBEL REINFORCEMENT {co.B:0.#}x{co.H:0.#}x{co.A:0.#}" + (co.Duplo ? " - DOUBLE" : "")))
        { c.Texto(t, x0, y, 1.4 * hc); y -= 2.2 * hc; }
        c.Texto($"ESC 1:{c.escC:0}", x0, y, 0.8 * hc);
        y -= 8 * hc;

        // corte pelo consolo: pilar (largura na direção do balanço) com o(s) consolo(s); barras em linhas finas
        int fDir = co.Pares.Count > 0 ? co.Pares[0].F1 : co.Faces[0];
        bool duplo = co.Pares.Count > 0;
        double colW = fDir == 1 || fDir == 3 ? b : h;
        double ox = x0 + 6 * hc + (duplo ? co.A : 0), zt = y;
        double tirZ = zt - cc - co.TirPhi / 20;
        double xTipR = ox + colW + co.A, xTipL = ox - co.A;
        // pilar (com folga acima e abaixo) e consolos
        c.Linha(ox, zt + 15, ox, zt - co.H - 15); c.Linha(ox + colW, zt + 15, ox + colW, zt - co.H - 15);
        void Consolo(double xf, double s)
        {
            r.Contornos.Add(new ContornoTemplate
            {
                Vertices = { (xf, zt), (xf + s * co.A, zt), (xf + s * co.A, zt - co.H1), (xf, zt - co.H) }, Layer = "ARM-FORMA",
            });
        }
        Consolo(ox + colW, 1);
        if (duplo) Consolo(ox, -1);

        // ---- formas das barras (armaduras do plugin), em linha abaixo do corte ----
        int nConsolos = co.Faces.Count;
        int nPares = co.Pares.Count, nSoz = co.Sozinhas.Count;
        double fy = zt - co.H - 30 * hc, fx = x0 + 4 * hc;
        void Forma(BarraTemplate bt, double largura) { r.Barras.Add(bt); fx += largura + 14 * hc; }
        string id = $"cons{co.Slot}";
        var indic = new List<(string barra, double X, double Y)>();

        // tirante
        double compPar = 2 * co.A + colW - 2 * cc, compSoz = co.A + colW - 2 * cc;
        double lgTir = cfg.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho90, co.TirPhi, acoLong, estribo: false);
        if (!co.Laco)
        {
            if (nPares > 0)
                Forma(new BarraTemplate { Nome = id + "tir", Vertices = { (fx, fy), (fx + compPar, fy) }, Bitola = co.TirPhi, Aco = acoLong,
                    Quantidade = co.TirN * nPares, Escala = c.escC }, compPar);
            if (nSoz > 0)
            {
                var bt = new BarraTemplate { Nome = id + "tirs", Vertices = { (fx, fy), (fx + compSoz, fy), (fx + compSoz, fy - lgTir) },
                    Bitola = co.TirPhi, Aco = acoLong, Quantidade = co.TirN * nSoz, Escala = c.escC };
                bt.Formatos.AddRange(new[] { FormatoSegmento.Linha, FormatoSegmento.Gancho90 });
                Forma(bt, compSoz);
            }
            // barra transversal soldada na ponta (uma por consolo)
            double compTr = co.B - 2 * cc;
            Forma(new BarraTemplate { Nome = id + "tr", Vertices = { (fx, fy), (fx + compTr, fy) }, Bitola = co.TirPhi, Aco = acoLong,
                Quantidade = nConsolos, Escala = c.escC }, compTr);
            r.Textos.Add(new TextoTemplate { Texto = c.Idioma == IdiomaDesenho.EN ? "WELDED ANCHOR BAR" : "BARRA DE ANCORAGEM SOLDADA",
                X = fx - 14 * hc - compTr / 2, Y = fy - 4 * hc, Altura = 0.7 * hc, Alinhamento = 1 });
        }
        else
        {
            // laço horizontal: U em planta, com a curva na ponta; um laço vale por 2 barras do tirante
            double larg = Math.Max(4 * co.TirPhi / 10, (co.B - 2 * cc - co.TirPhi / 10) / Math.Max(1, co.TirN - 1));
            double perna = compSoz;
            var bt = new BarraTemplate { Nome = id + "laco", Vertices = { (fx, fy), (fx + perna, fy), (fx + perna, fy + larg), (fx, fy + larg) },
                Bitola = co.TirPhi, Aco = acoLong, Quantidade = (co.TirN + 1) / 2 * nConsolos, Escala = c.escC };
            bt.Bulges.AddRange(new[] { 0.0, 1.0, 0.0, 0.0 });
            Forma(bt, perna);
        }
        indic.Add((co.Laco ? id + "laco" : (nPares > 0 ? id + "tir" : id + "tirs"), ox + colW + co.A / 2, tirZ));
        c.Linha(duplo ? xTipL + cc : ox + cc, tirZ, xTipR - cc, tirZ, "ARM-BARRA-DETALHE");
        if (!duplo && !co.Laco) c.Linha(ox + cc, tirZ, ox + cc, tirZ - lgTir, "ARM-BARRA-DETALHE");
        if (!co.Laco)
        {
            r.Circulos.Add(new CirculoTemplate { X = xTipR - cc - co.TirPhi / 20, Y = tirZ - co.TirPhi / 10, Raio = co.TirPhi / 20, Layer = "ARM-BARRA-DETALHE" });
            if (duplo) r.Circulos.Add(new CirculoTemplate { X = xTipL + cc + co.TirPhi / 20, Y = tirZ - co.TirPhi / 10, Raio = co.TirPhi / 20, Layer = "ARM-BARRA-DETALHE" });
            indic.Add((id + "tr", xTipR - cc - co.TirPhi / 20, tirZ - co.TirPhi / 10));
        }

        // costura: estribos horizontais fechados (em volta dos consolos e do pilar), em 2/3 d abaixo do tirante
        double d = co.H - cc - co.TirPhi / 20;
        if (co.CostN > 0)
        {
            double w = (duplo ? compPar : compSoz) + 2 * co.CostPhi / 10, prof = co.B - 2 * cc;
            r.Barras.Add(Estribo(cfg, id + "cost", fx, fy - prof, w, prof, co.CostPhi, acoEst, c.escC));
            r.Barras[r.Barras.Count - 1].Quantidade = co.CostN * Math.Max(1, nPares + nSoz);
            fx += w + 14 * hc;
            double passo = 2.0 / 3 * d / co.CostN;
            for (int k = 1; k <= co.CostN; k++)
            {
                double z = tirZ - k * passo;
                c.Linha(duplo ? xTipL + cc : ox + cc, z, xTipR - cc, z, "ARM-BARRA-DETALHE");
                if (k == 1) indic.Add((id + "cost", ox + colW + co.A / 2, z));
            }
        }
        // estribos verticais: no plano paralelo à face, distribuídos ao longo do balanço
        if (co.VertN > 0)
        {
            double wv = co.B - 2 * cc, hvert = co.H - 2 * cc;
            r.Barras.Add(Estribo(cfg, id + "vert", fx, fy - hvert, wv, hvert, co.VertPhi, acoEst, c.escC));
            r.Barras[r.Barras.Count - 1].Quantidade = co.VertN * nConsolos;
            fx += wv + 14 * hc;
            for (int k = 0; k < co.VertN; k++)
            {
                double n = cc + (co.VertN == 1 ? (co.A - 2 * cc) / 2 : k * (co.A - 2 * cc) / (co.VertN - 1));
                double hz = co.H - (co.H - co.H1) * n / co.A;
                foreach (double s in duplo ? new[] { 1.0, -1.0 } : new[] { 1.0 })
                {
                    double xx = s > 0 ? ox + colW + n : ox - n;
                    c.Linha(xx, zt - cc, xx, zt - hz + cc, "ARM-BARRA-DETALHE");
                }
                if (k == co.VertN / 2) indic.Add((id + "vert", ox + colW + n, zt - hz / 2));
            }
        }
        // armadura inferior: perna na ponta e o trecho de baixo entrando no pilar
        if (co.InfN > 0)
        {
            double perna = co.H1 - 2 * cc, horiz = co.A - cc + co.InfAnc;
            var bt = new BarraTemplate { Nome = id + "inf", Vertices = { (fx, fy), (fx, fy - perna), (fx + horiz, fy - perna) },
                Bitola = co.InfPhi, Aco = acoLong, Quantidade = co.InfN * nConsolos, Escala = c.escC };
            Forma(bt, horiz);
            foreach (double s in duplo ? new[] { 1.0, -1.0 } : new[] { 1.0 })
            {
                double xp = s > 0 ? xTipR - cc : xTipL + cc, zb = zt - co.H1 + cc;
                c.Linha(xp, zt - cc - co.TirPhi / 10, xp, zb, "ARM-BARRA-DETALHE");
                c.Linha(xp, zb, xp - s * horiz, zb, "ARM-BARRA-DETALHE");
            }
            indic.Add((id + "inf", xTipR - cc - co.A / 3, zt - co.H1 + cc));
        }
        // pinos (L): acima do consolo, embutidos e com o pé
        if (co.PinoN > 0)
        {
            var (_, _, nPino) = Almofada(co, cc);
            var bt = new BarraTemplate { Nome = id + "pino", Vertices = { (fx, fy), (fx, fy - co.PinoAcima - co.PinoEmb), (fx + co.PinoPe, fy - co.PinoAcima - co.PinoEmb) },
                Bitola = co.PinoPhi, Aco = acoLong, Quantidade = co.PinoN * nConsolos, Escala = c.escC };
            if (co.PinoPe <= 0) bt.Vertices.RemoveAt(2);
            Forma(bt, Math.Max(co.PinoPe, 5));
            foreach (double s in duplo ? new[] { 1.0, -1.0 } : new[] { 1.0 })
            {
                double xp = s > 0 ? ox + colW + nPino : ox - nPino;
                c.Linha(xp, zt + co.PinoAcima, xp, zt - co.PinoEmb, "ARM-PINO");
                if (co.PinoPe > 0) c.Linha(xp, zt - co.PinoEmb, xp - s * co.PinoPe, zt - co.PinoEmb, "ARM-PINO");
            }
            indic.Add((id + "pino", ox + colW + nPino, zt + co.PinoAcima / 2));
        }
        // almofada sobre o consolo
        if (co.AlmA > 0 && co.AlmE > 0)
        {
            var (n0, n1, _) = Almofada(co, cc);
            c.Retangulo(ox + colW + n0, zt, ox + colW + n1, zt + co.AlmE, "ARM-ALMOFADA");
            if (duplo) c.Retangulo(ox - n1, zt, ox - n0, zt + co.AlmE, "ARM-ALMOFADA");
            string alm = $"NEOPRENE {Num(co.AlmA, "0.#")}x{Num(co.AlmB, "0.#")}x{Num(co.AlmE * 10, "0")} mm";
            c.Texto(c.Idioma == IdiomaDesenho.EN ? alm.Replace("NEOPRENE", "ELASTOMERIC PAD") : alm, ox + colW + (n0 + n1) / 2, zt + co.AlmE + 2.5 * hc, 0.7 * hc, 1);
        }
        // indicações à direita do corte, em ordem de altura
        double tx = xTipR + 8 * hc;
        var lista = indic.OrderByDescending(i => i.Y).ToList();
        double yl = zt + co.PinoAcima / 2;
        foreach (var it in lista)
        {
            r.Faixas.Add(new FaixaTemplate { Barra = it.barra, Tipo = TipoFaixa.Indicacao, Referencias = { (it.X, it.Y) }, Posicao = (tx, Math.Min(yl, it.Y)), Escala = c.escC });
            yl = Math.Min(yl, it.Y) - 1.8 * hc;
        }
        // cotas do corte: altura, balanço e o pilar
        c.Cota(xTipR, zt - co.H1, xTipR, zt, xTipR + 3 * hc, zt - co.H1 / 2, Math.PI / 2, c.escC);
        c.Cota(ox + colW, zt - co.H - 4 * hc, xTipR, zt - co.H - 4 * hc, (ox + colW + xTipR) / 2, zt - co.H - 6 * hc, 0, c.escC);
        c.Cota(ox, zt - co.H - 4 * hc, ox + colW, zt - co.H - 4 * hc, ox + colW / 2, zt - co.H - 6 * hc, 0, c.escC);

        VerificarConsolo(r, co, cc);
        return Math.Min(fy - Math.Max(co.PinoAcima + co.PinoEmb, co.H) - 16 * hc, zt - co.H - 20 * hc);
    }

    /// <summary>Regras de detalhamento de consolos da NBR 9062 (7.3), como avisos.</summary>
    private static void VerificarConsolo(ResultadoTemplate r, ConsoloPilar co, double cob)
    {
        string nome = $"Consolo {co.Slot}";
        double d = co.H - cob - co.TirPhi / 20;
        var (n0, n1, nCarga) = Almofada(co, cob);
        double ad = nCarga / d;
        if (ad > 1) r.Avisos.Add($"{nome}: a/d = {Num(ad, "0.00")} > 1 — dimensionar como viga em balanço (NBR 9062, 7.3.2.2 a).");
        if (co.H1 < co.H / 2 - 1e-6) r.Avisos.Add($"{nome}: altura na ponta {Num(co.H1, "0.#")} < h/2 (NBR 9062, 7.3.3.1).");
        double fator = co.Laco ? 8 : 6;
        double menor = Math.Min(co.B, co.H);
        if (co.TirPhi > Math.Min(25, menor * 10 / fator))
            r.Avisos.Add($"{nome}: tirante ø{Num(co.TirPhi, "0.0")} > 1/{fator:0} da menor dimensão do consolo ou 25 mm (NBR 9062, 7.3.3.{(co.Laco ? 6 : 7)}).");
        if (co.TirN > 1)
        {
            double esp = (co.B - 2 * cob - co.TirPhi / 10) / (co.TirN - 1);
            double lim = Math.Min((co.Laco ? 15 : 20) * co.TirPhi / 10, d);
            if (esp > lim + 1e-6) r.Avisos.Add($"{nome}: espaçamento do tirante {Num(esp, "0.#")} cm > {Num(lim, "0.#")} cm (NBR 9062, 7.3.3.{(co.Laco ? 6 : 7)}).");
        }
        if (co.CostN > 0)
        {
            if (co.CostPhi > menor * 10 / 15) r.Avisos.Add($"{nome}: costura ø{Num(co.CostPhi, "0.0")} > 1/15 da menor dimensão (NBR 9062, 7.3.3.12).");
            double passo = 2.0 / 3 * d / co.CostN;
            if (passo > Math.Min(10, co.A) + 1e-6) r.Avisos.Add($"{nome}: costura a cada {Num(passo, "0.#")} cm > 10 cm ou a (NBR 9062, 7.3.3.12).");
        }
        else r.Avisos.Add($"{nome}: sem armadura de costura (obrigatória, NBR 9062, 7.3.6).");
        if (co.AlmA > 0)
        {
            if (n0 <= 1e-6 && n1 - n0 < co.AlmA - 1e-6)
                r.Avisos.Add($"{nome}: almofada de {Num(co.AlmA, "0.#")} cm não cabe com a2 = {Num(A2Minimo(cob, co.TirPhi, co.Laco), "0.#")} cm (NBR 9062, 7.3.3.4).");
            if (co.AlmB > co.B - 2 * cob + 1e-6) r.Avisos.Add($"{nome}: almofada mais larga que o consolo menos os cobrimentos.");
        }
    }
}
