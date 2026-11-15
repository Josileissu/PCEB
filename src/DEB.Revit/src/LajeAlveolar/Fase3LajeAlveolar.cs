using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DEB.Core;
using DEB.Core.Lajes;
using static DEB.Revit.LajeAlveolar.GeometriaRevit;

namespace DEB.Revit.LajeAlveolar
{
    /// <summary>Perfil medido de uma junta entre dois painéis do modelo (pontos (t, z) em metros; t ao longo de n, de A para B).</summary>
    internal sealed class PerfilJunta
    {
        public List<(double T, double Z)> Pontos = new List<(double, double)>();
        /// <summary>Centro da parte estreita (onde a junta "fecha"), em relação à linha da junta.</summary>
        public double TFecha;
        public double ZFecha;
        /// <summary>Menor abertura medida (m).</summary>
        public double AberturaMin = double.NaN;
        /// <summary>n em planta (pés, unitário), de A para B.</summary>
        public XYZ N;
    }

    /// <summary>
    /// Fase 3 (port do protótipo, opcional por partes): capa, chaves de cisalhamento e tirantes dos suportes de instalações.
    /// Registrados como complementos da inserção (<see cref="Todos"/>).
    /// </summary>
    internal static class Fase3LajeAlveolar
    {
        public const string AppId = "DEB.Revit.LajeAlveolar";

        public static readonly IComplementoLajeAlveolar[] Todos = { new Capa(), new Chaves(), new Tirantes() };

        // ------------------------------------------------------------------ capa
        /// <summary>Os pisos paginados passam a um tipo "CAPA e" (camada única com a espessura da capa, material do piso). O piso é
        /// referenciado pelo topo: o topo fica e a base sobe até o topo dos painéis. Etapa da capa.</summary>
        private sealed class Capa : IComplementoLajeAlveolar
        {
            public string Nome => "capa";
            public bool Ativo(ConfigPaginacao c) => c.Fase3.Capa && c.Capa > 0.001;
            public void Aplicar(ContextoLajeAlveolar ctx)
            {
                int mud = 0;
                foreach (var pano in ctx.Panos.Where(p => p.Erro == null))
                {
                    var fl = ctx.PisoDe(pano);
                    if (fl == null) continue;
                    var ft = InsercaoLajeAlveolar.TipoPisoUnico(ctx, fl.FloorType, "CAPA " + Fmt.Virgula(ctx.Cfg.Capa * 100, "0.#"), ctx.Cfg.Capa);
                    double topoAntes = M(fl.get_BoundingBox(null).Max.Z);
                    if (fl.GetTypeId() != ft.Id) { fl.ChangeTypeId(ft.Id); mud++; }
                    ctx.GravarEtapa(fl, ctx.Cfg.Etapas.Capa);
                    ctx.Doc.Regenerate();
                    double topoDepois = M(fl.get_BoundingBox(null).Max.Z);
                    if (Math.Abs(topoDepois - topoAntes) > 0.001)
                        ctx.Avisos.Add($"piso {pano.Piso.Id}: o topo mudou ao trocar para a capa ({Fmt.N(topoAntes, "0.000")} → {Fmt.N(topoDepois, "0.000")} m) — conferir");
                    foreach (var j in JoinGeometryUtils.GetJoinedElements(ctx.Doc, fl).ToList())
                    {
                        var o = ctx.Doc.GetElement(j);
                        if (o != null && ExtracaoLajeAlveolar.CriadoPeloComando(o)) try { JoinGeometryUtils.UnjoinGeometry(ctx.Doc, fl, o); } catch { }
                    }
                }
                ctx.Log.AppendLine($"capa: {mud} piso(s) trocados de tipo");
            }
        }

