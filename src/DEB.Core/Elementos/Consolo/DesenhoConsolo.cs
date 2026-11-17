using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Documentacao;
using DEB.Core.Modelo;
using DEB.Core.Motor;

namespace DEB.Core.Elementos.Consolo;

/// <summary>
/// O que o elemento que recebe os consolos passa para o desenho do detalhe deles (D27: o consolo não conhece quem o usa).
/// A seção é um retângulo <see cref="B"/> × <see cref="H"/> com as faces numeradas como no pilar (1 = +x, 2 = −y, 3 = −x,
/// 4 = +y, numeração interna); medidas em cm, bitolas em mm.
/// </summary>
internal sealed class ContextoConsolo
{
    /// <summary>Contexto de desenho (resultado, idioma, escalas e alturas de texto).</summary>
    public Ctx Desenho;
    public ConfiguracaoArmadura Cfg;
    /// <summary>Seção do elemento: lado em x (<see cref="B"/>) e em y (<see cref="H"/>).</summary>
    public double B, H;
    /// <summary>Cobrimento de cada face (índice = face − 1).</summary>
    public double[] Cob;
    /// <summary>Aço das barras longitudinais (tirante, armadura inferior, pino) e dos estribos (vertical, costura).</summary>
    public string AcoLong, AcoEst;
    /// <summary>Maiores bitolas do estribo e da longitudinal do elemento: o tirante e a costura param por dentro delas.</summary>
    public double PhiEstriboApoio, PhiLongApoio;
}

/// <summary>
/// Desenho do consolo (saiu do template do pilar, 16/11/2026, D27): forma de frente e de lado nas vistas, planta no corte de
/// forma, detalhe "ARMADURAS CONSOLO b x h x a" com corte, planta e as formas das barras (tirante com barra transversal
/// soldada ou em laço, costura, estribos verticais, armadura inferior, pinos) e as regras da NBR 9062, 7.3 (avisos). Quem usa
/// o consolo decide onde ele fica e passa a posição (vistas, corte) ou o <see cref="ContextoConsolo"/> (detalhe).
/// </summary>
internal static class DesenhoConsolo
{
    /// <summary>
    /// Distância mínima a2 da face externa do aparelho de apoio à ponta do consolo (NBR 9062, 7.3.3.4), arredondada a 0,5 cm:
    /// c + φ (barra soldada); c + 3,5φ (laço, φ &lt; 20); c + 5φ (laço, φ ≥ 20).
    /// </summary>
    internal static double A2Minimo(double cob, double phiTirante, bool laco)
    {
        double f = !laco ? 1 : phiTirante < 20 ? 3.5 : 5;
        return Math.Ceiling((cob + f * phiTirante / 10) * 2 - 1e-9) / 2;
    }

    /// <summary>Posições dos pinos ao longo da largura (a ±b/4 para 2, como no P61) e a distância deles à face.</summary>
    private static List<double> PinosU(ConsoloPilar c)
    {
        if (c.PinoN <= 0) return new List<double>();
        if (c.PinoN == 1) return new List<double> { 0 };
        double ext = c.B / 4;
        return Enumerable.Range(0, c.PinoN).Select(i => -ext + i * 2 * ext / (c.PinoN - 1)).ToList();
    }

    /// <summary>Faixa da almofada no sentido do balanço (distância à face do pilar) e o centro (onde ficam os pinos).</summary>
    private static (double n0, double n1, double centro) Almofada(ConsoloPilar c, double cob)
    {
        double a2 = A2Minimo(cob, c.TirPhi, c.Laco);
        double n1 = c.A - a2, n0 = Math.Max(0, n1 - c.AlmA);
        return (n0, n1, c.AlmA > 0 ? (n0 + n1) / 2 : c.A / 2);
    }

    private static ResultadoTemplate R(Ctx c) => c.R;
    private static string Num(double v, string formato) => Fmt.Virgula(v, formato);
    private static (int b, int c, int t, int ci, int co) Marca(ResultadoTemplate r) => KitDesenho.Marca(r);
    private static double MaxX(ResultadoTemplate r, (int b, int c, int t, int ci, int co) m) => KitDesenho.MaxX(r, m);
    private static BarraTemplate Estribo(ConfiguracaoArmadura cfg, string nome, double x, double y, double w, double h, double phi, string aco, double escala) =>
        KitDesenho.EstriboFechado(cfg, nome, x, y, w, h, phi, aco, escala);
    private static void Seta(Ctx c, (double X, double Y) de, (double X, double Y) para, double ht, string layer) =>
        new DesenhoTemplate(c.R, c.Idioma, c.escV, c.hV).Seta(de, para, ht, layer);

    // ======================== vistas ========================

    /// <summary>
    /// Consolo visto de frente: retângulo b × h (e a aresta da ponta, se trapezoidal), pinos e almofada. <paramref name="um"/> =
    /// centro da largura na vista; <paramref name="zTop"/> = topo do consolo.
    /// </summary>
    internal static void VistaDeFrente(Ctx c, ConsoloPilar co, double um, double zTop)
    {
        c.Retangulo(um - co.B / 2, zTop - co.H, um + co.B / 2, zTop);
        if (co.H1 < co.H - 1e-6) c.Linha(um - co.B / 2, zTop - co.H1, um + co.B / 2, zTop - co.H1);
        foreach (double u in PinosU(co)) c.Linha(um + u, zTop, um + u, zTop + co.PinoAcima, "ARM-PINO");
        if (co.AlmA > 0 && co.AlmE > 0) c.Retangulo(um - co.AlmB / 2, zTop, um + co.AlmB / 2, zTop + co.AlmE, "ARM-ALMOFADA");
    }

    /// <summary>
    /// Consolo visto de lado: sai da aresta <paramref name="xf"/> para o lado <paramref name="s"/> (−1 = esquerda, +1 = direita),
    /// trapézio se h1 &lt; h, com o pino e a almofada; <paramref name="cob"/> = cobrimento da face do consolo (posição da almofada).
    /// </summary>
    internal static void VistaDeLado(Ctx c, ConsoloPilar co, double xf, double s, double zTop, double cob)
    {
        var (n0, n1, nPino) = Almofada(co, cob);
        R(c).Contornos.Add(new ContornoTemplate
        {
            Vertices = { (xf, zTop), (xf + s * co.A, zTop), (xf + s * co.A, zTop - co.H1), (xf, zTop - co.H) }, Fechado = true, Layer = "ARM-FORMA",
        });
        if (co.PinoN > 0) c.Linha(xf + s * nPino, zTop, xf + s * nPino, zTop + co.PinoAcima, "ARM-PINO");
        if (co.AlmA > 0 && co.AlmE > 0) c.Retangulo(Math.Min(xf + s * n0, xf + s * n1), zTop, Math.Max(xf + s * n0, xf + s * n1), zTop + co.AlmE, "ARM-ALMOFADA");
    }

