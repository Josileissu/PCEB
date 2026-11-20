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
  mudam quando os formulários do Windows são recriados. A janela lê as folhas da impressora escolhida (`PlotadorPdf.NomesMidias`;
  sem desenho aberto, `DeviceCapabilities` do driver) e a linha cuja folha falta avisa "crie as folhas na impressora" (o
  Exportar pergunta antes de seguir).
- **Escala** (18 e 19/11): com a moldura reconhecida, escala FIXA pela unidade do desenho (`FormatosFolha.EscalaPlot` = mm de
  papel por unidade: folha em cm → 1 mm = 0,1 unidade; em mm → 1 = 1; em m → 1 = 0,001), centralizada (`SetCustomPrintScale`);
  sem moldura, "ajustar à folha". Não depende do papel nem de a moldura ter a medida exata (antes era papel ÷ moldura: uma
  moldura 2 mm fora mudava a escala). O GstarCAD reserva 3,2 mm de cada lado nos papéis personalizados da PDF24 (o driver
  informa 0): com "ajustar" o PDF saía a 98,9 % e com borda. Testado: moldura 289 × 202 mm em papel A4 sai com 289,0 × 202,0 mm
  no PDF e 4 mm de cada lado.
- **Folga do papel** (19/11, `FolgaPapel` do pdflote.json, campo "folga (mm por lado)" da janela; padrão 4, de 0 a 10): com o
  papel do tamanho exato da moldura, a linha externa caía em cima da borda da página e sumia do PDF. O botão "Criar folhas na
  impressora" cria o papel com formato + 2 × folga (`ConfigLotePdf.FormatosDoPapel`); a lista de formatos e o reconhecimento
  continuam nominais, e a escala fixa não muda com a folga. Por que 4: o GstarCAD RECORTA a plotagem na área imprimível dele
  (3,2 mm de cada borda nos papéis personalizados da PDF24; clip de 3,2–3,3 mm medido no PDF) — com folga 1 a moldura ficava
  a 1,1 mm da borda e continuava cortada. Mudar a folga e clicar no botão recria os papéis (muda o UserNNNNN).
  Diagnóstico das margens por mídia: `ARMTESTEMIDIAS` (`ARM_MIDIAS_SAIDA`). Os .pc3/.pmp do GstarCAD são serialização MFC com estruturas cruas da memória
  (não dá para gravar folhas com segurança): a impressora do lote no GstarCAD continua a PDF24.
- **Impressora** (17/11): GstarCAD/ZWCAD = PDF24 (campo `Impressora` do pdflote.json). **AutoCAD** = combo com a
  "AutoCAD PDF (High Quality Print).pc3" (padrão) e a PDF24, se instalada; a escolha fica em `ImpressoraAutoCad` (o mesmo
  pdflote.json serve aos dois CADs; `ConfigLotePdf.ImpressoraLote` escolhe pelo hospedeiro). Com a .pc3 o lote plota para
  arquivo (`BeginDocument(..., true, arquivo)`) e a PDF24 não é tocada; com a PDF24, o salvamento automático de sempre.
  Tipo pela impressora: `ImpressorasPdf.Tipo` (Core). Lote com a .pc3 NÃO testado (AutoCAD só no PC2).
