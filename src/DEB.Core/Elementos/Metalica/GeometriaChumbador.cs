using System;
using System.Globalization;
using System.Linq;

namespace DEB.Core.Elementos.Metalica;

/// <summary>Forma do chumbador (D50): pré-concretado com gancho (J) ou reto com porca de ancoragem, e os pós-instalados.</summary>
public enum FormaChumbador { Gancho, RetoComPorca, Quimico, Expansao, ParafusoConcreto }

/// <summary>
/// Geometria de um chumbador para a família do Revit e os desenhos (mm). Origem no topo da placa de base (onde assenta a arruela); z
/// para cima. A placa e o graute ficam entre z = −pega e 0; o concreto começa em z = −pega.
/// </summary>
public sealed class GeometriaChumbador
{
    public FormaChumbador Forma { get; set; }
    public double Db { get; set; }
    /// <summary>Embutimento abaixo da face do concreto (comprimento reto do fuste dentro do concreto).</summary>
    public double Embutimento { get; set; }
    /// <summary>Espessura da placa + graute (da face do concreto ao topo da placa).</summary>
    public double Pega { get; set; }
    /// <summary>Gancho: raio da dobra (eixo) e comprimento da ponta reta depois da dobra.</summary>
    public double RaioGancho { get; set; }
    public double PontaGancho { get; set; }
    /// <summary>Químico: diâmetro do furo (a resina preenche o anel em volta da barra).</summary>
    public double DiametroFuro { get; set; }
    public DimensoesParafuso Porca { get; set; }
    public string Produto { get; set; } = "";

    /// <summary>Altura da rosca acima da porca (3 fios, mínimo 5 mm).</summary>
    public double Sobra => Math.Max(5, 3 * (Db >= 24 ? 3 : 2.5));
    /// <summary>Topo da barra (acima do topo da placa): arruela + porca + sobra.</summary>
    public double Topo => (Porca?.EspessuraArruela ?? 4) + (Porca?.AlturaPorca ?? Db) + Sobra;

    /// <summary>Nome da família pela forma e pelas medidas (cm): uma família por medida, como a do muro.</summary>
    public string NomeFamilia()
    {
        string N(double mm) => Math.Round(mm).ToString("0", CultureInfo.InvariantCulture);
        string f = Forma switch
        {
            FormaChumbador.Gancho => "Chumbador J",
            FormaChumbador.RetoComPorca => "Chumbador reto",
            FormaChumbador.Quimico => "Chumbador químico",
            FormaChumbador.Expansao => "Chumbador de expansão",
            _ => "Parafuso para concreto",
        };
        return $"PCEB {f} ø{Fmt.Virgula(Db, "0.#")} L{N(Embutimento)} P{N(Pega)}";
    }

    /// <summary>Chumbador da placa de base: a forma pelo produto do catálogo (tipo) ou pela opção do pré-concretado.</summary>
    public static GeometriaChumbador DaPlaca(DadosPlacaBase d, CatalogoFixadores cat, double tp, double graute = 25)
    {
        cat ??= CatalogoFixadores.Carregar();
        var g = new GeometriaChumbador { Db = d.Db, Pega = tp + graute, Porca = cat.Parafuso(d.Db) ?? ParafusoAproximado(d.Db) };
        if (!string.IsNullOrEmpty(d.ProdutoAncoragem))
        {
            var linhas = cat.Chumbadores.Where(c => c.Produto == d.ProdutoAncoragem && Math.Abs(c.Db - d.Db) < 0.6).ToList();
            string tipo = linhas.Select(c => c.Tipo).FirstOrDefault() ?? "Químico";
            g.Produto = d.ProdutoAncoragem;
            g.Forma = tipo.StartsWith("Mec", StringComparison.OrdinalIgnoreCase) ? FormaChumbador.Expansao
                    : tipo.StartsWith("Paraf", StringComparison.OrdinalIgnoreCase) ? FormaChumbador.ParafusoConcreto : FormaChumbador.Quimico;
            g.Embutimento = d.Hef > 0 ? d.Hef : linhas.Select(c => c.Hef).DefaultIfEmpty(12 * d.Db).Max();
            g.DiametroFuro = linhas.Select(c => c.D0).FirstOrDefault(x => x > 0);
            if (g.DiametroFuro <= 0) g.DiametroFuro = d.Db + (d.Db < 20 ? 4 : 6);
        }
        else
        {
            g.Forma = d.Forma;
            g.Embutimento = d.Hef > 0 ? d.Hef : Math.Max(300, 20 * d.Db);
            g.RaioGancho = 2.5 * d.Db;     // dobra de pino de 5·d (NBR 6118 9.4.2.3, barras lisas/rosqueadas)
            g.PontaGancho = 4 * d.Db;
        }
        return g;
    }

    /// <summary>Diâmetro fora da tabela de parafusos: porca e arruela proporcionais (pesada: chave 1,6·d, altura d; arruela 2·d × 0,15·d).</summary>
    private static DimensoesParafuso ParafusoAproximado(double db) => new DimensoesParafuso
    {
        Nome = "ø" + Fmt.Virgula(db, "0.#"), Db = db, Chave = 1.6 * db, AlturaCabeca = 0.65 * db, AlturaPorca = db, DiametroArruela = 2 * db,
        EspessuraArruela = Math.Max(4, 0.15 * db), Rosca = 2 * db,
    };
}
