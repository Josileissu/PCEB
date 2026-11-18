using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DEB.Revit.Core;

namespace DEB.Revit.Presentation
{
    /// <summary>
    /// Dados das barras usados nas tabelas: partição (numeração N1, N2... por peça), aço (CA50/CA60), comprimento e peso.
    /// Ficam em parâmetros compartilhados do plugin, criados no projeto na primeira vez (o Revit não oferece peso de barra
    /// nem campos calculados pela API). Comprimento unitário e total são os do núcleo (regras do CAD: dobras, ganchos e
    /// arredondamento da relação do aço), gravados quando o plugin cria a barra — decisão do dono D30, item 4 (16/11/2026).
    /// São valores gravados: o Revit não os recalcula se a barra for editada à mão (rodar o comando de novo regrava).
    /// </summary>
    internal static class RebarData
    {
        public const string SteelParam = "PCEB_Aço";
        public const string WeightParam = "PCEB_Peso (kg)";
        private static readonly Guid SteelGuid = new Guid("5f0c3a8e-6b1d-4c2a-9e47-1d2b3c4d5e01");
        private static readonly Guid WeightGuid = new Guid("5f0c3a8e-6b1d-4c2a-9e47-1d2b3c4d5e02");
        // comprimento pelo núcleo (D30): GUIDs fixos novos, na sequência dos dois acima (nunca mudar)
        public const string CoreLengthParam = "PCEB_Comprimento (núcleo)";
        public const string CoreTotalParam = "PCEB_Comprimento total (núcleo)";
        private static readonly Guid CoreLengthGuid = new Guid("5f0c3a8e-6b1d-4c2a-9e47-1d2b3c4d5e03");
        private static readonly Guid CoreTotalGuid = new Guid("5f0c3a8e-6b1d-4c2a-9e47-1d2b3c4d5e04");

        public static ElementId SteelParamId(Document doc) => SharedParameterElement.Lookup(doc, SteelGuid)?.Id ?? ElementId.InvalidElementId;
        public static ElementId WeightParamId(Document doc) => SharedParameterElement.Lookup(doc, WeightGuid)?.Id ?? ElementId.InvalidElementId;
        public static ElementId CoreLengthParamId(Document doc) => SharedParameterElement.Lookup(doc, CoreLengthGuid)?.Id ?? ElementId.InvalidElementId;
        public static ElementId CoreTotalParamId(Document doc) => SharedParameterElement.Lookup(doc, CoreTotalGuid)?.Id ?? ElementId.InvalidElementId;

        /// <summary>Comprimento unitário do núcleo gravado na barra (cm), ou null se a barra não tem o valor (não veio do plugin).</summary>
        public static double? CoreLengthCm(Rebar r)
        {
            Parameter p = r.get_Parameter(CoreLengthGuid);
            return p != null && p.HasValue && p.AsDouble() > 0 ? UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Centimeters) : (double?)null;
        }

