using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Modelo;
using DEB.Core.Normas;

namespace DEB.Core.Templates.Reservatorio;

/// <summary>
/// Reservatório retangular de concreto armado moldado in loco (pedido do usuário, 2026-11-05): paredes externas e
/// internas (ortogonais, de face a face), laje de fundo e lajes superiores (intermediária/tampa). O padrão de
/// desenho é o do exemplo do escritório (<c>src/DEB.Cad/auxiliar_cad/reservatório</c>):
/// <list type="bullet">
/// <item>paredes em planta (1:25) com as horizontais rebatidas em volta, L nos cantos e nos encontros;</item>
/// <item>cortes (1:25) com as verticais rebatidas dos dois lados e os arranques parede-fundo;</item>
/// <item>fundo positivo e negativo e cada laje superior em planta (1:50) com faixas;</item>
/// <item>relação do aço.</item>
/// </list>
/// Medidas do exemplo (parede 20, fundo 15): horizontais = comprimento externo − 2c com ganchos que fecham na outra
/// face (t − 2c); verticais da face inferior do fundo ao topo − 2c com gancho de 90° da NBR; dobras do fundo h − 2c;
/// perna do L de canto 88, arranque 100 (fundo) × 111 (parede): padrões editáveis, conferidos contra lb/traspasse
/// da NBR 6118 (aviso quando menores). Nada de dimensionamento: bitolas e espaçamentos vêm do engenheiro (ou do
/// desenho reconhecido).
/// </summary>
public sealed class TemplateReservatorio : ITemplateArmadura
{
    public string Id => "reservatorio";
    public string Nome => "Reservatório retangular (paredes, fundo e tampas)";
    public CategoriaElemento Categoria => CategoriaElemento.Reservatorio;
    public string Descricao => "Paredes externas e internas, laje de fundo e lajes superiores: planta das paredes, cortes, " +
                               "fundo positivo/negativo, lajes e relação do aço no padrão do escritório.";

    private static ParametroTemplate P(string grupo, string nome, string rotulo, string padrao, string unidade = "",
        TipoParametro tipo = TipoParametro.Numero, double min = double.NegativeInfinity, double max = double.PositiveInfinity,
        string descricao = "", params string[] opcoes) => new ParametroTemplate
    {
        Grupo = grupo, Nome = nome, Rotulo = rotulo, Padrao = padrao, Unidade = unidade, Tipo = tipo, Minimo = min, Maximo = max,
        Descricao = descricao, Opcoes = opcoes.ToList(),
    };

    private const string G1 = "1. Geometria", G2 = "2. Fundo", G3 = "3. Lajes superiores", G4 = "4. Cobrimentos e regras",
        G5 = "5. Armadura das paredes", G6 = "6. Armadura do fundo", G7 = "7. Armadura das lajes superiores", G8 = "8. Desenho";

    public IReadOnlyList<ParametroTemplate> Parametros { get; } = new[]
    {
        P(G1, "Lx", "Comprimento externo (x)", "396", "cm", min: 50),
        P(G1, "Ly", "Largura externa (y)", "402", "cm", min: 50),
        P(G1, "t", "Espessura das paredes", "15", "cm", min: 8, max: 60),
        P(G1, "H", "Altura das paredes (acima do fundo)", "398", "cm", min: 30),
        P(G1, "InternasX", "Paredes internas paralelas a y: eixo x", "", "cm", TipoParametro.Texto,
          descricao: "Posições do eixo medidas da face externa esquerda, separadas por ';' (vazio = nenhuma)."),
        P(G1, "InternasY", "Paredes internas paralelas a x: eixo y", "165", "cm", TipoParametro.Texto,
          descricao: "Posições do eixo medidas da face externa de baixo, separadas por ';' (vazio = nenhuma)."),

        P(G2, "hf", "Espessura do fundo", "15", "cm", min: 8, max: 100),
        P(G2, "Balanco", "Fundo além das paredes (cada lado)", "30", "cm", min: 0),

        P(G3, "Lajes", "Lajes superiores", "", tipo: TipoParametro.Tabela,
          descricao: "Nome, nível do topo (cm acima do topo do fundo), espessura e células (vazio = todas; ex.: 1,2)."),

        P(G4, "CobParede", "Cobrimento das paredes", "4.5", "cm", min: 1, max: 10,
          descricao: "Medido até a face externa da barra (exemplo: parede 20 → gancho de fechamento 11)."),
        P(G4, "CobLaje", "Cobrimento das lajes", "3.5", "cm", min: 1, max: 10, descricao: "Exemplo: laje 15 → dobra 8."),
        P(G4, "Fck", "fck (para lb e traspasse)", "25", "MPa", min: 20, max: 90),
        P(G4, "Aco", "Aço", "CA-50", tipo: TipoParametro.Opcao, opcoes: new[] { "CA-50", "CA-60" }),

        P(G5, "HBitola", "Horizontais: bitola", "6.3", "mm", TipoParametro.Bitola, 5, 40),
        P(G5, "HEsp", "Horizontais: espaçamento (paredes em x)", "20", "cm", min: 5),
        P(G5, "HEspY", "Horizontais: espaçamento (paredes em y)", "0", "cm", min: 0, descricao: "0 = o mesmo das paredes em x."),
        P(G5, "VBitola", "Verticais: bitola", "6.3", "mm", TipoParametro.Bitola, 5, 40),
        P(G5, "VEsp", "Verticais: espaçamento", "20", "cm", min: 5),
        P(G5, "CantoPerna", "L dos cantos e encontros: perna", "88", "cm", min: 10,
          descricao: "Padrão do escritório (exemplo); aviso se menor que o traspasse da NBR 6118."),
        P(G5, "ArrBitola", "Arranques: bitola", "6.3", "mm", TipoParametro.Bitola, 5, 40),
        P(G5, "ArrEsp", "Arranques: espaçamento", "20", "cm", min: 5),
        P(G5, "ArrFundo", "Arranques: perna no fundo", "100", "cm", min: 10),
        P(G5, "ArrParede", "Arranques: perna na parede", "111", "cm", min: 10),
        P(G5, "ArrGancho", "Arranques: gancho na ponta do fundo", "6", "cm", min: 0),
        P(G5, "CantoVBitola", "Verticais de canto: bitola", "0", "mm", min: 0, max: 40, descricao: "0 = sem barras de canto."),
        P(G5, "CantoVQtd", "Verticais de canto: barras por canto", "4", tipo: TipoParametro.Inteiro, min: 0),

        P(G6, "FPosBitola", "Positiva: bitola", "6.3", "mm", TipoParametro.Bitola, 5, 40),
        P(G6, "FPosEsp", "Positiva: espaçamento", "20", "cm", min: 5),
        P(G6, "FPosVolta", "Positiva: volta por cima na borda", "30", "cm", min: 0),
        P(G6, "FNegBitola", "Negativa: bitola", "6.3", "mm", TipoParametro.Bitola, 5, 40),
        P(G6, "FNegEsp", "Negativa: espaçamento", "20", "cm", min: 5),
        P(G6, "BordaBitola", "Borda: bitola", "6.3", "mm", min: 0, max: 40, descricao: "0 = sem armadura de borda."),
        P(G6, "BordaQtd", "Borda: barras por lado", "2", tipo: TipoParametro.Inteiro, min: 0),

        P(G7, "LPosBitola", "Positiva: bitola", "6.3", "mm", TipoParametro.Bitola, 5, 40),
        P(G7, "LPosEsp", "Positiva: espaçamento", "20", "cm", min: 5),
        P(G7, "LNegBitola", "Negativa: bitola", "6.3", "mm", TipoParametro.Bitola, 5, 40),
        P(G7, "LNegEsp", "Negativa: espaçamento", "20", "cm", min: 5),

        P(G8, "EscParedes", "Escala das paredes e cortes (1:n)", "25", min: 1),
        P(G8, "EscLajes", "Escala das lajes (1:n)", "50", min: 1),
        P(G8, "Nome", "Nome do detalhamento", "RESERVATÓRIO", tipo: TipoParametro.Texto),
    };

