# Matriz de detalhamento — 15 elementos da D19 × programas

Etapa C do plano de ataque (escopo, 05/11/2026). Só **detalhamento** (armadura desenhada, vistas, cortes, quadro de
aço, padrão configurável); análise e dimensionamento ficam nas fichas.

● faz (gera o detalhamento) · ○ parcial (calcula, verifica ou desenha só parte) · — não faz · ? a confirmar ·
**PC2** = coluna a preencher no outro computador. Evidências nas fichas de cada programa (✓ = conferido nos arquivos).

| # | Elemento | TQS | Eberick | CYPE 3D | Revit (nativo + Precast) | DEB.Cad | DEB.Revit | Tekla | IDEA |
|---|---|---|---|---|---|---|---|---|---|
| 1 | Blocos de fundação | ● 1–24 estacas, bielas ✓ | ● ✓ | ● `encepado` ✓ | ○ armadura manual | ○ template previsto (1 e 2 estacas, cálice) | — | PC2 | PC2 |
| 2 | Sapatas | ● `SAP` ✓ | ● ✓ | ● `elemcim` ✓ | ○ manual | — | — | PC2 | PC2 |
| 3 | Paredes de concreto | ○ `PrConcra` (a confirmar) | ● paredes/contenção ✓ | ○ painéis | ○ Precast parede maciça/dupla ✓ | — | — | PC2 | — |
| 4 | Reservatórios | ? | ● paredes e lajes de reservatório ✓ | — | ○ manual | ○ template base de reservatório ✓ | — | PC2 | — |
| 5 | Painéis tilt-up | — | — | — | ○ Precast (Split + armação de paredes) ✓ | — | — | PC2 | — |
| 6 | Pilares pré-moldados | ● PREFAB ✓ | ● ✓ | — | ○ assemblies, sem regra | ● template PP (PP127/P61) ✓ | ● levantamento, similares, armadura, prancha ✓ | PC2 | — |
| 7 | Pilares moldados in loco | ● `PIL` ✓ | ● ✓ | ○ `pilararm` ✓ | ○ manual | ○ template previsto | — | PC2 | — |
| 8 | Vigas pré-moldadas | ● PREFAB (+ protensão) ✓ | ● (barra passante, etapas) ✓ | — | ○ manual | ○ template previsto | ○ previsto (próximo) | PC2 | ○ Beam (PC2) |
| 9 | Vigas moldadas in loco / baldrame | ● `VIG` ✓ | ● (viga de fundação) ✓ | ○ `dmtbarmvig` ✓ | ○ manual | ○ template previsto | — | PC2 | — |
| 10 | Lajes maciças | ● `LAJ` ✓ | ● ✓ | — | ○ armadura por área/caminho, telas ✓ | ○ template previsto; telas ✓ | — | PC2 | — |
| 11 | Folha de capeamento | ○ "laje pré c/ capa", solidarização ✓ | ? | — | ○ Precast pré-laje treliçada ✓ | ○ telas e treliças ✓ | — | PC2 | — |
| 12 | Paginação de lajes alveolares | ○ lajes pré (a confirmar) | ○ cadastro e verificação vão × sobrecarga (sem paginação) ✓ | — | ● **Precast HollowcoreSlab** (Split + CAM) ✓ — testar | — | — | PC2 | — |
| 13 | Consolos metálicos | — | — | ○ placas e chapas de ligação ✓ | ○ SteelConnections | — | — | PC2 | ● Connection (PC2) |
| 14 | Consolos de concreto | ● PREFAB + calculadora ✓ | ● (no pilar PM) ✓ | — | — | ● no template PP ✓ | ● tirante + costura NBR 9062 ✓ | PC2 | ● Detail (PC2) |
| 15 | Içamento / insertos (transversal) | ● alças, furos, verificações ✓ | — | — | ○ Precast (contraventamento de içamento) ✓ | ○ cordoalhas ✓ | ● alça de içamento ✓ | PC2 | — |

## Leitura
- **Ninguém cobre tudo**: o TQS é o mais completo em concreto (in loco + PREFAB); o Eberick cobre bem in loco e
  reservatórios; o **Revit Precast** é o único com paginação de alveolares e painéis com CAM; consolos metálicos só em
  IDEA/ligações.
- **Lacunas de mercado** que o motor D19 preenche: painéis tilt-up detalhados pela NBR, folha de capeamento,
  consolos metálicos detalhados, e tudo isso **no Revit com o padrão do escritório**.
- **Nossos plugins hoje**: pilar pré-moldado com consolo (CAD e Revit) e base de reservatório (CAD); o resto está
  previsto. A ordem do `motor_detalhamento.md` continua válida.

## Para o PC2
Preencher as colunas **Tekla** e **IDEA** (P2-5 no `coordenacao.md`): para cada elemento, se o programa gera o
detalhamento, o que fica configurável (padrões, templates de prancha) e o que exporta (CAM, BVBS, IFC).
