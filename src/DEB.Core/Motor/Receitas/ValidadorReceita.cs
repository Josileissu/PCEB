using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Motor.Componentes;

namespace DEB.Core.Motor.Receitas;

/// <summary>Um problema encontrado na receita.</summary>
public sealed class ProblemaReceita
{
    /// <summary>true = erro (a receita não vai funcionar como escrita); false = atenção (pode estar certo).</summary>
    public bool Erro { get; set; }
    /// <summary>Onde: "passo 3 \"secundária x\" (malha_face), argumento \"perna\"".</summary>
    public string Local { get; set; } = "";
    public string Mensagem { get; set; } = "";
    public override string ToString() => $"{(Erro ? "ERRO" : "Atenção")} — {Local}: {Mensagem}";
}

public sealed class ResultadoValidacao
{
    public List<ProblemaReceita> Problemas { get; } = new List<ProblemaReceita>();
    public bool Valida => !Problemas.Any(p => p.Erro);
    public IEnumerable<ProblemaReceita> Erros => Problemas.Where(p => p.Erro);
    public IEnumerable<ProblemaReceita> Atencoes => Problemas.Where(p => !p.Erro);
    public override string ToString() => Problemas.Count == 0 ? "Receita sem problemas." : string.Join("\n", Problemas);
}

/// <summary>
/// Confere uma receita contra um catálogo de componentes SEM executar componentes: componentes existem, argumentos são
/// conhecidos do componente (<see cref="IComponente.Argumentos"/>), fórmulas compilam, variáveis citadas existem.
/// Com um elemento de exemplo (e o perfil), confere também as variáveis do elemento/perfil e avalia a seção
/// "variaveis" para achar erros de tipo/divisão por zero. Variáveis que só um componente publica: se o componente
/// implementa <see cref="IComponenteComSaidas"/>, elas valem nos passos seguintes; senão, nome desconhecido depois dele
/// vira "atenção" (pode ser publicado por ele) em vez de erro.
/// </summary>
public static class ValidadorReceita
{
    public static ResultadoValidacao Validar(Receita receita, CatalogoComponentes catalogo,
        ElementoEstrutural exemplo = null, PerfilDetalhamento perfil = null)
    {
        var r = new ResultadoValidacao();
        if (receita == null) { Erro(r, "receita", "receita vazia (null)."); return r; }
        catalogo ??= new CatalogoComponentes();

        if (string.IsNullOrWhiteSpace(receita.TipoElemento)) Atencao(r, "tipoElemento", "não informado.");
        else if (exemplo != null && exemplo.Tipo != receita.TipoElemento)
            Atencao(r, "tipoElemento", $"a receita é para \"{receita.TipoElemento}\", mas o elemento de exemplo é \"{exemplo.Tipo}\".");

        VariaveisElemento varsEx = exemplo != null ? new VariaveisElemento(exemplo, perfil) : null;
        var conhecidas = new HashSet<string>(StringComparer.Ordinal);   // definidas pela receita / publicadas
        bool publicacaoIncerta = false;                                  // houve componente sem IComponenteComSaidas
        bool avaliavel = varsEx != null;                                 // dá para avaliar as variáveis com o exemplo

        // ---------------------------------------------------------------- variáveis
        for (int i = 0; i < receita.Variaveis.Count; i++)
        {
            var v = receita.Variaveis[i];
            if (v == null) continue;
            string loc = $"variaveis[{i}] \"{v.Nome}\"";
            if (!Formula.NomeValido(v.Nome))
                Erro(r, loc, "nome inválido (letras, dígitos, _ e pontos; começa com letra; não pode ser nome de função).");
            else if (conhecidas.Contains(v.Nome))
                Atencao(r, loc, "variável definida de novo (vale a última definição daqui em diante).");
            else if (varsEx != null && varsEx.Tentar(v.Nome, out _))
                Atencao(r, loc, "tem o mesmo nome de um dado do elemento/perfil; a receita passa a usar este valor.");
            var f = Compilar(r, loc, v.Formula);
            if (f != null)
            {
                Referencias(r, loc, f, conhecidas, varsEx, perfil, publicacaoIncerta);
                if (avaliavel)
                {
                    try { varsEx.Definir(v.Nome, f.Avaliar(varsEx)); }
                    catch (ErroFormula e)
                    {
                        if (!e.Motivo.StartsWith("variável desconhecida", StringComparison.Ordinal))
                            Atencao(r, loc, $"com o elemento de exemplo: {e.Message}");
                        avaliavel = false;   // as seguintes podem depender desta
                    }
                }
            }
            else avaliavel = false;
            if (Formula.NomeValido(v.Nome)) conhecidas.Add(v.Nome);
        }

        // ---------------------------------------------------------------- passos
        for (int i = 0; i < receita.Passos.Count; i++)
        {
            var p = receita.Passos[i];
            if (p == null) continue;
            string loc = p.Rotulo(i);
            var comp = catalogo.Obter(p.Componente);
            if (comp == null)
            {
                var sug = Semelhanca.MaisParecido(p.Componente, catalogo.Tipos);
                Erro(r, loc, $"o componente \"{p.Componente}\" não existe" + (sug != null ? $" — você quis dizer \"{sug}\"?" : "") +
                             $" (disponíveis: {(catalogo.Tipos.Count == 0 ? "nenhum" : string.Join(", ", catalogo.Tipos))}).");
            }
            if (!string.IsNullOrWhiteSpace(p.Se))
            {
                var f = Compilar(r, loc + ", \"se\"", p.Se);
                if (f != null) Referencias(r, loc + ", \"se\"", f, conhecidas, varsEx, perfil, publicacaoIncerta);
            }
            var doc = comp?.Argumentos;
            foreach (var kv in p.Argumentos)
            {
                string la = $"{loc}, argumento \"{kv.Key}\"";
                if (doc != null && !doc.ContainsKey(kv.Key))
                {
                    var sug = Semelhanca.MaisParecido(kv.Key, doc.Keys);
                    Erro(r, la, $"o componente \"{comp.Tipo}\" não tem este argumento" + (sug != null ? $" — você quis dizer \"{sug}\"?" : "") +
                                $" (argumentos: {(doc.Count == 0 ? "nenhum" : string.Join(", ", doc.Keys.OrderBy(k => k, StringComparer.Ordinal)))}).");
                }
                var f = Compilar(r, la, kv.Value);
                if (f != null) Referencias(r, la, f, conhecidas, varsEx, perfil, publicacaoIncerta);
            }
            if (comp is IComponenteComSaidas cs) { foreach (var n in cs.VariaveisPublicadas ?? Array.Empty<string>()) conhecidas.Add(n); }
            else if (comp != null) publicacaoIncerta = true;
        }

        // ---------------------------------------------------------------- verificações
        for (int i = 0; i < receita.Verificacoes.Count; i++)
        {
            var c = receita.Verificacoes[i];
            if (c == null) continue;
            string loc = $"verificacoes[{i}]";
            if (!string.IsNullOrWhiteSpace(c.Se))
            {
                var fs = Compilar(r, loc + ", \"se\"", c.Se);
                if (fs != null) Referencias(r, loc + ", \"se\"", fs, conhecidas, varsEx, perfil, publicacaoIncerta);
            }
            var f = Compilar(r, loc + ", \"condicao\"", c.Condicao);
            if (f != null) Referencias(r, loc + ", \"condicao\"", f, conhecidas, varsEx, perfil, publicacaoIncerta);
            if (string.IsNullOrWhiteSpace(c.Aviso)) Atencao(r, loc + ", \"aviso\"", "sem texto (o aviso mostrará só a condição).");
            foreach (var n in ExecutorReceita.MarcadoresDoAviso(c.Aviso))
                Nome(r, loc + ", \"aviso\"", n, $"{{{n}}}", conhecidas, varsEx, perfil, publicacaoIncerta);
        }
        return r;
    }

