using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Modelo;
using DEB.Core.Persistencia;
// "Json" sozinho aqui resolveria para o namespace DEB.Core.Motor.Json: use o apelido.
using TextoJson = DEB.Core.Persistencia.Json;
using static DEB.Core.Persistencia.LeitorJson;

namespace DEB.Core.Motor.Json;

/// <summary>
/// Serialização dos contratos do motor (Contratos.cs) para JSON, campo a campo (sem reflection).
/// Regras: nomes dos campos = nome da propriedade em camelCase; enums como texto ("Grade", "CaliceColarinho");
/// P3 = [x, y, z]; ponto 2D = [x, y]; campo desconhecido é ignorado; campo ausente fica com o padrão do contrato;
/// campo com tipo errado ou enum desconhecido → <see cref="FormatException"/> com o caminho do campo.
/// Documentos (Gravar*/Ler*) levam "versao": 1 no topo; versão maior que a suportada → FormatException.
/// </summary>
public static class SerializacaoMotor
{
    public const int Versao = 1;

    // =================================================================================================================
    // Fábrica de elementos (ElementoEstrutural é abstrato: grava "tipo" e lê de volta pelo registro)
    // =================================================================================================================

    private sealed class Registro
    {
        public Func<ElementoEstrutural> Criar;
        public Action<ElementoEstrutural, ValorJson> Gravar;
        public Action<ValorJson, ElementoEstrutural, string> Ler;
    }

    private static readonly Dictionary<string, Registro> _tipos = new Dictionary<string, Registro>(StringComparer.Ordinal);

    static SerializacaoMotor()
    {
        RegistrarElemento<ElementoBloco>(ElementoBloco.TipoId, () => new ElementoBloco(), GravarBloco, LerBloco);
    }

    /// <summary>Registra um tipo de elemento: como criar, gravar e ler os campos próprios (os comuns de
    /// <see cref="ElementoEstrutural"/> já são tratados aqui). O último registro de um tipo vale.</summary>
    public static void RegistrarElemento<T>(string tipo, Func<T> criar, Action<T, ValorJson> gravarCampos, Action<ValorJson, T, string> lerCampos)
        where T : ElementoEstrutural
    {
        if (string.IsNullOrEmpty(tipo)) throw new ArgumentException("Tipo vazio.", nameof(tipo));
        lock (_tipos)
            _tipos[tipo] = new Registro
            {
                Criar = () => criar(),
                Gravar = (e, o) => gravarCampos((T)e, o),
                Ler = (o, e, cam) => lerCampos(o, (T)e, cam),
            };
    }

    /// <summary>Tipos de elemento registrados.</summary>
    public static IReadOnlyList<string> TiposRegistrados { get { lock (_tipos) return _tipos.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList(); } }

    private static Registro Tipo(string tipo, string caminho)
    {
        lock (_tipos)
            if (tipo != null && _tipos.TryGetValue(tipo, out var r)) return r;
        throw new FormatException($"{caminho}.tipo: tipo de elemento desconhecido \"{tipo}\" (registrados: {string.Join(", ", TiposRegistrados)}).");
    }

    // =================================================================================================================
    // Documentos (texto com "versao")
    // =================================================================================================================

    public static string GravarElemento(ElementoEstrutural e) => TextoJson.Gravar(Documento(ParaJson(e)));
    public static ElementoEstrutural LerElemento(string texto) => ElementoDeJson(AbrirDocumento(texto, "elemento"), "elemento");

    public static string GravarSobrescritas(IEnumerable<Sobrescrita> lista) =>
        TextoJson.Gravar(ValorJson.NovoObjeto().Definir("versao", Versao).Definir("sobrescritas", ParaJson(lista)));
    public static List<Sobrescrita> LerSobrescritas(string texto)
    {
        var o = AbrirDocumento(texto, "sobrescritas");
        return SobrescritasDeJson(o["sobrescritas"], "sobrescritas");
    }

