using System;
using System.Collections.Generic;
using System.Linq;
using static DEB.Core.Motor.Componentes.GeometriaPlanta;
using static DEB.Core.Motor.Geometria2D;

namespace DEB.Core.Motor.Componentes;

/// <summary>
/// <c>malha_face</c>: uma direção de malha numa face (inferior ou superior) — barras retas a cada espaçamento no vão
/// face a face − 2·cobrimento, recortadas pelo contorno (VAR no polígono), com pernas e, opcionalmente, interrompidas por
/// uma abertura retangular (colarinho/cálice, furo de laje, poço de reservatório): a barra que cruza a abertura vira dois
/// trechos (os de até <c>trecho_minimo</c> somem). Uma posição por chamada; a malha completa = duas chamadas (0° e 90°).
/// Interrupção só nas direções 0° e 90°.
/// <para>Consome Dados <c>contorno</c> e <c>abertura</c> (Retangulo). Publica o topo da camada em <c>publicar_topo</c>.</para>
/// <para>Parâmetros (aceitam sobrescrita): bitola, espacamento, perna, quantidade, comprimento_reto (sem VAR).</para>
/// </summary>
public sealed class MalhaFace : ComponenteBase
{
    public override string Tipo => "malha_face";
    public override IReadOnlyCollection<string> VariaveisPublicadas { get; } = new[] { "z_topo_inferior" };
    public override IReadOnlyCollection<string> VariaveisPublicadasCom(ArgumentosComponente a) =>
        new[] { a.Txt("publicar_topo", a.Bool("superior", false) ? "" : "z_topo_inferior") }.Where(n => n.Length > 0).ToArray();
    protected override IEnumerable<(string, string)> Docs => new[]
    {
        ("funcao", "texto: função da armadura calculada (bitola, espaçamento). Obrigatório."),
        ("chave", "texto: chave da posição (ex.: \"secundaria_x\"). Padrão = funcao."),
        ("nome", "texto: nome legível. Padrão = chave."),
        ("angulo", "graus: direção das barras em planta (0 = x, 90 = y). Padrão 0."),
        ("contorno", "texto: polígono em ctx.Dados. Padrão \"contorno\"."),
        ("cobrimento", "cm. Padrão 4."),
        ("recuo_lateral", "cm a mais de recuo nas pontas (ø da lateral / 10). Padrão 0."),
        ("z_face", "cm: z da face externa do conjunto (inferior: face de baixo; superior: face de cima). Obrigatório."),
        ("superior", "1 = face de cima (pernas para baixo). Padrão 0."),
        ("camadas", "nº de bitolas próprias entre z_face e esta camada (0 = encostada; 1 = sobre a outra direção). Padrão 0."),
        ("perna", "cm (0 = reta). Padrão 0."),
        ("regra_perna", "texto: regra gravada para a perna. Padrão \"argumento da receita\"."),
        ("abertura", "texto: Retangulo em ctx.Dados que interrompe as barras (vazio = não interrompe). Padrão \"\"."),
        ("folga_abertura", "cm entre a abertura e a ponta da barra interrompida. Padrão = cobrimento."),
        ("trecho_minimo", "cm: trechos interrompidos até este comprimento são descartados. Padrão 10."),
        ("regra_quantidade", "texto: regra gravada para a quantidade. Padrão \"vão (face a face − 2·cobrimento) / espaçamento\"."),
        ("cobrimento_normal", "1 = cobrimento das pontas medido na normal da face (corda do contorno encolhido: face curva ou oblíqua); " +
                              "0 = ao longo da barra (corda − 2·cobrimento; o template antigo do bloco). Padrão 1 (decisão do usuário, 07/11)."),
        ("publicar_topo", "texto: variável com o z da face interna da camada (inferior: máx.; superior: mín.). Padrão \"z_topo_inferior\" (inferior) ou \"\"."),
    };

    protected override void Executar(ContextoMotor ctx, ArgumentosComponente a)
    {
        string funcao = a.Txt("funcao", "");
        var arm = ctx.Armadura(funcao);
        if (arm == null) return;
        var contorno = Poligono(ctx, a);
        string chave = a.Txt("chave", funcao);
        bool sup = a.Bool("superior", false);
        double cob = a.Num("cobrimento", 4), recuo = a.Num("recuo_lateral", 0), zFace = a.Num("z_face", cob);
        int camadas = a.Int("camadas", 0);
        var p = ctx.NovaPosicao(chave, funcao, a.Txt("nome", chave), FormaBarra.U);
        double phi = ctx.Par(p, "bitola", arm.Bitola, "cálculo (elemento)");
        if (phi <= 0) return;
        var f = new FamiliaRetas
        {
            Ang = Graus(a.Num("angulo", 0)), Superior = sup, Bitola = phi, Esp = ctx.Par(p, "espacamento", arm.Espacamento, "cálculo (elemento)"),
            Z = sup ? zFace - phi / 20 - (camadas > 0 ? camadas * phi / 10 : 0) : zFace + phi / 20 + (camadas > 0 ? camadas * phi / 10 : 0),
        };
        f.CobrimentoNormal = a.Bool("cobrimento_normal", true);
        f.Perna = ctx.Par(p, "perna", a.Num("perna", 0), a.Txt("regra_perna", "argumento da receita"));

        double t0 = contorno.Min(pt => Dot(f.N, pt)), t1 = contorno.Max(pt => Dot(f.N, pt));
        double vao = t1 - t0 - 2 * cob;
        int n = ctx.ParInt(p, "quantidade", ctx.Quantidade(vao, f.Esp), a.Txt("regra_quantidade", "vão (face a face − 2·cobrimento) / espaçamento"));
        f.PorFaixa = n;
        var ab = Retangulo(ctx, a.Txt("abertura", ""));
        double folga = a.Num("folga_abertura", cob), minimo = a.Num("trecho_minimo", 10);
        bool emX = Math.Abs(f.Ang) < 1e-6, emY = Math.Abs(f.Ang - Math.PI / 2) < 1e-6;
        foreach (var s in ctx.Posicoes(vao, n, f.Esp))
        {
            double t = t0 + cob + s;
            int antes = f.Linhas.Count;
            f.AdicionarLinha(contorno, cob, recuo, t);
            if (ab == null || !(emX || emY) || f.Linhas.Count == antes) continue;
            var ln = f.Linhas[f.Linhas.Count - 1];
            double coord = emX ? t : -t;   // em x: t = y; em y: N = (−1, 0) → t = −x
            double lo = emX ? ab.Y0 : ab.X0, hi = emX ? ab.Y1 : ab.X1;
            if (coord <= lo - folga || coord >= hi + folga) continue;
            double s0 = emX ? ab.X0 : ab.Y0, s1 = emX ? ab.X1 : ab.Y1;   // ao longo de U: em x, s = x; em y, s = y
            f.Linhas.RemoveAt(f.Linhas.Count - 1);
            if (s0 - folga - ln.A > minimo) f.Linhas.Add(new LinhaReta { T = t, A = ln.A, B = s0 - folga });
            if (ln.B - (s1 + folga) > minimo) f.Linhas.Add(new LinhaReta { T = t, A = s1 + folga, B = ln.B });
        }
        f.Faixas = 1;
        if (f.Linhas.Count == 0) return;
        string pub = a.Txt("publicar_topo", sup ? "" : "z_topo_inferior");
        if (sup) PublicarMin(ctx, pub, f.Z - phi / 20); else PublicarMax(ctx, pub, f.Z + phi / 20);
        f.Montar(ctx, p);
    }
}
