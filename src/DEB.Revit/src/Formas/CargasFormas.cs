using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DEB.Core.Formas;
using DEB.Core.Numeracao;
using DEB.Revit.Core;
using DEB.Revit.Presentation;

namespace DEB.Revit.Formas
{
    /// <summary>
    /// Cargas na fundação por CSV (D25, etapa 1): importar grava PCEB_Fz_Max… nos pilares pela Marca (célula vazia não mexe; marcas
    /// que não existem no modelo vão para o relatório); exportar grava o mesmo formato com os valores do modelo. A leitura e a
    /// escrita do arquivo estão no núcleo (<see cref="CsvCargas"/>).
    /// </summary>
    internal static class CargasFormas
    {
        /// <summary>Pilares do filtro com marca, agrupados pela marca (peças iguais podem repetir a marca).</summary>
        private static Dictionary<string, List<FamilyInstance>> PorMarca(Document doc, FormasSettings cfg) =>
            ModeloFormas.Filtrar(doc, BuiltInCategory.OST_StructuralColumns, cfg.FiltroPilares, null, "pilar")
                .Where(f => ModeloFormas.Marca(f) != "")
                .GroupBy(f => ModeloFormas.Marca(f), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        /// <summary>Importa o texto do CSV. Precisa de transação (e dos parâmetros já criados).</summary>
        public static void Importar(Document doc, FormasSettings cfg, string texto, PieceLog log)
        {
            var avisos = new List<string>();
            List<CargaFundacao> cargas = CsvCargas.Ler(texto, avisos);
            foreach (string a in avisos) log.Warn(a);
            Dictionary<string, List<FamilyInstance>> pilares = PorMarca(doc, cfg);
            var naoAchadas = new List<string>();
            int pecas = 0, valores = 0, falhas = 0;
            foreach (CargaFundacao c in cargas)
            {
                if (!pilares.TryGetValue(c.Marca, out List<FamilyInstance> l)) { naoAchadas.Add(c.Marca); continue; }
                foreach (FamilyInstance f in l)
                {
                    pecas++;
                    for (int k = 0; k < CsvCargas.Colunas.Length; k++)
                    {
                        if (c.Valores[k] == null) continue;
                        Parameter p = f.get_Parameter(ParametrosFormas.Esforcos[k].Guid);
                        try
                        {
                            if (p == null || p.IsReadOnly || !p.Set(c.Valores[k].Value)) falhas++;
                            else valores++;
                        }
                        catch { falhas++; }
                    }
                }
                if (l.Count > 1) log.Info($"Marca {c.Marca}: {l.Count} pilares com a mesma marca receberam os mesmos esforços.");
            }
            log.Info($"Cargas importadas: {cargas.Count} linha(s), {pecas} pilar(es), {valores} valor(es) gravado(s).");
            if (falhas > 0) log.Warn($"{falhas} valor(es) não gravados (parâmetro ausente ou elemento bloqueado).");
            if (naoAchadas.Count > 0)
                log.Warn($"Marcas do arquivo sem pilar no modelo ({naoAchadas.Count}): {string.Join(", ", naoAchadas.OrderBy(m => m, ComparadorNatural.Instancia))}.");
        }

        /// <summary>Texto do CSV com os esforços do modelo, um pilar por marca, na ordem natural.</summary>
        public static string Exportar(Document doc, FormasSettings cfg, out int linhas)
        {
            var cargas = new List<CargaFundacao>();
            foreach (var kv in PorMarca(doc, cfg).OrderBy(k => k.Key, ComparadorNatural.Instancia))
            {
                var c = new CargaFundacao { Marca = kv.Key };
                FamilyInstance f = kv.Value[0];
                for (int k = 0; k < CsvCargas.Colunas.Length; k++)
                {
                    Parameter p = f.get_Parameter(ParametrosFormas.Esforcos[k].Guid);
                    if (p != null && p.HasValue) c.Valores[k] = p.AsDouble();
                }
                cargas.Add(c);
            }
            linhas = cargas.Count;
            return CsvCargas.Gravar(cargas);
        }
    }
}
