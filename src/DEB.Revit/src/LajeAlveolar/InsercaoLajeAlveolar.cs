using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DEB.Core;
using DEB.Core.Lajes;
using static DEB.Revit.LajeAlveolar.GeometriaRevit;

namespace DEB.Revit.LajeAlveolar
{
    /// <summary>Tudo o que a inserção e as etapas seguintes (complementos) compartilham.</summary>
    internal sealed class ContextoLajeAlveolar
    {
        public Document Doc;
        public ConfigPaginacao Cfg;
        public List<PanoPaginado> Panos = new List<PanoPaginado>();
        /// <summary>Piso do modelo de cada pano (id do piso → Floor).</summary>
        public Dictionary<string, Floor> Pisos = new Dictionary<string, Floor>();
        /// <summary>Elementos do comando no modelo, por marca (os criados agora e os que já existiam).</summary>
        public Dictionary<string, ElementId> PorMarca = new Dictionary<string, ElementId>();
        public HashSet<string> Existentes = new HashSet<string>();
        public List<string> Avisos = new List<string>();
        public List<string> Erros = new List<string>();
        public StringBuilder Log = new StringBuilder();
        public HashSet<string> SemParametroEtapa = new HashSet<string>();
        /// <summary>Perfil medido de cada junta (marca da junta → perfil), compartilhado por chaves e tirantes.</summary>
        public Dictionary<string, PerfilJunta> Perfis = new Dictionary<string, PerfilJunta>();
        /// <summary>Tirantes que pedem furo em obra (peças vizinhas da junta).</summary>
        public List<(string PecaA, string PecaB)> TirantesComFuro = new List<(string, string)>();
        public int Criados;

        public Floor PisoDe(PanoPaginado p) => Pisos.TryGetValue(p.Piso.Id, out var f) ? f : null;

        /// <summary>Marca, comentário de controle, etapa (parâmetro por nome; null = não grava), fase e subprojeto do piso de origem.</summary>
        public void Comum(Element e, string marca, string descricao, int? etapa, Floor origem)
        {
            e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.Set(marca);
            e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(ExtracaoLajeAlveolar.Prefixo + marca + " (" + descricao + ")");
            if (etapa.HasValue) GravarEtapa(e, etapa.Value);
            if (origem != null)
            {
                try
                {
                    var fase = origem.get_Parameter(BuiltInParameter.PHASE_CREATED)?.AsElementId();
                    var pf = e.get_Parameter(BuiltInParameter.PHASE_CREATED);
                    if (fase != null && fase != ElementId.InvalidElementId && pf != null && !pf.IsReadOnly) pf.Set(fase);
                }
                catch { /* fase incompatível: fica a padrão */ }
                if (Doc.IsWorkshared)
                {
                    var pw = e.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                    if (pw != null && !pw.IsReadOnly) try { pw.Set(origem.WorksetId.IntegerValue); } catch { }
                }
            }
        }

        public bool GravarEtapa(Element e, int etapa)
        {
            string nome = Cfg.Insercao.ParametroEtapa;
            if (string.IsNullOrWhiteSpace(nome)) return false;
            var p = e.LookupParameter(nome);
            if (p == null || p.IsReadOnly) { SemParametroEtapa.Add(e.Category?.Name ?? "?"); return false; }
            switch (p.StorageType)
            {
                case StorageType.Integer: return p.Set(etapa);
                case StorageType.Double: return p.Set((double)etapa);
                case StorageType.String: return p.Set(etapa.ToString(CultureInfo.InvariantCulture));
                default: return false;
            }
        }
    }

    /// <summary>Ponto de extensão: etapas depois da inserção dos painéis (fase 3: capa, chaves, tirantes; outras no futuro).
    /// Rodam na mesma transação, cada uma numa subtransação (falha de uma não desfaz os painéis).</summary>
    internal interface IComplementoLajeAlveolar
    {
        string Nome { get; }
        bool Ativo(ConfigPaginacao cfg);
        void Aplicar(ContextoLajeAlveolar ctx);
    }

