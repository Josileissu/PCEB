using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace DEB.Core.Tabelas;

public enum Fabricacao { Barra, Rolo }

/// <summary>Uma linha da configuração "Barras" (seção 4.6 da especificação).</summary>
[DataContract]
public sealed class Bitola
{
    /// <summary>Diâmetro nominal em mm.</summary>
    [DataMember(Order = 1)] public double Diametro { get; set; }
    [DataMember(Order = 2)] public bool Ativa { get; set; } = true;
    [DataMember(Order = 3)] public string Aco { get; set; } = "CA-50";
    [DataMember(Order = 4)] public Fabricacao Fabricacao { get; set; } = Fabricacao.Barra;
    /// <summary>Comprimento comercial em cm.</summary>
    [DataMember(Order = 5)] public double ComprimentoComercial { get; set; } = 1200;
    /// <summary>Peso do rolo em kg (só para Fabricacao = Rolo).</summary>
    [DataMember(Order = 6)] public double PesoRolo { get; set; }
    /// <summary>Traspasse fixo em cm (0 = não definido).</summary>
    [DataMember(Order = 7)] public double TraspasseFixo { get; set; }
    /// <summary>Massa linear em kg/m.</summary>
    [DataMember(Order = 8)] public double MassaLinear { get; set; }

    public override string ToString() => $"ø{Diametro:0.0} {Aco}";

    [OnDeserializing]
    private void AoDesserializar(StreamingContext _) => Configuracao.Padroes.Copiar(new Bitola(), this);
}

/// <summary>Tabela de bitolas. Valores padrão: NBR 7480 [VALIDAR NBR] (massa ≈ 0,006165·φ²).</summary>
[DataContract]
public sealed class TabelaBarras
{
    [DataMember(Order = 1)] public List<Bitola> Bitolas { get; set; } = new List<Bitola>();

    public IEnumerable<Bitola> Ativas => Bitolas.Where(b => b.Ativa).OrderBy(b => b.Diametro);

    /// <summary>Procura pela bitola (tolerância 0,05 mm); com aço informado, prefere o mesmo aço.</summary>
    public Bitola Obter(double diametro, string aco = null)
    {
        var candidatas = Bitolas.Where(b => Math.Abs(b.Diametro - diametro) < 0.05).ToList();
        if (candidatas.Count == 0) return null;
        return candidatas.FirstOrDefault(b => string.Equals(b.Aco, aco, StringComparison.OrdinalIgnoreCase))
               ?? candidatas[0];
    }

    /// <summary>Massa linear (kg/m) da tabela; se a bitola não existir, calcula por 7850 kg/m³.</summary>
    public double MassaLinear(double diametro, string aco = null)
    {
        var b = Obter(diametro, aco);
        return b != null && b.MassaLinear > 0 ? b.MassaLinear : MassaTeorica(diametro);
    }

    [OnDeserialized]
    private void AoTerminar(StreamingContext _) => Bitolas ??= new List<Bitola>();

    public static double MassaTeorica(double diametroMm) => Math.Round(0.006165 * diametroMm * diametroMm, 3);

    public static TabelaBarras Padrao()
    {
        var t = new TabelaBarras();
        void Add(double d, string aco, bool ativa, Fabricacao fab, double massa) =>
            t.Bitolas.Add(new Bitola
            {
                Diametro = d, Aco = aco, Ativa = ativa, Fabricacao = fab, MassaLinear = massa,
                ComprimentoComercial = 1200, PesoRolo = fab == Fabricacao.Rolo ? 170 : 0
            });

        Add(4.2, "CA-60", false, Fabricacao.Rolo, 0.109);
        Add(5.0, "CA-60", true, Fabricacao.Rolo, 0.154);
        Add(6.0, "CA-60", false, Fabricacao.Rolo, 0.222);
        Add(6.3, "CA-50", true, Fabricacao.Barra, 0.245);
        Add(8.0, "CA-50", true, Fabricacao.Barra, 0.395);
        Add(10.0, "CA-50", true, Fabricacao.Barra, 0.617);
        Add(12.5, "CA-50", true, Fabricacao.Barra, 0.963);
        Add(16.0, "CA-50", true, Fabricacao.Barra, 1.578);
        Add(20.0, "CA-50", true, Fabricacao.Barra, 2.466);
        Add(22.0, "CA-50", false, Fabricacao.Barra, 2.984);
        Add(25.0, "CA-50", true, Fabricacao.Barra, 3.853);
        Add(32.0, "CA-50", false, Fabricacao.Barra, 6.313);
        Add(40.0, "CA-50", false, Fabricacao.Barra, 9.865);
        t.AcrescentarCordoalhas();
        return t;
    }

    /// <summary>Aço de protensão (cordoalha/fio CP): sem ganchos, sem traspasse, aço na descrição.</summary>
    public static bool EhCordoalha(string aco) =>
        !string.IsNullOrEmpty(aco) && aco.TrimStart().StartsWith("CP", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Cordoalhas de 7 fios CP-190 RB (NBR 7483), fornecidas em bobina. Massas nominais
    /// [VALIDAR com o catálogo do fornecedor]. Só acrescenta as que faltam.
    /// </summary>
    public void AcrescentarCordoalhas()
    {
        void Add(double d, double massa)
        {
            if (Bitolas.Any(b => Math.Abs(b.Diametro - d) < 0.05 && EhCordoalha(b.Aco))) return;
            Bitolas.Add(new Bitola
            {
                Diametro = d, Aco = "CP-190", Ativa = true, Fabricacao = Fabricacao.Rolo, MassaLinear = massa,
                ComprimentoComercial = 0, PesoRolo = 0,
            });
        }
        Add(9.5, 0.441);
        Add(12.7, 0.792);
        Add(15.2, 1.126);
    }
}
