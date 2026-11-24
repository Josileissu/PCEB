using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DEB.Core.Analise.Barras;
using DEB.Core.Analise.Objetos;
using DEB.Core.Configuracao.Projeto;
using DEB.Core.Elementos.Consolo;
using DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D;
using DEB.Core.Pecas;
using DEB.Revit.Core;
using DEB.Revit.Precast;
using DEB.Revit.Vigas;

namespace DEB.Revit.Analise
{
    /// <summary>
    /// Estrutura por objetos a partir do modelo do Revit (D46; o modelo de análise é do PCEB, o Revit só é lido):
    /// <list type="bullet">
    /// <item>pilares: todos os do levantamento (seção, embutimento no cálice, consolos);</item>
    /// <item>vigas apoiadas em consolos (família do consolo aninhada na viga): ligação por consolo, barra rígida + mola do aparelho;</item>
    /// <item>demais vigas (linha de locação reta): ligação direta ao pilar onde a ponta cai — monolítica (moldada no local) ou
    /// articulada (pré-moldada);</item>
    /// <item>pisos estruturais: laje em grelha, com as cargas PCEB_ por m² ou as da configuração.</item>
    /// </list>
    /// </summary>
    internal static class EstruturaRevit
    {
        private const double Cm = 30.48;   // pés → cm
        private static Vetor3 V(XYZ p) => new Vetor3(p.X * Cm, p.Y * Cm, p.Z * Cm);

        /// <summary>Parâmetros de carga das lajes (por nome; criados pelo usuário ou num passo futuro do plugin).</summary>
        public const string ParamLajeG = "PCEB_Carga permanente (kN/m²)", ParamLajeQ = "PCEB_Carga variável (kN/m²)";

