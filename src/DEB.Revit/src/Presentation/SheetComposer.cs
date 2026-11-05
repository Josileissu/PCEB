using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DEB.Revit.Core;

namespace DEB.Revit.Presentation
{
    /// <summary>
    /// Monta a prancha do assembly segundo as regiões do padrão: cada item (vista, tabela, quadro de texto,
    /// legenda, título) vai para a sua região e é arranjado em linhas, da esquerda para a direita, de cima
    /// para baixo. Se as vistas de uma região não couberem, as escalas sobem pela lista do padrão.
    /// </summary>
    internal sealed class SheetComposer
    {
        private readonly Document _doc;
        private readonly PresentationContext _ctx;
        private readonly AnnotationStyles _styles;
        private readonly PieceLog _log;
        private PresentationStandard Std => _ctx.Standard;

        private sealed class Item
        {
            public Element Element;
            public Viewport Viewport;           // null para tabelas, textos e título
            public TextNote Heading;            // título acima da vista (TituloAcima)
            public string HeadingText;          // título sem a linha da escala
            public double W, H, LabelH;
            public double LabelW;               // largura estimada do título da vista
            public Rect2 Target;
        }

        public SheetComposer(Document doc, PresentationContext ctx, AnnotationStyles styles, PieceLog log)
        {
            _doc = doc; _ctx = ctx; _styles = styles; _log = log;
        }

