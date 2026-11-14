using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using DEB.Revit.Core;
using DEB.Revit.Presentation;

namespace DEB.Revit.Formas
{
    public enum TipoCorte { Automatico, Geral, CaixaElevador, Escada }

    /// <summary>O que o "Anotar corte" faz num corte.</summary>
    internal sealed class OpcoesCorte
    {
        public TipoCorte Tipo = TipoCorte.Automatico;
        public bool Cotas = true, Nomes = true, Niveis = true;
    }

    /// <summary>
    /// "Anotar corte" (pedido do usuário, 14/11/2026): cotas verticais, identificadores e cotas de nível num corte, no padrão lido dos
    /// cortes cotados à mão (docs/04_arquitetura/pranchas_de_formas.md, "Anotação automática de cortes e plantas de fôrma").
    /// Princípio: só se anota o que está cortado ou visível — peça escondida atrás de outra mais próxima do observador e peça de fase
    /// posterior à da vista ficam de fora. Tipo do corte: escada (corta lances), caixa de elevador (corta paredes) ou geral.
    /// O que o comando cria leva a marca <see cref="MarcaFormas"/> e é refeito ao rodar de novo; o feito à mão não é tocado.
    /// Precisa de transação (uma por corte, no chamador).
    /// </summary>
    internal sealed class CorteAnotacao
    {
        private readonly Document _doc;
        private readonly FormasSettings _cfg;
        private readonly CorteAnotacaoSpec _c;
        private readonly PieceLog _log;

        public CorteAnotacao(Document doc, FormasSettings cfg, PieceLog log)
        {
            _doc = doc; _cfg = cfg; _log = log;
            _c = cfg.AnotarCorte ?? new CorteAnotacaoSpec();
        }

        public const string PapelCota = "corte-cota", PapelNivel = "corte-nivel", PrefixoId = "id-corte-";

        // ------------------------------------------------------------------ peças da vista

        /// <summary>Peça na vista, em coordenadas do recorte: R (direita), U (altura), D (profundidade; maior = mais perto do observador).</summary>
        private sealed class Peca
        {
            public Element El;
            public double MinR, MaxR, MinU, MaxU, MinD, MaxD;
            public bool Cortada;
            public double R => (MinR + MaxR) / 2;
            public double Larg => MaxR - MinR;
        }

        private ViewSection _v;
        private Transform _t, _inv;
        private double _cortD, _fundoD, _minR, _maxR, _minU, _maxU;
        private List<Peca> _pecas;
        private List<double> _eixos;          // posição R dos eixos que cruzam o corte, crescente
        private List<Level> _niveis;          // níveis da cadeia (sem os ignorados), de baixo para cima
        private Level _terreo;

        private (double R, double U, double D) Local(XYZ p) { XYZ q = _inv.OfPoint(p); return (q.X, q.Y, q.Z); }
        private XYZ Mundo(double r, double u, double d) => _t.OfPoint(new XYZ(r, u, d));

