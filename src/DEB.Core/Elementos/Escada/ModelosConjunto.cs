using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Elementos.Escada;

/// <summary>
/// Posição de uma peça do conjunto em planta (cm, graus), no sistema do conjunto: para lance e patamar, o ponto do eixo do apoio
/// inferior no meio da largura, na cota do topo da ponta de baixo, e o sentido do x da peça; para a viga do patamar, a ponta esquerda
/// no meio da largura, na cota do topo, e o sentido do comprimento.
/// </summary>
public sealed class PosicaoPeca
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Angulo { get; set; }
    public (double X, double Y) Dir => (Math.Cos(Angulo * Math.PI / 180), Math.Sin(Angulo * Math.PI / 180));
    /// <summary>Ponto em planta a <paramref name="ao"/> cm ao longo e <paramref name="lado"/> cm à esquerda (sentido anti-horário).</summary>
    public (double X, double Y) Em(double ao, double lado = 0)
    {
        var d = Dir;
        return (X + d.X * ao - d.Y * lado, Y + d.Y * ao + d.X * lado);
    }
    /// <summary>Coordenadas locais (ao longo, ao lado) de um ponto em planta.</summary>
    public (double Ao, double Lado) Local(double px, double py)
    {
        var d = Dir; double dx = px - X, dy = py - Y;
        return (dx * d.X + dy * d.Y, -dx * d.Y + dy * d.X);
    }
}

/// <summary>Arranjos prontos do conjunto (D43): o engenheiro escolhe, ajusta as peças e as posições ficam coerentes.</summary>
public static class ModelosConjunto
{
    /// <summary>Lance de partida do conjunto: meio pé-direito (144 cm em 8 espelhos), patamares de 40 e 60 cm, pré-moldado com apoio
    /// direto embaixo e dente em cima.</summary>
    public static EscadaLaje LancePadrao() => new EscadaLaje
    {
        Desnivel = 144, Espelhos = 8, Piso = 28, Largura = 120, Espessura = 16, PatamarInferior = 40, PatamarSuperior = 60,
        Execucao = ExecucaoEscada.PreMoldada,
        LigacaoInferior = new LigacaoEscada { Ponta = PontaPreMoldada.ApoioDireto, ComprimentoApoio = 15, Apoio = ApoioPreMoldado.Elastomero },
        LigacaoSuperior = new LigacaoEscada { Ponta = PontaPreMoldada.Dente, ComprimentoApoio = 15, AlturaDente = 10, ComprimentoDente = 25, Apoio = ApoioPreMoldado.Argamassa },
    };

    /// <summary>Em linha: a ponta inferior do 2º lance assenta no patamar superior do 1º (que vai até a estrutura).</summary>
    public static ConjuntoEscada EmLinha(EscadaLaje l1, EscadaLaje l2)
    {
        var c = new ConjuntoEscada { Modelo = "Em linha (patamar sobre patamar)" };
        var g1 = GeometriaEscada.De(l1);
        c.Pecas.Add(("L1", l1.Copia())); c.Pecas.Add(("L2", l2.Copia()));
        double x = g1.Vao - Math.Max(l1.PatamarSuperior, 30) + l2.LigacaoInferior.ComprimentoApoio / 2;
        x = Math.Max(g1.X2 + l2.LigacaoInferior.ComprimentoApoio / 2, Math.Min(g1.Vao - l2.LigacaoInferior.ComprimentoApoio / 2 - 5, x));
        c.Posicoes["L1"] = new PosicaoPeca();
        c.Posicoes["L2"] = new PosicaoPeca { X = x, Z = l1.Desnivel };
        c.Apoios.Add(new ApoioEntrePecas { Apoiada = "L2", Ponta = PontaPeca.Inferior, Suporte = "L1", X = x, Direcao = DirecaoApoio.Transversal });
        return c;
    }

