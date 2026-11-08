using System;
using System.Collections.Generic;
using System.Linq;
using static DEB.Core.Motor.Componentes.GeometriaPlanta;
using static DEB.Core.Motor.Geometria2D;

namespace DEB.Core.Motor.Componentes;

// Componentes de geometria e de apoio à receita: apoios em planta, corpos de concreto, níveis, bitola efetiva, aviso.

/// <summary>
/// <c>apoios_estacas</c>: contorno em planta e pontos de apoio (estacas) de um elemento apoiado em grade nx × ny ou em
/// triângulo de 3 apoios (contorno hexagonal com as 6 faces a <c>dist_face</c> dos eixos). Origem no canto inferior
/// esquerdo da caixa envolvente. Serve ao bloco e a qualquer elemento sobre apoios pontuais (sapata corrida sobre
/// estacas, radier estaqueado de faixa…).
/// <para>Publica: Dados "contorno" (List&lt;(X,Y)&gt;, anti-horário), "estacas" (List&lt;(X,Y)&gt;), "arranjo" ("grade" |
/// "triangulo"); Variaveis "largura_x", "largura_y" (caixa envolvente), "centro_apoios_x", "centro_apoios_y" (média dos
/// apoios), "triangular" (1/0), "n_apoios".</para>
/// </summary>
public sealed class ApoiosEstacas : ComponenteBase
{
    public override string Tipo => "apoios_estacas";
    public override IReadOnlyCollection<string> VariaveisPublicadas { get; } =
        new[] { "largura_x", "largura_y", "centro_apoios_x", "centro_apoios_y", "triangular", "n_apoios", "circular", "raio" };
    protected override IEnumerable<(string, string)> Docs => new[]
    {
        ("arranjo", "texto: \"grade\" (nx × ny), \"triangulo\" (3 apoios) ou \"cinco\" (4 nos cantos de esp_x × esp_y e 1 no centro). Padrão \"grade\"."),
        ("nx", "apoios em x (grade), ≥ 1. Padrão 2."),
        ("ny", "apoios em y (grade), ≥ 1. Padrão 1."),
        ("esp_x", "cm, entre eixos em x (triângulo: lado). Padrão 120."),
        ("esp_y", "cm, entre eixos em y. Padrão 120."),
        ("dist_face", "cm, eixo do apoio → face (triângulo: as 6 faces). Padrão 35."),
        ("forma", "texto: \"\" = contorno pelos apoios (acima); \"circular\" (diametro) ou \"retangular\" (lx, ly) = contorno dado, " +
                  "apoios centrados nele (grade ou anéis). Padrão \"\"."),
        ("diametro", "cm (forma circular)."), ("lx", "cm (forma retangular)."), ("ly", "cm (forma retangular)."),
        ("lados", "lados do polígono que representa o círculo (recorte das malhas). Padrão 360."),
        ("anel1_n", "arranjo \"aneis\": apoios no anel 1. Padrão 0."), ("anel1_raio", "cm."), ("anel1_angulo", "graus do 1º apoio. Padrão 0."),
        ("anel2_n", "apoios no anel 2. Padrão 0."), ("anel2_raio", "cm."), ("anel2_angulo", "graus. Padrão 0."),
        ("central", "1 = apoio no centro (anéis). Padrão 0."),
    };

