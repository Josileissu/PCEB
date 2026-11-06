using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using DEB.Revit.Core;

namespace DEB.Revit.Presentation
{
    /// <summary>
    /// Filtros de vista do escritório para a armadura em 3D: um filtro por bitola ("ø6.3", "ø8.0"…) com cor de linha e de
    /// superfície (padrão sólido), como no template do escritório. Filtro existente com o mesmo nome é reaproveitado
    /// (o usuário pode mudar as cores no Revit; a próxima vista segue o que estiver no projeto).
    /// </summary>
    internal static class RebarFilters
    {
        private static readonly (double Mm, byte R, byte G, byte B)[] Colors =
        {
            (5.0, 255, 128, 0), (6.3, 255, 0, 0), (8.0, 0, 128, 0), (10.0, 0, 128, 255), (12.5, 0, 0, 255),
            (16.0, 128, 0, 0), (20.0, 0, 0, 0), (25.0, 255, 0, 255), (32.0, 128, 0, 255)
        };

        /// <summary>Cria (se faltar) e aplica os filtros de bitola na vista. Precisa de transação. Devolve quantos aplicou.</summary>
        public static int Apply(Document doc, View view, PieceLog log)
        {
            var cat = new ElementId(BuiltInCategory.OST_Rebar);
            var cats = new List<ElementId> { cat };
            ElementId diam = ParameterFilterUtilities.GetFilterableParametersInCommon(doc, cats)
                .FirstOrDefault(id => id.Value == (long)BuiltInParameter.REBAR_BAR_DIAMETER);
            if (diam == null)
            {
                log?.Warn("Filtros de bitola: o parâmetro de diâmetro da barra não é filtrável neste Revit.");
                return 0;
            }
            ElementId solid = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern().IsSolidFill)?.Id;
            var existing = new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>()
                .ToDictionary(f => f.Name, f => f, StringComparer.OrdinalIgnoreCase);
            int n = 0;
            foreach (var (mm, r, g, b) in Colors)
            {
                string name = "ø" + mm.ToString("0.0", CultureInfo.InvariantCulture);
                try
                {
                    if (!existing.TryGetValue(name, out ParameterFilterElement f))
                    {
                        FilterRule rule = ParameterFilterRuleFactory.CreateEqualsRule(diam, U.Mm(mm), U.Mm(0.05));
                        f = ParameterFilterElement.Create(doc, name, cats, new ElementParameterFilter(rule));
                    }
                    if (!view.GetFilters().Contains(f.Id)) view.AddFilter(f.Id);
                    var c = new Color(r, g, b);
                    var o = new OverrideGraphicSettings();
                    o.SetProjectionLineColor(c);
                    o.SetCutLineColor(c);
                    if (solid != null)
                    {
                        o.SetSurfaceForegroundPatternId(solid);
                        o.SetSurfaceForegroundPatternColor(c);
                    }
                    view.SetFilterOverrides(f.Id, o);
                    view.SetFilterVisibility(f.Id, true);
                    n++;
                }
                catch (Exception ex) { log?.Trace("Filtro " + name, ex); }
            }
            return n;
        }
    }
}
