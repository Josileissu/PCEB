using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DEB.Core.Persistencia;

namespace DEB.Core.Elementos.Metalica;

/// <summary>
/// Dimensões de um parafuso estrutural com porca e arruela (mm) para o desenho e as famílias (D50, item 6). Os valores do PCEB são os
/// das normas de produto: cabeça e porca sextavadas pesadas (ASME B18.2.6, ASTM A563/A194) e arruela ASTM F436 nos parafusos em
/// polegada; ISO 4014/4032 e ISO 7089 nos métricos.
/// </summary>
public sealed class DimensoesParafuso
{
    public string Nome { get; set; } = "";
    public double Db { get; set; }
    /// <summary>Abertura da chave (entre faces) da cabeça e da porca.</summary>
    public double Chave { get; set; }
    public double AlturaCabeca { get; set; }
    public double AlturaPorca { get; set; }
    public double DiametroArruela { get; set; }
    public double EspessuraArruela { get; set; }
    /// <summary>Comprimento de rosca (ASTM F3125: parafuso estrutural tem rosca curta).</summary>
    public double Rosca { get; set; }
    public bool Polegada { get; set; }

    /// <summary>Comprimentos comerciais: de ¼" em ¼" até 5" e de ½" em ½" acima (polegada); de 5 em 5 mm até 100 e de 10 em 10 acima (métrico).</summary>
    public IEnumerable<double> Comprimentos()
    {
        if (Polegada)
        {
            for (double p = 1.0; p <= 5.0 + 1e-9; p += 0.25) yield return p * 25.4;
            for (double p = 5.5; p <= 12.0 + 1e-9; p += 0.5) yield return p * 25.4;
        }
        else
        {
            for (double c = 30; c <= 100 + 1e-9; c += 5) yield return c;
            for (double c = 110; c <= 300 + 1e-9; c += 10) yield return c;
        }
    }

    /// <summary>
    /// Comprimento comercial para a pega (soma das espessuras apertadas, mm): pega + arruela + porca + sobra de 3 fios de rosca,
    /// arredondado para cima na série comercial.
    /// </summary>
    public double ComprimentoPara(double pega, int arruelas = 1)
    {
        double passo = Polegada ? 25.4 / FiosPorPolegada(Db) : PassoMetrico(Db);
        double min = pega + arruelas * EspessuraArruela + AlturaPorca + 3 * passo;
        foreach (double c in Comprimentos()) if (c >= min - 1e-6) return c;
        return Math.Ceiling(min / 10) * 10;
    }

    private static double FiosPorPolegada(double db)
    {
        double p = db / 25.4;
        return p <= 0.5 + 1e-3 ? 13 : p <= 0.625 + 1e-3 ? 11 : p <= 0.75 + 1e-3 ? 10 : p <= 0.875 + 1e-3 ? 9 : p <= 1 + 1e-3 ? 8 : 7;
    }

    private static double PassoMetrico(double db) => db <= 12 ? 1.75 : db <= 16 ? 2 : db <= 22 ? 2.5 : db <= 27 ? 3 : 3.5;
}

/// <summary>
/// Linha de um chumbador de catálogo (D50, item 6): produto, diâmetro, embutimento, distâncias e as resistências do fabricante para
/// um fck. <see cref="Natureza"/> diz se os valores são de cálculo (já com os coeficientes do fabricante: compara com o esforço de
/// cálculo) ou recomendados/admissíveis (compara com o esforço de serviço; o PCEB divide o de cálculo por 1,4).
/// </summary>
public sealed class LinhaChumbador
{
    public string Produto { get; set; } = "";
    public string Fabricante { get; set; } = "";
    public string Tipo { get; set; } = "Químico";
    public string Fonte { get; set; } = "";
    public string Natureza { get; set; } = "Cálculo";
    /// <summary>O que a resistência cobre: "concreto e adesivo" (o aço da barra é verificado à parte) ou "ancoragem completa".</summary>
    public string Cobre { get; set; } = "concreto e adesivo";
    public double Db { get; set; }
    public double Hef { get; set; }
    public double D0 { get; set; }
    public double Scr { get; set; }
    public double Ccr { get; set; }
    public double Smin { get; set; }
    public double Cmin { get; set; }
    public double Hmin { get; set; }
    public double Torque { get; set; }
    public double Fck { get; set; }
    /// <summary>Resistência à tração do catálogo (kN), na natureza indicada.</summary>
    public double N { get; set; }
    /// <summary>Resistência ao corte do catálogo (kN).</summary>
    public double V { get; set; }
}

