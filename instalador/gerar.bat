@echo off
rem Compila o plugin e o carregador e gera instalador\saida\ArmaduraBR_Setup_<versao>.exe
setlocal
cd /d "%~dp0.."
"C:\Program Files\dotnet\dotnet.exe" build src\Armadura.Carregador -c Release --nologo -v q || goto erro
"C:\Program Files\dotnet\dotnet.exe" build src\Armadura.GstarCAD -c Release --nologo -v q || goto erro
"%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" /Q "instalador\ArmaduraBR.iss" || goto erro
echo.
echo Instalador gerado em instalador\saida\
exit /b 0
:erro
echo.
echo FALHOU - veja as mensagens acima.
exit /b 1