        private void Ler(ViewSection v)
        {
            _v = v;
            BoundingBoxXYZ cb = v.CropBox;
            _t = cb.Transform; _inv = _t.Inverse;
            // o plano de corte é Max.Z do recorte; "à frente" (mais perto do observador) é Z maior
            _cortD = cb.Max.Z; _fundoD = cb.Min.Z;
            _minR = cb.Min.X; _maxR = cb.Max.X; _minU = cb.Min.Y; _maxU = cb.Max.Y;

            var cantos = new List<XYZ>();
            foreach (double x in new[] { cb.Min.X, cb.Max.X })
                foreach (double y in new[] { cb.Min.Y, cb.Max.Y })
                    foreach (double z in new[] { cb.Min.Z, cb.Max.Z })
                        cantos.Add(_t.OfPoint(new XYZ(x, y, z)));
            var ol = new Outline(new XYZ(cantos.Min(p => p.X), cantos.Min(p => p.Y), cantos.Min(p => p.Z)),
                                 new XYZ(cantos.Max(p => p.X), cantos.Max(p => p.Y), cantos.Max(p => p.Z)));
            var cats = new ElementMulticategoryFilter(new List<BuiltInCategory>
            {
                BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_StructuralFoundation, BuiltInCategory.OST_Walls, BuiltInCategory.OST_Stairs, BuiltInCategory.OST_Windows,
            });
            // pela caixa do recorte: pedir as peças à vista obriga o Revit a gerar a vista (lento num modelo grande)
            List<Element> els = new FilteredElementCollector(_doc).WherePasses(cats).WherePasses(new BoundingBoxIntersectsFilter(ol))
                .WhereElementIsNotElementType().ToList();

            int faseVista = OrdemFase(v.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId());
            var fasesIgn = (_c.FasesIgnorar ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            int futuras = 0;
            _pecas = new List<Peca>();
            foreach (Element e in els)
            {
                if (e is FamilyInstance fi && fi.SuperComponent != null) continue;
                // fase posterior à da vista, ou fase da lista (ex.: "FUTURA", que a vista mostra em destaque mas não se anota)
                string fase = (_doc.GetElement(e.CreatedPhaseId) as Phase)?.Name ?? "";
                if ((faseVista >= 0 && OrdemFase(e.CreatedPhaseId) > faseVista)
                    || fasesIgn.Any(x => fase.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0)) { futuras++; continue; }
                BoundingBoxXYZ bb = e.get_BoundingBox(null);
                if (bb == null) continue;
                var pts = new List<(double R, double U, double D)>();
                foreach (double x in new[] { bb.Min.X, bb.Max.X })
                    foreach (double y in new[] { bb.Min.Y, bb.Max.Y })
                        foreach (double z in new[] { bb.Min.Z, bb.Max.Z })
                            pts.Add(Local(new XYZ(x, y, z)));
                var p = new Peca
                {
                    El = e, MinR = pts.Min(q => q.R), MaxR = pts.Max(q => q.R), MinU = pts.Min(q => q.U), MaxU = pts.Max(q => q.U),
                    MinD = pts.Min(q => q.D), MaxD = pts.Max(q => q.D),
                };
                if (p.MinD > _cortD + 1e-6 || p.MaxD < _fundoD - 1e-6) continue;   // à frente do corte ou além da profundidade
                if (p.MaxR < _minR || p.MinR > _maxR) continue;
                p.Cortada = p.MinD < _cortD - 1e-6 && p.MaxD > _cortD - 1e-6;
                _pecas.Add(p);
            }
            if (futuras > 0) _log.Info($"{v.Name}: {futuras} peça(s) de fase posterior à da vista ou de fase ignorada ficaram de fora.");

            // eixos que cruzam o corte: direção perpendicular à direita da vista
            _eixos = new FilteredElementCollector(_doc).OfClass(typeof(Grid)).Cast<Grid>()
                .Select(g => g.Curve as Line).Where(l => l != null && Math.Abs(l.Direction.DotProduct(_t.BasisX)) < 0.01)
                .Select(l => Local(l.Origin).R).Where(r => r >= _minR - 1e-6 && r <= _maxR + 1e-6)
                .Distinct().OrderBy(r => r).ToList();

            var ign = (_c.NiveisIgnorar ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            List<Level> todos = ModeloFormas.Niveis(_doc);
            _terreo = FormasUi.NivelPadrao(_doc, todos, TipoPlanta.Locacao);
            _niveis = todos.Where(l => !ign.Any(x => l.Name.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0))
                .Where(l => U(l) >= _minU - 1e-6 && U(l) <= _maxU + 1e-6 && l.ProjectElevation >= _terreo.ProjectElevation - 1e-6).ToList();
        }

        private int OrdemFase(ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId) return -1;
            int i = 0;
            foreach (Phase f in _doc.Phases) { if (f.Id == id) return i; i++; }
            return -1;
        }

        /// <summary>Altura do nível nas coordenadas do recorte (Z interno do nível = ProjectElevation).</summary>
        private double U(Level l) => Local(new XYZ(_t.Origin.X, _t.Origin.Y, l.ProjectElevation)).U;

        /// <summary>
        /// Visível = cortada, ou vista sem estar coberta pelas peças mais próximas do observador: a projeção da caixa (R × U) é amostrada
        /// numa grade 6 × 6 e a peça fica oculta se ≥ 85% dos pontos caem dentro de peças mais à frente (inclui paredes e escadas).
        /// </summary>
        private List<Peca> Visiveis()
        {
            var r = new List<Peca>();
            foreach (Peca p in _pecas)
            {
                if (p.Cortada) { r.Add(p); continue; }
                var frente = _pecas.Where(o => o != p && o.MaxD > p.MaxD + 1e-6 && !(o.El is FamilyInstance w && w.Category?.Id.Value == (long)BuiltInCategory.OST_Windows)).ToList();
                int cobertos = 0, n = 0;
                double r0 = Math.Max(p.MinR, _minR), r1 = Math.Min(p.MaxR, _maxR);
                for (int i = 0; i < 6; i++)
                    for (int j = 0; j < 6; j++)
                    {
                        double rr = r0 + (r1 - r0) * (i + 0.5) / 6, uu = p.MinU + (p.MaxU - p.MinU) * (j + 0.5) / 6;
                        n++;
                        if (frente.Any(o => rr >= o.MinR && rr <= o.MaxR && uu >= o.MinU && uu <= o.MaxU)) cobertos++;
                    }
                if (n == 0 || cobertos < 0.85 * n) r.Add(p);
            }
            return r;
        }

        // ------------------------------------------------------------------ entrada

        /// <summary>Anota o corte. Precisa de transação.</summary>
        public string Anotar(ViewSection v, OpcoesCorte op)
        {
            Ler(v);
            // o que o comando fez antes nesta vista é refeito (o feito à mão fica)
            var velhas = MarcaFormas.Com(new FilteredElementCollector(_doc, v.Id).OfClass(typeof(Dimension)), PapelCota)
                .Concat(MarcaFormas.Com(new FilteredElementCollector(_doc, v.Id).OfClass(typeof(SpotDimension)), PapelNivel))
                .Select(e => e.Id).Distinct().ToList();
            if (velhas.Count > 0) _doc.Delete(velhas);

            List<Peca> vis = Visiveis();
            // caixa de elevador: corta duas ou mais paredes (as da caixa); escada: corta um lance; senão geral
            TipoCorte tipo = op.Tipo != TipoCorte.Automatico ? op.Tipo
                : vis.Count(p => p.Cortada && Cat(p, BuiltInCategory.OST_Walls)) >= 2 ? TipoCorte.CaixaElevador
                : Lances(vis).Any(l => l.P.Cortada) ? TipoCorte.Escada : TipoCorte.Geral;
            _log.Info($"{v.Name}: corte {tipo}; {vis.Count} peça(s) cortadas ou visíveis de {_pecas.Count}; {_eixos.Count} eixo(s); {_niveis.Count} nível(is).");

            if (op.Cotas)
            {
                CadeiaNiveis();
                if (tipo == TipoCorte.Geral) CotasGeral(vis);
                else if (tipo == TipoCorte.CaixaElevador) CotasCaixa(vis);
                else CotasEscada(vis);
            }
            if (op.Niveis) CotasDeNivel(vis, tipo);
            if (op.Nomes) Nomes(vis);
            return tipo.ToString();
        }

        /// <summary>
        /// Diagnóstico para o autoteste (só leitura): peças cortadas, lances e portas com as medidas em cm (R a partir da borda esquerda
        /// do recorte, Z a partir do térreo), faces horizontais e referências. Serve para ajustar as regras sem abrir o modelo.
        /// </summary>
        internal string Diagnostico(ViewSection v)
        {
            Ler(v);
            var sb = new System.Text.StringBuilder();
            double z0 = _terreo.ProjectElevation;
            string Cm(double x) => DEB.Core.Fmt.N(UnitUtils.ConvertFromInternalUnits(x, UnitTypeId.Centimeters));
            string Fase(Element e) => (_doc.GetElement(e.CreatedPhaseId) as Phase)?.Name ?? "?";
            var vis = new HashSet<Peca>(Visiveis());
            sb.AppendLine($"  [diag] fase da vista: {(_doc.GetElement(v.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsElementId() ?? ElementId.InvalidElementId) as Phase)?.Name}; eixos R: {string.Join(" ", _eixos.Select(r => Cm(r - _minR)))}");
            foreach (Peca p in _pecas.OrderBy(p => p.MinR))
            {
                bool interessa = p.Cortada || Cat(p, BuiltInCategory.OST_Stairs) || Cat(p, BuiltInCategory.OST_Windows);
                if (!interessa) continue;
                string tipo = p.El is FamilyInstance fi ? fi.Symbol.FamilyName + ":" + fi.Symbol.Name : p.El.Name;
                sb.AppendLine($"  [diag] {p.El.Id.Value} {p.El.Category?.Name} \"{tipo}\" marca={ModeloFormas.Marca(p.El)} fase={Fase(p.El)} " +
                              $"R={Cm(p.MinR - _minR)}..{Cm(p.MaxR - _minR)} Z={Cm(p.MinU - UDeZ(z0))}..{Cm(p.MaxU - UDeZ(z0))} D={Cm(p.MinD - _cortD)}..{Cm(p.MaxD - _cortD)} " +
                              $"{(p.Cortada ? "cortada" : "vista")}{(vis.Contains(p) ? "" : " OCULTA")} viga={EhViga(p)}");
                if (Cat(p, BuiltInCategory.OST_Stairs) || EhViga(p) || p.El is Floor)
                    foreach (FaceH f in Faces(p.El).OrderBy(f => f.Z))
                        sb.AppendLine($"           {(f.Topo ? "topo " : "fundo")} z={Cm(f.Z - z0)} R={Cm(f.MinR - _minR)}..{Cm(f.MaxR - _minR)}");
                if (Cat(p, BuiltInCategory.OST_Stairs))
                {
                    Lance l = Analisar(p.El, p);
                    sb.AppendLine($"           início={(l.Inicio == null ? "-" : Cm(l.Inicio.Z - z0))} fim={(l.Fim == null ? "-" : Cm(l.Fim.Z - z0))} sobe={l.Sobe}; " +
                                  $"faces verticais R: {string.Join(" ", FacesVerticais(p.El).Where(f => f != null).Select(f => Cm(f.Value.Item2 - _minR)))}");
                }
                if (p.El is FamilyInstance j && Cat(p, BuiltInCategory.OST_Windows))
                {
                    Parameter ps = j.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM), ph = j.get_Parameter(BuiltInParameter.INSTANCE_HEAD_HEIGHT_PARAM);
                    var nomes = new List<string>();
                    foreach (FamilyInstanceReferenceType t in Enum.GetValues(typeof(FamilyInstanceReferenceType)))
                    {
                        int n = 0;
                        try { n = j.GetReferences(t).Count; } catch { }
                        if (n > 0) nomes.Add(t + "=" + n + "(" + string.Join(",", j.GetReferences(t).Select(r => { try { return j.GetReferenceName(r); } catch { return "?"; } })) + ")");
                    }
                    sb.AppendLine($"           nível={(_doc.GetElement(j.LevelId) as Level)?.Name} soleira={(ps != null && ps.HasValue ? Cm(ps.AsDouble()) : "-")} " +
                                  $"topo={(ph != null && ph.HasValue ? Cm(ph.AsDouble()) : "-")} refs: {string.Join(" ", nomes)}; faces: " +
                                  string.Join(" ", Faces(j).OrderBy(f => f.Z).Select(f => (f.Topo ? "t" : "f") + Cm(f.Z - z0))));
                }
                if (Cat(p, BuiltInCategory.OST_Walls))
                    sb.AppendLine($"           faces verticais R: {string.Join(" ", FacesVerticais(p.El).Where(f => f != null).Select(f => Cm(f.Value.Item2 - _minR)))}");
            }
            return sb.ToString();
        }

        private static bool Cat(Peca p, BuiltInCategory c) => p.El.Category?.Id.Value == (long)c;

