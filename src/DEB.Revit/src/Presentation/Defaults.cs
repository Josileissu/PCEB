using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DEB.Revit.Presentation
{
    /// <summary>
    /// Padrões de fábrica, gravados na pasta de padrões na primeira execução para servirem de base.
    /// Os padrões do PCEB ficam no repositório (src/DEB.Revit/Padroes/*.json, embutidos na DLL);
    /// o de pilar pré-moldado é o PCEB-PPM-A1 (prancha-modelo PP127). Os demais ainda são genéricos.
    /// </summary>
    internal static class Defaults
    {
        public static PresentationStandard For(string elemento)
        {
            PresentationStandard embedded = Embedded(elemento);
            if (embedded != null) return embedded;
            switch (elemento)
            {
                case "VigaPreMoldada": return Generic("VigaPreMoldada", "ARMADURA DA VIGA / BEAM REINFORCEMENT");
                case "Laje": return Generic("Laje", "ARMADURA DA LAJE / SLAB REINFORCEMENT");
                case "Fundacao": return Generic("Fundacao", "ARMADURA DA FUNDAÇÃO / FOUNDATION REINFORCEMENT");
                case Formas: return FormasStandard();
                default: return Generic(elemento, "DETALHAMENTO");
            }
        }

        /// <summary>Elemento do padrão das pranchas de formas (locação e montagem).</summary>
        public const string Formas = "Formas";

        /// <summary>
        /// Pranchas de formas (D25): planta à esquerda, tabelas e legendas à direita, sem título grande nem legenda de níveis, escala
        /// fixa (a planta não muda de escala para caber). Família do carimbo, arquivos e legendas: na cópia do usuário em %AppData%.
        /// </summary>
        private static PresentationStandard FormasStandard() => new PresentationStandard
        {
            Codigo = "PCEB-FORMAS-A0",
            Versao = 2,
            Nome = "Pranchas de formas (locação e montagem)",
            Elemento = Formas,
            Descricao = "Planta de locação (com a tabela de cargas na fundação) e plantas de montagem por pavimento (com a tabela de lajes). " +
                        "Ajuste a seção \"Formas\" (famílias dos identificadores, filtros, nomes) e o carimbo.",
            Folha = new SheetSettings
            {
                Carimbo = "A0",   // referência em A0; o arquivo da família (ArquivoCarimbo) fica na cópia local do usuário
                NumeroFolha = "",
                NomeFolha = "",
                LegendaNiveis = false,
                EscalasAlternativas = new[] { 50, 75, 100 },
            },
            Textos = new TextSettings { TituloPeca = "" },
            Regioes = new Dictionary<string, Region>
            {
                // planta em cima, com a largura toda (obras compridas); tabelas, notas e legendas embaixo
                ["Planta"] = new Region(0.00, 0.36, 1.00, 0.64),
                ["Tabelas"] = new Region(0.00, 0.00, 0.45, 0.35),
                ["Notas"] = new Region(0.46, 0.00, 0.25, 0.35),
                ["Legendas"] = new Region(0.72, 0.00, 0.28, 0.35),
            },
            Vistas = new List<ViewSpec>(),
            Tabelas = new List<TableSpec>(),
            Formas = new FormasSettings(),
        };

        /// <summary>Padrão do PCEB embutido na DLL (arquivos src/DEB.Revit/Padroes/*.json) para o elemento.</summary>
        private static PresentationStandard Embedded(string elemento)
        {
            Assembly asm = typeof(Defaults).Assembly;
            foreach (string name in asm.GetManifestResourceNames()
                         .Where(n => n.StartsWith("DEB.Revit.Padroes.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
                         .OrderBy(n => n))
            {
                using (Stream st = asm.GetManifestResourceStream(name))
                using (var rd = new StreamReader(st))
                {
                    PresentationStandard s = PresentationStandard.Deserialize(rd.ReadToEnd());
                    if (s != null && string.Equals(s.Elemento, elemento, StringComparison.OrdinalIgnoreCase)) return s;
                }
            }
            return null;
        }

        private static Dictionary<string, Region> StandardRegions() => new Dictionary<string, Region>
        {
            // Frações da área útil (dentro das margens), origem no canto inferior esquerdo.
            ["Titulo"] = new Region(0.00, 0.92, 0.20, 0.08),
            ["Vistas"] = new Region(0.00, 0.40, 0.62, 0.52),
            ["Secoes"] = new Region(0.62, 0.66, 0.16, 0.34),
            ["Cortes"] = new Region(0.62, 0.40, 0.16, 0.26),
            ["Tabelas"] = new Region(0.78, 0.56, 0.22, 0.44),
            ["Producao"] = new Region(0.78, 0.42, 0.22, 0.14),
            ["Detalhes"] = new Region(0.00, 0.00, 0.78, 0.38),
            ["Notas"] = new Region(0.78, 0.30, 0.22, 0.12)
        };

        /// <summary>Estrutura mínima para elementos ainda não implementados (o arquivo serve de ponto de partida).</summary>
        private static PresentationStandard Generic(string elemento, string titulo) => new PresentationStandard
        {
            Nome = elemento,
            Elemento = elemento,
            Descricao = "Padrão inicial; ajuste vistas, regiões e textos conforme o escritório.",
            Folha = new SheetSettings
            {
                Carimbo = "A1",
                NomeFolha = titulo + " - {Marca}",
                ParametrosCarimbo = new Dictionary<string, string> { ["Título"] = titulo + "\n{Marca}" }
            },
            Regioes = StandardRegions(),
            Vistas = new List<ViewSpec>
            {
                new ViewSpec { Tipo = ViewKind.Face, Face = 1, Titulo = "VISTA 1\n(VIEW 1)", Escala = 50, Regiao = "Vistas" },
                new ViewSpec { Tipo = ViewKind.Armadura, Face = 1, Titulo = "ARMADURA\n(REINFORCEMENT)", Escala = 50, Regiao = "Vistas", Cotas = false, MostrarArmadura = true },
                new ViewSpec { Tipo = ViewKind.SecaoArmadura, Titulo = "SEÇÃO {Numero}-{Numero} - ARMADURA", Escala = 25, Regiao = "Secoes", Cotas = false, MostrarArmadura = true }
            },
            Tabelas = new List<TableSpec>
            {
                new TableSpec { Tipo = TableKind.RelacaoDoAco, Titulo = "RELAÇÃO DO AÇO - (REBAR SCHEDULE)" },
                new TableSpec { Tipo = TableKind.ResumoDoAco, Titulo = "RESUMO DO AÇO (REBAR SUMMARY)" }
            },
            Producao = new Dictionary<string, string> { ["fck"] = "", ["DensidadeConcreto_t_m3"] = "2.5" }
        };
    }
}
