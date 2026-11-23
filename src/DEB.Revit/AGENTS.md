# DEB.Revit — Detalhador Estrutural Brasileiro para Revit (ex-StructuralDetailer)

Plugin para Revit 2025/2026 (C#, .NET 8): detalhamento de peças estruturais, com foco em **pilares pré-moldados**
(consolos, armadura, pranchas no padrão do escritório). Projeto/DLL/namespace `DEB.Revit`, aba "Detalhador Estrutural Brasileiro",
`DEB.Revit.addin`; dados em `%AppData%\PCEB\DEB.Revit` (copiados de `%AppData%\StructuralDetailer` na 1ª execução, `Core/UserFolders.cs`).
Mantidos por estarem gravados nos modelos: `Tag = "StructuralDetailer"` das barras, grupo de parâmetros compartilhados
`StructuralDetailer`; também os IDs de botão `SD_*` e as variáveis `SD_AUTORUN`/`SD_AUTOEXIT`/`SD_SAVEAS`. Os **parâmetros** passaram
de `SD_` a `PCEB_` (D38).
Regras gerais do PCEB: `AGENTS.md` da raiz.

Arquivos do escritório (fora do Git — padrões não são públicos), na pasta-mãe do PCEB (`desenvolvedor\`): **modelo-base dos testes `Teste_PM.rvt`** (gerado pelo AutoRun `criarmodelo`:
pilares pré-moldados 40x40 e 50x50 com consolos — grupos de 4 e 2 peças — e vigas "Viga Pré-Moldada" 20x50 com consolos aninhados
e parâmetros de dente Gerber); `Projeto1.rvt` (modelo antigo, obra real com peças moldadas no local); prancha-modelo
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
  Revit do usuário aberto: renomeie `DEB.Revit.dll`/`DEB.Core.dll` instaladas para `.antigo` (o Windows deixa), copie e assine as
  novas — o Revit dele segue com a carregada; os abertos depois pegam a nova.
- Revit 2026: 18 avisos CS0618 (API obsoleta) conhecidos.
- **Assinatura**: o build assina `DEB.Revit.dll`/`DEB.Core.dll` com o certificado `CN=PCEB DEB` do usuário, se existir
  (`Cert:\CurrentUser\My`). Sem assinatura o Revit pede "Sempre carregar" a cada build — e a instância de teste do
  AutoRun fica parada nessa janela. No PC2 o certificado já existe (raiz e editor confiáveis do usuário). Outro computador:
  exportar o .pfx com senha ou criar um igual (`New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=PCEB DEB"` + importar
  o .cer em TrustedPublisher e Root).
- **PC2**: pode testar no Revit **2025 e no 2026**. O modelo real de teste (168 vigas) é arquivo do 2026 (o 2025 para no aviso
  "versão posterior"). Modelo de teste com embutimento: `criarmodelo` (base −1,00 m).

## Instalador e Release
- `instalador\gerar.bat`: compila 2025 e 2026 com `DeployToRevit=false`, assina com `CN=PCEB DEB` e gera
  `instalador\saida\DEB_Revit_Setup_<versão>.exe` (Inno Setup, por usuário: `%AppData%\Autodesk\Revit\Addins\<ano>`). Versão = `VersaoRevit`
  em `Directory.Build.props` (raiz). Só gere quando o usuário PEDIR. Não rode o Setup com o Revit dele aberto (o aviso "feche o Revit" vira laço em modo silencioso).
- Release própria: `gh release create revit-v<ver> src/DEB.Revit/instalador/saida/DEB_Revit_Setup_<ver>.exe --target main
  --title "DEB para Revit <ver>" --notes-file <notas> --latest=false` — `--latest=false` mantém a do CAD como "mais recente" (o botão
  "Baixar para CAD" do site usa `releases/latest`; o "Baixar para Revit" procura a última release com `DEB_Revit_Setup_*.exe`).
- Fora deste computador o Revit avisa "editor não verificado" (o certificado é autoassinado): o usuário escolhe "Sempre carregar".

## Testar sem o usuário (AutoRun)
`src/AutoRun.cs`: abrir o Revit com variáveis de ambiente roda uma tarefa no evento Idling e fecha o Revit.
```powershell
$env:SD_AUTORUN = "precast"   # diag | probe | precast | ferramentas | exportarpecas | conferir | ... (ver abaixo)
$env:SD_COPYFROM = "<.rvt de origem>"   # só "ferramentas": testa as cópias entre arquivos
$env:SD_AUTOEXIT = "1"
$env:SD_SAVEAS = "<scratchpad>\Teste_PM_resultado.rvt"   # resultado vai para uma CÓPIA; o original nunca é salvo
$p = Start-Process "C:\Program Files\Autodesk\Revit 2025\Revit.exe" '"<pasta-mãe do PCEB>\Teste_PM.rvt"' -PassThru
$p.WaitForExit(540000)
```
- `diag`: levantamento do modelo em JSON. `probe`: testes de viabilidade da API (transação desfeita).
  `precast`: fluxo completo de pilares com armadura padrão + PNG da prancha e da janela. `SD_LINHAS=1`: armadura por linha de
  teste (cantos ø20, faces 2 e 4 com redução ø16 → ø12,5, faces 1 e 3 em feixe de 2); o log traz x, y e z das longitudinais.
- `janelas` (sem projeto): grava em PNG as janelas de prévia que não dependem do modelo, com dados de exemplo (hoje a prévia da
  paginação de laje alveolar) — `src/UI/JanelasDemo.cs`. As janelas com abas (vigas, pilares) gravam uma imagem por aba (`janela_2.png`…).
- **Padrão visual das janelas (D41)**: use o kit `src/UI/Estilo.cs` (`Estilo.Aplicar`, `Cabecalho`, `Cartao`, `Selo`, `Campo`,
  `TabelaVerificacoes`, `Grafico`); janela nova de ferramenta pelo `ToolDialogs.NewWindow` (já aplica o kit). Cores só da identidade.
- `vigas`: cálculo e armação das vigas pré-moldadas (D40; `SD_CARGAS`, `SD_SOCALCULO`), ver `docs/VIGAS_PRE_MOLDADAS.md`.
  `SD_VIGA_FAMILIA` (.rfa; copie com nome sem acento), `SD_VIGA_TIPO` e `SD_VIGA_PARAMS` (`b=200;h=500;dz_aba=300;...`, mm, num tipo
  duplicado) trocam a família das vigas do Teste_PM — para testar aba e ressalto com as famílias de `familias_PCEBariantes_PCEB`
  (as de "aba laje e aba superior" são do 2026). A família entra pelo documento dela (`fd.LoadFamily(doc)`): o `doc.LoadFamily` devolvia falso.
- `exportarpecas` (`SD_EXPORT` = caminho), `conferir`, `conferireditar`, `furos`: ver `docs/EXPORTAR_PARA_O_CAD.md` e `docs/CONFERIR_PECAS.md`.
  Tarefas `formas*`: `docs/FORMAS.md`. `contarlegendas`: `docs/DETALHES_PADRAO.md`.
- Sem projeto aberto (`src/Precast/TestModel.cs`): `familias` (relatório de `SD_FAMILIAS`, .rfa separados por `;`) e
  `criarmodelo` (projeto novo de `SD_TEMPLATE` com grade 3×2 de `SD_PILAR` + vigas `SD_VIGA`, gravado em `SD_SAVEAS`).
  Famílias do usuário em `S:\Meu Drive\Arquivos\Familias Revit` (copiar antes; `.000N.rfa` são backups). O template do
  escritório `EST_RVT_Residência.rte` traz uma residência modelada — para teste use `Structural Analysis-DefaultBRAPTB.rte`.
- Famílias do PCEB (D36, `src/Precast/FamiliasPceb*.cs`): `familiassonda` (planos, cotas rotuladas e formas de `SD_FAMILIAS` e das aninhadas)
  e `familiaspceb` (`SD_FAMILIA` = cópia da "Viga Pré-Moldada", `SD_SAVEAS` = saída; apoio no consolo + furos na viga + conferência
  variando os parâmetros; `SD_SOCONFERIR=1` só reabre e confere). Ver `docs/04_arquitetura/familias_apoio_furos.md`.
- `criarmodelo` com `SD_NIVEIS` = "cota_mm:direções;..." (ex.: "3000:X;6000:XY;9000:Y") cria vigas/consolos em vários níveis
  (modelo de teste do PC2: `Teste_PM_niveis.rvt`, pilar de 10 m com 4 consolos em 3 níveis).
- Vigas pré-moldadas com consolos aninhados: a folga vem de `folga_esq`/`folga_dir` da família; **recuo da junta
  (START/END_JOIN_CUTBACK, ½" padrão) e extensões inicial/final da instância têm de ser 0**, senão os consolos se afastam
  do pilar e o levantamento não os encontra.
- O AutoRun fecha sozinho as janelas do Revit (`DialogBoxShowing`); no 2026 o Revit de teste fecha também com arquivo LOCAL de
  modelo compartilhado ("não salvar" e "manter a propriedade"). Abrir arquivo antigo pela API: `LegendFactory.OpenQuietly`.
- `APPDATA` trocado NÃO isola a instância de teste (o Revit usa a pasta conhecida do Windows): o teste usa a DLL instalada.
- Resultados em `%AppData%\PCEB\DEB.Revit\logs\` (`autorun.txt` aponta para o arquivo gerado).
  Leia os PNG exportados para conferir a prancha visualmente.
- Logs de cada execução pelo usuário: `detalhamento_*.log` (exceções com pilha).
  Journals do Revit: `%LocalAppData%\Autodesk\Revit\Autodesk Revit 2025\Journals\`.
- Janelas WPF não são testadas pelo AutoRun (só a lógica): conferir com o usuário.

## Arquitetura
```
src/
  App.cs, AutoRun.cs
  Commands/      DetailPieceCommand (peça avulsa), PrecastColumnsCommand, DiagnosticsCommand, OpenConfigCommand
  Core/          PieceAnalyzer (sistema local), PieceDetailer (assembly → vistas → cotas → folha),
                 ViewFactory, DimensionService, SheetBuilder, GeometryHelper, DetailingConfig/Report
  Precast/       PrecastColumn (ColumnFrame, Corbel, PrecastSurvey: levantamento e grupos),
                 ColumnRebarPresets (modelos salvos); ColumnRebarSpec, ColumnRebarLayout, Loc/Corbel/ColumnBox no núcleo
                 (DEB.Core/Elementos/Pilar/PreMoldado/Modelo3D: geometria das barras — usada pela prévia E pelo Revit);
                 longitudinais pela armadura por linha (D32, `ArmaduraPorLinha.cs`, a mesma do CAD): `ColumnRebarSpec.ArmaduraPorLinha`
                 (modelo salvo sem ela = linhas dos campos simples), tabela "Armadura por linha" na janela,
                 RebarFactory, PrecastColumnPipeline, UI/ (janela WPF em código, sem XAML)
  Presentation/  PresentationStandard (JSON por elemento em %AppData%\PCEB\DEB.Revit\Padroes),
                 Defaults (padrão de fábrica = PP127), StandardDetailer, SheetComposer (regiões),
                 RebarSchedules, RebarData (partição N1.., parâmetros PCEB_Aço / PCEB_Peso), AnnotationStyles
  LajeAlveolar/  paginação de laje alveolar (docs/LAJE_ALVEOLAR.md)
  Formas/        pranchas de formas (docs/FORMAS.md)
  Tools/         painel "Ferramentas" (docs/FERRAMENTAS.md)
  Vigas/         vigas pré-moldadas: cargas PCEB_, cálculo e armação pelo núcleo, janela com prévia (docs/VIGAS_PRE_MOLDADAS.md, D40)
```
Fluxo dos pré-moldados: levantamento → grupos de peças iguais → janela (armadura por grupo) →
para cada pilar: limpar → marca → barras → dados das barras → assembly (pilar + consolos + barras) →
o 1º do grupo gera a prancha pelo padrão; os demais caem no mesmo tipo de assembly e reaproveitam a folha.

Detalhe de cada funcionalidade (consulte quando for mexer nela), em `docs/`: `EXPORTAR_PARA_O_CAD.md` (D22),
`CONFERIR_PECAS.md`, `LAJE_ALVEOLAR.md` (D24), `FORMAS.md` (D25), `FERRAMENTAS.md`, `DETALHES_PADRAO.md`.

## Decisões do usuário (não reabrir sem ele pedir)
- Consolos são família "Consolo" **aninhada e compartilhada na viga**; entram no **assembly do pilar**
  (testado: funciona; não usar Parts).
- Peças iguais **ou giradas** 90/180/270° = mesma marca; espelhadas não.
- Armaduras: longitudinal + estribos, zonas de estribos (topo/base/consolo), consolo (tirante + costura),
  içamento/insertos.
- Pranchas guiadas por padrões de apresentação (pilar, viga, laje, fundação).
- Parâmetros do plugin com prefixo **`PCEB_`** (D38, 18/11/2026: troca direta de `SD_`, sem migrar os modelos antigos); os **GUIDs
  continuam os mesmos** (as famílias de anotação do escritório leem os rótulos pelo GUID).
- Tabelas, rótulos e peso das barras pelo comprimento do **núcleo** (D30, 16/11/2026): `PCEB_Comprimento (núcleo)` e
  `PCEB_Comprimento total (núcleo)` (GUIDs fixos `5f0c3a8e-…-1d2b3c4d5e03`/`…5e04`, nunca mudar), gravados na criação da barra
  (`RebarData.Apply`). São valores gravados: barra editada à mão no Revit só se atualiza rodando o comando de novo.
- Parâmetros de Formas: compartilhados com GUIDs FIXOS `8e2a5c10-4b7d-4e61-9f3a-0a1b2c3dNNNN` (nunca mudar).
- Parâmetros de carga das vigas (D40): compartilhados com GUIDs FIXOS `3c7d9a20-5e1f-4a2b-8c3d-7b6a5f4e00NN` (nunca mudar).

## Armadilhas já encontradas
- Guardar `Solid` sem o `GeometryElement` dono → SEHException (GC libera a memória nativa).
  Ler todas as faces **antes** de criar cotas (criar cota regenera e invalida a geometria do corte).
- Assemblies idênticos: o Revit junta no mesmo tipo e **apaga vistas novas**; nunca renomear um gêmeo.
- `RenderTargetBitmap` (PNG das janelas no AutoRun) sai TRANSPARENTE com a sessão do Windows bloqueada (madrugada): não é erro do
  código; as vistas exportadas pelo Revit saem normais.
- API 2025: `GeometryInstance.GetSymbolGeometryId().SymbolId` (não há `.Symbol`);
  `RebarBarType.SetBarTypeDiameters(BarTypeDiameterOptions)`; sem parâmetro de peso de barra e sem
  campos calculados via API (por isso `PCEB_Peso (kg)`). Documentação local: `C:\Program Files\Autodesk\Revit 2025\RevitAPI.xml` (grep).
- Tabelas/textos na folha: posicionar por `ScheduleSheetInstance.Point` / `TextNote.Coord`, não pelo bounding box.
- A API não cria legenda do zero: `LegendFactory` duplica uma legenda existente (ver `docs/DETALHES_PADRAO.md`).
- Ler `TagText` do tipo "NV" de uma família de pilar do escritório derruba o Revit (queda nativa): a sondagem `formassonda` pula
  os tipos com " NV "; só em instância de teste.
- **Editar famílias pela API** (18/11/2026): geometria só segue os parâmetros quando FACES PLANAS ficam presas a planos de referência
  (`NewAlignment`) ou quando as linhas do esboço nascem sobre os planos (o Revit prende sozinho). Numa família NÃO dá: `SketchEditScope`
  (só projeto), cotar o centro de um círculo de esboço ("Invalid number of references"), cota rotulada a aresta/face cilíndrica (entra,
  mas não move a geometria) nem vazio aninhado cortando a forma (`InstanceVoidCutUtils` só em projeto). Furo redondo = polígono de 16 lados.
  Fórmula não compara texto (use inteiro) nem liga parâmetro de tipo a um de instância. A instância de teste às vezes não fecha
  sozinha depois de `familias*`: feche pelo PID que você abriu.
- Python que lê arquivos baixados: usar `-I`; `pymupdf` está instalado no usuário (renderizar PDFs em blocos).
- AutoCAD 2026 está no PC2: `accoreconsole.exe` converte DWG↔DXF e plota PNG sem janela (README de `tools/detalhes_dxf`).
