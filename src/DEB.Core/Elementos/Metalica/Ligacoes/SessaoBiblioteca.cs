using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DEB.Core.Persistencia;

namespace DEB.Core.Elementos.Metalica.Ligacoes;

/// <summary>
/// Janela da biblioteca de ligações (D49): a página (<see cref="Pagina"/>, a mesma no Revit e no CAD) mostra os modelos em vigor e
/// edita os do escritório. Editar um modelo do PCEB cria a versão do escritório com o mesmo id (o do PCEB fica como estava e volta
/// com "restaurar"). A sessão devolve também a geometria resolvida para um perfil de referência, para o desenho da ligação.
/// </summary>
public sealed class SessaoBiblioteca
{
    public BibliotecaLigacoes Biblioteca { get; }
    public bool Alterado { get; private set; }
    /// <summary>"salvar" ou "cancelar" depois que a página pediu para fechar.</summary>
    public string Fim { get; private set; }
    public string Selecionado { get; private set; } = "";
    /// <summary>Perfis de referência do desenho (viga e pilar).</summary>
    public string PerfilViga { get; private set; } = "W310X38.7";
    public string PerfilPilar { get; private set; } = "W250X32.7";

    public SessaoBiblioteca(BibliotecaLigacoes b)
    {
        Biblioteca = b;
        Selecionado = b.Todos.FirstOrDefault()?.Id ?? "";
    }

