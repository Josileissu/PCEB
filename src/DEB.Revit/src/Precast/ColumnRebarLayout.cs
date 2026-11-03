using System;
using System.Collections.Generic;
using System.Linq;

namespace StructuralDetailer.Precast
{
    internal enum BarRole { Longitudinal, Stirrup, CrossTie, CorbelTie, CorbelSew, LiftLoop }

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
            if (s.LiftLoops > 0)
            {
                double w = U_.Mm(s.LiftWidthMm) / 2, emb = U_.Mm(s.LiftEmbedMm), pr = U_.Mm(s.LiftProtrusionMm);
                double sp = U_.Mm(s.LiftSpacingMm);
                IEnumerable<double> bs = s.LiftLoops == 1 ? new[] { f.By / 2 } : new[] { f.By / 2 - sp / 2, f.By / 2 + sp / 2 };
                foreach (double b in bs)
                {
                    double a0 = f.Bx / 2 - w, a1 = f.Bx / 2 + w;
                    bars.Add(new BarDef
                    {
                        Role = BarRole.LiftLoop,
                        DiameterMm = s.LiftDiameterMm,
                        Points = { new Loc(a0, b, f.H - emb), new Loc(a0, b, f.H + pr), new Loc(a1, b, f.H + pr), new Loc(a1, b, f.H - emb) },
                        Normal = Loc.UnitB
                    });
                }
            }
            return bars;
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
        /// Tirante em "U" invertido (perna de ancoragem dentro do pilar + perna na ponta do consolo)
        /// e estribos horizontais de costura em 2/3 d abaixo do tirante.
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

            yield return new BarDef
            {
                Role = BarRole.CorbelTie,
                DiameterMm = s.TieDiameterMm,
                Points =
                {
                    k.At(f, dIn, sStart, zTie - innerLeg),
                    k.At(f, dIn, sStart, zTie),
                    k.At(f, dTip, sStart, zTie),
                    k.At(f, dTip, sStart, zTie - tipLeg)
                },
                Normal = k.T,
                Count = n,
                Spacing = step
            };

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
