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
    /// SD_AUTORUN = familiaspceb → acrescenta à família da viga pré-moldada (D36, docs/04_arquitetura/familias_apoio_furos.md):
    ///   • no consolo aninhado, o sólido do APOIO sobre a face superior (neoprene, argamassa ou direto);
    ///   • na viga, os parâmetros PCEB_Apoio* (ligados ao consolo; e_neoprene = PCEB_ApoioEspessura) e os FUROS dos pinos no dente.
    ///   SD_FAMILIA = cópia da "Viga Pré-Moldada.rfa" (o original do usuário nunca é aberto por aqui)
    ///   SD_SAVEAS  = onde gravar a família pronta
    /// Depois de montar, varia b e o tipo de apoio (transações desfeitas) e grava as medidas no relatório.
    /// </summary>
    internal static partial class FamiliasPceb
    {
        private static double Ft(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        private const string MatNeoprene = "PCEB - Neoprene";
        private const string MatArgamassa = "PCEB - Argamassa de assentamento";

        public static string Aplicar(Application app)
        {
            string src = Environment.GetEnvironmentVariable("SD_FAMILIA");
            string saida = Environment.GetEnvironmentVariable("SD_SAVEAS");
            if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(saida)) throw new InvalidOperationException("SD_FAMILIA e SD_SAVEAS são obrigatórios.");
            var log = new StringBuilder();
            Document viga = app.OpenDocumentFile(src);
            if (Environment.GetEnvironmentVariable("SD_SOCONFERIR") == "1")   // só confere uma família já gravada
            {
                try
                {
                    FamilyManager fm = viga.FamilyManager;
                    double meio = new FilteredElementCollector(viga).OfClass(typeof(Extrusion)).Cast<Extrusion>().Select(x => x.get_BoundingBox(null))
                        .Where(b => b != null).Average(b => (b.Min.X + b.Max.X) / 2);
                    foreach (Extrusion ex in new FilteredElementCollector(viga).OfClass(typeof(Extrusion)).Cast<Extrusion>().Where(x => !x.IsSolid))
                    {
                        string assoc = ex.Parameters.Cast<Parameter>().Select(p => fm.GetAssociatedFamilyParameter(p)?.Definition.Name).FirstOrDefault(n => n != null && n.StartsWith("PCEB_Furo"));
                        if (assoc == null) continue;
                        BoundingBoxXYZ b = ex.get_BoundingBox(null);
                        _marca["PCEB furo " + ((b.Min.X + b.Max.X) / 2 < meio ? "E" : "D") + (assoc == "PCEB_FuroUnico" ? "1" : "2")] = ex.Id;
                    }
                    log.AppendLine("Reaberta: " + src + $" ({_marca.Count} vazios de furo)");
                    FlexViga(viga, log);
                }
                catch (Exception ex) { log.AppendLine("ERRO " + ex); }
                finally { viga.Close(false); }
                Directory.CreateDirectory(DetailingReport.LogDir);
                string arq0 = Path.Combine(DetailingReport.LogDir, "familiaspceb_conferir_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                File.WriteAllText(arq0, log.ToString(), Encoding.UTF8);
                return arq0;
            }
            try
            {
                // 1) consolo: apoio
                FamilyInstance cons = new FilteredElementCollector(viga).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                    .First(f => f.Symbol.Family.Name == "Consolo");
                Document con = viga.EditFamily(cons.Symbol.Family);
                try
                {
                    ApoioNoConsolo(con, log);
                    FlexConsolo(con, log);
                    con.LoadFamily(viga, new Sobrescrever());
                    log.AppendLine("Consolo recarregado na viga.");
                }
                finally { con.Close(false); }

                // 2) viga: parâmetros e ligação com o consolo; 3) furos
                ParametrosViga(viga, log);
                if (Environment.GetEnvironmentVariable("SD_SEMFUROS") == "1") log.AppendLine("Furos: não criados (SD_SEMFUROS=1; o usuário desenha à mão).");
                else FurosNaViga(app, viga, Path.GetDirectoryName(saida), log);
                // 4) conferência
                FlexViga(viga, log);

                Directory.CreateDirectory(Path.GetDirectoryName(saida));
                viga.SaveAs(saida, new SaveAsOptions { OverwriteExistingFile = true });
                log.AppendLine("Gravada em " + saida);
            }
            catch (Exception ex) { log.AppendLine("ERRO " + ex); }
            finally { viga.Close(false); }

            Directory.CreateDirectory(DetailingReport.LogDir);
            string arq = Path.Combine(DetailingReport.LogDir, "familiaspceb_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            File.WriteAllText(arq, log.ToString(), Encoding.UTF8);
            return arq;
        }

        // ---------------------------------------------------------------- consolo

        private static void ApoioNoConsolo(Document con, StringBuilder log)
        {
            FamilyManager fm = con.FamilyManager;
            View planta = Vista(con, ViewType.FloorPlan, "Ref. Level");
            View frente = Vista(con, ViewType.Elevation, "Front");
            ReferencePlane centroLR = Plano(con, "Center (Left/Right)");
            // c: do plano da face do pilar (z=0) ao da ponta (z=c)
            var planosC = PlanosDaCota(con, "c").OrderBy(p => p.GetPlane().Origin.Z).ToList();
            ReferencePlane ponta = planosC.Last();
            double c = ponta.GetPlane().Origin.Z;

            using (var t = new Transaction(con, "PCEB: apoio no consolo"))
            {
                t.Start();
                Silenciar(t, log);
                FamilyParameter pTipo = Novo(fm, "PCEB_ApoioTipo", SpecTypeId.Int.Integer, GroupTypeId.Construction, true);
                FamilyParameter pComp = Novo(fm, "PCEB_ApoioComprimento", SpecTypeId.Length, GroupTypeId.Geometry, true);
                FamilyParameter pLarg = Novo(fm, "PCEB_ApoioLargura", SpecTypeId.Length, GroupTypeId.Geometry, true);
                FamilyParameter pEsp = Novo(fm, "PCEB_ApoioEspessura", SpecTypeId.Length, GroupTypeId.Geometry, true);
                FamilyParameter pAf = Novo(fm, "PCEB_ApoioAfastamento", SpecTypeId.Length, GroupTypeId.Geometry, true);
                FamilyParameter pVis = Novo(fm, "PCEB_ApoioVisivel", SpecTypeId.Boolean.YesNo, GroupTypeId.Graphics, true);
                FamilyParameter pMat = Novo(fm, "PCEB_ApoioMaterial", SpecTypeId.Reference.Material, GroupTypeId.Materials, true);
                // sólido com espessura mínima: "direto" tem espessura 0 e o sólido fica invisível
                FamilyParameter pSol = Novo(fm, "PCEB_ApoioEspessuraDoSolido", SpecTypeId.Length, GroupTypeId.Geometry, true);

                Material neo = MaterialPceb(con, MatNeoprene, new Color(40, 40, 40));
                MaterialPceb(con, MatArgamassa, new Color(190, 190, 180));

                fm.Set(pTipo, 1);
                fm.Set(pComp, Ft(150)); fm.Set(pLarg, Ft(150)); fm.Set(pEsp, Ft(15)); fm.Set(pAf, Ft(25));
                fm.Set(pVis, 1); fm.Set(pMat, neo.Id);
                fm.SetFormula(pSol, "if(PCEB_ApoioEspessura < 1 mm, 1 mm, PCEB_ApoioEspessura)");

                double w = Ft(150), zExt = c - Ft(25), zInt = zExt - Ft(150);
                // planos: laterais (normal X) na planta; externo/interno (normal Z) na elevação frontal
                ReferencePlane esq = con.FamilyCreate.NewReferencePlane(new XYZ(-w / 2, -Ft(600), 0), new XYZ(-w / 2, Ft(300), 0), XYZ.BasisZ, planta);
                ReferencePlane dir = con.FamilyCreate.NewReferencePlane(new XYZ(w / 2, -Ft(600), 0), new XYZ(w / 2, Ft(300), 0), XYZ.BasisZ, planta);
                ReferencePlane ext = con.FamilyCreate.NewReferencePlane(new XYZ(-Ft(400), 0, zExt), new XYZ(Ft(400), 0, zExt), XYZ.BasisY, frente);
                ReferencePlane inn = con.FamilyCreate.NewReferencePlane(new XYZ(-Ft(400), 0, zInt), new XYZ(Ft(400), 0, zInt), XYZ.BasisY, frente);
                esq.Name = "PCEB apoio esquerda"; dir.Name = "PCEB apoio direita"; ext.Name = "PCEB apoio externo"; inn.Name = "PCEB apoio interno";
                con.Regenerate();

                Dimension dw = Cota(con, planta, Line.CreateBound(new XYZ(-w, -Ft(700), 0), new XYZ(w, -Ft(700), 0)), esq.GetReference(), dir.GetReference());
                dw.FamilyLabel = pLarg;
                Dimension deq = Cota(con, planta, Line.CreateBound(new XYZ(-w, -Ft(650), 0), new XYZ(w, -Ft(650), 0)), esq.GetReference(), centroLR.GetReference(), dir.GetReference());
                deq.AreSegmentsEqual = true;
                Dimension daf = Cota(con, frente, Line.CreateBound(new XYZ(Ft(500), 0, zExt), new XYZ(Ft(500), 0, c)), ponta.GetReference(), ext.GetReference());
                daf.FamilyLabel = pAf;
                Dimension dl = Cota(con, frente, Line.CreateBound(new XYZ(Ft(550), 0, zInt), new XYZ(Ft(550), 0, zExt)), ext.GetReference(), inn.GetReference());
                dl.FamilyLabel = pComp;
                con.Regenerate();

                // sólido: perfil no plano da face superior (y = 0, a origem do consolo), crescendo para +Y
                SketchPlane sp = SketchPlane.Create(con, Plane.CreateByNormalAndOrigin(XYZ.BasisY, XYZ.Zero));
                var perfil = new CurveArray();
                XYZ a = new XYZ(-w / 2, 0, zInt), b = new XYZ(w / 2, 0, zInt), cc = new XYZ(w / 2, 0, zExt), d = new XYZ(-w / 2, 0, zExt);
                perfil.Append(Line.CreateBound(a, b)); perfil.Append(Line.CreateBound(b, cc)); perfil.Append(Line.CreateBound(cc, d)); perfil.Append(Line.CreateBound(d, a));
                var arr = new CurveArrArray(); arr.Append(perfil);
                Extrusion apoio = con.FamilyCreate.NewExtrusion(true, arr, sp, Ft(15));
                con.Regenerate();
                BoundingBoxXYZ bb = apoio.get_BoundingBox(null);
                log.AppendLine($"Apoio criado: bb {P(bb.Min)}–{P(bb.Max)}");
                if (bb.Max.Y < Ft(1))   // cresceu para baixo: inverte
                {
                    apoio.StartOffset = 0; apoio.EndOffset = -Ft(15);
                    log.AppendLine("  (sentido invertido)");
                }

                // trava as faces nos planos
                Alinhar(con, apoio, planta, -XYZ.BasisX, esq, log);
                Alinhar(con, apoio, planta, XYZ.BasisX, dir, log);
                Alinhar(con, apoio, frente, XYZ.BasisZ, ext, log);
                Alinhar(con, apoio, frente, -XYZ.BasisZ, inn, log);
                fm.AssociateElementParameterToFamilyParameter(apoio.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), pSol);
                fm.AssociateElementParameterToFamilyParameter(apoio.get_Parameter(BuiltInParameter.IS_VISIBLE_PARAM), pVis);
                fm.AssociateElementParameterToFamilyParameter(apoio.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM), pMat);
                _marca["PCEB apoio"] = apoio.Id;
                t.Commit();
                log.AppendLine("Consolo: apoio pronto (" + t.GetStatus() + ").");
            }
        }

        /// <summary>Varia o consolo e confere o sólido do apoio (desfeito no fim).</summary>
        private static void FlexConsolo(Document con, StringBuilder log)
        {
            FamilyManager fm = con.FamilyManager;
            var casos = new[]
            {
                new { c = 250.0, W = 150.0, L = 150.0, A = 25.0, E = 15.0 },
                new { c = 250.0, W = 100.0, L = 200.0, A = 50.0, E = 20.0 },
                new { c = 350.0, W = 180.0, L = 120.0, A = 30.0, E = 10.0 },
                new { c = 250.0, W = 150.0, L = 150.0, A = 25.0, E = 0.0 },
            };
            log.AppendLine("Conferência do consolo (esperado: X ±W/2, Y 0..E, Z c-A-L..c-A):");
            foreach (var k in casos)
            {
                using (var t = new Transaction(con, "flex"))
                {
                    t.Start();
                    var erros = Silenciar(t, log);
                    try
                    {
                        fm.Set(fm.get_Parameter("c"), Ft(k.c));
                        fm.Set(fm.get_Parameter("PCEB_ApoioLargura"), Ft(k.W));
                        fm.Set(fm.get_Parameter("PCEB_ApoioComprimento"), Ft(k.L));
                        fm.Set(fm.get_Parameter("PCEB_ApoioAfastamento"), Ft(k.A));
                        fm.Set(fm.get_Parameter("PCEB_ApoioEspessura"), Ft(k.E));
                        con.Regenerate();
                        Extrusion ap = Comentario(con, "PCEB apoio");
                        BoundingBoxXYZ bb = ap.get_BoundingBox(null);
                        log.AppendLine($"  c={k.c} W={k.W} L={k.L} A={k.A} E={k.E} → {P(bb.Min)}–{P(bb.Max)}  " +
                                       $"esperado ({-k.W / 2:0},0,{k.c - k.A - k.L:0})–({k.W / 2:0},{Math.Max(k.E, 1):0},{k.c - k.A:0})");
                    }
                    catch (Exception ex) { log.AppendLine("  ERRO " + ex.Message); }
                    t.RollBack();
                }
            }
        }

        // ---------------------------------------------------------------- viga

        private static readonly string[] LigadosAoConsolo =
            { "PCEB_ApoioTipo", "PCEB_ApoioComprimento", "PCEB_ApoioLargura", "PCEB_ApoioEspessura", "PCEB_ApoioAfastamento", "PCEB_ApoioVisivel", "PCEB_ApoioMaterial" };

        private static void ParametrosViga(Document viga, StringBuilder log)
        {
            FamilyManager fm = viga.FamilyManager;
            using (var t = new Transaction(viga, "PCEB: parâmetros da viga"))
            {
                t.Start();
                Silenciar(t, log);
                FamilyParameter pTipo = Novo(fm, "PCEB_ApoioTipo", SpecTypeId.Int.Integer, GroupTypeId.Construction, false);
                FamilyParameter pNome = Novo(fm, "PCEB_Apoio", SpecTypeId.String.Text, GroupTypeId.Construction, false);
                FamilyParameter pEsp = Novo(fm, "PCEB_ApoioEspessura", SpecTypeId.Length, GroupTypeId.Geometry, false);
                FamilyParameter pComp = Novo(fm, "PCEB_ApoioComprimento", SpecTypeId.Length, GroupTypeId.Geometry, false);
                FamilyParameter pLarg = Novo(fm, "PCEB_ApoioLargura", SpecTypeId.Length, GroupTypeId.Geometry, false);
                FamilyParameter pAf = Novo(fm, "PCEB_ApoioAfastamento", SpecTypeId.Length, GroupTypeId.Geometry, false);
                FamilyParameter pVis = Novo(fm, "PCEB_ApoioVisivel", SpecTypeId.Boolean.YesNo, GroupTypeId.Graphics, false);
                FamilyParameter pMat = Novo(fm, "PCEB_ApoioMaterial", SpecTypeId.Reference.Material, GroupTypeId.Materials, false);

                FamilyParameter fDia = Novo(fm, "PCEB_FuroDiametro", SpecTypeId.Length, GroupTypeId.Geometry, false);
                FamilyParameter fQtd = Novo(fm, "PCEB_FuroQtd", SpecTypeId.Int.Integer, GroupTypeId.Geometry, false);
                FamilyParameter fDE = Novo(fm, "PCEB_FuroDistE", SpecTypeId.Length, GroupTypeId.Geometry, false);
                FamilyParameter fDD = Novo(fm, "PCEB_FuroDistD", SpecTypeId.Length, GroupTypeId.Geometry, false);
                FamilyParameter fEntre = Novo(fm, "PCEB_FuroEntreFuros", SpecTypeId.Length, GroupTypeId.Geometry, false);
                FamilyParameter fUm = Novo(fm, "PCEB_FuroUnico", SpecTypeId.Boolean.YesNo, GroupTypeId.Geometry, false);
                FamilyParameter fDois = Novo(fm, "PCEB_FuroDuplo", SpecTypeId.Boolean.YesNo, GroupTypeId.Geometry, false);
                FamilyParameter fMeia = Novo(fm, "PCEB_FuroMeiaDistancia", SpecTypeId.Length, GroupTypeId.Geometry, false);

                foreach (FamilyType ft in fm.Types.Cast<FamilyType>().ToList())
                {
                    fm.CurrentType = ft;
                    fm.Set(pTipo, 1);
                    fm.Set(pComp, Ft(150)); fm.Set(pLarg, Ft(150)); fm.Set(pAf, Ft(25));
                    Material neo = new FilteredElementCollector(viga).OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(m => m.Name == MatNeoprene)
                                   ?? MaterialPceb(viga, MatNeoprene, new Color(40, 40, 40));
                    MaterialPceb(viga, MatArgamassa, new Color(190, 190, 180));
                    fm.Set(pMat, neo.Id);
                    fm.Set(fDia, Ft(32)); fm.Set(fDE, Ft(110)); fm.Set(fDD, Ft(110));   // 110 mm: padrão do usuário (18/11/2026)
                }
                fm.SetFormula(pNome, "if(PCEB_ApoioTipo = 1, \"Neoprene\", if(PCEB_ApoioTipo = 2, \"Argamassa\", \"Direto\"))");
                fm.SetFormula(pEsp, "if(PCEB_ApoioTipo = 1, 15 mm, if(PCEB_ApoioTipo = 2, 20 mm, 0 mm))");
                fm.SetFormula(pVis, "not(PCEB_ApoioTipo = 3)");
                fm.SetFormula(fm.get_Parameter("e_neoprene"), "PCEB_ApoioEspessura");
                fm.SetFormula(fQtd, "if(b < 300 mm, 1, 2)");
                fm.SetFormula(fEntre, "b / 2");
                fm.SetFormula(fUm, "PCEB_FuroQtd = 1");
                fm.SetFormula(fDois, "PCEB_FuroQtd > 1");
                fm.SetFormula(fMeia, "PCEB_FuroEntreFuros / 2");

                foreach (FamilyInstance fi in new FilteredElementCollector(viga).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                             .Where(f => f.Symbol.Family.Name == "Consolo"))
                    foreach (string nome in LigadosAoConsolo)
                    {
                        Parameter ip = fi.LookupParameter(nome);
                        if (ip == null) { log.AppendLine($"  consolo {fi.Id.Value}: sem {nome}"); continue; }
                        fm.AssociateElementParameterToFamilyParameter(ip, fm.get_Parameter(nome));
                    }
                t.Commit();
                log.AppendLine("Viga: parâmetros PCEB_ criados e ligados aos consolos (" + t.GetStatus() + ").");
            }
        }

        /// <summary>
        /// Furos dos pinos: vazios direto na viga, com seção de POLÍGONO DE 16 LADOS (largura entre faces = PCEB_FuroDiametro).
        /// Numa família a API não prende o centro de um círculo (não há edição de esboço) e um vazio aninhado não corta a forma da
        /// viga; faces planas, sim, travam em planos de referência (como o apoio do consolo). Por ponta: 1 vazio no eixo e 1 com o par
        /// simétrico; "Cortar geometria" liga/desliga por PCEB_FuroUnico / PCEB_FuroDuplo.
        /// </summary>
        private static void FurosNaViga(Application app, Document viga, string pasta, StringBuilder log)
        {
            FamilyManager fm = viga.FamilyManager;
            View planta = Vista(viga, ViewType.FloorPlan, "Ref. Level");
            ReferencePlane pontaE = Plano(viga, "2"), pontaD = Plano(viga, "3");
            // eixo da alma: plano novo no meio das faces cotadas por "b" (nas variantes a alma não é simétrica ao "Center (Front/Back)")
            var ladosB = PlanosDaCota(viga, "b").OrderBy(p => p.GetPlane().Origin.Y).ToList();
            ReferencePlane centro = null;
            // vazios a partir do plano do topo, descendo h (ajuste do usuário, 18/11/2026)
            // altura: cota rotulada "h" ou "Altura" (variantes); topo: o plano "Topo" ou o de cima dessa cota
            Dimension cotaAltura = new FilteredElementCollector(viga).OfClass(typeof(Dimension)).Cast<Dimension>()
                .First(x => { try { string n = x.FamilyLabel?.Definition.Name; return n == "h" || n == "Altura"; } catch { return false; } });
            FamilyParameter pAltura = cotaAltura.FamilyLabel;
            ReferencePlane topo = new FilteredElementCollector(viga).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>().FirstOrDefault(p => p.Name == "Topo")
                ?? cotaAltura.References.Cast<Reference>().Select(r => viga.GetElement(r)).OfType<ReferencePlane>().OrderBy(p => p.GetPlane().Origin.Z).Last();
            log.AppendLine($"  altura = {pAltura.Definition.Name}, topo = \"{topo.Name}\"");
            double xE = pontaE.GetPlane().Origin.X, xD = pontaD.GetPlane().Origin.X, zF = topo.GetPlane().Origin.Z;
            double r = Ft(16), dist = Ft(110), s = Ft(50);
            double xIni = xE - Ft(300), xFim = xD + Ft(300);
            FamilyParameter pDia = fm.get_Parameter("PCEB_FuroDiametro");

            Extrusion gerber = new FilteredElementCollector(viga).OfClass(typeof(Extrusion)).Cast<Extrusion>().First(x => !x.IsSolid && x.Parameters.Cast<Parameter>().Any(p => fm.GetAssociatedFamilyParameter(p)?.Definition.Name == "Viga_com_gerber"));
            Parameter corteGerber = gerber.Parameters.Cast<Parameter>().FirstOrDefault(p => fm.GetAssociatedFamilyParameter(p)?.Definition.Name == "Viga_com_gerber");
            BuiltInParameter bipCorte = (corteGerber?.Definition as InternalDefinition)?.BuiltInParameter ?? BuiltInParameter.INVALID;

            ReferencePlane PX(double x, string nome)
            {
                ReferencePlane p = viga.FamilyCreate.NewReferencePlane(new XYZ(x, -Ft(600), 0), new XYZ(x, Ft(600), 0), XYZ.BasisZ, planta);
                p.Name = nome; return p;
            }
            ReferencePlane PY(double y, string nome)
            {
                ReferencePlane p = viga.FamilyCreate.NewReferencePlane(new XYZ(xIni, y, 0), new XYZ(xFim, y, 0), XYZ.BasisZ, planta);
                p.Name = nome; return p;
            }

            // planos: por ponta, o centro dos furos e as faces ±X; por linha (eixo, linha 1, linha 2), o centro e as faces ±Y
            var px = new Dictionary<string, (ReferencePlane c, ReferencePlane menos, ReferencePlane mais)>();
            var py = new Dictionary<string, (ReferencePlane c, ReferencePlane menos, ReferencePlane mais)>();
            bool okPlanos = Passo(viga, log, "planos dos furos", () =>
            {
                foreach (var (lado, x) in new[] { ("E", xE + dist), ("D", xD - dist) })
                    px[lado] = (PX(x, $"PCEB furo {lado}"), PX(x - r, $"PCEB furo {lado} face -X"), PX(x + r, $"PCEB furo {lado} face +X"));
                double y0 = (ladosB[0].GetPlane().Origin.Y + ladosB[1].GetPlane().Origin.Y) / 2;
                centro = PY(y0, "PCEB eixo da alma");
                viga.Regenerate();
                Cota(viga, planta, Line.CreateBound(new XYZ(xE - Ft(150), y0 - Ft(400), 0), new XYZ(xE - Ft(150), y0 + Ft(400), 0)),
                     ladosB[0].GetReference(), centro.GetReference(), ladosB[1].GetReference()).AreSegmentsEqual = true;
                py["0"] = (centro, PY(y0 - r, "PCEB furo eixo face -Y"), PY(y0 + r, "PCEB furo eixo face +Y"));
                py["1"] = (PY(y0 - s, "PCEB furos linha 1"), PY(y0 - s - r, "PCEB furos linha 1 face -Y"), PY(y0 - s + r, "PCEB furos linha 1 face +Y"));
                py["2"] = (PY(y0 + s, "PCEB furos linha 2"), PY(y0 + s - r, "PCEB furos linha 2 face -Y"), PY(y0 + s + r, "PCEB furos linha 2 face +Y"));
                viga.Regenerate();

                double yc = Ft(450);
                foreach (var (lado, ponta, nomeDist) in new[] { ("E", pontaE, "PCEB_FuroDistE"), ("D", pontaD, "PCEB_FuroDistD") })
                {
                    var (c, m, M) = px[lado];
                    double xp = ponta.GetPlane().Origin.X, xc = c.GetPlane().Origin.X;
                    Cota(viga, planta, Line.CreateBound(new XYZ(Math.Min(xp, xc), yc, 0), new XYZ(Math.Max(xp, xc), yc, 0)), ponta.GetReference(), c.GetReference())
                        .FamilyLabel = fm.get_Parameter(nomeDist);
                    Cota(viga, planta, Line.CreateBound(new XYZ(xc - Ft(50), yc + Ft(60), 0), new XYZ(xc + Ft(50), yc + Ft(60), 0)), m.GetReference(), c.GetReference(), M.GetReference())
                        .AreSegmentsEqual = true;
                    Cota(viga, planta, Line.CreateBound(new XYZ(xc - Ft(50), yc + Ft(120), 0), new XYZ(xc + Ft(50), yc + Ft(120), 0)), m.GetReference(), M.GetReference())
                        .FamilyLabel = pDia;
                }
                double xm = (xE + xD) / 2;
                Cota(viga, planta, Line.CreateBound(new XYZ(xm, -Ft(200), 0), new XYZ(xm, Ft(200), 0)), py["1"].c.GetReference(), centro.GetReference(), py["2"].c.GetReference())
                    .AreSegmentsEqual = true;
                Cota(viga, planta, Line.CreateBound(new XYZ(xm + Ft(60), -Ft(200), 0), new XYZ(xm + Ft(60), Ft(200), 0)), py["1"].c.GetReference(), py["2"].c.GetReference())
                    .FamilyLabel = fm.get_Parameter("PCEB_FuroEntreFuros");
                double xl = xm + Ft(120);
                foreach (string k in new[] { "0", "1", "2" })
                {
                    var (c, m, M) = py[k];
                    Cota(viga, planta, Line.CreateBound(new XYZ(xl, -Ft(250), 0), new XYZ(xl, Ft(250), 0)), m.GetReference(), c.GetReference(), M.GetReference()).AreSegmentsEqual = true;
                    Cota(viga, planta, Line.CreateBound(new XYZ(xl + Ft(60), -Ft(250), 0), new XYZ(xl + Ft(60), Ft(250), 0)), m.GetReference(), M.GetReference()).FamilyLabel = pDia;
                    xl += Ft(120);
                }
            });
            if (!okPlanos) return;

            Passo(viga, log, "vazios dos furos", () =>
            {
                if (topo.Name == "Plano de referência" || topo.Name == "Reference Plane") topo.Name = "Topo";
                SketchPlane sp = SketchPlane.Create(viga, topo.Id);
                // o vazio nasce ONDE OS PLANOS FICARAM depois das fórmulas (entre furos = b/2, distâncias, diâmetro): só assim as
                // linhas do esboço caem sobre os planos e o Revit as prende
                double X(ReferencePlane p) => p.GetPlane().Origin.X;
                double Y(ReferencePlane p) => p.GetPlane().Origin.Y;
                double rr = (Y(py["0"].mais) - Y(py["0"].menos)) / 2;
                foreach (string lado in new[] { "E", "D" })
                {
                    double x = X(px[lado].c);
                    Extrusion um = Vazio(viga, sp, new[] { new XYZ(x, Y(py["0"].c), zF) }, rr);
                    Extrusion dois = Vazio(viga, sp, new[] { new XYZ(x, Y(py["1"].c), zF), new XYZ(x, Y(py["2"].c), zF) }, rr);
                    _marca["PCEB furo " + lado + "1"] = um.Id;
                    _marca["PCEB furo " + lado + "2"] = dois.Id;
                    viga.Regenerate();
                    foreach (Extrusion ex in new[] { um, dois })
                    {
                        fm.AssociateElementParameterToFamilyParameter(ex.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), pAltura);
                        if (bipCorte != BuiltInParameter.INVALID)
                            fm.AssociateElementParameterToFamilyParameter(ex.get_Parameter(bipCorte), fm.get_Parameter(ex == um ? "PCEB_FuroUnico" : "PCEB_FuroDuplo"));
                    }
                }
            });

            // Não há passo de travar as faces: as linhas do esboço nascem sobre os planos e o Revit as prende sozinho (conferido:
            // DistE/DistD, EntreFuros e Diametro movem/redimensionam os vazios). NewAlignment nessas faces é recusado ("not aligned").
        }

        private static bool Passo(Document doc, StringBuilder log, string nome, Action a)
        {
            using (var t = new Transaction(doc, nome))
            {
                t.Start();
                Silenciar(t, log);
                try { a(); }
                catch (Exception e) { t.RollBack(); log.AppendLine($"    FALHOU: {nome}: {e.Message}"); return false; }
                TransactionStatus st = t.Commit();
                log.AppendLine($"    {(st == TransactionStatus.Committed ? "ok" : "DESFEITO")}: {nome}");
                return st == TransactionStatus.Committed;
            }
        }

        /// <summary>Vazio vertical com polígonos de 16 lados (faces ±X e ±Y a <paramref name="r"/> do centro), um por centro.</summary>
        private static Extrusion Vazio(Document doc, SketchPlane sp, IEnumerable<XYZ> centros, double r)
        {
            const int n = 16;
            double R = r / Math.Cos(Math.PI / n);
            var arr = new CurveArrArray();
            foreach (XYZ c in centros)
            {
                var loop = new CurveArray();
                for (int i = 0; i < n; i++)
                {
                    double a0 = (i + 0.5) * 2 * Math.PI / n, a1 = (i + 1.5) * 2 * Math.PI / n;
                    loop.Append(Line.CreateBound(c + new XYZ(R * Math.Cos(a0), R * Math.Sin(a0), 0), c + new XYZ(R * Math.Cos(a1), R * Math.Sin(a1), 0)));
                }
                arr.Append(loop);
            }
            return doc.FamilyCreate.NewExtrusion(false, arr, sp, Ft(500));
        }

        /// <summary>Varia b e o tipo de apoio na viga e mede os furos e os apoios dos consolos (desfeito no fim).</summary>
        private static void FlexViga(Document viga, StringBuilder log)
        {
            FamilyManager fm = viga.FamilyManager;
            log.AppendLine("Conferência da viga:");
            foreach (var k in new[] { (b: 200.0, tipo: 1), (b: 400.0, tipo: 2), (b: 300.0, tipo: 3), (b: 200.0, tipo: 1) })
            {
                using (var t = new Transaction(viga, "flex"))
                {
                    t.Start();
                    Silenciar(t, log);
                    try
                    {
                        fm.Set(fm.get_Parameter("b"), Ft(k.b));
                        fm.Set(fm.get_Parameter("PCEB_ApoioTipo"), k.tipo);
                        if (k.b == 400) { fm.Set(fm.get_Parameter("PCEB_FuroDistE"), Ft(100)); fm.Set(fm.get_Parameter("PCEB_FuroDistD"), Ft(150)); fm.Set(fm.get_Parameter("PCEB_FuroDiametro"), Ft(40)); fm.Set(fm.get_Parameter("h") ?? fm.get_Parameter("Altura"), Ft(600)); }
                        viga.Regenerate();
                        FamilyType ft = fm.CurrentType;
                        string v(string n) { FamilyParameter p = fm.get_Parameter(n); return p == null ? "?" : p.StorageType == StorageType.Double ? Math.Round(Mm(ft.AsDouble(p) ?? 0)).ToString(CultureInfo.InvariantCulture) : p.StorageType == StorageType.String ? ft.AsString(p) : (ft.AsInteger(p) ?? 0).ToString(); }
                        log.AppendLine($"  b={k.b} tipo={k.tipo}: {v("PCEB_Apoio")}, esp={v("PCEB_ApoioEspessura")}, e_neoprene={v("e_neoprene")}, dz_folga={v("dz_folga_consolo")}, " +
                                       $"furos={v("PCEB_FuroQtd")}, entre={v("PCEB_FuroEntreFuros")}");
                        var furos = _marca.Where(kv => kv.Key.StartsWith("PCEB furo")).OrderBy(kv => kv.Key)
                            .Select(kv => { var ex = (Extrusion)viga.GetElement(kv.Value); int corta = ex.get_Parameter(BuiltInParameter.VOID_CUTS_GEOMETRY)?.AsInteger() ?? -1; BoundingBoxXYZ bb = ex.get_BoundingBox(null); if (bb == null) return $"{kv.Key.Substring(10)} SEM GEOMETRIA"; return $"{kv.Key.Substring(10)}{(corta == 1 ? "" : "(não corta)")} x {Mm(bb.Min.X):0}..{Mm(bb.Max.X):0} y {Mm(bb.Min.Y):0}..{Mm(bb.Max.Y):0}"; });
                        log.AppendLine("    furos: " + string.Join("  ", furos));
                        foreach (FamilyInstance fi in new FilteredElementCollector(viga).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().Where(f => f.Symbol.Family.Name == "Consolo"))
                        {
                            var solidos = new List<string>();
                            GeometryElement geo = fi.get_Geometry(new Options());
                            if (geo != null)
                                foreach (GeometryObject go in geo)
                                    if (go is GeometryInstance gi)
                                        foreach (Solid so in gi.GetInstanceGeometry().OfType<Solid>().Where(x => x.Volume > 1e-9))
                                        {
                                            BoundingBoxXYZ sb = so.GetBoundingBox();
                                            XYZ mn = sb.Transform.OfPoint(sb.Min), mx = sb.Transform.OfPoint(sb.Max);
                                            solidos.Add($"{P(new XYZ(Math.Min(mn.X, mx.X), Math.Min(mn.Y, mx.Y), Math.Min(mn.Z, mx.Z)))}–{P(new XYZ(Math.Max(mn.X, mx.X), Math.Max(mn.Y, mx.Y), Math.Max(mn.Z, mx.Z)))}");
                                        }
                            log.AppendLine($"    consolo {fi.Id.Value}: {solidos.Count} sólido(s): {string.Join(" ; ", solidos)}");
                        }
                    }
                    catch (Exception ex) { log.AppendLine("  ERRO " + ex.Message); }
                    t.RollBack();
                }
            }
        }

        // ---------------------------------------------------------------- utilidades

        private static FamilyParameter Novo(FamilyManager fm, string nome, ForgeTypeId spec, ForgeTypeId grupo, bool instancia) =>
            fm.get_Parameter(nome) ?? fm.AddParameter(nome, grupo, spec, instancia);

        private static Material MaterialPceb(Document doc, string nome, Color cor)
        {
            Material m = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(x => x.Name == nome);
            if (m != null) return m;
            m = (Material)doc.GetElement(Material.Create(doc, nome));
            m.Color = cor;
            return m;
        }

        private static View Vista(Document doc, ViewType tipo, string nome) =>
            new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().First(v => !v.IsTemplate && v.ViewType == tipo && v.Name == nome);

        private static ReferencePlane Plano(Document doc, string nome) =>
            new FilteredElementCollector(doc).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>().First(p => p.Name == nome);

        private static List<ReferencePlane> PlanosDaCota(Document doc, string rotulo)
        {
            Dimension d = new FilteredElementCollector(doc).OfClass(typeof(Dimension)).Cast<Dimension>()
                .First(x => { try { return x.FamilyLabel?.Definition.Name == rotulo; } catch { return false; } });
            return d.References.Cast<Reference>().Select(r => doc.GetElement(r)).OfType<ReferencePlane>().ToList();
        }

        private static Extrusion Comentario(Document doc, string texto) =>
            (Extrusion)doc.GetElement(_marca[texto]);

        /// <summary>Formas criadas por esta tarefa (o comentário não existe em formas de família).</summary>
        private static readonly Dictionary<string, ElementId> _marca = new Dictionary<string, ElementId>();


        private static Dimension Cota(Document doc, View v, Line linha, params Reference[] refs)
        {
            var ra = new ReferenceArray();
            foreach (Reference r in refs) ra.Append(r);
            return doc.FamilyCreate.NewDimension(v, linha, ra);
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
            log.AppendLine($"  sem face com normal {V(normal)} para alinhar a \"{plano.Name}\"");
        }

        private sealed class Sobrescrever : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues) { overwriteParameterValues = true; return true; }
            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            { source = FamilySource.Family; overwriteParameterValues = true; return true; }
        }

        /// <summary>Avisos vão para o relatório e somem; erros desfazem a transação (e vão para o relatório).</summary>
        private static List<string> Silenciar(Transaction t, StringBuilder log)
        {
            var lista = new List<string>();
            FailureHandlingOptions o = t.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(new Falhas(log, lista));
            o.SetClearAfterRollback(true);
            t.SetFailureHandlingOptions(o);
            return lista;
        }

        private sealed class Falhas : IFailuresPreprocessor
        {
            private readonly StringBuilder _log; private readonly List<string> _lista;
            public Falhas(StringBuilder log, List<string> lista) { _log = log; _lista = lista; }
            public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
            {
                bool erro = false;
                foreach (FailureMessageAccessor m in fa.GetFailureMessages())
                {
                    string txt = $"  [{m.GetSeverity()}] {m.GetDescriptionText()}";
                    _log.AppendLine(txt); _lista.Add(txt);
                    if (m.GetSeverity() == FailureSeverity.Warning) fa.DeleteWarning(m);
                    else erro = true;
                }
                return erro ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }
    }
}