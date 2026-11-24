using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using DEB.Core.Analise.Barras;
using DEB.Core.Analise.Objetos;
using DEB.Core.Elementos.Metalica;
using DEB.Core.Elementos.Metalica.Ligacoes;

namespace DEB.Revit.Analise
{
    /// <summary>
    /// Modela no Revit as ligações metálicas calculadas pelo PCEB (D49) com as famílias PCEB (chapas e parafusos), cada ligação num
    /// grupo "PCEB &lt;id&gt;"; rodar de novo substitui o que existir da mesma ligação. A geometria vem dos mesmos dados do cálculo
    /// (modelo da biblioteca resolvido para a peça e espessuras adotadas). Cantoneiras: chapas das duas abas de cada cantoneira.
    /// </summary>
    internal static class LigacoesRevit
    {
        private const double CmPe = 30.48, MmPe = 304.8;
        private static XYZ P(Vetor3 v) => new XYZ(v.X / CmPe, v.Y / CmPe, v.Z / CmPe);
        private static XYZ D(Vetor3 v) => new XYZ(v.X, v.Y, v.Z).Normalize();
        private static double Mm(double mm) => mm / MmPe;

        public static int Modelar(Document doc, EstruturaObjetos e, StringBuilder log)
        {
            if (e.LigacoesMetalicas.Count == 0) return 0;
            var (chapa, parafuso) = FamiliasLigacoes.Garantir(doc, log);
            if (chapa == null || parafuso == null) { log.AppendLine("Famílias PCEB de ligação indisponíveis: nada modelado."); return 0; }
            int n = 0;
            using (var t = new Transaction(doc, "PCEB: modelar ligações metálicas"))
            {
                var fho = t.GetFailureHandlingOptions();
                fho.SetFailuresPreprocessor(new Falhas(log));
                t.SetFailureHandlingOptions(fho);
                t.Start();
                if (!chapa.IsActive) chapa.Activate();
                if (!parafuso.IsActive) parafuso.Activate();
                foreach (var l in e.LigacoesMetalicas)
                {
                    try
                    {
                        Apagar(doc, l.Dono.Id);
                        var ids = new List<ElementId>();
                        switch (l.Tipo)
                        {
                            case TipoLigacao.ChapaAlma: ChapaAlma(doc, l, chapa, parafuso, ids); break;
                            case TipoLigacao.CantoneiraDupla: Cantoneira(doc, l, chapa, parafuso, ids); break;
                            case TipoLigacao.ChapaTopo: ChapaTopo(doc, l, chapa, parafuso, ids); break;
                            case TipoLigacao.PlacaBase: PlacaBase(doc, l, chapa, parafuso, ids); break;
                            default: log.AppendLine($"{l.Dono}: tipo {l.Tipo} ainda sem modelagem no Revit."); continue;
                        }
                        foreach (var id in ids) doc.GetElement(id).LookupParameter(FamiliasLigacoes.PLigacao)?.Set(l.Dono.Id);
                        if (ids.Count > 1)
                        {
                            Group g = doc.Create.NewGroup(ids);
                            try { g.GroupType.Name = "PCEB " + l.Dono.Id; } catch { }
                        }
                        n++;
                    }
                    catch (Exception ex) { log.AppendLine($"{l.Dono}: não modelada ({ex.Message})."); }
                }
                var st = t.Commit();
                if (st != TransactionStatus.Committed) { log.AppendLine($"Transação das ligações: {st}."); n = 0; }
            }
            log.AppendLine($"Ligações modeladas no Revit com as famílias PCEB: {n} de {e.LigacoesMetalicas.Count}.");
            return n;
        }

