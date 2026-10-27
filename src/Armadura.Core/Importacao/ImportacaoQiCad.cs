using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Armadura.Core.Modelo;

namespace Armadura.Core.Importacao;

/// <summary>Ponto em cm (coordenadas do nosso desenho, escala real).</summary>
public struct PontoImp
{
    public double X, Y;
    public PontoImp(double x, double y) { X = x; Y = y; }
}

public abstract class ItemImportado
{
    /// <summary>Detalhamento (bloco) a que pertence; "" = solto.</summary>
    public string Detalhamento { get; set; } = "";
    public string Layer { get; set; } = "";
    /// <summary>Número do objeto no .cad (para mensagens e vínculos).</summary>
    public int Origem { get; set; }
}

public sealed class BarraImportada : ItemImportado
{
    public List<PontoImp> Pontos { get; } = new List<PontoImp>();
    public List<double> Bulges { get; } = new List<double>();
    public bool Fechada { get; set; }
    public DadosArmadura Dados { get; } = new DadosArmadura();
    /// <summary>Posição do texto da descrição no .cad (null = posição padrão do plugin).</summary>
    public PontoImp? Descricao { get; set; }
    public bool Trelica { get; set; }
}

public sealed class RebatidaImportada : ItemImportado
{
    public int Barra { get; set; }
    public List<PontoImp> Pontos { get; } = new List<PontoImp>();
}

public sealed class FaixaImportada : ItemImportado
{
    public int Barra { get; set; }
    public DadosFaixa Dados { get; } = new DadosFaixa();
    /// <summary>Posição da cota (faixas) ou do texto (indicação).</summary>
    public PontoImp Posicao { get; set; }
}

public sealed class LinhaImportada : ItemImportado
{
    public List<PontoImp> Pontos { get; } = new List<PontoImp>();
    public List<double> Bulges { get; } = new List<double>();
    public bool Fechada { get; set; }
}

public sealed class CirculoImportado : ItemImportado
{
    public PontoImp Centro { get; set; }
    public double Raio { get; set; }
    /// <summary>Arco: ângulos em rad (null = círculo completo).</summary>
    public double? AnguloIni { get; set; }
    public double? AnguloFim { get; set; }
}

public sealed class TextoImportado : ItemImportado
{
    public string Texto { get; set; } = "";
    public PontoImp Posicao { get; set; }
    public double Altura { get; set; }
    public double Angulo { get; set; }
    /// <summary>0 esquerda, 1 centro, 2 direita.</summary>
    public int AlinhamentoH { get; set; }
    /// <summary>0 base, 1 meio, 2 topo (como no .cad).</summary>
    public int AlinhamentoV { get; set; }
}

public sealed class CotaImportada : ItemImportado
{
    public PontoImp P1 { get; set; }
    public PontoImp P2 { get; set; }
    public PontoImp Posicao { get; set; }
    public double Angulo { get; set; }
    public string Texto { get; set; } = "";
    public double Escala { get; set; }
}

public sealed class DetalhamentoImportado
{
    public string Nome { get; set; } = "";
    public int Repeticoes { get; set; } = 1;
    public double Volume { get; set; }
    public double AreaForma { get; set; }
    public string Classe { get; set; } = "";
    public double Escala { get; set; }
}

public sealed class TabelaImportada
{
    public PontoImp Posicao { get; set; }
    public List<string> Detalhamentos { get; } = new List<string>();
    public double Escala { get; set; }
}

public sealed class ResultadoImportacao
{
    public List<BarraImportada> Barras { get; } = new List<BarraImportada>();
    public List<RebatidaImportada> Rebatidas { get; } = new List<RebatidaImportada>();
    public List<FaixaImportada> Faixas { get; } = new List<FaixaImportada>();
    public List<LinhaImportada> Linhas { get; } = new List<LinhaImportada>();
    public List<CirculoImportado> Circulos { get; } = new List<CirculoImportado>();
    public List<TextoImportado> Textos { get; } = new List<TextoImportado>();
    public List<CotaImportada> Cotas { get; } = new List<CotaImportada>();
    public List<DetalhamentoImportado> Detalhamentos { get; } = new List<DetalhamentoImportado>();
    public List<TabelaImportada> Tabelas { get; } = new List<TabelaImportada>();
    public List<string> Avisos { get; } = new List<string>();
    /// <summary>Os N recomeçam em cada detalhamento (o desenho deve usar numeração Individual).</summary>
    public bool NumeracaoPorDetalhamento { get; set; }
    /// <summary>Escala predominante (1:n) do arquivo.</summary>
    public double EscalaPredominante { get; set; }
}

