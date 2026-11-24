using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Elementos.Escada;

/// <summary>Funcionamento estrutural da escada em laje (apostila de escadas, 7.3.1.2).</summary>
public enum TipoEscada
{
    /// <summary>Armada longitudinalmente: vence o vão entre os apoios das pontas (vigas ou lajes), com patamares opcionais.</summary>
    Longitudinal = 0,
    /// <summary>Armada transversalmente: apoiada nas vigas ou paredes dos dois lados do lance; vence a largura.</summary>
    Transversal = 1,
    /// <summary>Em balanço, engastada numa viga ou parede de um lado do lance.</summary>
    Balanco = 2,
}

/// <summary>Como a escada é executada (D43).</summary>
public enum ExecucaoEscada
{
    MoldadaNoLocal = 0,
    /// <summary>Pré-moldada: peças biapoiadas com ligação em cada ponta (apoio direto ou dente), pinos e içamento.</summary>
    PreMoldada = 1,
}

/// <summary>Material entre a peça e o apoio: define a força horizontal Hd = k·Fd (NBR 9062, 7.3.9).</summary>
public enum ApoioPreMoldado
{
    Elastomero = 0,
    Argamassa = 1,
    JuntaSeca = 2,
    ChapasNaoSoldadas = 3,
    PTFE = 4,
    ConcretoChapa = 5,
}

/// <summary>Forma da ponta da peça pré-moldada.</summary>
public enum PontaPreMoldada
{
    /// <summary>Apoio direto, sem recorte (NBR 9062, 7.5): a ponta do patamar assenta no apoio.</summary>
    ApoioDireto = 0,
    /// <summary>Dente (NBR 9062, 7.4): a ponta tem a altura reduzida e assenta num apoio mais alto.</summary>
    Dente = 1,
}

/// <summary>Que peça é: o lance (com os patamares das pontas) ou só um patamar plano (patamar intermediário pré-moldado).</summary>
public enum PecaEscada
{
    Lance = 0,
    /// <summary>Laje plana biapoiada (o patamar intermediário como peça à parte): vão entre os apoios das pontas, sem degraus.</summary>
    Patamar = 1,
}

/// <summary>
/// Carga vinda de outra peça (a ponta de um lance ou de um patamar apoiada nesta), por metro de largura desta peça, uniforme de
/// <see cref="X"/> a X + <see cref="Comprimento"/> (x horizontal a partir do eixo do apoio inferior). Valores característicos (kN/m):
/// a resultante na faixa de 1 m.
/// </summary>
public sealed class CargaApoiada
{
    public string Nome { get; set; } = "";
    public double X { get; set; }
    public double Comprimento { get; set; } = 10;
    public double G { get; set; }
    public double Q { get; set; }
    public CargaApoiada Copia() => (CargaApoiada)MemberwiseClone();
}

/// <summary>Valores padrão da escada pré-moldada. Vão ser lidos do arquivo de configurações do PCEB (em criação noutra branch).</summary>
public static class PadroesPreMoldado
{
    /// <summary>k de Hd = k·Fd (NBR 9062, 7.3.9).</summary>
    public static double CoefHd(ApoioPreMoldado a) => a switch
    {
        ApoioPreMoldado.JuntaSeca => 0.8,
        ApoioPreMoldado.Argamassa => 0.5,
        ApoioPreMoldado.ConcretoChapa => 0.4,
        ApoioPreMoldado.ChapasNaoSoldadas => 0.25,
        ApoioPreMoldado.PTFE => 0.08,
        _ => 0.16,
    };

    public static string Nome(ApoioPreMoldado a) => a switch
    {
        ApoioPreMoldado.JuntaSeca => "junta seca",
        ApoioPreMoldado.Argamassa => "argamassa",
        ApoioPreMoldado.ConcretoChapa => "concreto com chapa metálica",
        ApoioPreMoldado.ChapasNaoSoldadas => "chapas não soldadas",
        ApoioPreMoldado.PTFE => "PTFE",
        _ => "elastômero",
    };

    public const double CoefDinamico = 1.3;
}

