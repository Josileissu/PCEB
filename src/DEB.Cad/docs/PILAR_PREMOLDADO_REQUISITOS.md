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

### 1.3 O DWG do P61 (`316-CON-AB-100-6098-DET-BAS-R00.dwg`, analisado em 2026-10-29)
- O arquivo traz **3 pilares na mesma folha** (P60, P61, P71; folhas 6097/6098/6110) — a folha é reaproveitada
  trocando o conteúdo.
- **Modelo em tamanho real (cm)**; as escalas saem das **viewports do Layout1** (papel em mm): vistas 1:50,
  seções/cortes 1:25, consolo 1:25, alça 1:20, chaves de cisalhamento 1:10, espaçamento no canto 1:5. → O template
  desenha tudo em verdadeira grandeza; a escala de cada região define a altura dos textos e o estilo de cota
  (igual ao que o plugin já faz com a escala por barra).
- Textos: Arial; 2 mm no papel (10 un em 1:50, 5 un em 1:25); títulos maiores (11 a 16 un).
- Estilos de cota: **ESTRUT5, ESTRUT10, ESTRUT20, ESTRUT25, ESTRUT50** (um por escala) e ESTRUT50_QLEADER; cotas
  da forma em "DIM"/"Cotas"; "COMPRIMENTO TOTAL DO PILAR: <>" é uma cota com texto prefixado.
- Barras na seção = **círculos com o diâmetro real** (r 1,0 ø20; r 0,625 ø12,5; r 1,25 ø25).
- Layers usados: `DT-Forma`/`Pilares` (forma), `DT-Armadura`/`Armadura` (barras), `DT-Barras no corte`,
  `DT-Barras na forma`, `Descrição da armadura`, `Textos armaduras`, `Texto da cota`, `Cotas`/`DIM`/`DT-Cotas`,
  `Relação do aço`, `Insertos`, `DT-Furo`, `Cortes`, `DT-Pontilhado` (DASHDOT), `Hachuras` (AR-CONC), `TEXTOS`.
  [DECIDIR na implementação: usar estes nomes ou os layers ARM-* do plugin.]
- Blocos: `INSERTO_25CB660_FRONTAL` (inserto do topo, espelhado conforme a vista), `GC1A` (gabarito no corte A-A),
  `embutimento_100_50` (trecho embutido com as chaves, nas 4 vistas), `50` (alça na vista, espelhada).

---

## 2. Normas e regras que afetam o DESENHO

Conferido em 2026-10-29 no índice local (`tools/normas/buscar.py`). Página = página do PDF. **NBR 9062:2017**
= `Pré-Moldado\NBR 9062 - 2017 ….pdf`. **NBR 6118**: as duas cópias de 2023 da pasta têm o texto
embaralhado (fonte com codificação própria) — conferido na **edição 2014** (`Concreto\Antigos`), cujos itens de
detalhamento de pilares se mantêm; [CONFERIR na 2023 impressa] se algum valor mudou.

### 2.1 NBR 6118 (detalhamento geral)
| Tema | Regra (valor padrão ou aviso) | Item (pág. 2014) |
|---|---|---|
| Cobrimento nominal | pilar: CAA I 25 / II 30 / III 40 / IV 50 mm; cnom ≥ φ barra/feixe | tab. 7.2 |
| Longitudinal | φ ≥ 10 mm e ≤ 1/8 da menor dimensão; **≥ 1 barra em cada vértice**; seção circular ≥ 6 barras | 18.4.2.1–2 (p. 169) |
| Espaçamento livre | ≥ 20 mm, ≥ φ (barra, feixe φn = φ√n, ou luva), ≥ 1,2 dmáx — vale também na região de traspasse; entre eixos ≤ 2× menor dimensão e ≤ 400 mm | 18.4.2.2 (p. 169) |
| Taxas | limites de 17.3.5.3 (só aviso) | 17.3.5.3 |
| Estribos | em toda a altura, obrigatórios no cruzamento com vigas/lajes; φt ≥ 5 mm e ≥ φ/4; s ≤ 200 mm, ≤ menor dimensão, ≤ 24φ (CA-25) / 12φ (CA-50); C55–C90: s reduzido 50 % e ganchos ≥ 135° | 18.4.3 (p. 169–170) |
| Flambagem das barras | estribo protege as barras até **20 φt do canto** se nesse trecho não houver mais de 2 barras (sem contar a do canto); as demais → grampo/estribo suplementar | 18.2.4 (p. 163–164) |
| Pilar-parede | maior dimensão > 5× a menor → pilar-parede; armadura transversal ≥ 25 % da longitudinal por metro da maior face | 18.5 (p. 170) |
| Pinos de dobramento | barras CA-50: 5φ (φ < 20) / 8φ (φ ≥ 20); estribos φ ≤ 10: 3φ | tab. 9.1/9.2 (já no plugin) |
| Ganchos | já no plugin | 9.4.2.3 / 9.4.6.1 |
| Traspasse | tração: l0t = α0t·lb,nec ≥ máx(0,3 α0t lb; 15φ; 200 mm), α0t 1,2…2,0 pela % emendada (tab. 9.4); **comprimida: l0c = lb,nec ≥ máx(0,6 lb; 15φ; 200 mm)**; barra **permanentemente comprimida: todas podem ser emendadas na mesma seção**; proibido para φ > 32 mm | 9.5.2.1–3 (p. 61) |
| **Regra do escritório** | traspasse = norma, **nunca < 45φ**, com **nota ao lado da emenda** para o projetista ajustar | decisão de 2026-10-29 |

