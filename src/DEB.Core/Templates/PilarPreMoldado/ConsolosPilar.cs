using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Estribos;
using DEB.Core.Modelo;

namespace DEB.Core.Templates.PilarPreMoldado;

/// <summary>Consolo de um nível (uma ou mais faces), com a armadura padrão do escritório (P61/PP127).</summary>
public sealed class ConsoloPilar
{
    public int Slot { get; set; }
    /// <summary>Nome do tipo no catálogo de consolos padrão.</summary>
    public string Tipo { get; set; } = "";
    /// <summary>O tipo é duplo (tirante passante nas faces opostas).</summary>
    public bool TipoDuplo { get; set; }
    /// <summary>Letra do corte de forma (B, C…).</summary>
    public string Letra { get; set; } = "B";
    public List<int> Faces { get; set; } = new List<int>();
    /// <summary>Distância da base do pilar ao topo do consolo (cm).</summary>
    public double ZTopo { get; set; }
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
    public List<(int F1, int F2)> Pares => !TipoDuplo ? new List<(int, int)>()
        : new[] { (1, 3), (2, 4) }.Where(p => Faces.Contains(p.Item1) && Faces.Contains(p.Item2)).ToList();
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
    private static readonly string[] LetrasCorte = { "B", "C", "D", "E", "F", "G" };

    public const string SemConsolo = "(nenhum)";

    /// <summary>
    /// Catálogo de consolos padrão usado pelo template (o plugin põe aqui o da configuração do usuário ao carregar e
    /// depois do ARMCONSOLOS). As opções do parâmetro "tipo" saem dele.
    /// </summary>
    public static List<TipoConsolo> Catalogo { get; set; } = TipoConsolo.Padrao();

    /// <summary>Uma lista (janela) com os consolos do pilar: tipo do catálogo, faces, distância da base ao topo, deslocamento.</summary>
    private static IEnumerable<ParametroTemplate> ParametrosConsolos()
    {
        string padrao = Catalogo.Count > 0 ? $"{Catalogo[0].Nome}|{AjustarFaces(Catalogo[0].Nome, "1")}|592.5|0" : "";
        yield return new ParametroTemplate
        {
            Grupo = "12. Consolos", Nome = "Consolos", Rotulo = "Consolos do pilar", Tipo = TipoParametro.Tabela, NomeItem = "consolo",
            Padrao = padrao,
            Descricao = "Clique em … para incluir os consolos: tipo (catálogo do ARMCONSOLOS), face (1 = +x, 2 = −y, 3 = −x, 4 = +y; " +
                        "tipo duplo já ocupa as duas faces opostas: 1 e 3 ou 2 e 4), distância da base ao topo e deslocamento lateral.",
            Colunas =
            {
                new ColunaTabela { Nome = "Tipo", Rotulo = "Tipo", Tipo = TipoParametro.Opcao, Opcoes = Catalogo.Select(t => t.Nome).ToList(),
                    Padrao = Catalogo.Count > 0 ? Catalogo[0].Nome : "", Largura = 170 },
                new ColunaTabela
                {
                    Nome = "Faces", Rotulo = "Faces", Tipo = TipoParametro.Opcao, Opcoes = FacesSimples.Concat(FacesDuplas).ToList(),
                    Padrao = "1", Largura = 80,
                    OpcoesDaLinha = l => OpcoesFaces(l.Count > 0 ? l[0] : ""),
                    Ajustar = (l, f) => AjustarFaces(l.Count > 0 ? l[0] : "", f),
                },
                new ColunaTabela { Nome = "Dist", Rotulo = "Base → topo (cm)", Tipo = TipoParametro.Numero, Padrao = "300", Largura = 110 },
                new ColunaTabela { Nome = "Desl", Rotulo = "Deslocamento (cm)", Tipo = TipoParametro.Numero, Padrao = "0", Largura = 110 },
            },
        };
    }

    private static readonly string[] FacesSimples = { "1", "2", "3", "4" };
    private static readonly string[] FacesDuplas = { "1 e 3", "2 e 4" };

    private static bool TipoEhDuplo(string tipo) => TipoConsolo.Obter(Catalogo, tipo)?.Duplo == true;

    /// <summary>Lista de faces da janela conforme o tipo: duplo = pares opostos ("1 e 3", "2 e 4"); simples = 1 a 4.</summary>
    public static List<string> OpcoesFaces(string tipo) => (TipoEhDuplo(tipo) ? FacesDuplas : FacesSimples).ToList();

