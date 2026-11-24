# Escadas de concreto armado — levantamento e plano (D42)

Pedido do usuário (23/11/2026): levantamento de escadas e tipologias; plugin que cria a escada do tipo escolhido e a posiciona na
estrutura; otimizador; métodos de cálculo conferidos com elementos finitos (OpenSees e CalculiX no início, biblioteca própria
depois); esforços, reações e deformada numa janela fora do Revit, como a prévia da laje alveolar e a da viga pré-moldada (D41).

Fonte principal: a apostila manuscrita de escadas da EPUSP/IPT (1983–84, 46 folhas), na biblioteca local
(`docs/02_biblioteca/artigos/`, fora do Git). Ela segue a NB-5/1978, que hoje é a NBR 6120:2019, e a NB-1, hoje NBR 6118:2023:
os valores normativos abaixo precisam ser **conferidos nas normas atuais** antes de virar regra do núcleo.

## 1. Geometria e funcionalidade (folhas 7.1–7.3)

| Item | Valor da apostila | No programa |
|---|---|---|
| Espelho `ae` | 16 a 19 cm | entrada, com aviso fora da faixa |
| Piso `ad` | 28 a 30 cm | entrada, com aviso fora da faixa |
| Fórmula de Blondel | `2·ae + ad` = 61 a 65 cm (inclinação α de 30° a 33°) | verificação; base do otimizador |
| Projeção do lance | `ℓ = (n − 1)·ad` para n espelhos; altura `n·ae` | calculado |
| Largura | 80 cm em geral; ≥ 120 cm em edifícios de apartamentos, hotéis e escritórios | entrada, com aviso |
| Patamar | obrigatório com **n > 19** espelhos; largura igual à da escada e comprimento ≈ largura, ≥ 80 cm | verificação |
| Altura livre | ≥ 2 m sob qualquer ponto do lance | verificação contra a laje de cima |

Hoje vale também a NBR 9077 (saídas de emergência) para largura, patamar e altura livre: entra no levantamento das normas.

## 2. Cargas (folhas 7.4–7.11)

- **Peso próprio** por m² de **projeção horizontal**, que é o usual: `g = γ·(h/cos α + ae/2)`, onde h é a espessura da laje
  medida perpendicularmente ao plano inclinado. A carga por m² de superfície inclinada vale `gi = g·cos α` e dá a mesma
  resultante. No patamar, `g = γ·hp`.
- **Revestimento**: de 0,5 a 1,0 kN/m² em projeção. Sem o detalhe de arquitetura, usar 1,0 kN/m² (granilite com espelho).
- **Sobrecarga** (NB-5): 3,0 kN/m² com acesso ao público e 2,5 kN/m² sem acesso. Na NBR 6120:2019, conferir os valores da
  tabela de escadas e passarelas.
- **Degrau isolado em balanço**: carga concentrada de 2,5 kN na ponta. A apostila mostra que **duas pessoas** por degrau
  (2 × 0,8 kN sobre 0,30 × 1,20 m = 4,4 kN/m²) passam da sobrecarga da norma. Recomenda calcular a viga com o maior dos dois valores.
- **Parapeito e corrimão**: 0,8 kN/m horizontal na altura do corrimão e 2,0 kN/m vertical (conferir na NBR 6120).

## 3. Tipologias (classificação 7.3.1)

As marcadas com ★ são as que a apostila calcula. São as usuais em edifícios e o ponto de partida do programa.

