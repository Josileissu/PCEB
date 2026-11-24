using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DEB.Core.Persistencia;

namespace DEB.Core.Configuracao.Projeto;

/// <summary>
/// Sessão da tela de configuração (D45): a página HTML (<see cref="Pagina"/>, a mesma no Revit e no CAD, dentro do WebView2) troca
/// mensagens JSON com o hospedeiro, que só as repassa para cá. Toda regra fica no núcleo (D26): a página mostra o estado e pede
/// ações; a sessão aplica na <see cref="ConfiguracaoProjeto"/> e devolve o estado novo. Gravar é com o hospedeiro (escritório em
/// <see cref="ArquivoEscritorio"/>, projeto no arquivo do modelo).
/// </summary>
public sealed class SessaoConfiguracao
{
    public ConfiguracaoProjeto Cfg { get; }
    /// <summary>Linha de contexto no topo ("Projeto: galpão.rvt · NBR 6118:2023 · NBR 9062:2017").</summary>
    public string Contexto { get; set; } = "";
    /// <summary>Há um projeto aberto (sem projeto: só o nível do escritório é editável).</summary>
    public bool ComProjeto { get; set; } = true;
    /// <summary>Algo mudou desde a abertura (o hospedeiro pergunta antes de fechar sem gravar).</summary>
    public bool Alterado { get; private set; }
    /// <summary>"salvar" ou "cancelar" depois que a página pediu para fechar; null enquanto aberta.</summary>
    public string Fim { get; private set; }

    public SessaoConfiguracao(ConfiguracaoProjeto cfg) { Cfg = cfg ?? new ConfiguracaoProjeto(); }

    /// <summary>Página da tela (recurso embutido no núcleo).</summary>
    public static string Pagina()
    {
        using Stream s = typeof(SessaoConfiguracao).GetTypeInfo().Assembly.GetManifestResourceStream("DEB.Core.TelaConfiguracao.html");
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    private static readonly string[] NomesNivel = { "Padrão do PCEB", "Escritório", "Projeto", "Grupo" };
    public static string NomeNivel(NivelConfiguracao n) => NomesNivel[(int)n];

    private static NivelConfiguracao LerNivel(ValorJson m)
    {
        string t = m["nivel"]?.Texto ?? "Projeto";
        return t == "Escritorio" || t == "Escritório" ? NivelConfiguracao.Escritorio : NivelConfiguracao.Projeto;
    }

    /// <summary>
    /// Trata uma mensagem da página e devolve a resposta (JSON) — sempre o estado do nível pedido, com "erro" se a ação não
    /// valeu. Ações: estado, definir (id, valor, justificativa), justificar (id, justificativa), restaurar (id), restaurarGrupo
    /// (grupo), sistema (sistema), promover (projeto → escritório), salvar, cancelar.
    /// </summary>
    public string Receber(string mensagem)
    {
        string erro = null;
        NivelConfiguracao nivel = NivelConfiguracao.Projeto;
        try
        {
            ValorJson m = Json.Ler(mensagem);
            nivel = LerNivel(m);
            if (!ComProjeto) nivel = NivelConfiguracao.Escritorio;
            string acao = m["acao"]?.Texto ?? "estado";
            string id = m["id"]?.Texto;
            switch (acao)
            {
                case "estado": break;
                case "definir":
                    Definir(nivel, id, m["valor"]?.Texto ?? "", m["justificativa"]?.Texto);
                    break;
                case "justificar":
                {
                    var n = Cfg.Nivel(nivel);
                    string j = (m["justificativa"]?.Texto ?? "").Trim();
                    if (!n.Valores.ContainsKey(id)) throw new ArgumentException("Só se justifica um valor gravado neste nível.");
                    if (j == "") n.Justificativas.Remove(id); else n.Justificativas[id] = j;
                    Alterado = true;
                    break;
                }
                case "restaurar":
                    Cfg.Restaurar(nivel, id); Alterado = true;
                    break;
                case "restaurarGrupo":
                    string g = m["grupo"]?.Texto;
                    foreach (var c in CatalogoConfiguracao.Campos.Where(c => c.Grupo == g)) Cfg.Restaurar(nivel, c.Id);
                    Alterado = true;
                    break;
                case "sistema":
                    Cfg.AplicarSistema(nivel, m["sistema"]?.Texto); Alterado = true;
                    break;
                case "promover":
                    foreach (var kv in Cfg.Projeto.Valores.ToList())
                    {
                        Cfg.Escritorio.Valores[kv.Key] = kv.Value;
                        if (Cfg.Projeto.Justificativas.TryGetValue(kv.Key, out string j)) Cfg.Escritorio.Justificativas[kv.Key] = j;
                        else Cfg.Escritorio.Justificativas.Remove(kv.Key);
                    }
                    Cfg.Projeto.Valores.Clear(); Cfg.Projeto.Justificativas.Clear();
                    Alterado = true;
                    break;
                case "salvar": Fim = "salvar"; break;
                case "cancelar": Fim = "cancelar"; break;
                default: throw new ArgumentException("Ação desconhecida: " + acao);
            }
        }
        catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is KeyNotFoundException)
        {
            erro = ex.Message;
        }
        return Estado(nivel, erro);
    }

