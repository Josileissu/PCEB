using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DEB.Core.Formas;
using DEB.Revit.Core;
using DEB.Revit.Presentation;

namespace DEB.Revit.Formas
{
    /// <summary>
    /// AutoRun das pranchas de formas (instância de teste do Revit, nunca a do usuário):
    ///   formasdados    — parâmetros + dados + CSV (exporta, importa um arquivo de teste com uma marca que não existe);
    ///   formaslocacao  — planta de locação completa, DUAS vezes (a 2ª não pode duplicar nada) + PNG da vista e da folha;
    ///   formasmontagem — idem, planta de montagem;
    ///   formassonda    — carrega cada família de identificador de SD_FAMILIAS_PASTA e descobre qual parâmetro cada rótulo lê.
    /// SD_NIVEL = nome do nível (senão o sugerido pela janela); SD_FORMAS_MARCAR = 1 dá marcas (P/V/L) às peças sem marca, para
    /// testar em modelos sem marcas; SD_SAVEAS = cópia do modelo com o resultado. Resultado em logs\formas_*\resultado.txt.
    /// </summary>
    internal static class FormasAutoTest
    {
        /// <summary>SD_MODELO = .rvt aberto pela API (central destacado, preservando os subprojetos) em vez do arquivo da linha de
        /// comando; o resultado só vai para SD_SAVEAS (o arquivo de origem nunca é salvo) e o documento é fechado no fim.</summary>
        public static string Run(UIApplication uiapp, UIDocument uidoc, string tarefa)
        {
            string modelo = Environment.GetEnvironmentVariable("SD_MODELO")?.Trim();
            if (string.IsNullOrEmpty(modelo)) return Run(uidoc.Document, tarefa);
            var oo = new OpenOptions();
            try
            {
                if (BasicFileInfo.Extract(modelo).IsWorkshared)
                {
                    oo.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
                    oo.SetOpenWorksetsConfiguration(new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));
                }
            }
            catch { /* arquivo comum */ }
            Document d = uiapp.Application.OpenDocumentFile(ModelPathUtils.ConvertUserVisiblePathToModelPath(modelo), oo);
            try { return Run(d, tarefa); }
            finally { try { d.Close(false); } catch { /* fecha com o Revit */ } }
        }

