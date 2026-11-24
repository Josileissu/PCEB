using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DEB.Core.Analise.Barras;
using DEB.Core.Persistencia;

namespace DEB.Core.Analise.Objetos;

/// <summary>
/// Dados da janela do modelo de análise (D46: o modelo de análise é do PCEB, fora do Revit — decisão de 24/11): a página
/// (<see cref="Pagina"/>, a mesma no Revit e no CAD) recebe o modelo e os resultados em JSON e desenha o 3D das barras por dono,
/// os apoios, as ligações, as cargas, a deformada, os diagramas e o mapa colorido de um esforço. Unidades do JSON: cm, kN, kN·cm.
/// </summary>
public static class VisualizacaoAnalise
{
    /// <summary>Estações por barra para os diagramas: [x, N, Vy, Vz, T, My, Mz, dx, dy, dz] (deslocamento global, cm).</summary>
    public const int Estacoes = EstruturaObjetos.NumEstacoes;

    public static string Pagina()
    {
        using Stream s = typeof(VisualizacaoAnalise).GetTypeInfo().Assembly.GetManifestResourceStream("DEB.Core.TelaAnalise.html");
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    private static ValorJson Lista(IEnumerable<double> v)
    {
        var l = ValorJson.NovaLista();
        foreach (double x in v) l.Adicionar(ValorJson.De(Math.Round(x, 6)));
        return l;
    }

    private static ValorJson Vet(Vetor3 v) => Lista(new[] { v.X, v.Y, v.Z });

    /// <summary>JSON completo para a página: modelo, cargas e resultados (casos e combinações).</summary>
    public static string Json(EstruturaObjetos e, string titulo = "", Configuracao.Projeto.ConfiguracaoProjeto cfg = null)
    {
        ModeloBarras m = e.Modelo;
        var raiz = ValorJson.NovoObjeto();
        raiz.Definir("tipo", "modelo");
        raiz.Definir("titulo", titulo);

        var nos = ValorJson.NovaLista(); raiz.Definir("nos", nos);
        foreach (var n in m.Nos)
        {
            var o = ValorJson.NovoObjeto();
            o.Definir("x", n.X); o.Definir("y", n.Y); o.Definir("z", n.Z); o.Definir("nome", n.Nome);
            nos.Adicionar(o);
        }

        // donos (objetos): pilares, vigas, ligações
        var donos = ValorJson.NovaLista(); raiz.Definir("objetos", donos);
        void Obj(Dono d, string resumo)
        {
            var o = ValorJson.NovoObjeto();
            o.Definir("chave", Chave(d)); o.Definir("tipo", d.Tipo.ToString()); o.Definir("nome", d.ToString()); o.Definir("resumo", resumo);
            donos.Adicionar(o);
        }
        foreach (var p in e.Pilares) Obj(p.Dono, $"seção {Fmt.Virgula(p.Secao.B, "0.#")}×{Fmt.Virgula(p.Secao.H, "0.#")} cm, {Fmt.Virgula(p.Comprimento, "0")} cm, engaste a {Fmt.Virgula(p.Embutimento, "0")} cm da base");
        foreach (var v in e.Vigas) Obj(v.Dono, $"seção {Fmt.Virgula(v.Secao.B, "0.#")}×{Fmt.Virgula(v.Secao.H, "0.#")} cm, g = {Fmt.Virgula(v.G, "0.##")} kN/m, q = {Fmt.Virgula(v.Q, "0.##")} kN/m");
        foreach (var l in e.Ligacoes) Obj(l.Dono, l.Observacao);
        foreach (var la in e.Lajes) Obj(la.Dono, $"h = {Fmt.Virgula(la.Espessura, "0.#")} cm, g = {Fmt.Virgula(la.G, "0.##")} kN/m², q = {Fmt.Virgula(la.Q, "0.##")} kN/m², malha {Fmt.Virgula(la.Sx, "0")} × {Fmt.Virgula(la.Sy, "0")} cm");

        // lajes: malha (nós por i, j) e contorno no sistema da laje
        var lajes = ValorJson.NovaLista(); raiz.Definir("lajes", lajes);
        foreach (var la in e.Lajes)
        {
            var o = ValorJson.NovoObjeto();
            o.Definir("chave", Chave(la.Dono)); o.Definir("nx", la.Nx); o.Definir("ny", la.Ny); o.Definir("sx", la.Sx); o.Definir("sy", la.Sy);
            o.Definir("origem", Vet(la.Origem)); o.Definir("ex", Vet(la.Ex)); o.Definir("ey", Vet(la.Ey));
            var nl = new List<double>();
            for (int i = 0; i <= la.Nx; i++) for (int j = 0; j <= la.Ny; j++) nl.Add(la.Nos[i, j]);
            o.Definir("nos", Lista(nl));
            lajes.Adicionar(o);
        }

        var barras = ValorJson.NovaLista(); raiz.Definir("barras", barras);
        foreach (var b in m.Barras)
        {
            var (x, y, z, L) = SolverBarras.Eixos(m, b);
            var o = ValorJson.NovoObjeto();
            o.Definir("i", b.NoI); o.Definir("j", b.NoJ); o.Definir("tipo", (b.Dono?.Tipo ?? TipoDono.Outro).ToString());
            o.Definir("dono", b.Dono == null ? "" : Chave(b.Dono)); o.Definir("nome", b.Nome); o.Definir("secao", b.Secao.Descricao);
            o.Definir("L", L); o.Definir("ex", Vet(x)); o.Definir("ey", Vet(y)); o.Definir("ez", Vet(z));
            o.Definir("A", b.Secao.A); o.Definir("Iy", b.Secao.Iy); o.Definir("Iz", b.Secao.Iz); o.Definir("J", b.Secao.J);
            o.Definir("rotI", b.LiberaI.Skip(3).Any(t => t)); o.Definir("rotJ", b.LiberaJ.Skip(3).Any(t => t));
            barras.Adicionar(o);
        }
        var vinc = ValorJson.NovaLista(); raiz.Definir("vinculos", vinc);
        foreach (var v in m.Vinculos)
        {
            var o = ValorJson.NovoObjeto();
            o.Definir("i", v.Mestre); o.Definir("j", v.Escravo); o.Definir("dono", v.Dono == null ? "" : Chave(v.Dono)); o.Definir("nome", v.Nome);
            vinc.Adicionar(o);
        }
        var molas = ValorJson.NovaLista(); raiz.Definir("molas", molas);
        foreach (var mo in m.Molas)
        {
            var o = ValorJson.NovoObjeto();
            o.Definir("i", mo.NoI); o.Definir("j", mo.NoJ); o.Definir("dono", mo.Dono == null ? "" : Chave(mo.Dono)); o.Definir("nome", mo.Nome);
            o.Definir("k", Lista(mo.K));
            molas.Adicionar(o);
        }
        var apoios = ValorJson.NovaLista(); raiz.Definir("apoios", apoios);
        foreach (var a in m.Apoios)
        {
            var o = ValorJson.NovoObjeto();
            o.Definir("no", a.No);
            var t = ValorJson.NovaLista(); foreach (bool x in a.Travado) t.Adicionar(ValorJson.De(x));
            o.Definir("travado", t);
            apoios.Adicionar(o);
        }

        // cargas por caso (para desenhar): distribuídas e pontuais nas barras, nodais
        var cargas = ValorJson.NovoObjeto(); raiz.Definir("cargas", cargas);
        foreach (var c in m.Casos)
        {
            var l = ValorJson.NovaLista();
            if (c.PesoProprio)
                foreach (var b in m.Barras.Where(b => b.Material.Peso > 0))
                    l.Adicionar(CargaDist(b.Id, new Vetor3(0, 0, -b.Material.Peso * b.Secao.A), 0, 0, "peso próprio"));
            foreach (var d in c.Distribuidas) l.Adicionar(CargaDist(d.Barra, d.Q, d.A, d.B, ""));
            foreach (var p in c.Pontuais)
            {
                var o = ValorJson.NovoObjeto();
                o.Definir("tipo", "pontual"); o.Definir("barra", p.Barra); o.Definir("a", p.A); o.Definir("F", Vet(p.F)); o.Definir("local", p.Local);
                l.Adicionar(o);
            }
            foreach (var (no, f) in c.Nodais)
            {
                var o = ValorJson.NovoObjeto();
                o.Definir("tipo", "nodal"); o.Definir("no", no); o.Definir("F", Lista(f));
                l.Adicionar(o);
            }
            cargas.Definir(c.Nome, l);
        }

        var res = ValorJson.NovaLista(); raiz.Definir("resultados", res);
        void Resultado(string nome, string tipo, ResultadoCaso r, double[][][] est)
        {
            var o = ValorJson.NovoObjeto();
            o.Definir("nome", nome); o.Definir("tipo", tipo);
            var d = ValorJson.NovaLista(); foreach (var u in r.Deslocamentos) d.Adicionar(Lista(u)); o.Definir("desl", d);
            var rb = ValorJson.NovaLista();
            foreach (var be in est) { var lb = ValorJson.NovaLista(); foreach (var s in be) lb.Adicionar(Lista(s)); rb.Adicionar(lb); }
            o.Definir("estacoes", rb);
            var fm = ValorJson.NovaLista(); foreach (var f in r.ForcasMolas) fm.Adicionar(Lista(f)); o.Definir("molas", fm);
            var re = ValorJson.NovaLista();
            for (int i = 0; i < r.Reacoes.Length; i++)
                if (r.Reacoes[i] != null) { var x = ValorJson.NovoObjeto(); x.Definir("no", i); x.Definir("R", Lista(r.Reacoes[i])); re.Adicionar(x); }
            o.Definir("reacoes", re);
            var rl = ValorJson.NovaLista();
            foreach (var la in e.Lajes)
            {
                var (uz, mx, my, mt) = la.Resultados(r);
                var x = ValorJson.NovoObjeto();
                ValorJson Grade(double[,] g) { var l = ValorJson.NovaLista(); foreach (double v in g) l.Adicionar(double.IsNaN(v) ? ValorJson.Nulo : ValorJson.De(Math.Round(v, 5))); return l; }
                x.Definir("uz", Grade(uz)); x.Definir("mx", Grade(mx)); x.Definir("my", Grade(my)); x.Definir("mxy", Grade(mt));
                rl.Adicionar(x);
            }
            o.Definir("lajes", rl);
            res.Adicionar(o);
        }
        foreach (var c in m.Casos) Resultado(c.Nome, "caso", e.Casos[c.Nome], e.Estacoes(c.Nome));
        foreach (var def in e.DefinicaoCombinacoes) Resultado(def.Nome, def.Tipo, e.Combinacoes[def.Nome], e.Estacoes(def.Nome));

        // dimensionamento por objeto (D46)
        var dim = ValorJson.NovaLista(); raiz.Definir("dimensionamento", dim);
        foreach (var rp in Dimensionamento.DimensionamentoObjetos.Verificar(e, cfg))
        {
            var o = ValorJson.NovoObjeto();
            o.Definir("chave", rp.Chave); o.Definir("situacao", rp.Situacao.ToString());
            var rs = ValorJson.NovaLista(); foreach (string t in rp.Resumo) rs.Adicionar(ValorJson.De(t)); o.Definir("resumo", rs);
            var vs = ValorJson.NovaLista();
            foreach (var v in rp.Verificacoes)
            {
                var x = ValorJson.NovoObjeto();
                x.Definir("codigo", v.Codigo); x.Definir("descricao", v.Descricao); x.Definir("valor", v.Valor); x.Definir("limite", v.Limite);
                x.Definir("situacao", v.Situacao.ToString()); x.Definir("referencia", v.Referencia); x.Definir("dica", v.Dica ?? "");
                vs.Adicionar(x);
            }
            o.Definir("verificacoes", vs);
            var mem = ValorJson.NovoObjeto(); mem.Definir("titulo", rp.Memoria.Titulo ?? "");
            var ml = ValorJson.NovaLista(); foreach (string t in rp.Memoria.Linhas) ml.Adicionar(ValorJson.De(t)); mem.Definir("linhas", ml);
            o.Definir("memoria", mem);
            dim.Adicionar(o);
        }

        var av = ValorJson.NovaLista(); foreach (string a in e.Avisos.Distinct()) av.Adicionar(ValorJson.De(a));
        raiz.Definir("avisos", av);
        return Persistencia.Json.Gravar(raiz, indentado: false);
    }

    private static ValorJson CargaDist(int barra, Vetor3 q, double a, double b, string nome)
    {
        var o = ValorJson.NovoObjeto();
        o.Definir("tipo", "distribuida"); o.Definir("barra", barra); o.Definir("q", Vet(q)); o.Definir("a", a); o.Definir("b", b); o.Definir("nome", nome);
        return o;
    }

    private static string Chave(Dono d) => d.Tipo + ":" + (d.Id != "" ? d.Id : d.Nome);
}
