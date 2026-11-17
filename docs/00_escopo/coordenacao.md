# Quadro de coordenação

Uma linha por tarefa **em aberto**. Leia no início da sessão e atualize no fim (situação, branch, data).
O que ainda serve de cada tarefa (pendências, achados, como testar) fica em `tarefas/<#>.md`; tarefa feita sai do
quadro (o Git guarda o histórico). Regras no `AGENTS.md` da raiz.

Situação: a fazer · em andamento · pausado · aguardando usuário · pronto p/ revisão · pronto p/ merge

| # | Tarefa | Situação | Branch | Atualizado em |
|---|---|---|---|---|
| O-2 | Organização do núcleo: etapas 3 a 6 (D26–D30) — [tarefa](tarefas/O-2.md). Feitas e na `main`: etapa 3 (pilar do Revit pelo núcleo, D30) e etapa 4 (`Templates/` dividido em `Elementos/`, `Documentacao/`, `Motor/`). Etapa 5 (seção por família, D28) feita na branch `feat/secao-por-familia`, sobre `ref/consolo-separado` (nenhuma das duas na `main`). Falta a etapa 6 | pronto p/ revisão | `feat/secao-por-familia` | 16/11 |
| P1-9 | Pranchas de formas no Revit (D25) — [tarefa](tarefas/P1-9.md) | em andamento | — | 14/11 |
| P1-8 | Paginar laje alveolar no Revit (D24) — [tarefa](tarefas/P1-8.md) | pronto p/ revisão | — | 12/11 |
| P1-1 | Pilar pré-moldado no Revit igual à PP127 — [tarefa](tarefas/P1-1.md) | em andamento | — | 06/11 |
| P1-2 | Lógica do pilar no `DEB.Core` (motor de lineares + perfil JSON), depois do P1-1 | a fazer | — | 05/11 |
| P2-10 | Motor de detalhamento: contratos + bloco como piloto — [tarefa](tarefas/P2-10.md) | em andamento | — | 08/11 |
| P2-9 | Template de blocos sobre estacas — [tarefa](tarefas/P2-9.md) | pronto p/ revisão | — | 06/11 |
| P2-8 | Template de viga pré-moldada — [tarefa](tarefas/P2-8.md) | em andamento (aguardando o padrão) | — | 06/11 |
| P2-7 | Template de reservatório com reconhecimento — [tarefa](tarefas/P2-7.md) | pausado (decisão do usuário) | — | 05/11 |
| P2-6 | Testar o DEB.Revit no Revit do PC2 com o AutoRun — [tarefa](tarefas/P2-6.md) | em andamento | — | 05/11 |
| P2-5 | Matriz de detalhamento: Tekla, CYPE, Eberick e IDEA nos 15 elementos da D19 (`01_catalogo/matriz_detalhamento.md`) | a fazer | — | 05/11 |
| P2-4 | IDEA StatiCa conferido no programa: Detail (consolos, dentes Gerber), normas, IOM (`01_catalogo/programas.md`) | a fazer | — | 05/11 |
| P2-3 | Tekla conferido no programa — [tarefa](tarefas/P2-3.md) | em andamento | — | 05/11 |
| P2-2 | Inventário do PC2 e raio-X dos programas de lá — [tarefa](tarefas/P2-2.md) | pronto p/ merge | — | 07/11 |

## Pendências sem tarefa
- GN Corbel: avaliar depois (decisão do usuário, 05/11).
- Pasta privada no Drive para os arquivos do escritório: ainda não é necessária (decisão do usuário, 05/11).
- Consolo inclinado na lateral da viga: adiado pelo usuário (11/11); proposta: consolo local da viga com o motor do consolo do pilar.
- Consolos digitados nas faces 1/3 em modelos salvos antes da 0.1.37 trocam de lado (a face 1 passou a ser oeste).
