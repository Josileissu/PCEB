using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Armadura.Cad.Barra;
using Armadura.Core.Calculo;
using Armadura.Core.Configuracao;
using Armadura.Core.Descricao;
using Armadura.Core.Modelo;
using Armadura.Core.Tabelas;

namespace Armadura.Cad.Paleta;

/// <summary>Contexto de aplicação de uma alteração feita na paleta.</summary>
internal sealed class ContextoAlteracao
{
    public Transaction Tr;
    public Polyline Pl;
    public DadosArmadura D;
    public ConfiguracaoArmadura Cfg;
    public double CmPorUnidade;
    /// <summary>Mensagens de erro a mostrar ao usuário.</summary>
    public List<string> Erros = new List<string>();
}

/// <summary>
/// O que o PropertyGrid mostra para uma barra. Os setters guardam a alteração como uma ação
/// pendente; a paleta aplica as ações no desenho (com o documento travado) logo depois.
/// </summary>
[TypeConverter(typeof(OrdemDeclaradaConverter))]
internal sealed class PropriedadesBarra : IPropriedadesCondicionais
{
    private const string GArmadura = "1. Armadura", GTela = "1. Armadura - tela", GDescricao = "2. Descrição", GSegmentos = "3. Segmentos";

    public bool Mostrar(string condicao) => condicao switch
    {
        "Tela" => D.EhTela,
        "VarLista" => D.EhVarLista,
        "EstriboComGanchos" => _comGanchos,
        _ => true,
    };

    internal ObjectId Id { get; }
    internal DadosArmadura D { get; }
    internal List<Action<ContextoAlteracao>> Pendentes { get; } = new List<Action<ContextoAlteracao>>();
    private readonly double _comprimento;
    private readonly string _descricao;
    private readonly ConfiguracaoArmadura _cfg;

    private readonly bool _comGanchos;
    private readonly Core.Estribos.PosicaoGanchos? _posicao;

    internal PropriedadesBarra(ObjectId id, DadosArmadura d, List<GeoSegmento> geo, ConfiguracaoArmadura cfg,
        bool estriboComGanchos = false, Core.Estribos.PosicaoGanchos? posicaoGanchos = null)
    {
        Id = id;
        D = d;
        _cfg = cfg;
        _comGanchos = estriboComGanchos;
        _posicao = posicaoGanchos;
        _comprimento = CalculoComprimento.ComprimentoUnitario(geo, cfg, d);
        _descricao = FormatadorDescricao.Formatar(d, _comprimento, cfg).Replace("%%c", "ø").Replace("%%C", "ø");
        Segmentos = new ListaSegmentos(this, geo);
    }

    private void Mudar(Action<ContextoAlteracao> acao) => Pendentes.Add(acao);

    // ---------- 1. Armadura ----------

    [Category(GArmadura), DisplayName("Numeração"), ReadOnly(true),
     Description("Calculada pela relação do aço. N0 = barra nova: rode ARMRAATUALIZAR.")]
    public string Numeracao => _cfg.PrefixoPosicao + D.Numero;

