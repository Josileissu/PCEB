using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DEB.Core.Configuracao.Projeto;

public enum TipoCampo { Numero, Opcao, Texto, Unidade, Bitolas }

/// <summary>Grupo da tela de configuração (D45).</summary>
public sealed class GrupoConfiguracao
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Descricao { get; set; } = "";
}

/// <summary>
/// Definição de um campo da configuração do projeto (D45): rótulo, explicação, item da norma, valor de partida (texto
/// invariante: ponto decimal) e, quando houver, a faixa da norma. Valor fora da faixa é aceito com justificativa.
/// </summary>
public sealed class CampoConfiguracao
{
    public string Id { get; set; } = "";
    public string Grupo { get; set; } = "";
    public string Subgrupo { get; set; } = "";
    public string Rotulo { get; set; } = "";
    public string Explicacao { get; set; } = "";
    public string Referencia { get; set; } = "";
    public TipoCampo Tipo { get; set; }
    public string Padrao { get; set; } = "";
    public IReadOnlyList<string> Opcoes { get; set; } = Array.Empty<string>();
    /// <summary>Unidade do valor (texto da tela) — os campos numéricos do catálogo estão na unidade base do núcleo.</summary>
    public string Unidade { get; set; } = "";
    /// <summary>Para os campos de unidade: a grandeza que eles escolhem.</summary>
    public Grandeza? Grandeza { get; set; }
    /// <summary>Faixa da norma (null = sem limite). Fora dela, o valor exige justificativa (D45).</summary>
    public double? Min { get; set; }
    public double? Max { get; set; }
}

/// <summary>Bitola disponível e como chega à fábrica (D45): rolo, barra (comprimento comercial) ou os dois.</summary>
public sealed class BitolaDisponivel
{
    public double Phi { get; set; }
    public string Aco { get; set; } = "CA-50";
    public Fornecimento Fornecimento { get; set; }
    public bool Usar { get; set; } = true;
}

public enum Fornecimento { Rolo, Barra, RoloEBarra }

/// <summary>
/// Catálogo único dos campos da configuração do projeto (D45): a tela (WebView2) e o cálculo leem daqui. Os valores de partida
/// são os do PCEB, revisados pelo escritório no nível "Escritório".
/// </summary>
public static class CatalogoConfiguracao
{
    public const string PreFabricado = "Pré-fabricado (controle de fábrica)", PreMoldado = "Pré-moldado";

    public static IReadOnlyList<GrupoConfiguracao> Grupos { get; } = new[]
    {
        G("normas", "Normas e segurança", "Coeficientes de ponderação e de ajustamento; o cálculo da viga, do consolo e do pilar lê daqui."),
        G("unid", "Unidades", "Como cada grandeza aparece nas telas, no memorial e na prancha; o cálculo interno é na unidade base."),
        G("materiais", "Materiais", "Valores de partida das peças; a peça pode ter exceção."),
        G("durab", "Durabilidade e cobrimentos", "Classe de agressividade e cobrimentos por elemento."),
        G("acoes", "Ações e fases", "Fases transitórias e forças nas ligações."),
        G("solo", "Solo e contenções", "Aterro, solo de fundação e segurança dos muros de arrimo (D47)."),
        G("analise", "Análise", "Modelo estrutural e efeitos de 2ª ordem."),
        G("dim", "Dimensionamento", "Modelos de cálculo."),
        G("det", "Detalhamento", "Bitolas, fornecimento e regras de desenho da armadura."),
        G("apres", "Apresentação", "Idioma e números na prancha."),
    };

