using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using Armadura.Core.Modelo;

namespace Armadura.Cad.Paleta;

/// <summary>Detalhamento (bloco selecionado) no PropertyGrid.</summary>
[TypeConverter(typeof(OrdemDeclaradaConverter))]
internal sealed class PropriedadesDetalhamento
{
    private const string G = "Detalhamento";

    internal ObjectId BlocoId { get; }
    internal DadosDetalhamento D { get; }
    internal string NomeOriginal { get; }
    internal string NovoNome { get; private set; }
    internal List<Action<DadosDetalhamento>> Pendentes { get; } = new List<Action<DadosDetalhamento>>();
    private readonly int _repeticoes;

    internal PropriedadesDetalhamento(ObjectId blocoId, string nome, DadosDetalhamento d, int repeticoes)
    {
        BlocoId = blocoId; NomeOriginal = nome; NovoNome = nome; D = d; _repeticoes = repeticoes;
    }

    [Category(G), DisplayName("Nome"), Description("Nome do bloco (único no desenho). Renomear acerta as relações parciais.")]
    public string Nome
    {
        get => NovoNome;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Informe um nome.");
            NovoNome = value.Trim();
            Pendentes.Add(_ => { }); // marca alteração (o nome é aplicado à parte)
        }
    }

    [Category(G), DisplayName("Grupo"), Description("Vigas, Lajes, Pilares, Escadas, Fundações… (texto livre).")]
    public string Grupo { get => D.Grupo; set { D.Grupo = value ?? ""; Pendentes.Add(d => d.Grupo = value ?? ""); } }

    [Category(G), DisplayName("Pavimento")]
    public string Pavimento { get => D.Pavimento; set { D.Pavimento = value ?? ""; Pendentes.Add(d => d.Pavimento = value ?? ""); } }

    [Category(G), DisplayName("Repetições"),
     Description("Quantidade de peças iguais: multiplica as barras na relação do aço. Vem do número de inserções do bloco; " +
                 "digite outro valor quando houver mais peças do mesmo detalhamento (0 = volta a contar as inserções).")]
    public int Repeticoes
    {
        get => D.Repeticoes > 0 ? D.Repeticoes : _repeticoes;
        set
        {
            if (value < 0) throw new ArgumentException("Repetições negativas.");
            int r = value == _repeticoes && D.Repeticoes == 0 ? 0 : value; // igual às inserções: continua automático
            D.Repeticoes = r;
            Pendentes.Add(d => d.Repeticoes = r);
        }
    }

    [Category(G), DisplayName("Inserções no desenho"), ReadOnly(true),
     Description("Quantas vezes o bloco está inserido (as repetições usam este número enquanto não forem digitadas).")]
    public int Insercoes => _repeticoes;

    [Category(G), DisplayName("Classe do concreto")]
    public string Classe { get => D.Classe; set { D.Classe = value ?? ""; Pendentes.Add(d => d.Classe = value ?? ""); } }

    [Category(G), DisplayName("Volume de concreto (m³)"), TypeConverter(typeof(CmConverter)), Description("De uma peça.")]
    public double Volume
    {
        get => D.Volume;
        set { if (value < 0) throw new ArgumentException("Volume negativo."); D.Volume = value; Pendentes.Add(d => d.Volume = value); }
    }

    [Category(G), DisplayName("Área de forma (m²)"), TypeConverter(typeof(CmConverter)), Description("De uma peça.")]
    public double AreaForma
    {
        get => D.AreaForma;
        set { if (value < 0) throw new ArgumentException("Área negativa."); D.AreaForma = value; Pendentes.Add(d => d.AreaForma = value); }
    }

    [Category(G), DisplayName("Sistema construtivo"), TypeConverter(typeof(SistemaConverter))]
    public SistemaConstrutivo Sistema { get => D.Sistema; set { D.Sistema = value; Pendentes.Add(d => d.Sistema = value); } }

    [Category(G), DisplayName("Comentários"), Description("Não aparecem no desenho.")]
    public string Comentarios { get => D.Comentarios; set { D.Comentarios = value ?? ""; Pendentes.Add(d => d.Comentarios = value ?? ""); } }

    [Category(G), DisplayName("Volume total (m³)"), ReadOnly(true)]
    public string VolumeTotal => (D.Volume * Math.Max(1, Repeticoes)).ToString("0.###", CultureInfo.InvariantCulture);
}

internal sealed class SistemaConverter : NomesEnumConverter<SistemaConstrutivo>
{
    public SistemaConverter() : base(
        (SistemaConstrutivo.MoldadoInLoco, "Moldado in loco"),
        (SistemaConstrutivo.PreMoldado, "Pré-moldado"),
        (SistemaConstrutivo.AlvenariaEstrutural, "Alvenaria estrutural")) { }
}
