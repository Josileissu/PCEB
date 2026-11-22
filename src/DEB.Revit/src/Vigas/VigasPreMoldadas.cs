using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DEB.Core.Configuracao;
using DEB.Core.Documentacao;
using DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D;
using DEB.Core.Elementos.Viga.PreMoldada;
using DEB.Core.Elementos.Viga.PreMoldada.Calculo;
using DEB.Core.Elementos.Viga.PreMoldada.Modelo3D;
using DEB.Core.Intercambio;
using DEB.Core.Motor;
using DEB.Core.Normas.Verificacao;
using DEB.Core.Pecas;
using DEB.Revit.Core;
using DEB.Revit.Precast;
using DEB.Revit.Presentation;
using Fmt = DEB.Core.Fmt;

namespace DEB.Revit.Vigas
{
    /// <summary>Uma viga do modelo dentro de um grupo de peças iguais.</summary>
    internal sealed class MembroViga
    {
        public FamilyInstance Instance { get; set; }
        public PieceInfo Info { get; set; }
        public PecaLevantada Levantada { get; set; }
        /// <summary>0 ou 180: a peça está girada em relação à representante do grupo.</summary>
        public int Giro { get; set; }
        public CargasViga Cargas { get; set; } = new CargasViga();
        public long Id => Instance.Id.Value;
    }

    /// <summary>Grupo de vigas pré-moldadas iguais: geometria (do levantamento), cargas, cálculo e proposta de armadura.</summary>
    internal sealed class GrupoViga
    {
        public string Marca { get; set; } = "";
        public VigaPeca Peca { get; set; }
        public List<MembroViga> Membros { get; } = new List<MembroViga>();
        /// <summary>Valores do template da viga com a geometria do modelo e o padrão do escritório (antes do cálculo).</summary>
        public ValoresTemplate Base { get; set; }
        /// <summary>Alma da representante no sistema local dela (cm): início na largura e fundo.</summary>
        public double AlmaY0 { get; set; }
        public double AlmaZ0 { get; set; }
        public List<string> Avisos { get; } = new List<string>();
        public bool Selecionado { get; set; } = true;
        public CargasViga Cargas { get; set; } = new CargasViga();
        public ResultadoCalculoViga Calculo { get; set; }
        public ValoresTemplate Proposta => Calculo?.Proposta ?? Base;
        public MembroViga Representante => Membros[0];
        public int Quantidade => Membros.Count;
        public string Secao => Peca == null ? "" : $"{Fmt.Virgula(Peca.B, "0.#")} x {Fmt.Virgula(Peca.H, "0.#")}";
        public string Pontas => Peca == null ? "" : $"{(Peca.DenteE != null ? "dente" : "reta")} / {(Peca.DenteD != null ? "dente" : "reta")}";
        public SituacaoVerificacao Situacao => Calculo == null ? SituacaoVerificacao.Conferir : Calculo.Verificacoes.Geral;
    }

    /// <summary>
    /// "Vigas pré-moldadas" (D40, 22/11/2026): levanta as vigas (o mesmo levantamento da conferência e da exportação), agrupa as
    /// iguais, lê as cargas digitadas (parâmetros <see cref="ParametrosCargasViga"/>), calcula no núcleo
    /// (<see cref="CalculoViga"/>), propõe a armadura e, depois do aceite na janela (D26), arma as peças no Revit pela geometria do
    /// núcleo (<see cref="VigaRebarLayout"/>), cria a montagem, as vistas (elevação, planta, cortes, 3D; D29: sem montar folha) e
    /// as tabelas de aço, e grava o relatório de cálculo e verificação do grupo (D37).
    /// </summary>
    internal static class VigasPreMoldadas
    {
        /// <summary>Padrão do escritório para as vigas: os valores de partida do template (os mesmos do CAD).</summary>
        public static ValoresTemplate PadraoEscritorio() => new ValoresTemplate(new TemplateVigaPreMoldada().Parametros);

