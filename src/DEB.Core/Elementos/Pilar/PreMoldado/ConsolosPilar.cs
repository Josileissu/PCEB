using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Motor;
using DEB.Core.Documentacao;
using DEB.Core.Elementos.Consolo;

namespace DEB.Core.Elementos.Pilar.PreMoldado;

/// <summary>
/// Etapa 3 do pilar (2026-10-29): consolos — o que é do pilar: a lista de consolos (parâmetro, leitura, faces, pares em
/// nível), onde cada consolo aparece nas 4 vistas e as cotas acumuladas delas, os cortes de forma por grupo (B-B, C-C) com a
/// seção do pilar, e o volume e o CG. O desenho do próprio consolo (forma de frente e de lado, planta no corte, detalhe
/// "ARMADURAS CONSOLO b x h x a" e os avisos da NBR 9062, 7.3) está em <see cref="DesenhoConsolo"/> (Elementos/Consolo, D27).
/// </summary>
public sealed partial class TemplatePilarPreMoldado
{
    public const string AncSoldada = "Barra transversal soldada", AncLaco = "Laço (alça horizontal)";
    private static readonly string[] LetrasCorte = { "B", "C", "D", "E", "F", "G", "H", "J", "K", "L", "M", "N", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z" };

    /// <summary>Consolos com topos a menos disto (cm) aparecem no mesmo corte (pedido do usuário, 2026-11-11).</summary>
    public const double DistanciaCorteComum = 50;

    /// <summary>
    /// Cortes de forma dos consolos (pedido do usuário, 2026-11-11, pela prancha de pilar do escritório): de cima para baixo, os consolos com
    /// topos a menos de <see cref="DistanciaCorteComum"/> do primeiro do grupo ficam num corte só; e um grupo igual a um já
    /// apresentado (mesmas faces, tipos e deslocamentos) usa a letra dele, sem desenhar de novo ("evita muita informação no desenho").
    /// </summary>
    public static void AgruparCortes(List<ConsoloPilar> consolos)
    {
        var ordem = consolos.OrderByDescending(co => co.ZTopo).ThenBy(co => co.Slot).ToList();
        var letras = new Dictionary<string, string>();
        int grupo = 0;
        for (int i = 0; i < ordem.Count;)
        {
            double z0 = ordem[i].ZTopo;
            var g = new List<ConsoloPilar>();
            while (i < ordem.Count && z0 - ordem[i].ZTopo < DistanciaCorteComum - 1e-6) g.Add(ordem[i++]);
            string ass = string.Join(";", g.Select(co => FormattableString.Invariant($"{string.Join(",", co.Faces)}|{co.Tipo}|{Math.Round(co.Desl, 1)}"))
                                           .OrderBy(s => s, StringComparer.Ordinal));
            bool novo = !letras.TryGetValue(ass, out var letra);
            if (novo) { letra = LetrasCorte[letras.Count % LetrasCorte.Length]; letras[ass] = letra; }
            grupo++;
            foreach (var co in g) { co.Letra = letra; co.GrupoCorte = grupo; co.CorteDesenhado = novo; }
        }
    }

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
            Descricao = "Clique em … para incluir os consolos: tipo (catálogo do ARMCONSOLOS), face (1 = oeste/−x, 2 = sul/−y, 3 = leste/+x, 4 = norte/+y; " +
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
                .Where(f => f >= 1 && f <= 4).Select(FaceExibida).Distinct().OrderBy(f => f).ToList();   // digitada (apresentada) → interna
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
                InfN = t.InfN, InfPhi = t.InfPhi, InfAnc = t.InfAnc, InfRetorno = t.InfRetorno, InfDescida = t.InfDescida, TirPerna = t.TirPerna,
                PinoN = t.PinoN, PinoPhi = t.PinoPhi, PinoAcima = t.PinoAcima, PinoEmb = t.PinoEmb, PinoPe = t.PinoPe,
                AlmA = t.AlmA, AlmB = t.AlmB, AlmE = t.AlmE,
            };
            if (c.ZTopo - c.H < 0 || c.ZTopo > L) { avisos.Add($"Consolo {k}: fora do pilar (topo a {Num(c.ZTopo, "0.#")} cm da base)."); continue; }
            r.Add(c);
        }
        JuntarEmNivel(r, v.Numero("B"), v.Numero("H"));
        AgruparCortes(r);
        return r;
    }

