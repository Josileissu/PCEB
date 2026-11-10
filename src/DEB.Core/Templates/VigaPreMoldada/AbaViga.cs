using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Estribos;
using DEB.Core.Modelo;
using Ctx = DEB.Core.Templates.PilarPreMoldado.TemplatePilarPreMoldado.Ctx;

namespace DEB.Core.Templates.VigaPreMoldada;

/// <summary>
/// Aba da viga (apoio de laje ou de escada): saliência lateral ao longo de um trecho, de um lado (seção L) ou dos dois (T
/// invertido), no fundo ou elevada (consolo corrido), retangular ou com o FUNDO INCLINADO — mais alta junto da alma, mais fina
/// na ponta, com o topo plano (o formato das vigas do modelo Nordika). "Esquerda" = o lado de cima da planta (y = b) e o lado
/// esquerdo das seções; "direita" = o lado de baixo da planta (y = 0), que é a face de frente da elevação. A largura b da viga
/// continua sendo a da ALMA (como no nome das peças: "19x65 com aba").
/// </summary>
public sealed class AbaViga
{
    public bool Esquerda { get; set; }
    public bool Direita { get; set; }
    /// <summary>Balanço da aba a partir da face da alma (cm).</summary>
    public double Largura { get; set; }
    /// <summary>Altura junto da alma (cm).</summary>
    public double Altura { get; set; }
    /// <summary>Altura na ponta (cm); 0 ou ≥ <see cref="Altura"/> = aba retangular.</summary>
    public double AlturaPonta { get; set; }
    /// <summary>Fundo da aba (junto da alma) acima do fundo da viga (cm); 0 = no fundo (T invertido / L).</summary>
    public double Base { get; set; }
    /// <summary>Trecho com aba, cm a partir da ponta esquerda.</summary>
    public double Ini { get; set; }
    public double Fim { get; set; }

    public bool Existe => (Esquerda || Direita) && Largura > 0 && Altura > 0 && Fim - Ini > 1;
    public int Lados => Existe ? (Esquerda ? 1 : 0) + (Direita ? 1 : 0) : 0;
    public double LargE => Existe && Esquerda ? Largura : 0;
    public double LargD => Existe && Direita ? Largura : 0;
    public double Comprimento => Existe ? Fim - Ini : 0;
    /// <summary>Altura na ponta já resolvida (a da face quando a aba é retangular).</summary>
    public double AltPonta => AlturaPonta > 0 && AlturaPonta < Altura ? AlturaPonta : Altura;
    public bool Inclinada => AltPonta < Altura - 1e-6;
    public double VolumeCm3 => Lados * Largura * (Altura + AltPonta) / 2 * Comprimento;
    /// <summary>O corte em x passa pela aba?</summary>
    public bool Em(double x) => Existe && x >= Ini - 1e-6 && x <= Fim + 1e-6;

    /// <summary>Aba curta = consolo lateral de apoio de outra viga (até este comprimento, cm).</summary>
    public const double ComprimentoConsolo = 60;
    public bool Consolo => Existe && Comprimento <= ComprimentoConsolo + 1e-6;

    /// <summary>
    /// A aba na seção do DENTE (a parte de cima da viga, acima do recorte de altura <paramref name="recorte"/>): a mesma aba com
    /// o fundo medido a partir do fundo do dente — negativo quando a aba desce abaixo do recorte (aba de escada do Nordika, que
    /// passa pelo dente pendurada abaixo dele); null se ela fica INTEIRA abaixo do recorte.
    /// </summary>
    public AbaViga NoDente(double recorte) => !Existe || Base + Altura <= recorte + 1e-6 ? null : new AbaViga
    {
        Esquerda = Esquerda, Direita = Direita, Largura = Largura, Altura = Altura, AlturaPonta = AlturaPonta, Base = Base - recorte, Ini = Ini, Fim = Fim,
    };
}

