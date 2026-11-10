# Templates de armadura (ambiente preparado em 2026-10-27)

Pedido do usuário: templates para os elementos com padrão estabelecido no escritório. A implementação
de cada um começa quando ele enviar os PDFs/DWGs (pastas em `info_auxiliar/templates/<elemento>/`).

## Elementos previstos (`CategoriaElemento`)
| Categoria | Variações citadas |
|---|---|
| Base circular de reservatório | fundação |
| Base retangular de reservatório | fundação |
| Bloco de 1 estaca | com/sem colarinho ou cálice; embutimento para pilar pré-moldado |
| Bloco de 2 estacas | idem |
| Pilar pré-moldado | levantamento em `docs/PILAR_PREMOLDADO_REQUISITOS.md` (2026-10-29): categorias PP-1…PP-5, normas, padrão P61/PP127 |
| Viga pré-moldada | protendida ou não; levantamento em `docs/VIGA_PREMOLDADA_REQUISITOS.md` (2026-11-06, PC2): categorias VP-1…VP-5, NBR 6118 18.3 e NBR 9062 (dente Gerber, apoio sem recorte, protensão) |
| Pilar moldado in loco | |
| Viga moldada in loco | |
| Laje | |

## Estrutura do código
- `PCEB/src/DEB.Core/Templates/Templates.cs` (testável, sem CAD):
  - `ITemplateArmadura`: `Id`, `Nome`, `Categoria`, `Descricao`, `Parametros`, `Gerar(valores, cfg)`;
  - `ParametroTemplate` (número, inteiro, texto, opção, bitola, sim/não; grupo, unidade, limites);
  - `ValoresTemplate` (valores digitados, com leitura numérica tolerante a vírgula);
  - `ResultadoTemplate`: `Barras` (`BarraTemplate`: vértices em cm relativos à inserção, bulges,
    formatos dos segmentos/ganchos, sobrepostos, cotados, bitola, aço, quantidade, espaçamento, estribo),
    `Contornos` (linhas de forma) e `Avisos`;
  - `CatalogoTemplates`: registro, busca por categoria, `Validar`.
- `src/DEB.Cad/ComandosTemplate.cs`: comando **ARMTEMPLATE** (janela com a árvore de elementos e os
  parâmetros num PropertyGrid dinâmico → ponto de inserção → desenho), `Desenhar` (contornos + armaduras
  pelo `ServicoBarra.Criar`). Sem templates registrados, o comando lista os elementos previstos.
- Estribos fechados em qualquer poligonal: `GeradorEstribo.FechadoPoligono` (o template monta a volta e
  escolhe o canto dos ganchos).

## Para implementar um template
1. Nova classe em `PCEB/src/DEB.Core/Templates/<Elemento>/`, implementando `ITemplateArmadura`.
2. Registrar no carregamento do plugin (`CatalogoTemplates.Registrar`).
3. Testes de unidade com as medidas de um projeto real (do PDF/DWG do usuário) e uma verificação no autoteste.
4. Próximos passos previstos: faixas geradas pelo template (distribuição), cortes, quadro de ferros por
   elemento, salvar conjuntos de valores ("predefinições") por elemento.

## Implementados
### Base de reservatório (`Templates/BaseReservatorio/TemplateBaseReservatorio.cs`, 2026-10-27)
Padrão dos projetos Assaí SPA/SRP/ASC (2024–2025). Circular ou retangular, num template só.
- Parâmetros: geometria (forma, D ou Lx/Ly, h, cobrimentos 4/5, reservatório Ø e capacidade), estacas (anéis,
  grade ou nenhuma), malha inferior e superior (aço, bitola, espaçamento, dobra: até a outra face/fixa/sem),
  faixa central reforçada (largura e bitola), borda (U: bitola, espaçamento, aba; retangular: barras de borda
  por face; circular: anel com nº de barras, bitola e emenda), malha intermediária (barras ou tela, camadas),
  barras VAR (posição única ou uma por barra), escala e nome do detalhamento.
- Distribuição SIMÉTRICA (sobra metade de cada lado); quantidade pela mesma regra das faixas.
- Circular: cada família vira um grupo VAR (`DadosArmadura.ComprimentosVar`, XData "CVAR") com tabela de
  comprimentos "L"; ou, na opção "uma posição por barra", um N para cada comprimento (simétricas juntas).
