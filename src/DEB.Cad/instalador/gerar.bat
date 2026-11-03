@echo off
rem Compila o plugin para todos os programas e gera o instalador unico:
rem   instalador\saida\ArmaduraBR_Setup_<versao>.exe   (gerar.bat)
rem   instalador\saida\demo_<versao>.exe               (gerar.bat demo, ou gerar_demo.bat: 1 hora de uso)
rem GstarCAD 2021 a 2027, AutoCAD 2021 a 2026 e ZWCAD 2024 a 2026 usam o SDK do .NET 8; o AutoCAD 2027 usa o do .NET 10
rem (%LocalAppData%\Microsoft\dotnet10, instalado com o dotnet-install.ps1 oficial).
setlocal
cd /d "%~dp0.."
set DOTNET8=C:\Program Files\dotnet\dotnet.exe
set DOTNET10=%LocalAppData%\Microsoft\dotnet10\dotnet.exe
set OPC=-c Release --nologo -v q
set ISCCDEMO=
if /i "%~1"=="demo" (
  set OPC=-c Release --nologo -v q -p:Demo=true
  set ISCCDEMO=/DDemo
)
if not exist "%DOTNET10%" (
  echo FALTA o SDK do .NET 10 em %DOTNET10% ^(necessario para o AutoCAD 2027^)
  goto erro
)
echo Carregador...
"%DOTNET8%" build src\Armadura.Carregador -c Release --nologo -v q || goto erro
for %%v in (2021 2022 2023 2024 2025 2026 2027) do (
  echo GstarCAD %%v...
  "%DOTNET8%" build src\Armadura.GstarCAD %OPC% -p:GstarVersao=%%v || goto erro
)
for %%v in (2021 2022 2023 2024 2025 2026) do (
  echo AutoCAD %%v...
  "%DOTNET8%" build src\Armadura.AutoCAD %OPC% -p:AutoCadVersao=%%v || goto erro
)
echo AutoCAD 2027...
"%DOTNET10%" build src\Armadura.AutoCAD %OPC% -p:AutoCadVersao=2027 || goto erro
for %%v in (2024 2025 2026) do (
  echo ZWCAD %%v...
  "%DOTNET8%" build src\Armadura.ZWCAD %OPC% -p:ZwcadVersao=%%v || goto erro
)
"%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" /Q %ISCCDEMO% "instalador\ArmaduraBR.iss" || goto erro
echo.
echo Instalador gerado em instalador\saida\
exit /b 0
:erro
echo.
echo FALHOU - veja as mensagens acima.
exit /b 1
