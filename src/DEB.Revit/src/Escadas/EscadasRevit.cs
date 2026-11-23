using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DEB.Core.Elementos.Escada;
using DEB.Revit.Core;
using DEB.Revit.Precast;
using DEB.Revit.Presentation;
using DEB.Revit.Vigas;

namespace DEB.Revit.Escadas
{
    /// <summary>Onde a escada entra no modelo: ponto do eixo do apoio inferior (na cota do piso de baixo), sentido de subida e o lado.</summary>
    internal sealed class PosicaoEscada
    {
        /// <summary>Eixo do apoio inferior, no meio da largura, na cota do piso inferior (pés).</summary>
        public XYZ Base { get; set; } = XYZ.Zero;
        /// <summary>Sentido de subida (horizontal, unitário).</summary>
        public XYZ Direcao { get; set; } = XYZ.BasisX;
        public Level Nivel { get; set; }
        /// <summary>Texto para a janela: de onde veio a posição.</summary>
        public string Origem { get; set; } = "";
    }

    /// <summary>
    /// Escada no Revit (D42): família PCEB gerada pelo programa (modelo genérico que aceita armadura) com o contorno do núcleo
    /// — degraus, lance e patamares — extrudado na largura; a instância vai para a posição escolhida e recebe as barras do
    /// <see cref="ResultadoEscada"/> (as mesmas da prévia). A família é refeita a cada criação (geometria fixa por escada: refaça
    /// pelo comando para mudar). Marca E1, E2… na instância e na partição das barras.
    /// </summary>
    internal static class EscadasRevit
    {
        private const double Cm = 1 / 30.48;
        public const string PrefixoFamilia = "PCEB_Escada_";

        /// <summary>Lê a posição e a geometria inicial de dois apoios selecionados (vigas ou lajes): o mais baixo é o inferior.</summary>
        public static PosicaoEscada DeApoios(Document doc, IList<Element> apoios, EscadaLaje e)
        {
            if (apoios.Count != 2) return null;
            var info = apoios.Select(a => (El: a, Bb: a.get_BoundingBox(null), Lc: (a.Location as LocationCurve)?.Curve as Line)).ToList();
            if (info.Any(i => i.Bb == null)) return null;
            var ord = info.OrderBy(i => i.Bb.Max.Z).ToList();
            var inf = ord[0]; var sup = ord[1];
            XYZ CentroXY(BoundingBoxXYZ b) => new XYZ((b.Min.X + b.Max.X) / 2, (b.Min.Y + b.Max.Y) / 2, 0);
            XYZ pInf, pSup, d;
            double larguraApoio = e.LarguraApoio;
            if (inf.Lc != null && sup.Lc != null)
            {
                // vigas: sentido = perpendicular ao eixo da inferior, apontando para a superior
                var a0 = Plano(inf.Lc.GetEndPoint(0)); var a1 = Plano(inf.Lc.GetEndPoint(1));
                var t = (a1 - a0).Normalize();
                var cSup = Plano((sup.Lc.GetEndPoint(0) + sup.Lc.GetEndPoint(1)) / 2);
                double u = (cSup - a0).DotProduct(t);
                // trecho comum das duas vigas (projeção da superior na inferior)
                double s0 = (Plano(sup.Lc.GetEndPoint(0)) - a0).DotProduct(t), s1 = (Plano(sup.Lc.GetEndPoint(1)) - a0).DotProduct(t);
                double lo = Math.Max(0, Math.Min(s0, s1)), hi = Math.Min(a0.DistanceTo(a1), Math.Max(s0, s1));
                double meio = hi > lo ? (lo + hi) / 2 : Math.Max(0, Math.Min(a0.DistanceTo(a1), u));
                pInf = a0 + t * meio;
                var n = new XYZ(-t.Y, t.X, 0);
                d = (cSup - pInf).DotProduct(n) >= 0 ? n : -n;
                pSup = pInf + d * (cSup - pInf).DotProduct(d);
                if (hi > lo) e.Largura = Math.Min(e.Largura, Math.Floor((hi - lo) / Cm));
                double lb = Math.Abs((inf.Bb.Max - inf.Bb.Min).DotProduct(new XYZ(Math.Abs(d.X), Math.Abs(d.Y), 0)));
                if (lb > 0 && lb / Cm < 120) larguraApoio = Math.Round(lb / Cm);
            }
            else
            {
                pInf = CentroXY(inf.Bb); pSup = CentroXY(sup.Bb);
                d = (pSup - pInf).Normalize();
            }
            double vao = (pSup - pInf).DotProduct(d) / Cm;
            double desnivel = (sup.Bb.Max.Z - inf.Bb.Max.Z) / Cm;
            if (vao < 50 || desnivel < 30) return null;
            e.Desnivel = Math.Round(desnivel, 1);
            e.LarguraApoio = larguraApoio;
            // o lance cabe no vão? se sobrar, a sobra vira patamares (metade em cada ponta, ajustável na janela)
            var g = GeometriaEscada.De(e);
            double sobra = vao - g.LanceHorizontal;
            if (sobra > 0) { e.PatamarInferior = Math.Round(sobra / 2); e.PatamarSuperior = Math.Round(vao - g.LanceHorizontal - e.PatamarInferior); }
            var nivel = NivelAbaixo(doc, inf.Bb.Max.Z);
            return new PosicaoEscada
            {
                Base = new XYZ(pInf.X, pInf.Y, inf.Bb.Max.Z), Direcao = d, Nivel = nivel,
                Origem = $"Apoios: {Nome(inf.El)} (embaixo) e {Nome(sup.El)} (em cima); vão {vao:0} cm, desnível {desnivel:0} cm",
            };
        }

