using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

namespace StructuralDetailer.Core
{
    internal enum DetailOutcome { Done, Skipped, Failed, CancelAll }

    /// <summary>
    /// Orquestra o detalhamento de uma peça:
    /// análise → assembly → vistas/cortes → cotas → folha.
    /// Cada etapa é uma transação própria (todas dentro do TransactionGroup do comando).
    /// </summary>
    internal sealed class PieceDetailer
    {
        private readonly Document _doc;
        private readonly DetailingConfig _cfg;
        private readonly DetailingReport _report;
        private readonly ElementId _activeViewId;

        public ViewSheet LastSheet { get; private set; }

        /// <summary>
        /// Quando definido, substitui as etapas de vistas, cotas e folha (ex.: detalhamento por padrão de apresentação).
        /// Recebe o assembly já orientado e nomeado; devolve a prancha (null = falha).
        /// </summary>
        public Func<AssemblyInstance, PieceInfo, PieceLog, ViewSheet> CustomDetail { get; set; }

        public PieceDetailer(Document doc, DetailingConfig cfg, DetailingReport report, ElementId activeViewId)
        {
            _doc = doc; _cfg = cfg; _report = report; _activeViewId = activeViewId;
        }

        public DetailOutcome Run(FamilyInstance fi)
        {
            // 1. Análise
            PieceInfo p;
            try { p = PieceAnalyzer.Analyze(fi); }
            catch (PieceNotSupportedException ex)
            {
                _report.Begin($"Elemento {fi.Id.Value}").Error(ex.Message);
                return DetailOutcome.Failed;
            }

            PieceLog log = _report.Begin($"{p.KindLabel} {p.Mark}");

            // 2. Assembly (novo ou existente)
            AssemblyInstance asm = null;
            if (fi.AssemblyInstanceId != ElementId.InvalidElementId)
            {
                switch (AskAboutExisting(p))
                {
                    case TaskDialogResult.CommandLink1: break;
                    case TaskDialogResult.CommandLink2: log.Info("Pulada (já possuía assembly)."); return DetailOutcome.Skipped;
                    default: log.Info("Cancelado pelo usuário."); return DetailOutcome.CancelAll;
                }

                asm = _doc.GetElement(fi.AssemblyInstanceId) as AssemblyInstance;
                if (!RunTx("Limpar detalhamento anterior", log, () => DeleteAssemblyViews(asm, log)))
                    return DetailOutcome.Failed;

                // Pilar já em assembly mas ainda sem armadura: arma e inclui as barras no assembly.
                List<ElementId> added = BuildColumnRebar(p, log);
                if (added.Count > 0)
                    RunTx("Incluir armadura no assembly", log, () => asm.AddMemberIds(added), critical: false);
            }

            bool isNew = asm == null;
            if (isNew)
            {
                // A armadura entra no assembly desde a criação, para aparecer nas vistas de assembly.
                List<ElementId> members = new List<ElementId> { fi.Id };
                members.AddRange(BuildColumnRebar(p, log));

                if (!RunTx("Criar assembly", log, () => asm = CreateAssembly(fi, members)) || asm == null)
                    return DetailOutcome.Failed;
            }

            return DetailAssembly(asm, p, log, isNew);
        }

        /// <summary>
        /// Pré-moldado: cria o assembly com os membros já definidos (pilar, consolos, armaduras, insertos)
        /// e gera o detalhamento. Peças do mesmo grupo caem no mesmo tipo de assembly e reaproveitam a folha.
        /// </summary>
        public DetailOutcome RunPrecast(PieceInfo p, List<ElementId> members, PieceLog log)
        {
            AssemblyInstance asm = null;
            if (!RunTx("Criar assembly", log, () => asm = CreateAssembly(p.Instance, members)) || asm == null)
                return DetailOutcome.Failed;
            return DetailAssembly(asm, p, log, isNew: true);
        }

        /// <summary>Orienta, nomeia (ou reaproveita, se idêntico) e gera vistas, cotas e folha do assembly.</summary>
        private DetailOutcome DetailAssembly(AssemblyInstance asm, PieceInfo p, PieceLog log, bool isNew)
        {
            if (!RunTx("Orientar assembly", log, () => { asm.SetTransform(p.ToTransform()); _doc.Regenerate(); }))
                log.Warn("Não foi possível alinhar o assembly ao eixo da peça; as vistas usam a orientação padrão do Revit.");

            if (isNew)
            {
                // Peças idênticas (mesma família, geometria e armadura) caem no mesmo tipo de assembly.
                // Nesse caso o Revit compartilha as vistas do tipo e apaga vistas novas: reaproveita o detalhamento.
                List<AssemblyInstance> twins = Twins(asm);
                if (twins.Count > 0)
                {
                    ViewSheet shared = SheetOf(twins);
                    if (shared != null)
                    {
                        LastSheet = shared;
                        log.Info($"{p.Description} → idêntico ao assembly \"{asm.AssemblyTypeName}\": " +
                                 $"usa a folha existente {shared.SheetNumber}.");
                        return DetailOutcome.Done;
                    }
                    log.Info($"Idêntico ao assembly \"{asm.AssemblyTypeName}\": o nome do tipo foi mantido.");
                }
                // O nome do assembly precisa ser definido numa transação posterior à criação.
                else if (!RunTx("Nomear assembly", log, () =>
                             asm.AssemblyTypeName = NameRegistry.ForAssemblyTypes(_doc).Unique(p.Mark)))
                {
                    return DetailOutcome.Failed;
                }
            }

            // Detalhamento por padrão de apresentação (pré-moldados): vistas, cotas e prancha ficam com ele.
            if (CustomDetail != null)
            {
                ViewSheet custom = CustomDetail(asm, p, log);
                if (custom == null) return DetailOutcome.Failed;
                LastSheet = custom;
                log.Infos.Insert(0, $"{p.Description} → prancha {custom.SheetNumber}.");
                return DetailOutcome.Done;
            }

            // 3. Vistas e cortes
            DetailViews views = null;
            if (!RunTx("Criar vistas", log, () => views = new ViewFactory(_doc, _cfg, log).Create(asm, p)) || views == null)
                return DetailOutcome.Failed;

            RunTx("Exibir armadura", log, () => ShowRebar(asm, views), critical: false);

            // 4. Cotas
            int dims = 0;
            RunTx("Cotar vistas", log, () =>
            {
                var ds = new DimensionService(_doc, _cfg, log);
                foreach (DetailView v in views.Views)
                {
                    dims += ds.AddOverallDimensions(v, p);
                    foreach (FamilyInstance k in p.Attached) dims += ds.AddAttachedDimensions(v, p, k);
                }
            });

            // 5. Folha
            ViewSheet sheet = null;
            if (!RunTx("Montar folha", log, () => sheet = new SheetBuilder(_doc, _cfg, log).Build(asm, p, views)))
                return DetailOutcome.Failed;

            LastSheet = sheet;
            log.Infos.Insert(0, $"{p.Description} → folha {sheet.SheetNumber}, {views.Views.Count} vistas, {dims} cotas.");
            return DetailOutcome.Done;
        }