        /// <summary>Só no AutoRun: vista 3D sombreada com caixa de corte em volta das peças de aço e das ligações PCEB, gravada em PNG.</summary>
        public static string ImagemPortico(Document doc, string png)
        {
            var els = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Where(fi => fi.StructuralMaterialType == Autodesk.Revit.DB.Structure.StructuralMaterialType.Steel || fi.Symbol.FamilyName.StartsWith("PCEB Chapa") || fi.Symbol.FamilyName.StartsWith("PCEB Parafuso"))
                .Select(fi => fi.get_BoundingBox(null)).Where(b => b != null).ToList();
            if (els.Count == 0) return "Imagem 3D: nada para mostrar.";
            var min = new XYZ(els.Min(b => b.Min.X), els.Min(b => b.Min.Y), els.Min(b => b.Min.Z)) - new XYZ(1, 1, 1);
            var max = new XYZ(els.Max(b => b.Max.X), els.Max(b => b.Max.Y), els.Max(b => b.Max.Z)) + new XYZ(1, 1, 1);
            View3D v;
            using (var t = new Transaction(doc, "PCEB: vista das ligações"))
            {
                t.Start();
                var tipo = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(x => x.ViewFamily == ViewFamily.ThreeDimensional);
                v = View3D.CreateIsometric(doc, tipo.Id);
                v.SetSectionBox(new BoundingBoxXYZ { Min = min, Max = max });
                v.DisplayStyle = DisplayStyle.ShadingWithEdges;
                v.DetailLevel = ViewDetailLevel.Fine;
                t.Commit();
            }
            var o = new ImageExportOptions
            {
                ExportRange = ExportRange.SetOfViews, FilePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(png), System.IO.Path.GetFileNameWithoutExtension(png)),
                ZoomType = ZoomFitType.FitToPage, PixelSize = 1600, ImageResolution = ImageResolution.DPI_150, HLRandWFViewsFileType = ImageFileType.PNG,
                ShadowViewsFileType = ImageFileType.PNG, FitDirection = FitDirectionType.Horizontal,
            };
            o.SetViewsAndSheets(new List<ElementId> { v.Id });
            doc.ExportImage(o);
            var arq = System.IO.Directory.GetFiles(System.IO.Path.GetDirectoryName(png), System.IO.Path.GetFileNameWithoutExtension(png) + "*.png").FirstOrDefault();
            string r = "Imagem 3D: " + (arq ?? "não gravada");
            // aproximação: a primeira chapa de alma e a primeira placa de base
            foreach (string prefixo in new[] { "chapa de alma", "placa de base" })
            {
                var membros = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                    .Where(fi => fi.Symbol.FamilyName.StartsWith("PCEB")).GroupBy(fi => fi.LookupParameter(FamiliasLigacoes.PLigacao)?.AsString() ?? "")
                    .FirstOrDefault(g => g.Key != "" && (prefixo == "placa de base" ? g.Key.EndsWith("-base") : !g.Key.EndsWith("-base")));
                if (membros == null) continue;
                var bbs = membros.Select(x => x.get_BoundingBox(null)).Where(b => b != null).ToList();
                var mn = new XYZ(bbs.Min(b => b.Min.X), bbs.Min(b => b.Min.Y), bbs.Min(b => b.Min.Z)) - new XYZ(1.2, 1.2, 1.2);
                var mx = new XYZ(bbs.Max(b => b.Max.X), bbs.Max(b => b.Max.Y), bbs.Max(b => b.Max.Z)) + new XYZ(1.2, 1.2, 1.2);
                using (var t = new Transaction(doc, "PCEB: vista da ligação"))
                {
                    t.Start();
                    v.SetSectionBox(new BoundingBoxXYZ { Min = mn, Max = mx });
                    t.Commit();
                }
                string nome = System.IO.Path.GetFileNameWithoutExtension(png) + "_" + prefixo.Replace(' ', '_');
                o.FilePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(png), nome);
                doc.ExportImage(o);
                r += Environment.NewLine + "Imagem da ligação: " + System.IO.Directory.GetFiles(System.IO.Path.GetDirectoryName(png), nome + "*.png").FirstOrDefault();
            }
            return r;
        }

        private sealed class Falhas : IFailuresPreprocessor
        {
            private readonly StringBuilder _log;
            public Falhas(StringBuilder log) { _log = log; }
            public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
            {
                var vistos = new HashSet<string>();
                foreach (FailureMessageAccessor m in fa.GetFailureMessages())
                {
                    string txt = $"[{m.GetSeverity()}] {m.GetDescriptionText()}";
                    if (vistos.Add(txt)) _log.AppendLine("  " + txt);
                    if (m.GetSeverity() == FailureSeverity.Warning) fa.DeleteWarning(m);
                }
                return FailureProcessingResult.Continue;
            }
        }

        /// <summary>Apaga as peças (e o grupo) de uma ligação já modelada.</summary>
        private static void Apagar(Document doc, string id)
        {
            var grupos = new FilteredElementCollector(doc).OfClass(typeof(Group)).Cast<Group>().Where(g => g.GroupType.Name == "PCEB " + id).ToList();
            foreach (var g in grupos) { var membros = g.UngroupMembers(); doc.Delete(membros); }
            var soltos = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Where(fi => fi.LookupParameter(FamiliasLigacoes.PLigacao)?.AsString() == id).Select(fi => fi.Id).ToList();
            if (soltos.Count > 0) doc.Delete(soltos);
            foreach (var gt in new FilteredElementCollector(doc).OfClass(typeof(GroupType)).Cast<GroupType>().Where(x => x.Name == "PCEB " + id && x.Groups.Size == 0).Select(x => x.Id).ToList())
                doc.Delete(gt);
        }

        /// <summary>
        /// Coloca uma caixa PCEB num plano por <paramref name="origem"/> com normal = eixoX × eixoNormal… (o plano contém eixoX e
        /// eixoY; a espessura cresce para eixoX × eixoY, ou fica centrada no caso do parafuso).
        /// </summary>
        private static ElementId Caixa(Document doc, FamilySymbol s, XYZ centro, XYZ eixoX, XYZ eixoY, double compMm, double largMm, double espMm, List<ElementId> ids)
        {
            // plano com normal eixoX × eixoY: linha ao longo de eixoX e vetor de corte eixoY
            // plano criado numa vista 3D: numa planta o Revit projeta o plano no plano da vista (as chapas saíam deitadas)
            // a API dá normal = corte × (livre − bolha): para a normal eixoX × eixoY, o corte é −eixoY. Plano grande e centrado no ponto
            // (com o plano curto a instância não se prendia: "a origem não está na face do hospedeiro").
            ReferencePlane rp = doc.Create.NewReferencePlane2(centro - eixoX * 3, centro + eixoX * 3, centro + eixoY * 3, Vista3D(doc));
            if (rp.Normal.DotProduct(eixoX.CrossProduct(eixoY)) < 0) rp.Flip();
            FamilyInstance fi = doc.Create.NewFamilyInstance(rp.GetReference(), centro, eixoX, s);
            if (Diag != null && Diag.Length < 2000)
            {
                doc.Regenerate();
                var bb = fi.get_BoundingBox(null);
                Diag.AppendLine($"  plano normal {rp.Normal} (pedido {eixoX.CrossProduct(eixoY)}), eixoX {eixoX}; instância X {fi.HandOrientation}, Y {fi.FacingOrientation}; bb {(bb.Max - bb.Min) * 304.8}");
            }
            fi.LookupParameter(FamiliasLigacoes.PComp)?.Set(Mm(compMm));
            fi.LookupParameter(FamiliasLigacoes.PLarg)?.Set(Mm(largMm));
            fi.LookupParameter(FamiliasLigacoes.PEsp)?.Set(Mm(espMm));
            ids.Add(fi.Id);
            return fi.Id;
        }

        /// <summary>Diagnóstico de orientação (AutoRun).</summary>
        public static StringBuilder Diag;
        private static View3D _v3;
        private static View3D Vista3D(Document doc)
        {
            if (_v3 != null && _v3.IsValidObject && _v3.Document.Equals(doc)) return _v3;
            _v3 = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(v => !v.IsTemplate && !v.IsPerspective);
            if (_v3 == null)
            {
                var tipo = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(x => x.ViewFamily == ViewFamily.ThreeDimensional);
                _v3 = View3D.CreateIsometric(doc, tipo.Id);
            }
            return _v3;
        }

        /// <summary>Ponta da viga: ponto do eixo, direção para dentro da viga, lateral (horizontal) e afastamento da face do suporte (mm).</summary>
        private static (XYZ Ponto, XYZ Dir, XYZ Lat, double Afast) Ponta(LigacaoMetalica l)
        {
            var v = l.Viga;
            XYZ p = P((l.PontaI ? v.PontoI : v.PontoJ) + v.DeslocamentoFisico);
            XYZ dir = D(v.Direcao); if (!l.PontaI) dir = -dir;
            XYZ lat = dir.CrossProduct(XYZ.BasisZ).Normalize();
            var s = l.Suporte;
            double afast = s.Pilar ? (Math.Abs(D(s.DirAlma).DotProduct(dir)) > 0.7 ? s.Perfil.D / 2 : s.Perfil.Tw / 2) : s.Perfil.Tw / 2;
            return (p, dir, lat, afast + 2);   // 2 mm de folga de montagem
        }

        private static void ChapaAlma(Document doc, LigacaoMetalica l, FamilySymbol ch, FamilySymbol pa, List<ElementId> ids)
        {
            var d = l.Chapa;
            var (p, dir, lat, af) = Ponta(l);
            double larg = d.A + d.EhChapa;
            // chapa encostada na face da alma (lado +lat), crescendo para fora; comprimento vertical, largura ao longo da viga
            XYZ face = p + lat * Mm(d.Viga.Tw / 2);
            XYZ c = face + dir * Mm(af + larg / 2);
            Caixa(doc, ch, c, XYZ.BasisZ, -dir, d.AlturaChapa, larg, d.Tp, ids);       // normal Z × (−dir) = +lat: cresce para fora da alma
            for (int i = 0; i < d.N; i++)
            {
                double y = (i - (d.N - 1) / 2.0) * d.S;
                XYZ pb = p + dir * Mm(af + d.A) + XYZ.BasisZ * Mm(y) + lat * Mm((d.Tp) / 2);
                Caixa(doc, pa, pb, XYZ.BasisZ, dir, d.Db, d.Db, d.Viga.Tw + d.Tp + 2 * d.Db, ids);
            }
        }

        private static void Cantoneira(Document doc, LigacaoMetalica l, FamilySymbol ch, FamilySymbol pa, List<ElementId> ids)
        {
            var d = l.Cantoneira;
            var (p, dir, lat, af) = Ponta(l);
            foreach (int lado in new[] { 1, -1 })
            {
                XYZ ln = lat * lado;
                // aba na alma da viga
                XYZ faceAlma = p + ln * Mm(d.Viga.Tw / 2);
                Caixa(doc, ch, faceAlma + dir * Mm(af + d.Aba / 2), XYZ.BasisZ, lado > 0 ? -dir : dir, d.Comprimento, d.Aba, d.T, ids);   // normal = ±lat, para fora
                // aba no suporte (perpendicular à viga)
                XYZ faceSup = p + dir * Mm(af) + ln * Mm(d.Viga.Tw / 2 + d.Aba / 2);
                Caixa(doc, ch, faceSup, XYZ.BasisZ, lat, d.Comprimento, d.Aba, d.T, ids);   // normal Z × lat = dir: da face do suporte para a viga
            }
            for (int i = 0; i < d.N; i++)
            {
                double y = (i - (d.N - 1) / 2.0) * d.S;
                Caixa(doc, pa, p + dir * Mm(af + d.Aba - d.Eh) + XYZ.BasisZ * Mm(y), XYZ.BasisZ, dir, d.Db, d.Db, d.Viga.Tw + 2 * d.T + 2 * d.Db, ids);
            }
        }

        private static void ChapaTopo(Document doc, LigacaoMetalica l, FamilySymbol ch, FamilySymbol pa, List<ElementId> ids)
        {
            var d = l.Topo;
            var (p, dir, lat, af) = Ponta(l);
            double tp = d.TpAdotada > 0 ? d.TpAdotada : (d.Tp > 0 ? d.Tp : 19);
            var w = d.Viga;
            double topo = d.Estendida ? -(d.Pf + Math.Max(1.5 * d.Db, 40)) : -10, base0 = w.D + 10;
            XYZ topoViga = p + XYZ.BasisZ * Mm(w.D / 2);
            XYZ face = p + dir * Mm(af - 2);
            XYZ centro = new XYZ(face.X, face.Y, topoViga.Z - Mm((topo + base0) / 2));
            // plano com eixos vertical e lateral; espessura para dentro da viga (Z × lat = ±dir)
            XYZ eixoY = XYZ.BasisZ.CrossProduct(lat).DotProduct(dir) > 0 ? lat : -lat;
            Caixa(doc, ch, centro, XYZ.BasisZ, eixoY, base0 - topo, d.BpEf, tp, ids);
            var ys = new List<double>();
            if (d.Estendida) ys.Add(-d.Pf);
            for (int k = 0; k < d.LinhasInternas; k++) ys.Add(w.Tf + d.Pf + k * 3 * d.Db);
            ys.Add(w.D - w.Tf - d.Pf);
            foreach (double y in ys)
                foreach (int s in new[] { -1, 1 })
                    Caixa(doc, pa, new XYZ(face.X, face.Y, topoViga.Z - Mm(y)) + lat * Mm(s * d.G / 2), XYZ.BasisZ, eixoY, d.Db, d.Db, tp + 25 + 2 * d.Db, ids);
        }

        private static void PlacaBase(Document doc, LigacaoMetalica l, FamilySymbol ch, FamilySymbol pa, List<ElementId> ids)
        {
            var d = l.Placa;
            var pil = l.Pilar;
            XYZ b = P(pil.PontoI);
            XYZ alma = D(pil.DirAlma); alma = new XYZ(alma.X, alma.Y, 0).Normalize();
            XYZ mesa = XYZ.BasisZ.CrossProduct(alma);
            double tp = d.TpAdotada > 0 ? d.TpAdotada : (d.Tp > 0 ? d.Tp : 25);
            // placa abaixo do pé do pilar: plano horizontal com eixos alma × mesa; a espessura cresce para alma × mesa = +Z → centro em −tp
            Caixa(doc, ch, b - XYZ.BasisZ * Mm(tp), alma, mesa, d.LxEf, d.LyEf, tp, ids);
            var (a1, a2, _, _) = d.Tabela18(out _);
            int porLado = Math.Max(1, d.Nb / 2);
            for (int lado = -1; lado <= 1; lado += 2)
                for (int k = 0; k < porLado; k++)
                {
                    double y = porLado > 1 ? (k - (porLado - 1) / 2.0) * (d.LyEf - 2 * a1) / (porLado - 1) : 0;
                    XYZ c = b + alma * Mm(lado * d.A) + mesa * Mm(y) - XYZ.BasisZ * Mm(tp);
                    Caixa(doc, pa, c, alma, mesa, d.Db, d.Db, 2 * (tp + 300), ids);   // chumbador (trecho visível ±)
                }
        }
    }
}