    /// <summary>
    /// Unidades de partida do PCEB (24/11/2026: as do escritório do usuário passaram a ser o padrão): sistema técnico em tf e kgf, cm
    /// nas cotas do modelo. As demais grandezas ficam no primeiro símbolo da lista.
    /// </summary>
    public static IReadOnlyDictionary<Grandeza, string> UnidadesPadrao { get; } = new Dictionary<Grandeza, string>
    {
        [Grandeza.Forca] = "tf", [Grandeza.Cortante] = "tf", [Grandeza.Normal] = "tf", [Grandeza.Momento] = "tf·m", [Grandeza.Torcao] = "tf·m",
        [Grandeza.CargaLinear] = "kgf/m", [Grandeza.CargaSuperficie] = "kgf/m²", [Grandeza.PesoEspecifico] = "kgf/m³", [Grandeza.Comprimento] = "cm",
        [Grandeza.MomentoDistribuido] = "tf·m/m", [Grandeza.RigidezMola] = "tf/m", [Grandeza.RigidezRotacao] = "tf·m/rad", [Grandeza.Idade] = "dias",
    };

    /// <summary>
    /// Grandeza de um campo numérico (a tela mostra e recebe o valor na unidade escolhida no grupo Unidades; o catálogo guarda na
    /// unidade do campo). null = sem conversão (fatores, contagens, cm de malha, mm de abertura de fissura…).
    /// </summary>
    public static Grandeza? GrandezaDoCampo(CampoConfiguracao c)
    {
        if (c.Tipo != TipoCampo.Numero) return null;
        if (c.Id.StartsWith("durab.cob")) return Projeto.Grandeza.Cobrimento;
        return c.Unidade switch
        {
            "kN/m³" => Projeto.Grandeza.PesoEspecifico,
            "MPa" => Projeto.Grandeza.Tensao,
            "kN/m²" => Projeto.Grandeza.CargaSuperficie,
            "kN/m" => Projeto.Grandeza.CargaLinear,
            _ => (Grandeza?)null,
        };
    }

    public static IReadOnlyList<BitolaDisponivel> BitolasPadrao { get; } = new[]
    {
        new BitolaDisponivel { Phi = 5, Aco = "CA-60", Fornecimento = Fornecimento.Rolo },
        new BitolaDisponivel { Phi = 6.3, Fornecimento = Fornecimento.Rolo },
        new BitolaDisponivel { Phi = 8, Fornecimento = Fornecimento.RoloEBarra },
        new BitolaDisponivel { Phi = 10, Fornecimento = Fornecimento.RoloEBarra },
        new BitolaDisponivel { Phi = 12.5, Fornecimento = Fornecimento.Barra },
        new BitolaDisponivel { Phi = 16, Fornecimento = Fornecimento.Barra },
        new BitolaDisponivel { Phi = 20, Fornecimento = Fornecimento.Barra },
        new BitolaDisponivel { Phi = 25, Fornecimento = Fornecimento.Barra },
        new BitolaDisponivel { Phi = 32, Fornecimento = Fornecimento.Barra, Usar = false },
    };

    private static readonly List<CampoConfiguracao> _campos = Montar();
    private static readonly Dictionary<string, CampoConfiguracao> _porId = _campos.ToDictionary(c => c.Id);

    public static IReadOnlyList<CampoConfiguracao> Campos => _campos;
    public static CampoConfiguracao Campo(string id) =>
        _porId.TryGetValue(id, out var c) ? c : throw new KeyNotFoundException($"Campo de configuração desconhecido: {id}");
    public static bool Existe(string id) => _porId.ContainsKey(id);

    /// <summary>Id do campo que escolhe a unidade de uma grandeza.</summary>
    public static string IdUnidade(Grandeza g) => "unid." + g;

    private static GrupoConfiguracao G(string id, string nome, string desc) => new GrupoConfiguracao { Id = id, Nome = nome, Descricao = desc };

