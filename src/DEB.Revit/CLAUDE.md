# DEB.Revit — Detalhador Estrutural Brasileiro para Revit (ex-StructuralDetailer)

Plugin para Revit 2025/2026 (C#, .NET 8): detalhamento de peças estruturais, com foco em **pilares pré-moldados**
(consolos, armadura, pranchas no padrão do escritório). Renomeado na etapa 4 (03/11/2026): projeto/DLL/namespace `DEB.Revit`, aba "Detalhador Estrutural Brasileiro",
`DEB.Revit.addin`; dados em `%AppData%\PCEB\DEB.Revit` (copiados de `%AppData%\StructuralDetailer` na 1ª execução, `Core/UserFolders.cs`).
Mantidos por estarem gravados nos modelos: `Tag = "StructuralDetailer"` das barras, grupo de parâmetros compartilhados
`StructuralDetailer`, parâmetros `SD_*`; também os IDs de botão `SD_*` e as variáveis `SD_AUTORUN`/`SD_AUTOEXIT`/`SD_SAVEAS`.

Arquivos do escritório (fora do Git — padrões não são públicos), na pasta-mãe do PCEB (`desenvolvedor\`; no PC2, ver
`docs/04_arquitetura/trabalho_em_paralelo.md`): modelo de teste `Projeto1.rvt`; prancha-modelo
`exemplo_armadura_pilar_pre_moldado.pdf` (PP127, A1, bilíngue PT/EN, cotas em cm).

## Compilar e instalar
```powershell
cd <raiz do PCEB>\src\DEB.Revit
& "C:\Program Files\dotnet\dotnet.exe" build DEB.Revit.csproj -c Release -p:RevitVersion=2025 -p:NoWarn=MSB3277
& "C:\Program Files\dotnet\dotnet.exe" build DEB.Revit.csproj -c Release -p:RevitVersion=2026 -p:NoWarn=MSB3277
```
- O build copia a DLL e o `.addin` para `%AppData%\Autodesk\Revit\Addins\<versão>\`.
- **Com o Revit daquela versão aberto a DLL fica bloqueada**: confira com `Get-Process Revit` antes (pode ser de
  outro usuário do Windows); para só compilar use `-p:DeployToRevit=false`. Nunca feche o Revit do usuário à força.
- Revit 2026: 18 avisos CS0618 (API obsoleta) conhecidos.
- **Assinatura**: o build assina `DEB.Revit.dll`/`DEB.Core.dll` com o certificado `CN=PCEB DEB` do usuário, se existir
  (`Cert:\CurrentUser\My`). Sem assinatura o Revit pede "Sempre carregar" a cada build — e a instância de teste do
  AutoRun fica parada nessa janela (era a demora "irregular" dos testes no PC2). No PC2 o certificado foi criado pelo
  usuário em 05/11/2026 (raiz e editor confiáveis do usuário). Outro computador: exportar o .pfx com senha ou criar um
  igual (`New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=PCEB DEB"` + importar o .cer em TrustedPublisher e Root).
- **PC2**: testes no Revit 2026 (o 2025 é o do usuário). Modelo de teste com embutimento: `criarmodelo` (base −1,00 m).

## Testar sem o usuário (AutoRun)
`src/AutoRun.cs`: abrir o Revit 2025 com variáveis de ambiente roda uma tarefa no evento Idling e fecha o Revit.
```powershell
$env:SD_AUTORUN = "precast"   # diag | probe | precast | ferramentas
$env:SD_COPYFROM = "<.rvt de origem>"   # só "ferramentas": testa as cópias entre arquivos
$env:SD_AUTOEXIT = "1"
$env:SD_SAVEAS = "<scratchpad>\Projeto1_teste.rvt"   # resultado vai para uma CÓPIA; o original nunca é salvo
$p = Start-Process "C:\Program Files\Autodesk\Revit 2025\Revit.exe" '"<pasta-mãe do PCEB>\Projeto1.rvt"' -PassThru
$p.WaitForExit(540000)
```
- Sem projeto aberto (`src/Precast/TestModel.cs`): `familias` (relatório de `SD_FAMILIAS`, .rfa separados por `;`) e
  `criarmodelo` (projeto novo de `SD_TEMPLATE` com grade 3×2 de `SD_PILAR` + vigas `SD_VIGA`, gravado em `SD_SAVEAS`).
  Famílias do usuário em `S:\Meu Drive\Arquivos\Familias Revit` (copiar antes; `.000N.rfa` são backups). O template do
  escritório `EST_RVT_Residência.rte` traz uma residência modelada — para teste use `Structural Analysis-DefaultBRAPTB.rte`.
- Vigas pré-moldadas com consolos aninhados: a folga vem de `folga_esq`/`folga_dir` da família; **recuo da junta
  (START/END_JOIN_CUTBACK, ½" padrão) e extensões inicial/final da instância têm de ser 0**, senão os consolos se afastam
  do pilar e o levantamento não os encontra.
- `criarmodelo` com `SD_NIVEIS` = "cota_mm:direções;..." (ex.: "3000:X;6000:XY;9000:Y") cria vigas/consolos em vários níveis
  (modelo de teste do PC2: `Teste_PM_niveis.rvt`, pilar de 10 m com 4 consolos em 3 níveis).
- `diag`: levantamento do modelo em JSON. `probe`: testes de viabilidade da API (transação desfeita).
  `precast`: fluxo completo de pilares com armadura padrão + PNG da prancha e da janela.
- Resultados em `%AppData%\PCEB\DEB.Revit\logs\` (`autorun.txt` aponta para o arquivo gerado).
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
  Presentation/  PresentationStandard (JSON por elemento em %AppData%\PCEB\DEB.Revit\Padroes),
                 Defaults (padrão de fábrica = PP127), StandardDetailer, SheetComposer (regiões),
                 RebarSchedules, RebarData (partição N1.., parâmetros SD_Aço / SD_Peso), AnnotationStyles
```
Fluxo dos pré-moldados: levantamento → grupos de peças iguais → janela (armadura por grupo) →
para cada pilar: limpar → marca → barras → dados das barras → assembly (pilar + consolos + barras) →
o 1º do grupo gera a prancha pelo padrão; os demais caem no mesmo tipo de assembly e reaproveitam a folha.

## Ferramentas (painel "Ferramentas", `src/Tools`) — ex-scripts Revit_Suelio
Os scripts originais (`auxiliar_scripts/`) foram apagados a pedido do usuário em 04/11/2026; a lógica deles está aqui.
- `ToolCommand`: base de todos — barreira de exceções (mensagem + log `logs/ferramentas_AAAAMMDD.log`), `ToolCancelled`.
- `ToolDialogs`: `FieldForm` (formulário com validação) e `CheckListWindow` (lista com Shift+clique, Todos/Nenhum/Inverter/filtro).
- `RenumberCommands`: pilares, vigas, lajes, fundações, paredes, peças. Ordem em **DEB.Core** (`Numeracao/OrdenacaoMarcas`,
  com testes); tolerância 0,50 m; entra em grupos aninhados. `Apply()` sem janela (usado pelo AutoRun).
- `ModelToolsCommands`: Selecionar por marca (`FiltroMarca` no núcleo) e Alterar material (tipo; ou instância quando a
  família tem o material na peça — caso do Projeto1).
- `CopyFromFileCommands`: fundações e montagens com vistas/folhas de outro .rvt (subtransação por montagem; não fecha
  documento que já estava aberto). `ExportCsvCommand`: 6 CSVs, mesmas colunas do ExportarTudo_Colunas.
- Janelas WPF não testadas pelo AutoRun (só a lógica): conferir com o usuário.

## Decisões do usuário (não reabrir sem ele pedir)
- Consolos são família "Consolo" **aninhada e compartilhada na viga**; entram no **assembly do pilar**
  (testado: funciona; não usar Parts).
- Peças iguais **ou giradas** 90/180/270° = mesma marca; espelhadas não.
- Armaduras: longitudinal + estribos, zonas de estribos (topo/base/consolo), consolo (tirante + costura),
  içamento/insertos.
- Pranchas guiadas por padrões de apresentação (pilar, viga, laje, fundação).
- Parâmetros `SD_Aço` / `SD_Peso (kg)` mantêm o nome (modelos já detalhados).
- Escopo e regras gerais do PCEB: ver `CLAUDE.md` da raiz do repositório.

## Detalhes padrão (legendas)
- DXFs em `%AppData%\PCEB\DEB.Revit\Padroes\Detalhes\` (fora do Git), extraídos do DWG do P56 com
  `tools/detalhes_dxf` (README lá). `LegendFactory` cria a legenda que falta (linhas/textos/regiões nativas; estilos
  "PCEB - Detalhe …") duplicando uma legenda do projeto; sem nenhuma, copia uma do `Folha.ArquivoLegendaBase`
  (`EST_RVT_Residência.rte` tem 5) — a API não cria legenda do zero. Legenda existente nunca é refeita.
- AutoRun: fecha sozinho as janelas do Revit (`DialogBoxShowing`, ex.: aviso do modelo analítico ao abrir um .rte
  antigo) e `contarlegendas` lista as legendas de arquivos (`SD_FAMILIAS`). Abrir arquivo antigo pela API: use
  `LegendFactory.OpenQuietly` (descarta os avisos da atualização, que senão param numa janela).
- AutoCAD 2026 está no PC2: `accoreconsole.exe` converte DWG↔DXF e plota PNG sem janela (ver o README).

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
