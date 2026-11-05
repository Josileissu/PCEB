using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DEB.Revit.Core;

namespace DEB.Revit.Presentation
{
    /// <summary>Dados da peça que o padrão usa (campos de texto e cotas dos cortes).</summary>
    internal sealed class PresentationContext
    {
        public PresentationStandard Standard { get; set; }
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
        /// <summary>Alturas locais (a partir da base) dos cortes por posição.</summary>
        public Dictionary<CutPosition, List<double>> CutHeights { get; set; } = new Dictionary<CutPosition, List<double>>();
    }

    /// <summary>Vista criada a partir de um item do padrão.</summary>
    internal sealed class PlacedView
    {
        public View View { get; set; }
        public ViewSpec Spec { get; set; }
        public string Title { get; set; }
    }

    /// <summary>
    /// Gera o detalhamento de um assembly seguindo um <see cref="PresentationStandard"/>:
    /// vistas (faces, armadura, seções, cortes), cotas, rótulos das posições, dobramentos e a prancha.
    /// Cada etapa roda em sua própria transação.
    /// </summary>
    internal sealed class StandardDetailer
    {
        private readonly Document _doc;
        private readonly DetailingConfig _cfg;
        private readonly PresentationContext _ctx;
        private PresentationStandard Std => _ctx.Standard;

        public StandardDetailer(Document doc, DetailingConfig cfg, PresentationContext ctx)
        {
            _doc = doc; _ctx = ctx;
            _cfg = cfg.Clone();
            _cfg.DimensionOffsetMm = ctx.Standard.Unidades.AfastamentoCotaMm;
        }

        public ViewSheet Detail(AssemblyInstance asm, PieceInfo p, PieceLog log)
        {
            AnnotationStyles styles = null;
            if (!Tx("Estilos do padrão", log, () => styles = AnnotationStyles.Ensure(_doc, Std))) return null;

            List<PlacedView> views = null;
            if (!Tx("Criar vistas", log, () => views = CreateViews(asm, p, log)) || views == null) return null;

            int dims = 0;
            Tx("Cotar vistas", log, () =>
            {
                var ds = new DimensionService(_doc, _cfg, log) { DimensionType = styles.Dimension };
                foreach (PlacedView v in views.Where(v => v.Spec.Cotas && !(v.View is View3D)))
                {
                    dims += ds.AddOverallDimensions(new DetailView(v.View, ViewRole.Elevation, v.Title), p);
                    foreach (FamilyInstance k in p.Attached)
                        dims += ds.AddAttachedDimensions(new DetailView(v.View, ViewRole.Elevation, v.Title), p, k);
                }
            }, critical: false);

            // Níveis e comprimento total na 1ª vista de face (como a VISTA 1 da PP127)
            PlacedView firstFace = views.FirstOrDefault(v => v.Spec.Tipo == ViewKind.Face);
            if (firstFace != null && p.Kind == PieceKind.Column)
                Tx("Níveis", log, () => LevelMarks(firstFace.View, p, styles), critical: false);

            List<Rebar> rebars = asm.GetMemberIds().Select(_doc.GetElement).OfType<Rebar>().ToList();
            if (rebars.Count > 0)
            {
                Tx("Rotular armadura", log, () =>
                {
                    _doc.Regenerate();   // numeração das posições
                    foreach (PlacedView v in views.Where(v => v.Spec.Tipo == ViewKind.Armadura))
                        LabelRebar(v.View, p, rebars, styles);
                }, critical: false);

                if (Std.Armadura.DesenhoDeDobramento)
                    Tx("Dobramento das barras", log, () =>
                    {
                        // Ao lado da elevação da armadura (como as formas da PP127); na seção só se a barra não aparecer
                        // na elevação. Na seção elas aumentavam a vista, que não cabia em 1:25 e ia para 1:50.
                        List<View> targets = views.Where(v => v.Spec.Tipo == ViewKind.Armadura)
                            .Concat(views.Where(v => v.Spec.Tipo == ViewKind.SecaoArmadura).Take(1))
                            .Select(v => v.View).ToList();
                        if (targets.Count > 0) BendingDetails(targets, p, rebars, styles, log);
                    }, critical: false);
            }

            ViewSheet sheet = null;
            if (!Tx("Montar prancha", log, () => sheet = new SheetComposer(_doc, _ctx, styles, log).Compose(asm, views)))
                return null;
            log.Info($"{views.Count} vistas, {dims} cotas.");
            return sheet;
        }