/// <summary>
/// Converte o .cad lido (<see cref="LeitorQiCad"/>) no nosso modelo. Coordenadas do .cad são de PAPEL:
/// medida real (cm) = coordenada × denominador da escala. Cada detalhamento usa a escala das suas barras,
/// em torno do seu canto inferior esquerdo, que fica na posição da prancha × escala predominante.
/// </summary>
public static class ConversorQiCad
{
    /// <summary>Códigos de bitola confirmados nos exemplos do usuário (2026-10-27): código → mm.</summary>
    public static readonly IReadOnlyDictionary<int, double> BitolasConhecidas = new Dictionary<int, double>
    {
        [6] = 5.0, [8] = 6.3, [10] = 8.0, [12] = 10.0, [13] = 12.5, [14] = 16.0, [16] = 20.0, [18] = 25.0,
    };

    /// <summary>Tipo de segmento do .cad → formato (0 reto, 1 gancho 90°, 2 passagem do canto, 3 gancho 45°).</summary>
    public static (FormatoSegmento formato, bool sobreposto, bool conhecido) Segmento(int tipo) => tipo switch
    {
        0 => (FormatoSegmento.Linha, false, true),
        1 => (FormatoSegmento.Gancho90, false, true),
        2 => (FormatoSegmento.Linha, true, true),
        3 => (FormatoSegmento.Gancho45, false, true),
        _ => (FormatoSegmento.Linha, false, false),
    };

    private static readonly HashSet<string> TextosDeArmadura = new HashSet<string> { "TRebarText", "TRebarBandText", "TDimensionText" };

