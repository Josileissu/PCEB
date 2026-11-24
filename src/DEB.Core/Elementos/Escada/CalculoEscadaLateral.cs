using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Documentacao;
using DEB.Core.Elementos.Viga.PreMoldada.Calculo;
using DEB.Core.Normas;
using DEB.Core.Normas.Verificacao;

namespace DEB.Core.Elementos.Escada;

/// <summary>
/// Escada armada transversalmente e em balanço (apostila de escadas, 7.3.2.3 e 7.3.2.4): faixa de 1 m medida ao longo do lance,
/// vencendo a largura — biapoiada nas vigas ou paredes laterais (transversal) ou engastada de um lado (balanço). Carga
/// perpendicular à laje p1 = p·cosα (p em projeção horizontal; a favor da segurança). Os degraus não entram na resistência (a
/// apostila permite a colaboração deles como zona comprimida triangular: fica a favor da segurança desprezar). No balanço, uma
/// barra no canto de cada degrau (todos os degraus ficam tracionados em cima).
/// </summary>
public static partial class CalculoEscada
{
    private static void CalcularLateral(ResultadoEscada r, CoeficientesViga k)
    {
        var e = r.Entrada; var g = r.Geo;
        bool bal = e.Tipo == TipoEscada.Balanco;
        double c = g.Cos, h = g.H, la = e.LarguraApoio, cob = e.Cobrimento, b = 100;
        double L = bal ? e.Largura : e.Largura + la;   // balanço: da face do apoio à borda livre; transversal: eixo a eixo
        r.VaoCalculo = L;
        r.GParapeito = 0;
        double pPar = bal && e.Parapeito > 0 && e.BordasComParapeito > 0 ? e.Parapeito : 0;   // na borda livre (kN/m)
        double g1 = r.GLance * c, q1 = r.Q * c;   // kN/m² perpendicular à laje (por m de lance)
        string N(double x, string f = "0.##") => Fmt.Virgula(x, f);

        // ---------------- 3. esforços na faixa ----------------
        // y: 0 no apoio (esquerdo no transversal; o engaste no balanço) → L
        double M(double y, double w, double p) => bal ? -(w * Math.Pow(L - y, 2) / 2 + p * (L - y)) : w * y * (L - y) / 2;   // kN·cm/m (w em kN/m/cm)
        double V(double y, double w, double p) => bal ? w * (L - y) + p : w * (L / 2 - y);
        double wg = g1 / 100, wq = q1 / 100;
        r.RaG = bal ? wg * L + pPar : wg * L / 2; r.RaQ = bal ? wq * L : wq * L / 2;
        r.RbG = bal ? 0 : r.RaG; r.RbQ = bal ? 0 : r.RaQ;
        if (bal) { r.MEngG = -M(0, wg, pPar) / 100; r.MEngQ = -M(0, wq, 0) / 100; }
        int np = 100;
        for (int i = 0; i <= np; i++)
        {
            double y = L * i / np;
            r.Diagrama.Add(new PontoEscada
            {
                X = y, Z = 0,
                Md = (k.GamaG * M(y, wg, pPar) + k.GamaQ * M(y, wq, 0)) / 100,
                Vd = k.GamaG * V(y, wg, pPar) + k.GamaQ * V(y, wq, 0),
            });
        }
        var pm = r.Diagrama.OrderByDescending(p => Math.Abs(p.Md)).First();
        r.MdMax = Math.Abs(pm.Md); r.XMdMax = pm.X;
        r.VdMax = r.Diagrama.Max(p => Math.Abs(p.Vd));
        var m3 = new PassoMemoria { Titulo = $"3. Esforços na faixa de 1 m ao longo do lance — {(bal ? "balanço (apostila 7.3.2.4)" : "armada transversalmente (apostila 7.3.2.3)")}" };
        m3.Linhas.Add($"p1 = p·cosα (perpendicular à laje, a favor da segurança): g1 = {N(g1)} kN/m², q1 = {N(q1)} kN/m².");
        m3.Linhas.Add(bal
            ? $"Balanço de ℓ = {N(L, "0")} cm (da face do apoio à borda livre)" + (pPar > 0 ? $", parapeito de {N(pPar)} kN/m na borda" : "") +
              $": Md = −(pd·ℓ²/2{(pPar > 0 ? " + γg·P·ℓ" : "")}) = {N(-r.MdMax, "0.00")} kN·m/m no engaste; Vd = {N(r.VdMax, "0.00")} kN/m."
            : $"Biapoiada nos apoios laterais, ℓ = largura + apoio = {N(L, "0")} cm: Md = pd·ℓ²/8 = {N(r.MdMax, "0.00")} kN·m/m; Vd = pd·ℓ/2 = {N(r.VdMax, "0.00")} kN/m.");
        m3.Linhas.Add(bal
            ? $"Reação no apoio por metro de lance: G = {N(r.RaG)} + Q = {N(r.RaQ)} kN/m; momento no engaste G = {N(r.MEngG)} + Q = {N(r.MEngQ)} kN·m/m (o apoio tem de resistir à torção ou ao momento)."
            : $"Reação em cada apoio lateral por metro de lance: G = {N(r.RaG)} + Q = {N(r.RaQ)} kN/m.");
        m3.Linhas.Add($"No lance inteiro ({N(r.ComprimentoReacao, "0.00")} m inclinado): {N((r.RaG + r.RaQ) * r.ComprimentoReacao, "0.0")} kN em {(bal ? "o" : "cada")} apoio.");
        m3.Linhas.Add("Os degraus não entram na resistência (a apostila admite a colaboração deles; desprezar fica a favor da segurança).");
        r.Memoria.Add(m3);

        // ---------------- 4. flexão ----------------
        double fcd = e.Fck / k.GamaC / 10, fyd = TabelasNbr.Fyk(e.Aco) / k.GamaS / 10;
        double fctm = TabelasNbr.Fctm(e.Fck) / 10, fctd = 0.7 * fctm / k.GamaC;
        var mat = new Flexao(e.Fck, fcd, fyd);
        double rhoMin = TabelasNbr.RhoMinVigaPct(e.Fck) / 100;
        r.AsMin = rhoMin * b * h;
        double DD(double phi) => h - cob - phi / 20;
        (double Bitola, double Esp) melhor = (0, 0); double melhorAs = double.MaxValue, nec0 = 0;
        foreach (double sMin in new[] { 10.0, 7.5 })
        {
            foreach (double phi in BitolasPrincipal)
            {
                if (phi > h * 10 / 8 + 1e-9) continue;
                double nec = Math.Max(mat.Armadura(r.MdMax * 100, b, DD(phi), cob).As, r.AsMin);
                double esp = Math.Min(Math.Min(2 * h, 20), Math.Floor(A(phi) * 100 / nec / 2.5 + 1e-9) * 2.5);
                if (esp < sMin - 1e-9) continue;
                double a = A(phi) * 100 / esp;
                if (a < melhorAs - 1e-6 || (Math.Abs(a - melhorAs) < 1e-6 && esp > melhor.Esp)) { melhorAs = a; melhor = (phi, esp); nec0 = nec; }
            }
            if (melhor.Esp > 0) break;
        }
        var m4 = new PassoMemoria { Titulo = "4. Flexão — armadura principal na largura (NBR 6118, 17.2.2; 19.3.3.2; 20.1)" };
        m4.Linhas.Add($"As,mín = ρmín·b·h = {N(r.AsMin)} cm²/m (Tabela 19.1{(bal ? ", armadura negativa" : ", positiva de laje armada numa direção")}).");
        if (melhor.Esp <= 0)
        {
            r.Avisos.Add("Flexão: nenhuma bitola até ø16 com espaçamento ≥ 7,5 cm resolve — aumente a espessura.");
            r.Verificacoes.Add(K("K1", "Flexão", "Armadura principal (ELU)", $"Md = {N(r.MdMax, "0.0")} kN·m/m", "bitola até ø16 c/ ≥ 7,5", true, "NBR 6118, 17.2.2", dica: "Aumente a espessura."));
            r.Memoria.Add(m4);
            return;
        }
        r.Principal = melhor; r.AsNec = nec0; r.D = DD(melhor.Bitola);
        var f = mat.Armadura(r.MdMax * 100, b, r.D, cob);
        double xr = Math.Min(r.AsPrincipal * fyd / (mat.AlfaC * fcd * b * mat.Lambda), mat.XdLim * r.D);
        r.MRd = r.AsPrincipal * fyd * (r.D - mat.Lambda * xr / 2) / 100;
        m4.Linhas.Add($"d = {N(r.D, "0.0")} cm; x/d = {N(f.XsobreD, "0.000")}; As = {N(f.As)} cm²/m → {(bal ? "em cima" : "embaixo")}: ø{N(melhor.Bitola, "0.#")} c/{N(melhor.Esp, "0.#")} = {N(r.AsPrincipal)} cm²/m; MRd = {N(r.MRd)} kN·m/m.");
        r.Memoria.Add(m4);
        r.Verificacoes.Add(K("K1", "Flexão", $"Armadura principal {(bal ? "negativa (em cima)" : "positiva")} na largura (ELU)", $"ø{N(melhor.Bitola, "0.#")} c/{N(melhor.Esp, "0.#")} = {N(r.AsPrincipal)} cm²/m", $"≥ {N(nec0)} cm²/m", false, "NBR 6118, 17.2.2"));
        r.Verificacoes.Add(K("K2", "Flexão", "Ductilidade: posição da linha neutra", $"x/d = {N(f.XsobreD, "0.000")}", $"≤ {N(mat.XdLim)}", f.AsCompr > 0, "NBR 6118, 14.6.4.3", dica: "Aumente a espessura da laje."));
        r.Verificacoes.Add(K("K3", "Flexão", "Espaçamento da armadura principal", $"{N(melhor.Esp, "0.#")} cm", $"≤ 2h = {N(2 * h, "0")} e ≤ 20 cm", melhor.Esp > Math.Min(2 * h, 20) + 1e-9, "NBR 6118, 20.1"));

        // ---------------- 5. distribuição e construtivas ----------------
        r.AsDistNec = Math.Max(Math.Max(0.2 * r.AsPrincipal, 0.9), 0.5 * rhoMin * b * h);
        r.Distribuicao = Escolher(BitolasDistribuicao, r.AsDistNec, 33);
        r.AsNegNec = 0.67 * rhoMin * b * h;
        r.Negativa = bal ? (0, 0) : Escolher(BitolasPrincipal.Where(p => p <= h * 10 / 8 + 1e-9).ToArray(), r.AsNegNec, Math.Min(2 * h, 20));
        var m5 = new PassoMemoria { Titulo = "5. Distribuição e construtivas (NBR 6118, 19.3.3.2; apostila 7.3.2.3 e 7.3.2.4)" };
        m5.Linhas.Add($"Distribuição ao longo do lance: ≥ 20 % da principal, ≥ 0,9 cm²/m e ≥ 0,5ρmín·b·h = {N(r.AsDistNec)} cm²/m → ø{N(r.Distribuicao.Bitola, "0.#")} c/{N(r.Distribuicao.Esp, "0.#")}.");
        if (bal) m5.Linhas.Add("Canto de cada degrau: 1 barra (todos os degraus ficam tracionados em cima; evita trinca visível no revestimento), ancorada no apoio.");
        else m5.Linhas.Add($"Negativa junto aos apoios laterais (a parede ou a viga restringe o giro): ρs ≥ 0,67ρmín = {N(r.AsNegNec)} cm²/m → ø{N(r.Negativa.Bitola, "0.#")} c/{N(r.Negativa.Esp, "0.#")} até 0,15ℓ.");
        r.Memoria.Add(m5);

        // ---------------- 6. cisalhamento ----------------
        double kk = Math.Max(1, 1.6 - r.D / 100), rho1 = Math.Min(0.02, r.AsPrincipal / (b * r.D)), tauRd = 0.25 * fctd;
        r.VRd1 = tauRd * kk * (1.2 + 40 * rho1) * b * r.D;
        r.Memoria.Add(new PassoMemoria
        {
            Titulo = "6. Cisalhamento sem armadura transversal (NBR 6118, 19.4.1)",
            Linhas = { $"VRd1 = τRd·k·(1,2 + 40ρ1)·bw·d = {N(tauRd * 10, "0.000")}·{N(kk, "0.000")}·(1,2 + 40·{N(rho1, "0.0000")})·100·{N(r.D, "0.0")}/10 = {N(r.VRd1, "0.0")} kN/m ≥ Vd = {N(r.VdMax, "0.0")} kN/m." },
        });
        r.Verificacoes.Add(K("K4", "Cisalhamento", "Força cortante sem estribos (VRd1)", $"Vd = {N(r.VdMax, "0.0")} kN/m", $"≤ VRd1 = {N(r.VRd1, "0.0")} kN/m", r.VdMax > r.VRd1 + 1e-9, "NBR 6118, 19.4.1",
            dica: "Aumente a espessura."));

        // ---------------- 7. ancoragem no apoio ----------------
        var rt = new RegrasTraspasse { Fck = e.Fck, GamaC = k.GamaC, GamaS = k.GamaS };
        double phiP = r.Principal.Bitola;
        r.Lb = rt.Lb(phiP, e.Aco);
        r.LbNec = Math.Ceiling(Math.Max(Math.Max(r.Lb * Math.Min(1, r.AsNec / r.AsPrincipal), 0.3 * r.Lb), Math.Max(phiP, 10)) - 1e-9);
        double reto = la - cob;
        double pernaGancho = Math.Max(0, Math.Ceiling(0.7 * r.LbNec - reto));   // com gancho, lb,nec × 0,7 (9.4.2.5); o resto desce dentro do apoio
        var m7 = new PassoMemoria { Titulo = "7. Ancoragem no apoio (NBR 6118, 9.4.2)" };
        if (bal)
        {
            m7.Linhas.Add($"Engaste: a barra de cima tem de ancorar lb,nec = {N(r.LbNec, "0")} cm dentro do apoio; reto disponível {N(reto, "0")} cm → " +
                          (reto >= r.LbNec ? "reta basta." : $"com gancho (0,7·lb,nec = {N(0.7 * r.LbNec, "0")} cm): perna de {N(pernaGancho, "0")} cm descendo no apoio."));
            r.Verificacoes.Add(K("K5", "Ancoragem", "Ancoragem da armadura do balanço no apoio", reto >= r.LbNec ? $"{N(reto, "0")} cm reto" : $"{N(reto, "0")} cm + gancho com perna de {N(pernaGancho, "0")} cm",
                $"lb,nec = {N(r.LbNec, "0")} cm", pernaGancho > 60, "NBR 6118, 9.4.2", SituacaoVerificacao.Conferir,
                "O apoio é estreito para ancorar: leve a barra até a laje do outro lado ou use um apoio mais largo."));
            m7.Linhas.Add("O apoio recebe o momento do balanço: a viga trabalha à torção (ou a parede à flexão) — verificar no cálculo do apoio.");
        }
        else
        {
            m7.Linhas.Add($"Nos apoios laterais a armadura principal entra {N(reto, "0")} cm; exigido ≥ 10φ = {N(phiP, "0")} cm.");
            r.Verificacoes.Add(K("K5", "Ancoragem", "Armadura principal dentro dos apoios laterais", $"{N(reto, "0")} cm", $"≥ 10φ = {N(phiP, "0")} cm", reto < phiP - 1e-9, "NBR 6118, 18.3.2.4.1"));
        }
        r.Memoria.Add(m7);

        // ---------------- 8. ELS ----------------
        double eci = e.Fck <= 50 ? k.AlfaE * 5600 * Math.Sqrt(e.Fck) : 21500 * k.AlfaE * Math.Pow(e.Fck / 10 + 1.25, 1.0 / 3);
        double ecs = Math.Min(1, 0.8 + 0.2 * e.Fck / 80) * eci, ecKn = ecs / 10, es = 210000;
        double ic = b * h * h * h / 12, mr = 1.5 * fctm * ic / (h / 2);
        double wqp = wg + k.Psi2 * wq, wfr = wg + k.Psi1 * wq;
        double ma = Math.Abs(bal ? M(0, wqp, pPar) : M(L / 2, wqp, 0));
        double aa = es / ecs * r.AsPrincipal, x2 = (-aa + Math.Sqrt(aa * aa + 2 * b * aa * r.D)) / b;
        double i2 = b * x2 * x2 * x2 / 3 + aa * (r.D - x2) * (r.D - x2);
        double rel = ma > 0 ? Math.Min(1, Math.Pow(mr / ma, 3)) : 1, ie = Math.Min(ic, rel * ic + (1 - rel) * i2);
        double Flecha(double yi, double eiv)
        {
            int n = 400; double hs = L / n, sum = 0;
            for (int i = 0; i <= n; i++)
            {
                double y = i * hs;
                double mi = bal ? (y < yi ? -(yi - y) : 0) : (y <= yi ? (1 - yi / L) * y : yi * (1 - y / L));
                double fv = M(y, wqp, pPar) * mi / eiv;
                sum += (i == 0 || i == n) ? fv / 2 : fv;
            }
            return sum * hs;
        }
        foreach (var p in r.Diagrama) p.FlechaElastica = Flecha(p.X, ecKn * ic);
        r.FlechaElasticaMax = r.Diagrama.Max(p => p.FlechaElastica);
        double xf = bal ? L : L / 2;
        r.FlechaImediata = Flecha(xf, ecKn * ie);
        double Xi(double t) => t > 70 ? 2 : 0.68 * Math.Pow(0.996, t) * Math.Pow(t, 0.32);
        double af = 2 - Xi(k.IdadeCarregamentoMeses);
        r.FlechaTotal = r.FlechaImediata * (1 + af);
        double lRef = bal ? 2 * L : L, lim = lRef / k.FlechaLimite;
        var m8 = new PassoMemoria { Titulo = "8. Estados-limite de serviço (NBR 6118, 17.3)" };
        m8.Linhas.Add($"Quase permanente: Ma = {N(ma / 100)} kN·m/m; Mr = {N(mr / 100)} kN·m/m; Ie = {N(ie, "0")} cm⁴/m (Branson).");
        m8.Linhas.Add($"Flecha {(bal ? "na borda livre" : "no meio")}: elástica {N(r.FlechaElasticaMax, "0.000")} cm; imediata {N(r.FlechaImediata, "0.000")} cm; total (αf = {N(af)}) {N(r.FlechaTotal, "0.00")} cm; " +
                      $"limite {(bal ? "2ℓ" : "ℓ")}/{N(k.FlechaLimite, "0")} = {N(lim, "0.00")} cm.");
        r.Verificacoes.Add(K("K6", "Serviço", "Flecha total (imediata + diferida)", $"{N(r.FlechaTotal, "0.00")} cm", $"≤ {(bal ? "2ℓ" : "ℓ")}/{N(k.FlechaLimite, "0")} = {N(lim, "0.00")} cm",
            r.FlechaTotal > lim + 1e-9, "NBR 6118, 17.3.2.1 e 13.3", dica: "Aumente a espessura."));
        double mf = Math.Abs(bal ? M(0, wfr, pPar) : M(L / 2, wfr, 0));
        double wLim = TabelasNbr.Classe(e.ClasseAgressividade) switch { ClasseAgressividade.I => 0.4, ClasseAgressividade.IV => 0.2, _ => 0.3 };
        if (mf <= mr) { r.Wk = 0; m8.Linhas.Add($"Frequente: M = {N(mf / 100)} kN·m/m ≤ Mr: não fissura."); }
        else
        {
            double a15 = 15 * r.AsPrincipal, xf15 = (-a15 + Math.Sqrt(a15 * a15 + 2 * b * a15 * r.D)) / b, if15 = b * Math.Pow(xf15, 3) / 3 + a15 * Math.Pow(r.D - xf15, 2);
            double sig = 15 * mf * (r.D - xf15) / if15 * 10, pc = phiP / 10, acr = r.Principal.Esp * Math.Min(cob + pc / 2 + 7.5 * pc, h / 2), rr = A(phiP) / acr;
            r.Wk = Math.Min(phiP / (12.5 * 2.25) * sig / es * 3 * sig / (fctm * 10), phiP / (12.5 * 2.25) * sig / es * (4 / rr + 45));
            m8.Linhas.Add($"Frequente: M = {N(mf / 100)} kN·m/m; σs = {N(sig, "0")} MPa; wk = {N(r.Wk, "0.000")} mm.");
        }
        r.Verificacoes.Add(K("K7", "Serviço", "Abertura de fissuras (combinação frequente)", $"wk = {N(r.Wk, "0.00")} mm", $"≤ {N(wLim, "0.0")} mm", r.Wk > wLim + 1e-9, "NBR 6118, 17.3.3.2"));
        r.Memoria.Add(m8);

        GerarBarrasLateral(r, pernaGancho);
    }

    /// <summary>
    /// Barras da transversal e do balanço. A principal corre na largura (repetida ao longo do lance): embaixo, de apoio a apoio, na
    /// transversal; em cima, do apoio (com gancho para baixo dentro dele, se precisar) à borda livre (dobrada para baixo), no balanço.
    /// A distribuição corre ao longo do lance. Transversal: negativas junto aos dois apoios. Balanço: uma barra no canto de cada degrau.
    /// O apoio do balanço fica do lado y &lt; 0 (à esquerda de quem sobe).
    /// </summary>
    private static void GerarBarrasLateral(ResultadoEscada r, double pernaGancho)
    {
        var e = r.Entrada; var g = r.Geo;
        bool bal = e.Tipo == TipoEscada.Balanco;
        double cob = e.Cobrimento, h = g.H, c = g.Cos, s = g.Sen, tg = Math.Tan(g.Alfa), B = e.Largura, la = e.LarguraApoio;
        double phi = r.Principal.Bitola, pd = r.Distribuicao.Bitola;
        // linhas paralelas ao lance (x de ponta a ponta do lance, com cobrimento nas faces das pontas)
        double x0 = cob, x1 = g.Vao - cob;
        (double X, double Z) NaLinha(double x, double dPerp) => (x, (x - g.X1) * tg - h / c + dPerp / c);   // dPerp: distância perpendicular a partir da face de baixo
        List<(double X, double Z)> Linha(double dPerp) => new List<(double X, double Z)> { NaLinha(x0, dPerp), NaLinha(x1, dPerp) };
        double compLance = (x1 - x0) / c;
        int Qt(double comp, double esp) => QtReta(comp, esp);
        double yApoio = B / 2 + la - cob;   // dentro do apoio
        double desce = h - 2 * cob - phi / 10;
        if (!bal)
        {
            double t1 = cob + phi / 20;
            var n1 = new BarraEscada
            {
                Nome = "N1", Funcao = "principal na largura (embaixo, de apoio a apoio)", Bitola = phi, Espacamento = r.Principal.Esp, Transversal = true,
                Pontos = Linha(t1), Quantidade = Qt(compLance, r.Principal.Esp), PerfilY = { (-yApoio, 0), (yApoio, 0) }, Comprimento = Math.Ceiling(2 * yApoio),
            };
            r.Barras.Add(n1);
            double t2 = cob + phi / 10 + pd / 20;
            r.Barras.Add(new BarraEscada
            {
                Nome = "N2", Funcao = "distribuição ao longo do lance", Bitola = pd, Espacamento = r.Distribuicao.Esp, Pontos = Linha(t2),
                Quantidade = Qt(B - 2 * cob - pd / 10, r.Distribuicao.Esp), Comprimento = Math.Ceiling(compLance),
            });
            var ng = r.Negativa;
            if (ng.Esp > 0)
            {
                double t3 = h - cob - ng.Bitola / 20, ext = 0.15 * r.VaoCalculo;
                foreach (var (nome, lado) in new[] { ("N3", -1), ("N4", 1) })
                    r.Barras.Add(new BarraEscada
                    {
                        Nome = nome, Funcao = $"negativa junto ao apoio {(lado < 0 ? "esquerdo" : "direito")}", Bitola = ng.Bitola, Espacamento = ng.Esp, Transversal = true,
                        Pontos = Linha(t3), Quantidade = Qt(compLance, ng.Esp),
                        PerfilY = { (lado * yApoio, -desce), (lado * yApoio, 0), (lado * (B / 2 - ext), 0) },
                        Comprimento = Math.Ceiling(desce + (yApoio - (B / 2 - ext))),
                    });
            }
        }
        else
        {
            double t1 = h - cob - phi / 20;
            var perfil = new List<(double Y, double N)>();
            if (pernaGancho > 0) perfil.Add((-yApoio, -Math.Min(pernaGancho, 60)));
            perfil.Add((-yApoio, 0)); perfil.Add((B / 2 - cob, 0)); perfil.Add((B / 2 - cob, -desce));
            r.Barras.Add(new BarraEscada
            {
                Nome = "N1", Funcao = "principal do balanço (em cima, ancorada no apoio)", Bitola = phi, Espacamento = r.Principal.Esp, Transversal = true,
                Pontos = Linha(t1), Quantidade = Qt(compLance, r.Principal.Esp), PerfilY = perfil, Comprimento = Math.Ceiling(CompPerfil(perfil)),
            });
            double t2 = h - cob - phi / 10 - pd / 20;
            r.Barras.Add(new BarraEscada
            {
                Nome = "N2", Funcao = "distribuição ao longo do lance", Bitola = pd, Espacamento = r.Distribuicao.Esp, Pontos = Linha(t2),
                Quantidade = Qt(B - 2 * cob - pd / 10, r.Distribuicao.Esp), Comprimento = Math.Ceiling(compLance),
            });
            // canto de cada degrau: um ponto por degrau, dentro do canto (cobrimento nas duas faces)
            double pc = Math.Max(8, phi);
            int nd = g.N - 1;
            if (nd >= 1)
            {
                double co = cob + pc / 20;
                var p0 = (g.X1 + co, g.Espelho - co);
                var p1 = (g.X1 + (nd - 1) * g.Piso + co, nd * g.Espelho - co);
                var perf = new List<(double Y, double N)> { (-yApoio, 0), (B / 2 - cob, 0) };
                r.Barras.Add(new BarraEscada
                {
                    Nome = "N3", Funcao = "canto de cada degrau (1 por degrau)", Bitola = pc, Espacamento = g.Piso / c, Transversal = true,
                    Pontos = nd > 1 ? new List<(double X, double Z)> { p0, p1 } : new List<(double X, double Z)> { p0, (p0.Item1 + 1, p0.Item2 + tg) },
                    Quantidade = nd, PerfilY = perf, Comprimento = Math.Ceiling(CompPerfil(perf)),
                });
            }
        }
    }

    public static double CompPerfil(IReadOnlyList<(double Y, double N)> p)
    {
        double s = 0;
        for (int i = 1; i < p.Count; i++) s += Math.Sqrt(Math.Pow(p[i].Y - p[i - 1].Y, 2) + Math.Pow(p[i].N - p[i - 1].N, 2));
        return s;
    }
}
