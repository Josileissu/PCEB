@echo off
rem Compila o plugin para AutoCAD 2021 a 2027 (bibliotecas do pacote NuGet oficial "AutoCAD.NET") e gera
rem instalador\saida\ArmaduraBR_AutoCAD_Setup_<versao>.exe com uma DLL por versao no mesmo bundle.
rem 2027 usa .NET 10: SDK em %LocalAppData%\Microsoft\dotnet10 (instalado com o dotnet-install.ps1 oficial).
setlocal
cd /d "%~dp0.."
set DOTNET8=C:\Program Files\dotnet\dotnet.exe
set DOTNET10=%LocalAppData%\Microsoft\dotnet10\dotnet.exe
for %%v in (2021 2022 2023 2024 2025 2026) do (
  echo AutoCAD %%v...
  "%DOTNET8%" build src\Armadura.AutoCAD -c Release --nologo -v q -p:AutoCadVersao=%%v || goto erro
)
if not exist "%DOTNET10%" (
  echo FALTA o SDK do .NET 10 em %DOTNET10% ^(necessario para o AutoCAD 2027^)
  goto erro
)
echo AutoCAD 2027...
"%DOTNET10%" build src\Armadura.AutoCAD -c Release --nologo -v q -p:AutoCadVersao=2027 || goto erro
"%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" /Q "instalador\ArmaduraBR_AutoCAD.iss" || goto erro
echo.
echo Instalador gerado em instalador\saida\
exit /b 0
:erro
echo.
echo FALHOU - veja as mensagens acima.
exit /b 1
