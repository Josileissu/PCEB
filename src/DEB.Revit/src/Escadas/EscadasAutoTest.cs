using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using DEB.Core.Elementos.Escada;
using DEB.Revit.Commands;
using DEB.Revit.Core;

namespace DEB.Revit.Escadas
{
    /// <summary>
    /// AutoRun "escadas" (D42): calcula uma escada de exemplo (ou a de SD_ESCADA = "Desnivel=144;Espelhos=8;…"), roda a conferência
    /// por MEF, grava a janela em PNG (uma imagem por aba), cria a escada no modelo aberto longe das peças e exporta um corte e
    /// uma vista 3D com as barras. Resultado em logs\escadas_AAAAMMDD_HHMMSS.
    /// </summary>
    internal static class EscadasAutoTest
    {
        public static string Run(UIDocument uidoc)
        {
            var doc = uidoc.Document;
            string dir = Path.Combine(DetailingReport.LogDir, "escadas_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            var e = new EscadaLaje
            {
                Desnivel = 144, Espelhos = 8, Piso = 28, Largura = 120, Espessura = 16, EspessuraPatamar = 16,
                PatamarInferior = 120, PatamarSuperior = 100, Revestimento = 1, Sobrecarga = 3, Fck = 25,
            };
            Ler(Environment.GetEnvironmentVariable("SD_ESCADA"), e, sb);

            var win = new EscadaWindow(e, "Autoteste");
            win.ConferirMefAgora();
            win.Otimizar();
            var r = win.Resultado;
            sb.AppendLine($"cálculo: n = {r.Geo.N}, e = {r.Geo.Espelho:0.00}, vão {r.Geo.Vao:0}, Md = {r.MdMax:0.00} kN·m/m, flecha total {r.FlechaTotal:0.00} cm");
            foreach (var b in r.Barras) sb.AppendLine("  " + b.Texto + $" — {b.Comprimento:0} cm");
            foreach (var v in r.Verificacoes) sb.AppendLine($"  [{v.Situacao}] {v.Codigo} {v.Descricao}: {v.Valor} ({v.Limite})");
            RenderToPng(win, Path.Combine(dir, "janela.png"));
            sb.AppendLine("janela: " + Path.Combine(dir, "janela*.png"));

            // longe das peças do modelo de teste
            var bbAll = new FilteredElementCollector(doc).WhereElementIsNotElementType().Where(x => x.Category != null && x.get_BoundingBox(null) != null)
                .Select(x => x.get_BoundingBox(null)).ToList();
            double xMin = bbAll.Count > 0 ? bbAll.Min(b => b.Min.X) : 0, yMin = bbAll.Count > 0 ? bbAll.Min(b => b.Min.Y) : 0;
            var nivel = EscadasRevit.NivelAbaixo(doc, 0.01);
            var pos = new PosicaoEscada { Base = new XYZ(xMin - 20, yMin - 20, nivel?.Elevation ?? 0), Direcao = XYZ.BasisX, Nivel = nivel, Origem = "autoteste" };
            // duas vigas de apoio (a de cima 144 cm acima, 416 cm adiante, giradas 30° no plano) e a posição lida delas
            var sym = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_StructuralFraming).Cast<FamilySymbol>().FirstOrDefault();
            if (sym != null)
            {
                double cm = 1 / 30.48, ang = Math.PI / 6;
                var dd = new XYZ(Math.Cos(ang), Math.Sin(ang), 0); var tt = new XYZ(-dd.Y, dd.X, 0);
                var o = new XYZ(xMin - 60, yMin - 60, nivel?.Elevation ?? 0);
                var apoios = new List<Element>();
                using (var t = new Transaction(doc, "Vigas de apoio (autoteste)"))
                {
                    t.Start();
                    if (!sym.IsActive) sym.Activate();
                    foreach (var (x, z) in new[] { (0.0, 0.0), (r.Geo.Vao, r.Entrada.Desnivel) })
                    {
                        var c0 = o + dd * (x * cm) - tt * (150 * cm) + XYZ.BasisZ * (z * cm);
                        var c1 = o + dd * (x * cm) + tt * (150 * cm) + XYZ.BasisZ * (z * cm);
                        var fiv = doc.Create.NewFamilyInstance(Line.CreateBound(c0, c1), sym, nivel, StructuralType.Beam);
                        apoios.Add(fiv);
                    }
                    t.Commit();
                }
                var e2 = r.Entrada.Copia();
                var pos2 = EscadasRevit.DeApoios(doc, apoios, e2);
                if (pos2 == null) sb.AppendLine("APOIOS: não deu para ler a posição das duas vigas");
                else
                {
                    var g2 = GeometriaEscada.De(e2);
                    sb.AppendLine($"apoios: {pos2.Origem}; direção {pos2.Direcao.X:0.000},{pos2.Direcao.Y:0.000} (esperado {dd.X:0.000},{dd.Y:0.000}); base z {pos2.Base.Z / cm:0.0} cm; " +
                                  $"desnível {e2.Desnivel:0.0}; vão lido {g2.Vao:0.0} (esperado {r.Geo.Vao:0.0}); patamares {e2.PatamarInferior:0}/{e2.PatamarSuperior:0}; largura {e2.Largura:0}; apoio {e2.LarguraApoio:0}");
                    pos = pos2;
                    r = CalculoEscada.Calcular(e2);
                }
            }
            string marca = EscadaCommand.Criar(doc, r, pos, out var log);
            sb.AppendLine($"criada: {marca}");
            foreach (var i in log.Infos) sb.AppendLine("  " + i);
            foreach (var w in log.Warnings) sb.AppendLine("  ATENÇÃO " + w);
            var fi = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().First(x => x.Symbol.Family.Name == EscadasRevit.PrefixoFamilia + marca);
            var rebars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>().Where(x => x.GetHostId() == fi.Id).ToList();
            sb.AppendLine($"barras no hospedeiro: {rebars.Count} conjuntos, {rebars.Sum(x => x.Quantity)} barras");
            var bb = fi.get_BoundingBox(null);
            sb.AppendLine($"caixa da escada (cm): {(bb.Max.X - bb.Min.X) * 30.48:0} × {(bb.Max.Y - bb.Min.Y) * 30.48:0} × {(bb.Max.Z - bb.Min.Z) * 30.48:0}; base z = {bb.Min.Z * 30.48:0.0}");
            sb.AppendLine("barras fora da caixa da escada (tolerância 25 cm, os apoios): " +
                rebars.Count(x => { var b2 = x.get_BoundingBox(null); return b2 == null || b2.Min.X < bb.Min.X - 0.82 || b2.Max.X > bb.Max.X + 0.82 || b2.Min.Z < bb.Min.Z - 0.82 || b2.Max.Z > bb.Max.Z + 0.82; }));

            // vistas: corte longitudinal no meio da largura e 3D com as barras sólidas
            using (var t = new Transaction(doc, "Vistas da escada (autoteste)"))
            {
                t.Start();
                var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>();
                var tSec = vft.First(v => v.ViewFamily == ViewFamily.Section);
                var t3d = vft.First(v => v.ViewFamily == ViewFamily.ThreeDimensional);
                double cm = 1 / 30.48;
                var o = pos.Base;
                var box = new BoundingBoxXYZ
                {
                    Transform = Autodesk.Revit.DB.Transform.Identity,
                    Min = new XYZ(-60 * cm, -60 * cm - r.Geo.Contorno.Min(p => p.Z) * 0, -10 * cm),
                    Max = new XYZ((r.Geo.Vao + 60) * cm, (r.Entrada.Desnivel + 60) * cm, 200 * cm),
                };
                // z local = −y do modelo: plano de corte em Max.Z (meio da largura), vista até Min.Z (além da largura)
                // a vista mostra o x local para a esquerda: com BasisX = −X a escada sobe para a direita no corte
                box.Min = new XYZ(-(r.Geo.Vao + 60) * cm, (r.Geo.Contorno.Min(p => p.Z) - 60) * cm, -(r.Entrada.Largura / 2 + 20) * cm);
                box.Max = new XYZ(60 * cm, (r.Entrada.Desnivel + 60) * cm, 0);
                var tr = Autodesk.Revit.DB.Transform.Identity;
                var dx = pos.Direcao.Normalize(); tr.Origin = o; tr.BasisX = -dx; tr.BasisY = XYZ.BasisZ; tr.BasisZ = (-dx).CrossProduct(XYZ.BasisZ);
                box.Transform = tr;
                var sec = ViewSection.CreateSection(doc, tSec.Id, box);
                sec.Name = "Escada " + marca + " — corte (autoteste)";
                sec.Scale = 25; sec.DetailLevel = ViewDetailLevel.Fine;
                var v3 = View3D.CreateIsometric(doc, t3d.Id);
                v3.Name = "Escada " + marca + " — 3D (autoteste)";
                v3.DetailLevel = ViewDetailLevel.Fine;
                v3.SetSectionBox(new BoundingBoxXYZ { Min = bb.Min - new XYZ(1, 1, 1), Max = bb.Max + new XYZ(1, 1, 1) });
                foreach (var rb in rebars) { rb.SetUnobscuredInView(sec, true); rb.SetUnobscuredInView(v3, true); }
                t.Commit();
                foreach (var (v, nome) in new[] { ((View)sec, "corte"), ((View)v3, "3d") })
                {
                    var ieo = new ImageExportOptions
                    {
                        ExportRange = ExportRange.SetOfViews, FilePath = Path.Combine(dir, nome), FitDirection = FitDirectionType.Horizontal,
                        HLRandWFViewsFileType = ImageFileType.PNG, ShadowViewsFileType = ImageFileType.PNG, ImageResolution = ImageResolution.DPI_150,
                        PixelSize = 2400, ZoomType = ZoomFitType.FitToPage,
                    };
                    ieo.SetViewsAndSheets(new List<ElementId> { v.Id });
                    doc.ExportImage(ieo);
                }
            }
            sb.AppendLine("diálogos fechados: " + string.Join(" | ", AutoRun.Dialogs));
            string arq = Path.Combine(dir, "resultado.txt");
            File.WriteAllText(arq, sb.ToString(), Encoding.UTF8);
            return arq;
        }