        private static XYZ Plano(XYZ p) => new XYZ(p.X, p.Y, 0);
        private static string Nome(Element e) => $"{e.Name} ({e.Id})";

        public static Level NivelAbaixo(Document doc, double z) =>
            new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Where(l => l.Elevation <= z + 1e-6).OrderByDescending(l => l.Elevation).FirstOrDefault()
            ?? new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).FirstOrDefault();

        /// <summary>Próxima marca livre (E1, E2…) pelas famílias PCEB de escada já no projeto.</summary>
        public static string ProximaMarca(Document doc)
        {
            var usadas = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().Select(f => f.Name)
                .Where(n => n.StartsWith(PrefixoFamilia)).Select(n => n.Substring(PrefixoFamilia.Length)).ToHashSet();
            for (int i = 1; ; i++) if (!usadas.Contains("E" + i)) return "E" + i;
        }

        // ------------------------------------------------------------------ família

        private static string Modelo(Autodesk.Revit.ApplicationServices.Application app)
        {
            var cand = new List<string>();
            if (!string.IsNullOrEmpty(app.FamilyTemplatePath))
            {
                cand.Add(Path.Combine(app.FamilyTemplatePath, "Metric Generic Model.rft"));
                cand.Add(Path.Combine(app.FamilyTemplatePath, "Modelo genérico métrico.rft"));
            }
            string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            foreach (string lang in new[] { "English", "Portuguese" })
            {
                string dir = Path.Combine(pd, "Autodesk", "RVT " + app.VersionNumber, "Family Templates", lang);
                cand.Add(Path.Combine(dir, "Metric Generic Model.rft"));
                cand.Add(Path.Combine(dir, "Modelo genérico métrico.rft"));
            }
            return cand.FirstOrDefault(File.Exists) ?? throw new InvalidOperationException("Modelo de família \"Metric Generic Model.rft\" não encontrado (Family Templates do Revit).");
        }