- **Criar folhas na impressora** (botão da janela, 17/11): age sobre a impressora do combo.
  - PDF24 (ou outra do Windows): formulários de papel do Windows da lista de formatos do pdflote.json, sem margem
    (ImageableArea = folha inteira), em milésimos de mm. Plano no núcleo (`FormulariosPapel.Planejar`, D26): ordem crescente
    de área; não existe = criar; form de usuário com outro tamanho ou margem = recriar; o Windows lista os forms de usuário na
    ordem de criação, então do 1º fora de ordem em diante os da lista são recriados (os anteriores ficam, para não mudar
    códigos sem necessidade); form do driver (PRINTER) com o mesmo nome e outro tamanho = pergunta específica (Sim =
    substituir); form do Windows (BUILTIN) = nunca (avisa o conflito). Confirmação lista criar/recriar/substituir/manter/pular
    e avisa que DWGs que já usavam papéis da PDF24 podem abrir com o papel errado. Aplicação (`FormulariosPapel.Aplicar`) um
    passo por vez: apaga e cria em seguida. Chamada ao Windows em `Pdf/FormulariosWindows.cs` (OpenPrinter(null) com
    SERVER_ACCESS_ADMINISTER, EnumForms/AddForm/DeleteForm nível 1); funcionou sem administrador no Windows 10; erro 5 =
    mensagem "abra o CAD como administrador" (sem contorno). No fim relê as folhas e mostra criados/recriados/pulados/erros.
  - .pc3 (AutoCAD PDF ou outra .pc3 da Autodesk; pedido de 17/11): CRIA as folhas no .pmp ligado ao .pc3 (`Pdf/FolhasPc3.cs`;
    regra no núcleo `TamanhosPc3`). Confirmação curta ("Criar/ajustar N folha(s) em X?") e resumo. Conciliação: mesmo nome e
    tamanho = mantida; mesmo nome e outro tamanho = ajustada; outra folha do mesmo tamanho (±1 mm, qualquer orientação) =
    reaproveitada com o nome da lista (não duplica; ex.: "A1" vira "A1 (ISO A1)"); senão criada (deitada, como o assistente da
    Autodesk). Folhas fora da lista ficam. Cópia `.pmp.bak_AAAAMMDD-HHMMSS` antes de gravar; .pc3 sem .pmp (ou ligado a um
    caminho que não existe neste PC) ganha um .pmp novo em PrinterDescDir ("PMP Files") e é ligado a ele (cópia `.pc3.bak_…`).
    Pastas: Preferences.Files.PrinterConfigPath/PrinterDescPath ou PrinterConfigDir/PrinterDescDir do perfil. Depois:
    `PlotConfigManager.RefreshList(RefreshCode.All)`; se a janela ainda não listar, ela pede para reabrir o CAD.
    GstarCAD: os .pc3/.pmp dele são binários (outro formato) → o botão explica e não grava.
  - Impressora do Windows cujo driver não mostra formulários personalizados (Microsoft Print to PDF: só papéis fixos + o form
    que coincide com um deles, como "A4 (ISO A4)"): forms de usuário que existem no Windows e o driver não lista →
    "o driver da X não aceita tamanhos personalizados; use PDF24 ou AutoCAD PDF" (no fim do botão, nas linhas da janela e na
    causa da falha do lote: `FormulariosPapel.DriverIgnoraPersonalizados`, `CausaErroPdf.DriverSemTamanhoPersonalizado`).
  - Fora do CAD (outro computador): `tools\criar_folhas_pdf24.ps1` (mesma regra, em PowerShell; instalado em
    `<pasta do plugin>\ferramentas`): `powershell -ExecutionPolicy Bypass -File criar_folhas_pdf24.ps1` mostra o plano e
    pergunta; `-Listar` só lista; `-Sim` sem perguntas; `-SubstituirDoDriver`; `-Config <json>`; `-Impressora <nome>`.
  - Teste (17/11): núcleo (`FormulariosPapelTests`); winspool com um form de TESTE ("ZZ teste DEB", criado e apagado; EnumForms
    igual antes/depois); script com uma lista de teste (criar, ordem, recriar, "nada a fazer"); janela numa instância separada
    do GstarCAD (BM_CLICK + PrintWindow): com os forms certos, "Nada a fazer".
