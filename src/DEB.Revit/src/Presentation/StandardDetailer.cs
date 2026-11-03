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
                        // Junto à 1ª seção com armadura; a barra que não aparece nela vai para a elevação da armadura.
                        List<View> targets = views.Where(v => v.Spec.Tipo == ViewKind.SecaoArmadura).Take(1)
                            .Concat(views.Where(v => v.Spec.Tipo == ViewKind.Armadura))
                            .Select(v => v.View).ToList();
                        if (targets.Count > 0) BendingDetails(targets, p, rebars, log);
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
            if (!string.IsNullOrWhiteSpace(spec.Template))
            {
                View t = new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>()
                    .FirstOrDefault(x => x.IsTemplate && string.Equals(x.Name, spec.Template.Trim(), StringComparison.OrdinalIgnoreCase));
                if (t != null) Try(() => v.ViewTemplateId = t.Id);
            }
            Try(() => v.Scale = spec.Escala);
            Try(() => v.DetailLevel = ViewDetailLevel.Fine);
            if (!(v is View3D)) Try(() => v.DisplayStyle = DisplayStyle.HLR);
            v.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION)?.Set(title.Replace("\n", " "));
            return new PlacedView { View = v, Spec = spec, Title = title };
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
        // Dobramento (forma das barras) abaixo da seção com armadura

        private void BendingDetails(List<View> targets, PieceInfo p, List<Rebar> rebars, PieceLog log)
        {
            RebarBendingDetailType type = new FilteredElementCollector(_doc).OfClass(typeof(RebarBendingDetailType))
                .Cast<RebarBendingDetailType>().FirstOrDefault() ?? RebarBendingDetailType.CreateSchematic(_doc);
            double step = Math.Max(p.LengthX, p.LengthY) * 1.6;
            var placedPerView = targets.ToDictionary(v => v.Id, v => 0);

            var done = new HashSet<string>();
            foreach (Rebar r in rebars)
            {
                string role = (r.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "").Split('|').ElementAtOrDefault(2) ?? "";
                if (role == "Longitudinal") continue;
                string n = r.get_Parameter(BuiltInParameter.REBAR_NUMBER)?.AsString() ?? r.Id.ToString();
                if (!done.Add(n)) continue;

                bool ok = false;
                foreach (View view in targets)
                {
                    XYZ right = view.RightDirection, up = view.UpDirection, vd = view.ViewDirection;
                    double minU = p.Vertices.Min(v => v.DotProduct(up)), minR = p.Vertices.Min(v => v.DotProduct(right));
                    int i = placedPerView[view.Id];
                    XYZ pos = right * (minR + i * step) + up * (minU - step * 0.6) + vd * view.Origin.DotProduct(vd);
                    using (var st = new SubTransaction(_doc))
                    {
                        st.Start();
                        try
                        {
                            RebarBendingDetail.Create(_doc, view.Id, r.Id, 0, type, pos, 0);
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
                if (!ok) log.Warn($"Dobramento da posição N{n} não criado (a barra não aparece nas vistas de armadura).");
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