    private static Formula Compilar(ResultadoValidacao r, string loc, string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) { Erro(r, loc, "fórmula vazia."); return null; }
        try { return Formula.Compilar(texto); }
        catch (ErroFormula e) { Erro(r, loc, e.Message); return null; }
    }

    private static void Referencias(ResultadoValidacao r, string loc, Formula f, HashSet<string> conhecidas,
        VariaveisElemento varsEx, PerfilDetalhamento perfil, bool incerta)
    {
        foreach (var v in f.Variaveis)
            Nome(r, loc, v.Nome, FormattableString.Invariant($"\"{v.Nome}\" (posição {v.Posicao} em \"{f.Texto}\")"), conhecidas, varsEx, perfil, incerta);
        if (perfil != null)
        {
            foreach (var k in f.RegrasUsadas.Where(k => !perfil.Regras.ContainsKey(k)))
                Atencao(r, loc, $"o perfil \"{perfil.Id}\" não tem a regra '{k}' (regra() dará o texto padrão).");
            foreach (var k in f.PadroesUsados.Where(k => !perfil.Padroes.ContainsKey(k)))
                Atencao(r, loc, $"o perfil \"{perfil.Id}\" não tem o padrão '{k}' (padrao() usará o valor de reserva).");
        }
    }

    private static void Nome(ResultadoValidacao r, string loc, string nome, string descricao, HashSet<string> conhecidas,
        VariaveisElemento varsEx, PerfilDetalhamento perfil, bool incerta)
    {
        if (conhecidas.Contains(nome)) return;
        bool ehPerfil = nome.StartsWith(VariaveisElemento.PrefixoPerfil, StringComparison.Ordinal);
        if (ehPerfil)
        {
            if (perfil == null) return;   // sem perfil não dá para saber
            if (perfil.Padroes.ContainsKey(nome.Substring(VariaveisElemento.PrefixoPerfil.Length))) return;
        }
        else
        {
            if (varsEx == null) return;   // sem elemento de exemplo não dá para saber
            if (varsEx.Tentar(nome, out _)) return;
        }

        var candidatos = conhecidas.Concat(varsEx?.Nomes ?? Enumerable.Empty<string>())
            .Concat(perfil?.Padroes.Keys.Select(k => VariaveisElemento.PrefixoPerfil + k) ?? Enumerable.Empty<string>());
        var sug = Semelhanca.MaisParecido(nome, candidatos);
        string msg = $"variável desconhecida {descricao}" + (sug != null ? $" — você quis dizer \"{sug}\"?" : "");
        if (incerta && !ehPerfil) Atencao(r, loc, msg + " (se um componente anterior a publica, está certo).");
        else Erro(r, loc, msg + ".");
    }

    private static void Erro(ResultadoValidacao r, string loc, string msg) => r.Problemas.Add(new ProblemaReceita { Erro = true, Local = loc, Mensagem = msg });
    private static void Atencao(ResultadoValidacao r, string loc, string msg) => r.Problemas.Add(new ProblemaReceita { Erro = false, Local = loc, Mensagem = msg });
}
