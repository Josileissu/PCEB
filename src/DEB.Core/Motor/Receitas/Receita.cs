using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Motor.Componentes;
using DEB.Core.Persistencia;
// "Json" sozinho aqui resolveria para o namespace DEB.Core.Motor.Json: use o apelido.
using TextoJson = DEB.Core.Persistencia.Json;

namespace DEB.Core.Motor.Receitas;

// =====================================================================================================================
// RECEITA: um elemento = composição de componentes, em dados (JSON). O executor (MotorReceita) avalia as variáveis em
// ordem, executa os passos (cada um com os argumentos avaliados NA HORA, já vendo o que os passos anteriores publicaram)
// e roda as verificações.
// {
//   "versao": 1, "tipoElemento": "bloco_estacas", "descricao": "...",
//   "variaveis":    [ { "nome": "c", "formula": "perfil.cobrimento.solo" }, ... ],
//   "passos":       [ { "componente": "malha_face", "nome": "secundária x", "se": "arm.secundaria.bitola > 0",
//                       "argumentos": { "chave": "'secundaria_x'", "angulo": "0", "perna": "perfil.secundaria.perna" } } ],
//   "verificacoes": [ { "condicao": "lemb >= 40", "aviso": "Lemb {lemb} cm < 40 cm (NBR 9062 7.7).", "se": "..." } ]
// }
// Argumento cuja fórmula dá texto → ArgumentosComponente.Textos; número → Numeros. "se" é opcional (nos passos e nas
// verificações). No aviso, {nome} = valor da variável (pt-BR, vírgula).
// =====================================================================================================================

public sealed class Receita
{
    /// <summary>Versão do formato suportada por este programa.</summary>
    public const int VersaoAtual = 1;

    public int Versao { get; set; } = VersaoAtual;
    public string TipoElemento { get; set; } = "";
    public string Descricao { get; set; } = "";
    public List<VariavelReceita> Variaveis { get; set; } = new List<VariavelReceita>();
    public List<PassoReceita> Passos { get; set; } = new List<PassoReceita>();
    public List<VerificacaoReceita> Verificacoes { get; set; } = new List<VerificacaoReceita>();

    /// <summary>Cópia profunda (pelo JSON).</summary>
    public Receita Copiar() => SerializacaoReceita.DeJson(SerializacaoReceita.ParaJson(this));

    public Receita Variavel(string nome, string formula) { Variaveis.Add(new VariavelReceita { Nome = nome, Formula = formula }); return this; }
    public Receita Passo(PassoReceita p) { Passos.Add(p); return this; }
    public Receita Verificacao(string condicao, string aviso, string se = null)
    { Verificacoes.Add(new VerificacaoReceita { Condicao = condicao, Aviso = aviso, Se = se }); return this; }
}

public sealed class VariavelReceita
{
    public string Nome { get; set; } = "";
    public string Formula { get; set; } = "";
}

public sealed class PassoReceita
{
    /// <summary><see cref="IComponente.Tipo"/>.</summary>
    public string Componente { get; set; } = "";
    /// <summary>Nome legível (avisos); vazio = o do componente.</summary>
    public string Nome { get; set; } = "";
    /// <summary>Condição para executar (vazio = sempre).</summary>
    public string Se { get; set; }
    /// <summary>Argumento → fórmula (texto entre aspas simples: "'secundaria_x'").</summary>
    public Dictionary<string, string> Argumentos { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public PassoReceita Arg(string nome, string formula) { Argumentos[nome] = formula; return this; }

    /// <summary>Como o passo aparece nas mensagens: passo 3 "secundária x" (malha_face).</summary>
    public string Rotulo(int indice) =>
        string.IsNullOrWhiteSpace(Nome) || Nome == Componente
            ? $"passo {indice + 1} ({Componente})"
            : $"passo {indice + 1} \"{Nome}\" ({Componente})";
}

public sealed class VerificacaoReceita
{
    /// <summary>Verdadeira = ok; falsa = aviso.</summary>
    public string Condicao { get; set; } = "";
    /// <summary>Texto do aviso; {variavel} é trocado pelo valor (pt-BR).</summary>
    public string Aviso { get; set; } = "";
    /// <summary>Só verifica quando esta condição for verdadeira (vazio = sempre).</summary>
    public string Se { get; set; }
}

/// <summary>Receita ↔ JSON. Erros de formato → <see cref="FormatException"/> com o caminho do campo.</summary>
public static class SerializacaoReceita
{
    public static string Gravar(Receita r) => TextoJson.Gravar(ParaJson(r));

