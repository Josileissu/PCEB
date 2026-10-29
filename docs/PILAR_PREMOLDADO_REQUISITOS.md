# Pilar pré-moldado — levantamento de requisitos para o template (2026-10-29)

Objetivo (pedido do usuário): **desenhar** a peça e as armaduras já definidas pelo engenheiro, no padrão do
escritório; **não dimensionar**. As regras de norma entram como **valores padrão** e **verificações de
detalhamento** (avisos que não bloqueiam), como já acontece com ganchos, pinos de dobramento e traspasse.

Fontes usadas neste levantamento:
- Padrão do escritório: `auxiliar_cad/pilar_padrão/` (pilar **P61**, 12 recortes) e a prancha **PP127**
  (`desenvolvedor/exemplo_armadura_pilar_pre_moldado.pdf`, Dairy Factory, A1, bilíngue).
- Conhecimento das normas e da bibliografia. **Os itens marcados [CONFERIR] dependem do texto da norma**: os
  números de item e os valores precisam ser confirmados nos PDFs pedidos na seção 13 antes de virarem regra no código.

> Observação: o pedido cita "NBR 9162". A norma de pré-moldados é a **ABNT NBR 9062** (Projeto e execução de
> estruturas de concreto pré-moldado, edição 2017). Considerei que é ela.

---

## 1. O que o padrão do escritório já mostra (P61 e PP127)

### 1.1 Organização da prancha (A1, bilíngue PT/EN)
| Região | Conteúdo | Escala |
|---|---|---|
| Título | "P61"; no modelo há também o resumo "4ø25+8ø20" (resumo das longitudinais, em cores) | — |
| Faixa esquerda | **4 vistas de forma** (VISTA 1…4 = faces), lado a lado, com os níveis à esquerda (TOPO PILAR, níveis de piso, TÉRREO 0,00, BASE PILAR) e as cotas verticais (parciais e acumuladas); "COMPRIMENTO TOTAL DO PILAR: 1390,5" na vertical | 1:50 |
| Ao lado das vistas | **Elevação das armaduras**: longitudinais desenhadas lado a lado (cada posição em uma linha, com C= e as emendas cotadas, ex. 115), grampos U de topo e de base (N11, N7, N10, N8) e as **faixas de estribos** por zona (100 com "2x10 N2 c/10 / 20 N4 c/5"; 1040,5 com "71 N1 c/15"; 260 com "2x30 N2 c/10 / 26 N4 c/10") e as marcas de seção 1-1, 2-2, 3-3 | 1:50 |
| Centro superior | **Seções de armadura** 1-1 (topo), 2-2 (corrente), 3-3 (base), com as indicações N de cada barra; abaixo as **formas dos estribos** (43x43) e do grampo (2x40 N2 ø6.3 c/10 C=62) | 1:25 |
| Centro | **Cortes de forma** A-A (topo, com gabarito de chumbadores "GC1A" 13x13) e B-B (consolo duplo, hachurado), com as vistas 1…4 indicadas nos lados | 1:25 |
| Direita | **Relação do aço** e **resumo** (só peso), **Informações p/ produção** (PT e EN), detalhe "LIFTING HANDLE", **ATENÇÃO SPDA** (PT e EN) | — |
| Faixa inferior | Detalhes padrão: **armaduras do consolo** (planta, cortes AA/BB, formas das barras), **chaves de cisalhamento** (detalhe 01), **espaçamento das barras no canto** (1:5), **reforço nos furos** (com RA unitária própria), **raio de curvatura / emendas por traspasse / emendas por solda / comprimento das barras** (sem escala, NBR 6118), notas PT/EN | 1:25, 1:5, s/e |
| Canto inferior direito | Carimbo (cliente/gerenciadora, obra, código, revisões) | — |

