# criar_folhas_pdf24.ps1 - cria/ajusta os formularios de papel do Windows usados pelo ARMPDFLOTE (impressora PDF24).
#
# Faz o mesmo que o botao "Criar folhas na impressora" da janela do ARMPDFLOTE, fora do CAD (DEB.Cad):
#   - lista de folhas = a do %AppData%\PCEB\DEB.Cad\pdflote.json (campo "Formatos"); sem o arquivo, a lista padrao abaixo;
#   - formularios sem margem, em ordem crescente de area (o Windows lista os forms de usuario na ordem em que foram criados:
#     do primeiro fora de ordem em diante, os forms de usuario da lista sao apagados e criados de novo);
#   - form do Windows (BUILTIN) nunca e alterado; form do driver de uma impressora (PRINTER) com o mesmo nome e outro tamanho
#     so e substituido com autorizacao (pergunta, ou -SubstituirDoDriver).
# Recriar um formulario muda o codigo interno do papel (UserNNNNN): DWGs que ja usavam papeis da PDF24 podem abrir com o
# papel errado no page setup (confira). O ARMPDFLOTE procura a folha pelo nome e nao e afetado.
#
# Uso (PowerShell comum; no Windows 10 funcionou sem administrador - se der "acesso negado", abra como administrador):
#   powershell -ExecutionPolicy Bypass -File criar_folhas_pdf24.ps1            # mostra o plano e pergunta
#   powershell -ExecutionPolicy Bypass -File criar_folhas_pdf24.ps1 -Listar    # so lista, nao altera nada
#   ... -Sim                    # nao pergunta (aplica o plano; form do driver so com -SubstituirDoDriver)
#   ... -Config <pdflote.json>  # outra lista de folhas
#   ... -Impressora PDF24       # impressora conferida no fim (papeis que o driver lista)
param(
    [switch]$Listar,
    [switch]$Sim,
    [switch]$SubstituirDoDriver,
    [string]$Config = (Join-Path $env:APPDATA 'PCEB\DEB.Cad\pdflote.json'),
    [string]$Impressora = 'PDF24'
)

$ErrorActionPreference = 'Stop'
if (-not ('DebFormularios' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class DebFormularios {
    [StructLayout(LayoutKind.Sequential)] public struct SIZEL { public int cx; public int cy; }
    [StructLayout(LayoutKind.Sequential)] public struct RECTL { public int left; public int top; public int right; public int bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct FORM_INFO_1 { public uint Flags; public string pName; public SIZEL Size; public RECTL ImageableArea; }
    [StructLayout(LayoutKind.Sequential)]
    public struct PRINTER_DEFAULTS { public IntPtr pDatatype; public IntPtr pDevMode; public uint DesiredAccess; }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "OpenPrinterW")]
    static extern bool OpenPrinter(string name, out IntPtr h, ref PRINTER_DEFAULTS pd);
    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool ClosePrinter(IntPtr h);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "AddFormW")]
    static extern bool AddForm(IntPtr h, uint level, ref FORM_INFO_1 form);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "DeleteFormW")]
    static extern bool DeleteForm(IntPtr h, string name);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "EnumFormsW")]
    static extern bool EnumForms(IntPtr h, uint level, IntPtr buf, int cb, out int needed, out int returned);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "DeviceCapabilitiesW")]
    static extern int DeviceCapabilities(string device, string port, ushort cap, IntPtr output, IntPtr devmode);

    // alterar = SERVER_ACCESS_ADMINISTER (1); senao SERVER_ACCESS_ENUMERATE (2)
    public static IntPtr Abrir(bool alterar) {
        PRINTER_DEFAULTS pd = new PRINTER_DEFAULTS(); pd.DesiredAccess = alterar ? 1u : 2u;
        IntPtr h;
        if (!OpenPrinter(null, out h, ref pd))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "OpenPrinter(servidor local)");
        return h;
    }

    public static FORM_INFO_1[] Listar(IntPtr h) {
        int needed, n;
        EnumForms(h, 1, IntPtr.Zero, 0, out needed, out n);
        IntPtr buf = Marshal.AllocHGlobal(needed);
        try {
            if (!EnumForms(h, 1, buf, needed, out needed, out n))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "EnumForms");
            var r = new FORM_INFO_1[n];
            int sz = Marshal.SizeOf(typeof(FORM_INFO_1));
            for (int i = 0; i < n; i++)
                r[i] = (FORM_INFO_1)Marshal.PtrToStructure(new IntPtr(buf.ToInt64() + (long)i * sz), typeof(FORM_INFO_1));
            return r;
        } finally { Marshal.FreeHGlobal(buf); }
    }

    // 0 = certo; senao o codigo de erro do Windows
    public static int Apagar(IntPtr h, string nome) { return DeleteForm(h, nome) ? 0 : Marshal.GetLastWin32Error(); }

    public static int Criar(IntPtr h, string nome, int largura, int altura) {
        FORM_INFO_1 f = new FORM_INFO_1();
        f.Flags = 0; f.pName = nome;
        f.Size.cx = largura; f.Size.cy = altura;
        f.ImageableArea.left = 0; f.ImageableArea.top = 0; f.ImageableArea.right = largura; f.ImageableArea.bottom = altura;
        return AddForm(h, 1, ref f) ? 0 : Marshal.GetLastWin32Error();
    }

    // DC_PAPERNAMES = 16 (64 caracteres cada)
    public static string[] PapeisDaImpressora(string impressora) {
        int n = DeviceCapabilities(impressora, null, 16, IntPtr.Zero, IntPtr.Zero);
        if (n <= 0) return new string[0];
        IntPtr buf = Marshal.AllocHGlobal(n * 128);
        try {
            DeviceCapabilities(impressora, null, 16, buf, IntPtr.Zero);
            var r = new string[n];
            for (int i = 0; i < n; i++) {
                string s = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + i * 128), 64);
                int z = s.IndexOf('\0'); r[i] = z >= 0 ? s.Substring(0, z) : s;
            }
            return r;
        } finally { Marshal.FreeHGlobal(buf); }
    }
}
'@
}

