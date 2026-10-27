using System;
using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Armadura.Cad.Infra;
using Armadura.Core.Modelo;

namespace Armadura.Cad.Paleta;

/// <summary>Sim/Não em vez de True/False.</summary>
internal sealed class SimNaoConverter : BooleanConverter
{
    public override object ConvertTo(ITypeDescriptorContext c, CultureInfo cul, object v, Type t) =>
        t == typeof(string) && v is bool b ? (b ? "Sim" : "Não") : base.ConvertTo(c, cul, v, t);

    public override object ConvertFrom(ITypeDescriptorContext c, CultureInfo cul, object v) =>
        v is string s ? s.Trim().StartsWith("S", StringComparison.OrdinalIgnoreCase) : base.ConvertFrom(c, cul, v);
}

/// <summary>Lista das bitolas ativas da configuração; aceita também digitar o valor.</summary>
internal sealed class BitolaConverter : DoubleConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext c) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => false;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) =>
        new StandardValuesCollection(ConfigDesenho.Obter(CadApp.DocumentManager.MdiActiveDocument?.Database).Barras.Ativas.Select(b => b.Diametro).Distinct().ToArray());

    public override object ConvertTo(ITypeDescriptorContext c, CultureInfo cul, object v, Type t) =>
        t == typeof(string) && v is double d ? d.ToString("0.0", CultureInfo.InvariantCulture) : base.ConvertTo(c, cul, v, t);

    public override object ConvertFrom(ITypeDescriptorContext c, CultureInfo cul, object v) =>
        v is string s ? double.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture) : base.ConvertFrom(c, cul, v);
}

/// <summary>Números em cm: aceita vírgula ou ponto.</summary>
internal sealed class CmConverter : DoubleConverter
{
    public override object ConvertTo(ITypeDescriptorContext c, CultureInfo cul, object v, Type t) =>
        t == typeof(string) && v is double d ? Math.Round(d, 2).ToString("0.##", CultureInfo.InvariantCulture) : base.ConvertTo(c, cul, v, t);

    public override object ConvertFrom(ITypeDescriptorContext c, CultureInfo cul, object v) =>
        v is string s ? double.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture) : base.ConvertFrom(c, cul, v);
}

/// <summary>Escala como "1:10"; aceita "1:25", "1/25" ou "25". Lista as escalas usuais.</summary>
internal sealed class EscalaConverter : DoubleConverter
{
    private static readonly double[] Usuais = { 1, 2, 5, 10, 20, 25, 50, 75, 100 };

    public override bool GetStandardValuesSupported(ITypeDescriptorContext c) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => false;
    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) => new StandardValuesCollection(Usuais);

    public override object ConvertTo(ITypeDescriptorContext c, CultureInfo cul, object v, Type t) =>
        t == typeof(string) && v is double d ? "1:" + Math.Round(d, 2).ToString("0.##", CultureInfo.InvariantCulture) : base.ConvertTo(c, cul, v, t);

    public override object ConvertFrom(ITypeDescriptorContext c, CultureInfo cul, object v)
    {
        if (v is string s)
        {
            s = s.Trim().Replace(',', '.');
            int sep = s.IndexOfAny(new[] { ':', '/' });
            if (sep >= 0) s = s.Substring(sep + 1);
            return double.Parse(s.Trim(), CultureInfo.InvariantCulture);
        }
        return base.ConvertFrom(c, cul, v);
    }
}

/// <summary>Modelos do catálogo: treliças para treliças, telas para telas.</summary>
internal sealed class ModeloConverter : StringConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext c) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => true;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c)
    {
        var cfg = ConfigDesenho.Obter(CadApp.DocumentManager.MdiActiveDocument?.Database);
        var p = c?.Instance as PropriedadesBarra ?? (c?.Instance as object[])?.OfType<PropriedadesBarra>().FirstOrDefault();
        if (p != null && p.D.EhTela) return new StandardValuesCollection(cfg.Telas.Select(t => t.Nome).ToArray());
        if (p != null && p.D.EhTrelica) return new StandardValuesCollection(cfg.Trelicas.Select(t => t.Nome).ToArray());
        return new StandardValuesCollection(new string[0]);
    }
}

internal sealed class AcoConverter : StringConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext c) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => false;
    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) =>
        new StandardValuesCollection(new[] { "CA-25", "CA-50", "CA-60", "CP-190" });
}

