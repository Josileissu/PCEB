using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Modelo;
using static DEB.Core.Motor.Componentes.GeometriaPlanta;

namespace DEB.Core.Motor.Componentes;

/// <summary>
/// <c>calice_forma</c>: geometria do cálice de um pilar pré-moldado (abertura = pilar + 2·folga; paredes de espessura
/// <c>parede</c>), com colarinho acima da base (limitado ao contorno, como no B28) ou embutido nela. Acrescenta os corpos
/// "colarinho", "furo_calice" (vazio) e "regularizacao". Serve a bloco, sapata e qualquer fundação com cálice.
/// <para>Publica Dados "calice_furo" (Retangulo da abertura), "calice_colarinho" (Retangulo das faces externas das
/// paredes; limitado ao contorno se houver colarinho), "abertura" (o que interrompe a malha superior: colarinho, se houver,
/// senão o furo); Variaveis "z_fundo_pilar" (z_topo − embutimento) e "z_fundo_calice" (− regularização).</para>
/// </summary>
public sealed class CaliceForma : ComponenteBase
{
    public override string Tipo => "calice_forma";
    public override IReadOnlyCollection<string> VariaveisPublicadas { get; } = new[] { "z_fundo_pilar", "z_fundo_calice",
        "calice_colarinho_x0", "calice_colarinho_x1", "calice_colarinho_y0", "calice_colarinho_y1" };
    protected override IEnumerable<(string, string)> Docs => new[]
    {
        ("centro_x", "cm: centro do pilar em x. Obrigatório."), ("centro_y", "cm: centro do pilar em y. Obrigatório."),
        ("pilar_bx", "cm: dimensão do pilar em x. Obrigatório."), ("pilar_by", "cm: em y. Obrigatório."),
        ("folga", "cm entre o pilar e a parede do cálice. Padrão 4."),
        ("parede", "cm: espessura da parede do cálice. Padrão 21."),
        ("colarinho", "1 = colarinho acima da base; 0 = embutido. Padrão 1."),
        ("z_base_colarinho", "cm: base do colarinho (topo da base). Obrigatório com colarinho."),
        ("z_topo", "cm: topo do elemento (topo do colarinho, ou da base se embutido). Obrigatório."),
        ("embutimento", "cm: topo → base do pilar (Lemb). Obrigatório."),
        ("regularizacao", "cm de concreto de regularização sob o pilar. Padrão 0."),
        ("contorno", "texto: polígono que limita o colarinho. Padrão \"contorno\" (sem o dado = não limita)."),
        ("corpos", "1 = acrescenta os corpos de concreto. Padrão 1."),
    };

    protected override void Executar(ContextoMotor ctx, ArgumentosComponente a)
    {
        double px = a.Num("centro_x", 0), py = a.Num("centro_y", 0), folga = a.Num("folga", 4), t = a.Num("parede", 21);
        bool colar = a.Bool("colarinho", true);
        double ax = a.Num("pilar_bx", 0) / 2 + folga, ay = a.Num("pilar_by", 0) / 2 + folga;
        var o = new Retangulo(px - ax, py - ay, px + ax, py + ay);
        var cr = new Retangulo(o.X0 - t, o.Y0 - t, o.X1 + t, o.Y1 + t);
        var cont = ctx.Dado<List<(double X, double Y)>>(a.Txt("contorno", "contorno"));
        if (colar && cont != null)
        {
            // colarinho limitado ao contorno (B28: largura do colarinho = largura do bloco)
            cr.X0 = Math.Max(cr.X0, cont.Min(p => p.X)); cr.Y0 = Math.Max(cr.Y0, cont.Min(p => p.Y));
            cr.X1 = Math.Min(cr.X1, cont.Max(p => p.X)); cr.Y1 = Math.Min(cr.Y1, cont.Max(p => p.Y));
        }
        ctx.Dados["calice_furo"] = o;
        ctx.Dados["calice_colarinho"] = cr;
        ctx.Dados["abertura"] = colar ? cr : o;
        double hT = a.Num("z_topo", 0), hB = a.Num("z_base_colarinho", hT), reg = Math.Max(0, a.Num("regularizacao", 0));
        double zP = hT - a.Num("embutimento", 0), zF = zP - reg;
        ctx.Variaveis["z_fundo_pilar"] = zP;
        ctx.Variaveis["z_fundo_calice"] = zF;
        // limites do colarinho (ou da parede do cálice embutido) para as receitas (ex.: trecho sem estribos verticais)
        ctx.Variaveis["calice_colarinho_x0"] = cr.X0; ctx.Variaveis["calice_colarinho_x1"] = cr.X1;
        ctx.Variaveis["calice_colarinho_y0"] = cr.Y0; ctx.Variaveis["calice_colarinho_y1"] = cr.Y1;
        if (!a.Bool("corpos", true)) return;
        var cs = ctx.Modelo.Corpos;
        if (colar && hT > hB) cs.Add(new CorpoConcreto { Papel = "colarinho", Planta = cr.Planta(), Z0 = hB, Z1 = hT });
        cs.Add(new CorpoConcreto { Papel = "furo_calice", Planta = o.Planta(), Z0 = zF, Z1 = hT, Vazio = true });
        if (reg > 0) cs.Add(new CorpoConcreto { Papel = "regularizacao", Planta = o.Planta(), Z0 = zF, Z1 = zP });
    }
}

