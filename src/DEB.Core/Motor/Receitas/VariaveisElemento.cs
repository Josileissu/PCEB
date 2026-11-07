using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using DEB.Core.Motor.Componentes;

namespace DEB.Core.Motor.Receitas;

/// <summary>
/// Variáveis que uma receita enxerga, em ordem de prioridade:
/// <list type="number">
/// <item>textos definidos pela própria receita (variável cuja fórmula deu texto);</item>
/// <item>as variáveis já calculadas (<see cref="ContextoMotor.Variaveis"/>: as da receita e as publicadas pelos componentes);</item>
/// <item>as propriedades públicas do elemento em snake_case (AlturaBase → altura_base, DiamEstaca → diam_estaca; enums
///   como texto, bool como 1/0; listas ficam de fora), mais <c>n_pilares</c> e <c>repeticoes</c> (efetivo: 0 no elemento
///   = número de pilares, mínimo 1);</item>
/// <item><c>arm.&lt;funcao&gt;.bitola|quantidade|espacamento</c> (0 quando a função não está no elemento; funções aceitas =
///   as de <see cref="Funcoes"/> e as presentes em <see cref="ElementoEstrutural.Armaduras"/>);</item>
/// <item><c>perfil.&lt;chave&gt;</c> = <see cref="PerfilDetalhamento.Padroes"/> (regras pelo regra('…')).</item>
/// </list>
/// </summary>
public sealed class VariaveisElemento : IVariaveisFormula
{
    public const string PrefixoArmadura = "arm.";
    public const string PrefixoPerfil = "perfil.";
    private static readonly string[] CamposArmadura = { "bitola", "quantidade", "espacamento" };

    public ElementoEstrutural Elemento { get; }
    public PerfilDetalhamento Perfil { get; }
    /// <summary>Variáveis numéricas calculadas (normalmente o próprio <see cref="ContextoMotor.Variaveis"/>, ao vivo).</summary>
    public Dictionary<string, double> Calculadas { get; }
    /// <summary>Variáveis de texto da receita.</summary>
    public Dictionary<string, string> Textos { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly Dictionary<string, ValorFormula> _base = new Dictionary<string, ValorFormula>(StringComparer.Ordinal);

    public VariaveisElemento(ElementoEstrutural elemento, PerfilDetalhamento perfil = null, Dictionary<string, double> calculadas = null)
    {
        Elemento = elemento ?? throw new ArgumentNullException(nameof(elemento));
        Perfil = perfil;
        Calculadas = calculadas ?? new Dictionary<string, double>(StringComparer.Ordinal);
        Montar();
    }

    /// <summary>Variáveis do contexto de execução (calculadas = ctx.Variaveis, ao vivo).</summary>
    public static VariaveisElemento De(ContextoMotor ctx) => new VariaveisElemento(ctx.Elemento, ctx.Perfil, ctx.Variaveis);

    /// <summary>Define uma variável da receita (número → Calculadas; texto → Textos).</summary>
    public void Definir(string nome, ValorFormula v)
    {
        if (v.EhTexto) { Calculadas.Remove(nome); Textos[nome] = v.Texto; }
        else { Textos.Remove(nome); Calculadas[nome] = v.Numero; }
    }

    public bool Tentar(string nome, out ValorFormula valor)
    {
        valor = default;
        if (string.IsNullOrEmpty(nome)) return false;
        if (Textos.TryGetValue(nome, out var t)) { valor = ValorFormula.De(t); return true; }
        if (Calculadas.TryGetValue(nome, out var d)) { valor = ValorFormula.De(d); return true; }
        if (_base.TryGetValue(nome, out valor)) return true;
        if (nome.StartsWith(PrefixoPerfil, StringComparison.Ordinal))
        {
            if (Perfil != null && Perfil.Padroes.TryGetValue(nome.Substring(PrefixoPerfil.Length), out var p)) { valor = ValorFormula.De(p); return true; }
            return false;
        }
        if (nome.StartsWith(PrefixoArmadura, StringComparison.Ordinal))
        {
            int ult = nome.LastIndexOf('.');
            if (ult <= PrefixoArmadura.Length) return false;
            string funcao = nome.Substring(PrefixoArmadura.Length, ult - PrefixoArmadura.Length), campo = nome.Substring(ult + 1);
            if (Array.IndexOf(CamposArmadura, campo) < 0 || !FuncoesAceitas().Contains(funcao)) return false;
            var a = Elemento.Armadura(funcao);
            double v = a == null ? 0 : campo == "bitola" ? a.Bitola : campo == "quantidade" ? a.Quantidade : a.Espacamento;
            valor = ValorFormula.De(v);
            return true;
        }
        return false;
    }

    public string Regra(string chave) => Perfil != null && chave != null && Perfil.Regras.TryGetValue(chave, out var r) ? r : null;
    public double? Padrao(string chave) => Perfil != null && chave != null && Perfil.Padroes.TryGetValue(chave, out var v) ? v : (double?)null;

    public IEnumerable<string> Nomes
    {
        get
        {
            foreach (var k in Textos.Keys) yield return k;
            foreach (var k in Calculadas.Keys) yield return k;
            foreach (var k in _base.Keys) yield return k;
            foreach (var f in FuncoesAceitas()) foreach (var c in CamposArmadura) yield return PrefixoArmadura + f + "." + c;
            if (Perfil != null) foreach (var k in Perfil.Padroes.Keys) yield return PrefixoPerfil + k;
        }
    }

    private HashSet<string> FuncoesAceitas()
    {
        var s = new HashSet<string>(FuncoesConhecidas, StringComparer.Ordinal);
        foreach (var a in Elemento.Armaduras) if (!string.IsNullOrEmpty(a?.Funcao)) s.Add(a.Funcao);
        return s;
    }

    /// <summary>Funções do vocabulário (<see cref="Funcoes"/>).</summary>
    public static IReadOnlyList<string> FuncoesConhecidas { get; } = typeof(Funcoes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()).OrderBy(s => s, StringComparer.Ordinal).ToList();

    // ------------------------------------------------------------------ elemento por reflexão

    private static readonly ConcurrentDictionary<Type, List<(string Nome, PropertyInfo P)>> _props =
        new ConcurrentDictionary<Type, List<(string, PropertyInfo)>>();

    private static List<(string Nome, PropertyInfo P)> Propriedades(Type t) => _props.GetOrAdd(t, tt =>
        tt.GetProperties(BindingFlags.Public | BindingFlags.Instance)
          .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && Suportado(p.PropertyType))
          .Select(p => (SnakeCase(p.Name), p)).ToList());

