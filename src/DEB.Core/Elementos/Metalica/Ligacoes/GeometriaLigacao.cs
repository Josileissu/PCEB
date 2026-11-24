using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Elementos.Metalica.Ligacoes;

/// <summary>Papel de um sólido no desenho da ligação: os perfis ligados ficam transparentes; o resto é a ligação.</summary>
public enum PapelSolido { Membro, Chapa, Enrijecedor, Parafuso, Solda, Concreto }

/// <summary>Caixa alinhada aos eixos (mm) com o papel e um rótulo.</summary>
public sealed class Caixa
{
    public PapelSolido Papel { get; set; }
    public double X0, X1, Y0, Y1, Z0, Z1;
    public string Rotulo { get; set; } = "";
    public Caixa(PapelSolido p, double x0, double x1, double y0, double y1, double z0, double z1, string rotulo = "")
    {
        Papel = p; X0 = Math.Min(x0, x1); X1 = Math.Max(x0, x1); Y0 = Math.Min(y0, y1); Y1 = Math.Max(y0, y1); Z0 = Math.Min(z0, z1); Z1 = Math.Max(z0, z1); Rotulo = rotulo;
    }
}

/// <summary>
/// Geometria 3D das ligações da biblioteca (D50, item 4) em caixas, no sistema da ligação (mm): x ao longo da viga a partir da
/// face do suporte, y transversal, z vertical (eixo da viga em z = 0). Placa de base: x ao longo da alma do pilar, z = 0 na face de
/// cima da placa. A mesma geometria dá as vistas de topo, frontal, lateral e o 3D da janela, e (depois) as famílias no Revit.
/// </summary>
public static class GeometriaLigacao
{
    /// <summary>Perfil I como caixas: x ao longo de [x0, x1], alma em z (d) — ou, se <paramref name="vertical"/>, eixo em z e alma em x.</summary>
    private static IEnumerable<Caixa> PerfilI(PerfilI p, double a0, double a1, bool vertical, string rot)
    {
        double d = p.D, bf = p.Bf, tf = p.Tf, tw = p.Tw;
        if (!vertical)
        {
            yield return new Caixa(PapelSolido.Membro, a0, a1, -bf / 2, bf / 2, d / 2 - tf, d / 2, rot);
            yield return new Caixa(PapelSolido.Membro, a0, a1, -bf / 2, bf / 2, -d / 2, -d / 2 + tf, rot);
            yield return new Caixa(PapelSolido.Membro, a0, a1, -tw / 2, tw / 2, -d / 2 + tf, d / 2 - tf, rot);
        }
        else
        {
            yield return new Caixa(PapelSolido.Membro, -d / 2, -d / 2 + tf, -bf / 2, bf / 2, a0, a1, rot);
            yield return new Caixa(PapelSolido.Membro, d / 2 - tf, d / 2, -bf / 2, bf / 2, a0, a1, rot);
            yield return new Caixa(PapelSolido.Membro, -d / 2 + tf, d / 2 - tf, -tw / 2, tw / 2, a0, a1, rot);
        }
    }

    /// <summary>Pilar de apoio inteiro atrás da face x = 0 (mesa na face, alma ao longo de x), num trecho de 3·d de altura.</summary>
    private static IEnumerable<Caixa> Suporte(PerfilI pilar, double zMeio)
    {
        double z0 = zMeio - 1.5 * Math.Max(pilar.D, 300), z1 = zMeio + 1.5 * Math.Max(pilar.D, 300), d = pilar.D, tf = pilar.Tf, bf = pilar.Bf, tw = pilar.Tw;
        string r = "pilar " + pilar.Nome;
        yield return new Caixa(PapelSolido.Membro, -tf, 0, -bf / 2, bf / 2, z0, z1, r);
        yield return new Caixa(PapelSolido.Membro, -d, -d + tf, -bf / 2, bf / 2, z0, z1, r);
        yield return new Caixa(PapelSolido.Membro, -d + tf, -tf, -tw / 2, tw / 2, z0, z1, r);
    }

