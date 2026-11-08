using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Templates;
using DEB.Core.Templates.Blocos;

namespace DEB.Core.Motor.Entrada;

/// <summary>
/// Valores do template que são padrão do escritório (pernas, cobrimentos, escala, idioma…): não vão para o
/// <see cref="ElementoBloco"/>; viram sobreposições do <see cref="PerfilDetalhamento"/> (chaves de <see cref="PerfilDetalhamento.Padroes"/>
/// e <see cref="PerfilDetalhamento.Regras"/>) e opções da folha.
/// </summary>
public sealed class SobreposicoesPerfil
{
    /// <summary>"cobrimento.solo", "cobrimento.calice", "secundaria.perna", "superior.perna", "principal.perna", "arranque.pe",
    /// "arranque.espera", "calice.grampos" (1/0), "fundo_calice.comprimento".</summary>
    public Dictionary<string, double> Padroes { get; } = new Dictionary<string, double>();
    /// <summary>"principal.perna" ("valor" | "altura_base"), "arranque.pe_sentido" ("fora" | "dentro").</summary>
    public Dictionary<string, string> Regras { get; } = new Dictionary<string, string>();
    public double Escala { get; set; } = 20;
    /// <summary>"PT", "EN" ou "PT/EN".</summary>
    public string Idioma { get; set; } = "PT";
    /// <summary>Desenhar a forma (vistas <see cref="TipoVista.FormaPlanta"/>/<see cref="TipoVista.FormaCorte"/>).</summary>
    public bool Forma { get; set; } = true;
    /// <summary>Desenhar os detalhes padrão.</summary>
    public bool Detalhes { get; set; } = true;

    /// <summary>Aplica no perfil (que é alterado): padrões, regras, escala e idioma; sem forma tira as vistas de forma; sem
    /// detalhes esvazia a lista de detalhes.</summary>
    public void AplicarEm(PerfilDetalhamento perfil)
    {
        if (perfil == null) throw new ArgumentNullException(nameof(perfil));
        foreach (var kv in Padroes) perfil.Padroes[kv.Key] = kv.Value;
        foreach (var kv in Regras) perfil.Regras[kv.Key] = kv.Value;
        perfil.Folha ??= new FolhaPerfil();
        perfil.Folha.Escala = Escala;
        perfil.Folha.Idioma = Idioma;
        if (!Forma) perfil.Folha.Vistas.RemoveAll(x => x.Tipo == TipoVista.FormaPlanta || x.Tipo == TipoVista.FormaCorte);
        if (!Detalhes) perfil.Folha.Detalhes.Clear();
    }
}

/// <summary>
/// Converte os valores da janela do <see cref="TemplateBlocoEstacas"/> (ARMTEMPLATE, arquivo de modelos) em
/// <see cref="ElementoBloco"/> + <see cref="SobreposicoesPerfil"/>, e de volta. A ida e volta preserva todos os parâmetros.
/// </summary>
public static class ConversorBloco
{
    /// <summary>Parâmetros do template de cada função (null = o template não tem; fica 0).</summary>
    public sealed class MapaArmadura
    {
        public string Funcao { get; }
        public string Bitola { get; }
        public string Quantidade { get; }
        public string Espacamento { get; }
        public MapaArmadura(string funcao, string bitola, string qtd, string esp) { Funcao = funcao; Bitola = bitola; Quantidade = qtd; Espacamento = esp; }
    }

    public static readonly IReadOnlyList<MapaArmadura> Mapa = new[]
    {
        new MapaArmadura(Funcoes.Principal, "PrincBitola", "PrincQtd", "PrincEsp"),
        new MapaArmadura(Funcoes.Secundaria, "SecBitola", null, "SecEsp"),
        new MapaArmadura(Funcoes.Superior, "SupBitola", null, "SupEsp"),
        new MapaArmadura(Funcoes.Lateral, "LatBitola", null, "LatEsp"),
        new MapaArmadura(Funcoes.EstriboVertical, "EstVBitola", null, "EstVEsp"),
        new MapaArmadura(Funcoes.CaliceTopo, "CalTopoBitola", "CalTopoQtd", "CalTopoEsp"),
        new MapaArmadura(Funcoes.Calice, "CalBitola", null, "CalEsp"),
        new MapaArmadura(Funcoes.CaliceVertical, "CalVertBitola", "CalVertQtd", null),
        new MapaArmadura(Funcoes.Suspensao, "SuspBitola", "SuspQtd", "SuspEsp"),
        new MapaArmadura(Funcoes.FundoCalice, "FundoBitola", "FundoQtd", null),
        new MapaArmadura(Funcoes.Arranque, "ArrBitola", null, null),
        new MapaArmadura(Funcoes.ArranqueEstribo, "ArrEstBitola", "ArrEstQtd", null),
    };

