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
    public static string Json(EstruturaObjetos e, string titulo = "", Configuracao.Projeto.ConfiguracaoProjeto cfg = null,
                              Dictionary<string, Dictionary<string, string>> ajustes = null)
    {
        var objAjuste = AjustesAnalise.Objetos(e);
        ModeloBarras m = e.Modelo;
        var raiz = ValorJson.NovoObjeto();
        raiz.Definir("tipo", "modelo");
        raiz.Definir("titulo", titulo);
        // unidades da tela pela configuração do projeto (a página converte os valores internos em kN e cm)
        var un = new UnidadesAnalise(cfg);
        raiz.Definir("unid", un.Json());

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
            o.Definir("chave", Chave(d)); o.Definir("tipo", d.Tipo.ToString()); o.Definir("nome", d.ToString()); o.Definir("resumo", un.ConverterTexto(resumo));
            // ajustes da janela (D50): propriedades editáveis, valor em vigor e se foi ajustado
            if (objAjuste.TryGetValue(Chave(d), out object alvo))
            {
                var tc = ValorJson.NovaLista(); o.Definir("tiposCarga", tc);
                foreach (var t in CargasObjetos.Aceitas(alvo)) tc.Adicionar(ValorJson.De(t.ToString()));
                var cs = ValorJson.NovaLista(); o.Definir("cargas", cs);
                foreach (var c in e.Cargas.Where(x => x.Alvo == Chave(d)))
                {
                    var q = ValorJson.NovoObjeto(); cs.Adicionar(q);
                    q.Definir("id", c.Id); q.Definir("texto", c.Texto(un)); q.Definir("origem", c.Origem.ToString()); q.Definir("descricao", c.Descricao);
                }
                var aj = ValorJson.NovaLista(); o.Definir("ajustes", aj);
                Dictionary<string, string> feitos = null;
                ajustes?.TryGetValue(Chave(d), out feitos);
                var props = AjustesAnalise.Do(alvo).Select(p => (p.Id, p.Rotulo, p.Grupo, p.Unidade, p.Opcoes, p.Dica, Valor: p.Ler(alvo))).ToList();
                if (alvo is LigacaoMetalica lm)
                    props.Add(("modelo", "Modelo da biblioteca de ligações", "Ligação", "", AjustesAnalise.ModelosLigacao(e, lm), "", lm.Modelo?.Id ?? ""));
                foreach (var p in props)
                {
                    var q = ValorJson.NovoObjeto(); aj.Adicionar(q);
                    // número com unidade convertível: valor e símbolo na unidade da tela
                    string valorTela = p.Valor, unTela = p.Unidade;
                    if (un.Conversao(p.Unidade) != null && double.TryParse(p.Valor, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double vNum))
                    {
                        var (vt, st) = un.ParaTela(p.Unidade, vNum);
                        valorTela = Math.Round(vt, 6).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture); unTela = st;
                    }
                    q.Definir("id", p.Id); q.Definir("rotulo", p.Rotulo); q.Definir("grupo", p.Grupo); q.Definir("unidade", unTela); q.Definir("dica", p.Dica);
                    q.Definir("valor", valorTela); q.Definir("alterado", feitos != null && feitos.ContainsKey(p.Id));
                    var ops = ValorJson.NovaLista(); q.Definir("opcoes", ops);
                    foreach (string x in p.Opcoes) ops.Adicionar(ValorJson.De(x));
                }
            }
            donos.Adicionar(o);
        }
        foreach (var p in e.Pilares) Obj(p.Dono, $"seção {Fmt.Virgula(p.Secao.B, "0.#")}×{Fmt.Virgula(p.Secao.H, "0.#")} cm, {Fmt.Virgula(p.Comprimento, "0")} cm, engaste a {Fmt.Virgula(p.Embutimento, "0")} cm da base");
        foreach (var v in e.Vigas) Obj(v.Dono, $"seção {Fmt.Virgula(v.Secao.B, "0.#")}×{Fmt.Virgula(v.Secao.H, "0.#")} cm, g = {un.Texto("kN/m", v.G)}, q = {un.Texto("kN/m", v.Q)}");
        foreach (var b in e.Metalicas)
            Obj(b.Dono, $"{b.Perfil.Nome} ({b.Aco.Nome}), {Fmt.Virgula(b.Comprimento, "0")} cm" + (b.Pilar ? $", base {(b.Base == BasePilarMetalico.Engastada ? "engastada" : "rotulada")}"
                : $", ligações {(b.LigacaoI == LigacaoExtremidade.Flexivel ? "flexível" : "rígida")}/{(b.LigacaoJ == LigacaoExtremidade.Flexivel ? "flexível" : "rígida")}, g = {un.Texto("kN/m", b.G)}, q = {un.Texto("kN/m", b.Q)}"));
        foreach (var l in e.Ligacoes) Obj(l.Dono, l.Observacao);
        foreach (var l in e.LigacoesMetalicas)
            Obj(l.Dono, l.Placa != null
                ? $"placa de base {Fmt.Virgula(l.Placa.LxEf, "0")} × {Fmt.Virgula(l.Placa.LyEf, "0")} mm, {l.Placa.Nb} chumbadores ø{Fmt.Virgula(l.Placa.Db, "0")} ({l.Modelo?.Id})"
                : l.Topo != null ? $"chapa de topo, {l.Topo.NTotal} parafusos ø{Fmt.Virgula(l.Topo.Db, "0.#")} ({l.Modelo?.Id})"
                : l.Cantoneira != null ? $"cantoneira dupla L {Fmt.Virgula(l.Cantoneira.Aba, "0")} × {Fmt.Virgula(l.Cantoneira.T, "0.#")}, {l.Cantoneira.N} parafusos ø{Fmt.Virgula(l.Cantoneira.Db, "0.#")} ({l.Modelo?.Id})"
                : $"chapa de alma {Fmt.Virgula(l.Chapa.Tp, "0.#")} × {Fmt.Virgula(l.Chapa.AlturaChapa, "0")} mm, {l.Chapa.N} parafusos ø{Fmt.Virgula(l.Chapa.Db, "0.#")} {l.Chapa.Parafuso.Nome} ({l.Modelo?.Id})");
        // ligações de aço: os sólidos da ligação com os perfis das peças (o 2D da peça desenha as quatro vistas, como na biblioteca)
        var lacos = ValorJson.NovoObjeto(); raiz.Definir("ligacoesAco", lacos);
        foreach (var l in e.LigacoesMetalicas)
        {
            if (l.Modelo == null) continue;
            try
            {
                var viga = l.Viga?.Perfil ?? DEB.Core.Elementos.Metalica.PerfilI.Buscar("W310X38.7");
                var pil = l.Suporte?.Perfil ?? l.Pilar?.Perfil ?? DEB.Core.Elementos.Metalica.PerfilI.Buscar("W250X32.7");
                var cx = ValorJson.NovaLista();
                foreach (var c in DEB.Core.Elementos.Metalica.Ligacoes.GeometriaLigacao.Montar(l.Modelo, viga, pil))
                {
                    var q = ValorJson.NovaLista();
                    q.Adicionar(ValorJson.De(c.Papel.ToString()));
                    foreach (double v in new[] { c.X0, c.X1, c.Y0, c.Y1, c.Z0, c.Z1 }) q.Adicionar(ValorJson.De(Math.Round(v, 1)));
                    q.Adicionar(ValorJson.De(c.Rotulo));
                    cx.Adicionar(q);
                }
                var o = ValorJson.NovoObjeto(); o.Definir("caixas", cx); o.Definir("tipo", l.Tipo.ToString()); o.Definir("modelo", l.Modelo.Nome);
                // barra e ponta da peça que chega na ligação (esforços no nó, como no IDEA StatiCa): viga na ponta ligada; pilar na base
                var peca = l.Viga ?? l.Pilar;
                if (peca != null && peca.Barras.Count > 0)
                {
                    bool pi = l.Viga != null ? l.PontaI : true;
                    var bar = pi ? peca.Barras[0] : peca.Barras[peca.Barras.Count - 1];
                    o.Definir("barra", m.Barras.IndexOf(bar)); o.Definir("pontaI", pi); o.Definir("peca", peca.Dono.ToString());
                    if (l.Suporte != null) o.Definir("suporte", l.Suporte.Dono.ToString());
                }
                lacos.Definir(Chave(l.Dono), o);
            }
            catch (Exception) { /* sem desenho: o painel mostra só o dimensionamento */ }
        }
        foreach (var la in e.Lajes) Obj(la.Dono, $"h = {Fmt.Virgula(la.Espessura, "0.#")} cm, g = {Fmt.Virgula(la.G, "0.##")} kN/m², q = {Fmt.Virgula(la.Q, "0.##")} kN/m², malha {Fmt.Virgula(la.Sx, "0")} × {Fmt.Virgula(la.Sy, "0")} cm");

        // lajes: malha (nós por i, j) e contorno no sistema da laje
        var lajes = ValorJson.NovaLista(); raiz.Definir("lajes", lajes);
        foreach (var la in e.Lajes)
        {
            var o = ValorJson.NovoObjeto();
            o.Definir("chave", Chave(la.Dono)); o.Definir("nx", la.Nx); o.Definir("ny", la.Ny); o.Definir("xs", Lista(la.Xs)); o.Definir("ys", Lista(la.Ys)); o.Definir("sx", la.Sx); o.Definir("sy", la.Sy);
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
            for (int ib = 0; ib < est.Length; ib++)
            {
                // barras da grelha da laje: só as pontas (a laje tem a vista de isovalores; o JSON fica leve)
                var be = est[ib];
                var sel = m.Barras[ib].Dono?.Tipo == TipoDono.Laje ? new[] { be[0], be[be.Length - 1] } : be;
                var lb = ValorJson.NovaLista(); foreach (var s in sel) lb.Adicionar(Lista(s)); rb.Adicionar(lb);
            }
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
            var rs = ValorJson.NovaLista(); foreach (string t in rp.Resumo) rs.Adicionar(ValorJson.De(un.ConverterTexto(t))); o.Definir("resumo", rs);
            var vs = ValorJson.NovaLista();
            foreach (var v in rp.Verificacoes)
            {
                var x = ValorJson.NovoObjeto();
                x.Definir("codigo", v.Codigo); x.Definir("descricao", v.Descricao); x.Definir("valor", un.ConverterTexto(v.Valor)); x.Definir("limite", un.ConverterTexto(v.Limite));
                x.Definir("situacao", v.Situacao.ToString()); x.Definir("referencia", v.Referencia); x.Definir("dica", v.Dica ?? "");
                vs.Adicionar(x);
            }
            o.Definir("verificacoes", vs);
            var mem = ValorJson.NovoObjeto(); mem.Definir("titulo", rp.Memoria.Titulo ?? "");
            var ml = ValorJson.NovaLista(); foreach (string t in rp.Memoria.Linhas) ml.Adicionar(ValorJson.De(un.ConverterTexto(t))); mem.Definir("linhas", ml);
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
