using System;
using System.Globalization;

// Parte pura do sistema local do pilar pré-moldado do DEB.Revit (etapa 3 da organização, 16/11/2026): saiu de
// DEB.Revit/src/Precast/PrecastColumn.cs sem mudar o cálculo. O que lê o Revit (origem e eixos do pilar em XYZ, a
// instância da família do consolo, o levantamento) continua no plugin: ColumnFrame herda de ColumnBox e RevitCorbel de Corbel.
// Unidades: pés (unidade interna do Revit), como antes.
namespace DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D
{
    /// <summary>Medidas do pilar no sistema local: origem no canto mínimo, Bx/By = seção, H = comprimento (pés).</summary>
    public class ColumnBox
    {
        public double Bx { get; }
        public double By { get; }
        public double H { get; }

        public ColumnBox(double bx, double by, double h)
        {
            Bx = bx; By = by; H = h;
        }
    }

    /// <summary>Ponto ou direção em coordenadas locais do pilar (pés).</summary>
    public readonly struct Loc
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

    public enum ColumnFace { PX, NX, PY, NY }

    /// <summary>
    /// Consolo descrito no sistema local do pilar.
    /// d = distância para fora da face (0 na face), s = coordenada ao longo da face (local A ou B), c = altura.
    /// </summary>
    public class Corbel
    {
        public ColumnFace Face { get; set; }
        public double S0 { get; set; }          // início ao longo da face
        public double S1 { get; set; }          // fim ao longo da face
        public double ZBottom { get; set; }     // base do consolo junto à face
        public double ZTop { get; set; }        // topo do consolo
        public double Projection { get; set; }  // balanço (c)
        public double TipHeight { get; set; }   // altura na ponta (h)
        /// <summary>Aparelho de apoio da viga sobre o consolo (PCEB_Apoio*, D36); null = a família não informa.</summary>
        public DEB.Core.Intercambio.ApoioPeca Apoio { get; set; }
        /// <summary>Reação da viga apoiada no consolo (D44, etapa 1); null = nenhuma viga calculada apoia nele.</summary>
        public DEB.Core.Elementos.Consolo.CargaConsolo Carga { get; set; }

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
        public double ColumnDepth(ColumnBox f) => Face == ColumnFace.PX || Face == ColumnFace.NX ? f.Bx : f.By;

        /// <summary>Ponto local a partir de (d, s, c).</summary>
        public Loc At(ColumnBox f, double d, double s, double c)
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

    /// <summary>
    /// Conversões pés ↔ mm (evita conflito de nome com a propriedade U do consolo). Antes chamava UnitUtils do Revit; o fator
    /// é o mesmo (1 pé = 304,8 mm exatos).
    /// </summary>
    public static class U_
    {
        public const double MmPerFoot = 304.8;
        public static double ToMm(double ft) => ft * MmPerFoot;
        public static double Mm(double mm) => mm / MmPerFoot;
    }
}