### 1.2 Elementos da peça vistos nas referências
- **Seção** quadrada 50x50 (P61) e 40x40 (PP127); cobrimento ao eixo da barra de canto 5,5 cm, barras do canto
  agrupadas em "L" (3 barras com 5 cm entre eixos, detalhe ESPAÇAMENTO BARRAS 1:5).
- **Longitudinais com comprimentos diferentes e emendas**: 4 N17 ø25 C=1200 + 8 N15 ø20 C=900 + 8 N9 ø12,5 C=600
  + 4 N12 ø12,5 C=300 → a armadura **diminui com a altura** (emendas por traspasse de 115 cm cotadas).
- **Grampos U** no topo (3 N11 e 3 N7 ø12,5 C=200, 88/31/87) e na base (3 N10 e 3 N8 ø12,5 C=600) — fretagem e
  ancoragem nas extremidades.
- **Estribos**: perimetral N1 ø6,3 c/15 (zona corrente), N4 ø8 c/5 (topo) e c/10 (base/embutimento), **grampos
  suplementares** retos com ganchos (N2 ø6,3 "2x10 c/10" e "2x30 c/10" = 2 grampos por camada).
- **Consolo duplo** 40x23,5x25 (P61) e consolos 20x28,5x25 e 40x(23,5…33,5)x25 de **altura variável** (PP127):
  tirante N13 ø16 com **barras de ancoragem soldadas** N14 nas pontas ("Solda"), armadura inferior em U (N6),
  estribos verticais (N3) e horizontais (N5, costura), **pinos** N16 ø20 em L (2x2, 47 acima do consolo) para a viga.
- **Furo ø100 mm** atravessando o pilar (içamento/montagem com pino passante), com **reforço**: 8 grampos U
  ø6,3 (2x4 N1 C=125) por furo, com RA unitária própria.
- **Alças de içamento** em cordoalha 2x2 ø12,7 mm (C=175 no P61, C=155 no PP127) nas faces laterais, em 2 alturas
  (desforma/manuseio na horizontal).
- **Insertos no topo** ("INSERTO 25CB660", barras em vermelho = chumbadores/inserto com gabarito GC1A).
- **Chaves de cisalhamento** na base embutida (dentes 4/1 a cada 5 cm, detalhe 01) → ligação por **cálice**.
- **Aterramento estrutural** (PP127): "aterrinserte" + barra da armadura ø ≥ 8 mm (50 mm²) com continuidade
  elétrica, cabo de cobre nu 50 mm², detalhe genérico "consultar projeto de SPDA".
- Notas: "dimensões em cm", "utilizar neoprene 15 mm", "insertos metálicos são de responsabilidade da fornecedora".
- **Informações p/ produção**: fcj saque (30), fcj montagem (35), fck 28 dias (40), volume (m³), peso (t), quantidade.

---

## 2. Normas e regras que afetam o DESENHO