    protected override void Executar(ContextoMotor ctx, ArgumentosComponente a)
    {
        string forma = a.Txt("forma", "");
        if (forma.Length > 0) { ComFormaDada(ctx, a, forma); return; }
        string arr = a.Txt("arranjo", "grade");
        bool tri = arr == "triangulo", cinco = arr == "cinco";
        double e = a.Num("dist_face", 35);
        List<(double X, double Y)> cont, est;
        if (tri) (cont, est) = Triangular(a.Num("esp_x", 120), e);
        else
        {
            int nx = cinco ? 2 : Math.Max(1, a.Int("nx", 2)), ny = cinco ? 2 : Math.Max(1, a.Int("ny", 1));
            double ex = a.Num("esp_x", 120), ey = a.Num("esp_y", 120);
            double W = (nx - 1) * ex + 2 * e, H = (ny - 1) * ey + 2 * e;
            cont = new List<(double, double)> { (0, 0), (W, 0), (W, H), (0, H) };
            est = new List<(double X, double Y)>();
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) est.Add((e + i * ex, e + j * ey));
            if (cinco) est.Add((W / 2, H / 2));
        }
        ctx.Dados["contorno"] = cont;
        ctx.Dados["estacas"] = est;
        ctx.Dados["arranjo"] = tri ? "triangulo" : cinco ? "cinco" : "grade";
        ctx.Variaveis["largura_x"] = cont.Max(p => p.X);
        ctx.Variaveis["largura_y"] = cont.Max(p => p.Y);
        ctx.Variaveis["centro_apoios_x"] = est.Average(p => p.X);
        ctx.Variaveis["centro_apoios_y"] = est.Average(p => p.Y);
        ctx.Variaveis["triangular"] = tri ? 1 : 0;
        ctx.Variaveis["n_apoios"] = est.Count;
        ctx.Variaveis["circular"] = 0;
        ctx.Variaveis["raio"] = 0;
    }

    /// <summary>
    /// Contorno dado (base de reservatório, sapata circular): círculo (polígono de <c>lados</c> lados inscrito) ou retângulo,
    /// com a origem no canto inferior esquerdo da caixa; apoios centrados — grade nx × ny a esp_x/esp_y, ou anéis
    /// (até 2, com ângulo do 1º) e o central. Publica também Dados "forma" e Variaveis "circular", "raio" (circular).
    /// </summary>
    private static void ComFormaDada(ContextoMotor ctx, ArgumentosComponente a, string forma)
    {
        bool circ = forma == "circular";
        if (!circ && forma != "retangular") throw new ArgumentException($"apoios_estacas: forma \"{forma}\" desconhecida (circular | retangular).");
        double W, H;
        List<(double X, double Y)> cont;
        if (circ)
        {
            double R = a.Num("diametro", 0) / 2;
            if (R <= 0) throw new ArgumentException("apoios_estacas: forma circular sem diametro.");
            int n = Math.Max(16, a.Int("lados", 360));
            cont = Enumerable.Range(0, n).Select(k => (R + R * Math.Cos(2 * Math.PI * k / n), R + R * Math.Sin(2 * Math.PI * k / n))).ToList();
            W = H = 2 * R;
            ctx.Variaveis["raio"] = R;
        }
        else
        {
            W = a.Num("lx", 0); H = a.Num("ly", 0);
            ctx.Variaveis["raio"] = 0;
            if (W <= 0 || H <= 0) throw new ArgumentException("apoios_estacas: forma retangular sem lx/ly.");
            cont = new List<(double, double)> { (0, 0), (W, 0), (W, H), (0, H) };
        }
        double cx = W / 2, cy = H / 2;
        var est = new List<(double X, double Y)>();
        string arr = a.Txt("arranjo", "grade");
        if (arr == "aneis")
        {
            foreach (int k in new[] { 1, 2 })
            {
                int n = Math.Max(0, a.Int($"anel{k}_n", 0));
                double r = a.Num($"anel{k}_raio", 0), ang = a.Num($"anel{k}_angulo", 0);
                for (int i = 0; i < n; i++)
                {
                    double t = (ang + 360.0 * i / n) * Math.PI / 180;
                    est.Add((cx + r * Math.Cos(t), cy + r * Math.Sin(t)));
                }
            }
            if (a.Bool("central", false)) est.Add((cx, cy));
        }
        else if (arr == "grade")
        {
            int nx = Math.Max(1, a.Int("nx", 2)), ny = Math.Max(1, a.Int("ny", 1));
            double ex = a.Num("esp_x", 120), ey = a.Num("esp_y", 120);
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) est.Add((cx + (i - (nx - 1) / 2.0) * ex, cy + (j - (ny - 1) / 2.0) * ey));
        }
        else if (arr != "nenhum") throw new ArgumentException($"apoios_estacas: arranjo \"{arr}\" não serve com forma dada (grade | aneis | nenhum).");
        ctx.Dados["contorno"] = cont;
        ctx.Dados["estacas"] = est;
        ctx.Dados["arranjo"] = arr;
        ctx.Dados["forma"] = forma;
        ctx.Variaveis["largura_x"] = W;
        ctx.Variaveis["largura_y"] = H;
        ctx.Variaveis["centro_apoios_x"] = est.Count > 0 ? est.Average(p => p.X) : cx;
        ctx.Variaveis["centro_apoios_y"] = est.Count > 0 ? est.Average(p => p.Y) : cy;
        ctx.Variaveis["triangular"] = 0;
        ctx.Variaveis["circular"] = circ ? 1 : 0;
        ctx.Variaveis["n_apoios"] = est.Count;
    }
}

/// <summary>
/// <c>corpo_prismatico</c>: corpo de concreto (ou vazio) = polígono em planta extrudado de z0 a z1. O polígono vem de um
/// dado publicado (<c>contorno</c> ou <c>retangulo</c>) ou de x0, y0, x1, y1. Usado para base, lastro, pilar, viga, laje,
/// parede de reservatório…
/// </summary>
public sealed class CorpoPrismatico : ComponenteBase
{
    public override string Tipo => "corpo_prismatico";
    protected override IEnumerable<(string, string)> Docs => new[]
    {
        ("papel", "texto: papel do corpo (\"bloco\", \"lastro\", \"pilar\", \"colarinho\"…). Obrigatório."),
        ("contorno", "texto: nome do polígono em ctx.Dados (usado se não houver retangulo nem x0)."),
        ("retangulo", "texto: nome de um Retangulo em ctx.Dados (ex.: \"calice_furo\")."),
        ("x0", "cm; com y0, x1, y1: retângulo explícito (tem prioridade)."), ("y0", "cm."), ("x1", "cm."), ("y1", "cm."),
        ("z0", "cm, face inferior. Padrão 0."),
        ("z1", "cm, face superior. Obrigatório."),
        ("vazio", "1 = vazio (subtrai do concreto). Padrão 0."),
    };

