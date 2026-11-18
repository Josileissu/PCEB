# Famílias: apoio no consolo e furo dos pinos na viga (D36)

Pedido do usuário (18/11/2026): a família do consolo ganha o elemento **apoio** (neoprene, argamassa ou direto) e a família da
viga pré-moldada ganha o **furo dos pinos**. Serve às verificações do consolo (C1, C3–C5) e do dente (D8), à prancha e, na fase
de cálculo, ao Hd (NBR 9062 7.3.9). Parâmetros novos com o prefixo `PCEB_` (D38).

Família de origem (do usuário, no Drive — **nunca editar o original**): `S:\Meu Drive\Arquivos\Familias Revit\Estrutura\Concreto\Viga Pré-Moldada.rfa`
(consolo "Consolo" aninhado e compartilhado). Feita no PC2 em 18/11/2026 pela tarefa `familiaspceb` (abaixo), sobre uma cópia;
resultado `desenvolvedor\familias_PCEB\Viga Pré-Moldada - PCEB.rfa` (fora do Git), **aguardando o aceite do usuário** para
substituir a do Drive. As variantes "Viga Pré-Moldada - …" da mesma pasta ainda não foram feitas.

## Decisões do usuário na construção (18/11/2026)
- **Tipo do apoio por número** (fórmula do Revit não compara texto): `PCEB_ApoioTipo` = 1 neoprene, 2 argamassa, 3 direto; o texto
  `PCEB_Apoio` sai por fórmula.
- **Um parâmetro de espessura, na viga**: os `PCEB_Apoio*` são da viga (tipo) e passam ao consolo como `b`, `c`, `H`; o
  `e_neoprene` (folga dente–consolo que já existia) passou a ser `= PCEB_ApoioEspessura` — o apoio preenche essa folga.
- **Furo no meio do dente**: `PCEB_FuroDistE/D` = 125 mm (dx_gerber/2), editáveis; 2 furos simétricos ao eixo.

## Viga (parâmetros de tipo)

| Parâmetro | Valor / fórmula | Uso |
|---|---|---|
| `PCEB_ApoioTipo` | inteiro, 1 | 1 neoprene, 2 argamassa, 3 direto |
| `PCEB_Apoio` | `if(PCEB_ApoioTipo = 1, "Neoprene", if(PCEB_ApoioTipo = 2, "Argamassa", "Direto"))` | texto para tabela e prancha |
| `PCEB_ApoioEspessura` | `if(PCEB_ApoioTipo = 1, 15 mm, if(PCEB_ApoioTipo = 2, 20 mm, 0 mm))` | folga; `e_neoprene = PCEB_ApoioEspessura` (15 mm era o valor da família e da nota da prancha) |
| `PCEB_ApoioComprimento` | 150 mm | ao longo da viga (a, a2) |
| `PCEB_ApoioLargura` | 150 mm | transversal, centrado (C4) |
| `PCEB_ApoioAfastamento` | 25 mm | ponta do consolo → face externa do apoio (a2, C3) |
| `PCEB_ApoioVisivel` | `not(PCEB_ApoioTipo = 3)` | direto = sem sólido |
| `PCEB_ApoioMaterial` | "PCEB - Neoprene" | **escolher à mão** ("PCEB - Argamassa de assentamento" também existe): fórmula não escolhe material |
| `PCEB_FuroDiametro` | 32 mm | largura do furo |
| `PCEB_FuroQtd` | `if(b < 300 mm, 1, 2)` (D23) | por ser fórmula, não é editável no tipo |
| `PCEB_FuroDistE` / `PCEB_FuroDistD` | 125 mm | ponta → centro do furo |
| `PCEB_FuroEntreFuros` | `b / 2` | 2 furos |
| `PCEB_FuroUnico` / `PCEB_FuroDuplo` / `PCEB_FuroMeiaDistancia` | fórmulas auxiliares | ligam/desligam os vazios |

Fatores de Hd por tipo (fase de cálculo, NBR 9062 7.3.9): neoprene 0,16·Fd; argamassa 0,5·Fd; direto (junta a seco) 0,8·Fd.
O pino continua só no detalhamento (a família não leva o pino).

## Consolo (parâmetros de instância, ligados aos da viga)
Sólido sobre a face superior: largura centrada (planos "PCEB apoio esquerda/direita"), faces externa/interna presas a planos
cotados a partir da ponta (`PCEB_ApoioAfastamento`, `PCEB_ApoioComprimento`), altura `PCEB_ApoioEspessuraDoSolido`
(= espessura, mínimo 1 mm para o "direto", que fica invisível), material e visibilidade pelos parâmetros.

## Furos
Vazios verticais (altura `h`, do plano "Bottom"), por ponta: 1 no eixo (`PCEB_FuroUnico`) e 1 com o par simétrico
(`PCEB_FuroDuplo`), pelo "Cortar geometria" do vazio (o mesmo parâmetro que o gerber usa). **Seção de polígono de 16 lados**
(largura entre faces = `PCEB_FuroDiametro`): a API não prende o centro de um círculo numa família (ver armadilhas no `AGENTS.md`
do DEB.Revit). Planos "PCEB furo E/D" (cotados da ponta), "PCEB furos linha 1/2" (simétricos, `PCEB_FuroEntreFuros`) e as faces ±X/±Y
de cada furo, a ±diâmetro/2.

## Conferência (18/11/2026, Revit 2026, também com a família gravada e reaberta)
- Consolo: 4 combinações de c, largura, comprimento, afastamento e espessura — sólido exatamente onde devia.
- Viga: b 200/400/300 × tipo 1/2/3, mais DistE 100, DistD 150, diâmetro 40 e h 600 — espessura, folga, apoio oculto no direto,
  1 ou 2 furos e posições/diâmetros conferem.

A exportação para o CAD (D22, `Intercambio`) ainda precisa ler estes valores (`FurosE/FurosD`/`FuroDistE/FuroDistD` do template da viga).
