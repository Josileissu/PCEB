using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Documentacao;
using DEB.Core.Elementos.Viga.PreMoldada.Calculo;
using DEB.Core.Normas;
using DEB.Core.Normas.Verificacao;

namespace DEB.Core.Elementos.Escada;

/// <summary>
/// Rebaixo lateral de uma peça para o encaixe invertido (D43): a metade de cima da espessura é retirada junto à borda, no trecho
/// em que a outra peça assenta; sobra embaixo um dente invertido (meia espessura) que recebe a ponta (com dente de meia espessura) da
/// peça apoiada. Valores em cm; cargas características por metro ao longo da borda (kN/m).
/// </summary>
public sealed class RebaixoLateral
{
    public string Nome { get; set; } = "";
    /// <summary>Trecho do rebaixo ao longo do x da peça (cm), já com a folga dos dois lados.</summary>
    public double X0 { get; set; }
    public double X1 { get; set; }
    /// <summary>Borda: +1 = esquerda (y positivo, olhando o sentido do x), −1 = direita.</summary>
    public int Lado { get; set; } = 1;
    /// <summary>Profundidade a partir da borda (comprimento de apoio + folga) e altura retirada do topo (meia espessura).</summary>
    public double Profundidade { get; set; } = 16.5;
    public double Altura { get; set; } = 8;
    public double Folga { get; set; } = 1.5;
    /// <summary>Reação característica da peça apoiada por metro de borda (kN/m) e o k de Hd = k·Fd do material do apoio.</summary>
    public double G { get; set; }
    public double Q { get; set; }
    public double CoefHd { get; set; } = 0.16;
    public RebaixoLateral Copia() => (RebaixoLateral)MemberwiseClone();
}

/// <summary>Resultado do dente invertido de um rebaixo (por metro de borda).</summary>
public sealed class RebaixoCalculado
{
    public RebaixoLateral Rebaixo { get; set; }
    public double Fd { get; set; }
    public double Hd { get; set; }
    public double HDente { get; set; }
    public double DDente { get; set; }
    public double A { get; set; }
    public double AsTirante { get; set; }
    public double AsSuspensao { get; set; }
    public double VRd2 { get; set; }
    public (double Bitola, double Esp) Tirante { get; set; }
    public (double Bitola, double Esp) Suspensao { get; set; }
}

public static partial class CalculoEscada
{
    /// <summary>Topo da peça (cota local) no x: patamar de baixo 0, de cima o desnível; null no lance inclinado.</summary>
    private static double? TopoPlano(EscadaLaje e, GeometriaEscada g, double x0, double x1)
    {
        if (e.SoPatamar) return 0;
        if (x1 <= g.X1 + 1e-6) return 0;
        if (x0 >= g.X2 - 1e-6) return e.Desnivel;
        return null;
    }