- Saída: planta de locação e formas, planta de armaduras com faixas, coluna "detalhes das barras", corte 1-1
  com indicações, tabelas "L", notas, e tudo num detalhamento (bloco) com a RA parcial ("nome no título").
- Fora do escopo (pedido do usuário): nichos de concretagem, chumbadores, tabela de cargas das estacas.
[VALIDAR com o usuário no uso: posição das faixas, tamanhos de texto, valores padrão das dobras.]

### Pilar pré-moldado PP-1 (`Templates/PilarPreMoldado/TemplatePilarPreMoldado.cs`) — em construção
Requisitos e regras de norma: `docs/PILAR_PREMOLDADO_REQUISITOS.md`. Verdadeira grandeza (cm); a escala de cada
região só dá a altura dos textos/cotas (a folha é montada com viewports, como no DWG do P61). Seção b (x) × h (y);
face 1 = +x, 2 = −y, 3 = −x, 4 = +y; a VISTA n olha a face n (1 e 3 mostram h, 2 e 4 mostram b).
- **Etapa 1 (2026-10-29, feita)**: 4 vistas lado a lado (passo = maior lado + 20 alturas de texto), níveis (topo e base
  sempre + lista "NOME/NAME=cota"), cadeia de cotas entre níveis e "COMPRIMENTO TOTAL DO PILAR: <>", embutimento com
  chaves (linhas a cada passo/dente nas 4 vistas, cota na VISTA 2, avisos da tab. 15 e do mínimo de 40 cm), chanfros,
  marcas do corte A-A, corte A-A com as vistas em volta, notas (cobrimento por face, tolerâncias da tab. 2, insertos) e
  informações p/ produção (volume da forma, peso a 2500 kgf/m³). Idioma PT, EN ou PT/EN.
  Quadro de produção EM TABELA (modelo do usuário, 2026-10-29): título "PT\P(EN)", rótulo à esquerda e valor com
  unidade à direita ("fcj = 28 MPa", "1,209 m³", "3,023 ton.", "1 un."); linha da montagem só com fcj > 0.
  `TabelaTemplate` ganhou larguras/alinhamentos por coluna e quebra de linha na célula (`TabelaTemplate.Quebra`).
  GstarCAD 2021: `HorizontalCellMargin`/`VerticalCellMargin` não têm efeito e a altura mínima da linha vem do estilo;
  o espaço inicial afasta o texto da borda esquerda, o final é descartado [pendente: estilo de tabela próprio].
- **Seções e cortes (plano, 2026-10-29)**: incrementais — gerados onde há o que mostrar: corte de forma no topo
  (insertos/gabarito) e um corte por nível de consolo (B-B, C-C…, hachurado); seções de armadura onde o arranjo das
  longitudinais muda (topo com grampos U, trecho corrente, cada redução/emenda, base) e no consolo; marcas nas vistas
  e na elevação das armaduras; seções iguais não se repetem.
- **Etapa 2 (2026-10-29, feita)** — `ArmadurasPilar.cs` (partial): longitudinais por parâmetros (canto com 1 barra ou 3
  em "L" a 5 cm, intermediárias por face b/h; centro do canto = cobrimento da face + φt + φ/2, o P61 dá 5,55), emendas
  ALTERNADAS (metade das barras de cada grupo começa com peça menor: emendas afastadas ≥ 1,3 l0) ou NA MESMA SEÇÃO, ou
  sem emendas (rolo; aviso se a bitola só existe em barras); traspasse = NBR 6118 com o fck do pilar (50 % ou 100 %
  emendadas) e nunca < 45φ, arredondado a 5 cm, com cota e NOTA PT/EN ao lado ("AJUSTAR SE NECESSÁRIO"); estribos
  fechados (ganchos de 90° no canto superior esquerdo, medidas externas = seção − cobrimentos) por zona (base, corrente,
  topo; um N por bitola), faixas na elevação sem o diâmetro ("71 N1 c/15"); grampos suplementares (uma ou duas
  direções; quadrado = um N com "2x"); grampos U no topo e na base (duas direções; a 2ª recuada pelo canto em "L");
  seções 1-1/2-2/3-3 com as barras em ponto (diâmetro real), estribo, grampos, pernas dos U e UMA INDICAÇÃO POR BARRA
  (textos em ordem dos dois lados, sem cruzar); formas dos estribos/grampos abaixo das seções; RA do detalhamento à
  direita. Avisos da NBR 6118 (18.4.2.1, 18.4.2.2, 18.4.3, 18.2.4). Descrição das barras: `BarraTemplate.PosicaoDescricao`
  e `RotacaoDescricao` (na vertical, ao lado da barra, como no P61).
  Correção no plugin: a INDICAÇÃO não passa mais o espaçamento dela (15) para a barra (`ServicoFaixa.Criar`).