### 2.2 NBR 9062:2017 (pré-moldado) — conferida
| Tema | Regra | Item (pág.) |
|---|---|---|
| Tolerâncias de fabricação (pilares) | comprimento ±10 mm (L ≤ 5 m), ±15 (5–10 m), ±20 (> 10 m); seção −5/+10 mm; distorção ±5 mm; linearidade ±L/1000; **insertos ±15 mm** | 5.2.2.2, tab. 2; 5.2.2.5 (p. 24–25) |
| Tolerâncias de montagem | prumo ±1/300 da altura ≤ 2,5 cm; pilar em planta/elevação ±1,0 cm; nível dos apoios ±1,0 cm (acumulado 3,0) | 5.2.2.6 (p. 25) |
| Amplificação dinâmica | βa = 1,3 (saque, manuseio, montagem; transporte desfavorável), 1,4 (circunstâncias desfavoráveis), 0,8 (transporte favorável), **3,0 para os dispositivos de içamento**; pilares: βa 1,3 com tensão na armadura longitudinal ≤ 0,50 fyk; γf = 1,30 | 5.3.2 (p. 31) |
| **Alças de içamento** | **proibido CA-25, CA-50 e CA-60**; ASTM A36 só ø10–25 sem feixe; **cordoalha** (NBR 7483, não engraxada) ou cabo de aço (ISO 2408); içamento só no plano dos ramos; ângulo do cabo ≥ 45° com a peça; ancoragem por aderência verificada; prioridade a furos de içamento e dispositivos mecânicos | 5.3.3 (p. 31–32) |
| **Conteúdo obrigatório do desenho** | fck; fcj de manuseio/transporte; aços, bitolas, quantidades, formas, **soldas e emendas**; **cobrimento das armaduras e insertos em todas as faces**; armadura adicional de obra identificada à parte; **volume e peso**; ligações de obra; **tolerâncias dimensionais**; detalhamento do **içamento (tipo, posição, ancoragem das alças)** | 5.6.1.2 (p. 36–37) |
| Aparelho de apoio elastomérico | NBR 19783; simples ou fretado (camadas ≤ 25 mm, chapas ≥ 2 mm); Shore A 50/60/70 → G 0,8/1,0/1,2 MPa; σk ≤ 7 MPa (simples); não sobrepor nem encostar dois aparelhos simples | 7.2.1.6 (p. 43–45) |
| Consolo — classes | 1 < a/d ≤ 2: viga em balanço; 0,5 < a/d ≤ 1: curto (biela-tirante); a/d ≤ 0,5: muito curto (atrito-cisalhamento) | 7.3.2.2 (p. 57–58) |
| Consolo — geometria | altura na face externa h1 ≥ h/2 (deduzido o afastamento do apoio); a1 e b com folga (fig. 17); afastamento lateral do apoio > cobrimento → armar fendilhamento | 7.3.3.1–3 (p. 58) |
| Consolo — a2 (apoio → face externa) | **c + φ** (tirante com barra transversal soldada de mesmo φ); c + 3,5φ (alça horizontal φ < 20); c + 5φ (alça horizontal φ ≥ 20); c + 4φ (alça vertical φ ≤ 16) | 7.3.3.4 (p. 58) |
| Consolo — tirante | φ ≤ 1/6 da menor dimensão do consolo e ≤ 25 mm (barra soldada) ou ≤ 1/8 (alça horizontal); espaçamento ≤ 20φ ou d (soldada) / 15φ ou d (alça); **no quinto superior da altura**; sem redução de seção até o engaste; **alça vertical proibida para φ > 16** | 7.3.3.6–16, 7.3.5.1 (p. 58–61) |
| Consolo — costura | φ ≤ 1/15 da menor dimensão; espaçamento vertical ≤ 10 cm e ≤ a; distribuída em 2/3 d junto ao tirante; As,cost/s ≥ 0,4 As,v/d (curto) ou 0,5 (muito curto); mínimo 0,15·b cm²/m | 7.3.3.12, 7.3.6 (p. 59–62) |
| Consolo — demais | estribos verticais ≥ 0,15 % bw·h (a/d ≤ 1, carga direta); suspensão para carga indireta; **no pilar, armadura igual à do tirante na face do engaste** (fig. 20a) | 7.3.7, 7.3.8, 7.3.3.14 (p. 60–62) |
| Cálice — interface | lisa; **rugosa = 3 mm a cada 3 cm**; **chaves = profundidade ≥ 1 cm a cada ≤ 5 cm** (inclinação ≤ 45°), em toda a altura de embutimento (o P61, 4/1 a cada 5, atende) | 7.7.1.3, fig. 25 (p. 68) |
| Cálice — embutimento | tab. 15: lisa/rugosa 1,5h (Md/Ndh ≤ 0,15) a 2,0h (≥ 2); com chaves 1,2h a 1,6h; tração: 2,0h e não lisa; **≥ 40 cm** e compatível com a ancoragem; ancoragem do pilar conta a partir de 0,5 Lemb (liso/rugoso) ou como traspasse (chaves) | 7.7.2, 7.7.5.4 (p. 68–72) |
| Cálice — construtivo | colarinho ≥ 15 cm; fundo ≥ 20 cm; folga para o enchimento e o vibrador | 7.7.5 (p. 72) |
| Cobrimento | pré-moldado: Δc = 5 mm sobre a 6118; pré-fabricado com fck ≥ 40 e a/c ≤ 0,45: mais −5 mm, **mínimo 20 mm para pilares** de concreto armado | 9.2.1.1 (p. 77) |

