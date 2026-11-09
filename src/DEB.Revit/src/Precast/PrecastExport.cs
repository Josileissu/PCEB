using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using DEB.Core.Intercambio;

namespace DEB.Revit.Precast
{
    /// <summary>
    /// Levantamento dos pilares pré-moldados → arquivo de troca do DEB (<see cref="ArquivoPecas"/>), para detalhar no CAD
    /// (ARMIMPORTARPECAS). Usa o mesmo levantamento e os mesmos grupos de peças iguais (ou giradas) do botão
    /// "Pilares pré-moldados"; não altera o modelo.
    /// Faces: o sistema canônico do grupo tem a seção Bx (x) × By (y), como o template do CAD (b em x, h em y), e
    /// face 1 = +x, 2 = −y, 3 = −x, 4 = +y.
    /// </summary>
    internal static class PrecastExport
    {
        public static ArquivoPecas Build(Document doc)
        {
            List<ColumnGroup> groups = PrecastSurvey.Run(doc, out List<string> notes);
            var arq = new ArquivoPecas
            {
                Origem = string.IsNullOrEmpty(doc.PathName) ? doc.Title : System.IO.Path.GetFileName(doc.PathName),
                Data = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                Notas = notes,
            };

            // níveis: a cota mostrada (Elevation) e a altura geométrica (ProjectElevation, mesmo referencial das coordenadas)
            List<Level> levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.ProjectElevation).ToList();
            double offset = levels.Count > 0 ? levels[0].ProjectElevation - levels[0].Elevation : 0;

            foreach (ColumnGroup g in groups)
            {
                PrecastColumn rep = g.Representative;
                ColumnFrame f = rep.Frame;
                double zBase = f.O.Z, zTop = (f.O + f.Z * f.H).Z;
                if (zTop < zBase) (zBase, zTop) = (zTop, zBase);
                double tol = Core.U.Mm(10);

                var p = new PilarPeca
                {
                    Marca = g.Mark,
                    B = Cm(f.Bx), H = Cm(f.By), L = Cm(f.H),
                    CotaBase = Math.Round(Core.U.ToMm(zBase - offset) / 1000.0, 4),
                    Embutimento = Embedment(rep.Instance),
                    Niveis = levels.Where(l => l.ProjectElevation > zBase + tol && l.ProjectElevation < zTop - tol)
                        .Select(l => new NivelPeca { Nome = l.Name, Cota = Math.Round(Core.U.ToMm(l.Elevation) / 1000.0, 4) }).ToList(),
                };
                foreach (PrecastColumn m in g.Members)
                    p.Elementos.Add(new ElementoPeca { Id = m.Id, Marca = m.CurrentMark ?? "", Giro = m.Rotation * 90 });
                foreach (Corbel c in rep.Corbels)
                    p.Consolos.Add(new ConsoloPeca
                    {
                        Face = Face(c.Face),
                        Inicio = Cm(c.S0), Fim = Cm(c.S1),
                        Base = Cm(c.ZBottom), Topo = Cm(c.ZTop),
                        Balanco = Cm(c.Projection), AlturaPonta = Cm(c.TipHeight),
                    });
                arq.Pilares.Add(p);
            }
            return arq;
        }

        public static int Face(ColumnFace f) => f switch
        {
            ColumnFace.PX => 1,
            ColumnFace.NY => 2,
            ColumnFace.NX => 3,
            _ => 4,
        };

        /// <summary>Parâmetro "Embutimento" do pilar (cm); null se a família não tiver.</summary>
        private static double? Embedment(Element e)
        {
            Parameter p = e.LookupParameter("Embutimento");
            if (p == null || p.StorageType != StorageType.Double) return null;
            return Cm(p.AsDouble());
        }

        private static double Cm(double ft) => Math.Round(Core.U.ToMm(ft) / 10.0, 2);
    }
}
