using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Templates.PilarPreMoldado;

namespace DEB.Core.Templates.VigaPreMoldada;

/// <summary>Extremidade da viga (ponta E = x 0, ponta D = x L).</summary>
public sealed class ExtremidadeViga
{
    /// <summary>Dente do catálogo; null = extremidade reta.</summary>
    public TipoDente Dente { get; set; }
    public int Furos { get; set; }
    /// <summary>Distância da ponta ao centro dos furos (cm).</summary>
    public double DistFuro { get; set; }
    public bool TemDente => Dente != null;
    /// <summary>Altura da viga na ponta (cm).</summary>
    public double AlturaPonta(double h) => TemDente ? Dente.Altura : h;
    public double Recorte(double h) => TemDente ? h - Dente.Altura : 0;
    public double CompDente => TemDente ? Dente.Comprimento : 0;
}

/// <summary>
/// Viga pré-moldada (VP-1/VP-2) no padrão do escritório (pranchas 316-6208/6219, GRU 702; requisitos em
/// docs/VIGA_PREMOLDADA_REQUISITOS.md). Etapa 1 (2026-11-07): forma — FÔRMAS EM PLANTA e FÔRMAS EM ELEVAÇÃO com dentes
/// Gerber (catálogo), furos de pino e alças, cotas, seções de forma (A-A, B-B…), notas e informações para produção.
/// Verdadeira grandeza (cm), como o pilar: a escala só dá a altura dos textos e cotas. x ao longo da viga (ponta E em 0),
/// y para cima; na planta, y = largura b.
/// </summary>
public sealed partial class TemplateVigaPreMoldada : ITemplateArmadura
{
    public string Id => "viga-premoldada";
    public string Nome => "Viga pré-moldada (VP-1/VP-2)";
    public CategoriaElemento Categoria => CategoriaElemento.VigaPreMoldada;
    public string Descricao => "Viga retangular pré-moldada no padrão do escritório: fôrmas em planta e em elevação, dentes Gerber do catálogo, " +
                               "furos de pino, alças, armaduras em elevação (longitudinais fora da forma, estribos por zona), seções de forma e " +
                               "de armadura, notas, relação do aço e informações para produção.";

    public const string Reta = "Reta";

    /// <summary>Catálogo de dentes (o plugin põe aqui o da configuração do usuário).</summary>
    public static List<TipoDente> CatalogoDentes { get; set; } = TipoDente.Padrao();

    private static ParametroTemplate P(string grupo, string nome, string rotulo, string padrao, string unidade = "",
        TipoParametro tipo = TipoParametro.Numero, double min = double.NegativeInfinity, double max = double.PositiveInfinity,
        string descricao = "", params string[] opcoes) => new ParametroTemplate
    {
        Grupo = grupo, Nome = nome, Rotulo = rotulo, Padrao = padrao, Unidade = unidade, Tipo = tipo, Minimo = min, Maximo = max,
        Descricao = descricao, Opcoes = opcoes.ToList(),
    };

    private const string G1 = "01. Identificação", G2 = "02. Geometria", G3 = "03. Extremidades", G4 = "04. Cobrimentos",
        G5 = "05. Içamento (alças)", G6 = "06. Concreto e produção";

    /// <summary>Calculado a cada leitura: as opções das extremidades vêm do catálogo de dentes.</summary>
    public IReadOnlyList<ParametroTemplate> Parametros
    {
        get
        {
            var ext = new[] { Reta }.Concat(CatalogoDentes.Select(t => t.Nome)).ToArray();
            string padrao = CatalogoDentes.Count > 0 ? CatalogoDentes[0].Nome : Reta;
            const string dExt = "Reta ou um dente Gerber do catálogo (altura do dente × comprimento).";
            return new[]
            {
                P(G1, "Nome", "Nome da viga", "V143", tipo: TipoParametro.Texto),
                P(G1, "Quantidade", "Quantidade de peças", "1", tipo: TipoParametro.Inteiro, min: 1),
                P(G1, "Idioma", "Idioma dos textos", "PT/EN", tipo: TipoParametro.Opcao, opcoes: new[] { "PT", "EN", "PT/EN" }),
                P(G1, "EscalaVistas", "Escala das vistas (1:n)", "50", min: 1),
                P(G1, "EscalaCortes", "Escala das seções (1:n)", "25", min: 1),

                P(G2, "B", "Seção: largura b", "20", "cm", min: 10),
                P(G2, "H", "Seção: altura h", "75", "cm", min: 10),
                P(G2, "L", "Comprimento total", "466", "cm", min: 50),

                P(G3, "ExtE", "Ponta esquerda", padrao, tipo: TipoParametro.Opcao, descricao: dExt, opcoes: ext),
                P(G3, "ExtD", "Ponta direita", padrao, tipo: TipoParametro.Opcao, descricao: dExt, opcoes: ext),
                P(G3, "FurosModo", "Quantidade de furos de pino", FurosPelaLargura, tipo: TipoParametro.Opcao, opcoes: new[] { FurosPelaLargura, FurosDigitados },
                  descricao: "Padrão do escritório (2026-11-11): viga com até 25 cm de largura leva 1 furo; com 30 cm ou mais, 2 furos. " +
                             "\"Digitada\" usa os valores das pontas abaixo (0 = sem furo)."),
                P(G3, "FurosE", "Furos de pino na ponta esquerda (digitada)", "1", tipo: TipoParametro.Inteiro, min: 0, max: 4,
                  descricao: "Furos verticais para o pino do consolo, distribuídos por igual na largura."),
                P(G3, "FurosD", "Furos de pino na ponta direita (digitada)", "1", tipo: TipoParametro.Inteiro, min: 0, max: 4),
                P(G3, "FuroDiam", "Furos: diâmetro", "32", "mm", min: 10),
                P(G3, "FuroDistE", "Furos: distância à ponta esquerda", "11.5", "cm", min: 1),
                P(G3, "FuroDistD", "Furos: distância à ponta direita", "11.5", "cm", min: 1),

                P(G4, "CobFundo", "Fundo", "2.5", "cm", min: 1, max: 15),
                P(G4, "CobTopo", "Topo", "2.5", "cm", min: 1, max: 15),
                P(G4, "CobLateral", "Laterais", "2.5", "cm", min: 1, max: 15),
                P(G4, "CobPontas", "Pontas", "2.5", "cm", min: 1, max: 15),

                P(G5, "Alcas", "Alças de içamento", "Sim", tipo: TipoParametro.SimNao),
                P(G5, "AlcaPct", "Distância às pontas", "20", "% do comprimento", min: 5, max: 45,
                  descricao: "As duas alças ficam simétricas em relação ao CG da viga, afastadas (50 − x) % do comprimento dele (regra do pilar)."),
                P(G5, "AlcaX1", "Alça da esquerda (0 = automática)", "0", "cm", min: 0, descricao: "Distância da ponta esquerda; 0 = pela regra do CG."),
                P(G5, "AlcaX2", "Alça da direita (0 = automática)", "0", "cm", min: 0, descricao: "Distância da ponta esquerda; 0 = pela regra do CG."),
                P(G5, "AlcaEmb", "Embutimento (0 = até o fundo)", "0", "cm", min: 0,
                  descricao: "0 = as pernas descem até o fundo da viga (h − cobrimento do fundo), com o pé lá (regra do usuário para o pilar, 2026-11-08)."),
                P(G5, "AlcaBitola", "Cordoalha", "12.7", "mm", TipoParametro.Bitola, 9.5, 15.2),
                P(G5, "AlcaPernas", "Cordoalhas por alça", "2", tipo: TipoParametro.Inteiro, min: 1, max: 4),
                P(G5, "AlcaPe", "Pé (dobra no fim de cada perna)", "20", "cm", min: 0),
                P(G5, "AlcaAcima", "Altura acima do topo", "15", "cm", min: 5),
                P(G5, "AlcaEntre", "Distância entre as pernas", "10", "cm", min: 4),

                P(G6, "Fck", "fck (28 dias)", "40", "MPa", min: 10),
                P(G6, "FcjSaque", "fcj no saque", "30", "MPa", min: 5),
                P(G6, "FcjMontagem", "fcj na montagem", "35", "MPa", min: 0),
                P(G6, "PesoEspecifico", "Peso específico do concreto", "2500", "kgf/m³", min: 1000),
                P(G6, "Neoprene", "Neoprene (espessura; 0 = sem nota)", "15", "mm", min: 0),
                P(G6, "Contraflecha", "Contraflecha estimada (0 = sem nota)", "0", "cm", min: 0),
                P(G6, "ModuloE", "Módulo de elasticidade adotado (0 = sem nota)", "0", "GPa", min: 0,
                  descricao: "NBR 9062, 5.6.1.2: o desenho informa o E adotado."),
                P(G6, "Estocagem", "Estocagem: apoios a partir das pontas (0 = nas alças)", "0", "cm", min: 0),
            }.Concat(ParametrosCantoneira()).Concat(ParametrosArmadura()).Concat(ParametrosProtensao()).Concat(ParametrosAba()).Concat(ParametrosRessalto()).Concat(ParametrosAberturas()).ToArray();
        }
    }

