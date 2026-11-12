using System;
using System.Collections.Generic;
using System.IO;
using DEB.Core.Persistencia;

namespace DEB.Core.Lajes;

/// <summary>Como os painéis são distribuídos na largura do pano.</summary>
public enum EstrategiaPaginacao
{
    /// <summary>Junta no meio de cada abertura (dois painéis recortados); o resto com painéis inteiros e o ajuste junto à borda.</summary>
    AberturasPrimeiro,
    /// <summary>Como "aberturas primeiro", mas evita ajuste estreito: um ajuste largo ou dois iguais (simétricos quando der).</summary>
    MelhorDistribuicao,
    /// <summary>Começa na borda escolhida com painéis inteiros; o ajuste fica no fim; aberturas viram entalhes.</summary>
    DeUmLado,
}

/// <summary>Tirantes dos suportes de instalações (barra roscada na chave + chapas em cima e embaixo). Metros.</summary>
public sealed class ConfigTirante
{
    public double Diametro = 0.0125;
    public double ChapaAoLongoJunta = 0.30;
    public double ChapaTransversal = 0.30;
    public double ChapaEspessura = 0.006;
    public double MalhaLongitudinal = 1.25;
    public double MalhaTransversal = 1.25;
    public double MargemPontaJunta = 0.30;
}

/// <summary>
/// Cotas em que a seção das vigas é lida, RELATIVAS À BASE DO PAINEL (metros; negativo = abaixo). A alma é lida bem abaixo do
/// painel; a aba logo abaixo da base do painel (onde ele apoia); a faixa "painel" mostra se a viga sobe ao lado do painel.
/// </summary>
public sealed class ConfigLeituraViga
{
    public double DzAlma = -0.20;
    public double DzAba = -0.01;
    public double DzPainelMin = 0.005;
    public double DzPainelMax = 0.23;
    /// <summary>Passo das linhas de leitura na faixa do painel.</summary>
    public double PassoPainel = 0.02;
}

/// <summary>O que fazer quando a barra do tirante não cabe na parte estreita (inferior) da junta.</summary>
public enum TratamentoBarraJunta
{
    /// <summary>Furo na borda dos dois painéis vizinhos feito em obra (decisão do usuário, 12/11/2026; padrão). A geometria
    /// dos painéis não é recortada: a informação vai no tirante e o relatório lista os furos por painel.</summary>
    FuroEmObra,
    /// <summary>Insere o tirante assim mesmo e só avisa.</summary>
    InserirEAvisar,
    /// <summary>Não insere o tirante dessa junta e avisa.</summary>
    NaoInserir,
}

/// <summary>Fase 3 (opcional, liga/desliga): capa, chaves de cisalhamento e detalhe do tirante dos suportes de instalações.</summary>
public sealed class ConfigFase3
{
    /// <summary>Troca o tipo dos pisos paginados para um tipo de capa (camada única com a espessura da capa, mesmo topo), etapa da capa.</summary>
    public bool Capa = true;
    /// <summary>Chaves de cisalhamento (forma medida entre os painéis do modelo), etapa das chaves.</summary>
    public bool Chaves = true;
    /// <summary>Passo vertical da medição do perfil da junta nos sólidos dos painéis.</summary>
    public double PassoPerfilChave = 0.004;
    /// <summary>Sobra da barra do tirante acima da chapa superior e abaixo da chapa inferior.</summary>
    public double SobraBarraCima = 0.025, SobraBarraBaixo = 0.05;
    /// <summary>Porca simplificada (diâmetro e altura).</summary>
    public double PorcaDiametro = 0.022, PorcaAltura = 0.011;
    public TratamentoBarraJunta BarraNaoCabe = TratamentoBarraJunta.FuroEmObra;
    /// <summary>Folga do furo em obra sobre o diâmetro da barra (furo = barra + folga; padrão 3 mm).</summary>
    public double FolgaFuro = 0.003;
    /// <summary>Material dos tirantes/chapas (vazio = procurar um aço no projeto).</summary>
    public string MaterialAco = "";

