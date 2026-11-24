using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DEB.Core.Configuracao.Projeto;
using DEB.Core.Persistencia;

namespace DEB.Core.Elementos.Contencao;

/// <summary>
/// Janela do muro de arrimo (D47): a página (<see cref="Pagina"/>, a mesma no Revit e no CAD) mostra a seção com o empuxo, as
/// forças, as tensões na base e as armaduras, e os dados editáveis. A página manda <c>{"tipo":"calcular","dados":{…}}</c>; o
/// hospedeiro passa a mensagem a <see cref="Processar"/> e devolve a resposta à página. "aplicar" encerra com os dados aceitos.
/// </summary>
public sealed class SessaoMuro
{
    private readonly ConfiguracaoProjeto _cfg;
    public DadosMuro Dados { get; private set; }
    public ResultadoMuro Resultado { get; private set; }
    public string Titulo { get; }
    /// <summary>A página pediu para aplicar (gravar no elemento do hospedeiro) os dados atuais.</summary>
    public bool Aplicar { get; private set; }

    public SessaoMuro(DadosMuro dados, ConfiguracaoProjeto cfg, string titulo = "")
    {
        _cfg = cfg ?? new ConfiguracaoProjeto();
        Dados = dados ?? DadosMuro.DaConfiguracao(_cfg);
        Titulo = titulo ?? "";
        Resultado = CalculoMuro.Calcular(Dados, _cfg);
    }