    // ======================== planta no corte de forma ========================

    /// <summary>
    /// Consolo da face <paramref name="f"/> na planta do corte de forma: contorno hachurado, almofada, pinos e as cotas. A seção
    /// é o retângulo <paramref name="b"/> × <paramref name="h"/> com o canto inferior esquerdo em (<paramref name="ox"/>,
    /// <paramref name="oy"/>); <paramref name="cob"/> = cobrimento da face.
    /// </summary>
    internal static void PlantaNoCorte(Ctx c, ConsoloPilar co, int f, double b, double h, double ox, double oy, double cob)
    {
        double hc = c.hC;
        var (x0, y0, x1, y1) = RetanguloPlanta(co, f, b, h, 0, co.A);
        var ret = new List<(double, double)> { (ox + x0, oy + y0), (ox + x1, oy + y0), (ox + x1, oy + y1), (ox + x0, oy + y1) };
        R(c).Contornos.Add(new ContornoTemplate { Vertices = ret, Fechado = true, Layer = "ARM-FORMA" });
        R(c).Hachuras.Add(new HachuraTemplate { Vertices = ret, EscalaPadrao = 0.4 * hc });
        var (n0, n1, nPino) = Almofada(co, cob);
        if (co.AlmA > 0)
        {
            var (ax0, ay0, ax1, ay1) = RetanguloPlantaLargura(co, f, b, h, n0, n1, co.AlmB);
            c.Retangulo(ox + ax0, oy + ay0, ox + ax1, oy + ay1, "ARM-ALMOFADA");
        }
        foreach (double u in PinosU(co))
        {
            var (px, py) = PontoPlanta(co, f, b, h, nPino, u);
            R(c).Circulos.Add(new CirculoTemplate { X = ox + px, Y = oy + py, Raio = co.PinoPhi / 20, Layer = "ARM-PINO" });
        }
        CotasConsoloNoCorte(c, co, f, b, h, ox, oy, nPino);
    }

    /// <summary>Retângulo do consolo na planta da seção (origem no canto inferior esquerdo da seção).</summary>
    private static (double x0, double y0, double x1, double y1) RetanguloPlanta(ConsoloPilar co, int f, double b, double h, double na, double nb)
    {
        // na..nb: faixa de distância à face (0 = face, A = ponta); a largura fica centrada (com o deslocamento)
        switch (f)
        {
            case 1: { double ym = h / 2 + co.Desl; return (b + na, ym - co.B / 2, b + nb, ym + co.B / 2); }
            case 2: { double xm = b / 2 + co.Desl; return (xm - co.B / 2, -nb, xm + co.B / 2, -na); }
            case 3: { double ym = h / 2 - co.Desl; return (-nb, ym - co.B / 2, -na, ym + co.B / 2); }
            default: { double xm = b / 2 - co.Desl; return (xm - co.B / 2, h + na, xm + co.B / 2, h + nb); }
        }
    }

    /// <summary>Ponto da planta: distância n à face f e posição u ao longo da largura (a partir do centro do consolo).</summary>
    private static (double X, double Y) PontoPlanta(ConsoloPilar co, int f, double b, double h, double n, double u) => f switch
    {
        1 => (b + n, h / 2 + co.Desl + u),
        2 => (b / 2 + co.Desl + u, -n),
        3 => (-n, h / 2 - co.Desl - u),
        _ => (b / 2 - co.Desl - u, h + n),
    };

    /// <summary>
    /// Cotas encadeadas do consolo no corte de forma, como no P56: ao longo da face, do canto do pilar até o consolo e a largura
    /// dele (e até o outro canto), com a cadeia dos pinos por dentro; no sentido do balanço, da face ao pino e do pino à ponta.
    /// </summary>
    private static void CotasConsoloNoCorte(Ctx c, ConsoloPilar co, int f, double b, double h, double ox, double oy, double nPino)
    {
        double hc = c.hC, W = f == 1 || f == 3 ? h : b;
        // s = ao longo da face (0..W, no sentido de x ou y), t = para fora da face
        (double X, double Y) P(double s, double t) => f switch
        {
            1 => (ox + b + t, oy + s),
            2 => (ox + s, oy - t),
            3 => (ox - t, oy + s),
            _ => (ox + s, oy + h + t),
        };
        double angAo = f == 1 || f == 3 ? Math.PI / 2 : 0, angPerp = angAo == 0 ? Math.PI / 2 : 0;
        // posição do consolo ao longo da face (no corte, s cresce com x ou y)
        var p0 = PontoPlanta(co, f, b, h, 0, -co.B / 2);
        var p1 = PontoPlanta(co, f, b, h, 0, co.B / 2);
        double sa = f == 1 || f == 3 ? Math.Min(p0.Y, p1.Y) : Math.Min(p0.X, p1.X), sb = sa + co.B;
        void Cadeia(IEnumerable<double> ss, double t)
        {
            var l = ss.Distinct().OrderBy(s => s).ToList();
            for (int i = 0; i + 1 < l.Count; i++)
            {
                if (l[i + 1] - l[i] < 1e-6) continue;
                // linhas de chamada saem da ponta do consolo (dentro dele) ou da face do pilar (fora)
                double T(double s) => s >= sa - 1e-6 && s <= sb + 1e-6 ? co.A : 0;
                var a = P(l[i], T(l[i])); var bb = P(l[i + 1], T(l[i + 1])); var m = P((l[i] + l[i + 1]) / 2, t);
                c.Cota(a.X, a.Y, bb.X, bb.Y, m.X, m.Y, angAo, c.escC);
            }
        }
        var pinos = PinosU(co).Select(u => (f == 3 || f == 4 ? -u : u) + (sa + sb) / 2).ToList();
        if (pinos.Count > 0) Cadeia(new[] { sa, sb }.Concat(pinos), co.A + 1.5 * hc);
        Cadeia(new[] { 0, sa, sb, W }.Where(s => s >= -1e-6 && s <= W + 1e-6), co.A + 3.5 * hc);
        // balanço: da face ao pino e do pino à ponta (junto da ponta lateral do consolo), e o total mais afastado
        double sCota = sb + 1.5 * hc;
        void Perp(double t0, double t1, double s)
        {
            var a = P(sb, t0); var bb = P(sb, t1); var m = P(s, (t0 + t1) / 2);
            c.Cota(a.X, a.Y, bb.X, bb.Y, m.X, m.Y, angPerp, c.escC);
        }
        if (co.PinoN > 0 && nPino > 1e-6 && nPino < co.A - 1e-6) { Perp(0, nPino, sCota); Perp(nPino, co.A, sCota); Perp(0, co.A, sCota + 2 * hc); }
        else Perp(0, co.A, sCota);
    }

