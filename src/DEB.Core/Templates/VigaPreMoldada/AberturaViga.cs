using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Estribos;
using DEB.Core.Modelo;

namespace DEB.Core.Templates.VigaPreMoldada;

/// <summary>Abertura na alma da viga (furo horizontal atravessando a largura b), em cm.</summary>
public sealed class AberturaViga
{
    /// <summary>Número da linha na tabela (1, 2…), usado nos nomes das barras.</summary>
    public int Indice { get; set; }
    public bool Circular { get; set; }
    /// <summary>Centro, a partir da ponta esquerda.</summary>
    public double X { get; set; }
    /// <summary>Largura ao longo da viga (o diâmetro, na circular).</summary>
    public double Largura { get; set; }
    /// <summary>Fundo da abertura acima do fundo da viga.</summary>
    public double Z { get; set; }
    /// <summary>Altura (o diâmetro, na circular).</summary>
    public double Altura { get; set; }
    /// <summary>Recebe o reforço (a circular pequena não recebe).</summary>
    public bool Reforcada { get; set; } = true;
    public double X0 => X - Largura / 2;
    public double X1 => X + Largura / 2;
    public double Z1 => Z + Altura;
}

/// <summary>
/// Aberturas na alma (pedido do usuário, 2026-11-11, regra tirada das pranchas do escritório e aprovada por ele): em cada abertura
/// reforçada, <c>AbertEstQtd</c> estribos inteiros (os da alma) numa faixa de <c>AbertEstFaixa</c> de cada lado; estribos curtos acima e
/// abaixo da abertura a cada <c>AbertCurtoEsp</c>; <c>AbertBarrasQtd</c> barras horizontais acima e abaixo, passando a ancoragem de cada
/// lado. A circular até <c>AbertSemReforco</c> de diâmetro não recebe reforço. Na forma: a abertura (com X) e as cotas; nas armaduras:
/// os estribos da alma saem do trecho da abertura, as faixas entram na cadeia dos estribos, as barras horizontais numa linha fora da
/// forma ("REFORÇO ABERTURA") e cada abertura ganha uma seção.
/// </summary>
public sealed partial class TemplateVigaPreMoldada
{
    private const string G13 = "13. Aberturas na alma";
    public const string AbertRetangular = "Retangular", AbertCircular = "Circular";

    private static IEnumerable<ParametroTemplate> ParametrosAberturas() => new[]
    {
        new ParametroTemplate
        {
            Grupo = G13, Nome = "Aberturas", Rotulo = "Aberturas na alma", Tipo = TipoParametro.Tabela, NomeItem = "abertura", Padrao = "",
            Descricao = "Clique em … para incluir as aberturas (furos horizontais atravessando a alma): forma, centro (cm da ponta esquerda), " +
                        "largura (diâmetro na circular), fundo acima do fundo da viga e altura.",
            Colunas =
            {
                new ColunaTabela { Nome = "Forma", Rotulo = "Forma", Tipo = TipoParametro.Opcao, Opcoes = { AbertRetangular, AbertCircular }, Padrao = AbertRetangular, Largura = 90 },
                new ColunaTabela { Nome = "X", Rotulo = "Centro (cm)", Tipo = TipoParametro.Numero, Padrao = "200", Largura = 80 },
                new ColunaTabela { Nome = "Largura", Rotulo = "Largura/diâmetro (cm)", Tipo = TipoParametro.Numero, Padrao = "25", Largura = 120 },
                new ColunaTabela { Nome = "Z", Rotulo = "Fundo (cm)", Tipo = TipoParametro.Numero, Padrao = "20", Largura = 75 },
                new ColunaTabela { Nome = "Altura", Rotulo = "Altura (cm)", Tipo = TipoParametro.Numero, Padrao = "25", Largura = 75 },
            },
        },
        P(G13, "AbertEstQtd", "Estribos de reforço de cada lado", "3", tipo: TipoParametro.Inteiro, min: 0, max: 8,
          descricao: "Estribos da alma juntos de cada face da abertura (padrão do escritório: 3 numa faixa de 10 cm)."),
        P(G13, "AbertEstFaixa", "Faixa dos estribos de reforço (de cada lado)", "10", "cm", min: 2),
        P(G13, "AbertCurtoBitola", "Estribos curtos acima e abaixo: bitola", "6.3", "mm", TipoParametro.Bitola, 5, 12.5),
        P(G13, "AbertCurtoEsp", "Estribos curtos: espaçamento", "5", "cm", min: 2),
        P(G13, "AbertBarrasQtd", "Barras horizontais acima e abaixo (cada)", "2", tipo: TipoParametro.Inteiro, min: 0, max: 6),
        P(G13, "AbertBarrasBitola", "Barras horizontais: bitola", "10", "mm", TipoParametro.Bitola, 6.3, 20),
        P(G13, "AbertAncoragem", "Barras horizontais: ancoragem além da abertura", "40", "φ", min: 10),
        P(G13, "AbertSemReforco", "Circular sem reforço até o diâmetro", "15", "cm", min: 0),
    };