### 2.1 NBR 6118:2023 (detalhamento geral)
| Tema | Regra (para valor padrão ou aviso) | Item |
|---|---|---|
| Cobrimento nominal | pilar: CAA I 25 / II 30 / III 40 / IV 50 mm; cnom ≥ φ barra, ≥ φ feixe, ≥ 0,5 φ bainha | tab. 7.2, 7.4.7.5 |
| Longitudinal | φ ≥ 10 mm e ≤ b/8 | 18.4.2.1 |
| Espaçamento livre | ≥ 20 mm, ≥ φ (barra, feixe ou luva), ≥ 1,2 dmáx agregado; entre eixos ≤ 2b e ≤ 400 mm | 18.4.2.2 |
| Taxas | As,mín = 0,15 Nd/fyd ≥ 0,4 % Ac; As,máx = 8 % Ac **inclusive na emenda** (na prática ≤ 4 % fora dela) | 17.3.5.3 — só aviso |
| Estribos | φt ≥ 5 mm e ≥ φ/4 (φ do feixe); s ≤ 200 mm, ≤ menor dimensão, ≤ 24φ (CA-25) / 12φ (CA-50) | 18.4.3 |
| Flambagem das barras | estribo poligonal protege até 20 φt do canto; barras além disso → **estribo suplementar/grampo**, com gancho envolvendo a barra | 18.2.4 |
| Pinos de dobramento | barras CA-50: 5φ (φ < 20) e 8φ (φ ≥ 20); CA-60 6φ; estribos (φ ≤ 10): 3φ | tab. 9.1 e 9.2 (já no plugin) |
| Ganchos | barras: 180° ponta ≥ 2φ, 135° ≥ 4φ, 90° ≥ 8φ; estribos: 180°/135° ≥ 5φt e ≥ 5 cm, 90° ≥ 10φt e ≥ 7 cm | 9.4.2.3 e 9.4.6.1 (já no plugin) |
| Emenda por traspasse | proibida para φ > 32 mm; comprimida: l0c ≥ máx(lb,nec; 0,6 lb; 15φ; 200 mm) — o escritório usa ≥ 45φ na nota padrão | 9.5.2 |
| Emenda por solda / luva | traspasse com cordões ≥ 5φ (nota padrão do escritório); luvas mecânicas conforme fabricante | 9.5.4 / 9.5.3 |
| Feixes | até 3 barras em feixe em pilares; φn = φ√n | 9.4.3 |
| Consolos (in loco) | regras de consolo curto/muito curto também na 6118 | 22.5 [CONFERIR] |

### 2.2 NBR 9062:2017 (pré-moldado) — itens que mudam o desenho [CONFERIR todos na norma]
- **Cobrimento** pode ser reduzido em relação à 6118 para peças com controle rigoroso de produção (Δc = 5 mm);
  mínimos próprios para pré-fabricados.
- **Situações transitórias**: desforma, manuseio, armazenamento, transporte e montagem, com **coeficiente de
  ação dinâmica** e fcj de cada fase. Por isso a prancha traz "fcj saque" e "fcj montagem".
- **Dispositivos de içamento**: aço permitido nas alças (CA-25 e cordoalhas; restrições ao CA-50/CA-60),
  coeficiente de segurança, ancoragem mínima e diâmetro do pino de dobra da alça. O escritório usa **cordoalha ø12,7**.
- **Consolos**: classificação por a/d (curto 0,5 < a/d ≤ 1; muito curto a/d ≤ 0,5); tirante **ancorado na ponta**
  com barra transversal soldada de mesmo φ, laço horizontal ou chapa; **costura** horizontal (As,cost ≥ 0,4 As,tir
  no curto, ≥ 0,5 no muito curto [CONFERIR]) distribuída em 2/3 d; altura na face externa ≥ h/2; distância da
  almofada à borda e à ancoragem do tirante; armadura de suspensão quando a carga é indireta.
- **Dentes Gerber**, que são o par do consolo na viga apoiada.
- **Aparelhos de apoio**: elastômero simples (neoprene dureza Shore A 60±5, espessura mínima, folga às bordas),
  argamassa, chapa metálica. O escritório usa nota "neoprene 15 mm".
- **Cálice de fundação**: comprimento de embutimento lemb em função de e/h e da rugosidade (lisa ~1,5h a 2,0h;
  rugosa ~1,2h a 1,6h; mínimo absoluto). Rugosidade por **chaves de cisalhamento**; o escritório usa dentes de
  4 cm a cada 5 cm com 1 cm de profundidade (P61, detalhe 01).
- **Ligações**: pinos/chumbadores (furo grauteado + elastômero), ligações soldadas por chapas, ligações
  **semirrígidas** (chapas inferior e superior, barras passantes grauteadas, armadura de continuidade).
- **Tolerâncias** de fabricação e montagem (comprimento, seção, posição de insertos, prumo). Entram como nota e
  como folgas nas ligações.

