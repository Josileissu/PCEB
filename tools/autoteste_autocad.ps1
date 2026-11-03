# Compila o plugin AutoCAD 2026 e roda o autoteste (ARMTESTE) numa instância NOVA do AutoCAD.
# A instância do usuário não é tocada. Resultado: %AppData%\ArmaduraBR\logs\autoteste.txt
# Uso: powershell -File tools\autoteste_autocad.ps1 [-Saida <pasta>] [-Acad <acad.exe>] [-SemBuild]
param(
    [string]$Saida = (Join-Path $env:TEMP 'armadura_acad'),
    [string]$Acad = 'C:\Program Files\Autodesk\AutoCAD 2026\acad.exe',
    [switch]$SemBuild
)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $raiz 'bin\AutoCAD2026\ArmaduraBR.AutoCAD.dll'

if (-not $SemBuild) {
    & 'C:\Program Files\dotnet\dotnet.exe' build (Join-Path $raiz 'src\Armadura.AutoCAD\Armadura.AutoCAD.csproj') -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação.' }
}
if (-not (Test-Path $Acad)) { throw "AutoCAD não encontrado: $Acad" }
New-Item -ItemType Directory -Force $Saida | Out-Null

$scr = Join-Path $Saida 'armadura_autoteste_acad.scr'
$dllLisp = $dll -replace '\\', '/'
# FILEDIA=0 para o PNGOUT; SECURELOAD=0 para o NETLOAD; ZOOM _E porque o /b abre sem vista
# (sem vista, as janelas de seleção do STRETCH/MOVE acham 0 objetos e o autoteste trava).
@(
    'FILEDIA', '0',
    'SECURELOAD', '0',
    "(command `"_.NETLOAD`" `"$dllLisp`")",
    '_.ZOOM', '_E',
    'ARMTESTE'
) | Out-File -Encoding ascii $scr

$relatorio = Join-Path $env:APPDATA 'ArmaduraBR\logs\autoteste.txt'
Remove-Item $relatorio -ErrorAction SilentlyContinue
$env:ARM_AUTOTESTE_SAIDA = Join-Path $Saida 'autoteste.dwg'
$env:ARM_LOG_DETALHADO = '1'

$p = Start-Process $Acad "/nologo /b `"$scr`"" -PassThru
if (-not $p.WaitForExit(300000)) {
    Write-Warning "AutoCAD do teste não fechou em 5 min (PID $($p.Id)); encerrando só essa instância."
    Stop-Process -Id $p.Id -Force
}
if (Test-Path $relatorio) { Get-Content $relatorio } else { Write-Warning 'Sem relatório: veja o log do plugin.' }
"DWG do teste: $env:ARM_AUTOTESTE_SAIDA"