    public static string GravarModelo(ModeloArmadura m) => TextoJson.Gravar(Documento(ParaJson(m)));
    public static ModeloArmadura LerModelo(string texto) => ModeloDeJson(AbrirDocumento(texto, "modelo"), "modelo");

    public static string GravarPerfil(PerfilDetalhamento p) => TextoJson.Gravar(Documento(ParaJson(p)));
    public static PerfilDetalhamento LerPerfil(string texto) => PerfilDeJson(AbrirDocumento(texto, "perfil"), "perfil");

    /// <summary>Põe "versao" como primeiro campo do objeto.</summary>
    public static ValorJson Documento(ValorJson corpo)
    {
        var d = ValorJson.NovoObjeto().Definir("versao", Versao);
        foreach (var kv in corpo.Campos) if (kv.Key != "versao") d.Definir(kv.Key, kv.Value);
        return d;
    }

    /// <summary>Lê o texto e confere o topo (objeto, versão suportada). Versão ausente = 1.</summary>
    public static ValorJson AbrirDocumento(string texto, string caminho)
    {
        var o = TextoJson.Ler(texto);
        ConferirVersao(o, caminho);
        return o;
    }

    public static void ConferirVersao(ValorJson o, string caminho)
    {
        if (o == null || !o.EhObjeto) throw new FormatException($"{caminho}: o documento deve ser um objeto JSON {{ … }}.");
        int v = Int(o, "versao", Versao, caminho);
        if (v > Versao) throw new FormatException($"{caminho}.versao: versão {v} é mais nova que a suportada ({Versao}); atualize o programa.");
        if (v < 1) throw new FormatException($"{caminho}.versao: versão {v} inválida.");
    }

    // =================================================================================================================
    // Elemento
    // =================================================================================================================

    public static ValorJson ParaJson(ElementoEstrutural e)
    {
        var reg = Tipo(e.Tipo, "elemento");
        var o = ValorJson.NovoObjeto()
            .Definir("tipo", e.Tipo)
            .Definir("nome", e.Nome)
            .Definir("pilares", Textos(e.Pilares))
            .Definir("repeticoes", e.Repeticoes);
        var niveis = ValorJson.NovaLista();
        foreach (var n in e.Niveis) niveis.Adicionar(ValorJson.NovoObjeto().Definir("pilar", n.Pilar).Definir("cotaM", n.CotaM));
        o.Definir("niveis", niveis).Definir("fck", e.Fck).Definir("aco", e.Aco);
        var arms = ValorJson.NovaLista();
        foreach (var a in e.Armaduras) arms.Adicionar(ParaJson(a));
        o.Definir("armaduras", arms);
        reg.Gravar(e, o);
        return o;
    }

    public static ElementoEstrutural ElementoDeJson(ValorJson o, string caminho = "elemento")
    {
        Objeto(o, caminho);
        var tipo = Txt(o, "tipo", null, caminho);
        if (tipo == null) throw new FormatException($"{caminho}.tipo: campo obrigatório (ex.: \"{ElementoBloco.TipoId}\").");
        var reg = Tipo(tipo, caminho);
        var e = reg.Criar();
        e.Nome = Txt(o, "nome", e.Nome, caminho);
        e.Pilares = ListaTxt(o, "pilares", e.Pilares, caminho);
        e.Repeticoes = Int(o, "repeticoes", e.Repeticoes, caminho);
        var nv = Lista(o, "niveis", caminho);
        if (nv != null)
        {
            e.Niveis = new List<NivelPilar>();
            for (int i = 0; i < nv.Quantidade; i++)
            {
                var c = $"{caminho}.niveis[{i}]";
                var it = Objeto(nv[i], c);
                var n = new NivelPilar();
                n.Pilar = Txt(it, "pilar", n.Pilar, c);
                n.CotaM = Num(it, "cotaM", n.CotaM, c);
                e.Niveis.Add(n);
            }
        }
        e.Fck = Num(o, "fck", e.Fck, caminho);
        e.Aco = Txt(o, "aco", e.Aco, caminho);
        var ar = Lista(o, "armaduras", caminho);
        if (ar != null)
        {
            e.Armaduras = new List<ArmaduraCalculada>();
            for (int i = 0; i < ar.Quantidade; i++) e.Armaduras.Add(ArmaduraDeJson(ar[i], $"{caminho}.armaduras[{i}]"));
        }
        reg.Ler(o, e, caminho);
        return e;
    }

