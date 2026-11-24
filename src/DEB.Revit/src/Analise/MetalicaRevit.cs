using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DEB.Core.Analise.Barras;
using DEB.Core.Analise.Objetos;
using DEB.Core.Configuracao.Projeto;
using DEB.Core.Elementos.Metalica;
using DEB.Revit.Vigas;

namespace DEB.Revit.Analise
{
    /// <summary>
    /// Peças de aço do Revit para a análise do PCEB (D48): vigas (framing) e pilares com material estrutural "Aço". O perfil vem do
    /// nome do tipo ("W310X38.7", "W 310 x 38,7", "CVS 400x200x9.5x6.3") ou das dimensões da seção da família (altura, largura,
    /// espessuras e raio); o aço, do material (A572, A36, MR250…) ou da configuração. O Revit só é lido.
    /// </summary>
    internal static class MetalicaRevit
    {
        private const double Cm = 30.48, Mm = 304.8;
        private static Vetor3 V(XYZ p) => new Vetor3(p.X * Cm, p.Y * Cm, p.Z * Cm);

        public static bool DeAco(FamilyInstance fi) => fi.StructuralMaterialType == StructuralMaterialType.Steel;

        /// <summary>Perfil do tipo: pelo nome (catálogo ou soldado) ou pelas dimensões da seção da família. null = não reconhecido.</summary>
        public static PerfilI Perfil(FamilySymbol s)
        {
            var p = PerfilI.Buscar(s.Name) ?? PerfilI.Buscar(s.FamilyName + " " + s.Name);
            if (p != null) return p;
            double D(BuiltInParameter bp) => (s.get_Parameter(bp)?.AsDouble() ?? 0) * Mm;
            double d = D(BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT), bf = D(BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH);
            double tf = D(BuiltInParameter.STRUCTURAL_SECTION_ISHAPE_FLANGETHICKNESS), tw = D(BuiltInParameter.STRUCTURAL_SECTION_ISHAPE_WEBTHICKNESS);
            double r = D(BuiltInParameter.STRUCTURAL_SECTION_ISHAPE_WEBFILLET);
            if (d > 0 && bf > 0 && tf > 0 && tw > 0) return new PerfilI(s.Name, d, bf, tf, tw, r, r <= 0);
            return null;
        }

        private static AcoEstrutural Aco(FamilyInstance fi, ConfiguracaoProjeto cfg)
        {
            Document doc = fi.Document;
            var mid = fi.StructuralMaterialId;
            string nome = mid != null && mid != ElementId.InvalidElementId ? (doc.GetElement(mid) as Material)?.Name ?? "" : "";
            foreach (var a in AcoEstrutural.Lista)
            {
                string chave = a.Nome.Split(' ').Last();   // "50", "A992", "A36", "MR250"…
                if (a.Nome.Contains("A572") && nome.Contains("A572")) return a;
                if (chave.Length > 2 && nome.IndexOf(chave, StringComparison.OrdinalIgnoreCase) >= 0) return a;
            }
            return AcoEstrutural.Buscar(cfg.Texto("materiais.acoPerfil")) ?? AcoEstrutural.Lista[0];
        }

        /// <summary>Lê as peças de aço para a estrutura; devolve os ids lidos (as outras leituras os pulam).</summary>
        public static HashSet<long> Ler(Document doc, EstruturaObjetos e, ConfiguracaoProjeto cfg, List<string> notas)
        {
            var lidos = new HashSet<long>();
            e.Biblioteca = DEB.Core.Elementos.Metalica.Ligacoes.BibliotecaLigacoes.Carregar(notas);
            var lig = cfg.Texto("analise.ligacaoMetalica").StartsWith("R") ? LigacaoExtremidade.Rigida : LigacaoExtremidade.Flexivel;
            var bas = cfg.Texto("analise.baseMetalica").StartsWith("R") ? BasePilarMetalico.Rotulada : BasePilarMetalico.Engastada;
            int semPerfil = 0, giradas = 0;
            foreach (BuiltInCategory cat in new[] { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming })
                foreach (FamilyInstance fi in new FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType()
                             .OfType<FamilyInstance>().Where(x => x.SuperComponent == null && DeAco(x)))
                {
                    PerfilI perfil = Perfil(fi.Symbol);
                    if (perfil == null) { semPerfil++; continue; }
                    bool pilar = cat == BuiltInCategory.OST_StructuralColumns;
                    XYZ a, b;
                    if (fi.Location is LocationCurve lc && lc.Curve is Line ln) { a = ln.GetEndPoint(0); b = ln.GetEndPoint(1); }
                    else if (fi.Location is LocationPoint lp && fi.get_BoundingBox(null) is BoundingBoxXYZ bb)
                    { a = new XYZ(lp.Point.X, lp.Point.Y, bb.Min.Z); b = new XYZ(lp.Point.X, lp.Point.Y, bb.Max.Z); }
                    else continue;
                    if (pilar && a.Z > b.Z) { var t = a; a = b; b = t; }
                    string marca = fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
                    var o = new BarraMetalicaObjeto
                    {
                        Dono = new Dono { Tipo = pilar ? TipoDono.Pilar : TipoDono.Viga, Id = fi.Id.Value.ToString(),
                                          Nome = $"{(string.IsNullOrEmpty(marca) ? (pilar ? "pilar" : "viga") : marca)} {perfil.Nome} ({fi.Id.Value})" },
                        PontoI = V(a), PontoJ = V(b), Perfil = perfil, Aco = Aco(fi, cfg), LigacaoI = lig, LigacaoJ = lig, Base = bas,
                    };
                    if (pilar)
                    {
                        // a altura do perfil (d) segue o eixo Y da família do pilar
                        XYZ y = fi.GetTransform().BasisY;
                        o.DirAlma = new Vetor3(y.X, y.Y, y.Z);
                        o.LigacaoI = o.LigacaoJ = LigacaoExtremidade.Rigida;   // o pilar é contínuo; as vigas é que chegam rotuladas
                    }
                    else
                    {
                        double giro = fi.get_Parameter(BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE)?.AsDouble() ?? 0;
                        if (Math.Abs(giro) > 1e-3) giradas++;
                        var cargas = ParametrosCargasViga.Ler(fi);
                        o.G = cargas.G; o.Q = cargas.Q;
                        foreach (var c in cargas.Concentradas) o.Concentradas.Add((c.X, c.G, c.Q));
                    }
                    e.Metalicas.Add(o);
                    lidos.Add(fi.Id.Value);
                }
            if (e.Metalicas.Count > 0)
                notas.Add($"{e.Metalicas.Count(x => x.Pilar)} pilares e {e.Metalicas.Count(x => !x.Pilar)} vigas de aço (NBR 8800:2024); " +
                          $"ligação viga–pilar {(lig == LigacaoExtremidade.Rigida ? "rígida" : "flexível")}, base {(bas == BasePilarMetalico.Engastada ? "engastada" : "rotulada")} (configuração do projeto).");
            if (semPerfil > 0) notas.Add($"{semPerfil} peças de aço sem perfil I/H reconhecido (nome do tipo ou dimensões da seção) ficaram fora da análise.");
            if (giradas > 0) notas.Add($"{giradas} vigas de aço giradas no Revit: a análise considera a alma vertical — conferir.");
            return lidos;
        }
    }
}
