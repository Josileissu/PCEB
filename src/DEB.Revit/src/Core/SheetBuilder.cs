using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace DEB.Revit.Core
{
    /// <summary>
    /// Cria a folha do assembly e distribui as vistas:
    ///  • lista de peças acima do carimbo, alinhada à direita;
    ///  • vistas em "prateleiras" da esquerda para a direita, de cima para baixo, evitando carimbo e tabela;
    ///  • se não couber, aumenta as escalas pela sequência ScaleLadder e tenta de novo.
    /// </summary>
    internal sealed class SheetBuilder
    {
        private const double Eps = 1e-6;

        private readonly Document _doc;
        private readonly DetailingConfig _cfg;
        private readonly PieceLog _log;

        private sealed class Item
        {
            public Viewport Vp;
            public string Title;
            public double W, H, BoxH;
            public XYZ OutlineCenter;
            public Rect2 Target;
        }

        public SheetBuilder(Document doc, DetailingConfig cfg, PieceLog log)
        {
            _doc = doc; _cfg = cfg; _log = log;
        }

        public ViewSheet Build(AssemblyInstance asm, PieceInfo p, DetailViews dv)
        {
            ElementId tbId = ResolveTitleBlock();
            if (tbId == ElementId.InvalidElementId)
                _log.Warn("Nenhum carimbo carregado no projeto: folha criada sem carimbo.");

            ViewSheet sheet = AssemblyViewUtils.CreateSheet(_doc, asm.Id, tbId);
            sheet.SheetNumber = NameRegistry.ForSheetNumbers(_doc).Unique(_cfg.SheetNumberPrefix + p.Mark);
            sheet.Name = _cfg.SheetNamePrefix + p.Mark;
            _doc.Regenerate();

            Rect2 area = GetDrawingArea(sheet);
            var obstacles = new List<Rect2>
            {
                new Rect2(area.R - U.Mm(_cfg.TitleBlockStampWidthMm), area.B, area.R, area.B + U.Mm(_cfg.TitleBlockStampHeightMm))
            };
            var center = new XYZ((area.L + area.R) / 2.0, (area.B + area.T) / 2.0, 0);

            // Viewports (posição provisória no centro; o arranjo vem depois)
            var items = new List<Item>();
            foreach (DetailView v in dv.Views)
            {
                if (!Viewport.CanAddViewToSheet(_doc, sheet.Id, v.View.Id))
                {
                    _log.Warn($"{v.Title}: a vista não pôde ser colocada na folha.");
                    continue;
                }
                items.Add(new Item { Vp = Viewport.Create(_doc, sheet.Id, v.View.Id, center), Title = v.Title });
            }

            // Lista de peças acima do carimbo
            if (dv.PartList != null)
            {
                try
                {
                    ScheduleSheetInstance si = ScheduleSheetInstance.Create(_doc, sheet.Id, dv.PartList.Id, center);
                    _doc.Regenerate();
                    BoundingBoxXYZ bb = si.get_BoundingBox(sheet);
                    if (bb != null)
                    {
                        double gap = U.Mm(_cfg.ViewportGapMm);
                        Rect2 stamp = obstacles[0];
                        var delta = new XYZ(area.R - bb.Max.X, stamp.T + gap - bb.Min.Y, 0);
                        ElementTransformUtils.MoveElement(_doc, si.Id, delta);
                        obstacles.Add(new Rect2(bb.Min.X + delta.X, bb.Min.Y + delta.Y, bb.Max.X + delta.X, bb.Max.Y + delta.Y));
                    }
                }
                catch (Exception ex) { _log.Warn("Lista de peças não colocada na folha: " + ex.Message); }
            }

            // Arranjo com redução automática de escala
            bool fits = false;
            bool rescaled = false;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                _doc.Regenerate();
                Measure(items);
                if (Pack(items, area, obstacles, strict: true)) { fits = true; break; }
                if (!IncreaseScales(dv)) break;
                rescaled = true;
            }

            if (!fits)
            {
                _doc.Regenerate();
                Measure(items);
                Pack(items, area, obstacles, strict: false);
                _log.Warn("As vistas não couberam na área útil da folha. Reorganize manualmente ou use um formato maior.");
            }

            Apply(items);

            if (rescaled)
                _log.Info("Escalas ajustadas para caber na folha: " +
                          string.Join(", ", dv.Views.Select(v => $"{v.Title} 1:{v.View.Scale}")));

            return sheet;
        }

        // ------------------------------------------------------------------

        private ElementId ResolveTitleBlock()
        {
            List<FamilySymbol> tbs = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .OfType<FamilySymbol>()
                .ToList();
            if (tbs.Count == 0) return ElementId.InvalidElementId;

            FamilySymbol pick = null;
            if (!string.IsNullOrWhiteSpace(_cfg.TitleBlockName))
            {
                pick = tbs.FirstOrDefault(s =>
                    (s.FamilyName + " " + s.Name).IndexOf(_cfg.TitleBlockName.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
                if (pick == null)
                    _log.Warn($"Carimbo \"{_cfg.TitleBlockName}\" não encontrado; usando \"{tbs[0].FamilyName} - {tbs[0].Name}\".");
            }
            return (pick ?? tbs[0]).Id;
        }

        private Rect2 GetDrawingArea(ViewSheet sheet)
        {
            Element tb = new FilteredElementCollector(_doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .FirstElement();
            BoundingBoxXYZ bb = tb?.get_BoundingBox(sheet);

            double l = 0, b = 0, r = U.Mm(_cfg.DefaultSheetWidthMm), t = U.Mm(_cfg.DefaultSheetHeightMm);
            if (bb != null) { l = bb.Min.X; b = bb.Min.Y; r = bb.Max.X; t = bb.Max.Y; }

            return new Rect2(
                l + U.Mm(_cfg.SheetMarginLeftMm),
                b + U.Mm(_cfg.SheetMarginBottomMm),
                r - U.Mm(_cfg.SheetMarginRightMm),
                t - U.Mm(_cfg.SheetMarginTopMm));
        }

        private void Measure(List<Item> items)
        {
            double label = U.Mm(_cfg.ViewportLabelAllowanceMm);
            foreach (Item it in items)
            {
                Outline o = it.Vp.GetBoxOutline();
                it.W = o.MaximumPoint.X - o.MinimumPoint.X;
                it.BoxH = o.MaximumPoint.Y - o.MinimumPoint.Y;
                it.H = it.BoxH + label;
                it.OutlineCenter = (o.MinimumPoint + o.MaximumPoint) * 0.5;
            }
        }

        private bool Pack(List<Item> items, Rect2 area, List<Rect2> obstacles, bool strict)
        {
            double gap = U.Mm(_cfg.ViewportGapMm);
            double cx = area.L, rowTop = area.T, rowH = 0;

            foreach (Item it in items)
            {
                for (int guard = 0; ; guard++)
                {
                    if (guard > 200) return false;

                    var r = new Rect2(cx, rowTop - it.H, cx + it.W, rowTop);
                    bool fitsRight = r.R <= area.R + Eps;
                    bool fitsBottom = r.B >= area.B - Eps;
                    List<Rect2> hits = obstacles.Where(o => o.Intersects(r)).ToList();

                    if (fitsRight && hits.Count == 0 && (fitsBottom || !strict))
                    {
                        it.Target = r;
                        cx = r.R + gap;
                        rowH = Math.Max(rowH, it.H);
                        break;
                    }

                    if (strict && !fitsBottom) return false;

                    if (cx > area.L + Eps)
                    {
                        // nova linha
                        cx = area.L;
                        rowTop -= rowH + gap;
                        rowH = 0;
                        continue;
                    }

                    if (!fitsRight)
                    {
                        // vista mais larga que a folha
                        if (strict) return false;
                        it.Target = r;
                        cx = r.R + gap;
                        rowH = Math.Max(rowH, it.H);
                        break;
                    }

                    // início de linha bloqueado por carimbo/tabela: desce abaixo do obstáculo
                    rowTop = hits.Min(o => o.B) - gap;
                }
            }
            return true;
        }

        private static void Apply(List<Item> items)
        {
            foreach (Item it in items)
            {
                var targetCenter = new XYZ(it.Target.L + it.W / 2.0, it.Target.T - it.BoxH / 2.0, 0);
                XYZ boxCenter = it.Vp.GetBoxCenter();
                it.Vp.SetBoxCenter(boxCenter + (targetCenter - it.OutlineCenter));
            }
        }

        private bool IncreaseScales(DetailViews dv)
        {
            bool changed = false;
            int[] ladder = (_cfg.ScaleLadder ?? Array.Empty<int>()).OrderBy(s => s).ToArray();
            foreach (DetailView v in dv.Views)
            {
                int cur = v.View.Scale;
                int next = ladder.FirstOrDefault(s => s > cur);
                if (next <= 0) continue;
                try { v.View.Scale = next; changed = true; }
                catch { /* escala controlada pelo template */ }
            }
            return changed;
        }
    }
}