    /// <summary>Todas as funções do bloco, na ordem da planilha.</summary>
    public static IEnumerable<string> FuncoesBloco => Mapa.Select(m => m.Funcao);

    // chaves do perfil
    public const string CobSolo = "cobrimento.solo", CobCalice = "cobrimento.calice", SecPerna = "secundaria.perna",
        SupPerna = "superior.perna", PrincPerna = "principal.perna", ArrPe = "arranque.pe", ArrEspera = "arranque.espera",
        ArrPeSentido = "arranque.pe_sentido", CalGrampos = "calice.grampos", FundoComp = "fundo_calice.comprimento";
    public const string RegraValor = "valor", RegraAlturaBase = "altura_base", SentidoFora = "fora", SentidoDentro = "dentro";

    /// <summary>Elemento a partir dos valores do template (os padrões do escritório são descartados).</summary>
    public static ElementoBloco DeValores(ValoresTemplate v) => DeValores(v, out _);

    /// <summary>Elemento + sobreposições do perfil a partir dos valores do template.</summary>
    public static ElementoBloco DeValores(ValoresTemplate v, out SobreposicoesPerfil perfil)
    {
        if (v == null) throw new ArgumentNullException(nameof(v));
        var e = new ElementoBloco
        {
            Nome = v["Nome"].Trim(),
            Pilares = TemplateBlocoEstacas.LerPilares(v["Pilares"]),
            Repeticoes = Math.Max(0, v.Inteiro("Quantidade")),
            Niveis = v.Tabela("Niveis").Where(l => l.Length >= 1 && l[0].Length > 0)
                .Select(l => new NivelPilar { Pilar = l[0], CotaM = ValoresTemplate.NumeroCelula(l, 1) }).ToList(),
            Arranjo = LerArranjo(v["Arranjo"]) ?? ArranjoEstacas.Grade,
            EstacasX = v.Inteiro("NEstX"), EstacasY = v.Inteiro("NEstY"),
            EspX = v.Numero("EspX"), EspY = v.Numero("EspY"),
            DiamEstaca = v.Numero("DiamEstaca"), DistFace = v.Numero("DistFace"), EmbutimentoEstaca = v.Numero("EmbEstaca"),
            AlturaBase = v.Numero("hBase"), Lastro = v.Numero("Lastro"),
            PilarBx = v.Numero("PilarBx"), PilarBy = v.Numero("PilarBy"), PilarEx = v.Numero("PilarEx"), PilarEy = v.Numero("PilarEy"),
            Ligacao = LerLigacao(v["Ligacao"]) ?? LigacaoPilar.CaliceColarinho,
            AlturaColarinho = v.Numero("hColar"), ParedeCalice = v.Numero("tParede"), FolgaCalice = v.Numero("Folga"),
            Lemb = v.Numero("Lemb"), Regularizacao = v.Numero("Regularizacao"),
            Interface = LerInterface(v["Interface"]) ?? InterfaceCalice.Chaves,
            ArranqueNx = v.Inteiro("ArrNx"), ArranqueNy = v.Inteiro("ArrNy"), ArranqueCobrimento = v.Numero("ArrCob"),
            Fck = v.Numero("Fck"),
            Aco = string.IsNullOrWhiteSpace(v["Aco"]) ? "CA-50" : v["Aco"].Trim(),
        };
        foreach (var m in Mapa)
            e.Armaduras.Add(new ArmaduraCalculada
            {
                Funcao = m.Funcao,
                Bitola = v.Numero(m.Bitola),
                Quantidade = m.Quantidade == null ? 0 : v.Inteiro(m.Quantidade),
                Espacamento = m.Espacamento == null ? 0 : v.Numero(m.Espacamento),
            });

        perfil = new SobreposicoesPerfil();
        var p = perfil.Padroes;
        p[CobSolo] = v.Numero("Cob");
        p[CobCalice] = v.Numero("CobCalice");
        p[SecPerna] = v.Numero("SecPerna");
        p[SupPerna] = v.Numero("SupPerna");
        double pp = v.Numero("PrincPerna");
        p[PrincPerna] = pp;
        perfil.Regras[PrincPerna] = pp > 0 ? RegraValor : RegraAlturaBase;
        p[ArrPe] = v.Numero("ArrPe");
        p[ArrEspera] = v.Numero("ArrEspera");
        perfil.Regras[ArrPeSentido] = v["ArrPeSentido"] == TemplateBlocoEstacas.PeDentro ? SentidoDentro : SentidoFora;
        p[CalGrampos] = v.SimNao("CalGrampos") ? 1 : 0;
        p[FundoComp] = v.Numero("FundoComp");
        perfil.Escala = v.Numero("Escala");
        perfil.Idioma = string.IsNullOrWhiteSpace(v["Idioma"]) ? "PT" : v["Idioma"].Trim();
        perfil.Forma = v.SimNao("Forma");
        perfil.Detalhes = v.SimNao("Detalhes");
        return e;
    }

