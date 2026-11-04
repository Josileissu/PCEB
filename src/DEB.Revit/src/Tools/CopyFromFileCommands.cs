using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DEB.Revit.Tools
{
    /// <summary>
    /// Copia elementos de outro arquivo .rvt para o projeto atual, na mesma posição: abre o arquivo de origem em segundo
    /// plano, mostra a lista para marcar, copia e fecha a origem sem salvar. Se o arquivo escolhido já estiver aberto no
    /// Revit, usa o documento aberto e não o fecha. Tipos com o mesmo nome: usa os do projeto atual.
    /// </summary>
    public abstract class CopyFromFileCommand : ToolCommand
    {
        protected abstract string Noun { get; }   // "fundações"

        /// <summary>Itens oferecidos (id na origem, texto da lista).</summary>
        internal abstract List<(ElementId Id, string Text)> Collect(Document source);

        /// <summary>Copia os itens marcados; devolve o resumo. Já dentro de uma transação no destino.</summary>
        internal abstract string Copy(Document source, Document target, List<ElementId> ids, CopyPasteOptions options);

        protected override Result Run(UIApplication uiapp, UIDocument uidoc, Document target)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Arquivo de origem (com as {Noun})",
                Filter = "Projetos do Revit (*.rvt)|*.rvt"
            };
            if (dlg.ShowDialog() != true) throw new ToolCancelled();

            string path = Path.GetFullPath(dlg.FileName);
            if (string.Equals(target.PathName, path, StringComparison.OrdinalIgnoreCase))
                throw new ToolCancelled("O arquivo escolhido é o próprio projeto atual.");

            Document source = uiapp.Application.Documents.Cast<Document>()
                .FirstOrDefault(d => string.Equals(d.PathName, path, StringComparison.OrdinalIgnoreCase));
            bool openedHere = source == null;
            if (openedHere) source = uiapp.Application.OpenDocumentFile(path);
            try
            {
                List<(ElementId Id, string Text)> items = Collect(source);
                if (items.Count == 0) throw new ToolCancelled($"Nenhum item ({Noun}) no arquivo de origem.");

                List<ElementId> chosen = new CheckListWindow<ElementId>($"{Title} — {Path.GetFileName(path)}",
                    items.OrderBy(i => i.Text, StringComparer.OrdinalIgnoreCase).Select(i => (i.Id, i.Text)), Noun).Show();
                if (chosen.Count == 0) throw new ToolCancelled();

                var options = new CopyPasteOptions();
                options.SetDuplicateTypeNamesHandler(new UseTargetTypes());
                string summary;
                using (var t = new Transaction(target, Title))
                {
                    t.Start();
                    summary = Copy(source, target, chosen, options);
                    t.Commit();
                }
                TaskDialog.Show(Title, summary);
                return Result.Succeeded;
            }
            finally
            {
                if (openedHere) source.Close(false);
            }
        }

        internal sealed class UseTargetTypes : IDuplicateTypeNamesHandler
        {
            public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args) => DuplicateTypeAction.UseDestinationTypes;
        }
    }

    /// <summary>Fundações (instâncias, sapatas corridas e montagens que contêm fundações). Ex-script CopiarFundacoesEntreArquivos.</summary>
    [Transaction(TransactionMode.Manual)]
    public class CopyFoundationsCommand : CopyFromFileCommand
    {
        protected override string Title => "Copiar fundações de outro arquivo";
        protected override string Noun => "fundações";

        internal override List<(ElementId, string)> Collect(Document doc)
        {
            var items = new Dictionary<ElementId, Element>();
            foreach (Element e in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFoundation).WhereElementIsNotElementType())
                items[e.Id] = e;
            foreach (Element e in new FilteredElementCollector(doc).OfClass(typeof(WallFoundation)).WhereElementIsNotElementType())
                items[e.Id] = e;
            foreach (AssemblyInstance a in new FilteredElementCollector(doc).OfClass(typeof(AssemblyInstance)).Cast<AssemblyInstance>())
                if (a.GetMemberIds().Any(id => ElementTools.IsCategory(doc.GetElement(id), BuiltInCategory.OST_StructuralFoundation)))
                    items[a.Id] = a;
            return items.Values.Select(e => (e.Id, Describe(e))).ToList();
        }

        private static string Describe(Element e)
        {
            string kind = e is AssemblyInstance ? "Montagem" : e is WallFoundation ? "Sapata corrida"
                        : e is FamilyInstance fi ? fi.Symbol?.FamilyName ?? "Sapata" : e.GetType().Name;
            string mark = ElementTools.MarkText(e);
            return $"[{kind}] {(mark.Length > 0 ? mark : "(sem marca)")} - {e.Name}";
        }

        internal override string Copy(Document source, Document target, List<ElementId> ids, CopyPasteOptions options)
        {
            ICollection<ElementId> copied = ElementTransformUtils.CopyElements(source, ids, target, Transform.Identity, options);
            return $"Copiados {copied.Count} elementos ({ids.Count} fundações marcadas) para o projeto atual.";
        }
    }

    /// <summary>
    /// Montagens com os membros, as vistas da montagem e as folhas onde elas estão. Ex-script CopiarMontagensComVistas.
    /// Cada montagem é copiada numa subtransação: se uma falhar, as outras seguem e a falha aparece no resumo.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CopyAssembliesCommand : CopyFromFileCommand
    {
        protected override string Title => "Copiar montagens com vistas";
        protected override string Noun => "montagens";

        internal override List<(ElementId, string)> Collect(Document doc)
        {
            List<View> views = AssemblyViews(doc);
            return new FilteredElementCollector(doc).OfClass(typeof(AssemblyInstance)).Cast<AssemblyInstance>()
                .Select(a =>
                {
                    string name = a.get_Parameter(BuiltInParameter.ASSEMBLY_NAME)?.AsString() ?? a.Name;
                    return (a.Id, $"{name} ({views.Count(v => v.AssociatedAssemblyInstanceId == a.Id)} vista(s)) - ID: {a.Id}");
                }).ToList();
        }

        internal override string Copy(Document source, Document target, List<ElementId> ids, CopyPasteOptions options)
        {
            List<View> views = AssemblyViews(source);
            List<ViewSheet> sheets = new FilteredElementCollector(source).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().ToList();
            int assemblies = 0, viewCount = 0, sheetCount = 0;
            var failures = new List<string>();
            foreach (ElementId id in ids)
            {
                var a = (AssemblyInstance)source.GetElement(id);
                var toCopy = new List<ElementId> { id };
                toCopy.AddRange(a.GetMemberIds());
                List<View> mine = views.Where(v => v.AssociatedAssemblyInstanceId == id).ToList();
                toCopy.AddRange(mine.Select(v => v.Id));
                List<ViewSheet> mySheets = sheets.Where(s => s.GetAllPlacedViews().Any(v => mine.Any(m => m.Id == v))).ToList();
                toCopy.AddRange(mySheets.Select(s => s.Id));

                using var st = new SubTransaction(target);
                st.Start();
                try
                {
                    ElementTransformUtils.CopyElements(source, toCopy.Distinct().ToList(), target, Transform.Identity, options);
                    st.Commit();
                    assemblies++; viewCount += mine.Count; sheetCount += mySheets.Count;
                }
                catch (Exception ex)
                {
                    st.RollBack();
                    failures.Add($"{a.get_Parameter(BuiltInParameter.ASSEMBLY_NAME)?.AsString() ?? a.Name}: {ex.Message}");
                }
            }
            string s = $"Copiadas {assemblies} montagens\nCopiadas {viewCount} vistas da montagem\nCopiadas {sheetCount} folhas";
            if (failures.Count > 0) s += $"\n\nNão copiadas ({failures.Count}):\n" + string.Join("\n", failures.Take(15));
            return s;
        }

        private static List<View> AssemblyViews(Document doc) =>
            new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => v.AssociatedAssemblyInstanceId != ElementId.InvalidElementId).ToList();
    }
}