    /// <summary>
    /// Consolos opostos em nível viram duplo (D30: "se tornam um tipo, com tirante igual, desde que em nível"), pela mesma
    /// função do modelo 3D do Revit (<see cref="BarrasConsolo.ParesEmNivel"/>): mesmo tipo (mesma armadura), faces opostas,
    /// mesmo topo e mesma posição ao longo da face dentro de 0,6 cm. Na mesma linha (tipo simples em "1,3"), a linha passa a
    /// ser duplo; em linhas diferentes, a face da segunda vai para a primeira quando o deslocamento da primeira a põe no
    /// mesmo lugar. Os tipos duplos do catálogo continuam como antes.
    /// </summary>
    private static void JuntarEmNivel(List<ConsoloPilar> r, double b, double h)
    {
        // centro ao longo da face, no eixo comum às faces opostas (mesma convenção de RetanguloPlanta)
        double Centro(ConsoloPilar co, int f) => f == 1 ? h / 2 + co.Desl : f == 3 ? h / 2 - co.Desl : f == 2 ? b / 2 + co.Desl : b / 2 - co.Desl;
        var itens = new List<(ConsoloPilar Co, int Face)>();
        foreach (var co in r.Where(co => !co.TipoDuplo))
            foreach (int f in co.Faces) itens.Add((co, f));
        var nivel = itens.Select(t => new ConsoloNoNivel(t.Face, t.Co.ZTopo, t.Co.H, Centro(t.Co, t.Face), t.Co.B, t.Co.A, t.Co.H1, t.Co.Tipo)).ToList();
        foreach (var (i, j) in BarrasConsolo.ParesEmNivel(nivel))
        {
            var (ci, _) = itens[i];
            var (cj, fj) = itens[j];
            if (ci == cj) { ci.EmNivel = true; continue; }
            if (Math.Abs(Centro(ci, fj) - nivel[j].Centro) > BarrasConsolo.ToleranciaNivel) continue;
            cj.Faces.Remove(fj);
            ci.Faces.Add(fj);
            ci.Faces.Sort();
            ci.EmNivel = true;
        }
        r.RemoveAll(co => co.Faces.Count == 0);
    }

    /// <summary>Distância mínima a2 do aparelho de apoio à ponta do consolo (NBR 9062, 7.3.3.4): <see cref="DesenhoConsolo.A2Minimo"/>.</summary>
    public static double A2Minimo(double cob, double phiTirante, bool laco) => DesenhoConsolo.A2Minimo(cob, phiTirante, laco);

    /// <summary>Volume total (m³): pilar com chanfros + consolos.</summary>
    public static double VolumeTotal(double b, double h, double l, double chanfro, IEnumerable<ConsoloPilar> consolos) =>
        Volume(b, h, l, chanfro) + consolos.Sum(c => c.Volume);

    /// <summary>Altura do CG (cm, a partir da base): pilar no meio do comprimento, consolos no seu centro.</summary>
    public static double AlturaCG(double b, double h, double l, double chanfro, IEnumerable<ConsoloPilar> consolos) =>
        AlturaCG(Volume(b, h, l, chanfro), l, consolos.Select(c => (c.Volume, c.ZTopo - c.H / 2)));

    /// <summary>
    /// Altura do CG ao longo do eixo (unidade de <paramref name="l"/>): o pilar (volume <paramref name="volPilar"/>) no meio do
    /// comprimento e cada consolo (volume, altura do centro) somado. Regra única do CAD e do modelo 3D do Revit (alças, D30).
    /// </summary>
    public static double AlturaCG(double volPilar, double l, IEnumerable<(double Volume, double Z)> consolos)
    {
        double m = volPilar * l / 2, vt = volPilar;
        foreach (var (vol, z) in consolos) { m += vol * z; vt += vol; }
        return vt > 0 ? m / vt : l / 2;
    }

