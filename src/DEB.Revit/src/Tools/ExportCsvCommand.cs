using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DEB.Revit.Tools
{
    /// <summary>
    /// Exporta tudo o que o modelo sabe das peças para CSV (separador ";"): visão geral, parâmetros de instância e de tipo,
    /// BuiltInParameters preenchidos, sólidos e malhas. Mesmos arquivos e colunas do script ExportarTudo_Colunas
    /// (coordenadas em pés, parâmetros de comprimento em m e de volume em m³); o ColumnFilter era um subconjunto dele.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class ExportCsvCommand : ToolCommand
    {
        private const double FootToMeter = 0.3048;
        private const double Foot3ToM3 = 0.028316846592;

        private static readonly (string Name, BuiltInCategory Cat, bool On)[] Categories =
        {
            ("Pilares estruturais", BuiltInCategory.OST_StructuralColumns, true),
            ("Pilares arquitetônicos", BuiltInCategory.OST_Columns, true),
            ("Vigas", BuiltInCategory.OST_StructuralFraming, true),
            ("Modelos genéricos", BuiltInCategory.OST_GenericModel, true),
            ("Fundações", BuiltInCategory.OST_StructuralFoundation, false),
            ("Lajes", BuiltInCategory.OST_Floors, false),
        };

        protected override string Title => "Exportar dados (CSV)";

        protected override Result Run(UIApplication uiapp, UIDocument uidoc, Document doc)
        {
            string defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "RevitExport_Pilares");
            var form = new FieldForm(Title, "Gera 6 arquivos CSV: visão geral, parâmetros de instância e de tipo, BuiltInParameters, sólidos e malhas.", 460)
                .Text("pasta", "Pasta de saída:", defaultFolder);
            foreach (var c in Categories) form.Check(c.Name, c.Name, c.On);
            form.Validate(f => string.IsNullOrWhiteSpace(f.Get("pasta")) ? "Informe a pasta."
                             : Categories.Any(c => f.IsChecked(c.Name)) ? null : "Marque ao menos uma categoria.");
            if (!form.Show("Exportar")) throw new ToolCancelled();

            List<Element> els = Categories.Where(c => form.IsChecked(c.Name))
                .SelectMany(c => new FilteredElementCollector(doc).OfCategory(c.Cat).WhereElementIsNotElementType().ToElements())
                .Where(e => e != null).ToList();
            if (els.Count == 0) throw new ToolCancelled("Nenhum elemento nas categorias marcadas.");

            string folder = form.Get("pasta");
            Directory.CreateDirectory(folder);
            Export(doc, els, folder);
            TaskDialog.Show(Title, $"Exportados {els.Count} elementos.\nArquivos em:\n{folder}");
            return Result.Succeeded;
        }

        internal static void Export(Document doc, List<Element> els, string folder)
        {
            StreamWriter W(string name) => new StreamWriter(Path.Combine(folder, name), false, Encoding.UTF8);
            using var ov = W("elements_overview.csv");
            using var inst = W("parameters_instance.csv");
            using var typ = W("parameters_type.csv");
            using var bips = W("builtins.csv");
            using var sol = W("geometry_solids.csv");
            using var mesh = W("geometry_meshes.csv");
            ov.WriteLine("ElementId;UniqueId;Categoria;Nome;TypeId;TypeName;IsInPlace;X;Y;Z;BB_MinX;BB_MinY;BB_MinZ;BB_MaxX;BB_MaxY;BB_MaxZ");
            inst.WriteLine("ElementId;Parametro;StorageType;Valor");
            typ.WriteLine("ElementId;Parametro;StorageType;Valor");
            bips.WriteLine("ElementId;BuiltInParameter;Valor");
            sol.WriteLine("ElementId;SolidIndex;Volume_m3;Volume_ft3;FaceCount;EdgeCount;BB_MinX;BB_MinY;BB_MinZ;BB_MaxX;BB_MaxY;BB_MaxZ");
            mesh.WriteLine("ElementId;MeshIndex;VertexCount;TriangleCount;BB_MinX;BB_MinY;BB_MinZ;BB_MaxX;BB_MaxY;BB_MaxZ");

            var geomOptions = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false, IncludeNonVisibleObjects = true };
            Array allBips = Enum.GetValues(typeof(BuiltInParameter));
            foreach (Element el in els)
            {
                long id = el.Id.Value;
                ElementId typeId = el.GetTypeId();
                Element type = Try(() => doc.GetElement(typeId));
                bool inPlace = Try(() => el is FamilyInstance fi && (fi.Symbol?.Family?.IsInPlace ?? false));
                var (x, y, z) = Location(el);
                BoundingBoxXYZ bb = Try(() => el.get_BoundingBox(null));
                ov.WriteLine($"{id};{Esc(el.UniqueId)};{Esc(el.Category?.Name ?? "SemCategoria")};{Esc(el.Name)};{typeId.Value};{Esc(type?.Name ?? "SemTipo")};{inPlace};{x};{y};{z};{Box(bb)}");

                foreach (Parameter p in el.Parameters)
                    inst.WriteLine($"{id};{Esc(p.Definition?.Name ?? "(SemNome)")};{p.StorageType};{Esc(Value(p))}");
                if (type != null)
                    foreach (Parameter p in type.Parameters)
                        typ.WriteLine($"{id};{Esc(p.Definition?.Name ?? "(SemNome)")};{p.StorageType};{Esc(Value(p))}");

                foreach (BuiltInParameter bip in allBips)
                {
                    string v = Try(() => { Parameter bp = el.get_Parameter(bip); return bp == null ? null : Value(bp); });
                    if (!string.IsNullOrEmpty(v)) bips.WriteLine($"{id};{bip};{Esc(v)}");
                }

                GeometryElement geom = Try(() => el.get_Geometry(geomOptions));
                int si = 0, mi = 0;
                if (geom != null)
                    foreach (GeometryObject go in geom) Geometry(go, id, ref si, ref mi, sol, mesh);
            }
        }

        private static void Geometry(GeometryObject go, long id, ref int si, ref int mi, StreamWriter sol, StreamWriter mesh)
        {
            switch (go)
            {
                case GeometryInstance gi:
                    GeometryElement g = gi.GetInstanceGeometry();
                    if (g != null) foreach (GeometryObject o in g) Geometry(o, id, ref si, ref mi, sol, mesh);
                    break;
                case Solid s when s.Volume > 0:
                    sol.WriteLine($"{id};{si++};{s.Volume * Foot3ToM3:G8};{s.Volume:G8};{s.Faces?.Size ?? 0};{s.Edges?.Size ?? 0};{Box(s.GetBoundingBox())}");
                    break;
                case Mesh m:
                    // a malha não tem caixa pronta na API: calcula pelos vértices
                    IList<XYZ> v = m.Vertices;
                    string box = v.Count == 0 ? "0;0;0;0;0;0"
                        : $"{v.Min(p => p.X)};{v.Min(p => p.Y)};{v.Min(p => p.Z)};{v.Max(p => p.X)};{v.Max(p => p.Y)};{v.Max(p => p.Z)}";
                    mesh.WriteLine($"{id};{mi++};{v.Count};{m.NumTriangles};{box}");
                    break;
            }
        }

        private static string Box(BoundingBoxXYZ b) =>
            $"{b?.Min.X ?? 0};{b?.Min.Y ?? 0};{b?.Min.Z ?? 0};{b?.Max.X ?? 0};{b?.Max.Y ?? 0};{b?.Max.Z ?? 0}";

        private static (double, double, double) Location(Element el) => Try(() => el.Location switch
        {
            LocationPoint lp when lp.Point != null => (lp.Point.X, lp.Point.Y, lp.Point.Z),
            LocationCurve lc when lc.Curve != null => (lc.Curve.GetEndPoint(0).X, lc.Curve.GetEndPoint(0).Y, lc.Curve.GetEndPoint(0).Z),
            _ => (0.0, 0.0, 0.0)
        });

        /// <summary>Valor legível: comprimentos em m e volumes em m³ (pelo nome do parâmetro), como no script.</summary>
        private static string Value(Parameter p) => Try(() =>
        {
            if (p == null || !p.HasValue) return "";
            switch (p.StorageType)
            {
                case StorageType.String: return p.AsString() ?? "";
                case StorageType.Integer: return p.AsInteger().ToString();
                case StorageType.ElementId: return p.AsElementId()?.Value.ToString() ?? "";
                case StorageType.Double:
                    double raw = p.AsDouble();
                    string name = p.Definition?.Name?.ToLowerInvariant() ?? "";
                    return name.Contains("volum") ? (raw * Foot3ToM3).ToString("G6") + " m3" : (raw * FootToMeter).ToString("G6") + " m";
                default: return "";
            }
        }) ?? "";

        private static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace(";", ",").Replace("\r", " ").Replace("\n", " ");

        private static T Try<T>(Func<T> f)
        {
            try { return f(); } catch { return default; }
        }
    }
}
