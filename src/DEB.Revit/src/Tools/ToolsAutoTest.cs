using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DEB.Core.Numeracao;
using DEB.Revit.Core;

namespace DEB.Revit.Tools
{
    /// <summary>
    /// AutoRun "ferramentas": roda a lógica das ferramentas sem janelas no modelo aberto.
    /// SD_COPYFROM = .rvt de origem para testar as cópias entre arquivos (opcional);
    /// SD_SAVEAS = cópia onde gravar o resultado (o original nunca é salvo).
    /// </summary>
    internal static class ToolsAutoTest
    {
        public static string Run(UIApplication uiapp, UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            string dir = Path.Combine(DetailingReport.LogDir, $"ferramentas_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            void Step(string name, Func<string> f)
            {
                try { sb.AppendLine($"✓ {name}: {f()}"); }
                catch (Exception ex) { sb.AppendLine($"✗ {name}: {ex.Message}"); sb.AppendLine("   " + ex); }
            }
            List<Element> All(BuiltInCategory c) => new FilteredElementCollector(doc).OfCategory(c).WhereElementIsNotElementType()
                .Where(e => ElementTools.Mark(e) is { IsReadOnly: false }).ToList();
            string Marks(BuiltInCategory c) => string.Join(", ", All(c).Select(e => $"{ElementTools.MarkText(e)}#{e.Id.Value}"));

            Step("Renumerar pilares", () => new RenumberColumnsCommand().Apply(doc, All(BuiltInCategory.OST_StructuralColumns), "TP", 1) + " → " + Marks(BuiltInCategory.OST_StructuralColumns));
            Step("Renumerar vigas", () => new RenumberBeamsCommand().Apply(doc, All(BuiltInCategory.OST_StructuralFraming).Where(e => e is FamilyInstance).ToList(), "TV", 1).Replace("\n", " ") + " → " + Marks(BuiltInCategory.OST_StructuralFraming));
            Step("Renumerar fundações", () => new RenumberFoundationsCommand().Apply(doc, All(BuiltInCategory.OST_StructuralFoundation), "TF", 1));
            Step("Renumerar lajes", () => new RenumberSlabsCommand().Apply(doc, All(BuiltInCategory.OST_Floors), "TL", 1));
            Step("Renumerar paredes", () => new RenumberWallsCommand().Apply(doc, All(BuiltInCategory.OST_Walls).Where(e => e is Wall).ToList(), "TW", 1).Replace("\n", " "));

            Step("Selecionar por marca (pilares começando com TP)", () =>
            {
                List<Element> f = SelectByMarkCommand.Find(doc, BuiltInCategory.OST_StructuralColumns, ModoFiltroMarca.ComecaCom, "tp");
                uidoc.Selection.SetElementIds(f.Select(e => e.Id).ToList());
                int esperado = All(BuiltInCategory.OST_StructuralColumns).Count;
                if (f.Count != esperado) throw new Exception($"achou {f.Count}, esperado {esperado}");
                return $"{f.Count} selecionados";
            });
            Step("Selecionar por marca (regex ^TV\\d+$)", () => SelectByMarkCommand.Find(doc, BuiltInCategory.OST_StructuralFraming, ModoFiltroMarca.Regex, @"^TV\d+$").Count + " vigas");

            Step("Alterar material", () =>
            {
                List<Material> mats = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().ToList();
                Material m = mats.FirstOrDefault(x => x.Name.Equals("Concreto - Pré-Moldado - C40", StringComparison.OrdinalIgnoreCase))
                             ?? mats.FirstOrDefault(x => x.Name.IndexOf("Concreto", StringComparison.OrdinalIgnoreCase) >= 0) ?? mats.First();
                var (types, pieces) = ChangeMaterialCommand.Apply(doc, m, new List<BuiltInCategory> { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming });
                return $"\"{m.Name}\" em {types} tipo(s) e {pieces} peça(s)";
            });

            Step("Exportar CSV", () =>
            {
                string folder = Path.Combine(dir, "csv");
                var els = new[] { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_GenericModel }
                    .SelectMany(c => new FilteredElementCollector(doc).OfCategory(c).WhereElementIsNotElementType().ToElements()).ToList();
                Directory.CreateDirectory(folder);
                ExportCsvCommand.Export(doc, els, folder);
                return $"{els.Count} elementos; " + string.Join(", ", Directory.GetFiles(folder).Select(f => $"{Path.GetFileName(f)} ({File.ReadLines(f).Count() - 1} linhas)"));
            });

            string from = Environment.GetEnvironmentVariable("SD_COPYFROM");
            if (!string.IsNullOrWhiteSpace(from) && File.Exists(from))
            {
                Document src = uiapp.Application.OpenDocumentFile(from);
                try
                {
                    var options = new CopyPasteOptions();
                    options.SetDuplicateTypeNamesHandler(new CopyFromFileCommand.UseTargetTypes());
                    foreach (CopyFromFileCommand cmd in new CopyFromFileCommand[] { new CopyAssembliesCommand(), new CopyFoundationsCommand() })
                        Step(cmd.GetType().Name, () =>
                        {
                            var items = cmd.Collect(src);
                            if (items.Count == 0) return "nada a copiar na origem";
                            using var t = new Transaction(doc, "Teste cópia");
                            t.Start();
                            string r = cmd.Copy(src, doc, items.Select(i => i.Id).ToList(), options).Replace("\n", " | ");
                            t.Commit();
                            return $"{items.Count} na origem ({string.Join("; ", items.Take(3).Select(i => i.Text))}) → {r}";
                        });
                }
                finally { src.Close(false); }
            }
            else sb.AppendLine("– cópias entre arquivos: SD_COPYFROM não informado");

            string saveAs = Environment.GetEnvironmentVariable("SD_SAVEAS");
            if (!string.IsNullOrWhiteSpace(saveAs))
            {
                doc.SaveAs(saveAs, new SaveAsOptions { OverwriteExistingFile = true });
                sb.AppendLine("Modelo salvo em " + saveAs);
            }
            string log = Path.Combine(dir, "resultado.txt");
            File.WriteAllText(log, sb.ToString(), Encoding.UTF8);
            return log;
        }
    }
}
