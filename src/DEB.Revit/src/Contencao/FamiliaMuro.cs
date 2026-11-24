using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DEB.Core.Elementos.Contencao;

namespace DEB.Revit.Contencao
{
    /// <summary>
    /// Família PCEB do muro de arrimo (D50, item 7): a seção do núcleo (<see cref="DadosMuro.Secao"/>, com o dente) extrudada ao longo do
    /// eixo, na categoria Fundação estrutural (hospeda armadura). Uma família por seção ("PCEB Muro de arrimo …" com as medidas no
    /// nome); o comprimento é parâmetro de instância (<c>PCEB_Comprimento</c>). Sistema da família: x ao longo do muro, y para o lado do
    /// aterro (y = 0 na face de trás do fuste), z = 0 no terreno da frente — a instância fica no nível da vista.
    /// </summary>
    internal static class FamiliaMuro
    {
        public const string Prefixo = "PCEB Muro de arrimo";
        public const string PComp = "PCEB_Comprimento";
        private const double Pe = 0.3048;
        private static double Ft(double m) => m / Pe;

        public static string Pasta => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCEB", "familias");

        /// <summary>Nome da família pela seção (cm): altura total, base, ponta, fuste topo/base, sapata e dente.</summary>
        public static string Nome(DadosMuro d)
        {
            string C(double m) => Math.Round(m * 100).ToString("0", CultureInfo.InvariantCulture);
            string s = $"{Prefixo} H{C(d.AlturaTotal)} B{C(d.Base)} P{C(d.Ponta)} F{C(d.EspessuraTopo)}-{C(d.EspessuraBase)} S{C(d.EspessuraSapata)} R{C(d.TerrenoFrente)}";
            if (d.DenteAltura > 1e-9) s += $" D{C(d.DenteAltura)}x{C(d.DenteLargura)}-{C(d.DenteInicio)}";
            return s;
        }

        public static bool EhMuro(Element e) => e is FamilyInstance fi && fi.Symbol?.FamilyName?.StartsWith(Prefixo, StringComparison.Ordinal) == true;

        /// <summary>O tipo da família para a seção (carrega ou cria). null se não deu (motivo no log).</summary>
        public static FamilySymbol Garantir(Document doc, DadosMuro d, StringBuilder log)
        {
            string nome = Nome(d);
            FamilySymbol Achar() => new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().FirstOrDefault(s => s.FamilyName == nome);
            var sym = Achar();
            if (sym != null) return sym;
            string arq = Criar(doc.Application, d, nome, log);
            if (arq == null) return null;
            using (var t = new Transaction(doc, "PCEB: carregar " + nome))
            {
                t.Start();
                doc.LoadFamily(arq, out Family _);
                t.Commit();
            }
            sym = Achar();
            if (sym == null) log.AppendLine($"Família {nome} não carregou.");
            return sym;
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

        private static string Criar(Autodesk.Revit.ApplicationServices.Application app, DadosMuro d, string nome, StringBuilder log)
        {
            string modelo = Modelo(app);
            if (modelo == null) { log.AppendLine("Modelo de família genérico métrico não encontrado."); return null; }
            Directory.CreateDirectory(Pasta);
            string arq = Path.Combine(Pasta, nome + ".rfa");   // o Revit dá à família o nome do arquivo
            Document fd = app.NewFamilyDocument(modelo);
            try
            {
                using (var t = new Transaction(fd, "PCEB: " + nome))
                {
                    t.Start();
                    Family fam = fd.OwnerFamily;
                    try { fam.FamilyCategory = fd.Settings.Categories.get_Item(BuiltInCategory.OST_StructuralFoundation); }
                    catch (Exception exCat) { log.AppendLine($"Categoria Fundação estrutural recusada ({exCat.Message}); fica Modelo genérico."); }
                    FamilyManager fm = fd.FamilyManager;
                    if (fm.CurrentType == null) fm.NewType("Padrão");
                    FamilyParameter pc = fm.AddParameter(PComp, GroupTypeId.Geometry, SpecTypeId.Length, true);
                    fm.Set(pc, Ft(Math.Max(0.5, d.Comprimento)));
                    // seção no plano yz (normal +x): y = x_seção − (ponta + fuste na base); z = y_seção − terreno na frente
                    double xt = d.Ponta + d.EspessuraBase;
                    var pts = d.Secao().Select(p => new XYZ(0, Ft(p.X - xt), Ft(p.Y - d.TerrenoFrente))).ToList();
                    var perfil = new CurveArray();
                    for (int i = 0; i < pts.Count; i++) perfil.Append(Line.CreateBound(pts[i], pts[(i + 1) % pts.Count]));
                    var arr = new CurveArrArray(); arr.Append(perfil);
                    SketchPlane sp = SketchPlane.Create(fd, Plane.CreateByNormalAndOrigin(XYZ.BasisX, XYZ.Zero));
                    Extrusion ex = fd.FamilyCreate.NewExtrusion(true, arr, sp, Ft(Math.Max(0.5, d.Comprimento)));
                    fm.AssociateElementParameterToFamilyParameter(ex.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), pc);
                    t.Commit();
                }
                fd.SaveAs(arq, new SaveAsOptions { OverwriteExistingFile = true });
                log.AppendLine($"{nome}: família gravada em {arq}.");
                return arq;
            }
            catch (Exception exC) { log.AppendLine($"{nome}: não foi possível criar a família ({exC.Message})."); return null; }
            finally { fd.Close(false); }
        }

