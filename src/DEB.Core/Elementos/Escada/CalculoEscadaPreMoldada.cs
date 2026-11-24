using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Documentacao;
using DEB.Core.Elementos.Viga.PreMoldada.Calculo;
using DEB.Core.Normas;
using DEB.Core.Normas.Verificacao;

namespace DEB.Core.Elementos.Escada;

/// <summary>Resultado da ligação de uma ponta da escada pré-moldada (por metro de largura, salvo onde dito).</summary>
public sealed class LigacaoCalculada
{
    public string Lado { get; set; } = "";
    public LigacaoEscada Ligacao { get; set; }
    /// <summary>Reação vertical de cálculo e força horizontal (kN/m).</summary>
    public double Fd { get; set; }
    public double Hd { get; set; }
    public double CoefHd { get; set; }
    /// <summary>Apoio direto: tirante (Fd/1,2 + Hd)/fyd e costura Fd/(8fyd); dente: tirante e suspensão (cm²/m).</summary>
    public double AsTirante { get; set; }
    public double AsCostura { get; set; }
    public double AsSuspensao { get; set; }
    public (double Bitola, double Esp) Tirante { get; set; }
    public (double Bitola, double Esp) Suspensao { get; set; }
    /// <summary>Dente: altura útil, distância do apoio ao recorte, a/d e a biela.</summary>
    public double DDente { get; set; }
    public double ADente { get; set; }
    public double VRd2Dente { get; set; }
    /// <summary>Pinos: força horizontal na ponta inteira (kN) e resistência de um pino (kN).</summary>
    public double HTotal { get; set; }
    public double RvdPino { get; set; }
}

/// <summary>Içamento da peça (NBR 9062, 5.3): 4 alças a 0,207 do comprimento a partir das pontas.</summary>
public sealed class IcamentoEscada
{
    public double Peso { get; set; }          // kN, peça inteira
    public double Comprimento { get; set; }   // cm, desenvolvido no eixo
    public double DistAlca { get; set; }      // cm, da ponta à alça (no eixo)
    public double MdNeg { get; set; }         // kN·m/m
    public double MdPos { get; set; }
    public double Fcj { get; set; }
    public double AsNeg { get; set; }
    public double AsPos { get; set; }
    /// <summary>Força em uma alça (kN): peso × coeficiente dinâmico / 3 (só 3 das 4 garantidas).</summary>
    public double ForcaAlca { get; set; }
    /// <summary>Posição das alças na elevação (x, z do topo).</summary>
    public List<(double X, double Z)> Alcas { get; } = new List<(double, double)>();
}