/// <summary>Resistência de uma ancoragem de catálogo para um caso (kN), já interpolada no fck e no embutimento.</summary>
public sealed class ResistenciaAncoragem
{
    public double NRd, VRd;
    public string Texto = "";
    public LinhaChumbador Base;
}

/// <summary>
/// Catálogo de fixadores (D50, item 6): parafusos com porca e arruela (embutidos no PCEB, das normas de produto) e chumbadores de
/// fabricantes (arquivo do escritório <c>%AppData%\PCEB\padroes\fixadores.json</c>, com a fonte de cada linha; os catálogos são de
/// terceiros e ficam fora do Git).
/// </summary>
public sealed class CatalogoFixadores
{
    public List<DimensoesParafuso> Parafusos { get; } = new List<DimensoesParafuso>();
    public List<LinhaChumbador> Chumbadores { get; } = new List<LinhaChumbador>();

    public static string CaminhoEscritorio =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCEB", "padroes", "fixadores.json");

    private static DimensoesParafuso P(string nome, double db, double chave, double k, double m, double dw, double tw, double rosca, bool pol) =>
        new DimensoesParafuso { Nome = nome, Db = db, Chave = chave, AlturaCabeca = k, AlturaPorca = m, DiametroArruela = dw, EspessuraArruela = tw, Rosca = rosca, Polegada = pol };

    /// <summary>Parafusos das normas (mm). Polegada: ASME B18.2.6 (cabeça pesada), porca pesada, arruela F436. Métrico: ISO 4014/4032/7089.</summary>
    public static IReadOnlyList<DimensoesParafuso> PadraoParafusos { get; } = new[]
    {
        P("1/2\"", 12.7, 22.2, 7.9, 12.3, 27.0, 4.0, 25.4, true),
        P("5/8\"", 15.88, 27.0, 9.9, 15.5, 33.3, 4.0, 31.8, true),
        P("3/4\"", 19.05, 31.8, 11.9, 18.7, 37.3, 4.0, 34.9, true),
        P("7/8\"", 22.2, 36.5, 13.9, 21.8, 44.5, 4.0, 38.1, true),
        P("1\"", 25.4, 41.3, 15.5, 25.0, 50.8, 4.0, 44.5, true),
        P("1.1/8\"", 28.6, 46.0, 17.5, 28.2, 57.2, 4.0, 50.8, true),
        P("1.1/4\"", 31.75, 50.8, 19.8, 31.0, 63.5, 4.0, 50.8, true),
        P("M12", 12, 18, 7.5, 10.8, 24, 2.5, 30, false),
        P("M16", 16, 24, 10, 14.8, 30, 3, 38, false),
        P("M20", 20, 30, 12.5, 18, 37, 3, 46, false),
        P("M22", 22, 34, 14, 19.4, 39, 3, 50, false),
        P("M24", 24, 36, 15, 21.5, 44, 4, 54, false),
        P("M27", 27, 41, 17, 23.8, 50, 4, 60, false),
        P("M30", 30, 46, 18.7, 25.6, 56, 4, 66, false),
    };

    public DimensoesParafuso Parafuso(double db) =>
        Parafusos.OrderBy(p => Math.Abs(p.Db - db)).FirstOrDefault(p => Math.Abs(p.Db - db) < 0.6);

    public IEnumerable<string> Produtos => Chumbadores.Select(c => c.Produto).Distinct();