/// <summary>
/// <c>calice_armaduras</c>: armaduras do cálice (NBR 9062 7.7): horizontais do topo (concentradas) e as restantes até a
/// base do pilar, em estribo nas paredes, com grampos em x e em y; verticais em grampo nas 4 paredes (do topo até acima da
/// armadura inferior); suspensão (desce numa parede, passa sob o pilar e sobe na oposta, plano xz); barras retas sob a
/// base do pilar (fundo). Chaves fixas do vocabulário do cálice: calice_topo, calice_grampos_topo_x/y, calice,
/// calice_grampos_x/y, calice_vertical, suspensao, fundo_calice (funções Funcoes.CaliceTopo, Calice, CaliceVertical,
/// Suspensao, FundoCalice).
/// <para>Consome Dados "calice_furo" e "calice_colarinho" (de <c>calice_forma</c>).</para>
/// </summary>
public sealed class CaliceArmaduras : ComponenteBase
{
    public override string Tipo => "calice_armaduras";
    protected override IEnumerable<(string, string)> Docs => new[]
    {
        ("colarinho", "1 = paredes = colarinho (limitado); 0 = embutido (paredes = furo + 2·parede). Padrão 1."),
        ("cobrimento", "cm (faces externas). Padrão 4."),
        ("cobrimento_calice", "cm (face interna do cálice). Padrão 2."),
        ("parede", "cm: espessura da parede. Padrão 21."),
        ("centro_x", "cm: centro do pilar (suspensão e fundo). Obrigatório."), ("centro_y", "cm. Obrigatório."),
        ("z_topo", "cm: topo do cálice. Obrigatório."),
        ("embutimento", "cm: topo → base do pilar. Obrigatório."),
        ("regularizacao", "cm. Padrão 0."),
        ("z_topo_inferior", "cm: topo da armadura inferior (as verticais e a suspensão descem até aqui). Padrão = cobrimento."),
        ("grampos", "1 = grampos nas horizontais. Padrão 1."),
        ("desconto_perna_externa", "cm: perna externa da suspensão = perna − isto. Padrão 6."),
        ("regra_desconto", "texto. Padrão \"perna − desconto\"."),
        ("comprimento_fundo", "cm: comprimento das barras sob o pilar. Padrão 100."),
        ("regra_comprimento_fundo", "texto. Padrão \"argumento da receita\"."),
    };

