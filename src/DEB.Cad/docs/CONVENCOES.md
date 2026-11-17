# DEB.Cad — convenções e detalhes das funcionalidades

Consulte quando for mexer na funcionalidade. O que vale em toda sessão (compilar, testar, instalar, armadilhas) está no
`AGENTS.md` do DEB.Cad.

## Convenções
- **Unidades** (pedido de 2026-11-10): o desenho pode ser feito em cm, m ou mm (`UnidadeDesenho`; 1 unidade do CAD = 1 cm/1 m/1 mm) e as medidas
  das barras ESCRITAS em cm, m ou mm (`UnidadeApresentacao`): cotas dos segmentos, C=, c/, comprimento das faixas, relação do aço e as cotas do
  CAD dos templates (estilo ESTRUTnn-MM-UM: DIMLFAC = unidades escritas por unidade do desenho). Tudo passa por `CalculoComprimento.Comprimento`/
  `Espacamento` (Core). Por desenho: ARMUNIDADES grava o registro DEB_UNIDADES no DWG, sobreposto à configuração (`ConfigDesenho`); sem ele valem
  os padrões da configuração (cm/cm). A geometria NÃO é convertida. A paleta continua mostrando cm (rotulado). Autoteste: ARMTESTEUNIDADES.
  Antes: desenho SEMPRE em cm. Escala por barra e por
  tabela (XData "ESC", base 1:10); altura do texto = AlturaTextoMm/10 × escala (2 un em 1:10, 5 em 1:25).
- Partes vinculadas: descrição/cota (→ barra), faixa (→ barra; lista "FX"), corte/chamada (→ faixa),
  rebatida (→ barra; lista "RB"). Vínculo só vale se for recíproco. Cópia de faixa/rebatida = nova da
  mesma barra; cópia de barra nasce sem faixas/rebatidas.
- Detalhamento = bloco (decisão do usuário, 2026-10-26). `ServicoRA.Coletar` percorre os layouts e
  desce nos BlockReference (× inserções); `Desenho.Entidades(incluirBlocos: true)` inclui definições.
- Treliça e tela são `DadosArmadura` com `Trelica`/`Tela` (modelo do catálogo); o desenho gerado são
  partes `TipoParte.Desenho` (lista "PD"). Cordoalha = aço "CP-…" (`TabelaBarras.EhCordoalha`).
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
- Propriedades rápidas (ARMPALETARAPIDA, `Paleta/PaletaRapida.cs`): `CadApp.ShowModelessDialog` + `ShowWithoutActivation`;
  foco de volta ao desenho com `GrxCAD.Internal.Utils.SetFocusToDwgView()`. Abrir/recolher grupos do PropertyGrid
  só depois da janela visível (escondida, não há GridItems).
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
  **EXPLODE de uma BARRA** (polilinha da armadura) NÃO transfere nada: a barra é apagada e o Sincronizador apaga descrição,
  cotas e faixas (caminho "barra apagada", `ServicoBarra.ApagarPartes`). O tratamento de EXPLODE é só para faixas dentro de blocos.