        private bool EhViga(Peca p) => p.El is FamilyInstance fi && Cat(p, BuiltInCategory.OST_StructuralFraming) && ModeloFormas.Passa(fi, _cfg.FiltroVigas);
        private bool EhPainel(Peca p) => p.El is FamilyInstance fi && Cat(p, BuiltInCategory.OST_StructuralFraming) && ModeloFormas.Passa(fi, _cfg.FiltroPaineis);
        private bool EhBaldrame(Peca p) => EhViga(p) && ModeloFormas.NivelDaViga(_doc, (FamilyInstance)p.El)?.Id == _terreo.Id;

        // ------------------------------------------------------------------ faces e cotas

        /// <summary>Face horizontal (topo ou fundo) de uma peça, com a referência que serve para cota.</summary>
        private sealed class FaceH
        {
            public Reference Ref;
            public PlanarFace Face;
            public Transform T = Transform.Identity;
            public double Z;   // Z interno
            public bool Topo;
            public double MinR, MaxR;   // extensão da face na direita da vista
            public double Comp => MaxR - MinR;
        }

        /// <summary>Extensão R da face (pelos vértices das arestas, já no modelo).</summary>
        private void Extensao(FaceH h)
        {
            double a = double.MaxValue, b = double.MinValue;
            foreach (EdgeArray ea in h.Face.EdgeLoops)
                foreach (Edge ed in ea)
                    foreach (XYZ q in ed.Tessellate())
                    { double r = Local(h.T.OfPoint(q)).R; a = Math.Min(a, r); b = Math.Max(b, r); }
            h.MinR = a; h.MaxR = b;
        }

        private readonly Dictionary<ElementId, List<FaceH>> _faces = new Dictionary<ElementId, List<FaceH>>();

        /// <summary>
        /// Faces horizontais da peça. Família: geometria do SÍMBOLO + referência estável da instância
        /// ("&lt;UniqueId&gt;:0:INSTANCE:" + referência do símbolo) — a face da geometria da instância trava a cota.
        /// </summary>
        private List<FaceH> Faces(Element e)
        {
            if (_faces.TryGetValue(e.Id, out var cache)) return cache;
            var r = new List<FaceH>();
            var opt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
            GeometryElement ge = e.get_Geometry(opt);
            if (ge != null)
                foreach (GeometryObject go in ge)
                {
                    if (go is GeometryInstance gi && e is FamilyInstance fi)
                    {
                        foreach (GeometryObject so in gi.GetSymbolGeometry())
                            if (so is Solid s && s.Volume > 1e-9)
                                foreach (Face f in s.Faces)
                                {
                                    if (!(f is PlanarFace pf) || f.Reference == null) continue;
                                    XYZ n = gi.Transform.OfVector(pf.FaceNormal);
                                    if (Math.Abs(n.Z) < 0.999) continue;
                                    Reference rf;
                                    try { rf = Reference.ParseFromStableRepresentation(_doc, fi.UniqueId + ":0:INSTANCE:" + f.Reference.ConvertToStableRepresentation(_doc)); }
                                    catch { continue; }
                                    { var h = new FaceH { Ref = rf, Face = pf, T = gi.Transform, Z = gi.Transform.OfPoint(pf.Origin).Z, Topo = n.Z > 0 }; Extensao(h); r.Add(h); }
                                }
                    }
                    else if (go is Solid s && s.Volume > 1e-9)
                        foreach (Face f in s.Faces)
                            if (f is PlanarFace pf && f.Reference != null && Math.Abs(pf.FaceNormal.Z) > 0.999)
                                { var h = new FaceH { Ref = f.Reference, Face = pf, Z = pf.Origin.Z, Topo = pf.FaceNormal.Z > 0 }; Extensao(h); r.Add(h); }
                }
            _faces[e.Id] = r;
            return r;
        }

        private FaceH Topo(Element e) => Faces(e).Where(f => f.Topo).OrderByDescending(f => f.Z).FirstOrDefault();
        private FaceH Fundo(Element e) => Faces(e).Where(f => !f.Topo).OrderBy(f => f.Z).FirstOrDefault();