        // ------------------------------------------------------------------
        // Vistas

        private readonly Dictionary<string, AssemblyDetailViewOrientation> _elevationCache = new Dictionary<string, AssemblyDetailViewOrientation>();

        private List<PlacedView> CreateViews(AssemblyInstance asm, PieceInfo p, PieceLog log)
        {
            var result = new List<PlacedView>();
            var names = NameRegistry.ForViews(_doc);
            var vf = new ViewFactory(_doc, _cfg, log);
            string mark = _ctx.Values.TryGetValue("Marca", out string mk) ? mk : p.Mark;
            char letter = 'A';
            int number = 1;

            foreach (ViewSpec spec in Std.Vistas.Where(v => v.Ativa))
            {
                try
                {
                    switch (spec.Tipo)
                    {
                        case ViewKind.Face:
                        case ViewKind.Armadura:
                        {
                            View v = CreateElevation(asm, FaceDirection(p, spec.Face));
                            if (v == null) { log.Warn($"{spec.Titulo}: elevação da face {spec.Face} não criada."); break; }
                            result.Add(Setup(v, spec, Title(spec, spec.Face.ToString(), ""), mark, names));
                            break;
                        }
                        case ViewKind.Corte:
                        case ViewKind.SecaoArmadura:
                        {
                            if (!_ctx.CutHeights.TryGetValue(spec.Posicao, out List<double> zs) || zs.Count == 0) break;
                            foreach (double z in zs)
                            {
                                ViewSection v = vf.CreateCrossSectionView(asm, p.AxisZ);
                                if (v == null) { log.Warn($"{spec.Titulo}: corte transversal não criado."); break; }
                                string id = spec.Tipo == ViewKind.Corte ? (letter++).ToString() : (number++).ToString();
                                string title = spec.Tipo == ViewKind.Corte ? Title(spec, "", id) : Title(spec, "", "", id);
                                PlacedView pv = Setup(v, spec, title, mark, names);   // nome antes de mover (marcador = nome)
                                if (!vf.MoveSection(v, p.AxisZ, p.Origin.DotProduct(p.AxisZ) + z))
                                    log.Warn($"{title.Split('\n')[0]}: posição não ajustada.");
                                result.Add(pv);
                            }
                            break;
                        }
                        case ViewKind.Vista3D:
                        {
                            View3D v3 = AssemblyViewUtils.Create3DOrthographic(_doc, asm.Id);
                            result.Add(Setup(v3, spec, Title(spec, "", ""), mark, names));
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.Warn($"{spec.Titulo.Split('\n')[0]}: {ex.Message}");
                    log.Trace("Vista " + spec.Tipo, ex);
                }
            }

            _doc.Regenerate();
            var pts = new List<XYZ>(p.Vertices);
            foreach (FamilyInstance k in p.Attached) pts.AddRange(GeometryHelper.GetVertices(GeometryHelper.GetSolids(k, null)));
            foreach (PlacedView pv in result)
            {
                if (pv.View is ViewSection vs)
                {
                    try { ViewFactory.FitCrop(vs, pts, U.Mm(_cfg.CropMarginMm)); }
                    catch (Exception ex) { log.Warn($"{pv.Title.Split('\n')[0]}: recorte não ajustado ({ex.Message})."); }
                }
            }
            ShowOrHideRebar(asm, result);
            HideMarkers(result);
            return result;
        }

        /// <summary>Face 1 = +X, 2 = −Y, 3 = −X, 4 = +Y (sistema da peça). A vista olha para a face.</summary>
        private static XYZ FaceDirection(PieceInfo p, int face) => face switch
        {
            2 => p.AxisY.Negate(),
            3 => p.AxisX.Negate(),
            4 => p.AxisY,
            _ => p.AxisX
        };

        /// <summary>Elevação de assembly cuja direção de vista coincide com <paramref name="dir"/>.</summary>
        private View CreateElevation(AssemblyInstance asm, XYZ dir)
        {
            string key = $"{Math.Round(dir.X, 2)}|{Math.Round(dir.Y, 2)}|{Math.Round(dir.Z, 2)}";
            IEnumerable<AssemblyDetailViewOrientation> candidates = _elevationCache.TryGetValue(key, out var known)
                ? new[] { known }
                : new[]
                {
                    AssemblyDetailViewOrientation.ElevationFront, AssemblyDetailViewOrientation.ElevationBack,
                    AssemblyDetailViewOrientation.ElevationLeft, AssemblyDetailViewOrientation.ElevationRight,
                    AssemblyDetailViewOrientation.ElevationTop, AssemblyDetailViewOrientation.ElevationBottom
                };
            foreach (AssemblyDetailViewOrientation o in candidates)
            {
                ViewSection v;
                try { v = AssemblyViewUtils.CreateDetailSection(_doc, asm.Id, o); }
                catch { continue; }
                _doc.Regenerate();
                if (v.ViewDirection.DotProduct(dir) > 0.99) { _elevationCache[key] = o; return v; }
                _doc.Delete(v.Id);
            }
            return null;
        }

        private static string Title(ViewSpec spec, string face, string letter, string number = "") =>
            spec.Titulo.Replace("{Face}", face).Replace("{Letra}", letter).Replace("{Numero}", number);

        private PlacedView Setup(View v, ViewSpec spec, string title, string mark, NameRegistry names)
        {
            v.Name = names.Unique($"{mark} - {title.Split('\n')[0]}");
            Try(() => v.Scale = spec.Escala);
            Try(() => v.DetailLevel = ViewDetailLevel.Fine);
            if (!(v is View3D)) Try(() => v.DisplayStyle = DisplayStyle.HLR);
            if (!spec.MostrarArmadura) Try(() => v.SetCategoryHidden(new ElementId(BuiltInCategory.OST_Rebar), true));
            ApplyTemplate(v, spec.Template);
            v.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION)?.Set(title.Replace("\n", " "));
            return new PlacedView { View = v, Spec = spec, Title = title };
        }

        /// <summary>Parâmetros que o template não controla: escala e recorte são de cada peça (a prancha ajusta).</summary>
        private static readonly BuiltInParameter[] FreeInTemplate =
        {
            BuiltInParameter.VIEW_SCALE_PULLDOWN_METRIC, BuiltInParameter.VIEW_SCALE_PULLDOWN_IMPERIAL, BuiltInParameter.VIEW_SCALE,
            BuiltInParameter.VIEWER_CROP_REGION, BuiltInParameter.VIEWER_CROP_REGION_VISIBLE, BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE,
            BuiltInParameter.VIEWER_BOUND_ACTIVE_FAR, BuiltInParameter.VIEWER_BOUND_FAR_CLIPPING, BuiltInParameter.VIEWER_BOUND_OFFSET_FAR,
            BuiltInParameter.VIEW_DESCRIPTION
        };

        /// <summary>
        /// Aplica o template de vista pelo nome. Se não existir, cria a partir desta vista (já configurada pelo
        /// padrão): o usuário pode depois editar o template no Revit, e as próximas pranchas seguem a edição.
        /// </summary>
        private void ApplyTemplate(View v, string name)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            View t = new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>()
                .FirstOrDefault(x => x.IsTemplate && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (t == null)
            {
                try
                {
                    t = v.CreateViewTemplate();
                    t.Name = name;
                    var free = new HashSet<long>(FreeInTemplate.Select(b => (long)b));
                    t.SetNonControlledTemplateParameterIds(t.GetTemplateParameterIds().Where(id => free.Contains(id.Value)).ToList());
                }
                catch { return; /* vista sem suporte a template: segue sem */ }
            }
            Try(() => v.ViewTemplateId = t.Id);
        }

        private void ShowOrHideRebar(AssemblyInstance asm, List<PlacedView> views)
        {
            List<Rebar> rebars = asm.GetMemberIds().Select(_doc.GetElement).OfType<Rebar>().ToList();
            var cat = new ElementId(BuiltInCategory.OST_Rebar);
            foreach (PlacedView pv in views)
            {
                if (pv.Spec.MostrarArmadura)
                    foreach (Rebar r in rebars) Try(() => r.SetUnobscuredInView(pv.View, true));
                else
                    Try(() => pv.View.SetCategoryHidden(cat, true));
            }
        }

        /// <summary>
        /// Marcas de corte: nas vistas de face aparecem os cortes (A-A...), na elevação da armadura
        /// aparecem as seções (1-1...); nos cortes e seções nenhuma marca.
        /// </summary>
        private void HideMarkers(List<PlacedView> views)
        {
            _doc.Regenerate();
            var kindByName = views.GroupBy(v => v.View.Name).ToDictionary(g => g.Key, g => g.First().Spec.Tipo);
            List<Element> markers = new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_Viewers)
                .WhereElementIsNotElementType().Where(e => kindByName.ContainsKey(e.Name)).ToList();

            foreach (PlacedView pv in views.Where(v => !(v.View is View3D)))
            {
                ViewKind show = pv.Spec.Tipo == ViewKind.Face ? ViewKind.Corte
                              : pv.Spec.Tipo == ViewKind.Armadura ? ViewKind.SecaoArmadura
                              : (ViewKind)(-1);
                List<ElementId> hide = markers.Where(m => kindByName[m.Name] != show && m.CanBeHidden(pv.View))
                    .Select(m => m.Id).ToList();
                if (hide.Count > 0) Try(() => pv.View.HideElements(hide));
            }
        }

