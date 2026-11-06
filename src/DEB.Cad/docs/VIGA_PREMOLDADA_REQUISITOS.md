# Viga pré-moldada — levantamento de requisitos para o template (2026-11-06)

Objetivo (pedido do usuário, PC2): **desenhar** a viga e as armaduras já definidas pelo engenheiro, no padrão do
escritório; **não dimensionar**. Como no pilar (`PILAR_PREMOLDADO_REQUISITOS.md`), a norma entra como **valores
padrão** e **verificações de detalhamento** (avisos que não bloqueiam).

Fontes deste levantamento:
- Normas, conferidas no índice local (`tools/normas/buscar.py`; página = página do PDF):
  - **NBR 9062:2017** (`Pré-Moldado\NBR 9062 - 2017 … Marquezin.pdf`);
  - **NBR 6118:2014** (`Concreto\Antigos`), porque as cópias de 2023 têm o texto embaralhado. [CONFERIR na 2023 se
    algum valor mudou.]
- O que já foi decidido e implementado no pilar pré-moldado (vale para a viga quando o assunto é o mesmo).
- **Padrão do escritório: ainda não recebido.** O usuário vai enviar o padrão a seguir. A seção 1 fica a preencher
  com ele (organização da prancha, layers, escalas, textos, blocos). Os itens marcados **[PADRÃO]** dependem desse exemplo.

---

## 1. Padrão do escritório (a preencher com o exemplo do usuário)
Pasta para o exemplo: `src/DEB.Cad/info_auxiliar/templates/viga_pre_moldada/` (fora do Git), com PDF e, de
preferência, **DWG**: o DWG dá layers, estilos de cota, alturas de texto e blocos.

O que vou medir no exemplo, como foi feito no P61:
| Item | Pergunta |
|---|---|
| Organização da prancha | Quais vistas existem (elevação/forma, planta do topo, vistas das extremidades), em que ordem e em que escala |
| Elevação das armaduras | Barras lado a lado ou sobre a forma? Estribos em faixas por zona? |
| Seções | Quais seções (meio do vão, apoio, dente, região isolada das cordoalhas) e em que escala; barras em ponto com indicação N |
| Protensão | Quadro das cordoalhas (posição, isolamento), força, alongamento, fcj de liberação |
| Extremidades | Dente Gerber e apoio sem recorte (armadura padrão), almofada, furos de pino, chapas |
| Detalhes padrão | Alças, furos, insertos, detalhes típicos (já existem no pilar: raio, emendas, comprimento) |
| Quadros e notas | RA, informações para produção, contraflecha, notas PT/EN |
| Layers, textos e cotas | Nomes e cores, ESTRUT{escala}, Arial 2 mm no papel? |

---

## 2. Normas e regras que afetam o DESENHO