| Grupo | Tipo | Esquema e cálculo | Ordem proposta |
|---|---|---|---|
| Em laje | ★ **Armada longitudinalmente**: lance reto entre vigas, com ou sem patamar; formas em I, L e U | viga inclinada de largura igual à da escada, vão = distância horizontal entre os apoios | **1ª** |
| Em laje | ★ **Armada transversalmente**: apoiada nas vigas laterais (vigas de escada) ou paredes | laje de vão pequeno (1 a 1,5 m) na direção da largura; o degrau pode colaborar | 2ª |
| Em laje | ★ **Em balanço**, engastada numa viga reta ou numa parede | armada transversalmente; todo degrau tracionado em cima | 3ª |
| Em laje | ★ **Armada em cruz**: vãos grandes (> 6 m) ou forma em U com bordas livres | laje ortótropa; tabelas do Beton-Kalender 1980 (anexo A.7: 2 e 3 apoios, bordas livres) ou MEF | com o MEF |
| Em laje | ★ **Face inferior em degraus** ("em cascata") | longitudinal: viga de eixo quebrado (espelho em flexo-compressão ou flexo-tração, piso em flexão); transversal: espelhos como vigas | depois |
| Em laje | Helicoidal | espacial | futuro (biblioteca própria de MEF) |
| Em viga | ★ **Viga reta com degraus em balanço** ("espinha de peixe") | degrau em balanço (2,5 kN na ponta) e viga inclinada com flexão, torção `Mt = m*·ℓ/2` e flexão lateral (`Mℓ = 0`, cortante `Hℓ` nas pontas). Rigidez à torção no estádio II ≈ 1/5 da do estádio I | 4ª |
| Em viga | Viga curva helicoidal com degraus em balanço | espacial | futuro |
| Em pilares | Degraus ou lajes em balanço a partir de um pilar | — | futuro |

### Esforços do caso fundamental (laje armada longitudinalmente, biapoiada)

Para a carga `p = g + q` em projeção, o vão horizontal `ℓ` e o ângulo `α`:

- `M(x) = p·ℓ·x/2 − p·x²/2`, com máximo `p·ℓ²/8`;
- `V(x) = (p·ℓ/2 − p·x)·cos α`;
- `N(x) = (p·ℓ/2 − p·x)·sen α`, com compressão embaixo e tração em cima ou o contrário, conforme o apoio que segura a
  componente horizontal (a apostila mostra os dois esquemas);
- escada com patamares: viga de eixo quebrado, com carga maior no trecho inclinado (`g/cos α`).
- Escada em L ou U: o lance se apoia no patamar do outro lance. A apostila soma cerca de 50% da reação no patamar para cobrir
  o apoio na viga lateral, que o esquema simplificado despreza.

### Detalhamento típico (folhas 7.26, 7.31–7.34, 7.40)

- **Quina reentrante** (inclinado → patamar inferior): as barras **se cruzam**, cada uma ancorada na outra face. A barra que
  dobra seguindo a quina está **errada**, porque o empuxo no vazio a arranca do concreto (detalhe "CERTO / ERRADO").
- Positiva inferior contínua no lance; negativa nos apoios; armadura de distribuição; armadura de ligação nos patamares quando
  os lances são calculados separados.
- Armada transversalmente: o degrau colabora como zona comprimida triangular no momento positivo. No negativo, a face inferior
  fica comprimida e a laje é calculada como maciça se a linha neutra não cortar o degrau. O balanço precisa de **armadura no
  canto superior do degrau**.
- Em cascata: duas opções de armadura (vãos pequenos e vãos grandes, folha 7.40).

## 4. Cálculo e conferência por elementos finitos

1. **Cálculo do programa** (núcleo, `DEB.Core`): os esquemas da apostila com as combinações e o dimensionamento que a viga
   pré-moldada já usa (`Calculo` da D40: flexão, cisalhamento, flecha e fissuração).
2. **Conferência por MEF**: o núcleo monta o modelo e grava a entrada de um programa externo, roda o programa e lê o resultado:
   - **OpenSees**: pórtico ou grelha de barras, para as escadas armadas longitudinalmente e para as de viga com degraus em balanço;
   - **CalculiX**: casca (S8R/S4) ou sólido, para as armadas em cruz, em U ou em cascata.

   O programa mostra lado a lado a resposta do cálculo e a do MEF: esforços, reações e flecha, com a diferença em %.
3. **Biblioteca própria** de MEF: barras e cascas no núcleo. Fica para depois, quando os casos de teste dos externos estiverem
   prontos para servir de referência.

### Ferramentas instaladas (23/11/2026, fora do Git, em `programas/`)
- **OpenSees**: openseespy 3.8 no **Python 3.12** (`py -3.12`). O pacote do Windows só carrega no 3.12; o 3.13 continua
  como padrão. Rodas em `programas/python312/` para instalar sem rede. Conferido com uma viga biapoiada de 4 m e
  q = 10 kN/m: reação de 20,0 kN.
