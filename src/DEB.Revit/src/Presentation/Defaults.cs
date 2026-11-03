using System.Collections.Generic;

namespace DEB.Revit.Presentation
{
    /// <summary>
    /// Padrões de fábrica, gravados na pasta de padrões na primeira execução para servirem de base.
    /// O de pilar pré-moldado segue a prancha-modelo do escritório (PP127, formato A1).
    /// </summary>
    internal static class Defaults
    {
        public static PresentationStandard For(string elemento)
        {
            switch (elemento)
            {
                case "PilarPreMoldado": return PrecastColumn();
                case "VigaPreMoldada": return Generic("VigaPreMoldada", "ARMADURA DA VIGA / BEAM REINFORCEMENT");
                case "Laje": return Generic("Laje", "ARMADURA DA LAJE / SLAB REINFORCEMENT");
                case "Fundacao": return Generic("Fundacao", "ARMADURA DA FUNDAÇÃO / FOUNDATION REINFORCEMENT");
                default: return Generic(elemento, "DETALHAMENTO");
            }
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

        private static PresentationStandard PrecastColumn() => new PresentationStandard
        {
            Nome = "Pilar pré-moldado (padrão PP127)",
            Elemento = "PilarPreMoldado",
            Descricao = "Prancha A1 com vistas das 4 faces, elevação da armadura, seções com armadura, cortes, " +
                        "relação/resumo do aço, informações de produção, notas e detalhes padrão.",
            Folha = new SheetSettings
            {
                Carimbo = "A1",
                NumeroFolha = "{Marca}",
                NomeFolha = "ARMADURA DO PILAR / COLUMN REINFORCEMENT - {Marca}",
                ParametrosCarimbo = new Dictionary<string, string>
                {
                    ["Título"] = "ARMADURA DO PILAR / COLUMN REINFORCEMENT\n{Marca}"
                }
            },
            Regioes = StandardRegions(),
            Vistas = new List<ViewSpec>
            {
                new ViewSpec { Tipo = ViewKind.Face, Face = 1, Titulo = "VISTA 1\n(VIEW 1)", Escala = 50, Regiao = "Vistas" },
                new ViewSpec { Tipo = ViewKind.Face, Face = 2, Titulo = "VISTA 2\n(VIEW 2)", Escala = 50, Regiao = "Vistas" },
                new ViewSpec { Tipo = ViewKind.Face, Face = 3, Titulo = "VISTA 3\n(VIEW 3)", Escala = 50, Regiao = "Vistas" },
                new ViewSpec { Tipo = ViewKind.Face, Face = 4, Titulo = "VISTA 4\n(VIEW 4)", Escala = 50, Regiao = "Vistas" },
                new ViewSpec { Tipo = ViewKind.Armadura, Face = 1, Titulo = "ARMADURA\n(REINFORCEMENT)", Escala = 50, Regiao = "Vistas", Cotas = false, MostrarArmadura = true },
                new ViewSpec { Tipo = ViewKind.SecaoArmadura, Posicao = CutPosition.Consolos, Titulo = "SEÇÃO {Numero}-{Numero} - ARMADURA\n(SECTION {Numero}-{Numero} - REBAR)", Escala = 25, Regiao = "Secoes", Cotas = false, MostrarArmadura = true },
                new ViewSpec { Tipo = ViewKind.SecaoArmadura, Posicao = CutPosition.Corrente, Titulo = "SEÇÃO {Numero}-{Numero} - ARMADURA\n(SECTION {Numero}-{Numero} - REBAR)", Escala = 25, Regiao = "Secoes", Cotas = false, MostrarArmadura = true },
                new ViewSpec { Tipo = ViewKind.Corte, Posicao = CutPosition.Consolos, Titulo = "CORTE {Letra}-{Letra}\n(SECTION {Letra}-{Letra})", Escala = 25, Regiao = "Cortes" },
                new ViewSpec { Tipo = ViewKind.Corte, Posicao = CutPosition.Corrente, Titulo = "CORTE {Letra}-{Letra}\n(SECTION {Letra}-{Letra})", Escala = 25, Regiao = "Cortes" },
                new ViewSpec { Tipo = ViewKind.Vista3D, Ativa = false, Titulo = "VISTA 3D", Escala = 50, Regiao = "Detalhes", Cotas = false, MostrarArmadura = true }
            },
            Tabelas = new List<TableSpec>
            {
                new TableSpec { Tipo = TableKind.RelacaoDoAco, Titulo = "RELAÇÃO DO AÇO - (REBAR SCHEDULE)", Regiao = "Tabelas" },
                new TableSpec { Tipo = TableKind.ResumoDoAco, Titulo = "RESUMO DO AÇO (REBAR SUMMARY)", Regiao = "Tabelas" }
            },
            Quadros = new List<TextBlockSpec>
            {
                new TextBlockSpec
                {
                    Nome = "Informações para produção",
                    Regiao = "Producao",
                    Moldura = true,
                    Texto = "INFORMAÇÕES P/ PRODUÇÃO\n" +
                            "RESISTÊNCIA AO SAQUE: fcj = {fcjSaque} MPa\n" +
                            "RESISTÊNCIA MONTAGEM: fcj = {fcjMontagem} MPa\n" +
                            "RESISTÊNCIA 28 DIAS: fck = {fck} MPa\n" +
                            "VOLUME DE CONCRETO: {Volume} m³\n" +
                            "PESO: {Peso} ton\n" +
                            "QUANTIDADE: {Quantidade} unidades\n\n" +
                            "MANUFACTURING INFORMATION\n" +
                            "STRENGTH DEMOLDING: fcj = {fcjSaque} MPa\n" +
                            "STRENGTH ERECTION: fcj = {fcjMontagem} MPa\n" +
                            "STRENGTH AT 28 DAYS: fck = {fck} MPa\n" +
                            "CONCRETE VOLUME: {Volume} m³\n" +
                            "WEIGHT: {Peso} ton\n" +
                            "QUANTITY: {Quantidade} unit"
                },
                new TextBlockSpec
                {
                    Nome = "Notas",
                    Regiao = "Notas",
                    Texto = "NOTA:\n" +
                            "1) DIMENSÕES EM CM;\n" +
                            "2) PARA INFORMAÇÕES GERAIS, VER PRANCHA: ----;\n" +
                            "3) UTILIZAR NEOPRENE 15 mm.\n\n" +
                            "NOTE:\n" +
                            "1) DIMENSIONS IN CM;\n" +
                            "2) FOR GENERAL INFORMATION, SEE SHEET: ----;\n" +
                            "3) USE 15 mm NEOPRENE."
                }
            },
            Legendas = new List<LegendSpec>
            {
                new LegendSpec { Nome = "RAIO DE CURVATURA PARA GANCHOS", Regiao = "Detalhes" },
                new LegendSpec { Nome = "COMPRIMENTO DAS BARRAS", Regiao = "Detalhes" },
                new LegendSpec { Nome = "EMENDAS POR TRASPASSE", Regiao = "Detalhes" },
                new LegendSpec { Nome = "EMENDAS POR SOLDA POR TRASPASSE", Regiao = "Detalhes" },
                new LegendSpec { Nome = "ATERRAMENTO ESTRUTURAL PILARES", Regiao = "Detalhes" }
            },
            Producao = new Dictionary<string, string>
            {
                ["fcjSaque"] = "30",
                ["fcjMontagem"] = "35",
                ["fck"] = "40",
                ["DensidadeConcreto_t_m3"] = "2.5"
            }
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
