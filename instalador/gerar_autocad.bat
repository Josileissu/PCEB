@echo off
rem Compila o plugin AutoCAD 2026 e gera instalador\saida\ArmaduraBR_AutoCAD_Setup_<versao>.exe
setlocal
cd /d "%~dp0.."
"C:\Program Files\dotnet\dotnet.exe" build src\Armadura.AutoCAD -c Release --nologo -v q || goto erro
"%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" /Q "instalador\ArmaduraBR_AutoCAD.iss" || goto erro
echo.
echo Instalador gerado em instalador\saida\
exit /b 0
:erro
echo.
echo FALHOU - veja as mensagens acima.
exit /b 1