- **CalculiX 2.22**: `programas/calculix/calculix_2.22_4win/ccx_static.exe`, do site do autor. Conferido com um balanço e
  carga na ponta; a flecha bate com a de Timoshenko. Em `*BEAM SECTION RECT a,b`, a dimensão `a` fica na direção 1, que é o
  vetor da linha seguinte.
- **Firewall**: regra de saída "Python 3.13 (PCEB)" para o pip. Programa novo que use rede precisa de regra própria (UAC).

## 5. Plugin, posicionamento e janela

- **Janela de prévia** no kit `UI/Estilo` (D41), com abas Geometria, Cargas, Esforços (M, V, N), Reações, Deformada, MEF × cálculo
  e Verificações.
- **Criação no Revit**: o objeto que recebe a escada é a pergunta 2 abaixo.
- **Posicionamento**: o engenheiro escolhe as duas vigas ou lajes de apoio, ou dois níveis e um ponto de partida. O programa
  ajusta o número de espelhos ao desnível e mostra a escada na prévia antes de criar (D26).
- **Otimizador**: procura n, ae e ad (Blondel), a espessura h e o arranjo dos patamares que cabem no espaço e no desnível,
  ordenados por consumo de concreto e de aço e pela flecha.

## 6. Escada pré-moldada (D43)

Pedido do usuário (23/11/2026): "vamos trabalhar com as escadas pré-moldadas, com apoio nos tipos definidos e com pinos". Os tipos
de apoio são os do arquivo de configurações que está sendo criado em outra branch. A geometria continua a mesma; o que muda são
as ligações, a divisão em peças e as fases transitórias.

- **Execução** é uma opção da escada: moldada no local ou pré-moldada. A pré-moldada é montada por **peças**.
  - Cada **lance** é uma peça biapoiada; o vão vai de centro de apoio a centro de apoio.
  - O **patamar intermediário** (escadas em L e em U) é uma peça própria, apoiada nas vigas ou paredes. Ela recebe as
    reações dos lances que se apoiam nela.
- **Ligação em cada ponta**, de dois tipos:
  - *apoio direto*, sem recorte (NBR 9062, 7.5). O tirante As = (Fd/1,2 + Hd)/fyd e a costura Ash = Asv = Fd/(8·fyd) são feitos com
    grampos em U na ponta;
  - *dente* (7.4), com tirante, suspensão e biela, como nas vigas pré-moldadas.

  Nas duas, o patamar de apoio da peça tem pelo menos meio comprimento de apoio para cada lado do centro.
- **Força horizontal no apoio** (7.3.9), Hd = k·Fd:

  | Apoio | k |
  |---|---|
  | junta seca | 0,8 |
  | argamassa | 0,5 |
  | concreto com chapa | 0,4 |
  | chapas não soldadas | 0,25 |
  | elastômero (padrão) | 0,16 |
  | PTFE | 0,08 |

  Os valores padrão vêm de uma classe só, que vai ler o arquivo de configurações quando ele chegar.
- **Pinos** (chumbadores grauteados, que passam por furo na peça e entram no apoio): resistem ao Hd por corte.
  - Resistência por pino: Rvd = 1,0·φ²·√(fcd·fyd), valor conservador da literatura de ligações com pino; a conferir com o usuário.
  - Distâncias e embutimento pela NBR 9062, Tabela 12: embutimento de 8φ a 20φ; distância à borda de no mínimo 4φ, crítica 7φ.
  - Furo maior que o pino, para a tolerância de montagem.
- **Barras**: todas ficam dentro da peça, com cobrimento nas pontas. Saem as negativas que entravam na viga (moldada no
  local). Entram a armadura superior de içamento e construtiva e os grampos de apoio.
- **Içamento** (NBR 9062, 5.3), com coeficiente dinâmico de 1,3:
  - 4 alças a 0,207 do comprimento da peça a partir das pontas;
  - momentos negativo e positivo com fcj de desforma;
  - força por alça (com 3 alças trabalhando), para escolher o inserto.
- **No Revit**: a família ganha o recorte do dente e os furos dos pinos. As barras saem do mesmo resultado da prévia.

