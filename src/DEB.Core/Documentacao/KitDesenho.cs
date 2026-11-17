using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Motor;

namespace DEB.Core.Documentacao;

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
        // viga protendida (escritório A, folha 1071): taxas de armadura passiva e ativa por m³ de concreto
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

    /// <summary>
    /// O último estribo de uma zona (posições de <see cref="Modelo.CalculoFaixa"/>) cai exatamente na divisa com a zona seguinte,
    /// onde a seguinte também põe o seu primeiro: o estribo sairia repetido. Acontece sem espaçamento inicial quando o trecho é
    /// múltiplo do espaçamento (⌊L/s⌋ + 1) ou sempre no modo ⌈L/s⌉ + 1. Unidade livre (a mesma em comprimento e espaçamento).
    /// </summary>
    public static bool RepeteNaDivisa(double comprimento, double esp, Configuracao.ConfiguracaoArmadura cfg)
    {
        double tol = 1e-6 * Math.Max(1, comprimento);
        int n = Modelo.CalculoFaixa.Quantidade(comprimento, esp, cfg.EspacamentoInicialFaixa, cfg.ModoQuantidadeFaixa);
        if (n < 2) return false;
        var p = Modelo.CalculoFaixa.Posicoes(comprimento, n, esp, cfg.EspacamentoInicialFaixa);
        return p[0] < tol && comprimento - p[n - 1] < tol;
    }

    /// <summary>
    /// Estribos de uma zona sem repetir na divisa (decisão do dono, D30, 16/11/2026): a divisa pertence à zona seguinte, então uma
    /// zona que não é a última perde o estribo que cairia nela. Usado pelo pilar (template do CAD e modelo 3D do Revit) e pela
    /// viga pré-moldada.
    /// </summary>
    public static int EstribosNaZona(double comprimento, double esp, bool ultima, Configuracao.ConfiguracaoArmadura cfg)
    {
        int n = Modelo.CalculoFaixa.Quantidade(comprimento, esp, cfg.EspacamentoInicialFaixa, cfg.ModoQuantidadeFaixa);
        return !ultima && RepeteNaDivisa(comprimento, esp, cfg) ? n - 1 : n;
    }

    /// <summary>
    /// Fim da faixa de uma zona [a, b] no desenho: b, ou um espaçamento antes quando o último estribo repetiria o primeiro da zona
    /// seguinte. A faixa do CAD conta os estribos pelo comprimento desenhado; com ela encurtada, conta <see cref="EstribosNaZona"/>.
    /// </summary>
    public static double FimSemRepetir(double a, double b, double esp, bool ultima, Configuracao.ConfiguracaoArmadura cfg) =>
        !ultima && RepeteNaDivisa(b - a, esp, cfg) ? b - esp : b;

    /// <summary>Quantos elementos o resultado já tem (para medir o que foi desenhado depois).</summary>
    internal static (int b, int c, int t, int ci, int co) Marca(ResultadoTemplate r) =>
        (r.Barras.Count, r.Contornos.Count, r.Textos.Count, r.Circulos.Count, r.Cotas.Count);

    /// <summary>Maior x do que foi desenhado depois da marca (texto: largura aproximada 0,7·h por caractere).</summary>
    internal static double MaxX(ResultadoTemplate r, (int b, int c, int t, int ci, int co) m)
    {
        var xs = r.Barras.Skip(m.b).SelectMany(x => x.Vertices).Select(p => p.X)
            .Concat(r.Contornos.Skip(m.c).SelectMany(x => x.Vertices).Select(p => p.X))
            .Concat(r.Textos.Skip(m.t).Select(x => x.X + (Math.Abs(x.Angulo) < 1e-9 ? (x.Alinhamento == 0 ? 1.0 : x.Alinhamento == 1 ? 0.5 : 0) * 0.7 * x.Altura * x.Texto.Length : x.Altura)))
            .Concat(r.Circulos.Skip(m.ci).Select(x => x.X + x.Raio))
            .Concat(r.Cotas.Skip(m.co).SelectMany(x => new[] { x.P1.X, x.P2.X, x.Posicao.X }));
        return xs.DefaultIfEmpty(0).Max();
    }

    /// <summary>
    /// Estribo fechado (medidas externas w × h) com ganchos de 135° no canto superior esquerdo. Vale para o estribo do pilar, a
    /// costura e o estribo vertical do consolo. Decisão do dono (D30, 16/11/2026): 135° é o padrão do pilar, como no modelo 3D
    /// do Revit — o comprimento sai da mesma função (<see cref="Estribos.GeradorEstribo.FechadoPadrao"/> com gancho de 45°, ponta reta
    /// 5φt ≥ 5 cm, NBR 6118 9.4.6.1) que <see cref="Calculo.ComprimentoPorEixo"/> usa para as barras do Revit.
    /// </summary>
    internal static BarraTemplate EstriboFechado(Configuracao.ConfiguracaoArmadura cfg, string nome, double x, double y, double w, double h, double phi, string aco, double escala)
    {
        var poligono = new List<(double X, double Y)> { (x, y), (x + w, y), (x + w, y + h), (x, y + h) };
        var f = Estribos.GeradorEstribo.FechadoPadrao(poligono, phi, aco, cfg.Dobramento, Modelo.FormatoSegmento.Gancho45);
        var bt = new BarraTemplate { Nome = nome, Bitola = phi, Aco = aco, Estribo = true, Escala = escala, Quantidade = 1 };
        bt.Vertices.AddRange(f.Vertices.Select(p => (p.X, p.Y)));
        bt.Formatos.AddRange(f.Formatos);
        foreach (var s in f.Sobrepostos) bt.Sobrepostos.Add(s);
        foreach (var s in f.Cotados) bt.Cotados.Add(s);
        return bt;
    }

    /// <summary>"12,5" → "12.5", "8.0" → "8" (a lista de bitolas aceita o que vem do Excel).</summary>
    public static string NormalizarBitola(string s)
    {
        s = (s ?? "").Trim().Replace("ø", "").Replace("Ø", "").Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d.ToString("0.##", CultureInfo.InvariantCulture) : s;
    }
}
