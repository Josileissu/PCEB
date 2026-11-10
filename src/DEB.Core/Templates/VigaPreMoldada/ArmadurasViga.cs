using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Estribos;
using DEB.Core.Modelo;
using DEB.Core.Templates.PilarPreMoldado;

namespace DEB.Core.Templates.VigaPreMoldada;

/// <summary>Uma linha da lista de longitudinais da viga (cm a partir da ponta esquerda).</summary>
public sealed class LinhaLongViga
{
    public int Linha { get; set; }
    public string Funcao { get; set; } = "";
    /// <summary>Barras (pele: barras por face).</summary>
    public int Quantidade { get; set; }
    public double Bitola { get; set; }
    /// <summary>1 = junto da face; 2 = segunda camada.</summary>
    public int Camada { get; set; } = 1;
    public double DobraE { get; set; }
    public double DobraD { get; set; }
    public double Ini { get; set; }
    public double Fim { get; set; }
    public bool Inferior => Funcao == TemplateVigaPreMoldada.FInferior;
    public bool Pele => Funcao == TemplateVigaPreMoldada.FPele;
}

/// <summary>Zona de estribos ao longo da viga (cm a partir da ponta esquerda).</summary>
public sealed class ZonaEstriboViga
{
    public string Nome { get; set; } = "";
    public double X1 { get; set; }
    public double X2 { get; set; }
    public double Esp { get; set; }
    /// <summary>Posições dos estribos (eixo), da regra de faixas do plugin (a mesma que a faixa usa para contar).</summary>
    public List<double> Posicoes { get; set; } = new List<double>();
}

/// <summary>
/// Etapa 2 da viga (2026-11-08): armaduras passivas no padrão das pranchas 316-6208/6219 e GRU 702 — ARMADURAS EM ELEVAÇÃO com
/// a forma em linha fina e os estribos dentro, as longitudinais DESENHADAS FORA da forma (uma por linha, acima as superiores
/// e a pele, abaixo as inferiores) com o rótulo da função, faixas de estribos por zona cotadas embaixo da forma, SEÇÃO X-X -
/// ARMADURA ao lado de cada seção de forma (barras em ponto e uma indicação por linha) e a forma do estribo ao lado da seção
/// do vão. A armadura do dente (estribos Gerber, suspensão, grampo, costura, tirante) é a etapa 3.
/// </summary>
public sealed partial class TemplateVigaPreMoldada
{
    public const string FInferior = "Inferior", FSuperior = "Superior", FPele = "Pele", FConstrutiva = "Construtiva";
    public static readonly string[] FuncoesLong = { FInferior, FSuperior, FPele, FConstrutiva };
    public const string EstFechado = "Fechado", EstAberto = "Aberto (U) com saliência para a capa";
    private const string G7 = "07. Armadura longitudinal", G8 = "08. Estribos";
    private static readonly string[] BitolasLongViga = { "8", "10", "12.5", "16", "20", "22", "25", "32" };

