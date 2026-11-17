using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using DEB.Core.Documentacao;

namespace DEB.Cad.Pdf;

/// <summary>
/// Formulários de papel do servidor de impressão local (winspool): listar, apagar e criar sem margem. As decisões (o que criar,
/// recriar, substituir e em que ordem) ficam no núcleo (<see cref="FormulariosPapel"/>); aqui só a chamada ao Windows.
/// Abrir com <see cref="Abrir"/>(true) pede SERVER_ACCESS_ADMINISTER: no Windows 10 funcionou sem administrador; se o Windows
/// negar (erro 5), a janela diz para rodar o CAD como administrador (não há contorno).
/// </summary>
internal sealed class FormulariosWindows : IServidorFormularios, IDisposable
{
    private IntPtr _h;

    private FormulariosWindows(IntPtr h) { _h = h; }

    /// <summary>Abre o servidor local (null). <paramref name="alterar"/> = acesso de administração (apagar/criar).</summary>
    public static FormulariosWindows Abrir(bool alterar)
    {
        var pd = new PRINTER_DEFAULTS { DesiredAccess = alterar ? SERVER_ACCESS_ADMINISTER : SERVER_ACCESS_ENUMERATE };
        if (!OpenPrinter(null, out var h, ref pd))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "não abriu o servidor de impressão local");
        return new FormulariosWindows(h);
    }

    public void Dispose()
    {
        if (_h != IntPtr.Zero) { ClosePrinter(_h); _h = IntPtr.Zero; }
    }

    /// <summary>Todos os formulários, na ordem do Windows (EnumForms nível 1).</summary>
    public List<FormularioWindows> Listar()
    {
        var res = new List<FormularioWindows>();
        EnumForms(_h, 1, IntPtr.Zero, 0, out int needed, out _);
        if (needed <= 0) return res;
        IntPtr buf = Marshal.AllocHGlobal(needed);
        try
        {
            if (!EnumForms(_h, 1, buf, needed, out needed, out int n))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "EnumForms falhou");
            int sz = Marshal.SizeOf(typeof(FORM_INFO_1));
            for (int i = 0; i < n; i++)
            {
                var f = (FORM_INFO_1)Marshal.PtrToStructure(new IntPtr(buf.ToInt64() + (long)i * sz), typeof(FORM_INFO_1));
                res.Add(new FormularioWindows
                {
                    Nome = f.pName, Largura = f.Size.cx, Altura = f.Size.cy,
                    Esquerda = f.ImageableArea.left, Topo = f.ImageableArea.top, Direita = f.ImageableArea.right, Base = f.ImageableArea.bottom,
                    Tipo = f.Flags == 1 ? TipoFormulario.Interno : f.Flags == 2 ? TipoFormulario.Impressora : TipoFormulario.Usuario,
                });
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        return res;
    }

    public int Apagar(string nome) => DeleteForm(_h, nome) ? 0 : Marshal.GetLastWin32Error();

    public int Adicionar(string nome, int largura, int altura)
    {
        var f = new FORM_INFO_1
        {
            Flags = 0, pName = nome,
            Size = new SIZEL { cx = largura, cy = altura },
            ImageableArea = new RECTL { left = 0, top = 0, right = largura, bottom = altura },
        };
        return AddForm(_h, 1, ref f) ? 0 : Marshal.GetLastWin32Error();
    }

    /// <summary>Nomes dos papéis que o driver da impressora lista (DeviceCapabilities DC_PAPERNAMES); vazio se não der.</summary>
    public static HashSet<string> PapeisDaImpressora(string impressora)
    {
        var res = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            int n = DeviceCapabilities(impressora, null, 16, IntPtr.Zero, IntPtr.Zero);
            if (n <= 0) return res;
            IntPtr buf = Marshal.AllocHGlobal(n * 128);
            try
            {
                DeviceCapabilities(impressora, null, 16, buf, IntPtr.Zero);
                for (int i = 0; i < n; i++)
                {
                    var s = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + i * 128), 64);
                    int z = s.IndexOf('\0');
                    res.Add(z >= 0 ? s.Substring(0, z) : s);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch (Exception ex) { Infra.Log.Erro("ARMPDFLOTE: papéis da impressora " + impressora, ex); }
        return res;
    }

    // ---------------- winspool ----------------

    private const uint SERVER_ACCESS_ADMINISTER = 0x1;
    private const uint SERVER_ACCESS_ENUMERATE = 0x2;

    [StructLayout(LayoutKind.Sequential)] private struct SIZEL { public int cx; public int cy; }
    [StructLayout(LayoutKind.Sequential)] private struct RECTL { public int left; public int top; public int right; public int bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FORM_INFO_1 { public uint Flags; public string pName; public SIZEL Size; public RECTL ImageableArea; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PRINTER_DEFAULTS { public IntPtr pDatatype; public IntPtr pDevMode; public uint DesiredAccess; }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "OpenPrinterW")]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, ref PRINTER_DEFAULTS pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr h);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "AddFormW")]
    private static extern bool AddForm(IntPtr h, uint level, ref FORM_INFO_1 form);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "DeleteFormW")]
    private static extern bool DeleteForm(IntPtr h, string name);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "EnumFormsW")]
    private static extern bool EnumForms(IntPtr h, uint level, IntPtr buf, int cb, out int needed, out int returned);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "DeviceCapabilitiesW")]
    private static extern int DeviceCapabilities(string device, string port, ushort cap, IntPtr output, IntPtr devmode);
}
