using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DEB.Core.Templates.PilarPreMoldado;

namespace DEB.Cad.Paleta;

/// <summary>Tipo de consolo no PropertyGrid, com nomes em português.</summary>
internal sealed class PropriedadesConsolo
{
    private const string G1 = "1. Tipo", G2 = "2. Forma (cm)", G3 = "3. Tirante", G4 = "4. Costura e estribos", G5 = "5. Armadura inferior",
        G6 = "6. Pinos", G7 = "7. Almofada (cm)";

    public TipoConsolo T { get; }
    public PropriedadesConsolo(TipoConsolo t) { T = t; }

    [Category(G1), DisplayName("Nome"), Description("Nome que aparece na lista do template do pilar.")]
    public string Nome { get => T.Nome; set => T.Nome = value?.Trim() ?? ""; }
    [Category(G1), DisplayName("Descrição")] public string Descricao { get => T.Descricao; set => T.Descricao = value ?? ""; }
    [Category(G1), DisplayName("Duplo"), TypeConverter(typeof(SimNaoConverter)),
     Description("Dois consolos em faces opostas com o tirante passando pelo pilar.")]
    public bool Duplo { get => T.Duplo; set => T.Duplo = value; }
    [Category(G1), DisplayName("Resumo"), ReadOnly(true)] public string Resumo => T.Resumo;

    [Category(G2), DisplayName("Largura b (ao longo da face)")] public double B { get => T.B; set => T.B = Math.Max(5, value); }
    [Category(G2), DisplayName("Balanço a")] public double A { get => T.A; set => T.A = Math.Max(5, value); }
    [Category(G2), DisplayName("Altura junto ao pilar h")] public double H { get => T.H; set => T.H = Math.Max(5, value); }
    [Category(G2), DisplayName("Altura na ponta h1"), Description("Menor que h = trapezoidal; igual = retangular.")]
    public double H1 { get => T.H1; set => T.H1 = Math.Max(1, value); }

    [Category(G3), DisplayName("Barras")] public int TirN { get => T.TirN; set => T.TirN = Math.Max(1, value); }
    [Category(G3), DisplayName("Bitola (mm)")] public double TirPhi { get => T.TirPhi; set => T.TirPhi = value; }
    [Category(G3), DisplayName("Ancoragem na ponta em laço"), TypeConverter(typeof(SimNaoConverter)),
     Description("Não = barra transversal soldada (padrão do escritório).")]
    public bool Laco { get => T.Laco; set => T.Laco = value; }
    [Category(G3), DisplayName("Perna junto à outra face (cm)"),
     Description("Só no consolo simples: o tirante desce junto à face oposta do pilar. 0 = gancho de 90° padrão.")]
    public double TirPerna { get => T.TirPerna; set => T.TirPerna = Math.Max(0, value); }

    [Category(G4), DisplayName("Costura: estribos horizontais")] public int CostN { get => T.CostN; set => T.CostN = Math.Max(0, value); }
    [Category(G4), DisplayName("Costura: bitola (mm)")] public double CostPhi { get => T.CostPhi; set => T.CostPhi = value; }
    [Category(G4), DisplayName("Estribos verticais (por consolo)")] public int VertN { get => T.VertN; set => T.VertN = Math.Max(0, value); }
    [Category(G4), DisplayName("Estribos verticais: bitola (mm)")] public double VertPhi { get => T.VertPhi; set => T.VertPhi = value; }

    [Category(G5), DisplayName("Barras (por consolo)")] public int InfN { get => T.InfN; set => T.InfN = Math.Max(0, value); }
    [Category(G5), DisplayName("Bitola (mm)")] public double InfPhi { get => T.InfPhi; set => T.InfPhi = value; }
    [Category(G5), DisplayName("Comprimento dentro do pilar (cm)")] public double InfAnc { get => T.InfAnc; set => T.InfAnc = Math.Max(0, value); }
    [Category(G5), DisplayName("Retorno em cima (cm)"), Description("0 = padrão: duplo = 3 cm; simples = do tamanho da perna.")]
    public double InfRetorno { get => T.InfRetorno; set => T.InfRetorno = Math.Max(0, value); }
    [Category(G5), DisplayName("Descida no fim do retorno (cm)"), Description("0 = padrão: duplo = 5 cm; simples = sem descida.")]
    public double InfDescida { get => T.InfDescida; set => T.InfDescida = Math.Max(0, value); }

