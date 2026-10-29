using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Armadura.Core.Configuracao;
using Armadura.Core.Modelo;

namespace Armadura.Core.Templates;

/// <summary>
/// Elementos estruturais com padrão de armação do escritório (pedido do usuário, 2026-10-27). Cada
/// categoria terá um ou mais templates; o padrão real virá dos PDFs/DWGs que o usuário enviar
/// (pasta <c>info_auxiliar/templates/&lt;categoria&gt;/</c>).
/// </summary>
public enum CategoriaElemento
{
    BaseCircularReservatorio,
    BaseRetangularReservatorio,
    Bloco1Estaca,
    Bloco2Estacas,
    PilarPreMoldado,
    VigaPreMoldada,
    PilarMoldadoInLoco,
    VigaMoldadaInLoco,
    Laje,
}

public enum TipoParametro { Numero, Inteiro, Texto, Opcao, Bitola, SimNao }

/// <summary>Dado de entrada de um template (dimensão, cobrimento, bitola, opção…).</summary>
public sealed class ParametroTemplate
{
    /// <summary>Chave usada pelo template (sem espaços).</summary>
    public string Nome { get; set; } = "";
    /// <summary>Texto mostrado ao usuário.</summary>
    public string Rotulo { get; set; } = "";
    public string Grupo { get; set; } = "Geometria";
    public string Unidade { get; set; } = "";
    public string Descricao { get; set; } = "";
    public TipoParametro Tipo { get; set; } = TipoParametro.Numero;
    /// <summary>Valor padrão (número em cultura invariante, ou texto).</summary>
    public string Padrao { get; set; } = "";
    public double Minimo { get; set; } = double.NegativeInfinity;
    public double Maximo { get; set; } = double.PositiveInfinity;
    /// <summary>Valores possíveis (Tipo = Opcao).</summary>
    public List<string> Opcoes { get; set; } = new List<string>();
}

/// <summary>Valores escolhidos pelo usuário, por nome de parâmetro.</summary>
public sealed class ValoresTemplate
{
    private readonly Dictionary<string, string> _v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public ValoresTemplate() { }
    public ValoresTemplate(IEnumerable<ParametroTemplate> parametros) { foreach (var p in parametros) _v[p.Nome] = p.Padrao; }

    public string this[string nome] { get => _v.TryGetValue(nome, out var s) ? s : ""; set => _v[nome] = value ?? ""; }

