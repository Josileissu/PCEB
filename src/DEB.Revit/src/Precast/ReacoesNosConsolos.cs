using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DEB.Core.Analise.Barras;
using DEB.Core.Analise.Objetos;
using DEB.Core.Configuracao.Projeto;
using DEB.Core.Elementos.Consolo;
using DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D;
using DEB.Core.Elementos.Viga.PreMoldada.Calculo;
using DEB.Core.Pecas;
using DEB.Revit.Core;
using DEB.Revit.Vigas;

namespace DEB.Revit.Precast
{
    /// <summary>
    /// Carga das vigas nos consolos dos pilares (D44 etapa 1, D46). O consolo é família aninhada na viga que se apoia nele: a viga é
    /// o <c>SuperComponent</c> do consolo, e a ponta sai da posição do consolo ao longo do eixo dela.
    /// <list type="number">
    /// <item>Análise por objetos (D46): pilares, vigas e ligações (barra rígida + mola do aparelho) num modelo de barras; o consolo
    /// recebe Vg, Vq e as forças horizontais Hg, Hq da análise.</item>
    /// <item>Se a análise não puder ser feita: a reação da viga isolada (cálculo do grupo da viga, D40), com Hd pela regra da ligação.</item>
    /// </list>
    /// Nos pilares de um grupo, o consolo da representante fica com a maior carga entre os consolos correspondentes.
    /// </summary>
    internal static class ReacoesNosConsolos
    {
        private const double Cm = 30.48;   // pés → cm
        private static Vetor3 V(XYZ p) => new Vetor3(p.X * Cm, p.Y * Cm, p.Z * Cm);

        /// <summary>Preenche <see cref="Corbel.Carga"/>; devolve quantos consolos receberam carga. Só leitura do modelo.</summary>
        public static int Aplicar(Document doc, List<ColumnGroup> grupos, List<string> notas, ConfiguracaoProjeto cfg = null)
            => Aplicar(doc, grupos, notas, cfg, out _);

        public static int Aplicar(Document doc, List<ColumnGroup> grupos, List<string> notas, ConfiguracaoProjeto cfg, out EstruturaObjetos estrutura)
        {
            estrutura = null;
            cfg ??= new ConfiguracaoProjeto();
            List<Corbel> todos = grupos.SelectMany(g => g.Members).SelectMany(m => m.Corbels)
                .Where(k => k.Instance()?.SuperComponent is FamilyInstance).ToList();
            if (todos.Count == 0) return 0;

            var porId = new Dictionary<long, (GrupoViga G, MembroViga M)>();
            try
            {
                foreach (GrupoViga g in VigasPreMoldadas.Levantar(doc, new List<string>()))
                    foreach (MembroViga m in g.Membros) porId[m.Id] = (g, m);
            }
            catch (Exception ex)
            {
                notas.Add("Vigas não levantadas: os consolos ficam sem carga (" + ex.Message + ").");
                return 0;
            }

            int n = 0;
            try
            {
                estrutura = DEB.Revit.Analise.EstruturaRevit.Montar(doc, grupos, porId, cfg, notas, out var ligacoes);
                estrutura.Analisar(cfg);
                foreach (var (k, l) in ligacoes) { k.Carga = estrutura.CargaDoConsolo(l); n++; }
                foreach (string a in estrutura.Avisos.Distinct().Take(12)) notas.Add("Análise: " + a);
                if (estrutura.Avisos.Count > 12) notas.Add($"Análise: mais {estrutura.Avisos.Count - 12} avisos.");
                var semCarga = porId.Values.Where(x => ligacoes.Any(l => l.L.Viga.Dono.Id == x.M.Id.ToString()) && x.M.Cargas.Vazia)
                    .Select(x => x.G.Marca).Distinct().OrderBy(x => x).ToList();
                if (semCarga.Count > 0) notas.Add($"Vigas sem carga digitada (só o peso próprio vai aos consolos): {string.Join(", ", semCarga)}.");
            }
            catch (Exception ex)
            {
                estrutura = null;
                notas.Add($"Análise por objetos não feita ({ex.Message}): os consolos usam a reação da viga isolada.");
                n = ReacaoIsolada(todos, porId, notas);
            }

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

        /// <summary>Reserva: a reação da viga isolada (cálculo do grupo da viga, D40), com Hd pela regra da ligação.</summary>
        private static int ReacaoIsolada(List<Corbel> todos, Dictionary<long, (GrupoViga G, MembroViga M)> porId, List<string> notas)
        {
            int n = 0;
            var semCarga = new HashSet<string>();
            foreach (var g in porId.Values.Select(x => x.G).Distinct())
            {
                try { if (g.Calculo == null) VigasPreMoldadas.Calcular(g); }
                catch (Exception ex) { notas.Add($"Viga {g.Marca}: cálculo não feito ({ex.Message}); os consolos dela ficam sem carga."); }
            }
            foreach (Corbel k in todos)
            {
                var viga = (FamilyInstance)k.Instance().SuperComponent;
                if (!porId.TryGetValue(viga.Id.Value, out var gm) || gm.G.Calculo == null) continue;
                BoundingBoxXYZ bb = k.Instance().get_BoundingBox(null);
                if (bb == null) continue;
                XYZ c = (bb.Min + bb.Max) / 2;
                PieceInfo info = gm.M.Info;
                bool esquerda = (c - info.Origin).DotProduct(info.AxisX) < info.LengthX / 2;
                if (gm.M.Giro == 180) esquerda = !esquerda;   // ponta no sistema da representante (a do relatório da viga)
                var r = gm.G.Calculo;
                if (gm.G.Cargas.Vazia) semCarga.Add(gm.G.Marca);
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
            return n;
        }
    }
}
