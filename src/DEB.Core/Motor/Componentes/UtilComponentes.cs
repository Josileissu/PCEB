using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Estribos;
using DEB.Core.Modelo;
using static DEB.Core.Motor.Componentes.GeometriaPlanta;

namespace DEB.Core.Motor.Componentes;

/// <summary>Retângulo em planta (cm), publicado em <see cref="ContextoMotor.Dados"/> (ex.: "abertura", "calice_furo").</summary>
public sealed class Retangulo
{
    public double X0 { get; set; }
    public double Y0 { get; set; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public Retangulo() { }
    public Retangulo(double x0, double y0, double x1, double y1) { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
    public List<(double X, double Y)> Planta() => Ret(X0, Y0, X1, Y1);
}

/// <summary>
/// Base dos componentes: o argumento comum <c>ativo</c> (0 = não faz nada; padrão 1) permite à receita ligar/desligar um
/// passo por fórmula (ex.: "ativo": "calice"). A documentação de cada componente acrescenta esse argumento.
/// </summary>
public abstract class ComponenteBase : IComponente
{
    public abstract string Tipo { get; }
    protected abstract IEnumerable<(string Nome, string Descricao)> Docs { get; }

    private IReadOnlyDictionary<string, string> _args;
    public IReadOnlyDictionary<string, string> Argumentos
    {
        get
        {
            if (_args != null) return _args;
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (n, t) in Docs) d[n] = t;
            d["ativo"] = "0 = pula o componente (padrão 1).";
            return _args = d;
        }
    }

    /// <summary>Variáveis (ctx.Variaveis, snake_case) que o componente publica com os argumentos PADRÃO — para o validador de
    /// receitas (interface IComponenteComSaidas da frente R). Quando o nome vem de um argumento, use
    /// <see cref="VariaveisPublicadasCom"/>.</summary>
    public virtual IReadOnlyCollection<string> VariaveisPublicadas => Array.Empty<string>();

    /// <summary>Variáveis publicadas com estes argumentos (nomes configuráveis resolvidos).</summary>
    public virtual IReadOnlyCollection<string> VariaveisPublicadasCom(ArgumentosComponente args) => VariaveisPublicadas;

    public void Aplicar(ContextoMotor ctx, ArgumentosComponente args)
    {
        if (ctx == null) throw new ArgumentNullException(nameof(ctx));
        args ??= new ArgumentosComponente();
        if (!args.Bool("ativo", true)) return;
        Executar(ctx, args);
    }

    protected abstract void Executar(ContextoMotor ctx, ArgumentosComponente a);

    /// <summary>Polígono publicado em ctx.Dados pelo nome dado no argumento (padrão "contorno"); erro claro se faltar.</summary>
    protected List<(double X, double Y)> Poligono(ContextoMotor ctx, ArgumentosComponente a, string arg = "contorno")
    {
        string nome = a.Txt(arg, "contorno");
        return ctx.Dado<List<(double X, double Y)>>(nome)
            ?? throw new InvalidOperationException($"{Tipo}: falta o dado \"{nome}\" (polígono em planta) — rode antes o componente que o publica.");
    }

    protected static Retangulo Retangulo(ContextoMotor ctx, string nome) => string.IsNullOrEmpty(nome) ? null : ctx.Dado<Retangulo>(nome);

    /// <summary>Acumula o máximo numa variável do contexto (nome vazio = não publica).</summary>
    protected static void PublicarMax(ContextoMotor ctx, string nome, double v)
    {
        if (string.IsNullOrEmpty(nome)) return;
        ctx.Variaveis[nome] = ctx.Variaveis.TryGetValue(nome, out var x) ? Math.Max(x, v) : v;
    }

    /// <summary>Acumula o mínimo numa variável do contexto (nome vazio = não publica).</summary>
    protected static void PublicarMin(ContextoMotor ctx, string nome, double v)
    {
        if (string.IsNullOrEmpty(nome)) return;
        ctx.Variaveis[nome] = ctx.Variaveis.TryGetValue(nome, out var x) ? Math.Min(x, v) : v;
    }

    protected static double Graus(double g) => g == 0 ? 0 : g == 90 ? Math.PI / 2 : g * Math.PI / 180;
}

/// <summary>Uma barra reta de uma família: reta t (coordenada normal) de A a B (coordenada ao longo de U).</summary>
internal sealed class LinhaReta { public double T, A, B; }

/// <summary>Família de barras retas (com pernas) numa direção da planta — usada pela faixa e pela malha.</summary>
internal sealed class FamiliaRetas
{
    public double Ang, Bitola, Esp, Perna, Z;
    public bool Superior;
    /// <summary>Distribuição com o passo real entre as duas primeiras retas (faixa) em vez do espaçamento pedido (malha).</summary>
    public bool PassoReal;
    public List<LinhaReta> Linhas = new List<LinhaReta>();
    public int Faixas = 1, PorFaixa;
    public (double X, double Y) U => (Math.Cos(Ang), Math.Sin(Ang));
    public (double X, double Y) N => (-Math.Sin(Ang), Math.Cos(Ang));
    public (double X, double Y) Ponto(double t, double s) => (t * N.X + s * U.X, t * N.Y + s * U.Y);

