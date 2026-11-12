using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using DEB.Core.Lajes;
using static DEB.Revit.LajeAlveolar.GeometriaRevit;

namespace DEB.Revit.LajeAlveolar
{
    /// <summary>
    /// Leitura do modelo para a paginação (só lê): contorno e furos do croqui do piso, sentido do vão (SpanDirectionAngle), topo;
    /// vigas em volta (seção medida por linhas horizontais que cortam o sólido — acha alma, aba e se a viga sobe ao lado do painel);
    /// pilares (pontos da seção na altura do painel). Saída em metros, coordenadas do modelo.
    /// </summary>
    internal static class ExtracaoLajeAlveolar
    {
        /// <summary>Comentário que marca o que o comando criou (proteção contra duplicação).</summary>
        public const string Prefixo = "LAJE ALVEOLAR ";

        public static string Comentario(Element e) => e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "";
        public static bool CriadoPeloComando(Element e) => Comentario(e).StartsWith(Prefixo, StringComparison.Ordinal);

        public static string Id(Element e) => e.Id.Value.ToString(CultureInfo.InvariantCulture);

        public static PisoPlanta Extrair(Document doc, Floor piso, ConfigPaginacao cfg, ElementId familiaPainel, List<string> notas)
        {
            var bb = piso.get_BoundingBox(null) ?? throw new InvalidOperationException("piso sem geometria");
            var r = new PisoPlanta
            {
                Id = Id(piso),
                Nome = (piso.Name + " " + (piso.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "")).Trim(),
                AnguloVao = piso.SpanDirectionAngle,
                ZTopo = M(bb.Max.Z),
            };
            r.Loops = Loops(doc, piso);
            if (r.Loops.Count == 0) throw new InvalidOperationException("não consegui ler o contorno do piso");

            double zBase = r.ZTopo - cfg.Capa - cfg.AlturaPainel;
            // vizinhança: 1 m em planta; de 60 cm abaixo do painel até 10 cm acima do piso
            var outline = new Outline(new XYZ(bb.Min.X - Ft(1), bb.Min.Y - Ft(1), Ft(zBase - 0.6)), new XYZ(bb.Max.X + Ft(1), bb.Max.Y + Ft(1), Ft(r.ZTopo + 0.1)));
            var filtro = new BoundingBoxIntersectsFilter(outline);

            foreach (FamilyInstance fi in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming)
                         .WhereElementIsNotElementType().WherePasses(filtro).OfType<FamilyInstance>())
            {
                if (CriadoPeloComando(fi) || (familiaPainel != null && fi.Symbol?.Family?.Id == familiaPainel)) continue;
                try
                {
                    var v = Viga(fi, zBase, cfg);
                    if (v != null) r.Vigas.Add(v);
                }
                catch (Exception ex) { notas.Add($"viga {Id(fi)}: seção não lida ({ex.Message})"); }
            }
            foreach (FamilyInstance fi in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralColumns)
                         .WhereElementIsNotElementType().WherePasses(filtro).OfType<FamilyInstance>())
            {
                try
                {
                    var p = Pilar(fi, zBase + 0.8 * cfg.AlturaPainel);
                    if (p != null) r.Pilares.Add(p);
                }
                catch (Exception ex) { notas.Add($"pilar {Id(fi)}: seção não lida ({ex.Message})"); }
            }
            return r;
        }

        /// <summary>Laços do croqui (contorno e furos) em metros; o maior (pela caixa) vem primeiro = contorno externo.</summary>
        private static List<List<(double X, double Y)>> Loops(Document doc, Floor piso)
        {
            var loops = new List<List<(double X, double Y)>>();
            if (doc.GetElement(piso.SketchId) is Sketch sk)
            {
                foreach (CurveArray ca in sk.Profile)
                {
                    var pts = new List<(double, double)>();
                    foreach (Curve c in ca)
                    {
                        var t = c.Tessellate();
                        for (int k = 0; k < t.Count - 1; k++) pts.Add((M(t[k].X), M(t[k].Y)));
                    }
                    if (pts.Count >= 3) loops.Add(pts);
                }
            }
            if (loops.Count == 0)
            {
                // sem croqui: laços da face superior
                var ss = Solidos(piso, out var donos);
                PlanarFace topo = null;
                foreach (var s in ss) foreach (Face f in s.Faces)
                        if (f is PlanarFace pf && pf.FaceNormal.Z > 0.99 && (topo == null || pf.Origin.Z > topo.Origin.Z)) topo = pf;
                if (topo != null)
                    foreach (EdgeArray ea in topo.EdgeLoops)
                    {
                        var pts = new List<(double, double)>();
                        foreach (Edge ed in ea) { var t = ed.Tessellate(); for (int k = 0; k < t.Count - 1; k++) pts.Add((M(t[k].X), M(t[k].Y))); }
                        if (pts.Count >= 3) loops.Add(pts);
                    }
                GC.KeepAlive(donos);
            }
            double Area(List<(double X, double Y)> l) => (l.Max(p => p.X) - l.Min(p => p.X)) * (l.Max(p => p.Y) - l.Min(p => p.Y));
            return loops.OrderByDescending(Area).ToList();
        }

        /// <summary>Seção da viga no meio do comprimento: linhas horizontais nas cotas de leitura (relativas à base do painel).</summary>
        private static VigaPlanta Viga(FamilyInstance fi, double zBase, ConfigPaginacao cfg)
        {
            if (!(fi.Location is LocationCurve lc) || !(lc.Curve is Line ln)) return null;
            var a0 = ln.GetEndPoint(0); var a1 = ln.GetEndPoint(1);
            var dh = new XYZ(a1.X - a0.X, a1.Y - a0.Y, 0);
            if (dh.GetLength() < 1e-6) return null;   // viga vertical
            var d = dh.Normalize(); var n = new XYZ(-d.Y, d.X, 0);
            var v = new VigaPlanta { Id = Id(fi), Tipo = fi.Symbol?.Name ?? "", P0 = (M(a0.X), M(a0.Y)), P1 = (M(a1.X), M(a1.Y)) };
            var ss = Solidos(fi, out var donos);
            if (ss.Count == 0) return null;
            var c0 = a0 + d * ((a1 - a0).DotProduct(d) * 0.5);
            var lv = cfg.LeituraViga;
            var zs = new List<double> { lv.DzAlma, lv.DzAba };
            for (double z = lv.DzPainelMin; z < lv.DzPainelMax - 1e-9; z += lv.PassoPainel) zs.Add(z);
            zs.Add(lv.DzPainelMax);
            foreach (var z in zs)
            {
                var o = new XYZ(c0.X, c0.Y, Ft(zBase + z));
                var tr = TrechosDentro(ss, o, n, Ft(1.5));
                if (tr.Count == 0) continue;
                v.Secao.Linhas.Add(new LinhaSecao { Z = z, Trechos = tr.Select(t => (M(t.A), M(t.B))).ToList() });
            }
            GC.KeepAlive(donos);
            return v.Secao.Linhas.Count > 0 ? v : null;
        }

        /// <summary>Pontos da seção do pilar na cota z (m): varredura com linhas em x e em y sobre a caixa do pilar.</summary>
        private static PilarPlanta Pilar(FamilyInstance fi, double zm)
        {
            var bb = fi.get_BoundingBox(null);
            if (bb == null || Ft(zm) < bb.Min.Z || Ft(zm) > bb.Max.Z) return null;
            var ss = Solidos(fi, out var donos);
            var p = new PilarPlanta { Id = Id(fi) };
            const int nAm = 40;
            double z = Ft(zm);
            for (int i = 0; i <= nAm; i++)
            {
                double y = bb.Min.Y + (bb.Max.Y - bb.Min.Y) * i / nAm;
                var o = new XYZ((bb.Min.X + bb.Max.X) / 2, y, z);
                double meio = (bb.Max.X - bb.Min.X) / 2 + 0.5;
                foreach (var t in TrechosDentro(ss, o, XYZ.BasisX, meio)) { p.Pontos.Add((M(o.X + t.A), M(y))); p.Pontos.Add((M(o.X + t.B), M(y))); }
                double x = bb.Min.X + (bb.Max.X - bb.Min.X) * i / nAm;
                var o2 = new XYZ(x, (bb.Min.Y + bb.Max.Y) / 2, z);
                double meio2 = (bb.Max.Y - bb.Min.Y) / 2 + 0.5;
                foreach (var t in TrechosDentro(ss, o2, XYZ.BasisY, meio2)) { p.Pontos.Add((M(x), M(o2.Y + t.A))); p.Pontos.Add((M(x), M(o2.Y + t.B))); }
            }
            GC.KeepAlive(donos);
            return p.Pontos.Count > 0 ? p : null;
        }
    }
}
