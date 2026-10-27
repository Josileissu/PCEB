namespace Armadura.Core.Modelo;

public enum TipoSegmento { Reta, Arco }

/// <summary>Formato do segmento (seção 2.1). "Gancho 45°" = gancho de 135° com ponta a 45°.</summary>
public enum FormatoSegmento { Linha = 0, Gancho45 = 1, Gancho90 = 2, Gancho180 = 3, Curva = 4 }

/// <summary>Geometria de um segmento, lida do CAD e convertida para cm.</summary>
public sealed class GeoSegmento
{
    public TipoSegmento Tipo { get; set; }
    /// <summary>Comprimento do segmento em cm (arcos: desenvolvimento do arco).</summary>
    public double Comprimento { get; set; }
    /// <summary>Ângulo (graus, -180..180) entre este segmento e o anterior; no primeiro, 0 (ou, em barra fechada, o ângulo em relação ao último).</summary>
    public double Deflexao { get; set; }
    /// <summary>Ângulo central do arco em graus (com sinal); 0 para retas.</summary>
    public double AnguloArco { get; set; }
    /// <summary>Passagem do gancho pelo canto do estribo (desenhada sobre o lado): conta como aço, sem cota.</summary>
    public bool Sobreposto { get; set; }

    public GeoSegmento() { }
    public GeoSegmento(TipoSegmento tipo, double comprimento, double deflexao = 0, double anguloArco = 0)
    {
        Tipo = tipo; Comprimento = comprimento; Deflexao = deflexao; AnguloArco = anguloArco;
    }
}

/// <summary>Dados de um segmento gravados no DWG (o comprimento real vem sempre da geometria).</summary>
public sealed class Segmento
{
    public FormatoSegmento Formato { get; set; } = FormatoSegmento.Linha;
    public bool Cotar { get; set; } = true;
    public bool Var { get; set; }
    /// <summary>Comprimento (cm) na última sincronização; detecta edição por grips/stretch.</summary>
    public double ComprimentoRegistrado { get; set; }
    /// <summary>Handle (hex) do texto da cota; vazio = sem cota.</summary>
    public string HandleCota { get; set; } = "";
    /// <summary>
    /// Trecho desenhado sobre outro (no estribo fechado, cada gancho passa do canto o diâmetro externo
    /// de curvatura, pino + 2φ): é aço (entra no comprimento real, com as duas dobras), mas não é cotado
    /// nem tem medida no esboço da RA (decisão do usuário, 2026-10-27).
    /// </summary>
    public bool Sobreposto { get; set; }

    public static bool EhGancho(FormatoSegmento f) =>
        f == FormatoSegmento.Gancho45 || f == FormatoSegmento.Gancho90 || f == FormatoSegmento.Gancho180;

    public Segmento Clonar() => (Segmento)MemberwiseClone();
}
