using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Normas.Verificacao;

/// <summary>Função de uma barra longitudinal da viga na seção (para as verificações).</summary>
public enum FuncaoBarraViga { Inferior, Superior, Pele, Construtiva }

/// <summary>Uma barra longitudinal na seção da viga: centro (cm, origem no canto inferior esquerdo da alma), bitola (mm), função.</summary>
public sealed class BarraSecaoViga
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Bitola { get; set; }
    public FuncaoBarraViga Funcao { get; set; }
    public double Area => Math.PI * Bitola * Bitola / 400;   // cm²
}

/// <summary>Dados de uma viga para as verificações de detalhamento (cm e mm como indicado), do que o desenho tem.</summary>
public sealed class DadosViga
{
    /// <summary>Largura da alma e altura da viga, cm.</summary>
    public double B { get; set; }
    public double H { get; set; }
    /// <summary>Altura útil, cm (até o centro da armadura inferior).</summary>
    public double D { get; set; }
    public double FckMPa { get; set; } = 40;
    public string AcoEstribo { get; set; } = "CA-50";
    /// <summary>Dimensão máxima do agregado, mm (0 = não informado).</summary>
    public double AgregadoMm { get; set; }
    /// <summary>Viga protendida: a armadura ativa conta na armadura mínima (V1 fica "Conferir").</summary>
    public bool Protendida { get; set; }
    /// <summary>Seção com aba (L / T invertido): a Tabela 17.3 é de seção retangular (V1 fica "Conferir").</summary>
    public bool ComAba { get; set; }
    /// <summary>Estribo: bitola (mm), ramos verticais e distância entre os ramos (eixo a eixo, cm).</summary>
    public double EstBitola { get; set; }
    public int EstRamos { get; set; } = 2;
    public double EstDistRamos { get; set; }
    /// <summary>Espaçamentos das zonas de estribos (nome da zona, cm).</summary>
    public List<(string Zona, double Esp)> Zonas { get; set; } = new List<(string, double)>();
    /// <summary>Vd/VRd2 do cálculo (D40); null = sem cálculo (os limites que dependem do Vd ficam "Conferir").</summary>
    public double? VdSobreVRd2 { get; set; }
}

/// <summary>
/// Regras V* da viga pré-moldada (NBR 6118:2023, 17.3.5.2, 17.4.1.1.1, 18.3), sem esforços (D19/D36): o que dá para decidir com a
/// geometria e a armadura desenhada. Os limites que dependem do Vd saem como "Conferir" com o limite mais rigoroso (D36, resposta 2).
/// V9 (ligação mesa-alma), V10 (suspensão de viga apoiada em viga) e V12 (apoio sem recorte) precisam de dados que o template não
/// tem (mesa colaborante, vigas apoiadas, aparelho de apoio) e ficam de fora.
/// </summary>
public static class VerificacoesViga
{
    private static string N(double x, string f = "0.#") => Fmt.Virgula(x, f);

    private static ResultadoVerificacao R(string cod, string grupo, string desc, string valor, string limite, bool falha, string refer,
                                          SituacaoVerificacao seFalha = SituacaoVerificacao.NaoAtende, string dica = "") =>
        new ResultadoVerificacao
        {
            Codigo = cod, Grupo = grupo, Descricao = desc, Valor = valor, Limite = limite, Referencia = refer, Dica = dica,
            Situacao = falha ? seFalha : SituacaoVerificacao.Ok,
        };

    /// <summary>V1: ρ = As,inferior/(b·h) ≥ ρmín da Tabela 17.3 (no meio do vão).</summary>
    public static ResultadoVerificacao ArmaduraMinima(DadosViga v, double asInfCm2)
    {
        double rho = asInfCm2 / (v.B * v.H) * 100, min = TabelasNbr.RhoMinVigaPct(v.FckMPa);
        bool conferir = v.Protendida || v.ComAba;
        return R("V1", "Longitudinal", $"Armadura mínima de flexão (inferior no meio do vão, C{N(v.FckMPa, "0")})", $"ρ = {N(rho, "0.000")} %",
                 $"≥ {N(min, "0.000")} %", rho < min - 1e-9, "NBR 6118, 17.3.5.2.1, Tab. 17.3",
                 conferir ? SituacaoVerificacao.Conferir : SituacaoVerificacao.NaoAtende,
                 v.Protendida ? "Só a armadura passiva: a ativa também conta na armadura mínima."
                 : v.ComAba ? "Seção com aba: a Tabela 17.3 é de seção retangular (Ac = alma b·h)." : "");
    }

