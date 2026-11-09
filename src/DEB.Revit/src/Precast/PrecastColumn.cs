using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using DEB.Revit.Core;

namespace DEB.Revit.Precast
{
    /// <summary>
    /// Sistema local de um pilar: origem no canto mínimo, X/Y = seção, Z = eixo.
    /// Coordenadas locais (a, b, c) em unidades internas (pés).
    /// </summary>
    internal sealed class ColumnFrame
    {
        public XYZ O { get; }
        public XYZ X { get; }
        public XYZ Y { get; }
        public XYZ Z { get; }
        public double Bx { get; }
        public double By { get; }
        public double H { get; }

        public ColumnFrame(XYZ o, XYZ x, XYZ y, XYZ z, double bx, double by, double h)
        {
            O = o; X = x; Y = y; Z = z; Bx = bx; By = by; H = h;
        }

        public static ColumnFrame From(PieceInfo p) =>
            new ColumnFrame(p.Origin, p.AxisX, p.AxisY, p.AxisZ, p.LengthX, p.LengthY, p.LengthZ);

        public XYZ ToWorld(double a, double b, double c) => O + X * a + Y * b + Z * c;
        public XYZ ToWorld(Loc p) => ToWorld(p.A, p.B, p.C);
        public XYZ DirToWorld(Loc d) => (X * d.A + Y * d.B + Z * d.C).Normalize();

        public Loc ToLocal(XYZ p)
        {
            XYZ v = p - O;
            return new Loc(v.DotProduct(X), v.DotProduct(Y), v.DotProduct(Z));
        }

        /// <summary>Gira o sistema 90° em torno de Z (k vezes), mantendo a origem no canto mínimo.</summary>
        public ColumnFrame Rotate(int k)
        {
            ColumnFrame f = this;
            for (int i = 0; i < ((k % 4) + 4) % 4; i++)
                f = new ColumnFrame(f.O + f.X * f.Bx, f.Y, f.X.Negate(), f.Z, f.By, f.Bx, f.H);
            return f;
        }

        public PieceInfo ToPieceInfo(PieceInfo source, string mark) => new PieceInfo
        {
            Instance = source.Instance,
            Kind = PieceKind.Column,
            Mark = mark,
            AxisX = X,
            AxisY = Y,
            AxisZ = Z,
            Origin = O,
            LengthX = Bx,
            LengthY = By,
            LengthZ = H,
            Vertices = source.Vertices
        };
    }

    /// <summary>Ponto ou direção em coordenadas locais do pilar (pés).</summary>
    internal readonly struct Loc
    {
        public readonly double A, B, C;
        public Loc(double a, double b, double c) { A = a; B = b; C = c; }
        public static Loc operator +(Loc p, Loc q) => new Loc(p.A + q.A, p.B + q.B, p.C + q.C);
        public static Loc operator -(Loc p, Loc q) => new Loc(p.A - q.A, p.B - q.B, p.C - q.C);
        public static Loc operator *(Loc p, double s) => new Loc(p.A * s, p.B * s, p.C * s);
        public static Loc operator -(Loc p) => new Loc(-p.A, -p.B, -p.C);
        public Loc Cross(Loc o) => new Loc(B * o.C - C * o.B, C * o.A - A * o.C, A * o.B - B * o.A);
        public double Dot(Loc o) => A * o.A + B * o.B + C * o.C;
        public static readonly Loc UnitA = new Loc(1, 0, 0), UnitB = new Loc(0, 1, 0), UnitC = new Loc(0, 0, 1);
    }

    internal enum ColumnFace { PX, NX, PY, NY }

    /// <summary>
    /// Consolo descrito no sistema local do pilar.
    /// d = distância para fora da face (0 na face), s = coordenada ao longo da face (local A ou B), c = altura.
    /// </summary>
    internal sealed class Corbel
    {
        public FamilyInstance Instance { get; set; }
        public ColumnFace Face { get; set; }
        public double S0 { get; set; }          // início ao longo da face
        public double S1 { get; set; }          // fim ao longo da face
        public double ZBottom { get; set; }     // base do consolo junto à face
        public double ZTop { get; set; }        // topo do consolo
        public double Projection { get; set; }  // balanço (c)
        public double TipHeight { get; set; }   // altura na ponta (h)

        public double Width => S1 - S0;
        public double HeightAtFace => ZTop - ZBottom;

        /// <summary>Direção para fora da face, em coordenadas locais.</summary>
        public Loc U => Face switch
        {
            ColumnFace.PX => Loc.UnitA,
            ColumnFace.NX => -Loc.UnitA,
            ColumnFace.PY => Loc.UnitB,
            _ => -Loc.UnitB
        };

        /// <summary>Direção ao longo da face (eixo de S).</summary>
        public Loc T => Face == ColumnFace.PX || Face == ColumnFace.NX ? Loc.UnitB : Loc.UnitA;

        /// <summary>Profundidade do pilar na direção U (lado da seção perpendicular à face).</summary>
        public double ColumnDepth(ColumnFrame f) => Face == ColumnFace.PX || Face == ColumnFace.NX ? f.Bx : f.By;