### 2.3 Outras normas que tocam o detalhamento
| Norma | Uso no template |
|---|---|
| NBR 7480 (aço CA-25/50/60) | bitolas e massas (já na tabela de bitolas) |
| NBR 7483 (cordoalhas) | alças de cordoalha ø9,5/12,7/15,2 (já há aço "CP-…") |
| NBR 8800 / NBR 8681 | insertos, chapas, chumbadores, soldas (só desenho e nota "responsabilidade da fornecedora") |
| NBR 15200 (incêndio) | dimensão mínima da seção e distância c1 ao eixo da barra por TRRF — **aviso opcional** |
| NBR 14931 (execução) | tolerâncias, espaçadores, notas de execução |
| NBR 5419 (SPDA) | detalhe de aterramento: continuidade elétrica, barra ø ≥ 8 mm, aterrinserte |
| NBR 19783 (aparelhos de apoio elastoméricos, citada na 9062 7.2.1.6) | especificação do neoprene na nota |
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

## 13. Referências
**Normas: resolvido** — ficam em `S:\Meu Drive\Normas` e foram indexadas (117 PDFs; ver CLAUDE.md, "Normas"). A pasta `info_auxiliar/referencias_pilar_premoldado/` fica para bibliografia e catálogos que não estejam lá.
**DWG do P61: recebido** (seção 1.3). Ainda úteis: os itens abaixo marcados como bibliografia/escritório.

### 13.1 Lista original
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

## 14. Decisões do usuário (2026-10-29)
- **O template monta o detalhamento** (o bloco do plugin com todas as regiões), não a folha/carimbo; a folha
  continua sendo montada com viewports no layout, como no P61.
- **Numeração automática** do plugin.
- **Detalhes padrão desenhados pelo template** (raio de curvatura, emendas, chaves, espaçamento no canto,
  reforço do furo, alça, consolo).
- **Traspasse**: calculado pela norma (o plugin já calcula), **nunca menor que 45φ**; onde houver emenda, uma
  **nota ao lado** para o projetista ajustar se necessário.
