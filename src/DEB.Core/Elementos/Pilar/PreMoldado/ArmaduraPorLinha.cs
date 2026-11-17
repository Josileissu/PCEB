using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Documentacao;
using DEB.Core.Geometria.Secao;
using DEB.Core.Modelo;
using DEB.Core.Motor;

namespace DEB.Core.Elementos.Pilar.PreMoldado;

/// <summary>Uma linha da armadura por linha: arranjo na seção, bitola, feixe e o trecho do pilar que ela ocupa.</summary>
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

/// <summary>
/// Uma linha da armadura por linha como o usuário digita (as colunas da tabela "Longitudinais" do CAD): arranjo, barras por face,
/// bitola (mm), feixe e início e fim (cm a partir da base da peça; vazio = a peça toda). É o que o Revit grava nos modelos de
/// armadura; vira <see cref="LinhaLongitudinal"/> pela mesma leitura do CAD (<see cref="ArmaduraPorLinha.Ler"/> sobre <see cref="Celulas"/>).
/// </summary>
public sealed class LinhaArmaduraPorLinha
{
    public string Arranjo { get; set; } = ArmaduraPorLinha.Cantos;
    /// <summary>Barras por face (arranjos de face); ignorado nos cantos.</summary>
    public int PorFace { get; set; } = 1;
    public double Bitola { get; set; } = 12.5;
    /// <summary>Barras por feixe (1, 2 ou 3).</summary>
    public int Feixe { get; set; } = 1;
    /// <summary>Início, cm a partir da base da peça; null = desde a base (menos o cobrimento da ponta).</summary>
    public double? Ini { get; set; }
    /// <summary>Fim, cm a partir da base da peça; null = até o topo (menos o cobrimento da ponta).</summary>
    public double? Fim { get; set; }

    public LinhaArmaduraPorLinha Copia() => (LinhaArmaduraPorLinha)MemberwiseClone();

    /// <summary>As células na ordem das colunas do template (números com ponto; cantos sem barras por face).</summary>
    public string[] Celulas()
    {
        static string N(double x) => x.ToString("0.###", CultureInfo.InvariantCulture);
        return new[]
        {
            Arranjo ?? "", ArmaduraPorLinha.ArranjoDeCanto(Arranjo) ? "" : PorFace.ToString(CultureInfo.InvariantCulture), N(Bitola),
            Feixe.ToString(CultureInfo.InvariantCulture), Ini.HasValue ? N(Ini.Value) : "", Fim.HasValue ? N(Fim.Value) : "",
        };
    }
}

/// <summary>
/// Armadura por linha: as barras longitudinais digitadas em linhas — cada linha é um arranjo na seção (cantos, ao lado dos
/// cantos, faces), barras por face, bitola, feixe (1 a 3) e início e fim ao longo da peça. Permite bitolas mistas, feixes e
/// redução com a altura (linhas do mesmo arranjo que se sobrepõem formam uma emenda). Uma só regra para o template do pilar do
/// CAD (tabela "Longitudinais") e para o modelo 3D do Revit (D32). Saiu do template do pilar em 17/11/2026 sem mudar a conta;
/// o template mantém os atalhos (<see cref="TemplatePilarPreMoldado.LerLongitudinais"/>, <see cref="TemplatePilarPreMoldado.PosicoesLinhas"/>).
/// </summary>
public static class ArmaduraPorLinha
{
    public const string Cantos = "Cantos", AoLado = "Ao lado dos cantos", Faces13 = "Faces 1 e 3", Faces24 = "Faces 2 e 4",
        Face1 = "Face 1", Face2 = "Face 2", Face3 = "Face 3", Face4 = "Face 4";
    /// <summary>Os arranjos, na ordem da lista de opções (mesmos nomes no CAD e no Revit).</summary>
    public static readonly string[] Arranjos = { Cantos, AoLado, Faces24, Faces13, Face1, Face2, Face3, Face4 };

    /// <summary>Cantos (4 barras) e "ao lado dos cantos" (8, em "L"): quantidade fixa, sem barras por face.</summary>
    public static bool ArranjoDeCanto(string a) => a == Cantos || a == AoLado;