    /// <summary>Rebaixos laterais (encaixe invertido): verificação do dente invertido (NBR 9062, 7.4), grampos do tirante e da
    /// suspensão e as barras de cima afastadas do vazio.</summary>
    private static void CalcularRebaixos(ResultadoEscada r, CoeficientesViga k)
    {
        var e = r.Entrada; var g = r.Geo;
        double cob = e.Cobrimento, hp = g.Hp, B = e.Largura;
        double fcd = e.Fck / k.GamaC / 10, fyd = TabelasNbr.Fyk(e.Aco) / k.GamaS / 10, fywd = Math.Min(TabelasNbr.Fyk(e.Aco) / k.GamaS, 435) / 10;
        var rt = new RegrasTraspasse { Fck = e.Fck, GamaC = k.GamaC, GamaS = k.GamaS };
        var m = new PassoMemoria { Titulo = "11. Rebaixos do encaixe invertido (dente invertido, NBR 9062, 7.4)" };
        int nb = r.Barras.Count == 0 ? 1 : r.Barras.Select(b => int.TryParse(b.Nome.TrimStart('N'), out int i) ? i : 0).Max() + 1;
        foreach (var rb in e.Rebaixos)
        {
            string grupo = $"Rebaixo para {rb.Nome}";
            double? zt = TopoPlano(e, g, rb.X0, rb.X1);
            if (zt == null) { r.Erros.Add($"{grupo}: o trecho x = {rb.X0:0} a {rb.X1:0} cm pega o lance inclinado — o rebaixo tem de ficar num patamar."); continue; }
            if (rb.X0 < g.XPontaInf - 1e-6 || rb.X1 > g.XPontaSup + 1e-6) r.Avisos.Add($"{grupo}: o rebaixo passa da ponta da peça.");
            if ((rb.X0 < g.XRecorteInf && g.XRecorteInf > g.XPontaInf + 1e-6) || (rb.X1 > g.XRecorteSup && g.XRecorteSup < g.XPontaSup - 1e-6))
                r.Erros.Add($"{grupo}: o rebaixo encontra o dente da ponta da peça — afaste a peça apoiada da ponta.");
            var c = new RebaixoCalculado { Rebaixo = rb };
            c.Fd = k.GamaG * rb.G + k.GamaQ * rb.Q;
            c.Hd = rb.CoefHd * c.Fd;
            c.HDente = hp - rb.Altura;
            double phiT = 8;
            c.DDente = c.HDente - cob - phiT / 20;
            c.A = (rb.Profundidade + rb.Folga) / 2;   // do centro do apoio (meio do assento) à face do rebaixo
            double ad = c.DDente > 0 ? c.A / c.DDente : 99;
            c.AsTirante = ad <= 0.5 ? 0.8 * c.Fd / (fyd * 1.4) + c.Hd / fyd : (0.1 + ad) * c.Fd / fyd + c.Hd / fyd;
            c.AsSuspensao = c.Fd / fywd;
            c.Tirante = Escolher(new[] { 6.3, 8, 10 }, c.AsTirante, 20);
            c.Suspensao = Escolher(new[] { 6.3, 8, 10 }, c.AsSuspensao / 2, 20);
            c.VRd2 = 0.27 * (1 - e.Fck / 250) * fcd * 100 * c.DDente;
            r.Rebaixos.Add(c);
            double asT = c.Tirante.Esp > 0 ? A(c.Tirante.Bitola) * 100 / c.Tirante.Esp : 0, asS = c.Suspensao.Esp > 0 ? 2 * A(c.Suspensao.Bitola) * 100 / c.Suspensao.Esp : 0;
            m.Linhas.Add($"{grupo}: x = {N(rb.X0, "0")} a {N(rb.X1, "0")} cm, borda {(rb.Lado > 0 ? "esquerda" : "direita")}; rebaixo de {N(rb.Altura, "0.#")} cm (meia espessura) × {N(rb.Profundidade, "0.#")} cm, folga {N(rb.Folga, "0.#")} cm; " +
                          $"dente invertido de {N(c.HDente, "0.#")} cm, d = {N(c.DDente, "0.0")} cm, a = {N(c.A, "0.0")} cm, a/d = {N(ad)}.");
            m.Linhas.Add($"  Fd = {N(k.GamaG)}·{N(rb.G, "0.00")} + {N(k.GamaQ)}·{N(rb.Q, "0.00")} = {N(c.Fd, "0.0")} kN/m de borda; Hd = {N(rb.CoefHd)}·Fd = {N(c.Hd, "0.0")} kN/m; " +
                          $"tirante {N(c.AsTirante)} cm²/m → grampos ø{N(c.Tirante.Bitola, "0.#")} c/{N(c.Tirante.Esp, "0.#")}; suspensão Fd/fywd = {N(c.AsSuspensao)} cm²/m → ø{N(c.Suspensao.Bitola, "0.#")} c/{N(c.Suspensao.Esp, "0.#")} (2 ramos); biela VRd2 = {N(c.VRd2, "0.0")} kN/m.");
            r.Verificacoes.Add(K("R1", grupo, "Tirante do dente invertido", $"{N(asT)} cm²/m", $"≥ {N(c.AsTirante)} cm²/m", asT < c.AsTirante - 1e-9 || ad > 1, "NBR 9062, 7.4.4",
                ad > 1 ? SituacaoVerificacao.Conferir : SituacaoVerificacao.NaoAtende, ad > 1 ? "a/d > 1: o dente invertido funciona como balanço — aumente a espessura." : "Aumente a bitola."));
            r.Verificacoes.Add(K("R2", grupo, "Suspensão junto à face do rebaixo", $"{N(asS)} cm²/m", $"≥ {N(c.AsSuspensao)} cm²/m", asS < c.AsSuspensao - 1e-9, "NBR 9062, 7.4.6"));
            r.Verificacoes.Add(K("R3", grupo, "Biela do dente invertido", $"Fd = {N(c.Fd, "0.0")} kN/m", $"≤ {N(c.VRd2, "0.0")} kN/m", c.Fd > c.VRd2, "NBR 9062, 7.4.7", dica: "Aumente a espessura da peça."));

            // ---- barras (transversais à peça, repetidas ao longo do rebaixo) ----
            double zTop = zt.Value, s = rb.Lado, yB = B / 2;
            double xa = rb.X0 + cob, xb = rb.X1 - cob;
            if (xb - xa < 5) continue;
            var linha = new List<(double X, double Z)> { (xa, zTop), (xb, zTop) };
            var t = c.Tirante;
            if (t.Esp > 0)
            {
                double ct = cob + t.Bitola / 20, lb = Math.Ceiling(rt.Lb(t.Bitola, e.Aco));
                double yE = s * (yB - ct), yI = s * (yB - rb.Profundidade - lb);
                if (Math.Abs(yI) > yB - ct) yI = -s * (yB - ct);   // peça estreita: até a outra borda
                double nTop = -rb.Altura - ct, nBot = -hp + ct;
                var perfil = new List<(double Y, double N)> { (yI, nTop), (yE, nTop), (yE, nBot), (yI, nBot) };
                if (!DobraCabe(nTop - nBot, t.Bitola))
                {
                    perfil = new List<(double Y, double N)> { (yE, nTop), (yI, nTop) };
                    r.Avisos.Add($"{grupo}: o dente invertido é baixo para o laço do tirante ø{Fmt.Virgula(t.Bitola, "0.#")} — tirante reto; ancore na borda com barra soldada (NBR 9062, 7.3.3).");
                }
                r.Barras.Add(new BarraEscada
                {
                    Nome = "N" + nb++, Funcao = $"tirante do rebaixo ({rb.Nome})", Bitola = t.Bitola, Espacamento = t.Esp, Transversal = true,
                    Quantidade = QtLinha(linha, t.Esp), Pontos = linha, PerfilY = perfil, Comprimento = Math.Ceiling(CompPerfilY(perfil)),
                });
            }
            var su = c.Suspensao;
            if (su.Esp > 0)
            {
                double cs = cob + su.Bitola / 20;
                double y1 = s * (yB - rb.Profundidade - cs), y2 = y1 - s * 12;
                var perfil = new List<(double Y, double N)> { (y1, -cs), (y1, -hp + cs), (y2, -hp + cs), (y2, -cs) };
                r.Barras.Add(new BarraEscada
                {
                    Nome = "N" + nb++, Funcao = $"suspensão do rebaixo ({rb.Nome})", Bitola = su.Bitola, Espacamento = su.Esp, Transversal = true,
                    Quantidade = QtLinha(linha, su.Esp), Pontos = linha, PerfilY = perfil, Comprimento = Math.Ceiling(CompPerfilY(perfil)),
                });
            }
            // barras longitudinais que passam pela metade de cima no trecho do rebaixo: saem daquela borda (ficam fora do vazio)
            foreach (var b in r.Barras.Where(b => !b.Transversal))
            {
                bool cruza = b.Pontos.Count > 1 && b.Pontos.Min(p => p.X) < rb.X1 && b.Pontos.Max(p => p.X) > rb.X0;
                bool emCima = b.Pontos.Where(p => p.X >= rb.X0 - 1 && p.X <= rb.X1 + 1).Any(p => p.Z > zTop - rb.Altura)
                              || Segmentos(b.Pontos).Any(sg => Math.Max(sg.a.X, sg.b.X) > rb.X0 && Math.Min(sg.a.X, sg.b.X) < rb.X1 && Math.Max(sg.a.Z, sg.b.Z) > zTop - rb.Altura);
                if (!cruza || !emCima) continue;
                double lim = yB - rb.Profundidade - cob;
                if (s > 0) b.YMax = Math.Min(b.YMax ?? (yB - cob), lim); else b.YMin = Math.Max(b.YMin ?? -(yB - cob), -lim);
                double yA = b.YMin ?? -(yB - cob), yZ = b.YMax ?? (yB - cob);
                b.Quantidade = QtReta(yZ - yA - b.Bitola / 10, b.Espacamento);
            }
        }
        if (m.Linhas.Count > 0) r.Memoria.Add(m);
    }

    private static IEnumerable<((double X, double Z) a, (double X, double Z) b)> Segmentos(IReadOnlyList<(double X, double Z)> p)
    {
        for (int i = 1; i < p.Count; i++) yield return (p[i - 1], p[i]);
    }

    private static double CompPerfilY(IReadOnlyList<(double Y, double N)> p)
    {
        double s = 0;
        for (int i = 1; i < p.Count; i++) s += Math.Sqrt(Math.Pow(p[i].Y - p[i - 1].Y, 2) + Math.Pow(p[i].N - p[i - 1].N, 2));
        return s;
    }
}