    public static ValorJson ParaJson(ArmaduraCalculada a) => ValorJson.NovoObjeto()
        .Definir("funcao", a.Funcao).Definir("bitola", a.Bitola).Definir("quantidade", a.Quantidade).Definir("espacamento", a.Espacamento);

    public static ArmaduraCalculada ArmaduraDeJson(ValorJson o, string caminho = "armadura")
    {
        Objeto(o, caminho);
        var a = new ArmaduraCalculada();
        a.Funcao = Txt(o, "funcao", a.Funcao, caminho);
        a.Bitola = Num(o, "bitola", a.Bitola, caminho);
        a.Quantidade = Int(o, "quantidade", a.Quantidade, caminho);
        a.Espacamento = Num(o, "espacamento", a.Espacamento, caminho);
        return a;
    }

    private static void GravarBloco(ElementoBloco b, ValorJson o)
    {
        o.Definir("arranjo", b.Arranjo.ToString())
         .Definir("estacasX", b.EstacasX).Definir("estacasY", b.EstacasY)
         .Definir("espX", b.EspX).Definir("espY", b.EspY)
         .Definir("diamEstaca", b.DiamEstaca).Definir("distFace", b.DistFace)
         .Definir("embutimentoEstaca", b.EmbutimentoEstaca).Definir("alturaBase", b.AlturaBase).Definir("lastro", b.Lastro)
         .Definir("pilarBx", b.PilarBx).Definir("pilarBy", b.PilarBy).Definir("pilarEx", b.PilarEx).Definir("pilarEy", b.PilarEy)
         .Definir("ligacao", b.Ligacao.ToString())
         .Definir("alturaColarinho", b.AlturaColarinho).Definir("paredeCalice", b.ParedeCalice).Definir("folgaCalice", b.FolgaCalice)
         .Definir("lemb", b.Lemb).Definir("regularizacao", b.Regularizacao)
         .Definir("interface", b.Interface.ToString())
         .Definir("arranqueNx", b.ArranqueNx).Definir("arranqueNy", b.ArranqueNy).Definir("arranqueCobrimento", b.ArranqueCobrimento);
    }

    private static void LerBloco(ValorJson o, ElementoBloco b, string c)
    {
        b.Arranjo = EnumCampo(o, "arranjo", b.Arranjo, c);
        b.EstacasX = Int(o, "estacasX", b.EstacasX, c);
        b.EstacasY = Int(o, "estacasY", b.EstacasY, c);
        b.EspX = Num(o, "espX", b.EspX, c);
        b.EspY = Num(o, "espY", b.EspY, c);
        b.DiamEstaca = Num(o, "diamEstaca", b.DiamEstaca, c);
        b.DistFace = Num(o, "distFace", b.DistFace, c);
        b.EmbutimentoEstaca = Num(o, "embutimentoEstaca", b.EmbutimentoEstaca, c);
        b.AlturaBase = Num(o, "alturaBase", b.AlturaBase, c);
        b.Lastro = Num(o, "lastro", b.Lastro, c);
        b.PilarBx = Num(o, "pilarBx", b.PilarBx, c);
        b.PilarBy = Num(o, "pilarBy", b.PilarBy, c);
        b.PilarEx = Num(o, "pilarEx", b.PilarEx, c);
        b.PilarEy = Num(o, "pilarEy", b.PilarEy, c);
        b.Ligacao = EnumCampo(o, "ligacao", b.Ligacao, c);
        b.AlturaColarinho = Num(o, "alturaColarinho", b.AlturaColarinho, c);
        b.ParedeCalice = Num(o, "paredeCalice", b.ParedeCalice, c);
        b.FolgaCalice = Num(o, "folgaCalice", b.FolgaCalice, c);
        b.Lemb = Num(o, "lemb", b.Lemb, c);
        b.Regularizacao = Num(o, "regularizacao", b.Regularizacao, c);
        b.Interface = EnumCampo(o, "interface", b.Interface, c);
        b.ArranqueNx = Int(o, "arranqueNx", b.ArranqueNx, c);
        b.ArranqueNy = Int(o, "arranqueNy", b.ArranqueNy, c);
        b.ArranqueCobrimento = Num(o, "arranqueCobrimento", b.ArranqueCobrimento, c);
    }