    /// <summary>Retângulo na planta entre as distâncias n0..n1 da face e com a largura dada (centrada no consolo).</summary>
    private static (double x0, double y0, double x1, double y1) RetanguloPlantaLargura(ConsoloPilar co, int f, double b, double h, double n0, double n1, double largura)
    {
        var p0 = PontoPlanta(co, f, b, h, n0, -largura / 2);
        var p1 = PontoPlanta(co, f, b, h, n1, largura / 2);
        return (Math.Min(p0.X, p1.X), Math.Min(p0.Y, p1.Y), Math.Max(p0.X, p1.X), Math.Max(p0.Y, p1.Y));
    }

    // ======================== detalhe das armaduras do consolo ========================

    /// <summary>
    /// Quadro "ARMADURAS console(s) b x h x a" no padrão do escritório (prints em info_auxiliar/templates/consolos_armadura,
    /// pedido do usuário em 2026-11-09): o consolo duplo é desenhado com os DOIS consolos (planta e corte B-B) e a barra soldada
    /// nas duas pontas. Tirante e costura de cada consolo param por dentro das longitudinais da face oposta, o tirante descendo
    /// junto delas — também no duplo, um por consolo (decisão do dono D30, 16/11/2026, a regra do modelo 3D do Revit:
    /// <see cref="BarrasConsolo"/>; antes o duplo tinha o tirante e a costura retos passantes do P61).
    /// Linha de cima: PLANTA, CORTE B-B e as formas do tirante (com a armadura inferior espelhada no duplo) e da barra soldada;
    /// linha de baixo: CORTE AA com o estribo vertical, estribo horizontal (e a armadura inferior no simples) e o pino.
    /// As barras de cada consolo levam as repetições ("2x5 N7"), inclusive tirante e estribo horizontal.
    /// Devolve o y livre abaixo do quadro. <paramref name="co"/> representa o grupo: <paramref name="nPares"/> pares (duplos) e
    /// <paramref name="nSoz"/> faces sozinhas com o mesmo tipo; <paramref name="id"/> prefixa os nomes das barras.
    /// </summary>
    internal static double Detalhe(ContextoConsolo apoio, ConsoloPilar co, int nPares, int nSoz, string id, double x0, double yTopo)
    {
        Ctx c = apoio.Desenho;
        ConfiguracaoArmadura cfg = apoio.Cfg;
        double b = apoio.B, h = apoio.H;
        double[] cob = apoio.Cob;
        string acoLong = apoio.AcoLong, acoEst = apoio.AcoEst;
        double phiEstPilar = apoio.PhiEstriboApoio, phiLongPilar = apoio.PhiLongApoio;
        var r = c.R;
        double hc = c.hC, ht = 0.8 * hc;
        double cc = co.Faces.Select(f => cob[f - 1]).Min();
        int fDir = co.Pares.Count > 0 ? co.Pares[0].F1 : co.Faces[0];
        bool duplo = nPares > 0;
        int nConsolos = 2 * nPares + nSoz, rep = Math.Max(1, nConsolos);
        double colW = fDir == 1 || fDir == 3 ? b : h;   // pilar na direção do balanço
        double wFace = fDir == 1 || fDir == 3 ? h : b;  // pilar ao longo da face
        string D(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        string dims = $"{D(co.B)}x{D(co.H)}" + (co.H1 < co.H - 1e-6 ? $"/{D(co.H1)}" : "") + $"x{D(co.A)}";
        var marca = Marca(r);
        double yMin = yTopo;
        void Rotulo(string t, double x, double y, double alt = 0) => c.Texto(t, x, y, alt > 0 ? alt : ht, 1);
        void Indicar(string barra, IEnumerable<(double X, double Y)> refs, double x, double y) =>
            r.Faixas.Add(new FaixaTemplate { Barra = barra, Tipo = TipoFaixa.Indicacao, Referencias = refs.ToList(), Posicao = (x, y), Escala = c.escC, Patamar = true });
        void Polilinha(string layer, params (double X, double Y)[] pts)
        {
            var ct = new ContornoTemplate { Layer = layer };
            ct.Vertices.AddRange(pts);
            r.Contornos.Add(ct);
        }
        // lados com consolo: −1 = à esquerda do pilar (o simples); +1 = à direita (o outro do duplo)
        var lados = duplo ? new[] { -1, 1 } : new[] { -1 };

        // ---- título (print: "ARMADURAS consoles 20x28.5x25 - duplo (x2)" / "ARMADURAS console 40x23.5x25") ----
        double y = yTopo - 2 * hc;
        string vezes = nConsolos > 1 ? $" (x{nConsolos})" : "";
        foreach (var t in c.Linhas(duplo ? $"ARMADURAS consoles {dims} - duplo{vezes}" : $"ARMADURAS console {dims}{vezes}",
                                   duplo ? $"CORBEL REINFORCEMENT {dims} - double{vezes}" : $"CORBEL REINFORCEMENT {dims}{vezes}"))
        { c.Texto(t, x0 + 1.5 * hc, y, 1.4 * hc); y -= 2.1 * hc; }
        c.Texto($"ESC 1:{c.escC:0}", x0 + 1.5 * hc, y, 0.7 * hc);
        double yR1 = y - 4 * hc;

        var (_, _, nPino) = Almofada(co, cc);
        var pinos = PinosU(co);
        bool pinoCotado = pinos.Count > 0 && nPino > 1e-6 && nPino < co.A - 1e-6;
        double lgTir = cfg.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho90, co.TirPhi, acoLong, estribo: false);
        double pernaTir = co.TirPerna > 0 ? co.TirPerna : lgTir;
        // tirante e costura: da ponta (menos o cobrimento) até por dentro das longitudinais da face oposta, um por consolo também
        // nos opostos (D30 item 3, a mesma regra do modelo 3D do Revit: BarrasConsolo)
        int fOposta = (fDir + 1) % 4 + 1;   // 1↔3, 2↔4
        double fimPilar = BarrasConsolo.FimNoPilar(colW, cob[fOposta - 1], phiEstPilar, phiLongPilar);
        double compSoz = BarrasConsolo.Horizontal(co.A, cc, fimPilar);
        double compPar = BarrasConsolo.Passante(co.A, co.A, colW, cc, cc);   // duplo: tirante e costura de ponta a ponta
        // armadura inferior: perna na ponta, trecho de baixo entrando no pilar e o retorno em cima (duplo: gancho 3 + 5)
        double pernaInf = co.H1 - 2 * cc;
        double horizInf = Math.Min(co.A - cc + co.InfAnc, co.A + colW - 2 * cc);
        double retInf = Math.Min(co.InfRetorno > 0 ? co.InfRetorno : duplo ? 3 : pernaInf, horizInf);
        double descInf = Math.Min(co.InfDescida > 0 ? co.InfDescida : duplo ? 5 : 0, pernaInf / 2);
        // forma da armadura inferior com a perna em xv e o resto para o lado dir (+1 = direita)
        (double X, double Y)[] FormaInf(double xv, double yTopoInf, int dir)
        {
            var l = new List<(double X, double Y)>();
            if (descInf > 0) l.Add((xv + dir * retInf, yTopoInf - descInf));
            if (retInf > 0) l.Add((xv + dir * retInf, yTopoInf));
            l.Add((xv, yTopoInf));
            l.Add((xv, yTopoInf - pernaInf));
            l.Add((xv + dir * horizInf, yTopoInf - pernaInf));
            return l.ToArray();
        }

        // =================== PLANTA ===================
        double ext = Math.Max(wFace, co.B) / 2;
        double xP0 = x0 + 13 * hc + co.A, yPc = yR1 - 9 * hc - ext;
        double yCad = yPc + ext + 2 * hc, yTot = yCad + 2 * hc;
        Rotulo("PLANTA", xP0 + colW / 2, yR1 - 1.5 * hc, hc);
        c.Retangulo(xP0, yPc - wFace / 2, xP0 + colW, yPc + wFace / 2, "ARM-PILAR-SECAO");
        c.Cota(xP0, yPc + wFace / 2, xP0 + colW, yPc + wFace / 2, xP0 + colW / 2, yTot, 0, c.escC);
        double yAb = yPc - ext - 0.8 * hc;
        void MarcaB(double xb, int s)
        {
            c.Linha(xb, yPc, xb, yPc - 1.2 * hc, "ARM-TEXTO-FORMA");
            c.Linha(xb, yPc, xb - s * 1.2 * hc, yPc, "ARM-TEXTO-FORMA");
            Rotulo("B", xb, yPc + 0.5 * hc, hc);
        }
        foreach (int s in lados)
        {
            double xf = s < 0 ? xP0 : xP0 + colW, xt = xf + s * co.A, xpn = xf + s * nPino;
            c.Retangulo(Math.Min(xf, xt), yPc - co.B / 2, Math.Max(xf, xt), yPc + co.B / 2);
            var refs = new List<(double X, double Y)>();
            foreach (double u in pinos)
            {
                r.Circulos.Add(new CirculoTemplate { X = xpn, Y = yPc + u, Raio = co.PinoPhi / 20, Layer = "ARM-PINO" });
                refs.Add((xpn, yPc + u));
            }
            // cotas de cima: ponta → pino → face (junto) e o balanço (afastado)
            if (pinoCotado)
            {
                c.Cota(xt, yPc + co.B / 2, xpn, yPc + co.B / 2, (xt + xpn) / 2, yCad, 0, c.escC);
                c.Cota(xpn, yPc + co.B / 2, xf, yPc + co.B / 2, (xpn + xf) / 2, yCad, 0, c.escC);
            }
            c.Cota(Math.Min(xt, xf), yPc + co.B / 2, Math.Max(xt, xf), yPc + co.B / 2, (xt + xf) / 2, yTot, 0, c.escC);
            // cotas do lado de fora: cadeia dos pinos e a largura
            var ys = new[] { -co.B / 2, co.B / 2 }.Concat(pinos).Distinct().OrderBy(u => u).ToList();
            double xCad = xt + s * 2 * hc, xLarg = ys.Count > 2 ? xt + s * 4 * hc : xCad;
            if (ys.Count > 2)
                for (int i = 0; i + 1 < ys.Count; i++)
                    c.Cota(xt, yPc + ys[i], xt, yPc + ys[i + 1], xCad, yPc + (ys[i] + ys[i + 1]) / 2, Math.PI / 2, c.escC);
            c.Cota(xt, yPc - co.B / 2, xt, yPc + co.B / 2, xLarg, yPc, Math.PI / 2, c.escC);
            if (refs.Count > 0) Indicar(id + "pino", refs, xt + s * 3.5 * hc, yPc + co.B / 2 + 1.8 * hc);
            // marcas do corte AA atravessando o consolo (em cima e embaixo, voltadas para fora) e do B-B do lado de fora
            double xA = xf + s * co.A / 2;
            foreach (var (ya, v) in new[] { (yTot + 1.5 * hc, 1.0), (yAb, -1.0) })
            {
                c.Linha(xA, ya, xA, ya + v * 1.5 * hc, "ARM-TEXTO-FORMA");
                c.Linha(xA, ya + v * 1.5 * hc, xA + s * 1.2 * hc, ya + v * 1.5 * hc, "ARM-TEXTO-FORMA");
                Rotulo("A", xA + s * 2.1 * hc, ya + v * 1.5 * hc - 0.4 * hc, hc);
            }
            MarcaB(xLarg + s * 3.5 * hc, s);
        }
        if (!duplo) MarcaB(xP0 + colW + 2 * hc, 1);
        yMin = Math.Min(yMin, yAb - 3.5 * hc);
        double xPlantaFim = xP0 + colW + (duplo ? co.A + 9 * hc : 3 * hc);

        // =================== CORTE B-B ===================
        double xB = xPlantaFim + 16 * hc + co.A;                   // face esquerda do pilar
        double zt = yR1 - 6 * hc - Math.Max(co.PinoAcima, 2 * hc); // topo do consolo
        double tirZ = zt - cc - co.TirPhi / 20;
        // ponta do tirante e da costura de cada lado (cobrimento) e o fim delas dentro do pilar (por dentro das longitudinais)
        double XPonta(int s) => (s < 0 ? xB : xB + colW) + s * (co.A - cc);
        double XFim(int s) => (s < 0 ? xB : xB + colW) - s * fimPilar;
        double xIni = XPonta(-1);
        bool pernaNoCorte = !duplo && !co.Laco;
        Rotulo("CORTE B-B", xB + colW / 2, yR1 - 1.5 * hc, hc);
        double yPilTopo = zt + Math.Max(co.PinoAcima, 2 * hc) + 2 * hc;
        double yPilBase = Math.Min(zt - co.H - 8 * hc, pernaNoCorte ? tirZ - pernaTir - 3 * hc : double.MaxValue);
        c.Linha(xB, yPilTopo, xB, zt); c.Linha(xB, zt - co.H, xB, yPilBase);
        if (duplo) { c.Linha(xB + colW, yPilTopo, xB + colW, zt); c.Linha(xB + colW, zt - co.H, xB + colW, yPilBase); }
        else c.Linha(xB + colW, yPilTopo, xB + colW, yPilBase);
        foreach (double yz in new[] { yPilTopo, yPilBase }) Interrupcao(c, xB - 1 * hc, xB + colW + 1 * hc, yz, hc);
        double d = co.H - cc - co.TirPhi / 20;
        double passoCost = co.CostN > 0 ? 2.0 / 3 * d / co.CostN : 0;
        foreach (int s in lados)
        {
            double xf = s < 0 ? xB : xB + colW, xt = xf + s * co.A;
            r.Contornos.Add(new ContornoTemplate { Vertices = { (xf, zt), (xt, zt), (xt, zt - co.H1), (xf, zt - co.H) }, Layer = "ARM-FORMA" });
            double xRot = xt + s * 3 * hc;  // coluna das indicações do lado de fora do consolo
            // barra soldada na ponta do tirante e "Solda"
            if (!co.Laco)
            {
                double xe = XPonta(s) - s * co.TirPhi / 20;
                r.Circulos.Add(new CirculoTemplate { X = xe, Y = tirZ - co.TirPhi / 10, Raio = co.TirPhi / 20, Layer = "ARM-BARRA-DETALHE" });
                Rotulo("Solda", xRot, zt + 0.9 * hc);
                Seta(c, (xt + s * 1.2 * hc, zt + 1.1 * hc), (xe, tirZ), ht, "ARM-DETALHE");
            }
            // estribos verticais: todas as linhas de chamada no mesmo texto
            if (co.VertN > 0)
            {
                var refs = new List<(double X, double Y)>();
                for (int k = 0; k < co.VertN; k++)
                {
                    double n = cc + (co.VertN == 1 ? (co.A - 2 * cc) / 2 : k * (co.A - 2 * cc) / (co.VertN - 1));
                    double hz = co.H - (co.H - co.H1) * n / co.A;
                    c.Linha(xf + s * n, zt - cc, xf + s * n, zt - hz + cc, "ARM-BARRA-DETALHE");
                    refs.Add((xf + s * n, zt - cc - 0.5));
                }
                Indicar(id + "vert", refs, xRot, zt + 2.9 * hc);
            }
            // armadura inferior: perna na ponta, retorno em cima e o trecho de baixo entrando no pilar
            if (co.InfN > 0)
            {
                double xv = xt - s * cc, zb = zt - co.H1 + cc;
                Polilinha("ARM-BARRA-DETALHE", FormaInf(xv, zb + pernaInf, -s));
                Indicar(id + (s > 0 ? "inf2" : "inf"), new[] { (xv - s * horizInf * 0.35, zb) }, xt + s * 1.5 * hc, zt - co.H - 2.5 * hc);
            }
            // pino (azul) acima do consolo, embutido, com o pé para o pilar
            if (co.PinoN > 0)
            {
                double xp = xf + s * nPino;
                if (co.PinoPe > 0) Polilinha("ARM-BARRA-2", (xp, zt + co.PinoAcima), (xp, zt - co.PinoEmb), (xp - s * co.PinoPe, zt - co.PinoEmb));
                else c.Linha(xp, zt + co.PinoAcima, xp, zt - co.PinoEmb, "ARM-BARRA-2");
                Indicar(id + "pino", new[] { (xp, zt + co.PinoAcima * 0.75) }, xRot, zt + Math.Max(4.9 * hc, co.PinoAcima * 0.9));
            }
        }
        // tirante: no duplo, reto de ponta a ponta (passante); no simples, da ponta até por dentro das longitudinais da outra
        // face, descendo junto delas (D30)
        if (duplo) c.Linha(XPonta(-1), tirZ, XPonta(1), tirZ, "ARM-BARRA-DETALHE");
        else if (pernaNoCorte) Polilinha("ARM-BARRA-DETALHE", (XPonta(-1), tirZ), (XFim(-1), tirZ), (XFim(-1), tirZ - pernaTir));
        else c.Linha(XPonta(-1), tirZ, XFim(-1), tirZ, "ARM-BARRA-DETALHE");
        string idTir = co.Laco ? id + "laco" : duplo ? id + "tir" : id + "tirs";
        if (duplo) Indicar(idTir, new[] { (xB + colW / 2 + 1 * hc, tirZ) }, xB + colW / 2 - 1.5 * hc, zt + 2.5 * hc);
        else Indicar(idTir, new[] { (xB + colW * 0.6, tirZ) }, xB + colW + 4 * hc, zt + 2.5 * hc);
        // estribos horizontais (costura): passante no duplo; no simples até por dentro das longitudinais da outra face
        if (co.CostN > 0)
        {
            var refs = new List<(double X, double Y)>();
            for (int k = 1; k <= co.CostN; k++)
            {
                double z = tirZ - k * passoCost;
                c.Linha(XPonta(-1), z, duplo ? XPonta(1) : XFim(-1), z, "ARM-BARRA-DETALHE");
                refs.Add(duplo ? (xB + colW / 2 - 1.5 * hc + 0.6 * k * hc, z) : (XFim(-1) - 1.5 * hc - 0.6 * k * hc, z));
            }
            if (duplo) Indicar(id + "cost", refs, xB + colW / 2 + 2 * hc, zt - co.H - 3 * hc);
            else Indicar(id + "cost", refs, xB + colW + 4 * hc, zt - co.H / 2 - 1 * hc);
        }
        // cotas do lado esquerdo: pino acima do consolo e a altura na ponta; no simples, o balanço embaixo
        double xTipE = xB - co.A, xCotaE = xTipE - 7.5 * hc;
        if (co.PinoN > 0 && co.PinoAcima > 0) c.Cota(xTipE, zt, xTipE, zt + co.PinoAcima, xCotaE, zt + co.PinoAcima / 2, Math.PI / 2, c.escC);
        c.Cota(xTipE, zt - co.H1, xTipE, zt, xCotaE, zt - co.H1 / 2, Math.PI / 2, c.escC);
        if (!duplo) c.Cota(xTipE, zt - co.H, xB, zt - co.H, xTipE + co.A / 2, zt - co.H - 5.5 * hc, 0, c.escC);
        yMin = Math.Min(yMin, yPilBase - 2 * hc);
        double xCorteFim = xB + colW + (duplo ? co.A + 6 * hc : 8 * hc);

        // =================== formas: tirante (e a armadura inferior do duplo) e a barra soldada ===================
        double xS = xCorteFim + 8 * hc, yS = yR1 - 5 * hc;
        double xFim = xS, ySFim = yS;
        if (!co.Laco)
        {
            // duplo (par em nível, D30): reto de ponta a ponta, um conjunto por par; faces sozinhas (e o simples): em L
            double comp = duplo ? compPar : compSoz;
            Rotulo("Tirante", xS + comp / 2, yS + 5.2 * hc);
            if (duplo)
            {
                r.Barras.Add(new BarraTemplate { Nome = id + "tir", Vertices = { (xS, yS), (xS + comp, yS) }, Bitola = co.TirPhi, Aco = acoLong,
                    Quantidade = co.TirN, Repeticoes = Math.Max(1, nPares), Escala = c.escC });
                // a barra soldada nas duas pontas
                Indicar(id + "tr", new[] { (xS, yS) }, xS - 3 * hc, yS - 1.2 * hc);
                Indicar(id + "tr", new[] { (xS + comp, yS) }, xS + comp + 3 * hc, yS - 1.2 * hc);
                ySFim = yS;
            }
            if (!duplo || nSoz > 0)
            {
                double y0 = duplo ? yS - 5 * hc : yS;
                var bt = new BarraTemplate { Nome = id + "tirs", Vertices = { (xS, y0), (xS + compSoz, y0), (xS + compSoz, y0 - pernaTir) },
                    Bitola = co.TirPhi, Aco = acoLong, Quantidade = co.TirN, Repeticoes = duplo ? nSoz : rep, Escala = c.escC };
                if (co.TirPerna <= 0) bt.Formatos.AddRange(new[] { FormatoSegmento.Linha, FormatoSegmento.Gancho90 });
                r.Barras.Add(bt);
                if (!duplo) Indicar(id + "tr", new[] { (xS, y0) }, xS - 4 * hc, y0);
                ySFim = y0 - pernaTir;
            }
            xFim = Math.Max(xFim, xS + comp + 5 * hc);
        }
        else
        {
            double larg = Math.Max(4 * co.TirPhi / 10, (co.B - 2 * cc - co.TirPhi / 10) / Math.Max(1, co.TirN - 1));
            var bt = new BarraTemplate { Nome = id + "laco", Vertices = { (xS, yS), (xS + compSoz, yS), (xS + compSoz, yS + larg), (xS, yS + larg) },
                Bitola = co.TirPhi, Aco = acoLong, Quantidade = (co.TirN + 1) / 2, Repeticoes = rep, Escala = c.escC };
            bt.Bulges.AddRange(new[] { 0.0, 1.0, 0.0, 0.0 });
            r.Barras.Add(bt);
            Rotulo("Tirante (laço)", xS + compSoz / 2, yS + larg + 3.6 * hc);
            xFim = Math.Max(xFim, xS + compSoz + 3 * hc);
        }
        double yForma = ySFim - 6 * hc;
        // duplo: as duas armaduras inferiores espelhadas embaixo do tirante (como no print)
        if (duplo && co.InfN > 0)
        {
            double yTi = yForma - Math.Max(1.5 * hc, 0);
            double xEsq = xS, xDir = xS + Math.Max(compPar, 2 * horizInf + 6 * hc);
            var esq = new BarraTemplate { Nome = id + "inf", Bitola = co.InfPhi, Aco = acoLong, Quantidade = co.InfN, Repeticoes = Math.Max(1, nPares), Escala = c.escC };
            esq.Vertices.AddRange(FormaInf(xEsq, yTi, 1));
            var dir = new BarraTemplate { Nome = id + "inf2", Bitola = co.InfPhi, Aco = acoLong, Quantidade = co.InfN, Repeticoes = Math.Max(1, nPares), Escala = c.escC };
            dir.Vertices.AddRange(FormaInf(xDir, yTi, -1));
            r.Barras.Add(esq); r.Barras.Add(dir);
            Rotulo("Arm. Inferior", xEsq + horizInf / 2, yTi - pernaInf - 4.5 * hc);
            Rotulo("Arm. Inferior", xDir - horizInf / 2, yTi - pernaInf - 4.5 * hc);
            xFim = Math.Max(xFim, xDir + 3 * hc);
            yForma = yTi - pernaInf - 7 * hc;
        }
        if (!co.Laco)
        {
            // barra transversal soldada na ponta (uma por consolo), inclinada como na prancha
            double compTr = co.B - 2 * cc, q = compTr * Math.Sqrt(0.5);
            double xt = xS + 4 * hc, yt = yForma - q - 4 * hc;
            r.Barras.Add(new BarraTemplate
            {
                Nome = id + "tr", Vertices = { (xt, yt), (xt + q, yt + q) }, Bitola = co.TirPhi, Aco = acoLong, Quantidade = 1, Repeticoes = rep, Escala = c.escC,
                PosicaoDescricao = (xt + q / 2 - 2.6 * hc, yt + q / 2 + 2.6 * hc), RotacaoDescricao = Math.PI / 4,
            });
            foreach (var (t, k) in c.Linhas("Barra de ancoragem soldada", "Welded anchor bar").Select((t, k) => (t, k))
                         .Concat(new[] { (c.Idioma == IdiomaDesenho.EN ? "SEE SPECIFIC DETAIL" : "VER DET. ESPECÍFICO", 9) }))
            {
                double off = 2.2 * hc + (k == 9 ? c.Linhas("a", "b").Length : k) * 1.7 * ht;
                c.R.Textos.Add(new TextoTemplate { Texto = t, X = xt + q / 2 + off * Math.Sqrt(0.5), Y = yt + q / 2 - off * Math.Sqrt(0.5),
                    Altura = ht, Alinhamento = 1, Angulo = Math.PI / 4, Layer = "ARM-TEXTO-FORMA" });
            }
            yMin = Math.Min(yMin, yt - 5 * hc);
            xFim = Math.Max(xFim, xt + q + 10 * hc);
        }
        yMin = Math.Min(yMin, yForma);

        // =================== linha de baixo: CORTE AA + estribo vertical, estribo horizontal, pino ===================
        double yR2 = yMin - 3 * hc;
        double xAA = x0 + 7 * hc, topAA = yR2 - 3 * hc;
        Rotulo(c.Idioma == IdiomaDesenho.EN ? "SECTION AA" : "CORTE AA", xAA + co.B / 2, yR2 + 1.5 * hc, hc);
        c.Retangulo(xAA, topAA - co.H, xAA + co.B, topAA);
        if (co.VertN > 0) c.Retangulo(xAA + cc, topAA - co.H + cc, xAA + co.B - cc, topAA - cc, "ARM-BARRA-DETALHE");
        var indicAA = new List<(string barra, List<(double X, double Y)> pts, bool baixo)>();
        if (!co.Laco)
        {
            var pts = new List<(double X, double Y)>();
            for (int k = 0; k < co.TirN; k++)
            {
                double xk = xAA + cc + co.TirPhi / 20 + (co.TirN == 1 ? (co.B - 2 * cc - co.TirPhi / 10) / 2 : k * (co.B - 2 * cc - co.TirPhi / 10) / (co.TirN - 1));
                r.Circulos.Add(new CirculoTemplate { X = xk, Y = topAA - cc - co.TirPhi / 20, Raio = co.TirPhi / 20, Layer = "ARM-BARRA-DETALHE" });
                pts.Add((xk, topAA - cc - co.TirPhi / 20));
            }
            indicAA.Add((idTir, pts, false));
        }
        if (co.CostN > 0)
        {
            var pts = new List<(double X, double Y)>();
            for (int k = 1; k <= co.CostN; k++)
            {
                double z = topAA - cc - co.TirPhi / 20 - k * passoCost;
                c.Linha(xAA + cc, z, xAA + co.B - cc, z, "ARM-BARRA-DETALHE");
                pts.Add((xAA + co.B - cc - 0.3 * k * hc, z));
            }
            indicAA.Add((id + "cost", pts, false));
        }
        if (co.InfN > 0)
        {
            var pts = new List<(double X, double Y)>();
            for (int k = 0; k < co.InfN; k++)
            {
                double xk = xAA + cc + co.InfPhi / 20 + (co.InfN == 1 ? (co.B - 2 * cc - co.InfPhi / 10) / 2 : k * (co.B - 2 * cc - co.InfPhi / 10) / (co.InfN - 1));
                double yk = topAA - co.H + cc + co.InfPhi / 20;
                r.Circulos.Add(new CirculoTemplate { X = xk, Y = yk, Raio = co.InfPhi / 20, Layer = "ARM-BARRA-DETALHE" });
                pts.Add((xk, yk));
            }
            indicAA.Add((id + "inf", pts, true));
        }
        // tirante em cima à esquerda, costura à direita, armadura inferior embaixo à esquerda (como no print)
        foreach (var it in indicAA)
        {
            (double X, double Y) pos = it.baixo ? (xAA - 3.5 * hc, topAA - co.H - 1.5 * hc)
                : it.barra == id + "cost" ? (xAA + co.B + 4 * hc, topAA - co.H / 2) : (xAA - 3.5 * hc, topAA + 2.5 * hc);
            Indicar(it.barra, it.pts, pos.X, pos.Y);
        }
        c.Cota(xAA, topAA, xAA + co.B, topAA, xAA + co.B / 2, topAA + 2 * hc, 0, c.escC);
        c.Cota(xAA, topAA - co.H, xAA, topAA, xAA - 2.5 * hc, topAA - co.H / 2, Math.PI / 2, c.escC);
        double yBaixo = topAA - co.H - 4 * hc, xCur = xAA + co.B + 9 * hc;

        if (co.VertN > 0)
        {
            double wv = co.B - 2 * cc, hvert = co.H - 2 * cc;
            var bv = Estribo(cfg, id + "vert", xCur, topAA - hvert, wv, hvert, co.VertPhi, acoEst, c.escC);
            bv.Quantidade = co.VertN;
            bv.Repeticoes = rep;
            r.Barras.Add(bv);
            Rotulo("Estribo vertical", xCur + wv / 2, topAA - hvert - 4.5 * hc);
            xCur += wv + 12 * hc;
            yBaixo = Math.Min(yBaixo, topAA - hvert - 6 * hc);
        }
        // estribo horizontal embaixo do corte B-B (no simples, a armadura inferior embaixo dele)
        xCur = Math.Max(xCur, xB - co.A);
        double xMeioFim = xCur;
        if (co.CostN > 0)
        {
            // comprimento: passante no duplo; no simples até por dentro das longitudinais da outra face (D30); largura por
            // dentro dos estribos verticais
            double w = duplo ? compPar : compSoz, prof = BarrasConsolo.LarguraCostura(co.B, cc, co.VertPhi, co.CostPhi);
            var bc = Estribo(cfg, id + "cost", xCur, topAA - prof, w, prof, co.CostPhi, acoEst, c.escC);
            bc.Quantidade = co.CostN;
            bc.Repeticoes = duplo ? Math.Max(1, nPares) : rep;   // duplo: uma em volta dos dois consolos de cada par
            if (duplo && nSoz > 0)
            {
                // faces sozinhas num nível com par: costura própria, até por dentro das longitudinais
                var bs = Estribo(cfg, id + "cost2", xCur, topAA - 2 * prof - 6 * hc, compSoz, prof, co.CostPhi, acoEst, c.escC);
                bs.Quantidade = co.CostN;
                bs.Repeticoes = nSoz;
                r.Barras.Add(bs);
                yBaixo = Math.Min(yBaixo, topAA - 2 * prof - 12 * hc);
            }
            r.Barras.Add(bc);
            Rotulo("Estribo horizontal", xCur + w / 2, topAA - prof - 4.5 * hc);
            xMeioFim = Math.Max(xMeioFim, xCur + w + 10 * hc);
            yBaixo = Math.Min(yBaixo, topAA - prof - 6 * hc);
        }
        if (!duplo && co.InfN > 0)
        {
            double yi = yBaixo - 2 * hc;
            var bi = new BarraTemplate { Nome = id + "inf", Bitola = co.InfPhi, Aco = acoLong, Quantidade = co.InfN, Repeticoes = rep, Escala = c.escC };
            bi.Vertices.AddRange(FormaInf(xCur, yi, 1));
            r.Barras.Add(bi);
            Rotulo("Arm. Inferior", xCur + horizInf / 2, yi - pernaInf - 4.5 * hc);
            xMeioFim = Math.Max(xMeioFim, xCur + horizInf + 10 * hc);
            yBaixo = Math.Min(yBaixo, yi - pernaInf - 6 * hc);
        }
        xCur = Math.Max(xMeioFim, xS);
        if (co.PinoN > 0)
        {
            double xp = xCur + 2 * hc, yp = topAA;
            var bt = new BarraTemplate { Nome = id + "pino", Vertices = { (xp, yp), (xp, yp - co.PinoAcima - co.PinoEmb), (xp + co.PinoPe, yp - co.PinoAcima - co.PinoEmb) },
                Bitola = co.PinoPhi, Aco = acoLong, Quantidade = co.PinoN, Repeticoes = rep, Escala = c.escC };
            if (co.PinoPe <= 0) bt.Vertices.RemoveAt(2);
            r.Barras.Add(bt);
            // topo do consolo atravessando o pino (como na prancha)
            c.Linha(xp - 5 * hc, yp - co.PinoAcima, xp + Math.Max(co.PinoPe, 5) + 5 * hc, yp - co.PinoAcima, "ARM-FORMA");
            Rotulo(co.PinoN * nConsolos > 1 ? "PINOS" : "PINO", xp + Math.Max(co.PinoPe, 5) / 2, yp - co.PinoAcima - co.PinoEmb - 4.5 * hc);
            xCur = xp + Math.Max(co.PinoPe, 5) + 10 * hc;
            yBaixo = Math.Min(yBaixo, yp - co.PinoAcima - co.PinoEmb - 6 * hc);
        }

        // ---- moldura ----
        double xMax = Math.Max(Math.Max(xCur, xFim), MaxX(r, marca)) + 2 * hc;
        c.Retangulo(x0, yBaixo, xMax, yTopo, "ARM-DETALHE");
        VerificarConsolo(r, co, cc);
        return yBaixo - 8 * hc;
    }

