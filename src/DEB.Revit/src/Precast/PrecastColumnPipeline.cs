using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DEB.Revit.Core;
using DEB.Revit.Presentation;

namespace DEB.Revit.Precast
{
    internal sealed class PipelineOptions
    {
        public bool WriteMarks { get; set; } = true;
        public bool CreateRebar { get; set; } = true;
        public bool Detail { get; set; } = true;
    }

    /// <summary>
    /// Para cada grupo e cada pilar: limpa o detalhamento anterior do plugin, grava a marca,
    /// arma (pilar + consolos + içamento), posiciona insertos e detalha. O primeiro pilar do grupo
    /// gera vistas e folha; os demais caem no mesmo tipo de assembly e reaproveitam a folha.
    /// Deve rodar dentro de um TransactionGroup (cada etapa é uma transação).
    /// </summary>
    internal sealed class PrecastColumnPipeline
    {
        private readonly Document _doc;
        private readonly DetailingConfig _cfg;
        private readonly DetailingReport _report;
        private readonly ElementId _activeViewId;

        public ViewSheet LastSheet { get; private set; }

        public PrecastColumnPipeline(Document doc, DetailingConfig cfg, DetailingReport report, ElementId activeViewId)
        {
            _doc = doc; _report = report; _activeViewId = activeViewId;
            // A lista de peças do assembly lista cada barra e não cabe na folha: a tabela de aço vem depois.
            _cfg = cfg.Clone();
            _cfg.IncludePartList = false;
            _standard = PresentationStandard.Load("PilarPreMoldado", out string warning);
            if (warning != null) report.General.Add(warning);
        }

        private readonly PresentationStandard _standard;

        /// <summary>Padrão de apresentação em uso (pasta %AppData%\PCEB\DEB.Revit\Padroes).</summary>
        public PresentationStandard Standard => _standard;

        public void Run(IEnumerable<ColumnGroup> groups, PipelineOptions opt)
        {
            foreach (ColumnGroup g in groups)
            {
                foreach (PrecastColumn m in g.Members)
                {
                    PieceLog log = _report.Begin($"{g.Mark} - pilar {m.Id}");
                    try
                    {
                        RunMember(g, m, opt, log);
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex.Message);
                        log.Trace("Pipeline", ex);
                    }
                }
            }
        }

        private void RunMember(ColumnGroup g, PrecastColumn m, PipelineOptions opt, PieceLog log)
        {
            if (!Tx("Limpar detalhamento anterior", log, () => Clean(m, log))) return;

            if (opt.WriteMarks)
                Tx("Gravar marca", log, () => m.Instance.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.Set(g.Mark), critical: false);

            var members = new List<ElementId> { m.Instance.Id };
            foreach (Corbel k in m.Corbels)
            {
                if (k.Instance.AssemblyInstanceId != ElementId.InvalidElementId)
                    log.Warn($"Consolo {k.Instance.Id.Value} já pertence a outro assembly e não foi incluído no pilar.");
                else
                    members.Add(k.Instance.Id);
            }

            if (opt.CreateRebar)
            {
                List<BarDef> bars = ColumnRebarLayout.Compute(m.Frame, m.Corbels, g.Spec);
                List<ElementId> rebar = null;
                Tx("Armar pilar", log, () => rebar = new RebarFactory(_doc, log).Create(m.Frame, m.Instance, bars, g.Mark), critical: false);
                if (rebar != null)
                {
                    members.AddRange(rebar);
                    log.Info($"{rebar.Count} conjunto(s) de barras criados.");
                    string partition = PresentationStandard.Fill(_standard.Armadura.Particao, new Dictionary<string, string> { ["Marca"] = g.Mark });
                    Tx("Dados das barras", log, () =>
                    {
                        RebarData.EnsureParameters(_doc);
                        RebarData.Apply(_doc, rebar, partition, _standard.Armadura.DensidadeAco);
                    }, critical: false);
                }

                if (!string.IsNullOrWhiteSpace(g.Spec.InsertFamily))
                {
                    ElementId ins = null;
                    Tx("Posicionar inserto", log, () => ins = PlaceInsert(m, g, log), critical: false);
                    if (ins != null) members.Add(ins);
                }
            }

            if (!opt.Detail) { log.Info("Armado (sem detalhamento)."); return; }

            PieceInfo p = m.Frame.ToPieceInfo(m.Info, g.Mark);
            p.Attached = m.Corbels.Where(k => members.Contains(k.Instance.Id)).Select(k => k.Instance).ToList();
            Dictionary<CutPosition, List<double>> cuts = CutHeights(m, g.Spec);
            p.SectionHeights = cuts[CutPosition.Corrente].Concat(cuts[CutPosition.Consolos]).ToList();

            var ctx = new PresentationContext { Standard = _standard, Values = Values(g, m), CutHeights = cuts };
            var detailer = new PieceDetailer(_doc, _cfg, _report, _activeViewId)
            {
                CustomDetail = (asm, info, l) => new StandardDetailer(_doc, _cfg, ctx).Detail(asm, info, l)
            };
            detailer.RunPrecast(p, members, log);
            if (detailer.LastSheet != null) LastSheet = detailer.LastSheet;
        }

