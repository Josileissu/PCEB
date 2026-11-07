using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DEB.Core.Motor.Componentes;

namespace DEB.Core.Motor.Receitas;

// =====================================================================================================================
// FÓRMULAS DAS RECEITAS (frente R, 2026-11-07). Avaliador próprio (sem NuGet): texto → árvore (compilada uma vez) →
// avaliada várias vezes contra um conjunto de variáveis.
//
// GRAMÁTICA (da menor para a maior precedência):
//   ou      := e ( "||" e )*
//   e       := igual ( "&&" igual )*
//   igual   := rel ( ("==" | "!=") rel )*
//   rel     := soma ( ("<" | "<=" | ">" | ">=") soma )*
//   soma    := prod ( ("+" | "-") prod )*                 ("−" U+2212 também vale como menos)
//   prod    := unario ( ("*" | "/") unario )*
//   unario  := ("-" | "!") unario | pot
//   pot     := primario ( "^" unario )?                   (à direita: 2^3^2 = 2^9; -2^2 = -4)
//   primario:= número | 'texto' | função "(" args ")" | variável | "(" ou ")"
// Números com PONTO decimal (1.5; expoente 1e-3 aceito). Textos entre aspas simples ('CaliceColarinho'; '' = aspa).
// Variáveis: letra ou _ seguida de letras, dígitos, _ e pontos (altura_base, perfil.cobrimento.solo, arm.principal.bitola).
// Booleanos = 1/0 (qualquer número ≠ 0 é verdadeiro). Comparações aceitam número×número ou texto×texto.
// Funções: min, max (1 ou mais), abs, floor, ceil, sqrt, round(x[, casas]), se(cond, a, b) (preguiçosa),
// regra('chave'[, 'se faltar']) (texto da regra do perfil; "" se faltar), padrao('chave', se faltar) (número do perfil).
// && e || também são preguiçosos. Erros (sintaxe ou avaliação) → ErroFormula com a posição (coluna, desde 1).
// =====================================================================================================================