        private DimensionType _tipoCota;
        private bool _tipoCotaLido;
        private DimensionType TipoCota()
        {
            if (_tipoCotaLido) return _tipoCota;
            _tipoCotaLido = true;
            string nome = _c.TipoCota?.Trim() ?? "";
            var lin = new FilteredElementCollector(_doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().Where(t => t.StyleType == DimensionStyleType.Linear).ToList();
            _tipoCota = lin.FirstOrDefault(t => string.Equals(t.Name, nome, StringComparison.OrdinalIgnoreCase));
            if (_tipoCota != null) return _tipoCota;
            // reserva: tipo próprio em cm com uma casa (os apoios de escada têm 250,4); nunca o "SecretInternal…" do Revit
            _tipoCota = lin.FirstOrDefault(t => t.Name == NomeCotaReserva);
            if (_tipoCota == null)
            {
                DimensionType fonte = lin.Where(t => !t.Name.StartsWith("SecretInternal", StringComparison.OrdinalIgnoreCase))
                    .Where(t => t.GetSpecTypeId() == SpecTypeId.Length).OrderBy(t => t.Name).FirstOrDefault();
                if (fonte != null)
                    try
                    {
                        _tipoCota = (DimensionType)fonte.Duplicate(NomeCotaReserva);
                        _tipoCota.SetUnitsFormatOptions(new FormatOptions(UnitTypeId.Centimeters) { UseDefault = false, Accuracy = 0.1, SuppressTrailingZeros = true });
                        _tipoCota.get_Parameter(BuiltInParameter.LINE_COLOR)?.Set(0);
                    }
                    catch { _tipoCota = null; }
            }
            _log.Warn($"Tipo de cota \"{nome}\" não existe no projeto; usado \"{_tipoCota?.Name ?? "o padrão do Revit"}\".");
            return _tipoCota;
        }

        public const string NomeCotaReserva = "PCEB - Cota corte (cm)", NomeNivelReserva = "PCEB - Cota de nível (m)";

        /// <summary>Uma referência com a sua altura (Z interno), para montar cadeias verticais.</summary>
        private readonly struct RefZ
        {
            public readonly Reference Ref; public readonly double Z;
            public RefZ(Reference r, double z) { Ref = r; Z = z; }
        }

        private RefZ? Nivel(Level l) => l == null ? (RefZ?)null : new RefZ(l.GetPlaneReference(), l.ProjectElevation);
        private RefZ? DeFace(FaceH f) => f == null ? (RefZ?)null : new RefZ(f.Ref, f.Z);

        /// <summary>Cadeia vertical na posição R da vista (referências repetidas na mesma altura saem uma vez). Devolve se criou.</summary>
        private bool Vertical(double r, IEnumerable<RefZ?> refs, string oQue)
        {
            var l = new List<RefZ>();
            foreach (var x in refs.Where(x => x != null).Select(x => x.Value).OrderBy(x => x.Z))
                if (l.Count == 0 || x.Z - l[l.Count - 1].Z > U_(0.005)) l.Add(x);
            if (l.Count < 2) return false;
            var ra = new ReferenceArray();
            foreach (var x in l) ra.Append(x.Ref);
            double d = _cortD;
            XYZ a = Mundo(r, Local(new XYZ(_t.Origin.X, _t.Origin.Y, l[0].Z)).U, d), b = Mundo(r, Local(new XYZ(_t.Origin.X, _t.Origin.Y, l[l.Count - 1].Z)).U, d);
            try
            {
                DimensionType tipo = TipoCota();
                Dimension dim = tipo != null ? _doc.Create.NewDimension(_v, Line.CreateBound(a, b), ra, tipo) : _doc.Create.NewDimension(_v, Line.CreateBound(a, b), ra);
                MarcaFormas.Marcar(dim, PapelCota);
                return true;
            }
            catch (Exception ex) { _log.Warn($"{_v.Name}: cota \"{oQue}\" não criada ({ex.Message})."); return false; }
        }

        /// <summary>Cota horizontal entre referências com posição R conhecida, na altura U.</summary>
        private bool Horizontal(double u, IEnumerable<(Reference Ref, double R)?> refs, string oQue)
        {
            var l = new List<(Reference Ref, double R)>();
            foreach (var x in refs.Where(x => x != null).Select(x => x.Value).OrderBy(x => x.R))
                if (l.Count == 0 || x.R - l[l.Count - 1].R > U_(0.005)) l.Add(x);
            if (l.Count < 2) return false;
            var ra = new ReferenceArray();
            foreach (var x in l) ra.Append(x.Ref);
            try
            {
                Line ln = Line.CreateBound(Mundo(l[0].R, u, _cortD), Mundo(l[l.Count - 1].R, u, _cortD));
                DimensionType tipo = TipoCota();
                Dimension dim = tipo != null ? _doc.Create.NewDimension(_v, ln, ra, tipo) : _doc.Create.NewDimension(_v, ln, ra);
                MarcaFormas.Marcar(dim, PapelCota);
                return true;
            }
            catch (Exception ex) { _log.Warn($"{_v.Name}: cota \"{oQue}\" não criada ({ex.Message})."); return false; }
        }

        private static double U_(double m) => UnitUtils.ConvertToInternalUnits(m, UnitTypeId.Meters);

        private double UDeZ(double z) => Local(new XYZ(_t.Origin.X, _t.Origin.Y, z)).U;

        /// <summary>Peças cortadas (ou visíveis) cuja faixa R contém <paramref name="r"/> (com folga).</summary>
        private IEnumerable<Peca> Em(IEnumerable<Peca> l, double r, double folgaM = 0.05) =>
            l.Where(p => r >= p.MinR - U_(folgaM) && r <= p.MaxR + U_(folgaM));

        private double Estrutura0 => _pecas.Count == 0 ? _minR : Math.Max(_minR, _pecas.Min(p => p.MinR));

        // ------------------------------------------------------------------ cadeias comuns

        /// <summary>Níveis e total, do lado de fora do início do corte (à esquerda da estrutura).</summary>
        private void CadeiaNiveis()
        {
            if (_niveis.Count < 2) return;
            double r = (_eixos.Count > 0 ? Math.Min(_eixos[0], Estrutura0) : Estrutura0) - U_(_c.NiveisForaM);
            Vertical(r, _niveis.Select(Nivel), "níveis");
            if (_niveis.Count > 2) Vertical(r - U_(_c.PassoCadeiaM), new[] { Nivel(_niveis[0]), Nivel(_niveis[_niveis.Count - 1]) }, "total");
        }

        // ------------------------------------------------------------------ corte geral

        private void CotasGeral(List<Peca> vis)
        {
            if (_eixos.Count == 0) { _log.Warn($"{_v.Name}: nenhum eixo cruza o corte; cadeias do vão e da fachada não criadas."); return; }
            double g0 = _eixos[0], gN = _eixos[_eixos.Count - 1];
            double vao = _eixos.Count > 1 ? _eixos[1] - g0 : U_(5);
            var cortadas = vis.Where(p => p.Cortada).ToList();
            List<Level> pisos = _niveis.Where(l => l.ProjectElevation > _terreo.ProjectElevation + 1e-6).ToList();

            // laje de cada piso (piso cortado do nível) na posição r
            Peca Laje(Level l, double r) => Em(cortadas.Where(p => p.El is Floor f && f.LevelId == l.Id), r, 0.5).OrderBy(p => Math.Abs(p.R - r)).FirstOrDefault()
                                            ?? cortadas.Where(p => p.El is Floor f && f.LevelId == l.Id).OrderBy(p => Math.Abs(p.R - r)).FirstOrDefault();

            // 2) topo do baldrame → térreo → topo das capas, no vão junto ao eixo da ponta
            double r2 = g0 + U_(_c.JuntoEixoM);
            Peca bald = cortadas.Where(EhBaldrame).OrderBy(p => Math.Abs(p.R - g0)).FirstOrDefault();
            var c2 = new List<RefZ?> { bald != null ? DeFace(Topo(bald.El)) : null, Nivel(_terreo) };
            foreach (Level l in pisos) { Peca lj = Laje(l, r2); if (lj != null) c2.Add(DeFace(Topo(lj.El))); }
            Vertical(r2, c2, "baldrame/térreo/capas");

            // 3) rebaixo da capa em cada piso
            double r3 = r2 + U_(_c.PassoCadeiaM);
            foreach (Level l in pisos)
            {
                Peca lj = Laje(l, r3);
                if (lj != null) Vertical(r3, new[] { Nivel(l), DeFace(Topo(lj.El)) }, "rebaixo " + l.Name);
            }

            // 4) no vão: painel + capa e pé-direito livre até o térreo (só se todos os pisos têm laje ali)
            double r4 = g0 + 0.6 * vao;
            var c4 = new List<RefZ?> { Nivel(_terreo) };
            bool completa = pisos.Count > 0;
            foreach (Level l in pisos)
            {
                Peca lj = Laje(l, r4);
                if (lj == null) { completa = false; break; }
                FaceH topo = Topo(lj.El);
                Peca painel = Em(cortadas.Where(EhPainel), r4, 0.3).Where(p => topo != null && Math.Abs(UDeZ(topo.Z) - p.MaxU) < U_(0.40))
                    .OrderByDescending(p => p.MaxU).FirstOrDefault();
                c4.Add(DeFace(Topo(lj.El)));
                c4.Add(DeFace(painel != null ? Fundo(painel.El) : Fundo(lj.El)));
            }
            if (completa) Vertical(r4, c4, "vão (laje e pé-direito)");
            else _log.Info($"{_v.Name}: falta laje em algum piso no vão; cadeia do vão não criada.");

            // 5) pela fachada, 0,70 m além do último eixo: baldrame, bloco, fundo e topo de cada viga de borda, topo do pilar
            double r5 = gN + U_(_c.FachadaAlemM);
            var c5 = new List<RefZ?>();
            // só o topo do bloco (o fundo e as estacas não entram) e só as vigas de borda CORTADAS (as vistas atrás davam trechos a mais)
            Peca bloco = Em(vis.Where(p => Cat(p, BuiltInCategory.OST_StructuralFoundation)), gN, 0.30)
                .OrderByDescending(p => Topo(p.El)?.Z ?? double.MinValue).FirstOrDefault();
            if (bloco != null) c5.Add(DeFace(Topo(bloco.El)));
            foreach (Peca p in Em(cortadas, gN, 0.30).Where(EhViga))
            { c5.Add(DeFace(Fundo(p.El))); c5.Add(DeFace(Topo(p.El))); }
            Peca pil = Em(vis.Where(p => Cat(p, BuiltInCategory.OST_StructuralColumns) && p.El is FamilyInstance fc && ModeloFormas.Passa(fc, _cfg.FiltroPilares)), gN, 0.30)
                .OrderBy(p => Math.Abs(p.R - gN)).FirstOrDefault();
            if (pil != null) c5.Add(DeFace(Topo(pil.El)));
            Vertical(r5, c5, "fachada");
        }

        // ------------------------------------------------------------------ caixa de elevador

        private void CotasCaixa(List<Peca> vis)
        {
            var cortadas = vis.Where(p => p.Cortada).ToList();
            var paredes = cortadas.Where(p => Cat(p, BuiltInCategory.OST_Walls)).ToList();
            if (paredes.Count == 0) return;
            double cx0 = paredes.Min(p => p.MinR), cx1 = paredes.Max(p => p.MaxR);

            // cadeia junto ao pilar do eixo, como a da fachada: topo do bloco mais alto, fundo e topo das vigas CORTADAS, topo do pilar
            Peca pilar = vis.Where(p => Cat(p, BuiltInCategory.OST_StructuralColumns)).OrderBy(p => _eixos.Count == 0 ? 0 : _eixos.Min(g => Math.Abs(g - p.R))).FirstOrDefault();
            if (pilar != null)
            {
                bool esq = pilar.R < (cx0 + cx1) / 2;
                double rp = (esq ? pilar.MinR : pilar.MaxR) + (esq ? -1 : 1) * U_(_c.FachadaAlemM / 2);
                var c = new List<RefZ?> { DeFace(Topo(pilar.El)) };
                Peca bloco = Em(vis.Where(p => Cat(p, BuiltInCategory.OST_StructuralFoundation)), pilar.R, 0.40)
                    .OrderByDescending(p => Topo(p.El)?.Z ?? double.MinValue).FirstOrDefault();
                if (bloco != null) c.Add(DeFace(Topo(bloco.El)));
                foreach (Peca p in Em(cortadas, pilar.R, 0.40).Where(EhViga)) { c.Add(DeFace(Fundo(p.El))); c.Add(DeFace(Topo(p.El))); }
                Vertical(rp, c, "pilar do eixo");
            }

            // portas: base e topo pela soleira/altura da instância (a caixa da família começa no nível, a porta 3 cm abaixo dele);
            // referência da base: "Bottom" da família, senão a face mais baixa da geometria
            var janelas = vis.Where(p => Cat(p, BuiltInCategory.OST_Windows) && p.El is FamilyInstance).ToList();
            var portas = new List<(Peca P, RefZ? Base, RefZ? Topo)>();
            foreach (Peca j in janelas)
            {
                var fi = (FamilyInstance)j.El;
                double nz = (_doc.GetElement(fi.LevelId) as Level)?.ProjectElevation ?? 0;
                Parameter ps = fi.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM), ph = fi.get_Parameter(BuiltInParameter.INSTANCE_HEAD_HEIGHT_PARAM);
                BoundingBoxXYZ bb = fi.get_BoundingBox(null);
                double zb = ps != null && ps.HasValue ? nz + ps.AsDouble() : bb?.Min.Z ?? nz;
                double zt = ph != null && ph.HasValue ? nz + ph.AsDouble() : bb?.Max.Z ?? nz;
                Reference rt = fi.GetReferences(FamilyInstanceReferenceType.Top).FirstOrDefault(), rb = fi.GetReferences(FamilyInstanceReferenceType.Bottom).FirstOrDefault();
                // sem "Bottom" (abertura sem sólido): plano de referência com nome de base, senão a face horizontal da parede na
                // altura da soleira (fundo do vão na parede, ou a base da parede quando a porta começa nela)
                rb = rb ?? RefDeBase(fi);
                FaceH fb = rb != null ? null : Faces(fi).Concat(paredes.SelectMany(w => Faces(w.El))).Where(f => Math.Abs(f.Z - zb) < U_(0.005))
                    .OrderBy(f => Math.Abs(f.Z - zb)).FirstOrDefault();
                RefZ? b = rb != null ? new RefZ(rb, zb) : DeFace(fb);
                RefZ? t = rt != null ? new RefZ(rt, zt) : DeFace(Faces(fi).Where(f => f.Topo).OrderBy(f => Math.Abs(f.Z - zt)).FirstOrDefault());
                portas.Add((j, b, t));
            }

            // cadeia vertical por dentro da caixa: lajes de topo e do fundo, base e topo de cada porta, níveis entre elas
            double rc = (cx0 + cx1) / 2;
            var cin = new List<RefZ?>();
            foreach (Peca lj in cortadas.Where(p => p.El is Floor && p.MinR >= cx0 - U_(0.3) && p.MaxR <= cx1 + U_(0.3)))
            { cin.Add(DeFace(Topo(lj.El))); cin.Add(DeFace(Fundo(lj.El))); }
            foreach (var pt in portas) { cin.Add(pt.Base); cin.Add(pt.Topo); }
            var zs = cin.Where(x => x != null).Select(x => x.Value.Z).ToList();
            // só os níveis dentro da caixa (o da cobertura, acima da laje de topo, fica de fora)
            if (zs.Count > 0) cin.AddRange(_niveis.Where(l => l.ProjectElevation > zs.Min() + U_(0.005) && l.ProjectElevation < zs.Max() - U_(0.005)).Select(Nivel));
            Vertical(rc, cin, "dentro da caixa");

            // altura de cada porta
            foreach (var pt in portas)
                if (pt.Base != null && pt.Topo != null) Vertical(pt.P.MaxR + U_(0.15), new[] { pt.Base, pt.Topo }, "altura da porta");

            // em cada piso: face externa da parede → bordas da porta → face externa da parede
            foreach (Peca j in janelas)
            {
                var fi = (FamilyInstance)j.El;
                var refs = new List<(Reference, double)?>();
                foreach (var tipo in new[] { FamilyInstanceReferenceType.Left, FamilyInstanceReferenceType.Right })
                {
                    Reference rr = fi.GetReferences(tipo).FirstOrDefault();
                    if (rr != null) refs.Add((rr, tipo == FamilyInstanceReferenceType.Left ? Math.Min(j.MinR, j.MaxR) : Math.Max(j.MinR, j.MaxR)));
                }
                if (refs.Count == 2 && refs[0].Value.Item2 > refs[1].Value.Item2) refs.Reverse();
                var faces = paredes.Where(w => j.MinU < w.MaxU && j.MaxU > w.MinU).SelectMany(w => FacesVerticais(w.El)).Where(f => f != null).ToList();
                if (faces.Count > 0)
                {
                    refs.Add(faces.OrderBy(f => f.Value.Item2).First());
                    refs.Add(faces.OrderByDescending(f => f.Value.Item2).First());
                }
                Horizontal(j.MaxU + U_(0.30), refs, "parede/porta");
            }
        }

