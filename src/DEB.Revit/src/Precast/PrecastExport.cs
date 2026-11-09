using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using DEB.Core.Intercambio;
using DEB.Core.Pecas;

namespace DEB.Revit.Precast
{
    /// <summary>
    /// Peças do modelo → arquivo de troca do DEB (<see cref="ArquivoPecas"/>), para detalhar no CAD (ARMIMPORTARPECAS).
    /// Desde 09/11/2026 há UM levantamento só: o da conferência (<see cref="PieceReview"/> — seções exatas por corte do sólido,
    /// consolos pelo perfil), convertido no núcleo por <see cref="ConversorLevantamento"/>. O que a janela "Conferir peças"
    /// mostra é o que vai para o JSON; a marca das peças no modelo é o nome do detalhamento. Não altera o modelo.
    /// Faces do pilar: 1 = +x, 2 = −y, 3 = −x, 4 = +y, com b em x e h em y (sistema do representante do grupo).
    /// </summary>
    internal static class PrecastExport
    {
        public static ArquivoPecas Build(Document doc)
        {
            var notes = new List<string>();
            return Build(doc, PieceReview.Run(doc, notes), notes);
        }

        /// <summary>Do levantamento já feito (a janela de conferência exporta o que está mostrando).</summary>
        public static ArquivoPecas Build(Document doc, IEnumerable<PecaLevantada> pecas, IEnumerable<string> notes) =>
            ConversorLevantamento.Converter(pecas,
                string.IsNullOrEmpty(doc.PathName) ? doc.Title : System.IO.Path.GetFileName(doc.PathName),
                DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), notes);
    }
}
