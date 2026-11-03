using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace StructuralDetailer.Core
{
    /// <summary>
    /// Levantamento do modelo (somente leitura) para planejar o detalhamento de pré-moldados:
    /// pilares, vigas, subcomponentes aninhados (consolos), geometria, contatos entre peças,
    /// aptidão para Parts / armadura e a estrutura interna das famílias.
    /// Grava um JSON em %AppData%\StructuralDetailer\logs\.
    /// </summary>
    internal static class ModelDiagnostics
    {
        private static readonly BuiltInCategory[] PieceCategories =
        {
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_GenericModel,
            BuiltInCategory.OST_StructConnections,
            BuiltInCategory.OST_StructuralFoundation
        };

        public static string Run(Document doc)
        {
            var root = new Dictionary<string, object>
            {
                ["data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ["modelo"] = doc.Title,
                ["caminho"] = doc.PathName,
                ["revit"] = doc.Application.VersionNumber,
                ["unidadeComprimento"] = doc.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId().TypeId
            };

            List<FamilyInstance> all = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Where(fi => fi.Category != null && PieceCategories.Contains(fi.Category.BuiltInCategory))
                .ToList();

            root["contagemPorCategoria"] = all.GroupBy(fi => fi.Category.Name)
                .ToDictionary(g => g.Key, g => (object)g.Count());

            // Só as peças "de topo" (os subcomponentes aparecem dentro delas).
            root["pecas"] = all.Where(fi => fi.SuperComponent == null)
                .OrderBy(fi => fi.Category.BuiltInCategory).ThenBy(fi => fi.Id.Value)
                .Select(fi => DescribeInstance(doc, fi, all, 0))
                .ToList();

            root["familias"] = all.Where(fi => fi.SuperComponent == null)
                .Select(fi => fi.Symbol.Family).GroupBy(f => f.Id).Select(g => g.First())
                .Select(f => DescribeFamily(doc, f))
                .ToList();

            root["assemblies"] = new FilteredElementCollector(doc).OfClass(typeof(AssemblyInstance))
                .Cast<AssemblyInstance>()
                .Select(a => new Dictionary<string, object>
                {
                    ["id"] = a.Id.Value,
                    ["tipo"] = a.AssemblyTypeName,
                    ["membros"] = a.GetMemberIds().Select(id => id.Value).ToList()
                }).ToList();

            root["parts"] = new FilteredElementCollector(doc).OfClass(typeof(Part)).Cast<Part>()
                .Select(p => new Dictionary<string, object>
                {
                    ["id"] = p.Id.Value,
                    ["origem"] = p.GetSourceElementIds().Select(l => l.HostElementId.Value).ToList()
                }).ToList();

            root["tiposDeBarra"] = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
                .Select(t => $"{t.Name} (Ø {U.ToMm(t.BarNominalDiameter):0.#} mm)").ToList();

            root["tiposDeGancho"] = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
                .Select(t => $"{t.Name} ({t.HookAngle * 180 / Math.PI:0}°, {t.Style})").ToList();

            root["formasDeBarra"] = new FilteredElementCollector(doc).OfClass(typeof(RebarShape))
                .Select(s => s.Name).OrderBy(n => n).ToList();

            Directory.CreateDirectory(DetailingReport.LogDir);
            string path = Path.Combine(DetailingReport.LogDir, $"diagnostico_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            var opt = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            File.WriteAllText(path, JsonSerializer.Serialize(root, opt), Encoding.UTF8);
            return path;
        }

        // ------------------------------------------------------------------

        private static Dictionary<string, object> DescribeInstance(Document doc, FamilyInstance fi, List<FamilyInstance> all, int depth)
        {
            var d = new Dictionary<string, object>
            {
                ["id"] = fi.Id.Value,
                ["categoria"] = fi.Category?.Name,
                ["familia"] = fi.Symbol.FamilyName,
                ["tipo"] = fi.Symbol.Name,
                ["marca"] = fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString(),
                ["materialEstrutural"] = fi.StructuralMaterialType.ToString(),
                ["materialEstruturalNome"] = (doc.GetElement(fi.StructuralMaterialId) as Material)?.Name,
                ["usoEstrutural"] = Safe(() => fi.StructuralUsage.ToString()),
                ["tipoEstrutural"] = fi.StructuralType.ToString(),
                ["localizacao"] = DescribeLocation(fi.Location),
                ["hand"] = V(fi.HandOrientation),
                ["facing"] = V(fi.FacingOrientation),
                ["espelhado"] = fi.Mirrored,
                ["caixa"] = Box(fi.get_BoundingBox(null)),
                ["hospedeiroArmaduraValido"] = Safe(() => RebarHostData.IsValidHost(fi)),
                ["armadurasHospedadas"] = Safe(() => RebarHostData.GetRebarHostData(fi)?.GetRebarsInHost().Count ?? 0),
                ["podeCriarParts"] = Safe(() => PartUtils.AreElementsValidForCreateParts(doc, new List<ElementId> { fi.Id })),
                ["temParts"] = Safe(() => PartUtils.HasAssociatedParts(doc, fi.Id)),
                ["assembly"] = fi.AssemblyInstanceId == ElementId.InvalidElementId ? null : (object)fi.AssemblyInstanceId.Value,
                ["unidoCom"] = Safe(() => JoinGeometryUtils.GetJoinedElements(doc, fi).Select(id => Label(doc, id)).ToList()),
                ["parametros"] = Params(fi),
                ["parametrosTipo"] = Params(fi.Symbol),
                ["geometria"] = Geometry(fi)
            };

            if (fi.SuperComponent != null) d["superComponente"] = fi.SuperComponent.Id.Value;
            if (depth == 0) d["encostaEm"] = Touching(doc, fi, all);

            List<ElementId> subs = fi.GetSubComponentIds().ToList();
            if (subs.Count > 0 && depth < 3)
            {
                d["subcomponentes"] = subs.Select(id => doc.GetElement(id)).OfType<FamilyInstance>()
                    .Select(s =>
                    {
                        Dictionary<string, object> sd = DescribeInstance(doc, s, all, depth + 1);
                        sd["encostaEm"] = Touching(doc, s, all);
                        return sd;
                    }).ToList();
            }
            return d;
        }

        /// <summary>Elementos (peças ou subcomponentes de outras peças) cuja caixa toca a deste, com 5 mm de folga.</summary>
        private static List<object> Touching(Document doc, FamilyInstance fi, List<FamilyInstance> all)
        {
            BoundingBoxXYZ bb = fi.get_BoundingBox(null);
            if (bb == null) return new List<object>();
            double tol = U.Mm(5);
            var outline = new Outline(bb.Min - new XYZ(tol, tol, tol), bb.Max + new XYZ(tol, tol, tol));

            HashSet<ElementId> family = new HashSet<ElementId> { fi.Id };
            if (fi.SuperComponent != null) family.Add(fi.SuperComponent.Id);
            foreach (ElementId s in fi.GetSubComponentIds()) family.Add(s);

            var result = new List<object>();
            foreach (FamilyInstance o in all.Where(o => !family.Contains(o.Id)))
            {
                BoundingBoxXYZ ob = o.get_BoundingBox(null);
                if (ob == null) continue;
                if (!outline.Intersects(new Outline(ob.Min, ob.Max), 0)) continue;

                bool solidIntersect = Safe(() => new FilteredElementCollector(doc, new List<ElementId> { o.Id })
                    .WherePasses(new ElementIntersectsElementFilter(fi)).Any());
                result.Add(new Dictionary<string, object>
                {
                    ["elemento"] = Label(doc, o.Id),
                    ["superComponente"] = o.SuperComponent == null ? null : Label(doc, o.SuperComponent.Id),
                    ["solidosSeInterceptam"] = solidIntersect
                });
            }
            return result;
        }

        private static object DescribeLocation(Location loc)
        {
            switch (loc)
            {
                case LocationPoint lp:
                    return new Dictionary<string, object> { ["ponto"] = P(lp.Point), ["rotacaoGraus"] = Math.Round(lp.Rotation * 180 / Math.PI, 3) };
                case LocationCurve lc:
                    return new Dictionary<string, object>
                    {
                        ["inicio"] = P(lc.Curve.GetEndPoint(0)),
                        ["fim"] = P(lc.Curve.GetEndPoint(1)),
                        ["tipoCurva"] = lc.Curve.GetType().Name,
                        ["comprimentoMm"] = Math.Round(U.ToMm(lc.Curve.Length), 1)
                    };
                default:
                    return null;
            }
        }

        private static List<object> Geometry(Element e)
        {
            var opt = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = false };
            var list = new List<object>();
            GeometryElement ge = e.get_Geometry(opt);
            Walk(e.Document, ge, list, 0);
            GC.KeepAlive(ge);
            return list;
        }

        private static void Walk(Document doc, GeometryElement ge, List<object> list, int depth)
        {
            if (ge == null || depth > 3) return;
            foreach (GeometryObject go in ge)
            {
                if (go is Solid s && s.Volume > 1e-9)
                {
                    BoundingBoxXYZ bb = s.GetBoundingBox();
                    list.Add(new Dictionary<string, object>
                    {
                        ["solido"] = true,
                        ["nivel"] = depth,
                        ["volumeL"] = Math.Round(UnitUtils.ConvertFromInternalUnits(s.Volume, UnitTypeId.Liters), 2),
                        ["faces"] = s.Faces.Size,
                        ["facesPlanas"] = s.Faces.Cast<Face>().Count(f => f is PlanarFace),
                        ["material"] = s.GraphicsStyleId == ElementId.InvalidElementId ? null : (object)s.GraphicsStyleId.Value,
                        ["caixaLocal"] = bb == null ? null : Box(bb.Min, bb.Max)
                    });
                }
                else if (go is GeometryInstance gi)
                {
                    Element sym = doc.GetElement(gi.GetSymbolGeometryId().SymbolId);
                    var inst = new Dictionary<string, object>
                    {
                        ["instancia"] = sym?.Name,
                        ["familia"] = (sym as FamilySymbol)?.FamilyName,
                        ["nivel"] = depth,
                        ["origem"] = P(gi.Transform.Origin)
                    };
                    var children = new List<object>();
                    Walk(doc, gi.GetSymbolGeometry(), children, depth + 1);
                    inst["filhos"] = children;
                    list.Add(inst);
                }
            }
        }

        /// <summary>Abre a família só para leitura e lista famílias aninhadas e parâmetros.</summary>
        private static Dictionary<string, object> DescribeFamily(Document doc, Family fam)
        {
            var d = new Dictionary<string, object>
            {
                ["nome"] = fam.Name,
                ["categoria"] = fam.FamilyCategory?.Name,
                ["noLocal"] = fam.IsInPlace,
                ["compartilhada"] = fam.get_Parameter(BuiltInParameter.FAMILY_SHARED)?.AsInteger() == 1,
                ["tipos"] = fam.GetFamilySymbolIds().Select(id => doc.GetElement(id).Name).ToList()
            };
            if (fam.IsInPlace || !fam.IsEditable) return d;

            Document fd = null;
            try
            {
                fd = doc.EditFamily(fam);
                d["aninhadas"] = new FilteredElementCollector(fd).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                    .GroupBy(fi => fi.Symbol.FamilyName + " : " + fi.Symbol.Name)
                    .Select(g =>
                    {
                        FamilyInstance f = g.First();
                        Family nf = f.Symbol.Family;
                        return new Dictionary<string, object>
                        {
                            ["familiaTipo"] = g.Key,
                            ["quantidade"] = g.Count(),
                            ["categoria"] = nf.FamilyCategory?.Name,
                            ["compartilhada"] = nf.get_Parameter(BuiltInParameter.FAMILY_SHARED)?.AsInteger() == 1,
                            ["visivel"] = g.Select(i => i.get_Parameter(BuiltInParameter.IS_VISIBLE_PARAM)?.AsInteger()).Distinct().ToList()
                        };
                    }).ToList();

                d["parametrosFamilia"] = fd.FamilyManager.Parameters.Cast<FamilyParameter>()
                    .OrderBy(p => p.Definition.Name)
                    .Select(p => $"{p.Definition.Name} [{(p.IsInstance ? "inst" : "tipo")}{(p.IsShared ? ", comp." : "")}" +
                                 $"{(string.IsNullOrEmpty(p.Formula) ? "" : ", fórmula: " + p.Formula)}]")
                    .ToList();

                d["solidosNaFamilia"] = new FilteredElementCollector(fd).OfClass(typeof(GenericForm)).Cast<GenericForm>()
                    .Select(g => $"{g.GetType().Name} {(g.IsSolid ? "sólido" : "vazio")} (id {g.Id.Value})").ToList();
            }
            catch (Exception ex)
            {
                d["erroAoAbrir"] = ex.Message;
            }
            finally
            {
                fd?.Close(false);
            }
            return d;
        }

        private static Dictionary<string, string> Params(Element e)
        {
            var d = new Dictionary<string, string>();
            foreach (Parameter p in e.Parameters)
            {
                if (!p.HasValue) continue;
                string v = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
                if (string.IsNullOrEmpty(v)) continue;
                string key = p.Definition.Name;
                if (!d.ContainsKey(key)) d[key] = v;
            }
            return d;
        }

        private static string Label(Document doc, ElementId id)
        {
            Element e = doc.GetElement(id);
            if (e == null) return id.Value.ToString();
            string fam = (e as FamilyInstance)?.Symbol.FamilyName;
            return $"{e.Category?.Name} | {fam} : {e.Name} [{id.Value}]";
        }

        private static double[] P(XYZ p) => p == null ? null : new[] { R(p.X), R(p.Y), R(p.Z) };
        private static double R(double ft) => Math.Round(U.ToMm(ft), 1);   // mm
        private static double[] V(XYZ v) => v == null ? null : new[] { Math.Round(v.X, 4), Math.Round(v.Y, 4), Math.Round(v.Z, 4) };

        private static object Box(BoundingBoxXYZ bb) => bb == null ? null : Box(bb.Min, bb.Max);

        private static object Box(XYZ min, XYZ max) => new Dictionary<string, object>
        {
            ["min"] = P(min),
            ["max"] = P(max),
            ["dimMm"] = P(max - min)
        };

        private static T Safe<T>(Func<T> f)
        {
            try { return f(); } catch { return default; }
        }
    }
}
