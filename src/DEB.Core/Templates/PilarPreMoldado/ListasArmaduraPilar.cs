using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Motor;

namespace DEB.Core.Templates.PilarPreMoldado;

/// <summary>Uma linha da lista de longitudinais: arranjo na seção, bitola, feixe e o trecho do pilar que ela ocupa.</summary>
public sealed class LinhaLongitudinal
{
    /// <summary>Número da linha na lista (1, 2…).</summary>
    public int Linha { get; set; }
    public string Arranjo { get; set; } = "";
    /// <summary>Barras por face (arranjos de face); nos cantos é fixo.</summary>
    public int PorFace { get; set; }
    public double Bitola { get; set; }
    /// <summary>Barras por feixe (1, 2 ou 3).</summary>
    public int Feixe { get; set; } = 1;
    /// <summary>Trecho ao longo do pilar (cm a partir da base, já limitado aos cobrimentos).</summary>
    public double Ini { get; set; }
    public double Fim { get; set; }
    public string Chave => "L" + Linha.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Zona de estribos (cm a partir da base). A "corrente" preenche os trechos que a lista não cobre.</summary>
public sealed class ZonaEstribo
{
    public string Nome { get; set; } = "";
    public double Y1 { get; set; }
    public double Y2 { get; set; }
    public double Bitola { get; set; }
    public double Esp { get; set; }
    public bool Grampos { get; set; }
}

/// <summary>
/// Etapa 5 do pilar (2026-10-30): armadura longitudinal e zonas de estribos digitadas em listas (janela com Ctrl+V do Excel),
/// com redução ao longo da altura (cada linha tem início e fim; linhas do mesmo arranjo que se sobrepõem formam uma emenda
/// cotada), bitolas mistas por arranjo e feixes de 2 ou 3 barras.
/// </summary>
public sealed partial class TemplatePilarPreMoldado
{
    public const string ArrCantos = "Cantos", ArrAoLado = "Ao lado dos cantos", ArrFaces13 = "Faces 1 e 3", ArrFaces24 = "Faces 2 e 4",
        ArrFace1 = "Face 1", ArrFace2 = "Face 2", ArrFace3 = "Face 3", ArrFace4 = "Face 4";
    public static readonly string[] Arranjos = { ArrCantos, ArrAoLado, ArrFaces24, ArrFaces13, ArrFace1, ArrFace2, ArrFace3, ArrFace4 };
    private static readonly string[] BitolasLong = { "10", "12.5", "16", "20", "22", "25", "32", "40" };
    private static readonly string[] BitolasEst = { "5", "6.3", "8", "10", "12.5", "16" };

    private static bool ArranjoDeCanto(string a) => a == ArrCantos || a == ArrAoLado;

    public static string NormalizarBitola(string s) => KitDesenho.NormalizarBitola(s);

    private static ParametroTemplate ParametroLongitudinais() => new ParametroTemplate
    {
        Grupo = G7, Nome = "Longitudinais", Rotulo = "Barras longitudinais", Tipo = TipoParametro.Tabela, NomeItem = "linha",
        Padrao = $"{ArrCantos}||25|1||;{ArrAoLado}||20|1||",
        Descricao = "Clique em … para a lista: arranjo (cantos = 4 barras; ao lado dos cantos = 8, em \"L\"; faces = barras por face), " +
                    "bitola, feixe, início e fim (cm da base; vazio = o pilar todo). Para reduzir com a altura, termine uma linha e comece " +
                    "outra do mesmo arranjo antes do fim dela: a sobreposição é a emenda (cotada, com nota).",
        Colunas =
        {
            new ColunaTabela { Nome = "Arranjo", Rotulo = "Arranjo", Tipo = TipoParametro.Opcao, Opcoes = Arranjos.ToList(), Padrao = ArrFaces24, Largura = 140 },
            new ColunaTabela
            {
                Nome = "PorFace", Rotulo = "Barras por face", Tipo = TipoParametro.Numero, Padrao = "1", Largura = 90,
                // nos cantos a quantidade é fixa (4 ou 8): a célula fica vazia
                Ajustar = (l, s) => ArranjoDeCanto(l.Count > 0 ? l[0] : "") ? "" : (string.IsNullOrWhiteSpace(s) ? "1" : s),
            },
            new ColunaTabela
            {
                Nome = "Bitola", Rotulo = "Bitola (mm)", Tipo = TipoParametro.Opcao, Opcoes = BitolasLong.ToList(), Padrao = "20", Largura = 80,
                Ajustar = (l, s) => NormalizarBitola(s),
            },
            new ColunaTabela { Nome = "Feixe", Rotulo = "Feixe", Tipo = TipoParametro.Opcao, Opcoes = { "1", "2", "3" }, Padrao = "1", Largura = 55 },
            new ColunaTabela { Nome = "Ini", Rotulo = "Início (cm da base)", Tipo = TipoParametro.Numero, Padrao = "", Largura = 115 },
            new ColunaTabela { Nome = "Fim", Rotulo = "Fim (cm da base)", Tipo = TipoParametro.Numero, Padrao = "", Largura = 105 },
        },
    };

