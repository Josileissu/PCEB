using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Armadura.Cad.Infra;

namespace Armadura.Cad.Paleta;

/// <summary>
/// Propriedades rápidas (ARMPALETARAPIDA): janela pequena, flutuante, ao lado do cursor, com o mesmo
/// PropertyGrid da paleta "Armadura". Só o 1º grupo vem aberto (os outros ficam recolhidos e guardam o
/// estado que o usuário der). Acompanha a seleção e some quando ela fica vazia, com Esc ou no X.
/// </summary>
internal sealed class PaletaRapida : Form
{
    private static PaletaRapida _janela;
    private static Size _tamanho = new Size(300, 330);

    private readonly PropertyGrid _grid;
    private readonly Label _status;
    private readonly Dictionary<string, bool> _abertos = new Dictionary<string, bool>();
    private Document _doc;

    /// <summary>Janela aberta agora (para o autoteste).</summary>
    internal static PaletaRapida Atual => _janela != null && !_janela.IsDisposed && _janela.Visible ? _janela : null;

    internal object[] Objetos => _grid.SelectedObjects;
    internal string Status => _status.Text;
    internal IReadOnlyList<(string Grupo, bool Aberto)> GruposParaTeste => Grupos().Select(g => (g.Label, g.Expanded)).ToList();

    /// <summary>Mostra a janela para a seleção atual; false se a seleção não tem nada do plugin.</summary>
    public static bool Mostrar(Document doc)
    {
        var objetos = PaletaArmadura.LerSelecao(doc, out var status);
        if (objetos.Length == 0) return false;
        if (_janela == null || _janela.IsDisposed) _janela = new PaletaRapida();
        var j = _janela;
        j.Ligar(doc);
        if (!j.Visible)
        {
            j.Size = _tamanho;
            j.Location = PertoDoCursor(j.Size);
            CadApp.ShowModelessDialog(j);
        }
        // depois de aberta: com a janela escondida o PropertyGrid não tem itens para abrir/recolher os grupos
        j.Exibir(objetos, status);
        return true;
    }

    public static void Fechar() { if (_janela != null && !_janela.IsDisposed) _janela.Ocultar(); }

    private PaletaRapida()
    {
        Text = "Armadura";
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(220, 160);
        KeyPreview = true;

        _grid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            PropertySort = PropertySort.Categorized,
            ToolbarVisible = false,
            HelpVisible = false,
        };
        _grid.PropertyValueChanged += (s, e) => Aplicar();
        _status = new Label { Dock = DockStyle.Top, Height = 22, Padding = new Padding(3), AutoEllipsis = true };
        Controls.Add(_grid);
        Controls.Add(_status);
        PaletaArmadura.AplicarTema(this, _status, _grid);

        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { e.Handled = true; Ocultar(); } };
        FormClosing += (s, e) =>
        {
            // o X só esconde: a janela é reaproveitada no próximo ARMPALETARAPIDA
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Ocultar(); }
        };
        ResizeEnd += (s, e) => _tamanho = Size;
        CadApp.DocumentManager.DocumentActivated += (s, e) => { if (e.Document != _doc) Ocultar(); };
    }

    protected override bool ShowWithoutActivation => true;

    private static Point PertoDoCursor(Size tam)
    {
        var c = Cursor.Position;
        var tela = Screen.FromPoint(c).WorkingArea;
        int x = c.X + 24, y = c.Y + 16;
        if (x + tam.Width > tela.Right) x = c.X - 24 - tam.Width;
        if (y + tam.Height > tela.Bottom) y = tela.Bottom - tam.Height;
        return new Point(Math.Max(tela.Left, x), Math.Max(tela.Top, y));
    }

    private void Ligar(Document doc)
    {
        if (_doc == doc) return;
        Desligar();
        _doc = doc;
        if (doc == null) return;
        doc.ImpliedSelectionChanged += AoMudarSelecao;
        doc.CommandEnded += AoTerminarComando;
    }

    private void Desligar()
    {
        if (_doc == null) return;
        try
        {
            _doc.ImpliedSelectionChanged -= AoMudarSelecao;
            _doc.CommandEnded -= AoTerminarComando;
        }
        catch (Exception ex) { Log.Erro("PaletaRapida.Desligar", ex); }
        _doc = null;
    }

    private void AoMudarSelecao(object sender, EventArgs e) { if (!PaletaArmadura.Aplicando) Recarregar(); }

    // grips, STRETCH, ARMPROP… podem ter mudado os valores
    private void AoTerminarComando(object sender, CommandEventArgs e) { if (!PaletaArmadura.Aplicando) Recarregar(); }

    /// <summary>Relê a seleção; vazia (ou sem nada do plugin) → a janela some, como o Quick Properties.</summary>
    internal void Recarregar()
    {
        if (!Visible) return;
        try
        {
            var objetos = PaletaArmadura.LerSelecao(_doc, out var status);
            if (objetos.Length == 0) { Ocultar(); return; }
            Exibir(objetos, status);
        }
        catch (Exception ex) { Log.Erro("PaletaRapida.Recarregar", ex); }
    }

    private void Exibir(object[] objetos, string status)
    {
        GuardarGrupos();
        _grid.SelectedObjects = objetos;
        _status.Text = status;
        Text = objetos.Length == 1 ? Titulo(objetos[0]) : $"{Titulo(objetos[0])} ({objetos.Length})";
        AbrirGrupos();
    }

    private static string Titulo(object o) => o switch
    {
        PropriedadesBarra => "Armadura",
        PropriedadesFaixa => "Faixa",
        PropriedadesTabela => "Relação do aço",
        PropriedadesDetalhamento => "Detalhamento",
        _ => "Armadura",
    };

    private IEnumerable<GridItem> Grupos()
    {
        var item = _grid.SelectedGridItem;
        if (item == null) return Enumerable.Empty<GridItem>();
        while (item.Parent != null) item = item.Parent;
        return item.GridItems.Cast<GridItem>().Where(g => g.GridItemType == GridItemType.Category);
    }

    private void GuardarGrupos()
    {
        foreach (var g in Grupos()) _abertos[g.Label] = g.Expanded;
    }

    /// <summary>Grupo já visto: como o usuário deixou; novo: só o primeiro aberto.</summary>
    private void AbrirGrupos()
    {
        bool primeiro = true;
        foreach (var g in Grupos())
        {
            g.Expanded = _abertos.TryGetValue(g.Label, out var aberto) ? aberto : primeiro;
            primeiro = false;
        }
    }

    private void Aplicar()
    {
        PaletaArmadura.AplicarObjetos(_grid.SelectedObjects);
        Recarregar();
        PaletaArmadura.Recarregar();
    }

    private void Ocultar()
    {
        if (!Visible) return;
        GuardarGrupos();
        Hide();
        Desligar();
        try { CadUtils.SetFocusToDwgView(); } catch (Exception ex) { Log.Erro("PaletaRapida.Foco", ex); }
    }
}
