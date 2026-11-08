using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DEB.Core.Configuracao;
using DEB.Core.Motor.Componentes;

namespace DEB.Core.Motor.Receitas;

/// <summary>
/// Opcional para componentes: declara as variáveis que o componente publica em <see cref="ContextoMotor.Variaveis"/>,
/// para o <see cref="ValidadorReceita"/> saber que elas existirão nos passos seguintes. (Não faz parte do contrato de
/// <see cref="IComponente"/>; quem não implementar continua funcionando, só que o validador avisa em vez de dar erro.)
/// </summary>
public interface IComponenteComSaidas : IComponente
{
    IReadOnlyCollection<string> VariaveisPublicadas { get; }
    /// <summary>Variáveis publicadas com estes argumentos (só os textos literais vêm preenchidos), quando o nome depende deles.</summary>
    IReadOnlyCollection<string> VariaveisPublicadasCom(ArgumentosComponente args);
}

/// <summary>
/// Motor genérico: executa uma <see cref="Receita"/> com os componentes do <see cref="CatalogoComponentes"/>.
/// Nunca lança para o chamador por causa da receita ou de um componente: tudo vira aviso no modelo (o CAD não pode cair).
/// </summary>
public sealed class MotorReceita<T> : IMotorDetalhamento<T> where T : ElementoEstrutural
{
    public Receita Receita { get; }
    public CatalogoComponentes Catalogo { get; }
    private readonly ConfiguracaoArmadura _cfg;
    private readonly ExecutorReceita _exec;

    public MotorReceita(Receita receita, CatalogoComponentes catalogo, ConfiguracaoArmadura cfg = null)
    {
        Receita = receita ?? throw new ArgumentNullException(nameof(receita));
        Catalogo = catalogo ?? new CatalogoComponentes();
        _cfg = cfg;
        _exec = new ExecutorReceita(Receita, Catalogo);
    }

    public string TipoElemento => Receita.TipoElemento;

    public ModeloArmadura Gerar(T elemento, PerfilDetalhamento perfil, IReadOnlyList<Sobrescrita> sobrescritas)
    {
        if (elemento == null) throw new ArgumentNullException(nameof(elemento));
        var ctx = new ContextoMotor(elemento, perfil, sobrescritas, _cfg);
        _exec.Executar(ctx);
        return ctx.Concluir();
    }
}

/// <summary>Executa uma receita num <see cref="ContextoMotor"/> (sem chamar <see cref="ContextoMotor.Concluir"/>).
/// As fórmulas são compiladas uma vez e guardadas.</summary>
public sealed class ExecutorReceita
{
    public Receita Receita { get; }
    public CatalogoComponentes Catalogo { get; }
    private readonly Dictionary<string, (Formula F, ErroFormula E)> _cache = new Dictionary<string, (Formula, ErroFormula)>(StringComparer.Ordinal);

    public ExecutorReceita(Receita receita, CatalogoComponentes catalogo)
    {
        Receita = receita ?? throw new ArgumentNullException(nameof(receita));
        Catalogo = catalogo ?? new CatalogoComponentes();
    }

    private Formula Compilada(string texto)
    {
        texto ??= "";
        (Formula F, ErroFormula E) r;
        lock (_cache)
            if (!_cache.TryGetValue(texto, out r))
            {
                try { r = (Formula.Compilar(texto), null); }
                catch (ErroFormula e) { r = (null, e); }
                _cache[texto] = r;
            }
        if (r.E != null) throw r.E;
        return r.F;
    }

    private string Prefixo => $"Receita \"{(string.IsNullOrEmpty(Receita.TipoElemento) ? "?" : Receita.TipoElemento)}\"";

    public void Executar(ContextoMotor ctx)
    {
        if (ctx == null) throw new ArgumentNullException(nameof(ctx));
        try
        {
            if (!string.IsNullOrEmpty(Receita.TipoElemento) && Receita.TipoElemento != ctx.Elemento.Tipo)
                ctx.Aviso($"{Prefixo} é para \"{Receita.TipoElemento}\", mas o elemento é \"{ctx.Elemento.Tipo}\" — conferir.");
            var vars = VariaveisElemento.De(ctx);
            Variaveis(ctx, vars);
            for (int i = 0; i < Receita.Passos.Count; i++)
                if (Receita.Passos[i] != null) Passo(ctx, vars, Receita.Passos[i], i);
            for (int i = 0; i < Receita.Verificacoes.Count; i++)
                if (Receita.Verificacoes[i] != null) Verificar(ctx, vars, Receita.Verificacoes[i], i);
        }
        catch (Exception e)
        {
            // rede de segurança: nada sobe para o CAD
            ctx.Aviso($"{Prefixo}: erro inesperado ({e.GetType().Name}: {e.Message}); o modelo pode estar incompleto.");
        }
    }

