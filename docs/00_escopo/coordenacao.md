# Quadro de coordenação — PC1 × PC2

Cada Claude lê este quadro no **início** da sessão e atualiza no **fim** (situação, branch, próximo passo).
Regras em `docs/04_arquitetura/trabalho_em_paralelo.md`.

Situação: a fazer · em andamento · aguardando usuário · pronto p/ merge · feito

## PC2 — estudo (programas informados pelo usuário: AutoCAD, Revit, CYPE, Eberick, SAP2000, IDEA StatiCa, Tekla, Tekla Structures)
| # | Tarefa | Situação | Branch | Entrega / observação |
|---|---|---|---|---|
| P2-1 | Preparar o PC2 (Git, gh, .NET 8, Claude Code, clone) | a fazer | — | passos em `trabalho_em_paralelo.md` |
| P2-2 | **Inventário do PC2**: todos os programas estruturais/CAD instalados, versões, caminhos, API/SDK (o usuário citou "Tekla" e "Tekla Structures": conferir o que é cada um) | a fazer | `pc2/inventario` | `docs/01_catalogo/inventario_pc2.md`, no formato do `inventario_local.md` |
| P2-3 | Ficha do **Tekla** conferida no programa (✓): detalhamento de pré-moldados, pranchas, numeração, quadros, Open API | a fazer | `pc2/estudo-tekla` | atualizar `01_catalogo/comerciais/tekla.md` |
| P2-4 | Ficha do **IDEA StatiCa** conferida (✓): módulo Detail para consolos e dentes Gerber, normas (tem NBR?), IOM | a fazer | `pc2/estudo-idea` | atualizar `01_catalogo/comerciais/idea-statica.md` |
| P2-5 | **Matriz de detalhamento**: como Tekla, CYPE, Eberick e IDEA tratam os 15 elementos da D19 | a fazer | `pc2/matriz-detalhamento` | `01_catalogo/matriz_detalhamento.md` (o PC1 preenche TQS e DEB) |
| P2-6 | Testar o **DEB.Revit** no Revit do PC2 (versão?) com o AutoRun | a fazer | — | depende dos arquivos privados no Drive |

## PC1 — código
| # | Tarefa | Situação | Branch | Entrega / observação |
|---|---|---|---|---|
| P1-1 | Pilar pré-moldado no Revit igual à PP127 | **pausado — continua no PC2** (no PC1 há outros usuários no Revit) | `pc1/teste-pilares` | ver "Passagem para o PC2" abaixo |
| P1-2 | Levar a lógica do pilar para o `DEB.Core` (motor de lineares + perfil JSON) | a fazer | `pc1/motor-lineares` | depois do P1-1 |
| P1-3 | Colunas TQS e DEB da matriz de detalhamento | a fazer | `pc1/matriz-detalhamento` | junta com o P2-5 |

## Passagem para o PC2 — pilar pré-moldado (05/11/2026)
O usuário pediu para **aguardar a ordem dele** antes de continuar; os testes no Revit passam para o PC2.

Feito no PC1 (branch `pc1/teste-pilares`):
- Tarefas de AutoRun sem projeto aberto: `familias` e `criarmodelo` (`src/DEB.Revit/src/Precast/TestModel.cs`; uso no
  `CLAUDE.md` do DEB.Revit). O modelo de teste é gerado de novo em cada computador (não vai para o Git).
- Famílias usadas (`S:\Meu Drive\Arquivos\Familias Revit\Estrutura\Concreto`): `Pilar Pré-Moldado.rfa` (tipos
  "40x40 - docas" e "50x50 - internos") e `Viga Pré-Moldada.rfa` (consolos aninhados). Template:
  `Structural Analysis-DefaultBRAPTB.rte` do Revit 2025.
- Resultado: P1 = 4 pilares de canto (giros 0/90/180/270°), P2 = 2 do meio (0/180°); armadura e prancha geradas.

Respostas do usuário:
- **Folha padrão**: `S:\Meu Drive\Arquivos\Familias Revit\Carimbo e Folhas\Folhas Revit.rfa`, com tipos/instâncias A0, A1, A2, A3…
  (a PP127 é A1).
- **Embutimento** na família "Pilar Pré-Moldado": **deslocamento negativo da base** (não há parâmetro "Embutimento").

Próximos passos (só com a ordem do usuário), comparando com a PP127:
1. Aço CA25 saindo nas barras Ø8/12,5/16 — deve ser **CA50** (erro).
2. Carimbo: usar `Folhas Revit.rfa` (A1).
3. Embutimento lido do deslocamento negativo da base; `criarmodelo` passa a criar pilares com base negativa.
4. Layout: seções e cortes em 1:25, sem sobreposição com tabelas e títulos.
5. Faltam: detalhe da armadura do consolo (planta, cortes, formas), formas das barras cotadas, níveis (topo/térreo/base),
   alça de içamento, legendas padrão (raio, emendas, comprimento). Aviso no P2: ρ = 0,39% < 0,40%.

## Aguardando o usuário
- GN Corbel: avaliar depois (decisão do usuário, 05/11/2026).
- Pasta privada no Drive: ainda não é necessária (decisão do usuário, 05/11/2026). A PP127 e o `Projeto1.rvt`, se o PC2 precisar,
  passam para lá depois.