        /// <summary>Plano de referência da família cujo nome indica a base da abertura (soleira, peitoril, base, inferior, bottom, sill).</summary>
        private static Reference RefDeBase(FamilyInstance fi)
        {
            string[] nomes = { "bottom", "sill", "soleira", "peitoril", "base", "inferior" };
            foreach (var t in new[] { FamilyInstanceReferenceType.StrongReference, FamilyInstanceReferenceType.WeakReference, FamilyInstanceReferenceType.NotAReference })
                foreach (Reference r in fi.GetReferences(t))
                {
                    string n = "";
                    try { n = fi.GetReferenceName(r) ?? ""; } catch { }
                    if (nomes.Any(x => n.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0)) return r;
                }
            return null;
        }

        /// <summary>Faces verticais da peça perpendiculares à direita da vista (com a posição R), só de geometria própria (paredes).</summary>
        private List<(Reference, double)?> FacesVerticais(Element e)
        {
            var r = new List<(Reference, double)?>();
            GeometryElement ge = e.get_Geometry(new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine });
            if (ge == null) return r;
            foreach (GeometryObject go in ge)
            {
                if (go is GeometryInstance gi && e is FamilyInstance fi)
                {
                    foreach (GeometryObject so in gi.GetSymbolGeometry())
                        if (so is Solid s1 && s1.Volume > 1e-9)
                            foreach (Face f in s1.Faces)
                            {
                                if (!(f is PlanarFace pf) || f.Reference == null) continue;
                                if (Math.Abs(gi.Transform.OfVector(pf.FaceNormal).DotProduct(_t.BasisX)) < 0.999) continue;
                                try
                                {
                                    Reference rf = Reference.ParseFromStableRepresentation(_doc, fi.UniqueId + ":0:INSTANCE:" + f.Reference.ConvertToStableRepresentation(_doc));
                                    r.Add((rf, Local(gi.Transform.OfPoint(pf.Origin)).R));
                                }
                                catch { }
                            }
                }
                else if (go is Solid s && s.Volume > 1e-9)
                    foreach (Face f in s.Faces)
                        if (f is PlanarFace pf && f.Reference != null && Math.Abs(pf.FaceNormal.DotProduct(_t.BasisX)) > 0.999)
                            r.Add((f.Reference, Local(pf.Origin).R));
            }
            return r;
        }

        // ------------------------------------------------------------------ escada