        /// <summary>Cria e vincula (à categoria Vergalhão estrutural) os parâmetros do plugin. Precisa de transação.</summary>
        public static void EnsureParameters(Document doc)
        {
            var all = new[]
            {
                (SteelParam, SteelGuid, SpecTypeId.String.Text),
                (WeightParam, WeightGuid, SpecTypeId.Number),
                (CoreLengthParam, CoreLengthGuid, SpecTypeId.Length),
                (CoreTotalParam, CoreTotalGuid, SpecTypeId.Length)
            };
            if (all.All(p => SharedParameterElement.Lookup(doc, p.Item2) != null)) return;

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

                foreach (var (name, guid, spec) in all)
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

        /// <summary>
        /// Partição, aço, comprimento e peso de cada barra. Precisa de transação.
        /// <paramref name="coreLengthsCm"/> = comprimento unitário do núcleo por conjunto (RebarFactory.CoreLengthsCm): grava
        /// PCEB_Comprimento e PCEB_Comprimento total (unitário × quantidade de barras do conjunto) e calcula o peso por ele, como o
        /// CAD. Conjunto sem comprimento do núcleo (não veio do plugin): os dois ficam vazios e o peso sai do comprimento do Revit.
        /// </summary>
        /// <returns>conjuntos que receberam o comprimento do núcleo</returns>
        public static int Apply(Document doc, IEnumerable<ElementId> rebarIds, string partition, IReadOnlyDictionary<ElementId, double> coreLengthsCm = null)
        {
            int written = 0;
            foreach (Rebar r in rebarIds.Select(doc.GetElement).OfType<Rebar>())
            {
                if (!string.IsNullOrEmpty(partition))
                    r.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM)?.Set(partition);

                var type = doc.GetElement(r.GetTypeId()) as RebarBarType;
                r.LookupParameter(SteelParam)?.Set(SteelGrade(type));

                double d = type?.BarNominalDiameter ?? 0;
                double dMm = UnitUtils.ConvertFromInternalUnits(d, UnitTypeId.Millimeters);
                double totalM = UnitUtils.ConvertFromInternalUnits(r.TotalLength, UnitTypeId.Meters);
                if (coreLengthsCm != null && coreLengthsCm.TryGetValue(r.Id, out double unitCm))
                {
                    int n = r.get_Parameter(BuiltInParameter.REBAR_ELEM_QUANTITY_OF_BARS)?.AsInteger() ?? r.Quantity;
                    double totalCm = unitCm * Math.Max(1, n);
                    r.get_Parameter(CoreLengthGuid)?.Set(UnitUtils.ConvertToInternalUnits(unitCm, UnitTypeId.Centimeters));
                    r.get_Parameter(CoreTotalGuid)?.Set(UnitUtils.ConvertToInternalUnits(totalCm, UnitTypeId.Centimeters));
                    totalM = totalCm / 100;
                    written++;
                }
                // massa linear da tabela de bitolas do núcleo (a mesma do plugin de CAD); cordoalha pela tabela de cordoalhas
                string grade = SteelGrade(type);
                double kg = totalM * SharedRules.KgPerMeter(dMm, grade == "CP190" ? "CP-190" : SharedRules.SteelFor(dMm));
                r.LookupParameter(WeightParam)?.Set(Math.Round(kg, 2));
            }
            SyncScheduleMarks(doc, rebarIds);
            return written;
        }

        /// <summary>
        /// "Marca da tabela" = número do vergalhão (N) da partição. A etiqueta do escritório (Identificador Vergalhão) lê a
        /// marca da tabela; o Revit a cria "única no hospedeiro" (1, 2…), que não bate com o N: estribo saía "N1" como a
        /// longitudinal. A numeração só existe depois de regenerar. Precisa de transação.
        /// </summary>
        public static void SyncScheduleMarks(Document doc, IEnumerable<ElementId> rebarIds)
        {
            doc.Regenerate();
            foreach (Rebar r in rebarIds.Select(doc.GetElement).OfType<Rebar>())
            {
                string n = r.get_Parameter(BuiltInParameter.REBAR_NUMBER)?.AsString();
                if (!string.IsNullOrEmpty(n) && r.ScheduleMark != n)
                    try { r.ScheduleMark = n; } catch { /* conjunto travado em grupo: fica a marca do Revit */ }
            }
        }

        /// <summary>"CP190"/"CA50"/"CA60": do nome do tipo de barra; sem indicação, Ø &lt; 6,3 mm é CA60.</summary>
        public static string SteelGrade(RebarBarType t)
        {
            string n = (t?.Name ?? "").ToUpperInvariant().Replace("-", "").Replace(" ", "");
            if (n.Contains("CP190")) return "CP190";
            if (n.Contains("CA60")) return "CA60";
            if (n.Contains("CA50")) return "CA50";
            if (n.Contains("CA25")) return "CA25";
            return t != null && U.ToMm(t.BarNominalDiameter) < 6.3 ? "CA60" : "CA50";
        }
    }
}
