using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DEB.Core.Configuracao;
using DEB.Core.Normas;

namespace DEB.Cad.Paleta;

/// <summary>
/// Menu de configurações (ARMCONFIG / botão "Configuração"): as opções mais usadas agrupadas, com
/// nomes e explicações em português. Tabelas de bitolas, catálogos e traspasse: "Editar JSON…".
/// </summary>
internal sealed class FormConfiguracao : Form
{
    /// <summary>O que o usuário escolheu ao fechar.</summary>
    public enum Acao { Nenhuma, Salvar, CopiarParaDesenho, RemoverDoDesenho, EditarJson }

    public Acao Escolha { get; private set; }
    public ConfiguracaoArmadura Editada { get; }

    public FormConfiguracao(ConfiguracaoArmadura atual, bool doDesenho, string caminhoUsuario)
    {
        Editada = atual.Clonar();
        Text = "Armadura — configurações";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(560, 680);
        MinimumSize = new Size(460, 420);
        ShowInTaskbar = false;
        MinimizeBox = false;

        var origem = new Label
        {
            Dock = DockStyle.Top, Height = 40, Padding = new Padding(6),
            Text = doDesenho
                ? "Este desenho tem configuração PRÓPRIA (gravada no DWG): as alterações valem só para ele."
                : "Configuração do usuário (vale para todos os desenhos sem configuração própria):\n" + caminhoUsuario,
        };
        var grid = new PropertyGrid
        {
            Dock = DockStyle.Fill, PropertySort = PropertySort.Categorized, ToolbarVisible = false, HelpVisible = true,
            SelectedObject = new ConfiguracaoEditavel(Editada),
        };
        var botoes = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft, Padding = new Padding(4) };
        Button B(string texto, Acao a, int largura = 110)
        {
            var b = new Button { Text = texto, Width = largura, Height = 28 };
            b.Click += (s, e) => { Escolha = a; DialogResult = a == Acao.Nenhuma ? DialogResult.Cancel : DialogResult.OK; Close(); };
            botoes.Controls.Add(b);
            return b;
        }
        CancelButton = B("Cancelar", Acao.Nenhuma, 90);
        AcceptButton = B("Salvar", Acao.Salvar, 90);
        B("Editar JSON…", Acao.EditarJson);
        if (doDesenho) B("Remover do desenho", Acao.RemoverDoDesenho, 130);
        else B("Gravar no desenho", Acao.CopiarParaDesenho, 130);
        var dica = new ToolTip();
        dica.SetToolTip(botoes.Controls.OfType<Button>().First(b => b.Text == "Editar JSON…"),
            "Tabela de bitolas, catálogos de treliças/telas, traspasse e demais opções avançadas.");

        Controls.Add(grid);
        Controls.Add(origem);
        Controls.Add(botoes);
    }
}

/// <summary>A configuração como o PropertyGrid do menu a mostra (só as opções mais usadas).</summary>
[TypeConverter(typeof(OrdemDeclaradaConverter))]
internal sealed class ConfiguracaoEditavel
{
    private const string GNum = "1. Numeração e descrição", GComp = "2. Comprimentos", GRA = "3. Relação do aço",
        GDes = "4. Desenho", GFx = "5. Faixas e cortes", GDob = "6. Dobramento (NBR 6118)";
    private readonly ConfiguracaoArmadura _c;

    public ConfiguracaoEditavel(ConfiguracaoArmadura c) { _c = c; Dobramento = new DobramentoEditavel(c.Dobramento); }

    // 1. Numeração e descrição
    [Category(GNum), DisplayName("Numeração"), TypeConverter(typeof(TipoNumeracaoConverter)),
     Description("Global: barras iguais têm o mesmo N no desenho todo. Individual: recomeça em cada detalhamento (bloco). Sequencial: por detalhamento, sem repetir números.")]
    public Core.Numeracao.TipoNumeracao TipoNumeracao { get => _c.TipoNumeracao; set => _c.TipoNumeracao = value; }

    [Category(GNum), DisplayName("Número inicial")]
    public int NumeroInicial { get => _c.NumeroInicial; set => _c.NumeroInicial = Math.Max(0, value); }

    [Category(GNum), DisplayName("Prefixo da posição"), Description("Ex.: N → N1, N2…")]
    public string PrefixoPosicao { get => _c.PrefixoPosicao; set => _c.PrefixoPosicao = value ?? ""; }

    [Category(GNum), DisplayName("Prefixo do comprimento"), Description("Ex.: C → C=250")]
    public string PrefixoComprimento { get => _c.PrefixoComprimento; set => _c.PrefixoComprimento = value ?? ""; }

    [Category(GNum), DisplayName("Formato da descrição"), TypeConverter(typeof(PadraoDescricaoConverter))]
    public PadraoDescricao PadraoDescricao { get => _c.PadraoDescricao; set => _c.PadraoDescricao = value; }

