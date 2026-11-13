using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DEB.Revit.Core;
using DEB.Revit.Presentation;

namespace DEB.Revit.Formas
{
    /// <summary>
    /// Folha das pranchas de formas (D25, etapa 4): vista(s) + tabela(s) + legendas fixas do projeto (pelo nome) + notas fixas +
    /// carimbo do padrão, arranjados pelas regiões do padrão com o <see cref="SheetComposer"/>. Número e nome da folha pelos
    /// parâmetros da folha (por nível, se o padrão tiver). Nunca apaga folhas nem o que está nelas: se o número pedido já é de uma
    /// folha, ela é adotada e atualizada (decisão do usuário, 13/11/2026) — só o que o comando coloca é acrescentado e arranjado; o
    /// resto fica e é relatado. A folha do comando é reaproveitada e rearranjada. Precisa de transação.
    /// </summary>
    internal sealed class FolhaFormas
    {
        private readonly Document _doc;
        private readonly PresentationStandard _std;
        private readonly PieceLog _log;
        private LegendFactory _legendas;

        public FolhaFormas(Document doc, PresentationStandard std, PieceLog log) { _doc = doc; _std = std; _log = log; }

        public static string Papel(TipoPlanta t, Level l) => $"folha-{PlantaFormas.Chave(t)}:{l.UniqueId}";

        /// <summary>Número e nome da folha: os do nível no padrão (<see cref="FormasSheetSpec.PorNivel"/>), senão os dados.</summary>
        public static (string Numero, string Nome) NumeroENome(FormasSheetSpec spec, Level nivel, string numero, string nome)
        {
            if (nivel != null && spec?.PorNivel != null && spec.PorNivel.TryGetValue(nivel.Name, out FolhaNome fn) && fn != null)
                return (string.IsNullOrWhiteSpace(fn.Numero) ? numero : fn.Numero, string.IsNullOrWhiteSpace(fn.Nome) ? nome : fn.Nome);
            return (numero, nome);
        }

        public ViewSheet Montar(TipoPlanta tipo, Level nivel, ViewPlan planta, IEnumerable<ViewSchedule> tabelas, string numero, string nome)
        {
            FormasSheetSpec spec = Spec(tipo);
            var notas = new List<string>();
            if (tipo == TipoPlanta.Locacao && _std.Formas.FundacoesEsquematicas && !string.IsNullOrWhiteSpace(_std.Formas.NotaFundacoesEsquematicas))
                notas.Add(_std.Formas.NotaFundacoesEsquematicas);
            return Montar(Papel(tipo, nivel), spec, nivel, new[] { (View)planta }, tabelas, numero, nome, notas);
        }

        public FormasSheetSpec Spec(TipoPlanta t) => t switch
        {
            TipoPlanta.Locacao => _std.Formas.Locacao,
            TipoPlanta.Paginacao => _std.Formas.Paginacao,
            _ => _std.Formas.Montagem,
        };