- **Formato do .pmp** (levantado em 17/11 nos .pmp do DWG TrueView 2022; gravado pelo plugin desde 17/11, ver abaixo): cabeçalho ASCII
  `PIAFILEVERSION_2.0,PC3VER1,compress\r\npmzlibcodec` (48 bytes), depois 3 inteiros de 32 bits little-endian: Adler-32 dos
  bytes compactados, tamanho descompactado e tamanho compactado; em seguida o fluxo zlib. Dentro, texto terminado em `\0`
  com blocos `meta{}`, `mod{}`, `udm{ calibration{} media{ size{ 0{ caps_type=2 name="UserDefinedMetric (L x A MM)"
  localized_name="<nome>" media_description_name=… media_group=15 landscape_mode=TRUE } … } description{ … media_bounds_urx=…
  printable_bounds_*=… printable_area=… dimensional=TRUE } } }` e `hidden{}`. Valores não exatos em float levam um código entre
  parênteses (ex.: `911.08331299 (    H*IXC$!X20)`): 6 bits por caractere (ASCII − 32) com os 8 bytes do double em
  little-endian + 16 bits de conferência NÃO decifrados (não é CRC-16, soma, Fletcher nem Adler). O código é OPCIONAL: valor só
  em decimal é aceito. O Adler-32 do cabeçalho é dos bytes COMPRIMIDOS (igual ao .ctb). Há entradas órfãs (`User-1`).
  Ligação size ↔ description: `media_description_name` do size = `name` da description; o nome que o CAD mostra é o
  `localized_name`. .pc3 sem .pmp: `meta{ user_defined_model_pathname="` vazio. Leitura/gravação genérica: `ArquivoPia`.
  **Validação (17/11, console do DWG TrueView 2022 = `accoreconsole.exe /i <dwg> /s <scr>`, sem janela)**: no console o `PLOT`
  é a versão de linha de comando (`-PLOT` não existe; nomes com espaço SEM aspas; `?` lista dispositivos e papéis). Com
  cópia de segurança da pasta Plotters do TrueView: (1) folha nova "1000.3 × 707.15" sem código no .pmp existente → listada e
  plotada (PDF 1000,1 × 707,3 mm); (2) .pmp criado do zero (meta do .pc3 + udm, sem `mod`) ligado à "Smallest File.pc3" →
  listado e plotado; (3) o gravador C# (`TamanhosPc3` + `ArquivoPia`, por um console descartável) nas duas .pc3: 9 folhas
  com os nomes da lista, A0++ 1486,3 × 841, A4 297 × 210, A1 (ISO A1) 841 × 594, A1+ 911,1 × 594. Pasta restaurada e
  conferida igual ao backup. No AutoCAD de verdade o botão e o `RefreshList` ainda NÃO foram testados (só no PC2).
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
- **PCEB.ctb** (pedido de 17/11: "quando a pessoa não tem o ctb, criar um padrão copiando o ctb atual"): `DEB.Core/Documentacao/
  EstiloPlotagemPadrao.cs` monta o texto do ctb (tudo preto, exceções em `Excecoes`: cores 1, 10 e 20 vermelhas, 4 ciano, 5 azul,
  250-253 e 255 cor do objeto, 254 cinza claro; lineweight 0, linetype 31… iguais nas 255 cores; `custom_lineweight_table`
  0,0…2,11) e grava o arquivo (zlib à mão sobre o DeflateStream). Teste: o texto com descrição vazia tem o SHA-256 do ctb do
  escritório (o arquivo original fica fora do Git); a descrição gravada é neutra ("PCEB - estilo padrao de plotagem", ASCII).
  Escolha (`Escolher`): o ctb da janela, senão o do layout; inexistente neste PC ou vazio → PCEB.ctb, com aviso na linha do
  relatório ("ctb X não encontrado neste computador; usado PCEB.ctb"). PCEB.ctb existente NUNCA é sobrescrito.
  **O GstarCAD lê a lista de estilos uma vez por sessão** (17/11: gravar no meio, `RefreshLists` e regravar a pasta nas
  preferências não adiantam) → o plugin grava PCEB.ctb ao CARREGAR (`PlotadorPdf.GarantirPcebAoCarregar`, em
  `Plugin.Initialize`); se mesmo assim faltar, a falha diz "feche e abra o CAD". Pasta: `Preferences.Files.PrinterStyleSheetPath`
  (o GstarCAD 2021 responde DISP_E_UNKNOWNNAME) → `PrinterStyleSheetDir` do perfil (`UserRegistryProductRootKey\Profiles\
  <CPROFILE>\General`) → FindFile de um ctb da lista. PCEB.ctb também aparece no combo da janela.
- **Folhas no Model** (pedido de 17/11: DWGs de outro computador sem layout): regra no núcleo (`FolhasModeloPdf`): algum layout
  com moldura reconhecida → só os layouts; nenhum (sem layout, layouts vazios/só viewport, sem moldura) → as molduras do Model,
  uma folha por moldura ("Model", "Model 2"…; PDF "<dwg> - Model 2.pdf" com 2+); nada em lugar nenhum → linha de erro
  "nenhuma moldura/carimbo encontrado no Model nem nos layouts" (desmarcada, no log). No Model a moldura está em escala: formato
  por unidade (mm, cm, m) × escala usual (1:1…1:1000), menor diferença relativa; ficam só as molduras externas (a polilinha
  dentro do carimbo sai) e com ≥ 20% da maior (seção de pilar solta sai). Blocos do Model são explodidos em memória se forem
  grandes ou tiverem sozinhos a medida de um formato (até 3000). Plotagem do Model: janela na moldura, sempre.
