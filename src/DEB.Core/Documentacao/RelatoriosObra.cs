using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DEB.Core.Normas.Verificacao;
using DEB.Core.Persistencia;

namespace DEB.Core.Documentacao;

/// <summary>Pendência "Conferir" de um grupo da obra, com o aceite que vale para ela (null = em aberto).</summary>
public sealed class PendenciaObra
{
    public string Marca { get; set; } = "";
    public ResultadoVerificacao Verificacao { get; set; }
    public AceitePendencia Aceite { get; set; }
}

/// <summary>Decisão do engenheiro sobre uma pendência, vinda da tela do aceite.</summary>
public sealed class DecisaoAceite
{
    public PendenciaObra Pendencia { get; set; }
    public bool Aceitar { get; set; }
    public string Comentario { get; set; } = "";
}

/// <summary>
/// Relatórios de uma obra (D37): os dados de cada grupo ficam gravados ao lado do HTML (<see cref="SubpastaDados"/>) para a tela
/// do aceite listar as pendências e regravar os relatórios sem redesenhar as peças; o índice da obra junta o selo de cada grupo,
/// as pendências em aberto e os aceites (quem, quando, por quê).
/// </summary>
public static class RelatoriosObra
{
    /// <summary>Dados dos grupos dentro de "Relatórios PCEB": um .json por marca.</summary>
    public const string SubpastaDados = "dados";
    public const string NomeIndice = "Verificações da obra.html";

    private static string PastaDados(string pastaObra) => Path.Combine(pastaObra, RelatorioHtml.Subpasta, SubpastaDados);

    private static string ArquivoDados(string pastaObra, string marca) =>
        Path.Combine(PastaDados(pastaObra), Path.ChangeExtension(RelatorioHtml.NomeArquivo(marca).Substring("Verificação - ".Length), ".json"));

    public static ValorJson ParaJson(RelatorioGrupo g)
    {
        var o = ValorJson.NovoObjeto();
        o.Definir("formato", "DEB.Relatorio");
        o.Definir("versao", 1);
        o.Definir("marca", g.Marca);
        o.Definir("elemento", g.Elemento);
        o.Definir("origem", g.Origem);
        o.Definir("data", g.Data.ToString("s", CultureInfo.InvariantCulture));
        var dados = ValorJson.NovaLista();
        o.Definir("dados", dados);
        foreach (var d in g.Dados)
        {
            var x = ValorJson.NovoObjeto();
            dados.Adicionar(x);
            x.Definir("nome", d.Key);
            x.Definir("valor", d.Value);
        }
        var ver = ValorJson.NovaLista();
        o.Definir("verificacoes", ver);
        foreach (var r in g.Verificacoes)
        {
            var x = ValorJson.NovoObjeto();
            ver.Adicionar(x);
            x.Definir("codigo", r.Codigo);
            x.Definir("grupo", r.Grupo);
            x.Definir("descricao", r.Descricao);
            x.Definir("valor", r.Valor);
            x.Definir("limite", r.Limite);
            x.Definir("situacao", r.Situacao.ToString());
            x.Definir("referencia", r.Referencia);
            x.Definir("dica", r.Dica);
        }
        return o;
    }

