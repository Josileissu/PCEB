namespace DEB.Core.Modelo;

public enum SistemaConstrutivo { MoldadoInLoco = 0, PreMoldado = 1, AlvenariaEstrutural = 2 }

/// <summary>
/// Detalhamento (seção 2.9) = bloco do CAD. O nome é o do bloco; as repetições são o número de
/// inserções no desenho; o resto fica no XData da definição do bloco.
/// </summary>
public sealed class DadosDetalhamento
{
    /// <summary>Vigas, Lajes, Pilares, Escadas, Fundações… (texto livre).</summary>
    public string Grupo { get; set; } = "";
    public string Pavimento { get; set; } = "";
    /// <summary>Classe do concreto, ex.: "C30".</summary>
    public string Classe { get; set; } = "";
    /// <summary>Volume de concreto de UMA peça (m³); o total multiplica pelas repetições.</summary>
    public double Volume { get; set; }
    /// <summary>Área de forma de UMA peça (m²).</summary>
    public double AreaForma { get; set; }
    public SistemaConstrutivo Sistema { get; set; } = SistemaConstrutivo.MoldadoInLoco;
    /// <summary>Não aparecem no desenho.</summary>
    public string Comentarios { get; set; } = "";
    /// <summary>
    /// Quantidade de peças iguais digitada pelo usuário (pedido de 2026-11-03); 0 = automático (nº de inserções do bloco no
    /// desenho). Com valor, a relação do aço multiplica por ele em vez das inserções.
    /// </summary>
    public int Repeticoes { get; set; }
    public int Numero { get; set; }
    /// <summary>
    /// Origem no motor de detalhamento (2026-11-07, decisão do usuário: fica no detalhamento): JSON com o elemento, o perfil,
    /// as sobreposições do perfil e as sobrescritas (edições manuais) que geraram este detalhamento, para regenerar depois.
    /// Vazio = desenhado à mão ou por um template antigo. XData "MOT" (em pedaços de 250 caracteres).
    /// </summary>
    public string Motor { get; set; } = "";
}
