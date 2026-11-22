using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D;
using DEB.Core.Motor;

namespace DEB.Core.Elementos.Viga.PreMoldada.Modelo3D;

/// <summary>
/// Barras 3D da viga pré-moldada (D40) a partir dos MESMOS valores e regras do template do CAD (D26: um modelo só para o desenho
/// do CAD, a prévia e o Revit): longitudinais (<see cref="TemplateVigaPreMoldada.LerLongitudinais"/> e
/// <see cref="TemplateVigaPreMoldada.PontosNaSecao"/>), estribos por zona (<see cref="TemplateVigaPreMoldada.ZonasEstribos"/>),
/// dente Gerber do catálogo (estribos do dente, suspensão, tirante, grampo e costura), barra transversal soldada na ponta da
/// inferior, porta-estribo do 2º estágio e alças de cordoalha.
/// Sistema local da viga: A ao longo (ponta esquerda em 0), B na largura, C para cima (fundo da alma em <c>z0</c>); pés, como o
/// modelo 3D do pilar (<see cref="BarDef"/>). A alma começa em <c>y0</c> (a aba fica fora dela).
/// Ainda não entram (avisos): reforço das aberturas, aba, ressalto e protensão — continuam só no CAD.
/// </summary>
public static class VigaRebarLayout
{
    private static double Ft(double cm) => U_.Mm(cm * 10);
    private static Loc P(double x, double y, double z) => new Loc(Ft(x), Ft(y), Ft(z));

