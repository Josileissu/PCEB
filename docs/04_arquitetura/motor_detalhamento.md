# Motor de detalhamento (decisão D19, 05/11/2026)

**Prioridade:** o motor de detalhamento vem **antes** das análises estruturais. Meta: detalhar **uma obra inteira**
primeiro; análise e dimensionamento entram depois, alimentando o mesmo motor.

## Abordagem
**Rotinas paramétricas configuradas por templates de dados** — não templates de desenho fixos.
- Rotina (código): lê geometria do elemento + regras → gera armadura e prancha.
- Template de dados (JSON por tipo de elemento): bitolas permitidas, cobrimento, espaçamentos, estribo padrão,
  vistas, cortes, escalas, layout da prancha (ex.: PP127). Padrões do escritório ficam fora do Git (D15).
- Template de desenho: só o fixo (carimbo, moldura, notas gerais, legendas).

## Camadas
1. **Núcleo comum** (`DEB.Core`): Barra, Estribo, Malha, Posição (N1..Nn), ancoragem/emenda/cobrimento (NBR 6118),
   formas de dobra, quadro de aço, cotagem, numeração, similares (iguais ou girados).
2. **Motores** — geram um **modelo de armadura neutro** (sem Revit/CAD).
3. **Perfis JSON** por tipo de elemento.
4. **Saídas**: DEB.Revit (Rebar/Assembly/Sheets) e DEB.Cad.

## Motores
| Motor | Elementos | Lógica comum |
|---|---|---|
| 1. Lineares | pilares pré-moldados e moldados in loco, vigas pré-moldadas e moldadas in loco, baldrames | seção, longitudinais, estribos, arranques/esperas |
| 2. Superfícies | lajes maciças, paredes de concreto, tilt-up, reservatórios | malhas 1/2 camadas, reforços de aberturas, bordas e cantos |
| 3. Fundações | blocos, sapatas | bielas e tirantes / flexão, armadura de fundo, arranques (reusa o motor 1) |
| 4. Ligações | consolos de concreto, consolos metálicos | tirante + costura (NBR 9062); chapas, studs, solda |
| 5. Lajes alveolares | paginação, folha de capeamento | layout de painéis; tela + negativos sobre apoios |

Camada transversal de pré-moldado: **insertos, içamento, chumbadores** (pilares/vigas PM e tilt-up).
Reservatório = composição do motor 2 (paredes + fundo + tampa) + regras de estanqueidade.

## Ordem sugerida
1. Pilar pré-moldado (referência PP127) — constrói a maior parte do núcleo
2. Consolo de concreto
3. Viga pré-moldada → pilar/viga moldados in loco e baldrame
4. Blocos e sapatas
5. Lajes maciças → paredes → tilt-up → reservatórios (aproveitar o template base de reservatório do DEB.Cad)
6. Paginação de alveolares + capeamento
7. Consolo metálico