/// <summary>Ligação de uma ponta da escada pré-moldada (D43): forma, apoio e pinos.</summary>
public sealed class LigacaoEscada
{
    public PontaPreMoldada Ponta { get; set; } = PontaPreMoldada.ApoioDireto;
    /// <summary>Comprimento de apoio (cm): a peça vai meio comprimento além do centro do apoio.</summary>
    public double ComprimentoApoio { get; set; } = 15;
    /// <summary>Dente: altura da parte que assenta (cm) e comprimento do recorte a partir da ponta (cm).</summary>
    public double AlturaDente { get; set; } = 10;
    public double ComprimentoDente { get; set; } = 25;
    public ApoioPreMoldado Apoio { get; set; } = ApoioPreMoldado.Elastomero;
    /// <summary>Pinos (chumbadores grauteados) na ponta: quantidade, bitola (mm), furo na peça (mm) e embutimento no apoio (cm).</summary>
    public int Pinos { get; set; } = 2;
    public double BitolaPino { get; set; } = 16;
    public double FuroPino { get; set; } = 40;
    public double EmbutimentoPino { get; set; } = 20;

    public LigacaoEscada Copia() => (LigacaoEscada)MemberwiseClone();
    /// <summary>Distância horizontal do centro do apoio à ponta da peça (cm).</summary>
    public double Balanco => ComprimentoApoio / 2;
    /// <summary>Altura que a peça tem sobre o apoio (cm): a do dente ou a do patamar.</summary>
    public double AlturaNoApoio(double hp) => Ponta == PontaPreMoldada.Dente ? Math.Min(AlturaDente, hp) : hp;
}

/// <summary>
/// Escada em laje (D42): um lance reto, armado longitudinalmente (entre os apoios das pontas, com patamares opcionais),
/// transversalmente (entre as vigas ou paredes laterais) ou em balanço (de uma viga ou parede lateral). Entrada digitada pelo
/// engenheiro ou lida do modelo. Unidades: cm, kN, MPa.
/// Convenção da elevação: x horizontal do eixo do apoio inferior para o superior; z vertical a partir do piso acabado
/// inferior (topo do patamar inferior = 0; topo do patamar superior = <see cref="Desnivel"/>).
/// </summary>
public sealed class EscadaLaje
{
    public TipoEscada Tipo { get; set; } = TipoEscada.Longitudinal;
    /// <summary>Lance ou patamar plano (só armada longitudinalmente; o patamar usa <see cref="ComprimentoPatamar"/> e a espessura do patamar).</summary>
    public PecaEscada Peca { get; set; } = PecaEscada.Lance;
    /// <summary>Patamar: vão horizontal entre os eixos dos apoios (cm).</summary>
    public double ComprimentoPatamar { get; set; } = 150;
    /// <summary>Cargas de outras peças apoiadas nesta (escadas em L e U montadas por peças).</summary>
    public List<CargaApoiada> CargasApoiadas { get; set; } = new List<CargaApoiada>();
    public bool SoPatamar => Peca == PecaEscada.Patamar && Tipo == TipoEscada.Longitudinal;
    public ExecucaoEscada Execucao { get; set; } = ExecucaoEscada.MoldadaNoLocal;
    /// <summary>Pré-moldada: ligação da ponta de baixo e da de cima.</summary>
    public LigacaoEscada LigacaoInferior { get; set; } = new LigacaoEscada();
    public LigacaoEscada LigacaoSuperior { get; set; } = new LigacaoEscada();
    /// <summary>Içamento: coeficiente dinâmico (NBR 9062, 5.3) e fcj na desforma (MPa; 0 = 70 % do fck).</summary>
    public double CoefDinamico { get; set; } = PadroesPreMoldado.CoefDinamico;
    public double FcjIcamento { get; set; }
    public bool PreMoldada => Execucao == ExecucaoEscada.PreMoldada && Tipo == TipoEscada.Longitudinal;
    // ---------------- geometria ----------------
    /// <summary>Desnível entre os pisos acabados (cm).</summary>
    public double Desnivel { get; set; } = 288;
    /// <summary>Número de espelhos; 0 = o programa escolhe pelo <see cref="EspelhoAlvo"/>.</summary>
    public int Espelhos { get; set; }
    /// <summary>Espelho desejado (cm) quando o número de espelhos é automático.</summary>
    public double EspelhoAlvo { get; set; } = 17.5;
    /// <summary>Piso do degrau (cm).</summary>
    public double Piso { get; set; } = 28;
    /// <summary>Largura da escada (cm).</summary>
    public double Largura { get; set; } = 120;
    /// <summary>Espessura da laje do lance, medida perpendicularmente ao plano inclinado (cm).</summary>
    public double Espessura { get; set; } = 12;
    /// <summary>Espessura do patamar (cm); 0 = a mesma do lance.</summary>
    public double EspessuraPatamar { get; set; }
    /// <summary>Patamar inferior: distância horizontal do eixo do apoio inferior ao 1º espelho (cm); 0 = sem patamar.</summary>
    public double PatamarInferior { get; set; }
    /// <summary>Patamar superior: distância horizontal do último espelho ao eixo do apoio superior (cm); 0 = sem patamar.</summary>
    public double PatamarSuperior { get; set; }
    /// <summary>Largura de cada apoio (viga ou parede) na direção do vão (cm): a armadura principal entra no apoio até a face oposta.
    /// Na transversal e no balanço, os apoios são os laterais.</summary>
    public double LarguraApoio { get; set; } = 20;
    /// <summary>Altura livre disponível sob o lance (cm), se conhecida (0 = não verificar).</summary>
    public double AlturaLivre { get; set; }

