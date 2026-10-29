using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Armadura.Core.Templates.PilarPreMoldado;

/// <summary>
/// Consolo padrão do escritório (pedido do usuário, 2026-10-29): tipo nomeado com forma, dimensões e armadura
/// padronizadas. O pilar só escolhe o tipo, as faces e a distância da base ao topo do consolo. Os comprimentos das
/// barras saem da geometria do pilar (o tirante do duplo atravessa o pilar). Fica na configuração do usuário
/// (ARMCONSOLOS edita).
/// </summary>
[DataContract]
public sealed class TipoConsolo
{
    [DataMember(Order = 1)] public string Nome { get; set; } = "";
    [DataMember(Order = 2)] public string Descricao { get; set; } = "";
    /// <summary>Duplo: dois consolos em faces opostas com o tirante passando pelo pilar.</summary>
    [DataMember(Order = 3)] public bool Duplo { get; set; }
    /// <summary>Largura (ao longo da face), balanço, altura junto ao pilar e na ponta (h1 &lt; h = trapezoidal), cm.</summary>
    [DataMember(Order = 4)] public double B { get; set; } = 40;
    [DataMember(Order = 5)] public double A { get; set; } = 25;
    [DataMember(Order = 6)] public double H { get; set; } = 23.5;
    [DataMember(Order = 7)] public double H1 { get; set; } = 23.5;
    [DataMember(Order = 8)] public int TirN { get; set; } = 5;
    [DataMember(Order = 9)] public double TirPhi { get; set; } = 16;
    [DataMember(Order = 10)] public bool Laco { get; set; }
    [DataMember(Order = 11)] public int CostN { get; set; } = 4;
    [DataMember(Order = 12)] public double CostPhi { get; set; } = 8;
    [DataMember(Order = 13)] public int VertN { get; set; } = 5;
    [DataMember(Order = 14)] public double VertPhi { get; set; } = 6.3;
    [DataMember(Order = 15)] public int InfN { get; set; } = 5;
    [DataMember(Order = 16)] public double InfPhi { get; set; } = 10;
    [DataMember(Order = 17)] public double InfAnc { get; set; } = 30;
    [DataMember(Order = 18)] public int PinoN { get; set; } = 2;
    [DataMember(Order = 19)] public double PinoPhi { get; set; } = 20;
    [DataMember(Order = 20)] public double PinoAcima { get; set; } = 47;
    [DataMember(Order = 21)] public double PinoEmb { get; set; } = 18;
    [DataMember(Order = 22)] public double PinoPe { get; set; } = 20;
    [DataMember(Order = 23)] public double AlmA { get; set; } = 15;
    [DataMember(Order = 24)] public double AlmB { get; set; } = 30;
    [DataMember(Order = 25)] public double AlmE { get; set; } = 1.5;

    public bool Trapezoidal => H1 < H - 1e-6;

    /// <summary>Texto curto do tipo ("40x23,5x25 duplo, trapezoidal").</summary>
    public string Resumo => $"{TemplatePilarPreMoldado.Num(B, "0.#")}x{TemplatePilarPreMoldado.Num(H, "0.#")}" +
                            (Trapezoidal ? $"/{TemplatePilarPreMoldado.Num(H1, "0.#")}" : "") + $"x{TemplatePilarPreMoldado.Num(A, "0.#")}" +
                            (Duplo ? " duplo" : "") + (Trapezoidal ? ", trapezoidal" : "");

    public TipoConsolo Clonar() => (TipoConsolo)MemberwiseClone();

    [OnDeserializing]
    private void AoDesserializar(StreamingContext _) => Configuracao.Padroes.Copiar(new TipoConsolo(), this);

    /// <summary>Tipos de partida, das pranchas do escritório (P61 e PP127). O usuário ajusta no ARMCONSOLOS.</summary>
    public static List<TipoConsolo> Padrao() => new List<TipoConsolo>
    {
        new TipoConsolo { Nome = "CD 40x23,5x25", Descricao = "Duplo retangular (P61)", Duplo = true },
        new TipoConsolo
        {
            Nome = "CS 20x28,5x25", Descricao = "Um só, retangular (PP127)", B = 20, A = 25, H = 28.5, H1 = 28.5,
            TirN = 2, TirPhi = 12.5, CostN = 4, CostPhi = 6.3, VertN = 5, VertPhi = 6.3, InfN = 2, InfPhi = 8,
            PinoN = 1, PinoPhi = 20, AlmA = 12, AlmB = 15,
        },
        new TipoConsolo
        {
            Nome = "CT 40x33,5-23,5x25", Descricao = "Um só, trapezoidal (PP127)", B = 40, A = 25, H = 33.5, H1 = 23.5,
            TirN = 5, TirPhi = 12.5, CostN = 6, CostPhi = 6.3, VertN = 6, VertPhi = 6.3, InfN = 2, InfPhi = 10,
            PinoN = 2, PinoPhi = 20,
        },
    };

    public static TipoConsolo Obter(IEnumerable<TipoConsolo> tipos, string nome) =>
        tipos?.FirstOrDefault(t => string.Equals(t.Nome?.Trim(), nome?.Trim(), System.StringComparison.OrdinalIgnoreCase));
}