    /// <summary>Lê o texto; JSON inválido → <see cref="ErroJson"/> (linha/coluna); campo errado → FormatException.</summary>
    public static Receita Ler(string texto) => DeJson(TextoJson.Ler(texto));

    public static ValorJson ParaJson(Receita r)
    {
        var o = ValorJson.NovoObjeto()
            .Definir("versao", r.Versao <= 0 ? Receita.VersaoAtual : r.Versao)
            .Definir("tipoElemento", r.TipoElemento ?? "")
            .Definir("descricao", r.Descricao ?? "");
        var vs = ValorJson.NovaLista();
        foreach (var v in r.Variaveis.Where(v => v != null))
            vs.Adicionar(ValorJson.NovoObjeto().Definir("nome", v.Nome ?? "").Definir("formula", v.Formula ?? ""));
        o.Definir("variaveis", vs);
        var ps = ValorJson.NovaLista();
        foreach (var p in r.Passos.Where(p => p != null))
        {
            var po = ValorJson.NovoObjeto().Definir("componente", p.Componente ?? "");
            if (!string.IsNullOrEmpty(p.Nome)) po.Definir("nome", p.Nome);
            if (!string.IsNullOrWhiteSpace(p.Se)) po.Definir("se", p.Se);
            var a = ValorJson.NovoObjeto();
            foreach (var kv in p.Argumentos) a.Definir(kv.Key, kv.Value ?? "");
            po.Definir("argumentos", a);
            ps.Adicionar(po);
        }
        o.Definir("passos", ps);
        var cs = ValorJson.NovaLista();
        foreach (var c in r.Verificacoes.Where(c => c != null))
        {
            var co = ValorJson.NovoObjeto();
            if (!string.IsNullOrWhiteSpace(c.Se)) co.Definir("se", c.Se);
            co.Definir("condicao", c.Condicao ?? "").Definir("aviso", c.Aviso ?? "");
            cs.Adicionar(co);
        }
        o.Definir("verificacoes", cs);
        return o;
    }

    public static Receita DeJson(ValorJson o, string caminho = "receita")
    {
        if (o == null || !o.EhObjeto) throw new FormatException($"{caminho}: a receita deve ser um objeto JSON {{ … }}.");
        var r = new Receita();
        var ver = o["versao"];
        if (ver != null && !ver.EhNulo)
        {
            if (ver.Tipo != TipoJson.Numero || ver.Numero != Math.Floor(ver.Numero)) throw new FormatException($"{caminho}.versao: esperado um número inteiro.");
            if (ver.Numero > Receita.VersaoAtual) throw new FormatException($"{caminho}.versao: versão {ver.Numero} é mais nova que a suportada ({Receita.VersaoAtual}); atualize o programa.");
            if (ver.Numero < 1) throw new FormatException($"{caminho}.versao: versão {ver.Numero} inválida.");
            r.Versao = (int)ver.Numero;
        }
        r.TipoElemento = LeitorJson.Txt(o, "tipoElemento", null, caminho) ?? "";
        r.Descricao = LeitorJson.Txt(o, "descricao", null, caminho) ?? "";

        int i = 0;
        foreach (var v in Lista(o, "variaveis", caminho))
        {
            string cam = $"{caminho}.variaveis[{i++}]";
            LeitorJson.Objeto(v, cam);
            var nome = LeitorJson.Txt(v, "nome", null, cam);
            if (!Formula.NomeValido(nome))
                throw new FormatException($"{cam}.nome: \"{nome}\" não serve como nome de variável (letras, dígitos, _ e pontos; começa com letra; não pode ser nome de função).");
            r.Variaveis.Add(new VariavelReceita { Nome = nome, Formula = FormulaCampo(v, "formula", cam, obrigatoria: true) });
        }
        i = 0;
        foreach (var p in Lista(o, "passos", caminho))
        {
            string cam = $"{caminho}.passos[{i++}]";
            LeitorJson.Objeto(p, cam);
            var passo = new PassoReceita
            {
                Componente = LeitorJson.Txt(p, "componente", null, cam) ?? "",
                Nome = LeitorJson.Txt(p, "nome", null, cam) ?? "",
                Se = FormulaCampo(p, "se", cam, obrigatoria: false),
            };
            if (string.IsNullOrWhiteSpace(passo.Componente)) throw new FormatException($"{cam}.componente: falta o nome do componente.");
            var a = p["argumentos"];
            if (a != null && !a.EhNulo)
            {
                if (!a.EhObjeto) throw new FormatException($"{cam}.argumentos: esperado um objeto {{ \"nome\": \"fórmula\", … }}.");
                foreach (var kv in a.Campos) passo.Argumentos[kv.Key] = FormulaCampo(a, kv.Key, cam + ".argumentos", obrigatoria: true);
            }
            r.Passos.Add(passo);
        }
        i = 0;
        foreach (var c in Lista(o, "verificacoes", caminho))
        {
            string cam = $"{caminho}.verificacoes[{i++}]";
            LeitorJson.Objeto(c, cam);
            r.Verificacoes.Add(new VerificacaoReceita
            {
                Condicao = FormulaCampo(c, "condicao", cam, obrigatoria: true),
                Aviso = LeitorJson.Txt(c, "aviso", null, cam) ?? "",
                Se = FormulaCampo(c, "se", cam, obrigatoria: false),
            });
        }
        return r;
    }

