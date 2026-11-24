using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.DB.Structure;
using DEB.Core.Analise.Barras;
using DEB.Core.Analise.Objetos;

namespace DEB.Revit.Analise
{
    /// <summary>
    /// Cargas da análise no Revit (D50, item 2): as lançadas na janela do PCEB ficam no .rvt (Extensible Storage, JSON); as cargas
    /// nativas do Revit (pontual, linear, de área) são lidas a cada montagem e ligadas à peça mais próxima (viga, pilar, laje). A
    /// natureza do caso de carga do Revit decide o caso: permanente/morta → G; as demais → Q.
    /// </summary>
    internal static class CargasRevit
    {
        // GUID fixo: gravado nos modelos (nunca mudar)
        private static readonly Guid SchemaId = new Guid("c4a1e7d2-6f3b-4b8e-9a52-0d8e3f61b2a7");
        private const string Campo = "Json";
        private const double Cm = 30.48;
        private static Vetor3 V(XYZ p) => new Vetor3(p.X * Cm, p.Y * Cm, p.Z * Cm);

        private static Schema GetSchema(bool criar)
        {
            Schema s = Schema.Lookup(SchemaId);
            if (s != null || !criar) return s;
            var b = new SchemaBuilder(SchemaId);
            b.SetSchemaName("PCEB_CargasAnalise");
            b.SetDocumentation("PCEB: cargas lançadas na janela de análise (D50) em JSON.");
            b.SetReadAccessLevel(AccessLevel.Public);
            b.SetWriteAccessLevel(AccessLevel.Public);
            b.AddSimpleField(Campo, typeof(string));
            return b.Finish();
        }

        public static List<CargaObjeto> LerPceb(Document doc, List<string> notas = null)
        {
            try
            {
                Schema s = GetSchema(false);
                Entity ent = s == null ? null : doc.ProjectInformation?.GetEntity(s);
                return CargasObjetos.DeJson(ent != null && ent.IsValid() ? ent.Get<string>(Campo) : null);
            }
            catch (Exception ex) { notas?.Add("Cargas do PCEB não lidas: " + ex.Message); return new List<CargaObjeto>(); }
        }

        public static void GravarPceb(Document doc, IEnumerable<CargaObjeto> cargas)
        {
            using (var t = new Transaction(doc, "PCEB: cargas da análise"))
            {
                t.Start();
                var ent = new Entity(GetSchema(true));
                ent.Set(Campo, CargasObjetos.ParaJson(cargas));
                doc.ProjectInformation.SetEntity(ent);
                t.Commit();
            }
        }

        private static string Caso(LoadBase l)
        {
            string n = (l.LoadNatureName ?? "") + " " + (l.LoadCaseName ?? "");
            return new[] { "Dead", "Mort", "Perm", "Própri", "Propri" }.Any(x => n.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0) ? EstruturaObjetos.CasoG : EstruturaObjetos.CasoQ;
        }

