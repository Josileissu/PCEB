using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DEB.Core.Calculo;
using DEB.Core.Configuracao;
using DEB.Core.Estribos;
using DEB.Core.Modelo;
using DEB.Core.Templates;

namespace DEB.Core.Motor.Prancha;

/// <summary>Vista montada na folha (para conferência e testes): retângulo envolvente já com o título.</summary>
public sealed class VistaGerada
{
    public string Id { get; set; } = "";
    public string Titulo { get; set; } = "";
    public TipoVista Tipo { get; set; }
    public int Quadro { get; set; }
    public Retangulo Limites { get; set; } = new Retangulo();
}

/// <summary>
/// Gerador de prancha comum a todos os elementos (frente C, 2026-11-07): projeta o <see cref="ModeloArmadura"/> (3D) nas
/// vistas pedidas pelo perfil e monta a folha como o <c>TemplateBlocoEstacas</c> monta a do bloco (padrão BPM):
/// <list type="bullet">
/// <item>FormaPlanta: contornos dos corpos vistos de cima, cilindros como círculos (estacas com quadrantes hachurados),
/// cadeias de cotas (faces, estacas, corpos internos) e as marcas dos cortes da forma;</item>
/// <item>FormaCorte: corpos cortados no plano (contorno da união, sem linhas internas), lastro e regularização hachurados,
/// pilar interrompido, estacas, cotas verticais e os níveis por pilar (<see cref="ElementoEstrutural.Niveis"/> +
/// <see cref="ModeloArmadura.Niveis"/>; só os níveis com rótulo "nivel.&lt;nome&gt;");</item>
/// <item>Planta: projeção das posições filtradas; faixas de distribuição, indicações, "L" das barras VAR;</item>
/// <item>Corte: barras atravessadas = pontos com indicação; no plano = verdadeira grandeza (a mais próxima); estribos de
/// perfil = linhas;</item>
/// <item>Barras: formas dobradas (o CAD cota os segmentos).</item>
/// </list>
/// Cada posição vira UMA <see cref="BarraTemplate"/> (Nome = Chave): na vista de barras/"barras abaixo" que a listar, ou
/// no lugar, na primeira vista em que aparece em verdadeira grandeza; sem nenhuma, numa vista "BARRAS" automática. As
/// outras vistas desenham a representação e apontam para ela com faixa ou indicação. Nunca lança exceção (avisos).
/// </summary>
public sealed partial class GeradorPrancha : IGeradorPrancha
{
    private readonly ConfiguracaoArmadura _cfg;

    public GeradorPrancha(ConfiguracaoArmadura cfg = null) { _cfg = cfg ?? new ConfiguracaoArmadura(); }

    /// <summary>Vistas da última prancha gerada (posição na folha).</summary>
    public IReadOnlyList<VistaGerada> Vistas { get; private set; } = new List<VistaGerada>();

    public ResultadoTemplate Gerar(ElementoEstrutural elemento, ModeloArmadura modelo, PerfilDetalhamento perfil)
    {
        Vistas = new List<VistaGerada>();
        try
        {
            return GerarInterno(elemento, modelo, perfil);
        }
        catch (Exception ex)
        {
            var r = new ResultadoTemplate();
            r.Avisos.Add("Prancha: erro inesperado ao montar a folha (" + ex.GetType().Name + ": " + ex.Message + ").");
            return r;
        }
    }

    // ======================== contexto ========================

    private sealed class Info
    {
        public PosicaoArmadura P;
        public string Nome = "";
        public int Indice;
        public PlanoBarra Plano;
        public List<List<P3>> Inst = new List<List<P3>>();
        public BarraTemplate Barra;
        /// <summary>Vista onde a barra é desenhada; Dobrada = forma dobrada (vista de barras ou "barras abaixo").</summary>
        public string Casa = "";
        public bool Dobrada;
        public bool Var => P.ComprimentosVar != null && P.ComprimentosVar.Count > 0;
    }

    private sealed class Ctx
    {
        public ElementoEstrutural E;
        public ModeloArmadura M;
        public PerfilDetalhamento P;
        public ConfiguracaoArmadura Cfg;
        public ResultadoTemplate R;
        public double esc, ht;
        public IdiomaDesenho Idioma;
        public string Tipo = "";
        public FormatadorCampos Fmt;
        public Dictionary<string, string> Campos;
        public List<Info> Pos = new List<Info>();
        public HashSet<string> AvisosDados = new HashSet<string>();
        public CorpoConcreto Principal;
        public int Repeticoes;

        public void Aviso(string s) { if (AvisosDados.Add(s)) R.Avisos.Add(s); }

        public string Rot(string chave, string seFaltar = "") => VocabularioPrancha.Rotulo(P, Tipo, chave, seFaltar);

        /// <summary>Linhas de um texto "PT|EN" no idioma da folha (PT/EN: os dois, o inglês entre parênteses).</summary>
        public string[] L(string texto, IDictionary<string, string> extras = null)
        {
            var s = Fmt.Formatar(texto ?? "", extras);
            int k = s.IndexOf('|');
            string pt = k < 0 ? s : s.Substring(0, k).Trim(), en = k < 0 ? pt : s.Substring(k + 1).Trim();
            if (pt.Length == 0 && en.Length == 0) return new string[0];
            switch (Idioma)
            {
                case IdiomaDesenho.PT: return new[] { pt.Length > 0 ? pt : en };
                case IdiomaDesenho.EN: return new[] { en.Length > 0 ? en : pt };
                default:
                    if (k < 0 || en == pt || en.Length == 0) return new[] { pt };
                    return new[] { pt, en.StartsWith("(") ? en : "(" + en + ")" };
            }
        }