        public static List<GrupoViga> Levantar(Document doc, List<string> notas, ValoresTemplate padrao = null)
        {
            padrao ??= PadraoEscritorio();
            List<PecaLevantada> pecas = PieceReview.Run(doc, notas);
            ArquivoPecas arq = PrecastExport.Build(doc, pecas, new List<string>());
            notas.AddRange(arq.Notas.Where(n => n.StartsWith("Viga", StringComparison.OrdinalIgnoreCase) || n.Contains("vigas")));
            var porId = pecas.Where(p => p.Tipo == TipoPeca.Viga).ToDictionary(p => p.Id);

            // dentes do modelo que faltam no catálogo: entram com a armadura padrão (opção b do usuário, 2026-11-09) — o cálculo confere
            var catalogo = TipoDente.Padrao();
            var novos = CatalogoAutomatico.Dentes(arq.Vigas, catalogo);
            catalogo.AddRange(novos);
            TemplateVigaPreMoldada.CatalogoDentes = catalogo;
            if (novos.Count > 0) notas.Add($"Dentes criados com a armadura padrão (conferir no cálculo): {string.Join(", ", novos.Select(t => t.Nome))}.");

            var grupos = new List<GrupoViga>();
            foreach (VigaPeca vp in arq.Vigas)
            {
                var g = new GrupoViga { Marca = vp.Marca, Peca = vp };
                foreach (ElementoPeca el in vp.Elementos)
                {
                    if (!long.TryParse(el.Id, out long id) || !(doc.GetElement(new ElementId(id)) is FamilyInstance fi)) continue;
                    porId.TryGetValue(el.Id, out PecaLevantada lev);
                    var erros = new List<string>();
                    var m = new MembroViga { Instance = fi, Info = PieceAnalyzer.Analyze(fi), Levantada = lev, Giro = el.Giro, Cargas = ParametrosCargasViga.Ler(fi, erros) };
                    foreach (string e in erros) g.Avisos.Add($"{fi.Id.Value}: {e}");
                    if (el.Giro != 0 && el.Giro != 180) g.Avisos.Add($"{fi.Id.Value}: giro de {el.Giro}° não esperado numa viga; armada como a representante.");
                    g.Membros.Add(m);
                }
                if (g.Membros.Count == 0) continue;
                var av = new List<string>();
                g.Base = ConversorVigaPeca.Valores(vp, padrao, catalogo, av);
                g.Avisos.AddRange(av);
                if (g.Base == null) continue;
                // alma da representante (a aba e o ressalto ficam fora dela)
                SecaoComAba s = g.Representante.Levantada?.Principal?.Secao is SecaoPoligonal sp ? SecaoComAba.Ler(sp) : null;
                g.AlmaY0 = s?.Y0 ?? 0;
                g.AlmaZ0 = s?.Z0 ?? 0;
                g.Cargas = CargasViga.Envoltoria(g.Membros.Select(x => x.Cargas));
                if (g.Membros.Select(x => Assinatura(x.Cargas)).Distinct().Count() > 1)
                    g.Avisos.Add("As peças do grupo têm cargas diferentes: o cálculo usa a envoltória (maiores cargas) para todas.");
                grupos.Add(g);
            }
            return grupos;
        }

        private static string Assinatura(CargasViga c) =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.###}|{1:0.###}|{2}|{3:0.#}|{4:0.#}", c.G, c.Q, CargasViga.TextoConcentradas(c.Concentradas), c.ApoioE, c.ApoioD);

        /// <summary>Aplica materiais comuns (janela) nos valores de partida do grupo.</summary>
        public static void AjustarMateriais(GrupoViga g, double fck, string classe, double cobrimento)
        {
            if (g.Base == null) return;
            if (fck > 0) g.Base["Fck"] = Fmt.N(fck);
            if (!string.IsNullOrWhiteSpace(classe)) g.Base["ClasseAgressividade"] = classe;
            if (cobrimento > 0)
                foreach (string k in new[] { "CobFundo", "CobTopo", "CobLateral", "CobPontas" }) g.Base[k] = Fmt.N(cobrimento);
        }

        public static void Calcular(GrupoViga g, CoeficientesViga k = null)
        {
            if (g.Base == null) return;
            g.Calculo = CalculoViga.Calcular(g.Base, g.Cargas, k, TemplateVigaPreMoldada.CatalogoDentes);
        }