- **Ajustes de 2026-10-29**: quadro de produção com `Table.SetMargin` por célula (a margem global não funciona no
  GstarCAD) e `SetTextHeight` (sem ele o texto ficava com a altura do estilo); colunas 24/11 alturas de texto. Seções:
  cada longitudinal com a chamada na altura da própria barra (afastada só o necessário: passadas de cima para baixo e
  de volta, depois recentradas); U com UMA chamada por conjunto (direção x ao lado, direção y em cima).
- **Etapa 3 (2026-10-29, feita)** — `ConsolosPilar.cs`: 2 consolos parametrizados (faces "1,3" = duplo com tirante
  passante; face sozinha = tirante com gancho de 90° no pilar), cota do topo, b, a, h e h1 (trapezoidal), deslocamento;
  vistas: de frente (retângulo + aresta da ponta + pinos + almofada) e de lado (trapézio + pino + almofada + cotas na
  VISTA 2); cortes de forma B-B/C-C hachurados (ANSI31; `HachuraTemplate`) com almofada, pinos e cotas, e marcas na
  VISTA 2; detalhe "ARMADURAS CONSOLO b x h x a - DUPLO" abaixo das vistas (corte com as barras e indicações + formas:
  tirante, barra transversal soldada ou laço horizontal, costura (estribo horizontal em volta), estribos verticais,
  inferior em L, pinos em L; almofada "NEOPRENE a x b x e mm"); a2 da NBR 9062 7.3.3.4 posiciona a almofada e os pinos
  (no centro dela); volume com os consolos (P61 = 3,523 m³ / 8,808 t, igual à prancha) e `AlturaCG` (para as alças).
  Avisos 7.3.2.2, 7.3.3.1, 7.3.3.4, 7.3.3.6/7, 7.3.3.12, 7.3.6.
- **Catálogo de consolos padrão (2026-10-29, pedido do usuário)**: `TipoConsolo` (nome, descrição, duplo, b, a, h, h1,
  armaduras padronizadas) na configuração do usuário (`ConfiguracaoArmadura.Consolos`, DataMember 68; padrão = P61 duplo,
  PP127 simples e trapezoidal); comando **ARMCONSOLOS** (`Paleta/FormConsolos.cs`: lista + PropertyGrid, novo/duplicar/
  excluir, nomes únicos); `TemplatePilarPreMoldado.Catalogo` (estático, o Plugin põe o da configuração ao carregar e
  depois do ARMCONSOLOS) dá as opções. No pilar: **5 posições** (C1…C5) com tipo, faces, **distância da base ao topo do
  consolo** (cm) e deslocamento. Tipo duplo exige faces opostas; tipo simples em "1,3" = dois consolos ancorados.
  Um detalhe de armaduras por tipo (e largura do pilar na direção do balanço), com as quantidades somadas.
  `Parametros` virou propriedade calculada (as opções mudam com o catálogo). Testes do pilar na coleção "Pilar" (sem
  paralelismo: o catálogo é estático).
- **Etapa 4 (2026-10-29, feita)** — `ItensPilar.cs`: alças de cordoalha CP-190 (NBR 9062 5.3.3.5) simétricas ao CG
  (`PosicaoAlcas`: CG ± (50 − pct) % de L; pct 20; valores digitados mandam; aviso perto de consolo na mesma face), na
  face escolhida (2): de lado o laço fora da face, de frente o traço com cotas 25|25 e "ALÇA", cotas base→alça→alça→topo;
  detalhe "ALÇA DE IÇAMENTO" 1:20 com a barra (pé + pernas + laço; "2x2"). Montagem: furo ø100 a pct do topo (30 %,
  aviso fora de 25–35 %) nas faces 1-3 ou 2-4 (círculo de frente, linhas ocultas de lado, cota ao topo) com o detalhe
  "REFORÇO NOS FUROS" (grampos U, r = ø/2 + 3 → C=125 do P61, 8 por furo), ou alça de topo (+1 alça). Inserto: nome
  acima da VISTA 1, chumbadores nas vistas, gabarito com cotas em cadeia no corte A-A. Detalhes padrão: chaves (1:10),
  espaçamento no canto (1:5, com os valores reais), ATENÇÃO SPDA (PT/EN). A tabela de raio de curvatura, as emendas e o
  comprimento das barras (A1/B1) saíram do pilar em 2026-11-07 (pedido do usuário: o desenho gerado ficou feio). O x do próximo detalhe respeita a largura do título.