        /// <summary>Texto de uma célula de tabela no idioma (PT/EN em duas linhas).</summary>
        public string Celula(string texto, IDictionary<string, string> extras = null) => string.Join(TabelaTemplate.Quebra, L(texto, extras));
    }

    private static string Num(double x) => Calculo.CalculoComprimento.NumeroCompacto(x);
    private static string NumV(double x) => VocabularioPrancha.Num(x);

    private static bool Externo(CorpoConcreto cp) => VocabularioPrancha.PapeisExternos.Contains(cp.Papel ?? "");
    private static bool Papel(CorpoConcreto cp, string p) => string.Equals(cp.Papel, p, StringComparison.OrdinalIgnoreCase);
    private static bool Circular(CorpoConcreto cp) => (cp.Planta == null || cp.Planta.Count < 3) && cp.Raio > 0;

    private static double AreaPlanta(CorpoConcreto cp) => Circular(cp) ? Math.PI * cp.Raio * cp.Raio : cp.Planta?.Count >= 3 ? Geometria2D.Area(cp.Planta) : 0;
    private static double PerimetroPlanta(CorpoConcreto cp) => Circular(cp) ? 2 * Math.PI * cp.Raio : cp.Planta?.Count >= 3 ? Geometria2D.Perimetro(cp.Planta) : 0;

    private static Retangulo CaixaPlanta(CorpoConcreto cp)
    {
        var r = new Retangulo();
        if (Circular(cp)) { r.Incluir(cp.Centro.X - cp.Raio, cp.Centro.Y - cp.Raio); r.Incluir(cp.Centro.X + cp.Raio, cp.Centro.Y + cp.Raio); }
        else if (cp.Planta != null) foreach (var (x, y) in cp.Planta) r.Incluir(x, y);
        return r;
    }

    // ======================== geração ========================

    private ResultadoTemplate GerarInterno(ElementoEstrutural e, ModeloArmadura m, PerfilDetalhamento p)
    {
        var c = new Ctx { E = e, M = m, P = p ?? new PerfilDetalhamento(), Cfg = _cfg, R = new ResultadoTemplate() };
        var folha = c.P.Folha ?? new FolhaPerfil();
        c.esc = folha.Escala > 0 ? folha.Escala : 20;
        c.ht = (_cfg.AlturaTextoMm > 0 ? _cfg.AlturaTextoMm : 2) / 10.0 * c.esc;
        c.Idioma = (folha.Idioma ?? "PT").Trim().ToUpperInvariant() switch { "EN" => IdiomaDesenho.EN, "PT/EN" => IdiomaDesenho.PTEN, "PTEN" => IdiomaDesenho.PTEN, _ => IdiomaDesenho.PT };
        c.Tipo = e?.Tipo ?? m?.TipoElemento ?? c.P.TipoElemento ?? "";
        c.R.Escala = c.esc;
        if (e == null) c.Aviso("Prancha: elemento não informado (título e níveis sem dados).");
        if (m == null) { c.Aviso("Prancha: modelo de armadura não informado; folha vazia."); m = c.M = new ModeloArmadura(); }
        if (p == null) c.Aviso("Prancha: perfil não informado; folha sem vistas.");
        c.Repeticoes = e == null ? 1 : e.Repeticoes > 0 ? e.Repeticoes : Math.Max(1, e.Pilares?.Count ?? 0);
        c.Campos = MontarCampos(c);
        c.Fmt = new FormatadorCampos(c.Campos, nome => c.Aviso($"Prancha: campo {{{nome}}} desconhecido nos textos do perfil."));
        c.Principal = m.Corpos.Where(cp => !cp.Vazio && !Externo(cp)).OrderByDescending(AreaPlanta).FirstOrDefault();

        PrepararPosicoes(c);
        var vistas = (folha.Vistas ?? new List<VistaPerfil>()).Where(v => v != null)
            .OrderBy(v => v.Quadro).ThenBy(v => v.Linha).ThenBy(v => v.Ordem).ToList();
        if (vistas.Count == 0) c.Aviso("Prancha: o perfil não tem vistas; só as barras, o título e as tabelas.");
        var ids = new HashSet<string>();
        for (int i = 0; i < vistas.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(vistas[i].Id)) vistas[i].Id = "vista" + (i + 1);
            if (!ids.Add(vistas[i].Id)) { string novo = vistas[i].Id + "_" + (i + 1); c.Aviso($"Prancha: vista \"{vistas[i].Id}\" repetida; renomeada {novo}."); vistas[i].Id = novo; ids.Add(novo); }
        }
        EscolherCasas(c, vistas);