$tipos = @{ 0 = 'usuario'; 1 = 'Windows'; 2 = 'driver' }
$tolerancia = 50   # milesimos de mm
function Mm([int]$m) { ($m / 1000).ToString('0.#', [Globalization.CultureInfo]::GetCultureInfo('pt-BR')) }

# ---- lista de folhas (mesma do ARMPDFLOTE) ----
$formatos = @()
if (Test-Path -LiteralPath $Config) {
    try {
        $json = [IO.File]::ReadAllText($Config, [Text.Encoding]::UTF8) | ConvertFrom-Json
        foreach ($f in $json.Formatos) { $formatos += [pscustomobject]@{ Nome = [string]$f.Nome; Largura = [double]$f.Largura; Altura = [double]$f.Altura } }
        Write-Host "Lista de folhas: $Config"
    } catch { Write-Host "Nao deu para ler $Config ($($_.Exception.Message)); usando a lista padrao." }
}
if ($formatos.Count -eq 0) {
    foreach ($d in @(@('A4 (ISO A4)', 210, 297), @('A3 (ISO A3)', 420, 295), @('A2 (ISO A2)', 594, 420), @('A1 (ISO A1)', 841, 594),
                     @('A1+', 911.1, 594), @('A1++', 1121.3, 594), @('A0', 1189, 841), @('A0+', 1337.6, 841), @('A0++', 1486.3, 841))) {
        $formatos += [pscustomobject]@{ Nome = $d[0]; Largura = [double]$d[1]; Altura = [double]$d[2] }
    }
    Write-Host 'Lista de folhas: padrao do DEB.Cad'
}
# validos, sem nome repetido, em ordem crescente de area (empate: maior lado; Sort-Object -Stable nao existe no PS 5.1)
$vistos = @{}; $validos = @(); $i = 0
foreach ($f in $formatos) {
    $n = $f.Nome.Trim()
    if (-not $n -or $f.Largura -le 0 -or $f.Altura -le 0 -or $vistos.ContainsKey($n.ToUpperInvariant())) { continue }
    $vistos[$n.ToUpperInvariant()] = 1
    $validos += [pscustomobject]@{ Nome = $n; L = [int][math]::Round($f.Largura * 1000); A = [int][math]::Round($f.Altura * 1000)
                                   Area = [math]::Round($f.Largura * $f.Altura, 3); Maior = [math]::Max($f.Largura, $f.Altura); Pos = $i++ }
}
$ordem = @($validos | Sort-Object Area, Maior, Pos)

# ---- forms existentes ----
$h = [DebFormularios]::Abrir($false)
try { $existentes = [DebFormularios]::Listar($h) } finally { [void][DebFormularios]::ClosePrinter($h) }
$porNome = @{}
for ($k = 0; $k -lt $existentes.Count; $k++) {
    $chave = $existentes[$k].pName.ToUpperInvariant()
    if (-not $porNome.ContainsKey($chave)) { $porNome[$chave] = @{ F = $existentes[$k]; Pos = $k } }
}