### 2.1 NBR 6118:2014 — vigas (seção 18.3, p. 164–168) e gerais
| Tema | Regra (valor padrão ou aviso) | Item (pág.) |
|---|---|---|
| Campo de aplicação | biapoiada com ℓ/h ≥ 2 e contínua com ℓ/h ≥ 3; abaixo disso a peça é **viga-parede** (aviso) | 18.3.1 (p. 164) |
| Cobrimento nominal | viga CA: CAA I 25 / II 30 / III 40 / IV 50 mm; protendido: +5 mm (30/35/45/55); cnom ≥ φ da barra ou do feixe | tab. 7.2 |
| Espaçamento livre das longitudinais | horizontal ≥ 20 mm, ≥ φ (barra, feixe φ√n ou luva), ≥ 1,2 dmáx; vertical ≥ 20 mm, ≥ φ, ≥ 0,5 dmáx; vale também no traspasse | 18.3.2.2 (p. 164–165) |
| Ancoragem no vão | prolongar ≥ 10φ além do ponto de tensão nula e nunca < lb,nec; o diagrama é decalado de aℓ | 18.3.2.3 (p. 165) — o template **não** calcula o ponto de corte: o engenheiro informa os comprimentos |
| Barras nas mesas | o comprimento ganha a distância da barra à face da alma | 18.3.2.3.2 (p. 166) |
| Armadura nos apoios | chega ao apoio ≥ 1/3 As,vão (Mapoio nulo ou ≤ 0,5 Mvão) ou ≥ 1/4 (Mapoio > 0,5 Mvão) — **aviso** pela quantidade de barras que chegam | 18.3.2.4 (p. 166) |
| Ancoragem no apoio extremo | a partir da face do apoio: ≥ máx(lb,nec; **r + 5,5φ**; **60 mm**); com cobrimento ≥ 70 mm no gancho pode dispensar o lb,nec; apoio intermediário: 10φ (sem risco de momento positivo) | 18.3.2.4.1 (p. 166) |
| Estribos | fechados, com ramo horizontal envolvendo a armadura tracionada e ancorados na face oposta; **5 mm ≤ φt ≤ bw/10** (lisa ≤ 12 mm; tela ≥ 4,2 mm) | 18.3.3.2 (p. 167) |
| Espaçamento dos estribos | s ≥ passagem do vibrador; **smáx = 0,6 d ≤ 300 mm** (Vd ≤ 0,67 VRd2) ou **0,3 d ≤ 200 mm**; entre ramos: d ≤ 800 mm ou 0,6 d ≤ 350 mm. Sem os esforços, o template usa o **limite mais folgado** e avisa | 18.3.3.2 (p. 167) |
| Traspasse de estribos | só com tela ou barra de alta aderência | 18.3.3.2 |
| Torção | estribos fechados em todo o contorno, **ganchos de 45°** (135°); longitudinais de torção ≤ 350 mm entre si; uma barra em cada vértice | 18.3.4 (p. 168) |
| Armadura de pele | h > 60 cm: **0,10 % Ac,alma por face**, CA-50/60, s ≤ 20 cm e ≤ d/3, ≤ 5 cm²/m por face, ancorada nos apoios | 17.3.5.2.3 (p. 150), 18.3.5 (p. 168) |
| Taxas | As + As' ≤ 4 % Ac (fora das emendas); mínima pela 17.3.5.2.1 | 17.3.5.2.4 (só aviso) |
| Suspensão | onde outra viga ou carga se apoia ou se pendura | 18.3.6 (p. 168) |
| Ligação mesa–alma | ≥ **1,5 cm²/m** atravessando o plano de ligação (vigas T e I) | 18.3.7 (p. 168) |
| Pinos e ganchos | tab. 9.1/9.2 e 9.4 (já no plugin) | — |
| Traspasse | 9.5.2 (já no plugin) + **regra do escritório**: nunca < 45φ e nota ao lado da emenda | decisão de 2026-10-29 |
| Protensão — pré-tração (espaço livre) | horizontal ≥ 2φ, ≥ 1,2 dmáx, ≥ 2 cm (e 3φ / 2,5 cm em alguns arranjos da figura); vertical ≥ 2φ / 3φ, ≥ 1,2 dmáx, ≥ 2 a 3 cm | tab. 18.2 (p. 173) — [CONFERIR na figura qual valor vale para cada arranjo] |
| Protensão — pós-tração | bainhas: ah ≥ φext, ≥ 4 cm (e 1,2 / 1,5 φext, 5 cm); grupos de até 4 cabos nos trechos retos | tab. 18.1, 18.6.2.2 (p. 172) |
| Transferência (pré-tração) | comprimento de transferência ℓbpt e de ancoragem ℓbpd | 9.4.5 (p. 57) — dado do cálculo, o template só cota |

