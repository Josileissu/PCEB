using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DEB.Core.Motor.Prancha;

/// <summary>
/// Vocabulário do gerador de prancha: papéis dos corpos, rótulos padrão e campos do título por tipo de elemento.
/// Tudo aqui é DADO (o perfil sobrescreve os rótulos em <see cref="FolhaPerfil.Rotulos"/>); o código do gerador não sabe
/// que elemento está desenhando. Registre aqui (ou no carregamento do plugin) o que um tipo novo precisar.
/// </summary>
public static class VocabularioPrancha
{
    public const string Estaca = "estaca", Pilar = "pilar", Lastro = "lastro", Regularizacao = "regularizacao";

    /// <summary>Corpos que não são concreto do elemento (não entram no volume nem no contorno do corte).</summary>
    public static readonly HashSet<string> PapeisExternos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { Estaca, Pilar, Lastro, Regularizacao };

    /// <summary>Rótulos "PT|EN". Chave "tipo:chave" (específica do tipo de elemento) ou "chave" (geral).</summary>
    public static readonly Dictionary<string, string> Rotulos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["nivel.topo"] = "(TOPO)|(TOP)",
        ["nivel.fundo_pilar"] = "(FUNDO DO PILAR)|(COLUMN BASE)",
        ["nivel.arrasamento"] = "(COTA DE ARRASAMENTO)|(PILE CUT-OFF LEVEL)",
        ["papel.lastro"] = "CONCRETO MAGRO e={e} cm|LEAN CONCRETE",
        ["papel.regularizacao"] = "REGULARIZAÇÃO DE FUNDO e={e} cm|BOTTOM LEVELING",
        ["peca"] = "PEÇA|PIECE",
        ["grupo"] = "",
        ["nivel_referencia"] = "topo",
        ["producao"] = "INFORMAÇÕES P/ PRODUÇÃO|MANUFACTURING INFORMATION",
        ["notas"] = "NOTAS:|NOTES:",
        ["escala"] = "ESC 1:{escala}|SCALE 1:{escala}",
        ["barras_restantes"] = "BARRAS|BARS",
        // bloco sobre estacas (folhas BPM)
        [ElementoBloco.TipoId + ":nivel.topo"] = "(TOPO DO BLOCO)|(TOP OF PILE CAP)",
        [ElementoBloco.TipoId + ":peca"] = "BLOCO|PILE CAP",
        [ElementoBloco.TipoId + ":grupo"] = "Fundações",
    };

    /// <summary>
    /// Campos extras do título/notas por tipo de elemento (além dos automáticos: propriedades simples do elemento em
    /// minúsculas, {repeticoes}, {pilares}, {escala}, os padrões do perfil e {qtd.&lt;papel&gt;}).
    /// </summary>
    public static readonly Dictionary<string, Action<ElementoEstrutural, ModeloArmadura, IDictionary<string, string>>> CamposPorTipo =
        new Dictionary<string, Action<ElementoEstrutural, ModeloArmadura, IDictionary<string, string>>>(StringComparer.OrdinalIgnoreCase)
        {
            [ElementoBloco.TipoId] = (e, m, d) =>
            {
                var est = m?.Corpos.Where(cp => string.Equals(cp.Papel, Estaca, StringComparison.OrdinalIgnoreCase)).ToList()
                          ?? new List<CorpoConcreto>();
                d["estacas"] = est.Count.ToString(CultureInfo.InvariantCulture);
                double diam = est.Count > 0 ? 2 * est[0].Raio : 0;
                if (diam <= 0 && d.TryGetValue("diamestaca", out var de)) d["diametro"] = de;
                else d["diametro"] = Num(diam);
                // {calice:…} nas notas: o texto só entra com ligação por cálice
                d["calice"] = e is ElementoBloco b && b.Ligacao != LigacaoPilar.Arranque ? "1" : "0";
            },
        };

    /// <summary>Número com vírgula e sem zeros à direita ("12,5").</summary>
    public static string Num(double x) => Calculo.CalculoComprimento.NumeroVirgula(x);

    /// <summary>Rótulo do perfil, senão o padrão do tipo, senão o geral, senão <paramref name="seFaltar"/>.</summary>
    public static string Rotulo(PerfilDetalhamento perfil, string tipo, string chave, string seFaltar = "")
    {
        if (perfil?.Folha?.Rotulos != null && perfil.Folha.Rotulos.TryGetValue(chave, out var p)) return p ?? "";
        if (!string.IsNullOrEmpty(tipo) && Rotulos.TryGetValue(tipo + ":" + chave, out var t)) return t;
        return Rotulos.TryGetValue(chave, out var g) ? g : seFaltar;
    }

    /// <summary>Existe rótulo para a chave (no perfil ou nos padrões)?</summary>
    public static bool TemRotulo(PerfilDetalhamento perfil, string tipo, string chave) =>
        (perfil?.Folha?.Rotulos?.ContainsKey(chave) ?? false) || (!string.IsNullOrEmpty(tipo) && Rotulos.ContainsKey(tipo + ":" + chave))
        || Rotulos.ContainsKey(chave);
}
