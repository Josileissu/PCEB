using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using DEB.Revit.Core;

namespace DEB.Revit.Presentation
{
    /// <summary>
    /// Legendas dos detalhes padrão (raio de dobra, emendas, chaves de cisalhamento, alça, aterramento...).
    /// Se a legenda pedida pelo padrão não existe no projeto e o padrão aponta um DXF (pasta Padroes\Detalhes),
    /// ela é criada uma vez, com elementos nativos do Revit (linhas de detalhe, textos e regiões preenchidas), e
    /// fica salva no projeto — o escritório pode editá-la à vontade; o plugin não a refaz.
    /// A API do Revit não cria legendas do zero: a nova é duplicada de qualquer legenda existente.
    /// </summary>
    internal sealed class LegendFactory
    {
        public static string Folder => Path.Combine(PresentationStandard.Folder, "Detalhes");

        private readonly Document _doc;
        private readonly AnnotationStyles _styles;
        private readonly PieceLog _log;
        private readonly Dictionary<string, GraphicsStyle> _lineStyles = new Dictionary<string, GraphicsStyle>();
        private readonly Dictionary<string, TextNoteType> _textTypes = new Dictionary<string, TextNoteType>();
        private ElementId _solidType;

        private readonly string _baseFile;

        public LegendFactory(Document doc, AnnotationStyles styles, PieceLog log, string baseFile)
        {
            _doc = doc; _styles = styles; _log = log; _baseFile = baseFile?.Trim();
        }

