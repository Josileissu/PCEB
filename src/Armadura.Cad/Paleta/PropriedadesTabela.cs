using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Armadura.Core.RelacaoAco;

namespace Armadura.Cad.Paleta;

/// <summary>Opções de uma tabela de relação do aço, como o PropertyGrid as mostra.</summary>
[TypeConverter(typeof(OrdemDeclaradaConverter))]
internal sealed class PropriedadesTabela
{
    private const string G = "Relação do aço";

    internal ObjectId Id { get; }
    internal OpcoesTabelaRA O { get; }
    internal List<Action<OpcoesTabelaRA>> Pendentes { get; } = new List<Action<OpcoesTabelaRA>>();
    /// <summary>Novo tipo de numeração do DESENHO (não é opção da tabela): aplicado pela paleta.</summary>
    internal Core.Numeracao.TipoNumeracao? NovaNumeracao { get; private set; }
    private Core.Numeracao.TipoNumeracao _numeracao;

    internal PropriedadesTabela(ObjectId id, OpcoesTabelaRA o, Core.Numeracao.TipoNumeracao numeracao)
    {
        Id = id; O = o; _numeracao = numeracao;
    }

    [Category(G), DisplayName("Numeração (do desenho)"), TypeConverter(typeof(TipoNumeracaoConverter)),
     Description("Global: barras iguais têm o mesmo N no desenho todo. Individual: recomeça em N1 em cada " +
                 "detalhamento (bloco). Sequencial: por detalhamento, sem repetir números. Vale para todas as tabelas do desenho.")]
    public Core.Numeracao.TipoNumeracao Numeracao
    {
        get => _numeracao;
        set { _numeracao = value; NovaNumeracao = value; }
    }

    [Category(G), DisplayName("Coluna do detalhamento"), TypeConverter(typeof(ColunaElementoConverter)),
     Description("Coluna ELEMENTO com o nome do bloco/detalhamento de cada posição. Automática: só com numeração Individual ou Sequencial.")]
    public ColunaElemento ColunaElemento
    {
        get => O.ColunaElemento;
        set { O.ColunaElemento = value; Pendentes.Add(o => o.ColunaElemento = value); }
    }

    [Category(G), DisplayName("Nome no título"), TypeConverter(typeof(SimNaoConverter)),
     Description("RA parcial: o título mostra o(s) detalhamento(s) da tabela, ex.: \"RELAÇÃO DO AÇO - V1\".")]
    public bool NomeNoTitulo
    {
        get => O.NomeNoTitulo;
        set { O.NomeNoTitulo = value; Pendentes.Add(o => o.NomeNoTitulo = value); }
    }

    [Category(G), DisplayName("Apresentação"), TypeConverter(typeof(ApresentacaoRAConverter)),
     Description("Relação e resumo, só a relação, ou só o resumo.")]
    public ApresentacaoRA Apresentacao
    {
        get => O.Apresentacao;
        set { O.Apresentacao = value; Pendentes.Add(o => o.Apresentacao = value); }
    }

    [Category(G), DisplayName("Resumo"), TypeConverter(typeof(ApresentacaoResumoConverter)),
     Description("Colunas do resumo: peso (kg), quantidade de barras comerciais, ou os dois.")]
    public ApresentacaoResumo Resumo
    {
        get => O.Resumo;
        set { O.Resumo = value; Pendentes.Add(o => o.Resumo = value); }
    }

    [Category(G), DisplayName("Escala"), TypeConverter(typeof(EscalaConverter)),
     Description("Escala de desenho da tabela (1:n): altura do texto.")]
    public double Escala
    {
        get => O.Escala > 0 ? O.Escala : 10;
        set
        {
            if (value <= 0) throw new ArgumentException("A escala deve ser positiva (ex.: 1:10).");
            O.Escala = value;
            Pendentes.Add(o => o.Escala = value);
        }
    }

