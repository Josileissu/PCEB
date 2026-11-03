using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StructuralDetailer.Core;

namespace StructuralDetailer.Presentation
{
    /// <summary>
    /// Dados das barras usados nas tabelas: partição (numeração N1, N2... por peça), aço (CA50/CA60)
    /// e peso. Aço e peso ficam em dois parâmetros compartilhados do plugin, criados no projeto
    /// na primeira vez (o Revit não oferece peso de barra nem campos calculados pela API).
    /// </summary>
    internal static class RebarData
    {
        public const string SteelParam = "SD_Aço";
        public const string WeightParam = "SD_Peso (kg)";
        private static readonly Guid SteelGuid = new Guid("5f0c3a8e-6b1d-4c2a-9e47-1d2b3c4d5e01");
        private static readonly Guid WeightGuid = new Guid("5f0c3a8e-6b1d-4c2a-9e47-1d2b3c4d5e02");

        public static ElementId SteelParamId(Document doc) => SharedParameterElement.Lookup(doc, SteelGuid)?.Id ?? ElementId.InvalidElementId;
        public static ElementId WeightParamId(Document doc) => SharedParameterElement.Lookup(doc, WeightGuid)?.Id ?? ElementId.InvalidElementId;

        /// <summary>Cria e vincula (à categoria Vergalhão estrutural) os parâmetros do plugin. Precisa de transação.</summary>
        public static void EnsureParameters(Document doc)
        {
            if (SharedParameterElement.Lookup(doc, SteelGuid) != null && SharedParameterElement.Lookup(doc, WeightGuid) != null) return;

            Autodesk.Revit.ApplicationServices.Application app = doc.Application;
            string previous = app.SharedParametersFilename;
            string temp = Path.Combine(Path.GetTempPath(), "StructuralDetailer_params.txt");
            File.WriteAllText(temp, "");
            try
            {
                app.SharedParametersFilename = temp;
                DefinitionFile file = app.OpenSharedParameterFile();
                DefinitionGroup group = file.Groups.get_Item("StructuralDetailer") ?? file.Groups.Create("StructuralDetailer");

                var cats = new CategorySet();
                cats.Insert(Category.GetCategory(doc, BuiltInCategory.OST_Rebar));

                foreach (var (name, guid, spec) in new[]
                {
                    (SteelParam, SteelGuid, SpecTypeId.String.Text),
                    (WeightParam, WeightGuid, SpecTypeId.Number)
                })
                {
                    if (SharedParameterElement.Lookup(doc, guid) != null) continue;
                    Definition def = group.Definitions.get_Item(name) ??
                                     group.Definitions.Create(new ExternalDefinitionCreationOptions(name, spec) { GUID = guid, UserModifiable = false });
                    doc.ParameterBindings.Insert(def, app.Create.NewInstanceBinding(cats), GroupTypeId.Data);
                }
            }
            finally
            {
                app.SharedParametersFilename = previous ?? "";
                try { File.Delete(temp); } catch { /* temporário */ }
            }
        }

        /// <summary>Partição, aço e peso de cada barra. Precisa de transação.</summary>
        public static void Apply(Document doc, IEnumerable<ElementId> rebarIds, string partition, double steelDensity)
        {
            foreach (Rebar r in rebarIds.Select(doc.GetElement).OfType<Rebar>())
            {
                if (!string.IsNullOrEmpty(partition))
                    r.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM)?.Set(partition);

                var type = doc.GetElement(r.GetTypeId()) as RebarBarType;
                r.LookupParameter(SteelParam)?.Set(SteelGrade(type));

                double d = type?.BarNominalDiameter ?? 0;
                double totalM = UnitUtils.ConvertFromInternalUnits(r.TotalLength, UnitTypeId.Meters);
                double dM = UnitUtils.ConvertFromInternalUnits(d, UnitTypeId.Meters);
                double kg = totalM * Math.PI * dM * dM / 4 * steelDensity;
                r.LookupParameter(WeightParam)?.Set(Math.Round(kg, 2));
            }
        }

        /// <summary>"CA50"/"CA60": do nome do tipo de barra; sem indicação, Ø &lt; 6,3 mm é CA60.</summary>
        public static string SteelGrade(RebarBarType t)
        {
            string n = (t?.Name ?? "").ToUpperInvariant().Replace("-", "").Replace(" ", "");
            if (n.Contains("CA60")) return "CA60";
            if (n.Contains("CA50")) return "CA50";
            if (n.Contains("CA25")) return "CA25";
            return t != null && U.ToMm(t.BarNominalDiameter) < 6.3 ? "CA60" : "CA50";
        }
    }
}
