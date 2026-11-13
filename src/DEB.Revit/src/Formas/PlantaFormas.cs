using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using DEB.Revit.Core;
using DEB.Revit.Presentation;

namespace DEB.Revit.Formas
{
    public enum TipoPlanta { Locacao, Montagem, Paginacao }

    /// <summary>
    /// Plantas de formas (D25, etapa 3): prepara a vista do nível (a indicada, a que o comando já criou, ou uma cópia/nova planta
    /// estrutural) em 1:50 e anota — cotas entre eixos (uma linha acima e uma à esquerda, referências dos eixos), identificadores
    /// de pilar, fundação, viga (alinhado à peça, acima dela) e laje (no centro), e o símbolo de sentido do vão. Tudo o que o
    /// comando cria leva a marca <see cref="MarcaFormas"/>: rodar de novo refaz as cotas, mantém os identificadores que já existem
    /// (só troca o tipo, se mudou), cria os que faltam e apaga os seus que ficaram sem peça. Precisa de transação.
    /// </summary>
    internal sealed class PlantaFormas
    {
        private readonly Document _doc;
        private readonly FormasSettings _cfg;
        private readonly PieceLog _log;
        private readonly Dictionary<string, FamilySymbol> _tipos = new Dictionary<string, FamilySymbol>();

        public PlantaFormas(Document doc, FormasSettings cfg, PieceLog log) { _doc = doc; _cfg = cfg; _log = log; }

        private static readonly HashSet<string> Recarregadas = new HashSet<string>();

