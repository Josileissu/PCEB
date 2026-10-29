# Armadura — plugin de armaduras para GstarCAD (depois AutoCAD)

Especificação: `info_auxiliar/prompt_port_editor_armaduras_gstarcad_autocad.txt`.
Arquitetura, modelo de dados, comandos e fases: `docs/ARQUITETURA.md`. SDK GstarCAD 2021: `sdk/`.

Decisão do usuário: **.NET + Overrules** sobre entidades nativas (LWPOLYLINE + MTEXT/TEXT + TABLE com
XData `ARMADURA_BR`), e não entidade C++ (GRX). O DWG tem de abrir legível sem o plugin.

## Compilar e testar
```powershell
& "C:\Program Files\dotnet\dotnet.exe" test tests\Armadura.Core.Tests        # núcleo, sem CAD
& "C:\Program Files\dotnet\dotnet.exe" build src\Armadura.GstarCAD -c Release  # → bin\GstarCAD2021\
```
- Plugin: `bin\GstarCAD2021\ArmaduraBR.GstarCAD.dll` (net48 x64; gmap/gmdb do SDK, Private=false).
  O GstarCAD não solta a DLL que carregou; o do usuário carrega uma CÓPIA (carregador, abaixo), então o
  build em `bin\` funciona com ele aberto. Só falha se alguém fez NETLOAD direto da DLL do bin.
- **Autoteste** (os passos de `tools\autoteste.ps1`; rode-os inline, sem `-ExecutionPolicy Bypass`):
  1. `Copy-Item bin\GstarCAD2021\*.dll "$env:LOCALAPPDATA\Programs\ArmaduraBR\GstarCAD2021\" -Force` — com o
     plugin instalado, a instância de teste TAMBÉM carrega a versão instalada (autoload); as duas têm de ser
     a mesma build. (Não estraga o GstarCAD aberto do usuário: ele usa a cópia em `run\`.)
  2. script `.scr` com `(command "_.NETLOAD" "<dll com />")` + `ARMTESTE`, variáveis
     `ARM_AUTOTESTE_SAIDA=<scratchpad>\autoteste.dwg` e `ARM_LOG_DETALHADO=1`, e
     `gcad.exe /nologo /b "<scr>"` numa instância NOVA (a do usuário nunca é tocada).
  Relatório em `%AppData%\ArmaduraBR\logs\autoteste.txt` (2026-10-27: 174 ok); PNG ao lado do DWG de saída.
  Depois confira o `FILEDIA` em `HKCU\SOFTWARE\Gstarsoft\GstarCAD\R21\en-WW\Profiles\<<Unnamed Profile>>\Config`
  (valor ausente = padrão 1; o GstarCAD só grava o que difere do padrão).
- Antes de encerrar um gcad, confira de onde veio (`Win32_Process` → pai `explorer.exe` = o USUÁRIO abriu).
- Ler o desenho aberto do usuário (só leitura): `[Runtime.InteropServices.Marshal]::GetActiveObject("GStarCAD.Application")`
  → `ActiveDocument.ModelSpace`. Formate números com `InvariantCulture` (o Windows é pt-BR).
- Planilhas do usuário (xlsx): `openpyxl` instalado com `pip --user`; rode `python -E -P script.py` (o `-I`
  ignora o site do usuário). Copie o arquivo para uma pasta própria no scratchpad antes.
- Testes de unidade: `tests/Armadura.Core.Tests` (108 em 2026-10-27).
- Log do plugin: `%AppData%\ArmaduraBR\logs\armadura_AAAAMMDD.log`; configuração do usuário: `%AppData%\ArmaduraBR\config.json`.

## Estrutura
- `src/Armadura.Core` (netstandard2.0, sem CAD): tabelas, normas, comprimento, descrição, numeração, RA, codec XData.
- `src/Armadura.Cad` (fontes compartilhados, sem .csproj): Sincronizador (eventos → fila → CommandEnded),
  ServicoBarra (cotas/descrição), ServicoRA (tabela), Overrule de desenho (curvatura), Comandos, Testes/AutoTeste.
- `src/Armadura.GstarCAD`: csproj + `GlobalUsings.cs` (GrxCAD.*). O projeto AutoCAD terá outro GlobalUsings.

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
  um bloco, use `ServicoFaixa.Transformar` (o `ServicoDetalhamento.Criar` já usa). O MOVE comum ainda não
  atualiza as referências das chamadas [pendente].
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
- Sem ribbon .NET no GstarCAD 2021: a paleta separada "Armadura - Comandos" (`Paleta/PainelComandos.cs`,
  comando ARMCOMANDOS, config `MostrarComandos`) faz esse papel; a paleta "Armadura" só tem Propriedades.
- Classe NOVA com `[CommandMethod]` → acrescentar `[assembly: Rt.CommandClass(typeof(...))]`: com a lista, o CAD
  só registra as classes listadas (o ARMTEMPLATE ficou fora na 0.1.9). O autoteste confere isso.
- Botões de paleta: `SendStringToExecute` com comando em andamento vira resposta ao prompt; cancelar com
  `"\x1B\x1B"` (ESC). `\x03` NÃO cancela no GstarCAD. Layout dos botões é manual (AutoSize/FlowLayoutPanel
  aninhados podiam sumir ao re-encaixar). Teste de clique: `BM_CLICK` (SendMessage); cliques postados
  falham (o WinForms confere o cursor real) e NUNCA usar mouse real: o usuário trabalha na mesma tela.
- **Carregador** (`src/Armadura.Carregador` → `bin\carregador\ArmaduraBR.Carregador.dll`): copia
  `..\GstarCAD2021\*.dll` (pasta vizinha: a do bin ou a instalada) para `%LocalAppData%\ArmaduraBR\run\<data>\` e faz NETLOAD da cópia no Idle,
  então o build em `bin\GstarCAD2021` não fica preso. O usuário carrega o CARREGADOR, nunca a DLL do bin.
  Autoload: chave `HKCU\SOFTWARE\Gstarsoft\GstarCAD\R21\en-WW\Applications\ArmaduraBR` (LOADCTRLS=2,
  MANAGED=1, LOADER=<dll>). Eu não posso gravar essa chave diretamente; quem grava é o instalador.
- **Instalador** (Inno Setup 6 em `%LocalAppData%\Programs\Inno Setup 6`): `instalador\gerar.bat` →
  `instalador\saida\ArmaduraBR_Setup_<ver>.exe` (sem admin; instala em `%LocalAppData%\Programs\ArmaduraBR`,
  grava a chave para cada idioma em `R21\`, avisa se o gcad.exe estiver aberto). Testado: instala, carrega
  sozinho ao abrir o GstarCAD e desinstala limpo. Versão = `<Version>` do Armadura.GstarCAD.csproj
  (0.1.17 em 2026-10-29): SÓ gere o instalador quando o usuário PEDIR (aí: suba a versão, rode `gerar.bat` e
  apague o Setup antigo de `saida\`). NUNCA rode o Setup com algum gcad.exe aberto: em `/VERYSILENT
  /SUPPRESSMSGBOXES` o aviso "feche o GstarCAD" vira laço infinito (atualize copiando as DLLs para a pasta instalada).
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
- Pilar pré-moldado: requisitos e regras conferidas na norma em `docs/PILAR_PREMOLDADO_REQUISITOS.md`.

## Pendências [VALIDAR com o engenheiro]
Espelhadas = mesmo N; catálogos (cordoalhas, treliças, telas) e traspasse;
BEDIT (ele confirma). Validados em 2026-10-27 pela planilha/tabela dele: gancho = ponta reta + pino/2 + φ
(medida externa, "compr. reto"), pinos e pontas da NBR 6118 (tab. 9.1/9.2) e comprimento real pelas dobras.

## Git (2026-10-27)
- Git instalado para o usuário: `%LocalAppData%\Programs\Git\cmd\git.exe` (fora do PATH desta sessão; use o caminho).
- Repositório local em `armadura/` (branch `main`), 1º commit 0.1.14. Fora do repositório (decisão do usuário):
  `auxiliar_cad/` (projetos do escritório) e `info_auxiliar/` (especificação); bin/obj e `instalador/saida/` também.
  O `sdk/` VAI (o build referencia `sdk\grxsdk\inc\gmap.dll`/`gmdb.dll`).
- Remoto: ainda não existe — o usuário vai criar um repositório VAZIO e passar a URL; aí `git remote add origin <url>` e `git push -u origin main`.
- O csproj do plugin tem `IncludeSourceRevisionInInformationalVersion=false`: sem isso, com o Git, a ProductVersion
  ganha "+hash" e o nome do Setup sai errado.
