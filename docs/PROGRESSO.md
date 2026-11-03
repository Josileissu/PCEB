# SITUAÇÃO ATUAL (2026-10-27): versão 0.1.15 (instalador só quando o usuário pedir) — autoteste 174/174, unidade 108/108
Último instalador: `instalador\saida\ArmaduraBR_Setup_0.1.15.exe`. Novo Setup SÓ quando o usuário pedir (aí: subir
`<Version>` e rodar `instalador\gerar.bat`).
Em andamento: templates — base de reservatório feita; aguardando a avaliação dele da prancha gerada. Próximos
elementos (blocos 1/2 estacas, pilares/vigas pré-moldados e in loco, lajes) conforme ele indicar os padrões.
Aguardando o usuário também: paleta com "ocultar automaticamente" no uso real, gancho padrão do ARMESTRIBO
(45° como no QiArmaduras?), RA por pasta, DOCX/XLSX, reconhecimento de DWG (só quando ele mandar), port AutoCAD,
BEDIT. Telas identificadas: modelo com hífen ("Q-92", do catálogo) ou sem ("Q92", como no exemplo dele)? T global
ou por detalhamento? Retomar pela última seção deste arquivo.

# Progresso — Fase 2 (iniciada em 2026-10-26)

Pedido do usuário (2026-10-26): seguir a fase 2; desenhar em **cm** (não mm); **escala base 1:10**
como propriedade na paleta; texto = 2 un em 1:10 → em 1:25 = 5 un (altura = 0,2 × escala).
"Se os tokens acabarem, abra uma nova sessão e continue de onde parou" → continuar por esta lista.

## Etapas
- [x] 0. (feito, autoteste 50/50) Unidade cm fixa (config `UnidadeDesenho`), escala por barra e por tabela (XData "ESC"),
        propriedade "Escala" na paleta, migração do config.json antigo (Escala 50 → 10).
- [x] 1. (feito, autoteste 63/63; corte-chamada-arco sem teste no CAD) Faixas: ortogonal, alinhada, corte+cota (ort/ali), corte+chamada, chamada, indicação.
        Quantidade da barra = soma das faixas. Dimension com texto sobrescrito + XData FAIXA;
        círculos CORTE; chamadas.
- [x] 2. (feito, autoteste 75/75; o diálogo em si precisa de teste manual) Estribos: gerador no Core (simples, aberto, múltiplo, pente, circular ext/int; ganchos
        45/90/180; cobrimento) + ARMESTRIBO (diálogo WinForms) + ARMESTRIBOREP.
- [x] 3. (feito, autoteste 83/83) ARMREBATER: rebatida vinculada (deslocamento guardado; edição propaga nos dois sentidos).
- [x] 4. (feito, autoteste 87/87) Ponto no cobrimento (opção do ARMBARRA) + ARMRECOTA.

**Fase 2 concluída em 2026-10-26.** Pendências para validar com o usuário: diálogo do ARMESTRIBO
(teste manual), regra de quantidade das faixas (⌊L/s⌋+1), representação dos ganchos de estribo
(traço a 45°), corte e chamada em arco (sem teste no CAD). Próxima: fase 3 (detalhamentos, RA
individual/sequencial/parcial, bloquear RA, repetições, camadas).

# Fase 3 (iniciada em 2026-10-26)

Pedido do usuário: seguir para a fase 3; "a relação de aço parcial pode ser vinculada a quando as
barras pertencerem a um bloco do CAD" → **detalhamento = bloco** (BlockReference/BlockTableRecord).

**Fase 3 concluída em 2026-10-26 (autoteste 109/109).** Sem teste automático: edição dentro do bloco
(BEDIT) com a sincronização, os diálogos/grade (só montagem), ARMDETCRIAR digitado (testado pela API).
Não feito (opcional da especificação): "exibir somente layout", reordenar/renumerar detalhamentos na
folha, área útil da prancha. Próxima: fase 4 (traspasse, treliças, telas soldadas).

- [x] A. Handles do XData como 1005 (o CAD traduz em BLOCK/COPY/WBLOCK/INSERT); cópia de barra não
        limpa mais as listas (a validação recíproca resolve); índice de GUID inclui blocos.
