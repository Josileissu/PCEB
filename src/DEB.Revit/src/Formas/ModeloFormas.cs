using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DEB.Core.Formas;
using DEB.Revit.Core;
using DEB.Revit.Presentation;

namespace DEB.Revit.Formas
{
    /// <summary>Leituras do modelo usadas pelos comandos de formas (marca, filtro das peças, seção, níveis do pilar, nível da viga).
    /// Parâmetros embutidos sempre por BuiltInParameter (o Revit do escritório é pt-BR).</summary>
    internal static class ModeloFormas
    {
        public static string Marca(Element e) => (e?.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "").Trim();

        /// <summary>
        /// Peças da categoria (instâncias de 1º nível) que passam no filtro. Se NENHUMA tem o material pedido (família sem material
        /// estrutural definido), o critério de material é deixado de lado, com aviso — o mesmo critério da exportação para o CAD.
        /// </summary>
        public static List<FamilyInstance> Filtrar(Document doc, BuiltInCategory cat, PieceFilterSpec f, PieceLog log, string nome)
        {
            List<FamilyInstance> todas = new FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType().OfType<FamilyInstance>()
                .Where(x => x.SuperComponent == null).ToList();
            List<FamilyInstance> r = todas.Where(x => Passa(x, f)).ToList();
            if (r.Count == 0 && todas.Count > 0 && f?.Materiais != null && f.Materiais.Count > 0)
            {
                r = todas.Where(x => Passa(x, f, semMaterial: true)).ToList();
                if (r.Count > 0)
                    log?.Warn($"Nenhum(a) {nome} com material estrutural {string.Join("/", f.Materiais)}: o filtro de material foi ignorado " +
                              $"(materiais no modelo: {string.Join(", ", todas.Select(Material).Distinct())}).");
            }
            return r;
        }

        private static string Material(FamilyInstance fi) { try { return fi.StructuralMaterialType.ToString(); } catch { return ""; } }

        /// <summary>A peça entra (material estrutural, começo da marca, trecho do nome da família)?</summary>
        public static bool Passa(FamilyInstance fi, PieceFilterSpec f, bool semMaterial = false)
        {
            if (f == null) return true;
            if (!semMaterial && f.Materiais != null && f.Materiais.Count > 0)
            {
                string mat;
                try { mat = fi.StructuralMaterialType.ToString(); } catch { mat = ""; }
                if (!f.Materiais.Any(m => string.Equals(m?.Trim(), mat, StringComparison.OrdinalIgnoreCase))) return false;
            }
            if (!string.IsNullOrWhiteSpace(f.PrefixoMarca) && !Marca(fi).StartsWith(f.PrefixoMarca.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
            if (f.Familias != null && f.Familias.Count(x => !string.IsNullOrWhiteSpace(x)) > 0)
            {
                string fam = fi.Symbol?.FamilyName ?? "";
                if (!f.Familias.Any(x => !string.IsNullOrWhiteSpace(x) && fam.IndexOf(x.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)) return false;
            }
            return true;
        }

        /// <summary>"b x h" (cm) dos parâmetros do tipo (ou da instância); vazio se a família não tem as duas medidas.</summary>
        public static string Secao(FamilyInstance fi, FormasSettings cfg) =>
            TextosFormas.Secao(Medida(fi, cfg.ParametrosLargura, BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH),
                               Medida(fi, cfg.ParametrosAltura, BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT));

        /// <summary>Medida em cm: parâmetro de comprimento pelo nome (tipo, depois instância), senão o da seção estrutural.</summary>
        public static double? Medida(FamilyInstance fi, IEnumerable<string> nomes, BuiltInParameter reserva)
        {
            double? Ler(Parameter p) =>
                p != null && p.StorageType == StorageType.Double && p.HasValue && p.Definition.GetDataType() == SpecTypeId.Length && p.AsDouble() > 0
                    ? U.ToMm(p.AsDouble()) / 10 : (double?)null;
            foreach (string n in nomes ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                double? v = Ler(fi.Symbol?.LookupParameter(n)) ?? Ler(fi.LookupParameter(n));
                if (v != null) return v;
            }
            return Ler(fi.Symbol?.get_Parameter(reserva)) ?? Ler(fi.get_Parameter(reserva));
        }

        /// <summary>Z (interno) da base e do topo do pilar: níveis + deslocamentos; sem eles, a caixa do pilar.</summary>
        public static bool AlturasPilar(Document doc, FamilyInstance c, out double zBase, out double zTopo)
        {
            zBase = zTopo = 0;
            Level lb = doc.GetElement(c.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.AsElementId() ?? ElementId.InvalidElementId) as Level;
            Level lt = doc.GetElement(c.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)?.AsElementId() ?? ElementId.InvalidElementId) as Level;
            if (lb != null && lt != null)
            {
                zBase = lb.ProjectElevation + (c.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM)?.AsDouble() ?? 0);
                zTopo = lt.ProjectElevation + (c.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM)?.AsDouble() ?? 0);
                if (zTopo > zBase) return true;
            }
            BoundingBoxXYZ bb = c.get_BoundingBox(null);
            if (bb == null) return false;
            zBase = bb.Min.Z; zTopo = bb.Max.Z;
            return true;
        }

        /// <summary>Ponto do eixo do pilar (planta).</summary>
        public static XYZ Eixo(FamilyInstance c)
        {
            if (c.Location is LocationPoint lp) return lp.Point;
            if (c.Location is LocationCurve lc) return lc.Curve.GetEndPoint(0);
            BoundingBoxXYZ bb = c.get_BoundingBox(null);
            return bb == null ? null : (bb.Min + bb.Max) / 2;
        }

        /// <summary>
        /// Z (interno) do topo da viga no meio do vão: eixo (já com os deslocamentos de nível das pontas) + "Valor do deslocamento z",
        /// conforme a justificação z (topo, centro, origem = topo, base). A caixa da peça não serve: abas, consolos e capas aninhados
        /// sobem acima do topo da alma (deu "e=0" em todas as vigas de um modelo real).
        /// </summary>
        public static double? TopoDaViga(FamilyInstance b, FormasSettings cfg)
        {
            if (!(b.Location is LocationCurve lc)) return b.get_BoundingBox(null)?.Max.Z;
            double z = lc.Curve.Evaluate(0.5, true).Z + (b.get_Parameter(BuiltInParameter.Z_OFFSET_VALUE)?.AsDouble() ?? 0);
            int just = b.get_Parameter(BuiltInParameter.Z_JUSTIFICATION)?.AsInteger() ?? 0;   // 0 topo, 1 centro, 2 origem, 3 base
            double h = U.Mm((Medida(b, cfg.ParametrosAltura, BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT) ?? 0) * 10);
            if (just == 1) z += h / 2;
            else if (just == 3) z += h;
            return z;
        }

        /// <summary>Nível de referência da viga (parâmetro da instância; senão o hospedeiro).</summary>
        public static Level NivelDaViga(Document doc, FamilyInstance b) =>
            doc.GetElement(b.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)?.AsElementId() ?? ElementId.InvalidElementId) as Level
            ?? b.Host as Level
            ?? doc.GetElement(b.LevelId) as Level;

        /// <summary>Níveis do projeto, de baixo para cima.</summary>
        public static List<Level> Niveis(Document doc) =>
            new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.ProjectElevation).ToList();

        /// <summary>Remove os caracteres que o Revit não aceita em nomes de vistas e números de folha.</summary>
        public static string Nome(string s) => NameRegistry.Sanitize(s ?? "");
    }
}
