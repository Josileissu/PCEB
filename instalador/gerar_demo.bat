@echo off
rem Gera instalador\saida\demo_<versao>.exe: versao de demonstracao (1 hora de uso a partir da primeira carga no computador)
setlocal
cd /d "%~dp0.."
"C:\Program Files\dotnet\dotnet.exe" build src\Armadura.Carregador -c Release --nologo -v q || goto erro
"C:\Program Files\dotnet\dotnet.exe" build src\Armadura.GstarCAD -c Release --nologo -v q -p:Demo=true || goto erro
"%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" /Q /DDemo "instalador\ArmaduraBR.iss" || goto erro
rem o build demo usa a mesma pasta obj: recompila a versao normal para o bin\GstarCAD2021 e os testes
"C:\Program Files\dotnet\dotnet.exe" build src\Armadura.GstarCAD -c Release --nologo -v q || goto erro
echo.
echo Demonstracao gerada em instalador\saida\ (demo_^<versao^>.exe)
exit /b 0
:erro
echo.
echo FALHOU - veja as mensagens acima.
exit /b 1