/// <summary>
/// VP-4, 1ª parte (2026-11-09, proposta — o usuário vai enviar os padrões para ajustar): viga com ABA lateral, seção L (um
/// lado) ou T invertido (dois lados), em parte ou em todo o comprimento, inclusive passando pelo dente quando fica acima do
/// recorte. Forma: a aba na planta, na elevação e nas seções, com as cotas; volume, peso e CG (alças) com a aba. Armadura da
/// aba, tratada como consolo corrido: ESTRIBO DA ABA (fechado, abraçando a alma e a(s) aba(s), com o ramo de baixo acompanhando
/// o fundo inclinado: o ramo de cima é o tirante) em faixa própria e as LONGITUDINAIS DA ABA na ponta. A suspensão da carga da
/// aba fica com os estribos da alma (aviso para conferir). A seção I e a mesa superior ficam para depois.
/// </summary>
public sealed partial class TemplateVigaPreMoldada
{
    public const string AbaDentesAuto = "Automático", AbaDentesPassa = "Passa pelos dentes", AbaDentesPara = "Para nos dentes";
    public const string AbaSem = "Sem aba", AbaEsq = "Esquerda (L)", AbaDir = "Direita (L)", AbaDois = "Dois lados (T invertido)";
    private const string G10 = "10. Aba (apoio de laje)";

    private static IEnumerable<ParametroTemplate> ParametrosAba() => new[]
    {
        P(G10, "Aba", "Aba", AbaSem, tipo: TipoParametro.Opcao, opcoes: new[] { AbaSem, AbaEsq, AbaDir, AbaDois },
          descricao: "Esquerda = lado de cima da planta e lado esquerdo das seções; direita = lado de baixo da planta (a frente da elevação). " +
                     "A largura b da viga é a da alma."),
        P(G10, "AbaLargura", "Aba: balanço (a partir da alma)", "15", "cm", min: 0),
        P(G10, "AbaAltura", "Aba: altura junto da alma", "20", "cm", min: 0),
        P(G10, "AbaAlturaPonta", "Aba: altura na ponta (0 = igual: aba retangular)", "0", "cm", min: 0,
          descricao: "Menor que a altura junto da alma = fundo inclinado (o topo da aba é plano)."),
        P(G10, "AbaBase", "Aba: fundo acima do fundo da viga (0 = no fundo)", "0", "cm", min: 0),
        P(G10, "AbaDentes", "Aba nos dentes Gerber", AbaDentesAuto, tipo: TipoParametro.Opcao, opcoes: new[] { AbaDentesAuto, AbaDentesPassa, AbaDentesPara },
          descricao: "Automático = passa pelo dente a aba que fica toda acima do recorte e para na face do dente a que desce abaixo dele. " +
                     "\"Passa\" com a aba descendo abaixo do recorte = ela fica pendurada abaixo do dente (aba de escada)."),
        P(G10, "AbaIni", "Aba: início (0 = pela opção dos dentes)", "0", "cm", min: 0, descricao: "Distância da ponta esquerda."),
        P(G10, "AbaFim", "Aba: fim (0 = pela opção dos dentes)", "0", "cm", min: 0, descricao: "Distância da ponta esquerda."),
        P(G10, "AbaEstBitola", "Estribo da aba: bitola", "8", "mm", TipoParametro.Bitola, 5, 16,
          descricao: "Estribo fechado que abraça a alma e a(s) aba(s); o ramo de cima é o tirante da aba (consolo corrido)."),
        P(G10, "AbaEstEsp", "Estribo da aba: espaçamento", "15", "cm", min: 2),
        P(G10, "AbaLongQtd", "Longitudinais na ponta de cada aba", "2", tipo: TipoParametro.Inteiro, min: 0, max: 6),
        P(G10, "AbaLongBitola", "Longitudinais da aba: bitola", "10", "mm", TipoParametro.Bitola, 5, 25),
    };

    // ======================== leitura e cálculos (testáveis) ========================

