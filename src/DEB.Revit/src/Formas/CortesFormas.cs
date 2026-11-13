using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DEB.Revit.Core;
using DEB.Revit.Presentation;

namespace DEB.Revit.Formas
{
    /// <summary>
    /// Cortes e elevações da estrutura (D25, etapa 5 antecipada a pedido do usuário, 13/11/2026). O comando cria os seus — um corte por
    /// direção entre os eixos centrais e as quatro elevações (cortes por fora da estrutura, olhando para dentro) — marcados como do
    /// comando; os cortes que já existem no modelo não são mexidos. Os níveis aparecem pelas cabeças de nível do próprio Revit; o
    /// comando põe os identificadores de pilares e vigas (acima de cada peça). Rodar de novo reaproveita as vistas e os identificadores.
    /// Precisa de transação.
    /// </summary>
    internal sealed class CortesFormas
    {
        private readonly Document _doc;
        private readonly FormasSettings _cfg;
        private readonly PieceLog _log;

        public CortesFormas(Document doc, FormasSettings cfg, PieceLog log) { _doc = doc; _cfg = cfg; _log = log; }

        private sealed class Plano
        {
            public string Papel, Nome;
            public XYZ Origem, Dir;      // Dir = direção da direita da vista (horizontal)
            public double Meia, Prof;    // meia largura e profundidade (pés)
        }

        /// <summary>Cortes (true) ou elevações (false), criados ou reaproveitados.</summary>
        public List<ViewSection> Vistas(bool cortes)
        {
            var est = Estrutura();
            if (est == null) { _log.Warn("Cortes: nenhuma peça estrutural de concreto para enquadrar."); return new List<ViewSection>(); }
            var (min, max) = est.Value;
            double m = U.Mm(1500);
            XYZ c = (min + max) / 2;
            double lx = (max.X - min.X) / 2 + m, ly = (max.Y - min.Y) / 2 + m;
            var planos = new List<Plano>();
            if (cortes)
            {
                // entre os dois eixos centrais de cada direção (o corte não passa em cima da linha dos pilares)
                var grids = new FilteredElementCollector(_doc).OfClass(typeof(Grid)).Cast<Grid>().Select(g => g.Curve).OfType<Line>().ToList();
                double Meio(IEnumerable<double> v, double padrao)
                {
                    var l = v.OrderBy(x => x).ToList();
                    if (l.Count < 2) return padrao;
                    int i = (l.Count - 1) / 2;
                    return (l[i] + l[i + 1]) / 2;
                }
                double yc = Meio(grids.Where(g => Math.Abs(g.Direction.X) > 0.9999).Select(g => g.Origin.Y), c.Y);
                double xc = Meio(grids.Where(g => Math.Abs(g.Direction.Y) > 0.9999).Select(g => g.Origin.X), c.X);
                planos.Add(new Plano { Papel = "corte:A", Nome = Nome(_cfg.Cortes.NomeVista, "A", ""), Origem = new XYZ(c.X, yc, 0), Dir = XYZ.BasisX, Meia = lx, Prof = max.Y - yc + m });
                planos.Add(new Plano { Papel = "corte:B", Nome = Nome(_cfg.Cortes.NomeVista, "B", ""), Origem = new XYZ(xc, c.Y, 0), Dir = -XYZ.BasisY, Meia = ly, Prof = max.X - xc + m });
            }
            else
            {
                // elevação = fachada: só a faixa da frente (ProfundidadeElevacaoM), senão os identificadores de todas as peças de trás
                // se amontoam
                double fx = m + U.Mm(_cfg.ProfundidadeElevacaoM * 1000);
                double py = Math.Min(max.Y - min.Y + 2 * m, fx), px = Math.Min(max.X - min.X + 2 * m, fx);
                planos.Add(new Plano { Papel = "elevacao:1", Nome = Nome(_cfg.Elevacoes.NomeVista, "", "1"), Origem = new XYZ(c.X, min.Y - m, 0), Dir = XYZ.BasisX, Meia = lx, Prof = py });
                planos.Add(new Plano { Papel = "elevacao:2", Nome = Nome(_cfg.Elevacoes.NomeVista, "", "2"), Origem = new XYZ(max.X + m, c.Y, 0), Dir = XYZ.BasisY, Meia = ly, Prof = px });
                planos.Add(new Plano { Papel = "elevacao:3", Nome = Nome(_cfg.Elevacoes.NomeVista, "", "3"), Origem = new XYZ(c.X, max.Y + m, 0), Dir = -XYZ.BasisX, Meia = lx, Prof = py });
                planos.Add(new Plano { Papel = "elevacao:4", Nome = Nome(_cfg.Elevacoes.NomeVista, "", "4"), Origem = new XYZ(min.X - m, c.Y, 0), Dir = -XYZ.BasisY, Meia = ly, Prof = px });
            }
            var r = new List<ViewSection>();
            List<ViewSection> marcadas = new FilteredElementCollector(_doc).OfClass(typeof(ViewSection)).Cast<ViewSection>().Where(v => !v.IsTemplate).ToList();
            foreach (Plano p in planos)
            {
                ViewSection v = marcadas.FirstOrDefault(x => MarcaFormas.Papel(x) == p.Papel);
                if (v == null)
                {
                    try { v = Criar(p, min.Z - m, max.Z + m); }
                    catch (Exception ex) { _log.Warn($"{p.Nome}: não criada ({ex.Message})."); continue; }
                    _log.Info($"Vista \"{v.Name}\" criada.");
                }
                else _log.Info($"Vista \"{v.Name}\" (do comando) atualizada.");
                if (v.Scale != _cfg.EscalaCortes) try { v.Scale = _cfg.EscalaCortes; } catch { /* template */ }
                r.Add(v);
            }
            return r;
        }

