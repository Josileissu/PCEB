using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static DEB.Core.Lajes.GeometriaPano;

namespace DEB.Core.Lajes;

/// <summary>
/// Motor de paginação de lajes alveolares (2026-11-12) — funções PURAS, sem Revit, no sistema local do pano (u = vão, v = transversal).
/// Regras de negócio (definidas pelo usuário):
///   módulo 125 (painel 124,5 + junta 0,5); ajuste mínimo 30 cm (sobra menor → dois ajustes; sem como redistribuir → faixa maciça, etapa 2);
///   comprimento = face da alma − folga (1,5 cm) em cada ponta; apoio mínimo 8 cm; viga sem aba → mesma folga + alerta;
///   aberturas: junta no meio da abertura com dois painéis recortados, depois o restante; pilares → recortes;
///   faixa maciça junto a viga e peça cortada por furo junto a viga → alerta "apoio em consolo a colocar";
///   tirantes dos suportes de instalações nas juntas, numa malha alinhada no pano.
/// Estratégias: aberturas primeiro (padrão), melhor distribuição e de um lado.
/// </summary>
public static class MotorPaginacao
{
    private static string Cm(double m) => Fmt.Virgula(m * 100, "0.0");
    private static string Cm0(double m) => Fmt.Virgula(m * 100, "0");
    private static string M2(double m) => Fmt.Virgula(m, "0.00");

    // ------------------------------------------------------------------ 1. leitura do pano

    /// <summary>Aberturas (furos e entalhes do contorno), recortes de canto (forma) e faixas em v de comprimento constante.
    /// <paramref name="loops"/>: 1º = contorno externo, demais = furos (croqui do piso).</summary>
    public static ClassificacaoPano ClassificarPano(IReadOnlyList<IReadOnlyList<PontoUV>> loops, IEnumerable<PilarPano> pilares)
    {
        var ext = loops[0];
        var furos = loops.Skip(1).ToList();
        // limites arredondados como as linhas da grade: com coordenadas do modelo (ruído de 1e-9), o limite exato e o arredondado
        // viravam duas linhas da grade e a célula fininha entre elas (no contorno) ligava o vazio ao corpo do pano inteiro
        double U0 = Arred(ext.Min(p => p.U), 4), U1 = Arred(ext.Max(p => p.U), 4), V0 = Arred(ext.Min(p => p.V), 4), V1 = Arred(ext.Max(p => p.V), 4);
        var g = new ClassificacaoPano { U0 = U0, U1 = U1, V0 = V0, V1 = V1 };
        var caixa = new RetUV(U0, V0, U1, V1);
        g.Pilares = pilares.Where(p => Intersecao(p.Ret.Expandido(0.1), caixa) != null).ToList();

        var su = new SortedSet<double>(ext.Select(p => Arred(p.U, 4)).Concat(furos.SelectMany(f => f.Select(p => Arred(p.U, 4)))));
        su.Add(U0); su.Add(U1);
        var sv = new SortedSet<double>(ext.Select(p => Arred(p.V, 4)).Concat(furos.SelectMany(f => f.Select(p => Arred(p.V, 4)))));
        sv.Add(V0); sv.Add(V1);
        var cu = su.ToList(); var cv = sv.ToList();

        // células da grade: laje | furo | vazio | pilar (vazio em volta de pilar, até 7 cm além da face)
        var cls = new Dictionary<(int, int), string>();
        var ordem = new List<(int, int)>();
        for (int i = 0; i < cu.Count - 1; i++)
            for (int j = 0; j < cv.Count - 1; j++)
            {
                double uc = (cu[i] + cu[i + 1]) / 2, vc = (cv[j] + cv[j + 1]) / 2;
                string c;
                if (!PontoNoPoligono(uc, vc, ext))
                {
                    c = "vazio";
                    foreach (var p in g.Pilares)
                    {
                        var r = p.Ret;
                        if (r.U0 - .07 <= uc && uc <= r.U1 + .07 && r.V0 - .07 <= vc && vc <= r.V1 + .07) c = "pilar";
                    }
                }
                else if (furos.Any(f => PontoNoPoligono(uc, vc, f))) c = "furo";
                else c = "laje";
                cls[(i, j)] = c;
                ordem.Add((i, j));
            }

        var vistos = new HashSet<(int, int)>();
        var comps = new List<(string Tipo, RetUV Ret)>();
        foreach (var k in ordem)
        {
            string c = cls[k];
            if ((c != "vazio" && c != "furo") || vistos.Contains(k)) continue;
            var pilha = new Stack<(int, int)>(); var cel = new List<(int I, int J)>();
            pilha.Push(k); vistos.Add(k);
            while (pilha.Count > 0)
            {
                var a = pilha.Pop(); cel.Add(a);
                foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var b = (a.Item1 + di, a.Item2 + dj);
                    if (cls.TryGetValue(b, out var cb) && !vistos.Contains(b) && cb == c) { vistos.Add(b); pilha.Push(b); }
                }
            }
            comps.Add((c, new RetUV(cel.Min(a => cu[a.I]), cel.Min(a => cv[a.J]), cel.Max(a => cu[a.I + 1]), cel.Max(a => cv[a.J + 1]))));
        }