    /// <summary>V2: As + As' ≤ 4 % Ac (alma b·h), fora das emendas (a viga não tem emendas).</summary>
    public static ResultadoVerificacao ArmaduraMaxima(DadosViga v, double asTotalCm2, string onde = "")
    {
        double rho = asTotalCm2 / (v.B * v.H) * 100;
        return R("V2", "Longitudinal", "Armadura longitudinal máxima (tração + compressão)" + (onde.Length > 0 ? $" ({onde})" : ""),
                 $"{N(rho, "0.00")} %", "≤ 4 %", rho > 4 + 1e-9, "NBR 6118, 17.3.5.2.4");
    }

    /// <summary>
    /// V3: h &gt; 60 cm → pele em cada face com 0,10 % da alma (até 5 cm²/m de face), espaçamento ≤ mín(20 cm; d/3). As barras da face
    /// são as da coluna junto à face esquerda (pele e cantos); o espaçamento é a maior distância entre elas na altura.
    /// </summary>
    public static IEnumerable<ResultadoVerificacao> Pele(DadosViga v, IReadOnlyList<BarraSecaoViga> barras)
    {
        if (v.H <= 60 + 1e-9) yield break;
        var pele = barras.Where(b => b.Funcao == FuncaoBarraViga.Pele).ToList();
        double xFace = pele.Count > 0 ? pele.Min(b => b.X) : 0;
        var naFace = pele.Where(b => b.X <= xFace + 0.5).ToList();
        double asFace = naFace.Sum(b => b.Area), min = Math.Min(0.001 * v.B * v.H, 5 * v.H / 100);
        yield return R("V3", "Longitudinal", "Armadura de pele por face (h > 60 cm)", $"{N(asFace, "0.00")} cm²",
                       $"≥ {N(min, "0.00")} cm² (0,10 % b·h, até 5 cm²/m)", asFace < min - 1e-9, "NBR 6118, 17.3.5.2.3",
                       dica: pele.Count == 0 ? "Viga sem armadura de pele." : "");
        if (naFace.Count == 0) yield break;
        // a coluna da face: pele e as barras de cima e de baixo junto à mesma face
        var ys = barras.Where(b => Math.Abs(b.X - xFace) <= Math.Max(1, b.Bitola / 10)).Select(b => b.Y).Concat(naFace.Select(b => b.Y))
            .Distinct().OrderBy(y => y).ToList();
        double s = ys.Zip(ys.Skip(1), (a, b) => b - a).DefaultIfEmpty(0).Max(), sMax = Math.Min(20, v.D / 3);
        yield return R("V3", "Longitudinal", "Espaçamento vertical da armadura de pele", $"{N(s)} cm", $"≤ mín(20 cm; d/3) = {N(sMax)} cm",
                       s > sMax + 1e-6, "NBR 6118, 17.3.5.2.3 e 18.3.5");
    }

