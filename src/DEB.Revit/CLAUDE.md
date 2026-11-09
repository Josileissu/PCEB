# DEB.Revit — Detalhador Estrutural Brasileiro para Revit (ex-StructuralDetailer)

Plugin para Revit 2025/2026 (C#, .NET 8): detalhamento de peças estruturais, com foco em **pilares pré-moldados**
(consolos, armadura, pranchas no padrão do escritório). Renomeado na etapa 4 (03/11/2026): projeto/DLL/namespace `DEB.Revit`, aba "Detalhador Estrutural Brasileiro",
`DEB.Revit.addin`; dados em `%AppData%\PCEB\DEB.Revit` (copiados de `%AppData%\StructuralDetailer` na 1ª execução, `Core/UserFolders.cs`).
Mantidos por estarem gravados nos modelos: `Tag = "StructuralDetailer"` das barras, grupo de parâmetros compartilhados
`StructuralDetailer`, parâmetros `SD_*`; também os IDs de botão `SD_*` e as variáveis `SD_AUTORUN`/`SD_AUTOEXIT`/`SD_SAVEAS`.

Arquivos do escritório (fora do Git — padrões não são públicos), na pasta-mãe do PCEB (`desenvolvedor\`; no PC2, ver
`docs/04_arquitetura/trabalho_em_paralelo.md`): **modelo-base dos testes `Teste_PM.rvt`** (decisão do usuário, 09/11/2026;
gerado pelo AutoRun `criarmodelo` em 05/11: pilares pré-moldados 40x40 e 50x50 com consolos — grupos de 4 e 2 peças — e vigas
"Viga Pré-Moldada" 20x50 com consolos aninhados e parâmetros de dente Gerber); `Projeto1.rvt` (modelo antigo, obra real com
peças moldadas no local); prancha-modelo
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

## Testar sem o usuário (AutoRun)
`src/AutoRun.cs`: abrir o Revit 2025 com variáveis de ambiente roda uma tarefa no evento Idling e fecha o Revit.
```powershell
$env:SD_AUTORUN = "precast"   # diag | probe | precast | ferramentas
$env:SD_COPYFROM = "<.rvt de origem>"   # só "ferramentas": testa as cópias entre arquivos
$env:SD_AUTOEXIT = "1"
$env:SD_SAVEAS = "<scratchpad>\Teste_PM_resultado.rvt"   # resultado vai para uma CÓPIA; o original nunca é salvo
$p = Start-Process "C:\Program Files\Autodesk\Revit 2025\Revit.exe" '"<pasta-mãe do PCEB>\Teste_PM.rvt"' -PassThru
$p.WaitForExit(540000)
```
- `exportarpecas`: grava o JSON das peças para o CAD (`SD_EXPORT` = caminho; senão vai para logs) — ver abaixo.
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

## Exportar para o CAD (decisão D22, 09/11/2026)
Botão "Exportar para o CAD" (painel Pré-moldados, `Commands/ExportPiecesCommand` → `Precast/PrecastExport`): o mesmo
levantamento/grupos do `PrecastSurvey` vira um `ArquivoPecas` (`DEB.Core/Intercambio`, JSON "DEB.Pecas") com seção, comprimento,
cota da base, níveis entre a base e o topo, embutimento (parâmetro "Embutimento", null se a família não tiver) e consolos
por face. Faces: PX→1, NY→2, NX→3, PY→4 (as do template do CAD); Bx = b (x), By = h (y). Cotas: `Elevation` dos níveis
(o referencial mostrado); a base = Z do canto do pilar menos (`ProjectElevation − Elevation`). No CAD: ARMIMPORTARPECAS.
Vigas: as de material "Concreto pré-moldado" (as moldadas no local ficam de fora); `PieceAnalyzer` dá eixo/seção/vértices e
`PerfilViga` (Core) acha os dentes Gerber pelo perfil (x, z) de cada ponta — face da ponta mais baixa que h, com topo em h;
comprimento = ponta → início do fundo; `AgrupadorVigas` junta as iguais e as invertidas (giro 180). Não depende dos parâmetros
da família (dx_gerber etc.). Furos de pino ainda não são lidos (ficam os do PADRÃO do CAD).
Seção (09/11/2026, PC2): volume da peça (HOST_VOLUME_COMPUTED) < 95% do de b × h com os dentes = aba/L/T invertido/I → viga NÃO
exportada, com nota (`PerfilViga.ConferirSecao`; ex.: "seção retangular com aba laje 26x170" do Projeto1 media 51 × 170). Volume a
mais (consolo aninhado) passa.
Só lê o modelo. Em 09/11/2026 o AutoRun não rodou (Revit 2025 parado em "manage licensing"): exportação real ainda não testada.
No PC2 (09/11/2026, Revit 2025, cópia do `EST_RVT_ThiagoAdriana-2025.rvt`): `exportarpecas` rodou — 9 grupos, 24 pilares
(modelo sem material pré-moldado: todos os pilares considerados); 0 vigas (nenhuma com "Concreto pré-moldado"): vigas reais ainda
não testadas.

## Conferir peças (09/11/2026, PC2 — `docs/04_arquitetura/conferencia_e_subdivisao_pecas.md`)
Botão "Conferir peças" (painel Pré-moldados, `Commands/ReviewPiecesCommand`): `Precast/PieceReview` levanta vigas e pilares
(SuperComponent nulo), estações nos vértices ao longo do eixo/altura, corta o sólido no meio de cada intervalo
(`BooleanOperationsUtils.CutWithHalfSpace`; a face com normal −eixo no plano = seção, com furos) e junta os trechos iguais
(`DEB.Core/Pecas`: `SecaoPoligonal`, `Subdivisao`, `ConferenciaPecas` — grupos pela GEOMETRIA, invertidas iguais; nome do tipo só
conferido). Janela `Precast/UI/PieceReviewWindow` (abas Vigas/Pilares, só leitura nesta etapa) + `PieceReviewPreview` (elevação
com trechos coloridos pela função, seções cotadas, área/Iy/CG). AutoRun `conferir` → `logs/conferir_*/resultado.txt` + PNG por grupo.
Testado no PC2: Teste_PM (3 grupos de vigas 20×50 com dentes 25×25, pilares 40×40 e 50×50, tudo ok) e Projeto1 (viga com mísula
de 0 a 325 + trecho sem aba + dente: "seção que o CAD ainda não desenha").
Pilares (mesmo dia, pedido do usuário): consolos pelo mesmo reconhecimento do "Pilares pré-moldados" (`PrecastSurvey`), unidos ao
pilar nos cortes horizontais; perfil exato de cada consolo cortado no meio da largura (`ConsoloLevantado`); aba Pilares com as
QUATRO FACES em pé (como no template: face 1 = +x, 2 = −y, 3 = −x, 4 = +y, vistas de fora; consolo de frente, de perfil ou tracejado
atrás), coluna "CONSOLOS (perfil)" ampliada com cotas e chanfros, seções empilhadas na vertical; grupos com giro 90/180/270° = iguais.
**Chanfro do consolo** (relato do usuário): `Describe` media a altura da ponta nos vértices da ponta — o chanfro 2 × 2 do Teste_PM
virava "ponta 21,5" (trapezoidal, CT no catálogo). Agora `PerfilConsolo.Medir` (Core): envoltórias convexas do perfil, chanfro =
último trecho ≤ 5 cm com outra inclinação, altura pelas retas principais → 23,5 (o JSON do CAD também). Próximo (etapa 3): marca editável gravada no Revit, função dos
trechos, aceitar/ignorar, Extensible Storage, exportar pela janela, "Selecionar no modelo".

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