    [Category(G6), DisplayName("Pinos (por consolo)")] public int PinoN { get => T.PinoN; set => T.PinoN = Math.Max(0, value); }
    [Category(G6), DisplayName("Bitola (mm)")] public double PinoPhi { get => T.PinoPhi; set => T.PinoPhi = value; }
    [Category(G6), DisplayName("Altura acima do consolo (cm)")] public double PinoAcima { get => T.PinoAcima; set => T.PinoAcima = Math.Max(0, value); }
    [Category(G6), DisplayName("Embutimento (cm)")] public double PinoEmb { get => T.PinoEmb; set => T.PinoEmb = Math.Max(1, value); }
    [Category(G6), DisplayName("Pé / dobra (cm)")] public double PinoPe { get => T.PinoPe; set => T.PinoPe = Math.Max(0, value); }

    [Category(G7), DisplayName("No sentido do balanço"), Description("0 = sem almofada.")] public double AlmA { get => T.AlmA; set => T.AlmA = Math.Max(0, value); }
    [Category(G7), DisplayName("No sentido da largura")] public double AlmB { get => T.AlmB; set => T.AlmB = Math.Max(0, value); }
    [Category(G7), DisplayName("Espessura")] public double AlmE { get => T.AlmE; set => T.AlmE = Math.Max(0, value); }
}

/// <summary>Tipo de inserto no PropertyGrid.</summary>
internal sealed class PropriedadesInserto
{
    private const string G1 = "1. Tipo", G2 = "2. Topo: chumbadores", G3 = "3. Face: chapa e grapas";

    public TipoInserto T { get; }
    public PropriedadesInserto(TipoInserto t) { T = t; }

    [Category(G1), DisplayName("Nome")] public string Nome { get => T.Nome; set => T.Nome = value?.Trim() ?? ""; }
    [Category(G1), DisplayName("Descrição")] public string Descricao { get => T.Descricao; set => T.Descricao = value ?? ""; }
    [Category(G1), DisplayName("Modo"), TypeConverter(typeof(ModoInsertoConverter))] public string Modo { get => T.Modo; set => T.Modo = value; }
    [Category(G1), DisplayName("Resumo"), ReadOnly(true)] public string Resumo => T.Resumo;

    [Category(G2), DisplayName("Gabarito")] public string Gabarito { get => T.Gabarito; set => T.Gabarito = value?.Trim() ?? ""; }
    [Category(G2), DisplayName("Distância entre chumbadores em x (cm)")] public double Ax { get => T.Ax; set => T.Ax = Math.Max(0, value); }
    [Category(G2), DisplayName("Distância entre chumbadores em y (cm)")] public double Ay { get => T.Ay; set => T.Ay = Math.Max(0, value); }
    [Category(G2), DisplayName("Chumbadores")] public int ChumbN { get => T.ChumbN; set => T.ChumbN = Math.Max(1, Math.Min(4, value)); }
    [Category(G2), DisplayName("Bitola (mm)")] public double ChumbPhi { get => T.ChumbPhi; set => T.ChumbPhi = value; }
    [Category(G2), DisplayName("Comprimento dentro do pilar (cm)")] public double ChumbComp { get => T.ChumbComp; set => T.ChumbComp = Math.Max(1, value); }
    [Category(G2), DisplayName("Saliência acima do topo (cm)")] public double ChumbAcima { get => T.ChumbAcima; set => T.ChumbAcima = Math.Max(0, value); }

    [Category(G3), DisplayName("Chapa: largura (cm)")] public double ChapaL { get => T.ChapaL; set => T.ChapaL = Math.Max(1, value); }
    [Category(G3), DisplayName("Chapa: altura (cm)")] public double ChapaH { get => T.ChapaH; set => T.ChapaH = Math.Max(1, value); }
    [Category(G3), DisplayName("Chapa: espessura (cm)")] public double ChapaE { get => T.ChapaE; set => T.ChapaE = Math.Max(0.1, value); }
    [Category(G3), DisplayName("Grapas")] public int GrapaN { get => T.GrapaN; set => T.GrapaN = Math.Max(0, value); }
    [Category(G3), DisplayName("Grapas: bitola (mm)")] public double GrapaPhi { get => T.GrapaPhi; set => T.GrapaPhi = value; }
    [Category(G3), DisplayName("Grapas: comprimento (cm)")] public double GrapaComp { get => T.GrapaComp; set => T.GrapaComp = Math.Max(1, value); }
}

internal sealed class ModoInsertoConverter : StringConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext c) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => true;
    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) =>
        new StandardValuesCollection(new[] { TipoInserto.ModoTopo, TipoInserto.ModoFace });
}

/// <summary>
/// Catálogo editável (ARMCONSOLOS, ARMINSERTOS): lista à esquerda, propriedades à direita, novo/duplicar/excluir.
/// Edita cópias; o OK confere nomes vazios ou repetidos.
/// </summary>
internal sealed class FormCatalogo<T> : Form
{
    private readonly List<T> _itens;
    private readonly Func<T, string> _nome;
    private readonly Action<T, string> _renomear;
    private readonly Func<T, string> _resumo;
    private readonly Func<T, T> _clonar;
    private readonly ListBox _lista = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly PropertyGrid _grid = new PropertyGrid { Dock = DockStyle.Fill, ToolbarVisible = false, PropertySort = PropertySort.Categorized };