        private void CotasEscada(List<Peca> vis)
        {
            var todos = Lances(vis).Where(l => l.P.MaxD >= _fundoD && l.P.MinD <= _cortD).ToList();
            if (todos.Count == 0) { _log.Warn($"{_v.Name}: corte de escada sem lances visíveis."); return; }
            // os lances cortados recebem vão, espelhos e pisos; os só vistos (atrás) entram só na cadeia dos níveis
            var lances = todos.Any(l => l.P.Cortada) ? todos.Where(l => l.P.Cortada).ToList() : todos;
            var cortadas = vis.Where(p => p.Cortada).ToList();

            // apoios de cada lado: as vigas cortadas que chegam à ponta de algum lance definem a faixa do lado; entram todas as vigas
            // cortadas dessa faixa acima do térreo (inclusive a do lance de fase ignorada) — cadeia térreo → topo de cada viga
            double folga = U_(_c.ApoioEscadaFolgaM), zTerreo = _terreo.ProjectElevation;
            var apoiosEsq = new List<Peca>(); var apoiosDir = new List<Peca>();
            var info = new Dictionary<Element, Lance>();
            foreach (var (run, p) in lances)
            {
                Lance l = Analisar(run, p);
                info[run] = l;
                Peca a0 = cortadas.Where(EhViga).Where(a => a.MinR <= p.MinR + folga && a.MaxR >= p.MinR - folga && a.MaxU >= p.MinU - U_(0.6) && a.MaxU <= p.MaxU + U_(0.1))
                    .OrderBy(a => Math.Abs(a.MaxR - p.MinR)).FirstOrDefault();
                Peca a1 = cortadas.Where(EhViga).Where(a => a.MinR <= p.MaxR + folga && a.MaxR >= p.MaxR - folga && a.MaxU >= p.MinU - U_(0.6) && a.MaxU <= p.MaxU + U_(0.6))
                    .OrderBy(a => Math.Abs(a.MinR - p.MaxR)).FirstOrDefault();
                if (a0 != null) apoiosEsq.Add(a0);
                if (a1 != null && a1 != a0) apoiosDir.Add(a1);
                l.Esq = a0; l.Dir = a1;
                // o patamar fica sobre o apoio mais baixo: face de cima abaixo do topo dele (encaixe, dente) não é o início
                double? zApoio = new[] { a0, a1 }.Where(a => a != null).Select(a => Topo(a.El)?.Z).Where(z => z != null).Select(z => z.Value).DefaultIfEmpty(double.NaN).Min();
                if (zApoio != null && !double.IsNaN(zApoio.Value)) Analisar(run, p, zApoio.Value - U_(0.01));
            }
            foreach (var (lado, sinal) in new[] { (apoiosEsq, -1), (apoiosDir, 1) })
            {
                if (lado.Count == 0) continue;
                double f0 = lado.Min(a => a.MinR), f1 = lado.Max(a => a.MaxR);
                var faixa = cortadas.Where(EhViga).Where(a => a.MaxR >= f0 - U_(0.05) && a.MinR <= f1 + U_(0.05)).Select(a => DeFace(Topo(a.El)))
                    .Where(f => f != null && f.Value.Z > zTerreo + U_(0.10)).ToList();
                double r = sinal < 0 ? f0 - U_(0.60) : f1 + U_(0.60);
                Vertical(r, new[] { Nivel(_terreo) }.Concat(faixa), sinal < 0 ? "apoios da escada (esquerda)" : "apoios da escada (direita)");
            }

            var feitos = new HashSet<string>();
            foreach (var (run, p) in lances)
            {
                Lance l = info[run];
                // vão do lance entre as faces dos apoios voltadas para ele
                if (l.Esq != null && l.Dir != null)
                {
                    var f0 = FacesVerticais(l.Esq.El).Where(f => f != null).OrderByDescending(f => f.Value.Item2).FirstOrDefault();
                    var f1 = FacesVerticais(l.Dir.El).Where(f => f != null).OrderBy(f => f.Value.Item2).FirstOrDefault();
                    string k = f0 == null || f1 == null ? "" : Math.Round(f0.Value.Item2, 2) + "|" + Math.Round(f1.Value.Item2, 2) + "|" + Math.Round(p.MaxU, 1);
                    if (f0 != null && f1 != null && feitos.Add(k)) Horizontal(p.MaxU + U_(0.30), new[] { f0, f1 }, "vão do lance");
                }
                if (l.Inicio == null) continue;
                // 3 espelhos: o patamar e os 3 degraus seguintes; 3 pisos: os 4 espelhos a partir da ponta do patamar
                var degraus = Faces(run).Where(f => f.Topo && f.Z >= l.Inicio.Z - U_(0.005)).OrderBy(f => f.Z).Take(4).ToList();
                double borda = l.Sobe > 0 ? l.Inicio.MaxR : l.Inicio.MinR;
                var espelhos = FacesVerticais(run).Where(f => f != null && l.Sobe * (f.Value.Item2 - borda) >= -U_(0.005))
                    .OrderBy(f => l.Sobe * (f.Value.Item2 - borda)).ToList();
                var quatro = new List<(Reference, double)?>();
                foreach (var f in espelhos)
                    if (quatro.Count < 4 && (quatro.Count == 0 || Math.Abs(f.Value.Item2 - quatro[quatro.Count - 1].Value.Item2) > U_(0.005))) quatro.Add(f);
                double rEsp = quatro.Count > 0 ? quatro[quatro.Count - 1].Value.Item2 + l.Sobe * U_(0.30) : borda + l.Sobe * U_(1.2);
                if (degraus.Count >= 4) Vertical(rEsp, degraus.Select(DeFace), "espelhos");
                if (quatro.Count >= 4) Horizontal(UDeZ(degraus.Count > 0 ? degraus[degraus.Count - 1].Z : l.Inicio.Z) + U_(0.30), quatro, "pisos");
            }

            // níveis com o início de cada lance (patamar): térreo → patamar → nível → …
            var cl = _niveis.Select(Nivel).ToList();
            foreach (var (run, _) in todos) cl.Add(DeFace((info.TryGetValue(run, out Lance x) ? x : Analisar(run, null)).Inicio));
            double e0 = todos.Min(l => l.P.MinR);
            Vertical(Math.Min(e0, apoiosEsq.Count > 0 ? apoiosEsq.Min(a => a.MinR) - U_(0.60) : e0) - U_(_c.PassoCadeiaM), cl, "níveis e lances");
        }

        /// <summary>Lance de escada lido das faces: início (patamar = a face de cima mais comprida da metade de baixo), fim (a face
        /// de cima mais alta) e o sentido em que sobe na vista. Os apoios são preenchidos por quem chama.</summary>
        private sealed class Lance
        {
            public FaceH Inicio, Fim;
            public int Sobe = 1;
            public Peca Esq, Dir;
        }

        private readonly Dictionary<ElementId, Lance> _analise = new Dictionary<ElementId, Lance>();

        /// <summary>Lê o lance (guardado: as cotas de nível usam o mesmo início). <paramref name="zMin"/> = o início não fica abaixo disto.</summary>
        private Lance Analisar(Element run, Peca p, double zMin = double.NaN)
        {
            if (double.IsNaN(zMin) && _analise.TryGetValue(run.Id, out Lance ja)) return ja;
            var l = _analise.TryGetValue(run.Id, out Lance velho) ? velho : new Lance();
            _analise[run.Id] = l;
            var topos = Faces(run).Where(f => f.Topo).OrderBy(f => f.Z).ToList();
            if (!double.IsNaN(zMin) && topos.Any(f => f.Z >= zMin)) topos = topos.Where(f => f.Z >= zMin).ToList();
            if (topos.Count == 0) return l;
            l.Fim = topos[topos.Count - 1];
            double meio = (topos[0].Z + l.Fim.Z) / 2;
            l.Inicio = topos.Where(f => f.Z <= meio + U_(0.005)).OrderByDescending(f => f.Comp).ThenBy(f => f.Z).First();
            l.Sobe = (l.Fim.MinR + l.Fim.MaxR) / 2 >= (l.Inicio.MinR + l.Inicio.MaxR) / 2 ? 1 : -1;
            return l;
        }

        private List<(Element Run, Peca P)> _lances;

