using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Calculo;
using DEB.Core.Configuracao;
using DEB.Core.Modelo;

namespace DEB.Core.Motor.Componentes;

// =====================================================================================================================
// COMPONENTES E RECEITAS (pedido do usuário, 2026-11-07): um elemento = composição de componentes reutilizáveis
// (malha numa face, faixa sobre apoios, contorno lateral, cálice, arranque…), descrita numa RECEITA em dados (JSON) com
// fórmulas. O código só sabe executar componentes e avaliar fórmulas. Mudar estes tipos = combinar antes.
// =====================================================================================================================

/// <summary>Argumentos de um componente, já avaliados pela receita (números) ou literais (textos).</summary>
public sealed class ArgumentosComponente
{
    public Dictionary<string, double> Numeros { get; } = new Dictionary<string, double>(StringComparer.Ordinal);
    public Dictionary<string, string> Textos { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public bool Tem(string nome) => Numeros.ContainsKey(nome) || Textos.ContainsKey(nome);
    public double Num(string nome, double seFaltar) => Numeros.TryGetValue(nome, out var v) ? v : seFaltar;
    public int Int(string nome, int seFaltar) => Numeros.TryGetValue(nome, out var v) ? (int)Math.Round(v) : seFaltar;
    public bool Bool(string nome, bool seFaltar) => Numeros.TryGetValue(nome, out var v) ? Math.Abs(v) > 1e-12 : seFaltar;
    public string Txt(string nome, string seFaltar) => Textos.TryGetValue(nome, out var v) ? v : seFaltar;

    public ArgumentosComponente Com(string nome, double v) { Numeros[nome] = v; return this; }
    public ArgumentosComponente Com(string nome, string v) { Textos[nome] = v; return this; }
}

/// <summary>
/// Estado de uma execução: elemento, perfil, modelo sendo montado, sobrescritas, variáveis da receita e dados
/// compartilhados entre componentes (ex.: o componente de geometria publica "contorno" e "estacas"; a malha superior lê
/// "abertura" do cálice). Traz os utilitários da intenção (parâmetro com regra + sobrescrita).
/// </summary>
public sealed class ContextoMotor
{
    public ElementoEstrutural Elemento { get; }
    public PerfilDetalhamento Perfil { get; }
    public ConfiguracaoArmadura Cfg { get; }
    public ModeloArmadura Modelo { get; }
    /// <summary>Variáveis numéricas (da receita e as publicadas pelos componentes), por nome.</summary>
    public Dictionary<string, double> Variaveis { get; } = new Dictionary<string, double>(StringComparer.Ordinal);
    /// <summary>Dados não numéricos compartilhados (polígonos, listas de pontos, famílias…), por nome.</summary>
    public Dictionary<string, object> Dados { get; } = new Dictionary<string, object>(StringComparer.Ordinal);

    private readonly Dictionary<(string, string), double> _sobr = new Dictionary<(string, string), double>();
    private readonly HashSet<(string, string)> _usadas = new HashSet<(string, string)>();

    public ContextoMotor(ElementoEstrutural elemento, PerfilDetalhamento perfil, IReadOnlyList<Sobrescrita> sobrescritas,
        ConfiguracaoArmadura cfg = null)
    {
        Elemento = elemento ?? throw new ArgumentNullException(nameof(elemento));
        Perfil = perfil ?? new PerfilDetalhamento { TipoElemento = elemento.Tipo };
        Cfg = cfg ?? new ConfiguracaoArmadura();
        Modelo = new ModeloArmadura { TipoElemento = elemento.Tipo, Nome = elemento.Nome ?? "" };
        foreach (var s in sobrescritas ?? Array.Empty<Sobrescrita>())
            if (s != null) _sobr[(s.Posicao ?? "", s.Parametro ?? "")] = s.Valor;
    }

    public string Aco => string.IsNullOrWhiteSpace(Elemento.Aco) ? "CA-50" : Elemento.Aco;

    public T Dado<T>(string nome) where T : class => Dados.TryGetValue(nome, out var o) ? o as T : null;
    public double Var(string nome, double seFaltar) => Variaveis.TryGetValue(nome, out var v) ? v : seFaltar;

    /// <summary>Armadura calculada da função (null = sem: função ausente ou bitola 0).</summary>
    public ArmaduraCalculada Armadura(string funcao)
    {
        var a = Elemento.Armadura(funcao);
        return a != null && a.Bitola > 0 ? a : null;
    }

    public PosicaoArmadura NovaPosicao(string chave, string funcao, string nome, FormaBarra forma) =>
        new PosicaoArmadura { Chave = chave, Funcao = funcao, Nome = nome, Forma = forma, Aco = Aco };