        // ------------------------------------------------------------------ chaves
        /// <summary>Uma DirectShape (Modelos genéricos) por trecho de junta: seção = vão entre as faces dos dois painéis, medida nos
        /// sólidos do modelo do topo até onde a junta fecha (entre ajustes serrados: altura toda). Etapa das chaves.</summary>
        private sealed class Chaves : IComplementoLajeAlveolar
        {
            public string Nome => "chaves";
            public bool Ativo(ConfigPaginacao c) => c.Fase3.Chaves;
            public void Aplicar(ContextoLajeAlveolar ctx)
            {
                int n = 0; double vol = 0;
                ElementId mat = ElementId.InvalidElementId;
                foreach (var pano in ctx.Panos.Where(p => p.Erro == null))
                {
                    var piso = ctx.PisoDe(pano);
                    if (mat == ElementId.InvalidElementId && piso != null) mat = MaterialDoPiso(piso.FloorType);
                    foreach (var j in pano.Juntas)
                    {
                        string marca = "CH-" + Resto(j.Marca, ctx.Cfg.Insercao.PrefixoJunta);
                        if (ctx.Existentes.Contains(marca)) continue;
                        var perfil = Perfil(ctx, pano, j);
                        if (perfil == null) { ctx.Avisos.Add($"{j.Marca}: perfil da junta não medido — chave não criada"); continue; }
                        try
                        {
                            var ini = P(j.Ini.X, j.Ini.Y, 0); var fim = P(j.Fim.X, j.Fim.Y, 0);
                            var dir = (fim - ini).Normalize(); double L = ini.DistanceTo(fim);
                            var loop = new CurveLoop();
                            var pts = perfil.Pontos;
                            for (int i = 0; i < pts.Count; i++)
                            {
                                var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                                var pa = new XYZ(ini.X, ini.Y, Ft(a.Z)) + perfil.N * Ft(a.T);
                                var pb = new XYZ(ini.X, ini.Y, Ft(b.Z)) + perfil.N * Ft(b.T);
                                if (pa.DistanceTo(pb) > 1e-4) loop.Append(Line.CreateBound(pa, pb));
                            }
                            var sol = GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, dir, L, new SolidOptions(mat, ElementId.InvalidElementId));
                            var ds = DirectShape.CreateElement(ctx.Doc, new ElementId(BuiltInCategory.OST_GenericModel));
                            ds.ApplicationId = AppId; ds.ApplicationDataId = marca;
                            ds.SetShape(new List<GeometryObject> { sol });
                            ds.SetName("Chave de cisalhamento");
                            ctx.Comum(ds, marca, $"chave {j.EntreA}/{j.EntreB}", ctx.Cfg.Etapas.ChavesEFaixasMacicas, piso);
                            ctx.PorMarca[marca] = ds.Id;
                            n++; vol += sol.Volume * Pe * Pe * Pe; ctx.Criados++;
                        }
                        catch (Exception ex) { ctx.Erros.Add($"{marca}: {ex.Message}"); }
                    }
                }
                ctx.Log.AppendLine($"chaves criadas: {n} (volume {Fmt.N(vol, "0.000")} m³)");
            }
        }