        // ------------------------------------------------------------------
        // Níveis (topo, níveis do projeto que cruzam a peça, base) e comprimento total

        private void LevelMarks(View view, PieceInfo p, AnnotationStyles styles)
        {
            XYZ right = view.RightDirection, up = view.UpDirection, vd = view.ViewDirection;
            double scale = view.Scale;
            double w = view.Origin.DotProduct(vd);
            XYZ At(double r, double u) => right * r + up * u + vd * w;
            double minR = p.Vertices.Min(v => v.DotProduct(right));
            double bottom = p.Vertices.Min(v => v.DotProduct(up)), top = p.Vertices.Max(v => v.DotProduct(up));
            double zBottom = p.Vertices.Min(v => v.Z), zTop = p.Vertices.Max(v => v.Z);
            // cota vertical da vista ↔ Z do modelo (a vista de face é vertical: up = +Z)
            double Vz(double z) => bottom + (z - zBottom) * (up.Z >= 0 ? 1 : -1);

            double tick = U.Mm(25) * scale;             // traço do nível até a peça
            double x0 = minR - U.Mm(14) * scale;        // fim do traço (afastado das cotas da vista)
            double textX = x0 - tick;
            var opt = new TextNoteOptions(styles.Text.Id) { HorizontalAlignment = HorizontalTextAlignment.Left };

            var marks = new List<(string Name, double Z)> { ("TOPO PILAR\n(TOP COLUMN)", zTop) };
            foreach (Level lv in new FilteredElementCollector(_doc).OfClass(typeof(Level)).Cast<Level>())
                if (lv.Elevation > zBottom + U.Mm(5) && lv.Elevation < zTop - U.Mm(5))
                    marks.Add((lv.Name.ToUpperInvariant(), lv.Elevation));
            marks.Add(("BASE PILAR\n(COLUMN BASE)", zBottom));

            foreach (var (name, z) in marks)
            {
                double u = Vz(z);
                Try(() =>
                {
                    DetailCurve dc = _doc.Create.NewDetailCurve(view, Line.CreateBound(At(textX, u), At(minR, u)));
                    // linha fina e tracejada não aparece na prancha: estilo "Linhas médias"/"Medium Lines" se existir
                    GraphicsStyle gs = dc.GetLineStyleIds().Select(id => _doc.GetElement(id)).OfType<GraphicsStyle>()
                        .FirstOrDefault(g => g.Name.IndexOf("dia", StringComparison.OrdinalIgnoreCase) >= 0 || g.Name.IndexOf("Medium", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (gs != null) dc.LineStyle = gs;
                });
                double zm = Math.Round(UnitUtils.ConvertFromInternalUnits(z, UnitTypeId.Meters), 2);
                string elev = zm == 0 ? "0,00" : zm.ToString("+0.00;-0.00", CultureInfo.GetCultureInfo("pt-BR"));
                double lineH = U.Mm(Std.Textos.AlturaTextoMm * 1.9) * scale;
                Try(() => TextNote.Create(_doc, view.Id, At(textX, u + lineH * 2.2), name, opt));
                Try(() => TextNote.Create(_doc, view.Id, At(x0 - tick * 0.9, u + lineH * 0.9), elev, opt));
            }

            // comprimento total (texto vertical à esquerda dos níveis)
            double lenCm = Math.Round(U_ToCm(zTop - zBottom));
            var vert = new TextNoteOptions(styles.Text.Id) { HorizontalAlignment = HorizontalTextAlignment.Center, Rotation = Math.PI / 2 };
            Try(() => TextNote.Create(_doc, view.Id, At(textX - U.Mm(8) * scale, (bottom + top) / 2),
                $"COMPRIMENTO TOTAL DO PILAR {lenCm:0}\n(TOTAL COLUMN LENGTH {lenCm:0})", vert));
        }

        private static double U_ToCm(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Centimeters);

        // ------------------------------------------------------------------
        // Rótulos das posições na elevação da armadura

        private void LabelRebar(View view, PieceInfo p, List<Rebar> rebars, AnnotationStyles styles)
        {
            XYZ right = view.RightDirection, up = view.UpDirection, vd = view.ViewDirection;
            double scale = view.Scale;
            double gap = U.Mm(6) * scale;
            double line = U.Mm(Std.Textos.AlturaTextoMm * 1.9) * scale;
            double w = view.Origin.DotProduct(vd);
            double maxR = p.Vertices.Max(v => v.DotProduct(right)), minR = p.Vertices.Min(v => v.DotProduct(right));
            XYZ At(double r, double u) => right * r + up * u + vd * w;

            var rows = rebars.Select(r => new RebarRow(_doc, r)).Where(r => r.Number != "").ToList();

            // Estribos (e grampos do mesmo trecho), à direita, na altura de cada trecho.
            var right_ = new TextNoteOptions(styles.Text.Id) { HorizontalAlignment = HorizontalTextAlignment.Left };
            foreach (var run in rows.Where(r => r.Role == "Estribo").OrderBy(r => r.Center.DotProduct(up)))
            {
                string text = Fill(Std.Armadura.RotuloEstribos, run);
                List<RebarRow> ties = rows.Where(t => t.Role == "Grampo" && Math.Abs(t.Center.DotProduct(up) - run.Center.DotProduct(up)) < U.Mm(20)).ToList();
                if (ties.Count > 0) text += $" + {ties.Count}x{ties[0].Quantity} N{ties[0].Number}";
                TextNote.Create(_doc, view.Id, At(maxR + gap, run.Center.DotProduct(up) + line / 2), text, right_);
            }

            // Demais posições (longitudinais, consolo, içamento), à esquerda, empilhadas a partir do topo.
            var left = new TextNoteOptions(styles.Text.Id) { HorizontalAlignment = HorizontalTextAlignment.Right };
            double top = p.Vertices.Max(v => v.DotProduct(up));
            int i = 0;
            foreach (var g in rows.Where(r => r.Role != "Estribo" && r.Role != "Grampo")
                         .GroupBy(r => r.Number).OrderBy(g => NumberKey(g.Key)))
            {
                RebarRow first = g.First();
                var merged = new RebarRow(first, g.Sum(x => x.Quantity));
                TextNote.Create(_doc, view.Id, At(minR - gap, top - i++ * line * 1.4), Fill(Std.Armadura.RotuloBarra, merged), left);
            }
        }

        private static int NumberKey(string n) => int.TryParse(n, out int v) ? v : 9999;

        private static string Fill(string template, RebarRow r) => template
            .Replace("{Qtd}", r.Quantity.ToString(CultureInfo.InvariantCulture))
            .Replace("{N}", r.Number)
            .Replace("{Diam}", r.DiameterMm.ToString("0.0", CultureInfo.InvariantCulture))
            .Replace("{Comp}", Math.Round(r.LengthCm).ToString(CultureInfo.InvariantCulture))
            .Replace("{Espacamento}", r.SpacingCm.ToString("0.#", CultureInfo.InvariantCulture));

        /// <summary>Resumo de um conjunto de barras para rótulos.</summary>
        private sealed class RebarRow
        {
            public string Role, Number;
            public int Quantity;
            public double DiameterMm, LengthCm, SpacingCm;
            public XYZ Center;
            public Rebar Rebar;

            public RebarRow(Document doc, Rebar r)
            {
                Rebar = r;
                string[] c = (r.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "").Split('|');
                Role = c.Length > 2 ? c[2] : "";
                Number = r.get_Parameter(BuiltInParameter.REBAR_NUMBER)?.AsString() ?? "";
                Quantity = r.Quantity;
                DiameterMm = U.ToMm((doc.GetElement(r.GetTypeId()) as RebarBarType)?.BarNominalDiameter ?? 0);
                LengthCm = U.ToMm(r.get_Parameter(BuiltInParameter.REBAR_ELEM_LENGTH)?.AsDouble() ?? 0) / 10;
                SpacingCm = U.ToMm(r.get_Parameter(BuiltInParameter.REBAR_ELEM_BAR_SPACING)?.AsDouble() ?? 0) / 10;
                BoundingBoxXYZ bb = r.get_BoundingBox(null);
                Center = bb == null ? XYZ.Zero : (bb.Min + bb.Max) / 2;
            }

            public RebarRow(RebarRow o, int qty)
            {
                Role = o.Role; Number = o.Number; Quantity = qty; DiameterMm = o.DiameterMm;
                LengthCm = o.LengthCm; SpacingCm = o.SpacingCm; Center = o.Center; Rebar = o.Rebar;
            }
        }

        // ------------------------------------------------------------------
        // Formas das barras (RebarBendingDetail) com rótulo, como as formas cotadas da PP127

        private const string BendingTypeName = "PCEB - Forma esquemática";
        private const double ShapeWmm = 28, ShapeHmm = 16;   // tamanho da forma no papel

        /// <summary>Tipo de forma do PCEB: esquemático, cotas de segmento no tipo de cota em cm, sem pino nem ângulo.</summary>
        private RebarBendingDetailType BendingType(AnnotationStyles styles)
        {
            RebarBendingDetailType t = new FilteredElementCollector(_doc).OfClass(typeof(RebarBendingDetailType))
                .Cast<RebarBendingDetailType>().FirstOrDefault(x => x.Name == BendingTypeName);
            if (t != null) return t;
            t = RebarBendingDetailType.CreateSchematic(_doc);
            Try(() => t.Name = BendingTypeName);
            Try(() => t.SegmentLengthDimensionsEnabled = true);
            if (styles?.Dimension != null) Try(() => t.SegmentLengthDimensionTypeId = styles.Dimension.Id);
            Try(() => t.BendDiameterDimensionsEnabled = false);
            Try(() => t.AngularDimensionsEnabled = false);
            Try(() => t.OrthogonalAndOverallDimesionsEnabled = false);
            Try(() => t.SchematicWidth = U.Mm(ShapeWmm));
            Try(() => t.SchematicHeight = U.Mm(ShapeHmm));
            // sem a etiqueta automática (com linha de chamada até a barra): o rótulo da PP127 vai em texto embaixo
            Try(() => t.TagTypeId = ElementId.InvalidElementId);
            return t;
        }

        private void BendingDetails(List<View> targets, PieceInfo p, List<Rebar> rebars, AnnotationStyles styles, PieceLog log)
        {
            RebarBendingDetailType type = BendingType(styles);
            var placedPerView = targets.ToDictionary(v => v.Id, v => 0);
            var center = new TextNoteOptions(styles.Text.Id) { HorizontalAlignment = HorizontalTextAlignment.Center };

            // uma forma por posição (N), na ordem da numeração; quantidade = soma dos conjuntos da posição
            var byNumber = rebars
                .Select(r => new RebarRow(_doc, r))
                .Where(r => r.Role != "Longitudinal" && r.Number != "")
                .GroupBy(r => r.Number).OrderBy(g => NumberKey(g.Key));

            foreach (var g in byNumber)
            {
                RebarRow first = g.First();
                string label = Fill(Std.Armadura.RotuloBarra, new RebarRow(first, g.Sum(x => x.Quantity)));
                bool ok = false;
                foreach (View view in targets)
                {
                    XYZ right = view.RightDirection, up = view.UpDirection, vd = view.ViewDirection;
                    double s = view.Scale;
                    double minU = p.Vertices.Min(v => v.DotProduct(up)), maxU = p.Vertices.Max(v => v.DotProduct(up));
                    double minR = p.Vertices.Min(v => v.DotProduct(right)), maxR = p.Vertices.Max(v => v.DotProduct(right));
                    int i = placedPerView[view.Id];
                    double pitch = U.Mm(ShapeHmm + 8) * s, colW = U.Mm(ShapeWmm + 18) * s;
                    XYZ pos;
                    if (maxU - minU > 2 * (maxR - minR))
                    {
                        // elevação alta: colunas à direita, depois dos rótulos dos estribos
                        int perCol = Math.Max(1, (int)((maxU - minU) / pitch));
                        pos = right * (maxR + U.Mm(42) * s + colW * (0.5 + i / perCol)) + up * (maxU - pitch * (0.5 + i % perCol));
                    }
                    else
                        pos = right * (minR + colW * (0.5 + i)) + up * (minU - pitch * 0.8);
                    pos += vd * view.Origin.DotProduct(vd);
                    using (var st = new SubTransaction(_doc))
                    {
                        st.Start();
                        try
                        {
                            RebarBendingDetail.Create(_doc, view.Id, first.Rebar.Id, 0, type, pos, 0);
                            TextNote.Create(_doc, view.Id, pos - up * (U.Mm(ShapeHmm / 2 + 5) * s), label, center);
                            st.Commit();
                            placedPerView[view.Id] = i + 1;
                            ok = true;
                            break;
                        }
                        catch
                        {
                            st.RollBack();   // a barra não aparece nesta vista: tenta a próxima
                        }
                    }
                }
                if (!ok) log.Warn($"Forma da posição N{g.Key} não criada (a barra não aparece nas vistas de armadura).");
            }
        }

        // ------------------------------------------------------------------

        private static void Try(Action a)
        {
            try { a(); } catch { /* parâmetro controlado pelo template ou não aplicável */ }
        }

        private bool Tx(string name, PieceLog log, Action action, bool critical = true)
        {
            using (var t = new Transaction(_doc, name))
            {
                try
                {
                    t.Start();
                    WarningSwallower.Attach(t);
                    action();
                    if (t.Commit() == TransactionStatus.Committed) return true;
                    (critical ? (Action<string>)log.Error : log.Warn)($"{name}: transação não confirmada.");
                    return false;
                }
                catch (Exception ex)
                {
                    if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                    (critical ? (Action<string>)log.Error : log.Warn)($"{name}: {ex.Message}");
                    log.Trace(name, ex);
                    return false;
                }
            }
        }
    }
}
