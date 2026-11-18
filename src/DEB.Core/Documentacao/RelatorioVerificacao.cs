using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using DEB.Core.Normas.Verificacao;

namespace DEB.Core.Documentacao;

/// <summary>
/// Relatório de verificação de um grupo de peças iguais (D37, 18/11/2026): HTML em português, um por grupo, com todas as
/// verificações (inclusive as OK), o selo geral e as pendências "Conferir" com o aceite do engenheiro, que fica num arquivo da
/// obra (<see cref="AceitesObra"/>). O modelo mora no núcleo (D26); o CAD e o Revit só mandam gravar.
/// </summary>
public sealed class RelatorioGrupo
{
    /// <summary>Marca do grupo ("P61"); vira o nome do arquivo.</summary>
    public string Marca { get; set; } = "";
    /// <summary>Tipo da peça ("Pilar pré-moldado").</summary>
    public string Elemento { get; set; } = "";
    /// <summary>Dados do cabeçalho (seção, comprimento, materiais…), na ordem.</summary>
    public List<KeyValuePair<string, string>> Dados { get; set; } = new List<KeyValuePair<string, string>>();
    public ListaVerificacoes Verificacoes { get; set; } = new ListaVerificacoes();
    /// <summary>De onde veio (template do CAD, modelo do Revit, arquivo de peças) — texto livre para o rodapé.</summary>
    public string Origem { get; set; } = "";
    public DateTime Data { get; set; } = DateTime.Now;
}

/// <summary>Aceite do engenheiro para uma pendência "Conferir" (D37): quem, quando e por quê.</summary>
[DataContract]
public sealed class AceitePendencia
{
    [DataMember(Order = 1)] public string Marca { get; set; } = "";
    [DataMember(Order = 2)] public string Codigo { get; set; } = "";
    /// <summary>Descrição da verificação no momento do aceite: se a regra ou o valor mudar, o aceite deixa de valer.</summary>
    [DataMember(Order = 3)] public string Descricao { get; set; } = "";
    [DataMember(Order = 4)] public string Valor { get; set; } = "";
    [DataMember(Order = 5)] public string Engenheiro { get; set; } = "";
    [DataMember(Order = 6)] public string Data { get; set; } = "";
    [DataMember(Order = 7)] public string Comentario { get; set; } = "";
}

/// <summary>Aceites das pendências de uma obra: arquivo <see cref="NomeArquivo"/> na pasta da obra (ao lado do modelo).</summary>
[DataContract]
public sealed class AceitesObra
{
    public const string NomeArquivo = "PCEB_aceites.json";

    [DataMember(Order = 1)] public List<AceitePendencia> Aceites { get; set; } = new List<AceitePendencia>();

    /// <summary>Aceite que vale para esta verificação da marca (mesmo código, descrição e valor).</summary>
    public AceitePendencia Encontrar(string marca, ResultadoVerificacao r) =>
        Aceites.LastOrDefault(a => string.Equals(a.Marca, marca, StringComparison.OrdinalIgnoreCase) && a.Codigo == r.Codigo
                                   && a.Descricao == r.Descricao && a.Valor == r.Valor);

    public static AceitesObra Ler(string pastaObra)
    {
        string caminho = Path.Combine(pastaObra ?? "", NomeArquivo);
        try
        {
            if (File.Exists(caminho))
            {
                var ser = new DataContractJsonSerializer(typeof(AceitesObra));
                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(caminho, Encoding.UTF8)));
                var a = (AceitesObra)ser.ReadObject(ms);
                a.Aceites ??= new List<AceitePendencia>();
                return a;
            }
        }
        catch { /* arquivo estragado: sem aceites (o relatório mostra as pendências em aberto) */ }
        return new AceitesObra();
    }

    public void Salvar(string pastaObra)
    {
        var ser = new DataContractJsonSerializer(typeof(AceitesObra));
        using var ms = new MemoryStream();
        using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
            ser.WriteObject(w, this);
        Directory.CreateDirectory(pastaObra);
        File.WriteAllBytes(Path.Combine(pastaObra, NomeArquivo), ms.ToArray());
    }
}

/// <summary>Gera o HTML (uma página, sem arquivos externos; imprime em PDF pelo navegador ou pela PDF24).</summary>
public static class RelatorioHtml
{
    /// <summary>Subpasta dos relatórios dentro da pasta da obra.</summary>
    public const string Subpasta = "Relatórios PCEB";