    /// <summary>Diâmetro do furo em obra para a barra.</summary>
    public double DiametroFuro(double diametroBarra) => diametroBarra + FolgaFuro;

    /// <summary>Barra cabe na menor abertura da junta? (inserir, furo em obra necessário, texto para o tirante/aviso — null se cabe).</summary>
    public (bool Inserir, bool Furo, string Texto) AvaliarBarra(double aberturaMinima, double diametro)
    {
        if (double.IsNaN(aberturaMinima) || aberturaMinima >= diametro) return (true, false, null);
        string folga = $"barra Ø{Fmt.Virgula(diametro * 1000, "0.0")} mm não cabe na parte estreita da junta ({Fmt.Virgula(aberturaMinima * 1000, "0.0")} mm)";
        switch (BarraNaoCabe)
        {
            case TratamentoBarraJunta.NaoInserir: return (false, false, folga + ": tirante não inserido");
            case TratamentoBarraJunta.InserirEAvisar: return (true, false, folga + ": inserido assim mesmo, conferir");
            default: return (true, true, TextoFuro(diametro));
        }
    }

    /// <summary>Texto gravado no tirante quando há furo em obra.</summary>
    public string TextoFuro(double diametroBarra) => $"Furo Ø{Fmt.Virgula(DiametroFuro(diametroBarra) * 1000, "0.0")} em obra na borda dos painéis";

    /// <summary>Furos em obra por peça: 2 por tirante com furo, um em cada peça vizinha da junta.</summary>
    public static SortedDictionary<string, int> FurosPorPeca(IEnumerable<(string PecaA, string PecaB)> tirantesComFuro)
    {
        var r = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (a, b) in tirantesComFuro)
            foreach (var m in new[] { a, b })
                if (!string.IsNullOrEmpty(m)) r[m] = (r.TryGetValue(m, out int n) ? n : 0) + 1;
        return r;
    }
}

/// <summary>Valores do parâmetro de etapa de concretagem.</summary>
public sealed class ConfigEtapas
{
    public int Paineis = 1;
    public int ChavesEFaixasMacicas = 2;
    public int Capa = 3;
}

/// <summary>Escolhas da inserção no modelo (sem nomes fixos: o usuário escolhe na janela; ficam gravadas na pasta do usuário).</summary>
public sealed class ConfigInsercao
{
    /// <summary>Família de quadro estrutural dos painéis e o tipo (vazio = escolher na janela).</summary>
    public string FamiliaPainel = "";
    public string TipoPainel = "";
    /// <summary>Largura do ajuste: true = cria um tipo por largura (parâmetro <see cref="ParametroLargura"/>); false = recorte lateral.</summary>
    public bool CriarTipoPorLargura = false;
    public string ParametroLargura = "b";
    /// <summary>Parâmetros de folga da família zerados na inserção (a linha do painel já é a peça real).</summary>
    public List<string> ParametrosFolga = new List<string> { "folga_esq", "folga_dir" };
    /// <summary>Nome do parâmetro (de instância) que recebe a etapa de concretagem.</summary>
    public string ParametroEtapa = "Etapa de concretagem";
    public string PrefixoPainel = "L";
    public string PrefixoMacica = "M";
    public string PrefixoJunta = "J";
}

