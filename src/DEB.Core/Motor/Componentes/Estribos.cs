using System;
using System.Collections.Generic;
using System.Linq;
using static DEB.Core.Motor.Componentes.GeometriaPlanta;
using static DEB.Core.Motor.Geometria2D;

namespace DEB.Core.Motor.Componentes;

/// <summary>
/// <c>contorno_lateral</c>: estribos horizontais fechados em volta do contorno (recuado do cobrimento), distribuídos em
/// altura entre z0 + cobrimento e z1 − cobrimento. Lateral do bloco/sapata alta, cinta de borda de laje, armadura de
/// pele horizontal de parede de reservatório. Ganchos 90° no canto superior esquerdo.
/// <para>Consome Dados <c>contorno</c>. Parâmetros: bitola, espacamento, quantidade, gancho.</para>
/// </summary>
public sealed class ContornoLateral : ComponenteBase
{
    public override string Tipo => "contorno_lateral";
    protected override IEnumerable<(string, string)> Docs => new[]
    {
        ("funcao", "texto: função calculada. Padrão \"lateral\"."),
        ("chave", "texto: chave da posição. Padrão = funcao."),
        ("nome", "texto: nome legível. Padrão \"lateral horizontal\"."),
        ("contorno", "texto: polígono em ctx.Dados. Padrão \"contorno\"."),
        ("cobrimento", "cm. Padrão 4."),
        ("z0", "cm: base do trecho. Padrão 0."),
        ("z1", "cm: topo do trecho. Obrigatório."),
        ("regra_quantidade", "texto. Padrão \"(altura − 2·cobrimento) / espaçamento\"."),
        ("regra_contorno", "texto. Padrão \"contorno − cobrimento\"."),
    };

    protected override void Executar(ContextoMotor ctx, ArgumentosComponente a)
    {
        string funcao = a.Txt("funcao", Funcoes.Lateral);
        var arm = ctx.Armadura(funcao);
        if (arm == null) return;
        var contorno = Poligono(ctx, a);
        var p = ctx.NovaPosicao(a.Txt("chave", funcao), funcao, a.Txt("nome", "lateral horizontal"), FormaBarra.Estribo);
        double phi = Math.Max(0, ctx.Par(p, "bitola", arm.Bitola, "cálculo (elemento)"));
        if (phi <= 0) return;
        double cob = a.Num("cobrimento", 4), z0 = a.Num("z0", 0);
        double esp = ctx.Par(p, "espacamento", arm.Espacamento, "cálculo (elemento)");
        double alt = a.Num("z1", 0) - z0 - 2 * cob;
        int n = ctx.ParInt(p, "quantidade", ctx.Quantidade(alt, esp), a.Txt("regra_quantidade", "(altura − 2·cobrimento) / espaçamento"));
        if (n <= 0) return;
        var zs = ctx.Posicoes(alt, n, esp).Select(z => z + cob + z0).ToList();
        var pol = Encolher(contorno, cob);
        p.Regras["contorno"] = a.Txt("regra_contorno", "contorno − cobrimento");
        BarrasUtil.Estribo(ctx, p, pol, phi, zs.Select(BarrasUtil.Horizontal));
        var (cx, cy) = (pol.Average(q => q.X), pol.Average(q => q.Y));
        p.Distribuicao = new Distribuicao { Inicio = new P3(cx, cy, zs[0]), Fim = new P3(cx, cy, zs[zs.Count - 1]), Quantidade = zs.Count, Espacamento = esp };
        ctx.Adicionar(p);
    }
}