    private static List<CampoConfiguracao> Montar()
    {
        var l = new List<CampoConfiguracao>();
        string sub = "";
        void S(string s) => sub = s;
        void N(string grupo, string id, string rot, double padrao, string un, string refer, string expl, double? min = null, double? max = null) =>
            l.Add(new CampoConfiguracao { Id = grupo + "." + id, Grupo = grupo, Subgrupo = sub, Rotulo = rot, Padrao = padrao.ToString(CultureInfo.InvariantCulture),
                Unidade = un, Referencia = refer, Explicacao = expl, Tipo = TipoCampo.Numero, Min = min, Max = max });
        void O(string grupo, string id, string rot, string[] ops, string refer, string expl, int padrao = 0) =>
            l.Add(new CampoConfiguracao { Id = grupo + "." + id, Grupo = grupo, Subgrupo = sub, Rotulo = rot, Opcoes = ops, Padrao = ops[padrao],
                Referencia = refer, Explicacao = expl, Tipo = TipoCampo.Opcao });

        // ---- normas e segurança
        S("Normas");
        O("normas", "nbr6118", "Concreto", new[] { "NBR 6118:2023" }, "—", "Edição da norma de concreto usada nas verificações.");
        O("normas", "nbr9062", "Pré-moldado", new[] { "NBR 9062:2017" }, "—", "Norma de pré-moldados: consolos, dentes, ligações, içamento.");
        S("Ponderação (ELU)");
        O("normas", "tipoElemento", "Tipo de elemento", new[] { PreMoldado, PreFabricado }, "NBR 9062 3.8, 3.9 e 8.1",
          "Pré-fabricado (3.9, controle de qualidade de fábrica) admite γc = 1,3 e γs = 1,10 (8.1) e γn menor nos consolos (7.3.1.1). " +
          "Pré-moldado (3.8): γc = 1,4 e γs = 1,15.");
        N("normas", "gamaC", "γc · concreto", 1.4, "", "NBR 6118 12.4.1 · NBR 9062 8.1", "Ponderação da resistência do concreto.", 1.3, null);
        N("normas", "gamaS", "γs · aço", 1.15, "", "NBR 6118 12.4.1 · NBR 9062 8.1", "Ponderação da resistência do aço.", 1.10, null);
        N("normas", "gamaG", "γg · permanente", 1.4, "", "NBR 6118 Tab. 11.1", "Ações permanentes, combinação normal.", 1.3, null);
        N("normas", "gamaQ", "γq · variável", 1.4, "", "NBR 6118 Tab. 11.1", "Ações variáveis, combinação normal.", 1.4, null);
        N("normas", "gamaNPerm", "γn · consolos e dentes (permanente preponderante)", 1.1, "", "NBR 9062 7.3.1.1",
          "Multiplica o γf nas ligações por consolo e dente: pré-fabricado 1,0; pré-moldado 1,1.", 1.0, null);
        N("normas", "gamaNVar", "γn · consolos e dentes (caso contrário)", 1.2, "", "NBR 9062 7.3.1.1",
          "Pré-fabricado 1,1; pré-moldado 1,2.", 1.1, null);
        S("Estruturas de aço");
        O("normas", "nbr8800", "Aço", new[] { "NBR 8800:2024" }, "—", "Norma das estruturas de aço (D48).");
        N("normas", "gamaA1", "γa1 · escoamento e instabilidade", 1.10, "", "NBR 8800 Tab. 3", "Resistência do aço ao escoamento, flambagem e instabilidade.", 1.10, null);
        N("normas", "gamaA2", "γa2 · ruptura", 1.35, "", "NBR 8800 Tab. 3", "Ruptura da seção líquida, parafusos e soldas.", 1.35, null);
        S("Combinações");
        O("normas", "uso", "Uso da edificação", new[] { "Residencial", "Comercial / escritórios", "Bibliotecas, garagens e oficinas" }, "NBR 6118 Tab. 11.2",
          "Escolhe ψ0, ψ1 e ψ2.", 1);
        N("normas", "psi0", "ψ0", 0.7, "", "NBR 6118 Tab. 11.2", "Combinação rara / ações secundárias.", 0, 1);
        N("normas", "psi1", "ψ1 · frequente", 0.6, "", "NBR 6118 Tab. 11.2", "Fissuração (combinação frequente).", 0, 1);
        N("normas", "psi2", "ψ2 · quase permanente", 0.4, "", "NBR 6118 Tab. 11.2", "Flecha (combinação quase permanente).", 0, 1);

        // ---- unidades: uma por grandeza
        var nomes = new Dictionary<Grandeza, (string Sub, string Rot, string Ex)>
        {
            [Grandeza.Comprimento] = ("Geometria", "Comprimento (vãos, cotas do modelo)", "Vãos, níveis, posições no modelo."),
            [Grandeza.Secao] = ("Geometria", "Dimensões de seção", "Largura, altura, mesa, consolo."),
            [Grandeza.Cobrimento] = ("Geometria", "Cobrimento e espaçamentos", "Cobrimentos e espaçamento de estribos."),
            [Grandeza.Diametro] = ("Geometria", "Diâmetro das barras", "Bitolas."),
            [Grandeza.Deslocamento] = ("Geometria", "Deslocamentos e flechas", "Flechas, contraflecha, deslocamentos."),
            [Grandeza.Angulo] = ("Geometria", "Ângulos", "Inclinação de bielas."),
            [Grandeza.Rotacao] = ("Geometria", "Rotações (análise)", "Rotação nos apoios e nas ligações."),
            [Grandeza.Area] = ("Propriedades da seção", "Área de concreto", "Área da seção."),
            [Grandeza.AreaAco] = ("Propriedades da seção", "Área de aço", "As, As'."),
            [Grandeza.AreaAcoPorMetro] = ("Propriedades da seção", "Área de aço por metro", "Estribos, costura, malhas."),
            [Grandeza.Inercia] = ("Propriedades da seção", "Momento de inércia", "I, J, Ieq."),
            [Grandeza.ModuloResistente] = ("Propriedades da seção", "Módulo resistente", "W."),
            [Grandeza.Taxa] = ("Propriedades da seção", "Taxa de armadura", "ρ."),
            [Grandeza.Forca] = ("Forças e esforços", "Força concentrada e reações", "Cargas aplicadas, reações, Hd."),
            [Grandeza.Cortante] = ("Forças e esforços", "Esforço cortante", "V e VRd."),
            [Grandeza.Normal] = ("Forças e esforços", "Esforço normal", "N dos pilares."),
            [Grandeza.Momento] = ("Forças e esforços", "Momento fletor", "M e MRd."),
            [Grandeza.Torcao] = ("Forças e esforços", "Momento de torção", "T e TRd."),
            [Grandeza.CargaLinear] = ("Forças e esforços", "Carga distribuída linear", "Cargas em vigas."),
            [Grandeza.CargaSuperficie] = ("Forças e esforços", "Carga distribuída de superfície", "Sobrecargas de laje, vento."),
            [Grandeza.MomentoDistribuido] = ("Forças e esforços", "Momento distribuído", "Lajes, torção distribuída da aba."),
            [Grandeza.RigidezMola] = ("Forças e esforços", "Rigidez de mola", "Molas de apoio e de ligação."),
            [Grandeza.RigidezRotacao] = ("Forças e esforços", "Rigidez à rotação", "Ligações semirrígidas, base no cálice."),
            [Grandeza.Tensao] = ("Materiais", "Tensões e resistências", "fck, fyd, σ, τ."),
            [Grandeza.ModuloElasticidade] = ("Materiais", "Módulo de elasticidade", "Eci, Ecs, Es."),
            [Grandeza.PesoEspecifico] = ("Materiais", "Peso específico", "Concreto, revestimentos."),
            [Grandeza.Massa] = ("Materiais", "Massa e peso de aço", "Relação do aço, peso da peça."),
            [Grandeza.Temperatura] = ("Materiais", "Temperatura", "Variação térmica."),
            [Grandeza.Aceleracao] = ("Dinâmica e tempo", "Aceleração", "Sismo, vibração."),
            [Grandeza.Velocidade] = ("Dinâmica e tempo", "Velocidade", "Vento (NBR 6123)."),
            [Grandeza.Frequencia] = ("Dinâmica e tempo", "Frequência", "Modos de vibração (NBR 6118 23.3)."),
            [Grandeza.Tempo] = ("Dinâmica e tempo", "Tempo de análise", "Análise no tempo."),
            [Grandeza.Idade] = ("Dinâmica e tempo", "Idade e fluência", "Idade de carregamento, fluência (NBR 6118 17.3.2.1.2)."),
        };
        foreach (Grandeza g in Enum.GetValues(typeof(Grandeza)))
        {
            var (s, rot, ex) = nomes[g];
            var ops = Unidades.Opcoes(g);
            string padrao = UnidadesPadrao.TryGetValue(g, out string up) && ops.Contains(up) ? up : g == Grandeza.ModuloElasticidade ? "GPa" : ops[0];
            l.Add(new CampoConfiguracao { Id = IdUnidade(g), Grupo = "unid", Subgrupo = s, Rotulo = rot, Explicacao = ex, Tipo = TipoCampo.Unidade,
                Grandeza = g, Opcoes = ops, Padrao = padrao, Referencia = "—" });
        }

        // ---- materiais
        S("");
        N("materiais", "fck", "fck das peças", 40, "MPa", "NBR 6118 8.2", "Concreto das peças pré-moldadas.", 20, 90);
        N("materiais", "fckCapa", "fck da capa", 30, "MPa", "NBR 6118 8.2", "Concreto moldado no local (2º estágio).", 20, 90);
        N("materiais", "fckMuro", "fck dos muros e contenções", 30, "MPa", "NBR 6118 8.2 e Tab. 7.1", "Concreto moldado no local das contenções.", 20, 90);
        N("materiais", "fcj", "fcj no saque", 20, "MPa", "NBR 9062 5.3", "Resistência na desforma e no içamento na fábrica.", 0, 90);
        O("materiais", "aco", "Aço das barras", new[] { "CA-50", "CA-60" }, "NBR 7480", "Aço das barras longitudinais.");
        O("materiais", "agregado", "Agregado graúdo (αE)", new[] { "Granito/gnaisse · 1,0", "Basalto/diabásio · 1,2", "Calcário · 0,9", "Arenito · 0,7" },
          "NBR 6118 8.2.8", "Corrige o módulo de elasticidade.");
        O("materiais", "acoPerfil", "Aço dos perfis", DEB.Core.Elementos.Metalica.AcoEstrutural.Lista.Select(a => a.Nome).ToArray(), "NBR 8800 Anexo A",
          "Aço dos perfis metálicos quando o tipo do Revit não traz o material.");
        N("materiais", "pesoEspecifico", "Peso específico do concreto armado", 25, "kN/m³", "NBR 6120", "Peso próprio das peças.", 20, 30);
        S("Pesos específicos dos aços");
        foreach (var (id, rot, refer, ex) in new[]
        {
            ("CA25", "CA-25", "NBR 7480", "Barras de armadura passiva."), ("CA50", "CA-50", "NBR 7480", "Barras de armadura passiva."),
            ("CA60", "CA-60", "NBR 7480", "Fios de armadura passiva."), ("CP175", "CP-175", "NBR 7483", "Cordoalhas e fios de protensão."),
            ("CP190", "CP-190", "NBR 7483", "Cordoalhas e fios de protensão."), ("CP210", "CP-210", "NBR 7483", "Cordoalhas e fios de protensão."),
            ("CP220", "CP-220", "NBR 7483 (conferir a edição)", "Cordoalhas e fios de protensão."),
            ("Perfis", "Perfis e chapas de aço estrutural", "NBR 8800 4.9.4", "Perfis, chapas e parafusos das estruturas de aço."),
        })
            N("materiais", "peso" + id, "Peso específico · " + rot, 78.5, "kN/m³", refer, ex + " Massa específica de 7850 kg/m³.", 70, 85);
        S("");

        // ---- durabilidade
        O("durab", "caa", "Classe de agressividade", new[] { "I", "II", "III", "IV" }, "NBR 6118 Tab. 6.1", "fck mínimo, cobrimento e wk.", 1);
        O("durab", "controleRigido", "Controle rígido (Δc = 5 mm)", new[] { "Sim", "Não" }, "NBR 6118 7.4.7.4", "Reduz o cobrimento em 5 mm com controle rígido de execução.");
        N("durab", "cobPilar", "Cobrimento · pilar", 0.030, "m", "NBR 6118 Tab. 7.2", "Cobrimento nominal do pilar.", 0.015, null);
        N("durab", "cobViga", "Cobrimento · viga", 0.025, "m", "NBR 6118 Tab. 7.2", "Cobrimento nominal da viga.", 0.015, null);
        N("durab", "cobLaje", "Cobrimento · laje", 0.020, "m", "NBR 6118 Tab. 7.2", "Cobrimento nominal da laje (CAA II: 25 mm; com controle rígido: 20 mm).", 0.015, null);
        N("durab", "cobConsolo", "Cobrimento · consolo", 0.025, "m", "NBR 9062 7.3.3", "Cobrimento do consolo.", 0.015, null);
        N("durab", "cobMuro", "Cobrimento · em contato com o solo", 0.030, "m", "NBR 6118 Tab. 7.2",
          "Muros e sapatas em contato com o solo (CAA I e II: 30 mm; III: 40 mm; IV: 50 mm).", 0.030, null);
        N("durab", "wk", "Abertura de fissura limite", 0.3, "mm", "NBR 6118 Tab. 13.4", "wk da classe de agressividade.", 0, 0.4);

        // ---- ações e fases
        N("acoes", "coefDinamico", "Coeficiente dinâmico do içamento", 1.3, "", "NBR 9062 5.3.3", "Majora o peso próprio no manuseio.", 1.3, null);
        O("acoes", "apoioPadrao", "Apoio padrão das vigas (Hd/Fd)", new[] { "Elastômero · 0,16", "Argamassa · 0,5", "Junta seca · 0,8", "Chapas não soldadas · 0,25",
          "Concreto e chapa · 0,4", "PTFE · 0,08" }, "NBR 9062 7.3.9", "Força horizontal na ligação pela vertical, sem cálculo da rigidez.");
        N("acoes", "lajeG", "Laje · carga permanente além do peso próprio", 1.0, "kN/m²", "NBR 6120", "Revestimento e enchimento, quando o piso não tem PCEB_Carga permanente (kN/m²).", 0, null);
        N("acoes", "lajeQ", "Laje · carga variável", 2.0, "kN/m²", "NBR 6120 Tab. 10", "Sobrecarga de uso, quando o piso não tem PCEB_Carga variável (kN/m²).", 0, null);
        N("acoes", "fracaoAlveolar", "Laje alveolar · fração maciça (peso)", 0.55, "", "catálogo do fabricante", "Peso próprio = 25 kN/m³ × h × fração (vazios dos alvéolos), quando o piso não tem PCEB_Peso próprio (kN/m²).", 0.3, 1);
        N("acoes", "fracaoTrelicada", "Laje treliçada · fração maciça (peso)", 0.55, "", "catálogo do fabricante", "Peso próprio = 25 kN/m³ × h × fração (enchimento de EPS ou cerâmico).", 0.3, 1);
        N("acoes", "fracaoVigota", "Laje de vigota protendida · fração maciça (peso)", 0.6, "", "catálogo do fabricante", "Peso próprio = 25 kN/m³ × h × fração (lajotas).", 0.3, 1);
        N("acoes", "gElastomero", "Módulo G do elastômero", 1.0, "MPa", "NBR 9062 7.2.3 · El Debs (2017) 3.3",
          "Rigidez do aparelho de apoio na análise (D46): Kh = G·a·b/t. Elastômero de dureza 60 Shore A: cerca de 1 MPa.", 0.5, 2);
        N("acoes", "hdMin", "Hd mínimo (× Vd)", 0.2, "", "El Debs (2017) Quadro 3.2", "Piso da força horizontal no consolo.", 0.2, null);
        N("acoes", "posCargaConsolo", "Posição da carga no consolo (× comprimento)", 0.75, "", "El Debs (2017) 3.4.3", "Na falta de cálculo, a reação a ¾ do consolo.", 0.5, 1);

        // ---- solo e contenções (D47)
        S("Aterro (solo contido)");
        N("solo", "gama", "Peso específico do aterro", 18, "kN/m³", "sondagem / ensaio", "Solo acima do nível d'água.", 14, 23);
        N("solo", "gamaSat", "Peso específico saturado", 20, "kN/m³", "sondagem / ensaio", "Solo abaixo do nível d'água (γ submerso = γsat − 10).", 16, 24);
        N("solo", "phi", "Ângulo de atrito do aterro (φ)", 30, "°", "ensaio ou correlação com o SPT", "Aterro compactado e drenado; coesão desprezada.", 20, 45);
        N("solo", "delta", "Atrito solo-muro (δ)", 20, "°", "Coulomb", "Usado só no método de Coulomb (comum: 2/3 de φ).", 0, 30);
        N("solo", "sobrecarga", "Sobrecarga sobre o aterro", 10, "kN/m²", "NBR 6120", "Tráfego leve ou uso: variável, entra só onde desfavorece.", 0, null);
        O("solo", "metodo", "Empuxo ativo", new[] { "Rankine", "Coulomb" }, "—", "Rankine: empuxo paralelo ao terreno no plano pelo fim do talão. Coulomb: com atrito δ.");
        S("Solo de fundação");
        N("solo", "gamaFund", "Peso específico", 18, "kN/m³", "sondagem", "Solo sob a sapata do muro.", 14, 23);
        N("solo", "phiFund", "Ângulo de atrito (φ)", 30, "°", "sondagem", "Capacidade de carga e deslizamento.", 0, 45);
        N("solo", "cFund", "Coesão (c)", 0, "kPa", "sondagem", "Coesão efetiva do solo de fundação.", 0, null);
        N("solo", "sigmaAdm", "Tensão admissível (0 = só calcular)", 0, "kPa", "NBR 6122 6.3", "Da sondagem, quando houver: confere a tensão máxima da base.", 0, null);
        N("solo", "k1", "Fator do atrito na base (k1·φ)", 0.67, "", "Das, Principles of Foundation Engineering 13.6", "tg(k1·φ) no deslizamento.", 0.5, 1);
        N("solo", "k2", "Fator da adesão na base (k2·c)", 0.67, "", "Das, Principles of Foundation Engineering 13.6", "Adesão no deslizamento.", 0.5, 1);
        N("solo", "fracaoPassivo", "Fração do empuxo passivo na frente", 0, "", "boa prática", "0 = desprezado (o terreno da frente pode ser escavado).", 0, 1);
        N("solo", "fracaoDente", "Fração do passivo no dente", 1.0, "", "Das, Principles of Foundation Engineering 13.6",
          "Passivo na face do dente sob a sapata (abaixo da base, não se escava); reduza se o deslocamento para mobilizá-lo não for aceitável.", 0, 1);
        S("Segurança");
        N("solo", "fsTombamento", "FS ao tombamento", 2.0, "", "NBR 11682", "Muros de arrimo.", 1.5, null);
        N("solo", "fsDeslizamento", "FS ao deslizamento", 1.5, "", "NBR 11682", "Muros de arrimo.", 1.5, null);
        N("solo", "fsCapacidade", "FS à capacidade de carga", 3.0, "", "NBR 6122 Tab. 1", "Fundação direta, método analítico (fator global).", 3.0, null);

        // ---- análise
        S("");
        O("analise", "modelo", "Modelo", new[] { "Elemento isolado", "Pórtico 2D", "Estrutura 3D (motor externo)" }, "D44", "A estrutura completa entra na etapa 4 da D44.");
        N("analise", "malhaLaje", "Malha da grelha das lajes", 50, "cm", "D46", "Espaçamento das faixas da grelha (menor = mais preciso e mais lento).", 15, 200);
        O("analise", "segundaOrdem", "2ª ordem local do pilar", new[] { "Pilar-padrão com rigidez κ", "Pilar-padrão com curvatura aproximada", "Momento-curvatura" },
          "NBR 6118 15.8.3", "Método para os pilares.");

        S("Estruturas de aço");
        O("analise", "baseMetalica", "Base dos pilares metálicos", new[] { "Engastada", "Rotulada" }, "NBR 8800 6.7",
          "Apoio da base quando o pilar não tem peça embaixo. Rotulada exige contraventamento para a estrutura ficar estável.");
        O("analise", "ligacaoMetalica", "Ligação viga–pilar metálica", new[] { "Flexível", "Rígida" }, "NBR 8800 6.1.2",
          "Flexível: a viga chega rotulada (cantoneiras, chapa de alma); rígida: transmite momento (chapa de topo estendida, soldada).");
        O("analise", "tipoFlexivel", "Ligação flexível padrão", new[] { "Chapa de alma", "Cantoneira dupla" }, "D49",
          "Tipo da biblioteca de ligações usado nas pontas flexíveis das vigas de aço (o primeiro modelo do tipo em vigor).");
        S("");

        // ---- dimensionamento
        N("dim", "flechaAco", "Flecha das vigas de aço (L/…)", 350, "", "NBR 8800 Anexo B, Tab. B.1", "Vigas de piso: L/350; de cobertura: L/250.", 250, null);
        O("dim", "modeloCortante", "Cortante e torção", new[] { "Modelo I (θ = 45°)" }, "NBR 6118 17.4.2 e 17.7.2.1", "O mesmo θ vale para a torção.");
        O("dim", "consoloCurto", "Consolo curto", new[] { "El Debs (2017) · τ ≤ 0,2 fcd", "NBR 9062 7.3" }, "NBR 9062 7.3.2",
          "A NBR 9062 não fixa a largura da biela; El Debs recomenda τwd ≤ 0,2 fcd para 0,4 ≤ a/d ≤ 1.");

        // ---- detalhamento
        S("Bitolas e fornecimento");
        l.Add(new CampoConfiguracao { Id = "det.bitolas", Grupo = "det", Subgrupo = sub, Rotulo = "Bitolas disponíveis", Tipo = TipoCampo.Bitolas,
            Padrao = TextoBitolas(BitolasPadrao), Referencia = "NBR 7480",
            Explicacao = "Quais bitolas o programa pode propor e como chegam à fábrica: rolo (sem limite de comprimento), barra (comprimento comercial, com emenda) ou os dois." });
        N("det", "compBarra", "Comprimento da barra comercial", 12, "m", "NBR 7480", "Vale para as bitolas em barra; maiores pedem emenda.", 0, null);
        N("det", "corteRolo", "Comprimento máximo cortado do rolo", 14, "m", "—", "Limite de manuseio da barra cortada do rolo.", 0, null);
        S("Regras");
        O("det", "gancho", "Gancho dos estribos", new[] { "135°", "90°" }, "NBR 6118 9.4.6 · D30", "135° é o padrão do PCEB e é exigido na torção (18.3.4).");

        // ---- apresentação
        S("");
        O("apres", "idioma", "Idioma das pranchas", new[] { "Português / inglês", "Português" }, "—", "Pranchas bilíngues como o padrão do escritório.");
        O("apres", "separador", "Separador decimal", new[] { "Vírgula", "Ponto" }, "—", "Nas telas, no memorial e na prancha.");
        return l;
    }

    // ---- bitolas como texto: "5|CA-60|Rolo|1; 6.3|CA-50|Rolo|1; ..."
    public static string TextoBitolas(IEnumerable<BitolaDisponivel> l) =>
        string.Join("; ", l.Select(b => $"{b.Phi.ToString(CultureInfo.InvariantCulture)}|{b.Aco}|{b.Fornecimento}|{(b.Usar ? 1 : 0)}"));

    public static List<BitolaDisponivel> LerBitolas(string texto)
    {
        var r = new List<BitolaDisponivel>();
        foreach (string p in (texto ?? "").Split(';'))
        {
            string[] c = p.Trim().Split('|');
            if (c.Length < 4 || !double.TryParse(c[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double phi)) continue;
            if (!Enum.TryParse(c[2], out Fornecimento f)) f = Fornecimento.Barra;
            r.Add(new BitolaDisponivel { Phi = phi, Aco = c[1], Fornecimento = f, Usar = c[3] == "1" });
        }
        return r;
    }
}
