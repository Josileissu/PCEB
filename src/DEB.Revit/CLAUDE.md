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
- **Assinatura**: o build assina `DEB.Revit.dll`/`DEB.Core.dll` com o certificado `CN=PCEB DEB` do usuário, se existir
  (`Cert:\CurrentUser\My`). Sem assinatura o Revit pede "Sempre carregar" a cada build — e a instância de teste do
  AutoRun fica parada nessa janela (era a demora "irregular" dos testes no PC2). No PC2 o certificado foi criado pelo
  usuário em 05/11/2026 (raiz e editor confiáveis do usuário). Outro computador: exportar o .pfx com senha ou criar um
  igual (`New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=PCEB DEB"` + importar o .cer em TrustedPublisher e Root).
- **PC2** (decisão do usuário, 10/11/2026): pode testar no Revit **2025 e no 2026**, e implementar aqui. O modelo real de teste (168 vigas) é arquivo
  do 2026 (o 2025 para no aviso "versão posterior"). Com o Revit do usuário aberto a DLL fica presa: use o procedimento do `.antigo`. Modelo de teste com embutimento: `criarmodelo` (base −1,00 m).

## Instalador e Release (desde 11/11/2026, 0.2.0)
- `instalador\gerar.bat`: compila 2025 e 2026 com `DeployToRevit=false`, assina com `CN=PCEB DEB` e gera
  `instalador\saida\DEB_Revit_Setup_<versão>.exe` (Inno Setup, por usuário: `%AppData%\Autodesk\Revit\Addins\<ano>`). Versão = `<Version>`
  do csproj. Só gere quando o usuário PEDIR. Não rode o Setup com o Revit dele aberto (o aviso "feche o Revit" vira laço em modo silencioso).
- Release própria: `gh release create revit-v<ver> src/DEB.Revit/instalador/saida/DEB_Revit_Setup_<ver>.exe --target main
  --title "DEB para Revit <ver>" --notes-file <notas> --latest=false` — `--latest=false` mantém a do CAD como "mais recente" (o botão
  "Baixar para CAD" do site usa `releases/latest`; o "Baixar para Revit" procura a última release com `DEB_Revit_Setup_*.exe`).
- Fora deste computador o Revit avisa "editor não verificado" (o certificado é autoassinado): o usuário escolhe "Sempre carregar".

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

## Exportar para o CAD (decisão D22, 09/11/2026)
**Um levantamento só** (branch `pc2/exportar-pela-conferencia`): `Precast/PrecastExport.Build` = `PieceReview.Run` (o da conferência: seções
exatas por corte, consolos pelo perfil) → `DEB.Core/Intercambio/ConversorLevantamento` → `ArquivoPecas` (JSON "DEB.Pecas", versão 1).
O botão "Exportar para o CAD" do painel e o botão "Exportar para o CAD…" da janela "Conferir peças" gravam o MESMO arquivo
(`ExportPiecesCommand.Save`); a janela exporta o levantamento que está mostrando. Não existe mais a medição pela caixa envolvente
e pelo volume (`PerfilViga`, `AgrupadorVigas`, `PrecastExport.Beams` foram removidos).
- **Marca = nome da peça**: vale a marca que as peças têm no modelo (`MarcasPecas`); só grupo sem marca recebe P1…/V1… (número livre).
  Marcas diferentes no mesmo grupo, ou a mesma marca em peças diferentes = nota. Antes a exportação numerava pela ordem de tamanho.
- **Pilar**: b × h = caixa do pilar sem os consolos, no sistema do representante (faces 1 = +x, 2 = −y, 3 = −x, 4 = +y); o trecho
  corrente tem de ter ≥ 95% da área de b × h (chanfro de canto passa; L, circular etc. = não exportado, com nota); trecho mais fino
  (cabeça reduzida, chaves) = exportado com nota "conferir". Embutimento (parâmetro "Embutimento") entra na assinatura do grupo.
- **Viga**: [dente] + corrente + [dente], com alma retangular e, desde o VP-4 (09/11/2026), UMA aba lateral num trecho contínuo
  (`DEB.Core/Pecas/SecaoComAba`: L, T invertido, aba elevada, aba rente ao topo, fundo inclinado; a aba acima do recorte passa pelo
  dente) → `VigaPeca.Aba`, JSON versão 2 só quando há aba. Trecho curto (≤ 20 cm) de seção reduzida dentro da caixa = furo de
  pino/recorte local, ignorado com nota. I, duas saliências no mesmo lado, mísula em toda a altura ou consolo de outra viga = não
  exportada, com o motivo. Invertida = mesmo grupo (giro 180). Detalhes em `src/DEB.Cad/docs/TEMPLATES.md` (VP-4).