    public static ResultadoImportacao Converter(QiDocumento doc)
    {
        var r = new ResultadoImportacao();
        var raiz = doc.Raiz ?? throw new InvalidOperationException("O .cad não tem o desenho (TDrawing).");
        var bitolas = CalibrarBitolas(doc, r);
        var acos = doc.DaClasse("TDiameterConfig").GroupBy(o => o.Int("diameter")).ToDictionary(g => g.Key, g => g.First().Int("steelType"));

        // escala predominante e escala de cada detalhamento (pelas barras)
        double EscalaBarra(QiObjeto b) => b.Obj("scale")?.Dbl("denominador") is double e && e > 0 ? e : 0;
        var escalas = doc.DaClasse("TCadRebar").Concat(doc.DaClasse("TCadTruss")).Select(EscalaBarra).Where(e => e > 0).ToList();
        double f = escalas.Count > 0 ? escalas.GroupBy(e => e).OrderByDescending(g => g.Count()).First().Key : 50;
        r.EscalaPredominante = f;

        // quem pertence a cada detalhamento
        var dono = new Dictionary<int, QiObjeto>();
        var dets = raiz.Lista("elements").Where(o => o.E("TCadSimpleDetail")).ToList();
        foreach (var det in dets)
            foreach (var el in Expandir(det.Lista("elements")))
                if (!dono.ContainsKey(el.Numero)) dono[el.Numero] = det;

        // transformação de cada grupo: (canto de referência no papel, escala)
        var transf = new Dictionary<int, (double rx, double ry, double esc)>();
        var nomes = new Dictionary<int, string>();
        foreach (var det in dets)
        {
            var els = Expandir(det.Lista("elements")).ToList();
            var esc = els.Where(e => e.E("TCadRebar")).Select(EscalaBarra).Where(e => e > 0).GroupBy(e => e).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            if (esc <= 0) esc = f;
            var pts = els.SelectMany(PontosBrutos).ToList();
            double rx = pts.Count > 0 ? pts.Min(p => p.X) : 0, ry = pts.Count > 0 ? pts.Min(p => p.Y) : 0;
            transf[det.Numero] = (rx, ry, esc);
            string nome = string.IsNullOrWhiteSpace(det.Str("detailName")) ? "DETALHE " + det.Numero : det.Str("detailName").Trim();
            while (nomes.ContainsValue(nome)) nome += "_";
            nomes[det.Numero] = nome;
            r.Detalhamentos.Add(new DetalhamentoImportado
            {
                Nome = nome, Repeticoes = Math.Max(1, det.Int("repetitions")), Volume = det.Dbl("concreteVolume"),
                AreaForma = det.Dbl("mouldArea"), Classe = det.Str("concreteClass"), Escala = esc,
            });
        }

        PontoImp T(QiObjeto el, double x, double y)
        {
            if (dono.TryGetValue(el.Numero, out var det) && transf.TryGetValue(det.Numero, out var t))
                return new PontoImp((x - t.rx) * t.esc + t.rx * f, (y - t.ry) * t.esc + t.ry * f);
            return new PontoImp(x * f, y * f);
        }
        double Fator(QiObjeto el) => dono.TryGetValue(el.Numero, out var det) ? transf[det.Numero].esc : f;
        PontoImp P(QiObjeto el, QiObjeto ponto) => ponto == null ? new PontoImp(0, 0) : T(el, ponto.Dbl("x"), ponto.Dbl("y"));
        string Det(QiObjeto el) => dono.TryGetValue(el.Numero, out var det) ? nomes[det.Numero] : "";
        string Layer(QiObjeto el) => el.Obj("layer")?.Str("name") ?? "";
        void Base(ItemImportado it, QiObjeto el) { it.Detalhamento = Det(el); it.Layer = Layer(el); it.Origem = el.Numero; }

        var todos = Expandir(raiz.Lista("elements").Where(o => !o.E("TCadSimpleDetail")))
            .Concat(dets.SelectMany(d => Expandir(d.Lista("elements")))).ToList();
        int hachuras = 0, trelicas = 0, segDesconhecidos = 0, cortes = 0, outros = 0;
        var tiposIgnorados = new SortedSet<string>();
        foreach (var el in todos)
        {
            switch (el.Classe)
            {
                case "TCadRebar":
                case "TCadTruss":
                {
                    if (el.Classe == "TCadTruss") trelicas++;
                    var b = new BarraImportada { Trelica = el.Classe == "TCadTruss" };
                    Base(b, el);
                    Poligono(el, (x, y) => T(el, x, y), b.Pontos, b.Bulges, out bool fechada);
                    b.Fechada = fechada;
                    var rb = el.Obj("rebar");
                    var d = b.Dados;
                    if (rb != null)
                    {
                        int cod = rb.Int("diameter");
                        d.Bitola = bitolas.TryGetValue(cod, out var mm) ? mm : 0;
                        d.Aco = (acos.TryGetValue(cod, out var tipoAco) ? tipoAco : (cod < 8 ? 6 : 4)) switch { 6 => "CA-60", 2 => "CA-25", _ => "CA-50" };
                        d.Numero = rb.Int("id");
                        d.Quantidade = Math.Max(1, rb.Int("quantity"));
                        d.Espacamento = rb.Dbl("spacing");
                        d.Repeticoes = Math.Max(1, rb.Int("repetitions"));
                        d.Corrido = rb.Bool("continuous");
                        d.Estribo = rb.Bool("stirrup");
                        var niveis = rb.Obj("levels")?.Lista("levels").Select(l => l.Int("quantity")).Where(q => q > 0).ToList();
                        if (niveis != null && niveis.Count > 1) d.Camadas = string.Join("+", niveis);
                    }
                    d.Escala = EscalaBarra(el) > 0 ? EscalaBarra(el) : Fator(el);
                    var textos = Filhos(el).Where(x => x.Classe == "TRebarText").ToList();
                    var cotados = new HashSet<int>(textos.Select(x => x.Int("segmentNumber")).Where(n => n > 0));
                    var segs = el.Lista("segments").OrderBy(s => s.Int("segmentNumber")).ToList();
                    int nSeg = b.Fechada ? b.Pontos.Count : b.Pontos.Count - 1;
                    for (int i = 0; i < nSeg; i++)
                    {
                        var s = segs.FirstOrDefault(x => x.Int("segmentNumber") == i + 1);
                        var (formato, sob, conhecido) = Segmento(s?.Int("segmentType") ?? 0);
                        if (!conhecido) segDesconhecidos++;
                        d.Segmentos.Add(new Segmento
                        {
                            Formato = formato, Sobreposto = sob, Var = s?.Bool("variable") ?? false,
                            Cotar = !Modelo.Segmento.EhGancho(formato) && !sob && cotados.Contains(i + 1),
                        });
                    }
                    var desc = textos.FirstOrDefault(x => x.Int("segmentNumber") == 0);
                    if (desc?.Obj("position") is QiObjeto pd) b.Descricao = P(el, pd);
                    if (d.Bitola <= 0) r.Avisos.Add($"Barra N{d.Numero} ({Det(el)}): bitola desconhecida (código {rb?.Int("diameter")}); usada a bitola corrente.");
                    r.Barras.Add(b);
                    break;
                }
                case "TReflectedRebar":
                {
                    var reb = new RebatidaImportada { Barra = el.Obj("cadRebar")?.Numero ?? 0 };
                    Base(reb, el);
                    var bul = new List<double>();
                    Poligono(el, (x, y) => T(el, x, y), reb.Pontos, bul, out _);
                    r.Rebatidas.Add(reb);
                    break;
                }
                case "TRebarBand":
                case "TRebarIndication":
                {
                    var fx = new FaixaImportada { Barra = el.Obj("rebar")?.Numero ?? 0 };
                    Base(fx, el);
                    var fd = fx.Dados;
                    fd.Espacamento = el.Dbl("spacing") > 0 ? el.Dbl("spacing") : 15;
                    fd.EspacamentoInicial = el.Dbl("percInitialDistance") > 1 ? el.Dbl("percInitialDistance") / 100.0 : el.Dbl("percInitialDistance");
                    fd.Escala = Fator(el);
                    if (el.Classe == "TRebarIndication")
                    {
                        fd.Tipo = TipoFaixa.Indicacao;
                        // linha de chamada: 1º ponto encosta na barra, o último fica junto do texto. A posição
                        // gravada do texto às vezes é antiga (detalhamento copiado/movido no QiArmaduras):
                        // só vale se estiver perto da ponta da chamada.
                        var lideres = Filhos(el).Where(x => x.Classe == "TRebarLeader")
                            .Select(l => l.Lista("points").Select(p => P(el, p)).ToList()).Where(l => l.Count > 0).ToList();
                        if (lideres.Count == 0) { outros++; continue; }
                        foreach (var l in lideres) fd.Referencias.Add((l[0].X, l[0].Y));
                        var ancora = lideres[0][lideres[0].Count - 1];
                        var texto = Filhos(el).FirstOrDefault(x => x.Classe == "TRebarBandText");
                        var pt = texto?.Obj("position") is QiObjeto po ? P(el, po) : ancora;
                        fx.Posicao = Dist(pt, ancora) <= 3 * Fator(el) ? pt : ancora;
                    }
                    else
                    {
                        if (el.Bool("cutCallBand")) cortes++;
                        var p1 = P(el, el.Obj("pos1"));
                        var p2 = P(el, el.Obj("pos2"));
                        fd.P1X = p1.X; fd.P1Y = p1.Y; fd.P2X = p2.X; fd.P2Y = p2.Y;
                        double ang = el.Dbl("dimensionAngle");
                        bool ortogonal = Math.Abs(Math.Sin(2 * ang)) < 1e-6; // 0°, 90°, 180°, 270°
                        fd.Tipo = ortogonal ? TipoFaixa.Ortogonal : TipoFaixa.Alinhada;
                        if (el.Bool("fixedQuantity")) r.Avisos.Add($"Faixa da barra #{fx.Barra} ({Det(el)}) tinha quantidade fixa ({el.Int("quantity")}): recalculada pelo espaçamento.");
                        fx.Posicao = P(el, el.Obj("finalPos"));
                    }
                    r.Faixas.Add(fx);
                    break;
                }
                case "TCadPolygon":
                case "TCadLeader":
                {
                    var l = new LinhaImportada();
                    Base(l, el);
                    Poligono(el, (x, y) => T(el, x, y), l.Pontos, l.Bulges, out bool fechada);
                    l.Fechada = fechada;
                    if (l.Pontos.Count >= 2) r.Linhas.Add(l);
                    break;
                }
                case "TCadLine":
                {
                    var l = new LinhaImportada();
                    Base(l, el);
                    l.Pontos.Add(P(el, el.Obj("initialPos")));
                    l.Pontos.Add(P(el, el.Obj("finalPos")));
                    r.Linhas.Add(l);
                    break;
                }
                case "TCadCircle":
                case "TCadCircleArc":
                {
                    var c = new CirculoImportado { Centro = P(el, el.Obj("center")), Raio = el.Dbl("radiusX") * Fator(el) };
                    Base(c, el);
                    if (el.Classe == "TCadCircleArc") { c.AnguloIni = el.Dbl("initialAngle"); c.AnguloFim = el.Dbl("finalAngle"); }
                    if (c.Raio > 0) r.Circulos.Add(c);
                    break;
                }
                case "TCadText":
                case "TLeaderText":
                {
                    var t = new TextoImportado
                    {
                        Texto = el.Str("text"), Posicao = P(el, el.Obj("position")), Angulo = el.Dbl("angle"),
                        AlinhamentoH = el.Int("horizAlignment"), AlinhamentoV = el.Int("vertAlignment"),
                        Altura = (el.Obj("font")?.Dbl("height") ?? 0.2) * Fator(el),
                    };
                    Base(t, el);
                    if (!string.IsNullOrWhiteSpace(t.Texto) && t.Altura > 0) r.Textos.Add(t);
                    break;
                }
                case "TCadDimension":
                {
                    var texto = Filhos(el).FirstOrDefault(x => x.Classe == "TDimensionText");
                    var c = new CotaImportada
                    {
                        P1 = P(el, el.Obj("pos1")), P2 = P(el, el.Obj("pos2")), Posicao = P(el, el.Obj("finalPos")),
                        Angulo = el.Dbl("dimensionAngle"), Texto = el.Bool("useText") ? el.Str("text") : "", Escala = Fator(el),
                    };
                    Base(c, el);
                    r.Cotas.Add(c);
                    break;
                }
                case "TCadHatch": hachuras++; break;
                case "TCadRebarList":
                {
                    var t = new TabelaImportada { Posicao = P(el, el.Obj("position")), Escala = f };
                    foreach (var dref in el.Lista("details"))
                        if (nomes.TryGetValue(dref.Numero, out var nm)) t.Detalhamentos.Add(nm);
                    r.Tabelas.Add(t);
                    break;
                }
                default:
                    if (!TextosDeArmadura.Contains(el.Classe) && el.Classe != "TRebarLeader") tiposIgnorados.Add(el.Classe);
                    break;
            }
        }

        if (trelicas > 0) r.Avisos.Add($"{trelicas} treliça(s) importada(s) como barras comuns: escolha o modelo na paleta.");
        if (segDesconhecidos > 0) r.Avisos.Add($"{segDesconhecidos} segmento(s) de tipo desconhecido importado(s) como reto(s): confira os ganchos.");
        if (cortes > 0) r.Avisos.Add($"{cortes} faixa(s) com corte + chamada importada(s) como faixa simples.");
        if (hachuras > 0) r.Avisos.Add($"{hachuras} hachura(s) não importada(s).");
        if (outros > 0) r.Avisos.Add($"{outros} indicação(ões) sem linha de chamada ignorada(s).");
        if (tiposIgnorados.Count > 0) r.Avisos.Add("Elementos não importados: " + string.Join(", ", tiposIgnorados) + ".");

        // N recomeçando por detalhamento: o mesmo N em 2+ detalhamentos
        r.NumeracaoPorDetalhamento = r.Barras.Where(b => b.Detalhamento.Length > 0 && b.Dados.Numero > 0)
            .GroupBy(b => b.Dados.Numero).Any(g => g.Select(b => b.Detalhamento).Distinct().Count() > 1);
        return r;
    }