        public static View Find(Document doc, string name) =>
            new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .FirstOrDefault(v => v.ViewType == ViewType.Legend && !v.IsTemplate &&
                                     string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

        public static string PathOf(LegendSpec ls) =>
            string.IsNullOrWhiteSpace(ls.Arquivo) ? null : Path.IsPathRooted(ls.Arquivo) ? ls.Arquivo : Path.Combine(Folder, ls.Arquivo);

        /// <summary>Cria a legenda a partir do DXF do padrão. Precisa de transação. Devolve null (com aviso) se não der.</summary>
        public View Create(LegendSpec ls)
        {
            string path = PathOf(ls);
            if (path == null)
            {
                _log.Warn($"Legenda \"{ls.Nome}\" não existe no projeto (crie-a em Vistas → Legendas, ou indique o DXF do detalhe no padrão).");
                return null;
            }
            if (!File.Exists(path))
            {
                _log.Warn($"Legenda \"{ls.Nome}\": arquivo do detalhe não encontrado ({path}).");
                return null;
            }
            View source = BaseLegend();
            if (source == null)
            {
                if (!_noBaseWarned)
                    _log.Warn("Legendas dos detalhes padrão não criadas: o projeto não tem nenhuma legenda para servir de base (a API do " +
                              "Revit só cria legendas duplicando outra). Crie uma legenda vazia em Vista → Legendas (uma vez, de preferência " +
                              "no template) ou indique em \"ArquivoLegendaBase\" do padrão um .rte/.rvt que tenha uma.");
                _noBaseWarned = true;
                return null;
            }

            DxfDetail dxf;
            try { dxf = DxfDetail.Read(path); }
            catch (Exception ex) { _log.Warn($"Legenda \"{ls.Nome}\": DXF não lido ({ex.Message})."); _log.Trace("Legenda DXF", ex); return null; }

            _layerColor = dxf.LayerColors;
            var view = (View)_doc.GetElement(source.Duplicate(ViewDuplicateOption.Duplicate));
            view.Name = NameRegistry.ForViews(_doc).Unique(ls.Nome);
            int scale = ls.Escala > 0 ? ls.Escala : view.Scale;
            try { view.Scale = scale; } catch { scale = view.Scale; }

            int made = 0, failed = 0;
            foreach (DxfDetail.Prim p in dxf.Items)
            {
                try { if (Draw(view, p, scale)) made++; }
                catch (Exception ex) { failed++; if (failed == 1) _log.Trace($"Legenda {ls.Nome}", ex); }
            }
            string ign = dxf.Ignored.Count == 0 ? "" : "; ignorados: " + string.Join(", ", dxf.Ignored.Select(kv => $"{kv.Value} {kv.Key}"));
            _log.Info($"Legenda \"{view.Name}\" criada de {Path.GetFileName(path)} em 1:{scale}: {made} elementos" +
                      (failed > 0 ? $", {failed} falharam" : "") + ign + ".");
            return view;
        }

        // uma tentativa por arquivo e por projeto na sessão (cada prancha cria a sua fábrica)
        private static readonly HashSet<string> BaseTried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _noBaseWarned;

        /// <summary>Legenda do projeto para duplicar; se não houver, copia uma do ArquivoLegendaBase (uma tentativa por prancha).</summary>
        private View BaseLegend()
        {
            View Pick() => new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => v.ViewType == ViewType.Legend && !v.IsTemplate && v.CanViewBeDuplicated(ViewDuplicateOption.Duplicate))
                .OrderBy(v => v.Name).FirstOrDefault();
            View v0 = Pick();
            if (v0 != null || string.IsNullOrWhiteSpace(_baseFile)) return v0;
            if (!BaseTried.Add(_doc.Title + "|" + _baseFile)) { _noBaseWarned = true; return null; }
            if (!File.Exists(_baseFile)) { _log.Warn($"Arquivo da legenda base não encontrado: {_baseFile}"); return null; }

            Document src = null;
            bool opened = false;
            try
            {
                src = _doc.Application.Documents.Cast<Document>()
                    .FirstOrDefault(d => string.Equals(d.PathName, _baseFile, StringComparison.OrdinalIgnoreCase));
                if (src == null) { src = OpenQuietly(_doc.Application, _baseFile); opened = true; }
                View legend = new FilteredElementCollector(src).OfClass(typeof(View)).Cast<View>()
                    .Where(v => v.ViewType == ViewType.Legend && !v.IsTemplate)
                    .OrderBy(v => new FilteredElementCollector(src, v.Id).GetElementCount()).FirstOrDefault();
                if (legend == null) { _log.Warn($"{Path.GetFileName(_baseFile)} não tem legendas."); return null; }
                ICollection<ElementId> ids = ElementTransformUtils.CopyElements(src, new List<ElementId> { legend.Id }, _doc,
                    Transform.Identity, new CopyPasteOptions());
                View copy = ids.Select(id => _doc.GetElement(id)).OfType<View>().FirstOrDefault(v => v.ViewType == ViewType.Legend);
                _log.Info($"Legenda base \"{legend.Name}\" copiada de {Path.GetFileName(_baseFile)}" + (copy == null ? " (falhou)." : " como \"PCEB - Legenda base\"."));
                if (copy != null) try { copy.Name = NameRegistry.ForViews(_doc).Unique("PCEB - Legenda base"); } catch { /* fica o nome original */ }
                return copy ?? Pick();
            }
            catch (Exception ex)
            {
                _log.Warn($"Legenda base não copiada de {Path.GetFileName(_baseFile)}: {ex.Message}");
                _log.Trace("Legenda base", ex);
                return null;
            }
            finally
            {
                if (opened) try { src.Close(false); } catch { /* fecha quando der */ }
            }
        }

        /// <summary>Abre um arquivo descartando os avisos (ex.: os da atualização de versão), que senão param numa janela.</summary>
        public static Document OpenQuietly(Autodesk.Revit.ApplicationServices.Application app, string path)
        {
            void Quiet(object s, Autodesk.Revit.DB.Events.FailuresProcessingEventArgs e)
            {
                FailuresAccessor fa = e.GetFailuresAccessor();
                if (fa.GetFailureMessages().All(m => m.GetSeverity() == FailureSeverity.Warning))
                {
                    fa.DeleteAllWarnings();
                    e.SetProcessingResult(FailureProcessingResult.Continue);
                }
            }
            app.FailuresProcessing += Quiet;
            try { return app.OpenDocumentFile(path); }
            finally { app.FailuresProcessing -= Quiet; }
        }

        // ------------------------------------------------------------------ níveis da obra