- **Ajustes de 2026-10-29 (3ª rodada)**:
  - **Parâmetro tabela** (`TipoParametro.Tabela`, `ColunaTabela`; valor = células "|", linhas ";"): no ARMTEMPLATE vira
    "N consolos" com botão "…" (`Paleta/FormTabela.cs`: grade, opções em lista, adicionar/remover/subir/descer, **Ctrl+V
    do Excel**). Usado em **Consolos** (tipo, faces, base→topo, deslocamento — sem limite de quantidade), **Níveis**
    (PT, EN, cota; `LerNiveis` aceita também o formato antigo) e **Insertos** (tipo, local Topo/Face n, base→centro,
    deslocamento).
    Faces do consolo em lista conforme o tipo (`OpcoesDaLinha`/`Ajustar` da coluna): duplo = "1 e 3"/"2 e 4" (escolher a
    face 1 já leva a 3), simples = 1 a 4; trocar o tipo converte a face.
  - Grupos com dois dígitos ("01." … "16."): o PropertyGrid ordena categorias como texto.
  - **Catálogo de insertos** (`TipoInserto`, config DataMember 69; ARMINSERTOS): modo topo (chumbadores + gabarito, corte
    A-A com cotas em cadeia nas duas direções) ou chapa na face (chapa de frente; espessura e grapas de lado). Não entram na RA.
  - Janela de catálogo genérica `FormCatalogo<T>` (`Paleta/FormConsolos.cs`) para consolos e insertos.
  - **Chaves de cisalhamento** refeitas como o detalhe 01 do P61 (`PerfilChaves`): rebaixo de "dente" recuado "prof",
    trecho de passo − dente até a face com chanfros, arestas atravessando o pilar dentro de um círculo, cadeias 4/1 e 5,
    chamadas CHAVES/PILAR. Nas vistas, linhas nas arestas do rebaixo.
  - **Detalhe típico** como a prancha do escritório (`DetalheTipico`): títulos sublinhados com SEM ESCALA e NBR-6118,
    ganchos desenhados + tabela, emendas por traspasse (barras hachuradas, ≥ 45ø e o par com ≥ 9ø), por solda (cordões
    ≥ 5ø e corte a-a ≥ 0,3ø), comprimento das barras (U com r, A1/B1) e notas PT/EN em colunas.
- **Etapa 5 (2026-10-30)** — `ListasArmaduraPilar.cs`: longitudinais e zonas de estribos em LISTAS (parâmetros tabela).
  - "Barras longitudinais": arranjo (Cantos = 4, Ao lado dos cantos = 8 em "L", Faces 2 e 4 / 1 e 3, Face 1…4 = barras por
    face), bitola, feixe (1–3), início/fim (cm da base; vazio = pilar todo). Linhas do mesmo arranjo uma sobre a outra:
    a sobreposição é emenda (cota + nota; aviso se < traspasse da NBR 6118 na mesma seção, mín. 45φ); linha contida em outra
    = aviso. Posições alinhadas pelo maior feixe do arranjo (`PosicoesLinhas`, `RaioFeixe`, `CentrosFeixe`); faces entre o
    "L" (se houver) ou entre os cantos. Quantidade da RA = posições × feixe. Espaçamento livre com o raio do feixe.
  - "Zonas de estribos": nome, medida a partir da base/topo, de/até, bitola, espaçamento, grampos; o trecho corrente
    (EstBitola/EstEsp/GramposCorrente) preenche os vãos (`LerZonas`); zonas sobrepostas = aviso, vale a primeira.
  - Elevação: uma coluna por arranjo (e lado A/B das alternadas). Seções (`Cortes`): divididas onde mudam as linhas
    ativas ou o estribo/grampos da zona; trechos de emenda entre linhas e < 10 cm não ganham seção; composição igual repete
    o número (marcas em todos os trechos); grampos U distinguem seções iguais (topo × base) sem criar trechos.
  - Campos antigos removidos (CantoBitola, BarrasPorCanto, VizBitola, FaceB/HNum, FaceBitola, Topo/Base Comp/Bitola/Esp,
    Grampos); os valores não são guardados nos detalhamentos, então não há migração.