    // ---------------- materiais ----------------
    public double Fck { get; set; } = 25;
    public string Aco { get; set; } = "CA-50";
    /// <summary>Cobrimento nominal (cm). Laje, classe II: 2,5 cm (NBR 6118, Tabela 7.2).</summary>
    public double Cobrimento { get; set; } = 2.5;
    public string ClasseAgressividade { get; set; } = "II";
    /// <summary>Peso específico do concreto armado (kN/m³).</summary>
    public double PesoEspecifico { get; set; } = 25;

    // ---------------- cargas (características, por m² de projeção horizontal) ----------------
    /// <summary>Revestimento (kN/m²): sem o detalhe da arquitetura, 1,0 (granilite com espelho).</summary>
    public double Revestimento { get; set; } = 1.0;
    /// <summary>Sobrecarga de uso (kN/m²): 2,5 sem acesso ao público, 3,0 com acesso (NB-5; conferir na NBR 6120:2019).</summary>
    public double Sobrecarga { get; set; } = 2.5;
    /// <summary>Peso do parapeito ou guarda-corpo apoiado na escada (kN/m, por borda).</summary>
    public double Parapeito { get; set; }
    /// <summary>Bordas com parapeito (0, 1 ou 2).</summary>
    public int BordasComParapeito { get; set; }

    public EscadaLaje Copia()
    {
        var c = (EscadaLaje)MemberwiseClone();
        c.LigacaoInferior = LigacaoInferior.Copia(); c.LigacaoSuperior = LigacaoSuperior.Copia();
        c.CargasApoiadas = CargasApoiadas.Select(a => a.Copia()).ToList();
        return c;
    }
}

/// <summary>Trecho do eixo da escada na elevação (patamar ou lance).</summary>
public sealed class TrechoEscada
{
    public string Nome { get; set; } = "";
    public bool Inclinado { get; set; }
    /// <summary>Início e fim no eixo (x, z em cm).</summary>
    public (double X, double Z) P0 { get; set; }
    public (double X, double Z) P1 { get; set; }
    /// <summary>Espessura perpendicular ao eixo (cm).</summary>
    public double H { get; set; }
    public double Angulo => Math.Atan2(P1.Z - P0.Z, P1.X - P0.X);
    public double Comprimento => Math.Sqrt(Math.Pow(P1.X - P0.X, 2) + Math.Pow(P1.Z - P0.Z, 2));
    public double ZEm(double x) => Math.Abs(P1.X - P0.X) < 1e-9 ? P0.Z : P0.Z + (x - P0.X) * (P1.Z - P0.Z) / (P1.X - P0.X);
}