    // ======================== modelo ========================

    /// <summary>Parede (na planta): retângulo [X0,X1]×[Y0,Y1]; Horizontal = comprimento em x.</summary>
    internal sealed class Parede
    {
        public string Nome = "";
        public double X0, Y0, X1, Y1;
        public bool Horizontal, Externa;
        public double Comprimento => Horizontal ? X1 - X0 : Y1 - Y0;
    }

    internal sealed class Laje
    {
        public string Nome = "";
        public double Nivel, h;
        /// <summary>Células cobertas (índices na grade das paredes internas); vazio = todas.</summary>
        public List<int> Celulas = new List<int>();
    }

    private sealed class Ctx
    {
        public ValoresTemplate V;
        public ConfiguracaoArmadura Cfg;
        public ResultadoTemplate R;
        public double Lx, Ly, t, H, hf, bal, cp, cl, escP, escL, htP, htL;
        public string Aco;
        public List<double> Xs = new List<double>(), Ys = new List<double>();   // eixos das internas
        public List<Parede> Paredes = new List<Parede>();
        public List<Laje> Lajes = new List<Laje>();
        public List<(double X0, double Y0, double X1, double Y1)> Celulas = new List<(double, double, double, double)>();
        public int Seq;
        public double YLivre;   // próxima faixa livre (abaixo) para as plantas das lajes
    }

    private static string Num(double x) => Calculo.CalculoComprimento.NumeroCompacto(x);

