# Quadro de coordenação

Uma linha por tarefa **em aberto**. Leia no início da sessão e atualize no fim (situação, branch, data).
O que ainda serve de cada tarefa (pendências, achados, como testar) fica em `tarefas/<#>.md`; tarefa feita sai do
quadro (o Git guarda o histórico). Regras no `AGENTS.md` da raiz.

Situação: a fazer · em andamento · pausado · aguardando usuário · pronto p/ revisão · pronto p/ merge

| # | Tarefa | Situação | Branch | Atualizado em |
|---|---|---|---|---|
| P1-14 | Análise estrutural (D44–D46): consolos com as cargas das vigas; configuração do projeto (D45, tela WebView2); suíte de análise por objetos (solver próprio, laje em grelha, dimensionamento por peça, janela 3D/2D com isovalores) lida do Revit — [tarefa](tarefas/P1-14.md) | pronto p/ revisão (testado no Revit 2025: Teste_PM com e sem laje, modelo real moldado no local); falta ver as janelas com o usuário | `feat/pilar-calculo` | 24/11 |
| P1-16 | Estruturas de aço (D48): perfis I/H, barras pela NBR 8800:2024, peças de aço na análise lidas do Revit, ligação por chapa de alma como objeto | pronto p/ revisão (testado no Revit 2025 com pórtico de teste SD_ACO=1); próximos: chapa de topo, placa de base, Steel Connections do Revit | `feat/estrutura-metalica` (de `feat/pilar-calculo`) | 24/11 |
| O-2 | Organização do núcleo: etapas 3 a 6 (D26–D32) — [tarefa](tarefas/O-2.md). Etapas 3 a 6 feitas; arranjo das barras nas seções não retangulares feito no núcleo (D31, branch `feat/arranjo-por-familia`; regra provisória do cobrimento interno a confirmar); armadura por linha como forma única das longitudinais do pilar no CAD e no Revit (D32, branch `feat/armadura-por-linha`); falta decidir a fonte das DLLs de referência dos CADs | aguardando usuário | — | 17/11 |
| P1-12 | Janelas de prévia no padrão visual do PCEB (D41): kit `UI/Estilo`, vigas (abas, Md decalado × MRd, Vd × VRd3), pilares, Conferir peças, laje alveolar e caixas de ferramenta | pronto p/ revisão (imagens pelo autoteste: `vigas`, `precast`, `conferir`, `janelas`) | `feat/janelas-previa-pceb` (sobre a `feat/viga-revit-armacao-calculo`) | 23/11 |
| P1-11 | Vigas pré-moldadas no Revit com cálculo (D40): cargas digitadas, cálculo de concreto armado, proposta da armadura, barras, montagem, vistas, tabelas e relatório — [tarefa](tarefas/P1-11.md) | pronto p/ revisão (testado no Revit 2025 com o modelo de teste; a janela WPF falta ver com o usuário) | `feat/viga-revit-armacao-calculo` | 22/11 |
| P1-10 | Verificações normativas do detalhamento, sem cálculo (D36–D38): fichas, Etapa 1 (pilar e cobrimento), suspensão do dente, parâmetros PCEB_; famílias (apoio e furo) feitas no PC2 — [tarefa](tarefas/P1-10.md) | exportação e relatórios testados no Revit 2025 e no GstarCAD (18/11) e levados à `main`; na branch `feat/verificacoes-normativas` (18/11, aguardando a conferência do usuário para ir à `main`): regras de viga, dente e consolo (C12, C14, C16, C17), tela do aceite (`ARMACEITES`), quadro-resumo (`ARMQUADROVERIF`) e campos + painel na janela do Revit, testados no GstarCAD 2021 e no Revit 2025; 3 variantes de família com furo à mão | `feat/verificacoes-normativas` | 18/11 |
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
