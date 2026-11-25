using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using DEB.Core.Elementos.Metalica;

namespace DEB.Revit.Analise
{
    /// <summary>
    /// Famílias PCEB dos chumbadores (D50): gancho (J), reto com porca de ancoragem, químico (barra roscada + resina), de expansão (luva e
    /// cone) e parafuso para concreto — com arruela e porca. Uma família por medida (nome com forma, ø, embutimento e pega), gerada pela
    /// <see cref="GeometriaChumbador"/> do núcleo: origem no topo da placa de base, eixo vertical.
    /// </summary>
    internal static class FamiliasChumbadores
    {
        private static double Mm(double mm) => mm / 304.8;
        public static string Pasta => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCEB", "familias");

        /// <summary>O tipo da família para a geometria (carrega ou cria). Chamado dentro da transação aberta do projeto.</summary>
        public static FamilySymbol Garantir(Document doc, GeometriaChumbador g, StringBuilder log)
        {
            string nome = g.NomeFamilia();
            FamilySymbol Achar() => new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().FirstOrDefault(s => s.FamilyName == nome);
            var sym = Achar();
            if (sym != null) return sym;
            string arq = Criar(doc.Application, g, nome, log);
            if (arq == null) return null;
            doc.LoadFamily(arq, out Family _);
            sym = Achar();
            if (sym == null) log.AppendLine($"Família {nome} não carregou.");
            else if (!sym.IsActive) sym.Activate();
            return sym;
        }

        /// <summary>
        /// Parafuso PCEB (D50): fuste redondo no comprimento comercial, cabeça sextavada, arruela e porca sextavada, com as medidas do
        /// catálogo. Família baseada em plano de trabalho: o eixo do parafuso é a normal do plano (z da família); z = 0 na face onde encosta a
        /// cabeça, a porca do outro lado da pega. Uma família por diâmetro, comprimento e pega.
        /// </summary>
        public static FamilySymbol GarantirParafuso(Document doc, DimensoesParafuso p, double comp, double pega, StringBuilder log)
        {
            string nome = $"PCEB Parafuso ø{DEB.Core.Fmt.Virgula(p.Db, "0.#")} L{Math.Round(comp):0} P{Math.Round(pega):0}";   // sem "/" (3/4") no nome do arquivo
            FamilySymbol Achar() => new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().FirstOrDefault(s => s.FamilyName == nome);
            var sym = Achar();
            if (sym != null) return sym;
            string modelo = Modelo(doc.Application);
            if (modelo == null) return null;
            Directory.CreateDirectory(Pasta);
            string arq = Path.Combine(Pasta, nome + ".rfa");
            Document fd = doc.Application.NewFamilyDocument(modelo);
            try
            {
                using (var t = new Transaction(fd, "PCEB: " + nome))
                {
                    t.Start();
                    try { fd.OwnerFamily.FamilyCategory = fd.Settings.Categories.get_Item(BuiltInCategory.OST_StructConnections); } catch { }
                    fd.OwnerFamily.get_Parameter(BuiltInParameter.FAMILY_WORK_PLANE_BASED)?.Set(1);
                    var vert = fd.OwnerFamily.get_Parameter(BuiltInParameter.FAMILY_ALWAYS_VERTICAL); if (vert != null && !vert.IsReadOnly) vert.Set(0);
                    var fm = fd.FamilyManager;
                    if (fm.CurrentType == null) fm.NewType("Padrão");
                    fm.AddParameter(FamiliasLigacoes.PLigacao, GroupTypeId.IdentityData, SpecTypeId.String.Text, true);
                    var c = new Construtor(fd) { Aco = MaterialDe(fd, "PCEB Parafuso", new Color(225, 190, 40)) };
                    double r = p.Db / 2;
                    c.Sextavado(p.Chave, -p.AlturaCabeca, 0);                                             // cabeça
                    c.Cilindro(0, 0, 0, comp, r);                                                          // fuste
                    c.Cilindro(0, 0, pega, pega + p.EspessuraArruela, p.DiametroArruela / 2, r + 1);         // arruela
                    c.Sextavado(p.Chave, pega + p.EspessuraArruela, pega + p.EspessuraArruela + p.AlturaPorca);   // porca
                    t.Commit();
                }
                fd.SaveAs(arq, new SaveAsOptions { OverwriteExistingFile = true });
            }
            catch (Exception ex) { log.AppendLine($"{nome}: não foi possível criar ({ex.Message})."); return null; }
            finally { fd.Close(false); }
            doc.LoadFamily(arq, out Family _);
            sym = Achar();
            if (sym != null && !sym.IsActive) sym.Activate();
            return sym;
        }

