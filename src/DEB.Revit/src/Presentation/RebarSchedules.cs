using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace DEB.Revit.Presentation
{
    /// <summary>
    /// Tabelas de aço do assembly da peça:
    ///  • Relação do aço: AÇO, N, DIAM (mm), QUANT, C.UNIT (cm), C.TOTAL (cm), uma linha por posição;
    ///  • Resumo do aço: AÇO, DIAM (mm), C.TOTAL (m), PESO (kg), com peso total.
    /// </summary>
    internal static class RebarSchedules
    {
        public static ViewSchedule Create(Document doc, AssemblyInstance asm, TableSpec spec, string name)
        {
            ViewSchedule vs = AssemblyViewUtils.CreateSingleCategorySchedule(doc, asm.Id, new ElementId(BuiltInCategory.OST_Rebar));
            vs.Name = name;
            ScheduleDefinition def = vs.Definition;
            def.ClearFields();
            def.IsItemized = false;

            ElementId steel = RebarData.SteelParamId(doc), weight = RebarData.WeightParamId(doc);
            var cm = new FormatOptions(UnitTypeId.Centimeters) { UseDefault = false, Accuracy = 1 };
            var mm = new FormatOptions(UnitTypeId.Millimeters) { UseDefault = false, Accuracy = 0.1 };
            var m = new FormatOptions(UnitTypeId.Meters) { UseDefault = false, Accuracy = 0.1 };

            if (spec.Tipo == TableKind.RelacaoDoAco)
            {
                ScheduleField fSteel = Add(def, steel, "AÇO\n(STEEL)");
                ScheduleField fN = Add(def, new ElementId(BuiltInParameter.REBAR_NUMBER), "N");
                Add(def, new ElementId(BuiltInParameter.REBAR_BAR_DIAMETER), "DIAM\n(mm)", mm);
                Add(def, new ElementId(BuiltInParameter.REBAR_ELEM_QUANTITY_OF_BARS), "QUANT", totals: true);
                Add(def, new ElementId(BuiltInParameter.REBAR_ELEM_LENGTH), "C.UNIT\n(U. LENGTH)\n(cm)", cm);
                Add(def, new ElementId(BuiltInParameter.REBAR_ELEM_TOTAL_LENGTH), "C.TOTAL\n(T. LENGTH)\n(cm)", cm, totals: true);
                if (fN != null) def.AddSortGroupField(new ScheduleSortGroupField(fN.FieldId));
                def.ShowGrandTotal = false;
            }
            else
            {
                ScheduleField fSteel = Add(def, steel, "AÇO\n(STEEL)");
                ScheduleField fD = Add(def, new ElementId(BuiltInParameter.REBAR_BAR_DIAMETER), "DIAM\n(mm)", mm);
                Add(def, new ElementId(BuiltInParameter.REBAR_ELEM_TOTAL_LENGTH), "C.TOTAL\n(T. LENGTH)\n(m)", m, totals: true);
                Add(def, weight, "PESO\n(WEIGHT)\n(kg)", totals: true);
                if (fSteel != null) def.AddSortGroupField(new ScheduleSortGroupField(fSteel.FieldId));
                if (fD != null) def.AddSortGroupField(new ScheduleSortGroupField(fD.FieldId));
                def.ShowGrandTotal = true;
                def.ShowGrandTotalTitle = true;
                def.ShowGrandTotalCount = false;
                def.GrandTotalTitle = "PESO TOTAL (TOTAL WEIGHT)";
            }

            // Título da tabela = texto do padrão (o nome da vista continua único no projeto).
            try
            {
                TableSectionData header = vs.GetTableData().GetSectionData(SectionType.Header);
                if (header != null && header.NumberOfRows > 0) header.SetCellText(header.FirstRowNumber, header.FirstColumnNumber, spec.Titulo);
            }
            catch { /* título padrão do Revit */ }
            return vs;
        }

        private static ScheduleField Add(ScheduleDefinition def, ElementId paramId, string heading,
            FormatOptions fmt = null, bool totals = false)
        {
            if (paramId == ElementId.InvalidElementId) return null;
            SchedulableField sf = def.GetSchedulableFields().FirstOrDefault(f => f.ParameterId == paramId);
            if (sf == null) return null;
            ScheduleField field = def.AddField(sf);
            field.ColumnHeading = heading;
            if (fmt != null)
            {
                try { field.SetFormatOptions(fmt); } catch { /* campo sem unidade */ }
            }
            if (totals)
            {
                try { field.DisplayType = ScheduleFieldDisplayType.Totals; } catch { /* campo não somável */ }
            }
            return field;
        }
    }
}