- Normas: índice local de `S:\Meu Drive\Normas` (`tools/normas/`, ver CLAUDE.md).

### 14.1 Respostas de 2026-10-29 (2ª rodada)
- **Entrada da armadura**: por **parâmetros**, e também por **tabela** (colar do Excel / importar .xlsx/.csv) ou por
  uma **janela** de edição das posições.
- **4 vistas sempre**.
- **Idioma dos textos**: opção **PT**, **EN** ou **PT/EN** (bilíngue não é sempre).
- **Cobrimentos no template**: um valor por face (faces 1 a 4), **topo** e **base** — uma face pode ter cobrimento
  maior (ex.: face em contato com contenção).
- **Emendas**: opcional. **Com emendas** = barras divididas no comprimento comercial (12 m), com o campo
  **"alternadas" ou "na mesma seção"**; o traspasse segue a regra do escritório (norma, ≥ 45φ, nota ao lado).
  **Sem emendas** = barras **em rolo** (sem limite de 12 m; avisar se a bitola não existir em rolo na tabela).
- **Alças (desforma/manuseio)**: o par de alças fica **simétrico em relação ao CG** do pilar (com os consolos, o CG
  sai do meio) e a **≈ 20 % do comprimento** a partir de cada extremidade; com consolo, a posição é ajustada
  para manter o CG centralizado entre as alças. Valores editáveis.
- **Montagem**: **alça de topo** ou **furo** (opção marcada). O furo fica a **25 % a 35 % do comprimento** a partir
  da extremidade (padrão 30 %; aviso fora da faixa). [Assumido: medido a partir do TOPO — confirmar no uso.]
- **Tirante do consolo**: padrão **barra transversal soldada**; opção **laço** (alça horizontal; e vertical só
  φ ≤ 16), com o a2 mínimo da NBR 9062 7.3.3.4 de cada caso.
- **Peso**: concreto **2500 kgf/m³** → peso = volume (forma + consolos − furos) × 2,5 t/m³.

## 15. Entrada do template PP-1 (especificação)
| Grupo | Campos |
|---|---|
| Identificação | nome (P61), quantidade de peças, idioma (PT/EN/PT-EN), escalas (vistas 1:50, seções 1:25, detalhes) |
| Geometria | b (faces 1/3) × h (faces 2/4), comprimento total, chanfros; níveis (nome PT/EN, cota) e nível da base; embutimento (comprimento, interface: lisa/rugosa/chaves e padrão das chaves) |
| Cobrimentos | face 1, face 2, face 3, face 4, topo, base |
| Concreto | fck, fcj saque, fcj montagem, peso específico (2500 kgf/m³) |
| Longitudinais | lista de posições: bitola, aço, quantidade por face/canto (ou arranjo: cantos agrupados, faces), comprimento ou nível inicial/final (para a redução com a altura); grampos U do topo e da base |
| Emendas | sim/não; se sim: comprimento comercial (12 m), alternadas ou mesma seção; traspasse = máx(norma, 45φ) + nota |
| Estribos | perimetral (bitola, aço); zonas (início, fim, espaçamento) — topo, consolo, corrente, emenda, base; grampos suplementares (quantos por camada, direção) |
| Consolos | lista: face, nível do topo, b, a (balanço), h raiz, h ponta, posição lateral; tirante (bitola, nº, ancoragem soldada/laço), costura, estribos verticais, armadura inferior, pinos (bitola, nº, comprimentos), almofada (a × b × espessura) |
| Topo | inserto (bloco) e gabarito, chumbadores, almofada |
| Içamento | alças (cordoalha ø, nº de pernas, distância às extremidades — padrão por CG a ~20 %); montagem: alça de topo **ou** furo (ø, % do comprimento, reforço padrão) |
| Outros | aterramento SPDA (sim/não, face), notas (insertos da fornecedora, neoprene, SPDA) |

Saída (um detalhamento = bloco): 4 vistas de forma com níveis e cotas; elevação das armaduras com faixas de
estribos e emendas cotadas (com nota); seções 1-1/2-2/3-3 com barras em ponto e indicações N; cortes de forma
A-A/B-B; formas das barras; detalhes padrão (consolo, chaves, canto, furo, alça, raio/emendas/comprimento das
barras); RA + resumo; informações p/ produção (fck, fcj, volume, peso, quantidade); notas de cobrimento por face e
de tolerâncias (NBR 9062 5.6.1.2).

## 16. O que o plugin já tem e o que falta
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