- Próximas (quando houver projeto real): seções circular/L/T/U/vazada e pilar-parede, seção variável, topo inclinado/garfo,
  base com chapa/luvas/esperas, consolo esconso/corrido/metálico, furos e nichos, seção na altura do consolo.

### Reservatório retangular (Templates/Reservatorio/, 2026-11-05, PC2, branch pc2/template-reservatorio)
Pedido do usuário: reconhecer a estrutura de um DWG de outro programa e gerar o detalhamento no padrão do exemplo
uxiliar_cad/reservatório (armadura.dwg + forma.dwg), usando só as bitolas e espaçamentos do desenho original.
- TemplateReservatorio (id `reservatorio`, também no ARMTEMPLATE): retângulo externo Lx × Ly, paredes de espessura t,
  internas de face a face (eixos x/y), altura H acima do fundo, fundo hf com balanço, lajes superiores (tabela: nome,
  nível, espessura, células). Vistas: paredes em planta 1:25 (horizontais rebatidas em volta, L de canto externo e L nos
  cantos das células, verticais de canto com indicação), cortes A-A e B-B 1:25 (verticais rebatidas, arranques), fundo
  positivo/negativo 1:50 com faixas e borda, cada laje superior 1:50, RA.
- Regras medidas no exemplo (padrões editáveis): horizontais = comprimento externo − 2c com ganchos que fecham na outra
  face (t − 2c; as internas atravessam as externas); verticais da face inferior do fundo ao topo − 2c com gancho de 90°
  da NBR; dobras do fundo hf − 2c, positiva volta 30 por cima; cobrimento 4,5 (paredes) e 3,5 (lajes); perna do L 88;
  arranque 100 (fundo) × 111 (parede) com gancho 6. Avisos quando menores que lb/traspasse (NBR 6118, fck).
- ReconhecedorReservatorio (Core, sem CAD): vistas = agrupamentos das linhas de forma (layer com "forma", sem "barra"/"arm")
  casadas com os títulos (PLANTA, CORTE, ARMAÇÃO … LAJES (nível)); no Eberick o título fica embaixo e à DIREITA da vista
  inteira. "Escala 1:n" perto do título; desenho em cm de papel (vista < 150 un) é multiplicado por n. Famílias = par
  (bitola, espaçamento) mais frequente nos textos da vista (horizontais por direção pelo giro do texto; negativa sem ø
  usa a bitola da positiva, com aviso).
- ARMRESERVATORIO (em ComandosTemplate.cs): seleção → reconhecer → gerar ao lado do original, direto (decisão do usuário).
  Testado no AutoCAD 2026 com o DWG de teste do usuário (Eberick, 396 × 402, 1 interna): leitura correta, 13 posições.
- Fora da v1 (avisos): furos/aberturas, mísulas, planta em L, paredes de espessuras diferentes, vistas por nível
  ("vista topo", "inferior à laje") e corte D-D do exemplo.
### Blocos sobre estacas (Templates/Blocos/TemplateBlocoEstacas.cs, 2026-11-06, PC2)
Exemplos e regras medidas: `docs/BLOCOS_REQUISITOS.md` (B3 em imagem; B4, B15, B28, B31 e B3.1 em PDF/DWG). Decisões do
usuário (2026-11-06): **padrão de folha BPM para todos**, **todos os tipos de uma vez**, **perna da principal pela altura do
bloco** (topo da base − cobrimento; valor digitado manda).
Uma classe, dois registros: `bloco_1_estaca` (Bloco1Estaca) e `bloco_estacas` (Bloco2Estacas).
- Formas: retangular em grade nx × ny (bloco = estacas ± "e", a distância eixo-face) ou triangular de 3 estacas (hexágono
  com as 6 faces a "e": 3 nos lados e 3 nos cantos; recorte de semiplanos). Pilar no centro das estacas (+ deslocamento).