    [Category(GNum), DisplayName("Espelhadas = mesmo N"), TypeConverter(typeof(SimNaoConverter)),
     Description("Barras espelhadas recebem o mesmo número das originais.")]
    public bool EspelhadasIguais { get => _c.EspelhadasIguais; set => _c.EspelhadasIguais = value; }

    // 2. Comprimentos
    [Category(GComp), DisplayName("Comprimento real (desconta dobras)"), TypeConverter(typeof(SimNaoConverter)),
     Description("Sim: C= e relação do aço pelo comprimento real (curvaturas do pino, NBR 6118). Não: soma das medidas externas. As cotas são sempre externas.")]
    public bool DescontarDobras { get => _c.DescontarDobras; set => _c.DescontarDobras = value; }

    [Category(GComp), DisplayName("Casas decimais")]
    public int CasasDecimais { get => _c.CasasDecimais; set => _c.CasasDecimais = Math.Max(0, Math.Min(3, value)); }

    [Category(GComp), DisplayName("Arredondamento"), TypeConverter(typeof(ArredondamentoConverter))]
    public ModoArredondamento Arredondamento { get => _c.Arredondamento; set => _c.Arredondamento = value; }

    // 3. Relação do aço
    [Category(GRA), DisplayName("Título da relação")]
    public string TituloRelacao { get => _c.TituloRelacao; set => _c.TituloRelacao = value ?? ""; }

    [Category(GRA), DisplayName("Título do resumo")]
    public string TituloResumo { get => _c.TituloResumo; set => _c.TituloResumo = value ?? ""; }

    [Category(GRA), DisplayName("Apresentação (tabelas novas)"), TypeConverter(typeof(ApresentacaoRAConverter))]
    public Core.RelacaoAco.ApresentacaoRA ApresentacaoRA { get => _c.ApresentacaoRA; set => _c.ApresentacaoRA = value; }

    [Category(GRA), DisplayName("Resumo (tabelas novas)"), TypeConverter(typeof(ApresentacaoResumoConverter))]
    public Core.RelacaoAco.ApresentacaoResumo ApresentacaoResumo { get => _c.ApresentacaoResumo; set => _c.ApresentacaoResumo = value; }

    [Category(GRA), DisplayName("Mostrar peso total"), TypeConverter(typeof(SimNaoConverter))]
    public bool MostrarPesoTotal { get => _c.MostrarPesoTotal; set => _c.MostrarPesoTotal = value; }

    [Category(GRA), DisplayName("Acréscimo de peso (%)"), Description("Perdas/sobras somadas ao peso do resumo.")]
    public double AcrescimoPesoPercentual { get => _c.AcrescimoPesoPercentual; set => _c.AcrescimoPesoPercentual = Math.Max(0, value); }

    [Category(GRA), DisplayName("Comandos que atualizam a RA"),
     Description("Antes destes comandos o plugin renumera e recalcula descrições e tabelas. Separe por vírgula.")]
    public string ComandosQueAtualizamRA
    {
        get => string.Join(", ", _c.ComandosQueAtualizamRA);
        set => _c.ComandosQueAtualizamRA = (value ?? "").Split(',').Select(s => s.Trim().ToUpperInvariant())
            .Where(s => s.Length > 0).Distinct().ToList();
    }

    // 4. Desenho
    [Category(GDes), DisplayName("Escala base"), TypeConverter(typeof(EscalaConverter)), Description("Escala das barras e tabelas novas.")]
    public double Escala { get => _c.Escala; set { if (value > 0) _c.Escala = value; } }

    [Category(GDes), DisplayName("Altura do texto (mm no papel)"), Description("2 mm = 2 un em 1:10, 5 un em 1:25.")]
    public double AlturaTextoMm { get => _c.AlturaTextoMm; set { if (value > 0) _c.AlturaTextoMm = value; } }

    [Category(GDes), DisplayName("Afastamento do texto (mm no papel)")]
    public double AfastamentoTextoMm { get => _c.AfastamentoTextoMm; set => _c.AfastamentoTextoMm = Math.Max(0, value); }

    [Category(GDes), DisplayName("Representar curvatura"), TypeConverter(typeof(SimNaoConverter)),
     Description("Desenha as dobras com o raio do pino (só na tela).")]
    public bool RepresentarCurvatura { get => _c.RepresentarCurvatura; set => _c.RepresentarCurvatura = value; }