    public static List<Caixa> Montar(ModeloLigacao m, PerfilI viga, PerfilI pilar)
    {
        var l = new List<Caixa>();
        var aco = AcoEstrutural.Buscar("A572");
        switch (m.Tipo)
        {
            case TipoLigacao.ChapaAlma:
            {
                var d = DadosChapaAlma.DoModelo(m, viga, aco);
                double gap = 12, bp = d.A + d.EhChapa, L = Math.Max(2.5 * viga.D, bp + 200);
                l.AddRange(Suporte(pilar, 0));
                l.AddRange(PerfilI(viga, gap, L, false, "viga " + viga.Nome));
                double y0 = viga.Tw / 2;
                l.Add(new Caixa(PapelSolido.Chapa, 0, bp, y0, y0 + d.Tp, -d.AlturaChapa / 2, d.AlturaChapa / 2, $"chapa {d.Tp:0.#} × {d.AlturaChapa:0} mm"));
                l.Add(new Caixa(PapelSolido.Solda, 0, d.Perna, y0 - d.Perna, y0, -d.AlturaChapa / 2, d.AlturaChapa / 2, $"filete {d.Perna:0} mm"));
                l.Add(new Caixa(PapelSolido.Solda, 0, d.Perna, y0 + d.Tp, y0 + d.Tp + d.Perna, -d.AlturaChapa / 2, d.AlturaChapa / 2, $"filete {d.Perna:0} mm"));
                for (int i = 0; i < d.N; i++)
                {
                    double z = (i - (d.N - 1) / 2.0) * d.S;
                    l.Add(new Caixa(PapelSolido.Parafuso, d.A - d.Db / 2, d.A + d.Db / 2, -viga.Tw / 2 - 1.2 * d.Db, y0 + d.Tp + 1.2 * d.Db, z - d.Db / 2, z + d.Db / 2, $"ø{d.Db:0.#}"));
                }
                break;
            }
            case TipoLigacao.CantoneiraDupla:
            {
                var d = DadosCantoneira.DoModelo(m, viga, aco);
                double gap = 12, L = Math.Max(2.5 * viga.D, d.Aba + 200), hc = d.Comprimento;
                l.AddRange(Suporte(pilar, 0));
                l.AddRange(PerfilI(viga, gap, L, false, "viga " + viga.Nome));
                foreach (int s in new[] { 1, -1 })
                {
                    double y0 = s * viga.Tw / 2;
                    l.Add(new Caixa(PapelSolido.Chapa, 0, d.Aba, y0, y0 + s * d.T, -hc / 2, hc / 2, $"L {d.Aba:0} × {d.T:0.#}"));
                    l.Add(new Caixa(PapelSolido.Chapa, 0, d.T, y0, y0 + s * d.Aba, -hc / 2, hc / 2, $"L {d.Aba:0} × {d.T:0.#}"));
                    for (int i = 0; i < d.N; i++)
                    {
                        double z = (i - (d.N - 1) / 2.0) * d.S, ys = y0 + s * (d.Aba - d.Eh);
                        if (!d.SoldadaNoSuporte)
                            l.Add(new Caixa(PapelSolido.Parafuso, -pilar.Tf - 1.2 * d.Db, d.T + 1.2 * d.Db, ys - d.Db / 2, ys + d.Db / 2, z - d.Db / 2, z + d.Db / 2, $"ø{d.Db:0.#}"));
                    }
                    if (d.SoldadaNoSuporte) l.Add(new Caixa(PapelSolido.Solda, 0, d.Perna, y0 + s * d.Aba, y0 + s * (d.Aba + d.Perna), -hc / 2, hc / 2, "filete"));
                }
                for (int i = 0; i < d.N; i++)
                {
                    double z = (i - (d.N - 1) / 2.0) * d.S, x = d.Aba - d.Eh;
                    l.Add(new Caixa(PapelSolido.Parafuso, x - d.Db / 2, x + d.Db / 2, -viga.Tw / 2 - d.T - 1.2 * d.Db, viga.Tw / 2 + d.T + 1.2 * d.Db, z - d.Db / 2, z + d.Db / 2, $"ø{d.Db:0.#}"));
                }
                break;
            }
            case TipoLigacao.ChapaTopo:
            {
                var d = DadosChapaTopo.DoModelo(m, viga);
                double tp = d.Tp > 0 ? d.Tp : (d.TpAdotada > 0 ? d.TpAdotada : 19), L = 2.5 * viga.D;
                double topoZ = viga.D / 2, ext = d.Estendida ? d.Pf + Math.Max(1.5 * d.Db, 40) : 10;
                l.AddRange(Suporte(pilar, 0));
                l.Add(new Caixa(PapelSolido.Chapa, 0, tp, -d.BpEf / 2, d.BpEf / 2, -viga.D / 2 - 10, topoZ + ext, $"chapa de topo {tp:0.#} mm"));
                l.AddRange(PerfilI(viga, tp, L, false, "viga " + viga.Nome));
                // enrijecedores do pilar na altura das mesas (representação)
                foreach (double z in new[] { topoZ - viga.Tf / 2, -topoZ + viga.Tf / 2 })
                    foreach (int s in new[] { 1, -1 })
                        l.Add(new Caixa(PapelSolido.Enrijecedor, -pilar.D + pilar.Tf, -pilar.Tf, s * pilar.Tw / 2, s * pilar.Bf / 2, z - viga.Tf / 2, z + viga.Tf / 2, "enrijecedor do pilar"));
                double pern = d.PernaMesa > 0 ? d.PernaMesa : 8;
                foreach (double z in new[] { topoZ, -topoZ })
                    l.Add(new Caixa(PapelSolido.Solda, tp, tp + pern, -viga.Bf / 2, viga.Bf / 2, z, z + (z > 0 ? pern : -pern), "solda da mesa"));
                var ys = new List<double>();
                if (d.Estendida) ys.Add(topoZ + d.Pf);
                for (int k = 0; k < d.LinhasInternas; k++) ys.Add(topoZ - viga.Tf - d.Pf - k * 3 * d.Db);
                ys.Add(-topoZ + viga.Tf + d.Pf);
                foreach (double z in ys)
                    foreach (int s in new[] { -1, 1 })
                        l.Add(new Caixa(PapelSolido.Parafuso, -pilar.Tf - 1.2 * d.Db, tp + 1.2 * d.Db, s * d.G / 2 - d.Db / 2, s * d.G / 2 + d.Db / 2, z - d.Db / 2, z + d.Db / 2, $"ø{d.Db:0.#}"));
                break;
            }
            default:
            {
                var d = DadosPlacaBase.DoModelo(m, pilar, 1.4);
                double tp = d.Tp > 0 ? d.Tp : (d.TpAdotada > 0 ? d.TpAdotada : 25);
                l.Add(new Caixa(PapelSolido.Concreto, -d.LxEf / 2 - 150, d.LxEf / 2 + 150, -d.LyEf / 2 - 150, d.LyEf / 2 + 150, -tp - 400, -tp - 25, "bloco de concreto"));
                l.Add(new Caixa(PapelSolido.Concreto, -d.LxEf / 2, d.LxEf / 2, -d.LyEf / 2, d.LyEf / 2, -tp - 25, -tp, "argamassa expansiva"));
                l.Add(new Caixa(PapelSolido.Chapa, -d.LxEf / 2, d.LxEf / 2, -d.LyEf / 2, d.LyEf / 2, -tp, 0, $"placa {d.LxEf:0} × {d.LyEf:0} × {tp:0.#} mm"));
                l.AddRange(PerfilI(pilar, 0, Math.Max(600, 2 * pilar.D), true, "pilar " + pilar.Nome));
                foreach (int s in new[] { 1, -1 })
                    l.Add(new Caixa(PapelSolido.Solda, s * (pilar.D / 2) - (s > 0 ? 0 : d.Perna), s * pilar.D / 2 + (s > 0 ? d.Perna : 0), -pilar.Bf / 2, pilar.Bf / 2, 0, d.Perna, "filete"));
                var (a1, _, _, _) = d.Tabela18(out _);
                int porLado = Math.Max(1, d.Nb / 2);
                foreach (int s in new[] { 1, -1 })
                    for (int k = 0; k < porLado; k++)
                    {
                        double y = porLado > 1 ? (k - (porLado - 1) / 2.0) * (d.LyEf - 2 * a1) / (porLado - 1) : 0, x = s * d.A;
                        l.Add(new Caixa(PapelSolido.Parafuso, x - d.Db / 2, x + d.Db / 2, y - d.Db / 2, y + d.Db / 2, -tp - 380, 2.5 * d.Db, $"chumbador ø{d.Db:0}"));
                    }
                break;
            }
        }
        return l;
    }
}
