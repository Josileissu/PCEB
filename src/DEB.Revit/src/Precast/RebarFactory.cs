using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DEB.Revit.Core;

namespace DEB.Revit.Precast
{
    /// <summary>Cria no Revit as barras calculadas por <see cref="ColumnRebarLayout"/>.</summary>
    internal sealed class RebarFactory
    {
        public const string Tag = "StructuralDetailer";

        private readonly Document _doc;
        private readonly PieceLog _log;
        private readonly Dictionary<double, RebarBarType> _types = new Dictionary<double, RebarBarType>();
        private RebarHookType _hook135;

        public RebarFactory(Document doc, PieceLog log)
        {
            _doc = doc; _log = log;
        }

        public List<ElementId> Create(ColumnFrame f, FamilyInstance column, IEnumerable<BarDef> bars, string mark)
        {
            var ids = new List<ElementId>();
            var created = new List<(BarDef Def, Rebar Bar)>();
            foreach (BarDef b in bars)
            {
                try
                {
                    Rebar r = CreateOne(f, column, b, mark);
                    ids.Add(r.Id);
                    created.Add((b, r));
                }
                catch (Exception ex)
                {
                    _log.Warn($"{RoleLabel(b.Role)} Ø{Fmt(b.DiameterMm)} não criada: {ex.Message}");
                    _log.Trace(RoleLabel(b.Role), ex);
                }
            }
            // o comprimento do Revit só existe depois de regenerar
            try { _doc.Regenerate(); foreach (var (def, bar) in created) LogLengthComparison(def, bar); }
            catch (Exception ex) { _log.Trace("Comprimento pelo núcleo", ex); }
            return ids;
        }

        /// <summary>
        /// Etapa 5.3c-1: registra o comprimento do Revit ao lado do comprimento real do núcleo (regras do CAD),
        /// para conferência antes de o núcleo passar a valer nas tabelas e pesos. Não altera nada no modelo.
        /// </summary>
        private void LogLengthComparison(BarDef b, Rebar r)
        {
            try
            {
                const double cmPerFoot = 30.48;
                var axis = b.Points.Select(p => (p.A * cmPerFoot, p.B * cmPerFoot, p.C * cmPerFoot)).ToList();
                bool tie = IsTie(b.Role);
                double core = SharedRules.RealLengthCm(axis, b.Closed, b.DiameterMm, tie, b.Hooks135);
                double revit = (r.get_Parameter(BuiltInParameter.REBAR_ELEM_LENGTH)?.AsDouble() ?? 0) * cmPerFoot;
                _log.Info($"Comprimento {RoleLabel(b.Role)} Ø{Fmt(b.DiameterMm)}: Revit {revit.ToString("0.0", CultureInfo.InvariantCulture)} cm | núcleo {core.ToString("0.0", CultureInfo.InvariantCulture)} cm | diferença {(core - revit).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture)}");
            }
            catch (Exception ex) { _log.Trace("Comprimento pelo núcleo", ex); }
        }

        private Rebar CreateOne(ColumnFrame f, FamilyInstance column, BarDef b, string mark)
        {
            List<Loc> pts = b.Points;
            if (b.Closed && SignedArea(pts, b.Normal) < 0) pts = Enumerable.Reverse(pts).ToList();

            var curves = new List<Curve>();
            for (int i = 0; i < pts.Count - 1; i++) curves.Add(Line.CreateBound(f.ToWorld(pts[i]), f.ToWorld(pts[i + 1])));
            if (b.Closed) curves.Add(Line.CreateBound(f.ToWorld(pts[^1]), f.ToWorld(pts[0])));

            bool tie = IsTie(b.Role);
            RebarHookType hook = b.Hooks135 ? Hook135() : null;
            Element host = (Element)b.HostCorbel?.Instance ?? column;

            // Laço anti-horário em torno da normal: o interior fica à esquerda da barra, então o gancho
            // do início aponta para a direita e o do fim para a esquerda (ambos para dentro).
            Rebar r = Rebar.CreateFromCurves(_doc, tie ? RebarStyle.StirrupTie : RebarStyle.Standard,
                BarType(b.DiameterMm), hook, hook, host, f.DirToWorld(b.Normal), curves,
                RebarHookOrientation.Right, RebarHookOrientation.Left, true, true);

            if (b.Count > 1 && b.Spacing > 1e-9)
                r.GetShapeDrivenAccessor().SetLayoutAsNumberWithSpacing(b.Count, b.Spacing, true, true, true);

            r.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"{Tag}|{mark}|{RoleLabel(b.Role)}");
            return r;
        }

