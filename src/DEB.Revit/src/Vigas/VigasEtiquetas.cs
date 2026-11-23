using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DEB.Revit.Core;
using DEB.Revit.Precast;
using DEB.Revit.Presentation;

namespace DEB.Revit.Vigas
{
    /// <summary>
    /// Etiquetas das barras na elevação da montagem da viga (D40), com a família de etiqueta de vergalhão do padrão do escritório
    /// (a mesma do pilar: tipos "1 N1 Ø C" para as barras e "1 N1 c/" para os estribos). Barras de cima (superior, pele, tirante,
    /// grampo e costura) etiquetadas acima da viga; inferior, estribos, suspensão e estribos do dente, abaixo; linhas
    /// escalonadas para não encavalar. Sem a família no projeto nem no arquivo do padrão: não etiqueta (aviso no log).
    /// </summary>
    internal static class VigasEtiquetas
    {
        public static int Etiquetar(Document doc, View view, AssemblyInstance asm, Element viga, PieceLog log)
        {
            PresentationStandard std = PresentationStandard.Load("PilarPreMoldado", out _);
            string fam = std?.Armadura?.FamiliaEtiqueta?.Trim() ?? "", arq = std?.Armadura?.ArquivoEtiqueta?.Trim() ?? "";
            FamilySymbol Tipo(string nome)
            {
                FamilySymbol Achar() => new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RebarTags).WhereElementIsElementType()
                    .OfType<FamilySymbol>().FirstOrDefault(s => (fam == "" || string.Equals(s.FamilyName, fam, StringComparison.OrdinalIgnoreCase)) &&
                                                                (string.IsNullOrWhiteSpace(nome) || string.Equals(s.Name, nome, StringComparison.OrdinalIgnoreCase)));
                FamilySymbol t = Achar();
                if (t == null && arq != "" && System.IO.File.Exists(arq))
                {
                    try { bool ok = doc.LoadFamily(arq, out Family _); if (!ok) log.Info("Etiquetas: LoadFamily devolveu falso."); }
                    catch (Exception ex) { log.Warn("Etiquetas: família do padrão não carregada: " + ex.Message); }
                    t = Achar();
                    if (t == null) log.Info("Etiquetas: tipos no projeto: " + string.Join(", ", new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RebarTags).WhereElementIsElementType().OfType<FamilySymbol>().Select(s => s.FamilyName + ":" + s.Name)));
                }
                if (t != null && !t.IsActive) t.Activate();
                return t;
            }
            FamilySymbol barra = Tipo(std?.Armadura?.EtiquetaLongitudinal), estribo = Tipo(std?.Armadura?.EtiquetaEstribo) ?? barra;
            if (barra == null)
            {
                barra = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RebarTags).WhereElementIsElementType().OfType<FamilySymbol>().FirstOrDefault();
                estribo = barra;
            }
            log.Info($"Etiquetas: padrão \"{std?.Codigo}\" família \"{fam}\" → {(barra == null ? "não achada" : barra.FamilyName + " : " + barra.Name)}.");
            if (barra == null) { log.Warn("Nenhuma etiqueta de vergalhão no projeto nem no padrão do escritório: elevação sem etiquetas."); return 0; }

            XYZ dir = view.RightDirection, cima = view.UpDirection;
            var rebars = asm.GetMemberIds().Select(id => doc.GetElement(id)).OfType<Rebar>().ToList();
            BoundingBoxXYZ caixa = viga.get_BoundingBox(null);
            double topo = Math.Max(caixa.Min.DotProduct(cima), caixa.Max.DotProduct(cima)), fundo = Math.Min(caixa.Min.DotProduct(cima), caixa.Max.DotProduct(cima));
            double passo = U.Mm(150 * view.Scale / 25.0), largura = U.Mm(900 * view.Scale / 25.0);
            var ocupado = new Dictionary<(bool, int), List<double>>();
            int n = 0;
            foreach (Rebar r in rebars.OrderBy(r => Centro(r).DotProduct(dir)))
            {
                string papel = (r.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "").Split('|').LastOrDefault() ?? "";
                if (papel == RebarFactory.RoleLabel(DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D.BarRole.LiftLoop) ||
                    papel == RebarFactory.RoleLabel(DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D.BarRole.BeamWelded)) continue;
                XYZ c = Centro(r);
                bool tipoEstribo = papel.StartsWith("Estribo") || papel.StartsWith("Suspensão") || papel == "Grampo da aba" || papel == "Ligação com a capa";
                bool acima = papel.EndsWith("ressalto") || papel == "Ligação com a capa" ||
                             (!tipoEstribo && !papel.EndsWith("da aba") && (c.DotProduct(cima) > (topo + fundo) / 2 || papel.StartsWith("Tirante") ||
                                                                         papel.StartsWith("Grampo") || papel.StartsWith("Costura")));
                double x = c.DotProduct(dir);
                int linha = 0;
                while (ocupado.TryGetValue((acima, linha), out var xs) && xs.Any(o => Math.Abs(o - x) < largura)) linha++;
                if (!ocupado.ContainsKey((acima, linha))) ocupado[(acima, linha)] = new List<double>();
                ocupado[(acima, linha)].Add(x);
                double h = acima ? topo + U.Mm(350 * view.Scale / 25.0) + linha * passo : fundo - U.Mm(1050 * view.Scale / 25.0) - linha * passo;   // abaixo da cota total e das marcas de corte
                XYZ at = c + cima * (h - c.DotProduct(cima));
                if (Criar(doc, tipoEstribo ? estribo : barra, view, r, at)) n++;
                else if (UltimoErro != null) { log.Warn($"Etiqueta não criada ({barra.FamilyName}: {UltimoErro})."); UltimoErro = null; }
            }
            return n;
        }

        private static XYZ Centro(Rebar r)
        {
            try
            {
                IList<Curve> cs = r.GetTransformedCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                if (cs.Count > 0)
                {
                    XYZ s = XYZ.Zero; int k = 0;
                    foreach (Curve c in cs) { s += c.Evaluate(0.5, true); k++; }
                    XYZ p = s / k;
                    // conjunto: o meio do conjunto (1ª e última barra)
                    int nb = r.NumberOfBarPositions;
                    if (nb > 1)
                    {
                        IList<Curve> ult = r.GetTransformedCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, nb - 1);
                        if (ult.Count > 0) p = (p + ult[0].Evaluate(0.5, true)) / 2;
                    }
                    return p;
                }
            }
            catch { }
            BoundingBoxXYZ b = r.get_BoundingBox(null);
            return (b.Min + b.Max) / 2;
        }

        public static string UltimoErro;

        private static bool Criar(Document doc, FamilySymbol tipo, View view, Rebar r, XYZ at)
        {
            var refs = new List<Reference> { new Reference(r) };
            try
            {
                GeometryElement ge = r.get_Geometry(new Options { View = view, ComputeReferences = true });
                if (ge != null)
                    foreach (GeometryObject go in ge)
                    {
                        if (go is Curve c && c.Reference != null) refs.Add(c.Reference);
                        else if (go is Solid so) foreach (Edge e in so.Edges) { if (e.Reference != null) refs.Add(e.Reference); break; }
                    }
            }
            catch { }
            foreach (Reference rf in refs.Take(12))
                using (var st = new SubTransaction(doc))
                {
                    st.Start();
                    try
                    {
                        IndependentTag t = IndependentTag.Create(doc, tipo.Id, view.Id, rf, true, TagOrientation.Horizontal, at);
                        try { t.TagHeadPosition = at; } catch { }
                        st.Commit(); return true;
                    }
                    catch (Exception ex) { UltimoErro = ex.Message; st.RollBack(); }
                }
            return false;
        }
    }
}
