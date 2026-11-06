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
    /// Apoio aos testes automáticos (AutoRun) que não precisam de um projeto aberto.
    ///   SD_AUTORUN = familias → relatório das famílias de SD_FAMILIAS (caminhos separados por ";"):
    ///                           tipos, parâmetros e famílias aninhadas.
    /// </summary>
    internal static class TestModel
    {
        public static string InspectFamilies(Application app)
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
                    Describe(fam, sb, "");
                }
                catch (Exception ex)
                {
                    sb.AppendLine("ERRO " + ex.Message);
                }
                finally
                {
                    fam?.Close(false);
                }
                sb.AppendLine();
            }

            Directory.CreateDirectory(DetailingReport.LogDir);
            string log = Path.Combine(DetailingReport.LogDir, "familias_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            File.WriteAllText(log, sb.ToString(), Encoding.UTF8);
            return log;
        }

        /// <summary>
        /// SD_AUTORUN = criarmodelo → projeto novo de teste com pilares e vigas pré-moldadas (consolos aninhados na viga).
        ///   SD_TEMPLATE  = template .rte
        ///   SD_PILAR     = família .rfa do pilar (tipos "40x40 - docas" nos cantos e "50x50 - internos" no meio)
        ///   SD_VIGA      = família .rfa da viga (consolos aninhados nas pontas)
        ///   SD_SAVEAS    = onde gravar o .rvt
        ///   SD_NIVEIS    = níveis das vigas, "cota_mm:direções" separados por ";" (ex.: "3000:X;6000:XY;9000:Y");
        ///                  padrão "6000:XY". O pilar vai até o nível mais alto. Direções: X (2 linhas), Y (3 linhas).
        /// Grade 3 × 2 pilares com vãos de 6 m; vigas do centro de um pilar ao do outro, em cada nível pedido.
        /// </summary>
        public static string BuildPrecastModel(Application app)
        {
            string template = Env("SD_TEMPLATE"), pilarRfa = Env("SD_PILAR"), vigaRfa = Env("SD_VIGA"), saveAs = Env("SD_SAVEAS");
            var sb = new StringBuilder();

            Document doc = app.NewProjectDocument(template);
            double mm(double v) => UnitUtils.ConvertToInternalUnits(v, UnitTypeId.Millimeters);
            const double vao = 6000;
            var niveis = new List<(double Cota, string Dirs)>();
            foreach (string item in (Environment.GetEnvironmentVariable("SD_NIVEIS") ?? "6000:XY").Split(';').Where(x => x.Trim() != ""))
            {
                string[] kv = item.Split(':');
                niveis.Add((double.Parse(kv[0].Trim(), CultureInfo.InvariantCulture), kv.Length > 1 ? kv[1].Trim().ToUpperInvariant() : "XY"));
            }
            niveis = niveis.OrderBy(n => n.Cota).ToList();
            double altura = niveis[^1].Cota;
            double embutimento = double.TryParse(Environment.GetEnvironmentVariable("SD_EMBUTIMENTO_MM"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double e) ? e : 1000;

            using (var t = new Transaction(doc, "Modelo de teste — pré-moldados"))
            {
                t.Start();

                // Níveis: o mais baixo existente vira o 0; um nível por cota de viga (o mais alto = topo dos pilares).
                List<Level> levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
                Level baseLv = levels.First();
                baseLv.Elevation = 0;
                Level LevelAt(double cota)
                {
                    Level lv = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                                   .FirstOrDefault(l => l.Id != baseLv.Id && Math.Abs(l.Elevation - mm(cota)) < mm(1))
                               ?? Level.Create(doc, mm(cota));
                    return lv;
                }
                Level topLv = LevelAt(altura);
                try { topLv.Name = "Topo pilares"; } catch { /* nome já existe */ }

                if (!doc.LoadFamily(pilarRfa, out Family pilarFam)) throw new InvalidOperationException("não carregou " + pilarRfa);
                if (!doc.LoadFamily(vigaRfa, out Family vigaFam)) throw new InvalidOperationException("não carregou " + vigaRfa);
                FamilySymbol Sym(Family f, string name)
                {
                    FamilySymbol s = f.GetFamilySymbolIds().Select(id => (FamilySymbol)doc.GetElement(id))
                        .FirstOrDefault(x => name == null || x.Name == name) ?? throw new InvalidOperationException($"tipo \"{name}\" não existe em {f.Name}");
                    if (!s.IsActive) s.Activate();
                    return s;
                }
                FamilySymbol canto = Sym(pilarFam, "40x40 - docas"), meio = Sym(pilarFam, "50x50 - internos"), viga = Sym(vigaFam, null);

                // Pilares: grade 3 (x) × 2 (y).
                var pos = new Dictionary<(int, int), XYZ>();
                for (int i = 0; i < 3; i++)
                    for (int j = 0; j < 2; j++)
                    {
                        XYZ p = new XYZ(mm(i * vao), mm(j * vao), 0);
                        pos[(i, j)] = p;
                        FamilyInstance c = doc.Create.NewFamilyInstance(p, i == 1 ? meio : canto, baseLv, Autodesk.Revit.DB.Structure.StructuralType.Column);
                        c.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM).Set(topLv.Id);
                        // embutimento no cálice = deslocamento negativo da base (padrão do escritório); SD_EMBUTIMENTO_MM muda
                        c.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM).Set(-mm(embutimento));
                        c.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM).Set(0.0);
                        sb.AppendLine($"pilar ({i},{j}) {c.Symbol.Name} id {c.Id.Value}");
                    }

                // Vigas: ao longo de X (2 linhas) e/ou de Y (3 linhas), em cada nível pedido.
                void Beam(XYZ a, XYZ b, Level lv)
                {
                    XYZ za = new XYZ(a.X, a.Y, lv.Elevation), zb = new XYZ(b.X, b.Y, lv.Elevation);
                    FamilyInstance v = doc.Create.NewFamilyInstance(Line.CreateBound(za, zb), viga, lv, Autodesk.Revit.DB.Structure.StructuralType.Beam);
                    // A folga da peça vem dos parâmetros folga_esq/folga_dir da família; o recuo (½" padrão do Revit)
                    // e as extensões da instância precisam ser 0, senão os consolos aninhados se afastam da face do pilar.
                    foreach (BuiltInParameter bip in new[] { BuiltInParameter.START_JOIN_CUTBACK, BuiltInParameter.END_JOIN_CUTBACK,
                                                             BuiltInParameter.START_EXTENSION, BuiltInParameter.END_EXTENSION })
                    {
                        Parameter p = v.get_Parameter(bip);
                        if (p == null) continue;
                        if (p.IsReadOnly) sb.AppendLine($"   {p.Definition.Name}: só leitura, não zerado");
                        else p.Set(0.0);
                    }
                    sb.AppendLine($"viga {v.Id.Value} nível {lv.Name}");
                }
                foreach (var (cota, dirs) in niveis)
                {
                    Level lv = LevelAt(cota);
                    if (dirs.Contains('X')) for (int j = 0; j < 2; j++) for (int i = 0; i < 2; i++) Beam(pos[(i, j)], pos[(i + 1, j)], lv);
                    if (dirs.Contains('Y')) for (int i = 0; i < 3; i++) Beam(pos[(i, 0)], pos[(i, 1)], lv);
                }

                t.Commit();
            }

            // Conferência da geometria: parâmetros de comprimento das vigas e caixas dos consolos aninhados.
            string Mm(double v) => Math.Round(UnitUtils.ConvertFromInternalUnits(v, UnitTypeId.Millimeters)).ToString(CultureInfo.InvariantCulture);
            string Box(Element e) { BoundingBoxXYZ b = e.get_BoundingBox(null); return b == null ? "sem caixa" : $"({Mm(b.Min.X)},{Mm(b.Min.Y)},{Mm(b.Min.Z)})-({Mm(b.Max.X)},{Mm(b.Max.Y)},{Mm(b.Max.Z)})"; }
            foreach (FamilyInstance v in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming)
                         .WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                sb.AppendLine($"viga {v.Id.Value} caixa {Box(v)}");
                foreach (Parameter p in v.Parameters.Cast<Parameter>().Where(p => p.StorageType == StorageType.Double &&
                             p.Definition.GetDataType() == SpecTypeId.Length &&
                             (p.Definition.Name.Contains("ompriment") || p.Definition.Name.StartsWith("folga") || p.Definition.Name.Contains("ecuo") || p.Definition.Name.Contains("ecorte") || p.Definition.Name.Contains("xtens"))))
                    sb.AppendLine($"   {p.Definition.Name}{(p.IsShared ? "" : (p.Id.Value < 0 ? " [interno]" : " [família]"))} = {Mm(p.AsDouble())} mm");
                foreach (ElementId sid in v.GetSubComponentIds())
                {
                    Element s = doc.GetElement(sid);
                    sb.AppendLine($"   aninhado {s.Name} [{s.Category?.Name}] caixa {Box(s)}");
                }
            }

            // Conferência: o levantamento do plugin acha os consolos encostados em cada pilar?
            List<ColumnGroup> groups = PrecastSurvey.Run(doc, out List<string> notes);
            foreach (string n in notes) sb.AppendLine("NOTA: " + n);
            foreach (ColumnGroup g in groups)
            {
                sb.AppendLine($"{g.Mark}: {g.Members.Count} un. | {g.Signature}");
                foreach (PrecastColumn m in g.Members)
                    sb.AppendLine($"   pilar {m.Id} giro {m.Rotation * 90}° consolos {m.Corbels.Count}");
            }

            doc.SaveAs(saveAs, new SaveAsOptions { OverwriteExistingFile = true });
            doc.Close(false);
            sb.AppendLine("Modelo salvo em " + saveAs);

            Directory.CreateDirectory(DetailingReport.LogDir);
            string log = Path.Combine(DetailingReport.LogDir, "criarmodelo_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            File.WriteAllText(log, sb.ToString(), Encoding.UTF8);
            return log;
        }

        private static string Env(string name)
        {
            string v = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(v)) throw new InvalidOperationException(name + " vazio.");
            return v.Trim();
        }

        private static void Describe(Document fam, StringBuilder sb, string ind)
        {
            Family owner = fam.OwnerFamily;
            sb.AppendLine($"{ind}Categoria: {owner.FamilyCategory?.Name}");
            Parameter shared = owner.get_Parameter(BuiltInParameter.FAMILY_SHARED);
            if (shared != null) sb.AppendLine($"{ind}Compartilhada: {shared.AsInteger() == 1}");
            Parameter mat = owner.get_Parameter(BuiltInParameter.FAMILY_STRUCT_MATERIAL_TYPE);
            if (mat != null) sb.AppendLine($"{ind}Material estrutural: {mat.AsValueString()}");

            FamilyManager fm = fam.FamilyManager;
            sb.AppendLine($"{ind}Tipos: " + string.Join(" | ", fm.Types.Cast<FamilyType>().Select(t => t.Name)));
            FamilyType cur = fm.CurrentType;
            foreach (FamilyParameter p in fm.GetParameters().OrderBy(p => p.IsInstance).ThenBy(p => p.Definition.Name))
            {
                string val = "";
                if (cur != null && cur.HasValue(p))
                {
                    switch (p.StorageType)
                    {
                        case StorageType.Double:
                            val = p.Definition.GetDataType() == SpecTypeId.Length
                                ? Math.Round(UnitUtils.ConvertFromInternalUnits(cur.AsDouble(p) ?? 0, UnitTypeId.Millimeters)).ToString(CultureInfo.InvariantCulture) + " mm"
                                : (cur.AsDouble(p) ?? 0).ToString("0.###", CultureInfo.InvariantCulture);
                            break;
                        case StorageType.Integer: val = (cur.AsInteger(p) ?? 0).ToString(); break;
                        case StorageType.String: val = cur.AsString(p); break;
                        case StorageType.ElementId:
                            ElementId id = cur.AsElementId(p);
                            val = id != null && id != ElementId.InvalidElementId ? fam.GetElement(id)?.Name : "";
                            break;
                    }
                }
                string formula = string.IsNullOrEmpty(p.Formula) ? "" : "  = " + p.Formula;
                sb.AppendLine($"{ind}  [{(p.IsInstance ? "inst" : "tipo")}] {p.Definition.Name} ({p.Definition.GetDataType()?.TypeId?.Split(':').Last().Split('-').First()}): {val}{formula}");
            }

            List<FamilyInstance> nested = new FilteredElementCollector(fam).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().ToList();
            foreach (FamilyInstance fi in nested)
            {
                Family nf = fi.Symbol.Family;
                Parameter ns = nf.get_Parameter(BuiltInParameter.FAMILY_SHARED);
                XYZ o = (fi.Location as LocationPoint)?.Point;
                string pos = o == null ? "" : string.Format(CultureInfo.InvariantCulture, " em ({0:0},{1:0},{2:0}) mm",
                    UnitUtils.ConvertFromInternalUnits(o.X, UnitTypeId.Millimeters),
                    UnitUtils.ConvertFromInternalUnits(o.Y, UnitTypeId.Millimeters),
                    UnitUtils.ConvertFromInternalUnits(o.Z, UnitTypeId.Millimeters));
                sb.AppendLine($"{ind}Aninhada: {nf.Name} : {fi.Symbol.Name} [{fi.Category?.Name}] compartilhada={(ns?.AsInteger() == 1)}{pos}");
                // Parâmetros da instância aninhada ligados a parâmetros da família hospedeira.
                foreach (Parameter ip in fi.Parameters)
                {
                    FamilyParameter assoc = fm.GetAssociatedFamilyParameter(ip);
                    if (assoc != null) sb.AppendLine($"{ind}    {ip.Definition.Name} ← {assoc.Definition.Name}");
                }
            }
        }
    }
}
