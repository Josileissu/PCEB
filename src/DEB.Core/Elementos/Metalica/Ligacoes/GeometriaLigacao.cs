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
            case TipoLigacao.ApoioTerca:
            {
                // viga de apoio (ou banzo) ao longo de y com a mesa superior em z = 0; chapa no plano xz (espessura em y), largura bp ao
                // longo de x; terça Ue ao longo de x com a alma encostada na face +y da chapa e as abas para +y
                var d = DadosApoioTerca.DoModelo(m);
                double L = Math.Max(3 * viga.Bf, 500), ht = d.HTerca, bt = Math.Max(50, 0.4 * ht), tt = d.TTerca, y0 = d.Tp / 2, Lt = Math.Max(4 * ht, 3 * d.Bp);
                l.Add(new Caixa(PapelSolido.Membro, -viga.Bf / 2, viga.Bf / 2, -L / 2, L / 2, -viga.Tf, 0, "viga de apoio " + viga.Nome));
                l.Add(new Caixa(PapelSolido.Membro, -viga.Tw / 2, viga.Tw / 2, -L / 2, L / 2, -viga.D + viga.Tf, -viga.Tf, "viga de apoio " + viga.Nome));
                l.Add(new Caixa(PapelSolido.Membro, -viga.Bf / 2, viga.Bf / 2, -L / 2, L / 2, -viga.D, -viga.D + viga.Tf, "viga de apoio " + viga.Nome));
                l.Add(new Caixa(PapelSolido.Chapa, -d.Bp / 2, d.Bp / 2, -d.Tp / 2, d.Tp / 2, 0, d.Hp, $"chapa {d.Bp:0} × {d.Hp:0} × {d.Tp:0.#} mm"));
                foreach (int sy in new[] { 1, -1 })
                    l.Add(new Caixa(PapelSolido.Solda, -d.Bp / 2, d.Bp / 2, sy > 0 ? y0 : -y0 - d.Perna, sy > 0 ? y0 + d.Perna : -y0, 0, d.Perna, "filete"));
                double zc = d.ZCentro, zb = Math.Max(d.Perna + 2, zc - ht / 2);
                l.Add(new Caixa(PapelSolido.Membro, -Lt / 2, Lt / 2, y0, y0 + tt, zb, zb + ht, "terça (alma)"));
                l.Add(new Caixa(PapelSolido.Membro, -Lt / 2, Lt / 2, y0, y0 + bt, zb, zb + tt, "terça (mesa)"));
                l.Add(new Caixa(PapelSolido.Membro, -Lt / 2, Lt / 2, y0, y0 + bt, zb + ht - tt, zb + ht, "terça (mesa)"));
                for (int i = 0; i < d.N; i++)
                {
                    double z = d.Hp - d.Ev - i * d.S;
                    l.Add(new Caixa(PapelSolido.Parafuso, -d.Db / 2, d.Db / 2, -y0 - 1.2 * d.Db, y0 + tt + 1.2 * d.Db, z - d.Db / 2, z + d.Db / 2, $"ø{d.Db:0.#}"));
                }
                break;
            }
            case TipoLigacao.ChapaNo:
            {
                // suporte: mesa do pilar na face x = 0; chapa de nó no plano xz (espessura em y); barra ao longo de x
                var d = DadosChapaNo.DoModelo(m);
                double lg = d.Comprimento + d.Ev + 80, hg = Math.Max(d.Lw, d.Whitmore + 60);
                l.AddRange(Suporte(pilar, 0));
                l.Add(new Caixa(PapelSolido.Chapa, 0, lg, 0, d.Tg, -hg / 2, hg / 2, $"chapa de nó {d.Tg:0.#} mm"));
                foreach (int sy in new[] { 1, -1 })
                    l.Add(new Caixa(PapelSolido.Solda, 0, d.Perna, sy > 0 ? d.Tg : -d.Perna, sy > 0 ? d.Tg + d.Perna : 0, -d.Lw / 2, d.Lw / 2, "filete"));
                double xb0 = lg - d.Ev - d.Comprimento - d.Ev, Lb = 3 * lg;
                // barra encostada na face +y da chapa; a fila de parafusos em z = 0
                double zb = d.Cantoneira ? -d.G : -d.Bb / 2;
                l.Add(new Caixa(PapelSolido.Membro, xb0, xb0 + Lb, d.Tg, d.Tg + d.Tb, zb, zb + d.Bb, d.Cantoneira ? "cantoneira (aba ligada)" : "barra chata"));
                if (d.Cantoneira) l.Add(new Caixa(PapelSolido.Membro, xb0, xb0 + Lb, d.Tg, d.Tg + d.Bb, zb, zb + d.Tb, "cantoneira (aba livre)"));
                for (int i = 0; i < d.N; i++)
                {
                    double x = lg - d.Ev - i * d.S;
                    l.Add(new Caixa(PapelSolido.Parafuso, x - d.Db / 2, x + d.Db / 2, -1.2 * d.Db, d.Tg + d.Tb + 1.2 * d.Db, -d.Db / 2, d.Db / 2, $"ø{d.Db:0.#}"));
                }
                break;
            }
            case TipoLigacao.TuboFendido:
            {
                // chapa de nó soldada à mesa do pilar (x = 0), tubo ao longo de x encaixado nela pela fenda (o tubo aparece como prisma)
                var d = DadosTuboFendido.DoModelo(m);
                double lg = d.Lw + 60, hg = d.D + 2 * d.Lw * Math.Tan(Math.PI / 6);
                l.AddRange(Suporte(pilar, 0));
                l.Add(new Caixa(PapelSolido.Chapa, 0, lg, -d.Tg / 2, d.Tg / 2, -hg / 2, hg / 2, $"chapa de nó {d.Tg:0.#} mm"));
                double xt = lg - d.Lw, r = d.D / 2;
                // tubo em 8 faixas (octógono) para o desenho
                for (int k = 0; k < 8; k++)
                {
                    double a0 = k * Math.PI / 4, ym = r * Math.Cos(a0 + Math.PI / 8), zm = r * Math.Sin(a0 + Math.PI / 8), w = 2 * r * Math.Sin(Math.PI / 8);
                    bool hor = Math.Abs(Math.Cos(a0 + Math.PI / 8)) < Math.Abs(Math.Sin(a0 + Math.PI / 8));
                    if (hor) l.Add(new Caixa(PapelSolido.Membro, xt, xt + 4 * d.D, ym - w / 2, ym + w / 2, zm - d.T / 2, zm + d.T / 2, $"tubo ø{d.D:0.#} × {d.T:0.#}"));
                    else l.Add(new Caixa(PapelSolido.Membro, xt, xt + 4 * d.D, ym - d.T / 2, ym + d.T / 2, zm - w / 2, zm + w / 2, $"tubo ø{d.D:0.#} × {d.T:0.#}"));
                }
                foreach (int sz in new[] { 1, -1 })
                    foreach (int sy in new[] { 1, -1 })
                        l.Add(new Caixa(PapelSolido.Solda, xt, lg, sy > 0 ? d.Tg / 2 : -d.Tg / 2 - d.Perna, sy > 0 ? d.Tg / 2 + d.Perna : -d.Tg / 2, sz * (r - d.T) - d.Perna / 2, sz * (r - d.T) + d.Perna / 2, "filete"));
                foreach (int sy in new[] { 1, -1 })
                    l.Add(new Caixa(PapelSolido.Solda, 0, d.Perna, sy > 0 ? d.Tg / 2 : -d.Tg / 2 - d.Perna, sy > 0 ? d.Tg / 2 + d.Perna : -d.Tg / 2, -hg / 2, hg / 2, "filete no suporte"));
                break;
            }
            case TipoLigacao.ChapaEmbutida:
            {
                // topo do pilar de concreto em z = 0 com a chapa embutida rente; pinos para baixo; viga de aço apoiada ao longo de x
                var d = DadosChapaEmbutida.DoModelo(m);
                double lc = Math.Max(d.Bp, d.Hp) + 300, dh = d.DCabeca > 0 ? d.DCabeca : 1.6 * d.Db, kc = Math.Max(8, 0.5 * d.Db);
                l.Add(new Caixa(PapelSolido.Concreto, -lc / 2, lc / 2, -lc / 2, lc / 2, -d.Hef - 300, -d.Tp, "pilar de concreto"));
                l.Add(new Caixa(PapelSolido.Chapa, -d.Bp / 2, d.Bp / 2, -d.Hp / 2, d.Hp / 2, -d.Tp, 0, $"chapa embutida {d.Bp:0} × {d.Hp:0} × {d.Tp:0.#}"));
                for (int i = 0; i < d.Nx; i++)
                    for (int j = 0; j < d.Ny; j++)
                    {
                        double x = (i - (d.Nx - 1) / 2.0) * d.Sx, y = (j - (d.Ny - 1) / 2.0) * d.Sy;
                        l.Add(new Caixa(PapelSolido.Parafuso, x - d.Db / 2, x + d.Db / 2, y - d.Db / 2, y + d.Db / 2, -d.Tp - d.Hef, -d.Tp, $"pino ø{d.Db:0}"));
                        l.Add(new Caixa(PapelSolido.Parafuso, x - dh / 2, x + dh / 2, y - dh / 2, y + dh / 2, -d.Tp - d.Hef - kc, -d.Tp - d.Hef, "cabeça"));
                    }
                // viga apoiada (perfil de referência), soldada na chapa
                double L = lc * 1.3;
                l.AddRange(PerfilI(viga, -L / 2, L / 2, false, "viga " + viga.Nome).Select(c => new Caixa(c.Papel, c.X0, c.X1, c.Y0, c.Y1, c.Z0 + viga.D / 2, c.Z1 + viga.D / 2, c.Rotulo)));
                foreach (int sy in new[] { 1, -1 })
                    l.Add(new Caixa(PapelSolido.Solda, -d.Bp / 2 + 10, d.Bp / 2 - 10, sy * viga.Bf / 2, sy * (viga.Bf / 2 + 6), 0, 6, "filete na chapa"));
                break;
            }
            case TipoLigacao.EmendaTalas:
            {
                // duas vigas ao longo de x com a junta em x = 0; talas externas nas mesas e duas talas na alma
                var d = DadosEmendaTalas.DoModelo(m, viga, AcoEstrutural.Buscar("A572"));
                double g = d.Folga / 2, Lv = d.LTalaMesa / 2 + 300, h2 = viga.D / 2;
                l.AddRange(PerfilI(viga, g, g + Lv, false, "viga " + viga.Nome));
                l.AddRange(PerfilI(viga, -g - Lv, -g, false, "viga " + viga.Nome));
                double lm = d.LTalaMesa / 2, ha = d.HTalaAlma / 2, la = d.Folga / 2 + 2 * d.E;
                l.Add(new Caixa(PapelSolido.Chapa, -lm, lm, -d.BTalaMesa / 2, d.BTalaMesa / 2, h2, h2 + d.Tm, $"tala de mesa {d.BTalaMesa:0} × {d.Tm:0.#}"));
                l.Add(new Caixa(PapelSolido.Chapa, -lm, lm, -d.BTalaMesa / 2, d.BTalaMesa / 2, -h2 - d.Tm, -h2, $"tala de mesa {d.BTalaMesa:0} × {d.Tm:0.#}"));
                foreach (int sy in new[] { 1, -1 })
                    l.Add(new Caixa(PapelSolido.Chapa, -la, la, sy * viga.Tw / 2, sy * (viga.Tw / 2 + d.Ta), -ha, ha, $"tala de alma {d.Ta:0.#} × {d.HTalaAlma:0}"));
                foreach (int sx in new[] { 1, -1 })
                {
                    for (int i = 0; i < d.Nm; i++)
                    {
                        double x = sx * (g + d.E + i * d.Sm);
                        foreach (int sy in new[] { 1, -1 })
                            foreach (int sz in new[] { 1, -1 })
                                l.Add(new Caixa(PapelSolido.Parafuso, x - d.Db / 2, x + d.Db / 2, sy * d.Gm / 2 - d.Db / 2, sy * d.Gm / 2 + d.Db / 2,
                                                sz > 0 ? h2 - viga.Tf - 1.2 * d.Db : -h2 - d.Tm - 1.2 * d.Db, sz > 0 ? h2 + d.Tm + 1.2 * d.Db : -h2 + viga.Tf + 1.2 * d.Db, $"ø{d.Db:0.#}"));
                    }
                    for (int i = 0; i < d.Na; i++)
                    {
                        double x = sx * (g + d.E), z = (i - (d.Na - 1) / 2.0) * d.Sa;
                        l.Add(new Caixa(PapelSolido.Parafuso, x - d.Db / 2, x + d.Db / 2, -viga.Tw / 2 - d.Ta - 1.2 * d.Db, viga.Tw / 2 + d.Ta + 1.2 * d.Db, z - d.Db / 2, z + d.Db / 2, $"ø{d.Db:0.#}"));
                    }
                }
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