        public static EstruturaObjetos Montar(Document doc, List<ColumnGroup> grupos, Dictionary<long, (GrupoViga G, MembroViga M)> porId,
                                              ConfiguracaoProjeto cfg, List<string> notas, out List<(Corbel K, LigacaoConsolo L)> ligacoes)
        {
            var e = new EstruturaObjetos();
            ligacoes = new List<(Corbel, LigacaoConsolo)>();
            double fck = cfg.Numero("materiais.fck");
            double alfaE = CalculoConsolo.FatorApoio(cfg.Texto("materiais.agregado"));
            var mat = MaterialBarra.Concreto(fck, alfaE <= 0 ? 1 : alfaE, cfg.Numero("materiais.pesoEspecifico"));

            // ---------------- pilares (todos)
            var pilares = new Dictionary<long, (PilarObjeto P, PrecastColumn Pc, ColumnGroup G)>();
            foreach (ColumnGroup g in grupos)
                foreach (PrecastColumn pc in g.Members)
                {
                    PieceInfo info = pc.Info;
                    var p = new PilarObjeto
                    {
                        Dono = new Dono { Tipo = TipoDono.Pilar, Id = pc.Id, Nome = $"{g.Mark} ({pc.Id})" },
                        Base = V(info.Origin + info.AxisX * (info.LengthX / 2) + info.AxisY * (info.LengthY / 2)),
                        Eixo = new Vetor3(info.AxisZ.X, info.AxisZ.Y, info.AxisZ.Z),
                        DirY = new Vetor3(info.AxisX.X, info.AxisX.Y, info.AxisX.Z),
                        Secao = SecaoPoligonal.Retangulo(info.LengthX * Cm, info.LengthY * Cm),
                        Comprimento = info.LengthZ * Cm, Embutimento = pc.EmbedmentMm / 10, Material = mat,
                    };
                    e.Pilares.Add(p);
                    pilares[pc.Instance.Id.Value] = (p, pc, g);
                }

            // ---------------- vigas sobre consolos
            var vigas = new Dictionary<long, (VigaObjeto V, MembroViga M, List<(Corbel K, double T)> Apoios)>();
            foreach (var (p, pc, g) in pilares.Values)
                foreach (Corbel k in pc.Corbels)
                {
                    if (!(k.Instance()?.SuperComponent is FamilyInstance fv) || !porId.TryGetValue(fv.Id.Value, out var gm)) continue;
                    BoundingBoxXYZ bb = k.Instance().get_BoundingBox(null);
                    if (bb == null) continue;
                    PieceInfo info = gm.M.Info;
                    double t = ((bb.Min + bb.Max) / 2 - info.Origin).DotProduct(info.AxisX);
                    if (!vigas.TryGetValue(fv.Id.Value, out var vv)) vigas[fv.Id.Value] = vv = (null, gm.M, new List<(Corbel, double)>());
                    vv.Apoios.Add((k, t));
                }
            foreach (var kv in vigas.ToList())
            {
                MembroViga m = kv.Value.M;
                PieceInfo info = m.Info;
                double lx = info.LengthX;
                var apoios = kv.Value.Apoios;
                double tI = apoios.Where(a => a.T < lx / 2).Select(a => a.T).DefaultIfEmpty(0.02 * lx).Average();
                double tJ = apoios.Where(a => a.T >= lx / 2).Select(a => a.T).DefaultIfEmpty(0.98 * lx).Average();
                XYZ Ponto(double t) => info.Origin + info.AxisX * t + info.AxisY * (info.LengthY / 2);
                SecaoPoligonal sec = m.Levantada?.Principal?.Secao ?? SecaoPoligonal.Retangulo(info.LengthY * Cm, info.LengthZ * Cm);
                string marca = porId[kv.Key].G.Marca;
                var v = new VigaObjeto
                {
                    Dono = new Dono { Tipo = TipoDono.Viga, Id = kv.Key.ToString(), Nome = $"{marca} ({kv.Key})" },
                    ApoioI = V(Ponto(tI)), ApoioJ = V(Ponto(tJ)), Secao = sec, Material = mat, G = m.Cargas.G, Q = m.Cargas.Q,
                };
                foreach (var c in m.Cargas.Concentradas) v.Concentradas.Add((c.X - tI * Cm, c.G, c.Q));
                e.Vigas.Add(v);
                vigas[kv.Key] = (v, m, apoios);
            }
            foreach (var (p, pc, g) in pilares.Values)
                foreach (Corbel k in pc.Corbels)
                {
                    if (!(k.Instance()?.SuperComponent is FamilyInstance fv) || !vigas.TryGetValue(fv.Id.Value, out var vv)) continue;
                    double t = vv.Apoios.First(a => a.K == k).T;
                    var l = new LigacaoConsolo
                    {
                        Dono = new Dono { Tipo = TipoDono.Ligacao, Id = k.Instance().Id.Value.ToString(), Nome = $"consolo {g.Mark}·{porId[fv.Id.Value].G.Marca}" },
                        Pilar = p, Viga = vv.V, PontaI = t < vv.M.Info.LengthX / 2, ZTopoConsolo = U_.ToMm(k.ZTop) / 10, Aparelho = k.Apoio,
                    };
                    try { if (g.Spec != null && g.Spec.CorbelRebar) l.Consolo = g.Spec.DadosCalculo(k); } catch { }
                    e.Ligacoes.Add(l);
                    ligacoes.Add((k, l));
                }

            // ---------------- demais vigas: linha de locação reta, ligadas ao pilar onde a ponta cai
            int semPilar = 0, curvas = 0, sobreViga = 0;
            var gerais = new List<(VigaObjeto V, bool PreMoldada)>();
            foreach (FamilyInstance fi in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming)
                         .WhereElementIsNotElementType().OfType<FamilyInstance>().Where(x => x.SuperComponent == null))
            {
                if (vigas.ContainsKey(fi.Id.Value)) continue;
                if (!(fi.Location is LocationCurve lc) || !(lc.Curve is Line linha)) { curvas++; continue; }
                BoundingBoxXYZ bb = fi.get_BoundingBox(null);
                if (bb == null) continue;
                SecaoPoligonal sec;
                if (porId.TryGetValue(fi.Id.Value, out var gm0) && gm0.M.Levantada?.Principal?.Secao != null) sec = gm0.M.Levantada.Principal.Secao;
                else
                {
                    try { var inf = PieceAnalyzer.Analyze(fi); sec = SecaoPoligonal.Retangulo(inf.LengthY * Cm, inf.LengthZ * Cm); }
                    catch { sec = SecaoPoligonal.Retangulo(20, (bb.Max.Z - bb.Min.Z) * Cm); }
                }
                double zb = bb.Min.Z;
                XYZ a = linha.GetEndPoint(0), b = linha.GetEndPoint(1);
                var cargas = ParametrosCargasViga.Ler(fi);
                string marca = fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
                var v = new VigaObjeto
                {
                    Dono = new Dono { Tipo = TipoDono.Viga, Id = fi.Id.Value.ToString(), Nome = $"{(string.IsNullOrEmpty(marca) ? "viga" : marca)} ({fi.Id.Value})" },
                    ApoioI = V(new XYZ(a.X, a.Y, zb)), ApoioJ = V(new XYZ(b.X, b.Y, zb)), Secao = sec, Material = mat, G = cargas.G, Q = cargas.Q,
                };
                foreach (var c in cargas.Concentradas) v.Concentradas.Add((c.X, c.G, c.Q));
                e.Vigas.Add(v);
                gerais.Add((v, fi.StructuralMaterialType == StructuralMaterialType.PrecastConcrete));
            }
            // pontas: pilar embaixo (ligação direta) ou outra viga (viga sobre viga)
            foreach (var (v, preMoldada) in gerais)
                foreach (bool pontaI in new[] { true, false })
                {
                    Vetor3 pt = pontaI ? v.ApoioI : v.ApoioJ;
                    var pil = e.Pilares.FirstOrDefault(pl =>
                    {
                        Vetor3 d = pt - pl.Base; double z = d.Ponto(pl.Eixo.Unitario);
                        return z > -2 && z < pl.Comprimento + 2 && Math.Abs(d.Ponto(pl.DirY)) <= pl.Secao.B / 2 + 2 && Math.Abs(d.Ponto(pl.DirZ)) <= pl.Secao.H / 2 + 2;
                    });
                    if (pil != null)
                    {
                        e.LigacoesDiretas.Add(new LigacaoPilarViga
                        {
                            Dono = new Dono { Tipo = TipoDono.Ligacao, Id = $"{v.Dono.Id}-{(pontaI ? "I" : "J")}", Nome = $"{pil.Dono.Nome.Split(' ')[0]}·{v.Dono.Nome.Split(' ')[0]} {(preMoldada ? "articulada" : "monolítica")}" },
                            Pilar = pil, Viga = v, PontaI = pontaI, Articulada = preMoldada,
                        });
                        continue;
                    }
                    // viga de suporte: a ponta cai sobre o eixo dela em planta, dentro da altura dela
                    Vetor3 eixoPt = pt + Vetor3.EixoZ * v.AlturaEixo;
                    VigaObjeto sup = null; double tSup = 0;
                    foreach (var w in e.Vigas)
                    {
                        if (w == v) continue;
                        Vetor3 dir = new Vetor3(w.Direcao.X, w.Direcao.Y, 0).Unitario, d = new Vetor3(pt.X - w.ApoioI.X, pt.Y - w.ApoioI.Y, 0);
                        double vaoPlanta = new Vetor3(w.ApoioJ.X - w.ApoioI.X, w.ApoioJ.Y - w.ApoioI.Y, 0).Modulo, t = d.Ponto(dir);
                        double dist = Math.Abs(d.Ponto(Vetor3.EixoZ.Vetorial(dir)));
                        double zb = w.ApoioI.Z + (w.ApoioJ.Z - w.ApoioI.Z) * Math.Max(0, Math.Min(1, t / Math.Max(1e-9, vaoPlanta)));
                        if (t < 1 || t > vaoPlanta - 1 || dist > w.Secao.B / 2 + v.Secao.B / 2 + 2 || eixoPt.Z < zb - 2 || eixoPt.Z > zb + w.Secao.H + 2) continue;
                        sup = w; tSup = t / Math.Max(1e-9, vaoPlanta) * w.Vao; break;
                    }
                    if (sup == null) { semPilar++; continue; }
                    e.LigacoesVigaViga.Add(new LigacaoVigaViga
                    {
                        Dono = new Dono { Tipo = TipoDono.Ligacao, Id = $"{v.Dono.Id}-{(pontaI ? "I" : "J")}", Nome = $"{v.Dono.Nome.Split(' ')[0]} sobre {sup.Dono.Nome.Split(' ')[0]}" },
                        Apoiada = v, PontaI = pontaI, Suporte = sup, T = tSup,
                    });
                    sobreViga++;
                }
            if (curvas > 0) notas.Add($"{curvas} vigas curvas ou sem linha de locação ficaram fora da análise.");
            if (sobreViga > 0) notas.Add($"{sobreViga} pontas de viga apoiadas em outra viga (ligação articulada).");
            if (semPilar > 0) notas.Add($"{semPilar} pontas de viga sem pilar nem viga embaixo: apoio articulado fixo.");