    /// <summary>Nome do arquivo do grupo: "Verificação - P61.html" (caracteres proibidos no Windows viram "-").</summary>
    public static string NomeArquivo(string marca)
    {
        string m = string.Concat((string.IsNullOrWhiteSpace(marca) ? "peça" : marca.Trim()).Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch));
        return $"Verificação - {m}.html";
    }

    /// <summary>Grava o relatório em &lt;pastaObra&gt;\Relatórios PCEB\ e devolve o caminho.</summary>
    public static string Gravar(RelatorioGrupo g, string pastaObra)
    {
        string pasta = Path.Combine(pastaObra, Subpasta);
        Directory.CreateDirectory(pasta);
        string caminho = Path.Combine(pasta, NomeArquivo(g.Marca));
        File.WriteAllText(caminho, Gerar(g, AceitesObra.Ler(pastaObra)), new UTF8Encoding(true));
        // dados do grupo e índice da obra (tela do aceite); falha aqui não tira o relatório já gravado
        try { RelatoriosObra.GravarDados(g, pastaObra); RelatoriosObra.GravarIndice(pastaObra); } catch { }
        return caminho;
    }

    /// <summary>Escape mínimo (o arquivo é UTF-8: acentos ficam legíveis, sem &amp;#224;).</summary>
    internal static string H(string s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    internal static string Selo(SituacaoVerificacao s) => s switch
    {
        SituacaoVerificacao.NaoAtende => "NÃO ATENDE",
        SituacaoVerificacao.Conferir => "CONFERIR",
        _ => "OK",
    };

    internal static string Classe(SituacaoVerificacao s) => s switch
    {
        SituacaoVerificacao.NaoAtende => "na", SituacaoVerificacao.Conferir => "cf", _ => "ok",
    };

    /// <summary>Início da página (estilo comum do relatório do grupo e do índice da obra).</summary>
    internal static void Cabecalho(StringBuilder sb, string titulo)
    {
        sb.Append("<!DOCTYPE html>\n<html lang=\"pt-BR\">\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append($"<title>{H(titulo)}</title>\n");
        sb.Append(@"<style>
:root { --tinta: #1d2430; --suave: #5b6576; --linha: #d9dee6; --fundo: #ffffff; --faixa: #f4f6f9;
        --ok: #1f7a3f; --ok-f: #e6f4ea; --cf: #8a5a00; --cf-f: #fff4dc; --na: #a8221a; --na-f: #fde8e6; }
* { box-sizing: border-box; }
body { margin: 0; padding: 24px 16px; background: var(--fundo); color: var(--tinta);
       font: 14px/1.45 ""Segoe UI"", Arial, sans-serif; }
main { max-width: 1100px; margin: 0 auto; }
header { display: flex; flex-wrap: wrap; gap: 12px 24px; align-items: flex-start; justify-content: space-between;
         border-bottom: 2px solid var(--tinta); padding-bottom: 12px; }
h1 { font-size: 22px; margin: 0; }
.sub { color: var(--suave); margin-top: 2px; }
.selo { font-weight: 700; letter-spacing: .04em; padding: 8px 14px; border-radius: 6px; border: 2px solid; white-space: nowrap; }
.selo.ok { color: var(--ok); background: var(--ok-f); } .selo.cf { color: var(--cf); background: var(--cf-f); }
.selo.na { color: var(--na); background: var(--na-f); }
.contagem { color: var(--suave); font-size: 13px; margin-top: 6px; text-align: right; }
h2 { font-size: 16px; margin: 22px 0 8px; }
dl { display: grid; grid-template-columns: max-content 1fr; gap: 4px 16px; margin: 12px 0 0; }
dt { color: var(--suave); } dd { margin: 0; }
.tabela { overflow-x: auto; }
table { width: 100%; border-collapse: collapse; font-size: 13px; }
th, td { text-align: left; padding: 6px 8px; border-bottom: 1px solid var(--linha); vertical-align: top; }
th { background: var(--faixa); font-weight: 600; }
td.cod { font-family: Consolas, monospace; white-space: nowrap; }
.sit { font-weight: 700; white-space: nowrap; }
.sit.ok { color: var(--ok); } .sit.cf { color: var(--cf); } .sit.na { color: var(--na); }
tr.na td { background: var(--na-f); } tr.cf td { background: var(--cf-f); }
.dica { color: var(--suave); font-size: 12px; margin-top: 2px; }
a { color: inherit; }
.aceite { color: var(--ok); font-size: 12px; margin-top: 2px; }
footer { margin-top: 28px; color: var(--suave); font-size: 12px; border-top: 1px solid var(--linha); padding-top: 8px; }
@media print { body { padding: 0; } tr { break-inside: avoid; } }
</style>
</head>
<body>
<main>
");
    }

    public static string Gerar(RelatorioGrupo g, AceitesObra aceites = null)
    {
        aceites ??= new AceitesObra();
        var lista = g.Verificacoes ?? new ListaVerificacoes();
        // situação de cada linha considerando os aceites: "Conferir" aceito conta como OK no selo geral
        AceitePendencia Aceite(ResultadoVerificacao r) => r.Situacao == SituacaoVerificacao.Conferir ? aceites.Encontrar(g.Marca, r) : null;
        var efetiva = lista.Select(r => Aceite(r) != null ? SituacaoVerificacao.Ok : r.Situacao).DefaultIfEmpty(SituacaoVerificacao.Ok).Max();
        int nNa = lista.Count(r => r.Situacao == SituacaoVerificacao.NaoAtende);
        int nCf = lista.Count(r => r.Situacao == SituacaoVerificacao.Conferir && Aceite(r) == null);
        int nAc = lista.Count(r => Aceite(r) != null);
        int nOk = lista.Count(r => r.Situacao == SituacaoVerificacao.Ok);

        var sb = new StringBuilder();
        Cabecalho(sb, "Verificação - " + g.Marca);
        sb.Append("<header>\n<div>\n");
        sb.Append($"<h1>Verificação normativa — {H(g.Marca)}</h1>\n");
        sb.Append($"<div class=\"sub\">{H(g.Elemento)} · detalhamento sem cálculo de esforços (NBR 6118:2023, NBR 9062:2017)</div>\n");
        sb.Append("</div>\n<div>\n");
        sb.Append($"<div class=\"selo {Classe(efetiva)}\">{Selo(efetiva)}</div>\n");
        sb.Append($"<div class=\"contagem\">{nOk} OK · {nCf} a conferir · {nAc} aceita(s) · {nNa} não atende(m)</div>\n");
        sb.Append("</div>\n</header>\n");

        if (g.Dados.Count > 0)
        {
            sb.Append("<h2>Dados do grupo</h2>\n<dl>\n");
            foreach (var d in g.Dados) sb.Append($"<dt>{H(d.Key)}</dt><dd>{H(d.Value)}</dd>\n");
            sb.Append("</dl>\n");
        }

        var pendentes = lista.Where(r => r.Situacao != SituacaoVerificacao.Ok && Aceite(r) == null).ToList();
        if (pendentes.Count > 0)
        {
            sb.Append("<h2>Pendências</h2>\n<div class=\"tabela\"><table>\n<thead><tr><th>Código</th><th>Verificação</th><th>Situação</th><th>Norma</th></tr></thead>\n<tbody>\n");
            foreach (var r in pendentes.OrderByDescending(r => r.Situacao))
                sb.Append($"<tr class=\"{Classe(r.Situacao)}\"><td class=\"cod\">{H(r.Codigo)}</td><td>{H(r.Descricao)}" +
                          (r.Dica.Length > 0 ? $"<div class=\"dica\">{H(r.Dica)}</div>" : "") +
                          $"</td><td class=\"sit {Classe(r.Situacao)}\">{Selo(r.Situacao)}</td><td>{H(r.Referencia)}</td></tr>\n");
            sb.Append("</tbody></table></div>\n");
        }

        foreach (var grupo in lista.GroupBy(r => string.IsNullOrEmpty(r.Grupo) ? "Geral" : r.Grupo))
        {
            sb.Append($"<h2>{H(grupo.Key)}</h2>\n<div class=\"tabela\"><table>\n");
            sb.Append("<thead><tr><th>Código</th><th>Verificação</th><th>Valor</th><th>Limite</th><th>Situação</th><th>Norma</th></tr></thead>\n<tbody>\n");
            foreach (var r in grupo)
            {
                var ac = Aceite(r);
                var sit = ac != null ? SituacaoVerificacao.Ok : r.Situacao;
                sb.Append($"<tr class=\"{(ac != null ? "ok" : Classe(r.Situacao))}\"><td class=\"cod\">{H(r.Codigo)}</td><td>{H(r.Descricao)}");
                if (r.Dica.Length > 0 && r.Situacao != SituacaoVerificacao.Ok) sb.Append($"<div class=\"dica\">{H(r.Dica)}</div>");
                if (ac != null)
                    sb.Append($"<div class=\"aceite\">Aceito por {H(ac.Engenheiro)} em {H(ac.Data)}" +
                              (string.IsNullOrWhiteSpace(ac.Comentario) ? "" : $": {H(ac.Comentario)}") + "</div>");
                sb.Append($"</td><td>{H(r.Valor)}</td><td>{H(r.Limite)}</td>");
                sb.Append($"<td class=\"sit {Classe(sit)}\">{(ac != null ? "ACEITO" : Selo(r.Situacao))}</td><td>{H(r.Referencia)}</td></tr>\n");
            }
            sb.Append("</tbody></table></div>\n");
        }

        sb.Append("<footer>\n");
        sb.Append($"Gerado pelo PCEB em {g.Data:dd/MM/yyyy HH:mm}" + (string.IsNullOrWhiteSpace(g.Origem) ? "" : $" · {H(g.Origem)}") + ".<br>\n");
        sb.Append("Verificações do desenho pela norma, sem esforços: os limites que dependem de esforço aparecem como \"Conferir\" com o " +
                  $"limite mais rigoroso. Aceites das pendências: arquivo {AceitesObra.NomeArquivo} na pasta da obra (comando ARMACEITES no CAD).\n");
        sb.Append("</footer>\n</main>\n</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>Relatório a partir do resultado de um template (null se o template não preenche as verificações).</summary>
    public static RelatorioGrupo DoTemplate(Motor.ResultadoTemplate r, string marca, string origem)
    {
        if (r == null || string.IsNullOrEmpty(r.ElementoRelatorio)) return null;
        return new RelatorioGrupo
        {
            Marca = string.IsNullOrWhiteSpace(marca) ? r.Detalhamento : marca, Elemento = r.ElementoRelatorio,
            Dados = r.DadosRelatorio.ToList(), Verificacoes = r.Verificacoes, Origem = origem,
        };
    }
}
