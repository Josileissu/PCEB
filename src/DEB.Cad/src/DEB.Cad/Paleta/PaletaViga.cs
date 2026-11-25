using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DEB.Cad.Infra;
using DEB.Core.Elementos.Viga.PreMoldada;
using DEB.Core.Motor;

namespace DEB.Cad.Paleta;

/// <summary>
/// Aba "Viga" da paleta Armadura (D51, 25/11/2026: "seria bom se esses parâmetros estivessem numa paleta de propriedades, talvez
/// específica para vigas"): selecionando o detalhamento de uma viga pré-moldada ou qualquer parte da análise dela, mostra os
/// parâmetros do template (seção, dentes, aba, ressalto, aberturas, armaduras…), agrupados como na janela do template. As mudanças
/// ficam pendentes até "Atualizar viga", que chama o ARMVIGAATUALIZAR: a análise, a viga armada e a memória são refeitas no lugar
/// (ou, num detalhamento sem análise, o detalhamento é redesenhado).
/// </summary>
internal static class PaletaViga
{
    private static PropertyGrid _grid;
    private static Label _status;
    private static Button _atualizar;
    private static (string Analise, ObjectId Bloco, ValoresTemplate Valores)? _atual;

    /// <summary>O que o "Atualizar viga" mandou aplicar (lido pelo ARMVIGAATUALIZAR).</summary>
    internal static (string Analise, ObjectId Bloco, ValoresTemplate Valores)? Pendente { get; set; }

    public static Control Painel()
    {
        var painel = new Panel { Dock = DockStyle.Fill };
        _grid = new PropertyGrid { Dock = DockStyle.Fill, PropertySort = PropertySort.Categorized, ToolbarVisible = false, HelpVisible = true };
        _status = new Label { Dock = DockStyle.Top, Height = 34, Padding = new Padding(4), AutoEllipsis = true, Text = "Selecione o detalhamento de uma viga pré-moldada ou a análise dela." };
        _atualizar = new Button { Dock = DockStyle.Bottom, Height = 30, Text = "Atualizar viga", Enabled = false };
        _atualizar.Click += (s, e) => Atualizar();
        _grid.PropertyValueChanged += (s, e) => { if (_atual != null) _status.Text = "Alterado — clique em \"Atualizar viga\" para refazer."; };
        painel.Controls.Add(_grid);
        painel.Controls.Add(_status);
        painel.Controls.Add(_atualizar);
        return painel;
    }

    /// <summary>Lê a viga da seleção atual (chamado com a paleta de propriedades).</summary>
    public static void Recarregar(Document doc)
    {
        if (_grid == null) return;
        try
        {
            _atual = null;
            if (doc != null)
            {
                var sel = doc.Editor.SelectImplied();
                if (sel.Status == PromptStatus.OK && sel.Value != null)
                    foreach (var oid in sel.Value.GetObjectIds())
                    {
                        _atual = ComandosAnaliseViga.VigaDoObjeto(doc.Database, oid);
                        if (_atual != null) break;
                    }
            }
            if (_atual == null)
            {
                _grid.SelectedObject = null;
                _atualizar.Enabled = false;
                _status.Text = "Selecione o detalhamento de uma viga pré-moldada ou a análise dela.";
                return;
            }
            var v = _atual.Value.Valores;
            _grid.SelectedObject = new ParametrosEditaveis(new TemplateVigaPreMoldada(), v);
            _atualizar.Enabled = true;
            _status.Text = $"Viga {v["Nome"]} — " + (_atual.Value.Analise != null ? "com análise (recalcula tudo)" : "detalhamento (redesenha)");
        }
        catch (Exception ex) { Log.Erro("PaletaViga.Recarregar", ex); }
    }

    private static void Atualizar()
    {
        var doc = CadApp.DocumentManager.MdiActiveDocument;
        if (doc == null || _atual == null) return;
        Pendente = _atual;
        doc.SendStringToExecute("ARMVIGAATUALIZAR\n", true, false, true);
    }
}