- **JOIN / PEDIT→Juntar / "Juntar"** (pedido de 2026-11-12, aprovado pelo usuário): o GstarCAD 2021 cria uma polilinha NOVA
  (sem XData) e apaga a armadura e o objeto juntado — sempre, seja quem for selecionado primeiro; quando os sentidos não
  batem, mantém o da LINE e INVERTE a polilinha (diagnóstico `ARMTESTEJOINLOG`). O PEDIT feito NA armadura edita no lugar
  (mesmo handle); o PEDIT feito na LINE converte a linha (polilinha nova) e apaga a armadura.
  `Sincronizador` (nome GLOBAL do comando: JOIN/PEDIT — cobre o "Juntar" traduzido): guarda as armaduras apagadas e as
  polilinhas anexadas durante o comando; no fim (`Juntar`, antes do caminho de barra apagada) acha a armadura dentro da
  polilinha nova (`Core/Calculo/JuncaoBarra.Localizar`, direto ou inverso, tolerância 1e-4), faz UNERASE, copia a geometria
  NO SENTIDO DA BARRA (`NoSentidoDaBarra`), realoca os dados por trecho (`Realocar`: trechos novos retos, cotados, sem gancho;
  gancho em que a linha encostou vira reto) e apaga a polilinha nova — handle, XData, descrição, cotas, faixas continuam.
  Duas armaduras juntadas: fica a de MAIOR comprimento real (decisão do usuário); a outra continua apagada com as partes e a
  linha de comando avisa qual N ficou e qual saiu. Linha que não encosta: nada muda. Polilinha juntada FECHADA: não tratada.
  PEDIT na própria armadura com o trecho no INÍCIO: `ServicoBarra.Atualizar` realinha os dados pelos comprimentos registrados
  (`JuncaoBarra.TrechosAntes`) — antes o `AjustarSegmentos` só mexia no fim e desalinhava gancho/cotas.
  A relação do aço NÃO se atualiza sozinha no JOIN (igual à barra esticada; JOIN não está em `ComandosQueAtualizamRA`).
  U logo depois do JOIN volta tudo (a correção feita no fim do comando entra no mesmo grupo de desfazer) — testado no GstarCAD.
  Autoteste: `ARMTESTEJOIN` (passa/falha; 2026-11-12: 124 ok no GstarCAD 2021; relatório `logs\autoteste_join_teste_<CAD>.txt`
  e `logs\autoteste.txt`). **AutoCAD ainda NÃO testado** (não instalado no PC1): rode `tools\autoteste_autocad.ps1 -Comando
  ARMTESTEJOIN` no PC2 — se o JOIN do AutoCAD editar a armadura no lugar em vez de criar uma nova, vale o realinhamento por
  comprimentos, mas a regra "fica a mais longa" não se aplica (fica a de origem); se ele fundir trechos colineares (tirar o
  vértice do encontro), a armadura não é achada na polilinha nova e as partes se perdem como antes.
  Armadilhas do teste por script: no GstarCAD o ESC tem de ir SOZINHO no `SendStringToExecute` (`"\x1B\x1BARM…"` virou o
  comando desconhecido "\x1BRM…" e a fila parou); JOIN = "Select source object or a merger of multiple objects" — 2 seleções
  e UM Enter; PEDIT = "Select polyline or [Multiple]", opções, `_J`, "Select object:", Enter fecha a seleção e outro Enter sai;
  com PEDITACCEPT=0 (padrão do GstarCAD) o PEDIT numa LINE pergunta "Do you want to turn it into polyline? <Y>" — leia a
  variável e responda conforme (não mude: é do perfil). Os eventos `PromptingFor*` disparam nos comandos nativos, mas quase
  sempre com a mensagem vazia. Faixas ÓRFÃS (barra apagada com o Sincronizador suspenso) recebem ObjectModified do plugin a
  cada faixa apagada em outra barra: num autoteste, limpe a área de cada caso.
- **ARMCONVERTER com linhas soltas** (pedido de 2026-11-12): aceita LINE além de LWPOLYLINE. Linhas que se encostam ponta com
  ponta (tolerância 0,01 cm) viram UMA polilinha nova na ordem das ligações (`JuncaoBarra.Encadear`), que substitui as linhas;
  linha isolada = barra reta; cadeia fechada = poligonal fechada (pergunta do estribo, como antes); 3+ linhas no mesmo ponto =
  grupo ramificado, avisa e NÃO converte. O ARMRECONHECER já aceitava LINE (cada linha = uma barra, casada com o texto mais
  próximo) e ficou como estava. Teste: caso "conv" do `ARMTESTEJOIN`.
- Tela soldada: opções por tela no XData ("TDIS" distribuição dos fios, "TCOT" cotas das 2 dimensões, "TDIAG"
  texto na diagonal; padrão: cotas e diagonal ligadas). Fios: `DistribuicaoTela` (Core; longitudinais ao longo do
  lado maior, famílias centradas). Na paleta, `[SoQuando("Tela")]`/`IPropriedadesCondicionais` escondem o que não se aplica.
- Grupo VAR (barras de comprimento variável numa posição): `DadosArmadura.ComprimentosVar` (externos, XData
  "CVAR"); quantidade = nº de itens (faixas não sobrescrevem); RA soma `CalculoComprimento.ComprimentosReaisVar`
  (cada um − desconto das dobras da forma representativa); a assinatura da numeração inclui a lista.
- **Comprimento real** (C= e RA; `DescontarDobras`, padrão true): soma externa − desconto por dobra
  (`CalculoComprimento.DescontoDobras`, planilha do usuário `S:\Meu Drive\Planilhas\Estudo_comprimento_real_barra_vergalhao.xlsx`):
  2·re2·tan(δ/2) − re·δ entre retos; no gancho 2·re2 − re·δ com δ NOMINAL (90/135/180°, confirmado pelo
  usuário em 2026-10-27: o ângulo desenhado do gancho é só representação); re2 = D/2+φ,
  re = (D+φ)/2, D = pino da NBR 6118 (tab. 9.1 barras, 9.2 estribos = `RegrasDobramento`). Cotas seguem externas.
  Barra fechada: `GeometriaBarra.Ler` põe no 1º segmento a deflexão em relação ao último.
  Ângulos: vetores dos segmentos a partir dos vértices, atan2(a×b, a·b) (= acos do produto escalar da
  planilha, com sinal). REGEN/REGENALL estão em `ComandosQueAtualizamRA` (config v4): recalculam descrições e tabelas.