        /// <summary>
        /// Pino com cabeça do insert (D50, ISO 13918): fuste redondo de z = 0 (face da chapa embutida) a z = hef e cabeça redonda de
        /// diâmetro dh. Baseada em plano: o eixo do pino é a normal do plano.
        /// </summary>
        public static FamilySymbol GarantirPino(Document doc, double db, double dh, double hef, StringBuilder log)
        {
            double kc = Math.Max(8, 0.5 * db);
            string nome = $"PCEB Pino ø{DEB.Core.Fmt.Virgula(db, "0.#")} H{Math.Round(hef):0}";
            FamilySymbol Achar() => new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().FirstOrDefault(s => s.FamilyName == nome);
            var sym = Achar();
            if (sym != null) return sym;
            string modelo = Modelo(doc.Application);
            if (modelo == null) return null;
            Directory.CreateDirectory(Pasta);
            string arq = Path.Combine(Pasta, nome + ".rfa");
            Document fd = doc.Application.NewFamilyDocument(modelo);
            try
            {
                using (var t = new Transaction(fd, "PCEB: " + nome))
                {
                    t.Start();
                    try { fd.OwnerFamily.FamilyCategory = fd.Settings.Categories.get_Item(BuiltInCategory.OST_StructConnections); } catch { }
                    fd.OwnerFamily.get_Parameter(BuiltInParameter.FAMILY_WORK_PLANE_BASED)?.Set(1);
                    var vert = fd.OwnerFamily.get_Parameter(BuiltInParameter.FAMILY_ALWAYS_VERTICAL); if (vert != null && !vert.IsReadOnly) vert.Set(0);
                    var fm = fd.FamilyManager;
                    if (fm.CurrentType == null) fm.NewType("Padrão");
                    fm.AddParameter(FamiliasLigacoes.PLigacao, GroupTypeId.IdentityData, SpecTypeId.String.Text, true);
                    var c = new Construtor(fd) { Aco = MaterialDe(fd, "PCEB Pino", new Color(225, 190, 40)) };
                    c.Cilindro(0, 0, 0, hef - kc, db / 2);          // fuste (hef medido até a face de apoio da cabeça)
                    c.Cilindro(0, 0, hef - kc, hef, dh / 2);        // cabeça
                    t.Commit();
                }
                fd.SaveAs(arq, new SaveAsOptions { OverwriteExistingFile = true });
            }
            catch (Exception ex) { log.AppendLine($"{nome}: não foi possível criar ({ex.Message})."); return null; }
            finally { fd.Close(false); }
            doc.LoadFamily(arq, out Family _);
            sym = Achar();
            if (sym != null && !sym.IsActive) sym.Activate();
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

        // ---------------------------------------------------------------- sólidos

        private sealed class Construtor
        {
            private readonly Document _fd;
            public ElementId Aco, Resina;
            public Construtor(Document fd) { _fd = fd; }

            private SketchPlane Horizontal(double zMm) => SketchPlane.Create(_fd, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, Mm(zMm))));

            private static CurveArray Circulo(double xMm, double yMm, double zMm, double rMm)
            {
                var c = new XYZ(Mm(xMm), Mm(yMm), Mm(zMm)); double r = Mm(rMm);
                var a = new CurveArray();
                a.Append(Arc.Create(c, r, 0, Math.PI, XYZ.BasisX, XYZ.BasisY));
                a.Append(Arc.Create(c, r, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY));
                return a;
            }

            private void Material(GenericForm f, ElementId m)
            {
                if (m == null || m == ElementId.InvalidElementId) return;
                f.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)?.Set(m);
            }