        public ViewSheet Compose(AssemblyInstance asm, List<PlacedView> views)
        {
            ViewSheet sheet = AssemblyViewUtils.CreateSheet(_doc, asm.Id, TitleBlock());
            sheet.SheetNumber = NameRegistry.ForSheetNumbers(_doc).Unique(Fill(Std.Folha.NumeroFolha));
            sheet.Name = Fill(Std.Folha.NomeFolha);
            FillTitleBlock(sheet);
            _doc.Regenerate();

            Rect2 area = DrawingArea(sheet);
            XYZ center = new XYZ((area.L + area.R) / 2, (area.B + area.T) / 2, 0);
            var byRegion = new Dictionary<string, List<Item>>(StringComparer.OrdinalIgnoreCase);
            void Put(string region, Item it)
            {
                if (!byRegion.TryGetValue(region ?? "", out var l)) byRegion[region ?? ""] = l = new List<Item>();
                l.Add(it);
            }

            // Título da peça
            if (!string.IsNullOrWhiteSpace(Std.Textos.TituloPeca))
            {
                TextNote t = TextNote.Create(_doc, sheet.Id, center, Fill(Std.Textos.TituloPeca), _styles.Title.Id);
                Put(Std.Textos.RegiaoTituloPeca, new Item { Element = t });
            }

            // Vistas
            double label = U.Mm(Std.Folha.TituloVistaMm);
            ElementId noLabel = null;
            foreach (PlacedView pv in views)
            {
                if (!Viewport.CanAddViewToSheet(_doc, sheet.Id, pv.View.Id)) { _log.Warn($"{pv.Title.Split('\n')[0]}: não pôde ir para a prancha."); continue; }
                Viewport vp = Viewport.Create(_doc, sheet.Id, pv.View.Id, center);
                if (Std.Folha.TituloAcima)
                {
                    noLabel ??= ViewportTypeWithoutLabel(vp);
                    if (noLabel != ElementId.InvalidElementId) vp.ChangeTypeId(noLabel);
                    var opt = new TextNoteOptions(_styles.ViewTitle.Id) { HorizontalAlignment = HorizontalTextAlignment.Center };
                    // largura da caixa = linha mais longa (sem largura, a caixa do texto fica bem maior que o texto e
                    // o arranjo acha que a vista não cabe)
                    int chars = pv.Title.Split('\n').Concat(new[] { Std.Folha.TituloEscala ?? "" }).Max(l => l.Length) + 2;
                    double hw = chars * U.Mm(Std.Textos.AlturaTituloVistaMm * 0.8);
                    TextNote h = TextNote.Create(_doc, sheet.Id, center, hw, pv.Title, opt);
                    Put(pv.Spec.Regiao, new Item { Element = vp, Viewport = vp, Heading = h, HeadingText = pv.Title, LabelW = hw });
                    continue;
                }
                // o Revit quebra o título na largura da linha: conta a linha mais longa (PT ou EN), não o título inteiro
                double labelW = pv.Title.Split('\n').Max(l => l.Length) * U.Mm(Std.Folha.LarguraCaractereTituloMm);
                try { vp.LabelLineLength = labelW; } catch { /* tipo de viewport sem linha de título */ }
                Put(pv.Spec.Regiao, new Item { Element = vp, Viewport = vp, LabelH = label, LabelW = labelW });
            }

            // Tabelas de aço
            foreach (TableSpec ts in Std.Tabelas.Where(t => t.Ativa))
            {
                try
                {
                    string name = NameRegistry.ForViews(_doc).Unique($"{Fill("{Marca}")} - {ts.Titulo.Split('(')[0].Trim()}");
                    ViewSchedule vs = RebarSchedules.Create(_doc, asm, ts, name);
                    Put(ts.Regiao, new Item { Element = ScheduleSheetInstance.Create(_doc, sheet.Id, vs.Id, center) });
                }
                catch (Exception ex) { _log.Warn($"Tabela \"{ts.Titulo}\" não criada: {ex.Message}"); _log.Trace("Tabela", ex); }
            }

            // Quadros de texto (largura = região)
            foreach (TextBlockSpec tb in Std.Quadros)
            {
                Rect2 r = RegionRect(area, tb.Regiao);
                TextNoteType type = tb.Moldura ? _styles.FramedText : _styles.Text;
                TextNote n = TextNote.Create(_doc, sheet.Id, center, Math.Max(U.Mm(30), r.W * 0.95), Fill(tb.Texto),
                    new TextNoteOptions(type.Id));
                Put(tb.Regiao, new Item { Element = n });
            }

            // Legendas (detalhes padrão do escritório)
            foreach (LegendSpec ls in Std.Legendas)
            {
                View legend = new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>()
                    .FirstOrDefault(v => v.ViewType == ViewType.Legend && !v.IsTemplate &&
                                         string.Equals(v.Name, ls.Nome, StringComparison.OrdinalIgnoreCase));
                if (legend == null) { _log.Warn($"Legenda \"{ls.Nome}\" não existe no projeto (crie-a em Vistas → Legendas para ela entrar na prancha)."); continue; }
                Viewport vp = Viewport.Create(_doc, sheet.Id, legend.Id, center);
                Put(ls.Regiao, new Item { Element = vp, Viewport = vp, LabelH = label });
            }

            // Arranjo por região, com aumento de escala das vistas quando não couber.
            foreach (var kv in byRegion)
            {
                Rect2 r = RegionRect(area, kv.Key);
                Dictionary<ElementId, int> original = kv.Value.Where(i => i.Viewport != null)
                    .Select(i => _doc.GetElement(i.Viewport.ViewId) as View).Where(v => v != null)
                    .ToDictionary(v => v.Id, v => v.Scale);
                bool fits = false;
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    UpdateHeadings(kv.Value);
                    _doc.Regenerate();
                    Measure(sheet, kv.Value, r.W);
                    if (Pack(kv.Value, r, strict: true)) { fits = true; break; }
                    if (attempt == 0)
                    {
                        // diagnóstico: o que não coube na escala do padrão (larguras e alturas em mm de papel)
                        string sizes = string.Join("; ", kv.Value.Select(i =>
                            $"{(i.Heading != null ? i.HeadingText.Split('\n')[0] : i.Element.Name)} {U.ToMm(i.W):0}x{U.ToMm(i.H):0}"));
                        _log.Info($"Região \"{kv.Key}\" ({U.ToMm(r.W):0}x{U.ToMm(r.H):0} mm) não coube na escala do padrão: {sizes}");
                    }
                    if (!IncreaseScales(kv.Value)) break;
                }
                if (!fits)
                {
                    // Não coube nem reduzindo: volta às escalas do padrão e arranja assim mesmo.
                    foreach (var o in original)
                        try { ((View)_doc.GetElement(o.Key)).Scale = o.Value; } catch { /* escala do template */ }
                    UpdateHeadings(kv.Value);
                    _doc.Regenerate();
                    Measure(sheet, kv.Value, r.W);
                    Pack(kv.Value, r, strict: false);
                    _log.Warn($"Região \"{kv.Key}\": os itens não couberam; confira a prancha ou ajuste as regiões no padrão.");
                }
                Apply(sheet, kv.Value);
            }
            return sheet;
        }