/// <summary>Geometria resolvida da escada: espelhos, ângulo, eixo (para a estática e o MEF) e o contorno real com os degraus.</summary>
public sealed class GeometriaEscada
{
    public int N { get; set; }
    public double Espelho { get; set; }
    public double Piso { get; set; }
    public double Alfa { get; set; }
    public double Cos => Math.Cos(Alfa);
    public double Sen => Math.Sin(Alfa);
    /// <summary>Projeção horizontal do lance, (n − 1)·piso (cm).</summary>
    public double LanceHorizontal { get; set; }
    /// <summary>x do 1º e do último espelho (cm).</summary>
    public double X1 { get; set; }
    public double X2 { get; set; }
    /// <summary>Vão horizontal entre os eixos dos apoios (cm).</summary>
    public double Vao { get; set; }
    public double H { get; set; }
    public double Hp { get; set; }
    /// <summary>Trechos do eixo (patamar inferior, lance, patamar superior), em ordem.</summary>
    public List<TrechoEscada> Trechos { get; } = new List<TrechoEscada>();
    /// <summary>Contorno da peça na elevação (cm), anti-horário, com os degraus: para a prévia, o MEF de chapa e a família.</summary>
    public List<(double X, double Z)> Contorno { get; } = new List<(double, double)>();
    /// <summary>Quina inferior do lance com o patamar superior (onde a face de baixo é reentrante): x, z da face inferior.</summary>
    public (double X, double Z)? QuinaReentrante { get; set; }
    /// <summary>Quina da face inferior do lance com o patamar inferior (saliente).</summary>
    public (double X, double Z)? QuinaInferior { get; set; }
    /// <summary>Pré-moldada: x das pontas da peça (além dos centros dos apoios em 0 e no vão) e das faces dos recortes dos dentes
    /// (= ponta quando não há dente); z do assento em cada apoio (face de baixo da peça sobre o apoio).</summary>
    public double XPontaInf { get; set; }
    public double XPontaSup { get; set; }
    public double XRecorteInf { get; set; }
    public double XRecorteSup { get; set; }
    public double ZAssentoInf { get; set; }
    public double ZAssentoSup { get; set; }
    /// <summary>Ajustes feitos na entrada (não impedem o cálculo).</summary>
    public List<string> Avisos { get; } = new List<string>();

    public TrechoEscada TrechoEm(double x) =>
        Trechos.FirstOrDefault(t => x >= Math.Min(t.P0.X, t.P1.X) - 1e-9 && x <= Math.Max(t.P0.X, t.P1.X) + 1e-9) ?? Trechos.Last();

