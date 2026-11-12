using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DEB.Core.Lajes;
using DEB.Revit.Core;
using DEB.Revit.Tools;

namespace DEB.Revit.LajeAlveolar
{
    /// <summary>
    /// "Paginar laje alveolar" (2026-11-12): pisos selecionados → leitura (contorno, furos, vão, topo, vigas, pilares) → janela de
    /// configuração → paginação (DEB.Core/Lajes) → prévia (planta + relatório) → inserção (painéis, faixas maciças) e fase 3 opcional
    /// (capa, chaves, tirantes) numa transação → relatório com os avisos. Barreira de exceções do <see cref="ToolCommand"/>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class PaginarLajeAlveolarCommand : ToolCommand
    {
        protected override string Title => "Paginar laje alveolar";

        public static string ArquivoConfig => Path.Combine(UserFolders.Root, "LajeAlveolar", "config_paginacao.json");

        private sealed class FiltroPiso : ISelectionFilter
        {
            public bool AllowElement(Element e) => e is Floor;
            public bool AllowReference(Reference r, XYZ p) => false;
        }

        protected override Result Run(UIApplication uiapp, UIDocument uidoc, Document doc)
        {
            // 1) pisos
            var pisos = uidoc.Selection.GetElementIds().Select(doc.GetElement).OfType<Floor>().ToList();
            if (pisos.Count == 0)
            {
                IList<Reference> refs;
                try { refs = uidoc.Selection.PickObjects(ObjectType.Element, new FiltroPiso(), "Selecione os pisos de laje alveolar e clique em Concluir"); }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { throw new ToolCancelled(); }
                pisos = refs.Select(r => doc.GetElement(r)).OfType<Floor>().ToList();
            }
            if (pisos.Count == 0) throw new ToolCancelled("Selecione um ou mais pisos de laje alveolar.");

            // 2) configuração e famílias de painel
            var cfg = ConfigPaginacao.Carregar(ArquivoConfig, out string erroCfg);
            if (erroCfg != null) TaskDialog.Show(Title, erroCfg);
            var simbolos = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_StructuralFraming)
                .Cast<FamilySymbol>().ToList();
            if (simbolos.Count == 0) throw new ToolCancelled("Nenhuma família de quadro estrutural carregada no projeto. Carregue a família do painel alveolar.");
            var familias = simbolos.GroupBy(s => s.FamilyName).ToDictionary(g => g.Key, g => g.Select(s => s.Name).OrderBy(n => n).ToList());
            cfg = new ConfigPaginacaoWindow(cfg, familias, pisos.Count).Mostrar();
            if (cfg == null) throw new ToolCancelled();
            try { cfg.Gravar(ArquivoConfig); } catch (Exception ex) { TaskDialog.Show(Title, "A configuração não foi gravada: " + ex.Message); }
            var simbolo = simbolos.First(s => s.FamilyName == cfg.Insercao.FamiliaPainel && s.Name == cfg.Insercao.TipoPainel);

            // 3) o que já existe no modelo (comentário "LAJE ALVEOLAR <marca> (…, piso <id>)")
            var existentes = Existentes(doc);
            var numeroDoPiso = new Dictionary<string, int>();
            var rx = new Regex("^" + Regex.Escape(cfg.Insercao.PrefixoPainel) + @"(\d+)-");
            int maxNum = 0;
            foreach (var (marca, coment, _) in existentes)
            {
                var m = rx.Match(marca);
                if (!m.Success) continue;
                int n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                maxNum = Math.Max(maxNum, n);
                var mp = Regex.Match(coment, @"piso (\d+)\)");
                if (mp.Success) numeroDoPiso[mp.Groups[1].Value] = n;
            }
            var jaFeitos = pisos.Where(p => numeroDoPiso.ContainsKey(ExtracaoLajeAlveolar.Id(p))).ToList();
            if (jaFeitos.Count > 0)
            {
                var td = new TaskDialog(Title)
                {
                    MainInstruction = $"{jaFeitos.Count} piso(s) já têm painéis deste comando.",
                    MainContent = "Completar: pagina de novo com a mesma numeração e só cria o que falta (o que já existe não é refeito).\nPular: deixa esses pisos de fora.",
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Completar");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Pular esses pisos");
                td.CommonButtons = TaskDialogCommonButtons.Cancel;
                var r = td.Show();
                if (r == TaskDialogResult.CommandLink2) pisos = pisos.Except(jaFeitos).ToList();
                else if (r != TaskDialogResult.CommandLink1) throw new ToolCancelled();
                if (pisos.Count == 0) throw new ToolCancelled();
            }

            // 4) leitura e paginação
            var notas = new List<string>();
            var plantas = new List<PisoPlanta>();
            foreach (var p in pisos)
            {
                try { plantas.Add(ExtracaoLajeAlveolar.Extrair(doc, p, cfg, simbolo.Family.Id, notas)); }
                catch (Exception ex) { notas.Add($"piso {ExtracaoLajeAlveolar.Id(p)}: não lido ({ex.Message})"); }
            }
            if (plantas.Count == 0) throw new ToolCancelled("Nenhum piso pôde ser lido:\n" + string.Join("\n", notas));
            var panos = new List<PanoPaginado>();
            foreach (var pl in plantas.Where(p => numeroDoPiso.ContainsKey(p.Id)))
                panos.AddRange(PaginacaoObra.Paginar(new[] { pl }, cfg, numeroDoPiso[pl.Id]));
            panos.AddRange(PaginacaoObra.Paginar(plantas.Where(p => !numeroDoPiso.ContainsKey(p.Id)), cfg, maxNum + 1));
            panos = panos.OrderBy(p => p.Numero).ToList();

            var avisos = PaginacaoObra.Avisos(panos);
            avisos.AddRange(notas);
            foreach (var p in panos.Where(p => p.Erro == null && p.Piso.Vigas.Count == 0))
                avisos.Add($"pano {p.Numero}: nenhuma viga encontrada em volta do piso — pontas no contorno do piso");
            string relatorio = PaginacaoObra.Relatorio(panos, cfg);

            // 5) prévia
            if (!PreviaPaginacaoWindow.Mostrar(panos, cfg, relatorio, avisos)) throw new ToolCancelled();

            // 6) inserção + fase 3
            var ctx = new ContextoLajeAlveolar { Doc = doc, Cfg = cfg, Panos = panos };
            foreach (var p in pisos) ctx.Pisos[ExtracaoLajeAlveolar.Id(p)] = p;
            foreach (var (marca, _, id) in existentes) { ctx.Existentes.Add(marca); ctx.PorMarca[marca] = id; }
            var falhas = new List<string>();
            TransactionStatus status;
            using (var t = new Transaction(doc, Title))
            {
                var fho = t.GetFailureHandlingOptions();
                fho.SetFailuresPreprocessor(new SemJanelas(falhas));
                fho.SetForcedModalHandling(false);
                fho.SetClearAfterRollback(true);
                t.SetFailureHandlingOptions(fho);
                t.Start();
                InsercaoLajeAlveolar.Inserir(ctx, simbolo);
                doc.Regenerate();
                foreach (var comp in Fase3LajeAlveolar.Todos.Where(c => c.Ativo(cfg)))
                {
                    using (var st = new SubTransaction(doc))
                    {
                        st.Start();
                        try { comp.Aplicar(ctx); doc.Regenerate(); st.Commit(); }
                        catch (Exception ex) { st.RollBack(); ctx.Erros.Add($"{comp.Nome}: {ex.Message} (etapa desfeita)"); }
                    }
                }
                status = t.Commit();
            }

            // 7) relatório final
            string final = RelatorioFinal(ctx, status, relatorio, avisos, falhas);
            string arq = null;
            try
            {
                Directory.CreateDirectory(DetailingReport.LogDir);
                arq = Path.Combine(DetailingReport.LogDir, $"laje_alveolar_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(arq, final, Encoding.UTF8);
            }
            catch { arq = null; }
            PreviaPaginacaoWindow.Relatorio(Title + " — resultado", (arq != null ? "Relatório gravado em " + arq + "\n\n" : "") + final);
            return status == TransactionStatus.Committed ? Result.Succeeded : Result.Failed;
        }

        private static string RelatorioFinal(ContextoLajeAlveolar ctx, TransactionStatus status, string relatorio, List<string> avisos, List<string> falhas)
        {
            var sb = new StringBuilder();
            sb.AppendLine(status == TransactionStatus.Committed
                ? $"Inserção concluída: {ctx.Criados} elemento(s) criado(s); {ctx.Existentes.Count} já existiam e foram mantidos."
                : $"A transação não foi confirmada ({status}): NADA foi alterado no modelo.");
            sb.Append(ctx.Log);
            if (ctx.SemParametroEtapa.Count > 0)
                sb.AppendLine($"ATENÇÃO: o parâmetro \"{ctx.Cfg.Insercao.ParametroEtapa}\" não existe (ou é só leitura) em: {string.Join(", ", ctx.SemParametroEtapa)} — a etapa não foi gravada nesses elementos.");
            var furos = ConfigFase3.FurosPorPeca(ctx.TirantesComFuro);
            if (furos.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"FUROS EM OBRA na borda dos painéis ({ctx.Cfg.Fase3.TextoFuro(ctx.Cfg.Tirante.Diametro)}; 2 por tirante, um em cada painel vizinho): {furos.Values.Sum()} furo(s)");
                foreach (var kv in furos) sb.AppendLine($"   {kv.Key,-10} {kv.Value} furo(s)");
            }
            var todosAvisos = avisos.Concat(ctx.Avisos).Distinct().ToList();
            sb.AppendLine();
            sb.AppendLine($"AVISOS ({todosAvisos.Count})");
            foreach (var a in todosAvisos) sb.AppendLine("• " + a);
            if (ctx.Erros.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"ERROS ({ctx.Erros.Count})");
                foreach (var e in ctx.Erros) sb.AppendLine("• " + e);
            }
            if (falhas.Count > 0) sb.AppendLine("Mensagens do Revit: " + string.Join(" | ", falhas.Distinct().Take(30)));
            sb.AppendLine();
            sb.Append(relatorio);
            return sb.ToString();
        }

        /// <summary>Elementos criados por este comando: (marca, comentário, id).</summary>
        private static List<(string Marca, string Comentario, ElementId Id)> Existentes(Document doc)
        {
            var r = new List<(string, string, ElementId)>();
            var cats = new ElementMulticategoryFilter(new List<BuiltInCategory> { BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_Floors, BuiltInCategory.OST_GenericModel });
            foreach (var e in new FilteredElementCollector(doc).WhereElementIsNotElementType().WherePasses(cats))
            {
                string c = ExtracaoLajeAlveolar.Comentario(e);
                if (!c.StartsWith(ExtracaoLajeAlveolar.Prefixo, StringComparison.Ordinal)) continue;
                string marca = c.Substring(ExtracaoLajeAlveolar.Prefixo.Length).Split(' ')[0];
                if (marca.Length > 0) r.Add((marca, c, e.Id));
            }
            return r;
        }

        /// <summary>Apaga avisos; erros com resolução são resolvidos; sem resolução → desfaz (nunca abre janela no Revit do usuário).</summary>
        private sealed class SemJanelas : IFailuresPreprocessor
        {
            private readonly List<string> _log;
            public SemJanelas(List<string> l) { _log = l; }
            public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
            {
                bool resolveu = false;
                foreach (var f in fa.GetFailureMessages())
                {
                    _log.Add(f.GetSeverity() + ": " + f.GetDescriptionText());
                    if (f.GetSeverity() == FailureSeverity.Warning) { fa.DeleteWarning(f); continue; }
                    if (f.HasResolutions()) { fa.ResolveFailure(f); resolveu = true; }
                    else return FailureProcessingResult.ProceedWithRollBack;
                }
                return resolveu ? FailureProcessingResult.ProceedWithCommit : FailureProcessingResult.Continue;
            }
        }
    }
}