    // ======================== cálculos (testáveis) ========================

    /// <summary>Volume (m³): seção cheia × comprimento menos os recortes dos dentes (os furos não descontam, como na prancha V144).</summary>
    public static double Volume(double b, double h, double L, ExtremidadeViga e, ExtremidadeViga d) =>
        (b * h * L - b * e.Recorte(h) * e.CompDente - b * d.Recorte(h) * d.CompDente) / 1e6;

    /// <summary>Posição do CG ao longo da viga (cm da ponta esquerda), com os recortes dos dentes.</summary>
    public static double CentroDeGravidade(double b, double h, double L, ExtremidadeViga e, ExtremidadeViga d)
    {
        double a0 = h * L, a1 = e.Recorte(h) * e.CompDente, a2 = d.Recorte(h) * d.CompDente;
        double m = a0 * L / 2 - a1 * e.CompDente / 2 - a2 * (L - d.CompDente / 2);
        return m / (a0 - a1 - a2);
    }

    public const string FurosPelaLargura = "Pela largura (até 25 cm: 1; 30 cm ou mais: 2)", FurosDigitados = "Digitada";

    /// <summary>Furos de pino pela largura da viga (padrão do escritório, 2026-11-11): b &lt; 30 cm → 1; b ≥ 30 cm → 2.</summary>
    public static int FurosPorLargura(double b) => b < 30 - 1e-6 ? 1 : 2;

    /// <summary>Lê a ponta ("ExtE"/"ExtD"): reta ou o dente do catálogo (nome desconhecido = reta, com aviso).</summary>
    public static ExtremidadeViga LerExtremidade(ValoresTemplate v, bool esquerda, IReadOnlyList<TipoDente> catalogo, List<string> avisos)
    {
        string k = esquerda ? "E" : "D", lado = esquerda ? "esquerda" : "direita";
        string nome = v["Ext" + k];
        int furos = v["FurosModo"] == FurosDigitados ? Math.Max(0, v.Inteiro("Furos" + k)) : FurosPorLargura(v.Numero("B"));
        var e = new ExtremidadeViga { Furos = furos, DistFuro = v.Numero("FuroDist" + k) };
        if (!string.IsNullOrWhiteSpace(nome) && !nome.Equals(Reta, StringComparison.OrdinalIgnoreCase))
        {
            e.Dente = TipoDente.Obter(catalogo, nome);
            if (e.Dente == null) avisos.Add($"Ponta {lado}: dente \"{nome}\" não está no catálogo; desenhada reta.");
        }
        return e;
    }

    private static string Num(double v, string f) => KitDesenho.Num(v, f);

    // ======================== desenho ========================