    /// <summary>Em L: o 2º lance sai a 90° (à esquerda) do patamar superior do 1º e apoia a ponta inferior nele (borda ao longo do vão do 1º).</summary>
    public static ConjuntoEscada EmL(EscadaLaje l1, EscadaLaje l2)
    {
        var c = new ConjuntoEscada { Modelo = "Em L (patamar sobre patamar)" };
        var a = l1.Copia(); var b = l2.Copia();
        // o patamar superior do 1º precisa caber a largura do 2º
        if (a.PatamarSuperior < b.Largura + 10) { c.Avisos.Add($"L1: patamar superior aumentado para {b.Largura + 10:0} cm (cabe a largura do L2)."); a.PatamarSuperior = b.Largura + 10; }
        var g1 = GeometriaEscada.De(a);
        c.Pecas.Add(("L1", a)); c.Pecas.Add(("L2", b));
        double x = g1.Vao - 5 - b.Largura / 2;                          // eixo do L2 sobre o patamar do L1
        double cA = b.LigacaoInferior.ComprimentoApoio;
        c.Posicoes["L1"] = new PosicaoPeca();
        // o apoio do L2 fica dentro da largura do L1, junto à borda esquerda
        c.Posicoes["L2"] = new PosicaoPeca { X = x, Y = a.Largura / 2 - cA / 2, Z = a.Desnivel, Angulo = 90 };
        c.Apoios.Add(new ApoioEntrePecas { Apoiada = "L2", Ponta = PontaPeca.Inferior, Suporte = "L1", X = x, Direcao = DirecaoApoio.AoLongo });
        return c;
    }

    /// <summary>
    /// Em U: os dois lances chegam pelo lado do patamar, que é peça própria e vence a largura da caixa (de um lado ao outro, na
    /// direção transversal aos lances). Ponta inferior do patamar na estrutura; a superior na viga do patamar (ou na estrutura, sem viga).
    /// </summary>
    public static ConjuntoEscada EmU(EscadaLaje l1, EscadaLaje l2, double folga = 15, double profundidade = 150, double apoioPatamar = 10, VigaPatamar viga = null, EscadaLaje patamar = null)
    {
        var c = new ConjuntoEscada { Modelo = "Em U (patamar como peça" + (viga != null ? ", sobre a viga do patamar)" : ")") };
        var a = l1.Copia(); var b = l2.Copia();
        var g1 = GeometriaEscada.De(a);
        double cA = Math.Max(5, a.LigacaoSuperior.ComprimentoApoio);
        double y0 = -a.Largura / 2 - apoioPatamar, yL2 = a.Largura / 2 + folga + b.Largura / 2, y1 = yL2 + b.Largura / 2 + apoioPatamar;
        double xn = g1.Vao - cA / 2;                                   // borda do patamar do lado dos lances
        var p = patamar?.Copia() ?? new EscadaLaje { Espessura = a.EspessuraPatamar > 0 ? a.EspessuraPatamar : a.Espessura, Execucao = a.Execucao, Fck = a.Fck, Cobrimento = a.Cobrimento,
                                                     Revestimento = a.Revestimento, Sobrecarga = a.Sobrecarga, LigacaoInferior = a.LigacaoInferior.Copia(), LigacaoSuperior = a.LigacaoInferior.Copia() };
        p.Peca = PecaEscada.Patamar; p.Tipo = TipoEscada.Longitudinal;
        p.ComprimentoPatamar = y1 - y0; p.Largura = profundidade; p.Desnivel = 0;
        c.Pecas.Add(("L1", a)); c.Pecas.Add(("L2", b)); c.Pecas.Add(("PAT", p));
        c.Posicoes["L1"] = new PosicaoPeca();
        c.Posicoes["PAT"] = new PosicaoPeca { X = xn + profundidade / 2, Y = y0, Z = a.Desnivel, Angulo = 90 };
        c.Posicoes["L2"] = new PosicaoPeca { X = g1.Vao, Y = yL2, Z = a.Desnivel, Angulo = 180 };
        c.Apoios.Add(new ApoioEntrePecas { Apoiada = "L1", Ponta = PontaPeca.Superior, Suporte = "PAT", X = -y0, Direcao = DirecaoApoio.AoLongo });
        c.Apoios.Add(new ApoioEntrePecas { Apoiada = "L2", Ponta = PontaPeca.Inferior, Suporte = "PAT", X = yL2 - y0, Direcao = DirecaoApoio.AoLongo });
        if (viga != null)
        {
            var v = viga;
            v.Comprimento = profundidade + 2 * Math.Max(v.ApoioE, v.ApoioD) + 20;
            c.Vigas.Add(v);
            // a viga corre ao longo da profundidade do patamar, sob a ponta superior dele
            c.Posicoes[v.Nome] = new PosicaoPeca { X = xn - Math.Max(v.ApoioE, v.ApoioD) - 10, Y = y1, Z = a.Desnivel + GeometriaEscada.De(p).ZAssentoSup, Angulo = 0 };
            c.Apoios.Add(new ApoioEntrePecas { Apoiada = "PAT", Ponta = PontaPeca.Superior, Suporte = v.Nome, X = v.Comprimento / 2, Direcao = DirecaoApoio.AoLongo });
        }
        return c;
    }

