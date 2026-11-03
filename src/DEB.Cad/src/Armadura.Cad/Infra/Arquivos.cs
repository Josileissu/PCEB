using System;
using System.Windows.Forms;

namespace Armadura.Cad.Infra;

/// <summary>Escolha e abertura de arquivos pelos comandos (antes repetido na importação e nos modelos dos templates).</summary>
internal static class Arquivos
{
    /// <summary>
    /// Caminho de arquivo: diálogo, ou digitado na linha de comando quando há script ativo (CMDACTIVE bit 4) ou FILEDIA = 0 —
    /// o autoteste não mexe no FILEDIA, que é gravado no perfil e travaria os diálogos do usuário. null = cancelado.
    /// </summary>
    public static string Pedir(Editor ed, bool salvar, string titulo, string filtro, string extensao, string sugestao = "")
    {
        bool dialogo = true;
        try { dialogo = Convert.ToInt32(CadApp.GetSystemVariable("FILEDIA")) != 0; } catch { }
        try { if ((Convert.ToInt32(CadApp.GetSystemVariable("CMDACTIVE")) & 4) != 0) dialogo = false; } catch { }
        if (dialogo)
        {
            using FileDialog d = salvar ? new SaveFileDialog { FileName = sugestao } : new OpenFileDialog();
            d.Title = titulo;
            d.Filter = filtro;
            return d.ShowDialog() == DialogResult.OK ? d.FileName : null;
        }
        var r = ed.GetString(new PromptStringOptions($"\nArquivo {extensao}:") { AllowSpaces = true });
        return r.Status == PromptStatus.OK && r.StringResult.Trim().Length > 0 ? r.StringResult.Trim().Trim('"') : null;
    }

    /// <summary>Abre o arquivo no programa padrão do Windows (Excel, Bloco de Notas…).</summary>
    public static void Abrir(string caminho) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(caminho) { UseShellExecute = true });
}
