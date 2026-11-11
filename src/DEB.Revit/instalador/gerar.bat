@echo off
rem Compila o plugin para o Revit 2025 e 2026 e gera o instalador:
rem   instalador\saida\DEB_Revit_Setup_<versao>.exe
rem Precisa do Revit 2025 e 2026 instalados (as referencias RevitAPI vem da pasta de cada um) e do Inno Setup 6.
rem Nao copia nada para a pasta de Addins (DeployToRevit=false). Assina as DLLs com o certificado "CN=PCEB DEB", se existir.
setlocal
cd /d "%~dp0.."
set DOTNET=C:\Program Files\dotnet\dotnet.exe
for %%v in (2025 2026) do (
  echo Revit %%v...
  "%DOTNET%" build DEB.Revit.csproj -c Release --nologo -v q -p:RevitVersion=%%v -p:DeployToRevit=false -p:NoWarn=MSB3277 || goto erro
)
powershell -NoProfile -Command "$c = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object Subject -eq 'CN=PCEB DEB' | Select-Object -First 1; if ($c) { Get-Item 'bin\Release\R2025\DEB.*.dll','bin\Release\R2026\DEB.*.dll' | Set-AuthenticodeSignature -Certificate $c -HashAlgorithm SHA256 | Out-Null; Write-Host 'DLLs assinadas (PCEB DEB)' } else { Write-Host 'Sem certificado PCEB DEB: DLLs sem assinatura' }"
if not exist instalador\saida mkdir instalador\saida
"%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" /Q "instalador\DEB_Revit.iss" || goto erro
echo.
echo Instalador gerado em instalador\saida\
exit /b 0
:erro
echo.
echo ERRO ao gerar o instalador do Revit.
exit /b 1