- Ligação: cálice com colarinho (limitado à face do bloco, como o B28), cálice embutido ou arranque.
- Armaduras: principal em faixas sobre as estacas (triangular: uma por lado; faixa larga demais é deslocada para dentro,
  como o B31); malhas secundária (inferior) e superior nas duas direções (superior interrompida no colarinho/cálice;
  comprimentos diferentes = grupo VAR com os "L" escritos na planta); lateral horizontal fechada no contorno − c (inclusive
  o hexágono); estribos verticais (retangular); cálice: horizontais fechadas (topo concentrado + restante até a base do
  pilar), grampos U por parede ("2x", perna t − c − c_cálice), verticais em grampo (2 por parede), suspensão (NBR 9062
  7.7.3.7) e barras sob a base do pilar; arranque com pé e estribos do pilar.
- Desenho (padrão BPM): FORMA (título "BLOCO B28 - 6x - 2 ø 40", pilares, PLANTA cotada com quadrantes hachurados, CORTE A-A
  com regularização, concreto magro e NÍVEIS por pilar a partir do topo — tabela "Níveis"); ARMADURA (VISTA SUPERIOR da
  inferior principal, inferior secundária e superior com as dobradas embaixo; CÁLICE; ARM. LATERAL HORIZONTAL; CORTES A-A e
  B-B com barras em ponto e chamadas curtas; arranque); relação do aço; INFORMAÇÕES P/ PRODUÇÃO; detalhes padrão do pilar
  (`DetalhesTipicos.Desenhar`, em `DEB.Core/Templates/DetalhesTipicos.cs`: DETALHE 01 chaves, raio de curvatura, emendas, comprimento das barras);
  ANCORAGEM DAS ARMADURAS DAS ESTACAS; INDICAÇÃO BARRA VARIÁVEL; notas. Idioma PT, EN ou PT/EN.
- Detalhamento com repetições = nº de pilares (`ResultadoTemplate.DadosDetalhamento`: grupo, classe, volume, área de forma).
- Avisos: estaca/pilar fora, Lemb < 40 e < 1,2h/1,5h (NBR 9062 Tab. 15), parede < 15, fundo sob o cálice < 20, arranque < lb.
- Testes: `BlocoEstacasTests` (B28, B15, B31, arranque, 1 estaca embutido); autoteste `toolsutoteste_autocad.ps1 -Comando
  ARMTESTEBLOCO` desenha B28 e B15 (8 ok no AutoCAD 2026 do PC2; PNGs autoteste_bloco_b28/b15.png). Prévia sem CAD:
  `GravarPreviaDoBloco` (ARM_PREVIA).
- Decidido pelo usuário (2026-11-07): perna da principal = topo da base − c (diferenças como o 68 do B31 se ajustam na
  edição do bloco); principal no triangular reta de face a face; pernas da secundária/superior = valor padrão do template
  (20); suspensão com perna externa = interna − 6. [VALIDAR no uso] posição das chamadas; o B3 agora sai no padrão BPM.
- Depois (não são comuns, decisão de 2026-11-07): 5 estacas em quincôncio e outros arranjos não em grade; colarinho excêntrico; chaves desenhadas nas paredes do cálice.
### Viga pré-moldada VP-1/VP-2/VP-3 (Templates/VigaPreMoldada/, 2026-11-07/08, PC2) — aguardando a conferência do usuário
Padrão medido e decisões: `docs/VIGA_PREMOLDADA_REQUISITOS.md` (seção 1 = pranchas do escritório; seção 16 = etapas).
Usa o contexto de desenho do pilar (`TemplatePilarPreMoldado.Ctx`, agora `internal`), o quadro de produção e a regra das alças.
- **Etapa 1 (forma, feita)**: planta e elevação com dentes Gerber do catálogo (`TipoDente`), furos de pino, alças, cotas,
  seções de forma, notas e produção. Autoteste: `tools\autoteste_autocad.ps1 -Comando ARMTESTEVIGA`.
- **Etapa 2** (`ArmadurasViga.cs`): longitudinais em tabela desenhadas fora da forma com o rótulo da função, estribos por zona
  (`CalculoFaixa`), SEÇÃO X-X - ARMADURA, forma do estribo (fechado ou U com saliência para a capa).
- **Etapa 3** (`DenteViga.cs`): armadura do dente pelo catálogo (estribos Gerber, suspensão, grampo, costura, tirante com barra
  soldada). **ARMDENTES** (`Paleta/FormConsolos.cs`, `Catalogos.Dentes`; config DataMember 70 `Dentes`; botão "Dentes" na paleta).
