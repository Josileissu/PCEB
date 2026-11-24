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
    /// AutoRun "escadas" (D42), no modelo aberto, longe das peças:
    /// (1) longitudinal com patamares — calcula, roda o MEF e o otimizador, grava a janela em PNG, cria duas vigas de apoio em níveis
    /// diferentes (giradas 30°) e posiciona a escada por elas; (2) transversal e (3) em balanço, ao lado. Para cada escada: família,
    /// barras, corte longitudinal, corte transversal e 3D exportados. SD_ESCADA = "Desnivel=144;Espelhos=8;…" muda a de exemplo.
    /// Resultado em logs\escadas_AAAAMMDD_HHMMSS.
    /// </summary>
    internal static class EscadasAutoTest
    {
        private const double Cm = 1 / 30.48;

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

            var bbAll = new FilteredElementCollector(doc).WhereElementIsNotElementType().Where(x => x.Category != null && x.get_BoundingBox(null) != null)
                .Select(x => x.get_BoundingBox(null)).ToList();
            double xMin = bbAll.Count > 0 ? bbAll.Min(b => b.Min.X) : 0, yMin = bbAll.Count > 0 ? bbAll.Min(b => b.Min.Y) : 0;
            var nivel = EscadasRevit.NivelAbaixo(doc, 0.01);
            double z0 = nivel?.Elevation ?? 0;

            // ---- 1. longitudinal, posicionada por duas vigas ----
            var win = new EscadaWindow(e, "Autoteste");
            win.ConferirMefAgora();
            win.Otimizar();
            Escrever(sb, "longitudinal", win.Resultado);
            RenderToPng(win, Path.Combine(dir, "janela.png"));
            var pos = new PosicaoEscada { Base = new XYZ(xMin - 20, yMin - 20, z0), Direcao = XYZ.BasisX, Nivel = nivel, Origem = "autoteste" };
            var r = win.Resultado;
            var sym = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_StructuralFraming).Cast<FamilySymbol>().FirstOrDefault();
            if (sym != null)
            {
                double ang = Math.PI / 6;
                var dd = new XYZ(Math.Cos(ang), Math.Sin(ang), 0); var tt = new XYZ(-dd.Y, dd.X, 0);
                var o = new XYZ(xMin - 60, yMin - 60, z0);
                var apoios = new List<Element>();
                using (var t = new Transaction(doc, "Vigas de apoio (autoteste)"))
                {
                    t.Start();
                    if (!sym.IsActive) sym.Activate();
                    foreach (var (x, z) in new[] { (0.0, 0.0), (r.Geo.Vao, r.Entrada.Desnivel) })
                    {
                        var c0 = o + dd * (x * Cm) - tt * (150 * Cm) + XYZ.BasisZ * (z * Cm);
                        var c1 = o + dd * (x * Cm) + tt * (150 * Cm) + XYZ.BasisZ * (z * Cm);
                        apoios.Add(doc.Create.NewFamilyInstance(Line.CreateBound(c0, c1), sym, nivel, StructuralType.Beam));
                    }
                    t.Commit();
                }
                var e2 = r.Entrada.Copia();
                var pos2 = EscadasRevit.DeApoios(doc, apoios, e2);
                if (pos2 == null) sb.AppendLine("APOIOS: não deu para ler a posição das duas vigas");
                else
                {
                    var g2 = GeometriaEscada.De(e2);
                    sb.AppendLine($"apoios: {pos2.Origem}; direção {pos2.Direcao.X:0.000},{pos2.Direcao.Y:0.000} (esperado {dd.X:0.000},{dd.Y:0.000}); base z {(pos2.Base.Z - z0) / Cm:0.0} cm; " +
                                  $"desnível {e2.Desnivel:0.0}; vão lido {g2.Vao:0.0} (esperado {r.Geo.Vao:0.0}); patamares {e2.PatamarInferior:0}/{e2.PatamarSuperior:0}; largura {e2.Largura:0}; apoio {e2.LarguraApoio:0}");
                    pos = pos2; r = CalculoEscada.Calcular(e2);
                }
            }
            Criar(doc, r, pos, dir, "longitudinal", sb);

            // ---- 2. transversal e 3. balanço, lado a lado ----
            int k = 0;
            foreach (var tipo in new[] { TipoEscada.Transversal, TipoEscada.Balanco })
            {
                k++;
                var el = e.Copia(); el.Tipo = tipo; el.PatamarInferior = 0; el.PatamarSuperior = 0; el.Espessura = 12; el.EspessuraPatamar = 0;
                el.Parapeito = tipo == TipoEscada.Balanco ? 0.5 : 0; el.BordasComParapeito = tipo == TipoEscada.Balanco ? 1 : 0;
                var w = new EscadaWindow(el, "Autoteste");
                w.ConferirMefAgora();
                string nome = tipo == TipoEscada.Transversal ? "transversal" : "balanco";
                Escrever(sb, nome, w.Resultado);
                RenderToPng(w, Path.Combine(dir, $"janela_{nome}.png"));
                var p = new PosicaoEscada { Base = new XYZ(xMin - 20, yMin - 20 - k * 300 * Cm, z0), Direcao = XYZ.BasisX, Nivel = nivel, Origem = "autoteste" };
                Criar(doc, w.Resultado, p, dir, nome, sb);
            }
            // ---- 4. pré-moldada: apoio direto embaixo, dente em cima ----
            {
                var ep = e.Copia();
                ep.Execucao = ExecucaoEscada.PreMoldada;
                ep.LigacaoInferior = new LigacaoEscada { Ponta = PontaPreMoldada.ApoioDireto, ComprimentoApoio = 15, Apoio = ApoioPreMoldado.Elastomero, Pinos = 2, BitolaPino = 16 };
                ep.LigacaoSuperior = new LigacaoEscada { Ponta = PontaPreMoldada.Dente, ComprimentoApoio = 15, AlturaDente = 9, ComprimentoDente = 25, Apoio = ApoioPreMoldado.Argamassa, Pinos = 2, BitolaPino = 16 };
                var w = new EscadaWindow(ep, "Autoteste");
                w.ConferirMefAgora();
                Escrever(sb, "premoldada", w.Resultado);
                foreach (var lc in w.Resultado.Ligacoes) sb.AppendLine($"  ligação {lc.Lado}: Fd {lc.Fd:0.0} kN/m, Hd {lc.Hd:0.0} kN/m, tirante {lc.AsTirante:0.00} cm²/m, pinos {lc.Ligacao.Pinos}×{lc.RvdPino:0.0} kN ≥ {lc.HTotal:0.0} kN");
                if (w.Resultado.Icamento != null) sb.AppendLine($"  içamento: peso {w.Resultado.Icamento.Peso:0.0} kN, alça {w.Resultado.Icamento.ForcaAlca:0.0} kN");
                RenderToPng(w, Path.Combine(dir, "janela_premoldada.png"));
                var p = new PosicaoEscada { Base = new XYZ(xMin - 20, yMin - 20 - 3 * 300 * Cm, z0), Direcao = XYZ.BasisX, Nivel = nivel, Origem = "autoteste" };
                Criar(doc, w.Resultado, p, dir, "premoldada", sb);

            }
            // ---- 5. escadas em U (com a viga do patamar) e em L, pela janela do conjunto, criadas de uma vez ----
            {
                var lp = e.Copia();
                lp.Execucao = ExecucaoEscada.PreMoldada; lp.PatamarInferior = 40; lp.PatamarSuperior = 60;
                lp.LigacaoInferior = new LigacaoEscada { Ponta = PontaPreMoldada.ApoioDireto, ComprimentoApoio = 15, Apoio = ApoioPreMoldado.Elastomero, Pinos = 2, BitolaPino = 16 };
                lp.LigacaoSuperior = new LigacaoEscada { Ponta = PontaPreMoldada.Dente, ComprimentoApoio = 15, AlturaDente = 10, ComprimentoDente = 25, Apoio = ApoioPreMoldado.Argamassa, Pinos = 2, BitolaPino = 16 };
                foreach (var (modelo, nome, dy) in new[] { (0, "conjunto_u", 4), (1, "conjunto_l", 6) })
                {
                    var wc = new ConjuntoEscadaWindow(lp, "Autoteste");
                    wc.Montar(modelo);
                    var rc = wc.Resultado;
                    sb.AppendLine($"== {nome}: {wc.Conjunto.Modelo}; ordem {string.Join(" → ", rc.Ordem)}; erros: {string.Join(" | ", rc.Erros)}");
                    foreach (string t in rc.Transferencias) sb.AppendLine("  " + t);
                    foreach (string a in rc.Avisos) sb.AppendLine("  aviso: " + a);
                    foreach (var re in rc.ReacoesExternas) sb.AppendLine($"  sai do conjunto: {re.Peca} ({re.Ponta}) G {re.G:0.0} + Q {re.Q:0.0} kN");
                    foreach (var kv in rc.Vigas) sb.AppendLine($"  {kv.Key}: Md {kv.Value.MdMax:0.0} kN·m, reações G {kv.Value.RkE_G:0.0}/{kv.Value.RkD_G:0.0} kN, {string.Join("; ", kv.Value.ResumoProposta())}");
                    foreach (var kv in rc.Pecas) sb.AppendLine($"  {kv.Key}: Md {kv.Value.MdMax:0.00} kN·m/m, {kv.Value.Verificacoes.Count(v => v.Situacao == DEB.Core.Normas.Verificacao.SituacaoVerificacao.NaoAtende)} não atende");
                    foreach (var kv in rc.Pecas)
                        foreach (var rr in kv.Value.Rebaixos)
                            sb.AppendLine($"  encaixe: {kv.Key} recebe {rr.Rebaixo.Nome}: rebaixo x {rr.Rebaixo.X0:0}–{rr.Rebaixo.X1:0}, lado {rr.Rebaixo.Lado}, {rr.Rebaixo.Altura:0.#}×{rr.Rebaixo.Profundidade:0.#} cm; Fd {rr.Fd:0.0} kN/m; tirante ø{rr.Tirante.Bitola:0.#} c/{rr.Tirante.Esp:0.#}; suspensão ø{rr.Suspensao.Bitola:0.#} c/{rr.Suspensao.Esp:0.#}");
                    foreach (var kv in rc.Pecas) foreach (string av in kv.Value.Avisos) sb.AppendLine($"  aviso {kv.Key}: {av}");
                    RenderToPng(wc, wc.Abas, Path.Combine(dir, $"janela_{nome}.png"));
                    var org = new PosicaoEscada { Base = new XYZ(xMin - 20, yMin - 20 - dy * 300 * Cm, z0), Direcao = XYZ.BasisX, Nivel = nivel, Origem = "autoteste" };
                    var logs = ConjuntoEscadaCommand.Criar(doc, wc.Conjunto, rc, org);
                    foreach (var kv in logs)
                    {
                        sb.AppendLine($"  criada {kv.Key}");
                        foreach (var w in kv.Value.Warnings) sb.AppendLine("    ATENÇÃO " + w);
                        foreach (var w in kv.Value.Errors) sb.AppendLine("    ERRO " + w);
                    }
                    // barras dentro da caixa de cada peça
                    var inst = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                        .Where(x => x.Symbol.Family.Name.StartsWith(EscadasRevit.PrefixoFamilia) && logs.Keys.Any(k => k.EndsWith("(" + x.Symbol.Family.Name.Substring(EscadasRevit.PrefixoFamilia.Length) + ")"))).ToList();
                    BoundingBoxXYZ caixa = null;
                    foreach (var fi in inst)
                    {
                        var bb = fi.get_BoundingBox(null);
                        caixa = caixa == null ? new BoundingBoxXYZ { Min = bb.Min, Max = bb.Max } : new BoundingBoxXYZ
                        {
                            Min = new XYZ(Math.Min(caixa.Min.X, bb.Min.X), Math.Min(caixa.Min.Y, bb.Min.Y), Math.Min(caixa.Min.Z, bb.Min.Z)),
                            Max = new XYZ(Math.Max(caixa.Max.X, bb.Max.X), Math.Max(caixa.Max.Y, bb.Max.Y), Math.Max(caixa.Max.Z, bb.Max.Z)),
                        };
                        var rbs = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>().Where(x => x.GetHostId() == fi.Id).ToList();
                        double tol = 10 * Cm;
                        int fora = rbs.Count(x => { var b2 = x.get_BoundingBox(null); return b2 == null || b2.Min.X < bb.Min.X - tol || b2.Max.X > bb.Max.X + tol || b2.Min.Y < bb.Min.Y - tol || b2.Max.Y > bb.Max.Y + tol || b2.Min.Z < bb.Min.Z - tol || b2.Max.Z > bb.Max.Z + tol; });
                        sb.AppendLine($"  {fi.Symbol.Family.Name}: {rbs.Count} conjuntos, {rbs.Sum(x => x.Quantity)} barras, fora da peça: {fora}");
                    }
                    if (caixa == null) continue;
                    View3D v3;
                    using (var t = new Transaction(doc, "3D do conjunto (autoteste)"))
                    {
                        t.Start();
                        var t3d = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(v => v.ViewFamily == ViewFamily.ThreeDimensional);
                        v3 = View3D.CreateIsometric(doc, t3d.Id);
                        v3.Name = $"Escada {nome} — 3D (autoteste)"; v3.DetailLevel = ViewDetailLevel.Fine;
                        v3.SetSectionBox(new BoundingBoxXYZ { Min = caixa.Min - new XYZ(1, 1, 1), Max = caixa.Max + new XYZ(1, 1, 1) });
                        t.Commit();
                    }
                    var ieo = new ImageExportOptions
                    {
                        ExportRange = ExportRange.SetOfViews, FilePath = Path.Combine(dir, nome + "_3d"), FitDirection = FitDirectionType.Horizontal,
                        HLRandWFViewsFileType = ImageFileType.PNG, ShadowViewsFileType = ImageFileType.PNG, ImageResolution = ImageResolution.DPI_150, PixelSize = 2000, ZoomType = ZoomFitType.FitToPage,
                    };
                    ieo.SetViewsAndSheets(new List<ElementId> { v3.Id });
                    doc.ExportImage(ieo);
                }
            }
            sb.AppendLine("diálogos fechados: " + string.Join(" | ", AutoRun.Dialogs));
            string arq = Path.Combine(dir, "resultado.txt");
            File.WriteAllText(arq, sb.ToString(), Encoding.UTF8);
            return arq;
        }

        private static void Escrever(StringBuilder sb, string nome, ResultadoEscada r)
        {
            sb.AppendLine($"== {nome}: n = {r.Geo.N}, e = {r.Geo.Espelho:0.00}, vão de cálculo {r.VaoCalculo:0}, Md = {r.MdMax:0.00} kN·m/m, flecha total {r.FlechaTotal:0.00} cm");
            foreach (var b in r.Barras) sb.AppendLine("  " + b.Texto + $" — {b.Comprimento:0} cm");
            foreach (var v in r.Verificacoes.Where(v => v.Situacao != DEB.Core.Normas.Verificacao.SituacaoVerificacao.Ok))
                sb.AppendLine($"  [{v.Situacao}] {v.Codigo} {v.Descricao}: {v.Valor} ({v.Limite})");
            foreach (var a in r.Avisos) sb.AppendLine("  aviso: " + a);
        }

        /// <summary>Cria a escada e exporta o corte longitudinal (meio da largura), o transversal (meio do lance) e o 3D.</summary>
        private static void Criar(Document doc, ResultadoEscada r, PosicaoEscada pos, string dir, string nome, StringBuilder sb)
        {
            string marca = EscadaCommand.Criar(doc, r, pos, out var log);
            sb.AppendLine($"criada ({nome}): {marca}");
            foreach (var i in log.Infos) sb.AppendLine("  " + i);
            foreach (var w in log.Warnings) sb.AppendLine("  ATENÇÃO " + w);
            foreach (var w in log.Errors) sb.AppendLine("  ERRO " + w);
            var fi = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().FirstOrDefault(x => x.Symbol.Family.Name == EscadasRevit.PrefixoFamilia + marca);
            if (fi == null)
            {
                sb.AppendLine("  ERRO: instância não encontrada. Famílias PCEB no projeto: " + string.Join(", ", new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                    .Where(f => f.Name.StartsWith("PCEB")).Select(f => $"{f.Name} ({new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().Count(x => x.Symbol.Family.Id == f.Id)} inst.)")));
                return;
            }
            var rebars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>().Where(x => x.GetHostId() == fi.Id).ToList();
            sb.AppendLine($"  barras no hospedeiro: {rebars.Count} conjuntos, {rebars.Sum(x => x.Quantity)} barras (núcleo: {r.Barras.Sum(b => b.Quantidade)})");
            var bb = fi.get_BoundingBox(null);
            double tol = (r.Entrada.LarguraApoio + 30) * Cm;
            sb.AppendLine($"  barras fora da caixa da escada (tolerância: o apoio + 30 cm): " +
                rebars.Count(x => { var b2 = x.get_BoundingBox(null); return b2 == null || b2.Min.X < bb.Min.X - tol || b2.Max.X > bb.Max.X + tol || b2.Min.Y < bb.Min.Y - tol || b2.Max.Y > bb.Max.Y + tol || b2.Min.Z < bb.Min.Z - tol || b2.Max.Z > bb.Max.Z + tol; }));

            var vistas = new List<(View, string)>();
            using (var t = new Transaction(doc, "Vistas da escada (autoteste)"))
            {
                t.Start();
                var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>();
                var tSec = vft.First(v => v.ViewFamily == ViewFamily.Section);
                var t3d = vft.First(v => v.ViewFamily == ViewFamily.ThreeDimensional);
                var o = pos.Base; var dx = pos.Direcao.Normalize(); var sy = new XYZ(-dx.Y, dx.X, 0);
                double zMin = r.Geo.Contorno.Min(p => p.Z) - 60, la = r.Entrada.LarguraApoio, B = r.Entrada.Largura;
                // longitudinal: olha o lado da escada; x local para a esquerda (BasisX = −d) para a escada subir para a direita
                var tr = Autodesk.Revit.DB.Transform.Identity;
                tr.Origin = o; tr.BasisX = -dx; tr.BasisY = XYZ.BasisZ; tr.BasisZ = (-dx).CrossProduct(XYZ.BasisZ);
                var box = new BoundingBoxXYZ
                {
                    Transform = tr,
                    Min = new XYZ(-(r.Geo.Vao + 60) * Cm, zMin * Cm, -(B / 2 + 20) * Cm),
                    Max = new XYZ(60 * Cm, (r.Entrada.Desnivel + 60) * Cm, 0),
                };
                var sec = ViewSection.CreateSection(doc, tSec.Id, box);
                sec.Name = $"Escada {marca} — corte longitudinal (autoteste)"; sec.Scale = 25; sec.DetailLevel = ViewDetailLevel.Fine;
                vistas.Add((sec, $"{nome}_corte"));
                // transversal: plano vertical perpendicular ao sentido, no meio do lance
                double xm = (r.Geo.X1 + r.Geo.X2) / 2;
                var tr2 = Autodesk.Revit.DB.Transform.Identity;
                tr2.Origin = o + dx * (xm * Cm); tr2.BasisX = sy; tr2.BasisY = XYZ.BasisZ; tr2.BasisZ = sy.CrossProduct(XYZ.BasisZ);
                var box2 = new BoundingBoxXYZ
                {
                    Transform = tr2,
                    Min = new XYZ(-(B / 2 + la + 40) * Cm, zMin * Cm, -30 * Cm),
                    Max = new XYZ((B / 2 + la + 40) * Cm, (r.Entrada.Desnivel + 30) * Cm, 0),
                };
                var sec2 = ViewSection.CreateSection(doc, tSec.Id, box2);
                sec2.Name = $"Escada {marca} — corte transversal (autoteste)"; sec2.Scale = 20; sec2.DetailLevel = ViewDetailLevel.Fine;
                vistas.Add((sec2, $"{nome}_transversal"));
                var v3 = View3D.CreateIsometric(doc, t3d.Id);
                v3.Name = $"Escada {marca} — 3D (autoteste)"; v3.DetailLevel = ViewDetailLevel.Fine;
                v3.SetSectionBox(new BoundingBoxXYZ { Min = bb.Min - new XYZ(1, 1, 1), Max = bb.Max + new XYZ(1, 1, 1) });
                vistas.Add((v3, $"{nome}_3d"));
                foreach (var rb in rebars) foreach (var (v, _) in vistas) rb.SetUnobscuredInView(v, true);
                t.Commit();
            }
            foreach (var (v, arq) in vistas)
            {
                var ieo = new ImageExportOptions
                {
                    ExportRange = ExportRange.SetOfViews, FilePath = Path.Combine(dir, arq), FitDirection = FitDirectionType.Horizontal,
                    HLRandWFViewsFileType = ImageFileType.PNG, ShadowViewsFileType = ImageFileType.PNG, ImageResolution = ImageResolution.DPI_150,
                    PixelSize = 2000, ZoomType = ZoomFitType.FitToPage,
                };
                ieo.SetViewsAndSheets(new List<ElementId> { v.Id });
                doc.ExportImage(ieo);
            }
        }

        /// <summary>"Chave=valor;…" sobre a entrada (nomes das propriedades de <see cref="EscadaLaje"/>; Tipo = 0, 1 ou 2).</summary>
        private static void Ler(string txt, EscadaLaje e, StringBuilder sb)
        {
            if (string.IsNullOrWhiteSpace(txt)) return;
            foreach (string par in txt.Split(';'))
            {
                var kv = par.Split('=');
                if (kv.Length != 2) continue;
                var p = typeof(EscadaLaje).GetProperty(kv[0].Trim());
                if (p == null || !double.TryParse(kv[1].Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) { sb.AppendLine("SD_ESCADA ignorado: " + par); continue; }
                if (p.PropertyType == typeof(int)) p.SetValue(e, (int)Math.Round(v));
                else if (p.PropertyType == typeof(double)) p.SetValue(e, v);
                else if (p.PropertyType == typeof(TipoEscada)) p.SetValue(e, (TipoEscada)(int)Math.Round(v));
            }
        }

        /// <summary>Uma imagem por aba (janela.png, janela_2.png…).</summary>
        public static void RenderToPng(EscadaWindow w, string path, int width = 1500, int height = 900) => RenderToPng(w, w.Abas, path, width, height);

        public static void RenderToPng(Window w, System.Windows.Controls.TabControl abas, string path, int width = 1500, int height = 900)
        {
            var root = (FrameworkElement)w.Content;
            for (int t = 0; t < abas.Items.Count; t++)
            {
                abas.SelectedIndex = t;
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
            abas.SelectedIndex = 0;
        }
    }
}