        /// <summary>Lances das escadas da vista, com a caixa pela GEOMETRIA do lance (a caixa do elemento lance pode vir vazia).</summary>
        private List<(Element Run, Peca P)> Lances(List<Peca> vis)
        {
            if (_lances != null) return _lances;
            _lances = new List<(Element, Peca)>();
            // escada de família (peça pré-moldada na categoria Escadas): a própria peça é o lance
            foreach (Peca p in vis.Where(p => p.El is FamilyInstance && Cat(p, BuiltInCategory.OST_Stairs)))
                _lances.Add((p.El, p));
            foreach (Peca p in vis.Where(p => p.El is Stairs))
                foreach (ElementId id in ((Stairs)p.El).GetStairsRuns())
                {
                    if (!(_doc.GetElement(id) is StairsRun sr)) continue;
                    var pts = new List<(double R, double U, double D)>();
                    GeometryElement ge = sr.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
                    if (ge != null)
                        foreach (GeometryObject go in ge)
                            if (go is Solid so && so.Volume > 1e-9)
                            {
                                BoundingBoxXYZ b = so.GetBoundingBox();
                                foreach (double x in new[] { b.Min.X, b.Max.X }) foreach (double y in new[] { b.Min.Y, b.Max.Y }) foreach (double z in new[] { b.Min.Z, b.Max.Z })
                                            pts.Add(Local(b.Transform.OfPoint(new XYZ(x, y, z))));
                            }
                    if (pts.Count == 0) continue;
                    var q = new Peca { El = sr, MinR = pts.Min(x => x.R), MaxR = pts.Max(x => x.R), MinU = pts.Min(x => x.U), MaxU = pts.Max(x => x.U),
                                       MinD = pts.Min(x => x.D), MaxD = pts.Max(x => x.D) };
                    q.Cortada = q.MinD < _cortD - 1e-6 && q.MaxD > _cortD - 1e-6;
                    _lances.Add((sr, q));
                }
            return _lances;
        }

        // ------------------------------------------------------------------ cotas de nível

        private SpotDimensionType _tipoNivel;
        private bool _tipoNivelLido;

        private void CotasDeNivel(List<Peca> vis, TipoCorte tipo)
        {
            var alvos = new List<FaceH>();
            var cortadas = vis.Where(p => p.Cortada).ToList();
            if (tipo == TipoCorte.Escada)
            {
                // início (patamar) e fim de cada lance cortado (sem cortado, dos vistos)
                var ls = Lances(vis);
                foreach (var (sr, _) in ls.Any(l => l.P.Cortada) ? ls.Where(l => l.P.Cortada) : ls)
                {
                    Lance l = Analisar(sr, null);
                    alvos.Add(l.Inicio); alvos.Add(l.Fim);
                }
            }
            else
            {
                double gViz = _eixos.Count > 1 ? _eixos[1] : _eixos.Count == 1 ? _eixos[0] : double.NaN;
                IEnumerable<Peca> vigas = double.IsNaN(gViz) ? cortadas.Where(EhViga) : Em(cortadas.Where(EhViga), gViz, 0.5);
                alvos.AddRange(vigas.Select(p => Topo(p.El)));
                alvos.AddRange(cortadas.Where(EhBaldrame).Take(1).Select(p => Topo(p.El)));
                alvos.AddRange(vis.Where(p => Cat(p, BuiltInCategory.OST_StructuralFoundation)).OrderBy(p => double.IsNaN(gViz) ? 0 : Math.Abs(p.R - gViz)).Take(1).Select(p => Topo(p.El)));
                if (tipo == TipoCorte.CaixaElevador)
                {
                    var paredes = cortadas.Where(p => Cat(p, BuiltInCategory.OST_Walls)).ToList();
                    if (paredes.Count > 0)
                    {
                        alvos.Add(paredes.Select(p => Fundo(p.El)).Where(f => f != null).OrderBy(f => f.Z).FirstOrDefault());   // fundo do poço
                        double cx0 = paredes.Min(p => p.MinR), cx1 = paredes.Max(p => p.MaxR);
                        alvos.Add(cortadas.Where(p => p.El is Floor && p.MinR >= cx0 - U_(0.3) && p.MaxR <= cx1 + U_(0.3))
                            .Select(p => Topo(p.El)).Where(f => f != null).OrderByDescending(f => f.Z).FirstOrDefault());   // topo da laje da caixa
                    }
                }
            }
            int n = 0;
            foreach (FaceH f in alvos.Where(f => f != null).GroupBy(f => f.Ref.ConvertToStableRepresentation(_doc)).Select(g => g.First()))
                if (CotaDeNivel(f)) n++;
            _log.Info($"{_v.Name}: {n} cota(s) de nível.");
        }

