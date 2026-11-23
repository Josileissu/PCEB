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

## 5. Plugin, posicionamento e janela

- **Janela de prévia** no kit `UI/Estilo` (D41), com abas Geometria, Cargas, Esforços (M, V, N), Reações, Deformada, MEF × cálculo
  e Verificações.
- **Criação no Revit**: o objeto que recebe a escada é a pergunta 2 abaixo.
- **Posicionamento**: o engenheiro escolhe as duas vigas ou lajes de apoio, ou dois níveis e um ponto de partida. O programa
  ajusta o número de espelhos ao desnível e mostra a escada na prévia antes de criar (D26).
- **Otimizador**: procura n, ae e ad (Blondel), a espessura h e o arranjo dos patamares que cabem no espaço e no desnível,
  ordenados por consumo de concreto e de aço e pela flecha.

## 6. Decisões

Decididos em 23/11; ver a tarefa `docs/00_escopo/tarefas/P1-13.md`.
