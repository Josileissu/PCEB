using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Modelo;
using DEB.Core.Normas;
using static DEB.Core.Motor.Componentes.GeometriaPlanta;
using static DEB.Core.Motor.Geometria2D;

namespace DEB.Core.Motor.Componentes;

/// <summary>
/// <c>arranque</c>: arranque de pilar moldado in loco — barras de espera no contorno da seção (nx × ny por face, com os
/// cantos), com pé (gancho) no fundo, trecho no elemento e espera acima do topo (traspasse NBR 6118, 100 % emendado, a
/// cada 5 cm, se a espera não for dada) e estribos do pilar dentro do elemento. Bloco, sapata, viga de transição, laje
/// (arranque de pilar que nasce nela), topo de pilar para o lance de cima.
/// <para>Chaves: <c>arranque</c>, <c>arranque_estribo</c> (funções Funcoes.Arranque, ArranqueEstribo). Avisos de lb e de
/// espera manual menor que o traspasse.</para>
/// <para>Parâmetros: bitola, comprimento_interno, espera, pe, quantidade (as barras); bitola, quantidade, espacamento,
/// gancho, largura, altura (estribos).</para>
/// </summary>
public sealed class Arranque : ComponenteBase
{
    public override string Tipo => "arranque";
    protected override IEnumerable<(string, string)> Docs => new[]
    {
        ("centro_x", "cm: centro do pilar. Obrigatório."), ("centro_y", "cm. Obrigatório."),
        ("pilar_bx", "cm. Obrigatório."), ("pilar_by", "cm. Obrigatório."),
        ("nx", "barras por face em x (com os cantos). Padrão 2."), ("ny", "barras por face em y. Padrão 2."),
        ("cobrimento_pilar", "cm. Padrão 3."),
        ("cobrimento", "cm do elemento (o estribo mais alto fica a isto do topo). Padrão 4."),
        ("z_topo", "cm: topo do elemento (início da espera). Obrigatório."),
        ("z_topo_inferior", "cm: topo da armadura inferior (o pé apoia aqui). Padrão = cobrimento."),
        ("regra_comprimento_interno", "texto. Padrão \"topo − topo da armadura inferior\"."),
        ("fck", "MPa (traspasse/lb). Padrão = fck do elemento."),
        ("espera", "cm acima do topo; 0 = traspasse NBR 6118. Padrão 0."),
        ("regra_espera", "texto (quando espera > 0). Padrão \"argumento da receita\"."),
        ("pe", "cm do pé (0 = sem). Padrão 20."),
        ("regra_pe", "texto. Padrão \"argumento da receita\"."),
        ("pe_dentro", "1 = pé para dentro do pilar; 0 = para fora. Padrão 0."),
        ("regra_pe_sentido", "texto. Padrão \"para fora\"/\"para dentro\"."),
    };