    public ResultadoTemplate Gerar(ValoresTemplate v, ConfiguracaoArmadura cfg)
    {
        var r = new ResultadoTemplate();
        double escV = v.Numero("EscalaVistas") > 0 ? v.Numero("EscalaVistas") : 50;
        double escC = v.Numero("EscalaCortes") > 0 ? v.Numero("EscalaCortes") : 25;
        double mm = cfg.AlturaTextoMm > 0 ? cfg.AlturaTextoMm : 2;
        var c = new Ctx { R = r, Idioma = KitDesenho.LerIdioma(v["Idioma"]), escV = escV, escC = escC, hV = mm / 10 * escV, hC = mm / 10 * escC };
        r.Escala = escV;
        string nome = string.IsNullOrWhiteSpace(v["Nome"]) ? "VIGA" : v["Nome"].Trim();
        r.Detalhamento = nome;
        double hv = c.hV, hc = c.hC;

        double b = v.Numero("B"), h = v.Numero("H"), L = v.Numero("L");
        var e = LerExtremidade(v, true, CatalogoDentes, r.Avisos);
        var d = LerExtremidade(v, false, CatalogoDentes, r.Avisos);
        ValidarExtremidades(r, b, h, L, e, d, v.Numero("FuroDiam") / 10);
        ValidarCantoneiras(v, b, h, e, d, r.Avisos);
        var aba = LerAba(v, h, L, e, d, r.Avisos);   // VP-4: aba de apoio de laje (L / T invertido)
        var ress = LerRessalto(v, b, h, r.Avisos);   // VP-4: ressalto sobre a alma (ligação com a capa)
        var abs = LerAberturas(v, h, L, e, d, r.Avisos);   // aberturas na alma (2026-11-11)
        int qtd = Math.Max(1, v.Inteiro("Quantidade"));

        // ---------------- título ----------------
        double y = 0;
        double capa = Capa(v);
        string titulo = nome + (qtd > 1 ? $" - {qtd}x" : "") + $" ({Num(b, "0.#")} x {Num(h, "0.#")}{(capa > 0 ? "+" + Num(capa, "0.#") : "")})";
        c.Texto(titulo, 0, y, 2.5 * hv);
        c.Texto($"ESC 1:{escV:0}", 0, y - 1.5 * hv, 0.7 * hv);
        // dente criado pela importação do Revit com a armadura padrão: aviso em vermelho acima do título até o usuário conferir
        {
            double ya = y + 3.5 * hv;
            foreach (string t in MarcaConferir.Linhas(new[] { e.Dente, d.Dente }.Where(x => x != null && MarcaConferir.Pendente(x.Descricao)).Select(x => x.Nome), c.Linhas).Reverse())
            { c.Texto(t, 0, ya, 0.9 * hv, layer: MarcaConferir.Layer); ya += 1.6 * hv; }
        }

        // ---------------- fôrmas em planta ----------------
        y -= 7 * hv;
        TituloVista(c, "FÔRMAS EM PLANTA", "FORMWORK PLAN", 0, y, escV);
        double yP = y - 10 * hv - b - aba.LargE; // fundo da planta (face de y = 0); folga para a 2ª linha da cadeia dos furos
        DesenharPlanta(c, v, b, L, e, d, yP, aba);
        RessaltoEmPlanta(c, ress, b, L, yP);

        // ---------------- fôrmas em elevação ----------------
        y = yP - 13 * hv - aba.LargD - (AbaParcial(aba, L, e, d) ? 3 * hv : 0);
        TituloVista(c, "FÔRMAS EM ELEVAÇÃO", "FORMWORK ELEVATION", 0, y, escV);
        bool alcas = v.SimNao("Alcas");
        double acima = alcas ? v.Numero("AlcaAcima") : 0;
        double yE = y - (alcas ? 12 : 8) * hv - acima - h - Math.Max(ress.Altura, capa); // folga para a cadeia e o rótulo das alças (e o ressalto/capa)
        // a seção do vão passa pela aba (se a aba não pega o meio da viga, o corte vai para o meio dela)
        var cortes = CortesForma(e, d, L, aba.Existe && !aba.Em(L / 2) ? (aba.Ini + aba.Fim) / 2 : (double?)null);
        // uma seção por abertura, com as letras seguintes
        var corteAbertura = new Dictionary<string, AberturaViga>();
        foreach (var a in abs)
        {
            string id = ((char)('A' + cortes.Count)).ToString();
            cortes.Add((id, a.X));
            corteAbertura[id] = a;
        }
        DesenharElevacao(c, v, b, h, L, e, d, yE, cortes, alcas, Math.Max(ress.Altura, capa));
        if (capa > 0)
        {
            // 2º estágio em tracejado acima do topo, com a altura cotada fora da ponta direita
            c.R.Contornos.Add(new ContornoTemplate { Vertices = { (0.0, yE + h), (0.0, yE + h + capa), (L, yE + h + capa), (L, yE + h) }, Layer = "ARM-FURO-OCULTO" });
            c.Cota(L, yE + h, L, yE + h + capa, L + 5.5 * hv, yE + h + capa / 2, Math.PI / 2, escV);
        }
        AberturasEmElevacao(c, abs, L, yE);
        AbaEmElevacao(c, aba, L, yE, cotar: true);
        RessaltoEmElevacao(c, ress, L, yE + h, cotar: true);

        // ---------------- armaduras em elevação (etapa 2) ----------------
        var longs = LerLongitudinais(v, L, v.Numero("CobPontas"), e, d, r.Avisos);
        // ---------------- protensão (VP-3): superiores acima das passivas, as outras abaixo ----------------
        bool prot = v.SimNao("Protendida");
        var cords = prot ? LerCordoalhas(v, r.Avisos) : new List<GrupoCordoalhas>();
        // fretagem: estribos das pontas até o fim da transferência das cordoalhas aderentes (NBR 6118 9.4.5.2)
        var fret = Fretagem(cords, h, L, e, d, v.Numero("FcjSaque"), v.SimNao("LiberacaoGradual"));
        var zonas = ZonasComAberturas(v, cfg, ZonasEstribos(v, cfg, L, e, d, fret.E, fret.D), abs);
        double yArm = ArmadurasEmElevacao(c, v, cfg, b, h, L, e, d, longs, zonas, cortes, yE - 16 * hv, cords.Where(g => Superior(g, h)).ToList(), aba, ress, abs);

        VerificarCordoalhas(cords, b, h, v.Numero("CobLateral"), v.Numero("CobFundo"), v.Numero("CobTopo"), v.Numero("EstBitola"),
            prot ? v.Numero("FcjSaque") : 99, e, d, r.Avisos);
        VerificarPassivasComCordoalhas(cords, longs, b, h, v.Numero("CobFundo"), v.Numero("CobTopo"), v.Numero("CobLateral"), v.Numero("EstBitola"), r.Avisos);
        CordoalhasEmElevacao(c, cords.Where(g => !Superior(g, h)).ToList(), h, L, e, d, yArm - 2 * hv);

        // ---------------- seções de forma e de armadura (coluna à direita das vistas) ----------------
        double wAba = aba.LargE + aba.LargD;
        double xS = L + 25 * hv, yS = 0, xSA = xS + b + wAba + 25 * hc;
        double ySVao = 0;
        foreach (var (id, xc) in cortes)
        {
            var ext = xc < L / 2 && e.TemDente && xc < e.CompDente ? e : xc > L / 2 && d.TemDente && xc > L - d.CompDente ? d : null;
            // as seções ficam alinhadas pela alma (deslocadas da aba esquerda); no dente, só a aba que fica acima do recorte
            var abaSec = !aba.Em(xc) ? null : ext?.TemDente == true ? aba.NoDente(h - ext.Dente.Altura) : aba;
            corteAbertura.TryGetValue(id, out var abCorte);
            SecaoArmadura(c, v, id, xc, b, h, L, ext, longs, zonas, xSA + aba.LargE, yS, abaSec, ress, abCorte);
            if (cords.Count > 0) CordoalhasNaSecao(c, cords, xc, h, L, e, d, b, xSA + aba.LargE + 4 * hc, SecaoBase(c, yS, ext, h) - ress.Altura);
            if (ext == null) ySVao = yS;
            yS = SecaoForma(c, id, b, h, ext, xS + aba.LargE, yS, abaSec, ress, abCorte, ext?.TemDente == true ? 0 : capa) - 8 * hc;
        }
        // forma do estribo ao lado da seção do vão (descrição embaixo)
        double xEst = xSA + b + wAba + 22 * hc;
        var est = FormaEstribo(v, cfg, b, h, xEst, ySVao - 7 * hc - (h - v.Numero("CobFundo") - v.Numero("CobTopo")));
        est.Escala = escC;
        var ex = est.Vertices;
        est.PosicaoDescricao = ((ex.Min(q => q.X) + ex.Max(q => q.X)) / 2, ex.Min(q => q.Y) - 3 * hc);
        if (zonas.Count > 0) r.Barras.Add(est);
        // formas dos estribos do dente (Gerber e suspensão de cada ponta), embaixo, uma depois da outra
        double yForma = ex.Min(q => q.Y) - 9 * hc;
        foreach (var (x, esq) in new[] { (e, true), (d, false) })
            foreach (var f in FormasDente(v, cfg, b, h, x, esq))
            {
                double fy0 = f.Vertices.Min(q => q.Y), fy1 = f.Vertices.Max(q => q.Y), fx0 = f.Vertices.Min(q => q.X);
                for (int i = 0; i < f.Vertices.Count; i++) f.Vertices[i] = (f.Vertices[i].X - fx0 + xEst, f.Vertices[i].Y - fy1 + yForma);
                f.Escala = escC;
                f.PosicaoDescricao = ((f.Vertices.Min(q => q.X) + f.Vertices.Max(q => q.X)) / 2, yForma - (fy1 - fy0) - 3 * hc);
                r.Barras.Add(f);
                yForma -= (fy1 - fy0) + 9 * hc;
            }

        // forma do estribo da aba, embaixo das outras
        int nEstAba = PosicoesEstribosAba(v, cfg, aba).Count;
        if (nEstAba > 0 && FormaEstriboAba(v, cfg, aba, b, xEst, 0) is { } fa)
        {
            double fy1 = fa.Vertices.Max(q => q.Y), fy0 = fa.Vertices.Min(q => q.Y);
            for (int i = 0; i < fa.Vertices.Count; i++) fa.Vertices[i] = (fa.Vertices[i].X, fa.Vertices[i].Y - fy1 + yForma);
            fa.Escala = escC;
            fa.PosicaoDescricao = ((fa.Vertices.Min(q => q.X) + fa.Vertices.Max(q => q.X)) / 2, yForma - (fy1 - fy0) - 3 * hc);
            r.Barras.Add(fa);
        }

        // formas do estribo do ressalto e da barra em L da ligação com a capa, embaixo das outras
        foreach (var fr in FormasRessalto(v, cfg, ress, b, h))
        {
            double fy1 = fr.Vertices.Max(q => q.Y), fy0 = fr.Vertices.Min(q => q.Y), fx0 = fr.Vertices.Min(q => q.X);
            yForma -= 9 * hc;
            for (int i = 0; i < fr.Vertices.Count; i++) fr.Vertices[i] = (fr.Vertices[i].X - fx0 + xEst, fr.Vertices[i].Y - fy1 + yForma);
            fr.Escala = escC;
            fr.PosicaoDescricao = ((fr.Vertices.Min(q => q.X) + fr.Vertices.Max(q => q.X)) / 2, yForma - (fy1 - fy0) - 3 * hc);
            r.Barras.Add(fr);
            yForma -= fy1 - fy0;
        }

        // formas dos estribos curtos das aberturas, embaixo das outras
        foreach (var fr in FormasAberturas(v, cfg, abs, b, h))
        {
            double fy1 = fr.Vertices.Max(q => q.Y), fy0 = fr.Vertices.Min(q => q.Y), fx0 = fr.Vertices.Min(q => q.X);
            yForma -= 9 * hc;
            for (int i = 0; i < fr.Vertices.Count; i++) fr.Vertices[i] = (fr.Vertices[i].X - fx0 + xEst, fr.Vertices[i].Y - fy1 + yForma);
            fr.Escala = escC;
            fr.PosicaoDescricao = ((fr.Vertices.Min(q => q.X) + fr.Vertices.Max(q => q.X)) / 2, yForma - (fy1 - fy0) - 3 * hc);
            r.Barras.Add(fr);
            yForma -= fy1 - fy0;
        }

        // ---------------- detalhe da alça (embaixo das seções) e estabilidade lateral ----------------
        double yItens = alcas ? DetalheAlca(c, v, xS, yS - 4 * hc) : yS - 4 * hc;
        NomenclaturaCordoalhas(c, cords, b, h, xS, yItens);
        double lAlcas = alcas ? Math.Abs(PosicaoAlcas(v, b, L, e, d)[1] - PosicaoAlcas(v, b, L, e, d)[0]) : L;
        var (esb, ind) = EstabilidadeLateral(lAlcas, b, h);
        if (esb > 50 || ind > 500)
            r.Avisos.Add($"Estabilidade lateral (NBR 9062 6.1.6, L = {Num(lAlcas, "0")} cm entre as alças): L/b = {Num(esb, "0.#")} (≤ 50) e " +
                         $"L·h/b² = {Num(ind, "0")} (≤ 500) — verificar a carga crítica, travar ou mudar as alças (6.1.4, 6.1.7).");

        // ---------------- notas e informações para produção ----------------
        double xD = xEst + b + wAba + 30 * hc, yD = 0;
        yD = Notas(c, v, L, xD, yD, fret.Lbpt);
        double vol = Volume(b, h, L, e, d) + aba.VolumeCm3 / 1e6 + ress.VolumeCm3(L) / 1e6, peso = vol * v.Numero("PesoEspecifico") / 1000;
        r.Tabelas.Add(KitDesenho.TabelaProducao(c.Idioma, v.Numero("FcjSaque"), v.Numero("FcjMontagem"), v.Numero("Fck"),
            vol, peso, qtd, xD, yD, escV, protendida: prot,
            taxaPassiva: prot ? PesoPassivo(r, cfg, zonas, nEstAba * (GrampoNaAba(v, aba) ? aba.Lados : 1)) / vol : 0, taxaAtiva: prot ? PesoAtivo(r, cfg) / vol : 0));
        r.PosicaoRA = (xD + 75 * hv, 0);

        // ---------------- detalhes típicos da biblioteca do escritório (DWG; coluna à direita, o usuário ajusta) ----------------
        BibliotecaDetalhes.Pedir(r, BibliotecaDetalhes.RaioEmendasComprimento);
        if ((v.SimNao("InfSoldada") && longs.Any(l => l.Inferior)) || e.TemDente || d.TemDente) BibliotecaDetalhes.Pedir(r, BibliotecaDetalhes.SoldaTirante);
        BibliotecaDetalhes.PosicionarColuna(r, hv);
        if (r.ColunaDetalhes is { } col) r.ColunaDetalhes = (Math.Max(col.X, r.PosicaoRA.Value.X + 110 * hv), col.Y);
        return r;
    }