    /// <summary>Linha de interrupção (zigue-zague no meio) atravessando o pilar no corte do consolo.</summary>
    private static void Interrupcao(Ctx c, double xa, double xb, double y, double hc)
    {
        double xm = (xa + xb) / 2, a = 0.6 * hc;
        c.R.Contornos.Add(new ContornoTemplate
        {
            Vertices = { (xa, y), (xm - a, y), (xm - a / 2, y + a), (xm + a / 2, y - a), (xm + a, y), (xb, y) }, Layer = "ARM-FORMA",
        });
    }

    /// <summary>Regras de detalhamento de consolos da NBR 9062 (7.3), como avisos.</summary>
    private static void VerificarConsolo(ResultadoTemplate r, ConsoloPilar co, double cob)
    {
        string nome = $"Consolo {co.Slot}";
        double d = co.H - cob - co.TirPhi / 20;
        var (n0, n1, nCarga) = Almofada(co, cob);
        double ad = nCarga / d;
        if (ad > 1) r.Avisos.Add($"{nome}: a/d = {Num(ad, "0.00")} > 1 — dimensionar como viga em balanço (NBR 9062, 7.3.2.2 a).");
        if (co.H1 < co.H / 2 - 1e-6) r.Avisos.Add($"{nome}: altura na ponta {Num(co.H1, "0.#")} < h/2 (NBR 9062, 7.3.3.1).");
        double fator = co.Laco ? 8 : 6;
        double menor = Math.Min(co.B, co.H);
        if (co.TirPhi > Math.Min(25, menor * 10 / fator))
            r.Avisos.Add($"{nome}: tirante ø{Num(co.TirPhi, "0.0")} > 1/{fator:0} da menor dimensão do consolo ou 25 mm (NBR 9062, 7.3.3.{(co.Laco ? 6 : 7)}).");
        if (co.TirN > 1)
        {
            double esp = (co.B - 2 * cob - co.TirPhi / 10) / (co.TirN - 1);
            double lim = Math.Min((co.Laco ? 15 : 20) * co.TirPhi / 10, d);
            if (esp > lim + 1e-6) r.Avisos.Add($"{nome}: espaçamento do tirante {Num(esp, "0.#")} cm > {Num(lim, "0.#")} cm (NBR 9062, 7.3.3.{(co.Laco ? 6 : 7)}).");
        }
        if (co.CostN > 0)
        {
            if (co.CostPhi > menor * 10 / 15) r.Avisos.Add($"{nome}: costura ø{Num(co.CostPhi, "0.0")} > 1/15 da menor dimensão (NBR 9062, 7.3.3.12).");
            double passo = 2.0 / 3 * d / co.CostN;
            if (passo > Math.Min(10, co.A) + 1e-6) r.Avisos.Add($"{nome}: costura a cada {Num(passo, "0.#")} cm > 10 cm ou a (NBR 9062, 7.3.3.12).");
        }
        else r.Avisos.Add($"{nome}: sem armadura de costura (obrigatória, NBR 9062, 7.3.6).");
        if (co.AlmA > 0)
        {
            if (n0 <= 1e-6 && n1 - n0 < co.AlmA - 1e-6)
                r.Avisos.Add($"{nome}: almofada de {Num(co.AlmA, "0.#")} cm não cabe com a2 = {Num(A2Minimo(cob, co.TirPhi, co.Laco), "0.#")} cm (NBR 9062, 7.3.3.4).");
            if (co.AlmB > co.B - 2 * cob + 1e-6) r.Avisos.Add($"{nome}: almofada mais larga que o consolo menos os cobrimentos.");
        }
    }
}