- [x] B. RA com blocos: barras em blocos × nº de inserções (recursivo); tabela parcial por bloco
        (tabela dentro do bloco ou ARMRAPARCIAL/ARMRAASSOCIAR); numeração Global/Individual/Sequencial
        + coluna ELEMENTO.
- [x] C. Detalhamentos: ARMDETCRIAR (seleção → bloco), propriedades do detalhamento (XData no
        BlockTableRecord) na paleta, ARMDETGERENCIAR (grade).
- [x] D. Bloquear RA (ARMRABLOQUEAR): números travados, novas/alteradas acima do maior, "*" na tabela.
- [x] E. Camadas ("3+1") nas barras.

# Fase 4 (iniciada em 2026-10-26)

**Fase 4 concluída em 2026-10-26 (autoteste 122/122).** Valores de catálogo (cordoalhas, treliças, telas) e
regras de traspasse marcados [VALIDAR]. Próxima: fase 5 (RA por pasta, relatórios, corte e dobra,
reconhecimento de DWG) e depois o port para AutoCAD.

Pedido do usuário: seguir para a fase 4 e considerar **cordoalhas CP-190** como armadura (além de
CA-50/CA-60). Respostas da fase 3: barras de blocos não inseridos ficam fora (confirmado); bloco de
nível mais alto = elemento (confirmado); BEDIT ele confirma no fim.

- [x] A. Cordoalhas CP-190 RB (9,5/12,7/15,2) na tabela de bitolas; sem ganchos/curvatura; aço na descrição.
- [x] B. Traspasse (NBR 6118: fck, aderência, % emendada; ou fixo) + ARMDIVIDIR (ponto / automático
        pelo comprimento comercial) + verificação de comprimento limite (config).
- [x] C. Treliças: catálogo, ARMTRELICA (2 pontos), desenho gerado (partes), RA por modelo/peso.
- [x] D. Telas soldadas: catálogo, ARMTELA (2 cantos), resumo por massa (área × kg/m²).

# Fase 5 (iniciada em 2026-10-26)

**Etapas pedidas concluídas em 2026-10-26 (autoteste 129/129).** Pendentes da fase 5, aguardando o usuário:
RA por pasta (explicada), relatórios DOCX/XLSX, reconhecimento de DWG (SÓ quando ele mandar).

Pedido do usuário: seguir com a **lista de corte e dobra**; nova apresentação da RA com **detalhe de
dobra** (prévia da forma da barra) — mesma estrutura atual + uma coluna no FIM com o formato (modelo:
imagem com "DETALHE DE DOBRA (DIMENSÕES EXTERNAS EM cm)"). **Reconhecimento de barras em DWG de outros
programas: só quando o usuário der o comando.** RA por pasta: explicada, não pedida ainda. Port AutoCAD: depois.

- [x] A. Gerador do esquema da barra (Core, testável): ângulos reais, segmentos curtos ampliados, cotas.
- [x] B. Coluna "DETALHE DE DOBRA" opcional na tabela (OpcoesTabelaRA), desenhada sobre a tabela como
        partes vinculadas (acompanham MOVE/COPY/ERASE da tabela); opção na paleta e no ARMRAINSERIR.
- [x] C. ARMCORTEDOBRA: CSV (Excel, ;) + HTML com SVG de cada posição, ao lado do DWG.

# Uso, instalação e ajustes (2026-10-26 a 2026-10-27)

(Situação ao fim desta etapa, manhã de 2026-10-27: versão 0.1.4, autoteste 132/132, unidade 88/88 — ver o topo do arquivo.)

- [x] Resumo dos comandos inserido no DWG aberto do usuário (MText na layer ARM-AJUDA; não salvo).
- [x] Autocompletar da linha de comando: o GstarCAD lê uma lista fixa (`alias.pgc`); `Infra/Autocompletar.cs`
        acrescenta os comandos do plugin ao carregar (backup `alias.pgc.antes_armadura`).
- [x] "Ribbon": o GstarCAD 2021 não tem API .NET de ribbon. Virou a paleta separada **"Armadura - Comandos"**
        (ARMCOMANDOS; a paleta "Armadura" ficou só com Propriedades). Layout manual dos botões (os painéis
        automáticos sumiam ao re-encaixar); clique com comando em andamento cancela antes com ESC
        (`\x03` não funciona no GstarCAD); aceita clique com a paleta inativa.
