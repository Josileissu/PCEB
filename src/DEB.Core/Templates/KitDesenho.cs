using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DEB.Core.Templates;

// =====================================================================================================================
// Kit de desenho comum dos templates (2026-11-10, reuso): o contexto de desenho (textos, linhas, cotas, marcas de corte) e as
// funções que o pilar e a viga pré-moldados compartilham. Nasceram dentro do template do pilar e a viga os usava de lá —
// doze arquivos dependiam de partes internas do pilar. Agora ficam aqui; o pilar mantém atalhos com os mesmos nomes.
// A regressão visual (tests/…/RegressaoVisualTests) confirma que mover não mudou nenhum desenho.
// =====================================================================================================================

/// <summary>Contexto de desenho de um template: textos, linhas, cotas e marcas de corte, nas escalas das vistas e dos cortes.</summary>
internal sealed class Ctx
{
    public ResultadoTemplate R;
    public IdiomaDesenho Idioma;
    public double hV, hC; // altura dos textos (cm) nas escalas das vistas e dos cortes
    public double escV, escC;
    /// <summary>x depois das faixas da elevação das armaduras (as linhas de nível vão até ali).</summary>
    public double XFimFaixas;

    /// <summary>Linhas do texto conforme o idioma: PT, EN, ou PT e "(EN)" embaixo.</summary>
    public string[] Linhas(string pt, string en) => Idioma switch
    {
        IdiomaDesenho.PT => new[] { pt },
        IdiomaDesenho.EN => new[] { en.Length > 0 ? en : pt },
        _ => en.Length > 0 && en != pt ? new[] { pt, $"({en})" } : new[] { pt },
    };

    public void Texto(string s, double x, double y, double h, int alinhamento = 0, double angulo = 0, string layer = "ARM-TEXTO-FORMA") =>
        R.Textos.Add(new TextoTemplate { Texto = s, X = x, Y = y, Altura = h, Alinhamento = alinhamento, Angulo = angulo, Layer = layer });

    /// <summary>Bloco de linhas (de cima para baixo) a partir de y; devolve o y abaixo da última linha.</summary>
    public double Bloco(IEnumerable<string> linhas, double x, double y, double h, int alinhamento = 0, double entre = 1.6)
    {
        foreach (var l in linhas) { Texto(l, x, y, h, alinhamento); y -= entre * h; }
        return y;
    }

    public void Linha(double x1, double y1, double x2, double y2, string layer = "ARM-FORMA") =>
        R.Contornos.Add(new ContornoTemplate { Vertices = { (x1, y1), (x2, y2) }, Layer = layer });

    public void Retangulo(double x1, double y1, double x2, double y2, string layer = "ARM-FORMA") =>
        R.Contornos.Add(new ContornoTemplate { Vertices = { (x1, y1), (x2, y1), (x2, y2), (x1, y2) }, Fechado = true, Layer = layer });

    public void Cota(double x1, double y1, double x2, double y2, double px, double py, double angulo, double escala, string texto = "") =>
        R.Cotas.Add(new CotaTemplate { P1 = (x1, y1), P2 = (x2, y2), Posicao = (px, py), Angulo = angulo, Escala = escala, Texto = texto });

    /// <summary>
    /// Marca de corte do escritório (P56): triângulo vazado apontando para baixo, com a ponta pouco acima da altura do corte,
    /// e a letra/número em cima. <paramref name="h"/> = altura de texto da escala.
    /// </summary>
    public void MarcaCorte(string id, double x, double y, double h)
    {
        double w = 1.05 * h, topo = y + 1.0 * h, ponta = y + 0.47 * h;
        R.Contornos.Add(new ContornoTemplate { Vertices = { (x - w, topo), (x + w, topo), (x, ponta) }, Fechado = true, Layer = "ARM-CORTE" });
        Texto(id, x, topo + 0.35 * h, h, 1, 0, "ARM-CORTE");
    }
}

/// <summary>Funções comuns aos templates de peças pré-moldadas (formatação dos textos da prancha, idioma, quadro de produção, alças).</summary>
public static class KitDesenho
{
    /// <summary>Número para os textos da prancha (vírgula decimal).</summary>
    public static string Num(double v, string formato) => Fmt.Virgula(v, formato);

    public static IdiomaDesenho LerIdioma(string s) =>
        s == "PT" ? IdiomaDesenho.PT : s == "EN" ? IdiomaDesenho.EN : IdiomaDesenho.PTEN;

