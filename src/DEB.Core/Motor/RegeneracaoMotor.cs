using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Motor.Entrada;
using DEB.Core.Motor.Fundacoes;
using DEB.Core.Templates;

namespace DEB.Core.Motor;

/// <summary>Um parâmetro de uma posição que aceita edição manual (sobrescrita), com o valor pela regra e o manual.</summary>
public sealed class ParametroEditavel
{
    /// <summary><see cref="PosicaoArmadura.Chave"/>.</summary>
    public string Posicao { get; set; } = "";
    public string NomePosicao { get; set; } = "";
    public string Parametro { get; set; } = "";
    /// <summary>Valor calculado pela regra (sem nenhuma sobrescrita).</summary>
    public double PelaRegra { get; set; }
    /// <summary>Regra legível (vazio se o componente não informou).</summary>
    public string Regra { get; set; } = "";
    /// <summary>Valor manual; null = vale a regra.</summary>
    public double? Manual { get; set; }
}

/// <summary>
/// Regenera um detalhamento a partir da origem gravada nele (<see cref="OrigemDetalhamento"/>, XData "MOT"): mesmo
/// elemento, perfil e receita ATUAIS (do usuário ou de fábrica) e as edições manuais. Também lista os parâmetros que
/// aceitam edição e grava as edições de volta na origem. Hoje só o bloco sobre estacas tem motor no CAD.
/// </summary>
public static class RegeneracaoMotor
{
    /// <summary>Se há motor para o elemento da origem.</summary>
    public static bool Suporta(OrigemDetalhamento origem) => origem?.Elemento is ElementoBloco;

    private static TemplateBlocoMotor Template(OrigemDetalhamento origem, string pastaPerfis, string pastaReceitas)
    {
        if (origem?.Elemento == null) throw new ArgumentNullException(nameof(origem));
        if (!Suporta(origem))
            throw new NotSupportedException($"Ainda não há motor para regenerar \"{origem.Elemento.Tipo}\" ({origem.Elemento.Nome}).");
        return new TemplateBlocoMotor(false, pastaPerfis, pastaReceitas);
    }

    /// <param name="pastaPerfis">pasta dos perfis do usuário (null = só fábrica)</param>
    /// <param name="pastaReceitas">pasta das receitas do usuário (null = só fábrica)</param>
    public static ResultadoTemplate Gerar(OrigemDetalhamento origem, ConfiguracaoArmadura cfg, string pastaPerfis = null, string pastaReceitas = null) =>
        Template(origem, pastaPerfis, pastaReceitas).Gerar(origem, cfg);

    /// <summary>
    /// Parâmetros editáveis, na ordem das posições do modelo: o valor pela regra vem de um modelo gerado SEM as
    /// sobrescritas; o manual, da origem. Sobrescritas de posições/parâmetros que não existem mais ficam de fora
    /// (o motor avisa que foram ignoradas) e continuam gravadas.
    /// </summary>
    public static List<ParametroEditavel> Parametros(OrigemDetalhamento origem, ConfiguracaoArmadura cfg, string pastaPerfis = null, string pastaReceitas = null)
    {
        var t = Template(origem, pastaPerfis, pastaReceitas);
        var semEdicao = new OrigemDetalhamento
        {
            Perfil = origem.Perfil, Elemento = origem.Elemento, SobreposicoesPerfil = origem.SobreposicoesPerfil,
        };
        var modelo = t.GerarModelo(semEdicao, cfg, new List<string>(), out _);
        var r = new List<ParametroEditavel>();
        foreach (var p in modelo.Posicoes)
            foreach (var kv in p.Parametros)
            {
                if (r.Any(x => x.Posicao == p.Chave && x.Parametro == kv.Key)) continue; // posição repetida (2 camadas)
                r.Add(new ParametroEditavel
                {
                    Posicao = p.Chave, NomePosicao = p.Nome, Parametro = kv.Key, PelaRegra = kv.Value,
                    Regra = p.Regras.TryGetValue(kv.Key, out var g) ? g ?? "" : "",
                    Manual = Sobrescritas.Obter(origem.Sobrescritas, p.Chave, kv.Key),
                });
            }
        return r;
    }

    /// <summary>
    /// Grava na origem as edições da lista: Manual preenchido = sobrescrita; vazio (ou igual à regra) = volta para a
    /// regra. Sobrescritas que não estão na lista não mudam. Devolve quantas mudaram.
    /// </summary>
    public static int Aplicar(OrigemDetalhamento origem, IEnumerable<ParametroEditavel> parametros)
    {
        if (origem == null) throw new ArgumentNullException(nameof(origem));
        origem.Sobrescritas ??= new List<Sobrescrita>();
        int n = 0;
        foreach (var p in parametros ?? Enumerable.Empty<ParametroEditavel>())
        {
            var antes = Sobrescritas.Obter(origem.Sobrescritas, p.Posicao, p.Parametro);
            bool manual = p.Manual.HasValue && Math.Abs(p.Manual.Value - p.PelaRegra) > 1e-9;
            if (manual)
            {
                if (antes.HasValue && Math.Abs(antes.Value - p.Manual.Value) <= 1e-9) continue;
                Sobrescritas.Definir(origem.Sobrescritas, p.Posicao, p.Parametro, p.Manual.Value);
                n++;
            }
            else if (Sobrescritas.Remover(origem.Sobrescritas, p.Posicao, p.Parametro)) n++;
        }
        return n;
    }
}