        // ------------------------------------------------------------------

        private AssemblyInstance CreateAssembly(FamilyInstance fi, List<ElementId> ids)
        {
            ElementId catId = fi.Category.Id;
            if (!AssemblyInstance.IsValidNamingCategory(_doc, catId, ids))
                throw new InvalidOperationException("Categoria não aceita como categoria de nomeação de assembly.");
            return AssemblyInstance.Create(_doc, ids, catId);
        }

        /// <summary>
        /// Arma o pilar (se configurado) e devolve as barras a incluir no assembly.
        /// Se o pilar já tiver armadura, ela é mantida e devolvida, sem gerar barras novas.
        /// </summary>
        private List<ElementId> BuildColumnRebar(PieceInfo p, PieceLog log)
        {
            var ids = new List<ElementId>();
            if (p.Kind != PieceKind.Column || !_cfg.ColumnRebarEnabled) return ids;

            List<ElementId> existing = ColumnRebarBuilder.ExistingRebar(p.Instance);
            if (existing.Count > 0)
            {
                log.Info($"O pilar já possui {existing.Count} armadura(s): mantidas, nenhuma barra nova foi gerada.");
                return existing.Where(id => _doc.GetElement(id)?.AssemblyInstanceId == ElementId.InvalidElementId).ToList();
            }

            RunTx("Armar pilar", log, () => ids = new ColumnRebarBuilder(_doc, _cfg, log).Build(p), critical: false);
            return ids;
        }

        private List<AssemblyInstance> Twins(AssemblyInstance asm)
        {
            ElementId typeId = asm.GetTypeId();
            return new FilteredElementCollector(_doc).OfClass(typeof(AssemblyInstance)).Cast<AssemblyInstance>()
                .Where(a => a.Id != asm.Id && a.GetTypeId() == typeId)
                .ToList();
        }

        private ViewSheet SheetOf(List<AssemblyInstance> instances)
        {
            var ids = new HashSet<ElementId>(instances.Select(a => a.Id));
            return new FilteredElementCollector(_doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .FirstOrDefault(s => ids.Contains(s.AssociatedAssemblyInstanceId));
        }

        /// <summary>Armadura "sem obstrução" nas vistas do detalhamento (visível através do concreto).</summary>
        private void ShowRebar(AssemblyInstance asm, DetailViews views)
        {
            List<Rebar> rebars = asm.GetMemberIds().Select(id => _doc.GetElement(id)).OfType<Rebar>().ToList();
            foreach (DetailView v in views.Views)
                foreach (Rebar r in rebars)
                    r.SetUnobscuredInView(v.View, true);
        }

        private void DeleteAssemblyViews(AssemblyInstance asm, PieceLog log)
        {
            if (asm == null) return;
            List<View> old = new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && v.AssociatedAssemblyInstanceId == asm.Id)
                .ToList();

            View active = old.FirstOrDefault(v => v.Id == _activeViewId);
            if (active != null)
            {
                log.Warn($"A vista ativa \"{active.Name}\" não pôde ser apagada (está aberta). Feche-a e apague manualmente.");
                old.Remove(active);
            }

            if (old.Count > 0) _doc.Delete(old.Select(v => v.Id).ToList());
        }

        private static TaskDialogResult AskAboutExisting(PieceInfo p)
        {
            var td = new TaskDialog("Detalhamento Estrutural")
            {
                MainInstruction = $"{p.KindLabel} {p.Mark} já pertence a um assembly.",
                MainContent = "O que fazer com o detalhamento existente?",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.CommandLink1
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Recriar detalhamento",
                "Apaga as vistas, cotas e a folha atuais desse assembly e gera tudo de novo.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Pular esta peça");
            return td.Show();
        }

        /// <param name="critical">false = a falha vira aviso e não conta a peça como falha.</param>
        private bool RunTx(string name, PieceLog log, Action action, bool critical = true)
        {
            Action<string> report = critical ? log.Error : log.Warn;
            using (var t = new Transaction(_doc, name))
            {
                try
                {
                    t.Start();
                    WarningSwallower.Attach(t);
                    action();
                    TransactionStatus st = t.Commit();
                    if (st != TransactionStatus.Committed)
                    {
                        report($"{name}: transação não confirmada ({st}).");
                        return false;
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                    report($"{name}: {ex.Message}");
                    log.Trace(name, ex);
                    return false;
                }
            }
        }
    }
}
