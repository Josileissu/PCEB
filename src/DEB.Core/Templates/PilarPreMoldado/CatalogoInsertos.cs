using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace DEB.Core.Templates.PilarPreMoldado;

/// <summary>
/// Inserto metálico padrão (pedido do usuário, 2026-10-29): como os consolos, um catálogo nomeado na configuração do
/// usuário (ARMINSERTOS); o pilar só escolhe o tipo, o local (topo ou face) e a posição. Os insertos são da fornecedora
/// (nota do desenho): o template só os representa — não entram na relação do aço.
/// </summary>
[DataContract]
public sealed class TipoInserto
{
    public const string ModoTopo = "Topo (chumbadores)", ModoFace = "Chapa na face";

    [DataMember(Order = 1)] public string Nome { get; set; } = "";
    [DataMember(Order = 2)] public string Descricao { get; set; } = "";
    /// <summary><see cref="ModoTopo"/> ou <see cref="ModoFace"/>.</summary>
    [DataMember(Order = 3)] public string Modo { get; set; } = ModoTopo;
    // ---- topo: chumbadores verticais com gabarito ----
    [DataMember(Order = 4)] public string Gabarito { get; set; } = "";
    /// <summary>Distância entre os chumbadores em x (ao longo de b) e em y (ao longo de h), cm.</summary>
    [DataMember(Order = 5)] public double Ax { get; set; } = 13;
    [DataMember(Order = 6)] public double Ay { get; set; } = 13;
    [DataMember(Order = 7)] public int ChumbN { get; set; } = 4;
    [DataMember(Order = 8)] public double ChumbPhi { get; set; } = 25;
    [DataMember(Order = 9)] public double ChumbComp { get; set; } = 60;
    /// <summary>Quanto o chumbador sai acima do topo (cm; 0 = rente).</summary>
    [DataMember(Order = 10)] public double ChumbAcima { get; set; }
    // ---- face: chapa embutida com grapas ----
    [DataMember(Order = 11)] public double ChapaL { get; set; } = 20;
    [DataMember(Order = 12)] public double ChapaH { get; set; } = 20;
    [DataMember(Order = 13)] public double ChapaE { get; set; } = 1.25;
    [DataMember(Order = 14)] public int GrapaN { get; set; } = 4;
    [DataMember(Order = 15)] public double GrapaPhi { get; set; } = 12.5;
    [DataMember(Order = 16)] public double GrapaComp { get; set; } = 20;

    public bool NoTopo => Modo != ModoFace;

    public string Resumo => NoTopo
        ? $"topo, {ChumbN} chumbadores ø{TemplatePilarPreMoldado.Num(ChumbPhi, "0.#")} L={TemplatePilarPreMoldado.Num(ChumbComp, "0.#")}" +
          (Gabarito.Length > 0 ? $", gabarito {Gabarito}" : "")
        : $"chapa {TemplatePilarPreMoldado.Num(ChapaL, "0.#")}x{TemplatePilarPreMoldado.Num(ChapaH, "0.#")}, {GrapaN} grapas ø{TemplatePilarPreMoldado.Num(GrapaPhi, "0.#")}";

    public TipoInserto Clonar() => (TipoInserto)MemberwiseClone();

    [OnDeserializing]
    private void AoDesserializar(StreamingContext _) => Configuracao.Padroes.Copiar(new TipoInserto(), this);

    /// <summary>Tipos de partida: o inserto do topo do P61 e uma chapa de face genérica.</summary>
    public static List<TipoInserto> Padrao() => new List<TipoInserto>
    {
        new TipoInserto { Nome = "25CB660", Descricao = "Topo do P61 (fornecedora)", Modo = ModoTopo, Gabarito = "GC1A" },
        new TipoInserto { Nome = "CHAPA 20x20", Descricao = "Chapa embutida na face", Modo = ModoFace },
    };

    public static TipoInserto Obter(IEnumerable<TipoInserto> tipos, string nome) =>
        tipos?.FirstOrDefault(t => string.Equals(t.Nome?.Trim(), nome?.Trim(), System.StringComparison.OrdinalIgnoreCase));
}
