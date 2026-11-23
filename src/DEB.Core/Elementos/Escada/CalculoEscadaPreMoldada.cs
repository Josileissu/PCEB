using DEB.Core.Elementos.Viga.PreMoldada.Calculo;
using DEB.Core.Normas.Verificacao;

namespace DEB.Core.Elementos.Escada;

/// <summary>
/// Escada pré-moldada (D43) — EM ANDAMENTO. A geometria da peça (pontas além dos apoios, dentes) já sai de
/// <see cref="GeometriaEscada"/>; faltam aqui: ligações por ponta (apoio direto NBR 9062 7.5: As = (Fd/1,2 + Hd)/fyd e
/// Ash = Asv = Fd/(8fyd); dente 7.4), Hd = k·Fd pelo apoio (7.3.9), pinos (corte e Tabela 12), içamento (5.3) e as barras da peça
/// (grampos de apoio, armadura superior de içamento, barras dentro da peça). Plano em docs/04_arquitetura/escadas.md, seção 6.
/// </summary>
public static partial class CalculoEscada
{
    private static void CalcularPreMoldada(ResultadoEscada r, CoeficientesViga k)
    {
        r.Avisos.Add("Escada pré-moldada: o cálculo das ligações, dos pinos e do içamento e as barras da peça ainda estão em desenvolvimento (D43).");
        r.Verificacoes.Add(K("P0", "Pré-moldada", "Ligações, pinos e içamento", "ainda não calculados", "—", true, "NBR 9062", SituacaoVerificacao.Conferir,
            "Em desenvolvimento: confira as ligações à parte."));
    }
}