        // ------------------------------------------------------------------ tirantes
        /// <summary>Uma DirectShape por tirante: barra roscada, chapas em cima (sob a capa) e embaixo (sob os painéis), porcas. Barra que
        /// não cabe na parte estreita da junta: tratamento configurável (padrão: furo em obra na borda dos dois painéis, sem recortar a
        /// geometria; a informação vai no comentário do tirante e o relatório lista os furos por peça). Etapa: não gravada (pendente).</summary>
        private sealed class Tirantes : IComplementoLajeAlveolar
        {
            public string Nome => "suportes de instalações (tirantes)";
            public bool Ativo(ConfigPaginacao c) => c.SuportesInstalacoes;
            public void Aplicar(ContextoLajeAlveolar ctx)
            {
                var cfg = ctx.Cfg; var t = cfg.Tirante; var f3 = cfg.Fase3;
                ElementId mat = MaterialAco(ctx.Doc, f3.MaterialAco);
                int n = 0, pulados = 0;
                foreach (var pano in ctx.Panos.Where(p => p.Erro == null))
                {
                    var piso = ctx.PisoDe(pano);
                    foreach (var ts in pano.Tirantes)
                    {
                        string marca = "TS-" + Resto(ts.Marca, cfg.Insercao.PrefixoJunta).Replace("-T", "-");
                        var js = pano.Juntas.FirstOrDefault(x => x.Marca == ts.Junta);
                        if (js == null) continue;
                        var perfil = Perfil(ctx, pano, js);
                        var (inserir, furo, texto) = f3.AvaliarBarra(perfil?.AberturaMin ?? double.NaN, t.Diametro);
                        if (perfil == null) ctx.Avisos.Add($"{marca}: perfil da junta não medido — não deu para conferir se a barra cabe");
                        if (!inserir) { ctx.Avisos.Add($"{marca}: {texto}"); pulados++; continue; }
                        if (texto != null && !furo) ctx.Avisos.Add($"{marca}: {texto}");
                        if (furo) ctx.TirantesComFuro.Add((js.EntreA, js.EntreB));
                        if (ctx.Existentes.Contains(marca)) continue;
                        try
                        {
                            var dirXY = new XYZ(ts.DirJunta.X, ts.DirJunta.Y, 0).Normalize();
                            var nn = perfil?.N ?? new XYZ(-dirXY.Y, dirXY.X, 0);
                            var c = P(ts.X, ts.Y, 0) + nn * Ft(perfil?.TFecha ?? 0);   // barra no centro da parte estreita
                            double zs = pano.ZTopoPainel, zi = pano.ZBasePainel;
                            var so = new SolidOptions(mat, ElementId.InvalidElementId);
                            var geo = new List<GeometryObject>
                            {
                                Caixa(c, dirXY, nn, t.ChapaAoLongoJunta, t.ChapaTransversal, zs, zs + t.ChapaEspessura, so),
                                Caixa(c, dirXY, nn, t.ChapaAoLongoJunta, t.ChapaTransversal, zi - t.ChapaEspessura, zi, so),
                                Cilindro(c, t.Diametro / 2, zi - t.ChapaEspessura - f3.SobraBarraBaixo, zs + t.ChapaEspessura + f3.SobraBarraCima, so),
                            };
                            if (f3.PorcaDiametro > 0 && f3.PorcaAltura > 0)
                            {
                                geo.Add(Cilindro(c, f3.PorcaDiametro / 2, zs + t.ChapaEspessura, zs + t.ChapaEspessura + f3.PorcaAltura, so));
                                geo.Add(Cilindro(c, f3.PorcaDiametro / 2, zi - t.ChapaEspessura - f3.PorcaAltura, zi - t.ChapaEspessura, so));
                            }
                            var ds = DirectShape.CreateElement(ctx.Doc, new ElementId(BuiltInCategory.OST_GenericModel));
                            ds.ApplicationId = AppId; ds.ApplicationDataId = marca;
                            ds.SetShape(geo);
                            ds.SetName("Suporte de instalações - tirante na chave");
                            string desc = $"tirante Ø{Fmt.Virgula(t.Diametro * 1000, "0.0")} mm, chapas {Fmt.N(t.ChapaAoLongoJunta * 1000, "0")}x{Fmt.N(t.ChapaTransversal * 1000, "0")}x{Fmt.N(t.ChapaEspessura * 1000, "0")} mm, junta {ts.Junta}"
                                          + (furo ? "; " + texto : "");
                            ctx.Comum(ds, marca, desc, null, piso);
                            ctx.PorMarca[marca] = ds.Id;
                            n++; ctx.Criados++;
                        }
                        catch (Exception ex) { ctx.Erros.Add($"{marca}: {ex.Message}"); }
                    }
                }
                ctx.Log.AppendLine($"tirantes criados: {n}" + (pulados > 0 ? $"; não inseridos (barra não cabe): {pulados}" : ""));
            }
        }

        // ------------------------------------------------------------------ utilidades