/// <summary>Formato do segmento com nomes em português.</summary>
internal sealed class FormatoConverter : EnumConverter
{
    private static readonly (FormatoSegmento f, string nome)[] Nomes =
    {
        (FormatoSegmento.Linha, "Linha contínua"),
        (FormatoSegmento.Gancho45, "Gancho 45°"),
        (FormatoSegmento.Gancho90, "Gancho 90°"),
        (FormatoSegmento.Gancho180, "Gancho 180°"),
        (FormatoSegmento.Curva, "Curva"),
    };

    public FormatoConverter() : base(typeof(FormatoSegmento)) { }

    public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => true;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c)
    {
        // segmento reto: linha ou ganchos; em arco: só curva
        bool arco = c?.Instance is PropriedadesSegmento s && s.EhArco;
        var lista = arco
            ? new ArrayList { FormatoSegmento.Curva }
            : new ArrayList { FormatoSegmento.Linha, FormatoSegmento.Gancho45, FormatoSegmento.Gancho90, FormatoSegmento.Gancho180 };
        return new StandardValuesCollection(lista);
    }

    public override object ConvertTo(ITypeDescriptorContext c, CultureInfo cul, object v, Type t)
    {
        if (t == typeof(string) && v is FormatoSegmento f)
            return Nomes.First(n => n.f == f).nome;
        return base.ConvertTo(c, cul, v, t);
    }

    public override object ConvertFrom(ITypeDescriptorContext c, CultureInfo cul, object v)
    {
        if (v is string s)
        {
            var n = Nomes.FirstOrDefault(x => string.Equals(x.nome, s, StringComparison.OrdinalIgnoreCase));
            if (n.nome != null) return n.f;
        }
        return base.ConvertFrom(c, cul, v);
    }
}

/// <summary>Mostra os segmentos como "Segmento 1", "Segmento 2"… expansíveis.</summary>
internal sealed class ListaSegmentosConverter : TypeConverter
{
    public override bool GetPropertiesSupported(ITypeDescriptorContext c) => true;

    public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext c, object valor, Attribute[] atr)
    {
        var lista = (ListaSegmentos)valor;
        return new PropertyDescriptorCollection(
            lista.Itens.Select((s, i) => (PropertyDescriptor)new ItemDescriptor(i, s)).ToArray());
    }

    public override object ConvertTo(ITypeDescriptorContext c, CultureInfo cul, object v, Type t) =>
        t == typeof(string) && v is ListaSegmentos l ? $"{l.Itens.Count} segmento(s)" : base.ConvertTo(c, cul, v, t);

    private sealed class ItemDescriptor : PropertyDescriptor
    {
        private readonly PropriedadesSegmento _s;
        public ItemDescriptor(int i, PropriedadesSegmento s) : base($"Segmento {i + 1}", null) { _s = s; }
        public override Type ComponentType => typeof(ListaSegmentos);
        public override bool IsReadOnly => true;
        public override Type PropertyType => typeof(PropriedadesSegmento);
        public override TypeConverter Converter => new ExpandableObjectConverter();
        public override bool CanResetValue(object c) => false;
        public override object GetValue(object c) => _s;
        public override void ResetValue(object c) { }
        public override void SetValue(object c, object v) { }
        public override bool ShouldSerializeValue(object c) => false;
    }
}

/// <summary>Propriedade que só aparece quando o objeto atende a condição (ex.: "Tela").</summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class SoQuandoAttribute : Attribute
{
    public SoQuandoAttribute(string condicao) { Condicao = condicao; }
    public string Condicao { get; }
}

/// <summary>Objeto da paleta que esconde propriedades que não se aplicam a ele.</summary>
internal interface IPropriedadesCondicionais
{
    bool Mostrar(string condicao);
}

/// <summary>
/// Mantém as propriedades na ordem em que foram declaradas (o PropertyGrid ordena por nome) e esconde as
/// marcadas com <see cref="SoQuandoAttribute"/> que não se aplicam ao objeto.
/// </summary>
internal sealed class OrdemDeclaradaConverter : ExpandableObjectConverter
{
    public override bool GetPropertiesSupported(ITypeDescriptorContext c) => true;

    public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext c, object valor, Attribute[] atr)
    {
        var props = TypeDescriptor.GetProperties(valor, atr);
        if (valor is IPropriedadesCondicionais cond)
            props = new PropertyDescriptorCollection(props.Cast<PropertyDescriptor>()
                .Where(p => !(p.Attributes[typeof(SoQuandoAttribute)] is SoQuandoAttribute s) || cond.Mostrar(s.Condicao)).ToArray());
        var nomes = valor.GetType().GetProperties().OrderBy(p => p.MetadataToken).Select(p => p.Name).ToArray();
        return props.Sort(nomes);
    }
}
