using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Normas.Verificacao;

/// <summary>Situação de uma verificação normativa do detalhamento (docs/04_arquitetura/verificacoes_normativas.md).</summary>
public enum SituacaoVerificacao
{
    /// <summary>Atende.</summary>
    Ok,
    /// <summary>Não dá para decidir sem esforço (ou é recomendação, ou o programa ajustou um valor): mostra o limite e pede conferência.</summary>
    Conferir,
    /// <summary>Contraria a norma com os dados do desenho.</summary>
    NaoAtende,
}

/// <summary>
/// Resultado de uma regra: código (P8, G2…), o que conferiu, valor usado, limite, situação e referência da norma.
/// O mesmo resultado vai para o aviso do CAD, para a janela do Revit e, depois, para o relatório de verificação.
/// </summary>
public sealed class ResultadoVerificacao
{
    public string Codigo { get; set; } = "";
    /// <summary>Grupo para o relatório (Geral, Longitudinal, Estribos, Cobrimento…).</summary>
    public string Grupo { get; set; } = "";
    /// <summary>O que foi conferido, em uma frase ("Bitola longitudinal ø25 > 1/8 da menor dimensão").</summary>
    public string Descricao { get; set; } = "";
    /// <summary>Valor encontrado no desenho, já formatado com unidade.</summary>
    public string Valor { get; set; } = "";
    /// <summary>Limite da norma, já formatado ("≤ 25 mm", "≥ 0,40 %").</summary>
    public string Limite { get; set; } = "";
    public SituacaoVerificacao Situacao { get; set; }
    /// <summary>Norma e item ("NBR 6118, 18.4.2.1").</summary>
    public string Referencia { get; set; } = "";
    /// <summary>Como resolver (opcional).</summary>
    public string Dica { get; set; } = "";

    /// <summary>Texto de uma linha para a lista de avisos: "Não atende (P3): … (NBR 6118, 18.4.2.1)."</summary>
    public string Mensagem
    {
        get
        {
            string sit = Situacao == SituacaoVerificacao.NaoAtende ? "Não atende" : Situacao == SituacaoVerificacao.Conferir ? "Conferir" : "OK";
            string vl = Valor.Length > 0 && Limite.Length > 0 ? $": {Valor} (limite {Limite})" : Valor.Length > 0 ? $": {Valor}" : "";
            string dica = Dica.Length > 0 ? " " + Dica : "";
            return $"{sit} ({Codigo}): {Descricao}{vl} ({Referencia}).{dica}";
        }
    }

    public override string ToString() => Mensagem;
}

/// <summary>Lista de resultados de uma peça, com a situação geral (o pior resultado).</summary>
public sealed class ListaVerificacoes : List<ResultadoVerificacao>
{
    public SituacaoVerificacao Geral => Count == 0 ? SituacaoVerificacao.Ok : this.Max(r => r.Situacao);

    /// <summary>Só o que precisa de atenção (Conferir e Não atende), sem repetir a mesma mensagem.</summary>
    public IEnumerable<string> Avisos() =>
        this.Where(r => r.Situacao != SituacaoVerificacao.Ok).Select(r => r.Mensagem).Distinct();

    public void Add(ResultadoVerificacao r, bool soFalhas) { if (r != null && (!soFalhas || r.Situacao != SituacaoVerificacao.Ok)) Add(r); }
}
