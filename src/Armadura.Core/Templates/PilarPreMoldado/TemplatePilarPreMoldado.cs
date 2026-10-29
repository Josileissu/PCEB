using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Armadura.Core.Configuracao;

namespace Armadura.Core.Templates.PilarPreMoldado;

/// <summary>Idioma dos textos do detalhamento (pedido do usuário, 2026-10-29: PT, EN ou bilíngue).</summary>
public enum IdiomaDesenho { PT, EN, PTEN }

/// <summary>Nível marcado nas vistas (cota em metros).</summary>
public sealed class NivelPilar
{
    public string NomePT { get; set; } = "";
    public string NomeEN { get; set; } = "";
    public double Cota { get; set; }
}

/// <summary>
/// Pilar pré-moldado PP-1 no padrão do escritório (P61/PP127; requisitos em docs/PILAR_PREMOLDADO_REQUISITOS.md).
/// Etapa 1 (2026-10-29): forma — 4 vistas com níveis, cotas e embutimento (chaves de cisalhamento), corte A-A,
/// notas (cobrimentos por face, tolerâncias da NBR 9062) e informações para produção (volume e peso).
/// Tudo em verdadeira grandeza (cm); a escala de cada região só define a altura dos textos e das cotas, como no
/// DWG do P61 (a folha é montada com viewports). Origem: nome dos níveis à esquerda, na cota da base do pilar.
/// Faces: seção b (x) × h (y); face 1 = +x, 2 = −y, 3 = −x, 4 = +y — a VISTA n olha a face n.
/// </summary>
public sealed partial class TemplatePilarPreMoldado : ITemplateArmadura
{
    public string Id => "pilar-premoldado";
    public string Nome => "Pilar pré-moldado (PP-1)";
    public CategoriaElemento Categoria => CategoriaElemento.PilarPreMoldado;
    public string Descricao => "Pilar retangular pré-moldado no padrão do escritório: 4 vistas com níveis e cotas, embutimento " +
                               "com chaves, corte A-A, notas e informações para produção. (Armaduras, consolos e içamento: próximas etapas.)";

    public const string Chaves = "Chaves de cisalhamento", Rugosa = "Rugosa", Lisa = "Lisa";

    private static ParametroTemplate P(string grupo, string nome, string rotulo, string padrao, string unidade = "",
        TipoParametro tipo = TipoParametro.Numero, double min = double.NegativeInfinity, double max = double.PositiveInfinity,
        string descricao = "", params string[] opcoes) => new ParametroTemplate
    {
        Grupo = grupo, Nome = nome, Rotulo = rotulo, Padrao = padrao, Unidade = unidade, Tipo = tipo, Minimo = min, Maximo = max,
        Descricao = descricao, Opcoes = opcoes.ToList(),
    };

    // dois dígitos: o PropertyGrid ordena os grupos como texto (com "1." o "10." vinha logo depois do "1.")
    private const string G1 = "01. Identificação", G2 = "02. Geometria", G3 = "03. Níveis", G4 = "04. Embutimento (base)",
        G5 = "05. Cobrimentos", G6 = "06. Concreto e produção";

    /// <summary>Calculado a cada leitura: as opções dos consolos vêm do catálogo atual.</summary>
    public IReadOnlyList<ParametroTemplate> Parametros => ParametrosForma.Concat(ParametrosArmadura).Concat(ParametrosConsolos()).Concat(ParametrosItens).Concat(new[] { ParametroInsertos() }).ToArray();

