using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using DEB.Revit.Core;
using DEB.Revit.Presentation;

namespace DEB.Revit.Formas
{
    /// <summary>
    /// Fluxo das pranchas de formas, usado pelos botões e pelo AutoRun: cada etapa (parâmetros e dados, tabelas, vista, anotações,
    /// folha) numa transação própria — a que falha é desfeita inteira e vai para o relatório, sem janela do Revit no meio.
    /// </summary>
    internal static class ExecucaoFormas
    {
        /// <summary>Padrão das pranchas de formas (%AppData%\PCEB\DEB.Revit\Padroes\PCEB-FORMAS-A1.json; criado na 1ª vez).</summary>
        public static PresentationStandard Padrao(PieceLog log)
        {
            PresentationStandard std = PresentationStandard.Load(Defaults.Formas, out string aviso);
            if (aviso != null) log.Warn(aviso);
            std.Formas ??= new FormasSettings();
            return std;
        }

        /// <summary>Cria os parâmetros que faltam e atualiza os dados calculados.</summary>
        public static bool Dados(Document doc, PresentationStandard std, PieceLog log) =>
            Etapa(doc, "Formas: atualizar dados", log, () =>
            {
                var avisos = new List<string>();
                int n = ParametrosFormas.Garantir(doc, avisos);
                foreach (string a in avisos) log.Warn(a);
                if (n > 0) log.Info($"{n} parâmetro(s) SD_ criado(s) no projeto.");
                doc.Regenerate();
                new DadosFormas(doc, std.Formas, log).Atualizar();
            });

        /// <summary>Planta de locação ou de montagem do nível: dados → tabela(s) → vista → anotações → folha (opcional).</summary>
        public static ViewSheet Planta(Document doc, PresentationStandard std, TipoPlanta tipo, Level nivel, ViewPlan indicada,
                                       string numero, string nome, bool folha, PieceLog log, out ViewPlan vista)
        {
            ViewPlan v = null;
            ViewSheet sheet = null;
            var tabelas = new List<ViewSchedule>();
            using (var tg = new TransactionGroup(doc, tipo == TipoPlanta.Locacao ? "Planta de locação" : "Planta de montagem"))
            {
                tg.Start();
                Dados(doc, std, log);
                Etapa(doc, "Formas: tabelas", log, () =>
                {
                    var t = new TabelasFormas(doc, std.Formas, log);
                    tabelas.Add(tipo == TipoPlanta.Locacao ? t.Cargas() : tipo == TipoPlanta.Paginacao ? t.Paineis(nivel) : t.Lajes(nivel));
                });
                var planta = new PlantaFormas(doc, std.Formas, log);
                if (Etapa(doc, "Formas: vista", log, () => v = planta.Vista(nivel, tipo, indicada)))
                {
                    Etapa(doc, "Formas: anotações", log, () => planta.Anotar(v, nivel, tipo));
                    if (folha)
                        Etapa(doc, "Formas: folha", log, () =>
                            sheet = new FolhaFormas(doc, std, log).Montar(tipo, nivel, v, tabelas.Where(x => x != null && x.IsValidObject), numero, nome));
                }
                else v = null;
                tg.Assimilate();
            }
            vista = v;
            return sheet != null && sheet.IsValidObject ? sheet : null;
        }

        /// <summary>Cortes (true) ou elevações (false): vistas do comando + identificadores + folha. Devolve a folha (ou null).</summary>
        public static ViewSheet Cortes(Document doc, PresentationStandard std, bool cortes, PieceLog log, out List<ViewSection> vistas)
        {
            List<ViewSection> vs = new List<ViewSection>();
            ViewSheet sheet = null;
            using (var tg = new TransactionGroup(doc, cortes ? "Cortes" : "Elevações"))
            {
                tg.Start();
                Dados(doc, std, log);
                var c = new CortesFormas(doc, std.Formas, log);
                if (Etapa(doc, "Formas: " + (cortes ? "cortes" : "elevações"), log, () => vs = c.Vistas(cortes)) && vs.Count > 0)
                {
                    var planta = new PlantaFormas(doc, std.Formas, log);
                    foreach (ViewSection v in vs) Etapa(doc, "Formas: identificadores em " + v.Name, log, () => c.Anotar(v, planta));
                    FormasSheetSpec spec = cortes ? std.Formas.Cortes : std.Formas.Elevacoes;
                    Etapa(doc, "Formas: folha", log, () => sheet = new FolhaFormas(doc, std, log)
                        .Montar(cortes ? "folha-cortes" : "folha-elevacoes", spec, null, vs, null, spec.NumeroFolha, spec.NomeFolha));
                }
                tg.Assimilate();
            }
            vistas = vs.Where(v => v.IsValidObject).ToList();
            return sheet != null && sheet.IsValidObject ? sheet : null;
        }

