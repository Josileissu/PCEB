using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Analise.Barras;
using DEB.Core.Configuracao.Projeto;
using DEB.Core.Elementos.Metalica;
using DEB.Core.Elementos.Metalica.Ligacoes;
using DEB.Core.Intercambio;
using DEB.Core.Persistencia;

namespace DEB.Core.Analise.Objetos;

/// <summary>Uma propriedade ajustável de um objeto da análise (D50): número (com unidade) ou opção.</summary>
public sealed class PropriedadeAjuste
{
    public string Id { get; set; } = "";
    public string Rotulo { get; set; } = "";
    public string Grupo { get; set; } = "";
    public string Unidade { get; set; } = "";
    public IReadOnlyList<string> Opcoes { get; set; } = Array.Empty<string>();
    public string Dica { get; set; } = "";
    internal Func<object, string> Ler { get; set; }
    internal Action<object, string> Gravar { get; set; }
}

/// <summary>
/// Ajustes do modelo analítico feitos na janela de análise (D50): por objeto (chave = tipo:id do dono) e propriedade, o valor
/// digitado. Ficam gravados no modelo do hospedeiro e são reaplicados toda vez que a estrutura é montada — o que vem do Revit é o
/// ponto de partida; o ajuste vale por cima. Unidades: cm, kN/m, kN/m², kN/cm² como nos objetos.
/// </summary>
public static class AjustesAnalise
{
    private static string F(double x) => x.ToString("0.####", CultureInfo.InvariantCulture);
    private static double D(string s) => double.Parse((s ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
    private static readonly string[] SimNao = { "Sim", "Não" };

    private static PropriedadeAjuste P<T>(string id, string rot, string grupo, string un, Func<T, string> ler, Action<T, string> gravar,
                                          string[] ops = null, string dica = "") where T : class =>
        new PropriedadeAjuste { Id = id, Rotulo = rot, Grupo = grupo, Unidade = un, Opcoes = ops ?? Array.Empty<string>(), Dica = dica,
            Ler = o => ler((T)o), Gravar = (o, v) => gravar((T)o, v) };

    private static readonly PropriedadeAjuste[] Pilar =
    {
        P<PilarObjeto>("embutimento", "Embutimento (engaste a partir da base)", "Vinculação", "cm", p => F(p.Embutimento), (p, v) => p.Embutimento = D(v)),
        P<PilarObjeto>("base", "Base", "Vinculação", "", p => p.BaseArticulada ? "Articulada" : "Engastada", (p, v) => p.BaseArticulada = v.StartsWith("Art"),
          new[] { "Engastada", "Articulada" }),
        P<PilarObjeto>("fatorFlexao", "Fator da rigidez à flexão", "Rigidez", "", p => F(p.FatorFlexao), (p, v) => p.FatorFlexao = D(v), null,
          "NBR 6118 15.7.3: 0,8 nos pilares na análise de 2ª ordem global; 1 = elástica."),
    };

    private static readonly PropriedadeAjuste[] Viga =
    {
        P<VigaObjeto>("g", "Carga permanente além do peso próprio", "Cargas", "kN/m", v => F(v.G), (v, s) => v.G = D(s)),
        P<VigaObjeto>("q", "Carga variável", "Cargas", "kN/m", v => F(v.Q), (v, s) => v.Q = D(s)),
        P<VigaObjeto>("fatorFlexao", "Fator da rigidez à flexão", "Rigidez", "", v => F(v.FatorFlexao), (v, s) => v.FatorFlexao = D(s)),
        P<VigaObjeto>("fatorTorcao", "Fator da rigidez à torção", "Rigidez", "", v => F(v.FatorTorcao), (v, s) => v.FatorTorcao = D(s), null,
          "0,15 = concreto fissurado (prática usual); 1 = elástica."),
    };

    private static ApoioPeca Aparelho(LigacaoConsolo l) => l.Aparelho ??= new ApoioPeca { Tipo = ApoioPeca.Neoprene };
    private static readonly PropriedadeAjuste[] Consolo =
    {
        P<LigacaoConsolo>("representacao", "Representação", "Modelo", "", l => l.Representacao == RepresentacaoLigacao.BarraRigidaEMola ? "Barra rígida + mola do aparelho" : "Barra rígida + articulação",
          (l, v) => l.Representacao = v.StartsWith("Barra rígida + mola") ? RepresentacaoLigacao.BarraRigidaEMola : RepresentacaoLigacao.BarraRigidaArticulada,
          new[] { "Barra rígida + mola do aparelho", "Barra rígida + articulação" }),
        P<LigacaoConsolo>("aparelho", "Aparelho de apoio", "Aparelho", "", l => l.Aparelho == null ? "—" : ApoioPeca.Texto(l.Aparelho.Tipo),
          (l, v) => { if (v == "—") l.Aparelho = null; else Aparelho(l).Tipo = ApoioPeca.TipoDoTexto(v); }, ApoioPeca.Opcoes),
        P<LigacaoConsolo>("apComp", "Comprimento (ao longo da viga)", "Aparelho", "cm", l => F(l.Aparelho?.Comprimento ?? 0), (l, v) => Aparelho(l).Comprimento = D(v)),
        P<LigacaoConsolo>("apLarg", "Largura", "Aparelho", "cm", l => F(l.Aparelho?.Largura ?? 0), (l, v) => Aparelho(l).Largura = D(v)),
        P<LigacaoConsolo>("apEsp", "Espessura do elastômero", "Aparelho", "cm", l => F(l.Aparelho?.Espessura ?? 0), (l, v) => Aparelho(l).Espessura = D(v)),
        P<LigacaoConsolo>("zTopo", "Altura do topo do consolo (da base do pilar)", "Geometria", "cm", l => F(l.ZTopoConsolo), (l, v) => l.ZTopoConsolo = D(v)),
    };

    private static readonly PropriedadeAjuste[] Direta =
    {
        P<LigacaoPilarViga>("articulada", "Ligação", "Modelo", "", l => l.Articulada ? "Articulada" : "Monolítica", (l, v) => l.Articulada = v.StartsWith("Art"),
          new[] { "Monolítica", "Articulada" }),
    };

    private static readonly PropriedadeAjuste[] Laje =
    {
        P<LajeObjeto>("espessura", "Espessura", "Geometria", "cm", l => F(l.Espessura), (l, v) => l.Espessura = D(v)),
        P<LajeObjeto>("tipo", "Tipo", "Geometria", "", l => l.Tipo.ToString(), (l, v) => l.Tipo = (TipoLaje)Enum.Parse(typeof(TipoLaje), v),
          Enum.GetNames(typeof(TipoLaje))),
        P<LajeObjeto>("fracao", "Fração maciça (peso)", "Geometria", "", l => F(l.FracaoMacica), (l, v) => l.FracaoMacica = D(v)),
        P<LajeObjeto>("g", "Carga permanente além do peso próprio", "Cargas", "kN/m²", l => F(l.G), (l, v) => l.G = D(v)),
        P<LajeObjeto>("q", "Carga variável", "Cargas", "kN/m²", l => F(l.Q), (l, v) => l.Q = D(v)),
        P<LajeObjeto>("malha", "Malha da grelha", "Modelo", "cm", l => F(l.Malha), (l, v) => l.Malha = D(v)),
        P<LajeObjeto>("bordas", "Bordas apoiadas (sem vigas embaixo)", "Apoios", "", l => l.ApoiarBordas ? "Sim" : "Não", (l, v) => l.ApoiarBordas = v == "Sim", SimNao,
          "Sim: toda a borda vira apoio simples (laje isolada); Não: a laje apoia nas vigas e pilares que estão embaixo."),
    };

    private static string Lig(LigacaoExtremidade x) => x == LigacaoExtremidade.Rigida ? "Rígida" : "Flexível";
    private static LigacaoExtremidade Lig(string v) => v.StartsWith("R") ? LigacaoExtremidade.Rigida : LigacaoExtremidade.Flexivel;
    private static readonly PropriedadeAjuste[] Aco =
    {
        P<BarraMetalicaObjeto>("perfil", "Perfil", "Seção", "", b => b.Perfil.Nome, (b, v) => b.Perfil = PerfilI.Buscar(v) ?? throw new ArgumentException($"perfil \"{v}\" não reconhecido")),
        P<BarraMetalicaObjeto>("aco", "Aço", "Seção", "", b => b.Aco.Nome, (b, v) => b.Aco = AcoEstrutural.Buscar(v) ?? b.Aco, AcoEstrutural.Lista.Select(a => a.Nome).ToArray()),
        P<BarraMetalicaObjeto>("ligI", "Ligação na ponta I", "Ligações", "", b => Lig(b.LigacaoI), (b, v) => b.LigacaoI = Lig(v), new[] { "Flexível", "Rígida" }),
        P<BarraMetalicaObjeto>("ligJ", "Ligação na ponta J", "Ligações", "", b => Lig(b.LigacaoJ), (b, v) => b.LigacaoJ = Lig(v), new[] { "Flexível", "Rígida" }),
        P<BarraMetalicaObjeto>("base", "Base (pilar)", "Ligações", "", b => b.Base == BasePilarMetalico.Engastada ? "Engastada" : "Rotulada",
          (b, v) => b.Base = v.StartsWith("R") ? BasePilarMetalico.Rotulada : BasePilarMetalico.Engastada, new[] { "Engastada", "Rotulada" }),
        P<BarraMetalicaObjeto>("lb", "Comprimento destravado Lb (0 = a barra)", "Estabilidade", "cm", b => F(b.Lb), (b, v) => b.Lb = D(v)),
        P<BarraMetalicaObjeto>("g", "Carga permanente (viga)", "Cargas", "kN/m", b => F(b.G), (b, v) => b.G = D(v)),
        P<BarraMetalicaObjeto>("q", "Carga variável (viga)", "Cargas", "kN/m", b => F(b.Q), (b, v) => b.Q = D(v)),
    };

    /// <summary>Propriedades ajustáveis do objeto (vazio = nenhuma).</summary>
    public static IReadOnlyList<PropriedadeAjuste> Do(object o) => o switch
    {
        PilarObjeto _ => Pilar,
        VigaObjeto _ => Viga,
        LigacaoConsolo _ => Consolo,
        LigacaoPilarViga _ => Direta,
        LajeObjeto _ => Laje,
        BarraMetalicaObjeto b => b.Pilar ? Aco.Where(p => p.Id != "g" && p.Id != "q").ToArray() : Aco.Where(p => p.Id != "base").ToArray(),
        LigacaoMetalica _ => Array.Empty<PropriedadeAjuste>(),
        _ => Array.Empty<PropriedadeAjuste>(),
    };

    public static string Chave(Dono d) => d.Tipo + ":" + (d.Id != "" ? d.Id : d.Nome);

    /// <summary>Todos os objetos ajustáveis da estrutura, pela chave.</summary>
    public static Dictionary<string, object> Objetos(EstruturaObjetos e)
    {
        var r = new Dictionary<string, object>();
        void A(Dono d, object o) { if (d != null) r[Chave(d)] = o; }
        foreach (var x in e.Pilares) A(x.Dono, x);
        foreach (var x in e.Vigas) A(x.Dono, x);
        foreach (var x in e.Ligacoes) A(x.Dono, x);
        foreach (var x in e.LigacoesDiretas) A(x.Dono, x);
        foreach (var x in e.Lajes) A(x.Dono, x);
        foreach (var x in e.Metalicas) A(x.Dono, x);
        foreach (var x in e.LigacoesMetalicas) A(x.Dono, x);
        return r;
    }

    /// <summary>Ligação metálica: o modelo da biblioteca escolhido (opções = os do tipo).</summary>
    public static IReadOnlyList<string> ModelosLigacao(EstruturaObjetos e, LigacaoMetalica l) =>
        (e.Biblioteca ?? BibliotecaLigacoes.Carregar()).Todos.Where(m => m.Tipo == l.Tipo).Select(m => m.Id).ToList();

    /// <summary>
    /// Aplica os ajustes aos objetos (antes de gerar o modelo). As ligações metálicas são recriadas ao gerar: o modelo escolhido
    /// para elas vai em <see cref="EstruturaObjetos.ModelosLigacaoEscolhidos"/>. Devolve os avisos (ajuste que não vale mais).
    /// </summary>
    public static List<string> Aplicar(EstruturaObjetos e, Dictionary<string, Dictionary<string, string>> ajustes)
    {
        var avisos = new List<string>();
        var obj = Objetos(e);
        e.ModelosLigacaoEscolhidos.Clear();
        foreach (var kv in ajustes)
        {
            if (kv.Key.StartsWith(TipoDono.Ligacao + ":") && kv.Value.TryGetValue("modelo", out string mod)) { e.ModelosLigacaoEscolhidos[kv.Key] = mod; }
            if (!obj.TryGetValue(kv.Key, out object o)) { if (!kv.Value.ContainsKey("modelo")) avisos.Add($"Ajuste de {kv.Key}: o objeto não está mais no modelo."); continue; }
            var props = Do(o);
            foreach (var pv in kv.Value)
            {
                var p = props.FirstOrDefault(x => x.Id == pv.Key);
                if (p == null) continue;
                try { p.Gravar(o, pv.Value); }
                catch (Exception ex) { avisos.Add($"Ajuste {kv.Key}·{pv.Key} = \"{pv.Value}\" não aplicado: {ex.Message}"); }
            }
        }
        return avisos;
    }

    public static string ParaJson(Dictionary<string, Dictionary<string, string>> a)
    {
        var raiz = ValorJson.NovoObjeto();
        foreach (var kv in a.OrderBy(x => x.Key))
        {
            var o = ValorJson.NovoObjeto();
            foreach (var pv in kv.Value.OrderBy(x => x.Key)) o.Definir(pv.Key, pv.Value);
            raiz.Definir(kv.Key, o);
        }
        return Json.Gravar(raiz, indentado: false);
    }

    public static Dictionary<string, Dictionary<string, string>> DeJson(string json)
    {
        var r = new Dictionary<string, Dictionary<string, string>>();
        if (string.IsNullOrWhiteSpace(json) || !Json.TentarLer(json, out ValorJson raiz, out _) || raiz == null || !raiz.EhObjeto) return r;
        foreach (var kv in raiz.Campos)
        {
            var d = new Dictionary<string, string>();
            foreach (var pv in kv.Value.Campos) d[pv.Key] = pv.Value.Texto ?? pv.Value.Numero.ToString(CultureInfo.InvariantCulture);
            r[kv.Key] = d;
        }
        return r;
    }
}

/// <summary>
/// Sessão da janela de análise com ajustes (D50): guarda a estrutura e os ajustes; "ajustar" e "restaurar" mudam um objeto,
/// regeram e reanalisam, e devolvem o JSON novo da janela. O hospedeiro grava <see cref="Ajustes"/> no modelo ao fechar.
/// </summary>
public sealed class SessaoAnalise
{
    public EstruturaObjetos Estrutura { get; }
    public ConfiguracaoProjeto Cfg { get; }
    public string Titulo { get; }
    public Dictionary<string, Dictionary<string, string>> Ajustes { get; }
    public bool Alterado { get; private set; }
    /// <summary>As cargas lançadas na janela mudaram (o hospedeiro grava).</summary>
    public bool CargasAlteradas { get; private set; }
    /// <summary>A página pediu para criar no hospedeiro (Revit) as cargas lançadas no PCEB (feito ao fechar a janela).</summary>
    public bool EnviarCargas { get; private set; }
    /// <summary>
    /// Gravação de um arquivo exportado (D50, item 8): o hospedeiro recebe o nome sugerido e o texto, pergunta onde gravar e devolve
    /// o caminho (null = cancelado). Sem hospedeiro, a exportação só devolve o texto.
    /// </summary>
    public Func<string, string, string> GravarArquivo { get; set; }

    public SessaoAnalise(EstruturaObjetos e, ConfiguracaoProjeto cfg, string titulo, Dictionary<string, Dictionary<string, string>> ajustes)
    {
        Estrutura = e; Cfg = cfg ?? new ConfiguracaoProjeto(); Titulo = titulo ?? ""; Ajustes = ajustes ?? new Dictionary<string, Dictionary<string, string>>();
        _partida = e.ValoresDePartida;
    }

    public string Json() => VisualizacaoAnalise.Json(Estrutura, Titulo, Cfg, Ajustes);

    /// <summary>Mensagens: {acao:"pronto"}; {acao:"ajustar", chave, prop, valor}; {acao:"restaurar", chave, prop?}. Devolve o JSON da janela ou "".</summary>
    public string Receber(string mensagem)
    {
        ValorJson m;
        try { m = Persistencia.Json.Ler(mensagem ?? ""); } catch (ErroJson) { return ""; }
        string acao = m?["acao"]?.Texto ?? "";
        if (acao == "pronto") return Json();
        if (acao == "carga") return Carga(m);
        if (acao == "detalhe")
        {
            // detalhe do consolo (item 3): chapa e biela-tirante calculados sob demanda
            string ch = m["chave"]?.Texto ?? "";
            var lc = Estrutura.Ligacoes.FirstOrDefault(x => AjustesAnalise.Chave(x.Dono) == ch);
            if (lc == null) return "";
            try { return DEB.Core.Analise.Chapas.DetalheConsolo.Json(Estrutura, lc, Cfg, ch); }
            catch (Exception ex) { return "{\"tipo\":\"consolo\",\"chave\":" + Persistencia.Json.Gravar(ValorJson.De(ch), false) + ",\"erro\":" + Persistencia.Json.Gravar(ValorJson.De(ex.Message), false) + "}"; }
        }
        if (acao == "exportar")
        {
            string f = m["formato"]?.Texto ?? "";
            var fmt = f == "CalculiX" ? DEB.Core.Analise.Barras.FormatoExportacao.CalculiX : f == "Sap2000" ? DEB.Core.Analise.Barras.FormatoExportacao.Sap2000
                    : DEB.Core.Analise.Barras.FormatoExportacao.OpenSees;
            string aviso;
            try
            {
                string texto = DEB.Core.Analise.Barras.ExportacaoAnalise.Exportar(Estrutura.Modelo, fmt, Titulo);
                string nome = "modelo_pceb" + DEB.Core.Analise.Barras.ExportacaoAnalise.Extensao(fmt);
                string arq = GravarArquivo?.Invoke(nome, texto);
                aviso = arq == null ? (GravarArquivo == null ? "Exportação gerada (prévia sem o Revit: nada gravado)." : "Exportação cancelada.")
                      : $"Modelo exportado para {fmt}: {arq}" + (fmt == DEB.Core.Analise.Barras.FormatoExportacao.OpenSees ? " (rode com python; os resultados saem em resultados_<caso>.json)"
                        : fmt == DEB.Core.Analise.Barras.FormatoExportacao.CalculiX ? " (rode com ccx; seção retangular equivalente nas inércias)" : " (SAP2000: Arquivo → Importar → .s2k)");
            }
            catch (Exception ex) { aviso = "Exportação não feita: " + ex.Message; }
            return "{\"tipo\":\"aviso\",\"texto\":" + Persistencia.Json.Gravar(ValorJson.De(aviso), false) + "}";
        }
        if (acao == "enviarCargas") { EnviarCargas = true; return Json().Insert(1, "\"aviso\":\"As cargas do PCEB serão criadas no Revit ao fechar a janela.\","); }
        if (acao != "ajustar" && acao != "restaurar") return "";
        string chave = m["chave"]?.Texto ?? "", prop = m["prop"]?.Texto ?? "";
        string erro = null;
        var antes = AjustesAnalise.ParaJson(Ajustes);
        if (acao == "ajustar")
        {
            string valor = (m["valor"]?.Texto ?? "").Trim();
            // valida no próprio objeto antes de guardar (perfil inexistente, número inválido…)
            if (AjustesAnalise.Objetos(Estrutura).TryGetValue(chave, out object alvo) && AjustesAnalise.Do(alvo).FirstOrDefault(p => p.Id == prop) is PropriedadeAjuste pa)
            {
                string atual = pa.Ler(alvo);
                try { pa.Gravar(alvo, valor); pa.Gravar(alvo, atual); }
                catch (Exception ex)
                {
                    try { pa.Gravar(alvo, atual); } catch { }
                    string j = Json();
                    return j.Insert(1, "\"erro\":" + Persistencia.Json.Gravar(ValorJson.De($"{pa.Rotulo}: \"{valor}\" não vale ({ex.Message})."), false) + ",");
                }
            }
            if (!Ajustes.TryGetValue(chave, out var d)) Ajustes[chave] = d = new Dictionary<string, string>();
            d[prop] = valor;
        }
        else if (Ajustes.TryGetValue(chave, out var d))
        {
            if (prop == "") Ajustes.Remove(chave); else { d.Remove(prop); if (d.Count == 0) Ajustes.Remove(chave); }
        }
        try { Reanalisar(); }
        catch (Exception ex)
        {
            erro = ex.Message;
            Ajustes.Clear();
            foreach (var kv in AjustesAnalise.DeJson(antes)) Ajustes[kv.Key] = kv.Value;
            try { Reanalisar(); } catch { }
        }
        Alterado = true;
        string json = Json();
        return erro == null ? json : json.Insert(1, "\"erro\":" + Persistencia.Json.Gravar(ValorJson.De("Ajuste não aplicado: " + erro), false) + ",");
    }

    /// <summary>
    /// Reaplica os ajustes e refaz a análise. Os objetos guardam o valor de partida (o lido do hospedeiro) na primeira vez que são
    /// ajustados, para "restaurar" voltar a ele.
    /// </summary>
    private void Reanalisar()
    {
        if (_partida == null)
        {
            _partida = new Dictionary<string, Dictionary<string, string>>();
            foreach (var kv in AjustesAnalise.Objetos(Estrutura))
                _partida[kv.Key] = AjustesAnalise.Do(kv.Value).ToDictionary(p => p.Id, p => p.Ler(kv.Value));
        }
        // volta tudo ao valor de partida e reaplica os ajustes
        var obj = AjustesAnalise.Objetos(Estrutura);
        foreach (var kv in _partida)
            if (obj.TryGetValue(kv.Key, out object o))
                foreach (var p in AjustesAnalise.Do(o))
                    if (kv.Value.TryGetValue(p.Id, out string v)) { try { p.Gravar(o, v); } catch { } }
        AjustesAnalise.Aplicar(Estrutura, Ajustes);
        Estrutura.Gerar(Cfg);
        Estrutura.Analisar(Cfg);
    }

    private Dictionary<string, Dictionary<string, string>> _partida;

    /// <summary>Cargas (item 2): op "adicionar" (alvo, tipo, caso, valor, posicao, fim, direcao) ou "remover" (id). Só as do PCEB mudam.</summary>
    private string Carga(ValorJson m)
    {
        string op = m["op"]?.Texto ?? "", erro = null;
        var e = Estrutura;
        double N(string k) => m[k] == null ? 0 : m[k].Tipo == TipoJson.Numero ? m[k].Numero
            : double.TryParse((m[k].Texto ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ? x : 0;
        if (op == "adicionar")
        {
            string alvo = m["alvo"]?.Texto ?? "";
            if (!AjustesAnalise.Objetos(e).TryGetValue(alvo, out object o)) erro = "objeto não encontrado";
            else if (!Enum.TryParse(m["tipo"]?.Texto ?? "", out TipoCargaObjeto t) || !CargasObjetos.Aceitas(o).Contains(t)) erro = "esse objeto não recebe esse tipo de carga";
            else if (Math.Abs(N("valor")) < 1e-12) erro = "valor da carga vazio";
            else
                e.Cargas.Add(new CargaObjeto
                {
                    Alvo = alvo, Tipo = t, Caso = (m["caso"]?.Texto ?? "G") == "Q" ? EstruturaObjetos.CasoQ : EstruturaObjetos.CasoG, Valor = N("valor"),
                    Posicao = N("posicao"), Fim = N("fim"), Direcao = m["direcao"]?.Texto is string d && (d == "X" || d == "Y") ? d : "Z", Origem = OrigemCarga.Pceb,
                });
        }
        else if (op == "remover")
        {
            string id = m["id"]?.Texto ?? "";
            if (e.Cargas.RemoveAll(c => c.Id == id && c.Origem == OrigemCarga.Pceb) == 0) erro = "só as cargas lançadas no PCEB são removidas aqui (as do Revit, no Revit)";
        }
        if (erro == null)
        {
            CargasAlteradas = true;
            try { e.Gerar(Cfg); e.Analisar(Cfg); }
            catch (Exception ex) { erro = ex.Message; }
        }
        string j = Json();
        return erro == null ? j : j.Insert(1, "\"erro\":" + Persistencia.Json.Gravar(ValorJson.De("Carga: " + erro), false) + ",");
    }

    /// <summary>Guarda os valores de partida antes do primeiro ajuste (chamar logo depois de montar, antes de aplicar ajustes gravados).</summary>
    public void GuardarPartida(Dictionary<string, Dictionary<string, string>> partida) => _partida = partida;

    /// <summary>Valores atuais de todas as propriedades ajustáveis (para guardar a partida antes de aplicar os ajustes gravados).</summary>
    public static Dictionary<string, Dictionary<string, string>> Valores(EstruturaObjetos e) =>
        AjustesAnalise.Objetos(e).ToDictionary(kv => kv.Key, kv => AjustesAnalise.Do(kv.Value).ToDictionary(p => p.Id, p => p.Ler(kv.Value)));
}