    public double Numero(string nome) =>
        double.TryParse(this[nome].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

    public int Inteiro(string nome) => (int)Math.Round(Numero(nome));
    public bool SimNao(string nome) => this[nome].Equals("Sim", StringComparison.OrdinalIgnoreCase) || this[nome] == "1";
    public IReadOnlyDictionary<string, string> Todos => _v;
}

/// <summary>Uma armadura gerada (coordenadas em cm, relativas ao ponto de inserção, y para cima).</summary>
public sealed class BarraTemplate
{
    /// <summary>Chave da barra no resultado (faixas e indicações apontam para ela).</summary>
    public string Nome { get; set; } = "";
    /// <summary>Grupo VAR: comprimento externo de cada barra (a polilinha é a representativa).</summary>
    public List<double> ComprimentosVar { get; set; } = new List<double>();
    /// <summary>Segmentos marcados como VAR (o trecho que muda de barra para barra).</summary>
    public HashSet<int> SegmentosVar { get; set; } = new HashSet<int>();
    public int Repeticoes { get; set; } = 1;
    public bool Corrido { get; set; }
    /// <summary>Escala (1:n) dos textos da barra; 0 = a do resultado.</summary>
    public double Escala { get; set; }
    /// <summary>Tela soldada: modelo (a polilinha fechada é o painel).</summary>
    public string Tela { get; set; } = "";
    public List<(double X, double Y)> Vertices { get; set; } = new List<(double, double)>();
    /// <summary>Bulge de cada vértice (vazio = só retas).</summary>
    public List<double> Bulges { get; set; } = new List<double>();
    public bool Fechada { get; set; }
    public List<FormatoSegmento> Formatos { get; set; } = new List<FormatoSegmento>();
    public HashSet<int> Sobrepostos { get; set; } = new HashSet<int>();
    /// <summary>Segmentos cotados (vazio = todos os retos que não são gancho nem sobrepostos).</summary>
    public HashSet<int> Cotados { get; set; } = new HashSet<int>();
    public double Bitola { get; set; }
    public string Aco { get; set; } = "CA-50";
    public int Quantidade { get; set; } = 1;
    public double Espacamento { get; set; }
    public bool Estribo { get; set; }
}

/// <summary>Linha de forma (contorno do concreto, eixo…) desenhada como polilinha comum.</summary>
public sealed class ContornoTemplate
{
    public List<(double X, double Y)> Vertices { get; set; } = new List<(double, double)>();
    public List<double> Bulges { get; set; } = new List<double>();
    public bool Fechado { get; set; }
    public string Layer { get; set; } = "ARM-FORMA";
}

/// <summary>Faixa de distribuição ou indicação ligada a uma barra do resultado (pelo nome).</summary>
public sealed class FaixaTemplate
{
    public string Barra { get; set; } = "";
    public TipoFaixa Tipo { get; set; } = TipoFaixa.Ortogonal;
    public (double X, double Y) P1 { get; set; }
    public (double X, double Y) P2 { get; set; }
    /// <summary>Posição da cota (faixas) ou do texto (indicação).</summary>
    public (double X, double Y) Posicao { get; set; }
    public double Espacamento { get; set; } = 15;
    /// <summary>Indicação: pontos na barra.</summary>
    public List<(double X, double Y)> Referencias { get; set; } = new List<(double, double)>();
    public double Escala { get; set; }
}

public sealed class TextoTemplate
{
    public string Texto { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Altura em cm (já na escala do desenho).</summary>
    public double Altura { get; set; }
    public double Angulo { get; set; }
    /// <summary>0 esquerda, 1 centro, 2 direita (ancorado na base).</summary>
    public int Alinhamento { get; set; }
    public string Layer { get; set; } = "ARM-TEXTO-FORMA";
}

public sealed class CirculoTemplate
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Raio { get; set; }
    public string Layer { get; set; } = "ARM-FORMA";
}

public sealed class CotaTemplate
{
    public (double X, double Y) P1 { get; set; }
    public (double X, double Y) P2 { get; set; }
    public (double X, double Y) Posicao { get; set; }
    /// <summary>Direção medida (rad): 0 horizontal, π/2 vertical.</summary>
    public double Angulo { get; set; }
    public string Texto { get; set; } = "";
    public double Escala { get; set; }
}

/// <summary>Tabela de texto simples (ex.: comprimentos "L" das barras VAR).</summary>
public sealed class TabelaTemplate
{
    public string Titulo { get; set; } = "";
    public List<string[]> Linhas { get; } = new List<string[]>();
    /// <summary>Canto superior esquerdo.</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double Escala { get; set; }
    /// <summary>Largura de cada coluna em alturas de texto (vazio = 6 para todas).</summary>
    public List<double> Larguras { get; } = new List<double>();
    /// <summary>Alinhamento de cada coluna: 0 esquerda, 1 centro, 2 direita (vazio = centro). O título fica à esquerda se a 1ª coluna for.</summary>
    public List<int> Alinhamentos { get; } = new List<int>();
    /// <summary>Quebra de linha dentro da célula (o desenho troca por \P). Use em textos "PT" + Quebra + "(EN)".</summary>
    public const string Quebra = "\n";

    /// <summary>Altura da tabela em alturas de texto (para posicionar o que vem embaixo).</summary>
    public double AlturaEmTextos() =>
        new[] { new[] { Titulo } }.Concat(Linhas).Sum(l => AlturaLinha(l.Max(c => (c ?? "").Split('\n').Length)));