    private static void ValidarExtremidades(ResultadoTemplate r, double b, double h, double L, ExtremidadeViga e, ExtremidadeViga d, double diamFuro)
    {
        foreach (var (x, lado) in new[] { (e, "esquerda"), (d, "direita") })
        {
            if (x.TemDente && x.Dente.Altura >= h - 1e-6)
                r.Avisos.Add($"Ponta {lado}: dente com altura {Num(x.Dente.Altura, "0.#")} cm ≥ h da viga ({Num(h, "0.#")} cm): sem recorte.");
            if (x.TemDente && x.Dente.Altura < h / 2 - 1e-6)
                r.Avisos.Add($"Ponta {lado}: dente com altura {Num(x.Dente.Altura, "0.#")} cm < h/2 — conferir (NBR 9062, 7.4).");
            if (x.Furos > 0 && x.DistFuro + diamFuro / 2 > (x.TemDente ? x.CompDente : L / 2) + 1e-6)
                r.Avisos.Add($"Ponta {lado}: o furo de pino (a {Num(x.DistFuro, "0.#")} cm da ponta) sai do {(x.TemDente ? "dente" : "meio da viga")}.");
        }
        if (e.CompDente + d.CompDente > L / 2) r.Avisos.Add("Os dentes ocupam mais da metade do comprimento da viga: conferir.");
    }

    private static void TituloVista(Ctx c, string pt, string en, double x, double y, double esc)
    {
        foreach (var t in c.Linhas(pt, en)) { c.Texto(t, x, y, 1.5 * c.hV); y -= 2 * c.hV; }
        c.Texto($"ESC 1:{esc:0}", x, y + 0.6 * c.hV, 0.7 * c.hV);
    }

    /// <summary>x dos furos (centro) e y relativos na largura (distribuídos por igual).</summary>
    private static IEnumerable<double> PosicoesNaLargura(int n, double b) => Enumerable.Range(1, n).Select(k => k * b / (n + 1));

    /// <summary>Cadeia de cotas horizontais pelos pontos (em ordem), na altura <paramref name="yc"/>.</summary>
    private static void CadeiaH(Ctx c, IEnumerable<double> xs, double yRef, double yc, double esc)
    {
        foreach (var (a, z, desl) in Trechos(c, xs, yc - yRef))
            c.Cota(a, yRef, z, yRef, (a + z) / 2, yc + desl, 0, esc);
    }

    private static void CadeiaV(Ctx c, IEnumerable<double> ys, double xRef, double xc, double esc)
    {
        foreach (var (a, z, desl) in Trechos(c, ys, xc - xRef))
            c.Cota(xRef, a, xRef, z, xc + desl, (a + z) / 2, Math.PI / 2, esc);
    }

    /// <summary>
    /// Trechos da cadeia; dois trechos curtos seguidos (menos de 3 alturas de texto: em 1:50, 11,5 | 13,5 e 10 | 10) não cabem
    /// na mesma linha — o segundo vai uma linha mais para fora (<paramref name="lado"/> = sentido da cota em relação à peça).
    /// </summary>
    private static IEnumerable<(double A, double Z, double Desl)> Trechos(Ctx c, IEnumerable<double> pontos, double lado)
    {
        var p = pontos.Distinct().OrderBy(q => q).ToList();
        double s = lado < 0 ? -1 : 1;
        bool antCurto = false, antDesl = false;
        for (int i = 0; i + 1 < p.Count; i++)
        {
            if (p[i + 1] - p[i] <= 1e-6) continue;
            bool curto = p[i + 1] - p[i] < 3 * c.hV, desl = curto && antCurto && !antDesl;
            yield return (p[i], p[i + 1], desl ? s * 2.2 * c.hV : 0);
            antCurto = curto; antDesl = desl;
        }
    }

    /// <summary>Pontos da cadeia "dente | vão | dente" (só a ponta e o total quando não há dente).</summary>
    private static List<double> PontosComprimento(double L, ExtremidadeViga e, ExtremidadeViga d)
    {
        var p = new List<double> { 0, L };
        if (e.TemDente) p.Add(e.CompDente);
        if (d.TemDente) p.Add(L - d.CompDente);
        return p;
    }

    /// <summary>A aba não vai de dente a dente (ou de ponta a ponta): o trecho dela ganha uma cadeia própria na planta.</summary>
    private static bool AbaParcial(AbaViga a, double L, ExtremidadeViga e, ExtremidadeViga d) =>
        a.Existe && (Math.Abs(a.Ini - (e.TemDente ? e.CompDente : 0)) > 1e-6 || Math.Abs(a.Fim - (L - (d.TemDente ? d.CompDente : 0))) > 1e-6);

    private static void DesenharPlanta(Ctx c, ValoresTemplate v, double b, double L, ExtremidadeViga e, ExtremidadeViga d, double yP, AbaViga a)
    {
        double hv = c.hV, esc = c.escV, rf = v.Numero("FuroDiam") / 20;
        c.Retangulo(0, yP, L, yP + b);
        AbaEmPlanta(c, a, b, L, yP);
        // face dos recortes dos dentes (embaixo): oculta na planta
        if (e.TemDente) c.Linha(e.CompDente, yP, e.CompDente, yP + b, "ARM-FURO-OCULTO");
        if (d.TemDente) c.Linha(L - d.CompDente, yP, L - d.CompDente, yP + b, "ARM-FURO-OCULTO");
        // alças: o laço visto de cima é um traço ao longo da viga
        if (v.SimNao("Alcas"))
            foreach (double xa in PosicaoAlcas(v, b, L, e, d))
                c.Linha(xa - v.Numero("AlcaEntre") / 2, yP + b / 2, xa + v.Numero("AlcaEntre") / 2, yP + b / 2, "ARM-ALCA");
        // furos de pino
        var pontosFuros = new List<double>();
        foreach (var (x, esq) in new[] { (e, true), (d, false) })
        {
            if (x.Furos <= 0) continue;
            double xf = esq ? x.DistFuro : L - x.DistFuro;
            var ys = PosicoesNaLargura(x.Furos, b).ToList();
            foreach (var yy in ys) c.R.Circulos.Add(new CirculoTemplate { X = xf, Y = yP + yy, Raio = rf, Layer = "ARM-FURO" });
            // "1 Furo / ø32 mm" fora da ponta e a cota na largura
            double xt = esq ? -6 * hv : L + 6 * hv;
            int al = esq ? 2 : 0;
            string rot = c.Idioma == IdiomaDesenho.EN ? $"{x.Furos} Hole{(x.Furos > 1 ? "s" : "")}" : $"{x.Furos} Furo{(x.Furos > 1 ? "s" : "")}";
            c.Texto(rot, xt, yP + b / 2 + 0.4 * hv, 0.9 * hv, al);
            c.Texto($"ø{Num(v.Numero("FuroDiam"), "0.#")} mm", xt, yP + b / 2 - 1.2 * hv, 0.9 * hv, al);
            double xr = esq ? 0 : L, xc = esq ? -2.5 * hv : L + 2.5 * hv;
            CadeiaV(c, new[] { yP, yP + b }.Concat(ys.Select(q => yP + q)), xr, xc, esc);
            pontosFuros.AddRange(esq ? new[] { 0, xf, x.TemDente ? x.CompDente : xf } : new[] { L, xf, x.TemDente ? L - x.CompDente : xf });
        }
        // cotas: furos em cima; dente | vão | dente e o total embaixo
        if (pontosFuros.Count > 0)
        {
            // só os trechos das pontas (o vão fica na cadeia de baixo)
            var p = pontosFuros.Distinct().OrderBy(q => q).ToList();
            var pe = p.Where(q => q <= Math.Max(e.CompDente, e.DistFuro) + 1e-6).ToList();
            var pd = p.Where(q => q >= L - Math.Max(d.CompDente, d.DistFuro) - 1e-6).ToList();
            if (pe.Count > 1) CadeiaH(c, pe, yP + b, yP + b + a.LargE + 2.5 * hv, esc);
            if (pd.Count > 1) CadeiaH(c, pd, yP + b, yP + b + a.LargE + 2.5 * hv, esc);
        }
        if (e.TemDente || d.TemDente) CadeiaH(c, PontosComprimento(L, e, d), yP, yP - a.LargD - 3 * hv, esc);
        c.Cota(0, yP, L, yP, L / 2, yP - a.LargD - 6 * hv, 0, esc);
        if (AbaParcial(a, L, e, d)) CadeiaH(c, new[] { 0, a.Ini, a.Fim, L }, yP, yP - a.LargD - 9 * hv, esc);
        CantoneirasEmPlanta(c, v, b, L, yP);
    }

    /// <summary>Posições das duas alças (cm da ponta esquerda): simétricas ao CG, como no pilar; valores digitados mandam.</summary>
    public static double[] PosicaoAlcas(ValoresTemplate v, double b, double L, ExtremidadeViga e, ExtremidadeViga d)
    {
        double cg = CentroDeGravidade(b, v.Numero("H"), L, e, d, LerAba(v, v.Numero("H"), L, e, d, null));
        var (x1, x2) = KitDesenho.PosicaoAlcas(L, cg, v.Numero("AlcaPct"), v.Numero("AlcaX1"), v.Numero("AlcaX2"));
        return new[] { x1, x2 };
    }

    /// <summary>Seções de forma: o dente de cada ponta (no meio do dente) e o vão; letras da esquerda para a direita.</summary>
    public static List<(string Id, double X)> CortesForma(ExtremidadeViga e, ExtremidadeViga d, double L, double? xVao = null)
    {
        var xs = new List<double>();
        if (e.TemDente) xs.Add(e.CompDente / 2);
        xs.Add(xVao ?? L / 2);
        if (d.TemDente) xs.Add(L - d.CompDente / 2);
        return xs.Select((x, i) => (((char)('A' + i)).ToString(), x)).ToList();
    }

    private static void DesenharElevacao(Ctx c, ValoresTemplate v, double b, double h, double L, ExtremidadeViga e, ExtremidadeViga d,
        double yE, List<(string Id, double X)> cortes, bool alcas, double acimaDoTopo = 0)
    {
        // acimaDoTopo: o ressalto sobe acima do topo da alma — marcas de corte e a cadeia das alças vão para cima dele
        double hv = c.hV, esc = c.escV, yT = yE + h;
        // contorno com os recortes dos dentes embaixo
        var k = new ContornoTemplate { Fechado = true, Layer = "ARM-FORMA" };
        if (e.TemDente) k.Vertices.AddRange(new[] { (0.0, yT - e.Dente.Altura), (e.CompDente, yT - e.Dente.Altura), (e.CompDente, yE) });
        else k.Vertices.Add((0, yE));
        if (d.TemDente) k.Vertices.AddRange(new[] { (L - d.CompDente, yE), (L - d.CompDente, yT - d.Dente.Altura), (L, yT - d.Dente.Altura) });
        else k.Vertices.Add((L, yE));
        k.Vertices.AddRange(new[] { (L, yT), (0.0, yT) });
        c.R.Contornos.Add(k);

        // furos de pino: verticais, ocultos, na altura da ponta
        double rf = v.Numero("FuroDiam") / 20;
        foreach (var (x, esq) in new[] { (e, true), (d, false) })
        {
            if (x.Furos <= 0) continue;
            double xf = esq ? x.DistFuro : L - x.DistFuro;
            bool noDente = x.TemDente && x.DistFuro < x.CompDente;
            double yb = noDente ? yT - x.Dente.Altura : yE;
            c.Linha(xf - rf, yb, xf - rf, yT, "ARM-FURO-OCULTO");
            c.Linha(xf + rf, yb, xf + rf, yT, "ARM-FURO-OCULTO");
        }

        CantoneirasEmElevacao(c, v, L, yT);

        // alças: laço acima do topo; cadeia ponta → alça → alça → ponta em cima
        double yCadeiaTopo = yT + acimaDoTopo + 2.5 * hv;
        if (alcas)
        {
            double emb = EmbutimentoAlcaViga(v), acima = v.Numero("AlcaAcima"), r0 = v.Numero("AlcaEntre") / 2;
            var xa = PosicaoAlcas(v, b, L, e, d);
            foreach (double x in xa)
                c.R.Contornos.Add(new ContornoTemplate
                {
                    Vertices = { (x - r0, yT - Math.Min(emb, h - 1)), (x - r0, yT + acima - r0), (x + r0, yT + acima - r0), (x + r0, yT - Math.Min(emb, h - 1)) },
                    Bulges = { 0, -1, 0, 0 }, Layer = "ARM-ALCA",
                });
            yCadeiaTopo = yT + Math.Max(acima, acimaDoTopo) + 2.5 * hv;
            CadeiaH(c, new[] { 0, xa[0], xa[1], L }, yT, yCadeiaTopo, esc);
            // rótulo acima do texto da cota: PT em cima, "(EN)" embaixo
            var linhasAlca = c.Linhas("ALÇAS", "LIFTING HANDLES");
            double ytx = yCadeiaTopo + 1.8 * hv + (linhasAlca.Length - 1) * 1.2 * hv;
            foreach (var t in linhasAlca) { c.Texto(t, (xa[0] + xa[1]) / 2, ytx, 0.8 * hv, 1); ytx -= 1.2 * hv; }
        }

        // alturas nas pontas (recorte + dente, ou h)
        foreach (var (x, esq) in new[] { (e, true), (d, false) })
        {
            double xr = esq ? 0 : L, xc = esq ? -2.5 * hv : L + 2.5 * hv;
            var ys = new List<double> { yE, yT };
            if (x.TemDente) ys.Add(yT - x.Dente.Altura);
            CadeiaV(c, ys, xr, xc, esc);
        }

        // comprimentos embaixo: dente | vão | dente e o total
        // (abaixo das marcas dos cortes)
        if (e.TemDente || d.TemDente) CadeiaH(c, PontosComprimento(L, e, d), yE, yE - 5.5 * hv, esc);
        c.Cota(0, yE, L, yE, L / 2, yE - 8.5 * hv, 0, esc);

        // marcas das seções: em cima (apontando para baixo) e embaixo (para cima)
        foreach (var (id, xc) in cortes)
        {
            c.MarcaCorte(id, xc, yT + acimaDoTopo + 0.2 * hv, hv);
            MarcaCorteBaixo(c, id, xc, yE - 0.2 * hv, hv);
        }
    }

    /// <summary>Marca de corte embaixo da vista: triângulo vazado apontando para cima e a letra embaixo.</summary>
    private static void MarcaCorteBaixo(Ctx c, string id, double x, double y, double h)
    {
        double w = 1.05 * h, base0 = y - 1.0 * h, ponta = y - 0.47 * h;
        c.R.Contornos.Add(new ContornoTemplate { Vertices = { (x - w, base0), (x + w, base0), (x, ponta) }, Fechado = true, Layer = "ARM-CORTE" });
        c.Texto(id, x, base0 - 1.35 * h, h, 1, 0, "ARM-CORTE");
    }

    /// <summary>
    /// Seção de forma (escala das seções) com o topo em <paramref name="yTopo"/>; devolve o y abaixo dela. No dente, a parte
    /// cortada é o dente (b × altura do dente) e o recorte aparece atrás, em vista.
    /// </summary>
    private static double SecaoForma(Ctx c, string id, double b, double h, ExtremidadeViga ext, double x0, double yTopo, AbaViga a = null, RessaltoViga rs = null,
        AberturaViga ab = null, double capa = 0)
    {
        double hc = c.hC, esc = c.escC, y = yTopo;
        foreach (var t in c.Linhas($"SEÇÃO {id}-{id}", $"SECTION {id}-{id}")) { c.Texto(t, x0, y, 1.2 * hc); y -= 2 * hc; }
        c.Texto($"ESC 1:{esc:0}", x0, y, 0.8 * hc);
        bool comRess = rs != null && rs.Existe;
        double sx0 = x0 + 4 * hc, sy1 = y - 5 * hc - (comRess ? rs.Altura + 3 * hc : 0), sy0 = sy1 - h;
        double hCorte = ext?.TemDente == true ? ext.Dente.Altura : h;
        if (a != null || comRess) c.R.Contornos.Add(new ContornoTemplate { Vertices = ContornoSecao(b, hCorte, a, comRess ? rs : null, sx0, sy1 - hCorte), Fechado = true, Layer = "ARM-PILAR-SECAO" });
        else c.Retangulo(sx0, sy1 - hCorte, sx0 + b, sy1, "ARM-PILAR-SECAO");
        if (hCorte < h - 1e-6) c.Retangulo(sx0, sy0, sx0 + b, sy1 - hCorte); // recorte do dente, em vista
        c.Cota(sx0, sy0, sx0 + b, sy0, sx0 + b / 2, sy0 - 3 * hc, 0, esc);
        // com aba só à direita, a cota da altura da aba fica junto dela e a da viga vai uma linha para fora
        double xh = sx0 + b + (a?.LargD ?? 0) + (a != null && !a.Esquerda ? (a.Inclinada ? 9 : 6) : 3) * hc;
        c.Cota(sx0 + b, sy1 - hCorte, sx0 + b, sy1, xh, sy1 - hCorte / 2, Math.PI / 2, esc);
        if (a != null) CotasAbaNaSecao(c, a, b, sx0, sy1 - hCorte, sy0 - 3 * hc);
        if (comRess) CotasRessaltoNaSecao(c, rs, b, hCorte, sx0, sy1 - hCorte);
        if (ab != null) AberturaNaSecaoForma(c, ab, b, sx0, sy0);
        if (capa > 0)
        {
            // 2º estágio: a capa em tracejado acima da peça e a altura dela cotada do lado esquerdo
            c.R.Contornos.Add(new ContornoTemplate { Vertices = { (sx0, sy1), (sx0, sy1 + capa), (sx0 + b, sy1 + capa), (sx0 + b, sy1) }, Layer = "ARM-FURO-OCULTO" });
            c.Cota(sx0, sy1, sx0, sy1 + capa, sx0 - 3 * hc, sy1 + capa / 2, Math.PI / 2, esc);
        }
        return sy0 - 6 * hc;
    }

    /// <summary>Peso (kg) da armadura passiva de uma peça pelas regras da RA (estribos: um por posição das zonas; sem as alças).</summary>
    public static double PesoPassivo(ResultadoTemplate r, ConfiguracaoArmadura cfg, List<ZonaEstriboViga> zonas, int estribosAba = 1) =>
        r.Barras.Where(b => !Tabelas.TabelaBarras.EhCordoalha(b.Aco))
                .Sum(b => MedidaBarra.Peso(b, cfg) * (b.Nome == "estribo" ? Math.Max(1, zonas.Sum(z => z.Posicoes.Count))
                                                  : b.Nome == "abaestribo" ? Math.Max(1, estribosAba) : 1));

    /// <summary>Peso (kg) das cordoalhas de protensão de uma peça (sem as alças, que também são CP).</summary>
    public static double PesoAtivo(ResultadoTemplate r, ConfiguracaoArmadura cfg) =>
        r.Barras.Where(b => b.Nome.StartsWith("cord")).Sum(b => MedidaBarra.Peso(b, cfg));

    private static double Notas(Ctx c, ValoresTemplate v, double L, double x, double y, double lbpt = 0)
    {
        double hn = 0.9 * c.hV;
        string N(double q) => Num(q, "0.0#");
        int tol = TemplatePilarPreMoldado.ToleranciaComprimento(L);
        double neo = v.Numero("Neoprene"), cf = v.Numero("Contraflecha"), E = v.Numero("ModuloE"), est = v.Numero("Estocagem");
        var pt = new List<string>
        {
            "NOTAS:",
            "1) DIMENSÕES EM cm;",
            $"2) COBRIMENTOS: FUNDO = {N(v.Numero("CobFundo"))}; TOPO = {N(v.Numero("CobTopo"))}; LATERAIS = {N(v.Numero("CobLateral"))}; PONTAS = {N(v.Numero("CobPontas"))} cm;",
            $"3) TOLERÂNCIAS DE FABRICAÇÃO (NBR 9062, TAB. 2): COMPRIMENTO ±{tol} mm; SEÇÃO -5/+10 mm;",
            "    DISTORÇÃO ±5 mm; LINEARIDADE ±L/1000; POSIÇÃO DOS INSERTOS ±15 mm;",
        };
        var en = new List<string>
        {
            "NOTES:",
            "1) DIMENSIONS IN cm;",
            FormattableString.Invariant($"2) CONCRETE COVER: BOTTOM = {v.Numero("CobFundo"):0.0#}; TOP = {v.Numero("CobTopo"):0.0#}; SIDES = {v.Numero("CobLateral"):0.0#}; ENDS = {v.Numero("CobPontas"):0.0#} cm;"),
            $"3) MANUFACTURING TOLERANCES (NBR 9062, TABLE 2): LENGTH ±{tol} mm; SECTION -5/+10 mm;",
            "    DISTORTION ±5 mm; STRAIGHTNESS ±L/1000; POSITION OF INSERTS ±15 mm;",
        };
        int n = 4;
        if (neo > 0) { pt.Add($"{n}) UTILIZAR NEOPRENE {Num(neo, "0.#")} mm;"); en.Add(FormattableString.Invariant($"{n++}) USE {neo:0.#} mm NEOPRENE;")); }
        if (cf > 0) { pt.Add($"{n}) CONTRAFLECHA ESTIMADA: {Num(cf, "0.0#")} cm;"); en.Add(FormattableString.Invariant($"{n++}) ESTIMATED CAMBER: {cf:0.0#} cm;")); }
        if (E > 0) { pt.Add($"{n}) MÓDULO DE ELASTICIDADE ADOTADO: E = {Num(E, "0.#")} GPa;"); en.Add(FormattableString.Invariant($"{n++}) ADOPTED MODULUS OF ELASTICITY: E = {E:0.#} GPa;")); }
        if (est > 0)
        {
            pt.Add($"{n}) ESTOCAGEM E TRANSPORTE: APOIAR A {Num(est, "0.#")} cm DAS PONTAS;");
            en.Add(FormattableString.Invariant($"{n++}) STORAGE AND TRANSPORT: SUPPORT AT {est:0.#} cm FROM THE ENDS;"));
        }
        else
        {
            pt.Add($"{n}) ESTOCAGEM E TRANSPORTE: APOIAR NA POSIÇÃO DAS ALÇAS;");
            en.Add($"{n++}) STORAGE AND TRANSPORT: SUPPORT AT THE LIFTING HANDLES;");
        }
        if (lbpt > 0)
        {
            pt.Add($"{n}) FRETAGEM: ESTRIBOS DAS PONTAS ATÉ O FIM DA TRANSFERÊNCIA DA PROTENSÃO (lbpt = {Num(lbpt, "0")} cm, NBR 6118 9.4.5.2);");
            en.Add(FormattableString.Invariant($"{n++}) BURSTING REINFORCEMENT: END STIRRUPS ALONG THE PRESTRESS TRANSFER LENGTH (lbpt = {lbpt:0} cm, NBR 6118 9.4.5.2);"));
        }
        if (c.Idioma != IdiomaDesenho.EN) y = c.Bloco(pt, x, y, hn) - hn;
        if (c.Idioma != IdiomaDesenho.PT) y = c.Bloco(en, x, y, hn) - hn;
        return y;
    }
}