    /// <summary>
    /// Quadro "Informações para produção" (modelo do usuário, 2026-10-29): tabela de 2 colunas — rótulo (PT e "(EN)")
    /// à esquerda e valor com unidade à direita. A linha da montagem só entra com fcj de montagem &gt; 0.
    /// </summary>
    public static TabelaTemplate TabelaProducao(IdiomaDesenho idioma, double fcjSaque, double fcjMontagem, double fck,
        double volume, double peso, int quantidade, double x, double y, double escala, bool protendida = false,
        double taxaPassiva = 0, double taxaAtiva = 0)
    {
        bool en = idioma == IdiomaDesenho.EN;
        string N(double v, string f) => en ? v.ToString(f, CultureInfo.InvariantCulture) : Num(v, f);
        string R(string pt, string ing) => idioma switch
        {
            IdiomaDesenho.PT => pt,
            IdiomaDesenho.EN => ing,
            _ => pt + TabelaTemplate.Quebra + "(" + ing.TrimEnd(':') + ")",
        };
        var t = new TabelaTemplate
        {
            Titulo = R("INFORMAÇÕES PARA PRODUÇÃO", "MANUFACTURING INFORMATION"),
            X = x, Y = y, Escala = escala,
        };
        t.Larguras.AddRange(new[] { 24.0, 11.0 });
        t.Alinhamentos.AddRange(new[] { 0, 2 });
        t.Linhas.Add(protendida
            ? new[] { R("RESISTÊNCIA ATO DA PROT.:", "STRENGTH AT PRESTRESS RELEASE:"), $"fcj = {N(fcjSaque, "0.#")} MPa" }
            : new[] { R("RESISTÊNCIA AO SAQUE:", "STRENGTH DEMOLDING:"), $"fcj = {N(fcjSaque, "0.#")} MPa" });
        if (fcjMontagem > 0)
            t.Linhas.Add(new[] { R("RESISTÊNCIA NA MONTAGEM:", "STRENGTH ERECTION:"), $"fcj = {N(fcjMontagem, "0.#")} MPa" });
        t.Linhas.Add(new[] { R("RESISTÊNCIA 28 DIAS:", "STRENGTH AT 28 DAYS:"), $"fck = {N(fck, "0.#")} MPa" });
        t.Linhas.Add(new[] { R("VOLUME DE CONCRETO:", "CONCRETE VOLUME:"), $"{N(volume, "0.000")} m³" });
        t.Linhas.Add(new[] { R("PESO:", "WEIGHT:"), $"{N(peso, "0.000")} ton." });
        t.Linhas.Add(new[] { R("QUANTIDADE:", "QUANTITY:"), $"{quantidade} un." });
        // viga protendida (BPM 1071): taxas de armadura passiva e ativa por m³ de concreto
        if (taxaPassiva > 0) t.Linhas.Add(new[] { R("TAXA DE ARM. PASSIVA:", "PASSIVE REINF. RATIO:"), $"{N(taxaPassiva, "0.0")} kg/m³" });
        if (taxaAtiva > 0) t.Linhas.Add(new[] { R("TAXA DE ARM. ATIVA:", "ACTIVE REINF. RATIO:"), $"{N(taxaAtiva, "0.0")} kg/m³" });
        return t;
    }

    /// <summary>
    /// Alturas das alças (cm da base): simétricas ao CG, a (50 − pct) % do comprimento dele (regra do escritório:
    /// CG centralizado e alças a ~20 % das pontas). Valores &gt; 0 em z1/z2 mandam.
    /// </summary>
    public static (double Z1, double Z2) PosicaoAlcas(double L, double cg, double pct, double z1 = 0, double z2 = 0)
    {
        double d = (0.5 - pct / 100) * L;
        double a = z1 > 0 ? z1 : cg - d, b = z2 > 0 ? z2 : cg + d;
        a = Math.Max(0.05 * L, Math.Min(a, 0.95 * L));
        b = Math.Max(0.05 * L, Math.Min(b, 0.95 * L));
        return (Math.Round(a, 1), Math.Round(b, 1));
    }

    /// <summary>"12,5" → "12.5", "8.0" → "8" (a lista de bitolas aceita o que vem do Excel).</summary>
    public static string NormalizarBitola(string s)
    {
        s = (s ?? "").Trim().Replace("ø", "").Replace("Ø", "").Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d.ToString("0.##", CultureInfo.InvariantCulture) : s;
    }
}