    /// <summary>
    /// Face escolhida → valor da lista do tipo (pedido do usuário, 2026-10-29): no duplo, a face 1 (ou 3) vira "1 e 3" e a 2 (ou 4)
    /// vira "2 e 4"; no simples, um par vira a 1ª face. Aceita "1,3", "1 e 3", "3". Sem face válida: "".
    /// </summary>
    public static string AjustarFaces(string tipo, string valor)
    {
        var f = (valor ?? "").Split(new[] { ',', ';', ' ', '/', 'e', 'E', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .Where(n => n >= 1 && n <= 4).ToList();
        if (f.Count == 0) return "";
        if (TipoEhDuplo(tipo)) return f[0] % 2 == 1 ? "1 e 3" : "2 e 4";
        return f[0].ToString(CultureInfo.InvariantCulture);
    }

    // ======================== leitura e cálculos (testáveis) ========================

    public static List<ConsoloPilar> LerConsolos(ValoresTemplate v, IReadOnlyList<TipoConsolo> catalogo, double L, List<string> avisos)
    {
        var r = new List<ConsoloPilar>();
        var linhas = v.Tabela("Consolos");
        for (int k = 1; k <= linhas.Count; k++)
        {
            var l = linhas[k - 1];
            string nomeTipo = ValoresTemplate.Celula(l, 0);
            if (string.IsNullOrWhiteSpace(nomeTipo) || nomeTipo == SemConsolo) continue;
            var t = TipoConsolo.Obter(catalogo, nomeTipo);
            if (t == null) { avisos.Add($"Consolo {k}: tipo \"{nomeTipo}\" não existe no catálogo (ARMCONSOLOS)."); continue; }
            var faces = ValoresTemplate.Celula(l, 1).Split(new[] { ',', ';', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var f) ? f : 0)
                .Where(f => f >= 1 && f <= 4).Distinct().OrderBy(f => f).ToList();
            if (faces.Count == 0) { avisos.Add($"Consolo {k}: nenhuma face válida (use 1 a 4)."); continue; }
            if (t.Duplo && !((faces.Contains(1) && faces.Contains(3)) || (faces.Contains(2) && faces.Contains(4))))
            {
                avisos.Add($"Consolo {k}: o tipo \"{t.Nome}\" é duplo e precisa de duas faces opostas (1,3 ou 2,4).");
                continue;
            }
            var c = new ConsoloPilar
            {
                Slot = k, Tipo = t.Nome, TipoDuplo = t.Duplo, Letra = LetrasCorte[r.Count % LetrasCorte.Length], Faces = faces,
                ZTopo = ValoresTemplate.NumeroCelula(l, 2), Desl = ValoresTemplate.NumeroCelula(l, 3),
                B = t.B, A = t.A, H = t.H, H1 = Math.Min(t.H1, t.H),
                TirN = Math.Max(1, t.TirN), TirPhi = t.TirPhi, Laco = t.Laco,
                CostN = t.CostN, CostPhi = t.CostPhi, VertN = t.VertN, VertPhi = t.VertPhi,
                InfN = t.InfN, InfPhi = t.InfPhi, InfAnc = t.InfAnc,
                PinoN = t.PinoN, PinoPhi = t.PinoPhi, PinoAcima = t.PinoAcima, PinoEmb = t.PinoEmb, PinoPe = t.PinoPe,
                AlmA = t.AlmA, AlmB = t.AlmB, AlmE = t.AlmE,
            };
            if (c.ZTopo - c.H < 0 || c.ZTopo > L) { avisos.Add($"Consolo {k}: fora do pilar (topo a {Num(c.ZTopo, "0.#")} cm da base)."); continue; }
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
    public static double AlturaCG(double b, double h, double l, double chanfro, IEnumerable<ConsoloPilar> consolos)
    {
        double v0 = Volume(b, h, l, chanfro), m = v0 * l / 2, vt = v0;
        foreach (var c in consolos)
        {
            double z = c.ZTopo - c.H / 2;
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

    private void DesenharConsolosNasVistas(Ctx c, List<ConsoloPilar> consolos, double[] xs, double[] larguras, double[] cob)
    {
        double hv = c.hV;
        foreach (var co in consolos)
        {
            double zTop = co.ZTopo;
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
                    }
                }
            }
        }
    }

    private static ResultadoTemplate R(Ctx c) => c.R;

    /// <summary>
    /// Cotas dos consolos nas vistas, como no P56 (pedido do usuário, 2026-10-30): na vista em que o consolo sai pela direita,
    /// junto dele a altura do consolo e a distância do topo dele ao nível acima; mais afastada, a cota da base ao topo do
    /// consolo. As cotas vão para colunas sem sobreposição. Devolve, por vista, o x mais à direita das cotas.
    /// </summary>
    private static double[] CotasConsolosNasVistas(Ctx c, List<ConsoloPilar> consolos, double[] xs, double[] larguras, List<double> ysNiveis, double L)
    {
        double hv = c.hV;
        var xMax = xs.Select((x, i) => x + larguras[i]).ToArray();
        var niveis = ysNiveis.Concat(new[] { L }).Distinct().OrderBy(y => y).ToList();
        foreach (var grupo in consolos.GroupBy(co => co.Pares.Count > 0 ? co.Pares[0].F1 : co.Faces[0]))
        {
            int face = grupo.Key;
            int v = Enumerable.Range(1, 4).First(k => FaceDireita(k) == face) - 1;
            double xBase = xs[v] + larguras[v], xPonta = xBase + grupo.Max(co => co.A);
            var colunas = new List<List<(double a, double b)>>();
            int Coluna(double a, double b, int desde)
            {
                for (int k = desde; ; k++)
                {
                    if (k == colunas.Count) colunas.Add(new List<(double, double)>());
                    if (colunas[k].All(q => b <= q.a + 1e-6 || a >= q.b - 1e-6)) { colunas[k].Add((a, b)); return k; }
                }
            }
            double X(int k) => xPonta + (2.1 + 2.1 * k) * hv;
            var lista = grupo.OrderBy(co => co.ZTopo).ToList();
            foreach (var co in lista)
            {
                double zb = co.ZTopo - co.H, zt = co.ZTopo;
                int k = Coluna(zb, zt, 0);
                c.Cota(xBase + co.A, zb, xBase + co.A, zt, X(k), (zb + zt) / 2, Math.PI / 2, c.escV);
                double acima = niveis.FirstOrDefault(y => y > zt + 1e-6);
                if (acima > zt + 1e-6)
                {
                    int k2 = Coluna(zt, acima, 0);
                    c.Cota(xBase + co.A, zt, xBase + co.A, acima, X(k2), (zt + acima) / 2, Math.PI / 2, c.escV);
                }
            }
            int interno = colunas.Count;
            foreach (var co in lista)
            {
                int k = Coluna(0, co.ZTopo, interno);
                c.Cota(xBase, 0, xBase + co.A, co.ZTopo, X(k), co.ZTopo / 2, Math.PI / 2, c.escV);
            }
            xMax[v] = Math.Max(xMax[v], X(colunas.Count - 1) + hv);
        }
        return xMax;
    }

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
            // folgas para as cotas encadeadas do consolo (até ~5 textos além da ponta)
            double extra = 5.5 * hc;
            double ox = xD + 8 * hc + (f3 ? co.A + extra : 0);
            double topo = yCur - 7 * hc - (f4 ? co.A + extra : 0);
            double oy = topo - h;
            c.Retangulo(ox, oy, ox + b, oy + h, "ARM-PILAR-SECAO"); // pilar cortado (magenta no P56)
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
                CotasConsoloNoCorte(c, co, f, b, h, ox, oy, nPino);
            }
            // pilar: b em cima e h à direita (além dos consolos dessas faces)
            double yb = oy + h + (f4 ? co.A + extra : 0) + 2.5 * hc, xh = ox + b + (f1 ? co.A + extra : 0) + 2.5 * hc;
            c.Cota(ox, oy + h, ox + b, oy + h, ox + b / 2, yb, 0, c.escC);
            c.Cota(ox + b, oy, ox + b, oy + h, xh, oy + h / 2, Math.PI / 2, c.escC);
            double baixo = oy - (f2 ? co.A + extra : 0);
            RotuloVista(c, 4, ox + b / 2, yb + 1.5 * hc);
            RotuloVista(c, 2, ox + b / 2, baixo - 1.5 * hc);
            RotuloVista(c, 3, ox - (f3 ? co.A + extra : 0) - 1.5 * hc, oy + h / 2);
            RotuloVista(c, 1, xh + 2 * hc, oy + h / 2);
            yCur = baixo - 12 * hc;
        }
        return yCur;
    }

    /// <summary>
    /// Cotas encadeadas do consolo no corte de forma, como no P56: ao longo da face, do canto do pilar até o consolo e a largura
    /// dele (e até o outro canto), com a cadeia dos pinos por dentro; no sentido do balanço, da face ao pino e do pino à ponta.
    /// </summary>
    private static void CotasConsoloNoCorte(Ctx c, ConsoloPilar co, int f, double b, double h, double ox, double oy, double nPino)
    {
        double hc = c.hC, W = f == 1 || f == 3 ? h : b;
        // s = ao longo da face (0..W, no sentido de x ou y), t = para fora da face
        (double X, double Y) P(double s, double t) => f switch
        {
            1 => (ox + b + t, oy + s),
            2 => (ox + s, oy - t),
            3 => (ox - t, oy + s),
            _ => (ox + s, oy + h + t),
        };
        double angAo = f == 1 || f == 3 ? Math.PI / 2 : 0, angPerp = angAo == 0 ? Math.PI / 2 : 0;
        // posição do consolo ao longo da face (no corte, s cresce com x ou y)
        var p0 = PontoPlanta(co, f, b, h, 0, -co.B / 2);
        var p1 = PontoPlanta(co, f, b, h, 0, co.B / 2);
        double sa = f == 1 || f == 3 ? Math.Min(p0.Y, p1.Y) : Math.Min(p0.X, p1.X), sb = sa + co.B;
        void Cadeia(IEnumerable<double> ss, double t)
        {
            var l = ss.Distinct().OrderBy(s => s).ToList();
            for (int i = 0; i + 1 < l.Count; i++)
            {
                if (l[i + 1] - l[i] < 1e-6) continue;
                // linhas de chamada saem da ponta do consolo (dentro dele) ou da face do pilar (fora)
                double T(double s) => s >= sa - 1e-6 && s <= sb + 1e-6 ? co.A : 0;
                var a = P(l[i], T(l[i])); var bb = P(l[i + 1], T(l[i + 1])); var m = P((l[i] + l[i + 1]) / 2, t);
                c.Cota(a.X, a.Y, bb.X, bb.Y, m.X, m.Y, angAo, c.escC);
            }
        }
        var pinos = PinosU(co).Select(u => (f == 3 || f == 4 ? -u : u) + (sa + sb) / 2).ToList();
        if (pinos.Count > 0) Cadeia(new[] { sa, sb }.Concat(pinos), co.A + 1.5 * hc);
        Cadeia(new[] { 0, sa, sb, W }.Where(s => s >= -1e-6 && s <= W + 1e-6), co.A + 3.5 * hc);
        // balanço: da face ao pino e do pino à ponta (junto da ponta lateral do consolo), e o total mais afastado
        double sCota = sb + 1.5 * hc;
        void Perp(double t0, double t1, double s)
        {
            var a = P(sb, t0); var bb = P(sb, t1); var m = P(s, (t0 + t1) / 2);
            c.Cota(a.X, a.Y, bb.X, bb.Y, m.X, m.Y, angPerp, c.escC);
        }
        if (co.PinoN > 0 && nPino > 1e-6 && nPino < co.A - 1e-6) { Perp(0, nPino, sCota); Perp(nPino, co.A, sCota); Perp(0, co.A, sCota + 2 * hc); }
        else Perp(0, co.A, sCota);
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
    /// Quadro "ARMADURAS console b x h x a - n un." no padrão do escritório (P56, pedido do usuário em 2026-10-30): moldura com a
    /// PLANTA (pilar cortado, consolo, pinos, cotas e marcas A/B), o CORTE B-B (pilar com linhas de interrupção, consolo e as
    /// barras, pino em azul, "Solda" na ponta do tirante), o CORTE AA (seção do consolo) e as formas das barras (as armaduras
    /// do plugin) com os nomes: Tirante, Arm. Inferior, Estribo vertical, Estribo horizontal, Barra de ancoragem soldada e Pino.
    /// Devolve o y livre abaixo do quadro.
    /// </summary>
    private double DetalheConsolo(Ctx c, ConfiguracaoArmadura cfg, ConsoloPilar co, int nPares, int nSoz, string id,
        double b, double h, double[] cob, double x0, double yTopo, string acoLong, string acoEst)
    {
        var r = c.R;
        double hc = c.hC, ht = 0.8 * hc;
        double cc = co.Faces.Select(f => cob[f - 1]).Min();
        int fDir = co.Pares.Count > 0 ? co.Pares[0].F1 : co.Faces[0];
        bool duplo = nPares > 0;
        int nConsolos = 2 * nPares + nSoz;
        double colW = fDir == 1 || fDir == 3 ? b : h;   // pilar na direção do balanço
        double wFace = fDir == 1 || fDir == 3 ? h : b;  // pilar ao longo da face
        string D(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        string dims = $"{D(co.B)}x{D(co.H)}" + (co.H1 < co.H - 1e-6 ? $"/{D(co.H1)}" : "") + $"x{D(co.A)}";
        var marca = Marca(r);
        double yMin = yTopo;
        void Rotulo(string t, double x, double y, double alt = 0) => c.Texto(t, x, y, alt > 0 ? alt : ht, 1);

        // ---- título ----
        double y = yTopo - 2 * hc;
        foreach (var t in c.Linhas($"ARMADURAS console {dims}" + (nConsolos > 1 ? $" - {nConsolos}un." : ""),
                                   $"CORBEL REINFORCEMENT {dims}" + (nConsolos > 1 ? $" - {nConsolos}pcs." : "")))
        { c.Texto(t, x0 + 1.5 * hc, y, 1.4 * hc); y -= 2.1 * hc; }
        c.Texto($"ESC 1:{c.escC:0}", x0 + 1.5 * hc, y, 0.7 * hc);
        double yR1 = y - 4 * hc;

        var (_, _, nPino) = Almofada(co, cc);
        var pinos = PinosU(co);
        double lgTir = cfg.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho90, co.TirPhi, acoLong, estribo: false);

        // =================== PLANTA (consolo à esquerda do pilar) ===================
        double xP0 = x0 + 9 * hc + co.A, yPc = yR1 - 7 * hc - Math.Max(wFace, co.B) / 2;
        Rotulo("PLANTA", xP0 + colW / 2 - co.A / 2, yR1 - 1.5 * hc, hc);
        c.Retangulo(xP0, yPc - wFace / 2, xP0 + colW, yPc + wFace / 2, "ARM-PILAR-SECAO");
        c.Retangulo(xP0 - co.A, yPc - co.B / 2, xP0, yPc + co.B / 2);
        foreach (double u in pinos) r.Circulos.Add(new CirculoTemplate { X = xP0 - nPino, Y = yPc + u, Raio = co.PinoPhi / 20, Layer = "ARM-PINO" });
        {
            double yCad = yPc + Math.Max(wFace, co.B) / 2 + 2 * hc, yTot = yCad + 2 * hc;
            if (pinos.Count > 0 && nPino > 1e-6 && nPino < co.A - 1e-6)
            {
                c.Cota(xP0 - co.A, yPc + co.B / 2, xP0 - nPino, yPc + co.B / 2, xP0 - (co.A + nPino) / 2, yCad, 0, c.escC);
                c.Cota(xP0 - nPino, yPc + co.B / 2, xP0, yPc + co.B / 2, xP0 - nPino / 2, yCad, 0, c.escC);
            }
            c.Cota(xP0 - co.A, yPc + co.B / 2, xP0, yPc + co.B / 2, xP0 - co.A / 2, yTot, 0, c.escC);
            c.Cota(xP0, yPc + wFace / 2, xP0 + colW, yPc + wFace / 2, xP0 + colW / 2, yTot, 0, c.escC);
            double xCad = xP0 - co.A - 2 * hc, xTot = xCad - 2 * hc;
            var ys = new[] { -co.B / 2, co.B / 2 }.Concat(pinos).Distinct().OrderBy(u => u).ToList();
            if (ys.Count > 2)
                for (int i = 0; i + 1 < ys.Count; i++)
                    c.Cota(xP0 - co.A, yPc + ys[i], xP0 - co.A, yPc + ys[i + 1], xCad, yPc + (ys[i] + ys[i + 1]) / 2, Math.PI / 2, c.escC);
            c.Cota(xP0 - co.A, yPc - co.B / 2, xP0 - co.A, yPc + co.B / 2, ys.Count > 2 ? xTot : xCad, yPc, Math.PI / 2, c.escC);
            // marcas: A (corte AA, atravessando o consolo) em cima e embaixo; B (corte B-B, ao longo do balanço) dos lados
            double xA = xP0 - co.A / 2, yAt = yTot + 1.5 * hc, yAb = yPc - Math.Max(wFace, co.B) / 2 - 0.8 * hc;
            foreach (var (ya, s) in new[] { (yAt, 1.0), (yAb, -1.0) })
            {
                c.Linha(xA, ya, xA, ya + s * 1.5 * hc, "ARM-TEXTO-FORMA");
                c.Linha(xA, ya + s * 1.5 * hc, xA + 1.2 * hc, ya + s * 1.5 * hc, "ARM-TEXTO-FORMA");
                Rotulo("A", xA - 0.9 * hc, ya + s * 1.5 * hc - 0.4 * hc, hc);
            }
            foreach (var (xb, s) in new[] { ((ys.Count > 2 ? xTot : xCad) - 3.5 * hc, -1.0), (xP0 + colW + 2 * hc, 1.0) })
            {
                c.Linha(xb, yPc, xb, yPc - 1.2 * hc, "ARM-TEXTO-FORMA");
                c.Linha(xb, yPc, xb - s * 1.2 * hc, yPc, "ARM-TEXTO-FORMA");
                Rotulo("B", xb, yPc + 0.5 * hc, hc);
            }
            yMin = Math.Min(yMin, yAb - 2 * hc);
        }

        // =================== CORTE B-B (consolo à esquerda, pilar interrompido) ===================
        double xB = xP0 + colW + 18 * hc + co.A;                  // face do pilar
        double zt = yR1 - 6 * hc - Math.Max(co.PinoAcima, 2 * hc); // topo do consolo
        double tirZ = zt - cc - co.TirPhi / 20, xTip = xB - co.A;
        Rotulo("CORTE B-B", xB + colW / 2, yR1 - 1.5 * hc, hc);
        double yPilTopo = zt + Math.Max(co.PinoAcima, 2 * hc) + 2 * hc, yPilBase = zt - co.H - 8 * hc;
        c.Linha(xB, yPilTopo, xB, zt); c.Linha(xB, zt - co.H, xB, yPilBase); c.Linha(xB + colW, yPilTopo, xB + colW, yPilBase);
        foreach (double yz in new[] { yPilTopo, yPilBase }) Interrupcao(c, xB - 1 * hc, xB + colW + 1 * hc, yz, hc);
        r.Contornos.Add(new ContornoTemplate { Vertices = { (xB, zt), (xTip, zt), (xTip, zt - co.H1), (xB, zt - co.H) }, Layer = "ARM-FORMA" });
        var indic = new List<(string barra, double X, double Y, bool esq)>();
        // tirante (até a outra face do pilar: dobra para baixo no sozinho; segue interrompido no duplo)
        double xFimTir = xB + colW - cc;
        c.Linha(xTip + cc, tirZ, duplo ? xB + colW + 1 * hc : xFimTir, tirZ, "ARM-BARRA-DETALHE");
        if (!duplo && !co.Laco) c.Linha(xFimTir, tirZ, xFimTir, tirZ - lgTir, "ARM-BARRA-DETALHE");
        indic.Add((co.Laco ? id + "laco" : (nPares > 0 ? id + "tir" : id + "tirs"), xB + colW / 2, tirZ, false));
        if (!co.Laco)
        {
            r.Circulos.Add(new CirculoTemplate { X = xTip + cc + co.TirPhi / 20, Y = tirZ - co.TirPhi / 10, Raio = co.TirPhi / 20, Layer = "ARM-BARRA-DETALHE" });
            indic.Add((id + "tr", xTip + cc + co.TirPhi / 20, tirZ - co.TirPhi / 10, true));
            // abaixo do consolo (à esquerda ficam as indicações e a cota do pino)
            double ySolda = zt - co.H - 3 * hc;
            Rotulo("Solda", xTip - 2 * hc, ySolda);
            Seta(c, (xTip - 0.5 * hc, ySolda + 1.1 * hc), (xTip + cc, tirZ), ht, "ARM-DETALHE");
        }
        // costura (estribos horizontais): da ponta até a outra face do pilar
        double d = co.H - cc - co.TirPhi / 20;
        if (co.CostN > 0)
        {
            double passo = 2.0 / 3 * d / co.CostN;
            for (int k = 1; k <= co.CostN; k++)
            {
                double z = tirZ - k * passo;
                c.Linha(xTip + cc, z, xB + colW - cc, z, "ARM-BARRA-DETALHE");
                if (k == (co.CostN + 1) / 2) indic.Add((id + "cost", xB + colW / 2, z, false));
            }
        }
        // estribos verticais no balanço
        if (co.VertN > 0)
            for (int k = 0; k < co.VertN; k++)
            {
                double n = cc + (co.VertN == 1 ? (co.A - 2 * cc) / 2 : k * (co.A - 2 * cc) / (co.VertN - 1));
                double hz = co.H - (co.H - co.H1) * n / co.A;
                c.Linha(xB - n, zt - cc, xB - n, zt - hz + cc, "ARM-BARRA-DETALHE");
                if (k == co.VertN / 2) indic.Add((id + "vert", xB - n, zt - cc - 0.5, true));
            }
        // armadura inferior: perna na ponta e o trecho de baixo entrando no pilar
        if (co.InfN > 0)
        {
            double zb = zt - co.H1 + cc, horiz = co.A - cc + co.InfAnc;
            c.Linha(xTip + cc, tirZ - co.TirPhi / 10, xTip + cc, zb, "ARM-BARRA-DETALHE");
            c.Linha(xTip + cc, zb, xTip + cc + horiz, zb, "ARM-BARRA-DETALHE");
            indic.Add((id + "inf", xTip + cc + horiz * 0.4, zb, true));
        }
        // pino (azul) acima do consolo, embutido, com o pé para o pilar
        if (co.PinoN > 0)
        {
            double xp = xB - nPino;
            c.Linha(xp, zt + co.PinoAcima, xp, zt - co.PinoEmb, "ARM-BARRA-2");
            if (co.PinoPe > 0) c.Linha(xp, zt - co.PinoEmb, xp + co.PinoPe, zt - co.PinoEmb, "ARM-BARRA-2");
            indic.Add((id + "pino", xp, zt + co.PinoAcima * 0.6, true));
            c.Cota(xTip, zt, xp, zt + co.PinoAcima, xTip - 3 * hc, zt + co.PinoAcima / 2, Math.PI / 2, c.escC);
        }
        if (co.AlmA > 0 && co.AlmE > 0)
        {
            var (n0, n1, _) = Almofada(co, cc);
            c.Retangulo(xB - n1, zt, xB - n0, zt + co.AlmE, "ARM-ALMOFADA");
            string alm = $"NEOPRENE {Num(co.AlmA, "0.#")}x{Num(co.AlmB, "0.#")}x{Num(co.AlmE * 10, "0")} mm";
            c.Texto(c.Idioma == IdiomaDesenho.EN ? alm.Replace("NEOPRENE", "ELASTOMERIC PAD") : alm, xB + colW + 2 * hc, zt + co.AlmE + 1.5 * hc, 0.7 * hc);
        }
        c.Cota(xTip, zt - co.H1, xTip, zt, xTip - 3 * hc, zt - co.H1 / 2, Math.PI / 2, c.escC);
        // indicações: as do lado do consolo à esquerda (em ordem de altura), as do pilar à direita
        foreach (var lado in indic.GroupBy(i => i.esq))
        {
            double tx = lado.Key ? xTip - 8 * hc : xB + colW + 4 * hc, yl = double.MaxValue;
            foreach (var it in lado.OrderByDescending(i => i.Y))
            {
                double yy = Math.Min(yl, it.Y + 1.5 * hc);
                r.Faixas.Add(new FaixaTemplate { Barra = it.barra, Tipo = TipoFaixa.Indicacao, Referencias = { (it.X, it.Y) }, Posicao = (tx, yy), Escala = c.escC, Patamar = true });
                yl = yy - 1.9 * hc;
            }
        }
        yMin = Math.Min(yMin, yPilBase - 2 * hc);

        // =================== formas das barras: tirante, ancoragem e armadura inferior (à direita) ===================
        double xS = xB + colW + 16 * hc, yS = yR1 - 5 * hc;
        double compPar = 2 * co.A + colW - 2 * cc, compSoz = co.A + colW - 2 * cc;
        double xFim = xS;
        if (!co.Laco)
        {
            Rotulo("Tirante", xS + (duplo ? compPar : compSoz) / 2, yS + 5.2 * hc);
            if (nPares > 0)
                r.Barras.Add(new BarraTemplate { Nome = id + "tir", Vertices = { (xS, yS), (xS + compPar, yS) }, Bitola = co.TirPhi, Aco = acoLong,
                    Quantidade = co.TirN * nPares, Escala = c.escC });
            if (nSoz > 0)
            {
                double xs2 = nPares > 0 ? xS : xS;
                double ys2 = nPares > 0 ? yS - 6 * hc : yS;
                var bt = new BarraTemplate { Nome = id + "tirs", Vertices = { (xs2, ys2), (xs2 + compSoz, ys2), (xs2 + compSoz, ys2 - lgTir) },
                    Bitola = co.TirPhi, Aco = acoLong, Quantidade = co.TirN * nSoz, Escala = c.escC };
                bt.Formatos.AddRange(new[] { FormatoSegmento.Linha, FormatoSegmento.Gancho90 });
                r.Barras.Add(bt);
                if (nPares > 0) yS -= 6 * hc;
            }
            xFim = Math.Max(xFim, xS + (duplo ? compPar : compSoz) + 3 * hc);
            // barra transversal soldada na ponta (uma por consolo), inclinada como na prancha
            double compTr = co.B - 2 * cc, q = compTr * Math.Sqrt(0.5);
            double xt = xS + 4 * hc, yt = yS - lgTir - 14 * hc;
            r.Barras.Add(new BarraTemplate
            {
                Nome = id + "tr", Vertices = { (xt, yt), (xt + q, yt + q) }, Bitola = co.TirPhi, Aco = acoLong, Quantidade = nConsolos, Escala = c.escC,
                PosicaoDescricao = (xt + q / 2 - 2.6 * hc, yt + q / 2 + 2.6 * hc), RotacaoDescricao = Math.PI / 4,
            });
            foreach (var (t, k) in c.Linhas("Barra de ancoragem soldada", "Welded anchor bar").Select((t, k) => (t, k))
                         .Concat(new[] { (c.Idioma == IdiomaDesenho.EN ? "SEE SPECIFIC DETAIL" : "VER DET. ESPECÍFICO", 9) }))
            {
                double off = 2.2 * hc + (k == 9 ? c.Linhas("a", "b").Length : k) * 1.7 * ht;
                c.R.Textos.Add(new TextoTemplate { Texto = t, X = xt + q / 2 + off * Math.Sqrt(0.5), Y = yt + q / 2 - off * Math.Sqrt(0.5),
                    Altura = ht, Alinhamento = 1, Angulo = Math.PI / 4, Layer = "ARM-TEXTO-FORMA" });
            }
            yMin = Math.Min(yMin, yt - 3 * hc);
            xFim = Math.Max(xFim, xt + q + 10 * hc);
        }
        else
        {
            double larg = Math.Max(4 * co.TirPhi / 10, (co.B - 2 * cc - co.TirPhi / 10) / Math.Max(1, co.TirN - 1));
            var bt = new BarraTemplate { Nome = id + "laco", Vertices = { (xS, yS), (xS + compSoz, yS), (xS + compSoz, yS + larg), (xS, yS + larg) },
                Bitola = co.TirPhi, Aco = acoLong, Quantidade = (co.TirN + 1) / 2 * nConsolos, Escala = c.escC };
            bt.Bulges.AddRange(new[] { 0.0, 1.0, 0.0, 0.0 });
            r.Barras.Add(bt);
            Rotulo("Tirante (laço)", xS + compSoz / 2, yS + larg + 3.6 * hc);
            xFim = Math.Max(xFim, xS + compSoz + 3 * hc);
        }
        if (co.InfN > 0)
        {
            double perna = co.H1 - 2 * cc, horiz = co.A - cc + co.InfAnc;
            double xi = xFim + 6 * hc, yi = yR1 - 6 * hc;
            r.Barras.Add(new BarraTemplate { Nome = id + "inf", Vertices = { (xi, yi), (xi, yi - perna), (xi + horiz, yi - perna) },
                Bitola = co.InfPhi, Aco = acoLong, Quantidade = co.InfN * nConsolos, Escala = c.escC });
            Rotulo("Arm. Inferior", xi + horiz / 2, yi - perna - 6.2 * hc);
            xFim = xi + horiz + 4 * hc;
            yMin = Math.Min(yMin, yi - perna - 8 * hc);
        }

        // =================== linha de baixo: CORTE AA, estribos, pino ===================
        double yR2 = yMin - 3 * hc;
        double xAA = x0 + 7 * hc, topAA = yR2 - 3 * hc;
        Rotulo(c.Idioma == IdiomaDesenho.EN ? "SECTION AA" : "CORTE AA", xAA + co.B / 2, yR2 + 1.5 * hc, hc);
        c.Retangulo(xAA, topAA - co.H, xAA + co.B, topAA);
        if (co.VertN > 0) c.Retangulo(xAA + cc, topAA - co.H + cc, xAA + co.B - cc, topAA - cc, "ARM-BARRA-DETALHE");
        var indicAA = new List<(string barra, List<(double X, double Y)> pts, bool esq)>();
        if (!co.Laco)
        {
            var pts = new List<(double X, double Y)>();
            for (int k = 0; k < co.TirN; k++)
            {
                double xk = xAA + cc + co.TirPhi / 20 + (co.TirN == 1 ? (co.B - 2 * cc - co.TirPhi / 10) / 2 : k * (co.B - 2 * cc - co.TirPhi / 10) / (co.TirN - 1));
                r.Circulos.Add(new CirculoTemplate { X = xk, Y = topAA - cc - co.TirPhi / 20, Raio = co.TirPhi / 20, Layer = "ARM-BARRA-DETALHE" });
                pts.Add((xk, topAA - cc - co.TirPhi / 20));
            }
            indicAA.Add((nPares > 0 ? id + "tir" : id + "tirs", pts, false));
        }
        if (co.CostN > 0)
        {
            double passo = 2.0 / 3 * d / co.CostN;
            var pts = new List<(double X, double Y)>();
            for (int k = 1; k <= co.CostN; k++)
            {
                double z = topAA - cc - co.TirPhi / 20 - k * passo;
                c.Linha(xAA + cc, z, xAA + co.B - cc, z, "ARM-BARRA-DETALHE");
                if (pts.Count == 0) pts.Add((xAA + co.B - cc, z)); // uma linha de chamada basta (as outras são iguais)
            }
            indicAA.Add((id + "cost", pts, false));
        }
        if (co.InfN > 0)
        {
            var pts = new List<(double X, double Y)>();
            for (int k = 0; k < co.InfN; k++)
            {
                double xk = xAA + cc + co.InfPhi / 20 + (co.InfN == 1 ? (co.B - 2 * cc - co.InfPhi / 10) / 2 : k * (co.B - 2 * cc - co.InfPhi / 10) / (co.InfN - 1));
                double yk = topAA - co.H + cc + co.InfPhi / 20;
                r.Circulos.Add(new CirculoTemplate { X = xk, Y = yk, Raio = co.InfPhi / 20, Layer = "ARM-BARRA-DETALHE" });
                pts.Add((xk, yk));
            }
            indicAA.Add((id + "inf", pts, true));
        }
        foreach (var lado in indicAA.GroupBy(i => i.esq))
        {
            double tx = lado.Key ? xAA - 4 * hc : xAA + co.B + 4 * hc;
            int k = 0;
            foreach (var it in lado)
            {
                // tirante: as linhas se encontram (patamar); pontos demais viram só os das pontas
                var pts = it.pts.Count > 3 ? new List<(double X, double Y)> { it.pts[0], it.pts[it.pts.Count / 2], it.pts[it.pts.Count - 1] } : it.pts;
                r.Faixas.Add(new FaixaTemplate { Barra = it.barra, Tipo = TipoFaixa.Indicacao, Referencias = pts,
                    Posicao = (tx, (lado.Key ? topAA - co.H - 1.5 * hc : topAA + 1.5 * hc) - k * 2.5 * hc), Escala = c.escC, Patamar = true });
                k++;
            }
        }
        c.Cota(xAA, topAA, xAA + co.B, topAA, xAA + co.B / 2, topAA + 2 * hc, 0, c.escC);
        c.Cota(xAA, topAA - co.H, xAA, topAA, xAA - 2.5 * hc, topAA - co.H / 2, Math.PI / 2, c.escC);
        double yBaixo = topAA - co.H - 4 * hc, xCur = xAA + co.B + 14 * hc;

        if (co.VertN > 0)
        {
            double wv = co.B - 2 * cc, hvert = co.H - 2 * cc;
            r.Barras.Add(Estribo(cfg, id + "vert", xCur, topAA - hvert, wv, hvert, co.VertPhi, acoEst, c.escC));
            r.Barras[r.Barras.Count - 1].Quantidade = co.VertN * nConsolos;
            Rotulo("Estribo vertical", xCur + wv / 2, topAA - hvert - 4.5 * hc);
            xCur += wv + 14 * hc;
            yBaixo = Math.Min(yBaixo, topAA - hvert - 6 * hc);
        }
        if (co.CostN > 0)
        {
            double w = (duplo ? compPar : compSoz) + 2 * co.CostPhi / 10, prof = co.B - 2 * cc;
            r.Barras.Add(Estribo(cfg, id + "cost", xCur, topAA - prof, w, prof, co.CostPhi, acoEst, c.escC));
            r.Barras[r.Barras.Count - 1].Quantidade = co.CostN * Math.Max(1, nPares + nSoz);
            Rotulo("Estribo horizontal", xCur + w / 2, topAA - prof - 4.5 * hc);
            xCur += w + 14 * hc;
            yBaixo = Math.Min(yBaixo, topAA - prof - 6 * hc);
        }
        if (co.PinoN > 0)
        {
            double xp = xCur + 2 * hc, yp = topAA;
            var bt = new BarraTemplate { Nome = id + "pino", Vertices = { (xp, yp), (xp, yp - co.PinoAcima - co.PinoEmb), (xp + co.PinoPe, yp - co.PinoAcima - co.PinoEmb) },
                Bitola = co.PinoPhi, Aco = acoLong, Quantidade = co.PinoN * nConsolos, Escala = c.escC };
            if (co.PinoPe <= 0) bt.Vertices.RemoveAt(2);
            r.Barras.Add(bt);
            // topo do consolo atravessando o pino (como na prancha)
            c.Linha(xp - 5 * hc, yp - co.PinoAcima, xp + Math.Max(co.PinoPe, 5) + 5 * hc, yp - co.PinoAcima, "ARM-FORMA");
            Rotulo(co.PinoN * nConsolos > 1 ? "PINOS" : "PINO", xp + Math.Max(co.PinoPe, 5) / 2, yp - co.PinoAcima - co.PinoEmb - 4.5 * hc);
            xCur = xp + Math.Max(co.PinoPe, 5) + 10 * hc;
            yBaixo = Math.Min(yBaixo, yp - co.PinoAcima - co.PinoEmb - 6 * hc);
        }

        // ---- moldura ----
        double xMax = Math.Max(Math.Max(xCur, xFim), MaxX(r, marca)) + 2 * hc;
        c.Retangulo(x0, yBaixo, xMax, yTopo, "ARM-DETALHE");
        VerificarConsolo(r, co, cc);
        return yBaixo - 8 * hc;
    }

    /// <summary>Linha de interrupção (zigue-zague no meio) atravessando o pilar no corte do consolo.</summary>
    private static void Interrupcao(Ctx c, double xa, double xb, double y, double hc)
    {
        double xm = (xa + xb) / 2, a = 0.6 * hc;
        c.R.Contornos.Add(new ContornoTemplate
        {
            Vertices = { (xa, y), (xm - a, y), (xm - a / 2, y + a), (xm + a / 2, y - a), (xm + a, y), (xb, y) }, Layer = "ARM-FORMA",
        });
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