    [Category(GArmadura), DisplayName("Modelo (treliça/tela)"), TypeConverter(typeof(ModeloConverter)),
     Description("Treliça (TR …) ou tela soldada (Q-…) do catálogo. Vazio em barras comuns.")]
    public string Modelo
    {
        get => D.EhTrelica ? D.Trelica : D.EhTela ? D.Tela : "";
        set
        {
            if (D.EhTrelica)
            {
                var m = _cfg.Trelicas.Trelica(value) ?? throw new ArgumentException("Treliça fora do catálogo.");
                D.Trelica = m.Nome;
                Mudar(c => { c.D.Trelica = m.Nome; c.D.Bitola = m.PhiInferior; });
            }
            else if (D.EhTela)
            {
                var m = _cfg.Telas.Tela(value) ?? throw new ArgumentException("Tela fora do catálogo.");
                D.Tela = m.Nome;
                Mudar(c => { c.D.Tela = m.Nome; c.D.Bitola = m.PhiLongitudinal; c.D.Aco = m.Aco; });
            }
            else if (!string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Só treliças e telas têm modelo.");
        }
    }

    [Category(GArmadura), DisplayName("Bitola (mm)"), TypeConverter(typeof(BitolaConverter)),
     Description("Bitolas ativas na configuração (ARMCONFIG).")]
    public double Bitola
    {
        get => D.Bitola;
        set
        {
            var b = _cfg.Barras.Obter(value, D.Aco) ?? throw new ArgumentException("Bitola fora da tabela de barras.");
            D.Bitola = b.Diametro;
            // a bitola existe só como cordoalha (ou só como barra): o aço acompanha
            bool trocaAco = Core.Tabelas.TabelaBarras.EhCordoalha(b.Aco) != Core.Tabelas.TabelaBarras.EhCordoalha(D.Aco);
            string aco = trocaAco ? b.Aco : D.Aco;
            D.Aco = aco;
            Mudar(c => { c.D.Bitola = b.Diametro; c.D.Aco = aco; RefazerEstribo(c); });
        }
    }

    /// <summary>Estribo com ganchos: bitola/aço mudam o gancho e a passagem do canto (pino + 2φ).</summary>
    private static void RefazerEstribo(ContextoAlteracao c)
    {
        if (!ServicoEstribo.ComGanchos(c.Pl, c.D)) return;
        var e = ServicoEstribo.Converter(c.Tr, c.Pl, c.D, c.Cfg);
        if (e != null) c.Erros.Add(e);
    }

    [Category(GArmadura), DisplayName("Aço"), TypeConverter(typeof(AcoConverter))]
    public string Aco
    {
        get => D.Aco;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Informe o aço.");
            var v = value.Trim().ToUpperInvariant();
            D.Aco = v;
            Mudar(c => { c.D.Aco = v; RefazerEstribo(c); });
        }
    }

    [Category(GArmadura), DisplayName("Quantidade"),
     Description("Com faixas, a quantidade é a soma delas (edite as faixas).")]
    public int Quantidade
    {
        get => D.Quantidade;
        set
        {
            if (D.TemFaixas) throw new ArgumentException("Esta barra tem faixas: a quantidade é a soma delas.");
            if (value < 1) throw new ArgumentException("A quantidade mínima é 1.");
            D.Quantidade = value;
            Mudar(c => c.D.Quantidade = value);
        }
    }

    [Category(GArmadura), DisplayName("Repetições"), Description("Multiplica a quantidade na relação do aço.")]
    public int Repeticoes
    {
        get => D.Repeticoes;
        set
        {
            if (value < 1) throw new ArgumentException("O mínimo é 1.");
            D.Repeticoes = value;
            Mudar(c => c.D.Repeticoes = value);
        }
    }

