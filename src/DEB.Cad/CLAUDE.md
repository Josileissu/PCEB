# Armadura — plugin de armaduras para GstarCAD (depois AutoCAD)

> Parte do repositório **PCEB** (`src/DEB.Cad`); passará a se chamar **DEB — Detalhador Estrutural Brasileiro** na
> etapa 4 da migração. Regras gerais do projeto: `CLAUDE.md` da raiz.

Especificação: `info_auxiliar/prompt_port_editor_armaduras_gstarcad_autocad.txt`.
Arquitetura, modelo de dados, comandos e fases: `docs/ARQUITETURA.md`. SDK GstarCAD 2021: `sdk/`.

Decisão do usuário: **.NET + Overrules** sobre entidades nativas (LWPOLYLINE + MTEXT/TEXT + TABLE com
XData `ARMADURA_BR`), e não entidade C++ (GRX). O DWG tem de abrir legível sem o plugin.

## Compilar e testar
```powershell
& "C:\Program Files\dotnet\dotnet.exe" test ..\..\tests\DEB.Core.Tests   # núcleo, sem CAD (PCEB\tests)
& "C:\Program Files\dotnet\dotnet.exe" build src\DEB.GstarCAD -c Release  # → bin\GstarCAD2021\
```
- Plugin: `bin\GstarCAD2021\DEB.GstarCAD.dll` (net48 x64; gmap/gmdb do SDK, Private=false).
  Outras versões do GstarCAD (2026-11-03): `-p:GstarVersao=2022…2027` → `bin\GstarCAD<ano>`. SDKs oficiais baixados pelo
  usuário (`sdk\grxsdk_*.zip`, fora do Git); no Git só `sdk\gstarcad\<ano>\ref\`. 2022-2023: gmap/gmdb, `GrxCAD.*`, net48;
  2024-2025: GcMgd/GcDbMgd/GcCoreMgd, `Gssoft.Gscad.*` (`GlobalUsings.Gssoft.cs`), net48; 2026-2027: idem em .NET 8.
  obj por versão no `Directory.Build.props` (`obj\<ano>`, `obj\demo<ano>`). Autoload: `HKCU\SOFTWARE\Gstarsoft\GstarCAD\R<aa>\
  <idioma>\Applications` (guia da 2024 em `info_auxiliar\gstarcad\2024`; manuais Mgd*.chm por ano). Só a 2021 está instalada
  aqui: 2022-2027 compilam, mas NÃO foram testadas.
  O GstarCAD não solta a DLL que carregou; o do usuário carrega uma CÓPIA (carregador, abaixo), então o
  build em `bin\` funciona com ele aberto. Só falha se alguém fez NETLOAD direto da DLL do bin.
- **Autoteste** (os passos de `tools\autoteste.ps1`; rode-os inline, sem `-ExecutionPolicy Bypass`):
  1. `Copy-Item bin\GstarCAD2021\*.dll "$env:LOCALAPPDATA\Programs\ArmaduraBR\GstarCAD2021\" -Force` — com o
     plugin instalado, a instância de teste TAMBÉM carrega a versão instalada (autoload); as duas têm de ser
     a mesma build. (Não estraga o GstarCAD aberto do usuário: ele usa a cópia em `run\`.)
  2. script `.scr` com `(command "_.NETLOAD" "<dll com />")` + `ARMTESTE`, variáveis
     `ARM_AUTOTESTE_SAIDA=<scratchpad>\autoteste.dwg` e `ARM_LOG_DETALHADO=1`, e
     `gcad.exe /nologo /b "<scr>"` numa instância NOVA (a do usuário nunca é tocada).
  Relatório em `%AppData%\PCEB\DEB.Cad\logs\autoteste.txt` (2026-10-27: 174 ok); PNG ao lado do DWG de saída.
  Depois confira o `FILEDIA` em `HKCU\SOFTWARE\Gstarsoft\GstarCAD\R21\en-WW\Profiles\<<Unnamed Profile>>\Config`
  (valor ausente = padrão 1; o GstarCAD só grava o que difere do padrão).
- Antes de encerrar um gcad, confira de onde veio (`Win32_Process` → pai `explorer.exe` = o USUÁRIO abriu).
- Ler o desenho aberto do usuário (só leitura): `[Runtime.InteropServices.Marshal]::GetActiveObject("GStarCAD.Application")`
  → `ActiveDocument.ModelSpace`. Formate números com `InvariantCulture` (o Windows é pt-BR).
- Planilhas do usuário (xlsx): `openpyxl` instalado com `pip --user`; rode `python -E -P script.py` (o `-I`
  ignora o site do usuário). Copie o arquivo para uma pasta própria no scratchpad antes.
- Testes de unidade: `PCEB/tests/DEB.Core.Tests` (108 em 2026-10-27).
- Log do plugin: `%AppData%\PCEB\DEB.Cad\logs\deb_AAAAMMDD.log` (até 03/11/2026: `%AppData%\ArmaduraBR`, migrada sozinha na 1ª execução); configuração do usuário: `%AppData%\PCEB\DEB.Cad\config.json`.

## Estrutura
- `PCEB/src/DEB.Core` (netstandard2.0, sem CAD; desde a etapa 5 fica fora do plugin, comum ao Revit): tabelas, normas, comprimento, descrição, numeração, RA, codec XData.
- `src/DEB.Cad` (fontes compartilhados, sem .csproj): Sincronizador (eventos → fila → CommandEnded),
  ServicoBarra (cotas/descrição), ServicoRA (tabela), Overrule de desenho (curvatura), Comandos, Testes/AutoTeste.
- `src/DEB.GstarCAD`: csproj + `GlobalUsings.cs` (GrxCAD.*). O projeto AutoCAD terá outro GlobalUsings.

## Armadilhas do GstarCAD 2021 (.NET)
- **Nunca deixar uma transação ser abortada (Dispose sem Commit) em eventos como CommandWillStart**: no
  GstarCAD isso desliga as notificações ObjectModified/Erased do comando seguinte. Leituras: sempre Commit.
- `TransactionManager.QueueForGraphicsFlush()` → `eNotImplementedYet`; `FlushGraphics` não existe.
- `Table`: mesclar células DEPOIS de escrever zera a altura do texto da linha anterior → mesclar antes.
- COPY por script fica em modo múltiplo: mande um Enter extra para sair.
- `SendStringToExecute`: os prompts de arquivo não aceitam caminho entre aspas; `PNGOUT` só sem diálogo com FILEDIA=0
  (o autoteste restaura; se a instância for fechada no meio, restaure no registro).
- Paleta: `PaletteSet.Add(nome, Control)` com PropertyGrid WinForms funciona; `COLORTHEME` não dá o tema
  (a paleta fica clara). `System.Drawing.Color` colide com `GrxCAD.Colors.Color` (use alias).
- Para ver a paleta: rode o autoteste SEM `ARM_AUTOTESTE_SAIDA` (a instância fica aberta), capture a janela
  com PrintWindow (user32) e encerre só essa instância.
- `PaletteSet.AutoRollUp`/`RolledUp` lançam NotImplementedException. Com "ocultar automaticamente", a paleta
  recolhida informa `Visible = false`: nunca condicione a leitura da seleção a `Visible`.
- Números em textos de relatório: o Windows é pt-BR ("21,6"); use `FormattableString.Invariant`.
- Objetos de teste novos no autoteste: longe dos existentes (vários testes procuram barras por posição;
  use y = −5000 e x ≥ 12000).
- Overrule (`DesenhoArmadura.WorldDraw`): NÃO use `Geometry.WorldLine` — no GstarCAD ela não aparece nas viewports do
  layout (só os arcos apareciam; relato do usuário, 2026-10-30). Use `Geometry.Polyline(Point3dCollection, normal, IntPtr.Zero)`.
  O autoteste cria uma viewport no Layout1 (ARMTESTELAYOUT) e gera `autoteste_layout_viewport.png`.
- `GraphicsInterface.Polyline` colide com `DatabaseServices.Polyline` e `Runtime.Exception` com `System.Exception`:
  use os apelidos `Gi.` e `Rt.`.

## Convenções
- Desenho SEMPRE em cm (config `UnidadeDesenho = Centimetro`; INSUNITS ignorado). Escala por barra e por
  tabela (XData "ESC", base 1:10); altura do texto = AlturaTextoMm/10 × escala (2 un em 1:10, 5 em 1:25).
- Partes vinculadas: descrição/cota (→ barra), faixa (→ barra; lista "FX"), corte/chamada (→ faixa),
  rebatida (→ barra; lista "RB"). Vínculo só vale se for recíproco. Cópia de faixa/rebatida = nova da
  mesma barra; cópia de barra nasce sem faixas/rebatidas.
- Referências entre objetos no XData = código 1005 (vazio = 1000 ""): o CAD traduz em cópias/blocos.
  `XDataCad` converte Handle ↔ hex. Desenhos antigos (handles em 1000) continuam sendo lidos.
- Detalhamento = bloco (decisão do usuário, 2026-10-26). `ServicoRA.Coletar` percorre os layouts e
  desce nos BlockReference (× inserções); `Desenho.Entidades(incluirBlocos: true)` inclui definições.
- GstarCAD: `GetBlockReferenceIds` só enxerga uma referência nova depois do commit da transação.
- Treliça e tela são `DadosArmadura` com `Trelica`/`Tela` (modelo do catálogo); o desenho gerado são
  partes `TipoParte.Desenho` (lista "PD"). Cordoalha = aço "CP-…" (`TabelaBarras.EhCordoalha`).
- O autoteste NÃO pode deixar valores do usuário alterados no config.json (guarde e restaure; vários
  comandos chamam `Plugin.SalvarConfig()`).
- Detalhe de dobra da tabela: partes `TipoParte.Desenho` com dono = a TABELA (lista "PD" nas opções).
  Posições pelas células reais: `RecomputeTableBlock(true)` + `GetCellExtents` (somar `RowHeight` erra
  quando o CAD cresce linhas de texto quebrado). MOVE da tabela: `ServicoRA.AcompanharMovimento`.
- Estribo fechado (padrão do escritório, exemplo do usuário em 2026-10-27, ESPELHADO): ganchos SEMPRE no canto
  superior ESQUERDO (decisão do usuário); o 1º sai do lado esquerdo e o 2º do lado de cima, cada um passando do canto o diâmetro externo
  de curvatura (pino + 2φ = 2,5 no ø5 CA-60); 90° = perpendicular ao lado (os ganchos se cruzam, como no
  exemplo), 45°/180° = diagonal. Gancho padrão = 90°. As passagens são `Segmento.Sobreposto` (XData "SO"):
  CONTAM como aço (decisão do usuário), mas não são cotadas nem têm medida no esboço da RA.
- Estribo em QUALQUER poligonal fechada: `GeradorEstribo.FechadoPoligono` (sentido horário; ganchos no
  vértice escolhido, padrão superior esquerdo — pedido do usuário para conversões); `ServicoEstribo`
  converte/refaz/desfaz (a volta é recuperada da própria polilinha: vértices 2..n+1). O ARMESTRIBO usa
  o mesmo gerador e o mesmo canto (superior esquerdo).
- Faixa com escala própria (`DadosFaixa.Escala`, 0 = a da barra; padrão das novas = `EscalaFaixa`).
- Chamada/indicação/corte+chamada podem ocultar as linhas de chamada (`DadosFaixa.OcultarLinhas`, XData "OCL";
  [SemLinha] no ARMCHAMADA, "Mostrar linha de chamada" na paleta); os pontos de referência ficam guardados.
  Linha apagada à mão NÃO é recriada: o Sincronizador grava o índice dela (vínculo "IDX") em `LinhasApagadas`
  (XData "LAP"); só "Mostrar linha de chamada" = Sim limpa a lista.
- Faixa em cota (TemCota) mostra o comprimento abaixo da linha: `DimensionText` = texto + "\X" + comprimento
  (`DadosFaixa.MostrarComprimento`, XData "MCMP" só quando desligado; paleta "Mostrar comprimento"; pedido de 2026-10-30).
  Templates: `FaixaTemplate.MostrarComprimento` (grampos ao lado do estribo: desligado).
- Texto da faixa com campos opcionais (quantidade, N, diâmetro, espaçamento; XData MQTD/MPOS/MDIA/MESP só
  quando desligados), em `TextoFaixa.Formatar`; nunca fica vazio (sobra o N). Repetições da faixa (XData "REP")
  multiplicam o que ela soma na barra (`QuantidadeTotal`) e o texto vira "2x11 N1 …" (repetição colada na quantidade, SEM parênteses — pedido de 2026-10-28; vale também para a descrição da barra).
- Tela identificada (T1, T2…): `TelaIdentificada`/`NumeroTela` (XData TID/TN); o T vem de
  `ServicoRA.NumerarTelas` (na renumeração; a paleta e o ARMTELA chamam `ServicoRA.Atualizar`). Texto de
  `FormatadorDescricao.TextoTela`; diagonal em 2 trechos em `ServicoDesenhoGerado` (vão = largura do MText + h).
- Enter do usuário nos textos: `Sincronizador.GuardarQuebras` lê as quebras da descrição/faixa editada e guarda
  o índice da palavra (XData QBR); `QuebrasTexto.Aplicar` reaplica no texto gerado. Só as quebras sobrevivem; o resto
  da edição volta ao texto gerado.
- Autoteste depende da escala de fábrica: `Escala` 10 e `EscalaFaixa` 0 no config do usuário. Com outros valores
  (ele usou 25/50 em 2026-10-28) falham testes de escala e ARMDIVIDIR; para conferir, rode com uma cópia ajustada e
  devolva o config original no fim (try/finally).
- Propriedades rápidas (ARMPALETARAPIDA, `Paleta/PaletaRapida.cs`): `CadApp.ShowModelessDialog` + `ShowWithoutActivation`;
  foco de volta ao desenho com `GrxCAD.Internal.Utils.SetFocusToDwgView()`. Abrir/recolher grupos do PropertyGrid
  só depois da janela visível (escondida, não há GridItems).
- Roteiro do autoteste: COPY termina com ESC (`\x1B\x1B`), não com Enter extra — com COPYMODE de cópia única
  no perfil o Enter repete o COPY e engole o comando seguinte. Não mude o COPYMODE (é do perfil do usuário).
- Numeração (Global/Individual/Sequencial) é do DESENHO: `ConfigDesenho.Alterar` grava no DWG se ele tem
  configuração própria, senão na do usuário. Coluna ELEMENTO e nome no título são opções da TABELA.
- Importação do .cad do QiArmaduras (ARMIMPORTARCAD): `docs/FORMATO_QICAD.md`; exemplos do usuário em
  `auxiliar_cad/` (testes de unidade usam se existirem). Teste no CAD por script: o comando pede o caminho
  como TEXTO quando há script ativo (CMDACTIVE & 4) — NUNCA mudar o FILEDIA para testar (ele é gravado no
  perfil e trava os diálogos do GstarCAD do usuário), e o .scr não pode terminar com Enter sobrando (repete
  o comando fora do script e abre o diálogo de arquivo na tela do usuário).
- Faixas guardam pontos ABSOLUTOS no XData (P1/P2, arco, referências das chamadas): ao deslocar/copiar para
  um bloco, use `ServicoFaixa.Transformar` (o `ServicoDetalhamento.Criar` já usa; ele leva também as âncoras).
  **EXPLODE** (2026-10-29, relato do usuário: chamadas iam para longe): o Sincronizador guarda o `BlockTransform` dos
  blocos apagados durante EXPLODE/XPLODE/BURST e, antes do resto, transforma as faixas de texto novas cuja âncora
  (XData ANX/ANY = posição do texto na última atualização) cai no texto. E em qualquer atualização, se o texto saiu da
  âncora e as linhas de chamada continuam presas a ele (ou o 1º círculo do corte, C0X/C0Y, andou igual), a faixa
  desloca os pontos (MOVE de tudo, edição no lugar); texto movido sozinho não mexe nas referências.
  Com PATAMAR as linhas terminam no início do patamar, não no texto: a detecção compara com o patamar recalculado
  (2026-11-02, relato do usuário: no REFEDIT do P61 as chamadas iam para longe = deslocadas do ponto de inserção).
  Autoteste ARMTESTEMOVERPILAR/ARMTESTEMOVIDO desloca o pilar explodido pela API sem tocar no XData (como o REFEDIT).
  Teste por script: o EXPLODE do GstarCAD pega UM objeto e termina — mande um Enter só.
- Tela soldada: opções por tela no XData ("TDIS" distribuição dos fios, "TCOT" cotas das 2 dimensões, "TDIAG"
  texto na diagonal; padrão: cotas e diagonal ligadas). Fios: `DistribuicaoTela` (Core; longitudinais ao longo do
  lado maior, famílias centradas). Na paleta, `[SoQuando("Tela")]`/`IPropriedadesCondicionais` escondem o que não se aplica.
- Grupo VAR (barras de comprimento variável numa posição): `DadosArmadura.ComprimentosVar` (externos, XData
  "CVAR"); quantidade = nº de itens (faixas não sobrescrevem); RA soma `CalculoComprimento.ComprimentosReaisVar`
  (cada um − desconto das dobras da forma representativa); a assinatura da numeração inclui a lista.
- Templates: `docs/TEMPLATES.md` (ambiente pronto; nenhum template implementado até o usuário enviar os padrões).
- **Comprimento real** (C= e RA; `DescontarDobras`, padrão true): soma externa − desconto por dobra
  (`CalculoComprimento.DescontoDobras`, planilha do usuário `S:\Meu Drive\Planilhas\Estudo_comprimento_real_barra_vergalhao.xlsx`):
  2·re2·tan(δ/2) − re·δ entre retos; no gancho 2·re2 − re·δ com δ NOMINAL (90/135/180°, confirmado pelo
  usuário em 2026-10-27: o ângulo desenhado do gancho é só representação); re2 = D/2+φ,
  re = (D+φ)/2, D = pino da NBR 6118 (tab. 9.1 barras, 9.2 estribos = `RegrasDobramento`). Cotas seguem externas.
  Barra fechada: `GeometriaBarra.Ler` põe no 1º segmento a deflexão em relação ao último.
  Ângulos: vetores dos segmentos a partir dos vértices, atan2(a×b, a·b) (= acos do produto escalar da
  planilha, com sinal). REGEN/REGENALL estão em `ComandosQueAtualizamRA` (config v4): recalculam descrições e tabelas.
- Relação do aço ordenada pela posição N (pedido do usuário, 2026-10-27); por detalhamento quando a numeração é
  Individual/Sequencial (detalhamento, depois N).
- Reconhecimento (ARMRECONHECER, pedido em 2026-10-29): QUALQUER layer (pedido dele), texto → curva mais próxima;
  interpretador no Core (`TextoArmadura`, testes em ReconhecimentoTests). Falta calibrar com um DWG real de outro programa.
- Cotas movidas pelo usuário: `Segmento.CotaMovida` + deslocamento nos eixos do trecho (Dx ao longo, Dy perpendicular,
  "legível"), gravado pelo Sincronizador (`ServicoBarra.RegistrarMovimentoCota`); ARMTEXTOPADRAO zera.
- STRETCH/seleção por janela em script só pega o que está na área visível: objetos de teste em y = −8000 não são
  alcançados — teste a geometria direto (SetPointAt + Atualizar).
- AUTOCOMPLETE do GstarCAD = lista fixa em `%AppData%\Gstarsoft\GstarCAD\R21\en-WW\Support\alias.pgc`
  (lida ao abrir o CAD). `Infra/Autocompletar.cs` acrescenta os comandos do plugin ao carregar
  (backup `alias.pgc.antes_armadura`). Comando novo → aparece nas sugestões após reabrir o GstarCAD.
- Sem ribbon .NET no GstarCAD 2021: a paleta separada "DEB - Comandos" (`Paleta/PainelComandos.cs`,
  comando ARMCOMANDOS, config `MostrarComandos`) faz esse papel; a paleta "Armadura" só tem Propriedades.
- Classe NOVA com `[CommandMethod]` → acrescentar `[assembly: Rt.CommandClass(typeof(...))]`: com a lista, o CAD
  só registra as classes listadas (o ARMTEMPLATE ficou fora na 0.1.9). O autoteste confere isso.
- Botões de paleta: `SendStringToExecute` com comando em andamento vira resposta ao prompt; cancelar com
  `"\x1B\x1B"` (ESC). `\x03` NÃO cancela no GstarCAD. Layout dos botões é manual (AutoSize/FlowLayoutPanel
  aninhados podiam sumir ao re-encaixar). Teste de clique: `BM_CLICK` (SendMessage); cliques postados
  falham (o WinForms confere o cursor real) e NUNCA usar mouse real: o usuário trabalha na mesma tela.
- **Carregador** (`src/DEB.Carregador` → `bin\carregador\DEB.Carregador.dll`): copia
  `..\GstarCAD2021\*.dll` (pasta vizinha: a do bin ou a instalada) para `%LocalAppData%\PCEB\DEB.Cad\run\<data>\` e faz NETLOAD da cópia no Idle,
  então o build em `bin\GstarCAD2021` não fica preso. O usuário carrega o CARREGADOR, nunca a DLL do bin.
  Autoload: chave `HKCU\SOFTWARE\Gstarsoft\GstarCAD\R21\en-WW\Applications\ArmaduraBR` (LOADCTRLS=2,
  MANAGED=1, LOADER=<dll>). Eu não posso gravar essa chave diretamente; quem grava é o instalador.
- **Instalador ÚNICO** (2026-11-03, pedido do usuário; Inno Setup 6 em `%LocalAppData%\Programs\Inno Setup 6`):
  `instalador\DEB.iss` via `instalador\gerar.bat` (compila GstarCAD 2021-2027, AutoCAD 2021-2027 e ZWCAD 2024-2026) →
  `instalador\saida\DEB_Setup_<ver>.exe`. Página de componentes GstarCAD / AutoCAD / ZWCAD: procura no registro
  (`Gstarsoft\GstarCAD\R21…R27`, `Autodesk\AutoCAD\R24.0…R26.0`, `ZWSOFT\ZWCAD\<ano>`, HKLM ou HKCU), marca os encontrados e mostra
  "(encontrado: …)"; o usuário pode desmarcar — desmarcado tira o carregamento de uma instalação anterior. Mesmo AppId do antigo
  instalador do GstarCAD; o antigo só-AutoCAD (AppId 6B7A0FEC…) é desinstalado em silêncio antes. Pede para fechar
  gcad/acad/zwcad. Conferido (página de componentes capturada); instalação real com AutoCAD/ZWCAD ainda não testada.
  GstarCAD (sem admin; instala em `%LocalAppData%\Programs\ArmaduraBR`,
  grava a chave para cada idioma em `R21\`, avisa se o gcad.exe estiver aberto). Testado: instala, carrega
  sozinho ao abrir o GstarCAD e desinstala limpo. Versão = `<Version>` do DEB.GstarCAD.csproj
  (0.1.31 em 2026-11-09: viga pré-moldada completa — armaduras, dente Gerber com ARMDENTES, protensão, cantoneira; 0.1.30 em 2026-11-09: o aviso "feche o CAD" só conta os CADs da sessão do Windows de quem instala — o GstarCAD de
  outro usuário conectado no computador travava a instalação; 0.1.29 em 2026-11-08: bloco pelo motor com variantes por nº de estacas, alça do pilar no fundo; 0.1.27 em 2026-11-06: reservatório e as novidades da main; 0.1.26 = instalador único com GstarCAD 2021-2027, AutoCAD e ZWCAD): SÓ gere o instalador quando o usuário PEDIR (aí: suba a versão, rode `gerar.bat` e
  apague o Setup antigo de `saida\`). NUNCA rode o Setup com algum gcad.exe aberto NA SESSÃO do usuário: em `/VERYSILENT
  /SUPPRESSMSGBOXES` o aviso "feche o GstarCAD" vira laço infinito (atualize copiando as DLLs para a pasta instalada).
- **Release no GitHub** (pedido do usuário, 2026-11-06; 1ª = v0.1.27): os instaladores vão para as Releases do repositório
  (não para o Git). Fluxo quando ele pedir o instalador: subir a versão → `gerar.bat` → commit → merge na `main` → push →
  `gh release create v<ver> src/DEB.Cad/instalador/saida/DEB_Setup_<ver>.exe -R Josileissu/PCEB --target main
  --title "DEB <ver>" --notes-file <notas.md>` (notas em PT: novidades e "feche os CADs e execute o Setup"); confira com
  `gh release view v<ver> --json assets`. O gh precisa estar logado na conta Josileissu (`gh auth status`); o login é do
  usuário (`gh auth login -w`, em janela NÃO administradora) — não use a credencial guardada do Git.
  PC2: gh portátil em `%LocalAppData%\Programs\gh\bin\gh.exe` (o winget de lá está quebrado) e o firewall bloqueia toda
  saída por padrão; o gh tem a regra "GitHub CLI (gh)". Programa novo que precise de rede no PC2 precisa de regra igual (UAC).
- **Demonstração** (pedido do usuário, 2026-10-30): `instalador\gerar_demo.bat` → `instalador\saida\demo_<versão>.exe` (numerada pela versão do plugin, pedido de 2026-10-30) (build com
  `-p:Demo=true` → constante DEMO, saída `bin\demo\GstarCAD2021`; ISCC `/DDemo`). `Infra/Demo.cs`: 1 hora a partir da 1ª carga
  no computador (início em `HKCU\Software\ArmaduraBR\Demo` e `%AppData%\PCEB\DEB.Cad\demo.dat`, vale o mais antigo); depois,
  `Comandos.Executar` e a paleta não alteram nada (log "demonstração expirada"). Testar sem deixar rastro: apague a chave e o
  demo.dat depois. O build demo usa a mesma pasta obj: o .bat recompila a versão normal no fim.
- **Layers** (pedido do usuário, 2026-10-30): padrão em `%AppData%\PCEB\DEB.Cad\layers.csv` (Layer;Cor;Espessura (mm);Tipo de
  linha;Descrição — ELE organiza; criado com o padrão de fábrica `PadroesLayers.Padrao()` se faltar, relido quando muda).
  `Desenho.GarantirLayer` aplica ao CRIAR (fora do arquivo: a cor dada, nunca amarela, e 0,00 mm — nunca a espessura
  "padrão"); ARMLAYERS aplica aos layers existentes do desenho [Abrir] abre o arquivo. Nunca sobrescreva o arquivo dele.
- **Modelos dos templates** (pedido do usuário, 2026-10-30): `%AppData%/PCEB/DEB.Cad/Templates/<id>.csv` (CSV ";", uma linha por
  modelo, coluna por campo; linha PADRÃO = valores com que a janela abre; números com vírgula para o Excel pt-BR; listas
  entre aspas). Core `ArquivoModelos`; Cad `Infra/ModelosTemplates` (carregado ao abrir o CAD, relido quando muda). Todo
  elemento desenhado pelo ARMTEMPLATE é salvo pelo Nome. Janela: Modelo, Salvar como padrão, Excluir, Importar, Exportar,
  Abrir arquivo; comandos ARMTEMPLATEIMPORTAR/ARMTEMPLATEEXPORTAR. O autoteste importa nesse arquivo: o roteiro tem de
  guardar e devolver a pasta Templates (como o config.json).
- **Padrão do escritório para pranchas**: o DWG do P56 (`auxiliar_cad/pilar_padrão/316-CON-DD-100-6113-DET-BAS-R02.dwg`) é a
  referência de layers, cotas e arranjo. Para medir: copie para o scratchpad, ARMTESTEDXF (ARM_DXF_ENTRADA/SAIDA) numa
  instância nova e leia com ezdxf (`python -E -P`). Prévia rápida do template sem CAD: `tools/previa_template.py`.
- Autoteste × config do usuário: a cópia de teste também fixa `TipoNumeracao` = 0 (Global); ele usa Individual.
- Linha de chamada (sem patamar) termina ao lado do texto, 0,4 altura afastada, do lado de onde vem (`ServicoFaixa.PontoNoTexto`;
  pedido de 2026-11-03); `DeslocamentoDoConjunto` reconhece essa ponta para o MOVE de tudo.
- Paleta da faixa: um nome só ("Mostrar quantidade/diâmetro/espaçamento") para todos os tipos — `DadosFaixa.QuantidadeNoTexto`,
  `DiametroNoTexto`, `EspacamentoNoTexto` gravam no "Indicar…" (indicação, padrão desligado) ou no "Mostrar…" (os outros).
- Reaproveitamento (checagem de 2026-11-03): `Infra/Arquivos` (Pedir = diálogo ou caminho digitado em script; Abrir),
  `Core/Persistencia/Csv` (leitor com aspas e separador do Excel, usado por layers.csv e modelos dos templates),
  `ServicoFaixa.AoLadoDoTexto` (ponta da linha e patamar), `AutoTeste.SalvarJanela`.
- O ARMTESTEFIM ENCERRA a própria instância de teste (Process.Kill) depois de salvar: com o layout/viewport o Quit nem sempre
  fechava e cada rodada esperava o limite do roteiro (relato do usuário, 2026-11-03). Encerre sempre só instâncias com o .scr.
- Indicação com quantidade/diâmetro/espaçamento opcionais (`IndicarQuantidade/Diametro/Espacamento`, XData IQT/IDI/IES; quantidade
  digitada, 0 = a da barra; nunca soma). Detalhamento com repetições digitadas (`DadosDetalhamento.Repeticoes`, XData "REP";
  0 = nº de inserções): `ServicoRA.Coletar` troca as inserções pelo valor.
- Indicação/chamada com **patamar** (`DadosFaixa.Patamar`, XData "PAT"; paleta "Patamar na chamada"): as linhas das referências
  terminam num ponto ao lado do texto e um trecho horizontal liga ao texto (índice da linha = 1000). Templates: `FaixaTemplate.Patamar`.
- Cotas dos templates no estilo do escritório: `Infra/EstilosCota` cria ESTRUT{escala} (9/9/9/5/3, tique oblíquo, DIMZIN 8,
  DIMSCALE = escala/50) se o desenho não tiver; se tiver, usa o dele.
- Progresso por etapas: `docs/PROGRESSO.md` (retomar por ele numa sessão nova).
- **Normas** (2026-10-29): os PDFs ficam em `S:\Meu Drive\Normas` (Google Drive, lento). Índice local de texto por
  página em `info_auxiliar/indice_normas/normas.sqlite` (FTS5, sem acentos; fora do Git) + `CATALOGO.md`.
  Consultar: `python -E -P tools/normas/buscar.py "termos" --doc 9062` · `--pagina "NBR 9062" 58` · `--docs filtro`.
  Atualizar (incremental, ~3 min): `python -E -P tools/normas/indexar.py` (rode em segundo plano). As 2 cópias da
  NBR 6118:2023 têm texto embaralhado (fonte deslocada +29: "$UPDGXUDV" = "Armaduras") → use a 2014 (`Antigos`)
  ou decodifique [pendente]. 15 PDFs são escaneados (sem texto).
- **Ler um DWG de referência sem abrir no CAD do usuário**: `ARMTESTEDXF` (em AutoTeste.cs) converte
  `ARM_DXF_ENTRADA` → `ARM_DXF_SAIDA` num banco lateral (script NETLOAD + ARMTESTEDXF numa instância nova; copie o
  DWG para o scratchpad antes). A instância NÃO fecha sozinha: encerre só ela (confira o pai e a linha de comando
  com o .scr). Depois `ezdxf` (instalado com pip --user) lê o DXF.
  **PC2** (sem GstarCAD, sem pip: firewall): `accoreconsole.exe` do AutoCAD 2026 com um .scr `(command "_.DXFOUT" <caminho> 16)`
  + `(command "_.QUIT" "_Y")` — chame pelo PowerShell (o Git Bash troca `/i` por `I:/`) e sem o QUIT o console não fecha.
  Sem ezdxf, o DXF ASCII se lê como pares de linhas (código, valor); cuidado com o nome do bloco corrente: zere-o no ENDBLK.
- Pilar pré-moldado: requisitos e regras conferidas na norma em `docs/PILAR_PREMOLDADO_REQUISITOS.md`; template e
  etapas em `docs/TEMPLATES.md`.
- **Nunca** editar fonte com `Get-Content`/`Set-Content` do Windows PowerShell 5.1: sem `-Encoding` ele lê UTF-8 como ANSI
  e grava os acentos estragados (aconteceu no AutoTeste.cs em 2026-10-29; restaurado do Git). Use Python (io, utf-8) ou
  o Edit. Heredoc do Git Bash troca `\n` por quebra real dentro de strings C# — prefira scripts .py no scratchpad.
- Autoteste × config do usuário: além de Escala/EscalaFaixa, valores "último usado" (ex.: `UltimoEstribo.Gancho`)
  mudam padrões — o teste tem de fixar o valor que verifica e devolvê-lo.

## Pendências [VALIDAR com o engenheiro]
Espelhadas = mesmo N; catálogos (cordoalhas, treliças, telas) e traspasse;
BEDIT (ele confirma). Validados em 2026-10-27 pela planilha/tabela dele: gancho = ponta reta + pino/2 + φ
(medida externa, "compr. reto"), pinos e pontas da NBR 6118 (tab. 9.1/9.2) e comprimento real pelas dobras.

## Git (2026-10-27)
- Git instalado para o usuário: `%LocalAppData%\Programs\Git\cmd\git.exe` (fora do PATH desta sessão; use o caminho).
- Repositório local em `armadura/` (branch `main`), 1º commit 0.1.14. Fora do repositório (decisão do usuário):
  `auxiliar_cad/` (projetos do escritório) e `info_auxiliar/` (especificação); bin/obj e `instalador/saida/` também.
  O `sdk/` VAI (o build referencia `sdk\grxsdk\inc\gmap.dll`/`gmdb.dll`).
- Remoto (2026-11-02): `origin` = https://github.com/Josileissu/PCEB (privado, conta Josileissu via `gh`; renomeado de armadura-gstarcad em 2026-11-03 — será o repositório único do PCEB);
  e-mail dos commits: josileissu@gmail.com. Enviar com `git push`.
- O csproj do plugin tem `IncludeSourceRevisionInInformationalVersion=false`: sem isso, com o Git, a ProductVersion
  ganha "+hash" e o nome do Setup sai errado.

## Port para AutoCAD (2021 a 2027)
- `src/DEB.AutoCAD`: csproj + `GlobalUsings.cs` (Autodesk.AutoCAD.*); compila `src/DEB.Cad` com os defines
  `AUTOCAD` e `AUTOCAD<ano>`. Bibliotecas do pacote OFICIAL "AutoCAD.NET" no NuGet (2026-11-03): compila sem o AutoCAD.
  `dotnet build src\DEB.AutoCAD -c Release -p:AutoCadVersao=<ano>` (padrão 2026) → `bin\AutoCAD<ano>\DEB.AutoCAD.dll`.
  Pacotes: 2021-2024 = 24.0/24.1.51000/24.2/24.3 (net48); 2025 = 25.0.1 e 2026 = 25.1.0 (net8 — a 25.0.0 pede uma versão de
  teste não publicada e as 25.0.2/25.1.1 vieram como net10); 2027 = 26.0.0 (net10: SDK em `%LocalAppData%\Microsoft\dotnet10`,
  instalado com o dotnet-install.ps1 oficial). obj separado por versão (`obj\<ano>`).
- GstarCAD: o SDK oficial fica no repositório (`sdk\grxsdk`). Pacotes de GstarCAD/ZWCAD no NuGet são de TERCEIROS (não usar).
- ZWCAD (2026-11-03): ZRXSDK oficial 2024/2025/2026 (instaladores assinados pela ZWSOFT, baixados pelo usuário em `sdk\*.exe`),
  extraídos com 7-Zip SEM executar em `sdk\zwcad\<ano>\ZWCAD_*` (fora do Git, 337 MB). No Git: `sdk\zwcad\<ano>\ref\`
  (ZwManaged.dll, ZwDatabaseMgd.dll, ZwDatabaseMgdBrep.dll — .NET Framework 4.x, como o GstarCAD). Manuais .NET 2026 em
  `info_auxiliar\zwcad\2026` (os de 2024/2025 dão 404 no site).
- **Port para ZWCAD** (2026-11-03): `src/DEB.ZWCAD` (net48 x64, `GlobalUsings.cs` com `ZwSoft.ZwCAD.*`, defines `ZWCAD` e
  `ZWCAD<ano>`), `-p:ZwcadVersao=2024|2025|2026` (padrão 2026) → `bin\ZWCAD<ano>\DEB.ZWCAD.dll`; demo `-p:Demo=true` →
  `bin\demo\ZWCAD<ano>`. Compila sem erros; NÃO testado (ZWCAD não está instalado aqui). Diferenças de API ficam em
  `Infra/Compat.cs` (`db.TransacaoLeve()`, `ed.ZoomExtensao()`, `db.SalvarDxf()`): o ZWCAD 2024 não tem
  StartOpenCloseTransaction nem Editor.Command, e o DxfOut dele pede um bool a mais. Como no AutoCAD, `EnumConverter` precisa de
  apelido. Autoload (guia .NET do SDK): `HKCU\Software\ZWSOFT\ZWCAD\<ano>\<idioma>\Applications\ArmaduraBR` (LOADER = a própria
  DLL do plugin, LOADCTRLS=2, MANAGED=1) — gravado pelo instalador único.
- Autoteste: `tools\autoteste_autocad.ps1 -Versao <ano>` (instância NOVA do AutoCAD; relatório em
  `%AppData%\PCEB\DEB.Cad\logs\autoteste.txt`). Port da 0.1.21: 129 verificações, 0 falhas (AutoCAD 2026, em outra máquina).
  O roteiro desliga o bundle instalado (PackageContents.xml → .off) durante o teste; o AutoCAD então apaga as chaves
  `Applications\ArmaduraBR` (recria ao abrir com o bundle). Se o roteiro parar no meio, CONFIRA que o .xml voltou.
- Descrição da barra: barra nova nasce girada com o maior segmento; a cópia recria o texto com a posição relativa e a
  ROTAÇÃO do original (texto original, senão `DescricaoRotacao`, XData "DROT"; ausente = 0). Pedidos de 2026-11-05.
- **MATCHPROP/PAINTER** (pedido de 2026-11-05): o Sincronizador copia também os dados do plugin entre objetos do MESMO tipo
  — barra→barra (`DadosArmadura.CopiarPropriedadesDe`: bitola, aço, quantidade, espaçamento, escala, campos…; nunca geometria,
  partes, Id ou N; tipo treliça/tela não muda), faixa/indicação/chamada→idem (`DadosFaixa.CopiarPropriedadesDe`; nunca barra
  dona, tipo ou pontos) e tabela→tabela (`OpcoesTabelaRA.CopiarPropriedadesDe`; não copia os Blocos da RA parcial). Origem =
  1º `PromptForEntityEnding` do comando (ou a pré-seleção de 1 objeto); destinos = seleções + modificados. Clicar num texto,
  cota ou linha de chamada vale pelo objeto dono. Autoteste: `tools\autoteste_autocad.ps1 -Comando ARMTESTEMATCH`.
  GstarCAD/ZWCAD têm os mesmos eventos (compilam), mas NÃO foram testados.
- Faixas em cota ORTOGONAL e ALINHADA nascem sem o diâmetro no texto (`DadosFaixa.MostrarDiametroAoCriar`, ARMFAIXAORT/ALI e
  faixa do ARMESTRIBO; pedido de 2026-11-05). As existentes não mudam (sem "MDIA" no XData = mostra); templates usam o deles.
- Autoteste completo no AutoCAD desta máquina (2026-11-05): trava no STRETCH (janela sem objetos → ARMBARRA fica esperando);
  provavelmente a vista da instância de teste (o ZOOM _E roda com o desenho vazio) — não investigado. Os testes parciais
  (-Comando) funcionam.
- Demo do AutoCAD: `-p:Demo=true` → `bin\demo\AutoCAD<ano>`. A pasta obj (inclusive a restauração do NuGet) é definida no
  `src\DEB.AutoCAD\Directory.Build.props` (`obj\<ano>` / `obj\demo<ano>`): no .csproj ela chegava tarde (aviso MSB3539).
- Instalação no AutoCAD (componente do instalador único; o `gerar_autocad.bat` e o `.iss` próprio foram removidos):
  um bundle só em `%AppData%\Autodesk\ApplicationPlugins\DEB.bundle` (sem admin), com um componente por série
  (R24.0…R26.0) apontando para `Contents\Windows\<ano>\` (`instalador\AutoCAD.bundle\PackageContents.xml`).
- Código compartilhado NÃO usa mais `GrxCAD.*` direto: use os apelidos `PaletteSet`, `PaletteSetStyles` e `CadUtils`
  (definidos nos dois `GlobalUsings.cs`). `Font` ambíguo no AutoCAD → `System.Drawing.Font`.
- GstarCAD continua compilando e com o mesmo comportamento: os apelidos apontam para os mesmos tipos `GrxCAD.*`.
- **Reservatório** (2026-11-05): template `reservatorio` + reconhecedor (Core) + ARMRESERVATORIO; regras e limites em `docs/TEMPLATES.md`.
  Faixa: `ServicoFaixa` chama `RecomputeDimensionBlock` ao mudar o texto (dentro de bloco o plot mostrava "N0").
