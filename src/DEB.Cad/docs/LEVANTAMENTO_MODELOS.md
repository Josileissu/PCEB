# Levantamento dos modelos do escritório (2026-11-07)

Fonte: `info_auxiliar/templates/` (fora do Git), 14 folhas em PDF+DWG, lidas pelo PDF. Objetivo: o que cada elemento
informa e COMO a folha apresenta. Folhas lidas em detalhe: bloco 1 estaca (6129), B4, B3.1 (6009), B28, pilar PP70 (6034),
vigas VT-1 (1071), VLV (1078), V129/V130 (6208), V149… (702). Vistas só por amostragem: B15, B31, 6219, 6238, 710 (mesmo
padrão das anteriores).

## 1. Padrão comum de apresentação (todas as folhas)

- **Carimbos**: três famílias — BPM/Marquezin (obra Abimar–Içara, folhas 104/115/128/131, 1071, 1078: só PT), Schreiber/Diase/TCA
  (Dairy Factory, 316-CON-DD-…: PT/EN) e Goodman/MQZ (340-GRU-EST-…: PT). Tabela de revisões acima do carimbo.
- **Título do elemento** no alto à esquerda, grande: nome + quantidade + dados-chave. Ex.: "BLOCO B4 - PILAR 40x60 2x / P1=P67 /
  3 ø 50 / 1:20"; "BLOCO B3.1 (x13)" + lista dos pilares + "4xø60"; "BLOCO BPile (x24)"; "VT-1 (VT 40x100)"; "V129 (40 x 50)".