        /// <summary>Cota de nível na face: o ponto tem de estar DENTRO da face (o topo do bloco com cálice tem furo no centro) e dentro do recorte.</summary>
        private bool CotaDeNivel(FaceH f)
        {
            if (!_tipoNivelLido)
            {
                _tipoNivelLido = true;
                string nome = _c.TipoCotaNivel?.Trim() ?? "";
                var tipos = new FilteredElementCollector(_doc).OfClass(typeof(SpotDimensionType)).Cast<SpotDimensionType>().Where(t => t.StyleType == DimensionStyleType.SpotElevation).ToList();
                _tipoNivel = tipos.FirstOrDefault(t => string.Equals(t.Name, nome, StringComparison.OrdinalIgnoreCase));
                if (_tipoNivel == null)
                {
                    // reserva: tipo próprio em metros com duas casas e sinal (o padrão do Revit sai "232.561304 cm")
                    _tipoNivel = tipos.FirstOrDefault(t => t.Name == NomeNivelReserva);
                    SpotDimensionType fonte = tipos.Where(t => !t.Name.StartsWith("SecretInternal", StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.Name).FirstOrDefault();
                    if (_tipoNivel == null && fonte != null)
                        try
                        {
                            _tipoNivel = (SpotDimensionType)fonte.Duplicate(NomeNivelReserva);
                            _tipoNivel.SetUnitsFormatOptions(new FormatOptions(UnitTypeId.Meters) { UseDefault = false, Accuracy = 0.01, UsePlusPrefix = true });
                        }
                        catch { _tipoNivel = null; }
                    _log.Warn($"Tipo de cota de nível \"{nome}\" não existe no projeto; usado \"{_tipoNivel?.Name ?? "o padrão"}\".");
                }
            }
            BoundingBoxUV bb = f.Face.GetBoundingBox();
            XYZ melhor = null;
            double melhorDist = double.MaxValue, centroR = (_minR + _maxR) / 2;
            for (int i = 1; i < 8; i++)
                for (int j = 1; j < 8; j++)
                {
                    var uv = new UV(bb.Min.U + (bb.Max.U - bb.Min.U) * i / 8, bb.Min.V + (bb.Max.V - bb.Min.V) * j / 8);
                    if (!f.Face.IsInside(uv)) continue;
                    XYZ p = f.T.OfPoint(f.Face.Evaluate(uv));
                    var l = Local(p);
                    if (l.R < _minR || l.R > _maxR) continue;
                    double dist = Math.Abs(l.D - _cortD) + 0.01 * Math.Abs(l.R - centroR);
                    if (dist < melhorDist) { melhorDist = dist; melhor = p; }
                }
            if (melhor == null) return false;
            try
            {
                var lp = Local(melhor);
                XYZ dobra = Mundo(lp.R + U_(0.30), lp.U + U_(0.30), _cortD), fim = Mundo(lp.R + U_(0.60), lp.U + U_(0.30), _cortD);
                SpotDimension sd = _doc.Create.NewSpotElevation(_v, f.Ref, melhor, dobra, fim, melhor, false);
                if (_tipoNivel != null && sd.GetTypeId() != _tipoNivel.Id) sd.ChangeTypeId(_tipoNivel.Id);
                MarcaFormas.Marcar(sd, PapelNivel);
                return true;
            }
            catch (Exception ex) { _log.Warn($"{_v.Name}: cota de nível não criada ({ex.Message})."); return false; }
        }

        // ------------------------------------------------------------------ nomes

        private void Nomes(List<Peca> vis)
        {
            // peças que já têm identificador nesta vista (feito à mão ou pelo comando) são puladas
            var marcadas = new HashSet<ElementId>(new FilteredElementCollector(_doc, _v.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>()
                .SelectMany(t => t.GetTaggedLocalElementIds()));
            var novos = new List<(IndependentTag Tag, Peca P, string Lado)>();
            Level primeiro = _niveis.FirstOrDefault(l => l.ProjectElevation > _terreo.ProjectElevation + 1e-6) ?? _terreo;
            double uPav = U(primeiro);
            double off = U_(_c.PilarAfastamentoM);

            void Criar(Peca p, string familia, BuiltInCategory cat, string lado, double r, double u)
            {
                if (marcadas.Contains(p.El.Id) || string.IsNullOrWhiteSpace(familia)) return;
                FamilySymbol s = Simbolo(familia, cat);
                if (s == null) return;
                try
                {
                    var t = IndependentTag.Create(_doc, s.Id, _v.Id, new Reference(p.El), false, TagOrientation.Horizontal, Mundo(r, u, _cortD));
                    MarcaFormas.Marcar(t, PrefixoId + lado);
                    marcadas.Add(p.El.Id);
                    novos.Add((t, p, lado));
                }
                catch (Exception ex) { _log.Warn($"{_v.Name}: identificador de {ModeloFormas.Marca(p.El)} não criado ({ex.Message})."); }
            }

            foreach (Peca p in vis)
            {
                string marca = ModeloFormas.Marca(p.El);
                if (marca == "") continue;
                if (Cat(p, BuiltInCategory.OST_StructuralColumns) && p.El is FamilyInstance fc && ModeloFormas.Passa(fc, _cfg.FiltroPilares))
                    Criar(p, _c.FamiliaPilar, BuiltInCategory.OST_StructuralColumnTags, "pilar", p.MaxR + off, uPav);
                else if (EhViga(p))
                    Criar(p, _c.FamiliaViga, BuiltInCategory.OST_StructuralFramingTags, p.Cortada ? "viga-cortada" : "viga-vista", p.Cortada ? p.MinR : (Math.Max(p.MinR, _minR) + Math.Min(p.MaxR, _maxR)) / 2, p.MinU);
                else if (p.El is Floor && p.Cortada && Tools.RenumberSlabsCommand.ForaDaNumeracao(_doc, p.El, "L") == null)
                {
                    double r0 = Math.Max(p.MinR, _minR), r1 = Math.Min(p.MaxR, _maxR);
                    Criar(p, _c.FamiliaLaje, BuiltInCategory.OST_FloorTags, "laje", (r0 + r1) / 2, (p.MinU + p.MaxU) / 2);
                }
                else if (Cat(p, BuiltInCategory.OST_Walls))
                    Criar(p, _c.FamiliaParede, BuiltInCategory.OST_WallTags, "parede", p.R, (p.MinU + p.MaxU) / 2);
            }
            foreach (var (sr, pr) in Lances(vis))
                if (ModeloFormas.Marca(sr) != "")
                {
                    Criar(pr, _c.FamiliaEscada, BuiltInCategory.OST_StairsRunTags, "escada", pr.R, (pr.MinU + pr.MaxU) / 2);
                    if (!marcadas.Contains(sr.Id)) Criar(pr, _c.FamiliaEscada, BuiltInCategory.OST_StairsTags, "escada", pr.R, (pr.MinU + pr.MaxU) / 2);
                }
            if (novos.Count == 0) return;

            // posição final pela caixa real do texto
            _doc.Regenerate();
            double viga = U_(_c.VigaAbaixoM), g = U_(0.05);
            foreach (var (t, p, lado) in novos)
            {
                try
                {
                    var b = PlantaFormas.CaixaDe(t.get_BoundingBox(_v), _v);
                    if (b == null) continue;
                    double tr = (b.Value.MinR + b.Value.MaxR) / 2, tu = (b.Value.MinU + b.Value.MaxU) / 2;
                    // caixa do texto na vista → para o sistema do recorte (as duas usam a direita/para cima da vista)
                    // só o trecho dentro do recorte (viga vista que passa do recorte tinha o nome fora do desenho)
                    double cr0 = Math.Max(p.MinR, _minR), cr1 = Math.Min(p.MaxR, _maxR);
                    if (cr1 < cr0) { cr0 = cr1 = Math.Max(_minR, Math.Min(_maxR, p.R)); }
                    double pr0 = Mundo(cr0, 0, _cortD).DotProduct(_v.RightDirection), pr1 = Mundo(cr1, 0, _cortD).DotProduct(_v.RightDirection);
                    double pu0 = Mundo(0, p.MinU, _cortD).DotProduct(_v.UpDirection), pu1 = Mundo(0, p.MaxU, _cortD).DotProduct(_v.UpDirection);
                    double prMin = Math.Min(pr0, pr1), prMax = Math.Max(pr0, pr1), puMin = Math.Min(pu0, pu1), puMax = Math.Max(pu0, pu1);
                    double dr = 0, du = 0;
                    switch (lado)
                    {
                        case "pilar": dr = prMax + off - b.Value.MinR; du = Mundo(0, uPav, _cortD).DotProduct(_v.UpDirection) + g - b.Value.MinU; break;
                        case "viga-cortada": dr = prMin - g - b.Value.MaxR; du = puMin - b.Value.MinU; break;
                        case "viga-vista": dr = (prMin + prMax) / 2 - tr; du = puMin - viga - b.Value.MaxU; break;
                        default: dr = (prMin + prMax) / 2 - tr; du = (puMin + puMax) / 2 - tu; break;
                    }
                    if (lado == "laje") dr = 0;   // já nasce no meio da parte cortada (dentro do recorte)
                    t.TagHeadPosition = t.TagHeadPosition + _v.RightDirection * dr + _v.UpDirection * du;
                }
                catch { /* fica onde nasceu */ }
            }
            _log.Info($"{_v.Name}: {novos.Count} identificador(es).");
        }

        private readonly Dictionary<string, FamilySymbol> _simbolos = new Dictionary<string, FamilySymbol>();

        /// <summary>Tipo do identificador: família pelo nome (exato; senão a que começa com ele), tipo "N" (senão o 1º); sem a família,
        /// qualquer identificador da categoria (com aviso).</summary>
        private FamilySymbol Simbolo(string familia, BuiltInCategory cat)
        {
            string k = familia + "|" + cat;
            if (_simbolos.TryGetValue(k, out var s)) return s;
            List<FamilySymbol> Todos() => new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)).OfCategory(cat).Cast<FamilySymbol>().ToList();
            var todos = Todos();
            string raiz0 = familia.Split('.')[0].Trim();
            if (!todos.Any(x => x.FamilyName.StartsWith(raiz0, StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(_c.PastaFamilias))
            {
                // família fora do projeto: carregada da pasta da biblioteca (configuração local), "<nome>.rfa" ou "<nome sem sufixo>.rfa"
                foreach (string arq in new[] { familia + ".rfa", raiz0 + ".rfa" }.Select(n => System.IO.Path.Combine(_c.PastaFamilias.Trim(), n)).Distinct())
                {
                    if (!System.IO.File.Exists(arq)) continue;
                    try { if (_doc.LoadFamily(arq, new SheetComposer.OverwriteNothing(), out Family _)) _log.Info($"Família carregada de {arq}."); } catch { }
                    todos = Todos();
                    break;
                }
            }
            var fam = todos.Where(x => string.Equals(x.FamilyName, familia, StringComparison.OrdinalIgnoreCase)).ToList();
            if (fam.Count == 0)
            {
                string raiz = familia.Split('.')[0].Trim();
                fam = todos.Where(x => x.FamilyName.StartsWith(raiz, StringComparison.OrdinalIgnoreCase)).ToList();
                if (fam.Count > 0) _log.Info($"Identificador \"{familia}\" não está no projeto; usado \"{fam[0].FamilyName}\".");
            }
            s = fam.FirstOrDefault(x => string.Equals(x.Name, _c.TipoNome, StringComparison.OrdinalIgnoreCase)) ?? fam.FirstOrDefault();
            if (s == null && todos.Count > 0 && cat != BuiltInCategory.OST_StairsTags)
            {
                s = todos[0];
                _log.Warn($"Identificador \"{familia}\" não está no projeto; usado \"{s.FamilyName} : {s.Name}\".");
            }
            if (s != null && !s.IsActive) { s.Activate(); _doc.Regenerate(); }
            _simbolos[k] = s;
            return s;
        }
    }
}