- **Page setup de outro computador** (impressora/papel/ctb inexistentes): a configuração é feita sobre a cópia do page setup;
  se o validador recusar, refaz do zero (`new PlotSettings`) e avisa. O giro sai do tamanho REAL do papel (`PlotPaperSize`).
- **Erros** (pedido de 17/11: "quando der algum erro saber do que se trata"): `DEB.Core/Documentacao/ErrosLotePdf.cs` classifica
  (exceção + internas, HRESULT/Win32, ErrorStatus do CAD na mensagem, etapa) em `CausaErroPdf` com mensagem curta e dica:
  PDF24 não instalada, impressora inexistente, papel inexistente ("use Criar folhas na impressora"), driver sem tamanhos
  personalizados, formato não escolhido, sem moldura, ctb não encontrado, DWG não abre / em uso / danificado ou versão nova,
  PDF não gerado em X s (PDF24 travada?), PDF de destino aberto em outro programa / somente leitura (conferido ANTES de plotar),
  pasta sem permissão / não encontrada, disco cheio, CAD ocupado, plotagem falhou, cancelado. O plugin lança
  `ErroLotePdfException` nos casos conhecidos. Uma falha não para o lote. Relatório: resumo, "Por que falharam" (por causa),
  uma linha por folha com etapa, avisos; botões "Copiar detalhes do erro" (área de transferência: relatório + bloco técnico de
  cada falha: data/hora, arquivo, folha, etapa, causa, exceção, HRESULT, Win32, pilha) e "Abrir log". Log = o do plugin
  (`deb_AAAAMMDD.log`, `Log.ErroDetalhado`). Barreira no comando: nada sobe para o CAD. A PDF24 é restaurada por folha
  (Dispose) e de novo no fim do lote.
- Teste (17/11, GstarCAD 2021, instância separada; DWG de teste criado pelo autoteste com `ARM_PDFLOTE_CRIARMODEL`: só Model, 2
  molduras A1 a 1:50 em cm + seção 42 × 29,7 dentro, page setup "Plotter Inexistente.pc3"/ctb inexistente escrito via DXF):
  2 PDFs A1 com PCEB.ctb (cor 1 vermelha, cor 3 preta), seção ignorada, aviso do ctb; erros provocados com
  `ARM_PDFLOTE_FORMATO_ERRADO` (papel inexistente) e `ARM_PDFLOTE_TRAVAR=1` (PDF de destino aberto): mensagens, relatório
  (`ARM_PDFLOTE_RELATORIO_PNG`) e "Copiar detalhes" conferidos; `ARM_PDFLOTE_FOLHAS_DIAG=1` (só leitura): Microsoft Print to
  PDF → "driver não aceita tamanhos personalizados"; "DWG To PDF.pc3" do GstarCAD → "formato próprio, não cria folhas".
  A instância de teste carrega a DLL do bin por NETLOAD antes do carregador do plugin instalado (sem copiar DLLs).

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
- Lado do texto da chamada/indicação (2026-11-20): nos comandos ARMCHAMADA/ARMINDICACAO, com as referências à direita do ponto
  clicado o texto nasce `MiddleRight` (termina no ponto, fica à esquerda dele) e a linha chega pela direita; à esquerda, `MiddleLeft`
  como antes (`CalculoFaixa.TextoTerminaNoPonto`, média das referências). Só ao criar pelo comando: templates, importação e
  indicações existentes não mudam, e mover o texto depois não troca o lado. Autoteste: ARMTESTEINDIC.
- Afastamento da faixa (D39, 2026-11-20): `DadosFaixa.AfastamentoInicial`/`AfastamentoFinal` em cm (XData "AFI"/"AFF" só quando > 0;
  paleta "Afastamento inicial/final (cm)", faixas que distribuem). A cota mede a peça inteira; quantidade e posições saem de
  `CalculoFaixa.QuantidadeEntre`/`PosicoesEntre` com `DadosFaixa.AfastamentosCm()` (0 = a fração `EspacamentoInicial` naquela ponta).
  Início = 1º ponto da faixa (P1). Autoteste: ARMTESTEAFAST.
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