- **Etapa 4** (`ItensViga.cs`): alça CP-190 até o fundo com detalhe 1:20; aviso de estabilidade lateral (NBR 9062 6.1.6);
  detalhes típicos pela biblioteca DWG (`BibliotecaDetalhes`, `%AppData%\PCEB\DEB.Cad\Detalhes`, inseridos pelo CAD).
- **VP-3** (`ProtensaoViga.cs`): "Viga protendida" + tabela de grupos de cordoalhas (fiada, quantidade, bitola, isolamento E/D,
  força). Nós da grade de 5 cm (fiada 1 a 5 cm do fundo, simétrica; o 1º grupo da fiada no meio), numeração de baixo para cima
  e da esquerda para a direita; ARMADURAS DE PROTENSÃO abaixo das passivas (layer ARM-CORDOALHA azul; isolamento = trecho
  SOLID em ARM-ISOLAMENTO cotado, "MANGUEIRA ISOLAMENTO"); fiada que passa no recorte do dente é cortada na face dele;
  seção NOMENCLATURA 1:15 (isoladas em vermelho, grade cotada); RA em CP-190; quadro com "RESISTÊNCIA ATO DA PROT." (fcj do
  saque) e aviso < 21 MPa; avisos de largura, espaço livre (9.2.1) e passiva encostando em cordoalha.
- **Cantoneira nas pontas** (`LigacaoViga.cs`, padrão CAN-03 da 1071): por ponta (CantE/CantD), nome, altura, distância ao topo e
  aba; retângulo na elevação e L nos dois cantos na planta (layer ARM-INSERTO), cotas por fora e o nome com chamada; não entra
  na RA. Cordoalhas superiores (fiada acima de h/2) numa linha à parte acima das passivas.
- **Fretagem** (resposta do usuário: feita com estribos; no dente a suspensão ajuda): a zona dos estribos de cada ponta vai
  pelo menos até o fim da transferência das cordoalhas aderentes, ℓbpt da NBR 6118 9.4.5 (fbpd com o fcj do saque, ηp1 = 1,2;
  × 1,25 se a liberação não for gradual); nota no desenho. **Taxas** passiva e ativa (kg/m³) no quadro de produção da protendida
  (pesos pelas regras da RA, `MedidaBarra`; alças fora).
- Falta: VP-4 (L, T invertido, I).

### Viga com aba — VP-4, 1ª parte (Templates/VigaPreMoldada/AbaViga.cs, 2026-11-09, PC2) — PROPOSTA, aguardando os padrões do usuário
Pedido do usuário: "pode propor o detalhamento das vigas com seções diferentes; depois eu te passo padrões para ajustarmos".
Formato tirado das vigas reais de um modelo real (janela "Conferir peças"): a aba é um CONSOLO CORRIDO lateral.
- Grupo de parâmetros "10. Aba (apoio de laje)": `Aba` (sem / esquerda (L) / direita (L) / dois lados (T invertido)), `AbaLargura`
  (balanço a partir da alma), `AbaAltura` (junto da alma), `AbaAlturaPonta` (0 = retangular; menor = fundo inclinado, topo plano),
  `AbaBase` (fundo acima do fundo da viga: 0 = L/T invertido; maior = aba elevada; base + altura = h = aba rente ao topo),
  `AbaIni`/`AbaFim` (0 = automático), `AbaEstBitola`/`AbaEstEsp`, `AbaLongQtd`/`AbaLongBitola`. **b continua sendo a ALMA.**
- Esquerda = lado de cima da planta e lado esquerdo das seções; direita = lado de baixo da planta = frente da elevação (aba só do
  outro lado sai em linha oculta na elevação).
- Dente Gerber: a aba que fica ACIMA do recorte passa pelo dente (padrão do trecho = de ponta a ponta, e a seção do dente sai com
  a aba); a que fica abaixo para na face do dente.
- Forma: aba na planta (com o balanço cotado), na elevação e nas seções (contorno poligonal, cotas do balanço, da ponta, da altura
  junto da alma e do fundo). A seção do vão é cortada dentro do trecho da aba. Volume, peso e CG (alças) contam a aba.