    /// <summary>Grava um valor vindo da tela: número com vírgula ou ponto, bitolas como texto do catálogo.</summary>
    private void Definir(NivelConfiguracao nivel, string id, string valor, string justificativa)
    {
        var c = CatalogoConfiguracao.Campo(id);
        valor = (valor ?? "").Trim();
        if (c.Tipo == TipoCampo.Numero)
        {
            if (!double.TryParse(valor.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double x))
                throw new FormatException($"{c.Rotulo}: \"{valor}\" não é número.");
            valor = x.ToString("R", CultureInfo.InvariantCulture);
        }
        if (c.Tipo == TipoCampo.Bitolas) valor = CatalogoConfiguracao.TextoBitolas(CatalogoConfiguracao.LerBitolas(valor));
        // justificativa já dada continua valendo se o valor mudar dentro do mesmo nível
        if (justificativa == null && Cfg.Nivel(nivel).Justificativas.TryGetValue(id, out string antiga)) justificativa = antiga;
        Cfg.Definir(nivel, id, valor, justificativa);
        Alterado = true;
    }

    /// <summary>Estado do nível para a página: grupos, campos (valor em vigor, origem, cadeia de níveis), fora da norma.</summary>
    public string Estado(NivelConfiguracao nivel, string erro = null)
    {
        if (!ComProjeto) nivel = NivelConfiguracao.Escritorio;
        ConfiguracaoProjeto vista = Cfg.AteNivel(nivel), acima = Cfg.AcimaDe(nivel);
        var fora = vista.ForaDaNorma().ToDictionary(f => f.Campo.Id);
        var e = ValorJson.NovoObjeto();
        e.Definir("tipo", "estado");
        e.Definir("nivel", nivel == NivelConfiguracao.Escritorio ? "Escritorio" : "Projeto");
        e.Definir("contexto", Contexto);
        e.Definir("comProjeto", ComProjeto);
        e.Definir("alterado", Alterado);
        if (erro != null) e.Definir("erro", erro);

        var grupos = ValorJson.NovaLista(); e.Definir("grupos", grupos);
        foreach (var g in CatalogoConfiguracao.Grupos)
        {
            var o = ValorJson.NovoObjeto();
            o.Definir("id", g.Id); o.Definir("nome", g.Nome); o.Definir("descricao", g.Descricao);
            o.Definir("alterados", CatalogoConfiguracao.Campos.Count(c => c.Grupo == g.Id && Cfg.Nivel(nivel).Valores.ContainsKey(c.Id)));
            grupos.Adicionar(o);
        }

        var sistemas = ValorJson.NovaLista(); e.Definir("sistemas", sistemas);
        foreach (string s in Unidades.Sistemas.Keys) sistemas.Adicionar(ValorJson.De(s));

        var campos = ValorJson.NovaLista(); e.Definir("campos", campos);
        foreach (var c in CatalogoConfiguracao.Campos)
        {
            var o = ValorJson.NovoObjeto();
            o.Definir("id", c.Id); o.Definir("grupo", c.Grupo); o.Definir("subgrupo", c.Subgrupo);
            o.Definir("rotulo", c.Rotulo); o.Definir("explicacao", c.Explicacao); o.Definir("referencia", c.Referencia);
            o.Definir("tipo", c.Tipo.ToString()); o.Definir("unidade", c.Unidade);
            var ops = ValorJson.NovaLista(); o.Definir("opcoes", ops);
            foreach (string op in c.Opcoes) ops.Adicionar(ValorJson.De(op));
            if (c.Min.HasValue) o.Definir("min", c.Min.Value);
            if (c.Max.HasValue) o.Definir("max", c.Max.Value);
            var (valor, origem) = vista.Ler(c.Id);
            o.Definir("valor", valor);
            o.Definir("origem", NomeNivel(origem));
            o.Definir("herdado", acima.Texto(c.Id));
            o.Definir("alterado", origem == nivel);
            if (Cfg.Nivel(nivel).Justificativas.TryGetValue(c.Id, out string j)) o.Definir("justificativa", j);
            if (fora.TryGetValue(c.Id, out var f))
            {
                o.Definir("fora", true);
                o.Definir("justificado", f.Justificado);
                o.Definir("textoFora", f.Texto);
            }
            // de onde vem: padrão → escritório → projeto (o que cada nível grava)
            var cadeia = ValorJson.NovaLista(); o.Definir("cadeia", cadeia);
            void Elo(NivelConfiguracao n, string v)
            {
                var x = ValorJson.NovoObjeto();
                x.Definir("nivel", NomeNivel(n)); x.Definir("valor", v); x.Definir("vale", n == origem);
                cadeia.Adicionar(x);
            }
            Elo(NivelConfiguracao.Padrao, Cfg.AteNivel(NivelConfiguracao.Padrao).Texto(c.Id));
            if (Cfg.Escritorio.Valores.TryGetValue(c.Id, out string ve)) Elo(NivelConfiguracao.Escritorio, ve);
            if (nivel >= NivelConfiguracao.Projeto && Cfg.Projeto.Valores.TryGetValue(c.Id, out string vp)) Elo(NivelConfiguracao.Projeto, vp);
            campos.Adicionar(o);
        }

        var pend = ValorJson.NovaLista(); e.Definir("pendencias", pend);
        foreach (var f in fora.Values.Where(f => !f.Justificado)) pend.Adicionar(ValorJson.De(f.Texto));
        e.Definir("jsonNivel", Cfg.Nivel(nivel).ParaJson());
        return Json.Gravar(e, indentado: false);
    }
}