- **Coluna do detalhamento** (pedido de 2026-11-10): padrão das tabelas NOVAS = `ConfiguracaoArmadura.ColunaElementoPadrao` (Nunca); com a
  coluna, `MontadorTabela` deixa o nome só na 1ª linha de cada detalhamento e mescla as de baixo (`ConteudoTabela.MesclasVerticais`, texto
  no alto da célula). Escalas das listas da paleta = `ConfiguracaoArmadura.EscalasUsuais` ("Escalas da lista" no ARMCONFIG).
- Relação do aço ordenada pela posição N (pedido do usuário, 2026-10-27); por detalhamento quando a numeração é
  Individual/Sequencial (detalhamento, depois N).
- Reconhecimento (ARMRECONHECER, pedido em 2026-10-29): QUALQUER layer (pedido dele), texto → curva mais próxima;
  interpretador no Core (`TextoArmadura`, testes em ReconhecimentoTests). Falta calibrar com um DWG real de outro programa.
- Cotas movidas pelo usuário: `Segmento.CotaMovida` + deslocamento nos eixos do trecho (Dx ao longo, Dy perpendicular,
  "legível"), gravado pelo Sincronizador (`ServicoBarra.RegistrarMovimentoCota`); ARMTEXTOPADRAO zera.
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
- **Padrão do escritório para pranchas**: o DWG do P56 (`auxiliar_cad/pilar_padrão/<arquivo do projeto>.dwg`) é a
  referência de layers, cotas e arranjo. Para medir: copie para o scratchpad, ARMTESTEDXF (ARM_DXF_ENTRADA/SAIDA) numa
  instância nova e leia com ezdxf (`python -E -P`). Prévia rápida do template sem CAD: `tools/previa_template.py`.
- Linha de chamada (sem patamar) termina ao lado do texto, 0,4 altura afastada, do lado de onde vem (`ServicoFaixa.PontoNoTexto`;
  pedido de 2026-11-03); `DeslocamentoDoConjunto` reconhece essa ponta para o MOVE de tudo.
- Paleta da faixa: um nome só ("Mostrar quantidade/diâmetro/espaçamento") para todos os tipos — `DadosFaixa.QuantidadeNoTexto`,
  `DiametroNoTexto`, `EspacamentoNoTexto` gravam no "Indicar…" (indicação, padrão desligado) ou no "Mostrar…" (os outros).
- Indicação com quantidade/diâmetro/espaçamento opcionais (`IndicarQuantidade/Diametro/Espacamento`, XData IQT/IDI/IES; quantidade
  digitada, 0 = a da barra; nunca soma). Detalhamento com repetições digitadas (`DadosDetalhamento.Repeticoes`, XData "REP";
  0 = nº de inserções): `ServicoRA.Coletar` troca as inserções pelo valor.
- Indicação/chamada com **patamar** (`DadosFaixa.Patamar`, XData "PAT"; paleta "Patamar na chamada"): as linhas das referências
  terminam num ponto ao lado do texto e um trecho horizontal liga ao texto (índice da linha = 1000). Templates: `FaixaTemplate.Patamar`.
- Cotas dos templates no estilo do escritório: `Infra/EstilosCota` cria ESTRUT{escala} (9/9/9/5/3, tique oblíquo, DIMZIN 8,
  DIMSCALE = escala/50) se o desenho não tiver; se tiver, usa o dele.
- Pilar pré-moldado: requisitos e regras conferidas na norma em `src/DEB.Core/Elementos/Pilar/PreMoldado/REQUISITOS.md`; template e
  etapas em `docs/TEMPLATES.md`.