        var fora = new List<RetUV>();
        foreach (var (tipo, r) in comps)
        {
            if (tipo == "furo") { g.Aberturas.Add(new AberturaPano { Ret = r, Origem = "furo interno (croqui do piso)" }); continue; }
            if (r.LarguraU < 0.30 || r.LarguraV < 0.30)
            {
                g.Alertas.Add($"trecho fora do contorno pequeno ({M2(r.LarguraU)} x {M2(r.LarguraV)} m) ignorado");
                continue;
            }
            bool canto = (Math.Abs(r.U0 - U0) < 1e-3 || Math.Abs(r.U1 - U1) < 1e-3) && (Math.Abs(r.V0 - V0) < 1e-3 || Math.Abs(r.V1 - V1) < 1e-3);
            if (canto) fora.Add(r);
            else g.Aberturas.Add(new AberturaPano { Ret = r, Origem = "recorte do contorno externo (entalhe na borda do pano)" });
        }
        g.Fora = fora;

        var cortes = new SortedSet<double> { V0, V1 };
        foreach (var f in fora) { cortes.Add(f.V0); cortes.Add(f.V1); }
        var lc = cortes.ToList();
        for (int i = 0; i + 1 < lc.Count; i++)
        {
            double a = lc[i], b = lc[i + 1];
            if (b - a < 0.05) continue;
            double vm = (a + b) / 2, ua = U0, ub = U1;
            foreach (var c in fora)
                if (c.V0 - Eps <= vm && vm <= c.V1 + Eps)
                {
                    if (Math.Abs(c.U0 - U0) < 1e-3) ua = Math.Max(ua, c.U1);
                    if (Math.Abs(c.U1 - U1) < 1e-3) ub = Math.Min(ub, c.U0);
                }
            if (ub - ua <= 0.05) continue;
            var ult = g.Faixas.Count > 0 ? g.Faixas[g.Faixas.Count - 1] : null;
            if (ult != null && Math.Abs(ult.U0 - ua) < 1e-3 && Math.Abs(ult.U1 - ub) < 1e-3 && Math.Abs(ult.V1 - a) < 1e-3) ult.V1 = b;
            else g.Faixas.Add(new FaixaPano { V0 = a, V1 = b, U0 = ua, U1 = ub });
        }
        // o limite entre faixas não é borda: a faixa de baixo leva a junta, a de cima começa no limite
        foreach (var f in g.Faixas) { f.BordaV0 = true; f.BordaV1 = Math.Abs(f.V1 - V1) < 1e-3; }
        return g;
    }

    // ------------------------------------------------------------------ 2. paginação transversal

    /// <summary>Elemento da sequência transversal: painel (largura) ou faixa maciça.</summary>
    public struct ElementoSeq
    {
        public bool Macica; public double W;
        public ElementoSeq(bool macica, double w) { Macica = macica; W = w; }
    }

    /// <summary>Sequência de elementos para um vão livre de comprimento L (em v), na ordem de montagem.
    /// <paramref name="melhor"/> = modo "melhor distribuição". Devolve (sequência, simétrica, alertas).</summary>
    public static (List<ElementoSeq> Seq, bool Simetrico, List<string> Alertas) LargurasVaoLivre(double L, bool bordaA, bool bordaB, ConfigPaginacao cfg, bool melhor)
    {
        double J = cfg.Junta, B = cfg.LarguraPainel, M = cfg.Modulo, amin = cfg.AjusteMin, abom = cfg.AjusteBom;
        double W = L - (bordaA ? 0 : J) - (bordaB ? 0 : J);
        int k = (int)Math.Floor((W + 1e-6) / M);
        double a = W - k * M;
        var al = new List<string>();
        List<ElementoSeq> Rep(int n, double w) => Enumerable.Repeat(new ElementoSeq(false, w), Math.Max(0, n)).ToList();
        if (a < 0.01)
        {
            if (a > 0.0005) al.Add($"sobra de {Cm(a)} cm absorvida nas folgas");
            return (Rep(k, B), false, al);
        }
        if (a >= amin)
        {
            if (melhor && a < abom && k >= 1)
            {
                double dois = (a + B) / 2;
                al.Add($"ajuste de {Cm(a)} cm trocado por dois de {Cm(dois)} cm (melhor distribuição)");
                var s = new List<ElementoSeq> { new ElementoSeq(false, dois) }; s.AddRange(Rep(k - 1, B)); s.Add(new ElementoSeq(false, dois));
                return (s, true, al);
            }
            var s1 = Rep(k, B); s1.Add(new ElementoSeq(false, a));
            return (s1, false, al);
        }
        if (k >= 1)
        {
            double dois = (a + B) / 2;
            al.Add($"sobra de {Cm(a)} cm < {Cm0(amin)}: redistribuída em dois ajustes de {Cm(dois)} cm");
            if (melhor)
            {
                var s = new List<ElementoSeq> { new ElementoSeq(false, dois) }; s.AddRange(Rep(k - 1, B)); s.Add(new ElementoSeq(false, dois));
                return (s, true, al);
            }
            var s2 = Rep(k - 1, B); s2.Add(new ElementoSeq(false, dois)); s2.Add(new ElementoSeq(false, dois));
            return (s2, false, al);
        }
        al.Add($"vão livre de {Cm(L)} cm < {Cm0(amin)} sem como redistribuir: FAIXA MACIÇA");
        return (new List<ElementoSeq> { new ElementoSeq(true, L) }, false, al);
    }

    /// <summary>Posiciona a sequência no vão [a, b]; devolve (v0, v1, maciça) em ordem crescente.</summary>
    public static List<(double V0, double V1, bool Macica)> Montar(List<ElementoSeq> seq, double a, double b, bool bordaA, bool bordaB, bool partirDeA, bool simetrico, ConfigPaginacao cfg)
    {
        double J = cfg.Junta;
        if (seq.Count == 1 && seq[0].Macica) return new List<(double, double, bool)> { (a, b, true) };
        var r = new List<(double V0, double V1, bool Macica)>();
        if (simetrico && bordaA && bordaB) partirDeA = true;
        if (partirDeA)
        {
            double v = a + (bordaA ? 0 : J);
            foreach (var e in seq) { r.Add((v, v + e.W, e.Macica)); v += e.W + J; }
        }
        else
        {
            double v = b - (bordaB ? 0 : J);
            foreach (var e in seq) { r.Add((v - e.W, v, e.Macica)); v -= e.W + J; }
        }
        return r.OrderBy(x => x.V0).ThenBy(x => x.V1).ThenBy(x => x.Macica).ToList();
    }

    /// <summary>Elemento transversal de uma faixa do pano.</summary>
    public sealed class ElementoFaixa
    {
        public double V0, V1;
        /// <summary>padrao | ajuste | abertura | abertura+ajuste | macica</summary>
        public string Tipo = "";
        public int? Abertura;
    }

    /// <summary>Elementos transversais de uma faixa (painéis das aberturas primeiro, conforme a estratégia; depois os vãos livres).</summary>
    public static (List<ElementoFaixa> Elementos, List<string> Alertas) LayoutFaixa(FaixaPano fx, IList<AberturaPano> aberturas, ConfigPaginacao cfg)
    {
        double J = cfg.Junta, B = cfg.LarguraPainel, amin = cfg.AjusteMin;
        var estr = cfg.Estrategia;
        var al = new List<string>();
        var ocup = new List<ElementoFaixa>();
        if (estr == EstrategiaPaginacao.AberturasPrimeiro || estr == EstrategiaPaginacao.MelhorDistribuicao)
        {
            for (int ia = 0; ia < aberturas.Count; ia++)
            {
                var ab = aberturas[ia];
                var r = ab.Ret;
                if (r.V1 <= fx.V0 + Eps || r.V0 >= fx.V1 - Eps) continue;
                double vc = (r.V0 + r.V1) / 2;
                ab.VCorte = vc;
                if (r.V1 - r.V0 > 2 * B) al.Add($"abertura maior que 2 painéis em v ({M2(r.V1 - r.V0)} m) - rever");
                foreach (var (a, b) in new[] { (vc - J / 2 - B, vc - J / 2), (vc + J / 2, vc + J / 2 + B) })
                {
                    double a2 = Math.Max(a, fx.V0), b2 = Math.Min(b, fx.V1);
                    if (b2 - a2 <= 0.01) continue;
                    if (b2 - a2 < amin)
                    {
                        al.Add($"painel de abertura cortado pela borda com {Cm(b2 - a2)} cm (< {Cm0(amin)}): FAIXA MACIÇA");
                        ocup.Add(new ElementoFaixa { V0 = a2, V1 = b2, Tipo = "macica", Abertura = ia });
                    }
                    else ocup.Add(new ElementoFaixa { V0 = a2, V1 = b2, Tipo = "abertura", Abertura = ia });
                }
            }
        }
        ocup = ocup.OrderBy(o => o.V0).ToList();
        for (int i = 0; i + 1 < ocup.Count; i++)
            if (ocup[i].V1 > ocup[i + 1].V0 + 1e-4)
                al.Add($"painéis de aberturas diferentes se sobrepõem em v = {Fmt.Virgula(ocup[i + 1].V0, "0.000")} - rever");

        // vãos livres
        var livres = new List<(double A, double B, bool BA, bool BB)>();
        double pos = fx.V0; bool bordaPos = fx.BordaV0;
        foreach (var o in ocup)
        {
            if (o.V0 - pos > 0.01) livres.Add((pos, o.V0, bordaPos, o.Tipo == "macica"));
            pos = o.V1; bordaPos = o.Tipo == "macica";   // faixa maciça encosta sem junta
        }
        if (fx.V1 - pos > 0.01) livres.Add((pos, fx.V1, bordaPos, fx.BordaV1));

        bool modoMelhor = estr == EstrategiaPaginacao.MelhorDistribuicao;
        var novos = new List<ElementoFaixa>();
        foreach (var (a, b, ba, bb) in livres)
        {
            var (seq, sim, al2) = LargurasVaoLivre(b - a, ba, bb, cfg, modoMelhor);
            al.AddRange(al2);
            bool partirDeA;
            if (estr == EstrategiaPaginacao.DeUmLado) partirDeA = cfg.BordaInicioVMin;
            else if (ba && !bb) partirDeA = false;   // parte da abertura (b) e o ajuste fica junto à borda (a)
            else partirDeA = true;                    // parte de a; sem aberturas o ajuste fica na borda v máx.
            foreach (var (v0, v1, mac) in Montar(seq, a, b, ba, bb, partirDeA, sim, cfg))
                novos.Add(new ElementoFaixa { V0 = v0, V1 = v1, Tipo = mac ? "macica" : (Math.Abs(v1 - v0 - B) < 1e-4 ? "padrao" : "ajuste") });
        }
        var todos = ocup.Concat(novos).OrderBy(o => o.V0).ToList();
        foreach (var o in todos)
            if (o.Tipo == "abertura" && o.V1 - o.V0 < B - 1e-4) o.Tipo = "abertura+ajuste";
        return (todos, al);
    }

    // ------------------------------------------------------------------ 3. pontas (apoio nas vigas)

    private static double? ExtSecao(List<(double A, double B)> segs, double s)
        => segs == null || segs.Count == 0 ? (double?)null : segs.Max(t => Math.Max(s * t.A, s * t.B));

    /// <summary>(face da alma, fim da aba) a partir do eixo, no lado s da coordenada n da viga.</summary>
    public static (double Face, double Aba) ApoioViga(SecaoViga sec, double s, ConfigPaginacao cfg)
    {
        var lv = cfg.LeituraViga;
        double? web = ExtSecao(sec.Em(lv.DzAlma), s);
        if (web == null) web = ExtSecao(sec.MaisBaixa?.Trechos, s);
        double face = web ?? 0;
        foreach (var l in sec.Linhas)
            if (lv.DzPainelMin <= l.Z && l.Z <= lv.DzPainelMax)
            {
                var x = ExtSecao(l.Trechos, s);
                if (x != null && x.Value > face) face = x.Value;
            }
        if (web == null) face = sec.Linhas.Where(l => lv.DzPainelMin <= l.Z && l.Z <= lv.DzPainelMax).Select(l => ExtSecao(l.Trechos, s)).Where(x => x != null).Select(x => x.Value).DefaultIfEmpty(0).Max();
        double? aba = ExtSecao(sec.Em(lv.DzAba), s);
        return (face, aba ?? web ?? face);
    }

    /// <summary>Ponta do painel: lado −1 = início (u mínimo), +1 = fim. Ponta = face da alma ∓ folga.</summary>
    public static (double U, ApoioPonta Info) Ponta(double uCont, int lado, double pv0, double pv1, IList<VigaTransversal> vigas, ConfigPaginacao cfg)
    {
        VigaTransversal vg = null; double melhor = 0;
        foreach (var v in vigas)
        {
            if (Math.Min(v.Vb, pv1) - Math.Max(v.Va, pv0) < 0.05) continue;
            double dist = (v.UEixo - uCont) * lado;
            if (-0.10 <= dist && dist <= 0.60 && (vg == null || Math.Abs(dist) < Math.Abs(melhor))) { vg = v; melhor = dist; }
        }
        if (vg == null)
            return (uCont, new ApoioPonta { Alerta = "SEM VIGA DE APOIO identificada - ponta no contorno do piso" });
        int tau = -lado;
        double s = vg.NU * tau > 0 ? 1 : -1;
        var (face, aba) = ApoioViga(vg.Secao, s, cfg);
        double uFace = vg.UEixo + tau * face;
        double apoio = aba - face - cfg.FolgaPonta;
        var info = new ApoioPonta
        {
            Viga = vg.Id, VigaTipo = vg.Tipo, UEixoViga = vg.UEixo, UFace = uFace, UFimAba = vg.UEixo + tau * aba,
            Aba = aba - face, Apoio = apoio, ContornoPiso = uCont, PisoAlemDaFace = (uFace - uCont) * tau,
        };
        if (apoio <= 0) info.Alerta = $"SEM APOIO: viga {vg.Id} ({vg.Tipo}) sem aba - comprimento até a face - folga";
        else if (apoio < cfg.ApoioMin - 1e-4) info.Alerta = $"APOIO {Cm(apoio)} cm < {Cm0(cfg.ApoioMin)} cm";
        return (uFace + tau * cfg.FolgaPonta, info);
    }

    // ------------------------------------------------------------------ 4. recortes, tiras < mínimo e peças

    /// <summary>Recortes (aberturas e pilares) de uma faixa de painel e a divisão em peças. Tira de painel menor que o ajuste
    /// mínimo ao lado de um entalhe: o entalhe vai até a borda e a tira vira faixa maciça. Entalhe que atravessa o painel o divide.</summary>
    public static (List<PecaPainel> Pecas, List<FaixaMacica> Macicas, List<RecortePeca> Recortes, List<string> Alertas)
        RecortesEPecas(FaixaPainel pn, IList<AberturaPano> aberturas, IList<PilarPano> pilares, ConfigPaginacao cfg)
    {
        double amin = cfg.AjusteMin, fp = cfg.FolgaPilar;
        var rp = new RetUV(pn.U0, pn.V0, pn.U1, pn.V1);
        var rec = new List<RecortePeca>(); var mac = new List<FaixaMacica>(); var al = new List<string>();
        foreach (var ab in aberturas)
        {
            var x = Intersecao(rp, ab.Ret);
            if (x == null) continue;
            var r = x.Value;
            double u0 = r.U0, v0 = r.V0, u1 = r.U1, v1 = r.V1;
            double baixo = v0 - pn.V0, cima = pn.V1 - v1;
            if (1e-4 < baixo && baixo < amin)
            {
                mac.Add(new FaixaMacica { Ret = new RetUV(u0, pn.V0, u1, v0), Motivo = $"tira de painel de {Cm(baixo)} cm ao lado de entalhe de abertura" });
                v0 = pn.V0;
            }
            if (1e-4 < cima && cima < amin)
            {
                mac.Add(new FaixaMacica { Ret = new RetUV(u0, v1, u1, pn.V1), Motivo = $"tira de painel de {Cm(cima)} cm ao lado de entalhe de abertura" });
                v1 = pn.V1;
            }
            rec.Add(new RecortePeca { Tipo = "abertura", Ret = new RetUV(u0, v0, u1, v1) });
        }
        foreach (var p in pilares)
        {
            var r2 = Intersecao(rp, p.Ret.Expandido(fp));
            if (r2 != null && Math.Min(r2.Value.LarguraU, r2.Value.LarguraV) >= cfg.RecorteMin)   // lascas (só a folga) são desprezadas
                rec.Add(new RecortePeca { Tipo = "pilar", Ret = r2.Value, Ref = p.Id });
        }
        // recortes que atravessam toda a largura dividem o painel
        var cortes = rec.Where(r => r.Ret.V0 <= pn.V0 + 1e-4 && r.Ret.V1 >= pn.V1 - 1e-4)
                        .Select(r => (r.Ret.U0, r.Ret.U1)).OrderBy(t => t.U0).ThenBy(t => t.U1).ToList();
        var trechos = new List<(double A, double B)> { (pn.U0, pn.U1) };
        foreach (var (a, b) in cortes) trechos = SubtrairIntervalo(trechos, a, b, 0.0);
        var pecas = new List<PecaPainel>();
        foreach (var (a, b) in trechos)
        {
            if (b - a < 0.05) continue;
            var ini = Math.Abs(a - pn.U0) < 1e-4 ? pn.Ini : new ApoioPonta { Apoio = 0, Alerta = "SEM APOIO: ponta no recorte da abertura (peça apoiada só de um lado)" };
            var fim = Math.Abs(b - pn.U1) < 1e-4 ? pn.Fim : new ApoioPonta { Apoio = 0, Alerta = "SEM APOIO: ponta no recorte da abertura (peça apoiada só de um lado)" };
            var rr = new List<RecortePeca>();
            foreach (var r in rec)
            {
                var x = Intersecao(new RetUV(a, pn.V0, b, pn.V1), r.Ret);
                if (x != null) rr.Add(new RecortePeca { Tipo = r.Tipo, Ret = x.Value, Ref = r.Ref });
            }
            pecas.Add(new PecaPainel { U0 = a, U1 = b, V0 = pn.V0, V1 = pn.V1, Ini = ini, Fim = fim, Recortes = rr, Painel = pn });
        }
        if (pecas.Count != 1) al.Add($"painel dividido pelo recorte em {pecas.Count} peça(s)");
        return (pecas, mac, rec, al);
    }

    // ------------------------------------------------------------------ 5. juntas (chaves) e tirantes

    /// <summary>Juntas onde há painel dos dois lados (fora dos recortes).</summary>
    public static List<JuntaPaineis> Juntas(IList<FaixaPainel> paineis, ConfigPaginacao cfg)
    {
        double J = cfg.Junta;
        var r = new List<JuntaPaineis>();
        var ps = paineis.OrderBy(p => p.V0).ToList();
        for (int i = 0; i < ps.Count; i++)
        {
            var a = ps[i];
            for (int k = i + 1; k < ps.Count; k++)
            {
                var b = ps[k];
                if (Math.Abs(b.V0 - a.V1 - J) > 2e-4) continue;
                double u0 = Math.Max(a.U0, b.U0), u1 = Math.Min(a.U1, b.U1);
                if (u1 - u0 < 0.05) continue;
                var trechos = new List<(double A, double B)> { (u0, u1) };
                foreach (var (p, vb) in new[] { (a, a.V1), (b, b.V0) })
                    foreach (var rc in p.Recortes)
                        if (rc.Ret.V0 - 1e-3 <= vb && vb <= rc.Ret.V1 + 1e-3)
                            trechos = SubtrairIntervalo(trechos, rc.Ret.U0, rc.Ret.U1);
                foreach (var (x0, x1) in trechos)
                    r.Add(new JuntaPaineis { V = (a.V1 + b.V0) / 2, U0 = x0, U1 = x1, Esq = a, Dir = b });
            }
        }
        return r;
    }

    /// <summary>Tirantes numa malha alinhada no pano: u_meio + (k + ½)·passo, com margem nas pontas de cada trecho de junta.</summary>
    public static List<PontoUV> Tirantes(IList<JuntaPaineis> juntas, double uMeio, ConfigPaginacao cfg)
    {
        var pts = new List<PontoUV>();
        if (!cfg.SuportesInstalacoes) { foreach (var j in juntas) j.Tirantes = new List<double>(); return pts; }
        double P = cfg.Tirante.MalhaLongitudinal;
        double mg = Math.Max(cfg.Tirante.MargemPontaJunta, cfg.Tirante.ChapaAoLongoJunta / 2);   // a chapa não passa da ponta da junta
        foreach (var j in juntas)
        {
            int k0 = (int)Math.Ceiling((j.U0 + mg - uMeio) / P - 0.5 - 1e-9);
            int k1 = (int)Math.Floor((j.U1 - mg - uMeio) / P - 0.5 + 1e-9);
            var us = new List<double>();
            for (int k = k0; k <= k1; k++) us.Add(uMeio + (k + 0.5) * P);
            if (us.Count == 0 && j.U1 - j.U0 > 2 * mg) us.Add((j.U0 + j.U1) / 2);
            j.Tirantes = us;
            pts.AddRange(us.Select(u => new PontoUV(u, j.V)));
        }
        return pts;
    }

    // ------------------------------------------------------------------ 5b. apoio em consolo (a colocar pelo usuário)

    /// <summary>(lo, hi) da seção na coordenada n, na altura do painel (ou da alma, se a viga não chega lá).</summary>
    public static (double Lo, double Hi) ExtensaoPlanta(SecaoViga sec, ConfigPaginacao cfg)
    {
        var lv = cfg.LeituraViga;
        var segs = sec.Linhas.Where(l => lv.DzPainelMin <= l.Z && l.Z <= lv.DzPainelMax).SelectMany(l => l.Trechos).ToList();
        if (segs.Count == 0) segs = sec.Em(lv.DzAlma) ?? sec.MaisBaixa?.Trechos ?? new List<(double, double)> { (0, 0) };
        return (segs.Min(t => t.A), segs.Max(t => t.B));
    }

    /// <summary>Faixa maciça cuja borda fica a até <see cref="ConfigPaginacao.DistanciaConsolo"/> da face de uma viga → apoio em consolo nessa viga.</summary>
    public static List<ConsoloAColocar> ConsolosFaixas(IList<FaixaMacica> macicas, IList<VigaTransversal> vigasV, IList<VigaLongitudinal> vigasU, ConfigPaginacao cfg)
    {
        double dmax = cfg.DistanciaConsolo;
        var r = new List<ConsoloAColocar>();
        for (int im = 0; im < macicas.Count; im++)
        {
            var m = macicas[im].Ret;
            foreach (var vg in vigasU)
            {
                double a = Math.Min(vg.Ua, vg.Ub), b = Math.Max(vg.Ua, vg.Ub);
                double x0 = Math.Max(a, m.U0), x1 = Math.Min(b, m.U1);
                if (x1 - x0 < 0.05) continue;
                var (lo, hi) = ExtensaoPlanta(vg.Secao, cfg);
                double f0 = Math.Min(vg.VEixo + vg.NV * lo, vg.VEixo + vg.NV * hi), f1 = Math.Max(vg.VEixo + vg.NV * lo, vg.VEixo + vg.NV * hi);
                if ((0 <= f0 - m.V1 && f0 - m.V1 <= dmax) || (0 <= m.V0 - f1 && m.V0 - f1 <= dmax))
                    r.Add(new ConsoloAColocar { Faixas = { im }, Motivo = "apoio da faixa maciça", Viga = vg.Id, VigaTipo = vg.Tipo, EixoViga = 'u', De = x0, Ate = x1, Face = f0 >= m.V1 ? f0 : f1 });
            }
            foreach (var vg in vigasV)
            {
                double x0 = Math.Max(vg.Va, m.V0), x1 = Math.Min(vg.Vb, m.V1);
                if (x1 - x0 < 0.05) continue;
                var (lo, hi) = ExtensaoPlanta(vg.Secao, cfg);
                double f0 = Math.Min(vg.UEixo + vg.NU * lo, vg.UEixo + vg.NU * hi), f1 = Math.Max(vg.UEixo + vg.NU * lo, vg.UEixo + vg.NU * hi);
                if ((0 <= f0 - m.U1 && f0 - m.U1 <= dmax) || (0 <= m.U0 - f1 && m.U0 - f1 <= dmax))
                    r.Add(new ConsoloAColocar { Faixas = { im }, Motivo = "apoio da faixa maciça", Viga = vg.Id, VigaTipo = vg.Tipo, EixoViga = 'v', De = x0, Ate = x1, Face = f0 >= m.U1 ? f0 : f1 });
            }
        }
        foreach (var c in r)
            macicas[c.Faixas[0]].Alertas.Add($"APOIO EM CONSOLO (a colocar pelo usuário) na viga {c.Viga} ({c.VigaTipo}), extensão {M2(c.Ate - c.De)} m");
        return r;
    }

    /// <summary>Peça cortada por furo (ponta no recorte) cujo painel encosta numa viga que corre em u → a ponta apoia em consolo
    /// a colocar nessa viga; o consolo cobre o recorte e avança <see cref="ConfigPaginacao.ConsoloAlemDoRecorte"/> sob cada peça.</summary>
    public static List<ConsoloAColocar> ConsolosPecas(IList<FaixaPainel> paineis, IList<VigaLongitudinal> vigasU, ConfigPaginacao cfg)
    {
        double ext = cfg.ConsoloAlemDoRecorte, dmax = cfg.DistanciaConsolo;
        var r = new List<ConsoloAColocar>();
        foreach (var p in paineis)
        {
            var pecas = p.Pecas.OrderBy(q => q.U0).ToList();
            var cortes = new List<(double A, double B)>();
            if (pecas.Count > 0 && pecas[0].U0 > p.U0 + 1e-4) cortes.Add((p.U0, pecas[0].U0));
            for (int i = 0; i + 1 < pecas.Count; i++) cortes.Add((pecas[i].U1, pecas[i + 1].U0));
            if (pecas.Count > 0 && pecas[pecas.Count - 1].U1 < p.U1 - 1e-4) cortes.Add((pecas[pecas.Count - 1].U1, p.U1));
            foreach (var (a, b) in cortes)
                foreach (var vg in vigasU)
                {
                    var (lo, hi) = ExtensaoPlanta(vg.Secao, cfg);
                    double f0 = Math.Min(vg.VEixo + vg.NV * lo, vg.VEixo + vg.NV * hi), f1 = Math.Max(vg.VEixo + vg.NV * lo, vg.VEixo + vg.NV * hi);
                    if (!((0 <= f0 - p.V1 && f0 - p.V1 <= dmax) || (0 <= p.V0 - f1 && p.V0 - f1 <= dmax))) continue;
                    double va = Math.Min(vg.Ua, vg.Ub), vb = Math.Max(vg.Ua, vg.Ub);
                    double de = Math.Max(va, a - ext), ate = Math.Min(vb, b + ext);
                    if (ate - de < 0.05) continue;
                    string aviso = $"apoio em CONSOLO a colocar pelo usuário na viga {vg.Id} ({vg.Tipo})";
                    var apoiadas = new List<PecaPainel>();
                    foreach (var q in pecas)
                    {
                        if (Math.Abs(q.U1 - a) < 1e-4)
                        {
                            q.Fim = new ApoioPonta { Viga = vg.Id, VigaTipo = vg.Tipo, Consolo = true, Aviso = aviso };
                            apoiadas.Add(q);
                        }
                        if (Math.Abs(q.U0 - b) < 1e-4)
                        {
                            q.Ini = new ApoioPonta { Viga = vg.Id, VigaTipo = vg.Tipo, Consolo = true, Aviso = aviso };
                            apoiadas.Add(q);
                        }
                    }
                    var c = new ConsoloAColocar
                    {
                        Viga = vg.Id, VigaTipo = vg.Tipo, EixoViga = 'u', De = de, Ate = ate, Face = f0 >= p.V1 ? f0 : f1,
                        Motivo = $"apoio das pontas das peças cortadas pelo recorte ({M2(ext)} m além do recorte de cada lado)",
                    };
                    c.Pecas.AddRange(apoiadas);
                    r.Add(c);
                }
        }
        return r;
    }

    /// <summary>Une os registros da mesma viga/face que se sobrepõem.</summary>
    public static List<ConsoloAColocar> UnirConsolos(IEnumerable<ConsoloAColocar> lista)
    {
        var r = new List<ConsoloAColocar>();
        foreach (var c in lista.OrderBy(c => c.Viga ?? "", ComparadorId.Instancia).ThenBy(c => Arred(c.Face, 3)).ThenBy(c => c.De))
        {
            var u = r.Count > 0 ? r[r.Count - 1] : null;
            if (u != null && u.Viga == c.Viga && Math.Abs(u.Face - c.Face) < 1e-3 && c.De <= u.Ate + 1e-3)
            {
                u.Ate = Math.Max(u.Ate, c.Ate); u.De = Math.Min(u.De, c.De);
                u.Faixas.AddRange(c.Faixas); u.Pecas.AddRange(c.Pecas); u.Motivos.Add(c.Motivo);
            }
            else
            {
                var n = new ConsoloAColocar { Viga = c.Viga, VigaTipo = c.VigaTipo, EixoViga = c.EixoViga, De = c.De, Ate = c.Ate, Face = c.Face, Motivo = c.Motivo };
                n.Faixas.AddRange(c.Faixas); n.Pecas.AddRange(c.Pecas); n.Motivos.Add(c.Motivo);
                r.Add(n);
            }
        }
        return r;
    }

    /// <summary>Ids numéricos em ordem numérica (ids do Revit); os demais em ordem de texto.</summary>
    public sealed class ComparadorId : IComparer<string>
    {
        public static readonly ComparadorId Instancia = new ComparadorId();
        public int Compare(string a, string b)
        {
            bool na = long.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out long x);
            bool nb = long.TryParse(b, NumberStyles.Integer, CultureInfo.InvariantCulture, out long y);
            if (na && nb) return x.CompareTo(y);
            if (na != nb) return na ? -1 : 1;
            return string.CompareOrdinal(a, b);
        }
    }

    // ------------------------------------------------------------------ 6. pano completo

    /// <summary>Pagina um pano: contorno + furos (sistema do pano), vigas transversais (apoio das pontas), pilares e vigas
    /// longitudinais (para os consolos a colocar).</summary>
    public static ResultadoPano PaginarPano(IReadOnlyList<IReadOnlyList<PontoUV>> loops, IList<VigaTransversal> vigas, IList<PilarPano> pilares,
                                           ConfigPaginacao cfg, IList<VigaLongitudinal> vigasU = null)
    {
        vigasU ??= new List<VigaLongitudinal>();
        var g = ClassificarPano(loops, pilares);
        var res = new ResultadoPano { Geo = g };
        res.Alertas.AddRange(g.Alertas);
        foreach (var fx in g.Faixas)
        {
            var abFx = g.Aberturas.Where(a => !(a.Ret.V1 <= fx.V0 + Eps || a.Ret.V0 >= fx.V1 - Eps)).ToList();
            var (elems, al) = LayoutFaixa(fx, abFx, cfg);
            res.Alertas.AddRange(al);
            foreach (var e in elems)
            {
                if (e.Tipo == "macica")
                {
                    res.FaixasMacicas.Add(new FaixaMacica { Ret = new RetUV(fx.U0, e.V0, fx.U1, e.V1), Motivo = "vão transversal < ajuste mínimo" });
                    continue;
                }
                var (u0, i0) = Ponta(fx.U0, -1, e.V0, e.V1, vigas, cfg);
                var (u1, i1) = Ponta(fx.U1, +1, e.V0, e.V1, vigas, cfg);
                res.Paineis.Add(new FaixaPainel { U0 = u0, U1 = u1, V0 = e.V0, V1 = e.V1, Tipo = e.Tipo, Ini = i0, Fim = i1, Faixa = fx });
            }
        }
        foreach (var p in res.Paineis)
        {
            var (pecas, mac, rec, al) = RecortesEPecas(p, g.Aberturas, g.Pilares, cfg);
            p.Recortes = rec;
            p.Pecas = pecas;
            res.FaixasMacicas.AddRange(mac);
            res.Alertas.AddRange(al);
            if ((p.Tipo == "padrao" || p.Tipo == "ajuste") && rec.Any(r => r.Tipo == "abertura")) p.Tipo += "+entalhe_abertura";
        }
        foreach (var m in res.FaixasMacicas)
        {
            double w = Math.Min(m.Ret.LarguraU, m.Ret.LarguraV);
            if (w < cfg.FaixaMacicaMin - 1e-6) m.Alertas.Add($"faixa maciça estreita ({Cm(w)} cm < {Cm(cfg.FaixaMacicaMin)}): conferir execução");
        }
        res.Juntas = Juntas(res.Paineis, cfg);
        double uMeio = res.Paineis.Count > 0 ? (res.Paineis.Min(p => p.U0) + res.Paineis.Max(p => p.U1)) / 2 : 0;
        res.Tirantes = Tirantes(res.Juntas, uMeio, cfg);
        res.Consolos = UnirConsolos(ConsolosFaixas(res.FaixasMacicas, vigas, vigasU, cfg).Concat(ConsolosPecas(res.Paineis, vigasU, cfg)));
        foreach (var c in res.Consolos)
            res.Alertas.Add($"CONSOLO a colocar pelo usuário na viga {c.Viga}: {M2(c.Ate - c.De)} m ({string.Join("; ", c.Motivos)})");
        return res;
    }
}
