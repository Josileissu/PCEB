using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Modelo;
using static DEB.Core.Motor.Geometria2D;

namespace DEB.Core.Motor.Componentes;

/// <summary>
/// <c>borda_u</c>: U de borda em volta do contorno (base de reservatório, radier, sapata): perna vertical junto da face
/// lateral (a cobrimento) e abas horizontais para dentro, de z_inferior a z_superior (faces externas). Contorno circular
/// (Variavel "circular" = 1 do apoios_estacas): U radiais, quantidade = perímetro a cobrimento / espaçamento (padrão do
/// escritório); outro polígono: em cada lado, distribuídos como faixa (CalculoFaixa).
/// <para>Consome Dados "contorno" (e Var "circular", "raio"). Chave: <c>{chave}</c>. Parâmetros (aceitam sobrescrita):
/// bitola, espacamento, aba. Informativos: quantidade, altura.</para>
/// </summary>
public sealed class BordaU : ComponenteBase
{
    public override string Tipo => "borda_u";
    protected override IEnumerable<(string, string)> Docs => new[]
    {
        ("funcao", "texto: função (bitola, espaçamento). Padrão \"borda_u\"."),
        ("chave", "texto. Padrão = funcao."), ("nome", "texto. Padrão \"U de borda\"."),
        ("contorno", "texto: polígono em ctx.Dados. Padrão \"contorno\"."),
        ("cobrimento", "cm na face lateral. Padrão 4."),
        ("z_inferior", "cm: face externa de baixo do U (= cobrimento inferior). Obrigatório."),
        ("z_superior", "cm: face externa de cima do U (= altura − cobrimento superior). Obrigatório."),
        ("aba", "cm: aba horizontal (medida externa). Padrão 100."),
        ("regra_aba", "texto. Padrão \"argumento da receita\"."),
    };

    protected override void Executar(ContextoMotor ctx, ArgumentosComponente a)
    {
        string funcao = a.Txt("funcao", "borda_u");
        var arm = ctx.Armadura(funcao);
        if (arm == null) return;
        var cont = Poligono(ctx, a);
        string chave = a.Txt("chave", funcao);
        var p = ctx.NovaPosicao(chave, funcao, a.Txt("nome", "U de borda"), FormaBarra.U);
        double phi = ctx.Par(p, "bitola", arm.Bitola, "cálculo (elemento)");
        if (phi <= 0) return;
        double esp = ctx.Par(p, "espacamento", arm.Espacamento > 0 ? arm.Espacamento : 20, arm.Espacamento > 0 ? "cálculo (elemento)" : "padrão 20 cm");
        double aba = ctx.Par(p, "aba", a.Num("aba", 100), a.Txt("regra_aba", "argumento da receita"));
        double c = a.Num("cobrimento", 4), zi = a.Num("z_inferior", 0), zs = a.Num("z_superior", 0);
        if (zs - zi <= 2 * phi / 10 || esp <= 0) { ctx.Aviso($"{chave}: altura do U ({FormatoMotor.Num(zs - zi)} cm) ou espaçamento inválido."); return; }
        double r = phi / 20;
        double zt = zs - r, zb = zi + r;
        p.Parametros["altura"] = zs - zi; p.Regras["altura"] = "altura − cobrimentos (medida externa)";

        // pontos na face externa do U (a cobrimento do contorno) e a normal para dentro
        var pontos = new List<((double X, double Y) P, (double X, double Y) N)>();
        bool circ = ctx.Var("circular", 0) > 0.5 && ctx.Var("raio", 0) > 0;
        if (circ)
        {
            double R = ctx.Var("raio", 0) - c;
            var ctr = Centro(cont);
            int n = Math.Max(1, (int)Math.Round(2 * Math.PI * R / esp));
            for (int i = 0; i < n; i++)
            {
                double t = 2 * Math.PI * i / n;
                var u = (Math.Cos(t), Math.Sin(t));
                pontos.Add(((ctr.X + R * u.Item1, ctr.Y + R * u.Item2), (-u.Item1, -u.Item2)));
            }
            p.Regras["quantidade"] = "perímetro a cobrimento / espaçamento";
        }
        else
        {
            var face = Encolher(cont, c);
            for (int k = 0; k < face.Count; k++)
            {
                var A = face[k]; var B = face[(k + 1) % face.Count];
                double L = Dist(A, B);
                if (L < 1e-6) continue;
                var u = Unit((B.X - A.X, B.Y - A.Y));
                var nIn = (-u.Y, u.X);                    // anti-horário: o interior fica à esquerda
                int n = ctx.Quantidade(L, esp);
                foreach (var s in ctx.Posicoes(L, n, esp)) pontos.Add(((A.X + u.X * s, A.Y + u.Y * s), nIn));
            }
            p.Regras["quantidade"] = "em cada lado: comprimento a cobrimento / espaçamento";
        }
        if (pontos.Count == 0) return;
        foreach (var (q, n) in pontos)
        {
            var eixo = (X: q.X + n.X * r, Y: q.Y + n.Y * r);
            var dentro = (X: q.X + n.X * aba, Y: q.Y + n.Y * aba);
            p.Barras.Add(new List<P3>
            {
                new P3(dentro.X, dentro.Y, zt), new P3(eixo.X, eixo.Y, zt), new P3(eixo.X, eixo.Y, zb), new P3(dentro.X, dentro.Y, zb),
            });
        }
        p.Bitola = phi;
        p.Formatos.AddRange(new[] { FormatoSegmento.Linha, FormatoSegmento.Linha, FormatoSegmento.Linha });
        p.Parametros["quantidade"] = pontos.Count;
        var b0 = p.Barras[0]; var b1 = p.Barras[p.Barras.Count - 1];
        p.Distribuicao = new Distribuicao { Inicio = b0[1], Fim = b1[1], Quantidade = pontos.Count, Espacamento = esp };
        ctx.Adicionar(p);
    }
}