    /// <summary>
    /// Inserção dos painéis (família de quadro estrutural escolhida pelo usuário) e das faixas maciças (piso). Port do protótipo:
    /// linha do painel no topo do painel (topo do piso − capa), justificação y = centro, z = topo; folgas da família zeradas; a caixa do
    /// sólido é medida e os offsets y/z corrigidos; largura de ajuste por recorte lateral (ou tipo por largura); recortes de abertura e de
    /// pilar por abertura estrutural (NewOpening, CenterZ); marca, etapa e comentário "LAJE ALVEOLAR &lt;marca&gt;" (o que já existe é pulado).
    /// </summary>
    internal static class InsercaoLajeAlveolar
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void Inserir(ContextoLajeAlveolar ctx, FamilySymbol simboloBase)
        {
            var doc = ctx.Doc; var cfg = ctx.Cfg;
            if (!simboloBase.IsActive) { simboloBase.Activate(); doc.Regenerate(); }
            var tipos = new Dictionary<string, FamilySymbol>();
            int recOk = 0; double piorDesvio = 0; string sPior = "";
            foreach (var pano in ctx.Panos.Where(p => p.Erro == null))
            {
                var piso = ctx.PisoDe(pano);
                var nivel = piso != null ? doc.GetElement(piso.LevelId) as Level : null;
                if (nivel == null) { ctx.Erros.Add($"pano {pano.Numero}: piso sem nível"); continue; }
                foreach (var s in pano.Pecas)
                {
                    if (ctx.Existentes.Contains(s.Marca)) continue;
                    try
                    {
                        var pc = s.Peca;
                        var sim = Simbolo(ctx, simboloBase, pc.Largura, tipos);
                        var a = P(s.EixoIni.X, s.EixoIni.Y, pano.ZTopoPainel); var b = P(s.EixoFim.X, s.EixoFim.Y, pano.ZTopoPainel);
                        var fi = doc.Create.NewFamilyInstance(Line.CreateBound(a, b), sim, nivel, StructuralType.Beam);
                        StructuralFramingUtils.DisallowJoinAtEnd(fi, 0);
                        StructuralFramingUtils.DisallowJoinAtEnd(fi, 1);
                        fi.get_Parameter(BuiltInParameter.Y_JUSTIFICATION)?.Set((int)YJustification.Center);
                        fi.get_Parameter(BuiltInParameter.Z_JUSTIFICATION)?.Set((int)ZJustification.Top);
                        fi.get_Parameter(BuiltInParameter.Y_OFFSET_VALUE)?.Set(0.0);
                        fi.get_Parameter(BuiltInParameter.Z_OFFSET_VALUE)?.Set(0.0);
                        foreach (var nome in cfg.Insercao.ParametrosFolga ?? new List<string>())
                        {
                            var pf = fi.LookupParameter(nome);
                            if (pf != null && !pf.IsReadOnly && pf.StorageType == StorageType.Double) pf.Set(0.0);
                        }
                        ctx.Comum(fi, s.Marca, $"{s.Tipo}, piso {pano.Piso.Id}", cfg.Etapas.Paineis, piso);
                        doc.Regenerate();
                        foreach (var j in JoinGeometryUtils.GetJoinedElements(doc, fi).ToList())
                            try { JoinGeometryUtils.UnjoinGeometry(doc, fi, doc.GetElement(j)); } catch { }
                        Ajustar(doc, fi, pc, pano);
                        CortarExcesso(ctx, fi, s, pano);
                        double d = Desvio(fi, pc, pano);
                        if (d > piorDesvio) { piorDesvio = d; sPior = s.Marca; }
                        foreach (var r in pc.Recortes)
                        {
                            if (Math.Min(r.Ret.LarguraU, r.Ret.LarguraV) < cfg.RecorteMin) continue;
                            if (Recortar(ctx, fi, s, r, pano)) recOk++;
                            else ctx.Avisos.Add($"{s.Marca}: recorte de {r.Tipo} não aplicado (fazer à mão) — ver relatório");
                        }
                        ctx.PorMarca[s.Marca] = fi.Id;
                        ctx.Criados++;
                    }
                    catch (Exception ex) { ctx.Erros.Add($"{s.Marca}: {ex.Message}"); }
                }
                foreach (var m in pano.Macicas)
                {
                    if (ctx.Existentes.Contains(m.Marca)) continue;
                    try
                    {
                        var ft = TipoPisoUnico(ctx, piso.FloorType, "FAIXA MACIÇA h" + Fmt.Virgula(cfg.AlturaPainel * 100, "0.#"), cfg.AlturaPainel);
                        var fl = Floor.Create(doc, new List<CurveLoop> { LacoRet(pano.Sistema, m.Faixa.Ret, 0) }, ft.Id, nivel.Id);
                        fl.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.Set(Ft(pano.ZTopoPainel) - nivel.ProjectElevation);
                        ctx.Comum(fl, m.Marca, $"faixa maciça, piso {pano.Piso.Id}", cfg.Etapas.ChavesEFaixasMacicas, piso);
                        ctx.PorMarca[m.Marca] = fl.Id;
                        ctx.Criados++;
                    }
                    catch (Exception ex) { ctx.Erros.Add($"{m.Marca}: {ex.Message}"); }
                }
            }
            ctx.Log.AppendLine($"recortes aplicados: {recOk}");
            ctx.Log.AppendLine($"maior desvio da caixa do painel em relação à paginação (antes dos recortes): {Fmt.N(piorDesvio * 1000, "0.0")} mm {sPior}");
        }

