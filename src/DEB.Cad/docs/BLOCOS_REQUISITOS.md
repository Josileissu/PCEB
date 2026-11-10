# Blocos sobre estacas — levantamento dos exemplos do escritório (PC2, 2026-11-06)

Exemplos do usuário em `info_auxiliar/templates/blocos/` (PDF + DWG; fora do Git) e o BLOCO B3 enviado como imagem.
Medidas lidas dos DWGs (convertidos com ARMTESTEDXF numa instância nova do AutoCAD e lidos por um leitor de DXF próprio,
sem ezdxf). O que está marcado **[CONFIRMAR]** é interpretação minha.

## 1. Exemplos
| Bloco | Estacas | Planta (cm) | Ligação do pilar | Pilar | Peças | Cliente / folha |
|---|---|---|---|---|---|---|
| B3 (imagem) | 2 × ø25 a 75 | 110 × 50 × 45 | **arranque** (moldado in loco) | 14 × 26 | 19 | — (já no template v1) |
| B28 | 2 × ø40 a 120 | 190 × 70 | **cálice com colarinho** 80 × 70 | 20 × 30 | 6 | BPM, folha 128 |
| B4 | 3 × ø50 (triângulo, lado 150) | hexágono 240 × 226 | cálice com colarinho 118 × 98 | 40 × 60 | 2 | BPM, folha 104 |
| B15 | 3 × ø50 (triângulo, lado 150) | hexágono 210 × 242,4 | cálice com colarinho 98 × 98 | 40 × 40 | 24 | BPM, folha 115 |
| B31 | 4 × ø50 a 150 | 230 × 230 | cálice com colarinho 128 × 128 | 60 × 60 | 2 | BPM, folha 131 |
| B3.1 | 4 × ø60 | 220 × 220 | cálice (malha sob a base, suspensão) | — | 13 | Schreiber/DIASE, PT/EN |

## 2. Geometria (regras medidas)
- **Distância do eixo da estaca à face** (e): B28 35, B4 45, B15 40, B31 40 (B3: 17,5 — bloco pequeno).
- **Bloco triangular (3 estacas)**: estacas num triângulo equilátero; o bloco é um hexágono com as 6 faces a `e` dos eixos:
  3 faces paralelas aos lados do triângulo e 3 faces nos cantos, perpendiculares às bissetrizes. B15: estacas (40; 121,2),
  (170; 46,2), (170; 196,2), faces a 40 → 210 × 242,4, lados de canto 46,2. Pilar no baricentro (126,7 / 83,3 da face).
- **Altura** = base + colarinho: B28 60 + 40, B4 95 + 50, B15 105 + 30, B31 110 + 50 (total = arrasamento → topo + 5).
- **Colarinho**: paredes t (B28 21, B4/B15 25, B31 30) + folga 4 + pilar + folga 4 + t; mesma largura do bloco quando ele é
  estreito (B28: 70).
- **Embutimento** (topo do colarinho → base do pilar): B28 50, B4 95, B15 65, B31 95. Abaixo do pilar: regularização
  de fundo e = 5 (dentro do cálice) + o resto da base. Concreto magro e = 5 sob o bloco.
- **Níveis** no corte de forma, por pilar quando os níveis diferem: TOPO DO BLOCO, FUNDO (BASE) DO PILAR, COTA DE
  ARRASAMENTO ("P1=-3,43 m / P67=-11,63 m").
- Cobrimentos (notas): 4,0 cm em contato com o solo; 2,0 cm interno do cálice. fck 30, CAA III.
- NBR 9062 7.7 (conferida no índice local): Lemb mínimo pela Tabela 15 (1,5h a 2,0h; chaves 1,2h a 1,6h) e ≥ 40 cm;
  parede do colarinho ≥ 15 cm, fundo sob o pilar ≥ 20 cm (Fig. 25); chaves ≥ 1 cm a cada ≤ 10 cm; pressões
  horizontais concentradas no topo (Lemb/3, 7.7.4.3); armadura de suspensão para a punção (7.7.3.7).

## 3. Apresentação (folhas BPM)
1. **Forma** (quadro à esquerda): "BLOCO B15 - 24x - 3 ø 50", lista dos pilares, PLANTA 1:20 com cadeias de cotas (faces,
   estacas, colarinho, pilar), estacas com quadrantes hachurados, marca A; CORTE A-A com pilar interrompido, colarinho,
   chaves no cálice, REGULARIZAÇÃO DE FUNDO e=5 cm, CONCRETO MAGRO e=5 cm e os níveis.
2. **Armadura**: título + pilares; "VISTA SUPERIOR — ARMADURA INFERIOR PRINCIPAL", "… ARMADURA INFERIOR SECUNDÁRIA"
   (B28/B31: "PLANTA - VISTA INFERIOR" e "PLANTA - VISTA SUPERIOR"), "… ARMADURA SUPERIOR", CÁLICE, CORTE A-A, B-B (C-C),
   "ARM. LATERAL HORIZONTAL" (triangular), barras dobradas ao lado das vistas.