if ($Listar) {
    Write-Host "`nFormularios da lista (na ordem do Windows quando existem):"
    foreach ($f in $ordem) {
        $e = $porNome[$f.Nome.ToUpperInvariant()]
        if ($e) { Write-Host ("  {0,-14} {1,8} x {2,-6} mm  {3,-8} posicao {4}" -f $f.Nome, (Mm $e.F.Size.cx), (Mm $e.F.Size.cy), $tipos[[int]$e.F.Flags], $e.Pos) }
        else { Write-Host ("  {0,-14} NAO EXISTE (pede {1} x {2} mm)" -f $f.Nome, (Mm $f.L), (Mm $f.A)) }
    }
    exit 0
}

# ---- plano (mesma regra do DEB.Core: FormulariosPapel.Planejar) ----
function Planejar([bool]$substituir) {
    $passos = @()
    foreach ($f in $ordem) {
        $p = [pscustomobject]@{ Nome = $f.Nome; L = $f.L; A = $f.A; Acao = 'Criar'; Existente = $null; Pos = -1; Motivo = '' }
        $e = $porNome[$f.Nome.ToUpperInvariant()]
        if ($e) {
            $p.Existente = $e.F; $p.Pos = $e.Pos
            $igual = [math]::Abs($e.F.Size.cx - $f.L) -le $tolerancia -and [math]::Abs($e.F.Size.cy - $f.A) -le $tolerancia
            $tem = "$(Mm $e.F.Size.cx) x $(Mm $e.F.Size.cy) mm"
            switch ([int]$e.F.Flags) {
                1 { if ($igual) { $p.Acao = 'Manter' } else { $p.Acao = 'Pular'; $p.Motivo = "ja existe como formulario do Windows com $tem (nao e alterado)" } }
                2 { if ($igual) { $p.Acao = 'Manter' } elseif ($substituir) { $p.Acao = 'Substituir'; $p.Motivo = "o do driver tem $tem" }
                    else { $p.Acao = 'Pular'; $p.Motivo = "form do driver com $tem (substituicao nao autorizada)" } }
                default {
                    $ia = $e.F.ImageableArea
                    $semMargem = $ia.left -eq 0 -and $ia.top -eq 0 -and $ia.right -eq $e.F.Size.cx -and $ia.bottom -eq $e.F.Size.cy
                    if (-not $igual) { $p.Acao = 'Recriar'; $p.Motivo = "tinha $tem" }
                    elseif (-not $semMargem) { $p.Acao = 'Recriar'; $p.Motivo = 'tinha margem' }
                    else { $p.Acao = 'Manter' }
                }
            }
        }
        $passos += $p
    }
    # ordem: forms de usuario que ficam vem antes dos criados e na ordem da lista; do 1o fora de ordem em diante, recria
    $ultima = -1; $quebrou = $false
    foreach ($p in $passos) {
        $adiciona = $p.Acao -in @('Criar', 'Recriar', 'Substituir')
        $ficaUsuario = $adiciona -or ($p.Acao -eq 'Manter' -and $p.Existente -and [int]$p.Existente.Flags -eq 0)
        if (-not $ficaUsuario) { continue }
        if ($p.Acao -ne 'Manter') { $quebrou = $true; continue }
        if (-not $quebrou -and $p.Pos -gt $ultima) { $ultima = $p.Pos; continue }
        $quebrou = $true; $p.Acao = 'Recriar'; $p.Motivo = 'para ficar na ordem'
    }
    return ,$passos
}

$plano = Planejar $true
$conflitos = @($plano | Where-Object { $_.Acao -eq 'Substituir' })
if ($conflitos.Count -gt 0 -and -not $SubstituirDoDriver) {
    Write-Host "`nJa existe formulario instalado pelo driver de uma impressora com o mesmo nome e outro tamanho:"
    foreach ($p in $conflitos) { Write-Host "   $($p.Nome): $($p.Motivo); a lista pede $(Mm $p.L) x $(Mm $p.A) mm" }
    $r = if ($Sim) { 'N' } else { Read-Host 'Pode substituir? (S = apagar o do driver e criar o da lista; N = pular essa folha)' }
    if ($r -notmatch '^[sS]') { $plano = Planejar $false }
}