    private static readonly ParametroTemplate[] ParametrosForma =
    {
        P(G1, "Nome", "Nome do pilar", "P61", tipo: TipoParametro.Texto),
        P(G1, "Quantidade", "Quantidade de peças", "1", tipo: TipoParametro.Inteiro, min: 1),
        P(G1, "Idioma", "Idioma dos textos", "PT/EN", tipo: TipoParametro.Opcao, opcoes: new[] { "PT", "EN", "PT/EN" }),
        P(G1, "EscalaVistas", "Escala das vistas (1:n)", "50", min: 1),
        P(G1, "EscalaCortes", "Escala dos cortes (1:n)", "25", min: 1),

        P(G2, "B", "Seção: b (faces 2 e 4, eixo x)", "50", "cm", min: 10),
        P(G2, "H", "Seção: h (faces 1 e 3, eixo y)", "50", "cm", min: 10),
        P(G2, "L", "Comprimento total", "1390.5", "cm", min: 50),
        P(G2, "Chanfro", "Chanfro dos cantos (cateto)", "0", "cm", min: 0, max: 10, descricao: "0 = cantos vivos."),

        P(G3, "CotaBase", "Cota da base do pilar", "-2.90", "m", descricao: "O topo fica em cota da base + comprimento."),
        new ParametroTemplate
        {
            Grupo = G3, Nome = "Niveis", Rotulo = "Níveis intermediários", Tipo = TipoParametro.Tabela, NomeItem = "nível",
            Padrao = "TÉRREO|GROUND FLOOR|0.00;WAREHOUSE||3.025",
            Descricao = "Clique em … para editar a lista (nome em português, em inglês e a cota em m). O topo e a base são marcados sempre.",
            Colunas =
            {
                new ColunaTabela { Nome = "PT", Rotulo = "Nome (PT)", Largura = 160 },
                new ColunaTabela { Nome = "EN", Rotulo = "Nome (EN)", Largura = 160 },
                new ColunaTabela { Nome = "Cota", Rotulo = "Cota (m)", Tipo = TipoParametro.Numero, Padrao = "0.00", Largura = 90 },
            },
        },

        P(G4, "Lemb", "Comprimento embutido no cálice", "100", "cm", min: 0, descricao: "0 = sem embutimento desenhado."),
        P(G4, "Interface", "Interface com o cálice", Chaves, tipo: TipoParametro.Opcao, opcoes: new[] { Chaves, Rugosa, Lisa }),
        P(G4, "ChavePasso", "Chaves: passo", "5", "cm", min: 1),
        P(G4, "ChaveDente", "Chaves: altura do dente", "4", "cm", min: 0.5),
        P(G4, "ChaveProf", "Chaves: profundidade", "1", "cm", min: 0.5),

        P(G5, "CobFace1", "Face 1", "3.5", "cm", min: 1, max: 15),
        P(G5, "CobFace2", "Face 2", "3.5", "cm", min: 1, max: 15),
        P(G5, "CobFace3", "Face 3", "3.5", "cm", min: 1, max: 15),
        P(G5, "CobFace4", "Face 4", "3.5", "cm", min: 1, max: 15),
        P(G5, "CobTopo", "Topo", "3.5", "cm", min: 1, max: 15),
        P(G5, "CobBase", "Base", "3.5", "cm", min: 1, max: 15),

        P(G6, "Fck", "fck (28 dias)", "40", "MPa", min: 10),
        P(G6, "FcjSaque", "fcj no saque", "30", "MPa", min: 5),
        P(G6, "FcjMontagem", "fcj na montagem", "35", "MPa", min: 5),
        P(G6, "PesoEspecifico", "Peso específico do concreto", "2500", "kgf/m³", min: 1000),
    };

    // ------------------------------------------------------------------

    /// <summary>Volume de concreto (m³) da forma: seção (com chanfros) × comprimento.</summary>
    public static double Volume(double b, double h, double l, double chanfro) =>
        (b * h - 2 * chanfro * chanfro) * l / 1e6;

    /// <summary>Cota de nível no padrão do P61: "+11,005", "3,025" → "+3,025", "0,00", "-2,90".</summary>
    public static string FormatarCota(double m)
    {
        double v = Math.Round(m, 3);
        string s = Math.Abs(v).ToString(v == Math.Round(v, 2) ? "0.00" : "0.000", CultureInfo.InvariantCulture).Replace('.', ',');
        return v > 0 ? "+" + s : v < 0 ? "-" + s : s;
    }