    public static string Pagina()
    {
        using Stream s = typeof(SessaoMuro).GetTypeInfo().Assembly.GetManifestResourceStream("DEB.Core.TelaMuro.html");
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    /// <summary>A página com o estado atual embutido (sem hospedeiro: prévia em navegador e captura de tela).</summary>
    public string PaginaComDados() => Pagina().Replace("/*DADOS*/null", Estado());

    /// <summary>Trata uma mensagem da página e devolve o JSON para ela (vazio = nada a responder).</summary>
    public string Processar(string mensagem)
    {
        ValorJson m;
        try { m = Json.Ler(mensagem ?? ""); } catch (ErroJson) { return ""; }
        string tipo = m?["tipo"]?.Texto ?? "";
        switch (tipo)
        {
            case "pronto": return Estado();
            case "calcular":
                Dados = LerDados(m["dados"], Dados);
                Resultado = CalculoMuro.Calcular(Dados, _cfg);
                return Estado();
            case "aplicar":
                if (m.Tem("dados")) { Dados = LerDados(m["dados"], Dados); Resultado = CalculoMuro.Calcular(Dados, _cfg); }
                Aplicar = true;
                return "";
            default: return "";
        }
    }

    // ---------------------------------------------------------------- campos editáveis (a página monta o formulário daqui)

    private sealed class Campo
    {
        public string Id, Grupo, Rotulo, Unidade, Dica;
        public Func<DadosMuro, double> Ler;
        public Action<DadosMuro, double> Gravar;
        public double Min, Max, Passo;
    }

    private static readonly Campo[] Campos =
    {
        C("h", "Geometria", "Altura do fuste", "m", d => d.AlturaFuste, (d, v) => d.AlturaFuste = v, 0.5, 12, 0.05, "Do topo da sapata ao topo do muro."),
        C("tt", "Geometria", "Espessura no topo", "m", d => d.EspessuraTopo, (d, v) => d.EspessuraTopo = v, 0.12, 1.5, 0.01),
        C("tb", "Geometria", "Espessura na base", "m", d => d.EspessuraBase, (d, v) => d.EspessuraBase = v, 0.12, 2, 0.01, "Face de trás vertical; a da frente inclina."),
        C("ponta", "Geometria", "Ponta", "m", d => d.Ponta, (d, v) => d.Ponta = v, 0, 5, 0.05),
        C("talao", "Geometria", "Talão", "m", d => d.Talao, (d, v) => d.Talao = v, 0, 8, 0.05),
        C("es", "Geometria", "Espessura da sapata", "m", d => d.EspessuraSapata, (d, v) => d.EspessuraSapata = v, 0.2, 2, 0.05),
        C("df", "Geometria", "Terreno na frente (da base)", "m", d => d.TerrenoFrente, (d, v) => d.TerrenoFrente = v, 0, 5, 0.05),
        C("denteH", "Dente", "Altura do dente", "m", d => d.DenteAltura, (d, v) => d.DenteAltura = v, 0, 2, 0.05, "0 = sem dente."),
        C("denteB", "Dente", "Largura do dente", "m", d => d.DenteLargura, (d, v) => d.DenteLargura = v, 0.15, 1.5, 0.05),
        C("denteX", "Dente", "Início do dente (da ponta)", "m", d => d.DenteInicio, (d, v) => d.DenteInicio = v, 0, 10, 0.05),
        C("gama", "Aterro", "γ", "kN/m³", d => d.GamaSolo, (d, v) => d.GamaSolo = v, 12, 24, 0.5),
        C("gamaSat", "Aterro", "γsat", "kN/m³", d => d.GamaSat, (d, v) => d.GamaSat = v, 14, 25, 0.5),
        C("phi", "Aterro", "φ", "°", d => d.PhiSolo, (d, v) => d.PhiSolo = v, 15, 45, 1),
        C("beta", "Aterro", "Inclinação do aterro β", "°", d => d.Beta, (d, v) => d.Beta = v, 0, 40, 1),
        C("delta", "Aterro", "Atrito solo-muro δ (Coulomb)", "°", d => d.Delta, (d, v) => d.Delta = v, 0, 35, 1),
        C("q", "Aterro", "Sobrecarga", "kN/m²", d => d.Sobrecarga, (d, v) => d.Sobrecarga = v, 0, 100, 1),
        C("na", "Aterro", "Nível d'água (da base)", "m", d => d.NivelAgua, (d, v) => d.NivelAgua = v, 0, 15, 0.05, "0 = drenado."),
        C("gamaF", "Fundação", "γ", "kN/m³", d => d.GamaFundacao, (d, v) => d.GamaFundacao = v, 12, 24, 0.5),
        C("phiF", "Fundação", "φ", "°", d => d.PhiFundacao, (d, v) => d.PhiFundacao = v, 0, 45, 1),
        C("cF", "Fundação", "Coesão c", "kPa", d => d.CoesaoFundacao, (d, v) => d.CoesaoFundacao = v, 0, 200, 1),
        C("sAdm", "Fundação", "σadm (0 = só calcular)", "kPa", d => d.TensaoAdmissivel, (d, v) => d.TensaoAdmissivel = v, 0, 2000, 5),
        C("fPas", "Fundação", "Fração do passivo na frente", "", d => d.FracaoPassivo, (d, v) => d.FracaoPassivo = v, 0, 1, 0.05),
    };

    private static Campo C(string id, string g, string rot, string un, Func<DadosMuro, double> ler, Action<DadosMuro, double> gravar,
                           double min, double max, double passo, string dica = "") =>
        new Campo { Id = id, Grupo = g, Rotulo = rot, Unidade = un, Ler = ler, Gravar = gravar, Min = min, Max = max, Passo = passo, Dica = dica };

    private static DadosMuro LerDados(ValorJson j, DadosMuro base0)
    {
        var d = Clonar(base0);
        if (j == null || !j.EhObjeto) return d;
        foreach (var c in Campos)
        {
            var v = j[c.Id];
            if (v != null && v.Tipo == TipoJson.Numero && !double.IsNaN(v.Numero)) c.Gravar(d, v.Numero);
        }
        if (j["metodo"]?.Texto is string met) d.Metodo = met == "Coulomb" ? MetodoEmpuxo.Coulomb : MetodoEmpuxo.Rankine;
        return d;
    }

    /// <summary>Dados do muro em JSON (os mesmos ids da página), para guardar no elemento do hospedeiro.</summary>
    public static string DadosParaJson(DadosMuro d)
    {
        var o = ValorJson.NovoObjeto();
        foreach (var c in Campos) o.Definir(c.Id, c.Ler(d));
        o.Definir("metodo", d.Metodo.ToString());
        o.Definir("fracaoDente", d.FracaoDente);
        return Json.Gravar(o, indentado: false);
    }

    /// <summary>Lê o JSON de <see cref="DadosParaJson"/> sobre os valores de partida (campo ausente fica como está).</summary>
    public static DadosMuro DadosDeJson(string json, DadosMuro partida)
    {
        if (!Json.TentarLer(json ?? "", out ValorJson j, out _)) return Clonar(partida);
        var d = LerDados(j, partida);
        if (j["fracaoDente"] is ValorJson f && f.Tipo == TipoJson.Numero) d.FracaoDente = f.Numero;
        return d;
    }

    private static DadosMuro Clonar(DadosMuro a)
    {
        var d = new DadosMuro { Metodo = a.Metodo, FracaoDente = a.FracaoDente };
        foreach (var c in Campos) c.Gravar(d, c.Ler(a));
        return d;
    }

    // ---------------------------------------------------------------- estado para a página

    public string Estado()
    {
        var d = Dados; var r = Resultado;
        var raiz = ValorJson.NovoObjeto();
        raiz.Definir("tipo", "muro");
        raiz.Definir("titulo", Titulo);
        var campos = ValorJson.NovaLista(); raiz.Definir("campos", campos);
        foreach (var c in Campos)
        {
            var o = ValorJson.NovoObjeto(); campos.Adicionar(o);
            o.Definir("id", c.Id); o.Definir("grupo", c.Grupo); o.Definir("rotulo", c.Rotulo); o.Definir("unidade", c.Unidade);
            o.Definir("valor", c.Ler(d)); o.Definir("min", c.Min); o.Definir("max", c.Max); o.Definir("passo", c.Passo); o.Definir("dica", c.Dica);
        }
        raiz.Definir("metodo", d.Metodo.ToString());
        // geometria derivada para o desenho
        var g = ValorJson.NovoObjeto(); raiz.Definir("geo", g);
        g.Definir("B", d.Base); g.Definir("H", d.AlturaTotal); g.Definir("xf", d.Ponta); g.Definir("xt", d.Ponta + d.EspessuraBase);

        var res = ValorJson.NovoObjeto(); raiz.Definir("res", res);
        res.Definir("ka", r.Ka); res.Definir("kp", r.Kp);
        res.Definir("fsT", Fin(r.FsTombamento)); res.Definir("fsD", Fin(r.FsDeslizamento)); res.Definir("fsC", Fin(r.FsCapacidade));
        res.Definir("e", r.Excentricidade); res.Definir("sMax", r.SigmaMax); res.Definir("sMin", r.SigmaMin); res.Definir("bc", r.BaseComprimida);
        res.Definir("n", r.NormalBase); res.Definir("hb", r.HorizontalBase); res.Definir("qu", r.CapacidadeCarga);
        res.Definir("fsTLim", _cfg.Numero("solo.fsTombamento")); res.Definir("fsDLim", _cfg.Numero("solo.fsDeslizamento")); res.Definir("fsCLim", _cfg.Numero("solo.fsCapacidade"));
        res.Definir("situacao", r.Situacao.ToString());

        var fs = ValorJson.NovaLista(); raiz.Definir("forcas", fs);
        foreach (var f in r.Forcas)
        {
            var o = ValorJson.NovoObjeto(); fs.Adicionar(o);
            o.Definir("nome", f.Nome); o.Definir("tipo", f.Tipo); o.Definir("fx", f.Fx); o.Definir("fy", f.Fy); o.Definir("x", f.X); o.Definir("y", f.Y); o.Definir("variavel", f.Variavel);
        }
        var emp = ValorJson.NovaLista(); raiz.Definir("empuxo", emp);
        foreach (var (y, ps, pw) in r.DiagramaEmpuxo) emp.Adicionar(Lista(y, ps, pw));
        var fu = ValorJson.NovaLista(); raiz.Definir("fuste", fu);
        foreach (var (y, p, v, m) in r.DiagramaFuste) fu.Adicionar(Lista(y, p, v, m));
        var ps2 = ValorJson.NovaLista(); raiz.Definir("partes", ps2);
        foreach (var p in r.Partes)
        {
            var o = ValorJson.NovoObjeto(); ps2.Adicionar(o);
            o.Definir("nome", p.Nome); o.Definir("face", p.Face); o.Definir("h", p.Espessura); o.Definir("d", p.D); o.Definir("mk", p.Mk); o.Definir("md", p.Md);
            o.Definir("vd", p.Vd); o.Definir("vrd1", p.VRd1); o.Definir("as", p.As); o.Definir("asMin", p.AsMin); o.Definir("arranjo", p.Arranjo);
            o.Definir("dist", p.ArranjoDistribuicao); o.Definir("xd", p.XsobreD);
        }
        var vs = ValorJson.NovaLista(); raiz.Definir("verificacoes", vs);
        foreach (var v in r.Verificacoes)
        {
            var o = ValorJson.NovoObjeto(); vs.Adicionar(o);
            o.Definir("codigo", v.Codigo); o.Definir("grupo", v.Grupo); o.Definir("descricao", v.Descricao); o.Definir("valor", v.Valor);
            o.Definir("limite", v.Limite); o.Definir("situacao", v.Situacao.ToString()); o.Definir("referencia", v.Referencia); o.Definir("dica", v.Dica);
        }
        var mem = ValorJson.NovaLista(); raiz.Definir("memoria", mem);
        foreach (var p in r.Memoria)
        {
            var o = ValorJson.NovoObjeto(); mem.Adicionar(o);
            o.Definir("titulo", p.Titulo);
            var ls = ValorJson.NovaLista(); o.Definir("linhas", ls);
            foreach (string l in p.Linhas) ls.Adicionar(ValorJson.De(l));
        }
        return Json.Gravar(raiz, indentado: false);
    }

    private static double Fin(double x) => double.IsInfinity(x) || double.IsNaN(x) ? 99 : Math.Min(99, x);

    private static ValorJson Lista(params double[] v)
    {
        var l = ValorJson.NovaLista();
        foreach (double x in v) l.Adicionar(ValorJson.De(x));
        return l;
    }
}