    // ======================== vistas ========================

    /// <summary>Face à esquerda e à direita de quem olha a face n (VISTA n).</summary>
    private static int FaceEsquerda(int vista) => vista % 4 + 1;
    private static int FaceDireita(int vista) => (vista + 2) % 4 + 1;

    private void DesenharConsolosNasVistas(Ctx c, List<ConsoloPilar> consolos, double[] xs, double[] larguras, double[] cob)
    {
        double hv = c.hV;
        foreach (var co in consolos)
        {
            double zTop = co.ZTopo;
            foreach (int f in co.Faces)
            {
                double cf = cob[f - 1];
                for (int vista = 1; vista <= 4; vista++)
                {
                    double x0 = xs[vista - 1], x1 = x0 + larguras[vista - 1];
                    if (vista == f)
                        DesenhoConsolo.VistaDeFrente(c, co, (x0 + x1) / 2 + co.Desl, zTop);
                    else if (FaceEsquerda(vista) == f || FaceDireita(vista) == f)
                    {
                        bool esq = FaceEsquerda(vista) == f;
                        DesenhoConsolo.VistaDeLado(c, co, esq ? x0 : x1, esq ? -1 : 1, zTop, cf);
                    }
                }
            }
        }
    }

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

    private double CortesConsolo(Ctx c, List<ConsoloPilar> consolos, double b, double h, double xD, double yCur, double[] cob)
    {
        double hc = c.hC;
        // um corte por grupo (consolos próximos juntos); os grupos que repetem um corte já apresentado não são desenhados
        foreach (var grupo in consolos.Where(co => co.CorteDesenhado).GroupBy(co => co.GrupoCorte).OrderBy(g => g.Key))
        {
            var lista = grupo.ToList();
            string letra = lista[0].Letra;
            foreach (var t in c.Linhas($"CORTE {letra}-{letra}", $"SECTION {letra}-{letra}")) { c.Texto(t, xD, yCur, 1.4 * hc); yCur -= 2.2 * hc; }
            c.Texto($"ESC 1:{c.escC:0}", xD, yCur, 0.8 * hc);
            // balanço máximo em cada face (0 = sem consolo nela) e a folga para as cotas encadeadas (até ~5 textos além da ponta)
            double extra = 5.5 * hc;
            double Ext(int f) { double a = lista.Where(co => co.Faces.Contains(f)).Select(co => co.A).DefaultIfEmpty(0).Max(); return a > 0 ? a + extra : 0; }
            double ox = xD + 8 * hc + Ext(3);
            double topo = yCur - 7 * hc - Ext(4);
            double oy = topo - h;
            c.Retangulo(ox, oy, ox + b, oy + h, "ARM-PILAR-SECAO"); // pilar cortado (magenta no P56)
            foreach (var co in lista)
            foreach (int f in co.Faces)
                DesenhoConsolo.PlantaNoCorte(c, co, f, b, h, ox, oy, cob[f - 1]);
            // pilar: b em cima e h à direita (além dos consolos dessas faces)
            double yb = oy + h + Ext(4) + 2.5 * hc, xh = ox + b + Ext(1) + 2.5 * hc;
            c.Cota(ox, oy + h, ox + b, oy + h, ox + b / 2, yb, 0, c.escC);
            c.Cota(ox + b, oy, ox + b, oy + h, xh, oy + h / 2, Math.PI / 2, c.escC);
            double baixo = oy - Ext(2);
            RotuloVista(c, 4, ox + b / 2, yb + 1.5 * hc);
            RotuloVista(c, 2, ox + b / 2, baixo - 1.5 * hc);
            RotuloVista(c, 3, ox - Ext(3) - 1.5 * hc, oy + h / 2);
            RotuloVista(c, 1, xh + 2 * hc, oy + h / 2);
            yCur = baixo - 12 * hc;
        }
        return yCur;
    }
}