    /// <summary>Monta a geometria da entrada. Erros de entrada (espelho nulo etc.) vão para <paramref name="erros"/>.</summary>
    public static GeometriaEscada De(EscadaLaje e, List<string> erros = null)
    {
        erros ??= new List<string>();
        if (e.SoPatamar) return DePatamar(e, erros);
        var g = new GeometriaEscada();
        int n = e.Espelhos > 0 ? e.Espelhos : (int)Math.Max(2, Math.Round(e.Desnivel / Math.Max(1, e.EspelhoAlvo)));
        g.N = n;
        g.Espelho = e.Desnivel / n;
        g.Piso = e.Piso;
        if (e.Piso <= 0 || e.Desnivel <= 0) { erros.Add("Piso e desnível precisam ser positivos."); g.Piso = Math.Max(1, e.Piso); }
        g.Alfa = Math.Atan2(g.Espelho, g.Piso);
        g.LanceHorizontal = (n - 1) * g.Piso;
        g.H = e.Espessura;
        g.Hp = e.EspessuraPatamar > 0 ? e.EspessuraPatamar : e.Espessura;
        double pi = Math.Max(0, e.PatamarInferior), ps = Math.Max(0, e.PatamarSuperior);
        bool pm = e.PreMoldada;
        if (pm)
        {
            // a peça pré-moldada assenta num patamar horizontal: pelo menos o comprimento de apoio (ou o dente) de cada lado
            double minI = Math.Max(e.LigacaoInferior.ComprimentoApoio, e.LigacaoInferior.Ponta == PontaPreMoldada.Dente ? e.LigacaoInferior.ComprimentoDente : 0);
            double minS = Math.Max(e.LigacaoSuperior.ComprimentoApoio, e.LigacaoSuperior.Ponta == PontaPreMoldada.Dente ? e.LigacaoSuperior.ComprimentoDente : 0);
            if (pi < minI) { g.Avisos.Add($"Pré-moldada: patamar inferior aumentado para {minI:0} cm (a ponta assenta num trecho horizontal)."); pi = minI; }
            if (ps < minS) { g.Avisos.Add($"Pré-moldada: patamar superior aumentado para {minS:0} cm (a ponta assenta num trecho horizontal)."); ps = minS; }
        }
        g.X1 = pi;
        g.X2 = pi + g.LanceHorizontal;
        g.Vao = g.X2 + ps;
        double tg = Math.Tan(g.Alfa), c = Math.Cos(g.Alfa), H = e.Desnivel, h = g.H, hp = g.Hp;

        // linha dos cantos internos dos degraus: z = (x − X1)·tgα; face inferior do lance: essa linha baixada h/cosα;
        // eixo do lance: baixada h/(2cosα). Patamares: eixo a hp/2 abaixo do piso.
        double ZEixoLance(double x) => (x - g.X1) * tg - h / (2 * c);
        double ZFundoLance(double x) => (x - g.X1) * tg - h / c;
        // quinas do eixo (interseção do eixo do lance com o do patamar)
        double xk1 = pi > 0 ? g.X1 + (h / (2 * c) - hp / 2) / tg : 0;
        // patamar superior: piso em z = H; o último espelho fica em X2 e sobe de (n−1)e até H
        double xk2 = ps > 0 ? g.X1 + (H - hp / 2 + h / (2 * c)) / tg : g.Vao;
        if (pi > 0 && xk1 < 0) { erros.Add("Patamar inferior curto demais para a espessura: aumente o patamar."); xk1 = 0; }
        if (ps > 0 && xk2 > g.Vao) { erros.Add("Patamar superior curto demais para a espessura: aumente o patamar."); xk2 = g.Vao; }
        if (pi > 0) g.Trechos.Add(new TrechoEscada { Nome = "Patamar inferior", P0 = (0, -hp / 2), P1 = (xk1, -hp / 2), H = hp });
        g.Trechos.Add(new TrechoEscada { Nome = "Lance", Inclinado = true, P0 = (xk1, pi > 0 ? -hp / 2 : ZEixoLance(0)), P1 = (xk2, ps > 0 ? H - hp / 2 : ZEixoLance(g.Vao)), H = h });
        if (ps > 0) g.Trechos.Add(new TrechoEscada { Nome = "Patamar superior", P0 = (xk2, H - hp / 2), P1 = (g.Vao, H - hp / 2), H = hp });

        // contorno real (anti-horário a partir do canto inferior esquerdo)
        var k = g.Contorno;
        double hpI = hp, hpS = hp;
        g.XPontaInf = 0; g.XPontaSup = g.Vao; g.XRecorteInf = 0; g.XRecorteSup = g.Vao; g.ZAssentoInf = -hp; g.ZAssentoSup = H - hp;
        if (pm)
        {
            var li = e.LigacaoInferior; var ls = e.LigacaoSuperior;
            g.XPontaInf = -li.Balanco; g.XPontaSup = g.Vao + ls.Balanco;
            bool di = li.Ponta == PontaPreMoldada.Dente, ds = ls.Ponta == PontaPreMoldada.Dente;
            g.XRecorteInf = di ? g.XPontaInf + li.ComprimentoDente : g.XPontaInf;
            g.XRecorteSup = ds ? g.XPontaSup - ls.ComprimentoDente : g.XPontaSup;
            g.ZAssentoInf = -li.AlturaNoApoio(hp); g.ZAssentoSup = H - ls.AlturaNoApoio(hp);
            if (di && g.XRecorteInf > xb1Previo(e, g, h, c, tg) - 1) erros.Add("Dente inferior mais comprido que o patamar: aumente o patamar ou encurte o dente.");
        }
        // face inferior: patamar inferior → lance → patamar superior
        double xb1 = pi > 0 ? g.X1 + (h / c - hp) / tg : 0;          // quina da face inferior com o patamar inferior
        double xb2 = ps > 0 ? g.X1 + (H - hp + h / c) / tg : g.Vao;  // quina da face inferior com o patamar superior
        if (pm)
        {
            // ponta de baixo: dente (sobe a face de baixo até a altura do dente) ou apoio direto
            if (g.XRecorteInf > g.XPontaInf + 1e-9) { k.Add((g.XPontaInf, g.ZAssentoInf)); k.Add((g.XRecorteInf, g.ZAssentoInf)); }
            k.Add((g.XRecorteInf, -hp)); k.Add((xb1, -hp)); g.QuinaInferior = (xb1, -hp);
        }
        else if (pi > 0) { k.Add((0, -hp)); k.Add((xb1, -hp)); g.QuinaInferior = (xb1, -hp); }
        else k.Add((0, ZFundoLance(0)));
        if (pm)
        {
            k.Add((xb2, H - hp)); g.QuinaReentrante = (xb2, H - hp);
            k.Add((g.XRecorteSup, H - hp));
            if (g.XRecorteSup < g.XPontaSup - 1e-9) { k.Add((g.XRecorteSup, g.ZAssentoSup)); k.Add((g.XPontaSup, g.ZAssentoSup)); }
        }
        else if (ps > 0) { k.Add((xb2, H - hp)); k.Add((g.Vao, H - hp)); g.QuinaReentrante = (xb2, H - hp); }
        else k.Add((g.Vao, ZFundoLance(g.Vao)));
        // face superior, da direita para a esquerda: piso do patamar superior, degraus, piso do patamar inferior
        // (sem patamar superior o último espelho é a face da ponta, encostada no apoio)
        k.Add((pm ? g.XPontaSup : g.Vao, H));
        for (int i = n; i >= 1; i--)
        {
            double xr = g.X1 + (i - 1) * g.Piso;   // espelho i (1 = o primeiro), de (i−1)·e até i·e; piso i à direita dele
            k.Add((xr, i * g.Espelho));
            k.Add((xr, (i - 1) * g.Espelho));
        }
        if (pm) k.Add((g.XPontaInf, 0));
        else if (pi > 0) k.Add((0, 0));
        LimparContorno(k);
        return g;
    }