### 2.3 Outras normas que tocam o detalhamento
| Norma | Uso no template |
|---|---|
| NBR 7480 (aço CA-25/50/60) | bitolas e massas (já na tabela de bitolas) |
| NBR 7483 (cordoalhas) | alças de cordoalha ø9,5/12,7/15,2 (já há aço "CP-…") |
| NBR 8800 / NBR 8681 | insertos, chapas, chumbadores, soldas (só desenho e nota "responsabilidade da fornecedora") |
| NBR 15200 (incêndio) | dimensão mínima da seção e distância c1 ao eixo da barra por TRRF — **aviso opcional** |
| NBR 14931 (execução) | tolerâncias, espaçadores, notas de execução |
| NBR 5419 (SPDA) | detalhe de aterramento: continuidade elétrica, barra ø ≥ 8 mm, aterrinserte |
| Elastômero (aparelhos de apoio) | especificação do neoprene na nota [CONFERIR a norma vigente] |
| NBR 6122 / cálice | lado da fundação (fora do escopo do pilar, mas o embutimento é cotado no pilar) |

---

## 3. Geometria da peça (parâmetros)

### 3.1 Seção
- Retangular/quadrada (b × h) — **categoria base**.
- Com chanfros nos cantos (pingadeira/chanfro 2×2 cm etc.).
- Circular (estribo circular ou **espiral**).
- Em "L", "T", "I", "U", vazada (pilares de canto, pilares com canaleta para painel ou com encaixe de calha).
- Seção variável ao longo da altura: redução no topo, mísulas e transição de seção (pilar escalonado).
- Pilar-parede (b ≫ h), com duas malhas afastadas por **caranguejos** (espaçadores entre as camadas).

### 3.2 Ao longo da altura
- Comprimento total; **níveis** com nome PT/EN (TOPO PILAR, pisos intermediários, TÉRREO 0,00, BASE PILAR) e cota;
  trecho **embutido** na fundação (base abaixo do nível do bloco).
- Faces numeradas **1 a 4** (= VISTA 1…4); tudo o que se coloca numa face (consolo, alça, furo, inserto) se refere
  a ela, com posição transversal (centrado, deslocado) e vertical (nível ou distância da base/topo).
- Furos passantes (ø100 com reforço padrão), furos para barras passantes/bainhas, nichos, recortes (dentes para
  apoio de terça ou calha), rebaixos.
- Topo: plano, inclinado (cobertura em duas águas), com **garfo** (forquilha) para a viga, com consolo de topo
  ou capitel.
- Base: lisa, **rugosa com chaves** (cálice), com **chapa de base** e chumbadores, com **luvas grauteadas** ou
  barras de espera para ligação com a fundação.

### 3.3 Consolos (por face e por nível)
- Forma: retangular; **trapezoidal** (fundo inclinado, altura variável: PP127 23,5→33,5); com chanfro na quina
  externa; **esconso** em relação à seção (planta inclinada); **duplo** (faces opostas no mesmo nível, como no P61);
  em 3 ou 4 faces; **corrido** ao longo da face (apoio de laje alveolar/painel); consolo **embutido metálico**
  (oculto) ou inserto para console metálico.
- Parâmetros: face, nível do topo do consolo, largura (b), balanço (a), altura na raiz (h) e na ponta (h1),
  deslocamento lateral, chanfros.
- Em vários níveis (até 5 ou mais), com tipos diferentes por nível.

---

## 4. Armadura longitudinal — configurações

1. **Simétrica contínua**: n barras iguais, cantos + intermediárias por face (nx, ny), uma camada.
2. **Agrupada no canto** (padrão P61: 3 barras em "L" por canto, 5 cm entre eixos) + intermediárias.
3. **Feixes** de 2 ou 3 barras.
4. **Bitolas mistas** (cantos ø25 + faces ø20), como no P61 "4ø25+8ø20".
5. **Redução com a altura**: posições com comprimentos diferentes, que terminam em níveis diferentes, com
   **emenda/traspasse cotado** (115 cm no P61) ou com simples interrupção ancorada.