    [Category(GArmadura), DisplayName("Camadas"),
     Description("Barras em camadas por nervura (lajes nervuradas), ex.: 3+1. A quantidade passa a ser o nº de nervuras. Vazio = sem camadas.")]
    public string Camadas
    {
        get => D.Camadas;
        set
        {
            var v = (value ?? "").Replace(" ", "");
            if (v.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d+(\+\d+)*$"))
                throw new ArgumentException("Use números separados por +, ex.: 3+1.");
            D.Camadas = v;
            Mudar(c => c.D.Camadas = v);
        }
    }

    [Category(GArmadura), DisplayName("Espaçamento (cm)"), TypeConverter(typeof(CmConverter)),
     Description("0 = não indicar \"c/\" na descrição.")]
    public double Espacamento
    {
        get => D.Espacamento;
        set
        {
            if (value < 0) throw new ArgumentException("O espaçamento não pode ser negativo.");
            D.Espacamento = value;
            Mudar(c => c.D.Espacamento = value);
        }
    }

    [Category(GArmadura), DisplayName("Estribo"), TypeConverter(typeof(SimNaoConverter)),
     Description("Ganchos calculados pelos critérios de estribo. Numa polilinha FECHADA, \"Sim\" põe os ganchos " +
                 "no vértice superior mais à esquerda (mude em \"Ganchos no canto\"); \"Não\" volta à poligonal fechada.")]
    public bool Estribo
    {
        get => D.Estribo;
        set
        {
            D.Estribo = value;
            Mudar(c =>
            {
                c.D.Estribo = value;
                if (value && ServicoEstribo.PoligonoFechado(c.Pl) != null)
                {
                    var e = ServicoEstribo.Converter(c.Tr, c.Pl, c.D, c.Cfg);
                    if (e != null) c.Erros.Add(e);
                }
                else if (!value && ServicoEstribo.ComGanchos(c.Pl, c.D))
                    ServicoEstribo.Desfazer(c.Tr, c.Pl, c.D);
            });
        }
    }

    [Category(GArmadura), DisplayName("Gancho do estribo"), TypeConverter(typeof(GanchoEstriboConverter)), SoQuando("EstriboComGanchos"),
     Description("Estribo fechado: gancho de 90° (perpendicular ao lado), 45° (dobra de 135°) ou 180°. Comprimento pela norma.")]
    public FormatoSegmento GanchoEstribo
    {
        get => _comGanchos ? ServicoEstribo.Gancho(D) : FormatoSegmento.Linha;
        set
        {
            if (!_comGanchos) throw new ArgumentException("Só estribos fechados com ganchos (marque Estribo = Sim numa polilinha fechada).");
            if (!Segmento.EhGancho(value)) throw new ArgumentException("Escolha 90°, 45° ou 180°.");
            Mudar(c =>
            {
                var e = ServicoEstribo.Converter(c.Tr, c.Pl, c.D, c.Cfg, gancho: value);
                if (e != null) c.Erros.Add(e);
            });
        }
    }

    [Category(GArmadura), DisplayName("Ganchos no canto"), TypeConverter(typeof(CantoGanchosConverter)), SoQuando("EstriboComGanchos"),
     Description("Vértice do estribo fechado em que ficam os ganchos. \"Próximo vértice\" passa para o vértice seguinte.")]
    public string CantoGanchos
    {
        get => !_comGanchos ? "" : _posicao.HasValue ? CantoGanchosConverter.Nome(_posicao.Value) : "Outro vértice";
        set
        {
            if (!_comGanchos) throw new ArgumentException("Só estribos fechados com ganchos.");
            var pos = CantoGanchosConverter.Valor(value) ?? throw new ArgumentException("Escolha um dos cantos da lista.");
            Mudar(c =>
            {
                var e = ServicoEstribo.Converter(c.Tr, c.Pl, c.D, c.Cfg, posicao: pos);
                if (e != null) c.Erros.Add(e);
            });
        }
    }

    [Category(GArmadura), DisplayName("Corrido"), TypeConverter(typeof(SimNaoConverter)),
     Description("Comprimento indicativo: a descrição mostra CORRIDO; o comprimento real entra no resumo.")]
    public bool Corrido
    {
        get => D.Corrido;
        set { D.Corrido = value; Mudar(c => c.D.Corrido = value); }
    }

    [Category(GArmadura), DisplayName("Visível"), TypeConverter(typeof(SimNaoConverter)),
     Description("Oculta a linha e as cotas; a barra continua na relação do aço.")]
    public bool Visivel
    {
        get => D.Visivel;
        set { D.Visivel = value; Mudar(c => c.D.Visivel = value); }
    }

    [Category(GArmadura), DisplayName("Escala"), TypeConverter(typeof(EscalaConverter)),
     Description("Escala de desenho da barra (1:n). Define a altura dos textos: 2 un em 1:10, 5 un em 1:25.")]
    public double Escala
    {
        get => _cfg.EscalaEfetiva(D.Escala);
        set
        {
            if (value <= 0) throw new ArgumentException("A escala deve ser positiva (ex.: 1:10).");
            D.Escala = value;
            Mudar(c => c.D.Escala = value);
        }
    }

    [Category(GTela), DisplayName("Mostrar distribuição do aço"), TypeConverter(typeof(SimNaoConverter)), SoQuando("Tela"),
     Description("Desenha os fios da tela (espaçamentos do catálogo; longitudinais ao longo do lado maior).")]
    public bool TelaDistribuicao
    {
        get => D.TelaDistribuicao;
        set { D.TelaDistribuicao = value; Mudar(c => c.D.TelaDistribuicao = value); }
    }

    [Category(GTela), DisplayName("Identificar (T1, T2…)"), TypeConverter(typeof(SimNaoConverter)), SoQuando("Tela"),
     Description("Representação do escritório: \"T1 - 2x Q-92\" e as dimensões (ex.: 245 x 348) no meio da diagonal, " +
                 "que fica interrompida no texto. Painéis iguais recebem o mesmo T; sem cotas nos lados.")]
    public bool TelaIdentificada
    {
        get => D.TelaIdentificada;
        set { D.TelaIdentificada = value; Mudar(c => c.D.TelaIdentificada = value); }
    }

    [Category(GTela), DisplayName("Cotar dimensões do painel"), TypeConverter(typeof(SimNaoConverter)), SoQuando("Tela"),
     Description("Cotas da largura e da altura do retângulo da tela.")]
    public bool TelaCotas
    {
        get => D.TelaCotas;
        set { D.TelaCotas = value; Mudar(c => c.D.TelaCotas = value); }
    }

    [Category(GTela), DisplayName("Texto na diagonal"), TypeConverter(typeof(SimNaoConverter)), SoQuando("Tela"),
     Description("A descrição (ex.: 1 Q-92) fica no meio da diagonal do painel, girada com ela.")]
    public bool TelaTextoDiagonal
    {
        get => D.TelaTextoDiagonal;
        set { D.TelaTextoDiagonal = value; Mudar(c => c.D.TelaTextoDiagonal = value); }
    }

    [Category(GArmadura), DisplayName("Comprimento (cm)"), ReadOnly(true), Description("Muda só pela geometria.")]    public string Comprimento => D.TemVar ? $"VAR ({CalculoComprimento.NumeroCompacto(_comprimento)})"
        : CalculoComprimento.NumeroCompacto(_comprimento);

    [Category(GArmadura), DisplayName("Barras VAR"), ReadOnly(true), SoQuando("VarLista"),
     Description("Grupo de comprimento variável: cada barra tem o seu comprimento (externo); a relação do aço soma todas.")]
    public string BarrasVar => D.EhVarLista
        ? $"{D.ComprimentosVar.Count} barras, de {CalculoComprimento.NumeroCompacto(D.ComprimentosVar.Min())} a {CalculoComprimento.NumeroCompacto(D.ComprimentosVar.Max())} cm (externo)"
        : "";

    [Category(GArmadura), DisplayName("Descrição"), ReadOnly(true)]
    public string Descricao => _descricao;

    // ---------- 2. Descrição ----------

    private bool Campo(CamposDescricao f) => D.Campos.HasFlag(f);

    private void Campo(CamposDescricao f, bool v)
    {
        D.Campos = v ? D.Campos | f : D.Campos & ~f;
        Mudar(c => c.D.Campos = v ? c.D.Campos | f : c.D.Campos & ~f);
    }

    [Category(GDescricao), DisplayName("Mostrar quantidade"), TypeConverter(typeof(SimNaoConverter))]
    public bool MostrarQuantidade { get => Campo(CamposDescricao.Quantidade); set => Campo(CamposDescricao.Quantidade, value); }

    [Category(GDescricao), DisplayName("Mostrar N"), TypeConverter(typeof(SimNaoConverter))]
    public bool MostrarNumero { get => Campo(CamposDescricao.Numero); set => Campo(CamposDescricao.Numero, value); }

    [Category(GDescricao), DisplayName("Mostrar bitola"), TypeConverter(typeof(SimNaoConverter))]
    public bool MostrarBitola { get => Campo(CamposDescricao.Bitola); set => Campo(CamposDescricao.Bitola, value); }

    [Category(GDescricao), DisplayName("Mostrar espaçamento"), TypeConverter(typeof(SimNaoConverter))]
    public bool MostrarEspacamento { get => Campo(CamposDescricao.Espacamento); set => Campo(CamposDescricao.Espacamento, value); }

    [Category(GDescricao), DisplayName("Mostrar comprimento"), TypeConverter(typeof(SimNaoConverter))]
    public bool MostrarComprimento { get => Campo(CamposDescricao.Comprimento); set => Campo(CamposDescricao.Comprimento, value); }

    // ---------- 3. Segmentos ----------

    // MergableProperty(false): com várias barras selecionadas, os segmentos não aparecem
    [Category(GSegmentos), DisplayName("Segmentos"), TypeConverter(typeof(ListaSegmentosConverter)), MergableProperty(false),
     Description("Comprimento, formato (linha/ganchos), VAR e cota de cada segmento. Só com uma barra selecionada.")]
    public ListaSegmentos Segmentos { get; }

    internal void MudarSegmento(Action<ContextoAlteracao> acao) => Mudar(acao);
}

internal sealed class ListaSegmentos
{
    public List<PropriedadesSegmento> Itens { get; }