### 2.2 NBR 9062:2017 — o que vale para vigas
| Tema | Regra | Item (pág.) |
|---|---|---|
| Tolerâncias de fabricação (elementos lineares) | comprimento ±10 mm (L ≤ 5 m), ±15 (5–10 m), ±20 (> 10 m); seção −5/+10 mm; distorção ±5 mm; linearidade ±L/1000 | tab. 2 (p. 24) |
| Tolerâncias da protensão e dos insertos | cabo individual ±10 mm; centro resultante da protensão ±5 mm; insertos ±15 mm | 5.2.2.5 (p. 25) |
| Montagem | ±1,0 cm entre apoios consecutivos (acumulado 0,1 % do comprimento); nível dos apoios ±1,0 cm (acumulado 3,0; **2,0 em caminho de rolamento**); justaposição ±2,0 cm | 5.2.2.6 (p. 25) |
| Conteúdo obrigatório do desenho | fck; **fcj de manuseio/transporte e de aplicação da protensão (posição e tensão ou força)** ou fcj de liberação; aços, bitolas, quantidades, formas, soldas e emendas; **cobrimento de armaduras e insertos em todas as faces, inclusive a altura dos suportes da armadura superior em vigas T**; armadura de obra identificada à parte; volume e peso; ligações de obra; tolerâncias; **içamento (tipo, posição, ancoragem das alças)**; **módulo de elasticidade adotado** (protensão, saque, transporte, montagem, final) | 5.6.1.2 (p. 36–37) |
| Alças de içamento | CA-25/50/60 proibidos; cordoalha ou cabo de aço; βa 3,0; ângulo do cabo ≥ 45° | 5.3.3 (já no pilar) |
| **Estabilidade lateral** | no manuseio/montagem, L = distância entre alças ou apoios provisórios; sem cálculo da carga crítica: **L/bf ≤ 50** e **L·h/bf² ≤ 500** (bf = menor largura da zona comprimida) → **aviso** com a distância entre as alças | 6.1.2–6.1.6 (p. 38) — [CONFERIR a fórmula na página: o texto extraído está embaralhado] |
| Peças compostas (viga + capa) | aderência na interface pela armadura que atravessa (estribos salientes); a capa conta como peso próprio | 6.3 (p. 40), 5.6.2.2 (p. 37) |
| Apoio — juntas | seca (≤ 0,042 fcd e ≤ 1 MPa), argamassa (≤ 5 MPa), concreto local, dispositivo metálico, **elastomérico** | 7.2.1 (p. 42–43) |
| Aparelho elastomérico | NBR 19783; fretado com camadas ≤ 25 mm e chapas ≥ 2 mm; σk ≤ 7 MPa (simples) ou 8–15 MPa (fretado, pela menor dimensão); estabilidade dispensada se Σhi < a/5; não sobrepor nem encostar dois simples; elementos protendidos: prever levantar a peça para aliviar o apoio | 7.2.1.6 (p. 43–48) |
| Força horizontal no apoio | Hd = 0,16 Fd (elastômero), 0,08 (PTFE), 0,25 (chapas não soldadas), 0,4 (concreto × chapa), 0,5 (argamassa) — dado do cálculo; só entra na nota | 7.3.9 (p. 62–63) |
| Ligação por flexão (continuidade) | por protensão, solda, dispositivo metálico ou concretagem local; ligação que impede a rotação antes da sobrecarga; armadura contra fissuras | 7.2.3 (p. 51) |
| Vergalhões com adesivo | embutimento 8φ a 20φ; furo ≤ 1,5φ; borda e espaçamento ≥ 4φ (crítico 7φ / 14φ) | 7.2.2.3 (p. 49–50) |
| **Dente Gerber** | tratado como consolo (7.3); **tirante** ancorado no dente por **barra transversal soldada de mesmo φ** ou **alças horizontais** (7.3.3); ancoragem do tirante na viga começa a (dviga − d) do 1º estribo, com **lb de má aderência**; **estribos horizontais** ancorados na face do dente e entrando **1,5 lb** na viga; estribos verticais no dente (7.3.7); **suspensão** para toda a Fd (fyd ≤ 435 MPa), **concentrada na extremidade** em estribos fechados que envolvem a longitudinal (barras verticais ≤ 0,4 Fd); a fig. 23 marca "≤ dviga/4" junto à suspensão [CONFERIR na figura]; protendida: força horizontal de retração e fluência no tirante | 7.4 (p. 63–66) |
| **Apoio sem recorte** (extremidade da viga) | tirante Asd = (Fd/1,2 + Hd)/fyd (dado do cálculo), com as disposições da 7.3.3; costura Ash = Asv = Fd/(8 fyd), em 2d/3; tirante com **solda** ou ancorado **2 lb** (boa aderência) | 7.5 (p. 66–67) |
| Consolo na viga (apoio de terça/laje) | regras do consolo (7.3), já no pilar | 7.3 |
| Cobrimento | pré-moldado: Δc = 5 mm; pré-fabricado com fck ≥ 40 e a/c ≤ 0,45: mais −5 mm, com mínimos de **20 mm (vigas CA)** e **25 mm (protendidas)**; nas protendidas, o mínimo vale para cordoalhas e para os estribos em contato com elas | 9.2.1.1 (p. 77) |
| Espaçamento das cordoalhas (pré-tração) | entre faces ≥ 2φ, ≥ 1,2 dmáx, ≥ 2,0 cm | 9.2.1 (p. 76–77) |
| Amarração | mesas de vigas T: amarrações ≤ 35 cm | 9.2.6.2 (p. 78) |
| Liberação da protensão | só com o fcj do projeto e **nunca < 21 MPa** → aviso se o fcj informado for menor | 9.2.5.3 (p. 78) |
| Armazenamento | apoios nas regiões definidas no projeto; pressão ≤ 0,3 fcj (0,4 com fundação da pilha) → **pontos de apoio de estocagem** no desenho ou na nota | 10.2 (p. 82–83) |