        /// <summary>Tipo do painel: o escolhido; com "criar tipo por largura", um tipo por largura de ajuste.</summary>
        private static FamilySymbol Simbolo(ContextoLajeAlveolar ctx, FamilySymbol baseS, double largura, Dictionary<string, FamilySymbol> cache)
        {
            var ins = ctx.Cfg.Insercao;
            if (!ins.CriarTipoPorLargura || Math.Abs(largura - ctx.Cfg.LarguraPainel) < 1e-4) return baseS;
            string nome = baseS.Name + " b=" + Fmt.N(largura * 100, "0.0");
            if (cache.TryGetValue(nome, out var c)) return c;
            var s = new FilteredElementCollector(ctx.Doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .FirstOrDefault(x => x.Family.Id == baseS.Family.Id && x.Name == nome);
            if (s == null)
            {
                s = (FamilySymbol)baseS.Duplicate(nome);
                var p = s.LookupParameter(ins.ParametroLargura ?? "");
                if (p == null || p.IsReadOnly || p.StorageType != StorageType.Double)
                {
                    ctx.Avisos.Add($"a família não tem o parâmetro de tipo \"{ins.ParametroLargura}\": larguras de ajuste por recorte lateral");
                    ins.CriarTipoPorLargura = false;
                    ctx.Doc.Delete(s.Id);
                    return baseS;
                }
                p.Set(Ft(largura));
                ctx.Log.AppendLine("tipo novo " + nome);
            }
            if (!s.IsActive) { s.Activate(); ctx.Doc.Regenerate(); }
            cache[nome] = s;
            return s;
        }

        /// <summary>Tipo de piso de camada única (faixa maciça, capa) a partir do tipo do piso de origem (mesmo material).</summary>
        public static FloorType TipoPisoUnico(ContextoLajeAlveolar ctx, FloorType refT, string nome, double esp)
        {
            var doc = ctx.Doc;
            var ft = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().FirstOrDefault(t => t.Name == nome);
            var csr = refT.GetCompoundStructure();
            ElementId mat = ElementId.InvalidElementId;
            if (csr != null && csr.LayerCount > 0)
            {
                int i = csr.StructuralMaterialIndex >= 0 && csr.StructuralMaterialIndex < csr.LayerCount ? csr.StructuralMaterialIndex : 0;
                mat = csr.GetLayers()[i].MaterialId;
            }
            if (ft == null) { ft = (FloorType)refT.Duplicate(nome); ctx.Log.AppendLine("tipo de piso novo " + nome); }
            var cs = ft.GetCompoundStructure();
            if (cs != null && (cs.LayerCount != 1 || Math.Abs(cs.GetWidth() - Ft(esp)) > 1e-6))
            {
                // camada única: troca as camadas da estrutura do próprio tipo (CreateSingleLayerCompoundStructure recusa a condição de borda do piso)
                cs.SetNumberOfShellLayers(ShellLayerType.Exterior, 0);
                cs.SetNumberOfShellLayers(ShellLayerType.Interior, 0);
                cs.SetLayers(new List<CompoundStructureLayer> { new CompoundStructureLayer(Ft(esp), MaterialFunctionAssignment.Structure, mat) });
                cs.StructuralMaterialIndex = 0;
                ft.SetCompoundStructure(cs);
            }
            return ft;
        }

        // ---------- posição: mede a caixa do sólido e corrige os offsets y/z (a origem da seção da família não é conhecida)

        private static (double Dv, double Dz) Erro(FamilyInstance fi, PecaPainel pc, PanoPaginado pano)
        {
            var (r, _, z1) = CaixaNoPano(fi, pano.Sistema);
            return ((r.V0 + r.V1) / 2 - (pc.V0 + pc.V1) / 2, z1 - pano.ZTopoPainel);
        }

        private static void Ajustar(Document doc, FamilyInstance fi, PecaPainel pc, PanoPaginado pano)
        {
            var py = fi.get_Parameter(BuiltInParameter.Y_OFFSET_VALUE); var pz = fi.get_Parameter(BuiltInParameter.Z_OFFSET_VALUE);
            if (py == null || pz == null) return;
            var (l0, z0) = Erro(fi, pc, pano);
            if (Math.Abs(l0) < 0.0005 && Math.Abs(z0) < 0.0005) return;
            double h = 0.01;   // 10 mm de teste para descobrir o sinal de cada offset
            py.Set(Ft(h)); pz.Set(Ft(h)); doc.Regenerate();
            var (l1, z1) = Erro(fi, pc, pano);
            double ky = (l1 - l0) / h, kz = (z1 - z0) / h;   // ±1 esperado
            double oy = Math.Abs(ky) > 0.5 ? -l0 / ky : 0, oz = Math.Abs(kz) > 0.5 ? -z0 / kz : 0;
            py.Set(Ft(oy)); pz.Set(Ft(oz)); doc.Regenerate();
        }

        private static double Desvio(FamilyInstance fi, PecaPainel pc, PanoPaginado pano)
        {
            var (r, _, z1) = CaixaNoPano(fi, pano.Sistema);
            return new[] { r.U0 - pc.U0, r.V0 - pc.V0, r.U1 - pc.U1, r.V1 - pc.V1, z1 - pano.ZTopoPainel }.Max(Math.Abs);
        }

        /// <summary>Sólido mais largo que a peça (ajuste com o tipo inteiro): corta as sobras laterais em todo o comprimento.</summary>
        private static void CortarExcesso(ContextoLajeAlveolar ctx, FamilyInstance fi, PecaSaida s, PanoPaginado pano)
        {
            var pc = s.Peca;
            var (c, _, _) = CaixaNoPano(fi, pano.Sistema);
            double e = 0.02, tol = 0.001;
            var faixas = new List<RetUV>();
            if (pc.V0 - c.V0 > tol) faixas.Add(new RetUV(pc.U0 - e, c.V0 - e, pc.U1 + e, pc.V0));
            if (c.V1 - pc.V1 > tol) faixas.Add(new RetUV(pc.U0 - e, pc.V1, pc.U1 + e, c.V1 + e));
            foreach (var r in faixas)
                if (!Abrir(ctx, fi, s.Marca, r, pano, "excesso de largura", 0))
                    ctx.Avisos.Add($"{s.Marca}: excesso de largura não cortado (conferir)");
        }

        private static bool Recortar(ContextoLajeAlveolar ctx, FamilyInstance fi, PecaSaida s, RecortePeca r, PanoPaginado pano)
        {
            // o lado do recorte que coincide com a borda da peça é estendido 2 cm para fora, para o corte sair limpo
            var pc = s.Peca; double e = 0.02, tol = 1e-4;
            var x = new RetUV(r.Ret.U0 - (Math.Abs(r.Ret.U0 - pc.U0) < tol ? e : 0), r.Ret.V0 - (Math.Abs(r.Ret.V0 - pc.V0) < tol ? e : 0),
                              r.Ret.U1 + (Math.Abs(r.Ret.U1 - pc.U1) < tol ? e : 0), r.Ret.V1 + (Math.Abs(r.Ret.V1 - pc.V1) < tol ? e : 0));
            double esperado = r.Ret.LarguraU * r.Ret.LarguraV * ctx.Cfg.AlturaPainel;
            return Abrir(ctx, fi, s.Marca, x, pano, "recorte " + r.Tipo, 0.2 * esperado);
        }

        /// <summary>Abertura estrutural vertical no painel (perfil no plano médio). Confere pelo volume; senão desfaz.</summary>
        private static bool Abrir(ContextoLajeAlveolar ctx, FamilyInstance fi, string marca, RetUV r, PanoPaginado pano, string oque, double volMin)
        {
            var doc = ctx.Doc;
            try
            {
                using (var st = new SubTransaction(doc))
                {
                    st.Start();
                    double v0 = Volume(fi);
                    var op = doc.Create.NewOpening(fi, PerfilRet(pano.Sistema, r, pano.ZTopoPainel - ctx.Cfg.AlturaPainel / 2), Autodesk.Revit.Creation.eRefFace.CenterZ);
                    if (op == null) { st.RollBack(); ctx.Erros.Add($"{marca} {oque}: NewOpening devolveu null"); return false; }
                    doc.Regenerate();
                    double dv = v0 - Volume(fi);
                    if (dv < Math.Max(1e-6, volMin)) { st.RollBack(); ctx.Erros.Add($"{marca} {oque}: a abertura não cortou (Δvol = {Fmt.N(dv, "0.0000")} m³)"); return false; }
                    st.Commit();
                }
                return true;
            }
            catch (Exception ex) { ctx.Erros.Add($"{marca} {oque}: {ex.Message}"); return false; }
        }
    }
}