    /// <summary>Número com vírgula decimal (textos em PT).</summary>
    public static string Num(double v, string formato) => v.ToString(formato, CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>Lê a lista de níveis: "PT|EN|cota; …" (janela) ou o formato antigo "PT/EN=cota; …" (cota em m).</summary>
    public static List<NivelPilar> LerNiveis(string texto)
    {
        var r = new List<NivelPilar>();
        if ((texto ?? "").Contains("|"))
        {
            foreach (var l in ValoresTemplate.LerTabela(texto))
            {
                string pt = ValoresTemplate.Celula(l, 0);
                if (pt.Length == 0) continue;
                r.Add(new NivelPilar { NomePT = pt, NomeEN = ValoresTemplate.Celula(l, 1), Cota = ValoresTemplate.NumeroCelula(l, 2) });
            }
            return r;
        }
        foreach (var item in (texto ?? "").Split(';'))
        {
            var partes = item.Split('=');
            if (partes.Length != 2) continue;
            if (!double.TryParse(partes[1].Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var cota)) continue;
            var nomes = partes[0].Split('/');
            var pt = nomes[0].Trim();
            if (pt.Length == 0) continue;
            r.Add(new NivelPilar { NomePT = pt, NomeEN = nomes.Length > 1 ? nomes[1].Trim() : "", Cota = cota });
        }
        return r;
    }

    public static IdiomaDesenho LerIdioma(string s) =>
        s == "PT" ? IdiomaDesenho.PT : s == "EN" ? IdiomaDesenho.EN : IdiomaDesenho.PTEN;

    /// <summary>Tolerância de comprimento da NBR 9062 (tab. 2) para pilares, em mm.</summary>
    public static int ToleranciaComprimento(double lCm) => lCm <= 500 ? 10 : lCm <= 1000 ? 15 : 20;

    private sealed class Ctx
    {
        public ResultadoTemplate R;
        public IdiomaDesenho Idioma;
        public double hV, hC; // altura dos textos (cm) nas escalas das vistas e dos cortes
        public double escV, escC;

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
    }

    public ResultadoTemplate Gerar(ValoresTemplate v, ConfiguracaoArmadura cfg)
    {
        var r = new ResultadoTemplate();
        double escV = v.Numero("EscalaVistas") > 0 ? v.Numero("EscalaVistas") : 50;
        double escC = v.Numero("EscalaCortes") > 0 ? v.Numero("EscalaCortes") : 25;
        double mm = cfg.AlturaTextoMm > 0 ? cfg.AlturaTextoMm : 2;
        var c = new Ctx { R = r, Idioma = LerIdioma(v["Idioma"]), escV = escV, escC = escC, hV = mm / 10 * escV, hC = mm / 10 * escC };
        r.Escala = escV;
        string nome = string.IsNullOrWhiteSpace(v["Nome"]) ? "PILAR" : v["Nome"].Trim();
        r.Detalhamento = nome;

        double b = v.Numero("B"), h = v.Numero("H"), L = v.Numero("L"), ch = Math.Max(0, v.Numero("Chanfro"));
        double cotaBase = v.Numero("CotaBase");
        double cotaTopo = cotaBase + L / 100;
        double hv = c.hV;

        // ---------------- colunas da faixa das vistas ----------------
        double xTotal = 12 * hv, xCadeia = 15 * hv, xMarca = 17 * hv, xVista0 = 26 * hv;
        double passoVista = Math.Max(b, h) + 20 * hv;
        // VISTA n olha a face n: 1 e 3 mostram h; 2 e 4 mostram b
        var larguras = new[] { h, b, h, b };
        var xs = Enumerable.Range(0, 4).Select(i => xVista0 + i * passoVista).ToArray();
        double xFimVistas = xs[3] + larguras[3];

        // título do pilar
        c.Texto(nome, 0, L + 22 * hv, 3 * hv);

        for (int i = 0; i < 4; i++)
        {
            double x0 = xs[i], x1 = xs[i] + larguras[i], xm = (x0 + x1) / 2;
            c.Retangulo(x0, 0, x1, L);
            // chanfros: linhas ao longo do comprimento, a "cateto" das arestas
            if (ch > 0) { c.Linha(x0 + ch, 0, x0 + ch, L); c.Linha(x1 - ch, 0, x1 - ch, L); }
            // título: VISTA n / (VIEW n) / Esc. 1:50
            double y = L + 16 * hv;
            foreach (var t in c.Linhas($"VISTA {i + 1}", $"VIEW {i + 1}")) { c.Texto(t, xm, y, 1.2 * hv, 1); y -= 1.7 * hv; }
            c.Texto($"Esc. 1:{escV:0}", xm, y, 0.7 * hv, 1);
        }

        // ---------------- consolos nas vistas e as marcas dos cortes de forma na VISTA 2 ----------------
        var cob0 = new[] { "CobFace1", "CobFace2", "CobFace3", "CobFace4", "CobTopo", "CobBase" }.Select(k => v.Numero(k)).ToArray();
        var consolos = LerConsolos(v, Catalogo, L, r.Avisos);
        DesenharConsolosNasVistas(c, consolos, xs, larguras, cob0);
        // alças (pelo CG), montagem (furo ou alça de topo) e inserto do topo
        var itens = DesenharItensNasVistas(c, v, consolos, xs, larguras, b, h, L, ch);
        var insertos = LerInsertos(v, CatalogoInsertos, L, r.Avisos);
        DesenharInsertosNasVistas(c, insertos, xs, larguras, L);
        foreach (var co in consolos)
        {
            double yc = co.ZTopo - co.H / 2;
            double extE = co.Faces.Contains(FaceEsquerda(2)) ? co.A : 0, extD = co.Faces.Contains(FaceDireita(2)) ? co.A : 0;
            // além das cotas do consolo (altura a 2 alturas de texto da ponta)
            foreach (var (xa, xb) in new[] { (xs[1] - extE - 7 * hv, xs[1] - extE - 5 * hv), (xs[1] + larguras[1] + extD + 5 * hv, xs[1] + larguras[1] + extD + 7 * hv) })
            {
                c.Linha(xa, yc, xb, yc, "ARM-CORTE");
                c.Texto(co.Letra, (xa + xb) / 2, yc + 0.5 * hv, hv, 1, 0, "ARM-CORTE");
            }
        }

        // ---------------- embutimento (base) ----------------
        double lemb = Math.Max(0, Math.Min(v.Numero("Lemb"), L));
        string interf = v["Interface"];
        if (lemb > 0)
        {
            if (interf == Chaves)
            {
                double passo = Math.Max(1, v.Numero("ChavePasso")), dente = Math.Max(0.5, Math.Min(v.Numero("ChaveDente"), passo));
                // cada passo: dente (face cheia) e o rebaixo; uma linha em cada mudança, nas 4 vistas
                for (int i = 0; i < 4; i++)
                    for (double y = 0; y + passo <= lemb + 1e-6; y += passo)
                    {
                        // arestas do rebaixo (perfil do detalhe 01: trecho de passo − dente que vai à face, rebaixo de "dente")
                        c.Linha(xs[i], y + passo - dente, xs[i] + larguras[i], y + passo - dente);
                        c.Linha(xs[i], y + passo, xs[i] + larguras[i], y + passo);
                    }
            }
            // cota do embutimento ao lado da VISTA 2 (como no P61)
            c.Cota(xs[1], 0, xs[1], lemb, xs[1] - 4 * hv, lemb / 2, Math.PI / 2, escV);
            string nota = interf == Chaves ? "CHAVES DE CISALHAMENTO" : interf == Rugosa ? "SUPERFÍCIE RUGOSA" : "SUPERFÍCIE LISA";
            string notaEn = interf == Chaves ? "SHEAR KEYS" : interf == Rugosa ? "ROUGH SURFACE" : "SMOOTH SURFACE";
            double yN = lemb / 2;
            foreach (var t in c.Linhas(nota, notaEn)) { c.Texto(t, xs[3] + larguras[3] + 2 * hv, yN, 0.8 * hv); yN -= 1.3 * 0.8 * hv; }
            if (lemb < 40) r.Avisos.Add($"Embutimento de {Num(lemb, "0.#")} cm < 40 cm (NBR 9062, 7.7.2.4).");
            double fator = interf == Chaves ? 1.2 : 1.5;
            if (lemb < fator * Math.Max(b, h) - 1e-6)
                r.Avisos.Add($"Embutimento de {Num(lemb, "0.#")} cm menor que {Num(fator, "0.0")}h = {Num(fator * Math.Max(b, h), "0.#")} cm " +
                             $"(mínimo da NBR 9062, tab. 15, para Md/(Nd·h) ≤ 0,15 com interface {interf.ToLowerInvariant()}). Conferir.");
        }

        // ---------------- níveis ----------------
        var niveis = new List<NivelPilar>
        {
            new NivelPilar { NomePT = "TOPO PILAR", NomeEN = "TOP COLUMN", Cota = cotaTopo },
            new NivelPilar { NomePT = "BASE PILAR", NomeEN = "COLUMN BASE", Cota = cotaBase },
        };
        foreach (var n in LerNiveis(v["Niveis"]))
        {
            if (n.Cota <= cotaBase + 1e-6 || n.Cota >= cotaTopo - 1e-6)
            {
                r.Avisos.Add($"Nível \"{n.NomePT}\" ({FormatarCota(n.Cota)}) fora do pilar: ignorado.");
                continue;
            }
            niveis.Add(n);
        }
        niveis = niveis.OrderByDescending(n => n.Cota).ToList();
        double ladoTri = 0.9 * hv;
        foreach (var n in niveis)
        {
            double y = (n.Cota - cotaBase) * 100;
            // linha de nível atravessando as vistas
            c.Linha(xMarca - 0.5 * hv, y, xFimVistas + 2 * hv, y, "ARM-NIVEL");
            // marca (triângulo com a ponta na linha) e o valor
            R_Triangulo(c, xMarca, y, ladoTri);
            c.Texto(FormatarCota(n.Cota), xMarca + 0.8 * hv, y + 0.6 * hv, 0.9 * hv);
            // nome à esquerda, acima da linha
            double yt = y + (c.Idioma == IdiomaDesenho.PTEN && n.NomeEN.Length > 0 ? 1.5 * hv : 0.5 * hv);
            foreach (var t in c.Linhas(n.NomePT, n.NomeEN)) { c.Texto(t, 0, yt, 0.9 * hv); yt -= 1.3 * hv; }
        }
        // cotas: cadeia entre níveis e o comprimento total
        for (int i = 0; i + 1 < niveis.Count; i++)
        {
            double y1 = (niveis[i + 1].Cota - cotaBase) * 100, y2 = (niveis[i].Cota - cotaBase) * 100;
            c.Cota(xVista0, y1, xVista0, y2, xCadeia, (y1 + y2) / 2, Math.PI / 2, escV);
        }
        string total = c.Idioma switch
        {
            IdiomaDesenho.EN => "TOTAL COLUMN LENGTH: <>",
            IdiomaDesenho.PT => "COMPRIMENTO TOTAL DO PILAR: <>",
            _ => "COMPRIMENTO TOTAL DO PILAR (TOTAL LENGTH): <>",
        };
        c.Cota(xVista0, 0, xVista0, L, xTotal, L / 2, Math.PI / 2, escV, total);

        // marcas do corte A-A na VISTA 2, perto do topo
        double yA = L - Math.Min(20, L / 10);
        foreach (var (xa, xb) in new[] { (xs[1] - 3 * hv, xs[1] - 1 * hv), (xs[1] + larguras[1] + 1 * hv, xs[1] + larguras[1] + 3 * hv) })
        {
            c.Linha(xa, yA, xb, yA, "ARM-CORTE");
            c.Texto("A", (xa + xb) / 2, yA + 0.5 * hv, hv, 1, 0, "ARM-CORTE");
        }

        // ---------------- coluna da direita: corte A-A, notas, produção ----------------
        double hc = c.hC;
        // armaduras (elevação, faixas, seções, formas das barras) entre as vistas e a coluna da direita
        double xD = GerarArmaduras(c, v, cfg, xFimVistas + 30 * hv, b, h, L, ch) + 30 * hv;   // margem esquerda da coluna
        double yCur = L + 16 * hv;
        // título do corte
        foreach (var t in c.Linhas("CORTE A-A", "SECTION A-A")) { c.Texto(t, xD, yCur, 1.4 * hc); yCur -= 2.2 * hc; }
        c.Texto($"ESC 1:{escC:0}", xD, yCur, 0.8 * hc);
        yCur -= 6 * hc;
        // seção (verdadeira grandeza), com os nomes das vistas em volta
        double sx0 = xD + 6 * hc, sy1 = yCur, sx1 = sx0 + b, sy0 = sy1 - h;
        var secao = new ContornoTemplate { Fechado = true, Layer = "ARM-FORMA" };
        if (ch > 0)
            secao.Vertices.AddRange(new[] { (sx0 + ch, sy0), (sx1 - ch, sy0), (sx1, sy0 + ch), (sx1, sy1 - ch), (sx1 - ch, sy1), (sx0 + ch, sy1), (sx0, sy1 - ch), (sx0, sy0 + ch) });
        else
            secao.Vertices.AddRange(new[] { (sx0, sy0), (sx1, sy0), (sx1, sy1), (sx0, sy1) });
        r.Contornos.Add(secao);
        bool insertoTopo = InsertoNoCorte(c, insertos, sx0, sy0, b, h);
        double smx = (sx0 + sx1) / 2, smy = (sy0 + sy1) / 2;
        RotuloVista(c, 4, smx, sy1 + (insertoTopo ? 5 : 1.5) * hc, 0); // em cima (acima das cotas do gabarito)
        RotuloVista(c, 2, smx, sy0 - 7 * hc, 0);                           // embaixo (abaixo da cota)
        RotuloVista(c, 3, sx0 - 1.5 * hc, smy, Math.PI / 2);               // esquerda
        RotuloVista(c, 1, sx1 + 7 * hc, smy, Math.PI / 2);                 // direita (depois da cota)
        c.Cota(sx0, sy0, sx1, sy0, smx, sy0 - 3 * hc, 0, escC);
        c.Cota(sx1, sy0, sx1, sy1, sx1 + 3 * hc, smy, Math.PI / 2, escC);
        yCur = sy0 - 14 * hc;
        // cortes de forma nos níveis dos consolos (B-B, C-C…)
        yCur = CortesConsolo(c, consolos, b, h, xD, yCur, cob0);

        // detalhes das armaduras dos consolos, abaixo das vistas
        double yDet = -30 * hv;
        // um detalhe por tipo (e largura do pilar na direção do balanço), somando as posições que usam o tipo
        int nDet = 0;
        foreach (var grupo in consolos.GroupBy(co => (co.Tipo, co.Duplo, colW: co.Faces[0] == 1 || co.Faces[0] == 3 ? b : h)))
        {
            var rep = grupo.First();
            int pares = grupo.Sum(co => co.Pares.Count), soz = grupo.Sum(co => co.Sozinhas.Count);
            yDet = DetalheConsolo(c, cfg, rep, pares, soz, $"cons{++nDet}", b, h, cob0, 0, yDet, v["LongAco"], v["EstAco"]);
        }
        // detalhes padrão (alça, reforço do furo, chaves, canto, raio de curvatura, emendas, SPDA)
        double phiTd = new[] { "EstBitola", "TopoBitola", "BaseBitola" }.Max(k => v.Numero(k));
        DetalhesPadrao(c, v, cfg, itens, cob0, phiTd, 0, yDet);

        // notas
        double hn = 0.9 * hv;
        var cob = new[] { "CobFace1", "CobFace2", "CobFace3", "CobFace4", "CobTopo", "CobBase" }.Select(k => v.Numero(k)).ToArray();
        int tol = ToleranciaComprimento(L);
        string cobPt = $"COBRIMENTOS: FACE 1 = {Num(cob[0], "0.0#")}; FACE 2 = {Num(cob[1], "0.0#")}; FACE 3 = {Num(cob[2], "0.0#")}; " +
                       $"FACE 4 = {Num(cob[3], "0.0#")}; TOPO = {Num(cob[4], "0.0#")}; BASE = {Num(cob[5], "0.0#")} cm;";
        var notasPt = new[]
        {
            "NOTAS:",
            "1) DIMENSÕES EM cm;",
            "2) " + cobPt,
            $"3) TOLERÂNCIAS DE FABRICAÇÃO (NBR 9062, TAB. 2): COMPRIMENTO ±{tol} mm; SEÇÃO -5/+10 mm;",
            "    DISTORÇÃO ±5 mm; LINEARIDADE ±L/1000; POSIÇÃO DOS INSERTOS ±15 mm;",
            "4) OS INSERTOS METÁLICOS SÃO DE RESPONSABILIDADE DA FORNECEDORA. TODOS OS CRITÉRIOS",
            "    NORMATIVOS DEVEM SER ASSEGURADOS PELA FORNECEDORA.",
        };
        var notasEn = new[]
        {
            "NOTES:",
            "1) DIMENSIONS IN cm;",
            "2) " + FormattableString.Invariant($"CONCRETE COVER: FACE 1 = {cob[0]:0.0#}; FACE 2 = {cob[1]:0.0#}; FACE 3 = {cob[2]:0.0#}; FACE 4 = {cob[3]:0.0#}; TOP = {cob[4]:0.0#}; BASE = {cob[5]:0.0#} cm;"),
            $"3) MANUFACTURING TOLERANCES (NBR 9062, TABLE 2): LENGTH ±{tol} mm; SECTION -5/+10 mm;",
            "    DISTORTION ±5 mm; STRAIGHTNESS ±L/1000; POSITION OF INSERTS ±15 mm;",
            "4) STEEL INSERTS ARE THE SUPPLIER'S RESPONSIBILITY. ALL CODE REQUIREMENTS",
            "    SHALL BE ENSURED BY THE SUPPLIER.",
        };
        if (c.Idioma != IdiomaDesenho.EN) yCur = c.Bloco(notasPt, xD, yCur, hn) - hn;
        if (c.Idioma != IdiomaDesenho.PT) yCur = c.Bloco(notasEn, xD, yCur, hn) - hn;

        // informações para produção (volume e peso calculados)
        double vol = VolumeTotal(b, h, L, ch, consolos);
        double peso = vol * v.Numero("PesoEspecifico") / 1000; // t
        int qtd = Math.Max(1, v.Inteiro("Quantidade"));
        r.Tabelas.Add(TabelaProducao(c.Idioma, v.Numero("FcjSaque"), v.Numero("FcjMontagem"), v.Numero("Fck"), vol, peso, qtd, xD, yCur, escV));

        // verificações de detalhamento (avisos, não bloqueiam)
        if (cob.Take(4).Any(x => x < 2)) r.Avisos.Add("Cobrimento lateral < 2,0 cm: abaixo do mínimo da NBR 9062 (9.2.1.1.2) para pilares.");
        // relação do aço do detalhamento, à direita de tudo
        r.PosicaoRA = (xD + 75 * hv, L + 16 * hv);
        return r;
    }

    /// <summary>
    /// Quadro "Informações para produção" (modelo do usuário, 2026-10-29): tabela de 2 colunas — rótulo (PT e "(EN)")
    /// à esquerda e valor com unidade à direita. A linha da montagem só entra com fcj de montagem &gt; 0.
    /// </summary>
    public static TabelaTemplate TabelaProducao(IdiomaDesenho idioma, double fcjSaque, double fcjMontagem, double fck,
        double volume, double peso, int quantidade, double x, double y, double escala)
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
        t.Linhas.Add(new[] { R("RESISTÊNCIA AO SAQUE:", "STRENGTH DEMOLDING:"), $"fcj = {N(fcjSaque, "0.#")} MPa" });
        if (fcjMontagem > 0)
            t.Linhas.Add(new[] { R("RESISTÊNCIA NA MONTAGEM:", "STRENGTH ERECTION:"), $"fcj = {N(fcjMontagem, "0.#")} MPa" });
        t.Linhas.Add(new[] { R("RESISTÊNCIA 28 DIAS:", "STRENGTH AT 28 DAYS:"), $"fck = {N(fck, "0.#")} MPa" });
        t.Linhas.Add(new[] { R("VOLUME DE CONCRETO:", "CONCRETE VOLUME:"), $"{N(volume, "0.000")} m³" });
        t.Linhas.Add(new[] { R("PESO:", "WEIGHT:"), $"{N(peso, "0.000")} ton." });
        t.Linhas.Add(new[] { R("QUANTIDADE:", "QUANTITY:"), $"{quantidade} un." });
        return t;
    }

    private static void R_Triangulo(Ctx c, double x, double y, double lado) =>
        c.R.Contornos.Add(new ContornoTemplate
        {
            Vertices = { (x, y), (x - lado / 2, y + lado * 0.87), (x + lado / 2, y + lado * 0.87) }, Fechado = true, Layer = "ARM-NIVEL",
        });

    /// <summary>"VISTA n" (e "(VIEW n)") em volta do corte; angulo π/2 = texto na vertical.</summary>
    private static void RotuloVista(Ctx c, int n, double x, double y, double angulo)
    {
        var linhas = c.Linhas($"VISTA {n}", $"VIEW {n}");
        double h = 0.9 * c.hC;
        for (int i = 0; i < linhas.Length; i++)
        {
            // na vertical as linhas se afastam em x; na horizontal, em y
            double d = i * 1.3 * h;
            if (angulo == 0) c.Texto(linhas[i], x, y - d, h, 1);
            else c.Texto(linhas[i], x + d, y, h, 1, angulo);
        }
    }
}