/// <summary>
/// <c>barras_borda</c>: barras corridas ao longo das faces, dentro dos U de borda (base retangular): <c>por_face</c>
/// barras retas por lado, de canto a canto a cobrimento, distribuídas na altura entre as abas do U. Contorno circular
/// precisa de barra curva (anel), que o modelo ainda não tem (só trechos retos): o componente avisa e não gera.
/// <para>Consome Dados "contorno" (Var "circular"). Chaves: <c>{chave}_x</c> (lados em x), <c>{chave}_y</c>, ou
/// <c>{chave}_{k}</c> por lado de outro polígono. Parâmetros (aceitam sobrescrita): bitola, por_lado. Informativos: quantidade (total), comprimento_reto.</para>
/// </summary>
public sealed class BarrasBorda : ComponenteBase
{
    public override string Tipo => "barras_borda";
    protected override IEnumerable<(string, string)> Docs => new[]
    {
        ("funcao", "texto: função das barras (bitola; quantidade = por lado). Padrão \"borda\"."),
        ("chave", "texto. Padrão = funcao."), ("nome", "texto. Padrão \"barras de borda\"."),
        ("contorno", "texto: polígono em ctx.Dados. Padrão \"contorno\"."),
        ("cobrimento", "cm na face lateral. Padrão 4."),
        ("bitola_u", "mm do U de borda (as barras ficam por dentro dele). Padrão 0."),
        ("z_inferior", "cm: face externa de baixo do U. Obrigatório."), ("z_superior", "cm: face externa de cima do U. Obrigatório."),
        ("por_face", "barras por lado; 0 = quantidade da função. Padrão 0."),
    };

    protected override void Executar(ContextoMotor ctx, ArgumentosComponente a)
    {
        string funcao = a.Txt("funcao", "borda");
        var arm = ctx.Armadura(funcao);
        if (arm == null) return;
        string chave = a.Txt("chave", funcao);
        if (ctx.Var("circular", 0) > 0.5)
        {
            ctx.Aviso($"{chave}: o anel de borda da base circular precisa de barra curva, que o modelo do motor ainda não tem — não gerado.");
            return;
        }
        var cont = Poligono(ctx, a);
        double c = a.Num("cobrimento", 4), phiU = a.Num("bitola_u", 0), zi = a.Num("z_inferior", 0), zs = a.Num("z_superior", 0);
        int porFaceArg = a.Int("por_face", 0);
        var face = Encolher(cont, c);
        bool ret = face.Count == 4 && Enumerable.Range(0, 4).All(k =>
        {
            var A = face[k]; var B = face[(k + 1) % 4];
            return Math.Abs(A.X - B.X) < 1e-6 || Math.Abs(A.Y - B.Y) < 1e-6;
        });
        var grupos = new Dictionary<string, PosicaoArmadura>();
        for (int k = 0; k < face.Count; k++)
        {
            var A = face[k]; var B = face[(k + 1) % face.Count];
            double L = Dist(A, B);
            if (L < 1e-6) continue;
            var u = Unit((B.X - A.X, B.Y - A.Y));
            var nIn = (X: -u.Y, Y: u.X);
            string k2 = ret ? (Math.Abs(u.Y) < 1e-6 ? "x" : "y") : (k + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!grupos.TryGetValue(k2, out var p))
            {
                p = ctx.NovaPosicao($"{chave}_{k2}", funcao, $"{a.Txt("nome", "barras de borda")} ({k2})", FormaBarra.Reta);
                grupos[k2] = p;
                double phi0 = ctx.Par(p, "bitola", arm.Bitola, "cálculo (elemento)");
                p.Bitola = phi0;
                ctx.ParInt(p, "por_lado", porFaceArg > 0 ? porFaceArg : arm.Quantidade, porFaceArg > 0 ? "argumento da receita" : "cálculo (elemento)");
                p.Parametros["comprimento_reto"] = Math.Round(L);
                p.Regras["comprimento_reto"] = "lado a cobrimento";
            }
            double phi = p.Bitola;
            int q = (int)Math.Round(p.Parametros["por_lado"]);
            if (phi <= 0 || q <= 0) continue;
            double rb = phi / 20, d = phiU / 10 + rb;   // por dentro da perna do U
            double z0 = zi + phiU / 10 + rb, z1 = zs - phiU / 10 - rb;
            for (int j = 0; j < q; j++)
            {
                double z = q == 1 ? (z0 + z1) / 2 : z0 + j * (z1 - z0) / (q - 1);
                // pontas a cobrimento das faces vizinhas (corridas de canto a canto)
                p.Barras.Add(new List<P3>
                {
                    new P3(A.X + nIn.X * d, A.Y + nIn.Y * d, z), new P3(B.X + nIn.X * d, B.Y + nIn.Y * d, z),
                });
            }
        }
        foreach (var p in grupos.Values.Where(x => x.Barras.Count > 0))
        {
            p.Formatos.Add(FormatoSegmento.Linha);
            p.Parametros["quantidade"] = p.Barras.Count;
            p.Regras["quantidade"] = "barras por lado × lados";
            p.Distribuicao = new Distribuicao { Inicio = p.Barras[0][0], Fim = p.Barras[p.Barras.Count - 1][0], Quantidade = p.Barras.Count };
            ctx.Adicionar(p);
        }
    }
}