    protected override void Executar(ContextoMotor ctx, ArgumentosComponente a)
    {
        var O = ctx.Dado<Retangulo>("calice_furo") ?? throw new InvalidOperationException("calice_armaduras: falta \"calice_furo\" (rode calice_forma antes).");
        var C = ctx.Dado<Retangulo>("calice_colarinho") ?? throw new InvalidOperationException("calice_armaduras: falta \"calice_colarinho\".");
        bool colar = a.Bool("colarinho", true);
        double cb = a.Num("cobrimento", 4), cc = a.Num("cobrimento_calice", 2), t = a.Num("parede", 21);
        double Px = a.Num("centro_x", 0), Py = a.Num("centro_y", 0), hT = a.Num("z_topo", 0), Lemb = a.Num("embutimento", 0);
        double reg = Math.Max(0, a.Num("regularizacao", 0));
        double wF = colar ? C.X1 - C.X0 : O.X1 - O.X0 + 2 * t, hF = colar ? C.Y1 - C.Y0 : O.Y1 - O.Y0 + 2 * t;
        double xw0 = colar ? C.X0 : O.X0 - t, yw0 = colar ? C.Y0 : O.Y0 - t;
        double w = wF - 2 * cb, h = hF - 2 * cb;
        var aT = ctx.Armadura(Funcoes.CaliceTopo); var aR = ctx.Armadura(Funcoes.Calice);
        var pT = aT != null ? ctx.NovaPosicao("calice_topo", Funcoes.CaliceTopo, "cálice: horizontais do topo", FormaBarra.Estribo) : null;
        var pR = aR != null ? ctx.NovaPosicao("calice", Funcoes.Calice, "cálice: horizontais", FormaBarra.Estribo) : null;
        double phiT = pT != null ? ctx.Par(pT, "bitola", aT.Bitola, "cálculo (elemento)") : 0;
        double phiR = pR != null ? ctx.Par(pR, "bitola", aR.Bitola, "cálculo (elemento)") : 0;
        int nT = pT != null && phiT > 0 ? ctx.ParInt(pT, "quantidade", aT.Quantidade, "cálculo (concentradas no topo, NBR 9062 7.7.4.3)") : 0;
        double espT = pT != null ? ctx.Par(pT, "espacamento", aT.Espacamento, "cálculo (elemento)") : 0;
        double zTopo = hT - cb - Math.Max(phiT, phiR) / 20;
        var zT = new List<double>();
        for (int i = 0; i < nT; i++) zT.Add(zTopo - i * espT);
        var zR = new List<double>();
        double espR = 0;
        if (pR != null && phiR > 0)
        {
            espR = ctx.Par(pR, "espacamento", aR.Espacamento, "cálculo (elemento)");
            double zIni = nT > 0 ? zT[zT.Count - 1] - espR : zTopo, zFim = hT - Lemb;
            if (zIni > zFim)
            {
                int n = ctx.ParInt(pR, "quantidade", ctx.Quantidade(zIni - zFim, espR), "do topo concentrado até a base do pilar / espaçamento");
                zR = ctx.Posicoes(zIni - zFim, n, espR).Select(z => zIni - z).ToList();
            }
        }
        bool gr = a.Bool("grampos", true);
        var anel = Ret(xw0 + cb, yw0 + cb, xw0 + cb + w, yw0 + cb + h);
        var (acx, acy) = (xw0 + wF / 2, yw0 + hF / 2);
        foreach (var (p, phi, zs, esp, suf) in new[] { (pT, phiT, zT, espT, "_topo"), (pR, phiR, zR, espR, "") })
        {
            if (p == null || zs.Count == 0) continue;
            p.Parametros["largura"] = w; p.Regras["largura"] = "colarinho − 2·cobrimento";
            p.Parametros["altura"] = h; p.Regras["altura"] = "colarinho − 2·cobrimento";
            BarrasUtil.Estribo(ctx, p, anel, phi, zs.Select(BarrasUtil.Horizontal));
            p.Distribuicao = new Distribuicao { Inicio = new P3(acx, acy, zs[0]), Fim = new P3(acx, acy, zs[zs.Count - 1]), Quantidade = zs.Count, Espacamento = esp };
            ctx.Adicionar(p);
            if (!gr) continue;
            foreach (bool emX in new[] { true, false })
            {
                string chave = "calice_grampos" + suf + (emX ? "_x" : "_y");
                var g = ctx.NovaPosicao(chave, p.Funcao, "cálice: grampos" + (suf.Length > 0 ? " do topo" : "") + (emX ? " (x)" : " (y)"), FormaBarra.U);
                double phiG = ctx.Par(g, "bitola", phi, "= horizontais" + (suf.Length > 0 ? " do topo" : ""));
                double pg = ctx.Par(g, "perna", Math.Round(t - cb - cc), "parede − cobrimento − cobrimento do cálice");
                double L = ctx.Par(g, "comprimento_reto", emX ? w : h, "colarinho − 2·cobrimento");
                if (phiG <= 0) continue;
                g.Parametros["quantidade"] = zs.Count; g.Parametros["espacamento"] = esp;
                double x0 = xw0 + cb, y0 = yw0 + cb, x1 = x0 + w, y1 = y0 + h;
                foreach (int lado in new[] { 0, 1 })
                    foreach (var z in zs)
                    {
                        List<P3> pts;
                        if (emX)
                        {
                            double xa = acx - L / 2, xb = acx + L / 2, yy = lado == 0 ? y0 : y1, sg = lado == 0 ? 1 : -1;
                            pts = new List<P3> { new P3(xa, yy + sg * pg, z), new P3(xa, yy, z), new P3(xb, yy, z), new P3(xb, yy + sg * pg, z) };
                        }
                        else
                        {
                            double ya = acy - L / 2, yb = acy + L / 2, xx = lado == 0 ? x0 : x1, sg = lado == 0 ? 1 : -1;
                            pts = new List<P3> { new P3(xx + sg * pg, ya, z), new P3(xx, ya, z), new P3(xx, yb, z), new P3(xx + sg * pg, yb, z) };
                        }
                        g.Barras.Add(pts);
                    }
                g.Bitola = phiG;
                g.Formatos.AddRange(new[] { FormatoSegmento.Linha, FormatoSegmento.Linha, FormatoSegmento.Linha });
                var ref0 = emX ? new P3(acx, y0, 0) : new P3(x0, acy, 0);
                g.Distribuicao = new Distribuicao
                {
                    Inicio = new P3(ref0.X, ref0.Y, zs[0]), Fim = new P3(ref0.X, ref0.Y, zs[zs.Count - 1]), Quantidade = zs.Count, Espacamento = esp, Repeticoes = 2,
                };
                ctx.Adicionar(g);
            }
        }

        // verticais em grampo nas paredes: da face de cima até acima da armadura inferior
        double zPrinc = a.Num("z_topo_inferior", cb);
        double phiMax = Math.Max(phiT, phiR);
        double altCalc = Math.Round(hT - cb - zPrinc), largCalc = Math.Max(4, Math.Round(t - cb - cc - 2 * phiMax / 10));
        var aV = ctx.Armadura(Funcoes.CaliceVertical);
        if (aV != null)
        {
            var p = ctx.NovaPosicao("calice_vertical", Funcoes.CaliceVertical, "cálice: verticais", FormaBarra.U);
            double phiV = ctx.Par(p, "bitola", aV.Bitola, "cálculo (elemento)");
            int nV = ctx.ParInt(p, "quantidade", aV.Quantidade, "cálculo: por parede");
            double larg = ctx.Par(p, "largura", largCalc, "parede − cobrimento − cobrimento do cálice − 2·ø horizontais");
            double alt = ctx.Par(p, "altura", altCalc, "topo − cobrimento − topo da armadura inferior");
            if (phiV > 0 && nV > 0)
            {
                double g = ctx.Par(p, "gancho", Math.Round(ctx.Cfg.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho90, phiV, ctx.Aco, false)), "gancho 90° (NBR 6118 tab. 9.1)");
                double zt = hT - cb, zb = zt - alt;
                // paredes: y0 (de baixo), y1, x0 (esquerda), x1 — (ponto da face externa, direção para dentro, tangente)
                var paredes = new[]
                {
                    (Ext: (X: 0.0, Y: yw0 + cb), In: (X: 0.0, Y: 1.0), Tan: (X: 1.0, Y: 0.0), Ini: xw0, Comp: wF),
                    (Ext: (X: 0.0, Y: yw0 + hF - cb), In: (X: 0.0, Y: -1.0), Tan: (X: 1.0, Y: 0.0), Ini: xw0, Comp: wF),
                    (Ext: (X: xw0 + cb, Y: 0.0), In: (X: 1.0, Y: 0.0), Tan: (X: 0.0, Y: 1.0), Ini: yw0, Comp: hF),
                    (Ext: (X: xw0 + wF - cb, Y: 0.0), In: (X: -1.0, Y: 0.0), Tan: (X: 0.0, Y: 1.0), Ini: yw0, Comp: hF),
                };
                foreach (var pw in paredes)
                    for (int i = 0; i < nV; i++)
                    {
                        double s = pw.Ini + pw.Comp * (i + 1.0) / (nV + 1);
                        var o = (X: pw.Ext.X + pw.Tan.X * s, Y: pw.Ext.Y + pw.Tan.Y * s);   // face externa (a coordenada tangente vem de s)
                        var q = (X: o.X + pw.In.X * larg, Y: o.Y + pw.In.Y * larg);
                        p.Barras.Add(new List<P3>
                        {
                            new P3(o.X + pw.Tan.X * g, o.Y + pw.Tan.Y * g, zt), new P3(o.X, o.Y, zt), new P3(o.X, o.Y, zb),
                            new P3(q.X, q.Y, zb), new P3(q.X, q.Y, zt), new P3(q.X + pw.Tan.X * g, q.Y + pw.Tan.Y * g, zt),
                        });
                    }
                p.Bitola = phiV;
                for (int i = 0; i < 5; i++) p.Formatos.Add(FormatoSegmento.Linha);
                double espV = wF / (nV + 1);
                p.Distribuicao = new Distribuicao
                {
                    Inicio = BarrasUtil.Centro(p.Barras[0]), Fim = BarrasUtil.Centro(p.Barras[nV - 1]), Quantidade = nV, Espacamento = espV, Repeticoes = 4,
                };
                p.Parametros["espacamento"] = espV; p.Regras["espacamento"] = "parede dividida em quantidade + 1";
                ctx.Adicionar(p);
            }
        }

