using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace DEB.Revit.Analise
{
    /// <summary>
    /// Famílias PCEB das ligações metálicas (D49): "PCEB Chapa" (caixa: comprimento × largura no plano de trabalho, espessura para
    /// fora dele) e "PCEB Parafuso" (prisma de seção quadrada com lado = diâmetro, centrado no plano — representação para coordenação).
    /// Categoria Conexões estruturais, baseadas em plano de trabalho (colocadas em qualquer orientação). Criadas pela API a partir do
    /// modelo genérico métrico, com as faces presas a planos de referência cotados (como as famílias da D36), gravadas em
    /// %AppData%\PCEB\familias e carregadas no projeto. O Steel Connections do Revit não é usado.
    /// </summary>
    internal static class FamiliasLigacoes
    {
        public const string Chapa = "PCEB Chapa", Parafuso = "PCEB Parafuso";
        public const string PComp = "PCEB_Comprimento", PLarg = "PCEB_Largura", PEsp = "PCEB_Espessura", PLigacao = "PCEB_Ligação";
        private const double Pe = 304.8;
        private static double Ft(double mm) => mm / Pe;

        public static string Pasta => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCEB", "familias");

        /// <summary>Garante as duas famílias no projeto (cria e carrega o que faltar). Devolve os tipos (chapa, parafuso).</summary>
        public static (FamilySymbol Chapa, FamilySymbol Parafuso) Garantir(Document doc, StringBuilder log)
        {
            FamilySymbol Achar(string nome) => new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().FirstOrDefault(s => s.FamilyName == nome);
            FamilySymbol ch = Achar(Chapa), pa = Achar(Parafuso);
            if (ch == null) ch = Carregar(doc, Criar(doc.Application, Chapa, false, log), Chapa, log);
            if (pa == null) pa = Carregar(doc, Criar(doc.Application, Parafuso, true, log), Parafuso, log);
            return (ch, pa);
        }

        private static FamilySymbol Carregar(Document doc, string arquivo, string nome, StringBuilder log)
        {
            if (arquivo == null) return null;
            using (var t = new Transaction(doc, "PCEB: carregar " + nome))
            {
                t.Start();
                doc.LoadFamily(arquivo, new Sobrescrever(), out Family f);
                t.Commit();
                f ??= new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().FirstOrDefault(x => x.Name == nome);
                if (f == null) { log.AppendLine($"Família {nome} não carregou."); return null; }
            }
            return new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().FirstOrDefault(s => s.FamilyName == nome);
        }

        private static string Modelo(Autodesk.Revit.ApplicationServices.Application app)
        {
            string raiz = app.FamilyTemplatePath;
            foreach (string c in new[]
            {
                Path.Combine(raiz, "Modelo genérico métrico.rft"), Path.Combine(raiz, "Metric Generic Model.rft"),
                @"C:\ProgramData\Autodesk\RVT " + app.VersionNumber + @"\Family Templates\Portuguese\Modelo genérico métrico.rft",
                @"C:\ProgramData\Autodesk\RVT " + app.VersionNumber + @"\Family Templates\English\Metric Generic Model.rft",
            })
                if (File.Exists(c)) return c;
            return null;
        }

        /// <summary>Cria a família (caixa paramétrica) e grava o .rfa; null se não deu.</summary>
        private static string Criar(Autodesk.Revit.ApplicationServices.Application app, string nome, bool centradaNoPlano, StringBuilder log)
        {
            string modelo = Modelo(app);
            if (modelo == null) { log.AppendLine("Modelo de família genérico métrico não encontrado."); return null; }
            Directory.CreateDirectory(Pasta);
            string arq = Path.Combine(Pasta, nome + ".rfa");
            Document fd = app.NewFamilyDocument(modelo);
            try
            {
                View planta = new FilteredElementCollector(fd).OfClass(typeof(ViewPlan)).Cast<View>().First(v => !v.IsTemplate);
                var planos = new FilteredElementCollector(fd).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>().ToList();
                ReferencePlane centroX = planos.First(p => p.Normal.IsAlmostEqualTo(XYZ.BasisX) || p.Normal.IsAlmostEqualTo(-XYZ.BasisX));
                ReferencePlane centroY = planos.First(p => p.Normal.IsAlmostEqualTo(XYZ.BasisY) || p.Normal.IsAlmostEqualTo(-XYZ.BasisY));
                using (var t = new Transaction(fd, "PCEB: " + nome))
                {
                    t.Start();
                    Family fam = fd.OwnerFamily;
                    try { fam.FamilyCategory = fd.Settings.Categories.get_Item(BuiltInCategory.OST_StructConnections); }
                        catch (Exception exCat) { log.AppendLine($"{nome}: categoria Conexões estruturais recusada ({exCat.Message}); fica Modelo genérico."); }
                    Parameter wpb = fam.get_Parameter(BuiltInParameter.FAMILY_WORK_PLANE_BASED);
                    bool okWpb = wpb != null && !wpb.IsReadOnly && wpb.Set(1);
                    if (!okWpb || wpb.AsInteger() != 1)
                    {
                        // a categoria pode não aceitar: volta a Modelo genérico e tenta de novo
                        fam.FamilyCategory = fd.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel);
                        wpb = fam.get_Parameter(BuiltInParameter.FAMILY_WORK_PLANE_BASED);
                        okWpb = wpb != null && !wpb.IsReadOnly && wpb.Set(1);
                    }
                    // "Sempre vertical" ligado (padrão do modelo genérico) faz a instância ignorar a orientação do plano de trabalho
                    Parameter vert = fam.get_Parameter(BuiltInParameter.FAMILY_ALWAYS_VERTICAL);
                    if (vert != null && !vert.IsReadOnly) vert.Set(0);
                    log.AppendLine($"{nome}: sempre vertical = {vert?.AsInteger()}.");
                    log.AppendLine($"{nome}: categoria {fam.FamilyCategory?.Name}, baseada em plano de trabalho = {wpb?.AsInteger()} (leitura {(wpb?.IsReadOnly == true ? "só" : "e escrita")}).");
                    FamilyManager fm = fd.FamilyManager;
                    if (fm.CurrentType == null) fm.NewType(nome);
                    FamilyParameter pc = fm.AddParameter(PComp, GroupTypeId.Geometry, SpecTypeId.Length, true);
                    FamilyParameter pl = fm.AddParameter(PLarg, GroupTypeId.Geometry, SpecTypeId.Length, true);
                    FamilyParameter pe = fm.AddParameter(PEsp, GroupTypeId.Geometry, SpecTypeId.Length, true);
                    fm.AddParameter(PLigacao, GroupTypeId.IdentityData, SpecTypeId.String.Text, true);
                    double c0 = Ft(centradaNoPlano ? 20 : 300), l0 = Ft(centradaNoPlano ? 20 : 200), e0 = Ft(centradaNoPlano ? 60 : 10);
                    fm.Set(pc, c0); fm.Set(pl, l0); fm.Set(pe, e0);
                    FamilyParameter pIni = null, pFim = null;
                    if (centradaNoPlano)
                    {
                        pIni = fm.AddParameter("PCEB_Início", GroupTypeId.Geometry, SpecTypeId.Length, true);
                        pFim = fm.AddParameter("PCEB_Fim", GroupTypeId.Geometry, SpecTypeId.Length, true);
                        fm.SetFormula(pIni, "-" + PEsp + " / 2");
                        fm.SetFormula(pFim, PEsp + " / 2");
                    }

                    ReferencePlane Plano(XYZ a, XYZ b, string n)
                    {
                        var p = fd.FamilyCreate.NewReferencePlane(a, b, XYZ.BasisZ, planta);
                        p.Name = n;
                        return p;
                    }
                    double g = Ft(1000);
                    ReferencePlane esq = Plano(new XYZ(-c0 / 2, -g, 0), new XYZ(-c0 / 2, g, 0), "PCEB esquerda");
                    ReferencePlane dir = Plano(new XYZ(c0 / 2, -g, 0), new XYZ(c0 / 2, g, 0), "PCEB direita");
                    ReferencePlane fre = Plano(new XYZ(-g, -l0 / 2, 0), new XYZ(g, -l0 / 2, 0), "PCEB frente");
                    ReferencePlane tra = Plano(new XYZ(-g, l0 / 2, 0), new XYZ(g, l0 / 2, 0), "PCEB trás");
                    fd.Regenerate();
                    Dimension Cota(Line linha, params Reference[] refs)
                    {
                        var ra = new ReferenceArray();
                        foreach (var r in refs) ra.Append(r);
                        return fd.FamilyCreate.NewDimension(planta, linha, ra);
                    }
                    Cota(Line.CreateBound(new XYZ(-c0, -l0 - Ft(100), 0), new XYZ(c0, -l0 - Ft(100), 0)), esq.GetReference(), dir.GetReference()).FamilyLabel = pc;
                    Cota(Line.CreateBound(new XYZ(-c0, -l0 - Ft(60), 0), new XYZ(c0, -l0 - Ft(60), 0)), esq.GetReference(), centroX.GetReference(), dir.GetReference()).AreSegmentsEqual = true;
                    Cota(Line.CreateBound(new XYZ(c0 + Ft(100), -l0, 0), new XYZ(c0 + Ft(100), l0, 0)), fre.GetReference(), tra.GetReference()).FamilyLabel = pl;
                    Cota(Line.CreateBound(new XYZ(c0 + Ft(60), -l0, 0), new XYZ(c0 + Ft(60), l0, 0)), fre.GetReference(), centroY.GetReference(), tra.GetReference()).AreSegmentsEqual = true;
                    fd.Regenerate();

                    SketchPlane sp = SketchPlane.Create(fd, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
                    var perfil = new CurveArray();
                    XYZ a1 = new XYZ(-c0 / 2, -l0 / 2, 0), b1 = new XYZ(c0 / 2, -l0 / 2, 0), c1 = new XYZ(c0 / 2, l0 / 2, 0), d1 = new XYZ(-c0 / 2, l0 / 2, 0);
                    perfil.Append(Line.CreateBound(a1, b1)); perfil.Append(Line.CreateBound(b1, c1)); perfil.Append(Line.CreateBound(c1, d1)); perfil.Append(Line.CreateBound(d1, a1));
                    var arr = new CurveArrArray(); arr.Append(perfil);
                    Extrusion ex = fd.FamilyCreate.NewExtrusion(true, arr, sp, e0);
                    fd.Regenerate();
                    Alinhar(fd, ex, planta, -XYZ.BasisX, esq, log); Alinhar(fd, ex, planta, XYZ.BasisX, dir, log);
                    Alinhar(fd, ex, planta, -XYZ.BasisY, fre, log); Alinhar(fd, ex, planta, XYZ.BasisY, tra, log);
                    if (centradaNoPlano)
                    {
                        fm.AssociateElementParameterToFamilyParameter(ex.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM), pIni);
                        fm.AssociateElementParameterToFamilyParameter(ex.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), pFim);
                    }
                    else fm.AssociateElementParameterToFamilyParameter(ex.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), pe);
                    t.Commit();
                }
                // conferência: muda as medidas e vê se o sólido acompanha
                using (var t = new Transaction(fd, "PCEB: conferência"))
                {
                    t.Start();
                    FamilyManager fm = fd.FamilyManager;
                    fm.Set(fm.get_Parameter(PComp), Ft(450)); fm.Set(fm.get_Parameter(PLarg), Ft(120)); fm.Set(fm.get_Parameter(PEsp), Ft(25));
                    fd.Regenerate();
                    var ex = new FilteredElementCollector(fd).OfClass(typeof(Extrusion)).Cast<Extrusion>().First();
                    BoundingBoxXYZ bb = ex.get_BoundingBox(null);
                    log.AppendLine($"{nome}: conferência 450 × 120 × 25 mm → {(bb.Max.X - bb.Min.X) * Pe:0} × {(bb.Max.Y - bb.Min.Y) * Pe:0} × {(bb.Max.Z - bb.Min.Z) * Pe:0} mm");
                    t.RollBack();
                }
                fd.SaveAs(arq, new SaveAsOptions { OverwriteExistingFile = true });
                log.AppendLine($"{nome}: gravada em {arq}.");
                return arq;
            }
            catch (Exception exC) { log.AppendLine($"{nome}: não foi possível criar ({exC.Message})."); return null; }
            finally { fd.Close(false); }
        }

        private static void Alinhar(Document doc, Extrusion ex, View v, XYZ normal, ReferencePlane plano, StringBuilder log)
        {
            var opt = new Options { ComputeReferences = true, IncludeNonVisibleObjects = true };
            foreach (Solid s in ex.get_Geometry(opt).OfType<Solid>())
                foreach (Face f in s.Faces)
                    if (f is PlanarFace pf && pf.FaceNormal.IsAlmostEqualTo(normal) && pf.Reference != null)
                    {
                        doc.FamilyCreate.NewAlignment(v, plano.GetReference(), pf.Reference);
                        return;
                    }
            log.AppendLine($"  sem face para alinhar a \"{plano.Name}\"");
        }

        private sealed class Sobrescrever : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues) { overwriteParameterValues = true; return true; }
            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            { source = FamilySource.Family; overwriteParameterValues = true; return true; }
        }
    }
}