    /// <summary>Entra nos grupos comuns (TCadGroup e símbolos); a RA e os detalhamentos ficam como estão.</summary>
    private static IEnumerable<QiObjeto> Expandir(IEnumerable<QiObjeto> elementos)
    {
        foreach (var el in elementos)
        {
            if (el.E("TCadGroup") && !el.E("TCadSimpleDetail") && !el.E("TCadRebarList"))
            {
                foreach (var dentro in Expandir(el.Lista("elements"))) yield return dentro;
            }
            else yield return el;
        }
    }

    private static double Dist(PontoImp a, PontoImp b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>Elementos filhos (textos da barra, linhas de chamada…).</summary>
    private static IEnumerable<QiObjeto> Filhos(QiObjeto el) =>
        el.Obj("elementChildren")?.Lista("children").Select(c => c.Obj("element")).Where(x => x != null) ?? Enumerable.Empty<QiObjeto>();

    /// <summary>Pontos no papel (para o retângulo de cada detalhamento).</summary>
    private static IEnumerable<(double X, double Y)> PontosBrutos(QiObjeto el)
    {
        foreach (var p in el.Lista("points")) yield return (p.Dbl("x"), p.Dbl("y"));
        foreach (var c in new[] { "initialPos", "finalPos", "center", "position", "pos1", "pos2" })
            if (el.Obj(c) is QiObjeto p && p.E("Point3D")) yield return (p.Dbl("x"), p.Dbl("y"));
    }

    /// <summary>Vértices e bulges de um TCadPolygon (arcos pelo TPolygonArc do vértice inicial). [VALIDAR arcos]</summary>
    private static void Poligono(QiObjeto el, Func<double, double, PontoImp> t, List<PontoImp> pontos, List<double> bulges, out bool fechada)
    {
        fechada = el.Bool("closed");
        var pts = el.Lista("points").ToList();
        foreach (var p in pts)
        {
            pontos.Add(t(p.Dbl("x"), p.Dbl("y")));
            double bulge = 0;
            var arco = p.Obj("polygonArc");
            if (arco != null)
            {
                double ini = arco.Dbl("initialAngle"), fim = arco.Dbl("finalAngle");
                double varredura = fim - ini;
                while (varredura <= 0) varredura += 2 * Math.PI;
                while (varredura > 2 * Math.PI) varredura -= 2 * Math.PI;
                // o arco começa neste vértice? (ângulo do vértice em relação ao centro ≈ ângulo inicial)
                var c = arco.Obj("center");
                bool antiHorario = true;
                if (c != null)
                {
                    double a = Math.Atan2(p.Dbl("y") - c.Dbl("y"), p.Dbl("x") - c.Dbl("x"));
                    double dif = Math.Abs(Math.IEEERemainder(a - ini, 2 * Math.PI));
                    antiHorario = dif < 1e-3;
                }
                bulge = (antiHorario ? 1 : -1) * Math.Tan(varredura / 4);
            }
            bulges.Add(bulge);
        }
    }

    /// <summary>
    /// Código de bitola → mm: pelas descrições das próprias barras ("10 N1 ø5.0 C=547"); códigos sem
    /// descrição usam a tabela confirmada.
    /// </summary>
    private static Dictionary<int, double> CalibrarBitolas(QiDocumento doc, ResultadoImportacao r)
    {
        var mapa = new Dictionary<int, double>(BitolasConhecidas.ToDictionary(k => k.Key, k => k.Value));
        var rx = new Regex(@"[øØ∅]\s*(\d+(?:[.,]\d+)?)");
        foreach (var b in doc.DaClasse("TCadRebar"))
        {
            var rb = b.Obj("rebar");
            var desc = Filhos(b).FirstOrDefault(x => x.Classe == "TRebarText" && x.Int("segmentNumber") == 0);
            var m = desc == null ? null : rx.Match(desc.Str("text"));
            if (rb == null || m == null || !m.Success) continue;
            double mm = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            int cod = rb.Int("diameter");
            if (mapa.TryGetValue(cod, out var ja) && Math.Abs(ja - mm) > 1e-9 && BitolasConhecidas.ContainsKey(cod))
                r.Avisos.Add($"Código de bitola {cod}: o arquivo diz ø{mm}, a tabela conhecida diz ø{ja}; usado ø{mm}.");
            mapa[cod] = mm;
        }
        return mapa;
    }
}