        /// <summary>Grava as cargas digitadas na janela em todas as peças do grupo (cria os parâmetros se faltarem). Precisa de transação.</summary>
        public static void GravarCargas(Document doc, GrupoViga g, List<string> avisos)
        {
            ParametrosCargasViga.Garantir(doc, avisos);
            foreach (MembroViga m in g.Membros) { ParametrosCargasViga.Gravar(m.Instance, g.Cargas); m.Cargas = g.Cargas; }
        }

        // ------------------------------------------------------------------ aplicação

        /// <summary>Barras de uma peça do grupo no sistema local dela (a representante + giro de 180° quando for o caso).</summary>
        public static List<BarDef> Barras(GrupoViga g, MembroViga m, List<string> avisos)
        {
            var bars = VigaRebarLayout.Compute(g.Proposta, new ConfiguracaoArmadura(), g.AlmaY0, g.AlmaZ0, avisos, TemplateVigaPreMoldada.CatalogoDentes);
            if (m.Giro == 180) bars = VigaRebarLayout.Girar180(bars, m.Info.LengthX, m.Info.LengthY);
            return bars;
        }

        public static void Aplicar(Document doc, IEnumerable<GrupoViga> grupos, DetailingReport report, ElementId activeViewId, bool vistas = true)
        {
            DetailingConfig cfg = DetailingConfig.Load(out string w).Clone();
            if (w != null) report.General.Add(w);
            cfg.IncludePartList = false;
            foreach (GrupoViga g in grupos.Where(x => x.Selecionado && x.Base != null))
            {
                GravarRelatorio(doc, g, report);
                bool primeiro = true;
                foreach (MembroViga m in g.Membros)
                {
                    PieceLog log = report.Begin($"{g.Marca} - viga {m.Id}");
                    try { AplicarMembro(doc, g, m, cfg, log, activeViewId, vistas && primeiro); }
                    catch (Exception ex) { log.Error(ex.Message); log.Trace("Vigas pré-moldadas", ex); }
                    primeiro = false;
                }
            }
        }