    // ======================== leitura e cálculos (testáveis) ========================

    public static List<AberturaViga> LerAberturas(ValoresTemplate v, double h, double L, ExtremidadeViga e, ExtremidadeViga d, List<string> avisos)
    {
        var r = new List<AberturaViga>();
        var linhas = v.Tabela("Aberturas");
        double semRef = v.Numero("AbertSemReforco");
        for (int k = 1; k <= linhas.Count; k++)
        {
            var l = linhas[k - 1];
            bool circ = ValoresTemplate.Celula(l, 0).StartsWith("Circ", StringComparison.OrdinalIgnoreCase);
            double w = ValoresTemplate.NumeroCelula(l, 2), z = ValoresTemplate.NumeroCelula(l, 3), alt = circ ? w : ValoresTemplate.NumeroCelula(l, 4);
            var a = new AberturaViga { Indice = k, Circular = circ, X = ValoresTemplate.NumeroCelula(l, 1), Largura = w, Z = z, Altura = alt };
            if (w <= 0 || alt <= 0) { avisos.Add($"Abertura {k}: informe a largura e a altura."); continue; }
            if (a.X0 < 0 || a.X1 > L) { avisos.Add($"Abertura {k}: fora da viga (de {Num(a.X0, "0.#")} a {Num(a.X1, "0.#")} cm)."); continue; }
            if (a.Z < 0 || a.Z1 > h) { avisos.Add($"Abertura {k}: fora da altura da viga (fundo {Num(a.Z, "0.#")}, topo {Num(a.Z1, "0.#")} cm)."); continue; }
            double li = e.TemDente ? e.CompDente + ExtensaoSuspensao(e) : 0, lf = L - (d.TemDente ? d.CompDente + ExtensaoSuspensao(d) : 0);
            if (a.X0 < li || a.X1 > lf) avisos.Add($"Abertura {k}: na região do dente ou da suspensão — conferir.");
            a.Reforcada = !(circ && w <= semRef + 1e-6);
            r.Add(a);
        }
        r = r.OrderBy(a => a.X).ToList();
        double fx = v.Inteiro("AbertEstQtd") > 0 ? v.Numero("AbertEstFaixa") : 0;
        for (int i = 0; i + 1 < r.Count; i++)
            if (r[i].X1 + fx > r[i + 1].X0 - fx - 1e-6)
                avisos.Add($"Aberturas {r[i].Indice} e {r[i + 1].Indice}: próximas demais para o reforço de cada uma — conferir.");
        if (r.Any(a => a.Reforcada))
            avisos.Add("Aberturas na alma: reforço pelo padrão do escritório (estribos de cada lado, estribos curtos e barras horizontais) — conferir.");
        return r;
    }

    /// <summary>
    /// Zonas de estribos com as aberturas: o trecho de cada abertura reforçada (com as faixas de reforço) sai da zona, que se divide em
    /// duas com o mesmo espaçamento, e entram as faixas de reforço de cada lado com a quantidade fixa (<see cref="ZonaEstriboViga.Contada"/>).
    /// </summary>
    public static List<ZonaEstriboViga> ZonasComAberturas(ValoresTemplate v, ConfiguracaoArmadura cfg, List<ZonaEstriboViga> zonas, List<AberturaViga> abs)
    {
        var refs = abs.Where(a => a.Reforcada).ToList();
        if (refs.Count == 0) return zonas;
        int n = Math.Max(0, v.Inteiro("AbertEstQtd"));
        double fx = n > 0 ? Math.Max(1, v.Numero("AbertEstFaixa")) : 0;
        double cob = v.Numero("CobPontas"), phi = v.Numero("EstBitola") / 10;
        var r = new List<ZonaEstriboViga>();
        foreach (var z in zonas)
        {
            var pedacos = new List<(double A, double B)> { (z.X1, z.X2) };
            foreach (var a in refs)
            {
                double ca = a.X0 - fx, cb = a.X1 + fx;
                pedacos = pedacos.SelectMany(p => p.B <= ca || p.A >= cb ? new[] { p }
                    : new[] { (p.A, Math.Min(p.B, ca)), (Math.Max(p.A, cb), p.B) }.Where(q => q.Item2 - q.Item1 > 1e-6)).ToList();
            }
            foreach (var (a0, b0) in pedacos.Where(p => p.B - p.A >= 1))
            {
                var nz = new ZonaEstriboViga { Nome = z.Nome, X1 = a0, X2 = b0, Esp = z.Esp };
                int q = CalculoFaixa.Quantidade(b0 - a0, z.Esp, cfg.EspacamentoInicialFaixa, cfg.ModoQuantidadeFaixa);
                nz.Posicoes = CalculoFaixa.Posicoes(b0 - a0, q, z.Esp, cfg.EspacamentoInicialFaixa).Select(s => a0 + s).ToList();
                r.Add(nz);
            }
        }
        if (n > 0)
            foreach (var a in refs)
                foreach (bool esq in new[] { true, false })
                {
                    double fa = esq ? a.X0 - fx : a.X1, fb = esq ? a.X0 : a.X1 + fx;
                    // o primeiro junto da face da abertura (a cobrimento), os outros para fora, por igual na faixa
                    double c0 = cob + phi / 2, passo = n > 1 ? Math.Max(0, fx - c0 - phi / 2) / (n - 1) : 0;
                    var pos = Enumerable.Range(0, n).Select(i => esq ? a.X0 - c0 - i * passo : a.X1 + c0 + i * passo).OrderBy(x => x).ToList();
                    r.Add(new ZonaEstriboViga { Nome = $"reforço abertura {a.Indice}", X1 = fa, X2 = fb, Esp = passo > 0 ? passo : fx, Contada = n, Posicoes = pos });
                }
        return r.OrderBy(z => z.X1).ToList();
    }

    /// <summary>Estribo curto acima (ou abaixo) da abertura: y das faces externas (do fundo da viga); null se não cabe.</summary>
    public static (double Y0, double Y1)? EstriboCurto(ValoresTemplate v, AberturaViga a, double h, bool acima)
    {
        double cobF = v.Numero("CobFundo"), cobT = v.Numero("CobTopo"), cobL = v.Numero("CobLateral");
        double y0 = acima ? a.Z1 + cobL : cobF, y1 = acima ? h - cobT : a.Z - cobL;
        return y1 - y0 >= 5 ? (y0, y1) : ((double, double)?)null;
    }

    /// <summary>Posições (eixo) dos estribos curtos ao longo da abertura, pela regra de faixas do plugin.</summary>
    public static List<double> PosicoesCurtos(ValoresTemplate v, ConfiguracaoArmadura cfg, AberturaViga a)
    {
        double esp = Math.Max(2, v.Numero("AbertCurtoEsp"));
        int q = CalculoFaixa.Quantidade(a.Largura, esp, cfg.EspacamentoInicialFaixa, cfg.ModoQuantidadeFaixa);
        return CalculoFaixa.Posicoes(a.Largura, q, esp, cfg.EspacamentoInicialFaixa).Select(s => a.X0 + s).ToList();
    }

    /// <summary>Nome da barra dos estribos curtos: um só (repetido 2x) quando os de cima e de baixo são iguais.</summary>
    private static string NomeCurto(AberturaViga a, bool? acima) => acima == null ? $"abert{a.Indice}curto" : acima.Value ? $"abert{a.Indice}cima" : $"abert{a.Indice}baixo";

    private static List<(string Nome, double Y0, double Y1, int Rep)> CurtosDaAbertura(ValoresTemplate v, AberturaViga a, double h)
    {
        var r = new List<(string, double, double, int)>();
        if (!a.Reforcada) return r;
        var cima = EstriboCurto(v, a, h, true);
        var baixo = EstriboCurto(v, a, h, false);
        if (cima is { } c1 && baixo is { } b1 && Math.Abs((c1.Y1 - c1.Y0) - (b1.Y1 - b1.Y0)) < 0.05) { r.Add((NomeCurto(a, null), c1.Y0, c1.Y1, 2)); return r; }
        if (cima is { } c2) r.Add((NomeCurto(a, true), c2.Y0, c2.Y1, 1));
        if (baixo is { } b2) r.Add((NomeCurto(a, false), b2.Y0, b2.Y1, 1));
        return r;
    }

    /// <summary>Barras horizontais de uma abertura: comprimento (dentro da viga) e quantos lados (em cima e/ou embaixo) as têm.</summary>
    public static (double Ini, double Fim, int Lados) BarrasHorizontais(ValoresTemplate v, AberturaViga a, double h, double L)
    {
        double phi = v.Numero("AbertBarrasBitola"), anc = v.Numero("AbertAncoragem") * phi / 10, cob = v.Numero("CobPontas");
        int lados = (EstriboCurto(v, a, h, true) != null ? 1 : 0) + (EstriboCurto(v, a, h, false) != null ? 1 : 0);
        return (Math.Max(cob, a.X0 - anc), Math.Min(L - cob, a.X1 + anc), lados);
    }

    // ======================== desenho ========================

    /// <summary>Abertura em vista (elevação): retângulo com X ou círculo; <paramref name="yB"/> = fundo da viga.</summary>
    private static void AberturaEmVista(Ctx c, AberturaViga a, double yB)
    {
        if (a.Circular) { c.R.Circulos.Add(new CirculoTemplate { X = a.X, Y = yB + a.Z + a.Altura / 2, Raio = a.Largura / 2, Layer = "ARM-FORMA" }); return; }
        c.Retangulo(a.X0, yB + a.Z, a.X1, yB + a.Z1);
        c.Linha(a.X0, yB + a.Z, a.X1, yB + a.Z1);
        c.Linha(a.X0, yB + a.Z1, a.X1, yB + a.Z);
    }

    /// <summary>Na FÔRMA EM ELEVAÇÃO: as aberturas, a cadeia das posições embaixo (abaixo do comprimento total) e a altura de cada uma.</summary>
    private static void AberturasEmElevacao(Ctx c, List<AberturaViga> abs, double L, double yE)
    {
        if (abs.Count == 0) return;
        double hv = c.hV, esc = c.escV;
        foreach (var a in abs)
        {
            AberturaEmVista(c, a, yE);
            CadeiaV(c, new List<double> { yE, yE + a.Z, yE + a.Z1 }, a.X0, a.X0 - 2.2 * hv, esc);
        }
        var xs = new List<double> { 0, L };
        foreach (var a in abs) { xs.Add(a.X0); xs.Add(a.X1); }
        CadeiaH(c, xs.Distinct().OrderBy(x => x).ToArray(), yE, yE - 11.5 * hv, esc);
    }

    /// <summary>Nas ARMADURAS EM ELEVAÇÃO: as aberturas, os estribos curtos (traços) e as barras horizontais no lugar.</summary>
    private static void AberturasEmElevacaoArmaduras(Ctx c, ValoresTemplate v, ConfiguracaoArmadura cfg, List<AberturaViga> abs, double h, double L, double yB)
    {
        double tC = v.Numero("AbertCurtoBitola") / 20, rH = v.Numero("AbertBarrasBitola") / 20, cobL = v.Numero("CobLateral");
        foreach (var a in abs)
        {
            AberturaEmVista(c, a, yB);
            if (!a.Reforcada) continue;
            var xs = PosicoesCurtos(v, cfg, a);
            foreach (bool acima in new[] { true, false })
                if (EstriboCurto(v, a, h, acima) is { } ec)
                    foreach (double x in xs) c.Linha(x, yB + ec.Y0 + tC, x, yB + ec.Y1 - tC, "ARM-BARRA-DETALHE");
            if (v.Inteiro("AbertBarrasQtd") <= 0) continue;
            var (i0, f0, _) = BarrasHorizontais(v, a, h, L);
            if (EstriboCurto(v, a, h, true) != null) c.Linha(i0, yB + a.Z1 + cobL + 2 * tC + rH, f0, yB + a.Z1 + cobL + 2 * tC + rH, "ARM-BARRA-DETALHE");
            if (EstriboCurto(v, a, h, false) != null) c.Linha(i0, yB + a.Z - cobL - 2 * tC - rH, f0, yB + a.Z - cobL - 2 * tC - rH, "ARM-BARRA-DETALHE");
        }
    }

    /// <summary>
    /// Faixas da cadeia dos estribos (embaixo da forma): as zonas (com as de reforço contadas) e os estribos curtos das aberturas,
    /// da esquerda para a direita, em duas linhas alternadas. Devolve a linha (0 ou 1) do primeiro e do último item.
    /// </summary>
    private static (int Primeiro, int Ultimo) CadeiaEstribos(Ctx c, ValoresTemplate v, ConfiguracaoArmadura cfg, List<ZonaEstriboViga> zonas,
        List<AberturaViga> abs, double h, double yF)
    {
        double hv = c.hV, esc = c.escV;
        var itens = new List<(double X1, Func<double, FaixaTemplate[]> Criar)>();
        foreach (var z in zonas)
        {
            var zz = z;
            itens.Add((zz.X1, y => new[]
            {
                zz.Contada > 0
                    ? FaixaContada("estribo", zz.X1, zz.X2, y, zz.Contada, y + 0.8 * hv, esc, cfg)
                    : new FaixaTemplate { Barra = "estribo", Tipo = TipoFaixa.Ortogonal, P1 = (zz.X1, y), P2 = (zz.X2, y), Posicao = ((zz.X1 + zz.X2) / 2, y + 0.8 * hv), Espacamento = zz.Esp, Escala = esc },
            }));
        }
        double esp = Math.Max(2, v.Numero("AbertCurtoEsp"));
        foreach (var a in abs.Where(a => a.Reforcada))
        {
            var curtos = CurtosDaAbertura(v, a, h);
            if (curtos.Count == 0) continue;
            var aa = a;
            itens.Add((aa.X0, y => curtos.Select((ct, i) =>
            {
                double yy = y - i * 3.2 * hv;   // de baixo (diferente do de cima): uma linha abaixo
                return new FaixaTemplate { Barra = ct.Nome, Tipo = TipoFaixa.Ortogonal, P1 = (aa.X0, yy), P2 = (aa.X1, yy), Posicao = (aa.X, yy + 0.8 * hv),
                    Espacamento = esp, Repeticoes = ct.Rep, Escala = esc };
            }).ToArray()));
        }
        itens = itens.OrderBy(i => i.X1).ToList();
        for (int i = 0; i < itens.Count; i++) c.R.Faixas.AddRange(itens[i].Criar(yF - (i % 2) * 3.2 * hv));
        return (0, Math.Max(0, itens.Count - 1) % 2);
    }

    /// <summary>As barras horizontais de cada abertura numa linha fora da forma ("REFORÇO ABERTURA"); devolve o y abaixo delas.</summary>
    private static double AberturasBarras(Ctx c, ValoresTemplate v, List<AberturaViga> abs, double h, double L, double y, double passo)
    {
        int q = Math.Max(0, v.Inteiro("AbertBarrasQtd"));
        if (q == 0) return y;
        double hv = c.hV;
        foreach (var a in abs.Where(a => a.Reforcada))
        {
            var (i0, f0, lados) = BarrasHorizontais(v, a, h, L);
            if (lados == 0 || f0 - i0 < 10) continue;
            c.R.Barras.Add(new BarraTemplate
            {
                Nome = $"abert{a.Indice}h", Bitola = v.Numero("AbertBarrasBitola"), Aco = v["LongAco"], Quantidade = q, Repeticoes = lados, Escala = c.escV,
                Vertices = { (i0, y), (f0, y) }, Formatos = { FormatoSegmento.Linha }, PosicaoDescricao = ((i0 + f0) / 2, y + 2.6 * hv),
            });
            c.Texto(string.Join(" / ", c.Linhas("REFORÇO ABERTURA", "OPENING REINFORCEMENT")), (i0 + f0) / 2, y - 3.4 * hv, 0.75 * hv, 1);
            y -= passo;
        }
        return y;
    }

    /// <summary>Formas dos estribos curtos (fechados) de cada abertura, para a coluna das formas.</summary>
    private static List<BarraTemplate> FormasAberturas(ValoresTemplate v, ConfiguracaoArmadura cfg, List<AberturaViga> abs, double b, double h)
    {
        var r = new List<BarraTemplate>();
        double cobL = v.Numero("CobLateral"), phi = v.Numero("AbertCurtoBitola"), w = b - 2 * cobL;
        string aco = v["EstAco"];
        foreach (var a in abs)
            foreach (var (nome, y0, y1, rep) in CurtosDaAbertura(v, a, h))
            {
                var f = GeradorEstribo.FechadoPadrao(new List<(double X, double Y)> { (0, 0), (w, 0), (w, y1 - y0), (0, y1 - y0) }, phi, aco, cfg.Dobramento);
                var bt = new BarraTemplate { Nome = nome, Bitola = phi, Aco = aco, Estribo = true, Quantidade = PosicoesCurtos(v, cfg, a).Count, Repeticoes = rep };
                bt.Vertices.AddRange(f.Vertices.Select(p => (p.X, p.Y)));
                bt.Formatos.AddRange(f.Formatos);
                foreach (var s in f.Sobrepostos) bt.Sobrepostos.Add(s);
                foreach (var s in f.Cotados) bt.Cotados.Add(s);
                r.Add(bt);
            }
        return r;
    }

    /// <summary>Seção de forma pela abertura: a alma com o vazio (retângulo com X) e as cotas das partes.</summary>
    private static void AberturaNaSecaoForma(Ctx c, AberturaViga a, double b, double sx0, double sy0)
    {
        double y0 = sy0 + a.Z, y1 = sy0 + a.Z1;
        c.Retangulo(sx0, y0, sx0 + b, y1);
        c.Linha(sx0, y0, sx0 + b, y1);
        c.Linha(sx0, y1, sx0 + b, y0);
        CadeiaV(c, new List<double> { sy0, y0, y1 }, sx0, sx0 - 3 * c.hC, c.escC);
    }

    /// <summary>Seção de armadura pela abertura: o vazio, os estribos curtos e as barras horizontais (com as indicações).</summary>
    private static void AberturaNaSecaoArmadura(Ctx c, ValoresTemplate v, AberturaViga a, double b, double h, double sx0, double sy0)
    {
        double hc = c.hC, cobL = v.Numero("CobLateral"), t = v.Numero("AbertCurtoBitola") / 20, rH = v.Numero("AbertBarrasBitola") / 20;
        AberturaNaSecaoForma(c, a, b, sx0, sy0);
        if (!a.Reforcada) return;
        var curtos = CurtosDaAbertura(v, a, h);
        int q = Math.Max(0, v.Inteiro("AbertBarrasQtd"));
        var pts = new List<(double X, double Y)>();
        int k = 0;
        foreach (bool acima in new[] { true, false })
        {
            if (!(EstriboCurto(v, a, h, acima) is { } ec)) continue;
            double y0 = sy0 + ec.Y0 + t, y1 = sy0 + ec.Y1 - t, x0 = sx0 + cobL + t, x1 = sx0 + b - cobL - t;
            c.Retangulo(x0, y0, x1, y1, "ARM-BARRA-DETALHE");
            string nome = curtos.Count == 1 && curtos[0].Rep == 2 ? curtos[0].Nome : NomeCurto(a, acima);
            if (k++ == 0 || nome != curtos[0].Nome)
                c.R.Faixas.Add(new FaixaTemplate { Barra = nome, Tipo = TipoFaixa.Indicacao, Referencias = { (x1, (y0 + y1) / 2) },
                    Posicao = (sx0 + b + 4 * hc, (y0 + y1) / 2), Escala = c.escC });
            // barras horizontais junto da abertura, por dentro do estribo curto
            double yb = acima ? y0 + t + rH : y1 - t - rH;
            if (q == 1) pts.Add(((x0 + x1) / 2, yb));
            else for (int i = 0; i < q; i++) pts.Add((x0 + t + rH + i * (x1 - x0 - 2 * t - 2 * rH) / (q - 1), yb));
        }
        foreach (var (px, py) in pts) c.R.Circulos.Add(new CirculoTemplate { X = px, Y = py, Raio = rH, Layer = "ARM-BARRA-SECAO" });
        if (pts.Count > 0)
            c.R.Faixas.Add(new FaixaTemplate { Barra = $"abert{a.Indice}h", Tipo = TipoFaixa.Indicacao, Referencias = pts,
                Posicao = (sx0 - 5 * hc, sy0 + a.Z + a.Altura / 2), Escala = c.escC });
    }
}