    // =================================================================================================================
    // Sobrescritas
    // =================================================================================================================

    public static ValorJson ParaJson(Sobrescrita s) => ValorJson.NovoObjeto()
        .Definir("posicao", s.Posicao).Definir("parametro", s.Parametro).Definir("valor", s.Valor);

    public static ValorJson ParaJson(IEnumerable<Sobrescrita> lista)
    {
        var l = ValorJson.NovaLista();
        foreach (var s in lista ?? Enumerable.Empty<Sobrescrita>()) l.Adicionar(ParaJson(s));
        return l;
    }

    public static Sobrescrita SobrescritaDeJson(ValorJson o, string caminho = "sobrescrita")
    {
        Objeto(o, caminho);
        var s = new Sobrescrita();
        s.Posicao = Txt(o, "posicao", s.Posicao, caminho);
        s.Parametro = Txt(o, "parametro", s.Parametro, caminho);
        s.Valor = Num(o, "valor", s.Valor, caminho);
        return s;
    }

    /// <summary>Lista de sobrescritas; null/ausente = lista vazia.</summary>
    public static List<Sobrescrita> SobrescritasDeJson(ValorJson l, string caminho = "sobrescritas")
    {
        var r = new List<Sobrescrita>();
        if (l == null || l.EhNulo) return r;
        if (!l.EhLista) throw new FormatException($"{caminho}: esperado uma lista [ … ].");
        for (int i = 0; i < l.Quantidade; i++) r.Add(SobrescritaDeJson(l[i], $"{caminho}[{i}]"));
        return r;
    }

    // =================================================================================================================
    // Modelo de armadura
    // =================================================================================================================

    public static ValorJson ParaJson(ModeloArmadura m)
    {
        var o = ValorJson.NovoObjeto().Definir("tipoElemento", m.TipoElemento).Definir("nome", m.Nome);
        var corpos = ValorJson.NovaLista();
        foreach (var c in m.Corpos) corpos.Adicionar(ParaJson(c));
        var pos = ValorJson.NovaLista();
        foreach (var p in m.Posicoes) pos.Adicionar(ParaJson(p));
        return o.Definir("corpos", corpos).Definir("posicoes", pos).Definir("niveis", Numeros(m.Niveis)).Definir("avisos", Textos(m.Avisos));
    }

    public static ModeloArmadura ModeloDeJson(ValorJson o, string caminho = "modelo")
    {
        Objeto(o, caminho);
        var m = new ModeloArmadura();
        m.TipoElemento = Txt(o, "tipoElemento", m.TipoElemento, caminho);
        m.Nome = Txt(o, "nome", m.Nome, caminho);
        var cs = Lista(o, "corpos", caminho);
        if (cs != null) { m.Corpos = new List<CorpoConcreto>(); for (int i = 0; i < cs.Quantidade; i++) m.Corpos.Add(CorpoDeJson(cs[i], $"{caminho}.corpos[{i}]")); }
        var ps = Lista(o, "posicoes", caminho);
        if (ps != null) { m.Posicoes = new List<PosicaoArmadura>(); for (int i = 0; i < ps.Quantidade; i++) m.Posicoes.Add(PosicaoDeJson(ps[i], $"{caminho}.posicoes[{i}]")); }
        m.Niveis = DicNum(o, "niveis", m.Niveis, caminho);
        m.Avisos = ListaTxt(o, "avisos", m.Avisos, caminho);
        return m;
    }