    private static ParametroTemplate ParametroZonas() => new ParametroTemplate
    {
        Grupo = G9, Nome = "ZonasEstribos", Rotulo = "Zonas de estribos", Tipo = TipoParametro.Tabela, NomeItem = "zona",
        Padrao = "Topo|Topo|0|100|8|5|Sim;Base|Base|0|260|8|10|Sim",
        Descricao = "Clique em … para a lista de zonas (topo, base, consolo…): medida a partir da base ou do topo, de/até (cm), bitola, " +
                    "espaçamento e grampos. O que ficar fora das zonas recebe o estribo do trecho corrente.",
        Colunas =
        {
            new ColunaTabela { Nome = "Nome", Rotulo = "Zona", Padrao = "Consolo", Largura = 90 },
            new ColunaTabela { Nome = "Ref", Rotulo = "Medida a partir", Tipo = TipoParametro.Opcao, Opcoes = { "Base", "Topo" }, Padrao = "Base", Largura = 95 },
            new ColunaTabela { Nome = "De", Rotulo = "De (cm)", Tipo = TipoParametro.Numero, Padrao = "0", Largura = 65 },
            new ColunaTabela { Nome = "Ate", Rotulo = "Até (cm)", Tipo = TipoParametro.Numero, Padrao = "100", Largura = 65 },
            new ColunaTabela
            {
                Nome = "Bitola", Rotulo = "Bitola (mm)", Tipo = TipoParametro.Opcao, Opcoes = BitolasEst.ToList(), Padrao = "8", Largura = 80,
                Ajustar = (l, s) => NormalizarBitola(s),
            },
            new ColunaTabela { Nome = "Esp", Rotulo = "Espaçamento (cm)", Tipo = TipoParametro.Numero, Padrao = "10", Largura = 100 },
            new ColunaTabela { Nome = "Grampos", Rotulo = "Grampos", Tipo = TipoParametro.Opcao, Opcoes = { "Sim", "Não" }, Padrao = "Sim", Largura = 65 },
        },
    };

    // ======================== leitura (testável) ========================