        // ------------------------------------------------------------------

        private string Fill(string s) => PresentationStandard.Fill(s, _ctx.Values);

        /// <summary>Tipo de viewport igual ao atual, mas sem o título do Revit ("PCEB - Sem título").</summary>
        private ElementId ViewportTypeWithoutLabel(Viewport vp)
        {
            const string name = "PCEB - Sem título";
            try
            {
                if (!(_doc.GetElement(vp.GetTypeId()) is ElementType cur)) return ElementId.InvalidElementId;
                ElementType t = new FilteredElementCollector(_doc).OfClass(typeof(ElementType)).Cast<ElementType>()
                    .FirstOrDefault(e => e.FamilyName == cur.FamilyName && e.Name == name)
                    ?? cur.Duplicate(name);
                t.get_Parameter(BuiltInParameter.VIEWPORT_ATTR_SHOW_LABEL)?.Set(0);
                return t.Id;
            }
            catch (Exception ex) { _log.Warn($"Tipo de viewport sem título não criado: {ex.Message}"); return ElementId.InvalidElementId; }
        }

        /// <summary>Texto do título acima de cada vista: título + linha da escala atual (a escala muda no arranjo).
        /// A 1ª linha vai em negrito sublinhado.</summary>
        private void UpdateHeadings(List<Item> items)
        {
            foreach (Item it in items.Where(i => i.Heading != null))
            {
                View v = _doc.GetElement(it.Viewport.ViewId) as View;
                string scale = string.IsNullOrWhiteSpace(Std.Folha.TituloEscala) || v == null ? ""
                    : "\n" + Std.Folha.TituloEscala.Replace("{Escala}", v.Scale.ToString());
                it.Heading.Text = it.HeadingText + scale;
                try
                {
                    FormattedText ft = it.Heading.GetFormattedText();
                    var range = new TextRange(0, it.HeadingText.Split('\n')[0].Length);
                    ft.SetBoldStatus(range, true);
                    ft.SetUnderlineStatus(range, true);
                    it.Heading.SetFormattedText(ft);
                }
                catch { /* formatação é só estética */ }
            }
        }

        private ElementId TitleBlock()
        {
            string family = Std.Folha.FamiliaCarimbo?.Trim() ?? "";
            string want = Std.Folha.Carimbo?.Trim() ?? "";
            List<FamilySymbol> All() => new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType().OfType<FamilySymbol>().ToList();
            FamilySymbol Find(List<FamilySymbol> l) => l
                .Where(s => family == "" || string.Equals(s.FamilyName, family, StringComparison.OrdinalIgnoreCase))
                .Where(s => want == "" || (family == "" ? s.FamilyName + " " + s.Name : s.Name).IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0)
                // "A1" antes de "A1++": o nome mais curto que contém o trecho
                .OrderBy(s => s.Name.Length).FirstOrDefault();

            List<FamilySymbol> tbs = All();
            FamilySymbol pick = Find(tbs);
            string file = Std.Folha.ArquivoCarimbo?.Trim() ?? "";
            if (pick == null && file != "")
            {
                if (!System.IO.File.Exists(file)) _log.Warn($"Arquivo do carimbo não encontrado: {file}");
                else
                {
                    try
                    {
                        if (_doc.LoadFamily(file, new OverwriteNothing(), out Family _)) _log.Info($"Carimbo carregado de {file}.");
                        tbs = All();
                        pick = Find(tbs);
                    }
                    catch (Exception ex) { _log.Warn($"Carimbo não carregado de {file}: {ex.Message}"); }
                }
            }
            if (tbs.Count == 0) { _log.Warn("Nenhum carimbo carregado no projeto."); return ElementId.InvalidElementId; }
            if (pick == null) _log.Warn($"Carimbo \"{(family + " " + want).Trim()}\" não encontrado; usado \"{tbs[0].FamilyName} - {tbs[0].Name}\".");
            pick ??= tbs[0];
            if (!pick.IsActive) pick.Activate();
            return pick.Id;
        }

