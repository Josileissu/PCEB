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
        /// <summary>Ganchos de estribo que saíram para fora e foram invertidos (relatório).</summary>
        public int HooksFlipped { get; private set; }

        public RebarFactory(Document doc, PieceLog log)
        {
            _doc = doc; _log = log;
        }

        public List<ElementId> Create(ColumnFrame f, FamilyInstance column, IEnumerable<BarDef> bars, string mark)
        {
            var ids = new List<ElementId>();
            var created = new List<(BarDef Def, Rebar Bar)>();
            List<BarDef> all = bars.ToList();

            // Longitudinais iguais (mesma bitola e mesma forma = mesmo N) viram UM conjunto do Revit: a barra é criada com
            // quantidade (layout de número fixo) e cada barra do conjunto é levada para a sua posição com "Modificar barras"
            // (MoveBarInSet), como se faz à mão no Revit. Assim a quantidade (8 N1) sai do próprio conjunto na etiqueta, na
            // tabela e no detalhe de curvatura em escala (que não desenha barra de forma livre).
            var groups = all.Where(b => (b.Role == BarRole.Longitudinal || b.Role == BarRole.LiftLoop) && b.Count <= 1 && b.HostCorbel == null && !b.Closed)
                .GroupBy(ShapeKey).Where(g => g.Count() > 1).Select(g => g.ToList()).ToList();
            var grouped = new HashSet<BarDef>();
            foreach (List<BarDef> g in groups)
            {
                try
                {
                    Rebar r = CreateMovedSet(f, column, g, mark);
                    ids.Add(r.Id);
                    created.Add((g[0], r));
                    foreach (BarDef b in g) grouped.Add(b);
                }
                catch (Exception ex)
                {
                    _log.Warn($"Longitudinais Ø{Fmt(g[0].DiameterMm)} não agrupadas ({ex.Message}); criadas uma a uma.");
                    _log.Trace("Conjunto com barras movidas", ex);
                }
            }

            foreach (BarDef b in all.Where(x => !grouped.Contains(x)))
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
            if (HooksFlipped > 0) _log.Info($"{HooksFlipped} gancho(s) de estribo virados para dentro.");
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

            if (b.Closed && hook != null)
                try { HooksInward(f, r, pts, b.Normal); }
                catch (Exception ex) { _log.Trace("Ganchos para dentro", ex); }

            if (b.Count > 1 && b.Spacing > 1e-9)
                r.GetShapeDrivenAccessor().SetLayoutAsNumberWithSpacing(b.Count, b.Spacing, true, true, true);

            r.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"{Tag}|{mark}|{RoleLabel(b.Role)}");
            return r;
        }

        /// <summary>Chave de igualdade das barras: bitola + vetores dos trechos (mm), sem a posição.</summary>
        private static string ShapeKey(BarDef b)
        {
            const double mmPerFoot = 304.8;
            var parts = new List<string> { b.DiameterMm.ToString("0.0", CultureInfo.InvariantCulture) };
            for (int i = 0; i < b.Points.Count - 1; i++)
            {
                Loc p = b.Points[i], q = b.Points[i + 1];
                // 0,1 mm: em mm inteiro, um trecho de x,5 mm (perna da alça = 599,5) arredondava para lados diferentes
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.0},{1:0.0},{2:0.0}",
                    R((q.A - p.A) * mmPerFoot), R((q.B - p.B) * mmPerFoot), R((q.C - p.C) * mmPerFoot)));
            }
            return string.Join("|", parts);
            static double R(double mm) { double r = Math.Round(mm, 1); return r == 0 ? 0 : r; }   // sem "-0.0"
        }

        /// <summary>
        /// Conjunto de barras iguais em posições quaisquer: cria a 1ª barra, distribui g.Count barras (número fixo com
        /// espaçamento até a 2ª posição) e move cada barra do conjunto até a sua posição (MoveBarInSet = "Modificar barras").
        /// </summary>
        private Rebar CreateMovedSet(ColumnFrame f, FamilyInstance column, List<BarDef> g, string mark)
        {
            XYZ Mid(BarDef b) => (f.ToWorld(b.Points[0]) + f.ToWorld(b.Points[^1])) / 2;
            BarDef first = g[0];
            XYZ n;
            double spacing;
            if (first.Points.Count == 2)
            {
                // barra reta: distribui na direção da 2ª barra (o "Modificar barras" ajusta as demais)
                XYZ axis = (f.ToWorld(first.Points[^1]) - f.ToWorld(first.Points[0])).Normalize();
                XYZ d = Mid(g[1]) - Mid(first);
                n = d - axis * d.DotProduct(axis);
                if (n.GetLength() < 1e-6) throw new InvalidOperationException("posições coincidentes");
                // espaçamento pequeno: com a distância até a 2ª barra, as últimas do conjunto (6 ø20 num 50x50) caíam fora
                // do pilar antes de serem movidas e o Revit não as devolvia; cada barra vai depois para a sua posição
                spacing = Math.Min(n.GetLength(), U.Mm(Math.Max(30, 2 * first.DiameterMm)));
                n = n.Normalize();
            }
            else
            {
                // barra dobrada (alça): normal do plano da barra; cada barra é movida para a sua posição
                n = f.DirToWorld(first.Normal);
                spacing = U.Mm(50);
            }

            var curves = new List<Curve>();
            for (int i = 0; i < first.Points.Count - 1; i++)
                curves.Add(Line.CreateBound(f.ToWorld(first.Points[i]), f.ToWorld(first.Points[i + 1])));
            Rebar r = Rebar.CreateFromCurves(_doc, RebarStyle.Standard, BarType(first.DiameterMm), null, null, column, n, curves,
                RebarHookOrientation.Right, RebarHookOrientation.Left, true, true);
            try
            {
                r.GetShapeDrivenAccessor().SetLayoutAsNumberWithSpacing(g.Count, spacing, true, true, true);
                _doc.Regenerate();
                for (int i = 1; i < g.Count; i++)
                {
                    IList<Curve> c = r.GetTransformedCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, i);
                    if (c.Count == 0) throw new InvalidOperationException($"barra {i + 1} do conjunto sem geometria");
                    XYZ now = (c[0].GetEndPoint(0) + c[^1].GetEndPoint(1)) / 2;
                    XYZ move = Mid(g[i]) - now;
                    if (move.GetLength() > 1e-6) r.MoveBarInSet(i, Transform.CreateTranslation(move));
                }
            }
            catch
            {
                // o conjunto incompleto não pode ficar no modelo (as barras são criadas uma a uma depois)
                try { _doc.Delete(r.Id); } catch { /* já removido */ }
                throw;
            }
            r.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"{Tag}|{mark}|{RoleLabel(first.Role)}");
            return r;
        }

        /// <summary>
        /// Estribos fechados: cada gancho de 135° tem de apontar para dentro do estribo. A orientação (direita/esquerda) do
        /// Revit depende do sentido das curvas e da normal; em vez de deduzir, confere a ponta de cada gancho e inverte o
        /// que ficou para fora.
        /// </summary>
        private void HooksInward(ColumnFrame f, Rebar r, List<Loc> pts, Loc normal)
        {
            _doc.Regenerate();
            Loc e1 = pts[1] - pts[0];
            double l1 = Math.Sqrt(e1.Dot(e1));
            if (l1 < 1e-9) return;
            e1 = e1 * (1 / l1);
            Loc e2 = normal.Cross(e1);
            var poly = pts.Select(q => (X: (q - pts[0]).Dot(e1), Y: (q - pts[0]).Dot(e2))).ToList();
            bool Inside(XYZ w)
            {
                Loc q = f.ToLocal(w) - pts[0];
                double x = q.Dot(e1), y = q.Dot(e2);
                bool inside = false;
                for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                    if ((poly[i].Y > y) != (poly[j].Y > y) &&
                        x < (poly[j].X - poly[i].X) * (y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                        inside = !inside;
                return inside;
            }
            IList<Curve> c = r.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
            if (c.Count < 3) return;
            var tips = new[] { c[0].GetEndPoint(0), c[^1].GetEndPoint(1) };
            for (int end = 0; end < 2; end++)
            {
                if (Inside(tips[end])) continue;
                RebarHookOrientation o = r.GetHookOrientation(end);
                r.SetHookOrientation(end, o == RebarHookOrientation.Left ? RebarHookOrientation.Right : RebarHookOrientation.Left);
                HooksFlipped++;
            }
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