    /// <summary>Parâmetro da intenção: a sobrescrita (se houver) manda; grava valor, regra e a marca de manual.</summary>
    public double Par(PosicaoArmadura p, string nome, double calculado, string regra)
    {
        double v = calculado;
        if (_sobr.TryGetValue((p.Chave, nome), out double s))
        {
            v = s;
            _usadas.Add((p.Chave, nome));
            p.Manuais.Add(nome);
            if (s < 0) Aviso($"{p.Chave}: {nome} manual = {CalculoComprimento.NumeroCompacto(s)} < 0 — conferir.");
        }
        p.Parametros[nome] = v;
        if (!string.IsNullOrEmpty(regra)) p.Regras[nome] = regra;
        return v;
    }

    public int ParInt(PosicaoArmadura p, string nome, int calculado, string regra) => (int)Math.Round(Par(p, nome, calculado, regra));

    /// <summary>Sobrescrita de um parâmetro (sem consumir), para quem precisa saber antes de calcular.</summary>
    public double? Sobrescrita(string posicao, string parametro) =>
        _sobr.TryGetValue((posicao, parametro), out var v) ? v : (double?)null;

    public void Adicionar(PosicaoArmadura p)
    {
        if (p.Barras.Count > 0 && p.Vertices.Count == 0) p.Vertices = p.Barras[0];
        Modelo.Posicoes.Add(p);
    }

    public void Aviso(string texto) { if (!Modelo.Avisos.Contains(texto)) Modelo.Avisos.Add(texto); }

    /// <summary>
    /// Distribuição do motor (decisão do usuário, 2026-11-08): a 1ª e a última barra no cobrimento (as pontas do vão) e o
    /// vão dividido por igual no menor número de espaços que não passa do espaçamento proposto.
    /// </summary>
    public int Quantidade(double vao, double esp)
    {
        if (vao <= 1e-9) return vao >= 0 ? 1 : 0;
        if (esp <= 0) return 1;
        return (int)Math.Ceiling(vao / esp - 1e-6) + 1;
    }

    /// <summary>Posições (0 … vão) das n barras divididas por igual (n = 1: no meio).</summary>
    public List<double> Posicoes(double vao, int n, double esp)
    {
        var r = new List<double>();
        if (n <= 0) return r;
        if (n == 1) { r.Add(Math.Max(0, vao) / 2); return r; }
        for (int i = 0; i < n; i++) r.Add(Math.Max(0, vao) * i / (n - 1));
        return r;
    }

    /// <summary>Espaçamento do texto (c/…): o real entre as barras, arredondado a 0,5 cm (decisão do usuário, 2026-11-08).</summary>
    public static double Rotulo(double espReal) => espReal <= 0 ? 0 : Math.Round(espReal * 2, MidpointRounding.AwayFromZero) / 2;

    /// <summary>Espaçamento do texto a partir das posições (iguais): (última − primeira) / (n − 1), a 0,5 cm.</summary>
    public static double EspacamentoReal(IEnumerable<double> posicoes, double proposto)
    {
        var l = posicoes.ToList();
        return l.Count > 1 ? Rotulo(Math.Abs(l[l.Count - 1] - l[0]) / (l.Count - 1)) : proposto;
    }

    /// <summary>Fecha a execução: avisa as sobrescritas que não foram usadas. Chamado pelo executor no fim.</summary>
    public ModeloArmadura Concluir()
    {
        foreach (var k in _sobr.Keys.Where(k => !_usadas.Contains(k)))
            Aviso(Modelo.Posicao(k.Item1) != null
                ? $"Sobrescrita ignorada: a posição \"{k.Item1}\" não tem o parâmetro \"{k.Item2}\" (ou ele não se aplica)."
                : $"Sobrescrita ignorada: a posição \"{k.Item1}\" não existe neste {Elemento.Tipo.Replace('_', ' ')}.");
        return Modelo;
    }
}

/// <summary>Peça reutilizável do motor: gera corpos, níveis, posições ou verificações a partir dos argumentos.</summary>
public interface IComponente
{
    /// <summary>Nome usado nas receitas (ex.: "malha_face", "faixa_sobre_apoios", "calice").</summary>
    string Tipo { get; }
    /// <summary>Documentação dos argumentos aceitos (nome → descrição com unidade e padrão).</summary>
    IReadOnlyDictionary<string, string> Argumentos { get; }
    void Aplicar(ContextoMotor ctx, ArgumentosComponente args);
}

/// <summary>Registro dos componentes disponíveis para as receitas.</summary>
public sealed class CatalogoComponentes
{
    private readonly Dictionary<string, IComponente> _c = new Dictionary<string, IComponente>(StringComparer.Ordinal);
    public CatalogoComponentes Registrar(IComponente c) { _c[c.Tipo] = c; return this; }
    public IComponente Obter(string tipo) => tipo != null && _c.TryGetValue(tipo, out var c) ? c : null;
    public IReadOnlyList<string> Tipos => _c.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
}

/// <summary>Formatação de números para avisos/regras (invariante, compacto).</summary>
public static class FormatoMotor
{
    public static string Num(double x) => CalculoComprimento.NumeroCompacto(x);
    public static string NumV(double x) => CalculoComprimento.NumeroVirgula(x);
}