    protected override void Executar(ContextoMotor ctx, ArgumentosComponente a)
    {
        var arm = ctx.Armadura(Funcoes.Arranque);
        if (arm == null) return;
        double Px = a.Num("centro_x", 0), Py = a.Num("centro_y", 0), Bx = a.Num("pilar_bx", 0), By = a.Num("pilar_by", 0);
        double hT = a.Num("z_topo", 0), c = a.Num("cobrimento", 4);
        var p = ctx.NovaPosicao("arranque", Funcoes.Arranque, "arranque", FormaBarra.Generica);
        var aE = ctx.Armadura(Funcoes.ArranqueEstribo);
        var pE = aE != null ? ctx.NovaPosicao("arranque_estribo", Funcoes.ArranqueEstribo, "estribo do pilar no bloco", FormaBarra.Estribo) : null;
        double phiA = ctx.Par(p, "bitola", arm.Bitola, "cálculo (elemento)");
        double phiE = pE != null ? ctx.Par(pE, "bitola", aE.Bitola, "cálculo (elemento)") : 0;
        if (phiA <= 0) return;
        double cp = a.Num("cobrimento_pilar", 3);
        double d = cp + phiE / 10 + phiA / 20;
        int ax = Math.Max(1, a.Int("nx", 2)), ay = Math.Max(1, a.Int("ny", 2));
        var pontos = new List<(double X, double Y)>();
        if (ax >= 2 && ay >= 2)
        {
            // contorno da seção pelo cálculo comum (o do pilar e do barras_secao_longitudinal), em ordem x, depois y
            var cobP = new[] { cp, cp, cp, cp };
            pontos = SecaoBarras.Posicoes(Bx, By, cobP, phiE, phiA, 1, 0, phiA, ax - 2, ay - 2, phiA)
                .Select(s => (X: Px - Bx / 2 + s.X, Y: Py - By / 2 + s.Y))
                .OrderBy(q => Math.Round(q.X, 6)).ThenBy(q => Math.Round(q.Y, 6)).ToList();
        }
        else
        {
            // uma fila só (pilar-parede estreito ou barra central): pontos ao longo do eixo
            var xs = new List<double>(); var ys = new List<double>();
            for (int i = 0; i < ax; i++) xs.Add(ax == 1 ? Px : Px - Bx / 2 + d + i * (Bx - 2 * d) / (ax - 1));
            for (int j = 0; j < ay; j++) ys.Add(ay == 1 ? Py : Py - By / 2 + d + j * (By - 2 * d) / (ay - 1));
            for (int i = 0; i < ax; i++) for (int j = 0; j < ay; j++) pontos.Add((xs[i], ys[j]));
        }
        var tr = new RegrasTraspasse { Fck = a.Num("fck", ctx.Elemento.Fck), PercentualEmendado = 100 };
        double interno = ctx.Par(p, "comprimento_interno", Math.Round(hT - a.Num("z_topo_inferior", c)), a.Txt("regra_comprimento_interno", "topo − topo da armadura inferior"));
        double esperaArg = a.Num("espera", 0);
        double traspasse = Math.Ceiling(tr.Comprimento(phiA, ctx.Aco) / 5) * 5;
        double espera = ctx.Par(p, "espera", esperaArg > 0 ? esperaArg : traspasse,
            esperaArg > 0 ? a.Txt("regra_espera", "argumento da receita") : "traspasse NBR 6118 (100 % emendado), a cada 5 cm");
        double pe = ctx.Par(p, "pe", a.Num("pe", 20), a.Txt("regra_pe", "argumento da receita"));
        bool dentro = a.Bool("pe_dentro", false);
        p.Regras["pe_sentido"] = a.Txt("regra_pe_sentido", dentro ? "para dentro" : "para fora");
        p.Parametros["quantidade"] = pontos.Count; p.Regras["quantidade"] = "barras por face (com os cantos) no contorno do pilar";
        double zf = hT - interno, zt = hT + espera;
        foreach (var (x, y) in pontos)
        {
            double nx = Bx > 0 ? (x - Px) / (Bx / 2) : 0, ny = By > 0 ? (y - Py) / (By / 2) : 0;
            double sg = dentro ? 1 : -1;
            var dir = Math.Abs(nx) >= Math.Abs(ny)
                ? (X: (Math.Abs(nx) < 1e-9 ? 1 : Math.Sign(Px - x) * sg), Y: 0.0)
                : (X: 0.0, Y: Math.Sign(Py - y) * sg);
            var pts = new List<P3>();
            if (pe > 0) pts.Add(new P3(x + dir.X * pe, y + dir.Y * pe, zf));
            pts.Add(new P3(x, y, zf));
            pts.Add(new P3(x, y, zt));
            p.Barras.Add(pts);
        }
        p.Bitola = phiA;
        for (int i = 0; i + 1 < p.Barras[0].Count; i++) p.Formatos.Add(FormatoSegmento.Linha);
        p.Distribuicao = new Distribuicao
        {
            Inicio = new P3(pontos[0].X, pontos[0].Y, zf), Fim = new P3(pontos[pontos.Count - 1].X, pontos[pontos.Count - 1].Y, zf), Quantidade = pontos.Count,
        };
        ctx.Adicionar(p);
        // verificações da ancoragem / espera (NBR 6118)
        double lb = Math.Ceiling(tr.Lb(phiA, ctx.Aco));
        if (interno < lb)
            ctx.Aviso($"Arranque: trecho reto no bloco {FormatoMotor.Num(interno)} cm < lb = {FormatoMotor.Num(lb)} cm; com o pé (gancho) a NBR 6118 9.4.2.5 permite 0,7·lb = {FormatoMotor.Num(Math.Ceiling(0.7 * lb))} cm — conferir.");
        if (p.Manuais.Contains("espera") && espera < traspasse)
            ctx.Aviso($"Arranque: espera manual {FormatoMotor.Num(espera)} cm < traspasse da NBR 6118 ({FormatoMotor.Num(traspasse)} cm) — conferir.");

        if (pE == null || phiE <= 0) return;
        int ne = ctx.ParInt(pE, "quantidade", aE.Quantidade, "cálculo (elemento)");
        if (ne <= 0) return;
        double zTop = hT - c - phiE / 20, zBot = Math.Min(zTop, zf + 5);
        double espE = ctx.Par(pE, "espacamento", aE.Espacamento > 0 ? aE.Espacamento : ne > 1 ? Math.Max(0, zTop - zBot) / (ne - 1) : 0,
            aE.Espacamento > 0 ? "cálculo (elemento)" : "do topo − cobrimento até 5 cm acima do pé, dividido igualmente");
        var zs = Enumerable.Range(0, ne).Select(i => zTop - i * espE).ToList();
        var pol = Ret(Px - Bx / 2 + cp, Py - By / 2 + cp, Px + Bx / 2 - cp, Py + By / 2 - cp);
        pE.Parametros["largura"] = Bx - 2 * cp; pE.Regras["largura"] = "pilar − 2·cobrimento do pilar";
        pE.Parametros["altura"] = By - 2 * cp; pE.Regras["altura"] = "pilar − 2·cobrimento do pilar";
        BarrasUtil.Estribo(ctx, pE, pol, phiE, zs.Select(BarrasUtil.Horizontal));
        pE.Distribuicao = new Distribuicao { Inicio = new P3(Px, Py, zs[0]), Fim = new P3(Px, Py, zs[zs.Count - 1]), Quantidade = ne, Espacamento = espE };
        ctx.Adicionar(pE);
    }
}
