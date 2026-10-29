using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Armadura.Core.Templates.PilarPreMoldado;

namespace Armadura.Cad.Paleta;

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

    [Category(G4), DisplayName("Costura: estribos horizontais")] public int CostN { get => T.CostN; set => T.CostN = Math.Max(0, value); }
    [Category(G4), DisplayName("Costura: bitola (mm)")] public double CostPhi { get => T.CostPhi; set => T.CostPhi = value; }
    [Category(G4), DisplayName("Estribos verticais (por consolo)")] public int VertN { get => T.VertN; set => T.VertN = Math.Max(0, value); }
    [Category(G4), DisplayName("Estribos verticais: bitola (mm)")] public double VertPhi { get => T.VertPhi; set => T.VertPhi = value; }

    [Category(G5), DisplayName("Barras (por consolo)")] public int InfN { get => T.InfN; set => T.InfN = Math.Max(0, value); }
    [Category(G5), DisplayName("Bitola (mm)")] public double InfPhi { get => T.InfPhi; set => T.InfPhi = value; }
    [Category(G5), DisplayName("Comprimento dentro do pilar (cm)")] public double InfAnc { get => T.InfAnc; set => T.InfAnc = Math.Max(0, value); }

    [Category(G6), DisplayName("Pinos (por consolo)")] public int PinoN { get => T.PinoN; set => T.PinoN = Math.Max(0, value); }
    [Category(G6), DisplayName("Bitola (mm)")] public double PinoPhi { get => T.PinoPhi; set => T.PinoPhi = value; }
    [Category(G6), DisplayName("Altura acima do consolo (cm)")] public double PinoAcima { get => T.PinoAcima; set => T.PinoAcima = Math.Max(0, value); }
    [Category(G6), DisplayName("Embutimento (cm)")] public double PinoEmb { get => T.PinoEmb; set => T.PinoEmb = Math.Max(1, value); }
    [Category(G6), DisplayName("Pé / dobra (cm)")] public double PinoPe { get => T.PinoPe; set => T.PinoPe = Math.Max(0, value); }

    [Category(G7), DisplayName("No sentido do balanço"), Description("0 = sem almofada.")] public double AlmA { get => T.AlmA; set => T.AlmA = Math.Max(0, value); }
    [Category(G7), DisplayName("No sentido da largura")] public double AlmB { get => T.AlmB; set => T.AlmB = Math.Max(0, value); }
    [Category(G7), DisplayName("Espessura")] public double AlmE { get => T.AlmE; set => T.AlmE = Math.Max(0, value); }
}

/// <summary>
/// ARMCONSOLOS: catálogo de consolos padrão (pedido do usuário, 2026-10-29). Edita cópias; o OK grava na
/// configuração do usuário e atualiza a lista do template do pilar.
/// </summary>
internal sealed class FormConsolos : Form
{
    private readonly List<TipoConsolo> _tipos;
    private readonly ListBox _lista = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly PropertyGrid _grid = new PropertyGrid { Dock = DockStyle.Fill, ToolbarVisible = false, PropertySort = PropertySort.Categorized };

    public List<TipoConsolo> Resultado => _tipos;

    public FormConsolos(IEnumerable<TipoConsolo> tipos)
    {
        _tipos = tipos.Select(t => t.Clonar()).ToList();
        Text = "Armadura — consolos padrão";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, 560);
        ShowInTaskbar = false;
        MinimizeBox = false;

        var esquerda = new Panel { Dock = DockStyle.Left, Width = 250 };
        var botoesLista = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(2) };
        var novo = new Button { Text = "Novo", Width = 75, Height = 26 };
        var duplicar = new Button { Text = "Duplicar", Width = 75, Height = 26 };
        var excluir = new Button { Text = "Excluir", Width = 75, Height = 26 };
        botoesLista.Controls.AddRange(new Control[] { novo, duplicar, excluir });
        esquerda.Controls.Add(_lista);
        esquerda.Controls.Add(botoesLista);

        var botoes = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft, Padding = new Padding(4) };
        var cancelar = new Button { Text = "Cancelar", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "OK", Width = 90, Height = 28 };
        botoes.Controls.Add(cancelar);
        botoes.Controls.Add(ok);
        CancelButton = cancelar;

        _lista.SelectedIndexChanged += (s, e) =>
            _grid.SelectedObject = _lista.SelectedIndex >= 0 ? new PropriedadesConsolo(_tipos[_lista.SelectedIndex]) : null;
        _grid.PropertyValueChanged += (s, e) => Atualizar(_lista.SelectedIndex);
        novo.Click += (s, e) => { _tipos.Add(new TipoConsolo { Nome = NomeLivre("Novo consolo") }); Atualizar(_tipos.Count - 1); };
        duplicar.Click += (s, e) =>
        {
            if (_lista.SelectedIndex < 0) return;
            var c = _tipos[_lista.SelectedIndex].Clonar();
            c.Nome = NomeLivre(c.Nome + " (cópia)");
            _tipos.Add(c);
            Atualizar(_tipos.Count - 1);
        };
        excluir.Click += (s, e) =>
        {
            int i = _lista.SelectedIndex;
            if (i < 0 || _tipos.Count <= 1) return; // o catálogo nunca fica vazio
            _tipos.RemoveAt(i);
            Atualizar(Math.Min(i, _tipos.Count - 1));
        };
        ok.Click += (s, e) =>
        {
            var vazios = _tipos.Where(t => string.IsNullOrWhiteSpace(t.Nome)).ToList();
            var repetidos = _tipos.GroupBy(t => t.Nome.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (vazios.Count > 0 || repetidos.Count > 0)
            {
                MessageBox.Show(vazios.Count > 0 ? "Há consolo sem nome." : "Nomes repetidos: " + string.Join(", ", repetidos), "Armadura",
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
        for (int k = 2; _tipos.Any(t => string.Equals(t.Nome, n, StringComparison.OrdinalIgnoreCase)); k++) n = $"{baseNome} {k}";
        return n;
    }

    private void Atualizar(int selecionar)
    {
        _lista.BeginUpdate();
        _lista.Items.Clear();
        foreach (var t in _tipos) _lista.Items.Add($"{t.Nome}  —  {t.Resumo}");
        _lista.EndUpdate();
        if (selecionar >= 0 && selecionar < _tipos.Count) _lista.SelectedIndex = selecionar;
    }
}
