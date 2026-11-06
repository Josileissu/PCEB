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
        /// <summary>Consolo detalhado nesta vista (vistas de consolo); null nas demais.</summary>
        public FamilyInstance Corbel { get; set; }
        /// <summary>Cortes e seções transversais: altura do corte (ao longo do eixo da peça, coordenada do projeto) e "A"/"1".</summary>
        public double CutZ { get; set; } = double.NaN;
        public string CutId { get; set; }
        /// <summary>Elevações da peça (faces e armadura): ponto da base do pilar, usado para nivelar as vistas na folha.</summary>
        public XYZ Anchor { get; set; }
        /// <summary>Elevação com os rótulos de nível: começo (à esquerda) das linhas de nível, onde a legenda encosta.</summary>
        public XYZ LevelLineStart { get; set; }
        /// <summary>Vista da alça de içamento: a alça detalhada.</summary>
        internal LiftLoopGeo Loop { get; set; }
    }

    /// <summary>Alça de içamento de cima: a barra, a linha de centro dela (modelo), a direção para fora da face e a face.</summary>
    internal sealed class LiftLoopGeo
    {
        public Rebar Bar;
        public List<Curve> Curves;
        public XYZ U;
        public double Face;
        public double DiameterMm;
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
                    if (v.Corbel != null)
                    {
                        dims += ds.AddAttachedDimensions(new DetailView(v.View, ViewRole.Elevation, v.Title), p, v.Corbel, detail: true);
                        continue;
                    }
                    var dv = new DetailView(v.View, ViewRole.Elevation, v.Title);
                    dims += ds.AddOverallDimensions(dv, p, vertical: v.Spec.CotaTotal);
                    // elevações: uma cota acumulada (base → consolos → topo) no lugar de uma cadeia por consolo
                    int chain = ds.AddLevelChain(dv, p);
                    dims += chain;
                    foreach (FamilyInstance k in p.Attached)
                        dims += ds.AddAttachedDimensions(dv, p, k, skipLevels: chain > 0);
                }
            }, critical: false);

            List<Rebar> rebars = asm.GetMemberIds().Select(_doc.GetElement).OfType<Rebar>().ToList();
            var schematic = _schematic;   // vista esquemática → pontos do desenho
            schematic.Clear();
            if (rebars.Count > 0)
            {
                Tx("Rotular armadura", log, () =>
                {
                    _doc.Regenerate();   // numeração das posições
                    foreach (PlacedView v in views.Where(v => v.Spec.Tipo == ViewKind.Armadura))
                    {
                        if (v.Spec.Esquematica) schematic[v.View.Id] = SchematicElevation(v.View, p, rebars, styles);
                        else LabelRebar(v.View, p, rebars, styles);
                    }
                    if (_tagWarn != null) { log.Warn(_tagWarn); _tagWarn = null; }
                    if (Std.Armadura.ChamadasNasSecoes)
                    {
                        foreach (PlacedView v in views.Where(v => v.Spec.Tipo == ViewKind.SecaoArmadura))
                            SectionCallouts(v.View, p, rebars, styles, log);
                        foreach (PlacedView v in views.Where(v => v.Corbel != null && v.Spec.MostrarArmadura))
                            CorbelCallouts(v.View, v.Corbel, v.Spec.Tipo == ViewKind.ConsoloPlanta, rebars, styles, log);
                    }
                }, critical: false);

                foreach (PlacedView v in views.Where(v => v.Loop != null))
                    Tx("Detalhe da alça", log, () =>
                    {
                        // só a alça: as outras barras (estribos, longitudinais) somem desta vista
                        List<ElementId> other = rebars.Where(r => r.Id != v.Loop.Bar.Id && r.CanBeHidden(v.View)).Select(r => r.Id).ToList();
                        // e os níveis do projeto que cortam o recorte (a linha "Nível 4" passava por cima da etiqueta)
                        other.AddRange(new FilteredElementCollector(_doc).OfClass(typeof(Level)).Where(l => l.CanBeHidden(v.View)).Select(l => l.Id));
                        if (other.Count > 0) v.View.HideElements(other);
                        List<XYZ> pts = LiftDetail(v.View, v.Loop, styles);
                        if (v.View is ViewSection vs && pts.Count > 0) ViewFactory.FitCrop(vs, pts, U.Mm(20));
                    }, critical: false);

                if (Std.Armadura.DesenhoDeDobramento)
                    Tx("Dobramento das barras", log, () =>
                    {
                        // Ao lado da elevação da armadura (como as formas da PP127); na seção só se a barra não aparecer
                        // na elevação. Na seção elas aumentavam a vista, que não cabia em 1:25 e ia para 1:50.
                        List<View> targets = views.Where(v => v.Spec.Tipo == ViewKind.Armadura)
                            .Concat(views.Where(v => v.Spec.Tipo == ViewKind.SecaoArmadura).Take(1))
                            .Select(v => v.View).ToList();
                        // Barras do consolo: no quadro do consolo (embaixo do corte do consolo), como o "ARMADURAS console".
                        List<View> corbelTargets = views.Where(v => v.Spec.Tipo == ViewKind.ConsoloCorte)
                            .Concat(views.Where(v => v.Spec.Tipo == ViewKind.ConsoloPlanta)).Select(v => v.View).ToList();
                        // Estribos e grampos: embaixo da última seção com armadura (como no pilar padrão do CAD).
                        List<View> stirrupTargets = views.Where(v => v.Spec.Tipo == ViewKind.SecaoArmadura).Select(v => v.View)
                            .Reverse().Take(1).ToList();
                        if (targets.Count > 0) BendingDetails(targets, corbelTargets, stirrupTargets, p, rebars, styles, log);
                    }, critical: false);

                // Esquemática: o concreto some; a armadura 3D fica em BRANCO (oculta, as formas das barras somem junto);
                // o recorte passa a abranger o desenho.
                if (schematic.Count > 0)
                    Tx("Elevação esquemática", log, () =>
                    {
                        var hide = new[] { p.Instance.Id }.Concat(p.Attached.Select(k => k.Id)).ToList();
                        var white = new OverrideGraphicSettings();
                        white.SetProjectionLineColor(new Color(255, 255, 255));
                        white.SetCutLineColor(new Color(255, 255, 255));
                        foreach (PlacedView v in views.Where(v => schematic.ContainsKey(v.View.Id)))
                        {
                            // com as longitudinais em detalhe de curvatura (jeito Revit, como a folha 301 do escritório): a
                            // elevação mostra o pilar e os vergalhões reais; o detalhe em escala herdaria o branco do vergalhão
                            bool real = Std.Armadura.LongitudinaisComoDetalhe && _realistic != null;
                            List<ElementId> h = real ? new List<ElementId>() : hide.Where(id => _doc.GetElement(id).CanBeHidden(v.View)).ToList();
                            if (h.Count > 0) v.View.HideElements(h);
                            if (!real) foreach (Rebar r in rebars) Try(() => v.View.SetElementOverrides(r.Id, white));
                            // detalhes de curvatura em escala herdam o branco do vergalhão: voltam ao vermelho da armadura
                            var black = new OverrideGraphicSettings();
                            black.SetProjectionLineColor(RebarRed);
                            List<Element> details = new FilteredElementCollector(_doc, v.View.Id).OfCategory(BuiltInCategory.OST_RebarBendingDetails)
                                .WhereElementIsNotElementType().ToList();
                            foreach (Element d in details) Try(() => v.View.SetElementOverrides(d.Id, black));
                            log.Info($"{v.View.Name}: {details.Count} detalhes de curvatura.");
                            if (v.View is ViewSection vs)
                                ViewFactory.FitCrop(vs, p.Vertices.Concat(schematic[v.View.Id]), U.Mm(_cfg.CropMarginMm));
                        }
                    }, critical: false);
            }

            // Níveis: linha traço-ponto atravessando todas as vistas de face e a armadura (como o template do CAD);
            // nomes, cotas e comprimento total só na 1ª vista de face.
            PlacedView firstFace = views.FirstOrDefault(v => v.Spec.Tipo == ViewKind.Face);
            if (firstFace != null && p.Kind == PieceKind.Column)
                Tx("Níveis", log, () =>
                {
                    foreach (PlacedView v in views.Where(v => v.Corbel == null && (v.Spec.Tipo == ViewKind.Face || v.Spec.Tipo == ViewKind.Armadura)))
                    {
                        XYZ ls = LevelMarks(v.View, p, styles, labels: v == firstFace, log,
                            schematic.TryGetValue(v.View.Id, out List<XYZ> sp) ? sp : null);
                        if (v == firstFace) v.LevelLineStart = ls;
                    }
                }, critical: false);

            // Marcas de corte como no pilar padrão do CAD (traço + letra/número dos dois lados), no lugar das do Revit
            if (Std.Folha.MarcasDeCorteSimples)
                Tx("Marcas de corte", log, () =>
                {
                    var names = new HashSet<string>(views.Select(v => v.View.Name));
                    List<ElementId> revitMarks = new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_Viewers)
                        .WhereElementIsNotElementType().Where(e => names.Contains(e.Name)).Select(e => e.Id).ToList();
                    foreach (PlacedView v in views.Where(v => v.Corbel == null && (v.Spec.Tipo == ViewKind.Face || v.Spec.Tipo == ViewKind.Armadura)))
                    {
                        ViewKind cutKind = v.Spec.Tipo == ViewKind.Face ? ViewKind.Corte : ViewKind.SecaoArmadura;
                        List<PlacedView> cuts = views.Where(c => c.Spec.Tipo == cutKind && !double.IsNaN(c.CutZ)).ToList();
                        List<ElementId> h = revitMarks.Where(id => _doc.GetElement(id)?.CanBeHidden(v.View) == true).ToList();
                        if (h.Count > 0) Try(() => v.View.HideElements(h));
                        bool sch = schematic.TryGetValue(v.View.Id, out List<XYZ> sp);
                        List<XYZ> mk = CutMarks(v.View, p, cuts, styles, sch ? sp : null);
                        // elevação da armadura: o recorte justo cortava o traço da marca da esquerda; passa a abranger as marcas
                        if (sch && mk.Count > 0 && v.View is ViewSection vs)
                            Try(() => ViewFactory.FitCrop(vs, p.Vertices.Concat(sp).Concat(mk), U.Mm(_cfg.CropMarginMm)));
                        else if (mk.Count > 0 && v.View is ViewSection fs)
                            // vistas de face: o traço da marca (fora das cotas) ficava fora do recorte; o recorte cresce até ele
                            Try(() =>
                            {
                                BoundingBoxXYZ cb = fs.CropBox;
                                var keep = new[] { cb.Transform.OfPoint(cb.Min), cb.Transform.OfPoint(cb.Max) };
                                ViewFactory.FitCrop(fs, keep.Concat(mk), 0);
                            });
                    }
                }, critical: false);

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
                            PlacedView ev = Setup(v, spec, Title(spec, spec.Face.ToString(), ""), mark, names);
                            if (p.Kind == PieceKind.Column)
                            {
                                XYZ c0 = p.Vertices.Aggregate(XYZ.Zero, (a, b) => a + b) / p.Vertices.Count;
                                ev.Anchor = new XYZ(c0.X, c0.Y, p.Vertices.Min(q => q.Z));
                            }
                            result.Add(ev);
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
                                pv.CutZ = p.Origin.DotProduct(p.AxisZ) + z;
                                pv.CutId = id;
                                result.Add(pv);
                            }
                            break;
                        }
                        case ViewKind.ConsoloCorte:
                        case ViewKind.ConsoloPlanta:
                        case ViewKind.ConsoloCorteTransversal:
                        {
                            foreach (CorbelGeo cg in DistinctCorbels(p))
                            {
                                ViewSection v = CreateCorbelView(cg, spec.Tipo);
                                if (v == null) { log.Warn($"{spec.Titulo.Split('\n')[0]}: vista do consolo não criada."); break; }
                                PlacedView pv = Setup(v, spec, Title(spec, "", "").Replace("{Consolo}", cg.Label)
                                    .Replace("{Qtd}", cg.Count.ToString(CultureInfo.InvariantCulture)), mark, names);
                                pv.Corbel = cg.K;
                                result.Add(pv);
                            }
                            break;
                        }
                        case ViewKind.AlcaIcamento:
                        {
                            LiftLoopGeo loop = LiftLoopOf(asm, p);
                            if (loop == null) { log.Info($"{spec.Titulo.Split('\n')[0]}: peça sem alça de içamento."); break; }
                            ViewSection v = CreateLiftView(loop, p);
                            if (v == null) { log.Warn($"{spec.Titulo.Split('\n')[0]}: vista não criada."); break; }
                            PlacedView pv = Setup(v, spec, Title(spec, "", ""), mark, names);
                            pv.Loop = loop;
                            result.Add(pv);
                            break;
                        }
                        case ViewKind.Vista3D:
                        {
                            View3D v3 = AssemblyViewUtils.Create3DOrthographic(_doc, asm.Id);
                            // filtros de bitola (cores do escritório) antes do template: um template novo nasce com eles
                            int nf = RebarFilters.Apply(_doc, v3, log);
                            if (nf > 0) log.Info($"{nf} filtros de bitola na vista 3D.");
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
                if (pv.View is ViewSection vs && pv.Corbel == null && pv.Loop == null)
                {
                    try
                    {
                        // cortes/seções: só o que está na profundidade da vista, com folga pequena (cada corte ocupava
                        // ~65 mm de largura em 1:25 com todos os consolos + 300 mm e não cabia na coluna)
                        if (!double.IsNaN(pv.CutZ)) FitSectionCrop(vs, p);
                        else ViewFactory.FitCrop(vs, pts, U.Mm(_cfg.CropMarginMm));
                    }
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
            ApplyTemplate(v, spec.Template, spec.MostrarArmadura);
            // folga do recorte de anotação (padrão do Revit: 1" de papel por lado = a vista ocupa ~50 mm a mais na prancha)
            if (!(v is View3D) && Std.Folha.FolgaAnotacaoMm > 0)
                Try(() =>
                {
                    ViewCropRegionShapeManager m = v.GetCropRegionShapeManager();
                    if (m == null || !m.CanHaveAnnotationCrop) return;
                    double o = U.Mm(Math.Max(3.2, Std.Folha.FolgaAnotacaoMm));
                    m.TopAnnotationCropOffset = o; m.BottomAnnotationCropOffset = o;
                    m.LeftAnnotationCropOffset = o; m.RightAnnotationCropOffset = o;
                });
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
        private void ApplyTemplate(View v, string name, bool rebar = false)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) { if (rebar) Try(() => RebarInRed(v)); return; }
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
            if (rebar) Try(() => RebarInRed(t));
            Try(() => v.ViewTemplateId = t.Id);
        }

        private static readonly Color RebarRed = new Color(255, 0, 0);

        /// <summary>
        /// Armadura em vermelho (vergalhões e detalhes de curvatura = armadura rebatida), como na PP127: sobreposição da
        /// categoria no template (ou na vista sem template). Cor já escolhida pelo escritório no template é mantida.
        /// </summary>
        private void RebarInRed(View v)
        {
            foreach (BuiltInCategory bic in new[] { BuiltInCategory.OST_Rebar, BuiltInCategory.OST_RebarBendingDetails })
            {
                Category c = Category.GetCategory(_doc, bic);
                if (c == null || !v.IsCategoryOverridable(c.Id)) continue;
                OverrideGraphicSettings o = v.GetCategoryOverrides(c.Id);
                if (o.ProjectionLineColor.IsValid) continue;   // o escritório já escolheu a cor
                o.SetProjectionLineColor(RebarRed);
                o.SetCutLineColor(RebarRed);
                v.SetCategoryOverrides(c.Id, o);
            }
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
        // Consolos: um detalhe (corte e planta) por tipo de consolo da peça, como "ARMADURAS console" da PP127

        private sealed class CorbelGeo
        {
            public FamilyInstance K;
            public XYZ U, T, Z;                      // para fora da face, ao longo da face, eixo da peça
            public double U0, U1, T0, T1, Z0, Z1;    // face do pilar / ponta, lados, base / topo
            public double ColU0;                     // face oposta do pilar na direção U
            public string Label;                     // largura × altura × balanço (cm)
            public int Count = 1;                    // consolos iguais na peça
        }

        private static List<CorbelGeo> DistinctCorbels(PieceInfo p)
        {
            var result = new List<CorbelGeo>();
            var keys = new HashSet<string>();
            XYZ c = p.Vertices.Aggregate(XYZ.Zero, (a, b) => a + b) / p.Vertices.Count;
            string Cm(double ft) => Math.Round(UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Centimeters), 1)
                .ToString("0.#", CultureInfo.InvariantCulture);
            foreach (FamilyInstance k in p.Attached)
            {
                List<XYZ> kv = GeometryHelper.GetVertices(GeometryHelper.GetSolids(k, null));
                if (kv.Count == 0) continue;
                XYZ kc = kv.Aggregate(XYZ.Zero, (a, b) => a + b) / kv.Count;
                XYZ d = kc - c;
                XYZ u = new[] { p.AxisX, p.AxisX.Negate(), p.AxisY, p.AxisY.Negate() }.OrderByDescending(a => a.DotProduct(d)).First();
                XYZ z = p.AxisZ, t = z.CrossProduct(u);
                var g = new CorbelGeo
                {
                    K = k, U = u, T = t, Z = z,
                    U0 = p.Vertices.Max(v => v.DotProduct(u)), U1 = kv.Max(v => v.DotProduct(u)),
                    T0 = kv.Min(v => v.DotProduct(t)), T1 = kv.Max(v => v.DotProduct(t)),
                    Z0 = kv.Min(v => v.DotProduct(z)), Z1 = kv.Max(v => v.DotProduct(z)),
                    ColU0 = p.Vertices.Min(v => v.DotProduct(u))
                };
                g.Label = $"{Cm(g.T1 - g.T0)}x{Cm(g.Z1 - g.Z0)}x{Cm(g.U1 - g.U0)}";
                if (keys.Add(g.Label)) result.Add(g);
                else result.First(x => x.Label == g.Label).Count++;
            }
            return result;
        }

        /// <summary>
        /// Corte vertical pelo meio do consolo (olhando ao longo da face), planta logo abaixo do topo ou corte transversal
        /// no meio do balanço (olhando para a face; a profundidade vai só até a face, o pilar não aparece).
        /// </summary>
        private ViewSection CreateCorbelView(CorbelGeo g, ViewKind kind)
        {
            bool plan = kind == ViewKind.ConsoloPlanta;
            ViewFamilyType vft = new FilteredElementCollector(_doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.Section);
            if (vft == null) return null;
            double m = U.Mm(100);
            double tMid = (g.T0 + g.T1) / 2, w = g.T1 - g.T0, h = g.Z1 - g.Z0, c = g.U1 - g.U0;
            double back = Math.Min(g.U0 - g.ColU0, U.Mm(150));   // só um trecho do pilar atrás do consolo (como a PP127)
            var box = new BoundingBoxXYZ();
            Transform tr = Transform.Identity;
            if (kind == ViewKind.ConsoloCorteTransversal)
            {
                tr.Origin = g.U * (g.U0 + c / 2) + g.T * tMid + g.Z * ((g.Z0 + g.Z1) / 2);
                tr.BasisX = g.T; tr.BasisY = g.Z; tr.BasisZ = g.T.CrossProduct(g.Z);   // = U: olha para a face
                box.Min = new XYZ(-(w / 2 + m), -h / 2 - m, -(c / 2 - U.Mm(2)));
                box.Max = new XYZ(w / 2 + m, h / 2 + m, 0);
            }
            else if (!plan)
            {
                // plano do corte 3 cm à frente do meio do consolo: o pino central (no meio) fica visto, não "no plano"
                tr.BasisX = g.U; tr.BasisY = g.Z; tr.BasisZ = g.U.CrossProduct(g.Z);
                tr.Origin = g.U * g.U0 + g.T * tMid + g.Z * ((g.Z0 + g.Z1) / 2) + tr.BasisZ * U.Mm(30);
                box.Min = new XYZ(-back - m, -h / 2 - m, -(w / 2 + m + U.Mm(30)));
                box.Max = new XYZ(c + m, h / 2 + m, 0);
            }
            else
            {
                tr.Origin = g.U * g.U0 + g.T * tMid + g.Z * (g.Z1 - U.Mm(30));
                tr.BasisX = g.U; tr.BasisY = g.Z.CrossProduct(g.U); tr.BasisZ = g.Z;
                box.Min = new XYZ(-back - m, -(w / 2 + m), -(h + m));
                box.Max = new XYZ(c + m, w / 2 + m, 0);
            }
            box.Transform = tr;
            ViewSection v = ViewSection.CreateSection(_doc, vft.Id, box);
            v.CropBoxActive = true;
            v.CropBoxVisible = false;
            v.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE)?.Set(0);
            return v;
        }

        // ------------------------------------------------------------------
        // Alça de içamento: corte no plano da alça de cima, cotas e etiqueta (no lugar da legenda fixa)

        /// <summary>A alça mais alta da peça (posição de barra de maior cota). Null se a peça não tem alça.</summary>
        private LiftLoopGeo LiftLoopOf(AssemblyInstance asm, PieceInfo p)
        {
            LiftLoopGeo best = null;
            double bestZ = double.MinValue;
            foreach (Rebar r in asm.GetMemberIds().Select(_doc.GetElement).OfType<Rebar>())
            {
                string[] c = (r.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "").Split('|');
                if (c.Length < 3 || c[2] != "Alça de içamento") continue;
                for (int i = 0; i < r.NumberOfBarPositions; i++)
                {
                    List<Curve> cs;
                    try { cs = r.GetTransformedCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, i).ToList(); }
                    catch { continue; }
                    if (cs.Count == 0) continue;
                    double z = cs.Average(q => q.Evaluate(0.5, true).DotProduct(p.AxisZ));
                    if (z <= bestZ + U.Mm(1)) continue;
                    bestZ = z;
                    best = new LiftLoopGeo { Bar = r, Curves = cs, DiameterMm = U.ToMm((_doc.GetElement(r.GetTypeId()) as RebarBarType)?.BarNominalDiameter ?? 0) };
                }
            }
            if (best == null) return null;
            // para fora: o lado em que a alça passa da face do pilar
            List<XYZ> pts = best.Curves.SelectMany(q => new[] { q.GetEndPoint(0), q.GetEndPoint(1) }).ToList();
            best.U = new[] { p.AxisX, p.AxisX.Negate(), p.AxisY, p.AxisY.Negate() }
                .OrderByDescending(d => pts.Max(q => q.DotProduct(d)) - p.MaxAlong(d)).First();
            best.Face = p.MaxAlong(best.U);
            return best;
        }

        /// <summary>Corte no plano da alça (olhando ao longo da face), recortado em volta da alça e do trecho de face.</summary>
        private ViewSection CreateLiftView(LiftLoopGeo g, PieceInfo p)
        {
            ViewFamilyType vft = new FilteredElementCollector(_doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.Section);
            if (vft == null) return null;
            List<XYZ> pts = g.Curves.SelectMany(q => new[] { q.GetEndPoint(0), q.GetEndPoint(1), q.Evaluate(0.5, true) }).ToList();
            XYZ z = p.AxisZ, u = g.U;
            XYZ c = pts.Aggregate(XYZ.Zero, (a, b) => a + b) / pts.Count;
            var tr = Transform.Identity;
            tr.BasisX = u; tr.BasisY = z; tr.BasisZ = u.CrossProduct(z);
            // plano 3 cm à frente da alça: a alça (e a outra do mesmo ponto, logo atrás) fica vista, não cortada
            tr.Origin = c + tr.BasisZ * U.Mm(30);
            double m = U.Mm(60);
            double u0 = Math.Min(pts.Min(q => (q - c).DotProduct(u)), g.Face - c.DotProduct(u)), u1 = pts.Max(q => (q - c).DotProduct(u));
            double z0 = pts.Min(q => (q - c).DotProduct(z)), z1 = pts.Max(q => (q - c).DotProduct(z));
            var box = new BoundingBoxXYZ
            {
                Transform = tr,
                Min = new XYZ(u0 - m, z0 - m, -U.Mm(200)),
                Max = new XYZ(u1 + m, z1 + m, 0)
            };
            ViewSection v = ViewSection.CreateSection(_doc, vft.Id, box);
            v.CropBoxActive = true;
            v.CropBoxVisible = false;
            v.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE)?.Set(0);
            return v;
        }

        /// <summary>
        /// Cotas da alça como o detalhe do escritório: saída da face até o lado de fora da alça (em cima), e ao lado a
        /// cadeia perna embutida + largura + perna; embaixo a etiqueta da posição (quantidade, bitola e comprimento).
        /// As cotas pegam traços curtos de linha de detalhe nos pontos medidos (a cota de vergalhão não tem referência).
        /// </summary>
        private List<XYZ> LiftDetail(View view, LiftLoopGeo g, AnnotationStyles styles)
        {
            XYZ right = view.RightDirection, up = view.UpDirection, vd = view.ViewDirection;
            double s = view.Scale, w = view.Origin.DotProduct(vd);
            XYZ At(double r, double uu) => right * r + up * uu + vd * w;
            double sg = g.U.DotProduct(right) >= 0 ? 1 : -1;      // a alça sai para a direita (+1) ou para a esquerda
            List<XYZ> pts = g.Curves.SelectMany(q => new[] { q.GetEndPoint(0), q.GetEndPoint(1) }).ToList();
            double rFace = g.Face * g.U.DotProduct(right);
            double rad = U.Mm(g.DiameterMm / 2);
            double rOut = pts.Select(q => q.DotProduct(right)).OrderBy(x => x * sg).Last() + sg * rad;
            double rIn = pts.Select(q => q.DotProduct(right)).OrderBy(x => x * sg).First();
            // braços (trechos retos para fora da face) e pontas das pernas
            List<double> arms = g.Curves.OfType<Line>().Where(l => Math.Abs(l.Direction.DotProduct(right)) > 0.99)
                .Select(l => l.GetEndPoint(0).DotProduct(up)).OrderBy(x => x).ToList();
            double zA = pts.Min(q => q.DotProduct(up)), zD = pts.Max(q => q.DotProduct(up));
            double tick = U.Mm(1.5) * s, off = U.Mm(8) * s;
            var used = new List<XYZ>(pts);
            Reference Tick(double r, double uu, bool horizontal)
            {
                used.Add(At(r, uu));
                Line ln = horizontal ? Line.CreateBound(At(r - tick, uu), At(r + tick, uu)) : Line.CreateBound(At(r, uu - tick), At(r, uu + tick));
                return _doc.Create.NewDetailCurve(view, ln).GeometryCurve.Reference;
            }
            void Dim(Line line, params Reference[] refs)
            {
                var ra = new ReferenceArray();
                foreach (Reference rf in refs) ra.Append(rf);
                Try(() => _doc.Create.NewDimension(view, line, ra, styles.Dimension));
            }
            // saída da face: em cima da alça
            double uTop = zD + off;
            Dim(Line.CreateBound(At(rFace, uTop), At(rOut, uTop)), Tick(rFace, uTop, false), Tick(rOut, uTop, false));
            // perna + largura + perna: do lado de dentro da peça
            double rSide = (sg > 0 ? Math.Min(rIn, rFace) : Math.Max(rIn, rFace)) - sg * off;
            var zs = new List<double> { zA };
            if (arms.Count >= 2) { zs.Add(arms[0]); zs.Add(arms[^1]); }
            zs.Add(zD);
            Dim(Line.CreateBound(At(rSide, zA), At(rSide, zD)), zs.Select(zz => Tick(rSide, zz, true)).ToArray());
            // etiqueta da posição embaixo
            FamilySymbol tag = RebarTagType(Std.Armadura.EtiquetaLongitudinal);
            XYZ at = At((rFace + rOut) / 2, zA - off * 1.5);
            used.Add(at - up * off * 0.5);
            used.Add(At(rSide - sg * off, zA));   // texto da cota da cadeia
            used.Add(At(rFace, uTop + off));       // texto da cota de cima
            if (tag == null || TagRebar(tag, view, g.Bar, false, TagOrientation.Horizontal, at) == null)
            {
                var row = new RebarRow(_doc, g.Bar);
                Try(() => TextNote.Create(_doc, view.Id, at, Fill(Std.Armadura.RotuloBarra, row),
                    new TextNoteOptions(styles.Text.Id) { HorizontalAlignment = HorizontalTextAlignment.Center }));
            }
            return used;
        }

        // ------------------------------------------------------------------
        // Níveis (topo, níveis do projeto que cruzam a peça, base) e comprimento total

        private readonly Dictionary<ElementId, List<XYZ>> _schematic = new Dictionary<ElementId, List<XYZ>>();
        private GraphicsStyle _levelLineStyle;
        private bool _levelLineStyleLogged;

        /// <summary>Estilo das linhas de nível: traço-ponto (Centro/Center) se existir; senão "Linhas médias".</summary>
        private GraphicsStyle LevelLineStyle(DetailCurve dc, PieceLog log)
        {
            if (_levelLineStyle != null) return _levelLineStyle;
            List<GraphicsStyle> all = dc.GetLineStyleIds().Select(id => _doc.GetElement(id)).OfType<GraphicsStyle>().ToList();
            bool Has(GraphicsStyle g, params string[] k) => k.Any(x => g.Name.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
            _levelLineStyle = all.FirstOrDefault(g => Has(g, "traço-ponto", "traco-ponto", "dash dot", "dash-dot"))
                              ?? all.FirstOrDefault(g => Has(g, "centro", "center"))
                              ?? all.FirstOrDefault(g => Has(g, "dia", "medium"));
            if (!_levelLineStyleLogged)
            {
                _levelLineStyleLogged = true;
                log?.Info($"Linhas de nível: estilo \"{_levelLineStyle?.Name ?? "(padrão)"}\" (disponíveis: {string.Join(", ", all.Select(g => g.Name))}).");
            }
            return _levelLineStyle;
        }

        /// <summary>
        /// Marcas de corte simples: dos dois lados da vista, na altura de cada corte, um traço grosso curto (5 mm no papel)
        /// com a letra/número em cima. Os lados são os da peça com os consolos (e o desenho esquemático, se houver).
        /// </summary>
        private List<XYZ> CutMarks(View view, PieceInfo p, List<PlacedView> cuts, AnnotationStyles styles, List<XYZ> extra)
        {
            var marks = new List<XYZ>();
            if (cuts.Count == 0) return marks;
            XYZ right = view.RightDirection, up = view.UpDirection, vd = view.ViewDirection;
            if (Math.Abs(p.AxisZ.DotProduct(up)) < 0.99) return marks;   // só elevações
            double s = view.Scale, d = view.Origin.DotProduct(vd);
            XYZ At(double r, double u) => right * r + up * u + vd * d;
            var pts = new List<XYZ>(p.Vertices);
            foreach (FamilyInstance k in p.Attached) pts.AddRange(GeometryHelper.GetVertices(GeometryHelper.GetSolids(k, null)));
            if (extra != null) pts.AddRange(extra);
            double minR = pts.Min(q => q.DotProduct(right)), maxR = pts.Max(q => q.DotProduct(right));
            // a marca fica para trás das cotas (fora delas), não em cima dos textos: cotas que encostam na peça
            foreach (Dimension dm in new FilteredElementCollector(_doc, view.Id).OfClass(typeof(Dimension)).Cast<Dimension>())
            {
                BoundingBoxXYZ bb = dm.get_BoundingBox(view);
                if (bb == null) continue;
                List<XYZ> c = Corners(bb);
                double lo = c.Min(q => q.DotProduct(right)), hi = c.Max(q => q.DotProduct(right));
                double tall = c.Max(q => q.DotProduct(up)) - c.Min(q => q.DotProduct(up));
                bool vertical = tall > hi - lo;   // cota vertical (os textos com chamada avançam sobre a peça)
                if (vertical && hi > maxR && hi < maxR + U.Mm(40) * s) maxR = Math.Max(maxR, hi);
                if (vertical && lo < minR && lo > minR - U.Mm(12) * s) minR = Math.Min(minR, lo);
            }
            double len = U.Mm(5) * s, gap = U.Mm(2) * s, textH = U.Mm(Std.Textos.AlturaTextoMm) * s;
            var center = new TextNoteOptions(styles.Text.Id) { HorizontalAlignment = HorizontalTextAlignment.Center };
            GraphicsStyle thick = null;
            foreach (PlacedView c in cuts)
            {
                double u = (p.AxisZ * c.CutZ).DotProduct(up);
                foreach (double r0 in new[] { minR - gap - len, maxR + gap })
                {
                    Try(() =>
                    {
                        DetailCurve dc = _doc.Create.NewDetailCurve(view, Line.CreateBound(At(r0, u), At(r0 + len, u)));
                        thick ??= dc.GetLineStyleIds().Select(id => _doc.GetElement(id)).OfType<GraphicsStyle>()
                            .FirstOrDefault(g => g.Name.IndexOf("grossa", StringComparison.OrdinalIgnoreCase) >= 0 || g.Name.IndexOf("Wide", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (thick != null) dc.LineStyle = thick;
                        TextNote.Create(_doc, view.Id, At(r0 + len / 2, u + textH * 1.6), c.CutId, center);
                    });
                    marks.Add(At(r0 - gap, u)); marks.Add(At(r0 + len + gap, u + textH * 2.4));
                }
            }
            return marks;
        }

        private XYZ LevelMarks(View view, PieceInfo p, AnnotationStyles styles, bool labels, PieceLog log, List<XYZ> extra = null)
        {
            XYZ right = view.RightDirection, up = view.UpDirection, vd = view.ViewDirection;
            double scale = view.Scale;
            double w = view.Origin.DotProduct(vd);
            XYZ At(double r, double u) => right * r + up * u + vd * w;
            // extra = pontos do desenho esquemático: as linhas de nível atravessam o desenho todo
            IEnumerable<XYZ> span = extra == null ? p.Vertices : p.Vertices.Concat(extra);
            double minR = span.Min(v => v.DotProduct(right));
            double maxR = span.Max(v => v.DotProduct(right));
            double bottom = p.Vertices.Min(v => v.DotProduct(up)), top = p.Vertices.Max(v => v.DotProduct(up));
            double zBottom = p.Vertices.Min(v => v.Z), zTop = p.Vertices.Max(v => v.Z);
            // cota vertical da vista ↔ Z do modelo (a vista de face é vertical: up = +Z)
            double Vz(double z) => bottom + (z - zBottom) * (up.Z >= 0 ? 1 : -1);

            double tick = U.Mm(25) * scale;             // traço do nível até a peça
            double x0 = minR - U.Mm(14) * scale;        // fim do traço (afastado das cotas da vista)
            double textX = x0 - tick;
            var opt = new TextNoteOptions(styles.Text.Id) { HorizontalAlignment = HorizontalTextAlignment.Left };

            // com a legenda de níveis na folha, os nomes dos níveis do projeto ficam nela; na vista, só a linha. Topo/base
            // do pilar num nível do projeto: o nome sai da legenda (sem rótulo repetido na vista)
            bool legend = Std.Folha.LegendaNiveis;
            List<double> levelZ = new FilteredElementCollector(_doc).OfClass(typeof(Level)).Cast<Level>().Select(l => l.Elevation).ToList();
            string OwnLabel(string n, double z) => legend && levelZ.Any(e => Math.Abs(e - z) < U.Mm(5)) ? null : n;
            var marks = new List<(string Name, double Z)> { (OwnLabel("TOPO PILAR\n(TOP COLUMN)", zTop), zTop) };
            foreach (Level lv in new FilteredElementCollector(_doc).OfClass(typeof(Level)).Cast<Level>())
                if (lv.Elevation > zBottom + U.Mm(5) && lv.Elevation < zTop - U.Mm(5))
                    marks.Add((legend ? null : lv.Name.ToUpperInvariant(), lv.Elevation));
            marks.Add((OwnLabel("BASE PILAR\n(COLUMN BASE)", zBottom), zBottom));

            foreach (var (name, z) in marks)
            {
                double u = Vz(z);
                Try(() =>
                {
                    // atravessa a peça (e os consolos) até um pouco além da face direita
                    double r0 = labels && (name != null || !legend) ? textX : minR - U.Mm(8) * scale;
                    DetailCurve dc = _doc.Create.NewDetailCurve(view, Line.CreateBound(At(r0, u), At(maxR + U.Mm(8) * scale, u)));
                    GraphicsStyle gs = LevelLineStyle(dc, log);
                    if (gs != null) dc.LineStyle = gs;
                });
                if (!labels || name == null) continue;
                double zm = Math.Round(UnitUtils.ConvertFromInternalUnits(z, UnitTypeId.Meters), 2);
                string elev = zm == 0 ? "0,00" : zm.ToString("+0.00;-0.00", CultureInfo.GetCultureInfo("pt-BR"));
                double lineH = U.Mm(Std.Textos.AlturaTextoMm * 1.9) * scale;
                // cota logo acima da linha; o nome (1 ou 2 linhas) acima da cota
                int nameLines = name.Split('\n').Length;
                Try(() => TextNote.Create(_doc, view.Id, At(textX, u + lineH * (1.0 + nameLines)), name, opt));
                Try(() => TextNote.Create(_doc, view.Id, At(textX, u + lineH * 1.0), elev, opt));
            }

            XYZ lineStart = At(minR - U.Mm(8) * scale, bottom);
            if (!labels) return lineStart;
            // comprimento total (texto vertical à esquerda dos níveis, sem encostar nos nomes)
            double lenCm = Math.Round(U_ToCm(zTop - zBottom));
            var vert = new TextNoteOptions(styles.Text.Id) { HorizontalAlignment = HorizontalTextAlignment.Center, Rotation = Math.PI / 2 };
            Try(() => TextNote.Create(_doc, view.Id, At(textX - U.Mm(13) * scale, (bottom + top) / 2),
                $"COMPRIMENTO TOTAL DO PILAR {lenCm:0}\n(TOTAL COLUMN LENGTH {lenCm:0})", vert));
            return lineStart;
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

        // ------------------------------------------------------------------
        // Elevação esquemática da armadura ("explodida"), como o template de pilar do DEB.Cad

        /// <summary>
        /// Cada posição longitudinal vira uma linha grossa vertical (da cota mais baixa à mais alta das barras), lado a
        /// lado a partir da face esquerda, com o rótulo vertical "4 N1 ø12.5 C=695" à esquerda. À direita, uma linha de
        /// cota com traços nos limites de cada trecho de estribos e o rótulo vertical do trecho ("71 N1 c/15 + 2x71 N3").
        /// Devolve os pontos do desenho (para o recorte e as linhas de nível).
        /// </summary>
        private List<XYZ> SchematicElevation(View view, PieceInfo p, List<Rebar> rebars, AnnotationStyles styles)
        {
            XYZ right = view.RightDirection, up = view.UpDirection, vd = view.ViewDirection;
            double s = view.Scale, w = view.Origin.DotProduct(vd);
            XYZ At(double r, double u) => right * r + up * u + vd * w;
            double minR = p.Vertices.Min(v => v.DotProduct(right));
            double textLine = U.Mm(Std.Textos.AlturaTextoMm * 1.7) * s;
            var pts = new List<XYZ>();

            (double Lo, double Hi) Span(IEnumerable<Rebar> rs)
            {
                double lo = double.MaxValue, hi = double.MinValue;
                foreach (Rebar r in rs)
                {
                    BoundingBoxXYZ bb = r.get_BoundingBox(null);
                    if (bb == null) continue;
                    foreach (double x in new[] { bb.Min.X, bb.Max.X })
                        foreach (double y in new[] { bb.Min.Y, bb.Max.Y })
                            foreach (double z in new[] { bb.Min.Z, bb.Max.Z })
                            {
                                double u = new XYZ(x, y, z).DotProduct(up);
                                lo = Math.Min(lo, u); hi = Math.Max(hi, u);
                            }
                }
                return (lo, hi);
            }

            GraphicsStyle thick = null, thin = null;
            DetailCurve Line_(double r0, double u0, double r1, double u1, bool heavy)
            {
                pts.Add(At(r0, u0)); pts.Add(At(r1, u1));
                DetailCurve dc = _doc.Create.NewDetailCurve(view, Line.CreateBound(At(r0, u0), At(r1, u1)));
                if (thick == null)
                {
                    List<GraphicsStyle> all = dc.GetLineStyleIds().Select(id => _doc.GetElement(id)).OfType<GraphicsStyle>().ToList();
                    thick = all.FirstOrDefault(g => g.Name.IndexOf("grossa", StringComparison.OrdinalIgnoreCase) >= 0 || g.Name.IndexOf("Wide", StringComparison.OrdinalIgnoreCase) >= 0);
                    thin = all.FirstOrDefault(g => g.Name.IndexOf("finas", StringComparison.OrdinalIgnoreCase) >= 0 || g.Name.IndexOf("Thin", StringComparison.OrdinalIgnoreCase) >= 0);
                }
                GraphicsStyle gs = heavy ? thick : thin;
                if (gs != null) Try(() => dc.LineStyle = gs);
                return dc;
            }
            // texto vertical (lido de baixo para cima): o ponto é o meio do lado de cima do texto, que fica à esquerda
            var vertical = new TextNoteOptions(styles.Text.Id) { HorizontalAlignment = HorizontalTextAlignment.Center, Rotation = Math.PI / 2 };
            void VText(double r, double u, string text)
            {
                pts.Add(At(r, u)); pts.Add(At(r + textLine, u));
                Try(() => TextNote.Create(_doc, view.Id, At(r, u), text, vertical));
            }

            var rows = rebars.Select(r => new RebarRow(_doc, r)).Where(r => r.Number != "").ToList();

            // Longitudinais: detalhe de curvatura em escala, na altura real, com a etiqueta do escritório (recurso do Revit);
            // sem a família de etiqueta, ou com a opção desligada, a linha esquemática + texto de antes.
            double pitch = U.Mm(11) * s;
            double x = minR + pitch;
            RebarBendingDetailType full = Std.Armadura.LongitudinaisComoDetalhe ? RealisticType(styles) : null;
            foreach (var g in rows.Where(r => r.Role == "Longitudinal").GroupBy(r => r.Number).OrderBy(g => NumberKey(g.Key)))
            {
                var (lo, hi) = Span(g.Select(r => r.Rebar));
                if (lo >= hi) continue;
                if (full != null && BendingDetailAlong(view, g.OrderByDescending(r => r.Quantity).First().Rebar, full, x, (lo + hi) / 2, pts, out double dw))
                {
                    x += Math.Max(pitch, dw + U.Mm(6) * s);
                    continue;
                }
                Line_(x, lo, x, hi, heavy: true);
                // texto vertical: o texto cresce para a direita do ponto (uma linha de texto)
                VText(x - U.Mm(3) * s - textLine, (lo + hi) / 2, Fill(Std.Armadura.RotuloBarra, new RebarRow(g.First(), g.Sum(r => r.Quantity))));
                x += pitch;
            }

            // Alças de içamento: etiqueta do escritório com linha de chamada, à esquerda da peça, na altura da alça
            // (a forma está na legenda "ALÇA DE IÇAMENTO"; o detalhe de dobra ao lado da elevação saiu).
            FamilySymbol barTag = full != null ? RebarTagType(Std.Armadura.EtiquetaLongitudinal) : null;
            if (barTag != null)
                foreach (var g in rows.Where(r => r.Role == "Alça de içamento").GroupBy(r => r.Number))
                {
                    RebarRow lr = g.OrderBy(r => r.Center.DotProduct(up)).Last();
                    // altura da alça de cima do conjunto (o centro do conjunto fica entre as alças)
                    double lu = lr.Center.DotProduct(up);
                    Try(() =>
                    {
                        lu = Enumerable.Range(0, lr.Rebar.NumberOfBarPositions).Max(i =>
                        {
                            IList<Curve> cv = lr.Rebar.GetTransformedCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, i);
                            return cv.Average(c => c.Evaluate(0.5, true).DotProduct(up));
                        });
                    });
                    // vertical, encostada na face esquerda, na altura da alça de cima (não alarga a vista)
                    XYZ head = At(minR - U.Mm(3) * s - textLine * 0.5, lu);
                    TagRebar(barTag, view, lr.Rebar, false, TagOrientation.Vertical, head);
                    pts.Add(head - right * textLine);
                }

            // Estribos: cota dos trechos (uma cadeia) e, encostada nela, a etiqueta do conjunto de estribos (e a dos grampos)
            // — recurso do Revit, a quantidade e o espaçamento vêm do conjunto. Sem a família de etiqueta: o texto de antes.
            var runs = rows.Where(r => r.Role == "Estribo").Select(r => (Row: r, Span: Span(new[] { r.Rebar })))
                .Where(t => t.Span.Lo < t.Span.Hi).OrderBy(t => t.Span.Lo).ToList();
            FamilySymbol stirrupTag = full != null ? RebarTagType(Std.Armadura.EtiquetaEstribo) : null;
            FamilySymbol breakTag = stirrupTag != null && !string.IsNullOrWhiteSpace(Std.Armadura.EtiquetaEstriboQuebra)
                ? RebarTagType(Std.Armadura.EtiquetaEstriboQuebra) : null;
            if (runs.Count > 0 && stirrupTag != null)
            {
                double xs = x + U.Mm(6) * s, tick = U.Mm(1.5) * s;
                double charW = U.Mm(Std.Textos.AlturaTextoMm * 0.8) * s;
                // divisas: meio do vão entre o último estribo de um trecho e o primeiro do seguinte
                var cuts = new List<double> { runs[0].Span.Lo };
                for (int i = 0; i < runs.Count - 1; i++) cuts.Add((runs[i].Span.Hi + runs[i + 1].Span.Lo) / 2);
                cuts.Add(runs[^1].Span.Hi);
                var refs = new ReferenceArray();
                foreach (double cu in cuts)
                {
                    DetailCurve dc = Line_(xs - tick, cu, xs + tick, cu, heavy: false);
                    refs.Append(dc.GeometryCurve.Reference);
                }
                Try(() => _doc.Create.NewDimension(view, Line.CreateBound(At(xs, cuts[0]), At(xs, cuts[^1])), refs, styles.Dimension));
                double nextTop = double.MaxValue;
                foreach (var (row, (lo, hi)) in runs.OrderByDescending(t => t.Span.Hi))
                {
                    double mid = (lo + hi) / 2;
                    List<RebarRow> ties = rows.Where(t => t.Role == "Grampo" && Math.Abs(t.Center.DotProduct(up) - row.Center.DotProduct(up)) < U.Mm(20)).ToList();
                    var tagged = new List<Rebar> { row.Rebar };
                    if (ties.Count > 0) tagged.Add(ties[0].Rebar);
                    // "x 16 N2 c/15.00" ~ 16 caracteres; cabe no trecho: vertical, colado na cota; senão horizontal, à direita
                    double len = 16 * charW;
                    bool vert = len < (hi - lo) * 0.9;   // vertical, colada na cota (estribo e grampo lado a lado)
                    // trecho curto: etiqueta em duas linhas ("x 9 N3" / "c/7.50"), também vertical e lado a lado — como o
                    // escritório ajustou na prancha; um estribo só (sem espaçamento) fica na horizontal
                    bool brk = !vert && breakTag != null && row.Quantity > 1 && 8 * charW < (hi - lo) * 0.9;
                    for (int k = 0; k < tagged.Count; k++)
                    {
                        Rebar rb = tagged[k];
                        XYZ at;
                        if (vert)
                            at = At(xs + U.Mm(3) * s + textLine * (0.5 + 1.3 * k), mid);
                        else if (brk)
                            at = At(xs + U.Mm(3) * s + textLine * (1.0 + 1.9 * k), mid);
                        else
                        {
                            double top = Math.Min(mid + textLine / 2, nextTop);
                            at = At(xs + U.Mm(4) * s + len / 2, top - textLine / 2);
                            nextTop = top - textLine * 1.25;
                        }
                        TagRebar(brk ? breakTag : stirrupTag, view, rb, false, vert || brk ? TagOrientation.Vertical : TagOrientation.Horizontal, at);
                        pts.Add(at); pts.Add(at + right * (vert ? textLine : brk ? textLine * 1.2 : len / 2 + U.Mm(2) * s));
                    }
                }
            }
            else if (runs.Count > 0)
            {
                double xs = x + U.Mm(6) * s, tick = U.Mm(1.5) * s;
                double charW = U.Mm(Std.Textos.AlturaTextoMm * 0.8) * s;   // largura média de um caractere
                Line_(xs, runs.First().Span.Lo, xs, runs.Max(t => t.Span.Hi), heavy: false);
                var horiz = new TextNoteOptions(styles.Text.Id) { HorizontalAlignment = HorizontalTextAlignment.Left };
                double nextTop = double.MaxValue;   // rótulos horizontais: de cima para baixo, sem sobrepor
                foreach (var (row, (lo, hi)) in runs.OrderByDescending(t => t.Span.Hi))
                {
                    Line_(xs - tick, lo, xs + tick, lo, heavy: false);
                    Line_(xs - tick, hi, xs + tick, hi, heavy: false);
                    string text = row.Quantity <= 1 ? $"{row.Quantity} N{row.Number}" : Fill(Std.Armadura.RotuloEstribos, row);
                    List<RebarRow> ties = rows.Where(t => t.Role == "Grampo" && Math.Abs(t.Center.DotProduct(up) - row.Center.DotProduct(up)) < U.Mm(20)).ToList();
                    if (ties.Count > 0) text += $" + {ties.Count}x{ties[0].Quantity} N{ties[0].Number}";
                    if (text.Length * charW < (hi - lo) * 0.9)
                        VText(xs + U.Mm(1.5) * s + textLine, (lo + hi) / 2, text);
                    else
                    {
                        double top = Math.Min((lo + hi) / 2 + textLine / 2, nextTop);
                        double r0 = xs + U.Mm(2.5) * s;
                        Try(() => TextNote.Create(_doc, view.Id, At(r0, top), text, horiz));
                        pts.Add(At(r0, top)); pts.Add(At(r0 + text.Length * charW, top - textLine));
                        nextTop = top - textLine * 1.2;
                    }
                }
            }
            return pts;
        }

        // ------------------------------------------------------------------
        // Chamadas N nas seções com armadura (como o template do CAD)

        /// <summary>
        /// Barras que atravessam a seção (retas na direção da vista, dentro da profundidade): as de canto ganham chamada
        /// própria na diagonal; as demais são agrupadas por lado e por N — um texto "N1" fora do concreto com uma linha de
        /// chamada para cada barra.
        /// </summary>
        private void SectionCallouts(View view, PieceInfo p, List<Rebar> rebars, AnnotationStyles styles, PieceLog log)
        {
            XYZ right = view.RightDirection, up = view.UpDirection, vd = view.ViewDirection;
            double s = view.Scale, d = view.Origin.DotProduct(vd);
            double far = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR)?.AsDouble() ?? U.Mm(1000);
            XYZ At(double r, double u) => right * r + up * u + vd * d;
            double minR = p.Vertices.Min(v => v.DotProduct(right)), maxR = p.Vertices.Max(v => v.DotProduct(right));
            double minU = p.Vertices.Min(v => v.DotProduct(up)), maxU = p.Vertices.Max(v => v.DotProduct(up));
            double cr = (minR + maxR) / 2, cu = (minU + maxU) / 2, hw = (maxR - minR) / 2, hh = (maxU - minU) / 2;
            if (hw <= 0 || hh <= 0) return;

            // pontos das barras na seção
            var bars = new List<(string N, double R, double U)>();
            foreach (Rebar r in rebars)
            {
                string n = r.get_Parameter(BuiltInParameter.REBAR_NUMBER)?.AsString() ?? "";
                if (n == "") continue;
                // barras do consolo são chamadas no quadro do consolo (na seção ficariam amontoadas)
                if (IsCorbelRole(new RebarRow(_doc, r).Role)) continue;
                for (int i = 0; i < r.NumberOfBarPositions; i++)
                {
                    IList<Curve> cs;
                    try { cs = r.GetTransformedCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, i); }
                    catch { continue; }
                    foreach (Line ln in cs.OfType<Line>())
                    {
                        if (Math.Abs(ln.Direction.DotProduct(vd)) < 0.95) continue;   // só barras que atravessam a seção
                        XYZ a = ln.GetEndPoint(0), b = ln.GetEndPoint(1);
                        double da = a.DotProduct(vd), db = b.DotProduct(vd);
                        if (Math.Max(da, db) < d - far || Math.Min(da, db) > d + U.Mm(5)) continue;
                        XYZ q = Math.Abs(db - da) < 1e-9 ? a : a + (b - a) * Math.Max(0, Math.Min(1, (d - da) / (db - da)));
                        bars.Add((n, q.DotProduct(right), q.DotProduct(up)));
                        break;
                    }
                }
            }
            if (bars.Count == 0) { log.Info($"{view.Name}: nenhuma barra atravessa a seção (sem chamadas)."); return; }

            double off = U.Mm(5) * s, textH = U.Mm(Std.Textos.AlturaTextoMm * 1.6) * s;
            // barras fora do pilar (consolo): o texto vai além delas
            double cover = U.Mm(50);   // da barra à face do consolo
            minR = Math.Min(minR, bars.Min(b => b.R) - cover); maxR = Math.Max(maxR, bars.Max(b => b.R) + cover);
            minU = Math.Min(minU, bars.Min(b => b.U) - cover); maxU = Math.Max(maxU, bars.Max(b => b.U) + cover);
            // consolos cortados (ou vistos) pela seção: o texto vai além do concreto deles
            foreach (FamilyInstance k in p.Attached)
            {
                BoundingBoxXYZ bb = k.get_BoundingBox(null);
                if (bb == null) continue;
                var corners = new List<XYZ>();
                foreach (double x in new[] { bb.Min.X, bb.Max.X })
                    foreach (double y in new[] { bb.Min.Y, bb.Max.Y })
                        foreach (double z in new[] { bb.Min.Z, bb.Max.Z })
                            corners.Add(bb.Transform.OfPoint(new XYZ(x, y, z)));
                double lo = corners.Min(c => c.DotProduct(vd)), hi = corners.Max(c => c.DotProduct(vd));
                if (hi < d - far || lo > d + U.Mm(5)) continue;
                minR = Math.Min(minR, corners.Min(c => c.DotProduct(right))); maxR = Math.Max(maxR, corners.Max(c => c.DotProduct(right)));
                minU = Math.Min(minU, corners.Min(c => c.DotProduct(up))); maxU = Math.Max(maxU, corners.Max(c => c.DotProduct(up)));
            }
            var leftOpt = new TextNoteOptions(styles.Text.Id) { HorizontalAlignment = HorizontalTextAlignment.Right };
            var rightOpt = new TextNoteOptions(styles.Text.Id) { HorizontalAlignment = HorizontalTextAlignment.Left };
            var centerOpt = new TextNoteOptions(styles.Text.Id) { HorizontalAlignment = HorizontalTextAlignment.Center };

            void Callout(XYZ textAt, TextNoteOptions opt, string text, IEnumerable<(double R, double U)> targets)
            {
                Try(() =>
                {
                    TextNote tn = TextNote.Create(_doc, view.Id, textAt, text, opt);
                    foreach (var (r, u) in targets)
                    {
                        Leader l = tn.AddLeader(TextNoteLeaderTypes.TNLT_STRAIGHT_L);
                        l.End = At(r, u);
                    }
                });
            }

            // cantos: chamada própria na diagonal, para fora
            bool Corner((string N, double R, double U) b) => Math.Abs(b.R - cr) > hw * 0.6 && Math.Abs(b.U - cu) > hh * 0.6;
            foreach (var b in bars.Where(Corner))
            {
                double sr = Math.Sign(b.R - cr), su = Math.Sign(b.U - cu);
                XYZ at = At((sr > 0 ? maxR : minR) + sr * off, (su > 0 ? maxU : minU) + su * off + (su > 0 ? textH : 0));
                Callout(at, sr > 0 ? rightOpt : leftOpt, "N" + b.N, new[] { (b.R, b.U) });
            }

            // lados: agrupadas por (lado, N)
            string Side((string N, double R, double U) b) =>
                Math.Abs(b.R - cr) / hw >= Math.Abs(b.U - cu) / hh ? (b.R > cr ? "D" : "E") : (b.U > cu ? "C" : "B");
            // textos de um mesmo lado não se sobrepõem: o seguinte anda uma linha (E/D para baixo; C/B para o lado)
            var used = new Dictionary<string, List<double>>();
            double Free(string side, double v, double step)
            {
                if (!used.TryGetValue(side, out List<double> l)) used[side] = l = new List<double>();
                while (l.Any(x => Math.Abs(x - v) < Math.Abs(step) * 0.95)) v += step;
                l.Add(v);
                return v;
            }
            double charW = U.Mm(Std.Textos.AlturaTextoMm * 0.9) * s;
            foreach (var g in bars.Where(b => !Corner(b)).GroupBy(b => (Side(b), b.N)).OrderBy(g => NumberKey(g.Key.N)))
            {
                var pts = g.Select(b => (b.R, b.U)).ToList();
                double mr = pts.Average(t => t.R), mu = pts.Average(t => t.U);
                string text = "N" + g.Key.N, side = g.Key.Item1;
                double tw = (text.Length + 1) * charW;
                switch (side)
                {
                    case "E": Callout(At(minR - off * 1.6, Free(side, mu + textH / 2, -textH * 1.2)), leftOpt, text, pts); break;
                    case "D": Callout(At(maxR + off * 1.6, Free(side, mu + textH / 2, -textH * 1.2)), rightOpt, text, pts); break;
                    case "C": Callout(At(Free(side, mr, tw), maxU + off * 1.6 + textH), centerOpt, text, pts); break;
                    default: Callout(At(Free(side, mr, tw), minU - off * 1.6), centerOpt, text, pts); break;
                }
            }
        }

        /// <summary>
        /// Chamadas N no quadro do consolo (como o "ARMADURAS console"): no corte, todas as barras do consolo (e "Solda"
        /// na barra soldada); na planta, só os pinos. Texto fora do recorte, do lado mais próximo, com uma linha de chamada
        /// para cada barra visível do conjunto (até 6).
        /// </summary>
        private void CorbelCallouts(View view, FamilyInstance corbel, bool plan, List<Rebar> rebars, AnnotationStyles styles, PieceLog log)
        {
            if (!(view is ViewSection vs)) return;
            // só as barras deste consolo (o recorte pega também um trecho do pilar, onde passam as dos outros)
            BoundingBoxXYZ kb = corbel.get_BoundingBox(null);
            if (kb == null) return;
            double tol = U.Mm(30);
            bool InCorbel(XYZ q) => q.X > kb.Min.X - tol && q.X < kb.Max.X + tol && q.Y > kb.Min.Y - tol && q.Y < kb.Max.Y + tol
                                    && q.Z > kb.Min.Z - tol && q.Z < kb.Max.Z + tol;
            XYZ right = view.RightDirection, up = view.UpDirection, vd = view.ViewDirection;
            double s = view.Scale;
            BoundingBoxXYZ cb = vs.CropBox;
            var corners = new List<XYZ>();
            foreach (double x in new[] { cb.Min.X, cb.Max.X })
                foreach (double y in new[] { cb.Min.Y, cb.Max.Y })
                    foreach (double z in new[] { cb.Min.Z, cb.Max.Z })
                        corners.Add(cb.Transform.OfPoint(new XYZ(x, y, z)));
            double minR = corners.Min(c => c.DotProduct(right)), maxR = corners.Max(c => c.DotProduct(right));
            double minU = corners.Min(c => c.DotProduct(up)), maxU = corners.Max(c => c.DotProduct(up));
            double minD = corners.Min(c => c.DotProduct(vd)), maxD = corners.Max(c => c.DotProduct(vd));
            double d0 = view.Origin.DotProduct(vd);
            XYZ At(double r, double u) => right * r + up * u + vd * d0;
            bool Inside(XYZ q)
            {
                double r = q.DotProduct(right), u = q.DotProduct(up), dd = q.DotProduct(vd);
                return r > minR && r < maxR && u > minU && u < maxU && dd > minD - U.Mm(15) && dd < maxD + U.Mm(15);   // barra no plano de corte
            }

            // por N: pontos de chamada (um por posição visível, no meio do trecho reto visível mais longo)
            var groups = new List<(string N, string Role, List<(double R, double U)> Pts)>();
            foreach (Rebar r in rebars)
            {
                var row = new RebarRow(_doc, r);
                if (row.Number == "" || !IsCorbelRole(row.Role)) continue;
                if (plan && row.Role.IndexOf("Pino", StringComparison.OrdinalIgnoreCase) < 0) continue;
                bool mine = false;
                try
                {
                    mine = r.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0)
                        .Any(c => Enumerable.Range(0, 11).Any(k => InCorbel(c.Evaluate(k / 10.0, true))));
                }
                catch { }
                if (!mine) continue;
                var pts = new List<(double, double)>();
                for (int i = 0; i < r.NumberOfBarPositions && pts.Count < 6; i++)
                {
                    IList<Curve> cs;
                    try
                    {
                        // curvas da barra i já na posição dela (conjunto com forma ou de forma livre)
                        cs = r.GetTransformedCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, i);
                    }
                    catch { continue; }
                    XYZ best = null; double bestL = 0;
                    foreach (Line ln in cs.OfType<Line>())
                    {
                        XYZ a = ln.GetEndPoint(0), b = ln.GetEndPoint(1);
                        // comprimento projetado no plano da vista: linha "de ponta" não serve de alvo
                        XYZ ab = b - a; double lp = (ab - vd * ab.DotProduct(vd)).GetLength();
                        if (lp < U.Mm(20) || lp <= bestL) continue;
                        // trecho dentro do recorte (amostragem): o ponto do meio dele
                        List<XYZ> ins = Enumerable.Range(1, 9).Select(k => a + ab * (k / 10.0)).Where(Inside).ToList();
                        if (ins.Count == 0) continue;
                        best = ins[ins.Count / 2]; bestL = lp;
                    }
                    if (best == null)   // barra vista "de ponta" (pino na planta): o ponto dela
                        foreach (Line ln in cs.OfType<Line>())
                        {
                            XYZ a = ln.GetEndPoint(0), b = ln.GetEndPoint(1);
                            double da = a.DotProduct(vd), db = b.DotProduct(vd);
                            if (Math.Abs(db - da) < 1e-9) continue;
                            XYZ q = a + (b - a) * Math.Max(0, Math.Min(1, (maxD - da) / (db - da)));   // no plano de corte
                            if (Inside(q)) { best = q; break; }
                        }
                    // barras uma atrás da outra na vista: um alvo só
                    if (best != null && !pts.Any(t => Math.Abs(t.Item1 - best.DotProduct(right)) < U.Mm(5) && Math.Abs(t.Item2 - best.DotProduct(up)) < U.Mm(5)))
                        pts.Add((best.DotProduct(right), best.DotProduct(up)));
                }
                if (pts.Count == 0) { log.Info($"{view.Name}: N{row.Number} ({row.Role}) não visível no recorte (sem chamada)."); continue; }
                var gExisting = groups.FindIndex(g => g.N == row.Number);
                if (gExisting >= 0) groups[gExisting].Pts.AddRange(pts.Take(Math.Max(0, 6 - groups[gExisting].Pts.Count)));
                else groups.Add((row.Number, row.Role, pts));
            }
            if (groups.Count == 0) { log.Info($"{view.Name}: nenhuma barra do consolo visível (sem chamadas)."); return; }

            double off = U.Mm(5) * s, lineH = U.Mm(Std.Textos.AlturaTextoMm * 1.6) * s;
            var leftOpt = new TextNoteOptions(styles.Text.Id) { HorizontalAlignment = HorizontalTextAlignment.Right };
            var rightOpt = new TextNoteOptions(styles.Text.Id) { HorizontalAlignment = HorizontalTextAlignment.Left };
            var usedL = new List<double>(); var usedR = new List<double>();
            double Free(List<double> used, double v)
            {
                while (used.Any(x => Math.Abs(x - v) < lineH * 1.1)) v -= lineH * 1.2;
                used.Add(v);
                return v;
            }
            void Callout(bool left, double u, string text, IEnumerable<(double R, double U)> targets)
            {
                Try(() =>
                {
                    double v = Free(left ? usedL : usedR, u + lineH / 2);
                    // à esquerda ficam as cotas do consolo: o texto vai além delas
                    TextNote tn = TextNote.Create(_doc, view.Id, At(left ? minR - off * 3 : maxR + off, v), text, left ? leftOpt : rightOpt);
                    foreach (var (r, uu) in targets)
                    {
                        Leader l = tn.AddLeader(TextNoteLeaderTypes.TNLT_STRAIGHT_L);
                        l.End = At(r, uu);
                    }
                });
            }
            double cr = (minR + maxR) / 2;
            foreach (var g in groups.OrderByDescending(g => g.Pts.Average(t => t.U)))
            {
                double mr = g.Pts.Average(t => t.R), mu = g.Pts.Average(t => t.U);
                bool left = mr < cr;
                Callout(left, mu, "N" + g.N, g.Pts);
                if (!plan && g.Role.IndexOf("soldada", StringComparison.OrdinalIgnoreCase) >= 0)
                    Callout(left, mu, "Solda", g.Pts.Take(1));
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

        private const string RealisticTypeName = "PCEB - Barra em escala";
        private RebarBendingDetailType _realistic;
        private bool _realisticTried;

        /// <summary>
        /// Tipo de detalhe de curvatura em escala (realista) com a etiqueta de vergalhão do escritório. Null (com aviso) se a
        /// família de etiqueta não estiver no projeto nem no caminho do padrão — aí a armadura volta ao desenho esquemático.
        /// </summary>
        private RebarBendingDetailType RealisticType(AnnotationStyles styles)
        {
            if (_realisticTried) return _realistic;
            _realisticTried = true;
            FamilySymbol tag = RebarTagType(Std.Armadura.EtiquetaLongitudinal);
            if (tag == null) return null;
            RebarBendingDetailType t = new FilteredElementCollector(_doc).OfClass(typeof(RebarBendingDetailType))
                .Cast<RebarBendingDetailType>().FirstOrDefault(x => x.Name == RealisticTypeName);
            if (t == null)
            {
                t = RebarBendingDetailType.CreateRealistic(_doc);
                Try(() => t.Name = RealisticTypeName);
                Try(() => t.SegmentLengthDimensionsEnabled = false);   // o comprimento vai na etiqueta (C=...)
                Try(() => t.BendDiameterDimensionsEnabled = false);
                Try(() => t.AngularDimensionsEnabled = false);
                Try(() => t.OrthogonalAndOverallDimesionsEnabled = false);
            }
            Try(() => t.TagTypeId = tag.Id);
            return _realistic = t;
        }

        /// <summary>Tipo de etiqueta de vergalhão do escritório (família do padrão); carrega a família se faltar.</summary>
        private FamilySymbol RebarTagType(string typeName)
        {
            string fam = Std.Armadura.FamiliaEtiqueta?.Trim() ?? "", file = Std.Armadura.ArquivoEtiqueta?.Trim() ?? "";
            if (fam == "" || string.IsNullOrWhiteSpace(typeName)) return null;
            FamilySymbol Find() => new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_RebarTags).WhereElementIsElementType()
                .OfType<FamilySymbol>().FirstOrDefault(x => string.Equals(x.FamilyName, fam, StringComparison.OrdinalIgnoreCase) &&
                                                            string.Equals(x.Name, typeName, StringComparison.OrdinalIgnoreCase));
            FamilySymbol s = Find();
            if (s == null && file != "" && System.IO.File.Exists(file))
            {
                try { _doc.LoadFamily(file, out Family _); } catch { /* aviso abaixo */ }
                s = Find();
            }
            if (s == null) { _tagWarn ??= $"Etiqueta de vergalhão \"{fam} : {typeName}\" não encontrada (nem em {file}): armadura no desenho esquemático."; return null; }
            if (!s.IsActive) s.Activate();
            return s;
        }
        private string _tagWarn;

        /// <summary>Etiqueta de vergalhão; a 1ª falha vai para o log da peça (o motivo aparece no relatório).</summary>
        private IndependentTag TagRebar(FamilySymbol type, View view, Rebar bar, bool leader, TagOrientation o, XYZ at)
        {
            // 1º o elemento; o Revit recusa ("can not be tagged") em alguns conjuntos: aí a referência de uma barra
            // desenhada na vista (como o clique do usuário numa barra)
            var refs = new List<Reference> { new Reference(bar) };
            try
            {
                var opt = new Options { View = view, ComputeReferences = true };
                GeometryElement ge = bar.get_Geometry(opt);
                if (ge != null)
                    foreach (GeometryObject go in ge)
                    {
                        if (go is Curve c && c.Reference != null) refs.Add(c.Reference);
                        else if (go is Solid so) foreach (Edge e in so.Edges) { if (e.Reference != null) refs.Add(e.Reference); break; }
                    }
            }
            catch { /* sem geometria na vista */ }
            string err = "";
            foreach (Reference rf in refs.Take(12))
            {
                using (var st = new SubTransaction(_doc))
                {
                    st.Start();
                    try
                    {
                        IndependentTag t = IndependentTag.Create(_doc, type.Id, view.Id, rf, leader, o, at);
                        st.Commit();
                        return t;
                    }
                    catch (Exception ex) { err = ex.Message; st.RollBack(); }
                }
            }
            _tagWarn ??= $"Etiqueta \"{type.FamilyName} : {type.Name}\" não criada em {view.Name}: {err} ({refs.Count} referências tentadas)";
            return null;
        }

        /// <summary>
        /// Detalhe de curvatura de uma posição na vista de armadura: em escala, centrado na altura real da barra (u) e com a
        /// borda esquerda em r. Fica de pé (barra longitudinal): se sair deitado, é refeito girado. Devolve a largura.
        /// </summary>
        private bool BendingDetailAlong(View view, Rebar bar, RebarBendingDetailType type, double r, double u, List<XYZ> pts, out double width)
        {
            width = 0;
            XYZ right = view.RightDirection, up = view.UpDirection, vd = view.ViewDirection;
            XYZ at = right * r + up * u + vd * view.Origin.DotProduct(vd);
            foreach (double rot in new[] { 0.0, Math.PI / 2 })
            {
                using (var st = new SubTransaction(_doc))
                {
                    st.Start();
                    try
                    {
                        Element d = RebarBendingDetail.Create(_doc, view.Id, bar.Id, 0, type, at, rot);
                        _doc.Regenerate();
                        BoundingBoxXYZ bb = d.get_BoundingBox(view);
                        if (bb == null) { st.RollBack(); continue; }
                        var c = Corners(bb);
                        double w = c.Max(q => q.DotProduct(right)) - c.Min(q => q.DotProduct(right));
                        double h = c.Max(q => q.DotProduct(up)) - c.Min(q => q.DotProduct(up));
                        if (w > h && rot == 0) { st.RollBack(); continue; }   // deitado: tenta girado
                        // encosta a borda esquerda em r e centra na altura da barra
                        XYZ move = right * (r - c.Min(q => q.DotProduct(right))) + up * (u - (c.Max(q => q.DotProduct(up)) + c.Min(q => q.DotProduct(up))) / 2);
                        XYZ p0 = RebarBendingDetail.GetPosition(d);
                        RebarBendingDetail.SetPosition(d, p0 + move);
                        _doc.Regenerate();
                        foreach (XYZ q in c) pts.Add(q + move);
                        st.Commit();
                        width = w;
                        return true;
                    }
                    catch { if (st.HasStarted() && !st.HasEnded()) st.RollBack(); }
                }
            }
            return false;
        }

        private static List<XYZ> Corners(BoundingBoxXYZ bb)
        {
            var c = new List<XYZ>();
            foreach (double x in new[] { bb.Min.X, bb.Max.X })
                foreach (double y in new[] { bb.Min.Y, bb.Max.Y })
                    foreach (double z in new[] { bb.Min.Z, bb.Max.Z })
                        c.Add(bb.Transform.OfPoint(new XYZ(x, y, z)));
            return c;
        }

        /// <summary>Barras do consolo (tirante, barra soldada, costura, estribos verticais, inferior, pinos).</summary>
        private static bool IsCorbelRole(string role) =>
            role.IndexOf("consolo", StringComparison.OrdinalIgnoreCase) >= 0 || role.IndexOf("soldada", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsStirrupRole(string role) => role == "Estribo" || role == "Grampo";

        /// <summary>
        /// Recorte do corte/seção = pilar + consolos dentro da profundidade + 5 cm. O da vista de montagem é um quadrado
        /// de 1,25 m, que fazia cada corte ocupar ~65 mm de largura na prancha em 1:25 (e não caber na coluna).
        /// </summary>
        private static void FitSectionCrop(View v, PieceInfo p)
        {
            if (v is ViewSection vs) DEB.Revit.Core.ViewFactory.FitCrop(vs, SectionPoints(v, p), U.Mm(50));
        }

        /// <summary>Pontos do pilar e dos consolos que estão dentro da profundidade da vista.</summary>
        private static List<XYZ> SectionPoints(View view, PieceInfo p)
        {
            XYZ vd = view.ViewDirection;
            double d = view.Origin.DotProduct(vd);
            double far = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR)?.AsDouble() ?? U.Mm(1000);
            var pts = new List<XYZ>(p.Vertices);
            foreach (FamilyInstance k in p.Attached)
            {
                BoundingBoxXYZ bb = k.get_BoundingBox(null);
                if (bb == null) continue;
                var c = new List<XYZ>();
                foreach (double x in new[] { bb.Min.X, bb.Max.X })
                    foreach (double y in new[] { bb.Min.Y, bb.Max.Y })
                        foreach (double z in new[] { bb.Min.Z, bb.Max.Z })
                            c.Add(bb.Transform.OfPoint(new XYZ(x, y, z)));
                if (c.Max(q => q.DotProduct(vd)) < d - far || c.Min(q => q.DotProduct(vd)) > d + U.Mm(5)) continue;
                pts.AddRange(c);
            }
            return pts;
        }

        /// <summary>Extensão da seção na vista: pilar + consolos dentro da profundidade da vista.</summary>
        private static (double MinR, double MaxR, double MinU, double MaxU) SectionExtents(View view, PieceInfo p)
        {
            XYZ right = view.RightDirection, up = view.UpDirection, vd = view.ViewDirection;
            double d = view.Origin.DotProduct(vd);
            double far = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR)?.AsDouble() ?? U.Mm(1000);
            var pts = new List<XYZ>(p.Vertices);
            foreach (FamilyInstance k in p.Attached)
            {
                BoundingBoxXYZ bb = k.get_BoundingBox(null);
                if (bb == null) continue;
                var c = new List<XYZ>();
                foreach (double x in new[] { bb.Min.X, bb.Max.X })
                    foreach (double y in new[] { bb.Min.Y, bb.Max.Y })
                        foreach (double z in new[] { bb.Min.Z, bb.Max.Z })
                            c.Add(bb.Transform.OfPoint(new XYZ(x, y, z)));
                if (c.Max(q => q.DotProduct(vd)) < d - far || c.Min(q => q.DotProduct(vd)) > d + U.Mm(5)) continue;
                pts.AddRange(c);
            }
            return (pts.Min(q => q.DotProduct(right)), pts.Max(q => q.DotProduct(right)),
                    pts.Min(q => q.DotProduct(up)), pts.Max(q => q.DotProduct(up)));
        }

        private void BendingDetails(List<View> targets, List<View> corbelTargets, List<View> stirrupTargets, PieceInfo p,
            List<Rebar> rebars, AnnotationStyles styles, PieceLog log)
        {
            RebarBendingDetailType type = BendingType(styles);
            var placedPerView = targets.Concat(corbelTargets).Concat(stirrupTargets).GroupBy(v => v.Id).ToDictionary(g => g.Key, g => 0);
            var corbelIds = new HashSet<ElementId>(corbelTargets.Select(v => v.Id));
            var stirrupIds = new HashSet<ElementId>(stirrupTargets.Select(v => v.Id));
            var center = new TextNoteOptions(styles.Text.Id) { HorizontalAlignment = HorizontalTextAlignment.Center };

            // uma forma por posição (N), na ordem da numeração; quantidade = soma dos conjuntos da posição
            var byNumber = rebars
                .Select(r => new RebarRow(_doc, r))
                .Where(r => r.Role != "Longitudinal" && r.Role != "Alça de içamento" && r.Number != "")
                .GroupBy(r => r.Number).OrderBy(g => NumberKey(g.Key));

            var placedRoles = new HashSet<(string, double)>();
            var missing = new List<(string N, string Role, double D)>();
            foreach (var g in byNumber)
            {
                RebarRow first = g.First();
                string label = Fill(Std.Armadura.RotuloBarra, new RebarRow(first, g.Sum(x => x.Quantity)));
                bool ok = false;
                // peças do consolo (pinos, tirante...) só no quadro do consolo, junto do consolo: não vão para a elevação
                IEnumerable<View> order = IsCorbelRole(first.Role) ? corbelTargets
                    : IsStirrupRole(first.Role) ? stirrupTargets.Concat(targets)
                    : targets;
                // a barra do grupo que aparece na vista (o quadro mostra um consolo; a 1ª barra pode ser de outro)
                List<Rebar> candidates = g.Select(x => x.Rebar).ToList();
                foreach (View view in order)
                foreach (Rebar cand in candidates)
                {
                    if (ok) break;
                    XYZ right = view.RightDirection, up = view.UpDirection, vd = view.ViewDirection;
                    double s = view.Scale;
                    double minU = p.Vertices.Min(v => v.DotProduct(up)), maxU = p.Vertices.Max(v => v.DotProduct(up));
                    double minR = p.Vertices.Min(v => v.DotProduct(right)), maxR = p.Vertices.Max(v => v.DotProduct(right));
                    if (corbelIds.Contains(view.Id) && view is ViewSection cv)
                    {
                        // vista do consolo: as formas vão em linha embaixo do recorte
                        BoundingBoxXYZ cb = cv.CropBox;
                        var cs = new[] { cb.Min, cb.Max, new XYZ(cb.Min.X, cb.Max.Y, 0), new XYZ(cb.Max.X, cb.Min.Y, 0) }
                            .Select(q => cb.Transform.OfPoint(new XYZ(q.X, q.Y, 0))).ToList();
                        minU = cs.Min(q => q.DotProduct(up)); maxU = cs.Max(q => q.DotProduct(up));
                        minR = cs.Min(q => q.DotProduct(right)); maxR = cs.Max(q => q.DotProduct(right));
                    }
                    // elevação esquemática: as formas vão à direita do desenho
                    if (_schematic.TryGetValue(view.Id, out List<XYZ> sp) && sp.Count > 0)
                        maxR = Math.Max(maxR, sp.Max(q => q.DotProduct(right)) - U.Mm(36) * s);
                    int i = placedPerView[view.Id];
                    double pitch = U.Mm(ShapeHmm + 11) * s, colW = U.Mm(ShapeWmm + 12) * s;
                    XYZ pos;
                    if (stirrupIds.Contains(view.Id))
                    {
                        // seção: 2 formas por linha, centradas no pilar, abaixo das chamadas N
                        var (sr0, sr1, su0, _) = SectionExtents(view, p);
                        double cr = (sr0 + sr1) / 2, top = su0 - U.Mm(16) * s;
                        pos = right * (cr + colW * ((i % 2) - 0.5)) + up * (top - pitch * (0.5 + i / 2));
                    }
                    else if (maxU - minU > 2 * (maxR - minR))
                    {
                        // elevação alta: colunas à direita, depois dos rótulos dos estribos
                        int perCol = Math.Max(1, (int)((maxU - minU) / pitch));
                        pos = right * (maxR + U.Mm(42) * s + colW * (0.5 + i / perCol)) + up * (maxU - pitch * (0.5 + i % perCol));
                    }
                    else if (corbelIds.Contains(view.Id))
                    {
                        // quadro do consolo: 3 formas por linha, embaixo do corte
                        const int perRow = 3;
                        pos = right * (minR + colW * (0.5 + i % perRow)) + up * (minU - pitch * (0.8 + i / perRow));
                    }
                    else
                        pos = right * (minR + colW * (0.5 + i)) + up * (minU - pitch * 0.8);
                    pos += vd * view.Origin.DotProduct(vd);
                    using (var st = new SubTransaction(_doc))
                    {
                        st.Start();
                        try
                        {
                            RebarBendingDetail.Create(_doc, view.Id, cand.Id, 0, type, pos, 0);
                            TextNote.Create(_doc, view.Id, pos - up * (U.Mm(ShapeHmm / 2 + 6) * s), label, center);
                            st.Commit();
                            placedPerView[view.Id] = i + 1;
                            ok = true;
                        }
                        catch
                        {
                            st.RollBack();   // a barra não aparece nesta vista: tenta a próxima
                        }
                    }
                }
                if (ok) placedRoles.Add((first.Role, first.DiameterMm));
                else missing.Add((g.Key, first.Role, first.DiameterMm));
            }
            foreach (var (n, role, d) in missing)
                if (placedRoles.Contains((role, d)))
                    log.Info($"N{n} ({role}): forma igual à de outro consolo já desenhada no quadro do consolo.");
                else
                    log.Warn($"Forma da posição N{n} ({role}) não criada (a barra não aparece nas vistas {(IsCorbelRole(role) ? "do consolo" : "de armadura")}).");
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