    public static string Pagina()
    {
        using Stream s = typeof(SessaoBiblioteca).GetTypeInfo().Assembly.GetManifestResourceStream("DEB.Core.TelaBibliotecaLigacoes.html");
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    public string PaginaComDados() => Pagina().Replace("/*DADOS*/null", Estado(null));

    /// <summary>Ações: estado, selecionar(id), definir(id, param, valor), novo(tipo), duplicar(id), excluir(id), restaurar(id),
    /// renomear(id, nome, descricao), perfil(viga, pilar), salvar, cancelar.</summary>
    public string Receber(string mensagem)
    {
        string erro = null;
        try
        {
            ValorJson m = Json.Ler(mensagem ?? "");
            string acao = m["acao"]?.Texto ?? "estado", id = m["id"]?.Texto ?? Selecionado;
            switch (acao)
            {
                case "estado": break;
                case "selecionar": if (Biblioteca.Buscar(id) != null) Selecionado = id; break;
                case "definir":
                {
                    var mod = Editavel(id);
                    string p = m["param"]?.Texto ?? "", v = (m["valor"]?.Texto ?? "").Trim().Replace(',', '.');
                    EsquemasLigacao.Parametro(mod.Tipo, p);   // existe?
                    mod.Valores[p] = v;
                    Alterado = true;
                    break;
                }
                case "renomear":
                {
                    var mod = Editavel(id);
                    if (m["nome"]?.Texto is string n && n.Trim() != "") mod.Nome = n.Trim();
                    if (m["descricao"]?.Texto is string d) mod.Descricao = d.Trim();
                    Alterado = true;
                    break;
                }
                case "novo":
                {
                    if (!Enum.TryParse(m["tipo"]?.Texto ?? "", out TipoLigacao t)) throw new ArgumentException("tipo desconhecido");
                    var mod = new ModeloLigacao { Id = NovoId(t), Nome = "Nova — " + EsquemasLigacao.NomeTipo(t), Tipo = t, Origem = OrigemLigacao.Escritorio };
                    Biblioteca.Escritorio.Add(mod);
                    Selecionado = mod.Id; Alterado = true;
                    break;
                }
                case "duplicar":
                {
                    var o = Biblioteca.Buscar(id) ?? throw new ArgumentException("modelo não encontrado");
                    var c = o.Copia(OrigemLigacao.Escritorio);
                    c.Id = NovoId(o.Tipo); c.Nome = o.Nome + " (cópia)";
                    Biblioteca.Escritorio.Add(c);
                    Selecionado = c.Id; Alterado = true;
                    break;
                }
                case "excluir":
                case "restaurar":
                {
                    int n = Biblioteca.Escritorio.RemoveAll(x => x.Id == id);
                    if (n == 0) throw new ArgumentException(Biblioteca.Pceb.Any(x => x.Id == id)
                        ? "As ligações do PCEB não são apagadas: edite para criar a versão do escritório."
                        : "Só as ligações do escritório podem ser apagadas aqui.");
                    if (Biblioteca.Buscar(id) == null) Selecionado = Biblioteca.Todos.FirstOrDefault()?.Id ?? "";
                    Alterado = true;
                    break;
                }
                case "perfil":
                    if (PerfilI.Buscar(m["viga"]?.Texto ?? "") is PerfilI pv) PerfilViga = pv.Nome;
                    if (PerfilI.Buscar(m["pilar"]?.Texto ?? "") is PerfilI pp) PerfilPilar = pp.Nome;
                    break;
                case "salvar": Fim = "salvar"; break;
                case "cancelar": Fim = "cancelar"; break;
                default: erro = "Ação desconhecida: " + acao; break;
            }
        }
        catch (Exception ex) { erro = ex.Message; }
        return Estado(erro);
    }

    /// <summary>O modelo do escritório com esse id (criado a partir do do PCEB na primeira edição).</summary>
    private ModeloLigacao Editavel(string id)
    {
        var e = Biblioteca.Escritorio.FirstOrDefault(x => x.Id == id);
        if (e != null) return e;
        var o = Biblioteca.Buscar(id) ?? throw new ArgumentException("modelo não encontrado");
        e = o.Copia(OrigemLigacao.Escritorio);
        Biblioteca.Escritorio.Add(e);
        return e;
    }

    private string NovoId(TipoLigacao t)
    {
        string pre = t switch { TipoLigacao.ChapaAlma => "ESC-CA-", TipoLigacao.CantoneiraDupla => "ESC-CD-", TipoLigacao.ChapaTopo => "ESC-CT-", _ => "ESC-PB-" };
        int k = 1;
        while (Biblioteca.Buscar(pre + k) != null) k++;
        return pre + k;
    }

    // ---------------------------------------------------------------- estado

    public string Estado(string erro)
    {
        var raiz = ValorJson.NovoObjeto();
        raiz.Definir("tipo", "biblioteca");
        if (erro != null) raiz.Definir("erro", erro);
        raiz.Definir("alterado", Alterado);
        raiz.Definir("selecionado", Selecionado);
        raiz.Definir("perfilViga", PerfilViga); raiz.Definir("perfilPilar", PerfilPilar);
        raiz.Definir("arquivo", BibliotecaLigacoes.CaminhoEscritorio);
        var tipos = ValorJson.NovaLista(); raiz.Definir("tipos", tipos);
        foreach (TipoLigacao t in Enum.GetValues(typeof(TipoLigacao)))
        {
            var o = ValorJson.NovoObjeto(); tipos.Adicionar(o);
            o.Definir("id", t.ToString()); o.Definir("nome", EsquemasLigacao.NomeTipo(t));
            var ps = ValorJson.NovaLista(); o.Definir("parametros", ps);
            foreach (var p in EsquemasLigacao.Do(t))
            {
                var q = ValorJson.NovoObjeto(); ps.Adicionar(q);
                q.Definir("id", p.Id); q.Definir("rotulo", p.Rotulo); q.Definir("grupo", p.Grupo); q.Definir("unidade", p.Unidade); q.Definir("padrao", p.Padrao);
                q.Definir("auto", p.PodeAuto); q.Definir("explicacao", p.Explicacao);
                if (p.Min.HasValue) q.Definir("min", p.Min.Value);
                if (p.Max.HasValue) q.Definir("max", p.Max.Value);
                var ops = ValorJson.NovaLista(); q.Definir("opcoes", ops);
                foreach (string op in p.Opcoes) ops.Adicionar(ValorJson.De(op));
            }
        }
        var lista = ValorJson.NovaLista(); raiz.Definir("modelos", lista);
        foreach (var m in Biblioteca.Todos)
        {
            var o = ValorJson.NovoObjeto(); lista.Adicionar(o);
            o.Definir("id", m.Id); o.Definir("nome", m.Nome); o.Definir("tipo", m.Tipo.ToString()); o.Definir("descricao", m.Descricao);
            o.Definir("origem", m.Origem.ToString());
            o.Definir("temPceb", Biblioteca.Pceb.Any(x => x.Id == m.Id));
            var vs = ValorJson.NovoObjeto(); o.Definir("valores", vs);
            foreach (var p in EsquemasLigacao.Do(m.Tipo)) vs.Definir(p.Id, m.Texto(p.Id));
            var er = ValorJson.NovaLista(); o.Definir("erros", er);
            foreach (string x in m.Erros()) er.Adicionar(ValorJson.De(x));
        }
        var sel = Biblioteca.Buscar(Selecionado);
        if (sel != null && sel.Erros().Count == 0)
        {
            try { raiz.Definir("desenho", Desenho(sel)); }
            catch (Exception ex) { raiz.Definir("erroDesenho", ex.Message); }
        }
        return Json.Gravar(raiz, indentado: false);
    }

    /// <summary>Geometria resolvida (mm) para o desenho, com os perfis de referência.</summary>
    private ValorJson Desenho(ModeloLigacao m)
    {
        var o = ValorJson.NovoObjeto();
        var viga = PerfilI.Buscar(PerfilViga); var pilar = PerfilI.Buscar(PerfilPilar);
        var aco = AcoEstrutural.Buscar("A572");
        ValorJson Perfil(PerfilI p)
        {
            var q = ValorJson.NovoObjeto(); q.Definir("nome", p.Nome); q.Definir("d", p.D); q.Definir("bf", p.Bf); q.Definir("tf", p.Tf); q.Definir("tw", p.Tw); return q;
        }
        ValorJson L(params double[] v) { var l = ValorJson.NovaLista(); foreach (double x in v) l.Adicionar(ValorJson.De(x)); return l; }
        o.Definir("viga", Perfil(viga)); o.Definir("pilar", Perfil(pilar));
        // sólidos da ligação (D50, item 4): [papel, x0, x1, y0, y1, z0, z1, rótulo] em mm
        var cx = ValorJson.NovaLista(); o.Definir("caixas", cx);
        foreach (var c in GeometriaLigacao.Montar(m, viga, pilar))
        {
            var q = ValorJson.NovaLista();
            q.Adicionar(ValorJson.De(c.Papel.ToString()));
            foreach (double v in new[] { c.X0, c.X1, c.Y0, c.Y1, c.Z0, c.Z1 }) q.Adicionar(ValorJson.De(Math.Round(v, 2)));
            q.Adicionar(ValorJson.De(c.Rotulo));
            cx.Adicionar(q);
        }
        switch (m.Tipo)
        {
            case TipoLigacao.ChapaAlma:
            {
                var d = DadosChapaAlma.DoModelo(m, viga, aco);
                o.Definir("hp", d.AlturaChapa); o.Definir("bp", d.A + d.EhChapa); o.Definir("tp", d.Tp); o.Definir("a", d.A); o.Definir("db", d.Db);
                o.Definir("ys", L(Enumerable.Range(0, d.N).Select(i => (i - (d.N - 1) / 2.0) * d.S).ToArray()));
                o.Definir("perna", d.Perna);
                o.Definir("texto", $"{d.N} parafusos ø{Fmt.Virgula(d.Db, "0.#")} · chapa {Fmt.Virgula(d.Tp, "0.#")} × {Fmt.Virgula(d.AlturaChapa, "0")} mm · filete {Fmt.Virgula(d.Perna, "0")} mm");
                break;
            }
            case TipoLigacao.CantoneiraDupla:
            {
                var d = DadosCantoneira.DoModelo(m, viga, aco);
                o.Definir("hp", d.Comprimento); o.Definir("aba", d.Aba); o.Definir("t", d.T); o.Definir("eh", d.Eh); o.Definir("db", d.Db);
                o.Definir("ys", L(Enumerable.Range(0, d.N).Select(i => (i - (d.N - 1) / 2.0) * d.S).ToArray()));
                o.Definir("soldada", d.SoldadaNoSuporte);
                o.Definir("texto", $"2 L {Fmt.Virgula(d.Aba, "0")} × {Fmt.Virgula(d.T, "0.#")} × {Fmt.Virgula(d.Comprimento, "0")} mm · {d.N} parafusos ø{Fmt.Virgula(d.Db, "0.#")} na alma" +
                                   (d.SoldadaNoSuporte ? " · soldadas no suporte" : $" · {2 * d.N} no suporte"));
                break;
            }
            case TipoLigacao.ChapaTopo:
            {
                var d = DadosChapaTopo.DoModelo(m, viga);
                // linhas de parafusos (y a partir da face superior da viga, para baixo positivo)
                var ys = new List<double>();
                if (d.Estendida) ys.Add(-d.Pf);
                for (int k = 0; k < d.LinhasInternas; k++) ys.Add(viga.Tf + d.Pf + k * 3 * d.Db);
                ys.Add(viga.D - viga.Tf - d.Pf);
                double topo = d.Estendida ? -(d.Pf + Math.Max(1.5 * d.Db, 40)) : -10, base0 = viga.D + 10;
                o.Definir("ys", L(ys.ToArray())); o.Definir("g", d.G); o.Definir("bp", d.BpEf); o.Definir("db", d.Db);
                o.Definir("topo", topo); o.Definir("base", base0); o.Definir("tp", d.Tp);
                o.Definir("texto", $"{d.NTotal} parafusos ø{Fmt.Virgula(d.Db, "0.#")} (g = {Fmt.Virgula(d.G, "0")} mm) · chapa {Fmt.Virgula(d.BpEf, "0")} mm de largura, " +
                                   (d.Tp > 0 ? $"{Fmt.Virgula(d.Tp, "0.#")} mm" : "espessura pelo cálculo") + (d.Estendida ? " · estendida" : ""));
                break;
            }
            default:
            {
                var d = DadosPlacaBase.DoModelo(m, pilar, 1.4);
                var (a1, a2, _, _) = d.Tabela18(out _);
                var pts = new List<double>();
                int porLado = d.Nb / 2;
                for (int lado = -1; lado <= 1; lado += 2)
                    for (int k = 0; k < porLado; k++)
                    { pts.Add(lado * d.A); pts.Add((k - (porLado - 1) / 2.0) * (porLado > 1 ? (d.LyEf - 2 * a1) / (porLado - 1) : 0)); }
                o.Definir("lx", d.LxEf); o.Definir("ly", d.LyEf); o.Definir("db", d.Db); o.Definir("chumb", L(pts.ToArray())); o.Definir("tp", d.Tp);
                o.Definir("texto", $"placa {Fmt.Virgula(d.LxEf, "0")} × {Fmt.Virgula(d.LyEf, "0")} mm, " + (d.Tp > 0 ? $"{Fmt.Virgula(d.Tp, "0.#")} mm" : "espessura pelo cálculo") +
                                   $" · {d.Nb} chumbadores ø{Fmt.Virgula(d.Db, "0")} {d.AcoChumbador} (a1 = {Fmt.Virgula(a1, "0")} mm)");
                break;
            }
        }
        return o;
    }
}