    public static RelatorioGrupo DeJson(ValorJson o)
    {
        string T(ValorJson v, string campo) => v?[campo]?.Texto ?? "";
        var g = new RelatorioGrupo { Marca = T(o, "marca"), Elemento = T(o, "elemento"), Origem = T(o, "origem") };
        if (DateTime.TryParse(T(o, "data"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)) g.Data = data;
        foreach (var d in o?["dados"]?.Itens ?? Array.Empty<ValorJson>())
            g.Dados.Add(new KeyValuePair<string, string>(T(d, "nome"), T(d, "valor")));
        foreach (var x in o?["verificacoes"]?.Itens ?? Array.Empty<ValorJson>())
            g.Verificacoes.Add(new ResultadoVerificacao
            {
                Codigo = T(x, "codigo"), Grupo = T(x, "grupo"), Descricao = T(x, "descricao"), Valor = T(x, "valor"), Limite = T(x, "limite"),
                Situacao = Enum.TryParse(T(x, "situacao"), out SituacaoVerificacao s) ? s : SituacaoVerificacao.Conferir,
                Referencia = T(x, "referencia"), Dica = T(x, "dica"),
            });
        return g;
    }

    /// <summary>Grava os dados do grupo (chamado junto com o HTML).</summary>
    public static void GravarDados(RelatorioGrupo g, string pastaObra)
    {
        Directory.CreateDirectory(PastaDados(pastaObra));
        File.WriteAllText(ArquivoDados(pastaObra, g.Marca), Json.Gravar(ParaJson(g)), new UTF8Encoding(false));
    }

    /// <summary>Grupos com relatório gravado na obra, por marca (arquivo estragado é pulado).</summary>
    public static List<RelatorioGrupo> Ler(string pastaObra)
    {
        var lista = new List<RelatorioGrupo>();
        string pasta = PastaDados(pastaObra ?? "");
        if (!Directory.Exists(pasta)) return lista;
        foreach (string arq in Directory.GetFiles(pasta, "*.json"))
        {
            try
            {
                var g = DeJson(Json.LerArquivo(arq));
                if (g.Marca.Length > 0) lista.Add(g);
            }
            catch { /* um arquivo ruim não esconde os outros grupos */ }
        }
        lista.Sort((a, b) => CompararMarcas(a.Marca, b.Marca));
        return lista;
    }

    /// <summary>Ordem das marcas com os números pelo valor: "P2" antes de "P10".</summary>
    internal static int CompararMarcas(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int i0 = i, j0 = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                string na = a.Substring(i0, i - i0).TrimStart('0'), nb = b.Substring(j0, j - j0).TrimStart('0');
                int c = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : string.CompareOrdinal(na, nb);
                if (c != 0) return c;
            }
            else
            {
                int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (c != 0) return c;
                i++; j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    /// <summary>Pendências "Conferir" de todos os grupos da obra, com o aceite de cada uma (se houver).</summary>
    public static List<PendenciaObra> Pendencias(string pastaObra) => Pendencias(Ler(pastaObra), AceitesObra.Ler(pastaObra));

    public static List<PendenciaObra> Pendencias(IEnumerable<RelatorioGrupo> grupos, AceitesObra aceites) =>
        grupos.SelectMany(g => g.Verificacoes.Where(r => r.Situacao == SituacaoVerificacao.Conferir)
                                .Select(r => new PendenciaObra { Marca = g.Marca, Verificacao = r, Aceite = aceites.Encontrar(g.Marca, r) }))
              .ToList();

    /// <summary>
    /// Registra as decisões da tela do aceite no arquivo da obra e regrava os relatórios. Aceitar de novo o que já estava aceito
    /// com o mesmo comentário não muda nada (mantém quem aceitou e a data); desmarcar retira o aceite.
    /// </summary>
    public static string Aplicar(string pastaObra, IEnumerable<DecisaoAceite> decisoes, string engenheiro, DateTime? agora = null)
    {
        var aceites = AceitesObra.Ler(pastaObra);
        string data = (agora ?? DateTime.Now).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        foreach (var d in decisoes)
        {
            var r = d.Pendencia.Verificacao;
            string marca = d.Pendencia.Marca, coment = (d.Comentario ?? "").Trim();
            var atual = aceites.Encontrar(marca, r);
            if (d.Aceitar && atual != null && atual.Comentario == coment) continue;
            aceites.Aceites.RemoveAll(a => string.Equals(a.Marca, marca, StringComparison.OrdinalIgnoreCase) && a.Codigo == r.Codigo && a.Descricao == r.Descricao);
            if (d.Aceitar)
                aceites.Aceites.Add(new AceitePendencia
                {
                    Marca = marca, Codigo = r.Codigo, Descricao = r.Descricao, Valor = r.Valor,
                    Engenheiro = (engenheiro ?? "").Trim(), Data = data, Comentario = coment,
                });
        }
        aceites.Salvar(pastaObra);
        return Regravar(pastaObra);
    }

    /// <summary>Regrava o HTML de todos os grupos e o índice com os aceites atuais; devolve o caminho do índice.</summary>
    public static string Regravar(string pastaObra)
    {
        var grupos = Ler(pastaObra);
        var aceites = AceitesObra.Ler(pastaObra);
        string pasta = Path.Combine(pastaObra, RelatorioHtml.Subpasta);
        Directory.CreateDirectory(pasta);
        foreach (var g in grupos)
            File.WriteAllText(Path.Combine(pasta, RelatorioHtml.NomeArquivo(g.Marca)), RelatorioHtml.Gerar(g, aceites), new UTF8Encoding(true));
        return GravarIndice(pastaObra, grupos, aceites);
    }

    public static string GravarIndice(string pastaObra) => GravarIndice(pastaObra, Ler(pastaObra), AceitesObra.Ler(pastaObra));

    private static string GravarIndice(string pastaObra, List<RelatorioGrupo> grupos, AceitesObra aceites)
    {
        string caminho = Path.Combine(pastaObra, RelatorioHtml.Subpasta, NomeIndice);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho));
        File.WriteAllText(caminho, GerarIndice(grupos, aceites), new UTF8Encoding(true));
        return caminho;
    }

