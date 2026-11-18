using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using DEB.Revit.Core;

namespace DEB.Revit.Precast
{
    /// <summary>
    /// Famílias do PCEB (D36): apoio no consolo e furo dos pinos na viga pré-moldada.
    ///   SD_AUTORUN = familiassonda → geometria de SD_FAMILIAS (.rfa separados por ";") e das famílias aninhadas:
    ///                                planos de referência, cotas rotuladas, sólidos/vazios e associações.
    /// Só lê: as famílias abertas são fechadas sem salvar.
    /// </summary>
    internal static partial class FamiliasPceb
    {
        private static double Mm(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);
        private static string P(XYZ p) => p == null ? "-" : string.Format(CultureInfo.InvariantCulture, "({0:0},{1:0},{2:0})", Mm(p.X), Mm(p.Y), Mm(p.Z));
        private static string V(XYZ v) => v == null ? "-" : string.Format(CultureInfo.InvariantCulture, "({0:0.##},{1:0.##},{2:0.##})", v.X, v.Y, v.Z);

        public static string Sonda(Application app)
        {
            string list = Environment.GetEnvironmentVariable("SD_FAMILIAS");
            if (string.IsNullOrWhiteSpace(list)) throw new InvalidOperationException("SD_FAMILIAS vazio.");
            var sb = new StringBuilder();
            foreach (string path in list.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0))
            {
                sb.AppendLine("=== " + path);
                Document fam = null;
                try
                {
                    fam = app.OpenDocumentFile(path);
                    Geometria(fam, sb, "");
                    foreach (FamilyInstance fi in new FilteredElementCollector(fam).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                                 .GroupBy(f => f.Symbol.Family.Id.Value).Select(g => g.First()).ToList())
                    {
                        sb.AppendLine($"--- aninhada {fi.Symbol.Family.Name} (instância {fi.Id.Value}, giro {V(fi.HandOrientation)} / {V(fi.FacingOrientation)})");
                        Document sub = null;
                        try
                        {
                            sub = fam.EditFamily(fi.Symbol.Family);
                            Geometria(sub, sb, "    ");
                        }
                        catch (Exception ex) { sb.AppendLine("    ERRO " + ex.Message); }
                        finally { sub?.Close(false); }
                    }
                }
                catch (Exception ex) { sb.AppendLine("ERRO " + ex); }
                finally { fam?.Close(false); }
                sb.AppendLine();
            }
            Directory.CreateDirectory(DetailingReport.LogDir);
            string log = Path.Combine(DetailingReport.LogDir, "familiassonda_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            File.WriteAllText(log, sb.ToString(), Encoding.UTF8);
            return log;
        }

        private static void Geometria(Document fam, StringBuilder sb, string ind)
        {
            FamilyManager fm = fam.FamilyManager;
            sb.AppendLine($"{ind}Template/categoria: {fam.OwnerFamily.FamilyCategory?.Name}; parâmetros: " +
                          string.Join(", ", fm.GetParameters().Where(p => !p.IsShared || true).Select(p => p.Definition.Name + (p.IsShared ? "*" : ""))));

            sb.AppendLine($"{ind}Planos de referência:");
            foreach (ReferencePlane rp in new FilteredElementCollector(fam).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>())
            {
                string isRef = rp.get_Parameter(BuiltInParameter.ELEM_REFERENCE_NAME)?.AsValueString();
                sb.AppendLine($"{ind}  [{rp.Id.Value}] \"{rp.Name}\" normal {V(rp.Normal)} passa por {P(rp.BubbleEnd)}–{P(rp.FreeEnd)} é ref.: {isRef}");
            }

            sb.AppendLine($"{ind}Cotas:");
            foreach (Dimension d in new FilteredElementCollector(fam).OfClass(typeof(Dimension)).Cast<Dimension>())
            {
                string label = "";
                try { label = d.FamilyLabel?.Definition.Name ?? ""; } catch { }
                var refs = new List<string>();
                foreach (Reference r in d.References)
                {
                    Element e = fam.GetElement(r);
                    refs.Add(e == null ? "?" : $"{e.GetType().Name}[{e.Id.Value}]{(e is ReferencePlane rp ? "\"" + rp.Name + "\"" : "")}");
                }
                string val = d.Value.HasValue ? Math.Round(Mm(d.Value.Value)).ToString(CultureInfo.InvariantCulture) : "(multi)";
                sb.AppendLine($"{ind}  [{d.Id.Value}] {d.GetType().Name} rótulo={label} valor={val} vista={fam.GetElement(d.OwnerViewId)?.Name} refs: {string.Join(" | ", refs)}" +
                              (d.AreSegmentsEqual ? " EQ" : ""));
            }

            sb.AppendLine($"{ind}Formas:");
            foreach (GenericForm gf in new FilteredElementCollector(fam).OfClass(typeof(GenericForm)).Cast<GenericForm>())
            {
                BoundingBoxXYZ bb = gf.get_BoundingBox(null);
                var assoc = new List<string>();
                foreach (Parameter p in gf.Parameters)
                {
                    FamilyParameter fp = fm.GetAssociatedFamilyParameter(p);
                    if (fp != null) assoc.Add($"{p.Definition.Name}←{fp.Definition.Name}");
                }
                string extra = "";
                if (gf is Extrusion ex)
                    extra = $" plano={ex.Sketch?.SketchPlane?.Name} normal={V(ex.Sketch?.SketchPlane?.GetPlane().Normal)} ini={Mm(ex.StartOffset):0} fim={Mm(ex.EndOffset):0}";
                sb.AppendLine($"{ind}  [{gf.Id.Value}] {gf.GetType().Name} {(gf.IsSolid ? "sólido" : "VAZIO")} bb {P(bb?.Min)}–{P(bb?.Max)}{extra} {string.Join(", ", assoc)}");
            }

            var mats = new FilteredElementCollector(fam).OfClass(typeof(Material)).Cast<Material>().Select(m => m.Name).ToList();
            sb.AppendLine($"{ind}Materiais: {string.Join(", ", mats)}");
            var vistas = new FilteredElementCollector(fam).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate).Select(v => $"{v.ViewType}:{v.Name}");
            sb.AppendLine($"{ind}Vistas: {string.Join(", ", vistas)}");
        }
    }
}