        /// <summary>
        /// Alturas dos cortes: "Corrente" no meio do maior trecho livre de consolos (acima do embutimento);
        /// "Consolos" um por nível de consolo, logo abaixo do topo dele (mostra o tirante); "Topo" a 30 cm do topo.
        /// </summary>
        private static Dictionary<CutPosition, List<double>> CutHeights(PrecastColumn m, ColumnRebarSpec s)
        {
            double h = m.Frame.H;
            double start = Math.Min(U_.Mm(m.EmbedmentMm), h / 2);
            var blocked = m.Corbels.Select(k => (k.ZBottom, k.ZTop)).OrderBy(x => x.ZBottom).ToList();

            double bestA = start, bestB = h, bestLen = -1, cur = start;
            foreach (var (zb, zt) in blocked.Append((h, h)))
            {
                if (zb - cur > bestLen) { bestLen = zb - cur; bestA = cur; bestB = zb; }
                cur = Math.Max(cur, zt);
            }

            var corbels = new List<double>();
            foreach (Corbel k in m.Corbels.OrderByDescending(k => k.ZTop))
            {
                double z = k.ZTop - U_.Mm(s.CoverMm + s.TieDiameterMm + 20);
                if (corbels.All(x => Math.Abs(x - z) > U_.Mm(50))) corbels.Add(z);
            }

            return new Dictionary<CutPosition, List<double>>
            {
                [CutPosition.Corrente] = new List<double> { (bestA + bestB) / 2 },
                [CutPosition.Consolos] = corbels,
                [CutPosition.Topo] = new List<double> { h - U_.Mm(300) }
            };
        }

        /// <summary>Campos de texto do padrão: marca, quantidade, seção, comprimento, volume, peso, data e produção.</summary>
        private Dictionary<string, string> Values(ColumnGroup g, PrecastColumn m)
        {
            var br = new System.Globalization.CultureInfo("pt-BR");
            double vol = 0;
            foreach (Element e in new Element[] { m.Instance }.Concat(m.Corbels.Select(k => (Element)k.Instance)))
                vol += e.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED)?.AsDouble() ?? 0;
            double m3 = UnitUtils.ConvertFromInternalUnits(vol, UnitTypeId.CubicMeters);
            double density = double.TryParse(_standard.Producao.TryGetValue("DensidadeConcreto_t_m3", out string d) ? d : "2.5",
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double dv) ? dv : 2.5;