    public static List<BarDef> Compute(ValoresTemplate v, ConfiguracaoArmadura cfg, double y0, double z0, List<string> avisos = null,
        IReadOnlyList<TipoDente> catalogo = null)
    {
        avisos ??= new List<string>();
        cfg ??= new ConfiguracaoArmadura();
        catalogo ??= TemplateVigaPreMoldada.CatalogoDentes;
        var bars = new List<BarDef>();
        double b = v.Numero("B"), h = v.Numero("H"), L = v.Numero("L");
        double cobF = v.Numero("CobFundo"), cobT = v.Numero("CobTopo"), cobL = v.Numero("CobLateral"), cobP = v.Numero("CobPontas");
        double phiT = v.Numero("EstBitola"), t = phiT / 10;
        string acoL = v["LongAco"], acoT = v["EstAco"];
        var lixo = new List<string>();
        var e = TemplateVigaPreMoldada.LerExtremidade(v, true, catalogo, lixo);
        var d = TemplateVigaPreMoldada.LerExtremidade(v, false, catalogo, lixo);
        double capa = TemplateVigaPreMoldada.Capa(v);
        var longs = TemplateVigaPreMoldada.LerLongitudinais(v, L, cobP, e, d, avisos);

        // ---------------- longitudinais ----------------
        foreach (var l in longs)
        {
            double s = l.Inferior ? 1 : -1;
            foreach (var (px, pz) in TemplateVigaPreMoldada.PontosNaSecao(l, b, h, cobF, cobT, cobL, phiT, longs))
            {
                double y = y0 + px, z = z0 + pz;
                // barra abaixo do fundo do dente (pele, 2ª camada): para na face do recorte, a cobrimento — senão sai do concreto
                double ini = l.Ini, fim = l.Fim;
                if (e.TemDente && pz < h - e.Dente.Altura + cobF) ini = Math.Max(ini, e.CompDente + cobP);
                if (d.TemDente && pz < h - d.Dente.Altura + cobF) fim = Math.Min(fim, L - d.CompDente - cobP);
                if (fim - ini < 10) continue;
                var pts = new List<Loc>();
                if (l.DobraE > 0 && ini == l.Ini) pts.Add(P(ini, y, z + s * l.DobraE));
                pts.Add(P(ini, y, z));
                pts.Add(P(fim, y, z));
                if (l.DobraD > 0 && fim == l.Fim) pts.Add(P(fim, y, z + s * l.DobraD));
                bars.Add(new BarDef { Role = BarRole.Longitudinal, DiameterMm = l.Bitola, Steel = acoL, Points = pts, Normal = Loc.UnitB });
            }
            // barra transversal soldada nas pontas da 1ª camada inferior (NBR 9062 7.4/7.5)
            if (l.Inferior && l.Camada == 1 && v.SimNao("InfSoldada"))
            {
                double z = z0 + cobF + t + l.Bitola / 20 + l.Bitola / 10;   // logo acima da camada (encostada nas barras)
                foreach (double x in new[] { l.Ini + l.Bitola / 20, l.Fim - l.Bitola / 20 })
                    bars.Add(new BarDef
                    {
                        Role = BarRole.BeamWelded, DiameterMm = l.Bitola, Steel = acoL, Normal = Loc.UnitA,
                        Points = { P(x, y0 + cobL + t, z), P(x, y0 + b - cobL - t, z) },
                    });
            }
        }

        // porta-estribo do 2º estágio (na capa, cantos de cima do estribo, por dentro)
        int nPorta = capa > 0 ? Math.Max(0, v.Inteiro("PortaEstriboQtd")) : 0;
        if (nPorta > 0)
        {
            double rp = v.Numero("PortaEstriboBitola") / 20, zp = z0 + h + capa - cobT - t - rp;
            double ya = y0 + cobL + t + rp, yb = y0 + b - cobL - t - rp;
            for (int i = 0; i < nPorta; i++)
            {
                double y = nPorta == 1 ? (ya + yb) / 2 : ya + i * (yb - ya) / (nPorta - 1);
                bars.Add(new BarDef { Role = BarRole.Longitudinal, DiameterMm = v.Numero("PortaEstriboBitola"), Steel = acoL, Normal = Loc.UnitB,
                                      Points = { P(cobP, y, zp), P(L - cobP, y, zp) } });
            }
        }

        // ---------------- estribos da alma, por zona ----------------
        var zonas = TemplateVigaPreMoldada.ZonasEstribos(v, cfg, L, e, d);
        var abs = TemplateVigaPreMoldada.LerAberturas(v, h, L, e, d, lixo);
        if (abs.Count > 0)
        {
            zonas = TemplateVigaPreMoldada.ZonasComAberturas(v, cfg, zonas, abs);
            avisos.Add($"{abs.Count} abertura(s) na alma: os estribos curtos e as barras de reforço ainda não são criados no Revit (ficam no detalhamento do CAD).");
        }
        if (v["Aba"] != "" && v["Aba"] != TemplateVigaPreMoldada.AbaSem) avisos.Add("Aba lateral: a armadura da aba ainda não é criada no Revit (fica no detalhamento do CAD).");
        if (v["Ressalto"] != "" && v["Ressalto"] != TemplateVigaPreMoldada.RessaltoSem) avisos.Add("Ressalto: a armadura do ressalto ainda não é criada no Revit (fica no detalhamento do CAD).");
        if (v.SimNao("Protendida")) avisos.Add("Viga protendida: as cordoalhas não são criadas no Revit (o cálculo e a armação no Revit são só de concreto armado).");
        double ya0 = y0 + cobL + t / 2, ya1 = y0 + b - cobL - t / 2, za0 = z0 + cobF + t / 2;
        double za1 = z0 + h + capa - cobT - t / 2;
        bool aberto = v["EstTipo"] == TemplateVigaPreMoldada.EstAberto;
        double sal = aberto ? Math.Max(0, v.Numero("EstSaliencia")) : 0;
        List<Loc> Estribo(double x)
        {
            if (aberto) return new List<Loc> { P(x, ya0, z0 + h + sal), P(x, ya0, za0), P(x, ya1, za0), P(x, ya1, z0 + h + sal) };
            return new List<Loc> { P(x, ya0, za0), P(x, ya1, za0), P(x, ya1, za1), P(x, ya0, za1) };
        }
        foreach (var z in zonas)
        {
            if (z.Contada > 0) continue;   // estribos curtos das aberturas (ver aviso)
            AdicionarConjuntos(bars, z.Posicoes, x => new BarDef
            {
                Role = BarRole.Stirrup, DiameterMm = phiT, Steel = acoT, Points = Estribo(x), Closed = !aberto, Hooks135 = !aberto, Normal = Loc.UnitA,
            });
        }

        // ---------------- dentes Gerber (catálogo) ----------------
        foreach (var (x, esq) in new[] { (e, true), (d, false) })
        {
            if (!x.TemDente) continue;
            var dt = x.Dente;
            double zDente = z0 + h - dt.Altura;   // fundo do dente
            double sg = esq ? 1 : -1, xb = esq ? cobP : L - cobP;
            if (dt.EstGerberQtd > 0 && dt.EstGerberBitola > 0)
            {
                double tg = dt.EstGerberBitola / 10;
                AdicionarConjuntos(bars, TemplateVigaPreMoldada.PosicoesGerber(dt, cobP, esq, L), xx => new BarDef
                {
                    Role = BarRole.BeamGerber, DiameterMm = dt.EstGerberBitola, Steel = acoT, Closed = true, Hooks135 = true, Normal = Loc.UnitA,
                    Points = { P(xx, y0 + cobL + tg / 2, zDente + cobF + tg / 2), P(xx, y0 + b - cobL - tg / 2, zDente + cobF + tg / 2),
                               P(xx, y0 + b - cobL - tg / 2, z0 + h - cobT - tg / 2), P(xx, y0 + cobL + tg / 2, z0 + h - cobT - tg / 2) },
                });
            }
            if (dt.SuspQtd > 0 && dt.SuspBitola > 0)
            {
                double ts = dt.SuspBitola / 10;
                AdicionarConjuntos(bars, TemplateVigaPreMoldada.PosicoesSuspensao(dt, cobP, esq, L, x.TrechoSuspensao), xx => new BarDef
                {
                    Role = BarRole.BeamHanger, DiameterMm = dt.SuspBitola, Steel = acoT, Closed = true, Hooks135 = true, Normal = Loc.UnitA,
                    Points = { P(xx, y0 + cobL + ts / 2, z0 + cobF + ts / 2), P(xx, y0 + b - cobL - ts / 2, z0 + cobF + ts / 2),
                               P(xx, y0 + b - cobL - ts / 2, z0 + h - cobT - ts / 2), P(xx, y0 + cobL + ts / 2, z0 + h - cobT - ts / 2) },
                });
            }
            // tirante: uma camada no topo do dente, dentro do estribo do dente, de cobrimento a cobrimento + TiranteComp
            double tgc = dt.EstGerberQtd > 0 ? dt.EstGerberBitola / 10 : 0;
            double yl = y0 + cobL + tgc, yr = y0 + b - cobL - tgc;
            if (dt.TiranteQtd > 0 && dt.TiranteBitola > 0)
            {
                double rt = dt.TiranteBitola / 20, zt = z0 + h - cobT - tgc - rt;
                int nt = dt.TiranteQtd;
                for (int i = 0; i < nt; i++)
                {
                    double y = nt == 1 ? (yl + yr) / 2 : yl + rt + i * (yr - yl - 2 * rt) / (nt - 1);
                    bars.Add(new BarDef { Role = BarRole.BeamTie, DiameterMm = dt.TiranteBitola, Steel = acoL, Normal = Loc.UnitB,
                                          Points = { P(xb, y, zt), P(xb + sg * dt.TiranteComp, y, zt) } });
                }
                // barra transversal soldada na ponta do tirante (mesmo φ)
                bars.Add(new BarDef { Role = BarRole.BeamWelded, DiameterMm = dt.TiranteBitola, Steel = acoL, Normal = Loc.UnitA,
                                      Points = { P(xb + sg * rt, yl, zt - 2 * rt), P(xb + sg * rt, yr, zt - 2 * rt) } });
            }
            // grampo (metade de cima) e costura (metade de baixo): U horizontais, fechados na ponta, pernas entrando na viga
            double zTir = z0 + h - cobT - tgc - dt.TiranteBitola / 10 - 1.5, zMeio = zDente + dt.Altura / 2, zBase = zDente + cobF + tgc;
            void Us(BarRole role, double phi, int q, double perna, double za, double zb)
            {
                if (q <= 0 || phi <= 0) return;
                for (int i = 0; i < q; i++)
                {
                    double z = q == 1 ? (za + zb) / 2 : za + i * (zb - za) / (q - 1);
                    double r = phi / 20;
                    bars.Add(new BarDef
                    {
                        Role = role, DiameterMm = phi, Steel = acoL, Normal = Loc.UnitC,
                        Points = { P(xb + sg * perna, yl + r, z), P(xb, yl + r, z), P(xb, yr - r, z), P(xb + sg * perna, yr - r, z) },
                    });
                }
            }
            Us(BarRole.BeamHairpin, dt.GrampoBitola, dt.GrampoQtd, dt.GrampoPerna, zMeio, zTir);
            Us(BarRole.BeamStitch, dt.CosturaBitola, dt.CosturaQtd, dt.CosturaPerna, zBase, zMeio - 1.5);
        }

        // ---------------- alças de cordoalha (CP-190) ----------------
        if (v.SimNao("Alcas"))
        {
            double emb = TemplateVigaPreMoldada.EmbutimentoAlcaViga(v), acima = v.Numero("AlcaAcima"), ent = v.Numero("AlcaEntre"), pe = v.Numero("AlcaPe");
            double phiA = v.Numero("AlcaBitola");
            int pernas = Math.Max(1, v.Inteiro("AlcaPernas"));
            double zPe = z0 + h - Math.Min(emb, h - cobF), zTopo = z0 + h + acima;
            foreach (double xc in TemplateVigaPreMoldada.PosicaoAlcas(v, b, L, e, d))
                for (int k = 0; k < pernas; k++)
                {
                    double y = y0 + b / 2 + (k - (pernas - 1) / 2.0) * Math.Max(2.5, phiA / 10 + 1.5);
                    double xa = xc - ent / 2, xz = xc + ent / 2;
                    var bd = new BarDef { Role = BarRole.LiftLoop, DiameterMm = phiA, Steel = "CP-190", Normal = Loc.UnitB };
                    if (pe > 0) bd.Points.Add(P(xa - pe, y, zPe));
                    bd.Points.Add(P(xa, y, zPe));
                    bd.Points.Add(P(xa, y, zTopo - ent / 2));
                    bd.ArcMid[bd.Points.Count - 1] = P(xc, y, zTopo);
                    bd.Points.Add(P(xz, y, zTopo - ent / 2));
                    bd.Points.Add(P(xz, y, zPe));
                    if (pe > 0) bd.Points.Add(P(xz + pe, y, zPe));
                    bars.Add(bd);
                }
        }
        return bars;
    }