### 2.3 Outras normas
| Norma | Uso no template |
|---|---|
| NBR 7483 / NBR 7482 | cordoalhas (ø9,5 / 12,7 / 15,2, CP-190/210 RB) e fios (já há aço "CP-…" no plugin) |
| NBR 19783 | especificação do neoprene na nota |
| NBR 14861 | apoio de laje alveolar sobre a viga (largura mínima do apoio) — **aviso opcional** |
| NBR 15200 | bw mínimo e c1 por TRRF — **aviso opcional** |
| NBR 8800 | chapas, insertos e soldas (só desenho e nota) |
| NBR 8400 | viga de rolamento (só tolerância e nota) |

---

## 3. Tipos de viga pré-moldada (seção transversal)
| Tipo | Uso comum | Particularidades no desenho |
|---|---|---|
| **Retangular** | vigas de piso e de travamento | categoria base |
| **I / duplo T** | cobertura e vãos grandes, quase sempre protendida | mesas e alma, mísulas, estribos da alma + armadura de mesa |
| **T invertido** | apoio de laje (alveolar, duplo T, pré-laje) dos dois lados | abas com armadura de suspensão e de aba (como consolo corrido) |
| **L / meio T invertido** | viga de borda com laje de um lado | idem, uma aba |
| **U / calha** | calha de cobertura, viga-calha | paredes finas, fundo, impermeabilização, furos de descida |
| **Viga com capa** (composta) | piso com laje + capa | **estribos salientes** acima do topo, rugosidade da face, negativo na capa (moldado no local) |
| **Seção variável** | viga de cobertura em duas águas (altura variável), mísulas nas extremidades | estribos de altura variável (grupo VAR do plugin) |
| **Terça** | cobertura | seção pequena, geralmente protendida (cobrimento mínimo 15 mm) |
| **Viga de rolamento** | ponte rolante | chumbadores do trilho, insertos, tolerância de nível 2,0 cm |

## 4. Geometria da peça (parâmetros)
- Comprimento total, vão teórico (ou os apoios em cada extremidade), seção (b, h; mesas bf, hf; abas; mísulas).
- **Extremidades** (uma configuração por ponta, E e D):
  - reta, com apoio direto;
  - **dente Gerber**: altura do dente, comprimento, chanfro;
  - recorte superior (encaixe na viga ou no pilar);
  - chanfro vertical;
  - **furo vertical para o pino** do consolo (ø, posição, bainha, graute);
  - chapa de apoio.
- Ao longo do vão:
  - **furos** horizontais (passagem de instalações ou de barras de continuidade);
  - **nichos** e recortes;
  - **consolos** na viga, para apoio de terças;
  - insertos e chapas para solda;
  - pontos de apoio de laje.
- Contraflecha: dado do cálculo, informada e só **anotada** no desenho.
- Peso e volume (2500 kgf/m³, como no pilar), com furos e dentes descontados.

## 5. Armadura passiva (CA-50/60)
1. **Inferior (positiva)**:
   - camadas de barras (1ª, 2ª, …), com bitolas mistas e feixes;
   - barras corridas e barras **interrompidas**, com o comprimento ou o início e fim informados;
   - ancoragem nos apoios: reta, **gancho de 90°** ou **laço**. Verificar r + 5,5φ e 60 mm a partir da face do apoio.