        /// <summary>
        /// Legenda "PCEB - Níveis da obra - escala 1-N": para cada nível do projeto, linha traço-ponto com o nome e a cota em cima,
        /// em y = cota do nível (na escala da legenda), para a folha alinhar com as elevações. Uma por escala; refeita quando
        /// os níveis mudam (assinatura na descrição da vista). Precisa de transação. Null (com aviso) sem legenda base.
        /// </summary>
        public View LevelsLegend(int scale)
        {
            List<Level> levels = new FilteredElementCollector(_doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ToList();
            if (levels.Count == 0) return null;
            var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
            string Elev(double ft)
            {
                double m = Math.Round(UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Meters), 2);
                return m == 0 ? "0,00" : m.ToString("+0.00;-0.00", pt);
            }
            string sign = string.Join(";", levels.Select(l => l.Name + "=" + Elev(l.Elevation)));
            string name = $"PCEB - Níveis da obra - escala 1-{scale}";   // ":" não é aceito em nome de vista
            View view = Find(_doc, name);
            if (view != null)
            {
                if (view.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION)?.AsString() == sign) return view;
                // níveis mudaram: apaga o desenho (a vista fica, com os viewports das folhas) e redesenha
                List<ElementId> old = new FilteredElementCollector(_doc, view.Id).WhereElementIsNotElementType()
                    .Where(e => e is DetailCurve || e is TextNote).Select(e => e.Id).ToList();
                if (old.Count > 0) _doc.Delete(old);
                _log.Info($"Legenda \"{name}\": níveis do projeto mudaram, redesenhada.");
            }
            else
            {
                View source = BaseLegend();
                if (source == null)
                {
                    _log.Warn("Legenda de níveis não criada: o projeto não tem nenhuma legenda para servir de base (crie uma legenda " +
                              "vazia em Vista → Legendas ou indique \"ArquivoLegendaBase\" no padrão).");
                    return null;
                }
                view = (View)_doc.GetElement(source.Duplicate(ViewDuplicateOption.Duplicate));
                view.Name = NameRegistry.ForViews(_doc).Unique(name);
                view.Scale = scale;
                _log.Info($"Legenda \"{view.Name}\" criada ({levels.Count} níveis).");
            }
            view.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION)?.Set(sign);