    /// <summary>
    /// Resistência do produto para o diâmetro, o embutimento (hef ≤ 0: o maior do catálogo) e o fck: interpola no embutimento e no fck
    /// entre as linhas do catálogo, sem extrapolar (abaixo do menor fck do catálogo não há resistência).
    /// </summary>
    public ResistenciaAncoragem Resistencia(string produto, double db, double hef, double fck)
    {
        var ls = Chumbadores.Where(c => c.Produto == produto && Math.Abs(c.Db - db) < 0.6).ToList();
        if (ls.Count == 0) return null;
        if (hef <= 0) hef = ls.Max(c => c.Hef);
        // por fck: para cada fck do catálogo, interpola no hef; depois interpola entre os fck
        var porFck = ls.GroupBy(c => c.Fck).OrderBy(g => g.Key).Select(g =>
        {
            var o = g.OrderBy(c => c.Hef).ToList();
            if (hef < o[0].Hef - 1e-6) return (Fck: g.Key, N: 0.0, V: 0.0, L: o[0]);
            var a = o.LastOrDefault(c => c.Hef <= hef + 1e-6) ?? o[0];
            var b = o.FirstOrDefault(c => c.Hef >= hef - 1e-6) ?? o[o.Count - 1];
            double t = b.Hef - a.Hef > 1e-6 ? (hef - a.Hef) / (b.Hef - a.Hef) : 0;
            return (Fck: g.Key, N: a.N + t * (b.N - a.N), V: a.V + t * (b.V - a.V), L: a);
        }).ToList();
        if (fck < porFck[0].Fck - 1e-6) return new ResistenciaAncoragem { NRd = 0, VRd = 0, Base = porFck[0].L, Texto = $"fck {fck:0} abaixo do menor do catálogo ({porFck[0].Fck:0} MPa)" };
        var i = porFck.LastOrDefault(x => x.Fck <= fck + 1e-6);
        var j = porFck.FirstOrDefault(x => x.Fck >= fck - 1e-6);
        if (j.L == null) j = i;
        double u = j.Fck - i.Fck > 1e-6 ? (fck - i.Fck) / (j.Fck - i.Fck) : 0;
        var r = new ResistenciaAncoragem { NRd = i.N + u * (j.N - i.N), VRd = i.V + u * (j.V - i.V), Base = i.L };
        // valores recomendados (serviço) viram de cálculo com γf = 1,4
        if (r.Base.Natureza != "Cálculo") { r.NRd *= 1.4; r.VRd *= 1.4; }
        r.Texto = $"{produto} ø{db:0.#} mm, hef = {hef:0} mm, fck = {fck:0} MPa ({r.Base.Fonte}; {r.Base.Natureza.ToLowerInvariant()}" +
                  (r.Base.Natureza != "Cálculo" ? " × 1,4" : "") + $", cobre {r.Base.Cobre})";
        return r;
    }

    // ---------------------------------------------------------------- leitura e gravação

    public static CatalogoFixadores Carregar(string arquivoEscritorio = null, List<string> avisos = null)
    {
        var c = new CatalogoFixadores();
        c.Parafusos.AddRange(PadraoParafusos);
        string arq = arquivoEscritorio ?? CaminhoEscritorio;
        try
        {
            if (File.Exists(arq)) c.LerJson(File.ReadAllText(arq));
        }
        catch (Exception ex) { avisos?.Add($"Catálogo de fixadores do escritório não lido ({ex.Message})."); }
        return c;
    }

    private static double D(ValorJson o, string k) => o[k] is ValorJson v && v.Tipo == TipoJson.Numero ? v.Numero : 0;
    private static string S(ValorJson o, string k, string padrao = "") => o[k] is ValorJson v && v.Tipo == TipoJson.Texto ? v.Texto : padrao;

    /// <summary>Lê os chumbadores: { "chumbadores": [ { produto, fabricante, tipo, fonte, natureza, cobre, linhas: [ {db, hef, …, fck, n, v} ] } ] }.</summary>
    public void LerJson(string json)
    {
        var raiz = Json.Ler(json);
        if (!(raiz["chumbadores"] is ValorJson lista)) return;
        foreach (var p in lista.Itens)
        {
            if (!(p["linhas"] is ValorJson linhas)) continue;
            foreach (var l in linhas.Itens)
                Chumbadores.Add(new LinhaChumbador
                {
                    Produto = S(p, "produto"), Fabricante = S(p, "fabricante"), Tipo = S(p, "tipo", "Químico"), Fonte = S(p, "fonte"),
                    Natureza = S(p, "natureza", "Cálculo"), Cobre = S(p, "cobre", "concreto e adesivo"),
                    Db = D(l, "db"), Hef = D(l, "hef"), D0 = D(l, "d0"), Scr = D(l, "scr"), Ccr = D(l, "ccr"), Smin = D(l, "smin"), Cmin = D(l, "cmin"),
                    Hmin = D(l, "hmin"), Torque = D(l, "torque"), Fck = D(l, "fck"), N = D(l, "n"), V = D(l, "v"),
                });
        }
    }
}