    public static ValorJson ParaJson(CorpoConcreto c)
    {
        var planta = ValorJson.NovaLista();
        foreach (var (x, y) in c.Planta) planta.Adicionar(Par(x, y));
        return ValorJson.NovoObjeto()
            .Definir("papel", c.Papel).Definir("planta", planta).Definir("centro", Par(c.Centro.X, c.Centro.Y))
            .Definir("raio", c.Raio).Definir("z0", c.Z0).Definir("z1", c.Z1).Definir("vazio", c.Vazio);
    }

    public static CorpoConcreto CorpoDeJson(ValorJson o, string caminho = "corpo")
    {
        Objeto(o, caminho);
        var c = new CorpoConcreto();
        c.Papel = Txt(o, "papel", c.Papel, caminho);
        var pl = Lista(o, "planta", caminho);
        if (pl != null)
        {
            c.Planta = new List<(double X, double Y)>();
            for (int i = 0; i < pl.Quantidade; i++) c.Planta.Add(ParDeJson(pl[i], $"{caminho}.planta[{i}]"));
        }
        if (Presente(o, "centro")) c.Centro = ParDeJson(o["centro"], $"{caminho}.centro");
        c.Raio = Num(o, "raio", c.Raio, caminho);
        c.Z0 = Num(o, "z0", c.Z0, caminho);
        c.Z1 = Num(o, "z1", c.Z1, caminho);
        c.Vazio = Bool(o, "vazio", c.Vazio, caminho);
        return c;
    }

    public static ValorJson ParaJson(PosicaoArmadura p)
    {
        var o = ValorJson.NovoObjeto()
            .Definir("chave", p.Chave).Definir("funcao", p.Funcao).Definir("nome", p.Nome)
            .Definir("forma", p.Forma.ToString()).Definir("bitola", p.Bitola).Definir("aco", p.Aco)
            .Definir("parametros", Numeros(p.Parametros))
            .Definir("regras", Textos(p.Regras))
            .Definir("manuais", Textos(p.Manuais.OrderBy(s => s, StringComparer.Ordinal)))
            .Definir("vertices", Pontos(p.Vertices))
            .Definir("fechada", p.Fechada);
        var fmt = ValorJson.NovaLista();
        foreach (var f in p.Formatos) fmt.Adicionar(ValorJson.De(f.ToString()));
        o.Definir("formatos", fmt).Definir("distribuicao", ParaJson(p.Distribuicao));
        var cv = ValorJson.NovaLista();
        foreach (var d in p.ComprimentosVar) cv.Adicionar(ValorJson.De(d));
        var sv = ValorJson.NovaLista();
        foreach (var i in p.SegmentosVar.OrderBy(i => i)) sv.Adicionar(ValorJson.De(i));
        var vv = ValorJson.NovaLista();
        foreach (var l in p.VerticesVar) vv.Adicionar(Pontos(l));
        var so = ValorJson.NovaLista();
        foreach (var i in p.Sobrepostos.OrderBy(i => i)) so.Adicionar(ValorJson.De(i));
        var bs = ValorJson.NovaLista();
        foreach (var l in p.Barras) bs.Adicionar(Pontos(l));
        return o.Definir("comprimentosVar", cv).Definir("segmentosVar", sv).Definir("verticesVar", vv)
                .Definir("sobrepostos", so).Definir("barras", bs);
    }

