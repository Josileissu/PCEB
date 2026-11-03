using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Armadura.Core.Modelo;
using Armadura.Core.Tabelas;

namespace Armadura.Core.Normas;

public enum SituacaoAderencia { Boa = 0, Ma = 1 }

public enum ModoTraspasse
{
    /// <summary>Pela NBR 6118 (fck, aderência, % de barras emendadas na mesma seção).</summary>
    Calculado = 0,
    /// <summary>Valor por bitola da tabela "Barras" (se zero, calcula).</summary>
    Fixo = 1,
}

/// <summary>
/// Traspasse de barras tracionadas (NBR 6118, 9.3.2 e 9.5.2.2) [VALIDAR NBR]:
/// fctd = 0,7·0,3·fck^(2/3)/γc; fbd = η1·η2·η3·fctd; lb = (φ/4)·(fyd/fbd);
/// l0t = α0t·lb,nec ≥ l0t,min = máx(0,3·α0t·lb; 15φ; 200 mm), com lb,nec = lb (As,calc = As,ef).
/// </summary>
[DataContract]
public sealed class RegrasTraspasse
{
    [DataMember(Order = 1)] public ModoTraspasse Modo { get; set; } = ModoTraspasse.Calculado;
    /// <summary>fck do concreto (MPa).</summary>
    [DataMember(Order = 2)] public double Fck { get; set; } = 25;
    [DataMember(Order = 3)] public SituacaoAderencia Aderencia { get; set; } = SituacaoAderencia.Boa;
    /// <summary>% de barras emendadas na mesma seção (define α0t).</summary>
    [DataMember(Order = 4)] public double PercentualEmendado { get; set; } = 50;
    [DataMember(Order = 5)] public double GamaC { get; set; } = 1.4;
    [DataMember(Order = 6)] public double GamaS { get; set; } = 1.15;
    /// <summary>Qualquer barra maior que o comprimento comercial é dividida sozinha (seção 3.3).</summary>
    [DataMember(Order = 7)] public bool VerificarComprimentoLimite { get; set; }
    /// <summary>Afastamento (mm no papel) entre as barras sobrepostas, só para leitura do desenho.</summary>
    [DataMember(Order = 8)] public double AfastamentoDesenhoMm { get; set; } = 1.0;

    [OnDeserializing]
    private void AoDesserializar(StreamingContext _) => Configuracao.Padroes.Copiar(new RegrasTraspasse(), this);

    /// <summary>α0t pela porcentagem de barras emendadas (tabela 9.4 da NBR 6118).</summary>
    public static double Alfa0t(double percentual) =>
        percentual <= 20 ? 1.2 : percentual <= 25 ? 1.4 : percentual <= 33 ? 1.6 : percentual <= 50 ? 1.8 : 2.0;

    public static double Eta1(string aco) => RegrasDobramento.TipoAco(aco) switch
    {
        25 => 1.0,  // lisa
        60 => 1.4,  // entalhada
        _ => 2.25,  // nervurada
    };

    public static double Fyk(string aco) => RegrasDobramento.TipoAco(aco) switch { 25 => 250, 60 => 600, _ => 500 };

    /// <summary>Comprimento de ancoragem básico lb (cm).</summary>
    public double Lb(double bitolaMm, string aco)
    {
        double fctd = 0.7 * 0.3 * Math.Pow(Fck, 2.0 / 3.0) / GamaC;
        double eta2 = Aderencia == SituacaoAderencia.Boa ? 1.0 : 0.7;
        double eta3 = bitolaMm < 32 ? 1.0 : (132 - bitolaMm) / 100.0;
        double fbd = Eta1(aco) * eta2 * eta3 * fctd;
        double fyd = Fyk(aco) / GamaS;
        return bitolaMm / 4.0 * fyd / fbd / 10.0;
    }