    /// <summary>
    /// V4: espaçamento livre entre as longitudinais (fora a pele): na mesma camada (horizontal) ≥ máx(20 mm; φ; 1,2·dmáx); entre
    /// camadas (vertical) ≥ máx(20 mm; φ; 0,5·dmáx). Camada = barras na mesma altura.
    /// </summary>
    public static IEnumerable<ResultadoVerificacao> EspacamentoLivre(DadosViga v, IReadOnlyList<BarraSecaoViga> barras, string onde = "")
    {
        var lst = barras.Where(b => b.Funcao != FuncaoBarraViga.Pele).ToList();
        var camadas = new List<List<BarraSecaoViga>>();
        foreach (var b in lst.OrderBy(b => b.Y))
        {
            var cam = camadas.FirstOrDefault(k => Math.Abs(k[0].Y - b.Y) < 0.5);
            if (cam == null) camadas.Add(new List<BarraSecaoViga> { b }); else cam.Add(b);
        }
        string sfx = onde.Length > 0 ? $" ({onde})" : "";
        double livreH = double.MaxValue, phiH = 0;
        foreach (var k in camadas)
        {
            var o = k.OrderBy(b => b.X).ToList();
            for (int i = 0; i + 1 < o.Count; i++)
            {
                double l = (o[i + 1].X - o[i].X - o[i].Bitola / 20 - o[i + 1].Bitola / 20) * 10;
                if (l < livreH) { livreH = l; phiH = Math.Max(o[i].Bitola, o[i + 1].Bitola); }
            }
        }
        if (livreH < double.MaxValue)
        {
            double min = Math.Max(20, Math.Max(phiH, 1.2 * v.AgregadoMm));
            yield return R("V4", "Longitudinal", "Espaçamento livre horizontal entre as longitudinais" + sfx, $"{N(livreH)} mm",
                           $"≥ {N(min)} mm = máx(20 mm; φ; 1,2·dmáx)", livreH < min - 1e-6, "NBR 6118, 18.3.2.2",
                           dica: livreH < 0 ? "Barras sobrepostas na mesma camada: use a segunda camada ou outra quantidade." : "");
        }
        // vertical: camadas vizinhas da mesma metade (inferiores entre si, superiores entre si)
        double livreV = double.MaxValue, phiV = 0;
        foreach (var metade in new[] { camadas.Where(k => k[0].Y < v.H / 2).ToList(), camadas.Where(k => k[0].Y >= v.H / 2).ToList() })
            for (int i = 0; i + 1 < metade.Count; i++)
            {
                double ra = metade[i].Max(b => b.Bitola) / 20, rb = metade[i + 1].Max(b => b.Bitola) / 20;
                double l = (metade[i + 1][0].Y - metade[i][0].Y - ra - rb) * 10;
                if (l < livreV) { livreV = l; phiV = 20 * Math.Max(ra, rb); }
            }
        if (livreV < double.MaxValue)
        {
            double min = Math.Max(20, Math.Max(phiV, 0.5 * v.AgregadoMm));
            yield return R("V4", "Longitudinal", "Espaçamento livre vertical entre camadas" + sfx, $"{N(livreV)} mm",
                           $"≥ {N(min)} mm = máx(20 mm; φ; 0,5·dmáx)", livreV < min - 1e-6, "NBR 6118, 18.3.2.2");
        }
    }

