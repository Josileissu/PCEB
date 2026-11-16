using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Templates.PilarPreMoldado.Modelo3D
{
    public enum BarRole { Longitudinal, Stirrup, CrossTie, CorbelTie, CorbelSew, LiftLoop,
                            CorbelAnchor, CorbelStirrup, CorbelBottom, CorbelPin }

    /// <summary>
    /// Uma barra (ou conjunto de barras iguais) em coordenadas locais do pilar.
    /// O conjunto se repete Count vezes, com passo Spacing, na direção de Normal.
    /// </summary>
    public sealed class BarDef
    {
        public BarRole Role { get; set; }
        public double DiameterMm { get; set; }
        public List<Loc> Points { get; set; } = new List<Loc>();
        public bool Closed { get; set; }
        public Loc Normal { get; set; }
        public bool Hooks135 { get; set; }
        public int Count { get; set; } = 1;
        public double Spacing { get; set; }      // pés
        public Corbel HostCorbel { get; set; }   // null = hospedada no pilar

        /// <summary>Posições de cada barra do conjunto (deslocamento ao longo da normal).</summary>
        public IEnumerable<List<Loc>> Instances()
        {
            for (int i = 0; i < Count; i++)
            {
                Loc off = Normal * (Spacing * i);
                yield return Points.Select(p => p + off).ToList();
            }
        }
    }

    /// <summary>Calcula todas as barras de um pilar a partir da especificação (mesmo código da pré-visualização).</summary>
    public static class ColumnRebarLayout
    {
        public static List<BarDef> Compute(ColumnBox f, IList<Corbel> corbels, ColumnRebarSpec s)
        {
            var bars = new List<BarDef>();
            double cover = U_.Mm(s.CoverMm);
            double dl = U_.Mm(s.LongDiameterMm), dt = U_.Mm(s.StirrupDiameterMm);
            double oT = cover + dt / 2;              // eixo do estribo
            double oL = cover + dt + dl / 2;         // eixo da barra longitudinal
            if (f.Bx <= 2 * oL || f.By <= 2 * oL) return bars;

            // ---------------- Longitudinais ----------------
            // posições na seção pela regra do núcleo, a mesma do template do CAD (SecaoBarras.Posicoes); xs/ys = filas das
            // barras, usadas pelos grampos
            List<double> xs = Linspace(oL, f.Bx - oL, Math.Max(2, s.LongBarsAlongX));
            List<double> ys = Linspace(oL, f.By - oL, Math.Max(2, s.LongBarsAlongY));
            List<(double A, double B)> pos = LongitudinalPositions(f, s);

            double c0 = U_.Mm(s.LongEndCoverMm), c1 = f.H - U_.Mm(s.LongEndCoverMm);
            foreach (var (a, b) in pos)
            {
                bars.Add(new BarDef
                {
                    Role = BarRole.Longitudinal,
                    DiameterMm = s.LongDiameterMm,
                    Points = { new Loc(a, b, c0), new Loc(a, b, c1) },
                    Normal = Loc.UnitA
                });
            }

            // ---------------- Estribos por zonas ----------------
            foreach (var (z, n, sp) in StirrupRuns(f, corbels, s))
            {
                bars.Add(new BarDef
                {
                    Role = BarRole.Stirrup,
                    DiameterMm = s.StirrupDiameterMm,
                    Points = { new Loc(oT, oT, z), new Loc(f.Bx - oT, oT, z), new Loc(f.Bx - oT, f.By - oT, z), new Loc(oT, f.By - oT, z) },
                    Closed = true,
                    Normal = Loc.UnitC,
                    Hooks135 = true,
                    Count = n,
                    Spacing = sp
                });

                if (!s.CrossTies) continue;
                double protect = 20 * dt;   // NBR 6118 18.2.4
                foreach (double x in xs.Skip(1).Take(xs.Count - 2).Where(x => Math.Min(x - xs.First(), xs.Last() - x) > protect + 1e-9))
                    bars.Add(Tie(new Loc(x, oT, z), new Loc(x, f.By - oT, z), s, n, sp));
                foreach (double y in ys.Skip(1).Take(ys.Count - 2).Where(y => Math.Min(y - ys.First(), ys.Last() - y) > protect + 1e-9))
                    bars.Add(Tie(new Loc(oT, y, z), new Loc(f.Bx - oT, y, z), s, n, sp));
            }

            // ---------------- Consolos (NBR 9062) ----------------
            // consolos opostos em nível formam um duplo (D30): tirante e costura passantes, gerados pelo primeiro do par
            if (s.CorbelRebar)
            {
                var par = new Dictionary<Corbel, (Corbel Outro, bool Primeiro)>();
                foreach (var (a, b) in CorbelPairs(corbels))
                {
                    par[corbels[a]] = (corbels[b], true);
                    par[corbels[b]] = (corbels[a], false);
                }
                foreach (Corbel k in corbels)
                    bars.AddRange(par.TryGetValue(k, out var p) ? CorbelBars(f, k, s, p.Outro, p.Primeiro) : CorbelBars(f, k, s, null, true));
            }

            // ---------------- Alças de içamento ----------------
            // Numa face, simétricas ao CG ao longo do eixo e perto de 20 % das pontas, pela mesma função do CAD (D30): o pilar
            // é içado e transportado deitado, com a face das alças para cima. A face é escolhida em LiftFace (consolos para cima).
            if (s.LiftLoops > 0)
                bars.AddRange(LiftLoops(f, corbels, s, dl, dt));
            return bars;
        }

        /// <summary>
        /// Face das alças. Com consolos em até 3 faces: a face com mais consolos (desempate: consolos maiores, depois a
        /// face mais estreita), para os consolos ficarem para cima no transporte. Com consolos nas 4 faces: a face oposta
        /// ao consolo mais armado (maior), e no empate a face mais estreita. "Alça no maior lado" = alça na face menor:
        /// o pilar deitado fica com o lado maior na vertical (maior inércia no sentido do maior momento do transporte).
        /// Regra combinada com o usuário em 06/11/2026 (ver docs/notas/pendencias_escopo.md).
        /// </summary>
        public static ColumnFace LiftFace(ColumnBox f, IList<Corbel> corbels)
        {
            var faces = new[] { ColumnFace.PX, ColumnFace.NX, ColumnFace.PY, ColumnFace.NY };
            double Width(ColumnFace fc) => fc == ColumnFace.PX || fc == ColumnFace.NX ? f.By : f.Bx;
            double Size(Corbel k) => k.Width * k.HeightAtFace * k.Projection;
            ColumnFace Opposite(ColumnFace fc) => fc switch
            {
                ColumnFace.PX => ColumnFace.NX, ColumnFace.NX => ColumnFace.PX,
                ColumnFace.PY => ColumnFace.NY, _ => ColumnFace.PY
            };
            var used = corbels.Select(k => k.Face).Distinct().ToList();
            if (used.Count == 4)
            {
                Corbel big = corbels.OrderByDescending(Size).First();
                double max = Size(big);
                // consolos "mais armados" empatados: entre as faces opostas a eles, a mais estreita
                return corbels.Where(k => Size(k) > max * 0.999).Select(k => Opposite(k.Face)).Distinct()
                    .OrderBy(Width).First();
            }
            if (used.Count == 0) return faces.OrderBy(Width).First();
            return used.OrderByDescending(fc => corbels.Count(k => k.Face == fc))
                .ThenByDescending(fc => corbels.Where(k => k.Face == fc).Sum(Size))
                .ThenBy(Width).First();
        }

        /// <summary>
        /// Alturas das alças (pés, da base): simétricas ao CG do pilar com os consolos, a (50 − LiftEndPercent) % do comprimento
        /// dele — TemplatePilarPreMoldado.PosicaoAlcas sobre TemplatePilarPreMoldado.AlturaCG, as funções do CAD (D30, item 5).
        /// </summary>
        public static (double Z1, double Z2) LiftHeights(ColumnBox f, IList<Corbel> corbels, ColumnRebarSpec s)
        {
            double Cm(double ft) => U_.ToMm(ft) / 10;
            double l = Cm(f.H);
            // consolo: volume largura × balanço × média das alturas, centro na metade da altura junto à face (como o CAD)
            var cons = corbels.Select(k => (Cm(k.Width) * Cm(k.Projection) * (Cm(k.HeightAtFace) + Cm(k.TipHeight)) / 2,
                                            Cm(k.ZTop) - Cm(k.HeightAtFace) / 2));
            double cg = TemplatePilarPreMoldado.AlturaCG(Cm(f.Bx) * Cm(f.By) * l, l, cons);
            var (z1, z2) = TemplatePilarPreMoldado.PosicaoAlcas(l, cg, s.LiftEndPercent);
            return (U_.Mm(10 * z1), U_.Mm(10 * z2));
        }

        /// <summary>
        /// Alças como as do CAD (D30, item 5): no meio da face, no plano U × Z; as pernas entram no pilar até por dentro dos
        /// estribos da face oposta (TemplatePilarPreMoldado.EmbutimentoAlca) e dobram lá no pé, para fora; o topo fica LiftProtrusionMm
        /// acima da face. Se cair num consolo da face, a alça é deslocada o mínimo (o CAD só avisa).
        /// </summary>
        private static IEnumerable<BarDef> LiftLoops(ColumnBox f, IList<Corbel> corbels, ColumnRebarSpec s, double dl, double dt)
        {
            ColumnFace face = LiftFace(f, corbels);
            var g = new Corbel { Face = face };            // só para os eixos da face (U, T, At)
            double width = face == ColumnFace.PX || face == ColumnFace.NX ? f.By : f.Bx;
            double depth = g.ColumnDepth(f);
            double dLift = U_.Mm(s.LiftDiameterMm), w = U_.Mm(s.LiftWidthMm), pr = U_.Mm(s.LiftProtrusionMm);
            double foot = Math.Max(0, U_.Mm(s.LiftFootMm));
            double emb = U_.Mm(10 * TemplatePilarPreMoldado.EmbutimentoAlca(U_.ToMm(depth) / 10, s.CoverMm / 10, s.StirrupDiameterMm, s.LiftEmbedMm / 10));
            double dIn = -(emb - dLift / 2);               // eixo do pé (o lado de fora dele por dentro dos estribos da face oposta)
            double margin = U_.Mm(100);
            var busy = corbels.Where(k => k.Face == face)
                .Select(k => (A: k.ZBottom - margin, B: k.ZTop + U_.Mm(s.CorbelPinAboveMm) + margin)).ToList();
            double half = w / 2 + foot;
            double Free(double z)
            {
                // desloca para longe do consolo, o mínimo necessário (para cima ou para baixo)
                for (int it = 0; it < 4; it++)
                {
                    var hit = busy.FirstOrDefault(r => z + half > r.A && z - half < r.B);
                    if (hit == default) break;
                    double down = hit.A - half, up = hit.B + half;
                    z = Math.Abs(z - down) <= Math.Abs(up - z) && down - half > 0 ? down : up;
                }
                return Math.Min(Math.Max(z, half + U_.Mm(50)), f.H - half - U_.Mm(50));
            }
            double sp = U_.Mm(s.LiftSpacingMm);
            IEnumerable<double> ss = s.LiftLoops == 1 ? new[] { width / 2 } : new[] { width / 2 - sp / 2, width / 2 + sp / 2 };
            var (h1, h2) = LiftHeights(f, corbels, s);
            foreach (double z0 in new[] { h1, h2 })
            {
                double z = Free(z0);
                foreach (double t in ss)
                {
                    var pts = new List<Loc>();
                    if (foot > 0) pts.Add(g.At(f, dIn, t, z - w / 2 - foot));
                    pts.Add(g.At(f, dIn, t, z - w / 2));
                    pts.Add(g.At(f, pr, t, z - w / 2));
                    pts.Add(g.At(f, pr, t, z + w / 2));
                    pts.Add(g.At(f, dIn, t, z + w / 2));
                    if (foot > 0) pts.Add(g.At(f, dIn, t, z + w / 2 + foot));
                    yield return new BarDef { Role = BarRole.LiftLoop, DiameterMm = s.LiftDiameterMm, Points = pts, Normal = g.T };
                }
            }
        }

        private static BarDef Tie(Loc a, Loc b, ColumnRebarSpec s, int n, double sp) => new BarDef
        {
            Role = BarRole.CrossTie,
            DiameterMm = s.StirrupDiameterMm,
            Points = { a, b },
            Normal = Loc.UnitC,
            Hooks135 = true,
            Count = n,
            Spacing = sp
        };

        /// <summary>
        /// Barras do consolo como o quadro "ARMADURAS console" do escritório: tirante (perna de ancoragem dentro do pilar;
        /// na ponta, barra soldada ou perna para baixo), estribos horizontais de costura em 2/3 d abaixo do tirante,
        /// estribos verticais, armadura inferior em U e pinos em L.
        /// </summary>
        /// <summary>
        /// Pares de consolos opostos em nível (índices em <paramref name="corbels"/>), pela regra do núcleo comum ao CAD
        /// (<see cref="BarrasConsolo.ParesEmNivel"/>, tolerância de 0,6 cm). A armadura é a mesma (uma especificação por pilar).
        /// </summary>
        public static List<(int A, int B)> CorbelPairs(IList<Corbel> corbels)
        {
            double Cm(double ft) => U_.ToMm(ft) / 10;
            int Face(ColumnFace fc) => fc == ColumnFace.PX ? 1 : fc == ColumnFace.PY ? 2 : fc == ColumnFace.NX ? 3 : 4;
            return BarrasConsolo.ParesEmNivel(corbels.Select(k => new ConsoloNoNivel(Face(k.Face), Cm(k.ZTop), Cm(k.HeightAtFace),
                Cm((k.S0 + k.S1) / 2), Cm(k.Width), Cm(k.Projection), Cm(k.TipHeight))).ToList());
        }

        /// <param name="par">consolo oposto em nível (duplo) ou null</param>
        /// <param name="primeiro">no duplo, este consolo gera o tirante e a costura passantes; o outro não gera os dele</param>
        private static IEnumerable<BarDef> CorbelBars(ColumnBox f, Corbel k, ColumnRebarSpec s, Corbel par, bool primeiro)
        {
            double cover = U_.Mm(s.CoverMm);
            double dTie = U_.Mm(s.TieDiameterMm), dSew = U_.Mm(s.SewDiameterMm);
            double dl = U_.Mm(s.LongDiameterMm), dt = U_.Mm(s.StirrupDiameterMm);
            double depth = k.ColumnDepth(f);

            // Dentro do pilar: a barra para por dentro das longitudinais da face oposta — regra do núcleo, a mesma do desenho do
            // CAD (BarrasConsolo, D30 item 3). fim = face externa da barra, medida da face do consolo.
            double fim = U_.Mm(10 * BarrasConsolo.FimNoPilar(U_.ToMm(depth) / 10, s.CoverMm / 10, s.StirrupDiameterMm, s.LongDiameterMm));
            double dIn = -(fim - dTie / 2);
            double dTip = k.Projection - cover - dTie / 2;
            double zTie = k.ZTop - cover - dTie / 2;
            double tipLeg = Math.Max(k.TipHeight - 2 * cover, 10 * dTie);
            // perna de ancoragem: medida EXTERNA (do topo do tirante à ponta), como no CAD (D7) — o eixo da dobra fica φ/2 abaixo
            double innerLeg = U_.Mm(s.TieInnerLegMm) - dTie / 2;

            int n = Math.Max(1, s.TieCount);
            double s0 = k.S0 + cover + dTie / 2, s1 = k.S1 - cover - dTie / 2;
            double step = n > 1 ? (s1 - s0) / (n - 1) : 0;
            double sStart = n > 1 ? s0 : (k.S0 + k.S1) / 2;

            // duplo: a outra ponta é a do consolo oposto (d negativo = atravessando o pilar)
            double dOutro = par == null ? 0 : -(depth + par.Projection - cover);
            var tie = new BarDef { Role = BarRole.CorbelTie, DiameterMm = s.TieDiameterMm, Normal = k.T, Count = n, Spacing = step };
            if (par == null)
            {
                tie.Points.Add(k.At(f, dIn, sStart, zTie - innerLeg));
                tie.Points.Add(k.At(f, dIn, sStart, zTie));
            }
            else
            {
                // passante (D30): de ponta a ponta; com a perna na ponta, ela desce também do outro lado
                double tipLegOutro = Math.Max(par.TipHeight - 2 * cover, 10 * dTie);
                if (!s.TieWeldedBar) tie.Points.Add(k.At(f, dOutro + dTie / 2, sStart, zTie - tipLegOutro));
                tie.Points.Add(k.At(f, s.TieWeldedBar ? dOutro : dOutro + dTie / 2, sStart, zTie));
            }
            // com a barra soldada a ponta é livre e vai até o cobrimento, como no CAD; com a perna na ponta, o eixo da perna
            // fica a cobrimento + φ/2
            tie.Points.Add(k.At(f, s.TieWeldedBar ? k.Projection - cover : dTip, sStart, zTie));
            if (!s.TieWeldedBar) tie.Points.Add(k.At(f, dTip, sStart, zTie - tipLeg));
            if (par == null || primeiro) yield return tie;

            // Barra de ancoragem soldada: transversal, na ponta, logo abaixo do tirante.
            if (s.TieWeldedBar)
                yield return new BarDef
                {
                    Role = BarRole.CorbelAnchor,
                    DiameterMm = s.TieDiameterMm,
                    Points = { k.At(f, dTip, k.S0 + cover, zTie - dTie), k.At(f, dTip, k.S1 - cover, zTie - dTie) },
                    Normal = Loc.UnitC
                };

            // Estribos verticais: fechados no plano transversal (T × Z), do tirante até o fundo do consolo na ponta,
            // repetidos ao longo do balanço.
            double dV = U_.Mm(s.CorbelStirrupDiameterMm);
            double zBottomTip = k.ZTop - k.TipHeight;           // fundo do consolo na ponta (vale também se trapezoidal)
            double zvTop = zTie - dTie / 2 - dV / 2, zvBot = zBottomTip + cover + dV / 2;
            if (s.CorbelStirrupCount > 0 && zvTop - zvBot > 4 * dV)
            {
                int nv = s.CorbelStirrupCount;
                double d0 = cover + dV / 2, d1 = k.Projection - cover - dV / 2;
                double va = k.S0 + cover + dV / 2, vb = k.S1 - cover - dV / 2;
                yield return new BarDef
                {
                    Role = BarRole.CorbelStirrup,
                    DiameterMm = s.CorbelStirrupDiameterMm,
                    Points =
                    {
                        k.At(f, d0, va, zvBot),
                        k.At(f, d0, vb, zvBot),
                        k.At(f, d0, vb, zvTop),
                        k.At(f, d0, va, zvTop)
                    },
                    Closed = true,
                    Normal = k.U,
                    Hooks135 = true,
                    Count = nv,
                    Spacing = nv > 1 ? (d1 - d0) / (nv - 1) : 0
                };
            }

            // Armadura inferior: U com as pernas para cima, do pilar até a ponta, acima do fundo dos estribos verticais.
            double dB = U_.Mm(s.CorbelBottomDiameterMm);
            if (s.CorbelBottomCount > 0)
            {
                int nb = s.CorbelBottomCount;
                double zb = zvBot + dV / 2 + dB / 2;
                double leg = Math.Max(10 * dB, 0.75 * (k.TipHeight - 2 * cover));
                double dInB = -(depth - cover - dt - dl - dB / 2), dTipB = k.Projection - cover - dV - dB / 2;
                double ba = k.S0 + cover + dV + dB / 2, bb = k.S1 - cover - dV - dB / 2;
                yield return new BarDef
                {
                    Role = BarRole.CorbelBottom,
                    DiameterMm = s.CorbelBottomDiameterMm,
                    Points =
                    {
                        k.At(f, dInB, nb > 1 ? ba : (ba + bb) / 2, zb + leg),
                        k.At(f, dInB, nb > 1 ? ba : (ba + bb) / 2, zb),
                        k.At(f, dTipB, nb > 1 ? ba : (ba + bb) / 2, zb),
                        k.At(f, dTipB, nb > 1 ? ba : (ba + bb) / 2, zb + leg)
                    },
                    Normal = k.T,
                    Count = nb,
                    Spacing = nb > 1 ? (bb - ba) / (nb - 1) : 0
                };
            }

            // Pinos em L no meio do balanço (centro da almofada): acima do topo, embutidos, pé voltado para o pilar.
            if (s.CorbelPinCount > 0)
            {
                int np = s.CorbelPinCount;
                double dPin = k.Projection / 2;
                double above = U_.Mm(s.CorbelPinAboveMm), emb = U_.Mm(s.CorbelPinEmbedMm), foot = U_.Mm(s.CorbelPinFootMm);
                // embutido e pé são medidas EXTERNAS, como no CAD (convenção do DEB.Cad, D7): o eixo da dobra fica φ/2 para dentro
                double r = foot > 0 ? U_.Mm(s.CorbelPinDiameterMm) / 2 : 0;
                double pa = np > 1 ? k.S0 + k.Width / 4 : (k.S0 + k.S1) / 2;
                double pb = np > 1 ? k.S1 - k.Width / 4 : pa;
                yield return new BarDef
                {
                    Role = BarRole.CorbelPin,
                    DiameterMm = s.CorbelPinDiameterMm,
                    Points =
                    {
                        k.At(f, dPin, pa, k.ZTop + above),
                        k.At(f, dPin, pa, k.ZTop - emb + r),
                        k.At(f, dPin + r - foot, pa, k.ZTop - emb + r)
                    },
                    Normal = k.T,
                    Count = np,
                    Spacing = np > 1 ? (pb - pa) / (np - 1) : 0
                };
            }

            // Costura: estribos horizontais fechados, abaixo do tirante, em 2/3 d (no duplo, passante, gerada pelo primeiro).
            int m = Math.Max(0, s.SewCount);
            if (m == 0 || (par != null && !primeiro)) yield break;
            double d = k.HeightAtFace - cover;
            double zone = 2.0 / 3.0 * d;
            double sp = zone / m;
            double zFirst = zTie - dTie / 2 - dSew / 2 - sp;
            double sa = k.S0 + cover + dSew / 2, sb = k.S1 - cover - dSew / 2;
            double dInSew = par == null ? -(fim - dSew / 2) : dOutro + dSew / 2;
            double dTipSew = k.Projection - cover - dSew / 2;

            yield return new BarDef
            {
                Role = BarRole.CorbelSew,
                DiameterMm = s.SewDiameterMm,
                Points =
                {
                    k.At(f, dInSew, sa, zFirst),
                    k.At(f, dTipSew, sa, zFirst),
                    k.At(f, dTipSew, sb, zFirst),
                    k.At(f, dInSew, sb, zFirst)
                },
                Closed = true,
                Normal = -Loc.UnitC,
                Hooks135 = true,
                Count = m,
                Spacing = sp
            };
        }

        /// <summary>
        /// Divide a altura em trechos de espaçamento constante (o menor espaçamento vale onde as zonas se sobrepõem).
        /// Devolve (cota do primeiro estribo, quantidade, passo). Estribos não se repetem nas divisas.
        /// </summary>
        public static List<(double Z, int Count, double Spacing)> StirrupRuns(ColumnBox f, IList<Corbel> corbels, ColumnRebarSpec s)
        {
            double z0 = U_.Mm(s.StirrupEndOffsetMm), z1 = f.H - U_.Mm(s.StirrupEndOffsetMm);
            var zones = new List<(double A, double B, double S)>();
            if (s.BaseZoneLengthMm > 0) zones.Add((0, U_.Mm(s.BaseZoneLengthMm), U_.Mm(s.BaseZoneSpacingMm)));
            if (s.TopZoneLengthMm > 0) zones.Add((f.H - U_.Mm(s.TopZoneLengthMm), f.H, U_.Mm(s.TopZoneSpacingMm)));
            double ext = U_.Mm(s.CorbelZoneExtensionMm);
            foreach (Corbel k in corbels)
                zones.Add((k.ZBottom - ext, k.ZTop + ext, U_.Mm(s.CorbelZoneSpacingMm)));

            double sDefault = U_.Mm(s.StirrupSpacingMm);
            var cuts = new SortedSet<double> { z0, z1 };
            foreach (var (a, b, _) in zones)
            {
                if (a > z0 && a < z1) cuts.Add(a);
                if (b > z0 && b < z1) cuts.Add(b);
            }

            // Trechos com o espaçamento aplicável; une trechos vizinhos de mesmo espaçamento.
            var spans = new List<(double A, double B, double S)>();
            double[] c = cuts.ToArray();
            for (int i = 0; i < c.Length - 1; i++)
            {
                double mid = (c[i] + c[i + 1]) / 2;
                double sp = zones.Where(zn => mid > zn.A && mid < zn.B).Select(zn => zn.S).DefaultIfEmpty(sDefault).Min();
                sp = Math.Min(sp, sDefault);
                if (spans.Count > 0 && Math.Abs(spans[spans.Count - 1].S - sp) < 1e-9) spans[spans.Count - 1] = (spans[spans.Count - 1].A, c[i + 1], sp);
                else spans.Add((c[i], c[i + 1], sp));
            }

            // Espaçamento nominal exato (c/10, c/15...): a sobra fica no fim de cada trecho. Quantidade pela regra do núcleo,
            // a mesma das faixas do CAD (TemplatePilarPreMoldado.EstribosNaZona, ⌊L/s⌋ + 1 da configuração padrão): trecho
            // intermediário não repete o estribo da divisa (ela pertence ao trecho seguinte); o último inclui a ponta.
            var cfg = new Configuracao.ConfiguracaoArmadura();
            var runs = new List<(double, int, double)>();
            for (int i = 0; i < spans.Count; i++)
            {
                var (a, b, sp) = spans[i];
                if (sp <= 0) continue;
                bool last = i == spans.Count - 1;
                int count = Math.Max(1, TemplatePilarPreMoldado.EstribosNaZona(U_.ToMm(b - a) / 10, U_.ToMm(sp) / 10, last, cfg));
                runs.Add((a, count, sp));
            }
            return runs;
        }

        /// <summary>
        /// Centros das longitudinais na seção (pés), por <see cref="Motor.SecaoBarras.Posicoes"/>: cantos e barras das faces
        /// divididas por igual, a cobrimento + φt + φ/2 das faces. Na mesma ordem de antes da troca (o conjunto do Revit nasce
        /// das duas primeiras): faces paralelas a X por x e de baixo para cima; depois as das faces paralelas a Y.
        /// </summary>
        public static List<(double A, double B)> LongitudinalPositions(ColumnBox f, ColumnRebarSpec s)
        {
            int nx = Math.Max(2, s.LongBarsAlongX), ny = Math.Max(2, s.LongBarsAlongY);
            double[] cob = Enumerable.Repeat(s.CoverMm / 10, 4).ToArray();
            var sec = Motor.SecaoBarras.Posicoes(U_.ToMm(f.Bx) / 10, U_.ToMm(f.By) / 10, cob, s.StirrupDiameterMm,
                s.LongDiameterMm, 1, 0, s.LongDiameterMm, nx - 2, ny - 2, s.LongDiameterMm);
            var faceX = sec.Where(b => b.Grupo == "canto" || b.Grupo == "faceB").OrderBy(b => b.X).ThenBy(b => b.Y);
            var faceY = sec.Where(b => b.Grupo == "faceH").OrderBy(b => b.Y).ThenBy(b => b.X);
            return faceX.Concat(faceY).Select(b => (U_.Mm(b.X * 10), U_.Mm(b.Y * 10))).ToList();
        }

        private static List<double> Linspace(double a, double b, int n) =>
            n <= 1 ? new List<double> { (a + b) / 2 } : Enumerable.Range(0, n).Select(i => a + (b - a) * i / (n - 1)).ToList();
    }
}