        /// <summary>Carrega a família só se ela ainda não existe (não sobrescreve a do projeto).</summary>
        private sealed class OverwriteNothing : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues) { overwriteParameterValues = false; return false; }
            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            { source = FamilySource.Project; overwriteParameterValues = false; return false; }
        }

        /// <summary>Preenche parâmetros do carimbo; se o carimbo não tiver o parâmetro, tenta a própria folha.</summary>
        private void FillTitleBlock(ViewSheet sheet)
        {
            Element tb = new FilteredElementCollector(_doc, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType().FirstElement();
            foreach (var kv in Std.Folha.ParametrosCarimbo)
            {
                Parameter p = tb?.LookupParameter(kv.Key) ?? sheet.LookupParameter(kv.Key);
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.String)
                {
                    // lista os parâmetros de texto editáveis, para acertar o nome no padrão
                    string avail = string.Join(", ", (tb?.Parameters.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                        .Concat(sheet.Parameters.Cast<Parameter>())
                        .Where(x => x.StorageType == StorageType.String && !x.IsReadOnly)
                        .Select(x => x.Definition.Name).Distinct().OrderBy(n => n));
                    _log.Warn($"Parâmetro \"{kv.Key}\" não encontrado (ou só leitura) no carimbo/folha. Disponíveis: {avail}");
                    continue;
                }
                p.Set(Fill(kv.Value));
            }
        }

        private Rect2 DrawingArea(ViewSheet sheet)
        {
            Element tb = new FilteredElementCollector(_doc, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType().FirstElement();
            BoundingBoxXYZ bb = tb?.get_BoundingBox(sheet);
            double l = 0, b = 0, r = U.Mm(841), t = U.Mm(594);
            if (bb != null) { l = bb.Min.X; b = bb.Min.Y; r = bb.Max.X; t = bb.Max.Y; }
            SheetSettings f = Std.Folha;
            return new Rect2(l + U.Mm(f.MargemEsquerdaMm), b + U.Mm(f.MargemInferiorMm), r - U.Mm(f.MargemDireitaMm), t - U.Mm(f.MargemSuperiorMm));
        }

        private Rect2 RegionRect(Rect2 area, string name)
        {
            Region g = name != null && Std.Regioes.TryGetValue(name, out Region rg) ? rg : new Region(0, 0, 1, 1);
            return new Rect2(area.L + g.X * area.W, area.B + g.Y * area.H, area.L + (g.X + g.L) * area.W, area.B + (g.Y + g.A) * area.H);
        }

        /// <summary>Tamanhos na folha. O título da vista conta na largura, mas no máximo a largura da região (ele quebra linha).</summary>
        private void Measure(ViewSheet sheet, List<Item> items, double regionWidth)
        {
            foreach (Item it in items)
            {
                if (it.Heading != null)
                {
                    Outline o = it.Viewport.GetBoxOutline();
                    // altura pelo número de linhas (a caixa medida pelo Revit sai bem maior que o texto)
                    int lines = it.HeadingText.Split('\n').Length + (string.IsNullOrWhiteSpace(Std.Folha.TituloEscala) ? 0 : 1);
                    it.LabelH = lines * U.Mm(Std.Textos.AlturaTituloVistaMm * 1.55) + U.Mm(3);
                    it.W = Math.Max(o.MaximumPoint.X - o.MinimumPoint.X, it.LabelW);
                    it.H = o.MaximumPoint.Y - o.MinimumPoint.Y + it.LabelH;
                }
                else if (it.Viewport != null)
                {
                    Outline o = it.Viewport.GetBoxOutline();
                    it.W = Math.Max(o.MaximumPoint.X - o.MinimumPoint.X, Math.Min(it.LabelW, regionWidth));
                    it.H = o.MaximumPoint.Y - o.MinimumPoint.Y + it.LabelH;
                }
                else
                {
                    BoundingBoxXYZ bb = it.Element.get_BoundingBox(sheet);
                    it.W = bb == null ? 0 : bb.Max.X - bb.Min.X;
                    it.H = bb == null ? 0 : bb.Max.Y - bb.Min.Y;
                }
            }
        }

        /// <summary>Linhas da esquerda para a direita, de cima para baixo, dentro da região.</summary>
        private bool Pack(List<Item> items, Rect2 r, bool strict)
        {
            double gap = U.Mm(Std.Folha.EspacamentoMm);
            double x = r.L, top = r.T, rowH = 0;
            foreach (Item it in items)
            {
                if (x > r.L && x + it.W > r.R + 1e-9)
                {
                    x = r.L;
                    top -= rowH + gap;
                    rowH = 0;
                }
                if (strict && (x + it.W > r.R + 1e-9 || top - it.H < r.B - 1e-9)) return false;
                it.Target = new Rect2(x, top - it.H, x + it.W, top);
                x += it.W + gap;
                rowH = Math.Max(rowH, it.H);
            }
            return true;
        }

        private bool IncreaseScales(List<Item> items)
        {
            int[] ladder = (Std.Folha.EscalasAlternativas ?? Array.Empty<int>()).OrderBy(s => s).ToArray();
            bool changed = false;
            foreach (Item it in items.Where(i => i.Viewport != null))
            {
                if (!(_doc.GetElement(it.Viewport.ViewId) is View v) || v.ViewType == ViewType.Legend) continue;
                int next = ladder.FirstOrDefault(s => s > v.Scale);
                if (next <= 0) continue;
                try { v.Scale = next; changed = true; } catch { /* escala do template */ }
            }
            return changed;
        }

        private void Apply(ViewSheet sheet, List<Item> items)
        {
            foreach (Item it in items)
            {
                if (it.Viewport != null)
                {
                    Outline o = it.Viewport.GetBoxOutline();
                    XYZ oc = (o.MinimumPoint + o.MaximumPoint) / 2;
                    double boxH = it.H - it.LabelH;
                    // título acima: a vista desce o espaço do título; etiqueta do Revit: a vista fica no alto
                    double top = it.Heading != null ? it.Target.T - it.LabelH : it.Target.T;
                    var target = new XYZ(it.Target.L + it.W / 2, top - boxH / 2, 0);
                    it.Viewport.SetBoxCenter(it.Viewport.GetBoxCenter() + (target - oc));
                    if (it.Heading != null) it.Heading.Coord = new XYZ(it.Target.L + it.W / 2, it.Target.T, 0);
                }
                else if (it.Element is ScheduleSheetInstance si)
                {
                    // O ponto de inserção da tabela é o canto superior esquerdo.
                    si.Point = new XYZ(it.Target.L, it.Target.T, 0);
                }
                else if (it.Element is TextNote tn)
                {
                    // Texto alinhado à esquerda: Coord é o canto superior esquerdo.
                    tn.Coord = new XYZ(it.Target.L, it.Target.T, 0);
                }
                else
                {
                    BoundingBoxXYZ bb = it.Element.get_BoundingBox(sheet);
                    if (bb == null) continue;
                    ElementTransformUtils.MoveElement(_doc, it.Element.Id, new XYZ(it.Target.L - bb.Min.X, it.Target.T - bb.Max.Y, 0));
                }
            }
        }
    }
}