        private static void AplicarMembro(Document doc, GrupoViga g, MembroViga m, DetailingConfig cfg, PieceLog log, ElementId activeViewId, bool vistas)
        {
            if (!Tx(doc, "Limpar armadura anterior", log, () => Limpar(doc, m, log, activeViewId))) return;
            Tx(doc, "Gravar marca e cargas", log, () =>
            {
                m.Instance.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.Set(g.Marca);
                var av = new List<string>();
                ParametrosCargasViga.Garantir(doc, av);
                ParametrosCargasViga.Gravar(m.Instance, m.Cargas);
                foreach (string a in av) log.Warn(a);
            }, critical: false);

            var avisos = new List<string>();
            List<BarDef> bars = Barras(g, m, avisos);
            foreach (string a in avisos.Distinct()) log.Warn(a);
            ColumnFrame f = ColumnFrame.From(m.Info);
            var factory = new RebarFactory(doc, log);
            List<ElementId> rebar = null;
            Tx(doc, "Armar viga", log, () => rebar = factory.Create(f, m.Instance, bars, g.Marca), critical: false);
            if (rebar == null || rebar.Count == 0) { log.Error("Nenhuma barra criada."); return; }
            log.Info($"{rebar.Count} conjunto(s) de barras criados ({bars.Sum(b => b.Count)} barras).");
            Tx(doc, "Dados das barras", log, () =>
            {
                RebarData.EnsureParameters(doc);
                RebarData.Apply(doc, rebar, g.Marca, factory.CoreLengthsCm);
            }, critical: false);

            // montagem (viga + barras) e vistas: o 1º do grupo cria; os outros caem no mesmo tipo e reaproveitam
            var members = new List<ElementId> { m.Instance.Id };
            members.AddRange(rebar);
            AssemblyInstance asm = null;
            if (!Tx(doc, "Criar montagem", log, () =>
                {
                    ElementId cat = m.Instance.Category.Id;
                    if (!AssemblyInstance.IsValidNamingCategory(doc, cat, members)) throw new InvalidOperationException("categoria não aceita para nomear a montagem");
                    asm = AssemblyInstance.Create(doc, members, cat);
                }) || asm == null) return;
            PieceInfo p = m.Info;
            p.Mark = g.Marca;
            Tx(doc, "Orientar montagem", log, () => { asm.SetTransform(p.ToTransform()); doc.Regenerate(); }, critical: false);
            bool gemeo = new FilteredElementCollector(doc).OfClass(typeof(AssemblyInstance)).Cast<AssemblyInstance>()
                .Any(a => a.Id != asm.Id && a.GetTypeId() == asm.GetTypeId());
            if (gemeo) { log.Info($"Montagem igual à \"{asm.AssemblyTypeName}\": usa as vistas dela."); return; }
            Tx(doc, "Nomear montagem", log, () => asm.AssemblyTypeName = NameRegistry.ForAssemblyTypes(doc).Unique(g.Marca), critical: false);
            if (!vistas) return;

            var e = g.Peca;
            var c = cfg.Clone();
            // corte junto ao apoio no meio do dente (ou a d da ponta reta); o outro no meio do vão
            c.BeamSupportSectionDistanceMm = (e.DenteE != null ? e.DenteE.Comprimento / 2 : Math.Max(30, g.Proposta.Numero("H") * 0.6)) * 10;
            DetailViews views = null;
            Tx(doc, "Criar vistas", log, () => views = new ViewFactory(doc, c, log).Create(asm, p), critical: false);
            if (views == null) return;
            Tx(doc, "Armadura nas vistas", log, () =>
            {
                List<Rebar> rs = asm.GetMemberIds().Select(id => doc.GetElement(id)).OfType<Rebar>().ToList();
                foreach (DetailView dv in views.Views)
                {
                    foreach (Rebar r in rs)
                    {
                        try { r.SetUnobscuredInView(dv.View, true); } catch { }

                    }
                    try { RebarFilters.Apply(doc, dv.View, log); } catch (Exception ex) { log.Trace("Filtros de bitola", ex); }
                }
            }, critical: false);
            int dims = 0;
            Tx(doc, "Cotar vistas", log, () =>
            {
                var ds = new DimensionService(doc, c, log);
                foreach (DetailView dv in views.Views) dims += ds.AddOverallDimensions(dv, p);
            }, critical: false);
            int tags = 0;
            Tx(doc, "Etiquetas das barras", log, () =>
            {
                foreach (DetailView dv in views.Views.Where(x => x.Role == ViewRole.Elevation))
                    tags += VigasEtiquetas.Etiquetar(doc, dv.View, asm, m.Instance, log);
            }, critical: false);
            log.Info($"{tags} etiqueta(s) de barras na elevação ({views.Views.Count(x => x.Role == ViewRole.Elevation)} vista(s) de elevação).");
            Tx(doc, "Tabelas de aço", log, () =>
            {
                var nomes = NameRegistry.ForViews(doc);
                RebarSchedules.Create(doc, asm, new TableSpec { Tipo = TableKind.RelacaoDoAco }, nomes.Unique($"{g.Marca} - Relação do aço"));
                RebarSchedules.Create(doc, asm, new TableSpec { Tipo = TableKind.ResumoDoAco }, nomes.Unique($"{g.Marca} - Resumo do aço"));
            }, critical: false);
            log.Infos.Insert(0, $"{g.Marca}: {views.Views.Count} vistas da montagem e tabelas de aço (a folha fica com você, D29); {dims} cotas.");
        }

        /// <summary>Relatório de cálculo e verificação do grupo (D37) na pasta do modelo; sem modelo salvo, na pasta de logs.</summary>
        public static string GravarRelatorio(Document doc, GrupoViga g, DetailingReport report)
        {
            try
            {
                string pasta = null;
                try { if (!string.IsNullOrEmpty(doc.PathName)) pasta = Path.GetDirectoryName(doc.PathName); } catch { }
                if (pasta == null || !Directory.Exists(pasta)) pasta = DetailingReport.LogDir;
                RelatorioGrupo rel = Relatorio(doc, g);
                string arq = RelatorioHtml.Gravar(rel, pasta);
                report?.General.Add($"{g.Marca}: relatório de cálculo em {arq}");
                return arq;
            }
            catch (Exception ex) { report?.General.Add($"{g.Marca}: relatório não gravado — {ex.Message}"); return null; }
        }

