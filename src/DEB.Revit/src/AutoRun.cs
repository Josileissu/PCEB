using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using DEB.Revit.Core;
using DEB.Revit.Precast;

namespace DEB.Revit
{
    /// <summary>
    /// Execução sem interação, para testes automatizados. Ativada por variáveis de ambiente
    /// ao abrir o Revit:
    ///   SD_AUTORUN  = diag   → grava o diagnóstico do modelo assim que um projeto estiver aberto
    ///   SD_AUTOEXIT = 1      → fecha o Revit ao terminar
    /// O resultado (caminho do arquivo gerado ou o erro) vai para %AppData%\PCEB\DEB.Revit\logs\autorun.txt.
    /// Sem essas variáveis, nada acontece.
    /// </summary>
    internal static class AutoRun
    {
        private static string _task;
        private static bool _exit;

        public static void Register(UIControlledApplication app)
        {
            _task = Environment.GetEnvironmentVariable("SD_AUTORUN");
            if (string.IsNullOrWhiteSpace(_task)) return;
            _exit = Environment.GetEnvironmentVariable("SD_AUTOEXIT") == "1";
            app.Idling += OnIdling;
            // instância de teste: janelas do Revit (avisos de atualização ao abrir arquivos antigos...) não podem parar o teste
            app.DialogBoxShowing += (s, e) =>
            {
                Dialogs.Add(e.DialogId);
                if (e is TaskDialogShowingEventArgs)
                {
                    // Ao sair com um arquivo LOCAL de modelo compartilhado (o modelo real de teste), o Revit pergunta o que fazer com as alterações
                    // não sincronizadas e essa janela não tem botão "Fechar": a resposta genérica era recusada e o Revit de teste
                    // nunca fechava (10/11/2026). Respostas próprias: não salvar, e manter a propriedade dos elementos (não fala com
                    // o modelo central). O teste nunca salva o modelo aberto.
                    string id = e.DialogId ?? "";
                    if (id == "TaskDialog_Local_Changes_Not_Synchronized_With_Central") e.OverrideResult(1003);        // 3ª opção: não salvar o projeto
                    else if (id.IndexOf("Relinquish", StringComparison.OrdinalIgnoreCase) >= 0 || id == "TaskDialog_Close_Project_Without_Saving" ||
                             id == "TaskDialog_Save_Changes_To_Local_File") e.OverrideResult(1002);                    // 2ª opção: manter a propriedade / não salvar
                    else if (id == "TaskDialog_Save_File" || id == "TaskDialog_Changes_Not_Saved") e.OverrideResult((int)TaskDialogResult.No);
                    else e.OverrideResult((int)TaskDialogResult.Close);
                }
                else e.OverrideResult(1);   // IDOK
            };
        }

        /// <summary>Janelas do Revit fechadas sozinhas durante o AutoRun (vão para o resultado).</summary>
        internal static readonly List<string> Dialogs = new List<string>();

        private static void OnIdling(object sender, IdlingEventArgs e)
        {
            var uiapp = (UIApplication)sender;
            string task = _task.Trim().ToLowerInvariant();
            bool semProjeto = task == "familias" || task == "criarmodelo" || task == "contarlegendas" || task == "exportar";   // tarefas que não precisam de projeto aberto
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (!semProjeto && (uidoc == null || uidoc.Document.IsFamilyDocument)) return;   // ainda abrindo o projeto

            uiapp.Idling -= OnIdling;
            string status;
            try
            {
                switch (task)
                {
                    case "familias":
                        status = "OK " + TestModel.InspectFamilies(uiapp.Application);
                        break;
                    case "criarmodelo":
                        status = "OK " + TestModel.BuildPrecastModel(uiapp.Application);
                        break;
                    case "diag":
                        status = "OK " + ModelDiagnostics.Run(uidoc.Document);
                        break;
                    case "probe":
                        status = "OK " + PrecastProbe.Run(uidoc.Document);
                        break;
                    case "precast":
                        status = "OK " + PrecastAutoTest(uidoc);
                        break;
                    case "conferir":
                        status = "OK " + ReviewAutoTest(uidoc.Document);
                        break;
                    case "conferireditar":
                        status = "OK " + ReviewEditAutoTest(uidoc.Document);
                        break;
                    case "exportarpecas":
                        status = "OK " + ExportPiecesAutoTest(uidoc.Document);
                        break;
                    case "exportar":
                        status = "OK " + ExportFile(uiapp.Application);
                        break;
                    case "contarlegendas":
                        status = "OK " + CountLegends(uiapp.Application);
                        break;
                    case "ferramentas":
                        status = "OK " + Tools.ToolsAutoTest.Run(uiapp, uidoc);
                        break;
                    default:
                        status = "ERRO tarefa desconhecida: " + _task;
                        break;
                }
            }
            catch (Exception ex)
            {
                status = "ERRO " + ex;
            }

            try
            {
                Directory.CreateDirectory(DetailingReport.LogDir);
                File.WriteAllText(Path.Combine(DetailingReport.LogDir, "autorun.txt"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {status}");
            }
            catch { /* sem log, sem drama */ }

            if (_exit)
                uiapp.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit));
        }