- **Material**: o mesmo critério para pilares e vigas — as de "Concreto pré-moldado"; se o modelo não tem nenhuma do tipo, todas, com nota.
- `uid` (UniqueId) de cada elemento vai no JSON como campo opcional (o DEB.Cad 0.1.32 instalado ignora e continua lendo).
- `PrecastSurvey` lê a geometria de cada candidato a consolo uma vez (antes: de novo para cada pilar) e descarta os distantes pela caixa.
AutoRun `exportarpecas` (`SD_EXPORT` = caminho). Só lê o modelo. `conferir` aceita `SD_CONFERIR_INDICES="16,18"` (PNG de grupos de vigas escolhidos).
O modelo real de teste (168 vigas) é do Revit 2026 (o 2025 para no aviso "versão posterior"); no 2026 o AutoRun roda e, desde 10/11/2026, o Revit de teste fecha sozinho também com arquivo LOCAL de modelo compartilhado (o AutoRun responde "não salvar" e "manter a propriedade" às duas janelas da saída; antes a resposta genérica era recusada e a instância ficava aberta). Revit do usuário aberto = DLL presa: renomeie para `.antigo`, copie e assine.
**Aberturas na alma** (relato do usuário, 10/11/2026: viga com 5 furos aparecia em 13 trechos, com seções de "h = 75" cortadas dentro
dos furos): `PieceReview.Aberturas` lê os laços INTERNOS das faces laterais planas (normal ±y) = furos (nas duas faces = passante, numa
= nicho); as bordas deles saem das estações (`Subdivisao.SemBordasDeAberturas`) e o corte é feito fora deles (`Subdivisao.PontoDeCorte`).
A elevação da janela desenha os furos; a viga fica ✖ ("o CAD ainda não desenha aberturas nem o reforço") e não é exportada. Rebaixo no
topo (viga de 20 com recorte de 10 × 15) = ressalto (mesma forma); "aba" com mais de 3/4 da altura não é aba. AutoRun `furos`: corta dois
furos numa viga do Teste_PM (NewOpening) e desfaz no fim; `conferir` aceita `SD_CONFERIR_MARCA`.
**Escolher o que exportar** (pedido do usuário, 10/11/2026): 1ª coluna da janela = caixa "exportar" por grupo (só desta exportação),
botões Todos / Nenhum / Só ✔ / Seleção do Revit por aba e a contagem embaixo. Com peças selecionadas no Revit ao abrir a janela, ela começa
marcando só os grupos delas. "Nunca exportar" (gravado no modelo) continua valendo e desmarca o grupo. Desmarcados = nota no JSON.
**Edições na janela (etapa 3, 09/11/2026)** — as únicas gravações da conferência no modelo (`Precast/PieceReviewEdits`, comando em
`TransactionMode.Manual`): "Gravar marca no modelo" (parâmetro Marca de todas as peças do grupo; a janela recusa a marca que já é de
outro grupo) e "Não exportar este grupo" (Extensible Storage, schema `DEB_ConferenciaPeca`, campo `NaoExportar`, no próprio elemento:
vale nas próximas conferências; ⊘ na lista). AutoRun `conferireditar`: grava VT1/PT1 e um "não exportar", levanta de novo, exporta e
DESFAZ tudo (TransactionGroup.RollBack) → `logs/conferireditar_*/resultado.txt` + PNG. Testado no Teste_PM. Falta: função dos
trechos (sem uso no CAD até o JSON v2), "Selecionar no modelo" e voltar para "a conferir" quando a geometria muda.

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
último trecho ≤ 5 cm com outra inclinação, altura pelas retas principais → 23,5 (o JSON do CAD também).
**Viga apoiada em viga** (relato do usuário, modelo real de uma obra, V245): o consolo é aninhado na viga que SE APOIA e encosta na
face lateral da viga que RECEBE. `PieceReview.BeamCorbels` dá cada consolo sem dono (os dos pilares saem antes) à viga cuja face
lateral REAL (planar, normal ±y — inclui a da alma dentro das abas) ele toca e da qual sai; perfil cortado no meio do consolo;
trecho = "Apoio de viga" (situação ✖: o CAD ainda não desenha consolo em viga). Na cópia do modelo real: V112/V212, V237, V312 e 3
sem marca. "encostaEm" do diagnóstico NÃO serve para isso (consolos de pilar encostam nas vigas vizinhas).
Revit do usuário aberto segura a DLL: renomeie `DEB.Revit.dll`/`DEB.Core.dll` para `.antigo` (o Windows deixa) e copie as novas —
o Revit dele segue com a carregada; os abertos depois pegam a nova. Próximo (etapa 3): marca editável gravada no Revit, função dos
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