    /// <summary>Situação do grupo contando os aceites ("Conferir" aceito vale como OK).</summary>
    public static SituacaoVerificacao Situacao(RelatorioGrupo g, AceitesObra aceites) =>
        g.Verificacoes.Select(r => r.Situacao == SituacaoVerificacao.Conferir && aceites.Encontrar(g.Marca, r) != null ? SituacaoVerificacao.Ok : r.Situacao)
                      .DefaultIfEmpty(SituacaoVerificacao.Ok).Max();

    /// <summary>Índice da obra: selo de cada grupo, pendências em aberto e aceites registrados.</summary>
    public static string GerarIndice(IEnumerable<RelatorioGrupo> grupos, AceitesObra aceites, DateTime? agora = null)
    {
        aceites ??= new AceitesObra();
        var lista = grupos.ToList();
        string H(string s) => RelatorioHtml.H(s);
        var geral = lista.Select(g => Situacao(g, aceites)).DefaultIfEmpty(SituacaoVerificacao.Ok).Max();
        var pend = Pendencias(lista, aceites);
        var naoAtende = lista.SelectMany(g => g.Verificacoes.Where(r => r.Situacao == SituacaoVerificacao.NaoAtende).Select(r => (g.Marca, r))).ToList();

        var sb = new StringBuilder();
        RelatorioHtml.Cabecalho(sb, "Verificações da obra");
        sb.Append("<header>\n<div>\n<h1>Verificações da obra</h1>\n");
        sb.Append($"<div class=\"sub\">{lista.Count} grupo(s) de peças iguais · detalhamento sem cálculo de esforços (NBR 6118:2023, NBR 9062:2017)</div>\n");
        sb.Append("</div>\n<div>\n");
        sb.Append($"<div class=\"selo {RelatorioHtml.Classe(geral)}\">{RelatorioHtml.Selo(geral)}</div>\n");
        sb.Append($"<div class=\"contagem\">{naoAtende.Count} não atende(m) · {pend.Count(p => p.Aceite == null)} a conferir · {pend.Count(p => p.Aceite != null)} aceita(s)</div>\n");
        sb.Append("</div>\n</header>\n");

        sb.Append("<h2>Grupos</h2>\n<div class=\"tabela\"><table>\n<thead><tr><th>Marca</th><th>Peça</th><th>OK</th><th>A conferir</th><th>Aceitas</th><th>Não atende</th><th>Situação</th></tr></thead>\n<tbody>\n");
        foreach (var g in lista)
        {
            var s = Situacao(g, aceites);
            int ac = g.Verificacoes.Count(r => r.Situacao == SituacaoVerificacao.Conferir && aceites.Encontrar(g.Marca, r) != null);
            int cf = g.Verificacoes.Count(r => r.Situacao == SituacaoVerificacao.Conferir) - ac;
            sb.Append($"<tr><td class=\"cod\"><a href=\"{H(Uri.EscapeDataString(RelatorioHtml.NomeArquivo(g.Marca)))}\">{H(g.Marca)}</a></td><td>{H(g.Elemento)}</td>" +
                      $"<td>{g.Verificacoes.Count(r => r.Situacao == SituacaoVerificacao.Ok)}</td><td>{cf}</td><td>{ac}</td>" +
                      $"<td>{g.Verificacoes.Count(r => r.Situacao == SituacaoVerificacao.NaoAtende)}</td>" +
                      $"<td class=\"sit {RelatorioHtml.Classe(s)}\">{RelatorioHtml.Selo(s)}</td></tr>\n");
        }
        sb.Append("</tbody></table></div>\n");

        var abertas = naoAtende.Concat(pend.Where(p => p.Aceite == null).Select(p => (p.Marca, r: p.Verificacao))).ToList();
        if (abertas.Count > 0)
        {
            sb.Append("<h2>Pendências em aberto</h2>\n<div class=\"tabela\"><table>\n<thead><tr><th>Marca</th><th>Código</th><th>Verificação</th><th>Valor</th><th>Limite</th><th>Situação</th><th>Norma</th></tr></thead>\n<tbody>\n");
            foreach (var (marca, r) in abertas)
                sb.Append($"<tr class=\"{RelatorioHtml.Classe(r.Situacao)}\"><td class=\"cod\">{H(marca)}</td><td class=\"cod\">{H(r.Codigo)}</td><td>{H(r.Descricao)}" +
                          (r.Dica.Length > 0 ? $"<div class=\"dica\">{H(r.Dica)}</div>" : "") +
                          $"</td><td>{H(r.Valor)}</td><td>{H(r.Limite)}</td><td class=\"sit {RelatorioHtml.Classe(r.Situacao)}\">{RelatorioHtml.Selo(r.Situacao)}</td><td>{H(r.Referencia)}</td></tr>\n");
            sb.Append("</tbody></table></div>\n");
        }

        var aceitas = pend.Where(p => p.Aceite != null).ToList();
        if (aceitas.Count > 0)
        {
            sb.Append("<h2>Pendências aceitas</h2>\n<div class=\"tabela\"><table>\n<thead><tr><th>Marca</th><th>Código</th><th>Verificação</th><th>Valor</th><th>Aceito por</th><th>Data</th><th>Comentário</th></tr></thead>\n<tbody>\n");
            foreach (var p in aceitas)
                sb.Append($"<tr><td class=\"cod\">{H(p.Marca)}</td><td class=\"cod\">{H(p.Verificacao.Codigo)}</td><td>{H(p.Verificacao.Descricao)}</td><td>{H(p.Verificacao.Valor)}</td>" +
                          $"<td>{H(p.Aceite.Engenheiro)}</td><td>{H(p.Aceite.Data)}</td><td>{H(p.Aceite.Comentario)}</td></tr>\n");
            sb.Append("</tbody></table></div>\n");
        }

        sb.Append("<footer>\n");
        sb.Append($"Gerado pelo PCEB em {agora ?? DateTime.Now:dd/MM/yyyy HH:mm}. Aceites das pendências: arquivo {AceitesObra.NomeArquivo} na pasta da obra.\n");
        sb.Append("</footer>\n</main>\n</body>\n</html>\n");
        return sb.ToString();
    }
}