    /// <summary>Uma barra na reta t: corda do contorno − cobrimento − recuo (ø da lateral); descarta fora do cobrimento
    /// e trechos de até 5 cm.</summary>
    public void AdicionarLinha(List<(double X, double Y)> contorno, double cob, double recuo, double t)
    {
        var o = (X: t * N.X, Y: t * N.Y);
        var cd = Corda(contorno, o, U);
        if (cd == null) return;
        if (Corda(Encolher(contorno, cob + 0.5), o, U) == null) return;   // a reta fica fora do cobrimento
        double a = cd.Value.A + cob + recuo, b = cd.Value.B - cob - recuo;
        if (b - a > 5) Linhas.Add(new LinhaReta { T = t, A = a, B = b });
    }

    /// <summary>Geometria 3D: barras, representativa, distribuição e VAR; adiciona a posição ao modelo.</summary>
    public void Montar(ContextoMotor ctx, PosicaoArmadura p)
    {
        var comps = Linhas.Select(l => Math.Round(l.B - l.A)).ToList();
        bool var = comps.Distinct().Count() > 1;
        double L = comps.Max();
        if (var)
        {
            p.Parametros["comprimento_reto"] = L;
            p.Regras["comprimento_reto"] = "face a face − 2·cobrimento − lateral (grupo VAR: maior barra)";
        }
        else L = ctx.Par(p, "comprimento_reto", L, "face a face − 2·cobrimento − lateral");
        double perna = Perna;
        double zh = Superior ? Z + Bitola / 20 : Z - Bitola / 20;   // face externa do trecho reto
        double zp = Superior ? zh - perna : zh + perna;
        var meios = new List<P3>();
        for (int i = 0; i < Linhas.Count; i++)
        {
            var l = Linhas[i];
            double Li = var ? comps[i] : L, m = (l.A + l.B) / 2, s0 = m - Li / 2, s1 = m + Li / 2;
            var a = Ponto(l.T, s0); var b = Ponto(l.T, s1);
            var pts = new List<P3>();
            if (perna > 0) pts.Add(new P3(a.X, a.Y, zp));
            pts.Add(new P3(a.X, a.Y, zh));
            pts.Add(new P3(b.X, b.Y, zh));
            if (perna > 0) pts.Add(new P3(b.X, b.Y, zp));
            p.Barras.Add(pts);
            var mm = Ponto(l.T, m);
            meios.Add(new P3(mm.X, mm.Y, zh));
        }
        if (perna <= 0) p.Forma = FormaBarra.Reta;
        p.Bitola = Bitola;
        p.Vertices = p.Barras[0];
        for (int i = 0; i + 1 < p.Vertices.Count; i++) p.Formatos.Add(FormatoSegmento.Linha);
        if (Faixas > 1 && Linhas.Count % Faixas != 0) Faixas = 1;   // alguma barra caiu fora: uma distribuição só
        int porFaixa = Linhas.Count / Faixas;
        double passo = PassoReal && porFaixa > 1 ? Math.Abs(Linhas[1].T - Linhas[0].T) : Esp;
        p.Distribuicao = new Distribuicao
        {
            Inicio = meios[0], Fim = meios[Math.Max(0, porFaixa - 1)], Quantidade = porFaixa, Espacamento = passo, Repeticoes = Faixas,
        };
        if (var)
        {
            p.ComprimentosVar = comps.Select(x => x + 2 * perna).ToList();
            p.VerticesVar = p.Barras.Select(b => b.ToList()).ToList();
            p.SegmentosVar.Add(perna > 0 ? 1 : 0);
        }
        ctx.Adicionar(p);
    }
}

/// <summary>Utilitários de geometria de barras comuns aos componentes.</summary>
internal static class BarrasUtil
{
    public static P3 Centro(List<P3> pts) => new P3(pts.Average(p => p.X), pts.Average(p => p.Y), pts.Average(p => p.Z));

    /// <summary>Estribo fechado no plano (u, v) pelo <see cref="GeradorEstribo.FechadoPoligono"/> (ganchos 90° no canto
    /// superior esquerdo); cada mapa leva uma cópia a 3D. Grava o parâmetro "gancho" (aceita sobrescrita).</summary>
    public static void Estribo(ContextoMotor ctx, PosicaoArmadura p, List<(double X, double Y)> pol, double phi, IEnumerable<Func<double, double, P3>> mapas)
    {
        int canto = GeradorEstribo.Canto(pol, PosicaoGanchos.SuperiorEsquerdo);
        double lg = ctx.Par(p, "gancho", ctx.Cfg.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho90, phi, ctx.Aco, estribo: true),
            "gancho 90° (NBR 6118 tab. 9.2)");
        double passagem = ctx.Cfg.Dobramento.DiametroPino(phi, ctx.Aco, estribo: true) + 2 * phi / 10;
        var fo = GeradorEstribo.FechadoPoligono(pol, canto, FormatoSegmento.Gancho90, lg, passagem);
        foreach (var mapa in mapas) p.Barras.Add(fo.Vertices.Select(v => mapa(v.X, v.Y)).ToList());
        p.Formatos.AddRange(fo.Formatos);
        foreach (var s in fo.Sobrepostos) p.Sobrepostos.Add(s);
        p.Bitola = phi;
        p.Fechada = false;   // estribo fechado = forma Estribo; a poligonal (com os ganchos) é aberta
    }

    public static Func<double, double, P3> Horizontal(double z) => (x, y) => new P3(x, y, z);
}