        /// <summary>
        /// Edições da conferência (etapa 3), sem mostrar a janela: grava a marca VT1 no 1º grupo de vigas e PT1 no 1º de pilares,
        /// "não exportar" no 2º grupo de vigas, levanta o modelo DE NOVO (as marcas e a decisão têm de vir do modelo) e exporta
        /// (SD_EXPORT = caminho do .json). Tudo dentro de um grupo de transações DESFEITO no fim: o modelo não muda.
        /// </summary>
        private static string ReviewEditAutoTest(Document doc)
        {
            string dir = Path.Combine(DetailingReport.LogDir, "conferireditar_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            using (var tg = new TransactionGroup(doc, "Conferir peças (teste)"))
            {
                tg.Start();
                try
                {
                    var notes = new List<string>();
                    var grupos = DEB.Core.Pecas.ConferenciaPecas.Agrupar(PieceReview.Run(doc, notes));
                    var vigas = grupos.Where(g => g.Representante.Tipo == DEB.Core.Pecas.TipoPeca.Viga).ToList();
                    var pilares = grupos.Where(g => g.Representante.Tipo == DEB.Core.Pecas.TipoPeca.Pilar).ToList();
                    if (vigas.Count > 0) sb.AppendLine("marca VT1: " + (PieceReviewEdits.SetMark(doc, vigas[0], "VT1") ?? "ok"));
                    if (vigas.Count > 1) sb.AppendLine("não exportar: " + (PieceReviewEdits.SetSkipped(doc, vigas[1], true) ?? "ok"));
                    if (pilares.Count > 0) sb.AppendLine("marca PT1: " + (PieceReviewEdits.SetMark(doc, pilares[0], "PT1") ?? "ok"));

                    // de novo, do modelo
                    var notes2 = new List<string>();
                    var pecas2 = PieceReview.Run(doc, notes2);
                    var arq = PrecastExport.Build(doc, pecas2, notes2);
                    string path = Environment.GetEnvironmentVariable("SD_EXPORT");
                    if (string.IsNullOrWhiteSpace(path)) path = Path.Combine(dir, "pecas.json");
                    arq.Gravar(path);
                    sb.AppendLine("pilares: " + string.Join(", ", arq.Pilares.Select(p => $"{p.Marca} x{p.Quantidade}")));
                    sb.AppendLine("vigas: " + string.Join(", ", arq.Vigas.Select(v => $"{v.Marca} x{v.Quantidade}")));
                    foreach (string n in arq.Notas) sb.AppendLine("nota: " + n);
                    sb.AppendLine("json: " + path);
                    try
                    {
                        var grupos2 = DEB.Core.Pecas.ConferenciaPecas.Agrupar(pecas2);
                        var w = new Precast.UI.PieceReviewWindow(grupos2, notes2, new Precast.UI.ReviewActions
                        {
                            Export = (p, n) => { }, SetMark = (g, m) => null, SetSkip = (g, v) => null,
                            SelecaoRevit = new HashSet<string>(pecas2.Take(1).Select(p => p.Id)),   // como se a 1ª peça estivesse selecionada no Revit
                        });
                        w.RenderToPng(Path.Combine(dir, "vigas_1.png"), 0, 0);
                        w.RenderToPng(Path.Combine(dir, "vigas_2.png"), 0, 1);
                        w.RenderToPng(Path.Combine(dir, "pilares_1.png"), 1, 0);
                    }
                    catch (Exception ex) { sb.AppendLine("ERRO ao renderizar a janela: " + ex); }
                }
                finally { tg.RollBack(); }
            }
            File.WriteAllText(Path.Combine(dir, "resultado.txt"), sb.ToString());
            return dir;
        }

        /// <summary>
        /// Exportação das peças para o CAD (ARMIMPORTARPECAS), sem janela. SD_EXPORT = caminho do .json (senão vai para logs).
        /// </summary>
        private static string ExportPiecesAutoTest(Document doc)
        {
            var arq = PrecastExport.Build(doc);
            string path = Environment.GetEnvironmentVariable("SD_EXPORT");
            if (string.IsNullOrWhiteSpace(path))
                path = Path.Combine(DetailingReport.LogDir, DEB.Core.Intercambio.ArquivoPecas.NomeArquivo(arq.Origem, DateTime.Now));
            arq.Gravar(path);
            return $"{path} (pilares: {arq.Pilares.Count} grupo(s), {arq.Pilares.Sum(p => p.Quantidade)} peça(s); vigas: {arq.Vigas.Count} grupo(s), {arq.Vigas.Sum(v => v.Quantidade)} peça(s))";
        }

        /// <summary>
        /// Conferência das peças (etapa 2), sem mostrar a janela: grava o resumo dos grupos (situação, trechos, avisos) e a janela
        /// em PNG para cada grupo (até 8 por aba) em logs\conferir_AAAAMMDD_HHMMSS.
        /// </summary>
        private static string ReviewAutoTest(Document doc)
        {
            string dir = Path.Combine(DetailingReport.LogDir, "conferir_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);
            var notes = new List<string>();
            var grupos = DEB.Core.Pecas.ConferenciaPecas.Agrupar(PieceReview.Run(doc, notes));
            var sb = new StringBuilder();
            foreach (string n in notes) sb.AppendLine("NOTA: " + n);
            foreach (var g in grupos)
            {
                var r = g.Representante;
                sb.AppendLine($"{r.Tipo} {g.Marcas} x{g.Quantidade} [{g.Situacao}] {r.FamiliaTipo} L={r.Comprimento:0.#}");
                foreach (var t in r.Trechos)
                    sb.AppendLine($"   {t.X0:0.#}-{t.X1:0.#} {t.FuncaoSugerida}: {t.Secao.Classificar().Descricao} A={t.Secao.Area:0.#} Iy={t.Secao.Iy:0}");
                foreach (string a in g.Avisos) sb.AppendLine("   aviso: " + a);
            }
            try
            {
                var w = new Precast.UI.PieceReviewWindow(grupos, notes);
                for (int tab = 0; tab < 2; tab++)
                    for (int i = 0; i < Math.Min(8, w.Count(tab)); i++)
                        w.RenderToPng(Path.Combine(dir, $"{(tab == 0 ? "vigas" : "pilares")}_{i + 1}.png"), tab, i);
                // SD_CONFERIR_INDICES = "16,18,31": grupos de vigas escolhidos (posição na lista, a partir de 1), além dos 8 primeiros
                foreach (string s in (Environment.GetEnvironmentVariable("SD_CONFERIR_INDICES") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(s.Trim(), out int ix) && ix >= 1 && ix <= w.Count(0))
                        w.RenderToPng(Path.Combine(dir, $"vigas_{ix}.png"), 0, ix - 1);
                // vigas com consolo de outra viga (viga apoiada em viga), até 6
                var vigas = grupos.Where(g => g.Representante.Tipo == DEB.Core.Pecas.TipoPeca.Viga).ToList();
                int n = 0;
                for (int i = 0; i < vigas.Count && n < 6; i++)
                    if (vigas[i].Representante.Consolos.Count > 0)
                        w.RenderToPng(Path.Combine(dir, $"viga_com_consolo_{++n}.png"), 0, i);
            }
            catch (Exception ex) { sb.AppendLine("ERRO ao renderizar a janela: " + ex); }
            File.WriteAllText(Path.Combine(dir, "resultado.txt"), sb.ToString());
            return $"{dir} ({grupos.Count} grupo(s), {grupos.Sum(g => g.Quantidade)} peça(s))";
        }

        /// <summary>
        /// SD_AUTORUN = exportar: abre SD_ARQUIVO (uma CÓPIA de um projeto de referência; nunca é salvo) e grava PNG de todas as
        /// folhas + um inventário (folhas e suas vistas, legendas, tabelas, barras, detalhes de dobra) em logs\exportar_*.
        /// </summary>
        private static string ExportFile(Autodesk.Revit.ApplicationServices.Application app)
        {
            string file = Environment.GetEnvironmentVariable("SD_ARQUIVO")?.Trim();
            string dir = Path.Combine(DetailingReport.LogDir, "exportar_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);
            Document d = Presentation.LegendFactory.OpenQuietly(app, file);
            var sb = new StringBuilder();
            sb.AppendLine(file);
            List<ViewSheet> sheets = new FilteredElementCollector(d).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder).OrderBy(s => s.SheetNumber).ToList();
            foreach (ViewSheet s in sheets)
            {
                sb.AppendLine($"FOLHA {s.SheetNumber} - {s.Name}" + (s.AssociatedAssemblyInstanceId != ElementId.InvalidElementId ? " (montagem)" : ""));
                foreach (ElementId vid in s.GetAllPlacedViews())
                {
                    View v = d.GetElement(vid) as View;
                    if (v == null) continue;
                    int bend = new FilteredElementCollector(d, v.Id).OfCategory(BuiltInCategory.OST_RebarBendingDetails).GetElementCount();
                    int tags = new FilteredElementCollector(d, v.Id).OfCategory(BuiltInCategory.OST_RebarTags).GetElementCount();
                    int dims = new FilteredElementCollector(d, v.Id).OfClass(typeof(Dimension)).GetElementCount();
                    int texts = new FilteredElementCollector(d, v.Id).OfClass(typeof(TextNote)).GetElementCount();
                    int lines = new FilteredElementCollector(d, v.Id).OfClass(typeof(CurveElement)).GetElementCount();
                    sb.AppendLine($"   {v.ViewType} \"{v.Name}\" 1:{v.Scale} | dobra {bend} | etiquetas {tags} | cotas {dims} | textos {texts} | linhas {lines}");
                    // cada etiqueta de vergalhão: tipo, texto, orientação, chamada e posição (mm de papel, a partir do canto
                    // inferior esquerdo do recorte) — para repetir no plugin o que o usuário ajustou à mão
                    BoundingBoxXYZ cb = v.CropBoxActive ? v.CropBox : null;
                    XYZ o = cb?.Transform.OfPoint(cb.Min);
                    string Pos(XYZ q) => o == null || q == null ? "?" :
                        $"({UnitUtils.ConvertFromInternalUnits((q - o).DotProduct(v.RightDirection), UnitTypeId.Millimeters) / v.Scale:0.0}; " +
                        $"{UnitUtils.ConvertFromInternalUnits((q - o).DotProduct(v.UpDirection), UnitTypeId.Millimeters) / v.Scale:0.0})";
                    foreach (IndependentTag tg in new FilteredElementCollector(d, v.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>()
                                 .Where(x => x.Category?.Id.Value == (long)BuiltInCategory.OST_RebarTags))
                    {
                        Element host = tg.GetTaggedLocalElements().FirstOrDefault();
                        string role = host?.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "";
                        BoundingBoxXYZ hb = host?.get_BoundingBox(v);
                        string leader = tg.HasLeader ? $" chamada {tg.LeaderEndCondition}" : "";
                        sb.AppendLine($"      etiqueta {d.GetElement(tg.GetTypeId())?.Name} \"{tg.TagText}\" {tg.TagOrientation}{leader} cabeça {Pos(tg.TagHeadPosition)}" +
                                      $" | barra [{role}] {(hb == null ? "" : Pos((hb.Min + hb.Max) / 2))}");
                    }
                }
                foreach (ScheduleSheetInstance si in new FilteredElementCollector(d, s.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
                    sb.AppendLine($"   tabela \"{si.Name}\"");
            }
            sb.AppendLine($"barras: {new FilteredElementCollector(d).OfClass(typeof(Autodesk.Revit.DB.Structure.Rebar)).GetElementCount()}");
            // parâmetros dos vergalhões (os do escritório, como QTD, aparecem aqui)
            foreach (Autodesk.Revit.DB.Structure.Rebar rb in new FilteredElementCollector(d).OfClass(typeof(Autodesk.Revit.DB.Structure.Rebar))
                         .Cast<Autodesk.Revit.DB.Structure.Rebar>().Take(3))
            {
                sb.AppendLine($"vergalhão {rb.Id.Value}:");
                foreach (Parameter prm in rb.Parameters.Cast<Parameter>().OrderBy(x => x.Definition.Name))
                    sb.AppendLine($"   {prm.Definition.Name}{(prm.IsShared ? " [compartilhado]" : prm.Id.Value > 0 ? " [projeto]" : "")} = {prm.AsValueString() ?? prm.AsString()}");
            }
            foreach (IndependentTag tg in new FilteredElementCollector(d).OfClass(typeof(IndependentTag)).Cast<IndependentTag>()
                         .Where(x => x.Category?.Id.Value == (long)BuiltInCategory.OST_RebarTags).Take(400)
                         .GroupBy(x => x.GetTypeId()).Select(gr => gr.First()))
                sb.AppendLine($"etiqueta usada: {(d.GetElement(tg.GetTypeId()) as FamilySymbol)?.FamilyName} : {d.GetElement(tg.GetTypeId())?.Name} → \"{tg.TagText}\"");
            sb.AppendLine($"detalhes de dobra: {new FilteredElementCollector(d).OfCategory(BuiltInCategory.OST_RebarBendingDetails).WhereElementIsNotElementType().GetElementCount()}");
            sb.AppendLine("legendas: " + string.Join(" | ", new FilteredElementCollector(d).OfClass(typeof(View)).Cast<View>()
                .Where(v => v.ViewType == ViewType.Legend && !v.IsTemplate).Select(v => v.Name)));
            if (sheets.Count > 0)
            {
                var ieo = new ImageExportOptions
                {
                    ExportRange = ExportRange.SetOfViews,
                    FilePath = Path.Combine(dir, "folha"),
                    FitDirection = FitDirectionType.Horizontal,
                    HLRandWFViewsFileType = ImageFileType.PNG,
                    ShadowViewsFileType = ImageFileType.PNG,
                    ImageResolution = ImageResolution.DPI_150,
                    PixelSize = 3000,
                    ZoomType = ZoomFitType.FitToPage
                };
                ieo.SetViewsAndSheets(sheets.Select(s => s.Id).ToList());
                d.ExportImage(ieo);
            }
            string log = Path.Combine(dir, "inventario.txt");
            File.WriteAllText(log, sb.ToString(), Encoding.UTF8);
            d.Close(false);
            return log;
        }

        /// <summary>SD_AUTORUN = contarlegendas: legendas de cada arquivo de SD_FAMILIAS (.rte/.rvt separados por ";"),
        /// para escolher o ArquivoLegendaBase do padrão.</summary>
        private static string CountLegends(Autodesk.Revit.ApplicationServices.Application app)
        {
            var sb = new StringBuilder();
            foreach (string f in (Environment.GetEnvironmentVariable("SD_FAMILIAS") ?? "").Split(';').Where(x => x.Trim() != ""))
            {
                try
                {
                    Document d = Presentation.LegendFactory.OpenQuietly(app, f.Trim());
                    List<string> names = new FilteredElementCollector(d).OfClass(typeof(View)).Cast<View>()
                        .Where(v => v.ViewType == ViewType.Legend && !v.IsTemplate).Select(v => v.Name).ToList();
                    sb.AppendLine($"{f}: {names.Count} legenda(s) {string.Join(" | ", names)}");
                    d.Close(false);
                }
                catch (Exception ex) { sb.AppendLine($"{f}: ERRO {ex.Message}"); }
            }
            if (Dialogs.Count > 0) sb.AppendLine("janelas fechadas: " + string.Join(", ", Dialogs));
            string log = Path.Combine(DetailingReport.LogDir, "legendas.txt");
            File.WriteAllText(log, sb.ToString(), Encoding.UTF8);
            return log;
        }

        /// <summary>
        /// Fluxo completo de pilares pré-moldados com a armadura padrão, sem janela.
        /// SD_SAVEAS = caminho de uma CÓPIA do modelo para gravar o resultado (o original nunca é salvo).
        /// Exporta as folhas e vistas 3D geradas como PNG para conferência visual.
        /// SD_VARIAR = 1: pilares "50x50" ficam 1,50 m mais altos (comprimentos diferentes) e cada grupo recebe outra
        /// armadura (bitolas e quantidades diferentes), para testar pranchas com barras e comprimentos variados.
        /// </summary>
        private static string PrecastAutoTest(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            bool vary = Environment.GetEnvironmentVariable("SD_VARIAR") == "1";
            if (vary)
                using (var t = new Transaction(doc, "Teste: comprimentos diferentes"))
                {
                    t.Start();
                    foreach (FamilyInstance c in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralColumns)
                                 .WhereElementIsNotElementType().OfType<FamilyInstance>().Where(c => c.Symbol.Name.Contains("50x50")))
                        c.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM)?.Set(UnitUtils.ConvertToInternalUnits(1500, UnitTypeId.Millimeters));
                    t.Commit();
                }
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string dir = Path.Combine(DetailingReport.LogDir, "precast_" + stamp);
            Directory.CreateDirectory(dir);

            List<ColumnGroup> groups = PrecastSurvey.Run(doc, out List<string> notes);
            var sb = new StringBuilder();
            foreach (string n in notes) sb.AppendLine("NOTA: " + n);
            if (vary)
                for (int i = 0; i < groups.Count; i++)
                {
                    ColumnRebarSpec sp = groups[i].Spec;
                    switch (i % 3)
                    {
                        case 1: sp.LongDiameterMm = 16; sp.LongBarsAlongX = 3; sp.LongBarsAlongY = 3; sp.StirrupDiameterMm = 6.3; sp.TieDiameterMm = 16; break;
                        case 2: sp.LongDiameterMm = 20; sp.LongBarsAlongX = 2; sp.LongBarsAlongY = 3; sp.StirrupDiameterMm = 8; sp.StirrupSpacingMm = 200; break;
                    }
                }
            foreach (ColumnGroup g in groups)
            {
                sb.AppendLine($"{g.Mark}: {g.Members.Count} un. | {g.Signature}");
                foreach (PrecastColumn m in g.Members)
                    sb.AppendLine($"   pilar {m.Id} giro {m.Rotation * 90}° consolos {m.Corbels.Count}");
                foreach (string w in g.Spec.Check(g.Representative)) sb.AppendLine("   verificação: " + w);
                foreach (BarDef b in ColumnRebarLayout.Compute(g.Representative.Frame, g.Representative.Corbels, g.Spec))
                    sb.AppendLine($"   barra {b.Role} Ø{b.DiameterMm} x{b.Count} pts={b.Points.Count}");
            }

            try
            {
                new Precast.UI.PrecastColumnsWindow(groups, notes).RenderToPng(Path.Combine(dir, "janela.png"));
            }
            catch (Exception ex)
            {
                sb.AppendLine("ERRO ao renderizar a janela: " + ex);
            }

            DetailingConfig cfg = DetailingConfig.Load(out _);
            cfg.OpenSheetWhenDone = false;
            var report = new DetailingReport();
            var pipeline = new PrecastColumnPipeline(doc, cfg, report, uidoc.ActiveView?.Id ?? ElementId.InvalidElementId);
            using (var tg = new TransactionGroup(doc, "Pilares pré-moldados (teste)"))
            {
                tg.Start();
                pipeline.Run(groups, new PipelineOptions());
                tg.Assimilate();
            }
            sb.AppendLine().AppendLine(report.Summary()).AppendLine().AppendLine(report.Details());
            foreach (PieceLog p in report.Pieces)
                foreach (string t in p.Traces) sb.AppendLine().AppendLine(t);

            // Imagens das folhas e das vistas 3D de assembly.
            List<ElementId> ids = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && v.AssociatedAssemblyInstanceId != ElementId.InvalidElementId &&
                            (v is ViewSheet || v is View3D))
                .Select(v => v.Id).ToList();
            if (ids.Count > 0)
            {
                var ieo = new ImageExportOptions
                {
                    ExportRange = ExportRange.SetOfViews,
                    FilePath = Path.Combine(dir, "vista"),
                    FitDirection = FitDirectionType.Horizontal,
                    HLRandWFViewsFileType = ImageFileType.PNG,
                    ShadowViewsFileType = ImageFileType.PNG,
                    ImageResolution = ImageResolution.DPI_150,
                    PixelSize = 2400,
                    ZoomType = ZoomFitType.FitToPage
                };
                ieo.SetViewsAndSheets(ids);
                doc.ExportImage(ieo);
            }

            string saveAs = Environment.GetEnvironmentVariable("SD_SAVEAS");
            if (!string.IsNullOrWhiteSpace(saveAs))
            {
                doc.SaveAs(saveAs, new SaveAsOptions { OverwriteExistingFile = true });
                sb.AppendLine("Modelo salvo em " + saveAs);
            }

            string log = Path.Combine(dir, "resultado.txt");
            File.WriteAllText(log, sb.ToString(), Encoding.UTF8);
            return log;
        }
    }
}
