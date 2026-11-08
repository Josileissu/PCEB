using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using DEB.Core.Templates.PilarPreMoldado;

namespace DEB.Core.Templates.VigaPreMoldada;

/// <summary>
/// Dente Gerber do catálogo do escritório (pedido do usuário, 2026-11-07): a armadura do dente muda a cada projeto (vem do
/// dimensionamento, NBR 9062 7.4), então cada tipo guarda a geometria e, a partir da etapa 3, a armadura (estribos Gerber,
/// suspensão, grampo, costura, tirante), como o catálogo de consolos do pilar. A viga escolhe o tipo em cada ponta.
/// </summary>
[DataContract]
public sealed class TipoDente
{
    [DataMember(Order = 1)] public string Nome { get; set; } = "";
    [DataMember(Order = 2)] public string Descricao { get; set; } = "";
    /// <summary>Altura do dente (a parte da viga que fica sobre o apoio), cm. O recorte embaixo = h da viga − esta altura.</summary>
    [DataMember(Order = 3)] public double Altura { get; set; } = 45;
    /// <summary>Comprimento do dente, da ponta da viga à face do recorte, cm.</summary>
    [DataMember(Order = 4)] public double Comprimento { get; set; } = 25;

    /// <summary>Texto curto ("45 x 25").</summary>
    public string Resumo => $"{TemplatePilarPreMoldado.Num(Altura, "0.#")} x {TemplatePilarPreMoldado.Num(Comprimento, "0.#")}";

    public TipoDente Clonar() => (TipoDente)MemberwiseClone();

    [OnDeserializing]
    private void AoDesserializar(StreamingContext _) => Configuracao.Padroes.Copiar(new TipoDente(), this);

    /// <summary>Tipos de partida, das pranchas do escritório (316-6208 e 316-6219).</summary>
    public static List<TipoDente> Padrao() => new List<TipoDente>
    {
        new TipoDente { Nome = "DG 45x25", Descricao = "Dente 45 x 25 (V143/V144, viga 20 x 75)", Altura = 45, Comprimento = 25 },
        new TipoDente { Nome = "DG 35x25", Descricao = "Dente 35 x 25 (V129/V130, viga 40 x 50)", Altura = 35, Comprimento = 25 },
    };

    public static TipoDente Obter(IEnumerable<TipoDente> tipos, string nome) =>
        tipos?.FirstOrDefault(t => string.Equals(t.Nome?.Trim(), nome?.Trim(), System.StringComparison.OrdinalIgnoreCase));
}