    public List<T> Resultado => _itens;

    public FormCatalogo(string titulo, IEnumerable<T> itens, Func<T, T> clonar, Func<T> novo, Func<T, string> nome, Action<T, string> renomear,
        Func<T, string> resumo, Func<T, object> propriedades)
    {
        _clonar = clonar; _nome = nome; _renomear = renomear; _resumo = resumo;
        _itens = itens.Select(clonar).ToList();
        Text = titulo;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(780, 560);
        ShowInTaskbar = false;
        MinimizeBox = false;

        var esquerda = new Panel { Dock = DockStyle.Left, Width = 270 };
        var botoesLista = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(2) };
        var bNovo = new Button { Text = "Novo", Width = 80, Height = 26 };
        var bDuplicar = new Button { Text = "Duplicar", Width = 80, Height = 26 };
        var bExcluir = new Button { Text = "Excluir", Width = 80, Height = 26 };
        botoesLista.Controls.AddRange(new Control[] { bNovo, bDuplicar, bExcluir });
        esquerda.Controls.Add(_lista);
        esquerda.Controls.Add(botoesLista);

        var botoes = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft, Padding = new Padding(4) };
        var cancelar = new Button { Text = "Cancelar", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "OK", Width = 90, Height = 28 };
        botoes.Controls.Add(cancelar);
        botoes.Controls.Add(ok);
        CancelButton = cancelar;

        _lista.SelectedIndexChanged += (s, e) => _grid.SelectedObject = _lista.SelectedIndex >= 0 ? propriedades(_itens[_lista.SelectedIndex]) : null;
        _grid.PropertyValueChanged += (s, e) => Atualizar(_lista.SelectedIndex);
        bNovo.Click += (s, e) => { var t = novo(); _renomear(t, NomeLivre("Novo")); _itens.Add(t); Atualizar(_itens.Count - 1); };
        bDuplicar.Click += (s, e) =>
        {
            if (_lista.SelectedIndex < 0) return;
            var t = _clonar(_itens[_lista.SelectedIndex]);
            _renomear(t, NomeLivre(_nome(t) + " (cópia)"));
            _itens.Add(t);
            Atualizar(_itens.Count - 1);
        };
        bExcluir.Click += (s, e) =>
        {
            int i = _lista.SelectedIndex;
            if (i < 0 || _itens.Count <= 1) return; // o catálogo nunca fica vazio
            _itens.RemoveAt(i);
            Atualizar(Math.Min(i, _itens.Count - 1));
        };
        ok.Click += (s, e) =>
        {
            bool vazio = _itens.Any(t => string.IsNullOrWhiteSpace(_nome(t)));
            var repetidos = _itens.GroupBy(t => _nome(t).Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (vazio || repetidos.Count > 0)
            {
                MessageBox.Show(vazio ? "Há item sem nome." : "Nomes repetidos: " + string.Join(", ", repetidos), "Armadura",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };

        var direita = new Panel { Dock = DockStyle.Fill };
        direita.Controls.Add(_grid);
        Controls.Add(direita);
        Controls.Add(esquerda);
        Controls.Add(botoes);
        Atualizar(0);
    }

    private string NomeLivre(string baseNome)
    {
        string n = baseNome;
        for (int k = 2; _itens.Any(t => string.Equals(_nome(t), n, StringComparison.OrdinalIgnoreCase)); k++) n = $"{baseNome} {k}";
        return n;
    }

    private void Atualizar(int selecionar)
    {
        _lista.BeginUpdate();
        _lista.Items.Clear();
        foreach (var t in _itens) _lista.Items.Add($"{_nome(t)}  —  {_resumo(t)}");
        _lista.EndUpdate();
        if (selecionar >= 0 && selecionar < _itens.Count) _lista.SelectedIndex = selecionar;
    }
}

internal static class Catalogos
{
    public static FormCatalogo<TipoConsolo> Consolos(IEnumerable<TipoConsolo> tipos) => new FormCatalogo<TipoConsolo>(
        "Armadura — consolos padrão", tipos, t => t.Clonar(), () => new TipoConsolo(), t => t.Nome, (t, n) => t.Nome = n, t => t.Resumo,
        t => new PropriedadesConsolo(t));

    public static FormCatalogo<TipoInserto> Insertos(IEnumerable<TipoInserto> tipos) => new FormCatalogo<TipoInserto>(
        "Armadura — insertos metálicos padrão", tipos, t => t.Clonar(), () => new TipoInserto(), t => t.Nome, (t, n) => t.Nome = n, t => t.Resumo,
        t => new PropriedadesInserto(t));
}
