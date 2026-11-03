using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace DEB.Core.Tabelas;

/// <summary>Armação treliçada (seção 4.7). Valores padrão [VALIDAR com o catálogo do fornecedor].</summary>
[DataContract]
public sealed class ModeloTrelica
{
    [DataMember(Order = 1)] public string Nome { get; set; } = "";
    /// <summary>Altura H (cm).</summary>
    [DataMember(Order = 2)] public double Altura { get; set; }
    /// <summary>Largura da base B (cm).</summary>
    [DataMember(Order = 3)] public double Base { get; set; } = 9;
    [DataMember(Order = 4)] public double PhiSuperior { get; set; }
    [DataMember(Order = 5)] public double PhiDiagonal { get; set; }
    [DataMember(Order = 6)] public double PhiInferior { get; set; }
    /// <summary>Passo S da sinusoide (cm).</summary>
    [DataMember(Order = 7)] public double Passo { get; set; } = 20;
    /// <summary>Massa (kg/m).</summary>
    [DataMember(Order = 8)] public double PesoMetro { get; set; }

    [OnDeserializing]
    private void AoDesserializar(StreamingContext _) => Configuracao.Padroes.Copiar(new ModeloTrelica(), this);
}

/// <summary>Fios de uma tela num painel retangular (distribuição do aço).</summary>
public static class DistribuicaoTela
{
    /// <summary>
    /// Fios em coordenadas do painel (cm; x ao longo do 1º lado, y ao longo do 2º). Os longitudinais
    /// correm ao longo do lado MAIOR (comprimento do painel), espaçados de EspLongitudinal; os
    /// transversais ao longo do menor, espaçados de EspTransversal. Cada família é centrada no painel.
    /// </summary>
    public static List<((double X, double Y) A, (double X, double Y) B)> Fios(double lado1, double lado2, ModeloTela m)
    {
        var r = new List<((double, double), (double, double))>();
        if (m == null || lado1 <= 0 || lado2 <= 0) return r;
        bool xMaior = lado1 >= lado2;
        // fios paralelos a x (ao longo de lado1): posições em y
        double espParalelosX = xMaior ? m.EspLongitudinal : m.EspTransversal;
        double espParalelosY = xMaior ? m.EspTransversal : m.EspLongitudinal;
        foreach (var y in Posicoes(lado2, espParalelosX)) r.Add(((0, y), (lado1, y)));
        foreach (var x in Posicoes(lado1, espParalelosY)) r.Add(((x, 0), (x, lado2)));
        return r;
    }

    /// <summary>Posições centradas: n = ⌊L/s⌋ + 1 fios (os que cabem), sobra dividida nas duas bordas.</summary>
    public static List<double> Posicoes(double comprimento, double espacamento)
    {
        var r = new List<double>();
        if (espacamento <= 0 || comprimento <= 0) return r;
        int n = (int)Math.Floor(comprimento / espacamento + 1e-9) + 1;
        double sobra = comprimento - (n - 1) * espacamento;
        if (sobra < 1e-9 && n > 1) { n--; sobra = comprimento - (n - 1) * espacamento; } // não encostar fio na borda
        for (int i = 0; i < n; i++) r.Add(sobra / 2 + i * espacamento);
        return r;
    }
}

/// <summary>Tela soldada (seção 2.6, NBR 7481). Valores padrão [VALIDAR com o catálogo do fornecedor].</summary>
[DataContract]
public sealed class ModeloTela
{
    [DataMember(Order = 1)] public string Nome { get; set; } = "";
    [DataMember(Order = 2)] public double PhiLongitudinal { get; set; }
    [DataMember(Order = 3)] public double PhiTransversal { get; set; }
    /// <summary>Espaçamentos (cm).</summary>
    [DataMember(Order = 4)] public double EspLongitudinal { get; set; } = 10;
    [DataMember(Order = 5)] public double EspTransversal { get; set; } = 10;
    /// <summary>Painel padrão (cm).</summary>
    [DataMember(Order = 6)] public double Largura { get; set; } = 245;
    [DataMember(Order = 7)] public double Comprimento { get; set; } = 600;
    /// <summary>Massa (kg/m²).</summary>
    [DataMember(Order = 8)] public double PesoM2 { get; set; }
    [DataMember(Order = 9)] public string Aco { get; set; } = "CA-60";