/// <summary>
/// <c>estribos_verticais</c>: estribos verticais fechados no plano yz (seção retangular largura × altura − 2·cobrimento),
/// distribuídos ao longo de x; os que caem em [excluir_x0, excluir_x1] (cálice, abertura) são tirados. Estribos da base
/// do bloco; também estribos de viga/viga-baldrame, de sapata corrida.
/// <para>Parâmetros: bitola, espacamento, quantidade (= as que ficaram), largura, altura, gancho.</para>
/// </summary>
public sealed class EstribosVerticais : ComponenteBase
{
    public override string Tipo => "estribos_verticais";
    protected override IEnumerable<(string, string)> Docs => new[]
    {
        ("funcao", "texto: função calculada. Padrão \"estribo_vertical\"."),
        ("chave", "texto: chave da posição. Padrão \"estribo_vertical_x\"."),
        ("nome", "texto: nome legível. Padrão \"estribo vertical\"."),
        ("cobrimento", "cm. Padrão 4."),
        ("x0", "cm: início do trecho em x. Padrão 0."), ("y0", "cm: face y mínima. Padrão 0."), ("z0", "cm: face inferior. Padrão 0."),
        ("direcao", "texto: \"x\" = distribuídos em x, no plano yz; \"y\" = distribuídos em y, no plano xz. Padrão \"x\"."),
        ("comprimento", "cm: extensão na direção da distribuição. Obrigatório."),
        ("largura", "cm: extensão na outra direção em planta (face a face). Obrigatório."),
        ("altura", "cm: extensão em z (face a face). Obrigatório."),
        ("excluir_x0", "cm: início do trecho sem estribos (com excluir_x1). Sem = não exclui."), ("excluir_x1", "cm."),
        ("regra_quantidade", "texto. Padrão \"(comprimento − 2·cobrimento) / espaçamento\"."),
        ("regra_largura", "texto. Padrão \"largura − 2·cobrimento\"."),
        ("regra_altura", "texto. Padrão \"altura − 2·cobrimento\"."),
    };

    protected override void Executar(ContextoMotor ctx, ArgumentosComponente a)
    {
        string funcao = a.Txt("funcao", Funcoes.EstriboVertical);
        var arm = ctx.Armadura(funcao);
        if (arm == null) return;
        var p = ctx.NovaPosicao(a.Txt("chave", "estribo_vertical_x"), funcao, a.Txt("nome", "estribo vertical"), FormaBarra.Estribo);
        double phi = ctx.Par(p, "bitola", arm.Bitola, "cálculo (elemento)");
        double esp = ctx.Par(p, "espacamento", arm.Espacamento, "cálculo (elemento)");
        if (phi <= 0) return;
        double cob = a.Num("cobrimento", 4), x0 = a.Num("x0", 0), y0 = a.Num("y0", 0), z0 = a.Num("z0", 0);
        double W = a.Num("comprimento", 0), H = a.Num("largura", 0), hB = a.Num("altura", 0);
        bool excl = a.Tem("excluir_x0") && a.Tem("excluir_x1");
        double e0 = a.Num("excluir_x0", 0), e1 = a.Num("excluir_x1", 0);
        double vao = W - 2 * cob;
        int n = ctx.ParInt(p, "quantidade", ctx.Quantidade(vao, esp), a.Txt("regra_quantidade", "(comprimento − 2·cobrimento) / espaçamento"));
        var xs = ctx.Posicoes(vao, n, esp).Select(x => x + cob + x0).Where(x => !excl || x < e0 || x > e1).ToList();
        if (xs.Count == 0) return;
        p.Parametros["quantidade"] = xs.Count;
        double w = H - 2 * cob, h = hB - 2 * cob;
        p.Parametros["largura"] = w; p.Regras["largura"] = a.Txt("regra_largura", "largura − 2·cobrimento");
        p.Parametros["altura"] = h; p.Regras["altura"] = a.Txt("regra_altura", "altura − 2·cobrimento");
        bool emY = a.Txt("direcao", "x") == "y";
        // em x: plano yz (u = y, v = z), distribuídos em x; em y: plano xz (u = x, v = z), distribuídos em y (x0/y0 trocam de papel)
        P3 Pt(double s, double u, double v) => emY ? new P3(x0 + cob + u, s, z0 + cob + v) : new P3(s, y0 + cob + u, z0 + cob + v);
        if (emY) xs = xs.Select(s => s - x0 + y0).ToList();
        BarrasUtil.Estribo(ctx, p, Ret(0, 0, w, h), phi, xs.Select(s => (Func<double, double, P3>)((u, v) => Pt(s, u, v))));
        p.Distribuicao = new Distribuicao
        {
            Inicio = Pt(xs[0], w / 2, h / 2), Fim = Pt(xs[xs.Count - 1], w / 2, h / 2), Quantidade = xs.Count, Espacamento = esp,
        };
        ctx.Adicionar(p);
    }
}