/// <summary>Nível do escritório em arquivo (D45): %AppData%\PCEB\padroes\escritorio.json, o mesmo para o Revit e o CAD.</summary>
public static class ArquivoEscritorio
{
    public static string Caminho => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCEB", "padroes", "escritorio.json");

    /// <summary>Lê o nível do escritório; arquivo ausente = vazio; arquivo com erro = vazio e o aviso.</summary>
    public static NivelValores Ler(out string aviso, string caminho = null)
    {
        aviso = null;
        caminho ??= Caminho;
        if (!File.Exists(caminho)) return new NivelValores { Nivel = NivelConfiguracao.Escritorio };
        try
        {
            var n = NivelValores.DeJson(File.ReadAllText(caminho, Encoding.UTF8));
            n.Nivel = NivelConfiguracao.Escritorio;
            Limpar(n, ref aviso);
            return n;
        }
        catch (Exception ex)
        {
            aviso = $"Configuração do escritório não lida ({caminho}): {ex.Message}. Valem os padrões do PCEB.";
            return new NivelValores { Nivel = NivelConfiguracao.Escritorio };
        }
    }

    public static void Gravar(NivelValores n, string caminho = null)
    {
        caminho ??= Caminho;
        Directory.CreateDirectory(Path.GetDirectoryName(caminho));
        string tmp = caminho + ".tmp";
        File.WriteAllText(tmp, n.ParaJson(), new UTF8Encoding(false));
        if (File.Exists(caminho)) File.Replace(tmp, caminho, null); else File.Move(tmp, caminho);
    }

    /// <summary>Tira do nível os campos que o catálogo não conhece mais (versão antiga) e avisa.</summary>
    public static void Limpar(NivelValores n, ref string aviso)
    {
        var fora = n.Valores.Keys.Where(k => !CatalogoConfiguracao.Existe(k)).ToList();
        foreach (string k in fora) { n.Valores.Remove(k); n.Justificativas.Remove(k); }
        if (fora.Count > 0) aviso = (aviso == null ? "" : aviso + " ") + $"Campos sem uso ignorados: {string.Join(", ", fora)}.";
    }
}