Write-Host "`nFormularios de papel do Windows (valem para todas as impressoras), sem margem, em ordem crescente:"
foreach ($grupo in @(@('Criar', 'Criar:'), @('Recriar', 'Recriar (apagar e criar de novo):'), @('Substituir', 'Substituir form do driver:'),
                     @('Manter', 'Ja estao certos (nao mexe):'), @('Pular', 'Nao vai mexer (conflito):'))) {
    $ps = @($plano | Where-Object { $_.Acao -eq $grupo[0] })
    if ($ps.Count -eq 0) { continue }
    Write-Host $grupo[1]
    foreach ($p in $ps) { Write-Host ("   {0} ({1} x {2} mm){3}" -f $p.Nome, (Mm $p.L), (Mm $p.A), $(if ($p.Motivo) { " - $($p.Motivo)" } else { '' })) }
}
$fazer = @($plano | Where-Object { $_.Acao -in @('Criar', 'Recriar', 'Substituir') })
if ($fazer.Count -eq 0) { Write-Host "`nNada a fazer: as folhas ja estao certas."; exit 0 }
if (@($plano | Where-Object { $_.Acao -in @('Recriar', 'Substituir') }).Count -gt 0) {
    Write-Host "`nAtencao: recriar um formulario muda o codigo interno do papel. DWGs que ja usavam papeis da PDF24 podem abrir"
    Write-Host 'com o papel errado no page setup: confira o papel deles. O ARMPDFLOTE procura a folha pelo nome e nao e afetado.'
}
if (-not $Sim) {
    $r = Read-Host "`nContinuar? (S/N)"
    if ($r -notmatch '^[sS]') { Write-Host 'Nada foi alterado.'; exit 0 }
}

# ---- aplicar: um passo por vez (apaga e cria em seguida) ----
try { $h = [DebFormularios]::Abrir($true) }
catch {
    $e = $_.Exception; if ($e.InnerException) { $e = $e.InnerException }
    Write-Host "ERRO: $($e.Message)"
    Write-Host 'Sem permissao: abra o PowerShell como administrador e rode de novo.'
    exit 1
}
$cont = @{ Criar = 0; Recriar = 0; Substituir = 0; Erro = 0 }; $semPermissao = $false
try {
    foreach ($p in $plano) {
        if ($p.Acao -notin @('Criar', 'Recriar', 'Substituir')) { continue }
        if ($semPermissao) { Write-Host "[PULADO] $($p.Nome) (sem permissao)"; continue }
        if ($p.Acao -ne 'Criar') {
            $c = [DebFormularios]::Apagar($h, $p.Existente.pName)
            if ($c -ne 0) { Write-Host "[ERRO] $($p.Nome): nao apagou o antigo (erro $c)"; $cont.Erro++; if ($c -eq 5) { $semPermissao = $true }; continue }
        }
        $c = [DebFormularios]::Criar($h, $p.Nome, $p.L, $p.A)
        if ($c -ne 0) {
            Write-Host "[ERRO] $($p.Nome): nao criou (erro $c)$(if ($p.Acao -ne 'Criar') { ' - o antigo ja foi apagado' })"
            $cont.Erro++; if ($c -eq 5) { $semPermissao = $true }; continue
        }
        $cont[$p.Acao]++
        Write-Host ("[{0}] {1} {2} x {3} mm" -f $p.Acao.ToUpperInvariant(), $p.Nome, (Mm $p.L), (Mm $p.A))
    }
} finally { [void][DebFormularios]::ClosePrinter($h) }
Write-Host "`n$($cont.Criar) criada(s), $($cont.Recriar) recriada(s), $($cont.Substituir) substituida(s), $($cont.Erro) erro(s)."
if ($semPermissao) { Write-Host 'Sem permissao: abra o PowerShell como administrador e rode de novo.' }

# ---- conferencia na impressora ----
$papeis = [DebFormularios]::PapeisDaImpressora($Impressora)
if ($papeis.Count -gt 0) {
    $faltam = @($ordem | Where-Object { $papeis -notcontains $_.Nome } | ForEach-Object { $_.Nome })
    if ($faltam.Count -eq 0) { Write-Host "A impressora '$Impressora' lista todas as folhas." }
    else { Write-Host "A impressora '$Impressora' ainda nao lista: $($faltam -join ', ')" }
} else { Write-Host "Impressora '$Impressora' nao encontrada para conferir (use -Impressora <nome>)." }
if ($cont.Erro -gt 0) { exit 1 }