            /// <summary>Cilindro vertical de z0 a z1 (mm), centro em (x, y); com furo se rInt &gt; 0.</summary>
            public void Cilindro(double x, double y, double z0, double z1, double r, double rInt = 0, ElementId mat = null)
            {
                if (z1 - z0 < 0.05 || r <= 0) return;
                var perfil = new CurveArrArray(); perfil.Append(Circulo(x, y, z0, r));
                if (rInt > 0 && rInt < r) perfil.Append(Circulo(x, y, z0, rInt));
                var ex = _fd.FamilyCreate.NewExtrusion(true, perfil, Horizontal(z0), Mm(z1 - z0));
                Material(ex, mat ?? Aco);
            }

            /// <summary>Prisma sextavado (abertura da chave = distância entre faces), de z0 a z1.</summary>
            public void Sextavado(double chave, double z0, double z1, ElementId mat = null)
            {
                double rc = chave / Math.Sqrt(3);   // raio do círculo circunscrito
                var pts = Enumerable.Range(0, 6).Select(k => new XYZ(Mm(rc * Math.Cos(k * Math.PI / 3)), Mm(rc * Math.Sin(k * Math.PI / 3)), Mm(z0))).ToList();
                var a = new CurveArray();
                for (int k = 0; k < 6; k++) a.Append(Line.CreateBound(pts[k], pts[(k + 1) % 6]));
                var perfil = new CurveArrArray(); perfil.Append(a);
                var ex = _fd.FamilyCreate.NewExtrusion(true, perfil, Horizontal(z0), Mm(z1 - z0));
                Material(ex, mat ?? Aco);
            }

            /// <summary>Sólido de revolução em torno do eixo z, perfil (r, z) em mm no plano XZ.</summary>
            public void Revolucao(IList<(double R, double Z)> perfilRZ, ElementId mat = null)
            {
                var sp = SketchPlane.Create(_fd, Plane.CreateByNormalAndOrigin(XYZ.BasisY, XYZ.Zero));
                var a = new CurveArray();
                for (int k = 0; k < perfilRZ.Count; k++)
                {
                    var p = perfilRZ[k]; var q = perfilRZ[(k + 1) % perfilRZ.Count];
                    a.Append(Line.CreateBound(new XYZ(Mm(p.R), 0, Mm(p.Z)), new XYZ(Mm(q.R), 0, Mm(q.Z))));
                }
                var perfil = new CurveArrArray(); perfil.Append(a);
                var rv = _fd.FamilyCreate.NewRevolution(true, perfil, sp, Line.CreateBound(XYZ.Zero, XYZ.BasisZ), 0, 2 * Math.PI);
                Material(rv, mat ?? Aco);
            }