    public ListaSegmentos(PropriedadesBarra dono, List<GeoSegmento> geo)
    {
        Itens = geo.Select((g, i) => new PropriedadesSegmento(dono, i, g)).ToList();
    }
}

[TypeConverter(typeof(OrdemDeclaradaConverter))]
internal sealed class PropriedadesSegmento
{
    private readonly PropriedadesBarra _dono;
    private readonly int _i;
    private readonly GeoSegmento _geo;

    public PropriedadesSegmento(PropriedadesBarra dono, int i, GeoSegmento geo)
    {
        _dono = dono; _i = i; _geo = geo;
    }

    internal bool EhArco => _geo.Tipo == TipoSegmento.Arco;
    private Segmento S => _dono.D.Segmentos[_i];

    [DisplayName("Comprimento (cm)"), TypeConverter(typeof(CmConverter)),
     Description("Segmentos retos: muda a geometria (o 1º move o início; os demais empurram os seguintes). Arcos: só leitura.")]
    public double Comprimento
    {
        get => _geo.Comprimento;
        set
        {
            if (EhArco) throw new ArgumentException("O comprimento de um segmento em arco não é editável.");
            if (value <= 0) throw new ArgumentException("O comprimento deve ser positivo.");
            _dono.MudarSegmento(c =>
            {
                var e = ServicoBarra.AlterarComprimentoSegmento(c.Pl, c.D, _i, value, c.CmPorUnidade);
                if (e != null) c.Erros.Add(e);
            });
        }
    }