2. **Superior**: porta-estribos (construtivas) ou negativa; a negativa de continuidade fica na **capa** (armadura de obra, identificada à parte, NBR 9062 5.6.1.2 e).
3. **Pele** (h > 60 cm): pares por face, s ≤ 20 cm e ≤ d/3, ancorada nos apoios. Aviso se faltar.
4. **Emendas**: como no pilar (comprimento comercial de 12 m, alternadas ou na mesma seção, traspasse da norma ≥ 45φ com nota).
5. **Extremidades**: tirante do apoio sem recorte (7.5) ou do dente (7.4), com **barra transversal soldada** (padrão do pilar) ou **laço**; costura horizontal; **suspensão** concentrada junto ao dente.
6. **Mesas e abas**: armadura transversal da mesa (≥ 1,5 cm²/m na ligação mesa–alma); armadura de suspensão das abas do T invertido.
7. **Reforços**: em volta de furos e nichos, e junto aos insertos.

## 6. Armadura ativa (protendida por pré-tração)
- **Cordoalhas em grade** na seção: posições (x, y) nos nós da malha da pista, **5 cm entre eixos na horizontal e na
  vertical** (resposta do usuário, 2026-11-06), e quantidade por fiada. Origem da grade: **[PADRÃO]**.
- **Isolamento** (embainhamento) de cordoalhas nas extremidades, por cordoalha ou por grupo. É desenhado na elevação
  com uma convenção **[PADRÃO]** e listado num quadro de cordoalhas.
- **Cordoalhas superiores** (controle da tração no topo na liberação).
- Dados que vão para o desenho: tipo (CP-190 RB ø12,7…), força ou tensão inicial por cordoalha, alongamento
  (opcional), **fcj de liberação** (≥ 21 MPa), fcj de saque e de montagem, módulo E adotado, excentricidade
  (cota do centro resultante: tolerância ±5 mm).
- **Armadura de fretagem nas extremidades** (zona de transferência): estribos concentrados ao longo do ℓbpt e
  estribos ou malhas contra o fendilhamento na face. Quantidades informadas e zona cotada.
- Espaçamentos: 9.2.1 da NBR 9062 e tab. 18.2 da NBR 6118 (aviso).
- **Pós-tração** (bainhas, ancoragens, traçado parabólico) fica numa categoria posterior, se o escritório usar.

## 7. Estribos
- **Fechado** (ganchos no canto superior esquerdo: decisão já registrada) ou **aberto em U** com estribos salientes
  para a capa. A altura saliente é informada.
- **Múltiplos ramos** (2, 4, …), quando a largura passa do st,máx; ramos internos e grampos.
- **Torção**: fechado com ganchos de 135° (opção por zona).
- **Zonas** como no pilar:
  - extremidades (apoio ou dente; suspensão concentrada; fretagem da protensão);
  - zona corrente;
  - em volta de furos.

  Cada zona é uma faixa do plugin, cotada na elevação.
- **Altura variável**: estribos VAR nas vigas de seção variável.
- **Mesas e abas**: estribos ou grampos da mesa, e estribos da aba no T invertido.
- Verificações: φt (5 mm ≤ φt ≤ bw/10), smáx, st,máx, ganchos de torção.

## 8. Ligações e apoios (o que aparece na viga)
| Ligação | No desenho da viga |
|---|---|
| Articulada sobre consolo | almofada de neoprene (a × b × e), **furo vertical ou bainha** para o pino do consolo do pilar, graute |
| Dente Gerber sobre outra viga ou consolo | geometria do dente + armaduras padrão (7.4) + almofada |
| Semirrígida | chapas para solda no topo e no fundo (inserto com grapas), barras de continuidade passando pelo pilar (na capa ou em bainhas) |
| Apoio de laje | aba (T invertido) ou topo com estribos salientes; insertos para fixação |
| Apoio de terça | consolo na viga ou inserto/chumbador no topo |

## 9. Içamento, manuseio, estocagem
- **Alças** de cordoalha CP-190 (como no pilar), em par simétrico ao CG, a **≈ 20 % do comprimento** de cada ponta
  (valor do escritório, editável) ou na posição informada. Detalhe da alça e cota da posição.
  [PADRÃO: posição e número de pernas.]
- Aviso de **estabilidade lateral** (6.1.6) com a distância entre as alças.
- **Pontos de apoio para estocagem e transporte** (nota ou marcação na elevação; 10.2 e 6.2.4).
- Furo para pino de içamento (alternativa) com reforço padrão (como no pilar).