    /// <summary>Linhas válidas da lista de longitudinais, com o trecho limitado a [yIni, yFim] (base e topo menos os cobrimentos).</summary>
    public static List<LinhaLongitudinal> LerLongitudinais(ValoresTemplate v, double yIni, double yFim, List<string> avisos)
    {
        var r = new List<LinhaLongitudinal>();
        var linhas = v.Tabela("Longitudinais");
        for (int k = 1; k <= linhas.Count; k++)
        {
            var l = linhas[k - 1];
            string arr = Arranjos.FirstOrDefault(a => string.Equals(a, ValoresTemplate.Celula(l, 0), StringComparison.OrdinalIgnoreCase));
            if (arr == null) { avisos.Add($"Longitudinal {k}: arranjo \"{ValoresTemplate.Celula(l, 0)}\" desconhecido."); continue; }
            double phi = ValoresTemplate.NumeroCelula(l, 2);
            if (phi <= 0) { avisos.Add($"Longitudinal {k}: informe a bitola."); continue; }
            int porFace = ArranjoDeCanto(arr) ? 0 : (int)Math.Round(ValoresTemplate.NumeroCelula(l, 1, 1));
            if (!ArranjoDeCanto(arr) && porFace < 1) { avisos.Add($"Longitudinal {k}: nenhuma barra por face."); continue; }
            int feixe = Math.Max(1, Math.Min(3, (int)Math.Round(ValoresTemplate.NumeroCelula(l, 3, 1))));
            double ini = Math.Max(yIni, ValoresTemplate.NumeroCelula(l, 4, yIni));
            double fim = Math.Min(yFim, ValoresTemplate.NumeroCelula(l, 5, yFim));
            if (fim - ini < 10) { avisos.Add($"Longitudinal {k}: trecho de {Num(ini, "0.#")} a {Num(fim, "0.#")} cm curto demais."); continue; }
            r.Add(new LinhaLongitudinal { Linha = k, Arranjo = arr, PorFace = porFace, Bitola = phi, Feixe = feixe, Ini = ini, Fim = fim });
        }
        // linhas do mesmo arranjo: a sobreposição é emenda; uma contida na outra é posição repetida
        foreach (var g in r.GroupBy(x => x.Arranjo))
        {
            var lst = g.OrderBy(x => x.Ini).ToList();
            for (int i = 0; i < lst.Count; i++)
                for (int j = i + 1; j < lst.Count; j++)
                    if (lst[j].Ini >= lst[i].Ini - 1e-6 && lst[j].Fim <= lst[i].Fim + 1e-6)
                        avisos.Add($"Longitudinais {lst[i].Linha} e {lst[j].Linha} ({g.Key}): a {lst[j].Linha} fica toda dentro da {lst[i].Linha} " +
                                   "— mesma posição na seção. Use outro arranjo ou ajuste início/fim.");
        }
        return r;
    }

    /// <summary>
    /// Zonas de estribos em ordem, cobrindo [yIni, yFim]: as da lista (sobrepostas são cortadas) e o trecho corrente nos vãos.
    /// </summary>
    public static List<ZonaEstribo> LerZonas(ValoresTemplate v, double yIni, double yFim, double L, List<string> avisos)
    {
        var lista = new List<ZonaEstribo>();
        var linhas = v.Tabela("ZonasEstribos");
        for (int k = 1; k <= linhas.Count; k++)
        {
            var l = linhas[k - 1];
            string nome = ValoresTemplate.Celula(l, 0);
            if (nome.Length == 0) nome = $"Zona {k}";
            bool topo = ValoresTemplate.Celula(l, 1).Equals("Topo", StringComparison.OrdinalIgnoreCase);
            double de = ValoresTemplate.NumeroCelula(l, 2), ate = ValoresTemplate.NumeroCelula(l, 3);
            double y1 = topo ? L - Math.Max(de, ate) : Math.Min(de, ate), y2 = topo ? L - Math.Min(de, ate) : Math.Max(de, ate);
            y1 = Math.Max(yIni, y1); y2 = Math.Min(yFim, y2);
            double phi = ValoresTemplate.NumeroCelula(l, 4), esp = ValoresTemplate.NumeroCelula(l, 5);
            if (y2 - y1 < 1) { avisos.Add($"Zona de estribos \"{nome}\": fora do pilar ou sem comprimento."); continue; }
            if (phi <= 0 || esp <= 0) { avisos.Add($"Zona de estribos \"{nome}\": informe bitola e espaçamento."); continue; }
            lista.Add(new ZonaEstribo
            {
                Nome = nome, Y1 = y1, Y2 = y2, Bitola = phi, Esp = esp,
                Grampos = !ValoresTemplate.Celula(l, 6).Equals("Não", StringComparison.OrdinalIgnoreCase),
            });
        }
        lista = lista.OrderBy(z => z.Y1).ToList();
        for (int i = 1; i < lista.Count; i++)
            if (lista[i].Y1 < lista[i - 1].Y2 - 1e-6)
            {
                avisos.Add($"Zonas de estribos \"{lista[i - 1].Nome}\" e \"{lista[i].Nome}\" se sobrepõem: vale a primeira.");
                lista[i].Y1 = lista[i - 1].Y2;
                if (lista[i].Y2 - lista[i].Y1 < 1) lista.RemoveAt(i--);
            }
        // vãos: trecho corrente
        var r = new List<ZonaEstribo>();
        double y = yIni;
        ZonaEstribo Corrente(double a, double b) => new ZonaEstribo
        {
            Nome = "corrente", Y1 = a, Y2 = b, Bitola = v.Numero("EstBitola"), Esp = v.Numero("EstEsp"), Grampos = v.SimNao("GramposCorrente"),
        };
        foreach (var z in lista)
        {
            if (z.Y1 - y > 1e-6) r.Add(Corrente(y, z.Y1));
            r.Add(z);
            y = z.Y2;
        }
        if (yFim - y > 1e-6) r.Add(Corrente(y, yFim));
        return r;
    }

    /// <summary>Maior bitola de estribo (corrente e zonas): define a posição das longitudinais.</summary>
    public static double BitolaMaxEstribo(ValoresTemplate v) =>
        v.Tabela("ZonasEstribos").Select(l => ValoresTemplate.NumeroCelula(l, 4)).Concat(new[] { v.Numero("EstBitola") }).Max();

    /// <summary>Bitolas do detalhe do canto: a maior dos cantos, a maior "ao lado" e se há o "L".</summary>
    public static (double canto, double vizinha, bool emL) BitolasDoCanto(ValoresTemplate v)
    {
        var l = LerLongitudinais(v, 0, 1e9, new List<string>());
        double pc = l.Where(x => x.Arranjo == ArrCantos).Select(x => x.Bitola).DefaultIfEmpty(10).Max();
        var viz = l.Where(x => x.Arranjo == ArrAoLado).ToList();
        return (pc, viz.Select(x => x.Bitola).DefaultIfEmpty(pc).Max(), viz.Count > 0);
    }

    // ======================== posições na seção (testável) ========================

    /// <summary><see cref="SecaoBarras.RaioFeixe"/>.</summary>
    public static double RaioFeixe(double bitola, int feixe) => SecaoBarras.RaioFeixe(bitola, feixe);

    /// <summary><see cref="SecaoBarras.CentrosFeixe"/>.</summary>
    public static List<(double X, double Y)> CentrosFeixe(double bitola, int feixe) => SecaoBarras.CentrosFeixe(bitola, feixe);

    /// <summary>
    /// Posições (centro do feixe) de cada linha na seção. Cobrimentos por face (1 = +x, 2 = −y, 3 = −x, 4 = +y) até o estribo;
    /// φt = maior estribo (mm). As barras de um arranjo ficam alinhadas pelo maior feixe do arranjo (a posição não muda com a
    /// altura); as de face ficam entre as do "L" (se houver) ou entre os cantos, divididas por igual.
    /// </summary>
    public static Dictionary<int, List<BarraNaSecao>> PosicoesLinhas(double b, double h, double[] cob, double phiT,
        IReadOnlyList<LinhaLongitudinal> linhas, double sCanto)
    {
        double t = phiT / 10;
        double R(string arr, double padrao) => linhas.Where(l => l.Arranjo == arr).Select(l => RaioFeixe(l.Bitola, l.Feixe)).DefaultIfEmpty(padrao).Max();
        double rc = R(ArrCantos, 0.5);
        double xl = cob[2] + t + rc, xr = b - cob[0] - t - rc, yb = cob[1] + t + rc, yt = h - cob[3] - t - rc;
        bool emL = linhas.Any(l => l.Arranjo == ArrAoLado);
        double ax = emL ? xl + sCanto : xl, bx = emL ? xr - sCanto : xr, ay = emL ? yb + sCanto : yb, by = emL ? yt - sCanto : yt;
        var r = new Dictionary<int, List<BarraNaSecao>>();
        foreach (var l in linhas)
        {
            var p = new List<(double X, double Y)>();
            double rr = R(l.Arranjo, RaioFeixe(l.Bitola, l.Feixe));
            double xEsq = cob[2] + t + rr, xDir = b - cob[0] - t - rr, yBai = cob[1] + t + rr, yCim = h - cob[3] - t - rr;
            IEnumerable<double> Divisoes(double a, double c, int n) => Enumerable.Range(1, n).Select(k => a + k * (c - a) / (n + 1));
            switch (l.Arranjo)
            {
                case ArrCantos: p.AddRange(new[] { (xl, yb), (xr, yb), (xr, yt), (xl, yt) }); break;
                case ArrAoLado:
                    p.AddRange(new[] { (xl + sCanto, yBai), (xEsq, yb + sCanto), (xr - sCanto, yBai), (xDir, yb + sCanto),
                                       (xr - sCanto, yCim), (xDir, yt - sCanto), (xl + sCanto, yCim), (xEsq, yt - sCanto) });
                    break;
                case ArrFaces24: foreach (var x in Divisoes(ax, bx, l.PorFace)) { p.Add((x, yBai)); p.Add((x, yCim)); } break;
                case ArrFace2: p.AddRange(Divisoes(ax, bx, l.PorFace).Select(x => (x, yBai))); break;
                case ArrFace4: p.AddRange(Divisoes(ax, bx, l.PorFace).Select(x => (x, yCim))); break;
                case ArrFaces13: foreach (var y in Divisoes(ay, by, l.PorFace)) { p.Add((xEsq, y)); p.Add((xDir, y)); } break;
                // "Face 1" = oeste (−x, à esquerda da seção) e "Face 3" = leste (+x): numeração apresentada (2026-11-11)
                case ArrFace1: p.AddRange(Divisoes(ay, by, l.PorFace).Select(y => (xEsq, y))); break;
                case ArrFace3: p.AddRange(Divisoes(ay, by, l.PorFace).Select(y => (xDir, y))); break;
            }
            r[l.Linha] = p.Select(q => new BarraNaSecao(q.X, q.Y, l.Bitola, l.Chave, l.Feixe, l.Arranjo)).ToList();
        }
        return r;
    }
}