    [DisplayName("Formato"), TypeConverter(typeof(FormatoConverter)),
     Description("Nos ganchos, o comprimento é calculado pela bitola, pelo aço e pela norma (configuração).")]
    public FormatoSegmento Formato
    {
        get => S.Formato;
        set
        {
            _dono.MudarSegmento(c =>
            {
                var e = ServicoBarra.AlterarFormatoSegmento(c.Pl, c.D, _i, value, c.CmPorUnidade);
                if (e != null) c.Erros.Add(e);
            });
        }
    }

    [DisplayName("VAR"), TypeConverter(typeof(SimNaoConverter)),
     Description("A cota e a descrição mostram VAR; o comprimento real entra no resumo.")]
    public bool Var
    {
        get => S.Var;
        set => _dono.MudarSegmento(c => c.D.Segmentos[_i].Var = value);
    }

    [DisplayName("Cotar"), TypeConverter(typeof(SimNaoConverter))]
    public bool Cotar
    {
        get => S.Cotar;
        set => _dono.MudarSegmento(c => c.D.Segmentos[_i].Cotar = value);
    }

    public override string ToString() =>
        CalculoComprimento.NumeroCompacto(_geo.Comprimento) + " cm" + (Segmento.EhGancho(S.Formato) ? " (gancho)" : "") + (S.Var ? " VAR" : "");
}

/// <summary>Gancho do estribo: só 90°, 45° e 180°; barra sem ganchos mostra vazio.</summary>
internal sealed class GanchoEstriboConverter : NomesEnumConverter<FormatoSegmento>
{
    public GanchoEstriboConverter() : base(
        (FormatoSegmento.Gancho90, "90°"),
        (FormatoSegmento.Gancho45, "45° (dobra de 135°)"),
        (FormatoSegmento.Gancho180, "180°")) { }

    public override object ConvertTo(ITypeDescriptorContext c, System.Globalization.CultureInfo cul, object v, Type t) =>
        t == typeof(string) && v is FormatoSegmento f && !Segmento.EhGancho(f) ? "" : base.ConvertTo(c, cul, v, t);
}

/// <summary>Cantos para os ganchos do estribo fechado (lista fixa de textos).</summary>
internal sealed class CantoGanchosConverter : StringConverter
{
    private static readonly (Core.Estribos.PosicaoGanchos valor, string nome)[] Nomes =
    {
        (Core.Estribos.PosicaoGanchos.SuperiorEsquerdo, "Superior esquerdo"),
        (Core.Estribos.PosicaoGanchos.SuperiorDireito, "Superior direito"),
        (Core.Estribos.PosicaoGanchos.InferiorEsquerdo, "Inferior esquerdo"),
        (Core.Estribos.PosicaoGanchos.InferiorDireito, "Inferior direito"),
        (Core.Estribos.PosicaoGanchos.ProximoVertice, "Próximo vértice"),
    };

    public static string Nome(Core.Estribos.PosicaoGanchos p) => Nomes.First(n => n.valor == p).nome;

    public static Core.Estribos.PosicaoGanchos? Valor(string nome)
    {
        var n = Nomes.FirstOrDefault(x => string.Equals(x.nome, (nome ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        return n.nome == null ? (Core.Estribos.PosicaoGanchos?)null : n.valor;
    }

    public override bool GetStandardValuesSupported(ITypeDescriptorContext c) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => true;
    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) =>
        new StandardValuesCollection(Nomes.Select(n => n.nome).ToArray());
}