        /// <summary>Área com sinal do polígono projetado no plano da normal (positivo = anti-horário).</summary>
        private static double SignedArea(List<Loc> pts, Loc n)
        {
            Loc sum = new Loc(0, 0, 0);
            for (int i = 0; i < pts.Count; i++) sum = sum + pts[i].Cross(pts[(i + 1) % pts.Count]);
            return sum.Dot(n);
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Tipo de barra com a bitola pedida; cria se não existir.
        /// Ø &lt; 6,3 → CA-60; senão CA-50. Dobras pela NBR 6118 (tabelas 9.1 e 9.2).
        /// </summary>
        public RebarBarType BarType(double diameterMm)
        {
            if (_types.TryGetValue(diameterMm, out RebarBarType cached)) return cached;

            double d = U.Mm(diameterMm);
            // Só aceita tipo do aço certo (nome com CA-50/CA-60): o template do Revit traz tipos "CA25" com pinos de
            // CA25, que davam aço errado na tabela e comprimentos diferentes do núcleo. Sem tipo certo, cria o do PCEB.
            string want = SharedRules.SteelFor(diameterMm).Replace("-", "");
            RebarBarType found = new FilteredElementCollector(_doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
                .Where(t => Math.Abs(t.BarNominalDiameter - d) < U.Mm(0.05))
                .Where(t => RebarDataSteel(t) == want)
                .OrderBy(t => t.Name)
                .FirstOrDefault();
            if (found == null)
            {
                bool ca60 = diameterMm < 6.3;
                // pinos de dobramento do núcleo comum (NBR 6118, tabelas 9.1 e 9.2)
                string steel = SharedRules.SteelFor(diameterMm);
                double bend = SharedRules.BendDiameterMm(diameterMm, steel, false) / diameterMm;
                double stirrupBend = SharedRules.BendDiameterMm(diameterMm, steel, true) / diameterMm;

                found = RebarBarType.Create(_doc);
                found.Name = new NameRegistry(new FilteredElementCollector(_doc).OfClass(typeof(RebarBarType)).Select(e => e.Name))
                    .Unique($"Ø{Fmt(diameterMm)} {(ca60 ? "CA-60" : "CA-50")}");
                found.SetBarTypeDiameters(new BarTypeDiameterOptions
                {
                    BarNominalDiameter = d,
                    BarModelDiameter = d,
                    StandardBendDiameter = bend * d,
                    StandardHookBendDiameter = bend * d,
                    StirrupTieBendDiameter = stirrupBend * d
                });
                _log.Info($"Tipo de barra \"{found.Name}\" criado no projeto.");
            }

            found.SetHookPermission(Hook135().Id, true);
            _types[diameterMm] = found;
            return found;
        }

        /// <summary>Aço indicado no nome do tipo ("CA50", "CA60", "CA25") ou "" se o nome não indica.</summary>
        private static string RebarDataSteel(RebarBarType t)
        {
            string n = (t?.Name ?? "").ToUpperInvariant().Replace("-", "").Replace(" ", "");
            return n.Contains("CA60") ? "CA60" : n.Contains("CA50") ? "CA50" : n.Contains("CA25") ? "CA25" : "";
        }

        /// <summary>Gancho de 135° (ponta reta 10 Ø ≥ 5 cm, NBR 6118 9.4.6.1).</summary>
        private RebarHookType Hook135()
        {
            if (_hook135 != null) return _hook135;
            const double angle = 3 * Math.PI / 4;
            List<RebarHookType> hooks = new FilteredElementCollector(_doc).OfClass(typeof(RebarHookType))
                .Cast<RebarHookType>().Where(t => Math.Abs(t.HookAngle - angle) < 1e-3).ToList();
            _hook135 = hooks.FirstOrDefault(t => t.Style == RebarStyle.StirrupTie) ?? hooks.FirstOrDefault();
            if (_hook135 == null)
            {
                _hook135 = RebarHookType.Create(_doc, angle, 10);
                _hook135.Style = RebarStyle.StirrupTie;
                _hook135.Name = "Estribo 135°";
            }
            return _hook135;
        }

        /// <summary>Estribos e grampos (estilo de estribo do Revit; pinos da tabela 9.2 da NBR 6118).</summary>
        private static bool IsTie(BarRole r) => r == BarRole.Stirrup || r == BarRole.CrossTie || r == BarRole.CorbelSew || r == BarRole.CorbelStirrup;

        public static string RoleLabel(BarRole r) => r switch
        {
            BarRole.Longitudinal => "Longitudinal",
            BarRole.Stirrup => "Estribo",
            BarRole.CrossTie => "Grampo",
            BarRole.CorbelTie => "Tirante do consolo",
            BarRole.CorbelSew => "Costura do consolo",
            BarRole.LiftLoop => "Alça de içamento",
            BarRole.CorbelAnchor => "Barra soldada do tirante",
            BarRole.CorbelStirrup => "Estribo vertical do consolo",
            BarRole.CorbelBottom => "Armadura inferior do consolo",
            BarRole.CorbelPin => "Pino do consolo",
            _ => r.ToString()
        };

        public static string Fmt(double mm) => mm.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