        /// <summary>Folha genérica: <paramref name="papel"/> = marca do comando; vistas na região de plantas, tabelas na de tabelas.</summary>
        public ViewSheet Montar(string papel, FormasSheetSpec spec, Level nivel, IEnumerable<View> vistas, IEnumerable<ViewSchedule> tabelas,
                                string numero, string nome, IEnumerable<string> notasExtras = null)
        {
            (numero, nome) = NumeroENome(spec, nivel, numero, nome);
            numero = ModeloFormas.Nome(TabelasFormas.Fill(numero, nivel));
            nome = TabelasFormas.Fill(nome, nivel).Trim();
            if (numero == "") { _log.Warn("Número da folha vazio: folha não montada."); return null; }

            var ctx = new PresentationContext { Standard = _std, Values = new Dictionary<string, string> { ["Nivel"] = nivel?.Name ?? "", ["Marca"] = "", ["Numero"] = numero } };
            var composer = new SheetComposer(_doc, ctx, null, _log);
            List<ViewSheet> folhas = new FilteredElementCollector(_doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().ToList();
            ViewSheet sheet = folhas.FirstOrDefault(f => MarcaFormas.Papel(f) == papel);
            ViewSheet comNumero = folhas.FirstOrDefault(f => string.Equals(f.SheetNumber, numero, StringComparison.OrdinalIgnoreCase));
            // folha com o número pedido que já tem vistas de outra coisa (ex.: plantas de cargas): não mistura — a do comando
            // leva o sufixo do padrão ("-F") e a decisão fica com o usuário (13/11/2026)
            if (sheet == null && comNumero != null && MarcaFormas.Papel(comNumero) == "" &&
                Viewports(comNumero).Any(x => MarcaFormas.Papel(_doc.GetElement(x.ViewId)) == "" && (_doc.GetElement(x.ViewId) as View)?.ViewType != ViewType.Legend))
            {
                string antigo = numero;
                while (folhas.Any(f => string.Equals(f.SheetNumber, numero, StringComparison.OrdinalIgnoreCase) && MarcaFormas.Papel(f) != papel))
                    numero += _std.Formas.SufixoFolhaOcupada;
                _log.Warn($"A folha {antigo} já existe com vistas que não são do comando (\"{comNumero.Name}\"): a do comando ficou como {numero}. Decida a numeração.");
                comNumero = folhas.FirstOrDefault(f => string.Equals(f.SheetNumber, numero, StringComparison.OrdinalIgnoreCase));
            }
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
            foreach (View v in vistas.Where(x => x != null))
            {
                Viewport vp = Viewports(sheet).FirstOrDefault(x => x.ViewId == v.Id);
                if (vp == null)
                {
                    if (Viewport.CanAddViewToSheet(_doc, sheet.Id, v.Id)) vp = Viewport.Create(_doc, sheet.Id, v.Id, XYZ.Zero);
                    else _log.Warn($"A vista \"{v.Name}\" já está em outra folha: não foi colocada na {sheet.SheetNumber}.");
                }
                if (vp != null) itens.Add((vp, spec.RegiaoPlanta));
            }

            List<ScheduleSheetInstance> naFolha = new FilteredElementCollector(_doc, sheet.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>().ToList();
            foreach (ViewSchedule vs in (tabelas ?? Enumerable.Empty<ViewSchedule>()).Where(t => t != null && t.IsValidObject))
            {
                ScheduleSheetInstance si = naFolha.FirstOrDefault(x => x.ScheduleId == vs.Id) ?? ScheduleSheetInstance.Create(_doc, sheet.Id, vs.Id, XYZ.Zero);
                itens.Add((si, spec.RegiaoTabelas));
            }

            foreach (LegendSpec ls in (spec.Legendas ?? new List<LegendSpec>()).Where(l => l.Ativa && !string.IsNullOrWhiteSpace(l.Nome)))
            {
                // detalhe típico: a legenda com o nome; se não existe e o padrão aponta um DXF (cm, tamanho real), é criada dele uma
                // vez (linhas e textos nativos, LegendFactory — a mesma das pranchas dos pilares) e reaproveitada nas outras folhas
                View lg = LegendFactory.Find(_doc, ls.Nome.Trim());   // as do DXF são criadas antes, na etapa própria (CriarLegendas)
                if (lg == null) { _log.Warn($"Legenda \"{ls.Nome}\" não existe no projeto (não colocada)."); continue; }
                Viewport lvp = Viewports(sheet).FirstOrDefault(x => x.ViewId == lg.Id) ?? Viewport.Create(_doc, sheet.Id, lg.Id, XYZ.Zero);
                string reg = _std.Regioes.ContainsKey(ls.Regiao ?? "") ? ls.Regiao : "Legendas";
                itens.Add((lvp, reg));
            }

            // notas fixas num bloco "NOTAS" numerado (texto do comando: refeito a cada vez, nunca duplicado)
            var notas = new List<string>(notasExtras ?? Enumerable.Empty<string>());
            notas.AddRange(spec.Notas ?? new List<string>());
            notas = notas.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
            foreach (Element velha in MarcaFormas.ComPrefixo(new FilteredElementCollector(_doc, sheet.Id).OfClass(typeof(TextNote)), "nota:"))
                _doc.Delete(velha.Id);
            string regNotas = _std.Regioes.ContainsKey("Notas") ? "Notas" : "Legendas";
            if (notas.Count > 0)
            {
                Rect2 rn = Rect2Of(regNotas);
                string texto = "NOTAS:\n" + string.Join("\n", notas.Select((n, i) => $"{i + 1}) {n}"));
                TextNote tn = TextNote.Create(_doc, sheet.Id, XYZ.Zero, Math.Max(U.Mm(60), rn.W * 0.95), texto, new TextNoteOptions(TipoTexto()));
                MarcaFormas.Marcar(tn, "nota:0");
                itens.Add((tn, regNotas));
            }

            composer.ComposePlain(sheet, itens);
            Aproximar(sheet, itens, spec.RegiaoPlanta);
            return sheet;
        }

        /// <summary>
        /// Tabelas, notas e legendas sobem para logo abaixo das vistas (a região fixa deixava um vão grande quando a planta é baixa).
        /// Só o que o comando arranjou nesta folha.
        /// </summary>
        private void Aproximar(ViewSheet sheet, List<(Element Item, string Region)> itens, string regiaoVistas)
        {
            try
            {
                _doc.Regenerate();
                var vistas = itens.Where(i => i.Region == regiaoVistas && i.Item is Viewport).Select(i => ((Viewport)i.Item).GetBoxOutline()).ToList();
                var outros = itens.Where(i => i.Region != regiaoVistas).Select(i => i.Item).ToList();
                if (vistas.Count == 0 || outros.Count == 0) return;
                double baseVistas = vistas.Min(o => o.MinimumPoint.Y) - U.Mm(_std.Folha.TituloVistaMm + _std.Folha.EspacamentoMm);
                double topo = outros.Select(e => e.get_BoundingBox(sheet)).Where(b => b != null).Select(b => b.Max.Y).DefaultIfEmpty(double.MinValue).Max();
                double dy = baseVistas - topo;
                if (topo == double.MinValue || dy <= U.Mm(5)) return;
                ElementTransformUtils.MoveElements(_doc, outros.Select(e => e.Id).ToList(), new XYZ(0, dy, 0));
            }
            catch (Exception ex) { _log.Warn($"Tabelas/notas não aproximadas da planta: {ex.Message}"); }
        }

        /// <summary>Detalhes típicos da folha (legendas criadas dos DXF do padrão que ainda não existem). Etapa própria: um DXF ruim
        /// não desfaz a folha. Precisa de transação.</summary>
        public void CriarLegendas(FormasSheetSpec spec)
        {
            foreach (LegendSpec ls in (spec?.Legendas ?? new List<LegendSpec>()).Where(l => l.Ativa && !string.IsNullOrWhiteSpace(l.Nome) && !string.IsNullOrWhiteSpace(l.Arquivo)))
            {
                if (LegendFactory.Find(_doc, ls.Nome.Trim()) != null) continue;
                _legendas ??= new LegendFactory(_doc, AnnotationStyles.Ensure(_doc, _std), _log, _std.Folha.ArquivoLegendaBase);
                _legendas.Create(ls);
            }
        }

        /// <summary>Largura aproximada da região (fração da folha A0 útil), para a largura da caixa das notas.</summary>
        private Rect2 Rect2Of(string region)
        {
            Region g = _std.Regioes.TryGetValue(region, out Region r) ? r : new Region(0, 0, 0.3, 0.2);
            return new Rect2(0, 0, U.Mm(1100) * g.L, U.Mm(780) * g.A);
        }

        private ElementId TipoTexto()
        {
            string nome = _std.Formas.TipoTextoNotas?.Trim() ?? "";
            if (nome != "")
            {
                TextNoteType t = new FilteredElementCollector(_doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
                    .FirstOrDefault(x => string.Equals(x.Name, nome, StringComparison.OrdinalIgnoreCase));
                if (t != null) return t.Id;
                _log.Warn($"Tipo de texto das notas \"{nome}\" não existe; usado o padrão do projeto.");
            }
            return _doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
        }

        private List<Viewport> Viewports(ViewSheet s) => s.GetAllViewports().Select(_doc.GetElement).OfType<Viewport>().ToList();
    }
}