3. Relação do aço + resumo; "Informações p/ produção" (volume, peso de aço, taxa kg/m³, área de formas, quantidade).
4. Detalhes padrão (os mesmos da prancha do pilar — reaproveitar `DetalheTipico`): raio de curvatura, emendas por
   traspasse e por solda, comprimento das barras, DETALHE 01 CHAVES DE CISALHAMENTO, ANCORAGEM DAS ARMADURAS DAS ESTACAS,
   INDICAÇÃO BARRA VARIÁVEL ("L" = trecho reto variável, indicado nas vistas), notas.

## 4. Armaduras (por função)
Medidas do B28 (cálice 80 × 70, c = 4) e conferidas nos outros:
| Função | B28 | Outros | Regra |
|---|---|---|---|
| Principal inferior (faixa sobre as estacas) | 5 ø16 U 45/179/44 | B31 4×7 ø20 c/14 68/219/68; B15/B4 faixas nos 3 lados do triângulo | reto = L − 2c − …; pernas = topo da base − c (decidido) |
| Secundária inferior (malha) | 4 ø10 20/179/20, 4 ø10 30/179/29 | B31 24 ø10 c/10 25/219/25 nas 2 direções; triangular: barras VAR | triangular = grupo VAR com "L" |
| Superior | 3 ø8 c/12 20/67/20 (ao lado do colarinho) | B31 ø12.5 c/12 141/19/147/78/147/19/141 **[CONFIRMAR forma]** | |
| Lateral (pele) | 4 ø8 c/15 (N1/N2) | B31 2×34 ø8 c/14 20/100/20; triangular: polígono fechado em volta | |
| Estribos verticais da base | 12 ø8 c/12 59 × 49; 6 ø8 c/12 59 × 36 | | fechados |
| Cálice: horizontais fechados — **topo concentrado** | 3 ø12.5 c/5.5 72 × 62 | B31 5 ø20 c/7.5; B4 5 ø12.5 c/5; B15 5 ø16 c/4 | colarinho − 2c (72 × 62, 120 × 120) |
| Cálice: horizontais fechados — restante | 3 ø10 c/12 | B31 6 ø12.5 c/12; B4 6 ø10 c/13 | idem |
| Cálice: grampos U por parede (2× por direção) | 2×3 ø12.5 c/5.5 15/62/15 e 15/72/15 (topo); 2×3 ø10 c/12 (resto) | B31 4×5 ø20 22/120/22; 4×6 ø12.5 22/120/22 | perna = t − 2c (21 − 6 = 15) |
| Cálice: verticais (grampo na parede) | 8 ø10 12/85/11/87/15 | B31 4×3 ø12.5 28/145/19/143/28 | desce até a base, gancho no fundo |
| Fundo do cálice: suspensão | 4 ø10 c/12 81/11/87/46/87/11/81 | B3.1 "armadura de suspensão" | NBR 9062 7.7.3.7 |
| Fundo do cálice: retas / malha sob a base | 7 ø10 C=100 | B3.1 "malha sob base do cálice" 2×2 ø20 | |
| Ancoragem das armaduras das estacas | detalhe padrão (nota) | | não entra na RA |

## 5. Proposta de etapas
1. **Cálice com colarinho em bloco retangular** (B28 2 estacas, B31 4 estacas): forma no padrão BPM, armaduras da tabela,
   níveis, informações p/ produção, detalhes padrão. Ajusta o template atual (o arranque continua como opção).
2. **Bloco triangular de 3 estacas** (B4, B15): hexágono, faixas principais nos 3 lados, malha VAR com "L", lateral
   em polígono fechado.
3. **Cálice sem colarinho / malha sob a base / suspensão** (B3.1) e texto PT/EN (como o pilar).
4. Depois: 5 estacas, blocos sobre estacas em linha com cálice, bloco de 1 estaca com cálice.

## 6. Decisões do usuário (2026-11-06)
- Apresentação: **padrão BPM para todos** (inclusive o bloco com arranque).
- Ordem: **todos os tipos de uma vez** (feito: grade, triangular, colarinho, embutido, arranque).
- Pernas da principal: **regra pela altura do bloco** (topo da base − cobrimento). Diferenças como o B31 (68 com base 110)
  são ajustadas depois, na produção/edição do bloco (2026-11-07).
- Principal no triangular: **barras retas de face a face com pernas** (aprovado em 2026-11-07).
- Pernas da secundária e da superior: **valor padrão do template** (20), não vem do cálculo; suspensão com perna
  externa 6 menor que a interna (aprovado em 2026-11-07).
- 5 estacas e arranjos fora de grade: **depois** (não são comuns).

## 7. Perguntas ainda abertas
- Quantidades e bitolas do cálice (concentradas no topo e restante) são sempre digitadas (vêm do cálculo)?
- O B3.1 (Schreiber/DIASE, bilíngue) é outro padrão de cliente ou o mesmo escritório?
