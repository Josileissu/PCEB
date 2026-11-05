using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DEB.Revit.Presentation
{
    /// <summary>
    /// Padrão de apresentação de uma prancha: quais vistas, tabelas, quadros e detalhes padrão entram,
    /// com que títulos, escalas e em que região da folha. Um arquivo JSON por tipo de elemento
    /// (pilar pré-moldado, viga, laje, fundação...), em %AppData%\PCEB\DEB.Revit\Padroes\.
    ///
    /// Textos aceitam campos entre chaves, substituídos na geração:
    ///   {Marca} {Quantidade} {Secao} {Comprimento} {Volume} {Peso} {Data} {Letra} {Numero}
    ///   e qualquer chave de "Producao" (ex.: {fck}, {fcjSaque}).
    /// </summary>
    public sealed class PresentationStandard
    {
        /// <summary>Código pela convenção PCEB-&lt;ELEMENTO&gt;-&lt;FORMATO&gt;[-variante] (ver docs/04_arquitetura/padroes_apresentacao.md).</summary>
        public string Codigo { get; set; } = "";
        /// <summary>Versão do padrão de fábrica. Arquivo do usuário com versão menor é guardado (.bak) e trocado pelo novo.</summary>
        public int Versao { get; set; }
        public string Nome { get; set; } = "";
        /// <summary>Tipo de elemento a que o padrão se aplica: PilarPreMoldado, VigaPreMoldada, Laje, Fundacao...</summary>
        public string Elemento { get; set; } = "";
        public string Descricao { get; set; } = "";

        public SheetSettings Folha { get; set; } = new SheetSettings();
        public UnitSettings Unidades { get; set; } = new UnitSettings();
        public TextSettings Textos { get; set; } = new TextSettings();
        public RebarSettings Armadura { get; set; } = new RebarSettings();

        /// <summary>Regiões da área útil da folha, em frações (0–1) com origem no canto inferior esquerdo.</summary>
        public Dictionary<string, Region> Regioes { get; set; } = new Dictionary<string, Region>();

        public List<ViewSpec> Vistas { get; set; } = new List<ViewSpec>();
        public List<TableSpec> Tabelas { get; set; } = new List<TableSpec>();
        public List<TextBlockSpec> Quadros { get; set; } = new List<TextBlockSpec>();
        /// <summary>Legendas do projeto (detalhes padrão: raio de dobra, emendas, aterramento...) colocadas em toda prancha.</summary>
        public List<LegendSpec> Legendas { get; set; } = new List<LegendSpec>();

        /// <summary>Valores de produção usados nos textos (resistências, densidade do concreto...).</summary>
        public Dictionary<string, string> Producao { get; set; } = new Dictionary<string, string>();

        // ------------------------------------------------------------------

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Converters = { new JsonStringEnumConverter() }
        };

        public static string Folder => Path.Combine(DEB.Revit.Core.UserFolders.Root, "Padroes");

        /// <summary>
        /// Carrega o padrão do elemento. Procura um arquivo com o mesmo "Elemento" na pasta de padrões;
        /// se não houver, grava o padrão de fábrica (para servir de ponto de partida) e o devolve.
        /// </summary>
        public static PresentationStandard Load(string elemento, out string warning)
        {
            warning = null;
            Directory.CreateDirectory(Folder);
            PresentationStandard def = Defaults.For(elemento);
            foreach (string file in Directory.GetFiles(Folder, "*.json").OrderBy(f => f))
            {
                try
                {
                    var s = JsonSerializer.Deserialize<PresentationStandard>(File.ReadAllText(file), Json);
                    if (s == null || !string.Equals(s.Elemento, elemento, StringComparison.OrdinalIgnoreCase)) continue;
                    if (s.Versao >= def.Versao) return s;
                    // padrão de fábrica mais novo: guarda o do usuário e passa a usar o novo
                    string bak = file + $".v{s.Versao}.bak";
                    File.Copy(file, bak, true);
                    File.Delete(file);
                    warning = $"Padrão \"{Path.GetFileName(file)}\" (versão {s.Versao}) substituído pelo {def.Codigo} versão {def.Versao}; o antigo ficou em {Path.GetFileName(bak)}.";
                    break;
                }
                catch (Exception ex)
                {
                    warning = $"Padrão \"{Path.GetFileName(file)}\" ignorado: {ex.Message}";
                }
            }

            File.WriteAllText(Path.Combine(Folder, def.FileName()), JsonSerializer.Serialize(def, Json));
            return def;
        }

        public string FileName() => (string.IsNullOrWhiteSpace(Codigo) ? Elemento.ToLowerInvariant() : Codigo) + ".json";

        public static string Serialize(PresentationStandard s) => JsonSerializer.Serialize(s, Json);

        public static PresentationStandard Deserialize(string json) => JsonSerializer.Deserialize<PresentationStandard>(json, Json);

        /// <summary>Substitui {Campos} no texto.</summary>
        public static string Fill(string template, IDictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(template)) return template;
            string s = template;
            foreach (var kv in values) s = s.Replace("{" + kv.Key + "}", kv.Value ?? "");
            return s;
        }
    }

    public sealed class SheetSettings
    {
        /// <summary>Família do carimbo (nome exato; vazio = qualquer família).</summary>
        public string FamiliaCarimbo { get; set; } = "";
        /// <summary>Trecho do nome do tipo do carimbo (ex.: "A1"; vazio = primeiro tipo).</summary>
        public string Carimbo { get; set; } = "";
        /// <summary>.rfa carregado no projeto quando o carimbo não está lá.</summary>
        public string ArquivoCarimbo { get; set; } = "";
        public string NumeroFolha { get; set; } = "{Marca}";
        public string NomeFolha { get; set; } = "{Marca}";
        /// <summary>Parâmetros do carimbo (ou da folha) a preencher: nome do parâmetro → texto.</summary>
        public Dictionary<string, string> ParametrosCarimbo { get; set; } = new Dictionary<string, string>();

        public double MargemEsquerdaMm { get; set; } = 25;
        public double MargemDireitaMm { get; set; } = 10;
        public double MargemSuperiorMm { get; set; } = 10;
        public double MargemInferiorMm { get; set; } = 10;
        /// <summary>Espaço entre itens de uma mesma região (mm de papel).</summary>
        public double EspacamentoMm { get; set; } = 12;
        /// <summary>Espaço reservado abaixo de cada vista para o título (mm de papel).</summary>
        public double TituloVistaMm { get; set; } = 14;
        /// <summary>Largura média de um caractere do título das vistas (mm de papel), para não sobrepor títulos.</summary>
        public double LarguraCaractereTituloMm { get; set; } = 4.3;
        /// <summary>Título das vistas em texto centrado ACIMA da vista (como o template do CAD), no lugar da etiqueta
        /// do Revit embaixo. A 1ª linha (PT) sai em negrito sublinhado; depois vem <see cref="TituloEscala"/>.</summary>
        public bool TituloAcima { get; set; }
        /// <summary>Linha da escala no título acima ({Escala} = denominador). Vazio = sem linha de escala.</summary>
        public string TituloEscala { get; set; } = "Esc. 1:{Escala}";
        /// <summary>Escalas tentadas, em ordem, quando as vistas de uma região não cabem.</summary>
        public int[] EscalasAlternativas { get; set; } = { 20, 25, 50, 75, 100 };
    }

    public sealed class UnitSettings
    {
        /// <summary>Unidade das cotas: "cm" ou "mm".</summary>
        public string Cotas { get; set; } = "cm";
        public double Precisao { get; set; } = 0.5;
        /// <summary>Tipo de cota criado/usado pelo plugin.</summary>
        public string TipoDeCota { get; set; } = "SD - Cota cm";
        /// <summary>Distância da linha de cota à peça (mm de papel).</summary>
        public double AfastamentoCotaMm { get; set; } = 8;
    }

    public sealed class TextSettings
    {
        public string Fonte { get; set; } = "Arial";
        /// <summary>Altura (mm de papel) dos textos comuns (quadros, notas, rótulos de armadura).</summary>
        public double AlturaTextoMm { get; set; } = 2.5;
        /// <summary>Título grande da peça no canto da prancha (ex.: "PP127"). Vazio = não coloca.</summary>
        public string TituloPeca { get; set; } = "{Marca}";
        public double AlturaTituloPecaMm { get; set; } = 10;
        public string RegiaoTituloPeca { get; set; } = "Titulo";
        /// <summary>Altura (mm de papel) do título das vistas quando ele vai acima delas.</summary>
        public double AlturaTituloVistaMm { get; set; } = 3.5;
    }

    public sealed class RebarSettings
    {
        /// <summary>Partição (numeração N1, N2...) das barras de cada peça.</summary>
        public string Particao { get; set; } = "{Marca}";
        /// <summary>Rótulo de conjunto de barras: {Qtd} {N} {Diam} {Comp} {Espacamento}.</summary>
        public string RotuloBarra { get; set; } = "{Qtd} N{N} ø{Diam} C={Comp}";
        public string RotuloEstribos { get; set; } = "{Qtd} N{N} c/{Espacamento}";
        /// <summary>Desenha o dobramento (forma) das barras de estribo/tirante junto às seções.</summary>
        public bool DesenhoDeDobramento { get; set; } = true;
        /// <summary>Densidade do aço para o peso (kg/m³).</summary>
        public double DensidadeAco { get; set; } = 7850;
    }

    public sealed class Region
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double L { get; set; } = 1;
        public double A { get; set; } = 1;

        public Region() { }
        public Region(double x, double y, double l, double a) { X = x; Y = y; L = l; A = a; }
    }

    public enum ViewKind
    {
        /// <summary>Elevação de uma face (1 = +X, 2 = −Y, 3 = −X, 4 = +Y no sistema da peça).</summary>
        Face,
        /// <summary>Elevação da armadura (barras visíveis, com rótulos).</summary>
        Armadura,
        /// <summary>Corte transversal só com o concreto e cotas.</summary>
        Corte,
        /// <summary>Seção transversal com a armadura.</summary>
        SecaoArmadura,
        Vista3D,
        /// <summary>Corte vertical pelo meio de cada tipo de consolo (tirante e costura). Título aceita {Consolo}.</summary>
        ConsoloCorte,
        /// <summary>Planta de cada tipo de consolo, logo abaixo do topo. Título aceita {Consolo}.</summary>
        ConsoloPlanta
    }

    /// <summary>Onde passa um corte transversal.</summary>
    public enum CutPosition { Corrente, Consolos, Topo }

    public sealed class ViewSpec
    {
        public ViewKind Tipo { get; set; }
        public bool Ativa { get; set; } = true;
        public int Face { get; set; } = 1;
        public CutPosition Posicao { get; set; } = CutPosition.Corrente;
        /// <summary>Título (aceita {Letra}, {Numero}, {Face}). Use \n para a segunda linha (tradução).</summary>
        public string Titulo { get; set; } = "";
        public int Escala { get; set; } = 50;
        public string Regiao { get; set; } = "Vistas";
        public bool Cotas { get; set; } = true;
        public bool MostrarArmadura { get; set; }
        /// <summary>
        /// Template de vista (PCEB-&lt;ELEMENTO&gt;-&lt;VISTA&gt;). Se não existir no projeto, é criado a partir da 1ª vista
        /// configurada por este item (escala e recorte ficam livres, para a prancha poder ajustar). Vazio = nenhum.
        /// </summary>
        public string Template { get; set; } = "";
    }

    public enum TableKind { RelacaoDoAco, ResumoDoAco }

    public sealed class TableSpec
    {
        public TableKind Tipo { get; set; }
        public bool Ativa { get; set; } = true;
        public string Titulo { get; set; } = "";
        public string Regiao { get; set; } = "Tabelas";
    }

    public sealed class TextBlockSpec
    {
        public string Nome { get; set; } = "";
        public string Texto { get; set; } = "";
        public string Regiao { get; set; } = "Notas";
        public bool Moldura { get; set; }
    }

    public sealed class LegendSpec
    {
        /// <summary>Nome exato da legenda no projeto (Vistas → Legendas).</summary>
        public string Nome { get; set; } = "";
        public string Regiao { get; set; } = "Detalhes";
    }
}
