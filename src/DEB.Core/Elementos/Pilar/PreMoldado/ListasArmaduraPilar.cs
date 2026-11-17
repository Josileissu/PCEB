using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Modelo;
using DEB.Core.Motor;
using DEB.Core.Documentacao;
using DEB.Core.Elementos.Consolo;

namespace DEB.Core.Elementos.Pilar.PreMoldado;

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
    // arranjos da armadura por linha (ArmaduraPorLinha.cs); os nomes daqui ficam como atalhos
    public const string ArrCantos = ArmaduraPorLinha.Cantos, ArrAoLado = ArmaduraPorLinha.AoLado, ArrFaces13 = ArmaduraPorLinha.Faces13,
        ArrFaces24 = ArmaduraPorLinha.Faces24, ArrFace1 = ArmaduraPorLinha.Face1, ArrFace2 = ArmaduraPorLinha.Face2,
        ArrFace3 = ArmaduraPorLinha.Face3, ArrFace4 = ArmaduraPorLinha.Face4;
    public static readonly string[] Arranjos = ArmaduraPorLinha.Arranjos;
    private static readonly string[] BitolasLong = { "10", "12.5", "16", "20", "22", "25", "32", "40" };
    private static readonly string[] BitolasEst = { "5", "6.3", "8", "10", "12.5", "16" };

    private static bool ArranjoDeCanto(string a) => ArmaduraPorLinha.ArranjoDeCanto(a);

    public static string NormalizarBitola(string s) => KitDesenho.NormalizarBitola(s);

    private static ParametroTemplate ParametroLongitudinais() => new ParametroTemplate
    {
        Grupo = G7, Nome = "Longitudinais", Rotulo = "Armadura por linha (longitudinais)", Tipo = TipoParametro.Tabela, NomeItem = "linha",
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

    /// <summary>
    /// Linhas válidas da lista de longitudinais, com o trecho limitado a [yIni, yFim] (base e topo menos os cobrimentos):
    /// atalho para <see cref="ArmaduraPorLinha.Ler"/> sobre a tabela "Longitudinais".
    /// </summary>
    public static List<LinhaLongitudinal> LerLongitudinais(ValoresTemplate v, double yIni, double yFim, List<string> avisos) =>
        ArmaduraPorLinha.Ler(v.Tabela("Longitudinais"), yIni, yFim, avisos);

    /// <summary>
    /// Zonas de estribos em ordem, cobrindo [yIni, yFim]: as da lista e o trecho corrente nos vãos (<see cref="MontarZonas"/>:
    /// onde se sobrepõem, vale o menor espaçamento).
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
        var corrente = new ZonaEstribo { Nome = "corrente", Bitola = v.Numero("EstBitola"), Esp = v.Numero("EstEsp"), Grampos = v.SimNao("GramposCorrente") };
        return MontarZonas(lista, yIni, yFim, corrente, avisos);
    }

    /// <summary>
    /// Zonas de estribos em ordem, cobrindo [yIni, yFim] sem sobreposição — regra única do template do CAD (<see cref="LerZonas"/>)
    /// e do modelo 3D do Revit (<see cref="Modelo3D.ColumnRebarLayout.StirrupRuns"/>). Onde zonas da lista se sobrepõem vale o
    /// MENOR espaçamento (decisão do dono, D30, 16/11/2026; empate: a primeira da lista); nos vãos, o trecho corrente
    /// (<paramref name="corrente"/>: bitola, espaçamento e grampos, sem trecho). Pedaços vizinhos da mesma zona ficam juntos.
    /// </summary>
    public static List<ZonaEstribo> MontarZonas(IEnumerable<ZonaEstribo> lista, double yIni, double yFim, ZonaEstribo corrente, List<string> avisos)
    {
        var zs = lista.Select(z => new ZonaEstribo { Nome = z.Nome, Y1 = Math.Max(yIni, z.Y1), Y2 = Math.Min(yFim, z.Y2), Bitola = z.Bitola, Esp = z.Esp, Grampos = z.Grampos })
            .Where(z => z.Y2 - z.Y1 >= 1).ToList();
        for (int i = 0; i < zs.Count; i++)
            for (int j = i + 1; j < zs.Count; j++)
                if (zs[i].Y1 < zs[j].Y2 - 1e-6 && zs[j].Y1 < zs[i].Y2 - 1e-6)
                    avisos?.Add($"Zonas de estribos \"{zs[i].Nome}\" e \"{zs[j].Nome}\" se sobrepõem: vale o menor espaçamento.");
        var cortes = new SortedSet<double> { yIni, yFim };
        foreach (var z in zs) { cortes.Add(z.Y1); cortes.Add(z.Y2); }
        var c = cortes.Where(y => y >= yIni - 1e-9 && y <= yFim + 1e-9).ToArray();
        var r = new List<ZonaEstribo>();
        ZonaEstribo origemAnterior = null;
        for (int i = 0; i + 1 < c.Length; i++)
        {
            if (c[i + 1] - c[i] < 1e-6) continue;
            double meio = (c[i] + c[i + 1]) / 2;
            // a zona de menor espaçamento que cobre o trecho (empate: a primeira da lista); nenhuma = corrente
            ZonaEstribo origem = zs.Where(z => z.Y1 < meio && z.Y2 > meio).OrderBy(z => z.Esp).FirstOrDefault() ?? corrente;
            if (origem == origemAnterior && r.Count > 0 && Math.Abs(r[r.Count - 1].Y2 - c[i]) < 1e-6) { r[r.Count - 1].Y2 = c[i + 1]; continue; }
            r.Add(new ZonaEstribo { Nome = origem.Nome, Y1 = c[i], Y2 = c[i + 1], Bitola = origem.Bitola, Esp = origem.Esp, Grampos = origem.Grampos });
            origemAnterior = origem;
        }
        return r;
    }

    /// <summary>
    /// Fim da faixa de estribos de uma zona no desenho do CAD: o fim da zona, ou um espaçamento antes quando o último estribo
    /// repetiria o primeiro da zona seguinte (<see cref="KitDesenho.RepeteNaDivisa"/>, D30). A faixa do CAD conta os estribos
    /// pelo comprimento desenhado; com ela encurtada, conta <see cref="KitDesenho.EstribosNaZona"/>.
    /// </summary>
    public static double FimDaFaixa(ZonaEstribo z, bool ultima, Configuracao.ConfiguracaoArmadura cfg) =>
        KitDesenho.FimSemRepetir(z.Y1, z.Y2, z.Esp, ultima, cfg);

    /// <summary>Maior bitola de estribo (corrente e zonas): define a posição das longitudinais.</summary>
    public static double BitolaMaxEstribo(ValoresTemplate v) =>
        v.Tabela("ZonasEstribos").Select(l => ValoresTemplate.NumeroCelula(l, 4)).Concat(new[] { v.Numero("EstBitola") }).Max();

    /// <summary>
    /// Maior bitola longitudinal da lista, em mm (feixe: o diâmetro do envoltório; 10 sem lista): com o maior estribo, define
    /// onde o tirante e a costura do consolo param dentro do pilar (<see cref="BarrasConsolo.FimNoPilar"/>; a maior, a favor
    /// da segurança contra a colisão).
    /// </summary>
    public static double BitolaMaxLongitudinal(ValoresTemplate v) =>
        LerLongitudinais(v, 0, 1e9, new List<string>()).Select(l => 20 * RaioFeixe(l.Bitola, l.Feixe)).DefaultIfEmpty(10).Max();

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
    /// Posições (centro do feixe) de cada linha na seção b × h (cobrimentos por face 1 = +x, 2 = −y, 3 = −x, 4 = +y; φt em mm):
    /// atalho para <see cref="ArmaduraPorLinha.Posicoes(double, double, double[], double, IReadOnlyList{LinhaLongitudinal}, double)"/>.
    /// </summary>
    public static Dictionary<int, List<BarraNaSecao>> PosicoesLinhas(double b, double h, double[] cob, double phiT,
        IReadOnlyList<LinhaLongitudinal> linhas, double sCanto) =>
        ArmaduraPorLinha.Posicoes(b, h, cob, phiT, linhas, sCanto);
}
