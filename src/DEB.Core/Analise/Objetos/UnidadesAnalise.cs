using System;
using System.Collections.Generic;
using DEB.Core.Configuracao.Projeto;
using DEB.Core.Persistencia;

namespace DEB.Core.Analise.Objetos;

/// <summary>
/// Unidades da janela de análise pela configuração do projeto (grupo Unidades). A análise trabalha em kN e cm (momento em kN·cm, mola
/// em kN/cm, cargas lineares em kN/m e de superfície em kN/m²); a janela mostra e recebe na unidade escolhida para cada grandeza.
/// </summary>
public sealed class UnidadesAnalise
{
    private readonly ConfiguracaoProjeto _cfg;
    public UnidadesAnalise(ConfiguracaoProjeto cfg) { _cfg = cfg ?? new ConfiguracaoProjeto(); }

    /// <summary>Unidades internas que a janela converte: símbolo interno → (grandeza, símbolo interno na tabela de unidades).</summary>
    private static readonly Dictionary<string, (Grandeza G, string Sim)> Internas = new Dictionary<string, (Grandeza, string)>
    {
        ["kN"] = (Grandeza.Forca, "kN"),
        ["kN·cm"] = (Grandeza.Momento, "kN·cm"),
        ["kN·m"] = (Grandeza.Momento, "kN·m"),
        ["kN/m"] = (Grandeza.CargaLinear, "kN/m"),
        ["kN/m²"] = (Grandeza.CargaSuperficie, "kN/m²"),
        ["kN·m/m"] = (Grandeza.MomentoDistribuido, "kN·m/m"),
        ["kN/cm"] = (Grandeza.RigidezMola, "kN/cm"),
        ["MPa"] = (Grandeza.Tensao, "MPa"),
        ["kN/m³"] = (Grandeza.PesoEspecifico, "kN/m³"),
    };

    /// <summary>Unidade escolhida para a grandeza (a da configuração, ou a padrão do PCEB).</summary>
    public string Simbolo(Grandeza g)
    {
        string s = _cfg.Unidade(g);
        return Unidades.Aceita(g, s) ? s : Unidades.Opcoes(g)[0];
    }

    /// <summary>Fator: valor na unidade escolhida = valor interno × fator (null se a unidade interna não é convertida).</summary>
    public (double Fator, string Simbolo)? Conversao(string unidadeInterna)
    {
        if (string.IsNullOrEmpty(unidadeInterna) || !Internas.TryGetValue(unidadeInterna, out var x)) return null;
        string sim = Simbolo(x.G);
        double f = Unidades.DaBase(x.G, Unidades.ParaBase(x.G, 1, x.Sim), sim);
        return (f, sim);
    }

    /// <summary>Valor interno para a tela (e o símbolo); sem conversão devolve o próprio.</summary>
    public (double Valor, string Simbolo) ParaTela(string unidadeInterna, double v)
    {
        var c = Conversao(unidadeInterna);
        return c == null ? (v, unidadeInterna ?? "") : (v * c.Value.Fator, c.Value.Simbolo);
    }

    /// <summary>Valor digitado na tela de volta à unidade interna.</summary>
    public double DaTela(string unidadeInterna, double v)
    {
        var c = Conversao(unidadeInterna);
        return c == null ? v : v / c.Value.Fator;
    }

    /// <summary>Texto "valor símbolo" na unidade da tela, com vírgula.</summary>
    public string Texto(string unidadeInterna, double v, string formato = "0.##")
    {
        var (x, s) = ParaTela(unidadeInterna, v);
        return $"{Fmt.Virgula(x, formato)} {s}";
    }

    /// <summary>Para a página: {chave: {s: símbolo, f: fator}} das grandezas que ela desenha (a partir das unidades internas).</summary>
    public ValorJson Json()
    {
        var o = ValorJson.NovoObjeto();
        foreach (var (k, un) in new[] { ("forca", "kN"), ("momento", "kN·cm"), ("linear", "kN/m"), ("superficie", "kN/m²"), ("momDist", "kN·m/m"),
                                        ("mola", "kN/cm"), ("tensao", "MPa") })
        {
            var c = Conversao(un).Value;
            var q = ValorJson.NovoObjeto(); q.Definir("s", c.Simbolo); q.Definir("f", c.Fator); o.Definir(k, q);
        }
        return o;
    }

    // ---------------------------------------------------------------- textos prontos do núcleo (resumos, memória, verificações)

    private static readonly System.Text.RegularExpressions.Regex Valores = new System.Text.RegularExpressions.Regex(
        @"(?<![\w,.])(?<num>-?\d{1,3}(?:\.\d{3})*(?:,\d+)?|-?\d+(?:,\d+)?)\s?(?<un>kN·m/m|kN·m|kN·cm|kN/m²|kN/m³|kN/m|kN/cm²|kN/cm|kN)(?![\w/·²³])",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Converte para as unidades da tela os valores com unidade de força escritos nos textos do núcleo ("Vd 89,4 kN" → "Vd 9,12 tf").
    /// Só os valores com unidade explícita; tensões (MPa) e comprimentos ficam como estão. Sem mudança se a tela está em kN.
    /// </summary>
    public string ConverterTexto(string t)
    {
        if (string.IsNullOrEmpty(t)) return t;
        return Valores.Replace(t, m =>
        {
            string un = m.Groups["un"].Value;
            if (un == "kN/cm²") return m.Value;   // tensão: fica como está
            var c = Conversao(un);
            if (c == null || Math.Abs(c.Value.Fator - 1) < 1e-12 && c.Value.Simbolo == un) return m.Value;
            string n = m.Groups["num"].Value.Replace(".", "").Replace(",", ".");
            if (!double.TryParse(n, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) return m.Value;
            int casas = n.Contains(".") ? n.Length - n.IndexOf('.') - 1 : 0;
            double x = v * c.Value.Fator;
            // casas: as do original, com pelo menos 2 significativas depois da conversão
            int dec = Math.Max(casas, x == 0 ? 0 : Math.Max(0, 2 - (int)Math.Floor(Math.Log10(Math.Abs(x)))));
            return Fmt.Virgula(x, "0." + new string('#', Math.Min(4, Math.Max(1, dec)))) + " " + c.Value.Simbolo;
        });
    }
}