6. **Emendas**: traspasse (≤ φ32), solda por traspasse, **luva mecânica** e luvas grauteadas na base.
   Limite comercial de 12 m: barras emendadas (o plugin já tem ARMDIVIDIR e o traspasse por norma).
7. **Barras de espera** no topo (continuidade com outro pilar ou viga) e **arranques** na base.
8. **Grampos U** nas extremidades (topo e base), em duas direções (N11/N7 no topo, N10/N8 na base): comprimento
   das pernas e a largura igual à da armadura.
9. **Barras adicionais** junto a furos, recortes e consolos.
10. Dupla camada nos pilares-parede, com caranguejos entre as camadas.

Representação: na **elevação**, uma linha por posição (lado a lado, à direita das vistas), com o C= e as cotas das
emendas; nas **seções**, círculos com as indicações N (linhas de chamada saindo para fora da seção, padrão P61).

## 5. Estribos — configurações
- **Perimetral fechado** (ganchos no canto superior esquerdo — decisão já registrada).
- **Suplementares**: grampo reto com ganchos (N2 do P61), estribo interno (losango, retângulo central) e
  grampos em duas direções ("2x" no texto = dois grampos por camada).
- **Circular** ou **espiral** (seções circulares).
- **Zonas** com espaçamentos próprios: topo (c/5), região de consolo (≥ altura do consolo, acima e abaixo),
  corrente (c/15), região de **emenda** das longitudinais (espaçamento reduzido), base/embutimento (c/10),
  furos. Cada zona é uma **faixa** do plugin (quantidade calculada pelo comprimento ÷ espaçamento).
- Verificações: φt e s máximo (18.4.3); barras a mais de 20 φt do canto sem proteção → sugerir grampo.

## 6. Consolos — armaduras (padrão P61/PP127)
| Posição | Função | Forma típica |
|---|---|---|
| Tirante (N13) | tração principal, sobre a almofada | reta, ancorada nas pontas com **barra transversal soldada** (N14) — alternativas: laço horizontal em U, chapa soldada, gancho vertical |
| Costura (N5) | estribos **horizontais** fechados envolvendo o pilar (duplo: 94x33) | distribuídos em 2/3 da altura útil |
| Estribos verticais (N3) | fechados no consolo (34x18) | "2x5" = 5 por consolo, 2 consolos |
| Armadura inferior (N6) | U invertido/aberto no fundo do consolo | 55 + abas 18 |
| Pinos (N16) | ligação com a viga (chumbadores em L) | 2x2 ø20, 47 acima do consolo |
| Suspensão | quando a carga chega por baixo/indireta | estribos verticais adicionais |
| Altura variável | estribos e barras "C=VAR" (PP127: 6 N5 ø6,3 C=VAR, 4x C=196…) | tabela de comprimentos (grupo VAR do plugin) |

Almofada/aparelho de apoio sobre o consolo: retângulo em planta e corte (neoprene 15 mm), com afastamentos da
borda e da face do pilar; também no **topo do pilar** (vigas de cobertura/terças).

## 7. Topo do pilar
- Apoio de viga de cobertura/terça: chumbadores (pinos) com **gabarito** (GC1A 13x13 no P61), insertos com chapa
  para solda ("INSERTO 25CB660"), almofada de neoprene, **garfo** lateral para travamento.
- Fretagem: estribos c/5 e grampos U.
- Furo para pino de içamento/montagem perto do topo.

## 8. Base e ligação com a fundação
- **Cálice**: comprimento embutido cotado, chaves de cisalhamento (detalhe padrão), estribos c/10 na zona.
- **Chapa de base** + chumbadores (pilar metálico-like) ou "column shoes" (sapatas metálicas pré-fabricadas).
- **Luvas grauteadas** / bainhas para barras de espera do bloco.
- Pilar sobre colarinho (cálice externo) ou sobre bloco com arranques.

