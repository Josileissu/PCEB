using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace DEB.Revit.Core
{
    /// <summary>
    /// Armadura de pilar retangular segundo a NBR 6118:
    ///  • barras longitudinais nos cantos e intermediárias com espaçamento ≤ min(400 mm, 2 × menor lado);
    ///  • estribo fechado com ganchos de 135°, espaçamento ≤ min(200 mm, menor lado, 12 Ø long);
    ///  • grampos nas barras intermediárias a mais de 20 Ø estribo do canto.
    /// As barras são posicionadas no sistema local da peça (X, Y = seção; Z = eixo).
    /// </summary>
    internal sealed class ColumnRebarBuilder
    {
        public const string Tag = "StructuralDetailer";

        private readonly Document _doc;
        private readonly DetailingConfig _cfg;
        private readonly PieceLog _log;

        public ColumnRebarBuilder(Document doc, DetailingConfig cfg, PieceLog log)
        {
            _doc = doc; _cfg = cfg; _log = log;
        }

        /// <summary>Armaduras já hospedadas no pilar (para não duplicar e para incluir no assembly).</summary>
        public static List<ElementId> ExistingRebar(FamilyInstance fi)
        {
            RebarHostData host = RebarHostData.GetRebarHostData(fi);
            if (host == null) return new List<ElementId>();
            return host.GetRebarsInHost().Select(r => r.Id).ToList();
        }

        public List<ElementId> Build(PieceInfo p)
        {
            var created = new List<ElementId>();
            FamilyInstance fi = p.Instance;

            if (!RebarHostData.IsValidHost(fi))
            {
                _log.Warn("O pilar não aceita armadura: verifique se o material estrutural é concreto " +
                          "e se a família permite hospedar armadura.");
                return created;
            }

            double cover = U.Mm(_cfg.RebarCoverMm);
            double dl = U.Mm(_cfg.ColumnLongBarDiameterMm);
            double dt = U.Mm(_cfg.ColumnStirrupDiameterMm);
            double bx = p.LengthX, by = p.LengthY, h = p.LengthZ;
            double bMin = Math.Min(bx, by);

            double oT = cover + dt / 2.0;          // eixo do estribo
            double oL = cover + dt + dl / 2.0;     // eixo da barra longitudinal
            if (bx <= 2 * oL || by <= 2 * oL)
            {
                _log.Warn($"Seção {U.Cm(bx)} × {U.Cm(by)} cm pequena demais para o cobrimento e as bitolas configurados.");
                return created;
            }

            RebarBarType longType = GetBarType(_cfg.ColumnLongBarDiameterMm, _cfg.LongBarTypeNameFormat, 5, 5, 3);
            RebarBarType tieType = GetBarType(_cfg.ColumnStirrupDiameterMm, _cfg.StirrupBarTypeNameFormat, 3, 3, 3);
            RebarHookType hook135 = GetHook135();
            tieType.SetHookPermission(hook135.Id, true);

            XYZ P(double a, double b, double c) => p.Origin + p.AxisX * a + p.AxisY * b + p.AxisZ * c;

            // ---------------- Barras longitudinais ----------------
            double sLongMax = Math.Min(U.Mm(_cfg.ColumnLongBarMaxSpacingMm), 2 * bMin);
            List<double> xs = Distribute(oL, bx - oL, sLongMax);
            List<double> ys = Distribute(oL, by - oL, sLongMax);

            var positions = new List<(double X, double Y)>();
            foreach (double x in xs) { positions.Add((x, ys.First())); positions.Add((x, ys.Last())); }
            foreach (double y in ys.Skip(1).Take(ys.Count - 2)) { positions.Add((xs.First(), y)); positions.Add((xs.Last(), y)); }

            double zBot = cover, zTop = h - cover;
            foreach (var (x, y) in positions)
            {
                Line bar = Line.CreateBound(P(x, y, zBot), P(x, y, zTop));
                Rebar r = Rebar.CreateFromCurves(_doc, RebarStyle.Standard, longType, null, null, fi, p.AxisX,
                    new List<Curve> { bar }, RebarHookOrientation.Left, RebarHookOrientation.Left, true, true);
                Mark(r);
                created.Add(r.Id);
            }

            // ---------------- Estribos ----------------
            double sTie = Math.Min(Math.Min(U.Mm(_cfg.ColumnStirrupMaxSpacingMm), bMin), 12 * dl);
            sTie = Math.Floor(U.ToMm(sTie) / 5.0) * 5.0;   // múltiplo de 5 mm, como se especifica em obra
            double sTieInt = U.Mm(sTie);
            double z0 = U.Mm(_cfg.ColumnStirrupEndOffsetMm);
            double arrayLength = h - 2 * z0;

            // Retângulo no sentido anti-horário em torno de +Z: o interior fica à esquerda das barras,
            // então o gancho do início aponta para a direita e o do fim, para a esquerda (ambos para dentro).
            var loop = new List<Curve>
            {
                Line.CreateBound(P(oT, oT, z0), P(bx - oT, oT, z0)),
                Line.CreateBound(P(bx - oT, oT, z0), P(bx - oT, by - oT, z0)),
                Line.CreateBound(P(bx - oT, by - oT, z0), P(oT, by - oT, z0)),
                Line.CreateBound(P(oT, by - oT, z0), P(oT, oT, z0))
            };
            Rebar stirrup = Rebar.CreateFromCurves(_doc, RebarStyle.StirrupTie, tieType, hook135, hook135, fi, p.AxisZ,
                loop, RebarHookOrientation.Right, RebarHookOrientation.Left, true, true);
            SetLayout(stirrup, sTieInt, arrayLength);
            Mark(stirrup);
            created.Add(stirrup.Id);

            int nTies = stirrup.NumberOfBarPositions;
            string info = $"Armadura: {positions.Count} Ø{Fmt(_cfg.ColumnLongBarDiameterMm)} longitudinais; " +
                          $"estribos Ø{Fmt(_cfg.ColumnStirrupDiameterMm)} c/ {sTie:0} mm ({nTies} un.)";

            // ---------------- Grampos (NBR 6118 18.2.4) ----------------
            int nGrampos = 0;
            if (_cfg.ColumnCrossTies)
            {
                double protectedDist = 20 * dt;
                // Barras intermediárias das faces paralelas a X → grampo na direção Y, e vice-versa.
                foreach (double x in xs.Skip(1).Take(xs.Count - 2))
                {
                    if (Math.Min(x - xs.First(), xs.Last() - x) <= protectedDist + 1e-9) continue;
                    nGrampos += AddCrossTie(P(x, oT, z0), P(x, by - oT, z0), p.AxisZ, fi, tieType, hook135, sTieInt, arrayLength, created);
                }
                foreach (double y in ys.Skip(1).Take(ys.Count - 2))
                {
                    if (Math.Min(y - ys.First(), ys.Last() - y) <= protectedDist + 1e-9) continue;
                    nGrampos += AddCrossTie(P(oT, y, z0), P(bx - oT, y, z0), p.AxisZ, fi, tieType, hook135, sTieInt, arrayLength, created);
                }
            }
            if (nGrampos > 0) info += $"; {nGrampos} linha(s) de grampos";
            _log.Info(info + ".");

            return created;
        }

        // ------------------------------------------------------------------

        /// <summary>Grampo reto entre faces opostas, no plano da seção (normal = eixo do pilar), distribuído como o estribo.</summary>
        private int AddCrossTie(XYZ a, XYZ b, XYZ columnAxis, FamilyInstance fi, RebarBarType type, RebarHookType hook,
            double spacing, double arrayLength, List<ElementId> created)
        {
            try
            {
                Rebar r = Rebar.CreateFromCurves(_doc, RebarStyle.StirrupTie, type, hook, hook, fi, columnAxis,
                    new List<Curve> { Line.CreateBound(a, b) }, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                SetLayout(r, spacing, arrayLength);
                Mark(r);
                created.Add(r.Id);
                return 1;
            }
            catch (Exception ex)
            {
                _log.Warn("Grampo não criado: " + ex.Message);
                _log.Trace("Grampo", ex);
                return 0;
            }
        }

        private static void SetLayout(Rebar r, double spacing, double arrayLength)
        {
            if (arrayLength <= spacing) return;
            r.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(spacing, arrayLength, true, true, true);
        }

        /// <summary>Posições de min a max (inclusive) com espaçamento uniforme ≤ maxSpacing.</summary>
        private static List<double> Distribute(double min, double max, double maxSpacing)
        {
            int n = Math.Max(1, (int)Math.Ceiling((max - min) / maxSpacing - 1e-9));
            return Enumerable.Range(0, n + 1).Select(i => min + (max - min) * i / n).ToList();
        }

        private static void Mark(Rebar r) =>
            r.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(Tag);

        private static string Fmt(double mm) => mm.ToString("0.#", CultureInfo.InvariantCulture);

        /// <summary>
        /// Usa um tipo de barra do projeto com a bitola pedida; se não houver, cria um.
        /// Diâmetros de dobra em múltiplos da bitola (NBR 6118 tabelas 9.1 e 9.2).
        /// </summary>
        private RebarBarType GetBarType(double diameterMm, string nameFormat, double bendFactor, double hookFactor, double tieFactor)
        {
            double d = U.Mm(diameterMm);
            RebarBarType found = new FilteredElementCollector(_doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
                .Where(t => Math.Abs(t.BarNominalDiameter - d) < U.Mm(0.05))
                .OrderBy(t => t.Name)
                .FirstOrDefault();
            if (found != null) return found;

            RebarBarType bt = RebarBarType.Create(_doc);
            bt.Name = new NameRegistry(new FilteredElementCollector(_doc).OfClass(typeof(RebarBarType)).Select(e => e.Name))
                .Unique(string.Format(CultureInfo.InvariantCulture, nameFormat, Fmt(diameterMm)));
            // Os diâmetros são validados em conjunto (dobra > bitola): define todos de uma vez.
            bt.SetBarTypeDiameters(new BarTypeDiameterOptions
            {
                BarNominalDiameter = d,
                BarModelDiameter = d,
                StandardBendDiameter = bendFactor * d,
                StandardHookBendDiameter = hookFactor * d,
                StirrupTieBendDiameter = tieFactor * d
            });
            _log.Info($"Tipo de barra \"{bt.Name}\" criado no projeto.");
            return bt;
        }

        /// <summary>Gancho de 135° para estribos (ponta reta de 10 Ø ≥ 5 Ø e ≥ 50 mm para Ø ≥ 5 mm).</summary>
        private RebarHookType GetHook135()
        {
            const double angle = 3 * Math.PI / 4;
            List<RebarHookType> hooks = new FilteredElementCollector(_doc).OfClass(typeof(RebarHookType))
                .Cast<RebarHookType>()
                .Where(t => Math.Abs(t.HookAngle - angle) < 1e-3)
                .ToList();
            RebarHookType found = hooks.FirstOrDefault(t => t.Style == RebarStyle.StirrupTie) ?? hooks.FirstOrDefault();
            if (found != null) return found;

            RebarHookType ht = RebarHookType.Create(_doc, angle, 10);
            ht.Style = RebarStyle.StirrupTie;
            ht.Name = "Estribo 135°";
            _log.Info("Tipo de gancho \"Estribo 135°\" criado no projeto.");
            return ht;
        }
    }
}