    [Category(G), DisplayName("Detalhamentos (RA parcial)"),
     Description("Blocos listados por esta relação, separados por vírgula. Vazio = desenho inteiro " +
                 "(ou o bloco em que a tabela está). Use ARMRAASSOCIAR para mover blocos entre relações.")]
    public string Blocos
    {
        get => string.Join(", ", O.Blocos);
        set
        {
            var lista = new List<string>();
            foreach (var p in (value ?? "").Split(','))
                if (p.Trim().Length > 0 && !lista.Contains(p.Trim(), StringComparer.OrdinalIgnoreCase)) lista.Add(p.Trim());
            O.Blocos = lista;
            Pendentes.Add(o => o.Blocos = new List<string>(lista));
        }
    }

    [Category(G), DisplayName("Detalhe de dobra"), TypeConverter(typeof(SimNaoConverter)),
     Description("Acrescenta no fim da relação a coluna \"DETALHE DE DOBRA (DIMENSÕES EXTERNAS EM cm)\" com a forma de cada posição.")]
    public bool DetalheDobra
    {
        get => O.DetalheDobra;
        set { O.DetalheDobra = value; Pendentes.Add(o => o.DetalheDobra = value); }
    }

    [Category(G), DisplayName("Mostrar peso total"), TypeConverter(typeof(SimNaoConverter)),
     Description("Linhas de peso total por tipo de aço (e geral, quando há mais de um aço).")]
    public bool MostrarPesoTotal
    {
        get => O.MostrarPesoTotal;
        set { O.MostrarPesoTotal = value; Pendentes.Add(o => o.MostrarPesoTotal = value); }
    }
}

/// <summary>Enum com nomes em português no PropertyGrid.</summary>
internal abstract class NomesEnumConverter<T> : EnumConverter where T : struct, Enum
{
    private readonly (T valor, string nome)[] _nomes;

    protected NomesEnumConverter(params (T, string)[] nomes) : base(typeof(T)) { _nomes = nomes; }

    public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => true;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) =>
        new StandardValuesCollection(_nomes.Select(n => (object)n.valor).ToArray());

    public override object ConvertTo(ITypeDescriptorContext c, CultureInfo cul, object v, Type t)
    {
        if (t == typeof(string) && v is T e)
        {
            var n = _nomes.FirstOrDefault(x => x.valor.Equals(e));
            if (n.nome != null) return n.nome;
        }
        return base.ConvertTo(c, cul, v, t);
    }

    public override object ConvertFrom(ITypeDescriptorContext c, CultureInfo cul, object v)
    {
        if (v is string s)
        {
            var n = _nomes.FirstOrDefault(x => string.Equals(x.nome, s, StringComparison.OrdinalIgnoreCase));
            if (n.nome != null) return n.valor;
        }
        return base.ConvertFrom(c, cul, v);
    }
}

internal sealed class ApresentacaoRAConverter : NomesEnumConverter<ApresentacaoRA>
{
    public ApresentacaoRAConverter() : base(
        (ApresentacaoRA.TabelaEResumo, "Tabela e resumo"),
        (ApresentacaoRA.SomenteTabela, "Somente tabela"),
        (ApresentacaoRA.SomenteResumo, "Somente resumo")) { }
}

internal sealed class TipoNumeracaoConverter : NomesEnumConverter<Core.Numeracao.TipoNumeracao>
{
    public TipoNumeracaoConverter() : base(
        (Core.Numeracao.TipoNumeracao.Global, "Global"),
        (Core.Numeracao.TipoNumeracao.Individual, "Individual"),
        (Core.Numeracao.TipoNumeracao.Sequencial, "Sequencial")) { }
}

internal sealed class ColunaElementoConverter : NomesEnumConverter<ColunaElemento>
{
    public ColunaElementoConverter() : base(
        (ColunaElemento.Automatica, "Automática"),
        (ColunaElemento.Sempre, "Sempre"),
        (ColunaElemento.Nunca, "Nunca")) { }
}

internal sealed class ApresentacaoResumoConverter : NomesEnumConverter<ApresentacaoResumo>
{
    public ApresentacaoResumoConverter() : base(
        (ApresentacaoResumo.Peso, "Peso (kg)"),
        (ApresentacaoResumo.Barras, "Barras"),
        (ApresentacaoResumo.BarrasEPeso, "Barras e peso")) { }
}
