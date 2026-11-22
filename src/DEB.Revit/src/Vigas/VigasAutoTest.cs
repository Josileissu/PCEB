using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DEB.Core.Elementos.Viga.PreMoldada.Calculo;
using DEB.Revit.Core;

namespace DEB.Revit.Vigas
{
    /// <summary>
    /// AutoRun "vigas" (D40): levanta as vigas, grava as cargas de teste (SD_CARGAS = "g;q;concentradas", padrão "12;6;"), calcula,
    /// grava o relatório, arma, cria montagens e vistas e exporta PNG das vistas (3D e elevação). SD_SAVEAS = cópia do modelo.
    /// SD_SOCALCULO = 1: só o cálculo (nada muda no modelo). A janela é renderizada em PNG.
    /// </summary>
    internal static class VigasAutoTest
    {
        public static string Run(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            string dir = Path.Combine(DetailingReport.LogDir, "vigas_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            var notas = new List<string>();
            List<GrupoViga> grupos = VigasPreMoldadas.Levantar(doc, notas);
            foreach (string n in notas) sb.AppendLine("nota: " + n);
            string[] c = (Environment.GetEnvironmentVariable("SD_CARGAS") ?? "12;6;").Split(';');
            double N(int i, double pad) => i < c.Length && double.TryParse(c[i].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ? x : pad;
            var cargas = new CargasViga { G = N(0, 12), Q = N(1, 6), Concentradas = CargasViga.LerConcentradas(c.Length > 2 ? c[2] : "") };
            foreach (GrupoViga g in grupos)
            {
                g.Cargas = cargas;
                foreach (var m in g.Membros) m.Cargas = cargas;
                VigasPreMoldadas.Calcular(g);
                sb.AppendLine($"{g.Marca} x{g.Quantidade} {g.Secao} L={g.Peca.L} {g.Pontas} alma y0={g.AlmaY0:0.#} z0={g.AlmaZ0:0.#} giros={string.Join(",", g.Membros.Select(m => m.Giro))}");
                foreach (string a in g.Avisos) sb.AppendLine("   aviso: " + a);
                if (g.Calculo == null) continue;
                sb.AppendLine($"   Md={g.Calculo.MdMax:0.0} kN.m Vd={g.Calculo.VdE:0.0}/{g.Calculo.VdD:0.0} kN; situação {g.Situacao}");
                foreach (string l in g.Calculo.ResumoProposta()) sb.AppendLine("   " + l);
                foreach (var v in g.Calculo.Verificacoes.Where(v => v.Situacao != DEB.Core.Normas.Verificacao.SituacaoVerificacao.Ok)) sb.AppendLine("   " + v.Mensagem);
                foreach (string a in g.Calculo.Avisos) sb.AppendLine("   aviso: " + a);
                var av = new List<string>();
                var bars = VigasPreMoldadas.Barras(g, g.Representante, av);
                sb.AppendLine($"   barras: {bars.Count} definições, {bars.Sum(b => b.Count)} barras; " + string.Join(" ", bars.GroupBy(b => b.Role).Select(x => $"{x.Key}={x.Sum(b => b.Count)}")));
                foreach (string a in av.Distinct()) sb.AppendLine("   layout: " + a);
                var rel = VigasPreMoldadas.Relatorio(doc, g);
                string html = Path.Combine(dir, DEB.Core.Documentacao.RelatorioHtml.NomeArquivo(g.Marca));
                File.WriteAllText(html, DEB.Core.Documentacao.RelatorioHtml.Gerar(rel), new UTF8Encoding(true));
                sb.AppendLine("   relatório: " + html);
            }
            try { new VigasWindow(doc, grupos, notas).RenderToPng(Path.Combine(dir, "janela.png")); if (VigaPreview.UltimoErro != null) sb.AppendLine("ERRO na prévia: " + VigaPreview.UltimoErro); sb.AppendLine("janela: " + VigasWindow.Diagnostico); }
            catch (Exception ex) { sb.AppendLine("ERRO ao renderizar a janela: " + ex); }

            if (Environment.GetEnvironmentVariable("SD_SOCALCULO") != "1")
            {
                var report = new DetailingReport();
                using (var tg = new TransactionGroup(doc, "Vigas pré-moldadas (teste)"))
                {
                    tg.Start();
                    VigasPreMoldadas.Aplicar(doc, grupos, report, uidoc.ActiveView?.Id ?? ElementId.InvalidElementId);
                    tg.Assimilate();
                }
                sb.AppendLine().AppendLine(report.Summary()).AppendLine().AppendLine(report.Details());
                foreach (PieceLog p in report.Pieces) foreach (string t in p.Traces) sb.AppendLine().AppendLine(t);

                List<ElementId> ids = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .Where(v => !v.IsTemplate && v.AssociatedAssemblyInstanceId != ElementId.InvalidElementId && !(v is ViewSchedule) && v.CanBePrinted)
                    .Select(v => v.Id).ToList();
                sb.AppendLine($"vistas de montagem: {ids.Count}");
                if (ids.Count > 0)
                {
                    var ieo = new ImageExportOptions
                    {
                        ExportRange = ExportRange.SetOfViews, FilePath = Path.Combine(dir, "vista"), FitDirection = FitDirectionType.Horizontal,
                        HLRandWFViewsFileType = ImageFileType.PNG, ShadowViewsFileType = ImageFileType.PNG, ImageResolution = ImageResolution.DPI_150,
                        PixelSize = 1800, ZoomType = ZoomFitType.FitToPage,
                    };
                    ieo.SetViewsAndSheets(ids);
                    try { doc.ExportImage(ieo); } catch (Exception ex) { sb.AppendLine("ERRO ao exportar PNG: " + ex.Message); }
                }
                string saveAs = Environment.GetEnvironmentVariable("SD_SAVEAS");
                if (!string.IsNullOrWhiteSpace(saveAs)) { doc.SaveAs(saveAs, new SaveAsOptions { OverwriteExistingFile = true }); sb.AppendLine("Modelo salvo em " + saveAs); }
            }
            string log = Path.Combine(dir, "resultado.txt");
            File.WriteAllText(log, sb.ToString(), Encoding.UTF8);
            return log;
        }
    }
}