    /// <summary>Patamar plano: um trecho horizontal de 0 ao vão, topo em z = 0; pontas e dentes como no lance pré-moldado.</summary>
    private static GeometriaEscada DePatamar(EscadaLaje e, List<string> erros)
    {
        var g = new GeometriaEscada();
        g.N = 0; g.Espelho = 0; g.Piso = Math.Max(1, e.Piso); g.Alfa = 0; g.LanceHorizontal = 0;
        g.Hp = e.EspessuraPatamar > 0 ? e.EspessuraPatamar : e.Espessura; g.H = g.Hp;
        double hp = g.Hp, L = e.ComprimentoPatamar;
        if (L < 30) { erros.Add("Patamar: vão menor que 30 cm."); L = Math.Max(L, 30); }
        g.Vao = L; g.X1 = L; g.X2 = L;   // sem lance: tudo é patamar
        g.Trechos.Add(new TrechoEscada { Nome = "Patamar", P0 = (0, -hp / 2), P1 = (L, -hp / 2), H = hp });
        bool pm = e.PreMoldada;
        var li = e.LigacaoInferior; var ls = e.LigacaoSuperior;
        g.XPontaInf = pm ? -li.Balanco : 0; g.XPontaSup = pm ? L + ls.Balanco : L;
        bool di = pm && li.Ponta == PontaPreMoldada.Dente, ds = pm && ls.Ponta == PontaPreMoldada.Dente;
        g.XRecorteInf = di ? g.XPontaInf + li.ComprimentoDente : g.XPontaInf;
        g.XRecorteSup = ds ? g.XPontaSup - ls.ComprimentoDente : g.XPontaSup;
        g.ZAssentoInf = pm ? -li.AlturaNoApoio(hp) : -hp; g.ZAssentoSup = pm ? -ls.AlturaNoApoio(hp) : -hp;
        if (g.XRecorteInf > g.XRecorteSup - 10) erros.Add("Patamar: os dentes das duas pontas se encontram — encurte os dentes.");
        var k = g.Contorno;
        if (g.XRecorteInf > g.XPontaInf + 1e-9) { k.Add((g.XPontaInf, g.ZAssentoInf)); k.Add((g.XRecorteInf, g.ZAssentoInf)); }
        k.Add((g.XRecorteInf, -hp));
        k.Add((g.XRecorteSup, -hp));
        if (g.XRecorteSup < g.XPontaSup - 1e-9) { k.Add((g.XRecorteSup, g.ZAssentoSup)); k.Add((g.XPontaSup, g.ZAssentoSup)); }
        k.Add((g.XPontaSup, 0));
        k.Add((g.XPontaInf, 0));
        LimparContorno(k);
        return g;
    }

    private static double xb1Previo(EscadaLaje e, GeometriaEscada g, double h, double c, double tg) => g.X1 + (h / c - g.Hp) / tg;

    /// <summary>Tira pontos repetidos e pontos no meio de um segmento reto (colineares).</summary>
    public static void LimparContorno(List<(double X, double Z)> k)
    {
        bool mudou = true;
        while (mudou && k.Count > 3)
        {
            mudou = false;
            for (int i = 0; i < k.Count && k.Count > 3; i++)
            {
                var a = k[(i - 1 + k.Count) % k.Count]; var b = k[i]; var c = k[(i + 1) % k.Count];
                double cruz = (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);
                bool igual = Math.Abs(b.X - a.X) < 1e-6 && Math.Abs(b.Z - a.Z) < 1e-6;
                if (igual || Math.Abs(cruz) < 1e-6) { k.RemoveAt(i); mudou = true; break; }
            }
        }
    }
}