## 9. Ligações viga–pilar (categorias avançadas)
- Articulada: consolo + almofada + **pino** (chumbador no consolo, furo grauteado na viga).
- **Semirrígida**: chapa inferior (no consolo) e chapa superior (no topo da viga/pilar) soldadas; ou
  **barras passantes** pelo pilar (furos com bainha) + graute + armadura de continuidade.
- Consolo oculto metálico (perfil embutido) com a viga em dente Gerber.
- Apoio de lajes (alveolares, duplo T): consolo corrido ou cantoneira metálica.

## 10. Içamento, manuseio e montagem
| Fase | No desenho |
|---|---|
| Desforma (pilar deitado) | **alças laterais** (cordoalha 2x2 ø12,7, alças a 1/5…1/4 do comprimento das extremidades — posição informada, não calculada), fcj saque |
| Tombamento (deitado → em pé) | alça/furo no topo; verificação de fissuração é do cálculo |
| Transporte/estocagem | pontos de apoio (nota), sem desenho obrigatório |
| Montagem | **furo ø100** com pino passante e reforço padrão, ou alça de topo, ou inserto rosqueado/âncora de içamento (catálogo do fornecedor); fcj montagem |
| Pós-montagem | graute do cálice, travamentos provisórios (fora da peça) |

As alças são **cordoalha**, que é aço "CP-190" no plugin e já tem a regra de não ter gancho nem traspasse. O detalhe
"LIFTING HANDLE" vem de forma padronizada: laço com raio no topo, pernas de 45 cm, 15 cm acima da face e 10 cm
entre pernas.

## 11. Outros itens da peça
- Aterramento SPDA (aterrinserte + detalhe genérico + nota "ver projeto de SPDA").
- Tubo de águas pluviais embutido (comum em galpões), eletrodutos, caixas.
- Insertos para fixação de painéis de fachada, trilhos tipo Halfen, cantoneira de proteção de canto contra impacto.
- Identificação da peça (marca, peso, posição de montagem) — "Informações p/ produção".

---

## 12. Categorias propostas para os templates
| Cat. | Descrição | Inclui |
|---|---|---|
| **PP-1** | Retangular, armadura **contínua** (sem redução), 0 ou 1 nível de consolo (simples ou duplo), base em cálice, topo com chumbadores | vistas 1–4, níveis, elevação das armaduras, seções, estribos por zona, grampos U, consolo padrão, alças, furo ø100, detalhes padrão, RA, informações p/ produção |
| **PP-2** | PP-1 + **redução da armadura com a altura** (emendas), consolos em até 2 níveis e 2 faces, consolo trapezoidal | posições escalonadas, emendas cotadas, grupos VAR |
| **PP-3** | Consolos em **vários níveis e faces** (até 5+), consolos esconsos, consolo corrido para laje, pinos e almofadas por consolo | consolos parametrizados por lista (face, nível, tipo) |
| **PP-4** | Ligações **semirrígidas**: chapas inferior/superior, barras passantes, insertos especiais, topo em garfo, chapa de base | biblioteca de insertos (blocos) + armaduras de ligação |
| **PP-5** | **Pilar-parede** com duas malhas e caranguejos, seções não retangulares (L, T, I, vazada), circular com espiral | malhas/telas, caranguejos, estribos especiais |

Sugestão de ordem: PP-1 completo e validado com o P61/PP127 → PP-2 → PP-3. As ligações (PP-4) e os
pilares-parede (PP-5) ficam para depois, quando houver exemplos reais.

