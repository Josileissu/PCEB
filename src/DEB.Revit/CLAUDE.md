# DEB.Revit — Detalhador Estrutural Brasileiro para Revit (ex-StructuralDetailer)

Plugin para Revit 2025/2026 (C#, .NET 8): detalhamento de peças estruturais, com foco em **pilares pré-moldados**
(consolos, armadura, pranchas no padrão do escritório). Ainda com os nomes antigos no código (`StructuralDetailer`)
até a etapa 4 da migração (`docs/04_arquitetura/migracao_repositorio_unico.md`).

Arquivos do escritório (fora do Git — padrões não são públicos), na pasta `desenvolvedor`:
modelo de teste `C:\Users\SUELIO087\Documents\desenvolvedor\Projeto1.rvt`; prancha-modelo
`C:\Users\SUELIO087\Documents\desenvolvedor\exemplo_armadura_pilar_pre_moldado.pdf` (PP127, A1, bilíngue PT/EN, cotas em cm).

## Compilar e instalar
```powershell
cd C:\Users\SUELIO087\Documents\desenvolvedor\PCEB\src\DEB.Revit
& "C:\Program Files\dotnet\dotnet.exe" build -c Release -p:RevitVersion=2025 -p:NoWarn=MSB3277
& "C:\Program Files\dotnet\dotnet.exe" build -c Release -p:RevitVersion=2026 -p:NoWarn=MSB3277
```
- O build copia a DLL e o `.addin` para `%AppData%\Autodesk\Revit\Addins\<versão>\`.
- **Com o Revit daquela versão aberto a DLL fica bloqueada**: confira com `Get-Process Revit` antes (pode ser de
  outro usuário do Windows); para só compilar use `-p:DeployToRevit=false`. Nunca feche o Revit do usuário à força.
- Revit 2026: 18 avisos CS0618 (API obsoleta) conhecidos.

## Testar sem o usuário (AutoRun)
`src/AutoRun.cs`: abrir o Revit 2025 com variáveis de ambiente roda uma tarefa no evento Idling e fecha o Revit.
```powershell
$env:SD_AUTORUN = "precast"   # diag | probe | precast
$env:SD_AUTOEXIT = "1"
$env:SD_SAVEAS = "<scratchpad>\Projeto1_teste.rvt"   # resultado vai para uma CÓPIA; o original nunca é salvo
$p = Start-Process "C:\Program Files\Autodesk\Revit 2025\Revit.exe" '"C:\Users\SUELIO087\Documents\desenvolvedor\Projeto1.rvt"' -PassThru
$p.WaitForExit(540000)
```
- `diag`: levantamento do modelo em JSON. `probe`: testes de viabilidade da API (transação desfeita).
  `precast`: fluxo completo de pilares com armadura padrão + PNG da prancha e da janela.
- Resultados em `%AppData%\StructuralDetailer\logs\` (`autorun.txt` aponta para o arquivo gerado).
  Leia os PNG exportados para conferir a prancha visualmente.
- Logs de cada execução pelo usuário: `detalhamento_*.log` (exceções com pilha).
  Journals do Revit: `%LocalAppData%\Autodesk\Revit\Autodesk Revit 2025\Journals\`.

## Arquitetura
```
src/
  App.cs, AutoRun.cs
  Commands/      DetailPieceCommand (peça avulsa), PrecastColumnsCommand, DiagnosticsCommand, OpenConfigCommand
  Core/          PieceAnalyzer (sistema local), PieceDetailer (assembly → vistas → cotas → folha),
                 ViewFactory, DimensionService, SheetBuilder, GeometryHelper, DetailingConfig/Report
  Precast/       PrecastColumn (ColumnFrame, Corbel, PrecastSurvey: levantamento e grupos),
                 ColumnRebarSpec (armadura escolhida + verificações NBR 6118/9062 + modelos salvos),
                 ColumnRebarLayout (geometria das barras em coordenadas locais — usada pela prévia E pelo Revit),
                 RebarFactory, PrecastColumnPipeline, UI/ (janela WPF em código, sem XAML)
  Presentation/  PresentationStandard (JSON por elemento em %AppData%\StructuralDetailer\Padroes),
                 Defaults (padrão de fábrica = PP127), StandardDetailer, SheetComposer (regiões),
                 RebarSchedules, RebarData (partição N1.., parâmetros SD_Aço / SD_Peso), AnnotationStyles
```
Fluxo dos pré-moldados: levantamento → grupos de peças iguais → janela (armadura por grupo) →
para cada pilar: limpar → marca → barras → dados das barras → assembly (pilar + consolos + barras) →
o 1º do grupo gera a prancha pelo padrão; os demais caem no mesmo tipo de assembly e reaproveitam a folha.

## Decisões do usuário (não reabrir sem ele pedir)
- Consolos são família "Consolo" **aninhada e compartilhada na viga**; entram no **assembly do pilar**
  (testado: funciona; não usar Parts).
- Peças iguais **ou giradas** 90/180/270° = mesma marca; espelhadas não.
- Armaduras: longitudinal + estribos, zonas de estribos (topo/base/consolo), consolo (tirante + costura),
  içamento/insertos.
- Pranchas guiadas por padrões de apresentação (pilar, viga, laje, fundação).
- Parâmetros `SD_Aço` / `SD_Peso (kg)` mantêm o nome (modelos já detalhados).
- Escopo e regras gerais do PCEB: ver `CLAUDE.md` da raiz do repositório.

## Armadilhas já encontradas
- Guardar `Solid` sem o `GeometryElement` dono → SEHException (GC libera a memória nativa).
  Ler todas as faces **antes** de criar cotas (criar cota regenera e invalida a geometria do corte).
- Assemblies idênticos: o Revit junta no mesmo tipo e **apaga vistas novas**; nunca renomear um gêmeo.
- API 2025: `GeometryInstance.GetSymbolGeometryId().SymbolId` (não há `.Symbol`);
  `RebarBarType.SetBarTypeDiameters(BarTypeDiameterOptions)`; sem parâmetro de peso de barra e sem
  campos calculados via API (por isso `SD_Peso (kg)`). Documentação local: `C:\Program Files\Autodesk\Revit 2025\RevitAPI.xml` (grep).
- Tabelas/textos na folha: posicionar por `ScheduleSheetInstance.Point` / `TextNote.Coord`, não pelo bounding box.
- Python que lê arquivos baixados: usar `-I`; `pymupdf` está instalado no usuário (renderizar PDFs em blocos).

## Próximos passos combinados
Marcadores de nível e "comprimento total" nas vistas; etiquetas N nas seções (falta saber a família de tag);
quadros "ARMADURAS console"; cotas dos dobramentos em cm; aplicar o carimbo/legendas reais do escritório
(pedir o template); depois vigas pré-moldadas.