        public static string Run(Document doc, string tarefa)
        {
            if (tarefa == "formassonda") return Sonda(doc);
            string dir = Path.Combine(DetailingReport.LogDir, tarefa + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            void Gravar() { try { File.WriteAllText(Path.Combine(dir, "resultado.txt"), sb.ToString(), Encoding.UTF8); } catch { } }
            if (tarefa == "formaslevantar") { sb.Append(Levantamento(doc)); Gravar(); return Path.Combine(dir, "resultado.txt"); }
            var log = new PieceLog(tarefa);
            PresentationStandard std = ExecucaoFormas.Padrao(log);
            if (Environment.GetEnvironmentVariable("SD_FORMAS_MARCAR") == "1") sb.AppendLine("modelo de teste: " + CompletarTeste(doc) + "; " + MarcasDeTeste(doc));
            sb.AppendLine($"pilares {Conta(doc, BuiltInCategory.OST_StructuralColumns)}, vigas {Conta(doc, BuiltInCategory.OST_StructuralFraming)}, " +
                          $"pisos {Conta(doc, BuiltInCategory.OST_Floors)}, fundações {Conta(doc, BuiltInCategory.OST_StructuralFoundation)}, " +
                          $"eixos {new FilteredElementCollector(doc).OfClass(typeof(Grid)).GetElementCount()}");
            Gravar();

            if (tarefa == "formasdados")
            {
                sb.AppendLine("dados 1: " + ExecucaoFormas.Dados(doc, std, log));
                sb.AppendLine("dados 2 (de novo): " + ExecucaoFormas.Dados(doc, std, log));
                string csv = CargasFormas.Exportar(doc, std.Formas, out int n);
                File.WriteAllText(Path.Combine(dir, "cargas_exportadas.csv"), csv, Encoding.UTF8);
                sb.AppendLine($"exportados {n} pilar(es)");
                var marcas = csv.Replace("\r", "").Split('\n').Skip(1).Where(l => l.Trim() != "").Select(l => l.Split(';')[0]).Take(3).ToList();
                string teste = CsvCargas.Cabecalho + "\n" + string.Join("\n", marcas.Select((m, i) => $"{m};{100 + i},5;{50 + i};1,25;-1,5;2;-2;0,3;-0,3;0,4;-0,4")) + "\nP_NAO_EXISTE;1;2\n";
                File.WriteAllText(Path.Combine(dir, "cargas_teste.csv"), teste, Encoding.UTF8);
                sb.AppendLine("importar: " + ExecucaoFormas.Etapa(doc, "Formas: importar cargas", log, () => CargasFormas.Importar(doc, std.Formas, teste, log)));
                File.WriteAllText(Path.Combine(dir, "cargas_reexportadas.csv"), CargasFormas.Exportar(doc, std.Formas, out _), Encoding.UTF8);
                Amostras(doc, sb);
            }
            else
            {
                TipoPlanta tipo = tarefa == "formaslocacao" ? TipoPlanta.Locacao : TipoPlanta.Montagem;
                List<Level> niveis = ModeloFormas.Niveis(doc);
                string pedido = Environment.GetEnvironmentVariable("SD_NIVEL")?.Trim() ?? "";
                List<Level> alvo = pedido == "*" ? NiveisComPecas(doc, std.Formas)
                    : new List<Level> { niveis.FirstOrDefault(l => string.Equals(l.Name, pedido, StringComparison.OrdinalIgnoreCase)) ?? FormasUi.NivelPadrao(doc, niveis, tipo) };
                FormasSheetSpec spec = tipo == TipoPlanta.Locacao ? std.Formas.Locacao : std.Formas.Montagem;
                int vezes = Environment.GetEnvironmentVariable("SD_FORMAS_VEZES") == "1" ? 1 : 2;
                var ids = new List<ElementId>();
                foreach (Level nivel in alvo)
                {
                    sb.AppendLine($"nível: {nivel.Name}");
                    ViewSheet folha = null; ViewPlan vista = null;
                    for (int vez = 1; vez <= vezes; vez++)
                    {
                        folha = ExecucaoFormas.Planta(doc, std, tipo, nivel, null, spec.NumeroFolha, spec.NomeFolha, true, log, out vista);
                        sb.AppendLine($"execução {vez}: vista {vista?.Name ?? "-"} | folha {folha?.SheetNumber ?? "-"} | " + Contagem(doc, vista, folha));
                        Gravar();
                    }
                    if (vista != null) ids.Add(vista.Id);
                    if (folha != null) ids.Add(folha.Id);
                }
                Amostras(doc, sb);
                if (ids.Count > 0)
                    try
                    {
                        var ieo = new ImageExportOptions
                        {
                            ExportRange = ExportRange.SetOfViews, FilePath = Path.Combine(dir, "formas"), FitDirection = FitDirectionType.Horizontal,
                            HLRandWFViewsFileType = ImageFileType.PNG, ShadowViewsFileType = ImageFileType.PNG,
                            ImageResolution = ImageResolution.DPI_150, PixelSize = 4000, ZoomType = ZoomFitType.FitToPage
                        };
                        ieo.SetViewsAndSheets(ids.Distinct().ToList());
                        doc.ExportImage(ieo);
                    }
                    catch (Exception ex) { sb.AppendLine("PNG não exportado: " + ex.Message); }
            }
            sb.AppendLine().AppendLine(ExecucaoFormas.Relatorio(log, "Relatório", out _));
            foreach (string t in log.Traces) sb.AppendLine(t);
            string saveAs = Environment.GetEnvironmentVariable("SD_SAVEAS");
            if (!string.IsNullOrWhiteSpace(saveAs))
                try
                {
                    var so = new SaveAsOptions { OverwriteExistingFile = true };
                    if (doc.IsWorkshared) so.SetWorksharingOptions(new WorksharingSaveAsOptions { SaveAsCentral = true });
                    doc.SaveAs(saveAs, so);
                    sb.AppendLine("modelo salvo em " + saveAs);
                }
                catch (Exception ex) { sb.AppendLine("SaveAs falhou: " + ex.Message); }
            string arq = Path.Combine(dir, "resultado.txt");
            File.WriteAllText(arq, sb.ToString(), Encoding.UTF8);
            return arq;
        }

        private static int Conta(Document doc, BuiltInCategory c) =>
            new FilteredElementCollector(doc).OfCategory(c).WhereElementIsNotElementType().GetElementCount();

        private static string Contagem(Document doc, View v, ViewSheet s)
        {
            if (v == null) return "";
            int tags = new FilteredElementCollector(doc, v.Id).OfClass(typeof(IndependentTag)).GetElementCount();
            int cotas = new FilteredElementCollector(doc, v.Id).OfClass(typeof(Dimension)).GetElementCount();
            int vao = new FilteredElementCollector(doc, v.Id).OfCategory(BuiltInCategory.OST_SpanDirectionSymbol).WhereElementIsNotElementType().GetElementCount();
            int tabelas = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>().Count(x => MarcaFormas.Papel(x) != "");
            int vps = s?.GetAllViewports().Count ?? 0;
            int sis = s == null ? 0 : new FilteredElementCollector(doc, s.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>().Count(x => !x.IsTitleblockRevisionSchedule);
            int folhas = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).GetElementCount();
            int vistas = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).GetElementCount();
            return $"identificadores {tags}, cotas {cotas}, sentido do vão {vao}, tabelas do comando {tabelas}, viewports {vps}, tabelas na folha {sis}, folhas {folhas}, plantas {vistas}";
        }