    /// <summary>
    /// Linhas equivalentes à armadura simples de um pilar retangular — uma bitola, <paramref name="barrasFaceX"/> barras em cada
    /// face paralela a x e <paramref name="barrasFaceY"/> em cada face paralela a y, contando os cantos (a especificação antiga
    /// do Revit): cantos + "Faces 2 e 4" (as intermediárias das faces paralelas a x) + "Faces 1 e 3" (as das faces paralelas a y);
    /// a peça toda, sem feixe. Face sem intermediária não vira linha.
    /// </summary>
    public static List<LinhaArmaduraPorLinha> DaArmaduraSimples(double bitola, int barrasFaceX, int barrasFaceY)
    {
        var r = new List<LinhaArmaduraPorLinha> { new LinhaArmaduraPorLinha { Arranjo = Cantos, PorFace = 0, Bitola = bitola } };
        int nx = Math.Max(2, barrasFaceX) - 2, ny = Math.Max(2, barrasFaceY) - 2;
        if (nx > 0) r.Add(new LinhaArmaduraPorLinha { Arranjo = Faces24, PorFace = nx, Bitola = bitola });
        if (ny > 0) r.Add(new LinhaArmaduraPorLinha { Arranjo = Faces13, PorFace = ny, Bitola = bitola });
        return r;
    }

    // ======================== leitura (testável) ========================

    /// <summary>A mesma leitura de <see cref="Ler(IReadOnlyList{string[]}, double, double, List{string})"/> para as linhas digitadas.</summary>
    public static List<LinhaLongitudinal> Ler(IEnumerable<LinhaArmaduraPorLinha> linhas, double yIni, double yFim, List<string> avisos) =>
        Ler((linhas ?? Enumerable.Empty<LinhaArmaduraPorLinha>()).Select(l => l.Celulas()).ToList(), yIni, yFim, avisos);