/// <summary>Valor de uma fórmula: número (booleano = 1/0) ou texto.</summary>
public readonly struct ValorFormula
{
    public bool EhTexto { get; }
    public double Numero { get; }
    public string Texto { get; }
    private ValorFormula(bool ehTexto, double n, string t) { EhTexto = ehTexto; Numero = n; Texto = t; }
    public static ValorFormula De(double n) => new ValorFormula(false, n, null);
    public static ValorFormula De(string t) => new ValorFormula(true, 0, t ?? "");
    public static ValorFormula De(bool b) => new ValorFormula(false, b ? 1 : 0, null);
    public bool Verdadeiro => !EhTexto && Math.Abs(Numero) > 1e-12;
    /// <summary>Texto para mensagens: número no formato pt-BR (vírgula), texto entre aspas simples.</summary>
    public string Descrever() => EhTexto ? "'" + Texto + "'" : FormatoMotor.NumV(Numero);
    public override string ToString() => EhTexto ? Texto : Numero.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>De onde as fórmulas tiram variáveis e regras.</summary>
public interface IVariaveisFormula
{
    bool Tentar(string nome, out ValorFormula valor);
    /// <summary>Texto da regra do perfil (null = não há).</summary>
    string Regra(string chave);
    /// <summary>Número do perfil (Padroes) ou null.</summary>
    double? Padrao(string chave);
    /// <summary>Nomes conhecidos (para sugerir o mais parecido quando uma variável não existe).</summary>
    IEnumerable<string> Nomes { get; }
}

/// <summary>Variáveis num dicionário (testes, usos simples).</summary>
public sealed class VariaveisSimples : IVariaveisFormula
{
    public Dictionary<string, ValorFormula> Valores { get; } = new Dictionary<string, ValorFormula>(StringComparer.Ordinal);
    public Dictionary<string, string> Regras { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public Dictionary<string, double> Padroes { get; } = new Dictionary<string, double>(StringComparer.Ordinal);
    public VariaveisSimples Com(string nome, double v) { Valores[nome] = ValorFormula.De(v); return this; }
    public VariaveisSimples Com(string nome, string v) { Valores[nome] = ValorFormula.De(v); return this; }
    public bool Tentar(string nome, out ValorFormula valor) => Valores.TryGetValue(nome, out valor);
    public string Regra(string chave) => Regras.TryGetValue(chave, out var r) ? r : null;
    public double? Padrao(string chave) => Padroes.TryGetValue(chave, out var v) ? v : (double?)null;
    public IEnumerable<string> Nomes => Valores.Keys;
}

/// <summary>Erro de fórmula com a posição (coluna, desde 1) no texto.</summary>
public sealed class ErroFormula : FormatException
{
    public string Formula { get; }
    public int Posicao { get; }
    /// <summary>Mensagem sem a posição.</summary>
    public string Motivo { get; }
    public ErroFormula(string formula, int posicao, string motivo)
        : base(FormattableString.Invariant($"{motivo} (posição {posicao} em \"{formula}\")"))
    { Formula = formula; Posicao = posicao; Motivo = motivo; }
}

/// <summary>Uma variável citada pela fórmula (nome e posição).</summary>
public sealed class ReferenciaVariavel
{
    public string Nome { get; }
    public int Posicao { get; }
    public ReferenciaVariavel(string nome, int posicao) { Nome = nome; Posicao = posicao; }
}

/// <summary>Fórmula compilada (imutável; pode ser avaliada várias vezes, inclusive em paralelo).</summary>
public sealed class Formula
{
    public string Texto { get; }
    private readonly No _raiz;
    /// <summary>Variáveis citadas (na ordem em que aparecem; inclui as dos ramos de se() não avaliados).</summary>
    public IReadOnlyList<ReferenciaVariavel> Variaveis { get; }
    /// <summary>Chaves literais usadas em regra('…') e padrao('…').</summary>
    public IReadOnlyList<string> RegrasUsadas { get; }
    public IReadOnlyList<string> PadroesUsados { get; }

    private Formula(string texto, No raiz, List<ReferenciaVariavel> vars, List<string> regras, List<string> padroes)
    { Texto = texto; _raiz = raiz; Variaveis = vars; RegrasUsadas = regras; PadroesUsados = padroes; }

    /// <summary>Funções aceitas (nome → (mínimo, máximo) de argumentos; máximo −1 = sem limite).</summary>
    public static readonly IReadOnlyDictionary<string, (int Min, int Max)> Funcoes = new Dictionary<string, (int, int)>(StringComparer.Ordinal)
    {
        ["min"] = (1, -1), ["max"] = (1, -1), ["abs"] = (1, 1), ["floor"] = (1, 1), ["ceil"] = (1, 1), ["sqrt"] = (1, 1),
        ["round"] = (1, 2), ["se"] = (3, 3), ["regra"] = (1, 2), ["padrao"] = (2, 2), ["var"] = (2, 2), ["tem"] = (1, 1),
    };

    /// <summary>Compila; lança <see cref="ErroFormula"/> se a sintaxe estiver errada.</summary>
    public static Formula Compilar(string texto)
    {
        texto ??= "";
        var p = new Analisador(texto);
        var raiz = p.Analisar();
        return new Formula(texto, raiz, p.Vars, p.Regras, p.Padroes);
    }

    public static bool TentarCompilar(string texto, out Formula formula, out string erro)
    {
        try { formula = Compilar(texto); erro = null; return true; }
        catch (ErroFormula e) { formula = null; erro = e.Message; return false; }
    }

    /// <summary>Nome aceito como variável (o mesmo do analisador).</summary>
    public static bool NomeValido(string nome)
    {
        if (string.IsNullOrEmpty(nome) || !InicioNome(nome[0]) || nome[nome.Length - 1] == '.') return false;
        for (int i = 1; i < nome.Length; i++)
        {
            if (!MeioNome(nome[i])) return false;
            if (nome[i] == '.' && nome[i - 1] == '.') return false;
        }
        return !Funcoes.ContainsKey(nome);
    }

    internal static bool InicioNome(char c) => char.IsLetter(c) || c == '_';
    internal static bool MeioNome(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '.';

    public ValorFormula Avaliar(IVariaveisFormula vars)
    {
        var v = _raiz.Av(new Exec(Texto, vars ?? new VariaveisSimples()));
        if (!v.EhTexto && (double.IsNaN(v.Numero) || double.IsInfinity(v.Numero)))
            throw new ErroFormula(Texto, 1, "o resultado não é um número finito");
        return v;
    }

    /// <summary>Avalia exigindo número.</summary>
    public double AvaliarNumero(IVariaveisFormula vars)
    {
        var v = Avaliar(vars);
        if (v.EhTexto) throw new ErroFormula(Texto, 1, $"a fórmula deu o texto {v.Descrever()}, mas aqui é preciso um número");
        return v.Numero;
    }

    /// <summary>Avalia como condição (número ≠ 0 = verdadeiro; texto é erro).</summary>
    public bool AvaliarCondicao(IVariaveisFormula vars)
    {
        var v = Avaliar(vars);
        if (v.EhTexto) throw new ErroFormula(Texto, 1, $"a condição deu o texto {v.Descrever()}, mas aqui é preciso verdadeiro/falso (compare com ==)");
        return v.Verdadeiro;
    }

    /// <summary>Atalho: compila e avalia.</summary>
    public static ValorFormula Calcular(string texto, IVariaveisFormula vars = null) => Compilar(texto).Avaliar(vars);

    public override string ToString() => Texto;

    // =================================================================================================================
    // Execução
    // =================================================================================================================

    private sealed class Exec
    {
        public readonly string Texto;
        public readonly IVariaveisFormula Vars;
        public Exec(string t, IVariaveisFormula v) { Texto = t; Vars = v; }
        public ErroFormula Erro(int pos, string msg) => new ErroFormula(Texto, pos, msg);
        public double Num(ValorFormula v, int pos, string onde)
        {
            if (v.EhTexto) throw Erro(pos, $"{onde} precisa de número, mas recebeu o texto {v.Descrever()}");
            return v.Numero;
        }
    }

    private abstract class No
    {
        public int Pos;
        public abstract ValorFormula Av(Exec x);
    }

    private sealed class NoConst : No
    {
        public ValorFormula V;
        public override ValorFormula Av(Exec x) => V;
    }

    private sealed class NoVar : No
    {
        public string Nome;
        public override ValorFormula Av(Exec x)
        {
            if (x.Vars.Tentar(Nome, out var v)) return v;
            var sug = Semelhanca.MaisParecido(Nome, x.Vars.Nomes);
            throw x.Erro(Pos, $"variável desconhecida \"{Nome}\"" + (sug != null ? $" — você quis dizer \"{sug}\"?" : ""));
        }
    }

    private sealed class NoUnario : No
    {
        public string Op; public No A;
        public override ValorFormula Av(Exec x)
        {
            var a = A.Av(x);
            if (Op == "-") return ValorFormula.De(-x.Num(a, Pos, "o menos"));
            return ValorFormula.De(!Bool(x, a, Pos, "o '!'"));
        }
    }

    private static bool Bool(Exec x, ValorFormula v, int pos, string onde)
    {
        if (v.EhTexto) throw x.Erro(pos, $"{onde} precisa de verdadeiro/falso (número), mas recebeu o texto {v.Descrever()}");
        return v.Verdadeiro;
    }

    private sealed class NoBinario : No
    {
        public string Op; public No A, B;
        public override ValorFormula Av(Exec x)
        {
            switch (Op)
            {
                case "&&": return ValorFormula.De(Bool(x, A.Av(x), Pos, "o '&&'") && Bool(x, B.Av(x), Pos, "o '&&'"));
                case "||": return ValorFormula.De(Bool(x, A.Av(x), Pos, "o '||'") || Bool(x, B.Av(x), Pos, "o '||'"));
            }
            var a = A.Av(x); var b = B.Av(x);
            switch (Op)
            {
                case "==": case "!=":
                {
                    if (a.EhTexto != b.EhTexto)
                        throw x.Erro(Pos, $"'{Op}' compara texto com número ({a.Descrever()} e {b.Descrever()})");
                    bool ig = a.EhTexto ? string.Equals(a.Texto, b.Texto, StringComparison.Ordinal) : Math.Abs(a.Numero - b.Numero) <= 1e-9;
                    return ValorFormula.De(Op == "==" ? ig : !ig);
                }
                case "<": case "<=": case ">": case ">=":
                {
                    if (a.EhTexto != b.EhTexto)
                        throw x.Erro(Pos, $"'{Op}' compara texto com número ({a.Descrever()} e {b.Descrever()})");
                    int c = a.EhTexto ? Math.Sign(string.CompareOrdinal(a.Texto, b.Texto)) : a.Numero.CompareTo(b.Numero);
                    bool r = Op == "<" ? c < 0 : Op == "<=" ? c <= 0 : Op == ">" ? c > 0 : c >= 0;
                    return ValorFormula.De(r);
                }
            }
            string onde = $"o operador '{Op}'";
            double na = x.Num(a, Pos, onde), nb = x.Num(b, Pos, onde);
            switch (Op)
            {
                case "+": return ValorFormula.De(na + nb);
                case "-": return ValorFormula.De(na - nb);
                case "*": return ValorFormula.De(na * nb);
                case "/":
                    if (nb == 0) throw x.Erro(Pos, "divisão por zero");
                    return ValorFormula.De(na / nb);
                case "^":
                {
                    double r = Math.Pow(na, nb);
                    if (double.IsNaN(r) || double.IsInfinity(r))
                        throw x.Erro(Pos, $"potência sem resultado real ({FormatoMotor.NumV(na)} ^ {FormatoMotor.NumV(nb)})");
                    return ValorFormula.De(r);
                }
            }
            throw x.Erro(Pos, $"operador desconhecido '{Op}'");
        }
    }

    private sealed class NoFuncao : No
    {
        public string Nome; public List<No> Args;
        public override ValorFormula Av(Exec x)
        {
            switch (Nome)
            {
                case "se":
                    return Bool(x, Args[0].Av(x), Args[0].Pos, "a condição do se()") ? Args[1].Av(x) : Args[2].Av(x);
                case "regra":
                {
                    var k = Args[0].Av(x);
                    if (!k.EhTexto) throw x.Erro(Args[0].Pos, "regra() precisa da chave como texto: regra('principal.perna')");
                    string padrao = "";
                    if (Args.Count > 1)
                    {
                        var p = Args[1].Av(x);
                        padrao = p.EhTexto ? p.Texto : throw x.Erro(Args[1].Pos, "o 2º argumento de regra() é o texto usado se a regra faltar");
                    }
                    return ValorFormula.De(x.Vars.Regra(k.Texto) ?? padrao);
                }
                case "var":
                {
                    // var('nome', padrão): variável publicada por um componente anterior, ou o padrão se ainda não existe
                    var k = Args[0].Av(x);
                    if (!k.EhTexto) throw x.Erro(Args[0].Pos, "var() precisa do nome como texto: var('z_topo_inferior', 4)");
                    return x.Vars.Tentar(k.Texto, out var vv) ? vv : Args[1].Av(x);
                }
                case "tem":
                {
                    // tem('funcao'): 1 se o elemento tem a armadura da função (bitola > 0)
                    var k = Args[0].Av(x);
                    if (!k.EhTexto) throw x.Erro(Args[0].Pos, "tem() precisa da função como texto: tem('estribo_vertical')");
                    return ValorFormula.De(x.Vars.Tentar("arm." + k.Texto + ".bitola", out var b) && !b.EhTexto && b.Numero > 0 ? 1 : 0);
                }
                case "padrao":
                {
                    var k = Args[0].Av(x);
                    if (!k.EhTexto) throw x.Erro(Args[0].Pos, "padrao() precisa da chave como texto: padrao('cobrimento.solo', 4)");
                    double seFaltar = x.Num(Args[1].Av(x), Args[1].Pos, "o 2º argumento de padrao()");
                    return ValorFormula.De(x.Vars.Padrao(k.Texto) ?? seFaltar);
                }
            }
            var v = new double[Args.Count];
            for (int i = 0; i < Args.Count; i++) v[i] = x.Num(Args[i].Av(x), Args[i].Pos, $"{Nome}()");
            switch (Nome)
            {
                case "min": return ValorFormula.De(v.Min());
                case "max": return ValorFormula.De(v.Max());
                case "abs": return ValorFormula.De(Math.Abs(v[0]));
                case "floor": return ValorFormula.De(Math.Floor(v[0] + 1e-9));
                case "ceil": return ValorFormula.De(Math.Ceiling(v[0] - 1e-9));
                case "sqrt":
                    if (v[0] < 0) throw x.Erro(Pos, $"sqrt() de número negativo ({FormatoMotor.NumV(v[0])})");
                    return ValorFormula.De(Math.Sqrt(v[0]));
                case "round":
                {
                    int casas = v.Length > 1 ? (int)Math.Round(v[1]) : 0;
                    if (casas < -10 || casas > 10) throw x.Erro(Args[1].Pos, "round(): casas entre -10 e 10");
                    // decimal evita 2.345 → 2.34 (2.345 × 100 = 234.4999… em double)
                    if (casas >= 0 && Math.Abs(v[0]) < 1e15)
                        return ValorFormula.De((double)Math.Round((decimal)v[0], casas, MidpointRounding.AwayFromZero));
                    double f = Math.Pow(10, casas);
                    return ValorFormula.De(Math.Round(v[0] * f, MidpointRounding.AwayFromZero) / f);
                }
            }
            throw x.Erro(Pos, $"função desconhecida \"{Nome}\"");
        }
    }

    // =================================================================================================================
    // Análise
    // =================================================================================================================

    private enum TipoTok { Num, Txt, Id, Op, Fim }

    private sealed class Tok
    {
        public TipoTok Tipo; public string S; public double N; public int Pos;
        public override string ToString() => Tipo == TipoTok.Fim ? "fim da fórmula" : Tipo == TipoTok.Txt ? "'" + S + "'" : "'" + S + "'";
    }

    private sealed class Analisador
    {
        private readonly string _t;
        private readonly List<Tok> _toks = new List<Tok>();
        private int _i, _prof;
        public readonly List<ReferenciaVariavel> Vars = new List<ReferenciaVariavel>();
        public readonly List<string> Regras = new List<string>();
        public readonly List<string> Padroes = new List<string>();

        public Analisador(string t) { _t = t; }

        private ErroFormula Erro(int pos, string msg) => new ErroFormula(_t, pos, msg);
        private Tok Atual => _toks[_i];
        private bool EhOp(string op) => Atual.Tipo == TipoTok.Op && Atual.S == op;

        public No Analisar()
        {
            Lexico();
            if (Atual.Tipo == TipoTok.Fim) throw Erro(1, "fórmula vazia");
            var n = Ou();
            if (Atual.Tipo != TipoTok.Fim)
            {
                if (EhOp(",")) throw Erro(Atual.Pos, "vírgula fora de uma função (o separador decimal é o ponto: 1.5)");
                if (EhOp(")")) throw Erro(Atual.Pos, "')' sem o '(' correspondente");
                throw Erro(Atual.Pos, $"esperado um operador, encontrado {Atual}");
            }
            return n;
        }

        // ------------------------------------------------------------ léxico

        private void Lexico()
        {
            int p = 0;
            while (p < _t.Length)
            {
                char c = _t[p];
                if (char.IsWhiteSpace(c)) { p++; continue; }
                int pos = p + 1;
                if (char.IsDigit(c) || (c == '.' && p + 1 < _t.Length && char.IsDigit(_t[p + 1])))
                {
                    int ini = p;
                    while (p < _t.Length && char.IsDigit(_t[p])) p++;
                    if (p < _t.Length && _t[p] == '.')
                    {
                        p++;
                        if (p >= _t.Length || !char.IsDigit(_t[p])) throw Erro(pos, "número termina com ponto (escreva 1.0 ou 1)");
                        while (p < _t.Length && char.IsDigit(_t[p])) p++;
                    }
                    if (p < _t.Length && (_t[p] == 'e' || _t[p] == 'E'))
                    {
                        int q = p + 1;
                        if (q < _t.Length && (_t[q] == '+' || _t[q] == '-')) q++;
                        if (q < _t.Length && char.IsDigit(_t[q])) { p = q; while (p < _t.Length && char.IsDigit(_t[p])) p++; }
                    }
                    if (p < _t.Length && InicioNome(_t[p])) throw Erro(p + 1, $"número colado num nome ('{_t.Substring(ini, p - ini + 1)}…'); use * para multiplicar");
                    var s = _t.Substring(ini, p - ini);
                    if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || double.IsInfinity(d))
                        throw Erro(pos, $"número inválido '{s}'");
                    _toks.Add(new Tok { Tipo = TipoTok.Num, S = s, N = d, Pos = pos });
                    continue;
                }
                if (InicioNome(c))
                {
                    int ini = p;
                    while (p < _t.Length && MeioNome(_t[p])) p++;
                    var s = _t.Substring(ini, p - ini);
                    if (s.EndsWith(".", StringComparison.Ordinal)) throw Erro(pos + s.Length - 1, $"nome termina com ponto ('{s}')");
                    if (s.Contains("..")) throw Erro(pos, $"nome com dois pontos seguidos ('{s}')");
                    _toks.Add(new Tok { Tipo = TipoTok.Id, S = s, Pos = pos });
                    continue;
                }
                if (c == '\'')
                {
                    var sb = new StringBuilder();
                    p++;
                    while (true)
                    {
                        if (p >= _t.Length) throw Erro(pos, "texto sem a aspa simples de fechamento");
                        if (_t[p] == '\'')
                        {
                            if (p + 1 < _t.Length && _t[p + 1] == '\'') { sb.Append('\''); p += 2; continue; }
                            p++;
                            break;
                        }
                        sb.Append(_t[p++]);
                    }
                    _toks.Add(new Tok { Tipo = TipoTok.Txt, S = sb.ToString(), Pos = pos });
                    continue;
                }
                if (c == '"' || c == '“' || c == '”') throw Erro(pos, "textos vão entre aspas simples: 'CaliceColarinho'");
                string op = null;
                string dois = p + 1 < _t.Length ? _t.Substring(p, 2) : "";
                switch (dois)
                {
                    case "==": case "!=": case "<=": case ">=": case "&&": case "||": op = dois; break;
                }
                if (op != null) { p += 2; _toks.Add(new Tok { Tipo = TipoTok.Op, S = op, Pos = pos }); continue; }
                switch (c)
                {
                    case '+': case '-': case '*': case '/': case '^': case '(': case ')': case ',': case '<': case '>': case '!':
                        op = c.ToString(); break;
                    case '−': op = "-"; break;   // − (sinal de menos tipográfico)
                    case '=': throw Erro(pos, "use == para comparar (= sozinho não existe)");
                    case '&': throw Erro(pos, "use && para \"e\"");
                    case '|': throw Erro(pos, "use || para \"ou\"");
                    default: throw Erro(pos, $"caractere inesperado '{c}'");
                }
                p++;
                _toks.Add(new Tok { Tipo = TipoTok.Op, S = op, Pos = pos });
            }
            _toks.Add(new Tok { Tipo = TipoTok.Fim, S = "", Pos = _t.Length + 1 });
        }

        // ------------------------------------------------------------ sintático

        private No Binarios(Func<No> prox, params string[] ops)
        {
            var a = prox();
            while (Atual.Tipo == TipoTok.Op && Array.IndexOf(ops, Atual.S) >= 0)
            {
                var t = Atual; _i++;
                var b = prox();
                a = new NoBinario { Op = t.S, A = a, B = b, Pos = t.Pos };
            }
            return a;
        }

        private No Ou() => Binarios(E, "||");
        private No E() => Binarios(Igual, "&&");
        private No Igual() => Binarios(Rel, "==", "!=");
        private No Rel() => Binarios(Soma, "<", "<=", ">", ">=");
        private No Soma() => Binarios(Prod, "+", "-");
        private No Prod() => Binarios(Unario, "*", "/");

        private No Unario()
        {
            if (EhOp("-") || EhOp("!"))
            {
                var t = Atual; _i++;
                if (++_prof > 200) throw Erro(t.Pos, "fórmula aninhada demais");
                var a = Unario();
                _prof--;
                if (t.S == "-" && a is NoConst k && !k.V.EhTexto) return new NoConst { V = ValorFormula.De(-k.V.Numero), Pos = t.Pos };
                return new NoUnario { Op = t.S, A = a, Pos = t.Pos };
            }
            return Pot();
        }

        private No Pot()
        {
            var a = Primario();
            if (EhOp("^"))
            {
                var t = Atual; _i++;
                var b = Unario();
                return new NoBinario { Op = "^", A = a, B = b, Pos = t.Pos };
            }
            return a;
        }

        private No Primario()
        {
            var t = Atual;
            switch (t.Tipo)
            {
                case TipoTok.Num: _i++; return new NoConst { V = ValorFormula.De(t.N), Pos = t.Pos };
                case TipoTok.Txt: _i++; return new NoConst { V = ValorFormula.De(t.S), Pos = t.Pos };
                case TipoTok.Id:
                    _i++;
                    if (EhOp("(")) return Funcao(t);
                    if (Funcoes.ContainsKey(t.S)) throw Erro(t.Pos, $"\"{t.S}\" é uma função: falta o '(' (ex.: {t.S}(…))");
                    Vars.Add(new ReferenciaVariavel(t.S, t.Pos));
                    return new NoVar { Nome = t.S, Pos = t.Pos };
                case TipoTok.Op when t.S == "(":
                {
                    _i++;
                    if (++_prof > 200) throw Erro(t.Pos, "fórmula aninhada demais");
                    var n = Ou();
                    _prof--;
                    if (!EhOp(")")) throw Erro(Atual.Pos, $"falta ')' para fechar o '(' da posição {t.Pos} (encontrado {Atual})");
                    _i++;
                    return n;
                }
                case TipoTok.Fim: throw Erro(t.Pos, "a fórmula termina no meio (falta um valor)");
                default:
                    throw Erro(t.Pos, $"esperado um valor (número, 'texto', variável ou função), encontrado {t}");
            }
        }

        private No Funcao(Tok nome)
        {
            if (!Funcoes.TryGetValue(nome.S, out var ar))
            {
                var sug = Semelhanca.MaisParecido(nome.S, Funcoes.Keys);
                throw Erro(nome.Pos, $"função desconhecida \"{nome.S}\"" + (sug != null ? $" — você quis dizer \"{sug}\"?" : "") +
                                     $" (funções: {string.Join(", ", Funcoes.Keys)})");
            }
            var abre = Atual; _i++;   // (
            if (++_prof > 200) throw Erro(abre.Pos, "fórmula aninhada demais");
            var args = new List<No>();
            if (!EhOp(")"))
            {
                while (true)
                {
                    args.Add(Ou());
                    if (EhOp(",")) { _i++; continue; }
                    if (EhOp(")")) break;
                    throw Erro(Atual.Pos, $"esperado ',' ou ')' nos argumentos de {nome.S}(), encontrado {Atual}");
                }
            }
            _i++;   // )
            _prof--;
            if (args.Count < ar.Min || (ar.Max >= 0 && args.Count > ar.Max))
            {
                string quantos = ar.Max < 0 ? $"{ar.Min} ou mais" : ar.Min == ar.Max ? $"{ar.Min}" : $"{ar.Min} a {ar.Max}";
                throw Erro(nome.Pos, $"{nome.S}() recebe {quantos} argumento(s), não {args.Count}");
            }
            if ((nome.S == "regra" || nome.S == "padrao") && args[0] is NoConst k && k.V.EhTexto)
                (nome.S == "regra" ? Regras : Padroes).Add(k.V.Texto);
            return new NoFuncao { Nome = nome.S, Args = args, Pos = nome.Pos };
        }
    }
}

/// <summary>Sugestão do nome mais parecido (distância de edição), para as mensagens de erro.</summary>
public static class Semelhanca
{
    /// <summary>Candidato mais parecido, ou null se nenhum for razoavelmente parecido.</summary>
    public static string MaisParecido(string nome, IEnumerable<string> candidatos)
    {
        if (string.IsNullOrEmpty(nome) || candidatos == null) return null;
        string melhor = null; int dm = int.MaxValue;
        int limite = Math.Max(2, nome.Length / 3);
        foreach (var c in candidatos.Distinct())
        {
            if (string.IsNullOrEmpty(c) || c == nome) continue;
            int d = Distancia(nome.ToLowerInvariant(), c.ToLowerInvariant());
            if (d < dm || (d == dm && melhor != null && string.CompareOrdinal(c, melhor) < 0)) { dm = d; melhor = c; }
        }
        return dm <= limite ? melhor : null;
    }

    /// <summary>Distância de Levenshtein (com troca de vizinhos contando 1).</summary>
    public static int Distancia(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                int custo = a[i - 1] == b[j - 1] ? 0 : 1;
                int v = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + custo);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) v = Math.Min(v, d[i - 2, j - 2] + 1);
                d[i, j] = v;
            }
        return d[a.Length, b.Length];
    }
}