- [x] Carregador (`src/Armadura.Carregador`): copia o plugin para `%LocalAppData%\ArmaduraBR\run\` e faz
        NETLOAD da cópia → o build não fica preso com o GstarCAD aberto.
- [x] Instalador Inno Setup (sem administrador): instala em `%LocalAppData%\Programs\ArmaduraBR`, grava a
        chave de carregamento automático por idioma, avisa se o GstarCAD estiver aberto, desinstalador
        (opção de restaurar o alias.pgc). Testado: instala, carrega sozinho, desinstala limpo.
        `INSTALACAO.txt` com o passo a passo (Setup e manual).
- [x] Estribo fechado no padrão do escritório (exemplo do usuário no Drawing1; depois ESPELHADO para o canto superior esquerdo, v0.1.6): ganchos no canto superior
        direito, sem sobreposição; cada gancho passa do canto o diâmetro externo de curvatura (pino + 2φ =
        2,5 no ø5); gancho padrão 90° (perpendicular, os ganchos se cruzam como no exemplo); 45°/180° em
        diagonal. As passagens do canto (`Segmento.Sobreposto`) contam como aço, sem cota e sem medida no
        esboço da RA.
- [x] Comprimento real (C= e RA), pela planilha do usuário (`Estudo_comprimento_real_barra_vergalhao.xlsx`):
        soma das medidas externas − desconto por dobra (2·re2·tan(δ/2) − re·δ; nos ganchos 2·re2 − re·δ com
        ângulo NOMINAL, confirmado pelo usuário); pinos da NBR 6118 (tab. 9.1 e 9.2, já eram os do plugin);
        ângulos pelos vértices (produto escalar/vetorial); barra fechada conta a dobra do fecho.
        Opção `DescontarDobras` (padrão true) no config.json. Cotas continuam externas.
- [x] REGEN/REGENALL recalculam descrições e tabelas (na lista `ComandosQueAtualizamRA`; config v4 migra).

Observações: a planilha do usuário conta só uma dobra na barra em U (ø só na linha 4) e tem o "raio do
centro" errado nas linhas CA-60 da tabela 9.1 — o plugin usa as fórmulas. Estribos já desenhados
mantêm o formato antigo; o C= das barras existentes muda na próxima atualização (REGEN, salvar, plotar).

# Ajustes pedidos em 2026-10-27 (tarde) — versão 0.1.6, autoteste 147/147, unidade 95/95

- [x] Paleta com "ocultar automaticamente": recolhida, o GstarCAD informa Visible = false e a paleta não
        relia a seleção. Agora relê sempre (e ao ativar/abrir). `AutoRollUp`/`RolledUp` não existem na API
        do GstarCAD (NotImplementedException): o teste oculta a paleta. [CONFIRMAR com o usuário no uso real]
- [x] Faixa com escala própria (`DadosFaixa.Escala`, XData "ESC"; 0 = a da barra): opção [Escala] nos
        comandos de faixa (vira padrão das próximas: `EscalaFaixa`), propriedades da faixa na paleta
        (selecionar só faixas/cortes/chamadas mostra a faixa).
- [x] Poligonal fechada → estribo com ganchos (`GeradorEstribo.FechadoPoligono`, qualquer forma; ganchos no
        vértice superior esquerdo por padrão): Estribo = Sim na paleta, ARMCONVERTER (pergunta), ARMOFFSET.
        Na paleta: "Gancho do estribo" (90°/45°/180°) e "Ganchos no canto" (4 cantos ou próximo vértice);
        bitola/aço refazem a passagem; Estribo = Não volta à poligonal fechada.
        Todos os estribos fechados (ARMESTRIBO inclusive) com os ganchos no canto superior ESQUERDO (decisão do usuário; v0.1.6).
- [x] ARMOFFSET: armadura paralela a uma poligonal aberta ou fechada, do lado clicado (Enter = para dentro).
- [x] Templates: ambiente (`docs/TEMPLATES.md`, `Core/Templates`, ARMTEMPLATE, pastas
        `info_auxiliar/templates/<elemento>/`). Nenhum template implementado — aguardando PDFs/DWGs.
- [x] Menu de configurações (ARMCONFIG abre a janela; `-ARMCONFIG` = linha de comando).
- [x] RA: numeração Global/Individual/Sequencial já existia (só no JSON) → agora na paleta da tabela
        ("Numeração (do desenho)") e no menu; tabela com "Coluna do detalhamento" (Automática/Sempre/Nunca)
        e "Nome no título" ("RELAÇÃO DO AÇO - V1").

## Como testar
Ver `armadura/CLAUDE.md` (autoteste numa instância nova do GstarCAD; relatório em
`%AppData%\ArmaduraBR\logs\autoteste.txt`).

# Importação do QiArmaduras (2026-10-27) — versão 0.1.7

Pedido do usuário: importar o .cad do QiArmaduras (AltoQi Builder) e fazer a equivalência com o plugin.
- [x] Formato decifrado (esquema autodescritivo; `docs/FORMATO_QICAD.md`, scripts de estudo em `tools/qicad/`).
- [x] Leitor + conversor no Core (testes com `auxiliar_cad/Desenho 1.cad` e o projeto real `PRANCHAS_Blocos_R00.cad`).
- [x] ARMIMPORTARCAD (botão "Importar .cad"): barras/estribos (ganchos 90°/45°, passagens), faixas,
        indicações, rebatidas vinculadas, detalhamentos (blocos, repetições nas barras), relações do aço
        parciais com o nome no título, desenho de forma (linhas, polilinhas, círculos, arcos, textos, cotas).
        Projeto real: 327 armaduras, 20 detalhamentos, 20 RAs, 9139 entidades em ~2,5 s.
- [x] Correção junto: faixas (chamada/indicação) dentro de detalhamentos criados por bloco ficavam com as
        referências no lugar antigo (`ServicoFaixa.Transformar`).
Pendente: exemplos de gancho 180°, barra com arco, corte+chamada, treliça/tela no .cad; rebatidas giradas/
espelhadas (hoje viram polilinha comum); hachuras não são importadas; MOVE comum de chamadas.
# Tela soldada: apresentação (2026-10-27) — versão 0.1.8
- [x] Opção "Mostrar distribuição do aço": desenha os fios do modelo (Q-196 245×600: 25 + 60 fios).
- [x] Texto da tela ("1 Q-92") no meio da diagonal, girado com ela (opção "Texto na diagonal", padrão sim).
- [x] Dimensões do painel cotadas (opção "Cotar dimensões do painel", padrão sim).
- [x] Na paleta, as opções de tela só aparecem para telas; gancho/canto só para estribos com ganchos.
Telas já desenhadas passam a ter cotas e texto na diagonal na próxima atualização (REGEN, salvar, plotar).
# Templates: base de reservatório (2026-10-27)
Pedido: começar os templates pelas bases de reservatório (PDFs SPA, SRP, ASC abertos no PDF24). Decisões do
usuário: circular e retangular juntas; posição VAR única COM opção de uma posição por barra; não extrair os
DWGs (o padrão se repete); sem nichos/chumbadores/tabela de cargas.
- [x] Grupo VAR no modelo (lista de comprimentos), RA, descrição C=VAR, numeração, paleta ("Barras VAR").
- [x] Templates com faixas, indicações, textos, círculos, cotas, tabelas, detalhamento e RA parcial.
- [x] Template da base (docs/TEMPLATES.md); registrado no ARMTEMPLATE ("Base de reservatório").
- [x] Testes: 5 de unidade (cordas, simetria, faixa central, retangular, VAR na RA) + autoteste desenhando as duas.
Pendente: o usuário avaliar a prancha gerada (posições, textos, padrões) e o "uma posição por barra" (sem faixas).
# Correção: ARMTEMPLATE não abria (2026-10-27) — versão 0.1.10
- [x] A classe `ComandosTemplate` não estava em `[assembly: CommandClass]`: o GstarCAD não registrava o
        ARMTEMPLATE (nem pelo botão). Corrigido; o autoteste agora confere que toda classe com comandos está na
        lista e que o template da base está registrado (161/161). Testado também pelo comando numa instância nova.
- [x] Instalador 0.1.10 gerado a pedido do usuário (o 0.1.9 tinha o defeito e foi apagado).
# Chamada sem linha de chamada (2026-10-27)
- [x] Opção para ocultar a linha de chamada (`DadosFaixa.OcultarLinhas`, XData "OCL"): no ARMCHAMADA, palavra-chave
        [SemLinha]/[ComLinha] na posição do texto; na paleta da faixa, "Mostrar linha de chamada" (chamada,
        indicação e corte+chamada). Os pontos ficam guardados: religar redesenha as linhas.
- [x] Instalador 0.1.11 gerado a pedido do usuário.
# Relação do aço ordenada pelo N (2026-10-27)
- [x] Numeração Global: linhas pela posição N (antes: aço, depois N). Individual/Sequencial: detalhamento, depois N.
- [x] Instalador 0.1.12 gerado a pedido do usuário.
# Faixas: campos opcionais do texto e linha de chamada apagada (2026-10-27)
- [x] Texto da faixa com campos opcionais (paleta, grupo "Texto da faixa"): quantidade, posição (N), diâmetro e
        espaçamento (XData MQTD/MPOS/MDIA/MESP, gravados só quando desligados). Cada tipo só oferece os seus campos
        (indicação: N; chamada: quantidade e N). Tudo desligado → fica o N (texto nunca vazio).
- [x] Linha de chamada apagada pelo usuário NÃO volta na regeneração: a faixa guarda o índice da referência
        (`LinhasApagadas`, XData "LAP"; cada linha guarda o seu no vínculo "IDX"). Só volta com
        "Mostrar linha de chamada" = Sim (aparece "Não" quando alguma foi apagada; Sim recria todas).
- [x] Autoteste: o COPY do roteiro termina com ESC (com COPYMODE de cópia única o Enter extra repetia o COPY e
        engolia o comando seguinte). 167/167; unidade 105/105.
- [x] Instalador 0.1.13 gerado a pedido do usuário.
# Repetições nas faixas (2026-10-27, depois da 0.1.13)
- [x] Propriedade "Repetições" na faixa (paleta; XData "REP" só quando > 1): a quantidade que a faixa soma na barra
        é multiplicada (`DadosFaixa.QuantidadeTotal`) e o texto fica "2x11 N1 …", como a descrição da barra. A
        indicação não tem (não soma quantidade). Autoteste 168/168, unidade 106/106. Instalador não gerado.
# Telas identificadas T1, T2… (2026-10-27, depois da 0.1.13)
Pedido: representar as telas como no exemplo do escritório ("T2 - 2x Q92" e "213 x 348" no meio da diagonal).
- [x] Opção da tela "Identificar (T1, T2…)" (paleta; XData TID/TN) e padrão das telas novas no menu de configurações
        (`TelaIdentificada`, usado pelo ARMTELA). Texto em 2 linhas centrado SOBRE a diagonal, que fica interrompida
        no texto (largura do MText + folga); dimensões menor × maior (cm); "2x" = quantidade × repetições; sem cotas
        nos lados. T dado na renumeração (`ServicoRA.NumerarTelas`): ordem de criação, painéis iguais (modelo, lados,
        quantidade; girado conta igual) = mesmo T; numeração do desenho todo.
- [x] Autoteste com 3 painéis (T1, T2, T1) + `autoteste_telas_T.png`; 172/172, unidade 107/107.
- [x] Instalador 0.1.14 gerado a pedido do usuário (repetições nas faixas + telas identificadas).
# Texto em duas linhas (Enter na edição) (2026-10-27, depois da 0.1.14)
Pedido: poder editar a descrição da barra e o texto da faixa e dar Enter, deixando em duas linhas.
- [x] Ao editar (MTEDIT/DDEDIT), o Sincronizador lê as quebras (`QuebrasTexto.Ler`, ignora a formatação do editor)
        e guarda "depois da palavra k" (barra: XData QBR em `QuebrasDescricao`; faixa: QBR em `Quebras`). O texto
        continua gerado (quantidade, N, C= mudam) e as quebras são reaplicadas (`QuebrasTexto.Aplicar`). Tirar o
        Enter volta a uma linha. Tela identificada não usa (já tem as 2 linhas dela).
- [x] Autoteste: edição real da descrição de H e da cota de uma faixa (eventos) → 2 linhas após regenerar.
        174/174, unidade 108/108. Instalador não gerado.
# Repetição sem parênteses (2026-10-28)
Pedido: "2x11 N1 ø10.0 c/15" em vez de "2x(11 N1 ø10.0 c/15)".
- [x] Descrição da barra, texto da faixa e tela ("2x1 Q-92"): a repetição fica colada na quantidade
        (`FormatadorDescricao.Repeticao`). Nos padrões "N primeiro"/"N no fim" também ("N3 – 2x4ø8.0"); com a
        quantidade oculta, o "2x" vai na frente. Camadas continuam "10x(3+1)". Autoteste 174/174, unidade 108/108.
- [x] Instalador 0.1.15 gerado a pedido do usuário (Enter nos textos + repetição sem parênteses). Com o Git, o
        .NET punha o hash do commit na ProductVersion (Setup "0.1.15+hash"): desligado no csproj
        (`IncludeSourceRevisionInInformationalVersion=false`).
# Propriedades rápidas — ARMPALETARAPIDA (2026-10-28)
Pedido: algo como o QQ (Quick Properties) do GstarCAD para a armadura; o usuário escolhe o atalho (alias) depois.
- [x] `Paleta/PaletaRapida.cs`: janela flutuante (tool window, sem roubar o foco) ao lado do cursor, mesmo PropertyGrid
        e mesmas classes da paleta (`PaletaArmadura.LerSelecao`/`AplicarObjetos`, extraídos do Recarregar/Aplicar).
        Só o 1º grupo aberto; os outros lembram o estado. Acompanha a seleção; some com Esc, no X ou com a seleção
        vazia. Sem seleção prévia o comando pede os objetos e os deixa selecionados. Botão "Prop. rápida" no painel.
- [x] Autoteste: abre com 2 barras, título "Armadura (2)", só o 1º grupo aberto, some com a seleção vazia;
        `autoteste_paleta_rapida.png`.
- [x] Instalador 0.1.16 gerado a pedido do usuário (propriedades rápidas).
- Obs.: o autoteste supõe a configuração de fábrica de escala (Escala 10, EscalaFaixa 0). Com o config do usuário
        (Escala 25, EscalaFaixa 50 em 2026-10-28) falham 5 testes de escala/ARMDIVIDIR que não têm relação com a
        mudança; com Escala 10 sobra 1 (texto da faixa, pela EscalaFaixa 50).
# Reconhecimento pelo texto + cotas móveis (2026-10-29)
Pedido: "faça isso [o reconhecimento pelo texto próximo], e não somente para o layer 'armadura'. Também, quero a
possibilidade de poder mover os textos/comprimento que aparecem junto às barras."
- [x] ARMRECONHECER (`ComandosReconhecimento.cs`; interpretador `Core/Reconhecimento/TextoArmadura.cs`): qualquer layer,
        polilinhas e LINEs (viram polilinhas), TEXT/MTEXT. Formatos: "25 N1 ø10 c/15 C=300", "N5 22ø5.0", "4ø10", "2x11 N1",
        "2x(11 N1", %%c/Ø/∅/φ, vírgula decimal, formatação de MText. Associação um-para-um pelo menor custo. Escala pela altura
        do texto (2 un → 1:10). Aviso quando o C= não confere nem com a soma externa nem com o comprimento real.
- [x] Cotas móveis: `Segmento.CotaMovida/CotaDx/CotaDy` (XData SM/SMX/SMY, só quando movida), deslocamento nos eixos do
        trecho; o Sincronizador grava ao mover (MOVE/grip) e a cota acompanha a barra. A descrição já era móvel.
        ARMTEXTOPADRAO desfaz. Botões "Reconhecer" e "Textos padrão" no grupo Barras.
- [x] Autoteste 194/194 (com Escala 10/EscalaFaixa 0), unidade 129/129.
- [x] Instalador 0.1.17 gerado a pedido do usuário (ARMRECONHECER + cotas móveis).
- Sem DWG real de outro programa ainda: o formato dos textos foi tirado da especificação e dos padrões usuais; pedir
        um exemplo ao usuário para calibrar.
# Template de pilar pré-moldado — levantamento (2026-10-29)
Pedido: levantar requisitos (NBR 9062, 6118 e demais), todas as configurações (armaduras, seções, estribos, emendas,
consolos, almofadas, insertos, pinos, alças, montagem); só desenho, sem cálculo. Referência: `auxiliar_cad/pilar_padrão/`
(P61) e a prancha PP127.
- [x] `docs/PILAR_PREMOLDADO_REQUISITOS.md`: padrão da prancha, regras por norma ([CONFERIR] onde depende do texto),
        configurações, categorias PP-1…PP-5, referências pedidas, 12 perguntas abertas, lacunas do plugin.
- [ ] Usuário: deixar normas/bibliografia em `info_auxiliar/referencias_pilar_premoldado/` e responder as perguntas.
- [ ] Depois: implementar PP-1 (retangular, armadura contínua, 0–1 nível de consolo).
# Template do pilar pré-moldado — etapa 1: forma (2026-10-29)
- [x] `TemplatePilarPreMoldado` (registrado no Plugin; ARMTEMPLATE → "Pilar pré-moldado (PP-1)"): 4 vistas, níveis,
        cotas, embutimento com chaves, corte A-A, notas (cobrimentos por face, tolerâncias NBR 9062), informações p/
        produção (volume/peso), idioma PT/EN/PT-EN. Unidade 140/140 (7 novos com as medidas do P61); autoteste 197/197
        com `autoteste_pilar_p61*.png`.
- [x] Autoteste: o teste do estribo dependia do último gancho escolhido no ARMESTRIBO (config do usuário,
        `UltimoEstribo.Gancho` = 45°): agora fixa 90° durante a verificação e devolve.
- [ ] Usuário: conferir o desenho da etapa 1 no ARMTEMPLATE (reabrir o GstarCAD). Depois etapa 2 (armaduras).
# Template do pilar pré-moldado — etapa 2: armaduras (2026-10-29)
- [x] Longitudinais (canto 1 ou 3 em "L", faces), emendas alternadas/mesma seção ou rolo, traspasse norma ≥ 45φ com
        nota, estribos por zona, grampos, U no topo e na base, seções com indicações por barra, formas, RA. Unidade
        147/147 (7 novos), autoteste 201/201 com `autoteste_pilar_p61_{secoes,arm_topo,arm_emenda}.png`.
- [x] Plugin: indicação deixa de copiar o espaçamento (15) para a barra (aparecia "c/15" em barras indicadas).
- [ ] Usuário: conferir no ARMTEMPLATE. Próxima: etapa 3 (consolos: forma, armadura, cortes B-B…, volume e CG).
# Template do pilar pré-moldado — etapa 3: consolos (2026-10-29)
- [x] Ajustes pedidos: quadro de produção (margens por célula, altura real do texto, colunas mais largas) e chamadas
        das seções (na altura de cada barra; um rótulo por conjunto de U).
- [x] Consolos (até 2 níveis, faces livres, duplo/sozinho, trapezoidal), vistas, cortes B-B/C-C hachurados, detalhe
        de armaduras com as formas (tirante soldado ou laço, costura, verticais, inferior, pinos), almofada, volume e CG.
        P61: volume 3,523 m³ e peso 8,808 t (iguais à prancha); tirante C=93, pinos 47+18+20. Unidade 154/154,
        autoteste 201/201 (`autoteste_pilar_p61_{consolo,corteB,vistas_consolo}.png`).
- [ ] Usuário: conferir. Próxima: etapa 4 (alças pelo CG, furo/alça de topo, inserto, detalhes padrão).
# Pilar pré-moldado — catálogo de consolos e etapa 4 (2026-10-29)
- [x] Catálogo de consolos padrão (ARMCONSOLOS; 3 tipos de partida das pranchas P61/PP127); pilar escolhe tipo, faces,
        distância da base ao topo e deslocamento (5 posições); um detalhe por tipo.
- [x] Etapa 4: alças pelo CG (cordoalha), furo com reforço ou alça de topo, inserto/gabarito, detalhes padrão (chaves,
        canto, alça, reforço, raio de curvatura, emendas, comprimento, SPDA). Unidade 160/160, autoteste 201/201
        (`autoteste_pilar_p61_{detalhes,vistas_alcas}.png`).
- [ ] Usuário: conferir. Próxima: etapa 5 (entrada por tabela/Excel/janela: armadura e consolos).
# Pilar pré-moldado — 3ª rodada de ajustes (2026-10-29)
- [x] EXPLODE/edição do bloco: chamadas não vão mais para as coordenadas do bloco (transformação do bloco explodido +
        âncoras nas faixas). Autoteste com EXPLODE real: 54 linhas no lugar, 0 fora.
- [x] Chaves de cisalhamento iguais ao detalhe 01 do P61; detalhe típico (raio/emendas/comprimento) como a prancha.
- [x] Listas em janela (consolos sem limite, níveis, insertos) com Ctrl+V do Excel; grupos 01…16 em ordem.
- [x] Catálogo de insertos (ARMINSERTOS): topo com chumbadores/gabarito ou chapa na face.
- Unidade 164/164 (8 novos), autoteste 203/203 (`autoteste_pilar_p61_chaves.png`, `autoteste_janela_consolos.png`).
# Pilar pré-moldado — etapa 5 (2026-10-30)
- [x] Longitudinais em lista (arranjo, bitola, feixe, início/fim): redução com a altura com emenda cotada e aviso de
        traspasse curto, bitolas mistas, feixes de 2/3; zonas de estribos em lista (consolo etc.) + trecho corrente.
- [x] Seções onde a composição muda (P61 continua com 3). Unidade 168/168; autoteste 208/208 (P62 REDUCAO:
        `autoteste_pilar_reducao_emenda.png`, `autoteste_pilar_reducao_secoes.png`, `autoteste_janela_longitudinais.png`).
- [ ] Usuário: conferir no ARMTEMPLATE (reabrir o GstarCAD).
# Checagem do programa (2026-10-30)
- Desempenho medido (`ARMTESTEDESEMPENHO`, N em ARM_DESEMPENHO_N): linear; 2000 barras = RA 1,5 s, MOVE de tudo 5,2 s.
  Sincronizador > 200 ms e a RA antes de salvar/plotar registram o tempo no log.
- Descrição sem "c/" quando as faixas da barra têm espaçamentos diferentes (estribo do pilar c/5 + c/10).
- Seções do pilar: uma etiqueta por N e lado, com uma linha para cada barra.
- Reaproveitamento: `XDataCad.Gravar/GravarSeMudou(o, dados)` (49 trocas), `XDataCad.BarraDoHandle` + `Lista` (5 buscas
  da barra dona), `Cobrimentos(v)` no pilar.
- Detalhamento do pilar compactado (altura 27 → 21 m): faixa inferior a 12·hv da base, detalhes padrão ao lado dos
  detalhes do consolo, coluna da direita sem a sobra das formas. Prévia sem CAD: `tools/previa_template.py`
  (ARM_PREVIA + teste GravarPreviaDoPilar; matplotlib instalado com pip --user).
# Pilar — padrão de apresentação do P56 (2026-10-30)
Referências em `auxiliar_cad/pilar_padrão/referencia_*.png` e o DWG 316-CON-DD-100-6113-DET-BAS-R02 (lido via ARMTESTEDXF + ezdxf:
layers, cotas e posições).
- [x] Layers com cor/espessura/tipo de linha dos layers do escritório (Armadura, DT-Forma, Cortes, DIM, TEXTOS, DT-Pontilhado,
      DT-Barras no corte/na forma, Pilares, Insertos…); novos ARM-BARRA-2 (U e pinos, azul) e ARM-PILAR-SECAO (magenta).
- [x] Chaves de cisalhamento: onda trapezoidal (retos de 4 alternando recuado/face, chanfros de 1), 2 arestas por chanfro,
      cadeias 4|1 e 5, chamadas com seta.
- [x] Vistas: marcas de corte com triângulo, nível com triângulo cheio, títulos maiores, nota dos insertos no alto
      (sublinhada); cotas dos consolos (altura + até o nível acima; base → topo) em colunas; inserto do topo com nome e
      linha de chamada; chapas de face cotadas da base em escada.
- [x] Cortes de forma: pilar magenta, VISTA n com triângulo para o corte, cadeias do consolo (canto/consolo/canto, pinos,
      face/pino/ponta).
- [x] Quadro "ARMADURAS console b x h x a - n un.": PLANTA, CORTE B-B (pilar interrompido, pino azul, Solda), CORTE AA e as
      formas com Tirante / Arm. Inferior / Estribo vertical / Estribo horizontal / Barra de ancoragem / Pino.
- [x] Etiquetas com patamar ("N12 ——"), estilos de cota ESTRUT50/25/20/10/5, CORTE AA do consolo enxuto. Versão 0.1.19.
- [ ] Usuário: conferir no GstarCAD.
- [x] (2026-11-02) REFEDIT do detalhamento: chamadas com patamar iam para longe (deslocadas do ponto de inserção);
      detecção do conjunto movido compara também com o patamar. Autoteste ARMTESTEMOVERPILAR/ARMTESTEMOVIDO. Versão 0.1.21.