    internal static List<double> Lista(string s) =>
        (s ?? "").Split(new[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => double.TryParse(p.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN)
            .Where(d => !double.IsNaN(d)).OrderBy(d => d).ToList();

    public ResultadoTemplate Gerar(ValoresTemplate v, ConfiguracaoArmadura cfg)
    {
        var c = new Ctx { V = v, Cfg = cfg, R = new ResultadoTemplate() };
        c.Lx = v.Numero("Lx"); c.Ly = v.Numero("Ly"); c.t = v.Numero("t"); c.H = v.Numero("H");
        c.hf = v.Numero("hf"); c.bal = Math.Max(0, v.Numero("Balanco"));
        c.cp = v.Numero("CobParede"); c.cl = v.Numero("CobLaje");
        c.escP = Math.Max(1, v.Numero("EscParedes")); c.escL = Math.Max(1, v.Numero("EscLajes"));
        c.htP = cfg.AlturaTextoMm / 10.0 * c.escP; c.htL = cfg.AlturaTextoMm / 10.0 * c.escL;
        c.Aco = string.IsNullOrWhiteSpace(v["Aco"]) ? "CA-50" : v["Aco"];
        c.R.Escala = c.escP;
        c.R.Detalhamento = string.IsNullOrWhiteSpace(v["Nome"]) ? "RESERVATÓRIO" : v["Nome"].Trim();

        MontarModelo(c);
        PlantaParedes(c);
        Cortes(c);
        Fundo(c);
        LajesSuperiores(c);
        Verificar(c);
        return c.R;
    }

    private static void MontarModelo(Ctx c)
    {
        double t = c.t, Lx = c.Lx, Ly = c.Ly;
        c.Xs = Lista(c.V["InternasX"]).Where(x => x > t && x < Lx - t).ToList();
        c.Ys = Lista(c.V["InternasY"]).Where(y => y > t && y < Ly - t).ToList();
        c.Paredes.Add(new Parede { Nome = "PAR SUL", X0 = 0, Y0 = 0, X1 = Lx, Y1 = t, Horizontal = true, Externa = true });
        c.Paredes.Add(new Parede { Nome = "PAR NORTE", X0 = 0, Y0 = Ly - t, X1 = Lx, Y1 = Ly, Horizontal = true, Externa = true });
        c.Paredes.Add(new Parede { Nome = "PAR OESTE", X0 = 0, Y0 = 0, X1 = t, Y1 = Ly, Horizontal = false, Externa = true });
        c.Paredes.Add(new Parede { Nome = "PAR LESTE", X0 = Lx - t, Y0 = 0, X1 = Lx, Y1 = Ly, Horizontal = false, Externa = true });
        int k = 1;
        foreach (var y in c.Ys) c.Paredes.Add(new Parede { Nome = "PAR INT " + k++, X0 = t, Y0 = y - t / 2, X1 = Lx - t, Y1 = y + t / 2, Horizontal = true });
        foreach (var x in c.Xs) c.Paredes.Add(new Parede { Nome = "PAR INT " + k++, X0 = x - t / 2, Y0 = t, X1 = x + t / 2, Y1 = Ly - t, Horizontal = false });

        // células (faces internas), de baixo para cima e da esquerda para a direita
        var xs = new List<double> { t }; foreach (var x in c.Xs) { xs.Add(x - t / 2); xs.Add(x + t / 2); } xs.Add(Lx - t);
        var ys = new List<double> { t }; foreach (var y in c.Ys) { ys.Add(y - t / 2); ys.Add(y + t / 2); } ys.Add(Ly - t);
        for (int j = 0; j + 1 < ys.Count; j += 2)
            for (int i = 0; i + 1 < xs.Count; i += 2)
                c.Celulas.Add((xs[i], ys[j], xs[i + 1], ys[j + 1]));

        foreach (var l in c.V.Tabela("Lajes"))
        {
            var laje = new Laje
            {
                Nome = ValoresTemplate.Celula(l, 0), Nivel = ValoresTemplate.NumeroCelula(l, 1, c.H), h = ValoresTemplate.NumeroCelula(l, 2, 12),
            };
            foreach (var s in ValoresTemplate.Celula(l, 3).Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(s, out int i) && i >= 1 && i <= c.Celulas.Count) laje.Celulas.Add(i - 1);
            if (laje.Nome.Length == 0) laje.Nome = "L" + (c.Lajes.Count + 2);
            c.Lajes.Add(laje);
        }
    }

    // ======================== auxiliares de desenho ========================

    private static void Texto(Ctx c, string s, double x, double y, double ht, int alinh = 0, string layer = "ARM-TEXTO-FORMA", double ang = 0) =>
        c.R.Textos.Add(new TextoTemplate { Texto = s, X = x, Y = y, Altura = ht, Alinhamento = alinh, Layer = layer, Angulo = ang });

    /// <summary>Título no padrão do exemplo: nome (2,85 alturas) e "ESC 1:n" (1,78) embaixo, alinhados à esquerda.</summary>
    private static void Titulo(Ctx c, string linha1, string linha2, double x, double y, double esc)
    {
        double ht = c.Cfg.AlturaTextoMm / 10.0 * esc, hT = 2.85 * ht, hE = 1.78 * ht;
        Texto(c, linha1, x, y, hT, 0, "TEXTOS");
        if (!string.IsNullOrEmpty(linha2)) { y -= 1.4 * hT; Texto(c, linha2, x, y, hT, 0, "TEXTOS"); }
        Texto(c, "ESC 1:" + Num(esc), x, y - 1.2 * hT - 0.6 * hE, hE, 0, "TEXTOS");
    }

    private static void Retangulo(Ctx c, double x0, double y0, double x1, double y1, string layer = "DT-Forma") =>
        c.R.Contornos.Add(new ContornoTemplate { Fechado = true, Layer = layer, Vertices = { (x0, y0), (x1, y0), (x1, y1), (x0, y1) } });

    private static void Linha(Ctx c, string layer, params (double, double)[] pts) =>
        c.R.Contornos.Add(new ContornoTemplate { Layer = layer, Vertices = pts.ToList() });

    private static int Qtd(Ctx c, double vao, double esp) =>
        CalculoFaixa.Quantidade(vao, esp, c.Cfg.EspacamentoInicialFaixa, c.Cfg.ModoQuantidadeFaixa);

    private static BarraTemplate Barra(Ctx c, string nome, double bitola, double esp, int qtd, double esc, params (double, double)[] pts)
    {
        var b = new BarraTemplate
        {
            Nome = "r" + (++c.Seq) + " " + nome, Bitola = bitola, Aco = c.Aco, Espacamento = esp, Quantidade = Math.Max(1, qtd), Escala = esc,
        };
        b.Vertices.AddRange(pts);
        for (int i = 0; i + 1 < pts.Length; i++) b.Formatos.Add(FormatoSegmento.Linha);
        c.R.Barras.Add(b);
        return b;
    }

    /// <summary>Gancho de 90° da NBR 6118 (medida externa: ponta reta + pino/2 + φ), arredondado ao cm.</summary>
    private static double Gancho90(Ctx c, double bitola) =>
        Math.Round(c.Cfg.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho90, bitola, c.Aco, false));

    // ======================== paredes em planta ========================

    /// <summary>Horizontais de uma parede: comprimento externo − 2c, ganchos fechando na outra face (t − 2c).</summary>
    private static void PlantaParedes(Ctx c)
    {
        var v = c.V;
        double t = c.t, cp = c.cp, ht = c.htP, fe = t - 2 * cp;     // fe = gancho de fechamento
        double hb = v.Numero("HBitola"), heX = v.Numero("HEsp"), heY = v.Numero("HEspY") > 0 ? v.Numero("HEspY") : heX;
        double altura = c.H + c.hf - 2 * cp;                           // horizontais por face, na altura toda
        double EspDe(Parede p) => p.Horizontal ? heX : heY;
        int NDe(Parede p) => Qtd(c, altura, EspDe(p));
        double he = Math.Min(heX, heY);                                // L dos cantos: o menor espaçamento
        int nH = Qtd(c, altura, he);

        // forma
        Retangulo(c, 0, 0, c.Lx, c.Ly);
        foreach (var (x0, y0, x1, y1) in c.Celulas) Retangulo(c, x0, y0, x1, y1);

        // barras no lugar (DT-Armadura): duas faces de cada parede; verticais como pontos
        double vb = v.Numero("VBitola"), ve = v.Numero("VEsp");
        foreach (var p in c.Paredes)
        {
            if (p.Horizontal)
            {
                foreach (double y in new[] { p.Y0 + cp, p.Y1 - cp })
                {
                    Linha(c, "DT-Armadura", (p.X0 + cp, y), (p.X1 - cp, y));
                    for (double x = p.X0 + cp + ve / 2; x < p.X1 - cp; x += ve)
                        c.R.Circulos.Add(new CirculoTemplate { X = x, Y = y + (y < (p.Y0 + p.Y1) / 2 ? 1 : -1) * vb / 20, Raio = vb / 20, Layer = "DT-Armadura" });
                }
            }
            else
            {
                foreach (double x in new[] { p.X0 + cp, p.X1 - cp })
                {
                    Linha(c, "DT-Armadura", (x, p.Y0 + cp), (x, p.Y1 - cp));
                    for (double y = p.Y0 + cp + ve / 2; y < p.Y1 - cp; y += ve)
                        c.R.Circulos.Add(new CirculoTemplate { X = x + (x < (p.X0 + p.X1) / 2 ? 1 : -1) * vb / 20, Y = y, Raio = vb / 20, Layer = "DT-Armadura" });
                }
            }
        }

        // rebatidas das horizontais: Sul/internas em x embaixo, Norte em cima, Oeste à esquerda, Leste/internas em y à direita
        double passo = 7 * ht, d0 = 9 * ht;
        double yBaixo = -d0, yCima = c.Ly + d0, xEsq = -d0, xDir = c.Lx + d0;
        foreach (var p in c.Paredes)
        {
            // internas atravessam as externas: de face externa a face externa (− c), como no exemplo
            double a0 = p.Externa ? (p.Horizontal ? p.X0 : p.Y0) : 0, a1 = p.Externa ? (p.Horizontal ? p.X1 : p.Y1) : (p.Horizontal ? c.Lx : c.Ly);
            for (int face = 0; face < 2; face++)
            {
                string nome = $"{p.Nome} horizontal {(face == 0 ? "ext." : "int.")}";
                if (p.Horizontal)
                {
                    bool cima = p.Externa && p.Y0 > c.Ly / 2;
                    double y = cima ? yCima : yBaixo;
                    double s = cima ? -1 : 1;   // ganchos apontam para a planta
                    var b = Barra(c, nome, hb, EspDe(p), NDe(p), c.escP, (a0 + cp, y + s * fe), (a0 + cp, y), (a1 - cp, y), (a1 - cp, y + s * fe));
                    b.PosicaoDescricao = ((p.X0 + p.X1) / 2, y + s * 1.6 * ht);
                    if (cima) yCima += passo; else yBaixo -= passo;
                }
                else
                {
                    bool esq = p.Externa && p.X0 < c.Lx / 2;
                    double x = esq ? xEsq : xDir;
                    double s = esq ? 1 : -1;
                    var b = Barra(c, nome, hb, EspDe(p), NDe(p), c.escP, (x + s * fe, a0 + cp), (x, a0 + cp), (x, a1 - cp), (x + s * fe, a1 - cp));
                    b.PosicaoDescricao = (x + s * 1.6 * ht, (p.Y0 + p.Y1) / 2);
                    b.RotacaoDescricao = Math.PI / 2;
                    if (esq) xEsq -= passo; else xDir += passo;
                }
                if (!p.Externa) break; // interna: as duas faces são iguais → uma posição com o dobro
            }
            if (!p.Externa) c.R.Barras[c.R.Barras.Count - 1].Quantidade = 2 * NDe(p);
        }

        // L nos cantos externos (face externa) e em todos os cantos das células (face interna)
        double Lc = v.Numero("CantoPerna");
        double D = Math.Max(Lc * 0.9, 14 * ht) + 2 * 7 * ht;
        foreach (var (cx, cy, sx, sy) in new[] { (0.0, 0.0, 1, 1), (c.Lx, 0.0, -1, 1), (0.0, c.Ly, 1, -1), (c.Lx, c.Ly, -1, -1) })
        {
            // canto externo: o L corre pelas faces externas; os ganchos fecham para dentro
            double ox = cx + sx * cp - sx * D, oy = cy + sy * cp - sy * D;   // deslocado para fora, na bissetriz
            var b = Barra(c, "L de canto externo", hb, he, nH, c.escP,
                (ox + sx * Lc, oy + sy * fe), (ox + sx * Lc, oy), (ox, oy), (ox, oy + sy * Lc), (ox + sx * fe, oy + sy * Lc));
            b.PosicaoDescricao = (ox + sx * Lc / 2, oy + sy * 1.6 * ht);
            Linha(c, "DT-Pontilhado", (cx + sx * cp, cy + sy * cp), (ox, oy));
        }
        foreach (var (x0, y0, x1, y1) in c.Celulas)
        {
            foreach (var (cx, cy, sx, sy) in new[] { (x0, y0, 1, 1), (x1, y0, -1, 1), (x0, y1, 1, -1), (x1, y1, -1, -1) })
            {
                // canto da célula: L pelas faces internas, ganchos atravessando as paredes (para fora da célula)
                double ix = cx - sx * cp, iy = cy - sy * cp;         // face da barra (dentro da parede)
                double dd = Math.Min(Lc * 0.35, Math.Min(x1 - x0, y1 - y0) / 4);
                double ox = ix + sx * dd, oy = iy + sy * dd;          // desenhado dentro da célula
                var b = Barra(c, "L de canto interno", hb, he, nH, c.escP,
                    (ox + sx * Lc, oy - sy * fe), (ox + sx * Lc, oy), (ox, oy), (ox, oy + sy * Lc), (ox - sx * fe, oy + sy * Lc));
                b.PosicaoDescricao = (ox + sx * Lc / 2, oy + sy * 1.6 * ht);
                Linha(c, "DT-Pontilhado", (ix, iy), (ox, oy));
            }
        }

        // verticais de canto (indicação)
        double cvb = v.Numero("CantoVBitola"); int cvq = v.Inteiro("CantoVQtd");
        if (cvb > 0 && cvq > 0)
        {
            double Lv = c.H + c.hf - 2 * cp;
            var b = Barra(c, "verticais de canto", cvb, 0, 4 * cvq, c.escP, (xEsq - 2 * passo, 0), (xEsq - 2 * passo, Lv));
            b.PosicaoDescricao = (xEsq - 2 * passo - 1.6 * ht, Lv / 2);
            b.RotacaoDescricao = Math.PI / 2;
            foreach (var (cx, cy, sx, sy) in new[] { (0.0, 0.0, 1, 1), (c.Lx, 0.0, -1, 1), (0.0, c.Ly, 1, -1), (c.Lx, c.Ly, -1, -1) })
            {
                var f = new FaixaTemplate
                {
                    Barra = b.Nome, Tipo = TipoFaixa.Indicacao, Escala = c.escP,
                    Posicao = (cx - sx * 4 * ht, cy - sy * 4 * ht),
                };
                f.Referencias.Add((cx + sx * (cp + cvb / 20), cy + sy * (cp + cvb / 20)));
                c.R.Faixas.Add(f);
            }
        }

        // marcas dos cortes A (em x, no meio da 1ª célula) e B (em y)
        var (cx0, cy0, cx1, cy1) = c.Celulas[0];
        double ya = (cy0 + cy1) / 2, xb = (cx0 + cx1) / 2;
        Linha(c, "Cortes", (-3 * ht, ya), (-1 * ht, ya)); Linha(c, "Cortes", (c.Lx + ht, ya), (c.Lx + 3 * ht, ya));
        Texto(c, "A", -4 * ht, ya + 0.5 * ht, 2 * ht, 1, "Cortes"); Texto(c, "A", c.Lx + 4 * ht, ya + 0.5 * ht, 2 * ht, 1, "Cortes");
        Linha(c, "Cortes", (xb, -3 * ht), (xb, -1 * ht)); Linha(c, "Cortes", (xb, c.Ly + ht), (xb, c.Ly + 3 * ht));
        Texto(c, "B", xb + ht, -4 * ht, 2 * ht, 0, "Cortes"); Texto(c, "B", xb + ht, c.Ly + 3 * ht, 2 * ht, 0, "Cortes");

        Titulo(c, "ARMADURA DAS PAREDES", "VISTA EM PLANTA", xEsq, yBaixo - 4 * ht, c.escP);
        c.YLivre = yBaixo - 30 * ht;
    }

    // ======================== cortes ========================

    /// <summary>Corte A (plano xz, no meio da 1ª célula) e corte B (plano yz).</summary>
    private static void Cortes(Ctx c)
    {
        double gap = 60 * c.htP;
        double x0A = c.Lx + gap + 30 * c.htP;
        Corte(c, "CORTE A-A", true, x0A, 0);
        double x0B = x0A + c.Lx + 2 * c.bal + gap + 30 * c.htP;
        Corte(c, "CORTE B-B", false, x0B, 0);
    }

    /// <summary>Um corte: paredes cortadas (as perpendiculares ao plano), fundo, lajes, verticais e arranques.</summary>
    private static void Corte(Ctx c, string titulo, bool emX, double ox, double oy)
    {
        var v = c.V;
        double t = c.t, cp = c.cp, cl = c.cl, ht = c.htP, hf = c.hf, H = c.H;
        double L = emX ? c.Lx : c.Ly;
        // paredes cortadas: no corte A (plano y = cte) aparecem as paredes que correm em y (verticais na planta)
        var cortadas = c.Paredes.Where(p => p.Horizontal != emX).Select(p => emX ? (a: p.X0, b: p.X1, p) : (a: p.Y0, b: p.Y1, p)).OrderBy(q => q.a).ToList();
        double X(double s) => ox + s;   // coordenada ao longo do corte
        double Z(double z) => oy + z;   // z = 0 no topo do fundo

        // forma: fundo, paredes cortadas, lajes
        Retangulo(c, X(-c.bal), Z(-hf), X(L + c.bal), Z(0));
        foreach (var (a, b, _) in cortadas) Retangulo(c, X(a), Z(0), X(b), Z(H));
        foreach (var l in c.Lajes)
            Retangulo(c, X(t), Z(l.Nivel - l.h), X(L - t), Z(l.Nivel));

        double vb = v.Numero("VBitola"), ve = v.Numero("VEsp"), hb = v.Numero("HBitola"), he = v.Numero("HEsp");
        double gV = Gancho90(c, vb), fe = t - 2 * cp;
        double Lv = H + hf - 2 * cp;                 // da face inferior do fundo ao topo
        double zi = -hf + cp, zs = H - cp;

        // barras no lugar: verticais (linhas nas faces) e horizontais (pontos)
        foreach (var (a, b, _) in cortadas)
            foreach (double s in new[] { a + cp, b - cp })
            {
                Linha(c, "DT-Armadura", (X(s), Z(zi)), (X(s), Z(zs)));
                for (double z = 0 + he / 2; z < zs; z += he)
                    c.R.Circulos.Add(new CirculoTemplate { X = X(s + (s < (a + b) / 2 ? 1 : -1) * hb / 20), Y = Z(z), Raio = hb / 20, Layer = "DT-Armadura" });
            }
        // fundo no lugar
        Linha(c, "DT-Armadura", (X(-c.bal + cl), Z(-hf + cl)), (X(L + c.bal - cl), Z(-hf + cl)));
        Linha(c, "DT-Armadura", (X(-c.bal + cl), Z(-cl)), (X(L + c.bal - cl), Z(-cl)));

        // verticais rebatidas: paredes da esquerda à esquerda, da direita à direita, internas acima
        double passo = 7 * ht;
        double xe = X(-c.bal) - 10 * ht, xd = X(L + c.bal) + 10 * ht;
        var quem = cortadas.Select(q => q.p).ToList();
        foreach (var p in quem)
        {
            double comp = p.Comprimento - 2 * cp;
            int n = Qtd(c, comp, ve);
            bool esq = p.Externa && (emX ? p.X0 : p.Y0) < L / 2;
            bool dir = p.Externa && !esq;
            for (int face = 0; face < (p.Externa ? 2 : 1); face++)
            {
                double x = esq ? xe : xd;
                double s = esq ? 1 : -1;
                var b = Barra(c, $"{p.Nome} vertical {(p.Externa ? (face == 0 ? "ext." : "int.") : "")}".Trim(), vb, ve, p.Externa ? n : 2 * n, c.escP,
                    (x + s * gV, Z(zi)), (x, Z(zi)), (x, Z(zs)), (x + s * gV, Z(zs)));
                b.PosicaoDescricao = (x + s * 1.6 * ht, Z((zi + zs) / 2));
                b.RotacaoDescricao = Math.PI / 2;
                if (esq) xe -= passo; else xd += passo;
            }
        }

        // arranques: L com a perna no fundo apontando para dentro da célula e a perna na parede fechando no topo
        double ab = v.Numero("ArrBitola"), ae = v.Numero("ArrEsp"), af = v.Numero("ArrFundo"), ap = v.Numero("ArrParede"), ag = v.Numero("ArrGancho");
        double zf = -hf + cl;                 // nível da perna no fundo (camada inferior)
        double yArr = Z(-hf) - 12 * ht;
        foreach (var (a, b, p) in cortadas)
        {
            int n = Qtd(c, p.Comprimento - 2 * cp, ae);
            var lados = p.Externa ? new[] { (a + b) / 2 < L / 2 ? 1 : -1 } : new[] { -1, 1 };
            foreach (int sd in lados)
            {
                double s0 = sd > 0 ? b - cp : a + cp;            // face da parede voltada para a célula
                // rebatido abaixo do corte, ligado ao lugar por linha traço-ponto
                double rx = X(s0) + sd * 2 * ht, ry = yArr - (ap - (zf - Z(-hf)));
                var bar = Barra(c, $"{p.Nome} arranque", ab, ae, p.Externa ? 2 * n : n, c.escP,
                    (rx + sd * af, ry + ag), (rx + sd * af, ry), (rx, ry), (rx, ry + ap), (rx - sd * fe, ry + ap));
                bar.PosicaoDescricao = (rx + sd * af / 2, ry + 1.6 * ht);
                Linha(c, "DT-Pontilhado", (X(s0), Z(zf)), (rx, ry + ap * 0.2));
                if (!p.Externa) yArr -= ap + 10 * ht;
            }
        }

        // cotas: altura total e fundo
        c.R.Cotas.Add(new CotaTemplate { P1 = (X(L + c.bal), Z(-hf)), P2 = (X(L + c.bal), Z(H)), Posicao = (X(L + c.bal) + 5 * ht, Z(H / 2)), Angulo = Math.PI / 2, Escala = c.escP });
        c.R.Cotas.Add(new CotaTemplate { P1 = (X(-c.bal), Z(-hf)), P2 = (X(-c.bal), Z(0)), Posicao = (X(-c.bal) - 3 * ht, Z(-hf / 2)), Angulo = Math.PI / 2, Escala = c.escP });
        Titulo(c, titulo, null, X(-c.bal), yArr - c.V.Numero("ArrParede") - 10 * ht, c.escP);
    }

    // ======================== fundo ========================

    private static void Fundo(Ctx c)
    {
        var v = c.V;
        double ht = c.htL, cl = c.cl, hf = c.hf;
        double gap = 45 * ht;
        double Wf = c.Lx + 2 * c.bal, Hf = c.Ly + 2 * c.bal;
        double oy = c.YLivre - Hf;                    // planta positiva (canto inferior esquerdo do fundo)
        double oxP = -c.bal, oxN = oxP + Wf + gap;
        double dob = hf - 2 * cl, volta = v.Numero("FPosVolta");

        foreach (var (ox, pos) in new[] { (oxP, true), (oxN, false) })
        {
            Retangulo(c, ox, oy, ox + Wf, oy + Hf);
            // paredes tracejadas (contorno das faces)
            Retangulo(c, ox + c.bal, oy + c.bal, ox + c.bal + c.Lx, oy + c.bal + c.Ly, "DT-Pontilhado");
            double bit = v.Numero(pos ? "FPosBitola" : "FNegBitola"), esp = v.Numero(pos ? "FPosEsp" : "FNegEsp");
            string nome = pos ? "fundo positiva" : "fundo negativa";
            for (int dir = 0; dir < 2; dir++)
            {
                bool emX = dir == 0;
                double L = (emX ? Wf : Hf) - 2 * cl;      // trecho reto
                double vao = (emX ? Hf : Wf) - 2 * cl;    // distribuição
                int n = Qtd(c, vao, esp);
                // barra representativa a 1/3 do vão, com as dobras rebatidas no plano
                double a0 = (emX ? ox : oy) + cl, a1 = a0 + L;
                double pos0 = (emX ? oy : ox) + cl + (emX ? 0.3 : 0.7) * vao;
                (double, double) P(double ao, double tr) => emX ? (ao, tr) : (tr, ao);
                BarraTemplate b;
                if (pos)
                    b = Barra(c, nome + (emX ? " (x)" : " (y)"), bit, esp, n, c.escL,
                        P(a0 + volta, pos0 + dob), P(a0, pos0 + dob), P(a0, pos0), P(a1, pos0), P(a1, pos0 + dob), P(a1 - volta, pos0 + dob));
                else
                    b = Barra(c, nome + (emX ? " (x)" : " (y)"), bit, esp, n, c.escL,
                        P(a0, pos0 + dob), P(a0, pos0), P(a1, pos0), P(a1, pos0 + dob));
                if (pos && volta <= 0) b.Vertices.RemoveAt(b.Vertices.Count - 1);
                b.PosicaoDescricao = emX ? ((a0 + a1) / 2, pos0 + 1.6 * ht) : (pos0 + 1.6 * ht, (a0 + a1) / 2);
                b.RotacaoDescricao = emX ? 0 : Math.PI / 2;
                // faixa da distribuição, perpendicular à barra, a 1/4 do comprimento
                double fa = a0 + (emX ? 0.25 : 0.75) * L;   // longe da barra da outra direção
                double t0 = (emX ? oy : ox) + cl, t1 = t0 + vao;
                c.R.Faixas.Add(new FaixaTemplate
                {
                    Barra = b.Nome, Tipo = TipoFaixa.Ortogonal, Espacamento = esp, Escala = c.escL, MostrarDiametro = false,
                    P1 = emX ? (fa, t0) : (t0, fa), P2 = emX ? (fa, t1) : (t1, fa),
                    Posicao = emX ? (fa - 2 * ht, (t0 + t1) / 2) : ((t0 + t1) / 2, fa + 2 * ht),
                });
            }
            if (pos) Borda(c, ox, oy, Wf, Hf);
            Titulo(c, pos ? "ARMADURA POSITIVA DO FUNDO" : "ARMADURA NEGATIVA DO FUNDO", null, ox, oy - 8 * ht, c.escL);
        }
        c.YLivre = oy - 30 * ht;
    }

    /// <summary>Armadura de borda: barras retas junto das 4 bordas do fundo (exemplo: "2 N11 Ø8.0 C=432").</summary>
    private static void Borda(Ctx c, double ox, double oy, double Wf, double Hf)
    {
        var v = c.V;
        double bit = v.Numero("BordaBitola"); int q = v.Inteiro("BordaQtd");
        if (bit <= 0 || q <= 0) return;
        double cl = c.cl, ht = c.htL, d = cl + 2;
        foreach (var (emX, k) in new[] { (true, 0), (true, 1), (false, 0), (false, 1) })
        {
            double L = (emX ? Wf : Hf) - 2 * cl;
            double tr = k == 0 ? d : (emX ? Hf : Wf) - d;
            var b = emX
                ? Barra(c, "borda (x)", bit, 0, q, c.escL, (ox + cl, oy + tr), (ox + cl + L, oy + tr))
                : Barra(c, "borda (y)", bit, 0, q, c.escL, (ox + tr, oy + cl), (ox + tr, oy + cl + L));
            b.PosicaoDescricao = emX ? (ox + Wf * 0.3, oy + tr + (k == 0 ? 3.2 : -3.2) * ht) : (ox + tr + (k == 0 ? 3.2 : -3.2) * ht, oy + Hf * 0.3);
            b.RotacaoDescricao = emX ? 0 : Math.PI / 2;
        }
        Texto(c, "ARMADURA DE BORDA", ox + Wf / 2, oy + Hf + 1.5 * ht, ht, 1, "ARM-TEXTO-FORMA");
    }

    // ======================== lajes superiores ========================

    private static void LajesSuperiores(Ctx c)
    {
        var v = c.V;
        double ht = c.htL, cl = c.cl, t = c.t;
        double ox = -c.bal;
        foreach (var l in c.Lajes.OrderBy(l => l.Nivel))
        {
            var cels = (l.Celulas.Count == 0 ? Enumerable.Range(0, c.Celulas.Count) : l.Celulas).Select(i => c.Celulas[i]).ToList();
            double oy = c.YLivre - c.Ly;
            // forma: contorno externo e células cobertas
            Retangulo(c, ox, oy, ox + c.Lx, oy + c.Ly);
            foreach (var (x0, y0, x1, y1) in cels) Retangulo(c, ox + x0, oy + y0, ox + x1, oy + y1, "DT-Pontilhado");
            double dob = l.h - 2 * cl;
            int idx = 0;
            foreach (var (x0, y0, x1, y1) in cels)
            {
                idx++;
                // positivas: retas de face externa a face externa das paredes de apoio (− cobrimento)
                double pb = v.Numero("LPosBitola"), pe = v.Numero("LPosEsp");
                foreach (bool emX in new[] { true, false })
                {
                    double a0 = (emX ? x0 : y0) - t + c.cp, a1 = (emX ? x1 : y1) + t - c.cp;
                    double vao = emX ? y1 - y0 : x1 - x0;
                    double tr = (emX ? y0 : x0) + (emX ? 0.35 : 0.65) * vao;
                    var b = emX
                        ? Barra(c, $"{l.Nome} positiva (x) {idx}", pb, pe, Qtd(c, vao, pe), c.escL, (ox + a0, oy + tr), (ox + a1, oy + tr))
                        : Barra(c, $"{l.Nome} positiva (y) {idx}", pb, pe, Qtd(c, vao, pe), c.escL, (ox + tr, oy + a0), (ox + tr, oy + a1));
                    b.PosicaoDescricao = emX ? (ox + (a0 + a1) / 2, oy + tr + 1.4 * ht) : (ox + tr - 1.4 * ht, oy + (a0 + a1) / 2);
                    b.RotacaoDescricao = emX ? 0 : Math.PI / 2;
                    double fa = (emX ? x0 : y0) + 0.2 * (a1 - a0);
                    double t0 = emX ? y0 + cl : x0 + cl, t1 = emX ? y1 - cl : x1 - cl;
                    c.R.Faixas.Add(new FaixaTemplate
                    {
                        Barra = b.Nome, Tipo = TipoFaixa.Ortogonal, Espacamento = pe, Escala = c.escL, MostrarDiametro = false,
                        P1 = emX ? (ox + fa, oy + t0) : (ox + t0, oy + fa), P2 = emX ? (ox + fa, oy + t1) : (ox + t1, oy + fa),
                        Posicao = emX ? (ox + fa - 2 * ht, oy + (t0 + t1) / 2) : (ox + (t0 + t1) / 2, oy + fa + 2 * ht),
                    });
                }
            }
            // negativas sobre as paredes internas entre células cobertas: ¼ do menor vão de cada lado, dobras para baixo
            double nb = v.Numero("LNegBitola"), ne = v.Numero("LNegEsp");
            foreach (var p in c.Paredes.Where(p => !p.Externa))
            {
                var lados = cels.Where(q => p.Horizontal ? (Math.Abs(q.Item2 - p.Y1) < 1 || Math.Abs(q.Item4 - p.Y0) < 1)
                                                           : (Math.Abs(q.Item1 - p.X1) < 1 || Math.Abs(q.Item3 - p.X0) < 1)).ToList();
                if (lados.Count < 2) continue;
                double menor = lados.Min(q => p.Horizontal ? q.Item4 - q.Item2 : q.Item3 - q.Item1);
                double braco = Math.Ceiling(menor / 4 / 5) * 5;
                double L = 2 * braco + t;
                double ext = p.Comprimento;
                int n = Qtd(c, ext, ne);
                BarraTemplate b;
                if (p.Horizontal)
                {
                    double xm = ox + (p.X0 + p.X1) * 0.6, yc = oy + (p.Y0 + p.Y1) / 2;
                    b = Barra(c, $"{l.Nome} negativa {p.Nome}", nb, ne, n, c.escL, (xm - dob, yc - L / 2), (xm, yc - L / 2), (xm, yc + L / 2), (xm - dob, yc + L / 2));
                    b.PosicaoDescricao = (xm + 1.4 * ht, yc); b.RotacaoDescricao = Math.PI / 2;
                    c.R.Faixas.Add(new FaixaTemplate { Barra = b.Nome, Tipo = TipoFaixa.Ortogonal, Espacamento = ne, Escala = c.escL, MostrarDiametro = false,
                        P1 = (ox + p.X0 + cl, yc + L / 4), P2 = (ox + p.X1 - cl, yc + L / 4), Posicao = (ox + (p.X0 + p.X1) / 2, yc + L / 4 + 2 * ht) });
                }
                else
                {
                    double ym = oy + (p.Y0 + p.Y1) * 0.6, xc = ox + (p.X0 + p.X1) / 2;
                    b = Barra(c, $"{l.Nome} negativa {p.Nome}", nb, ne, n, c.escL, (xc - L / 2, ym - dob), (xc - L / 2, ym), (xc + L / 2, ym), (xc + L / 2, ym - dob));
                    b.PosicaoDescricao = (xc, ym + 1.4 * ht);
                    c.R.Faixas.Add(new FaixaTemplate { Barra = b.Nome, Tipo = TipoFaixa.Ortogonal, Espacamento = ne, Escala = c.escL, MostrarDiametro = false,
                        P1 = (xc + L / 4, oy + p.Y0 + cl), P2 = (xc + L / 4, oy + p.Y1 - cl), Posicao = (xc + L / 4 + 2 * ht, oy + (p.Y0 + p.Y1) / 2) });
                }
            }
            Titulo(c, "ARMADURA DA LAJE " + l.Nome, "POSITIVAS E NEGATIVAS", ox, oy - 8 * ht, c.escL);
            c.YLivre = oy - 35 * ht;
        }
        c.R.PosicaoRA = (2 * c.Lx + 4 * c.Ly + 200 * c.htP, 0);
    }

    // ======================== verificações (NBR 6118) ========================

    private static void Verificar(Ctx c)
    {
        var v = c.V;
        var tr = new RegrasTraspasse { Fck = v.Numero("Fck"), PercentualEmendado = 100 };
        double hb = v.Numero("HBitola"), ab = v.Numero("ArrBitola"), vb = v.Numero("VBitola");
        double l0 = tr.Comprimento(hb, c.Aco), lbArr = Math.Ceiling(tr.Lb(ab, c.Aco)), l0Arr = tr.Comprimento(Math.Max(ab, vb), c.Aco);
        if (v.Numero("CantoPerna") < l0)
            c.R.Avisos.Add($"L dos cantos: perna {Num(v.Numero("CantoPerna"))} cm menor que o traspasse da NBR 6118 ({Num(l0)} cm, ø{hb:0.0}, C{Num(v.Numero("Fck"))}).");
        if (v.Numero("ArrParede") < l0Arr)
            c.R.Avisos.Add($"Arranque: perna na parede {Num(v.Numero("ArrParede"))} cm menor que o traspasse com as verticais ({Num(l0Arr)} cm).");
        if (v.Numero("ArrFundo") < lbArr)
            c.R.Avisos.Add($"Arranque: perna no fundo {Num(v.Numero("ArrFundo"))} cm menor que lb ({Num(lbArr)} cm).");
        if (c.t - 2 * c.cp < 3) c.R.Avisos.Add("Parede fina demais para o cobrimento informado.");
        if (c.hf - 2 * c.cl < 3) c.R.Avisos.Add("Fundo fino demais para o cobrimento informado.");
        foreach (var l in c.Lajes)
            if (l.Nivel > c.H + 1e-6) c.R.Avisos.Add($"Laje {l.Nome}: nível acima do topo das paredes.");
    }
}
