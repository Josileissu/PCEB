# Comandos

`dotnet` não está no PATH: use `& "C:\Program Files\dotnet\dotnet.exe"` (abaixo, `dotnet`). Rode da raiz do PCEB.

## Build e testes do núcleo
```powershell
dotnet build PCEB.sln -c Release -p:DeployToRevit=false -p:NoWarn=MSB3277      # tudo, sem instalar no Revit
dotnet test tests\DEB.Core.Tests -c Release                                     # pt-BR
$env:ARM_CULTURA = "en-US"; dotnet test tests\DEB.Core.Tests -c Release --no-build   # outra cultura
dotnet test tests\DEB.Core.Tests -c Release --filter "FullyQualifiedName~Viga"  # por área (filtros em areas.md)
```

## Regressão visual
`RegressaoVisualTests` compara o desenho dos templates com `tests/DEB.Core.Tests/Referencias/*.txt`. Mudou um template de
propósito: confira a diferença que o teste mostra, regrave e inclua os `.txt` no commit.
```powershell
$env:ARM_ATUALIZAR_REFERENCIAS = "1"; dotnet test tests\DEB.Core.Tests -c Release --filter "FullyQualifiedName~RegressaoVisual"
Remove-Item Env:ARM_ATUALIZAR_REFERENCIAS
```

## Plugins por versão
- CAD: `dotnet build src\DEB.Cad\src\DEB.GstarCAD -c Release -p:GstarVersao=<ano>`, `...\DEB.AutoCAD -p:AutoCadVersao=<ano>`,
  `...\DEB.ZWCAD -p:ZwcadVersao=<ano>` — detalhes em `src/DEB.Cad/AGENTS.md`.
- Revit: `dotnet build src\DEB.Revit\DEB.Revit.csproj -c Release -p:RevitVersion=2025|2026 -p:NoWarn=MSB3277`
  (sem `-p:DeployToRevit=false` ele instala; com o Revit aberto a DLL fica presa) — `src/DEB.Revit/AGENTS.md`.

## Autotestes
- CAD (GstarCAD): passos de `src/DEB.Cad/tools/autoteste.ps1`, rodados inline numa instância nova; AutoCAD:
  `src/DEB.Cad/tools/autoteste_autocad.ps1 -Versao <ano> [-Comando ARMTESTE…]`. Relatório em `%AppData%\PCEB\DEB.Cad\logs\autoteste.txt`.
- Revit: AutoRun (`SD_AUTORUN`, `SD_AUTOEXIT`, `SD_SAVEAS`) numa instância nova — tarefas em `src/DEB.Revit/AGENTS.md`.
  Resultados em `%AppData%\PCEB\DEB.Revit\logs\`.
- Nunca use mouse/teclado reais nos testes: o usuário usa o computador ao mesmo tempo.