## 13. Referências que preciso (deixar em `armadura/info_auxiliar/referencias_pilar_premoldado/`)
A pasta está fora do Git (as normas não vão para o repositório).
**Normas (PDF):**
1. ABNT NBR 9062:2017 — Projeto e execução de estruturas de concreto pré-moldado (**prioritária**).
2. ABNT NBR 6118:2023 — Projeto de estruturas de concreto (capítulos 7, 9, 17, 18 e 22).
3. ABNT NBR 15200 — Projeto de estruturas de concreto em situação de incêndio (se quiser o aviso de TRRF).
4. ABNT NBR 14931 — Execução de estruturas de concreto (tolerâncias), se o escritório usar nas notas.
5. Norma de aparelhos de apoio de elastômero que o escritório adota para o neoprene.
**Bibliografia (o que tiver):**
- El Debs, *Concreto pré-moldado: fundamentos e aplicações* (2ª ed., 2017) — consolos, cálice, ligações, içamento.
- Melo (org.), *Manual Munte de projetos em pré-fabricados de concreto*.
- PCI Design Handbook (8ª ed.) e fib Bulletin 43 (*Structural connections for precast concrete buildings*).
- Catálogos dos insertos usados (alças/âncoras de içamento, "25CB660", chumbadores, column shoes, consolos metálicos).
**Do escritório:**
- Mais 2 ou 3 pilares de categorias diferentes (com redução de armadura, vários consolos, ligação semirrígida, parede).
- DWG do P61 (para medir blocos, layers, estilos de texto e cotas, além das imagens).
- Os blocos padrão (inserto, alça, almofada, gabarito, carimbo) e a prancha de "informações gerais" citada na nota.

## 14. Perguntas abertas (decidir antes de implementar)
1. Entrada: o template recebe a armadura pronta (posições, bitolas, quantidades) por parâmetros/tabela, ou
   também importa de outro programa (TQS/Eberick)?
2. As 4 vistas sempre (mesmo sem nada em uma face) ou só as faces com elementos?
3. Cobrimento padrão (P61 aparenta 3,5 cm ao estribo) e se aplica a redução da NBR 9062.
4. Traspasse padrão das emendas: o "≥ 45φ" da nota ou o lb,nec calculado pela 6118 (o plugin já calcula o traspasse)?
5. Comprimento máximo das barras (12 m) e posição das emendas (todas na mesma seção ou alternadas)?
6. Posição das alças (regra do escritório: distância às extremidades) e quando usar furo ø100 × alça de topo.
7. Consolo: a ancoragem do tirante é sempre barra transversal soldada? Quando usar laço?
8. Nomes e ordem da numeração N (P61: estribos N1, N2, N4… longitudinais N9…N17) — numeração automática do
   plugin (por bitola) ou ordem do escritório?
9. Detalhes padrão (raio de curvatura, emendas, chaves, espaçamento no canto, reforço do furo): blocos fixos do
   escritório ou desenhados pelo template?
10. Carimbo e tamanho da folha: o template monta a folha inteira (A1 com regiões) ou só o detalhamento (bloco)?
11. Bilíngue sempre (PT/EN) ou opcional por obra?
12. "Informações p/ produção": volume e peso calculados pelo template (da forma) — confirmar o peso específico (2,5 t/m³).

## 15. O que o plugin já tem e o que falta
Já existe: barras com ganchos/pinos da norma, estribos fechados e de qualquer poligonal, faixas (distribuição
com quantidade), cortes/chamadas, cordoalha, traspasse e ARMDIVIDIR, grupo VAR, RA/resumo por detalhamento,
detalhamento = bloco, infraestrutura de templates (ARMTEMPLATE), templates de base de reservatório.

Falta para o pilar:
- **Desenho de forma**: vistas 1–4, cortes de forma com hachura, níveis com nome, cotas verticais encadeadas.
- **Seção transversal com barras "em ponto"** (círculos) e indicações N com linhas de chamada (hoje há rebatida
  e corte de faixa, não a seção com todas as barras).
- Consolos parametrizados (forma + armaduras padrão), almofadas, furos com reforço padrão, alças, insertos (blocos).
- Elevação das longitudinais lado a lado com emendas cotadas.
- Detalhes padrão (blocos), notas bilíngues, ATENÇÃO SPDA, "Informações p/ produção" com volume e peso.
- Montagem da folha (regiões do padrão), se for decidido na pergunta 10.