    private static bool Suportado(Type t) =>
        t == typeof(double) || t == typeof(float) || t == typeof(int) || t == typeof(long) || t == typeof(decimal) ||
        t == typeof(bool) || t == typeof(string) || t.IsEnum;

    private void Montar()
    {
        foreach (var (nome, p) in Propriedades(Elemento.GetType()))
        {
            object o;
            try { o = p.GetValue(Elemento); } catch { continue; }
            ValorFormula v;
            switch (o)
            {
                case null: v = ValorFormula.De(""); break;
                case string s: v = ValorFormula.De(s); break;
                case bool b: v = ValorFormula.De(b); break;
                case Enum e: v = ValorFormula.De(e.ToString()); break;
                default: v = ValorFormula.De(Convert.ToDouble(o, System.Globalization.CultureInfo.InvariantCulture)); break;
            }
            _base[nome] = v;
        }
        int np = Elemento.Pilares?.Count ?? 0;
        _base["n_pilares"] = ValorFormula.De(np);
        _base["repeticoes"] = ValorFormula.De(Elemento.Repeticoes > 0 ? Elemento.Repeticoes : Math.Max(1, np));
    }

    /// <summary>AlturaBase → altura_base; DiamEstaca → diam_estaca; EspX → esp_x; XMLTexto → xml_texto.</summary>
    public static string SnakeCase(string nome)
    {
        if (string.IsNullOrEmpty(nome)) return nome;
        var sb = new StringBuilder(nome.Length + 4);
        for (int i = 0; i < nome.Length; i++)
        {
            char c = nome[i];
            if (char.IsUpper(c))
            {
                bool antMin = i > 0 && (char.IsLower(nome[i - 1]) || char.IsDigit(nome[i - 1]));
                bool sigla = i > 0 && char.IsUpper(nome[i - 1]) && i + 1 < nome.Length && char.IsLower(nome[i + 1]);
                if (antMin || sigla) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