    [OnDeserializing]
    private void AoDesserializar(StreamingContext _) => Configuracao.Padroes.Copiar(new ModeloTela(), this);
}

public static class Catalogos
{
    public static List<ModeloTrelica> TrelicasPadrao() => new List<ModeloTrelica>
    {
        T("TR 8644", 8, 6.0, 4.2, 4.2, 0.735),
        T("TR 12645", 12, 6.0, 4.2, 5.0, 0.886),
        T("TR 16745", 16, 7.0, 4.2, 5.0, 1.016),
        T("TR 20745", 20, 7.0, 4.2, 5.0, 1.091),
        T("TR 25856", 25, 8.0, 5.0, 6.0, 1.536),
        T("TR 30856", 30, 8.0, 5.0, 6.0, 1.611),
    };

    private static ModeloTrelica T(string nome, double h, double sup, double diag, double inf, double peso) =>
        new ModeloTrelica { Nome = nome, Altura = h, PhiSuperior = sup, PhiDiagonal = diag, PhiInferior = inf, PesoMetro = peso };

    public static List<ModeloTela> TelasPadrao() => new List<ModeloTela>
    {
        Q("Q-61", 3.4, 15, 0.97), Q("Q-75", 3.8, 15, 1.21), Q("Q-92", 4.2, 15, 1.48),
        Q("Q-113", 3.8, 10, 1.80), Q("Q-138", 4.2, 10, 2.20), Q("Q-159", 4.5, 10, 2.52),
        Q("Q-196", 5.0, 10, 3.11), Q("Q-246", 5.6, 10, 3.91), Q("Q-283", 6.0, 10, 4.48),
        Q("Q-335", 6.3, 10, 5.37), Q("Q-396", 7.1, 10, 6.28), Q("Q-503", 8.0, 10, 7.97),
        Q("Q-636", 9.0, 10, 10.09), Q("Q-785", 10.0, 10, 12.46),
    };

    private static ModeloTela Q(string nome, double phi, double esp, double peso) => new ModeloTela
    {
        Nome = nome, PhiLongitudinal = phi, PhiTransversal = phi, EspLongitudinal = esp, EspTransversal = esp, PesoM2 = peso,
    };

    public static ModeloTrelica Trelica(this IEnumerable<ModeloTrelica> lista, string nome) =>
        lista.FirstOrDefault(m => string.Equals(Normalizar(m.Nome), Normalizar(nome), StringComparison.OrdinalIgnoreCase));

    public static ModeloTela Tela(this IEnumerable<ModeloTela> lista, string nome) =>
        lista.FirstOrDefault(m => string.Equals(Normalizar(m.Nome), Normalizar(nome), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Sinusoide (diagonais) da treliça em coordenadas locais: x ao longo do banzo inferior (0..L),
    /// y = 0 embaixo e H em cima, um vértice a cada meio passo; o último fica em x = L.
    /// </summary>
    public static List<(double X, double Y)> Sinusoide(double comprimento, double altura, double passo)
    {
        var r = new List<(double, double)>();
        if (comprimento <= 0 || passo <= 0) return r;
        double meio = passo / 2;
        int k = 0;
        for (; k * meio < comprimento - 1e-9; k++) r.Add((k * meio, k % 2 == 0 ? 0 : altura));
        var (px, py) = r[r.Count - 1];
        double proxY = k % 2 == 0 ? 0 : altura;
        r.Add((comprimento, py + (proxY - py) * (comprimento - px) / meio));
        return r;
    }

    /// <summary>"TR8644", "tr 8644", "Q138" → comparáveis.</summary>
    public static string Normalizar(string s) => (s ?? "").Replace(" ", "").Replace("-", "");
}