    /// <summary>Grava o elemento nos valores (só os campos do elemento; os padrões do escritório ficam como estão).
    /// Função sem armadura na lista = bitola 0.</summary>
    public static void ParaValores(ElementoBloco e, ValoresTemplate v)
    {
        if (e == null) throw new ArgumentNullException(nameof(e));
        if (v == null) throw new ArgumentNullException(nameof(v));
        v["Nome"] = e.Nome ?? "";
        v["Pilares"] = TemplateBlocoEstacas.TextoPilares(e.Pilares ?? new List<string>());
        v["Quantidade"] = N(e.Repeticoes);
        v["Niveis"] = ValoresTemplate.JuntarTabela((e.Niveis ?? new List<NivelPilar>()).Select(n => new[] { n.Pilar, N(n.CotaM) }));
        v["Arranjo"] = e.Arranjo == ArranjoEstacas.Triangulo ? TemplateBlocoEstacas.Triangulo : e.Arranjo == ArranjoEstacas.Cinco ? TemplateBlocoEstacas.Cinco : TemplateBlocoEstacas.Grade;
        v["NEstX"] = N(e.EstacasX); v["NEstY"] = N(e.EstacasY);
        v["EspX"] = N(e.EspX); v["EspY"] = N(e.EspY);
        v["DiamEstaca"] = N(e.DiamEstaca); v["DistFace"] = N(e.DistFace); v["EmbEstaca"] = N(e.EmbutimentoEstaca);
        v["hBase"] = N(e.AlturaBase); v["Lastro"] = N(e.Lastro);
        v["PilarBx"] = N(e.PilarBx); v["PilarBy"] = N(e.PilarBy); v["PilarEx"] = N(e.PilarEx); v["PilarEy"] = N(e.PilarEy);
        v["Ligacao"] = e.Ligacao switch
        {
            LigacaoPilar.CaliceEmbutido => TemplateBlocoEstacas.Embutido,
            LigacaoPilar.Arranque => TemplateBlocoEstacas.Arranque,
            LigacaoPilar.Nenhuma => TemplateBlocoEstacas.SemLigacao,
            _ => TemplateBlocoEstacas.Colarinho,
        };
        v["hColar"] = N(e.AlturaColarinho); v["tParede"] = N(e.ParedeCalice); v["Folga"] = N(e.FolgaCalice);
        v["Lemb"] = N(e.Lemb); v["Regularizacao"] = N(e.Regularizacao);
        v["Interface"] = e.Interface switch
        {
            InterfaceCalice.Lisa => TemplateBlocoEstacas.Lisa,
            InterfaceCalice.Rugosa => TemplateBlocoEstacas.Rugosa,
            _ => TemplateBlocoEstacas.Chaves,
        };
        v["ArrNx"] = N(e.ArranqueNx); v["ArrNy"] = N(e.ArranqueNy); v["ArrCob"] = N(e.ArranqueCobrimento);
        v["Fck"] = N(e.Fck);
        v["Aco"] = string.IsNullOrWhiteSpace(e.Aco) ? "CA-50" : e.Aco;
        foreach (var m in Mapa)
        {
            var a = e.Armadura(m.Funcao);
            v[m.Bitola] = N(a?.Bitola ?? 0);
            if (a == null) continue;
            if (m.Quantidade != null) v[m.Quantidade] = N(a.Quantidade);
            if (m.Espacamento != null) v[m.Espacamento] = N(a.Espacamento);
        }
    }

