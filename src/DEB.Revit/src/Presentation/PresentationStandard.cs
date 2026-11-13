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

        /// <summary>Pranchas de formas (locação e montagem, D25): identificadores, filtros, tabelas e folhas. Padrões que não
        /// têm esta seção (JSONs antigos) recebem os valores de fábrica.</summary>
        public FormasSettings Formas { get; set; } = new FormasSettings();

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
        /// <summary>.rte/.rvt com pelo menos uma legenda: se o projeto não tiver nenhuma, uma é copiada de lá para servir
        /// de base às legendas dos detalhes padrão (a API do Revit não cria legendas do zero).</summary>
        public string ArquivoLegendaBase { get; set; } = "";
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
        /// <summary>
        /// Marcas de corte das elevações como no pilar padrão do CAD (traço grosso curto + letra/número, dos dois lados),
        /// no lugar das marcas do Revit (cabeça com nº do detalhe e folha).
        /// </summary>
        public bool MarcasDeCorteSimples { get; set; } = true;
        /// <summary>
        /// Legenda "PCEB - Níveis da obra - escala 1-N" (todos os níveis do projeto: linha traço-ponto, nome e cota) na folha, à
        /// esquerda das elevações e na altura de cada nível. Uma legenda por escala, reaproveitada em todas as pranchas;
        /// refeita quando os níveis do projeto mudam. As elevações ficam niveladas entre si (base do pilar na mesma altura).
        /// </summary>
        public bool LegendaNiveis { get; set; } = true;
        /// <summary>Folga (mm de papel) do recorte de anotação de cada vista; 0 = a do Revit (1"). Define quanto a vista ocupa na prancha.</summary>
        public double FolgaAnotacaoMm { get; set; } = 12;
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
        /// <summary>Chamadas "N1" com linha de chamada até cada barra que atravessa as seções com armadura.</summary>
        public bool ChamadasNasSecoes { get; set; } = true;
        /// <summary>Densidade do aço para o peso (kg/m³).</summary>
        public double DensidadeAco { get; set; } = 7850;
        /// <summary>Família de etiqueta de vergalhão do escritório (.rfa), carregada se não estiver no projeto.</summary>
        public string ArquivoEtiqueta { get; set; } = "";
        /// <summary>Nome da família de etiqueta de vergalhão (no projeto).</summary>
        public string FamiliaEtiqueta { get; set; } = "";
        /// <summary>Tipo da etiqueta das barras longitudinais (ex.: "1 N1 Ø C" → "8 N1 ø12.5 C=995").</summary>
        public string EtiquetaLongitudinal { get; set; } = "";
        /// <summary>Tipo da etiqueta dos estribos/grampos na cota dos trechos (ex.: "1 N1 c/" → "16 N2 c/15").</summary>
        public string EtiquetaEstribo { get; set; } = "";
        /// <summary>Tipo da etiqueta em duas linhas ("x 9 N3" / "c/7.50", sem bitola) para os trechos curtos de estribos,
        /// onde a de uma linha não cabe na vertical (ex.: "1 N1 c/ - Quebra"), como o escritório ajustou na prancha.</summary>
        public string EtiquetaEstriboQuebra { get; set; } = "";
        /// <summary>
        /// Longitudinais na vista de armadura como detalhes de curvatura (em escala, na altura real, ao lado da elevação,
        /// com a etiqueta do escritório) no lugar das linhas esquemáticas. Decisão do usuário em 06/11/2026.
        /// </summary>
        public bool LongitudinaisComoDetalhe { get; set; } = true;
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
        /// <summary>Planta de cada tipo de consolo, logo abaixo do topo. Título aceita {Consolo} e {Qtd} (consolos iguais).</summary>
        ConsoloPlanta,
        /// <summary>Corte transversal no meio do balanço de cada tipo de consolo, olhando para a face (CORTE A-A). Título aceita {Consolo}.</summary>
        ConsoloCorteTransversal,
        /// <summary>Corte pela alça de içamento de cima (plano da alça), com a saída, a largura e as pernas cotadas e a
        /// etiqueta da posição: o detalhe "ALÇA DE IÇAMENTO" da peça, no lugar da legenda fixa.</summary>
        AlcaIcamento
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
        /// <summary>Cota do comprimento total da peça nesta vista (o escritório põe só na VISTA 1).</summary>
        public bool CotaTotal { get; set; } = true;
        public bool MostrarArmadura { get; set; }
        /// <summary>Só elevação da armadura: desenho esquemático ("explodido") como o template do CAD — cada posição
        /// longitudinal numa linha com rótulo vertical e as zonas de estribos numa linha de cota; a armadura 3D e o
        /// concreto ficam ocultos na vista.</summary>
        public bool Esquematica { get; set; }
        /// <summary>
        /// Template de vista (PCEB-&lt;ELEMENTO&gt;-&lt;VISTA&gt;). Se não existir no projeto, é criado a partir da 1ª vista
        /// configurada por este item (escala e recorte ficam livres, para a prancha poder ajustar). Vazio = nenhum.
        /// </summary>
        public string Template { get; set; } = "";
    }

    /// <summary>RelacaoDoAco e ResumoDoAco (PP127); DobraDoAco = "bar bending schedule": por posição, forma do Revit
    /// (código e imagem da forma, se o tipo de forma tiver imagem), trechos A–E e comprimento.</summary>
    public enum TableKind { RelacaoDoAco, ResumoDoAco, DobraDoAco }

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
        public bool Ativa { get; set; } = true;
        /// <summary>DXF do detalhe (em cm, na escala da legenda), na pasta Padroes\Detalhes ou caminho completo. Se a legenda
        /// não existe no projeto, ela é criada uma vez a partir dele (ver LegendFactory).</summary>
        public string Arquivo { get; set; } = "";
        /// <summary>Escala da legenda criada do DXF (0 = a da legenda duplicada).</summary>
        public int Escala { get; set; }
        /// <summary>Tipo de vista (ex.: "AlcaIcamento") que ocupa este lugar no lugar da legenda: a vista da peça vai para a
        /// posição da legenda na região, e a legenda não entra.</summary>
        public string Vista { get; set; } = "";
    }

    // ----------------------------------------------------------------------
    // Pranchas de formas (D25, docs/04_arquitetura/pranchas_de_formas.md)

    /// <summary>
    /// Pranchas de formas: os papéis dos identificadores (família/tipo/arquivo), os filtros das peças, os nomes das tabelas, vistas e
    /// folhas e os valores usados nos parâmetros calculados. Caminhos de arquivo (famílias, carimbo) ficam só na cópia do usuário em
    /// %AppData% (padrões do escritório não vão para o Git). Campos de texto aceitam {Nivel} (nome do nível).
    /// </summary>
    public sealed class FormasSettings
    {
        /// <summary>Papéis dos identificadores: PilarLocacao, PilarMontagem, Viga, Laje, Fundacao.</summary>
        public Dictionary<string, TagRoleSpec> Identificadores { get; set; } = new Dictionary<string, TagRoleSpec>
        {
            ["PilarLocacao"] = new TagRoleSpec(),
            ["PilarMontagem"] = new TagRoleSpec(),
            ["Viga"] = new TagRoleSpec(),
            ["Laje"] = new TagRoleSpec(),
            ["Fundacao"] = new TagRoleSpec(),
        };
        /// <summary>Símbolo de sentido do vão nas lajes da montagem ("Família" ou "Família : Tipo"; vazio = o 1º carregado; sem
        /// nenhum carregado, não coloca).</summary>
        public string SentidoDoVao { get; set; } = "";
        public bool ColocarSentidoDoVao { get; set; } = true;

        /// <summary>Pilares que recebem dados, identificador e entram na tabela de cargas.</summary>
        public PieceFilterSpec FiltroPilares { get; set; } = new PieceFilterSpec();
        /// <summary>Vigas que recebem dados e identificador (as metálicas e os painéis alveolares modelados como viga ficam de fora).</summary>
        public PieceFilterSpec FiltroVigas { get; set; } = new PieceFilterSpec { PrefixoMarca = "V" };

        /// <summary>Nomes dos parâmetros de TIPO (ou de instância) com a largura e a altura da seção, na ordem de procura.</summary>
        public List<string> ParametrosLargura { get; set; } = new List<string> { "b", "B", "bw", "Largura" };
        public List<string> ParametrosAltura { get; set; } = new List<string> { "h", "H", "Altura" };

        /// <summary>Nível que é o zero dos níveis relativos (nome exato); vazio = ponto base do projeto. A cota absoluta vem sempre
        /// do ponto de levantamento (coordenadas compartilhadas).</summary>
        public string NivelZero { get; set; } = "";
        /// <summary>Peso próprio da laje = espessura × densidade (kgf/m³), só quando o parâmetro está vazio.</summary>
        public double DensidadeConcretoKgfM3 { get; set; } = 2500;
        /// <summary>Peso próprio (kgf/m²) por nome de tipo de piso (laje alveolar, nervurada...), no lugar da conta pela espessura.</summary>
        public Dictionary<string, double> PesoProprioPorTipo { get; set; } = new Dictionary<string, double>();

        /// <summary>Escala das plantas de formas.</summary>
        public int Escala { get; set; } = 50;
        /// <summary>Quanto (m) as vistas criadas pelo comando enxergam abaixo do nível (vigas penduradas, blocos); 0 = a da vista copiada.</summary>
        public double ProfundidadeVistaM { get; set; } = 2.0;
        /// <summary>Folga (m) do recorte das vistas criadas pelo comando em volta dos eixos e das peças.</summary>
        public double FolgaRecorteM { get; set; } = 1.0;
        /// <summary>Distância (mm de papel) da linha de cotas dos eixos à estrutura.</summary>
        public double AfastamentoCotasMm { get; set; } = 15;
        /// <summary>Altura (mm de papel) do texto do tipo de cota que o comando cria para os eixos.</summary>
        public double AlturaTextoCotaMm { get; set; } = 2.5;
        /// <summary>"Esquematicas" (padrão: fundações ainda não dimensionadas — nota na locação e identificador de bloco só com o nome)
        /// ou "Dimensionadas".</summary>
        public string Fundacoes { get; set; } = "Esquematicas";
        [JsonIgnore]
        public bool FundacoesEsquematicas => !string.Equals(Fundacoes?.Trim(), "Dimensionadas", StringComparison.OrdinalIgnoreCase);
        /// <summary>Parâmetros Sim/Não do tipo do identificador de bloco que ficam ligados nas fundações esquemáticas.</summary>
        public List<string> VisiveisEsquematica { get; set; } = new List<string> { "N" };
        /// <summary>Nota da locação com fundações esquemáticas.</summary>
        public string NotaFundacoesEsquematicas { get; set; } = "AS FUNDAÇÕES SÃO ESQUEMÁTICAS: DIMENSÕES E NÍVEIS DOS BLOCOS A CONFIRMAR NO PROJETO DE FUNDAÇÕES.";
        /// <summary>Tipo de texto das notas fixas (nome; vazio = o padrão do projeto).</summary>
        public string TipoTextoNotas { get; set; } = "";
        /// <summary>Distância (mm de papel) do identificador à peça.</summary>
        public double AfastamentoIdentificadorMm { get; set; } = 2;
        /// <summary>Tipo de cota das cotas de eixos (nome exato; vazio ou inexistente = o 1º tipo linear, com aviso).</summary>
        public string TipoDeCota { get; set; } = "";
        /// <summary>Tipos de texto das tabelas (nome exato; vazio = o do Revit).</summary>
        public string TextoTabelaCorpo { get; set; } = "";
        public string TextoTabelaCabecalho { get; set; } = "";
        public string TextoTabelaTitulo { get; set; } = "";

        public string NomeTabelaCargas { get; set; } = "PCEB - CARGAS NA FUNDAÇÃO";
        public string TituloTabelaCargas { get; set; } = "CARGAS NA FUNDAÇÃO";
        public string NomeTabelaLajes { get; set; } = "PCEB - LAJES - {Nivel}";
        public string TituloTabelaLajes { get; set; } = "LAJES - {Nivel}";

        public FormasSheetSpec Locacao { get; set; } = new FormasSheetSpec
        {
            NomeVista = "PCEB - LOCAÇÃO - {Nivel}",
            VistaBase = "Locação",
            NumeroFolha = "FOR-LOC",
            NomeFolha = "PLANTA DE LOCAÇÃO E CARGAS NA FUNDAÇÃO",
        };
        public FormasSheetSpec Montagem { get; set; } = new FormasSheetSpec
        {
            NomeVista = "PCEB - MONTAGEM - {Nivel}",
            VistaBase = "montagem",
            Notas = new List<string> { "FUROS NAS LAJES ALVEOLARES: PASSAR SOMENTE PELOS ALVÉOLOS, COM DIÂMETRO/LARGURA MÁXIMA IGUAL À LARGURA DO ALVÉOLO." },
            NumeroFolha = "FOR-{Nivel}",
            NomeFolha = "PLANTA DE MONTAGEM - {Nivel}",
        };

        /// <summary>Papel pelo nome; papel ausente no JSON = vazio (usa o identificador que houver).</summary>
        public TagRoleSpec Role(string name) =>
            Identificadores != null && Identificadores.TryGetValue(name, out TagRoleSpec r) && r != null ? r : new TagRoleSpec();
    }

    /// <summary>Família de identificador de um papel. As famílias são feitas à mão (a API não cria rótulos) lendo os parâmetros SD_.</summary>
    public sealed class TagRoleSpec
    {
        /// <summary>Nome da família no projeto (vazio = qualquer identificador da categoria).</summary>
        public string Familia { get; set; } = "";
        /// <summary>Nome do tipo (vazio = o 1º da família).</summary>
        public string Tipo { get; set; } = "";
        /// <summary>.rfa carregado quando a família não está no projeto (caminho local, fora do Git).</summary>
        public string Arquivo { get; set; } = "";
        /// <summary>Ajuste fino (mm de papel, direita/para cima) depois do posicionamento automático — a caixa de algumas famílias
        /// inclui rótulos escondidos.</summary>
        public double DeslocamentoXMm { get; set; }
        public double DeslocamentoYMm { get; set; }
    }

    /// <summary>Quais peças da categoria entram (dados, identificador, tabela).</summary>
    public sealed class PieceFilterSpec
    {
        /// <summary>Material estrutural da família (Concrete, PrecastConcrete, Steel...); vazio = todos.</summary>
        public List<string> Materiais { get; set; } = new List<string> { "Concrete", "PrecastConcrete" };
        /// <summary>Começo da marca (sem diferenciar maiúsculas); vazio = qualquer.</summary>
        public string PrefixoMarca { get; set; } = "";
        /// <summary>Trechos do nome da família; vazio = qualquer família.</summary>
        public List<string> Familias { get; set; } = new List<string>();
    }

    /// <summary>Vista e folha de um tipo de planta de formas.</summary>
    public sealed class FormasSheetSpec
    {
        public string NomeVista { get; set; } = "";
        /// <summary>Trecho do nome da planta do nível que serve de base (copiada) quando o comando cria a vista.</summary>
        public string VistaBase { get; set; } = "";
        /// <summary>Notas fixas da folha (texto do comando, um bloco por item), na região "Notas" (ou "Legendas").</summary>
        public List<string> Notas { get; set; } = new List<string>();
        public string NumeroFolha { get; set; } = "";
        public string NomeFolha { get; set; } = "";
        /// <summary>Legendas fixas do projeto (detalhes, convenções, notas), pelo nome; as que não existem são avisadas.</summary>
        public List<LegendSpec> Legendas { get; set; } = new List<LegendSpec>();
        /// <summary>Regiões da folha (padrão: "Planta", "Tabelas", "Legendas" de <see cref="PresentationStandard.Regioes"/>).</summary>
        public string RegiaoPlanta { get; set; } = "Planta";
        public string RegiaoTabelas { get; set; } = "Tabelas";
    }
}
