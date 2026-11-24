using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DEB.Core.Elementos.Consolo;
using DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D;
using DEB.Core.Elementos.Viga.PreMoldada.Calculo;
using DEB.Revit.Core;
using DEB.Revit.Vigas;

namespace DEB.Revit.Precast
{
    /// <summary>
    /// Reação das vigas calculadas (D40) nos consolos dos pilares (D44, etapa 1). O consolo é família aninhada na viga que se apoia
    /// nele: a viga é o <c>SuperComponent</c> do consolo, e a ponta (esquerda/direita, a do relatório da viga) sai da posição do
    /// consolo ao longo do eixo dela. Usa o cálculo do grupo da viga — o mesmo que arma a viga (envoltória das cargas do grupo).
    /// Nos pilares de um grupo, o consolo da representante fica com a maior reação entre os consolos correspondentes.
    /// </summary>
    internal static class ReacoesNosConsolos
    {
        /// <summary>Preenche <see cref="Corbel.Carga"/>; devolve quantos consolos receberam carga. Só leitura do modelo.</summary>
        public static int Aplicar(Document doc, List<ColumnGroup> grupos, List<string> notas)
        {
            List<Corbel> todos = grupos.SelectMany(g => g.Members).SelectMany(m => m.Corbels)
                .Where(k => k.Instance()?.SuperComponent is FamilyInstance).ToList();
            if (todos.Count == 0) return 0;

            var porId = new Dictionary<long, (GrupoViga G, MembroViga M)>();
            try
            {
                var nv = new List<string>();
                foreach (GrupoViga g in VigasPreMoldadas.Levantar(doc, nv))
                {
                    try { VigasPreMoldadas.Calcular(g); }
                    catch (Exception ex) { notas.Add($"Viga {g.Marca}: cálculo não feito ({ex.Message}); os consolos dela ficam sem carga."); continue; }
                    if (g.Calculo == null) continue;
                    foreach (MembroViga m in g.Membros) porId[m.Id] = (g, m);
                }
            }
            catch (Exception ex)
            {
                notas.Add("Reações das vigas nos consolos não calculadas: " + ex.Message);
                return 0;
            }

            int n = 0;
            var semCarga = new HashSet<string>();
            foreach (Corbel k in todos)
            {
                var viga = (FamilyInstance)k.Instance().SuperComponent;
                if (!porId.TryGetValue(viga.Id.Value, out var gm)) continue;
                BoundingBoxXYZ bb = k.Instance().get_BoundingBox(null);
                if (bb == null) continue;
                XYZ c = (bb.Min + bb.Max) / 2;
                PieceInfo info = gm.M.Info;
                bool esquerda = (c - info.Origin).DotProduct(info.AxisX) < info.LengthX / 2;
                if (gm.M.Giro == 180) esquerda = !esquerda;   // ponta no sistema da representante (a do relatório da viga)
                var r = gm.G.Calculo;
                CargasViga cv = gm.G.Cargas;
                if (cv.Vazia) semCarga.Add(gm.G.Marca);
                k.Carga = new CargaConsolo
                {
                    Vg = esquerda ? r.RkE_G : r.RkD_G,
                    Vq = esquerda ? r.RkE_Q : r.RkD_Q,
                    FatorHd = CargaConsolo.FatorDoApoio(k.Apoio),
                    Origem = $"reação da viga {gm.G.Marca} (id {viga.Id.Value}), ponta {(esquerda ? "esquerda" : "direita")}",
                };
                n++;
            }
            if (semCarga.Count > 0)
                notas.Add($"Vigas sem carga digitada (só o peso próprio vai aos consolos): {string.Join(", ", semCarga.OrderBy(x => x))}.");

            // grupo de pilares: a representante leva a envoltória (os consolos estão na mesma ordem canônica em todas as peças)
            foreach (ColumnGroup g in grupos)
            {
                List<Corbel> rep = g.Representative.Corbels;
                for (int i = 0; i < rep.Count; i++)
                {
                    int j = i;
                    rep[i].Carga = CargaConsolo.Maior(g.Members.Where(m => j < m.Corbels.Count).Select(m => m.Corbels[j].Carga));
                }
            }
            return n;
        }
    }
}