    private void Variaveis(ContextoMotor ctx, VariaveisElemento vars)
    {
        foreach (var v in Receita.Variaveis)
        {
            if (v == null) continue;
            try { vars.Definir(v.Nome, Compilada(v.Formula).Avaliar(vars)); }
            catch (ErroFormula e)
            { ctx.Aviso($"{Prefixo}, variável \"{v.Nome}\": {e.Message} — a variável ficou sem valor."); }
        }
    }

    private void Passo(ContextoMotor ctx, VariaveisElemento vars, PassoReceita p, int i)
    {
        string rot = $"{Prefixo}, {p.Rotulo(i)}";
        var comp = Catalogo.Obter(p.Componente);
        if (comp == null)
        {
            var sug = Semelhanca.MaisParecido(p.Componente, Catalogo.Tipos);
            ctx.Aviso($"{rot}: o componente \"{p.Componente}\" não existe" + (sug != null ? $" (você quis dizer \"{sug}\"?)" : "") +
                      $"; disponíveis: {(Catalogo.Tipos.Count == 0 ? "nenhum" : string.Join(", ", Catalogo.Tipos))} — passo ignorado.");
            return;
        }
        if (!string.IsNullOrWhiteSpace(p.Se))
        {
            try { if (!Compilada(p.Se).AvaliarCondicao(vars)) return; }
            catch (ErroFormula e) { ctx.Aviso($"{rot}, condição \"se\": {e.Message} — passo ignorado."); return; }
        }
        var args = new ArgumentosComponente();
        foreach (var kv in p.Argumentos)
        {
            try
            {
                var v = Compilada(kv.Value).Avaliar(vars);
                if (v.EhTexto) args.Textos[kv.Key] = v.Texto; else args.Numeros[kv.Key] = v.Numero;
            }
            catch (ErroFormula e) { ctx.Aviso($"{rot}, argumento \"{kv.Key}\": {e.Message} — passo ignorado."); return; }
        }
        try { comp.Aplicar(ctx, args); }
        catch (Exception e)
        { ctx.Aviso($"{rot} falhou ({e.GetType().Name}: {e.Message}) — passo ignorado; o resto da receita continua."); }
    }

    private void Verificar(ContextoMotor ctx, VariaveisElemento vars, VerificacaoReceita c, int i)
    {
        string rot = $"{Prefixo}, verificação {i + 1}";
        try
        {
            if (!string.IsNullOrWhiteSpace(c.Se) && !Compilada(c.Se).AvaliarCondicao(vars)) return;
            if (Compilada(c.Condicao).AvaliarCondicao(vars)) return;
            ctx.Aviso(string.IsNullOrWhiteSpace(c.Aviso) ? $"{rot}: falhou ({c.Condicao})." : FormatarAviso(c.Aviso, vars));
        }
        catch (ErroFormula e) { ctx.Aviso($"{rot} (\"{c.Condicao}\"): {e.Message}."); }
    }

    private static readonly Regex Marcador = new Regex(@"\{([\p{L}_][\p{L}\p{Nd}_.]*)\}", RegexOptions.CultureInvariant);

    /// <summary>Troca {nome} pelo valor (número pt-BR com vírgula; texto como está). Nome desconhecido fica como está.</summary>
    public static string FormatarAviso(string modelo, IVariaveisFormula vars) =>
        Marcador.Replace(modelo ?? "", m =>
            vars != null && vars.Tentar(m.Groups[1].Value, out var v) ? (v.EhTexto ? v.Texto : FormatoMotor.NumV(v.Numero)) : m.Value);

    /// <summary>Nomes citados como {nome} num texto de aviso.</summary>
    public static IEnumerable<string> MarcadoresDoAviso(string modelo) =>
        Marcador.Matches(modelo ?? "").Cast<Match>().Select(m => m.Groups[1].Value);
}
