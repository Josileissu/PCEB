# Compila o plugin e roda o autoteste (ARMTESTE/ARMTESTE2) numa instância NOVA do GstarCAD 2021.
# A instância do usuário não é tocada. Resultado: %AppData%\PCEB\DEB.Cad\logs\autoteste.txt
# Uso: powershell -File tools\autoteste.ps1 [-Saida <pasta>] [-SemBuild]
param(
    [string]$Saida = $env:TEMP,
    [switch]$SemBuild
)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $raiz 'bin\GstarCAD2021\DEB.GstarCAD.dll'

if (-not $SemBuild) {
    & 'C:\Program Files\dotnet\dotnet.exe' build (Join-Path $raiz 'src\DEB.GstarCAD\DEB.GstarCAD.csproj') -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação.' }
}

$scr = Join-Path $Saida 'armadura_autoteste.scr'
$dllLisp = $dll -replace '\\', '/'
"(command `"_.NETLOAD`" `"$dllLisp`")`r`nARMTESTE`r`n" | Out-File -Encoding ascii $scr

$relatorio = Join-Path $env:APPDATA 'PCEB\DEB.Cad\logs\autoteste.txt'
Remove-Item $relatorio -ErrorAction SilentlyContinue
$env:ARM_AUTOTESTE_SAIDA = Join-Path $Saida 'armadura_autoteste.dwg'
$env:ARM_LOG_DETALHADO = '1'

$p = Start-Process 'C:\Program Files\Gstarsoft\GstarCAD2021\gcad.exe' "/nologo /b `"$scr`"" -PassThru
if (-not $p.WaitForExit(300000)) {
    Write-Warning "GstarCAD do teste não fechou em 5 min (PID $($p.Id)); encerrando só essa instância."
    Stop-Process -Id $p.Id -Force
}
if (Test-Path $relatorio) { Get-Content $relatorio } else { Write-Warning 'Sem relatório: veja o log do plugin.' }
"DWG do teste: $env:ARM_AUTOTESTE_SAIDA"