        private static string Nome(string modelo, string letra, string lado) => (modelo ?? "").Replace("{Letra}", letra).Replace("{Lado}", lado);

        private ViewSection Criar(Plano p, double zMin, double zMax)
        {
            ViewFamilyType vft = new FilteredElementCollector(_doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.Section) ?? throw new InvalidOperationException("O projeto não tem tipo de vista de corte.");
            // caixa do corte: X = direita da vista, Y = para cima, Z = para o observador (a vista olha para −Z); corte em Z = 0
            var t = Transform.Identity;
            t.Origin = p.Origem;
            t.BasisX = p.Dir;
            t.BasisY = XYZ.BasisZ;
            t.BasisZ = p.Dir.CrossProduct(XYZ.BasisZ);
            var box = new BoundingBoxXYZ { Transform = t, Min = new XYZ(-p.Meia, zMin, -p.Prof), Max = new XYZ(p.Meia, zMax, 0) };
            ViewSection v = ViewSection.CreateSection(_doc, vft.Id, box);
            v.Name = NameRegistry.ForViews(_doc).Unique(ModeloFormas.Nome(p.Nome));
            MarcaFormas.Marcar(v, p.Papel);
            return v;
        }

        /// <summary>Caixa (pés, coordenadas internas) dos pilares e vigas de concreto do filtro.</summary>
        private (XYZ Min, XYZ Max)? Estrutura()
        {
            var els = ModeloFormas.Filtrar(_doc, BuiltInCategory.OST_StructuralColumns, _cfg.FiltroPilares, null, "pilar")
                .Concat(ModeloFormas.Filtrar(_doc, BuiltInCategory.OST_StructuralFraming, _cfg.FiltroVigas, null, "viga"))
                .Select(e => e.get_BoundingBox(null)).Where(b => b != null).ToList();
            if (els.Count == 0) return null;
            return (new XYZ(els.Min(b => b.Min.X), els.Min(b => b.Min.Y), els.Min(b => b.Min.Z)),
                    new XYZ(els.Max(b => b.Max.X), els.Max(b => b.Max.Y), els.Max(b => b.Max.Z)));
        }

        /// <summary>Identificadores de pilares e vigas no corte/elevação, acima de cada peça.</summary>
        public void Anotar(View v, PlantaFormas planta)
        {
            _doc.Regenerate();
            planta.EsconderMarcasAlheias(v);
            var pilaresOk = new HashSet<ElementId>(ModeloFormas.Filtrar(_doc, BuiltInCategory.OST_StructuralColumns, _cfg.FiltroPilares, null, "pilar").Select(c => c.Id));
            var vigasOk = new HashSet<ElementId>(ModeloFormas.Filtrar(_doc, BuiltInCategory.OST_StructuralFraming, _cfg.FiltroVigas, null, "viga").Select(c => c.Id));
            List<Element> Vis(BuiltInCategory cat, HashSet<ElementId> ok) => new FilteredElementCollector(_doc, v.Id).OfCategory(cat)
                .WhereElementIsNotElementType().Where(e => ok.Contains(e.Id) && ModeloFormas.Marca(e) != "").ToList();
            double off = U.Mm(_cfg.AfastamentoIdentificadorMm * v.Scale);
            (XYZ, double?)? Acima(Element e)
            {
                var c = PlantaFormas.Caixa(e, v);
                if (c == null) return null;
                double r = (c.Value.MinR + c.Value.MaxR) / 2, u = c.Value.MaxU + off;
                XYZ o = v.Origin;
                return (o + v.RightDirection * (r - o.DotProduct(v.RightDirection)) + v.UpDirection * (u - o.DotProduct(v.UpDirection)), (double?)null);
            }
            planta.Identificar(v, "PilarMontagem", BuiltInCategory.OST_StructuralColumnTags, Vis(BuiltInCategory.OST_StructuralColumns, pilaresOk), PlantaFormas.Lado.Acima, Acima);
            planta.Identificar(v, "Viga", BuiltInCategory.OST_StructuralFramingTags, Vis(BuiltInCategory.OST_StructuralFraming, vigasOk), PlantaFormas.Lado.Acima, Acima);
        }
    }
}