## 10. O que o template desenha (proposta; ajustar ao [PADRÃO])
1. **Elevação/forma** com cotas (comprimento total, apoios, dentes, furos, insertos, alças), mais as vistas das
   extremidades E e D e a planta do topo, se houver mesa ou insertos.
2. **Elevação das armaduras**: longitudinais (passivas lado a lado ou sobrepostas, com C= e emendas cotadas),
   cordoalhas com os trechos isolados, faixas de estribos por zona e as marcas das seções.
3. **Seções** onde o arranjo muda, como no pilar: meio do vão, apoio, dente, zona isolada. Barras e cordoalhas em
   ponto (diâmetro real), indicação N por barra e a grade da pista cotada.
4. **Formas das barras** (estribos, U, tirantes, laços) e detalhe das extremidades (dente ou apoio sem recorte), com as armaduras padrão.
5. **Quadro de cordoalhas**: posição, quantidade, isolamento E/D, força inicial.
6. **RA** do detalhamento, com as cordoalhas na RA (aço CP; peso por metro).
7. **Informações para produção**:
   - fck, fcj de liberação, saque e montagem;
   - protensão (força por cordoalha);
   - E adotado;
   - contraflecha estimada;
   - volume, peso e quantidade.
8. **Notas**: cobrimento por face, tolerâncias (tab. 2 e 5.2.2.5), neoprene, insertos da fornecedora, armadura de obra.

## 11. Categorias propostas
| Cat. | Descrição |
|---|---|
| **VP-1** | Retangular, **concreto armado**, biapoiada, extremidades retas sobre almofada (furo de pino opcional), positiva em camadas, porta-estribos, pele, estribos por zona, alças, RA, produção |
| **VP-2** | VP-1 + **dente Gerber** em uma ou nas duas pontas (armaduras da 7.4) e apoio sem recorte com o tirante da 7.5; ligação por ponta: **pino** e/ou **chapa/cantoneira para solda** |
| **VP-3** | **Protendida por pré-tração** (retangular, I e T invertido): grade de cordoalhas, isolamento, fretagem, quadro de cordoalhas, fcj de liberação |
| **VP-4** | Seções **T invertido, L, I** e **viga com capa** (estribos salientes, armadura de obra na capa), abas com suspensão |
| **VP-5** | Seção variável (duas águas), calha (U), viga de rolamento, ligações semirrígidas (chapas, barras passantes), consolos na viga |

Ordem (atualizada com as respostas de 2026-11-06): **VP-1 + VP-2** (armada, extremidade reta e dente, pino e chapas)
validadas com o padrão de viga armada → **VP-3** com o desenho protendido que o usuário vai enviar → VP-4 → VP-5.

## 12. Entrada do template (especificação inicial)
| Grupo | Campos |
|---|---|
| Identificação | nome (V12), quantidade de peças, idioma (PT/EN/PT-EN), escalas (elevação, seções, detalhes) |
| Geometria | tipo da seção; b, h (e bf, hf, abas, mísulas); comprimento; vão ou apoios; extremidade E e D (reta / dente: altura, comprimento / recorte / chanfro); furos e nichos (tabela: tipo, posição, dimensões); contraflecha |
| Cobrimentos | fundo, topo, laterais, extremidades (um valor por face, como no pilar) |
| Concreto | fck, fcj de liberação, saque e montagem, E adotado, peso específico (2500) |
| Longitudinais passivas | tabela: posição (inferior/superior/pele), camada, quantidade, bitola, feixe, início/fim (vazio = viga toda), ancoragem E/D (reta, 90°, laço) |
| Emendas | como no pilar |
| Protensão | tabela de cordoalhas: tipo, fiada (y), posições (x ou quantidade centrada), isolamento E/D (cm), força inicial; grade da pista (passo) |
| Estribos | tipo (fechado, U saliente), ramos, bitola; zonas (nome, de/até a partir da ponta E/D, espaçamento, ganchos de torção); salientes (altura) |
| Extremidades | por ponta: forma (reta/dente), ligação (pino, chapa/cantoneira para solda, ou as duas; chapa do catálogo de insertos), tirante (bitola, nº, soldada/laço), costura, suspensão, estribos do dente, almofada (a × b × e), furo de pino |
| Içamento | alças (cordoalha, pernas, posição %, ou valores), pontos de estocagem |
| Insertos | tabela: tipo (do catálogo de insertos do pilar), face, posição |
| Outros | armadura de obra (capa), notas |