        /// <summary>Ponto local a partir de (d, s, c).</summary>
        public Loc At(ColumnFrame f, double d, double s, double c)
        {
            Loc basePt = Face switch
            {
                ColumnFace.PX => new Loc(f.Bx, 0, 0),
                ColumnFace.PY => new Loc(0, f.By, 0),
                _ => new Loc(0, 0, 0)
            };
            return basePt + U * d + T * s + new Loc(0, 0, c);
        }

        public string Signature() => string.Format(CultureInfo.InvariantCulture,
            "{0}:{1:0}-{2:0}:{3:0}-{4:0}:c{5:0}:h{6:0}",
            Face, Mm(S0), Mm(S1), Mm(ZBottom), Mm(ZTop), Mm(Projection), Mm(TipHeight));

        private static double Mm(double ft) => Math.Round(U_.ToMm(ft));
    }

    /// <summary>Atalho para conversões (evita conflito de nome com a propriedade U do consolo).</summary>
    internal static class U_
    {
        public static double ToMm(double ft) => Core.U.ToMm(ft);
        public static double Mm(double mm) => Core.U.Mm(mm);
    }

    /// <summary>Pilar pré-moldado com seus consolos, já no sistema canônico do grupo.</summary>
    internal sealed class PrecastColumn
    {
        public FamilyInstance Instance { get; set; }
        public PieceInfo Info { get; set; }          // análise original (sistema da família)
        public ColumnFrame Frame { get; set; }       // sistema canônico (girado para casar com o grupo)
        public int Rotation { get; set; }            // giros de 90° aplicados
        public List<Corbel> Corbels { get; set; } = new List<Corbel>();
        public double EmbedmentMm { get; set; }      // parâmetro "Embutimento", se existir
        public string Signature { get; set; }
        public string CurrentMark { get; set; }

        public string SectionLabel => $"{U_.ToMm(Frame.Bx):0}×{U_.ToMm(Frame.By):0}";
        public string Id => Instance.Id.Value.ToString();
    }

    internal sealed class ColumnGroup
    {
        public string Mark { get; set; }
        public List<PrecastColumn> Members { get; } = new List<PrecastColumn>();
        public PrecastColumn Representative => Members[0];
        public ColumnRebarSpec Spec { get; set; }
        public string Signature => Representative.Signature;
    }

    /// <summary>Levantamento dos pilares pré-moldados, associação dos consolos e agrupamento de similares.</summary>
    internal static class PrecastSurvey
    {
        public static List<ColumnGroup> Run(Document doc, out List<string> notes)
        {
            notes = new List<string>();

            List<FamilyInstance> columns = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType().OfType<FamilyInstance>()
                .Where(fi => fi.SuperComponent == null)
                .ToList();

            List<FamilyInstance> precast = columns.Where(fi => fi.StructuralMaterialType == Autodesk.Revit.DB.Structure.StructuralMaterialType.PrecastConcrete).ToList();
            if (precast.Count == 0 && columns.Count > 0)
            {
                notes.Add("Nenhum pilar com material \"Concreto pré-moldado\": todos os pilares estruturais foram considerados.");
                precast = columns;
            }

            // Candidatos a consolo: subcomponentes aninhados (ex.: consolos da família da viga) e
            // conexões estruturais / modelos genéricos soltos.
            List<FamilyInstance> corbelCandidates = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Where(fi => fi.Category != null &&
                             (fi.Category.BuiltInCategory == BuiltInCategory.OST_StructConnections ||
                              fi.Category.BuiltInCategory == BuiltInCategory.OST_GenericModel ||
                              (fi.SuperComponent != null && fi.Category.BuiltInCategory != BuiltInCategory.OST_StructuralColumns)))
                .ToList();

            var pieces = new List<PrecastColumn>();
            var used = new HashSet<ElementId>();
            foreach (FamilyInstance fi in precast)
            {
                PieceInfo info;
                try { info = PieceAnalyzer.Analyze(fi); }
                catch (PieceNotSupportedException ex) { notes.Add($"Pilar {fi.Id.Value} ignorado: {ex.Message}"); continue; }

                var pc = new PrecastColumn
                {
                    Instance = fi,
                    Info = info,
                    EmbedmentMm = ReadLengthMm(fi, "Embutimento"),
                    CurrentMark = fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()
                };

                // Vértices dos consolos que encostam no pilar (guardados para descrevê-los em cada giro).
                ColumnFrame f0 = ColumnFrame.From(info);
                var corbelVerts = new List<(FamilyInstance Fi, List<XYZ> Verts)>();
                foreach (FamilyInstance k in corbelCandidates)
                {
                    if (used.Contains(k.Id)) continue;
                    List<XYZ> verts = GeometryHelper.GetVertices(GeometryHelper.GetSolids(k, null));
                    if (verts.Count == 0) continue;
                    if (Describe(f0, k, verts) == null) continue;
                    corbelVerts.Add((k, verts));
                    used.Add(k.Id);
                }

                // Sistema canônico: o giro que dá a menor assinatura.
                int[] rotations = Math.Abs(info.LengthX - info.LengthY) < U_.Mm(1) ? new[] { 0, 1, 2, 3 } : new[] { 0, 2 };
                string best = null;
                foreach (int k in rotations)
                {
                    ColumnFrame fk = f0.Rotate(k);
                    List<Corbel> ck = corbelVerts.Select(cv => Describe(fk, cv.Fi, cv.Verts)).Where(c => c != null)
                        .OrderBy(c => c.Signature(), StringComparer.Ordinal).ToList();
                    string sig = Signature(fk, pc.EmbedmentMm, ck);
                    if (best == null || string.CompareOrdinal(sig, best) < 0)
                    {
                        best = sig;
                        pc.Frame = fk;
                        pc.Rotation = k;
                        pc.Corbels = ck;
                    }
                }
                pc.Signature = best;
                pieces.Add(pc);
            }

            // Agrupamento: mesma assinatura = mesma peça. Ordena por seção, comprimento e nº de consolos.
            var groups = pieces.GroupBy(p => p.Signature)
                .Select(g =>
                {
                    var cg = new ColumnGroup();
                    cg.Members.AddRange(g.OrderBy(p => p.Instance.Id.Value));
                    return cg;
                })
                .OrderBy(g => g.Representative.Frame.Bx * g.Representative.Frame.By)
                .ThenBy(g => g.Representative.Frame.H)
                .ThenBy(g => g.Representative.Corbels.Count)
                .ToList();

            int n = 1;
            foreach (ColumnGroup g in groups)
            {
                g.Mark = "P" + n++;
                g.Spec = ColumnRebarSpec.Default(g.Representative);
            }
            return groups;
        }