    public static PosicaoArmadura PosicaoDeJson(ValorJson o, string caminho = "posicao")
    {
        Objeto(o, caminho);
        var p = new PosicaoArmadura();
        p.Chave = Txt(o, "chave", p.Chave, caminho);
        p.Funcao = Txt(o, "funcao", p.Funcao, caminho);
        p.Nome = Txt(o, "nome", p.Nome, caminho);
        p.Forma = EnumCampo(o, "forma", p.Forma, caminho);
        p.Bitola = Num(o, "bitola", p.Bitola, caminho);
        p.Aco = Txt(o, "aco", p.Aco, caminho);
        p.Parametros = DicNum(o, "parametros", p.Parametros, caminho);
        p.Regras = DicTxt(o, "regras", p.Regras, caminho);
        p.Manuais = new HashSet<string>(ListaTxt(o, "manuais", p.Manuais.ToList(), caminho));
        if (Presente(o, "vertices")) p.Vertices = PontosDeJson(o["vertices"], $"{caminho}.vertices");
        p.Fechada = Bool(o, "fechada", p.Fechada, caminho);
        var fm = Lista(o, "formatos", caminho);
        if (fm != null)
        {
            p.Formatos = new List<FormatoSegmento>();
            for (int i = 0; i < fm.Quantidade; i++) p.Formatos.Add(EnumDeValor<FormatoSegmento>(fm[i], $"{caminho}.formatos[{i}]"));
        }
        if (Presente(o, "distribuicao")) p.Distribuicao = DistribuicaoDeJson(o["distribuicao"], $"{caminho}.distribuicao");
        var cv = Lista(o, "comprimentosVar", caminho);
        if (cv != null)
        {
            p.ComprimentosVar = new List<double>();
            for (int i = 0; i < cv.Quantidade; i++) p.ComprimentosVar.Add(NumDeValor(cv[i], $"{caminho}.comprimentosVar[{i}]"));
        }
        var sv = Lista(o, "segmentosVar", caminho);
        if (sv != null)
        {
            p.SegmentosVar = new HashSet<int>();
            for (int i = 0; i < sv.Quantidade; i++) p.SegmentosVar.Add(IntDeValor(sv[i], $"{caminho}.segmentosVar[{i}]"));
        }
        var vv = Lista(o, "verticesVar", caminho);
        if (vv != null)
        {
            p.VerticesVar = new List<List<P3>>();
            for (int i = 0; i < vv.Quantidade; i++) p.VerticesVar.Add(PontosDeJson(vv[i], $"{caminho}.verticesVar[{i}]"));
        }
        var so = Lista(o, "sobrepostos", caminho);
        if (so != null)
        {
            p.Sobrepostos = new HashSet<int>();
            for (int i = 0; i < so.Quantidade; i++) p.Sobrepostos.Add(IntDeValor(so[i], $"{caminho}.sobrepostos[{i}]"));
        }
        var bs = Lista(o, "barras", caminho);
        if (bs != null)
        {
            p.Barras = new List<List<P3>>();
            for (int i = 0; i < bs.Quantidade; i++) p.Barras.Add(PontosDeJson(bs[i], $"{caminho}.barras[{i}]"));
        }
        return p;
    }

    public static ValorJson ParaJson(Distribuicao d) => ValorJson.NovoObjeto()
        .Definir("inicio", Ponto(d.Inicio)).Definir("fim", Ponto(d.Fim))
        .Definir("quantidade", d.Quantidade).Definir("espacamento", d.Espacamento).Definir("repeticoes", d.Repeticoes);

    public static Distribuicao DistribuicaoDeJson(ValorJson o, string caminho = "distribuicao")
    {
        var d = new Distribuicao();
        if (o == null || o.EhNulo) return d;
        Objeto(o, caminho);
        if (Presente(o, "inicio")) d.Inicio = PontoDeJson(o["inicio"], $"{caminho}.inicio");
        if (Presente(o, "fim")) d.Fim = PontoDeJson(o["fim"], $"{caminho}.fim");
        d.Quantidade = Int(o, "quantidade", d.Quantidade, caminho);
        d.Espacamento = Num(o, "espacamento", d.Espacamento, caminho);
        d.Repeticoes = Int(o, "repeticoes", d.Repeticoes, caminho);
        return d;
    }

    // =================================================================================================================
    // Perfil
    // =================================================================================================================

    public static ValorJson ParaJson(PerfilDetalhamento p) => ValorJson.NovoObjeto()
        .Definir("id", p.Id).Definir("nome", p.Nome).Definir("tipoElemento", p.TipoElemento)
        .Definir("padroes", Numeros(p.Padroes)).Definir("regras", Textos(p.Regras))
        .Definir("folha", ParaJson(p.Folha ?? new FolhaPerfil()));

