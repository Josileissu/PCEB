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
            int futuras = 0;
            _pecas = new List<Peca>();
            foreach (Element e in els)
            {
                if (e is FamilyInstance fi && fi.SuperComponent != null) continue;
                if (faseVista >= 0 && OrdemFase(e.CreatedPhaseId) > faseVista) { futuras++; continue; }
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
            if (futuras > 0) _log.Info($"{v.Name}: {futuras} peça(s) de fase posterior à da vista ignoradas.");

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
                                    r.Add(new FaceH { Ref = rf, Face = pf, T = gi.Transform, Z = gi.Transform.OfPoint(pf.Origin).Z, Topo = n.Z > 0 });
                                }
                    }
                    else if (go is Solid s && s.Volume > 1e-9)
                        foreach (Face f in s.Faces)
                            if (f is PlanarFace pf && f.Reference != null && Math.Abs(pf.FaceNormal.Z) > 0.999)
                                r.Add(new FaceH { Ref = f.Reference, Face = pf, Z = pf.Origin.Z, Topo = pf.FaceNormal.Z > 0 });
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
            _tipoCota = lin.FirstOrDefault(t => string.Equals(t.Name, nome, StringComparison.OrdinalIgnoreCase))
                        ?? new PlantaFormas(_doc, _cfg, new PieceLog("")).TipoDeCota();   // reserva: o tipo em cm do plugin
            if (_tipoCota == null || !string.Equals(_tipoCota.Name, nome, StringComparison.OrdinalIgnoreCase))
                _log.Warn($"Tipo de cota \"{nome}\" não existe no projeto; usado \"{_tipoCota?.Name ?? "o padrão do Revit"}\".");
            return _tipoCota;
        }

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

            // cadeia junto ao pilar do eixo (bloco, fundo e topo das vigas, topo do pilar)
            Peca pilar = vis.Where(p => Cat(p, BuiltInCategory.OST_StructuralColumns)).OrderBy(p => _eixos.Count == 0 ? 0 : _eixos.Min(g => Math.Abs(g - p.R))).FirstOrDefault();
            if (pilar != null)
            {
                double rp = (pilar.R < (cx0 + cx1) / 2 ? pilar.MinR : pilar.MaxR) + (pilar.R < (cx0 + cx1) / 2 ? -1 : 1) * U_(_c.FachadaAlemM / 2);
                var c = new List<RefZ?> { DeFace(Topo(pilar.El)) };
                foreach (Peca p in Em(vis, pilar.R, 0.40))
                    if (Cat(p, BuiltInCategory.OST_StructuralFoundation) || EhViga(p)) { c.Add(DeFace(Fundo(p.El))); c.Add(DeFace(Topo(p.El))); }
                Vertical(rp, c, "pilar do eixo");
            }

            // cadeia vertical por dentro da caixa: lajes de topo e do fundo, topo/base de cada porta, níveis
            double rc = (cx0 + cx1) / 2;
            var janelas = vis.Where(p => Cat(p, BuiltInCategory.OST_Windows) && p.El is FamilyInstance).ToList();
            var cin = new List<RefZ?>();
            foreach (Peca lj in cortadas.Where(p => p.El is Floor && p.MinR >= cx0 - U_(0.3) && p.MaxR <= cx1 + U_(0.3)))
            { cin.Add(DeFace(Topo(lj.El))); cin.Add(DeFace(Fundo(lj.El))); }
            foreach (Peca j in janelas)
            {
                var fi = (FamilyInstance)j.El;
                Reference rt = fi.GetReferences(FamilyInstanceReferenceType.Top).FirstOrDefault(), rb = fi.GetReferences(FamilyInstanceReferenceType.Bottom).FirstOrDefault();
                BoundingBoxXYZ bb = fi.get_BoundingBox(null);
                if (rt != null && bb != null) cin.Add(new RefZ(rt, bb.Max.Z));
                if (rb != null && bb != null) cin.Add(new RefZ(rb, bb.Min.Z));
                // altura de cada porta
                if (rt != null && rb != null && bb != null) Vertical(j.MaxR + U_(0.15), new RefZ?[] { new RefZ(rb, bb.Min.Z), new RefZ(rt, bb.Max.Z) }, "altura da porta");
            }
            cin.AddRange(_niveis.Select(Nivel));
            Vertical(rc, cin, "dentro da caixa");

            // em cada piso: face da parede → bordas da porta → face da parede
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
                foreach (Peca w in paredes.Where(w => j.MinU < w.MaxU && j.MaxU > w.MinU))
                    foreach (var f in FacesVerticais(w.El))
                        refs.Add(f);
                Horizontal(j.MaxU + U_(0.30), refs, "parede/porta");
            }
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
            var lances = Lances(vis).Where(l => l.P.MaxD >= _fundoD && l.P.MinD <= _cortD).ToList();
            if (lances.Count == 0) { _log.Warn($"{_v.Name}: corte de escada sem lances visíveis."); return; }
            double e0 = lances.Min(l => l.P.MinR), e1 = lances.Max(l => l.P.MaxR);
            var apoios = vis.Where(p => p.Cortada && EhViga(p) && p.Larg <= U_(_c.ApoioEscadaMaxM)).ToList();

            // apoios de cada lado: nível → topo das vigas de apoio
            foreach (var lado in new[] { apoios.Where(a => a.R <= (e0 + e1) / 2).ToList(), apoios.Where(a => a.R > (e0 + e1) / 2).ToList() })
            {
                if (lado.Count == 0) continue;
                double r = lado.Average(a => a.R) + (lado[0].R <= (e0 + e1) / 2 ? -1 : 1) * U_(0.60);
                Vertical(r, _niveis.Select(Nivel).Concat(lado.Select(a => DeFace(Topo(a.El)))), "apoios da escada");
            }

            foreach (var (run, p) in lances)
            {
                List<FaceH> hs = Faces(run).OrderBy(f => f.Z).ToList();
                var pisos = hs.Where(f => f.Topo).ToList();
                // vão do lance entre as faces dos apoios
                Peca a0 = apoios.Where(a => a.MaxR <= p.MinR + U_(0.30)).OrderByDescending(a => a.MaxR).FirstOrDefault();
                Peca a1 = apoios.Where(a => a.MinR >= p.MaxR - U_(0.30)).OrderBy(a => a.MinR).FirstOrDefault();
                if (a0 != null && a1 != null)
                {
                    var f0 = FacesVerticais(a0.El).Where(f => f.Value.Item2 >= a0.R).OrderBy(f => f.Value.Item2).FirstOrDefault();
                    var f1 = FacesVerticais(a1.El).Where(f => f.Value.Item2 <= a1.R).OrderByDescending(f => f.Value.Item2).FirstOrDefault();
                    if (f0 != null && f1 != null) Horizontal(p.MaxU + U_(0.40), new[] { f0, f1 }, "vão do lance");
                }
                // 3 espelhos (pisos consecutivos) e 3 pisos (faces verticais consecutivas)
                if (pisos.Count >= 4) Vertical(p.MinR - U_(0.30), pisos.Take(4).Select(DeFace), "espelhos");
                var risers = FacesVerticais(run).Select(f => f.Value).OrderBy(f => Math.Abs(f.Item2 - p.MinR)).Take(4).Select(f => ((Reference, double)?)f).ToList();
                if (risers.Count >= 4) Horizontal(p.MinU - U_(0.30), risers, "pisos");
            }

            // níveis com o início de cada lance
            var cl = _niveis.Select(Nivel).ToList();
            foreach (var (run, _) in lances) cl.Add(DeFace(Faces(run).Where(f => f.Topo).OrderBy(f => f.Z).FirstOrDefault()));
            Vertical(e0 - U_(1.0), cl, "níveis e lances");
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
                foreach (var (sr, _) in Lances(vis))
                {
                    var t = Faces(sr).Where(f => f.Topo).OrderBy(f => f.Z).ToList();
                    if (t.Count > 0) { alvos.Add(t[0]); alvos.Add(t[t.Count - 1]); }
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
                if (_tipoNivel == null) _log.Warn($"Tipo de cota de nível \"{nome}\" não existe no projeto; usado o padrão.");
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
                    Criar(p, _c.FamiliaViga, BuiltInCategory.OST_StructuralFramingTags, p.Cortada ? "viga-cortada" : "viga-vista", p.Cortada ? p.MinR : p.R, p.MinU);
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
                    double pr0 = Mundo(p.MinR, 0, _cortD).DotProduct(_v.RightDirection), pr1 = Mundo(p.MaxR, 0, _cortD).DotProduct(_v.RightDirection);
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
