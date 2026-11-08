using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Motor.Perfis;
using DEB.Core.Motor.Receitas;
using DEB.Core.Templates;
using DEB.Core.Templates.Blocos;

namespace DEB.Core.Motor.Fundacoes;

/// <summary>Variante do bloco na lista do ARMTEMPLATE (pedido do usuário, 2026-11-08).</summary>
public enum VarianteBloco { Simples, Arranque, Embutimento, EmbutimentoColarinho }

/// <summary>
/// Uma entrada do ARMTEMPLATE por número de estacas e variante (simples, com arranque, com embutimento, com embutimento e
/// colarinho). Mesma geração do <see cref="TemplateBlocoMotor"/>: a janela só mostra os campos da variante; o número de
/// estacas, o arranjo e a ligação do pilar ficam fixos.
/// </summary>
public sealed class TemplateBlocoVariante : ITemplateArmadura
{
    private readonly TemplateBlocoMotor _motor;
    private readonly IReadOnlyList<ParametroTemplate> _base;
    private readonly Dictionary<string, string> _fixos;

    public int Estacas { get; }
    public VarianteBloco Variante { get; }

    /// <summary>Números de estacas com arranjo no motor (5: aguardando o arranjo do escritório).</summary>
    public static readonly int[] EstacasSuportadas = { 1, 2, 3, 4 };

    /// <param name="pastaPerfis">pasta dos perfis do usuário (null = só fábrica)</param>
    /// <param name="pastaReceitas">pasta das receitas do usuário (null = só fábrica)</param>
    public TemplateBlocoVariante(int estacas, VarianteBloco variante, string pastaPerfis = null, string pastaReceitas = null)
    {
        if (!EstacasSuportadas.Contains(estacas)) throw new ArgumentOutOfRangeException(nameof(estacas), $"Bloco de {estacas} estacas ainda não tem arranjo.");
        Estacas = estacas;
        Variante = variante;
        _motor = new TemplateBlocoMotor(estacas == 1, pastaPerfis, pastaReceitas);
        _base = _motor.Parametros;
        _fixos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ligacao"] = variante switch
            {
                VarianteBloco.Simples => TemplateBlocoEstacas.SemLigacao,
                VarianteBloco.Arranque => TemplateBlocoEstacas.Arranque,
                VarianteBloco.Embutimento => TemplateBlocoEstacas.Embutido,
                _ => TemplateBlocoEstacas.Colarinho,
            },
            ["Arranjo"] = estacas == 3 ? TemplateBlocoEstacas.Triangulo : TemplateBlocoEstacas.Grade,
            ["NEstX"] = estacas == 1 ? "1" : "2",
            ["NEstY"] = estacas == 4 ? "2" : "1",
        };
        var ocultos = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ligacao", "Arranjo", "NEstX", "NEstY" };
        if (estacas == 1) ocultos.UnionWith(new[] { "EspX", "EspY", "PrincBitola", "PrincQtd", "PrincEsp", "PrincPerna" });
        if (estacas == 2 || estacas == 3) ocultos.Add("EspY");
        var calice = new[] { "hColar", "tParede", "Folga", "Lemb", "Regularizacao", "Interface", "CobCalice", "CalTopoBitola", "CalTopoQtd",
            "CalTopoEsp", "CalBitola", "CalEsp", "CalGrampos", "CalVertBitola", "CalVertQtd", "SuspBitola", "SuspQtd", "SuspEsp",
            "FundoBitola", "FundoQtd", "FundoComp" };
        var arranque = _base.Where(p => p.Nome.StartsWith("Arr", StringComparison.Ordinal)).Select(p => p.Nome).ToList();
        var pilar = new[] { "PilarBx", "PilarBy", "PilarEx", "PilarEy" };
        switch (variante)
        {
            case VarianteBloco.Simples: ocultos.UnionWith(calice); ocultos.UnionWith(arranque); ocultos.UnionWith(pilar); break;
            case VarianteBloco.Arranque: ocultos.UnionWith(calice); break;
            case VarianteBloco.Embutimento: ocultos.UnionWith(arranque); ocultos.Add("hColar"); break;
            default: ocultos.UnionWith(arranque); break;
        }
        Parametros = _base.Where(p => !ocultos.Contains(p.Nome)).ToList();
    }

    /// <summary>Com as pastas do usuário em %AppData%\PCEB (o que o CAD usa).</summary>
    public static TemplateBlocoVariante DoUsuario(int estacas, VarianteBloco variante) =>
        new TemplateBlocoVariante(estacas, variante, RepositorioPerfis.PastaPadrao(), RepositorioReceitas.PastaPadrao());

    /// <summary>Todas as variantes suportadas (para registrar no catálogo).</summary>
    public static IEnumerable<TemplateBlocoVariante> Todas(string pastaPerfis = null, string pastaReceitas = null) =>
        from n in EstacasSuportadas
        from v in new[] { VarianteBloco.Simples, VarianteBloco.Arranque, VarianteBloco.Embutimento, VarianteBloco.EmbutimentoColarinho }
        select new TemplateBlocoVariante(n, v, pastaPerfis, pastaReceitas);

    private static string IdVariante(VarianteBloco v) => v switch
    {
        VarianteBloco.Simples => "simples",
        VarianteBloco.Arranque => "arranque",
        VarianteBloco.Embutimento => "embutimento",
        _ => "embutimento-colarinho",
    };

    public string Id => $"bloco-{Estacas}e-{IdVariante(Variante)}";

    public string Nome => Variante switch
    {
        VarianteBloco.Simples => "Simples",
        VarianteBloco.Arranque => "Com arranque",
        VarianteBloco.Embutimento => "Com embutimento",
        _ => "Com embutimento e colarinho",
    };

    public CategoriaElemento Categoria => Estacas switch
    {
        1 => CategoriaElemento.Bloco1Estaca,
        2 => CategoriaElemento.Bloco2Estacas,
        3 => CategoriaElemento.Bloco3Estacas,
        _ => CategoriaElemento.Bloco4Estacas,
    };

    public string Descricao =>
        $"Bloco de {Estacas} estaca{(Estacas > 1 ? "s" : "")}" + (Estacas == 3 ? " (triângulo)" : Estacas == 4 ? " (2 × 2)" : Estacas == 2 ? " (em linha)" : "") +
        Variante switch
        {
            VarianteBloco.Simples => ", sem pilar ligado.",
            VarianteBloco.Arranque => ", com arranque do pilar moldado in loco (barras e estribos do pilar entram na relação do bloco).",
            VarianteBloco.Embutimento => ", com o pilar pré-moldado embutido no bloco (cálice embutido).",
            _ => ", com o pilar pré-moldado num cálice com colarinho.",
        };

    public IReadOnlyList<ParametroTemplate> Parametros { get; }

    /// <summary>Valores completos (janela + campos fixos + padrões dos ocultos) para o gerador do bloco.</summary>
    public ValoresTemplate Completar(ValoresTemplate valores)
    {
        var v = new ValoresTemplate(_base);
        foreach (var p in Parametros)
        {
            string s = valores?[p.Nome];
            v[p.Nome] = string.IsNullOrEmpty(s) ? p.Padrao : s;
        }
        foreach (var kv in _fixos) v[kv.Key] = kv.Value;
        return v;
    }

    public ResultadoTemplate Gerar(ValoresTemplate valores, ConfiguracaoArmadura cfg) => _motor.Gerar(Completar(valores), cfg);
}