    /// <summary>Compila todas as fórmulas (sem avaliar). Devolve os erros com o caminho (vazio = todas compilam).</summary>
    public static List<string> ErrosDeSintaxe(Receita r)
    {
        var erros = new List<string>();
        foreach (var (cam, f) in Formulas(r))
            if (!Formula.TentarCompilar(f, out _, out var e)) erros.Add($"{cam}: {e}");
        return erros;
    }

    /// <summary>Todas as fórmulas da receita com o caminho de cada uma (para mensagens).</summary>
    public static IEnumerable<(string Caminho, string Formula)> Formulas(Receita r)
    {
        for (int i = 0; i < r.Variaveis.Count; i++)
            yield return ($"variaveis[{i}] \"{r.Variaveis[i].Nome}\"", r.Variaveis[i].Formula);
        for (int i = 0; i < r.Passos.Count; i++)
        {
            var p = r.Passos[i];
            if (!string.IsNullOrWhiteSpace(p.Se)) yield return ($"{p.Rotulo(i)}, \"se\"", p.Se);
            foreach (var kv in p.Argumentos) yield return ($"{p.Rotulo(i)}, argumento \"{kv.Key}\"", kv.Value);
        }
        for (int i = 0; i < r.Verificacoes.Count; i++)
        {
            var c = r.Verificacoes[i];
            if (!string.IsNullOrWhiteSpace(c.Se)) yield return ($"verificacoes[{i}], \"se\"", c.Se);
            yield return ($"verificacoes[{i}], \"condicao\"", c.Condicao);
        }
    }

    // ------------------------------------------------------------------ auxiliares

    // "esperada uma lista" (≠ LeitorJson.Lista, "esperado"): mensagem mantida; ausente → lista vazia.
    private static IReadOnlyList<ValorJson> Lista(ValorJson o, string campo, string cam)
    {
        var v = o[campo];
        if (v == null || v.EhNulo) return Array.Empty<ValorJson>();
        if (!v.EhLista) throw new FormatException($"{cam}.{campo}: esperada uma lista [ … ].");
        return v.Itens;
    }

    /// <summary>Fórmula: texto; número e booleano também são aceitos (viram "1.5", "1"/"0").</summary>
    private static string FormulaCampo(ValorJson o, string campo, string cam, bool obrigatoria)
    {
        var v = o[campo];
        if (v == null || v.EhNulo)
        {
            if (obrigatoria) throw new FormatException($"{cam}.{campo}: falta a fórmula.");
            return null;
        }
        switch (v.Tipo)
        {
            case TipoJson.Texto:
                if (obrigatoria && string.IsNullOrWhiteSpace(v.Texto)) throw new FormatException($"{cam}.{campo}: fórmula vazia.");
                return v.Texto;
            case TipoJson.Numero: return TextoJson.Numero(v.Numero);
            case TipoJson.Booleano: return v.Booleano ? "1" : "0";
            default: throw new FormatException($"{cam}.{campo}: esperada uma fórmula (texto entre aspas, ex.: \"max(c, 5)\").");
        }
    }
}