        /// <summary>Descreve um elemento como consolo de uma face do pilar; null se não encostar em nenhuma face.</summary>
        private static Corbel Describe(ColumnFrame f, FamilyInstance fi, List<XYZ> verts)
        {
            double tol = U_.Mm(5);
            List<Loc> l = verts.Select(f.ToLocal).ToList();
            double aMin = l.Min(p => p.A), aMax = l.Max(p => p.A);
            double bMin = l.Min(p => p.B), bMax = l.Max(p => p.B);
            double cMin = l.Min(p => p.C), cMax = l.Max(p => p.C);

            // Precisa estar dentro da altura do pilar.
            if (cMax < -tol || cMin > f.H + tol) return null;

            ColumnFace face;
            if (Math.Abs(aMin - f.Bx) <= tol && bMin >= -tol && bMax <= f.By + tol) face = ColumnFace.PX;
            else if (Math.Abs(aMax) <= tol && bMin >= -tol && bMax <= f.By + tol) face = ColumnFace.NX;
            else if (Math.Abs(bMin - f.By) <= tol && aMin >= -tol && aMax <= f.Bx + tol) face = ColumnFace.PY;
            else if (Math.Abs(bMax) <= tol && aMin >= -tol && aMax <= f.Bx + tol) face = ColumnFace.NY;
            else return null;

            // d = distância para fora da face; s = coordenada ao longo da face.
            Func<Loc, double> d, s;
            switch (face)
            {
                case ColumnFace.PX: d = p => p.A - f.Bx; s = p => p.B; break;
                case ColumnFace.NX: d = p => -p.A; s = p => p.B; break;
                case ColumnFace.PY: d = p => p.B - f.By; s = p => p.A; break;
                default: d = p => -p.B; s = p => p.A; break;
            }

            double proj = l.Max(d);
            if (proj < U_.Mm(20)) return null;   // só encosta, não é saliência

            List<Loc> atFace = l.Where(p => d(p) <= tol).ToList();
            // altura na ponta pelas retas principais do perfil: um chanfro na ponta não torna o consolo trapezoidal
            // (antes: vértices da ponta → 23,5 com chanfro de 2 virava "ponta 21,5"; relato do usuário, 09/11/2026)
            var med = DEB.Core.Pecas.PerfilConsolo.Medir(l.Select(p => (U_.ToMm(d(p)) / 10, U_.ToMm(p.C) / 10)));

            return new Corbel
            {
                Instance = fi,
                Face = face,
                S0 = l.Min(s),
                S1 = l.Max(s),
                ZBottom = atFace.Count > 0 ? atFace.Min(p => p.C) : cMin,
                ZTop = cMax,
                Projection = proj,
                TipHeight = U_.Mm(med.AlturaPonta * 10)
            };
        }

        private static string Signature(ColumnFrame f, double embedmentMm, List<Corbel> corbels) =>
            string.Format(CultureInfo.InvariantCulture, "{0:0}x{1:0}x{2:0}|e{3:0}|{4}",
                Math.Round(U_.ToMm(f.Bx)), Math.Round(U_.ToMm(f.By)), Math.Round(U_.ToMm(f.H)), embedmentMm,
                string.Join(";", corbels.Select(c => c.Signature())));

        private static double ReadLengthMm(Element e, string name)
        {
            Parameter p = e.LookupParameter(name);
            if (p == null || p.StorageType != StorageType.Double) return 0;
            return Math.Round(U_.ToMm(p.AsDouble()));
        }
    }
}