    /// <summary>Grava o elemento e as sobreposições do perfil (chaves ausentes deixam o valor como está).</summary>
    public static void ParaValores(ElementoBloco e, SobreposicoesPerfil perfil, ValoresTemplate v)
    {
        ParaValores(e, v);
        if (perfil == null) return;
        void Num(string chave, string param) { if (perfil.Padroes.TryGetValue(chave, out var x)) v[param] = N(x); }
        Num(CobSolo, "Cob"); Num(CobCalice, "CobCalice"); Num(SecPerna, "SecPerna"); Num(SupPerna, "SupPerna");
        Num(ArrPe, "ArrPe"); Num(ArrEspera, "ArrEspera"); Num(FundoComp, "FundoComp");
        if (perfil.Regras.TryGetValue(PrincPerna, out var rp) && rp == RegraAlturaBase) v["PrincPerna"] = "0";
        else Num(PrincPerna, "PrincPerna");
        if (perfil.Padroes.TryGetValue(CalGrampos, out var g)) v["CalGrampos"] = g != 0 ? "Sim" : "Não";
        if (perfil.Regras.TryGetValue(ArrPeSentido, out var s))
            v["ArrPeSentido"] = s == SentidoDentro ? TemplateBlocoEstacas.PeDentro : TemplateBlocoEstacas.PeFora;
        v["Escala"] = N(perfil.Escala);
        v["Idioma"] = perfil.Idioma ?? "PT";
        v["Forma"] = perfil.Forma ? "Sim" : "Não";
        v["Detalhes"] = perfil.Detalhes ? "Sim" : "Não";
    }

    /// <summary>Valores completos (padrões do template ← elemento ← sobreposições).</summary>
    public static ValoresTemplate Valores(ElementoBloco e, SobreposicoesPerfil perfil = null)
    {
        var v = new ValoresTemplate(new TemplateBlocoEstacas().Parametros);
        ParaValores(e, perfil, v);
        return v;
    }

    // ======================== opções (aceitam o texto do template e as palavras curtas da planilha) ========================

    public static ArranjoEstacas? LerArranjo(string s)
    {
        string k = Chave(s);
        if (k.StartsWith("grade") || k.StartsWith("retangular")) return ArranjoEstacas.Grade;   // "Retangular (grade nx × ny)"
        if (k.StartsWith("triang")) return ArranjoEstacas.Triangulo;
        if (k.StartsWith("5") || k.StartsWith("cinco")) return ArranjoEstacas.Cinco;                            // "Triangular (3 estacas)"
        return null;
    }

    public static LigacaoPilar? LerLigacao(string s)
    {
        string k = Chave(s);
        if (k == Chave(TemplateBlocoEstacas.Colarinho) || k.Contains("colarinho")) return LigacaoPilar.CaliceColarinho;
        if (k == Chave(TemplateBlocoEstacas.Embutido) || k.Contains("embutido")) return LigacaoPilar.CaliceEmbutido;
        if (k == Chave(TemplateBlocoEstacas.Arranque) || k.StartsWith("arranque")) return LigacaoPilar.Arranque;
        if (k == Chave(TemplateBlocoEstacas.SemLigacao) || k.StartsWith("sem") || k.StartsWith("nenhum")) return LigacaoPilar.Nenhuma;
        return null;
    }

    public static InterfaceCalice? LerInterface(string s)
    {
        string k = Chave(s);
        if (k.StartsWith("lisa")) return InterfaceCalice.Lisa;
        if (k.StartsWith("rugosa")) return InterfaceCalice.Rugosa;
        if (k.Contains("chave")) return InterfaceCalice.Chaves;   // "chaves" ou "Com chaves de cisalhamento"
        return null;
    }

    /// <summary>Minúsculas, sem acentos e sem espaços nas pontas.</summary>
    internal static string Chave(string s)
    {
        var d = (s ?? "").Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (char ch in d) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    private static string N(double x) => x.ToString("R", CultureInfo.InvariantCulture);
}