    protected override void Executar(ContextoMotor ctx, ArgumentosComponente a)
    {
        List<(double X, double Y)> planta;
        if (a.Tem("x0")) planta = Ret(a.Num("x0", 0), a.Num("y0", 0), a.Num("x1", 0), a.Num("y1", 0));
        else if (a.Tem("retangulo"))
            planta = (Retangulo(ctx, a.Txt("retangulo", "")) ?? throw new InvalidOperationException($"{Tipo}: falta o retângulo \"{a.Txt("retangulo", "")}\".")).Planta();
        else planta = Poligono(ctx, a).ToList();
        ctx.Modelo.Corpos.Add(new CorpoConcreto
        {
            Papel = a.Txt("papel", ""), Planta = planta, Z0 = a.Num("z0", 0), Z1 = a.Num("z1", 0), Vazio = a.Bool("vazio", false),
        });
    }
}

/// <summary><c>corpos_circulares</c>: um cilindro por ponto publicado (estacas, tubulões, furos de dreno…).</summary>
public sealed class CorposCirculares : ComponenteBase
{
    public override string Tipo => "corpos_circulares";
    protected override IEnumerable<(string, string)> Docs => new[]
    {
        ("papel", "texto: papel dos corpos. Padrão \"estaca\"."),
        ("pontos", "texto: nome da lista de pontos em ctx.Dados. Padrão \"estacas\"."),
        ("raio", "cm. Obrigatório."),
        ("z0", "cm."), ("z1", "cm."),
    };

    protected override void Executar(ContextoMotor ctx, ArgumentosComponente a)
    {
        var pts = ctx.Dado<List<(double X, double Y)>>(a.Txt("pontos", "estacas")) ?? new List<(double X, double Y)>();
        foreach (var p in pts)
            ctx.Modelo.Corpos.Add(new CorpoConcreto
            {
                Papel = a.Txt("papel", "estaca"), Centro = p, Raio = a.Num("raio", 0), Z0 = a.Num("z0", 0), Z1 = a.Num("z1", 0),
            });
    }
}

/// <summary><c>niveis</c>: grava níveis nomeados no modelo. Cada argumento numérico (menos "ativo") é um nível, na ordem
/// em que foi passado (ex.: "fundo": 0, "topo_base": "altura_base", "topo": "altura_total").</summary>
public sealed class Niveis : ComponenteBase
{
    public override string Tipo => "niveis";
    protected override IEnumerable<(string, string)> Docs => new[]
    {
        ("<nome>", "cm: cada argumento numérico vira o nível <nome> (\"fundo\", \"topo_base\", \"topo\", \"fundo_pilar\", \"arrasamento\"…)."),
    };

    protected override void Executar(ContextoMotor ctx, ArgumentosComponente a)
    {
        foreach (var kv in a.Numeros) if (kv.Key != "ativo") ctx.Modelo.Niveis[kv.Key] = kv.Value;
    }
}

/// <summary>
/// <c>bitola_efetiva</c>: publica a bitola de uma função JÁ com a sobrescrita (sem consumi-la), para os componentes que
/// precisam dela antes de a posição ser montada (ex.: a lateral recua as barras retas da malha). 0 se a função não
/// existir ou tiver bitola 0; negativo vira 0.
/// </summary>
public sealed class BitolaEfetiva : ComponenteBase
{
    public override string Tipo => "bitola_efetiva";
    /// <summary>O nome depende de "variavel"/"funcao": veja <see cref="VariaveisPublicadasCom"/>.</summary>
    public override IReadOnlyCollection<string> VariaveisPublicadas { get; } = Array.Empty<string>();
    public override IReadOnlyCollection<string> VariaveisPublicadasCom(ArgumentosComponente a) =>
        new[] { a.Txt("variavel", "bitola_" + a.Txt("funcao", "")) };
    protected override IEnumerable<(string, string)> Docs => new[]
    {
        ("funcao", "texto: função da armadura calculada (ex.: \"lateral\"). Obrigatório."),
        ("posicao", "texto: chave da posição (onde a sobrescrita de \"bitola\" seria). Padrão = funcao."),
        ("variavel", "texto: nome da variável publicada (mm). Padrão \"bitola_\" + funcao."),
    };

    protected override void Executar(ContextoMotor ctx, ArgumentosComponente a)
    {
        string f = a.Txt("funcao", "");
        var arm = ctx.Armadura(f);
        double v = arm == null ? 0 : Math.Max(0, ctx.Sobrescrita(a.Txt("posicao", f), "bitola") ?? arm.Bitola);
        ctx.Variaveis[a.Txt("variavel", "bitola_" + f)] = v;
    }
}

/// <summary><c>aviso</c>: acrescenta um aviso fixo ao modelo (use com "ativo" = condição).</summary>
public sealed class Aviso : ComponenteBase
{
    public override string Tipo => "aviso";
    protected override IEnumerable<(string, string)> Docs => new[] { ("texto", "texto do aviso. Obrigatório.") };
    protected override void Executar(ContextoMotor ctx, ArgumentosComponente a)
    {
        string t = a.Txt("texto", "");
        if (t.Length > 0) ctx.Aviso(t);
    }
}