/// <summary>
/// Configuração da paginação de lajes alveolares (medidas em metros). Gravada em JSON na pasta de dados do usuário do plugin;
/// chaves desconhecidas do arquivo são preservadas ao gravar. "Restaurar padrão" = <c>new ConfigPaginacao()</c>.
/// </summary>
public sealed class ConfigPaginacao
{
    public EstrategiaPaginacao Estrategia = EstrategiaPaginacao.AberturasPrimeiro;
    /// <summary>Só na estratégia "de um lado": true = começa em v mínimo; false = em v máximo.</summary>
    public bool BordaInicioVMin = true;
    public double Modulo = 1.25;
    public double LarguraPainel = 1.245;
    public double Junta = 0.005;
    public double AjusteMin = 0.30;
    public double AjusteBom = 0.60;
    public double FolgaPonta = 0.015;
    public double FolgaPilar = 0.015;
    public double RecorteMin = 0.01;
    public double ApoioMin = 0.08;
    public double ConsoloAlemDoRecorte = 0.30;
    public double FaixaMacicaMin = 0.10;
    /// <summary>Distância máxima da faixa maciça (ou da peça cortada) à face da viga para pedir apoio em consolo.</summary>
    public double DistanciaConsolo = 0.06;
    public double AlturaPainel = 0.16;
    public double Capa = 0.075;
    public bool SuportesInstalacoes = true;
    /// <summary>Arquivo de catálogo de painéis do fornecedor (fora do repositório; vazio = nenhum).</summary>
    public string ArquivoCatalogo = "";
    public ConfigTirante Tirante = new ConfigTirante();
    public ConfigLeituraViga LeituraViga = new ConfigLeituraViga();
    public ConfigEtapas Etapas = new ConfigEtapas();
    public ConfigInsercao Insercao = new ConfigInsercao();
    public ConfigFase3 Fase3 = new ConfigFase3();

    private ValorJson _original;   // campos desconhecidos do arquivo lido (preservados ao gravar)

    public static string NomeEstrategia(EstrategiaPaginacao e) => e switch
    {
        EstrategiaPaginacao.MelhorDistribuicao => "melhor_distribuicao",
        EstrategiaPaginacao.DeUmLado => "de_um_lado",
        _ => "aberturas_primeiro",
    };

    public static EstrategiaPaginacao EstrategiaDeNome(string s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "melhor_distribuicao" => EstrategiaPaginacao.MelhorDistribuicao,
        "de_um_lado" => EstrategiaPaginacao.DeUmLado,
        _ => EstrategiaPaginacao.AberturasPrimeiro,
    };