    /// <summary>Comprimento de traspasse (cm, arredondado para cima). Cordoalhas: erro.</summary>
    public double Comprimento(double bitolaMm, string aco, TabelaBarras tabela = null)
    {
        if (TabelaBarras.EhCordoalha(aco)) throw new InvalidOperationException("Cordoalha de protensão não leva traspasse.");
        if (Modo == ModoTraspasse.Fixo)
        {
            var b = tabela?.Obter(bitolaMm, aco);
            if (b != null && b.TraspasseFixo > 0) return b.TraspasseFixo;
        }
        double lb = Lb(bitolaMm, aco);
        double a = Alfa0t(PercentualEmendado);
        double minimo = Math.Max(Math.Max(0.3 * a * lb, 15 * bitolaMm / 10.0), 20);
        return Math.Ceiling(Math.Max(a * lb, minimo) - 1e-9);
    }

    public string Resumo(double bitolaMm, string aco, TabelaBarras tabela) =>
        Modo == ModoTraspasse.Fixo && (tabela?.Obter(bitolaMm, aco)?.TraspasseFixo ?? 0) > 0
            ? "fixo (tabela de barras)"
            : $"calculado: C{Fck:0}, aderência {(Aderencia == SituacaoAderencia.Boa ? "boa" : "má")}, {PercentualEmendado:0}% emendadas (α0t = {Alfa0t(PercentualEmendado):0.0})";
}

/// <summary>Onde uma barra pode ser dividida e como dividi-la pelo comprimento comercial.</summary>
public static class PlanoDivisao
{
    /// <summary>
    /// Intervalos [início, fim] (cm, ao longo da barra) em que pode ficar o CENTRO do traspasse:
    /// o trecho sobreposto precisa caber inteiro num segmento reto.
    /// </summary>
    public static List<(double ini, double fim)> IntervalosValidos(IReadOnlyList<GeoSegmento> geo, double traspasse)
    {
        var r = new List<(double, double)>();
        double o = 0;
        foreach (var s in geo)
        {
            if (s.Tipo == TipoSegmento.Reta && s.Comprimento >= traspasse)
            {
                double a = o + traspasse / 2, b = o + s.Comprimento - traspasse / 2;
                if (b >= a - 1e-9) r.Add((a, b));
            }
            o += s.Comprimento;
        }
        return r;
    }

    /// <summary>Centro válido mais próximo de <paramref name="s"/>; null se não houver nenhum.</summary>
    public static double? MaisProximo(IReadOnlyList<GeoSegmento> geo, double traspasse, double s)
    {
        double? melhor = null;
        foreach (var (a, b) in IntervalosValidos(geo, traspasse))
        {
            double c = Math.Max(a, Math.Min(b, s));
            if (melhor == null || Math.Abs(c - s) < Math.Abs(melhor.Value - s)) melhor = c;
        }
        return melhor;
    }

    /// <summary>
    /// Centros dos traspasses (cm, na barra ORIGINAL) para que nenhuma peça passe do comprimento
    /// comercial. Cada corte fica o mais adiante possível. Null se não houver como dividir.
    /// </summary>
    public static List<double> Automatica(IReadOnlyList<GeoSegmento> geo, double traspasse, double comercial)
    {
        double total = geo.Sum(s => s.Comprimento);
        var cortes = new List<double>();
        if (comercial <= 0 || total <= comercial + 1e-6) return cortes;
        if (comercial <= traspasse) return null;
        var validos = IntervalosValidos(geo, traspasse);
        double inicioPeca = 0;
        while (total - inicioPeca > comercial + 1e-6)
        {
            // a peça vai de inicioPeca até s + l0/2 ≤ inicioPeca + comercial
            double limite = inicioPeca + comercial - traspasse / 2;
            double minimo = inicioPeca + traspasse / 2 + 1e-6; // a peça não pode ser só o traspasse
            double? s = null;
            foreach (var (a, b) in validos)
            {
                if (a > limite + 1e-9 || b < minimo) continue;
                double c = Math.Min(b, limite);
                if (c >= minimo && (s == null || c > s)) s = c;
            }
            if (s == null) return null;
            cortes.Add(s.Value);
            inicioPeca = s.Value - traspasse / 2;
        }
        return cortes;
    }
}
