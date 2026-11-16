# Quadro de coordenação — PC1 × PC2

Uma linha por tarefa **em aberto**. Leia no início da sessão e atualize no fim (situação, branch, data, quem).
O que ainda serve de cada tarefa (pendências, achados, como testar) fica em `tarefas/<#>.md`; tarefa feita sai do
quadro (o Git guarda o histórico). Regras em `docs/04_arquitetura/trabalho_em_paralelo.md`.

Situação: a fazer · em andamento · pausado · aguardando usuário · pronto p/ revisão · pronto p/ merge

| # | Tarefa | Situação | Branch | Atualizado em | Por |
|---|---|---|---|---|---|
| O-1 | Organização: limpeza pública e leitura barata (etapas 1 e 2) — [tarefa](tarefas/O-1.md) | aguardando usuário | `chore/limpeza-publica`, `docs/leitura-barata` | 16/11 | PC2 |
| P1-9 | Pranchas de formas no Revit (D25) — [tarefa](tarefas/P1-9.md) | em andamento | `pc1/pranchas-formas` (na `main`) | 14/11 | PC1 |
| P1-8 | Paginar laje alveolar no Revit (D24) — [tarefa](tarefas/P1-8.md) | pronto p/ revisão | `feature/laje-alveolar-paginacao` (local) | 12/11 | PC1 |
| P1-1 | Pilar pré-moldado no Revit igual à PP127 — [tarefa](tarefas/P1-1.md) | em andamento | `pc2/pilar-premoldado` | 06/11 | PC2 |
| P1-2 | Lógica do pilar no `DEB.Core` (motor de lineares + perfil JSON), depois do P1-1 | a fazer | `pc1/motor-lineares` | 05/11 | PC1 |
| P2-10 | Motor de detalhamento: contratos + bloco como piloto — [tarefa](tarefas/P2-10.md) | em andamento | `pc2/motor-detalhamento` | 08/11 | PC2 |
| P2-9 | Template de blocos sobre estacas — [tarefa](tarefas/P2-9.md) | pronto p/ revisão | `pc2/template-bloco-estacas` | 06/11 | PC2 |
| P2-8 | Template de viga pré-moldada — [tarefa](tarefas/P2-8.md) | em andamento (aguardando o padrão) | `pc2/viga-premoldada` | 06/11 | PC2 |
| P2-7 | Template de reservatório com reconhecimento — [tarefa](tarefas/P2-7.md) | pausado (decisão do usuário) | `pc2/template-reservatorio` | 05/11 | PC2 |
| P2-6 | Testar o DEB.Revit no Revit do PC2 com o AutoRun — [tarefa](tarefas/P2-6.md) | em andamento | — | 05/11 | PC2 |
| P2-5 | Matriz de detalhamento: Tekla, CYPE, Eberick e IDEA nos 15 elementos da D19 (`01_catalogo/matriz_detalhamento.md`) | a fazer | `pc2/matriz-detalhamento` | 05/11 | PC2 |
| P2-4 | IDEA StatiCa conferido no programa: Detail (consolos, dentes Gerber), normas, IOM (`01_catalogo/programas.md`) | a fazer | `pc2/estudo-idea` | 05/11 | PC2 |
| P2-3 | Tekla conferido no programa — [tarefa](tarefas/P2-3.md) | em andamento | `pc2/estudo-tekla` | 05/11 | PC2 |
| P2-2 | Inventário do PC2 e raio-X dos programas de lá — [tarefa](tarefas/P2-2.md) | pronto p/ merge | `pc2/inventario` | 07/11 | PC2 |
| P2-1 | Preparar o PC2 (Git, gh, .NET 8, Claude Code, clone); faltava o `gh` em 05/11 | em andamento | — | 05/11 | PC2 |

## Pendências sem tarefa
- GN Corbel: avaliar depois (decisão do usuário, 05/11).
- Pasta privada no Drive para os arquivos do escritório: ainda não é necessária (decisão do usuário, 05/11).
- Consolo inclinado na lateral da viga: adiado pelo usuário (11/11); proposta: consolo local da viga com o motor do consolo do pilar.
- Consolos digitados nas faces 1/3 em modelos salvos antes da 0.1.37 trocam de lado (a face 1 passou a ser oeste).
