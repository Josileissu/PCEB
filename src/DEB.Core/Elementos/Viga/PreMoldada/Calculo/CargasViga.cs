using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DEB.Core.Elementos.Viga.PreMoldada.Calculo;

/// <summary>Carga concentrada na viga (outra viga apoiada nela, por exemplo): posição em cm da ponta esquerda, valores em kN.</summary>
public sealed class CargaConcentrada
{
    public double X { get; set; }
    /// <summary>Parcela permanente (kN, característica).</summary>
    public double G { get; set; }
    /// <summary>Parcela variável (kN, característica).</summary>
    public double Q { get; set; }
}

/// <summary>
/// Cargas digitadas pelo engenheiro para uma viga pré-moldada biapoiada (D40, 22/11/2026: "eu digito"). Valores característicos;
/// o peso próprio da peça (e o da capa do 2º estágio, sobre a largura da viga) é calculado pelo programa e NÃO entra aqui.
/// </summary>
public sealed class CargasViga
{
    /// <summary>Permanente distribuída adicional (kN/m): reação da laje, capa fora da viga, revestimento, alvenaria.</summary>
    public double G { get; set; }
    /// <summary>Variável distribuída (kN/m): sobrecarga de uso transmitida pela laje.</summary>
    public double Q { get; set; }
    public List<CargaConcentrada> Concentradas { get; set; } = new List<CargaConcentrada>();

    /// <summary>
    /// Carga apoiada NA ABA, por lado (kN/m, característica): a laje que descarrega na aba. Soma-se às cargas verticais (G e Q
    /// acima não a incluem) e, fora do eixo da alma, gera TORÇÃO (NBR 6118, 17.5). No T invertido o cálculo considera a montagem
    /// com laje de um lado só e a sobrecarga de um lado só.
    /// </summary>
    public double GAba { get; set; }
    public double QAba { get; set; }
    /// <summary>Distância do eixo da alma à linha da carga na aba (cm); 0 = automática (meio do balanço da aba).</summary>
    public double ExcAba { get; set; }
    /// <summary>Torção de equilíbrio pela carga na aba (D51: o engenheiro liga e desliga); desligada, a aba e a suspensão continuam
    /// dimensionadas, só a torção não.</summary>
    public bool Torcao { get; set; } = true;

    /// <summary>Distância da ponta ao eixo do apoio (cm); 0 = automática (furo do pino, meio do dente ou 10 cm).</summary>
    public double ApoioE { get; set; }
    public double ApoioD { get; set; }

    public bool Vazia => Math.Abs(G) < 1e-9 && Math.Abs(Q) < 1e-9 && Math.Abs(GAba) < 1e-9 && Math.Abs(QAba) < 1e-9 && Concentradas.Count == 0;

    /// <summary>Envoltória de várias vigas do mesmo grupo: maiores distribuídas e todas as concentradas (cada uma com o seu x).</summary>
    public static CargasViga Envoltoria(IEnumerable<CargasViga> cargas)
    {
        var l = cargas.Where(c => c != null).ToList();
        if (l.Count == 0) return new CargasViga();
        if (l.Count == 1) return l[0];
        return new CargasViga
        {
            G = l.Max(c => c.G), Q = l.Max(c => c.Q),
            // concentradas na mesma posição (até 5 cm): fica a maior de cada parcela
            Concentradas = l.SelectMany(c => c.Concentradas).GroupBy(p => Math.Round(p.X / 5))
                .Select(g => new CargaConcentrada { X = g.First().X, G = g.Max(p => p.G), Q = g.Max(p => p.Q) }).ToList(),
            ApoioE = l.Max(c => c.ApoioE), ApoioD = l.Max(c => c.ApoioD),
            GAba = l.Max(c => c.GAba), QAba = l.Max(c => c.QAba), ExcAba = l.Max(c => c.ExcAba),
        };
    }

    // ------------------------------------------------------------------ texto das concentradas (parâmetro do Revit)

    /// <summary>
    /// Concentradas como texto "x: G + Q; x: G + Q" (cm e kN; vírgula ou ponto decimal), o mesmo do parâmetro do Revit.
    /// Ex.: "250: 30 + 12; 480: 18". Q ausente = 0.
    /// </summary>
    public static List<CargaConcentrada> LerConcentradas(string texto, List<string> erros = null)
    {
        var r = new List<CargaConcentrada>();
        foreach (string parte in (texto ?? "").Split(';'))
        {
            string s = parte.Trim();
            if (s.Length == 0) continue;
            int i = s.IndexOf(':');
            if (i < 0) { erros?.Add($"Carga concentrada \"{s}\": use \"x: G + Q\" (cm: kN + kN)."); continue; }
            string[] gq = s.Substring(i + 1).Split('+');
            if (!Num(s.Substring(0, i), out double x) || !Num(gq[0], out double g) || (gq.Length > 1 && !Num(gq[1], out _)))
            { erros?.Add($"Carga concentrada \"{s}\": número inválido."); continue; }
            double q = 0;
            if (gq.Length > 1) Num(gq[1], out q);
            r.Add(new CargaConcentrada { X = x, G = g, Q = q });
        }
        return r;
    }

    public static string TextoConcentradas(IEnumerable<CargaConcentrada> l) =>
        string.Join("; ", l.Select(p => $"{Fmt.Virgula(p.X, "0.#")}: {Fmt.Virgula(p.G, "0.##")}" + (Math.Abs(p.Q) > 1e-9 ? $" + {Fmt.Virgula(p.Q, "0.##")}" : "")));

    private static bool Num(string s, out double v) =>
        double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}

/// <summary>Coeficientes do cálculo (NBR 8681, NBR 6118, NBR 9062). Valores de partida editáveis pelo engenheiro.</summary>
public sealed class CoeficientesViga
{
    /// <summary>γg e γq do ELU, combinação normal (NBR 6118, Tabela 11.1).</summary>
    public double GamaG { get; set; } = 1.4;
    public double GamaQ { get; set; } = 1.4;
    public double GamaC { get; set; } = 1.4;
    public double GamaS { get; set; } = 1.15;
    /// <summary>ψ1 (combinação frequente, fissuração) e ψ2 (quase permanente, flecha) — edifícios residenciais: 0,4 e 0,3 (Tabela 11.2).</summary>
    public double Psi1 { get; set; } = 0.4;
    public double Psi2 { get; set; } = 0.3;
    /// <summary>Coeficiente dinâmico do içamento (NBR 9062, 5.3.3: 1,3 para o manuseio na fábrica e na obra).</summary>
    public double CoefDinamico { get; set; } = 1.3;
    /// <summary>Idade (meses) em que as cargas permanentes entram (flecha diferida, NBR 6118 17.3.2.1.2).</summary>
    public double IdadeCarregamentoMeses { get; set; } = 1;
    /// <summary>αE do módulo de elasticidade (granito/gnaisse = 1,0; basalto 1,2; calcário 0,9; arenito 0,7).</summary>
    public double AlfaE { get; set; } = 1.0;
    /// <summary>Limite da flecha total (aceitabilidade sensorial, Tabela 13.3): ℓ/250.</summary>
    public double FlechaLimite { get; set; } = 250;
}