        /// <summary>Gera a família da escada (contorno extrudado na largura, aceita armadura) e carrega no projeto. Fora de transação.</summary>
        public static Family CriarFamilia(Document doc, ResultadoEscada r, string marca, PieceLog log)
        {
            var app = doc.Application;
            Document fd = app.NewFamilyDocument(Modelo(app));
            try
            {
                using (var t = new Transaction(fd, "Escada PCEB"))
                {
                    t.Start();
                    fd.OwnerFamily.get_Parameter(BuiltInParameter.FAMILY_CAN_HOST_REBAR)?.Set(1);
                    var k = r.Geo.Contorno;
                    var loop = new CurveArray();
                    for (int i = 0; i < k.Count; i++)
                    {
                        var a = k[i]; var b = k[(i + 1) % k.Count];
                        loop.Append(Line.CreateBound(new XYZ(a.X * Cm, 0, a.Z * Cm), new XYZ(b.X * Cm, 0, b.Z * Cm)));
                    }
                    var perfil = new CurveArrArray(); perfil.Append(loop);
                    var sp = SketchPlane.Create(fd, Plane.CreateByNormalAndOrigin(XYZ.BasisY, XYZ.Zero));
                    double w = r.Entrada.Largura * Cm;
                    Extrusion ex = fd.FamilyCreate.NewExtrusion(true, perfil, sp, w);
                    ex.StartOffset = -w / 2; ex.EndOffset = w / 2;
                    var conc = new FilteredElementCollector(fd).OfClass(typeof(Material)).Cast<Material>()
                        .FirstOrDefault(m => m.Name.IndexOf("concret", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (conc == null)
                    {
                        var id = Material.Create(fd, "Concreto armado (PCEB)");
                        conc = fd.GetElement(id) as Material;
                        if (conc != null) conc.Color = new Color(190, 190, 185);
                    }
                    if (conc != null) ex.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM)?.Set(conc.Id);
                    t.Commit();
                }
                string dir = Path.Combine(Path.GetTempPath(), "PCEB_Escadas");
                Directory.CreateDirectory(dir);
                string arq = Path.Combine(dir, PrefixoFamilia + marca + ".rfa");
                fd.SaveAs(arq, new SaveAsOptions { OverwriteExistingFile = true });
                Family fam = fd.LoadFamily(doc, new SempreSobrescrever());
                log?.Info($"Família {fam?.Name} gerada ({k(r)} pontos no contorno) e carregada.");
                return fam;
            }
            finally { fd.Close(false); }
        }

        private static int k(ResultadoEscada r) => r.Geo.Contorno.Count;

        // ------------------------------------------------------------------ instância e barras

        /// <summary>Coloca a escada e cria as barras. Dentro de transação do projeto.</summary>
        public static FamilyInstance Colocar(Document doc, Family fam, PosicaoEscada pos, ResultadoEscada r, string marca, PieceLog log, out List<ElementId> barras)
        {
            barras = new List<ElementId>();
            var sym = doc.GetElement(fam.GetFamilySymbolIds().First()) as FamilySymbol;
            if (!sym.IsActive) sym.Activate();
            var nivel = pos.Nivel ?? NivelAbaixo(doc, pos.Base.Z);
            var fi = doc.Create.NewFamilyInstance(pos.Base, sym, nivel, StructuralType.NonStructural);
            double ang = Math.Atan2(pos.Direcao.Y, pos.Direcao.X);
            if (Math.Abs(ang) > 1e-9)
                ElementTransformUtils.RotateElement(doc, fi.Id, Line.CreateBound(pos.Base, pos.Base + XYZ.BasisZ), ang);
            // a cota: a família é posicionada pelo nível + deslocamento; confere e corrige
            doc.Regenerate();
            if (fi.Location is LocationPoint lp && Math.Abs(lp.Point.Z - pos.Base.Z) > 1e-4)
                ElementTransformUtils.MoveElement(doc, fi.Id, new XYZ(0, 0, pos.Base.Z - lp.Point.Z));
            fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.Set(marca);
            var g = r.Geo;
            fi.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(
                $"Escada PCEB {marca}: {g.N} × {g.Espelho:0.0#}/{g.Piso:0.#} cm, h = {g.H:0.#} cm, vão {g.Vao:0} cm — " + string.Join("; ", r.Barras.Select(b => $"{b.Nome} ø{b.Bitola:0.#} c/{b.Espacamento:0.#}")));
            doc.Regenerate();

            // barras: sistema local (x ao longo, y na largura, z vertical) → modelo
            XYZ d = pos.Direcao.Normalize(), s = new XYZ(-d.Y, d.X, 0), up = XYZ.BasisZ, o = pos.Base;
            XYZ W(double x, double y, double z) => o + d * (x * Cm) + s * (y * Cm) + up * (z * Cm);
            var factory = new RebarFactory(doc, log);
            double cob = r.Entrada.Cobrimento, B = r.Entrada.Largura;
            var compr = new Dictionary<ElementId, double>();
            foreach (var b in r.Barras)
            {
                var tipo = factory.BarType(b.Bitola);
                try
                {
                    if (!b.Transversal)
                    {
                        double y0 = -(B / 2 - cob - b.Bitola / 20);
                        var curvas = new List<Curve>();
                        for (int i = 1; i < b.Pontos.Count; i++)
                        {
                            var p = b.Pontos[i - 1]; var q = b.Pontos[i];
                            if (Math.Abs(p.X - q.X) + Math.Abs(p.Z - q.Z) < 0.1) continue;
                            curvas.Add(Line.CreateBound(W(p.X, y0, p.Z), W(q.X, y0, q.Z)));
                        }
                        var rb = Rebar.CreateFromCurves(doc, RebarStyle.Standard, tipo, null, null, fi, s, curvas,
                            RebarHookOrientation.Left, RebarHookOrientation.Left, true, true);
                        double arr = (B - 2 * cob - b.Bitola / 10) * Cm;
                        rb.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(b.Espacamento * Cm, arr, true, true, true);
                        barras.Add(rb.Id); compr[rb.Id] = b.Comprimento;
                    }
                    else
                    {
                        // transversais: uma barra na largura (reta ou com as dobras do perfil), repetida ao longo de cada trecho reto da linha
                        double yy = B / 2 - cob;
                        var perfil = b.PerfilY.Count > 0 ? b.PerfilY : new List<(double Y, double N)> { (-yy, 0), (yy, 0) };
                        for (int i = 1; i < b.Pontos.Count; i++)
                        {
                            var p = b.Pontos[i - 1]; var q = b.Pontos[i];
                            double len = Math.Sqrt(Math.Pow(q.X - p.X, 2) + Math.Pow(q.Z - p.Z, 2));
                            if (len < b.Espacamento / 2 && b.Quantidade > 1) continue;
                            var dir = (W(q.X, 0, q.Z) - W(p.X, 0, p.Z)).Normalize();
                            // normal à laje no plano da elevação (para cima): o deslocamento N do perfil
                            double sx = -(q.Z - p.Z) / len, sz = (q.X - p.X) / len;
                            var curvas = new List<Curve>();
                            for (int j = 1; j < perfil.Count; j++)
                            {
                                var a = perfil[j - 1]; var c = perfil[j];
                                if (Math.Abs(a.Y - c.Y) + Math.Abs(a.N - c.N) < 0.1) continue;
                                curvas.Add(Line.CreateBound(W(p.X + sx * a.N, a.Y, p.Z + sz * a.N), W(p.X + sx * c.N, c.Y, p.Z + sz * c.N)));
                            }
                            var rb = Rebar.CreateFromCurves(doc, RebarStyle.Standard, tipo, null, null, fi, dir, curvas,
                                RebarHookOrientation.Left, RebarHookOrientation.Left, true, true);
                            if (b.Pontos.Count == 2 && b.Quantidade > 1) rb.GetShapeDrivenAccessor().SetLayoutAsFixedNumber(b.Quantidade, len * Cm, true, true, true);   // trecho único: o número do núcleo
                            else if (b.Quantidade > 1) rb.GetShapeDrivenAccessor().SetLayoutAsMaximumSpacing(b.Espacamento * Cm, len * Cm, true, i == 1, true);
                            barras.Add(rb.Id); compr[rb.Id] = b.Comprimento;
                        }
                    }
                }
                catch (Exception ex) { log?.Warn($"{b.Nome}: não deu para criar ({ex.Message})."); }
            }
            try
            {
                RebarData.EnsureParameters(doc);
                doc.Regenerate();
                RebarData.Apply(doc, barras, marca, compr);
            }
            catch (Exception ex) { log?.Warn("Dados das barras: " + ex.Message); }
            return fi;
        }
    }
}