    /// <summary>Problemas que impedem a paginação (vazio = ok). Mensagens em português, prontas para a janela.</summary>
    public List<string> Validar()
    {
        var e = new List<string>();
        void Faixa(string nome, double v, double min, double max, string un = "m")
        {
            if (double.IsNaN(v) || v < min - 1e-9 || v > max + 1e-9) e.Add($"{nome}: {Fmt.N(v, "0.####")} fora da faixa {Fmt.N(min, "0.####")} a {Fmt.N(max, "0.####")} {un}.");
        }
        Faixa("Módulo", Modulo, 0.5, 3);
        Faixa("Largura do painel", LarguraPainel, 0.3, 3);
        Faixa("Junta", Junta, 0, 0.05);
        Faixa("Ajuste mínimo", AjusteMin, 0.05, 1.2);
        Faixa("Ajuste preferido", AjusteBom, 0.05, 3);
        Faixa("Folga na alma", FolgaPonta, 0, 0.1);
        Faixa("Folga em volta do pilar", FolgaPilar, 0, 0.1);
        Faixa("Recorte mínimo", RecorteMin, 0, 0.1);
        Faixa("Apoio mínimo", ApoioMin, 0.01, 0.5);
        Faixa("Consolo além do recorte", ConsoloAlemDoRecorte, 0, 2);
        Faixa("Faixa maciça mínima", FaixaMacicaMin, 0, 1);
        Faixa("Distância para apoio em consolo", DistanciaConsolo, 0, 0.5);
        Faixa("Altura do painel", AlturaPainel, 0.05, 1);
        Faixa("Capa", Capa, 0, 0.3);
        Faixa("Diâmetro do tirante", Tirante.Diametro, 0.006, 0.05);
        Faixa("Chapa (ao longo da junta)", Tirante.ChapaAoLongoJunta, 0.03, 1);
        Faixa("Chapa (transversal)", Tirante.ChapaTransversal, 0.05, 1);
        Faixa("Espessura da chapa", Tirante.ChapaEspessura, 0.002, 0.05);
        Faixa("Malha ao longo da junta", Tirante.MalhaLongitudinal, 0.2, 10);
        Faixa("Malha entre juntas", Tirante.MalhaTransversal, 0.2, 10);
        Faixa("Margem do tirante à ponta da junta", Tirante.MargemPontaJunta, 0, 2);
        Faixa("Leitura da alma (abaixo da base do painel)", LeituraViga.DzAlma, -2, 0);
        Faixa("Leitura da aba (abaixo da base do painel)", LeituraViga.DzAba, -0.5, 0);
        Faixa("Faixa do painel: início", LeituraViga.DzPainelMin, 0, 1);
        Faixa("Faixa do painel: fim", LeituraViga.DzPainelMax, 0, 1);
        Faixa("Passo da leitura", LeituraViga.PassoPainel, 0.005, 0.2);
        Faixa("Passo do perfil da chave", Fase3.PassoPerfilChave, 0.001, 0.02);
        Faixa("Sobra da barra (cima)", Fase3.SobraBarraCima, 0, 0.3);
        Faixa("Sobra da barra (baixo)", Fase3.SobraBarraBaixo, 0, 0.3);
        Faixa("Porca: diâmetro", Fase3.PorcaDiametro, 0, 0.1);
        Faixa("Porca: altura", Fase3.PorcaAltura, 0, 0.1);
        Faixa("Folga do furo em obra", Fase3.FolgaFuro, 0, 0.02);
        if (e.Count > 0) return e;
        if (Math.Abs(Modulo - (LarguraPainel + Junta)) > 1e-4)
            e.Add($"O módulo deve ser a largura do painel + a junta ({Fmt.N((LarguraPainel + Junta) * 100, "0.##")} cm).");
        if (AjusteMin >= LarguraPainel) e.Add("O ajuste mínimo deve ser menor que a largura do painel.");
        if (AjusteBom < AjusteMin) e.Add("O ajuste preferido não pode ser menor que o ajuste mínimo.");
        if (Tirante.ChapaTransversal <= Junta) e.Add("A chapa tem de ser mais larga que a junta.");
        if (LeituraViga.DzPainelMax < LeituraViga.DzPainelMin) e.Add("Faixa do painel: o fim deve ser maior que o início.");
        var ets = new HashSet<int> { Etapas.Paineis, Etapas.ChavesEFaixasMacicas, Etapas.Capa };
        if (ets.Count != 3) e.Add("As três etapas de concretagem precisam ter valores diferentes.");
        return e;
    }

    // ------------------------------------------------------------------ JSON

    public static ConfigPaginacao DeJson(ValorJson o)
    {
        var c = new ConfigPaginacao { _original = o };
        if (o == null || !o.EhObjeto) return c;
        const string r = "config";
        c.Estrategia = EstrategiaDeNome(LeitorJson.Txt(o, "estrategia", "aberturas_primeiro", r));
        c.BordaInicioVMin = LeitorJson.Txt(o, "borda_inicio", "v_min", r) != "v_max";
        c.Modulo = LeitorJson.Num(o, "modulo", c.Modulo, r);
        c.LarguraPainel = LeitorJson.Num(o, "largura_painel", c.LarguraPainel, r);
        c.Junta = LeitorJson.Num(o, "junta", c.Junta, r);
        c.AjusteMin = LeitorJson.Num(o, "ajuste_min", c.AjusteMin, r);
        c.AjusteBom = LeitorJson.Num(o, "ajuste_bom", c.AjusteBom, r);
        c.FolgaPonta = LeitorJson.Num(o, "folga_ponta", c.FolgaPonta, r);
        c.FolgaPilar = LeitorJson.Num(o, "folga_pilar", c.FolgaPilar, r);
        c.RecorteMin = LeitorJson.Num(o, "recorte_min", c.RecorteMin, r);
        c.ApoioMin = LeitorJson.Num(o, "apoio_min", c.ApoioMin, r);
        c.ConsoloAlemDoRecorte = LeitorJson.Num(o, "consolo_alem_do_recorte", c.ConsoloAlemDoRecorte, r);
        c.FaixaMacicaMin = LeitorJson.Num(o, "faixa_macica_min", c.FaixaMacicaMin, r);
        c.DistanciaConsolo = LeitorJson.Num(o, "distancia_consolo", c.DistanciaConsolo, r);
        c.AlturaPainel = LeitorJson.Num(o, "altura_painel", c.AlturaPainel, r);
        c.Capa = LeitorJson.Num(o, "capa", c.Capa, r);
        c.SuportesInstalacoes = LeitorJson.Bool(o, "suportes_instalacoes", c.SuportesInstalacoes, r);
        c.ArquivoCatalogo = LeitorJson.Txt(o, "catalogo", "", r) ?? "";
        var t = o["tirante"];
        if (t != null && t.EhObjeto)
        {
            c.Tirante.Diametro = LeitorJson.Num(t, "diametro", c.Tirante.Diametro, r + ".tirante");
            c.Tirante.ChapaAoLongoJunta = LeitorJson.Num(t, "chapa_ao_longo_junta", c.Tirante.ChapaAoLongoJunta, r + ".tirante");
            c.Tirante.ChapaTransversal = LeitorJson.Num(t, "chapa_transversal", c.Tirante.ChapaTransversal, r + ".tirante");
            c.Tirante.ChapaEspessura = LeitorJson.Num(t, "chapa_espessura", c.Tirante.ChapaEspessura, r + ".tirante");
            c.Tirante.MalhaLongitudinal = LeitorJson.Num(t, "malha_longitudinal", c.Tirante.MalhaLongitudinal, r + ".tirante");
            c.Tirante.MalhaTransversal = LeitorJson.Num(t, "malha_transversal", c.Tirante.MalhaTransversal, r + ".tirante");
            c.Tirante.MargemPontaJunta = LeitorJson.Num(t, "margem_ponta_junta", c.Tirante.MargemPontaJunta, r + ".tirante");
        }
        var l = o["leitura_viga"];
        if (l != null && l.EhObjeto)
        {
            c.LeituraViga.DzAlma = LeitorJson.Num(l, "dz_alma", c.LeituraViga.DzAlma, r + ".leitura_viga");
            c.LeituraViga.DzAba = LeitorJson.Num(l, "dz_aba", c.LeituraViga.DzAba, r + ".leitura_viga");
            c.LeituraViga.DzPainelMin = LeitorJson.Num(l, "dz_painel_min", c.LeituraViga.DzPainelMin, r + ".leitura_viga");
            c.LeituraViga.DzPainelMax = LeitorJson.Num(l, "dz_painel_max", c.LeituraViga.DzPainelMax, r + ".leitura_viga");
            c.LeituraViga.PassoPainel = LeitorJson.Num(l, "passo_painel", c.LeituraViga.PassoPainel, r + ".leitura_viga");
        }
        var et = o["etapas"];
        if (et != null && et.EhObjeto)
        {
            c.Etapas.Paineis = LeitorJson.Int(et, "paineis", c.Etapas.Paineis, r + ".etapas");
            c.Etapas.ChavesEFaixasMacicas = LeitorJson.Int(et, "chaves_e_faixas_macicas", c.Etapas.ChavesEFaixasMacicas, r + ".etapas");
            c.Etapas.Capa = LeitorJson.Int(et, "capa", c.Etapas.Capa, r + ".etapas");
        }
        var ins = o["insercao"];
        if (ins != null && ins.EhObjeto)
        {
            string ri = r + ".insercao";
            c.Insercao.FamiliaPainel = LeitorJson.Txt(ins, "familia_painel", "", ri) ?? "";
            c.Insercao.TipoPainel = LeitorJson.Txt(ins, "tipo_painel", "", ri) ?? "";
            c.Insercao.CriarTipoPorLargura = LeitorJson.Bool(ins, "criar_tipo_por_largura", false, ri);
            c.Insercao.ParametroLargura = LeitorJson.Txt(ins, "parametro_largura", c.Insercao.ParametroLargura, ri);
            c.Insercao.ParametrosFolga = LeitorJson.ListaTxt(ins, "parametros_folga", c.Insercao.ParametrosFolga, ri);
            c.Insercao.ParametroEtapa = LeitorJson.Txt(ins, "parametro_etapa", c.Insercao.ParametroEtapa, ri);
            c.Insercao.PrefixoPainel = LeitorJson.Txt(ins, "prefixo_painel", c.Insercao.PrefixoPainel, ri);
            c.Insercao.PrefixoMacica = LeitorJson.Txt(ins, "prefixo_macica", c.Insercao.PrefixoMacica, ri);
            c.Insercao.PrefixoJunta = LeitorJson.Txt(ins, "prefixo_junta", c.Insercao.PrefixoJunta, ri);
        }
        var f3 = o["fase3"];
        if (f3 != null && f3.EhObjeto)
        {
            string rf = r + ".fase3";
            c.Fase3.Capa = LeitorJson.Bool(f3, "capa", c.Fase3.Capa, rf);
            c.Fase3.Chaves = LeitorJson.Bool(f3, "chaves", c.Fase3.Chaves, rf);
            c.Fase3.PassoPerfilChave = LeitorJson.Num(f3, "passo_perfil_chave", c.Fase3.PassoPerfilChave, rf);
            c.Fase3.SobraBarraCima = LeitorJson.Num(f3, "sobra_barra_cima", c.Fase3.SobraBarraCima, rf);
            c.Fase3.SobraBarraBaixo = LeitorJson.Num(f3, "sobra_barra_baixo", c.Fase3.SobraBarraBaixo, rf);
            c.Fase3.PorcaDiametro = LeitorJson.Num(f3, "porca_diametro", c.Fase3.PorcaDiametro, rf);
            c.Fase3.PorcaAltura = LeitorJson.Num(f3, "porca_altura", c.Fase3.PorcaAltura, rf);
            c.Fase3.BarraNaoCabe = LeitorJson.Txt(f3, "barra_nao_cabe", "furo_em_obra", rf) switch
            {
                "nao_inserir" => TratamentoBarraJunta.NaoInserir,
                "inserir_e_avisar" => TratamentoBarraJunta.InserirEAvisar,
                _ => TratamentoBarraJunta.FuroEmObra,
            };
            c.Fase3.FolgaFuro = LeitorJson.Num(f3, "folga_furo", c.Fase3.FolgaFuro, rf);
            c.Fase3.MaterialAco = LeitorJson.Txt(f3, "material_aco", "", rf) ?? "";
        }
        return c;
    }

    public ValorJson ParaJson()
    {
        // parte do objeto lido (mantém chaves desconhecidas e a ordem) e sobrescreve as conhecidas
        var o = _original != null && _original.EhObjeto ? Json.Ler(Json.Gravar(_original, false)) : ValorJson.NovoObjeto();
        if (!o.Tem("_comentario"))
            o.Definir("_comentario", "Paginação de lajes alveolares (DEB.Revit). Medidas em metros; leitura_viga em cotas relativas à base do painel.");
        o.Definir("estrategia", NomeEstrategia(Estrategia));
        o.Definir("borda_inicio", BordaInicioVMin ? "v_min" : "v_max");
        o.Definir("modulo", Modulo);
        o.Definir("largura_painel", LarguraPainel);
        o.Definir("junta", Junta);
        o.Definir("ajuste_min", AjusteMin);
        o.Definir("ajuste_bom", AjusteBom);
        o.Definir("folga_ponta", FolgaPonta);
        o.Definir("folga_pilar", FolgaPilar);
        o.Definir("recorte_min", RecorteMin);
        o.Definir("apoio_min", ApoioMin);
        o.Definir("consolo_alem_do_recorte", ConsoloAlemDoRecorte);
        o.Definir("faixa_macica_min", FaixaMacicaMin);
        o.Definir("distancia_consolo", DistanciaConsolo);
        o.Definir("altura_painel", AlturaPainel);
        o.Definir("capa", Capa);
        o.Definir("suportes_instalacoes", SuportesInstalacoes);
        o.Definir("catalogo", ArquivoCatalogo ?? "");
        var t = Sub(o, "tirante");
        t.Definir("diametro", Tirante.Diametro);
        t.Definir("chapa_ao_longo_junta", Tirante.ChapaAoLongoJunta);
        t.Definir("chapa_transversal", Tirante.ChapaTransversal);
        t.Definir("chapa_espessura", Tirante.ChapaEspessura);
        t.Definir("malha_longitudinal", Tirante.MalhaLongitudinal);
        t.Definir("malha_transversal", Tirante.MalhaTransversal);
        t.Definir("margem_ponta_junta", Tirante.MargemPontaJunta);
        var l = Sub(o, "leitura_viga");
        l.Definir("dz_alma", LeituraViga.DzAlma);
        l.Definir("dz_aba", LeituraViga.DzAba);
        l.Definir("dz_painel_min", LeituraViga.DzPainelMin);
        l.Definir("dz_painel_max", LeituraViga.DzPainelMax);
        l.Definir("passo_painel", LeituraViga.PassoPainel);
        var et = Sub(o, "etapas");
        et.Definir("paineis", Etapas.Paineis);
        et.Definir("chaves_e_faixas_macicas", Etapas.ChavesEFaixasMacicas);
        et.Definir("capa", Etapas.Capa);
        var ins = Sub(o, "insercao");
        ins.Definir("familia_painel", Insercao.FamiliaPainel ?? "");
        ins.Definir("tipo_painel", Insercao.TipoPainel ?? "");
        ins.Definir("criar_tipo_por_largura", Insercao.CriarTipoPorLargura);
        ins.Definir("parametro_largura", Insercao.ParametroLargura ?? "");
        var lf = ValorJson.NovaLista();
        foreach (var s in Insercao.ParametrosFolga ?? new List<string>()) lf.Adicionar(ValorJson.De(s));
        ins.Definir("parametros_folga", lf);
        ins.Definir("parametro_etapa", Insercao.ParametroEtapa ?? "");
        ins.Definir("prefixo_painel", Insercao.PrefixoPainel ?? "");
        ins.Definir("prefixo_macica", Insercao.PrefixoMacica ?? "");
        ins.Definir("prefixo_junta", Insercao.PrefixoJunta ?? "");
        var f3 = Sub(o, "fase3");
        f3.Definir("capa", Fase3.Capa);
        f3.Definir("chaves", Fase3.Chaves);
        f3.Definir("passo_perfil_chave", Fase3.PassoPerfilChave);
        f3.Definir("sobra_barra_cima", Fase3.SobraBarraCima);
        f3.Definir("sobra_barra_baixo", Fase3.SobraBarraBaixo);
        f3.Definir("porca_diametro", Fase3.PorcaDiametro);
        f3.Definir("porca_altura", Fase3.PorcaAltura);
        f3.Definir("barra_nao_cabe", Fase3.BarraNaoCabe switch
        {
            TratamentoBarraJunta.NaoInserir => "nao_inserir",
            TratamentoBarraJunta.InserirEAvisar => "inserir_e_avisar",
            _ => "furo_em_obra",
        });
        f3.Definir("folga_furo", Fase3.FolgaFuro);
        f3.Definir("material_aco", Fase3.MaterialAco ?? "");
        return o;
    }

    private static ValorJson Sub(ValorJson o, string k)
    {
        var s = o[k];
        if (s == null || !s.EhObjeto) { s = ValorJson.NovoObjeto(); o.Definir(k, s); }
        return s;
    }

    /// <summary>Lê o arquivo; ausente → padrão. Arquivo com erro → padrão e a mensagem em <paramref name="erro"/>.</summary>
    public static ConfigPaginacao Carregar(string caminho, out string erro)
    {
        erro = null;
        try
        {
            if (string.IsNullOrEmpty(caminho) || !File.Exists(caminho)) return new ConfigPaginacao();
            return DeJson(Json.LerArquivo(caminho));
        }
        catch (Exception ex)
        {
            erro = "Não consegui ler a configuração (" + caminho + "): " + ex.Message + " — usando os valores padrão.";
            return new ConfigPaginacao();
        }
    }

    public void Gravar(string caminho) => Json.GravarArquivo(caminho, ParaJson());

    /// <summary>Cópia independente (a janela edita a cópia; o original só muda ao salvar).</summary>
    public ConfigPaginacao Clonar() { var c = DeJson(ParaJson()); c._original = _original; return c; }
}