        /// <summary>Lê as cargas nativas do Revit e liga cada uma a um objeto da estrutura (as que não acham peça vão para as notas).</summary>
        public static List<CargaObjeto> LerNativas(Document doc, EstruturaObjetos e, List<string> notas)
        {
            var r = new List<CargaObjeto>();
            int soltas = 0, horiz = 0;
            // eixos das vigas (concreto e aço): (chave, ponto I, ponto J)
            var vigas = e.Vigas.Select(v => (Chave: AjustesAnalise.Chave(v.Dono), I: v.ApoioI + Vetor3.EixoZ * v.AlturaEixo, J: v.ApoioJ + Vetor3.EixoZ * v.AlturaEixo, H: v.Secao.H))
                .Concat(e.Metalicas.Where(b => !b.Pilar).Select(b => (Chave: AjustesAnalise.Chave(b.Dono), I: b.PontoI + b.DeslocamentoFisico, J: b.PontoJ + b.DeslocamentoFisico, H: b.Perfil.D / 10)))
                .ToList();
            var pilares = e.Pilares.Select(p => (Chave: AjustesAnalise.Chave(p.Dono), Base: p.Base, Topo: p.Ponto(p.Comprimento), R: Math.Max(p.Secao.B, p.Secao.H)))
                .Concat(e.Metalicas.Where(b => b.Pilar).Select(b => (Chave: AjustesAnalise.Chave(b.Dono), Base: b.PontoI, Topo: b.PontoJ, R: Math.Max(b.Perfil.D, b.Perfil.Bf) / 10)))
                .ToList();
            (string Chave, double T, double L, double D)? Viga(Vetor3 p)
            {
                (string, double, double, double)? melhor = null;
                foreach (var v in vigas)
                {
                    Vetor3 d = v.J - v.I; double L = d.Modulo; if (L < 1) continue;
                    Vetor3 u = d * (1 / L); double t = (p - v.I).Ponto(u);
                    if (t < -5 || t > L + 5) continue;
                    Vetor3 pe = v.I + u * t, w = p - pe;
                    double dh = Math.Sqrt(w.X * w.X + w.Y * w.Y), dz = Math.Abs(w.Z);
                    if (dh > 50 || dz > v.H + 100) continue;
                    double dist = dh + dz * 0.1;
                    if (melhor == null || dist < melhor.Value.Item4) melhor = (v.Chave, Math.Max(0, Math.Min(L, t)), L, dist);
                }
                return melhor;
            }

            foreach (LineLoad ll in new FilteredElementCollector(doc).OfClass(typeof(LineLoad)).Cast<LineLoad>())
            {
                Vetor3 a = V(ll.StartPoint), b = V(ll.EndPoint), meio = (a + b) * 0.5;
                var vg = Viga(meio);
                if (vg == null) { soltas++; continue; }
                var va = Viga(a); var vb = Viga(b);
                double t0 = Math.Min(va?.T ?? 0, vb?.T ?? vg.Value.L), t1 = Math.Max(va?.T ?? 0, vb?.T ?? vg.Value.L);
                double f1 = UnitUtils.ConvertFromInternalUnits(ll.ForceVector1.Z, UnitTypeId.KilonewtonsPerMeter);
                double f2 = UnitUtils.ConvertFromInternalUnits(ll.ForceVector2.Z, UnitTypeId.KilonewtonsPerMeter);
                if (Math.Abs(ll.ForceVector1.X) + Math.Abs(ll.ForceVector1.Y) > 1e-9) horiz++;
                bool inteira = t0 < 5 && t1 > vg.Value.L - 5;
                r.Add(new CargaObjeto
                {
                    Alvo = vg.Value.Chave, Tipo = TipoCargaObjeto.Linear, Caso = Caso(ll), Valor = -(f1 + f2) / 2, Posicao = inteira ? 0 : t0, Fim = inteira ? 0 : t1,
                    Origem = OrigemCarga.Revit, IdRevit = ll.Id.Value.ToString(),
                    Descricao = $"Revit: carga linear {ll.Id.Value} ({ll.LoadCaseName})" + (Math.Abs(f1 - f2) > 1e-6 ? " — trapezoidal: usada a média" : ""),
                });
            }
            foreach (PointLoad pl in new FilteredElementCollector(doc).OfClass(typeof(PointLoad)).Cast<PointLoad>())
            {
                Vetor3 p = V(pl.Point);
                double fz = UnitUtils.ConvertFromInternalUnits(pl.ForceVector.Z, UnitTypeId.Kilonewtons);
                double fx = UnitUtils.ConvertFromInternalUnits(pl.ForceVector.X, UnitTypeId.Kilonewtons), fy = UnitUtils.ConvertFromInternalUnits(pl.ForceVector.Y, UnitTypeId.Kilonewtons);
                string alvo = null; double pos = 0;
                foreach (var c in pilares)
                {
                    double dx = p.X - c.Base.X, dy = p.Y - c.Base.Y;
                    if (Math.Sqrt(dx * dx + dy * dy) <= c.R / 2 + 10 && p.Z >= c.Base.Z - 5 && p.Z <= c.Topo.Z + 5) { alvo = c.Chave; pos = p.Z - c.Base.Z; break; }
                }
                if (alvo == null && Viga(p) is var vg && vg != null) { alvo = vg.Value.Chave; pos = vg.Value.T; }
                if (alvo == null) { soltas++; continue; }
                void Add(string dir, double v) =>
                    r.Add(new CargaObjeto { Alvo = alvo, Tipo = TipoCargaObjeto.Pontual, Caso = Caso(pl), Valor = v, Posicao = pos, Direcao = dir, Origem = OrigemCarga.Revit,
                        IdRevit = pl.Id.Value.ToString(), Descricao = $"Revit: carga pontual {pl.Id.Value} ({pl.LoadCaseName})" });
                if (Math.Abs(fz) > 1e-9) Add("Z", -fz);
                if (Math.Abs(fx) > 1e-9) Add("X", fx);
                if (Math.Abs(fy) > 1e-9) Add("Y", fy);
            }
            foreach (AreaLoad al in new FilteredElementCollector(doc).OfClass(typeof(AreaLoad)).Cast<AreaLoad>())
            {
                BoundingBoxXYZ bb = al.get_BoundingBox(null);
                if (bb == null) { soltas++; continue; }
                Vetor3 c = V((bb.Min + bb.Max) / 2);
                var laje = e.Lajes.FirstOrDefault(l => DentroDaLaje(l, c));
                if (laje == null) { soltas++; continue; }
                double fz = UnitUtils.ConvertFromInternalUnits(al.ForceVector1.Z, UnitTypeId.KilonewtonsPerSquareMeter);
                r.Add(new CargaObjeto { Alvo = AjustesAnalise.Chave(laje.Dono), Tipo = TipoCargaObjeto.Superficie, Caso = Caso(al), Valor = -fz, Origem = OrigemCarga.Revit,
                    IdRevit = al.Id.Value.ToString(), Descricao = $"Revit: carga de área {al.Id.Value} ({al.LoadCaseName}) — aplicada na laje inteira" });
            }
            if (r.Count > 0) notas.Add($"{r.Count} cargas nativas do Revit lidas para a análise (permanente/morta → G; demais → Q).");
            if (soltas > 0) notas.Add($"{soltas} cargas do Revit sem peça perto (viga, pilar ou laje) ficaram fora da análise.");
            if (horiz > 0) notas.Add($"{horiz} cargas lineares do Revit com componente horizontal: só a vertical foi considerada.");
            return r;
        }

        private static bool DentroDaLaje(LajeObjeto l, Vetor3 p)
        {
            var c = l.Contorno;
            if (c == null || c.Count < 3) return false;
            double zMed = c.Average(x => x.Z);
            if (Math.Abs(p.Z - zMed) > 100) return false;
            bool dentro = false;
            for (int i = 0, j = c.Count - 1; i < c.Count; j = i++)
                if ((c[i].Y > p.Y) != (c[j].Y > p.Y) && p.X < (c[j].X - c[i].X) * (p.Y - c[i].Y) / (c[j].Y - c[i].Y) + c[i].X) dentro = !dentro;
            return dentro;
        }
    }
}