    /// <summary>Posições ao longo da viga → conjuntos do Revit: trechos de espaçamento constante viram um conjunto com quantidade.</summary>
    private static void AdicionarConjuntos(List<BarDef> bars, IReadOnlyList<double> xs, Func<double, BarDef> criar)
    {
        var l = xs.OrderBy(x => x).ToList();
        int i = 0;
        while (i < l.Count)
        {
            int j = i + 1;
            double s = j < l.Count ? l[j] - l[i] : 0;
            while (j + 1 < l.Count && Math.Abs(l[j + 1] - l[j] - s) < 0.05) j++;
            if (j >= l.Count) j = l.Count - 1;
            int n = s > 0 ? j - i + 1 : 1;
            var bd = criar(l[i]);
            bd.Count = n;
            bd.Spacing = n > 1 ? Ft(s) : 0;
            bars.Add(bd);
            i += n;
        }
    }

    /// <summary>Gira o conjunto 180° em torno do eixo vertical da peça (peça igual, girada em relação à representante).</summary>
    public static List<BarDef> Girar180(IEnumerable<BarDef> bars, double comprimentoFt, double larguraFt)
    {
        Loc G(Loc p) => new Loc(comprimentoFt - p.A, larguraFt - p.B, p.C);
        Loc Gd(Loc d) => new Loc(-d.A, -d.B, d.C);
        return bars.Select(b =>
        {
            var n = new BarDef
            {
                Role = b.Role, DiameterMm = b.DiameterMm, Steel = b.Steel, Closed = b.Closed, Hooks135 = b.Hooks135, Count = b.Count,
                Spacing = b.Spacing, Points = b.Points.Select(G).ToList(), Normal = Gd(b.Normal),
                ArcMid = b.ArcMid.ToDictionary(kv => kv.Key, kv => G(kv.Value)),
            };
            return n;
        }).ToList();
    }
}