    [Category(GDes), DisplayName("Layer das barras")] public string LayerBarra { get => _c.LayerBarra; set => _c.LayerBarra = value; }
    [Category(GDes), DisplayName("Cor das barras")] public short CorBarra { get => _c.CorBarra; set => _c.CorBarra = value; }
    [Category(GDes), DisplayName("Layer dos textos")] public string LayerTexto { get => _c.LayerTexto; set => _c.LayerTexto = value; }
    [Category(GDes), DisplayName("Cor dos textos")] public short CorTexto { get => _c.CorTexto; set => _c.CorTexto = value; }
    [Category(GDes), DisplayName("Layer das tabelas")] public string LayerTabela { get => _c.LayerTabela; set => _c.LayerTabela = value; }
    [Category(GDes), DisplayName("Cor das tabelas")] public short CorTabela { get => _c.CorTabela; set => _c.CorTabela = value; }

    [Category(GDes), DisplayName("Telas novas identificadas (T1, T2…)"), TypeConverter(typeof(SimNaoConverter)),
     Description("Sim: o ARMTELA desenha \"T1 - Q-92\" e as dimensões no meio da diagonal (padrão do escritório).")]
    public bool TelaIdentificada { get => _c.TelaIdentificada; set => _c.TelaIdentificada = value; }

    // 5. Faixas
    [Category(GFx), DisplayName("Escala das faixas novas"), TypeConverter(typeof(EscalaOuBarraConverter)),
     Description("0 = a mesma escala da barra. Ex.: barra na elevação 1:50 e distribuição na seção 1:25.")]
    public double EscalaFaixa { get => _c.EscalaFaixa; set => _c.EscalaFaixa = Math.Max(0, value); }

    [Category(GFx), DisplayName("Quantidade na faixa"), TypeConverter(typeof(ModoQuantidadeFaixaConverter))]
    public Core.Modelo.ModoQuantidadeFaixa ModoQuantidadeFaixa { get => _c.ModoQuantidadeFaixa; set => _c.ModoQuantidadeFaixa = value; }

    [Category(GFx), DisplayName("Espaçamento inicial (fração)"), Description("Primeira barra a esta fração do espaçamento (0 a 1).")]
    public double EspacamentoInicialFaixa { get => _c.EspacamentoInicialFaixa; set => _c.EspacamentoInicialFaixa = Math.Max(0, Math.Min(1, value)); }

    [Category(GFx), DisplayName("Círculos do corte preenchidos"), TypeConverter(typeof(SimNaoConverter))]
    public bool CortePreenchido { get => _c.CortePreenchido; set => _c.CortePreenchido = value; }

    [Category(GFx), DisplayName("Ampliação do corte"), Description("Diâmetro do círculo = bitola × ampliação.")]
    public double AmpliacaoCorte { get => _c.AmpliacaoCorte; set { if (value > 0) _c.AmpliacaoCorte = value; } }

    [Category(GFx), DisplayName("Afastamento do corte (mm no papel)")]
    public double AfastamentoCorteMm { get => _c.AfastamentoCorteMm; set => _c.AfastamentoCorteMm = Math.Max(0, value); }

    [Category(GFx), DisplayName("Layer das faixas")] public string LayerFaixa { get => _c.LayerFaixa; set => _c.LayerFaixa = value; }
    [Category(GFx), DisplayName("Cor das faixas")] public short CorFaixa { get => _c.CorFaixa; set => _c.CorFaixa = value; }
    [Category(GFx), DisplayName("Layer dos cortes")] public string LayerCorte { get => _c.LayerCorte; set => _c.LayerCorte = value; }
    [Category(GFx), DisplayName("Cor dos cortes")] public short CorCorte { get => _c.CorCorte; set => _c.CorCorte = value; }

    // 6. Dobramento
    [Category(GDob), DisplayName("Pinos e ganchos"), Description("Múltiplos de φ (NBR 6118, tabelas 9.1 e 9.2) e pontas retas dos ganchos.")]
    public DobramentoEditavel Dobramento { get; }
}

[TypeConverter(typeof(OrdemDeclaradaConverter))]
internal sealed class DobramentoEditavel
{
    private readonly RegrasDobramento _r;
    public DobramentoEditavel(RegrasDobramento r) { _r = r; }
    public override string ToString() => "NBR 6118";