        public static RelatorioGrupo Relatorio(Document doc, GrupoViga g)
        {
            var t = new TemplateVigaPreMoldada();
            var v = new ValoresTemplate();
            foreach (var kv in g.Proposta.Todos) v[kv.Key] = kv.Value;
            v["Nome"] = g.Marca;
            v["Quantidade"] = g.Quantidade.ToString(CultureInfo.InvariantCulture);
            ResultadoTemplate r = t.Gerar(v, new ConfiguracaoArmadura());
            string origem = "modelo do Revit (" + (string.IsNullOrEmpty(doc.PathName) ? doc.Title : Path.GetFileName(doc.PathName)) + ")";
            RelatorioGrupo rel = RelatorioHtml.DoTemplate(r, g.Marca, origem) ?? new RelatorioGrupo { Marca = g.Marca, Elemento = "Viga pré-moldada", Origem = origem };
            g.Calculo?.PreencherRelatorio(rel);
            try
            {
                // desenho da arma��o proposta: as mesmas barras que v�o para o Revit (representante, sem giro)
                var bars = VigaRebarLayout.Compute(g.Proposta, new ConfiguracaoArmadura(), g.AlmaY0, g.AlmaZ0, new List<string>(), TemplateVigaPreMoldada.CatalogoDentes);
                foreach (var (tit, svg) in DesenhoArmacaoSvg.Figuras(g.Proposta, bars, g.AlmaY0, g.AlmaZ0))
                    rel.Figuras.Add(new KeyValuePair<string, string>(tit, svg));
            }
            catch (Exception ex) { rel.Dados.Add(new KeyValuePair<string, string>("Observa��o", "desenho da arma��o n�o gerado: " + ex.Message)); }
            foreach (string a in g.Avisos) rel.Dados.Add(new KeyValuePair<string, string>("Observação", a));
            return rel;
        }

        /// <summary>Desfaz a montagem anterior (e as vistas dela) e apaga as barras do plugin hospedadas na viga.</summary>
        private static void Limpar(Document doc, MembroViga m, PieceLog log, ElementId activeViewId)
        {
            if (m.Instance.AssemblyInstanceId != ElementId.InvalidElementId && doc.GetElement(m.Instance.AssemblyInstanceId) is AssemblyInstance asm)
            {
                bool sozinha = !new FilteredElementCollector(doc).OfClass(typeof(AssemblyInstance)).Cast<AssemblyInstance>()
                    .Any(a => a.Id != asm.Id && a.GetTypeId() == asm.GetTypeId());
                if (sozinha)
                {
                    var vs = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                        .Where(v => !v.IsTemplate && v.AssociatedAssemblyInstanceId == asm.Id && v.Id != activeViewId).Select(v => v.Id).ToList();
                    if (vs.Count > 0) doc.Delete(vs);
                }
                asm.Disassemble();
            }
            string tag = RebarFactory.Tag + "|";
            RebarHostData hd = RebarHostData.GetRebarHostData(m.Instance);
            var old = hd == null ? new List<ElementId>() : hd.GetRebarsInHost()
                .Where(r => (r.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "").StartsWith(tag)).Select(r => r.Id).ToList();
            if (old.Count > 0) { doc.Delete(old); log.Info($"Armadura anterior do plugin removida ({old.Count} conjunto(s))."); }
        }

        internal static bool Tx(Document doc, string name, PieceLog log, Action action, bool critical = true)
        {
            using (var t = new Transaction(doc, name))
            {
                try
                {
                    t.Start();
                    WarningSwallower.Attach(t);
                    action();
                    if (t.Commit() == TransactionStatus.Committed) return true;
                    (critical ? (Action<string>)log.Error : log.Warn)($"{name}: transação não confirmada{WarningSwallower.TakeReason()}.");
                    return false;
                }
                catch (Exception ex)
                {
                    if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                    (critical ? (Action<string>)log.Error : log.Warn)($"{name}: {ex.Message}");
                    log.Trace(name, ex);
                    return false;
                }
            }
        }
    }
}