    private static IEnumerable<ParametroTemplate> ParametrosArmadura() => new[]
    {
        P(G7, "LongAco", "Aço", "CA-50", tipo: TipoParametro.Opcao, opcoes: new[] { "CA-50", "CA-60" }),
        new ParametroTemplate
        {
            Grupo = G7, Nome = "Longitudinais", Rotulo = "Barras longitudinais", Tipo = TipoParametro.Tabela, NomeItem = "linha",
            Padrao = "Inferior|3|16|1|0|0||;Superior|2|10|1|0|0||;Pele|2|10|1|0|0||",
            Descricao = "Clique em … para a lista: função (inferior, superior, pele, construtiva), quantidade (pele: por face), bitola, " +
                        "camada (2 = segunda camada), dobras nas pontas (0 = reta) e início/fim (cm da ponta esquerda; vazio = a viga toda; " +
                        "a inferior para antes dos dentes).",
            Colunas =
            {
                new ColunaTabela { Nome = "Funcao", Rotulo = "Função", Tipo = TipoParametro.Opcao, Opcoes = FuncoesLong.ToList(), Padrao = FInferior, Largura = 100 },
                new ColunaTabela { Nome = "Qtd", Rotulo = "Quantidade", Tipo = TipoParametro.Numero, Padrao = "2", Largura = 75 },
                new ColunaTabela
                {
                    Nome = "Bitola", Rotulo = "Bitola (mm)", Tipo = TipoParametro.Opcao, Opcoes = BitolasLongViga.ToList(), Padrao = "12.5", Largura = 80,
                    Ajustar = (l, s) => KitDesenho.NormalizarBitola(s),
                },
                new ColunaTabela { Nome = "Camada", Rotulo = "Camada", Tipo = TipoParametro.Opcao, Opcoes = { "1", "2" }, Padrao = "1", Largura = 60 },
                new ColunaTabela { Nome = "DobraE", Rotulo = "Dobra esq. (cm)", Tipo = TipoParametro.Numero, Padrao = "0", Largura = 90 },
                new ColunaTabela { Nome = "DobraD", Rotulo = "Dobra dir. (cm)", Tipo = TipoParametro.Numero, Padrao = "0", Largura = 90 },
                new ColunaTabela { Nome = "Ini", Rotulo = "Início (cm)", Tipo = TipoParametro.Numero, Padrao = "", Largura = 75 },
                new ColunaTabela { Nome = "Fim", Rotulo = "Fim (cm)", Tipo = TipoParametro.Numero, Padrao = "", Largura = 70 },
            },
        },
        P(G7, "InfSoldada", "Inferior com barra transversal soldada nas pontas", "Sim", tipo: TipoParametro.SimNao,
          descricao: "NBR 9062 7.4/7.5: ancoragem da armadura inferior com barra transversal soldada (como nas pranchas 316 e 702)."),
        P(G8, "EstAco", "Aço", "CA-50", tipo: TipoParametro.Opcao, opcoes: new[] { "CA-50", "CA-60" }),
        P(G8, "EstBitola", "Bitola", "6.3", "mm", TipoParametro.Bitola, 5, 16),
        P(G8, "EstTipo", "Tipo", EstFechado, tipo: TipoParametro.Opcao, opcoes: new[] { EstFechado, EstAberto }),
        P(G8, "EstSaliencia", "Saliência acima do topo (estribo aberto)", "15", "cm", min: 0),
        P(G8, "EstZonaPonta", "Zona das pontas: comprimento", "100", "cm", min: 0,
          descricao: "A partir da face do dente (ou da ponta reta), nas duas pontas. 0 = só o espaçamento do vão."),
        P(G8, "EstEspPonta", "Zona das pontas: espaçamento", "10", "cm", min: 2),
        P(G8, "EstEspVao", "Vão: espaçamento", "15", "cm", min: 2),
    };

    // ======================== leitura (testável) ========================

    /// <summary>Trecho padrão de uma função: a inferior entre os dentes (a cobrimento das faces dos recortes); as outras de ponta a ponta.</summary>
    public static (double Ini, double Fim) TrechoPadrao(string funcao, double L, double cobPontas, ExtremidadeViga e, ExtremidadeViga d) =>
        funcao == FInferior
            ? ((e.TemDente ? e.CompDente : 0) + cobPontas, L - (d.TemDente ? d.CompDente : 0) - cobPontas)
            : (cobPontas, L - cobPontas);