        /// <summary>Valores SD_ de alguns pilares, vigas e lajes, e o conteúdo das tabelas do comando.</summary>
        private static void Amostras(Document doc, StringBuilder sb)
        {
            foreach (var (cat, defs) in new[]
            {
                (BuiltInCategory.OST_StructuralColumns, new[] { ParametrosFormas.Secao, ParametrosFormas.NivelTopo, ParametrosFormas.NivelTopoBloco, ParametrosFormas.NivelBase, ParametrosFormas.ComprimentoTotal, ParametrosFormas.Esforcos[0], ParametrosFormas.Ordem }),
                (BuiltInCategory.OST_StructuralFraming, new[] { ParametrosFormas.Secao, ParametrosFormas.Elevacao }),
                (BuiltInCategory.OST_Floors, new[] { ParametrosFormas.LajeTipo, ParametrosFormas.LajeAltura, ParametrosFormas.LajeElevacao, ParametrosFormas.LajeNivel, ParametrosFormas.LajePesoProprio, ParametrosFormas.Ordem }),
            })
                foreach (Element e in new FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType().Take(4))
                    sb.AppendLine($"   {cat} {ModeloFormas.Marca(e)} ({e.Name}): " + string.Join(" | ", defs.Select(d =>
                    {
                        Parameter p = e.get_Parameter(d.Guid);
                        return d.Nome + "=" + (p == null ? "(sem)" : p.StorageType == StorageType.String ? p.AsString() : p.HasValue ? p.AsValueString() : "(vazio)");
                    })));
            foreach (ViewSchedule vs in new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>().Where(x => MarcaFormas.Papel(x) != ""))
            {
                sb.AppendLine($"   tabela \"{vs.Name}\":");
                try
                {
                    TableData td = vs.GetTableData();
                    foreach (SectionType st in new[] { SectionType.Header, SectionType.Body })
                    {
                        TableSectionData sd = td.GetSectionData(st);
                        for (int r = sd.FirstRowNumber; r <= Math.Min(sd.LastRowNumber, sd.FirstRowNumber + 12); r++)
                            sb.AppendLine("      " + string.Join(" | ", Enumerable.Range(sd.FirstColumnNumber, sd.NumberOfColumns).Select(c => vs.GetCellText(st, r, c))));
                    }
                }
                catch (Exception ex) { sb.AppendLine("      (não lida: " + ex.Message + ")"); }
            }
        }