    [DisplayName("Pino CA-25 (φ < 20)")] public double PinoCA25Menor20 { get => _r.PinoCA25Menor20; set => _r.PinoCA25Menor20 = value; }
    [DisplayName("Pino CA-25 (φ ≥ 20)")] public double PinoCA25Maior20 { get => _r.PinoCA25Maior20; set => _r.PinoCA25Maior20 = value; }
    [DisplayName("Pino CA-50 (φ < 20)")] public double PinoCA50Menor20 { get => _r.PinoCA50Menor20; set => _r.PinoCA50Menor20 = value; }
    [DisplayName("Pino CA-50 (φ ≥ 20)")] public double PinoCA50Maior20 { get => _r.PinoCA50Maior20; set => _r.PinoCA50Maior20 = value; }
    [DisplayName("Pino CA-60")] public double PinoCA60 { get => _r.PinoCA60; set => _r.PinoCA60 = value; }
    [DisplayName("Pino de estribo (φ ≤ 10)")] public double PinoEstriboAte10 { get => _r.PinoEstriboAte10; set => _r.PinoEstriboAte10 = value; }
    [DisplayName("Pino de estribo CA-25 (10 < φ < 20)")] public double PinoEstriboCA25Ate20 { get => _r.PinoEstriboCA25Ate20; set => _r.PinoEstriboCA25Ate20 = value; }
    [DisplayName("Pino de estribo CA-50 (10 < φ < 20)")] public double PinoEstriboCA50Ate20 { get => _r.PinoEstriboCA50Ate20; set => _r.PinoEstriboCA50Ate20 = value; }
    [DisplayName("Pino de estribo CA-25 (φ ≥ 20)")] public double PinoEstriboCA25Acima20 { get => _r.PinoEstriboCA25Acima20; set => _r.PinoEstriboCA25Acima20 = value; }
    [DisplayName("Pino de estribo CA-50 (φ ≥ 20)")] public double PinoEstriboCA50Acima20 { get => _r.PinoEstriboCA50Acima20; set => _r.PinoEstriboCA50Acima20 = value; }
    [DisplayName("Ponta do gancho 180° (×φ)")] public double PontaGancho180 { get => _r.PontaGancho180; set => _r.PontaGancho180 = value; }
    [DisplayName("Ponta do gancho 45° (×φ)")] public double PontaGancho45 { get => _r.PontaGancho45; set => _r.PontaGancho45 = value; }
    [DisplayName("Ponta do gancho 90° (×φ)")] public double PontaGancho90 { get => _r.PontaGancho90; set => _r.PontaGancho90 = value; }
    [DisplayName("Estribo 45°/180°: ponta (×φ)")] public double PontaEstribo180ou45 { get => _r.PontaEstribo180ou45; set => _r.PontaEstribo180ou45 = value; }
    [DisplayName("Estribo 45°/180°: ponta mínima (cm)")] public double PontaEstribo180ou45Min { get => _r.PontaEstribo180ou45Min; set => _r.PontaEstribo180ou45Min = value; }
    [DisplayName("Estribo 90°: ponta (×φ)")] public double PontaEstribo90 { get => _r.PontaEstribo90; set => _r.PontaEstribo90 = value; }
    [DisplayName("Estribo 90°: ponta mínima (cm)")] public double PontaEstribo90Min { get => _r.PontaEstribo90Min; set => _r.PontaEstribo90Min = value; }
}

internal sealed class PadraoDescricaoConverter : NomesEnumConverter<PadraoDescricao>
{
    public PadraoDescricaoConverter() : base(
        (PadraoDescricao.QuantidadeNumero, "4 N3 ø8.0 c/10 C=200"),
        (PadraoDescricao.NumeroPrimeiro, "N3 – 4ø8.0 c/10 C=200"),
        (PadraoDescricao.NumeroNoFim, "4 ø8.0c/10 C=200 (N3)")) { }
}

internal sealed class ArredondamentoConverter : NomesEnumConverter<ModoArredondamento>
{
    public ArredondamentoConverter() : base(
        (ModoArredondamento.Proximo, "Para o mais próximo"),
        (ModoArredondamento.ParaCima, "Sempre para cima")) { }
}

internal sealed class ModoQuantidadeFaixaConverter : NomesEnumConverter<Core.Modelo.ModoQuantidadeFaixa>
{
    public ModoQuantidadeFaixaConverter() : base(
        (Core.Modelo.ModoQuantidadeFaixa.PisoMaisUm, "⌊L/s⌋ + 1"),
        (Core.Modelo.ModoQuantidadeFaixa.TetoMaisUm, "⌈L/s⌉ + 1"),
        (Core.Modelo.ModoQuantidadeFaixa.Teto, "⌈L/s⌉")) { }
}

/// <summary>Escala "1:n", ou "da barra" quando 0.</summary>
internal sealed class EscalaOuBarraConverter : DoubleConverter
{
    public override object ConvertTo(ITypeDescriptorContext c, System.Globalization.CultureInfo cul, object v, Type t) =>
        t == typeof(string) && v is double d
            ? (d > 0 ? "1:" + Core.Calculo.CalculoComprimento.NumeroCompacto(d) : "da barra")
            : base.ConvertTo(c, cul, v, t);

    public override object ConvertFrom(ITypeDescriptorContext c, System.Globalization.CultureInfo cul, object v)
    {
        if (v is string s)
        {
            s = s.Trim().ToLowerInvariant();
            if (s.Length == 0 || s.StartsWith("da")) return 0.0;
            if (s.StartsWith("1:")) s = s.Substring(2);
            return double.Parse(s.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
        }
        return base.ConvertFrom(c, cul, v);
    }
}
