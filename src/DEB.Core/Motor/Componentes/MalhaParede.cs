using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Modelo;
using DEB.Core.Normas;

namespace DEB.Core.Motor.Componentes;

/// <summary>
/// <c>malha_parede</c>: armadura de uma parede (reservatório, colarinho, cortina, consolo largo) — horizontais junto das
/// faces e verticais por dentro delas, em uma ou nas duas faces; pé nas verticais; U de fechamento no topo e nas pontas
/// livres; nas pontas em canto ou encontro, as horizontais vão até a face de fora da parede vizinha (a cobrimento) e dobram
/// em L ao longo dela.
/// <para>Geometria: eixo da parede em planta de (x0, y0) a (x1, y1) — nas pontas em canto/encontro, o ponto é o cruzamento
/// dos eixos; na ponta livre, a face da ponta. Face "a" = lado esquerdo de quem anda de 0 para 1 (+n); "b" = o outro.</para>
/// <para>Chaves: <c>{chave}_horizontal_a/_b</c>, <c>{chave}_vertical_a/_b</c>, <c>{chave}_fechamento_topo</c>,
/// <c>{chave}_fechamento_inicio/_fim</c>. Parâmetros (aceitam sobrescrita): bitola e espacamento (todas), quantidade
/// (horizontais e verticais), pe (verticais), perna_canto_inicio/_fim (horizontais), perna (fechamentos).</para>
/// <para>Primeira versão (2026-11-07): o detalhe dos cantos e encontros ainda vai ser trabalhado com o usuário.</para>
/// </summary>
public sealed class MalhaParede : ComponenteBase
{
    public override string Tipo => "malha_parede";
    protected override IEnumerable<(string, string)> Docs => new[]
    {
        ("chave", "texto: prefixo das chaves. Padrão \"parede\"."),
        ("nome", "texto: nome legível. Padrão = chave."),
        ("funcao_horizontal", "texto: função das horizontais (bitola, espaçamento). Padrão \"parede_horizontal\"."),
        ("funcao_vertical", "texto: função das verticais. Padrão \"parede_vertical\"."),
        ("funcao_fechamento", "texto: função dos U de fechamento (bitola; espaçamento = o das barras que fecham). Padrão = funcao_horizontal."),
        ("x0", "cm: início do eixo. Obrigatório."), ("y0", "cm."), ("x1", "cm: fim do eixo. Obrigatório."), ("y1", "cm."),
        ("espessura", "cm. Obrigatório."), ("z0", "cm: base da parede. Obrigatório."), ("z1", "cm: topo. Obrigatório."),
        ("cobrimento", "cm nas faces. Padrão 3."),
        ("cobrimento_topo", "cm. Padrão = cobrimento."), ("cobrimento_base", "cm. Padrão = cobrimento."),
        ("faces", "2 = as duas faces; 1 = só a face a. Padrão 2."),
        ("pe", "cm: pé das verticais na base (0 = sem). Padrão 0."),
        ("pe_sentido", "texto: \"fora\" (para fora da parede) ou \"dentro\" (para a outra face). Padrão \"fora\"."),
        ("z_pe", "cm: z do pé (padrão = base da parede + cobrimento_base)."),
        ("fechamento_topo", "1 = U de fechamento no topo, abraçando as verticais das duas faces. Padrão 1 (com 2 faces)."),
        ("extremo_inicio", "texto: \"livre\" (U de fechamento), \"canto\" (L) ou \"encontro\" (T). Padrão \"livre\"."),
        ("extremo_fim", "texto: idem. Padrão \"livre\"."),
        ("lado_canto_inicio", "+1 = a parede vizinha sai para o lado da face a; −1 = da face b. Padrão 1."),
        ("lado_canto_fim", "idem. Padrão 1."),
        ("espessura_vizinha", "cm: espessura da parede vizinha (canto/encontro). Padrão = espessura."),
        ("perna_canto", "cm: perna do L no canto/encontro; 0 = traspasse NBR 6118 (100 %), a cada 5 cm. Padrão 0."),
        ("perna_fechamento", "cm: perna dos U de fechamento; 0 = traspasse NBR 6118 (100 %). Padrão 0."),
        ("fck", "MPa (traspasse). Padrão = fck do elemento."),
    };

    private sealed class Parede
    {
        public double X0, Y0, Ux, Uy, Nx, Ny, L, T, Z0, Z1;
        public P3 Pt(double s, double d, double z) => new P3(X0 + Ux * s + Nx * d, Y0 + Uy * s + Ny * d, z);
    }

