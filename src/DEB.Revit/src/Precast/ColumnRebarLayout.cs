using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Revit.Precast
{
    internal enum BarRole { Longitudinal, Stirrup, CrossTie, CorbelTie, CorbelSew, LiftLoop,
                            CorbelAnchor, CorbelStirrup, CorbelBottom, CorbelPin }

    /// <summary>
    /// Uma barra (ou conjunto de barras iguais) em coordenadas locais do pilar.
    /// O conjunto se repete Count vezes, com passo Spacing, na direção de Normal.
    /// </summary>
    internal sealed class BarDef
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
    internal static class ColumnRebarLayout
    {
        public static List<BarDef> Compute(ColumnFrame f, IList<Corbel> corbels, ColumnRebarSpec s)
        {
            var bars = new List<BarDef>();
            double cover = U_.Mm(s.CoverMm);
            double dl = U_.Mm(s.LongDiameterMm), dt = U_.Mm(s.StirrupDiameterMm);
            double oT = cover + dt / 2;              // eixo do estribo
            double oL = cover + dt + dl / 2;         // eixo da barra longitudinal
            if (f.Bx <= 2 * oL || f.By <= 2 * oL) return bars;

            // ---------------- Longitudinais ----------------
            List<double> xs = Linspace(oL, f.Bx - oL, Math.Max(2, s.LongBarsAlongX));
            List<double> ys = Linspace(oL, f.By - oL, Math.Max(2, s.LongBarsAlongY));
            var pos = new List<(double A, double B)>();
            foreach (double x in xs) { pos.Add((x, ys.First())); pos.Add((x, ys.Last())); }
            foreach (double y in ys.Skip(1).Take(ys.Count - 2)) { pos.Add((xs.First(), y)); pos.Add((xs.Last(), y)); }

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
            if (s.CorbelRebar)
                foreach (Corbel k in corbels)
                    bars.AddRange(CorbelBars(f, k, s));

            // ---------------- Alças de içamento ----------------
            // Numa face, nos dois pontos da regra dos 20% (0,2 H e 0,8 H): o pilar é içado e transportado deitado, com a
            // face das alças para cima. A face é escolhida em LiftFace (consolos para cima).
            if (s.LiftLoops > 0)
                bars.AddRange(LiftLoops(f, corbels, s, dl, dt));
            return bars;
        }

        /// <summary>
        /// Face das alças. Com consolos em até 3 faces: a face com mais consolos (desempate: consolos maiores, depois o
        /// lado maior), para os consolos ficarem para cima no transporte. Com consolos nas 4 faces: a face oposta ao
        /// consolo mais armado (maior), e no empate o lado maior (maior inércia no sentido do maior momento).
        /// Regra combinada com o usuário em 06/11/2026 (ver docs/notas/pc2_pendencias.md).
        /// </summary>
        public static ColumnFace LiftFace(ColumnFrame f, IList<Corbel> corbels)
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
                // consolos "mais armados" empatados: entre as faces opostas a eles, a de lado maior
                return corbels.Where(k => Size(k) > max * 0.999).Select(k => Opposite(k.Face)).Distinct()
                    .OrderByDescending(Width).First();
            }
            if (used.Count == 0) return faces.OrderByDescending(Width).First();
            return used.OrderByDescending(fc => corbels.Count(k => k.Face == fc))
                .ThenByDescending(fc => corbels.Where(k => k.Face == fc).Sum(Size))
                .ThenByDescending(Width).First();
        }

        /// <summary>
        /// Alças em U saindo da face (plano U × Z, no meio da face): as pernas atravessam o cobrimento, dobram por dentro
        /// das longitudinais e seguem ao longo do pilar até completar o comprimento embutido. Fogem dos consolos da face.
        /// </summary>
        private static IEnumerable<BarDef> LiftLoops(ColumnFrame f, IList<Corbel> corbels, ColumnRebarSpec s, double dl, double dt)
        {
            ColumnFace face = LiftFace(f, corbels);
            var g = new Corbel { Face = face };            // só para os eixos da face (U, T, At)
            double width = face == ColumnFace.PX || face == ColumnFace.NX ? f.By : f.Bx;
            double dLift = U_.Mm(s.LiftDiameterMm), w = U_.Mm(s.LiftWidthMm), pr = U_.Mm(s.LiftProtrusionMm);
            double dIn = -(U_.Mm(s.CoverMm) + dt + dl + dLift / 2);
            double leg = Math.Max(10 * dLift, U_.Mm(s.LiftEmbedMm) - (pr - dIn));
            double margin = U_.Mm(100);
            var busy = corbels.Where(k => k.Face == face)
                .Select(k => (A: k.ZBottom - margin, B: k.ZTop + U_.Mm(s.CorbelPinAboveMm) + margin)).ToList();
            double half = w / 2 + leg;
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
            foreach (double z0 in new[] { 0.2 * f.H, 0.8 * f.H })
            {
                double z = Free(z0);
                foreach (double t in ss)
                    yield return new BarDef
                    {
                        Role = BarRole.LiftLoop,
                        DiameterMm = s.LiftDiameterMm,
                        Points =
                        {
                            g.At(f, dIn, t, z - w / 2 - leg),
                            g.At(f, dIn, t, z - w / 2),
                            g.At(f, pr, t, z - w / 2),
                            g.At(f, pr, t, z + w / 2),
                            g.At(f, dIn, t, z + w / 2),
                            g.At(f, dIn, t, z + w / 2 + leg)
                        },
                        Normal = g.T
                    };
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
        private static IEnumerable<BarDef> CorbelBars(ColumnFrame f, Corbel k, ColumnRebarSpec s)
        {
            double cover = U_.Mm(s.CoverMm);
            double dTie = U_.Mm(s.TieDiameterMm), dSew = U_.Mm(s.SewDiameterMm);
            double dl = U_.Mm(s.LongDiameterMm), dt = U_.Mm(s.StirrupDiameterMm);
            double depth = k.ColumnDepth(f);

            // Dentro do pilar: a barra vai até junto das longitudinais da face oposta.
            double dIn = -(depth - cover - dt - dl - dTie / 2);
            double dTip = k.Projection - cover - dTie / 2;
            double zTie = k.ZTop - cover - dTie / 2;
            double tipLeg = Math.Max(k.TipHeight - 2 * cover, 10 * dTie);
            double innerLeg = U_.Mm(s.TieInnerLegMm);

            int n = Math.Max(1, s.TieCount);
            double s0 = k.S0 + cover + dTie / 2, s1 = k.S1 - cover - dTie / 2;
            double step = n > 1 ? (s1 - s0) / (n - 1) : 0;
            double sStart = n > 1 ? s0 : (k.S0 + k.S1) / 2;

            var tie = new BarDef
            {
                Role = BarRole.CorbelTie,
                DiameterMm = s.TieDiameterMm,
                Points =
                {
                    k.At(f, dIn, sStart, zTie - innerLeg),
                    k.At(f, dIn, sStart, zTie),
                    k.At(f, dTip, sStart, zTie)
                },
                Normal = k.T,
                Count = n,
                Spacing = step
            };
            if (!s.TieWeldedBar) tie.Points.Add(k.At(f, dTip, sStart, zTie - tipLeg));
            yield return tie;

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
                double pa = np > 1 ? k.S0 + k.Width / 4 : (k.S0 + k.S1) / 2;
                double pb = np > 1 ? k.S1 - k.Width / 4 : pa;
                yield return new BarDef
                {
                    Role = BarRole.CorbelPin,
                    DiameterMm = s.CorbelPinDiameterMm,
                    Points =
                    {
                        k.At(f, dPin, pa, k.ZTop + above),
                        k.At(f, dPin, pa, k.ZTop - emb),
                        k.At(f, dPin - foot, pa, k.ZTop - emb)
                    },
                    Normal = k.T,
                    Count = np,
                    Spacing = np > 1 ? (pb - pa) / (np - 1) : 0
                };
            }

            // Costura: estribos horizontais fechados, abaixo do tirante, em 2/3 d.
            int m = Math.Max(0, s.SewCount);
            if (m == 0) yield break;
            double d = k.HeightAtFace - cover;
            double zone = 2.0 / 3.0 * d;
            double sp = zone / m;
            double zFirst = zTie - dTie / 2 - dSew / 2 - sp;
            double sa = k.S0 + cover + dSew / 2, sb = k.S1 - cover - dSew / 2;
            double dInSew = -(depth - cover - dt - dl - dSew / 2);
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
        public static List<(double Z, int Count, double Spacing)> StirrupRuns(ColumnFrame f, IList<Corbel> corbels, ColumnRebarSpec s)
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
                if (spans.Count > 0 && Math.Abs(spans[^1].S - sp) < 1e-9) spans[^1] = (spans[^1].A, c[i + 1], sp);
                else spans.Add((c[i], c[i + 1], sp));
            }

            // Espaçamento nominal exato (c/10, c/15...): a sobra fica no fim de cada trecho.
            var runs = new List<(double, int, double)>();
            for (int i = 0; i < spans.Count; i++)
            {
                var (a, b, sp) = spans[i];
                if (sp <= 0) continue;
                bool last = i == spans.Count - 1;
                int n = (int)Math.Floor((b - a) / sp + 1e-6);
                // Trecho intermediário: a divisa (b) pertence ao trecho seguinte. Último: inclui a ponta.
                int count = last ? n + 1 : Math.Max(1, (b - a) - n * sp < 1e-6 ? n : n + 1);
                runs.Add((a, count, sp));
            }
            return runs;
        }

        private static List<double> Linspace(double a, double b, int n) =>
            n <= 1 ? new List<double> { (a + b) / 2 } : Enumerable.Range(0, n).Select(i => a + (b - a) * i / (n - 1)).ToList();
    }
}