## 13. Reaproveitamento do pilar (já implementado)
| Já existe no pilar | Uso na viga |
|---|---|
| Vistas com níveis e cotas em cadeia | elevação com cotas na horizontal (a viga é o pilar "deitado") |
| Longitudinais por lista com emendas e feixes (`ListasArmaduraPilar`) | positiva, negativa e pele por lista |
| Zonas de estribos por lista (`LerZonas`) | zonas a partir das pontas |
| Seções com barras em ponto e indicações (`Cortes`) | seções no vão, no apoio e no dente |
| Consolos (biela/tirante, barra soldada, laço, a2 da 7.3.3.4) | dente Gerber (a 7.4 remete à 7.3) e consolos na viga |
| Alças de cordoalha, furo com reforço, catálogo de insertos | igual |
| Quadro de produção, detalhes típicos, notas PT/EN, peso a 2500 kgf/m³ | igual, com os dados de protensão |
| Parâmetro tabela (`FormTabela`, Ctrl+V do Excel), modelos em CSV | igual |

**Falta**:
- cordoalhas com isolamento: barra CP com trechos "isolados", convenção de desenho e RA;
- quadro de cordoalhas;
- grade da pista;
- seções I/T/L;
- estribos salientes e em U;
- estribos de altura variável (o grupo VAR já existe);
- extremidades com dente e as armaduras padrão da 7.4 e da 7.5;
- verificação de estabilidade lateral.

## 14. Perguntas para o usuário (antes de codar)
1. O padrão que vai enviar é de viga **armada** ou **protendida**? Qual seção (retangular, I, T invertido…)?
2. Quais extremidades são as mais comuns no escritório: reta sobre consolo com pino, dente Gerber, ou ambas?
3. Protensão: as cordoalhas seguem uma **grade fixa da pista** da fábrica? Qual passo e qual origem?
4. Isolamento: como o escritório desenha (traço mais grosso, hachura, tabela só)?
5. Estribos salientes para capa: são usados? A negativa da capa entra no desenho da viga (armadura de obra) ou em outra prancha?
6. Alças: valem os mesmos 20 % do pilar? Quantas pernas?
7. A viga do padrão tem contraflecha, pontos de estocagem e E adotado no desenho?

### 14.1 Respostas do usuário (2026-11-06)
1. **Armada e protendida**: o template cobre as duas (VP-1 e VP-3 andam juntas). Ele vai enviar também um desenho de
   viga protendida.
2. **Extremidades**: reta e **dente Gerber**, ambas **com pino** (furo vertical/bainha para o pino do consolo) e também
   **com chapas/cantoneiras para solda** (insertos na extremidade). → Extremidade = forma (reta/dente) + ligação
   (pino, chapa/cantoneira, ou as duas), escolhidas por ponta.
3. **Grade das cordoalhas**: **5 cm entre eixos**, na horizontal e na vertical. → As posições possíveis das cordoalhas
   são os nós dessa grade. [Falta confirmar a origem da grade (centro da seção ou face) — sai do desenho protendido.]
4. **Isolamento**: a convenção sai do desenho protendido que ele vai enviar.
5. **Estribos salientes para a capa**: usados, **mas não só eles** (também estribos fechados, conforme a viga) → tipo
   do estribo por zona/viga. [Falta: se a armadura da capa entra no desenho da viga — ver no padrão.]
6. **Alças**: vale a regra do pilar (par simétrico ao CG, ≈ 20 % do comprimento a partir de cada ponta, editável).
7. **Contraflecha, pontos de estocagem e E adotado**: entram no desenho (informações para produção e notas).

## 15. Decisões herdadas do pilar (valem até o usuário dizer o contrário)
- **O template monta o detalhamento** (bloco do plugin), não a folha.
- Numeração automática.
- Detalhes padrão desenhados pelo template.
- Traspasse da norma com mínimo de 45φ e nota ao lado.
- Idioma PT, EN ou PT/EN.
- Cobrimento por face.
- Peso a 2500 kgf/m³.
- Entrada por parâmetros e por tabela (com Ctrl+V do Excel).
- Modelos salvos em CSV.