        /// <summary>
        /// Cria o muro: início em <paramref name="p0"/>, ao longo de <paramref name="dir"/> (horizontal), com o aterro à esquerda de dir
        /// (ou à direita, se <paramref name="aterroEsquerda"/> = falso), no nível dado. Grava os dados do cálculo na instância.
        /// </summary>
        public static FamilyInstance Criar(Document doc, DadosMuro d, Level nivel, XYZ p0, XYZ dir, bool aterroEsquerda, StringBuilder log)
        {
            FamilySymbol sym = Garantir(doc, d, log);
            if (sym == null) return null;
            dir = new XYZ(dir.X, dir.Y, 0);
            if (dir.GetLength() < 1e-9) dir = XYZ.BasisX;
            dir = dir.Normalize();
            using (var t = new Transaction(doc, "PCEB: criar muro de arrimo"))
            {
                t.Start();
                if (!sym.IsActive) sym.Activate();
                var p = new XYZ(p0.X, p0.Y, nivel.Elevation);
                FamilyInstance fi = doc.Create.NewFamilyInstance(p, sym, nivel, StructuralType.Footing);
                fi.LookupParameter(PComp)?.Set(Ft(d.Comprimento));
                // a família tem y para o aterro (à esquerda de +x); aterro à direita: espelha girando 180° e começando no fim
                double ang = Math.Atan2(dir.Y, dir.X);
                var eixo = Line.CreateBound(p, p + XYZ.BasisZ);
                if (aterroEsquerda) { if (Math.Abs(ang) > 1e-9) ElementTransformUtils.RotateElement(doc, fi.Id, eixo, ang); }
                else
                {
                    ElementTransformUtils.RotateElement(doc, fi.Id, eixo, ang + Math.PI);
                    ElementTransformUtils.MoveElement(doc, fi.Id, dir * Ft(d.Comprimento));
                }
                MuroRevit.GravarNoElemento(fi, d);
                t.Commit();
                log.AppendLine($"Muro criado: {sym.FamilyName}, {d.Comprimento:0.##} m, id {fi.Id}.");
                return fi;
            }
        }

        /// <summary>Muro PCEB já existente: troca a seção (nova família se mudou), o comprimento e os dados gravados.</summary>
        public static void Atualizar(FamilyInstance fi, DadosMuro d, StringBuilder log)
        {
            Document doc = fi.Document;
            FamilySymbol sym = Garantir(doc, d, log);
            using (var t = new Transaction(doc, "PCEB: atualizar muro de arrimo"))
            {
                t.Start();
                if (sym != null && sym.Id != fi.Symbol.Id) { if (!sym.IsActive) sym.Activate(); fi.Symbol = sym; }
                fi.LookupParameter(PComp)?.Set(Ft(d.Comprimento));
                MuroRevit.GravarNoElemento(fi, d);
                t.Commit();
            }
        }
    }
}
