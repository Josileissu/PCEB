# Compila o plugin AutoCAD (padrão 2026) e roda o autoteste (ARMTESTE) numa instância NOVA do AutoCAD.
# A instância do usuário não é tocada. Resultado: %AppData%\PCEB\DEB.Cad\logs\autoteste.txt
# Uso: powershell -File tools\autoteste_autocad.ps1 [-Comando ARMTESTE|ARMTESTEPALETA|ARMTESTEMATCH] [-Versao 2026] [-Saida <pasta>] [-Acad <acad.exe>] [-SemBuild]
param(
    [string]$Comando = 'ARMTESTE',
    [string]$Versao = '2026',
    [string]$Saida = (Join-Path $env:TEMP 'armadura_acad'),
    [string]$Acad = '',
    [switch]$SemBuild
)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot -Parent
if (-not $Acad) { $Acad = "C:\Program Files\Autodesk\AutoCAD $Versao\acad.exe" }
$dll = Join-Path $raiz "bin\AutoCAD$Versao\DEB.AutoCAD.dll"

if (-not $SemBuild) {
    & 'C:\Program Files\dotnet\dotnet.exe' build (Join-Path $raiz 'src\DEB.AutoCAD\DEB.AutoCAD.csproj') -c Release --nologo -v q "-p:AutoCadVersao=$Versao"
    if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação.' }
}
if (-not (Test-Path $Acad)) { throw "AutoCAD não encontrado: $Acad" }
New-Item -ItemType Directory -Force $Saida | Out-Null

$scr = Join-Path $Saida 'armadura_autoteste_acad.scr'
$dllLisp = $dll -replace '\\', '/'
# FILEDIA=0 para o PNGOUT; SECURELOAD=0 para o NETLOAD; ZOOM _E porque o /b abre sem vista
# (sem vista, as janelas de seleção do STRETCH/MOVE acham 0 objetos e o autoteste trava).
# FILEDIA só no teste completo: é ele que o devolve (ARMTESTEFIM); os testes parciais não usam o PNGOUT
$linhas = @()
if ($Comando -eq 'ARMTESTE') { $linhas += 'FILEDIA', '0' }
$linhas += 'SECURELOAD', '0', "(command `"_.NETLOAD`" `"$dllLisp`")", '_.ZOOM', '_E', $Comando
$linhas | Out-File -Encoding ascii $scr

$relatorio = Join-Path $env:APPDATA 'PCEB\DEB.Cad\logs\autoteste.txt'
Remove-Item $relatorio -ErrorAction SilentlyContinue
$env:ARM_AUTOTESTE_SAIDA = Join-Path $Saida 'autoteste.dwg'
$env:ARM_LOG_DETALHADO = '1'

# O AutoCAD carrega sozinho o bundle instalado (ApplicationPlugins). Se ele entrar primeiro, o NETLOAD
# do bin recém-compilado é ignorado (mesmo nome de assembly) e o teste rodaria a versão instalada,
# que pode estar velha. Desliga o autoload só durante o teste e devolve no finally.
$bundleXml = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\DEB.bundle\PackageContents.xml'
$xmlDesligado = "$bundleXml.off"
$chavesAuto = @(Get-ChildItem 'HKCU:\Software\Autodesk\AutoCAD' -ErrorAction SilentlyContinue |
    ForEach-Object { Get-ChildItem $_.PSPath -ErrorAction SilentlyContinue } |
    ForEach-Object { $k = Join-Path $_.PSPath 'Applications\ArmaduraBR'; if (Test-Path $k) { $k } })
$loadCtrls = @{}
foreach ($k in $chavesAuto) {
    $v = (Get-ItemProperty -Path $k -Name LOADCTRLS -ErrorAction SilentlyContinue).LOADCTRLS
    if ($null -ne $v) { $loadCtrls[$k] = $v; Set-ItemProperty -Path $k -Name LOADCTRLS -Value 0 }
}
if (Test-Path $bundleXml) { Rename-Item $bundleXml 'PackageContents.xml.off' }

try {
    $p = Start-Process $Acad "/nologo /b `"$scr`"" -PassThru
    if (-not $p.WaitForExit(300000)) {
        Write-Warning "AutoCAD do teste não fechou em 5 min (PID $($p.Id)); encerrando só essa instância."
        Stop-Process -Id $p.Id -Force
    }
} finally {
    # o bundle PRIMEIRO: é ele que faz o plugin carregar no AutoCAD do usuário
    if (Test-Path $xmlDesligado) { Rename-Item $xmlDesligado 'PackageContents.xml' }
    # o AutoCAD do teste, sem achar o bundle, apaga as chaves Applications\ArmaduraBR que ele mesmo tinha
    # criado a partir do bundle (2026-11-05); ele as recria ao abrir. Só devolve o valor das que existem.
    foreach ($k in $loadCtrls.Keys) {
        if (Test-Path $k) { Set-ItemProperty -Path $k -Name LOADCTRLS -Value $loadCtrls[$k] -ErrorAction SilentlyContinue }
    }
}
if (Test-Path $relatorio) { Get-Content $relatorio } else { Write-Warning 'Sem relatório: veja o log do plugin.' }
"DWG do teste: $env:ARM_AUTOTESTE_SAIDA"