        /// <summary>Carrega a família por cima da do projeto, inclusive os valores dos parâmetros.</summary>
        private sealed class SubstituirFamilia : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues) { overwriteParameterValues = true; return true; }
            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            { source = FamilySource.Family; overwriteParameterValues = true; return true; }
        }

        public static string Chave(TipoPlanta t) => t == TipoPlanta.Locacao ? "locacao" : t == TipoPlanta.Paginacao ? "paginacao" : "montagem";

        private FormasSheetSpec SpecDe(TipoPlanta t) => t == TipoPlanta.Locacao ? _cfg.Locacao : t == TipoPlanta.Paginacao ? _cfg.Paginacao : _cfg.Montagem;
        public static string PapelVista(TipoPlanta t, Level l) => $"vista-{Chave(t)}:{l.UniqueId}";

        // ------------------------------------------------------------------ vista

        /// <summary>Vista da planta: a indicada (fica marcada como a do comando), a já marcada, ou uma nova.</summary>
        public ViewPlan Vista(Level nivel, TipoPlanta tipo, ViewPlan indicada)
        {
            string papel = PapelVista(tipo, nivel);
            List<ViewPlan> marcadas = MarcaFormas.Com(new FilteredElementCollector(_doc).OfClass(typeof(ViewPlan)), papel).OfType<ViewPlan>().ToList();
            ViewPlan v;
            bool nova = false;
            if (indicada != null)
            {
                if (indicada.GenLevel == null || indicada.GenLevel.Id != nivel.Id)
                    throw new InvalidOperationException($"A vista \"{indicada.Name}\" não é planta do nível \"{nivel.Name}\".");
                foreach (ViewPlan o in marcadas.Where(m => m.Id != indicada.Id)) MarcaFormas.Desmarcar(o);
                v = indicada;
                MarcaFormas.Marcar(v, papel);
                _log.Info($"Vista indicada: \"{v.Name}\".");
            }
            else if (marcadas.Count > 0)
            {
                v = marcadas[0];
                _log.Info($"Vista \"{v.Name}\" (do comando) atualizada.");
            }
            else
            {
                v = NovaVista(nivel, SpecDe(tipo).VistaBase);
                string nome = ModeloFormas.Nome(TabelasFormas.Fill(SpecDe(tipo).NomeVista, nivel));
                v.Name = NameRegistry.ForViews(_doc).Unique(nome);
                MarcaFormas.Marcar(v, papel);
                nova = true;
                _log.Info($"Vista \"{v.Name}\" criada.");
            }
            if (v.Scale != _cfg.Escala)
                try { v.Scale = _cfg.Escala; }
                catch { _log.Warn($"Escala da vista \"{v.Name}\" controlada pelo template de vista (ficou 1:{v.Scale})."); }
            if (nova)
            {
                Profundidade(v, nivel);
                _doc.Regenerate();
                Recortar(v);
            }
            return v;
        }

        /// <summary>Cópia (sem anotações) de uma planta estrutural do nível — guarda o template e a visibilidade do escritório —,
        /// senão uma planta estrutural nova.</summary>
        private ViewPlan NovaVista(Level nivel, string preferida)
        {
            // a planta do nível cujo nome contém o trecho do padrão ("montagem", "Locação"); depois as sem template (o template de
            // outra disciplina, ex. plantas de cargas, esconde pisos e peças); depois as estruturais
            string pref = preferida?.Trim() ?? "";
            ViewPlan fonte = new FilteredElementCollector(_doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .Where(p => !p.IsTemplate && p.GenLevel != null && p.GenLevel.Id == nivel.Id &&
                            (p.ViewType == ViewType.EngineeringPlan || p.ViewType == ViewType.FloorPlan) &&
                            MarcaFormas.Papel(p) == "" && p.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
                .OrderBy(p => pref != "" && p.Name.IndexOf(pref, StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1)
                .ThenBy(p => p.ViewTemplateId == ElementId.InvalidElementId ? 0 : 1)
                .ThenBy(p => p.ViewType == ViewType.EngineeringPlan ? 0 : 1).ThenBy(p => p.Name).FirstOrDefault();
            if (fonte != null)
            {
                _log.Info($"Planta nova = cópia de \"{fonte.Name}\" (sem anotações).");
                return (ViewPlan)_doc.GetElement(fonte.Duplicate(ViewDuplicateOption.Duplicate));
            }
            ViewFamilyType vft = new FilteredElementCollector(_doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .Where(t => t.ViewFamily == ViewFamily.StructuralPlan || t.ViewFamily == ViewFamily.FloorPlan)
                .OrderBy(t => t.ViewFamily == ViewFamily.StructuralPlan ? 0 : 1).FirstOrDefault()
                ?? throw new InvalidOperationException("O projeto não tem tipo de vista de planta.");
            return ViewPlan.Create(_doc, vft.Id, nivel.Id);
        }

        /// <summary>Vista nova olha abaixo do nível (vigas com o topo no nível, blocos de fundação): fundo e profundidade = nível −
        /// <see cref="FormasSettings.ProfundidadeVistaM"/>. Template que controla a faixa da vista: fica a dele, com aviso.</summary>
        private void Profundidade(ViewPlan v, Level nivel)
        {
            if (_cfg.ProfundidadeVistaM <= 0) return;
            try
            {
                PlanViewRange r = v.GetViewRange();
                double d = -U.Mm(_cfg.ProfundidadeVistaM * 1000);
                r.SetLevelId(PlanViewPlane.ViewDepthPlane, nivel.Id);
                r.SetOffset(PlanViewPlane.ViewDepthPlane, d);
                r.SetLevelId(PlanViewPlane.BottomClipPlane, nivel.Id);
                r.SetOffset(PlanViewPlane.BottomClipPlane, d);
                v.SetViewRange(r);
            }
            catch (Exception ex) { _log.Warn($"Faixa da vista \"{v.Name}\" não ajustada ({ex.Message}); peças abaixo do nível podem não aparecer."); }
        }

        /// <summary>Recorte da vista nova: eixos e peças estruturais visíveis, mais a folga do padrão.</summary>
        private void Recortar(ViewPlan v)
        {
            try
            {
                // estrutura (caixa das peças estruturais visíveis); os eixos são encurtados nesta vista (extensão 2D) até a estrutura
                // + EixoAlemM, e o recorte fica justo: estrutura + eixos + bolhas (FolgaRecorteM). Planta grande cabe em 1:50.
                var pts = new List<XYZ>();
                foreach (Element e in Estruturais(v))
                {
                    BoundingBoxXYZ bb = e.get_BoundingBox(v);
                    if (bb != null) { pts.Add(bb.Min); pts.Add(bb.Max); }
                }
                if (pts.Count < 2) return;
                double ext = U.Mm(_cfg.EixoAlemM * 1000);
                foreach (Grid g in new FilteredElementCollector(_doc, v.Id).OfClass(typeof(Grid)).Cast<Grid>())
                {
                    try
                    {
                        if (!(g.GetCurvesInView(DatumExtentType.ViewSpecific, v).FirstOrDefault() is Line l)) continue;
                        XYZ d = l.Direction, o = l.GetEndPoint(0);
                        double t0 = pts.Min(p => (p - o).DotProduct(d)) - ext, t1 = pts.Max(p => (p - o).DotProduct(d)) + ext;
                        g.SetDatumExtentType(DatumEnds.End0, v, DatumExtentType.ViewSpecific);
                        g.SetDatumExtentType(DatumEnds.End1, v, DatumExtentType.ViewSpecific);
                        g.SetCurveInView(DatumExtentType.ViewSpecific, v, Line.CreateBound(o + d * t0, o + d * t1));
                    }
                    catch { /* eixo que não aceita: fica como está */ }
                }
                foreach (Grid g in new FilteredElementCollector(_doc, v.Id).OfClass(typeof(Grid)).Cast<Grid>())
                    foreach (Curve c in g.GetCurvesInView(DatumExtentType.ViewSpecific, v)) { pts.Add(c.GetEndPoint(0)); pts.Add(c.GetEndPoint(1)); }
                BoundingBoxXYZ cb = v.CropBox;
                Transform inv = cb.Transform.Inverse;
                List<XYZ> loc = pts.Select(inv.OfPoint).ToList();
                double f = U.Mm(_cfg.FolgaRecorteM * 1000);
                v.CropBoxActive = true;
                v.CropBox = new BoundingBoxXYZ
                {
                    Transform = cb.Transform,
                    Min = new XYZ(loc.Min(p => p.X) - f, loc.Min(p => p.Y) - f, cb.Min.Z),
                    Max = new XYZ(loc.Max(p => p.X) + f, loc.Max(p => p.Y) + f, cb.Max.Z),
                };
                v.CropBoxVisible = false;
            }
            catch (Exception ex) { _log.Warn($"Recorte da vista não ajustado: {ex.Message}"); }
        }

        private IEnumerable<Element> Estruturais(View v) =>
            new FilteredElementCollector(_doc, v.Id).WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>
            {
                BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Floors, BuiltInCategory.OST_StructuralFoundation
            })).WhereElementIsNotElementType().ToElements();

        // ------------------------------------------------------------------ anotações

        private double _centroR, _centroU;
        private List<(double MinR, double MaxR, double MinU, double MaxU)> _blocos;

        public void Anotar(ViewPlan v, Level nivel, TipoPlanta tipo)
        {
            _doc.Regenerate();
            EsconderMarcasAlheias(v);
            var cx = Estruturais(v).Select(e => Caixa(e, v)).Where(c => c != null).Select(c => c.Value).ToList();
            if (cx.Count > 0) { _centroR = (cx.Min(c => c.MinR) + cx.Max(c => c.MaxR)) / 2; _centroU = (cx.Min(c => c.MinU) + cx.Max(c => c.MaxU)) / 2; }
            CotasDeEixos(v);
            double z = nivel.ProjectElevation;

            var pilaresOk = new HashSet<ElementId>(ModeloFormas.Filtrar(_doc, BuiltInCategory.OST_StructuralColumns, _cfg.FiltroPilares, null, "pilar").Select(c => c.Id));
            var vigasOk = new HashSet<ElementId>(ModeloFormas.Filtrar(_doc, BuiltInCategory.OST_StructuralFraming, _cfg.FiltroVigas, null, "viga").Select(c => c.Id));
            List<FamilyInstance> pilares = Visiveis<FamilyInstance>(v, BuiltInCategory.OST_StructuralColumns)
                .Where(c => pilaresOk.Contains(c.Id) && ModeloFormas.Marca(c) != "").ToList();
            string papelPilar = tipo == TipoPlanta.Locacao ? "PilarLocacao" : "PilarMontagem";
            // locação: identificador do pilar à direita do BLOCO sob ele (não do pilar: caía em cima do contorno do bloco)
            _blocos = tipo != TipoPlanta.Locacao ? null : Visiveis<Element>(v, BuiltInCategory.OST_StructuralFoundation)
                .Select(f => Caixa(f, v)).Where(c => c != null).Select(c => c.Value).ToList();
            Identificar(v, papelPilar, BuiltInCategory.OST_StructuralColumnTags, pilares.Cast<Element>().ToList(), Lado.Direita, e =>
            {
                var c = Caixa(e, v);
                return c == null ? null : (Ponto(v, c.Value.MaxR + Afast(v), (c.Value.MinU + c.Value.MaxU) / 2, z), (double?)null);
            });

            if (tipo == TipoPlanta.Paginacao)
            {
                // painéis alveolares (quadro estrutural com marca L…) do nível: identificador no meio, ao longo do painel
                var paineisOk = new HashSet<ElementId>(ModeloFormas.Filtrar(_doc, BuiltInCategory.OST_StructuralFraming, _cfg.FiltroPaineis, null, "painel").Select(c => c.Id));
                List<FamilyInstance> paineis = Visiveis<FamilyInstance>(v, BuiltInCategory.OST_StructuralFraming)
                    .Where(b => paineisOk.Contains(b.Id) && ModeloFormas.Marca(b) != "")
                    .Where(b => { Level l = ModeloFormas.NivelDaViga(_doc, b); return l == null || l.Id == nivel.Id; }).ToList();
                Identificar(v, "Painel", BuiltInCategory.OST_StructuralFramingTags, paineis.Cast<Element>().ToList(), Lado.Centro, e =>
                {
                    var p = PosicaoViga((FamilyInstance)e, v, z);
                    if (p == null) return null;
                    var c = Caixa(e, v);
                    return c == null ? p : (Ponto(v, (c.Value.MinR + c.Value.MaxR) / 2, (c.Value.MinU + c.Value.MaxU) / 2, z), p.Value.Item2);
                });
                return;
            }

            if (tipo == TipoPlanta.Locacao)
            {
                List<Element> fundacoes = Visiveis<Element>(v, BuiltInCategory.OST_StructuralFoundation).Where(f => ModeloFormas.Marca(f) != "").ToList();
                Identificar(v, "Fundacao", BuiltInCategory.OST_StructuralFoundationTags, fundacoes, Lado.Abaixo, e =>
                {
                    var c = Caixa(e, v);
                    return c == null ? null : (Ponto(v, c.Value.MinR, c.Value.MinU - 2 * Afast(v), z), (double?)null);
                });
                return;
            }

            List<FamilyInstance> vigas = Visiveis<FamilyInstance>(v, BuiltInCategory.OST_StructuralFraming)
                .Where(b => vigasOk.Contains(b.Id) && ModeloFormas.Marca(b) != "")
                .Where(b => { Level l = ModeloFormas.NivelDaViga(_doc, b); return l == null || l.Id == nivel.Id; }).ToList();
            Identificar(v, "Viga", BuiltInCategory.OST_StructuralFramingTags, vigas.Cast<Element>().ToList(), Lado.Viga, e => PosicaoViga((FamilyInstance)e, v, z));

            List<Floor> lajes = Visiveis<Floor>(v, BuiltInCategory.OST_Floors).Where(f => f.LevelId == nivel.Id && ModeloFormas.Marca(f) != "").ToList();
            var doNivel = new FilteredElementCollector(_doc).OfClass(typeof(Floor)).Cast<Floor>().Where(f => f.LevelId == nivel.Id).ToList();
            int comMarca = doNivel.Count(f => ModeloFormas.Marca(f) != "");
            if (comMarca > lajes.Count || comMarca < doNivel.Count)
                _log.Warn($"Lajes do nível: {doNivel.Count} piso(s), {comMarca} com marca, {lajes.Count} visíveis na vista " +
                          "(sem marca não recebem identificador nem entram na tabela; marca não visível = faixa ou visibilidade da vista).");
            Identificar(v, "Laje", BuiltInCategory.OST_FloorTags, lajes.Cast<Element>().ToList(), Lado.Centro, e =>
            {
                var c = Caixa(e, v);
                return c == null ? null : (Ponto(v, (c.Value.MinR + c.Value.MaxR) / 2, (c.Value.MinU + c.Value.MaxU) / 2, z), (double?)null);
            });
            SentidoDoVao(v, lajes, z);
        }

        /// <summary>Marcas de corte e de elevação que não são do comando ficam escondidas nas vistas do comando (poluíam a planta).</summary>
        internal void EsconderMarcasAlheias(View v)
        {
            try
            {
                var nossos = new HashSet<string>(new FilteredElementCollector(_doc).OfClass(typeof(ViewSection)).Cast<ViewSection>()
                    .Where(s => MarcaFormas.Papel(s) != "").Select(s => s.Name));
                var ids = new FilteredElementCollector(_doc, v.Id).WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>
                    { BuiltInCategory.OST_Viewers, BuiltInCategory.OST_Elev, BuiltInCategory.OST_Callouts }))
                    .WhereElementIsNotElementType().Where(e => !nossos.Contains(e.Name) && e.CanBeHidden(v)).Select(e => e.Id).ToList();
                if (ids.Count > 0) { v.HideElements(ids); _log.Info($"{ids.Count} marca(s) de corte/elevação que não são do comando escondidas na vista."); }
            }
            catch (Exception ex) { _log.Warn($"Marcas de corte não escondidas: {ex.Message}"); }
        }

        private List<T> Visiveis<T>(View v, BuiltInCategory cat) where T : Element =>
            new FilteredElementCollector(_doc, v.Id).OfCategory(cat).WhereElementIsNotElementType().OfType<T>().ToList();

        private double Afast(View v) => U.Mm(_cfg.AfastamentoIdentificadorMm * v.Scale);

        /// <summary>Ponto do modelo a partir das coordenadas da vista (r ao longo da direita, u para cima) na cota z.</summary>
        private static XYZ Ponto(View v, double r, double u, double z) =>
            new XYZ(v.RightDirection.X * r + v.UpDirection.X * u, v.RightDirection.Y * r + v.UpDirection.Y * u, z);

        /// <summary>Caixa do elemento nas coordenadas da vista (cantos da caixa projetados na direita/para cima).</summary>
        internal static (double MinR, double MaxR, double MinU, double MaxU)? Caixa(Element e, View v)
        {
            return CaixaDe(e.get_BoundingBox(v) ?? e.get_BoundingBox(null), v);
        }

        /// <summary>Viga: no meio do eixo, acima da peça (lado "de cima" da vista; viga em pé = à esquerda), girado com a peça
        /// e sempre legível.</summary>
        private (XYZ, double?)? PosicaoViga(FamilyInstance b, View v, double z)
        {
            if (!(b.Location is LocationCurve lc) || !(lc.Curve is Line ln)) return null;
            XYZ d = ln.Direction;
            double dr = d.DotProduct(v.RightDirection), du = d.DotProduct(v.UpDirection);
            if (Math.Abs(dr) < 1e-9 && Math.Abs(du) < 1e-9) return null;   // viga em pé (vista de cima)
            double ang = Math.Atan2(du, dr);
            if (ang > Math.PI / 2 + 1e-6) ang -= Math.PI;
            if (ang <= -Math.PI / 2 + 1e-6) ang += Math.PI;
            // normal "para cima" na vista: (−sen, cos) do ângulo já legível
            double nr = -Math.Sin(ang), nu = Math.Cos(ang);
            XYZ mid = ln.Evaluate(0.5, true);
            double meia = (ModeloFormas.Medida(b, _cfg.ParametrosLargura, BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH) ?? 20) / 2;
            double off = U.Mm(meia * 10) + Afast(v);
            double r = mid.DotProduct(v.RightDirection) + nr * off, u = mid.DotProduct(v.UpDirection) + nu * off;
            return (Ponto(v, r, u, z), Math.Abs(ang) < 1e-6 ? (double?)null : ang);
        }

        // ------------------------------------------------------------------ cotas de eixos

        private void CotasDeEixos(ViewPlan v)
        {
            foreach (Element d in MarcaFormas.Com(new FilteredElementCollector(_doc, v.Id).OfClass(typeof(Dimension)), "cota-eixos"))
                _doc.Delete(d.Id);

            XYZ right = v.RightDirection, up = v.UpDirection;
            var verticais = new List<(Grid G, double Pos)>();
            var horizontais = new List<(Grid G, double Pos)>();
            int outros = 0;
            foreach (Grid g in new FilteredElementCollector(_doc, v.Id).OfClass(typeof(Grid)).Cast<Grid>())
            {
                if (!(g.Curve is Line l)) { outros++; continue; }
                if (Math.Abs(l.Direction.DotProduct(up)) > 0.9999) verticais.Add((g, l.Origin.DotProduct(right)));
                else if (Math.Abs(l.Direction.DotProduct(right)) > 0.9999) horizontais.Add((g, l.Origin.DotProduct(up)));
                else outros++;
            }
            if (outros > 0) _log.Warn($"{outros} eixo(s) inclinado(s) ou em arco sem cota (só eixos alinhados com a vista são cotados).");
            if (verticais.Count + horizontais.Count == 0) { _log.Warn("Nenhum eixo visível na vista: sem cotas de eixos."); return; }

            // estrutura em coordenadas da vista (sem peças: os próprios eixos)
            var caixas = Estruturais(v).Select(e => Caixa(e, v)).Where(c => c != null).Select(c => c.Value).ToList();
            double minR = caixas.Count > 0 ? caixas.Min(c => c.MinR) : verticais.Select(x => x.Pos).DefaultIfEmpty(0).Min();
            double maxU = caixas.Count > 0 ? caixas.Max(c => c.MaxU) : horizontais.Select(x => x.Pos).DefaultIfEmpty(0).Max();
            double afast = U.Mm(_cfg.AfastamentoCotasMm * v.Scale);
            double z = v.GenLevel?.ProjectElevation ?? 0;
            DimensionType tipo = TipoDeCota();
            int n = 0;
            double passo = U.Mm(8 * v.Scale);   // 8 mm de papel entre a cadeia e a cota total
            n += Cota(v, verticais, p => Ponto(v, p, maxU + afast, z), tipo, p => Ponto(v, p, maxU + afast + passo, z));
            n += Cota(v, horizontais, p => Ponto(v, minR - afast, p, z), tipo, p => Ponto(v, minR - afast - passo, p, z));
            _log.Info($"Cotas de eixos: {n} linha(s) ({verticais.Count} eixo(s) verticais, {horizontais.Count} horizontais).");
        }

        private int Cota(ViewPlan v, List<(Grid G, double Pos)> eixos, Func<double, XYZ> ponto, DimensionType tipo, Func<double, XYZ> afastTotal = null)
        {
            var ord = new List<(Grid G, double Pos)>();
            foreach (var e in eixos.OrderBy(x => x.Pos))
                if (ord.Count == 0 || e.Pos - ord[ord.Count - 1].Pos > U.Mm(1)) ord.Add(e);   // eixos sobrepostos: um só
            if (ord.Count < 2) return 0;
            var ra = new ReferenceArray();
            foreach (var e in ord) ra.Append(new Reference(e.G));
            try
            {
                Line linha = Line.CreateBound(ponto(ord[0].Pos), ponto(ord[ord.Count - 1].Pos));
                Dimension d = tipo != null ? _doc.Create.NewDimension(v, linha, ra, tipo) : _doc.Create.NewDimension(v, linha, ra);
                MarcaFormas.Marcar(d, "cota-eixos");
                if (ord.Count > 2 && afastTotal != null)
                {
                    // cota total, por fora da cadeia
                    var rt = new ReferenceArray();
                    rt.Append(new Reference(ord[0].G));
                    rt.Append(new Reference(ord[ord.Count - 1].G));
                    Line lt = Line.CreateBound(afastTotal(ord[0].Pos), afastTotal(ord[ord.Count - 1].Pos));
                    Dimension dt = tipo != null ? _doc.Create.NewDimension(v, lt, rt, tipo) : _doc.Create.NewDimension(v, lt, rt);
                    MarcaFormas.Marcar(dt, "cota-eixos");
                }
                return 1;
            }
            catch (Exception ex) { _log.Warn($"Cota de eixos não criada: {ex.Message}"); return 0; }
        }

        private DimensionType TipoDeCota()
        {
            List<DimensionType> lin = new FilteredElementCollector(_doc).OfClass(typeof(DimensionType)).Cast<DimensionType>()
                .Where(t => t.StyleType == DimensionStyleType.Linear).ToList();
            string nome = _cfg.TipoDeCota?.Trim() ?? "";
            DimensionType t = lin.FirstOrDefault(x => string.Equals(x.Name, nome, StringComparison.OrdinalIgnoreCase));
            if (t != null) return t;
            if (nome != "") _log.Warn($"Tipo de cota \"{nome}\" não existe no projeto; usado \"{NomeCotaEixos}\".");
            // tipo próprio em cm inteiros: o das unidades do projeto pode ter 6 casas ("645.000000")
            t = lin.FirstOrDefault(x => x.Name == NomeCotaEixos);
            if (t != null) { try { t.get_Parameter(BuiltInParameter.LINE_COLOR)?.Set(0); } catch { } return t; }
            DimensionType fonte = lin.Where(x => x.GetSpecTypeId() == SpecTypeId.Length).OrderBy(x => x.Name).FirstOrDefault() ?? lin.FirstOrDefault();
            if (fonte == null) return null;
            try
            {
                t = (DimensionType)fonte.Duplicate(NomeCotaEixos);
                t.SetUnitsFormatOptions(new FormatOptions(UnitTypeId.Centimeters) { UseDefault = false, Accuracy = 1, SuppressTrailingZeros = true });
                t.get_Parameter(BuiltInParameter.TEXT_SIZE)?.Set(U.Mm(_cfg.AlturaTextoCotaMm));
                t.get_Parameter(BuiltInParameter.LINE_COLOR)?.Set(0);   // preto
                return t;
            }
            catch (Exception ex) { _log.Warn($"Tipo de cota próprio não criado ({ex.Message}); usado o padrão do Revit."); return null; }
        }

        public const string NomeCotaEixos = "PCEB - Cota eixos (cm)";

        // ------------------------------------------------------------------ identificadores

        /// <summary>Tipo de identificador do papel: família/tipo do padrão; carrega do arquivo se faltar; senão qualquer um da
        /// categoria (com aviso). null = nenhum identificador da categoria no projeto.</summary>
        private FamilySymbol TipoIdentificador(string papel, BuiltInCategory cat)
        {
            if (_tipos.TryGetValue(papel, out FamilySymbol cached)) return cached;
            TagRoleSpec r = _cfg.Role(papel);
            string fam = r.Familia?.Trim() ?? "", tipo = r.Tipo?.Trim() ?? "";
            List<FamilySymbol> Todos() => new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)).OfCategory(cat).Cast<FamilySymbol>()
                .OrderBy(s => s.FamilyName).ThenBy(s => s.Name).ToList();
            bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            List<FamilySymbol> todos = Todos();
            FamilySymbol s = todos.FirstOrDefault(x => fam != "" && Eq(x.FamilyName, fam) && (tipo == "" || Eq(x.Name, tipo)));
            // família do padrão com arquivo: carregada SUBSTITUINDO a do projeto com o mesmo nome (o usuário adapta a família na
            // biblioteca e o modelo pode ter a versão antiga), uma vez por arquivo e por documento na sessão
            if (fam != "" && !string.IsNullOrWhiteSpace(r.Arquivo) && File.Exists(r.Arquivo.Trim()) && todos.Any(x => Eq(x.FamilyName, fam)) &&
                Recarregadas.Add(_doc.GetHashCode() + "|" + r.Arquivo.Trim().ToLowerInvariant()))
            {
                try
                {
                    if (_doc.LoadFamily(r.Arquivo.Trim(), new SubstituirFamilia(), out Family _)) _log.Info($"Família \"{fam}\" atualizada de {r.Arquivo.Trim()}.");
                    _doc.Regenerate();
                    todos = Todos();
                    s = todos.FirstOrDefault(x => fam != "" && Eq(x.FamilyName, fam) && (tipo == "" || Eq(x.Name, tipo)));
                }
                catch (Exception ex) { _log.Warn($"Identificador {papel}: família não atualizada de {r.Arquivo} ({ex.Message})."); }
            }
            if (s == null && fam != "" && !todos.Any(x => Eq(x.FamilyName, fam)) && !string.IsNullOrWhiteSpace(r.Arquivo))
            {
                string arq = r.Arquivo.Trim();
                if (!File.Exists(arq)) _log.Warn($"Identificador {papel}: arquivo não encontrado ({arq}).");
                else
                {
                    try
                    {
                        if (_doc.LoadFamily(arq, new SheetComposer.OverwriteNothing(), out Family _)) _log.Info($"Família \"{fam}\" carregada de {arq}.");
                        todos = Todos();
                        s = todos.FirstOrDefault(x => Eq(x.FamilyName, fam) && (tipo == "" || Eq(x.Name, tipo)));
                    }
                    catch (Exception ex) { _log.Warn($"Identificador {papel}: família não carregada de {arq} ({ex.Message})."); }
                }
            }
            if (s == null && fam != "")
            {
                s = todos.FirstOrDefault(x => Eq(x.FamilyName, fam));
                if (s != null) _log.Warn($"Identificador {papel}: tipo \"{tipo}\" não existe na família \"{fam}\"; usado \"{s.Name}\".");
            }
            if (s == null)
            {
                s = todos.FirstOrDefault();
                if (s != null && fam != "") _log.Warn($"Identificador {papel}: família \"{fam}\" não está no projeto; usado \"{s.FamilyName} : {s.Name}\" (sem os rótulos SD_ o texto pode sair incompleto).");
                else if (s != null) _log.Info($"Identificador {papel}: sem família no padrão; usado \"{s.FamilyName} : {s.Name}\".");
                else _log.Warn($"Identificador {papel}: nenhuma família de identificador desta categoria no projeto — peças sem identificador. Carregue a família (ou indique o arquivo no padrão).");
            }
            if (s != null && papel == "Fundacao" && _cfg.FundacoesEsquematicas) s = TipoEsquematico(s);
            if (s != null && !s.IsActive) { s.Activate(); _doc.Regenerate(); }
            _tipos[papel] = s;
            return s;
        }

        /// <summary>
        /// Fundações esquemáticas: cópia do tipo do identificador ("… (esquemática)") com os parâmetros Sim/Não de TIPO ligados só nos
        /// nomes de <see cref="FormasSettings.VisiveisEsquematica"/> (ex.: "N" = só o nome do bloco; dimensões e níveis apagados).
        /// </summary>
        private FamilySymbol TipoEsquematico(FamilySymbol s)
        {
            string nome = s.Name + " (esquemática)";
            FamilySymbol e = new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .FirstOrDefault(x => x.Family.Id == s.Family.Id && x.Name == nome);
            if (e != null) return e;
            try
            {
                e = (FamilySymbol)s.Duplicate(nome);
                var vis = new HashSet<string>(_cfg.VisiveisEsquematica ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                int n = 0;
                foreach (Parameter p in e.Parameters.Cast<Parameter>())
                    if (!p.IsReadOnly && p.StorageType == StorageType.Integer && p.Definition.GetDataType() == SpecTypeId.Boolean.YesNo)
                    { p.Set(vis.Contains(p.Definition.Name) ? 1 : 0); n++; }
                _log.Info($"Fundações esquemáticas: tipo \"{nome}\" criado ({n} opção(ões) de visibilidade; ligadas: {string.Join(", ", vis)}).");
                return e;
            }
            catch (Exception ex) { _log.Warn($"Tipo esquemático do identificador de fundação não criado ({ex.Message}); usado o normal."); return s; }
        }

        internal void Identificar(View v, string papel, BuiltInCategory tagCat, List<Element> alvos, Lado lado, Func<Element, (XYZ, double?)?> posicao)
        {
            string marca = "id-" + papel;
            var ids = new HashSet<ElementId>(alvos.Select(e => e.Id));
            var tags = new FilteredElementCollector(_doc, v.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().ToList();
            var meus = new Dictionary<ElementId, IndependentTag>();
            int apagados = 0;
            foreach (IndependentTag t in tags.Where(t => MarcaFormas.Papel(t) == marca))
            {
                ElementId alvo = t.GetTaggedLocalElementIds().FirstOrDefault() ?? ElementId.InvalidElementId;
                if (alvo == ElementId.InvalidElementId || !ids.Contains(alvo) || meus.ContainsKey(alvo)) { _doc.Delete(t.Id); apagados++; }
                else meus[alvo] = t;
            }
            FamilySymbol sym = TipoIdentificador(papel, tagCat);
            if (sym == null) return;
            // identificadores feitos à mão nesta vista: a peça não ganha outro
            var aMao = new HashSet<ElementId>(tags.Where(t => MarcaFormas.Papel(t) == "" && t.IsValidObject && t.Category?.Id.Value == (long)tagCat)
                .SelectMany(t => t.GetTaggedLocalElementIds()));
            int criados = 0, mantidos = 0, manuais = 0, falhas = 0;
            string erro = null;
            var novos = new List<(IndependentTag Tag, Element Alvo)>();
            foreach (Element e in alvos)
            {
                if (meus.TryGetValue(e.Id, out IndependentTag t))
                {
                    if (t.GetTypeId() != sym.Id) try { t.ChangeTypeId(sym.Id); } catch { /* tipo de outra família */ }
                    mantidos++;
                    continue;
                }
                if (aMao.Contains(e.Id)) { manuais++; continue; }
                var pos = posicao(e);
                if (pos == null) { falhas++; continue; }
                try
                {
                    var (pt, ang) = pos.Value;
                    IndependentTag nt = IndependentTag.Create(_doc, sym.Id, v.Id, new Reference(e), false,
                        ang == null ? TagOrientation.Horizontal : TagOrientation.AnyModelDirection, pt);
                    if (ang != null) try { nt.RotationAngle = ang.Value; } catch { /* família que não gira */ }
                    MarcaFormas.Marcar(nt, marca);
                    novos.Add((nt, e));
                    criados++;
                }
                catch (Exception ex) { falhas++; erro ??= ex.Message; }
            }
            Ajustar(v, novos, lado, _cfg.Role(papel));
            _log.Info($"Identificadores {papel}: {criados} criado(s), {mantidos} mantido(s), {apagados} apagado(s)" +
                      (manuais > 0 ? $", {manuais} peça(s) já identificadas à mão" : "") + ".");
            if (falhas > 0) _log.Warn($"Identificadores {papel}: {falhas} não criados{(erro != null ? $" ({erro})" : "")}.");
        }

        /// <summary>Onde o identificador fica em relação à peça.</summary>
        internal enum Lado { Direita, Abaixo, Centro, Viga, Acima }

        /// <summary>
        /// Acerta a posição dos identificadores novos pela caixa REAL do texto (a origem de cada família é diferente: "Alinhamento dir"
        /// cresce para a esquerda, outras para a direita): pilar = texto à direita da peça, centrado na altura; fundação = abaixo,
        /// alinhado à esquerda; laje = centrado; viga = acima da peça (viga mais em pé que deitada: à esquerda). Uma regeneração só.
        /// </summary>
        private void Ajustar(View v, List<(IndependentTag Tag, Element Alvo)> novos, Lado lado, TagRoleSpec papel)
        {
            double exR = U.Mm(papel.DeslocamentoXMm * v.Scale), exU = U.Mm(papel.DeslocamentoYMm * v.Scale);
            if (novos.Count == 0) return;
            try { _doc.Regenerate(); } catch { return; }
            double off = Afast(v);
            foreach (var (t, e) in novos)
            {
                try
                {
                    var c = Caixa(e, v);
                    var tb = CaixaDe(t.get_BoundingBox(v), v);
                    if (c == null || tb == null) continue;
                    var (a, b) = (c.Value, tb.Value);
                    double cr = (a.MinR + a.MaxR) / 2, cu = (a.MinU + a.MaxU) / 2, tr = (b.MinR + b.MaxR) / 2, tu = (b.MinU + b.MaxU) / 2;
                    double dr, du;
                    switch (lado)
                    {
                        case Lado.Direita:
                            double maxR = a.MaxR;
                            foreach (var bl in _blocos ?? Enumerable.Empty<(double MinR, double MaxR, double MinU, double MaxU)>())
                                if (cr >= bl.MinR && cr <= bl.MaxR && cu >= bl.MinU && cu <= bl.MaxU) maxR = Math.Max(maxR, bl.MaxR);
                            dr = maxR + off - b.MinR; du = cu - tu; break;
                        case Lado.Abaixo: dr = a.MinR - b.MinR; du = a.MinU - off - b.MaxU; break;
                        case Lado.Centro: dr = cr - tr; du = cu - tu; break;
                        case Lado.Acima: dr = cr - tr; du = a.MaxU + off - b.MinU; break;
                        default:
                            // viga: do lado de FORA em relação ao centro da estrutura (as de borda não colam na linha de eixo
                            // nem nas peças de dentro); afastamento extra de 1 altura de texto
                            bool deitada = a.MaxR - a.MinR >= a.MaxU - a.MinU;
                            double o2 = off * 2;
                            if (deitada) { dr = cr - tr; du = cu >= _centroU ? a.MaxU + o2 - b.MinU : a.MinU - o2 - b.MaxU; }
                            else { du = cu - tu; dr = cr < _centroR ? a.MinR - o2 - b.MaxR : a.MaxR + o2 - b.MinR; }
                            break;
                    }
                    dr += exR; du += exU;
                    if (Math.Abs(dr) < 1e-6 && Math.Abs(du) < 1e-6) continue;
                    t.TagHeadPosition = t.TagHeadPosition + v.RightDirection * dr + v.UpDirection * du;
                }
                catch { /* fica onde foi criado */ }
            }
        }

        /// <summary>Caixa nas coordenadas da vista: os 8 cantos projetados na direita/para cima (serve para plantas e cortes).</summary>
        internal static (double MinR, double MaxR, double MinU, double MaxU)? CaixaDe(BoundingBoxXYZ bb, View v)
        {
            if (bb == null) return null;
            var rs = new List<double>(); var us = new List<double>();
            foreach (double x in new[] { bb.Min.X, bb.Max.X })
                foreach (double y in new[] { bb.Min.Y, bb.Max.Y })
                    foreach (double zz in new[] { bb.Min.Z, bb.Max.Z })
                    {
                        var p = new XYZ(x, y, zz);
                        rs.Add(p.DotProduct(v.RightDirection)); us.Add(p.DotProduct(v.UpDirection));
                    }
            return (rs.Min(), rs.Max(), us.Min(), us.Max());
        }

        // ------------------------------------------------------------------ sentido do vão

        private void SentidoDoVao(ViewPlan v, List<Floor> lajes, double z)
        {
            if (!_cfg.ColocarSentidoDoVao || lajes.Count == 0) return;
            List<FamilySymbol> tipos = new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_SpanDirectionSymbol)
                .Cast<FamilySymbol>().OrderBy(s => s.FamilyName).ThenBy(s => s.Name).ToList();
            if (tipos.Count == 0) { _log.Info("Sentido do vão: nenhum tipo de símbolo carregado no projeto (não colocado)."); return; }
            string want = _cfg.SentidoDoVao?.Trim() ?? "";
            FamilySymbol s = tipos.FirstOrDefault(t => want != "" && (string.Equals(t.FamilyName + " : " + t.Name, want, StringComparison.OrdinalIgnoreCase) ||
                                                                      string.Equals(t.FamilyName, want, StringComparison.OrdinalIgnoreCase)));
            if (s == null && want != "") _log.Warn($"Sentido do vão: tipo \"{want}\" não carregado; usado \"{tipos[0].FamilyName} : {tipos[0].Name}\".");
            s ??= tipos[0];
            if (!s.IsActive) { s.Activate(); _doc.Regenerate(); }
            int n = 0, falhas = 0;
            foreach (Floor f in lajes)
            {
                try
                {
                    if (f.GetSpanDirectionSymbolIds().Any(id => _doc.GetElement(id)?.OwnerViewId == v.Id)) continue;
                    var c = Caixa(f, v);
                    if (c == null) continue;
                    // abaixo do identificador da laje (que fica no centro)
                    XYZ p = Ponto(v, (c.Value.MinR + c.Value.MaxR) / 2, (c.Value.MinU + c.Value.MaxU) / 2 - 4 * Afast(v), z);
                    SpanDirectionSymbol sd = SpanDirectionSymbol.Create(_doc, v.Id, new LinkElementId(f.Id), p, s.Id);
                    MarcaFormas.Marcar(sd, "sentido-vao");
                    n++;
                }
                catch { falhas++; }
            }
            _log.Info($"Sentido do vão: {n} símbolo(s) criado(s)" + (falhas > 0 ? $", {falhas} laje(s) não aceitaram" : "") + ".");
        }
    }
}
