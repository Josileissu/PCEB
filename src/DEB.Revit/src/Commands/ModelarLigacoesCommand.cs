using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DEB.Core.Analise.Dimensionamento;
using DEB.Core.Normas.Verificacao;
using DEB.Revit.Analise;
using DEB.Revit.Configuracao;

namespace DEB.Revit.Commands
{
    /// <summary>
    /// "Modelar ligações" (D49): analisa o modelo (peças de aço e ligações da biblioteca), verifica pela NBR 8800:2024, mostra o
    /// resumo e, aceito, cria as chapas e os parafusos com as famílias PCEB (um grupo por ligação; rodar de novo atualiza).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ModelarLigacoesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc == null) return Result.Cancelled;
            var notas = new List<string>();
            var e = ModeloAnaliticoCommand.Montar(doc, notas);
            if (e == null || e.LigacoesMetalicas.Count == 0)
            {
                TaskDialog.Show("Modelar ligações", "Nenhuma ligação metálica encontrada (vigas e pilares com material Aço e perfil I/H reconhecido)." +
                                (notas.Count > 0 ? "\n\n" + string.Join("\n", notas) : ""));
                return Result.Cancelled;
            }
            var res = DimensionamentoObjetos.Verificar(e, ConfiguracaoRevit.Carregar(doc));
            var dasLig = res.Where(r => e.LigacoesMetalicas.Any(l => l.Dono.Id == r.Dono.Id)).ToList();
            var sb = new StringBuilder();
            foreach (var g in e.LigacoesMetalicas.GroupBy(l => l.Modelo?.Nome ?? l.Tipo.ToString()))
                sb.AppendLine($"• {g.Count()} × {g.Key}");
            int nao = dasLig.Count(r => r.Situacao == SituacaoVerificacao.NaoAtende), conf = dasLig.Count(r => r.Situacao == SituacaoVerificacao.Conferir);
            sb.AppendLine();
            sb.AppendLine($"Verificação (NBR 8800:2024): {dasLig.Count - nao - conf} atendem, {conf} a conferir, {nao} não atendem.");
            foreach (var r in dasLig.Where(r => r.Situacao == SituacaoVerificacao.NaoAtende).Take(6)) sb.AppendLine($"  ✗ {r.Dono}: {string.Join("; ", r.Resumo)}");
            var td = new TaskDialog("Modelar ligações")
            {
                MainInstruction = $"Modelar {e.LigacoesMetalicas.Count} ligações com as famílias PCEB?",
                MainContent = sb.ToString(),
                FooterText = "As ligações já modeladas pelo PCEB são substituídas. Os detalhes estão na janela \"Modelo de análise\".",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            };
            if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;
            var log = new StringBuilder();
            int n = LigacoesRevit.Modelar(doc, e, log);
            TaskDialog.Show("Modelar ligações", log.ToString());
            return n > 0 ? Result.Succeeded : Result.Failed;
        }
    }
}
