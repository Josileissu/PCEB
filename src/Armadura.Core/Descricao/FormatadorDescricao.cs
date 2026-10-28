using System.Collections.Generic;
using System.Globalization;
using Armadura.Core.Calculo;
using Armadura.Core.Configuracao;
using Armadura.Core.Modelo;

namespace Armadura.Core.Descricao;

/// <summary>Monta o texto da descrição da armadura nos três formatos da seção 4.1.</summary>
public static class FormatadorDescricao
{
    public static string Formatar(DadosArmadura d, double comprimentoUnitarioCm, ConfiguracaoArmadura cfg)
    {
        var c = d.Campos;
        bool usaQtd = c.HasFlag(CamposDescricao.Quantidade);
        bool usaN = c.HasFlag(CamposDescricao.Numero);
        bool usaBitola = c.HasFlag(CamposDescricao.Bitola);
        bool usaEsp = c.HasFlag(CamposDescricao.Espacamento) && d.Espacamento > 0;
        bool usaComp = c.HasFlag(CamposDescricao.Comprimento);

        // em camadas: "10x(3+1)" = 10 nervuras com 3+1 barras cada
        string qtd = d.Quantidade.ToString(CultureInfo.InvariantCulture)
                     + (string.IsNullOrWhiteSpace(d.Camadas) ? "" : "x(" + d.Camadas.Replace(" ", "") + ")");
        // repetições coladas na quantidade, sem parênteses: "2x11 N1 ø10.0" (pedido do usuário, 2026-10-28)
        string rep = Repeticao(d.Repeticoes);
        qtd = rep + qtd;
        string n = cfg.PrefixoPosicao + d.Numero.ToString(CultureInfo.InvariantCulture);
        // tela soldada: sem numeração, só quantidade e modelo ("2 Q-138"; repetida: "2x1 Q-138")
        if (d.EhTela) return rep + d.Quantidade.ToString(CultureInfo.InvariantCulture) + " " + d.Tela.Trim();
        string bitola = d.EhTrelica
            ? d.Trelica.Trim() // treliça: o modelo no lugar da bitola ("4 N5 TR 8644 C=300")
            : cfg.SimboloDiametro + d.Bitola.ToString("0.0", CultureInfo.InvariantCulture);
        // cordoalha: o aço faz parte da designação ("ø12.7 CP-190")
        if (Tabelas.TabelaBarras.EhCordoalha(d.Aco)) bitola += " " + d.Aco.Trim().ToUpperInvariant();
        string esp = "c/" + CalculoComprimento.NumeroCompacto(d.Espacamento);
        string comp = d.TemVar ? cfg.PrefixoComprimento + "=VAR"
            : d.Corrido ? "CORRIDO"
            : cfg.PrefixoComprimento + "=" + CalculoComprimento.Numero(comprimentoUnitarioCm, cfg.CasasDecimais);

        var partes = new List<string>();
        switch (cfg.PadraoDescricao)
        {
            case PadraoDescricao.NumeroPrimeiro:
                // "N3 – 4ø8.0 c/10 C=200"
                if (usaN) partes.Add(n + (usaQtd || usaBitola ? " –" : ""));
                if (usaQtd || usaBitola) partes.Add((usaQtd ? qtd : "") + (usaBitola ? bitola : ""));
                if (usaEsp) partes.Add(esp);
                if (usaComp) partes.Add(comp);
                break;

            case PadraoDescricao.NumeroNoFim:
                // "4 ø8.0c/10 C=200 (N3)"
                if (usaQtd) partes.Add(qtd);
                if (usaBitola || usaEsp) partes.Add((usaBitola ? bitola : "") + (usaEsp ? esp : ""));
                if (usaComp) partes.Add(comp);
                if (usaN) partes.Add("(" + n + ")");
                break;

            default:
                // "4 N3 ø8.0 c/10 C=200"
                if (usaQtd) partes.Add(qtd);
                if (usaN) partes.Add(n);
                if (usaBitola) partes.Add(bitola);
                if (usaEsp) partes.Add(esp);
                if (usaComp) partes.Add(comp);
                break;
        }

        string texto = string.Join(" ", partes);
        // sem a quantidade no texto, a repetição vai na frente: "2x N1 ø10.0"
        if (!usaQtd && rep.Length > 0) texto = rep + " " + texto;
        return texto;
    }

    /// <summary>Prefixo das repetições ("2x"); vazio sem repetição.</summary>
    public static string Repeticao(int repeticoes) =>
        repeticoes > 1 ? repeticoes.ToString(CultureInfo.InvariantCulture) + "x" : "";

    /// <summary>Lados da tela em cm, menor primeiro e arredondados a 1 casa.</summary>
    private static (double menor, double maior) Lados(double lado1Cm, double lado2Cm)
    {
        double a = System.Math.Round(lado1Cm, 1), b = System.Math.Round(lado2Cm, 1);
        return a <= b ? (a, b) : (b, a);
    }

    /// <summary>
    /// Tela identificada: "T1 - 2x Q-92" e "245 x 348" (menor × maior, cm) em duas linhas do MText.
    /// O "2x" é a quantidade de painéis (quantidade × repetições, ex.: 2 camadas); 1 painel não leva prefixo.
    /// </summary>
    public static (string linha1, string linha2) TextoTela(DadosArmadura d, double lado1Cm, double lado2Cm)
    {
        int n = System.Math.Max(1, d.Quantidade) * System.Math.Max(1, d.Repeticoes);
        string t = "T" + (d.NumeroTela > 0 ? d.NumeroTela.ToString(CultureInfo.InvariantCulture) : "?");
        string linha1 = t + " - " + (n > 1 ? n.ToString(CultureInfo.InvariantCulture) + "x " : "") + d.Tela.Trim();
        var (a, b) = Lados(lado1Cm, lado2Cm);
        return (linha1, CalculoComprimento.NumeroCompacto(a) + " x " + CalculoComprimento.NumeroCompacto(b));
    }

    /// <summary>Painéis iguais (modelo, lados e quantidade) recebem o mesmo T.</summary>
    public static string AssinaturaTela(DadosArmadura d, double lado1Cm, double lado2Cm)
    {
        var (a, b) = Lados(lado1Cm, lado2Cm);
        int n = System.Math.Max(1, d.Quantidade) * System.Math.Max(1, d.Repeticoes);
        return System.FormattableString.Invariant($"{d.Tela.Trim().ToUpperInvariant()}|{a:0.0}|{b:0.0}|{n}");
    }
}