        /// <summary>"Chave=valor;…" sobre a entrada (nomes das propriedades de <see cref="EscadaLaje"/>).</summary>
        private static void Ler(string txt, EscadaLaje e, StringBuilder sb)
        {
            if (string.IsNullOrWhiteSpace(txt)) return;
            foreach (string par in txt.Split(';'))
            {
                var kv = par.Split('=');
                if (kv.Length != 2) continue;
                var p = typeof(EscadaLaje).GetProperty(kv[0].Trim());
                if (p == null || !double.TryParse(kv[1].Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) { sb.AppendLine("SD_ESCADA ignorado: " + par); continue; }
                if (p.PropertyType == typeof(int)) p.SetValue(e, (int)Math.Round(v)); else if (p.PropertyType == typeof(double)) p.SetValue(e, v);
            }
        }

        /// <summary>Uma imagem por aba (janela.png, janela_2.png…).</summary>
        public static void RenderToPng(EscadaWindow w, string path, int width = 1500, int height = 900)
        {
            var root = (FrameworkElement)w.Content;
            for (int t = 0; t < w.Abas.Items.Count; t++)
            {
                w.Abas.SelectedIndex = t;
                root.Measure(new System.Windows.Size(width, height));
                root.Arrange(new Rect(0, 0, width, height));
                root.UpdateLayout();
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                var bg = new DrawingVisual();
                using (DrawingContext dc = bg.RenderOpen()) dc.DrawRectangle(w.Background ?? Brushes.White, null, new Rect(0, 0, width, height));
                bmp.Render(bg);
                bmp.Render(root);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                string arq = t == 0 ? path : Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "_" + (t + 1) + ".png");
                using (var fs = File.Create(arq)) enc.Save(fs);
            }
            w.Abas.SelectedIndex = 0;
        }
    }
}
