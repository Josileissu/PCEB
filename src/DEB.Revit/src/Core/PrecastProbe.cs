using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace DEB.Revit.Core
{
    /// <summary>
    /// Testes de viabilidade da API para pré-moldados (pilar + consolos aninhados na viga).
    /// Tudo roda dentro de uma transação que é desfeita no final: o modelo não é alterado.
    /// </summary>
    internal static class PrecastProbe
    {
        public static string Run(Document doc)
        {
            var sb = new StringBuilder();
            void W(string s) => sb.AppendLine(s);

            List<FamilyInstance> columns = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType().OfType<FamilyInstance>().ToList();
            List<FamilyInstance> beams = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType().OfType<FamilyInstance>().Where(f => f.SuperComponent == null).ToList();
            List<FamilyInstance> corbels = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Where(f => f.SuperComponent != null).ToList();

            FamilyInstance col = columns.FirstOrDefault();
            FamilyInstance beam = beams.FirstOrDefault();
            FamilyInstance corbel = corbels.FirstOrDefault(c => Touches(c, col)) ?? corbels.FirstOrDefault();
            W($"pilar={col?.Id.Value} viga={beam?.Id.Value} consolo={corbel?.Id.Value} (super={corbel?.SuperComponent?.Id.Value})");
            if (col == null || corbel == null) return Save(sb);

            using (var t = new Transaction(doc, "probe"))
            {
                t.Start();
                var failures = new List<string>();
                FailureHandlingOptions fho = t.GetFailureHandlingOptions();
                fho.SetFailuresPreprocessor(new Collector(failures));
                t.SetFailureHandlingOptions(fho);

                Try(doc, W, "P1 assembly [pilar + consolo]", () =>
                {
                    var ids = new List<ElementId> { col.Id, corbel.Id };
                    bool ok = AssemblyInstance.IsValidNamingCategory(doc, col.Category.Id, ids);
                    W($"   IsValidNamingCategory={ok}");
                    AssemblyInstance a = AssemblyInstance.Create(doc, ids, col.Category.Id);
                    doc.Regenerate();
                    W($"   criado {a.Id.Value}; membros={string.Join(",", a.GetMemberIds().Select(i => i.Value))}");
                });

                Try(doc, W, "P2 assembly [viga] (subcomponentes entram junto?)", () =>
                {
                    AssemblyInstance a = AssemblyInstance.Create(doc, new List<ElementId> { beam.Id }, beam.Category.Id);
                    doc.Regenerate();
                    W($"   membros={string.Join(",", a.GetMemberIds().Select(i => i.Value))}");
                });

                Try(doc, W, "P3 parts do pilar", () =>
                {
                    W($"   valid={PartUtils.AreElementsValidForCreateParts(doc, new List<ElementId> { col.Id })}");
                    PartUtils.CreateParts(doc, new List<ElementId> { col.Id });
                    doc.Regenerate();
                    ICollection<ElementId> parts = PartUtils.GetAssociatedParts(doc, col.Id, true, true);
                    W($"   parts={string.Join(",", parts.Select(i => i.Value))}");
                });

                Try(doc, W, "P4 parts da viga (consolos viram parts?)", () =>
                {
                    PartUtils.CreateParts(doc, new List<ElementId> { beam.Id });
                    doc.Regenerate();
                    ICollection<ElementId> parts = PartUtils.GetAssociatedParts(doc, beam.Id, true, true);
                    W($"   parts da viga={parts.Count}");
                    foreach (ElementId sub in beam.GetSubComponentIds())
                        W($"   sub {sub.Value}: hasParts={PartUtils.HasAssociatedParts(doc, sub)}");
                });

                Try(doc, W, "P5 barra hospedada no consolo", () =>
                {
                    BoundingBoxXYZ bb = corbel.get_BoundingBox(null);
                    XYZ c = (bb.Min + bb.Max) / 2;
                    RebarBarType bt = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().First();
                    var line = Line.CreateBound(new XYZ(bb.Min.X + U.Mm(30), c.Y, bb.Max.Z - U.Mm(40)),
                                                new XYZ(bb.Max.X - U.Mm(30), c.Y, bb.Max.Z - U.Mm(40)));
                    Rebar r = Rebar.CreateFromCurves(doc, RebarStyle.Standard, bt, null, null, corbel, XYZ.BasisY,
                        new List<Curve> { line }, RebarHookOrientation.Left, RebarHookOrientation.Left, true, true);
                    W($"   rebar {r.Id.Value} host={r.GetHostId().Value}");
                });

                Try(doc, W, "P6 barra hospedada no pilar atravessando para o consolo", () =>
                {
                    BoundingBoxXYZ cb = col.get_BoundingBox(null);
                    BoundingBoxXYZ kb = corbel.get_BoundingBox(null);
                    double z = kb.Max.Z - U.Mm(40);
                    double y = (kb.Min.Y + kb.Max.Y) / 2;
                    // do interior do pilar até a ponta do consolo
                    double x0 = Math.Abs(kb.Min.X - cb.Max.X) < Math.Abs(kb.Max.X - cb.Min.X) ? cb.Min.X + U.Mm(40) : cb.Max.X - U.Mm(40);
                    double x1 = Math.Abs(kb.Min.X - cb.Max.X) < Math.Abs(kb.Max.X - cb.Min.X) ? kb.Max.X - U.Mm(30) : kb.Min.X + U.Mm(30);
                    RebarBarType bt = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>().First();
                    Rebar r = Rebar.CreateFromCurves(doc, RebarStyle.Standard, bt, null, null, col, XYZ.BasisY,
                        new List<Curve> { Line.CreateBound(new XYZ(x0, y, z), new XYZ(x1, y, z)) },
                        RebarHookOrientation.Left, RebarHookOrientation.Left, true, true);
                    W($"   rebar {r.Id.Value} host={r.GetHostId().Value}");
                });

                Try(doc, W, "P7 DirectShape com o sólido do consolo", () =>
                {
                    var opt = new Options { DetailLevel = ViewDetailLevel.Fine };
                    GeometryElement ge = corbel.get_Geometry(opt);
                    List<GeometryObject> solids = Flatten(ge).Where(s => s.Volume > 1e-9).Cast<GeometryObject>().ToList();
                    DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_StructuralColumns));
                    ds.SetShape(solids);
                    W($"   directshape {ds.Id.Value}, sólidos={solids.Count}; hostValido={RebarHostData.IsValidHost(ds)}");
                    AssemblyInstance a = AssemblyInstance.Create(doc, new List<ElementId> { col.Id, ds.Id }, col.Category.Id);
                    doc.Regenerate();
                    W($"   assembly pilar+directshape: membros={string.Join(",", a.GetMemberIds().Select(i => i.Value))}");
                });

                Try(doc, W, "P8 sólido unido pilar ∪ consolo", () =>
                {
                    var opt = new Options { DetailLevel = ViewDetailLevel.Fine };
                    Solid sc = Flatten(col.get_Geometry(opt)).OrderByDescending(s => s.Volume).First();
                    Solid sk = Flatten(corbel.get_Geometry(opt)).OrderByDescending(s => s.Volume).First();
                    Solid u = BooleanOperationsUtils.ExecuteBooleanOperation(sc, sk, BooleanOperationsType.Union);
                    W($"   volumes: pilar={Vol(sc)} L, consolo={Vol(sk)} L, união={Vol(u)} L, faces união={u.Faces.Size}");
                });

                W("Falhas/avisos da transação: " + (failures.Count == 0 ? "nenhum" : ""));
                foreach (string f in failures.Distinct()) W("   " + f);

                t.RollBack();
            }
            W("Transação desfeita: modelo inalterado.");
            return Save(sb);
        }

        private static string Vol(Solid s) => UnitUtils.ConvertFromInternalUnits(s.Volume, UnitTypeId.Liters).ToString("0.0");

        private static IEnumerable<Solid> Flatten(GeometryElement ge)
        {
            foreach (GeometryObject go in ge)
            {
                if (go is Solid s && s.Volume > 1e-9) yield return s;
                else if (go is GeometryInstance gi)
                    foreach (Solid x in Flatten(gi.GetInstanceGeometry())) yield return x;
            }
        }

        private static bool Touches(FamilyInstance a, FamilyInstance b)
        {
            if (a == null || b == null) return false;
            BoundingBoxXYZ x = a.get_BoundingBox(null), y = b.get_BoundingBox(null);
            double tol = U.Mm(5);
            return x.Min.X <= y.Max.X + tol && x.Max.X >= y.Min.X - tol &&
                   x.Min.Y <= y.Max.Y + tol && x.Max.Y >= y.Min.Y - tol &&
                   x.Min.Z <= y.Max.Z + tol && x.Max.Z >= y.Min.Z - tol;
        }

        private static void Try(Document doc, Action<string> w, string name, Action a)
        {
            w(name);
            using (var st = new SubTransaction(doc))
            {
                st.Start();
                try { a(); w("   → OK"); }
                catch (Exception ex) { w($"   → FALHOU: {ex.GetType().Name}: {ex.Message}"); }
                finally { st.RollBack(); }
            }
        }

        private static string Save(StringBuilder sb)
        {
            Directory.CreateDirectory(DetailingReport.LogDir);
            string path = Path.Combine(DetailingReport.LogDir, $"probe_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        private sealed class Collector : IFailuresPreprocessor
        {
            private readonly List<string> _list;
            public Collector(List<string> list) { _list = list; }

            public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
            {
                foreach (FailureMessageAccessor f in fa.GetFailureMessages())
                {
                    _list.Add($"[{f.GetSeverity()}] {f.GetDescriptionText()}");
                    if (f.GetSeverity() == FailureSeverity.Warning) fa.DeleteWarning(f);
                }
                return FailureProcessingResult.Continue;
            }
        }
    }
}
