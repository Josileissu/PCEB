using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DEB.Revit.Core;
using DEB.Revit.Presentation;

namespace DEB.Revit.Formas
{
    /// <summary>
    /// Folha da planta de formas (D25, etapa 4): planta + tabela(s) + legendas fixas do projeto (pelo nome) + carimbo do padrão,
    /// arranjados pelas regiões do padrão com o <see cref="SheetComposer"/>. Número e nome da folha pelos parâmetros da folha.
    /// Nunca apaga folhas nem o que está nelas: se o número pedido já é de uma folha, ela é adotada e atualizada (decisão do
    /// usuário, 13/11/2026) — só o que o comando coloca (planta, tabelas, legendas) é acrescentado e arranjado; o resto fica e é
    /// relatado. A folha do comando é reaproveitada e rearranjada. Precisa de transação.
    /// </summary>
    internal sealed class FolhaFormas
    {
        private readonly Document _doc;
        private readonly PresentationStandard _std;
        private readonly PieceLog _log;

        public FolhaFormas(Document doc, PresentationStandard std, PieceLog log) { _doc = doc; _std = std; _log = log; }

        public static string Papel(TipoPlanta t, Level l) => $"folha-{PlantaFormas.Chave(t)}:{l.UniqueId}";

        public ViewSheet Montar(TipoPlanta tipo, Level nivel, ViewPlan planta, IEnumerable<ViewSchedule> tabelas, string numero, string nome)
        {
            FormasSheetSpec spec = tipo == TipoPlanta.Locacao ? _std.Formas.Locacao : _std.Formas.Montagem;
            numero = ModeloFormas.Nome(TabelasFormas.Fill(numero, nivel));
            nome = TabelasFormas.Fill(nome, nivel).Trim();
            if (numero == "") { _log.Warn("Número da folha vazio: folha não montada."); return null; }

            var ctx = new PresentationContext { Standard = _std, Values = new Dictionary<string, string> { ["Nivel"] = nivel.Name, ["Marca"] = "", ["Numero"] = numero } };
            var composer = new SheetComposer(_doc, ctx, null, _log);
            List<ViewSheet> folhas = new FilteredElementCollector(_doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().ToList();
            string papel = Papel(tipo, nivel);
            ViewSheet sheet = folhas.FirstOrDefault(f => MarcaFormas.Papel(f) == papel);
            ViewSheet comNumero = folhas.FirstOrDefault(f => string.Equals(f.SheetNumber, numero, StringComparison.OrdinalIgnoreCase));
            if (sheet == null && comNumero != null)
            {
                // decisão do usuário (13/11/2026): vale o número da referência; a folha que já tem esse número é ATUALIZADA (o comando
                // só acrescenta/atualiza o que é dele; o que já estava nela fica onde está e vai para o relatório)
                sheet = comNumero;
                MarcaFormas.Marcar(sheet, papel);
                var havia = Viewports(sheet).Select(x => _doc.GetElement(x.ViewId)?.Name)
                    .Concat(new FilteredElementCollector(_doc, sheet.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>()
                        .Where(x => !x.IsTitleblockRevisionSchedule).Select(x => "tabela " + x.Name))
                    .Where(n => n != null).ToList();
                _log.Warn($"A folha {numero} (\"{sheet.Name}\") já existia: foi adotada e atualizada pelo comando. Já havia nela: " +
                          (havia.Count == 0 ? "nada além do carimbo" : string.Join("; ", havia)) + " — mantido; confira sobreposições.");
            }
            if (sheet == null)
            {
                sheet = ViewSheet.Create(_doc, composer.TitleBlock());
                sheet.SheetNumber = numero;
                MarcaFormas.Marcar(sheet, papel);
                _log.Info($"Folha {numero} criada.");
            }
            else
            {
                if (!string.Equals(sheet.SheetNumber, numero, StringComparison.OrdinalIgnoreCase))
                {
                    if (comNumero == null) sheet.SheetNumber = numero;
                    else _log.Warn($"Número {numero} já é da folha \"{comNumero.Name}\"; a folha do comando continua como {sheet.SheetNumber}.");
                }
                _log.Info($"Folha {sheet.SheetNumber} (do comando) atualizada.");
            }
            if (nome != "") sheet.Name = nome;
            composer.FillTitleBlock(sheet);

            var itens = new List<(Element, string)>();
            Viewport vp = Viewports(sheet).FirstOrDefault(x => x.ViewId == planta.Id);
            if (vp == null)
            {
                if (Viewport.CanAddViewToSheet(_doc, sheet.Id, planta.Id)) vp = Viewport.Create(_doc, sheet.Id, planta.Id, XYZ.Zero);
                else _log.Warn($"A vista \"{planta.Name}\" já está em outra folha: não foi colocada na {sheet.SheetNumber}.");
            }
            if (vp != null) itens.Add((vp, spec.RegiaoPlanta));

            List<ScheduleSheetInstance> naFolha = new FilteredElementCollector(_doc, sheet.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>().ToList();
            foreach (ViewSchedule vs in tabelas.Where(t => t != null))
            {
                ScheduleSheetInstance si = naFolha.FirstOrDefault(x => x.ScheduleId == vs.Id) ?? ScheduleSheetInstance.Create(_doc, sheet.Id, vs.Id, XYZ.Zero);
                itens.Add((si, spec.RegiaoTabelas));
            }

            foreach (LegendSpec ls in (spec.Legendas ?? new List<LegendSpec>()).Where(l => l.Ativa && !string.IsNullOrWhiteSpace(l.Nome)))
            {
                View lg = LegendFactory.Find(_doc, ls.Nome.Trim());
                if (lg == null) { _log.Warn($"Legenda \"{ls.Nome}\" não existe no projeto (não colocada)."); continue; }
                Viewport lvp = Viewports(sheet).FirstOrDefault(x => x.ViewId == lg.Id) ?? Viewport.Create(_doc, sheet.Id, lg.Id, XYZ.Zero);
                string reg = _std.Regioes.ContainsKey(ls.Regiao ?? "") ? ls.Regiao : "Legendas";
                itens.Add((lvp, reg));
            }

            composer.ComposePlain(sheet, itens);
            return sheet;
        }

        private List<Viewport> Viewports(ViewSheet s) => s.GetAllViewports().Select(_doc.GetElement).OfType<Viewport>().ToList();
    }
}