    /// <summary>Altura de uma linha com n linhas de texto, em alturas de texto.</summary>
    public static double AlturaLinha(int linhasDeTexto) => linhasDeTexto <= 1 ? 2.0 : 1.4 * linhasDeTexto + 0.4;
}

public sealed class ResultadoTemplate
{
    public List<BarraTemplate> Barras { get; } = new List<BarraTemplate>();
    public List<ContornoTemplate> Contornos { get; } = new List<ContornoTemplate>();
    public List<FaixaTemplate> Faixas { get; } = new List<FaixaTemplate>();
    public List<TextoTemplate> Textos { get; } = new List<TextoTemplate>();
    public List<CirculoTemplate> Circulos { get; } = new List<CirculoTemplate>();
    public List<CotaTemplate> Cotas { get; } = new List<CotaTemplate>();
    public List<TabelaTemplate> Tabelas { get; } = new List<TabelaTemplate>();
    /// <summary>Escala (1:n) do desenho gerado (textos, faixas, tabelas).</summary>
    public double Escala { get; set; } = 25;
    /// <summary>Detalhamento (bloco) que reúne tudo; vazio = solto no desenho.</summary>
    public string Detalhamento { get; set; } = "";
    /// <summary>Canto superior esquerdo da relação do aço parcial (null = sem tabela).</summary>
    public (double X, double Y)? PosicaoRA { get; set; }
    /// <summary>Avisos ao usuário (valores fora do padrão, verificações).</summary>
    public List<string> Avisos { get; } = new List<string>();
}

/// <summary>
/// Template de armadura de um elemento. Implementações ficam em <c>Templates/&lt;Categoria&gt;/</c> e
/// são registradas em <see cref="CatalogoTemplates"/>. O template só GERA geometria e dados; o CAD
/// desenha (ARMTEMPLATE). Nada de dimensionamento: os valores são decisão do engenheiro.
/// </summary>
public interface ITemplateArmadura
{
    string Id { get; }
    string Nome { get; }
    CategoriaElemento Categoria { get; }
    string Descricao { get; }
    IReadOnlyList<ParametroTemplate> Parametros { get; }
    ResultadoTemplate Gerar(ValoresTemplate valores, ConfiguracaoArmadura cfg);
}

/// <summary>Catálogo dos templates disponíveis e das categorias planejadas.</summary>
public static class CatalogoTemplates
{
    private static readonly List<ITemplateArmadura> Registrados = new List<ITemplateArmadura>();

    /// <summary>Nome e o que ainda falta de cada categoria (mostrado pelo ARMTEMPLATE).</summary>
    public static readonly IReadOnlyList<(CategoriaElemento Categoria, string Nome, string Variacoes)> Categorias = new[]
    {
        (CategoriaElemento.BaseCircularReservatorio, "Base de reservatório", "circular ou retangular, sobre estacas"),
        (CategoriaElemento.Bloco1Estaca, "Bloco de 1 estaca", "com/sem colarinho ou cálice; embutimento para pilar pré-moldado"),
        (CategoriaElemento.Bloco2Estacas, "Bloco de 2 estacas", "com/sem colarinho ou cálice; embutimento para pilar pré-moldado"),
        (CategoriaElemento.PilarPreMoldado, "Pilar pré-moldado", "consolos, içamento, zonas de estribos"),
        (CategoriaElemento.VigaPreMoldada, "Viga pré-moldada", "protendida ou não"),
        (CategoriaElemento.PilarMoldadoInLoco, "Pilar moldado in loco", ""),
        (CategoriaElemento.VigaMoldadaInLoco, "Viga moldada in loco", ""),
        (CategoriaElemento.Laje, "Laje", ""),
    };

    public static IReadOnlyList<ITemplateArmadura> Todos => Registrados;

    public static IEnumerable<ITemplateArmadura> DaCategoria(CategoriaElemento c) => Registrados.Where(t => t.Categoria == c);

    public static ITemplateArmadura Obter(string id) =>
        Registrados.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Registra um template (os do plugin no carregamento; os testes registram os seus).</summary>
    public static void Registrar(ITemplateArmadura t)
    {
        if (t == null || Obter(t.Id) != null) return;
        Registrados.Add(t);
    }

    public static void Remover(string id) => Registrados.RemoveAll(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Confere os valores contra os limites dos parâmetros. Retorna os erros.</summary>
    public static List<string> Validar(ITemplateArmadura t, ValoresTemplate v)
    {
        var erros = new List<string>();
        foreach (var p in t.Parametros)
        {
            if (p.Tipo == TipoParametro.Numero || p.Tipo == TipoParametro.Inteiro || p.Tipo == TipoParametro.Bitola)
            {
                double x = v.Numero(p.Nome);
                if (x < p.Minimo || x > p.Maximo)
                    erros.Add($"{p.Rotulo}: {x.ToString(CultureInfo.InvariantCulture)} fora de [{p.Minimo.ToString(CultureInfo.InvariantCulture)}; {p.Maximo.ToString(CultureInfo.InvariantCulture)}].");
            }
            else if (p.Tipo == TipoParametro.Opcao && p.Opcoes.Count > 0 && !p.Opcoes.Contains(v[p.Nome], StringComparer.OrdinalIgnoreCase))
                erros.Add($"{p.Rotulo}: escolha uma das opções ({string.Join(", ", p.Opcoes)}).");
        }
        return erros;
    }
}
