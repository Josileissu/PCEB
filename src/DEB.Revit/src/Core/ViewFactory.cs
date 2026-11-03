using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace DEB.Revit.Core
{
    internal enum ViewRole { Elevation, Plan, Side, CrossSection, ThreeD }

    internal sealed class DetailView
    {
        public View View { get; }
        public ViewRole Role { get; }
        public string Title { get; }

        public DetailView(View view, ViewRole role, string title)
        {
            View = view; Role = role; Title = title;
        }
    }

    internal sealed class DetailViews
    {
        public List<DetailView> Views { get; } = new List<DetailView>();
        public ViewSchedule PartList { get; set; }
    }

    /// <summary>
    /// Cria as vistas de assembly da peça. As vistas de assembly mostram apenas os membros
    /// do assembly e ficam orientadas pelo sistema local definido em PieceInfo.
    /// </summary>
    internal sealed class ViewFactory
    {
        private readonly Document _doc;
        private readonly DetailingConfig _cfg;
        private readonly PieceLog _log;
        private readonly NameRegistry _names;
        private AssemblyDetailViewOrientation? _crossOrientation;

        public ViewFactory(Document doc, DetailingConfig cfg, PieceLog log)
        {
            _doc = doc; _cfg = cfg; _log = log;
            _names = NameRegistry.ForViews(doc);
        }

        public DetailViews Create(AssemblyInstance asm, PieceInfo p)
        {
            var dv = new DetailViews();

            if (p.Kind == PieceKind.Beam)
            {
                AddOrthogonal(dv, asm, p, AssemblyDetailViewOrientation.ElevationFront, _cfg.BeamElevationScale);
                AddOrthogonal(dv, asm, p, AssemblyDetailViewOrientation.ElevationTop, _cfg.BeamElevationScale);

                double L = p.LengthX;
                double dSupport = _cfg.BeamSupportSectionDistanceMm > 0 ? U.Mm(_cfg.BeamSupportSectionDistanceMm) : p.LengthZ;
                var cuts = new List<double>();
                if (dSupport < 0.4 * L) cuts.Add(dSupport);   // corte junto ao apoio
                cuts.Add(L / 2.0);                            // corte no meio do vão

                char letter = 'A';
                foreach (double t in cuts) AddCrossSection(dv, asm, p, p.AxisX, t, letter++);
            }
            else
            {
                AddOrthogonal(dv, asm, p, AssemblyDetailViewOrientation.ElevationFront, _cfg.ColumnElevationScale);
                AddOrthogonal(dv, asm, p, AssemblyDetailViewOrientation.ElevationLeft, _cfg.ColumnElevationScale);
                char letter = 'A';
                foreach (double z in p.SectionHeights ?? new List<double> { p.LengthZ / 2.0 })
                    AddCrossSection(dv, asm, p, p.AxisZ, z, letter++);
            }

            if (_cfg.Include3D)
            {
                try
                {
                    View3D v3 = AssemblyViewUtils.Create3DOrthographic(_doc, asm.Id);
                    Setup(v3, $"{p.Mark} - 3D", "VISTA 3D", _cfg.View3DScale, _cfg.View3DViewTemplate);
                    dv.Views.Add(new DetailView(v3, ViewRole.ThreeD, "VISTA 3D"));
                }
                catch (Exception ex) { _log.Warn("Vista 3D não criada: " + ex.Message); }
            }

            FitCrops(dv, p);
            HideForeignViewers(dv);

            if (_cfg.IncludePartList)
            {
                try
                {
                    ViewSchedule s = AssemblyViewUtils.CreatePartList(_doc, asm.Id);
                    s.Name = _names.Unique($"{p.Mark} - Lista de peças");
                    dv.PartList = s;
                }
                catch (Exception ex) { _log.Warn("Lista de peças não criada: " + ex.Message); }
            }

            return dv;
        }

        // ------------------------------------------------------------------

        private void AddOrthogonal(DetailViews dv, AssemblyInstance asm, PieceInfo p,
            AssemblyDetailViewOrientation orientation, int scale)
        {
            ViewSection v = AssemblyViewUtils.CreateDetailSection(_doc, asm.Id, orientation);
            _doc.Regenerate();

            (ViewRole role, string title) = Classify(v, p);
            Setup(v, $"{p.Mark} - {ToNameCase(title)}", title, scale, _cfg.ElevationViewTemplate);
            dv.Views.Add(new DetailView(v, role, title));
        }

        private void AddCrossSection(DetailViews dv, AssemblyInstance asm, PieceInfo p, XYZ axis, double t, char letter)
        {
            string title = $"CORTE {letter}-{letter}";
            ViewSection v = CreateCrossSectionView(asm, axis);
            if (v == null)
            {
                _log.Warn($"{title}: não foi possível criar um corte perpendicular ao eixo da peça.");
                return;
            }

            // O nome precisa estar definido antes de mover: o marcador do corte tem o mesmo nome da vista.
            Setup(v, $"{p.Mark} - Corte {letter}", title, _cfg.SectionScale, _cfg.SectionViewTemplate);

            double target = p.Origin.DotProduct(axis) + t;
            if (!MoveSection(v, axis, target))
                _log.Warn($"{title}: posição não ajustada automaticamente (ficou na posição padrão do Revit). Arraste o corte na elevação, se precisar.");
            else
                _log.Info($"{title} a {U.Cm(t)} cm da origem da peça.");

            dv.Views.Add(new DetailView(v, ViewRole.CrossSection, title));
        }

        /// <summary>
        /// Descobre qual orientação de assembly (Detail Section A/B ou Horizontal) corta
        /// perpendicularmente ao eixo pedido. O resultado fica em cache para os próximos cortes.
        /// </summary>
        internal ViewSection CreateCrossSectionView(AssemblyInstance asm, XYZ axis)
        {
            AssemblyDetailViewOrientation[] candidates = _crossOrientation.HasValue
                ? new[] { _crossOrientation.Value }
                : new[]
                {
                    AssemblyDetailViewOrientation.DetailSectionA,
                    AssemblyDetailViewOrientation.DetailSectionB,
                    AssemblyDetailViewOrientation.HorizontalDetail
                };

            foreach (AssemblyDetailViewOrientation o in candidates)
            {
                ViewSection v;
                try { v = AssemblyViewUtils.CreateDetailSection(_doc, asm.Id, o); }
                catch { continue; }

                _doc.Regenerate();
                if (Math.Abs(v.ViewDirection.DotProduct(axis)) > 0.99)
                {
                    _crossOrientation = o;
                    return v;
                }
                _doc.Delete(v.Id);
            }
            return null;
        }

        /// <summary>Move o marcador do corte ao longo do eixo até a posição desejada e confere o resultado.</summary>
        internal bool MoveSection(ViewSection v, XYZ axis, double targetAlongAxis)
        {
            try
            {
                _doc.Regenerate();
                double delta = targetAlongAxis - v.Origin.DotProduct(axis);
                if (Math.Abs(delta) < U.Mm(1)) return true;

                Element marker = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_Viewers)
                    .WhereElementIsNotElementType()
                    .FirstOrDefault(e => e.Id != v.Id && e.Name == v.Name);
                if (marker == null) return false;

                ElementTransformUtils.MoveElement(_doc, marker.Id, axis * delta);
                _doc.Regenerate();
                return Math.Abs(v.Origin.DotProduct(axis) - targetAlongAxis) < U.Mm(5);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Recorta cada elevação/corte na peça (+ elementos anexados, como consolos) com folga,
        /// para a viewport ter o tamanho do desenho e não o da caixa padrão do assembly.
        /// </summary>
        private void FitCrops(DetailViews dv, PieceInfo p)
        {
            var pts = new List<XYZ>(p.Vertices);
            foreach (FamilyInstance k in p.Attached)
                pts.AddRange(GeometryHelper.GetVertices(GeometryHelper.GetSolids(k, null)));
            double margin = U.Mm(_cfg.CropMarginMm);

            foreach (DetailView d in dv.Views)
            {
                if (!(d.View is ViewSection v)) continue;
                try { FitCrop(v, pts, margin); }
                catch (Exception ex) { _log.Warn($"{d.Title}: recorte não ajustado ({ex.Message})."); }
            }
        }

        /// <summary>Recorta a vista nos pontos dados (coordenadas do modelo) com folga; desliga o recorte de anotações.</summary>
        internal static void FitCrop(ViewSection v, IEnumerable<XYZ> pts, double margin)
        {
            BoundingBoxXYZ cb = v.CropBox;
            Transform inv = cb.Transform.Inverse;
            List<XYZ> loc = pts.Select(inv.OfPoint).ToList();
            cb.Min = new XYZ(loc.Min(q => q.X) - margin, loc.Min(q => q.Y) - margin, cb.Min.Z);
            cb.Max = new XYZ(loc.Max(q => q.X) + margin, loc.Max(q => q.Y) + margin, cb.Max.Z);
            v.CropBox = cb;
            v.CropBoxActive = true;
            v.CropBoxVisible = false;
            // Cotas ficam fora do recorte do modelo: o recorte de anotações não pode cortá-las.
            v.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE)?.Set(0);
        }

        /// <summary>
        /// Esconde marcas de vista que só poluem o desenho: nos cortes, todas; nas elevações,
        /// as das outras elevações (as linhas dos cortes transversais continuam visíveis).
        /// </summary>
        private void HideForeignViewers(DetailViews dv)
        {
            _doc.Regenerate();
            var names = dv.Views.ToDictionary(d => d.View.Name, d => d.Role);
            List<Element> markers = new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_Viewers)
                .WhereElementIsNotElementType()
                .Where(e => names.ContainsKey(e.Name))
                .ToList();

            foreach (DetailView d in dv.Views)
            {
                if (d.View is View3D) continue;
                List<ElementId> hide = markers
                    .Where(m => d.Role == ViewRole.CrossSection || names[m.Name] != ViewRole.CrossSection)
                    .Where(m => m.CanBeHidden(d.View))
                    .Select(m => m.Id).ToList();
                if (hide.Count == 0) continue;
                try { d.View.HideElements(hide); }
                catch (Exception ex) { _log.Warn($"{d.Title}: marcas de vista não ocultadas ({ex.Message})."); }
            }
        }

        private static (ViewRole, string) Classify(View v, PieceInfo p)
        {
            XYZ d = v.ViewDirection;
            bool beam = p.Kind == PieceKind.Beam;
            if (Math.Abs(d.DotProduct(p.AxisZ)) > 0.99) return (ViewRole.Plan, beam ? "VISTA SUPERIOR" : "SEÇÃO");
            if (Math.Abs(d.DotProduct(p.AxisY)) > 0.99) return (ViewRole.Elevation, beam ? "ELEVAÇÃO" : "ELEVAÇÃO FRONTAL");
            if (Math.Abs(d.DotProduct(p.AxisX)) > 0.99) return (ViewRole.Side, beam ? "VISTA LATERAL" : "ELEVAÇÃO LATERAL");
            return (ViewRole.Elevation, "VISTA");
        }

        private void Setup(View v, string name, string title, int scale, string templateName)
        {
            v.Name = _names.Unique(name);

            View template = FindTemplate(templateName);
            if (template != null)
            {
                try { v.ViewTemplateId = template.Id; }
                catch (Exception ex) { _log.Warn($"Template \"{templateName}\" não aplicado em {v.Name}: {ex.Message}"); }
            }
            else if (!string.IsNullOrWhiteSpace(templateName))
            {
                _log.Warn($"Template de vista \"{templateName}\" não encontrado no projeto.");
            }

            // Se o template controlar estes parâmetros, o Revit recusa a alteração: tudo bem.
            TrySet(() => v.Scale = scale);
            TrySet(() => v.DetailLevel = ViewDetailLevel.Fine);
            if (!(v is View3D)) TrySet(() => v.DisplayStyle = DisplayStyle.HLR);

            v.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION)?.Set(title);
        }

        private View FindTemplate(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>()
                .FirstOrDefault(v => v.IsTemplate && string.Equals(v.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static void TrySet(Action a)
        {
            try { a(); } catch { /* parâmetro controlado pelo template */ }
        }

        private static string ToNameCase(string upper) =>
            upper.Length <= 1 ? upper : upper.Substring(0, 1) + upper.Substring(1).ToLowerInvariant();
    }
}