    public static PerfilDetalhamento PerfilDeJson(ValorJson o, string caminho = "perfil")
    {
        Objeto(o, caminho);
        var p = new PerfilDetalhamento();
        p.Id = Txt(o, "id", p.Id, caminho);
        p.Nome = Txt(o, "nome", p.Nome, caminho);
        p.TipoElemento = Txt(o, "tipoElemento", p.TipoElemento, caminho);
        p.Padroes = DicNum(o, "padroes", p.Padroes, caminho);
        p.Regras = DicTxt(o, "regras", p.Regras, caminho);
        if (Presente(o, "folha")) p.Folha = FolhaDeJson(o["folha"], $"{caminho}.folha");
        return p;
    }

    public static ValorJson ParaJson(FolhaPerfil f)
    {
        var vistas = ValorJson.NovaLista();
        foreach (var v in f.Vistas) vistas.Adicionar(ParaJson(v));
        return ValorJson.NovoObjeto()
            .Definir("idioma", f.Idioma).Definir("escala", f.Escala).Definir("titulo", f.Titulo)
            .Definir("vistas", vistas).Definir("detalhes", Textos(f.Detalhes))
            .Definir("relacaoAco", f.RelacaoAco).Definir("informacoesProducao", f.InformacoesProducao)
            .Definir("notas", Textos(f.Notas)).Definir("rotulos", Textos(f.Rotulos ?? new Dictionary<string, string>()));
    }

    /// <param name="base">Folha de partida (mesclagem campo a campo: o que faltar no JSON fica como na base).</param>
    public static FolhaPerfil FolhaDeJson(ValorJson o, string caminho = "folha", FolhaPerfil @base = null)
    {
        Objeto(o, caminho);
        var f = @base == null ? new FolhaPerfil() : new FolhaPerfil
        {
            Idioma = @base.Idioma, Escala = @base.Escala, Titulo = @base.Titulo, Vistas = new List<VistaPerfil>(@base.Vistas),
            Detalhes = new List<string>(@base.Detalhes), RelacaoAco = @base.RelacaoAco,
            InformacoesProducao = @base.InformacoesProducao, Notas = new List<string>(@base.Notas),
        };
        f.Idioma = Txt(o, "idioma", f.Idioma, caminho);
        f.Escala = Num(o, "escala", f.Escala, caminho);
        f.Titulo = Txt(o, "titulo", f.Titulo, caminho);
        var vs = Lista(o, "vistas", caminho);
        if (vs != null) { f.Vistas = new List<VistaPerfil>(); for (int i = 0; i < vs.Quantidade; i++) f.Vistas.Add(VistaDeJson(vs[i], $"{caminho}.vistas[{i}]")); }
        f.Detalhes = ListaTxt(o, "detalhes", f.Detalhes, caminho);
        f.RelacaoAco = Bool(o, "relacaoAco", f.RelacaoAco, caminho);
        f.InformacoesProducao = Bool(o, "informacoesProducao", f.InformacoesProducao, caminho);
        f.Notas = ListaTxt(o, "notas", f.Notas, caminho);
        if (Presente(o, "rotulos"))
        {
            var rot = DicTxt(o, "rotulos", new Dictionary<string, string>(), caminho);
            if (@base == null) f.Rotulos = rot;
            else { f.Rotulos = new Dictionary<string, string>(@base.Rotulos ?? new Dictionary<string, string>()); foreach (var kv in rot) f.Rotulos[kv.Key] = kv.Value; }
        }
        else if (@base != null) f.Rotulos = new Dictionary<string, string>(@base.Rotulos ?? new Dictionary<string, string>());
        return f;
    }

    public static ValorJson ParaJson(VistaPerfil v) => ValorJson.NovoObjeto()
        .Definir("id", v.Id).Definir("titulo", v.Titulo).Definir("tipo", v.Tipo.ToString()).Definir("plano", v.Plano)
        .Definir("posicoes", Textos(v.Posicoes)).Definir("barrasAbaixo", v.BarrasAbaixo)
        .Definir("quadro", v.Quadro).Definir("linha", v.Linha).Definir("ordem", v.Ordem);