        private static string Resto(string marca, string prefixo)
            => !string.IsNullOrEmpty(prefixo) && marca.StartsWith(prefixo, StringComparison.Ordinal) ? marca.Substring(prefixo.Length) : marca;

        private static ElementId MaterialDoPiso(FloorType ft)
        {
            var cs = ft?.GetCompoundStructure();
            if (cs == null || cs.LayerCount == 0) return ElementId.InvalidElementId;
            int i = cs.StructuralMaterialIndex >= 0 && cs.StructuralMaterialIndex < cs.LayerCount ? cs.StructuralMaterialIndex : 0;
            return cs.GetLayers()[i].MaterialId;
        }

        private static ElementId MaterialAco(Document doc, string nome)
        {
            var mats = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().ToList();
            Material m = !string.IsNullOrWhiteSpace(nome) ? mats.FirstOrDefault(x => x.Name == nome) : null;
            m ??= mats.FirstOrDefault(x => x.Name.StartsWith("Aço", StringComparison.OrdinalIgnoreCase) || x.Name.StartsWith("Aco", StringComparison.OrdinalIgnoreCase))
               ?? mats.FirstOrDefault(x => x.Name.IndexOf("steel", StringComparison.OrdinalIgnoreCase) >= 0)
               ?? mats.FirstOrDefault(x => string.Equals(x.MaterialClass, "Metal", StringComparison.OrdinalIgnoreCase));
            return m?.Id ?? ElementId.InvalidElementId;
        }

        /// <summary>Perfil da junta medido nos sólidos dos dois painéis vizinhos, no meio do trecho (com cache no contexto).</summary>
        private static PerfilJunta Perfil(ContextoLajeAlveolar ctx, PanoPaginado pano, JuntaSaida j)
        {
            if (ctx.Perfis.TryGetValue(j.Marca, out var cache)) return cache;
            PerfilJunta r = null;
            try { r = Medir(ctx, pano, j); }
            catch (Exception ex) { ctx.Erros.Add($"{j.Marca}: perfil da junta: {ex.Message}"); }
            ctx.Perfis[j.Marca] = r;
            return r;
        }