            // ---------------- lajes: pisos estruturais
            var pisos = new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>().ToList();
            var estruturais = pisos.Where(f => f.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL)?.AsInteger() == 1).ToList();
            if (estruturais.Count == 0 && pisos.Count > 0)
            {
                estruturais = pisos.Where(f => (f.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM)?.AsDouble() ?? 0) * Cm >= 8).ToList();
                if (estruturais.Count > 0) notas.Add("Nenhum piso marcado como estrutural: entram os pisos com 8 cm ou mais.");
            }
            foreach (Floor f in estruturais)
            {
                var la = Laje(f, mat, cfg, notas);
                if (la != null) e.Lajes.Add(la);
            }
            return e;
        }

        /// <summary>Laje em grelha de um piso: contorno da face de cima (maior laço), plano médio, cargas PCEB_ ou da configuração.</summary>
        private static LajeObjeto Laje(Floor f, MaterialBarra mat, ConfiguracaoProjeto cfg, List<string> notas)
        {
            double h = (f.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM)?.AsDouble() ?? 0) * Cm;
            if (h <= 0) return null;
            PlanarFace topo = null;
            GeometryElement geo = f.get_Geometry(new Options { ComputeReferences = false });
            foreach (GeometryObject go in geo)
                if (go is Solid so && so.Volume > 0)
                    foreach (Face fc in so.Faces)
                        if (fc is PlanarFace pf && pf.FaceNormal.Z > 0.99 && (topo == null || pf.Area > topo.Area)) topo = pf;
            if (topo == null) { notas.Add($"Piso {f.Id.Value}: sem face de cima plana (inclinado?) — fora da análise."); return null; }
            var lacos = topo.GetEdgesAsCurveLoops();
            List<XYZ> melhor = null; double areaMax = 0;
            foreach (CurveLoop lp in lacos)
            {
                var pts = new List<XYZ>();
                foreach (Curve c in lp) { var t = c.Tessellate(); for (int i = 0; i < t.Count - 1; i++) pts.Add(t[i]); }
                double area = 0;
                for (int i = 0; i < pts.Count; i++) { var p0 = pts[i]; var p1 = pts[(i + 1) % pts.Count]; area += p0.X * p1.Y - p1.X * p0.Y; }
                if (Math.Abs(area) > areaMax) { areaMax = Math.Abs(area); melhor = pts; }
            }
            if (melhor == null || melhor.Count < 3) return null;
            if (lacos.Count() > 1) notas.Add($"Piso {f.Id.Value}: aberturas ignoradas na grelha (só o contorno externo).");
            double Par(string nome, string chave)
            {
                Parameter p = f.LookupParameter(nome);
                return p != null && p.StorageType == StorageType.Double && p.HasValue ? p.AsDouble() : cfg.Numero(chave);
            }
            string marca = f.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
            var la = new LajeObjeto
            {
                Dono = new Dono { Tipo = TipoDono.Laje, Id = f.Id.Value.ToString(), Nome = $"{(string.IsNullOrEmpty(marca) ? "laje" : marca)} ({f.Id.Value})" },
                Espessura = h, Material = mat, PesoEspecifico = cfg.Numero("materiais.pesoEspecifico"),
                G = Par(ParamLajeG, "acoes.lajeG"), Q = Par(ParamLajeQ, "acoes.lajeQ"), Malha = cfg.Numero("analise.malhaLaje"),
            };
            la.Contorno.AddRange(melhor.Select(p => V(p) + new Vetor3(0, 0, -h / 2)));
            return la;
        }
    }
}