        /// <summary>Marcas P1…/V1…/L1… nas peças sem marca (só para testar num modelo sem marcas).</summary>
        private static string MarcasDeTeste(Document doc)
        {
            int n = 0;
            using (var t = new Transaction(doc, "Teste: marcas"))
            {
                t.Start();
                WarningSwallower.Attach(t);
                foreach (var (cat, pre) in new[] { (BuiltInCategory.OST_StructuralColumns, "P"), (BuiltInCategory.OST_StructuralFraming, "V"), (BuiltInCategory.OST_Floors, "L"), (BuiltInCategory.OST_StructuralFoundation, "B") })
                {
                    int i = 0;
                    foreach (Element e in new FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType()
                                 .Where(e => !(e is FamilyInstance fi) || fi.SuperComponent == null))
                    {
                        i++;
                        Parameter p = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                        if (p == null || p.IsReadOnly || (p.AsString() ?? "") != "") continue;
                        try { if (p.Set(pre + i)) n++; } catch { /* em grupo */ }
                    }
                }
                t.Commit();
            }
            return n + " marca(s)";
        }

        /// <summary>Níveis com lajes marcadas ou vigas do filtro (SD_NIVEL = "*": uma planta de montagem por pavimento).</summary>
        private static List<Level> NiveisComPecas(Document doc, FormasSettings cfg)
        {
            var ids = new HashSet<ElementId>(new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>()
                .Where(f => ModeloFormas.Marca(f) != "").Select(f => f.LevelId));
            foreach (FamilyInstance b in ModeloFormas.Filtrar(doc, BuiltInCategory.OST_StructuralFraming, cfg.FiltroVigas, null, "viga"))
                if (ModeloFormas.Marca(b) != "" && ModeloFormas.NivelDaViga(doc, b) is Level l) ids.Add(l.Id);
            return ModeloFormas.Niveis(doc).Where(l => ids.Contains(l.Id)).ToList();
        }

