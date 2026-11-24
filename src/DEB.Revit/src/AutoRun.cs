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
using DEB.Core.Elementos.Pilar.PreMoldado;
using DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D;

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
            bool semProjeto = task == "familias" || task == "familiassonda" || task == "familiaspceb" || task == "criarmodelo" || task == "contarlegendas" || task == "exportar" || task == "janelas"   // tarefas que não precisam de projeto aberto
                              || (task.StartsWith("formas") && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SD_MODELO")));   // formas: abre SD_MODELO pela API
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
                    case "janelas":
                        status = "OK " + DEB.Revit.UI.JanelasDemo.Run();
                        break;
                    case "familiassonda":
                        status = "OK " + FamiliasPceb.Sonda(uiapp.Application);
                        break;
                    case "familiaspceb":
                        status = "OK " + FamiliasPceb.Aplicar(uiapp.Application);
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
                    case "analitico":
                        status = "OK " + AnaliticoAutoTest(uidoc);
                        break;
                    case "configuracao":
                        status = "OK " + ConfiguracaoAutoTest(uidoc);
                        break;
                    case "ligacoes":
                    {
                        string dir = Path.Combine(DetailingReport.LogDir, "ligacoes_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                        Directory.CreateDirectory(dir);
                        var av = new List<string>();
                        string png = Path.Combine(dir, "janela.png");
                        DEB.Revit.Commands.BibliotecaLigacoesCommand.Abrir(IntPtr.Zero, av, png);
                        File.WriteAllText(Path.Combine(dir, "resultado.txt"), $"Janela: {(File.Exists(png) ? new FileInfo(png).Length + " bytes" : "não gravada")}" + Environment.NewLine + string.Join(Environment.NewLine, av), Encoding.UTF8);
                        status = "OK " + Path.Combine(dir, "resultado.txt");
                        break;
                    }
                    case "parametros":
                    {
                        // botão "Parâmetros do PCEB" (D50): o que falta, criar, conferir e rodar de novo (nada a criar)
                        string dir = Path.Combine(DetailingReport.LogDir, "parametros_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                        Directory.CreateDirectory(dir);
                        var grupos = DEB.Revit.Commands.ParametrosPcebCommand.Grupos();
                        var sb = new StringBuilder();
                        sb.AppendLine("Antes:"); sb.AppendLine(DEB.Revit.Commands.ParametrosPcebCommand.Faltando(uidoc.Document, grupos));
                        sb.AppendLine("Criação:"); sb.AppendLine(DEB.Revit.Commands.ParametrosPcebCommand.Criar(uidoc.Document, grupos));
                        string resta = DEB.Revit.Commands.ParametrosPcebCommand.Faltando(uidoc.Document, grupos);
                        sb.AppendLine("Depois: " + (resta.Length == 0 ? "nada falta" : resta));
                        sb.AppendLine("De novo:"); sb.AppendLine(DEB.Revit.Commands.ParametrosPcebCommand.Criar(uidoc.Document, grupos));
                        var piso = new FilteredElementCollector(uidoc.Document).OfClass(typeof(Floor)).FirstElement();
                        if (piso != null) sb.AppendLine($"Piso {piso.Id}: {DEB.Revit.Analise.ParametrosAnalise.Ler(piso, DEB.Revit.Analise.ParametrosAnalise.LajeG)?.Definition.Name ?? "sem o parâmetro"}");
                        File.WriteAllText(Path.Combine(dir, "resultado.txt"), sb.ToString(), Encoding.UTF8);
                        status = "OK " + Path.Combine(dir, "resultado.txt");
                        break;
                    }
                    case "muro":
                        status = "OK " + MuroAutoTest(uidoc);
                        break;
                    case "vigas":
                        status = "OK " + Vigas.VigasAutoTest.Run(uidoc);
                        break;
                    case "escadas":
                        status = "OK " + Escadas.EscadasAutoTest.Run(uidoc);
                        break;
                    case "conferir":
                        status = "OK " + ReviewAutoTest(uidoc.Document);
                        break;
                    case "conferireditar":
                        status = "OK " + ReviewEditAutoTest(uidoc.Document);
                        break;
                    case "furos":
                        status = "OK " + OpeningsAutoTest(uidoc.Document);
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
                    case "formasdados":
                    case "formaslocacao":
                    case "formasmontagem":
                    case "formassonda":
                    case "formaslevantar":
                    case "formaslinhas":
                    case "formasrenumerar":
                    case "formascorteanotar":
                    case "formaspaginacao":
                    case "formascortes":
                    case "formassecoes":
                    case "formastudo":
                        status = "OK " + Formas.FormasAutoTest.Run(uiapp, uidoc, task);
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
        /// Furos na alma (2026-11-10): corta duas aberturas de 20 × 15 cm na 1ª viga do modelo (NewOpening, plano vertical do eixo),
        /// levanta a peça como a conferência e grava os trechos, as aberturas lidas e a janela em PNG; tudo DESFEITO no fim.
        /// </summary>
        private static string OpeningsAutoTest(Document doc)
        {
            string dir = Path.Combine(DetailingReport.LogDir, "furos_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            var beam = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming).WhereElementIsNotElementType()
                .OfType<FamilyInstance>().First(f => f.SuperComponent == null);
            var info = PieceAnalyzer.Analyze(beam);
            using (var tg = new TransactionGroup(doc, "Furos (teste)"))
            {
                tg.Start();
                try
                {
                    using (var t = new Transaction(doc, "Furos (teste)"))
                    {
                        t.Start();
                        foreach (double f in new[] { 0.3, 0.62 })
                        {
                            XYZ c = info.Origin + info.AxisX * (info.LengthX * f) + info.AxisY * (info.LengthY / 2) + info.AxisZ * (info.LengthZ / 2);
                            double a = U.Mm(100), h = U.Mm(75);
                            var q = new[] { c - info.AxisX * a - info.AxisZ * h, c + info.AxisX * a - info.AxisZ * h, c + info.AxisX * a + info.AxisZ * h, c - info.AxisX * a + info.AxisZ * h };
                            var ca = new CurveArray();
                            for (int i = 0; i < 4; i++) ca.Append(Line.CreateBound(q[i], q[(i + 1) % 4]));
                            try { doc.Create.NewOpening(beam, ca, Autodesk.Revit.Creation.eRefFace.CenterY); sb.AppendLine($"abertura criada em {f:0.##} L"); }
                            catch (Exception ex) { sb.AppendLine("NewOpening falhou: " + ex.Message); }
                        }
                        // furo redondo de ø10 cm (2026-11-11: o laço com arco vira abertura circular no arquivo do CAD)
                        {
                            XYZ c = info.Origin + info.AxisX * (info.LengthX * 0.45) + info.AxisY * (info.LengthY / 2) + info.AxisZ * (info.LengthZ / 2);
                            double rr = U.Mm(50);
                            var ca = new CurveArray();
                            ca.Append(Arc.Create(c, rr, 0, Math.PI, info.AxisX, info.AxisZ));
                            ca.Append(Arc.Create(c, rr, Math.PI, 2 * Math.PI, info.AxisX, info.AxisZ));
                            try { doc.Create.NewOpening(beam, ca, Autodesk.Revit.Creation.eRefFace.CenterY); sb.AppendLine("abertura redonda criada em 0,45 L"); }
                            catch (Exception ex) { sb.AppendLine("NewOpening (redonda) falhou: " + ex.Message); }
                        }
                        t.Commit();
                    }
                    var p = PieceReview.Survey(beam, new List<FamilyInstance>());
                    sb.AppendLine(FormattableString.Invariant($"{p.Marca} L={p.Comprimento:0.#} [{p.Situacao}] trechos: {p.Trechos.Count}"));
                    foreach (var tr in p.Trechos) sb.AppendLine(FormattableString.Invariant($"   {tr.X0:0.#}-{tr.X1:0.#}: {tr.Secao.Classificar().Descricao}"));
                    foreach (var ab in p.Aberturas) sb.AppendLine(FormattableString.Invariant($"   abertura x {ab.X0:0.#}-{ab.X1:0.#} z {ab.Z0:0.#}-{ab.Z1:0.#}{(ab.Passante ? " passante" : " nicho")}{(ab.Circular ? " circular" : "")}"));
                    foreach (string av in p.Avisos) sb.AppendLine("   aviso: " + av);
                    // o arquivo do CAD com as aberturas (versão 4)
                    try
                    {
                        var arq = DEB.Core.Intercambio.ConversorLevantamento.Converter(new[] { p }, Path.GetFileName(doc.PathName), DateTime.Now.ToString("s"));
                        arq.Gravar(Path.Combine(dir, "pecas.json"));
                        sb.AppendLine($"exportado: {arq.Vigas.Count} viga(s), versão {arq.VersaoNecessaria}, aberturas {arq.Vigas.Sum(v => v.Aberturas.Count)}");
                        foreach (var n in arq.Notas) sb.AppendLine("   nota: " + n);
                    }
                    catch (Exception ex) { sb.AppendLine("ERRO ao exportar: " + ex.Message); }
                    try { new Precast.UI.PieceReviewWindow(DEB.Core.Pecas.ConferenciaPecas.Agrupar(new[] { p }), new List<string>()).RenderToPng(Path.Combine(dir, "viga_com_furos.png"), 0, 0); }
                    catch (Exception ex) { sb.AppendLine("ERRO ao renderizar: " + ex.Message); }
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
                foreach (var t in r.Trechos.Where(t => g.Situacao == DEB.Core.Pecas.SituacaoPeca.NaoSuportada && t.Secao.Classificar().Tipo == DEB.Core.Pecas.TipoSecao.Poligonal))
                    sb.AppendLine("      contorno " + string.Join(" ", t.Secao.Contorno.Select(q => FormattableString.Invariant($"{q.Y:0.##},{q.Z:0.##}"))));
                foreach (var ab in r.Aberturas) sb.AppendLine(FormattableString.Invariant($"   abertura x {ab.X0:0.#}-{ab.X1:0.#} z {ab.Z0:0.#}-{ab.Z1:0.#}{(ab.Passante ? " passante" : " nicho")}"));
                foreach (string a in g.Avisos) sb.AppendLine("   aviso: " + a);
            }
            try
            {
                var w = new Precast.UI.PieceReviewWindow(grupos, notes);
                for (int tab = 0; tab < 2; tab++)
                    for (int i = 0; i < Math.Min(8, w.Count(tab)); i++)
                        w.RenderToPng(Path.Combine(dir, $"{(tab == 0 ? "vigas" : "pilares")}_{i + 1}.png"), tab, i);
                // SD_CONFERIR_MARCA = "V215": os grupos de vigas com essa marca
                string marca = Environment.GetEnvironmentVariable("SD_CONFERIR_MARCA");
                var vigasM = grupos.Where(g => g.Representante.Tipo == DEB.Core.Pecas.TipoPeca.Viga).ToList();
                if (!string.IsNullOrWhiteSpace(marca))
                    for (int i = 0; i < vigasM.Count; i++)
                        if (vigasM[i].Pecas.Any(q => string.Equals(q.Marca, marca.Trim(), StringComparison.OrdinalIgnoreCase)))
                            w.RenderToPng(Path.Combine(dir, $"viga_{marca.Trim()}_{i + 1}.png"), 0, i);
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
        /// <summary>
        /// Modelo de análise (D46): monta e analisa a estrutura por objetos e grava a imagem da janela própria do PCEB em três modos
        /// (modelo, diagrama de momentos, deformada). Não escreve no modelo do Revit.
        /// </summary>
        private static string AnaliticoAutoTest(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            string dir = Path.Combine(DetailingReport.LogDir, "analitico_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            var notas = new List<string>();
            // SD_LAJE = 1: piso estrutural de 12 cm sobre as vigas (no documento aberto, sem gravar), para testar as lajes;
            // "abertura" acrescenta um furo de 2 × 2 m no meio; "alveolar" usa um tipo de piso com "alveolar" no nome
            string sdLaje = Environment.GetEnvironmentVariable("SD_LAJE") ?? "";
            if (sdLaje != "")
            {
                var vigas = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming).WhereElementIsNotElementType().ToList();
                var bbs = vigas.Select(x => x.get_BoundingBox(null)).Where(x => x != null).ToList();
                if (bbs.Count > 0)
                {
                    double x0 = bbs.Min(b => b.Min.X), x1 = bbs.Max(b => b.Max.X), y0 = bbs.Min(b => b.Min.Y), y1 = bbs.Max(b => b.Max.Y), zt = bbs.Max(b => b.Max.Z);
                    Level nivel = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Where(l => l.Elevation <= zt).OrderByDescending(l => l.Elevation).FirstOrDefault()
                                  ?? new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().First();
                    using (var t = new Transaction(doc, "Laje de teste"))
                    {
                        t.Start();
                        var tipo = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().First(ft => !ft.IsFoundationSlab);
                        if (sdLaje.Contains("alveolar")) tipo = (FloorType)tipo.Duplicate("PCEB teste laje alveolar " + DateTime.Now.ToString("HHmmss"));
                        var loop = CurveLoop.Create(new List<Curve>
                        {
                            Line.CreateBound(new XYZ(x0, y0, 0), new XYZ(x1, y0, 0)), Line.CreateBound(new XYZ(x1, y0, 0), new XYZ(x1, y1, 0)),
                            Line.CreateBound(new XYZ(x1, y1, 0), new XYZ(x0, y1, 0)), Line.CreateBound(new XYZ(x0, y1, 0), new XYZ(x0, y0, 0)),
                        });
                        var lacos = new List<CurveLoop> { loop };
                        if (sdLaje.Contains("abertura"))
                        {
                            double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, a = 100 / 30.48;
                            lacos.Add(CurveLoop.Create(new List<Curve>
                            {
                                Line.CreateBound(new XYZ(cx - a, cy - a, 0), new XYZ(cx - a, cy + a, 0)), Line.CreateBound(new XYZ(cx - a, cy + a, 0), new XYZ(cx + a, cy + a, 0)),
                                Line.CreateBound(new XYZ(cx + a, cy + a, 0), new XYZ(cx + a, cy - a, 0)), Line.CreateBound(new XYZ(cx + a, cy - a, 0), new XYZ(cx - a, cy - a, 0)),
                            }));
                        }
                        Floor f = Floor.Create(doc, lacos, tipo.Id, nivel.Id);
                        f.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.Set(zt - nivel.Elevation);
                        f.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL)?.Set(1);
                        t.Commit();
                        sb.AppendLine($"Laje de teste criada: {f.Id.Value}, {(x1 - x0) * 30.48:0} × {(y1 - y0) * 30.48:0} cm, topo a {zt * 30.48:0} cm");
                    }
                }
            }
            // SD_ACO = 1: pórtico de aço de teste ao lado (4 pilares W250X32.7 de 4 m, vigas W310X38.7 e uma secundária W200X15 sobre as
            // vigas), com as famílias W métricas da biblioteca do Revit — no documento aberto, sem gravar
            if (Environment.GetEnvironmentVariable("SD_ACO") == "1") sb.AppendLine(CriarPorticoAco(doc));
            if (Environment.GetEnvironmentVariable("SD_ACO") == "1") sb.AppendLine(CargasNativasTeste(doc));
            var e = DEB.Revit.Commands.ModeloAnaliticoCommand.Montar(doc, notas);
            foreach (string n in notas) sb.AppendLine("NOTA: " + n);
            if (e != null)
            {
                var m = e.Modelo;
                sb.AppendLine($"Modelo: {e.Pilares.Count} pilares, {e.Vigas.Count} vigas, {e.Metalicas.Count} peças de aço, {e.Ligacoes.Count} ligações; {m.Nos.Count} nós, {m.Barras.Count} barras, " +
                              $"{m.Molas.Count} aparelhos, {m.Vinculos.Count} vínculos");
                // ajustes da janela (D50): consolo articulado, gravado no modelo e relido numa montagem nova
                if (e.Ligacoes.Count > 0)
                {
                    var ses = new DEB.Core.Analise.Objetos.SessaoAnalise(e, DEB.Revit.Configuracao.ConfiguracaoRevit.Carregar(doc), doc.Title, DEB.Revit.Analise.AjustesRevit.Ler(doc));
                    var lc = e.Ligacoes[0];
                    string ch = DEB.Core.Analise.Objetos.AjustesAnalise.Chave(lc.Dono);
                    string kAntes = lc.Mola?.K[0].ToString("0.#") ?? "?";
                    string resp = ses.Receber("{\"acao\":\"ajustar\",\"chave\":\"" + ch + "\",\"prop\":\"representacao\",\"valor\":\"Barra rígida + articulação\"}");
                    sb.AppendLine($"Ajuste {ch}: Kh {kAntes} → {lc.Mola?.K[0]:0.#} kN/cm; resposta {resp.Length / 1024} kB{(resp.Contains("\"erro\"") ? " COM ERRO" : "")}");
                    DEB.Revit.Analise.AjustesRevit.Gravar(doc, ses.Ajustes);
                    var notas2 = new List<string>();
                    var e2 = DEB.Revit.Commands.ModeloAnaliticoCommand.Montar(doc, notas2);
                    var lc2 = e2?.Ligacoes.FirstOrDefault(x => x.Dono.Id == lc.Dono.Id);
                    sb.AppendLine($"Relido do modelo: representação {lc2?.Representacao}; {notas2.FirstOrDefault(n => n.Contains("Ajustes do modelo"))}");
                    DEB.Revit.Analise.AjustesRevit.Gravar(doc, new Dictionary<string, Dictionary<string, string>>());
                    e = DEB.Revit.Commands.ModeloAnaliticoCommand.Montar(doc, new List<string>()) ?? e;
                }
                string json = DEB.Core.Analise.Objetos.VisualizacaoAnalise.Json(e, doc.Title, DEB.Revit.Configuracao.ConfiguracaoRevit.Carregar(doc));
                sb.AppendLine($"JSON da janela: {json.Length / 1024} kB");
                foreach (var c in e.Cargas) sb.AppendLine($"  carga em {c.Alvo}: {c.Texto()} [{c.Origem}] {c.Descricao}");
                if (Environment.GetEnvironmentVariable("SD_ACO") == "1" && e.Vigas.Count > 0)
                {
                    // item 2c: cargas lançadas no PCEB (viga de concreto e pilar de aço) enviadas ao Revit e relidas como nativas
                    e.Cargas.Add(new DEB.Core.Analise.Objetos.CargaObjeto { Alvo = DEB.Core.Analise.Objetos.AjustesAnalise.Chave(e.Vigas[0].Dono), Tipo = DEB.Core.Analise.Objetos.TipoCargaObjeto.Linear, Valor = 7, Caso = "G" });
                    var pm = e.Metalicas.FirstOrDefault(x => x.Pilar);
                    if (pm != null) e.Cargas.Add(new DEB.Core.Analise.Objetos.CargaObjeto { Alvo = DEB.Core.Analise.Objetos.AjustesAnalise.Chave(pm.Dono), Tipo = DEB.Core.Analise.Objetos.TipoCargaObjeto.Pontual, Valor = 3, Posicao = 200, Direcao = "X", Caso = "Q" });
                    var nc = new List<string>();
                    DEB.Revit.Analise.CargasRevit.EnviarAoRevit(doc, e, nc);
                    foreach (string n in nc) sb.AppendLine("ENVIO: " + n);
                    var n3 = new List<string>();
                    var e3 = DEB.Revit.Commands.ModeloAnaliticoCommand.Montar(doc, n3);
                    foreach (string n in n3.Where(x => x.Contains("arga"))) sb.AppendLine("NOTA3: " + n);
                    foreach (var ld in new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Structure.LoadBase)).Cast<Autodesk.Revit.DB.Structure.LoadBase>())
                    {
                        string geo = ld is Autodesk.Revit.DB.Structure.LineLoad l1 ? $"{l1.StartPoint * 30.48} → {l1.EndPoint * 30.48}" : ld is Autodesk.Revit.DB.Structure.PointLoad p1 ? $"{p1.Point * 30.48}" : "";
                        sb.AppendLine($"  carga Revit {ld.Id.Value} {ld.GetType().Name} caso {ld.LoadCaseName} host {ld.HostElementId.Value}: {geo}");
                    }
                    foreach (var c in e3.Cargas) sb.AppendLine($"  relida em {c.Alvo}: {c.Texto()} [{c.Origem}] {c.Descricao}");
                }
                var dim = DEB.Core.Analise.Dimensionamento.DimensionamentoObjetos.Verificar(e, DEB.Revit.Configuracao.ConfiguracaoRevit.Carregar(doc));
                if (Environment.GetEnvironmentVariable("SD_ACO") == "1")
                {
                    // ligações metálicas no modelo com as famílias PCEB (D49), depois do dimensionamento (espessuras adotadas)
                    var lg = new StringBuilder();
                    DEB.Revit.Analise.LigacoesRevit.Diag = lg;
                    DEB.Revit.Analise.LigacoesRevit.Modelar(doc, e, lg);
                    sb.Append(lg);
                    var pcs = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                        .Where(fi => fi.Symbol.FamilyName.StartsWith("PCEB Chapa") || fi.Symbol.FamilyName.StartsWith("PCEB Parafuso")).ToList();
                    sb.AppendLine($"Peças PCEB de ligação no modelo: {pcs.Count(x => x.Symbol.FamilyName == "PCEB Chapa")} chapas, {pcs.Count(x => x.Symbol.FamilyName == "PCEB Parafuso")} parafusos.");
                    string png3d = Path.Combine(dir, "ligacoes_3d.png");
                    try { sb.AppendLine(DEB.Revit.Analise.LigacoesRevit.ImagemPortico(doc, png3d)); } catch (Exception ex) { sb.AppendLine("Imagem 3D: " + ex.Message); }
                }
                foreach (var d in dim)
                {
                    sb.AppendLine($"  {d.Dono}: {d.Situacao} — {string.Join("; ", d.Resumo)}");
                    foreach (string l in d.Memoria.Linhas) sb.AppendLine("      " + l);
                    foreach (var v in d.Verificacoes.Where(x => x.Situacao != DEB.Core.Normas.Verificacao.SituacaoVerificacao.Ok)) sb.AppendLine($"      [{v.Codigo}] {v.Descricao}: {v.Valor} {v.Limite}");
                }
                // exportação (D50, item 8): os três formatos; OpenSees e CalculiX rodam se estiverem configurados (%AppData%\PCEB\mef.txt)
                try
                {
                    var mm = e.Modelo;
                    foreach (DEB.Core.Analise.Barras.FormatoExportacao fx in Enum.GetValues(typeof(DEB.Core.Analise.Barras.FormatoExportacao)))
                        File.WriteAllText(Path.Combine(dir, "modelo" + DEB.Core.Analise.Barras.ExportacaoAnalise.Extensao(fx)), DEB.Core.Analise.Barras.ExportacaoAnalise.Exportar(mm, fx, doc.Title));
                    string mef = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCEB", "mef.txt");
                    string Cam(string k) => File.Exists(mef) ? File.ReadAllLines(mef).Where(l => l.StartsWith(k + "=")).Select(l => l.Substring(k.Length + 1).Trim()).FirstOrDefault() : null;
                    var pceb = new DEB.Core.Analise.Barras.SolverBarras(mm).Resolver();
                    string py = Cam("python");
                    if (py != null && File.Exists(py))
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo(py, "-I modelo.py") { WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                        using (var pr = System.Diagnostics.Process.Start(psi)) { string err = pr.StandardError.ReadToEnd(); pr.WaitForExit(300000); if (pr.ExitCode != 0) sb.AppendLine("OpenSees: " + err); }
                        foreach (var cs in mm.Casos)
                        {
                            string rj = Path.Combine(dir, "resultados_" + cs.Nome + ".json");
                            if (!File.Exists(rj)) { sb.AppendLine($"OpenSees: sem resultado do caso {cs.Nome}"); continue; }
                            var jr = DEB.Core.Persistencia.Json.Ler(File.ReadAllText(rj))["deslocamentos"];
                            double dmax = 0, umax = 0;
                            for (int n = 0; n < mm.Nos.Count; n++)
                                for (int g = 0; g < 3; g++)
                                {
                                    double a1 = pceb[cs.Nome].Deslocamentos[n][g], b1 = jr[(n + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)][g].Numero;
                                    dmax = Math.Max(dmax, Math.Abs(a1 - b1)); umax = Math.Max(umax, Math.Abs(a1));
                                }
                            sb.AppendLine($"OpenSees caso {cs.Nome}: deslocamento máx. {umax:0.0000} cm; maior diferença para o PCEB {dmax:0.000000} cm");
                        }
                    }
                    string ccx = Cam("ccx");
                    if (ccx != null && File.Exists(ccx))
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo(ccx, "modelo") { WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                        using (var pr = System.Diagnostics.Process.Start(psi)) { string o = pr.StandardOutput.ReadToEnd(); pr.WaitForExit(300000); if (o.Contains("*ERROR")) sb.AppendLine("CalculiX: " + string.Join(" | ", o.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.None).Where(l => l.Contains("ERROR")).Take(3))); }
                        sb.AppendLine($"CalculiX: {(File.Exists(Path.Combine(dir, "modelo.dat")) ? new FileInfo(Path.Combine(dir, "modelo.dat")).Length + " bytes em modelo.dat" : "sem modelo.dat")}");
                    }
                }
                catch (Exception exE) { sb.AppendLine("Exportação: " + exE.Message); }
                var sessaoCap = new DEB.Core.Analise.Objetos.SessaoAnalise(e, DEB.Revit.Configuracao.ConfiguracaoRevit.Carregar(doc), doc.Title, DEB.Revit.Analise.AjustesRevit.Ler(doc));
                foreach (string modo in new[] { "modelo", "diagrama", "deformada", "situacao", "laje", "peca", "consolo", "campo" })
                {
                    string png = Path.Combine(dir, "janela_" + modo + ".png");
                    var win = new DEB.Revit.Analise.JanelaAnalise(json, doc.Title) { Captura = png, ModoCaptura = modo, Sessao = sessaoCap };
                    win.ShowDialog();
                    sb.AppendLine($"{modo}: {(win.Erro ?? (File.Exists(png) ? new FileInfo(png).Length + " bytes" : "imagem não gravada"))}");
                }
            }
            File.WriteAllText(Path.Combine(dir, "resultado.txt"), sb.ToString(), Encoding.UTF8);
            return Path.Combine(dir, "resultado.txt");
        }

        private sealed class FalhasAco : IFailuresPreprocessor
        {
            private readonly List<string> _l;
            public FalhasAco(List<string> l) { _l = l; }
            public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
            {
                foreach (FailureMessageAccessor f in fa.GetFailureMessages())
                {
                    _l.Add($"[{f.GetSeverity()}] {f.GetDescriptionText()}");
                    if (f.GetSeverity() == FailureSeverity.Warning) fa.DeleteWarning(f);
                }
                return FailureProcessingResult.Continue;
            }
        }

        /// <summary>Cargas nativas do Revit no pórtico de aço (item 2): linear de 5 kN/m na 1ª viga W310 e pontual de 20 kN no topo de um pilar.</summary>
        private static string CargasNativasTeste(Document doc)
        {
            var viga = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming).WhereElementIsNotElementType().OfType<FamilyInstance>()
                .FirstOrDefault(f => f.Symbol.Name == "W310X38.7");
            var pilar = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralColumns).WhereElementIsNotElementType().OfType<FamilyInstance>()
                .FirstOrDefault(f => f.Symbol.Name == "W250X32.7");
            if (viga == null || pilar == null) return "Cargas nativas: pórtico de aço não encontrado.";
            var tl = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Structure.LineLoadType)).Cast<Autodesk.Revit.DB.Structure.LineLoadType>().FirstOrDefault();
            var tp = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Structure.PointLoadType)).Cast<Autodesk.Revit.DB.Structure.PointLoadType>().FirstOrDefault();
            var txt = new StringBuilder();
            using (var t = new Transaction(doc, "Cargas nativas de teste"))
            {
                t.Start();
                // no Revit 2025 a carga é hospedada num elemento analítico: o associado à peça, ou um novo pela linha da peça
                var am = Autodesk.Revit.DB.Structure.AnalyticalToPhysicalAssociationManager.GetAnalyticalToPhysicalAssociationManager(doc);
                ElementId Analitico(FamilyInstance fi)
                {
                    ElementId id = am.GetAssociatedElementId(fi.Id);
                    if (id != null && id != ElementId.InvalidElementId) return id;
                    var membro = Autodesk.Revit.DB.Structure.AnalyticalMember.Create(doc, ((LocationCurve)fi.Location).Curve);
                    am.AddAssociation(membro.Id, fi.Id);
                    return membro.Id;
                }
                try
                {
                    var lc = (LocationCurve)viga.Location;
                    XYZ a = lc.Curve.GetEndPoint(0), b = lc.Curve.GetEndPoint(1);
                    double f = UnitUtils.ConvertToInternalUnits(-5, UnitTypeId.KilonewtonsPerMeter);
                    var ll = Autodesk.Revit.DB.Structure.LineLoad.Create(doc, Analitico(viga), Line.CreateBound(a, b), new XYZ(0, 0, f), XYZ.Zero, tl);
                    txt.Append($"linear {ll?.Id.Value} ({ll?.LoadCaseName}); ");
                }
                catch (Exception ex) { txt.Append("linear não criada: " + ex.Message + "; "); }
                try
                {
                    var c = (LocationCurve)pilar.Location;
                    XYZ topo = c.Curve.GetEndPoint(0).Z > c.Curve.GetEndPoint(1).Z ? c.Curve.GetEndPoint(0) : c.Curve.GetEndPoint(1);
                    double f = UnitUtils.ConvertToInternalUnits(-20, UnitTypeId.Kilonewtons);
                    var pl = Autodesk.Revit.DB.Structure.PointLoad.Create(doc, Analitico(pilar), Autodesk.Revit.DB.Structure.AnalyticalElementSelector.EndOrTop, new XYZ(0, 0, f), XYZ.Zero, tp);
                    txt.Append($"pontual {pl?.Id.Value} ({pl?.LoadCaseName})");
                }
                catch (Exception ex) { txt.Append("pontual não criada: " + ex.Message); }
                t.Commit();
            }
            return "Cargas nativas de teste: " + txt;
        }

        private static string CriarPorticoAco(Document doc)
        {
            const string lib = @"C:\ProgramData\Autodesk\RVT 2025\Libraries\English\US\";
            string famPilar = lib + @"Structural Columns\Steel\AISC 15.0\M_W Shapes-Column.rfa", famViga = lib + @"Structural Framing\Steel\AISC 15.0\M_W Shapes.rfa";
            if (!File.Exists(famPilar) || !File.Exists(famViga)) return "Pórtico de aço: famílias W da biblioteca não encontradas.";
            const double m = 1 / 0.3048;
            var falhas = new List<string>();
            using (var t = new Transaction(doc, "Pórtico de aço de teste"))
            {
                var fho = t.GetFailureHandlingOptions();
                fho.SetFailuresPreprocessor(new FalhasAco(falhas));
                t.SetFailureHandlingOptions(fho);
                t.Start();
                FamilySymbol Tipo(string arq, string nome)
                {
                    // carrega só o tipo pedido (o arquivo .txt ao lado é o catálogo de tipos)
                    doc.LoadFamilySymbol(arq, nome, out FamilySymbol s);
                    s ??= new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().FirstOrDefault(x => x.Name == nome);
                    if (s != null && !s.IsActive) s.Activate();
                    return s;
                }
                FamilySymbol pil = Tipo(famPilar, "W250X32.7"), vig = Tipo(famViga, "W310X38.7"), sec = Tipo(famViga, "W200X15");
                if (pil == null || vig == null || sec == null) { t.RollBack(); return "Pórtico de aço: tipos W não carregados."; }
                Level nivel = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).First();
                double x0 = -30 * m, y0 = -30 * m, h = 4 * m, z0 = nivel.Elevation;
                var cantos = new[] { new XYZ(x0, y0, z0), new XYZ(x0 + 6 * m, y0, z0), new XYZ(x0, y0 + 5 * m, z0), new XYZ(x0 + 6 * m, y0 + 5 * m, z0) };
                foreach (XYZ c in cantos)
                    doc.Create.NewFamilyInstance(Line.CreateBound(c, c + XYZ.BasisZ * h), pil, nivel, Autodesk.Revit.DB.Structure.StructuralType.Column);
                XYZ T(XYZ c) => c + XYZ.BasisZ * h;
                doc.Create.NewFamilyInstance(Line.CreateBound(T(cantos[0]), T(cantos[1])), vig, nivel, Autodesk.Revit.DB.Structure.StructuralType.Beam);
                doc.Create.NewFamilyInstance(Line.CreateBound(T(cantos[2]), T(cantos[3])), vig, nivel, Autodesk.Revit.DB.Structure.StructuralType.Beam);
                doc.Create.NewFamilyInstance(Line.CreateBound(T(cantos[0]), T(cantos[2])), vig, nivel, Autodesk.Revit.DB.Structure.StructuralType.Beam);
                doc.Create.NewFamilyInstance(Line.CreateBound(T(cantos[1]), T(cantos[3])), vig, nivel, Autodesk.Revit.DB.Structure.StructuralType.Beam);
                XYZ meioA = (T(cantos[0]) + T(cantos[1])) / 2, meioB = (T(cantos[2]) + T(cantos[3])) / 2;
                var ult = doc.Create.NewFamilyInstance(Line.CreateBound(meioA, meioB), sec, nivel, Autodesk.Revit.DB.Structure.StructuralType.Beam);
                var tipoMat = ult.StructuralMaterialType;
                var mat = doc.GetElement(ult.StructuralMaterialId) as Material;
                t.Commit();
                return string.Join(Environment.NewLine, falhas.Select(f => "FALHA: " + f)) + (falhas.Count > 0 ? Environment.NewLine : "") + $"Pórtico de aço de teste criado (4 pilares W250X32.7, 4 vigas W310X38.7, secundária W200X15); material {tipoMat} \"{mat?.Name}\"; " +
                       $"perfil lido: {DEB.Revit.Analise.MetalicaRevit.Perfil(sec)?.Nome ?? "nenhum"} (tipo \"{sec.Name}\", família \"{sec.FamilyName}\").";
            }
        }

        /// <summary>
        /// Configuração do projeto (D45): grava e relê o nível do projeto no modelo (sem a cópia .pceb.json e sem tocar no arquivo do
        /// escritório) e grava a imagem da tela (WebView2) com um γc fora da norma.
        /// </summary>
        private static string ConfiguracaoAutoTest(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            string dir = Path.Combine(DetailingReport.LogDir, "configuracao_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            var avisos = new List<string>();
            var cfg = DEB.Revit.Configuracao.ConfiguracaoRevit.Carregar(doc, avisos);
            cfg.Definir(DEB.Core.Configuracao.Projeto.NivelConfiguracao.Projeto, "durab.caa", "III");
            cfg.Definir(DEB.Core.Configuracao.Projeto.NivelConfiguracao.Projeto, "normas.gamaC", "1.3", "teste automático");
            DEB.Revit.Configuracao.ConfiguracaoRevit.GravarProjeto(doc, cfg.Projeto, avisos, copia: false);
            var lida = DEB.Revit.Configuracao.ConfiguracaoRevit.Carregar(doc, avisos);
            sb.AppendLine($"Relido do modelo: caa = {lida.Texto("durab.caa")} ({lida.Ler("durab.caa").Origem}); γc = {lida.Numero("normas.gamaC")}; " +
                          $"justificativa = {(lida.Projeto.Justificativas.TryGetValue("normas.gamaC", out string j) ? j : "—")}");
            string png = Path.Combine(dir, "tela.png");
            bool abriu = DEB.Revit.Commands.ConfiguracaoProjetoCommand.Abrir(doc, IntPtr.Zero, avisos, png, out _,
                s => s.Receber("{\"acao\":\"definir\",\"nivel\":\"Projeto\",\"id\":\"normas.gamaNPerm\",\"valor\":\"1,0\"}"));
            sb.AppendLine($"Tela abriu: {abriu}; imagem: {(File.Exists(png) ? new FileInfo(png).Length + " bytes" : "não gravada")}");
            foreach (string a in avisos) sb.AppendLine("AVISO: " + a);
            File.WriteAllText(Path.Combine(dir, "resultado.txt"), sb.ToString(), Encoding.UTF8);
            return Path.Combine(dir, "resultado.txt");
        }

        /// <summary>
        /// Muro de arrimo (D47): cria uma parede estrutural de 3,5 m com sapata corrida de arrimo (ponta 0,9 m, talão 1,9 m), lê o muro,
        /// grava e relê os dados na parede e grava as imagens das três abas da janela. O modelo não é salvo (só com SD_SAVEAS).
        /// </summary>
        private static string MuroAutoTest(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            string dir = Path.Combine(DetailingReport.LogDir, "muro_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            var notas = new List<string>();
            const double m = 1 / 0.3048;
            Wall w;
            using (var t = new Transaction(doc, "Teste: muro de arrimo"))
            {
                t.Start();
                Level nivel = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).First();
                WallType wt = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().First(x => x.Kind == WallKind.Basic);
                WallType wt35 = wt.Duplicate("PCEB teste muro 35 " + DateTime.Now.Ticks) as WallType;
                wt35.SetCompoundStructure(CompoundStructure.CreateSingleLayerCompoundStructure(MaterialFunctionAssignment.Structure, 0.35 * m, ElementId.InvalidElementId));
                w = Wall.Create(doc, Line.CreateBound(new XYZ(-20 * m, -20 * m, 0), new XYZ(-12 * m, -20 * m, 0)), wt35.Id, nivel.Id, 3.5 * m, 0, false, true);
                var ft = new FilteredElementCollector(doc).OfClass(typeof(WallFoundationType)).Cast<WallFoundationType>().FirstOrDefault();
                if (ft != null)
                {
                    var ft2 = ft.Duplicate("PCEB teste arrimo " + DateTime.Now.Ticks) as WallFoundationType;
                    Parameter uso = ft2.get_Parameter(BuiltInParameter.CONTINUOUS_FOOTING_STRUCTURAL_USAGE);
                    for (int k = 0; k < 3 && uso != null && !DEB.Revit.Contencao.MuroRevit.DeArrimo(ft2); k++)
                    {
                        bool foi = uso.Set(k);
                        sb.AppendLine($"  uso = {k}: {foi} → \"{uso.AsValueString()}\" ({uso.AsInteger()})");
                    }
                    sb.AppendLine($"Uso da sapata: {uso?.AsValueString()}");
                    foreach (var bp in new[] { BuiltInParameter.CONTINUOUS_FOOTING_TOP_TOE, BuiltInParameter.CONTINUOUS_FOOTING_BOTTOM_TOE })
                        { var p = ft2.get_Parameter(bp); if (p != null && !p.IsReadOnly) p.Set(0.9 * m); }
                    foreach (var bp in new[] { BuiltInParameter.CONTINUOUS_FOOTING_TOP_HEEL, BuiltInParameter.CONTINUOUS_FOOTING_BOTTOM_HEEL })
                        { var p = ft2.get_Parameter(bp); if (p != null && !p.IsReadOnly) p.Set(1.9 * m); }
                    ft2.get_Parameter(BuiltInParameter.STRUCTURAL_FOUNDATION_THICKNESS)?.Set(0.4 * m);
                    WallFoundation.Create(doc, ft2.Id, w.Id);
                }
                else sb.AppendLine("Sem tipo de sapata corrida no modelo.");
                t.Commit();
            }
            var cfg = DEB.Revit.Configuracao.ConfiguracaoRevit.Carregar(doc, notas);
            var d = DEB.Revit.Contencao.MuroRevit.Ler(w, cfg, notas);
            sb.AppendLine($"Lido: h = {d.AlturaFuste:0.###} m, t = {d.EspessuraBase:0.###} m, sapata {d.EspessuraSapata:0.###} m, ponta {d.Ponta:0.###} m, talão {d.Talao:0.###} m");
            d.DenteAltura = 0.6; d.DenteLargura = 0.35; d.DenteInicio = 0.7; d.Beta = 10;
            DEB.Revit.Contencao.MuroRevit.Gravar(w, d);
            var d2 = DEB.Revit.Contencao.MuroRevit.Ler(w, cfg, notas);
            sb.AppendLine($"Relido da parede: dente {d2.DenteAltura:0.##} m, β = {d2.Beta:0.#}°");
            var r = DEB.Core.Elementos.Contencao.CalculoMuro.Calcular(d2, cfg);
            sb.AppendLine($"FS tombamento {r.FsTombamento:0.00}, deslizamento {r.FsDeslizamento:0.00}, capacidade {r.FsCapacidade:0.00}; situação {r.Situacao}");
            foreach (var v in r.Verificacoes) sb.AppendLine("  " + v.Mensagem);
            foreach (string aba in new[] { "secao", "fuste", "memoria" })
            {
                string png = Path.Combine(dir, "janela_" + aba + ".png");
                DEB.Revit.Commands.MuroArrimoCommand.Abrir(doc, w, IntPtr.Zero, notas, png, aba == "secao" ? "" : aba);
                sb.AppendLine($"Janela ({aba}): {(File.Exists(png) ? new FileInfo(png).Length + " bytes" : "não gravada")}");
            }
            // D50, item 7: família PCEB do muro — cria dois muros (aterro à esquerda e à direita), confere a caixa e atualiza a seção
            try
            {
                Level nv = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).First();
                var dm = DEB.Core.Elementos.Contencao.DadosMuro.DaConfiguracao(cfg);
                dm.Comprimento = 8; dm.DenteAltura = 0.4; dm.DenteLargura = 0.3; dm.DenteInicio = 0.7;
                var lg = new StringBuilder();
                var m1 = DEB.Revit.Contencao.FamiliaMuro.Criar(doc, dm, nv, new XYZ(0, -40 * m, 0), XYZ.BasisX, true, lg);
                var m2 = DEB.Revit.Contencao.FamiliaMuro.Criar(doc, dm, nv, new XYZ(0, -50 * m, 0), XYZ.BasisY, false, lg);
                sb.Append(lg);
                foreach (var fi in new[] { m1, m2 })
                {
                    if (fi == null) { sb.AppendLine("Muro PCEB não criado."); continue; }
                    BoundingBoxXYZ bb = fi.get_BoundingBox(null);
                    sb.AppendLine($"Muro {fi.Id}: x {bb.Min.X * 0.3048:0.00}…{bb.Max.X * 0.3048:0.00}, y {bb.Min.Y * 0.3048:0.00}…{bb.Max.Y * 0.3048:0.00}, " +
                                  $"z {(bb.Min.Z - nv.Elevation) * 0.3048:0.00}…{(bb.Max.Z - nv.Elevation) * 0.3048:0.00} m (esperado: comprimento 8, base {dm.Base:0.00}, " +
                                  $"z {-dm.TerrenoFrente - dm.DenteAltura:0.00}…{dm.AlturaTotal - dm.TerrenoFrente:0.00})");
                    sb.AppendLine($"  categoria {fi.Category?.Name}; família {fi.Symbol.FamilyName}");
                }
                if (m1 != null)
                {
                    var lidos = DEB.Revit.Contencao.MuroRevit.LerElemento(m1, cfg, notas);
                    sb.AppendLine($"Relido do muro PCEB: comprimento {lidos.Comprimento:0.##} m, dente {lidos.DenteAltura:0.##} m");
                    lidos.AlturaFuste += 0.5; lidos.Comprimento = 12;
                    var lg2 = new StringBuilder();
                    DEB.Revit.Contencao.FamiliaMuro.Atualizar(m1, lidos, lg2);
                    BoundingBoxXYZ bb = m1.get_BoundingBox(null);
                    sb.Append(lg2);
                    sb.AppendLine($"Atualizado: {m1.Symbol.FamilyName}; caixa x {(bb.Max.X - bb.Min.X) * 0.3048:0.00} m, altura {(bb.Max.Z - bb.Min.Z) * 0.3048:0.00} m");
                    string png = Path.Combine(dir, "janela_muro_pceb.png");
                    DEB.Revit.Commands.MuroArrimoCommand.Abrir(doc, m1, IntPtr.Zero, notas, png, "");
                    sb.AppendLine($"Janela do muro PCEB: {(File.Exists(png) ? "gravada" : "não gravada")}");
                    // vista 3D isométrica dos muros
                    try
                    {
                        View3D v3;
                        using (var t = new Transaction(doc, "Teste: 3D do muro"))
                        {
                            t.Start();
                            var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(x => x.ViewFamily == ViewFamily.ThreeDimensional);
                            v3 = View3D.CreateIsometric(doc, vft.Id);
                            var caixa = new BoundingBoxXYZ { Min = new XYZ(-6 * m, -54 * m, -3 * m), Max = new XYZ(16 * m, -34 * m, 6 * m) };
                            v3.SetSectionBox(caixa);
                            v3.DisplayStyle = DisplayStyle.ShadingWithEdges;
                            t.Commit();
                        }
                        var op = new ImageExportOptions { ExportRange = ExportRange.SetOfViews, FilePath = Path.Combine(dir, "muros_3d"), PixelSize = 1400,
                                                          HLRandWFViewsFileType = ImageFileType.PNG, ShadowViewsFileType = ImageFileType.PNG, ZoomType = ZoomFitType.FitToPage };
                        op.SetViewsAndSheets(new List<ElementId> { v3.Id });
                        doc.ExportImage(op);
                        sb.AppendLine("3D exportado.");
                    }
                    catch (Exception ex3) { sb.AppendLine("3D: " + ex3.Message); }
                }
            }
            catch (Exception exM) { sb.AppendLine("Muro PCEB: " + exM); }
            foreach (string a in notas) sb.AppendLine("NOTA: " + a);
            File.WriteAllText(Path.Combine(dir, "resultado.txt"), sb.ToString(), Encoding.UTF8);
            return Path.Combine(dir, "resultado.txt");
        }

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
            var cfgProjeto = DEB.Revit.Configuracao.ConfiguracaoRevit.Carregar(doc, notes);
            int carregados = ReacoesNosConsolos.Aplicar(doc, groups, notes, cfgProjeto, out var estrutura);
            int comEsforcos = ReacoesNosConsolos.EsforcosNosPilares(estrutura, groups, cfgProjeto, notes);
            foreach (ColumnGroup g in groups) g.Spec.UsarConfiguracao(cfgProjeto);
            var sb = new StringBuilder();
            sb.AppendLine($"Consolos com reação de viga: {carregados}; grupos de pilares com os esforços da análise: {comEsforcos}");
            if (estrutura != null)
            {
                sb.AppendLine($"Análise por objetos: {estrutura.Modelo.Nos.Count} nós, {estrutura.Modelo.Barras.Count} barras, {estrutura.Modelo.Molas.Count} molas, " +
                              $"{estrutura.Modelo.Vinculos.Count} vínculos rígidos");
                var elu = estrutura.Combinacoes[estrutura.DefinicaoCombinacoes[0].Nome];
                foreach (var p in estrutura.Pilares)
                {
                    var b = estrutura.Esforcos(p, elu).OrderBy(x => x.Z).First();
                    sb.AppendLine($"  {p.Dono}: base (z = {b.Z:0}) N = {b.N:0.0} kN, My = {b.My:0} kN·cm, Mz = {b.Mz:0} kN·cm [{elu.Nome}]");
                }
                foreach (var l in estrutura.Ligacoes)
                {
                    var f = estrutura.Esforco(l, elu);
                    sb.AppendLine($"  {l.Dono}: V = {f.V:0.0} kN, H = {f.H:0.00} kN, Ht = {f.Ht:0.00} kN — {l.Observacao}");
                }
            }
            foreach (ColumnGroup g in groups)
                foreach (Corbel k in g.Representative.Corbels.Where(c => c.Carga != null))
                    sb.AppendLine($"{g.Mark}: consolo face {ColumnRebarLayout.FaceNumber(k.Face)}, Vg = {k.Carga.Vg:0.0} kN, Vq = {k.Carga.Vq:0.0} kN, Hd/Vd = {k.Carga.FatorHd:0.00} — {k.Carga.Origem}");
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
            // SD_LINHAS = 1: armadura por linha (D32) com bitola mista, redução com a altura e feixe de 2, para testar o modelo 3D
            if (Environment.GetEnvironmentVariable("SD_LINHAS") == "1")
                foreach (ColumnGroup g in groups)
                {
                    double hCm = U_.ToMm(g.Representative.Frame.H) / 10, meio = Math.Round(hCm / 2);
                    g.Spec.ArmaduraPorLinha = new List<LinhaArmaduraPorLinha>
                    {
                        new LinhaArmaduraPorLinha { Arranjo = ArmaduraPorLinha.Cantos, Bitola = 20 },
                        new LinhaArmaduraPorLinha { Arranjo = ArmaduraPorLinha.Faces24, PorFace = 1, Bitola = 16, Fim = meio + 60 },
                        new LinhaArmaduraPorLinha { Arranjo = ArmaduraPorLinha.Faces24, PorFace = 1, Bitola = 12.5, Ini = meio },
                        new LinhaArmaduraPorLinha { Arranjo = ArmaduraPorLinha.Faces13, PorFace = 1, Bitola = 12.5, Feixe = 2 },
                    };
                    sb.AppendLine($"{g.Mark}: armadura por linha (H = {hCm:0.#} cm): " +
                                  string.Join(" ; ", g.Spec.ArmaduraPorLinha.Select(l => string.Join("|", l.Celulas()))));
                }
            foreach (ColumnGroup g in groups)
            {
                sb.AppendLine($"{g.Mark}: {g.Members.Count} un. | {g.Signature}");
                foreach (PrecastColumn m in g.Members)
                    sb.AppendLine($"   pilar {m.Id} giro {m.Rotation * 90}° consolos {m.Corbels.Count}");
                foreach (string w in g.Spec.Check(g.Representative)) sb.AppendLine("   verificação: " + w);
                foreach (BarDef b in ColumnRebarLayout.Compute(g.Representative.Frame, g.Representative.Corbels, g.Spec))
                    sb.AppendLine($"   barra {b.Role} Ø{b.DiameterMm} x{b.Count} pts={b.Points.Count}" +
                                  (b.Role == BarRole.Longitudinal
                                      ? FormattableString.Invariant($" x={b.Points[0].A * 30.48:0.##} y={b.Points[0].B * 30.48:0.##} z={b.Points[0].C * 30.48:0.##}–{b.Points[^1].C * 30.48:0.##} cm")
                                      : ""));
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