        // suspensão (NBR 9062 7.7.3.7): desce numa parede, passa sob o pilar e sobe na oposta (plano xz)
        var aS = ctx.Armadura(Funcoes.Suspensao);
        if (aS != null)
        {
            var p = ctx.NovaPosicao("suspensao", Funcoes.Suspensao, "suspensão", FormaBarra.Generica);
            double phiS = ctx.Par(p, "bitola", aS.Bitola, "cálculo (elemento)");
            int nS = ctx.ParInt(p, "quantidade", aS.Quantidade, "cálculo (elemento)");
            double espS = ctx.Par(p, "espacamento", aS.Espacamento, "cálculo (elemento)");
            double fundo = ctx.Par(p, "largura_fundo", Math.Round(O.X1 - O.X0 + 2 * cb), "abertura do cálice + 2·cobrimento");
            double larg = ctx.Par(p, "aba", largCalc, "= largura das verticais do cálice");
            double hi = ctx.Par(p, "perna", altCalc, "topo − cobrimento − topo da armadura inferior");
            double ho = ctx.Par(p, "perna_externa", hi - a.Num("desconto_perna_externa", 6), a.Txt("regra_desconto", "perna − desconto"));
            if (phiS > 0 && nS > 0)
            {
                double xi0 = O.X0 - cb, xi1 = xi0 + fundo, zt = hT - cb, zb = zt - hi, zo = zt - ho;
                for (int i = 0; i < nS; i++)
                {
                    double y = Py + (i - (nS - 1) / 2.0) * espS;
                    p.Barras.Add(new List<P3>
                    {
                        new P3(xi0 - larg, y, zo), new P3(xi0 - larg, y, zt), new P3(xi0, y, zt), new P3(xi0, y, zb),
                        new P3(xi1, y, zb), new P3(xi1, y, zt), new P3(xi1 + larg, y, zt), new P3(xi1 + larg, y, zo),
                    });
                }
                p.Bitola = phiS;
                for (int i = 0; i < 7; i++) p.Formatos.Add(FormatoSegmento.Linha);
                p.Distribuicao = new Distribuicao
                {
                    Inicio = new P3((xi0 + xi1) / 2, p.Barras[0][0].Y, zb), Fim = new P3((xi0 + xi1) / 2, p.Barras[nS - 1][0].Y, zb), Quantidade = nS, Espacamento = espS,
                };
                if (ho > hi) ctx.Aviso("suspensao: perna externa maior que a interna — conferir.");
                ctx.Adicionar(p);
            }
        }