        /// <summary>Folha "Seções consideradas": tabelas por tipo de viga e de pilar.</summary>
        public static ViewSheet Secoes(Document doc, PresentationStandard std, PieceLog log)
        {
            ViewSheet sheet = null;
            using (var tg = new TransactionGroup(doc, "Seções consideradas"))
            {
                tg.Start();
                Dados(doc, std, log);
                var tabs = new List<ViewSchedule>();
                Etapa(doc, "Formas: tabelas de seções", log, () =>
                {
                    var t = new TabelasFormas(doc, std.Formas, log);
                    tabs.Add(t.Secoes(true));
                    tabs.Add(t.Secoes(false));
                });
                FormasSheetSpec spec = std.Formas.Secoes;
                Etapa(doc, "Formas: folha", log, () => sheet = new FolhaFormas(doc, std, log)
                    .Montar("folha-secoes", spec, null, new View[0], tabs, spec.NumeroFolha, spec.NomeFolha));
                tg.Assimilate();
            }
            return sheet != null && sheet.IsValidObject ? sheet : null;
        }

        /// <summary>Uma etapa = uma transação. Erro (exceção ou erro do Revit) desfaz a etapa e vai para o relatório.</summary>
        public static bool Etapa(Document doc, string nome, PieceLog log, Action acao)
        {
            using (var t = new Transaction(doc, nome))
            {
                var falhas = new List<string>();
                FailureHandlingOptions o = t.GetFailureHandlingOptions();
                o.SetFailuresPreprocessor(new SemJanelas(falhas));
                o.SetForcedModalHandling(false);
                o.SetClearAfterRollback(true);
                t.SetFailureHandlingOptions(o);
                try
                {
                    t.Start();
                    acao();
                    TransactionStatus st = t.Commit();
                    if (st == TransactionStatus.Committed) return true;
                    log.Error($"{nome}: o Revit não confirmou a etapa ({string.Join(" | ", falhas.Distinct().Take(5))}); etapa desfeita.");
                    return false;
                }
                catch (Exception ex)
                {
                    if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                    log.Error($"{nome}: {ex.Message} (etapa desfeita).");
                    log.Trace(nome, ex);
                    return false;
                }
            }
        }

        /// <summary>Avisos do Revit são descartados; erro desfaz a etapa (nunca abre janela no Revit do usuário).</summary>
        private sealed class SemJanelas : IFailuresPreprocessor
        {
            private readonly List<string> _falhas;
            public SemJanelas(List<string> f) { _falhas = f; }
            public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
            {
                foreach (FailureMessageAccessor f in fa.GetFailureMessages())
                {
                    if (f.GetSeverity() == FailureSeverity.Warning) { fa.DeleteWarning(f); continue; }
                    _falhas.Add(f.GetDescriptionText());
                    return FailureProcessingResult.ProceedWithRollBack;
                }
                return FailureProcessingResult.Continue;
            }
        }

        /// <summary>Texto do relatório (erros, avisos, o que foi feito) e o arquivo em logs\formas_*.txt (null se não gravou).</summary>
        public static string Relatorio(PieceLog log, string titulo, out string arquivo)
        {
            var sb = new StringBuilder();
            sb.AppendLine(titulo);
            if (log.Errors.Count > 0) { sb.AppendLine().AppendLine($"ERROS ({log.Errors.Count})"); foreach (string e in log.Errors) sb.AppendLine("• " + e); }
            if (log.Warnings.Count > 0) { sb.AppendLine().AppendLine($"AVISOS ({log.Warnings.Count})"); foreach (string w in log.Warnings) sb.AppendLine("• " + w); }
            sb.AppendLine().AppendLine("FEITO");
            foreach (string i in log.Infos) sb.AppendLine("• " + i);
            string texto = sb.ToString();
            arquivo = null;
            try
            {
                Directory.CreateDirectory(DetailingReport.LogDir);
                arquivo = Path.Combine(DetailingReport.LogDir, $"formas_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(arquivo, texto + (log.Traces.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, log.Traces) : ""), Encoding.UTF8);
            }
            catch { arquivo = null; }
            return texto;
        }

        /// <summary>Lê um CSV do Excel: UTF-8 (com ou sem BOM) ou, se não for UTF-8 válido, a página de código do Windows (Latin-1).</summary>
        public static string LerTexto(string caminho)
        {
            byte[] b = File.ReadAllBytes(caminho);
            try { return new UTF8Encoding(false, true).GetString(b).TrimStart('﻿'); }
            catch (DecoderFallbackException) { return Encoding.Latin1.GetString(b); }
        }
    }
}