        // vista automática para as barras sem lugar
        var semCasa = c.Pos.Where(i => i.Casa.Length == 0).ToList();
        if (semCasa.Count > 0)
        {
            var auto = new VistaPerfil
            {
                Id = "_barras", Titulo = c.Rot("barras_restantes", "BARRAS|BARS"), Tipo = TipoVista.Barras,
                Quadro = vistas.Count > 0 ? vistas.Max(v => v.Quadro) : 1, Linha = vistas.Count > 0 ? vistas.Max(v => v.Linha) + 1 : 0,
            };
            foreach (var i in semCasa) { i.Casa = auto.Id; i.Dobrada = true; }
            if (vistas.Count > 0)
                c.Aviso("Prancha: posições sem vista no perfil desenhadas na vista BARRAS: " + string.Join(", ", semCasa.Select(i => i.Nome)) + ".");
            vistas.Add(auto);
        }

        // vistas → peças em coordenadas locais
        var pecas = new List<(VistaPerfil V, ResultadoTemplate R)>();
        foreach (var v in vistas)
        {
            ResultadoTemplate r = null;
            try { r = MontarVista(c, v); }
            catch (Exception ex) { c.Aviso($"Prancha: vista \"{v.Id}\" não pôde ser montada ({ex.Message})."); }
            if (r == null) continue;
            var lim = Geo.Limites(r, c.ht);
            if (lim.Vazio) { c.Aviso($"Prancha: vista \"{v.Id}\" sem nada para mostrar."); continue; }
            TituloVista(c, r, v, lim);
            pecas.Add((v, r));
        }

        var limTudo = Dispor(c, pecas);
        Folha(c, pecas, limTudo);