        // barras retas sob a base do pilar (fundo do cálice), em x, distribuídas em y
        var aF = ctx.Armadura(Funcoes.FundoCalice);
        if (aF != null)
        {
            var p = ctx.NovaPosicao("fundo_calice", Funcoes.FundoCalice, "sob a base do pilar", FormaBarra.Reta);
            double phiF = ctx.Par(p, "bitola", aF.Bitola, "cálculo (elemento)");
            int nF = ctx.ParInt(p, "quantidade", aF.Quantidade, "cálculo (elemento)");
            double L = ctx.Par(p, "comprimento", a.Num("comprimento_fundo", 100), a.Txt("regra_comprimento_fundo", "argumento da receita"));
            double espF = ctx.Par(p, "espacamento", aF.Espacamento > 0 ? aF.Espacamento : nF > 1 ? (hF - 2 * cb) / (nF - 1) : 0,
                aF.Espacamento > 0 ? "cálculo (elemento)" : "largura do cálice − 2·cobrimento dividida igualmente");
            if (phiF > 0 && nF > 0)
            {
                double z = hT - Lemb - reg - cb;
                for (int i = 0; i < nF; i++)
                {
                    double y = Py + (i - (nF - 1) / 2.0) * espF;
                    p.Barras.Add(new List<P3> { new P3(Px - L / 2, y, z), new P3(Px + L / 2, y, z) });
                }
                p.Bitola = phiF;
                p.Formatos.Add(FormatoSegmento.Linha);
                p.Distribuicao = new Distribuicao
                {
                    Inicio = new P3(Px, p.Barras[0][0].Y, z), Fim = new P3(Px, p.Barras[nF - 1][0].Y, z), Quantidade = nF, Espacamento = espF,
                };
                ctx.Adicionar(p);
            }
        }
    }
}