Ordem: (1) lance pré-moldado com as ligações, pinos e içamento (feito em 23/11); (2) patamar intermediário e as escadas em L e U
por peças (núcleo, janela e Revit feitos em 24/11).

### 6.1 Patamar intermediário e escadas em L e U (24/11/2026)

Resposta do usuário: "o patamar de uma escada apoia no patamar da outra escada, mas há casos em que se faz um elemento parecido com uma
viga para apoiar" (há um projeto do escritório com a viga do patamar). Pesquisa (literatura de pré-fabricados e catálogos): além desses
dois, aparecem o patamar apoiado em cantoneiras ou consolos metálicos embutidos em paredes ou pilares e a escada "jacaré" (degraus
sobre vigas denteadas). Os dois primeiros e o apoio em consolo (que é uma ponta apoiada fora do conjunto) estão cobertos; a "jacaré"
fica para depois, como tipo próprio.

- **Peça "patamar"**: laje plana biapoiada, no local ou pré-moldada, com as mesmas ligações (apoio direto ou dente), pinos, içamento,
  barras e MEF do lance. Na janela: "Peça" = Patamar e o vão do patamar.
- **Cargas de outras peças** (`CargaApoiada`): a reação de uma ponta apoiada vira carga na peça que a recebe, por metro de largura
  dela, espalhada:
  - no comprimento de apoio, quando a borda da ponta atravessa a largura do suporte (peças em linha ou em L: o patamar de um lance
    sobre o patamar do outro);
  - na largura da peça apoiada, quando a borda corre ao longo do vão do suporte (em U, os lances chegam pelo lado do patamar).

  O cálculo por faixa de 1 m usa a média na largura do suporte e avisa quando a peça apoiada é bem mais estreita. Na janela, o campo
  "Cargas de outras peças" aceita `x comprimento: G + Q; …`.
- **Conjunto** (`ConjuntoEscada`): peças e vigas do patamar ligadas pelas pontas. O cálculo vai das peças apoiadas para as que suportam
  (apoio em ciclo é erro), passa as reações (com o trecho da peça além do apoio) e lista o que sai do conjunto para a estrutura.
- **Viga do patamar** (`VigaPatamar`): retangular, pré-moldada, biapoiada; recebe o patamar como carga distribuída (borda cobrindo o
  vão) ou em concentradas, e é calculada pelo cálculo da viga pré-moldada (D40).
- Conferido: patamar à mão (pℓ²/8 e reações), equilíbrio da escada em U (o que as peças pesam = o que sai pelas pontas + o que vai para
  a viga), patamar sobre patamar, MEF do patamar com as cargas dos lances (OpenSees 0,4 %, CalculiX 2,2 % no Md).
- **Arranjos prontos** (`ModelosConjunto`), com a posição de cada peça em planta (`PosicaoPeca`):
  - *em U*: os lances lado a lado, separados pela folga; o patamar é peça própria e vence a largura da caixa (transversal aos lances),
    com uma ponta na estrutura e a outra na viga do patamar (ou na estrutura);
  - *em L*: o 2º lance sai a 90° do patamar superior do 1º e apoia nele (o patamar do 1º cresce para caber a largura do 2º);
  - *em linha*: o 2º lance apoia no patamar superior do 1º.

  `ConferirPosicoes` confere se cada ponta cai sobre o suporte (x do apoio, dentro da largura, mesma cota de topo ou de assento).
- **Janela "Escada L/U"** (botão no painel Concreto armado): arranjo e medidas, peças editáveis na janela da escada, x de cada apoio;
  abas Planta, Cargas entre peças, Peças, Viga do patamar e Verificações (das peças e da viga).
- **No Revit**: clica-se o eixo do apoio de baixo do 1º lance e o sentido de subida; todas as peças entram com as barras, cada uma na
  sua posição; a viga do patamar entra como peça de concreto (sem barras: a armação é a do comando de vigas, D40).
- **Não modelado**: o encaixe (rebaixo) da peça que recebe a outra. As peças ficam com o topo nivelado e a ponta apoiada ocupa o mesmo
  volume no apoio; a janela avisa.

## 7. Decisões

Decididos em 23/11; ver a tarefa `docs/00_escopo/tarefas/P1-13.md`.