            double s = scale, len = U.Mm(32) * s;
            double textH = (_styles.Text.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? U.Mm(2.5)) * s;
            var opt = new TextNoteOptions(_styles.Text.Id) { HorizontalAlignment = HorizontalTextAlignment.Left, VerticalAlignment = VerticalTextAlignment.Bottom };
            GraphicsStyle dashDot = null;
            foreach (Level l in levels)
            {
                double y = l.Elevation;
                DetailCurve dc = _doc.Create.NewDetailCurve(view, Line.CreateBound(new XYZ(-len, y, 0), new XYZ(0, y, 0)));
                dashDot ??= DashDotStyle(dc);
                if (dashDot != null) dc.LineStyle = dashDot;
                TextNote.Create(_doc, view.Id, new XYZ(-len, y + textH * 0.6, 0), Elev(l.Elevation), opt);
                TextNote.Create(_doc, view.Id, new XYZ(-len, y + textH * 2.4, 0), l.Name.ToUpperInvariant(), opt);
            }
            return view;
        }

        /// <summary>Estilo traço-ponto (Centro/Center) das linhas; senão o padrão.</summary>
        private GraphicsStyle DashDotStyle(DetailCurve dc)
        {
            List<GraphicsStyle> all = dc.GetLineStyleIds().Select(id => _doc.GetElement(id)).OfType<GraphicsStyle>().ToList();
            bool Has(GraphicsStyle g, params string[] k) => k.Any(x => g.Name.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
            return all.FirstOrDefault(g => Has(g, "traço-ponto", "traco-ponto", "dash dot", "dash-dot"))
                   ?? all.FirstOrDefault(g => Has(g, "centro", "center"));
        }

        // ------------------------------------------------------------------ desenho

        private static readonly double Cm = U.Mm(10);

        private static XYZ P((double X, double Y) p) => new XYZ(p.X * Cm, p.Y * Cm, 0);

        private bool Draw(View view, DxfDetail.Prim p, int scale)
        {
            double tol = _doc.Application.ShortCurveTolerance;
            switch (p.Kind)
            {
                case DxfDetail.Kind.Line:
                {
                    XYZ a = P(p.Pts[0]), b = P(p.Pts[1]);
                    if (a.DistanceTo(b) < tol) return false;
                    return Styled(_doc.Create.NewDetailCurve(view, Line.CreateBound(a, b)), p);
                }
                case DxfDetail.Kind.Arc:
                case DxfDetail.Kind.Circle:
                {
                    double r = p.R * Cm;
                    if (r < tol) return false;
                    double a0 = p.Kind == DxfDetail.Kind.Circle ? 0 : p.A0;
                    double a1 = p.Kind == DxfDetail.Kind.Circle ? 2 * Math.PI : p.A1;
                    while (a1 <= a0) a1 += 2 * Math.PI;
                    if ((a1 - a0) * r < tol) return false;
                    XYZ c = P(p.Pts[0]);
                    if (p.Kind == DxfDetail.Kind.Circle)
                    {
                        // círculo = dois semicírculos (curva fechada não é aceita em toda versão)
                        Styled(_doc.Create.NewDetailCurve(view, Arc.Create(c, r, 0, Math.PI, XYZ.BasisX, XYZ.BasisY)), p);
                        return Styled(_doc.Create.NewDetailCurve(view, Arc.Create(c, r, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY)), p);
                    }
                    return Styled(_doc.Create.NewDetailCurve(view, Arc.Create(c, r, a0, a1, XYZ.BasisX, XYZ.BasisY)), p);
                }
                case DxfDetail.Kind.Fill:
                    return Fill(view, p, tol);
                case DxfDetail.Kind.Text:
                    return Text(view, p, scale);
            }
            return false;
        }

        private bool Styled(DetailCurve dc, DxfDetail.Prim p)
        {
            GraphicsStyle gs = LineStyle(p);
            if (gs != null) dc.LineStyle = gs;
            return true;
        }

        /// <summary>Estilos de linha "PCEB - ..." (subcategorias de Linhas), criados uma vez: o escritório ajusta espessura e cor.</summary>
        private GraphicsStyle LineStyle(DxfDetail.Prim p)
        {
            string lay = p.Layer.ToUpperInvariant();
            int color = p.Color == 256 && _layerColor != null && _layerColor.TryGetValue(p.Layer, out int lc) ? lc : p.Color;
            string name; int weight; bool dashed = false;
            if (lay.Contains("ARMADURA") || lay.Contains("BARRA") || color == 1) { name = "PCEB - Detalhe armadura"; weight = 3; }
            else if (lay.Contains("CONTORNO") || lay.Contains("FORMA")) { name = "PCEB - Detalhe contorno"; weight = 2; }
            else if (lay.Contains("TRACEJ") || lay.Contains("HIDDEN") || lay.Contains("OCULT")) { name = "PCEB - Detalhe tracejada"; weight = 1; dashed = true; }
            else { name = "PCEB - Detalhe fina"; weight = 1; }
            if (_lineStyles.TryGetValue(name, out GraphicsStyle gs)) return gs;

            Category lines = _doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            Category sub = lines.SubCategories.Cast<Category>().FirstOrDefault(c => c.Name == name);
            if (sub == null)
            {
                sub = _doc.Settings.Categories.NewSubcategory(lines, name);
                sub.SetLineWeight(weight, GraphicsStyleType.Projection);
                sub.LineColor = new Color(0, 0, 0);
                if (dashed)
                {
                    ElementId pat = DashedPattern();
                    if (pat != null) sub.SetLinePatternId(pat, GraphicsStyleType.Projection);
                }
            }
            gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
            _lineStyles[name] = gs;
            return gs;
        }

        private Dictionary<string, int> _layerColor;

        private ElementId DashedPattern()
        {
            LinePatternElement found = new FilteredElementCollector(_doc).OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>()
                .OrderBy(e => e.Name)
                .FirstOrDefault(e => new[] { "tracej", "dash", "hidden", "oculta" }.Any(k => e.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                                     && e.Name.IndexOf("dot", StringComparison.OrdinalIgnoreCase) < 0
                                     && e.Name.IndexOf("ponto", StringComparison.OrdinalIgnoreCase) < 0);
            if (found != null) return found.Id;
            try
            {
                var lp = new LinePattern("PCEB - Tracejada");
                lp.SetSegments(new List<LinePatternSegment>
                {
                    new LinePatternSegment(LinePatternSegmentType.Dash, U.Mm(3)),
                    new LinePatternSegment(LinePatternSegmentType.Space, U.Mm(1.5))
                });
                return LinePatternElement.Create(_doc, lp).Id;
            }
            catch { return null; }
        }

        /// <summary>Região preenchida sólida (setas, soldas). Polígonos degenerados são ignorados.</summary>
        private bool Fill(View view, DxfDetail.Prim p, double tol)
        {
            var pts = new List<XYZ>();
            foreach (var q in p.Pts)
            {
                XYZ x = P(q);
                if (pts.Count == 0 || pts[^1].DistanceTo(x) >= tol) pts.Add(x);
            }
            while (pts.Count > 1 && pts[0].DistanceTo(pts[^1]) < tol) pts.RemoveAt(pts.Count - 1);
            if (pts.Count < 3) return false;
            var loop = new CurveLoop();
            for (int i = 0; i < pts.Count; i++) loop.Append(Line.CreateBound(pts[i], pts[(i + 1) % pts.Count]));
            _solidType ??= SolidFillType();
            if (_solidType == null) return false;
            FilledRegion.Create(_doc, _solidType, view.Id, new List<CurveLoop> { loop });
            return true;
        }

        private ElementId SolidFillType()
        {
            const string name = "PCEB - Sólido preto";
            FilledRegionType t = new FilteredElementCollector(_doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>()
                .FirstOrDefault(x => x.Name == name);
            if (t != null) return t.Id;
            FillPatternElement solid = new FilteredElementCollector(_doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);
            FilledRegionType any = new FilteredElementCollector(_doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().FirstOrDefault();
            if (solid == null || any == null) return null;
            t = (FilledRegionType)any.Duplicate(name);
            t.ForegroundPatternId = solid.Id;
            t.ForegroundPatternColor = new Color(0, 0, 0);
            t.BackgroundPatternId = ElementId.InvalidElementId;
            t.IsMasking = false;
            return t.Id;
        }

        /// <summary>Texto com a altura de papel do DXF (altura em cm do desenho × 10 / escala), arredondada a 0,1 mm.</summary>
        private bool Text(View view, DxfDetail.Prim p, int scale)
        {
            double mm = Math.Round(p.H * 10.0 / scale, 1);
            if (mm <= 0) return false;
            TextNoteType type = TextType(mm);
            double h = p.H * Cm;
            XYZ ux = new XYZ(Math.Cos(p.Rot), Math.Sin(p.Rot), 0), uy = new XYZ(-Math.Sin(p.Rot), Math.Cos(p.Rot), 0);
            XYZ at = P(p.Pts[0]);
            VerticalTextAlignment va;
            switch (p.VA)
            {
                case 'T': va = VerticalTextAlignment.Top; break;
                case 'b': va = VerticalTextAlignment.Bottom; break;
                case 'M': va = VerticalTextAlignment.Middle; break;
                default:
                    // linha de base do TEXT: centro da caixa do Revit fica ~meia altura de maiúscula acima
                    va = VerticalTextAlignment.Middle;
                    at += uy * (h * 0.5);
                    break;
            }
            var opt = new TextNoteOptions(type.Id)
            {
                HorizontalAlignment = p.HA == 'C' ? HorizontalTextAlignment.Center : p.HA == 'R' ? HorizontalTextAlignment.Right : HorizontalTextAlignment.Left,
                VerticalAlignment = va,
                Rotation = p.Rot
            };
            TextNote.Create(_doc, view.Id, at, p.Text.Replace("\n", "\r"), opt);
            return true;
        }

        private TextNoteType TextType(double mm)
        {
            string name = $"PCEB - Detalhe {mm.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} mm";
            if (_textTypes.TryGetValue(name, out TextNoteType t)) return t;
            t = new FilteredElementCollector(_doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault(x => x.Name == name);
            if (t == null)
            {
                t = (TextNoteType)_styles.Text.Duplicate(name);
                t.get_Parameter(BuiltInParameter.TEXT_SIZE)?.Set(U.Mm(mm));
                t.get_Parameter(BuiltInParameter.TEXT_BOX_VISIBILITY)?.Set(0);
                try { t.get_Parameter(BuiltInParameter.LEADER_OFFSET_SHEET)?.Set(0.0); } catch { /* opcional */ }
            }
            _textTypes[name] = t;
            return t;
        }
    }
}