/// <summary>
/// Escada pré-moldada (D43): lance biapoiado entre os centros dos apoios. Por ponta: Fd (reação de cálculo, com o peso do trecho
/// além do apoio), Hd = k·Fd pelo material do apoio (NBR 9062, 7.3.9); apoio direto (7.5): As = (Fd/1,2 + Hd)/fyd e
/// Ash = Asv = Fd/(8fyd) em grampos em U; dente (7.4): tirante, suspensão e biela; pinos ao corte (Rvd = 1,0·φ²·√(fcd·fyd), a
/// conferir) e disposições da Tabela 12. Içamento (5.3) com 4 alças, coeficiente dinâmico e fcj da desforma. As barras ficam
/// todas dentro da peça.
/// </summary>
public static partial class CalculoEscada
{
    private static void CalcularPreMoldada(ResultadoEscada r, CoeficientesViga k)
    {
        var e = r.Entrada; var g = r.Geo;
        string N(double x, string f = "0.##") => Fmt.Virgula(x, f);
        double gc = e.PesoEspecifico > 0 ? e.PesoEspecifico : 25, cob = e.Cobrimento, hp = g.Hp, h = g.H, b = 100;
        double fcd = e.Fck / k.GamaC / 10, fyd = TabelasNbr.Fyk(e.Aco) / k.GamaS / 10, fywd = Math.Min(TabelasNbr.Fyk(e.Aco) / k.GamaS, 435) / 10;
        double rhoMin = TabelasNbr.RhoMinVigaPct(e.Fck) / 100;
        var rt = new RegrasTraspasse { Fck = e.Fck, GamaC = k.GamaC, GamaS = k.GamaS };
        var mat = new Flexao(e.Fck, fcd, fyd);

        // ---------------- 9. ligações ----------------
        var m9 = new PassoMemoria { Titulo = "9. Ligações da peça pré-moldada (NBR 9062, 7.3.9, 7.4, 7.5 e Tabela 12)" };
        foreach (var (lado, lig, rG, rQ) in new[] { ("inferior", e.LigacaoInferior, r.RaG, r.RaQ), ("superior", e.LigacaoSuperior, r.RbG, r.RbQ) })
        {
            var lc = new LigacaoCalculada { Lado = lado, Ligacao = lig, CoefHd = PadroesPreMoldado.CoefHd(lig.Apoio) };
            double hApoio = lig.AlturaNoApoio(hp);
            double gBal = gc * hApoio / 100 * lig.Balanco / 100 + e.Revestimento * lig.Balanco / 100;   // trecho além do centro do apoio
            lc.Fd = k.GamaG * (rG + gBal) + k.GamaQ * rQ;
            lc.Hd = lc.CoefHd * lc.Fd;
            string grupo = $"Ligação {lado}";
            m9.Linhas.Add($"Ponta {lado}: {(lig.Ponta == PontaPreMoldada.Dente ? $"dente de {N(hApoio, "0.#")} cm × {N(lig.ComprimentoDente, "0")} cm" : "apoio direto")}, apoio de {N(lig.ComprimentoApoio, "0")} cm em " +
                          $"{PadroesPreMoldado.Nome(lig.Apoio)}; Fd = {N(lc.Fd, "0.0")} kN/m (com o trecho além do apoio); Hd = {N(lc.CoefHd)}·Fd = {N(lc.Hd, "0.0")} kN/m.");
            if (lig.Ponta == PontaPreMoldada.ApoioDireto)
            {
                lc.AsTirante = (lc.Fd / 1.2 + lc.Hd) / fyd;
                lc.AsCostura = lc.Fd / (8 * fyd);
                lc.Tirante = Escolher(new[] { 6.3, 8, 10, 12.5 }, Math.Max(lc.AsTirante, lc.AsCostura), 20);
                double asT = lc.Tirante.Esp > 0 ? A(lc.Tirante.Bitola) * 100 / lc.Tirante.Esp : 0;
                m9.Linhas.Add($"  Apoio sem recorte (7.5): As = (Fd/1,2 + Hd)/fyd = {N(lc.AsTirante)} cm²/m; costura Ash = Asv = Fd/(8fyd) = {N(lc.AsCostura)} cm²/m → " +
                              $"grampos em U ø{N(lc.Tirante.Bitola, "0.#")} c/{N(lc.Tirante.Esp, "0.#")} = {N(asT)} cm²/m (as pernas fazem a costura).");
                r.Verificacoes.Add(K("P1", grupo, "Tirante do apoio direto (grampos em U)", $"{N(asT)} cm²/m", $"≥ {N(Math.Max(lc.AsTirante, lc.AsCostura))} cm²/m",
                    asT < Math.Max(lc.AsTirante, lc.AsCostura) - 1e-9, "NBR 9062, 7.5"));
            }
            else
            {
                double phiT = 10;
                lc.DDente = hApoio - cob - phiT / 20;
                lc.ADente = Math.Abs(lado == "inferior" ? g.XRecorteInf - 0 : g.Vao - g.XRecorteSup);
                double ad = lc.DDente > 0 ? lc.ADente / lc.DDente : 99;
                lc.AsTirante = ad <= 0.5 ? 0.8 * lc.Fd / (fyd * 1.4) + lc.Hd / fyd : (0.1 + ad) * lc.Fd / fyd + lc.Hd / fyd;
                lc.AsSuspensao = lc.Fd / fywd;
                lc.Tirante = Escolher(new[] { 8, 10, 12.5 }, lc.AsTirante, 20);
                lc.Suspensao = Escolher(new[] { 6.3, 8, 10, 12.5 }, lc.AsSuspensao / 2, 20);   // grampo de 2 pernas
                double av2 = 1 - e.Fck / 250;
                lc.VRd2Dente = 0.27 * av2 * fcd * b * lc.DDente;
                double asT = lc.Tirante.Esp > 0 ? A(lc.Tirante.Bitola) * 100 / lc.Tirante.Esp : 0, asS = lc.Suspensao.Esp > 0 ? 2 * A(lc.Suspensao.Bitola) * 100 / lc.Suspensao.Esp : 0;
                m9.Linhas.Add($"  Dente (7.4): d = {N(lc.DDente, "0.0")} cm; a = {N(lc.ADente, "0.0")} cm (centro do apoio → recorte); a/d = {N(ad)}; " +
                              $"tirante {(ad <= 0.5 ? "0,8·Fd/(fyd·μ)" : "(0,1 + a/d)·Fd/fyd")} + Hd/fyd = {N(lc.AsTirante)} cm²/m → ø{N(lc.Tirante.Bitola, "0.#")} c/{N(lc.Tirante.Esp, "0.#")}; " +
                              $"suspensão Fd/fywd = {N(lc.AsSuspensao)} cm²/m → grampos ø{N(lc.Suspensao.Bitola, "0.#")} c/{N(lc.Suspensao.Esp, "0.#")}; biela VRd2 = {N(lc.VRd2Dente, "0.0")} kN/m.");
                r.Verificacoes.Add(K("P1", grupo, "Tirante do dente", $"{N(asT)} cm²/m", $"≥ {N(lc.AsTirante)} cm²/m", asT < lc.AsTirante - 1e-9 || ad > 1, "NBR 9062, 7.4.4",
                    ad > 1 ? SituacaoVerificacao.Conferir : SituacaoVerificacao.NaoAtende, ad > 1 ? "a/d > 1: o dente funciona como balanço — aumente a altura do dente." : "Aumente a altura do dente."));
                r.Verificacoes.Add(K("P2", grupo, "Suspensão no recorte do dente", $"{N(asS)} cm²/m", $"≥ {N(lc.AsSuspensao)} cm²/m", asS < lc.AsSuspensao - 1e-9, "NBR 9062, 7.4.6"));
                r.Verificacoes.Add(K("P3", grupo, "Biela do dente (VRd2 com o d do dente)", $"Fd = {N(lc.Fd, "0.0")} kN/m", $"≤ {N(lc.VRd2Dente, "0.0")} kN/m", lc.Fd > lc.VRd2Dente, "NBR 9062, 7.4.7",
                    dica: "Aumente a altura do dente."));
            }
            // pinos
            double phiP = lig.BitolaPino / 10;   // cm
            lc.HTotal = lc.Hd * e.Largura / 100;
            lc.RvdPino = 1.0 * phiP * phiP * Math.Sqrt(fcd * fyd);
            double resist = lig.Pinos * lc.RvdPino;
            m9.Linhas.Add($"  Pinos: {lig.Pinos} ø{N(lig.BitolaPino, "0.#")} (furo ø{N(lig.FuroPino, "0")} mm), Hd na ponta = {N(lc.HTotal, "0.0")} kN; Rvd = 1,0·φ²·√(fcd·fyd) = {N(lc.RvdPino, "0.0")} kN por pino " +
                          $"→ {N(resist, "0.0")} kN; distância do pino à ponta da peça {N(lig.Balanco, "0.#")} cm (mín. 4φ = {N(4 * phiP, "0.#")}, crítica 7φ = {N(7 * phiP, "0.#")}); " +
                          $"embutimento {N(lig.EmbutimentoPino, "0")} cm (8φ a 20φ = {N(8 * phiP, "0")} a {N(20 * phiP, "0")}).");
            if (lig.Pinos > 0)
            {
                r.Verificacoes.Add(K("P4", grupo, "Pinos ao corte (força horizontal da ponta)", $"{N(resist, "0.0")} kN", $"≥ Hd = {N(lc.HTotal, "0.0")} kN", resist < lc.HTotal - 1e-9,
                    "NBR 9062, 7.2; Rvd = 1,0·φ²·√(fcd·fyd)", dica: "Aumente a bitola ou o número de pinos."));
                bool bordaMin = lig.Balanco < 4 * phiP - 1e-9, bordaCrit = lig.Balanco < 7 * phiP - 1e-9;
                r.Verificacoes.Add(K("P5", grupo, "Distância do pino à ponta da peça", $"{N(lig.Balanco, "0.#")} cm", $"≥ 4φ = {N(4 * phiP, "0.#")} cm (crítica 7φ = {N(7 * phiP, "0.#")})",
                    bordaCrit, "NBR 9062, Tabela 12", bordaMin ? SituacaoVerificacao.NaoAtende : SituacaoVerificacao.Conferir,
                    "Aumente o comprimento de apoio (o pino fica no centro do apoio)."));
                bool emb = lig.EmbutimentoPino < 8 * phiP - 1e-9 || lig.EmbutimentoPino > 20 * phiP + 1e-9;
                r.Verificacoes.Add(K("P6", grupo, "Embutimento do pino no apoio", $"{N(lig.EmbutimentoPino, "0")} cm", $"8φ a 20φ = {N(8 * phiP, "0")} a {N(20 * phiP, "0")} cm", emb, "NBR 9062, Tabela 12"));
                double sPino = e.Largura / lig.Pinos;
                r.Verificacoes.Add(K("P7", grupo, "Furo do pino (folga de montagem)", $"ø{N(lig.FuroPino, "0")} mm", $"≥ φ + 15 mm = {N(lig.BitolaPino + 15, "0")} mm", lig.FuroPino < lig.BitolaPino + 15 - 1e-9,
                    "NBR 9062, 7.2.2.2.2 (tolerâncias)", SituacaoVerificacao.Conferir));
                if (sPino < 4 * phiP) r.Avisos.Add($"Pinos da ponta {lado} muito próximos ({N(sPino, "0")} cm).");
            }
            r.Ligacoes.Add(lc);
        }
        r.Memoria.Add(m9);

        // ---------------- 10. içamento ----------------
        var ic = new IcamentoEscada();
        double area = Math.Abs(Area(g.Contorno));
        double pesoM = gc * area / 1e4;                         // kN por metro de largura
        ic.Peso = pesoM * e.Largura / 100;
        // comprimento desenvolvido: as pontas além dos apoios + os trechos do eixo
        ic.Comprimento = g.Trechos.Sum(t => t.Comprimento) + (0 - g.XPontaInf) + (g.XPontaSup - g.Vao);
        ic.DistAlca = 0.207 * ic.Comprimento;
        double w = pesoM / ic.Comprimento;                     // kN/cm por metro de largura
        double fat = k.GamaG * e.CoefDinamico;
        double mNeg = w * ic.DistAlca * ic.DistAlca / 2, mPos = Math.Max(0, w * Math.Pow(ic.Comprimento - 2 * ic.DistAlca, 2) / 8 - mNeg);
        ic.MdNeg = fat * mNeg / 100; ic.MdPos = fat * mPos / 100;
        ic.Fcj = e.FcjIcamento > 0 ? e.FcjIcamento : 0.7 * e.Fck;
        var matJ = new Flexao(ic.Fcj, ic.Fcj / k.GamaC / 10, fyd);
        double dSup = Math.Min(h, hp) - cob - 0.5, dInf = r.D > 0 ? r.D : Math.Min(h, hp) - cob - 0.5;
        ic.AsNeg = matJ.Armadura(ic.MdNeg * 100, b, dSup, cob).As;
        ic.AsPos = matJ.Armadura(ic.MdPos * 100, b, dInf, cob).As;
        ic.ForcaAlca = ic.Peso * e.CoefDinamico / 3;
        // alças no eixo, levadas ao topo (nas pontas: patamares; no lance: sobre o degrau)
        foreach (double sAl in new[] { ic.DistAlca, ic.Comprimento - ic.DistAlca })
        {
            double x = PontoNoEixo(g, sAl).X;
            ic.Alcas.Add((x, TopoEm(g, e, x)));
        }
        r.Icamento = ic;
        double asSupNec = Math.Max(ic.AsNeg, 0.67 * rhoMin * b * Math.Min(h, hp));
        r.AsSuperiorPm = asSupNec;
        r.SuperiorPm = Escolher(BitolasPrincipal.Where(p => p <= Math.Min(h, hp) * 10 / 8 + 1e-9).ToArray(), asSupNec, Math.Min(2 * Math.Min(h, hp), 20));
        double asSupEf = r.SuperiorPm.Esp > 0 ? A(r.SuperiorPm.Bitola) * 100 / r.SuperiorPm.Esp : 0;
        var m10 = new PassoMemoria { Titulo = "10. Içamento e transporte (NBR 9062, 5.3)" };
        m10.Linhas.Add($"Peça: {N(area / 1e4, "0.000")} m² × {N(e.Largura / 100, "0.00")} m × {N(gc, "0")} kN/m³ = {N(ic.Peso, "0.0")} kN ({N(ic.Peso * 101.97, "0")} kg); comprimento desenvolvido {N(ic.Comprimento, "0")} cm.");
        m10.Linhas.Add($"4 alças a 0,207·ℓ = {N(ic.DistAlca, "0")} cm das pontas (momentos iguais); γg·βdin = {N(k.GamaG)}·{N(e.CoefDinamico)}; fcj = {N(ic.Fcj, "0.#")} MPa.");
        m10.Linhas.Add($"Md− = {N(ic.MdNeg)} kN·m/m nas alças → As,sup = {N(ic.AsNeg)} cm²/m; Md+ = {N(ic.MdPos)} kN·m/m no meio → As,inf = {N(ic.AsPos)} cm²/m (tem {N(r.AsPrincipal)}).");
        m10.Linhas.Add($"Armadura superior da peça (içamento e construtiva ≥ 0,67ρmín): ø{N(r.SuperiorPm.Bitola, "0.#")} c/{N(r.SuperiorPm.Esp, "0.#")} = {N(asSupEf)} cm²/m.");
        m10.Linhas.Add($"Força por alça (3 das 4 garantidas): {N(ic.ForcaAlca, "0.0")} kN — escolha o inserto com essa carga de trabalho.");
        r.Memoria.Add(m10);
        r.Verificacoes.Add(K("I1", "Içamento", "Armadura superior no içamento", $"{N(asSupEf)} cm²/m", $"≥ {N(ic.AsNeg)} cm²/m", asSupEf < ic.AsNeg - 1e-9, "NBR 9062, 5.3"));
        r.Verificacoes.Add(K("I2", "Içamento", "Armadura inferior no içamento", $"{N(r.AsPrincipal)} cm²/m", $"≥ {N(ic.AsPos)} cm²/m", r.AsPrincipal < ic.AsPos - 1e-9, "NBR 9062, 5.3"));

        // ---------------- barras ----------------
        if (r.Principal.Esp <= 0) return;
        GerarBarras(r);
        GerarBarrasPreMoldada(r);
    }

    private static (double X, double Z) PontoNoEixo(GeometriaEscada g, double s)
    {
        // s a partir da ponta inferior: o trecho além do apoio inferior é horizontal
        double ext = -g.XPontaInf;
        if (s <= ext) return (g.XPontaInf + s, g.Trechos[0].P0.Z);
        s -= ext;
        foreach (var t in g.Trechos)
        {
            if (s <= t.Comprimento) { double u = s / t.Comprimento; return (t.P0.X + (t.P1.X - t.P0.X) * u, t.P0.Z + (t.P1.Z - t.P0.Z) * u); }
            s -= t.Comprimento;
        }
        var ult = g.Trechos[g.Trechos.Count - 1];
        return (Math.Min(g.XPontaSup, ult.P1.X + s), ult.P1.Z);
    }

    private static double TopoEm(GeometriaEscada g, EscadaLaje e, double x)
    {
        if (x <= g.X1) return 0;
        if (x >= g.X2) return e.Desnivel;
        int i = (int)Math.Floor((x - g.X1) / g.Piso) + 1;   // espelho à esquerda
        return i * g.Espelho;
    }

    /// <summary>A dobra cabe se o trecho entre as duas dobras tiver pelo menos 6φ (pino de 5φ do CA-50 mais a barra).</summary>
    private static bool DobraCabe(double trechoCm, double phiMm) => trechoCm >= 6 * phiMm / 10 - 1e-9;

    /// <summary>Tira o gancho de uma ponta quando ele é curto demais para dobrar (a barra fica reta naquela ponta).</summary>
    private static List<(double X, double Z)> SemGanchoCurto(List<(double X, double Z)> p, double phiMm)
    {
        double L((double X, double Z) a, (double X, double Z) b) => Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Z - a.Z, 2));
        if (p.Count > 2 && !DobraCabe(L(p[p.Count - 2], p[p.Count - 1]), phiMm)) p.RemoveAt(p.Count - 1);
        if (p.Count > 2 && !DobraCabe(L(p[0], p[1]), phiMm)) p.RemoveAt(0);
        return p;
    }

    internal static double Area(IReadOnlyList<(double X, double Z)> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count; i++) { var u = p[i]; var v = p[(i + 1) % p.Count]; a += u.X * v.Z - v.X * u.Z; }
        return a / 2;
    }

    /// <summary>Barras próprias da peça: superior (içamento e construtiva) cruzando nas quinas, grampos de apoio ou tirante e
    /// suspensão dos dentes.</summary>
    private static void GerarBarrasPreMoldada(ResultadoEscada r)
    {
        var e = r.Entrada; var g = r.Geo;
        double cob = e.Cobrimento, h = g.H, hp = g.Hp, H = e.Desnivel, c = g.Cos, tg = Math.Tan(g.Alfa), B = e.Largura;
        var sp = r.SuperiorPm;
        var rt = new RegrasTraspasse { Fck = e.Fck, GamaC = r.Coef.GamaC, GamaS = r.Coef.GamaS };
        if (sp.Esp > 0 && e.SoPatamar)
        {
            // patamar: superior reta de ponta a ponta, com gancho para baixo nas pontas
            double co = cob + sp.Bitola / 20;
            double xa = g.XPontaInf + co, xb = g.XPontaSup - co;
            var n4 = SemGanchoCurto(new List<(double X, double Z)> { (xa, g.ZAssentoInf + co), (xa, -co), (xb, -co), (xb, g.ZAssentoSup + co) }, sp.Bitola);
            r.Barras.Add(Barra("N4", "superior do patamar (içamento e construtiva)", sp.Bitola, sp.Esp, QtReta(B - 2 * cob - sp.Bitola / 10, sp.Esp), n4));
        }
        else if (sp.Esp > 0)
        {
            double co = cob + sp.Bitola / 20;
            int nL = QtReta(B - 2 * cob - sp.Bitola / 10, sp.Esp);
            // N4: superior do lance: sai do fundo do patamar inferior (cruzando), sobe pelo topo do lance e segue no topo do patamar superior até a ponta
            double xs = g.X1 + (-hp + co + co / c) / tg;              // topo do lance encontra o fundo do patamar inferior
            double xk = g.X1 + (H - co + co / c) / tg;               // topo do lance encontra o topo do patamar superior
            double xE = g.XPontaSup - cob - sp.Bitola / 20;
            double zGanchoS = Math.Max(H - hp, g.ZAssentoSup) + co;
            var n4 = SemGanchoCurto(new List<(double X, double Z)> { (xs, -hp + co), (xk, H - co), (xE, H - co), (xE, zGanchoS) }, sp.Bitola);
            r.Barras.Add(Barra("N4", "superior do lance e do patamar superior (içamento e construtiva)", sp.Bitola, sp.Esp, nL, n4));
            // N5: superior do patamar inferior: da ponta (gancho para baixo) até cruzar o fundo do lance
            double xI = g.XPontaInf + cob + sp.Bitola / 20;
            double zGanchoI = Math.Min(-hp, g.ZAssentoInf) + co;
            double xf = g.X1 + (-co + h / c - co / c) / tg;           // topo do patamar inferior encontra o fundo do lance
            var n5 = SemGanchoCurto(new List<(double X, double Z)> { (xI, Math.Max(zGanchoI, g.ZAssentoInf + co)), (xI, -co), (xf, -co) }, sp.Bitola);
            r.Barras.Add(Barra("N5", "superior do patamar inferior (içamento e construtiva)", sp.Bitola, sp.Esp, nL, n5));
        }
        int k = 6;
        foreach (var lc in r.Ligacoes)
        {
            bool inf = lc.Lado == "inferior";
            double sgn = inf ? 1 : -1;
            double xP = inf ? g.XPontaInf : g.XPontaSup, xR = inf ? g.XRecorteInf : g.XRecorteSup;
            double zTop = inf ? 0 : H, zBase = inf ? -hp : H - hp, zAss = inf ? g.ZAssentoInf : g.ZAssentoSup;
            var t = lc.Tirante;
            if (t.Esp <= 0) continue;
            double cot = cob + t.Bitola / 20;
            double xg = xP + sgn * cot;
            double lb = Math.Ceiling(rt.Lb(t.Bitola, e.Aco));
            int nL = QtReta(B - 2 * cob - t.Bitola / 10, t.Esp);
            if (lc.Ligacao.Ponta == PontaPreMoldada.ApoioDireto)
            {
                double lg = Math.Max(lb, lc.Ligacao.ComprimentoApoio + 10);
                var u = new List<(double X, double Z)> { (xg + sgn * lg, zTop - cot), (xg, zTop - cot), (xg, zBase + cot), (xg + sgn * lg, zBase + cot) };
                r.Barras.Add(Barra("N" + k++, $"grampo do apoio {lc.Lado} (tirante e costura)", t.Bitola, t.Esp, nL, u));
            }
            else
            {
                // tirante do dente: U dentro da altura do dente, passando do recorte o comprimento de ancoragem
                double xa = xR + sgn * lb;
                var u = new List<(double X, double Z)> { (xa, zTop - cot), (xg, zTop - cot), (xg, zAss + cot), (xa, zAss + cot) };
                if (!DobraCabe(zTop - zAss - 2 * cot, t.Bitola))
                {
                    // dente baixo: o laço não cabe — tirante reto, ancorado na ponta por barra transversal soldada
                    u = new List<(double X, double Z)> { (xg, zAss + cot), (xa, zAss + cot) };
                    r.Avisos.Add($"Dente {lc.Lado}: altura pequena para o laço do tirante ø{Fmt.Virgula(t.Bitola, "0.#")} — tirante reto; ancore na ponta com barra transversal soldada (NBR 9062, 7.3.3) ou aumente o dente.");
                }
                r.Barras.Add(Barra("N" + k++, $"tirante do dente {lc.Lado}", t.Bitola, t.Esp, nL, u));
                var s = lc.Suspensao;
                if (s.Esp > 0)
                {
                    double cs = cob + s.Bitola / 20, x1 = xR + sgn * cs, x2 = x1 + sgn * 12;
                    var su = new List<(double X, double Z)> { (x1, zTop - cs), (x1, zBase + cs), (x2, zBase + cs), (x2, zTop - cs) };
                    r.Barras.Add(Barra("N" + k++, $"suspensão do dente {lc.Lado}", s.Bitola, s.Esp, QtReta(B - 2 * cob - s.Bitola / 10, s.Esp), su));
                }
            }
        }
    }
}
