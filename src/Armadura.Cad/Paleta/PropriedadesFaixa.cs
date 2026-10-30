using System;
using System.Collections.Generic;
using System.ComponentModel;
using Armadura.Core.Modelo;

namespace Armadura.Cad.Paleta;

/// <summary>Uma faixa (cota, corte ou chamada) como o PropertyGrid a mostra.</summary>
[TypeConverter(typeof(OrdemDeclaradaConverter))]
internal sealed class PropriedadesFaixa : IPropriedadesCondicionais
{
    public bool Mostrar(string condicao) => condicao switch
    {
        "Chamada" => F.TemLinhasDeChamada,
        "Quantidade" => F.TextoTemQuantidade,
        "DiametroEspacamento" => F.TextoTemDiametroEspacamento,
        "Cota" => F.TemCota,
        _ => true,
    };

    private const string G = "Faixa";

    internal ObjectId Id { get; }
    internal DadosFaixa F { get; }
    internal List<Action<DadosFaixa>> Pendentes { get; } = new List<Action<DadosFaixa>>();
    private readonly double _escalaBarra;

    internal PropriedadesFaixa(ObjectId id, DadosFaixa f, double escalaBarra)
    {
        Id = id; F = f; _escalaBarra = escalaBarra;
    }

    [Category(G), DisplayName("Tipo"), ReadOnly(true)]
    public string Tipo => F.Tipo switch
    {
        TipoFaixa.Ortogonal => "Faixa ortogonal",
        TipoFaixa.Alinhada => "Faixa alinhada",
        TipoFaixa.CorteCotaOrtogonal => "Corte + cota ortogonal",
        TipoFaixa.CorteCotaAlinhada => "Corte + cota alinhada",
        TipoFaixa.CorteChamada => "Corte + chamada",
        TipoFaixa.CorteChamadaArco => "Corte + chamada em arco",
        TipoFaixa.Chamada => "Chamada",
        _ => "Indicação",
    };

    [Category(G), DisplayName("Escala"), TypeConverter(typeof(EscalaConverter)),
     Description("Escala da faixa (textos, cota e círculos do corte). Pode ser diferente da escala da barra: " +
                 "ex.: barra na elevação 1:50 e distribuição na seção 1:25.")]
    public double Escala
    {
        get => F.EscalaEfetiva(_escalaBarra);
        set
        {
            if (value <= 0) throw new ArgumentException("A escala deve ser positiva (ex.: 1:25).");
            F.Escala = value;
            Pendentes.Add(f => f.Escala = value);
        }
    }

    [Category(G), DisplayName("Usar a escala da barra"), TypeConverter(typeof(SimNaoConverter)),
     Description("Sim: a faixa acompanha a escala da barra (comportamento anterior).")]
    public bool EscalaDaBarra
    {
        get => F.Escala <= 0;
        set
        {
            if (!value) { if (F.Escala <= 0) { F.Escala = _escalaBarra; Pendentes.Add(f => f.Escala = _escalaBarra); } return; }
            F.Escala = 0;
            Pendentes.Add(f => f.Escala = 0);
        }
    }

    [Category(G), DisplayName("Mostrar linha de chamada"), TypeConverter(typeof(SimNaoConverter)), SoQuando("Chamada"),
     Description("Não: apaga as linhas de chamada e deixa só o texto (os pontos ficam guardados para voltar a mostrar). " +
                 "Linha apagada à mão não volta sozinha; Sim recria todas.")]
    public bool MostrarLinhas
    {
        get => !F.OcultarLinhas && F.LinhasApagadas.Count == 0;
        set
        {
            void Aplicar(DadosFaixa f) { f.OcultarLinhas = !value; if (value) f.LinhasApagadas.Clear(); }
            Aplicar(F);
            Pendentes.Add(Aplicar);
        }
    }

    [Category(G), DisplayName("Patamar na chamada"), TypeConverter(typeof(SimNaoConverter)), SoQuando("Chamada"),
     Description("Sim: as linhas das barras se encontram ao lado do texto e um trecho horizontal vai até ele (\"N12 ——\", " +
                 "como nas pranchas do escritório).")]
    public bool Patamar
    {
        get => F.Patamar;
        set => Campo(f => f.Patamar = value);
    }

    [Category(G), DisplayName("Repetições"), SoQuando("Quantidade"),
     Description("Multiplica a quantidade que a faixa soma na barra (ex.: a mesma distribuição em 2 camadas). " +
                 "O texto fica \"2x11 N1 …\" (sem parênteses).")]
    public int Repeticoes
    {
        get => Math.Max(1, F.Repeticoes);
        set
        {
            if (value < 1) throw new ArgumentException("O mínimo é 1 (sem repetição).");
            Campo(f => f.Repeticoes = value);
        }
    }

    private const string GT = "Texto da faixa";

    [Category(GT), DisplayName("Mostrar quantidade"), TypeConverter(typeof(SimNaoConverter)), SoQuando("Quantidade")]
    public bool MostrarQuantidade
    {
        get => F.MostrarQuantidade;
        set => Campo(f => f.MostrarQuantidade = value);
    }

    [Category(GT), DisplayName("Mostrar posição (N)"), TypeConverter(typeof(SimNaoConverter))]
    public bool MostrarPosicao
    {
        get => F.MostrarPosicao;
        set => Campo(f => f.MostrarPosicao = value);
    }

    [Category(GT), DisplayName("Mostrar diâmetro"), TypeConverter(typeof(SimNaoConverter)), SoQuando("DiametroEspacamento")]
    public bool MostrarDiametro
    {
        get => F.MostrarDiametro;
        set => Campo(f => f.MostrarDiametro = value);
    }

    [Category(GT), DisplayName("Mostrar comprimento"), TypeConverter(typeof(SimNaoConverter)), SoQuando("Cota"),
     Description("Comprimento da faixa abaixo da linha de cota. Desligue quando várias faixas ficam juntas.")]
    public bool MostrarComprimento
    {
        get => F.MostrarComprimento;
        set => Campo(f => f.MostrarComprimento = value);
    }

    [Category(GT), DisplayName("Mostrar espaçamento"), TypeConverter(typeof(SimNaoConverter)), SoQuando("DiametroEspacamento")]
    public bool MostrarEspacamento
    {
        get => F.MostrarEspacamento;
        set => Campo(f => f.MostrarEspacamento = value);
    }

    private void Campo(Action<DadosFaixa> aplicar)
    {
        aplicar(F);
        Pendentes.Add(aplicar);
    }

    [Category(G), DisplayName("Espaçamento (cm)"), TypeConverter(typeof(CmConverter))]
    public double Espacamento
    {
        get => F.Espacamento;
        set
        {
            if (value <= 0) throw new ArgumentException("O espaçamento deve ser positivo.");
            F.Espacamento = value;
            Pendentes.Add(f => f.Espacamento = value);
        }
    }

    [Category(G), DisplayName("Quantidade"),
     Description("Calculada pelo comprimento e pelo espaçamento; só a chamada tem quantidade digitada.")]
    public int Quantidade
    {
        get => F.Quantidade;
        set
        {
            if (!F.QuantidadeDigitada) throw new ArgumentException("A quantidade desta faixa é calculada pelo comprimento e pelo espaçamento.");
            if (value < 1) throw new ArgumentException("A quantidade mínima é 1.");
            F.Quantidade = value;
            Pendentes.Add(f => f.Quantidade = value);
        }
    }
}