    protected override void Executar(ContextoMotor ctx, ArgumentosComponente a)
    {
        string chave = a.Txt("chave", "parede"), nome = a.Txt("nome", chave);
        string fH = a.Txt("funcao_horizontal", "parede_horizontal"), fV = a.Txt("funcao_vertical", "parede_vertical");
        var armH = ctx.Armadura(fH); var armV = ctx.Armadura(fV);
        if (armH == null && armV == null) return;
        double x0 = a.Num("x0", 0), y0 = a.Num("y0", 0), x1 = a.Num("x1", 0), y1 = a.Num("y1", 0);
        double L = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        double t = a.Num("espessura", 0), z0 = a.Num("z0", 0), z1 = a.Num("z1", 0);
        if (L < 1 || t <= 0 || z1 - z0 <= 0) { ctx.Aviso($"{Tipo} ({chave}): eixo, espessura ou altura inválidos."); return; }
        var w = new Parede { X0 = x0, Y0 = y0, Ux = (x1 - x0) / L, Uy = (y1 - y0) / L, L = L, T = t, Z0 = z0, Z1 = z1 };
        w.Nx = -w.Uy; w.Ny = w.Ux;
        double c = a.Num("cobrimento", 3), ct = a.Num("cobrimento_topo", c), cb = a.Num("cobrimento_base", c);
        bool duas = a.Int("faces", 2) >= 2;
        double fck = a.Num("fck", ctx.Elemento.Fck);
        var tr = new RegrasTraspasse { Fck = fck, PercentualEmendado = 100 };
        double Traspasse(double phi) => Math.Ceiling(tr.Comprimento(phi, ctx.Aco) / 5) * 5;

        string[] faces = duas ? new[] { "a", "b" } : new[] { "a" };
        double Sinal(string f) => f == "a" ? 1 : -1;

        // bitolas (com sobrescrita) antes da geometria: as verticais ficam por dentro das horizontais
        double phiH = armH == null ? 0 : ctx.Sobrescrita($"{chave}_horizontal_a", "bitola") ?? armH.Bitola;
        double phiV = armV == null ? 0 : ctx.Sobrescrita($"{chave}_vertical_a", "bitola") ?? armV.Bitola;
        double dH = t / 2 - c - phiH / 20;               // eixo das horizontais (distância ao eixo da parede)
        double dV = t / 2 - c - phiH / 10 - phiV / 20;   // eixo das verticais

        string eIni = a.Txt("extremo_inicio", "livre"), eFim = a.Txt("extremo_fim", "livre");
        foreach (var e in new[] { eIni, eFim })
            if (e != "livre" && e != "canto" && e != "encontro")
                throw new ArgumentException($"{Tipo}: extremo \"{e}\" desconhecido (livre | canto | encontro).");
        double tViz = a.Num("espessura_vizinha", t);
        // alcance das horizontais em s: livre = a face da ponta − c; canto/encontro = face de fora da vizinha − c
        double sIni = eIni == "livre" ? c : -tViz / 2 + c;
        double sFim = eFim == "livre" ? L - c : L + tViz / 2 - c;

        // ---------------- horizontais ----------------
        if (armH != null && phiH > 0)
        {
            foreach (var f in faces)
            {
                var p = ctx.NovaPosicao($"{chave}_horizontal_{f}", fH, $"{nome}: horizontais (face {f})", FormaBarra.Reta);
                double phi = ctx.Par(p, "bitola", armH.Bitola, "cálculo (elemento)");
                double esp = ctx.Par(p, "espacamento", armH.Espacamento > 0 ? armH.Espacamento : 15, armH.Espacamento > 0 ? "cálculo (elemento)" : "padrão 15 cm");
                double zA = z0 + cb + phi / 20, zB = z1 - ct - phi / 20;
                int n = ctx.ParInt(p, "quantidade", ctx.Quantidade(zB - zA, esp), "altura − cobrimentos / espaçamento");
                if (n <= 0) continue;
                double lap = Traspasse(phi);
                double pIni = eIni == "livre" ? 0 : ctx.Par(p, "perna_canto_inicio", a.Num("perna_canto", 0) > 0 ? a.Num("perna_canto", 0) : lap,
                    a.Num("perna_canto", 0) > 0 ? "argumento da receita" : "traspasse NBR 6118 (100 %), a cada 5 cm");
                double pFim = eFim == "livre" ? 0 : ctx.Par(p, "perna_canto_fim", a.Num("perna_canto", 0) > 0 ? a.Num("perna_canto", 0) : lap,
                    a.Num("perna_canto", 0) > 0 ? "argumento da receita" : "traspasse NBR 6118 (100 %), a cada 5 cm");
                // sentido da perna: canto = para o lado da vizinha; encontro = cada face para o seu lado
                double LadoPerna(string ext, int lado) => ext == "encontro" ? Sinal(f) : lado;
                int ladoIni = a.Int("lado_canto_inicio", 1) >= 0 ? 1 : -1, ladoFim = a.Int("lado_canto_fim", 1) >= 0 ? 1 : -1;
                double d = Sinal(f) * dH;
                // perna no plano horizontal, ao longo da face de fora da vizinha (recuada do eixo pela espessura da barra)
                double sPI = sIni + phi / 20, sPF = sFim - phi / 20;
                foreach (var z in ctx.Posicoes(zB - zA, n, esp).Select(s => zA + s))
                {
                    var pts = new List<P3>();
                    if (pIni > 0) pts.Add(w.Pt(sPI, d + LadoPerna(eIni, ladoIni) * pIni, z));
                    pts.Add(w.Pt(sPI, d, z));
                    pts.Add(w.Pt(sPF, d, z));
                    if (pFim > 0) pts.Add(w.Pt(sPF, d + LadoPerna(eFim, ladoFim) * pFim, z));
                    p.Barras.Add(pts);
                }
                p.Bitola = phi;
                if (pIni > 0 || pFim > 0) p.Forma = FormaBarra.Generica;
                for (int k = 0; k + 1 < p.Barras[0].Count; k++) p.Formatos.Add(FormatoSegmento.Linha);
                p.Parametros["comprimento_reto"] = Math.Round(sPF - sPI);
                p.Regras["comprimento_reto"] = "de ponta a ponta a cobrimento (canto/encontro: até a face de fora da vizinha)";
                var b0 = p.Barras[0];
                p.Distribuicao = new Distribuicao
                {
                    Inicio = b0[pIni > 0 ? 1 : 0], Fim = new P3(b0[pIni > 0 ? 1 : 0].X, b0[pIni > 0 ? 1 : 0].Y, p.Barras[p.Barras.Count - 1][0].Z),
                    Quantidade = p.Barras.Count, Espacamento = ContextoMotor.EspacamentoReal(p.Barras.Select(b => b[0].Z), esp),
                };
                ctx.Adicionar(p);
            }
        }

        // ---------------- verticais ----------------
        List<double> sVert = null;
        double espV = 0;
        if (armV != null && phiV > 0)
        {
            double pe = a.Num("pe", 0);
            bool peDentro = a.Txt("pe_sentido", "fora") == "dentro";
            foreach (var f in faces)
            {
                var p = ctx.NovaPosicao($"{chave}_vertical_{f}", fV, $"{nome}: verticais (face {f})", FormaBarra.Reta);
                double phi = ctx.Par(p, "bitola", armV.Bitola, "cálculo (elemento)");
                double esp = ctx.Par(p, "espacamento", armV.Espacamento > 0 ? armV.Espacamento : 15, armV.Espacamento > 0 ? "cálculo (elemento)" : "padrão 15 cm");
                // ao longo da parede, entre as faces das pontas (canto/encontro: até a face de dentro da vizinha)
                double s0 = (eIni == "livre" ? c : tViz / 2 + c) + phiH / 10 + phi / 20;
                double s1 = (eFim == "livre" ? L - c : L - tViz / 2 - c) - phiH / 10 - phi / 20;
                int n = ctx.ParInt(p, "quantidade", ctx.Quantidade(s1 - s0, esp), "comprimento livre / espaçamento");
                if (n <= 0 || s1 <= s0) continue;
                double pePar = ctx.Par(p, "pe", pe, "argumento da receita");
                double zP = a.Num("z_pe", z0 + cb) + phi / 20, zT = z1 - ct - phiH / 10 - phi / 20;
                double d = Sinal(f) * dV, dirPe = (peDentro ? -1 : 1) * Sinal(f);
                var ss = ctx.Posicoes(s1 - s0, n, esp).Select(s => s0 + s).ToList();
                if (f == "a") { sVert = ss; espV = esp; }
                foreach (var s in ss)
                {
                    var pts = new List<P3>();
                    if (pePar > 0) pts.Add(w.Pt(s, d + dirPe * pePar, zP));
                    pts.Add(w.Pt(s, d, zP));
                    pts.Add(w.Pt(s, d, zT));
                    p.Barras.Add(pts);
                }
                p.Bitola = phi;
                if (pePar > 0) p.Forma = FormaBarra.Generica;
                for (int k = 0; k + 1 < p.Barras[0].Count; k++) p.Formatos.Add(FormatoSegmento.Linha);
                p.Distribuicao = new Distribuicao
                {
                    Inicio = w.Pt(ss[0], d, zP), Fim = w.Pt(ss[ss.Count - 1], d, zP), Quantidade = ss.Count, Espacamento = ContextoMotor.EspacamentoReal(ss, esp),
                };
                ctx.Adicionar(p);
            }
        }

        // ---------------- U de fechamento ----------------
        if (!duas) return;
        string fF = a.Txt("funcao_fechamento", fH);
        var armF = ctx.Armadura(fF) ?? armH ?? armV;
        if (armF == null) return;
        double PernaF(PosicaoArmadura p, double phi) => ctx.Par(p, "perna", a.Num("perna_fechamento", 0) > 0 ? a.Num("perna_fechamento", 0) : Traspasse(phi),
            a.Num("perna_fechamento", 0) > 0 ? "argumento da receita" : "traspasse NBR 6118 (100 %), a cada 5 cm");

        // topo: um U a cada vertical, por fora delas, pernas para baixo nas duas faces
        if (a.Bool("fechamento_topo", true) && sVert != null && sVert.Count > 0)
        {
            var p = ctx.NovaPosicao($"{chave}_fechamento_topo", fF, $"{nome}: U de fechamento do topo", FormaBarra.U);
            double phi = ctx.Par(p, "bitola", armF.Bitola, "cálculo (elemento)");
            double perna = PernaF(p, phi);
            double dU = t / 2 - c - phi / 20, zU = z1 - ct - phi / 20;
            foreach (var s in sVert)
                p.Barras.Add(new List<P3> { w.Pt(s, dU, zU - perna), w.Pt(s, dU, zU), w.Pt(s, -dU, zU), w.Pt(s, -dU, zU - perna) });
            Fechar(ctx, p, phi, espV);
        }
        // pontas livres: um U a cada horizontal, abraçando as duas faces, pernas ao longo da parede
        var hA = ctx.Modelo.Posicao($"{chave}_horizontal_a");
        if (hA == null) return;
        foreach (var (ext, nomeExt, fim) in new[] { (eIni, "inicio", false), (eFim, "fim", true) })
        {
            if (ext != "livre") continue;
            var p = ctx.NovaPosicao($"{chave}_fechamento_{nomeExt}", fF, $"{nome}: U de fechamento ({(fim ? "fim" : "início")})", FormaBarra.U);
            double phi = ctx.Par(p, "bitola", armF.Bitola, "cálculo (elemento)");
            double perna = PernaF(p, phi);
            double dU = t / 2 - c - phi / 20;
            double sU = fim ? L - c - phi / 20 : c + phi / 20, sg = fim ? -1 : 1;
            foreach (var hb in hA.Barras)
            {
                double z = hb[0].Z;
                p.Barras.Add(new List<P3> { w.Pt(sU + sg * perna, dU, z), w.Pt(sU, dU, z), w.Pt(sU, -dU, z), w.Pt(sU + sg * perna, -dU, z) });
            }
            Fechar(ctx, p, phi, hA.Distribuicao?.Espacamento ?? 0);
        }
    }

    private static void Fechar(ContextoMotor ctx, PosicaoArmadura p, double phi, double esp)
    {
        if (p.Barras.Count == 0) return;
        p.Bitola = phi;
        p.Formatos.AddRange(new[] { FormatoSegmento.Linha, FormatoSegmento.Linha, FormatoSegmento.Linha });
        p.Parametros["quantidade"] = p.Barras.Count;
        var b0 = p.Barras[0]; var b1 = p.Barras[p.Barras.Count - 1];
        p.Distribuicao = new Distribuicao { Inicio = b0[1], Fim = b1[1], Quantidade = p.Barras.Count, Espacamento = esp };
        ctx.Adicionar(p);
    }
}
