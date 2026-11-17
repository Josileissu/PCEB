using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DEB.Cad.Infra;
using Microsoft.Win32;

namespace DEB.Cad.Pdf;

/// <summary>
/// Salvamento automático da impressora PDF24 durante o lote (ARMPDFLOTE), sem o diálogo "onde salvar".
///
/// O PDF24 Creator (conferido na 11.30) lê as opções da impressora em <c>HKLM\SOFTWARE\PDF24\Services\PDF</c> e as do usuário em
/// <c>HKCU\Software\PDF24\Services\PDF</c>, que valem por cima e são relidas a cada trabalho de impressão. Durante o lote este
/// código grava em HKCU (sem administrador):
///   Handler = "autoSave" (em vez do assistente), AutoSaveDir = pasta temporária do lote, AutoSaveFilename = "$fileName",
///   AutoSaveOpenDir = 0, AutoSaveShowProgress = 0, AutoSaveUseFileChooser = 0, AutoSaveUseFileCmd = 0, AutoSaveOverwriteFile = 1.
/// No fim (também em erro ou cancelamento) os valores anteriores voltam: valor que não existia é apagado, e as chaves criadas
/// pelo lote também. Antes de mexer, o estado anterior vai para %AppData%\PCEB\DEB.Cad\pdf24_restaurar.txt; se o CAD fechar no
/// meio do lote, o plugin restaura a partir dele ao carregar (<see cref="RestaurarPendente"/>).
/// </summary>
internal sealed class Pdf24 : IDisposable
{
    private const string Chave = @"Software\PDF24\Services\PDF";
    private static string ArquivoBackup => Path.Combine(Log.PastaDados, "pdf24_restaurar.txt");

    private static readonly (string Nome, object Valor, RegistryValueKind Tipo)[] Valores =
    {
        ("Handler", "autoSave", RegistryValueKind.String),
        ("AutoSaveDir", null, RegistryValueKind.String),            // preenchido com a pasta do lote
        ("AutoSaveFilename", "$fileName", RegistryValueKind.String),
        ("AutoSaveOpenDir", 0, RegistryValueKind.DWord),
        ("AutoSaveShowProgress", 0, RegistryValueKind.DWord),
        ("AutoSaveUseFileChooser", 0, RegistryValueKind.DWord),
        ("AutoSaveUseFileCmd", 0, RegistryValueKind.DWord),
        ("AutoSaveOverwriteFile", 1, RegistryValueKind.DWord),
    };

    public string Pasta { get; }
    private bool _restaurado;

    private Pdf24(string pasta) { Pasta = pasta; }

    /// <summary>A impressora é a PDF24 (pelo nome ou pelo driver)?</summary>
    public static bool EhPdf24(string impressora) =>
        impressora != null && impressora.IndexOf("PDF24", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>Liga o salvamento automático na pasta dada. Devolve o objeto que restaura no Dispose.</summary>
    public static Pdf24 Ativar(string pasta)
    {
        RestaurarPendente(); // sobra de um lote interrompido: volta ao estado do usuário antes de guardar de novo
        Directory.CreateDirectory(pasta);
        var linhas = new List<string>();
        bool existiaServices = Registry.CurrentUser.OpenSubKey(@"Software\PDF24\Services") != null;
        bool existiaPdf = Registry.CurrentUser.OpenSubKey(Chave) != null;
        linhas.Add("#chave\tServices\t" + (existiaServices ? "1" : "0"));
        linhas.Add("#chave\tPDF\t" + (existiaPdf ? "1" : "0"));
        using (var k = Registry.CurrentUser.OpenSubKey(Chave))
        {
            foreach (var v in Valores)
            {
                object atual = k?.GetValue(v.Nome, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (atual == null) linhas.Add(v.Nome + "\t-\t");
                else linhas.Add(v.Nome + "\t" + k.GetValueKind(v.Nome) + "\t" + Codificar(atual));
            }
        }
        File.WriteAllLines(ArquivoBackup, linhas, Encoding.UTF8);
        using (var k = Registry.CurrentUser.CreateSubKey(Chave))
        {
            foreach (var v in Valores)
                k.SetValue(v.Nome, v.Nome == "AutoSaveDir" ? pasta : v.Valor, v.Tipo);
        }
        Log.Info($"PDF24: salvamento automático ligado em HKCU\\{Chave} (pasta {pasta}).");
        return new Pdf24(pasta);
    }

    public void Dispose()
    {
        if (_restaurado) return;
        _restaurado = true;
        RestaurarPendente();
    }

    /// <summary>Se houver um estado guardado (lote em andamento ou interrompido), devolve os valores do usuário.</summary>
    public static void RestaurarPendente()
    {
        try
        {
            if (!File.Exists(ArquivoBackup)) return;
            var linhas = File.ReadAllLines(ArquivoBackup, Encoding.UTF8);
            bool existiaServices = linhas.Any(l => l == "#chave\tServices\t1");
            bool existiaPdf = linhas.Any(l => l == "#chave\tPDF\t1");
            using (var k = Registry.CurrentUser.OpenSubKey(Chave, true))
            {
                if (k != null)
                {
                    foreach (var l in linhas.Where(l => !l.StartsWith("#")))
                    {
                        var p = l.Split('\t');
                        if (p.Length < 3) continue;
                        if (p[1] == "-") { k.DeleteValue(p[0], false); continue; }
                        var tipo = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), p[1]);
                        k.SetValue(p[0], Decodificar(p[2], tipo), tipo);
                    }
                }
            }
            if (!existiaPdf) ApagarSeVazia(Chave);
            if (!existiaServices) ApagarSeVazia(@"Software\PDF24\Services");
            File.Delete(ArquivoBackup);
            Log.Info("PDF24: configuração do usuário restaurada.");
        }
        catch (Exception ex) { Log.Erro("PDF24: restaurar configuração", ex); }
    }

    private static void ApagarSeVazia(string caminho)
    {
        using (var k = Registry.CurrentUser.OpenSubKey(caminho))
        {
            if (k == null || k.ValueCount > 0 || k.SubKeyCount > 0) return;
        }
        Registry.CurrentUser.DeleteSubKey(caminho, false);
    }

    private static string Codificar(object v) => v switch
    {
        string[] m => Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\0", m))),
        byte[] b => Convert.ToBase64String(b),
        _ => Convert.ToBase64String(Encoding.UTF8.GetBytes(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture))),
    };

    private static object Decodificar(string s, RegistryValueKind tipo)
    {
        var bytes = Convert.FromBase64String(s);
        var texto = Encoding.UTF8.GetString(bytes);
        return tipo switch
        {
            RegistryValueKind.DWord => int.TryParse(texto, out var i) ? i : unchecked((int)uint.Parse(texto)),
            RegistryValueKind.QWord => long.Parse(texto),
            RegistryValueKind.MultiString => texto.Split('\0'),
            RegistryValueKind.Binary => bytes,
            _ => texto,
        };
    }
}