- Armadura (como consolo corrido): ESTRIBO DA ABA fechado, abraçando a alma e a(s) aba(s), ramo de cima reto (tirante) e ramo de
  baixo acompanhando o fundo inclinado (`PoligonoEstriboAba` + `GeradorEstribo.FechadoPadrao`), em faixa própria embaixo das faixas
  dos estribos da alma (barra `abaestribo`); LONGITUDINAIS na ponta de cada aba (barra `abalong`, rótulo "ARMADURA ABA LAJE").
  Avisos: suspensão pelos estribos da alma (conferir) e aba de um lado só (CG fora do eixo: içamento e torção).
- Fora desta parte: seção I, duas abas no mesmo lado (ex.: "aba laje + aba superior" do modelo real, alma que estreita em cima), mísula
  em toda a altura, consolo de apoio de outra viga, estribos de suspensão próprios da aba.
- Prévia sem CAD nesta máquina (sem matplotlib): `ARM_PREVIA=<pasta>` + teste `GravarPreviaDaVigaComAba` → JSON; desenhado com
  System.Drawing (script de sessão). Testes: `VigaAbaTests`.
- Revit → CAD: `DEB.Core/Pecas/SecaoComAba` lê a seção cortada como alma + aba (trapézio de topo plano, medidas como as do consolo);
  `ConversorLevantamento` aceita [dente] + corrente + [dente] com UMA aba num trecho contínuo (que pode pegar os dentes);
  `VigaPeca.Aba` (`AbaPeca`) vai no JSON como "aba" e o arquivo sai na **versão 2** só quando alguma viga tem aba (o DEB.Cad 0.1.32
  instalado recusa o arquivo em vez de desenhar sem a aba; sem aba continua versão 1). Modelo real (Revit 2026): 150 vigas em 35 grupos
  exportadas (97 em 28 antes da aba); ficam fora 4 grupos com consolo de outra viga e 4 com duas saliências.
- **Iteração 2 (2026-11-10, branch `pc2/vigas-reais`)**:
  - `AbaDentes` (Automático / Passa pelos dentes / Para nos dentes): automático = passa a aba que fica toda acima do recorte e para a
    que desce abaixo dele. "Passa" com a aba descendo abaixo do recorte = ela fica PENDURADA abaixo do dente (aba de escada do
    modelo real); a seção do dente sai com a aba (fundo negativo em `AbaViga.NoDente`), com aviso.
  - **Consolo lateral de apoio de outra viga = aba curta** (`AbaViga.Consolo`, até 60 cm): mesma forma e mesma armadura da aba
    (estribos fechados = tirante), rótulo "ARMADURA CONSOLO" e aviso para conferir. Uma aba OU um consolo por viga, por enquanto.
  - Revit → CAD: `SecaoComAba` lê a aba pendurada abaixo da alma (no dente) e o consolo de outra viga (seção unida no corte);
    `ConversorVigaPeca` manda `AbaDentes` conforme o trecho do modelo. Modelo real: **157 de 168 vigas** exportadas (39 grupos); ficam
    fora 4 grupos (11 peças) com duas saliências ("aba laje + aba superior": alma que estreita em cima).
  - AutoCAD 2026: ARMTESTEVIGA 23 ok; ARMTESTEPECAS 14 ok com um JSON do modelo real (aba elevada, abas no topo, três consolos laterais).
- **Ressalto sobre a alma (2026-11-10, `RessaltoViga.cs`)** — o "aba laje + aba superior" de um modelo real (alma 30 × 50, aba de laje
  rente ao topo de um lado, ressalto 13 × 27,5 do outro, onde a capa da laje encosta). Grupo "11. Ressalto sobre a alma": lado, largura,
  altura acima do topo (h continua sendo a da alma). Armadura pedida pelo usuário: estribo fechado DENTRO do ressalto com longitudinais
  nos cantos, e barra em L de ligação com a capa — perna vertical junto da face de fora do ressalto, descendo `LigCapaAncoragem` (40)
  na alma; perna horizontal no alto, saindo pela face da laje `LigCapaSaida` (70) para fora. Faixas próprias na elevação, formas na
  relação do aço, marcas de corte e cadeia das alças acima do ressalto.
  Revit → CAD: `SecaoComAba` separa a faixa de cima; mais estreita e rente a uma face = ressalto; tão larga quanto a alma (até 1,5 cm
  de diferença) = a alma continuando (degrau de modelagem da viga de escada). JSON versão 3 só com ressalto. Modelo real: **168 de 168**
  vigas exportadas. AutoCAD 2026: ARMTESTEVIGA 30 ok.