    /// <summary>V5–V8: bitola, espaçamento, distância entre ramos e taxa mínima dos estribos.</summary>
    public static IEnumerable<ResultadoVerificacao> Estribos(DadosViga v)
    {
        const string G = "Estribos";
        double phi = v.EstBitola, bwMm = v.B * 10;
        // V5: 5 mm ≤ φt ≤ bw/10
        yield return phi < 5 - 1e-6
            ? R("V5", G, "Bitola do estribo", $"ø{N(phi)}", "≥ 5 mm", true, "NBR 6118, 18.3.3.2")
            : R("V5", G, "Bitola do estribo", $"ø{N(phi)}", $"≤ bw/10 = {N(bwMm / 10)} mm", phi > bwMm / 10 + 1e-6, "NBR 6118, 18.3.3.2");
        // V6: smáx = 0,6d ≤ 300 mm (Vd ≤ 0,67 VRd2) ou 0,3d ≤ 200 mm: sem Vd o mais rigoroso é "Conferir"
        double sRig = Math.Min(0.3 * v.D, 20), sMax = Math.Min(0.6 * v.D, 30);
        if (v.VdSobreVRd2 is double rel)
        {
            // com o cálculo (D40) o limite é decidido pelo Vd: sem "Conferir"
            bool alto = rel > 0.67 + 1e-9;
            double lim = alto ? sRig : sMax;
            foreach (var (zona, s) in v.Zonas.GroupBy(z => Math.Round(z.Esp, 3)).Select(g => g.First()))
                yield return R("V6", G, $"Espaçamento dos estribos ({zona})", $"{N(s)} cm",
                               alto ? $"≤ 0,3d ≤ 20 cm = {N(sRig)} cm (Vd/VRd2 = {N(rel, "0.00")} > 0,67)" : $"≤ 0,6d ≤ 30 cm = {N(sMax)} cm (Vd/VRd2 = {N(rel, "0.00")} ≤ 0,67)",
                               s > lim + 1e-6, "NBR 6118, 18.3.3.2");
            if (v.EstRamos >= 2 && v.EstDistRamos > 0)
            {
                bool alto7 = rel > 0.20 + 1e-9;
                double lim7 = alto7 ? Math.Min(0.6 * v.D, 35) : Math.Min(v.D, 80);
                yield return R("V7", G, "Distância transversal entre ramos do estribo", $"{N(v.EstDistRamos)} cm",
                               alto7 ? $"≤ 0,6d ≤ 35 cm = {N(lim7)} cm (Vd/VRd2 > 0,20)" : $"≤ d ≤ 80 cm = {N(lim7)} cm (Vd/VRd2 ≤ 0,20)",
                               v.EstDistRamos > lim7 + 1e-6, "NBR 6118, 18.3.3.2");
            }
        }
        else
        foreach (var (zona, s) in v.Zonas.GroupBy(z => Math.Round(z.Esp, 3)).Select(g => g.First()))
        {
            bool nao = s > sMax + 1e-6, conf = s > sRig + 1e-6;
            yield return R("V6", G, $"Espaçamento dos estribos ({zona})", $"{N(s)} cm",
                           nao ? $"≤ 0,6d ≤ 30 cm = {N(sMax)} cm" : $"≤ 0,3d ≤ 20 cm = {N(sRig)} cm (Vd > 0,67·VRd2)", nao || conf, "NBR 6118, 18.3.3.2",
                           nao ? SituacaoVerificacao.NaoAtende : SituacaoVerificacao.Conferir,
                           conf && !nao ? $"Atende 0,6d ≤ 30 cm = {N(sMax)} cm, que vale para Vd ≤ 0,67·VRd2." : "");
        }
        // V7: distância transversal entre ramos: 0,6d ≤ 350 mm (Vd > 0,20 VRd2, Conferir) ou d ≤ 800 mm
        if (v.VdSobreVRd2 == null && v.EstRamos >= 2 && v.EstDistRamos > 0)
        {
            double st = v.EstDistRamos, rig = Math.Min(0.6 * v.D, 35), max = Math.Min(v.D, 80);
            bool nao = st > max + 1e-6, conf = st > rig + 1e-6;
            yield return R("V7", G, "Distância transversal entre ramos do estribo", $"{N(st)} cm",
                           nao ? $"≤ d ≤ 80 cm = {N(max)} cm" : $"≤ 0,6d ≤ 35 cm = {N(rig)} cm (Vd > 0,20·VRd2)", nao || conf, "NBR 6118, 18.3.3.2",
                           nao ? SituacaoVerificacao.NaoAtende : SituacaoVerificacao.Conferir,
                           conf && !nao ? $"Atende d ≤ 80 cm = {N(max)} cm, que vale para Vd ≤ 0,20·VRd2." : "");
        }
        // V8: ρsw = Asw/(bw·s) ≥ 0,2·fctm/fywk (estribos verticais, α = 90°), na zona de maior espaçamento
        if (v.Zonas.Count > 0 && phi > 0)
        {
            var (zona, s) = v.Zonas.OrderByDescending(z => z.Esp).First();
            double asw = Math.Max(2, v.EstRamos) * Math.PI * phi * phi / 400, rho = asw / (v.B * s) * 100;
            double fywk = TabelasNbr.Fyk(v.AcoEstribo), min = 0.2 * TabelasNbr.Fctm(v.FckMPa) / fywk * 100;
            yield return R("V8", G, $"Taxa mínima de estribos ({zona}, s = {N(s)} cm)", $"ρsw = {N(rho, "0.000")} %",
                           $"≥ 0,2·fctm/fywk = {N(min, "0.000")} %", rho < min - 1e-9, "NBR 6118, 17.4.1.1.1");
        }
    }

    /// <summary>
    /// V11: na ponta sem dente, a fração da armadura inferior do vão que chega ao apoio ≥ 1/3 (momento no apoio nulo, viga
    /// pré-moldada biapoiada; 1/4 só com momento negativo maior que 0,5·Mvão). O comprimento de ancoragem além da face do apoio
    /// (18.3.2.4.1) depende da largura do apoio, que o template não tem: vai na dica.
    /// </summary>
    public static ResultadoVerificacao AncoragemNoApoio(string ponta, double asVaoCm2, double asNoApoioCm2)
    {
        double f = asVaoCm2 > 0 ? asNoApoioCm2 / asVaoCm2 : 0;
        return R("V11", "Longitudinal", $"Armadura inferior levada ao apoio da ponta {ponta}", $"{N(asNoApoioCm2, "0.00")} de {N(asVaoCm2, "0.00")} cm² ({N(f * 100, "0")} %)",
                 "≥ 1/3 de As,vão", f < 1.0 / 3 - 1e-9, "NBR 6118, 18.3.2.4", SituacaoVerificacao.Conferir,
                 "O comprimento de ancoragem além da face do apoio (18.3.2.4.1) depende do apoio: conferir.");
    }
}