    /// <summary>Topo da peça (cota local, cm) no x dela: 0 no patamar de baixo, o desnível no de cima, a linha dos degraus no lance.</summary>
    public static double Topo(EscadaLaje e, GeometriaEscada g, double x)
    {
        if (e.SoPatamar || x <= g.X1) return 0;
        if (x >= g.X2) return e.Desnivel;
        return Math.Floor((x - g.X1) / g.Piso + 1) * g.Espelho;
    }

    /// <summary>
    /// Confere as posições: cada ponta apoiada tem de cair sobre o suporte (o centro do apoio no x informado e dentro da largura dele) e
    /// na mesma cota do topo (ou do assento, para a viga). Devolve os avisos (vazio = coerente).
    /// </summary>
    public static List<string> ConferirPosicoes(ConjuntoEscada c, double tol = 2)
    {
        var av = new List<string>();
        var geos = c.Pecas.ToDictionary(p => p.Nome, p => GeometriaEscada.De(p.Peca));
        foreach (var a in c.Apoios)
        {
            if (!c.Posicoes.TryGetValue(a.Apoiada, out var pa) || !c.Posicoes.TryGetValue(a.Suporte, out var ps)) { av.Add($"{a.Apoiada} → {a.Suporte}: falta a posição em planta."); continue; }
            var ea = c.Peca(a.Apoiada); if (ea == null) continue;
            var ga = geos[a.Apoiada];
            double xa = a.Ponta == PontaPeca.Inferior ? 0 : ga.Vao;
            var pt = pa.Em(xa);
            double za = pa.Z + (a.Ponta == PontaPeca.Inferior || ea.SoPatamar ? 0 : ea.Desnivel);
            var (ao, lado) = ps.Local(pt.X, pt.Y);
            var es = c.Peca(a.Suporte);
            if (es != null)
            {
                var gs = geos[a.Suporte];
                // em L/U a ponta apoiada corre ao longo do vão do suporte: o centro dela é o x do apoio
                if (Math.Abs(ao - a.X) > tol) av.Add($"{a.Apoiada} ({a.Ponta}) cai em x = {ao:0} cm de {a.Suporte}, e o apoio diz x = {a.X:0}.");
                if (Math.Abs(lado) > es.Largura / 2 + tol) av.Add($"{a.Apoiada} ({a.Ponta}) cai fora da largura de {a.Suporte} ({lado:0} cm do eixo).");
                double zs = ps.Z + Topo(es, gs, a.X);
                if (Math.Abs(za - zs) > tol) av.Add($"{a.Apoiada} ({a.Ponta}): topo em {za:0} cm e o topo de {a.Suporte} ali em {zs:0} cm.");
            }
            else
            {
                var v = c.Vigas.FirstOrDefault(q => q.Nome == a.Suporte); if (v == null) continue;
                if (ao < -tol || ao > v.Comprimento + tol) av.Add($"{a.Apoiada} ({a.Ponta}) cai fora da viga {v.Nome}.");
                if (Math.Abs(lado) > v.B / 2 + 15) av.Add($"{a.Apoiada} ({a.Ponta}) cai a {lado:0} cm do eixo da viga {v.Nome}.");
                double zAss = za + (a.Ponta == PontaPeca.Inferior ? ga.ZAssentoInf : ga.ZAssentoSup);
                if (Math.Abs(zAss - ps.Z) > tol) av.Add($"{a.Apoiada} ({a.Ponta}): assento em {zAss:0} cm e o topo da viga {v.Nome} em {ps.Z:0} cm.");
            }
        }
        return av;
    }
}