## PDFs em lote (ARMPDFLOTE, pedido do usuário de 2026-11-17)
- Janela (`Paleta/FormPdfLote.cs`): origem = desenhos abertos (carregados ao abrir), arquivos DWG (multisseleção) ou pasta (com
  subpastas); lista desenho × layout (sem o Model) com caixinha, medida, formato reconhecido (combo para trocar; "?" = escolher)
  e "reconhecido por". Impressora (só a **PDF24** salva sem diálogo), estilo de plotagem (ctb), área (moldura reconhecida em
  janela, extensão ou limites), destino (pasta de cada DWG ou uma pasta), sobrescrever sem perguntar, gravar no layout (só
  desenhos abertos; o usuário salva), abrir a pasta no fim. Escolhas em `%AppData%\PCEB\DEB.Cad\pdflote.json` (`ConfigLotePdf`,
  inclusive a lista de formatos). Layout vazio entra desmarcado. Botão "PDFs em lote" na paleta DEB - Comandos (Geral).
- Nome: o do DWG (`FormatosFolha.NomePdf`); com 2+ layouts marcados do mesmo DWG, " - <layout>". Já existe? Pergunta Sim (todos) /
  Não (pula) / Cancelar, salvo "sobrescrever".
- **Formato** (`DEB.Core/Documentacao/LotePdf.cs` + `Pdf/FolhasPdf.cs`): no espaço do papel, candidatos = polilinhas retangulares
  e blocos grandes; vale o MAIOR que casa com um formato da lista (medidas ordenadas, ±10 mm; layout em mm, senão cm/pol/m).
  Sem candidato: a extensão do layout. Só conta o VISÍVEL: sem viewports, layers desligados/congelados/não plotáveis e, nos
  blocos grandes (explodidos em memória, até 4 níveis), só o estado de visibilidade atual — o `GeometricExtents` de bloco
  dinâmico no GstarCAD inclui os estados ocultos (o carimbo do escritório tem A0…A0++ num bloco só, em cm).
- Folha da impressora: nome canônico (`UserNNNNN`) achado pelo nome LOCAL (`GetLocaleMediaName`) a cada plotagem — os códigos
  mudam quando os formulários do Windows são recriados.
- **Plotagem** (`Pdf/PlotadorPdf.cs`): `PlotInfo.OverrideSettings` com uma cópia do layout (o page setup salvo não muda), janela
  na moldura, ajustar à folha, centralizado, giro 90° só quando a orientação do desenho ≠ a do formulário, ctb escolhido.
  `BACKGROUNDPLOT` = 0 durante o lote. DWG fechado: aberto só-leitura, plotado e fechado sem salvar; desenho aberto: volta o
  layout corrente. O desenho ativo do início volta no fim. Comando com `CommandFlags.Session`.
- **PDF24 sem diálogo** (`Pdf/Pdf24.cs`, PDF24 Creator 11.30): durante CADA folha grava em `HKCU\Software\PDF24\Services\PDF`
  Handler=autoSave, AutoSaveDir=%TEMP%\DEB_PDF_xxxx, AutoSaveFilename=$fileName, AutoSaveOpenDir/ShowProgress/UseFileChooser/
  UseFileCmd=0, AutoSaveOverwriteFile=1 (HKCU vale por cima do HKLM e é relido a cada trabalho). Quando o PDF chega, volta o
  que havia (valor ausente é apagado; chaves criadas também). Estado anterior em `%AppData%\PCEB\DEB.Cad\pdf24_restaurar.txt`;
  se o CAD fechar no meio, `Plugin.Initialize` restaura. O PDF é esperado (tamanho estável e livre, até 180 s) e copiado ao destino.
- **Orientação** (`Pdf/AjustePdf.cs`): a PDF24 grava tudo em pé (A0++ sai com MediaBox 841 × 1486,3 e o desenho girado). A
  página recebe /Rotate 90 (formulário deitado) ou 270 (A4 em pé com desenho deitado), reescrevendo o trecho com o mesmo número
  de bytes (o xref continua certo). Resultado: abre deitado, com o tamanho do formulário.
- Teste: `ARMTESTEPDFLOTE` (`Testes/AutoTestePdf.cs`; variáveis no comentário da classe) numa instância nova com CÓPIAS de DWGs.
  O roteiro .scr CONTINUA rodando quando o lote abre um desenho: não ponha ARMTESTEFIM depois; use `ARM_PDFLOTE_FIM=1`.
  Conferência: MediaBox/Rotate e pixels coloridos com PyMuPDF (`python -E -P`). 2026-11-17: A0++ e A0 das pranchas de teste,
  3 layouts num DWG (A0, A0, A4), cancelar no meio, desenhos abertos, gravar no layout e a janela/relatório pelo próprio
  ARMPDFLOTE (BM_CLICK): tudo certo; a PDF24 sempre restaurada. Com monochrome.ctb as linhas saem pretas; hachuras em cor
  verdadeira continuam coloridas (o GstarCAD não converte cor verdadeira pelo ctb).