        /// <summary>Modelo de teste sem eixos/lajes: eixos pelos centros dos pilares e uma laje por nível acima do mais baixo
        /// (só com SD_FORMAS_MARCAR = 1).</summary>
        private static string CompletarTeste(Document doc)
        {
            if (new FilteredElementCollector(doc).OfClass(typeof(Grid)).GetElementCount() > 0) return "já tem eixos";
            var cols = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralColumns).WhereElementIsNotElementType()
                .OfType<FamilyInstance>().Where(f => f.SuperComponent == null).Select(ModeloFormas.Eixo).Where(p => p != null).ToList();
            if (cols.Count == 0) return "sem pilares";
            double tol = U.Mm(100), ext = U.Mm(2000);
            List<double> Distintos(IEnumerable<double> v) { var r = new List<double>(); foreach (double x in v.OrderBy(x => x)) if (r.Count == 0 || x - r[r.Count - 1] > tol) r.Add(x); return r; }
            List<double> xs = Distintos(cols.Select(p => p.X)), ys = Distintos(cols.Select(p => p.Y));
            double x0 = xs.First() - ext, x1 = xs.Last() + ext, y0 = ys.First() - ext, y1 = ys.Last() + ext;
            int ng = 0, nl = 0;
            using (var t = new Transaction(doc, "Teste: eixos e lajes"))
            {
                t.Start();
                WarningSwallower.Attach(t);
                int i = 0;
                foreach (double x in xs) { Grid g = Grid.Create(doc, Line.CreateBound(new XYZ(x, y0, 0), new XYZ(x, y1, 0))); try { g.Name = (++i).ToString(); } catch { } ng++; }
                char c = 'A';
                foreach (double y in ys) { Grid g = Grid.Create(doc, Line.CreateBound(new XYZ(x0, y, 0), new XYZ(x1, y, 0))); try { g.Name = (c++).ToString(); } catch { } ng++; }
                double m = U.Mm(300);
                var loop = new CurveLoop();
                var q = new[] { new XYZ(xs.First() - m, ys.First() - m, 0), new XYZ(xs.Last() + m, ys.First() - m, 0), new XYZ(xs.Last() + m, ys.Last() + m, 0), new XYZ(xs.First() - m, ys.Last() + m, 0) };
                for (int k = 0; k < 4; k++) loop.Append(Line.CreateBound(q[k], q[(k + 1) % 4]));
                ElementId ft = doc.GetDefaultElementTypeId(ElementTypeGroup.FloorType);
                foreach (Level l in ModeloFormas.Niveis(doc).Skip(1))
                    try { Floor.Create(doc, new List<CurveLoop> { loop }, ft, l.Id); nl++; } catch { /* sem tipo de piso */ }
                t.Commit();
            }
            return $"{ng} eixo(s), {nl} laje(s) criados";
        }

        /// <summary>formaslevantar: o que o modelo tem (só leitura) para ajustar a configuração local.</summary>
        private static string Levantamento(Document doc)
        {
            var sb = new StringBuilder();
            sb.AppendLine("NÍVEIS");
            foreach (Level l in ModeloFormas.Niveis(doc)) sb.AppendLine($"   {l.Name} | {U.ToMm(l.ProjectElevation) / 1000:0.000} m (interno)");
            try { BasePoint pb = BasePoint.GetProjectBasePoint(doc); sb.AppendLine($"ponto base: Z interno {U.ToMm(pb.Position.Z) / 1000:0.000} m, compartilhado {U.ToMm(pb.SharedPosition.Z) / 1000:0.000} m"); } catch { }
            sb.AppendLine("PLANTAS");
            foreach (ViewPlan v in new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Where(v => !v.IsTemplate).OrderBy(v => v.GenLevel?.ProjectElevation ?? 0).ThenBy(v => v.Name))
                sb.AppendLine($"   {v.ViewType} \"{v.Name}\" [{v.GenLevel?.Name}] 1:{v.Scale} template={(v.ViewTemplateId != ElementId.InvalidElementId ? doc.GetElement(v.ViewTemplateId)?.Name : "-")} folha={v.get_Parameter(BuiltInParameter.VIEWPORT_SHEET_NUMBER)?.AsString()}");
            sb.AppendLine("FOLHAS");
            foreach (ViewSheet s in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => !s.IsPlaceholder).OrderBy(s => s.SheetNumber))
            {
                string tb = string.Join(", ", new FilteredElementCollector(doc, s.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType()
                    .Select(e => (doc.GetElement(e.GetTypeId()) as FamilySymbol) is FamilySymbol fs ? fs.FamilyName + " : " + fs.Name : "?"));
                var vs = s.GetAllPlacedViews().Select(doc.GetElement).OfType<View>().Select(v => v.Name);
                sb.AppendLine($"   {s.SheetNumber} | {s.Name} | carimbo {tb} | vistas: {string.Join("; ", vs)}");
            }
            sb.AppendLine("CARIMBOS CARREGADOS");
            foreach (FamilySymbol fs in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().OfType<FamilySymbol>())
                sb.AppendLine($"   {fs.FamilyName} : {fs.Name}");
            sb.AppendLine("IDENTIFICADORES CARREGADOS");
            foreach (var cat in new[] { BuiltInCategory.OST_StructuralColumnTags, BuiltInCategory.OST_StructuralFramingTags, BuiltInCategory.OST_FloorTags,
                                        BuiltInCategory.OST_StructuralFoundationTags, BuiltInCategory.OST_MultiCategoryTags, BuiltInCategory.OST_SpanDirectionSymbol,
                                        BuiltInCategory.OST_GenericAnnotation, BuiltInCategory.OST_SpotElevSymbols })
                foreach (FamilySymbol fs in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(cat).Cast<FamilySymbol>())
                {
                    int usos = new FilteredElementCollector(doc).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().Count(t => t.GetTypeId() == fs.Id);
                    sb.AppendLine($"   {cat} | {fs.FamilyName} : {fs.Name} | usado {usos}x");
                }
            foreach (var (cat, nome) in new[] { (BuiltInCategory.OST_StructuralColumns, "PILARES"), (BuiltInCategory.OST_StructuralFraming, "VIGAS"),
                                                (BuiltInCategory.OST_Floors, "PISOS"), (BuiltInCategory.OST_StructuralFoundation, "FUNDAÇÕES") })
            {
                sb.AppendLine(nome + " (tipo | material | quantidade | com marca | exemplos de marca | nível)");
                var els = new FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType()
                    .Where(e => !(e is FamilyInstance fi) || fi.SuperComponent == null).ToList();
                foreach (var g in els.GroupBy(e => (doc.GetElement(e.GetTypeId()) is ElementType et ? (et as FamilySymbol)?.FamilyName + " : " + et.Name : "?")).OrderBy(g => g.Key))
                {
                    string mat = g.First() is FamilyInstance f0 ? f0.StructuralMaterialType.ToString() : "";
                    var marcas = g.Select(ModeloFormas.Marca).Where(m => m != "").ToList();
                    string niv = string.Join("/", g.Select(e => doc.GetElement(e.LevelId)?.Name ?? (e is FamilyInstance fi && ModeloFormas.NivelDaViga(doc, fi) is Level lv ? lv.Name : "")).Distinct().Take(4));
                    sb.AppendLine($"   {g.Key} | {mat} | {g.Count()} | {marcas.Count} | {string.Join(",", marcas.Distinct().Take(6))} | {niv}");
                }
            }
            sb.AppendLine("TABELAS");
            foreach (ViewSchedule vs in new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>().Where(v => !v.IsTemplate && !v.IsTitleblockRevisionSchedule))
                sb.AppendLine($"   {vs.Name}");
            sb.AppendLine("LEGENDAS");
            foreach (View v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => v.ViewType == ViewType.Legend && !v.IsTemplate))
                sb.AppendLine($"   {v.Name}");
            sb.AppendLine("TIPOS DE COTA / TEXTO");
            sb.AppendLine("   " + string.Join(" | ", new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().Where(t => t.StyleType == DimensionStyleType.Linear).Select(t => t.Name)));
            sb.AppendLine("   " + string.Join(" | ", new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().Select(t => t.Name)));
            sb.AppendLine($"EIXOS: {new FilteredElementCollector(doc).OfClass(typeof(Grid)).GetElementCount()}");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ sondagem das famílias de identificador

        /// <summary>
        /// Para cada .rfa de SD_FAMILIAS_PASTA (sem os backups .000N.rfa; SD_SONDA_PULAR = nomes de arquivo a pular, separados por ";"):
        /// carrega, cria um identificador de cada tipo numa peça da categoria correspondente, lê o texto e casa com os parâmetros da
        /// peça e do tipo — antes, os parâmetros de texto vazios e graváveis recebem «nome do parâmetro», para o rótulo denunciar de
        /// onde lê. Tudo desfeito no fim de cada família. O arquivo é gravado linha a linha ANTES de cada passo arriscado: se o Revit
        /// cair (queda nativa), a última linha mostra a família/tipo.
        /// </summary>
        private static string Sonda(Document doc)
        {
            string pasta = Environment.GetEnvironmentVariable("SD_FAMILIAS_PASTA")?.Trim();
            if (string.IsNullOrEmpty(pasta) || !Directory.Exists(pasta)) throw new InvalidOperationException("SD_FAMILIAS_PASTA não é uma pasta.");
            Directory.CreateDirectory(DetailingReport.LogDir);
            string arq = Path.Combine(DetailingReport.LogDir, $"formassonda_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            void W(string s) { try { File.AppendAllText(arq, s + Environment.NewLine, Encoding.UTF8); } catch { /* segue */ } }
            var pular = new HashSet<string>((Environment.GetEnvironmentVariable("SD_SONDA_PULAR") ?? "").Split(';').Select(x => x.Trim()).Where(x => x != ""),
                StringComparer.OrdinalIgnoreCase);
            W($"pasta: {pasta}");

            // dados SD_ no modelo de teste (desfeitos no fim), para os rótulos SD_ mostrarem valores
            using (var tgAll = new TransactionGroup(doc, "Sonda (teste)"))
            {
                tgAll.Start();
                try
                {
                    var log = new PieceLog("sonda");
                    ExecucaoFormas.Dados(doc, ExecucaoFormas.Padrao(log), log);
                    W("dados SD_: " + (log.Errors.Count == 0 ? "ok" : string.Join(" | ", log.Errors)));
                    var alvos = new Dictionary<BuiltInCategory, (Element E, View V)>();
                    foreach (string f in Directory.GetFiles(pasta, "*.rfa").OrderBy(x => x))
                    {
                        string nome = Path.GetFileName(f);
                        if (Regex.IsMatch(nome, @"\.\d{4}\.rfa$", RegexOptions.IgnoreCase)) continue;
                        if (pular.Contains(nome)) { W($"== {nome}: pulada (SD_SONDA_PULAR)"); continue; }
                        W($"== {nome}");
                        try { SondaFamilia(doc, f, alvos, W); }
                        catch (Exception ex) { W("   ERRO " + ex.GetType().Name + ": " + ex.Message); }
                    }
                }
                finally { tgAll.RollBack(); }
            }
            W("fim");
            return arq;
        }

        /// <summary>Categoria de identificador → categoria das peças que ele identifica (as que a sondagem sabe testar).</summary>
        private static readonly Dictionary<BuiltInCategory, BuiltInCategory> AlvoDoIdentificador = new Dictionary<BuiltInCategory, BuiltInCategory>
        {
            [BuiltInCategory.OST_StructuralColumnTags] = BuiltInCategory.OST_StructuralColumns,
            [BuiltInCategory.OST_StructuralFramingTags] = BuiltInCategory.OST_StructuralFraming,
            [BuiltInCategory.OST_FloorTags] = BuiltInCategory.OST_Floors,
            [BuiltInCategory.OST_StructuralFoundationTags] = BuiltInCategory.OST_StructuralFoundation,
            [BuiltInCategory.OST_WallTags] = BuiltInCategory.OST_Walls,
            [BuiltInCategory.OST_GenericModelTags] = BuiltInCategory.OST_GenericModel,
            [BuiltInCategory.OST_MultiCategoryTags] = BuiltInCategory.OST_StructuralColumns,
            [BuiltInCategory.OST_StairsTags] = BuiltInCategory.OST_Stairs,
            [BuiltInCategory.OST_AssemblyTags] = BuiltInCategory.OST_Assemblies,
            [BuiltInCategory.OST_PartTags] = BuiltInCategory.OST_Parts,
        };

        private static void SondaFamilia(Document doc, string arquivo, Dictionary<BuiltInCategory, (Element E, View V)> alvos, Action<string> W)
        {
            // tipos cujo TagText derrubou o Revit (13/11/2026: "Nome de Pilar" tipo "N - S - NV - Alinhamento dir", queda nativa);
            // SD_SONDA_SEM_TEXTO = trechos de nome de tipo separados por ";" (padrão: " NV ")
            string[] semTextoTipos = (Environment.GetEnvironmentVariable("SD_SONDA_SEM_TEXTO") ?? " NV ").Split(';').Where(x => x != "").ToArray();
            using (var tg = new TransactionGroup(doc, "Sonda: família"))
            {
                tg.Start();
                try
                {
                    Family fam = null;
                    W("   carregando…");
                    using (var t = new Transaction(doc, "Sonda: carregar"))
                    {
                        t.Start();
                        WarningSwallower.Attach(t);
                        doc.LoadFamily(arquivo, new SheetComposer.OverwriteNothing(), out fam);
                        t.Commit();
                    }
                    if (fam == null) { W("   não carregada (já existia com o mesmo nome?)"); return; }
                    var cat = (BuiltInCategory)(fam.FamilyCategory?.Id.Value ?? -1);
                    W($"   família \"{fam.Name}\", categoria {fam.FamilyCategory?.Name} ({cat})");
                    if (!AlvoDoIdentificador.TryGetValue(cat, out BuiltInCategory alvoCat)) { W("   categoria sem peça de teste: pulada"); return; }
                    if (!alvos.TryGetValue(alvoCat, out var alvo))
                    {
                        alvo = AcharAlvo(doc, alvoCat);
                        alvos[alvoCat] = alvo;
                    }
                    if (alvo.E == null) { W($"   nenhuma peça {alvoCat} visível numa planta do modelo de teste: pulada"); return; }
                    W($"   peça: {alvo.E.Category?.Name} id {alvo.E.Id.Value} \"{alvo.E.Name}\" na vista \"{alvo.V.Name}\"");

                    // parâmetros de texto vazios = «nome»
                    var marcados = new List<string>();
                    using (var t = new Transaction(doc, "Sonda: marcar parâmetros"))
                    {
                        t.Start();
                        WarningSwallower.Attach(t);
                        foreach (Element e in new[] { alvo.E, doc.GetElement(alvo.E.GetTypeId()) }.Where(x => x != null))
                            foreach (Parameter p in e.Parameters.Cast<Parameter>())
                                if (p.StorageType == StorageType.String && !p.IsReadOnly && !string.IsNullOrEmpty(p.Definition?.Name))
                                    try { if (p.Set("«" + p.Definition.Name + "»")) marcados.Add(p.Definition.Name); } catch { /* bloqueado */ }
                        t.Commit();
                    }
                    W($"   {marcados.Count} parâmetro(s) de texto graváveis marcados com «nome»");
                    var valores = Valores(doc, alvo.E);

                    foreach (FamilySymbol s in fam.GetFamilySymbolIds().Select(doc.GetElement).OfType<FamilySymbol>().OrderBy(x => x.Name))
                    {
                        bool semTexto = semTextoTipos.Any(x => s.Name.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
                        W($"   tipo \"{s.Name}\": criando identificador…" + (semTexto ? " (texto NÃO lido: ler TagText deste tipo derruba o Revit)" : ""));
                        string texto;
                        using (var t = new Transaction(doc, "Sonda: identificador"))
                        {
                            t.Start();
                            WarningSwallower.Attach(t);
                            if (!s.IsActive) s.Activate();
                            BoundingBoxXYZ bb = alvo.E.get_BoundingBox(alvo.V) ?? alvo.E.get_BoundingBox(null);
                            XYZ pt = bb == null ? XYZ.Zero : (bb.Min + bb.Max) / 2;
                            IndependentTag tag = IndependentTag.Create(doc, s.Id, alvo.V.Id, new Reference(alvo.E), false, TagOrientation.Horizontal, pt);
                            W("      criado; regenerando…");
                            doc.Regenerate();
                            W("      lendo o texto…");
                            texto = semTexto ? "" : tag.TagText ?? "";
                            t.Commit();
                        }
                        string uma = texto.Replace("\r", " ").Replace("\n", " / ");
                        var lidos = valores.Where(v => v.Valor.Length >= 2 && texto.IndexOf(v.Valor, StringComparison.Ordinal) >= 0)
                            .Select(v => $"{v.Nome}{v.Onde}=\"{v.Valor}\"").Distinct().ToList();
                        W($"      texto: \"{uma}\"");
                        W($"      lê: {(lidos.Count == 0 ? "(nenhum parâmetro reconhecido)" : string.Join("; ", lidos))}");
                    }
                }
                finally { tg.RollBack(); }
            }
        }

        /// <summary>Valores (texto como aparece) dos parâmetros da peça e do tipo.</summary>
        private static List<(string Nome, string Onde, string Valor)> Valores(Document doc, Element e)
        {
            var r = new List<(string, string, string)>();
            foreach (var (el, onde) in new[] { (e, ""), (doc.GetElement(e.GetTypeId()), " [tipo]") })
            {
                if (el == null) continue;
                foreach (Parameter p in el.Parameters.Cast<Parameter>())
                {
                    string v = null;
                    try { v = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString(); } catch { /* sem valor */ }
                    if (!string.IsNullOrWhiteSpace(v)) r.Add((p.Definition.Name, onde, v.Trim()));
                }
            }
            return r;
        }

        /// <summary>Peça da categoria visível numa planta (não modelo) — a planta onde o identificador vai.</summary>
        private static (Element E, View V) AcharAlvo(Document doc, BuiltInCategory cat)
        {
            foreach (ViewPlan v in new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                         .Where(p => !p.IsTemplate && (p.ViewType == ViewType.EngineeringPlan || p.ViewType == ViewType.FloorPlan)))
            {
                Element e = new FilteredElementCollector(doc, v.Id).OfCategory(cat).WhereElementIsNotElementType()
                    .FirstOrDefault(x => !(x is FamilyInstance fi) || fi.SuperComponent == null);
                if (e != null) return (e, v);
            }
            return (null, null);
        }
    }
}