            var values = new Dictionary<string, string>(_standard.Producao)
            {
                ["Marca"] = g.Mark,
                ["Quantidade"] = g.Members.Count.ToString(),
                ["Secao"] = $"{U_.ToMm(m.Frame.Bx) / 10:0.#}x{U_.ToMm(m.Frame.By) / 10:0.#}",
                ["Comprimento"] = (U_.ToMm(m.Frame.H) / 10).ToString("0.#", br),
                ["Volume"] = m3.ToString("0.000", br),
                ["Peso"] = (m3 * density).ToString("0.000", br),
                ["Data"] = DateTime.Now.ToString("dd/MM/yyyy")
            };
            return values;
        }

        // ------------------------------------------------------------------

        /// <summary>Desfaz o assembly anterior (apagando suas vistas e folha) e remove barras/insertos do plugin.</summary>
        private void Clean(PrecastColumn m, PieceLog log)
        {
            var asmIds = new HashSet<ElementId>();
            if (m.Instance.AssemblyInstanceId != ElementId.InvalidElementId) asmIds.Add(m.Instance.AssemblyInstanceId);

            foreach (ElementId id in asmIds)
            {
                if (!(_doc.GetElement(id) is AssemblyInstance asm)) continue;
                List<ElementId> views = new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>()
                    .Where(v => !v.IsTemplate && v.AssociatedAssemblyInstanceId == asm.Id && v.Id != _activeViewId)
                    .Select(v => v.Id).ToList();
                if (views.Count > 0) _doc.Delete(views);
                asm.Disassemble();
            }

            string tag = RebarFactory.Tag + "|";
            var hosts = new List<Element> { m.Instance };
            hosts.AddRange(m.Corbels.Select(k => (Element)k.Instance));
            var old = new List<ElementId>();
            foreach (Element h in hosts)
            {
                RebarHostData hd = RebarHostData.GetRebarHostData(h);
                if (hd == null) continue;
                old.AddRange(hd.GetRebarsInHost()
                    .Where(r => (r.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "").StartsWith(tag))
                    .Select(r => r.Id));
            }

            string insertTag = $"{RebarFactory.Tag}|Inserto|{m.Id}";
            old.AddRange(new FilteredElementCollector(_doc).OfClass(typeof(FamilyInstance))
                .Where(e => e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() == insertTag)
                .Select(e => e.Id));

            if (old.Count > 0)
            {
                _doc.Delete(old);
                log.Info($"Detalhamento anterior removido ({old.Count} elemento(s) do plugin).");
            }
        }

        /// <summary>Insere a família de inserto no centro do topo do pilar.</summary>
        private ElementId PlaceInsert(PrecastColumn m, ColumnGroup g, PieceLog log)
        {
            string[] parts = g.Spec.InsertFamily.Split(new[] { ':' }, 2);
            string fam = parts[0].Trim(), type = parts.Length > 1 ? parts[1].Trim() : null;
            FamilySymbol sym = new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .FirstOrDefault(s => string.Equals(s.FamilyName, fam, StringComparison.OrdinalIgnoreCase) &&
                                     (type == null || string.Equals(s.Name, type, StringComparison.OrdinalIgnoreCase)));
            if (sym == null)
            {
                log.Warn($"Família de inserto \"{g.Spec.InsertFamily}\" não encontrada no projeto.");
                return null;
            }
            if (!sym.IsActive) { sym.Activate(); _doc.Regenerate(); }

            ColumnFrame f = m.Frame;
            FamilyInstance fi = _doc.Create.NewFamilyInstance(f.ToWorld(f.Bx / 2, f.By / 2, f.H), sym,
                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
            fi.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"{RebarFactory.Tag}|Inserto|{m.Id}");
            return fi.Id;
        }

        private bool Tx(string name, PieceLog log, Action action, bool critical = true)
        {
            using (var t = new Transaction(_doc, name))
            {
                try
                {
                    t.Start();
                    WarningSwallower.Attach(t);
                    action();
                    if (t.Commit() == TransactionStatus.Committed) return true;
                    (critical ? (Action<string>)log.Error : log.Warn)($"{name}: transação não confirmada.");
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