## Port para AutoCAD e ZWCAD: comportamentos
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
- **Reservatório** (2026-11-05): template `reservatorio` + reconhecedor (Core) + ARMRESERVATORIO; regras e limites em `docs/TEMPLATES.md`.
  Faixa: `ServicoFaixa` chama `RecomputeDimensionBlock` ao mudar o texto (dentro de bloco o plot mostrava "N0").
- **Peças do Revit** (D22, 2026-11-09): ARMIMPORTARPECAS (`ComandosPecas.cs`) lê o JSON do botão "Exportar para o CAD" do
  DEB.Revit (`Core/Intercambio`: `ArquivoPecas`, `ConversorPilarPeca`) e gera um detalhamento por grupo de pilares iguais pelo
  template `pilar-premoldado`, lado a lado (300 cm de folga). Valores = PADRÃO do usuário + o que o modelo traz (seção, L, cota
  da base, níveis, embutimento, consolos). Consolos casados com o catálogo do ARMCONSOLOS pelas medidas (±0,6 cm); dois iguais
  em faces opostas = tipo duplo (o duplo do template é simétrico pelo centro); fora do catálogo = aviso, fica de fora.
  Repetições do detalhamento = peças do grupo; os modelos do template NÃO são gravados (P1, P2… sobrescreveriam os dele).
  Vigas (mesmo dia): fila de baixo, pelo template `viga-premoldada`; pontas pelo catálogo do ARMDENTES (altura × comprimento,
  ±0,6 cm); dente fora do catálogo = viga NÃO gerada, com aviso (desenhar reta seria a peça errada).
  **Desde a 0.1.32 (opção b do usuário, 2026-11-09)**: antes de converter, `CatalogoAutomatico` (Core) CRIA nos catálogos do usuário os
  dentes (DG AxC) e consolos (CS/CT/CD, duplo quando há par igual na face oposta) que faltam, com a geometria do modelo e a armadura
  padrão (almofada reduzida para caber), com aviso "conferir no ARMDENTES/ARMCONSOLOS"; o comando grava a configuração. O autoteste
  devolve os catálogos como estavam (não grava).
  **Marca do modelo** (branch `pc2/exportar-pela-conferencia`): o nome do grupo no JSON agora é a marca das peças no Revit; caracteres
  inválidos para nome de bloco viram "-" (`ComandosTemplate.Desenhar`) e, se o desenho já tem um detalhamento com o nome, o novo entra
  como "nome (2)" com aviso (reimportar não substitui o que existe).
  **Reimportação** (2026-11-10, branch `pc2/vigas-reais`): o comentário do detalhamento importado leva a assinatura dos dados do modelo
  (`Core/Intercambio/AssinaturaPeca`: "…; dados #XXXXXXXX."). Importar de novo: mesma marca e mesma assinatura = NÃO redesenha (os ajustes do
  usuário ficam); mudou = o comando pergunta UMA vez [Substituir/Manter] — substituir apaga o antigo (inserções, a relação do aço que era só
  dele e a definição, com o conteúdo antes) e leva o novo para o mesmo canto superior esquerdo (`ComandosPecas.Apagar`/`Reposicionar`); manter
  = o novo entra como "nome (2)" com aviso. Importados antes da assinatura contam como "mudou".
  **Marca "conferir"** (`Core/Motor/MarcaConferir`): dente/consolo criado pela importação com a armadura padrão sai com um aviso vermelho
  acima do título da prancha (layer ARM-CORTE) enquanto a descrição do tipo for a da importação; o usuário troca a descrição no ARMDENTES /
  ARMCONSOLOS depois de conferir e o aviso some.
  O Sincronizador passou a isolar a falha de uma barra na atualização (uma exceção derrubava a atualização de todas as outras).
  Autoteste: `ARMTESTEPECAS` (3 pilares e 3 vigas de exemplo; `ARM_PECAS_ENTRADA` = um JSON real), `autoteste_pecas.dwg/.png`.

## Pendências [VALIDAR com o engenheiro]
Espelhadas = mesmo N; catálogos (cordoalhas, treliças, telas) e traspasse;
BEDIT (ele confirma). Validados em 2026-10-27 pela planilha/tabela dele: gancho = ponta reta + pino/2 + φ
(medida externa, "compr. reto"), pinos e pontas da NBR 6118 (tab. 9.1/9.2) e comprimento real pelas dobras.