        // barras na ordem do modelo (numeração N)
        var ordem = c.Pos.Where(i => i.Barra != null).ToDictionary(i => i.Barra, i => i.Indice);
        var ord = c.R.Barras.OrderBy(b => ordem.TryGetValue(b, out int k) ? k : int.MaxValue).ToList();
        c.R.Barras.Clear(); c.R.Barras.AddRange(ord);
        var nomes = new HashSet<string>(c.R.Barras.Select(b => b.Nome));
        int antes = c.R.Faixas.Count;
        c.R.Faixas.RemoveAll(f => !nomes.Contains(f.Barra));
        if (c.R.Faixas.Count < antes) c.Aviso("Prancha: faixas sem barra correspondente foram removidas.");
        return c.R;
    }

    // ======================== campos ========================

    private static Dictionary<string, string> MontarCampos(Ctx c)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var e = c.E;
        if (e != null)
            foreach (var pr in e.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (pr.GetIndexParameters().Length > 0) continue;
                var t = pr.PropertyType;
                if (!(t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal))) continue;
                object val;
                try { val = pr.GetValue(e); } catch { continue; }
                d[pr.Name.ToLowerInvariant()] = val switch
                {
                    null => "",
                    bool b => b ? "1" : "0",
                    double x => NumV(x),
                    float x => NumV(x),
                    IFormattable f when !(val is Enum) => f.ToString(null, CultureInfo.InvariantCulture),
                    _ => val.ToString(),
                };
            }
        d["nome"] = e?.Nome ?? c.M?.Nome ?? "";
        d["repeticoes"] = c.Repeticoes.ToString(CultureInfo.InvariantCulture);
        d["pilares"] = TextoPilares(e?.Pilares ?? new List<string>());
        d["escala"] = Num(c.esc);
        if (c.M != null)
            foreach (var g in c.M.Corpos.GroupBy(cp => (cp.Papel ?? "").ToLowerInvariant()))
                d["qtd." + g.Key] = g.Count().ToString(CultureInfo.InvariantCulture);
        foreach (var kv in c.P.Padroes ?? new Dictionary<string, double>()) d[kv.Key] = NumV(kv.Value);
        if (VocabularioPrancha.CamposPorTipo.TryGetValue(c.Tipo, out var prov))
            try { prov(e, c.M, d); } catch (Exception ex) { c.Aviso("Prancha: campos do tipo " + c.Tipo + " falharam (" + ex.Message + ")."); }
        var peca = VocabularioPrancha.Rotulo(c.P, c.Tipo, "peca", "PEÇA|PIECE");
        int k = peca.IndexOf('|');
        d["peca"] = k < 0 ? peca : peca.Substring(0, k);
        d["peca_en"] = k < 0 ? peca : peca.Substring(k + 1);
        return d;
    }

    private static string TextoPilares(List<string> ps)
    {
        ps = ps.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
        return ps.Count == 0 ? "" : ps.Count == 1 ? ps[0] : string.Join(", ", ps.Take(ps.Count - 1)) + " e " + ps[ps.Count - 1];
    }

    // ======================== posições ========================

    private static void PrepararPosicoes(Ctx c)
    {
        var nomes = new HashSet<string>(StringComparer.Ordinal);
        int k = 0;
        foreach (var p in c.M.Posicoes)
        {
            if (p == null) continue;
            var i = new Info { P = p, Indice = k++ };
            string nome = string.IsNullOrWhiteSpace(p.Chave) ? (string.IsNullOrWhiteSpace(p.Funcao) ? "posicao" : p.Funcao) : p.Chave.Trim();
            if (!nomes.Add(nome))
            {
                int n = 2;
                while (!nomes.Add(nome + "_" + n)) n++;
                c.Aviso($"Prancha: chave de posição \"{nome}\" repetida; a barra se chama {nome}_{n}.");
                nome = nome + "_" + n;
            }
            i.Nome = nome;
            p.Distribuicao ??= new Distribuicao();
            var vs = p.Vertices ?? new List<P3>();
            i.Plano = vs.Count >= 2 ? Geo.Plano(vs) : null;
            if (i.Plano == null)
            {
                c.Aviso($"Prancha: posição \"{nome}\" sem geometria (menos de 2 vértices distintos); não desenhada.");
                continue;
            }
            if (p.Barras != null && p.Barras.Count > 0)
                i.Inst.AddRange(p.Barras.Where(l => l != null && l.Count >= 2));
            if (i.Inst.Count == 0 && p.VerticesVar != null && p.VerticesVar.Count > 0)
                i.Inst.AddRange(p.VerticesVar.Where(l => l != null && l.Count >= 2));
            if (i.Inst.Count == 0)
            {
                int n = Math.Max(1, p.Distribuicao.Quantidade);
                var delta = n > 1 ? (p.Distribuicao.Fim - p.Distribuicao.Inicio) * (1.0 / (n - 1)) : new P3(0, 0, 0);
                for (int j = 0; j < n; j++) i.Inst.Add(vs.Select(q => q + delta * j).ToList());
            }
            c.Pos.Add(i);
        }
    }

    /// <summary>A vista mostra a posição? Lista vazia = todas; aceita função, chave e curinga "*" ("calice_*").</summary>
    internal static bool Filtra(VistaPerfil v, PosicaoArmadura p)
    {
        if (v.Posicoes == null || v.Posicoes.Count == 0) return true;
        foreach (var f in v.Posicoes)
        {
            if (string.IsNullOrWhiteSpace(f)) continue;
            var rx = new Regex("^" + Regex.Escape(f.Trim()).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase);
            if (rx.IsMatch(p.Funcao ?? "") || rx.IsMatch(p.Chave ?? "")) return true;
        }
        return false;
    }

    private static void EscolherCasas(Ctx c, List<VistaPerfil> vistas)
    {
        foreach (var i in c.Pos)
        {
            var v = vistas.FirstOrDefault(x => Filtra(x, i.P) && (x.Tipo == TipoVista.Barras || (x.BarrasAbaixo && (x.Tipo == TipoVista.Planta || x.Tipo == TipoVista.Corte))));
            if (v != null) { i.Casa = v.Id; i.Dobrada = true; continue; }
            v = vistas.FirstOrDefault(x => Filtra(x, i.P) &&
                ((x.Tipo == TipoVista.Planta && Geo.VerdadeiraGrandeza(i.Plano, Geo.Ez)) ||
                 (x.Tipo == TipoVista.Corte && Geo.VerdadeiraGrandeza(i.Plano, LerCorte(c, x.Plano).Normal))));
            if (v != null) { i.Casa = v.Id; i.Dobrada = false; }
        }
    }

    // ======================== barras ========================

    /// <summary>A barra da posição com a forma em 2D (já nas coordenadas da vista).</summary>
    private static BarraTemplate CriarBarra(Ctx c, Info i, List<(double X, double Y)> pts)
    {
        var p = i.P;
        var d = p.Distribuicao ?? new Distribuicao();
        var b = new BarraTemplate
        {
            Nome = i.Nome, Bitola = p.Bitola, Aco = string.IsNullOrWhiteSpace(p.Aco) ? (c.E?.Aco ?? "CA-50") : p.Aco,
            Quantidade = Math.Max(1, i.Var ? p.ComprimentosVar.Count : d.Quantidade), Espacamento = d.Espacamento,
            Repeticoes = Math.Max(1, d.Repeticoes), Estribo = p.Forma == FormaBarra.Estribo, Escala = c.esc,
        };
        if (i.Var) b.ComprimentosVar.AddRange(p.ComprimentosVar);
        foreach (var s in p.SegmentosVar ?? new HashSet<int>()) b.SegmentosVar.Add(s);
        pts = Geometria2D.SemRepetidos(pts);
        if (p.Fechada && pts.Count > 2 && Geometria2D.Dist(pts[0], pts[pts.Count - 1]) < 1e-4) pts.RemoveAt(pts.Count - 1);
        bool feito = false;
        if (p.Forma == FormaBarra.Estribo && p.Fechada && (p.Formatos == null || p.Formatos.Count == 0) && pts.Count >= 3)
        {
            try
            {
                var fo = GeradorEstribo.FechadoPadrao(pts, p.Bitola, b.Aco, c.Cfg.Dobramento);
                b.Vertices.AddRange(fo.Vertices.Select(v => (v.X, v.Y)));
                b.Formatos.AddRange(fo.Formatos);
                foreach (var s in fo.Sobrepostos) b.Sobrepostos.Add(s);
                foreach (var s in fo.Cotados) b.Cotados.Add(s);
                feito = true;
            }
            catch (Exception ex) { c.Aviso($"Prancha: ganchos do estribo \"{i.Nome}\" não gerados ({ex.Message})."); }
        }
        if (!feito)
        {
            b.Vertices.AddRange(pts);
            b.Fechada = p.Fechada;
            int nseg = p.Fechada ? pts.Count : pts.Count - 1;
            if (p.Formatos != null && p.Formatos.Count == nseg) b.Formatos.AddRange(p.Formatos);
            else for (int k = 0; k < nseg; k++) b.Formatos.Add(FormatoSegmento.Linha);
            foreach (var s in p.Sobrepostos ?? new HashSet<int>()) b.Sobrepostos.Add(s);
        }
        Descrever(c, b);
        i.Barra = b;
        return b;
    }

    /// <summary>Descrição embaixo da barra (ao lado, girada, se a barra for alta e estreita).</summary>
    private static void Descrever(Ctx c, BarraTemplate b)
    {
        if (b.Vertices.Count == 0) return;
        double x0 = b.Vertices.Min(q => q.X), x1 = b.Vertices.Max(q => q.X), y0 = b.Vertices.Min(q => q.Y), y1 = b.Vertices.Max(q => q.Y);
        if (y1 - y0 > 2.5 * Math.Max(x1 - x0, 1) && y1 - y0 > 15 * c.ht)
        { b.PosicaoDescricao = (x0 - 2 * c.ht, (y0 + y1) / 2); b.RotacaoDescricao = Math.PI / 2; }
        else { b.PosicaoDescricao = ((x0 + x1) / 2, y0 - 2.5 * c.ht); b.RotacaoDescricao = 0; }
    }

    /// <summary>Forma dobrada (verdadeira grandeza no plano da barra) com o canto superior esquerdo em (x, yTopo).</summary>
    private static BarraTemplate Dobrada(Ctx c, Info i, double x, double yTopo)
    {
        var pts = Geo.Desenvolver(i.P.Vertices, i.Plano);
        var b = CriarBarra(c, i, pts);
        if (b.Vertices.Count == 0) return b;
        double mx = b.Vertices.Min(q => q.X), my = b.Vertices.Max(q => q.Y);
        for (int k = 0; k < b.Vertices.Count; k++) b.Vertices[k] = (b.Vertices[k].X - mx + x, b.Vertices[k].Y - my + yTopo);
        Descrever(c, b);
        return b;
    }

    // ======================== layout ========================

    private static void TituloVista(Ctx c, ResultadoTemplate r, VistaPerfil v, Retangulo lim)
    {
        double ht = c.ht;
        var linhas = c.L(v.Titulo);
        double yEsc = lim.Y1 + 2 * ht;
        Texto(r, c.L(c.Rot("escala", "ESC 1:{escala}|SCALE 1:{escala}"))[0], lim.X0, yEsc, 0.7 * ht);
        double y = yEsc + 1.3 * ht + (linhas.Length - 1) * 1.65 * ht;
        foreach (var l in linhas) { Texto(r, l, lim.X0, y, 1.1 * ht); y -= 1.65 * ht; }
    }

    /// <summary>Quadros lado a lado (0 = forma, 1 = armadura…); em cada um, linhas de cima para baixo e vistas da esquerda
    /// para a direita, espaçadas pelos limites de cada vista. Devolve o retângulo de tudo.</summary>
    private Retangulo Dispor(Ctx c, List<(VistaPerfil V, ResultadoTemplate R)> pecas)
    {
        double ht = c.ht, gap = 8 * ht, gapQuadro = 20 * ht;
        var tudo = new Retangulo();
        var vistas = new List<VistaGerada>();
        double xQ = 0;
        foreach (var q in pecas.GroupBy(p => p.V.Quadro).OrderBy(g => g.Key))
        {
            double yLinha = 0, largQ = 0;
            foreach (var lin in q.GroupBy(p => p.V.Linha).OrderBy(g => g.Key))
            {
                double x = xQ, alt = 0;
                foreach (var (v, r) in lin.OrderBy(p => p.V.Ordem))
                {
                    var lim = Geo.Limites(r, ht);
                    Geo.Deslocar(r, x - lim.X0, yLinha - lim.Y1);
                    lim.Deslocar(x - lim.X0, yLinha - lim.Y1);
                    Geo.Juntar(c.R, r);
                    vistas.Add(new VistaGerada { Id = v.Id, Titulo = v.Titulo, Tipo = v.Tipo, Quadro = v.Quadro, Limites = lim });
                    tudo.Incluir(lim);
                    x += lim.Largura + gap;
                    alt = Math.Max(alt, lim.Altura);
                }
                largQ = Math.Max(largQ, x - gap - xQ);
                yLinha -= alt + gap;
            }
            xQ += largQ + gapQuadro;
        }
        Vistas = vistas;
        return tudo;
    }

    // ======================== folha: título, RA, produção, detalhes ========================

    private void Folha(Ctx c, List<(VistaPerfil V, ResultadoTemplate R)> pecas, Retangulo tudo)
    {
        double ht = c.ht;
        var folha = c.P.Folha ?? new FolhaPerfil();
        string titulo = c.Fmt.Formatar(string.IsNullOrWhiteSpace(folha.Titulo) ? "{nome}" : folha.Titulo).Trim();
        int k = titulo.IndexOf(" - ", StringComparison.Ordinal);
        c.R.Detalhamento = k > 0 ? titulo.Substring(0, k) : titulo;
        if (tudo.Vazio) { tudo.Incluir(0, 0); tudo.Incluir(40 * ht, 0); }

        // título + pilares em cima de cada quadro
        double yTopo = tudo.Y1;
        var quadros = Vistas.GroupBy(v => v.Quadro).Select(g => (X0: g.Min(v => v.Limites.X0), X1: g.Max(v => v.Limites.X1))).ToList();
        if (quadros.Count == 0) quadros.Add((tudo.X0, tudo.X1));
        double yTituloTopo = yTopo;
        foreach (var (x0, x1) in quadros)
        {
            var linhas = QuebrarPilares(c, Math.Max(x1 - x0, 40 * ht));
            double alt = 2.2 * ht + linhas.Count * 1.6 * ht + 2 * ht;
            double yt = yTopo + alt;
            if (titulo.Length > 0) Texto(c.R, titulo, x0, yt, 1.8 * ht);
            double y = yt - 2.2 * ht;
            foreach (var l in linhas) { Texto(c.R, l, x0, y, 1.1 * ht); y -= 1.6 * ht; }
            yTituloTopo = Math.Max(yTituloTopo, yt + 1.8 * ht);
        }

        // dados do detalhamento
        double vol = Volume(c), area = AreaForma(c);
        c.R.DadosDetalhamento = new DadosDetalhamento
        {
            Grupo = c.Fmt.Formatar(c.Rot("grupo")), Repeticoes = c.Repeticoes, Classe = c.E != null ? "C" + Num(c.E.Fck) : "",
            Volume = Math.Round(vol, 3), AreaForma = Math.Round(area, 2), Comentarios = "Gerado pelo gerador de prancha do motor de detalhamento.",
        };

        // relação do aço e produção à direita de tudo
        double xR = tudo.X1 + 6 * ht, yR = yTituloTopo;
        if (folha.RelacaoAco) c.R.PosicaoRA = (xR, yR);
        double altRA = folha.RelacaoAco ? Math.Max(30 * ht, (c.R.Barras.Count + 5) * 2.2 * ht) : 0;
        if (folha.InformacoesProducao) Producao(c, xR, yR - altRA - (folha.RelacaoAco ? 6 * ht : 0), vol, area);

        // detalhes e notas embaixo
        double xDet = quadros.Count > 1 ? quadros[1].X0 : quadros[0].X0;
        Detalhes(c, folha, xDet, tudo.Y0 - 10 * ht);
    }

    private static List<string> QuebrarPilares(Ctx c, double largura)
    {
        var r = new List<string>();
        string lista = c.Campos.TryGetValue("pilares", out var s) ? s : "";
        if (string.IsNullOrWhiteSpace(lista)) return r;
        int max = Math.Max(20, (int)(largura / (0.75 * 1.1 * c.ht)));
        string atual = "";
        foreach (var p in lista.Split(' '))
        {
            if (atual.Length > 0 && atual.Length + 1 + p.Length > max) { r.Add(atual); atual = p; }
            else atual = atual.Length == 0 ? p : atual + " " + p;
        }
        if (atual.Length > 0) r.Add(atual);
        return r;
    }

    /// <summary>Volume de concreto de uma peça (m³): corpos do elemento menos os vazios.</summary>
    private static double Volume(Ctx c)
    {
        double v = 0;
        foreach (var cp in c.M.Corpos)
        {
            if (Externo(cp)) continue;
            double vc = AreaPlanta(cp) * Math.Abs(cp.Z1 - cp.Z0);
            v += cp.Vazio ? -vc : vc;
        }
        return Math.Max(0, v) / 1e6;
    }

    /// <summary>Área de formas de uma peça (m²): faces laterais dos corpos do elemento e dos vazios.</summary>
    private static double AreaForma(Ctx c) =>
        c.M.Corpos.Where(cp => !Externo(cp)).Sum(cp => PerimetroPlanta(cp) * Math.Abs(cp.Z1 - cp.Z0)) / 1e4;

    /// <summary>Peso de aço de uma posição (kg, 1 peça), pelas regras do CAD/RA sobre a barra desenhada
    /// (<see cref="MedidaBarra"/>); sem barra desenhada, pelo eixo 3D (<see cref="ComprimentoPorEixo"/>).</summary>
    private static double PesoPosicao(Info i, ConfiguracaoArmadura cfg)
    {
        var p = i.P;
        if (i.Barra != null && i.Barra.Vertices.Count >= 2) return MedidaBarra.Peso(i.Barra, cfg);
        int rep = Math.Max(1, p.Distribuicao?.Repeticoes ?? 1);
        double l;
        if (i.Var) l = p.ComprimentosVar.Sum();
        else if (p.Vertices.Count >= 2)
            l = ComprimentoPorEixo.Calcular(p.Vertices.Select(v => (v.X, v.Y, v.Z)).ToList(), p.Fechada, p.Bitola, p.Aco ?? "CA-50",
                    p.Forma == FormaBarra.Estribo, false, cfg) * Math.Max(1, p.Distribuicao?.Quantidade ?? 1);
        else l = 0;
        return l * rep / 100 * MedidaBarra.MassaLinear(p.Bitola, p.Aco, cfg);
    }

    private void Producao(Ctx c, double x, double y, double vol, double area)
    {
        double peso = c.Pos.Sum(i => PesoPosicao(i, c.Cfg));
        double volR = Math.Round(vol, 3);
        var t = new TabelaTemplate { Titulo = c.Celula(c.Rot("producao")), X = x, Y = y, Escala = c.esc };
        t.Larguras.AddRange(new[] { 24.0, 11.0 });
        t.Alinhamentos.AddRange(new[] { 0, 2 });
        t.Linhas.Add(new[] { c.Celula("QUANTIDADE:|QUANTITY:"), $"{c.Repeticoes} un." });
        t.Linhas.Add(new[] { c.Celula("VOLUME DE CONCRETO (1 {peca}):|CONCRETE VOLUME (1 {peca_en}):"), $"{NumV(volR)} m³" });
        t.Linhas.Add(new[] { c.Celula("PESO DE AÇO (1 {peca}):|STEEL WEIGHT (1 {peca_en}):"), $"{NumV(Math.Round(peso, 1))} kg" });
        t.Linhas.Add(new[] { c.Celula("PESO TOTAL DE AÇO:|TOTAL STEEL WEIGHT:"), $"{NumV(Math.Round(peso * c.Repeticoes, 1))} kg" });
        t.Linhas.Add(new[] { c.Celula("TAXA:|RATIO:"), vol > 0 ? $"{NumV(Math.Round(peso / vol, 1))} kg/m³" : "-" });
        t.Linhas.Add(new[] { c.Celula("ÁREA DE FORMAS (1 {peca}):|FORMWORK AREA (1 {peca_en}):"), $"{NumV(Math.Round(area, 2))} m²" });
        c.R.Tabelas.Add(t);
    }

    private static readonly string[] DetalhesTipicos = { "raio_curvatura", "emendas", "comprimento_barras" };

    private void Detalhes(Ctx c, FolhaPerfil folha, double x0, double yTopo)
    {
        double ht = c.ht, x = x0;
        var pedidos = new HashSet<string>((folha.Detalhes ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase);
        foreach (var d in pedidos)
            if (!(d.Equals("chaves", StringComparison.OrdinalIgnoreCase) || DetalhesTipicos.Contains(d, StringComparer.OrdinalIgnoreCase)
                  || d.Equals("ancoragem_estacas", StringComparison.OrdinalIgnoreCase) || d.Equals("barra_variavel", StringComparison.OrdinalIgnoreCase)))
                c.Aviso($"Prancha: detalhe padrão \"{d}\" desconhecido; ignorado.");
        bool chaves = pedidos.Contains("chaves"), tipico = DetalhesTipicos.Any(pedidos.Contains);
        if (chaves || tipico)
        {
            try
            {
                x = DEB.Core.Templates.DetalhesTipicos.Desenhar(c.R, c.Idioma, c.esc, c.Cfg.AlturaTextoMm, x, yTopo, chaves,
                    c.P.Padrao("chaves.passo", 5), c.P.Padrao("chaves.dente", 4), c.P.Padrao("chaves.profundidade", 1), out _);
                x += 6 * ht;
            }
            catch (Exception ex) { c.Aviso("Prancha: detalhes padrão não desenhados (" + ex.Message + ")."); }
        }
        if (pedidos.Contains("ancoragem_estacas")) x = AncoragemEstacas(c, x, yTopo);
        if (pedidos.Contains("barra_variavel") && c.Pos.Any(i => i.Var)) x = IndicacaoBarraVariavel(c, x, yTopo);
        Notas(c, folha, x, yTopo);
    }

    /// <summary>ANCORAGEM DAS ARMADURAS DAS ESTACAS (esquemático). Copiado de TemplateBlocoEstacas.Detalhes [remover na integração].</summary>
    private static double AncoragemEstacas(Ctx c, double x, double yTopo)
    {
        double ht = c.ht;
        double y = Linhas(c, c.R, "ANCORAGEM DAS ARMADURAS DAS ESTACAS|ANCHORAGE OF PILE REINFORCEMENT", x, yTopo, 1.1 * ht);
        Texto(c.R, c.L("SEM ESCALA|NOT TO SCALE")[0], x, y + 0.3 * ht, 0.7 * ht);
        double bw = 16 * ht, bh = 8 * ht, bx = x, by = y - 4 * ht - bh;
        Ret(c.R, bx, by, bx + bw, by + bh);
        foreach (double fr in new[] { 0.2, 0.8 })
        {
            double ex = bx + fr * bw, r = 1.5 * ht;
            Ret(c.R, ex - r, by - 6 * ht, ex + r, by + 0.5 * ht, "ARM-ESTACA");
            foreach (double d in new[] { -0.6 * r, 0.6 * r })
                Linha(c.R, "ARM-BARRA-DETALHE", (ex + d, by - 6 * ht), (ex + d, by + bh - 0.6 * ht), (ex + d + Math.Sign(d) * 1.2 * ht, by + bh - 0.6 * ht));
        }
        double yt = by - 8 * ht;
        foreach (var l in c.L("ANCORAR AS ARMADURAS DAS ESTACAS NO TOPO DO BLOCO,|ANCHOR THE PILE REINFORCEMENT AT THE TOP OF THE PILE CAP,")
                     .Concat(c.L("ACIMA DAS ARMADURAS PRINCIPAIS|ABOVE THE MAIN REINFORCEMENT")))
        { Texto(c.R, l, bx, yt, 0.7 * ht); yt -= 1.2 * ht; }
        return x + 44 * ht;
    }

    /// <summary>INDICAÇÃO BARRA VARIÁVEL. Copiado de TemplateBlocoEstacas.Detalhes [remover na integração].</summary>
    private static double IndicacaoBarraVariavel(Ctx c, double x, double yTopo)
    {
        double ht = c.ht;
        double yv = Linhas(c, c.R, "INDICAÇÃO BARRA VARIÁVEL|VARIABLE BAR INDICATION", x, yTopo, 1.1 * ht);
        Texto(c.R, c.L(c.Rot("escala", "ESC 1:{escala}|SCALE 1:{escala}"))[0], x, yv + 0.3 * ht, 0.7 * ht);
        var linhas = c.L("\"L\" - TRECHO RETO DA BARRA QUE É VARIÁVEL|\"L\" - STRAIGHT PART OF THE BAR THAT VARIES")
            .Concat(c.L("ESTÁ INDICADO NAS VISTAS EM PLANTA|IS INDICATED IN THE PLAN VIEWS")).ToList();
        double alt = (linhas.Count + 1) * 1.5 * ht;
        Ret(c.R, x, yv - 1 * ht - alt, x + 42 * ht, yv - 1 * ht, "ARM-TEXTO-FORMA");
        double y = yv - 3 * ht;
        foreach (var l in linhas) { Texto(c.R, l, x + ht, y, 0.7 * ht); y -= 1.5 * ht; }
        return x + 48 * ht;
    }

    private static void Notas(Ctx c, FolhaPerfil folha, double x, double yTopo)
    {
        var notas = (folha.Notas ?? new List<string>()).Select(n => c.Fmt.Formatar(n ?? "").Trim()).Where(n => n.Length > 0).ToList();
        if (notas.Count == 0) return;
        double ht = c.ht, y = yTopo;
        foreach (var l in c.L(c.Rot("notas", "NOTAS:|NOTES:"))) { Texto(c.R, l, x, y, 0.8 * ht); y -= 1.4 * ht; }
        int k = 1;
        foreach (var n in notas)
        {
            string s = Regex.IsMatch(n, @"^\d+\s*[\)\.\-]") ? n : $"{k}) {n}";
            k++;
            foreach (var l in c.L(s)) { Texto(c.R, l, x, y, 0.8 * ht); y -= 1.4 * ht; }
        }
    }

    // ======================== desenho básico ========================

    private static void Texto(ResultadoTemplate r, string s, double x, double y, double h, int al = 0, string layer = "ARM-TEXTO-FORMA", double ang = 0)
    {
        if (string.IsNullOrEmpty(s)) return;
        r.Textos.Add(new TextoTemplate { Texto = s, X = x, Y = y, Altura = h, Alinhamento = al, Layer = layer, Angulo = ang });
    }

    /// <summary>Texto "PT|EN" em linhas (1,5 h); devolve o y abaixo.</summary>
    private static double Linhas(Ctx c, ResultadoTemplate r, string texto, double x, double y, double h, int al = 0, IDictionary<string, string> extras = null)
    {
        foreach (var l in c.L(texto, extras)) { Texto(r, l, x, y, h, al); y -= 1.5 * h; }
        return y;
    }

    private static void Polilinha(ResultadoTemplate r, string layer, bool fechada, IEnumerable<(double X, double Y)> pts)
    {
        var l = pts.ToList();
        if (l.Count >= 2) r.Contornos.Add(new ContornoTemplate { Layer = layer, Fechado = fechada, Vertices = l });
    }

    private static void Linha(ResultadoTemplate r, string layer, params (double X, double Y)[] pts) => Polilinha(r, layer, false, pts);

    private static void Ret(ResultadoTemplate r, double x0, double y0, double x1, double y1, string layer = "ARM-FORMA") =>
        Polilinha(r, layer, true, new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) });

    private static void Ponto(ResultadoTemplate r, double x, double y, double phi) =>
        r.Circulos.Add(new CirculoTemplate { X = x, Y = y, Raio = Math.Max(phi / 20, 0.3), Layer = "ARM-BARRA-SECAO" });

    private static void Cota(Ctx c, ResultadoTemplate r, (double, double) p1, (double, double) p2, (double, double) pos, bool vertical, string texto = "") =>
        r.Cotas.Add(new CotaTemplate { P1 = p1, P2 = p2, Posicao = pos, Angulo = vertical ? Math.PI / 2 : 0, Escala = c.esc, Texto = texto });

    private static void Indicacao(Ctx c, ResultadoTemplate r, Info i, (double, double) texto, IEnumerable<(double, double)> refs)
    {
        if (i?.Barra == null && string.IsNullOrEmpty(i?.Nome)) return;
        var f = new FaixaTemplate { Barra = i.Nome, Tipo = TipoFaixa.Indicacao, Escala = c.esc, Posicao = texto };
        f.Referencias.AddRange(refs);
        if (f.Referencias.Count > 0) r.Faixas.Add(f);
    }
}
