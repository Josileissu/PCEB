using System;
using System.Runtime.Serialization;
using DEB.Core.Modelo;

namespace DEB.Core.Normas;

/// <summary>
/// Pinos de dobramento e pontas retas dos ganchos (NBR 6118, itens 9.4.2.3 e 9.4.6.1) [VALIDAR NBR].
/// Todos os multiplicadores são configuráveis; os padrões abaixo seguem a seção 4.5 da especificação.
/// </summary>
[DataContract]
public sealed class RegrasDobramento
{
    // Pino das barras longitudinais (múltiplos de φ)
    [DataMember(Order = 1)] public double PinoCA25Menor20 { get; set; } = 4;
    [DataMember(Order = 2)] public double PinoCA25Maior20 { get; set; } = 5;
    [DataMember(Order = 3)] public double PinoCA50Menor20 { get; set; } = 5;
    [DataMember(Order = 4)] public double PinoCA50Maior20 { get; set; } = 8;
    [DataMember(Order = 5)] public double PinoCA60 { get; set; } = 6;

    // Pino dos estribos (múltiplos de φt)
    [DataMember(Order = 6)] public double PinoEstriboAte10 { get; set; } = 3;
    [DataMember(Order = 7)] public double PinoEstriboCA25Ate20 { get; set; } = 4;
    [DataMember(Order = 8)] public double PinoEstriboCA50Ate20 { get; set; } = 5;
    [DataMember(Order = 9)] public double PinoEstriboCA25Acima20 { get; set; } = 5;
    [DataMember(Order = 10)] public double PinoEstriboCA50Acima20 { get; set; } = 8;

    // Pontas retas das barras longitudinais (múltiplos de φ)
    [DataMember(Order = 11)] public double PontaGancho180 { get; set; } = 2;
    [DataMember(Order = 12)] public double PontaGancho45 { get; set; } = 4;
    [DataMember(Order = 13)] public double PontaGancho90 { get; set; } = 8;

    // Pontas retas dos estribos: múltiplo de φt e mínimo em cm
    [DataMember(Order = 14)] public double PontaEstribo180ou45 { get; set; } = 5;
    [DataMember(Order = 15)] public double PontaEstribo180ou45Min { get; set; } = 5;
    [DataMember(Order = 16)] public double PontaEstribo90 { get; set; } = 10;
    [DataMember(Order = 17)] public double PontaEstribo90Min { get; set; } = 7;

    /// <summary>Diâmetro do pino de dobramento, em cm.</summary>
    public double DiametroPino(double bitolaMm, string aco, bool estribo)
    {
        double phi = bitolaMm / 10.0;
        var tipo = TipoAco(aco);
        double k;
        if (estribo)
        {
            if (bitolaMm <= 10) k = PinoEstriboAte10;
            else if (bitolaMm < 20) k = tipo == 25 ? PinoEstriboCA25Ate20 : PinoEstriboCA50Ate20;
            else k = tipo == 25 ? PinoEstriboCA25Acima20 : PinoEstriboCA50Acima20;
        }
        else
        {
            k = tipo switch
            {
                25 => bitolaMm < 20 ? PinoCA25Menor20 : PinoCA25Maior20,
                60 => PinoCA60,
                _ => bitolaMm < 20 ? PinoCA50Menor20 : PinoCA50Maior20,
            };
        }
        return k * phi;
    }

    /// <summary>Ponta reta mínima do gancho, em cm.</summary>
    public double PontaReta(FormatoSegmento formato, double bitolaMm, bool estribo)
    {
        double phi = bitolaMm / 10.0;
        if (estribo)
        {
            return formato == FormatoSegmento.Gancho90
                ? Math.Max(PontaEstribo90 * phi, PontaEstribo90Min)
                : Math.Max(PontaEstribo180ou45 * phi, PontaEstribo180ou45Min);
        }
        return formato switch
        {
            FormatoSegmento.Gancho180 => PontaGancho180 * phi,
            FormatoSegmento.Gancho45 => PontaGancho45 * phi,
            _ => PontaGancho90 * phi,
        };
    }

    /// <summary>
    /// Comprimento do segmento de gancho, em cm: ponta reta + raio do pino + φ
    /// (medida externa até a face da barra anterior). [VALIDAR NBR / padrão do escritório]
    /// </summary>
    public double ComprimentoGancho(FormatoSegmento formato, double bitolaMm, string aco, bool estribo)
    {
        if (!Modelo.Segmento.EhGancho(formato)) throw new ArgumentException("Formato não é gancho.", nameof(formato));
        double phi = bitolaMm / 10.0;
        return PontaReta(formato, bitolaMm, estribo) + DiametroPino(bitolaMm, aco, estribo) / 2.0 + phi;
    }

    [OnDeserializing]
    private void AoDesserializar(StreamingContext _) => Configuracao.Padroes.Copiar(new RegrasDobramento(), this);

    /// <summary>25, 50 ou 60 a partir do nome ("CA-50", "CA50"…); outros aços contam como 50.</summary>
    public static int TipoAco(string aco)
    {
        if (string.IsNullOrEmpty(aco)) return 50;
        var s = aco.ToUpperInvariant();
        if (s.Contains("25")) return 25;
        if (s.Contains("60")) return 60;
        return 50;
    }
}