            /// <summary>Dobra do gancho: o círculo da barra (no plano horizontal em zb, centro na origem) gira 180° para baixo em torno do eixo y em x = R.</summary>
            public void Dobra(double zb, double r, double raio)
            {
                var perfil = new CurveArrArray(); perfil.Append(Circulo(0, 0, zb, r));
                var eixo = Line.CreateBound(new XYZ(Mm(raio), 0, Mm(zb)), new XYZ(Mm(raio), Mm(1), Mm(zb)));
                var rv = _fd.FamilyCreate.NewRevolution(true, perfil, Horizontal(zb), eixo, 0, Math.PI);   // 0 → π: para baixo (−π → 0 subia)
                Material(rv, Aco);
            }
        }

        private static string Criar(Autodesk.Revit.ApplicationServices.Application app, GeometriaChumbador g, string nome, StringBuilder log)
        {
            string modelo = Modelo(app);
            if (modelo == null) { log.AppendLine("Modelo de família genérico métrico não encontrado."); return null; }
            Directory.CreateDirectory(Pasta);
            string arq = Path.Combine(Pasta, nome + ".rfa");
            Document fd = app.NewFamilyDocument(modelo);
            try
            {
                using (var t = new Transaction(fd, "PCEB: " + nome))
                {
                    t.Start();
                    try { fd.OwnerFamily.FamilyCategory = fd.Settings.Categories.get_Item(BuiltInCategory.OST_StructConnections); } catch { }
                    var fm = fd.FamilyManager;
                    if (fm.CurrentType == null) fm.NewType("Padrão");
                    fm.AddParameter(FamiliasLigacoes.PLigacao, GroupTypeId.IdentityData, SpecTypeId.String.Text, true);
                    var c = new Construtor(fd);
                    c.Aco = MaterialDe(fd, "PCEB Aço galvanizado", new Color(170, 175, 180));
                    c.Resina = MaterialDe(fd, "PCEB Resina", new Color(196, 160, 90));
                    double r = g.Db / 2, zConc = -g.Pega, zFundo = zConc - g.Embutimento;
                    var p = g.Porca;
                    double tw = p.EspessuraArruela, m = p.AlturaPorca, topo = g.Topo;
                    // arruela e porca sobre a placa (todas as formas)
                    c.Cilindro(0, 0, 0, tw, p.DiametroArruela / 2, r + 1);
                    if (g.Forma == FormaChumbador.ParafusoConcreto)
                    {
                        // cabeça sextavada com flange no lugar da porca; fuste com fios até o embutimento
                        c.Cilindro(0, 0, tw, tw + 0.25 * g.Db, 0.95 * p.Chave);
                        c.Sextavado(p.Chave, tw + 0.25 * g.Db, tw + 0.25 * g.Db + p.AlturaCabeca);
                        c.Cilindro(0, 0, zFundo, tw, r * 0.85);
                        double passo = 0.7 * g.Db;
                        for (double z = zFundo + 0.5 * passo; z < zConc - 0.2 * passo; z += passo)
                            c.Revolucao(new[] { (r * 0.85, z), (r * 1.15, z + 0.15 * passo), (r * 1.15, z + 0.3 * passo), (r * 0.85, z + 0.45 * passo) });
                        c.Revolucao(new[] { (0.0, zFundo - 0.6 * g.Db), (r * 0.85, zFundo), (0.0, zFundo) });   // ponta
                    }
                    else
                    {
                        c.Sextavado(p.Chave, tw, tw + m);
                        switch (g.Forma)
                        {
                            case FormaChumbador.Gancho:
                                c.Cilindro(0, 0, zFundo, topo, r);
                                c.Dobra(zFundo, r, g.RaioGancho);
                                c.Cilindro(2 * g.RaioGancho, 0, zFundo, zFundo + g.PontaGancho, r);
                                break;
                            case FormaChumbador.RetoComPorca:
                                c.Cilindro(0, 0, zFundo, topo, r);
                                c.Sextavado(p.Chave, zFundo, zFundo + m);                         // porca de ancoragem
                                c.Cilindro(0, 0, zFundo + m, zFundo + m + tw, p.DiametroArruela / 2 * 1.4, r + 1);   // arruela/chapa de ancoragem
                                break;
                            case FormaChumbador.Quimico:
                                c.Cilindro(0, 0, zFundo, topo, r);
                                c.Revolucao(new[] { (0.0, zFundo - 1), (r, zFundo - 1), (r * 0.4, zFundo - 0.4 * g.Db), (0.0, zFundo - 0.4 * g.Db) });   // ponta chanfrada
                                c.Cilindro(0, 0, zFundo - 0.4 * g.Db - 3, zConc, g.DiametroFuro / 2, r, c.Resina);   // resina no anel do furo
                                break;
                            default:   // expansão: barra, luva de expansão e cone na ponta
                                c.Cilindro(0, 0, zFundo + 0.6 * g.Db, topo, r);
                                c.Cilindro(0, 0, zFundo + 0.6 * g.Db, zFundo + 2.6 * g.Db, r + Math.Max(1.5, 0.1 * g.Db), r);
                                c.Revolucao(new[] { (0.0, zFundo), (r * 1.15, zFundo), (r * 0.8, zFundo + 0.6 * g.Db), (0.0, zFundo + 0.6 * g.Db) });
                                break;
                        }
                    }
                    t.Commit();
                }
                fd.SaveAs(arq, new SaveAsOptions { OverwriteExistingFile = true });
                log.AppendLine($"{nome}: família gravada em {arq}.");
                return arq;
            }
            catch (Exception ex) { log.AppendLine($"{nome}: não foi possível criar ({ex.Message})."); return null; }
            finally { fd.Close(false); }
        }

        private static ElementId MaterialDe(Document fd, string nome, Color cor)
        {
            var m = new FilteredElementCollector(fd).OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(x => x.Name == nome);
            if (m != null) return m.Id;
            try
            {
                var id = Material.Create(fd, nome);
                var mat = (Material)fd.GetElement(id);
                mat.Color = cor; mat.Shininess = 60;
                return id;
            }
            catch { return ElementId.InvalidElementId; }
        }
    }
}