    /// <summary>
    /// Lê a aba; trecho padrão = entre as faces dos dentes (ou de ponta a ponta; a aba acima do recorte passa pelo dente).
    /// Aba inválida = sem aba, com aviso.
    /// </summary>
    public static AbaViga LerAba(ValoresTemplate v, double h, double L, ExtremidadeViga e, ExtremidadeViga d, List<string> avisos)
    {
        string tipo = v["Aba"];
        var a = new AbaViga
        {
            Esquerda = tipo == AbaEsq || tipo == AbaDois, Direita = tipo == AbaDir || tipo == AbaDois,
            Largura = v.Numero("AbaLargura"), Altura = v.Numero("AbaAltura"), AlturaPonta = Math.Max(0, v.Numero("AbaAlturaPonta")),
            Base = Math.Max(0, v.Numero("AbaBase")),
        };
        if (!a.Esquerda && !a.Direita) return a;
        // automático: o dente só interrompe a aba que fica (em parte) abaixo do recorte dele; "passa"/"para" mandam
        string nosDentes = v["AbaDentes"];
        bool Corta(ExtremidadeViga x) => x.TemDente && (nosDentes == AbaDentesPara || (nosDentes != AbaDentesPassa && a.Base < x.Recorte(h) - 1e-6));
        bool cortaE = Corta(e), cortaD = Corta(d);
        double i0 = cortaE ? e.CompDente : 0, f0 = L - (cortaD ? d.CompDente : 0);
        a.Ini = v.Numero("AbaIni") > 0 ? v.Numero("AbaIni") : i0;
        a.Fim = v.Numero("AbaFim") > 0 ? Math.Min(L, v.Numero("AbaFim")) : f0;
        if (a.Largura <= 0 || a.Altura <= 0) { avisos?.Add("Aba: informe o balanço e a altura; viga desenhada sem aba."); a.Esquerda = a.Direita = false; return a; }
        if (a.Fim - a.Ini <= 1) { avisos?.Add($"Aba: trecho de {Num(a.Ini, "0.#")} a {Num(a.Fim, "0.#")} cm inválido; viga desenhada sem aba."); a.Esquerda = a.Direita = false; return a; }
        if (a.Base + a.Altura > h + 1e-6)
        {
            avisos?.Add($"Aba: fundo {Num(a.Base, "0.#")} + altura {Num(a.Altura, "0.#")} cm passa do topo da viga (h = {Num(h, "0.#")}); a altura foi limitada.");
            a.Altura = Math.Max(1, h - a.Base);
        }
        bool Abaixo(ExtremidadeViga x) => x.TemDente && a.Base + a.Altura <= x.Recorte(h) + 1e-6;   // inteira abaixo do recorte
        if ((a.Ini < e.CompDente - 1e-6 && Abaixo(e)) || (a.Fim > L - d.CompDente + 1e-6 && Abaixo(d)))
            avisos?.Add("Aba: o trecho entra no dente Gerber, mas a aba fica toda abaixo do recorte — a seção do dente é desenhada sem aba; conferir.");
        else if ((e.TemDente && a.Ini < e.CompDente - 1e-6 && a.Base < e.Recorte(h) - 1e-6) || (d.TemDente && a.Fim > L - d.CompDente + 1e-6 && a.Base < d.Recorte(h) - 1e-6))
            avisos?.Add("Aba: passa pelo dente Gerber descendo abaixo do recorte (fica pendurada abaixo do dente) — conferir.");
        if (a.Consolo)
            avisos?.Add($"Aba curta ({Num(a.Comprimento, "0.#")} cm): consolo lateral de apoio de outra viga, armado como a aba (estribos fechados = tirante) — conferir a armadura do consolo.");
        if (a.Lados == 1)
            avisos?.Add("Aba de um lado só: o CG fica fora do eixo da alma — conferir o içamento (giro da peça) e a torção.");
        avisos?.Add("Aba: a carga apoiada na aba é suspensa pelos estribos da alma — conferir a armadura de suspensão no trecho da aba.");
        return a;
    }

    /// <summary>CG ao longo da viga (cm da ponta esquerda) com os recortes dos dentes e a aba.</summary>
    public static double CentroDeGravidade(double b, double h, double L, ExtremidadeViga e, ExtremidadeViga d, AbaViga a)
    {
        double vAlma = Volume(b, h, L, e, d) * 1e6, xAlma = CentroDeGravidade(b, h, L, e, d);
        if (a == null || !a.Existe) return xAlma;
        return (vAlma * xAlma + a.VolumeCm3 * (a.Ini + a.Fim) / 2) / (vAlma + a.VolumeCm3);
    }

    /// <summary>Tira vértices repetidos e os do meio de três colineares (polígono fechado).</summary>
    private static List<(double X, double Y)> SemColineares(List<(double X, double Y)> p)
    {
        var r = new List<(double X, double Y)>();
        foreach (var q in p) if (r.Count == 0 || Math.Abs(r[r.Count - 1].X - q.X) > 1e-9 || Math.Abs(r[r.Count - 1].Y - q.Y) > 1e-9) r.Add(q);
        if (r.Count > 1 && Math.Abs(r[r.Count - 1].X - r[0].X) < 1e-9 && Math.Abs(r[r.Count - 1].Y - r[0].Y) < 1e-9) r.RemoveAt(r.Count - 1);
        for (int i = 0; i < r.Count && r.Count > 3; i++)
        {
            var (ax, ay) = r[(i + r.Count - 1) % r.Count];
            var (bx, by) = r[i];
            var (cx, cy) = r[(i + 1) % r.Count];
            if (Math.Abs((bx - ax) * (cy - ay) - (by - ay) * (cx - ax)) < 1e-9) { r.RemoveAt(i); i--; }
        }
        return r;
    }

    /// <summary>
    /// Contorno da seção (sentido anti-horário, a partir do canto inferior esquerdo): a alma b × h com o canto inferior esquerdo
    /// em (x0, y0) e a(s) aba(s) para fora, com o fundo inclinado se houver. <paramref name="a"/> null ou sem aba = retângulo.
    /// </summary>
    public static List<(double X, double Y)> ContornoSecao(double b, double h, AbaViga a, double x0, double y0)
    {
        double le = a?.LargE ?? 0, ld = a?.LargD ?? 0, z0 = y0 + (a?.Base ?? 0), z1 = z0 + (a?.Altura ?? 0), zp = z1 - (a?.AltPonta ?? 0);
        var p = new List<(double X, double Y)> { (x0, y0), (x0 + b, y0) };
        if (ld > 0) p.AddRange(new[] { (x0 + b, z0), (x0 + b + ld, zp), (x0 + b + ld, z1), (x0 + b, z1) });
        p.Add((x0 + b, y0 + h));
        p.Add((x0, y0 + h));
        if (le > 0) p.AddRange(new[] { (x0, z1), (x0 - le, z1), (x0 - le, zp), (x0, z0) });
        return SemColineares(p);
    }

    /// <summary>Posições (eixo, cm da ponta esquerda) dos estribos da aba, pela regra de faixas do plugin.</summary>
    public static List<double> PosicoesEstribosAba(ValoresTemplate v, ConfiguracaoArmadura cfg, AbaViga a)
    {
        if (a == null || !a.Existe) return new List<double>();
        double cob = v.Numero("CobPontas"), phi = v.Numero("AbaEstBitola") / 10, esp = Math.Max(2, v.Numero("AbaEstEsp"));
        double x0 = a.Ini + cob + phi / 2, x1 = a.Fim - cob - phi / 2;
        if (x1 - x0 < 1) return new List<double>();
        int n = CalculoFaixa.Quantidade(x1 - x0, esp, cfg.EspacamentoInicialFaixa, cfg.ModoQuantidadeFaixa);
        return CalculoFaixa.Posicoes(x1 - x0, n, esp, cfg.EspacamentoInicialFaixa).Select(s => x0 + s).ToList();
    }

    /// <summary>
    /// Estribo da aba (eixo) na seção com o canto inferior esquerdo da alma em (sx0, sy0): fechado, com o ramo de cima reto (o
    /// tirante), descendo pela ponta de cada aba (ou pela face da alma, do lado sem aba) e o ramo de baixo reto sob a alma e
    /// inclinado sob a aba. Anti-horário a partir do canto inferior esquerdo; null se não cabe.
    /// </summary>
    public static List<(double X, double Y)> PoligonoEstriboAba(ValoresTemplate v, AbaViga a, double b, double sx0, double sy0)
    {
        double cobL = v.Numero("CobLateral"), cobF = v.Numero("CobFundo"), cobT = v.Numero("CobTopo"), t = v.Numero("AbaEstBitola") / 20;
        double xL = sx0 - a.LargE + cobL + t, xR = sx0 + b + a.LargD - cobL - t;
        double yTop = sy0 + a.Base + a.Altura - cobT - t, yFace = sy0 + a.Base + cobF + t, yPonta = sy0 + a.Base + a.Altura - a.AltPonta + cobF + t;
        if (xR - xL < 2 || yTop - yPonta < 2) return null;
        var p = new List<(double X, double Y)>();
        if (a.LargE > 0) { p.Add((xL, yPonta)); p.Add((sx0, yFace)); } else p.Add((xL, yFace));
        if (a.LargD > 0) { p.Add((sx0 + b, yFace)); p.Add((xR, yPonta)); } else p.Add((xR, yFace));
        p.Add((xR, yTop));
        p.Add((xL, yTop));
        return SemColineares(p);
    }

    // ======================== desenho ========================

    /// <summary>A(s) aba(s) na planta, ao lado da alma (que vai de yP a yP + b).</summary>
    private static void AbaEmPlanta(Ctx c, AbaViga a, double b, double L, double yP)
    {
        if (!a.Existe) return;
        if (a.Esquerda) c.Retangulo(a.Ini, yP + b, a.Fim, yP + b + a.Largura);
        if (a.Direita) c.Retangulo(a.Ini, yP - a.Largura, a.Fim, yP);
        // balanço da aba cotado na ponta do trecho (fora da peça quando a aba vai até a ponta)
        double xc = a.Fim + 2.5 * c.hV;
        if (a.Esquerda) c.Cota(a.Fim, yP + b, a.Fim, yP + b + a.Largura, xc, yP + b + a.Largura / 2, Math.PI / 2, c.escV);
        if (a.Direita) c.Cota(a.Fim, yP - a.Largura, a.Fim, yP, xc, yP - a.Largura / 2, Math.PI / 2, c.escV);
    }

    /// <summary>
    /// A aba na elevação (fundo da viga em <paramref name="yB"/>): a elevação olha a face da direita — aba desse lado em linha
    /// cheia; só do outro lado, oculta. Linhas: o topo, o fundo da ponta (aba inclinada) e o fundo junto da alma (aba elevada ou
    /// inclinada). <paramref name="cotar"/>: fundo e altura da aba numa cota junto do início do trecho.
    /// </summary>
    private static void AbaEmElevacao(Ctx c, AbaViga a, double L, double yB, bool cotar)
    {
        if (!a.Existe) return;
        string layer = a.Direita ? "ARM-FORMA" : "ARM-FURO-OCULTO";
        double z0 = yB + a.Base, z1 = z0 + a.Altura, zp = z1 - a.AltPonta;
        c.Linha(a.Ini, z1, a.Fim, z1, layer);
        if (a.Inclinada) c.Linha(a.Ini, zp, a.Fim, zp, layer);
        if (a.Base > 1e-6) c.Linha(a.Ini, z0, a.Fim, z0, layer);
        if (a.Ini > 1e-6) c.Linha(a.Ini, z0, a.Ini, z1, layer);
        if (a.Fim < L - 1e-6) c.Linha(a.Fim, z0, a.Fim, z1, layer);
        if (!cotar) return;
        double xr = a.Ini + Math.Min(40, a.Comprimento / 4), xc = xr + 2.5 * c.hV;
        if (a.Base > 1e-6) c.Cota(xr, yB, xr, z0, xc, (yB + z0) / 2, Math.PI / 2, c.escV);
        c.Cota(xr, z0, xr, z1, xc, (z0 + z1) / 2, Math.PI / 2, c.escV);
    }

    /// <summary>
    /// Cotas da aba na seção de forma (fundo da parte cortada em <paramref name="sy0"/>): balanço(s) na linha
    /// <paramref name="yCota"/>, a altura da ponta junto da aba e, uma linha para fora, a altura junto da alma (aba inclinada) e o fundo.
    /// </summary>
    private static void CotasAbaNaSecao(Ctx c, AbaViga a, double b, double sx0, double sy0, double yCota)
    {
        double hc = c.hC, esc = c.escC, z0 = sy0 + a.Base, z1 = z0 + a.Altura, zp = z1 - a.AltPonta;
        if (a.Esquerda) c.Cota(sx0 - a.Largura, sy0, sx0, sy0, sx0 - a.Largura / 2, yCota, 0, esc);
        if (a.Direita) c.Cota(sx0 + b, sy0, sx0 + b + a.Largura, sy0, sx0 + b + a.Largura / 2, yCota, 0, esc);
        // do lado esquerdo se houver aba lá (o direito tem a cota da altura da viga)
        double s = a.Esquerda ? -1 : 1, xr = a.Esquerda ? sx0 - a.Largura : sx0 + b + a.Largura, xc = xr + s * 3 * hc;
        c.Cota(xr, zp, xr, z1, xc, (zp + z1) / 2, Math.PI / 2, esc);
        double xc2 = a.Inclinada ? xc + s * 3 * hc : xc;
        if (a.Inclinada) c.Cota(xr, z0, xr, z1, xc2, (z0 + z1) / 2, Math.PI / 2, esc);
        if (a.Base > 1e-6) c.Cota(xr, sy0, xr, z0, xc2, (sy0 + z0) / 2, Math.PI / 2, esc);
    }

    /// <summary>Armadura da aba na SEÇÃO - ARMADURA: o estribo da aba e as longitudinais na ponta, com uma indicação de cada.</summary>
    private static void AbaNaSecaoArmadura(Ctx c, ValoresTemplate v, AbaViga a, double b, double sx0, double sy0)
    {
        double hc = c.hC;
        var pol = PoligonoEstriboAba(v, a, b, sx0, sy0);
        if (pol == null) return;
        c.R.Contornos.Add(new ContornoTemplate { Vertices = pol, Fechado = true, Layer = "ARM-BARRA-DETALHE" });
        double x1 = pol.Min(q => q.X), x2 = pol.Max(q => q.X), y2 = pol.Max(q => q.Y);
        // as indicações saem para o lado da aba (para a esquerda se houver aba lá)
        bool esq = a.Esquerda;
        double xp = esq ? x1 : x2, yp = pol.Where(q => Math.Abs(q.X - xp) < 1e-6).Min(q => q.Y);   // ponta: ramo vertical de yp a y2
        double xt = esq ? sx0 - a.Largura - 4 * hc : sx0 + b + a.Largura + 4 * hc, ym = (yp + y2) / 2;
        c.R.Faixas.Add(new FaixaTemplate
        {
            Barra = "abaestribo", Tipo = TipoFaixa.Indicacao, Referencias = { (xp, ym) }, Posicao = (xt, ym - 1.2 * hc), Escala = c.escC,
        });
        int q0 = Math.Max(0, v.Inteiro("AbaLongQtd"));
        if (q0 == 0) return;
        double phi = v.Numero("AbaLongBitola"), rr = phi / 20, t = v.Numero("AbaEstBitola") / 20;
        var pts = new List<(double X, double Y)>();
        foreach (bool lado in new[] { true, false })
        {
            if (lado ? !a.Esquerda : !a.Direita) continue;
            double xx = lado ? x1 + t + rr : x2 - t - rr, ya = yp + t + rr, yb = y2 - t - rr;
            for (int i = 0; i < q0; i++) pts.Add((xx, q0 == 1 ? yb : ya + i * (yb - ya) / (q0 - 1)));
        }
        foreach (var (px, py) in pts) c.R.Circulos.Add(new CirculoTemplate { X = px, Y = py, Raio = rr, Layer = "ARM-BARRA-SECAO" });
        c.R.Faixas.Add(new FaixaTemplate
        {
            Barra = "abalong", Tipo = TipoFaixa.Indicacao, Referencias = pts.Where(p => esq ? p.X < sx0 : p.X > sx0 + b).ToList(),
            Posicao = (xt, ym + 1.2 * hc), Escala = c.escC,
        });
    }

    /// <summary>
    /// Aba nas ARMADURAS EM ELEVAÇÃO: os estribos da aba dentro da forma (traços na altura da aba) e a faixa deles em cota, na
    /// linha <paramref name="yFaixa"/>; devolve se desenhou a faixa.
    /// </summary>
    private static bool AbaEstribosEmElevacao(Ctx c, ValoresTemplate v, ConfiguracaoArmadura cfg, AbaViga a, double yB, double yFaixa)
    {
        var xs = PosicoesEstribosAba(v, cfg, a);
        if (xs.Count == 0) return false;
        double cobF = v.Numero("CobFundo"), cobT = v.Numero("CobTopo");
        foreach (double x in xs) c.Linha(x, yB + a.Base + cobF, x, yB + a.Base + a.Altura - cobT, "ARM-BARRA-DETALHE");
        double cob = v.Numero("CobPontas"), phi = v.Numero("AbaEstBitola") / 10, x0 = a.Ini + cob + phi / 2, x1 = a.Fim - cob - phi / 2;
        c.R.Faixas.Add(new FaixaTemplate
        {
            Barra = "abaestribo", Tipo = TipoFaixa.Ortogonal, P1 = (x0, yFaixa), P2 = (x1, yFaixa), Posicao = ((x0 + x1) / 2, yFaixa + 0.8 * c.hV),
            Espacamento = Math.Max(2, v.Numero("AbaEstEsp")), Escala = c.escV,
        });
        return true;
    }

    /// <summary>As longitudinais da aba numa linha fora da forma (todas as abas somadas), com o rótulo embaixo.</summary>
    private static bool AbaLongitudinal(Ctx c, ValoresTemplate v, AbaViga a, double y)
    {
        int q = Math.Max(0, v.Inteiro("AbaLongQtd")) * a.Lados;
        if (q == 0) return false;
        double cob = v.Numero("CobPontas"), x0 = a.Ini + cob, x1 = a.Fim - cob, hv = c.hV;
        if (x1 - x0 < 10) return false;
        c.R.Barras.Add(new BarraTemplate
        {
            Nome = "abalong", Bitola = v.Numero("AbaLongBitola"), Aco = v["LongAco"], Quantidade = q, Escala = c.escV,
            Vertices = { (x0, y), (x1, y) }, Formatos = { FormatoSegmento.Linha }, PosicaoDescricao = ((x0 + x1) / 2, y + 2.6 * hv),
        });
        c.Texto(string.Join(" / ", a.Consolo ? c.Linhas("ARMADURA CONSOLO", "CORBEL REINFORCEMENT") : c.Linhas("ARMADURA ABA LAJE", "SLAB LEDGE REINFORCEMENT")),
            (x0 + x1) / 2, y - 3.4 * hv, 0.75 * hv, 1);
        return true;
    }

    /// <summary>Forma do estribo da aba (fechado, com ganchos) para a relação do aço; o canto inferior esquerdo em (x, y).</summary>
    private static BarraTemplate FormaEstriboAba(ValoresTemplate v, ConfiguracaoArmadura cfg, AbaViga a, double b, double x, double y)
    {
        var pol = PoligonoEstriboAba(v, a, b, 0, 0);
        if (pol == null) return null;
        double mx = pol.Min(q => q.X), my = pol.Min(q => q.Y), phi = v.Numero("AbaEstBitola");
        var f = GeradorEstribo.FechadoPadrao(pol.Select(q => (q.X - mx + x, q.Y - my + y)).ToList(), phi, v["EstAco"], cfg.Dobramento);
        var bt = new BarraTemplate { Nome = "abaestribo", Bitola = phi, Aco = v["EstAco"], Estribo = true, Escala = 0, Quantidade = 1 };
        bt.Vertices.AddRange(f.Vertices.Select(p => (p.X, p.Y)));
        bt.Formatos.AddRange(f.Formatos);
        foreach (var s in f.Sobrepostos) bt.Sobrepostos.Add(s);
        foreach (var s in f.Cotados) bt.Cotados.Add(s);
        return bt;
    }
}