- **Cada vista** com título em maiúsculas e "ESC 1:xx" abaixo (bilíngue nas folhas Schreiber: "ARMADURA EM PLANTA - BASE DO
  BLOCO (REINFORCEMENT LAYOUT - PILE CAP BASE)").
- **Escalas típicas**: blocos 1:20/1:25; pilar: vistas 1:50, seções/cortes/consolos 1:25, espaçamento de barras 1:5; vigas:
  fôrmas/elevação 1:25 ou 1:50, seções 1:15 ou 1:25; detalhes típicos sem escala.
- **Cores**: concreto/forma em preto (cortes com hachura de concreto), armadura em vermelho, 2ª camada/secundária em azul,
  peças do cálice e consolos em magenta, algumas formas em verde, cotas e linhas de chamada em ciano, estacas em amarelo na
  planta de forma (BPM).
- **Indicação das barras**: "N" com várias linhas de chamada do texto até as barras (ciano); faixas de distribuição cotadas
  ("N7 ø8.0 c/12", "3x11 N1 c/20"); em elevação, cadeia de espaçamentos ("47 N2 c/15", "11 N3 c/10").
- **Forma de cada barra** desenhada perto da vista, em vermelho, medidas EXTERNAS em cada trecho, e embaixo
  "6 N2 ø8.0 C=300" (repetições: "2x5 N20 ø16.0 C=VAR"). Estribo com o símbolo do gancho no canto superior esquerdo.
- **Barras variáveis**: "C=VAR" na forma; os comprimentos ficam escritos ao lado de cada barra na planta; quadro "INDICAÇÃO
  BARRA VARIÁVEL — 'L' trecho reto variável indicado nas vistas em planta" (BPM).
- **Relação do aço**: AÇO | N | DIAM (mm) | QUANT | C.UNIT (cm) | C.TOTAL (cm) (VAR permitido; Schreiber com coluna ELEMENTO;
  Goodman "Relação do aço — UNITÁRIA"). **Resumo do aço**: AÇO | DIAM | C.TOTAL (m) | PESO (kg) + PESO TOTAL.
- **Informações para produção**: fck (pré-moldado: na desforma/içamento, na montagem, aos 28 dias), volume de concreto
  (m³/un. e total), área de forma (m²/un. e total), peso, taxa (kg/m³; protendida: passiva e ativa), quantidade (un.).
- **Notas**: dimensões em cm; ver prancha de informações gerais; neoprene 15 mm (Schreiber); BPM lista propriedades do
  concreto, classe de agressividade, cobrimentos (elemento em contato com o solo 4,0; interno do cálice 2,0) e normas.
- **Níveis** nos cortes: marcas de cota com texto ("P1=-3,43 m (TOPO DO BLOCO)", "FUNDO DO PILAR", "COTA DE ARRASAMENTO"),
  concreto magro e=5 cm e regularização de fundo e=5 cm desenhados.
- **Detalhes típicos que se repetem** (desenhos fixos do escritório, não gerados): raio de curvatura (tabela NBR 6118), emendas
  por traspasse e por solda, comprimento das barras (A1/B1), DETALHE 01 chaves de cisalhamento, ancoragem das armaduras das
  estacas (1:25), quadro ATENÇÃO SPDA (PT/EN), aterramento, detalhe da solda do tirante, alça de içamento (cordoalha
  2ø12,7), aparelhos de apoio, carimbo "IN LOCO".

## 2. Por elemento — informações

### Bloco de 1 estaca (BPile, 6129)
- Planta e Corte A-A 1:25, cotas gerais (80×80, h=70, embutimento da estaca 5), estaca tracejada na planta.
- Armadura só de estribos fechados ø8: N1 (5×, 69×64, C=278), N2 (6×, 72×72, C=300), N3 (5×, 69×62, C=274) — horizontais e
  verticais nas duas direções; chamadas N1/N2/N3 na planta e no corte.
- Relação, resumo, informações para produção (fck 30, 0,448 m³/un., forma 2,24 m²/un., 24 un.).
- (O exemplo do usuário de 2026-11-07 — 50×50×50, estaca ø30, arranque 14×30 — segue o mesmo esquema: 3 N1 horizontais
  44×44 C=189 e 2×4 N2 verticais 44×35 C=172, mais as barras de arranque do pilar.)

### Blocos sobre estacas (B4, B28, B3.1; BPM e Schreiber)
- Forma: planta com cotas em cadeia, estacas, pilar e cálice; cortes A-A/B-B de forma com níveis.
- Armadura em planta, separada por camada: inferior principal (faixas sobre estacas), inferior secundária (malha, VAR no
  triangular), superior; topo do bloco com o cálice.
- Cortes A-A, B-B (e C-C) de armadura; cálice em planta (horizontais + grampos) com as formas ao lado; armadura de suspensão;
  malha sob a base do cálice; armadura de base sobre estacas.
- Formas das barras perto das vistas (U, estribos, grampos), com C= e VAR.
- Detalhes: ancoragem das armaduras das estacas, chaves de cisalhamento, raio/emendas/comprimento, SPDA e aterramento (B3.1).

### Pilar pré-moldado (PP70, 6034)
- Vistas 1–4 em elevação 1:50 com níveis (topo, piso, térreo, base), consolos, furos, insertos e alças marcados.
- Longitudinais em elevação ao lado das vistas ("4 N24 ø20 C=994", "16 N2 c/10"); seções 1-1/2-2 de armadura 1:25 com as formas
  dos estribos e grampos; cortes A-A…D-D nos consolos.
- Armaduras dos consolos por tipo (planta, corte, tirante, estribos horizontais e verticais, pino), reforço nos furos
  (grampos por furo), espaçamento das barras 1:5, alça de içamento, detalhe genérico de aterramento.
- Relação, resumo e produção com fck de desforma/montagem/28 dias.

### Vigas pré-moldadas (VT-1, VLV, V129/V130, V149…)
- Fôrmas em planta e em elevação (furos, alças, pinos, dentes Gerber), seções A/B/C de forma 1:15–1:25.
- Armaduras em elevação: cada grupo desenhado separado, empilhado abaixo da viga, com o nome da função ("ARMADURA SUPERIOR",
  "PELE", "ABA LAJE", "ARMADURA INFERIOR"), e a distribuição dos estribos cotada em cadeia ao longo da viga.
- Seções de armadura com as formas dos estribos; dente/consolo Gerber (costura, tirante, grampo, suspensão).
- Protendidas: cordoalhas listadas por posição ("Cordoalhas 1,2,3… 12,7 mm CP 190 RB, força 14,4 tf/cabo"), isolamento
  (mangueira) nas pontas, quadro "NOMENCLATURA E POSICIONAMENTO DAS CORDOALHAS".
- Detalhes: solda do tirante, alça de içamento, pino para calha, aparelhos de apoio.

## 3. O que isso diz sobre o gerador (para conversar)
- A folha é sempre: título do elemento → vistas de forma → vistas de armadura (uma por camada) → formas das barras ao lado →
  tabelas (relação, resumo, produção) → notas → detalhes típicos fixos → carimbo.
- Os detalhes típicos são desenhos FIXOS do escritório: melhor inserir como bloco do DWG padrão do que desenhar por código
  (o detalhe gerado no pilar ficou feio e foi removido em 2026-11-07).
- Bilíngue depende do cliente (Schreiber sim; BPM/Goodman não).