    public static VistaPerfil VistaDeJson(ValorJson o, string caminho = "vista")
    {
        Objeto(o, caminho);
        var v = new VistaPerfil();
        v.Id = Txt(o, "id", v.Id, caminho);
        v.Titulo = Txt(o, "titulo", v.Titulo, caminho);
        v.Tipo = EnumCampo(o, "tipo", v.Tipo, caminho);
        v.Plano = Txt(o, "plano", v.Plano, caminho);
        v.Posicoes = ListaTxt(o, "posicoes", v.Posicoes, caminho);
        v.BarrasAbaixo = Bool(o, "barrasAbaixo", v.BarrasAbaixo, caminho);
        v.Quadro = Int(o, "quadro", v.Quadro, caminho);
        v.Linha = Int(o, "linha", v.Linha, caminho);
        v.Ordem = Int(o, "ordem", v.Ordem, caminho);
        return v;
    }

    // =================================================================================================================
    // Auxiliares de gravação
    // =================================================================================================================

    private static ValorJson Textos(IEnumerable<string> l)
    {
        var r = ValorJson.NovaLista();
        foreach (var s in l ?? Enumerable.Empty<string>()) r.Adicionar(ValorJson.De(s ?? ""));
        return r;
    }

    private static ValorJson Textos(IDictionary<string, string> d)
    {
        var r = ValorJson.NovoObjeto();
        if (d != null) foreach (var kv in d) r.Definir(kv.Key, kv.Value ?? "");
        return r;
    }

    private static ValorJson Numeros(IDictionary<string, double> d)
    {
        var r = ValorJson.NovoObjeto();
        if (d != null) foreach (var kv in d) r.Definir(kv.Key, kv.Value);
        return r;
    }

    private static ValorJson Par(double x, double y) => ValorJson.NovaLista().Adicionar(ValorJson.De(x)).Adicionar(ValorJson.De(y));
    private static ValorJson Ponto(P3 p) => ValorJson.NovaLista().Adicionar(ValorJson.De(p.X)).Adicionar(ValorJson.De(p.Y)).Adicionar(ValorJson.De(p.Z));

    private static ValorJson Pontos(IEnumerable<P3> l)
    {
        var r = ValorJson.NovaLista();
        foreach (var p in l ?? Enumerable.Empty<P3>()) r.Adicionar(Ponto(p));
        return r;
    }

    // =================================================================================================================
    // Auxiliares de leitura de pontos (os de campo — ausente/null → padrão, tipo errado → FormatException com o caminho — estão em LeitorJson)
    // =================================================================================================================

    private static (double X, double Y) ParDeJson(ValorJson v, string caminho)
    {
        if (v == null || !v.EhLista || v.Quantidade != 2) throw new FormatException($"{caminho}: esperado um ponto [x, y].");
        return (NumDeValor(v[0], caminho + "[0]"), NumDeValor(v[1], caminho + "[1]"));
    }

    private static P3 PontoDeJson(ValorJson v, string caminho)
    {
        if (v == null || !v.EhLista || v.Quantidade != 3) throw new FormatException($"{caminho}: esperado um ponto [x, y, z].");
        return new P3(NumDeValor(v[0], caminho + "[0]"), NumDeValor(v[1], caminho + "[1]"), NumDeValor(v[2], caminho + "[2]"));
    }

    private static List<P3> PontosDeJson(ValorJson v, string caminho)
    {
        if (v == null || v.EhNulo) return new List<P3>();
        if (!v.EhLista) throw new FormatException($"{caminho}: esperado uma lista de pontos [[x, y, z], …].");
        var r = new List<P3>();
        for (int i = 0; i < v.Quantidade; i++) r.Add(PontoDeJson(v[i], $"{caminho}[{i}]"));
        return r;
    }
}