    /// <summary>
    /// Linhas válidas a partir das células digitadas (arranjo, barras por face, bitola, feixe, início, fim — a ordem das colunas do
    /// template), com o trecho limitado a [yIni, yFim] (base e topo menos os cobrimentos; início/fim vazios = o trecho todo).
    /// </summary>
    public static List<LinhaLongitudinal> Ler(IReadOnlyList<string[]> linhas, double yIni, double yFim, List<string> avisos)
    {
        var r = new List<LinhaLongitudinal>();
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
            if (fim - ini < 10) { avisos.Add($"Longitudinal {k}: trecho de {KitDesenho.Num(ini, "0.#")} a {KitDesenho.Num(fim, "0.#")} cm curto demais."); continue; }
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

    // ======================== posições na seção (testável) ========================

    /// <summary>
    /// Posições de cada linha pela seção do contrato (D28) e cobrimentos por parte nomeada (Topo, Fundo, Esquerda, Direita, até
    /// o estribo), como <see cref="SecaoBarras.Posicoes(SecaoTransversal, IReadOnlyDictionary{ParteSecao, double}, double, double, int, double, double, int, int, double, string, Configuracao.ConfiguracaoArmadura)"/>.
    /// Retangular: exatamente <see cref="Posicoes(double, double, double[], double, IReadOnlyList{LinhaLongitudinal}, double)"/>
    /// (cobrimentos convertidos pela <see cref="FacesRetangulo"/>). Outras famílias e seção medida: sem posições e com
    /// <see cref="PosicoesPorLinha.Erro"/> — a armadura por linha ainda não foi definida para elas (o arranjo por família da D31
    /// não é combinado com as linhas).
    /// </summary>
    public static PosicoesPorLinha Posicoes(SecaoTransversal secao, IReadOnlyDictionary<ParteSecao, double> cobrimentos, double phiT,
        IReadOnlyList<LinhaLongitudinal> linhas, double sCanto)
    {
        if (secao == null) throw new ArgumentNullException(nameof(secao));
        if (secao.Familia != FamiliasSecao.Retangular)
            return new PosicoesPorLinha(null, $"Armadura por linha não definida para esta família ({secao.Familia.Nome}).");
        double[] cob;
        try { cob = FacesRetangulo.CobrimentosPorFace(cobrimentos); }
        catch (ArgumentException e) { return new PosicoesPorLinha(null, e.Message); }
        return new PosicoesPorLinha(Posicoes(secao.Valor("b"), secao.Valor("h"), cob, phiT, linhas, sCanto), null);
    }

    /// <summary>
    /// Posições (centro do feixe) de cada linha na seção retangular b × h. Cobrimentos por face (1 = +x, 2 = −y, 3 = −x, 4 = +y)
    /// até o estribo; φt = maior estribo (mm). As barras de um arranjo ficam alinhadas pelo maior feixe do arranjo (a posição não
    /// muda com a altura); as de face ficam entre as do "L" (se houver) ou entre os cantos, divididas por igual.
    /// </summary>
    public static Dictionary<int, List<BarraNaSecao>> Posicoes(double b, double h, double[] cob, double phiT,
        IReadOnlyList<LinhaLongitudinal> linhas, double sCanto)
    {
        double t = phiT / 10;
        double R(string arr, double padrao) => linhas.Where(l => l.Arranjo == arr).Select(l => SecaoBarras.RaioFeixe(l.Bitola, l.Feixe)).DefaultIfEmpty(padrao).Max();
        double rc = R(Cantos, 0.5);
        double xl = cob[2] + t + rc, xr = b - cob[0] - t - rc, yb = cob[1] + t + rc, yt = h - cob[3] - t - rc;
        bool emL = linhas.Any(l => l.Arranjo == AoLado);
        double ax = emL ? xl + sCanto : xl, bx = emL ? xr - sCanto : xr, ay = emL ? yb + sCanto : yb, by = emL ? yt - sCanto : yt;
        var r = new Dictionary<int, List<BarraNaSecao>>();
        foreach (var l in linhas)
        {
            var p = new List<(double X, double Y)>();
            double rr = R(l.Arranjo, SecaoBarras.RaioFeixe(l.Bitola, l.Feixe));
            double xEsq = cob[2] + t + rr, xDir = b - cob[0] - t - rr, yBai = cob[1] + t + rr, yCim = h - cob[3] - t - rr;
            IEnumerable<double> Divisoes(double a, double c, int n) => Enumerable.Range(1, n).Select(k => a + k * (c - a) / (n + 1));
            switch (l.Arranjo)
            {
                case Cantos: p.AddRange(new[] { (xl, yb), (xr, yb), (xr, yt), (xl, yt) }); break;
                case AoLado:
                    p.AddRange(new[] { (xl + sCanto, yBai), (xEsq, yb + sCanto), (xr - sCanto, yBai), (xDir, yb + sCanto),
                                       (xr - sCanto, yCim), (xDir, yt - sCanto), (xl + sCanto, yCim), (xEsq, yt - sCanto) });
                    break;
                case Faces24: foreach (var x in Divisoes(ax, bx, l.PorFace)) { p.Add((x, yBai)); p.Add((x, yCim)); } break;
                case Face2: p.AddRange(Divisoes(ax, bx, l.PorFace).Select(x => (x, yBai))); break;
                case Face4: p.AddRange(Divisoes(ax, bx, l.PorFace).Select(x => (x, yCim))); break;
                case Faces13: foreach (var y in Divisoes(ay, by, l.PorFace)) { p.Add((xEsq, y)); p.Add((xDir, y)); } break;
                // "Face 1" = oeste (−x, à esquerda da seção) e "Face 3" = leste (+x): numeração apresentada (2026-11-11)
                case Face1: p.AddRange(Divisoes(ay, by, l.PorFace).Select(y => (xEsq, y))); break;
                case Face3: p.AddRange(Divisoes(ay, by, l.PorFace).Select(y => (xDir, y))); break;
            }
            r[l.Linha] = p.Select(q => new BarraNaSecao(q.X, q.Y, l.Bitola, l.Chave, l.Feixe, l.Arranjo)).ToList();
        }
        return r;
    }
}

/// <summary>Posições da armadura por linha numa seção: as barras de cada linha (pelo número da linha), ou o motivo de não haver.</summary>
public sealed class PosicoesPorLinha
{
    internal PosicoesPorLinha(Dictionary<int, List<BarraNaSecao>> porLinha, string erro)
    {
        PorLinha = porLinha ?? new Dictionary<int, List<BarraNaSecao>>(); Erro = erro;
    }
    /// <summary>Barras (centro do feixe) de cada linha, pelo número da linha; vazio se <see cref="Definido"/> é falso.</summary>
    public Dictionary<int, List<BarraNaSecao>> PorLinha { get; }
    /// <summary>Motivo de não haver posições (família sem armadura por linha, cobrimento faltando); null se há.</summary>
    public string Erro { get; }
    public bool Definido => Erro == null;
}