        private static PerfilJunta Medir(ContextoLajeAlveolar ctx, PanoPaginado pano, JuntaSaida j)
        {
            if (!ctx.PorMarca.TryGetValue(j.EntreA, out var ia) || !ctx.PorMarca.TryGetValue(j.EntreB, out var ib)) return null;
            var ea = ctx.Doc.GetElement(ia); var eb = ctx.Doc.GetElement(ib);
            if (ea == null || eb == null) return null;
            double x = (j.Ini.X + j.Fim.X) / 2, y = (j.Ini.Y + j.Fim.Y) / 2;
            var dir = (P(j.Fim.X, j.Fim.Y, 0) - P(j.Ini.X, j.Ini.Y, 0)).Normalize();
            var n = new XYZ(-dir.Y, dir.X, 0);
            var ca = Centro(ea); var cb = Centro(eb);
            if (ca != null && cb != null && (cb - ca).DotProduct(n) < 0) n = -n;
            var sa = Solidos(ea, out var da); var sb = Solidos(eb, out var db);
            double zTopo = pano.ZTopoPainel, zBase = pano.ZBasePainel, passo = ctx.Cfg.Fase3.PassoPerfilChave;
            var linhas = new List<(double Z, double A, double B)>();
            for (double z = zTopo - 0.0005; z > zBase + 0.002; z -= passo)
            {
                var o = P(x, y, z);
                double a = double.NaN, b = double.NaN;
                foreach (var (t0, t1) in TrechosDentro(sa, o, n, Ft(0.3)))
                    foreach (var t in new[] { M(t0), M(t1) }) if (t <= 0.02 && (double.IsNaN(a) || t > a)) a = t;
                foreach (var (t0, t1) in TrechosDentro(sb, o, n, Ft(0.3)))
                    foreach (var t in new[] { M(t0), M(t1) }) if (t >= -0.02 && (double.IsNaN(b) || t < b)) b = t;
                if (double.IsNaN(a) || double.IsNaN(b) || b - a < 0.0005) continue;
                linhas.Add((z, a, b));
            }
            GC.KeepAlive(da); GC.KeepAlive(db);
            if (linhas.Count < 3) return null;
            // fecha: menor abertura (a mais alta em caso de empate)
            var fecha = linhas.Aggregate((m, l) => (l.B - l.A) < (m.B - m.A) - 1e-5 ? l : m);
            var usar = linhas.Where(l => l.Z >= fecha.Z - 1e-9).ToList();
            // duas faces serradas (ajustes lado a lado): abertura constante, a junta não fecha → chave na altura toda
            if (linhas.Max(l => l.B - l.A) - linhas.Min(l => l.B - l.A) < 0.001 || usar.Count < 3) { usar = linhas; fecha = linhas[linhas.Count - 1]; }
            var pts = new List<(double T, double Z)> { (usar[0].A, zTopo) };
            foreach (var l in usar) pts.Add((l.A, l.Z));
            foreach (var l in Enumerable.Reverse(usar)) pts.Add((l.B, l.Z));
            pts.Add((usar[0].B, zTopo));
            var limpo = new List<(double T, double Z)>();
            foreach (var q in pts)
            {
                // pontos a menos de 1,5 mm do anterior geram aresta menor que a tolerância do Revit
                if (limpo.Count >= 1 && Math.Abs(q.T - limpo[limpo.Count - 1].T) < 0.0015 && Math.Abs(q.Z - limpo[limpo.Count - 1].Z) < 0.0015) continue;
                if (limpo.Count >= 2)
                {
                    var p1 = limpo[limpo.Count - 2]; var p2 = limpo[limpo.Count - 1];
                    double cr = (p2.T - p1.T) * (q.Z - p1.Z) - (p2.Z - p1.Z) * (q.T - p1.T);
                    if (Math.Abs(cr) < 1e-8) limpo.RemoveAt(limpo.Count - 1);
                }
                limpo.Add(q);
            }
            while (limpo.Count > 3 && Math.Abs(limpo[limpo.Count - 1].T - limpo[0].T) < 0.0015 && Math.Abs(limpo[limpo.Count - 1].Z - limpo[0].Z) < 0.0015)
                limpo.RemoveAt(limpo.Count - 1);
            return new PerfilJunta { Pontos = limpo, TFecha = (fecha.A + fecha.B) / 2, ZFecha = fecha.Z, AberturaMin = linhas.Min(l => l.B - l.A), N = n };
        }

        private static XYZ Centro(Element e)
        {
            if (e.Location is LocationCurve lc) return (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) / 2;
            var bb = e.get_BoundingBox(null);
            return bb == null ? null : (bb.Min + bb.Max) / 2;
        }

        private static Solid Caixa(XYZ c, XYZ dir, XYZ n, double l, double w, double z0, double z1, SolidOptions so)
        {
            var b = new XYZ(c.X, c.Y, Ft(z0));
            var p = new[] { b - dir * Ft(l / 2) - n * Ft(w / 2), b + dir * Ft(l / 2) - n * Ft(w / 2), b + dir * Ft(l / 2) + n * Ft(w / 2), b - dir * Ft(l / 2) + n * Ft(w / 2) };
            var loop = new CurveLoop();
            for (int i = 0; i < 4; i++) loop.Append(Line.CreateBound(p[i], p[(i + 1) % 4]));
            return GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, Ft(z1 - z0), so);
        }

        private static Solid Cilindro(XYZ c, double r, double z0, double z1, SolidOptions so)
        {
            var b = new XYZ(c.X, c.Y, Ft(z0));
            var loop = new CurveLoop();
            loop.Append(Arc.Create(b, Ft(r), 0, Math.PI, XYZ.BasisX, XYZ.BasisY));
            loop.Append(Arc.Create(b, Ft(r), Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY));
            return GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, Ft(z1 - z0), so);
        }
    }
}