    public static List<LinhaLongViga> LerLongitudinais(ValoresTemplate v, double L, double cobPontas, ExtremidadeViga e, ExtremidadeViga d, List<string> avisos)
    {
        var r = new List<LinhaLongViga>();
        var linhas = v.Tabela("Longitudinais");
        for (int k = 1; k <= linhas.Count; k++)
        {
            var l = linhas[k - 1];
            string f = FuncoesLong.FirstOrDefault(x => string.Equals(x, ValoresTemplate.Celula(l, 0), StringComparison.OrdinalIgnoreCase));
            if (f == null) { avisos.Add($"Longitudinal {k}: função \"{ValoresTemplate.Celula(l, 0)}\" desconhecida."); continue; }
            int q = (int)Math.Round(ValoresTemplate.NumeroCelula(l, 1, 0));
            double phi = ValoresTemplate.NumeroCelula(l, 2);
            if (q < 1 || phi <= 0) { avisos.Add($"Longitudinal {k} ({f}): informe a quantidade e a bitola."); continue; }
            var (i0, f0) = TrechoPadrao(f, L, cobPontas, e, d);
            double ini = Math.Max(i0, ValoresTemplate.NumeroCelula(l, 6, i0)), fim = Math.Min(f0, ValoresTemplate.NumeroCelula(l, 7, f0));
            if (fim - ini < 10) { avisos.Add($"Longitudinal {k} ({f}): trecho de {Num(ini, "0.#")} a {Num(fim, "0.#")} cm curto demais."); continue; }
            r.Add(new LinhaLongViga
            {
                Linha = k, Funcao = f, Quantidade = q, Bitola = phi,
                Camada = (int)Math.Round(ValoresTemplate.NumeroCelula(l, 3, 1)) >= 2 ? 2 : 1,
                DobraE = Math.Max(0, ValoresTemplate.NumeroCelula(l, 4, 0)), DobraD = Math.Max(0, ValoresTemplate.NumeroCelula(l, 5, 0)),
                Ini = ini, Fim = fim,
            });
        }
        double h = v.Numero("H");
        if (h > 60 && !r.Any(x => x.Pele))
            avisos.Add($"Viga com h = {Num(h, "0.#")} cm > 60 cm sem armadura de pele (NBR 6118, 17.3.5.2.3): conferir.");
        return r;
    }

    /// <summary>
    /// Zonas de estribos simétricas: ponta E, vão e ponta D, entre as faces dos dentes (ou as pontas retas) menos o cobrimento;
    /// posições pela regra de faixas do plugin (<see cref="CalculoFaixa"/>), a mesma com que a faixa conta os estribos.
    /// </summary>
    /// <param name="fretE">Fretagem (VP-3): a zona da ponta esquerda vai pelo menos até esta distância da ponta (fim da transferência).</param>
    /// <param name="fretD">Idem, medida da ponta direita.</param>
    public static List<ZonaEstriboViga> ZonasEstribos(ValoresTemplate v, ConfiguracaoArmadura cfg, double L, ExtremidadeViga e, ExtremidadeViga d,
        double fretE = 0, double fretD = 0)
    {
        double cob = v.Numero("CobPontas"), phi = v.Numero("EstBitola") / 10;
        double x0 = (e.TemDente ? e.CompDente : 0) + cob + phi / 2 + ExtensaoSuspensao(e),
               x1 = L - (d.TemDente ? d.CompDente : 0) - cob - phi / 2 - ExtensaoSuspensao(d);
        double lz = Math.Max(0, v.Numero("EstZonaPonta")), espP = v.Numero("EstEspPonta"), espV = v.Numero("EstEspVao");
        // fretagem da protensão: a zona da ponta cobre o comprimento de transferência (a suspensão do dente também ajuda)
        double lzE = Math.Max(lz, fretE - x0), lzD = Math.Max(lz, fretD - (L - x1));
        var r = new List<ZonaEstriboViga>();
        if (x1 - x0 < 1) return r;
        // vão menor que um espaçamento: uma zona só (viga curta, ou a suspensão do dente ocupou o lugar)
        if (lzE <= 0 && lzD <= 0)
            r.Add(new ZonaEstriboViga { Nome = "vão", X1 = x0, X2 = x1, Esp = espV });
        else if (lzE + lzD >= x1 - x0 - Math.Max(espV, espP) - 1e-6)
            r.Add(new ZonaEstriboViga { Nome = "ponta", X1 = x0, X2 = x1, Esp = espP });
        else
        {
            if (lzE > 0) r.Add(new ZonaEstriboViga { Nome = "ponta E", X1 = x0, X2 = x0 + lzE, Esp = espP });
            r.Add(new ZonaEstriboViga { Nome = "vão", X1 = x0 + Math.Max(0, lzE), X2 = x1 - Math.Max(0, lzD), Esp = espV });
            if (lzD > 0) r.Add(new ZonaEstriboViga { Nome = "ponta D", X1 = x1 - lzD, X2 = x1, Esp = espP });
        }
        foreach (var z in r)
        {
            int n = CalculoFaixa.Quantidade(z.X2 - z.X1, z.Esp, cfg.EspacamentoInicialFaixa, cfg.ModoQuantidadeFaixa);
            z.Posicoes = CalculoFaixa.Posicoes(z.X2 - z.X1, n, z.Esp, cfg.EspacamentoInicialFaixa).Select(s => z.X1 + s).ToList();
        }
        return r;
    }

    private static string RotuloFuncao(Ctx c, LinhaLongViga l)
    {
        string pt = l.Funcao switch
        {
            FInferior => "ARMADURA INFERIOR", FSuperior => "ARMADURA SUPERIOR", FPele => "(PELE)", _ => "ARMADURA CONSTRUTIVA",
        };
        string en = l.Funcao switch
        {
            FInferior => "BOTTOM REINFORCEMENT", FSuperior => "TOP REINFORCEMENT", FPele => "SKIN", _ => "CONSTRUCTION REINFORCEMENT",
        };
        if (l.Camada == 2) { pt += " - 2ª CAMADA"; en += " - 2ND LAYER"; }
        return string.Join(" / ", c.Linhas(pt, en));
    }

    // ======================== desenho ========================

    /// <summary>ARMADURAS EM ELEVAÇÃO abaixo de <paramref name="yTopo"/>; devolve o y abaixo de tudo.</summary>
    private double ArmadurasEmElevacao(Ctx c, ValoresTemplate v, ConfiguracaoArmadura cfg, double b, double h, double L,
        ExtremidadeViga e, ExtremidadeViga d, List<LinhaLongViga> longs, List<ZonaEstriboViga> zonas, List<(string Id, double X)> cortes, double yTopo,
        List<GrupoCordoalhas> cordSup, AbaViga aba, RessaltoViga ress)
    {
        var r = c.R;
        double hv = c.hV, esc = c.escV;
        TituloVista(c, "ARMADURAS EM ELEVAÇÃO", "REINFORCEMENT ELEVATION", 0, yTopo, esc);
        string aco = v["LongAco"];
        double passo = 7.5 * hv;   // descrição em cima (acima da cota do comprimento) e a função embaixo das cotas

        // linhas de cima (superior, construtiva, pele) e de baixo (inferior), cada uma numa linha fora da forma
        var cima = longs.Where(l => !l.Inferior).OrderBy(l => l.Pele ? 1 : 0).ThenBy(l => l.Camada).ToList();
        var baixo = longs.Where(l => l.Inferior).OrderBy(l => l.Camada).ToList();
        double y = yTopo - 7 * hv;
        // cordoalhas superiores (VP-3) acima das passivas
        if (cordSup.Count > 0) y = CordoalhasEmElevacao(c, cordSup, h, L, e, d, yTopo - 6 * hv, superiores: true) + 1 * hv;
        foreach (var l in cima) { BarraLongitudinal(c, l, aco, y, esc); y -= passo; }

        // forma em linha fina e estribos dentro
        double sal = v["EstTipo"] == EstAberto ? Math.Max(0, v.Numero("EstSaliencia")) : 0;
        double yT = y - 2 * hv - Math.Max(sal, ress.Altura), yB = yT - h;
        var k = new ContornoTemplate { Fechado = true, Layer = "ARM-FORMA" };
        if (e.TemDente) k.Vertices.AddRange(new[] { (0.0, yT - e.Dente.Altura), (e.CompDente, yT - e.Dente.Altura), (e.CompDente, yB) });
        else k.Vertices.Add((0, yB));
        if (d.TemDente) k.Vertices.AddRange(new[] { (L - d.CompDente, yB), (L - d.CompDente, yT - d.Dente.Altura), (L, yT - d.Dente.Altura) });
        else k.Vertices.Add((L, yB));
        k.Vertices.AddRange(new[] { (L, yT), (0.0, yT) });
        r.Contornos.Add(k);
        double cobF = v.Numero("CobFundo"), cobT = v.Numero("CobTopo");
        foreach (var x in zonas.SelectMany(z => z.Posicoes))
            c.Linha(x, yB + cobF, x, yT - cobT + sal + (sal > 0 ? cobT : 0), "ARM-BARRA-DETALHE");
        DenteEmElevacao(c, v, h, L, e, true, yT, yB);
        DenteEmElevacao(c, v, h, L, d, false, yT, yB);
        AbaEmElevacao(c, aba, L, yB, cotar: false);
        // marcas das seções também aqui
        foreach (var (id, xc) in cortes) { c.MarcaCorte(id, xc, yT + ress.Altura + 0.2 * hv, hv); MarcaCorteBaixo(c, id, xc, yB - 0.2 * hv, hv); }

        // faixas de estribos por zona, em cota embaixo da forma (cadeia a partir das pontas)
        double yF = yB - 4.5 * hv;
        // zonas vizinhas com o texto em alturas alternadas (zonas curtas não se encavalam)
        for (int iz = 0; iz < zonas.Count; iz++)
        {
            var z = zonas[iz];
            double yz = yF - (iz % 2) * 3.2 * hv;
            r.Faixas.Add(new FaixaTemplate
            {
                Barra = "estribo", Tipo = TipoFaixa.Ortogonal, P1 = (z.X1, yz), P2 = (z.X2, yz), Posicao = ((z.X1 + z.X2) / 2, yz + 0.8 * hv),
                Espacamento = z.Esp, Escala = esc,
            });
        }

        // estribos da aba: traços na altura da aba e a faixa deles numa linha própria, abaixo das faixas dos estribos da alma
        bool faixaAba = AbaEstribosEmElevacao(c, v, cfg, aba, yB, yF - (zonas.Count > 1 ? 6.4 : 3.2) * hv);
        // ressalto: a faixa dele acima do topo, os estribos em traços e as faixas (estribos e ligação com a capa) abaixo das outras
        int faixasRess = RessaltoEmElevacaoArmaduras(c, v, cfg, ress, L, yT, yF - ((zonas.Count > 1 ? 6.4 : 3.2) + (faixaAba ? 3.2 : 0)) * hv);
        y = yF - (zonas.Count > 1 ? 10 : 7) * hv - (faixaAba ? 3.2 * hv : 0) - faixasRess * 3.2 * hv;
        bool soldada = v.SimNao("InfSoldada");
        foreach (var l in baixo)
        {
            BarraLongitudinal(c, l, aco, y, esc);
            if (soldada && l.Camada == 1)
            {
                double w = b - 2 * v.Numero("CobLateral");
                BarraSoldada(c, $"solda_inf{l.Linha}_E", l.Ini, y, l.Bitola, aco, w, true, esc);
                BarraSoldada(c, $"solda_inf{l.Linha}_D", l.Fim, y, l.Bitola, aco, w, false, esc);
            }
            y -= passo;
        }
        if (aba.Existe && AbaLongitudinal(c, v, aba, y)) y -= passo;
        if (RessaltoLongitudinal(c, v, ress, L, y)) y -= passo;
        y = DenteBarras(c, v, b, L, e, d, y);
        return y;
    }

    /// <summary>Uma linha de longitudinais desenhada fora da forma: a barra com as dobras, a descrição em cima e a função embaixo.</summary>
    private static void BarraLongitudinal(Ctx c, LinhaLongViga l, string aco, double y, double esc)
    {
        double hv = c.hV, s = l.Inferior ? 1 : -1;
        var bt = new BarraTemplate
        {
            Nome = "long" + l.Linha, Bitola = l.Bitola, Aco = aco, Quantidade = l.Quantidade, Repeticoes = l.Pele ? 2 : 1, Escala = esc,
            PosicaoDescricao = ((l.Ini + l.Fim) / 2, y + 2.6 * hv),   // acima da cota do comprimento
        };
        if (l.DobraE > 0) bt.Vertices.Add((l.Ini, y + s * l.DobraE));
        bt.Vertices.Add((l.Ini, y));
        bt.Vertices.Add((l.Fim, y));
        if (l.DobraD > 0) bt.Vertices.Add((l.Fim, y + s * l.DobraD));
        for (int i = 0; i + 1 < bt.Vertices.Count; i++) bt.Formatos.Add(FormatoSegmento.Linha);
        c.R.Barras.Add(bt);
        c.Texto(RotuloFuncao(c, l), (l.Ini + l.Fim) / 2, y - 3.4 * hv, 0.75 * hv, 1);   // abaixo das cotas das dobras e das soldadas
    }

    /// <summary>Barras da linha na seção (centros, coordenadas da seção com a origem no canto inferior esquerdo).</summary>
    public static List<(double X, double Y)> PontosNaSecao(LinhaLongViga l, double b, double hSec, double cobF, double cobT, double cobL,
        double phiT, IReadOnlyList<LinhaLongViga> todas)
    {
        double t = phiT / 10, rr = l.Bitola / 20;
        double xl = cobL + t + rr, xr = b - cobL - t - rr;
        IEnumerable<double> Dividir(double a, double z, int n) => n == 1 ? new[] { (a + z) / 2 } : Enumerable.Range(0, n).Select(i => a + i * (z - a) / (n - 1));
        double Folga(double phi) => Math.Max(2, phi / 10);
        if (l.Pele)
        {
            // por face, entre a 1ª camada inferior e a superior
            double y0 = cobF + t + rr, y1 = hSec - cobT - t - rr;
            var ys = Enumerable.Range(1, l.Quantidade).Select(i => y0 + i * (y1 - y0) / (l.Quantidade + 1)).ToList();
            return ys.Select(yy => (xl, yy)).Concat(ys.Select(yy => (xr, yy))).ToList();
        }
        bool inf = l.Inferior;
        var mesmo = todas.Where(o => o.Inferior == inf && !o.Pele).ToList();
        double phi1 = mesmo.Where(o => o.Camada == 1).Select(o => o.Bitola).DefaultIfEmpty(l.Bitola).Max();
        double r1 = l.Camada == 2 ? phi1 / 20 : rr;              // a 2ª camada parte do centro da 1ª
        double yc = inf ? cobF + t + r1 : hSec - cobT - t - r1;
        if (l.Camada == 2) yc += (inf ? 1 : -1) * (phi1 / 20 + Folga(Math.Max(phi1, l.Bitola)) + rr);
        return Dividir(xl, xr, l.Quantidade).Select(xx => (xx, yc)).ToList();
    }

    /// <summary>y do fundo da SEÇÃO X-X - ARMADURA desenhada com o topo em <paramref name="yTopo"/> (a mesma conta de <see cref="SecaoArmadura"/>).</summary>
    private static double SecaoBase(Ctx c, double yTopo, ExtremidadeViga ext, double h) =>
        yTopo - (c.Idioma == IdiomaDesenho.PTEN ? 4 : 2) * c.hC - 5 * c.hC - (ext?.TemDente == true ? ext.Dente.Altura : h);

    /// <summary>SEÇÃO X-X - ARMADURA no x do corte, com o topo em <paramref name="yTopo"/> (mesma altura da seção de forma).</summary>
    private static void SecaoArmadura(Ctx c, ValoresTemplate v, string id, double xCorte, double b, double h, double L, ExtremidadeViga ext,
        List<LinhaLongViga> longs, List<ZonaEstriboViga> zonas, double x0, double yTopo, AbaViga aba = null, RessaltoViga ress = null)
    {
        double hc = c.hC, y = yTopo;
        foreach (var t in c.Linhas($"SEÇÃO {id}-{id} - ARMADURA", $"SECTION {id}-{id} - REINFORCEMENT")) { c.Texto(t, x0, y, 1.2 * hc); y -= 2 * hc; }
        c.Texto($"ESC 1:{c.escC:0}", x0, y, 0.8 * hc);
        bool comRess = ress != null && ress.Existe;
        double sx0 = x0 + 4 * hc, sy1 = y - 5 * hc - (comRess ? ress.Altura + 3 * hc : 0);
        double hSec = ext?.TemDente == true ? ext.Dente.Altura : h, sy0 = sy1 - hSec;
        if (aba != null || comRess) c.R.Contornos.Add(new ContornoTemplate { Vertices = ContornoSecao(b, hSec, aba, comRess ? ress : null, sx0, sy0), Fechado = true, Layer = "ARM-FORMA" });
        else c.Retangulo(sx0, sy0, sx0 + b, sy1, "ARM-FORMA");
        double cobF = v.Numero("CobFundo"), cobT = v.Numero("CobTopo"), cobL = v.Numero("CobLateral"), phiT = v.Numero("EstBitola");
        bool comEstribo = ext?.TemDente != true && zonas.Count > 0 && xCorte >= zonas[0].X1 - 1 && xCorte <= zonas[zonas.Count - 1].X2 + 1;
        if (comEstribo)
        {
            double t = phiT / 20;
            c.Retangulo(sx0 + cobL + t, sy0 + cobF + t, sx0 + b - cobL - t, sy1 - cobT - t, "ARM-BARRA-DETALHE");
        }
        if (ext?.TemDente == true) DenteNaSecao(c, v, ext.Dente, xCorte < L / 2, b, sx0, sy0, sy1);
        if (aba != null) AbaNaSecaoArmadura(c, v, aba, b, sx0, sy0);
        if (comRess) RessaltoNaSecaoArmadura(c, v, ress, b, hSec, sx0, sy0);
        // barras em ponto e uma indicação por linha (inferiores embaixo, superiores em cima, pele à direita)
        int kBaixo = 0, kCima = 0, kDir = 0;
        foreach (var l in longs.Where(l => xCorte >= l.Ini - 1e-6 && xCorte <= l.Fim + 1e-6))
        {
            var pts = PontosNaSecao(l, b, hSec, cobF, cobT, cobL, comEstribo ? phiT : 0, longs).Select(p => (sx0 + p.X, sy0 + p.Y)).ToList();
            foreach (var (px, py) in pts) c.R.Circulos.Add(new CirculoTemplate { X = px, Y = py, Raio = l.Bitola / 20, Layer = "ARM-BARRA-SECAO" });
            (double X, double Y) pos = l.Pele ? (sx0 + b + 4 * hc, sy0 + hSec / 2 - 2 * hc * kDir++)
                : l.Inferior ? (sx0 + b / 2 + 2 * hc * kBaixo, sy0 - (3 + 2 * kBaixo++) * hc)
                : (sx0 + b / 2 + 2 * hc * kCima, sy1 + (3 + 2 * kCima++) * hc);
            c.R.Faixas.Add(new FaixaTemplate
            {
                Barra = "long" + l.Linha, Tipo = TipoFaixa.Indicacao, Referencias = l.Pele ? pts.Where(p => p.Item1 > sx0 + b / 2).ToList() : pts,
                Posicao = pos, Escala = c.escC,
            });
        }
    }

    /// <summary>Forma do estribo (fechado com ganchos, ou U aberto com a saliência) para a relação e a descrição.</summary>
    private static BarraTemplate FormaEstribo(ValoresTemplate v, ConfiguracaoArmadura cfg, double b, double h, double x, double y)
    {
        double cobL = v.Numero("CobLateral"), cobF = v.Numero("CobFundo"), cobT = v.Numero("CobTopo");
        double w = b - 2 * cobL, hh = h - cobF - cobT, phi = v.Numero("EstBitola");
        string aco = v["EstAco"];
        var bt = new BarraTemplate { Nome = "estribo", Bitola = phi, Aco = aco, Estribo = true, Escala = 0, Quantidade = 1 };
        if (v["EstTipo"] == EstAberto)
        {
            double sal = Math.Max(0, v.Numero("EstSaliencia"));
            bt.Vertices.AddRange(new[] { (x, y + hh + sal + cobT), (x, y), (x + w, y), (x + w, y + hh + sal + cobT) });
            bt.Formatos.AddRange(new[] { FormatoSegmento.Linha, FormatoSegmento.Linha, FormatoSegmento.Linha });
        }
        else
        {
            var f = GeradorEstribo.FechadoPadrao(new List<(double X, double Y)> { (x, y), (x + w, y), (x + w, y + hh), (x, y + hh) }, phi, aco, cfg.Dobramento);
            bt.Vertices.AddRange(f.Vertices.Select(p => (p.X, p.Y)));
            bt.Formatos.AddRange(f.Formatos);
            foreach (var s in f.Sobrepostos) bt.Sobrepostos.Add(s);
            foreach (var s in f.Cotados) bt.Cotados.Add(s);
        }
        return bt;
    }
}
