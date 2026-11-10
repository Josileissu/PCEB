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

    // armadura do dente (etapa 3, 2026-11-08; padrão das pranchas 316-6208/6219 — muda por projeto, NBR 9062 7.4)
    /// <summary>Estribos Gerber: fechados dentro do dente (altura do dente), distribuídos no comprimento dele.</summary>
    [DataMember(Order = 5)] public double EstGerberBitola { get; set; } = 8;
    [DataMember(Order = 6)] public int EstGerberQtd { get; set; } = 4;
    /// <summary>Suspensão: estribos fechados de altura total, junto da face do recorte, a cada <see cref="SuspEsp"/>.</summary>
    [DataMember(Order = 7)] public double SuspBitola { get; set; } = 10;
    [DataMember(Order = 8)] public int SuspQtd { get; set; } = 5;
    [DataMember(Order = 9)] public double SuspEsp { get; set; } = 5;
    /// <summary>Grampo: U horizontal na parte de cima do dente, pernas entrando na viga.</summary>
    [DataMember(Order = 10)] public double GrampoBitola { get; set; } = 10;
    [DataMember(Order = 11)] public int GrampoQtd { get; set; } = 4;
    [DataMember(Order = 12)] public double GrampoPerna { get; set; } = 100;
    /// <summary>Costura: U horizontal na parte de baixo do dente.</summary>
    [DataMember(Order = 13)] public double CosturaBitola { get; set; } = 10;
    [DataMember(Order = 14)] public int CosturaQtd { get; set; } = 3;
    [DataMember(Order = 15)] public double CosturaPerna { get; set; } = 100;
    /// <summary>Tirante: barras retas no topo do dente, com barra transversal soldada na ponta (NBR 9062 7.4).</summary>
    [DataMember(Order = 16)] public double TiranteBitola { get; set; } = 16;
    [DataMember(Order = 17)] public int TiranteQtd { get; set; } = 3;
    [DataMember(Order = 18)] public double TiranteComp { get; set; } = 200;

    /// <summary>Texto curto ("45 x 25").</summary>
    public string Resumo => $"{KitDesenho.Num(Altura, "0.#")} x {KitDesenho.Num(Comprimento, "0.#")}";

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
